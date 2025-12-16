using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
	/// <summary>
	/// features/sliding_window：按给定窗口与步长对输入图像生成子图。
	/// 仅提供最小可编译与接口一致实现，后续可替换为高性能版本。
	/// properties:
	/// - window_size([w,h], required, example [320,600])
	/// - overlap([ow,oh], required, example [32,32])
	/// - min_size(int, optional, default 1)  // 约束窗口/裁剪最小尺寸
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

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

			var win = ReadInt2("window_size", 832, 704);
			var ov = ReadInt2("overlap", 16, 16);
			int minSize = ReadInt("min_size", 1);

			int winW = Math.Max(minSize, win.Item1);
			int winH = Math.Max(minSize, win.Item2);
			int hov = Math.Max(0, ov.Item1);
			int vov = Math.Max(0, ov.Item2);
			// overlap 不能 >= window，否则步长会变成 0（这里统一裁到 window-1）
			if (winW > 0) hov = Math.Min(hov, winW - 1);
			if (winH > 0) vov = Math.Min(vov, winH - 1);

            var outImages = new List<ModuleImage>();
            var outResults = new JArray();
			int outIndex = 0;

			for (int i = 0; i < images.Count; i++)
			{
                var tuple = Unwrap(images[i]);
                var wrap = tuple.Item1;
                var mat = tuple.Item2;
                if (mat == null || mat.Empty()) continue;

				int stepX = Math.Max(1, winW - hov);
				int stepY = Math.Max(1, winH - vov);

                for (int y = 0; y < mat.Height; y += stepY)
				{
                    for (int x = 0; x < mat.Width; x += stepX)
					{
                        int w = Math.Min(winW, Math.Max(1, mat.Width - x));
                        int h = Math.Min(winH, Math.Max(1, mat.Height - y));
						if (w < minSize || h < minSize) continue;
                        var rect = new Rect(x, y, w, h);
                        var cropped = new Mat(mat, rect).Clone();

                        var parentState = wrap != null ? wrap.TransformState : new TransformationState(mat.Width, mat.Height);
						var childA2x3 = new double[] { 1, 0, -x, 0, 1, -y };
						var childState = parentState.DeriveChild(childA2x3, w, h);
                        var childWrap = new ModuleImage(cropped, wrap != null ? wrap.OriginalImage : mat, childState, wrap != null ? wrap.OriginalIndex : i);
						outImages.Add(childWrap);

                        var entry = new JObject
                        {
                            ["type"] = "local",
                            ["index"] = outIndex,
                            ["origin_index"] = wrap != null ? wrap.OriginalIndex : i,
                            ["transform"] = JObject.FromObject(childState.ToDict()),
                            ["sample_results"] = new JArray(),
                            ["sliding_meta"] = new JObject
                            {
                                ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h,
                                ["win_w"] = winW, ["win_h"] = winH, ["step_x"] = stepX, ["step_y"] = stepY
                            }
                        };
                        outResults.Add(entry);
						outIndex += 1;
					}
				}
			}

			return new ModuleIO(outImages, outResults);
		}

		private int ReadInt(string key, int dv)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				try { return Convert.ToInt32(v); } catch { return dv; }
			}
			return dv;
		}

		private Tuple<int, int> ReadInt2(string key, int dv1, int dv2)
		{
			if (Properties == null || !Properties.TryGetValue(key, out object v) || v == null)
				return Tuple.Create(dv1, dv2);

			try
			{
				// 常见：JArray / object[] / List<object> / int[]
				if (v is JArray ja && ja.Count >= 2)
					return Tuple.Create(Convert.ToInt32(ja[0]), Convert.ToInt32(ja[1]));

				if (v is object[] oa && oa.Length >= 2)
					return Tuple.Create(Convert.ToInt32(oa[0]), Convert.ToInt32(oa[1]));

				if (v is List<object> lo && lo.Count >= 2)
					return Tuple.Create(Convert.ToInt32(lo[0]), Convert.ToInt32(lo[1]));

				if (v is int[] ia && ia.Length >= 2)
					return Tuple.Create(ia[0], ia[1]);

				if (v is long[] la && la.Length >= 2)
					return Tuple.Create(Convert.ToInt32(la[0]), Convert.ToInt32(la[1]));

				// 兜底：比如字符串 "320,600"
				if (v is string s)
				{
					var parts = s.Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length >= 2)
						return Tuple.Create(Convert.ToInt32(parts[0]), Convert.ToInt32(parts[1]));
				}
			}
			catch
			{
				// 只对新配置做适配：解析失败就回退默认值（不做旧字段兼容）
			}

			return Tuple.Create(dv1, dv2);
		}

        private static Tuple<ModuleImage, Mat> Unwrap(ModuleImage obj)
		{
			if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
            return Tuple.Create(obj, obj.ImageObject);
		}
	}
}




