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
        
        // 本地图像文件夹相关
        private bool _useLocalFolder = false;
        private string _localFolderPath = string.Empty;
        private List<string> _imageFiles = new List<string>();
        private int _currentImageIndex = 0;
        private bool _loopImages = true;
        private CancellationTokenSource _localImagesCts;
        private Task _localImagesTask;
        private Timer _imageTimer;
        private object _lock = new object();

        public bool IsConnected => _isCameraConnected || _useLocalFolder;
        public bool IsGrabbing => _isGrabbing;
        public CameraManager CameraManager => _cameraManager;
        public bool UseLocalFolder => _useLocalFolder;

        public event EventHandler<ImageEventArgs> ImageUpdated 
        {
            add { _cameraManager.ImageUpdated += value; }
            remove { _cameraManager.ImageUpdated -= value; }
        }

        public CameraInitializer(Action<string> statusCallback, Action<string> cameraStatusCallback)
        {
            _statusCallback = statusCallback;
            _cameraStatusCallback = cameraStatusCallback;
        }

        /// <summary>
        /// 初始化相机
        /// </summary>
        /// <param name="settings">系统设置</param>
        public void InitializeCamera(Settings settings)
        {
            try
            {
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
                _loopImages = settings.LoopImages;
                _useLocalFolder = true;
                _cameraStatusCallback?.Invoke($"本地文件夹: {_imageFiles.Count}张");
                
                // 初始化成功后，根据触发模式决定是否自动开始传输图像
                if (!settings.UseTrigger)
                {
                    StartLocalImageStream();
                }
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
        /// 开始本地图像流
        /// </summary>
        private void StartLocalImageStream()
        {
            if (_imageFiles.Count == 0 || _localImagesTask != null)
                return;

            _isGrabbing = true;
            _localImagesCts = new CancellationTokenSource();
            
            // 创建一个定时器，定期加载并发送图像
            _imageTimer = new Timer(LoadAndSendNextImage, null, 0, 1000); // 每秒一张图片
        }

        /// <summary>
        /// 加载并发送下一张图像
        /// </summary>
        private void LoadAndSendNextImage(object state)
        {
            try
            {
                if (_imageFiles.Count == 0 || _currentImageIndex >= _imageFiles.Count)
                {
                    if (_loopImages)
                    {
                        _currentImageIndex = 0;
                    }
                    else
                    {
                        StopLocalImageStream();
                        return;
                    }
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
                            using (Bitmap image = new Bitmap(imagePath))
                            {
                                // 克隆图像，因为原始Bitmap会在using块结束时被释放
                                Bitmap clonedImage = new Bitmap(image);
                                // 发送图像事件
                                _cameraManager.RaiseImageUpdatedEvent(clonedImage);
                            }
                        }
                        catch (Exception ex)
                        {
                            _statusCallback?.Invoke($"加载图像文件错误：{ex.Message}");
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 停止本地图像流
        /// </summary>
        private void StopLocalImageStream()
        {
            if (_imageTimer != null)
            {
                _imageTimer.Dispose();
                _imageTimer = null;
            }

            _isGrabbing = false;
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
                    return await CaptureLocalImageAsync(token, lastCapturedImage);
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
                        _cameraManager.ImageUpdated -= handler;
                    };

                    // 添加事件处理
                    _cameraManager.ImageUpdated += handler;

                    // 执行软触发
                    _cameraManager.TriggerOnce();

                    // 添加超时处理，1秒内如果没有图像返回，则取消
                    using (var timeoutCts = new System.Threading.CancellationTokenSource(1000))
                    using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
                    {
                        // 注册取消操作
                        linkedCts.Token.Register(() =>
                        {
                            _cameraManager.ImageUpdated -= handler;
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
                            _cameraManager.ImageUpdated -= handler;
                        };

                        // 添加事件处理
                        _cameraManager.ImageUpdated += handler;

                        // 添加超时处理，5秒内如果没有图像返回，则取消
                        using (var timeoutCts = new System.Threading.CancellationTokenSource(5000))
                        using (var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
                        {
                            // 注册取消操作
                            linkedCts.Token.Register(() =>
                            {
                                _cameraManager.ImageUpdated -= handler;
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
                // 如果有上次捕获的图像，直接返回
                if (lastCapturedImage != null)
                {
                    _statusCallback?.Invoke("使用最新捕获的图像");
                    return lastCapturedImage.Clone() as Bitmap;
                }

                // 如果没有图像文件，返回null
                if (_imageFiles.Count == 0)
                {
                    _statusCallback?.Invoke("本地文件夹中没有图像文件");
                    return null;
                }

                // 获取下一张图像
                lock (_lock)
                {
                    if (_currentImageIndex >= _imageFiles.Count)
                    {
                        if (_loopImages)
                        {
                            _currentImageIndex = 0;
                        }
                        else
                        {
                            _statusCallback?.Invoke("已到达图像文件末尾");
                            return null;
                        }
                    }

                    string imagePath = _imageFiles[_currentImageIndex];
                    _currentImageIndex++;

                    try
                    {
                        _statusCallback?.Invoke($"加载图像文件：{Path.GetFileName(imagePath)}");
                        using (Bitmap image = new Bitmap(imagePath))
                        {
                            // 克隆图像，因为原始Bitmap会在using块结束时被释放
                            return new Bitmap(image);
                        }
                    }
                    catch (Exception ex)
                    {
                        _statusCallback?.Invoke($"加载图像文件错误：{ex.Message}");
                        return null;
                    }
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
        public void TriggerLocalImage()
        {
            if (_useLocalFolder && _imageFiles.Count > 0)
            {
                LoadAndSendNextImage(null);
            }
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
                StopLocalImageStream();
                _useLocalFolder = false;
            }
        }
    }
} 