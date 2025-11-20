using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
	/// <summary>
	/// output/save_image：将输入图像按同序 result_list 的 filename 或时间戳保存到磁盘。
	/// properties:
	/// - save_path(string)
	/// - suffix(string, default "_out")
	/// - format(string, default "png")
	/// </summary>
	public class SaveImage : BaseModule
	{
		static SaveImage()
		{
			ModuleRegistry.Register("output/save_image", typeof(SaveImage));
		}

		public SaveImage(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
			var results = resultList ?? new JArray();

			string saveDir = ReadString("save_path", null);
			string suffix = ReadString("suffix", "_out");
			string fmt = ReadString("format", "png");
			if (!string.IsNullOrWhiteSpace(saveDir))
			{
				try { Directory.CreateDirectory(saveDir); } catch { }
			}

            for (int i = 0; i < images.Count; i++)
			{
				var (wrap, matRgb) = Unwrap(images[i]);
                if (matRgb == null || matRgb.Empty()) continue;
				string baseName = null;
				if (i < results.Count && results[i] != null && ((JObject)results[i])["filename"] != null && !string.IsNullOrWhiteSpace(((JObject)results[i])["filename"].ToString()))
				{
					baseName = Path.GetFileNameWithoutExtension(((JObject)results[i])["filename"].ToString());
				}
				if (string.IsNullOrWhiteSpace(baseName)) baseName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
				string fileName = baseName + suffix + "." + (string.IsNullOrWhiteSpace(fmt) ? "png" : fmt);
                if (!string.IsNullOrWhiteSpace(saveDir))
                {
                    string full = Path.Combine(saveDir, fileName);
                    try
                    {
                        // 统一按BGR写盘：
                        // 4通道 -> BGRA2BGR；1通道 -> GRAY2BGR；3通道视为BGR直写
                        int ch = matRgb.Channels();
                        if (ch == 4)
                        {
                            using (var matBgr = new Mat())
                            {
                                Cv2.CvtColor(matRgb, matBgr, ColorConversionCodes.BGRA2BGR);
                                Cv2.ImWrite(full, matBgr);
                            }
                        }
                        else if (ch == 1)
                        {
                            using (var matBgr = new Mat())
                            {
                                Cv2.CvtColor(matRgb, matBgr, ColorConversionCodes.GRAY2BGR);
                                Cv2.ImWrite(full, matBgr);
                            }
                        }
                        else
                        {
                            Cv2.ImWrite(full, matRgb);
                        }
                    } catch { }
                }
			}

			// 透传
			return new ModuleIO(images, results);
		}

		private string ReadString(string key, string dv)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				var s = v.ToString();
				return string.IsNullOrWhiteSpace(s) ? dv : s;
			}
			return dv;
		}

        private static Tuple<ModuleImage, Mat> Unwrap(ModuleImage obj)
		{
			if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
			return Tuple.Create(obj, obj.ImageObject);
		}
	}

	/// <summary>
	/// output/preview：透传。
	/// </summary>
	public class Preview : BaseModule
	{
		static Preview()
		{
			ModuleRegistry.Register("output/preview", typeof(Preview));
		}
		public Preview(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }

		public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			// 预览节点仅透传，不做额外绘制。前端直接显示该 image_list
			return new ModuleIO(imageList ?? new List<ModuleImage>(), resultList ?? new JArray());
		}
	}

    /// <summary>
    /// output/return_json：将检测结果还原到原图坐标系并组织为 JSON，发送给前端显示。
    /// </summary>
    public class ReturnJson : BaseModule
    {
        static ReturnJson()
        {
            ModuleRegistry.Register("output/return_json", typeof(ReturnJson));
        }

        public ReturnJson(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

            // 1. 建立 transform/index/origin_index -> dets 映射
            var transToDets = new Dictionary<string, List<JObject>>();
            var indexToDets = new Dictionary<int, List<JObject>>();
            var originToDets = new Dictionary<int, List<JObject>>();

            foreach (var entryToken in results)
            {
                if (!(entryToken is JObject entry) || entry["type"]?.ToString() != "local") continue;

                var dets = entry["sample_results"] as JArray;
                if (dets == null) continue;

                var detList = new List<JObject>();
                foreach (var d in dets) if (d is JObject dojb) detList.Add(dojb);

                var stDict = entry["transform"] as JObject;
                var st = stDict != null ? TransformationState.FromDict(stDict.ToObject<Dictionary<string, object>>()) : null;
                string tSig = SerializeTransformKey(st);

                if (tSig != null)
                {
                    if (!transToDets.ContainsKey(tSig)) transToDets[tSig] = new List<JObject>();
                    transToDets[tSig].AddRange(detList);
                }
                else
                {
                    int idx = entry["index"]?.Value<int?>() ?? -1;
                    if (idx >= 0)
                    {
                        if (!indexToDets.ContainsKey(idx)) indexToDets[idx] = new List<JObject>();
                        indexToDets[idx].AddRange(detList);
                    }
                    int oidx = entry["origin_index"]?.Value<int?>() ?? -1;
                    if (oidx >= 0)
                    {
                        if (!originToDets.ContainsKey(oidx)) originToDets[oidx] = new List<JObject>();
                        originToDets[oidx].AddRange(detList);
                    }
                }
            }

            // 2. 遍历图像，还原坐标
            var byImage = new List<Dictionary<string, object>>();

            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null) continue;

                var ori = wrap.OriginalImage ?? wrap.ImageObject;
                int W0 = ori != null ? ori.Width : 0;
                int H0 = ori != null ? ori.Height : 0;

                string sig = SerializeTransformKey(wrap.TransformState);
                List<JObject> dets = null;

                if (sig != null && transToDets.ContainsKey(sig)) dets = transToDets[sig];
                else if (indexToDets.ContainsKey(i)) dets = indexToDets[i];
                else if (originToDets.ContainsKey(wrap.OriginalIndex)) dets = originToDets[wrap.OriginalIndex];

                var outResults = new List<Dictionary<string, object>>();
                if (dets != null)
                {
                    // 计算 T_c2o
                    var T_c2o = BuildTC2O(wrap.TransformState);

                    foreach (var d in dets)
                    {
                        var item = new Dictionary<string, object>();
                        item["category_id"] = d["category_id"]?.Value<int>() ?? 0;
                        item["category_name"] = d["category_name"]?.ToString();
                        item["score"] = d["score"]?.Value<double>() ?? 0.0;

                        var bboxLocal = d["bbox"] as JArray;
                        bool isRot = false;
                        if (bboxLocal != null && bboxLocal.Count == 5) isRot = true;

                        // 还原 bbox
                        if (isRot)
                        {
                            var rboxG = RBoxLocalToGlobal(bboxLocal, T_c2o);
                            if (rboxG != null)
                            {
                                item["bbox"] = rboxG;
                                item["metadata"] = new Dictionary<string, object> { { "is_rotated", true } };
                            }
                        }
                        else if (bboxLocal != null && bboxLocal.Count == 4)
                        {
                            var poly = BBoxPolyInOriginal(bboxLocal, T_c2o);
                            if (poly != null)
                            {
                                item["bbox"] = AABBFromPoly(poly);
                                item["metadata"] = new Dictionary<string, object> { { "is_rotated", false } };
                            }
                        }

                        // 还原 mask -> poly
                        var maskToken = d["mask"]; // JArray of {x,y}
                        if (maskToken is JArray maskArr && maskArr.Count > 0)
                        {
                            // C# DetModel 已经将 mask 转为 contours 点集（局部坐标）
                            // 直接变换这些点
                            var ptsLocal = new List<Point2f>();
                            foreach (var p in maskArr)
                            {
                                if (p is JObject pj)
                                {
                                    ptsLocal.Add(new Point2f(pj.Value<float>("x"), pj.Value<float>("y")));
                                }
                            }
                            var ptsGlobal = TransformPoints(T_c2o, ptsLocal.ToArray());
                            var polyList = new List<List<float>>();
                            foreach (var p in ptsGlobal)
                            {
                                polyList.Add(new List<float> { p.X, p.Y });
                            }
                            item["poly"] = new List<object> { polyList }; // 格式：[[[x,y],...]]
                        }

                        outResults.Add(item);
                    }
                }

                var imgEntry = new Dictionary<string, object>();
                imgEntry["origin_index"] = wrap.OriginalIndex;
                imgEntry["original_size"] = new int[] { W0, H0 };
                imgEntry["results"] = outResults;
                byImage.Add(imgEntry);
            }

            var payload = new Dictionary<string, object> { { "by_image", byImage } };

            // 写入 Context
            if (Context != null)
            {
                var existing = Context.Get<Dictionary<string, object>>("frontend_json", null) ?? new Dictionary<string, object>();
                var byNode = Context.Get<Dictionary<string, object>>("frontend_json_by_node", null) ?? new Dictionary<string, object>();
                
                byNode[NodeId.ToString()] = payload;
                
                existing["last"] = payload;
                existing["by_node"] = byNode;
                
                Context.Set("frontend_json_by_node", byNode);
                Context.Set("frontend_json", existing);
            }

            return new ModuleIO(images, results);
        }

        private static string SerializeTransformKey(TransformationState st)
        {
            if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length < 6) return null;
            var a = st.AffineMatrix2x3;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "T:{0:F4},{1:F4},{2:F2},{3:F4},{4:F4},{5:F2}", a[0], a[1], a[2], a[3], a[4], a[5]);
        }

        private static double[] BuildTC2O(TransformationState st)
        {
            // T_c2o = Inverse(AffineMatrix2x3)
            // AffineMatrix2x3 is Original -> Current
            if (st == null || st.AffineMatrix2x3 == null) return new double[] { 1, 0, 0, 0, 1, 0 };
            try
            {
                return TransformationState.Inverse2x3(st.AffineMatrix2x3);
            }
            catch
            {
                return new double[] { 1, 0, 0, 0, 1, 0 };
            }
        }

        private static Point2f[] TransformPoints(double[] T, Point2f[] pts)
        {
            // T is 2x3
            var res = new Point2f[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                double x = pts[i].X;
                double y = pts[i].Y;
                double nx = T[0] * x + T[1] * y + T[2];
                double ny = T[3] * x + T[4] * y + T[5];
                res[i] = new Point2f((float)nx, (float)ny);
            }
            return res;
        }

        private static List<double> RBoxLocalToGlobal(JArray rbox, double[] T)
        {
            // rbox: [cx, cy, w, h, angle(rad)]
            double cx = rbox[0].Value<double>();
            double cy = rbox[1].Value<double>();
            double w = rbox[2].Value<double>();
            double h = rbox[3].Value<double>();
            double ang = rbox[4].Value<double>();

            // Transform center
            double ncx = T[0] * cx + T[1] * cy + T[2];
            double ncy = T[3] * cx + T[4] * cy + T[5];

            // Transform size & angle
            // Linear part L
            double l00 = T[0], l01 = T[1];
            double l10 = T[3], l11 = T[4];

            double c = Math.Cos(ang), s = Math.Sin(ang);
            // Unit vectors of box axes
            double ux = c, uy = s;
            double vx = -s, vy = c;

            // Transformed axes
            double tux_x = l00 * ux + l01 * uy;
            double tux_y = l10 * ux + l11 * uy;
            double tvx_x = l00 * vx + l01 * vy;
            double tvx_y = l10 * vx + l11 * vy;

            double scale_w = Math.Sqrt(tux_x * tux_x + tux_y * tux_y);
            double scale_h = Math.Sqrt(tvx_x * tvx_x + tvx_y * tvx_y);

            double nw = w * scale_w;
            double nh = h * scale_h;
            double nang = Math.Atan2(tux_y, tux_x);

            return new List<double> { ncx, ncy, nw, nh, nang };
        }

        private static Point2f[] BBoxPolyInOriginal(JArray bbox, double[] T)
        {
            // bbox: [x1, y1, x2, y2]
            float x1 = bbox[0].Value<float>();
            float y1 = bbox[1].Value<float>();
            float x2 = bbox[2].Value<float>();
            float y2 = bbox[3].Value<float>();

            var pts = new Point2f[]
            {
                new Point2f(x1, y1), new Point2f(x2, y1),
                new Point2f(x2, y2), new Point2f(x1, y2)
            };
            return TransformPoints(T, pts);
        }

        private static List<int> AABBFromPoly(Point2f[] poly)
        {
            float minx = float.MaxValue, miny = float.MaxValue;
            float maxx = float.MinValue, maxy = float.MinValue;
            foreach (var p in poly)
            {
                if (p.X < minx) minx = p.X;
                if (p.X > maxx) maxx = p.X;
                if (p.Y < miny) miny = p.Y;
                if (p.Y > maxy) maxy = p.Y;
            }
            return new List<int> { (int)Math.Floor(minx), (int)Math.Floor(miny), (int)Math.Ceiling(maxx), (int)Math.Ceiling(maxy) };
        }
    }
}
