using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using ZXingCpp;

namespace DlcvModules
{
    /// <summary>复用 ZXing-C++ 解码，图像保持不变；局部框和折线由输出节点映射回原图。</summary>
    public class BarcodeReader : BaseModule
    {
        static BarcodeReader()
        {
            ModuleRegistry.Register("features/barcode_reader", typeof(BarcodeReader));
        }

        public BarcodeReader(int nodeId, string title = null,
            Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context) { }

        private bool Option(string key)
        {
            if (!Properties.TryGetValue(key, out object value)) return true;
            if (value is bool option) return option;
            if (value is JValue token && token.Type == JTokenType.Boolean) return token.Value<bool>();
            throw new ArgumentException(key + " must be a boolean");
        }

        private static JArray ToDetections(Barcode[] codes)
        {
            var detections = new JArray();
            if (codes == null) return detections;
            foreach (var code in codes)
            {
                var pos = code.Position;
                var points = new[] { pos.TopLeft, pos.TopRight, pos.BottomRight, pos.BottomLeft };
                int x = points.Min(p => p.X), y = points.Min(p => p.Y);
                int width = points.Max(p => p.X) - x, height = points.Max(p => p.Y) - y;
                double area = 0;
                for (int j = 0; j < 4; j++) area += (double)points[j].X * points[(j + 1) % 4].Y - (double)points[j].Y * points[(j + 1) % 4].X;
                var line = new JArray(points.Select(p => new JArray(p.X, p.Y)));
                line.Add(new JArray(points[0].X, points[0].Y));
                detections.Add(new JObject {
                    ["category_id"] = 0, ["category_name"] = code.Text, ["score"] = 1.0,
                    ["bbox"] = new JArray(x, y, width, height), ["area"] = Math.Abs(area) / 2,
                    ["with_bbox"] = true, ["with_angle"] = false, ["angle"] = -100, ["with_mask"] = false,
                    ["extra_info"] = new JObject { ["barcode_format"] = code.Format.ToString(), ["polyline"] = line }
                });
            }
            return detections;
        }

        private static bool SameRegion(JToken first, JToken second)
        {
            if ((string)first["extra_info"]["barcode_format"] != (string)second["extra_info"]["barcode_format"]) return false;
            var firstLine = first["extra_info"]["polyline"];
            var secondLine = second["extra_info"]["polyline"];
            if (JToken.DeepEquals(firstLine, secondLine)) return true;
            double area = Math.Min((double)first["area"], (double)second["area"]);
            if (area <= 0) return false;
            var firstPoints = firstLine.Take(4).Select(p => new Point2f((float)p[0], (float)p[1]));
            var secondPoints = secondLine.Take(4).Select(p => new Point2f((float)p[0], (float)p[1]));
            return Cv2.IntersectConvexConvex(firstPoints, secondPoints, out Point2f[] intersection) >= area * 0.8;
        }

        private static JArray ReadAtBothScales(ZXingCpp.BarcodeReader reader, ImageView view)
        {
            var detections = new JArray();
            foreach (bool downscale in new[] { false, true })
            {
                int originalCount = detections.Count;
                reader.TryDownscale = downscale;
                Barcode[] codes = reader.From(view) ?? new Barcode[0];
                try
                {
                    foreach (var candidate in ToDetections(codes))
                    {
                        // 同区域的缩放误码优先保留原分辨率结果，其他区域的有效码仍须补齐。
                        if (!downscale || !detections.Take(originalCount).Any(original => SameRegion(original, candidate)))
                            detections.Add(candidate);
                    }
                }
                finally
                {
                    foreach (var code in codes) code.Dispose();
                }
            }
            return detections;
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = new JArray();
            string formats = "";
            if (Properties.TryGetValue("formats", out object value))
            {
                if (value is string text) formats = text;
                else if (value is JValue token && token.Type == JTokenType.String) formats = token.Value<string>();
                else throw new ArgumentException("formats must be a comma-separated string");
            }
            bool rotate = Option("try_rotate"), invert = Option("try_invert");
            if (images.Count == 0) return new ModuleIO(images, results);
            using (var reader = new ZXingCpp.BarcodeReader())
            {
                reader.TryRotate = rotate;
                reader.TryInvert = invert;
                reader.TextMode = TextMode.HRI;
                if (!string.IsNullOrWhiteSpace(formats))
                {
                    if (!BarcodeFormats.TryParse(formats, out BarcodeFormats parsed))
                        throw new ArgumentException("Invalid barcode formats: " + formats);
                    reader.Formats = parsed;
                }
                for (int i = 0; i < images.Count; i++)
                {
                    var wrap = images[i];
                    Mat image = wrap?.GetImage();
                    if (image == null || image.Empty()) throw new ArgumentException("Barcode reader requires a non-empty image");
                    int channels = image.Channels();
                    if (image.Depth() != MatType.CV_8U || (channels != 1 && channels != 3 && channels != 4))
                        throw new ArgumentException("Barcode reader requires uint8 gray, RGB or RGBA images");
                    var format = channels == 1 ? ImageFormat.Lum : channels == 3 ? ImageFormat.RGB : ImageFormat.RGBA;
                    var view = new ImageView(image.Data, image.Width, image.Height, format, checked((int)image.Step()));
                    Mat gray = null, enhanced = null;
                    try
                    {
                        var detections = ReadAtBothScales(reader, view);
                        if (detections.Count == 0)
                        {
                            gray = new Mat();
                            enhanced = new Mat();
                            if (channels == 1) image.CopyTo(gray);
                            else Cv2.CvtColor(image, gray, channels == 3 ? ColorConversionCodes.RGB2GRAY : ColorConversionCodes.RGBA2GRAY);
                            Cv2.EqualizeHist(gray, enhanced);
                            view = new ImageView(enhanced.Data, enhanced.Width, enhanced.Height, ImageFormat.Lum, checked((int)enhanced.Step()));
                            detections = ReadAtBothScales(reader, view);
                        }
                        var entry = JObject.FromObject(wrap.ToMeta());
                        entry["type"] = "local";
                        entry["index"] = i;
                        entry["sample_results"] = detections;
                        results.Add(entry);
                    }
                    finally
                    {
                        gray?.Dispose();
                        enhanced?.Dispose();
                        GC.KeepAlive(view);
                        GC.KeepAlive(image);
                    }
                }
            }
            return new ModuleIO(images, results);
        }
    }
}
