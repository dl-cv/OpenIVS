using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Drawing;
using DLCV;
using DLCV.Camera;
using dlcv_infer_csharp;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using MvCameraControl;
using OpenIVSWPF.Managers;
using System.Xml;
using System.Windows.Forms.Integration;

using Window = System.Windows.Window;
using Path = System.IO.Path;

namespace OpenIVSWPF
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 成员变量
        // 系统设置
        private Settings _settings;

        // Modbus通信管理器
        private ModbusManager _modbusInitializer;
        private bool _isModbusConnected = false;

        // 相机控制管理器
        private CameraInitializer _cameraInitializer;
        private bool _isCameraConnected = false;
        private bool _isGrabbing = false;

        // AI模型管理器
        private ModelManager _modelManager;
        private bool _isModelLoaded = false;

        // 主循环管理器
        private MainLoopManager _mainLoopManager;

        // 运行控制
        private bool _isRunning = false;
        private CancellationTokenSource _cts;
        
        // 上次拍照结果
        private System.Drawing.Bitmap _lastCapturedImage = null;
        private string _lastDetectionResult = "";

        // 统计数据
        private int _totalCount = 0;
        private int _okCount = 0;
        private int _ngCount = 0;
        private double _yieldRate = 0.0;

        // 是否已初始化
        private bool _isInitialized = false;
        #endregion

        // 添加ViewModel属性
        public MainWindowViewModel ViewModel { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;

            // 初始化ViewModel
            ViewModel = new MainWindowViewModel();
            DataContext = ViewModel;

            // 初始化UI状态
            InitializeUIState();

            // 创建设置对象
            LoadOrCreateSettings();
            
            // 初始化管理器
            InitializeManagers();
        }

        #region 初始化方法
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 更新界面状态
            UpdateControlState();
            UpdateStatus("系统已就绪，请点击开始按钮初始化设备");
        }

        // 初始化各个管理器
        private void InitializeManagers()
        {
            // 初始化Modbus管理器
            _modbusInitializer = new ModbusManager(
                UpdateStatus,
                ViewModel.UpdateDeviceStatus,
                ViewModel.UpdatePosition
            );

            // 初始化相机管理器
            _cameraInitializer = new CameraInitializer(
                UpdateStatus,
                ViewModel.UpdateCameraStatus
            );
            _cameraInitializer.ImageUpdated += CameraManager_ImageUpdated;

            // 初始化模型管理器
            _modelManager = new ModelManager(
                UpdateStatus,
                ViewModel.UpdateModelStatus,
                UpdateDisplayImage
            );

            // 初始化主循环管理器（延迟创建，需要等其他管理器就绪后）
        }

        // 加载或创建设置
        private void LoadOrCreateSettings()
        {
            try
            {
                // 创建默认设置
                _settings = new Settings();

                // 尝试从设置文件加载
                string settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");
                if (File.Exists(settingsFilePath))
                {
                    LoadSettingsFromFile(settingsFilePath, _settings);
                }
                else
                {
                    // 设置默认模型路径
                    string modelDefaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"C:\Users\91550\Desktop\名片识别\模型\名片识别_20250318_161513.dvt");
                    if (File.Exists(modelDefaultPath))
                    {
                        _settings.ModelPath = modelDefaultPath;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载设置时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _settings = new Settings();
            }
        }

        private void LoadSettingsFromFile(string settingsFilePath, Settings settings)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(settingsFilePath);
                XmlElement root = doc.DocumentElement;

                // 加载Modbus设置
                settings.PortName = GetSettingValue(root, "PortName", settings.PortName);
                settings.BaudRate = int.Parse(GetSettingValue(root, "BaudRate", settings.BaudRate.ToString()));
                settings.DataBits = int.Parse(GetSettingValue(root, "DataBits", settings.DataBits.ToString()));
                settings.StopBits = (StopBits)Enum.Parse(typeof(StopBits), GetSettingValue(root, "StopBits", settings.StopBits.ToString()));
                settings.Parity = (Parity)Enum.Parse(typeof(Parity), GetSettingValue(root, "Parity", settings.Parity.ToString()));
                settings.DeviceId = int.Parse(GetSettingValue(root, "DeviceId", settings.DeviceId.ToString()));

                // 加载相机设置
                settings.CameraIndex = int.Parse(GetSettingValue(root, "CameraIndex", settings.CameraIndex.ToString()));
                settings.UseTrigger = bool.Parse(GetSettingValue(root, "UseTrigger", settings.UseTrigger.ToString()));
                settings.UseSoftTrigger = bool.Parse(GetSettingValue(root, "UseSoftTrigger", settings.UseSoftTrigger.ToString()));

                // 加载模型设置
                settings.ModelPath = GetSettingValue(root, "ModelPath", settings.ModelPath);

                // 加载设备设置
                settings.Speed = float.Parse(GetSettingValue(root, "Speed", settings.Speed.ToString()));

                // 加载目标位置设置
                string targetPositionStr = GetSettingValue(root, "TargetPosition", settings.TargetPosition.ToString());
                if (!string.IsNullOrEmpty(targetPositionStr) && float.TryParse(targetPositionStr, out float targetPos))
                {
                    settings.TargetPosition = targetPos;
                }

                // 加载图像保存设置
                settings.SavePath = GetSettingValue(root, "SavePath", settings.SavePath);
                settings.SaveOKImage = bool.Parse(GetSettingValue(root, "SaveOKImage", settings.SaveOKImage.ToString()));
                settings.SaveNGImage = bool.Parse(GetSettingValue(root, "SaveNGImage", settings.SaveNGImage.ToString()));
                settings.ImageFormat = GetSettingValue(root, "ImageFormat", settings.ImageFormat);
                settings.JpegQuality = GetSettingValue(root, "JpegQuality", settings.JpegQuality);
                
                // 加载拍照延迟设置
                string preCaptureDelayStr = GetSettingValue(root, "PreCaptureDelay", settings.PreCaptureDelay.ToString());
                if (!string.IsNullOrEmpty(preCaptureDelayStr) && int.TryParse(preCaptureDelayStr, out int delay))
                {
                    settings.PreCaptureDelay = delay;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"从文件加载设置时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetSettingValue(XmlElement root, string key, string defaultValue)
        {
            XmlNode node = root.SelectSingleNode(key);
            return node != null ? node.InnerText : defaultValue;
        }

        // 初始化系统
        private async Task InitializeSystemAsync()
        {
            try
            {
                UpdateStatus("正在初始化系统...");

                // 初始化Modbus
                await Task.Run(() => _modbusInitializer.InitializeModbus(_settings));
                _isModbusConnected = _modbusInitializer.IsConnected;

                // 初始化相机
                await Task.Run(() => _cameraInitializer.InitializeCamera(_settings));
                _isCameraConnected = _cameraInitializer.IsConnected;
                _isGrabbing = _cameraInitializer.IsGrabbing;

                // 初始化模型
                await Task.Run(() => _modelManager.InitializeModel(_settings));
                _isModelLoaded = _modelManager.IsLoaded;

                // 创建主循环管理器
                _mainLoopManager = new MainLoopManager(
                    _modbusInitializer,
                    _cameraInitializer,
                    _modelManager,
                    _settings,
                    UpdateStatus,
                    UpdateDetectionResult,
                    UpdateStatisticsCallback,
                    SaveImageAsync
                );

                _isInitialized = true;
                UpdateStatus("系统初始化完成");

                // 更新界面控件状态
                UpdateControlState();
            }
            catch (Exception ex)
            {
                UpdateStatus($"系统初始化错误：{ex.Message}");
                MessageBox.Show($"初始化系统时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 初始化UI状态
        private void InitializeUIState()
        {
            // 初始状态已在ViewModel中设置，不需在此处设置
        }
        #endregion

        #region 回调方法
        // 更新统计信息的回调方法
        private void UpdateStatisticsCallback(bool isOK)
        {
            // 更新统计信息
            _totalCount++;
            if (isOK)
            {
                _okCount++;
            }
            else
            {
                _ngCount++;
            }

            // 计算良率
            _yieldRate = _totalCount > 0 ? (double)_okCount / _totalCount * 100 : 0;

            // 更新ViewModel
            ViewModel.UpdateStatistics(_totalCount, _okCount, _ngCount, _yieldRate);
            ViewModel.UpdateCurrentResult(isOK);
        }

        // 保存图像的异步方法
        private async Task SaveImageAsync(Bitmap image, bool isOK)
        {
            await _mainLoopManager.SaveImageAsync(image, isOK);
        }
        #endregion

        #region UI更新方法
        // 更新状态栏显示
        private void UpdateStatus(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                ViewModel.UpdateStatus(message);
            }
            else
            {
                Dispatcher.Invoke(() => UpdateStatus(message));
            }
        }

        // 更新检测结果显示
        private void UpdateDetectionResult(string result)
        {
            if (Dispatcher.CheckAccess())
            {
                ViewModel.UpdateDetectionResult(result);
                _lastDetectionResult = result;
                UpdateStatus($"推理完成");
            }
            else
            {
                Dispatcher.Invoke(() => UpdateDetectionResult(result));
            }
        }

        // 创建一个专门的方法用于处理图像更新，减少对UI控件的直接引用
        private void HandleImageUpdated(System.Drawing.Image image, dynamic result = null)
        {
            if (image != null)
            {
                _lastCapturedImage = image.Clone() as Bitmap;
            }

            // 在WPF线程上更新UI
            Dispatcher.Invoke(() =>
            {
                // 使用ImageViewer更新图像和检测结果
                if (imageViewer1 != null)
                {
                    imageViewer1.UpdateImage(image);

                    // 如果有检测结果，则更新显示
                    if (result is null)
                    {
                        imageViewer1.ClearResults();
                    }
                    else
                    {
                        // 将result转换为object类型
                        imageViewer1.UpdateResults(result);
                    }

                    // 刷新ImageViewer的显示
                    imageViewer1.Invalidate();
                }
            });
        }

        // 更新显示图像
        private void UpdateDisplayImage(System.Drawing.Image image, dynamic result = null)
        {
            if (Dispatcher.CheckAccess())
            {
                HandleImageUpdated(image, result);
            }
            else
            {
                Dispatcher.Invoke(new Action(() => HandleImageUpdated(image, result)));
            }
        }

        // 更新控件状态
        private void UpdateControlState()
        {
            Dispatcher.Invoke(() =>
            {
                ViewModel.UpdateRunningState(_isRunning);
            });
        }
        #endregion

        #region 事件处理
        // 相机图像更新事件处理
        private void CameraManager_ImageUpdated(object sender, ImageEventArgs e)
        {
            try
            {
                if (Dispatcher.CheckAccess())
                {
                    // 更新显示图像
                    if (e.Image != null)
                    {
                        // 保存最新图像，用于非触发模式
                        _lastCapturedImage = e.Image.Clone() as Bitmap;

                        // 如果是非触发模式，则立即执行推理
                        if (_isRunning && !_settings.UseTrigger)
                        {
                            // 执行AI推理
                            string result = _modelManager.PerformInference(_lastCapturedImage);

                            // 更新检测结果显示
                            UpdateDetectionResult(result);

                            // 判断检测结果
                            bool isOK = string.IsNullOrEmpty(result);

                            // 更新统计信息
                            UpdateStatisticsCallback(isOK);

                            // 根据设置保存图像
                            _ = SaveImageAsync(_lastCapturedImage, isOK);
                        }
                        else
                        {
                            // 仅更新显示图像
                            UpdateDisplayImage(e.Image.Clone() as Bitmap);
                        }
                    }
                }
                else
                {
                    Dispatcher.Invoke(() => CameraManager_ImageUpdated(sender, e));
                }
            }
            catch { }
        }

        // 启动按钮点击事件
        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
                return;

            try
            {
                // 如果系统尚未初始化，则先初始化
                if (!_isInitialized)
                {
                    await InitializeSystemAsync();

                    // 如果初始化失败，则返回
                    if (!_isModbusConnected || !_isCameraConnected || !_isModelLoaded)
                    {
                        UpdateStatus("系统初始化失败，请检查设置");
                        UpdateControlState();
                        return;
                    }
                }

                // 创建取消令牌
                _cts = new CancellationTokenSource();

                // 标记为正在运行
                _isRunning = true;
                UpdateControlState(); // 更新所有按钮状态
                UpdateStatus("系统启动");

                // 启动主循环任务
                await _mainLoopManager.RunMainLoopAsync(_cts.Token, _lastCapturedImage);
            }
            catch (Exception ex)
            {
                UpdateStatus($"启动过程中发生错误：{ex.Message}");
                _isRunning = false;
                UpdateControlState();
            }
        }

        // 停止按钮点击事件
        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 停止运行
                _isRunning = false;
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }

                UpdateStatus("系统已停止");
                UpdateControlState();
            }
            catch (Exception ex)
            {
                UpdateStatus($"停止时发生错误：{ex.Message}");
            }
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 显示确认对话框
                MessageBoxResult result = MessageBox.Show(
                    "确定要清零所有计数吗？",
                    "确认清零",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // 重置所有计数
                    _totalCount = 0;
                    _okCount = 0;
                    _ngCount = 0;
                    _yieldRate = 0.0;

                    // 更新ViewModel
                    ViewModel.UpdateStatistics(_totalCount, _okCount, _ngCount, _yieldRate);

                    UpdateStatus("计数已清零");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"清零时发生错误：{ex.Message}");
            }
        }

        // 设置按钮点击事件
        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 创建设置窗口
                SettingsWindow settingsWindow = new SettingsWindow(_settings);
                settingsWindow.Owner = this;

                // 显示设置窗口
                bool? result = settingsWindow.ShowDialog();

                // 如果设置已保存，则更新设置
                if (settingsWindow.IsSettingsSaved)
                {
                    // 弹出提示
                    MessageBoxResult mbResult = MessageBox.Show(
                        "设置已更改，需要重新初始化系统才能生效。\n是否立即重新初始化？",
                        "设置已更改",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (mbResult == MessageBoxResult.Yes)
                    {
                        // 重置初始化标志
                        _isInitialized = false;

                        // 清理已有资源
                        CleanupResources();

                        // 重置UI状态
                        InitializeUIState();
                    }

                    UpdateStatus("设置已更新");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"打开设置窗口时发生错误：{ex.Message}");
                MessageBox.Show($"设置过程中发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 窗口关闭事件
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // 取消所有操作
                _cts?.Cancel();

                // 清理资源
                CleanupResources();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"关闭过程中发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 清理资源
        private void CleanupResources()
        {
            // 关闭Modbus连接
            if (_isModbusConnected)
            {
                _modbusInitializer?.Close();
                _isModbusConnected = false;
            }

            // 关闭相机
            if (_isCameraConnected)
            {
                _cameraInitializer?.Close();
                _isCameraConnected = false;
                _isGrabbing = false;
            }

            // 释放AI模型
            if (_isModelLoaded)
            {
                _modelManager?.Dispose();
                _isModelLoaded = false;
            }
        }
        #endregion
    }
}
