using System;
using System.Collections.Generic;
using System.Threading;
using System.Drawing;
using System.Threading.Tasks;
using System.Diagnostics;
using MvCameraControl;

namespace DLCV.Camera
{
    #region 设备信息包装类

    /// <summary>
    /// 相机设备信息包装类，提供对设备基本信息的访问
    /// </summary>
    public class DeviceInfoWrapper
    {
        internal IDeviceInfo OriginalDeviceInfo { get; private set; }

        /// <summary>
        /// 制造商名称
        /// </summary>
        public string ManufacturerName { get; private set; }

        /// <summary>
        /// 型号
        /// </summary>
        public string ModelName { get; private set; }

        /// <summary>
        /// 序列号
        /// </summary>
        public string SerialNumber { get; private set; }

        /// <summary>
        /// 设备类型
        /// </summary>
        public string DeviceType { get; private set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="deviceInfo">原始设备信息</param>
        internal DeviceInfoWrapper(IDeviceInfo deviceInfo)
        {
            OriginalDeviceInfo = deviceInfo;
            ManufacturerName = deviceInfo.ManufacturerName;
            ModelName = deviceInfo.ModelName;
            SerialNumber = deviceInfo.SerialNumber;
            
            switch (deviceInfo.TLayerType)
            {
                case DeviceTLayerType.MvGigEDevice:
                    DeviceType = "GigE";
                    break;
                case DeviceTLayerType.MvUsbDevice:
                    DeviceType = "USB";
                    break;
                case DeviceTLayerType.MvGenTLGigEDevice:
                    DeviceType = "GenTL GigE";
                    break;
                case DeviceTLayerType.MvCameraLinkDevice:
                    DeviceType = "CameraLink";
                    break;
                default:
                    DeviceType = "其他";
                    break;
            }
        }

        /// <summary>
        /// 获取设备的完整显示名称
        /// </summary>
        /// <returns>设备显示名称</returns>
        public override string ToString()
        {
            return $"{ManufacturerName} {ModelName} ({SerialNumber})";
        }
    }

    #endregion

    #region 接口定义

    /// <summary>
    /// 相机设备接口，定义相机操作的基本功能
    /// </summary>
    public interface ICameraDevice : IDisposable
    {
        /// <summary>
        /// 获取设备信息
        /// </summary>
        IDeviceInfo DeviceInfo { get; }

        /// <summary>
        /// 设备是否已打开
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// 设备是否正在采集图像
        /// </summary>
        bool IsGrabbing { get; }

        /// <summary>
        /// 图像更新事件
        /// </summary>
        event EventHandler<ImageEventArgs> ImageUpdated;

        /// <summary>
        /// 图像捕获事件(单次采集)
        /// </summary>
        event EventHandler<ImageEventArgs> ImageCaptured;

        /// <summary>
        /// 打开设备
        /// </summary>
        void Open();

        /// <summary>
        /// 关闭设备
        /// </summary>
        void Close();

        /// <summary>
        /// 开始采集图像
        /// </summary>
        void StartGrabbing();

        /// <summary>
        /// 停止采集图像
        /// </summary>
        void StopGrabbing();

        /// <summary>
        /// 设置触发模式
        /// </summary>
        /// <param name="config">触发配置</param>
        void SetTriggerMode(TriggerConfig config);

        /// <summary>
        /// 执行一次软触发
        /// </summary>
        void TriggerOnce();

        /// <summary>
        /// 开始连续触发
        /// </summary>
        /// <param name="intervalMs">触发间隔(毫秒)</param>
        Task StartContinuousTriggerAsync(int intervalMs);

        /// <summary>
        /// 停止连续触发
        /// </summary>
        void StopContinuousTrigger();

        /// <summary>
        /// 设置参数
        /// </summary>
        /// <param name="name">参数名称</param>
        /// <param name="value">参数值</param>
        /// <returns>是否设置成功</returns>
        bool SetParameter(string name, object value);

        /// <summary>
        /// 获取参数
        /// </summary>
        /// <typeparam name="T">参数类型</typeparam>
        /// <param name="name">参数名称</param>
        /// <returns>参数值</returns>
        T GetParameter<T>(string name);
    }

    #endregion

    #region 事件参数

    /// <summary>
    /// 图像事件参数
    /// </summary>
    public class ImageEventArgs : EventArgs
    {
        /// <summary>
        /// 图像
        /// </summary>
        public Bitmap Image { get; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="image">图像</param>
        public ImageEventArgs(Bitmap image)
        {
            Image = image;
            Timestamp = DateTime.Now;
        }
    }

