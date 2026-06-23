using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace DlcvModules
{
    /// <summary>
    /// 元器件尺寸测量模块。
    /// 基于传统计算机图形学：Otsu 阈值 + 形态学闭运算 + minAreaRect 估算角度 + 旋转 ROI 内强度投影。
    /// 输出每个元器件的长边（height）、短边（width）、旋转角（angle）以及 rbox/bbox/polygon。
    /// </summary>
    public class ComponentSizeMeasure : BaseModule
    {
        static ComponentSizeMeasure()
        {
            ModuleRegistry.Register("post_process/component_size_measure", typeof(ComponentSizeMeasure));
        }

        public ComponentSizeMeasure(int nodeId, string title = null,
            Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();
            var outResults = new JArray(results);

            int minArea = ReadInt("min_area", 500);
            int closeKernelSize = ReadInt("close_kernel_size", 15);
            double projLowThr = ReadDouble("proj_low_thr", 0.1);
            double projHighThrCol = ReadDouble("proj_high_thr_col", 0.2);
            double projHighThrRow = ReadDouble("proj_high_thr_row", 0.3);
            string categoryName = ReadStringOrDefault("category_name", "元器件");

            double firstWidth = 0.0;
            double firstHeight = 0.0;
            double firstAngle = 0.0;
            bool found = false;

            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null || wrap.ImageObject == null || wrap.ImageObject.Empty())
                    continue;

                Mat gray;
                if (wrap.ImageObject.Channels() == 1)
                    gray = wrap.ImageObject.Clone();
                else
                {
                    gray = new Mat();
                    Cv2.CvtColor(wrap.ImageObject, gray, ColorConversionCodes.BGR2GRAY);
                }

                var det = MeasureSingle(
                    gray,
                    minArea,
                    closeKernelSize,
                    projLowThr,
                    projHighThrCol,
                    projHighThrRow,
                    categoryName
                );

                gray.Dispose();

                if (det == null)
                    continue;

                if (!found)
                {
                    firstWidth = det.Width;
                    firstHeight = det.Height;
                    firstAngle = det.Angle;
                    found = true;
                }

                double angleRad = det.Angle * Math.PI / 180.0;
                var sample = new JObject
                {
                    ["category_name"] = categoryName,
                    ["category_id"] = 0,
                    ["score"] = 1.0,
                    ["bbox"] = new JArray(det.Bbox[0], det.Bbox[1], det.Bbox[2], det.Bbox[3]),
                    ["rbox"] = new JArray(det.Center.X, det.Center.Y, det.Height, det.Width, angleRad),
                    ["width"] = det.Width,
                    ["height"] = det.Height,
                    ["angle"] = angleRad,
                    ["center"] = new JArray(det.Center.X, det.Center.Y),
                    ["polygon"] = new JArray(det.Polygon.Select(p => new JArray(p.X, p.Y))),
                    ["with_angle"] = true,
                };

                var entry = new JObject
                {
                    ["type"] = "local",
                    ["originating_module"] = "post_process/component_size_measure",
                    ["index"] = i,
                    ["origin_index"] = wrap.OriginalIndex,
                    ["sample_results"] = new JArray(sample),
                    ["transform"] = wrap.TransformState != null ? JObject.FromObject(wrap.TransformState.ToDict()) : null,
                };
                outResults.Add(entry);
            }

            ScalarOutputsByName = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (found)
            {
                ScalarOutputsByName["width"] = firstWidth;
                ScalarOutputsByName["height"] = firstHeight;
                ScalarOutputsByName["angle"] = firstAngle;
            }
            else
            {
                ScalarOutputsByName["width"] = 0.0;
                ScalarOutputsByName["height"] = 0.0;
                ScalarOutputsByName["angle"] = 0.0;
            }

            return new ModuleIO(images, outResults);
        }

        #region Helpers

        private int ReadInt(string key, int defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                try { return Convert.ToInt32(v); } catch { }
            }
            return defaultValue;
        }

        private double ReadDouble(string key, double defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
            {
                try { return Convert.ToDouble(v); } catch { }
            }
            return defaultValue;
        }

        private class Detection
        {
            public Point2f Center;
            public double Width;
            public double Height;
            public double Angle; // degrees
            public double[] Bbox; // [x1, y1, x2, y2]
            public Point2f[] Polygon;
        }

        private Detection MeasureSingle(
            Mat gray,
            int minArea,
            int closeKernelSize,
            double projLowThr,
            double projHighThrCol,
            double projHighThrRow,
            string categoryName)
        {
            using (Mat binary = Preprocess(gray, closeKernelSize))
            {
                Point[] contour = FindMainContour(binary, minArea);
                if (contour == null)
                    return null;

                RotatedRect rect = Cv2.MinAreaRect(contour);
                double angle = rect.Angle;
                double w = rect.Size.Width;
                double h = rect.Size.Height;
                if (w < h)
                    angle -= 90.0;
                angle = NormalizeAngle(angle);

                Size size = new Size(gray.Width, gray.Height);
                using (Mat M = Cv2.GetRotationMatrix2D(rect.Center, angle, 1.0))
                using (Mat rotGray = new Mat())
                using (Mat rotBin = new Mat())
                {
                    Cv2.WarpAffine(gray, rotGray, M, size, InterpolationFlags.Linear);
                    Cv2.WarpAffine(binary, rotBin, M, size, InterpolationFlags.Nearest);

                    Point[] rotContour = FindMainContour(rotBin, minArea);
                    if (rotContour == null)
                    {
                        double length = Math.Max(w, h);
                        double width = Math.Min(w, h);
                        Point2f[] box = Cv2.BoxPoints(rect);
                        return BuildDetection(rect.Center, length, width, NormalizeAngle(rect.Angle), box, categoryName);
                    }

                    Rect bbox = Cv2.BoundingRect(rotContour);
                    int margin = 10;
                    int x1 = Math.Max(0, bbox.X - margin);
                    int y1 = Math.Max(0, bbox.Y - margin);
                    int x2 = Math.Min(rotGray.Width, bbox.X + bbox.Width + margin);
                    int y2 = Math.Min(rotGray.Height, bbox.Y + bbox.Height + margin);

                    using (Mat cropGray = new Mat(rotGray, new Rect(x1, y1, x2 - x1, y2 - y1)))
                    using (Mat cropMask = new Mat(rotBin, new Rect(x1, y1, x2 - x1, y2 - y1)))
                    {
                        int cw = cropGray.Width;
                        int ch = cropGray.Height;
                        double[] colSum = new double[cw];
                        double[] rowSum = new double[ch];

                        for (int y = 0; y < ch; y++)
                        {
                            for (int x = 0; x < cw; x++)
                            {
                                if (cropMask.At<byte>(y, x) > 0)
                                {
                                    double v = cropGray.At<byte>(y, x);
                                    colSum[x] += v;
                                    rowSum[y] += v;
                                }
                            }
                        }

                        if (colSum.Length == 0 || rowSum.Length == 0 || colSum.Max() == 0 || rowSum.Max() == 0)
                            return null;

                        Tuple<int, int> colExt = RobustExtent(colSum, projLowThr, projHighThrCol);
                        Tuple<int, int> rowExt = RobustExtent(rowSum, projLowThr, projHighThrRow);

                        double length = colExt.Item2 - colExt.Item1 + 1;
                        double width = rowExt.Item2 - rowExt.Item1 + 1;

                        double cropCx = (colExt.Item1 + colExt.Item2) / 2.0 + x1;
                        double cropCy = (rowExt.Item1 + rowExt.Item2) / 2.0 + y1;

                        using (Mat MInv = Cv2.GetRotationMatrix2D(rect.Center, -angle, 1.0))
                        {
                            double m00 = MInv.At<double>(0, 0);
                            double m01 = MInv.At<double>(0, 1);
                            double m02 = MInv.At<double>(0, 2);
                            double m10 = MInv.At<double>(1, 0);
                            double m11 = MInv.At<double>(1, 1);
                            double m12 = MInv.At<double>(1, 2);
                            double origCx = m00 * cropCx + m01 * cropCy + m02;
                            double origCy = m10 * cropCx + m11 * cropCy + m12;

                            using (Mat cropRegion = cropMask.Clone())
                            {
                                for (int x = 0; x < cw; x++)
                                {
                                    if (x < colExt.Item1 || x > colExt.Item2)
                                        for (int y = 0; y < ch; y++)
                                            cropRegion.Set<byte>(y, x, 0);
                                }
                                for (int y = 0; y < ch; y++)
                                {
                                    if (y < rowExt.Item1 || y > rowExt.Item2)
                                        for (int x = 0; x < cw; x++)
                                            cropRegion.Set<byte>(y, x, 0);
                                }

                                Moments mom = Cv2.Moments(cropRegion);
                                double angleAdj = angle;
                                if (mom.Mu20 != mom.Mu02)
                                {
                                    double theta = 0.5 * Math.Atan2(2.0 * mom.Mu11, mom.Mu20 - mom.Mu02);
                                    angleAdj = NormalizeAngle(angle + theta * 180.0 / Math.PI);
                                }

                                double finalLen = Math.Max(length, width);
                                double finalWid = Math.Min(length, width);
                                Point2f center = new Point2f((float)origCx, (float)origCy);
                                Point2f[] box = BoxFromCenterSize(center, finalLen, finalWid, angleAdj);
                                return BuildDetection(center, finalLen, finalWid, angleAdj, box, categoryName);
                            }
                        }
                    }
                }
            }
        }

        private static Mat Preprocess(Mat gray, int closeKernelSize)
        {
            Mat blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
            Mat binary = new Mat();
            Cv2.Threshold(blurred, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(closeKernelSize, closeKernelSize));
            Mat closed = new Mat();
            Cv2.MorphologyEx(binary, closed, MorphTypes.Close, kernel);
            blurred.Dispose();
            binary.Dispose();
            kernel.Dispose();
            return closed;
        }

        private static Point[] FindMainContour(Mat binary, int minArea)
        {
            Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(binary, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            if (contours == null || contours.Length == 0)
                return null;
            var filtered = contours.Where(c => Cv2.ContourArea(c) > minArea).ToArray();
            if (filtered.Length == 0)
                return null;
            return filtered.OrderByDescending(c => Cv2.ContourArea(c)).First();
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle <= -90.0) angle += 180.0;
            while (angle > 90.0) angle -= 180.0;
            return angle;
        }

        private static Tuple<int, int> RobustExtent(double[] arr, double lowThr, double highThr)
        {
            if (arr.Length == 0)
                return new Tuple<int, int>(0, 0);
            double max = arr.Max();
            if (max == 0)
                return new Tuple<int, int>(0, arr.Length - 1);
            double tLow = max * lowThr;
            List<Tuple<int, int>> runs = new List<Tuple<int, int>>();
            int i = 0, n = arr.Length;
            while (i < n)
            {
                if (arr[i] > tLow)
                {
                    int j = i;
                    while (j < n && arr[j] > tLow) j++;
                    runs.Add(new Tuple<int, int>(i, j - 1));
                    i = j;
                }
                else
                {
                    i++;
                }
            }
            if (runs.Count == 0)
                return new Tuple<int, int>(0, arr.Length - 1);
            var longest = runs.OrderByDescending(r => r.Item2 - r.Item1).First();
            double tHigh = max * highThr;
            int start = longest.Item1;
            int end = longest.Item2;
            for (int k = longest.Item1; k <= longest.Item2; k++)
            {
                if (arr[k] > tHigh)
                {
                    start = k;
                    break;
                }
            }
            for (int k = longest.Item2; k >= longest.Item1; k--)
            {
                if (arr[k] > tHigh)
                {
                    end = k;
                    break;
                }
            }
            return new Tuple<int, int>(start, end);
        }

        private static Point2f[] BoxFromCenterSize(Point2f center, double length, double width, double angle)
        {
            double rad = angle * Math.PI / 180.0;
            double ca = Math.Cos(rad);
            double sa = Math.Sin(rad);
            double hw = length / 2.0;
            double hh = width / 2.0;
            Point2f[] corners = new Point2f[]
            {
                new Point2f((float)(-hw), (float)(-hh)),
                new Point2f((float)(hw), (float)(-hh)),
                new Point2f((float)(hw), (float)(hh)),
                new Point2f((float)(-hw), (float)(hh)),
            };
            for (int i = 0; i < 4; i++)
            {
                float x = corners[i].X;
                float y = corners[i].Y;
                corners[i] = new Point2f(
                    (float)(x * ca - y * sa + center.X),
                    (float)(x * sa + y * ca + center.Y));
            }
            return corners;
        }

        private static Detection BuildDetection(Point2f center, double length, double width, double angle, Point2f[] box, string categoryName)
        {
            double x1 = box.Min(p => p.X);
            double y1 = box.Min(p => p.Y);
            double x2 = box.Max(p => p.X);
            double y2 = box.Max(p => p.Y);
            return new Detection
            {
                Center = center,
                Width = width,
                Height = length,
                Angle = angle,
                Bbox = new[] { x1, y1, x2, y2 },
                Polygon = box,
            };
        }

        #endregion
    }
}
