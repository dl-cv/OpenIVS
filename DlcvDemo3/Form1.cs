using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DLCV;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using CSharpObjectResult = dlcv_infer_csharp.Utils.CSharpObjectResult;

namespace DlcvDemo3
{
    public partial class Form1 : Form
    {
        private const string ModelFileFilter = "AI模型 (*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp)|*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp|所有文件 (*.*)|*.*";
        private const string ImageFileFilter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif|所有文件 (*.*)|*.*";
        private const string UiStateFileName = "DlcvDemo3.ui-state.json";
        private const int DefaultModel2ThreadCount = 4;

        private Model model1;
        private Model model2;
        private string imagePath;
        private bool isInferenceRunning;

        private PressureTestRunner pressureTestRunner;
        private System.Windows.Forms.Timer speedTestUpdateTimer;
        private Mat speedTestImageRgb;
        private bool isSpeedTestRunning;
        private volatile bool speedTestStopRequested;
        private int speedTestModel2ThreadCount;

        private sealed class UiState
        {
            public string Model1Path { get; set; }
            public string Model2Path { get; set; }
            public string ImagePath { get; set; }
            public int Model2ThreadCount { get; set; } = DefaultModel2ThreadCount;
        }

        private sealed class InferenceExecutionResult : IDisposable
        {
            public Mat ImageBgr { get; set; }
            public Demo3Pipeline.PipelineRunResult RunResult { get; set; }
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
            numModel2Threads.Value = GetNormalizedModel2ThreadCount(state?.Model2ThreadCount ?? DefaultModel2ThreadCount);
            imagePath = txtImagePath.Text;
            richTextBox1.Text = "请先加载模型并选择图片。";

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

