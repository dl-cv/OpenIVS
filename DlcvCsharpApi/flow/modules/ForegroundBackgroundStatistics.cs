using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    /// <summary>前景背景统计：将结果选区映射回原图，取原图全部通道像素，图像保持不变。</summary>
    public class ForegroundBackgroundStatistics : BaseModule
    {
        static ForegroundBackgroundStatistics()
        {
            ModuleRegistry.Register("post_process/foreground_background_statistics", typeof(ForegroundBackgroundStatistics));
        }

        public ForegroundBackgroundStatistics(int nodeId, string title = null,
            Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            bool mean = ReadOption("mean", true), median = ReadOption("median", false);
            var images = imageList ?? new List<ModuleImage>();
            var results = new JArray();
            foreach (JToken token in resultList ?? new JArray())
            {
                if (!(token is JObject original)) throw new ArgumentException("结果必须为对象");
                var entry = (JObject)original.DeepClone();
                results.Add(entry);
                if (entry.Value<string>("type") != "local") continue;
                JToken detections = entry["sample_results"];
                if (detections == null) detections = entry["sample_results"] = new JArray();
                if (!(detections is JArray samples)) throw new ArgumentException("local.sample_results 必须为数组");
                ModuleImage selected = null;
                foreach (JToken sample in samples)
                {
                    if (!(sample is JObject det)) throw new ArgumentException("sample_results 中的目标必须为对象");
                    foreach (string name in new[] { "mean", "median" })
                    {
                        det.Remove("with_" + name);
                        det.Remove("foreground_" + name);
                        det.Remove("background_" + name);
                    }
                    double?[] values = null;
                    bool sampled = false;
                    if (mean || median)
                    {
                        using (Mat mask = DecodeMask(det))
                        {
                            if (!mask.Empty())
                            {
                                if (selected == null) selected = SelectImage(images, entry);
                                values = Measure(selected, entry, det, mask, mean, median, out sampled);
                            }
                        }
                    }
                    if (mean) SetStatistic(det, "mean", sampled, values?[0], values?[1]);
                    if (median) SetStatistic(det, "median", sampled, values?[2], values?[3]);
                }
            }
            return new ModuleIO(images, results);
        }

        private bool ReadOption(string key, bool defaultValue)
        {
            if (!Properties.TryGetValue(key, out object value)) return defaultValue;
            if (value is bool boolean) return boolean;
            if (value is JToken token && token.Type == JTokenType.Boolean) return token.Value<bool>();
            throw new ArgumentException(key + " 必须为 bool");
        }

        private static void SetStatistic(JObject det, string name, bool sampled, double? foreground, double? background)
        {
            det["with_" + name] = sampled;
            det["foreground_" + name] = foreground.HasValue ? new JValue(foreground.Value) : JValue.CreateNull();
            det["background_" + name] = background.HasValue ? new JValue(background.Value) : JValue.CreateNull();
        }

        private static Mat DecodeMask(JObject det)
        {
            if (det["mask_array"] != null)
            {
                if (!(det["mask_array"] is JArray rows) || rows.Count == 0 ||
                    !(rows[0] is JArray first) || first.Count == 0 || (long)rows.Count * first.Count > int.MaxValue)
                    return new Mat();
                var bytes = new byte[rows.Count * first.Count];
                for (int y = 0; y < rows.Count; y++)
                {
                    if (!(rows[y] is JArray row) || row.Count != first.Count) return new Mat();
                    for (int x = 0; x < row.Count; x++)
                    {
                        if (row[x].Type != JTokenType.Integer) return new Mat();
                        long value = row[x].Value<long>();
                        if (value < 0 || value > 255) return new Mat();
                        bytes[y * first.Count + x] = (byte)value;
                    }
                }
                var raw = new Mat(rows.Count, first.Count, MatType.CV_8UC1);
                Marshal.Copy(bytes, 0, raw.Data, bytes.Length);
                return raw;
            }
            if (!(det["mask_rle"] is JObject mask) || !(mask["runs"] is JArray runs)) return new Mat();
            if (mask["width"]?.Type != JTokenType.Integer || mask["height"]?.Type != JTokenType.Integer) return new Mat();
            long width = mask.Value<long>("width"), height = mask.Value<long>("height");
            if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue || width * height > int.MaxValue)
                return new Mat();
            long total = 0;
            foreach (JToken run in runs)
            {
                if (run.Type != JTokenType.Integer) return new Mat();
                long count = run.Value<long>();
                if (count < 0 || count > width * height - total) return new Mat();
                total += count;
            }
            return total == width * height ? MaskRleUtils.MaskInfoToMat(mask) : new Mat();
        }

        private static int? ReadIndex(JToken token, string name)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.Integer) throw new ArgumentException(name + " 必须为非负整数");
            long value = token.Value<long>();
            if (value < 0 || value > int.MaxValue) throw new ArgumentException(name + " 必须为非负整数");
            return (int)value;
        }

        private static ModuleImage SelectImage(List<ModuleImage> images, JObject entry)
        {
            int? origin = ReadIndex(entry["origin_index"], "origin_index"), index = ReadIndex(entry["index"], "index");
            var candidates = new List<int>();
            var matching = new List<int>();
            for (int i = 0; i < images.Count; i++)
            {
                ModuleImage wrap = images[i];
                if (wrap == null || (origin.HasValue && wrap.OriginalIndex != origin.Value)) continue;
                candidates.Add(i);
                if (entry["transform"] is JObject transform && wrap.TransformState != null &&
                    JToken.DeepEquals(transform, JObject.FromObject(wrap.TransformState.ToDict()))) matching.Add(i);
            }
            if (origin.HasValue)
            {
                if (index.HasValue && matching.Contains(index.Value)) return images[index.Value];
                if (matching.Count == 1) return images[matching[0]];
            }
            if (index.HasValue && candidates.Contains(index.Value)) return images[index.Value];
            if (candidates.Count == 1 && (origin.HasValue || !index.HasValue)) return images[candidates[0]];
            throw new ArgumentException("无法确定结果所属图像，请提供有效 index、origin_index 与 transform");
        }

        private static double[] Matrix(TransformationState state)
        {
            double[] matrix = state?.AffineMatrix2x3 ?? new double[] { 1, 0, 0, 0, 1, 0 };
            if (matrix.Length != 6) throw new ArgumentException("transform.affine_2x3 必须含 6 个数值");
            foreach (double value in matrix)
                if (!Finite(value)) throw new ArgumentException("transform.affine_2x3 必须为有限数值");
            double determinant = matrix[0] * matrix[4] - matrix[1] * matrix[3];
            if (!Finite(determinant)) throw new ArgumentException("transform.affine_2x3 行列式超出有限数值范围");
            TransformationState.Inverse2x3(matrix);
            return matrix;
        }

        private static TransformationState ReadSource(JObject entry, int originalWidth, int originalHeight)
        {
            if (entry["transform"] == null || entry["transform"].Type == JTokenType.Null)
                return new TransformationState(originalWidth, originalHeight);
            if (!(entry["transform"] is JObject transform)) throw new ArgumentException("transform 必须为对象");
            int? width = ReadIndex(transform["original_width"], "transform.original_width");
            int? height = ReadIndex(transform["original_height"], "transform.original_height");
            if (width != originalWidth || height != originalHeight) throw new ArgumentException("结果与图像的原图尺寸不一致");
            double[] affine = null;
            if (transform["affine_2x3"] != null)
            {
                if (!(transform["affine_2x3"] is JArray array) || array.Count != 6)
                    throw new ArgumentException("transform.affine_2x3 必须含 6 个数值");
                affine = new double[6];
                for (int i = 0; i < 6; i++)
                {
                    if (array[i].Type != JTokenType.Integer && array[i].Type != JTokenType.Float)
                        throw new ArgumentException("transform.affine_2x3 必须为数值数组");
                    affine[i] = array[i].Value<double>();
                }
            }
            int[] output = null;
            if (transform["output_size"] != null)
            {
                if (!(transform["output_size"] is JArray size) || size.Count != 2)
                    throw new ArgumentException("transform.output_size 必须含 2 个正整数");
                int? w = ReadIndex(size[0], "transform.output_size"), h = ReadIndex(size[1], "transform.output_size");
                if (!w.HasValue || !h.HasValue || w <= 0 || h <= 0) throw new ArgumentException("transform.output_size 必须含 2 个正整数");
                output = new[] { w.Value, h.Value };
            }
            return new TransformationState(originalWidth, originalHeight, affine2x3: affine, outputSize: output);
        }

        private static double?[] Measure(ModuleImage wrap, JObject entry, JObject det, Mat mask,
            bool mean, bool median, out bool sampled)
        {
            sampled = false;
            Mat image = wrap.OriginalImage;
            if (image == null || image.Empty()) throw new ArgumentException("统计需要有效原图");
            TransformationState current = wrap.TransformState ?? new TransformationState(image.Width, image.Height);
            if (current.OriginalWidth != image.Width || current.OriginalHeight != image.Height)
                throw new ArgumentException("原图尺寸与 transform 不一致");
            Matrix(current);
            if (current.OutputSize != null && (current.OutputSize.Length != 2 || current.OutputSize[0] <= 0 || current.OutputSize[1] <= 0))
                throw new ArgumentException("transform.output_size 必须含 2 个正整数");
            TransformationState source = ReadSource(entry, image.Width, image.Height);
            // 选区来自结果坐标；仅逆变换回原图，不采样前置处理或插值后的图像。
            double[] mapping = TransformationState.Inverse2x3(Matrix(source));
            foreach (double value in mapping)
                if (!Finite(value)) throw new ArgumentException("统计坐标转换超出有限数值范围");
            int[] sourceSize = source.OutputSize ?? new[] { image.Width, image.Height };
            if (!(det["bbox"] is JArray bbox) || (bbox.Count != 4 && bbox.Count != 5))
                throw new ArgumentException("bbox 必须为 XYWH 或含弧度角的旋转框");
            var box = new double[bbox.Count];
            for (int i = 0; i < box.Length; i++)
            {
                if (bbox[i].Type != JTokenType.Float && bbox[i].Type != JTokenType.Integer)
                    throw new ArgumentException("bbox 必须为数值数组");
                box[i] = bbox[i].Value<double>();
                if (!Finite(box[i])) throw new ArgumentException("bbox 必须为有限数值");
            }
            if (box[2] <= 0 || box[3] <= 0) return null;
            double left = box[0], top = box[1], right = box[0] + box[2], bottom = box[1] + box[3];
            double cos = 1, sin = 0;
            if (box.Length == 5)
            {
                cos = Cardinal(Math.Cos(box[4])); sin = Cardinal(Math.Sin(box[4]));
                double halfWidth = Math.Abs(cos) * box[2] / 2 + Math.Abs(sin) * box[3] / 2;
                double halfHeight = Math.Abs(sin) * box[2] / 2 + Math.Abs(cos) * box[3] / 2;
                left = box[0] - halfWidth; right = box[0] + halfWidth;
                top = box[1] - halfHeight; bottom = box[1] + halfHeight;
            }
            // 旋转选区的最大整数坐标仍可能在框内；候选范围多包含一个像素，不改变 mask 缩放范围。
            double candidateRight = box.Length == 5 ? Math.Floor(right) + 1 : Math.Ceiling(right);
            double candidateBottom = box.Length == 5 ? Math.Floor(bottom) + 1 : Math.Ceiling(bottom);
            left = Math.Floor(left); top = Math.Floor(top); right = Math.Ceiling(right); bottom = Math.Ceiling(bottom);
            if (!Finite(left) || !Finite(top) || !Finite(right) || !Finite(bottom)) throw new ArgumentException("bbox 尺寸超出数值范围");
            int x1 = Clip(left, sourceSize[0]), y1 = Clip(top, sourceSize[1]);
            int x2 = Clip(candidateRight, sourceSize[0]), y2 = Clip(candidateBottom, sourceSize[1]);
            if (x2 <= x1 || y2 <= y1) return null;
            using (var foreground = new Mat(y2 - y1, x2 - x1, MatType.CV_8UC1, Scalar.Black))
            using (var domain = new Mat(y2 - y1, x2 - x1, MatType.CV_8UC1, Scalar.Black))
            {
                bool fullMask = mask.Width == sourceSize[0] && mask.Height == sourceSize[1];
                // 与完整 INTER_NEAREST resize 的逆比例计算顺序一致，裁图不改变缩放范围。
                double scaleX = 1.0 / ((right - left) / mask.Width), scaleY = 1.0 / ((bottom - top) / mask.Height);
                for (int y = y1; y < y2; y++)
                    for (int x = x1; x < x2; x++)
                    {
                        double u = (x - box[0]) * cos + (y - box[1]) * sin;
                        double v = -(x - box[0]) * sin + (y - box[1]) * cos;
                        if (box.Length == 5 && (u < -box[2] / 2 || u >= box[2] / 2 || v < -box[3] / 2 || v >= box[3] / 2)) continue;
                        int mx = fullMask ? x : Clip(Math.Floor((x - left) * scaleX), mask.Width - 1);
                        int my = fullMask ? y : Clip(Math.Floor((y - top) * scaleY), mask.Height - 1);
                        domain.Set(y - y1, x - x1, (byte)1);
                        foreground.Set(y - y1, x - x1, mask.At<byte>(my, mx) > 127 ? (byte)1 : (byte)0);
                    }
                mapping[2] += mapping[0] * x1 + mapping[1] * y1;
                mapping[5] += mapping[3] * x1 + mapping[4] * y1;
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                foreach (double y in new[] { -0.5, foreground.Height - 0.5 })
                    foreach (double x in new[] { -0.5, foreground.Width - 0.5 })
                    {
                        double dx = mapping[0] * x + mapping[1] * y + mapping[2];
                        double dy = mapping[3] * x + mapping[4] * y + mapping[5];
                        minX = Math.Min(minX, dx); maxX = Math.Max(maxX, dx);
                        minY = Math.Min(minY, dy); maxY = Math.Max(maxY, dy);
                    }
                int dx1 = Clip(Math.Floor(minX + 0.5), image.Width), dy1 = Clip(Math.Floor(minY + 0.5), image.Height);
                int dx2 = Clip(Math.Ceiling(maxX + 0.5), image.Width), dy2 = Clip(Math.Ceiling(maxY + 0.5), image.Height);
                if (dx2 <= dx1 || dy2 <= dy1) return null;
                mapping[2] -= dx1; mapping[5] -= dy1;
                using (var matrix = new Mat(2, 3, MatType.CV_64FC1))
                using (var warpedForeground = new Mat())
                using (var warpedDomain = new Mat())
                using (var roi = new Mat(image, new Rect(dx1, dy1, dx2 - dx1, dy2 - dy1)))
                using (var pixels = new Mat())
                {
                    for (int i = 0; i < 6; i++) matrix.Set(i / 3, i % 3, mapping[i]);
                    var size = new Size(dx2 - dx1, dy2 - dy1);
                    Cv2.WarpAffine(foreground, warpedForeground, matrix, size, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.Black);
                    Cv2.WarpAffine(domain, warpedDomain, matrix, size, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.Black);
                    roi.ConvertTo(pixels, MatType.CV_64F);
                    var sums = new double[2];
                    // 按最大绝对值缩放，避免有限大数累加溢出。
                    var scales = new double[2];
                    long foregroundCount = Cv2.CountNonZero(warpedForeground);
                    var counts = new[] { foregroundCount * pixels.Channels(),
                        ((long)Cv2.CountNonZero(warpedDomain) - foregroundCount) * pixels.Channels() };
                    var values = median ? new[] { new List<double>(), new List<double>() } : null;
                    var row = new double[pixels.Width * pixels.Channels()];
                    for (int y = 0; y < pixels.Height; y++)
                    {
                        Marshal.Copy(pixels.Ptr(y), row, 0, row.Length);
                        for (int x = 0; x < pixels.Width; x++)
                        {
                            if (warpedDomain.At<byte>(y, x) == 0) continue;
                            int group = warpedForeground.At<byte>(y, x) > 0 ? 0 : 1;
                            for (int channel = 0; channel < pixels.Channels(); channel++)
                            {
                                double value = row[x * pixels.Channels() + channel];
                                if (!Finite(value)) throw new ArgumentException("统计区域图像包含非有限数值");
                                if (mean) scales[group] = Math.Max(scales[group], Math.Abs(value));
                                if (median) values[group].Add(value);
                            }
                        }
                    }
                    if (mean)
                    {
                        for (int y = 0; y < pixels.Height; y++)
                        {
                            Marshal.Copy(pixels.Ptr(y), row, 0, row.Length);
                            for (int x = 0; x < pixels.Width; x++)
                            {
                                if (warpedDomain.At<byte>(y, x) == 0) continue;
                                int group = warpedForeground.At<byte>(y, x) > 0 ? 0 : 1;
                                if (scales[group] == 0) continue;
                                for (int channel = 0; channel < pixels.Channels(); channel++)
                                    sums[group] += row[x * pixels.Channels() + channel] / scales[group];
                            }
                        }
                    }
                    sampled = counts[0] + counts[1] > 0;
                    var statistics = new double?[4];
                    for (int group = 0; group < 2; group++)
                    {
                        if (counts[group] == 0) continue;
                        if (mean) statistics[group] = Math.Max(-1, Math.Min(1, sums[group] / counts[group])) * scales[group];
                        if (median)
                        {
                            values[group].Sort();
                            int middle = values[group].Count / 2;
                            statistics[2 + group] = values[group][middle];
                            if (values[group].Count % 2 == 0)
                            {
                                double leftValue = values[group][middle - 1], rightValue = values[group][middle];
                                double scale = Math.Max(Math.Abs(leftValue), Math.Abs(rightValue));
                                statistics[2 + group] = scale == 0 ? 0 : (leftValue / scale + rightValue / scale) / 2 * scale;
                            }
                        }
                        if ((mean && !Finite(statistics[group].Value)) || (median && !Finite(statistics[2 + group].Value)))
                            throw new ArgumentException("统计结果超出有限数值范围");
                    }
                    return statistics;
                }
            }
        }

        private static double Cardinal(double value) => Math.Abs(value) < 1e-12 ? 0 :
            Math.Abs(Math.Abs(value) - 1) < 1e-12 ? Math.Sign(value) : value;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static int Clip(double value, int limit) => (int)Math.Max(0, Math.Min(limit, value));
    }
}
