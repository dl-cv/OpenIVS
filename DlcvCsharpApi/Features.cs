using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    /// <summary>
    /// 模块名称：生成裁剪
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

            // 与 Python 侧一致的裁剪参数
            double cropExpand = 0.0;
            int? cropW = null;
            int? cropH = null;
            int minSize = 1;
            if (Properties != null)
            {
                cropExpand = GetDouble(Properties, "crop_expand", 0.0);
                minSize = Math.Max(1, GetInt(Properties, "min_size", 1));

                try
                {
                    if (Properties.TryGetValue("crop_shape", out object cs) && cs != null)
                    {
                        var arr = cs as System.Collections.IEnumerable;
                        if (!(cs is string) && arr != null)
                        {
                            var list = new List<int>();
                            foreach (var o in arr)
                            {
                                if (o == null) continue;
                                try { list.Add(Convert.ToInt32(Convert.ToDouble(o))); } catch { }
                                if (list.Count >= 2) break;
                            }
                            if (list.Count >= 2)
                            {
                                cropW = list[0];
                                cropH = list[1];
                            }
                        }
                    }
                }
                catch
                {
                    cropW = null;
                    cropH = null;
                }
            }

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
                        // 与 Python 一致：支持 crop_expand / crop_shape / min_size，输出尺寸使用 int(...) 截断
                        double cx = bbox[0].Value<double>();
                        double cy = bbox[1].Value<double>();
                        double w = Math.Abs(bbox[2].Value<double>());
                        double h = Math.Abs(bbox[3].Value<double>());

                        double w2 = cropW.HasValue && cropH.HasValue ? (double)cropW.Value : Math.Max((double)minSize, w + 2.0 * cropExpand);
                        double h2 = cropW.HasValue && cropH.HasValue ? (double)cropH.Value : Math.Max((double)minSize, h + 2.0 * cropExpand);

                        int iw = Math.Max(minSize, (int)w2);
                        int ih = Math.Max(minSize, (int)h2);

                        var rotMat = Cv2.GetRotationMatrix2D(new Point2f((float)cx, (float)cy), (float)(angle * 180.0 / Math.PI), 1.0);
                        rotMat.Set<double>(0, 2, rotMat.Get<double>(0, 2) + (w2 / 2.0) - cx);
                        rotMat.Set<double>(1, 2, rotMat.Get<double>(1, 2) + (h2 / 2.0) - cy);

                        var dst = new Mat();
                        Cv2.WarpAffine(tup.Item2, dst, rotMat, new Size(iw, ih));
                        cropped = dst;

                        cw = iw;
                        ch = ih;
                        childA2x3 = new double[]
                        {
                            rotMat.Get<double>(0, 0), rotMat.Get<double>(0, 1), rotMat.Get<double>(0, 2),
                            rotMat.Get<double>(1, 0), rotMat.Get<double>(1, 1), rotMat.Get<double>(1, 2),
                        };
                    }
                    else
                    {
                        // 轴对齐框: 输入 bbox 为 xywh，但按要求先转换成 Python 语义的 xyxy，再按 Python 的方式裁剪
                        // Python 规则：xyxy 的 x2/y2 为 end-exclusive；裁剪用 [y1:y2, x1:x2]
                        int W = tup.Item2.Width;
                        int H = tup.Item2.Height;

                        double x = bbox[0].Value<double>();
                        double y = bbox[1].Value<double>();
                        double bw = Math.Abs(bbox[2].Value<double>());
                        double bh = Math.Abs(bbox[3].Value<double>());

                        double x1 = x;
                        double y1 = y;
                        double x2 = x + bw;
                        double y2 = y + bh;

                        int nx1, ny1, nx2, ny2;
                        if (cropW.HasValue && cropH.HasValue)
                        {
                            // 固定尺寸：以中心点裁剪（与 Python 一致，左上 int(...) 相当于 floor）
                            double cx = (x1 + x2) / 2.0;
                            double cy = (y1 + y2) / 2.0;
                            double tx1 = Math.Max(0.0, Math.Min((double)W, cx - cropW.Value / 2.0));
                            double ty1 = Math.Max(0.0, Math.Min((double)H, cy - cropH.Value / 2.0));
                            nx1 = (int)Math.Floor(tx1);
                            ny1 = (int)Math.Floor(ty1);
                            nx2 = (int)Math.Max(nx1 + 1, Math.Min(W, nx1 + cropW.Value));
                            ny2 = (int)Math.Max(ny1 + 1, Math.Min(H, ny1 + cropH.Value));
                        }
                        else
                        {
                            // 外扩：左上 int(...)（floor），右下 round 后再 clamp（与 Python 一致）
                            double tx1 = Math.Max(0.0, Math.Min((double)W, x1 - cropExpand));
                            double ty1 = Math.Max(0.0, Math.Min((double)H, y1 - cropExpand));
                            nx1 = (int)Math.Floor(tx1);
                            ny1 = (int)Math.Floor(ty1);

                            int rx2 = (int)Math.Round(x2 + cropExpand, MidpointRounding.ToEven);
                            int ry2 = (int)Math.Round(y2 + cropExpand, MidpointRounding.ToEven);
                            nx2 = Math.Min(W, Math.Max(0, rx2));
                            ny2 = Math.Min(H, Math.Max(0, ry2));
                            nx2 = Math.Max(nx1 + minSize, nx2);
                            ny2 = Math.Max(ny1 + minSize, ny2);
                        }

                        // 最终 clamp，保持 end-exclusive 语义：nx2/ny2 允许等于 W/H
                        nx1 = Math.Max(0, Math.Min(W, nx1));
                        ny1 = Math.Max(0, Math.Min(H, ny1));
                        nx2 = Math.Max(nx1 + 1, Math.Min(W, nx2));
                        ny2 = Math.Max(ny1 + 1, Math.Min(H, ny2));

                        int rw = nx2 - nx1;
                        int rh = ny2 - ny1;
                        if (rw <= 0 || rh <= 0) continue;

                        var rect = new OpenCvSharp.Rect(nx1, ny1, rw, rh);
                        cropped = new Mat(tup.Item2, rect).Clone();
                        cw = rect.Width;
                        ch = rect.Height;
                        // 平移到左上角（与 Python 的 translation_mat 一致）
                        childA2x3 = new double[] { 1, 0, -nx1, 0, 1, -ny1 };
                    }

                    if (cropped == null) continue;
                    
                    if (GlobalDebug.PrintDebug)
                    {
                        GlobalDebug.Log($"[ImageGeneration] 输入图像尺寸: {tup.Item2.Width}x{tup.Item2.Height}, 裁剪后的图像尺寸: {cropped.Width}x{cropped.Height}");
                    }

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
    /// 模块名称：镜像翻转
    /// 镜像翻转：支持水平（左右）与竖直（上下）翻转；仅处理图像并只输出图像。
    /// 输入：image；输出：image；不透传 results。
    /// properties:
    /// - direction: str 翻转方向，仅支持中文："水平"（左右）或 "竖直"（上下），默认 "水平"
    /// </summary>
    public class ImageFlip : BaseModule
    {
        static ImageFlip()
        {
            ModuleRegistry.Register("features/image_flip", typeof(ImageFlip));
        }

        public ImageFlip(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            if (images.Count == 0) return new ModuleIO(new List<ModuleImage>(), new JArray());

            string dirStr = "水平";
            if (Properties != null && Properties.TryGetValue("direction", out object v) && v != null)
            {
                dirStr = v.ToString();
            }
            string direction = "horizontal";
            if (dirStr.Contains("竖直") || dirStr.Contains("vertical")) direction = "vertical";

            var outImages = new List<ModuleImage>();

            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null || wrap.ImageObject == null || wrap.ImageObject.Empty()) continue;

                var baseImg = wrap.ImageObject;
                int w = baseImg.Width;
                int h = baseImg.Height;

                double[] A;
                if (direction == "vertical")
                {
                    // 上下翻转：y' = h-1 - y
                    A = new double[] { 1.0, 0.0, 0.0, 0.0, -1.0, h - 1.0 };
                }
                else
                {
                    // 水平翻转：x' = w-1 - x
                    A = new double[] { -1.0, 0.0, w - 1.0, 0.0, 1.0, 0.0 };
                }

                Mat flipped = new Mat();
                try
                {
                    var matA = new Mat(2, 3, MatType.CV_64FC1);
                    matA.Set(0, 0, A[0]); matA.Set(0, 1, A[1]); matA.Set(0, 2, A[2]);
                    matA.Set(1, 0, A[3]); matA.Set(1, 1, A[4]); matA.Set(1, 2, A[5]);
                    Cv2.WarpAffine(baseImg, flipped, matA, new Size(w, h));
                }
                catch
                {
                    FlipMode mode = direction == "vertical" ? FlipMode.X : FlipMode.Y; // OpenCV FlipMode.X is vertical (around x-axis)? No.
                    // FlipMode.X means flip around X-axis -> Vertical flip.
                    // FlipMode.Y means flip around Y-axis -> Horizontal flip.
                    Cv2.Flip(baseImg, flipped, mode);
                }

                var parentState = wrap.TransformState ?? new TransformationState(w, h);
                var childState = parentState.DeriveChild(A, w, h);
                var child = new ModuleImage(flipped, wrap.OriginalImage ?? baseImg, childState, wrap.OriginalIndex);
                outImages.Add(child);
            }

            // 不透传 results
            return new ModuleIO(outImages, new JArray());
        }
    }

    /// <summary>
    /// 模块名称：掩码转最小外接矩
    /// 根据 2D mask 生成最小外接矩旋转框（le90），替换原 bbox；image 透传。
    /// </summary>
    public class MaskToRBox : BaseModule
    {
        static MaskToRBox()
        {
            ModuleRegistry.Register("post_process/mask_to_rbox", typeof(MaskToRBox));
            ModuleRegistry.Register("features/mask_to_rbox", typeof(MaskToRBox));
        }

        public MaskToRBox(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();
            var outResults = new JArray();

            foreach (var entryToken in results)
            {
                if (!(entryToken is JObject entry) || entry["type"]?.ToString() != "local")
                {
                    outResults.Add(entryToken);
                    continue;
                }

                var dets = entry["sample_results"] as JArray;
                if (dets == null)
                {
                    outResults.Add(entryToken);
                    continue;
                }

                var newDets = new JArray();
                foreach (var dToken in dets)
                {
                    if (!(dToken is JObject d)) continue;

                    // 检查 mask_rle
                    var maskRleToken = d["mask_rle"];
                    if (maskRleToken == null)
                    {
                        // 无 mask_rle，跳过
                        continue;
                    }

                    // 检查 bbox
                    var bbox = d["bbox"] as JArray;
                    if (bbox == null || bbox.Count < 4) continue;

                    float bx = bbox[0].Value<float>();
                    float by = bbox[1].Value<float>();

                    // 直接通过 mask 计算最小外接矩
                    RotatedRect rr;
                    try
                    {
                        using (var maskMat = MaskRleUtils.MaskInfoToMat(maskRleToken))
                        using (var points = new Mat())
                        {
                            if (maskMat == null || maskMat.Empty()) continue;
                            Cv2.FindNonZero(maskMat, points);
                            if (points.Empty()) continue;
                            rr = Cv2.MinAreaRect(points);
                        }
                    }
                    catch { continue; }

                    // 加上偏移（mask 坐标是相对于 bbox 左上角的）
                    rr.Center += new Point2f(bx, by);

                    float rw = rr.Size.Width;
                    float rh = rr.Size.Height;
                    float angDeg = rr.Angle;

                    if (GlobalDebug.PrintDebug)
                    {
                        GlobalDebug.Log($"[MaskToRBox] bbox大小: {rw:F2}x{rh:F2}, 最小外接矩坐标: ({rr.Center.X:F2},{rr.Center.Y:F2}), 宽高: {rw:F2}x{rh:F2}, 角度: {angDeg:F2}");
                    }

                    // 转换为 le90：长边在前
                    if (rw < rh)
                    {
                        float tmp = rw; rw = rh; rh = tmp;
                        angDeg += 90.0f;
                    }

                    // 角度转弧度并归一到 [-PI/2, PI/2)
                    double angRad = angDeg * Math.PI / 180.0;
                    angRad = NormalizeAngleLe90Rad(angRad);

                    var d2 = (JObject)d.DeepClone();
                    d2["bbox"] = new JArray(rr.Center.X, rr.Center.Y, rw, rh, angRad);
                    d2["with_angle"] = true;
                    d2["angle"] = angRad;
                    d2.Remove("mask_rle"); // 移除 mask_rle 以减小体积
                    d2.Remove("mask");

                    newDets.Add(d2);
                }

                var entry2 = (JObject)entry.DeepClone();
                entry2["sample_results"] = newDets;
                outResults.Add(entry2);
            }

            return new ModuleIO(images, outResults);
        }

        private static double NormalizeAngleLe90Rad(double aRad)
        {
            double x = aRad;
            x = (x + Math.PI / 2.0) % Math.PI - Math.PI / 2.0;
            return x;
        }
    }

    /// <summary>
    /// 模块名称：结果合并
    /// 合并多路图像与结果：将主输入和 ExtraInputsIn 的对汇总；按 transform/index/origin_index 对齐结果
    /// 可选去重：当 bbox+category_name 一致时去重
    /// 输出：每张图一个 local 条目
    /// </summary>
    public class MergeResults : BaseModule
    {
        static MergeResults()
        {
            ModuleRegistry.Register("post_process/merge_results", typeof(MergeResults));
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
    /// 模块名称：结果筛选
    /// 结果过滤：按 categories 将结果与图像分流为两路；第二路通过 ExtraOutputs[0] 暴露
    /// </summary>
    public class ResultFilter : BaseModule
    {
        static ResultFilter()
        {
            ModuleRegistry.Register("post_process/result_filter", typeof(ResultFilter));
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

            if (GlobalDebug.PrintDebug)
            {
                var cats = new List<string>();
                foreach (var t in inResults)
                {
                    if (t is JObject r && r["sample_results"] is JArray srs)
                    {
                        foreach (var s in srs)
                        {
                            if (s is JObject so) cats.Add(so["category_name"]?.ToString() ?? "");
                        }
                    }
                }
                GlobalDebug.Log($"[ResultFilter] 筛选前 category_name 列表: {string.Join(", ", cats)}");
            }

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

            if (GlobalDebug.PrintDebug)
            {
                var cats = new List<string>();
                foreach (var t in mainResults)
                {
                    if (t is JObject r && r["sample_results"] is JArray srs)
                    {
                        foreach (var s in srs)
                        {
                            if (s is JObject so) cats.Add(so["category_name"]?.ToString() ?? "");
                        }
                    }
                }
                GlobalDebug.Log($"[ResultFilter] 筛选后 category_name 列表: {string.Join(", ", cats)}");
            }

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
    /// 模块名称：结果过滤（高级）
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
            ModuleRegistry.Register("post_process/result_filter_advanced", typeof(ResultFilterAdvanced));
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
                        double maskArea = MaskRleUtils.CalculateMaskArea(s["mask_rle"]);

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
    /// 模块名称：固定裁剪
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
            ModuleRegistry.Register("pre_process/coordinate_crop", typeof(CoordinateCrop));
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

    /// <summary>
    /// 模块名称：分类矫正
    /// features/image_rotate_by_cls：根据分类结果对图像做整图旋转，并同步更新旋转框/检测结果的几何与 transform。
    /// 使用场景：上游分类结果给出方向类别（例如 “90/180/270/0” 的自定义名称），且已经存在对应图像的检测/旋转框结果。
    /// 规则：
    /// - 主对：image/result_list 为检测或旋转框结果；
    /// - 额外输入对0（ExtraInputsIn[0]）：result_list 作为分类结果，只用于决定旋转角度，不会在输出中透传；
    /// - 逐对匹配优先级：transform 完全匹配 > index 匹配 > origin_index 匹配；均无则视为 0°；
    /// - 每张图最多匹配一个结果，且仅进行一种变换；
    /// - 若判定应旋转 0°，图像本身不旋转，但仍会统一更新 transform 与检测框坐标格式。
    ///
    /// properties:
    /// - rotate90_labels: List&lt;string&gt; 对应逆时针旋转 90 度的分类标签集合
    /// - rotate180_labels: List&lt;string&gt; 对应逆时针旋转 180 度的分类标签集合
    /// - rotate270_labels: List&lt;string&gt; 对应逆时针旋转 270 度的分类标签集合
    /// </summary>
    public class ImageRotateByClassification : BaseModule
    {
        static ImageRotateByClassification()
        {
            ModuleRegistry.Register("features/image_rotate_by_cls", typeof(ImageRotateByClassification));
        }

        public ImageRotateByClassification(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var resultsDet = resultList ?? new JArray();

            if (images.Count == 0)
            {
                // 没有图像时，直接透传检测结果
                return new ModuleIO(new List<ModuleImage>(), new JArray(resultsDet));
            }

            // 读取并标准化标签集合
            var set90 = ToLabelSet(ReadProperty("rotate90_labels"));
            var set180 = ToLabelSet(ReadProperty("rotate180_labels"));
            var set270 = ToLabelSet(ReadProperty("rotate270_labels"));

            // 从额外输入对0中读取分类结果
            JArray clsResults = new JArray();
            if (ExtraInputsIn != null && ExtraInputsIn.Count > 0 && ExtraInputsIn[0] != null && ExtraInputsIn[0].ResultList != null)
            {
                clsResults = ExtraInputsIn[0].ResultList;
            }

            // 聚合分类结果映射（仅基于分类结果，不透传分类本身）
            // 修正：同时也从 results_det 中尝试获取分类标签，以支持用户将分类结果连接到主 results 端口的情况
            var allClsSources = clsResults.Concat(resultsDet);

            Dictionary<string, string> tmap;
            Dictionary<int, string> imap;
            Dictionary<int, string> omap;
            BuildLabelMaps(allClsSources, out tmap, out imap, out omap);

            var outImages = new List<ModuleImage>();
            // 记录每张图像在本模块输出时的 TransformationState（包括 0° 情况），用于后续更新检测结果
            var imgNewStates = new Dictionary<int, TransformationState>();

            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null || wrap.GetImage() == null || wrap.GetImage().Empty())
                {
                    continue;
                }

                var baseImg = wrap.GetImage();
                int w = baseImg.Width;
                int h = baseImg.Height;

                string sig = SerializeTransformKey(wrap.TransformState);
                string label = null;

                // 逐对匹配：transform > index > origin_index
                if (sig != null && tmap != null && tmap.ContainsKey(sig))
                {
                    label = tmap[sig];
                }
                else if (imap != null && imap.ContainsKey(i))
                {
                    label = imap[i];
                }
                else if (omap != null && omap.ContainsKey(wrap.OriginalIndex))
                {
                    label = omap[wrap.OriginalIndex];
                }

                int angleCcw = 0;
                if (!string.IsNullOrEmpty(label))
                {
                    string key = label.Trim();
                    if (set90.Contains(key))
                    {
                        angleCcw = 90;
                    }
                    else if (set180.Contains(key))
                    {
                        angleCcw = 180;
                    }
                    else if (set270.Contains(key))
                    {
                        angleCcw = 270;
                    }
                }

                if (GlobalDebug.PrintDebug)
                {
                    GlobalDebug.Log($"[ImageRotateByClassification] 每张图的识别结果: {label ?? "null"}, 旋转角度: {angleCcw}");
                }

                if (angleCcw % 360 == 0)
                {
                    // 角度为 0：图像不做旋转，但仍记录当前 TransformationState，
                    // 以便后续对检测结果做统一的 transform 与坐标格式更新
                    outImages.Add(wrap);
                    imgNewStates[i] = wrap.TransformState != null ? wrap.TransformState.Clone() : new TransformationState(w, h);
                    continue;
                }

                double[] A;
                int newW, newH;
                GetRotationAffineCcwDeg(angleCcw, w, h, out A, out newW, out newH);

                Mat rotated = null;
                try
                {
                    var matA = new Mat(2, 3, MatType.CV_64FC1);
                    matA.Set(0, 0, A[0]);
                    matA.Set(0, 1, A[1]);
                    matA.Set(0, 2, A[2]);
                    matA.Set(1, 0, A[3]);
                    matA.Set(1, 1, A[4]);
                    matA.Set(1, 2, A[5]);
                    rotated = new Mat();
                    Cv2.WarpAffine(baseImg, rotated, matA, new Size(newW, newH));
                }
                catch
                {
                    rotated = new Mat();
                    int a = ((angleCcw % 360) + 360) % 360;
                    if (a == 90)
                    {
                        Cv2.Rotate(baseImg, rotated, RotateFlags.Rotate90Counterclockwise);
                    }
                    else if (a == 180)
                    {
                        Cv2.Rotate(baseImg, rotated, RotateFlags.Rotate180);
                    }
                    else
                    {
                        Cv2.Rotate(baseImg, rotated, RotateFlags.Rotate90Clockwise);
                    }
                }

                if (rotated == null || rotated.Empty())
                {
                    outImages.Add(wrap);
                    imgNewStates[i] = wrap.TransformState != null ? wrap.TransformState.Clone() : new TransformationState(w, h);
                    continue;
                }

                var parentState = wrap.TransformState ?? new TransformationState(w, h);
                var childState = parentState.DeriveChild(A, newW, newH);
                var child = new ModuleImage(rotated, wrap.OriginalImage ?? baseImg, childState, wrap.OriginalIndex);
                outImages.Add(child);
                imgNewStates[i] = childState;
            }

            // 若没有任何有效图像，直接透传检测结果
            if (imgNewStates.Count == 0)
            {
                return new ModuleIO(outImages, new JArray(resultsDet));
            }

            // 根据旋转后的 TransformationState 更新检测/旋转框结果
            var outResults = new JArray();
            foreach (var token in resultsDet)
            {
                var res = token as JObject;
                if (res == null)
                {
                    outResults.Add(token);
                    continue;
                }

                var typeStr = res.Value<string>("type") ?? string.Empty;
                if (!string.Equals(typeStr, "local", StringComparison.OrdinalIgnoreCase))
                {
                    outResults.Add(res);
                    continue;
                }

                int idx = res["index"] != null ? (res["index"].Value<int?>() ?? -1) : -1;
                if (!imgNewStates.ContainsKey(idx))
                {
                    // 该 result 不对应本模块中被旋转的图像，原样透传
                    outResults.Add(res);
                    continue;
                }

                var oldTransform = res["transform"] as JObject;
                var childState = imgNewStates[idx];
                var T_o2n = ForwardMatrixFromState(childState);

                var newRes = (JObject)res.DeepClone();
                // 将 transform 更新为旋转后图像的 state
                newRes["transform"] = childState != null ? JObject.FromObject(childState.ToDict()) : null;

                var oldDets = res["sample_results"] as JArray;
                var newDets = new JArray();

                if (oldDets != null)
                {
                    foreach (var dToken in oldDets)
                    {
                        var d = dToken as JObject;
                        if (d == null)
                        {
                            newDets.Add(dToken);
                            continue;
                        }

                        var dNew = (JObject)d.DeepClone();
                        var bboxToken = d["bbox"];
                        Point2f[] ptsCrop = null;

                        // 支持 rbox 与 xyxy
                        var bboxArr = bboxToken as JArray;
                        if (bboxArr != null)
                        {
                            if (bboxArr.Count == 5)
                            {
                                // 旋转框：[cx, cy, w, h, angle(rad)]
                                try
                                {
                                    double cx = bboxArr[0].Value<double>();
                                    double cy = bboxArr[1].Value<double>();
                                    double rw = bboxArr[2].Value<double>();
                                    double rh = bboxArr[3].Value<double>();
                                    double ra = bboxArr[4].Value<double>();
                                    float angDeg = (float)(ra * 180.0 / Math.PI);
                                    var rect = new RotatedRect(
                                        new Point2f((float)cx, (float)cy),
                                        new Size2f((float)rw, (float)rh),
                                        angDeg);
                                    ptsCrop = rect.Points();
                                }
                                catch (Exception ex)
                                {
                                    if (GlobalDebug.PrintDebug)
                                    {
                                        GlobalDebug.Log("[ImageRotateByClassification] 解析 rbox bbox 失败: " + ex.Message);
                                    }
                                    ptsCrop = null;
                                }
                            }
                            else if (bboxArr.Count == 4)
                            {
                                // 普通框（统一按 C# 侧 XYWH）：[x, y, w, h]
                                try
                                {
                                    double x1 = bboxArr[0].Value<double>();
                                    double y1 = bboxArr[1].Value<double>();
                                    double bw = bboxArr[2].Value<double>();
                                    double bh = bboxArr[3].Value<double>();
                                    double x2 = x1 + bw;
                                    double y2 = y1 + bh;
                                    ptsCrop = new[]
                                    {
                                        new Point2f((float)x1, (float)y1),
                                        new Point2f((float)x2, (float)y1),
                                        new Point2f((float)x2, (float)y2),
                                        new Point2f((float)x1, (float)y2)
                                    };
                                }
                                catch (Exception ex)
                                {
                                    if (GlobalDebug.PrintDebug)
                                    {
                                        GlobalDebug.Log("[ImageRotateByClassification] 解析 xywh bbox 失败: " + ex.Message);
                                    }
                                    ptsCrop = null;
                                }
                            }
                        }
                        else
                        {
                            var bboxObj = bboxToken as JObject;
                            if (bboxObj != null && bboxObj["cx"] != null)
                            {
                                // dict 形式的旋转框：{cx, cy, w, h, angle/theta}
                                try
                                {
                                    double cx = bboxObj.Value<double?>("cx") ?? 0.0;
                                    double cy = bboxObj.Value<double?>("cy") ?? 0.0;
                                    double rw = bboxObj.Value<double?>("w") ?? 0.0;
                                    double rh = bboxObj.Value<double?>("h") ?? 0.0;
                                    double ra = bboxObj["angle"] != null
                                        ? bboxObj.Value<double?>("angle") ?? 0.0
                                        : (bboxObj.Value<double?>("theta") ?? 0.0);
                                    float angDeg = (float)(ra * 180.0 / Math.PI);
                                    var rect = new RotatedRect(
                                        new Point2f((float)cx, (float)cy),
                                        new Size2f((float)rw, (float)rh),
                                        angDeg);
                                    ptsCrop = rect.Points();
                                }
                                catch (Exception ex)
                                {
                                    if (GlobalDebug.PrintDebug)
                                    {
                                        GlobalDebug.Log("[ImageRotateByClassification] 解析 dict rbox bbox 失败: " + ex.Message);
                                    }
                                    ptsCrop = null;
                                }
                            }
                        }

                        if (ptsCrop != null && oldTransform != null)
                        {
                            try
                            {
                                // 1. 先从当前图坐标映射回原图坐标
                                var ptsOrig = MapPointsToOriginal(oldTransform, ptsCrop);

                                // 2. 再从原图映射到矫正后的新图坐标
                                var ptsNew = TransformPoints3x3(T_o2n, ptsOrig);

                                // 3. 使用最小外接矩形重新拟合旋转框
                                var rr = Cv2.MinAreaRect(ptsNew);
                                double nangRad = rr.Angle * Math.PI / 180.0;

                                dNew["bbox"] = new JArray(
                                    (double)rr.Center.X,
                                    (double)rr.Center.Y,
                                    (double)rr.Size.Width,
                                    (double)rr.Size.Height,
                                    nangRad
                                );

                                // 几何已经变化，mask 不再可靠，移除
                                if (dNew["mask_array"] != null)
                                {
                                    dNew.Remove("mask_array");
                                }
                            }
                            catch (Exception ex)
                            {
                                if (GlobalDebug.PrintDebug)
                                {
                                    GlobalDebug.Log("[ImageRotateByClassification] 更新检测 bbox 失败，保留原值: " + ex.Message);
                                }
                            }
                        }

                        newDets.Add(dNew);
                    }
                }

                newRes["sample_results"] = newDets;
                outResults.Add(newRes);
            }

            // 输出：矫正后的图像与更新后的检测/旋转框结果；分类结果仅作为输入参与计算，不会出现在输出中
            return new ModuleIO(outImages, outResults);
        }

        private object ReadProperty(string key)
        {
            if (Properties == null || string.IsNullOrEmpty(key))
            {
                return null;
            }
            if (!Properties.TryGetValue(key, out object v) || v == null)
            {
                return null;
            }
            return v;
        }

        private static HashSet<string> ToLabelSet(object v)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (v == null)
            {
                return set;
            }

            try
            {
                var enumerable = v as System.Collections.IEnumerable;
                if (!(v is string) && enumerable != null)
                {
                    foreach (var item in enumerable)
                    {
                        if (item == null) continue;
                        string s = item.ToString();
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        set.Add(s.Trim());
                    }
                    return set;
                }

                string str = v as string;
                if (!string.IsNullOrEmpty(str))
                {
                    string s = str.Trim();
                    if (s.Length == 0) return set;
                    string[] seps = new string[] { "，", ",", ";", "；", "|", "/" };
                    foreach (var sep in seps)
                    {
                        s = s.Replace(sep, " ");
                    }
                    var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        string ps = p.Trim();
                        if (ps.Length > 0) set.Add(ps);
                    }
                }
            }
            catch
            {
            }

            return set;
        }

        private static string SerializeTransformKey(TransformationState st)
        {
            if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length < 6)
            {
                return null;
            }
            var a = st.AffineMatrix2x3;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "T:{0:F4},{1:F4},{2:F2},{3:F4},{4:F4},{5:F2}",
                a[0], a[1], a[2], a[3], a[4], a[5]);
        }

        private static void BuildLabelMaps(IEnumerable<JToken> clsResults, out Dictionary<string, string> tmap, out Dictionary<int, string> imap, out Dictionary<int, string> omap)
        {
            tmap = new Dictionary<string, string>(StringComparer.Ordinal);
            imap = new Dictionary<int, string>();
            omap = new Dictionary<int, string>();

            if (clsResults == null) return;

            foreach (var token in clsResults)
            {
                var entry = token as JObject;
                if (entry == null) continue;
                var type = entry.Value<string>("type") ?? "";
                if (!string.Equals(type, "local", StringComparison.OrdinalIgnoreCase)) continue;

                string label = ClsTop1Label(entry);
                if (label == null) continue;

                // transform -> label（仅使用 affine_2x3；若无则不建键）
                string tSig = null;
                var stDict = entry["transform"] as JObject;
                if (stDict != null)
                {
                    try
                    {
                        var dict = stDict.ToObject<Dictionary<string, object>>();
                        var st = TransformationState.FromDict(dict);
                        tSig = SerializeTransformKey(st);
                    }
                    catch
                    {
                        tSig = null;
                    }
                }
                if (tSig != null && !tmap.ContainsKey(tSig))
                {
                    tmap[tSig] = label;
                }

                // index -> label
                int idx = entry["index"] != null ? (entry["index"].Value<int?>() ?? -1) : -1;
                if (idx >= 0 && !imap.ContainsKey(idx))
                {
                    imap[idx] = label;
                }

                // origin_index -> label
                int oidx;
                var originToken = entry["origin_index"];
                if (originToken != null)
                {
                    oidx = originToken.Value<int?>() ?? -1;
                }
                else
                {
                    oidx = idx;
                }
                if (oidx >= 0 && !omap.ContainsKey(oidx))
                {
                    omap[oidx] = label;
                }
            }
        }

        private static string ClsTop1Label(JObject entry)
        {
            try
            {
                if (entry == null) return null;
                var type = entry.Value<string>("type") ?? "";
                if (!string.Equals(type, "local", StringComparison.OrdinalIgnoreCase)) return null;

                var srsToken = entry["sample_results"] as JArray;
                if (srsToken == null || srsToken.Count == 0) return null;

                var dets = new List<JObject>();

                foreach (var t in srsToken)
                {
                    var obj = t as JObject;
                    if (obj == null) continue;

                    bool hasCategory = obj["category_name"] != null || obj["results"] != null || obj["bbox"] != null;
                    if (!hasCategory) continue;

                    var innerResults = obj["results"] as JArray;
                    if (innerResults != null)
                    {
                        foreach (var r in innerResults)
                        {
                            var ro = r as JObject;
                            if (ro != null) dets.Add(ro);
                        }
                    }
                    else
                    {
                        dets.Add(obj);
                    }
                }

                if (dets.Count == 0) return null;

                JObject best = null;
                double bestScore = double.MinValue;
                foreach (var d in dets)
                {
                    double sc = 0.0;
                    var sv = d["score"];
                    if (sv != null)
                    {
                        double tmp;
                        if (double.TryParse(sv.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out tmp))
                        {
                            sc = tmp;
                        }
                    }
                    if (best == null || sc > bestScore)
                    {
                        best = d;
                        bestScore = sc;
                    }
                }

                if (best == null) return null;
                var cnToken = best["category_name"];
                if (cnToken == null) return null;
                var cn = cnToken.ToString();
                if (string.IsNullOrEmpty(cn)) return null;
                return cn;
            }
            catch
            {
                return null;
            }
        }

        private static void GetRotationAffineCcwDeg(int angle, int width, int height, out double[] A, out int newWidth, out int newHeight)
        {
            int w = width;
            int h = height;
            int a = ((angle % 360) + 360) % 360;

            if (a == 0)
            {
                A = new double[] { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0 };
                newWidth = w;
                newHeight = h;
                return;
            }
            if (a == 90)
            {
                // 逆时针 90：x' = y; y' = w-1 - x
                A = new double[] { 0.0, 1.0, 0.0, -1.0, 0.0, w - 1.0 };
                newWidth = h;
                newHeight = w;
                return;
            }
            if (a == 180)
            {
                // 180：x' = w-1 - x; y' = h-1 - y
                A = new double[] { -1.0, 0.0, w - 1.0, 0.0, -1.0, h - 1.0 };
                newWidth = w;
                newHeight = h;
                return;
            }
            // 270：逆时针 270 等同于顺时针 90，x' = h-1 - y; y' = x
            A = new double[] { 0.0, -1.0, h - 1.0, 1.0, 0.0, 0.0 };
            newWidth = h;
            newHeight = w;
        }

        /// <summary>
        /// 将当前 TransformationState 转换为原图 -> 当前图像的 3x3 仿射矩阵。
        /// 在 C# 版本中，AffineMatrix2x3 直接表示原图 -> 当前图，因此这里只需升维到 3x3。
        /// </summary>
        private static double[] ForwardMatrixFromState(TransformationState st)
        {
            if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length != 6)
            {
                // 单位矩阵
                return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            }
            return TransformationState.To3x3(st.AffineMatrix2x3);
        }

        /// <summary>
        /// 将当前图像坐标系下的点映射回原图坐标（使用 transform 中的 crop_box + affine）。
        /// 等价于 Python 中 RBoxAngleCorrection._map_points_to_original。
        /// </summary>
        private static Point2f[] MapPointsToOriginal(JObject transformObj, Point2f[] pts)
        {
            if (transformObj == null || pts == null || pts.Length == 0)
            {
                return pts;
            }

            try
            {
                // 读取 crop_box
                int x0 = 0, y0 = 0;
                var crop = transformObj["crop_box"] as JArray;
                if (crop != null && crop.Count >= 2)
                {
                    x0 = crop[0].Value<int>();
                    y0 = crop[1].Value<int>();
                }

                // 读取 2x3 仿射矩阵（支持 affine_2x3 或 affine_matrix 两种形式）
                double[] a2x3 = null;
                var flat = transformObj["affine_2x3"] as JArray;
                if (flat != null && flat.Count >= 6)
                {
                    a2x3 = new double[6];
                    for (int i = 0; i < 6; i++)
                    {
                        a2x3[i] = flat[i].Value<double>();
                    }
                }
                else
                {
                    var mat = transformObj["affine_matrix"] as JArray;
                    if (mat != null && mat.Count >= 2)
                    {
                        var row0 = mat[0] as JArray;
                        var row1 = mat[1] as JArray;
                        if (row0 != null && row0.Count >= 3 && row1 != null && row1.Count >= 3)
                        {
                            a2x3 = new double[]
                            {
                                row0[0].Value<double>(), row0[1].Value<double>(), row0[2].Value<double>(),
                                row1[0].Value<double>(), row1[1].Value<double>(), row1[2].Value<double>()
                            };
                        }
                    }
                }

                if (a2x3 == null || a2x3.Length != 6)
                {
                    return pts;
                }

                // 计算逆矩阵：当前图 -> ROI
                double[] inv = TransformationState.Inverse2x3(a2x3);
                var outPts = new Point2f[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                {
                    double x = pts[i].X;
                    double y = pts[i].Y;
                    double rx = inv[0] * x + inv[1] * y + inv[2];
                    double ry = inv[3] * x + inv[4] * y + inv[5];
                    outPts[i] = new Point2f((float)(rx + x0), (float)(ry + y0));
                }
                return outPts;
            }
            catch
            {
                return pts;
            }
        }

        /// <summary>
        /// 使用 3x3 仿射矩阵变换点集：P_new = T * P_orig。
        /// </summary>
        private static Point2f[] TransformPoints3x3(double[] T, Point2f[] pts)
        {
            if (T == null || T.Length != 9 || pts == null || pts.Length == 0)
            {
                return pts;
            }

            var outPts = new Point2f[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                double x = pts[i].X;
                double y = pts[i].Y;
                double nx = T[0] * x + T[1] * y + T[2];
                double ny = T[3] * x + T[4] * y + T[5];
                double w = T[6] * x + T[7] * y + T[8];
                if (Math.Abs(w) < 1e-8) w = 1.0;
                outPts[i] = new Point2f((float)(nx / w), (float)(ny / w));
            }
            return outPts;
        }
    }

    /// <summary>
    /// 模块名称：文本替换
    /// 特征/文本替换：根据映射表替换识别结果（category_name）中的字符或子串。
    /// 注册名：features/text_replacement
    /// properties:
    /// - mapping: Dict[string, string] 或 JSON 字符串，按 key->value 执行 str.Replace
    ///
    /// 行为：
    /// - 仅处理 type == "local" 的结果；图像透传不变；未配置或无效映射时直接透传。
    /// - 对每个 sample_result 的 category_name（字符串）进行多轮替换；若修改则写回。
    /// </summary>
    public class TextReplacement : BaseModule
    {
        static TextReplacement()
        {
            ModuleRegistry.Register("post_process/text_replacement", typeof(TextReplacement));
            ModuleRegistry.Register("features/text_replacement", typeof(TextReplacement));
        }

        public TextReplacement(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

            var mapping = ReadMapping(Properties);
            if (mapping == null || mapping.Count == 0)
            {
                // 无有效映射，直接透传
                return new ModuleIO(images, results);
            }

            var outResults = new JArray();

            foreach (var token in results)
            {
                var entry = token as JObject;
                if (entry == null)
                {
                    outResults.Add(token);
                    continue;
                }

                var typeStr = entry.Value<string>("type") ?? string.Empty;
                if (!string.Equals(typeStr, "local", StringComparison.OrdinalIgnoreCase))
                {
                    outResults.Add(entry);
                    continue;
                }

                var dets = entry["sample_results"] as JArray;
                if (dets == null)
                {
                    outResults.Add(entry);
                    continue;
                }

                var newDets = new JArray();
                foreach (var dToken in dets)
                {
                    var d = dToken as JObject;
                    if (d == null)
                    {
                        newDets.Add(dToken);
                        continue;
                    }

                    var catToken = d["category_name"];
                    if (catToken != null && catToken.Type == JTokenType.String)
                    {
                        string cat = catToken.ToString();
                        string newCat = ApplyMapping(cat, mapping);
                        if (!string.Equals(cat, newCat, StringComparison.Ordinal))
                        {
                            var d2 = (JObject)d.DeepClone();
                            d2["category_name"] = newCat;
                            newDets.Add(d2);
                        }
                        else
                        {
                            newDets.Add(d);
                        }
                    }
                    else
                    {
                        newDets.Add(d);
                    }
                }

                var entryNew = (JObject)entry.DeepClone();
                entryNew["sample_results"] = newDets;
                outResults.Add(entryNew);
            }

            return new ModuleIO(images, outResults);
        }

        private static string ApplyMapping(string input, Dictionary<string, string> mapping)
        {
            if (string.IsNullOrEmpty(input) || mapping == null || mapping.Count == 0) return input;
            string s = input;
            foreach (var kv in mapping)
            {
                if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null && s.IndexOf(kv.Key, StringComparison.Ordinal) >= 0)
                {
                    s = s.Replace(kv.Key, kv.Value);
                }
            }
            return s;
        }

        private static Dictionary<string, string> ReadMapping(Dictionary<string, object> props)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (props == null) return dict;

            if (!props.TryGetValue("mapping", out object v) || v == null) return dict;

            try
            {
                // 1) JObject
                var jobj = v as JObject;
                if (jobj != null)
                {
                    foreach (var p in jobj)
                    {
                        var key = p.Key;
                        if (string.IsNullOrEmpty(key)) continue;
                        var val = p.Value != null ? p.Value.ToString() : "";
                        dict[key] = val ?? "";
                    }
                    return dict;
                }

                // 2) Dictionary<string, object>
                var dso = v as Dictionary<string, object>;
                if (dso != null)
                {
                    foreach (var kv in dso)
                    {
                        if (string.IsNullOrEmpty(kv.Key)) continue;
                        dict[kv.Key] = kv.Value != null ? kv.Value.ToString() : "";
                    }
                    return dict;
                }

                // 3) JSON 字符串
                var str = v as string;
                if (str == null && v is JValue jv && jv.Type == JTokenType.String)
                {
                    str = jv.ToString();
                }
                if (!string.IsNullOrEmpty(str))
                {
                    try
                    {
                        var parsed = JObject.Parse(str);
                        foreach (var p in parsed)
                        {
                            var key = p.Key;
                            if (string.IsNullOrEmpty(key)) continue;
                            var val = p.Value != null ? p.Value.ToString() : "";
                            dict[key] = val ?? "";
                        }
                        return dict;
                    }
                    catch
                    {
                        // 非 JSON 字符串则忽略
                    }
                }

                // 4) 其它 IDictionary 兼容
                var idict = v as System.Collections.IDictionary;
                if (idict != null)
                {
                    foreach (var keyObj in idict.Keys)
                    {
                        var key = keyObj != null ? keyObj.ToString() : null;
                        if (string.IsNullOrEmpty(key)) continue;
                        var valObj = idict[keyObj];
                        dict[key] = valObj != null ? valObj.ToString() : "";
                    }
                }
            }
            catch
            {
                // 任何解析异常，按空映射处理
            }

            return dict;
        }
    }

    /// <summary>
    /// 模块名称：旋转框矫正
    /// 根据旋转框结果矫正图像，以图像中心为旋转中心，使基准框角度变为 0 度，并更新所有检测结果的坐标。
    /// 注册名：post_process/rbox_correction, features/rbox_correction
    /// 处理逻辑：
    /// 1. 取 results 中每张图关联的第一个含 transform 的 local 结果作为基准；
    /// 2. 从 transform 的仿射矩阵中解析旋转角度 angle；
    /// 3. 以图像中心为轴，旋转图像 angle 度；
    /// 4. 更新所有检测结果的 bbox 以匹配新图像坐标系；若存在 mask_array，则在几何变化后移除。
    /// properties:
    /// - fill_value: int 旋转图像填充值，默认 0
    /// </summary>
    public class RBoxCorrection : BaseModule
    {
        static RBoxCorrection()
        {
            ModuleRegistry.Register("post_process/rbox_correction", typeof(RBoxCorrection));
            ModuleRegistry.Register("features/rbox_correction", typeof(RBoxCorrection));
        }

        public RBoxCorrection(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

            int fillVal = 0;
            if (Properties != null && Properties.TryGetValue("fill_value", out object fv) && fv != null)
            {
                try { fillVal = Convert.ToInt32(fv); }
                catch { fillVal = 0; }
            }

            // 1. 预处理图像，保持索引一致
            var wrappers = new List<ModuleImage>();
            for (int idx = 0; idx < images.Count; idx++)
            {
                var im = images[idx];
                if (im == null || im.GetImage() == null || im.GetImage().Empty())
                {
                    wrappers.Add(null);
                }
                else
                {
                    wrappers.Add(im);
                }
            }

            // 2. 建立 Image Index -> List[Result Entry] 映射（仅 type==local）
            var imgIdxToLocalResults = new Dictionary<int, List<JObject>>();
            foreach (var token in results)
            {
                var res = token as JObject;
                if (res == null) continue;
                var typeStr = res.Value<string>("type") ?? string.Empty;
                if (!string.Equals(typeStr, "local", StringComparison.OrdinalIgnoreCase)) continue;
                int idx = res["index"] != null ? (res["index"].Value<int?>() ?? -1) : -1;
                if (idx >= 0 && idx < wrappers.Count)
                {
                    if (!imgIdxToLocalResults.TryGetValue(idx, out List<JObject> lst))
                    {
                        lst = new List<JObject>();
                        imgIdxToLocalResults[idx] = lst;
                    }
                    lst.Add(res);
                }
            }

            // 3. 计算每张图的旋转矩阵
            // Map: index -> (A_rot_2x3, child_state_of_new_image, angle_rad)
            var imgTransforms = new Dictionary<int, Tuple<double[], TransformationState, double>>();
            var outImages = new List<ModuleImage>();

            for (int i = 0; i < wrappers.Count; i++)
            {
                var wrap = wrappers[i];
                if (wrap == null)
                {
                    outImages.Add(null);
                    continue;
                }

                double refAngleRad = 0.0;
                bool foundRef = false;

                if (imgIdxToLocalResults.TryGetValue(i, out List<JObject> resEntries))
                {
                    foreach (var entry in resEntries)
                    {
                        var tObj = entry["transform"] as JObject;
                        if (tObj == null) continue;
                        double? ang = GetAngleFromTransform(tObj);
                        if (ang.HasValue)
                        {
                            refAngleRad = ang.Value;
                            foundRef = true;
                            break;
                        }
                    }
                }

                if (!foundRef)
                {
                    // 没有可用的基准 transform，则不做矫正
                    outImages.Add(wrap);
                    continue;
                }

                double rotDeg = -refAngleRad * 180.0 / Math.PI;

                var baseImg = wrap.GetImage();
                int h = baseImg.Height;
                int w = baseImg.Width;
                var center = new Point2f(w / 2.0f, h / 2.0f);

                // 使用 OpenCV 获取旋转矩阵，再提取 2x3 double[]
                var rotMat = Cv2.GetRotationMatrix2D(center, rotDeg, 1.0);
                double[] A = new double[6];
                A[0] = rotMat.Get<double>(0, 0);
                A[1] = rotMat.Get<double>(0, 1);
                A[2] = rotMat.Get<double>(0, 2);
                A[3] = rotMat.Get<double>(1, 0);
                A[4] = rotMat.Get<double>(1, 1);
                A[5] = rotMat.Get<double>(1, 2);

                Mat rotatedImg;
                try
                {
                    rotatedImg = new Mat();
                    // 使用常量边界填充值
                    Cv2.WarpAffine(baseImg, rotatedImg, rotMat, new Size(w, h), borderMode: BorderTypes.Constant, borderValue: new Scalar(fillVal, fillVal, fillVal));
                }
                catch
                {
                    rotatedImg = baseImg;
                }

                var parentState = wrap.TransformState ?? new TransformationState(w, h);
                var childState = parentState.DeriveChild(A, w, h);
                var newWrap = new ModuleImage(rotatedImg, wrap.OriginalImage ?? baseImg, childState, wrap.OriginalIndex);
                outImages.Add(newWrap);

                imgTransforms[i] = Tuple.Create(A, childState, refAngleRad);
            }

            // 4. 更新结果
            var outResults = new JArray();
            foreach (var token in results)
            {
                var res = token as JObject;
                if (res == null)
                {
                    outResults.Add(token);
                    continue;
                }

                var typeStr = res.Value<string>("type") ?? string.Empty;
                if (!string.Equals(typeStr, "local", StringComparison.OrdinalIgnoreCase))
                {
                    outResults.Add(res);
                    continue;
                }

                int idx = res["index"] != null ? (res["index"].Value<int?>() ?? -1) : -1;
                if (!imgTransforms.TryGetValue(idx, out Tuple<double[], TransformationState, double> tup))
                {
                    outResults.Add(res);
                    continue;
                }

                var Arot = tup.Item1;
                var newState = tup.Item2;

                var newRes = (JObject)res.DeepClone();
                // 更新 transform 为新图像的 state
                newRes["transform"] = newState != null ? JObject.FromObject(newState.ToDict()) : null;

                var oldDets = res["sample_results"] as JArray;
                var newDets = new JArray();

                if (oldDets != null)
                {
                    foreach (var dToken in oldDets)
                    {
                        var d = dToken as JObject;
                        if (d == null)
                        {
                            newDets.Add(dToken);
                            continue;
                        }

                        var dNew = (JObject)d.DeepClone();
                        var bboxToken = d["bbox"];
                        Point2f[] ptsCrop = null;

                        var bboxArr = bboxToken as JArray;
                        if (bboxArr != null)
                        {
                            if (bboxArr.Count == 5)
                            {
                                // 旋转框 [cx, cy, w, h, angle(rad)]
                                try
                                {
                                    double cx = bboxArr[0].Value<double>();
                                    double cy = bboxArr[1].Value<double>();
                                    double rw = bboxArr[2].Value<double>();
                                    double rh = bboxArr[3].Value<double>();
                                    double ra = bboxArr[4].Value<double>();
                                    float angDeg = (float)(ra * 180.0 / Math.PI);
                                    var rect = new RotatedRect(
                                        new Point2f((float)cx, (float)cy),
                                        new Size2f((float)rw, (float)rh),
                                        angDeg);
                                    ptsCrop = rect.Points();
                                }
                                catch
                                {
                                    ptsCrop = null;
                                }
                            }
                            else if (bboxArr.Count == 4)
                            {
                                // 轴对齐框（统一按 C# 侧 XYWH）[x, y, w, h]
                                try
                                {
                                    double x1 = bboxArr[0].Value<double>();
                                    double y1 = bboxArr[1].Value<double>();
                                    double bw = bboxArr[2].Value<double>();
                                    double bh = bboxArr[3].Value<double>();
                                    double x2 = x1 + bw;
                                    double y2 = y1 + bh;
                                    ptsCrop = new[]
                                    {
                                        new Point2f((float)x1, (float)y1),
                                        new Point2f((float)x2, (float)y1),
                                        new Point2f((float)x2, (float)y2),
                                        new Point2f((float)x1, (float)y2)
                                    };
                                }
                                catch
                                {
                                    ptsCrop = null;
                                }
                            }
                        }
                        else
                        {
                            var bboxObj = bboxToken as JObject;
                            if (bboxObj != null && bboxObj["cx"] != null)
                            {
                                // dict 形式旋转框 {cx, cy, w, h, angle/theta}
                                try
                                {
                                    double cx = bboxObj.Value<double?>("cx") ?? 0.0;
                                    double cy = bboxObj.Value<double?>("cy") ?? 0.0;
                                    double rw = bboxObj.Value<double?>("w") ?? 0.0;
                                    double rh = bboxObj.Value<double?>("h") ?? 0.0;
                                    double ra = bboxObj["angle"] != null
                                        ? bboxObj.Value<double?>("angle") ?? 0.0
                                        : (bboxObj.Value<double?>("theta") ?? 0.0);
                                    float angDeg = (float)(ra * 180.0 / Math.PI);
                                    var rect = new RotatedRect(
                                        new Point2f((float)cx, (float)cy),
                                        new Size2f((float)rw, (float)rh),
                                        angDeg);
                                    ptsCrop = rect.Points();
                                }
                                catch
                                {
                                    ptsCrop = null;
                                }
                            }
                        }

                        if (ptsCrop != null)
                        {
                            var tOrigin = res["transform"] as JObject;
                            if (tOrigin != null)
                            {
                                try
                                {
                                    // 1. 当前图 -> 原图
                                    var ptsOrig = MapPointsToOriginal(tOrigin, ptsCrop);
                                    // 2. 原图 -> 旋转后
                                    var ptsNew = TransformPoints2x3(Arot, ptsOrig);

                                    var rr = Cv2.MinAreaRect(ptsNew);
                                    double nangRad = rr.Angle * Math.PI / 180.0;

                                    dNew["bbox"] = new JArray(
                                        (double)rr.Center.X,
                                        (double)rr.Center.Y,
                                        (double)rr.Size.Width,
                                        (double)rr.Size.Height,
                                        nangRad
                                    );

                                    if (dNew["mask_array"] != null)
                                    {
                                        dNew.Remove("mask_array");
                                    }
                                }
                                catch
                                {
                                    // 几何更新失败时保留原 bbox
                                }
                            }
                        }

                        newDets.Add(dNew);
                    }
                }

                newRes["sample_results"] = newDets;
                outResults.Add(newRes);
            }

            // 过滤掉 null 图像占位
            var finalImages = new List<ModuleImage>();
            foreach (var im in outImages)
            {
                if (im != null) finalImages.Add(im);
            }

            return new ModuleIO(finalImages, outResults);
        }

        /// <summary>
        /// 从 transform 仿射矩阵中解析旋转角度（弧度）。
        /// 支持 affine_matrix（2x3）或 affine_2x3（长度 6 的一维数组）。
        /// </summary>
        private static double? GetAngleFromTransform(JObject transformObj)
        {
            if (transformObj == null) return null;
            try
            {
                double a, b;
                var mat = transformObj["affine_matrix"] as JArray;
                if (mat != null && mat.Count >= 2)
                {
                    var row0 = mat[0] as JArray;
                    if (row0 == null || row0.Count < 2) return null;
                    a = row0[0].Value<double>();
                    b = row0[1].Value<double>();
                }
                else
                {
                    var flat = transformObj["affine_2x3"] as JArray;
                    if (flat == null || flat.Count < 2) return null;
                    a = flat[0].Value<double>();
                    b = flat[1].Value<double>();
                }
                return Math.Atan2(b, a);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 将当前图像坐标系下的点映射回原图坐标（使用 transform 中的 crop_box + affine）。
        /// 与 ImageRotateByClassification.MapPointsToOriginal 行为保持一致。
        /// </summary>
        private static Point2f[] MapPointsToOriginal(JObject transformObj, Point2f[] pts)
        {
            if (transformObj == null || pts == null || pts.Length == 0)
            {
                return pts;
            }

            try
            {
                int x0 = 0, y0 = 0;
                var crop = transformObj["crop_box"] as JArray;
                if (crop != null && crop.Count >= 2)
                {
                    x0 = crop[0].Value<int>();
                    y0 = crop[1].Value<int>();
                }

                double[] a2x3 = null;
                var flat = transformObj["affine_2x3"] as JArray;
                if (flat != null && flat.Count >= 6)
                {
                    a2x3 = new double[6];
                    for (int i = 0; i < 6; i++)
                    {
                        a2x3[i] = flat[i].Value<double>();
                    }
                }
                else
                {
                    var mat = transformObj["affine_matrix"] as JArray;
                    if (mat != null && mat.Count >= 2)
                    {
                        var row0 = mat[0] as JArray;
                        var row1 = mat[1] as JArray;
                        if (row0 != null && row0.Count >= 3 && row1 != null && row1.Count >= 3)
                        {
                            a2x3 = new double[]
                            {
                                row0[0].Value<double>(), row0[1].Value<double>(), row0[2].Value<double>(),
                                row1[0].Value<double>(), row1[1].Value<double>(), row1[2].Value<double>()
                            };
                        }
                    }
                }

                if (a2x3 == null || a2x3.Length != 6)
                {
                    return pts;
                }

                double[] inv = TransformationState.Inverse2x3(a2x3);
                var outPts = new Point2f[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                {
                    double x = pts[i].X;
                    double y = pts[i].Y;
                    double rx = inv[0] * x + inv[1] * y + inv[2];
                    double ry = inv[3] * x + inv[4] * y + inv[5];
                    outPts[i] = new Point2f((float)(rx + x0), (float)(ry + y0));
                }
                return outPts;
            }
            catch
            {
                return pts;
            }
        }

        /// <summary>
        /// 使用 2x3 仿射矩阵变换点集：P_new = A * P_orig。
        /// </summary>
        private static Point2f[] TransformPoints2x3(double[] A, Point2f[] pts)
        {
            if (A == null || A.Length != 6 || pts == null || pts.Length == 0)
            {
                return pts;
            }

            var outPts = new Point2f[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                double x = pts[i].X;
                double y = pts[i].Y;
                double nx = A[0] * x + A[1] * y + A[2];
                double ny = A[3] * x + A[4] * y + A[5];
                outPts[i] = new Point2f((float)nx, (float)ny);
            }
            return outPts;
        }
    }
}
