using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
        private Model extractModel;
        private Model componentDetectModel;
        private Model icDetectModel;
        private string imagePath;
        private bool isInferenceRunning;

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

            if (BrowseModelPath(txtExtractModelPath, "选择元件提取模型"))
            {
                LoadModelWithStatus(
                    "当前正在执行推理，暂不能加载元件提取模型。",
                    txtExtractModelPath,
                    "元件提取模型",
                    LoadExtractModel);
            }
        }

        private void btnLoadExtractModel_Click(object sender, EventArgs e)
        {
            LoadModelWithStatus(
                "当前正在执行推理，暂不能加载元件提取模型。",
                txtExtractModelPath,
                "元件提取模型",
                LoadExtractModel);
        }

        private void btnBrowseComponentModel_Click(object sender, EventArgs e)
        {
            if (!TryEnsureIdle("当前正在执行推理，暂不能切换元件检测模型。"))
            {
                return;
            }

            if (BrowseModelPath(txtComponentModelPath, "选择元件检测模型"))
            {
                LoadModelWithStatus(
                    "当前正在执行推理，暂不能加载元件检测模型。",
                    txtComponentModelPath,
                    "元件检测模型",
                    LoadComponentDetectModel);
            }
        }

        private void btnLoadComponentModel_Click(object sender, EventArgs e)
        {
            LoadModelWithStatus(
                "当前正在执行推理，暂不能加载元件检测模型。",
                txtComponentModelPath,
                "元件检测模型",
                LoadComponentDetectModel);
        }

        private void btnBrowseIcModel_Click(object sender, EventArgs e)
        {
            if (!TryEnsureIdle("当前正在执行推理，暂不能切换IC检测模型。"))
            {
                return;
            }

            if (BrowseModelPath(txtIcModelPath, "选择IC检测模型"))
            {
                LoadModelWithStatus(
                    "当前正在执行推理，暂不能加载IC检测模型。",
                    txtIcModelPath,
                    "IC检测模型",
                    LoadIcDetectModel);
            }
        }

        private void btnLoadIcModel_Click(object sender, EventArgs e)
        {
            LoadModelWithStatus(
                "当前正在执行推理，暂不能加载IC检测模型。",
                txtIcModelPath,
                "IC检测模型",
                LoadIcDetectModel);
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
                await RunInferenceAsync(triggeredByImageSelection: true);
            }
        }

        private async void btnInfer_Click(object sender, EventArgs e)
        {
            await RunInferenceAsync(triggeredByImageSelection: false);
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

        private async Task RunInferenceAsync(bool triggeredByImageSelection)
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
                using (InferenceExecutionResult result = await Task.Run(() =>
                {
                    Mat imageBgr = null;
                    Mat imageRgb = null;
                    try
                    {
                        ReportInferenceProgress(progress, 2, "读取图片");

                        imageBgr = Cv2.ImRead(inferImagePath, ImreadModes.Unchanged);
                        if (imageBgr == null || imageBgr.Empty())
                        {
                            throw new InvalidOperationException("图片解码失败。");
                        }
                        imageRgb = PrepareImageForModelInput(imageBgr);

                        Stopwatch sw = Stopwatch.StartNew();
                        // 颜色约定：UI 显示使用 imageBgr；送入 dvst 的整条 pipeline 使用 imageRgb（RGB）。
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
                }))
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

        private PipelineRunResult RunPipeline(Mat fullImageRgb, SlidingWindowConfig config, IProgress<InferenceProgressInfo> progress = null)
        {
            var runResult = new PipelineRunResult();

            ReportInferenceProgress(progress, 8, "生成滑窗");
            List<Rect> windows = SlidingWindowUtils.BuildSlidingWindows(
                fullImageRgb.Width,
                fullImageRgb.Height,
                config != null ? config.WindowWidth : fullImageRgb.Width,
                config != null ? config.WindowHeight : fullImageRgb.Height,
                config != null ? config.OverlapX : 0,
                config != null ? config.OverlapY : 0);
            runResult.SlidingWindowCount = windows.Count;

            ReportInferenceProgress(progress, 12, $"元件提取模型滑窗推理 0/{windows.Count}");
            List<ExtractDetection> extractDetections = InferExtractModelOnWindows(fullImageRgb, windows, progress);
            ReportInferenceProgress(progress, 62, "合并元件提取结果");
            List<ExtractDetection> mergedExtract = ExtractMergeUtils.MergeExtractResults(extractDetections);
            runResult.MergedExtractCount = mergedExtract.Count;

            int roiTotal = mergedExtract.Count;
            int roiCompleted = 0;
            foreach (var target in mergedExtract)
            {
                int startPercent = 70 + (int)Math.Round(25.0 * roiCompleted / Math.Max(1, roiTotal));
                ReportInferenceProgress(progress, startPercent, $"局部模型推理 {roiCompleted}/{roiTotal}");

                string baseName;
                int normalizeAngle;
                DetectionGeometryUtils.ParseCategoryAndAngle(target.ObjectResult.CategoryName, out baseName, out normalizeAngle);
                bool useIcDetectModel = ShouldUseIcDetectModel(baseName);

                using (RoiProcessResult roi = CropAndRotateRoi(fullImageRgb, target, normalizeAngle))
                {
                    if (!roi.IsValid)
                    {
                        runResult.Logs.Add($"跳过目标[{target.ObjectResult.CategoryName}]：{roi.InvalidReason}");
                        continue;
                    }

                    try
                    {
                        List<CSharpObjectResult> mapped = InferDetectionModelAndMapBack(roi, target.ObjectResult, useIcDetectModel);
                        runResult.FinalObjects.AddRange(mapped);
                        int realResultCount = mapped.Count == 1 && ReferenceEquals(mapped[0], target.ObjectResult)
                            ? 0
                            : mapped.Count;
                        if (useIcDetectModel)
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
                        string routeName = useIcDetectModel ? "IC检测模型" : "元件检测模型";
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

        private static bool ShouldUseIcDetectModel(string baseName)
        {
            return string.Equals(baseName, "IC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(baseName, "BGA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(baseName, "座子", StringComparison.OrdinalIgnoreCase)
                || string.Equals(baseName, "开关", StringComparison.OrdinalIgnoreCase)
                || string.Equals(baseName, "晶振", StringComparison.OrdinalIgnoreCase);
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
                        CSharpObjectResult mapped = DetectionGeometryUtils.LiftExtractObjectToFull(obj, window);
                        Rect2d aabb = ExtractMergeUtils.GetAabbFromObject(mapped);
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

        private RoiProcessResult CropAndRotateRoi(Mat fullImageRgb, ExtractDetection target, int normalizeAngle)
        {
            var result = new RoiProcessResult
            {
                IsValid = false,
                InvalidReason = "未知错误"
            };

            Mat roi = null;
            Mat normalized = null;
            double[] fullToCropAffine = null;

            try
            {
                if (DetectionGeometryUtils.TryBuildRotatedCrop(fullImageRgb, target.ObjectResult, 0, out roi, out fullToCropAffine))
                {
                    // 使用旋转裁剪结果
                }
                else
                {
                    Rect roiRect = DetectionGeometryUtils.ClampRectToImage(target.MergeAabb, fullImageRgb.Width, fullImageRgb.Height);
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

                normalized = DetectionGeometryUtils.RotateRoiByRightAngle(roi, normalizeAngle);
                if (normalized == null || normalized.Empty())
                {
                    if (normalized != null)
                    {
                        normalized.Dispose();
                        normalized = null;
                    }

                    result.InvalidReason = "ROI归一化失败";
                    return result;
                }

                int _;
                int __;
                double[] cropToNorm = DetectionGeometryUtils.BuildRightAngleAffine(roi.Width, roi.Height, normalizeAngle, out _, out __);
                double[] normToCrop = DetectionGeometryUtils.InvertAffine2x3(cropToNorm);
                double[] cropToFull = DetectionGeometryUtils.InvertAffine2x3(fullToCropAffine);
                double[] normToFull = DetectionGeometryUtils.ComposeAffine(cropToFull, normToCrop);

                result.IsValid = true;
                result.InvalidReason = string.Empty;
                result.NormalizedRoi = normalized;
                normalized = null;
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
                if (normalized != null)
                {
                    normalized.Dispose();
                }

                if (roi != null)
                {
                    roi.Dispose();
                }
            }
        }

        private List<CSharpObjectResult> InferDetectionModelAndMapBack(RoiProcessResult roiContext, CSharpObjectResult extractFallback, bool useIcDetectModel)
        {
            JObject inferParams = new JObject
            {
                ["with_mask"] = false
            };

            Model activeModel = useIcDetectModel ? icDetectModel : componentDetectModel;
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
                if (DetectionGeometryUtils.TryMapObjectToFull(obj, roiContext.NormToFullAffine, out mapped))
                {
                    mappedObjects.Add(ResolveFinalDetectionObject(mapped, extractFallback));
                }
            }

            if (mappedObjects.Count == 0)
            {
                return new List<CSharpObjectResult> { extractFallback };
            }

            return mappedObjects;
        }

        private static CSharpObjectResult ResolveFinalDetectionObject(CSharpObjectResult mappedObject, CSharpObjectResult extractFallback)
        {
            if (!ShouldMapBackToExtractCategory(mappedObject.CategoryName))
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

        private static bool ShouldMapBackToExtractCategory(string detectionCategoryName)
        {
            string normalized = (detectionCategoryName ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return false;
            }

            // 元件检测模型与 IC 检测模型检测的主体都是"元件"，主体输出类别统一为 `元件`。
            // 为兼容老版本模型，仍然支持 `IC` 作为主体类别；两者在映射语义上等价，都映射回元件提取模型的 category_name。
            // 其他细分类别（焊点/引脚/文字 等）保持原结果不变。
            return string.Equals(normalized, "元件", StringComparison.Ordinal)
                || string.Equals(normalized, "IC", StringComparison.OrdinalIgnoreCase);
        }

        private CSharpResult BuildDisplayResult(List<CSharpObjectResult> finalObjects)
        {
            var sampleResults = new List<CSharpSampleResult>
            {
                new CSharpSampleResult(finalObjects ?? new List<CSharpObjectResult>())
            };
            return new CSharpResult(sampleResults);
        }

        private string BuildInferenceText(PipelineRunResult runResult, double elapsedMs)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"图片: {imagePath}");
            sb.AppendLine($"元件提取模型: {txtExtractModelPath.Text.Trim()}");
            sb.AppendLine($"元件检测模型: {txtComponentModelPath.Text.Trim()}");
            sb.AppendLine($"IC检测模型: {txtIcModelPath.Text.Trim()}");
            sb.AppendLine($"滑窗参数: {(int)numWindowWidth.Value} x {(int)numWindowHeight.Value}, overlap=({(int)numOverlapX.Value}, {(int)numOverlapY.Value})");
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

        private bool BrowseModelPath(TextBox targetTextBox, string dialogTitle)
        {
            var selected = BrowseFile(dialogTitle, ModelFileFilter, targetTextBox.Text);
            if (string.IsNullOrWhiteSpace(selected))
            {
                return false;
            }

            targetTextBox.Text = selected;
            SaveUiSettings();
            return true;
        }

        private void LoadModelWithStatus(string busyMessage, TextBox targetTextBox, string modelDisplayName, Func<bool> loadAction)
        {
            if (!TryEnsureIdle(busyMessage))
            {
                return;
            }

            SaveUiSettings();
            try
            {
                if (loadAction())
                {
                    richTextBox1.Text = string.Format("{0}加载成功:\n{1}", modelDisplayName, targetTextBox.Text.Trim());
                }
            }
            catch (Exception ex)
            {
                richTextBox1.Text = string.Format("{0}加载失败:\n{1}", modelDisplayName, ex.Message);
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

        private SlidingWindowConfig CaptureSlidingWindowConfig()
        {
            return new SlidingWindowConfig
            {
                WindowWidth = (int)numWindowWidth.Value,
                WindowHeight = (int)numWindowHeight.Value,
                OverlapX = (int)numOverlapX.Value,
                OverlapY = (int)numOverlapY.Value
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
            btnLoadExtractModel.Enabled = !isBusy;
            btnBrowseComponentModel.Enabled = !isBusy;
            btnLoadComponentModel.Enabled = !isBusy;
            btnBrowseIcModel.Enabled = !isBusy;
            btnLoadIcModel.Enabled = !isBusy;
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

        private static Mat PrepareImageForModelInput(Mat image)
        {
            if (image == null || image.Empty())
            {
                return image;
            }

            // 调用侧颜色约定：OpenCV 解码语义为 BGR/BGRA，进入 dvst 推理前统一转换为 RGB；
            // 灰度图保持单通道直送。
            int channels = image.Channels();
            if (channels == 1)
            {
                return image.Clone();
            }

            if (channels == 3)
            {
                var rgb = new Mat();
                Cv2.CvtColor(image, rgb, ColorConversionCodes.BGR2RGB);
                return rgb;
            }

            if (channels == 4)
            {
                var rgb = new Mat();
                Cv2.CvtColor(image, rgb, ColorConversionCodes.BGRA2RGB);
                return rgb;
            }

            return image.Clone();
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
