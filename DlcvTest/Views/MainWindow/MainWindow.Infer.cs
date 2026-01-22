using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using dlcv_infer_csharp;
using OpenCvSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DlcvTest.Properties;
using DlcvTest.WPFViewer;

namespace DlcvTest
{
    public partial class MainWindow
    {
        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "UnknownModel";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (invalid.Contains(ch))
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(ch);
                }
            }

            var cleaned = sb.ToString().Trim();
            cleaned = cleaned.TrimEnd(' ', '.');
            if (string.IsNullOrWhiteSpace(cleaned)) return "UnknownModel";
            return cleaned;
        }

        /// <summary>
        /// 计算文件相对于基准目录的相对路径，并组合到输出目录，同时确保目标目录存在。
        /// </summary>
        /// <param name="basePath">输入根目录（用于计算相对路径）</param>
        /// <param name="filePath">源文件完整路径</param>
        /// <param name="outputDir">输出目录</param>
        /// <param name="newExtension">可选的新扩展名（如 ".png"）</param>
        /// <returns>完整的输出文件路径</returns>
        private static string GetRelativeOutputPath(string basePath, string filePath, string outputDir, string newExtension = null)
        {
            // 计算相对路径（兼容 .NET Framework 4.7.2，手动实现）
            string relativePath;
            try
            {
                var baseUri = new Uri(EnsureTrailingSlash(Path.GetFullPath(basePath)));
                var fileUri = new Uri(Path.GetFullPath(filePath));
                var relativeUri = baseUri.MakeRelativeUri(fileUri);
                relativePath = Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                // 如果计算相对路径失败，回退到只使用文件名
                relativePath = Path.GetFileName(filePath);
            }

            // 组合输出路径
            string outputPath = Path.Combine(outputDir, relativePath);

            // 更改扩展名（如果需要）
            if (!string.IsNullOrEmpty(newExtension))
            {
                outputPath = Path.ChangeExtension(outputPath, newExtension);
            }

            // 确保目录存在
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return outputPath;
        }

        /// <summary>
        /// 确保路径以目录分隔符结尾（用于 Uri 相对路径计算）
        /// </summary>
        private static string EnsureTrailingSlash(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()) && !path.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                return path + Path.DirectorySeparatorChar;
            }
            return path;
        }

        private static string CreateUniqueDirectoryPath(string outputRoot, string baseName)
        {
            string basePath = Path.Combine(outputRoot, baseName);
            if (!Directory.Exists(basePath)) return basePath;

            for (int i = 1; i <= 999; i++)
            {
                string candidate = Path.Combine(outputRoot, baseName + "_" + i);
                if (!Directory.Exists(candidate)) return candidate;
            }

            return Path.Combine(outputRoot, baseName + "_" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
        }

        private async Task RunBatchInferJsonAsync()
        {
            if (model == null)
            {
                MessageBox.Show("请先加载模型！");
                return;
            }

            // 批量输入源：仅使用 DataPath（txtDataPath），不再从 TreeView 推断目录
            string folderPath = txtDataPath != null ? (txtDataPath.Text ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                MessageBox.Show("请先选择有效的图片文件夹！");
                return;
            }

            bool batchStarted = false;
            try
            {
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };
                var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (files.Length == 0)
                {
                    MessageBox.Show("文件夹中没有图片！");
                    return;
                }

                BeginBatchProgress(files.Length);
                batchStarted = true;

            // 读取推理参数（UI线程）
            double threshold = 0.5;
                double iouThreshold = 0.2;
                bool showMask = true;
                bool showContours = true;
                bool saveOriginal = false;
                bool saveVisualization = false;
                try
                {
                    // 置信度
                    if (ConfidenceVal != null && double.TryParse(ConfidenceVal.Text, out double confVal))
                    {
                        threshold = confVal;
                    }
                    // IOU
                    if (IOUVal != null && double.TryParse(IOUVal.Text, out double iouVal))
                    {
                        iouThreshold = iouVal;
                    }
                    threshold = Clamp01(threshold);
                    iouThreshold = Clamp01(iouThreshold);

                    // 是否显示 mask/边缘
                    try { showMask = Settings.Default.ShowMaskPane; } catch { showMask = true; }
                    try { showContours = Settings.Default.ShowContours; } catch { showContours = true; }
                    try { saveOriginal = Settings.Default.SaveOriginal; } catch { saveOriginal = false; }
                    try { saveVisualization = Settings.Default.SaveVisualization; } catch { saveVisualization = false; }
                }
                catch
                {
                    threshold = 0.5;
                    iouThreshold = 0.2;
                    showMask = true;
                    showContours = true;
                    saveOriginal = false;
                    saveVisualization = false;
                }

                // 是否请求mask：显示mask 或 显示边缘 都需要 mask 数据
                bool withMask = showMask || showContours;

                // 构造推理参数（与 DlcvDemo 保持一致，不传递 iou_threshold）
                var inferenceParams = new JObject();
                inferenceParams["threshold"] = (float)threshold;
                inferenceParams["with_mask"] = withMask;
                EnsureDvpParamsMirror(inferenceParams);

                int processed = 0;
                int skipped = 0;
                int failed = 0;
                int exported = 0;

                // 输出根目录：优先使用设置中的 OutputDirectory；为空则回退到输入文件夹下的“导出”
                string outputRoot = null;
                try { outputRoot = (Settings.Default.OutputDirectory ?? string.Empty).Trim(); } catch { outputRoot = string.Empty; }
                if (string.IsNullOrWhiteSpace(outputRoot))
                {
                    outputRoot = Path.Combine(folderPath, "导出");
                }

                try
                {
                    if (!Directory.Exists(outputRoot)) Directory.CreateDirectory(outputRoot);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("无法创建输出目录: " + outputRoot + "\n" + ex.Message);
                    return;
                }

                string modelPath = null;
                try { modelPath = Settings.Default.LastModelPath; } catch { modelPath = null; }
                string rawModelName = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(modelPath))
                    {
                        rawModelName = Path.GetFileNameWithoutExtension(modelPath);
                    }
                }
                catch
                {
                    rawModelName = null;
                }

                string modelName = SanitizeFileName(rawModelName);
                string timeText = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
                string batchFolderName = $"{modelName}_{timeText}";
                string outDir = null;
                try
                {
                    outDir = CreateUniqueDirectoryPath(outputRoot, batchFolderName);
                    if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("无法创建批量输出目录: " + outDir + "\n" + ex.Message);
                    return;
                }

                string originalDir = null;
                string resultDir = null;
                try
                {
                    if (saveOriginal)
                    {
                        originalDir = Path.Combine(outDir, "原图");
                        if (!Directory.Exists(originalDir)) Directory.CreateDirectory(originalDir);
                    }

                    resultDir = Path.Combine(outDir, saveVisualization ? "可视化和结果" : "结果");
                    if (!Directory.Exists(resultDir)) Directory.CreateDirectory(resultDir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("无法创建输出子目录: " + outDir + "\n" + ex.Message);
                    return;
                }

                bool enableProfile = IsBatchProfileEnabled();
                BatchProfileLogger profileLogger = enableProfile
                    ? new BatchProfileLogger(Path.Combine(outDir, "batch_profile.csv"))
                    : null;

                // 批量导出选项：尽量复用，减少循环内创建
                BuildWpfOptions(threshold, out var exportOptions, out double screenLineWidth, out double screenFontSize);
                WpfVisualize.Options labelOptions = null;
                if (saveVisualization)
                {
                    try
                    {
                        labelOptions = new WpfVisualize.Options
                        {
                            DisplayText = Settings.Default.ShowTextPane,
                            DisplayScore = false,
                            TextOutOfBbox = Settings.Default.ShowTextOutOfBboxPane,
                            DisplayTextShadow = Settings.Default.ShowTextShadowPane,
                            FontColor = SafeParseColor(Settings.Default.FontColor, System.Windows.Media.Colors.White),
                            MaskColor = System.Windows.Media.Colors.LimeGreen,
                            MaskAlpha = 128
                        };
                    }
                    catch
                    {
                        labelOptions = new WpfVisualize.Options();
                    }
                }

                // 批量推理改为异步，不阻塞界面
                const int maxInFlight = 2;
                using (var renderWorker = new BatchRenderWorker(maxInFlight))
                {
                    var pending = new Queue<PendingRender>(maxInFlight);
                    await Task.Run(async () =>
                {
                        async Task DrainOneAsync()
                        {
                            if (pending.Count == 0) return;
                            var item = pending.Dequeue();
                            BatchRenderResult renderResult = null;
                            try
                            {
                                renderResult = await item.RenderTask.ConfigureAwait(false);
                            }
                            catch
                            {
                                renderResult = null;
                            }

                            if (renderResult != null && renderResult.Exported)
                            {
                                exported += 1;
                            }

                            if (item.Profile != null)
                            {
                                item.Profile.RenderMs = renderResult?.RenderMs ?? 0.0;
                                item.Profile.SaveMs = renderResult?.SaveMs ?? 0.0;
                                item.Profile.Exported = renderResult?.Exported ?? false;
                                item.Profile.OutputPath = renderResult?.OutputPath;
                                item.Profile.Error = renderResult?.Error ?? item.Profile.Error;
                                item.Profile.Status = renderResult == null
                                    ? "render_fail"
                                    : (renderResult.Exported ? "ok" : "export_fail");
                                item.Profile.MarkDone();
                                profileLogger?.Log(item.Profile);
                            }

                            RaiseBatchItemCompleted(new BatchItemCompletedEventArgs(
                                item.ImagePath,
                                success: true,
                                error: null,
                                elapsedMs: item.InferMs,
                                inferenceParams: item.InferenceParams,
                                exportedImagePath: renderResult?.OutputPath,
                                result: item.Result));

                            DisposeCSharpResultMasks(item.Result);
                        }

                        try
                        {
                            foreach (var imagePath in files)
                            {
                                try
                                {
                                    BatchProfileItem profile = enableProfile ? BatchProfileItem.Start(imagePath) : null;
                                    if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                                    {
                                        skipped += 1;
                                        RaiseBatchItemCompleted(new BatchItemCompletedEventArgs(
                                            imagePath,
                                            success: false,
                                            error: null,
                                            elapsedMs: 0.0,
                                            inferenceParams: SafeCloneJObject(inferenceParams),
                                            exportedImagePath: null,
                                            result: null));
                                        if (profile != null)
                                        {
                                            profile.Status = "missing";
                                            profile.MarkDone();
                                            profileLogger?.Log(profile);
                                        }
                                        UpdateBatchProgress(processed + skipped + failed, files.Length);
                                        continue;
                                    }

                                    var readSw = Stopwatch.StartNew();
                                    using (Mat original = Cv2.ImRead(imagePath))
                                    {
                                        readSw.Stop();
                                        if (profile != null) profile.ReadMs = readSw.Elapsed.TotalMilliseconds;
                                        if (original == null || original.Empty())
                                        {
                                            skipped += 1;
                                            RaiseBatchItemCompleted(new BatchItemCompletedEventArgs(
                                                imagePath,
                                                success: false,
                                                error: null,
                                                elapsedMs: 0.0,
                                                inferenceParams: SafeCloneJObject(inferenceParams),
                                                exportedImagePath: null,
                                                result: null));
                                            if (profile != null)
                                            {
                                                profile.Status = "read_empty";
                                                profile.MarkDone();
                                                profileLogger?.Log(profile);
                                            }
                                            UpdateBatchProgress(processed + skipped + failed, files.Length);
                                            continue;
                                        }

                                        if (saveOriginal && !string.IsNullOrEmpty(originalDir))
                                        {
                                            try
                                            {
                                                var copySw = Stopwatch.StartNew();
                                                string originalOutPath = GetRelativeOutputPath(folderPath, imagePath, originalDir);
                                                File.Copy(imagePath, originalOutPath, true);
                                                copySw.Stop();
                                                if (profile != null) profile.CopyMs = copySw.Elapsed.TotalMilliseconds;
                                            }
                                            catch
                                            {
                                                // 保存原图失败不影响整体批处理
                                            }
                                        }

                                        // 推理输入：RGB
                                        Utils.CSharpResult result;
                                        double inferMs = 0.0;
                                        using (var rgb = new Mat())
                                        {
                                            try
                                            {
                                                int ch = original.Channels();
                                                if (ch == 3) Cv2.CvtColor(original, rgb, ColorConversionCodes.BGR2RGB);
                                                else if (ch == 4) Cv2.CvtColor(original, rgb, ColorConversionCodes.BGRA2RGB);
                                                else if (ch == 1) Cv2.CvtColor(original, rgb, ColorConversionCodes.GRAY2RGB);
                                                else original.CopyTo(rgb);
                                            }
                                            catch
                                            {
                                                original.CopyTo(rgb);
                                            }

                                            LogInferStart("batch", imagePath, inferenceParams, rgb);
                                            var sw = Stopwatch.StartNew();
                                            result = model.Infer(rgb, inferenceParams);
                                            sw.Stop();
                                            inferMs = sw.Elapsed.TotalMilliseconds;
                                            if (profile != null) profile.InferMs = inferMs;
                                            LogInferEnd("batch", imagePath, result, inferMs);
                                            RunDvpHttpDiagnostic(rgb, inferenceParams, imagePath, "batch");
                                        }

                                        JArray labelShapes = null;
                                        WpfVisualize.VisualizeResult labelOverlay = null;
                                        if (saveVisualization)
                                        {
                                            var jsonSw = Stopwatch.StartNew();
                                            try
                                            {
                                                string jsonPath = Path.ChangeExtension(imagePath, ".json");
                                                if (File.Exists(jsonPath))
                                                {
                                                    string jsonContent = File.ReadAllText(jsonPath);
                                                    var json = JsonConvert.DeserializeObject(jsonContent);
                                                    if (json is JObject jObj && jObj["shapes"] is JArray shp)
                                                    {
                                                        labelShapes = shp;
                                                    }
                                                }
                                            }
                                            catch
                                            {
                                                labelShapes = null;
                                            }

                                            if (labelShapes != null && labelShapes.Count > 0)
                                            {
                                                try
                                                {
                                                    var stroke = SafeParseColor(Settings.Default.BBoxBorderColor, System.Windows.Media.Colors.Red);
                                                    labelOverlay = WpfVisualize.BuildFromLabelmeShapes(labelShapes, labelOptions, stroke);
                                                }
                                                catch
                                                {
                                                    labelOverlay = null;
                                                }
                                            }
                                            jsonSw.Stop();
                                            if (profile != null) profile.JsonMs = jsonSw.Elapsed.TotalMilliseconds;
                                        }

                                        // GUI叠加导出：批量不预览，仅导出渲染结果（渲染线程执行）
                                        var baseSw = Stopwatch.StartNew();
                                        var baseImage = MatBitmapSource.ToBitmapSource(original);
                                        baseSw.Stop();
                                        if (profile != null) profile.BaseImageMs = baseSw.Elapsed.TotalMilliseconds;
                                        var renderTask = renderWorker.Enqueue(new BatchRenderJob
                                        {
                                            BaseImage = baseImage,
                                            Result = result,
                                            ExportOptions = exportOptions,
                                            ScreenLineWidth = screenLineWidth,
                                            ScreenFontSize = screenFontSize,
                                            LabelOverlay = labelOverlay,
                                            LabelOptions = labelOptions,
                                            SaveVisualization = saveVisualization,
                                            OutputDir = resultDir,
                                            ImagePath = imagePath,
                                            BaseFolderPath = folderPath
                                        });

                                        pending.Enqueue(new PendingRender(
                                            imagePath,
                                            renderTask,
                                            SafeCloneJObject(inferenceParams),
                                            inferMs,
                                            result,
                                            profile));

                                        if (pending.Count >= maxInFlight)
                                        {
                                            await DrainOneAsync().ConfigureAwait(false);
                                        }
                                    }

                                    processed += 1;
                                    UpdateBatchProgress(processed + skipped + failed, files.Length);
                                }
                                catch (Exception ex)
                                {
                                    failed += 1;
                                    RaiseBatchItemCompleted(new BatchItemCompletedEventArgs(
                                        imagePath,
                                        success: false,
                                        error: ex,
                                        elapsedMs: 0.0,
                                        inferenceParams: SafeCloneJObject(inferenceParams),
                                        exportedImagePath: null,
                                        result: null));
                                    if (enableProfile)
                                    {
                                        var profile = BatchProfileItem.Start(imagePath);
                                        profile.Status = "exception";
                                        profile.Error = ex.Message;
                                        profile.MarkDone();
                                        profileLogger?.Log(profile);
                                    }
                                    UpdateBatchProgress(processed + skipped + failed, files.Length);
                                }
                            }
                        }
                        finally
                        {
                            renderWorker.Complete();
                            while (pending.Count > 0)
                            {
                                await DrainOneAsync().ConfigureAwait(false);
                            }
                        }
                    });
                }

                RaiseBatchCompleted(new BatchCompletedEventArgs(
                    total: files.Length,
                    processed: processed,
                    exported: exported,
                    skipped: skipped,
                    failed: failed,
                    outputDir: outDir,
                    inferenceParams: SafeCloneJObject(inferenceParams)));
                
                bool openAfterBatch = false;
                try { openAfterBatch = Settings.Default.OpenOutputFolderAfterBatch; } catch { openAfterBatch = false; }
                if (openAfterBatch)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(outDir) && Directory.Exists(outDir))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = $"\"{outDir}\"",
                                UseShellExecute = true
                            });
                        }
                    }
                    catch
                    {
                        // 打开文件夹失败不影响流程
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("批量处理失败: " + ex.Message);
            }
            finally
            {
                if (batchStarted)
                {
                    EndBatchProgress();
                }
            }
        }

        /// <summary>
        /// 代码开关：是否导出批量预测的 CSV 性能日志（batch_profile.csv）。
        /// 设置为 false 可禁用 CSV 导出。
        /// </summary>
        private const bool EnableBatchProfileCsv = false;

        private static bool IsBatchProfileEnabled()
        {
            // 代码开关优先：如果代码中禁用，则直接返回 false
            if (!EnableBatchProfileCsv) return false;

            try
            {
                var v = Environment.GetEnvironmentVariable("DLCV_BATCH_PROFILE");
                if (string.IsNullOrWhiteSpace(v)) return true;
                v = v.Trim();
                return !string.Equals(v, "0", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(v, "no", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        private sealed class BatchProfileItem
        {
            public string ImagePath { get; }
            public string Status { get; set; }
            public double ReadMs { get; set; }
            public double CopyMs { get; set; }
            public double InferMs { get; set; }
            public double JsonMs { get; set; }
            public double BaseImageMs { get; set; }
            public double RenderMs { get; set; }
            public double SaveMs { get; set; }
            public double TotalMs { get; private set; }
            public double QueueWaitMs { get; private set; }
            public bool Exported { get; set; }
            public string OutputPath { get; set; }
            public string Error { get; set; }
            private long _totalStartTicks;

            private BatchProfileItem(string imagePath)
            {
                ImagePath = imagePath ?? string.Empty;
                _totalStartTicks = Stopwatch.GetTimestamp();
            }

            public static BatchProfileItem Start(string imagePath)
            {
                return new BatchProfileItem(imagePath);
            }

            public void MarkDone()
            {
                long end = Stopwatch.GetTimestamp();
                TotalMs = (end - _totalStartTicks) * 1000.0 / Stopwatch.Frequency;
                double accounted = ReadMs + CopyMs + InferMs + JsonMs + BaseImageMs + RenderMs + SaveMs;
                QueueWaitMs = Math.Max(0.0, TotalMs - accounted);
                if (string.IsNullOrWhiteSpace(Status)) Status = "ok";
            }
        }

        private sealed class BatchProfileLogger
        {
            private readonly string _path;
            private readonly object _lock = new object();

            public BatchProfileLogger(string path)
            {
                _path = path;
                try
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
                catch { }

                if (!File.Exists(_path))
                {
                    var header = string.Join(",",
                        "ImagePath",
                        "Status",
                        "ReadMs",
                        "CopyMs",
                        "InferMs",
                        "JsonMs",
                        "BaseImageMs",
                        "RenderMs",
                        "SaveMs",
                        "TotalMs",
                        "QueueWaitMs",
                        "Exported",
                        "OutputPath",
                        "Error");
                    File.WriteAllText(_path, header + Environment.NewLine, Encoding.UTF8);
                }
            }

            public void Log(BatchProfileItem item)
            {
                if (item == null) return;
                string line = string.Join(",",
                    Escape(item.ImagePath),
                    Escape(item.Status),
                    FormatNum(item.ReadMs),
                    FormatNum(item.CopyMs),
                    FormatNum(item.InferMs),
                    FormatNum(item.JsonMs),
                    FormatNum(item.BaseImageMs),
                    FormatNum(item.RenderMs),
                    FormatNum(item.SaveMs),
                    FormatNum(item.TotalMs),
                    FormatNum(item.QueueWaitMs),
                    item.Exported ? "1" : "0",
                    Escape(item.OutputPath),
                    Escape(item.Error));

                lock (_lock)
                {
                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                }
            }

            private static string FormatNum(double value)
            {
                return value.ToString("F3", CultureInfo.InvariantCulture);
            }

            private static string Escape(string value)
            {
                if (string.IsNullOrEmpty(value)) return "\"\"";
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
        }

        private sealed class PendingRender
        {
            public string ImagePath { get; }
            public Task<BatchRenderResult> RenderTask { get; }
            public JObject InferenceParams { get; }
            public double InferMs { get; }
            public Utils.CSharpResult Result { get; }
            public BatchProfileItem Profile { get; }

            public PendingRender(string imagePath, Task<BatchRenderResult> renderTask, JObject inferenceParams, double inferMs, Utils.CSharpResult result, BatchProfileItem profile)
            {
                ImagePath = imagePath;
                RenderTask = renderTask;
                InferenceParams = inferenceParams;
                InferMs = inferMs;
                Result = result;
                Profile = profile;
            }
        }

        private sealed class BatchRenderJob
        {
            public BitmapSource BaseImage { get; set; }
            public Utils.CSharpResult Result { get; set; }
            public WpfVisualize.Options ExportOptions { get; set; }
            public double ScreenLineWidth { get; set; }
            public double ScreenFontSize { get; set; }
            public WpfVisualize.VisualizeResult LabelOverlay { get; set; }
            public WpfVisualize.Options LabelOptions { get; set; }
            public bool SaveVisualization { get; set; }
            public string OutputDir { get; set; }
            public string ImagePath { get; set; }
            /// <summary>
            /// 输入根目录路径，用于计算相对路径以保留目录结构
            /// </summary>
            public string BaseFolderPath { get; set; }
            public TaskCompletionSource<BatchRenderResult> Completion { get; set; }
        }

        private sealed class BatchRenderResult
        {
            public string OutputPath { get; }
            public bool Exported { get; }
            public double RenderMs { get; }
            public double SaveMs { get; }
            public string Error { get; }

            public BatchRenderResult(string outputPath, bool exported, double renderMs, double saveMs, string error = null)
            {
                OutputPath = outputPath;
                Exported = exported;
                RenderMs = renderMs;
                SaveMs = saveMs;
                Error = error;
            }
        }

        private sealed class BatchRenderWorker : IDisposable
        {
            private readonly BlockingCollection<BatchRenderJob> _queue;
            private readonly Thread _thread;
            private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

            public BatchRenderWorker(int capacity)
            {
                _queue = new BlockingCollection<BatchRenderJob>(Math.Max(1, capacity));
                _thread = new Thread(RenderLoop)
                {
                    IsBackground = true
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                _ready.Wait();
            }

            public Task<BatchRenderResult> Enqueue(BatchRenderJob job)
            {
                if (job == null) throw new ArgumentNullException(nameof(job));
                job.Completion = new TaskCompletionSource<BatchRenderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _queue.Add(job);
                return job.Completion.Task;
            }

            public void Complete()
            {
                try { _queue.CompleteAdding(); } catch { }
            }

            private void RenderLoop()
            {
                _ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                _ready.Set();

                foreach (var job in _queue.GetConsumingEnumerable())
                {
                    BatchRenderResult result;
                    try
                    {
                        result = Execute(job);
                    }
                    catch
                    {
                        result = new BatchRenderResult(null, false, 0.0, 0.0, "render_exception");
                    }

                    try { job.Completion.TrySetResult(result); } catch { }
                }
            }

            private static BatchRenderResult Execute(BatchRenderJob job)
            {
            if (job == null || job.BaseImage == null) return new BatchRenderResult(null, false, 0.0, 0.0, "invalid_job");

                BitmapSource exportBitmap = null;
                BitmapSource labelBitmap = null;
            double renderMs = 0.0;
            double saveMs = 0.0;

            var renderSw = Stopwatch.StartNew();
            try
            {
                exportBitmap = GuiOverlayExporter.Render(
                    job.BaseImage,
                    job.Result,
                    job.ExportOptions,
                    job.ScreenLineWidth,
                    job.ScreenFontSize,
                    GuiOverlayExporter.ExportRenderMode.HardEdge);
            }
            catch
            {
                exportBitmap = null;
            }

            if (job.SaveVisualization)
            {
                try
                {
                    if (job.LabelOverlay != null && job.LabelOverlay.Items != null && job.LabelOverlay.Items.Count > 0)
                    {
                        labelBitmap = GuiOverlayExporter.RenderWithVisualizeResult(
                            job.BaseImage,
                            job.LabelOverlay,
                            job.LabelOptions,
                            job.ScreenLineWidth,
                            job.ScreenFontSize,
                            GuiOverlayExporter.ExportRenderMode.HardEdge);
                    }
                    else
                    {
                        labelBitmap = job.BaseImage;
                    }
                }
                catch
                {
                    labelBitmap = job.BaseImage;
                }
            }
            renderSw.Stop();
            renderMs = renderSw.Elapsed.TotalMilliseconds;

                string outPath = null;
                bool exported = false;
                if (exportBitmap != null)
                {
                    try
                    {
                        // 使用相对路径保留目录结构
                        outPath = GetRelativeOutputPath(job.BaseFolderPath, job.ImagePath, job.OutputDir, ".png");

                    var saveSw = Stopwatch.StartNew();
                        if (job.SaveVisualization)
                        {
                            if (labelBitmap == null) labelBitmap = job.BaseImage;
                            var combined = GuiOverlayExporter.ConcatenateHorizontal(labelBitmap, exportBitmap);
                            GuiOverlayExporter.SavePng(combined, outPath);
                        }
                        else
                        {
                            GuiOverlayExporter.SavePng(exportBitmap, outPath);
                        }
                    saveSw.Stop();
                    saveMs = saveSw.Elapsed.TotalMilliseconds;

                        exported = true;
                    }
                    catch
                    {
                        outPath = null;
                        exported = false;
                    }
                }

            string error = exported ? null : "export_failed";
            return new BatchRenderResult(outPath, exported, renderMs, saveMs, error);
            }

            public void Dispose()
            {
                try { _queue.CompleteAdding(); } catch { }
                try { _thread.Join(2000); } catch { }
                try { _queue.Dispose(); } catch { }
                try { _ready.Dispose(); } catch { }
            }
        }

        private static JObject SafeCloneJObject(JObject obj)
        {
            if (obj == null) return null;
            try
            {
                var t = obj.DeepClone();
                return t as JObject ?? JObject.FromObject(obj);
            }
            catch
            {
                try { return JObject.FromObject(obj); } catch { return obj; }
            }
        }

        private static void DisposeCSharpResultMasks(Utils.CSharpResult? result)
        {
            if (!result.HasValue) return;
            try
            {
                var samples = result.Value.SampleResults;
                if (samples == null) return;
                foreach (var sr in samples)
                {
                    if (sr.Results == null) continue;
                    foreach (var obj in sr.Results)
                    {
                        try
                        {
                            if (obj.Mask != null && !obj.Mask.Empty())
                            {
                                obj.Mask.Dispose();
                            }
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private (int RequestId, CancellationToken Token) BeginNewImageProcessRequest()
        {
            var cts = new CancellationTokenSource();
            int requestId = Interlocked.Increment(ref _imageProcessRequestId);

            CancellationTokenSource previous = null;
            lock (_imageProcessSync)
            {
                previous = _imageProcessCts;
                _imageProcessCts = cts;
                _imageProcessActiveRequestId = requestId;
            }

            try { previous?.Cancel(); } catch { }
            // 注意：不在这里 Dispose previous，避免后台任务访问 Token 时触发 ObjectDisposedException
            return (requestId, cts.Token);
        }

        private bool IsImageProcessRequestCurrent(int requestId, string imagePath, CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            if (requestId != Volatile.Read(ref _imageProcessActiveRequestId)) return false;

            // _currentImagePath 可能在切图时改变；仅允许回写当前正在看的那张图
            string current = _currentImagePath;
            if (string.IsNullOrEmpty(current)) return false;

            try
            {
                return string.Equals(Path.GetFullPath(imagePath), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(imagePath, current, StringComparison.OrdinalIgnoreCase);
            }
        }

        private async Task ProcessSelectedImageAsync(string imagePath)
        {
            int requestId = 0;
            CancellationToken token = CancellationToken.None;

            try
            {
                double thresholdVal = 0.5;
                double.TryParse(ConfidenceVal.Text, out thresholdVal);

                var req = BeginNewImageProcessRequest();
                requestId = req.RequestId;
                token = req.Token;

            // 先清理上一张图的叠加，避免切图时短暂残留旧推理结果（尤其是 WPF 叠加显示模式）
            try
                {
                    if (wpfViewer1 != null)
                    {
                        wpfViewer1.ClearExternalOverlay();
                        wpfViewer1.ClearResults();
                    }
                    if (wpfViewer2 != null)
                    {
                        wpfViewer2.ClearExternalOverlay();
                        wpfViewer2.ClearResults();
                    }
                }
                catch
                {
                    // ignore
                }

                // 在后台线程执行耗时操作
                await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return;

                    using (Mat original = Cv2.ImRead(imagePath))
                    {
                        if (token.IsCancellationRequested) return;

                        if (original.Empty())
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;
                                MessageBox.Show("无法读取图片: " + imagePath);
                            });
                            return;
                        }

                        // 功能 1：加载本地同名JSON并使用标准可视化模块 / 标注轮廓绘制
                        // 在后台线程中先读取设置值，避免在 Dispatcher.Invoke 内部读取时设置已被改动
                        bool showOriginalPane = Settings.Default.ShowOriginalPane;

                        string jsonPath = Path.ChangeExtension(imagePath, ".json");
                        if (File.Exists(jsonPath))
                        {
                            try
                            {
                                string jsonContent = File.ReadAllText(jsonPath);
                                var json = JsonConvert.DeserializeObject(jsonContent);
                                JArray shapes = null;

                                if (json is JObject jObj)
                                {
                                    if (jObj["shapes"] is JArray shp)
                                    {
                                        shapes = shp;
                                    }
                                }

                                if (shapes != null)
                                {
                                    // WPFViewer 模式：原图 + GUI 叠加层（标注只在左侧显示），文字缩放与推理一致
                                    if (token.IsCancellationRequested) return;
                                    var originalSource = MatBitmapSource.ToBitmapSource(original);

                                    Dispatcher.Invoke(() =>
                                    {
                                        if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;

                                        bool dual = (showOriginalPane && border1 != null && border1.Visibility == Visibility.Visible);
                                        if (dual)
                                        {
                                            // 标注 overlay（左侧）
                                            var stroke = SafeParseColor(Settings.Default.BBoxBorderColor, System.Windows.Media.Colors.Red);
                                            var fontColor = SafeParseColor(Settings.Default.FontColor, System.Windows.Media.Colors.White);
                                            var labelOpt = new WpfVisualize.Options
                                            {
                                                DisplayText = Settings.Default.ShowTextPane,
                                                FontColor = fontColor
                                            };
                                            var labelOverlay = WpfVisualize.BuildFromLabelmeShapes(shapes, labelOpt, stroke);

                                            if (wpfViewer1 != null)
                                            {
                                                ApplyWpfViewerOptions(wpfViewer1, thresholdVal);
                                                wpfViewer1.UpdateImage(originalSource);
                                                wpfViewer1.ExternalOverlay = labelOverlay;
                                                wpfViewer1.ClearResults();
                                            }
                                            if (wpfViewer2 != null)
                                            {
                                                ApplyWpfViewerOptions(wpfViewer2, thresholdVal);
                                                wpfViewer2.UpdateImage(originalSource);   // 推理前：先显示原图
                                                wpfViewer2.ClearExternalOverlay();        // 不显示标注
                                                wpfViewer2.ClearResults();                // 清理上一张图的推理叠加
                                            }
                                        }
                                        else
                                        {
                                            // 单图：推理前仅显示原图（不显示标注）
                                            if (wpfViewer1 != null)
                                            {
                                                wpfViewer1.ClearExternalOverlay();
                                                wpfViewer1.ClearResults();
                                            }
                                            if (wpfViewer2 != null)
                                            {
                                                ApplyWpfViewerOptions(wpfViewer2, thresholdVal);
                                                wpfViewer2.UpdateImage(originalSource);
                                                wpfViewer2.ClearExternalOverlay();
                                                wpfViewer2.ClearResults();
                                            }
                                        }
                                    }, System.Windows.Threading.DispatcherPriority.Normal);
                                }
                                else
                                {
                                    // JSON 不包含可识别的检测标注结果，直接显示原图
                                    if (token.IsCancellationRequested) return;
                                    var originalSource = MatBitmapSource.ToBitmapSource(original);

                                    Dispatcher.Invoke(() =>
                                    {
                                        if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;
                                        bool dual = (showOriginalPane && border1 != null && border1.Visibility == Visibility.Visible);
                                        if (dual)
                                        {
                                            if (wpfViewer1 != null) { ApplyWpfViewerOptions(wpfViewer1, thresholdVal); wpfViewer1.UpdateImage(originalSource); wpfViewer1.ClearExternalOverlay(); wpfViewer1.ClearResults(); }
                                            if (wpfViewer2 != null) { ApplyWpfViewerOptions(wpfViewer2, thresholdVal); wpfViewer2.UpdateImage(originalSource); wpfViewer2.ClearExternalOverlay(); wpfViewer2.ClearResults(); }
                                        }
                                        else
                                        {
                                            if (wpfViewer2 != null) { ApplyWpfViewerOptions(wpfViewer2, thresholdVal); wpfViewer2.UpdateImage(originalSource); wpfViewer2.ClearExternalOverlay(); wpfViewer2.ClearResults(); }
                                        }
                                    }, System.Windows.Threading.DispatcherPriority.Normal);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("处理JSON失败: " + ex.Message);
                                if (token.IsCancellationRequested) return;
                                var originalSource = MatBitmapSource.ToBitmapSource(original);

                                Dispatcher.Invoke(() =>
                                {
                                    if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;
                                    bool dual = (showOriginalPane && border1 != null && border1.Visibility == Visibility.Visible);
                                    if (dual)
                                    {
                                        if (wpfViewer1 != null) { ApplyWpfViewerOptions(wpfViewer1, thresholdVal); wpfViewer1.UpdateImage(originalSource); wpfViewer1.ClearExternalOverlay(); wpfViewer1.ClearResults(); }
                                        if (wpfViewer2 != null) { ApplyWpfViewerOptions(wpfViewer2, thresholdVal); wpfViewer2.UpdateImage(originalSource); wpfViewer2.ClearExternalOverlay(); wpfViewer2.ClearResults(); }
                                    }
                                    else
                                    {
                                        if (wpfViewer1 != null) { wpfViewer1.ClearExternalOverlay(); wpfViewer1.ClearResults(); }
                                        if (wpfViewer2 != null) { ApplyWpfViewerOptions(wpfViewer2, thresholdVal); wpfViewer2.UpdateImage(originalSource); wpfViewer2.ClearExternalOverlay(); wpfViewer2.ClearResults(); }
                                    }
                                }, System.Windows.Threading.DispatcherPriority.Normal);
                            }
                        }
                        else
                        {
                            if (token.IsCancellationRequested) return;
                            var originalSource = MatBitmapSource.ToBitmapSource(original);

                            Dispatcher.Invoke(() =>
                            {
                                if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;
                                bool dual = (showOriginalPane && border1 != null && border1.Visibility == Visibility.Visible);
                                if (dual)
                                {
                                    if (wpfViewer1 != null) { ApplyWpfViewerOptions(wpfViewer1, thresholdVal); wpfViewer1.UpdateImage(originalSource); wpfViewer1.ClearExternalOverlay(); wpfViewer1.ClearResults(); }
                                    if (wpfViewer2 != null) { ApplyWpfViewerOptions(wpfViewer2, thresholdVal); wpfViewer2.UpdateImage(originalSource); wpfViewer2.ClearExternalOverlay(); wpfViewer2.ClearResults(); }
                                }
                                else
                                {
                                    if (wpfViewer1 != null) { wpfViewer1.ClearExternalOverlay(); wpfViewer1.ClearResults(); }
                                    if (wpfViewer2 != null) { ApplyWpfViewerOptions(wpfViewer2, thresholdVal); wpfViewer2.UpdateImage(originalSource); wpfViewer2.ClearExternalOverlay(); wpfViewer2.ClearResults(); }
                                }
                            }, System.Windows.Threading.DispatcherPriority.Normal);
                        }

                        // 功能 2：使用已加载的模型进行推理并使用标准可视化模块绘制
                        if (model != null)
                        {
                            if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;

                            // 在UI线程上读取参数值并更新模型参数
                            double threshold = 0.5;
                            double iouThreshold = 0.2;
                            double epsilon = 1000.0;
                            bool useWpfViewer = true;

                            Dispatcher.Invoke(() =>
                            {
                                if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;
                                string confText = ConfidenceVal.Text;
                                string iouText = IOUVal.Text;
                                string epsText = AutoLabelComplexityVal.Text;

                                Console.WriteLine($"[推理前] 从UI读取参数文本: ConfidenceVal.Text='{confText}', IOUVal.Text='{iouText}', AutoLabelComplexityVal.Text='{epsText}'");

                                if (double.TryParse(confText, out double confVal))
                                {
                                    threshold = confVal;
                                    Console.WriteLine($"[推理前] 成功解析 ConfidenceVal: {threshold}");
                                }
                                else
                                {
                                    Console.WriteLine($"[推理前] 解析 ConfidenceVal 失败，使用默认值 {threshold}");
                                }

                                if (double.TryParse(iouText, out double iouVal))
                                {
                                    iouThreshold = iouVal;
                                    Console.WriteLine($"[推理前] 成功解析 IOUVal: {iouThreshold}");
                                }
                                else
                                {
                                    Console.WriteLine($"[推理前] 解析 IOUVal 失败，使用默认值 {iouThreshold}");
                                }

                                if (double.TryParse(epsText, out double epsVal))
                                {
                                    epsilon = epsVal;
                                }
                                else
                                {
                                }
                            }, System.Windows.Threading.DispatcherPriority.Normal);

                            if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;

                            // 使用 Model.Infer 进行推理（返回 CSharpResult，与 DLCVDEMO 一致）
                            // 构造推理参数（与 DlcvDemo 保持一致，不传递 iou_threshold）
                            JObject inferenceParams = new JObject();
                            inferenceParams["threshold"] = (float)threshold;
                            inferenceParams["with_mask"] = true;
                            EnsureDvpParamsMirror(inferenceParams);

                            System.Diagnostics.Debug.WriteLine($"[模型推理] 开始推理，参数: threshold={(float)threshold}, with_mask=true");

                            // 执行推理
                            Utils.CSharpResult result;
                            double inferMs = 0.0;
                            using (var rgb = new Mat())
                            {
                                // OpenCV 读图默认是 BGR；模型推理输入与 DLCV_DEMO/模块化推理保持一致：使用 RGB
                                try
                                {
                                    int ch = original.Channels();
                                    if (ch == 3) Cv2.CvtColor(original, rgb, ColorConversionCodes.BGR2RGB);
                                    else if (ch == 4) Cv2.CvtColor(original, rgb, ColorConversionCodes.BGRA2RGB);
                                    else if (ch == 1) Cv2.CvtColor(original, rgb, ColorConversionCodes.GRAY2RGB);
                                    else original.CopyTo(rgb);
                                }
                                catch
                                {
                                    // 如果转换失败，退化为直接输入（避免因异常导致整条链路中断）
                                    original.CopyTo(rgb);
                                }
                                LogInferStart("single", imagePath, inferenceParams, rgb);
                                var sw = Stopwatch.StartNew();
                                result = model.Infer(rgb, inferenceParams);
                                sw.Stop();
                                inferMs = sw.Elapsed.TotalMilliseconds;
                                LogInferEnd("single", imagePath, result, inferMs);
                                RunDvpHttpDiagnostic(rgb, inferenceParams, imagePath, "single");
                            }
                            System.Diagnostics.Debug.WriteLine($"[模型推理] 推理完成，SampleResults.Count: {result.SampleResults?.Count ?? 0}");

                            if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;

                            // 添加调试信息查看检测结果详情
                            if (result.SampleResults != null && result.SampleResults.Count > 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"[调试] Results.Count: {result.SampleResults[0].Results?.Count ?? 0}");
                                if (result.SampleResults[0].Results != null && result.SampleResults[0].Results.Count > 0)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[调试] 检测到 {result.SampleResults[0].Results.Count} 个物体");
                                    for (int i = 0; i < Math.Min(result.SampleResults[0].Results.Count, 5); i++)
                                    {
                                        var det = result.SampleResults[0].Results[i];
                                        System.Diagnostics.Debug.WriteLine($"[调试] 物体{i}: 类别={det.CategoryName}, 置信度={det.Score:F3}, WithBbox={det.WithBbox}, Bbox.Count={det.Bbox?.Count ?? 0}");
                                    }
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[调试] 没有检测到任何物体!");
                                }
                            }

                            // 检查是否有推理结果
                            bool hasResults = result.SampleResults != null && result.SampleResults.Count > 0 &&
                                            result.SampleResults[0].Results != null && result.SampleResults[0].Results.Count > 0;

                            // 直接在图像上绘制推理结果（与 DLCVDEMO 类似）
                            if (useWpfViewer)
                            {
                                // WPF 叠加显示：不再把文字画进 Mat，只显示原图 + GUI 叠加层
                                if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;

                                var baseImage = MatBitmapSource.ToBitmapSource(original);

                                Dispatcher.Invoke(() =>
                                {
                                    if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;
                                    // 右侧：原图 + 推理结果叠加
                                    if (wpfViewer2 != null)
                                    {
                                        ApplyWpfViewerOptions(wpfViewer2, threshold);
                                        wpfViewer2.UpdateImage(baseImage);
                                        wpfViewer2.ClearExternalOverlay(); // 推理视图不显示标注层
                                        if (hasResults) wpfViewer2.UpdateResults(result);
                                        else wpfViewer2.ClearResults();
                                    }
                                }, System.Windows.Threading.DispatcherPriority.Send);
                            }
                        } // if (model != null) 块结束
                    } // using 块结束
                }, token);
            }
            catch (OperationCanceledException)
            {
                // 被新请求取消：不弹窗、不打断用户操作
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    if (!IsImageProcessRequestCurrent(requestId, imagePath, token)) return;
                    MessageBox.Show("处理图片失败: " + ex.Message);
                });
            }
        }

        private string GetCurrentOrSelectedImagePathForRefresh()
        {
            if (!string.IsNullOrEmpty(_currentImagePath) && File.Exists(_currentImagePath))
            {
                return _currentImagePath;
            }

            if (tvFolders != null && tvFolders.SelectedItem is FileNode node)
            {
                if (!node.IsDirectory && !string.IsNullOrEmpty(node.FullPath) && File.Exists(node.FullPath))
                {
                    return node.FullPath;
                }
            }

            return null;
        }

        public Task RefreshImagesAsync()
        {
            string path = GetCurrentOrSelectedImagePathForRefresh();
            if (string.IsNullOrEmpty(path)) return Task.CompletedTask;

            // 保持 _currentImagePath 与 UI 选择一致，避免后续逻辑依赖为空
            _currentImagePath = path;
            return ProcessSelectedImageAsync(path);
        }

        // 兼容旧调用：保持签名不变，但不再使用 async void
        public void RefreshImages()
        {
            _ = RefreshImagesAsync();
        }

        public void RequestRefreshImagesDebounced()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RequestRefreshImagesDebounced), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            EnsureRefreshDebounceTimer();
            _refreshDebounceTimer.Stop();
            _refreshDebounceTimer.Start();
        }

        private void EnsureRefreshDebounceTimer()
        {
            if (_refreshDebounceTimer != null) return;

            _refreshDebounceTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };

            _refreshDebounceTimer.Tick += async (s, e) =>
            {
                _refreshDebounceTimer.Stop();
                try
                {
                    await RefreshImagesAsync();
                }
                catch
                {
                    // 保持静默：刷新失败不应打断用户交互
                }
            };
        }
    }
}

