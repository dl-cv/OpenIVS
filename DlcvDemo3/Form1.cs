using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using CSharpObjectResult = dlcv_infer_csharp.Utils.CSharpObjectResult;
using CSharpResult = dlcv_infer_csharp.Utils.CSharpResult;
using CSharpSampleResult = dlcv_infer_csharp.Utils.CSharpSampleResult;

namespace DlcvDemo3
{
    public partial class Form1 : Form
    {
        private const string ModelFileFilter = "AI模型 (*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp)|*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp|所有文件 (*.*)|*.*";
        private const string ImageFileFilter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif|所有文件 (*.*)|*.*";
        private const string UiStateFileName = "DlcvDemo3.ui-state.json";
        private const int FixedCropWidth = 128;
        private const int FixedCropHeight = 192;

        private Model model1;
        private Model model2;
        private string imagePath;
        private bool isInferenceRunning;

        private sealed class UiState
        {
            public string Model1Path { get; set; }
            public string Model2Path { get; set; }
            public string ImagePath { get; set; }
        }

        private sealed class PipelineRunResult
        {
            public CSharpResult DisplayResult { get; set; }
            public int Model1ObjectCount { get; set; }
            public int CropCount { get; set; }
            public int Model2BatchLimit { get; set; }
            public int FinalResultCount { get; set; }
            public List<CSharpObjectResult> FinalObjects { get; } = new List<CSharpObjectResult>();
            public List<string> Logs { get; } = new List<string>();
        }

        private sealed class CenteredCropContext : IDisposable
        {
            public bool IsValid { get; set; }
            public string InvalidReason { get; set; }
            public Mat CropRgb { get; set; }
            public Rect RequestedRect { get; set; }
            public double[] CropToFullAffine { get; set; }

            public static CenteredCropContext Invalid(string reason)
            {
                return new CenteredCropContext
                {
                    IsValid = false,
                    InvalidReason = reason ?? "未知错误"
                };
            }

            public void Dispose()
            {
                if (CropRgb != null)
                {
                    CropRgb.Dispose();
                    CropRgb = null;
                }
            }
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
            UiState state = LoadUiState();
            txtModel1Path.Text = state?.Model1Path ?? string.Empty;
            txtModel2Path.Text = state?.Model2Path ?? string.Empty;
            txtImagePath.Text = state?.ImagePath ?? string.Empty;
            imagePath = txtImagePath.Text;
            richTextBox1.Text = "请先加载模型并选择图片。";

            StringBuilder startupSb = new StringBuilder();
            if (TryLoadModelFromPath(txtModel1Path.Text, ref model1, "模型1", startupSb))
            {
                startupSb.AppendLine("模型1已自动加载。");
            }
            if (TryLoadModelFromPath(txtModel2Path.Text, ref model2, "模型2", startupSb))
            {
                startupSb.AppendLine("模型2已自动加载。");
            }
            if (startupSb.Length > 0)
            {
                richTextBox1.Text = startupSb.ToString().TrimEnd();
            }

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

            SaveUiState();
            ReleaseModels();
            Utils.FreeAllModels();
        }

        private void btnBrowseModel1_Click(object sender, EventArgs e)
        {
            if (!TryEnsureIdle("当前正在推理，暂不能切换模型1。"))
            {
                return;
            }

            string selected = BrowseFile("选择模型1（定位）", ModelFileFilter, txtModel1Path.Text);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                txtModel1Path.Text = selected;
                SaveUiState();
                LoadModelWithStatus(
                    "当前正在推理，暂不能加载模型1。",
                    txtModel1Path,
                    "模型1",
                    LoadModel1);
            }
        }

        private void btnLoadModel1_Click(object sender, EventArgs e)
        {
            LoadModelWithStatus(
                "当前正在推理，暂不能加载模型1。",
                txtModel1Path,
                "模型1",
                LoadModel1);
        }