    #endregion

    #region 配置类

    /// <summary>
    /// 相机触发配置
    /// </summary>
    public class TriggerConfig
    {
        /// <summary>
        /// 触发模式枚举
        /// </summary>
        public enum TriggerMode
        {
            /// <summary>
            /// 关闭触发模式
            /// </summary>
            Off,

            /// <summary>
            /// 软触发模式
            /// </summary>
            Software,

            /// <summary>
            /// 线触发模式 - Line0
            /// </summary>
            Line0,

            /// <summary>
            /// 线触发模式 - Line1
            /// </summary>
            Line1,

            /// <summary>
            /// 线触发模式 - Line2
            /// </summary>
            Line2,

            /// <summary>
            /// 线触发模式 - Line3
            /// </summary>
            Line3,

            /// <summary>
            /// 计数器触发
            /// </summary>
            Counter
        }

        /// <summary>
        /// 触发模式
        /// </summary>
        public TriggerMode Mode { get; set; } = TriggerMode.Off;

        /// <summary>
        /// 构造函数
        /// </summary>
        public TriggerConfig() { }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="mode">触发模式</param>
        public TriggerConfig(TriggerMode mode)
        {
            Mode = mode;
        }

        /// <summary>
        /// 获取触发模式字符串
        /// </summary>
        /// <returns>触发模式字符串</returns>
        public string GetModeString()
        {
            switch (Mode)
            {
                case TriggerMode.Off:
                    return "Off";
                case TriggerMode.Software:
                    return "Software";
                case TriggerMode.Line0:
                    return "Line0";
                case TriggerMode.Line1:
                    return "Line1";
                case TriggerMode.Line2:
                    return "Line2";
                case TriggerMode.Line3:
                    return "Line3";
                case TriggerMode.Counter:
                    return "Counter";
                default:
                    return "Off";
            }
        }
    }

    /// <summary>
    /// 相机采集配置
    /// </summary>
    public class GrabConfig
    {
        /// <summary>
        /// 采集模式
        /// </summary>
        public string AcquisitionMode { get; set; } = "Continuous";

        /// <summary>
        /// 包大小（GigE相机专用）
        /// </summary>
        public bool EnableOptimalPacketSize { get; set; } = true;
    }

    #endregion

    #region 工具类

    /// <summary>
    /// 相机工具类，提供设备发现和创建等静态方法
    /// </summary>
    public static class CameraUtils
    {
        /// <summary>
        /// 枚举所有设备
        /// </summary>
        /// <returns>设备信息包装列表</returns>
        public static List<DeviceInfoWrapper> EnumerateDevices()
        {
            // 支持的设备类型
            DeviceTLayerType enumTLayerType = DeviceTLayerType.MvGigEDevice | DeviceTLayerType.MvUsbDevice
                | DeviceTLayerType.MvGenTLGigEDevice | DeviceTLayerType.MvGenTLCXPDevice 
                | DeviceTLayerType.MvGenTLCameraLinkDevice | DeviceTLayerType.MvGenTLXoFDevice;

            List<IDeviceInfo> deviceInfoList = new List<IDeviceInfo>();
            int nRet = DeviceEnumerator.EnumDevices(enumTLayerType, out deviceInfoList);
            if (nRet != MvError.MV_OK)
            {
                throw new Exception($"枚举设备失败，错误码: {nRet}");
            }

            // 转换为包装类列表
            List<DeviceInfoWrapper> wrapperList = new List<DeviceInfoWrapper>();
            foreach (var deviceInfo in deviceInfoList)
            {
                wrapperList.Add(new DeviceInfoWrapper(deviceInfo));
            }

            return wrapperList;
        }

        /// <summary>
        /// 创建相机设备
        /// </summary>
        /// <param name="deviceInfo">设备信息包装类</param>
        /// <returns>相机设备</returns>
        public static ICameraDevice CreateDevice(DeviceInfoWrapper deviceInfo)
        {
            if (deviceInfo == null)
                throw new ArgumentNullException(nameof(deviceInfo));

            return new CameraDevice(deviceInfo.OriginalDeviceInfo);
        }

        /// <summary>
        /// 创建相机设备
        /// </summary>
        /// <param name="index">设备索引</param>
        /// <returns>相机设备</returns>
        public static ICameraDevice CreateDevice(int index)
        {
            var devices = EnumerateDevicesInternal();
            if (index < 0 || index >= devices.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "设备索引超出范围");
            }

