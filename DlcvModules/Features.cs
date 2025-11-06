using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

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

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var imagesIn = imageList ?? new List<ModuleImage>();
            var resultsIn = resultList ?? new JArray();

            // 构建 transform/index 映射，便于按条目裁剪
            var transformKeyToImage = new Dictionary<string, Tuple<ModuleImage, Mat, int>>();
            for (int i = 0; i < imagesIn.Count; i++)
            {
                var (wrap, bmp) = UnwrapImage(imagesIn[i]);
                if (bmp == null || bmp.Empty()) continue;
                string key = SerializeTransform(wrap != null ? wrap.TransformState : null, i, wrap != null ? wrap.OriginalIndex : i);
                transformKeyToImage[key] = Tuple.Create(wrap, bmp, i);
            }

            var imagesOut = new List<ModuleImage>();
            var resultsOut = new JArray();
            int outIndex = 0;

            foreach (var entryToken in resultsIn)
            {
                if (!(entryToken is JObject entry)) continue;
                int idx = entry["index"]?.Value<int?>() ?? -1;
                int originIndex = entry["origin_index"]?.Value<int?>() ?? idx;
                var stateDict = entry["transform"] as JObject;
                var state = stateDict != null ? TransformationState.FromDict(stateDict.ToObject<Dictionary<string, object>>()) : null;
                string key = SerializeTransform(state, idx, originIndex);
                if (!transformKeyToImage.TryGetValue(key, out Tuple<ModuleImage, Mat, int> tup))
                {
                    // 回退到 index 匹配
                    key = SerializeTransform(null, idx, originIndex);
                    transformKeyToImage.TryGetValue(key, out tup);
                }
                if (tup == null) continue;

                var sampleResults = entry["sample_results"] as JArray;
                if (sampleResults == null || sampleResults.Count == 0) continue;

                foreach (var sr in sampleResults)
                {
                    if (sr == null) continue;
                    // 每个 sr 应为一个对象结果，可能含 bbox、with_angle/angle、with_bbox
                    var bbox = sr["bbox"] as JArray;
                    bool withAngle = sr["with_angle"]?.Value<bool>() ?? false;
                    double angle = sr["angle"]?.Value<double?>() ?? -100.0;
                    bool withBbox = sr["with_bbox"]?.Value<bool?>() ?? (bbox != null && bbox.Count > 0);
                    if (!withBbox || bbox == null || bbox.Count < 4) continue;

                    Mat cropped = null;
                    double[] childA2x3 = null;
                    int cw, ch;

                    if (withAngle && angle != -100.0)
                    {
                        // 旋转框: bbox=[cx,cy,w,h]，angle为弧度
                        double cx = bbox[0].Value<double>();
                        double cy = bbox[1].Value<double>();
                        double w = bbox[2].Value<double>();
                        double h = bbox[3].Value<double>();
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
                        int x = ClampToInt(bbox[0].Value<double>());
                        int y = ClampToInt(bbox[1].Value<double>());
                        int w = Math.Max(1, ClampToInt(bbox[2].Value<double>()));
                        int h = Math.Max(1, ClampToInt(bbox[3].Value<double>()));
                        int rw = Math.Min(w, Math.Max(1, tup.Item2.Width - x));
                        int rh = Math.Min(h, Math.Max(1, tup.Item2.Height - y));
                        if (rw <= 0 || rh <= 0) continue;
                        var rect = new OpenCvSharp.Rect(x, y, rw, rh);
                        cropped = new Mat(tup.Item2, rect).Clone();
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

                    var outEntry = new JObject
                    {
                        ["type"] = "local",
                        ["index"] = outIndex,
                        ["origin_index"] = parentWrap != null ? parentWrap.OriginalIndex : originIndex,
                        ["transform"] = JObject.FromObject(childState.ToDict()),
                        ["sample_results"] = new JArray()
                    };
                    resultsOut.Add(outEntry);
                    outIndex += 1;
                }
            }

            return new ModuleIO(imagesOut, resultsOut);
        }

        private static Tuple<ModuleImage, Mat> UnwrapImage(ModuleImage obj)
        {
            if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
            return Tuple.Create(obj, obj.ImageObject);
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

        private static Mat CropRotated(Mat src, float cx, float cy, float w, float h, float angleDeg)
        {
            int iw = Math.Max(1, (int)Math.Round(w));
            int ih = Math.Max(1, (int)Math.Round(h));
            var rotMat = Cv2.GetRotationMatrix2D(new Point2f(cx, cy), angleDeg, 1.0);
            rotMat.Set<double>(0, 2, rotMat.Get<double>(0, 2) + (w / 2.0) - cx);
            rotMat.Set<double>(1, 2, rotMat.Get<double>(1, 2) + (h / 2.0) - cy);
            var dst = new Mat();
            Cv2.WarpAffine(src, dst, rotMat, new Size(iw, ih));
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

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var allImages = new List<ModuleImage>();
            var allResults = new JArray();

            if (imageList != null) allImages.AddRange(imageList);
            if (resultList != null) foreach (var r in resultList) allResults.Add(r);
            if (ExtraInputsIn != null)
            {
                foreach (var ch in ExtraInputsIn)
                {
                    if (ch == null) continue;
                    if (ch.ImageList != null) allImages.AddRange(ch.ImageList);
                    if (ch.ResultList != null) foreach (var r in ch.ResultList) allResults.Add(r);
                }
            }

            // 建立 image 键（仅依据 transform 矩阵；若无矩阵则使用 origin_index）
            var keyToImage = new Dictionary<string, Tuple<ModuleImage, int>>();
            for (int i = 0; i < allImages.Count; i++)
            {
                var (wrap, bmp) = ImageGeneration_Unwrap(allImages[i]);
                if (bmp == null) continue;
                int org = wrap != null ? wrap.OriginalIndex : i;
                string key = SerializeTransformOnly(wrap != null ? wrap.TransformState : null, org);
                keyToImage[key] = Tuple.Create(wrap, i);
            }

            // 将结果按 key 汇总（与上方相同关键字规则）
            var keyToResults = new Dictionary<string, List<JObject>>();
            foreach (var t in allResults)
            {
                var r = t as JObject;
                if (r == null) continue;
                int idx = r["index"]?.Value<int?>() ?? -1;
                int originIndex = r["origin_index"]?.Value<int?>() ?? idx;
                var stDict = r["transform"] as JObject;
                TransformationState st = null;
                try
                {
                    if (stDict != null)
                    {
                        // 兼容 JArray 字段的 FromDict 解析
                        st = TransformationState.FromDict(stDict.ToObject<Dictionary<string, object>>());
                    }
                }
                catch
                {
                    st = null;
                }
                string key = SerializeTransformOnly(st, originIndex);
                if (!keyToResults.TryGetValue(key, out List<JObject> list))
                {
                    list = new List<JObject>();
                    keyToResults[key] = list;
                }
                list.Add(r);
            }

            // 去重选项
            bool dedup = Properties != null && Properties.TryGetValue("deduplicate", out object dv) && Convert.ToBoolean(dv);

            var mergedImages = new List<ModuleImage>(allImages);
            var mergedResults = new JArray();

            int newIndex = 0;
            foreach (var kv in keyToImage)
            {
                var wrap = kv.Value.Item1;
                var st = wrap != null ? wrap.TransformState : null;
                var samples = new List<JObject>();

                if (keyToResults.TryGetValue(kv.Key, out List<JObject> rs))
                {
                    foreach (var r in rs)
                    {
                        var srs = r["sample_results"] as JArray;
                        if (srs == null) continue;
                        foreach (var o in srs) if (o is JObject oj) samples.Add(oj);
                    }
                }

                if (dedup && samples.Count > 1)
                {
                    samples = Deduplicate(samples);
                }

                var outEntry = new JObject
                {
                    ["type"] = "local",
                    ["index"] = newIndex,
                    ["origin_index"] = wrap != null ? wrap.OriginalIndex : 0,
                    ["transform"] = st != null ? JObject.FromObject(st.ToDict()) : null,
                    ["sample_results"] = new JArray(samples)
                };
                mergedResults.Add(outEntry);
                newIndex += 1;
            }

            return new ModuleIO(mergedImages, mergedResults);
        }

        private static Tuple<ModuleImage, Mat> ImageGeneration_Unwrap(ModuleImage obj)
        {
            if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
            return Tuple.Create(obj, obj.ImageObject);
        }

        private static List<JObject> Deduplicate(List<JObject> samples)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var outList = new List<JObject>();
            foreach (var s in samples)
            {
                string cat = s?["category_name"]?.ToString() ?? "";
                var bboxToken = s?["bbox"];
                string bstr = "";
                if (bboxToken is JArray bl)
                {
                    var nums = bl.Select(x => SafeToDouble(((JValue)x).Value)).ToList();
                    bstr = string.Join(",", nums.Select(v => v.ToString("F3")));
                }
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

        private static string SerializeTransformOnly(TransformationState st, int originIndex)
        {
            if (st == null || st.AffineMatrix2x3 == null)
            {
                return $"org:{originIndex}|T:null";
            }
            var a = st.AffineMatrix2x3;
            return $"T:{a[0]:F4},{a[1]:F4},{a[2]:F2},{a[3]:F4},{a[4]:F4},{a[5]:F2}";
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

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var inImages = imageList ?? new List<ModuleImage>();
            var inResults = resultList ?? new JArray();

            var categories = ReadCategories();
            var keepSet = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);

            var mainImages = new List<ModuleImage>();
            var mainResults = new JArray();
            var altImages = new List<ModuleImage>();
            var altResults = new JArray();

            // 构建 image 键映射
            var keyToImageObj = new Dictionary<string, ModuleImage>();
            for (int i = 0; i < inImages.Count; i++)
            {
                var (wrap, bmp) = ImageGeneration_Unwrap(inImages[i]);
                if (bmp == null) continue;
                string key = SerializeTransform(wrap != null ? wrap.TransformState : null, i, wrap != null ? wrap.OriginalIndex : i);
                keyToImageObj[key] = inImages[i];
            }

            foreach (var t in inResults)
            {
                var r = t as JObject;
                if (r == null) continue;
                int idx = r["index"]?.Value<int?>() ?? -1;
                int originIndex = r["origin_index"]?.Value<int?>() ?? idx;
                var stDict = r["transform"] as JObject;
                var st = stDict != null ? TransformationState.FromDict(stDict.ToObject<Dictionary<string, object>>()) : null;
                string key = SerializeTransform(st, idx, originIndex);

                // 复制结果，并按 categories 过滤 sample_results
                var srsArray = r["sample_results"] as JArray;
                var sKeep = new List<JObject>();
                var sAlt = new List<JObject>();
                if (srsArray != null)
                {
                    foreach (var sToken in srsArray)
                    {
                        if (!(sToken is JObject s)) continue;
                        string cat = s["category_name"]?.ToString() ?? "";
                        if (keepSet.Count == 0 || keepSet.Contains(cat)) sKeep.Add(s);
                        else sAlt.Add(s);
                    }
                }

                if (keyToImageObj.TryGetValue(key, out ModuleImage imgObj))
                {
                    if (sKeep.Count > 0 || srsArray == null)
                    {
                        mainImages.Add(imgObj);
                        var e = new JObject
                        {
                            ["type"] = "local",
                            ["index"] = mainResults.Count,
                            ["origin_index"] = originIndex,
                            ["transform"] = st != null ? JObject.FromObject(st.ToDict()) : null,
                            ["sample_results"] = new JArray(sKeep)
                        };
                        mainResults.Add(e);
                    }
                    if (sAlt.Count > 0)
                    {
                        altImages.Add(imgObj);
                        var e2 = new JObject
                        {
                            ["type"] = "local",
                            ["index"] = altResults.Count,
                            ["origin_index"] = originIndex,
                            ["transform"] = st != null ? JObject.FromObject(st.ToDict()) : null,
                            ["sample_results"] = new JArray(sAlt)
                        };
                        altResults.Add(e2);
                    }
                }
            }

            // 通过 extra output 暴露第二路
            this.ExtraOutputs.Add(new ModuleChannel(altImages, altResults));

            // 衍生布尔标量：是否存在保留类别（用于 D 面 OK/NG 判定）
            try
            {
                bool hasPositive = false;
                foreach (var t in mainResults)
                {
                    var e = t as JObject; if (e == null) continue;
                    var srs = e["sample_results"] as JArray;
                    if (srs != null && srs.Count > 0) { hasPositive = true; break; }
                }
                // 输出到 ScalarOutputsByName，供执行器写入标量出口
                this.ScalarOutputsByName["has_positive"] = hasPositive;
            }
            catch { }

            return new ModuleIO(mainImages, mainResults);
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

        private static Tuple<ModuleImage, Mat> ImageGeneration_Unwrap(ModuleImage obj)
        {
            if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
            return Tuple.Create(obj, obj.ImageObject);
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
    /// 高级结果过滤：支持按 bbox/rbox 的 w/h、bbox 面积、mask 面积过滤。
    /// 通过主通道输出通过项；通过 ExtraOutputs[0] 输出未通过项；同时保持图像对齐。
    /// properties:
    /// - enable_bbox_wh(bool), enable_rbox_wh(bool), enable_bbox_area(bool), enable_mask_area(bool)
    /// - bbox_w_min/max, bbox_h_min/max
    /// - rbox_w_min/max, rbox_h_min/max
    /// - bbox_area_min/max, mask_area_min/max
    /// </summary>
    public class ResultFilterAdvanced : BaseModule
    {
        static ResultFilterAdvanced()
        {
            ModuleRegistry.Register("features/result_filter_advanced", typeof(ResultFilterAdvanced));
        }

        public ResultFilterAdvanced(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var inImages = imageList ?? new List<ModuleImage>();
            var inResults = resultList ?? new JArray();

            var mainImages = new List<ModuleImage>();
            var mainResults = new JArray();
            var altImages = new List<ModuleImage>();
            var altResults = new JArray();

            // 读取配置
            bool enableBBoxWh = ReadBool("enable_bbox_wh", false);
            bool enableRBoxWh = ReadBool("enable_rbox_wh", false);
            bool enableBBoxArea = ReadBool("enable_bbox_area", false);
            bool enableMaskArea = ReadBool("enable_mask_area", false);

            double? bboxWMin = ReadNullableDouble("bbox_w_min");
            double? bboxWMax = ReadNullableDouble("bbox_w_max");
            double? bboxHMin = ReadNullableDouble("bbox_h_min");
            double? bboxHMax = ReadNullableDouble("bbox_h_max");

            double? rboxWMin = ReadNullableDouble("rbox_w_min");
            double? rboxWMax = ReadNullableDouble("rbox_w_max");
            double? rboxHMin = ReadNullableDouble("rbox_h_min");
            double? rboxHMax = ReadNullableDouble("rbox_h_max");

            double? bboxAreaMin = ReadNullableDouble("bbox_area_min");
            double? bboxAreaMax = ReadNullableDouble("bbox_area_max");
            double? maskAreaMin = ReadNullableDouble("mask_area_min");
            double? maskAreaMax = ReadNullableDouble("mask_area_max");

            // 构建 image 键映射：仅 transform；空则退回 origin_index
            var keyToImageObj = new Dictionary<string, ModuleImage>();
            for (int i = 0; i < inImages.Count; i++)
            {
                var tup = RFAdv_Unwrap(inImages[i]);
                var wrap = tup.Item1; var bmp = tup.Item2;
                if (bmp == null) continue;
                int org = wrap != null ? wrap.OriginalIndex : i;
                string key = SerializeTransformOnly(wrap != null ? wrap.TransformState : null, org);
                keyToImageObj[key] = inImages[i];
            }

            foreach (var t in inResults)
            {
                var r = t as JObject;
                if (r == null) continue;
                int idx = r["index"]?.Value<int?>() ?? -1;
                int originIndex = r["origin_index"]?.Value<int?>() ?? idx;
                var stDict = r["transform"] as JObject;
                TransformationState st = null;
                try { if (stDict != null) st = TransformationState.FromDict(stDict.ToObject<Dictionary<string, object>>()); } catch { st = null; }
                string key = SerializeTransformOnly(st, originIndex);

                var srsArray = r["sample_results"] as JArray;
                var passList = new List<JObject>();
                var failList = new List<JObject>();

                if (srsArray != null)
                {
                    foreach (var sToken in srsArray)
                    {
                        if (!(sToken is JObject s)) continue;

                        bool withAngle = s.Value<bool?>("with_angle") ?? false;
                        var bbox = s["bbox"] as JArray;
                        double w = 0.0, h = 0.0;
                        bool hasWH = false;
                        if (bbox != null && bbox.Count >= 4)
                        {
                            try
                            {
                                if (withAngle)
                                {
                                    // 旋转框：bbox=[cx,cy,w,h]
                                    w = Math.Abs(bbox[2].Value<double>());
                                    h = Math.Abs(bbox[3].Value<double>());
                                }
                                else
                                {
                                    // 轴对齐：bbox=[x,y,w,h]
                                    w = Math.Abs(bbox[2].Value<double>());
                                    h = Math.Abs(bbox[3].Value<double>());
                                }
                                hasWH = true;
                            }
                            catch { hasWH = false; }
                        }

                        // 面积
                        double bboxArea = hasWH ? (w * h) : 0.0;
                        double maskArea = ReadMaskPolygonArea(s);

                        bool pass = true;
                        if (enableBBoxWh && hasWH)
                        {
                            pass = pass && InRange(w, bboxWMin, bboxWMax) && InRange(h, bboxHMin, bboxHMax);
                        }
                        if (enableRBoxWh && hasWH && withAngle)
                        {
                            pass = pass && InRange(w, rboxWMin, rboxWMax) && InRange(h, rboxHMin, rboxHMax);
                        }
                        if (enableBBoxArea && hasWH)
                        {
                            pass = pass && InRange(bboxArea, bboxAreaMin, bboxAreaMax);
                        }
                        if (enableMaskArea && maskArea > 0)
                        {
                            pass = pass && InRange(maskArea, maskAreaMin, maskAreaMax);
                        }

                        if (pass) passList.Add(s);
                        else failList.Add(s);
                    }
                }

                if (keyToImageObj.TryGetValue(key, out ModuleImage imgObj))
                {
                    if (passList.Count > 0 || srsArray == null)
                    {
                        mainImages.Add(imgObj);
                        var e = new JObject
                        {
                            ["type"] = "local",
                            ["index"] = mainResults.Count,
                            ["origin_index"] = originIndex,
                            ["transform"] = st != null ? JObject.FromObject(st.ToDict()) : null,
                            ["sample_results"] = new JArray(passList)
                        };
                        mainResults.Add(e);
                    }
                    if (failList.Count > 0)
                    {
                        altImages.Add(imgObj);
                        var e2 = new JObject
                        {
                            ["type"] = "local",
                            ["index"] = altResults.Count,
                            ["origin_index"] = originIndex,
                            ["transform"] = st != null ? JObject.FromObject(st.ToDict()) : null,
                            ["sample_results"] = new JArray(failList)
                        };
                        altResults.Add(e2);
                    }
                }
            }

            // 通过 extra output 暴露第二路（未通过项）
            this.ExtraOutputs.Add(new ModuleChannel(altImages, altResults));
            return new ModuleIO(mainImages, mainResults);
        }

        private static bool InRange(double v, double? minV, double? maxV)
        {
            if (minV.HasValue && v < minV.Value) return false;
            if (maxV.HasValue && v > maxV.Value) return false;
            return true;
        }

        private static double ReadMaskPolygonArea(JObject s)
        {
            try
            {
                var maskToken = s["mask"] ?? s["polygon"];
                var pts = new List<System.Drawing.PointF>();
                if (maskToken is JArray arr)
                {
                    foreach (var p in arr)
                    {
                        if (p is JObject pj)
                        {
                            float x = pj.Value<float>("x");
                            float y = pj.Value<float>("y");
                            pts.Add(new System.Drawing.PointF(x, y));
                        }
                        else if (p is JArray pa && pa.Count >= 2)
                        {
                            float x = pa[0].Value<float>();
                            float y = pa[1].Value<float>();
                            pts.Add(new System.Drawing.PointF(x, y));
                        }
                    }
                }
                if (pts.Count < 3) return 0.0;
                // Shoelace formula
                double area = 0.0;
                for (int i = 0; i < pts.Count; i++)
                {
                    int j = (i + 1) % pts.Count;
                    area += pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
                }
                return Math.Abs(area) * 0.5;
            }
            catch { return 0.0; }
        }

        private bool ReadBool(string key, bool dv)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                try { return Convert.ToBoolean(v); } catch { return dv; }
            }
            return dv;
        }

        private double? ReadNullableDouble(string key)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                double x;
                if (double.TryParse(v.ToString(), out x)) return x;
            }
            return null;
        }

        private static string SerializeTransformOnly(TransformationState st, int originIndex)
        {
            if (st == null || st.AffineMatrix2x3 == null)
            {
                return $"org:{originIndex}|T:null";
            }
            var a = st.AffineMatrix2x3;
            return $"T:{a[0]:F4},{a[1]:F4},{a[2]:F2},{a[3]:F4},{a[4]:F4},{a[5]:F2}";
        }

        private static Tuple<ModuleImage, Mat> RFAdv_Unwrap(ModuleImage obj)
        {
            if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
            return Tuple.Create(obj, obj.ImageObject);
        }
    }

    /// <summary>
    /// 固定坐标裁剪：按 x,y,w,h 从每张输入图像裁剪，结果透传。
    /// 注册名：features/coordinate_crop
    /// properties:
    /// - x(int), y(int), w(int>=1), h(int>=1)
    /// - reference_image_path(string，可选，仅前端可用，不参与逻辑)
    /// </summary>
    public class CoordinateCrop : BaseModule
    {
        static CoordinateCrop()
        {
            ModuleRegistry.Register("features/coordinate_crop", typeof(CoordinateCrop));
        }

        public CoordinateCrop(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

            // 读取属性并规范化
            int x = ReadIntLike("x", 0);
            int y = ReadIntLike("y", 0);
            int w = Math.Max(1, ReadIntLike("w", 100));
            int h = Math.Max(1, ReadIntLike("h", 100));

            var outImages = new List<ModuleImage>();

            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null || wrap.ImageObject == null || wrap.ImageObject.Empty()) continue;
                var baseMat = wrap.ImageObject;
                int W = Math.Max(1, baseMat.Width);
                int H = Math.Max(1, baseMat.Height);

                // clamp 到图像范围
                int x0 = Math.Max(0, Math.Min(W - 1, x));
                int y0 = Math.Max(0, Math.Min(H - 1, y));
                int w0 = Math.Max(1, w);
                int h0 = Math.Max(1, h);

                int x1 = Math.Min(W, x0 + w0);
                int y1 = Math.Min(H, y0 + h0);
                if (x1 <= x0) x1 = Math.Min(W, x0 + 1);
                if (y1 <= y0) y1 = Math.Min(H, y0 + 1);

                int cw = Math.Max(1, x1 - x0);
                int ch = Math.Max(1, y1 - y0);

                var rect = new OpenCvSharp.Rect(x0, y0, cw, ch);
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                var crop = new Mat(baseMat, rect).Clone();

                // 平移矩阵：将裁剪区域左上角移动到 (0,0)
                var trans = new double[] { 1, 0, -x0, 0, 1, -y0 };
                var parentState = wrap.TransformState ?? new TransformationState(W, H);
                var childState = parentState.DeriveChild(trans, cw, ch);

                var child = new ModuleImage(crop, wrap.OriginalImage ?? baseMat, childState, wrap.OriginalIndex);
                outImages.Add(child);
            }

            return new ModuleIO(outImages, results);
        }

        private int ReadIntLike(string key, int dv)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                try
                {
                    // 兼容传入为字符串/浮点/整数
                    return Convert.ToInt32(Convert.ToDouble(v));
                }
                catch { return dv; }
            }
            return dv;
        }
    }
}