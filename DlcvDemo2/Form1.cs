using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using CSharpObjectResult = dlcv_infer_csharp.Utils.CSharpObjectResult;
using CSharpResult = dlcv_infer_csharp.Utils.CSharpResult;
using CSharpSampleResult = dlcv_infer_csharp.Utils.CSharpSampleResult;

namespace DlcvDemo2
{
    public partial class Form1 : Form
    {
        private const string ModelFileFilter = "AI模型 (*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp)|*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp|所有文件 (*.*)|*.*";
        private static readonly Regex CategoryAngleRegex = new Regex(@"^(.*?)(0|90|180|270)$", RegexOptions.Compiled);
        private static readonly HashSet<string> ExcludedMainCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            "字符",
            "引脚",
            "焊点"
        };

        private const double MergeIouThreshold = 0.2;
        private const double MergeIosThreshold = 0.2;

        private Model model1;
        private Model model2;
        private string imagePath;

        private sealed class Model1Detection
        {
            public CSharpObjectResult ObjectResult { get; set; }
            public Rect2d MergeAabb { get; set; }
            public int Order { get; set; }
        }

        private sealed class ParsedCategoryAngle
        {
            public string BaseName { get; set; }
            public int Angle { get; set; }
        }

        private sealed class RoiProcessResult : IDisposable
        {
            public bool IsValid { get; set; }
            public string InvalidReason { get; set; }
            public Mat NormalizedRoi { get; set; }
            public double[] NormToFullAffine { get; set; }

            public void Dispose()
            {
                if (NormalizedRoi != null)
                {
                    NormalizedRoi.Dispose();
                    NormalizedRoi = null;
                }
            }
        }

