using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using OpenCvSharp;
using CSharpObjectResult = dlcv_infer_csharp.Utils.CSharpObjectResult;

namespace DlcvDemo2
{
    internal static class DetectionGeometryUtils
    {
        private static readonly Regex CategoryAngleRegex = new Regex(@"^(.*?)(0|90|180|270)$", RegexOptions.Compiled);

        public static void ParseCategoryAndAngle(string categoryName, out string baseName, out int angle)
        {
            string normalizedCategoryName = (categoryName ?? string.Empty).Trim();
            angle = 0;
            if (normalizedCategoryName.Length == 0)
            {
                baseName = string.Empty;
                return;
            }

            Match match = CategoryAngleRegex.Match(normalizedCategoryName);
            if (!match.Success)
            {
                baseName = normalizedCategoryName;
                return;
            }

            int parsedAngle;
            if (!int.TryParse(match.Groups[2].Value, out parsedAngle))
            {
                parsedAngle = 0;
            }

            baseName = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = normalizedCategoryName;
            }

            angle = NormalizeRightAngle(parsedAngle);
        }

        public static int NormalizeRightAngle(int angle)
        {
            int normalized = angle % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            if (normalized != 0 && normalized != 90 && normalized != 180 && normalized != 270)
            {
                return 0;
            }

            return normalized;
        }

        public static Rect2d ExpandRect(Rect2d rect, int padding, int imageWidth, int imageHeight)
        {
            if (imageWidth <= 0 || imageHeight <= 0 || rect.Width <= 0 || rect.Height <= 0)
            {
                return new Rect2d();
            }

            double safePadding = Math.Max(0, padding);
            double left = Math.Max(0, rect.X - safePadding);
            double top = Math.Max(0, rect.Y - safePadding);
            double right = Math.Min(imageWidth, rect.X + rect.Width + safePadding);
            double bottom = Math.Min(imageHeight, rect.Y + rect.Height + safePadding);
            if (right <= left || bottom <= top)
            {
                return new Rect2d();
            }

            return new Rect2d(left, top, right - left, bottom - top);
        }

        public static CSharpObjectResult LiftExtractObjectToFull(CSharpObjectResult localObject, Rect windowRect)
        {
            if (localObject.Bbox == null || localObject.Bbox.Count < 4)
            {
                return localObject;
            }

            var bbox = new List<double>(localObject.Bbox);
            bbox[0] += windowRect.X;
            bbox[1] += windowRect.Y;

            bool withAngle = localObject.WithAngle || bbox.Count == 5;
            float angle = localObject.Angle;
            if (!withAngle)
            {
                angle = -100f;
            }
            else if (Math.Abs(angle + 100f) < 1e-4f && bbox.Count >= 5)
            {
                angle = (float)bbox[4];
            }

            return new CSharpObjectResult(
                localObject.CategoryId,
                localObject.CategoryName,
                localObject.Score,
                localObject.Area,
                bbox,
                false,
                new Mat(),
                true,
                withAngle,
                angle);
        }

        public static Rect ClampRectToImage(Rect2d rect, int imageWidth, int imageHeight)
        {
            int left = (int)Math.Floor(rect.X);
            int top = (int)Math.Floor(rect.Y);
            int right = (int)Math.Ceiling(rect.X + rect.Width);
            int bottom = (int)Math.Ceiling(rect.Y + rect.Height);

            left = Math.Max(0, Math.Min(imageWidth - 1, left));
            top = Math.Max(0, Math.Min(imageHeight - 1, top));
            right = Math.Max(left + 1, Math.Min(imageWidth, right));
            bottom = Math.Max(top + 1, Math.Min(imageHeight, bottom));

            return new Rect(left, top, right - left, bottom - top);
        }

