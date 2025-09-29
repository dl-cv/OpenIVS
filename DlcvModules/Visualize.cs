using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
	/// <summary>
	/// output/visualize：在原图坐标系绘制检测结果（最小骨架）。
	/// 规则：当 transform 存在时，认为为局部结果，直接绘制在对应原图上（此处简化：不做复杂坐标反投影，只绘制bbox框）。
	/// </summary>
	public class VisualizeOnOriginal : BaseModule
	{
		static VisualizeOnOriginal()
		{
			ModuleRegistry.Register("output/visualize", typeof(VisualizeOnOriginal));
		}

		public VisualizeOnOriginal(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
			var results = resultList ?? new JArray();

			// 构建 origin_index -> 原图 位图映射
            var originToBitmap = new Dictionary<int, Mat>();
			for (int i = 0; i < images.Count; i++)
			{
				var (wrap, bmp) = Unwrap(images[i]);
                if (bmp == null || bmp.Empty()) continue;
				int originIndex = wrap != null ? wrap.OriginalIndex : i;
				if (!originToBitmap.ContainsKey(originIndex))
				{
					originToBitmap[originIndex] = bmp;
				}
			}

			// 遍历结果并画在原图上（矩形框）
			foreach (var token in results)
			{
				var entry = token as JObject;
				if (entry == null) continue;
				int originIndex = entry?["origin_index"]?.Value<int?>() ?? (entry?["index"]?.Value<int?>() ?? 0);
                if (!originToBitmap.TryGetValue(originIndex, out Mat target)) continue;
                var samples = entry?["sample_results"] as JArray;
				if (samples == null) continue;
                foreach (var s in samples)
                {
                    if (!(s is JObject so)) continue;
                    if (!TryReadBbox(so, out int x, out int y, out int w, out int h)) continue;
                    Cv2.Rectangle(target, new OpenCvSharp.Rect(Math.Max(0, x), Math.Max(0, y), Math.Max(1, w), Math.Max(1, h)), new Scalar(255, 150, 0), 2);
                }
			}

			return new ModuleIO(images, results);
		}

        private static Tuple<ModuleImage, Mat> Unwrap(ModuleImage obj)
		{
			if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
			return Tuple.Create(obj, obj.ImageObject);
		}
        private static bool TryReadBbox(JObject s, out int x, out int y, out int w, out int h)
		{
			x = y = w = h = 0;
            var arr = s?["bbox"] as JArray;
            if (arr == null || arr.Count < 4) return false;
            x = Clamp(arr[0].Value<double>());
            y = Clamp(arr[1].Value<double>());
            w = Clamp(arr[2].Value<double>());
            h = Clamp(arr[3].Value<double>());
			return true;
		}

		private static int Clamp(double v)
		{
			if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
			if (v > int.MaxValue) return int.MaxValue;
			if (v < int.MinValue) return int.MinValue;
			return (int)Math.Round(v);
		}
	}

	/// <summary>
	/// output/visualize_local：在当前局部图上绘制（矩形框）。
	/// </summary>
	public class VisualizeOnLocal : BaseModule
	{
		static VisualizeOnLocal()
		{
			ModuleRegistry.Register("output/visualize_local", typeof(VisualizeOnLocal));
		}

		public VisualizeOnLocal(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
			var results = resultList ?? new JArray();

			for (int i = 0; i < images.Count; i++)
			{
				var (wrap, bmp) = Unwrap(images[i]);
                if (bmp == null || bmp.Empty()) continue;
				var matched = FindResultByIndex(results, i);
				if (matched == null) continue;
                var samples = matched?["sample_results"] as JArray;
				if (samples == null) continue;
                foreach (var s in samples)
                {
                    if (!(s is JObject so)) continue;
                    if (!TryReadBbox(so, out int x, out int y, out int w, out int h)) continue;
                    Cv2.Rectangle(bmp, new OpenCvSharp.Rect(Math.Max(0, x), Math.Max(0, y), Math.Max(1, w), Math.Max(1, h)), new Scalar(0, 255, 0), 2);
                }
			}

			return new ModuleIO(images, results);
		}

        private static JObject FindResultByIndex(JArray results, int idx)
		{
			foreach (var token in results)
			{
				var r = token as JObject;
				int index = r?["index"]?.Value<int?>() ?? -1;
				if (index == idx) return r;
			}
			return null;
		}

        private static Tuple<ModuleImage, Mat> Unwrap(ModuleImage obj)
		{
			if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
			return Tuple.Create(obj, obj.ImageObject);
		}

		private static int ReadInt(Dictionary<string, object> d, string k, int dv)
		{
			if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return dv;
			try { return Convert.ToInt32(v); } catch { return dv; }
		}

		private static List<Dictionary<string, object>> ReadSamples(Dictionary<string, object> d)
		{
			if (d == null || !d.TryGetValue("sample_results", out object v) || v == null) return null;
			return v as List<Dictionary<string, object>>;
		}

        private static bool TryReadBbox(JObject s, out int x, out int y, out int w, out int h)
		{
			x = y = w = h = 0;
            if (s == null) return false;
            var arr = s["bbox"] as JArray;
            if (arr == null || arr.Count < 4) return false;
            x = Clamp(arr[0].Value<double>()); y = Clamp(arr[1].Value<double>()); w = Clamp(arr[2].Value<double>()); h = Clamp(arr[3].Value<double>());
			return true;
		}

		private static int Clamp(double v)
		{
			if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
			if (v > int.MaxValue) return int.MaxValue;
			if (v < int.MinValue) return int.MinValue;
			return (int)Math.Round(v);
		}
	}
}




