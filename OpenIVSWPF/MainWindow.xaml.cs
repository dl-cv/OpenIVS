using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Drawing;
using DLCV;
using DLCV.Camera;
using dlcv_infer_csharp;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using MvCameraControl;

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
        private string _modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"C:\Users\91550\Desktop\名片识别\模型\名片识别_20250318_161513.dvt");

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
        
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        #region 初始化方法
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 异步初始化各个组件
            Task.Run(() =>
            {
                InitializeModbus();
                InitializeCamera();
                InitializeModel();

                // 如果所有设备都已准备就绪，启用开始按钮
                UpdateControlState();
            });
        }

        private void InitializeModbus()
        {
            try
            {
                UpdateStatus("正在初始化Modbus设备...");
                
                // 初始化ModbusApi
                _modbusApi = new ModbusApi();
                
                // 获取可用的串口
                string[] ports = SerialPort.GetPortNames();
                if (ports.Length == 0)
                {
                    UpdateStatus("未检测到串口设备");
                    UpdateDeviceStatus("未检测到串口");
                    return;
                }

                // 使用第一个可用的串口，设置为38400,8,1,None
                _modbusApi.SetSerialPort(
                    ports[0],     // 第一个可用的串口
                    38400,        // 波特率
                    8,            // 数据位
                    StopBits.One, // 停止位
                    Parity.None,  // 校验位
                    1             // 设备ID
                );

                // 打开串口
                if (_modbusApi.Open())
                {
                    _isModbusConnected = true;
                    UpdateStatus($"Modbus设备已连接，串口：{ports[0]}");
                    UpdateDeviceStatus("已连接");
                    
                    // 读取当前速度
                    _currentSpeed = _modbusApi.ReadFloat(0);
                    UpdateSpeedDisplay(_currentSpeed);
                }
                else
                {
                    UpdateStatus("Modbus设备连接失败");
                    UpdateDeviceStatus("连接失败");
                }

                float currentPosition = _modbusApi.ReadFloat(32);
                _currentPosition = currentPosition;
                UpdatePositionDisplay(currentPosition);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Modbus初始化错误：{ex.Message}");
                UpdateDeviceStatus("初始化错误");
            }
        }

        private void InitializeCamera()
        {
            try
            {
                UpdateStatus("正在初始化相机...");
                
                // 初始化相机管理器
                _cameraManager = new CameraManager();
                _cameraManager.ImageUpdated += CameraManager_ImageUpdated;
                
                // 刷新设备列表
                List<IDeviceInfo> deviceList = _cameraManager.RefreshDeviceList();
                
                if (deviceList.Count == 0)
                {
                    UpdateStatus("未检测到相机设备");
                    UpdateCameraStatus("未检测到相机");
                    return;
                }

                // 连接第一个可用的相机
                bool success = _cameraManager.ConnectDevice(0); // 连接第一个相机
                if (success)
                {
                    _isCameraConnected = true;
                    UpdateStatus($"相机已连接：{deviceList[0].ModelName}");
                    UpdateCameraStatus("已连接");
                    
                    // 设置为软触发模式
                    _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Software);
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
            }
        }

        private void InitializeModel()
        {
            try
            {
                UpdateStatus("正在加载AI模型...");
                
                // 检查模型文件是否存在
                if (!File.Exists(_modelPath))
                {
                    UpdateStatus($"模型文件不存在：{_modelPath}");
                    UpdateModelStatus("模型文件不存在");
                    return;
                }

                // 加载模型
                _model = new Model(_modelPath, 0); // 使用第一个GPU设备
                _isModelLoaded = true;
                
                UpdateStatus("AI模型已加载");
                UpdateModelStatus("已加载");
            }
            catch (Exception ex)
            {
                UpdateStatus($"AI模型加载错误：{ex.Message}");
                UpdateModelStatus("加载错误");
            }
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
                if (!_isCameraConnected)
                    return null;

                // 如果相机未采集，先开始采集
                if (!_isGrabbing)
                {
                    _isGrabbing = _cameraManager.StartGrabbing();
                    if (!_isGrabbing)
                    {
                        UpdateStatus("开始图像采集失败");
                        return null;
                    }
                }

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
                lblStatus.Text = $"{DateTime.Now.ToString("HH:mm:ss")} - {message}";
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
                lblDeviceStatus.Text = $"设备状态：{status}";
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
                lblCameraStatus.Text = $"相机状态：{status}";
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
                lblModelStatus.Text = $"模型状态：{status}";
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
                lblCurrentPosition.Text = $"当前位置：{position:F1}";
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
                txtResult.Text = $"检测结果：\n{result}";
                _lastDetectionResult = result;
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
                    _okCount++;
                else
                    _ngCount++;
                
                // 计算良率
                _yieldRate = _totalCount > 0 ? (double)_okCount / _totalCount * 100 : 0;
                
                // 更新显示
                lblTotalCount.Text = $"总数:{_totalCount}";
                lblOKCount.Text = $"OK:{_okCount}";
                lblNGCount.Text = $"NG:{_ngCount}";
                lblYieldRate.Text = $"良率:{_yieldRate:F1}%";
            }
            else
            {
                Dispatcher.Invoke(() => UpdateStatistics(isOK));
            }
        }

        // 更新显示图像
        private void UpdateDisplayImage(System.Drawing.Image image, dynamic result = null)
        {
            if (Dispatcher.CheckAccess())
            {
                // 保存图像以供后续使用
                if (image != null)
                {
                    _lastCapturedImage = image.Clone() as Bitmap;
                }

                // 使用ImageViewer更新图像和检测结果
                imageViewer1.UpdateImage(image);

                // 如果有检测结果，则更新显示
                if (result is null)
                {
                    imageViewer1.ClearResults(); 
                }
                else
                {
                    imageViewer1.UpdateResults(result);
                }

                // 刷新ImageViewer的显示
                imageViewer1.Invalidate();
            }
            else
            {
                Dispatcher.Invoke(() => UpdateDisplayImage(image, result));
            }
        }

        // 更新控件状态
        private void UpdateControlState()
        {
            if (Dispatcher.CheckAccess())
            {
                // 只有当所有设备都准备就绪时，才启用开始按钮
                btnStart.IsEnabled = _isModbusConnected && _isCameraConnected && _isModelLoaded && !_isRunning;
                btnStop.IsEnabled = _isRunning;
            }
            else
            {
                Dispatcher.Invoke(() => UpdateControlState());
            }
        }

        // 更新速度显示
        private void UpdateSpeedDisplay(float speed)
        {
            if (Dispatcher.CheckAccess())
            {
                txtSpeed.Text = speed.ToString("F1");
                _currentSpeed = speed;
            }
            else
            {
                Dispatcher.Invoke(() => UpdateSpeedDisplay(speed));
            }
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
                        UpdateDisplayImage(e.Image.Clone() as Bitmap);
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
                // 创建取消令牌
                _cts = new CancellationTokenSource();
                
                // 标记为正在运行
                _isRunning = true;
                UpdateControlState();
                UpdateStatus("系统启动");
                
                // 启动主循环任务
                await RunMainLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                UpdateStatus($"启动过程中发生错误：{ex.Message}");
            }
        }

        // 停止按钮点击事件
        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 请求取消操作
                _cts?.Cancel();
                
                // 标记为已停止运行
                _isRunning = false;
                UpdateControlState();
                UpdateStatus("系统已停止");
            }
            catch (Exception ex)
            {
                UpdateStatus($"停止过程中发生错误：{ex.Message}");
            }
        }

        // 设置速度按钮点击事件
        private void btnSetSpeed_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isModbusConnected)
                {
                    MessageBox.Show("设备未连接，无法设置速度", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!float.TryParse(txtSpeed.Text, out float speed))
                {
                    MessageBox.Show("请输入有效的速度值", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 设置速度（地址0，浮点数）
                bool result = _modbusApi.WriteFloat(0, speed);
                if (result)
                {
                    _currentSpeed = speed;
                    UpdateStatus($"速度已设置为：{speed:F1} mm/s");
                }
                else
                {
                    MessageBox.Show("设置速度失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置速度时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 窗口关闭事件
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // 取消所有操作
                _cts?.Cancel();
                
                // 关闭Modbus连接
                if (_isModbusConnected)
                {
                    _modbusApi.Close();
                    _isModbusConnected = false;
                }
                
                // 关闭相机
                if (_isCameraConnected)
                {
                    if (_isGrabbing)
                    {
                        _cameraManager.StopGrabbing();
                        _isGrabbing = false;
                    }
                    
                    _cameraManager.DisconnectDevice();
                    _isCameraConnected = false;
                }
                
                // 释放AI模型
                if (_isModelLoaded)
                {
                    _model = null;
                    _isModelLoaded = false;
                }
                
                // 释放资源
                _cameraManager?.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"关闭过程中发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        // 到达目标位置后，触发相机拍照
                        UpdateStatus($"在位置 {targetPosition} 进行拍照...");
                        var image = await CaptureImageAsync(token);
                        
                        if (image != null && !token.IsCancellationRequested)
                        {
                            // 获取到图像后，执行AI推理
                            UpdateStatus("执行AI推理...");
                            string result = PerformInference(image);
                            
                            // 更新检测结果显示
                            UpdateDetectionResult(result);
                            
                            // 根据结果更新统计信息（这里简单地假设有结果就是OK）
                            bool isOK = !string.IsNullOrEmpty(result) && !result.Contains("无效") && !result.Contains("错误");
                            UpdateStatistics(isOK);
                            
                            // 等待一段时间，以便查看结果
                            await Task.Delay(1000, token);
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