        public static bool TryBuildRotatedCrop(Mat fullImage, CSharpObjectResult obj, int padding, out Mat roi, out double[] fullToCropAffine)
        {
            roi = null;
            fullToCropAffine = null;

            if (obj.Bbox == null || obj.Bbox.Count < 4)
            {
                return false;
            }

            bool hasAngle = obj.WithAngle || obj.Bbox.Count == 5;
            if (!hasAngle)
            {
                return false;
            }

            double cx = obj.Bbox[0];
            double cy = obj.Bbox[1];
            double w = Math.Abs(obj.Bbox[2]) + Math.Max(0, padding) * 2.0;
            double h = Math.Abs(obj.Bbox[3]) + Math.Max(0, padding) * 2.0;
            if (w <= 1 || h <= 1)
            {
                return false;
            }

            double angleRad = obj.Angle;
            if (Math.Abs(angleRad + 100.0) < 1e-6 && obj.Bbox.Count >= 5)
            {
                angleRad = obj.Bbox[4];
            }
            if (Math.Abs(angleRad + 100.0) < 1e-6)
            {
                angleRad = 0.0;
            }

            double angleDeg = angleRad * 180.0 / Math.PI;
            Mat rotMat = null;
            try
            {
                rotMat = Cv2.GetRotationMatrix2D(new Point2f((float)cx, (float)cy), angleDeg, 1.0);
                rotMat.Set(0, 2, rotMat.Get<double>(0, 2) + w / 2.0 - cx);
                rotMat.Set(1, 2, rotMat.Get<double>(1, 2) + h / 2.0 - cy);

                int outW = Math.Max(1, (int)Math.Round(w));
                int outH = Math.Max(1, (int)Math.Round(h));

                roi = new Mat();
                Cv2.WarpAffine(fullImage, roi, rotMat, new Size(outW, outH));
                fullToCropAffine = MatrixFromAffineMat(rotMat);
                if (roi.Empty())
                {
                    roi.Dispose();
                    roi = null;
                    fullToCropAffine = null;
                    return false;
                }

                return true;
            }
            catch
            {
                if (roi != null)
                {
                    roi.Dispose();
                    roi = null;
                }

                fullToCropAffine = null;
                throw;
            }
            finally
            {
                if (rotMat != null)
                {
                    rotMat.Dispose();
                }
            }
        }

        public static Mat RotateRoiByRightAngle(Mat roi, int angle)
        {
            if (angle == 0)
            {
                return roi.Clone();
            }

            if (angle == 90)
            {
                var rotated = new Mat();
                Cv2.Rotate(roi, rotated, RotateFlags.Rotate90Counterclockwise);
                return rotated;
            }

            if (angle == 180)
            {
                var rotated = new Mat();
                Cv2.Rotate(roi, rotated, RotateFlags.Rotate180);
                return rotated;
            }

            if (angle == 270)
            {
                var rotated = new Mat();
                Cv2.Rotate(roi, rotated, RotateFlags.Rotate90Clockwise);
                return rotated;
            }

            return roi.Clone();
        }

        public static double[] BuildRightAngleAffine(int srcW, int srcH, int angle, out int dstW, out int dstH)
        {
            angle = NormalizeRightAngle(angle);
            if (angle == 90)
            {
                dstW = srcH;
                dstH = srcW;
                return new[] { 0.0, 1.0, 0.0, -1.0, 0.0, (double)srcW };
            }

            if (angle == 180)
            {
                dstW = srcW;
                dstH = srcH;
                return new[] { -1.0, 0.0, (double)srcW, 0.0, -1.0, (double)srcH };
            }

            if (angle == 270)
            {
                dstW = srcH;
                dstH = srcW;
                return new[] { 0.0, -1.0, (double)srcH, 1.0, 0.0, 0.0 };
            }

            dstW = srcW;
            dstH = srcH;
            return new[] { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0 };
        }

        public static double[] MatrixFromAffineMat(Mat affine)
        {
            return new[]
            {
                affine.Get<double>(0, 0),
                affine.Get<double>(0, 1),
                affine.Get<double>(0, 2),
                affine.Get<double>(1, 0),
                affine.Get<double>(1, 1),
                affine.Get<double>(1, 2)
            };
        }

        public static double[] InvertAffine2x3(double[] a)
        {
            if (a == null || a.Length != 6)
            {
                return new[] { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0 };
            }

            double det = a[0] * a[4] - a[1] * a[3];
            if (Math.Abs(det) < 1e-12)
            {
                return new[] { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0 };
            }

            double inv00 = a[4] / det;
            double inv01 = -a[1] / det;
            double inv10 = -a[3] / det;
            double inv11 = a[0] / det;
            double inv02 = -(inv00 * a[2] + inv01 * a[5]);
            double inv12 = -(inv10 * a[2] + inv11 * a[5]);

            return new[] { inv00, inv01, inv02, inv10, inv11, inv12 };
        }

