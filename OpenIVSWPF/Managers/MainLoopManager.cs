using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace OpenIVSWPF.Managers
{
    /// <summary>
    /// 主循环逻辑管理类
    /// </summary>
    public class MainLoopManager
    {
        private ModbusManager _modbusInitializer;
        private CameraInitializer _cameraInitializer;
        private ModelManager _modelManager;
        private Settings _settings;
        private Action<string> _statusCallback;
        private Action<string> _detectionResultCallback;
        private Action<bool> _statisticsCallback;
        private Func<Bitmap, bool, Task> _saveImageCallback;

        // 位置序列定义 (1-2-3-2-1循环)
        private readonly float[] _positionSequence = new float[] { 195, 305, 415, 305 };
        private int _currentPositionIndex = 0;

        public MainLoopManager(
            ModbusManager modbusInitializer, 
            CameraInitializer cameraInitializer, 
            ModelManager modelManager, 
            Settings settings,
            Action<string> statusCallback,
            Action<string> detectionResultCallback,
            Action<bool> statisticsCallback,
            Func<Bitmap, bool, Task> saveImageCallback)
        {
            _modbusInitializer = modbusInitializer;
            _cameraInitializer = cameraInitializer;
            _modelManager = modelManager;
            _settings = settings;
            _statusCallback = statusCallback;
            _detectionResultCallback = detectionResultCallback;
            _statisticsCallback = statisticsCallback;
            _saveImageCallback = saveImageCallback;
        }

        /// <summary>
        /// 运行主循环
        /// </summary>
        /// <param name="token">取消令牌</param>
        /// <param name="lastCapturedImage">上次捕获的图像</param>
        /// <returns>任务</returns>
        public async Task RunMainLoopAsync(CancellationToken token, Bitmap lastCapturedImage)
        {
            try
            {
                // 判断是否是离线模式
                bool isOfflineMode = _settings.UseLocalFolder;
                
                // 持续运行，直到取消
                while (!token.IsCancellationRequested)
                {
                    // 获取当前位置索引
                    float targetPosition = _positionSequence[_currentPositionIndex];
                    
                    bool moveResult = true;
                    
                    // 在线模式下才执行移动操作
                    if (!isOfflineMode)
                    {
                        // 移动到目标位置
                        moveResult = await _modbusInitializer.MoveToPositionAsync(targetPosition, token);
                    }
                    else
                    {
                        // 离线模式下提示当前位置
                        _statusCallback?.Invoke($"离线模式: 模拟位置 {targetPosition}");
                    }

                    if (moveResult && !token.IsCancellationRequested)
                    {
                        // 离线模式下，无论是否是触发模式，都执行拍照和推理
                        // 在线模式下，只有在触发模式才拍照和推理
                        if (isOfflineMode || _settings.UseTrigger)
                        {
                            // 触发相机拍照
                            _statusCallback?.Invoke($"在{(isOfflineMode ? "模拟" : "")}位置 {targetPosition} 进行拍照...");

                            try
                            {
                                // 等待运动稳定
                                if (!isOfflineMode)
                                {
                                    await Task.Delay(_settings.PreCaptureDelay, token);
                                }

                                // 等待拍照操作完成
                                var image = await _cameraInitializer.CaptureImageAsync(token, lastCapturedImage, _settings);

                                // 检查图像是否为空
                                if (image == null && isOfflineMode && !_settings.LoopLocalImages)
                                {
                                    // 离线模式下，非循环遍历模式，且图像为空，说明图像已用完
                                    _statusCallback?.Invoke("离线模式: 所有图像已处理完毕，停止检测。");
                                    return; // 退出方法，停止检测
                                }

                                if (image != null && !token.IsCancellationRequested)
                                {
                                    // 更新lastCapturedImage
                                    lastCapturedImage = image.Clone() as Bitmap;
                                    
                                    // 拍照完成后，异步处理图像（不等待完成）
                                    _ = Task.Run(() =>
                                    {
                                        try
                                        {
                                            // 获取到图像后，执行AI推理
                                            _statusCallback?.Invoke("执行AI推理...");
                                            string result = _modelManager.PerformInference(image);

                                            // 更新检测结果显示
                                            _detectionResultCallback?.Invoke(result);

                                            // 判断检测结果
                                            bool isOK = string.IsNullOrEmpty(result);

                                            // 更新统计信息
                                            _statisticsCallback?.Invoke(isOK);

                                            // 根据设置保存图像
                                            _ = _saveImageCallback?.Invoke(image, isOK);

                                            // 添加完成信息
                                            _statusCallback?.Invoke($"{(isOfflineMode ? "离线模式: " : "")}位置 {targetPosition} 的推理和保存已完成");
                                        }
                                        catch (Exception ex)
                                        {
                                            _statusCallback?.Invoke($"图像处理过程中发生错误：{ex.Message}");
                                        }
                                    });

                                    // 提示拍照已完成
                                    _statusCallback?.Invoke($"{(isOfflineMode ? "离线模式: " : "")}位置 {targetPosition} 的拍照已完成，准备移动到下一位置");
                                }
                            }
                            catch (Exception ex)
                            {
                                if (!token.IsCancellationRequested)
                                {
                                    _statusCallback?.Invoke($"拍照过程中发生错误：{ex.Message}");
                                }
                            }
                        }
                    }

                    if (token.IsCancellationRequested)
                        break;

                    // 更新位置索引，实现1-2-3-2-1循环
                    _currentPositionIndex = (_currentPositionIndex + 1) % _positionSequence.Length;
                    
                    // 在离线模式下，添加一个延迟，模拟移动时间
                    if (isOfflineMode)
                    {
                        try
                        {
                            await Task.Delay(500, token); // 500ms延迟
                        }
                        catch (OperationCanceledException)
                        {
                            break; // 循环被取消
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 操作被取消，正常退出
                _statusCallback?.Invoke("运行被取消");
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"运行过程中发生错误：{ex.Message}");
            }
            finally
            {
                if (!_settings.UseLocalFolder)
                {
                    _modbusInitializer.SendStopCommand();
                }
            }
        }

        /// <summary>
        /// 保存图像方法
        /// </summary>
        /// <param name="image">要保存的图像</param>
        /// <param name="isOK">是否合格</param>
        public async Task SaveImageAsync(Bitmap image, bool isOK)
        {
            await Task.Run(() => SaveImage(image, isOK));
        }

        /// <summary>
        /// 保存图像方法
        /// </summary>
        /// <param name="image">要保存的图像</param>
        /// <param name="isOK">是否合格</param>
        private void SaveImage(Bitmap image, bool isOK)
        {
            try
            {
                // 根据设置判断是否需要保存图像
                if ((isOK && !_settings.SaveOKImage) || (!isOK && !_settings.SaveNGImage))
                    return;

                // 确保保存路径存在
                if (string.IsNullOrEmpty(_settings.SavePath))
                    return;

                // 获取当前日期和时间
                string currentDate = DateTime.Now.ToString("yyyyMMdd");
                string timeString = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

                // 结果文件夹
                string resultFolder = isOK ? "OK" : "NG";

                // 创建日期和结果文件夹
                string dateFolder = Path.Combine(_settings.SavePath, currentDate);
                string resultPath = Path.Combine(dateFolder, resultFolder);

                if (!Directory.Exists(dateFolder))
                    Directory.CreateDirectory(dateFolder);

                if (!Directory.Exists(resultPath))
                    Directory.CreateDirectory(resultPath);

                // 生成文件名
                string extension = _settings.ImageFormat.ToLower();
                string filename = $"{timeString}.{extension}";
                string fullPath = Path.Combine(resultPath, filename);

                // 使用OpenCV保存图像
                using (var mat = BitmapConverter.ToMat(image))
                {
                    if (extension == "jpg")
                    {
                        // 获取JPG质量设置
                        int quality = 90;
                        if (!string.IsNullOrEmpty(_settings.JpegQuality) && int.TryParse(_settings.JpegQuality, out int jpegQuality))
                        {
                            quality = Math.Min(100, Math.Max(1, jpegQuality)); // 确保在1-100范围内
                        }

                        // 保存为JPG格式
                        var parameters = new int[]
                        {
                            (int)OpenCvSharp.ImwriteFlags.JpegQuality, quality,
                            (int)OpenCvSharp.ImwriteFlags.JpegProgressive, 1
                        };
                        OpenCvSharp.Cv2.ImWrite(fullPath, mat, parameters);
                    }
                    else // BMP格式
                    {
                        OpenCvSharp.Cv2.ImWrite(fullPath, mat);
                    }
                }

                _statusCallback?.Invoke($"图像已保存到: {fullPath}");
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"保存图像时发生错误: {ex.Message}");
            }
        }
    }
} 