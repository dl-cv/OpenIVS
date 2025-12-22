using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    /// <summary>
    /// 模块名称：结果过滤（区域）
    /// 按指定矩形区域将结果与图像拆分为两路输出（区域内 / 区域外）。
    ///
    /// 规则（对齐 Python: post_process/result_filter_region）：
    /// - “完全不在区域内”的结果将被分流到第二路（ExtraOutputs[0]）。
    /// - 若存在 mask_rle / mask_array：优先用 mask 与区域的像素级重叠判断（任意像素重叠即视为区域内）。
    /// - 若不存在 mask：使用 bbox 与区域的相交判断。
    /// - 区域属性 x/y/w/h 默认认为处于“原图坐标”；若结果处于当前图坐标，会自动做一定的容错匹配。
    /// - 标量输出：has_positive（第一路是否存在任何结果）。
    /// </summary>
    public class ResultFilterRegion : BaseModule
    {
        static ResultFilterRegion()
        {
            ModuleRegistry.Register("post_process/result_filter_region", typeof(ResultFilterRegion));
            ModuleRegistry.Register("features/result_filter_region", typeof(ResultFilterRegion));
        }

        public ResultFilterRegion(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

            // 读取属性并规范化
            int x = ReadInt("x", 0);
            int y = ReadInt("y", 0);
            int w = Math.Max(1, ReadInt("w", 100));
            int h = Math.Max(1, ReadInt("h", 100));

            // 为每张图像准备 ROI（原图/当前图）缓存
            var roiCache = new Dictionary<int, Tuple<int[], int[]>>(); // wrapId -> (roi_o, roi_c)
            Tuple<int[], int[]> GetRois(ModuleImage wrap)
            {
                int key = wrap != null ? RuntimeHelpers.GetHashCode(wrap) : 0;
                if (roiCache.TryGetValue(key, out var cached)) return cached;

                var baseImg = wrap != null ? wrap.GetImage() : null;
                int Wc = baseImg != null && !baseImg.Empty() ? baseImg.Width : 1;
                int Hc = baseImg != null && !baseImg.Empty() ? baseImg.Height : 1;

                int W0 = 0, H0 = 0;
                try
                {
                    if (wrap != null && wrap.TransformState != null)
                    {
                        W0 = wrap.TransformState.OriginalWidth;
                        H0 = wrap.TransformState.OriginalHeight;
                    }
                }
                catch { }
                if (W0 <= 0 || H0 <= 0)
                {
                    try
                    {
                        var ori = wrap != null ? wrap.OriginalImage : null;
                        if (ori != null && !ori.Empty())
                        {
                            W0 = ori.Width;
                            H0 = ori.Height;
                        }
                    }
                    catch { }
                }
                if (W0 <= 0) W0 = Wc;
                if (H0 <= 0) H0 = Hc;

                var roiO = ClampXYXY(x, y, x + w, y + h, W0, H0);
                var roiC = ComputeRoiInCurrent(wrap != null ? wrap.TransformState : null, roiO, Wc, Hc);
                var tup = Tuple.Create(roiO, roiC);
                roiCache[key] = tup;
                return tup;
            }

            // wrap 索引：transform(仅矩阵)+origin_index 优先，其次 origin_index
            var keyToWrap = new Dictionary<string, ModuleImage>(StringComparer.OrdinalIgnoreCase);
            var originToWrap = new Dictionary<int, ModuleImage>();
            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null) continue;
                originToWrap[wrap.OriginalIndex] = wrap;
                string k = SerializeTransformOnly(wrap.TransformState, wrap.OriginalIndex);
                keyToWrap[k] = wrap;
            }

            ModuleImage PickWrapForEntry(JObject entry)
            {
                if (entry == null) return null;
                int originIndex = entry["origin_index"]?.Value<int?>() ?? -1;
                try
                {
                    var stObj = entry["transform"] as JObject;
                    if (stObj != null)
                    {
                        var st = TransformationState.FromDict(stObj.ToObject<Dictionary<string, object>>());
                        if (st != null && originIndex >= 0)
                        {
                            string k = SerializeTransformOnly(st, originIndex);
                            if (keyToWrap.TryGetValue(k, out var w0)) return w0;
                        }
                    }
                }
                catch { }
                if (originIndex >= 0 && originToWrap.TryGetValue(originIndex, out var w1)) return w1;
                return null;
            }

            // 聚合：每张图像的 inside/outside det 列表
            var insideByWrap = new Dictionary<int, JArray>();
            var outsideByWrap = new Dictionary<int, JArray>();
            var others = new List<JToken>(); // 非 local / 无法定位的条目：透传到两路，避免丢失

            bool hasAnyInside = false;

            foreach (var token in results)
            {
                var entry = token as JObject;
                if (entry == null)
                {
                    others.Add(token);
                    continue;
                }
                if (!string.Equals(entry["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase))
                {
                    others.Add(entry);
                    continue;
                }

                var dets = entry["sample_results"] as JArray;
                if (dets == null)
                {
                    others.Add(entry);
                    continue;
                }

                var wrap = PickWrapForEntry(entry);
                if (wrap == null)
                {
                    others.Add(entry);
                    continue;
                }

                int wrapId = RuntimeHelpers.GetHashCode(wrap);
                if (!insideByWrap.TryGetValue(wrapId, out var inArr)) { inArr = new JArray(); insideByWrap[wrapId] = inArr; }
                if (!outsideByWrap.TryGetValue(wrapId, out var outArr)) { outArr = new JArray(); outsideByWrap[wrapId] = outArr; }

                var baseImg = wrap.GetImage();
                int Wc = baseImg != null && !baseImg.Empty() ? baseImg.Width : 1;
                int Hc = baseImg != null && !baseImg.Empty() ? baseImg.Height : 1;
                int W0 = wrap.TransformState != null ? wrap.TransformState.OriginalWidth : Wc;
                int H0 = wrap.TransformState != null ? wrap.TransformState.OriginalHeight : Hc;
                if (W0 <= 0) W0 = Wc;
                if (H0 <= 0) H0 = Hc;

                var rois = GetRois(wrap);
                var roiO = rois.Item1;
                var roiC = rois.Item2;

                foreach (var dTok in dets)
                {
                    if (!(dTok is JObject d))
                    {
                        outArr.Add(dTok);
                        continue;
                    }

                    // 提取 bbox（当前坐标系下的 AABB）
                    if (!TryExtractBboxAabbCurrent(d, out double bx1, out double by1, out double bx2, out double by2))
                    {
                        // 异常项：按区域外处理
                        outArr.Add(d);
                        continue;
                    }

                    var bboxCur = ClampXYXY(bx1, by1, bx2, by2, Wc, Hc);
                    int[] bboxOri = null;
                    try
                    {
                        bboxOri = MapAabbToOriginalAndClamp(wrap.TransformState, bboxCur, W0, H0);
                    }
                    catch { bboxOri = null; }

                    // metadata.global_bbox（容错：可能为原图坐标）
                    int[] bboxMetaOri = null;
                    try
                    {
                        var meta = d["metadata"] as JObject;
                        var gb = meta != null ? (meta["global_bbox"] as JArray) : null;
                        if (gb != null && (gb.Count == 4 || gb.Count == 5))
                        {
                            bboxMetaOri = ParseGlobalBboxToAabb(gb);
                            if (bboxMetaOri != null) bboxMetaOri = ClampXYXY(bboxMetaOri[0], bboxMetaOri[1], bboxMetaOri[2], bboxMetaOri[3], W0, H0);
                        }
                    }
                    catch { bboxMetaOri = null; }

                    // 选择空间（尽量与 Python 逻辑一致）
                    bool useOriginal = false;
                    int[] bboxUse = bboxCur;
                    try
                    {
                        // 若 metadata 的 bbox 明显超出当前图尺寸，优先认为它是原图坐标
                        if (bboxMetaOri != null && (bboxMetaOri[2] > Wc + 1 || bboxMetaOri[3] > Hc + 1))
                        {
                            useOriginal = true;
                            bboxUse = bboxMetaOri;
                        }
                        else
                        {
                            // 若 bbox 数值范围明显超出当前图，且可得到原图 bbox，则认为是原图坐标
                            if ((bx2 > Wc + 1 || by2 > Hc + 1) && bboxOri != null)
                            {
                                useOriginal = true;
                                bboxUse = bboxOri;
                            }
                        }
                    }
                    catch
                    {
                        useOriginal = false;
                        bboxUse = bboxCur;
                    }

                    var roiUse = useOriginal ? roiO : roiC;
                    int spaceW = useOriginal ? W0 : Wc;
                    int spaceH = useOriginal ? H0 : Hc;

                    // 判定：mask 优先，其次 bbox
                    bool isIn = false;
                    bool decided = false;

                    // mask_rle
                    var maskRle = d["mask_rle"];
                    if (maskRle != null)
                    {
                        try
                        {
                            using (var maskMat0 = MaskRleUtils.MaskInfoToMat(maskRle))
                            {
                                if (maskMat0 != null && !maskMat0.Empty())
                                {
                                    isIn = CheckMaskOverlapWithRegion(maskMat0, bboxUse, roiUse, spaceW, spaceH);
                                    decided = true;
                                }
                            }
                        }
                        catch { decided = false; }
                    }

                    // mask_array（尽力支持；解析失败则忽略）
                    if (!decided && d["mask_array"] != null)
                    {
                        try
                        {
                            using (var maskMat = TryParseMaskArrayToMat(d["mask_array"]))
                            {
                                if (maskMat != null && !maskMat.Empty())
                                {
                                    isIn = CheckMaskOverlapWithRegion(maskMat, bboxUse, roiUse, spaceW, spaceH);
                                    decided = true;
                                }
                            }
                        }
                        catch { decided = false; }
                    }

                    if (!decided)
                    {
                        // bbox 相交判定
                        isIn = BboxIntersects(bboxUse, roiUse);
                    }

                    if (isIn)
                    {
                        inArr.Add(d);
                        hasAnyInside = true;
                    }
                    else
                    {
                        outArr.Add(d);
                    }
                }
            }

            // 构建输出：保持 image_list 与 result_list 对齐（index 与 image 下标一致）
            var outImagesIn = new List<ModuleImage>();
            var outResultsIn = new JArray();
            var outImagesOut = new List<ModuleImage>();
            var outResultsOut = new JArray();

            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null) continue;
                int wrapId = RuntimeHelpers.GetHashCode(wrap);

                if (insideByWrap.TryGetValue(wrapId, out var inArr) && inArr != null && inArr.Count > 0)
                {
                    outImagesIn.Add(wrap);
                    var st = wrap.TransformState != null ? JObject.FromObject(wrap.TransformState.ToDict()) : null;
                    outResultsIn.Add(new JObject
                    {
                        ["type"] = "local",
                        ["index"] = outImagesIn.Count - 1,
                        ["origin_index"] = wrap.OriginalIndex,
                        ["transform"] = st,
                        ["sample_results"] = inArr
                    });
                }

                if (outsideByWrap.TryGetValue(wrapId, out var outArr) && outArr != null && outArr.Count > 0)
                {
                    outImagesOut.Add(wrap);
                    var st = wrap.TransformState != null ? JObject.FromObject(wrap.TransformState.ToDict()) : null;
                    outResultsOut.Add(new JObject
                    {
                        ["type"] = "local",
                        ["index"] = outImagesOut.Count - 1,
                        ["origin_index"] = wrap.OriginalIndex,
                        ["transform"] = st,
                        ["sample_results"] = outArr
                    });
                }
            }

            // 透传非 local 条目到两路（避免丢失）
            if (others.Count > 0)
            {
                foreach (var o in others)
                {
                    outResultsIn.Add(o);
                    outResultsOut.Add(o);
                }
            }

            // 第二路通过 extra output 暴露
            this.ExtraOutputs.Add(new ModuleChannel(outImagesOut, outResultsOut));

            // 标量输出
            try
            {
                this.ScalarOutputsByName["has_positive"] = hasAnyInside;
            }
            catch { }

            return new ModuleIO(outImagesIn, outResultsIn);
        }

        private int ReadInt(string key, int dv)
        {
            try
            {
                if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
                {
                    return Convert.ToInt32(Convert.ToDouble(v));
                }
            }
            catch { }
            return dv;
        }

        private static string SerializeTransformOnly(TransformationState st, int originIndex)
        {
            if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length < 6)
            {
                return $"org:{originIndex}|T:null";
            }
            var a = st.AffineMatrix2x3;
            return $"org:{originIndex}|T:{a[0]:F6},{a[1]:F6},{a[2]:F3},{a[3]:F6},{a[4]:F6},{a[5]:F3}";
        }

        private static int[] ClampXYXY(double x1, double y1, double x2, double y2, int W, int H)
        {
            W = Math.Max(1, W);
            H = Math.Max(1, H);

            double a1 = Math.Min(x1, x2);
            double a2 = Math.Max(x1, x2);
            double b1 = Math.Min(y1, y2);
            double b2 = Math.Max(y1, y2);

            int ix1 = (int)Math.Max(0, Math.Min(W - 1, Math.Floor(a1)));
            int iy1 = (int)Math.Max(0, Math.Min(H - 1, Math.Floor(b1)));
            int ix2 = (int)Math.Max(ix1 + 1, Math.Min(W, Math.Ceiling(a2)));
            int iy2 = (int)Math.Max(iy1 + 1, Math.Min(H, Math.Ceiling(b2)));
            return new[] { ix1, iy1, ix2, iy2 };
        }

        private static bool BboxIntersects(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length < 4 || b.Length < 4) return false;
            int iw = Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]);
            int ih = Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]);
            return iw > 0 && ih > 0;
        }

        private static int[] ComputeRoiInCurrent(TransformationState st, int[] roiO, int Wc, int Hc)
        {
            if (roiO == null || roiO.Length < 4) return ClampXYXY(0, 0, 1, 1, Wc, Hc);
            if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length != 6)
            {
                // 回退：视为同坐标
                return ClampXYXY(roiO[0], roiO[1], roiO[2], roiO[3], Wc, Hc);
            }

            try
            {
                var a = st.AffineMatrix2x3;
                double[] xs = new double[4];
                double[] ys = new double[4];

                var pts = new[]
                {
                    new Point2d(roiO[0], roiO[1]),
                    new Point2d(roiO[2], roiO[1]),
                    new Point2d(roiO[2], roiO[3]),
                    new Point2d(roiO[0], roiO[3]),
                };

                for (int i = 0; i < 4; i++)
                {
                    double x = pts[i].X;
                    double y = pts[i].Y;
                    double nx = a[0] * x + a[1] * y + a[2];
                    double ny = a[3] * x + a[4] * y + a[5];
                    xs[i] = nx;
                    ys[i] = ny;
                }

                double minX = xs[0], maxX = xs[0], minY = ys[0], maxY = ys[0];
                for (int i = 1; i < 4; i++)
                {
                    if (xs[i] < minX) minX = xs[i];
                    if (xs[i] > maxX) maxX = xs[i];
                    if (ys[i] < minY) minY = ys[i];
                    if (ys[i] > maxY) maxY = ys[i];
                }
                return ClampXYXY(minX, minY, maxX, maxY, Wc, Hc);
            }
            catch
            {
                return ClampXYXY(roiO[0], roiO[1], roiO[2], roiO[3], Wc, Hc);
            }
        }

        private static bool TryExtractBboxAabbCurrent(JObject det, out double x1, out double y1, out double x2, out double y2)
        {
            x1 = y1 = x2 = y2 = 0;
            if (det == null) return false;

            // bbox: 约定普通框为 xywh；旋转框为 [cx,cy,w,h] + angle(弧度)
            var bbox = det["bbox"] as JArray;
            if (bbox == null || bbox.Count < 4) return false;

            bool withAngle = det["with_angle"]?.Value<bool?>() ?? false;
            double angle = det["angle"]?.Value<double?>() ?? -100.0;

            if (withAngle && angle != -100.0)
            {
                double cx = SafeToDouble(bbox[0]);
                double cy = SafeToDouble(bbox[1]);
                double w = Math.Abs(SafeToDouble(bbox[2]));
                double h = Math.Abs(SafeToDouble(bbox[3]));
                if (w <= 0 || h <= 0) return false;

                // 角度容错：若像“度数”，则转换为弧度
                double angRad = Math.Abs(angle) > 3.2 ? (angle * Math.PI / 180.0) : angle;
                double c = Math.Cos(angRad);
                double s = Math.Sin(angRad);
                double hw = w / 2.0, hh = h / 2.0;
                double[,] offs = new double[,] { { -hw, -hh }, { hw, -hh }, { hw, hh }, { -hw, hh } };

                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    double dx = offs[k, 0];
                    double dy = offs[k, 1];
                    double px = cx + c * dx - s * dy;
                    double py = cy + s * dx + c * dy;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }
                x1 = minX; y1 = minY; x2 = maxX; y2 = maxY;
                return true;
            }
            else
            {
                // 视为 xywh
                double x = SafeToDouble(bbox[0]);
                double y = SafeToDouble(bbox[1]);
                double bw = Math.Abs(SafeToDouble(bbox[2]));
                double bh = Math.Abs(SafeToDouble(bbox[3]));
                x1 = x;
                y1 = y;
                x2 = x + bw;
                y2 = y + bh;
                return true;
            }
        }

        private static double SafeToDouble(JToken t)
        {
            try { return t != null ? Convert.ToDouble(((JValue)t).Value) : 0.0; } catch { return 0.0; }
        }

        private static int[] MapAabbToOriginalAndClamp(TransformationState st, int[] bboxCur, int W0, int H0)
        {
            if (bboxCur == null || bboxCur.Length < 4) return null;
            if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length != 6) return ClampXYXY(bboxCur[0], bboxCur[1], bboxCur[2], bboxCur[3], W0, H0);

            var inv = TransformationState.Inverse2x3(st.AffineMatrix2x3);
            var pts = new[]
            {
                new Point2d(bboxCur[0], bboxCur[1]),
                new Point2d(bboxCur[2], bboxCur[1]),
                new Point2d(bboxCur[2], bboxCur[3]),
                new Point2d(bboxCur[0], bboxCur[3]),
            };

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < pts.Length; i++)
            {
                double x = pts[i].X, y = pts[i].Y;
                double ox = inv[0] * x + inv[1] * y + inv[2];
                double oy = inv[3] * x + inv[4] * y + inv[5];
                if (ox < minX) minX = ox;
                if (ox > maxX) maxX = ox;
                if (oy < minY) minY = oy;
                if (oy > maxY) maxY = oy;
            }
            return ClampXYXY(minX, minY, maxX, maxY, W0, H0);
        }

        private static int[] ParseGlobalBboxToAabb(JArray gb)
        {
            if (gb == null) return null;
            try
            {
                if (gb.Count == 4)
                {
                    // 容错：当作 xywh（与 C# 常规 bbox 一致）
                    double x = SafeToDouble(gb[0]);
                    double y = SafeToDouble(gb[1]);
                    double w = Math.Abs(SafeToDouble(gb[2]));
                    double h = Math.Abs(SafeToDouble(gb[3]));
                    return new[] { (int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Ceiling(x + w), (int)Math.Ceiling(y + h) };
                }
                if (gb.Count == 5)
                {
                    // [cx,cy,w,h,angle]（angle 多为弧度）
                    double cx = SafeToDouble(gb[0]);
                    double cy = SafeToDouble(gb[1]);
                    double w = Math.Abs(SafeToDouble(gb[2]));
                    double h = Math.Abs(SafeToDouble(gb[3]));
                    double angle = SafeToDouble(gb[4]);
                    if (w <= 0 || h <= 0) return null;
                    double angRad = Math.Abs(angle) > 3.2 ? (angle * Math.PI / 180.0) : angle;
                    double c = Math.Cos(angRad);
                    double s = Math.Sin(angRad);
                    double hw = w / 2.0, hh = h / 2.0;
                    double[,] offs = new double[,] { { -hw, -hh }, { hw, -hh }, { hw, hh }, { -hw, hh } };
                    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                    for (int k = 0; k < 4; k++)
                    {
                        double dx = offs[k, 0];
                        double dy = offs[k, 1];
                        double px = cx + c * dx - s * dy;
                        double py = cy + s * dx + c * dy;
                        if (px < minX) minX = px;
                        if (px > maxX) maxX = px;
                        if (py < minY) minY = py;
                        if (py > maxY) maxY = py;
                    }
                    return new[] { (int)Math.Floor(minX), (int)Math.Floor(minY), (int)Math.Ceiling(maxX), (int)Math.Ceiling(maxY) };
                }
            }
            catch { }
            return null;
        }

        private static bool CheckMaskOverlapWithRegion(Mat maskMat0, int[] bboxXYXY, int[] roiXYXY, int W, int H)
        {
            if (maskMat0 == null || maskMat0.Empty()) return false;
            if (bboxXYXY == null || roiXYXY == null) return false;

            // bbox clamp + 同步裁剪 mask（若 bbox 越界）
            Mat maskMat = maskMat0;
            int[] bb = bboxXYXY;
            try
            {
                var tup = ClampBboxAndCropMask(bb, maskMat0, W, H);
                bb = tup.Item1;
                maskMat = tup.Item2 ?? maskMat0;
            }
            catch
            {
                bb = bboxXYXY;
                maskMat = maskMat0;
            }

            int bw = bb[2] - bb[0];
            int bh = bb[3] - bb[1];
            if (bw <= 0 || bh <= 0) return false;

            // mask resize 到 bbox 尺寸（实例分割常见：mask=32x32，bbox=512x512）
            Mat resized = null;
            Mat workMask = maskMat;
            try
            {
                if (workMask.Rows != bh || workMask.Cols != bw)
                {
                    resized = new Mat();
                    Cv2.Resize(workMask, resized, new Size(bw, bh), 0, 0, InterpolationFlags.Nearest);
                    workMask = resized;
                }
            }
            catch
            {
                if (resized != null) resized.Dispose();
                // resize 失败时降级为 bbox 相交判定（由外层决定）
                return false;
            }

            try
            {
                int ix1 = Math.Max(bb[0], roiXYXY[0]);
                int iy1 = Math.Max(bb[1], roiXYXY[1]);
                int ix2 = Math.Min(bb[2], roiXYXY[2]);
                int iy2 = Math.Min(bb[3], roiXYXY[3]);
                if (ix2 <= ix1 || iy2 <= iy1) return false;

                int lx1 = ix1 - bb[0];
                int ly1 = iy1 - bb[1];
                int lx2 = ix2 - bb[0];
                int ly2 = iy2 - bb[1];
                lx1 = Math.Max(0, Math.Min(bw, lx1));
                ly1 = Math.Max(0, Math.Min(bh, ly1));
                lx2 = Math.Max(lx1 + 1, Math.Min(bw, lx2));
                ly2 = Math.Max(ly1 + 1, Math.Min(bh, ly2));

                var rect = new Rect(lx1, ly1, lx2 - lx1, ly2 - ly1);
                using (var sub = new Mat(workMask, rect))
                {
                    // 任意像素非 0 即认为与 ROI 有重叠
                    return Cv2.CountNonZero(sub) > 0;
                }
            }
            finally
            {
                if (resized != null) resized.Dispose();
            }
        }

        private static Tuple<int[], Mat> ClampBboxAndCropMask(int[] bboxXYXY, Mat maskMat, int W, int H)
        {
            if (bboxXYXY == null || bboxXYXY.Length < 4) return Tuple.Create(bboxXYXY, (Mat)null);
            W = Math.Max(1, W);
            H = Math.Max(1, H);
            int x1 = bboxXYXY[0], y1 = bboxXYXY[1], x2 = bboxXYXY[2], y2 = bboxXYXY[3];
            int nx1 = Math.Max(0, Math.Min(W - 1, x1));
            int ny1 = Math.Max(0, Math.Min(H - 1, y1));
            int nx2 = Math.Max(nx1 + 1, Math.Min(W, x2));
            int ny2 = Math.Max(ny1 + 1, Math.Min(H, y2));

            if (maskMat == null || maskMat.Empty()) return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, (Mat)null);

            try
            {
                int dh0 = y2 - y1;
                int dw0 = x2 - x1;
                if (dh0 <= 0 || dw0 <= 0) return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, (Mat)null);
                if (maskMat.Rows != dh0 || maskMat.Cols != dw0) return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, (Mat)null);

                int cutL = Math.Max(0, nx1 - x1);
                int cutT = Math.Max(0, ny1 - y1);
                int cutR = Math.Max(0, x2 - nx2);
                int cutB = Math.Max(0, y2 - ny2);

                int rw = maskMat.Cols - cutL - cutR;
                int rh = maskMat.Rows - cutT - cutB;
                if (rw <= 0 || rh <= 0) return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, (Mat)null);
                var rect = new Rect(cutL, cutT, rw, rh);
                var cropped = new Mat(maskMat, rect).Clone();
                return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, cropped);
            }
            catch
            {
                return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, (Mat)null);
            }
        }

        private static Mat TryParseMaskArrayToMat(JToken tok)
        {
            // 期望形态：二维数组（行数组），元素可为 0/1/255
            var ja = tok as JArray;
            if (ja == null || ja.Count == 0) return null;
            if (!(ja[0] is JArray row0)) return null;
            int H = ja.Count;
            int W = row0.Count;
            if (H <= 0 || W <= 0) return null;

            var mat = new Mat(H, W, MatType.CV_8UC1);
            for (int y = 0; y < H; y++)
            {
                var row = ja[y] as JArray;
                if (row == null || row.Count != W) { mat.Dispose(); return null; }
                for (int x = 0; x < W; x++)
                {
                    byte v = 0;
                    try
                    {
                        double dv = Convert.ToDouble(((JValue)row[x]).Value);
                        v = (byte)(dv > 0 ? 255 : 0);
                    }
                    catch { v = 0; }
                    mat.Set(y, x, v);
                }
            }
            return mat;
        }
    }
}


