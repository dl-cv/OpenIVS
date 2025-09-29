using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace DlcvModules
{
    /// <summary>
    /// 根据检测结果对图像裁剪，支持轴对齐与旋转框
    /// 输入：image_list (ModuleImage/Bitmap)，result_list（local entries，含 sample_results）
    /// 输出：裁剪得到的新 image_list 与对应的 local result_list（含 transform/index/origin_index）
    /// </summary>
    public class ImageGeneration : BaseModule
    {
        static ImageGeneration()
        {
            ModuleRegistry.Register("features/image_generation", typeof(ImageGeneration));
        }

        public ImageGeneration(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override Tuple<List<object>, List<Dictionary<string, object>>> Process(List<object> imageList = null, List<Dictionary<string, object>> resultList = null)
        {
            var imagesIn = imageList ?? new List<object>();
            var resultsIn = resultList ?? new List<Dictionary<string, object>>();

            // 构建 transform/index 映射，便于按条目裁剪
            var transformKeyToImage = new Dictionary<string, Tuple<ModuleImage, Bitmap, int>>();
            for (int i = 0; i < imagesIn.Count; i++)
            {
                var (wrap, bmp) = UnwrapImage(imagesIn[i]);
                if (bmp == null) continue;
                string key = SerializeTransform(wrap != null ? wrap.TransformState : null, i, wrap != null ? wrap.OriginalIndex : i);
                transformKeyToImage[key] = Tuple.Create(wrap, bmp, i);
            }

            var imagesOut = new List<object>();
            var resultsOut = new List<Dictionary<string, object>>();
            int outIndex = 0;

            foreach (var entry in resultsIn)
            {
                if (entry == null) continue;
                int idx = GetInt(entry, "index", -1);
                int originIndex = GetInt(entry, "origin_index", idx);
                var stateDict = GetDict(entry, "transform");
                var state = stateDict != null ? TransformationState.FromDict(stateDict) : null;
                string key = SerializeTransform(state, idx, originIndex);
                if (!transformKeyToImage.TryGetValue(key, out Tuple<ModuleImage, Bitmap, int> tup))
                {
                    // 回退到 index 匹配
                    key = SerializeTransform(null, idx, originIndex);
                    transformKeyToImage.TryGetValue(key, out tup);
                }
                if (tup == null) continue;

                var sampleResults = GetList(entry, "sample_results");
                if (sampleResults == null || sampleResults.Count == 0) continue;

                foreach (var sr in sampleResults)
                {
                    if (sr == null) continue;
                    // 每个 sr 应为一个对象结果，可能含 bbox、with_angle/angle、with_bbox
                    var bbox = GetDoubleList(sr, "bbox");
                    bool withAngle = GetBool(sr, "with_angle", false);
                    double angle = GetDouble(sr, "angle", -100.0);
                    bool withBbox = GetBool(sr, "with_bbox", bbox != null && bbox.Count > 0);
                    if (!withBbox || bbox == null || bbox.Count < 4) continue;

                    Bitmap cropped = null;
                    double[] childA2x3 = null;
                    int cw, ch;

                    if (withAngle && angle != -100.0)
                    {
                        // 旋转框: bbox=[cx,cy,w,h]，angle为弧度
                        double cx = bbox[0];
                        double cy = bbox[1];
                        double w = bbox[2];
                        double h = bbox[3];
                        cw = Math.Max(1, (int)Math.Round(w));
                        ch = Math.Max(1, (int)Math.Round(h));
                        cropped = CropRotated(tup.Item2, (float)cx, (float)cy, (float)w, (float)h, (float)(angle * 180.0 / Math.PI));

                        // current->child 2x3: 先平移到(-cx,-cy)，再旋转(-angle)，再平移到(w/2,h/2)
                        // R(-a) = [c s; -s c]
                        double c = Math.Cos(-angle), s = Math.Sin(-angle);
                        double tx = (w / 2.0) + (-c * cx - s * cy);
                        double ty = (h / 2.0) + (s * cx - c * cy);
                        childA2x3 = new double[] { c, s, tx, -s, c, ty };
                    }
                    else
                    {
                        // 轴对齐框: bbox=[x,y,w,h]
                        int x = ClampToInt(bbox[0]);
                        int y = ClampToInt(bbox[1]);
                        int w = Math.Max(1, ClampToInt(bbox[2]));
                        int h = Math.Max(1, ClampToInt(bbox[3]));
                        var rect = new Rectangle(x, y, Math.Min(w, Math.Max(1, tup.Item2.Width - x)), Math.Min(h, Math.Max(1, tup.Item2.Height - y)));
                        if (rect.Width <= 0 || rect.Height <= 0) continue;
                        cropped = new Bitmap(rect.Width, rect.Height);
                        using (var g = Graphics.FromImage(cropped))
                        {
                            g.DrawImage(tup.Item2, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
                        }
                        cw = rect.Width;
                        ch = rect.Height;
                        // 平移到左上角
                        childA2x3 = new double[] { 1, 0, -x, 0, 1, -y };
                    }

                    if (cropped == null) continue;
                    // 派生状态
                    var parentWrap = tup.Item1;
                    var parentState = parentWrap != null ? parentWrap.TransformState : new TransformationState(tup.Item2.Width, tup.Item2.Height);
                    var childState = parentState.DeriveChild(childA2x3, cw, ch);
                    var childWrap = new ModuleImage(cropped, parentWrap != null ? parentWrap.OriginalImage : tup.Item2, childState, parentWrap != null ? parentWrap.OriginalIndex : originIndex);
                    imagesOut.Add(childWrap);

                    var outEntry = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    outEntry["type"] = "local";
                    outEntry["index"] = outIndex;
                    outEntry["origin_index"] = parentWrap != null ? parentWrap.OriginalIndex : originIndex;
                    outEntry["transform"] = childState.ToDict();
                    outEntry["sample_results"] = new List<Dictionary<string, object>>();
                    resultsOut.Add(outEntry);
                    outIndex += 1;
                }
            }

            return Tuple.Create(imagesOut, resultsOut);
        }

        private static Tuple<ModuleImage, Bitmap> UnwrapImage(object obj)
        {
            if (obj is ModuleImage mi)
            {
                if (mi.ImageObject is Bitmap bmp1) return Tuple.Create(mi, bmp1);
                return Tuple.Create(mi, mi.ImageObject as Bitmap);
            }
            return Tuple.Create<ModuleImage, Bitmap>(null, obj as Bitmap);
        }

        private static int GetInt(Dictionary<string, object> d, string k, int dv)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return dv;
            try { return Convert.ToInt32(v); } catch { return dv; }
        }

        private static double GetDouble(Dictionary<string, object> d, string k, double dv)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return dv;
            try { return Convert.ToDouble(v); } catch { return dv; }
        }

        private static bool GetBool(Dictionary<string, object> d, string k, bool dv)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return dv;
            try { return Convert.ToBoolean(v); } catch { return dv; }
        }

        private static List<Dictionary<string, object>> GetList(Dictionary<string, object> d, string k)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return null;
            return v as List<Dictionary<string, object>>;
        }

        private static List<double> GetDoubleList(Dictionary<string, object> d, string k)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return null;
            if (v is List<double> dl) return dl;
            if (v is IEnumerable<object> objs)
            {
                var list = new List<double>();
                foreach (var o in objs)
                {
                    try { list.Add(Convert.ToDouble(o)); } catch { }
                }
                return list;
            }
            return null;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string k)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return null;
            return v as Dictionary<string, object>;
        }

        private static string SerializeTransform(TransformationState st, int index, int originIndex)
        {
            if (st == null || st.AffineMatrix2x3 == null)
            {
                return $"idx:{index}|org:{originIndex}|T:null";
            }
            var a = st.AffineMatrix2x3;
            return $"idx:{index}|org:{originIndex}|T:{a[0]:F4},{a[1]:F4},{a[2]:F2},{a[3]:F4},{a[4]:F4},{a[5]:F2}";
        }

        private static int ClampToInt(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)Math.Round(v);
        }

        private static Bitmap CropRotated(Bitmap src, float cx, float cy, float w, float h, float angleDeg)
        {
            int iw = Math.Max(1, (int)Math.Round(w));
            int ih = Math.Max(1, (int)Math.Round(h));
            var dst = new Bitmap(iw, ih);
            using (var g = Graphics.FromImage(dst))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // 让 (cx,cy) 旋转后落在新图中心 (w/2,h/2)
                g.TranslateTransform(w / 2.0f, h / 2.0f);
                g.RotateTransform(angleDeg);
                g.TranslateTransform(-cx, -cy);
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            }
            return dst;
        }
    }

    /// <summary>
    /// 合并多路图像与结果：将主输入和 ExtraInputsIn 的对汇总；按 transform/index/origin_index 对齐结果
    /// 可选去重：当 bbox+category_name 一致时去重
    /// 输出：每张图一个 local 条目
    /// </summary>
    public class MergeResults : BaseModule
    {
        static MergeResults()
        {
            ModuleRegistry.Register("features/merge_results", typeof(MergeResults));
        }

        public MergeResults(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override Tuple<List<object>, List<Dictionary<string, object>>> Process(List<object> imageList = null, List<Dictionary<string, object>> resultList = null)
        {
            var allImages = new List<object>();
            var allResults = new List<Dictionary<string, object>>();

            if (imageList != null) allImages.AddRange(imageList);
            if (resultList != null) allResults.AddRange(resultList);
            if (ExtraInputsIn != null)
            {
                foreach (var ch in ExtraInputsIn)
                {
                    if (ch == null) continue;
                    if (ch.ImageList != null) allImages.AddRange(ch.ImageList);
                    if (ch.ResultList != null) allResults.AddRange(ch.ResultList);
                }
            }

            // 建立 image 键
            var keyToImage = new Dictionary<string, Tuple<ModuleImage, int>>();
            for (int i = 0; i < allImages.Count; i++)
            {
                var (wrap, bmp) = ImageGeneration_Unwrap(allImages[i]);
                if (bmp == null) continue;
                string key = SerializeTransform(wrap != null ? wrap.TransformState : null, i, wrap != null ? wrap.OriginalIndex : i);
                keyToImage[key] = Tuple.Create(wrap, i);
            }

            // 将结果按 key 汇总
            var keyToResults = new Dictionary<string, List<Dictionary<string, object>>>();
            foreach (var r in allResults)
            {
                if (r == null) continue;
                int idx = GetInt(r, "index", -1);
                int originIndex = GetInt(r, "origin_index", idx);
                var stDict = GetDict(r, "transform");
                var st = stDict != null ? TransformationState.FromDict(stDict) : null;
                string key = SerializeTransform(st, idx, originIndex);
                if (!keyToResults.TryGetValue(key, out List<Dictionary<string, object>> list))
                {
                    list = new List<Dictionary<string, object>>();
                    keyToResults[key] = list;
                }
                list.Add(r);
            }

            // 去重选项
            bool dedup = Properties != null && Properties.TryGetValue("deduplicate", out object dv) && Convert.ToBoolean(dv);

            var mergedImages = new List<object>(allImages);
            var mergedResults = new List<Dictionary<string, object>>();

            int newIndex = 0;
            foreach (var kv in keyToImage)
            {
                var wrap = kv.Value.Item1;
                var st = wrap != null ? wrap.TransformState : null;
                var samples = new List<Dictionary<string, object>>();

                if (keyToResults.TryGetValue(kv.Key, out List<Dictionary<string, object>> rs))
                {
                    foreach (var r in rs)
                    {
                        List<Dictionary<string, object>> srs = null;
                        if (r != null && r.TryGetValue("sample_results", out object sv) && sv is List<Dictionary<string, object>> svl)
                        {
                            srs = svl;
                        }
                        if (srs == null) continue;
                        foreach (var o in srs) samples.Add(o);
                    }
                }

                if (dedup && samples.Count > 1)
                {
                    samples = Deduplicate(samples);
                }

                var outEntry = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                outEntry["type"] = "local";
                outEntry["index"] = newIndex;
                outEntry["origin_index"] = wrap != null ? wrap.OriginalIndex : 0;
                outEntry["transform"] = st != null ? (object)st.ToDict() : null;
                outEntry["sample_results"] = samples;
                mergedResults.Add(outEntry);
                newIndex += 1;
            }

            return Tuple.Create(mergedImages, mergedResults);
        }

        private static Tuple<ModuleImage, Bitmap> ImageGeneration_Unwrap(object obj)
        {
            if (obj is ModuleImage mi)
            {
                if (mi.ImageObject is Bitmap bmp1) return Tuple.Create(mi, bmp1);
                return Tuple.Create(mi, mi.ImageObject as Bitmap);
            }
            return Tuple.Create<ModuleImage, Bitmap>(null, obj as Bitmap);
        }

        private static List<Dictionary<string, object>> Deduplicate(List<Dictionary<string, object>> samples)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var outList = new List<Dictionary<string, object>>();
            foreach (var s in samples)
            {
                string cat = s != null && s.TryGetValue("category_name", out object cn) && cn != null ? cn.ToString() : "";
                IEnumerable<object> bbox = null;
                if (s != null && s.TryGetValue("bbox", out object bv) && bv is IEnumerable<object> bl)
                {
                    bbox = bl;
                }
                string bstr = bbox != null ? string.Join(",", bbox.Select(x => SafeToDouble(x).ToString("F3"))) : "";
                string key = cat + "|" + bstr;
                if (set.Add(key)) outList.Add(s);
            }
            return outList;
        }

        private static double SafeToDouble(object x)
        {
            try { return Convert.ToDouble(x); } catch { return 0.0; }
        }

        private static int GetInt(Dictionary<string, object> d, string k, int dv)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return dv;
            try { return Convert.ToInt32(v); } catch { return dv; }
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string k)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return null;
            return v as Dictionary<string, object>;
        }

        private static string SerializeTransform(TransformationState st, int index, int originIndex)
        {
            if (st == null || st.AffineMatrix2x3 == null)
            {
                return $"idx:{index}|org:{originIndex}|T:null";
            }
            var a = st.AffineMatrix2x3;
            return $"idx:{index}|org:{originIndex}|T:{a[0]:F4},{a[1]:F4},{a[2]:F2},{a[3]:F4},{a[4]:F4},{a[5]:F2}";
        }
    }

    /// <summary>
    /// 结果过滤：按 categories 将结果与图像分流为两路；第二路通过 ExtraOutputs[0] 暴露
    /// </summary>
    public class ResultFilter : BaseModule
    {
        static ResultFilter()
        {
            ModuleRegistry.Register("features/result_filter", typeof(ResultFilter));
        }

        public ResultFilter(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override Tuple<List<object>, List<Dictionary<string, object>>> Process(List<object> imageList = null, List<Dictionary<string, object>> resultList = null)
        {
            var inImages = imageList ?? new List<object>();
            var inResults = resultList ?? new List<Dictionary<string, object>>();

            var categories = ReadCategories();
            var keepSet = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);

            var mainImages = new List<object>();
            var mainResults = new List<Dictionary<string, object>>();
            var altImages = new List<object>();
            var altResults = new List<Dictionary<string, object>>();

            // 构建 image 键映射
            var keyToImageObj = new Dictionary<string, object>();
            for (int i = 0; i < inImages.Count; i++)
            {
                var (wrap, bmp) = ImageGeneration_Unwrap(inImages[i]);
                if (bmp == null) continue;
                string key = SerializeTransform(wrap != null ? wrap.TransformState : null, i, wrap != null ? wrap.OriginalIndex : i);
                keyToImageObj[key] = inImages[i];
            }

            foreach (var r in inResults)
            {
                if (r == null) continue;
                int idx = GetInt(r, "index", -1);
                int originIndex = GetInt(r, "origin_index", idx);
                var stDict = GetDict(r, "transform");
                var st = stDict != null ? TransformationState.FromDict(stDict) : null;
                string key = SerializeTransform(st, idx, originIndex);

                // 复制结果，并按 categories 过滤 sample_results
                List<Dictionary<string, object>> srs = null;
                if (r != null && r.TryGetValue("sample_results", out object sv) && sv is List<Dictionary<string, object>> svl)
                {
                    srs = svl;
                }
                var sKeep = new List<Dictionary<string, object>>();
                var sAlt = new List<Dictionary<string, object>>();
                if (srs != null)
                {
                    foreach (var s in srs)
                    {
                        string cat = s != null && s.TryGetValue("category_name", out object cn) && cn != null ? cn.ToString() : "";
                        if (keepSet.Count == 0 || keepSet.Contains(cat)) sKeep.Add(s);
                        else sAlt.Add(s);
                    }
                }

                if (keyToImageObj.TryGetValue(key, out object imgObj))
                {
                    if (sKeep.Count > 0 || srs == null)
                    {
                        mainImages.Add(imgObj);
                        var e = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        e["type"] = "local";
                        e["index"] = mainResults.Count;
                        e["origin_index"] = originIndex;
                        e["transform"] = st != null ? (object)st.ToDict() : null;
                        e["sample_results"] = sKeep;
                        mainResults.Add(e);
                    }
                    if (sAlt.Count > 0)
                    {
                        altImages.Add(imgObj);
                        var e2 = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        e2["type"] = "local";
                        e2["index"] = altResults.Count;
                        e2["origin_index"] = originIndex;
                        e2["transform"] = st != null ? (object)st.ToDict() : null;
                        e2["sample_results"] = sAlt;
                        altResults.Add(e2);
                    }
                }
            }

            // 通过 extra output 暴露第二路
            this.ExtraOutputs.Add(new ModuleChannel(altImages, altResults));
            return Tuple.Create(mainImages, mainResults);
        }

        private List<string> ReadCategories()
        {
            var cats = new List<string>();
            if (Properties != null && Properties.TryGetValue("categories", out object v) && v is IEnumerable<object> objs)
            {
                foreach (var o in objs)
                {
                    if (o == null) continue;
                    var s = o.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) cats.Add(s);
                }
            }
            return cats;
        }

        private static Tuple<ModuleImage, Bitmap> ImageGeneration_Unwrap(object obj)
        {
            if (obj is ModuleImage mi)
            {
                if (mi.ImageObject is Bitmap bmp1) return Tuple.Create(mi, bmp1);
                return Tuple.Create(mi, mi.ImageObject as Bitmap);
            }
            return Tuple.Create<ModuleImage, Bitmap>(null, obj as Bitmap);
        }

        private static int GetInt(Dictionary<string, object> d, string k, int dv)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return dv;
            try { return Convert.ToInt32(v); } catch { return dv; }
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string k)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return null;
            return v as Dictionary<string, object>;
        }

        private static string SerializeTransform(TransformationState st, int index, int originIndex)
        {
            if (st == null || st.AffineMatrix2x3 == null)
            {
                return $"idx:{index}|org:{originIndex}|T:null";
            }
            var a = st.AffineMatrix2x3;
            return $"idx:{index}|org:{originIndex}|T:{a[0]:F4},{a[1]:F4},{a[2]:F2},{a[3]:F4},{a[4]:F4},{a[5]:F2}";
        }
    }
}


