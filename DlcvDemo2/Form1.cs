using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        private const int DefaultComponentPadding = 32;
        private static readonly Regex CategoryAngleRegex = new Regex(@"^(.*?)(0|90|180|270)$", RegexOptions.Compiled);

        private const double MergeIouThreshold = 0.2;
        private const double MergeIosThreshold = 0.2;

        private Model extractModel;
        private Model componentDetectModel;
        private Model icDetectModel;
        private string imagePath;
        private bool isInferenceRunning;

        private sealed class ExtractDetection
        {
            public CSharpObjectResult ObjectResult { get; set; }
            public Rect2d MergeAabb { get; set; }
            public int Order { get; set; }
        }

        private enum DetectionModelRoute
        {
            ComponentDetect = 0,
            IcDetect = 1
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
            public int MergedExtractCount { get; set; }
            public int ComponentModelResultCount { get; set; }
            public int IcModelResultCount { get; set; }
            public List<CSharpObjectResult> FinalObjects { get; } = new List<CSharpObjectResult>();
            public List<string> Logs { get; } = new List<string>();
        }

        private sealed class SlidingWindowConfig
        {
            public int WindowWidth { get; set; }
            public int WindowHeight { get; set; }
            public int OverlapX { get; set; }
            public int OverlapY { get; set; }
            public int ComponentPadding { get; set; }
        }

        private sealed class InferenceProgressInfo
        {
            public int Percent { get; set; }
            public string Stage { get; set; }
        }

        private sealed class InferenceExecutionResult : IDisposable
        {
            public Mat ImageBgr { get; set; }
            public PipelineRunResult RunResult { get; set; }
            public double ElapsedMs { get; set; }

            public void Dispose()
            {
                if (ImageBgr != null)
                {
                    ImageBgr.Dispose();
                    ImageBgr = null;
                }
            }
        }

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            RestoreUiSettings();
            TopMost = false;
            SetInferenceProgress(0, "空闲");
            UpdateBusyControlState();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isInferenceRunning)
            {
                MessageBox.Show("当前正在执行推理，请等待完成后再关闭。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
                return;
            }

            SaveUiSettings();
            ReleaseModels();
            Utils.FreeAllModels();
        }

        private void btnBrowseExtractModel_Click(object sender, EventArgs e)
        {
            if (!TryEnsureIdle("当前正在执行推理，暂不能切换元件提取模型。"))
            {
                return;
            }

            var selected = BrowseFile("选择元件提取模型", ModelFileFilter, txtExtractModelPath.Text);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                txtExtractModelPath.Text = selected;
                SaveUiSettings();

                try
                {
                    if (LoadExtractModel())
                    {
                        richTextBox1.Text = $"元件提取模型加载成功:\n{txtExtractModelPath.Text.Trim()}";
                    }
                }
                catch (Exception ex)
                {
                    richTextBox1.Text = $"元件提取模型加载失败:\n{ex.Message}";
                }
            }
        }

        private void btnBrowseComponentModel_Click(object sender, EventArgs e)
        {
            if (!TryEnsureIdle("当前正在执行推理，暂不能切换元件检测模型。"))
            {
                return;
            }

            var selected = BrowseFile("选择元件检测模型", ModelFileFilter, txtComponentModelPath.Text);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                txtComponentModelPath.Text = selected;
                SaveUiSettings();

                try
                {
                    if (LoadComponentDetectModel())
                    {
                        richTextBox1.Text = $"元件检测模型加载成功:\n{txtComponentModelPath.Text.Trim()}";
                    }
                }
                catch (Exception ex)
                {
                    richTextBox1.Text = $"元件检测模型加载失败:\n{ex.Message}";
                }
            }
        }

        private void btnBrowseIcModel_Click(object sender, EventArgs e)
        {
            if (!TryEnsureIdle("当前正在执行推理，暂不能切换IC检测模型。"))
            {
                return;
            }

            var selected = BrowseFile("选择IC检测模型", ModelFileFilter, txtIcModelPath.Text);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                txtIcModelPath.Text = selected;
                SaveUiSettings();

                try
                {
                    if (LoadIcDetectModel())
                    {
                        richTextBox1.Text = $"IC检测模型加载成功:\n{txtIcModelPath.Text.Trim()}";
                    }
                }
                catch (Exception ex)
                {
                    richTextBox1.Text = $"IC检测模型加载失败:\n{ex.Message}";
                }
            }
        }

        private async void btnBrowseImage_Click(object sender, EventArgs e)
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
                await StartInferenceAsync(triggeredByImageSelection: true);
            }
        }

        private async void btnInfer_Click(object sender, EventArgs e)
        {
            await StartInferenceAsync(triggeredByImageSelection: false);
        }

        private void btnReleaseModels_Click(object sender, EventArgs e)
        {
            if (!TryEnsureIdle("当前正在执行推理，暂不能释放模型。"))
            {
                return;
            }

            ReleaseModels();
            richTextBox1.Text = "模型已释放";
        }

        private async Task StartInferenceAsync(bool triggeredByImageSelection)
        {
            if (isInferenceRunning)
            {
                richTextBox1.Text = triggeredByImageSelection
                    ? "当前正在执行推理，已忽略本次自动推理请求。"
                    : "当前正在执行推理，请稍后再试。";
                return;
            }

            try
            {
                SaveUiSettings();
                if (!EnsureReadyForPipeline(showMessage: true))
                {
                    return;
                }

                string inferImagePath = imagePath;
                SlidingWindowConfig config = CaptureSlidingWindowConfig();

                isInferenceRunning = true;
                UpdateBusyControlState();
                SetInferenceProgress(0, "准备推理");

                var progress = new Progress<InferenceProgressInfo>(UpdateInferenceProgress);
                using (InferenceExecutionResult result = await Task.Run(() => RunInferenceCore(inferImagePath, config, progress)))
                {
                    imagePanel1.UpdateImageAndResult(result.ImageBgr, result.RunResult.DisplayResult);
                    richTextBox1.Text = BuildInferenceText(result.RunResult, result.ElapsedMs);
                }

                SetInferenceProgress(100, "完成");
            }
            catch (Exception ex)
            {
                richTextBox1.Text = $"执行推理失败:\n{ex}";
                SetInferenceProgress(0, "空闲");
            }
            finally
            {
                isInferenceRunning = false;
                UpdateBusyControlState();
            }
        }

        private InferenceExecutionResult RunInferenceCore(string inferImagePath, SlidingWindowConfig config, IProgress<InferenceProgressInfo> progress)
        {
            Mat imageBgr = null;
            Mat imageRgb = null;
            try
            {
                ReportInferenceProgress(progress, 2, "读取图片");

                string error;
                if (!TryLoadImageForInfer(inferImagePath, out imageBgr, out imageRgb, out error))
                {
                    throw new InvalidOperationException(error);
                }

                var sw = Stopwatch.StartNew();
                PipelineRunResult runResult = RunPipeline(imageRgb, config, progress);
                sw.Stop();

                return new InferenceExecutionResult
                {
                    ImageBgr = imageBgr,
                    RunResult = runResult,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds
                };
            }
            catch
            {
                if (imageBgr != null)
                {
                    imageBgr.Dispose();
                    imageBgr = null;
                }
                throw;
            }
            finally
            {
                if (imageRgb != null)
                {
                    imageRgb.Dispose();
                }
            }
        }

        private bool LoadExtractModel()
        {
            string path = (txtExtractModelPath.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("请先选择有效的元件提取模型文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            DisposeModel(ref extractModel);
            extractModel = new Model(path, 0, false);
            SaveUiSettings();
            return true;
        }

        private bool LoadComponentDetectModel()
        {
            string path = (txtComponentModelPath.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("请先选择有效的元件检测模型文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            DisposeModel(ref componentDetectModel);
            componentDetectModel = new Model(path, 0, false);
            SaveUiSettings();
            return true;
        }

        private bool LoadIcDetectModel()
        {
            string path = (txtIcModelPath.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("请先选择有效的IC检测模型文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            DisposeModel(ref icDetectModel);
            icDetectModel = new Model(path, 0, false);
            SaveUiSettings();
            return true;
        }

        private PipelineRunResult RunPipeline(Mat fullImageRgb, SlidingWindowConfig config, IProgress<InferenceProgressInfo> progress = null)
        {
            var runResult = new PipelineRunResult();

            ReportInferenceProgress(progress, 8, "生成滑窗");
            List<Rect> windows = BuildSlidingWindows(fullImageRgb, config);
            runResult.SlidingWindowCount = windows.Count;

            ReportInferenceProgress(progress, 12, $"元件提取模型滑窗推理 0/{windows.Count}");
            List<ExtractDetection> extractDetections = InferExtractModelOnWindows(fullImageRgb, windows, progress);
            ReportInferenceProgress(progress, 62, "合并元件提取结果");
            List<ExtractDetection> mergedExtract = MergeExtractResults(extractDetections);
            runResult.MergedExtractCount = mergedExtract.Count;

            int roiTotal = mergedExtract.Count;
            int roiCompleted = 0;
            foreach (var target in mergedExtract)
            {
                int startPercent = 70 + (int)Math.Round(25.0 * roiCompleted / Math.Max(1, roiTotal));
                ReportInferenceProgress(progress, startPercent, $"局部模型推理 {roiCompleted}/{roiTotal}");

                ParsedCategoryAngle parsed = ParseCategoryAndAngle(target.ObjectResult.CategoryName);
                DetectionModelRoute route = SelectDetectionModel(parsed.BaseName);
                using (RoiProcessResult roi = CropAndRotateRoi(fullImageRgb, target, parsed, config.ComponentPadding))
                {
                    if (!roi.IsValid)
                    {
                        runResult.Logs.Add($"跳过目标[{target.ObjectResult.CategoryName}]：{roi.InvalidReason}");
                        continue;
                    }

                    try
                    {
                        List<CSharpObjectResult> mapped = InferDetectionModelAndMapBack(roi, target.ObjectResult, route);
                        runResult.FinalObjects.AddRange(mapped);
                        int realResultCount = mapped.Count == 1 && ReferenceEquals(mapped[0], target.ObjectResult)
                            ? 0
                            : mapped.Count;
                        if (route == DetectionModelRoute.IcDetect)
                        {
                            runResult.IcModelResultCount += realResultCount;
                        }
                        else
                        {
                            runResult.ComponentModelResultCount += realResultCount;
                        }
                    }
                    catch (Exception ex)
                    {
                        string routeName = route == DetectionModelRoute.IcDetect ? "IC检测模型" : "元件检测模型";
                        runResult.Logs.Add($"目标[{target.ObjectResult.CategoryName}]{routeName}推理失败，保留元件提取结果兜底：{ex.Message}");
                        runResult.FinalObjects.Add(target.ObjectResult);
                    }
                }

                roiCompleted++;
                int finishPercent = 70 + (int)Math.Round(25.0 * roiCompleted / Math.Max(1, roiTotal));
                ReportInferenceProgress(progress, finishPercent, $"局部模型推理 {roiCompleted}/{roiTotal}");
            }

            ReportInferenceProgress(progress, 95, "整理结果");
            runResult.DisplayResult = BuildDisplayResult(runResult.FinalObjects);
            ReportInferenceProgress(progress, 100, "推理完成");
            return runResult;
        }

        private static List<Rect> BuildSlidingWindows(Mat image, SlidingWindowConfig config)
        {
            int configuredW = config != null ? config.WindowWidth : image.Width;
            int configuredH = config != null ? config.WindowHeight : image.Height;
            int overlapX = config != null ? config.OverlapX : 0;
            int overlapY = config != null ? config.OverlapY : 0;

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

        private List<ExtractDetection> InferExtractModelOnWindows(Mat fullImageRgb, List<Rect> windows, IProgress<InferenceProgressInfo> progress)
        {
            var output = new List<ExtractDetection>();
            int order = 0;
            JObject inferParams = new JObject
            {
                ["with_mask"] = false
            };

            int totalWindows = windows != null ? windows.Count : 0;
            if (totalWindows == 0)
            {
                ReportInferenceProgress(progress, 60, "元件提取模型滑窗推理 0/0");
                return output;
            }

            int lastPercent = -1;
            for (int i = 0; i < totalWindows; i++)
            {
                Rect window = windows[i];
                using (var tile = new Mat(fullImageRgb, window).Clone())
                {
                    CSharpResult tileResult = extractModel.Infer(tile, inferParams);
                    if (tileResult.SampleResults == null || tileResult.SampleResults.Count == 0)
                    {
                        continue;
                    }

                    foreach (var obj in tileResult.SampleResults[0].Results)
                    {
                        CSharpObjectResult mapped = LiftExtractObjectToFull(obj, window);
                        Rect2d aabb = GetAabbFromObject(mapped);
                        if (aabb.Width <= 0 || aabb.Height <= 0)
                        {
                            continue;
                        }

                        output.Add(new ExtractDetection
                        {
                            ObjectResult = mapped,
                            MergeAabb = aabb,
                            Order = order++
                        });
                    }
                }

                int percent = 15 + (int)Math.Round(45.0 * (i + 1) / totalWindows);
                if (percent != lastPercent)
                {
                    ReportInferenceProgress(progress, percent, $"元件提取模型滑窗推理 {i + 1}/{totalWindows}");
                    lastPercent = percent;
                }
            }

            return output;
        }

        private List<ExtractDetection> MergeExtractResults(List<ExtractDetection> fullImageDetections)
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

        private static ParsedCategoryAngle ParseCategoryAndAngle(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return new ParsedCategoryAngle
                {
                    BaseName = string.Empty,
                    Angle = 0
                };
            }

            Match match = CategoryAngleRegex.Match(categoryName.Trim());
            if (!match.Success)
            {
                return new ParsedCategoryAngle
                {
                    BaseName = categoryName.Trim(),
                    Angle = 0
                };
            }

            int angle;
            if (!int.TryParse(match.Groups[2].Value, out angle))
            {
                angle = 0;
            }

            string baseName = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = categoryName.Trim();
            }

            return new ParsedCategoryAngle
            {
                BaseName = baseName,
                Angle = NormalizeRightAngle(angle)
            };
        }

        private static int NormalizeRightAngle(int angle)
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

        private static DetectionModelRoute SelectDetectionModel(string baseName)
        {
            string normalized = (baseName ?? string.Empty).Trim();
            if (string.Equals(normalized, "IC", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "BGA", StringComparison.OrdinalIgnoreCase))
            {
                return DetectionModelRoute.IcDetect;
            }

            return DetectionModelRoute.ComponentDetect;
        }

        private static Rect2d ExpandRect(Rect2d rect, int padding, int imageWidth, int imageHeight)
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

        private RoiProcessResult CropAndRotateRoi(Mat fullImageRgb, ExtractDetection target, ParsedCategoryAngle parsedCategory, int padding)
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
                if (TryBuildRotatedCrop(fullImageRgb, target.ObjectResult, padding, out roi, out fullToCropAffine))
                {
                    // 使用旋转裁剪结果
                }
                else
                {
                    Rect2d expandedRect = ExpandRect(target.MergeAabb, padding, fullImageRgb.Width, fullImageRgb.Height);
                    Rect roiRect = ClampRectToImage(expandedRect, fullImageRgb.Width, fullImageRgb.Height);
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

                int normalizeAngle = NormalizeRightAngle(parsedCategory.Angle);
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

        private List<CSharpObjectResult> InferDetectionModelAndMapBack(RoiProcessResult roiContext, CSharpObjectResult extractFallback, DetectionModelRoute route)
        {
            JObject inferParams = new JObject
            {
                ["with_mask"] = false
            };

            Model activeModel = route == DetectionModelRoute.IcDetect ? icDetectModel : componentDetectModel;
            CSharpResult roiResult = activeModel.Infer(roiContext.NormalizedRoi, inferParams);
            if (roiResult.SampleResults == null || roiResult.SampleResults.Count == 0)
            {
                return new List<CSharpObjectResult> { extractFallback };
            }

            List<CSharpObjectResult> rawObjects = roiResult.SampleResults[0].Results ?? new List<CSharpObjectResult>();
            if (rawObjects.Count == 0)
            {
                return new List<CSharpObjectResult> { extractFallback };
            }

            var mappedObjects = new List<CSharpObjectResult>();
            foreach (var obj in rawObjects)
            {
                CSharpObjectResult mapped;
                if (TryMapObjectToFull(obj, roiContext.NormToFullAffine, out mapped))
                {
                    mappedObjects.Add(ResolveFinalDetectionObject(mapped, extractFallback, route));
                }
            }

            if (mappedObjects.Count == 0)
            {
                return new List<CSharpObjectResult> { extractFallback };
            }

            return mappedObjects;
        }

        private static CSharpObjectResult ResolveFinalDetectionObject(CSharpObjectResult mappedObject, CSharpObjectResult extractFallback, DetectionModelRoute route)
        {
            if (!ShouldMapBackToExtractCategory(mappedObject.CategoryName, route))
            {
                return mappedObject;
            }

            return new CSharpObjectResult(
                extractFallback.CategoryId,
                extractFallback.CategoryName,
                mappedObject.Score,
                mappedObject.Area,
                mappedObject.Bbox,
                mappedObject.WithMask,
                mappedObject.Mask,
                mappedObject.WithBbox,
                mappedObject.WithAngle,
                mappedObject.Angle);
        }

        private static bool ShouldMapBackToExtractCategory(string detectionCategoryName, DetectionModelRoute route)
        {
            string normalized = (detectionCategoryName ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return false;
            }

            if (route == DetectionModelRoute.IcDetect)
            {
                return string.Equals(normalized, "IC", StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(normalized, "元件", StringComparison.Ordinal);
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

        private static CSharpObjectResult LiftExtractObjectToFull(CSharpObjectResult localObject, Rect windowRect)
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

        private static bool TryBuildRotatedCrop(Mat fullImage, CSharpObjectResult obj, int padding, out Mat roi, out double[] fullToCropAffine)
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

        private static double[] BuildRightAngleAffine(int srcW, int srcH, int angle, out int dstW, out int dstH)
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

        private string BuildInferenceText(PipelineRunResult runResult, double elapsedMs)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"图片: {imagePath}");
            sb.AppendLine($"元件提取模型: {txtExtractModelPath.Text.Trim()}");
            sb.AppendLine($"元件检测模型: {txtComponentModelPath.Text.Trim()}");
            sb.AppendLine($"IC检测模型: {txtIcModelPath.Text.Trim()}");
            sb.AppendLine($"滑窗参数: {(int)numWindowWidth.Value} x {(int)numWindowHeight.Value}, overlap=({(int)numOverlapX.Value}, {(int)numOverlapY.Value})");
            sb.AppendLine($"元件外扩: {(int)numComponentPadding.Value}");
            sb.AppendLine($"滑窗数量: {runResult.SlidingWindowCount}");
            sb.AppendLine($"元件提取模型合并后目标数: {runResult.MergedExtractCount}");
            sb.AppendLine($"元件检测模型结果数: {runResult.ComponentModelResultCount}");
            sb.AppendLine($"IC检测模型结果数: {runResult.IcModelResultCount}");
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

        private SlidingWindowConfig CaptureSlidingWindowConfig()
        {
            return new SlidingWindowConfig
            {
                WindowWidth = (int)numWindowWidth.Value,
                WindowHeight = (int)numWindowHeight.Value,
                OverlapX = (int)numOverlapX.Value,
                OverlapY = (int)numOverlapY.Value,
                ComponentPadding = (int)numComponentPadding.Value
            };
        }

        private bool TryEnsureIdle(string busyMessage)
        {
            if (isInferenceRunning)
            {
                richTextBox1.Text = busyMessage;
                return false;
            }
            return true;
        }

        private void UpdateBusyControlState()
        {
            bool isBusy = isInferenceRunning;
            btnBrowseExtractModel.Enabled = !isBusy;
            btnBrowseComponentModel.Enabled = !isBusy;
            btnBrowseIcModel.Enabled = !isBusy;
            btnBrowseImage.Enabled = !isBusy;
            btnInfer.Enabled = !isBusy;
            btnReleaseModels.Enabled = !isBusy;

            txtExtractModelPath.Enabled = !isBusy;
            txtComponentModelPath.Enabled = !isBusy;
            txtIcModelPath.Enabled = !isBusy;
            txtImagePath.Enabled = !isBusy;
            numWindowWidth.Enabled = !isBusy;
            numWindowHeight.Enabled = !isBusy;
            numOverlapX.Enabled = !isBusy;
            numOverlapY.Enabled = !isBusy;
            numComponentPadding.Enabled = !isBusy;
        }

        private void UpdateInferenceProgress(InferenceProgressInfo info)
        {
            if (info == null)
            {
                return;
            }

            int percent = ClampProgressPercent(info.Percent);
            progressBarInference.Value = percent;
            lblInferenceProgress.Text = string.IsNullOrWhiteSpace(info.Stage)
                ? $"{percent}%"
                : $"{percent}% {info.Stage}";
        }

        private void SetInferenceProgress(int percent, string stage)
        {
            UpdateInferenceProgress(new InferenceProgressInfo
            {
                Percent = percent,
                Stage = stage ?? string.Empty
            });
        }

        private static void ReportInferenceProgress(IProgress<InferenceProgressInfo> progress, int percent, string stage)
        {
            if (progress == null)
            {
                return;
            }

            progress.Report(new InferenceProgressInfo
            {
                Percent = ClampProgressPercent(percent),
                Stage = stage ?? string.Empty
            });
        }

        private static int ClampProgressPercent(int percent)
        {
            if (percent < 0)
            {
                return 0;
            }
            if (percent > 100)
            {
                return 100;
            }
            return percent;
        }

        private bool EnsureReadyForPipeline(bool showMessage)
        {
            imagePath = (txtImagePath.Text ?? string.Empty).Trim();

            if (extractModel == null)
            {
                if (showMessage)
                {
                    MessageBox.Show("请先加载元件提取模型。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return false;
            }

            if (componentDetectModel == null)
            {
                if (showMessage)
                {
                    MessageBox.Show("请先加载元件检测模型。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return false;
            }

            if (icDetectModel == null)
            {
                if (showMessage)
                {
                    MessageBox.Show("请先加载IC检测模型。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            DisposeModel(ref extractModel);
            DisposeModel(ref componentDetectModel);
            DisposeModel(ref icDetectModel);
        }

        private void RestoreUiSettings()
        {
            txtExtractModelPath.Text = Properties.Settings.Default.LastExtractModelPath ?? string.Empty;
            txtComponentModelPath.Text = Properties.Settings.Default.LastComponentDetectModelPath ?? string.Empty;
            txtIcModelPath.Text = Properties.Settings.Default.LastIcDetectModelPath ?? string.Empty;
            txtImagePath.Text = Properties.Settings.Default.LastImagePath ?? string.Empty;
            imagePath = txtImagePath.Text;

            SetNumericValue(numWindowWidth, Properties.Settings.Default.SlidingWindowWidth, 2560);
            SetNumericValue(numWindowHeight, Properties.Settings.Default.SlidingWindowHeight, 2560);
            SetNumericValue(numOverlapX, Properties.Settings.Default.SlidingOverlapX, 1024);
            SetNumericValue(numOverlapY, Properties.Settings.Default.SlidingOverlapY, 1024);
            SetNumericValue(numComponentPadding, Properties.Settings.Default.ComponentPadding, DefaultComponentPadding);
        }

        private void SaveUiSettings()
        {
            Properties.Settings.Default.LastExtractModelPath = (txtExtractModelPath.Text ?? string.Empty).Trim();
            Properties.Settings.Default.LastComponentDetectModelPath = (txtComponentModelPath.Text ?? string.Empty).Trim();
            Properties.Settings.Default.LastIcDetectModelPath = (txtIcModelPath.Text ?? string.Empty).Trim();
            Properties.Settings.Default.LastImagePath = (txtImagePath.Text ?? string.Empty).Trim();
            Properties.Settings.Default.SlidingWindowWidth = (int)numWindowWidth.Value;
            Properties.Settings.Default.SlidingWindowHeight = (int)numWindowHeight.Value;
            Properties.Settings.Default.SlidingOverlapX = (int)numOverlapX.Value;
            Properties.Settings.Default.SlidingOverlapY = (int)numOverlapY.Value;
            Properties.Settings.Default.ComponentPadding = (int)numComponentPadding.Value;
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
