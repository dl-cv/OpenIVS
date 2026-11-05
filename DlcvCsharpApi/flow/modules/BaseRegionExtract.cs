using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    public class BaseRegionExtract : BaseModule
    {
        static BaseRegionExtract() { ModuleRegistry.Register("post_process/base_region_extract", typeof(BaseRegionExtract)); }

        public BaseRegionExtract(int nodeId, string title = null,
            Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList == null ? new JArray() : (JArray)resultList.DeepClone();
            string category = OriginalImageClassification.Property(Properties, "category_name", new JValue("底座")).ToString().Trim();
            if (category.Length == 0) category = "底座";
            for (int i = 0; i < images.Count; i++)
            {
                var image = images[i]?.GetImage();
                if (image == null || image.Empty()) continue;
                int[] box;
                try { box = ExtractBox(image); }
                catch (ArgumentException) { continue; }
                var state = images[i].TransformState ?? new TransformationState(image.Width, image.Height);
                results.Add(new JObject
                {
                    ["type"] = "local", ["originating_module"] = "post_process/base_region_extract",
                    ["index"] = i, ["origin_index"] = images[i].OriginalIndex, ["transform"] = JObject.FromObject(state.ToDict()),
                    ["sample_results"] = new JArray(new JObject
                    {
                        ["category_name"] = category, ["category_id"] = 0, ["score"] = 1.0,
                        ["bbox"] = new JArray(box[0], box[1], box[2] - box[0], box[3] - box[1]),
                        ["width"] = box[2] - box[0], ["height"] = box[3] - box[1], ["with_angle"] = false
                    })
                });
            }
            return new ModuleIO(images, results);
        }

        private int[] ExtractBox(Mat image)
        {
            int h = image.Height, w = image.Width;
            if (h < 20 || w < 20) throw new ArgumentException("图像尺寸过小");
            int kernel = (int)Number("blur_kernel", 5, 1, int.MaxValue - 1);
            if (kernel % 2 == 0) kernel++;
            using (var gray = new Mat())
            using (var smooth = new Mat())
            using (var gradient = new Mat())
            {
                // Flow 三通道输入为 RGB；Python 读图端使用 BGR，灰度需保持同一物理颜色。
                if (image.Channels() == 1) image.CopyTo(gray);
                else Cv2.CvtColor(image, gray, image.Channels() == 4 ? ColorConversionCodes.RGBA2GRAY : ColorConversionCodes.RGB2GRAY);
                Cv2.GaussianBlur(gray, smooth, new Size(kernel, kernel), 0);
                Cv2.Sobel(smooth, gradient, MatType.CV_32F, 1, 0, 3);
                var score = new float[w];
                var column = new float[h];
                double percentile = Number("edge_percentile", 90, 50, 99.9);
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++) column[y] = Math.Abs(gradient.At<float>(y, x));
                    Array.Sort(column);
                    score[x] = (float)PercentileSorted(column, percentile);
                }
                score = Smooth(score, 5);
                int mid = w / 2, begin = Math.Max(1, (int)Math.Round(w * 0.02));
                int end = Math.Max(mid + 1, Math.Min(w - 1, (int)Math.Round(w * 0.98)));
                int left = ArgMax(score, begin, mid), outer = ArgMax(score, mid, end);
                int start = Math.Max(mid, outer - Math.Max(3, (int)Math.Round(w * Number("right_pair_span_ratio", 0.05, 0.005, 0.2))));
                double rightThreshold = score[outer] * Number("right_edge_strength_ratio", 0.3, 0.01, 1);
                int right = outer;
                if (score[outer] > 1)
                {
                    while (start < outer && score[start] < rightThreshold) start++;
                    int stop = start + 1;
                    while (stop <= outer && score[stop] >= rightThreshold) stop++;
                    right = ArgMax(score, start, stop);
                }
                if (right - left < w * Number("min_width_ratio", 0.12, 0.01, 0.95) || (score[left] <= 1 && score[right] <= 1))
                    throw new ArgumentException("未找到可用的左右边界");
                var boundary = new float[h];
                for (int y = 0; y < h; y++)
                    for (int x = Math.Max(0, left - 2); x < Math.Min(w, left + 3); x++)
                        boundary[y] = Math.Max(boundary[y], Math.Abs(gradient.At<float>(y, x)));
                boundary = Smooth(boundary, 5);
                float peak = boundary[ArgMax(boundary, 0, h)];
                if (peak <= 1) throw new ArgumentException("左边界连续边线不足");
                using (var rows = new Mat(h, 1, MatType.CV_8U))
                using (var closed = new Mat())
                using (var element = new Mat(Math.Max(1, (int)Math.Round(h * 0.01)) * 2 + 1, 1, MatType.CV_8U, Scalar.All(1)))
                {
                    double threshold = peak * Number("line_strength_ratio", 0.25, 0.01, 1);
                    for (int y = 0; y < h; y++) rows.Set(y, 0, (byte)(boundary[y] >= threshold ? 1 : 0));
                    Cv2.MorphologyEx(rows, closed, MorphTypes.Close, element);
                    int top = -1, longest = 0, run = 0;
                    for (int y = 0; y <= h; y++)
                    {
                        if (y < h && closed.At<byte>(y, 0) != 0) run++;
                        else { if (run > longest) { longest = run; top = y - run; } run = 0; }
                    }
                    if (top < 0) throw new ArgumentException("无法确定底座顶部范围");
                    int bottom = NotchTop(gray, left, right, top);
                    if (bottom <= top) throw new ArgumentException("无法确定底座凹槽上沿");
                    return new[] { left, top, right, bottom };
                }
            }
        }

        private int NotchTop(Mat gray, int left, int right, int top)
        {
            int h = gray.Height;
            var means = new float[h];
            using (var roi = new Mat(gray, new Rect(left, 0, right - left, h)))
            using (var reduced = new Mat())
            {
                Cv2.Reduce(roi, reduced, ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_64F);
                for (int y = 0; y < h; y++) means[y] = (float)reduced.At<double>(y, 0);
            }
            var sorted = (float[])means.Clone(); Array.Sort(sorted);
            double threshold = Math.Max(10, (PercentileSorted(sorted, 90) - PercentileSorted(sorted, 10)) * Number("notch_drop_ratio", 0.15, 0.01, 1));
            int band = Math.Max(2, (int)Math.Round(h * Number("notch_band_ratio", 0.02, 0.005, 0.1)));
            var drop = new float[h];
            for (int y = band; y < h - band; y++) drop[y] = Sum(means, y - band, band) / band - Sum(means, y, band) / band;
            drop = Smooth(drop, 15);
            int start = Math.Max(top + 1, (int)Math.Round(h * Number("bottom_search_start_ratio", 0.55, 0, 0.95)));
            while (start < h && drop[start] <= threshold) start++;
            if (start == h) throw new ArgumentException("未找到底座下方凹槽上沿");
            int end = start + 1;
            while (end < h && drop[end] > threshold) end++;
            int peak = ArgMax(drop, start, end);
            return PercentileSorted(sorted, 50) >= Number("bright_product_threshold", 120, 0, 255)
                ? start + (int)Math.Round((peak - start) * Number("bright_transition_position_ratio", 0.25, 0, 1)) : peak;
        }

        private double Number(string key, double fallback, double min, double max)
        {
            var token = OriginalImageClassification.Property(Properties, key, new JValue(fallback));
            return Math.Min(max, Math.Max(min, OriginalImageClassification.TryNumber(token, out double value) ? value : fallback));
        }

        private static int ArgMax(float[] values, int start, int end)
        {
            int best = start;
            for (int i = start + 1; i < end; i++) if (values[i] > values[best]) best = i;
            return best;
        }

        private static double PercentileSorted(float[] values, double percentile)
        {
            double index = (values.Length - 1) * percentile / 100;
            int lower = (int)index, upper = Math.Min(lower + 1, values.Length - 1);
            return values[lower] + (values[upper] - (double)values[lower]) * (index - lower);
        }

        private static float[] Smooth(float[] values, int kernel)
        {
            using (var input = new Mat(1, values.Length, MatType.CV_32F))
            using (var output = new Mat())
            {
                for (int i = 0; i < values.Length; i++) input.Set(0, i, values[i]);
                Cv2.GaussianBlur(input, output, new Size(kernel, 1), 0);
                var result = new float[values.Length];
                for (int i = 0; i < values.Length; i++) result[i] = output.At<float>(0, i);
                return result;
            }
        }

        // 对齐 NumPy float32 mean 的成对累加，避免凹槽峰值受求和精度影响。
        private static float Sum(float[] values, int start, int count)
        {
            if (count < 8) { float total = 0; for (int i = 0; i < count; i++) total += values[start + i]; return total; }
            if (count > 128) { int half = count / 2; half -= half % 8; return Sum(values, start, half) + Sum(values, start + half, count - half); }
            var sums = new float[8]; Array.Copy(values, start, sums, 0, 8);
            int offset = 8;
            for (; offset < count - count % 8; offset += 8)
                for (int i = 0; i < 8; i++) sums[i] += values[start + offset + i];
            float sum = ((sums[0] + sums[1]) + (sums[2] + sums[3])) + ((sums[4] + sums[5]) + (sums[6] + sums[7]));
            for (; offset < count; offset++) sum += values[start + offset];
            return sum;
        }
    }
}
