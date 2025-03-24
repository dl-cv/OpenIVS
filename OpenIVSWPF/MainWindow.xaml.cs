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

            // 添加设置变更事件处理
            SettingsManager.Instance.SettingsChanged += SettingsManager_SettingsChanged;

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

            // 使用统一的图像捕获事件
            _cameraInitializer.ImageCaptured += CameraInitializer_ImageCaptured;

            // 初始化模型管理器
            _modelManager = new ModelManager(
                UpdateStatus,
                ViewModel.UpdateModelStatus,
                UpdateDisplayImage
            );

            // 初始化主循环管理器（延迟创建，需要等其他管理器就绪后）
        }

        // 设置变更事件处理
        private void SettingsManager_SettingsChanged(object sender, EventArgs e)
        {
            // 如果系统已初始化且正在运行，则提示需要重新初始化
            if (_isInitialized)
            {
                Dispatcher.Invoke(() =>
                {
                    // 如果正在运行，则停止
                    if (_isRunning)
                    {
                        btnStop_Click(this, new RoutedEventArgs());
                    }

                    UpdateStatus("设置已更改，需要重新初始化系统");

                    // 重置初始化标志
                    _isInitialized = false;
                });
            }
        }

        // 初始化系统
        private async Task InitializeSystemAsync()
        {
            try
            {
                UpdateStatus("正在初始化系统...");

                var settings = SettingsManager.Instance.Settings;
                bool isOfflineMode = settings.UseLocalFolder;

                // 初始化Modbus (仅在在线模式下)
                if (!isOfflineMode)
                {
                    await Task.Run(() => _modbusInitializer.InitializeModbus(settings));
                    _isModbusConnected = _modbusInitializer.IsConnected;
                }
                else
                {
                    _isModbusConnected = true; // 离线模式下默认为已连接
                    UpdateStatus("离线模式：跳过Modbus初始化");
                }

                // 初始化相机或本地图像文件夹
                await Task.Run(() => _cameraInitializer.InitializeCamera(settings));
                _isCameraConnected = _cameraInitializer.IsConnected;
                _isGrabbing = _cameraInitializer.IsGrabbing;

                // 初始化模型
                await Task.Run(() => _modelManager.InitializeModel(settings));
                _isModelLoaded = _modelManager.IsLoaded;

                // 创建主循环管理器
                _mainLoopManager = new MainLoopManager(
                    _modbusInitializer,
                    _cameraInitializer,
                    _modelManager,
                    settings,
                    UpdateStatus,
                    UpdateDetectionResult,
                    UpdateStatisticsCallback,
                    SaveImageAsync
                );

                _isInitialized = true;

                if (isOfflineMode)
                {
                    UpdateStatus("离线模式系统初始化完成");
                }
                else
                {
                    UpdateStatus("系统初始化完成");
                }

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
        public void UpdateStatus(string message)
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
        private void CameraInitializer_ImageCaptured(object sender, ImageEventArgs e)
        {
            try
            {
                if (Dispatcher.CheckAccess())
                {
                    // 更新显示图像
                    if (e.Image != null)
                    {
                        // 保存最新图像
                        _lastCapturedImage = e.Image.Clone() as Bitmap;

                        // 如果系统已启动，则由MainLoopManager统一处理推理
                        if (_isRunning)
                        {
                            // 无论何种模式，都统一使用MainLoopManager处理图像
                            // 获取当前模式信息
                            _ = _mainLoopManager.ProcessImageAsync(_lastCapturedImage);
                        }
                        else
                        {
                            // 系统未启动时，仅更新显示图像
                            UpdateDisplayImage(e.Image.Clone() as Bitmap);
                        }
                    }
                }
                else
                {
                    Dispatcher.Invoke(() => CameraInitializer_ImageCaptured(sender, e));
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

                    // 如果模型初始化失败，则返回
                    if (!_isModelLoaded)
                    {
                        UpdateStatus("模型初始化失败，请检查设置");
                        UpdateControlState();
                        return;
                    }

                    // 判断初始化结果：在线模式需要检查所有组件，离线模式只需要检查模型
                    bool isOfflineMode = SettingsManager.Instance.Settings.UseLocalFolder;
                    if (!isOfflineMode && (!_isModbusConnected || !_isCameraConnected))
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

                if (SettingsManager.Instance.Settings.UseLocalFolder)
                {
                    UpdateStatus("离线模式系统启动");

                    // 离线模式下，在启动时先重置图像索引，确保从第一张图像开始
                    _cameraInitializer.ResetImageIndex();

                    // 在离线模式下，不要在这里主动触发第一张图像加载
                    // 由MainLoopManager统一处理所有图像的加载和推理
                }
                else
                {
                    UpdateStatus("系统启动");
                }

                // 启动主循环任务
                await _mainLoopManager.RunMainLoopAsync(_cts.Token, _lastCapturedImage);

                // 检查是否是因为图像用完而返回（只有在离线模式下会出现）
                if (_isRunning && SettingsManager.Instance.Settings.UseLocalFolder
                    && !SettingsManager.Instance.Settings.LoopLocalImages)
                {
                    // 由于图像已经处理完毕，自动停止运行
                    _isRunning = false;
                    UpdateStatus("离线模式: 所有图像已处理完毕，停止检测。");
                    UpdateControlState();
                }
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

                // 若是离线模式，确保重置图像索引，使下次从头开始
                if (SettingsManager.Instance.Settings.UseLocalFolder)
                {
                    _cameraInitializer.ResetImageIndex();
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
                SettingsWindow settingsWindow = new SettingsWindow();
                settingsWindow.Owner = this;

                // 显示设置窗口
                bool? result = settingsWindow.ShowDialog();

                // 更新状态
                UpdateStatus("设置窗口已关闭");
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

                // 移除设置变更事件
                SettingsManager.Instance.SettingsChanged -= SettingsManager_SettingsChanged;
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