        private void btnBrowseModel2_Click(object sender, EventArgs e)
        {
            if (!TryEnsureIdle("当前正在推理，暂不能切换模型2。"))
            {
                return;
            }

            string selected = BrowseFile("选择模型2（识别）", ModelFileFilter, txtModel2Path.Text);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                txtModel2Path.Text = selected;
                SaveUiState();
                LoadModelWithStatus(
                    "当前正在推理，暂不能加载模型2。",
                    txtModel2Path,
                    "模型2",
                    LoadModel2);
            }
        }

        private void btnLoadModel2_Click(object sender, EventArgs e)
        {
            LoadModelWithStatus(
                "当前正在推理，暂不能加载模型2。",
                txtModel2Path,
                "模型2",
                LoadModel2);
        }

        private void btnBrowseImage_Click(object sender, EventArgs e)
        {
            string selected = BrowseFile("选择图片文件", ImageFileFilter, txtImagePath.Text);
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            txtImagePath.Text = selected;
            imagePath = selected;
            SaveUiState();
        }

        private async void btnInfer_Click(object sender, EventArgs e)
        {
            await RunInferenceAsync();
        }

        private void btnReleaseModels_Click(object sender, EventArgs e)
        {
            if (!TryEnsureIdle("当前正在推理，暂不能释放模型。"))
            {
                return;
            }

            ReleaseModels();
            richTextBox1.Text = "模型释放完成。";
        }

        private async Task RunInferenceAsync()
        {
            if (isInferenceRunning)
            {
                richTextBox1.Text = "当前正在推理，请稍后再试。";
                return;
            }

            try
            {
                if (!EnsureReadyForPipeline(showMessage: true))
                {
                    return;
                }

                string inferImagePath = imagePath;
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

                        string error;
                        if (!TryLoadImageForInfer(inferImagePath, out imageBgr, out imageRgb, out error))
                        {
                            throw new InvalidOperationException(error);
                        }

                        Stopwatch sw = Stopwatch.StartNew();
                        PipelineRunResult runResult = RunPipeline(imageRgb, progress);
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
                richTextBox1.Text = "执行推理失败:\n" + ex;
                SetInferenceProgress(0, "空闲");
            }
            finally
            {
                isInferenceRunning = false;
                UpdateBusyControlState();
            }
        }

        private PipelineRunResult RunPipeline(Mat fullImageRgb, IProgress<InferenceProgressInfo> progress = null)
        {
            var runResult = new PipelineRunResult();
            var inferParams = new JObject
            {
                ["with_mask"] = false
            };

            List<CenteredCropContext> cropContexts = new List<CenteredCropContext>();
            try
            {
                ReportInferenceProgress(progress, 10, "模型1整图推理");
                CSharpResult model1Result = model1.Infer(fullImageRgb, inferParams);

                List<CSharpObjectResult> model1Objects = new List<CSharpObjectResult>();
                foreach (var obj in ExtractObjects(model1Result))
                {
                    CSharpObjectResult clamped;
                    if (TryClampObjectToImage(obj, fullImageRgb.Width, fullImageRgb.Height, out clamped))
                    {
                        model1Objects.Add(clamped);
                    }
                }
                runResult.Model1ObjectCount = model1Objects.Count;

                ReportInferenceProgress(progress, 22, "按模型1结果在原图裁图");
                foreach (var obj in model1Objects)
                {
                    Point2d center = GetObjectCenter(obj);
                    CenteredCropContext context = BuildCenteredCropContext(
                        fullImageRgb,
                        center,
                        FixedCropWidth,
                        FixedCropHeight);

                    if (context.IsValid)
                    {
                        cropContexts.Add(context);
                    }
                    else
                    {
                        runResult.Logs.Add($"跳过目标[{obj.CategoryName}]：{context.InvalidReason}");
                        context.Dispose();
                    }
                }
                runResult.CropCount = cropContexts.Count;

                if (runResult.Model1ObjectCount == 0)
                {
                    runResult.Logs.Add("模型1未检测到目标。");
                }

                int batchLimit = Math.Max(1, model2.GetMaxBatchSize());
                runResult.Model2BatchLimit = batchLimit;

                int processed = 0;
                int total = cropContexts.Count;
                foreach (List<CenteredCropContext> chunk in SplitIntoChunks(cropContexts, batchLimit))
                {
                    int percent = 30 + (int)Math.Round(55.0 * processed / Math.Max(1, total));
                    ReportInferenceProgress(progress, percent, $"模型2 batch 推理 {processed}/{total}");

                    List<Mat> mats = chunk.Select(x => x.CropRgb).ToList();
                    CSharpResult batchResult;
                    try
                    {
                        batchResult = model2.InferBatch(mats, inferParams);
                    }
                    catch (Exception ex)
                    {
                        runResult.Logs.Add($"模型2 batch 推理失败(从第 {processed + 1} 张开始，共 {chunk.Count} 张)：{ex.Message}");
                        processed += chunk.Count;
                        continue;
                    }

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        List<CSharpObjectResult> localObjects = GetSampleObjects(batchResult, i);
                        foreach (var localObj in localObjects)
                        {
                            CSharpObjectResult mapped;
                            if (!TryMapObjectWithAffine(localObj, chunk[i].CropToFullAffine, out mapped))
                            {
                                continue;
                            }

                            CSharpObjectResult clamped;
                            if (TryClampObjectToImage(mapped, fullImageRgb.Width, fullImageRgb.Height, out clamped))
                            {
                                runResult.FinalObjects.Add(clamped);
                            }
                        }
                    }

                    processed += chunk.Count;
                }

                ReportInferenceProgress(progress, 95, "整理显示结果");
                runResult.DisplayResult = BuildDisplayResult(runResult.FinalObjects);
                runResult.FinalResultCount = runResult.FinalObjects.Count;
                ReportInferenceProgress(progress, 100, "推理完成");
                return runResult;
            }
            finally
            {
                foreach (var context in cropContexts)
                {
                    context.Dispose();
                }
            }
        }

        private static IEnumerable<CSharpObjectResult> ExtractObjects(CSharpResult result)
        {
            if (result.SampleResults == null || result.SampleResults.Count == 0)
            {
                return Enumerable.Empty<CSharpObjectResult>();
            }

            var sample = result.SampleResults[0];
            return sample.Results ?? new List<CSharpObjectResult>();
        }

        private static List<CSharpObjectResult> GetSampleObjects(CSharpResult batchResult, int sampleIndex)
        {
            if (batchResult.SampleResults == null)
            {
                return new List<CSharpObjectResult>();
            }
            if (sampleIndex < 0 || sampleIndex >= batchResult.SampleResults.Count)
            {
                return new List<CSharpObjectResult>();
            }

            return batchResult.SampleResults[sampleIndex].Results ?? new List<CSharpObjectResult>();
        }

        private static IEnumerable<List<CenteredCropContext>> SplitIntoChunks(List<CenteredCropContext> source, int chunkSize)
        {
            if (source == null || source.Count == 0)
            {
                yield break;
            }

            int realChunkSize = Math.Max(1, chunkSize);
            for (int i = 0; i < source.Count; i += realChunkSize)
            {
                int count = Math.Min(realChunkSize, source.Count - i);
                yield return source.GetRange(i, count);
            }
        }

        private static bool TryMapObjectWithAffine(CSharpObjectResult localObj, double[] affine2x3, out CSharpObjectResult mapped)
        {
            mapped = localObj;
            if (localObj.Bbox == null || localObj.Bbox.Count < 4 || affine2x3 == null || affine2x3.Length != 6)
            {
                return false;
            }

            var bbox = new List<double>(localObj.Bbox);
            double dx = affine2x3[2];
            double dy = affine2x3[5];
            bool isRotated = localObj.WithAngle || bbox.Count == 5;

            bbox[0] += dx;
            bbox[1] += dy;

            mapped.Bbox = bbox;
            mapped.WithBbox = true;
            mapped.WithAngle = isRotated || localObj.WithAngle;
            return true;
        }

        private static bool TryClampObjectToImage(CSharpObjectResult obj, int imageWidth, int imageHeight, out CSharpObjectResult clamped)
        {
            clamped = obj;
            if (obj.Bbox == null || obj.Bbox.Count < 4 || imageWidth <= 0 || imageHeight <= 0)
            {
                return false;
            }

            var bbox = new List<double>(obj.Bbox);
            bool isRotated = obj.WithAngle || bbox.Count == 5;
            if (isRotated)
            {
                double cx = bbox[0];
                double cy = bbox[1];
                double w = bbox[2];
                double h = bbox[3];
                if (w <= 0 || h <= 0)
                {
                    return false;
                }

                double left = cx - w / 2.0;
                double right = cx + w / 2.0;
                double top = cy - h / 2.0;
                double bottom = cy + h / 2.0;

                double clampedLeft = Math.Max(0.0, left);
                double clampedTop = Math.Max(0.0, top);
                double clampedRight = Math.Min(imageWidth, right);
                double clampedBottom = Math.Min(imageHeight, bottom);
                if (clampedRight <= clampedLeft || clampedBottom <= clampedTop)
                {
                    return false;
                }

                bbox[0] = (clampedLeft + clampedRight) / 2.0;
                bbox[1] = (clampedTop + clampedBottom) / 2.0;
                bbox[2] = clampedRight - clampedLeft;
                bbox[3] = clampedBottom - clampedTop;
            }
            else
            {
                double x = bbox[0];
                double y = bbox[1];
                double w = bbox[2];
                double h = bbox[3];
                if (w <= 0 || h <= 0)
                {
                    return false;
                }

                double left = Math.Max(0.0, x);
                double top = Math.Max(0.0, y);
                double right = Math.Min(imageWidth, x + w);
                double bottom = Math.Min(imageHeight, y + h);
                if (right <= left || bottom <= top)
                {
                    return false;
                }

                bbox[0] = left;
                bbox[1] = top;
                bbox[2] = right - left;
                bbox[3] = bottom - top;
            }

            clamped.Bbox = bbox;
            clamped.WithBbox = true;
            return true;
        }

        private static Point2d GetObjectCenter(CSharpObjectResult obj)
        {
            if (obj.Bbox == null || obj.Bbox.Count < 4)
            {
                return new Point2d(0, 0);
            }

            bool isRotated = obj.WithAngle || obj.Bbox.Count == 5;
            if (isRotated)
            {
                return new Point2d(obj.Bbox[0], obj.Bbox[1]);
            }

            return new Point2d(obj.Bbox[0] + obj.Bbox[2] / 2.0, obj.Bbox[1] + obj.Bbox[3] / 2.0);
        }

        private static CenteredCropContext BuildCenteredCropContext(Mat fullImageRgb, Point2d center, int cropW, int cropH)
        {
            int requestLeft = (int)Math.Round(center.X - cropW / 2.0);
            int requestTop = (int)Math.Round(center.Y - cropH / 2.0);
            Rect requestedRect = new Rect(requestLeft, requestTop, cropW, cropH);
            Rect imageRect = new Rect(0, 0, fullImageRgb.Width, fullImageRgb.Height);
            Rect srcRect = IntersectRect(requestedRect, imageRect);

            if (srcRect.Width <= 0 || srcRect.Height <= 0)
            {
                return CenteredCropContext.Invalid("裁图完全落在图像外");
            }

            Mat crop = null;
            try
            {
                crop = new Mat(new Size(cropW, cropH), fullImageRgb.Type(), Scalar.Black);
                Rect dstRect = new Rect(srcRect.X - requestLeft, srcRect.Y - requestTop, srcRect.Width, srcRect.Height);

                using (var srcView = new Mat(fullImageRgb, srcRect))
                using (var dstView = new Mat(crop, dstRect))
                {
                    srcView.CopyTo(dstView);
                }

                return new CenteredCropContext
                {
                    IsValid = true,
                    InvalidReason = string.Empty,
                    CropRgb = crop,
                    RequestedRect = requestedRect,
                    CropToFullAffine = new[] { 1.0, 0.0, (double)requestLeft, 0.0, 1.0, (double)requestTop }
                };
            }
            catch (Exception ex)
            {
                if (crop != null)
                {
                    crop.Dispose();
                }
                return CenteredCropContext.Invalid(ex.Message);
            }
        }

        private static Rect IntersectRect(Rect a, Rect b)
        {
            int x1 = Math.Max(a.X, b.X);
            int y1 = Math.Max(a.Y, b.Y);
            int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
            if (x2 <= x1 || y2 <= y1)
            {
                return new Rect(0, 0, 0, 0);
            }
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }

        private static CSharpResult BuildDisplayResult(List<CSharpObjectResult> finalObjects)
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
            sb.AppendLine("图片: " + imagePath);
            sb.AppendLine("模型1: " + (txtModel1Path.Text ?? string.Empty).Trim());
            sb.AppendLine("模型2: " + (txtModel2Path.Text ?? string.Empty).Trim());
            sb.AppendLine($"固定裁图大小: {FixedCropWidth} x {FixedCropHeight}");
            sb.AppendLine("模型1目标数: " + runResult.Model1ObjectCount);
            sb.AppendLine("有效裁图数: " + runResult.CropCount);
            sb.AppendLine("模型2最大Batch: " + runResult.Model2BatchLimit);
            sb.AppendLine("最终结果数: " + runResult.FinalResultCount);
            sb.AppendLine($"推理耗时: {elapsedMs:F2} ms");
            sb.AppendLine();

            if (runResult.FinalObjects.Count == 0)
            {
                sb.AppendLine("未检测到最终结果。");
            }
            else
            {
                for (int i = 0; i < runResult.FinalObjects.Count; i++)
                {
                    var obj = runResult.FinalObjects[i];
                    string rectText = BuildRectText(obj);
                    sb.AppendLine($"[{i + 1}] {obj.CategoryName,-12} score={obj.Score:F2}  {rectText}");
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

            bool isRotated = obj.WithAngle || obj.Bbox.Count == 5;
            if (isRotated)
            {
                return string.Format(
                    "rbox=(cx={0:F1}, cy={1:F1}, w={2:F1}, h={3:F1}, angle={4:F3})",
                    obj.Bbox[0], obj.Bbox[1], obj.Bbox[2], obj.Bbox[3], obj.Angle);
            }

            return string.Format(
                "rect=({0:F1}, {1:F1}, {2:F1}, {3:F1})",
                obj.Bbox[0], obj.Bbox[1], obj.Bbox[2], obj.Bbox[3]);
        }

        private void LoadModelWithStatus(string busyMessage, TextBox targetTextBox, string modelDisplayName, Func<bool> loadAction)
        {
            if (!TryEnsureIdle(busyMessage))
            {
                return;
            }

            try
            {
                if (loadAction())
                {
                    SaveUiState();
                    richTextBox1.Text = $"{modelDisplayName}加载成功:\n{targetTextBox.Text.Trim()}";
                }
            }
            catch (Exception ex)
            {
                richTextBox1.Text = $"{modelDisplayName}加载失败:\n{ex.Message}";
            }
        }

        private bool LoadModel1()
        {
            return TryLoadModelFromPath(
                txtModel1Path.Text,
                ref model1,
                "模型1",
                null,
                showPathInvalidMessage: true);
        }

        private bool LoadModel2()
        {
            return TryLoadModelFromPath(
                txtModel2Path.Text,
                ref model2,
                "模型2",
                null,
                showPathInvalidMessage: true);
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
            btnBrowseModel1.Enabled = !isBusy;
            btnLoadModel1.Enabled = !isBusy;
            btnBrowseModel2.Enabled = !isBusy;
            btnLoadModel2.Enabled = !isBusy;
            btnBrowseImage.Enabled = !isBusy;
            btnInfer.Enabled = !isBusy;
            btnReleaseModels.Enabled = !isBusy;

            txtModel1Path.Enabled = !isBusy;
            txtModel2Path.Enabled = !isBusy;
            txtImagePath.Enabled = !isBusy;
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

        private static bool TryLoadModelFromPath(
            string rawPath,
            ref Model targetModel,
            string modelDisplayName,
            StringBuilder logBuilder = null,
            bool showPathInvalidMessage = false)
        {
            string path = (rawPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                if (showPathInvalidMessage)
                {
                    MessageBox.Show($"请先选择有效的{modelDisplayName}文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (!string.IsNullOrWhiteSpace(path))
                {
                    logBuilder?.AppendLine($"{modelDisplayName}路径不存在，已跳过自动加载：{path}");
                }
                return false;
            }

            try
            {
                DisposeModel(ref targetModel);
                targetModel = new Model(path, 0, false);
                return true;
            }
            catch (Exception ex)
            {
                logBuilder?.AppendLine($"{modelDisplayName}自动加载失败：{ex.Message}");
                if (showPathInvalidMessage)
                {
                    throw;
                }
                return false;
            }
        }

        private UiState LoadUiState()
        {
            try
            {
                string path = GetUiStateFilePath();
                if (!File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                JObject obj = JObject.Parse(json);
                return new UiState
                {
                    Model1Path = (string)obj["model1_path"] ?? string.Empty,
                    Model2Path = (string)obj["model2_path"] ?? string.Empty,
                    ImagePath = (string)obj["image_path"] ?? string.Empty
                };
            }
            catch
            {
                return null;
            }
        }

        private void SaveUiState()
        {
            try
            {
                string path = GetUiStateFilePath();
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                JObject obj = new JObject
                {
                    ["model1_path"] = (txtModel1Path.Text ?? string.Empty).Trim(),
                    ["model2_path"] = (txtModel2Path.Text ?? string.Empty).Trim(),
                    ["image_path"] = (txtImagePath.Text ?? string.Empty).Trim()
                };

                File.WriteAllText(path, obj.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string GetUiStateFilePath()
        {
            return Path.Combine(Application.UserAppDataPath, UiStateFileName);
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
    }
}
