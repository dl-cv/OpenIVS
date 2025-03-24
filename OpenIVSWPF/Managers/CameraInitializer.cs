using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using DLCV.Camera;
using MvCameraControl;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using OpenCvSharp;

namespace OpenIVSWPF.Managers
{
    /// <summary>
    /// 为CameraManager类提供扩展方法
    /// </summary>
    public static class CameraManagerExtensions
    {
        /// <summary>
        /// 手动触发图像更新事件
        /// </summary>
        /// <param name="cameraManager">相机管理器实例</param>
        /// <param name="image">要发送的图像</param>
        public static void RaiseImageUpdatedEvent(this CameraManager cameraManager, Bitmap image)
        {
            // 创建事件参数
            var eventArgs = new ImageEventArgs(image);

            // 获取ImageUpdated事件字段
            var type = cameraManager.GetType();
            var eventField = type.GetField("ImageUpdated", BindingFlags.Instance | BindingFlags.NonPublic);

            // 获取事件委托
            var eventDelegate = eventField?.GetValue(cameraManager) as MulticastDelegate;
            if (eventDelegate != null)
            {
                // 调用所有订阅的事件处理器
                foreach (var handler in eventDelegate.GetInvocationList())
                {
                    try
                    {
                        handler.Method.Invoke(handler.Target, new object[] { cameraManager, eventArgs });
                    }
                    catch { }
                }
            }
        }
    }

    /// <summary>
    /// 相机管理单例类
    /// </summary>
    public class CameraInstance
    {
        #region 单例模式实现
        private static CameraManager _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取CameraInstance的单例实例
        /// </summary>
        public static CameraManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new CameraManager();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion
    }

    /// <summary>
    /// 相机初始化和控制管理类
    /// </summary>
    public class CameraInitializer
    {
        private CameraManager _cameraManager = CameraInstance.Instance;
        private bool _isCameraConnected = false;
        private bool _isGrabbing = false;
        private Action<string> _statusCallback;
        private Action<string> _cameraStatusCallback;
        private Settings _settings; // 添加设置引用

        // 本地图像文件夹相关
        private bool _useLocalFolder = false;
        private string _localFolderPath = string.Empty;
        private List<string> _imageFiles = new List<string>();
        private int _currentImageIndex = 0;
        private object _lock = new object();
        private int _localImageDelay = 500; // 默认间隔为500ms
        private bool _loopLocalImages = true; // 默认循环遍历

        public bool IsConnected => _isCameraConnected || _useLocalFolder;
        public bool IsGrabbing => _isGrabbing;
        public CameraManager CameraManager => _cameraManager;
        public bool UseLocalFolder => _useLocalFolder;

        /// <summary>
        /// 判断图像列表是否已用完（仅在非循环模式下有效）
        /// </summary>
        public bool IsImageListExhausted => _useLocalFolder && _currentImageIndex >= _imageFiles.Count && !_loopLocalImages;

        /// <summary>
        /// 原有的相机图像更新事件（保留用于兼容）
        /// </summary>
        public event EventHandler<ImageEventArgs> ImageUpdated
        {
            add { _cameraManager.ImageUpdated += value; }
            remove { _cameraManager.ImageUpdated -= value; }
        }

        /// <summary>
        /// 统一的图像捕获事件 - 所有模式下都通过这个事件通知外部有新图像
        /// </summary>
        public event EventHandler<ImageEventArgs> ImageCaptured;

        public CameraInitializer(Action<string> statusCallback, Action<string> cameraStatusCallback)
        {
            _statusCallback = statusCallback;
            _cameraStatusCallback = cameraStatusCallback;

            // 订阅相机的原始图像事件，转发为统一的图像捕获事件
            _cameraManager.ImageUpdated += OnCameraManagerImageUpdated;
        }

        /// <summary>
        /// 处理相机原始图像更新事件
        /// </summary>
        private void OnCameraManagerImageUpdated(object sender, ImageEventArgs e)
        {
            if (e.Image != null)
            {
                // 触发统一的图像捕获事件
                RaiseImageCapturedEvent(e.Image.Clone() as Bitmap);
            }
        }

        /// <summary>
        /// 触发统一的图像捕获事件
        /// </summary>
        /// <param name="image">捕获的图像</param>
        private void RaiseImageCapturedEvent(Bitmap image)
        {
            if (image != null)
            {
                // 创建事件参数
                var eventArgs = new ImageEventArgs(image);

                // 触发事件
                ImageCaptured?.Invoke(this, eventArgs);
            }
        }