            if (isSpeedTestRunning)
            {
                MessageBox.Show("当前正在进行速度测试，请先点击「停止」结束测试后再关闭。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        private void numModel2Threads_ValueChanged(object sender, EventArgs e)
        {
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

            StopSpeedTestIfRunning();
            ReleaseModels();
            richTextBox1.Text = "模型释放完成。";
        }

        private void btnSpeedTest_Click(object sender, EventArgs e)
        {
            if (pressureTestRunner != null && pressureTestRunner.IsRunning)
            {
                StopSpeedTest();
                return;
            }

            StartSpeedTest();
        }

        private void StartSpeedTest()
        {
            try
            {
                if (!EnsureReadyForPipeline(showMessage: true))
                {
                    return;
                }

                string path = (txtImagePath.Text ?? string.Empty).Trim();
                using (Mat imageBgr = Cv2.ImRead(path, ImreadModes.Color))
                {
                    if (imageBgr == null || imageBgr.Empty())
                    {
                        MessageBox.Show("图像解码失败或文件无效。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    Mat newRgb = new Mat();
                    Cv2.CvtColor(imageBgr, newRgb, ColorConversionCodes.BGR2RGB);
                    DisposeSpeedTestImage();
                    speedTestImageRgb = newRgb;
                }

                isSpeedTestRunning = true;
                speedTestStopRequested = false;
                speedTestModel2ThreadCount = GetConfiguredModel2ThreadCount();
                UpdateBusyControlState();

                pressureTestRunner = new PressureTestRunner(1, 1000000, 1);
                pressureTestRunner.SetFlowModelTiming(false);
                pressureTestRunner.SetTestAction(Demo3PipelineSpeedAction, speedTestImageRgb);

                if (speedTestUpdateTimer == null)
                {
                    speedTestUpdateTimer = new System.Windows.Forms.Timer();
                    speedTestUpdateTimer.Interval = 500;
                    speedTestUpdateTimer.Tick += SpeedTestUpdateTimer_Tick;
                }

                speedTestUpdateTimer.Start();
                pressureTestRunner.Start();
                btnSpeedTest.Text = "停止";
            }
            catch (Exception ex)
            {
                isSpeedTestRunning = false;
                DisposeSpeedTestImage();
                pressureTestRunner = null;
                UpdateBusyControlState();
                btnSpeedTest.Text = "速度测试";
                MessageBox.Show("启动速度测试失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopSpeedTest()
        {
            string finalStats = null;
            if (pressureTestRunner != null)
            {
                pressureTestRunner.Stop();
                finalStats = pressureTestRunner.GetStatistics(false);
                pressureTestRunner = null;
            }

            if (speedTestUpdateTimer != null)
            {
                speedTestUpdateTimer.Stop();
            }

            DisposeSpeedTestImage();
            isSpeedTestRunning = false;
            speedTestStopRequested = false;
            btnSpeedTest.Text = "速度测试";
            UpdateBusyControlState();

            if (!string.IsNullOrEmpty(finalStats))
            {
                richTextBox1.Text = finalStats;
            }
        }

        private void StopSpeedTestIfRunning()
        {
            if (isSpeedTestRunning || (pressureTestRunner != null && pressureTestRunner.IsRunning))
            {
                StopSpeedTest();
            }
            else
            {
                DisposeSpeedTestImage();
            }
        }

        private void DisposeSpeedTestImage()
        {
            if (speedTestImageRgb != null)
            {
                speedTestImageRgb.Dispose();
                speedTestImageRgb = null;
            }
        }

        private void Demo3PipelineSpeedAction(object parameter)
        {
            if (speedTestStopRequested)
            {
                throw new OperationCanceledException();
            }

            Mat rgb = parameter as Mat;
            if (rgb == null || rgb.Empty())
            {
                return;
            }

            try
            {
                Demo3Pipeline.Run(rgb, model1, model2, speedTestModel2ThreadCount, progress: null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("速度测试推理出错: " + ex.Message);
                speedTestStopRequested = true;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        StopSpeedTest();
                        MessageBox.Show("速度测试过程中发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        richTextBox1.Text = "推理错误: " + ex.Message;
                    });
                }
                catch
                {
                }

                throw;
            }
        }

        private void SpeedTestUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (pressureTestRunner == null || !pressureTestRunner.IsRunning)
            {
                return;
            }

            string stats = pressureTestRunner.GetStatistics(false);
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { richTextBox1.Text = stats; });
            }
            else
            {
                richTextBox1.Text = stats;
            }
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
                int model2ThreadCount = GetConfiguredModel2ThreadCount();
                isInferenceRunning = true;
                UpdateBusyControlState();
                SetInferenceProgress(0, "准备推理");

                var progress = new Progress<Demo3Pipeline.InferenceProgressInfo>(UpdateInferenceProgress);
                using (InferenceExecutionResult result = await Task.Run(() =>
                {
                    Mat imageBgr = null;
                    Mat imageRgb = null;
                    try
                    {
                        Demo3Pipeline.ReportProgress(progress, 2, "读取图片");

                        string error;
                        if (!TryLoadImageForInfer(inferImagePath, out imageBgr, out imageRgb, out error))
                        {
                            throw new InvalidOperationException(error);
                        }

                        Stopwatch sw = Stopwatch.StartNew();
                        Demo3Pipeline.PipelineRunResult runResult = RunPipeline(imageRgb, model2ThreadCount, progress);
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

        private Demo3Pipeline.PipelineRunResult RunPipeline(
            Mat fullImageRgb,
            int requestedThreadCount,
            IProgress<Demo3Pipeline.InferenceProgressInfo> progress = null)
        {
            return Demo3Pipeline.Run(fullImageRgb, model1, model2, requestedThreadCount, progress);
        }

        private string BuildInferenceText(Demo3Pipeline.PipelineRunResult runResult, double elapsedMs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("图片: " + imagePath);
            sb.AppendLine("模型1: " + (txtModel1Path.Text ?? string.Empty).Trim());
            sb.AppendLine("模型2: " + (txtModel2Path.Text ?? string.Empty).Trim());
            sb.AppendLine($"固定裁图大小: {Demo3Pipeline.FixedCropWidth} x {Demo3Pipeline.FixedCropHeight}");
            sb.AppendLine("模型1目标数: " + runResult.Model1ObjectCount);
            sb.AppendLine("有效裁图数: " + runResult.CropCount);
            sb.AppendLine("模型2最大Batch: " + runResult.Model2BatchLimit);
            sb.AppendLine("模型2线程数: " + runResult.Model2ThreadCount);
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
                    var sb = new StringBuilder();
                    sb.AppendLine($"{modelDisplayName}加载成功:");
                    sb.AppendLine(targetTextBox.Text.Trim());
                    sb.AppendLine();
                    sb.AppendLine(BuildModelLoadReport(modelDisplayName, modelDisplayName == "模型1" ? model1 : model2, targetTextBox.Text));
                    richTextBox1.Text = sb.ToString().TrimEnd();
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

        private static string BuildModelLoadReport(string modelDisplayName, Model model, string modelPathForFallback)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{modelDisplayName}信息:");

            if (model == null)
            {
                sb.AppendLine("- 模型对象为空");
                return sb.ToString().TrimEnd();
            }

            int resolvedBatch = 1;
            try
            {
                resolvedBatch = Math.Max(1, model.GetMaxBatchSize());
            }
            catch
            {
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool hasShape = false;

            try
            {
                List<JObject> subModels = model.GetResolvedSubModelBatchItems();
                if (subModels != null)
                {
                    foreach (JObject item in subModels)
                    {
                        if (item == null)
                        {
                            continue;
                        }

                        string name = NormalizeModelName(
                            (string)item["name"]
                            ?? (string)item["model_name"]
                            ?? (string)item["model_path_original"]
                            ?? (string)item["model_path"]);
                        JArray shape = item["max_shape"] as JArray;
                        if (shape == null || shape.Count == 0)
                        {
                            continue;
                        }

                        string key = name + "|" + FormatMaxShape(shape);
                        if (seen.Add(key))
                        {
                            sb.AppendLine($"- {name}： {FormatMaxShape(shape)}");
                            hasShape = true;
                        }
                    }
                }
            }
            catch
            {
            }

            if (!hasShape)
            {
                try
                {
                    JArray cached = model.GetCachedMaxShape();
                    if (cached != null && cached.Count > 0)
                    {
                        string fallbackName = NormalizeModelName(modelPathForFallback);
                        string key = fallbackName + "|" + FormatMaxShape(cached);
                        if (seen.Add(key))
                        {
                            sb.AppendLine($"- {fallbackName}： {FormatMaxShape(cached)}");
                            hasShape = true;
                        }
                    }
                }
                catch
                {
                }
            }

            if (!hasShape)
            {
                sb.AppendLine("- 无模型形状信息");
            }

            sb.AppendLine("- BatchSize: " + resolvedBatch);
            return sb.ToString().TrimEnd();
        }

        private static string NormalizeModelName(string rawNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(rawNameOrPath))
            {
                return "未命名模型";
            }

            string text = rawNameOrPath.Trim();
            try
            {
                string fileName = Path.GetFileName(text);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName;
                }
            }
            catch
            {
            }

            return text;
        }

        private static string FormatMaxShape(JArray shape)
        {
            if (shape == null || shape.Count == 0)
            {
                return "[]";
            }

            var values = shape.Select(t => t != null ? t.ToString() : "null");
            return "[" + string.Join(", ", values) + "]";
        }

        private bool TryEnsureIdle(string busyMessage)
        {
            if (isInferenceRunning)
            {
                richTextBox1.Text = busyMessage;
                return false;
            }

            if (isSpeedTestRunning)
            {
                richTextBox1.Text = "当前正在进行速度测试，请先停止速度测试。";
                return false;
            }

            return true;
        }

        private void UpdateBusyControlState()
        {
            bool isBusy = isInferenceRunning || isSpeedTestRunning;
            btnBrowseModel1.Enabled = !isBusy;
            btnLoadModel1.Enabled = !isBusy;
            btnBrowseModel2.Enabled = !isBusy;
            btnLoadModel2.Enabled = !isBusy;
            btnBrowseImage.Enabled = !isBusy;
            btnInfer.Enabled = !isInferenceRunning && !isSpeedTestRunning;
            btnSpeedTest.Enabled = !isInferenceRunning;
            btnReleaseModels.Enabled = !isBusy;

            txtModel1Path.Enabled = !isBusy;
            txtModel2Path.Enabled = !isBusy;
            txtImagePath.Enabled = !isBusy;
            numModel2Threads.Enabled = !isBusy;
        }

        private void UpdateInferenceProgress(Demo3Pipeline.InferenceProgressInfo info)
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
            UpdateInferenceProgress(new Demo3Pipeline.InferenceProgressInfo
            {
                Percent = percent,
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

        private int GetConfiguredModel2ThreadCount()
        {
            return GetNormalizedModel2ThreadCount((int)numModel2Threads.Value);
        }

        private static int GetNormalizedModel2ThreadCount(int requested)
        {
            return Demo3Pipeline.NormalizeThreadCount(requested);
        }

        private static bool TryLoadImageForInfer(string path, out Mat imageBgr, out Mat imageRgb, out string error)
        {
            imageBgr = null;
            imageRgb = null;
            error = string.Empty;
            try
            {
                imageBgr = Cv2.ImRead(path, ImreadModes.Unchanged);
                if (imageBgr == null || imageBgr.Empty())
                {
                    error = "图片解码失败。";
                    return false;
                }

                imageRgb = PrepareImageForModelInput(imageBgr);
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

        private static Mat PrepareImageForModelInput(Mat image)
        {
            if (image == null || image.Empty())
            {
                return image;
            }

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
                    ImagePath = (string)obj["image_path"] ?? string.Empty,
                    Model2ThreadCount = GetNormalizedModel2ThreadCount((int?)obj["model2_thread_count"] ?? DefaultModel2ThreadCount)
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
                    ["image_path"] = (txtImagePath.Text ?? string.Empty).Trim(),
                    ["model2_thread_count"] = GetConfiguredModel2ThreadCount()
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
