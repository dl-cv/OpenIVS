using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvTest.WPFViewer
{
    /// <summary>
    /// 将推理结果转换为可绘制的叠加元素（WPF 侧使用）�?    /// 目标：替代“画�?Mat 再显示”的方式，提升交互缩放时文字可读性与性能�?    /// </summary>
    public static class WpfVisualize
    {
        public sealed class Options
        {
            public double ConfidenceThreshold { get; set; } = 0.0;
            public bool DisplayBbox { get; set; } = true;
            public bool DisplayMask { get; set; } = true;
            public bool DisplayContours { get; set; } = true;
            public bool DisplayText { get; set; } = true;
            public bool DisplayScore { get; set; } = true;
            public bool TextOutOfBbox { get; set; } = true;
            public bool DisplayTextShadow { get; set; } = true;
            public bool DisplayCenterPoint { get; set; } = false;

            public bool BBoxFillEnabled { get; set; } = false;
            /// <summary>0-100</summary>
            public double BBoxFillOpacity { get; set; } = 0.0;

            public Color BboxColorOk { get; set; } = Colors.LimeGreen;
            public Color BboxColorNg { get; set; } = Colors.Red;
            public Color FontColor { get; set; } = Colors.White;
            public Color MaskColor { get; set; } = Colors.LimeGreen;
            public byte MaskAlpha { get; set; } = 128;
        }

        public sealed class VisualItem
        {
            public string CategoryName { get; set; }
            public float Score { get; set; }
            public bool IsOk { get; set; }
            public bool IsRotated { get; set; }
            public System.Windows.Point[] Polygon { get; set; } // 4 points, image coordinate
            public System.Windows.Rect BboxRect { get; set; } // axis-aligned in image coordinate
            public BitmapSource MaskBitmap { get; set; } // local mask bitmap (size = bbox w/h), may be null
            public List<System.Windows.Point[]> MaskContours { get; set; } // optional, in image coordinate
            public Color MaskColor { get; set; }
            public byte MaskAlpha { get; set; }
            public List<System.Windows.Point[]> Contours { get; set; } // optional, in image coordinate

            public string LabelText { get; set; }
            public Color StrokeColor { get; set; }
            public Color FontColor { get; set; }
            public System.Windows.Point? CenterPoint { get; set; }

            public bool FillEnabled { get; set; }
            public byte FillAlpha { get; set; }
        }

        public sealed class VisualizeResult
        {
            public List<VisualItem> Items { get; set; } = new List<VisualItem>();
            // 不再使用 OK/NG 语义；StatusText 仅用于可选 HUD（例如分类标签/无结果提示）。
            public string StatusText { get; set; } = string.Empty;
            public bool StatusIsOk { get; set; } = true;
        }

        public static VisualizeResult Build(Utils.CSharpResult? result, Options opt)
        {
            var r = new VisualizeResult();
            if (opt == null) opt = new Options();

            if (result == null || result.Value.SampleResults == null || result.Value.SampleResults.Count == 0)
            {
                r.StatusText = "No Result";
                r.StatusIsOk = true;
#if DEBUG
                try { System.Diagnostics.Debug.WriteLine("[WpfVisualize] No Result: SampleResults empty/null"); } catch { }
#endif
                return r;
            }

            var sample = result.Value.SampleResults[0];
            if (sample.Results == null || sample.Results.Count == 0)
            {
                r.StatusText = "No Result";
                r.StatusIsOk = true;
#if DEBUG
                try { System.Diagnostics.Debug.WriteLine("[WpfVisualize] No Result: sample.Results empty/null"); } catch { }
#endif
                return r;
            }

            // 先筛选过阈值的结果
            var passed = new List<Utils.CSharpObjectResult>();
            foreach (var det in sample.Results)
            {
                if (det.Score >= opt.ConfidenceThreshold)
                {
                    passed.Add(det);
                }
            }

            if (passed.Count == 0)
            {
                r.StatusText = "No Result";
                r.StatusIsOk = true;
#if DEBUG
                try { System.Diagnostics.Debug.WriteLine("[WpfVisualize] No Result: all filtered by threshold"); } catch { }
#endif
                return r;
            }

            // 判断是否存在有效 bbox（用于区分检测/分割 vs 分类）
            bool hasAnyBbox = false;
            foreach (var det in passed)
            {
                if (det.WithBbox && det.Bbox != null && det.Bbox.Count >= 4)
                {
                    hasAnyBbox = true;
                    break;
                }
            }

            // 分类任务（无 bbox）：用叠加文字显示“类�?+ 分数”，不做 OK/NG 判定
            if (!hasAnyBbox)
            {
                // 取分数最高的一项
                Utils.CSharpObjectResult best = passed[0];
                for (int i = 1; i < passed.Count; i++)
                {
                    if (passed[i].Score > best.Score) best = passed[i];
                }

                string cat = best.CategoryName ?? string.Empty;
                string label = cat;
                if (opt.DisplayScore)
                {
                    label = string.IsNullOrEmpty(label)
                        ? $"{best.Score * 100:F1}"
                        : $"{label}: {best.Score * 100:F1}";
                }

                if (opt.DisplayText && !string.IsNullOrEmpty(label))
                {
                    r.Items.Add(new VisualItem
                    {
                        CategoryName = cat,
                        Score = best.Score,
                        IsOk = true,
                        IsRotated = false,
                        Polygon = null,
                        // 用一个最�?bboxRect 作为锚点，把文字固定在左上角附近
                        BboxRect = new System.Windows.Rect(0, 0, 1, 1),
                        MaskBitmap = null,
                        MaskContours = null,
                        MaskColor = opt.MaskColor,
                        MaskAlpha = opt.MaskAlpha,
                        Contours = null,
                        StrokeColor = opt.BboxColorNg,
                        FontColor = opt.FontColor,
                        LabelText = label,
                        FillEnabled = false,
                        FillAlpha = 0
                    });
                }

                r.StatusText = string.Empty;
                r.StatusIsOk = true;
#if DEBUG
                try
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[WpfVisualize] Classification: raw={sample.Results?.Count ?? 0}, passed={passed.Count}, threshold={opt.ConfidenceThreshold:F3}, label={label}");
                }
                catch { }
#endif
                return r;
            }

            // 检�?分割结果
            bool any = false;
            foreach (var det in passed)
            {
                if (!det.WithBbox || det.Bbox == null || det.Bbox.Count < 4) continue;

                any = true;

                string cat = det.CategoryName ?? string.Empty;
                Color stroke = opt.BboxColorNg;

                var item = new VisualItem
                {
                    CategoryName = cat,
                    Score = det.Score,
                    IsOk = true,
                    StrokeColor = stroke,
                    FontColor = opt.FontColor,
                    MaskBitmap = null,
                    MaskContours = null,
                    MaskColor = opt.MaskColor,
                    MaskAlpha = opt.MaskAlpha,
                    Contours = null,
                    FillEnabled = opt.BBoxFillEnabled,
                    FillAlpha = OpacityToAlpha(opt.BBoxFillOpacity),
                };

                // 判断是否为旋转框：det.WithAngle 或 bbox 包含 5 个元素（第5个是角度）
                bool isRotated = det.WithAngle || (det.Bbox != null && det.Bbox.Count >= 5);
                double angle = det.Angle;

                // DVP 部分场景：角度直接放在 bbox[4]
                if (angle == -100f && det.Bbox != null && det.Bbox.Count >= 5)
                {
                    angle = det.Bbox[4];
                }

                bool withAngle = isRotated && angle != -100f;
                item.IsRotated = withAngle;

                // bbox 约定：普通框 [x,y,w,h]，旋转框 [cx,cy,w,h]
                double b0 = det.Bbox[0];
                double b1 = det.Bbox[1];
                double b2 = det.Bbox[2];
                double b3 = det.Bbox[3];

                if (withAngle)
                {
                    double cx = b0, cy = b1, w = Math.Max(1.0, b2), h = Math.Max(1.0, b3);
                    double hw = w / 2.0, hh = h / 2.0;
                    double c = Math.Cos(angle), s = Math.Sin(angle);

                    var pts = new[]
                    {
                        RotatePoint(cx, cy, -hw, -hh, c, s),
                        RotatePoint(cx, cy, +hw, -hh, c, s),
                        RotatePoint(cx, cy, +hw, +hh, c, s),
                        RotatePoint(cx, cy, -hw, +hh, c, s),
                    };
                    item.Polygon = pts;

                    double minX = pts.Min(p => p.X);
                    double minY = pts.Min(p => p.Y);
                    double maxX = pts.Max(p => p.X);
                    double maxY = pts.Max(p => p.Y);
                    item.BboxRect = new System.Windows.Rect(minX, minY, Math.Max(1.0, maxX - minX), Math.Max(1.0, maxY - minY));
                }
                else
                {
                    double x = b0, y = b1, w = Math.Max(1.0, b2), h = Math.Max(1.0, b3);
                    item.BboxRect = new System.Windows.Rect(x, y, w, h);
                    item.Polygon = new[]
                    {
                        new System.Windows.Point(x, y),
                        new System.Windows.Point(x + w, y),
                        new System.Windows.Point(x + w, y + h),
                        new System.Windows.Point(x, y + h)
                    };
                }

                // 若用户关闭 bbox 显示，则不输出 Polygon（但保留 BboxRect 供 mask/text 定位使用）
                if (!opt.DisplayBbox)
                {
                    item.Polygon = null;
                }

                // 文本
                if (opt.DisplayText)
                {
                    string label = cat;
                    if (opt.DisplayScore)
                    {
                        label = string.IsNullOrEmpty(label)
                            ? $"{det.Score * 100:F1}"
                            : $"{label}: {det.Score * 100:F1}";
                    }
                    item.LabelText = label;
                }

                // Mask / Contours（支持旋转：�?mask 轮廓�?bbox + angle 变换到图像坐标）
                if (det.WithMask && det.Mask != null && !det.Mask.Empty() && (opt.DisplayMask || opt.DisplayContours))
                {
                    var localContours = ReadMaskContours(det.Mask, 0, 0);
                    if (localContours != null && localContours.Count > 0)
                    {
                        List<System.Windows.Point[]> worldContours;
                        if (withAngle && !double.IsNaN(angle) && !double.IsInfinity(angle))
                        {
                            double cx = b0;
                            double cy = b1;
                            double w = Math.Max(1.0, b2);
                            double h = Math.Max(1.0, b3);
                            worldContours = RotateContours(localContours, cx, cy, w, h, angle);
                        }
                        else
                        {
                            double x = b0;
                            double y = b1;
                            worldContours = OffsetContours(localContours, x, y);
                        }

                        if (opt.DisplayMask)
                        {
                            item.MaskContours = worldContours;
                            item.MaskColor = opt.MaskColor;
                            item.MaskAlpha = opt.MaskAlpha;
                        }
                        if (opt.DisplayContours)
                        {
                            item.Contours = worldContours;
                        }
                    }
                }

                // 中心点
                if (opt.DisplayCenterPoint)
                {
                    double cx = item.BboxRect.X + item.BboxRect.Width / 2;
                    double cy = item.BboxRect.Y + item.BboxRect.Height / 2;
                    item.CenterPoint = new System.Windows.Point(cx, cy);
                }

                r.Items.Add(item);
            }

            if (!any)
            {
                r.StatusText = "No Result";
                r.StatusIsOk = true;
            }

#if DEBUG
            try
            {
                int raw = sample.Results != null ? sample.Results.Count : 0;
                int items = r.Items != null ? r.Items.Count : 0;
                System.Diagnostics.Debug.WriteLine(
                    $"[WpfVisualize] BuildDone: raw={raw}, items={items}, threshold={opt.ConfidenceThreshold:F3}, status={r.StatusText}");
            }
            catch { }
#endif
            return r;
        }

        /// <summary>
        /// �?labelme 标注文件中的 shapes 转为可叠加绘制元素（主要用于左侧“标注视图”）�?        /// </summary>
        public static VisualizeResult BuildFromLabelmeShapes(JArray shapes, Options opt, Color strokeColor)
        {
            var r = new VisualizeResult
            {
                StatusText = string.Empty,
                StatusIsOk = true
            };

            if (opt == null) opt = new Options();
            if (shapes == null || shapes.Count == 0) return r;

            foreach (var token in shapes)
            {
                var shape = token as JObject;
                if (shape == null) continue;

                string label = shape["label"]?.ToString() ?? string.Empty;
                string shapeType = shape["shape_type"]?.ToString() ?? string.Empty;
                var points = shape["points"] as JArray;
                if (points == null) continue;

                System.Windows.Point[] poly = null;

                // labelme: rectangle 通常只有两个点（左上/右下）；polygon 有多个点
                if (!string.IsNullOrEmpty(shapeType) && string.Equals(shapeType, "rectangle", StringComparison.OrdinalIgnoreCase))
                {
                    if (points.Count < 2) continue;
                    if (!(points[0] is JArray p0) || p0.Count < 2) continue;
                    if (!(points[1] is JArray p1) || p1.Count < 2) continue;

                    double x1 = p0[0].Value<double>();
                    double y1 = p0[1].Value<double>();
                    double x2 = p1[0].Value<double>();
                    double y2 = p1[1].Value<double>();

                    double minX = Math.Min(x1, x2);
                    double minY = Math.Min(y1, y2);
                    double maxX = Math.Max(x1, x2);
                    double maxY = Math.Max(y1, y2);

                    poly = new[]
                    {
                        new System.Windows.Point(minX, minY),
                        new System.Windows.Point(maxX, minY),
                        new System.Windows.Point(maxX, maxY),
                        new System.Windows.Point(minX, maxY),
                    };
                }
                else
                {
                    // 默认�?polygon 处理（兼�?shape_type 为空的情况）
                    if (points.Count < 3) continue;

                    var tmp = new System.Windows.Point[points.Count];
                    int n = 0;
                    for (int i = 0; i < points.Count; i++)
                    {
                        if (!(points[i] is JArray pt) || pt.Count < 2) continue;
                        double x = pt[0].Value<double>();
                        double y = pt[1].Value<double>();
                        tmp[n++] = new System.Windows.Point(x, y);
                    }
                    if (n < 3) continue;
                    if (n != tmp.Length) tmp = tmp.Take(n).ToArray();
                    poly = tmp;
                }

                if (poly == null || poly.Length < 3) continue;

                double minPx = poly.Min(p => p.X);
                double minPy = poly.Min(p => p.Y);
                double maxPx = poly.Max(p => p.X);
                double maxPy = poly.Max(p => p.Y);

                var rect = new System.Windows.Rect(minPx, minPy, Math.Max(1.0, maxPx - minPx), Math.Max(1.0, maxPy - minPy));

                var item = new VisualItem
                {
                    CategoryName = label,
                    Score = 1.0f,
                    IsOk = true,
                    IsRotated = false,
                    StrokeColor = strokeColor,
                    FontColor = opt.FontColor,

                    // 标注层只负责轮廓/文字；mask/contours 为空
                    // 与旧逻辑保持一致：标注轮廓默认始终可见（不受“显示BBox”开关影响）
                    Polygon = poly,
                    BboxRect = rect,
                    MaskBitmap = null,
                    MaskContours = null,
                    MaskColor = opt.MaskColor,
                    MaskAlpha = opt.MaskAlpha,
                    Contours = null,

                    LabelText = opt.DisplayText ? label : null,

                    // 避免�?bbox fill 用在 polygon 上导致“填充外接矩形”的误解
                    FillEnabled = false,
                    FillAlpha = 0
                };

                r.Items.Add(item);
            }

            return r;
        }

        private static byte OpacityToAlpha(double opacityPercent)
        {
            try
            {
                double op = Math.Max(0.0, Math.Min(100.0, opacityPercent));
                return (byte)Math.Round(op / 100.0 * 255.0, MidpointRounding.AwayFromZero);
            }
            catch
            {
                return 0;
            }
        }

        private static System.Windows.Point RotatePoint(double cx, double cy, double dx, double dy, double c, double s)
        {
            return new System.Windows.Point(cx + dx * c - dy * s, cy + dx * s + dy * c);
        }

        private static BitmapSource CreateMaskBitmap(Mat mask, Color color, byte alpha)
        {
            try
            {
                if (mask == null || mask.Empty()) return null;
                if (mask.Type() != MatType.CV_8UC1)
                {
                    using (var tmp = new Mat())
                    {
                        mask.ConvertTo(tmp, MatType.CV_8UC1);
                        return CreateMaskBitmap(tmp, color, alpha);
                    }
                }

                using (var bgra = new Mat(mask.Rows, mask.Cols, MatType.CV_8UC4, Scalar.All(0)))
                {
                    var scalar = new Scalar(color.B, color.G, color.R, alpha);
                    bgra.SetTo(scalar, mask); // mask !=0 区域填充
                    return MatBitmapSource.ToBitmapSource(bgra, freeze: true);
                }
            }
            catch
            {
                return null;
            }
        }

        private static List<System.Windows.Point[]> ReadMaskContours(Mat mask, double bx, double by)
        {
            var result = new List<System.Windows.Point[]>();
            try
            {
                if (mask == null || mask.Empty()) return result;
                if (mask.Type() != MatType.CV_8UC1)
                {
                    using (var tmp = new Mat())
                    {
                        mask.ConvertTo(tmp, MatType.CV_8UC1);
                        return ReadMaskContours(tmp, bx, by);
                    }
                }

                OpenCvSharp.Point[][] contours;
                HierarchyIndex[] hierarchy;
                Cv2.FindContours(mask, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                if (contours == null) return result;

                foreach (var c in contours)
                {
                    if (c == null || c.Length < 3) continue;
                    var pts = new System.Windows.Point[c.Length];
                    for (int i = 0; i < c.Length; i++)
                    {
                        pts[i] = new System.Windows.Point(c[i].X + bx, c[i].Y + by);
                    }
                    result.Add(pts);
                }
            }
            catch
            {
                // ignore
            }
            return result;
        }

        private static List<System.Windows.Point[]> OffsetContours(List<System.Windows.Point[]> contours, double ox, double oy)
        {
            var result = new List<System.Windows.Point[]>();
            if (contours == null) return result;
            foreach (var c in contours)
            {
                if (c == null || c.Length == 0) continue;
                var pts = new System.Windows.Point[c.Length];
                for (int i = 0; i < c.Length; i++)
                {
                    pts[i] = new System.Windows.Point(c[i].X + ox, c[i].Y + oy);
                }
                result.Add(pts);
            }
            return result;
        }

        private static List<System.Windows.Point[]> RotateContours(List<System.Windows.Point[]> contours, double cx, double cy, double w, double h, double angle)
        {
            var result = new List<System.Windows.Point[]>();
            if (contours == null) return result;
            double hw = w / 2.0;
            double hh = h / 2.0;
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            foreach (var contour in contours)
            {
                if (contour == null || contour.Length == 0) continue;
                var pts = new System.Windows.Point[contour.Length];
                for (int i = 0; i < contour.Length; i++)
                {
                    double dx = contour[i].X - hw;
                    double dy = contour[i].Y - hh;
                    double gx = cx + dx * c - dy * s;
                    double gy = cy + dx * s + dy * c;
                    pts[i] = new System.Windows.Point(gx, gy);
                }
                result.Add(pts);
            }
            return result;
        }
    }
}


