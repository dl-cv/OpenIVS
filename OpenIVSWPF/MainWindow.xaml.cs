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
using System.Xml;

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
        
        // Modbus通信
        private ModbusApi _modbusApi;
        private bool _isModbusConnected = false;
        private float _currentSpeed = 100.0f; // 当前速度值

        // 相机控制
        private CameraManager _cameraManager;
        private bool _isCameraConnected = false;
        private bool _isGrabbing = false;

        // AI模型
        private Model _model;
        private bool _isModelLoaded = false;
        private string _modelPath;

        // 运行控制
        private bool _isRunning = false;
        private CancellationTokenSource _cts;
        private float _currentPosition = 0;
        
        // 位置序列定义 (1-2-3-2-1循环)
        private readonly float[] _positionSequence = new float[] { 200, 310, 420, 310 };
        private int _currentPositionIndex = 0;
        
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
        }

        #region 初始化方法
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化相机管理器（但不连接相机）
            _cameraManager = new CameraManager();
            _cameraManager.ImageUpdated += CameraManager_ImageUpdated;
            
            // 更新界面状态
            UpdateControlState();
            UpdateStatus("系统已就绪，请点击开始按钮初始化设备");
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
                
                _modelPath = _settings.ModelPath;
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
                await Task.Run(() => InitializeModbus());
                
                // 初始化相机
                await Task.Run(() => InitializeCamera());
                
                // 初始化模型
                await Task.Run(() => InitializeModel());
                
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

        private void InitializeModbus()
        {
            try
            {
                UpdateStatus("正在初始化Modbus设备...");
                
                // 关闭已有连接
                if (_isModbusConnected && _modbusApi != null)
                {
                    _modbusApi.Close();
                    _isModbusConnected = false;
                }
                
                // 初始化ModbusApi
                _modbusApi = new ModbusApi();
                
                // 使用设置中的串口参数
                if (string.IsNullOrEmpty(_settings.PortName))
                {
                    // 如果未设置串口，尝试获取第一个可用的串口
                    string[] ports = SerialPort.GetPortNames();
                    if (ports.Length == 0)
                    {
                        UpdateStatus("未检测到串口设备");
                        UpdateDeviceStatus("未检测到串口");
                        return;
                    }
                    _settings.PortName = ports[0];
                }

                // 设置串口参数
                _modbusApi.SetSerialPort(
                    _settings.PortName,  // 串口
                    _settings.BaudRate,  // 波特率
                    _settings.DataBits,  // 数据位
                    _settings.StopBits,  // 停止位
                    _settings.Parity,    // 校验位
                    (byte)_settings.DeviceId   // 设备ID
                );

                // 打开串口
                if (_modbusApi.Open())
                {
                    _isModbusConnected = true;
                    UpdateStatus($"Modbus设备已连接，串口：{_settings.PortName}");
                    UpdateDeviceStatus("已连接");
                    
                    // 设置当前速度
                    _currentSpeed = _settings.Speed;
                    _modbusApi.WriteFloat(0, _currentSpeed);
                    
                    // 读取当前位置
                    float currentPosition = _modbusApi.ReadFloat(32);
                    _currentPosition = currentPosition;
                    UpdatePositionDisplay(currentPosition);
                }
                else
                {
                    UpdateStatus("Modbus设备连接失败");
                    UpdateDeviceStatus("连接失败");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Modbus初始化错误：{ex.Message}");
                UpdateDeviceStatus("初始化错误");
                throw;
            }
        }

        private void InitializeCamera()
        {
            try
            {
                UpdateStatus("正在初始化相机...");
                
                // 刷新设备列表
                List<IDeviceInfo> deviceList = _cameraManager.RefreshDeviceList();
                
                if (deviceList.Count == 0)
                {
                    UpdateStatus("未检测到相机设备");
                    UpdateCameraStatus("未检测到相机");
                    return;
                }

                // 检查相机索引是否有效
                int cameraIndex = _settings.CameraIndex;
                if (cameraIndex < 0 || cameraIndex >= deviceList.Count)
                {
                    cameraIndex = 0;
                }

                // 连接选中的相机
                bool success = _cameraManager.ConnectDevice(cameraIndex);
                if (success)
                {
                    _isCameraConnected = true;
                    UpdateStatus($"相机已连接：{deviceList[cameraIndex].UserDefinedName}");
                    UpdateCameraStatus("已连接");
                    
                    // 设置触发模式
                    if (_settings.UseTrigger)
                    {
                        TriggerConfig.TriggerMode mode = _settings.UseSoftTrigger
                            ? TriggerConfig.TriggerMode.Software
                            : TriggerConfig.TriggerMode.Line0;
                        
                        _cameraManager.SetTriggerMode(mode);
                        _cameraManager.StartGrabbing();
                        UpdateStatus($"相机设置为{(_settings.UseSoftTrigger ? "软触发" : "硬触发")}模式");
                    }
                    else
                    {
                        // 关闭触发模式
                        _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Off);
                        
                        // 开始抓取图像
                        if (_cameraManager.StartGrabbing())
                        {
                            _isGrabbing = true;
                            UpdateStatus("相机设置为连续采集模式");
                        }
                    }
                }
                else
                {
                    UpdateStatus("相机连接失败");
                    UpdateCameraStatus("连接失败");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"相机初始化错误：{ex.Message}");
                UpdateCameraStatus("初始化错误");
                throw;
            }
        }

        private void InitializeModel()
        {
            try
            {
                UpdateStatus("正在加载AI模型...");
                
                // 检查模型文件是否存在
                if (!File.Exists(_settings.ModelPath))
                {
                    UpdateStatus($"模型文件不存在：{_settings.ModelPath}");
                    UpdateModelStatus("模型文件不存在");
                    return;
                }

                // 加载模型
                _model = new Model(_settings.ModelPath, 0); // 使用第一个GPU设备
                _isModelLoaded = true;
                _modelPath = _settings.ModelPath;
                
                UpdateStatus("AI模型已加载");
                UpdateModelStatus("已加载");
            }
            catch (Exception ex)
            {
                UpdateStatus($"AI模型加载错误：{ex.Message}");
                UpdateModelStatus("加载错误");
                throw;
            }
        }

        // 初始化UI状态
        private void InitializeUIState()
        {
            // 初始状态已在ViewModel中设置，不需在此处设置
        }
        #endregion

        #region 设备控制方法
        // 移动到指定位置
        private async Task<bool> MoveToPositionAsync(float position, CancellationToken token)
        {
            try
            {
                if (!_isModbusConnected)
                    return false;

                // 设置目标位置（地址8，浮点数）
                UpdateStatus($"正在移动到位置：{position}");
                bool resultSetPosition = _modbusApi.WriteFloat(8, position);
                if (!resultSetPosition)
                {
                    UpdateStatus("设置目标位置失败");
                    return false;
                }

                // 发送移动命令（地址50，整数2）
                bool resultCommand = _modbusApi.WriteSingleRegister(50, 2);
                if (!resultCommand)
                {
                    UpdateStatus("发送移动命令失败");
                    return false;
                }

                // 等待移动完成（轮询当前位置）
                bool isReached = false;
                while (!isReached && !token.IsCancellationRequested)
                {
                    // 读取当前位置（地址32，浮点数）
                    float currentPosition = _modbusApi.ReadFloat(32);
                    _currentPosition = currentPosition;
                    
                    // 更新位置显示
                    UpdatePositionDisplay(currentPosition);
                    
                    // 判断是否到达目标位置（允许一定误差）
                    if (Math.Abs(currentPosition - position) < 1.0f)
                    {
                        isReached = true;
                    }
                    else
                    {
                        // 等待100ms再次检查
                        await Task.Delay(100, token);
                    }
                }

                if (isReached)
                {
                    UpdateStatus($"已到达位置：{position}");
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("移动操作已取消");
                return false;
            }
            catch (Exception ex)
            {
                UpdateStatus($"移动过程中发生错误：{ex.Message}");
                return false;
            }
        }

        // 触发相机拍照
        private async Task<Bitmap> CaptureImageAsync(CancellationToken token)
        {
            try
            {
                UpdateStatus("正在捕获图像...");
                
                if (!_isCameraConnected)
                {
                    UpdateStatus("相机未连接，无法捕获图像");
                    return null;
                }
                
                // 根据触发模式执行不同的捕获逻辑
                if (_settings.UseTrigger)
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
                    
                    // 添加超时处理，5秒内如果没有图像返回，则取消
                    using (var timeoutCts = new CancellationTokenSource(1000))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
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
                            UpdateStatus("图像捕获成功");
                            return capturedImage;
                        }
                        catch (OperationCanceledException)
                        {
                            UpdateStatus("图像捕获超时或被取消");
                            return null;
                        }
                    }
                }
                else
                {
                    // 非触发模式：使用最新的图像
                    if (_lastCapturedImage != null)
                    {
                        UpdateStatus("使用最新捕获的图像");
                        return _lastCapturedImage.Clone() as Bitmap;
                    }
                    else
                    {
                        UpdateStatus("等待首次图像捕获...");
                        
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
                        using (var timeoutCts = new CancellationTokenSource(5000))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
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
                                UpdateStatus("图像捕获成功");
                                return capturedImage;
                            }
                            catch (OperationCanceledException)
                            {
                                UpdateStatus("图像捕获超时或被取消");
                                return null;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"图像捕获过程中发生错误：{ex.Message}");
                return null;
            }
        }

        // 执行AI模型推理
        private string PerformInference(Bitmap image)
        {
            try
            {
                if (!_isModelLoaded || image == null)
                    return "无效的输入";

                // 将Bitmap转换为Mat
                Mat mat = BitmapToMat(image);
                
                // 创建批处理列表
                var imageList = new List<Mat> { mat };

                // 执行推理
                dlcv_infer_csharp.Utils.CSharpResult result = _model.InferBatch(imageList);
                
                // 提取结果文本
                StringBuilder sb = new StringBuilder();
                var sampleResults = result.SampleResults[0];
                
                foreach (var item in sampleResults.Results)
                {
                    sb.AppendLine($"{item.CategoryName}: {item.Score:F2}");
                }
                
                // 更新ImageViewer以显示检测结果
                UpdateDisplayImage(image, result);
                
                // 返回结果文本
                return sb.ToString();
            }
            catch (Exception ex)
            {
                UpdateStatus($"AI推理过程中发生错误：{ex.Message}");
                return $"推理错误: {ex.Message}";
            }
        }

        // 将Bitmap转换为Mat
        private Mat BitmapToMat(Bitmap bitmap)
        {
            try
            {
                return BitmapConverter.ToMat(bitmap);
            }
            catch (Exception ex)
            {
                UpdateStatus($"图像转换错误：{ex.Message}");
                return null;
            }
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

        // 更新设备状态显示
        private void UpdateDeviceStatus(string status)
        {
            if (Dispatcher.CheckAccess())
            {
                ViewModel.UpdateDeviceStatus(status);
            }
            else
            {
                Dispatcher.Invoke(() => UpdateDeviceStatus(status));
            }
        }

        // 更新相机状态显示
        private void UpdateCameraStatus(string status)
        {
            if (Dispatcher.CheckAccess())
            {
                ViewModel.UpdateCameraStatus(status);
            }
            else
            {
                Dispatcher.Invoke(() => UpdateCameraStatus(status));
            }
        }

        // 更新模型状态显示
        private void UpdateModelStatus(string status)
        {
            if (Dispatcher.CheckAccess())
            {
                ViewModel.UpdateModelStatus(status);
            }
            else
            {
                Dispatcher.Invoke(() => UpdateModelStatus(status));
            }
        }

        // 更新位置显示
        private void UpdatePositionDisplay(float position)
        {
            if (Dispatcher.CheckAccess())
            {
                ViewModel.UpdatePosition(position);
            }
            else
            {
                Dispatcher.Invoke(() => UpdatePositionDisplay(position));
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
        
        // 更新统计信息
        private void UpdateStatistics(bool isOK)
        {
            if (Dispatcher.CheckAccess())
            {
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
            else
            {
                Dispatcher.Invoke(() => UpdateStatistics(isOK));
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
                            string result = PerformInference(_lastCapturedImage);

                            // 更新检测结果显示
                            UpdateDetectionResult(result);
                            
                            // 根据结果更新统计信息
                            bool isOK = string.IsNullOrEmpty(result);
                            UpdateStatistics(isOK);
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
                await RunMainLoopAsync(_cts.Token);
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
                SettingsWindow settingsWindow = new SettingsWindow(_settings, _cameraManager);
                settingsWindow.Owner = this;
                
                // 显示设置窗口
                bool? result = settingsWindow.ShowDialog();
                
                // 如果设置已保存，则更新设置
                if (settingsWindow.IsSettingsSaved)
                {
                    // 更新设置
                    _settings.PortName = settingsWindow.SelectedPortName;
                    _settings.BaudRate = settingsWindow.BaudRate;
                    _settings.DataBits = settingsWindow.DataBits;
                    _settings.StopBits = settingsWindow.StopBits;
                    _settings.Parity = settingsWindow.Parity;
                    _settings.DeviceId = settingsWindow.DeviceId;
                    _settings.CameraIndex = settingsWindow.SelectedCameraIndex;
                    _settings.UseTrigger = settingsWindow.UseTrigger;
                    _settings.UseSoftTrigger = settingsWindow.UseSoftTrigger;
                    _settings.ModelPath = settingsWindow.ModelPath;
                    _settings.Speed = settingsWindow.Speed;
                    
                    // 如果系统已初始化，则需要重新初始化
                    if (_isInitialized)
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
                _modbusApi?.Close();
                _isModbusConnected = false;
            }
            
            // 关闭相机
            if (_isCameraConnected)
            {
                if (_isGrabbing)
                {
                    _cameraManager?.StopGrabbing();
                    _isGrabbing = false;
                }
                
                _cameraManager?.DisconnectDevice();
                _isCameraConnected = false;
            }
            
            // 释放AI模型
            if (_isModelLoaded)
            {
                _model = null;
                _isModelLoaded = false;
            }
        }
        #endregion

        #region 主循环
        // 主循环任务
        private async Task RunMainLoopAsync(CancellationToken token)
        {
            try
            {
                // 持续运行，直到取消
                while (!token.IsCancellationRequested)
                {
                    // 获取当前位置索引
                    float targetPosition = _positionSequence[_currentPositionIndex];
                    
                    // 移动到目标位置
                    bool moveResult = await MoveToPositionAsync(targetPosition, token);
                    
                    if (moveResult && !token.IsCancellationRequested)
                    {
                        // 到达目标位置后，如果是触发模式才拍照和推理
                        if (_settings.UseTrigger)
                        {
                            // 触发相机拍照
                            UpdateStatus($"在位置 {targetPosition} 进行拍照...");
                            
                            // 异步执行拍照和推理，但不等待结果
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    // 拍照
                                    var image = await CaptureImageAsync(token);
                                    
                                    if (image != null && !token.IsCancellationRequested)
                                    {
                                        // 获取到图像后，执行AI推理
                                        UpdateStatus("执行AI推理...");
                                        string result = PerformInference(image);
                                        
                                        // 更新检测结果显示
                                        UpdateDetectionResult(result);
                                        
                                        // 根据结果更新统计信息
                                        bool isOK = string.IsNullOrEmpty(result);
                                        UpdateStatistics(isOK);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (!token.IsCancellationRequested)
                                    {
                                        UpdateStatus($"拍照或推理过程中发生错误：{ex.Message}");
                                    }
                                }
                            }, token);
                        }
                    }
                    
                    if (token.IsCancellationRequested)
                        break;
                    
                    // 更新位置索引，实现1-2-3-2-1循环
                    _currentPositionIndex = (_currentPositionIndex + 1) % _positionSequence.Length;
                }
            }
            catch (OperationCanceledException)
            {
                // 操作被取消，正常退出
                UpdateStatus("运行被取消");
            }
            catch (Exception ex)
            {
                UpdateStatus($"运行过程中发生错误：{ex.Message}");
            }
            finally
            {
                // 无论如何，确保标记为已停止
                _isRunning = false;
                UpdateControlState();
            }
        }
        #endregion
    }
}
