using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
	/// <summary>
	/// features/sliding_window：按给定窗口与步长对输入图像生成子图。
	/// 逻辑已对齐 Python 版 (backend.multi_tasks.NewModules.sliding_window.py)：
	/// - 采用回退策略（Backtracking）：当窗口超出边界时，起始坐标回退，保证切出的图大小为 window_size（除非原图更小）。
	/// - 包含完整的 grid_x, grid_y 等元数据。
	/// properties:
	/// - window_size([w,h], required, default [640, 640])
	/// - overlap([ow,oh], required, default [0, 0])
	/// - min_size(int, optional, default 1)
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

			// 默认值尽量对齐 Python，但也保留一定的兼容性（如果需要严格一致，应在 JSON 配置中指定）
			// Python defaults: window=[640,640], overlap=[0,0]
			var win = ReadInt2("window_size", 640, 640);
			var ov = ReadInt2("overlap", 0, 0);
			int minSize = ReadInt("min_size", 1);

			int winW = Math.Max(minSize, win.Item1);
			int winH = Math.Max(minSize, win.Item2);
			int ovX = Math.Max(0, ov.Item1);
			int ovY = Math.Max(0, ov.Item2);

            var outImages = new List<ModuleImage>();
            var outResults = new JArray();
			int outIndex = 0;

			for (int i = 0; i < images.Count; i++)
			{
                var tuple = Unwrap(images[i]);
                var wrap = tuple.Item1;
                var mat = tuple.Item2;
                if (mat == null || mat.Empty()) continue;

                int H = mat.Height;
                int W = mat.Width;

                // 限制窗口不超过原图
                int smallW = Math.Min(winW, W);
                int smallH = Math.Min(winH, H);

                // 计算行列数 (逻辑对齐 Python)
                int rowNum, colNum;

                if (smallH >= H)
                {
                    rowNum = 1;
                }
                else
                {
                    int effH = Math.Max(1, smallH - ovY);
                    rowNum = H / effH;
                    if (H % effH > 0) rowNum++;
                }

                if (smallW >= W)
                {
                    colNum = 1;
                }
                else
                {
                    int effW = Math.Max(1, smallW - ovX);
                    colNum = W / effW;
                    if (W % effW > 0) colNum++;
                }

                // 遍历格点
                for (int r = 0; r < rowNum; r++)
				{
                    for (int c = 0; c < colNum; c++)
					{
                        int startX = c * (smallW - ovX);
                        int startY = r * (smallH - ovY);

                        // 右/下边界回退 (Backtracking)
                        if (startX + smallW > W) startX = W - smallW;
                        if (startY + smallH > H) startY = H - smallH;

                        // 边界保护
                        if (startX < 0) startX = 0;
                        if (startY < 0) startY = 0;

                        int endX = startX + smallW;
                        int endY = startY + smallH;

                        if ((endX - startX) < minSize || (endY - startY) < minSize) continue;

                        var rect = new Rect(startX, startY, endX - startX, endY - startY);
                        var cropped = new Mat(mat, rect).Clone();

                        var parentState = wrap != null ? wrap.TransformState : new TransformationState(W, H);
						// 2x3 matrix: [1, 0, -dx, 0, 1, -dy]
						var childA2x3 = new double[] { 1, 0, -startX, 0, 1, -startY };
						var childState = parentState.DeriveChild(childA2x3, endX - startX, endY - startY);
						
                        var childWrap = new ModuleImage(
                            cropped, 
                            wrap != null ? wrap.OriginalImage : mat, 
                            childState, 
                            wrap != null ? wrap.OriginalIndex : i
                        );
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
                                ["grid_x"] = c,
                                ["grid_y"] = r,
                                ["grid_size"] = new JArray { colNum, rowNum },
                                ["win_size"] = new JArray { endX - startX, endY - startY },
                                ["slice_index"] = new JArray { r, c },
                                ["x"] = startX, 
                                ["y"] = startY, 
                                ["w"] = endX - startX, 
                                ["h"] = endY - startY
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
