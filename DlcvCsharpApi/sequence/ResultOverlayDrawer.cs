using System;
using System.Collections.Generic;
using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using DrawingColor = System.Drawing.Color;
using DrawingPoint = System.Drawing.Point;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;

namespace DLCV.SequenceGraph
{
    public static class ResultOverlayDrawer
    {
        public static object Draw(object image, object result, JObject props, out Dictionary<string, object> okInfo)
        {
            okInfo = new Dictionary<string, object>
            {
                { "ok", true },
                { "reason", "" },
                { "det_count", 0 }
            };
            var mat = ToMat(image);
            if (mat == null || mat.Empty())
                return image;
            return Draw(mat, result, props, out okInfo);
        }

        public static Mat Draw(Mat imageBgr, object result, JObject props, out Dictionary<string, object> okInfo)
        {
            okInfo = new Dictionary<string, object>
            {
                { "ok", true },
                { "reason", "" },
                { "det_count", 0 }
            };
            if (imageBgr == null || imageBgr.Empty())
                return imageBgr;

            var vis = imageBgr.Clone();
            var drawMode = props != null && props["draw_mode"] != null ? props["draw_mode"].ToString() : "overlay";
            if (string.Equals(drawMode, "none", StringComparison.OrdinalIgnoreCase) || result == null)
            {
                if (GetBool(props, "display_ok", true))
                {
                    DrawLabelsGdi(vis, new List<LabelItem>
                    {
                        new LabelItem
                        {
                            Text = "OK",
                            X = 20,
                            Y = 12,
                            Color = new Scalar(0, 200, 0),
                            FontSize = Math.Max(20f, GetInt(props, "ok_font_size", 40))
                        }
                    });
                }
                return vis;
            }

            var dets = ExtractDetections(result);
            okInfo["det_count"] = dets.Count;
            bool? okFlag = ExtractOk(result);
            bool ok = okFlag.HasValue ? okFlag.Value : dets.Count == 0;
            string reason = ExtractReason(result);
            if (string.IsNullOrEmpty(reason))
                reason = ok ? "" : ("det=" + dets.Count);
            okInfo["ok"] = ok;
            okInfo["reason"] = reason;

            var bboxColor = ParseColor(props, "bbox_color", new Scalar(255, 0, 0));
            var fontColor = ParseColor(props, "font_color", new Scalar(0, 255, 255));
            int thickness = GetInt(props, "bbox_line_width", 2);
            double fontScale = Math.Max(0.4, GetInt(props, "font_size", 16) / 30.0);
            bool showBbox = GetBool(props, "display_bbox", true);
            bool showText = GetBool(props, "display_text", true);
            bool showScore = GetBool(props, "display_score", true);

            float pixelFont = Math.Max(12f, (float)(fontScale * 26.0));
            var labels = new List<LabelItem>();
            foreach (var det in dets)
            {
                if (showBbox && det.Bbox != null && det.Bbox.Length >= 4)
                {
                    var rect = ToRect(det.Bbox, vis.Width, vis.Height);
                    Cv2.Rectangle(vis, rect, bboxColor, thickness);
                    if (showText)
                    {
                        var label = det.Category ?? "";
                        if (showScore && det.Score.HasValue)
                            label = label + " " + det.Score.Value.ToString("0.00", CultureInfo.InvariantCulture);
                        if (!string.IsNullOrEmpty(label))
                        {
                            int ty = rect.Y - (int)Math.Ceiling(pixelFont * 1.2) - 2;
                            if (ty < 0) ty = rect.Y + 2;
                            labels.Add(new LabelItem { Text = label, X = rect.X, Y = ty, Color = fontColor, FontSize = pixelFont });
                        }
                    }
                }
            }
            if (GetBool(props, "display_ok", true))
            {
                var okColor = ok ? new Scalar(0, 200, 0) : new Scalar(0, 0, 255);
                labels.Add(new LabelItem
                {
                    Text = ok ? "OK" : "NG",
                    X = 20,
                    Y = 12,
                    Color = okColor,
                    FontSize = Math.Max(20f, GetInt(props, "ok_font_size", 40))
                });
            }
            if (labels.Count > 0)
                DrawLabelsGdi(vis, labels);
            return vis;
        }

        private static Mat ToMat(object image)
        {
            var mat = image as Mat;
            if (mat != null) return mat;
            var path = image as string;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return Cv2.ImRead(path);
            return null;
        }

        private class LabelItem
        {
            public string Text;
            public int X;
            public int Y;
            public Scalar Color;
            public float FontSize;
        }