            return new CameraDevice(devices[index]);
        }

        // 内部方法，用于获取原始设备列表
        private static List<IDeviceInfo> EnumerateDevicesInternal()
        {
            DeviceTLayerType enumTLayerType = DeviceTLayerType.MvGigEDevice | DeviceTLayerType.MvUsbDevice
                | DeviceTLayerType.MvGenTLGigEDevice | DeviceTLayerType.MvGenTLCXPDevice 
                | DeviceTLayerType.MvGenTLCameraLinkDevice | DeviceTLayerType.MvGenTLXoFDevice;

            List<IDeviceInfo> deviceInfoList = new List<IDeviceInfo>();
            int nRet = DeviceEnumerator.EnumDevices(enumTLayerType, out deviceInfoList);
            if (nRet != MvError.MV_OK)
            {
                throw new Exception($"枚举设备失败，错误码: {nRet}");
            }

            return deviceInfoList;
        }

        /// <summary>
        /// 释放SDK资源
        /// </summary>
        public static void ReleaseSDK()
        {
            SDKSystem.Finalize();
        }
    }

    #endregion

    #region 设备实现

    /// <summary>
    /// 相机设备实现类
    /// </summary>
    public class CameraDevice : ICameraDevice
    {
        private IDevice _device;
        private CancellationTokenSource _grabCts;
        private Thread _grabThread;
        private CancellationTokenSource _triggerCts;
        private bool _isDisposed;

        /// <summary>
        /// 设备信息
        /// </summary>
        public IDeviceInfo DeviceInfo { get; private set; }

        /// <summary>
        /// 设备是否已打开
        /// </summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// 设备是否正在采集图像
        /// </summary>
        public bool IsGrabbing { get; private set; }

        /// <summary>
        /// 图像更新事件
        /// </summary>
        public event EventHandler<ImageEventArgs> ImageUpdated;

        /// <summary>
        /// 图像捕获事件
        /// </summary>
        public event EventHandler<ImageEventArgs> ImageCaptured;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="deviceInfo">设备信息</param>
        public CameraDevice(IDeviceInfo deviceInfo)
        {
            DeviceInfo = deviceInfo ?? throw new ArgumentNullException(nameof(deviceInfo));
        }

        /// <summary>
        /// 打开设备
        /// </summary>
        public void Open()
        {
            if (IsOpen)
                return;

            try
            {
                // 创建设备
                _device = DeviceFactory.CreateDevice(DeviceInfo);
                if (_device == null)
                {
                    throw new Exception("创建设备失败");
                }

                // 打开设备
                int result = _device.Open();
                if (result != MvError.MV_OK)
                {
                    throw new Exception($"打开设备失败，错误码: {result}");
                }

                IsOpen = true;

                // 如果是GigE设备，设置最佳包大小
                if (_device is IGigEDevice)
                {
                    IGigEDevice gigEDevice = _device as IGigEDevice;
                    int optionPacketSize;
                    result = gigEDevice.GetOptimalPacketSize(out optionPacketSize);
                    if (result == MvError.MV_OK)
                    {
                        _device.Parameters.SetIntValue("GevSCPSPacketSize", (long)optionPacketSize);
                    }
                }

                // 设置默认采集模式
                _device.Parameters.SetEnumValueByString("AcquisitionMode", "Continuous");
                _device.Parameters.SetEnumValueByString("TriggerMode", "Off");
            }
            catch (Exception)
            {
                if (_device != null)
                {
                    _device.Close();
                    _device.Dispose();
                    _device = null;
                }
                IsOpen = false;
                throw;
            }
        }

        /// <summary>
        /// 关闭设备
        /// </summary>
        public void Close()
        {
            if (!IsOpen || _device == null)
                return;

            try
            {
                // 停止所有操作
                StopGrabbing();
                StopContinuousTrigger();

                // 关闭设备
                _device.Close();
                _device.Dispose();
                _device = null;
                IsOpen = false;
            }
            catch (Exception)
            {
                // 确保资源释放
                _device = null;
                IsOpen = false;
                throw;
            }
        }

