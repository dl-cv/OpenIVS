using System;
using System.Collections.Generic;

namespace DlcvModules
{
	/// <summary>
	/// GraphExecutor 最小骨架：仅顺序执行一个线性节点列表，便于可编译。
	/// 真实路由/多端口/额外输出可后续增强。
	/// </summary>
	public class GraphExecutor
	{
		private readonly List<Dictionary<string, object>> _nodes;
		private readonly ExecutionContext _context;

		public GraphExecutor(List<Dictionary<string, object>> nodes, ExecutionContext context = null)
		{
			_nodes = nodes ?? new List<Dictionary<string, object>>();
			_context = context ?? new ExecutionContext();
		}

		public Dictionary<int, Dictionary<string, List<object>>> Run()
		{
			var lastImages = new List<object>();
			var lastResults = new List<Dictionary<string, object>>();
			var outputs = new Dictionary<int, Dictionary<string, List<object>>>();

			for (int i = 0; i < _nodes.Count; i++)
			{
				var node = _nodes[i];
				string type = node != null && node.TryGetValue("type", out object tv) ? (tv != null ? tv.ToString() : null) : null;
				int nodeId = node != null && node.TryGetValue("id", out object iv) ? SafeToInt(iv, i) : i;
				string title = node != null && node.TryGetValue("title", out object tl) ? (tl != null ? tl.ToString() : null) : null;
				var props = node != null && node.TryGetValue("properties", out object pv) ? pv as Dictionary<string, object> : new Dictionary<string, object>();

				var moduleType = ModuleRegistry.Get(type);
				if (moduleType == null) continue;
				var module = (BaseModule)Activator.CreateInstance(moduleType, nodeId, title, props, _context);
				var tuple = module.Process(lastImages, lastResults);
				lastImages = tuple.Item1;
				lastResults = tuple.Item2;

				outputs[nodeId] = new Dictionary<string, List<object>>
				{
					{ "image_list", new List<object>(lastImages) },
					{ "result_list", new List<object>(lastResults.ConvertAll(x => (object)x)) }
				};
			}

			return outputs;
		}

		private static int SafeToInt(object v, int dv)
		{
			try { return Convert.ToInt32(v); } catch { return dv; }
		}
	}
}



