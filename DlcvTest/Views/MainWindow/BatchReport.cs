using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace DlcvTest
{
    /// <summary>
    /// 批量预测报表：汇总简报、详细统计、过漏检分析（对齐 dlcv_test_2 的 txt 报表语义）。
    /// </summary>
    internal static class BatchReport
    {
        public sealed class PredObject
        {
            public string CategoryName { get; set; }
            public float Score { get; set; }
            public List<double> Bbox { get; set; }
        }

        public sealed class ImageRecord
        {
            public string ImagePath { get; set; }
            public bool Success { get; set; }
            public bool HasResults { get; set; }
            public double InferMs { get; set; }
            public List<PredObject> Objects { get; set; } = new List<PredObject>();
        }

        public sealed class SummaryInput
        {
            public string OutputDir { get; set; }
            public string ReportName { get; set; }
            public string SrcDir { get; set; }
            public string ModelPath { get; set; }
            public double Threshold { get; set; }
            public DateTime StartTime { get; set; }
            public TimeSpan Elapsed { get; set; }
            public IList<ImageRecord> Records { get; set; }
            public bool WriteSummary { get; set; } = true;
            public bool WriteDetailed { get; set; }
            public bool WriteMissed { get; set; }
            public double IouThreshold { get; set; } = 0.5;
        }

        public static void WriteAll(SummaryInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(input.OutputDir)) throw new ArgumentException("OutputDir 为空", nameof(input));
            if (input.Records == null) input.Records = Array.Empty<ImageRecord>();

            Directory.CreateDirectory(input.OutputDir);

            string reportName = string.IsNullOrWhiteSpace(input.ReportName) ? "batch_report" : SanitizeFileName(input.ReportName);
            if (input.WriteSummary)
            {
                string summaryPath = Path.Combine(input.OutputDir, reportName + ".txt");
                WriteSummary(summaryPath, input);
            }

            if (input.WriteDetailed)
            {
                string detailDir = Path.Combine(input.OutputDir, "详细统计");
                Directory.CreateDirectory(detailDir);
                string detailPath = Path.Combine(detailDir, reportName + "_详细统计.txt");
                WriteDetailedStatistics(detailPath, input.Records);

                if (input.WriteMissed)
                {
                    string missedDir = Path.Combine(detailDir, "过漏检分析");
                    Directory.CreateDirectory(missedDir);
                    string missedPath = Path.Combine(missedDir, reportName + "_过漏检分析.txt");
                    WriteMissedDetectionAnalysis(missedPath, input.Records, input.SrcDir, input.IouThreshold);
                }
            }
        }

        private static void WriteSummary(string path, SummaryInput input)
        {
            var records = input.Records;
            int total = records.Count;
            int ok = records.Count(r => r.Success && !r.HasResults);
            int ng = records.Count(r => r.Success && r.HasResults);
            int failed = records.Count(r => !r.Success);

            var categoryDist = new Dictionary<string, int>(StringComparer.Ordinal);
            int resultCount = 0;
            foreach (var rec in records)
            {
                if (rec?.Objects == null) continue;
                foreach (var obj in rec.Objects)
                {
                    string name = string.IsNullOrWhiteSpace(obj.CategoryName) ? "未知" : obj.CategoryName.Trim();
                    if (!categoryDist.ContainsKey(name)) categoryDist[name] = 0;
                    categoryDist[name]++;
                    resultCount++;
                }
            }

            var inferMsList = records.Where(r => r.Success && r.InferMs > 0).Select(r => r.InferMs).ToList();
            double avgInfer = inferMsList.Count > 0 ? inferMsList.Average() : 0.0;
            double minInfer = inferMsList.Count > 0 ? inferMsList.Min() : 0.0;
            double maxInfer = inferMsList.Count > 0 ? inferMsList.Max() : 0.0;

            string elapsedText = FormatElapsed(input.Elapsed);
            double okPct = total > 0 ? ok * 100.0 / total : 0.0;
            double ngPct = total > 0 ? ng * 100.0 / total : 0.0;

            var sb = new StringBuilder();
            sb.AppendLine("测试时间: " + input.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("预测耗时: " + elapsedText);
            sb.AppendLine("数据集路径: " + (input.SrcDir ?? string.Empty));
            sb.AppendLine("模型路径: " + (input.ModelPath ?? string.Empty));
            sb.AppendLine("阈值: " + input.Threshold.ToString("0.####", CultureInfo.InvariantCulture));
            sb.AppendLine("图片数量: " + total.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("OK图片数量: " + ok.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("NG图片数量: " + ng.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("失败图片数量: " + failed.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("结果数量: " + resultCount.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("OK图片百分比: " + okPct.ToString("0.00", CultureInfo.InvariantCulture) + "%");
            sb.AppendLine("NG图片百分比: " + ngPct.ToString("0.00", CultureInfo.InvariantCulture) + "%");
            if (inferMsList.Count > 0)
            {
                sb.AppendLine("平均推理耗时(ms): " + avgInfer.ToString("0.00", CultureInfo.InvariantCulture));
                sb.AppendLine("最小推理耗时(ms): " + minInfer.ToString("0.00", CultureInfo.InvariantCulture));
                sb.AppendLine("最大推理耗时(ms): " + maxInfer.ToString("0.00", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
            sb.AppendLine("类别分布（降序）：");
            foreach (var kv in categoryDist.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
            {
                sb.AppendLine(kv.Key + ": " + kv.Value.ToString(CultureInfo.InvariantCulture));
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void WriteDetailedStatistics(string path, IList<ImageRecord> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 详细预测统计 ===");
            sb.AppendLine();

            for (int idx = 0; idx < records.Count; idx++)
            {
                var rec = records[idx];
                string imgName = rec?.ImagePath != null ? Path.GetFileName(rec.ImagePath) : string.Empty;
                sb.AppendLine("图片 " + (idx + 1).ToString(CultureInfo.InvariantCulture) + ": " + imgName);

                if (rec == null || !rec.Success)
                {
                    sb.AppendLine("  处理失败");
                    sb.AppendLine();
                    continue;
                }

                if (rec.Objects == null || rec.Objects.Count == 0)
                {
                    sb.AppendLine("  无检测结果");
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine("  检测目标数量: " + rec.Objects.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < rec.Objects.Count; i++)
                {
                    var item = rec.Objects[i];
                    sb.AppendLine("  目标 " + (i + 1).ToString(CultureInfo.InvariantCulture) + ":");
                    sb.AppendLine("    类别: " + (string.IsNullOrWhiteSpace(item.CategoryName) ? "未知" : item.CategoryName));
                    sb.AppendLine("    置信度: " + item.Score.ToString("0.0000", CultureInfo.InvariantCulture));
                    if (item.Bbox != null && item.Bbox.Count > 0)
                    {
                        sb.AppendLine("    bbox: [" + string.Join(", ", item.Bbox.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))) + "]");
                    }
                }
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void WriteMissedDetectionAnalysis(string path, IList<ImageRecord> records, string srcDir, double iouThreshold)
        {
            int totalImages = records.Count;
            int annotatedImages = 0;
            int totalMissed = 0;
            int totalOver = 0;
            int totalMisclass = 0;

            string parentDir = Path.GetDirectoryName(path) ?? ".";
            string missedImageDir = Path.Combine(parentDir, "漏检图片");
            string overImageDir = Path.Combine(parentDir, "过检图片");
            string misclassImageDir = Path.Combine(parentDir, "误检图片");

            var sb = new StringBuilder();
            sb.AppendLine("=== 过漏检分析报告 ===");
            sb.AppendLine("IoU 阈值: " + iouThreshold.ToString("0.###", CultureInfo.InvariantCulture));
            sb.AppendLine();

            foreach (var rec in records)
            {
                if (rec == null || string.IsNullOrWhiteSpace(rec.ImagePath) || !rec.Success) continue;

                string jsonPath = Path.ChangeExtension(rec.ImagePath, ".json");
                if (!File.Exists(jsonPath)) continue;

                var gtShapes = LoadLabelMeShapes(jsonPath);
                if (gtShapes == null || gtShapes.Count == 0) continue;

                annotatedImages++;
                var preds = (rec.Objects ?? new List<PredObject>())
                    .Select(o => new PredObject
                    {
                        CategoryName = o.CategoryName ?? string.Empty,
                        Score = o.Score,
                        Bbox = ToRect(o.Bbox)
                    })
                    .Where(o => o.Bbox != null)
                    .ToList();

                CompareWithAnnotations(preds, gtShapes, iouThreshold,
                    out var missedList, out var overList, out var misclassList);

                if (missedList.Count == 0 && overList.Count == 0 && misclassList.Count == 0) continue;

                sb.AppendLine("图片: " + Path.GetFileName(rec.ImagePath));

                if (missedList.Count > 0)
                {
                    totalMissed += missedList.Count;
                    sb.AppendLine("  漏检 (" + missedList.Count.ToString(CultureInfo.InvariantCulture) + "):");
                    foreach (var item in missedList)
                    {
                        sb.AppendLine("    - 标注类别: " + item.Label + ", bbox: " + FormatBbox(item.Bbox));
                    }
                    TryCopyErrorImage(missedImageDir, rec.ImagePath, "漏检");
                }

                if (overList.Count > 0)
                {
                    totalOver += overList.Count;
                    sb.AppendLine("  过检 (" + overList.Count.ToString(CultureInfo.InvariantCulture) + "):");
                    foreach (var item in overList)
                    {
                        sb.AppendLine(
                            "    - 预测类别: " + item.CategoryName +
                            ", 置信度: " + item.Score.ToString("0.0000", CultureInfo.InvariantCulture) +
                            ", bbox: " + FormatBbox(item.Bbox));
                    }
                    TryCopyErrorImage(overImageDir, rec.ImagePath, "过检");
                }

                if (misclassList.Count > 0)
                {
                    totalMisclass += misclassList.Count;
                    sb.AppendLine("  误检 (" + misclassList.Count.ToString(CultureInfo.InvariantCulture) + "):");
                    foreach (var item in misclassList)
                    {
                        sb.AppendLine(
                            "    - 标注类别: " + item.GtLabel +
                            ", 预测类别: " + item.PredLabel +
                            ", 置信度: " + item.Score.ToString("0.0000", CultureInfo.InvariantCulture) +
                            ", bbox: " + FormatBbox(item.Bbox));
                    }
                    TryCopyErrorImage(misclassImageDir, rec.ImagePath, "误检");
                }

                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("总计:");
            sb.AppendLine("  总图片数: " + totalImages.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("  有标注的图片数: " + annotatedImages.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("  漏检总数: " + totalMissed.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("  过检总数: " + totalOver.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("  误检总数: " + totalMisclass.ToString(CultureInfo.InvariantCulture));

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private sealed class GtItem
        {
            public string Label { get; set; }
            public List<double> Bbox { get; set; }
        }

        private sealed class MissItem
        {
            public string Label { get; set; }
            public List<double> Bbox { get; set; }
        }

        private sealed class OverItem
        {
            public string CategoryName { get; set; }
            public float Score { get; set; }
            public List<double> Bbox { get; set; }
        }

        private sealed class MisclassItem
        {
            public string GtLabel { get; set; }
            public string PredLabel { get; set; }
            public float Score { get; set; }
            public List<double> Bbox { get; set; }
        }

        private static List<GtItem> LoadLabelMeShapes(string jsonPath)
        {
            var list = new List<GtItem>();
            try
            {
                var json = JObject.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
                var shapes = json["shapes"] as JArray;
                if (shapes == null) return list;

                foreach (var shape in shapes)
                {
                    if (!(shape is JObject shapeObj)) continue;
                    string label = shapeObj["label"]?.ToString();
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    var points = shapeObj["points"] as JArray;
                    var bbox = BboxFromPoints(points);
                    if (bbox == null) continue;
                    list.Add(new GtItem { Label = label, Bbox = bbox });
                }
            }
            catch
            {
                // ignore broken annotation
            }
            return list;
        }

        private static List<double> BboxFromPoints(JArray points)
        {
            if (points == null || points.Count < 2) return null;
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            int valid = 0;
            foreach (var pt in points)
            {
                if (!(pt is JArray arr) || arr.Count < 2) continue;
                double x = arr[0].Value<double>();
                double y = arr[1].Value<double>();
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                valid++;
            }
            if (valid < 2) return null;
            return new List<double> { minX, minY, maxX - minX, maxY - minY };
        }

        /// <summary>
        /// 将预测 bbox 规整为 [x, y, w, h]。
        /// 4 元：若像 [x1,y1,x2,y2] 则转换；否则视为已是 [x,y,w,h]。
        /// 5 元：旋转框 [cx,cy,w,h,angle] 取外接轴对齐近似。
        /// </summary>
        private static List<double> ToRect(List<double> bbox)
        {
            if (bbox == null) return null;
            if (bbox.Count == 4)
            {
                if (bbox[2] > bbox[0] && bbox[3] > bbox[1])
                {
                    return new List<double> { bbox[0], bbox[1], bbox[2] - bbox[0], bbox[3] - bbox[1] };
                }
                return new List<double> { bbox[0], bbox[1], bbox[2], bbox[3] };
            }
            if (bbox.Count >= 5)
            {
                double cx = bbox[0], cy = bbox[1], w = bbox[2], h = bbox[3];
                return new List<double> { cx - w / 2.0, cy - h / 2.0, w, h };
            }
            return null;
        }

        private static double CalculateIou(List<double> a, List<double> b)
        {
            if (a == null || b == null || a.Count < 4 || b.Count < 4) return 0.0;
            double ax1 = a[0], ay1 = a[1], aw = a[2], ah = a[3];
            double bx1 = b[0], by1 = b[1], bw = b[2], bh = b[3];
            double ax2 = ax1 + aw, ay2 = ay1 + ah;
            double bx2 = bx1 + bw, by2 = by1 + bh;

            double ix1 = Math.Max(ax1, bx1);
            double iy1 = Math.Max(ay1, by1);
            double ix2 = Math.Min(ax2, bx2);
            double iy2 = Math.Min(ay2, by2);
            double iw = Math.Max(0.0, ix2 - ix1);
            double ih = Math.Max(0.0, iy2 - iy1);
            double inter = iw * ih;
            double union = aw * ah + bw * bh - inter;
            if (union <= 0.0) return 0.0;
            return inter / union;
        }

        private static void CompareWithAnnotations(
            List<PredObject> preds,
            List<GtItem> gts,
            double iouThreshold,
            out List<MissItem> missed,
            out List<OverItem> over,
            out List<MisclassItem> misclass)
        {
            missed = new List<MissItem>();
            over = new List<OverItem>();
            misclass = new List<MisclassItem>();

            var predMatched = new bool[preds.Count];

            foreach (var gt in gts)
            {
                int bestIdx = -1;
                double bestIou = 0.0;
                for (int i = 0; i < preds.Count; i++)
                {
                    if (predMatched[i]) continue;
                    double iou = CalculateIou(gt.Bbox, preds[i].Bbox);
                    if (iou > bestIou)
                    {
                        bestIou = iou;
                        bestIdx = i;
                    }
                }

                if (bestIdx < 0 || bestIou < iouThreshold)
                {
                    missed.Add(new MissItem { Label = gt.Label, Bbox = gt.Bbox });
                }
                else
                {
                    predMatched[bestIdx] = true;
                    string predLabel = preds[bestIdx].CategoryName ?? string.Empty;
                    if (!string.Equals(predLabel, gt.Label, StringComparison.Ordinal))
                    {
                        misclass.Add(new MisclassItem
                        {
                            GtLabel = gt.Label,
                            PredLabel = predLabel,
                            Score = preds[bestIdx].Score,
                            Bbox = preds[bestIdx].Bbox
                        });
                    }
                }
            }

            for (int i = 0; i < preds.Count; i++)
            {
                if (predMatched[i]) continue;
                over.Add(new OverItem
                {
                    CategoryName = preds[i].CategoryName,
                    Score = preds[i].Score,
                    Bbox = preds[i].Bbox
                });
            }
        }

        private static void TryCopyErrorImage(string dstDir, string imgPath, string errorType)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imgPath) || !File.Exists(imgPath)) return;
                Directory.CreateDirectory(dstDir);
                string stem = Path.GetFileNameWithoutExtension(imgPath);
                string ext = Path.GetExtension(imgPath);
                string name = string.IsNullOrEmpty(errorType)
                    ? Path.GetFileName(imgPath)
                    : stem + "_" + errorType + ext;
                string dst = Path.Combine(dstDir, name);
                File.Copy(imgPath, dst, true);
            }
            catch
            {
                // ignore copy failure
            }
        }

        private static string FormatBbox(List<double> bbox)
        {
            if (bbox == null || bbox.Count == 0) return "[]";
            return "[" + string.Join(", ", bbox.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))) + "]";
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds < 60.0)
            {
                return elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "秒";
            }
            return (elapsed.TotalSeconds / 60.0).ToString("0.00", CultureInfo.InvariantCulture) + "分钟";
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "batch_report";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            }
            var cleaned = sb.ToString().Trim().TrimEnd(' ', '.');
            return string.IsNullOrWhiteSpace(cleaned) ? "batch_report" : cleaned;
        }

        /// <summary>
        /// 从推理结果提取轻量目标列表（不持有 Mask，避免批量占用显存/内存）。
        /// </summary>
        public static ImageRecord FromResult(string imagePath, bool success, double inferMs, dlcv_infer_csharp.Utils.CSharpResult? result)
        {
            var rec = new ImageRecord
            {
                ImagePath = imagePath,
                Success = success,
                InferMs = inferMs,
                HasResults = false,
                Objects = new List<PredObject>()
            };

            if (!success || !result.HasValue) return rec;

            try
            {
                var samples = result.Value.SampleResults;
                if (samples == null || samples.Count == 0) return rec;
                var objs = samples[0].Results;
                if (objs == null || objs.Count == 0) return rec;

                rec.HasResults = true;
                foreach (var o in objs)
                {
                    List<double> bboxCopy = null;
                    if (o.Bbox != null)
                    {
                        bboxCopy = new List<double>(o.Bbox);
                    }
                    rec.Objects.Add(new PredObject
                    {
                        CategoryName = o.CategoryName,
                        Score = o.Score,
                        Bbox = bboxCopy
                    });
                }
            }
            catch
            {
                // keep empty objects
            }

            return rec;
        }
    }
}