        /// <summary>
        /// 开始采集图像
        /// </summary>
        public void StartGrabbing()
        {
            if (!IsOpen || _device == null)
                throw new InvalidOperationException("设备未打开");

            if (IsGrabbing)
                return;

            try
            {
                // 清理之前的采集操作
                StopGrabbing();

                // 初始化取消令牌
                _grabCts = new CancellationTokenSource();

                // 启动采集线程
                _grabThread = new Thread(() => GrabProcess(_grabCts.Token))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _grabThread.Start();

                // 启动硬件采集
                int result = _device.StreamGrabber.StartGrabbing();
                if (result != MvError.MV_OK)
                {
                    _grabCts.Cancel();
                    _grabThread.Join(1000);
                    throw new Exception($"启动图像采集失败，错误码: {result}");
                }

                IsGrabbing = true;
            }
            catch (Exception)
            {
                _grabCts?.Cancel();
                _grabThread?.Join(1000);
                _grabCts?.Dispose();
                _grabCts = null;
                _grabThread = null;
                IsGrabbing = false;
                throw;
            }
        }

        /// <summary>
        /// 停止采集图像
        /// </summary>
        public void StopGrabbing()
        {
            if (!IsGrabbing)
                return;

            try
            {
                if (_device != null)
                {
                    _device.StreamGrabber.StopGrabbing();
                }

                _grabCts?.Cancel();
                _grabThread?.Join(1500);
            }
            finally
            {
                _grabCts?.Dispose();
                _grabCts = null;
                _grabThread = null;
                IsGrabbing = false;
            }
        }

        /// <summary>
        /// 图像采集过程
        /// </summary>
        /// <param name="token">取消令牌</param>
        private void GrabProcess(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _device != null)
                {
                    IFrameOut frame;
                    int ret = _device.StreamGrabber.GetImageBuffer(1000, out frame);

                    if (ret == MvError.MV_OK)
                    {
                        using (var bitmap = frame.Image.ToBitmap())
                        {
                            var clonedBitmap = bitmap.Clone() as Bitmap;
                            ImageUpdated?.Invoke(this, new ImageEventArgs(clonedBitmap));
                        }
                        
                        if (_device != null)
                        {
                            _device.StreamGrabber.FreeImageBuffer(frame);
                        }
                    }
                    else if (ret != MvError.MV_E_GC_TIMEOUT)
                    {
                        // 非超时错误需要处理
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception)
            {
                // 错误处理
            }
            finally
            {
                if (_device != null)
                {
                    _device.StreamGrabber.StopGrabbing();
                }
            }
        }

