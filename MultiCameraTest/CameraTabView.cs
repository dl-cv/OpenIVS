using CameraManagerTest;
using DLCV.Camera;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MultiCameraTest
{
    public partial class CameraTabView : UserControl, IDisposable
    {
        public CameraManager CameraManager { get; private set; }
        public string CameraId { get; private set; }
        public string DeviceName { get; private set; }
        private Form1 _mainForm;

        // ROI绘制相关
        private bool _isDrawingROI = false;
        private Point _roiStartPoint;
        private Rectangle _roiRectangle;

        // 线程安全
        private readonly object _imageLock = new object();

        public CameraTabView()
        {
            // Designer requirement
            InitializeComponent();
        }

        public CameraTabView(string cameraId, string deviceName, Form1 mainForm)
        {
            InitializeComponent();

            CameraId = cameraId;
            DeviceName = deviceName;
            _mainForm = mainForm;

            // 设置初始值
            _comboTriggerMode.SelectedIndex = 0;

            // 初始状态下禁用所有控制组（相机未连接）
            _groupCamera.Enabled = false;
            _groupExposure.Enabled = false;
            _groupWhiteBalance.Enabled = false;
            _groupROI.Enabled = false;
            _groupUserSet.Enabled = false;

            BindEvents();
        }

        /// <summary>
        /// 设置相机管理器（必须在连接成功后调用）
        /// </summary>
        public void SetCameraManager(CameraManager cameraManager)
        {
            CameraManager = cameraManager;

            // 启用控件
            _groupCamera.Enabled = true;
            _groupExposure.Enabled = true;
            _groupWhiteBalance.Enabled = true;
            _groupROI.Enabled = true;
            _groupUserSet.Enabled = true;

            // 初始化参数显示
            InitializeParameterDisplay();
            
            // 初始化用户集显示
            InitializeUserSetDisplay();
        }

        /// <summary>
        /// 处理图像更新（线程安全）
        /// </summary>
        public void OnImageUpdated(System.Drawing.Image image)
        {
            if (image == null || _pictureBox == null)
                return;

            try
            {
                // 复制图像以避免线程问题
                System.Drawing.Image imageCopy = null;
                lock (_imageLock)
                {
                    imageCopy = (System.Drawing.Image)image.Clone();
                }

                // 在UI线程更新图像
                if (_pictureBox.InvokeRequired)
                {
                    _pictureBox.BeginInvoke(new Action(() =>
                    {
                        UpdatePictureBox(imageCopy);
                    }));
                }
                else
                {
                    UpdatePictureBox(imageCopy);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"图像更新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新PictureBox显示（必须在UI线程调用）
        /// </summary>
        private void UpdatePictureBox(System.Drawing.Image newImage)
        {
            try
            {
                // 释放旧图像
                var oldImage = _pictureBox.Image;
                _pictureBox.Image = newImage;
                oldImage?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新PictureBox失败: {ex.Message}");
                newImage?.Dispose();
            }
        }
        
        private void BindEvents()
        {
            // 原有事件
            _comboTriggerMode.SelectedIndexChanged += ComboTriggerMode_SelectedIndexChanged;
            _btnStartGrab.Click += BtnStartGrab_Click;
            _btnStopGrab.Click += BtnStopGrab_Click;
            _btnTriggerOnce.Click += BtnTriggerOnce_Click;
            _btnStartContinuous.Click += BtnStartContinuous_Click;
            _btnStopContinuous.Click += BtnStopContinuous_Click;
            
            // 曝光时间事件
            _btnSetExposure.Click += BtnSetExposure_Click;
            _btnSyncExposure.Click += BtnSyncExposure_Click;
            
            // 白平衡事件
            _btnAutoWhiteBalance.Click += BtnAutoWhiteBalance_Click;
            _btnSyncWhiteBalance.Click += BtnSyncWhiteBalance_Click;
            
            // ROI事件
            _btnSetROI.Click += BtnSetROI_Click;
            _btnApplyROI.Click += BtnApplyROI_Click;
            _btnSyncROI.Click += BtnSyncROI_Click;
            _btnRestoreROI.Click += BtnRestoreROI_Click;
            
            // 用户集事件
            _comboUserSet.SelectedIndexChanged += ComboUserSet_SelectedIndexChanged;
            _btnLoadUserSet.Click += BtnLoadUserSet_Click;
            _btnSaveUserSet.Click += BtnSaveUserSet_Click;
            _btnSetDefault.Click += BtnSetDefault_Click;
            _btnSyncCurrentUserSet.Click += BtnSyncCurrentUserSet_Click;
            _btnSyncDefaultUserSet.Click += BtnSyncDefaultUserSet_Click;
            
            // PictureBox鼠标事件（用于ROI绘制）
            _pictureBox.MouseDown += PictureBox_MouseDown;
            _pictureBox.MouseMove += PictureBox_MouseMove;
            _pictureBox.MouseUp += PictureBox_MouseUp;
            _pictureBox.Paint += PictureBox_Paint;

            // 帧率事件
            _btnSetFrameRate.Click += BtnSetFrameRate_Click;
            _btnSyncFrameRate.Click += BtnSyncFrameRate_Click;
            _btnSyncFrameRateEnable.Click += BtnSyncFrameRateEnable_Click;
            _chkFrameRateEnable.CheckedChanged += ChkFrameRateEnable_CheckedChanged;
        }

        private void InitializeParameterDisplay()
        {
            try
            {
                if (CameraManager?.ActiveDevice == null)
                {
                    Console.WriteLine("InitializeParameterDisplay: 相机未连接");
                    return;
                }

                Console.WriteLine($"初始化相机参数显示: {DeviceName}");

                // 初始化曝光时间显示
                Console.WriteLine("正在获取曝光时间...");
                float exposureTime = CameraManager.GetExposureTime();
                if (exposureTime > 0)
                {
                    _txtExposureTime.Text = exposureTime.ToString("F0");
                    Console.WriteLine($"曝光时间初始化: {exposureTime}μs");
                }
                else
                {
                    _txtExposureTime.Text = "10000"; // 默认值
                    Console.WriteLine("曝光时间获取失败，使用默认值10000μs");
                }

                // 初始化白平衡显示 (海康相机BalanceRatio是int类型)
                Console.WriteLine("正在获取白平衡比例...");
                var (red, green, blue) = CameraManager.GetBalanceRatio();
                if (red > 0 && green > 0 && blue > 0)
                {
                    _txtRedRatio.Text = ((int)red).ToString();
                    _txtGreenRatio.Text = ((int)green).ToString();
                    _txtBlueRatio.Text = ((int)blue).ToString();
                    Console.WriteLine($"白平衡比例初始化: R={red:F0}, G={green:F0}, B={blue:F0}");
                }
                else
                {
                    _txtRedRatio.Text = "1024";
                    _txtGreenRatio.Text = "1024";
                    _txtBlueRatio.Text = "1024";
                    Console.WriteLine("白平衡比例获取失败，使用默认值1024");
                }

                // 初始化ROI显示
                Console.WriteLine("正在获取ROI参数...");
                UpdateROIControls();
                
                // 初始化帧率显示
                Console.WriteLine("正在获取帧率参数...");
                try
                {
                    bool fpsEnable = CameraManager.GetFrameRateEnable();
                    _chkFrameRateEnable.CheckedChanged -= ChkFrameRateEnable_CheckedChanged;
                    _chkFrameRateEnable.Checked = fpsEnable;
                    _chkFrameRateEnable.CheckedChanged += ChkFrameRateEnable_CheckedChanged;

                    float fps = CameraManager.GetFrameRate();
                    if (fps > 0)
                    {
                        _txtFrameRate.Text = fps.ToString("F2");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"初始化帧率显示异常: {ex.Message}");
                }
                
                Console.WriteLine("参数显示初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InitializeParameterDisplay异常: {ex.Message}");
            }
        }

        private void ChkFrameRateEnable_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                    return;

                bool enable = _chkFrameRateEnable.Checked;
                bool result = CameraManager.SetFrameRateEnable(enable);
                if (!result)
                {
                    // 回退勾选状态
                    _chkFrameRateEnable.CheckedChanged -= ChkFrameRateEnable_CheckedChanged;
                    _chkFrameRateEnable.Checked = !enable;
                    _chkFrameRateEnable.CheckedChanged += ChkFrameRateEnable_CheckedChanged;
                    MessageBox.Show("设置帧率控制开关失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置帧率控制开关失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSetFrameRate_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!float.TryParse(_txtFrameRate.Text, out float fps) || fps <= 0)
                {
                    MessageBox.Show("请输入有效的帧率(FPS)", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                bool enabled = _chkFrameRateEnable.Checked;
                if (!enabled)
                {
                    var ask = MessageBox.Show("未启用帧率控制，是否启用并继续设置?", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (ask == DialogResult.Yes)
                    {
                        if (!CameraManager.SetFrameRateEnable(true))
                        {
                            MessageBox.Show("启用帧率控制失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        _chkFrameRateEnable.CheckedChanged -= ChkFrameRateEnable_CheckedChanged;
                        _chkFrameRateEnable.Checked = true;
                        _chkFrameRateEnable.CheckedChanged += ChkFrameRateEnable_CheckedChanged;
                    }
                    else
                    {
                        return;
                    }
                }

                bool success = CameraManager.SetFrameRate(fps);
                if (success)
                {
                    float actual = CameraManager.GetFrameRate();
                    _txtFrameRate.Text = actual > 0 ? actual.ToString("F2") : fps.ToString("F2");
                    MessageBox.Show($"帧率设置成功: {actual:F2} FPS", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("帧率设置失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置帧率失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSyncFrameRate_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                float fps = CameraManager.GetFrameRate();
                if (fps <= 0)
                {
                    MessageBox.Show("获取当前帧率失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _mainForm.SyncFrameRateToOtherCameras(this, fps);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步帧率失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSyncFrameRateEnable_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                bool enable = _chkFrameRateEnable.Checked;
                _mainForm.SyncFrameRateEnableToOtherCameras(this, enable);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步帧率开关失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        // 实现与原来相同的功能，但为每个相机独立
        private void BtnStartGrab_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                bool success = CameraManager.StartGrabbing();
                if (success)
                {
                    _btnStartGrab.Enabled = false;
                    _btnStopGrab.Enabled = true;
                }
                else
                {
                    MessageBox.Show("开始采集失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"开始采集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnStopGrab_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    return;
                }
                
                CameraManager.StopGrabbing();
                _btnStartGrab.Enabled = true;
                _btnStopGrab.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停止采集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void ComboTriggerMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                int index = _comboTriggerMode.SelectedIndex;
                
                // 若相机未连接，仅设置UI状态，不执行相机操作
                bool isCameraConnected = CameraManager != null && CameraManager.ActiveDevice != null;
                
                // 根据不同模式设置UI状态
                switch (index)
                {
                    case 0: // 关闭触发
                        if (isCameraConnected)
                            CameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Off);
                        
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 1: // 软触发
                        if (isCameraConnected)
                            CameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Software);
                        
                        _btnTriggerOnce.Enabled = isCameraConnected;
                        _lblInterval.Enabled = isCameraConnected;
                        _txtInterval.Enabled = isCameraConnected;
                        _btnStartContinuous.Enabled = isCameraConnected;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 2: // Line0
                        if (isCameraConnected)
                            CameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Line0);
                        
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 3: // Line1
                        if (isCameraConnected)
                            CameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Line1);
                        
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置触发模式失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnTriggerOnce_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                CameraManager.TriggerOnce();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"软触发失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private async void BtnStartContinuous_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                if (!int.TryParse(_txtInterval.Text, out int interval) || interval <= 0)
                {
                    MessageBox.Show("请输入有效的间隔时间", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _btnStartContinuous.Enabled = false;
                _btnStopContinuous.Enabled = true;

                await CameraManager.StartContinuousTriggerAsync(interval);
            }
            catch (Exception ex)
            {
                _btnStartContinuous.Enabled = true;
                _btnStopContinuous.Enabled = false;
                MessageBox.Show($"开始连续触发失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnStopContinuous_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    return;
                }
                
                CameraManager.StopContinuousTrigger();
                _btnStartContinuous.Enabled = true;
                _btnStopContinuous.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停止连续触发失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        #region 新增功能事件处理
        
        // 曝光时间设置事件
        private void BtnSetExposure_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                if (!float.TryParse(_txtExposureTime.Text, out float exposureTime) || exposureTime <= 0)
                {
                    MessageBox.Show("请输入有效的曝光时间值", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                if (exposureTime > 33000)
                {
                    MessageBox.Show("曝光时间不能超过33000微秒(33ms)", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                bool success = CameraManager.SetExposureTime(exposureTime);
                if (success)
                {
                    MessageBox.Show($"曝光时间设置成功: {exposureTime}μs", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("曝光时间设置失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置曝光时间失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnSyncExposure_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                float exposureTime = CameraManager.GetExposureTime();
                if (exposureTime <= 0)
                {
                    MessageBox.Show("获取当前曝光时间失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                // 调用Form1的同步方法
                _mainForm.SyncExposureToOtherCameras(this, exposureTime);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步曝光时间失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        // 白平衡设置事件
        private void BtnAutoWhiteBalance_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                bool success = CameraManager.ExecuteBalanceWhiteAuto();
                if (success)
                {
                    // 获取白平衡结果 (int类型显示)
                    var (red, green, blue) = CameraManager.GetBalanceRatio();
                    _txtRedRatio.Text = ((int)red).ToString();
                    _txtGreenRatio.Text = ((int)green).ToString();
                    _txtBlueRatio.Text = ((int)blue).ToString();
                    
                    MessageBox.Show($"一键白平衡执行成功\nR:{(int)red} G:{(int)green} B:{(int)blue}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("一键白平衡执行失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"白平衡设置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnSyncWhiteBalance_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                var (red, green, blue) = CameraManager.GetBalanceRatio();
                if (red <= 0 || green <= 0 || blue <= 0)
                {
                    MessageBox.Show("获取当前白平衡值失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                // 调用Form1的同步方法
                _mainForm.SyncWhiteBalanceToOtherCameras(this, red, green, blue);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步白平衡失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        // ROI设置事件
        private void BtnSetROI_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                if (_isDrawingROI)
                {
                    // 如果正在画框，退出画框模式
                    _isDrawingROI = false;
                    _btnSetROI.Text = "设置ROI";
                    _pictureBox.Cursor = Cursors.Default;
                }
                else
                {
                    // 进入画框模式
                    _isDrawingROI = true;
                    _btnSetROI.Text = "退出画框";
                    _pictureBox.Cursor = Cursors.Cross;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置ROI失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnApplyROI_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 读取文本框中的ROI数值
                if (!int.TryParse(_txtOffsetX.Text, out int offsetX) || offsetX < 0)
                {
                    MessageBox.Show("请输入有效的X偏移值（大于等于0）", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _txtOffsetX.Focus();
                    return;
                }

                if (!int.TryParse(_txtOffsetY.Text, out int offsetY) || offsetY < 0)
                {
                    MessageBox.Show("请输入有效的Y偏移值（大于等于0）", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _txtOffsetY.Focus();
                    return;
                }

                if (!int.TryParse(_txtWidth.Text, out int width) || width <= 0)
                {
                    MessageBox.Show("请输入有效的宽度值（大于0）", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _txtWidth.Focus();
                    return;
                }

                if (!int.TryParse(_txtHeight.Text, out int height) || height <= 0)
                {
                    MessageBox.Show("请输入有效的高度值（大于0）", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _txtHeight.Focus();
                    return;
                }

                // 获取相机最大分辨率进行验证
                var (maxWidth, maxHeight) = CameraManager.GetMaxResolution();
                if (maxWidth > 0 && maxHeight > 0)
                {
                    if (offsetX + width > maxWidth)
                    {
                        MessageBox.Show($"ROI超出范围：X偏移+宽度({offsetX + width}) 超过相机最大宽度({maxWidth})", 
                                      "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    if (offsetY + height > maxHeight)
                    {
                        MessageBox.Show($"ROI超出范围：Y偏移+高度({offsetY + height}) 超过相机最大高度({maxHeight})", 
                                      "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }

                // 停止采集
                bool wasGrabbing = CameraManager.ActiveDevice.IsGrabbing;
                if (wasGrabbing)
                    CameraManager.StopGrabbing();

                // 应用ROI设置
                bool success = CameraManager.SetROI(offsetX, offsetY, width, height);

                // 恢复采集
                if (wasGrabbing)
                    CameraManager.StartGrabbing();

                if (success)
                {
                    // 更新UI显示为实际设置的ROI值（可能会因为对齐要求被调整）
                    UpdateROIControls();
                    
                    // 获取实际设置的ROI值用于消息显示
                    var (actualX, actualY, actualWidth, actualHeight) = CameraManager.GetROI();
                    if (actualWidth > 0 && actualHeight > 0)
                    {
                        if (actualX != offsetX || actualY != offsetY || actualWidth != width || actualHeight != height)
                        {
                            MessageBox.Show($"ROI设置成功！\n" +
                                          $"输入值: ({offsetX}, {offsetY}, {width}, {height})\n" +
                                          $"实际值: ({actualX}, {actualY}, {actualWidth}, {actualHeight})\n" +
                                          $"注意：实际值可能因对齐要求被调整", 
                                          "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show($"ROI设置成功！\n值: ({actualX}, {actualY}, {actualWidth}, {actualHeight})", 
                                          "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    else
                    {
                        MessageBox.Show("ROI设置成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    MessageBox.Show("ROI设置失败，请检查输入的数值是否合理", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用ROI失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnSyncROI_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                var (offsetX, offsetY, width, height) = CameraManager.GetROI();
                if (width <= 0 || height <= 0)
                {
                    MessageBox.Show("获取当前ROI失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                // 调用Form1的同步方法
                _mainForm.SyncROIToOtherCameras(this, offsetX, offsetY, width, height);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步ROI失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnRestoreROI_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                // 停止采集
                bool wasGrabbing = CameraManager.ActiveDevice.IsGrabbing;
                if (wasGrabbing)
                    CameraManager.StopGrabbing();
                
                bool success = CameraManager.RestoreMaxROI();
                
                // 恢复采集
                if (wasGrabbing)
                    CameraManager.StartGrabbing();
                
                if (success)
                {
                    // 更新UI显示为实际的ROI值
                    UpdateROIControls();
                    
                    // 获取实际设置的ROI值用于消息显示
                    var (actualX, actualY, actualWidth, actualHeight) = CameraManager.GetROI();
                    if (actualWidth > 0 && actualHeight > 0)
                    {
                        MessageBox.Show($"ROI已恢复到最大分辨率\n实际值: ({actualX}, {actualY}, {actualWidth}, {actualHeight})", 
                                      "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("ROI已恢复到最大分辨率", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    MessageBox.Show("恢复ROI失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"恢复ROI失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void ApplyROISettings(int offsetX, int offsetY, int width, int height)
        {
            try
            {
                // 停止采集
                bool wasGrabbing = CameraManager.ActiveDevice.IsGrabbing;
                if (wasGrabbing)
                    CameraManager.StopGrabbing();
                
                bool success = CameraManager.SetROI(offsetX, offsetY, width, height);
                
                // 恢复采集
                if (wasGrabbing)
                    CameraManager.StartGrabbing();
                
                if (success)
                {
                    // 在UI线程更新界面显示为实际设置的ROI值
                    this.Invoke(new Action(UpdateROIControls));
                }
                else
                {
                    this.Invoke(new Action(() =>
                    {
                        UpdateROIControls(); // 更新以显示未改变的值
                        MessageBox.Show("ROI设置失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() =>
                {
                    UpdateROIControls(); // 失败时恢复原来的值
                    MessageBox.Show($"应用ROI设置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
        }
        
        // PictureBox鼠标事件（ROI绘制）
        private void PictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (!_isDrawingROI || e.Button != MouseButtons.Left)
                return;
                
            _roiStartPoint = e.Location;
            _roiRectangle = new Rectangle();
        }
        
        private void PictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawingROI || e.Button != MouseButtons.Left)
                return;
                
            int x = Math.Min(_roiStartPoint.X, e.X);
            int y = Math.Min(_roiStartPoint.Y, e.Y);
            int width = Math.Abs(e.X - _roiStartPoint.X);
            int height = Math.Abs(e.Y - _roiStartPoint.Y);
            
            _roiRectangle = new Rectangle(x, y, width, height);
            _pictureBox.Invalidate(); // 触发重绘
        }
        
        private void PictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (!_isDrawingROI || e.Button != MouseButtons.Left)
                return;
                
            // 立即退出画框模式
            _isDrawingROI = false;
            _btnSetROI.Text = "设置ROI";
            _pictureBox.Cursor = Cursors.Default;
            
            var capturedRoiRect = _roiRectangle;
            
            // 立即清除屏幕上的矩形
            _roiRectangle = new Rectangle();
            _pictureBox.Invalidate();

            // 检查绘制的矩形是否有效
            if (capturedRoiRect.Width > 10 && capturedRoiRect.Height > 10)
            {
                if (_pictureBox.Image != null)
                {
                    var imageROI = ConvertPictureBoxROIToImageROI(capturedRoiRect);
                    
                    // 异步应用ROI设置，避免UI卡顿
                    Task.Run(() => ApplyROISettings(imageROI.X, imageROI.Y, imageROI.Width, imageROI.Height));
                }
            }
        }
        
        private void PictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (_isDrawingROI && _roiRectangle.Width > 0 && _roiRectangle.Height > 0)
            {
                using (var pen = new Pen(Color.Red, 2))
                {
                    e.Graphics.DrawRectangle(pen, _roiRectangle);
                }
            }
        }
        
        #endregion
        
        #region 辅助方法
        
        private Rectangle ConvertPictureBoxROIToImageROI(Rectangle pictureBoxROI)
        {
            if (_pictureBox.Image == null)
                return pictureBoxROI;
                
            // 获取图像在PictureBox中的实际显示区域
            var imageRect = GetImageRectangleInPictureBox();
            
            // 计算缩放比例
            float scaleX = (float)_pictureBox.Image.Width / imageRect.Width;
            float scaleY = (float)_pictureBox.Image.Height / imageRect.Height;
            
            // 转换坐标
            int imageX = (int)((pictureBoxROI.X - imageRect.X) * scaleX);
            int imageY = (int)((pictureBoxROI.Y - imageRect.Y) * scaleY);
            int imageWidth = (int)(pictureBoxROI.Width * scaleX);
            int imageHeight = (int)(pictureBoxROI.Height * scaleY);
            
            // 确保坐标在图像范围内
            imageX = Math.Max(0, Math.Min(imageX, _pictureBox.Image.Width - 1));
            imageY = Math.Max(0, Math.Min(imageY, _pictureBox.Image.Height - 1));
            imageWidth = Math.Min(imageWidth, _pictureBox.Image.Width - imageX);
            imageHeight = Math.Min(imageHeight, _pictureBox.Image.Height - imageY);
            
            return new Rectangle(imageX, imageY, imageWidth, imageHeight);
        }
        
        private Rectangle GetImageRectangleInPictureBox()
        {
            if (_pictureBox.Image == null)
                return new Rectangle(0, 0, _pictureBox.Width, _pictureBox.Height);
                
            float imageAspect = (float)_pictureBox.Image.Width / _pictureBox.Image.Height;
            float containerAspect = (float)_pictureBox.Width / _pictureBox.Height;
            
            if (imageAspect > containerAspect)
            {
                // 图像比容器更宽，以宽度为准
                int displayHeight = (int)(_pictureBox.Width / imageAspect);
                int yOffset = (_pictureBox.Height - displayHeight) / 2;
                return new Rectangle(0, yOffset, _pictureBox.Width, displayHeight);
            }
            else
            {
                // 图像比容器更高，以高度为准
                int displayWidth = (int)(_pictureBox.Height * imageAspect);
                int xOffset = (_pictureBox.Width - displayWidth) / 2;
                return new Rectangle(xOffset, 0, displayWidth, _pictureBox.Height);
            }
        }
        
        private void UpdateROIControls()
        {
            try
            {
                if (CameraManager?.ActiveDevice != null)
                {
                    Console.WriteLine("正在更新ROI控件显示...");
                    var (offsetX, offsetY, width, height) = CameraManager.GetROI();
                    
                    if (width > 0 && height > 0)
                    {
                        _txtOffsetX.Text = offsetX.ToString();
                        _txtOffsetY.Text = offsetY.ToString();
                        _txtWidth.Text = width.ToString();
                        _txtHeight.Text = height.ToString();
                        Console.WriteLine($"ROI控件更新: X={offsetX}, Y={offsetY}, W={width}, H={height}");
                    }
                    else
                    {
                        // 如果获取失败，尝试获取最大分辨率作为默认值
                        var (maxWidth, maxHeight) = CameraManager.GetMaxResolution();
                        _txtOffsetX.Text = "0";
                        _txtOffsetY.Text = "0";
                        _txtWidth.Text = maxWidth > 0 ? maxWidth.ToString() : "1280";
                        _txtHeight.Text = maxHeight > 0 ? maxHeight.ToString() : "1024";
                        Console.WriteLine($"ROI获取失败，使用默认值: X=0, Y=0, W={_txtWidth.Text}, H={_txtHeight.Text}");
                    }
                }
                else
                {
                    Console.WriteLine("UpdateROIControls: 相机未连接");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateROIControls异常: {ex.Message}");
            }
        }

        private void InitializeUserSetDisplay()
        {
            try
            {
                if (CameraManager?.ActiveDevice == null)
                {
                    Console.WriteLine("InitializeUserSetDisplay: 相机未连接");
                    return;
                }

                // 获取当前用户集设置
                int currentUserSet = CameraManager.GetCurrentUserSet();
                if (currentUserSet >= 1 && currentUserSet <= 3)
                {
                    _comboUserSet.SelectedIndex = currentUserSet - 1; // 转换为0-2的索引
                    Console.WriteLine($"当前用户集: {currentUserSet}");
                }
                else
                {
                    // 如果获取失败，默认选择用户集1
                    _comboUserSet.SelectedIndex = 0;
                    Console.WriteLine("获取当前用户集失败，默认选择用户集1");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InitializeUserSetDisplay异常: {ex.Message}");
                // 异常时默认选择用户集1
                _comboUserSet.SelectedIndex = 0;
            }
        }

        #region 用户集事件处理

        private void ComboUserSet_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                    return;

                int selectedIndex = _comboUserSet.SelectedIndex + 1; // 转换为1-3的索引
                Console.WriteLine($"用户集选择改变为: 用户集{selectedIndex}");
                
                // 设置用户集选择器
                bool success = CameraManager.SetUserSetSelector(selectedIndex);
                if (success)
                {
                    UpdateStatus($"当前选择用户集{selectedIndex}");
                }
                else
                {
                    UpdateStatus($"切换到用户集{selectedIndex}失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"选择用户集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnLoadUserSet_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int selectedIndex = _comboUserSet.SelectedIndex + 1; // 转换为1-3的索引
                if (selectedIndex < 1 || selectedIndex > 3)
                {
                    MessageBox.Show("请选择有效的用户集", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                bool success = CameraManager.LoadUserSet(selectedIndex);
                if (success)
                {
                    MessageBox.Show($"用户集{selectedIndex}加载成功", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    
                    // 重新初始化参数显示
                    InitializeParameterDisplay();
                    UpdateStatus($"已加载用户集{selectedIndex}");
                }
                else
                {
                    MessageBox.Show($"用户集{selectedIndex}加载失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateStatus($"加载用户集{selectedIndex}失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载用户集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSaveUserSet_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int selectedIndex = _comboUserSet.SelectedIndex + 1; // 转换为1-3的索引
                if (selectedIndex < 1 || selectedIndex > 3)
                {
                    MessageBox.Show("请选择有效的用户集", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var result = MessageBox.Show($"确定要将当前参数保存到用户集{selectedIndex}吗？\n这将覆盖该用户集中已保存的参数。", 
                                           "确认保存", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                
                if (result == DialogResult.Yes)
                {
                    bool success = CameraManager.SaveToUserSet(selectedIndex);
                    if (success)
                    {
                        MessageBox.Show($"参数已保存到用户集{selectedIndex}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        UpdateStatus($"已保存到用户集{selectedIndex}");
                    }
                    else
                    {
                        MessageBox.Show($"保存到用户集{selectedIndex}失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateStatus($"保存到用户集{selectedIndex}失败");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存用户集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSetDefault_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int selectedIndex = _comboUserSet.SelectedIndex + 1; // 转换为1-3的索引
                if (selectedIndex < 1 || selectedIndex > 3)
                {
                    MessageBox.Show("请选择有效的用户集", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var result = MessageBox.Show($"确定要将用户集{selectedIndex}设为默认用户集吗？\n相机重启后将自动加载此用户集的参数。", 
                                           "确认设置", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                
                if (result == DialogResult.Yes)
                {
                    bool success = CameraManager.SetUserSetDefault(selectedIndex);
                    if (success)
                    {
                        MessageBox.Show($"用户集{selectedIndex}已设为默认", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        UpdateStatus($"用户集{selectedIndex}已设为默认");
                    }
                    else
                    {
                        MessageBox.Show($"设置默认用户集失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateStatus("设置默认用户集失败");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置默认用户集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSyncCurrentUserSet_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int selectedIndex = _comboUserSet.SelectedIndex + 1; // 转换为1-3的索引
                if (selectedIndex < 1 || selectedIndex > 3)
                {
                    MessageBox.Show("请选择有效的用户集", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var result = MessageBox.Show($"确定要将当前选择的用户集{selectedIndex}同步到其他所有相机吗？", 
                                           "确认同步", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                
                if (result == DialogResult.Yes)
                {
                    // 调用Form1的同步方法
                    _mainForm.SyncCurrentUserSetToOtherCameras(this, selectedIndex);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步当前用户集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSyncDefaultUserSet_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int selectedIndex = _comboUserSet.SelectedIndex + 1; // 转换为1-3的索引
                if (selectedIndex < 1 || selectedIndex > 3)
                {
                    MessageBox.Show("请选择有效的用户集", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var result = MessageBox.Show($"确定要将用户集{selectedIndex}设为所有相机的默认用户集吗？\n这将影响所有相机重启后的默认参数。", 
                                           "确认同步", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                
                if (result == DialogResult.Yes)
                {
                    // 调用Form1的同步方法
                    _mainForm.SyncDefaultUserSetToOtherCameras(this, selectedIndex);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步默认用户集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateStatus(string message)
        {
            // 这里可以更新状态显示，如果有状态栏的话
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        #endregion
        
        #endregion

        // Dispose method for cleanup
        public new void Dispose()
        {
            try
            {
                // 停止所有活动
                CameraManager?.StopContinuousTrigger();
                CameraManager?.StopGrabbing();
                CameraManager?.DisconnectDevice();
                CameraManager?.Dispose();
                
                // 释放图像资源
                if (_pictureBox?.Image != null)
                {
                    _pictureBox.Image.Dispose();
                    _pictureBox.Image = null;
                }
            }
            catch { }
            base.Dispose();
        }
    }
} 