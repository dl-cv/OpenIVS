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

            bool emitPoly = ShouldEmitPoly();
            List<Dictionary<string, object>> byImage = CanUseAlignedFastPath(images, results)
                ? BuildByImageAligned(images, results, emitPoly)
                : BuildByImageMapped(images, results, emitPoly);

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

        private bool ShouldEmitPoly()
        {
            // 默认关闭 poly 输出，避免在批量汇总链路中放大 JSON 体积。
            // 仅在明确请求（节点属性或上下文开关）时输出 poly。
            bool emitPoly = ReadBoolProperty("emit_poly", false) || ReadBoolProperty("return_poly", false);
            if (Context != null)
            {
                emitPoly = emitPoly || Context.Get<bool>("return_json_emit_poly", false);
            }
            return emitPoly;
        }

        private bool ReadBoolProperty(string key, bool defaultValue)
        {
            if (Properties == null || string.IsNullOrWhiteSpace(key)) return defaultValue;
            if (!Properties.TryGetValue(key, out object raw) || raw == null) return defaultValue;
            try
            {
                if (raw is bool b) return b;
                if (raw is string s)
                {
                    if (bool.TryParse(s, out bool parsedBool)) return parsedBool;
                    if (int.TryParse(s, out int parsedInt)) return parsedInt != 0;
                }
                return Convert.ToBoolean(raw);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static bool CanUseAlignedFastPath(List<ModuleImage> images, JArray results)
        {
            if (images == null || results == null) return false;
            if (images.Count == 0 || images.Count != results.Count) return false;
            for (int i = 0; i < results.Count; i++)
            {
                var entry = results[i] as JObject;
                if (entry == null) return false;
                if (!string.Equals(entry["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase)) return false;
                int idx = entry["index"]?.Value<int?>() ?? i;
                if (idx != i) return false;
            }
            return true;
        }

        private static List<Dictionary<string, object>> BuildByImageAligned(List<ModuleImage> images, JArray results, bool emitPoly)
        {
            var byImage = new List<Dictionary<string, object>>(images != null ? images.Count : 0);
            if (images == null || results == null) return byImage;

            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null) continue;

                var entry = results[i] as JObject;
                var dets = entry != null ? entry["sample_results"] as JArray : null;
                var outResults = BuildOutResults(wrap, dets, emitPoly);
                byImage.Add(BuildImageEntry(wrap, outResults));
            }
            return byImage;
        }

        private static List<Dictionary<string, object>> BuildByImageMapped(List<ModuleImage> images, JArray results, bool emitPoly)
        {
            var byImage = new List<Dictionary<string, object>>(images != null ? images.Count : 0);
            if (images == null || results == null) return byImage;

            // 建立 index/origin_index/transform -> dets 映射
            // 说明：
            // - transform 在“整图单位变换”等场景下很容易相同，若优先按 transform 匹配会导致跨图串结果/重复结果。
            // - 因此这里优先使用 index（其次 origin_index），transform 仅作为兜底兼容旧流程。
            var transToDets = new Dictionary<string, List<JObject>>(Math.Max(1, results.Count));
            var indexToDets = new Dictionary<int, List<JObject>>(Math.Max(1, results.Count));
            var originToDets = new Dictionary<int, List<JObject>>(Math.Max(1, results.Count));

            foreach (var entryToken in results)
            {
                var entry = entryToken as JObject;
                if (entry == null || !string.Equals(entry["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase)) continue;

                var dets = entry["sample_results"] as JArray;
                if (dets == null || dets.Count == 0) continue;

                int idx = entry["index"]?.Value<int?>() ?? -1;
                int oidx = entry["origin_index"]?.Value<int?>() ?? -1;
                string tSig = SerializeTransformKey(entry["transform"] as JObject);

                List<JObject> idxBucket = idx >= 0 ? GetOrCreateDetBucket(indexToDets, idx) : null;
                List<JObject> orgBucket = oidx >= 0 ? GetOrCreateDetBucket(originToDets, oidx) : null;
                List<JObject> transBucket = !string.IsNullOrEmpty(tSig) ? GetOrCreateDetBucket(transToDets, tSig) : null;

                foreach (var d in dets)
                {
                    var detObj = d as JObject;
                    if (detObj == null) continue;
                    idxBucket?.Add(detObj);
                    orgBucket?.Add(detObj);
                    transBucket?.Add(detObj);
                }
            }

            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null) continue;

                List<JObject> dets = null;
                if (!indexToDets.TryGetValue(i, out dets))
                {
                    if (!originToDets.TryGetValue(wrap.OriginalIndex, out dets))
                    {
                        string sig = SerializeTransformKey(wrap.TransformState);
                        if (sig != null) transToDets.TryGetValue(sig, out dets);
                    }
                }

                var outResults = BuildOutResults(wrap, dets, emitPoly);
                byImage.Add(BuildImageEntry(wrap, outResults));
            }

            return byImage;
        }

        private static List<JObject> GetOrCreateDetBucket<TKey>(Dictionary<TKey, List<JObject>> map, TKey key)
        {
            if (!map.TryGetValue(key, out List<JObject> list))
            {
                list = new List<JObject>();
                map[key] = list;
            }
            return list;
        }

        private static Dictionary<string, object> BuildImageEntry(ModuleImage wrap, List<Dictionary<string, object>> outResults)
        {
            var ori = wrap.OriginalImage ?? wrap.ImageObject;
            int w0 = ori != null ? ori.Width : 0;
            int h0 = ori != null ? ori.Height : 0;
            return new Dictionary<string, object>
            {
                ["origin_index"] = wrap.OriginalIndex,
                ["original_size"] = new int[] { w0, h0 },
                ["results"] = outResults ?? new List<Dictionary<string, object>>()
            };
        }

        private static List<Dictionary<string, object>> BuildOutResults(ModuleImage wrap, JArray dets, bool emitPoly)
        {
            var outResults = new List<Dictionary<string, object>>(dets != null ? dets.Count : 0);
            if (wrap == null || dets == null || dets.Count == 0) return outResults;

            var tC2O = BuildTC2O(wrap.TransformState);
            bool isAxisAlignedTransform = IsAxisAlignedTransform(tC2O);
            foreach (var d in dets)
            {
                if (d is JObject detObj)
                {
                    AppendOutResult(detObj, tC2O, isAxisAlignedTransform, emitPoly, outResults);
                }
            }
            return outResults;
        }

        private static List<Dictionary<string, object>> BuildOutResults(ModuleImage wrap, List<JObject> dets, bool emitPoly)
        {
            var outResults = new List<Dictionary<string, object>>(dets != null ? dets.Count : 0);
            if (wrap == null || dets == null || dets.Count == 0) return outResults;

            var tC2O = BuildTC2O(wrap.TransformState);
            bool isAxisAlignedTransform = IsAxisAlignedTransform(tC2O);
            for (int i = 0; i < dets.Count; i++)
            {
                var detObj = dets[i];
                if (detObj == null) continue;
                AppendOutResult(detObj, tC2O, isAxisAlignedTransform, emitPoly, outResults);
            }
            return outResults;
        }

        private static void AppendOutResult(
            JObject detObj,
            double[] tC2O,
            bool isAxisAlignedTransform,
            bool emitPoly,
            List<Dictionary<string, object>> outResults)
        {
            var item = new Dictionary<string, object>(8)
            {
                ["category_id"] = detObj["category_id"]?.Value<int>() ?? 0,
                ["category_name"] = detObj["category_name"]?.ToString(),
                ["score"] = detObj["score"]?.Value<double>() ?? 0.0
            };

            var bboxLocal = detObj["bbox"] as JArray;
            bool isRot = bboxLocal != null && bboxLocal.Count == 5;

            if (isRot)
            {
                var rboxG = RBoxLocalToGlobal(bboxLocal, tC2O);
                if (rboxG != null)
                {
                    item["bbox"] = rboxG;
                    item["metadata"] = new Dictionary<string, object> { { "is_rotated", true } };
                }
            }
            else if (bboxLocal != null && bboxLocal.Count >= 4)
            {
                if (TryMapLocalBboxToAabb(bboxLocal, tC2O, isAxisAlignedTransform, out List<int> aabb))
                {
                    item["bbox"] = aabb;
                    item["metadata"] = new Dictionary<string, object> { { "is_rotated", false } };
                }
            }

            var maskInfo = detObj["mask_rle"];
            if (maskInfo != null && maskInfo.Type != JTokenType.Null)
            {
                item["mask_rle"] = maskInfo;
                if (emitPoly && TryBuildMaskPoly(maskInfo, bboxLocal, tC2O, out List<object> poly))
                {
                    item["poly"] = poly;
                }
            }

            var localPolyline = detObj["polyline"] as JArray;
            if (TryMapLocalPolyline(localPolyline, tC2O, out List<object> polylineOut))
            {
                item["polyline"] = polylineOut;
            }

            outResults.Add(item);
        }

        private static bool TryMapLocalBboxToAabb(JArray bboxLocal, double[] t, bool isAxisAlignedTransform, out List<int> aabb)
        {
            aabb = null;
            if (bboxLocal == null || bboxLocal.Count < 4 || t == null || t.Length < 6) return false;

            try
            {
                double bx = bboxLocal[0].Value<double>();
                double by = bboxLocal[1].Value<double>();
                double bw = bboxLocal[2].Value<double>();
                double bh = bboxLocal[3].Value<double>();

                double x1 = bx;
                double y1 = by;
                double x2 = bx + bw;
                double y2 = by + bh;

                double minx, miny, maxx, maxy;
                if (isAxisAlignedTransform)
                {
                    double gx1 = t[0] * x1 + t[2];
                    double gx2 = t[0] * x2 + t[2];
                    double gy1 = t[4] * y1 + t[5];
                    double gy2 = t[4] * y2 + t[5];

                    minx = Math.Min(gx1, gx2);
                    maxx = Math.Max(gx1, gx2);
                    miny = Math.Min(gy1, gy2);
                    maxy = Math.Max(gy1, gy2);
                }
                else
                {
                    double p1x = t[0] * x1 + t[1] * y1 + t[2];
                    double p1y = t[3] * x1 + t[4] * y1 + t[5];
                    double p2x = t[0] * x2 + t[1] * y1 + t[2];
                    double p2y = t[3] * x2 + t[4] * y1 + t[5];
                    double p3x = t[0] * x2 + t[1] * y2 + t[2];
                    double p3y = t[3] * x2 + t[4] * y2 + t[5];
                    double p4x = t[0] * x1 + t[1] * y2 + t[2];
                    double p4y = t[3] * x1 + t[4] * y2 + t[5];

                    minx = Math.Min(Math.Min(p1x, p2x), Math.Min(p3x, p4x));
                    maxx = Math.Max(Math.Max(p1x, p2x), Math.Max(p3x, p4x));
                    miny = Math.Min(Math.Min(p1y, p2y), Math.Min(p3y, p4y));
                    maxy = Math.Max(Math.Max(p1y, p2y), Math.Max(p3y, p4y));
                }

                aabb = new List<int>
                {
                    (int)Math.Floor(minx),
                    (int)Math.Floor(miny),
                    (int)Math.Ceiling(maxx),
                    (int)Math.Ceiling(maxy)
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryBuildMaskPoly(JToken maskInfo, JArray bboxLocal, double[] t, out List<object> poly)
        {
            poly = null;
            if (maskInfo == null || t == null || t.Length < 6) return false;

            try
            {
                using (var localMask = MaskRleUtils.MaskInfoToMat(maskInfo))
                {
                    if (localMask == null || localMask.Empty()) return false;

                    double x0 = 0.0;
                    double y0 = 0.0;
                    if (bboxLocal != null && bboxLocal.Count >= 4)
                    {
                        try
                        {
                            x0 = bboxLocal[0].Value<double>();
                            y0 = bboxLocal[1].Value<double>();
                        }
                        catch
                        {
                            x0 = 0.0;
                            y0 = 0.0;
                        }
                    }

                    using (var nz = new Mat())
                    {
                        Cv2.FindNonZero(localMask, nz);
                        if (nz == null || nz.Empty()) return false;

                        Mat nzReadable = nz;
                        Mat nzClone = null;
                        if (!nzReadable.IsContinuous())
                        {
                            nzClone = nzReadable.Clone();
                            nzReadable = nzClone;
                        }

                        try
                        {
                            int nPts = nzReadable.Rows;
                            if (nPts <= 0) return false;

                            var raw = new int[nPts * 2]; // CV_32SC2: x,y 交错
                            System.Runtime.InteropServices.Marshal.Copy(nzReadable.Data, raw, 0, raw.Length);

                            var polyList = new List<List<float>>(nPts);
                            for (int i = 0; i < nPts; i++)
                            {
                                int baseIndex = i * 2;
                                int lx = raw[baseIndex];
                                int ly = raw[baseIndex + 1];

                                double cx = x0 + lx;
                                double cy = y0 + ly;
                                float gx = (float)(t[0] * cx + t[1] * cy + t[2]);
                                float gy = (float)(t[3] * cx + t[4] * cy + t[5]);
                                polyList.Add(new List<float> { gx, gy });
                            }

                            if (polyList.Count == 0) return false;
                            poly = new List<object>(1) { polyList };
                            return true;
                        }
                        finally
                        {
                            if (nzClone != null) nzClone.Dispose();
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAxisAlignedTransform(double[] t)
        {
            if (t == null || t.Length < 6) return false;
            return Math.Abs(t[1]) < 1e-8 && Math.Abs(t[3]) < 1e-8;
        }

        private static string SerializeTransformKey(JObject stObj)
        {
            if (stObj == null) return null;
            var affine = stObj["affine_2x3"] as JArray;
            if (affine == null || affine.Count < 6) return null;

            try
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "T:{0:F4},{1:F4},{2:F2},{3:F4},{4:F4},{5:F2}",
                    affine[0].Value<double>(),
                    affine[1].Value<double>(),
                    affine[2].Value<double>(),
                    affine[3].Value<double>(),
                    affine[4].Value<double>(),
                    affine[5].Value<double>());
            }
            catch
            {
                return null;
            }
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

        private static bool TryMapLocalPolyline(JArray localPolyline, double[] tC2O, out List<object> mapped)
        {
            mapped = null;
            if (localPolyline == null || localPolyline.Count < 2 || tC2O == null || tC2O.Length < 6) return false;

            var points = new List<object>(localPolyline.Count);
            for (int i = 0; i < localPolyline.Count; i++)
            {
                var token = localPolyline[i];
                double x;
                double y;
                if (token is JArray pa && pa.Count >= 2)
                {
                    x = pa[0].Value<double>();
                    y = pa[1].Value<double>();
                }
                else if (token is JObject po && po.ContainsKey("x") && po.ContainsKey("y"))
                {
                    x = po["x"].Value<double>();
                    y = po["y"].Value<double>();
                }
                else
                {
                    continue;
                }

                double gx = tC2O[0] * x + tC2O[1] * y + tC2O[2];
                double gy = tC2O[3] * x + tC2O[4] * y + tC2O[5];
                points.Add(new List<int> { (int)Math.Round(gx), (int)Math.Round(gy) });
            }

            if (points.Count < 2) return false;
            mapped = points;
            return true;
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