        /// <summary>
        /// 设置触发模式
        /// </summary>
        /// <param name="config">触发配置</param>
        public void SetTriggerMode(TriggerConfig config)
        {
            if (!IsOpen || _device == null)
                throw new InvalidOperationException("设备未打开");

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            try
            {
                if (config.Mode == TriggerConfig.TriggerMode.Off)
                {
                    _device.Parameters.SetEnumValueByString("TriggerMode", "Off");
                }
                else
                {
                    _device.Parameters.SetEnumValueByString("TriggerMode", "On");
                    _device.Parameters.SetEnumValueByString("TriggerSource", config.GetModeString());
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 执行一次软触发
        /// </summary>
        public void TriggerOnce()
        {
            if (!IsOpen || _device == null)
                throw new InvalidOperationException("设备未打开");

            int result = _device.Parameters.SetCommandValue("TriggerSoftware");
            if (result != MvError.MV_OK)
            {
                throw new Exception($"软触发失败，错误码: {result}");
            }
        }

        /// <summary>
        /// 开始连续触发
        /// </summary>
        /// <param name="intervalMs">触发间隔(毫秒)</param>
        public async Task StartContinuousTriggerAsync(int intervalMs)
        {
            if (!IsOpen || _device == null)
                throw new InvalidOperationException("设备未打开");

            if (intervalMs <= 0)
                throw new ArgumentException("间隔时间必须大于0", nameof(intervalMs));

            StopContinuousTrigger();
            _triggerCts = new CancellationTokenSource();
            
            try
            {
                await TriggerLoopAsync(intervalMs, _triggerCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception)
            {
                _triggerCts?.Cancel();
                _triggerCts?.Dispose();
                _triggerCts = null;
                throw;
            }
        }

        /// <summary>
        /// 停止连续触发
        /// </summary>
        public void StopContinuousTrigger()
        {
            _triggerCts?.Cancel();
            _triggerCts?.Dispose();
            _triggerCts = null;
        }

        /// <summary>
        /// 触发循环
        /// </summary>
        /// <param name="interval">间隔(毫秒)</param>
        /// <param name="token">取消令牌</param>
        private async Task TriggerLoopAsync(int interval, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var stopwatch = Stopwatch.StartNew();
                    TriggerOnce();

                    long remainingWait = interval - stopwatch.ElapsedMilliseconds;
                    if (remainingWait > 0)
                        await Task.Delay((int)remainingWait, token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出循环
            }
        }

        /// <summary>
        /// 设置参数
        /// </summary>
        /// <param name="name">参数名称</param>
        /// <param name="value">参数值</param>
        /// <returns>是否设置成功</returns>
        public bool SetParameter(string name, object value)
        {
            if (!IsOpen || _device == null)
                throw new InvalidOperationException("设备未打开");

            try
            {
                int result = MvError.MV_OK;
                
                if (value is string stringValue)
                {
                    result = _device.Parameters.SetEnumValueByString(name, stringValue);
                }
                else if (value is int intValue)
                {
                    result = _device.Parameters.SetIntValue(name, intValue);
                }
                else if (value is float floatValue)
                {
                    result = _device.Parameters.SetFloatValue(name, floatValue);
                }
                else if (value is double doubleValue)
                {
                    result = _device.Parameters.SetFloatValue(name, (float)doubleValue);
                }
                else if (value is bool boolValue)
                {
                    result = _device.Parameters.SetBoolValue(name, boolValue);
                }
                
                return result == MvError.MV_OK;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 获取参数
        /// </summary>
        /// <typeparam name="T">参数类型</typeparam>
        /// <param name="name">参数名称</param>
        /// <returns>参数值</returns>
        public T GetParameter<T>(string name)
        {
            if (!IsOpen || _device == null)
                throw new InvalidOperationException("设备未打开");

            try
            {
                if (typeof(T) == typeof(string))
                {
                    IStringValue value;
                    _device.Parameters.GetStringValue(name, out value);
                    return (T)(object)value;
                }
                else if (typeof(T) == typeof(int))
                {
                    IIntValue value;
                    _device.Parameters.GetIntValue(name, out value);
                    return (T)(object)value;
                }
                else if (typeof(T) == typeof(float))
                {
                    IFloatValue value;
                    _device.Parameters.GetFloatValue(name, out value);
                    return (T)(object)value;
                }
                else if (typeof(T) == typeof(bool))
                {
                    bool value;
                    _device.Parameters.GetBoolValue(name, out value);
                    return (T)(object)value;
                }
                
                throw new NotSupportedException($"不支持的参数类型: {typeof(T).Name}");
            }
            catch (Exception)
            {
                return default(T);
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing">是否释放托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                // 释放托管资源
                Close();
                _grabCts?.Dispose();
                _triggerCts?.Dispose();
            }

            _isDisposed = true;
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~CameraDevice()
        {
            Dispose(false);
        }
    }

    #endregion

    #region 相机管理器

    /// <summary>
    /// 相机管理器，提供相机设备的管理和操作
    /// </summary>
    public class CameraManager : IDisposable
    {
        private ICameraDevice _activeDevice;
        private List<ICameraDevice> _devices = new List<ICameraDevice>();
        private bool _isDisposed;

        /// <summary>
        /// 活动设备
        /// </summary>
        public ICameraDevice ActiveDevice => _activeDevice;

        /// <summary>
        /// 图像更新事件
        /// </summary>
        public event EventHandler<ImageEventArgs> ImageUpdated;

        /// <summary>
        /// 图像捕获事件
        /// </summary>
        public event EventHandler<ImageEventArgs> ImageCaptured;

        /// <summary>
        /// 构造函数
        /// </summary>
        public CameraManager()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="deviceIndex">设备索引</param>
        public CameraManager(int deviceIndex)
        {
            ConnectDevice(deviceIndex);
        }

        /// <summary>
        /// 刷新设备列表
        /// </summary>
        /// <returns>设备信息包装列表</returns>
        public List<DeviceInfoWrapper> RefreshDeviceList()
        {
            try
            {
                return CameraUtils.EnumerateDevices();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 连接设备
        /// </summary>
        /// <param name="deviceIndex">设备索引</param>
        /// <returns>是否连接成功</returns>
        public bool ConnectDevice(int deviceIndex)
        {
            try
            {
                // 断开当前连接
                DisconnectDevice();

                // 直接通过索引创建设备
                _activeDevice = CameraUtils.CreateDevice(deviceIndex);
                _activeDevice.Open();

                // 订阅事件
                _activeDevice.ImageUpdated += Device_ImageUpdated;
                _activeDevice.ImageCaptured += Device_ImageCaptured;

                return true;
            }
            catch (Exception)
            {
                _activeDevice = null;
                return false;
            }
        }

        /// <summary>
        /// 连接设备
        /// </summary>
        /// <param name="deviceInfo">设备信息包装类</param>
        /// <returns>是否连接成功</returns>
        public bool ConnectDevice(DeviceInfoWrapper deviceInfo)
        {
            try
            {
                // 断开当前连接
                DisconnectDevice();

                // 创建设备并打开
                _activeDevice = CameraUtils.CreateDevice(deviceInfo);
                _activeDevice.Open();

                // 订阅事件
                _activeDevice.ImageUpdated += Device_ImageUpdated;
                _activeDevice.ImageCaptured += Device_ImageCaptured;

                return true;
            }
            catch (Exception)
            {
                _activeDevice = null;
                return false;
            }
        }

        /// <summary>
        /// 断开设备
        /// </summary>
        public void DisconnectDevice()
        {
            if (_activeDevice == null)
                return;

            try
            {
                // 取消订阅事件
                _activeDevice.ImageUpdated -= Device_ImageUpdated;
                _activeDevice.ImageCaptured -= Device_ImageCaptured;

                // 关闭设备
                _activeDevice.Close();
                _activeDevice.Dispose();
                _activeDevice = null;
            }
            catch (Exception)
            {
                _activeDevice = null;
            }
        }

        /// <summary>
        /// 开始采集
        /// </summary>
        /// <returns>是否成功</returns>
        public bool StartGrabbing()
        {
            if (_activeDevice == null)
                return false;

            try
            {
                _activeDevice.StartGrabbing();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 停止采集
        /// </summary>
        public void StopGrabbing()
        {
            _activeDevice?.StopGrabbing();
        }

        /// <summary>
        /// 设置软触发模式
        /// </summary>
        public void SetSoftTrigger()
        {
            if (_activeDevice == null)
                return;

            _activeDevice.SetTriggerMode(new TriggerConfig(TriggerConfig.TriggerMode.Software));
        }

        /// <summary>
        /// 设置线触发模式
        /// </summary>
        /// <param name="lineNumber">线号</param>
        public void SetLineTrigger(int lineNumber)
        {
            if (_activeDevice == null || lineNumber < 0 || lineNumber > 3)
                return;

            TriggerConfig.TriggerMode mode = TriggerConfig.TriggerMode.Line0;
            switch (lineNumber)
            {
                case 0: mode = TriggerConfig.TriggerMode.Line0; break;
                case 1: mode = TriggerConfig.TriggerMode.Line1; break;
                case 2: mode = TriggerConfig.TriggerMode.Line2; break;
                case 3: mode = TriggerConfig.TriggerMode.Line3; break;
            }

            _activeDevice.SetTriggerMode(new TriggerConfig(mode));
        }

        /// <summary>
        /// 设置触发模式
        /// </summary>
        /// <param name="mode">触发模式</param>
        public void SetTriggerMode(TriggerConfig.TriggerMode mode)
        {
            _activeDevice?.SetTriggerMode(new TriggerConfig(mode));
        }

        /// <summary>
        /// 执行一次软触发
        /// </summary>
        public void TriggerOnce()
        {
            _activeDevice?.TriggerOnce();
        }

        /// <summary>
        /// 开始连续触发
        /// </summary>
        /// <param name="intervalMs">触发间隔(毫秒)</param>
        /// <returns>任务</returns>
        public Task StartContinuousTriggerAsync(int intervalMs)
        {
            if (_activeDevice == null)
                return Task.CompletedTask;

            return _activeDevice.StartContinuousTriggerAsync(intervalMs);
        }

        /// <summary>
        /// 停止连续触发
        /// </summary>
        public void StopContinuousTrigger()
        {
            _activeDevice?.StopContinuousTrigger();
        }

        /// <summary>
        /// 设备图像更新事件处理
        /// </summary>
        private void Device_ImageUpdated(object sender, ImageEventArgs e)
        {
            ImageUpdated?.Invoke(this, e);
        }

        /// <summary>
        /// 设备图像捕获事件处理
        /// </summary>
        private void Device_ImageCaptured(object sender, ImageEventArgs e)
        {
            ImageCaptured?.Invoke(this, e);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing">是否释放托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                // 断开所有设备
                DisconnectDevice();

                // 释放设备资源
                foreach (var device in _devices)
                {
                    try { device.Dispose(); } catch { }
                }
                _devices.Clear();
            }

            _isDisposed = true;
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~CameraManager()
        {
            Dispose(false);
        }
    }

    #endregion
}