        /// <summary>
        /// 初始化相机
        /// </summary>
        /// <param name="settings">系统设置</param>
        public void InitializeCamera(Settings settings)
        {
            try
            {
                // 保存设置引用
                _settings = settings;

                // 根据设置决定是使用本地文件夹还是相机
                if (settings.UseLocalFolder)
                {
                    InitializeLocalFolder(settings);
                }
                else
                {
                    InitializeCameraDevice(settings);
                }
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"初始化错误：{ex.Message}");
                _cameraStatusCallback?.Invoke("初始化错误");
                throw;
            }
        }

        /// <summary>
        /// 初始化本地图像文件夹
        /// </summary>
        private void InitializeLocalFolder(Settings settings)
        {
            try
            {
                _statusCallback?.Invoke("正在初始化本地图像文件夹...");

                // 获取文件夹路径
                _localFolderPath = settings.LocalFolderPath;
                _localImageDelay = settings.LocalImageDelay;
                _loopLocalImages = settings.LoopLocalImages;

                if (string.IsNullOrEmpty(_localFolderPath) || !Directory.Exists(_localFolderPath))
                {
                    _statusCallback?.Invoke($"图像文件夹不存在：{_localFolderPath}");
                    _cameraStatusCallback?.Invoke("文件夹不存在");
                    return;
                }

                // 查找所有图片文件
                _imageFiles.Clear();
                _imageFiles.AddRange(Directory.GetFiles(_localFolderPath, "*.jpg", SearchOption.TopDirectoryOnly));
                _imageFiles.AddRange(Directory.GetFiles(_localFolderPath, "*.jpeg", SearchOption.TopDirectoryOnly));
                _imageFiles.AddRange(Directory.GetFiles(_localFolderPath, "*.png", SearchOption.TopDirectoryOnly));
                _imageFiles.AddRange(Directory.GetFiles(_localFolderPath, "*.bmp", SearchOption.TopDirectoryOnly));

                // 按文件名排序
                _imageFiles = _imageFiles.OrderBy(f => f).ToList();

                if (_imageFiles.Count == 0)
                {
                    _statusCallback?.Invoke($"图像文件夹中没有支持的图像文件");
                    _cameraStatusCallback?.Invoke("没有图像文件");
                    return;
                }

                _statusCallback?.Invoke($"已找到 {_imageFiles.Count} 个图像文件");
                _currentImageIndex = 0;
                _useLocalFolder = true;
                _cameraStatusCallback?.Invoke($"本地文件夹: {_imageFiles.Count}张");
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"初始化本地图像文件夹错误：{ex.Message}");
                _cameraStatusCallback?.Invoke("初始化错误");
                throw;
            }
        }

        /// <summary>
        /// 初始化相机设备
        /// </summary>
        private void InitializeCameraDevice(Settings settings)
        {
            _statusCallback?.Invoke("正在初始化相机...");

            // 刷新设备列表
            List<IDeviceInfo> deviceList = _cameraManager.RefreshDeviceList();

            if (deviceList.Count == 0)
            {
                _statusCallback?.Invoke("未检测到相机设备");
                _cameraStatusCallback?.Invoke("未检测到相机");
                return;
            }

            // 检查相机索引是否有效
            int cameraIndex = settings.CameraIndex;
            if (cameraIndex < 0 || cameraIndex >= deviceList.Count)
            {
                cameraIndex = 0;
            }

            // 连接选中的相机
            bool success = _cameraManager.ConnectDevice(cameraIndex);
            if (success)
            {
                _isCameraConnected = true;
                _statusCallback?.Invoke($"相机已连接：{deviceList[cameraIndex].UserDefinedName}");
                _cameraStatusCallback?.Invoke("已连接");

                // 设置触发模式
                if (settings.UseTrigger)
                {
                    TriggerConfig.TriggerMode mode = settings.UseSoftTrigger
                        ? TriggerConfig.TriggerMode.Software
                        : TriggerConfig.TriggerMode.Line0;

                    _cameraManager.SetTriggerMode(mode);
                    _cameraManager.StartGrabbing();
                    _statusCallback?.Invoke($"相机设置为{(settings.UseSoftTrigger ? "软触发" : "硬触发")}模式");
                }
                else
                {
                    // 关闭触发模式
                    _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Off);

                    // 开始抓取图像
                    if (_cameraManager.StartGrabbing())
                    {
                        _isGrabbing = true;
                        _statusCallback?.Invoke("相机设置为连续采集模式");
                    }
                }
            }
            else
            {
                _statusCallback?.Invoke("相机连接失败");
                _cameraStatusCallback?.Invoke("连接失败");
            }
        }

        /// <summary>
        /// 加载并发送下一张图像
        /// </summary>
        private Bitmap LoadAndSendNextImage()
        {
            try
            {
                if (_imageFiles.Count == 0)
                    return null;

                // 检查索引是否已到末尾
                if (_currentImageIndex >= _imageFiles.Count)
                {
                    // 如果不循环遍历，则直接返回null
                    if (!_loopLocalImages)
                    {
                        _statusCallback?.Invoke("已到达图像列表末尾，停止遍历");
                        return null;
                    }

                    // 如果循环遍历，则重置索引
                    _currentImageIndex = 0;
                    _statusCallback?.Invoke("图像列表已遍历完毕，重新开始");
                }

                // 读取图像文件
                lock (_lock)
                {
                    if (_currentImageIndex < _imageFiles.Count)
                    {
                        string imagePath = _imageFiles[_currentImageIndex];
                        _currentImageIndex++;

                        try
                        {
                            // 使用OpenCV读取三通道图像
                            using (var mat = new Mat(imagePath))
                            {
                                if (mat.Empty())
                                {
                                    _statusCallback?.Invoke($"无法读取图像文件：{imagePath}");
                                    return null;
                                }

                                // 转换为Bitmap并返回
                                var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);
                                return bitmap;
                            }
                        }
                        catch (Exception ex)
                        {
                            _statusCallback?.Invoke($"加载图像文件错误：{ex.Message}");
                            return null;
                        }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 触发相机拍照
        /// </summary>
        /// <param name="token">取消令牌</param>
        /// <param name="lastCapturedImage">上一次捕获的图像</param>
        /// <param name="settings">系统设置</param>
        /// <returns>捕获的图像</returns>
        public async Task<Bitmap> CaptureImageAsync(System.Threading.CancellationToken token, Bitmap lastCapturedImage, Settings settings)
        {
            try
            {
                _statusCallback?.Invoke("正在捕获图像...");

                if (!IsConnected)
                {
                    _statusCallback?.Invoke("相机未连接且未配置本地图像，无法捕获图像");
                    return null;
                }

                // 处理本地图像文件夹模式
                if (_useLocalFolder)
                {
                    // 确保每次都获取新图像，不使用lastCapturedImage
                    return await CaptureLocalImageAsync(token, null);
                }

                // 处理相机模式，保持原有逻辑
                // 根据触发模式执行不同的捕获逻辑
                if (settings.UseTrigger)
                {
                    // 使用TaskCompletionSource等待图像更新事件
                    var tcs = new TaskCompletionSource<Bitmap>();

                    // 设置图像捕获事件处理
                    EventHandler<ImageEventArgs> handler = null;
                    handler = (s, e) =>
                    {
                        // 捕获到图像后，转换为Bitmap
                        if (e.Image != null)
                        {
                            tcs.TrySetResult(e.Image.Clone() as Bitmap);
                        }

                        // 移除事件处理器，防止多次触发
                        ImageCaptured -= handler;
                    };

                    // 添加事件处理
                    ImageCaptured += handler;

                    // 执行软触发
                    _cameraManager.TriggerOnce();

                    // 添加超时处理，1秒内如果没有图像返回，则取消
                    using (var timeoutCts = new System.Threading.CancellationTokenSource(1000))
                    using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
                    {
                        // 注册取消操作
                        linkedCts.Token.Register(() =>
                        {
                            ImageCaptured -= handler;
                            tcs.TrySetCanceled();
                        });

                        // 等待图像或取消
                        try
                        {
                            var capturedImage = await tcs.Task;
                            _statusCallback?.Invoke("图像捕获成功");
                            return capturedImage;
                        }
                        catch (System.Threading.Tasks.TaskCanceledException)
                        {
                            _statusCallback?.Invoke("图像捕获超时或被取消");
                            return null;
                        }
                    }
                }
                else
                {
                    // 非触发模式：使用最新的图像
                    if (lastCapturedImage != null)
                    {
                        _statusCallback?.Invoke("使用最新捕获的图像");
                        return lastCapturedImage.Clone() as Bitmap;
                    }
                    else
                    {
                        _statusCallback?.Invoke("等待首次图像捕获...");

                        // 等待图像
                        var tcs = new TaskCompletionSource<Bitmap>();

                        // 设置图像捕获事件处理
                        EventHandler<ImageEventArgs> handler = null;
                        handler = (s, e) =>
                        {
                            // 捕获到图像后，转换为Bitmap
                            if (e.Image != null)
                            {
                                tcs.TrySetResult(e.Image.Clone() as Bitmap);
                            }

                            // 移除事件处理器，防止多次触发
                            ImageCaptured -= handler;
                        };

                        // 添加事件处理
                        ImageCaptured += handler;

                        // 添加超时处理，5秒内如果没有图像返回，则取消
                        using (var timeoutCts = new System.Threading.CancellationTokenSource(5000))
                        using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
                        {
                            // 注册取消操作
                            linkedCts.Token.Register(() =>
                            {
                                ImageCaptured -= handler;
                                tcs.TrySetCanceled();
                            });

                            // 等待图像或取消
                            try
                            {
                                var capturedImage = await tcs.Task;
                                _statusCallback?.Invoke("图像捕获成功");
                                return capturedImage;
                            }
                            catch (System.Threading.Tasks.TaskCanceledException)
                            {
                                _statusCallback?.Invoke("图像捕获超时或被取消");
                                return null;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"图像捕获过程中发生错误：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 捕获本地文件夹中的图像
        /// </summary>
        private async Task<Bitmap> CaptureLocalImageAsync(CancellationToken token, Bitmap lastCapturedImage)
        {
            try
            {
                // 离线模式下，每次调用都加载新图像，不使用lastCapturedImage
                lastCapturedImage = null;

                // 如果没有图像文件，返回null
                if (_imageFiles.Count == 0)
                {
                    _statusCallback?.Invoke("本地文件夹中没有图像文件");
                    return null;
                }

                // 应用图像延迟
                if (_localImageDelay > 0)
                {
                    _statusCallback?.Invoke($"等待图像间隔时间 {_localImageDelay}ms...");
                    await Task.Delay(_localImageDelay, token);
                }

                // 获取下一张图像
                Bitmap bitmap = LoadAndSendNextImage();
                if (bitmap != null)
                {
                    // 通过统一的事件通知图像捕获
                    RaiseImageCapturedEvent(bitmap);

                    _statusCallback?.Invoke($"已加载图像: {_currentImageIndex}/{_imageFiles.Count}");
                    return bitmap;
                }
                else
                {
                    // 无法加载下一张图像，检查是否因为已到末尾且不循环遍历
                    if (_currentImageIndex >= _imageFiles.Count && !_loopLocalImages)
                    {
                        _statusCallback?.Invoke("图像列表已遍历完毕，不再循环遍历");
                    }
                    else
                    {
                        _statusCallback?.Invoke("无法加载下一张图像");
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"本地图像捕获过程中发生错误：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 手动触发本地图像
        /// </summary>
        /// <returns>加载的图像，若失败则返回null</returns>
        public Bitmap TriggerLocalImage()
        {
            if (_useLocalFolder && _imageFiles.Count > 0)
            {
                // 加载下一张图像
                Bitmap bitmap = LoadAndSendNextImage();
                if (bitmap != null)
                {
                    // 通过统一的事件通知图像捕获
                    RaiseImageCapturedEvent(bitmap);

                    return bitmap;
                }
            }
            return null;
        }

        /// <summary>
        /// 关闭相机连接
        /// </summary>
        public void Close()
        {
            if (_isGrabbing)
            {
                _cameraManager?.StopGrabbing();
                _isGrabbing = false;
            }

            if (_isCameraConnected)
            {
                _cameraManager?.DisconnectDevice();
                _isCameraConnected = false;
            }

            // 停止本地图像流
            if (_useLocalFolder)
            {
                // 重置图像索引，以便下次启动时从头开始
                ResetImageIndex();
                _useLocalFolder = false;
            }
        }

        /// <summary>
        /// 重置图像索引，使得下次从第一张图像开始处理
        /// </summary>
        public void ResetImageIndex()
        {
            _currentImageIndex = 0;
            _statusCallback?.Invoke("已重置图像索引，下次将从第一张图像开始");
        }
    }
}