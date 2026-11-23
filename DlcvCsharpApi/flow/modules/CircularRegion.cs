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
        private sealed class CircleOutline
        {
            public readonly int Width, Height, Radius, Area;
            public readonly JArray Bbox, Polygon;

            public CircleOutline(int width, int height, int radius)
            {
                Width = width;
                Height = height;
                Radius = radius;
                int cx = width / 2, cy = height / 2;
                int x = Math.Max(0, cx - radius), y = Math.Max(0, cy - radius);
                int w = (int)Math.Min(width, (long)cx + radius + 1) - x;
                int h = (int)Math.Min(height, (long)cy + radius + 1) - y;
                Bbox = new JArray(x, y, w, h);
                Polygon = new JArray();
                long dx = Math.Max(cx, width - 1 - cx), dy = Math.Max(cy, height - 1 - cy);
                if ((long)radius * radius >= dx * dx + dy * dy)
                {
                    Area = w * h;
                    AddRectangle(x, y, w, h);
                    return;
                }
                using (var mask = new Mat(h, w, MatType.CV_8UC1, Scalar.Black))
                {
                    Cv2.Circle(mask, new Point(cx - x, cy - y), radius, Scalar.All(255), -1);
                    Area = Cv2.CountNonZero(mask);
                    Cv2.FindContours(mask, out Point[][] contours, out HierarchyIndex[] _, RetrievalModes.External,
                        ContourApproximationModes.ApproxSimple, new Point(x, y));
                    foreach (var point in contours[0]) Polygon.Add(new JArray(point.X, point.Y));
                    while (Polygon.Count < 3) Polygon.Add(Polygon.Last.DeepClone());
                }
            }

            private void AddRectangle(int x, int y, int width, int height)
            {
                int right = x + width - 1, bottom = y + height - 1;
                Polygon.Add(new JArray(x, y));
                Polygon.Add(new JArray(right, y));
                Polygon.Add(new JArray(right, bottom));
                Polygon.Add(new JArray(x, bottom));
            }
        }

        private static volatile CircleOutline lastOutline;

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
                if (image == null || image.Empty()) throw new ArgumentException("输入图像不能为空");
                var outline = lastOutline;
                if (outline == null || outline.Width != image.Width || outline.Height != image.Height || outline.Radius != radius)
                    lastOutline = outline = new CircleOutline(image.Width, image.Height, radius);
                var detection = new JObject
                {
                    ["category_id"] = 0,
                    ["category_name"] = categoryName,
                    ["score"] = 1.0,
                    ["bbox"] = outline.Bbox.DeepClone(),
                    ["area"] = (double)outline.Area,
                    ["polygon"] = outline.Polygon.DeepClone(),
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
    }
}
