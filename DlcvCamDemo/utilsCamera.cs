using MvCameraControl;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using static System.Net.Mime.MediaTypeNames;
using System.Windows.Forms;
using System.Threading.Tasks;
using dlcv_infer_csharp;
using OpenCvSharp.Extensions;
using OpenCvSharp;
using System.Web;
using System.Diagnostics;

namespace DlcvCamDemo
{
    public class CameraManager : IDisposable
    {
        public List<IDeviceInfo> RefreshDeviceList()
        {
            DeviceTLayerType enumTLayerType = DeviceTLayerType.MvGigEDevice | DeviceTLayerType.MvUsbDevice
                | DeviceTLayerType.MvGenTLGigEDevice | DeviceTLayerType.MvGenTLCXPDevice | DeviceTLayerType.MvGenTLCameraLinkDevice | DeviceTLayerType.MvGenTLXoFDevice;

            List<IDeviceInfo> deviceInfoList = new List<IDeviceInfo>();
            int nRet = DeviceEnumerator.EnumDevices(enumTLayerType, out deviceInfoList);
            if (nRet != MvError.MV_OK)
            {
                Logger.Debug("Enumerate devices fail!", nRet);
                throw new Exception("获取摄像机列表失败，请检查插口连接。"); // 抛出异常
            }

            return deviceInfoList;
        }

        private IDevice _device;
        private List<IDeviceInfo> _deviceInfoList;
        //private bool _isGrabbing = false;
        private Thread _receiveThread = null;    // ch:接收图像线程 | en: Receive image thread
        public event Action<Bitmap> ImageUpdated;

        public event Action<System.Drawing.Image> ImageCaptured;

        // 默认构造函数
        public CameraManager()
        {
            _deviceInfoList = RefreshDeviceList();
        }

        // 带参数的构造函数，传入设备索引
        public CameraManager(int deviceIndex)
        {
            _deviceInfoList = RefreshDeviceList();

            if (deviceIndex >= 0 && deviceIndex < _deviceInfoList.Count)
            {
                InitializeDevice(deviceIndex);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(deviceIndex), "Device index out of range.");
            }
        }

        public bool CheckIfHaveDevice()
        {
            return _deviceInfoList.Count > 0;
        }

        public void InitializeDevice(int deviceIndex)
        {
            // 打开设备并设置软触发 这里支持用户直接输入索引
            OpenDevice(_deviceInfoList[deviceIndex]);
            StartGrabbing();
            SetSoftTrigger();
            TriggerSoftTriggerOnce();
        }

        public void InitializeDevice(IDeviceInfo deviceInfo)
        {
            // 打开设备并设置软触发
            OpenDevice(deviceInfo);
            SetSoftTrigger();
            TriggerSoftTriggerOnce();
        }

        public void OpenDevice(IDeviceInfo deviceInfo)
        {
            if (deviceInfo == null)
            {
                throw new ArgumentNullException(nameof(deviceInfo), "Device info cannot be null.");
            }
            if (_device != null)
            {
                // 如果设备已经打开，先关闭设备
                CloseDevice();
            }
            Logger.Info($"打开设备：{deviceInfo.ManufacturerName} {deviceInfo.ModelName} {deviceInfo.DeviceVersion} {deviceInfo.SerialNumber} {deviceInfo.UserDefinedName} {deviceInfo.DevTypeInfo}");
            try
            {
                // ch:打开设备 | en:Open device
                _device = DeviceFactory.CreateDevice(deviceInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Create Device fail!" + ex.Message);
                return;
            }

            if (_device == null)
            {
                throw new Exception("Failed to create device.");
            }

            int result = _device.Open();
            if (result != MvError.MV_OK)
            {
                Logger.Error("Open Device fail!", result);
                throw new Exception("Failed to open device.");
            }

            //ch: 判断是否为gige设备 | en: Determine whether it is a GigE device
            if (_device is IGigEDevice)
            {
                //ch: 转换为gigE设备 | en: Convert to Gige device
                IGigEDevice gigEDevice = _device as IGigEDevice;

                // ch:探测网络最佳包大小(只对GigE相机有效) | en:Detection network optimal package size(It only works for the GigE camera)
                int optionPacketSize;
                result = gigEDevice.GetOptimalPacketSize(out optionPacketSize);
                if (result != MvError.MV_OK)
                {
                    Logger.Error("Warning: Get Packet Size failed!", result);
                }
                else
                {
                    result = _device.Parameters.SetIntValue("GevSCPSPacketSize", (long)optionPacketSize);
                    if (result != MvError.MV_OK)
                    {
                        Logger.Error("Warning: Set Packet Size failed!", result);
                    }
                }
            }

            _device.Parameters.SetEnumValueByString("AcquisitionMode", "Continuous");
            _device.Parameters.SetEnumValueByString("TriggerMode", "Off");
        }

        public void CloseDevice()
        {
            if (_device != null)
            {

                StopGrabbing();
                StopTriggerLoop();


                _device.Close();
                _device.Dispose();
                _device = null; // 确保设备引用被清除
            }
        }

        public void CloseSDK()
        {
            CloseDevice();
            // 释放SDK资源
            SDKSystem.Finalize();
        }

        // 实现 IDisposable 接口
        public void Dispose()
        {
            CloseDevice();
        }

        private CancellationTokenSource _grabCts; // 图像采集取消令牌
        private Thread _grabThread;               // 图像采集线程

        public void StartGrabbing()
        {
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
                    Priority = ThreadPriority.AboveNormal // 根据需求调整
                };
                _grabThread.Start();
            }
            catch (Exception ex)
            {
                Logger.Error($"Start grabbing failed: {ex.Message}");
                throw;
            }

