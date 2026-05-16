using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
            ModuleRegistry.Register("pre_process/image_generation", typeof(ImageGeneration));
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
            string cropExpandMode = "pixel";
            double cropExpandPercent = 0.0;
            double cropExpandPercentLimit = 32.0;
            int? cropW = null;
            int? cropH = null;
            int minSize = 1;
            if (Properties != null)
            {
                cropExpand = Math.Max(0.0, GetDouble(Properties, "crop_expand", 0.0));
                cropExpandMode = (GetString(Properties, "crop_expand_mode", "pixel") ?? "pixel").Trim().ToLowerInvariant();
                cropExpandPercent = Math.Max(0.0, GetDouble(Properties, "crop_expand_percent", 0.0));
                cropExpandPercentLimit = Math.Max(0.0, GetDouble(Properties, "crop_expand_percent_limit", 32.0));
                if (cropExpandPercent > 0.0 && cropExpandMode != "pixel" && cropExpandMode != "px")
                {
                    cropExpandMode = "percent";
                }
                else if (cropExpandPercent > 0.0 && cropExpand <= 0.0)
                {
                    cropExpandMode = "percent";
                }
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
            Func<double, double, Tuple<double, double>> resolveExpand = (baseW, baseH) =>
            {
                if (cropExpandMode == "percent")
                {
                    double ratio = cropExpandPercent / 100.0;
                    double expandW = Math.Max(0.0, baseW) * ratio;
                    double expandH = Math.Max(0.0, baseH) * ratio;
                    if (cropExpandPercentLimit > 0.0)
                    {
                        expandW = Math.Min(expandW, cropExpandPercentLimit);
                        expandH = Math.Min(expandH, cropExpandPercentLimit);
                    }
                    return Tuple.Create(expandW, expandH);
                }
                return Tuple.Create(cropExpand, cropExpand);
            };

            // 构建 transform/index 映射，便于按条目裁剪。transform 仅在唯一时使用，避免多张整图恒等变换互相串图。
            var transformKeyToImage = new Dictionary<string, Tuple<ModuleImage, Mat, int>>();
            var duplicateTransformKeys = new HashSet<string>(StringComparer.Ordinal);
            var indexToImage = new Dictionary<int, Tuple<ModuleImage, Mat, int>>();
            var originToImage = new Dictionary<int, Tuple<ModuleImage, Mat, int>>();
            for (int i = 0; i < imagesIn.Count; i++)
            {
                var (wrap, bmp) = UnwrapImage(imagesIn[i]);
                if (bmp == null || bmp.Empty()) continue;
                var tup = Tuple.Create(wrap, bmp, i);
                indexToImage[i] = tup;

                int originIndex = wrap != null ? wrap.OriginalIndex : i;
                if (!originToImage.ContainsKey(originIndex))
                {
                    originToImage[originIndex] = tup;
                }

                string key = SerializeTransformKey(wrap != null ? wrap.TransformState : null);
                if (!string.IsNullOrEmpty(key))
                {
                    if (transformKeyToImage.ContainsKey(key))
                    {
                        transformKeyToImage.Remove(key);
                        duplicateTransformKeys.Add(key);
                    }
                    else if (!duplicateTransformKeys.Contains(key))
                    {
                        transformKeyToImage[key] = tup;
                    }
                }
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
                Tuple<ModuleImage, Mat, int> tup = null;

                string key = SerializeTransformKey(stateDict);
                if (!string.IsNullOrEmpty(key) && !duplicateTransformKeys.Contains(key))
                {
                    transformKeyToImage.TryGetValue(key, out tup);
                }

                if (tup == null && idx >= 0)
                {
                    indexToImage.TryGetValue(idx, out tup);
                }

                if (tup == null && originIndex >= 0)
                {
                    originToImage.TryGetValue(originIndex, out tup);
                }

                if (tup == null) continue;

                var sampleResults = entry["sample_results"] as JArray;
                if (sampleResults == null || sampleResults.Count == 0) continue;

                foreach (var sr in sampleResults)
                {
                    if (!(sr is JObject srObj)) continue;
                    // 每个 sr 应为一个对象结果，可能含 bbox、with_angle/angle、with_bbox
                    var bbox = srObj["bbox"] as JArray;
                    bool withAngle = srObj["with_angle"]?.Value<bool>() ?? false;
                    double angle = srObj["angle"]?.Value<double?>() ?? -100.0;
                    bool withBbox = srObj["with_bbox"]?.Value<bool?>() ?? (bbox != null && bbox.Count > 0);
                    if (!withBbox || bbox == null || bbox.Count < 4) continue;

                    Mat cropped = null;
                    double[] childA2x3 = null;
                    int cw, ch;
                    double rbx0 = 0.0, rbx1 = 0.0, rbx2 = 0.0, rbx3 = 0.0;
                    double rebuiltAngle = -100.0;
                    bool rebuiltWithAngle = false;
                    bool rebuiltWithBbox = false;

                    if (withAngle && angle != -100.0)
                    {
                        // 旋转框: bbox=[cx,cy,w,h]，angle为弧度
                        // 与 Python 一致：支持 crop_expand / crop_shape / min_size，输出尺寸使用 int(...) 截断
                        double cx = bbox[0].Value<double>();
                        double cy = bbox[1].Value<double>();
                        double w = Math.Abs(bbox[2].Value<double>());
                        double h = Math.Abs(bbox[3].Value<double>());

                        double w2, h2;
                        if (cropW.HasValue && cropH.HasValue)
                        {
                            w2 = cropW.Value;
                            h2 = cropH.Value;
                        }
                        else
                        {
                            var expand = resolveExpand(w, h);
                            w2 = Math.Max((double)minSize, w + 2.0 * expand.Item1);
                            h2 = Math.Max((double)minSize, h + 2.0 * expand.Item2);
                        }

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

                        // 旋转框重建：将原框4点映射到裁剪图坐标系后重拟合最小外接旋转框
                        try
                        {
                            var ptsNew = new Point2f[4];
                            double hw = Math.Max(0.5, w / 2.0);
                            double hh = Math.Max(0.5, h / 2.0);
                            double c = Math.Cos(angle);
                            double s = Math.Sin(angle);
                            double[,] offs = new double[,]
                            {
                                { -hw, -hh },
                                { hw, -hh },
                                { hw, hh },
                                { -hw, hh }
                            };
                            for (int pi = 0; pi < 4; pi++)
                            {
                                double dx = offs[pi, 0];
                                double dy = offs[pi, 1];
                                double ox = cx + c * dx - s * dy;
                                double oy = cy + s * dx + c * dy;
                                double nx = rotMat.Get<double>(0, 0) * ox + rotMat.Get<double>(0, 1) * oy + rotMat.Get<double>(0, 2);
                                double ny = rotMat.Get<double>(1, 0) * ox + rotMat.Get<double>(1, 1) * oy + rotMat.Get<double>(1, 2);
                                ptsNew[pi] = new Point2f((float)nx, (float)ny);
                            }

                            var rr = Cv2.MinAreaRect(ptsNew);
                            rbx0 = rr.Center.X;
                            rbx1 = rr.Center.Y;
                            rbx2 = Math.Max(1.0, Math.Abs(rr.Size.Width));
                            rbx3 = Math.Max(1.0, Math.Abs(rr.Size.Height));
                            rebuiltAngle = rr.Angle * Math.PI / 180.0;
                        }
                        catch
                        {
                            // 与 Python 一致的兜底：退回裁剪中心 + 原宽高
                            rbx0 = w2 / 2.0;
                            rbx1 = h2 / 2.0;
                            rbx2 = Math.Max(1.0, w);
                            rbx3 = Math.Max(1.0, h);
                            rebuiltAngle = angle;
                        }

                        rebuiltWithBbox = true;
                        rebuiltWithAngle = true;
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
                            var expand = resolveExpand(bw, bh);
                            double expandW = expand.Item1;
                            double expandH = expand.Item2;
                            double tx1 = Math.Max(0.0, Math.Min((double)W, x1 - expandW));
                            double ty1 = Math.Max(0.0, Math.Min((double)H, y1 - expandH));
                            nx1 = (int)Math.Floor(tx1);
                            ny1 = (int)Math.Floor(ty1);

                            int rx2 = (int)Math.Round(x2 + expandW, MidpointRounding.ToEven);
                            int ry2 = (int)Math.Round(y2 + expandH, MidpointRounding.ToEven);
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

                        // 普通框重建：保持 C# xywh 语义，回写裁剪图局部坐标
                        rbx0 = x - nx1;
                        rbx1 = y - ny1;
                        rbx2 = bw;
                        rbx3 = bh;
                        rebuiltAngle = -100.0;
                        rebuiltWithBbox = true;
                        rebuiltWithAngle = false;
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

                    var outDet = (JObject)srObj.DeepClone();
                    if (outDet["mask_array"] != null) outDet.Remove("mask_array");
                    if (outDet["mask_rle"] != null) outDet.Remove("mask_rle");
                    if (outDet["mask"] != null) outDet.Remove("mask");
                    if (outDet["polygon"] != null) outDet.Remove("polygon");
                    if (outDet["poly"] != null) outDet.Remove("poly");
                    outDet["with_mask"] = false;
                    if (rebuiltWithBbox)
                    {
                        outDet["bbox"] = new JArray(rbx0, rbx1, rbx2, rbx3);
                    }
                    outDet["with_bbox"] = rebuiltWithBbox;
                    outDet["with_angle"] = rebuiltWithAngle;
                    outDet["angle"] = rebuiltWithAngle ? rebuiltAngle : -100.0;

                    var outEntry = new JObject
                    {
                        ["type"] = "local",
                        ["originating_module"] = "pre_process/image_generation",
                        ["index"] = outIndex,
                        ["origin_index"] = parentWrap != null ? parentWrap.OriginalIndex : originIndex,
                        ["transform"] = JObject.FromObject(childState.ToDict()),
                        ["sample_results"] = new JArray(outDet)
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

        private static string GetString(Dictionary<string, object> d, string k, string dv)
        {
            if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return dv;
            try { return Convert.ToString(v); } catch { return dv; }
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

        private static string SerializeTransformKey(TransformationState st)
        {
            if (st == null) return null;
            try
            {
                return SerializeTransformKey(JObject.FromObject(st.ToDict()));
            }
            catch
            {
                return null;
            }
        }

        private static string SerializeTransformKey(JObject stObj)
        {
            if (stObj == null) return null;
            try
            {
                return NormalizeTransformJson(stObj).ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                return stObj.ToString(Newtonsoft.Json.Formatting.None);
            }
        }

        private static JToken NormalizeTransformJson(JToken token)
        {
            if (token is JObject obj)
            {
                var normalized = new JObject();
                foreach (var prop in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    normalized[prop.Name] = NormalizeTransformJson(prop.Value);
                }
                return normalized;
            }

            if (token is JArray arr)
            {
                var normalized = new JArray();
                foreach (var item in arr)
                {
                    normalized.Add(NormalizeTransformJson(item));
                }
                return normalized;
            }

            if (token != null && token.Type == JTokenType.Float)
            {
                return Math.Round(token.Value<double>(), 6);
            }

            return token != null ? token.DeepClone() : JValue.CreateNull();
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
            ModuleRegistry.Register("pre_process/image_flip", typeof(ImageFlip));
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
            var rectCache = new Dictionary<JToken, RotatedRect>(new JTokenEqualityComparer());

            for (int i = 0; i < results.Count; i++)
            {
                var entryToken = results[i];
                if (!(entryToken is JObject entry) || entry["type"]?.ToString() != "local")
                {
                    results[i] = null;
                    outResults.Add(entryToken);
                    continue;
                }

                var dets = entry["sample_results"] as JArray;
                if (dets == null)
                {
                    results[i] = null;
                    outResults.Add(entry);
                    continue;
                }

                var newDets = new JArray();
                for (int j = 0; j < dets.Count; j++)
                {
                    var dToken = dets[j];
                    dets[j] = null;
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
                        if (!rectCache.TryGetValue(maskRleToken, out rr))
                        {
                            if (!MaskRleUtils.TryComputeMinAreaRectFromMaskInfo(maskRleToken, out rr)) continue;
                            rectCache[maskRleToken] = rr;
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
                        // GlobalDebug.Log($"[MaskToRBox] bbox大小: {rw:F2}x{rh:F2}, 最小外接矩坐标: ({rr.Center.X:F2},{rr.Center.Y:F2}), 宽高: {rw:F2}x{rh:F2}, 角度: {angDeg:F2}");
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

                    d.Remove("mask_rle");
                    d.Remove("mask");
                    d["bbox"] = new JArray(rr.Center.X, rr.Center.Y, rw, rh, angRad);
                    d["with_angle"] = true;
                    d["angle"] = angRad;
                    newDets.Add(d);
                }

                entry["sample_results"] = newDets;
                results[i] = null;
                outResults.Add(entry);
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
    /// 合并多路图像与结果（简化逻辑，避免重复/串结果）：
    /// - 图像：按输入顺序拼接输出（主输入在前，其后按 ExtraInputsIn 顺序追加）
    /// - 结果：按输入顺序拼接输出；对 local 条目做 index/origin_index 的 offset 修正，
    ///         保证结果严格绑定到对应图像，且顺序与图像顺序一致
    ///
    /// 说明：
    /// - 不再按 transform 聚合/重建“每图一个 local 条目”。transform 在“整图单位变换”等场景下极易相同，
    ///   会导致跨图串结果/重复结果（典型现象：两个1、两个2）。
    /// - 不做去重（保持逻辑简单、可预期）。
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
            int estimatedImageCount = imageList != null ? imageList.Count : 0;
            if (ExtraInputsIn != null)
            {
                foreach (var ch in ExtraInputsIn)
                {
                    if (ch == null || ch.ImageList == null) continue;
                    estimatedImageCount += ch.ImageList.Count;
                }
            }
            var mergedImages = new List<ModuleImage>(Math.Max(0, estimatedImageCount));
            var mergedResults = new JArray();

            MergeGroup(imageList, resultList, mergedImages, mergedResults);
            if (ExtraInputsIn != null)
            {
                foreach (var ch in ExtraInputsIn)
                {
                    if (ch == null) continue;
                    MergeGroup(ch.ImageList, ch.ResultList, mergedImages, mergedResults);
                }
            }

            return new ModuleIO(mergedImages, mergedResults);
        }

        private static void MergeGroup(
            List<ModuleImage> imageList,
            JArray resultList,
            List<ModuleImage> mergedImages,
            JArray mergedResults)
        {
            int baseIndex = mergedImages.Count;
            int added = 0;

            int[] localToGlobal = null;
            if (imageList != null && imageList.Count > 0)
            {
                localToGlobal = new int[imageList.Count];
                for (int i = 0; i < localToGlobal.Length; i++) localToGlobal[i] = -1;

                for (int i = 0; i < imageList.Count; i++)
                {
                    var im = imageList[i];
                    if (im == null) continue;

                    int globalIdx = baseIndex + added;
                    localToGlobal[i] = globalIdx;
                    added++;

                    // 关键：一定要重包，确保 OriginalIndex 与全局顺序一致（Base.cs 中 OriginalIndex 为 private set，无法原地修改）
                    // 若这里退回透传，将导致多个图像的 OriginalIndex 仍为 0，进而在下游/前端按 origin_index 聚合时只保留一个。
                    mergedImages.Add(new ModuleImage(im.ImageObject, im.OriginalImage, im.TransformState, globalIdx));
                }
            }

            if (resultList == null || resultList.Count == 0) return;

            for (int i = 0; i < resultList.Count; i++)
            {
                var token = resultList[i];
                if (!(token is JObject r))
                {
                    // 非对象结构直接跳过（避免异常）
                    continue;
                }

                if (!string.Equals(r["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase))
                {
                    resultList[i] = null;
                    mergedResults.Add(r);
                    continue;
                }

                int idx = r["index"]?.Value<int?>() ?? -1;
                int oidx = r["origin_index"]?.Value<int?>() ?? idx;

                // 若本组只有 1 张图：强制把所有 local 结果都绑定到这张图（典型的 build_results 场景）
                if (added == 1)
                {
                    r["index"] = baseIndex;
                    r["origin_index"] = baseIndex;
                }
                else
                {
                    // 否则按 idx/oidx 尝试映射到全局索引
                    if (TryMapLocalIndex(localToGlobal, idx, out int gidx))
                        r["index"] = gidx;

                    if (TryMapLocalIndex(localToGlobal, oidx, out int goidx))
                        r["origin_index"] = goidx;

                    // 兜底：若 origin_index 仍缺失，则用 index 补齐
                    if ((r["origin_index"] == null || r["origin_index"].Type == JTokenType.Null) && r["index"] != null)
                    {
                        try { r["origin_index"] = r["index"].Value<int>(); } catch { }
                    }
                }

                resultList[i] = null;
                mergedResults.Add(r);
            }
        }

        private static bool TryMapLocalIndex(int[] localToGlobal, int localIdx, out int globalIdx)
        {
            globalIdx = -1;
            if (localToGlobal == null || localIdx < 0 || localIdx >= localToGlobal.Length) return false;
            int mapped = localToGlobal[localIdx];
            if (mapped < 0) return false;
            globalIdx = mapped;
            return true;
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

            // 构建 image 映射：优先 index/origin_index（折回 batch 槽位），transform+origin 兜底
            int imageCount = inImages.Count;
            var indexToImageObj = new Dictionary<int, ModuleImage>();
            var originToImageObj = new Dictionary<int, ModuleImage>();
            var keyToImageObj = new Dictionary<string, ModuleImage>();
            for (int i = 0; i < inImages.Count; i++)
            {
                var (wrap, bmp) = ImageGeneration_Unwrap(inImages[i]);
                if (bmp == null) continue;
                int originRaw = wrap != null ? wrap.OriginalIndex : i;
                int idxFolded = FoldAliasIndex(i, imageCount);
                int originFolded = FoldAliasIndex(originRaw, imageCount);
                string key = SerializeTransform(wrap != null ? wrap.TransformState : null, idxFolded, originFolded);
                indexToImageObj[idxFolded] = inImages[i];
                originToImageObj[originFolded] = inImages[i];
                keyToImageObj[key] = inImages[i];
            }

            for (int ri = 0; ri < inResults.Count; ri++)
            {
                var r = inResults[ri] as JObject;
                if (r == null) continue;
                int idxRaw = r["index"]?.Value<int?>() ?? -1;
                int originRaw = r["origin_index"]?.Value<int?>() ?? idxRaw;
                int idx = FoldAliasIndex(idxRaw, imageCount);
                int originIndex = FoldAliasIndex(originRaw, imageCount);
                var stDict = r["transform"] as JObject;
                var st = stDict != null ? TransformationState.FromDict(stDict.ToObject<Dictionary<string, object>>()) : null;
                string key = SerializeTransform(st, idx, originIndex);
                ModuleImage imgObj = null;

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

                if (!indexToImageObj.TryGetValue(idx, out imgObj))
                {
                    if (!originToImageObj.TryGetValue(originIndex, out imgObj))
                    {
                        keyToImageObj.TryGetValue(key, out imgObj);
                    }
                }

                if (imgObj != null)
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

        private static int FoldAliasIndex(int rawIndex, int imageCount)
        {
            if (imageCount <= 0) return rawIndex >= 0 ? rawIndex : 0;
            if (rawIndex < 0) return 0;
            return rawIndex % imageCount;
        }

        private static Dictionary<int, int> BuildReindexMap(bool[] flags)
        {
            var mp = new Dictionary<int, int>();
            int newIdx = 0;
            for (int i = 0; i < flags.Length; i++)
            {
                if (flags[i])
                {
                    mp[i] = newIdx++;
                }
            }
            return mp;
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
            bool noFilter = !enableBBoxWh && !enableRBoxWh && !enableBBoxArea && !enableMaskArea;
            var maskAreaCache = enableMaskArea ? new Dictionary<JToken, double>(new JTokenEqualityComparer()) : null;

            // 常见 batch 路径里，image_list 与 result_list 是按相同顺序一一对应的。
            // 这里直接走顺序匹配，避免每次都做 transform 反序列化和多张映射表构建。
            if (CanUseAlignedFastPath(inImages, inResults))
            {
                return ProcessAlignedFastPath(
                    inImages,
                    inResults,
                    enableBBoxWh,
                    enableRBoxWh,
                    enableBBoxArea,
                    enableMaskArea,
                    bboxWMin,
                    bboxWMax,
                    bboxHMin,
                    bboxHMax,
                    rboxWMin,
                    rboxWMax,
                    rboxHMin,
                    rboxHMax,
                    bboxAreaMin,
                    bboxAreaMax,
                    maskAreaMin,
                    maskAreaMax,
                    noFilter,
                    maskAreaCache);
            }

            var mainImages = new List<ModuleImage>();
            var mainResults = new JArray();
            var altImages = new List<ModuleImage>();
            var altResults = new JArray();

            // 精确标记哪些图像有通过/未通过结果，避免 transform 相同导致串图
            int imageCount = inImages.Count;
            var passFlags = new bool[imageCount];
            var failFlags = new bool[imageCount];
            var passEntries = new List<JObject>();
            var failEntries = new List<JObject>();

            var indexToImageObj = new Dictionary<int, ModuleImage>();
            var originToImageObj = new Dictionary<int, ModuleImage>();
            var keyToImageObj = new Dictionary<string, ModuleImage>();
            for (int i = 0; i < inImages.Count; i++)
            {
                var wrap = inImages[i];
                if (wrap == null || wrap.ImageObject == null) continue;
                int orgRaw = wrap.OriginalIndex;
                int idxFolded = FoldAliasIndex(i, imageCount);
                int orgFolded = FoldAliasIndex(orgRaw, imageCount);
                string key = SerializeTransformOnly(wrap.TransformState, orgFolded);
                indexToImageObj[idxFolded] = wrap;
                originToImageObj[orgFolded] = wrap;
                keyToImageObj[key] = wrap;
            }

            for (int ri = 0; ri < inResults.Count; ri++)
            {
                var r = inResults[ri] as JObject;
                if (r == null) continue;
                int idxRaw = r["index"]?.Value<int?>() ?? -1;
                int originRaw = r["origin_index"]?.Value<int?>() ?? idxRaw;
                int idx = FoldAliasIndex(idxRaw, imageCount);
                int originIndex = FoldAliasIndex(originRaw, imageCount);
                var stDict = r["transform"] as JObject;
                string key = SerializeTransformOnly(stDict, originIndex);
                ModuleImage imgObj = null;

                if (!indexToImageObj.TryGetValue(idx, out imgObj))
                {
                    if (!originToImageObj.TryGetValue(originIndex, out imgObj))
                    {
                        keyToImageObj.TryGetValue(key, out imgObj);
                    }
                }
                if (imgObj == null) continue;

                var srsArray = r["sample_results"] as JArray;
                JArray failArray = null;
                if (!noFilter && srsArray != null && srsArray.Count > 0)
                {
                    SplitAdvancedFilterSamplesFast(
                        srsArray,
                        enableBBoxWh,
                        enableRBoxWh,
                        enableBBoxArea,
                        enableMaskArea,
                        bboxWMin,
                        bboxWMax,
                        bboxHMin,
                        bboxHMax,
                        rboxWMin,
                        rboxWMax,
                        rboxHMin,
                        rboxHMax,
                        bboxAreaMin,
                        bboxAreaMax,
                        maskAreaMin,
                        maskAreaMax,
                        maskAreaCache,
                        out srsArray,
                        out failArray);
                }

                if ((srsArray != null && srsArray.Count > 0) || srsArray == null)
                {
                    passFlags[idx] = true;
                    var e = new JObject
                    {
                        ["type"] = "local",
                        ["index"] = idx,
                        ["origin_index"] = originIndex,
                        ["transform"] = stDict != null ? (JToken)stDict.DeepClone() : null,
                        ["sample_results"] = srsArray ?? new JArray()
                    };
                    passEntries.Add(e);
                }
                if (failArray != null && failArray.Count > 0)
                {
                    failFlags[idx] = true;
                    var e2 = new JObject
                    {
                        ["type"] = "local",
                        ["index"] = idx,
                        ["origin_index"] = originIndex,
                        ["transform"] = stDict != null ? (JToken)stDict.DeepClone() : null,
                        ["sample_results"] = failArray
                    };
                    failEntries.Add(e2);
                }
            }

            var passReindex = BuildReindexMap(passFlags);
            var failReindex = BuildReindexMap(failFlags);

            for (int i = 0; i < imageCount; i++)
            {
                if (passFlags[i]) mainImages.Add(inImages[i]);
                if (failFlags[i]) altImages.Add(inImages[i]);
            }

            foreach (var e in passEntries)
            {
                int imgIdx = e["index"]?.Value<int?>() ?? -1;
                if (imgIdx >= 0 && passReindex.TryGetValue(imgIdx, out int newIdx))
                {
                    e["index"] = newIdx;
                }
                mainResults.Add(e);
            }
            foreach (var e in failEntries)
            {
                int imgIdx = e["index"]?.Value<int?>() ?? -1;
                if (imgIdx >= 0 && failReindex.TryGetValue(imgIdx, out int newIdx))
                {
                    e["index"] = newIdx;
                }
                altResults.Add(e);
            }

            // 通过 extra output 暴露第二路（未通过项）
            this.ExtraOutputs.Add(new ModuleChannel(altImages, altResults));
            return new ModuleIO(mainImages, mainResults);
        }

        private ModuleIO ProcessAlignedFastPath(
            List<ModuleImage> inImages,
            JArray inResults,
            bool enableBBoxWh,
            bool enableRBoxWh,
            bool enableBBoxArea,
            bool enableMaskArea,
            double? bboxWMin,
            double? bboxWMax,
            double? bboxHMin,
            double? bboxHMax,
            double? rboxWMin,
            double? rboxWMax,
            double? rboxHMin,
            double? rboxHMax,
            double? bboxAreaMin,
            double? bboxAreaMax,
            double? maskAreaMin,
            double? maskAreaMax,
            bool noFilter,
            Dictionary<JToken, double> maskAreaCache)
        {
            var mainImages = new List<ModuleImage>();
            var mainResults = new JArray();
            var altImages = new List<ModuleImage>();
            var altResults = new JArray();

            for (int i = 0; i < inImages.Count; i++)
            {
                var imgObj = inImages[i];
                var resultObj = i < inResults.Count ? inResults[i] as JObject : null;
                if (imgObj == null || resultObj == null) continue;

                var srsArray = resultObj["sample_results"] as JArray;
                JArray failArray = null;
                if (!noFilter && srsArray != null && srsArray.Count > 0)
                {
                    SplitAdvancedFilterSamplesFast(
                        srsArray,
                        enableBBoxWh,
                        enableRBoxWh,
                        enableBBoxArea,
                        enableMaskArea,
                        bboxWMin,
                        bboxWMax,
                        bboxHMin,
                        bboxHMax,
                        rboxWMin,
                        rboxWMax,
                        rboxHMin,
                        rboxHMax,
                        bboxAreaMin,
                        bboxAreaMax,
                        maskAreaMin,
                        maskAreaMax,
                        maskAreaCache,
                        out srsArray,
                        out failArray);
                }

                int originIndex = resultObj["origin_index"]?.Value<int?>() ?? (imgObj != null ? imgObj.OriginalIndex : i);
                JToken transformToken = resultObj["transform"];

                if ((srsArray != null && srsArray.Count > 0) || srsArray == null)
                {
                    mainImages.Add(imgObj);
                    resultObj["type"] = "local";
                    resultObj["index"] = mainResults.Count;
                    resultObj["origin_index"] = originIndex;
                    if (srsArray == null) resultObj["sample_results"] = new JArray();
                    else if (!ReferenceEquals(resultObj["sample_results"], srsArray)) resultObj["sample_results"] = srsArray;
                    inResults[i] = null;
                    mainResults.Add(resultObj);
                }

                if (failArray != null && failArray.Count > 0)
                {
                    altImages.Add(imgObj);
                    altResults.Add(new JObject
                    {
                        ["type"] = "local",
                        ["index"] = altResults.Count,
                        ["origin_index"] = originIndex,
                        ["transform"] = transformToken != null ? transformToken.DeepClone() : null,
                        ["sample_results"] = failArray
                    });
                }
            }

            this.ExtraOutputs.Add(new ModuleChannel(altImages, altResults));
            return new ModuleIO(mainImages, mainResults);
        }

        private static bool CanUseAlignedFastPath(List<ModuleImage> inImages, JArray inResults)
        {
            if (inImages == null || inResults == null) return false;
            if (inImages.Count == 0 || inImages.Count != inResults.Count) return false;
            for (int i = 0; i < inResults.Count; i++)
            {
                var obj = inResults[i] as JObject;
                if (obj == null) return false;
                string type = obj["type"] != null ? obj["type"].ToString() : null;
                if (!string.Equals(type, "local", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static void SplitAdvancedFilterSamplesFast(
            JArray srsArray,
            bool enableBBoxWh,
            bool enableRBoxWh,
            bool enableBBoxArea,
            bool enableMaskArea,
            double? bboxWMin,
            double? bboxWMax,
            double? bboxHMin,
            double? bboxHMax,
            double? rboxWMin,
            double? rboxWMax,
            double? rboxHMin,
            double? rboxHMax,
            double? bboxAreaMin,
            double? bboxAreaMax,
            double? maskAreaMin,
            double? maskAreaMax,
            Dictionary<JToken, double> maskAreaCache,
            out JArray passArray,
            out JArray failArray)
        {
            passArray = new JArray();
            failArray = null;
            if (srsArray == null || srsArray.Count == 0) return;

            for (int i = 0; i < srsArray.Count; i++)
            {
                var token = srsArray[i];
                srsArray[i] = null;
                if (!(token is JObject s))
                {
                    // 与旧实现保持一致：非对象样本直接丢弃
                    continue;
                }

                if (EvaluateAdvancedFilterPass(
                    s,
                    enableBBoxWh,
                    enableRBoxWh,
                    enableBBoxArea,
                    enableMaskArea,
                    bboxWMin,
                    bboxWMax,
                    bboxHMin,
                    bboxHMax,
                    rboxWMin,
                    rboxWMax,
                    rboxHMin,
                    rboxHMax,
                    bboxAreaMin,
                    bboxAreaMax,
                    maskAreaMin,
                    maskAreaMax,
                    maskAreaCache))
                {
                    passArray.Add(s);
                    continue;
                }

                if (failArray == null) failArray = new JArray();
                failArray.Add(s);
            }
        }

        private static bool EvaluateAdvancedFilterPass(
            JObject s,
            bool enableBBoxWh,
            bool enableRBoxWh,
            bool enableBBoxArea,
            bool enableMaskArea,
            double? bboxWMin,
            double? bboxWMax,
            double? bboxHMin,
            double? bboxHMax,
            double? rboxWMin,
            double? rboxWMax,
            double? rboxHMin,
            double? rboxHMax,
            double? bboxAreaMin,
            double? bboxAreaMax,
            double? maskAreaMin,
            double? maskAreaMax,
            Dictionary<JToken, double> maskAreaCache)
        {
            if (s == null) return false;

            bool withAngle = s.Value<bool?>("with_angle") ?? false;
            var bbox = s["bbox"] as JArray;
            double w = 0.0, h = 0.0;
            bool hasWH = false;
            if (bbox != null && bbox.Count >= 4)
            {
                try
                {
                    w = Math.Abs(bbox[2].Value<double>());
                    h = Math.Abs(bbox[3].Value<double>());
                    hasWH = true;
                }
                catch { hasWH = false; }
            }

            double bboxArea = hasWH ? (w * h) : 0.0;

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
            if (pass && enableMaskArea)
            {
                var maskToken = s["mask_rle"];
                double maskArea = 0.0;
                if (maskAreaCache != null && maskToken != null)
                {
                    if (!maskAreaCache.TryGetValue(maskToken, out maskArea))
                    {
                        maskArea = MaskRleUtils.CalculateMaskArea(maskToken);
                        maskAreaCache[maskToken] = maskArea;
                    }
                }
                else
                {
                    maskArea = MaskRleUtils.CalculateMaskArea(maskToken);
                }
                if (maskArea > 0)
                {
                    pass = pass && InRange(maskArea, maskAreaMin, maskAreaMax);
                }
            }

            return pass;
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
            return $"org:{originIndex}|T:{a[0]:F4},{a[1]:F4},{a[2]:F2},{a[3]:F4},{a[4]:F4},{a[5]:F2}";
        }

        private static string SerializeTransformOnly(JObject stObj, int originIndex)
        {
            if (stObj == null) return $"org:{originIndex}|T:null";
            var affine = stObj["affine_2x3"] as JArray;
            if (affine == null || affine.Count < 6) return $"org:{originIndex}|T:null";
            try
            {
                double a0 = affine[0].Value<double>();
                double a1 = affine[1].Value<double>();
                double a2 = affine[2].Value<double>();
                double a3 = affine[3].Value<double>();
                double a4 = affine[4].Value<double>();
                double a5 = affine[5].Value<double>();
                return $"org:{originIndex}|T:{a0:F4},{a1:F4},{a2:F2},{a3:F4},{a4:F4},{a5:F2}";
            }
            catch
            {
                return $"org:{originIndex}|T:null";
            }
        }

        private static int FoldAliasIndex(int rawIndex, int imageCount)
        {
            if (imageCount <= 0) return rawIndex >= 0 ? rawIndex : 0;
            if (rawIndex < 0) return 0;
            return rawIndex % imageCount;
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
    /// 模块名称：矩形图像矫正
    /// 将竖向矩形图像旋转为横向；横图原样透传，不透传 results。
    /// 注册名：pre_process/rect_image_correction
    /// properties:
    /// - rotate_direction(string): clockwise/counterclockwise，默认 clockwise
    /// </summary>
    public class RectImageCorrection : BaseModule
    {
        static RectImageCorrection()
        {
            ModuleRegistry.Register("pre_process/rect_image_correction", typeof(RectImageCorrection));
        }

        public RectImageCorrection(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            if (images.Count == 0)
            {
                return new ModuleIO(new List<ModuleImage>(), new JArray());
            }

            string direction = NormalizeRotateDirection(ReadStringOrDefault("rotate_direction", "clockwise"));
            var outImages = new List<ModuleImage>();

            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null)
                {
                    continue;
                }

                var baseMat = wrap.ImageObject;
                if (baseMat == null || baseMat.Empty())
                {
                    outImages.Add(wrap);
                    continue;
                }

                int w = baseMat.Width;
                int h = baseMat.Height;
                if (w >= h)
                {
                    outImages.Add(wrap);
                    continue;
                }

                double[] A;
                int newW;
                int newH;
                GetRotationAffine(direction, w, h, out A, out newW, out newH);

                var rotated = RotateView(baseMat, direction, A, newW, newH);
                if (rotated == null || rotated.Empty())
                {
                    rotated?.Dispose();
                    outImages.Add(wrap);
                    continue;
                }

                var parentState = wrap.TransformState ?? new TransformationState(w, h);
                var childState = parentState.DeriveChild(A, newW, newH);
                outImages.Add(new ModuleImage(rotated, wrap.OriginalImage ?? baseMat, childState, wrap.OriginalIndex));
            }

            return new ModuleIO(outImages, new JArray());
        }

        private static string NormalizeRotateDirection(object value)
        {
            string text;
            try
            {
                text = (value == null ? "clockwise" : value.ToString()).Trim().ToLowerInvariant();
            }
            catch
            {
                return "clockwise";
            }

            if (text == "counterclockwise" || text == "ccw" || text == "left"
                || text == "逆时针" || text == "左转")
            {
                return "counterclockwise";
            }
            return "clockwise";
        }

        private static void GetRotationAffine(string direction, int width, int height, out double[] A, out int newWidth, out int newHeight)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            if (direction == "counterclockwise")
            {
                // 90 deg counterclockwise: x' = y, y' = w - 1 - x
                A = new double[] { 0.0, 1.0, 0.0, -1.0, 0.0, w - 1.0 };
                newWidth = h;
                newHeight = w;
                return;
            }

            // 90 deg clockwise: x' = h - 1 - y, y' = x
            A = new double[] { 0.0, -1.0, h - 1.0, 1.0, 0.0, 0.0 };
            newWidth = h;
            newHeight = w;
        }

        private static Mat RotateView(Mat image, string direction, double[] affine, int newWidth, int newHeight)
        {
            try
            {
                using (var matA = new Mat(2, 3, MatType.CV_64FC1))
                {
                    matA.Set(0, 0, affine[0]);
                    matA.Set(0, 1, affine[1]);
                    matA.Set(0, 2, affine[2]);
                    matA.Set(1, 0, affine[3]);
                    matA.Set(1, 1, affine[4]);
                    matA.Set(1, 2, affine[5]);

                    var rotated = new Mat();
                    Cv2.WarpAffine(image, rotated, matA, new Size(newWidth, newHeight));
                    return rotated;
                }
            }
            catch
            {
                var rotated = new Mat();
                var flag = direction == "counterclockwise"
                    ? RotateFlags.Rotate90Counterclockwise
                    : RotateFlags.Rotate90Clockwise;
                Cv2.Rotate(image, rotated, flag);
                return rotated;
            }
        }
    }

    /// <summary>
    /// 模块名称：按比例缩放
    /// 按比例缩放（scale=2 表示宽高各缩小 2 倍）并透传结果；注册名：pre_process/image_rescale, features/image_rescale
    /// properties: scale(double>0)
    /// </summary>
    public class ImageRescale : BaseModule
    {
        static ImageRescale()
        {
            ModuleRegistry.Register("pre_process/image_rescale", typeof(ImageRescale));
            ModuleRegistry.Register("features/image_rescale", typeof(ImageRescale));
        }

        public ImageRescale(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();
            double scale = ReadDoubleLike("scale", 1.0);
            if (scale <= 0) scale = 1.0;
            var outImages = new List<ModuleImage>();

            foreach (var wrap in images)
            {
                if (wrap == null || wrap.ImageObject == null || wrap.ImageObject.Empty()) continue;
                var baseMat = wrap.ImageObject;
                int W = Math.Max(1, baseMat.Width), H = Math.Max(1, baseMat.Height);
                int tw = Math.Max(1, (int)Math.Round(W / scale));
                int th = Math.Max(1, (int)Math.Round(H / scale));
                var resized = new Mat();
                Cv2.Resize(baseMat, resized, new Size(tw, th));
                var A = new double[] { (double)tw / W, 0, 0, 0, (double)th / H, 0 };
                var parentState = wrap.TransformState ?? new TransformationState(W, H);
                var childState = parentState.DeriveChild(A, tw, th);
                outImages.Add(new ModuleImage(resized, wrap.OriginalImage ?? baseMat, childState, wrap.OriginalIndex));
            }
            return new ModuleIO(outImages, results);
        }

        private double ReadDoubleLike(string key, double dv)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                try { return Convert.ToDouble(v); } catch { return dv; }
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
            ModuleRegistry.Register("pre_process/image_rotate_by_cls", typeof(ImageRotateByClassification));
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

    /// <summary>
    /// 模块名称：结果标签合并
    /// 对齐 Python: post_process/result_label_merge
    /// - 输入两路 (image, results)，要求是同一组图像
    /// - 输出 image 透传第 2 路；results 以第 2 路为基准
    /// - 将第 2 路 local 条目中的 category_name 改为：
    ///   first_label + fixed_text + second_label
    /// </summary>
    public class ResultLabelMerge : BaseModule
    {
        static ResultLabelMerge()
        {
            ModuleRegistry.Register("post_process/result_label_merge", typeof(ResultLabelMerge));
            ModuleRegistry.Register("features/result_label_merge", typeof(ResultLabelMerge));
        }

        public ResultLabelMerge(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var imagesA = imageList ?? new List<ModuleImage>();
            var resultsA = resultList ?? new JArray();

            // Python 侧第二路来自 extra_inputs_in[1]；C# 执行器中的扩展路仅包含“额外输入”，
            // 因此这里取 ExtraInputsIn[0] 作为第二路。
            var pair1 = (ExtraInputsIn != null && ExtraInputsIn.Count > 0) ? ExtraInputsIn[0] : null;
            var imagesB = pair1 != null ? (pair1.ImageList ?? new List<ModuleImage>()) : new List<ModuleImage>();
            var resultsB = pair1 != null ? (pair1.ResultList ?? new JArray()) : new JArray();

            if (imagesA.Count == 0 && resultsA.Count == 0 && imagesB.Count == 0 && resultsB.Count == 0)
            {
                return new ModuleIO(new List<ModuleImage>(), new JArray());
            }

            if (imagesB.Count == 0)
            {
                throw new InvalidOperationException("结果标签合并需要第2路输入（image_2/results_2）。");
            }

            if (imagesA.Count != imagesB.Count)
            {
                throw new InvalidOperationException(
                    string.Format("两路输入图像数量不一致: {0} vs {1}", imagesA.Count, imagesB.Count)
                );
            }

            for (int i = 0; i < imagesA.Count; i++)
            {
                if (!ImageSignature(imagesA[i]).Equals(ImageSignature(imagesB[i])))
                {
                    throw new InvalidOperationException(string.Format("两路输入不是同一组图像，index={0}", i));
                }
            }

            string fixedText = GetPropertyString("fixed_text", string.Empty);
            bool useTop1 = GetPropertyBool("use_first_score_top1", true);

            // 第一路标签映射：entry.index -> top1 category_name
            var labelMap = new Dictionary<int, string>();
            foreach (var token in resultsA)
            {
                var entry = token as JObject;
                if (entry == null) continue;
                if (!string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase)) continue;

                int idx = SafeInt(entry["index"], -1);
                if (idx < 0) continue;

                var dets = entry["sample_results"] as JArray;
                if (dets == null) continue;

                string firstLabel = PickFirstLabel(dets, useTop1);
                if (!string.IsNullOrWhiteSpace(firstLabel))
                {
                    labelMap[idx] = firstLabel;
                }
            }

            var outResults = new JArray();
            foreach (var token in resultsB)
            {
                var entry = token as JObject;
                if (entry == null || !string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase))
                {
                    outResults.Add(token);
                    continue;
                }

                int idx = SafeInt(entry["index"], -1);
                string prefix = labelMap.ContainsKey(idx) ? (labelMap[idx] ?? string.Empty) : string.Empty;
                var dets = entry["sample_results"] as JArray;
                if (dets == null || string.IsNullOrEmpty(prefix))
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

                    var cat = d["category_name"]?.ToString();
                    if (cat == null)
                    {
                        newDets.Add(d);
                        continue;
                    }

                    var d2 = (JObject)d.DeepClone();
                    d2["category_name"] = prefix + fixedText + cat;
                    newDets.Add(d2);
                }

                var e2 = (JObject)entry.DeepClone();
                e2["sample_results"] = newDets;
                outResults.Add(e2);
            }

            return new ModuleIO(imagesB, outResults);
        }

        private string GetPropertyString(string key, string defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                return v.ToString();
            }
            return defaultValue;
        }

        private bool GetPropertyBool(string key, bool defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                if (v is bool b) return b;
                if (bool.TryParse(v.ToString(), out bool pb)) return pb;
            }
            return defaultValue;
        }

        private static int SafeInt(JToken token, int defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            try { return token.Value<int>(); } catch { return defaultValue; }
        }

        private static double SafeScore(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return 0.0;
            try { return token.Value<double>(); } catch { return 0.0; }
        }

        private static string PickFirstLabel(JArray dets, bool useTop1)
        {
            if (dets == null || dets.Count == 0) return null;
            var candidates = new List<JObject>();
            foreach (var dToken in dets)
            {
                var d = dToken as JObject;
                if (d == null) continue;
                if (d["category_name"] == null || d["category_name"].Type != JTokenType.String) continue;
                candidates.Add(d);
            }
            if (candidates.Count == 0) return null;

            if (!useTop1)
            {
                return candidates[0].Value<string>("category_name");
            }

            JObject top = candidates[0];
            double topScore = SafeScore(top["score"]);
            for (int i = 1; i < candidates.Count; i++)
            {
                var cur = candidates[i];
                double curScore = SafeScore(cur["score"]);
                if (curScore > topScore)
                {
                    top = cur;
                    topScore = curScore;
                }
            }
            return top.Value<string>("category_name");
        }

        private static string ImageSignature(ModuleImage im)
        {
            if (im == null) return "null";
            string ts = string.Empty;
            try
            {
                ts = im.TransformState != null ? JObject.FromObject(im.TransformState.ToDict()).ToString(Newtonsoft.Json.Formatting.None) : string.Empty;
            }
            catch
            {
                ts = string.Empty;
            }
            return string.Format("module|{0}|{1}", im.OriginalIndex, ts);
        }
    }

    /// <summary>
    /// 模块名称：BBox 重叠去重
    /// 对齐 Python: post_process/bbox_iou_dedup
    /// - 仅处理 type == "local" 的条目
    /// - 从 sample_results 提取 bbox=[x,y,w,h]，内部转换为 xyxy 后按分组与面积从大到小去重
    /// - metric 支持 iou / ios；per_category 控制是否按类别分别去重
    /// - cross_model 默认 true：跨不同模型分支按原图指纹分组，并将 bbox 映射到原图坐标比较；false 时按 index/origin_index/transform 严格分组
    /// </summary>
    public class BBoxIoUDedup : BaseModule
    {
        private sealed class Candidate
        {
            public int EntryIndex;
            public int DetIndex;
            public double[] BBox;
            public double Area;
        }

        static BBoxIoUDedup()
        {
            ModuleRegistry.Register("post_process/bbox_iou_dedup", typeof(BBoxIoUDedup));
            ModuleRegistry.Register("features/bbox_iou_dedup", typeof(BBoxIoUDedup));
        }

        public BBoxIoUDedup(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

            string metric = NormalizeMetric(GetPropertyString("metric", "iou"));
            double threshold = Clamp01(GetPropertyDouble("iou_threshold", 0.5));
            bool perCategory = GetPropertyBool("per_category", true);
            bool crossModel = GetPropertyBool("cross_model", true);
            var imageGroups = crossModel ? BuildImageGroupsByIndex(images) : new Dictionary<int, string>();
            string singleImageGroup = null;
            if (imageGroups.Count > 0)
            {
                var uniqueGroups = new List<string>();
                foreach (string group in imageGroups.Values)
                {
                    if (!uniqueGroups.Contains(group)) uniqueGroups.Add(group);
                }
                if (uniqueGroups.Count == 1) singleImageGroup = uniqueGroups[0];
            }

            var keepFlags = new Dictionary<int, List<bool>>();
            var grouped = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);

            for (int entryIdx = 0; entryIdx < results.Count; entryIdx++)
            {
                var entry = results[entryIdx] as JObject;
                if (entry == null) continue;
                if (!string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase)) continue;

                var dets = entry["sample_results"] as JArray;
                if (dets == null) continue;

                var flags = new List<bool>(dets.Count);
                for (int i = 0; i < dets.Count; i++) flags.Add(true);
                keepFlags[entryIdx] = flags;

                int idx = SafeInt(entry["index"], -1);
                int originIdx = SafeInt(entry["origin_index"], idx);
                string transformSig = TransformGroupKey(entry["transform"]);
                string entryGroupKey = crossModel
                    ? BuildCrossModelGroupKey(entry, imageGroups, singleImageGroup)
                    : BuildStrictGroupKey(idx, originIdx, transformSig);

                for (int detIdx = 0; detIdx < dets.Count; detIdx++)
                {
                    var det = dets[detIdx] as JObject;
                    if (det == null) continue;

                    if (!TryExtractBboxXyxy(det, out double[] bbox)) continue;
                    if (crossModel)
                    {
                        bbox = BBoxToGlobalXyxy(bbox, entry["transform"]);
                    }

                    double area = BBoxArea(bbox);
                    if (area <= 0.0) continue;

                    string groupKey;
                    if (perCategory)
                    {
                        string catId = SerializeTokenCompact(det["category_id"]);
                        string catName = SerializeTokenCompact(det["category_name"]);
                        groupKey = string.Format("{0}|{1}|{2}", entryGroupKey, catId, catName);
                    }
                    else
                    {
                        groupKey = entryGroupKey + "|__all__";
                    }

                    if (!grouped.TryGetValue(groupKey, out List<Candidate> list))
                    {
                        list = new List<Candidate>();
                        grouped[groupKey] = list;
                    }
                    list.Add(new Candidate
                    {
                        EntryIndex = entryIdx,
                        DetIndex = detIdx,
                        BBox = bbox,
                        Area = area
                    });
                }
            }

            int removedCount = 0;
            foreach (var kv in grouped)
            {
                var items = kv.Value;
                items.Sort((a, b) =>
                {
                    int cmpArea = b.Area.CompareTo(a.Area);
                    if (cmpArea != 0) return cmpArea;
                    int cmpEntry = a.EntryIndex.CompareTo(b.EntryIndex);
                    if (cmpEntry != 0) return cmpEntry;
                    return a.DetIndex.CompareTo(b.DetIndex);
                });

                var keptBoxes = new List<double[]>();
                foreach (var item in items)
                {
                    bool shouldDrop = false;
                    for (int i = 0; i < keptBoxes.Count; i++)
                    {
                        if (IsOverlapExceeded(item.BBox, keptBoxes[i], threshold, metric))
                        {
                            shouldDrop = true;
                            break;
                        }
                    }

                    if (shouldDrop)
                    {
                        if (keepFlags.TryGetValue(item.EntryIndex, out List<bool> flags) &&
                            item.DetIndex >= 0 &&
                            item.DetIndex < flags.Count)
                        {
                            flags[item.DetIndex] = false;
                        }
                        removedCount++;
                        continue;
                    }

                    keptBoxes.Add(item.BBox);
                }
            }

            int keptCount = 0;
            var outResults = new JArray();
            for (int entryIdx = 0; entryIdx < results.Count; entryIdx++)
            {
                var entry = results[entryIdx] as JObject;
                if (entry == null || !string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase))
                {
                    outResults.Add(results[entryIdx]);
                    continue;
                }

                var dets = entry["sample_results"] as JArray;
                if (dets == null)
                {
                    outResults.Add(entry);
                    continue;
                }

                if (!keepFlags.TryGetValue(entryIdx, out List<bool> flags) || flags == null)
                {
                    keptCount += dets.Count;
                    outResults.Add(entry);
                    continue;
                }

                var newDets = new JArray();
                for (int detIdx = 0; detIdx < dets.Count; detIdx++)
                {
                    if (detIdx < flags.Count && flags[detIdx])
                    {
                        newDets.Add(dets[detIdx]);
                    }
                }
                keptCount += newDets.Count;

                if (newDets.Count == dets.Count)
                {
                    outResults.Add(entry);
                    continue;
                }

                var newEntry = (JObject)entry.DeepClone();
                newEntry["sample_results"] = newDets;
                outResults.Add(newEntry);
            }

            ScalarOutputsByName["kept_count"] = keptCount;
            ScalarOutputsByName["removed_count"] = removedCount;

            return new ModuleIO(images, outResults);
        }

        private string GetPropertyString(string key, string defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                string s = v.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return defaultValue;
        }

        private static Dictionary<int, string> BuildImageGroupsByIndex(List<ModuleImage> images)
        {
            var groups = new Dictionary<int, string>();
            if (images == null) return groups;

            for (int i = 0; i < images.Count; i++)
            {
                string fingerprint = ImageFingerprint(images[i]);
                if (!string.IsNullOrEmpty(fingerprint))
                {
                    groups[i] = fingerprint;
                }
            }
            return groups;
        }

        private static string BuildCrossModelGroupKey(JObject entry, Dictionary<int, string> imageGroups, string singleImageGroup)
        {
            int idx = SafeInt(entry["index"], -1);
            int originIdx = SafeInt(entry["origin_index"], idx);

            string group;
            if (imageGroups != null)
            {
                if (imageGroups.TryGetValue(originIdx, out group)) return "image|" + group;
                if (imageGroups.TryGetValue(idx, out group)) return "image|" + group;
            }

            if (!string.IsNullOrEmpty(singleImageGroup)) return "image|" + singleImageGroup;
            string transformSig = TransformGroupKey(entry["transform"]);
            return BuildStrictGroupKey(idx, originIdx, transformSig);
        }

        private static string BuildStrictGroupKey(int idx, int originIdx, string transformSig)
        {
            return string.Format("{0}|{1}|{2}", idx, originIdx, transformSig);
        }

        private static string ImageFingerprint(ModuleImage image)
        {
            try
            {
                if (image == null) return null;
                Mat mat = image.OriginalImage;
                if (mat == null || mat.Empty()) mat = image.ImageObject;
                if (mat == null || mat.Empty()) return null;

                using (var md5 = MD5.Create())
                {
                    string header = string.Format(
                        "{0}x{1}x{2}:{3}",
                        mat.Rows,
                        mat.Cols,
                        mat.Channels(),
                        mat.Type());
                    AppendBytes(md5, Encoding.UTF8.GetBytes(header));

                    const int patchHeight = 8;
                    const int patchWidth = 8;
                    var coords = new[]
                    {
                        Tuple.Create(0, 0),
                        Tuple.Create(0, Math.Max(0, mat.Cols - patchWidth)),
                        Tuple.Create(Math.Max(0, mat.Rows - patchHeight), 0),
                        Tuple.Create(Math.Max(0, mat.Rows - patchHeight), Math.Max(0, mat.Cols - patchWidth)),
                        Tuple.Create(Math.Max(0, mat.Rows / 2 - patchHeight / 2), Math.Max(0, mat.Cols / 2 - patchWidth / 2))
                    };

                    foreach (var coord in coords)
                    {
                        int yy = coord.Item1;
                        int xx = coord.Item2;
                        int width = Math.Min(patchWidth, mat.Cols - xx);
                        int height = Math.Min(patchHeight, mat.Rows - yy);
                        if (width <= 0 || height <= 0) continue;

                        using (var patch = new Mat(mat, new Rect(xx, yy, width, height)))
                        using (var contiguous = patch.Clone())
                        {
                            long byteCountLong = contiguous.Total() * contiguous.ElemSize();
                            if (byteCountLong <= 0 || byteCountLong > int.MaxValue) continue;
                            var bytes = new byte[(int)byteCountLong];
                            Marshal.Copy(contiguous.Data, bytes, 0, bytes.Length);
                            AppendBytes(md5, bytes);
                        }
                    }

                    md5.TransformFinalBlock(new byte[0], 0, 0);
                    return BitConverter.ToString(md5.Hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return null;
            }
        }

        private static void AppendBytes(HashAlgorithm hash, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        private double GetPropertyDouble(string key, double defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                try { return Convert.ToDouble(v); } catch { }
            }
            return defaultValue;
        }

        private bool GetPropertyBool(string key, bool defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                if (v is bool b) return b;
                string s = v.ToString();
                if (bool.TryParse(s, out bool pb)) return pb;
                if (int.TryParse(s, out int pi)) return pi != 0;
                s = (s ?? string.Empty).Trim().ToLowerInvariant();
                if (s == "yes" || s == "y" || s == "on") return true;
                if (s == "no" || s == "n" || s == "off") return false;
            }
            return defaultValue;
        }

        private static int SafeInt(JToken token, int defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            try { return token.Value<int>(); } catch { return defaultValue; }
        }

        private static double SafeDouble(JToken token, double defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            try { return token.Value<double>(); } catch { return defaultValue; }
        }

        private static string SerializeTokenCompact(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "null";
            try { return token.ToString(Newtonsoft.Json.Formatting.None); } catch { return token.ToString(); }
        }

        private static string TransformGroupKey(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "__global__";
            if (IsIdentityTransform(token as JObject)) return "__global__";
            return SerializeTokenCompact(token);
        }

        private static bool IsIdentityTransform(JObject transform)
        {
            if (transform == null) return false;

            bool hasOriginalSize = TryReadOriginalSize(transform, out int originalW, out int originalH);
            bool hasCrop = TryReadCropBox(transform, out int cropX, out int cropY, out int cropW, out int cropH);
            bool hasOutput = TryReadOutputSize(transform, out int outputW, out int outputH);
            bool hasAffine = TryReadAffine(transform, out double[] affine);

            if (!hasCrop && !hasAffine)
            {
                return hasOriginalSize;
            }

            if (hasCrop)
            {
                if (cropX != 0 || cropY != 0) return false;
                if (hasOutput && (outputW != cropW || outputH != cropH)) return false;
                if (hasOriginalSize && (cropW != originalW || cropH != originalH)) return false;
            }
            else if (hasOutput && hasOriginalSize && (outputW != originalW || outputH != originalH))
            {
                return false;
            }

            if (hasAffine && !IsIdentityAffine(affine)) return false;
            return hasAffine || hasCrop;
        }

        private static bool TryReadOriginalSize(JObject transform, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (transform == null) return false;

            var originalSize = transform["original_size"] as JArray;
            if (originalSize != null && originalSize.Count >= 2)
            {
                width = SafeInt(originalSize[0], 0);
                height = SafeInt(originalSize[1], 0);
                return width > 0 && height > 0;
            }

            width = SafeInt(transform["original_width"], 0);
            height = SafeInt(transform["original_height"], 0);
            return width > 0 && height > 0;
        }

        private static bool TryReadCropBox(JObject transform, out int x, out int y, out int width, out int height)
        {
            x = 0;
            y = 0;
            width = 0;
            height = 0;
            var crop = transform?["crop_box"] as JArray;
            if (crop == null || crop.Count < 4) return false;
            x = SafeInt(crop[0], 0);
            y = SafeInt(crop[1], 0);
            width = SafeInt(crop[2], 0);
            height = SafeInt(crop[3], 0);
            return width > 0 && height > 0;
        }

        private static bool TryReadOutputSize(JObject transform, out int width, out int height)
        {
            width = 0;
            height = 0;
            var output = transform?["output_size"] as JArray;
            if (output == null || output.Count < 2) return false;
            width = SafeInt(output[0], 0);
            height = SafeInt(output[1], 0);
            return width > 0 && height > 0;
        }

        private static bool TryReadAffine(JObject transform, out double[] affine)
        {
            affine = null;
            var flat = transform?["affine_2x3"] as JArray;
            if (flat != null && flat.Count >= 6)
            {
                affine = new[]
                {
                    SafeDouble(flat[0], 0.0),
                    SafeDouble(flat[1], 0.0),
                    SafeDouble(flat[2], 0.0),
                    SafeDouble(flat[3], 0.0),
                    SafeDouble(flat[4], 0.0),
                    SafeDouble(flat[5], 0.0)
                };
                return true;
            }

            var matrix = transform?["affine_matrix"] as JArray;
            if (matrix == null || matrix.Count < 2) return false;
            var row0 = matrix[0] as JArray;
            var row1 = matrix[1] as JArray;
            if (row0 == null || row1 == null || row0.Count < 3 || row1.Count < 3) return false;

            affine = new[]
            {
                SafeDouble(row0[0], 0.0),
                SafeDouble(row0[1], 0.0),
                SafeDouble(row0[2], 0.0),
                SafeDouble(row1[0], 0.0),
                SafeDouble(row1[1], 0.0),
                SafeDouble(row1[2], 0.0)
            };
            return true;
        }

        private static bool IsIdentityAffine(double[] affine)
        {
            if (affine == null || affine.Length < 6) return false;
            const double eps = 1e-6;
            return Math.Abs(affine[0] - 1.0) < eps
                && Math.Abs(affine[1]) < eps
                && Math.Abs(affine[2]) < eps
                && Math.Abs(affine[3]) < eps
                && Math.Abs(affine[4] - 1.0) < eps
                && Math.Abs(affine[5]) < eps;
        }

        private static bool TryExtractBboxXyxy(JObject det, out double[] bbox)
        {
            bbox = null;
            var arr = det["bbox"] as JArray;
            if (arr == null || arr.Count != 4) return false;

            double x;
            double y;
            double w;
            double h;
            try
            {
                x = arr[0].Value<double>();
                y = arr[1].Value<double>();
                w = arr[2].Value<double>();
                h = arr[3].Value<double>();
            }
            catch
            {
                return false;
            }

            double x1 = x;
            double y1 = y;
            double x2 = x + w;
            double y2 = y + h;

            if (x2 < x1)
            {
                double t = x1;
                x1 = x2;
                x2 = t;
            }
            if (y2 < y1)
            {
                double t = y1;
                y1 = y2;
                y2 = t;
            }
            if (x2 <= x1 || y2 <= y1) return false;

            bbox = new[] { x1, y1, x2, y2 };
            return true;
        }

        private static double[] BBoxToGlobalXyxy(double[] bboxLocal, JToken transformToken)
        {
            if (bboxLocal == null || bboxLocal.Length < 4) return bboxLocal;
            var transform = transformToken as JObject;
            if (transform == null || IsIdentityTransform(transform)) return bboxLocal;
            if (!TryBuildCurrentToOriginal(transform, out double[] currentToOriginal)) return bboxLocal;

            var points = new[]
            {
                TransformPoint(currentToOriginal, bboxLocal[0], bboxLocal[1]),
                TransformPoint(currentToOriginal, bboxLocal[2], bboxLocal[1]),
                TransformPoint(currentToOriginal, bboxLocal[2], bboxLocal[3]),
                TransformPoint(currentToOriginal, bboxLocal[0], bboxLocal[3])
            };

            double minX = points[0].Item1;
            double minY = points[0].Item2;
            double maxX = points[0].Item1;
            double maxY = points[0].Item2;
            for (int i = 1; i < points.Length; i++)
            {
                minX = Math.Min(minX, points[i].Item1);
                minY = Math.Min(minY, points[i].Item2);
                maxX = Math.Max(maxX, points[i].Item1);
                maxY = Math.Max(maxY, points[i].Item2);
            }
            return new[] { minX, minY, maxX, maxY };
        }

        private static bool TryBuildCurrentToOriginal(JObject transform, out double[] currentToOriginal)
        {
            currentToOriginal = null;
            if (transform == null) return false;

            double[] originalToCurrent;
            var flat = transform["affine_2x3"] as JArray;
            if (flat != null && flat.Count >= 6)
            {
                originalToCurrent = new[]
                {
                    SafeDouble(flat[0], 0.0),
                    SafeDouble(flat[1], 0.0),
                    SafeDouble(flat[2], 0.0),
                    SafeDouble(flat[3], 0.0),
                    SafeDouble(flat[4], 0.0),
                    SafeDouble(flat[5], 0.0)
                };
            }
            else
            {
                if (!TryReadCropBox(transform, out int cropX, out int cropY, out _, out _)) return false;
                var matrix = transform["affine_matrix"] as JArray;
                if (matrix == null || matrix.Count < 2) return false;
                var row0 = matrix[0] as JArray;
                var row1 = matrix[1] as JArray;
                if (row0 == null || row1 == null || row0.Count < 3 || row1.Count < 3) return false;

                double a00 = SafeDouble(row0[0], 0.0);
                double a01 = SafeDouble(row0[1], 0.0);
                double a02 = SafeDouble(row0[2], 0.0);
                double a10 = SafeDouble(row1[0], 0.0);
                double a11 = SafeDouble(row1[1], 0.0);
                double a12 = SafeDouble(row1[2], 0.0);
                originalToCurrent = new[]
                {
                    a00,
                    a01,
                    a02 - a00 * cropX - a01 * cropY,
                    a10,
                    a11,
                    a12 - a10 * cropX - a11 * cropY
                };
            }

            try
            {
                currentToOriginal = TransformationState.Inverse2x3(originalToCurrent);
                return true;
            }
            catch
            {
                currentToOriginal = null;
                return false;
            }
        }

        private static Tuple<double, double> TransformPoint(double[] t, double x, double y)
        {
            if (t == null || t.Length < 6) return Tuple.Create(x, y);
            return Tuple.Create(t[0] * x + t[1] * y + t[2], t[3] * x + t[4] * y + t[5]);
        }

        private static double BBoxArea(double[] bbox)
        {
            if (bbox == null || bbox.Length < 4) return 0.0;
            return Math.Max(0.0, bbox[2] - bbox[0]) * Math.Max(0.0, bbox[3] - bbox[1]);
        }

        private static string NormalizeMetric(string metricRaw)
        {
            string m = (metricRaw ?? "iou").Trim().ToLowerInvariant();
            if (m == "ios") return "ios";
            return "iou";
        }

        private static double Clamp01(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            if (v < 0.0) return 0.0;
            if (v > 1.0) return 1.0;
            return v;
        }

        private static bool IsOverlapExceeded(double[] bbox, double[] keptBbox, double threshold, string metric)
        {
            if (string.Equals(metric, "ios", StringComparison.OrdinalIgnoreCase))
            {
                return ComputeIoS(bbox, keptBbox) > threshold;
            }
            return ComputeIoU(bbox, keptBbox) > threshold;
        }

        private static double ComputeIoU(double[] a, double[] b)
        {
            double inter = IntersectionArea(a, b);
            if (inter <= 0.0) return 0.0;
            double areaA = BBoxArea(a);
            double areaB = BBoxArea(b);
            double union = areaA + areaB - inter;
            if (union <= 0.0) return 0.0;
            return inter / union;
        }

        private static double ComputeIoS(double[] a, double[] b)
        {
            double inter = IntersectionArea(a, b);
            if (inter <= 0.0) return 0.0;
            double areaA = BBoxArea(a);
            double areaB = BBoxArea(b);
            double smaller = Math.Min(areaA, areaB);
            if (smaller <= 0.0) return 0.0;
            return inter / smaller;
        }

        private static double IntersectionArea(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length < 4 || b.Length < 4) return 0.0;
            double x1 = Math.Max(a[0], b[0]);
            double y1 = Math.Max(a[1], b[1]);
            double x2 = Math.Min(a[2], b[2]);
            double y2 = Math.Min(a[3], b[3]);
            double w = Math.Max(0.0, x2 - x1);
            double h = Math.Max(0.0, y2 - y1);
            return w * h;
        }
    }
}
