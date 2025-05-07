using System;
using System.Windows.Forms;
using HalconDotNet;
using OpenCvSharp;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Runtime.InteropServices; // 添加此行用于DllImport
using System.Collections.Generic; // 添加此行用于List
using System.Diagnostics; // 用于性能监控
using DLCV; // 引用SimpleLogger命名空间

namespace HalconDemo
{
    public partial class Form1 : Form
    {
        private HObject halconImage = null;  // Halcon图像对象
        private string currentImagePath = ""; // 当前图像路径
        private string currentModelPath = ""; // 当前模型路径
        private Model model = null; // DLCV模型对象
        private int deviceId = 0;  // 默认使用GPU 0

        // 摄像机相关变量
        private HTuple acqHandle = null; // 采集设备句柄
        private bool isLiveMode = false; // 是否处于实时模式
        private bool isInferenceEnabled = false; // 是否启用实时推理
        private Timer liveTimer = null; // 实时显示定时器
        private List<string> availableCameras = new List<string>(); // 可用摄像机列表
        private string currentCameraType = ""; // 当前摄像机类型

        // 保存原始图像尺寸
        private int imageWidth = 0;
        private int imageHeight = 0;

        // 边界框容差值
        private const double BOX_TOLERANCE = 1.0;  // 允许边界框尺寸有1像素的容差

        // 性能监控变量
        private Stopwatch refreshStopwatch = new Stopwatch(); // 图像刷新计时器
        private Stopwatch inferenceStopwatch = new Stopwatch(); // 推理计时器
        private int totalFrameCount = 0; // 总刷新帧数
        private int totalInferenceCount = 0; // 总推理次数
        private double lastRefreshTime = 0; // 上次刷新时间(ms)
        private double lastInferenceTime = 0; // 上次推理时间(ms)
        private double avgRefreshFPS = 0; // 平均刷新帧率
        private double avgInferenceFPS = 0; // 平均推理帧率
        
        // 日志记录器
        private Logger logger = null;

        public Form1()
        {
            InitializeComponent();

            // 初始化Halcon图像对象
            halconImage = new HObject();

            // 初始化日志记录器
            string logDir = Path.Combine(Application.StartupPath, "logs");
            logger = new Logger(logDir, false, DLCV.LogLevel.Info);
            logger.Info("应用程序启动");

            // 初始化实时显示定时器
            liveTimer = new Timer();
            liveTimer.Interval = 50; // 20帧每秒
            liveTimer.Tick += LiveTimer_Tick;

            // 设置异常处理
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;
                logger.LogException(ex, "未处理的异常");
                MessageBox.Show($"发生未处理的异常：{e.ExceptionObject}", "严重错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
        }

        // 表单关闭事件
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            logger.Info("应用程序关闭");
            
            // 停止实时采集
            StopLiveAcquisition();

            // 关闭摄像机
            CloseCamera();

            base.OnFormClosing(e);
        }

