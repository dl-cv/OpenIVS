using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

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

		public override Tuple<List<object>, List<Dictionary<string, object>>> Process(List<object> imageList = null, List<Dictionary<string, object>> resultList = null)
		{
			var images = imageList ?? new List<object>();
			var results = resultList ?? new List<Dictionary<string, object>>();

			// 构建 origin_index -> 原图 位图映射
			var originToBitmap = new Dictionary<int, Bitmap>();
			for (int i = 0; i < images.Count; i++)
			{
				var (wrap, bmp) = Unwrap(images[i]);
				if (bmp == null) continue;
				int originIndex = wrap != null ? wrap.OriginalIndex : i;
				if (!originToBitmap.ContainsKey(originIndex))
				{
					originToBitmap[originIndex] = bmp;
				}
			}

			// 遍历结果并画在原图上（矩形框）
			foreach (var entry in results)
			{
				if (entry == null) continue;
				int originIndex = ReadInt(entry, "origin_index", ReadInt(entry, "index", 0));
				if (!originToBitmap.TryGetValue(originIndex, out Bitmap target)) continue;
				var samples = ReadSamples(entry);
				if (samples == null) continue;
				using (var g = Graphics.FromImage(target))
				{
					g.SmoothingMode = SmoothingMode.AntiAlias;
					foreach (var s in samples)
					{
						if (!TryReadBbox(s, out int x, out int y, out int w, out int h)) continue;
						var color = Color.FromArgb(255, 0, 150, 255);
						using (var pen = new Pen(color, 2))
						{
							g.DrawRectangle(pen, new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h)));
						}
					}
				}
			}

			return Tuple.Create(images, results);
		}

		private static Tuple<ModuleImage, Bitmap> Unwrap(object obj)
		{
			if (obj is ModuleImage mi)
			{
				if (mi.ImageObject is Bitmap bmp1) return Tuple.Create(mi, bmp1);
				return Tuple.Create(mi, mi.ImageObject as Bitmap);
			}
			return Tuple.Create<ModuleImage, Bitmap>(null, obj as Bitmap);
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

		private static bool TryReadBbox(Dictionary<string, object> s, out int x, out int y, out int w, out int h)
		{
			x = y = w = h = 0;
			if (s == null || !s.TryGetValue("bbox", out object bv) || bv == null) return false;
			var list = bv as System.Collections.IEnumerable;
			if (list == null) return false;
			var vals = new List<double>();
			foreach (var o in list)
			{
				try { vals.Add(Convert.ToDouble(o)); } catch { }
			}
			if (vals.Count < 4) return false;
			x = Clamp(vals[0]); y = Clamp(vals[1]); w = Clamp(vals[2]); h = Clamp(vals[3]);
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

		public override Tuple<List<object>, List<Dictionary<string, object>>> Process(List<object> imageList = null, List<Dictionary<string, object>> resultList = null)
		{
			var images = imageList ?? new List<object>();
			var results = resultList ?? new List<Dictionary<string, object>>();

			for (int i = 0; i < images.Count; i++)
			{
				var (wrap, bmp) = Unwrap(images[i]);
				if (bmp == null) continue;
				var matched = FindResultByIndex(results, i);
				if (matched == null) continue;
				var samples = ReadSamples(matched);
				if (samples == null) continue;
				using (var g = Graphics.FromImage(bmp))
				{
					g.SmoothingMode = SmoothingMode.AntiAlias;
					foreach (var s in samples)
					{
						if (!TryReadBbox(s, out int x, out int y, out int w, out int h)) continue;
						using (var pen = new Pen(Color.Lime, 2))
						{
							g.DrawRectangle(pen, new Rectangle(Math.Max(0, x), Math.Max(0, y), Math.Max(1, w), Math.Max(1, h)));
						}
					}
				}
			}

			return Tuple.Create(images, results);
		}

		private static Dictionary<string, object> FindResultByIndex(List<Dictionary<string, object>> results, int idx)
		{
			foreach (var r in results)
			{
				int index = ReadInt(r, "index", -1);
				if (index == idx) return r;
			}
			return null;
		}

		private static Tuple<ModuleImage, Bitmap> Unwrap(object obj)
		{
			if (obj is ModuleImage mi)
			{
				if (mi.ImageObject is Bitmap bmp1) return Tuple.Create(mi, bmp1);
				return Tuple.Create(mi, mi.ImageObject as Bitmap);
			}
			return Tuple.Create<ModuleImage, Bitmap>(null, obj as Bitmap);
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

		private static bool TryReadBbox(Dictionary<string, object> s, out int x, out int y, out int w, out int h)
		{
			x = y = w = h = 0;
			if (s == null || !s.TryGetValue("bbox", out object bv) || bv == null) return false;
			var list = bv as System.Collections.IEnumerable;
			if (list == null) return false;
			var vals = new List<double>();
			foreach (var o in list)
			{
				try { vals.Add(Convert.ToDouble(o)); } catch { }
			}
			if (vals.Count < 4) return false;
			x = Clamp(vals[0]); y = Clamp(vals[1]); w = Clamp(vals[2]); h = Clamp(vals[3]);
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



