using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DlcvModules
{
	/// <summary>
	/// GraphExecutor：支持按 nodes[*].inputs/outputs 的 link 进行最小路由，
	/// 将多路输入聚合为主对+额外对（ExtraInputsIn），并将模块的 ExtraOutputs 与 outputs[*] 对齐。
	/// </summary>
		public class GraphExecutor
	{
		private readonly List<Dictionary<string, object>> _nodes;
		private readonly ExecutionContext _context;
		private readonly Dictionary<int, Dictionary<string, object>> _outputs = new Dictionary<int, Dictionary<string, object>>();
		private readonly Dictionary<int, NodeExecOutput> _nodeExecMap = new Dictionary<int, NodeExecOutput>();

			public GraphExecutor(List<Dictionary<string, object>> nodes, ExecutionContext context = null)
		{
			_nodes = nodes ?? new List<Dictionary<string, object>>();
			_context = context ?? new ExecutionContext();
		}

			public Dictionary<int, Dictionary<string, object>> Run()
		{
			var outputs = new Dictionary<int, Dictionary<string, object>>();

			// 按 order 排序（若缺失则回退到 id），保证与前端配置顺序一致
			var ordered = new List<Dictionary<string, object>>(_nodes);
			ordered.Sort((a, b) =>
			{
				int ao = a != null && a.TryGetValue("order", out object aov) ? SafeToInt(aov, int.MaxValue - 1) : int.MaxValue - 1;
				int bo = b != null && b.TryGetValue("order", out object bov) ? SafeToInt(bov, int.MaxValue - 1) : int.MaxValue - 1;
				if (ao != bo) return ao.CompareTo(bo);
				int aid = a != null && a.TryGetValue("id", out object aidv) ? SafeToInt(aidv, 0) : 0;
				int bid = b != null && b.TryGetValue("id", out object bidv) ? SafeToInt(bidv, 0) : 0;
				return aid.CompareTo(bid);
			});

			// 预构建 linkId -> (srcNodeId, srcOutIdx)
			var linkToSource = BuildLinkSourceMap(ordered);

			for (int i = 0; i < ordered.Count; i++)
			{
				var node = ordered[i];
				string type = node != null && node.TryGetValue("type", out object tv) ? (tv != null ? tv.ToString() : null) : null;
				int nodeId = node != null && node.TryGetValue("id", out object iv) ? SafeToInt(iv, i) : i;
				string title = node != null && node.TryGetValue("title", out object tl) ? (tl != null ? tl.ToString() : null) : null;
				var props = node != null && node.TryGetValue("properties", out object pv) ? pv as Dictionary<string, object> : new Dictionary<string, object>();

				var moduleType = ModuleRegistry.Get(type);
				if (moduleType == null) continue;
				var module = (BaseModule)Activator.CreateInstance(moduleType, nodeId, title, props, _context);

				// 聚合当前节点输入（主对 + 额外对）
				var inputPairs = CollectInputPairs(node, linkToSource);
				var mainImages = inputPairs.TryGetValue(0, out ModuleChannel mainCh) ? (mainCh.ImageList ?? new List<ModuleImage>()) : new List<ModuleImage>();
				var mainResults = inputPairs.TryGetValue(0, out ModuleChannel mainCh2) ? (mainCh2.ResultList ?? new JArray()) : new JArray();
				var extraChannels = new List<ModuleChannel>();
				foreach (var kv in inputPairs)
				{
					if (kv.Key <= 0) continue;
					extraChannels.Add(kv.Value);
				}
				module.ExtraInputsIn.Clear();
				module.ExtraInputsIn.AddRange(extraChannels);

				// 执行当前节点
				var io = module.Process(mainImages, mainResults);

				// 保存该节点的全部输出通道
				var nodeOut = new NodeExecOutput
				{
					Main = new ModuleChannel(io.ImageList, io.ResultList),
					Extra = new List<ModuleChannel>(module.ExtraOutputs ?? new List<ModuleChannel>())
				};
				_nodeExecMap[nodeId] = nodeOut;

				// 对外暴露主通道（与原行为一致）
				outputs[nodeId] = new Dictionary<string, object>
				{
					{ "image_list", new List<ModuleImage>(io.ImageList ?? new List<ModuleImage>()) },
					{ "result_list", new JArray(io.ResultList ?? new JArray()) }
				};
			}

			return outputs;
		}

		private static int SafeToInt(object v, int dv)
		{
			try { return Convert.ToInt32(v); } catch { return dv; }
		}

		private class NodeExecOutput
		{
			public ModuleChannel Main;
			public List<ModuleChannel> Extra;
		}

		private Dictionary<int, Tuple<int, int>> BuildLinkSourceMap(List<Dictionary<string, object>> nodes)
		{
			var map = new Dictionary<int, Tuple<int, int>>(); // linkId -> (srcNodeId, srcOutIdx)
			foreach (var n in nodes)
			{
				if (n == null) continue;
				int nid = n.TryGetValue("id", out object iv) ? SafeToInt(iv, -1) : -1;
				if (nid < 0) continue;
				if (!n.TryGetValue("outputs", out object ov) || !(ov is List<object> outList)) continue;
				for (int oi = 0; oi < outList.Count; oi++)
				{
					var o = outList[oi] as Dictionary<string, object>;
					if (o == null) continue;
					if (!o.TryGetValue("links", out object lv) || lv == null) continue;
					if (lv is List<object> lobj)
					{
						foreach (var lidObj in lobj)
						{
							int lid = SafeToInt(lidObj, -1);
							if (lid >= 0 && !map.ContainsKey(lid)) map[lid] = Tuple.Create(nid, oi);
						}
					}
				}
			}
			return map;
		}

		private Dictionary<int, ModuleChannel> CollectInputPairs(Dictionary<string, object> node, Dictionary<int, Tuple<int, int>> linkToSource)
		{
			var pairs = new Dictionary<int, ModuleChannel>();
			if (node == null) return pairs;
			if (!node.TryGetValue("inputs", out object iv) || !(iv is List<object> inList))
			{
				return pairs;
			}
			for (int ii = 0; ii < inList.Count; ii++)
			{
				var inp = inList[ii] as Dictionary<string, object>;
				if (inp == null) continue;
				int linkId = inp.TryGetValue("link", out object lv) ? SafeToInt(lv, -1) : -1;
				string dtype = inp.TryGetValue("type", out object tv) && tv != null ? tv.ToString() : null;
				if (linkId < 0 || !linkToSource.TryGetValue(linkId, out Tuple<int, int> src)) continue;
				int pairIdx = ii / 2;
				var ch = pairs.ContainsKey(pairIdx) ? pairs[pairIdx] : new ModuleChannel(new List<ModuleImage>(), new JArray());
				var srcNodeId = src.Item1; var srcOutIdx = src.Item2;
				if (!_nodeExecMap.TryGetValue(srcNodeId, out NodeExecOutput srcOut))
				{
					// 源尚未执行：忽略（拓扑排序一般保证源在前）
					continue;
				}
				ModuleChannel picked = null;
				int srcPairIdx = srcOutIdx / 2;
				if (srcPairIdx == 0)
				{
					picked = srcOut.Main;
				}
				else
				{
					int ei = srcPairIdx - 1;
					if (srcOut.Extra != null && ei >= 0 && ei < srcOut.Extra.Count)
					{
						picked = srcOut.Extra[ei];
					}
				}
				if (picked == null) continue;
				if (string.Equals(dtype, "image_chan", StringComparison.OrdinalIgnoreCase))
				{
					// 覆盖为来自源的整个列表
					ch = new ModuleChannel(new List<ModuleImage>(picked.ImageList ?? new List<ModuleImage>()), ch.ResultList ?? new JArray());
				}
				else if (string.Equals(dtype, "result_chan", StringComparison.OrdinalIgnoreCase))
				{
					var r = new JArray();
					if (picked.ResultList != null) foreach (var t in picked.ResultList) r.Add(t);
					ch = new ModuleChannel(ch.ImageList ?? new List<ModuleImage>(), r);
				}
				pairs[pairIdx] = ch;
			}
			return pairs;
		}
	}
}




