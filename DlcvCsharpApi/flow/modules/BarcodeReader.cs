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
                    Barcode[] codes = reader.From(view);
                    var detections = new JArray();
                    try
                    {
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
                    }
                    finally
                    {
                        foreach (var code in codes) code.Dispose();
                        GC.KeepAlive(view);
                        GC.KeepAlive(image);
                    }
                    var entry = JObject.FromObject(wrap.ToMeta());
                    entry["type"] = "local";
                    entry["index"] = i;
                    entry["sample_results"] = detections;
                    results.Add(entry);
                }
            }
            return new ModuleIO(images, results);
        }
    }
}