        public static double[] ComposeAffine(double[] first, double[] second)
        {
            double[] m1 = To3x3(first);
            double[] m2 = To3x3(second);
            double[] m = new double[9];

            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    m[r * 3 + c] =
                        m1[r * 3 + 0] * m2[0 * 3 + c] +
                        m1[r * 3 + 1] * m2[1 * 3 + c] +
                        m1[r * 3 + 2] * m2[2 * 3 + c];
                }
            }

            return new[] { m[0], m[1], m[2], m[3], m[4], m[5] };
        }

        public static Point2d ApplyAffine(double[] a, Point2d p)
        {
            return new Point2d(
                a[0] * p.X + a[1] * p.Y + a[2],
                a[3] * p.X + a[4] * p.Y + a[5]);
        }

        public static bool TryMapObjectToFull(CSharpObjectResult obj, double[] normToFullAffine, out CSharpObjectResult mapped)
        {
            mapped = default(CSharpObjectResult);

            if (obj.Bbox == null || obj.Bbox.Count < 4)
            {
                return false;
            }

            var points = new List<Point2d>(4);
            if (obj.WithAngle || obj.Bbox.Count == 5)
            {
                double cx = obj.Bbox[0];
                double cy = obj.Bbox[1];
                double w = Math.Abs(obj.Bbox[2]);
                double h = Math.Abs(obj.Bbox[3]);
                if (w <= 0 || h <= 0)
                {
                    return false;
                }

                double angle = obj.Angle;
                if (Math.Abs(angle + 100.0) < 1e-6 && obj.Bbox.Count >= 5)
                {
                    angle = obj.Bbox[4];
                }
                if (Math.Abs(angle + 100.0) < 1e-6)
                {
                    angle = 0.0;
                }

                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                var offsets = new[]
                {
                    new Point2d(-w / 2.0, -h / 2.0),
                    new Point2d(w / 2.0, -h / 2.0),
                    new Point2d(w / 2.0, h / 2.0),
                    new Point2d(-w / 2.0, h / 2.0)
                };

                foreach (var offset in offsets)
                {
                    points.Add(new Point2d(
                        cx + offset.X * cos - offset.Y * sin,
                        cy + offset.X * sin + offset.Y * cos));
                }
            }
            else
            {
                double x = obj.Bbox[0];
                double y = obj.Bbox[1];
                double w = Math.Abs(obj.Bbox[2]);
                double h = Math.Abs(obj.Bbox[3]);
                if (w <= 0 || h <= 0)
                {
                    return false;
                }

                points.Add(new Point2d(x, y));
                points.Add(new Point2d(x + w, y));
                points.Add(new Point2d(x + w, y + h));
                points.Add(new Point2d(x, y + h));
            }

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (var p in points)
            {
                Point2d mappedPoint = ApplyAffine(normToFullAffine, p);
                minX = Math.Min(minX, mappedPoint.X);
                minY = Math.Min(minY, mappedPoint.Y);
                maxX = Math.Max(maxX, mappedPoint.X);
                maxY = Math.Max(maxY, mappedPoint.Y);
            }

            double outW = maxX - minX;
            double outH = maxY - minY;
            if (outW <= 1e-6 || outH <= 1e-6)
            {
                return false;
            }

            var bbox = new List<double> { minX, minY, outW, outH };
            mapped = new CSharpObjectResult(
                obj.CategoryId,
                obj.CategoryName,
                obj.Score,
                (float)(outW * outH),
                bbox,
                false,
                new Mat(),
                true,
                false,
                -100f);
            return true;
        }

        private static double[] To3x3(double[] a)
        {
            if (a == null || a.Length != 6)
            {
                return new[] { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 };
            }

            return new[] { a[0], a[1], a[2], a[3], a[4], a[5], 0.0, 0.0, 1.0 };
        }
    }
}