        // 获取可用的摄像机接口
        private void GetAvailableCameraInterfaces()
        {
            try
            {
                HTuple interfaces = null;
                HTuple values = null;

                // 尝试获取所有可用的接口
                try
                {
                    HOperatorSet.InfoFramegrabber("all", "info_boards", out interfaces, out values);
                }
                catch (HalconDotNet.HOperatorException)
                {
                    // 如果"all"不起作用，则尝试常见的接口类型
                    List<string> commonInterfaces = new List<string>
                    {
                        "GigEVision", "GigEVision2", "USB3Vision", "DirectShow", "File",
                        "GenICam", "GenTL", "HALCON", "HDS", "PYLON"
                    };

                    // 更新下拉框
                    cmbCameraInterface.Items.Clear();
                    availableCameras.Clear();

                    foreach (string interfaceName in commonInterfaces)
                    {
                        try
                        {
                            HOperatorSet.InfoFramegrabber(interfaceName, "info_boards", out HTuple info, out HTuple vals);
                            if (info != null && info.Length > 0)
                            {
                                cmbCameraInterface.Items.Add(interfaceName);
                                availableCameras.Add(interfaceName);
                            }
                        }
                        catch (HalconDotNet.HOperatorException)
                        {
                            // 忽略不支持的接口
                        }
                    }

                    if (cmbCameraInterface.Items.Count > 0)
                    {
                        cmbCameraInterface.SelectedIndex = 0;
                    }
                    return;
                }

                // 更新下拉框
                cmbCameraInterface.Items.Clear();
                availableCameras.Clear();

                if (interfaces != null && interfaces.Length > 0)
                {
                    for (int i = 0; i < interfaces.Length; i++)
                    {
                        string interfaceName = interfaces[i].S;
                        cmbCameraInterface.Items.Add(interfaceName);
                        availableCameras.Add(interfaceName);
                    }

                    if (cmbCameraInterface.Items.Count > 0)
                    {
                        cmbCameraInterface.SelectedIndex = 0;
                    }
                }
            }
            catch (HalconDotNet.HOperatorException ex)
            {
                MessageBox.Show($"获取摄像机接口时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 获取指定接口的摄像机设备列表
        private List<string> GetCameraDevices(string interfaceName)
        {
            List<string> devices = new List<string>();
            try
            {
                HTuple information = null;
                HTuple valueList = null;

                // 获取设备信息
                HOperatorSet.InfoFramegrabber(interfaceName, "info_boards", out information, out valueList);

                if (valueList != null && valueList.Length > 0)
                {
                    string deviceInfo = valueList[0].S;
                    string[] deviceItems = deviceInfo.Split('|');

                    foreach (string item in deviceItems)
                    {
                        if (item.Trim().StartsWith("device:"))
                        {
                            string deviceId = item.Trim().Substring(7).Trim();
                            if (!string.IsNullOrEmpty(deviceId))
                            {
                                devices.Add(deviceId);
                            }
                        }
                    }
                }

                // 如果没有找到任何设备，添加一个默认设备
                if (devices.Count == 0)
                {
                    devices.Add("default");
                }
            }
            catch (HalconDotNet.HOperatorException ex)
            {
                MessageBox.Show($"获取摄像机设备列表时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                
                // 错误情况下至少添加一个默认设备
                devices.Add("default");
            }

            return devices;
        }

        // 打开摄像机
        private bool OpenCamera(string interfaceName, string deviceId = "default")
        {
            try
            {
                logger.Info($"尝试打开摄像机 - 接口: {interfaceName}, 设备ID: {deviceId}");
                
                // 关闭之前的摄像机
                CloseCamera();

                // 打开新的摄像机
                HOperatorSet.OpenFramegrabber(
                    interfaceName,        // 接口名称
                    1, 1,                // 水平和垂直分辨率
                    0, 0, 0, 0,          // 图像宽度、高度、起始行、起始列
                    "default",           // 场（interlace）
                    -1,                  // 位深度
                    "default",           // 颜色空间
                    -1.0,                // 通用参数
                    "default",           // 外部触发
                    "default",           // 相机类型
                    deviceId,            // 设备ID
                    -1, -1,              // 端口和线路输入
                    out acqHandle);

                currentCameraType = interfaceName;
                
                logger.Info($"摄像机打开成功 - 接口: {interfaceName}, 设备ID: {deviceId}");
                return true;
            }
            catch (HalconDotNet.HOperatorException ex)
            {
                logger.LogException(ex, $"打开摄像机失败 - 接口: {interfaceName}, 设备ID: {deviceId}");
                MessageBox.Show($"打开摄像机时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // 关闭摄像机
        private void CloseCamera()
        {
            try
            {
                if (acqHandle != null)
                {
                    logger.Info("关闭摄像机");
                    HOperatorSet.CloseFramegrabber(acqHandle);
                    acqHandle = null;
                }
            }
            catch (HalconDotNet.HOperatorException ex)
            {
                logger.LogException(ex, "关闭摄像机失败");
                MessageBox.Show($"关闭摄像机时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 启动实时显示
        private void StartLiveAcquisition()
        {
            if (acqHandle == null)
                return;

            try
            {
                logger.Info("开始实时采集");
                
                // 开始图像采集
                HOperatorSet.GrabImageStart(acqHandle, -1);
                
                // 启动定时器
                isLiveMode = true;
                liveTimer.Start();
                
                // 重置性能统计数据
                avgRefreshFPS = 0;
                avgInferenceFPS = 0;
                
                // 更新按钮状态
                btnStartLive.Text = "停止实时";
                btnInfer.Enabled = false;
                btnInferLive.Enabled = true;
            }
            catch (HalconDotNet.HOperatorException ex)
            {
                logger.LogException(ex, "启动实时显示失败");
                MessageBox.Show($"启动实时显示时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 停止实时显示
        private void StopLiveAcquisition()
        {
            try
            {
                // 停止定时器
                liveTimer.Stop();
                isLiveMode = false;
                isInferenceEnabled = false;
                
                // 记录性能统计信息
                if(totalFrameCount > 0)
                {
                    logger.Info($"停止实时采集 - 总计处理帧数: {totalFrameCount}, 总计推理帧数: {totalInferenceCount}, " +
                               $"平均刷新帧率: {avgRefreshFPS:F1} FPS, 平均推理帧率: {avgInferenceFPS:F1} FPS");
                }
                
                // 更新按钮状态
                btnStartLive.Text = "开始实时";
                btnInferLive.Text = "实时推理";
                btnInfer.Enabled = true;
                btnInferLive.Enabled = false;
            }
            catch (Exception ex)
            {
                logger.LogException(ex, "停止实时显示失败");
                MessageBox.Show($"停止实时显示时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 定时器事件 - 抓取和显示图像
        private void LiveTimer_Tick(object sender, EventArgs e)
        {
            if (!isLiveMode || acqHandle == null)
                return;

            try
            {
                // 开始图像刷新计时
                refreshStopwatch.Restart();
                
                // 释放halconImage
                halconImage.Dispose();

                // 获取图像
                HOperatorSet.GrabImageAsync(out halconImage, acqHandle, -1);
                
                // 获取图像尺寸
                HTuple width = new HTuple(), height = new HTuple();
                HOperatorSet.GetImageSize(halconImage, out width, out height);
                imageWidth = width.I;
                imageHeight = height.I;

                // 显示图像
                RefreshDisplayImage();
                
                // 图像刷新计时结束
                refreshStopwatch.Stop();
                lastRefreshTime = refreshStopwatch.ElapsedMilliseconds;
                totalFrameCount++;
                
                // 计算平均刷新帧率（使用移动平均）
                avgRefreshFPS = 0.9 * avgRefreshFPS + 0.1 * (1000.0 / Math.Max(1, lastRefreshTime));
                
                // 如果开启了实时推理，执行推理
                if (isInferenceEnabled && model != null)
                {
                    // 开始推理计时
                    inferenceStopwatch.Restart();
                    
                    Utils.CSharpResult result = InferWithHalconImage(halconImage);
                    DisplayResults(result);
                    
                    // 推理计时结束
                    inferenceStopwatch.Stop();
                    lastInferenceTime = inferenceStopwatch.ElapsedMilliseconds;
                    totalInferenceCount++;
                    
                    // 计算平均推理帧率（使用移动平均）
                    avgInferenceFPS = 0.9 * avgInferenceFPS + 0.1 * (1000.0 / Math.Max(1, lastInferenceTime));
                    
                    // 更新性能信息显示
                    UpdatePerformanceDisplay();
                    
                    // 每100帧记录一次性能数据
                    if(totalInferenceCount % 100 == 0)
                    {
                        logger.Info($"性能统计 - 已处理帧数: {totalFrameCount}, 已推理帧数: {totalInferenceCount}, " +
                                   $"刷新帧率: {avgRefreshFPS:F1} FPS, 推理帧率: {avgInferenceFPS:F1} FPS, " +
                                   $"刷新耗时: {lastRefreshTime:F1} ms, 推理耗时: {lastInferenceTime:F1} ms");
                    }
                }
                else
                {
                    // 仅更新图像刷新性能信息
                    UpdatePerformanceDisplay();
                }
            }
            catch (HalconDotNet.HOperatorException ex)
            {
                // 采集错误，停止实时显示
                liveTimer.Stop();
                isLiveMode = false;
                logger.LogException(ex, "图像采集错误");
                MessageBox.Show($"图像采集时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 窗口大小改变事件处理
        private void Form1_Resize(object sender, EventArgs e)
        {
            // 重新显示图像
            RefreshDisplayImage();
        }

        // HWindowControl大小改变事件处理
        private void hWindowControl_SizeChanged(object sender, EventArgs e)
        {
            // 更新HWindowControl的WindowSize属性，确保与实际大小一致
            hWindowControl.WindowSize = new System.Drawing.Size(hWindowControl.Width, hWindowControl.Height);

            // 重新显示图像
            RefreshDisplayImage();
        }

        // 窗口加载事件
        private void Form1_Load(object sender, EventArgs e)
        {
            // 获取可用的摄像机接口
            GetAvailableCameraInterfaces();
        }

        // 刷新摄像机按钮点击事件
        private void btnRefreshCameras_Click(object sender, EventArgs e)
        {
            GetAvailableCameraInterfaces();
        }

        // 摄像机接口选择变更事件
        private void cmbCameraInterface_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbCameraInterface.SelectedItem != null)
            {
                string selectedInterface = cmbCameraInterface.SelectedItem.ToString();
                List<string> devices = GetCameraDevices(selectedInterface);
                
                cmbCameraDevice.Items.Clear();
                
                if (devices.Count > 0)
                {
                    foreach (string device in devices)
                    {
                        cmbCameraDevice.Items.Add(device);
                    }
                    cmbCameraDevice.SelectedIndex = 0;
                }
                else
                {
                    // 如果没有找到设备，添加一个默认选项
                    cmbCameraDevice.Items.Add("default");
                    cmbCameraDevice.SelectedIndex = 0;
                }
            }
        }

        // 连接摄像机按钮点击事件
        private void btnConnectCamera_Click(object sender, EventArgs e)
        {
            if (cmbCameraInterface.SelectedItem == null)
            {
                MessageBox.Show("请先选择摄像机接口！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string selectedInterface = cmbCameraInterface.SelectedItem.ToString();
            string selectedDevice = "default";
            
            if (cmbCameraDevice.SelectedItem != null)
            {
                selectedDevice = cmbCameraDevice.SelectedItem.ToString();
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                Application.DoEvents();

                // 尝试打开摄像机
                if (OpenCamera(selectedInterface, selectedDevice))
                {
                    MessageBox.Show("摄像机连接成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    btnStartLive.Enabled = true;
                }
                
                Cursor = Cursors.Default;
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show($"连接摄像机时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 开始/停止实时显示按钮点击事件
        private void btnStartLive_Click(object sender, EventArgs e)
        {
            if (acqHandle == null)
            {
                MessageBox.Show("请先连接摄像机！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (isLiveMode)
            {
                // 如果当前是实时模式，则停止
                StopLiveAcquisition();
            }
            else
            {
                // 如果当前不是实时模式，则启动
                StartLiveAcquisition();
            }
        }

        // 实时推理按钮点击事件
        private void btnInferLive_Click(object sender, EventArgs e)
        {
            if (!isLiveMode)
            {
                MessageBox.Show("请先开始实时显示！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (model == null)
            {
                MessageBox.Show("请先加载模型！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 切换实时推理状态
            isInferenceEnabled = !isInferenceEnabled;
            
            if (isInferenceEnabled)
            {
                logger.Info("开始实时推理");
                btnInferLive.Text = "停止推理";
            }
            else
            {
                logger.Info($"停止实时推理 - 已推理帧数: {totalInferenceCount}, 平均推理帧率: {avgInferenceFPS:F1} FPS");
                btnInferLive.Text = "实时推理";
                // 重新显示图像，清除推理结果
                RefreshDisplayImage();
            }
        }

        // 刷新显示图像
        private void RefreshDisplayImage()
        {
            if (halconImage != null && halconImage.IsInitialized())
            {
                try
                {
                    // 获取图像尺寸
                    HTuple width = new HTuple(), height = new HTuple();
                    HOperatorSet.GetImageSize(halconImage, out width, out height);

                    // 保存原始图像尺寸以便后续处理
                    imageWidth = width.I;
                    imageHeight = height.I;

                    // 关闭图形刷新，避免闪烁
                    HOperatorSet.SetSystem("flush_graphic", "false");

                    // 清空显示窗口
                    hWindowControl.HalconWindow.ClearWindow();

                    // 获取窗口尺寸
                    HTuple winRow = new HTuple(), winCol = new HTuple(), winWidth = new HTuple(), winHeight = new HTuple();
                    HOperatorSet.GetWindowExtents(hWindowControl.HalconWindow, out winRow, out winCol, out winWidth, out winHeight);

                    // 计算保持长宽比的显示区域
                    int partWidth, partHeight;
                    if (winWidth.I < winHeight.I)
                    {
                        partWidth = imageWidth;
                        partHeight = (int)((double)imageWidth * winHeight.I / winWidth.I);

                        // 确保高度不超过图像实际高度的范围
                        if (partHeight < imageHeight)
                        {
                            partHeight = imageHeight;
                            partWidth = (int)((double)imageHeight * winWidth.I / winHeight.I);
                        }
                    }
                    else
                    {
                        partHeight = imageHeight;
                        partWidth = (int)((double)imageHeight * winWidth.I / winHeight.I);

                        // 确保宽度不超过图像实际宽度的范围
                        if (partWidth < imageWidth)
                        {
                            partWidth = imageWidth;
                            partHeight = (int)((double)imageWidth * winHeight.I / winWidth.I);
                        }
                    }

                    // 计算居中显示的起始位置
                    int startRow = (partHeight - imageHeight) / 2;
                    int startCol = (partWidth - imageWidth) / 2;

                    // 设置窗口显示区域，保持图像的长宽比
                    HOperatorSet.SetPart(hWindowControl.HalconWindow, -startRow, -startCol, partHeight - startRow - 1, partWidth - startCol - 1);

                    // 显示图像
                    HOperatorSet.DispObj(halconImage, hWindowControl.HalconWindow);

                    // 重新开启图形刷新
                    HOperatorSet.SetSystem("flush_graphic", "true");
                }
                catch (HalconDotNet.HOperatorException ex)
                {
                    MessageBox.Show($"显示图像时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // 选择图像按钮点击事件
        private void btnSelectImage_Click(object sender, EventArgs e)
        {
            // 如果处于实时模式，先停止
            if (isLiveMode)
            {
                StopLiveAcquisition();
            }

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "图像文件|*.png;*.bmp;*.tif;*.tiff;*.jpg;*.jpeg|所有文件|*.*";
                openFileDialog.Title = "选择图像文件";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    currentImagePath = openFileDialog.FileName;
                    txtImagePath.Text = currentImagePath;

                    // 清空之前的结果
                    lblResult.Text = "推理结果：";

                    // 加载并显示图像
                    LoadAndDisplayImage(currentImagePath);
                }
            }
        }

        // 选择模型按钮点击事件
        private void btnSelectModel_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "模型文件|*.dvt|所有文件|*.*";
                openFileDialog.Title = "选择模型文件";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    currentModelPath = openFileDialog.FileName;
                    txtModelPath.Text = currentModelPath;

                    // 释放之前的模型（如果有）
                    if (model != null)
                    {
                        Utils.FreeAllModels();
                        model = null;
                    }

                    // 立即加载模型
                    try
                    {
                        Cursor = Cursors.WaitCursor;
                        lblResult.Text = "正在加载模型...";
                        Application.DoEvents();

                        LoadModel();

                        // 如果当前正在进行实时显示，启用实时推理按钮
                        if (isLiveMode)
                        {
                            btnInferLive.Enabled = true;
                        }

                        Cursor = Cursors.Default;
                    }
                    catch (Exception ex)
                    {
                        Cursor = Cursors.Default;
                        logger.LogException(ex, "选择并加载模型失败");
                        MessageBox.Show($"加载模型时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        lblResult.Text = "模型加载失败";
                    }
                }
            }
        }

        // 开始推理按钮点击事件
        private void btnInfer_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentImagePath))
            {
                MessageBox.Show("请先选择图像文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (halconImage == null || !halconImage.IsInitialized())
            {
                MessageBox.Show("图像未正确加载！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrEmpty(currentModelPath) || model == null)
            {
                MessageBox.Show("请先选择并加载模型文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                lblResult.Text = "推理结果：正在推理中...";
                Application.DoEvents();

                // 开始推理计时
                inferenceStopwatch.Restart();
                
                // 执行推理
                Utils.CSharpResult result = InferWithHalconImage(halconImage);

                // 推理计时结束
                inferenceStopwatch.Stop();
                lastInferenceTime = inferenceStopwatch.ElapsedMilliseconds;
                totalInferenceCount++;
                
                // 显示推理结果
                DisplayResults(result);
                
                // 更新性能信息
                UpdatePerformanceDisplay();
                
                // 记录推理信息
                logger.Info($"单张图像推理完成 - 图像: {Path.GetFileName(currentImagePath)}, 耗时: {lastInferenceTime:F1} ms");

                Cursor = Cursors.Default;
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                logger.LogException(ex, "单张图像推理失败");
                MessageBox.Show($"推理过程中发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblResult.Text = "推理结果：推理失败";
            }
        }

        // 加载并显示图像
        private void LoadAndDisplayImage(string imagePath)
        {
            try
            {
                // 检查文件是否存在
                if (!File.Exists(imagePath))
                {
                    MessageBox.Show($"文件不存在：{imagePath}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 清空之前的图像
                if (halconImage != null && halconImage.IsInitialized())
                {
                    halconImage.Dispose();
                }

                // 尝试使用Halcon原生方法读取图像
                bool halconReadSuccess = false;
                try
                {
                    HOperatorSet.ReadImage(out halconImage, imagePath);
                    if (halconImage.IsInitialized())
                    {
                        halconReadSuccess = true;
                    }
                }
                catch (HalconDotNet.HOperatorException)
                {
                    // Halcon读取失败，稍后会使用OpenCV读取
                    halconReadSuccess = false;
                }

                // 如果Halcon无法读取，尝试使用OpenCV读取
                if (!halconReadSuccess)
                {
                    try
                    {
                        // 使用OpenCV读取图像
                        using (Mat opencvImage = Cv2.ImRead(imagePath))
                        {
                            if (opencvImage.Empty())
                            {
                                throw new Exception("无法读取图像");
                            }

                            // 直接使用MatToHalcon方法转换图像
                            halconImage = MatToHalcon(opencvImage);

                            if (!halconImage.IsInitialized())
                            {
                                throw new Exception("OpenCV图像转换Halcon图像失败");
                            }
                        }
                    }
                    catch (Exception cvEx)
                    {
                        throw new Exception($"无法读取图像：{cvEx.Message}");
                    }
                }

                // 记录图像尺寸
                HTuple width = new HTuple(), height = new HTuple();
                HOperatorSet.GetImageSize(halconImage, out width, out height);
                imageWidth = width.I;
                imageHeight = height.I;

                // 刷新显示图像
                RefreshDisplayImage();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图像时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 加载模型
        private void LoadModel()
        {
            try
            {
                if (string.IsNullOrEmpty(currentModelPath))
                {
                    throw new Exception("模型路径为空");
                }

                // 检查文件是否存在
                if (!File.Exists(currentModelPath))
                {
                    throw new Exception($"模型文件不存在：{currentModelPath}");
                }

                logger.Info($"开始加载模型: {Path.GetFileName(currentModelPath)}");
                
                // 释放之前的模型（如果有）
                if (model != null)
                {
                    Utils.FreeAllModels();
                }

                // 创建新模型
                model = new Model(currentModelPath, deviceId);

                // 获取模型信息
                JObject modelInfo = model.GetModelInfo();
                if (modelInfo == null)
                {
                    throw new Exception("无法获取模型信息");
                }

                string modelType = modelInfo["model_type"]?.ToString() ?? "未知";
                
                logger.Info($"模型加载成功 - 类型: {modelType}, 设备ID: {deviceId}");

                lblResult.Text = $"推理结果：模型加载成功，类型：{modelType}";
            }
            catch (Exception ex)
            {
                model = null;
                logger.LogException(ex, "加载模型失败");
                throw new Exception($"加载模型失败：{ex.Message}");
            }
        }

        // 使用Halcon图像进行推理
        private Utils.CSharpResult InferWithHalconImage(HObject halconImage)
        {
            if (model == null)
            {
                string errorMsg = "模型未加载";
                logger.Error(errorMsg);
                throw new Exception(errorMsg);
            }

            try
            {
                // 将Halcon图像转换为OpenCV Mat
                Mat halconAsMat = HalconImageToMat(halconImage);

                if (halconAsMat == null || halconAsMat.Empty())
                {
                    string errorMsg = "图像转换失败";
                    logger.Error(errorMsg);
                    throw new Exception(errorMsg);
                }

                // 执行推理
                Utils.CSharpResult result = model.Infer(halconAsMat);

                // 释放Mat资源
                halconAsMat.Dispose();

                return result;
            }
            catch (Exception ex)
            {
                logger.LogException(ex, "推理过程异常");
                throw;
            }
        }

        // Halcon图像转换为OpenCV Mat（参考自程序示例）
        private Mat HalconImageToMat(HObject halconImage)
        {
            // 获取Halcon图像的属性
            HTuple width = new HTuple(), height = new HTuple(), channels = new HTuple();
            HTuple type = new HTuple(), imagePointer = new HTuple();

            HOperatorSet.GetImageSize(halconImage, out width, out height);
            HOperatorSet.CountChannels(halconImage, out channels);

            if (channels.I == 1)
            {
                // 单通道灰度图
                HOperatorSet.GetImagePointer1(halconImage, out imagePointer, out type, out width, out height);
                Mat matGray = new Mat(height.I, width.I, MatType.CV_8UC1);
                int imageSize = width.I * height.I;
                unsafe
                {
                    CopyMemory(matGray.Data, new IntPtr((byte*)imagePointer.IP), imageSize);
                }

                return matGray;
            }
            else if (channels.I == 3)
            {
                // 三通道彩色图
                HTuple pointerR = new HTuple(), pointerG = new HTuple(), pointerB = new HTuple();
                HOperatorSet.GetImagePointer3(halconImage, out pointerR, out pointerG, out pointerB, out type, out width, out height);

                Mat matR = new Mat(height.I, width.I, MatType.CV_8UC1);
                Mat matG = new Mat(height.I, width.I, MatType.CV_8UC1);
                Mat matB = new Mat(height.I, width.I, MatType.CV_8UC1);
                int imageSize = width.I * height.I;
                unsafe
                {
                    CopyMemory(matR.Data, new IntPtr((byte*)pointerR.IP), imageSize);
                    CopyMemory(matG.Data, new IntPtr((byte*)pointerG.IP), imageSize);
                    CopyMemory(matB.Data, new IntPtr((byte*)pointerB.IP), imageSize);
                }
                Mat matRGB = new Mat();
                Cv2.Merge(new Mat[] { matR, matG, matB }, matRGB);

                // 释放临时Mat
                matR.Dispose();
                matG.Dispose();
                matB.Dispose();

                return matRGB;
            }
            else
            {
                throw new Exception($"不支持的通道数: {channels.I}");
            }
        }

        // 在Halcon窗口上显示推理结果
        private void DisplayResults(Utils.CSharpResult result)
        {
            try
            {
                // 关闭图形刷新，避免闪烁
                HOperatorSet.SetSystem("flush_graphic", "false");

                // 首先重新显示原始图像
                RefreshDisplayImage();

                // 如果没有结果，直接返回
                if (result.SampleResults == null || result.SampleResults.Count == 0 ||
                    result.SampleResults[0].Results == null || result.SampleResults[0].Results.Count == 0)
                {
                    lblResult.Text = "推理结果：未检测到任何目标";
                    // 开启图形刷新
                    HOperatorSet.SetSystem("flush_graphic", "true");
                    return;
                }

                var sampleResult = result.SampleResults[0]; // 假设只有一个样本结果
                int objectCount = sampleResult.Results.Count;
                lblResult.Text = $"推理结果：检测到 {objectCount} 个目标";

                // 为每个检测结果设置不同的颜色
                string[] colors = new string[] {
                    "red", "blue", "yellow", "cyan", "magenta", "orange"
                };

                // 设置文本显示参数
                HOperatorSet.SetFont(hWindowControl.HalconWindow, "Arial-Bold-16");

                // 处理每个检测结果
                for (int i = 0; i < sampleResult.Results.Count; i++)
                {
                    var obj = sampleResult.Results[i];
                    string color = colors[i % colors.Length]; // 循环使用颜色

                    // 设置当前的绘图颜色
                    HOperatorSet.SetColor(hWindowControl.HalconWindow, color);

                    // 如果有边界框，处理边界框和掩码
                    if (obj.Bbox != null && obj.Bbox.Count >= 4)
                    {
                        // 边界框坐标 [x, y, width, height]
                        double x = obj.Bbox[0];
                        double y = obj.Bbox[1];
                        double width = obj.Bbox[2];
                        double height = obj.Bbox[3];

                        // 验证坐标值的有效性
                        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(width) || double.IsNaN(height))
                        {
                            continue;
                        }

                        // 验证宽度和高度
                        if (width <= BOX_TOLERANCE || height <= BOX_TOLERANCE)
                        {
                            // 修复太小的边界框
                            if (width <= BOX_TOLERANCE) width = BOX_TOLERANCE;
                            if (height <= BOX_TOLERANCE) height = BOX_TOLERANCE;
                        }

                        // 生成矩形区域
                        HObject rectangle;
                        HOperatorSet.GenRectangle1(out rectangle, y, x, y + height, x + width);

                        // 设置线宽和绘制样式
                        HOperatorSet.SetLineWidth(hWindowControl.HalconWindow, 2);
                        HOperatorSet.SetDraw(hWindowControl.HalconWindow, "margin");

                        // 准备类别和置信度文本
                        string text = $"{obj.CategoryName}: {obj.Score:F2}";

                        // 处理掩码
                        if (obj.WithMask && obj.Mask != null && !obj.Mask.Empty())
                        {
                            try
                            {
                                if (obj.Mask.Width != width || obj.Mask.Height != height)
                                {
                                    // 如果掩码尺寸不匹配，进行缩放
                                    Cv2.Resize(obj.Mask, obj.Mask, new OpenCvSharp.Size(width, height));
                                }
                                // 直接将OpenCV掩码转换为Halcon图像
                                HObject maskHalcon = MatToHalcon(obj.Mask);

                                // 对掩码进行阈值分割，创建区域
                                HObject maskRegion;
                                // 使用binary_threshold而不是简单的threshold
                                HTuple usedThreshold = new HTuple();
                                HOperatorSet.BinaryThreshold(maskHalcon, out maskRegion, "max_separability", "light", out usedThreshold);

                                // 检查区域是否为空
                                HTuple area, row, column;
                                HOperatorSet.AreaCenter(maskRegion, out area, out row, out column);

                                if (area.D <= 0)
                                {
                                    // 如果区域为空，尝试反转阈值
                                    HOperatorSet.BinaryThreshold(maskHalcon, out maskRegion, "max_separability", "dark", out usedThreshold);
                                    HOperatorSet.AreaCenter(maskRegion, out area, out row, out column);
                                }

                                // 如果提取到有效区域
                                if (area.D > 0)
                                {
                                    // 缩放区域到正确的尺寸和位置
                                    HObject scaledRegion;
                                    double scaleX = width / obj.Mask.Width;
                                    double scaleY = height / obj.Mask.Height;

                                    // 缩放区域
                                    HOperatorSet.ZoomRegion(maskRegion, out scaledRegion, scaleY, scaleX);

                                    // 移动区域到目标位置
                                    HObject movedRegion;
                                    HOperatorSet.MoveRegion(scaledRegion, out movedRegion, y, x);

                                    // 设置绘制属性并显示区域
                                    HOperatorSet.SetDraw(hWindowControl.HalconWindow, "margin");
                                    HOperatorSet.SetColor(hWindowControl.HalconWindow, color);
                                    HOperatorSet.DispObj(movedRegion, hWindowControl.HalconWindow);

                                    // 释放资源
                                    scaledRegion.Dispose();
                                    movedRegion.Dispose();
                                }

                                // 释放资源
                                maskHalcon.Dispose();
                                maskRegion.Dispose();
                            }
                            catch (Exception ex)
                            {
                                // 出错时继续，只显示边界框和文字
                                System.Diagnostics.Debug.WriteLine($"掩码处理错误: {ex.Message}");
                            }
                        }

                        // 显示边界框
                        HOperatorSet.SetColor(hWindowControl.HalconWindow, color);
                        HOperatorSet.SetDraw(hWindowControl.HalconWindow, "margin");
                        HOperatorSet.SetLineWidth(hWindowControl.HalconWindow, 2);
                        HOperatorSet.DispObj(rectangle, hWindowControl.HalconWindow);

                        // 显示类别和置信度 - 使用正确的位置
                        HOperatorSet.SetColor(hWindowControl.HalconWindow, color);
                        // 将"window"改为"image"，确保文本位置与图像坐标系一致
                        // 同时调整文本位置，确保显示在边界框正上方
                        double textY = Math.Max(5, y - 15); // 确保文本不会超出图像顶部
                        HOperatorSet.DispText(hWindowControl.HalconWindow, text, "image", textY, x, color, new HTuple(), new HTuple());

                        // 释放矩形资源
                        rectangle.Dispose();
                    }
                    else
                    {
                        // 如果没有边界框，只在图像上方显示类别和分数
                        string text = $"{obj.CategoryName}: {obj.Score:F2}";
                        int yPos = 20 + i * 30; // 每行文本的垂直位置
                        HOperatorSet.SetColor(hWindowControl.HalconWindow, color);
                        HOperatorSet.DispText(hWindowControl.HalconWindow, text, "image", yPos, 10, color, new HTuple(), new HTuple());
                    }
                }

                // 开启图形刷新，一次性更新所有绘制内容
                HOperatorSet.SetSystem("flush_graphic", "true");
            }
            catch (Exception ex)
            {
                // 确保图形刷新被重新开启
                HOperatorSet.SetSystem("flush_graphic", "true");
                MessageBox.Show($"显示结果时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // CopyMemory方法，用于内存复制
        [DllImport("kernel32.dll")]
        public static extern void CopyMemory(IntPtr dest, IntPtr source, int size);

        // Mat转换为Halcon图像
        private HObject MatToHalcon(Mat mat)
        {
            HObject hImage = new HObject();

            try
            {
                if (mat.Channels() == 1)
                {
                    // 单通道灰度图处理
                    IntPtr ptr = mat.Data;
                    int width = mat.Cols;
                    int height = mat.Rows;
                    HOperatorSet.GenImage1(out hImage, "byte", width, height, ptr);
                }
                else if (mat.Channels() == 3)
                {
                    // 三通道彩色图处理 - 注意OpenCV是BGR顺序，Halcon是RGB顺序
                    Mat[] channels = new Mat[3];
                    Cv2.Split(mat, out channels);

                    // OpenCV的顺序是BGR，而Halcon需要RGB顺序
                    // 所以channels[0]是B通道，channels[1]是G通道，channels[2]是R通道
                    IntPtr ptrB = channels[0].Data;
                    IntPtr ptrG = channels[1].Data;
                    IntPtr ptrR = channels[2].Data;

                    int width = mat.Cols;
                    int height = mat.Rows;

                    // GenImage3的参数顺序应该是红绿蓝
                    HOperatorSet.GenImage3(out hImage, "byte", width, height, ptrR, ptrG, ptrB);

                    // 释放通道资源
                    foreach (Mat channel in channels)
                    {
                        channel.Dispose();
                    }
                }
                else
                {
                    throw new Exception($"不支持的通道数：{mat.Channels()}");
                }
            }
            catch (Exception ex)
            {
                if (hImage != null && hImage.IsInitialized())
                {
                    hImage.Dispose();
                }
                throw new Exception($"Mat转Halcon图像失败: {ex.Message}");
            }

            return hImage;
        }

        // 鼠标滚轮事件处理
        private void hWindowControl_MouseWheel(object sender, MouseEventArgs e)
        {
            if (halconImage == null || !halconImage.IsInitialized())
                return;

            try
            {
                // 获取当前显示区域
                HTuple row1 = new HTuple(), col1 = new HTuple(), row2 = new HTuple(), col2 = new HTuple();
                HOperatorSet.GetPart(hWindowControl.HalconWindow, out row1, out col1, out row2, out col2);

                // 计算当前显示的宽度和高度
                double currentWidth = col2.D - col1.D + 1;
                double currentHeight = row2.D - row1.D + 1;

                // 计算缩放因子（滚轮向上放大，向下缩小）
                double zoomFactor = e.Delta > 0 ? 0.9 : 1.1; // 缩放比例

                // 计算鼠标位置对应的图像坐标
                HTuple mouseRow = new HTuple(), mouseCol = new HTuple();
                HOperatorSet.ConvertCoordinatesWindowToImage(
                    hWindowControl.HalconWindow,
                    e.Y, e.X,
                    out mouseRow, out mouseCol);

                // 计算新的显示区域
                double newWidth = currentWidth * zoomFactor;
                double newHeight = currentHeight * zoomFactor;

                // 计算新的显示区域的起始位置，使鼠标位置保持在同一个图像点上
                double rowDiff = mouseRow.D - row1.D;
                double colDiff = mouseCol.D - col1.D;

                double newRow1 = mouseRow.D - rowDiff * zoomFactor;
                double newCol1 = mouseCol.D - colDiff * zoomFactor;
                double newRow2 = newRow1 + newHeight - 1;
                double newCol2 = newCol1 + newWidth - 1;

                // 关闭图形刷新，避免闪烁
                HOperatorSet.SetSystem("flush_graphic", "false");

                // 清空窗口
                hWindowControl.HalconWindow.ClearWindow();

                // 设置新的显示区域
                HOperatorSet.SetPart(hWindowControl.HalconWindow, newRow1, newCol1, newRow2, newCol2);

                // 显示图像
                HOperatorSet.DispObj(halconImage, hWindowControl.HalconWindow);

                // 开启图形刷新
                HOperatorSet.SetSystem("flush_graphic", "true");
            }
            catch (HalconDotNet.HOperatorException ex)
            {
                // 确保图形刷新被重新开启
                HOperatorSet.SetSystem("flush_graphic", "true");
                MessageBox.Show($"缩放图像时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 更新性能显示信息
        private void UpdatePerformanceDisplay()
        {
            if (isInferenceEnabled && model != null)
            {
                // 显示完整的性能信息
                lblResult.Text = $"推理结果: 刷新速度: {avgRefreshFPS:F1} FPS ({lastRefreshTime:F1} ms), " +
                                $"推理速度: {avgInferenceFPS:F1} FPS ({lastInferenceTime:F1} ms), " +
                                $"累计推理: {totalInferenceCount} 帧";
            }
            else
            {
                // 仅显示图像刷新性能
                lblResult.Text = $"推理结果: 刷新速度: {avgRefreshFPS:F1} FPS ({lastRefreshTime:F1} ms), " +
                                $"累计推理: {totalInferenceCount} 帧";
            }
        }
    }
}
