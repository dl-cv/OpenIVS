using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    public class CurveTextAffine : BaseModule
    {
        private sealed class BfsResult
        {
            public int Farthest;
            public int[] Previous;
        }

        static CurveTextAffine()
        {
            ModuleRegistry.Register("pre_process/curve_text_affine", typeof(CurveTextAffine));
        }

        public CurveTextAffine(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();
            if (!ReadBool("enable", true)) return new ModuleIO(images, results);

            int outputHeight = Math.Max(2, ReadInt("out_height", 80));
            double sampleStep = Math.Max(1.0, ReadDouble("sample_step", 10.0));
            double shrinkInside = Math.Max(0.0, ReadDouble("shrink_inside", 1.5));
            int maxWidth = Math.Max(0, ReadInt("max_unwrap_width", 0));
            string borderName = ReadStringOrDefault("border_mode", "reflect101");
            BorderTypes borderMode = string.Equals(borderName, "constant", StringComparison.OrdinalIgnoreCase)
                || string.Equals(borderName, "const", StringComparison.OrdinalIgnoreCase)
                ? BorderTypes.Constant
                : BorderTypes.Reflect101;

            var outputImages = new List<ModuleImage>();
            var outputResults = new JArray();
            for (int imageIndex = 0; imageIndex < images.Count; imageIndex++)
            {
                ModuleImage sourceImage = images[imageIndex];
                if (sourceImage == null || sourceImage.ImageObject == null || sourceImage.ImageObject.Empty()) continue;
                JObject sourceEntry = FindEntryForImage(results, imageIndex, sourceImage.OriginalIndex);
                var detections = sourceEntry != null ? sourceEntry["sample_results"] as JArray : null;
                if (detections == null) continue;

                foreach (JToken token in detections)
                {
                    var detection = token as JObject;
                    if (detection == null) continue;
                    List<Point2f> polygon = ExtractPolygon(detection, sourceImage.ImageObject.Width, sourceImage.ImageObject.Height);
                    if (polygon.Count < 3 || PolygonArea(polygon) <= 0.0) continue;
                    List<Point2f> centerCurve = ComputeCenterCurve(polygon, sourceImage.ImageObject.Width, sourceImage.ImageObject.Height);
                    if (centerCurve.Count < 2) continue;

                    List<Point2f> centers;
                    List<Point2f> lefts;
                    List<Point2f> rights;
                    if (!BuildSides(centerCurve, polygon, sampleStep, shrinkInside, out centers, out lefts, out rights)) continue;
                    Mat affine = UnwrapByRemap(sourceImage.ImageObject, centers, lefts, rights, outputHeight, maxWidth, borderMode);
                    if (affine == null || affine.Empty())
                    {
                        affine?.Dispose();
                        continue;
                    }

                    var outputImage = new ModuleImage(
                        sourceImage.ImageObject,
                        sourceImage.OriginalImage,
                        sourceImage.TransformState != null ? sourceImage.TransformState.Clone() : null,
                        sourceImage.OriginalIndex)
                    {
                        UniqueId = sourceImage.UniqueId,
                        SlidingMeta = sourceImage.SlidingMeta != null ? (JObject)sourceImage.SlidingMeta.DeepClone() : null,
                        AffineImage = affine
                    };
                    outputImages.Add(outputImage);

                    var detectionOut = (JObject)detection.DeepClone();
                    detectionOut["polygon"] = PolygonToJson(polygon);
                    var entryOut = (JObject)sourceEntry.DeepClone();
                    entryOut["type"] = "local";
                    entryOut["index"] = outputImages.Count - 1;
                    entryOut["origin_index"] = sourceImage.OriginalIndex;
                    entryOut["transform"] = sourceImage.TransformState != null ? JObject.FromObject(sourceImage.TransformState.ToDict()) : null;
                    entryOut["sample_results"] = new JArray(detectionOut);
                    entryOut["ok"] = true;
                    entryOut["reason"] = null;
                    outputResults.Add(entryOut);
                }
            }
            return new ModuleIO(outputImages, outputResults);
        }

        private bool ReadBool(string key, bool defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object value) && value != null)
            {
                try { return Convert.ToBoolean(value); } catch { }
            }
            return defaultValue;
        }

        private int ReadInt(string key, int defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object value) && value != null)
            {
                try { return Convert.ToInt32(value); } catch { }
            }
            return defaultValue;
        }

        private double ReadDouble(string key, double defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object value) && value != null)
            {
                try { return Convert.ToDouble(value); } catch { }
            }
            return defaultValue;
        }

        private static JObject FindEntryForImage(JArray results, int imageIndex, int originalIndex)
        {
            foreach (JToken token in results)
            {
                var entry = token as JObject;
                if (entry == null || !string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase)) continue;
                if ((entry.Value<int?>("index") ?? -1) == imageIndex) return entry;
            }
            if (imageIndex >= 0 && imageIndex < results.Count && results[imageIndex] is JObject) return (JObject)results[imageIndex];
            foreach (JToken token in results)
            {
                var entry = token as JObject;
                if (entry == null || !string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase)) continue;
                if ((entry.Value<int?>("origin_index") ?? -1) == originalIndex) return entry;
            }
            return null;
        }

        private static List<Point2f> ExtractPolygon(JObject detection, int imageWidth, int imageHeight)
        {
            List<Point2f> polygon = ParsePolygon(detection["polygon"]);
            if (polygon.Count >= 3) return polygon;
            return PolygonFromMask(detection, imageWidth, imageHeight);
        }

        private static List<Point2f> ParsePolygon(JToken token)
        {
            if (token == null) return new List<Point2f>();
            try
            {
                if (token.Type == JTokenType.String) token = JToken.Parse(token.Value<string>());
            }
            catch
            {
                return new List<Point2f>();
            }
            var array = token as JArray;
            if (array == null) return new List<Point2f>();
            var points = new List<Point2f>();
            if (array.Count > 0 && array[0] is JArray)
            {
                foreach (JToken item in array)
                {
                    var point = item as JArray;
                    if (point == null || point.Count < 2) continue;
                    points.Add(new Point2f(point[0].Value<float>(), point[1].Value<float>()));
                }
            }
            else
            {
                for (int i = 0; i + 1 < array.Count; i += 2)
                    points.Add(new Point2f(array[i].Value<float>(), array[i + 1].Value<float>()));
            }
            return points.Count >= 3 ? points : new List<Point2f>();
        }

        private static List<Point2f> PolygonFromMask(JObject detection, int imageWidth, int imageHeight)
        {
            if (detection["mask_rle"] == null) return new List<Point2f>();
            using (Mat mask = MaskRleUtils.MaskInfoToMat(detection["mask_rle"]))
            {
                if (mask.Empty()) return new List<Point2f>();
                Point[][] contours;
                HierarchyIndex[] hierarchy;
                Cv2.FindContours(mask, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                if (contours == null || contours.Length == 0) return new List<Point2f>();
                Point[] best = null;
                double bestArea = 0.0;
                foreach (Point[] contour in contours)
                {
                    double area = Math.Abs(Cv2.ContourArea(contour));
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = contour;
                    }
                }
                if (best == null || best.Length < 3) return new List<Point2f>();

                double bx = 0.0;
                double by = 0.0;
                double bw = mask.Width;
                double bh = mask.Height;
                var bbox = detection["bbox"] as JArray;
                if (bbox != null && bbox.Count >= 4)
                {
                    bx = bbox[0].Value<double>();
                    by = bbox[1].Value<double>();
                    bw = Math.Max(1.0, bbox[2].Value<double>());
                    bh = Math.Max(1.0, bbox[3].Value<double>());
                }
                bool fullImageMask = mask.Width == imageWidth && mask.Height == imageHeight;
                double sx = fullImageMask ? 1.0 : bw / Math.Max(1, mask.Width);
                double sy = fullImageMask ? 1.0 : bh / Math.Max(1, mask.Height);
                double ox = fullImageMask ? 0.0 : bx;
                double oy = fullImageMask ? 0.0 : by;

                var points = new List<Point2f>(best.Length);
                foreach (Point point in best)
                    points.Add(new Point2f((float)(ox + point.X * sx), (float)(oy + point.Y * sy)));
                return points;
            }
        }

        private static double PolygonArea(List<Point2f> polygon)
        {
            double sum = 0.0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2f a = polygon[i];
                Point2f b = polygon[(i + 1) % polygon.Count];
                sum += a.X * b.Y - a.Y * b.X;
            }
            return Math.Abs(sum * 0.5);
        }

        private static List<Point2f> ComputeCenterCurve(List<Point2f> polygon, int imageWidth, int imageHeight)
        {
            int minX = imageWidth - 1;
            int minY = imageHeight - 1;
            int maxX = 0;
            int maxY = 0;
            var clipped = new List<Point>(polygon.Count);
            foreach (Point2f point in polygon)
            {
                int x = Math.Max(0, Math.Min(imageWidth - 1, (int)Math.Round(point.X)));
                int y = Math.Max(0, Math.Min(imageHeight - 1, (int)Math.Round(point.Y)));
                clipped.Add(new Point(x, y));
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
            minX = Math.Max(0, minX - 2);
            minY = Math.Max(0, minY - 2);
            maxX = Math.Min(imageWidth - 1, maxX + 2);
            maxY = Math.Min(imageHeight - 1, maxY + 2);
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            if (width < 3 || height < 3) return new List<Point2f>();

            var local = new Point[clipped.Count];
            for (int i = 0; i < clipped.Count; i++) local[i] = new Point(clipped[i].X - minX, clipped[i].Y - minY);
            using (var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.Black))
            {
                Cv2.FillPoly(mask, new[] { local }, Scalar.White);
                byte[] skeleton = ZhangSuenThin(mask);
                List<Point2f> curve = SmoothPath(LongestSkeletonPath(skeleton, width, height));
                if (curve.Count < 2) return curve;
                int directionIndex = Math.Min(9, curve.Count - 1);
                curve[0] = ExtendToMask(curve[0], Subtract(curve[0], curve[directionIndex]), skeleton, width, height, mask);
                curve[curve.Count - 1] = ExtendToMask(
                    curve[curve.Count - 1],
                    Subtract(curve[curve.Count - 1], curve[curve.Count - 1 - directionIndex]),
                    skeleton,
                    width,
                    height,
                    mask);
                for (int i = 0; i < curve.Count; i++) curve[i] = new Point2f(curve[i].X + minX, curve[i].Y + minY);
                float dx = curve[curve.Count - 1].X - curve[0].X;
                float dy = curve[curve.Count - 1].Y - curve[0].Y;
                if (dx < 0.0f || (Math.Abs(dx) < 1e-6f && dy < 0.0f)) curve.Reverse();
                return curve;
            }
        }

        private static byte[] ZhangSuenThin(Mat mask)
        {
            Mat source = mask;
            Mat continuous = null;
            try
            {
                if (!mask.IsContinuous())
                {
                    continuous = mask.Clone();
                    source = continuous;
                }
                int width = source.Width;
                int height = source.Height;
                var pixels = new byte[width * height];
                Marshal.Copy(source.Data, pixels, 0, pixels.Length);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i] == 0 ? (byte)0 : (byte)1;
                var marker = new bool[pixels.Length];
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int iteration = 0; iteration < 2; iteration++)
                    {
                        Array.Clear(marker, 0, marker.Length);
                        for (int y = 1; y < height - 1; y++)
                        {
                            for (int x = 1; x < width - 1; x++)
                            {
                                int index = y * width + x;
                                if (pixels[index] == 0) continue;
                                int p2 = pixels[index - width];
                                int p3 = pixels[index - width + 1];
                                int p4 = pixels[index + 1];
                                int p5 = pixels[index + width + 1];
                                int p6 = pixels[index + width];
                                int p7 = pixels[index + width - 1];
                                int p8 = pixels[index - 1];
                                int p9 = pixels[index - width - 1];
                                int neighbours = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9;
                                if (neighbours < 2 || neighbours > 6) continue;
                                int transitions =
                                    (p2 == 0 && p3 == 1 ? 1 : 0) + (p3 == 0 && p4 == 1 ? 1 : 0)
                                    + (p4 == 0 && p5 == 1 ? 1 : 0) + (p5 == 0 && p6 == 1 ? 1 : 0)
                                    + (p6 == 0 && p7 == 1 ? 1 : 0) + (p7 == 0 && p8 == 1 ? 1 : 0)
                                    + (p8 == 0 && p9 == 1 ? 1 : 0) + (p9 == 0 && p2 == 1 ? 1 : 0);
                                if (transitions != 1) continue;
                                if (iteration == 0)
                                {
                                    if (p2 * p4 * p6 != 0 || p4 * p6 * p8 != 0) continue;
                                }
                                else
                                {
                                    if (p2 * p4 * p8 != 0 || p2 * p6 * p8 != 0) continue;
                                }
                                marker[index] = true;
                            }
                        }
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            if (!marker[i]) continue;
                            pixels[i] = 0;
                            changed = true;
                        }
                    }
                }
                return pixels;
            }
            finally
            {
                continuous?.Dispose();
            }
        }

        private static BfsResult RunSkeletonBfs(byte[] skeleton, int width, int height, int start, bool keepPrevious)
        {
            var distance = new int[skeleton.Length];
            for (int i = 0; i < distance.Length; i++) distance[i] = -1;
            int[] previous = keepPrevious ? new int[skeleton.Length] : null;
            if (previous != null) for (int i = 0; i < previous.Length; i++) previous[i] = -1;
            var pending = new Queue<int>();
            distance[start] = 0;
            pending.Enqueue(start);
            int farthest = start;
            int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                if (distance[current] > distance[farthest]) farthest = current;
                int x = current % width;
                int y = current / width;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + dx[k];
                    int ny = y + dy[k];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int next = ny * width + nx;
                    if (skeleton[next] == 0 || distance[next] >= 0) continue;
                    distance[next] = distance[current] + 1;
                    if (previous != null) previous[next] = current;
                    pending.Enqueue(next);
                }
            }
            return new BfsResult { Farthest = farthest, Previous = previous };
        }

        private static List<Point2f> LongestSkeletonPath(byte[] skeleton, int width, int height)
        {
            int start = -1;
            for (int i = 0; i < skeleton.Length; i++)
            {
                if (skeleton[i] != 0)
                {
                    start = i;
                    break;
                }
            }
            if (start < 0) return new List<Point2f>();
            BfsResult first = RunSkeletonBfs(skeleton, width, height, start, false);
            BfsResult second = RunSkeletonBfs(skeleton, width, height, first.Farthest, true);
            var reverse = new List<Point2f>();
            int current = second.Farthest;
            while (current >= 0)
            {
                reverse.Add(new Point2f(current % width, current / width));
                if (current == first.Farthest) break;
                current = second.Previous[current];
            }
            if (reverse.Count < 2) return new List<Point2f>();
            reverse.Reverse();
            return reverse;
        }

        private static List<Point2f> SmoothPath(List<Point2f> input)
        {
            if (input.Count < 3) return input;
            var current = new List<Point2f>(input);
            for (int pass = 0; pass < 2; pass++)
            {
                var smoothed = new List<Point2f>(new Point2f[current.Count]);
                smoothed[0] = current[0];
                smoothed[current.Count - 1] = current[current.Count - 1];
                for (int i = 1; i + 1 < current.Count; i++)
                {
                    int begin = Math.Max(0, i - 4);
                    int end = Math.Min(current.Count - 1, i + 4);
                    float sx = 0.0f;
                    float sy = 0.0f;
                    for (int j = begin; j <= end; j++)
                    {
                        sx += current[j].X;
                        sy += current[j].Y;
                    }
                    float scale = 1.0f / (end - begin + 1);
                    smoothed[i] = new Point2f(sx * scale, sy * scale);
                }
                current = smoothed;
            }
            return current;
        }

        private static Point2f ExtendToMask(Point2f start, Point2f direction, byte[] skeleton, int width, int height, Mat mask)
        {
            double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length < 1e-6) return start;
            float ux = (float)(direction.X / length);
            float uy = (float)(direction.Y / length);
            Point2f last = start;
            for (int step = 0; step <= 300; step++)
            {
                Point2f point = new Point2f(start.X + ux * step, start.Y + uy * step);
                int x = (int)Math.Round(point.X);
                int y = (int)Math.Round(point.Y);
                if (x < 0 || y < 0 || x >= width || y >= height || mask.At<byte>(y, x) == 0) break;
                last = point;
            }
            return last;
        }

        private static List<Point2f> ResamplePath(List<Point2f> points, double step)
        {
            if (points.Count < 2) return points;
            var cumulative = new double[points.Count];
            for (int i = 1; i < points.Count; i++) cumulative[i] = cumulative[i - 1] + Distance(points[i], points[i - 1]);
            double total = cumulative[cumulative.Length - 1];
            if (total <= 1e-6) return new List<Point2f> { points[0] };
            int count = Math.Max(2, (int)Math.Floor(total / Math.Max(1e-6, step)) + 1);
            var output = new List<Point2f>(count);
            int segment = 0;
            for (int i = 0; i < count; i++)
            {
                double distance = total * i / (count - 1.0);
                while (segment + 1 < cumulative.Length && cumulative[segment + 1] < distance) segment++;
                if (segment + 1 >= points.Count)
                {
                    output.Add(points[points.Count - 1]);
                    continue;
                }
                double length = cumulative[segment + 1] - cumulative[segment];
                float alpha = length <= 1e-6 ? 0.0f : (float)((distance - cumulative[segment]) / length);
                output.Add(Lerp(points[segment], points[segment + 1], alpha));
            }
            return output;
        }

        private static bool BuildSides(
            List<Point2f> centerCurve,
            List<Point2f> polygon,
            double sampleStep,
            double shrinkInside,
            out List<Point2f> centers,
            out List<Point2f> lefts,
            out List<Point2f> rights)
        {
            centers = new List<Point2f>();
            lefts = new List<Point2f>();
            rights = new List<Point2f>();
            List<Point2f> sampled = ResamplePath(centerCurve, sampleStep);
            if (sampled.Count < 2) return false;
            for (int i = 0; i < sampled.Count; i++)
            {
                Point2f tangent = i == 0
                    ? Subtract(sampled[1], sampled[0])
                    : i + 1 == sampled.Count
                        ? Subtract(sampled[sampled.Count - 1], sampled[sampled.Count - 2])
                        : Subtract(sampled[i + 1], sampled[i - 1]);
                double length = Math.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
                if (length < 1e-6) continue;
                tangent = new Point2f((float)(tangent.X / length), (float)(tangent.Y / length));
                Point2f normal = new Point2f(-tangent.Y, tangent.X);
                double positive = double.PositiveInfinity;
                double negative = double.NegativeInfinity;
                for (int edge = 0; edge < polygon.Count; edge++)
                {
                    double t;
                    if (!SegmentIntersectionParameter(sampled[i], normal, polygon[edge], polygon[(edge + 1) % polygon.Count], out t)) continue;
                    if (t > 1e-6 && t < positive) positive = t;
                    else if (t < -1e-6 && t > negative) negative = t;
                }
                if (double.IsInfinity(positive) || double.IsInfinity(negative)) continue;
                Point2f left = Add(sampled[i], Scale(normal, positive - shrinkInside));
                Point2f right = Add(sampled[i], Scale(normal, negative + shrinkInside));
                if (!PullInside(polygon, sampled[i], ref left) || !PullInside(polygon, sampled[i], ref right)) continue;
                centers.Add(sampled[i]);
                lefts.Add(left);
                rights.Add(right);
            }
            return centers.Count >= 2;
        }

        private static bool SegmentIntersectionParameter(Point2f point, Point2f ray, Point2f a, Point2f b, out double t)
        {
            double sx = b.X - a.X;
            double sy = b.Y - a.Y;
            double cross = ray.X * sy - ray.Y * sx;
            t = 0.0;
            if (Math.Abs(cross) < 1e-12) return false;
            double qx = a.X - point.X;
            double qy = a.Y - point.Y;
            t = (qx * sy - qy * sx) / cross;
            double u = (qx * ray.Y - qy * ray.X) / cross;
            return u >= 0.0 && u <= 1.0;
        }

        private static bool PullInside(List<Point2f> polygon, Point2f center, ref Point2f point)
        {
            for (int i = 0; i < 4; i++)
            {
                if (PointInside(polygon, point)) return true;
                point = Lerp(point, center, 0.5f);
            }
            return PointInside(polygon, point);
        }

        private static bool PointInside(List<Point2f> polygon, Point2f point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Point2f a = polygon[i];
                Point2f b = polygon[j];
                if (DistanceToSegment(point, a, b) <= 1e-3) return true;
                bool intersects = ((a.Y > point.Y) != (b.Y > point.Y))
                    && point.X < (b.X - a.X) * (point.Y - a.Y) / ((b.Y - a.Y) + 1e-20f) + a.X;
                if (intersects) inside = !inside;
            }
            return inside;
        }

        private static double DistanceToSegment(Point2f point, Point2f a, Point2f b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 1e-12) return Distance(point, a);
            double t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double px = a.X + t * dx;
            double py = a.Y + t * dy;
            double ex = point.X - px;
            double ey = point.Y - py;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        private static Mat UnwrapByRemap(
            Mat image,
            List<Point2f> centers,
            List<Point2f> lefts,
            List<Point2f> rights,
            int outputHeight,
            int maxWidth,
            BorderTypes borderMode)
        {
            if (centers.Count < 2 || centers.Count != lefts.Count || centers.Count != rights.Count) return new Mat();
            int height = Math.Max(2, outputHeight);
            double meanLeftY = 0.0;
            double meanRightY = 0.0;
            for (int i = 0; i < lefts.Count; i++)
            {
                meanLeftY += lefts[i].Y;
                meanRightY += rights[i].Y;
            }
            List<Point2f> tops = meanLeftY <= meanRightY ? lefts : rights;
            List<Point2f> bottoms = meanLeftY <= meanRightY ? rights : lefts;
            var cumulative = new double[centers.Count];
            for (int i = 1; i < centers.Count; i++) cumulative[i] = cumulative[i - 1] + Distance(centers[i], centers[i - 1]);
            double total = cumulative[cumulative.Length - 1];
            if (total <= 1e-6) return new Mat();
            int width = Math.Max(2, (int)Math.Round(total));
            if (maxWidth > 0) width = Math.Min(width, maxWidth);

            using (var mapX = new Mat(height, width, MatType.CV_32FC1))
            using (var mapY = new Mat(height, width, MatType.CV_32FC1))
            {
                int segment = 0;
                for (int x = 0; x < width; x++)
                {
                    double distance = (x + 0.5) * total / width;
                    while (segment + 1 < cumulative.Length && cumulative[segment + 1] < distance) segment++;
                    if (segment + 1 >= centers.Count) segment = centers.Count - 2;
                    double length = Math.Max(1e-6, cumulative[segment + 1] - cumulative[segment]);
                    float alpha = (float)((distance - cumulative[segment]) / length);
                    Point2f top = Lerp(tops[segment], tops[segment + 1], alpha);
                    Point2f bottom = Lerp(bottoms[segment], bottoms[segment + 1], alpha);
                    for (int y = 0; y < height; y++)
                    {
                        float vertical = height <= 1 ? 0.0f : (float)y / (height - 1);
                        Point2f source = Lerp(top, bottom, vertical);
                        mapX.Set(y, x, source.X);
                        mapY.Set(y, x, source.Y);
                    }
                }
                var output = new Mat();
                Cv2.Remap(image, output, mapX, mapY, InterpolationFlags.Cubic, borderMode);
                return output;
            }
        }

        private static JArray PolygonToJson(List<Point2f> polygon)
        {
            var result = new JArray();
            foreach (Point2f point in polygon) result.Add(new JArray(point.X, point.Y));
            return result;
        }

        private static Point2f Add(Point2f a, Point2f b)
        {
            return new Point2f(a.X + b.X, a.Y + b.Y);
        }

        private static Point2f Subtract(Point2f a, Point2f b)
        {
            return new Point2f(a.X - b.X, a.Y - b.Y);
        }

        private static Point2f Scale(Point2f point, double scale)
        {
            return new Point2f((float)(point.X * scale), (float)(point.Y * scale));
        }

        private static Point2f Lerp(Point2f a, Point2f b, float alpha)
        {
            return new Point2f(a.X * (1.0f - alpha) + b.X * alpha, a.Y * (1.0f - alpha) + b.Y * alpha);
        }

        private static double Distance(Point2f a, Point2f b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