            // 启动硬件采集
            int result = _device.StreamGrabber.StartGrabbing();
            if (result == MvError.MV_OK) return;

            // 失败处理
            _grabCts.Cancel();
            _grabThread.Join(1000);
            Logger.Error("Start hardware grabbing failed", result);
        }

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
                        using (var bitmap = frame.Image.ToBitmap()) // 自动释放资源
                        {
                            ImageUpdated?.Invoke(bitmap.Clone() as Bitmap); // 深拷贝避免资源冲突
                        }
                        // 检查设备是否有效再释放资源
                        if (_device != null)
                        {
                            _device.StreamGrabber.FreeImageBuffer(frame);
                        }
                    }
                    else if (ret != MvError.MV_E_GC_TIMEOUT) // 过滤超时
                    {
                        Logger.Info($"Grab frame error: {ret}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                Logger.Error($"Grab process crashed: {ex.Message}");
            }
            finally
            {
                if (_device != null)
                {
                    _device.StreamGrabber.StopGrabbing();
                }
            }
        }

        public void StopGrabbing()
        {
            try
            {
                if (_device != null)
                {
                    _device.StreamGrabber.StopGrabbing();
                }
                _grabCts?.Cancel();
                _grabThread?.Join(1500); // 带超时等待
            }
            finally
            {
                _grabCts?.Dispose();
                _grabCts = null;
                _grabThread = null;
            }
        }

        public void SetSoftTrigger()
        {
            if (_device == null)
            {
                throw new InvalidOperationException("Device is not initialized.");
            }

            // ch:打开触发模式 | en:Open Trigger Mode
            _device.Parameters.SetEnumValueByString("TriggerMode", "On");

            // ch:触发源选择:0 - Line0; | en:Trigger source select:0 - Line0;
            //           1 - Line1;
            //           2 - Line2;
            //           3 - Line3;
            //           4 - Counter;
            //           7 - Software;

            _device.Parameters.SetEnumValueByString("TriggerSource", "Software");

        }

        public void SetLineTrigger()
        {
            if (_device == null)
            {
                throw new InvalidOperationException("Device is not initialized.");
            }
            // ch:打开触发模式 | en:Open Trigger Mode
            _device.Parameters.SetEnumValueByString("TriggerMode", "On");
            // ch:触发源选择:0 - Line0; | en:Trigger source select:0 - Line0;
            //           1 - Line1;
            //           2 - Line2;
            //           3 - Line3;
            //           4 - Counter;
            //           7 - Software;
            _device.Parameters.SetEnumValueByString("TriggerSource", "Line0");
        }

        public void SetLineTrigger(string mode)
        {
            if (_device == null)
            {
                throw new InvalidOperationException("Device is not initialized.");
            }

            // 定义有效触发源选项集合
            var validModes = new HashSet<string> { "Line0", "Line1", "Line2", "Line3", "Counter", "Software" };

            // 验证传入参数
            if (!validModes.Contains(mode))
            {
                throw new ArgumentException($"Invalid trigger mode: {mode}. Valid values are: {string.Join(", ", validModes)}");
            }

            // ch:打开触发模式 | en:Open Trigger Mode
            _device.Parameters.SetEnumValueByString("TriggerMode", "On");

            // 设置触发源（已通过参数校验）
            _device.Parameters.SetEnumValueByString("TriggerSource", mode);
        }


        public void TriggerSoftTriggerOnce()
        {
            if (_device == null)
            {
                throw new InvalidOperationException("Device is not initialized.");
            }

            // 软触发一次的实现代码
            int result = _device.Parameters.SetCommandValue("TriggerSoftware");
            if (result != MvError.MV_OK)
            {
                Logger.Error("Trigger Software Fail!", result);
                throw new Exception("Failed to trigger software.");
            }
        }

        private CancellationTokenSource _cts;

        public async Task TriggerSoftTriggerLoopAsync(int intervalInMilliseconds)
        {
            StopTriggerLoop(); // 停止现有循环
            _cts = new CancellationTokenSource();
            try
            {
                await TriggerLoopAsync(intervalInMilliseconds, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常取消，无需处理
            }
            finally
            {
                // 清理资源
                _cts.Dispose();
                _cts = null;
            }
        }

        public void StopTriggerLoop()
        {
            _cts?.Cancel(); // 安全取消，即使_cts为null
        }

        private async Task TriggerLoopAsync(int interval, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var stopwatch = Stopwatch.StartNew();
                    TriggerSoftTriggerOnce();

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
    }
}