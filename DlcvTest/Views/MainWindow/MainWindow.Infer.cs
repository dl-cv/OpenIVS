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
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using dlcv_infer_csharp;
using DlcvModules;
using OpenCvSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DlcvTest.Properties;
using DlcvTest.WPFViewer;
using System.Net.WebSockets;

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

            // 获取模型路径
            string modelPath = null;
            try { modelPath = Settings.Default.LastModelPath; } catch { }
            if(string.IsNullOrEmpty(modelPath))
            {
                MessageBox.Show("请先加载模型");
                return;
            }

            // 读取推理参数
            double threshold = 0.5;
            bool saveOriginal = false;
            bool saveVisualization = false;
            bool openAfterBatch = false;
            
            try
            {
                if(ConfidenceVal != null && double.TryParse(ConfidenceVal.Text, out double confVal))
                {
                    threshold = Clamp01(confVal);
                    saveOriginal = Settings.Default.SaveOriginal;
                    saveVisualization = Settings.Default.SaveVisualization;
                    openAfterBatch = Settings.Default.OpenOutputFolderAfterBatch;
                }
            }
            catch
            { }

            // 输出目录
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

            // 估算文件数量用于进度显示
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };
            int estimatedCount = 0;
            try
            {
                estimatedCount = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
                    .Count(f => extensions.Contains(Path.GetExtension(f).ToLower()));
            }
            catch {}

            if(estimatedCount == 0)
            {
                MessageBox.Show("文件夹中没有图片！");
                return;
            }

            BeginBatchProgress(estimatedCount);

            try
            {
                await RunBatchLocalAsync(
                    srcDir: folderPath,
                    dstDir: outputRoot,
                    threshold: threshold,
                    saveImg: saveOriginal,
                    saveVis: saveVisualization,
                    openDstDir: openAfterBatch
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("批量推理失败：" + ex.Message);
            }
            finally
            {
                EndBatchProgress();
            }
        }

        /// <summary>
        /// 通过 WebSocket 调用后端的 /predict_directory 接口进行批量推理
        /// </summary>
        private async Task RunBatchViaWebSocketAsync(
            string modelPath,
            string srcDir,
            string dstDir,
            double threshold,
            bool saveImg,
            bool saveVis,
            bool openDstDir)
        {
            string serverUrl = "ws://127.0.0.1:9890/predict_directory";

            using (var ws = new ClientWebSocket())
            {
                var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));

                try
                {
                    // 1. 连接 WebSocket
                    await ws.ConnectAsync(new Uri(serverUrl), cts.Token);

                    // 2. 发送批量推理参数
                    // 注意：后端会在 dst_dir 下创建 {模型名}_测试时间_{时间戳}/ 子目录保存结果
                    var requestData = new JObject
                    {
                        ["model_path"] = modelPath,
                        ["src_dir"] = srcDir,
                        ["dst_dir"] = dstDir,
                        ["threshold"] = threshold,
                        ["save_img"] = saveImg,
                        ["save_vis"] = true,        
                        ["save_json"] = true,       
                        ["open_dst_dir"] = openDstDir,
                        ["batch_size"] = 1,
                        ["save_ok_img"] = true,   
                        ["save_ng_img"] = true,   
                        ["save_by_category"] = false,
                        ["with_mask"] = true
                    };

                    string jsonStr = requestData.ToString(Formatting.None);
                    System.Diagnostics.Debug.WriteLine($"[WebSocket批量推理] 发送参数: {jsonStr}");
                    var sendBuffer = Encoding.UTF8.GetBytes(jsonStr);
                    await ws.SendAsync(
                        new ArraySegment<byte>(sendBuffer),
                        WebSocketMessageType.Text,
                        true,
                        cts.Token);

                    // 3. 接收进度和结果
                    var receiveBuffer = new byte[8192];
                    bool completed = false;
                    string lastErrorMessage = null;

                    while (ws.State == WebSocketState.Open && !completed)
                    {
                        try
                        {
                            var result = await ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts.Token);

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                System.Diagnostics.Debug.WriteLine($"[WebSocket批量推理] 服务端关闭连接, CloseStatus={ws.CloseStatus}, CloseStatusDescription={ws.CloseStatusDescription}");
                                break;
                            }

                            if (result.MessageType == WebSocketMessageType.Text)
                            {
                                string message = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                                System.Diagnostics.Debug.WriteLine($"[WebSocket批量推理] 收到消息: {message}");

                                try
                                {
                                    var json = JObject.Parse(message);

                                    // 处理错误（先检查错误）
                                    if (json.ContainsKey("code") && json["code"].ToString() != "00000")
                                    {
                                        lastErrorMessage = json["message"]?.ToString() ?? "未知错误";
                                        System.Diagnostics.Debug.WriteLine($"[WebSocket批量推理] 后端返回错误: {lastErrorMessage}");
                                        throw new Exception(lastErrorMessage);
                                    }

                                    // 处理进度更新
                                    if (json.ContainsKey("progress"))
                                    {
                                        double progress = json["progress"].Value<double>();
                                        int current = (int)(progress * FolderImageCount);
                                        UpdateBatchProgress(current, FolderImageCount);
                                        System.Diagnostics.Debug.WriteLine($"[WebSocket批量推理] 进度: {progress:P0}");

                                        if (progress >= 1.0)
                                        {
                                            completed = true;
                                            System.Diagnostics.Debug.WriteLine($"[WebSocket批量推理] 完成！输出目录: {dstDir}");
                                        }
                                    }
                                }
                                catch (JsonException)
                                {
                                    // 忽略无法解析的消息
                                }
                            }
                        }
                        catch (WebSocketException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[WebSocket批量推理] WebSocketException: {ex.Message}, ws.State={ws.State}");
                            
                            // 如果已经收到 progress=1.0，连接异常关闭也视为成功
                            if (completed)
                                break;

                            // 检查是否是"连接被关闭"类型的错误
                            if (ws.State == WebSocketState.Aborted ||
                                ws.State == WebSocketState.Closed)
                            {
                                // 服务端可能已完成并关闭，但如果没有收到完成消息，报错
                                if (!completed)
                                {
                                    throw new Exception($"WebSocket 连接被关闭，推理可能失败。请检查后端日志。");
                                }
                                break;
                            }

                            throw;
                        }
                    }

                    // 如果没有完成且没有收到任何进度消息，报错
                    if (!completed)
                    {
                        throw new Exception(lastErrorMessage ?? "批量推理未正常完成，请检查后端日志或输出目录。");
                    }

                    // 4. 尝试正常关闭（如果还没关闭的话）
                    if (ws.State == WebSocketState.Open)
                    {
                        try
                        {
                            await ws.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Done",
                                CancellationToken.None);
                        }
                        catch
                        {
                            // 忽略关闭错误
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw new Exception("批量推理超时");
                }
            }
        }

        /// <summary>
        /// 本地批量推理：使用 Model.Infer 进行推理，并保存原图/可视化图
        /// 可视化图为并排拼接：左边是 GT（LabelMe 标注），右边是模型推理结果
        /// </summary>
        private async Task RunBatchLocalAsync(
            string srcDir,
            string dstDir,
            double threshold,
            bool saveImg,
            bool saveVis,
            bool openDstDir)
        {
            // 1. 创建输出子目录
            string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            string modelName = "Model";
            try
            {
                var lastPath = Settings.Default.LastModelPath;
                if (!string.IsNullOrEmpty(lastPath))
                {
                    modelName = SanitizeFileName(Path.GetFileNameWithoutExtension(lastPath));
                }
            }
            catch { }

            string outputDir = Path.Combine(dstDir, $"{modelName}_{timestamp}");
            string imgDir = Path.Combine(outputDir, "images");
            string visDir = Path.Combine(outputDir, "visualizations");

            try
            {
                Directory.CreateDirectory(outputDir);
                if (saveImg) Directory.CreateDirectory(imgDir);
                if (saveVis) Directory.CreateDirectory(visDir);
            }
            catch (Exception ex)
            {
                throw new Exception($"无法创建输出目录: {ex.Message}");
            }

            // 2. 获取图片列表
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };
            var imageFiles = Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            int total = imageFiles.Count;
            if (total == 0)
            {
                throw new Exception("文件夹中没有图片！");
            }

            // 从设置读取可视化配置（与主界面预览保持一致）
            var visProperties = new Dictionary<string, object>
            {
                // 画布设置
                { "display_bbox", Settings.Default.ShowBBoxPane },
                { "display_mask", Settings.Default.ShowMaskPane },
                { "display_contours", Settings.Default.ShowContours },
                { "display_text", Settings.Default.ShowTextPane },
                { "display_score", Settings.Default.ShowScorePane },
                { "display_text_shadow", Settings.Default.ShowTextShadowPane },
                { "text_out_of_bbox", Settings.Default.ShowTextOutOfBboxPane },
                // 画笔设置
                { "bbox_line_width", Math.Max(1, (int)Settings.Default.BBoxBorderThickness) },
                { "font_size", Math.Max(6, (int)Settings.Default.FontSize) },
                { "bbox_color", ParseColorToRgbArray(Settings.Default.BBoxBorderColor) },
                { "font_color", ParseColorToRgbArray(Settings.Default.FontColor) }
            };

            // 3. 并行推理并保存（使用 Parallel.ForEach 提升性能）
            int processedCount = 0;
            await Task.Run(() =>
            {
                // 使用并行处理，限制最大并行度为 4
                var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
                Parallel.ForEach(imageFiles, options, (imgPath) =>
                {
                    string baseName = Path.GetFileNameWithoutExtension(imgPath);
                    string ext = Path.GetExtension(imgPath);

                    try
                    {
                        using (var mat = Cv2.ImRead(imgPath, ImreadModes.Color))
                        {
                            if (mat == null || mat.Empty())
                            {
                                System.Diagnostics.Debug.WriteLine($"[批量推理] 无法读取图片: {imgPath}");
                                Interlocked.Increment(ref processedCount);
                                Dispatcher.BeginInvoke(new Action(() => UpdateBatchProgress(processedCount, total)));
                                return;
                            }

                            // 推理
                            var result = model.Infer(mat);

                            // 保存原图
                            if (saveImg)
                            {
                                string imgOutPath = Path.Combine(imgDir, baseName + ext);
                                try { Cv2.ImWrite(imgOutPath, mat); } catch { }
                            }

                            // 可视化并保存（左边：GT 标注，右边：推理结果）
                            if (saveVis)
                            {
                                try
                                {
                                    // 1. 左边：原图 + LabelMe 标注绘制（GT）- 直接在 mat 上绘制以减少 Clone
                                    string labelMePath = Path.ChangeExtension(imgPath, ".json");
                                    using (var leftImage = saveImg ? DrawLabelMeAnnotations(mat.Clone(), labelMePath, visProperties) 
                                                                    : DrawLabelMeAnnotationsInPlace(mat, labelMePath, visProperties))
                                    {
                                        // 2. 右边：原图 + 推理结果绘制（需要重新读取，因为 mat 可能被修改）
                                        using (var matForVis = saveImg ? mat.Clone() : Cv2.ImRead(imgPath, ImreadModes.Color))
                                        {
                                            var visList = Utils.VisualizeResults(
                                                new List<Mat> { matForVis }, result, visProperties);
                                            if (visList != null && visList.Count > 0 && visList[0] != null)
                                            {
                                                using (var rightImage = visList[0])
                                                // 3. 使用 HConcat 高效拼接
                                                using (var combined = ConcatImagesHorizontallyFast(leftImage, rightImage))
                                                {
                                                    // 4. 保存
                                                    string visOutPath = Path.Combine(visDir, baseName + "_vis.png");
                                                    Cv2.ImWrite(visOutPath, combined);
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception visEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[批量推理] 可视化失败: {visEx.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[批量推理] 处理图片失败 {imgPath}: {ex.Message}");
                    }

                    // 线程安全更新进度
                    int currentCount = Interlocked.Increment(ref processedCount);
                    Dispatcher.BeginInvoke(new Action(() => UpdateBatchProgress(currentCount, total)));
                });
            });

            // 4. 打开输出目录
            if (openDstDir)
            {
                try
                {
                    Process.Start("explorer.exe", outputDir);
                }
                catch { }
            }
        }

        /// <summary>
        /// 读取 LabelMe JSON 标注并绘制到图像上（用于显示 GT）
        /// 返回新的 Mat（克隆后绘制）
        /// </summary>
        private static Mat DrawLabelMeAnnotations(Mat image, string jsonPath, Dictionary<string, object> visProperties)
        {
            var result = image.Clone();
            DrawLabelMeAnnotationsCore(result, jsonPath, visProperties);
            return result;
        }

        /// <summary>
        /// 读取 LabelMe JSON 标注并绘制到图像上（原地绘制版本，不做 Clone）
        /// 返回同一个 Mat（直接修改传入的图像）
        /// </summary>
        private static Mat DrawLabelMeAnnotationsInPlace(Mat image, string jsonPath, Dictionary<string, object> visProperties)
        {
            DrawLabelMeAnnotationsCore(image, jsonPath, visProperties);
            return image;
        }

        /// <summary>
        /// 绘制 LabelMe 标注的核心逻辑（不包含克隆）
        /// </summary>
        private static void DrawLabelMeAnnotationsCore(Mat image, string jsonPath, Dictionary<string, object> visProperties)
        {
            if (!File.Exists(jsonPath)) return;

            try
            {
                var json = JObject.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
                var shapes = json["shapes"] as JArray;
                if (shapes == null || shapes.Count == 0) return;

                // 读取可视化配置
                int lineWidth = 2;
                double fontScale = 0.5;
                bool displayText = true;
                bool textOutOfBbox = true;
                bool displayTextShadow = true;
                if (visProperties != null)
                {
                    if (visProperties.TryGetValue("bbox_line_width", out var lw)) lineWidth = Convert.ToInt32(lw);
                    if (visProperties.TryGetValue("font_size", out var fs)) fontScale = Math.Max(0.3, Convert.ToInt32(fs) / 26.0);
                    if (visProperties.TryGetValue("display_text", out var dt)) displayText = Convert.ToBoolean(dt);
                    if (visProperties.TryGetValue("text_out_of_bbox", out var tob)) textOutOfBbox = Convert.ToBoolean(tob);
                    if (visProperties.TryGetValue("display_text_shadow", out var dts)) displayTextShadow = Convert.ToBoolean(dts);
                }

                var bboxColor = new Scalar(0, 255, 0); // 绿色表示 GT
                var fontColor = new Scalar(255, 255, 255);

                foreach (var shape in shapes)
                {
                    if (!(shape is JObject shapeObj)) continue;

                    string label = shapeObj["label"]?.ToString() ?? "";
                    string shapeType = shapeObj["shape_type"]?.ToString() ?? "polygon";
                    var points = shapeObj["points"] as JArray;
                    if (points == null || points.Count == 0) continue;

                    // 解析点坐标
                    var pts = new List<OpenCvSharp.Point>();
                    foreach (var pt in points)
                    {
                        if (pt is JArray ptArr && ptArr.Count >= 2)
                        {
                            int x = (int)Math.Round(ptArr[0].Value<double>());
                            int y = (int)Math.Round(ptArr[1].Value<double>());
                            pts.Add(new OpenCvSharp.Point(x, y));
                        }
                    }

                    if (pts.Count == 0) continue;

                    // 根据 shape_type 绘制
                    if (shapeType == "rectangle" && pts.Count >= 2)
                    {
                        var rect = new OpenCvSharp.Rect(pts[0], new OpenCvSharp.Size(pts[1].X - pts[0].X, pts[1].Y - pts[0].Y));
                        Cv2.Rectangle(image, rect, bboxColor, lineWidth);
                    }
                    else if (shapeType == "polygon" || shapeType == "linestrip")
                    {
                        bool isClosed = (shapeType == "polygon");
                        Cv2.Polylines(image, new[] { pts.ToArray() }, isClosed, bboxColor, lineWidth, LineTypes.AntiAlias);
                    }
                    else if (shapeType == "circle" && pts.Count >= 2)
                    {
                        int radius = (int)Math.Sqrt(Math.Pow(pts[1].X - pts[0].X, 2) + Math.Pow(pts[1].Y - pts[0].Y, 2));
                        Cv2.Circle(image, pts[0], radius, bboxColor, lineWidth);
                    }
                    else if (shapeType == "point" && pts.Count >= 1)
                    {
                        Cv2.Circle(image, pts[0], 3, bboxColor, -1);
                    }
                    else
                    {
                        // 默认当作多边形处理
                        Cv2.Polylines(image, new[] { pts.ToArray() }, true, bboxColor, lineWidth, LineTypes.AntiAlias);
                    }

                    // 绘制标签文字（使用 GDI+ 支持中文）
                    if (displayText && !string.IsNullOrEmpty(label) && pts.Count > 0)
                    {
                        int minX = pts.Min(p => p.X);
                        int minY = pts.Min(p => p.Y);
                        
                        // 计算字体大小（与 VisualizeOnOriginal 一致）
                        float fontSize = (float)(fontScale * 26);
                        int textHeight = (int)Math.Ceiling(fontSize * 1.2);
                        
                        // 根据设置决定文字位置（与 WPF OverlayRenderer 一致）
                        int textX = minX;
                        int textY;
                        if (textOutOfBbox)
                        {
                            // 标签写在框外（上方）
                            textY = minY - textHeight - 2;
                            if (textY < 0) textY = minY + 2; // 超出边界则放框内
                        }
                        else
                        {
                            // 标签写在框内（左上角）
                            textY = minY + 2;
                        }
                        
                        // 使用 GDI+ 绘制文字（支持中文）
                        DrawLabelTextGdiPlus(image, label, textX, textY, fontSize, displayTextShadow);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DrawLabelMeAnnotations] 解析标注失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 水平拼接两张图像（左右并排）- 保留用于兼容
        /// </summary>
        private static Mat ConcatImagesHorizontally(Mat left, Mat right)
        {
            return ConcatImagesHorizontallyFast(left, right);
        }

        /// <summary>
        /// 水平拼接两张图像（优化版：使用 Cv2.HConcat 减少内存复制）
        /// </summary>
        private static Mat ConcatImagesHorizontallyFast(Mat left, Mat right)
        {
            if (left == null || left.Empty()) return right?.Clone() ?? new Mat();
            if (right == null || right.Empty()) return left.Clone();

            // 如果高度相同，直接使用 HConcat（最快，只需 1 次复制）
            if (left.Height == right.Height && left.Type() == right.Type())
            {
                var result = new Mat();
                Cv2.HConcat(new Mat[] { left, right }, result);
                return result;
            }

            // 高度不同时，先 resize 再拼接
            int targetHeight = Math.Max(left.Height, right.Height);
            
            // 准备左图（保持宽高比 resize）
            Mat leftResized;
            if (left.Height != targetHeight)
            {
                double scale = (double)targetHeight / left.Height;
                int newWidth = (int)(left.Width * scale);
                leftResized = new Mat();
                Cv2.Resize(left, leftResized, new OpenCvSharp.Size(newWidth, targetHeight));
            }
            else
            {
                leftResized = left;
            }

            // 准备右图（保持宽高比 resize）
            Mat rightResized;
            if (right.Height != targetHeight)
            {
                double scale = (double)targetHeight / right.Height;
                int newWidth = (int)(right.Width * scale);
                rightResized = new Mat();
                Cv2.Resize(right, rightResized, new OpenCvSharp.Size(newWidth, targetHeight));
            }
            else
            {
                rightResized = right;
            }

            // 使用 HConcat 拼接
            var combined = new Mat();
            Cv2.HConcat(new Mat[] { leftResized, rightResized }, combined);

            // 释放临时创建的 resize Mat
            if (leftResized != left) leftResized.Dispose();
            if (rightResized != right) rightResized.Dispose();

            return combined;
        }

        /// <summary>
        /// 代码开关：是否导出批量预测的 CSV 性能日志（batch_profile.csv）。
        /// 设置为 false 可禁用 CSV 导出。
        /// </summary>
#region CSV开关
        private const bool EnableBatchProfileCsv = false;
#endregion

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

        /// <summary>
        /// 使用 GDI+ 在 Mat 上绘制标注文字（支持中文），用于 GT 标注图绘制。
        /// 绿色文字，与推理结果的橙色区分。
        /// </summary>
        private static void DrawLabelTextGdiPlus(Mat mat, string text, int x, int y, float fontSize, bool withShadow)
        {
            if (mat == null || mat.Empty() || string.IsNullOrEmpty(text)) return;
            if (mat.Channels() != 3) return;

            try
            {
                int width = mat.Cols;
                int height = mat.Rows;
                int stride = (int)mat.Step();

                using (var bmp = new Bitmap(width, height, stride,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb, mat.Data))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    using (var font = new Font("Microsoft YaHei", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        // 测量文字大小
                        var textSize = g.MeasureString(text, font);
                        
                        // 绘制半透明黑色背景
                        using (var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(160, 0, 0, 0)))
                        {
                            g.FillRectangle(bgBrush, x, y, textSize.Width, textSize.Height);
                        }
                        
                        // 绘制文字阴影
                        if (withShadow)
                        {
                            using (var shadowBrush = new SolidBrush(System.Drawing.Color.Black))
                            {
                                g.DrawString(text, font, shadowBrush, x + 1, y + 1);
                            }
                        }
                        
                        // 绘制文字（绿色，表示GT标注）
                        using (var brush = new SolidBrush(System.Drawing.Color.LimeGreen))
                        {
                            g.DrawString(text, font, brush, x, y);
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 将颜色字符串（如 "#FFFF0000" 或 "#FF0000"）解析为 [R, G, B] 对象数组。
        /// 用于传递给 OpenCV 可视化模块（VisualizeOnOriginal）。
        /// 注意：必须返回 object[] 而非 int[]，因为 VisualizeOnOriginal.ReadColor 
        /// 检查的是 IEnumerable&lt;object&gt;，而 int[] 实现的是 IEnumerable&lt;int&gt;。
        /// </summary>
        private static object[] ParseColorToRgbArray(string colorText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(colorText))
                    return new object[] { 255, 0, 0 }; // 默认红色

                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorText);
                return new object[] { (int)color.R, (int)color.G, (int)color.B };
            }
            catch
            {
                return new object[] { 255, 0, 0 }; // 解析失败默认红色
            }
        }
    }
}

