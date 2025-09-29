using System;
using System.Collections.Generic;
using System.Drawing;

namespace DlcvModules
{
	/// <summary>
	/// features/sliding_window：按给定窗口与步长对输入图像生成子图。
	/// 仅提供最小可编译与接口一致实现，后续可替换为高性能版本。
	/// properties:
	/// - small_img_width(int, default 832)
	/// - small_img_height(int, default 704)
	/// - horizontal_overlap(int, default 16)
	/// - vertical_overlap(int, default 16)
	/// </summary>
	public class SlidingWindow : BaseModule
	{
		static SlidingWindow()
		{
			ModuleRegistry.Register("features/sliding_window", typeof(SlidingWindow));
		}

		public SlidingWindow(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context)
		{
		}

		public override Tuple<List<object>, List<Dictionary<string, object>>> Process(List<object> imageList = null, List<Dictionary<string, object>> resultList = null)
		{
			var images = imageList ?? new List<object>();
			var results = resultList ?? new List<Dictionary<string, object>>();

			int winW = ReadInt("small_img_width", 832);
			int winH = ReadInt("small_img_height", 704);
			int hov = ReadInt("horizontal_overlap", 16);
			int vov = ReadInt("vertical_overlap", 16);

			var outImages = new List<object>();
			var outResults = new List<Dictionary<string, object>>();
			int outIndex = 0;

			for (int i = 0; i < images.Count; i++)
			{
				var tuple = Unwrap(images[i]);
				var wrap = tuple.Item1;
				var bmp = tuple.Item2;
				if (bmp == null) continue;

				int stepX = Math.Max(1, winW - hov);
				int stepY = Math.Max(1, winH - vov);

				for (int y = 0; y < bmp.Height; y += stepY)
				{
					for (int x = 0; x < bmp.Width; x += stepX)
					{
						int w = Math.Min(winW, Math.Max(1, bmp.Width - x));
						int h = Math.Min(winH, Math.Max(1, bmp.Height - y));
						if (w <= 0 || h <= 0) continue;

						var rect = new Rectangle(x, y, w, h);
						var cropped = new Bitmap(rect.Width, rect.Height);
						using (var g = Graphics.FromImage(cropped))
						{
							g.DrawImage(bmp, new Rectangle(0, 0, rect.Width, rect.Height), rect, System.Drawing.GraphicsUnit.Pixel);
						}

						var parentState = wrap != null ? wrap.TransformState : new TransformationState(bmp.Width, bmp.Height);
						var childA2x3 = new double[] { 1, 0, -x, 0, 1, -y };
						var childState = parentState.DeriveChild(childA2x3, w, h);
						var childWrap = new ModuleImage(cropped, wrap != null ? wrap.OriginalImage : bmp, childState, wrap != null ? wrap.OriginalIndex : i);
						outImages.Add(childWrap);

						var entry = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
						entry["type"] = "local";
						entry["index"] = outIndex;
						entry["origin_index"] = wrap != null ? wrap.OriginalIndex : i;
						entry["transform"] = childState.ToDict();
						entry["sample_results"] = new List<Dictionary<string, object>>();
						// 简单标注网格信息
						entry["sliding_meta"] = new Dictionary<string, object>
						{
							{ "x", x }, { "y", y }, { "w", w }, { "h", h },
							{ "win_w", winW }, { "win_h", winH }, { "step_x", stepX }, { "step_y", stepY }
						};
						outResults.Add(entry);
						outIndex += 1;
					}
				}
			}

			return Tuple.Create(outImages, outResults);
		}

		private int ReadInt(string key, int dv)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				try { return Convert.ToInt32(v); } catch { return dv; }
			}
			return dv;
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
	}
}



