using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using DLCV.Camera;

namespace OpenIVSWPF.Managers
{
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

        public bool IsConnected => _isCameraConnected;
        public bool IsGrabbing => _isGrabbing;
        public CameraManager CameraManager => _cameraManager;

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
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"相机初始化错误：{ex.Message}");
                _cameraStatusCallback?.Invoke("初始化错误");
                throw;
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

                if (!_isCameraConnected)
                {
                    _statusCallback?.Invoke("相机未连接，无法捕获图像");
                    return null;
                }

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
        }
    }
} 