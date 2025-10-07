using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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

			// 1) 构建 origin_index -> 原图 Mat 映射（使用 OriginalImage）
            var originToBitmap = new Dictionary<int, Mat>();
			var originToState = new Dictionary<int, TransformationState>();
			for (int i = 0; i < images.Count; i++)
			{
				var (wrap, bmp) = Unwrap(images[i]);
                if (bmp == null || bmp.Empty()) continue;
				int originIndex = wrap != null ? wrap.OriginalIndex : i;
				Mat originMat = wrap != null && wrap.OriginalImage != null ? wrap.OriginalImage : bmp;
				if (!originToBitmap.ContainsKey(originIndex) && originMat != null && !originMat.Empty())
				{
					originToBitmap[originIndex] = originMat;
					originToState[originIndex] = new TransformationState(originMat.Width, originMat.Height);
				}
			}

			// 2) 在原图上绘制，将局部坐标通过 transform 反投影到原图坐标
			foreach (var token in results)
			{
				var entry = token as JObject;
				if (entry == null) continue;
				int originIndex = entry?["origin_index"]?.Value<int?>() ?? (entry?["index"]?.Value<int?>() ?? 0);
                if (!originToBitmap.TryGetValue(originIndex, out Mat target)) continue;
                var samples = entry?["sample_results"] as JArray;
				if (samples == null) continue;

                // 读取 transform 的 affine_2x3（original -> current），求逆得到 current -> original
                double[] inv2x3 = null;
                try
                {
                    var tdict = entry["transform"] as JObject;
                    var a23 = tdict != null ? (tdict["affine_2x3"] as JArray) : null;
                    if (a23 != null && a23.Count >= 6)
                    {
                        var m = new double[] { a23[0].Value<double>(), a23[1].Value<double>(), a23[2].Value<double>(), a23[3].Value<double>(), a23[4].Value<double>(), a23[5].Value<double>() };
                        inv2x3 = TransformationState.Inverse2x3(m);
                    }
                }
                catch { inv2x3 = null; }

                foreach (var s in samples)
                {
                    if (!(s is JObject so)) continue;
                    if (!TryReadBbox(so, out int lx, out int ly, out int lw, out int lh)) continue;

                    // 若存在角度信息，则先在局部坐标系内生成四角点；否则使用轴对齐矩形四角
                    var ptsLocal = new List<Point2d>();
                    bool withAngle = so["with_angle"]?.Value<bool>() ?? false;
                    double angle = so["angle"]?.Value<double?>() ?? -100.0;
                    if (withAngle && angle != -100.0)
                    {
                        double cx = so["bbox"][0].Value<double>();
                        double cy = so["bbox"][1].Value<double>();
                        double w = Math.Max(1.0, so["bbox"][2].Value<double>());
                        double h = Math.Max(1.0, so["bbox"][3].Value<double>());
                        double hw = w / 2.0, hh = h / 2.0;
                        double c = Math.Cos(angle), s2 = Math.Sin(angle);
                        double[,] offsets = new double[,] { { -hw, -hh }, { hw, -hh }, { hw, hh }, { -hw, hh } };
                        for (int k = 0; k < 4; k++)
                        {
                            double dx = offsets[k, 0];
                            double dy = offsets[k, 1];
                            double x = cx + c * dx - s2 * dy;
                            double y = cy + s2 * dx + c * dy;
                            ptsLocal.Add(new Point2d(x, y));
                        }
                    }
                    else
                    {
                        ptsLocal.Add(new Point2d(lx, ly));
                        ptsLocal.Add(new Point2d(lx + lw, ly));
                        ptsLocal.Add(new Point2d(lx + lw, ly + lh));
                        ptsLocal.Add(new Point2d(lx, ly + lh));
                    }

                    // 将局部点映射到原图
                    var ptsGlobal = new List<Point2d>();
                    if (inv2x3 != null)
                    {
                        foreach (var p in ptsLocal)
                        {
                            double gx = inv2x3[0] * p.X + inv2x3[1] * p.Y + inv2x3[2];
                            double gy = inv2x3[3] * p.X + inv2x3[4] * p.Y + inv2x3[5];
                            ptsGlobal.Add(new Point2d(gx, gy));
                        }
                    }
                    else
                    {
                        // 若无变换，认为坐标已是原图系
                        ptsGlobal.AddRange(ptsLocal);
                    }

                    // 取轴对齐外接矩形并绘制
                    int minX = (int)Math.Floor(ptsGlobal.Min(p => p.X));
                    int minY = (int)Math.Floor(ptsGlobal.Min(p => p.Y));
                    int maxX = (int)Math.Ceiling(ptsGlobal.Max(p => p.X));
                    int maxY = (int)Math.Ceiling(ptsGlobal.Max(p => p.Y));
                    int rw = Math.Max(1, maxX - minX);
                    int rh = Math.Max(1, maxY - minY);
                    var rect = new OpenCvSharp.Rect(Math.Max(0, minX), Math.Max(0, minY), rw, rh);
                    Cv2.Rectangle(target, rect, new Scalar(255, 150, 0), 2);
                }
			}

            // 3) 输出原图序列作为可视化结果
            var outImages = new List<ModuleImage>();
            foreach (var kv in originToBitmap)
            {
                var mat = kv.Value;
                if (mat == null || mat.Empty()) continue;
                var st = originToState.TryGetValue(kv.Key, out var sst) ? sst : new TransformationState(mat.Width, mat.Height);
                outImages.Add(new ModuleImage(mat, mat, st, kv.Key));
            }

			return new ModuleIO(outImages, results);
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




