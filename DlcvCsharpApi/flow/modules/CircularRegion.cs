using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    /// <summary>在图像中心生成圆形实例分割结果，图像保持不变。</summary>
    public class CircularRegion : BaseModule
    {
        static CircularRegion()
        {
            ModuleRegistry.Register("pre_process/circular_region", typeof(CircularRegion));
        }

        public CircularRegion(int nodeId, string title = null,
            Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList != null ? (JArray)resultList.DeepClone() : new JArray();
            string categoryName = ReadCategoryName();
            int radius = ReadRadius();
            for (int i = 0; i < images.Count; i++)
            {
                if (images[i] == null) throw new ArgumentException("输入图像不能为空");
                Mat image = images[i].GetImage();
                using (Mat mask = BuildCircleMask(image, radius))
                {
                    if (image == null || image.Empty()) throw new ArgumentException("输入图像不能为空");
                    int x1 = Math.Max(0, image.Width / 2 - radius);
                    int y1 = Math.Max(0, image.Height / 2 - radius);
                    int x2 = (int)Math.Min((long)image.Width, (long)image.Width / 2 + radius + 1);
                    int y2 = (int)Math.Min((long)image.Height, (long)image.Height / 2 + radius + 1);
                    var detection = new JObject
                    {
                        ["category_id"] = 0,
                        ["category_name"] = categoryName,
                        ["score"] = 1.0,
                        ["bbox"] = new JArray(x1, y1, x2 - x1, y2 - y1),
                        ["area"] = (double)Cv2.CountNonZero(mask),
                        ["mask_array"] = MaskToJArray(mask),
                        ["with_bbox"] = true,
                        ["with_mask"] = true,
                        ["with_angle"] = false,
                        ["angle"] = -100.0
                    };
                    var entry = JObject.FromObject(images[i].ToMeta());
                    entry["type"] = "local";
                    entry["originating_module"] = "pre_process/circular_region";
                    entry["index"] = i;
                    entry["sample_results"] = new JArray(detection);
                    results.Add(entry);
                }
            }
            return new ModuleIO(images, results);
        }

        private string ReadCategoryName()
        {
            if (!Properties.TryGetValue("categoryname", out object value) || value == null)
                return "圆形区域";
            if (value is JValue token) value = token.Value;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            text = text == null ? "" : text.Trim();
            return string.IsNullOrEmpty(text) ? "圆形区域" : text;
        }

        private int ReadRadius()
        {
            if (!Properties.TryGetValue("radius", out object value)) return 5;
            if (value is JValue token) value = token.Value;
            if (value == null || value is bool) throw new ArgumentException("radius 必须为正整数");
            double number;
            if (value is string text)
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    throw new ArgumentException("radius 必须为正整数");
            }
            else
            {
                try { number = Convert.ToDouble(value, CultureInfo.InvariantCulture); }
                catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
                { throw new ArgumentException("radius 必须为正整数", ex); }
            }
            if (double.IsNaN(number) || double.IsInfinity(number) || number <= 0 || number > int.MaxValue)
                throw new ArgumentException("radius 必须为正整数");
            return (int)number;
        }

        private static Mat BuildCircleMask(Mat image, int radius)
        {
            if (image == null || image.Empty()) throw new ArgumentException("输入图像不能为空");
            int centerX = image.Width / 2;
            int centerY = image.Height / 2;
            int x1 = Math.Max(0, centerX - radius);
            int y1 = Math.Max(0, centerY - radius);
            int x2 = (int)Math.Min((long)image.Width, (long)centerX + radius + 1);
            int y2 = (int)Math.Min((long)image.Height, (long)centerY + radius + 1);
            var mask = new Mat(y2 - y1, x2 - x1, MatType.CV_8UC1, Scalar.All(0));
            Cv2.Circle(mask, new Point(centerX - x1, centerY - y1), radius, Scalar.All(255), -1);
            return mask;
        }

        private static JArray MaskToJArray(Mat mask)
        {
            var rows = new JArray();
            for (int y = 0; y < mask.Rows; y++)
            {
                var row = new JArray();
                for (int x = 0; x < mask.Cols; x++) row.Add((int)mask.At<byte>(y, x));
                rows.Add(row);
            }
            return rows;
        }
    }
}