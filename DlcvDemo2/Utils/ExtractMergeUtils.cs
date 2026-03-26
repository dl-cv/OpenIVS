using System;
using System.Collections.Generic;
using System.Linq;
using dlcv_infer_csharp;
using OpenCvSharp;
using CSharpObjectResult = dlcv_infer_csharp.Utils.CSharpObjectResult;

namespace DlcvDemo2
{
    internal sealed class ExtractDetection
    {
        public CSharpObjectResult ObjectResult { get; set; }
        public Rect2d MergeAabb { get; set; }
        public int Order { get; set; }
    }

    internal static class ExtractMergeUtils
    {
        private const double MergeIouThreshold = 0.2;
        private const double MergeIosThreshold = 0.2;

        public static Rect2d GetAabbFromObject(CSharpObjectResult obj)
        {
            if (obj.Bbox == null || obj.Bbox.Count < 4)
            {
                return new Rect2d();
            }

            double w = Math.Abs(obj.Bbox[2]);
            double h = Math.Abs(obj.Bbox[3]);
            if (w <= 0 || h <= 0)
            {
                return new Rect2d();
            }

            if (obj.WithAngle || obj.Bbox.Count == 5)
            {
                double cx = obj.Bbox[0];
                double cy = obj.Bbox[1];
                return new Rect2d(cx - w / 2.0, cy - h / 2.0, w, h);
            }

            return new Rect2d(obj.Bbox[0], obj.Bbox[1], w, h);
        }

        public static List<ExtractDetection> MergeExtractResults(List<ExtractDetection> fullImageDetections)
        {
            var mergedAll = new List<ExtractDetection>();
            if (fullImageDetections == null || fullImageDetections.Count == 0)
            {
                return mergedAll;
            }

            foreach (var group in fullImageDetections.GroupBy(x => x.ObjectResult.CategoryName ?? string.Empty))
            {
                var clusters = new List<ExtractDetection>();
                var orderedGroup = group
                    .OrderByDescending(x => GetObjectArea(x.ObjectResult))
                    .ThenByDescending(x => x.ObjectResult.Score)
                    .ThenBy(x => x.Order);

                foreach (var detection in orderedGroup)
                {
                    bool merged = false;
                    for (int i = 0; i < clusters.Count; i++)
                    {
                        if (!CanMerge(clusters[i].MergeAabb, detection.MergeAabb))
                        {
                            continue;
                        }

                        clusters[i].MergeAabb = UnionRect(clusters[i].MergeAabb, detection.MergeAabb);
                        if (ShouldPreferRepresentative(detection, clusters[i]))
                        {
                            clusters[i].ObjectResult = detection.ObjectResult;
                            clusters[i].Order = Math.Min(clusters[i].Order, detection.Order);
                        }

                        merged = true;
                        break;
                    }

                    if (!merged)
                    {
                        clusters.Add(new ExtractDetection
                        {
                            ObjectResult = detection.ObjectResult,
                            MergeAabb = detection.MergeAabb,
                            Order = detection.Order
                        });
                    }
                }

                foreach (var cluster in clusters)
                {
                    if (!cluster.ObjectResult.WithAngle || cluster.ObjectResult.Bbox == null || cluster.ObjectResult.Bbox.Count < 4)
                    {
                        cluster.ObjectResult = BuildAabbObject(cluster.ObjectResult, cluster.MergeAabb);
                    }
                }

                mergedAll.AddRange(clusters);
            }

            mergedAll.Sort((a, b) => a.Order.CompareTo(b.Order));
            return mergedAll;
        }

        private static double GetObjectArea(CSharpObjectResult obj)
        {
            if (obj.Bbox != null && obj.Bbox.Count >= 4)
            {
                return Math.Abs(obj.Bbox[2] * obj.Bbox[3]);
            }

            return obj.Area > 0 ? obj.Area : 0.0;
        }

        private static bool CanMerge(Rect2d a, Rect2d b)
        {
            double inter = IntersectionArea(a, b);
            if (inter <= 0)
            {
                return false;
            }

            double areaA = Math.Max(0, a.Width) * Math.Max(0, a.Height);
            double areaB = Math.Max(0, b.Width) * Math.Max(0, b.Height);
            double union = areaA + areaB - inter;
            if (union <= 0)
            {
                return false;
            }

            double iou = inter / union;
            double ios = inter / Math.Max(1e-6, Math.Min(areaA, areaB));
            return iou >= MergeIouThreshold || ios >= MergeIosThreshold;
        }

        private static bool ShouldPreferRepresentative(ExtractDetection candidate, ExtractDetection current)
        {
            double areaCandidate = GetObjectArea(candidate.ObjectResult);
            double areaCurrent = GetObjectArea(current.ObjectResult);
            if (areaCandidate > areaCurrent + 1e-6)
            {
                return true;
            }

            if (Math.Abs(areaCandidate - areaCurrent) <= 1e-6)
            {
                if (candidate.ObjectResult.Score > current.ObjectResult.Score + 1e-6)
                {
                    return true;
                }

                if (Math.Abs(candidate.ObjectResult.Score - current.ObjectResult.Score) <= 1e-6 &&
                    candidate.Order < current.Order)
                {
                    return true;
                }
            }

            return false;
        }

        private static Rect2d UnionRect(Rect2d a, Rect2d b)
        {
            double minX = Math.Min(a.X, b.X);
            double minY = Math.Min(a.Y, b.Y);
            double maxX = Math.Max(a.X + a.Width, b.X + b.Width);
            double maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new Rect2d(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private static double IntersectionArea(Rect2d a, Rect2d b)
        {
            double x1 = Math.Max(a.X, b.X);
            double y1 = Math.Max(a.Y, b.Y);
            double x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            double y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
            if (x2 <= x1 || y2 <= y1)
            {
                return 0;
            }

            return (x2 - x1) * (y2 - y1);
        }

        private static CSharpObjectResult BuildAabbObject(CSharpObjectResult source, Rect2d aabb)
        {
            var bbox = new List<double>
            {
                aabb.X,
                aabb.Y,
                Math.Max(0, aabb.Width),
                Math.Max(0, aabb.Height)
            };

            return new CSharpObjectResult(
                source.CategoryId,
                source.CategoryName,
                source.Score,
                (float)(bbox[2] * bbox[3]),
                bbox,
                false,
                new Mat(),
                true,
                false,
                -100f);
        }
    }
}