        private sealed class PipelineRunResult
        {
            public CSharpResult DisplayResult { get; set; }
            public int SlidingWindowCount { get; set; }
            public int MergedModel1Count { get; set; }
            public List<CSharpObjectResult> FinalObjects { get; } = new List<CSharpObjectResult>();
            public List<string> Logs { get; } = new List<string>();
        }

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            RestoreUiSettings();
            TopMost = false;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveUiSettings();
            ReleaseModels();
            Utils.FreeAllModels();
        }

        private void btnBrowseModel1_Click(object sender, EventArgs e)
        {
            var selected = BrowseFile("选择模型1", ModelFileFilter, txtModel1Path.Text);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                txtModel1Path.Text = selected;
                SaveUiSettings();
            }
        }

        private void btnBrowseModel2_Click(object sender, EventArgs e)
        {
            var selected = BrowseFile("选择模型2", ModelFileFilter, txtModel2Path.Text);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                txtModel2Path.Text = selected;
                SaveUiSettings();
            }
        }

        private void btnBrowseImage_Click(object sender, EventArgs e)
        {
            var selected = BrowseFile(
                "选择图片文件",
                "图片文件 (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif|所有文件 (*.*)|*.*",
                txtImagePath.Text);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                txtImagePath.Text = selected;
                imagePath = selected;
                SaveUiSettings();
            }
        }

        private void btnLoadModel1_Click(object sender, EventArgs e)
        {
            try
            {
                if (LoadModel1())
                {
                    richTextBox1.Text = $"模型1加载成功:\n{txtModel1Path.Text.Trim()}";
                }
            }
            catch (Exception ex)
            {
                richTextBox1.Text = $"模型1加载失败:\n{ex.Message}";
            }
        }

        private void btnLoadModel2_Click(object sender, EventArgs e)
        {
            try
            {
                if (LoadModel2())
                {
                    richTextBox1.Text = $"模型2加载成功:\n{txtModel2Path.Text.Trim()}";
                }
            }
            catch (Exception ex)
            {
                richTextBox1.Text = $"模型2加载失败:\n{ex.Message}";
            }
        }

        private void btnInfer_Click(object sender, EventArgs e)
        {
            try
            {
                SaveUiSettings();
                if (!EnsureReadyForPipeline(showMessage: true))
                {
                    return;
                }

                Mat imageBgr;
                Mat imageRgb;
                string error;
                if (!TryLoadImageForInfer(imagePath, out imageBgr, out imageRgb, out error))
                {
                    richTextBox1.Text = error;
                    return;
                }

                using (imageBgr)
                using (imageRgb)
                {
                    var sw = Stopwatch.StartNew();
                    PipelineRunResult runResult = RunFixedPipeline(imageRgb);
                    sw.Stop();

                    imagePanel1.UpdateImageAndResult(imageBgr, runResult.DisplayResult);
                    richTextBox1.Text = BuildInferenceText(runResult, sw.Elapsed.TotalMilliseconds);
                }
            }
            catch (Exception ex)
            {
                richTextBox1.Text = $"执行推理失败:\n{ex}";
            }
        }

        private void btnSpeedTest_Click(object sender, EventArgs e)
        {
            try
            {
                SaveUiSettings();
                if (!EnsureReadyForPipeline(showMessage: true))
                {
                    return;
                }

                int rounds = (int)numSpeedRounds.Value;
                if (rounds <= 0)
                {
                    MessageBox.Show("测速轮数必须大于0。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Mat imageBgr;
                Mat imageRgb;
                string error;
                if (!TryLoadImageForInfer(imagePath, out imageBgr, out imageRgb, out error))
                {
                    richTextBox1.Text = error;
                    return;
                }

                using (imageBgr)
                using (imageRgb)
                {
                    var costs = new List<double>(rounds);
                    PipelineRunResult lastResult = null;

                    for (int i = 0; i < rounds; i++)
                    {
                        var sw = Stopwatch.StartNew();
                        lastResult = RunFixedPipeline(imageRgb);
                        sw.Stop();
                        costs.Add(sw.Elapsed.TotalMilliseconds);
                    }

                    richTextBox1.Text = BuildSpeedTestText(costs, rounds, lastResult);
                }
            }
            catch (Exception ex)
            {
                richTextBox1.Text = $"测速失败:\n{ex}";
            }
        }

        private void btnReleaseModels_Click(object sender, EventArgs e)
        {
            ReleaseModels();
            richTextBox1.Text = "模型已释放";
        }

        private bool LoadModel1()
        {
            string path = (txtModel1Path.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("请先选择有效的模型1文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            DisposeModel(ref model1);
            model1 = new Model(path, 0, false);
            SaveUiSettings();
            return true;
        }

        private bool LoadModel2()
        {
            string path = (txtModel2Path.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("请先选择有效的模型2文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            DisposeModel(ref model2);
            model2 = new Model(path, 0, false);
            SaveUiSettings();
            return true;
        }

        private PipelineRunResult RunFixedPipeline(Mat fullImageRgb)
        {
            var runResult = new PipelineRunResult();

            List<Rect> windows = BuildSlidingWindows(fullImageRgb);
            runResult.SlidingWindowCount = windows.Count;

            List<Model1Detection> model1Detections = InferModel1OnWindows(fullImageRgb, windows);
            List<Model1Detection> mergedModel1 = MergeModel1Results(model1Detections);
            runResult.MergedModel1Count = mergedModel1.Count;

            foreach (var target in mergedModel1)
            {
                ParsedCategoryAngle parsed = ParseCategoryAndAngle(target.ObjectResult.CategoryName);
                using (RoiProcessResult roi = CropAndRotateRoi(fullImageRgb, target, parsed))
                {
                    if (!roi.IsValid)
                    {
                        runResult.Logs.Add($"跳过目标[{target.ObjectResult.CategoryName}]：{roi.InvalidReason}");
                        continue;
                    }

                    try
                    {
                        List<CSharpObjectResult> mapped = InferModel2AndMapBack(roi, target.ObjectResult);
                        runResult.FinalObjects.AddRange(mapped);
                    }
                    catch (Exception ex)
                    {
                        runResult.Logs.Add($"目标[{target.ObjectResult.CategoryName}]模型2推理失败，保留模型1兜底：{ex.Message}");
                        runResult.FinalObjects.Add(target.ObjectResult);
                    }
                }
            }

            runResult.DisplayResult = BuildDisplayResult(runResult.FinalObjects);
            return runResult;
        }

        private List<Rect> BuildSlidingWindows(Mat image)
        {
            int configuredW = (int)numWindowWidth.Value;
            int configuredH = (int)numWindowHeight.Value;
            int overlapX = (int)numOverlapX.Value;
            int overlapY = (int)numOverlapY.Value;

            int windowW = Math.Min(Math.Max(1, configuredW), Math.Max(1, image.Width));
            int windowH = Math.Min(Math.Max(1, configuredH), Math.Max(1, image.Height));

            List<int> xs = BuildStartPositions(image.Width, windowW, overlapX);
            List<int> ys = BuildStartPositions(image.Height, windowH, overlapY);

            var windows = new List<Rect>(xs.Count * ys.Count);
            foreach (int y in ys)
            {
                foreach (int x in xs)
                {
                    windows.Add(new Rect(x, y, windowW, windowH));
                }
            }
            return windows;
        }

        private List<Model1Detection> InferModel1OnWindows(Mat fullImageRgb, List<Rect> windows)
        {
            var output = new List<Model1Detection>();
            int order = 0;
            JObject inferParams = new JObject
            {
                ["with_mask"] = false
            };

            foreach (var window in windows)
            {
                using (var tile = new Mat(fullImageRgb, window).Clone())
                {
                    CSharpResult tileResult = model1.Infer(tile, inferParams);
                    if (tileResult.SampleResults == null || tileResult.SampleResults.Count == 0)
                    {
                        continue;
                    }

                    foreach (var obj in tileResult.SampleResults[0].Results)
                    {
                        CSharpObjectResult mapped = LiftModel1ObjectToFull(obj, window);
                        Rect2d aabb = GetAabbFromObject(mapped);
                        if (aabb.Width <= 0 || aabb.Height <= 0)
                        {
                            continue;
                        }

                        output.Add(new Model1Detection
                        {
                            ObjectResult = mapped,
                            MergeAabb = aabb,
                            Order = order++
                        });
                    }
                }
            }

            return output;
        }

        private List<Model1Detection> MergeModel1Results(List<Model1Detection> fullImageDetections)
        {
            var mergedAll = new List<Model1Detection>();
            if (fullImageDetections == null || fullImageDetections.Count == 0)
            {
                return mergedAll;
            }

            foreach (var group in fullImageDetections.GroupBy(x => x.ObjectResult.CategoryName ?? string.Empty))
            {
                var clusters = new List<Model1Detection>();
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
                        clusters.Add(new Model1Detection
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

        private ParsedCategoryAngle ParseCategoryAndAngle(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return new ParsedCategoryAngle { BaseName = string.Empty, Angle = 0 };
            }

            Match match = CategoryAngleRegex.Match(categoryName.Trim());
            if (!match.Success)
            {
                return new ParsedCategoryAngle
                {
                    BaseName = categoryName,
                    Angle = 0
                };
            }

            int parsedAngle;
            if (!int.TryParse(match.Groups[2].Value, out parsedAngle))
            {
                parsedAngle = 0;
            }

            string baseName = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = categoryName;
            }

            return new ParsedCategoryAngle
            {
                BaseName = baseName,
                Angle = parsedAngle
            };
        }

        private RoiProcessResult CropAndRotateRoi(Mat fullImageRgb, Model1Detection target, ParsedCategoryAngle parsedCategory)
        {
            var result = new RoiProcessResult
            {
                IsValid = false,
                InvalidReason = "未知错误"
            };

            Mat roi = null;
            double[] fullToCropAffine = null;

            try
            {
                if (TryBuildRotatedCrop(fullImageRgb, target.ObjectResult, out roi, out fullToCropAffine))
                {
                    // 使用旋转裁剪结果
                }
                else
                {
                    Rect roiRect = ClampRectToImage(target.MergeAabb, fullImageRgb.Width, fullImageRgb.Height);
                    if (roiRect.Width <= 1 || roiRect.Height <= 1)
                    {
                        result.InvalidReason = "ROI无效";
                        return result;
                    }

                    roi = new Mat(fullImageRgb, roiRect).Clone();
                    fullToCropAffine = new[] { 1.0, 0.0, -roiRect.X, 0.0, 1.0, -roiRect.Y };
                }

                if (roi == null || roi.Empty())
                {
                    result.InvalidReason = "ROI为空";
                    return result;
                }

                int normalizeAngle = NormalizeAngle(parsedCategory.Angle);
                Mat normalized = RotateRoiByRightAngle(roi, normalizeAngle);
                if (normalized == null || normalized.Empty())
                {
                    result.InvalidReason = "ROI归一化失败";
                    return result;
                }

                int _;
                int __;
                double[] cropToNorm = BuildRightAngleAffine(roi.Width, roi.Height, normalizeAngle, out _, out __);
                double[] normToCrop = InvertAffine2x3(cropToNorm);
                double[] cropToFull = InvertAffine2x3(fullToCropAffine);
                double[] normToFull = ComposeAffine(cropToFull, normToCrop);

                result.IsValid = true;
                result.InvalidReason = string.Empty;
                result.NormalizedRoi = normalized;
                result.NormToFullAffine = normToFull;
                return result;
            }
            catch (Exception ex)
            {
                result.InvalidReason = ex.Message;
                return result;
            }
            finally
            {
                if (roi != null)
                {
                    roi.Dispose();
                }
            }
        }

        private List<CSharpObjectResult> InferModel2AndMapBack(RoiProcessResult roiContext, CSharpObjectResult model1Fallback)
        {
            JObject inferParams = new JObject
            {
                ["with_mask"] = false
            };

            CSharpResult roiResult = model2.Infer(roiContext.NormalizedRoi, inferParams);
            if (roiResult.SampleResults == null || roiResult.SampleResults.Count == 0)
            {
                return new List<CSharpObjectResult> { model1Fallback };
            }

            List<CSharpObjectResult> rawObjects = roiResult.SampleResults[0].Results ?? new List<CSharpObjectResult>();
            if (rawObjects.Count == 0)
            {
                return new List<CSharpObjectResult> { model1Fallback };
            }

            var mappedObjects = new List<CSharpObjectResult>();
            foreach (var obj in rawObjects)
            {
                CSharpObjectResult mapped;
                if (TryMapObjectToFull(obj, roiContext.NormToFullAffine, out mapped))
                {
                    mappedObjects.Add(mapped);
                }
            }

            if (mappedObjects.Count == 0)
            {
                return new List<CSharpObjectResult> { model1Fallback };
            }

            var candidateIndexes = new List<int>();
            for (int i = 0; i < mappedObjects.Count; i++)
            {
                if (!ExcludedMainCategories.Contains(mappedObjects[i].CategoryName ?? string.Empty))
                {
                    candidateIndexes.Add(i);
                }
            }

            if (candidateIndexes.Count > 0)
            {
                int bestIndex = SelectMainCandidateIndex(mappedObjects, candidateIndexes);
                mappedObjects[bestIndex] = CloneWithCategoryName(mappedObjects[bestIndex], model1Fallback.CategoryName);
            }

            return mappedObjects;
        }

        private CSharpResult BuildDisplayResult(List<CSharpObjectResult> finalObjects)
        {
            var sampleResults = new List<CSharpSampleResult>
            {
                new CSharpSampleResult(finalObjects ?? new List<CSharpObjectResult>())
            };
            return new CSharpResult(sampleResults);
        }

        private static List<int> BuildStartPositions(int totalSize, int windowSize, int overlap)
        {
            var positions = new List<int>();
            if (totalSize <= 0 || windowSize <= 0)
            {
                return positions;
            }

            if (windowSize >= totalSize)
            {
                positions.Add(0);
                return positions;
            }

            int step = Math.Max(1, windowSize - Math.Max(0, overlap));
            int current = 0;
            while (true)
            {
                if (current + windowSize >= totalSize)
                {
                    int tail = totalSize - windowSize;
                    if (positions.Count == 0 || positions[positions.Count - 1] != tail)
                    {
                        positions.Add(tail);
                    }
                    break;
                }

                positions.Add(current);
                current += step;
            }

            return positions;
        }

        private static CSharpObjectResult LiftModel1ObjectToFull(CSharpObjectResult localObject, Rect windowRect)
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

        private static double GetObjectArea(CSharpObjectResult obj)
        {
            if (obj.Bbox != null && obj.Bbox.Count >= 4)
            {
                return Math.Abs(obj.Bbox[2] * obj.Bbox[3]);
            }

            return obj.Area > 0 ? obj.Area : 0.0;
        }

        private static Rect2d GetAabbFromObject(CSharpObjectResult obj)
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

        private static bool ShouldPreferRepresentative(Model1Detection candidate, Model1Detection current)
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
                (float)(Math.Max(0, aabb.Width) * Math.Max(0, aabb.Height)),
                bbox,
                false,
                new Mat(),
                true,
                false,
                -100f);
        }

        private static Rect ClampRectToImage(Rect2d rect, int imageWidth, int imageHeight)
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

        private static bool TryBuildRotatedCrop(Mat fullImage, CSharpObjectResult obj, out Mat roi, out double[] fullToCropAffine)
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
            double w = Math.Abs(obj.Bbox[2]);
            double h = Math.Abs(obj.Bbox[3]);
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
            Mat rotMat = Cv2.GetRotationMatrix2D(new Point2f((float)cx, (float)cy), angleDeg, 1.0);
            rotMat.Set(0, 2, rotMat.Get<double>(0, 2) + w / 2.0 - cx);
            rotMat.Set(1, 2, rotMat.Get<double>(1, 2) + h / 2.0 - cy);

            int outW = Math.Max(1, (int)Math.Round(w));
            int outH = Math.Max(1, (int)Math.Round(h));

            roi = new Mat();
            Cv2.WarpAffine(fullImage, roi, rotMat, new OpenCvSharp.Size(outW, outH));
            fullToCropAffine = MatrixFromAffineMat(rotMat);
            rotMat.Dispose();
            return roi != null && !roi.Empty();
        }

        private static Mat RotateRoiByRightAngle(Mat roi, int angle)
        {
            if (angle == 0)
            {
                return roi.Clone();
            }

            var rotated = new Mat();
            if (angle == 90)
            {
                Cv2.Rotate(roi, rotated, RotateFlags.Rotate90Counterclockwise);
            }
            else if (angle == 180)
            {
                Cv2.Rotate(roi, rotated, RotateFlags.Rotate180);
            }
            else if (angle == 270)
            {
                Cv2.Rotate(roi, rotated, RotateFlags.Rotate90Clockwise);
            }
            else
            {
                rotated = roi.Clone();
            }

            return rotated;
        }

        private static int NormalizeAngle(int angle)
        {
            int n = angle % 360;
            if (n < 0)
            {
                n += 360;
            }

            if (n != 0 && n != 90 && n != 180 && n != 270)
            {
                return 0;
            }

            return n;
        }

        private static double[] BuildRightAngleAffine(int srcW, int srcH, int angle, out int dstW, out int dstH)
        {
            angle = NormalizeAngle(angle);
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

        private static double[] MatrixFromAffineMat(Mat affine)
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

        private static double[] InvertAffine2x3(double[] a)
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

        private static double[] ComposeAffine(double[] first, double[] second)
        {
            // result = first * second
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

        private static double[] To3x3(double[] a)
        {
            if (a == null || a.Length != 6)
            {
                return new[] { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 };
            }

            return new[] { a[0], a[1], a[2], a[3], a[4], a[5], 0.0, 0.0, 1.0 };
        }

        private static Point2d ApplyAffine(double[] a, Point2d p)
        {
            return new Point2d(
                a[0] * p.X + a[1] * p.Y + a[2],
                a[3] * p.X + a[4] * p.Y + a[5]);
        }

        private static bool TryMapObjectToFull(CSharpObjectResult obj, double[] normToFullAffine, out CSharpObjectResult mapped)
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

        private static int SelectMainCandidateIndex(List<CSharpObjectResult> mappedObjects, List<int> candidateIndexes)
        {
            int best = candidateIndexes[0];
            for (int i = 1; i < candidateIndexes.Count; i++)
            {
                int idx = candidateIndexes[i];
                if (IsBetterMainCandidate(mappedObjects[idx], mappedObjects[best]))
                {
                    best = idx;
                }
            }
            return best;
        }

        private static bool IsBetterMainCandidate(CSharpObjectResult lhs, CSharpObjectResult rhs)
        {
            double areaL = GetObjectArea(lhs);
            double areaR = GetObjectArea(rhs);
            if (areaL > areaR + 1e-6)
            {
                return true;
            }
            if (Math.Abs(areaL - areaR) <= 1e-6 && lhs.Score > rhs.Score + 1e-6)
            {
                return true;
            }
            return false;
        }

        private static CSharpObjectResult CloneWithCategoryName(CSharpObjectResult source, string categoryName)
        {
            return new CSharpObjectResult(
                source.CategoryId,
                categoryName,
                source.Score,
                source.Area,
                source.Bbox != null ? new List<double>(source.Bbox) : new List<double>(),
                false,
                new Mat(),
                source.WithBbox,
                source.WithAngle,
                source.Angle);
        }

        private string BuildInferenceText(PipelineRunResult runResult, double elapsedMs)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"图片: {imagePath}");
            sb.AppendLine($"模型1: {txtModel1Path.Text.Trim()}");
            sb.AppendLine($"模型2: {txtModel2Path.Text.Trim()}");
            sb.AppendLine($"滑窗参数: {(int)numWindowWidth.Value} x {(int)numWindowHeight.Value}, overlap=({(int)numOverlapX.Value}, {(int)numOverlapY.Value})");
            sb.AppendLine($"滑窗数量: {runResult.SlidingWindowCount}");
            sb.AppendLine($"模型1合并后目标数: {runResult.MergedModel1Count}");
            sb.AppendLine($"最终结果数: {runResult.FinalObjects.Count}");
            sb.AppendLine($"推理耗时: {elapsedMs:F2} ms");
            sb.AppendLine();

            if (runResult.FinalObjects.Count == 0)
            {
                sb.AppendLine("未检测到结果。");
            }
            else
            {
                for (int i = 0; i < runResult.FinalObjects.Count; i++)
                {
                    var obj = runResult.FinalObjects[i];
                    string rectText = BuildRectText(obj);
                    sb.AppendLine($"[{i + 1}] {obj.CategoryName}  score={obj.Score:F2}  {rectText}");
                }
            }

            if (runResult.Logs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("日志:");
                foreach (string log in runResult.Logs)
                {
                    sb.AppendLine("- " + log);
                }
            }

            return sb.ToString();
        }

        private string BuildSpeedTestText(List<double> costsMs, int rounds, PipelineRunResult lastResult)
        {
            double total = costsMs.Sum();
            double avg = costsMs.Average();
            double min = costsMs.Min();
            double max = costsMs.Max();

            var sb = new StringBuilder();
            sb.AppendLine($"测速图片: {imagePath}");
            sb.AppendLine($"测速轮数: {rounds}");
            sb.AppendLine($"总耗时: {total / 1000.0:F2} s");
            sb.AppendLine($"平均耗时: {avg:F1} ms/张");
            sb.AppendLine($"最快耗时: {min:F1} ms");
            sb.AppendLine($"最慢耗时: {max:F1} ms");
            sb.AppendLine("说明: 测速执行完整双模型管线，不覆盖当前图像区显示结果。");

            if (lastResult != null)
            {
                sb.AppendLine();
                sb.AppendLine($"（最后一轮）滑窗数量: {lastResult.SlidingWindowCount}");
                sb.AppendLine($"（最后一轮）模型1合并后目标数: {lastResult.MergedModel1Count}");
                sb.AppendLine($"（最后一轮）最终结果数: {lastResult.FinalObjects.Count}");
            }

            return sb.ToString();
        }

        private static string BuildRectText(CSharpObjectResult obj)
        {
            if (obj.Bbox == null || obj.Bbox.Count < 4)
            {
                return "rect=(N/A)";
            }

            if (obj.WithAngle || obj.Bbox.Count == 5)
            {
                return string.Format(
                    "rbox=(cx={0:F1}, cy={1:F1}, w={2:F1}, h={3:F1}, angle={4:F3})",
                    obj.Bbox[0], obj.Bbox[1], obj.Bbox[2], obj.Bbox[3], obj.Angle);
            }

            return string.Format(
                "rect=({0:F1}, {1:F1}, {2:F1}, {3:F1})",
                obj.Bbox[0], obj.Bbox[1], obj.Bbox[2], obj.Bbox[3]);
        }

        private bool EnsureReadyForPipeline(bool showMessage)
        {
            imagePath = (txtImagePath.Text ?? string.Empty).Trim();

            if (model1 == null)
            {
                if (showMessage)
                {
                    MessageBox.Show("请先加载模型1。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return false;
            }

            if (model2 == null)
            {
                if (showMessage)
                {
                    MessageBox.Show("请先加载模型2。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return false;
            }

            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                if (showMessage)
                {
                    MessageBox.Show("请先选择有效图片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return false;
            }

            return true;
        }

        private static bool TryLoadImageForInfer(string path, out Mat imageBgr, out Mat imageRgb, out string error)
        {
            imageBgr = null;
            imageRgb = null;
            error = string.Empty;
            try
            {
                imageBgr = Cv2.ImRead(path, ImreadModes.Color);
                if (imageBgr == null || imageBgr.Empty())
                {
                    error = "图片解码失败。";
                    return false;
                }

                imageRgb = new Mat();
                Cv2.CvtColor(imageBgr, imageRgb, ColorConversionCodes.BGR2RGB);
                return true;
            }
            catch (Exception ex)
            {
                error = "图片读取失败: " + ex.Message;
                if (imageBgr != null)
                {
                    imageBgr.Dispose();
                    imageBgr = null;
                }
                if (imageRgb != null)
                {
                    imageRgb.Dispose();
                    imageRgb = null;
                }
                return false;
            }
        }

        private static string BrowseFile(string title, string filter, string currentPath)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = title;
                dialog.Filter = filter;
                dialog.RestoreDirectory = true;

                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    try
                    {
                        dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
                        dialog.FileName = Path.GetFileName(currentPath);
                    }
                    catch
                    {
                    }
                }

                return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
            }
        }

        private static void DisposeModel(ref Model model)
        {
            if (model == null)
            {
                return;
            }

            try
            {
                model.Dispose();
            }
            catch
            {
            }
            finally
            {
                model = null;
            }
        }

        private void ReleaseModels()
        {
            DisposeModel(ref model1);
            DisposeModel(ref model2);
        }

        private void RestoreUiSettings()
        {
            txtModel1Path.Text = Properties.Settings.Default.LastModel1Path ?? string.Empty;
            txtModel2Path.Text = Properties.Settings.Default.LastModel2Path ?? string.Empty;
            txtImagePath.Text = Properties.Settings.Default.LastImagePath ?? string.Empty;
            imagePath = txtImagePath.Text;

            SetNumericValue(numWindowWidth, Properties.Settings.Default.SlidingWindowWidth, 2560);
            SetNumericValue(numWindowHeight, Properties.Settings.Default.SlidingWindowHeight, 2560);
            SetNumericValue(numOverlapX, Properties.Settings.Default.SlidingOverlapX, 1024);
            SetNumericValue(numOverlapY, Properties.Settings.Default.SlidingOverlapY, 1024);
        }

        private void SaveUiSettings()
        {
            Properties.Settings.Default.LastModel1Path = (txtModel1Path.Text ?? string.Empty).Trim();
            Properties.Settings.Default.LastModel2Path = (txtModel2Path.Text ?? string.Empty).Trim();
            Properties.Settings.Default.LastImagePath = (txtImagePath.Text ?? string.Empty).Trim();
            Properties.Settings.Default.SlidingWindowWidth = (int)numWindowWidth.Value;
            Properties.Settings.Default.SlidingWindowHeight = (int)numWindowHeight.Value;
            Properties.Settings.Default.SlidingOverlapX = (int)numOverlapX.Value;
            Properties.Settings.Default.SlidingOverlapY = (int)numOverlapY.Value;
            Properties.Settings.Default.Save();
        }

        private static void SetNumericValue(NumericUpDown control, int configuredValue, int fallbackValue)
        {
            int value = configuredValue > 0 ? configuredValue : fallbackValue;
            decimal asDecimal = value;
            if (asDecimal < control.Minimum)
            {
                asDecimal = control.Minimum;
            }
            if (asDecimal > control.Maximum)
            {
                asDecimal = control.Maximum;
            }
            control.Value = asDecimal;
        }
    }
}
