using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    /// <summary>
    /// ?? Python: post_process/poly_filter, features/poly_filter
    /// ? polygon/poly ? mask??? mask_rle?????/???????
    /// ??????????? polyline ?????? polygon/poly???? bbox?
    /// </summary>
    public class PolyFilter : BaseModule
    {
        static PolyFilter()
        {
            ModuleRegistry.Register("post_process/poly_filter", typeof(PolyFilter));
            ModuleRegistry.Register("features/poly_filter", typeof(PolyFilter));
        }

        public PolyFilter(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();
            string direction = NormalizeDirection(GetStringProperty("direction", "up"));
            double maskThreshold = GetDoubleProperty("mask_threshold", 0.5);
            int leftClip = GetNonNegativeIntProperty("left_clip", "left_crop", "left_trim", "???");
            int rightClip = GetNonNegativeIntProperty("right_clip", "right_crop", "right_trim", "???");

            var outResults = new JArray();
            for (int i = 0; i < results.Count; i++)
            {
                if (!(results[i] is JObject entry) || !string.Equals(entry["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase))
                {
                    outResults.Add(results[i]);
                    continue;
                }

                var sampleResults = entry["sample_results"] as JArray;
                if (sampleResults == null)
                {
                    outResults.Add(entry);
                    continue;
                }

                var newEntry = (JObject)entry.DeepClone();
                var newDets = new JArray();

                for (int di = 0; di < sampleResults.Count; di++)
                {
                    var detObj = sampleResults[di] as JObject;
                    if (detObj == null)
                    {
                        newDets.Add(sampleResults[di]);
                        continue;
                    }

                    var detOut = (JObject)detObj.DeepClone();
                    string sourceType = string.Empty;
                    List<double> bboxXyxy = null;
                    using (var sourceMask = BuildSourceMask(detObj, maskThreshold, out bboxXyxy, out sourceType))
                    {
                        if (sourceMask == null || sourceMask.Empty() || bboxXyxy == null || bboxXyxy.Count < 4)
                        {
                            newDets.Add(detOut);
                            continue;
                        }

                        List<double> bboxNewXywh;
                        var polyline = MaskToPolyline(sourceMask, bboxXyxy, direction, leftClip, rightClip, out bboxNewXywh);
                        if (polyline == null || polyline.Count < 2 || bboxNewXywh == null || bboxNewXywh.Count < 4)
                        {
                            newDets.Add(detOut);
                            continue;
                        }

                        var lineArr = new JArray();
                        for (int pi = 0; pi < polyline.Count; pi++)
                        {
                            var p = polyline[pi];
                            lineArr.Add(new JArray(p.X, p.Y));
                        }
                        var extraInfo = detOut["extra_info"] as JObject ?? new JObject();
                        extraInfo["polyline"] = lineArr;
                        detOut["extra_info"] = extraInfo;
                        detOut["bbox"] = new JArray(bboxNewXywh[0], bboxNewXywh[1], bboxNewXywh[2], bboxNewXywh[3]);

                        var metadata = detOut["metadata"] as JObject ?? new JObject();
                        metadata["poly_filter_direction"] = direction;
                        metadata["poly_filter_source"] = sourceType;
                        metadata["poly_filter_mode"] = "boundary_line";
                        detOut["metadata"] = metadata;
                    }

                    newDets.Add(detOut);
                }

                newEntry["sample_results"] = newDets;
                outResults.Add(newEntry);
            }

            return new ModuleIO(images, outResults);
        }

        private string GetStringProperty(string key, string defaultValue)
        {
            if (Properties == null || !Properties.TryGetValue(key, out object value) || value == null) return defaultValue;
            try { return value.ToString(); } catch { return defaultValue; }
        }

        private double GetDoubleProperty(string key, double defaultValue)
        {
            if (Properties == null || !Properties.TryGetValue(key, out object value) || value == null) return defaultValue;
            try { return Convert.ToDouble(value); } catch { return defaultValue; }
        }

        private int GetNonNegativeIntProperty(params string[] keys)
        {
            if (Properties == null || keys == null) return 0;
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (string.IsNullOrWhiteSpace(key) || !Properties.TryGetValue(key, out object value) || value == null) continue;
                try { return Math.Max(0, Convert.ToInt32(value)); } catch { }
            }
            return 0;
        }

        private static string NormalizeDirection(string value)
        {
            var s = (value ?? "up").Trim().ToLowerInvariant();
            if (s == "down" || s == "bottom" || s == "lower" || s == "?" || s == "lower_half") return "down";
            return "up";
        }

        private static Mat BuildSourceMask(JObject det, double maskThreshold, out List<double> bboxXyxy, out string sourceType)
        {
            bboxXyxy = null;
            sourceType = string.Empty;

            var rings = ExtractPolygonRings(det);
            if (rings.Count > 0)
            {
                var largest = rings[0];
                double bestArea = RingArea(largest);
                for (int i = 1; i < rings.Count; i++)
                {
                    var area = RingArea(rings[i]);
                    if (area > bestArea)
                    {
                        bestArea = area;
                        largest = rings[i];
                    }
                }

                var mask = BuildMaskFromRing(largest, out bboxXyxy);
                if (mask != null && !mask.Empty() && bboxXyxy != null)
                {
                    sourceType = "polygon";
                    return mask;
                }
                mask?.Dispose();
            }

            var bbox = ParseBboxToXyxy(det["bbox"]);
            if (bbox == null || bbox.Count < 4) return null;
            int tw = Math.Max(1, (int)Math.Round(bbox[2] - bbox[0]));
            int th = Math.Max(1, (int)Math.Round(bbox[3] - bbox[1]));

            Mat fromMaskRle = null;
            try
            {
                var maskRle = det["mask_rle"];
                if (maskRle != null && maskRle.Type != JTokenType.Null)
                {
                    fromMaskRle = MaskRleUtils.MaskInfoToMat(maskRle);
                    if (fromMaskRle != null && !fromMaskRle.Empty())
                    {
                        var resized = EnsureMaskSizeAndBinary(fromMaskRle, tw, th, maskThreshold);
                        fromMaskRle.Dispose();
                        bboxXyxy = bbox;
                        sourceType = "mask_rle";
                        return resized;
                    }
                }
            }
            catch
            {
                fromMaskRle?.Dispose();
            }

            if (det["mask_array"] is JArray maskArray)
            {
                var mask = BuildMaskFromMaskArray(maskArray, tw, th, maskThreshold);
                if (mask != null && !mask.Empty())
                {
                    bboxXyxy = bbox;
                    sourceType = "mask_array";
                    return mask;
                }
                mask?.Dispose();
            }

            return null;
        }

        private static List<Point2d> MaskToPolyline(
            Mat maskInput,
            List<double> bboxXyxy,
            string direction,
            int leftClip,
            int rightClip,
            out List<double> bboxOutXywh)
        {
            bboxOutXywh = null;
            if (maskInput == null || maskInput.Empty() || bboxXyxy == null || bboxXyxy.Count < 4) return null;

            using (var largest = KeepLargestComponent(maskInput))
            {
                if (largest == null || largest.Empty()) return null;

                int h = largest.Rows;
                int w = largest.Cols;
                if (h <= 0 || w <= 0) return null;

                double x1 = bboxXyxy[0];
                double y1 = bboxXyxy[1];

                var xVals = new List<double>(w);
                var yVals = new List<double>(w);

                for (int x = 0; x < w; x++)
                {
                    int minY = int.MaxValue;
                    int maxY = int.MinValue;
                    bool found = false;
                    for (int y = 0; y < h; y++)
                    {
                        if (largest.At<byte>(y, x) > 0)
                        {
                            found = true;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                    if (!found) continue;
                    int edgeY = direction == "down" ? maxY : minY;
                    xVals.Add(x1 + x);
                    yVals.Add(y1 + edgeY);
                }

                if (xVals.Count < 2) return null;

                double maxJump = Math.Max(3.0, h * 0.18);
                TrimEdgeOutliers(xVals, yVals, maxJump);
                if (xVals.Count < 2) return null;

                MedianDeSpike(yVals, maxJump, 2);
                var simplified = SimplifyPolyline(xVals, yVals);
                if (simplified.Count == 0) return null;

                if (leftClip > 0 || rightClip > 0)
                {
                    int start = Math.Min(Math.Max(0, leftClip), simplified.Count);
                    int endExclusive = Math.Max(start, simplified.Count - Math.Max(0, rightClip));
                    simplified = simplified.GetRange(start, endExclusive - start);
                }
                if (simplified.Count < 2) return null;

                double bboxMinX = double.MaxValue;
                double bboxMinY = double.MaxValue;
                double bboxMaxX = double.MinValue;
                double bboxMaxY = double.MinValue;
                for (int i = 0; i < simplified.Count; i++)
                {
                    var p = simplified[i];
                    if (p.X < bboxMinX) bboxMinX = p.X;
                    if (p.Y < bboxMinY) bboxMinY = p.Y;
                    if (p.X > bboxMaxX) bboxMaxX = p.X;
                    if (p.Y > bboxMaxY) bboxMaxY = p.Y;
                }

                double bboxW = Math.Max(1.0, bboxMaxX - bboxMinX + 1.0);
                double bboxH = Math.Max(1.0, bboxMaxY - bboxMinY + 1.0);
                bboxOutXywh = new List<double> { bboxMinX, bboxMinY, bboxW, bboxH };
                return simplified;
            }
        }

        private static Mat EnsureMaskSizeAndBinary(Mat src, int targetW, int targetH, double threshold)
        {
            Mat mask = src;
            bool needDispose = false;

            if (src.Type() != MatType.CV_32FC1 && src.Type() != MatType.CV_8UC1)
            {
                mask = new Mat();
                src.ConvertTo(mask, MatType.CV_32FC1);
                needDispose = true;
            }

            Mat resized = mask;
            bool resizedNeedDispose = false;
            if (mask.Cols != targetW || mask.Rows != targetH)
            {
                resized = new Mat();
                Cv2.Resize(mask, resized, new Size(targetW, targetH), 0, 0, InterpolationFlags.Linear);
                resizedNeedDispose = true;
            }

            var bin = new Mat();
            if (resized.Type() == MatType.CV_8UC1)
            {
                Cv2.Threshold(resized, bin, threshold <= 1.0 ? Math.Max(0.0, Math.Min(255.0, threshold * 255.0)) : threshold, 255, ThresholdTypes.Binary);
            }
            else
            {
                Cv2.Compare(resized, new Scalar(threshold), bin, CmpType.GT);
            }

            if (resizedNeedDispose) resized.Dispose();
            if (needDispose) mask.Dispose();
            return bin;
        }

        private static Mat BuildMaskFromMaskArray(JArray maskArray, int targetW, int targetH, double threshold)
        {
            int srcH = maskArray.Count;
            if (srcH <= 0) return null;
            var firstRow = maskArray[0] as JArray;
            if (firstRow == null || firstRow.Count <= 0) return null;
            int srcW = firstRow.Count;

            var srcMat = new Mat(srcH, srcW, MatType.CV_32FC1);
            for (int y = 0; y < srcH; y++)
            {
                var row = maskArray[y] as JArray;
                if (row == null) continue;
                int rowW = Math.Min(srcW, row.Count);
                for (int x = 0; x < rowW; x++)
                {
                    float v = 0f;
                    try { v = row[x].Value<float>(); } catch { }
                    srcMat.Set(y, x, v);
                }
            }

            var bin = EnsureMaskSizeAndBinary(srcMat, targetW, targetH, threshold);
            srcMat.Dispose();
            return bin;
        }

        private static Mat KeepLargestComponent(Mat maskInput)
        {
            if (maskInput == null || maskInput.Empty()) return null;
            var mask = new Mat();
            if (maskInput.Type() == MatType.CV_8UC1) mask = maskInput.Clone();
            else
            {
                Cv2.Compare(maskInput, Scalar.All(0), mask, CmpType.GT);
            }

            var labels = new Mat();
            var stats = new Mat();
            var centroids = new Mat();
            try
            {
                int n = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids, PixelConnectivity.Connectivity8, MatType.CV_32S);
                if (n <= 2)
                {
                    labels.Dispose();
                    stats.Dispose();
                    centroids.Dispose();
                    return mask;
                }

                int bestLabel = 1;
                int bestArea = 0;
                for (int label = 1; label < n; label++)
                {
                    int area = stats.At<int>(label, (int)ConnectedComponentsTypes.Area);
                    if (area > bestArea)
                    {
                        bestArea = area;
                        bestLabel = label;
                    }
                }

                var outMask = new Mat(mask.Rows, mask.Cols, MatType.CV_8UC1, Scalar.Black);
                for (int y = 0; y < labels.Rows; y++)
                {
                    for (int x = 0; x < labels.Cols; x++)
                    {
                        if (labels.At<int>(y, x) == bestLabel) outMask.Set(y, x, (byte)255);
                    }
                }

                mask.Dispose();
                labels.Dispose();
                stats.Dispose();
                centroids.Dispose();
                return outMask;
            }
            catch
            {
                labels.Dispose();
                stats.Dispose();
                centroids.Dispose();
                return mask;
            }
        }

        private static void TrimEdgeOutliers(List<double> xs, List<double> ys, double maxJump)
        {
            if (xs == null || ys == null || xs.Count != ys.Count || xs.Count <= 2) return;

            int start = 0;
            int end = ys.Count - 1;
            while (end - start + 1 >= 3)
            {
                bool changed = false;

                int leftEnd = Math.Min(end, start + 5);
                if (leftEnd >= start + 1)
                {
                    var leftRef = new List<double>();
                    for (int i = start + 1; i <= leftEnd; i++) leftRef.Add(ys[i]);
                    if (leftRef.Count > 0 && Math.Abs(ys[start] - Median(leftRef)) > maxJump)
                    {
                        start += 1;
                        changed = true;
                    }
                }

                if (end - start + 1 < 3) break;

                int rightStart = Math.Max(start, end - 5);
                if (rightStart <= end - 1)
                {
                    var rightRef = new List<double>();
                    for (int i = rightStart; i <= end - 1; i++) rightRef.Add(ys[i]);
                    if (rightRef.Count > 0 && Math.Abs(ys[end] - Median(rightRef)) > maxJump)
                    {
                        end -= 1;
                        changed = true;
                    }
                }

                if (!changed) break;
            }

            if (start == 0 && end == ys.Count - 1) return;

            var nx = new List<double>();
            var ny = new List<double>();
            for (int i = start; i <= end; i++)
            {
                nx.Add(xs[i]);
                ny.Add(ys[i]);
            }
            xs.Clear();
            ys.Clear();
            xs.AddRange(nx);
            ys.AddRange(ny);
        }

        private static void MedianDeSpike(List<double> values, double maxJump, int radius)
        {
            if (values == null || values.Count <= 2) return;
            var original = new List<double>(values);

            int n = values.Count;
            int rr = Math.Max(1, radius);
            for (int i = 0; i < n; i++)
            {
                int lo = Math.Max(0, i - rr);
                int hi = Math.Min(n - 1, i + rr);
                var refs = new List<double>();
                for (int j = lo; j <= hi; j++)
                {
                    if (j == i) continue;
                    refs.Add(original[j]);
                }
                if (refs.Count == 0) continue;
                double med = Median(refs);
                if (Math.Abs(original[i] - med) > maxJump) values[i] = med;
            }
        }

        private static List<Point2d> SimplifyPolyline(List<double> xs, List<double> ys)
        {
            var points = new List<Point2d>();
            if (xs == null || ys == null || xs.Count != ys.Count || xs.Count == 0) return points;
            if (xs.Count <= 2)
            {
                for (int i = 0; i < xs.Count; i++) points.Add(new Point2d(xs[i], ys[i]));
                return points;
            }

            points.Add(new Point2d(xs[0], ys[0]));
            for (int i = 1; i < xs.Count - 1; i++)
            {
                var prev = points[points.Count - 1];
                var cur = new Point2d(xs[i], ys[i]);
                var next = new Point2d(xs[i + 1], ys[i + 1]);

                double v1x = cur.X - prev.X;
                double v1y = cur.Y - prev.Y;
                double v2x = next.X - cur.X;
                double v2y = next.Y - cur.Y;
                double cross = v1x * v2y - v1y * v2x;
                if (Math.Abs(cross) <= 1e-6) continue;
                points.Add(cur);
            }
            points.Add(new Point2d(xs[xs.Count - 1], ys[ys.Count - 1]));
            return points;
        }

        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            values.Sort();
            int n = values.Count;
            if ((n & 1) == 1) return values[n / 2];
            return 0.5 * (values[n / 2 - 1] + values[n / 2]);
        }

        private static List<List<Point2f>> ExtractPolygonRings(JObject det)
        {
            var rings = new List<List<Point2f>>();
            var polygonRing = ParseRing(det["polygon"]);
            if (polygonRing.Count >= 3) rings.Add(polygonRing);

            var polyToken = det["poly"];
            if (polyToken is JArray polyArray)
            {
                var direct = ParseRing(polyArray);
                if (direct.Count >= 3)
                {
                    rings.Add(direct);
                }
                else
                {
                    for (int i = 0; i < polyArray.Count; i++)
                    {
                        var ring = ParseRing(polyArray[i]);
                        if (ring.Count >= 3)
                        {
                            rings.Add(ring);
                        }
                    }
                }
            }
            return rings;
        }

        private static List<Point2f> ParseRing(JToken token)
        {
            var ring = new List<Point2f>();
            var arr = token as JArray;
            if (arr == null || arr.Count < 3) return ring;

            for (int i = 0; i < arr.Count; i++)
            {
                var p = arr[i];
                if (p is JArray pa && pa.Count >= 2)
                {
                    ring.Add(new Point2f(pa[0].Value<float>(), pa[1].Value<float>()));
                }
                else if (p is JObject po && po.ContainsKey("x") && po.ContainsKey("y"))
                {
                    ring.Add(new Point2f(po["x"].Value<float>(), po["y"].Value<float>()));
                }
            }
            return ring.Count >= 3 ? ring : new List<Point2f>();
        }

        private static double RingArea(List<Point2f> ring)
        {
            if (ring == null || ring.Count < 3) return 0.0;
            try
            {
                return Math.Abs(Cv2.ContourArea(ring));
            }
            catch
            {
                return 0.0;
            }
        }

        private static Mat BuildMaskFromRing(List<Point2f> ring, out List<double> bboxXyxy)
        {
            bboxXyxy = null;
            if (ring == null || ring.Count < 3) return null;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < ring.Count; i++)
            {
                var p = ring[i];
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            int x1 = (int)Math.Floor(minX);
            int y1 = (int)Math.Floor(minY);
            int x2 = (int)Math.Ceiling(maxX);
            int y2 = (int)Math.Ceiling(maxY);
            if (x2 <= x1 || y2 <= y1) return null;

            int w = Math.Max(1, x2 - x1);
            int h = Math.Max(1, y2 - y1);
            var mask = Mat.Zeros(h, w, MatType.CV_8UC1);

            var local = new Point[ring.Count];
            for (int i = 0; i < ring.Count; i++)
            {
                int rx = (int)Math.Round(ring[i].X - x1);
                int ry = (int)Math.Round(ring[i].Y - y1);
                local[i] = new Point(rx, ry);
            }

            try
            {
                Cv2.FillPoly(mask, new Point[][] { local }, Scalar.White);
                bboxXyxy = new List<double> { x1, y1, x2, y2 };
                return mask;
            }
            catch
            {
                mask.Dispose();
                return null;
            }
        }

        private static List<double> ParseBboxToXyxy(JToken bboxToken)
        {
            var bbox = bboxToken as JArray;
            if (bbox == null) return null;

            try
            {
                if (bbox.Count == 4)
                {
                    double x = bbox[0].Value<double>();
                    double y = bbox[1].Value<double>();
                    double w = bbox[2].Value<double>();
                    double h = bbox[3].Value<double>();
                    if (w > 0 && h > 0)
                    {
                        return new List<double> { x, y, x + w, y + h };
                    }
                }
                else if (bbox.Count >= 5)
                {
                    double cx = bbox[0].Value<double>();
                    double cy = bbox[1].Value<double>();
                    double w = Math.Max(1.0, bbox[2].Value<double>());
                    double h = Math.Max(1.0, bbox[3].Value<double>());
                    double ang = bbox[4].Value<double>() * 180.0 / Math.PI;
                    var rect = new RotatedRect(new Point2f((float)cx, (float)cy), new Size2f((float)w, (float)h), (float)ang);
                    var pts = rect.Points();
                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var p = pts[i];
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                    }
                    return new List<double> { minX, minY, maxX, maxY };
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