        private static void DrawLabelsGdi(Mat matBgr, List<LabelItem> labels)
        {
            if (matBgr == null || matBgr.Empty() || labels == null || labels.Count == 0 || matBgr.Channels() != 3) return;
            try
            {
                using (var bmp = MatToBitmap(matBgr))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        var format = StringFormat.GenericTypographic;
                        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                        foreach (var item in labels)
                        {
                            if (string.IsNullOrEmpty(item.Text)) continue;
                            using (var font = CreateChineseFont(item.FontSize))
                            {
                                var textSize = g.MeasureString(item.Text, font, int.MaxValue, format);
                                using (var bgBrush = new SolidBrush(DrawingColor.FromArgb(160, 0, 0, 0)))
                                    g.FillRectangle(bgBrush, item.X, item.Y, textSize.Width, textSize.Height);
                                using (var shadowBrush = new SolidBrush(DrawingColor.Black))
                                    g.DrawString(item.Text, font, shadowBrush, new PointF(item.X + 1, item.Y + 1), format);
                                var color = DrawingColor.FromArgb(
                                    ClampByte(item.Color.Val2),
                                    ClampByte(item.Color.Val1),
                                    ClampByte(item.Color.Val0));
                                using (var brush = new SolidBrush(color))
                                    g.DrawString(item.Text, font, brush, new PointF(item.X, item.Y), format);
                            }
                        }
                    }
                    BitmapToMat(bmp, matBgr);
                }
            }
            catch { }
        }

        private static DrawingFont CreateChineseFont(float sizePx)
        {
            var candidates = new[]
            {
                "Microsoft YaHei", "????", "SimSun", "??",
                "Noto Sans CJK SC", "Source Han Sans CN", "Arial Unicode MS"
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                try { return new DrawingFont(candidates[i], sizePx, DrawingFontStyle.Bold, DrawingGraphicsUnit.Pixel); }
                catch { }
            }
            return new DrawingFont(FontFamily.GenericSansSerif, sizePx, DrawingFontStyle.Bold, DrawingGraphicsUnit.Pixel);
        }

        private static Bitmap MatToBitmap(Mat mat)
        {
            int width = mat.Width;
            int height = mat.Height;
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                int srcStride = (int)mat.Step();
                int dstStride = data.Stride;
                int rowBytes = width * 3;
                for (int y = 0; y < height; y++)
                {
                    IntPtr srcRow = mat.Data + y * srcStride;
                    IntPtr dstRow = data.Scan0 + y * dstStride;
                    byte[] buffer = new byte[rowBytes];
                    Marshal.Copy(srcRow, buffer, 0, rowBytes);
                    Marshal.Copy(buffer, 0, dstRow, rowBytes);
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        private static void BitmapToMat(Bitmap bmp, Mat mat)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int srcStride = data.Stride;
                int dstStride = (int)mat.Step();
                int rowBytes = bmp.Width * 3;
                for (int y = 0; y < bmp.Height; y++)
                {
                    IntPtr srcRow = data.Scan0 + y * srcStride;
                    IntPtr dstRow = mat.Data + y * dstStride;
                    byte[] buffer = new byte[rowBytes];
                    Marshal.Copy(srcRow, buffer, 0, rowBytes);
                    Marshal.Copy(buffer, 0, dstRow, rowBytes);
                }
            }
            finally { bmp.UnlockBits(data); }
        }

        private static int ClampByte(double v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (int)Math.Round(v);
        }

        private class DetItem
        {
            public double[] Bbox;
            public string Category;
            public double? Score;
        }

        private static List<DetItem> ExtractDetections(object result)
        {
            var list = new List<DetItem>();
            if (result == null) return list;

            var type = result.GetType();
            if (type.Name == "FaceRunResult")
            {
                var detailProp = type.GetProperty("Detail");
                var detail = detailProp != null ? detailProp.GetValue(result, null) as string : null;
                if (!string.IsNullOrWhiteSpace(detail))
                    list.AddRange(ParseDetailJson(detail));
                return list;
            }

            var structured = ParseStructuredResult(result);
            if (structured != null)
                return structured;

            var dict = result as Dictionary<string, object>;
            if (dict != null)
            {
                if (dict.ContainsKey("result_list"))
                    list.AddRange(ParseResultList(dict["result_list"]));
                else if (dict.ContainsKey("by_image"))
                    list.AddRange(ParseByImageEntries(dict["by_image"]));
                else if (dict.ContainsKey("sample_results"))
                    list.AddRange(ParseSampleResultsTree(dict["sample_results"]));
                else if (dict.ContainsKey("detail"))
                    list.AddRange(ParseDetailJson(dict["detail"] != null ? dict["detail"].ToString() : null));
                return list;
            }

            var jo = result as JObject;
            if (jo != null)
            {
                if (jo["result_list"] != null) list.AddRange(ParseResultList(jo["result_list"]));
                else if (jo["by_image"] != null) list.AddRange(ParseByImageEntries(jo["by_image"]));
                else if (jo["sample_results"] != null) list.AddRange(ParseSampleResultsTree(jo["sample_results"]));
                else if (jo["detail"] != null) list.AddRange(ParseDetailJson(jo["detail"].ToString()));
            }
            return list;
        }

        private static List<DetItem> ParseStructuredResult(object result)
        {
            if (result == null) return null;
            var sampleResultsProperty = result.GetType().GetProperty("SampleResults");
            if (sampleResultsProperty == null) return null;
            var samples = sampleResultsProperty.GetValue(result, null) as IEnumerable;
            if (samples == null) return new List<DetItem>();
            var detections = new List<DetItem>();
            foreach (var sample in samples)
            {
                if (sample == null) continue;
                var resultsProperty = sample.GetType().GetProperty("Results");
                var results = resultsProperty != null ? resultsProperty.GetValue(sample, null) as IEnumerable : null;
                if (results == null) continue;
                foreach (var raw in results)
                {
                    if (raw == null) continue;
                    var rawType = raw.GetType();
                    var item = new DetItem();
                    var category = rawType.GetProperty("CategoryName");
                    var score = rawType.GetProperty("Score");
                    var bbox = rawType.GetProperty("Bbox");
                    item.Category = category != null ? Convert.ToString(category.GetValue(raw, null), CultureInfo.InvariantCulture) : "";
                    if (score != null)
                        item.Score = Convert.ToDouble(score.GetValue(raw, null), CultureInfo.InvariantCulture);
                    var bboxValues = bbox != null ? bbox.GetValue(raw, null) as IEnumerable : null;
                    if (bboxValues != null)
                    {
                        var coordinates = new List<double>();
                        foreach (var value in bboxValues)
                            coordinates.Add(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                        item.Bbox = coordinates.ToArray();
                    }
                    detections.Add(item);
                }
            }
            return detections;
        }

        private static bool? ExtractOk(object result)
        {
            if (result == null) return null;
            var type = result.GetType();
            if (type.Name == "FaceRunResult")
            {
                var detailProp = type.GetProperty("Detail");
                var detail = detailProp != null ? detailProp.GetValue(result, null) as string : null;
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    try
                    {
                        var token = JToken.Parse(detail);
                        if (token["ok"] != null) return token["ok"].ToObject<bool>();
                    }
                    catch { }
                }
                var p = type.GetProperty("Ok");
                if (p != null) return (bool)p.GetValue(result, null);
            }
            var dict = result as Dictionary<string, object>;
            if (dict != null && dict.ContainsKey("ok"))
            {
                try { return Convert.ToBoolean(dict["ok"]); } catch { }
            }
            var jo = result as JObject;
            if (jo != null && jo["ok"] != null)
            {
                try { return jo["ok"].ToObject<bool>(); } catch { }
            }
            return null;
        }

        private static string ExtractReason(object result)
        {
            if (result == null) return null;
            var type = result.GetType();
            if (type.Name == "FaceRunResult")
            {
                var err = type.GetProperty("Error");
                var e = err != null ? err.GetValue(result, null) as string : null;
                if (!string.IsNullOrEmpty(e)) return e;
            }
            return null;
        }

        private static List<DetItem> ParseDetailJson(string detail)
        {
            var list = new List<DetItem>();
            if (string.IsNullOrWhiteSpace(detail)) return list;
            try
            {
                var token = JToken.Parse(detail);
                if (token["result_list"] != null)
                    list.AddRange(ParseResultList(token["result_list"]));
                else if (token["by_image"] != null)
                    list.AddRange(ParseByImageEntries(token["by_image"]));
                else if (token["sample_results"] != null)
                    list.AddRange(ParseSampleResultsTree(token["sample_results"]));
                else if (token["results"] != null)
                    list.AddRange(ParseDetArray(token["results"]));
            }
            catch { }
            return list;
        }

        private static List<DetItem> ParseResultList(object raw)
        {
            var list = new List<DetItem>();
            JArray arr = raw as JArray;
            if (arr == null && raw != null)
            {
                try { arr = JArray.FromObject(raw); } catch { }
            }
            if (arr == null) return list;
            foreach (var entry in arr)
            {
                if (entry["by_image"] != null)
                    list.AddRange(ParseByImageEntries(entry["by_image"]));
                if (entry["sample_results"] != null)
                    list.AddRange(ParseDetArray(entry["sample_results"]));
                if (entry["results"] != null)
                    list.AddRange(ParseDetArray(entry["results"]));
            }
            return list;
        }

        private static List<DetItem> ParseByImageEntries(object raw)
        {
            var list = new List<DetItem>();
            JArray arr = raw as JArray;
            if (arr == null && raw != null)
            {
                try { arr = JArray.FromObject(raw); } catch { }
            }
            if (arr == null) return list;
            foreach (var entry in arr)
            {
                if (entry["results"] != null)
                    list.AddRange(ParseDetArray(entry["results"]));
                else if (entry["sample_results"] != null)
                    list.AddRange(ParseDetArray(entry["sample_results"]));
                else if (entry["bbox"] != null)
                    list.AddRange(ParseDetArray(new JArray(entry)));
            }
            return list;
        }

        private static List<DetItem> ParseSampleResultsTree(object raw)
        {
            var list = new List<DetItem>();
            JArray arr = raw as JArray;
            if (arr == null && raw != null)
            {
                try { arr = JArray.FromObject(raw); } catch { }
            }
            if (arr == null) return list;
            foreach (var entry in arr)
            {
                if (entry["results"] != null)
                    list.AddRange(ParseDetArray(entry["results"]));
                else if (entry["sample_results"] != null)
                    list.AddRange(ParseDetArray(entry["sample_results"]));
                else if (entry["bbox"] != null)
                    list.AddRange(ParseDetArray(new JArray(entry)));
            }
            return list;
        }

        private static List<DetItem> ParseDetArray(object raw)
        {
            var list = new List<DetItem>();
            JArray arr = raw as JArray;
            if (arr == null && raw != null)
            {
                try { arr = JArray.FromObject(raw); } catch { }
            }
            if (arr == null) return list;
            foreach (var det in arr)
            {
                var item = new DetItem();
                item.Category = det["category_name"] != null ? det["category_name"].ToString() : (det["category"] != null ? det["category"].ToString() : "");
                if (det["score"] != null)
                {
                    double s;
                    if (double.TryParse(det["score"].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out s))
                        item.Score = s;
                }
                var bbox = det["bbox"] as JArray;
                if (bbox != null && bbox.Count >= 4)
                {
                    item.Bbox = new double[bbox.Count];
                    for (int i = 0; i < bbox.Count; i++)
                        item.Bbox[i] = bbox[i].ToObject<double>();
                }
                list.Add(item);
            }
            return list;
        }

        private static Rect ToRect(double[] bbox, int imgW, int imgH)
        {
            // C# ?????? bbox ? xywh?????? [cx,cy,w,h,angle]??????????
            int x, y, bw, bh;
            if (bbox.Length >= 5)
            {
                double cx = bbox[0], cy = bbox[1];
                bw = Math.Max(1, (int)Math.Round(bbox[2]));
                bh = Math.Max(1, (int)Math.Round(bbox[3]));
                x = (int)Math.Round(cx - bw / 2.0);
                y = (int)Math.Round(cy - bh / 2.0);
            }
            else
            {
                x = (int)Math.Round(bbox[0]);
                y = (int)Math.Round(bbox[1]);
                bw = Math.Max(1, (int)Math.Round(bbox[2]));
                bh = Math.Max(1, (int)Math.Round(bbox[3]));
            }
            x = Math.Max(0, Math.Min(imgW - 1, x));
            y = Math.Max(0, Math.Min(imgH - 1, y));
            bw = Math.Max(1, Math.Min(imgW - x, bw));
            bh = Math.Max(1, Math.Min(imgH - y, bh));
            return new Rect(x, y, bw, bh);
        }

        private static Scalar ParseColor(JObject props, string key, Scalar def)
        {
            if (props == null || props[key] == null) return def;
            var parts = props[key].ToString().Replace(" ", "").Split(',');
            if (parts.Length < 3) return def;
            int b, g, r;
            if (!int.TryParse(parts[0], out b) || !int.TryParse(parts[1], out g) || !int.TryParse(parts[2], out r))
                return def;
            return new Scalar(b, g, r);
        }

        private static bool GetBool(JObject props, string key, bool def)
        {
            if (props == null || props[key] == null) return def;
            try { return props[key].ToObject<bool>(); } catch { return def; }
        }

        private static int GetInt(JObject props, string key, int def)
        {
            if (props == null || props[key] == null) return def;
            int v;
            return int.TryParse(props[key].ToString(), out v) ? v : def;
        }
    }
}
