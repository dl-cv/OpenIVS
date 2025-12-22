using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

			// 预热注册：强制触发所有 BaseModule 子类的静态构造以完成 ModuleRegistry.Register
			try
			{
				var asm = typeof(BaseModule).Assembly;
				foreach (var t in asm.GetTypes())
				{
					if (t != null && t.IsClass && !t.IsAbstract && typeof(BaseModule).IsAssignableFrom(t))
					{
						RuntimeHelpers.RunClassConstructor(t.TypeHandle);
					}
				}
			}
			catch { }
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
				Dictionary<string, object> props = null;
				if (node != null && node.TryGetValue("properties", out object pv) && pv != null)
				{
					props = pv as Dictionary<string, object>;
					if (props == null)
					{
						var jo = pv as JObject;
						if (jo != null)
						{
							try { props = jo.ToObject<Dictionary<string, object>>(); } catch { props = null; }
						}
					}
				}
				if (props == null) props = new Dictionary<string, object>();
				
				// 统一 bbox 属性：允许前端/旧配置用 XYXY 输入，C# 侧统一补齐为 XYWH（不删除原字段）
				try { NormalizeBboxProperties(props); } catch { }

				var moduleType = ModuleRegistry.Get(type);
				if (moduleType == null) continue;
				var module = (BaseModule)Activator.CreateInstance(moduleType, nodeId, title, props, _context);

				// 聚合当前节点输入（主对 + 额外对）
				var inputPairs = CollectInputPairs(node, linkToSource);
				var mainImages = inputPairs.TryGetValue(0, out ModuleChannel mainCh) ? (mainCh.ImageList ?? new List<ModuleImage>()) : new List<ModuleImage>();
				var mainResults = inputPairs.TryGetValue(0, out ModuleChannel mainCh2) ? (mainCh2.ResultList ?? new JArray()) : new JArray();
				var mainTemplates = inputPairs.TryGetValue(0, out ModuleChannel mainCh3) ? (mainCh3.TemplateList ?? new List<SimpleTemplate>()) : new List<SimpleTemplate>();
				var extraChannels = new List<ModuleChannel>();
				foreach (var kv in inputPairs.OrderBy(k=>k.Key))
				{
					if (kv.Key <= 0) continue;
					extraChannels.Add(kv.Value);
				}
				module.ExtraInputsIn.Clear();
				module.ExtraInputsIn.AddRange(extraChannels);
				module.MainTemplateList = mainTemplates ?? new List<SimpleTemplate>();

				// 标量输入注入（按索引与名称）。从来源节点 outputs[*].scalars 读取
				var scalarInputsByIdx = new Dictionary<int, object>();
				var scalarInputsByName = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
				try
				{
					if (node != null && node.TryGetValue("inputs", out object inv))
					{
						var inMetaList = AsListOfDict(inv);
						for (int ii = 0; ii < inMetaList.Count; ii++)
						{
							var inp = inMetaList[ii];
							if (inp == null) continue;
							int linkId = inp.TryGetValue("link", out object lv) ? SafeToInt(lv, -1) : -1;
							string dtype = inp.TryGetValue("type", out object tv2) && tv2 != null ? tv2.ToString() : null;
							if (linkId < 0 || !linkToSource.TryGetValue(linkId, out Tuple<int, int> src2)) continue;
							string kind = ClassifyPort(dtype);
							if (kind == "scalar")
							{
								int srcNodeId2 = src2.Item1; int srcOutIdx2 = src2.Item2;
								if (_outputs.TryGetValue(srcNodeId2, out Dictionary<string, object> dummy)) { }
								if (outputs.TryGetValue(srcNodeId2, out Dictionary<string, object> srcOutMap))
								{
									if (srcOutMap.TryGetValue("scalars", out object scon) && scon is Dictionary<int, object> smap)
									{
										if (smap.TryGetValue(srcOutIdx2, out object val))
										{
											scalarInputsByIdx[ii] = val;
											string inName = inp.TryGetValue("name", out object nn) && nn != null ? nn.ToString() : null;
											if (!string.IsNullOrEmpty(inName)) scalarInputsByName[inName] = val;
										}
									}
								}
							}
						}
					}
				}//增加报错处理
				catch (Exception ex) { Console.WriteLine($"Error in CollectInputPairs: {ex.Message}"); }
				module.ScalarInputsByIndex = scalarInputsByIdx;
				module.ScalarInputsByName = scalarInputsByName;

				// 执行当前节点
				var io = module.Process(mainImages, mainResults);

				// 保存该节点的全部输出通道
				var nodeOut = new NodeExecOutput
				{
					Main = new ModuleChannel(io.ImageList, io.ResultList, io.TemplateList),
					Extra = new List<ModuleChannel>(module.ExtraOutputs ?? new List<ModuleChannel>())
				};
				_nodeExecMap[nodeId] = nodeOut;

				// 对外暴露主通道（与原行为一致）
				outputs[nodeId] = new Dictionary<string, object>
				{
					{ "image_list", new List<ModuleImage>(io.ImageList ?? new List<ModuleImage>()) },
					{ "result_list", new JArray(io.ResultList ?? new JArray()) },
					{ "template_list", new List<SimpleTemplate>(io.TemplateList ?? new List<SimpleTemplate>()) }
				};

				// 标量输出：依据节点 outputs 元信息，从 module.ScalarOutputsByName 取值并按索引写入
				var scalarsByIdx = new Dictionary<int, object>();
				try
				{
					var outPorts = node != null && node.TryGetValue("outputs", out object ov2) ? AsListOfDict(ov2) : null;
					var scalarMap = module.ScalarOutputsByName ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
					if (outPorts != null)
					{
						for (int oi = 0; oi < outPorts.Count; oi++)
						{
							var meta = outPorts[oi];
							if (meta == null) continue;
							string otype = meta.TryGetValue("type", out object tv3) && tv3 != null ? tv3.ToString().ToLower() : null;
							if (otype == "bool" || otype == "boolean" || otype == "int" || otype == "integer" || otype == "str" || otype == "string")
							{
								string oname = meta.TryGetValue("name", out object nv) && nv != null ? nv.ToString() : null;
								object val = null;
								if (!string.IsNullOrEmpty(oname) && scalarMap.TryGetValue(oname, out object v1)) val = v1;
								else if (scalarMap.TryGetValue(oi.ToString(), out object v2)) val = v2;
								// 类型规范化
								if (otype == "bool" || otype == "boolean")
								{
									val = val is bool b ? b : (val != null ? Convert.ToBoolean(val) : false);
								}
								else if (otype == "int" || otype == "integer")
								{
									try { val = Convert.ToInt32(val); } catch { val = 0; }
								}
								else
								{
									val = val == null ? "" : val.ToString();
								}
								scalarsByIdx[oi] = val;
							}
						}
					}
				}
				catch { }
				if (scalarsByIdx.Count > 0)
				{
					outputs[nodeId]["scalars"] = scalarsByIdx;
				}
			}

			return outputs;
		}

		public JObject LoadModels()
		{
			var report = new JObject();
			var items = new JArray();
			int failCount = 0;

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

			for (int i = 0; i < ordered.Count; i++)
			{
				var node = ordered[i];
				string type = node != null && node.TryGetValue("type", out object tv) ? (tv != null ? tv.ToString() : null) : null;
				int nodeId = node != null && node.TryGetValue("id", out object iv) ? SafeToInt(iv, i) : i;
				string title = node != null && node.TryGetValue("title", out object tl) ? (tl != null ? tl.ToString() : null) : null;
				Dictionary<string, object> props = null;
				if (node != null && node.TryGetValue("properties", out object pv) && pv != null)
				{
					props = pv as Dictionary<string, object>;
					if (props == null)
					{
						var jo = pv as JObject;
						if (jo != null)
						{
							try { props = jo.ToObject<Dictionary<string, object>>(); } catch { props = null; }
						}
					}
				}
				if (props == null) props = new Dictionary<string, object>();

				var moduleType = ModuleRegistry.Get(type);
				if (moduleType == null) continue;
				var module = (BaseModule)Activator.CreateInstance(moduleType, nodeId, title, props, _context);

				var modelModule = module as BaseModelModule;
				if (modelModule != null)
				{
					string modelPath = null;
					try { if (props != null && props.ContainsKey("model_path") && props["model_path"] != null) modelPath = props["model_path"].ToString(); } catch { }
					var item = new JObject();
					item["node_id"] = nodeId;
					item["type"] = type ?? string.Empty;
					item["title"] = title ?? string.Empty;
					item["model_path"] = modelPath ?? string.Empty;
					try
					{
						modelModule.LoadModel();
						item["status_code"] = 0;
						item["status_message"] = "ok";
					}
					catch (Exception ex)
					{
						failCount++;
						item["status_code"] = 1;
						item["status_message"] = ex.Message ?? string.Empty;
						try { item["exception"] = ex.ToString(); } catch { }
					}
					items.Add(item);
				}
			}

			report["code"] = failCount == 0 ? 0 : 1;
			report["message"] = failCount == 0 ? "all models loaded" : ("models loaded with " + failCount + " error(s)");
			report["models"] = items;
			return report;
		}

		private static int SafeToInt(object v, int dv)
		{
			try { return Convert.ToInt32(v); } catch { return dv; }
		}

		private static List<Dictionary<string, object>> AsListOfDict(object obj)
		{
			var list = new List<Dictionary<string, object>>();
			if (obj == null) return list;
			if (obj is List<object> lo)
			{
				for (int i = 0; i < lo.Count; i++)
				{
					var d = lo[i] as Dictionary<string, object>;
					if (d != null) { list.Add(d); continue; }
					var jo = lo[i] as JObject;
					if (jo != null)
					{
						try { list.Add(jo.ToObject<Dictionary<string, object>>()); } catch { }
					}
				}
				return list;
			}
			var ja = obj as JArray;
			if (ja != null)
			{
				for (int i = 0; i < ja.Count; i++)
				{
					var jo = ja[i] as JObject;
					if (jo != null)
					{
						try { list.Add(jo.ToObject<Dictionary<string, object>>()); } catch { }
					}
				}
			}
			return list;
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
                if (!n.TryGetValue("outputs", out object ov)) continue;
                var outPorts = AsListOfDict(ov);
                if (outPorts == null || outPorts.Count == 0) continue;
                for (int oi = 0; oi < outPorts.Count; oi++)
                {
                    var o = outPorts[oi];
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
                    else
                    {
                        var jarr = lv as JArray;
                        if (jarr != null)
                        {
                            for (int jj = 0; jj < jarr.Count; jj++)
                            {
                                int lid = SafeToInt(jarr[jj], -1);
                                if (lid >= 0 && !map.ContainsKey(lid)) map[lid] = Tuple.Create(nid, oi);
                            }
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
            if (!node.TryGetValue("inputs", out object iv))
            {
                return pairs;
            }
            var inMetaList = AsListOfDict(iv);
            if (inMetaList == null || inMetaList.Count == 0) return pairs;
            for (int ii = 0; ii < inMetaList.Count; ii++)
            {
                var inp = inMetaList[ii];
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
                    ch = new ModuleChannel(new List<ModuleImage>(picked.ImageList ?? new List<ModuleImage>()), ch.ResultList ?? new JArray(), ch.TemplateList ?? new List<SimpleTemplate>());
                }
                else if (string.Equals(dtype, "result_chan", StringComparison.OrdinalIgnoreCase))
                {
                    var r = new JArray();
                    if (picked.ResultList != null) foreach (var t in picked.ResultList) r.Add(t);
                    ch = new ModuleChannel(ch.ImageList ?? new List<ModuleImage>(), r, ch.TemplateList ?? new List<SimpleTemplate>());
                }
                else if (string.Equals(dtype, "template_chan", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dtype, "template", StringComparison.OrdinalIgnoreCase))
                {
                    var tlist = new List<SimpleTemplate>(picked.TemplateList ?? new List<SimpleTemplate>());
                    ch = new ModuleChannel(ch.ImageList ?? new List<ModuleImage>(), ch.ResultList ?? new JArray(), tlist);
                }
                pairs[pairIdx] = ch;
            }
            return pairs;
        }

		private static string ClassifyPort(string dtype)
		{
			if (string.IsNullOrWhiteSpace(dtype)) return "unknown";
			var t = dtype.Trim().ToLowerInvariant();
			if (t == "bool" || t == "boolean" || t == "int" || t == "integer" || t == "str" || t == "string" || t == "scalar")
				return "scalar";
			// 其余类型均视作非标量通道
			return "channel";
		}

		/// <summary>
		/// 将常见的 bbox_xyxy 属性形式补齐为 bbox_xywh，便于模块侧统一按 XYWH 读取。
		/// 当前仅处理：
		/// - bbox_x1/bbox_y1/bbox_x2/bbox_y2  -> bbox_x/bbox_y/bbox_w/bbox_h
		/// </summary>
		private static void NormalizeBboxProperties(Dictionary<string, object> props)
		{
			if (props == null) return;

			bool hasX1 = props.TryGetValue("bbox_x1", out object vx1) && vx1 != null;
			bool hasY1 = props.TryGetValue("bbox_y1", out object vy1) && vy1 != null;
			bool hasX2 = props.TryGetValue("bbox_x2", out object vx2) && vx2 != null;
			bool hasY2 = props.TryGetValue("bbox_y2", out object vy2) && vy2 != null;
			if (!(hasX1 && hasY1 && hasX2 && hasY2)) return;

			double x1, y1, x2, y2;
			try { x1 = Convert.ToDouble(vx1); } catch { return; }
			try { y1 = Convert.ToDouble(vy1); } catch { return; }
			try { x2 = Convert.ToDouble(vx2); } catch { return; }
			try { y2 = Convert.ToDouble(vy2); } catch { return; }

			// 顺序修正
			double bx = Math.Min(x1, x2);
			double by = Math.Min(y1, y2);
			double bw = Math.Abs(x2 - x1);
			double bh = Math.Abs(y2 - y1);

			// 仅在未显式提供 bbox_w/bbox_h 时覆盖，避免用户明确给了 XYWH 还被改写
			if (!props.ContainsKey("bbox_x")) props["bbox_x"] = bx;
			if (!props.ContainsKey("bbox_y")) props["bbox_y"] = by;
			if (!props.ContainsKey("bbox_w")) props["bbox_w"] = bw;
			if (!props.ContainsKey("bbox_h")) props["bbox_h"] = bh;
		}
	}
}




