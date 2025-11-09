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

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

			int winW = ReadInt("small_img_width", 832);
			int winH = ReadInt("small_img_height", 704);
			int hov = ReadInt("horizontal_overlap", 16);
			int vov = ReadInt("vertical_overlap", 16);

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
						if (w <= 0 || h <= 0) continue;
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

        private static Tuple<ModuleImage, Mat> Unwrap(ModuleImage obj)
		{
			if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
            return Tuple.Create(obj, obj.ImageObject);
		}
	}
}




