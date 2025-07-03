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

        /// <summary>
        /// 设置曝光时间
        /// </summary>
        /// <param name="exposureTime">曝光时间(微秒)，最大33000</param>
        /// <returns>是否设置成功</returns>
        bool SetExposureTime(float exposureTime);

        /// <summary>
        /// 获取曝光时间
        /// </summary>
        /// <returns>曝光时间(微秒)</returns>
        float GetExposureTime();

        /// <summary>
        /// 执行一键白平衡
        /// </summary>
        /// <returns>是否成功</returns>
        bool ExecuteBalanceWhiteAuto();

        /// <summary>
        /// 获取白平衡比例值
        /// </summary>
        /// <param name="selector">颜色选择器(Red/Green/Blue)</param>
        /// <returns>白平衡比例值</returns>
        float GetBalanceRatio(string selector);

        /// <summary>
        /// 设置手动白平衡
        /// </summary>
        /// <param name="redRatio">红色比例</param>
        /// <param name="greenRatio">绿色比例</param>
        /// <param name="blueRatio">蓝色比例</param>
        /// <returns>是否设置成功</returns>
        bool SetBalanceRatio(float redRatio, float greenRatio, float blueRatio);

        /// <summary>
        /// 设置ROI区域
        /// </summary>
        /// <param name="offsetX">X偏移</param>
        /// <param name="offsetY">Y偏移</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <returns>是否设置成功</returns>
        bool SetROI(int offsetX, int offsetY, int width, int height);

        /// <summary>
        /// 获取ROI区域
        /// </summary>
        /// <returns>ROI区域(offsetX, offsetY, width, height)</returns>
        (int offsetX, int offsetY, int width, int height) GetROI();

        /// <summary>
        /// 获取相机最大分辨率
        /// </summary>
        /// <returns>最大分辨率(width, height)</returns>
        (int width, int height) GetMaxResolution();

        /// <summary>
        /// 恢复ROI到最大分辨率
        /// </summary>
        /// <returns>是否设置成功</returns>
        bool RestoreMaxROI();

        /// <summary>
        /// 保存参数到用户集1
        /// </summary>
        /// <returns>是否保存成功</returns>
        bool SaveToUserSet1();
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
                    IFrameOut frame = null;
                    int ret = _device.StreamGrabber.GetImageBuffer(1000, out frame);

                    if (ret == MvError.MV_OK && frame != null)
                    {
                        try
                        {
                            // 检查 frame.Image 是否为 null
                            if (frame.Image != null)
                            {
                                using (var bitmap = frame.Image.ToBitmap())
                                {
                                    // 检查 bitmap 是否为 null
                                    if (bitmap != null)
                                    {
                                        var clonedBitmap = bitmap.Clone() as Bitmap;
                                        ImageUpdated?.Invoke(this, new ImageEventArgs(clonedBitmap));
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // 图像处理异常，继续下一帧
                        }
                        finally
                        {
                            // 确保释放图像缓冲区
                            if (_device != null && frame != null)
                            {
                                _device.StreamGrabber.FreeImageBuffer(frame);
                            }
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
        /// 设置曝光时间
        /// </summary>
        /// <param name="exposureTime">曝光时间(微秒)，最大33000</param>
        /// <returns>是否设置成功</returns>
        public bool SetExposureTime(float exposureTime)
        {
            if (!IsOpen || _device == null)
            {
                Console.WriteLine("SetExposureTime: 设备未打开");
                return false;
            }

            try
            {
                Console.WriteLine($"设置曝光时间: {exposureTime}μs");
                
                // 限制曝光时间上限为33ms(33000微秒)
                if (exposureTime > 33000)
                {
                    Console.WriteLine($"曝光时间超过上限，调整为33000μs: {exposureTime} -> 33000");
                    exposureTime = 33000;
                }
                
                if (exposureTime < 0)
                {
                    Console.WriteLine($"曝光时间不能为负，调整为0: {exposureTime} -> 0");
                    exposureTime = 0;
                }

                // 关闭自动曝光
                int result = _device.Parameters.SetEnumValue("ExposureAuto", 0);
                Console.WriteLine($"SetEnumValue ExposureAuto=0 结果: 0x{result:X8}");
                if (result != MvError.MV_OK)
                {
                    // 尝试字符串方式
                    result = _device.Parameters.SetEnumValueByString("ExposureAuto", "Off");
                    Console.WriteLine($"SetEnumValueByString ExposureAuto=Off 结果: 0x{result:X8}");
                    if (result != MvError.MV_OK)
                    {
                        Console.WriteLine("关闭自动曝光失败");
                        return false;
                    }
                }

                // 设置曝光时间
                result = _device.Parameters.SetFloatValue("ExposureTime", exposureTime);
                Console.WriteLine($"SetFloatValue ExposureTime={exposureTime} 结果: 0x{result:X8}");
                
                if (result == MvError.MV_OK)
                {
                    Console.WriteLine("曝光时间设置成功");
                    return true;
                }
                else
                {
                    Console.WriteLine("曝光时间设置失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SetExposureTime异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取曝光时间
        /// </summary>
        /// <returns>曝光时间(微秒)</returns>
        public float GetExposureTime()
        {
            if (!IsOpen || _device == null)
            {
                Console.WriteLine("GetExposureTime: 设备未打开");
                return 0;
            }

            try
            {
                Console.WriteLine("获取当前曝光时间...");
                IFloatValue value;
                int result = _device.Parameters.GetFloatValue("ExposureTime", out value);
                Console.WriteLine($"GetFloatValue ExposureTime 结果: 0x{result:X8}");
                
                if (result == MvError.MV_OK)
                {
                    Console.WriteLine($"当前曝光时间: {value.CurValue}μs");
                    return value.CurValue;
                }
                else
                {
                    Console.WriteLine("获取曝光时间失败");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetExposureTime异常: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 执行一键白平衡
        /// </summary>
        /// <returns>是否成功</returns>
        public bool ExecuteBalanceWhiteAuto()
        {
            if (!IsOpen || _device == null)
            {
                Console.WriteLine("ExecuteBalanceWhiteAuto: 设备未打开");
                return false;
            }

            try
            {
                Console.WriteLine("开始执行一键白平衡...");
                
                // 方法1: 尝试使用枚举值设置白平衡模式为一次性自动
                int result = _device.Parameters.SetEnumValue("BalanceWhiteAuto", 1); // 1代表Once (根据海康文档)
                Console.WriteLine($"SetEnumValue BalanceWhiteAuto=1 结果: 0x{result:X8}");
                
                if (result != MvError.MV_OK)
                {
                    // 方法2: 如果数字枚举失败，尝试字符串方式
                    result = _device.Parameters.SetEnumValueByString("BalanceWhiteAuto", "Once");
                    Console.WriteLine($"SetEnumValueByString BalanceWhiteAuto=Once 结果: 0x{result:X8}");
                    
                    if (result != MvError.MV_OK)
                    {
                        // 方法3: 尝试其他可能的值
                        result = _device.Parameters.SetEnumValue("BalanceWhiteAuto", 1);
                        Console.WriteLine($"SetEnumValue BalanceWhiteAuto=1 结果: 0x{result:X8}");
                        
                        if (result != MvError.MV_OK)
                        {
                            Console.WriteLine("白平衡设置失败，所有方法都尝试过了");
                            return false;
                        }
                    }
                }

                Console.WriteLine("白平衡命令发送成功，等待2秒钟完成...");
                // 等待白平衡完成
                System.Threading.Thread.Sleep(2000);
                
                Console.WriteLine("一键白平衡执行完成");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ExecuteBalanceWhiteAuto异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取白平衡比例值
        /// </summary>
        /// <param name="selector">颜色选择器(Red/Green/Blue)</param>
        /// <returns>白平衡比例值</returns>
        public float GetBalanceRatio(string selector)
        {
            if (!IsOpen || _device == null)
            {
                Console.WriteLine($"GetBalanceRatio({selector}): 设备未打开");
                return 0;
            }

            try
            {
                Console.WriteLine($"获取白平衡比例值: {selector}");
                
                // 设置颜色选择器
                int result = _device.Parameters.SetEnumValueByString("BalanceRatioSelector", selector);
                Console.WriteLine($"SetEnumValueByString BalanceRatioSelector={selector} 结果: 0x{result:X8}");
                
                if (result != MvError.MV_OK)
                {
                    Console.WriteLine($"设置BalanceRatioSelector失败: {selector}");
                    return 0;
                }

                // 获取白平衡比例值 (海康相机的BalanceRatio是int类型)
                IIntValue value;
                result = _device.Parameters.GetIntValue("BalanceRatio", out value);
                Console.WriteLine($"GetIntValue BalanceRatio 结果: 0x{result:X8}");
                
                if (result == MvError.MV_OK)
                {
                    float ratio = (float)value.CurValue; // 转换为float返回
                    Console.WriteLine($"白平衡比例值 {selector}: {ratio} (原始int值: {value.CurValue})");
                    return ratio;
                }
                else
                {
                    Console.WriteLine($"获取BalanceRatio失败: {selector}");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetBalanceRatio({selector})异常: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 设置手动白平衡
        /// </summary>
        /// <param name="redRatio">红色比例</param>
        /// <param name="greenRatio">绿色比例</param>
        /// <param name="blueRatio">蓝色比例</param>
        /// <returns>是否设置成功</returns>
        public bool SetBalanceRatio(float redRatio, float greenRatio, float blueRatio)
        {
            if (!IsOpen || _device == null)
            {
                Console.WriteLine("SetBalanceRatio: 设备未打开");
                return false;
            }

            try
            {
                Console.WriteLine($"设置手动白平衡: R={redRatio}, G={greenRatio}, B={blueRatio}");
                
                // 关闭自动白平衡
                int result = _device.Parameters.SetEnumValue("BalanceWhiteAuto", 0); // 0通常代表Off
                Console.WriteLine($"SetEnumValue BalanceWhiteAuto=0 结果: 0x{result:X8}");
                
                if (result != MvError.MV_OK)
                {
                    // 尝试字符串方式
                    result = _device.Parameters.SetEnumValueByString("BalanceWhiteAuto", "Off");
                    Console.WriteLine($"SetEnumValueByString BalanceWhiteAuto=Off 结果: 0x{result:X8}");
                    if (result != MvError.MV_OK)
                    {
                        Console.WriteLine("关闭自动白平衡失败");
                        return false;
                    }
                }

                // 设置红色比例
                result = _device.Parameters.SetEnumValueByString("BalanceRatioSelector", "Red");
                Console.WriteLine($"SetEnumValueByString BalanceRatioSelector=Red 结果: 0x{result:X8}");
                if (result == MvError.MV_OK)
                {
                    int intRedRatio = (int)redRatio; // 转换为int类型
                    result = _device.Parameters.SetIntValue("BalanceRatio", intRedRatio);
                    Console.WriteLine($"SetIntValue BalanceRatio={intRedRatio} 结果: 0x{result:X8}");
                    if (result != MvError.MV_OK)
                    {
                        Console.WriteLine($"设置红色比例失败: {intRedRatio}");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine("选择红色通道失败");
                    return false;
                }

                // 设置绿色比例
                result = _device.Parameters.SetEnumValueByString("BalanceRatioSelector", "Green");
                Console.WriteLine($"SetEnumValueByString BalanceRatioSelector=Green 结果: 0x{result:X8}");
                if (result == MvError.MV_OK)
                {
                    int intGreenRatio = (int)greenRatio; // 转换为int类型
                    result = _device.Parameters.SetIntValue("BalanceRatio", intGreenRatio);
                    Console.WriteLine($"SetIntValue BalanceRatio={intGreenRatio} 结果: 0x{result:X8}");
                    if (result != MvError.MV_OK)
                    {
                        Console.WriteLine($"设置绿色比例失败: {intGreenRatio}");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine("选择绿色通道失败");
                    return false;
                }

                // 设置蓝色比例
                result = _device.Parameters.SetEnumValueByString("BalanceRatioSelector", "Blue");
                Console.WriteLine($"SetEnumValueByString BalanceRatioSelector=Blue 结果: 0x{result:X8}");
                if (result == MvError.MV_OK)
                {
                    int intBlueRatio = (int)blueRatio; // 转换为int类型
                    result = _device.Parameters.SetIntValue("BalanceRatio", intBlueRatio);
                    Console.WriteLine($"SetIntValue BalanceRatio={intBlueRatio} 结果: 0x{result:X8}");
                    if (result != MvError.MV_OK)
                    {
                        Console.WriteLine($"设置蓝色比例失败: {intBlueRatio}");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine("选择蓝色通道失败");
                    return false;
                }

                Console.WriteLine("手动白平衡设置成功");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SetBalanceRatio异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置ROI区域
        /// </summary>
        /// <param name="offsetX">X偏移</param>
        /// <param name="offsetY">Y偏移</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <returns>是否设置成功</returns>
        public bool SetROI(int offsetX, int offsetY, int width, int height)
        {
            if (!IsOpen || _device == null)
            {
                Console.WriteLine("SetROI: 设备未打开");
                return false;
            }

            try
            {
                Console.WriteLine($"设置ROI: X={offsetX}, Y={offsetY}, W={width}, H={height}");
                
                // 获取当前最大分辨率
                var (maxWidth, maxHeight) = GetMaxResolution();
                Console.WriteLine($"相机最大分辨率: W={maxWidth}, H={maxHeight}");
                
                // 验证ROI参数
                if (offsetX < 0 || offsetY < 0 || width <= 0 || height <= 0)
                {
                    Console.WriteLine("ROI参数无效：偏移量不能为负，宽高必须大于0");
                    return false;
                }
                
                if (offsetX + width > maxWidth || offsetY + height > maxHeight)
                {
                    Console.WriteLine($"ROI超出范围: X+W={offsetX + width} > {maxWidth} 或 Y+H={offsetY + height} > {maxHeight}");
                    return false;
                }

                // 海康相机ROI参数可能需要满足特定的对齐要求（通常是4或8的倍数）
                // 调整宽度到4的倍数
                int adjustedWidth = (width / 4) * 4;
                int adjustedHeight = (height / 4) * 4;
                int adjustedOffsetX = (offsetX / 4) * 4;
                int adjustedOffsetY = (offsetY / 4) * 4;
                
                if (adjustedWidth != width || adjustedHeight != height || 
                    adjustedOffsetX != offsetX || adjustedOffsetY != offsetY)
                {
                    Console.WriteLine($"ROI参数已调整到4的倍数: " +
                        $"原始({offsetX},{offsetY},{width},{height}) -> " +
                        $"调整后({adjustedOffsetX},{adjustedOffsetY},{adjustedWidth},{adjustedHeight})");
                    
                    offsetX = adjustedOffsetX;
                    offsetY = adjustedOffsetY;
                    width = adjustedWidth;
                    height = adjustedHeight;
                }

                // 确保最小尺寸
                if (width < 64) width = 64;
                if (height < 64) height = 64;

                int result;
                
                // 根据海康相机的特性，按照更安全的顺序设置ROI参数
                Console.WriteLine("开始设置ROI，按照海康相机安全顺序...");
                
                // 步骤1: 首先将偏移量设置为0，避免冲突
                result = _device.Parameters.SetIntValue("OffsetX", 0);
                Console.WriteLine($"步骤1: SetIntValue OffsetX=0 结果: 0x{result:X8}");
                if (result != MvError.MV_OK)
                {
                    Console.WriteLine("步骤1失败: 无法将OffsetX设置为0");
                    return false;
                }
                
                result = _device.Parameters.SetIntValue("OffsetY", 0);
                Console.WriteLine($"步骤1: SetIntValue OffsetY=0 结果: 0x{result:X8}");
                if (result != MvError.MV_OK)
                {
                    Console.WriteLine("步骤1失败: 无法将OffsetY设置为0");
                    return false;
                }

                // 步骤2: 然后设置Width（先设置较小的值，避免超范围）
                result = _device.Parameters.SetIntValue("Width", width);
                Console.WriteLine($"步骤2: SetIntValue Width={width} 结果: 0x{result:X8}");
                if (result != MvError.MV_OK)
                {
                    Console.WriteLine($"步骤2失败: 设置Width={width}失败，错误码: 0x{result:X8}");
                    
                    // 尝试获取Width的范围信息
                    try
                    {
                        IIntValue widthInfo;
                        int getResult = _device.Parameters.GetIntValue("Width", out widthInfo);
                        if (getResult == MvError.MV_OK)
                        {
                            Console.WriteLine($"Width范围: Min={widthInfo.Min}, Max={widthInfo.Max}, 当前={widthInfo.CurValue}");
                        }
                    }
                    catch { }
                    
                    return false;
                }

                // 步骤3: 设置Height
                result = _device.Parameters.SetIntValue("Height", height);
                Console.WriteLine($"步骤3: SetIntValue Height={height} 结果: 0x{result:X8}");
                if (result != MvError.MV_OK)
                {
                    Console.WriteLine($"步骤3失败: 设置Height={height}失败，错误码: 0x{result:X8}");
                    
                    // 尝试获取Height的范围信息
                    try
                    {
                        IIntValue heightInfo;
                        int getResult = _device.Parameters.GetIntValue("Height", out heightInfo);
                        if (getResult == MvError.MV_OK)
                        {
                            Console.WriteLine($"Height范围: Min={heightInfo.Min}, Max={heightInfo.Max}, 当前={heightInfo.CurValue}");
                        }
                    }
                    catch { }
                    
                    return false;
                }

                // 步骤4: 最后设置偏移量
                result = _device.Parameters.SetIntValue("OffsetX", offsetX);
                Console.WriteLine($"步骤4: SetIntValue OffsetX={offsetX} 结果: 0x{result:X8}");
                if (result != MvError.MV_OK)
                {
                    Console.WriteLine($"步骤4失败: 设置OffsetX={offsetX}失败，错误码: 0x{result:X8}");
                    return false;
                }

                result = _device.Parameters.SetIntValue("OffsetY", offsetY);
                Console.WriteLine($"步骤4: SetIntValue OffsetY={offsetY} 结果: 0x{result:X8}");
                if (result != MvError.MV_OK)
                {
                    Console.WriteLine($"步骤4失败: 设置OffsetY={offsetY}失败，错误码: 0x{result:X8}");
                    return false;
                }

                Console.WriteLine("ROI设置成功");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SetROI异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取ROI区域
        /// </summary>
        /// <returns>ROI区域(offsetX, offsetY, width, height)</returns>
        public (int offsetX, int offsetY, int width, int height) GetROI()
        {
            if (!IsOpen || _device == null)
            {
                Console.WriteLine("GetROI: 设备未打开");
                return (0, 0, 0, 0);
            }

            try
            {
                Console.WriteLine("获取当前ROI参数...");
                IIntValue offsetXValue, offsetYValue, widthValue, heightValue;
                
                int result1 = _device.Parameters.GetIntValue("OffsetX", out offsetXValue);
                Console.WriteLine($"GetIntValue OffsetX 结果: 0x{result1:X8}");
                
                int result2 = _device.Parameters.GetIntValue("OffsetY", out offsetYValue);
                Console.WriteLine($"GetIntValue OffsetY 结果: 0x{result2:X8}");
                
                int result3 = _device.Parameters.GetIntValue("Width", out widthValue);
                Console.WriteLine($"GetIntValue Width 结果: 0x{result3:X8}");
                
                int result4 = _device.Parameters.GetIntValue("Height", out heightValue);
                Console.WriteLine($"GetIntValue Height 结果: 0x{result4:X8}");

                if (result1 == MvError.MV_OK && result2 == MvError.MV_OK && 
                    result3 == MvError.MV_OK && result4 == MvError.MV_OK)
                {
                    int offsetX = (int)offsetXValue.CurValue;
                    int offsetY = (int)offsetYValue.CurValue;
                    int width = (int)widthValue.CurValue;
                    int height = (int)heightValue.CurValue;
                    
                    Console.WriteLine($"当前ROI: X={offsetX}, Y={offsetY}, W={width}, H={height}");
                    return (offsetX, offsetY, width, height);
                }
                else
                {
                    Console.WriteLine("获取ROI参数失败");
                    return (0, 0, 0, 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetROI异常: {ex.Message}");
                return (0, 0, 0, 0);
            }
        }

        /// <summary>
        /// 获取相机最大分辨率
        /// </summary>
        /// <returns>最大分辨率(width, height)</returns>
        public (int width, int height) GetMaxResolution()
        {
            if (!IsOpen || _device == null)
            {
                Console.WriteLine("GetMaxResolution: 设备未打开");
                return (0, 0);
            }

            try
            {
                Console.WriteLine("获取相机最大分辨率...");
                IIntValue widthMaxValue, heightMaxValue;
                
                int result1 = _device.Parameters.GetIntValue("WidthMax", out widthMaxValue);
                Console.WriteLine($"GetIntValue WidthMax 结果: 0x{result1:X8}");
                
                int result2 = _device.Parameters.GetIntValue("HeightMax", out heightMaxValue);
                Console.WriteLine($"GetIntValue HeightMax 结果: 0x{result2:X8}");

                if (result1 == MvError.MV_OK && result2 == MvError.MV_OK)
                {
                    int maxWidth = (int)widthMaxValue.CurValue;
                    int maxHeight = (int)heightMaxValue.CurValue;
                    Console.WriteLine($"最大分辨率: {maxWidth}x{maxHeight}");
                    return (maxWidth, maxHeight);
                }
                else
                {
                    Console.WriteLine("获取最大分辨率失败");
                    return (0, 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetMaxResolution异常: {ex.Message}");
                return (0, 0);
            }
        }

        /// <summary>
        /// 恢复ROI到最大分辨率
        /// </summary>
        /// <returns>是否设置成功</returns>
        public bool RestoreMaxROI()
        {
            if (!IsOpen || _device == null)
                return false;

            try
            {
                var (maxWidth, maxHeight) = GetMaxResolution();
                return SetROI(0, 0, maxWidth, maxHeight);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 保存参数到用户集1
        /// </summary>
        /// <returns>是否保存成功</returns>
        public bool SaveToUserSet1()
        {
            if (!IsOpen || _device == null)
            {
                Console.WriteLine("SaveToUserSet1: 设备未打开");
                return false;
            }

            try
            {
                Console.WriteLine("保存参数到用户集1...");
                
                // 选择用户集1
                int result = _device.Parameters.SetEnumValueByString("UserSetSelector", "UserSet1");
                Console.WriteLine($"SetEnumValueByString UserSetSelector=UserSet1 结果: 0x{result:X8}");
                if (result != MvError.MV_OK)
                {
                    Console.WriteLine("选择用户集1失败");
                    return false;
                }

                // 保存参数
                result = _device.Parameters.SetCommandValue("UserSetSave");
                Console.WriteLine($"SetCommandValue UserSetSave 结果: 0x{result:X8}");
                
                if (result == MvError.MV_OK)
                {
                    Console.WriteLine("参数保存到用户集1成功");
                    return true;
                }
                else
                {
                    Console.WriteLine("参数保存到用户集1失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SaveToUserSet1异常: {ex.Message}");
                return false;
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
            if (_activeDevice == null)
                return;

            _activeDevice.SetTriggerMode(new TriggerConfig(mode));
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
        /// 设置曝光时间
        /// </summary>
        /// <param name="exposureTime">曝光时间(微秒)，最大33000</param>
        /// <returns>是否设置成功</returns>
        public bool SetExposureTime(float exposureTime)
        {
            if (_activeDevice == null)
                return false;
            
            bool result = _activeDevice.SetExposureTime(exposureTime);
            if (result)
                _activeDevice.SaveToUserSet1();
            
            return result;
        }

        /// <summary>
        /// 获取曝光时间
        /// </summary>
        /// <returns>曝光时间(微秒)</returns>
        public float GetExposureTime()
        {
            return _activeDevice?.GetExposureTime() ?? 0;
        }

        /// <summary>
        /// 执行一键白平衡
        /// </summary>
        /// <returns>是否成功</returns>
        public bool ExecuteBalanceWhiteAuto()
        {
            if (_activeDevice == null)
                return false;
            
            bool result = _activeDevice.ExecuteBalanceWhiteAuto();
            if (result)
                _activeDevice.SaveToUserSet1();
            
            return result;
        }

        /// <summary>
        /// 获取白平衡比例值
        /// </summary>
        /// <returns>白平衡比例值(红, 绿, 蓝)</returns>
        public (float red, float green, float blue) GetBalanceRatio()
        {
            if (_activeDevice == null)
                return (0, 0, 0);

            float red = _activeDevice.GetBalanceRatio("Red");
            float green = _activeDevice.GetBalanceRatio("Green");
            float blue = _activeDevice.GetBalanceRatio("Blue");
            
            return (red, green, blue);
        }

        /// <summary>
        /// 设置手动白平衡
        /// </summary>
        /// <param name="redRatio">红色比例</param>
        /// <param name="greenRatio">绿色比例</param>
        /// <param name="blueRatio">蓝色比例</param>
        /// <returns>是否设置成功</returns>
        public bool SetBalanceRatio(float redRatio, float greenRatio, float blueRatio)
        {
            if (_activeDevice == null)
                return false;
            
            bool result = _activeDevice.SetBalanceRatio(redRatio, greenRatio, blueRatio);
            if (result)
                _activeDevice.SaveToUserSet1();
            
            return result;
        }

        /// <summary>
        /// 设置ROI区域
        /// </summary>
        /// <param name="offsetX">X偏移</param>
        /// <param name="offsetY">Y偏移</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <returns>是否设置成功</returns>
        public bool SetROI(int offsetX, int offsetY, int width, int height)
        {
            if (_activeDevice == null)
                return false;
            
            bool result = _activeDevice.SetROI(offsetX, offsetY, width, height);
            if (result)
                _activeDevice.SaveToUserSet1();
            
            return result;
        }

        /// <summary>
        /// 获取ROI区域
        /// </summary>
        /// <returns>ROI区域(offsetX, offsetY, width, height)</returns>
        public (int offsetX, int offsetY, int width, int height) GetROI()
        {
            return _activeDevice?.GetROI() ?? (0, 0, 0, 0);
        }

        /// <summary>
        /// 获取相机最大分辨率
        /// </summary>
        /// <returns>最大分辨率(width, height)</returns>
        public (int width, int height) GetMaxResolution()
        {
            return _activeDevice?.GetMaxResolution() ?? (0, 0);
        }

        /// <summary>
        /// 恢复ROI到最大分辨率
        /// </summary>
        /// <returns>是否设置成功</returns>
        public bool RestoreMaxROI()
        {
            if (_activeDevice == null)
                return false;
            
            bool result = _activeDevice.RestoreMaxROI();
            if (result)
                _activeDevice.SaveToUserSet1();
            
            return result;
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
