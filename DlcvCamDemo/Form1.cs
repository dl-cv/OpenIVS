using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using MvCameraControl;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.IO;
using static dlcv_infer_csharp.Utils;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Net.Http;
using System.Net;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace DlcvCamDemo
{
    /// <summary>
    /// 工业视觉界面主窗体
    /// </summary>
    public partial class Form1 : Form
    {
        #region 成员变量

        // 核心组件
        private Inference _inference = new Inference();
        private CameraManager _cameraManager = new CameraManager();
        private List<IDeviceInfo> _deviceInfoList = new List<IDeviceInfo>();

        // 统计和计时
        private System.Threading.Timer _statsTimer;
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private int _frameCounter = 0;
        private DateTime _fpsStartTime = DateTime.Now;
        private long _totalProcessed = 0;

        // 触发循环和压力测试状态
        private bool _isRunning = false;
        private PressureTestRunner _pressureTestRunner;

        // FPS统计
        private readonly int[] _lastTenDeltas = new int[10];
        private int _deltaIndex = 0;
        private readonly object _lock = new object();

        #endregion

        #region 初始化和基础功能

        /// <summary>
        /// 窗体构造函数
        /// </summary>
        public Form1()
        {
            InitializeComponent();
            _statsTimer = new System.Threading.Timer(OnStatsTimerCallback, null, 300, 300);
            InitializeCameraManager();
        }

        /// <summary>
        /// 初始化相机管理器
        /// </summary>
        private void InitializeCameraManager()
        {
            _cameraManager = new CameraManager();
            _cameraManager.ImageUpdated += (bitmap) => OnImageUpdated(bitmap, null);

            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;

            refreshCameraSelectList();
        }

        /// <summary>
        /// 窗体加载事件
        /// </summary>
        private void Form1_Load(object sender, EventArgs e)
        {
            cbImageFormat.SelectedIndex = 2; // 默认选中JPG
        }

        /// <summary>
        /// 窗体关闭事件
        /// </summary>
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.Hide();
            _cameraManager.CloseSDK();
            _cameraManager = null;
            GC.Collect();
        }

        /// <summary>
        /// 键盘按键处理
        /// </summary>
        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            // I键切换推理状态（避免与 ImageViewer 的 V 键可视化开关冲突）
            if (e.KeyCode == Keys.I && !e.Alt && !e.Control && !e.Shift)
            {
                checkBox2.Checked = !checkBox2.Checked;
                e.Handled = true;
            }

            // S键保存图像
            if (e.KeyCode == Keys.S && !e.Alt && !e.Control && !e.Shift)
            {
                bnSaveImg.PerformClick();
                e.Handled = true;
            }
        }

        #endregion

        #region 图像处理和显示

        /// <summary>
        /// 相机或文件图像更新处理
        /// </summary>
        public void OnImageUpdated(Bitmap bitmap, string filePath = null)
        {
            try
            {
                string clonedFilePath = filePath?.Clone().ToString();
                dynamic result = _inference.ProcessInference(bitmap, clonedFilePath);
                UpdateUI(bitmap as Bitmap, result);
            }
            catch (Exception ex)
            {
                if (imagePanel1.InvokeRequired)
                {
                    imagePanel1.Invoke(new Action(() =>
                        MessageBox.Show(ex.Message, "处理错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
                else
                {
                    MessageBox.Show(ex.Message, "处理错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// 更新UI显示
        /// </summary>
        public void UpdateUI(Bitmap bitmap, dynamic result)
        {
            try
            {
                var currentTime = DateTime.Now;
                var interval = _lastUpdateTime != DateTime.MinValue
                    ? currentTime - _lastUpdateTime
                    : TimeSpan.Zero;

                _frameCounter++;
                UpdateFpsCounter(currentTime, interval);
                _lastUpdateTime = currentTime;

                if (result is CSharpResult typedResult)
                {
                    imagePanel1.UpdateImageAndResult(bitmap, typedResult);
                }
                else
                {
                    imagePanel1.UpdateImage(bitmap);
                    imagePanel1.ClearResults();
                    imagePanel1.Update();
                }
                
                if (checkBox1.Checked)
                {
                    bnSaveImg.PerformClick();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "UI更新错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 更新FPS计数器
        /// </summary>
        private void UpdateFpsCounter(DateTime currentTime, TimeSpan interval)
        {
            if (label_fps.InvokeRequired)
            {
                label_fps.Invoke(new Action(() => UpdateFpsCounter(currentTime, interval)));
                return;
            }
            var fpsInterval = currentTime - _fpsStartTime;
            if (fpsInterval.TotalSeconds < 1) return;

            var fps = _frameCounter / fpsInterval.TotalSeconds;
            label_fps.Text = $"时间: {currentTime:HH:mm:ss.fff} | " +
                            $"间隔: {interval.TotalMilliseconds:0}ms | " +
                            $"FPS: {fps:0.0}";

            _frameCounter = 0;
            _fpsStartTime = currentTime;
        }

        /// <summary>
        /// 跨线程更新UI的通用方法
        /// </summary>
        private void UpdateUI(Action action)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(action));
            }
            else
            {
                action();
            }
        }

        #endregion

        #region 相机控制

        /// <summary>
        /// 刷新相机列表
        /// </summary>
        private void refreshCameraSelectList()
        {
            _deviceInfoList = _cameraManager.RefreshDeviceList();
            cbDeviceList.Items.Clear();
            for (int i = 0; i < _deviceInfoList.Count; i++)
            {
                IDeviceInfo deviceInfo = _deviceInfoList[i];
                if (deviceInfo.UserDefinedName != "")
                {
                    cbDeviceList.Items.Add(deviceInfo.TLayerType.ToString() + ": " + deviceInfo.UserDefinedName + " (" + deviceInfo.SerialNumber + ")");
                }
                else
                {
                    cbDeviceList.Items.Add(deviceInfo.TLayerType.ToString() + ": " + deviceInfo.ManufacturerName + " " + deviceInfo.ModelName + " (" + deviceInfo.SerialNumber + ")");
                }
            }

            // 选择第一项
            if (_deviceInfoList.Count != 0)
            {
                cbDeviceList.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 设置启动采集时的控件状态
        /// </summary>
        private void SetCtrlWhenStartGrab()
        {
            bnStartGrab.Enabled = false;
            bnStopGrab.Enabled = true;

            bnSetHardTrigger.Enabled = true;
            bnSetSoftTrigger.Enabled = true;
            bnSoftTriggerLoop.Enabled = false;
            bnSortTriggerOnce.Enabled = false;
        }

        /// <summary>
        /// 设置停止采集时的控件状态
        /// </summary>
        private void SetCtrlWhenStopGrab()
        {
            bnStartGrab.Enabled = true;
            bnStopGrab.Enabled = false;

            bnSetHardTrigger.Enabled = false;
            bnSetSoftTrigger.Enabled = false;
            bnSoftTriggerLoop.Enabled = false;
            bnSortTriggerOnce.Enabled = false;
        }

        /// <summary>
        /// 设置相机打开时的控件状态
        /// </summary>
        private void SetCtrlWhenOpen()
        {
            bnOpen.Enabled = false;

            bnClose.Enabled = true;
            bnStartGrab.Enabled = false;
            bnStopGrab.Enabled = true;

            bnSetHardTrigger.Enabled = true;
            bnSetSoftTrigger.Enabled = false;
            bnSoftTriggerLoop.Enabled = true;
            bnSortTriggerOnce.Enabled = true;
        }

        /// <summary>
        /// 设置相机关闭时的控件状态
        /// </summary>
        private void SetCtrlWhenClose()
        {
            bnOpen.Enabled = true;
            bnClose.Enabled = false;

            bnStartGrab.Enabled = false;
            bnStopGrab.Enabled = false;
            bnSetHardTrigger.Enabled = false;
            bnSetSoftTrigger.Enabled = false;
            bnSoftTriggerLoop.Enabled = false;
            bnSortTriggerOnce.Enabled = false;
        }

        /// <summary>
        /// 打开相机
        /// </summary>
        private void bnOpen_Click(object sender, EventArgs e)
        {
            if (_cameraManager.CheckIfHaveDevice() is false || cbDeviceList.SelectedIndex == -1)
            {
                Logger.Debug("未找到摄像机设备，请检测线缆连接，或者点击刷新按钮");
            }

            try
            {
                _cameraManager.InitializeDevice(cbDeviceList.SelectedIndex);
                SetCtrlWhenOpen();
            }
            catch (Exception)
            {
                Logger.Error($"初始化设备 {cbDeviceList.SelectedIndex} 失败");
                MessageBox.Show($"初始化设备 {cbDeviceList.SelectedIndex} 失败");
            }
        }

        /// <summary>
        /// 关闭相机
        /// </summary>
        private void bnClose_Click(object sender, EventArgs e)
        {
            _cameraManager.CloseDevice();
            SetCtrlWhenClose();
        }

        /// <summary>
        /// 开始采集
        /// </summary>
        private void bnStartGrab_Click_1(object sender, EventArgs e)
        {
            _cameraManager.StartGrabbing();
            SetCtrlWhenStartGrab();
        }

        /// <summary>
        /// 停止采集
        /// </summary>
        private void bnStopGrab_Click(object sender, EventArgs e)
        {
            _cameraManager.StopGrabbing();
            SetCtrlWhenStopGrab();
        }

        /// <summary>
        /// 刷新相机列表按钮
        /// </summary>
        private void button5_Click(object sender, EventArgs e)
        {
            refreshCameraSelectList();
        }

        /// <summary>
        /// 设置软触发模式
        /// </summary>
        private void bnSetSoftTrigger_Click(object sender, EventArgs e)
        {
            _cameraManager.SetSoftTrigger();
            bnSetHardTrigger.Enabled = true;
            bnSetSoftTrigger.Enabled = false;

            bnSoftTriggerLoop.Enabled = true;
            bnSortTriggerOnce.Enabled = true;
        }

        /// <summary>
        /// 设置硬触发模式
        /// </summary>
        private void bnSetHardTrigger_Click(object sender, EventArgs e)
        {
            _cameraManager.SetLineTrigger();
            bnSetHardTrigger.Enabled = false;
            bnSetSoftTrigger.Enabled = true;

            bnSoftTriggerLoop.Enabled = false;
            bnSortTriggerOnce.Enabled = false;
        }

        /// <summary>
        /// 单次软触发
        /// </summary>
        private void button3_Click(object sender, EventArgs e)
        {
            _cameraManager.TriggerSoftTriggerOnce();
        }

        /// <summary>
        /// 循环软触发
        /// </summary>
        private async void bnSoftTriggerLoop_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_isRunning)
                {
                    _isRunning = true;
                    bnSoftTriggerLoop.Text = "停止循环";
                    int interval = (int)numLoopInterval.Value;
                    await _cameraManager.TriggerSoftTriggerLoopAsync(interval);
                }
                else
                {
                    _cameraManager.StopTriggerLoop();
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}");
            }
            finally
            {
                // 无论成功或失败，确保状态重置
                if (_isRunning)
                {
                    _isRunning = false;
                    bnSoftTriggerLoop.Text = "开始循环";
                }
            }
        }

        #endregion

        #region 模型加载与推理控制

        /// <summary>
        /// 加载本地模型
        /// </summary>
        private void bnLoadModel_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "模型文件 (*.dvt, *.dvst)|*.dvt;*.dvst";
                openFileDialog.InitialDirectory = !string.IsNullOrEmpty(Properties.Settings.Default.LastModelFolder)
                    ? Properties.Settings.Default.LastModelFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                openFileDialog.Title = "请选择模型文件（支持 dvt/dvst）";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string selectedPath = openFileDialog.FileName;

                        Model model = new Model(selectedPath, 0);
                        _inference.SetModel(model);

                        Properties.Settings.Default.LastModelFolder = Path.GetDirectoryName(selectedPath);
                        Properties.Settings.Default.Save();
                        
                        bnLoadModel.Text = "模型加载成功";
                        checkBox2.Enabled = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"模型加载失败：{ex.Message}", "错误",
                                      MessageBoxButtons.OK, MessageBoxIcon.Error);
                        bnLoadModel.Text = "加载模型";
                        checkBox2.Enabled = false;
                    }
                }
                else
                {
                    MessageBox.Show("未选择模型文件（支持 dvt/dvst）", "提示",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>
        /// HTTP服务器模型加载
        /// </summary>
        private void btnHttp_Click(object sender, EventArgs e)
        {
            if (!IsPortOpen("127.0.0.1", 9890, TimeSpan.FromMilliseconds(100)))
            {
                MessageBox.Show("9890端口未开放，请先启动服务！", "端口检测", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "DVP/DVT/DVST 模型文件 (*.dvp, *.dvt, *.dvst)|*.dvp;*.dvt;*.dvst";
                openFileDialog.Title = "选择推理模型文件（支持 dvp/dvt/dvst）";
                openFileDialog.InitialDirectory = !string.IsNullOrEmpty(Properties.Settings.Default.LastApiFolder)
                    ? Properties.Settings.Default.LastApiFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        Properties.Settings.Default.LastApiFolder = Path.GetDirectoryName(openFileDialog.FileName);
                        Properties.Settings.Default.Save();

                        _inference.SetApiModelPath(openFileDialog.FileName);

                        MessageBox.Show($"模型加载成功：{Path.GetFileName(openFileDialog.FileName)}",
                                      "模型加载", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        checkBox2.Enabled = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"模型加载失败（dvp/dvt/dvst）：{ex.Message}", "错误",
                                      MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        /// <summary>
        /// 端口检测方法
        /// </summary>
        private bool IsPortOpen(string host, int port, TimeSpan timeout)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    var connectTask = client.ConnectAsync(host, port);
                    bool completed = connectTask.Wait(timeout);
                    return completed && client.Connected;
                }
                catch (AggregateException ae) when (ae.InnerException is SocketException)
                {
                    return false;
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 推理状态切换
        /// </summary>
        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox2.Checked)
            {
                _inference.EnableInference();
                checkBox2.Text = "正在推理";
                ShowToolTip("推理模式已开启");
            }
            else
            {
                _inference.DisableInference();
                checkBox2.Text = "开启推理";
                imagePanel1.ClearResults();
                ShowToolTip("推理模式已关闭");
            }
        }

        #endregion

        #region 图像文件操作

        /// <summary>
        /// 加载图像文件
        /// </summary>
        private void btnLoadImage_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "图片文件 (*.png, *.bmp, *.jpg, *.jpeg, *.jfif)|*.png;*.bmp;*.jpg;*.jpeg;*.jfif";
                openFileDialog.Title = "选择图片文件";
                openFileDialog.CheckFileExists = true;
                openFileDialog.CheckPathExists = true;

                if (!string.IsNullOrEmpty(Properties.Settings.Default.LastSelectedFolder))
                {
                    openFileDialog.InitialDirectory = Properties.Settings.Default.LastSelectedFolder;
                }

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (FileStream fs = new FileStream(openFileDialog.FileName, FileMode.Open, FileAccess.Read))
                        {
                            Bitmap bitmap = new Bitmap(fs);
                            OnImageUpdated(bitmap, openFileDialog.FileName);
                        }

                        string selectedFolder = Path.GetDirectoryName(openFileDialog.FileName);
                        Properties.Settings.Default.LastSelectedFolder = selectedFolder;
                        Properties.Settings.Default.Save();
                    }
                    catch (ArgumentException ex)
                    {
                        MessageBox.Show("选择的文件不是有效的图片格式", "格式错误",
                                       MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"加载图片失败: {ex.Message}", "错误",
                                       MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        /// <summary>
        /// 保存图像
        /// </summary>
        private void bnSaveImage_Click(object sender, EventArgs e)
        {
            if (imagePanel1.image == null)
            {
                MessageBox.Show("当前没有可保存的图像！", "提示",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (cbImageFormat.SelectedItem == null)
                {
                    MessageBox.Show("请先选择图片格式！", "警告",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string format = cbImageFormat.SelectedItem.ToString();

                // 配置保存参数
                ImageFormat imageFormat;
                string extension;
                EncoderParameters encoderParams = null;

                switch (format)
                {
                    case "JPG":
                        imageFormat = ImageFormat.Jpeg;
                        extension = ".jpg";
                        encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(
                            System.Drawing.Imaging.Encoder.Quality,
                            90
                        );
                        break;
                    case "PNG":
                        imageFormat = ImageFormat.Png;
                        extension = ".png";
                        break;
                    case "BMP":
                        imageFormat = ImageFormat.Bmp;
                        extension = ".bmp";
                        break;
                    default:
                        throw new InvalidOperationException("不支持的图片格式");
                }

                // 构建存储路径
                string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
                string saveDirectory = Path.Combine(
                    System.Windows.Forms.Application.StartupPath,
                    "images",
                    dateFolder
                );

                Directory.CreateDirectory(saveDirectory);

                // 生成带完整时间的文件名
                string fileName = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-fff") + extension;
                string fullPath = Path.Combine(saveDirectory, fileName);

                // 保存图像
                using (Bitmap bmp = new Bitmap(imagePanel1.image))
                {
                    if (format == "JPG")
                    {
                        bmp.Save(fullPath, GetEncoder(imageFormat), encoderParams);
                    }
                    else
                    {
                        bmp.Save(fullPath, imageFormat);
                    }
                }

                Logger.Info($"图像已保存至：\n{fullPath}", "保存成功");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}", "错误",
                              MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 获取编码器信息
        /// </summary>
        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        /// <summary>
        /// 自动保存切换
        /// </summary>
        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                ShowToolTip("自动保存已开启");
            }
            else
            {
                ShowToolTip("自动保存已关闭");
            }
        }

        #endregion

        #region 压力测试

        /// <summary>
        /// 启动或停止压力测试
        /// </summary>
        private void btnStressTest_Click(object sender, EventArgs e)
        {
            // 检查是否正在运行测试
            if (_pressureTestRunner?.IsRunning == true)
            {
                // 取消当前测试
                _pressureTestRunner.Cancel();
                btnStressTest.Text = "开始压力测试";
                return;
            }

            // 验证路径
            var targetFolder = Properties.Settings.Default.LastSelectedFolder;
            if (!Directory.Exists(targetFolder))
            {
                MessageBox.Show("必须先选择图片", "路径错误",
                               MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 加载图片路径
            var imagePaths = LoadImagePaths(targetFolder);
            if (imagePaths.Count == 0)
            {
                MessageBox.Show("目录中没有有效图片文件", "路径错误",
                               MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 初始化测试
            btnStressTest.Text = "停止压力测试";
            _totalProcessed = 0;
            UpdateStats(0);

            // 创建并启动压力测试实例
            _pressureTestRunner = new PressureTestRunner(imagePaths);
            _pressureTestRunner.ImageUpdatedEvent += OnImageUpdated;
            _pressureTestRunner.TestCompleted += () =>
            {
                if (InvokeRequired)
                {
                    Invoke(new MethodInvoker(() =>
                    {
                        btnStressTest.Text = "开始压力测试";
                        _pressureTestRunner = null;
                    }));
                }
                else
                {
                    btnStressTest.Text = "开始压力测试";
                    _pressureTestRunner = null;
                }
            };

            var threadCount = (int)nudThread.Value;
            var targetRate = (int)nudRate.Value;
            Task.Run(() => _pressureTestRunner.RunPressureTest(threadCount, targetRate));
        }

        /// <summary>
        /// 加载图片路径
        /// </summary>
        private List<string> LoadImagePaths(string folder)
        {
            return Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => {
                    string ext = Path.GetExtension(f).ToLower();
                    return ext == ".png" || ext == ".bmp" ||
                           ext == ".jpg" || ext == ".jpeg" || ext == ".jfif";
                })
                .ToList();
        }

        /// <summary>
        /// 统计定时器回调
        /// </summary>
        private void OnStatsTimerCallback(object state)
        {
            if (_pressureTestRunner?.IsRunning == true)
            {
                // 获取压力测试的差值统计
                var stats = _pressureTestRunner.GetTotalProcessedAndTime();
                int deltaTotal = stats[0];
                int deltaTime = stats[1];

                // 累计总处理数
                _totalProcessed += deltaTotal;

                // 更新最近10次的deltaTotal
                lock (_lock)
                {
                    _lastTenDeltas[_deltaIndex % 10] = deltaTotal;
                    _deltaIndex = (_deltaIndex + 1) % 10;
                }

                // 更新UI
                UpdateStats(_totalProcessed);
            }
        }

        /// <summary>
        /// 更新统计信息显示
        /// </summary>
        private void UpdateStats(long totalCount)
        {
            // 计算最近3秒的平均FPS
            int recentTotal = 0;
            lock (_lock)
            {
                recentTotal = _lastTenDeltas.Sum();
            }
            double averageFPS = recentTotal / 3.0;

            if (lblStatus.InvokeRequired)
            {
                lblStatus.Invoke(new Action(() => UpdateStats(totalCount)));
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"已处理: {totalCount} 张");
            sb.AppendLine($"实时速率: {averageFPS:F1} FPS");
            sb.Append($"目标速率: {nudRate.Value} FPS");
            lblStatus.Text = sb.ToString();
        }

        #endregion

        #region 其他UI事件

        /// <summary>
        /// 显示工具提示
        /// </summary>
        private void ShowToolTip(string message, int duration = 3000)
        {
            int x = this.ClientSize.Width / 2;
            int y = 20;
            toolTip1.Show(message, this, x, y, duration);
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            // 未实现
        }

        private void nudThread_ValueChanged(object sender, EventArgs e)
        {
            // 未实现
        }

        #endregion
    }
}
