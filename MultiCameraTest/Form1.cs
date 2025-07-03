using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DLCV.Camera;

namespace CameraManagerTest
{
    public partial class Form1 : Form
    {
        private List<DeviceInfoWrapper> _deviceList;
        private List<CameraTab> _cameraTabs = new List<CameraTab>();

        public Form1()
        {
            InitializeComponent();
            
            // 确保所有控件已正确初始化
            if (_comboDevices == null || _tabCameras == null)
            {
                MessageBox.Show("控件初始化失败，程序可能无法正常工作", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            // 绑定窗体事件
            this.FormClosing += Form1_FormClosing;
            this.Load += Form1_Load;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                // 使用临时的相机管理器刷新设备列表
                using (var tempManager = new CameraManager())
                {
                    RefreshDeviceList(tempManager);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                // 释放所有相机资源
                foreach (var tab in _cameraTabs)
                {
                    tab.Dispose();
                }
                CameraUtils.ReleaseSDK();
            }
            catch { }
        }

        private void RefreshDeviceList(CameraManager manager)
        {
            try
            {
                _comboDevices.Items.Clear();
                _deviceList = manager.RefreshDeviceList();

                if (_deviceList.Count == 0)
                {
                    _comboDevices.Items.Add("未检测到设备");
                    _comboDevices.SelectedIndex = 0;
                    _comboDevices.Enabled = false;
                    _btnAddCamera.Enabled = false;
                    UpdateStatus("未检测到相机设备");
                }
                else
                {
                    foreach (var device in _deviceList)
                    {
                        _comboDevices.Items.Add(device.ToString());
                    }
                    _comboDevices.SelectedIndex = 0;
                    _comboDevices.Enabled = true;
                    _btnAddCamera.Enabled = true;
                    UpdateStatus($"检测到 {_deviceList.Count} 个相机设备");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取设备列表失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnAddCamera_Click(object sender, EventArgs e)
        {
            try
            {
                int index = _comboDevices.SelectedIndex;
                if (index >= 0 && index < _deviceList.Count)
                {
                    // 检查这个相机是否已经被添加了
                    foreach (var tab in _cameraTabs)
                    {
                        if (tab.DeviceIndex == index)
                        {
                            MessageBox.Show("该相机已添加", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                    }

                    // 创建新的相机标签页
                    var cameraTab = new CameraTab(index, _deviceList[index].ToString());
                    
                    // 连接相机
                    if (cameraTab.ConnectCamera())
                    {
                        _cameraTabs.Add(cameraTab);
                        _tabCameras.TabPages.Add(cameraTab.TabPage);
                        _tabCameras.SelectedTab = cameraTab.TabPage;
                        _btnRemoveCamera.Enabled = true;
                        UpdateStatus($"已添加相机: {cameraTab.DeviceName}");
                    }
                    else
                    {
                        cameraTab.Dispose();
                        MessageBox.Show("连接相机失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加相机失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnRemoveCamera_Click(object sender, EventArgs e)
        {
            try
            {
                if (_tabCameras.SelectedTab == null || _cameraTabs.Count == 0)
                    return;

                // 找到当前选中的标签页
                var tabPage = _tabCameras.SelectedTab;
                var cameraTab = _cameraTabs.FirstOrDefault(c => c.TabPage == tabPage);
                
                if (cameraTab != null)
                {
                    string deviceName = cameraTab.DeviceName;
                    cameraTab.Dispose(); // 释放资源
                    _cameraTabs.Remove(cameraTab);
                    _tabCameras.TabPages.Remove(tabPage);
                    
                    if (_cameraTabs.Count == 0)
                        _btnRemoveCamera.Enabled = false;
                    
                    UpdateStatus($"已移除相机: {deviceName}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"移除相机失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 保留这些方法，但仅供兼容性使用，不再实际使用
        private void BtnConnect_Click(object sender, EventArgs e) { }
        private void BtnStartGrab_Click(object sender, EventArgs e) { }
        private void BtnStopGrab_Click(object sender, EventArgs e) { }
        private void ComboTriggerMode_SelectedIndexChanged(object sender, EventArgs e) { }
        private void BtnTriggerOnce_Click(object sender, EventArgs e) { }
        private void BtnStartContinuous_Click(object sender, EventArgs e) { }
        private void BtnStopContinuous_Click(object sender, EventArgs e) { }

        private void UpdateStatus(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatus), message);
                return;
            }
            _lblStatus.Text = $"状态: {message}";
        }

        #region 相机间同步方法

        /// <summary>
        /// 同步曝光时间到其他相机
        /// </summary>
        /// <param name="sourceTab">源相机标签页</param>
        /// <param name="exposureTime">曝光时间</param>
        public void SyncExposureToOtherCameras(CameraTab sourceTab, float exposureTime)
        {
            try
            {
                int successCount = 0;
                int failCount = 0;

                foreach (var tab in _cameraTabs)
                {
                    if (tab == sourceTab || tab.CameraManager?.ActiveDevice == null)
                        continue;

                    try
                    {
                        bool success = tab.CameraManager.SetExposureTime(exposureTime);
                        if (success)
                        {
                            successCount++;
                            // 更新UI显示
                            if (tab._txtExposureTime != null)
                                tab._txtExposureTime.Text = exposureTime.ToString();
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    catch
                    {
                        failCount++;
                    }
                }

                string message = $"曝光时间同步完成: 成功{successCount}个，失败{failCount}个";
                UpdateStatus(message);
                MessageBox.Show(message, "同步结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步曝光时间失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 同步白平衡到其他相机
        /// </summary>
        /// <param name="sourceTab">源相机标签页</param>
        /// <param name="red">红色比例</param>
        /// <param name="green">绿色比例</param>
        /// <param name="blue">蓝色比例</param>
        public void SyncWhiteBalanceToOtherCameras(CameraTab sourceTab, float red, float green, float blue)
        {
            try
            {
                int successCount = 0;
                int failCount = 0;

                foreach (var tab in _cameraTabs)
                {
                    if (tab == sourceTab || tab.CameraManager?.ActiveDevice == null)
                        continue;

                    try
                    {
                        bool success = tab.CameraManager.SetBalanceRatio(red, green, blue);
                        if (success)
                        {
                            successCount++;
                            // 更新UI显示
                            if (tab._txtRedRatio != null && tab._txtGreenRatio != null && tab._txtBlueRatio != null)
                            {
                                tab._txtRedRatio.Text = red.ToString("F2");
                                tab._txtGreenRatio.Text = green.ToString("F2");
                                tab._txtBlueRatio.Text = blue.ToString("F2");
                            }
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    catch
                    {
                        failCount++;
                    }
                }

                string message = $"白平衡同步完成: 成功{successCount}个，失败{failCount}个";
                UpdateStatus(message);
                MessageBox.Show(message, "同步结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步白平衡失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 同步ROI到其他相机
        /// </summary>
        /// <param name="sourceTab">源相机标签页</param>
        /// <param name="offsetX">X偏移</param>
        /// <param name="offsetY">Y偏移</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        public void SyncROIToOtherCameras(CameraTab sourceTab, int offsetX, int offsetY, int width, int height)
        {
            try
            {
                int successCount = 0;
                int failCount = 0;

                foreach (var tab in _cameraTabs)
                {
                    if (tab == sourceTab || tab.CameraManager?.ActiveDevice == null)
                        continue;

                    try
                    {
                        // 停止采集
                        bool wasGrabbing = tab.CameraManager.ActiveDevice.IsGrabbing;
                        if (wasGrabbing)
                            tab.CameraManager.StopGrabbing();

                        bool success = tab.CameraManager.SetROI(offsetX, offsetY, width, height);

                        // 恢复采集
                        if (wasGrabbing)
                            tab.CameraManager.StartGrabbing();

                        if (success)
                        {
                            successCount++;
                            // 更新UI显示
                            if (tab._txtOffsetX != null && tab._txtOffsetY != null && 
                                tab._txtWidth != null && tab._txtHeight != null)
                            {
                                tab._txtOffsetX.Text = offsetX.ToString();
                                tab._txtOffsetY.Text = offsetY.ToString();
                                tab._txtWidth.Text = width.ToString();
                                tab._txtHeight.Text = height.ToString();
                            }
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    catch
                    {
                        failCount++;
                    }
                }

                string message = $"ROI同步完成: 成功{successCount}个，失败{failCount}个";
                UpdateStatus(message);
                MessageBox.Show(message, "同步结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步ROI失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion
    }

    // 为每个相机创建一个标签页类
    public class CameraTab : IDisposable
    {
        public TabPage TabPage { get; private set; }
        public CameraManager CameraManager { get; private set; }
        public int DeviceIndex { get; private set; }
        public string DeviceName { get; private set; }
        
        private PictureBox _pictureBox;
        private GroupBox _groupCamera, _groupExposure, _groupWhiteBalance, _groupROI;
        private ComboBox _comboTriggerMode;
        private Button _btnStartGrab, _btnStopGrab;
        private Button _btnTriggerOnce;
        private Label _lblInterval, _lblTriggerMode;
        private TextBox _txtInterval;
        private Button _btnStartContinuous, _btnStopContinuous;
        
        // 曝光时间控件
        private Label _lblExposureTime;
        internal TextBox _txtExposureTime;
        private Button _btnSetExposure, _btnSyncExposure;
        
        // 白平衡控件
        private Button _btnAutoWhiteBalance, _btnSyncWhiteBalance;
        private Label _lblRedRatio, _lblGreenRatio, _lblBlueRatio;
        internal TextBox _txtRedRatio, _txtGreenRatio, _txtBlueRatio;
        
        // ROI控件
        private Label _lblOffsetX, _lblOffsetY, _lblWidth, _lblHeight;
        internal TextBox _txtOffsetX, _txtOffsetY, _txtWidth, _txtHeight;
        private Button _btnSetROI, _btnSyncROI, _btnRestoreROI, _btnDrawROI;
        
        // ROI绘制相关
        private bool _isDrawingROI = false;
        private Point _roiStartPoint;
        private Rectangle _roiRectangle;
        
        public CameraTab(int deviceIndex, string deviceName)
        {
            DeviceIndex = deviceIndex;
            DeviceName = deviceName;
            
            // 创建标签页
            TabPage = new TabPage(deviceName);
            
            // 初始化控件
            InitializeControls();
            
            // 创建相机管理器
            CameraManager = new CameraManager();
            CameraManager.ImageUpdated += CameraManager_ImageUpdated;
        }
        
        private void InitializeControls()
        {
            // 设置标签页大小
            TabPage.Size = new Size(1000, 700);
            
            // 创建图像显示控件
            _pictureBox = new PictureBox()
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(10, 220),
                Size = new Size(TabPage.Width - 20, TabPage.Height - 230),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            
            // 相机控制组
            InitializeCameraControlGroup();
            
            // 曝光时间组
            InitializeExposureGroup();
            
            // 白平衡组
            InitializeWhiteBalanceGroup();
            
            // ROI组
            InitializeROIGroup();
            
            // 添加控件到标签页
            TabPage.Controls.Add(_groupCamera);
            TabPage.Controls.Add(_groupExposure);
            TabPage.Controls.Add(_groupWhiteBalance);
            TabPage.Controls.Add(_groupROI);
            TabPage.Controls.Add(_pictureBox);
            
            // 绑定事件
            BindEvents();
        }
        
        private void InitializeCameraControlGroup()
        {
            _groupCamera = new GroupBox()
            {
                Text = "相机控制",
                Location = new Point(10, 10),
                Size = new Size(240, 200),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            
            _lblTriggerMode = new Label()
            {
                Text = "触发模式:",
                Location = new Point(15, 25),
                AutoSize = true
            };
            
            _comboTriggerMode = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(15, 45),
                Size = new Size(180, 26)
            };
            
            _btnStartGrab = new Button()
            {
                Text = "开始采集",
                Location = new Point(15, 80),
                Size = new Size(85, 30)
            };
            
            _btnStopGrab = new Button()
            {
                Text = "停止采集",
                Location = new Point(110, 80),
                Size = new Size(85, 30),
                Enabled = false
            };
            
            _btnTriggerOnce = new Button()
            {
                Text = "单次触发",
                Location = new Point(15, 120),
                Size = new Size(85, 30),
                Enabled = false
            };
            
            _lblInterval = new Label()
            {
                Text = "间隔(ms):",
                Location = new Point(15, 155),
                AutoSize = true,
                Enabled = false
            };
            
            _txtInterval = new TextBox()
            {
                Text = "1000",
                Location = new Point(75, 152),
                Size = new Size(55, 23),
                Enabled = false
            };
            
            _btnStartContinuous = new Button()
            {
                Text = "开始",
                Location = new Point(140, 150),
                Size = new Size(50, 26),
                Enabled = false
            };
            
            _btnStopContinuous = new Button()
            {
                Text = "停止",
                Location = new Point(195, 150),
                Size = new Size(50, 26),
                Enabled = false
            };
            
            // 添加触发模式选项
            _comboTriggerMode.Items.Add("关闭触发");
            _comboTriggerMode.Items.Add("软触发");
            _comboTriggerMode.Items.Add("Line0");
            _comboTriggerMode.Items.Add("Line1");
            _comboTriggerMode.SelectedIndex = 0;
            
            // 添加控件到组
            _groupCamera.Controls.Add(_lblTriggerMode);
            _groupCamera.Controls.Add(_comboTriggerMode);
            _groupCamera.Controls.Add(_btnStartGrab);
            _groupCamera.Controls.Add(_btnStopGrab);
            _groupCamera.Controls.Add(_btnTriggerOnce);
            _groupCamera.Controls.Add(_lblInterval);
            _groupCamera.Controls.Add(_txtInterval);
            _groupCamera.Controls.Add(_btnStartContinuous);
            _groupCamera.Controls.Add(_btnStopContinuous);
        }
        
        private void InitializeExposureGroup()
        {
            _groupExposure = new GroupBox()
            {
                Text = "曝光时间设置",
                Location = new Point(260, 10),
                Size = new Size(240, 100),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            
            _lblExposureTime = new Label()
            {
                Text = "曝光时间(μs):",
                Location = new Point(15, 25),
                AutoSize = true
            };
            
            _txtExposureTime = new TextBox()
            {
                Text = "10000",
                Location = new Point(15, 45),
                Size = new Size(100, 23)
            };
            
            _btnSetExposure = new Button()
            {
                Text = "设置",
                Location = new Point(125, 44),
                Size = new Size(50, 26)
            };
            
            _btnSyncExposure = new Button()
            {
                Text = "同步到其他相机",
                Location = new Point(15, 70),
                Size = new Size(120, 26)
            };
            
            // 添加控件到组
            _groupExposure.Controls.Add(_lblExposureTime);
            _groupExposure.Controls.Add(_txtExposureTime);
            _groupExposure.Controls.Add(_btnSetExposure);
            _groupExposure.Controls.Add(_btnSyncExposure);
        }
        
        private void InitializeWhiteBalanceGroup()
        {
            _groupWhiteBalance = new GroupBox()
            {
                Text = "白平衡设置",
                Location = new Point(510, 10),
                Size = new Size(240, 200),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            
            _btnAutoWhiteBalance = new Button()
            {
                Text = "一键白平衡",
                Location = new Point(15, 25),
                Size = new Size(100, 30)
            };
            
            _btnSyncWhiteBalance = new Button()
            {
                Text = "同步到其他相机",
                Location = new Point(125, 25),
                Size = new Size(100, 30)
            };
            
            _lblRedRatio = new Label()
            {
                Text = "红色比例:",
                Location = new Point(15, 65),
                AutoSize = true
            };
            
            _txtRedRatio = new TextBox()
            {
                Location = new Point(15, 85),
                Size = new Size(60, 23),
                ReadOnly = true
            };
            
            _lblGreenRatio = new Label()
            {
                Text = "绿色比例:",
                Location = new Point(85, 65),
                AutoSize = true
            };
            
            _txtGreenRatio = new TextBox()
            {
                Location = new Point(85, 85),
                Size = new Size(60, 23),
                ReadOnly = true
            };
            
            _lblBlueRatio = new Label()
            {
                Text = "蓝色比例:",
                Location = new Point(155, 65),
                AutoSize = true
            };
            
            _txtBlueRatio = new TextBox()
            {
                Location = new Point(155, 85),
                Size = new Size(60, 23),
                ReadOnly = true
            };
            
            // 添加控件到组
            _groupWhiteBalance.Controls.Add(_btnAutoWhiteBalance);
            _groupWhiteBalance.Controls.Add(_btnSyncWhiteBalance);
            _groupWhiteBalance.Controls.Add(_lblRedRatio);
            _groupWhiteBalance.Controls.Add(_txtRedRatio);
            _groupWhiteBalance.Controls.Add(_lblGreenRatio);
            _groupWhiteBalance.Controls.Add(_txtGreenRatio);
            _groupWhiteBalance.Controls.Add(_lblBlueRatio);
            _groupWhiteBalance.Controls.Add(_txtBlueRatio);
        }
        
        private void InitializeROIGroup()
        {
            _groupROI = new GroupBox()
            {
                Text = "ROI设置",
                Location = new Point(760, 10),
                Size = new Size(230, 200),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            
            _lblOffsetX = new Label()
            {
                Text = "X偏移:",
                Location = new Point(15, 25),
                Size = new Size(45, 20)
            };
            
            _txtOffsetX = new TextBox()
            {
                Location = new Point(60, 22),
                Size = new Size(60, 23)
            };
            
            _lblOffsetY = new Label()
            {
                Text = "Y偏移:",
                Location = new Point(130, 25),
                Size = new Size(45, 20)
            };
            
            _txtOffsetY = new TextBox()
            {
                Location = new Point(175, 22),
                Size = new Size(40, 23)
            };
            
            _lblWidth = new Label()
            {
                Text = "宽度:",
                Location = new Point(15, 55),
                Size = new Size(45, 20)
            };
            
            _txtWidth = new TextBox()
            {
                Location = new Point(60, 52),
                Size = new Size(60, 23)
            };
            
            _lblHeight = new Label()
            {
                Text = "高度:",
                Location = new Point(130, 55),
                Size = new Size(45, 20)
            };
            
            _txtHeight = new TextBox()
            {
                Location = new Point(175, 52),
                Size = new Size(40, 23)
            };
            
            _btnSetROI = new Button()
            {
                Text = "设置ROI",
                Location = new Point(15, 85),
                Size = new Size(70, 26)
            };
            
            _btnSyncROI = new Button()
            {
                Text = "同步ROI",
                Location = new Point(90, 85),
                Size = new Size(70, 26)
            };
            
            _btnRestoreROI = new Button()
            {
                Text = "还原ROI",
                Location = new Point(15, 115),
                Size = new Size(70, 26)
            };
            
            _btnDrawROI = new Button()
            {
                Text = "画框设置",
                Location = new Point(90, 115),
                Size = new Size(70, 26)
            };
            
            // 添加控件到组
            _groupROI.Controls.Add(_lblOffsetX);
            _groupROI.Controls.Add(_txtOffsetX);
            _groupROI.Controls.Add(_lblOffsetY);
            _groupROI.Controls.Add(_txtOffsetY);
            _groupROI.Controls.Add(_lblWidth);
            _groupROI.Controls.Add(_txtWidth);
            _groupROI.Controls.Add(_lblHeight);
            _groupROI.Controls.Add(_txtHeight);
            _groupROI.Controls.Add(_btnSetROI);
            _groupROI.Controls.Add(_btnSyncROI);
            _groupROI.Controls.Add(_btnRestoreROI);
            _groupROI.Controls.Add(_btnDrawROI);
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
            _btnSyncROI.Click += BtnSyncROI_Click;
            _btnRestoreROI.Click += BtnRestoreROI_Click;
            _btnDrawROI.Click += BtnDrawROI_Click;
            
            // PictureBox鼠标事件（用于ROI绘制）
            _pictureBox.MouseDown += PictureBox_MouseDown;
            _pictureBox.MouseMove += PictureBox_MouseMove;
            _pictureBox.MouseUp += PictureBox_MouseUp;
            _pictureBox.Paint += PictureBox_Paint;
        }
        
        public bool ConnectCamera()
        {
            try
            {
                bool success = CameraManager.ConnectDevice(DeviceIndex);
                if (success)
                {
                    _groupCamera.Enabled = true;
                    _groupExposure.Enabled = true;
                    _groupWhiteBalance.Enabled = true;
                    _groupROI.Enabled = true;
                    
                    // 初始化参数显示
                    InitializeParameterDisplay();
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void InitializeParameterDisplay()
        {
            try
            {
                if (CameraManager?.ActiveDevice == null)
                    return;

                // 初始化曝光时间显示
                float exposureTime = CameraManager.GetExposureTime();
                if (exposureTime > 0)
                    _txtExposureTime.Text = exposureTime.ToString();

                // 初始化白平衡显示
                var (red, green, blue) = CameraManager.GetBalanceRatio();
                if (red > 0 && green > 0 && blue > 0)
                {
                    _txtRedRatio.Text = red.ToString("F2");
                    _txtGreenRatio.Text = green.ToString("F2");
                    _txtBlueRatio.Text = blue.ToString("F2");
                }

                // 初始化ROI显示
                UpdateROIControls();
            }
            catch { }
        }
        
        private void CameraManager_ImageUpdated(object sender, ImageEventArgs e)
        {
            try
            {
                if (_pictureBox.InvokeRequired)
                {
                    _pictureBox.BeginInvoke(new Action<object, ImageEventArgs>(CameraManager_ImageUpdated), sender, e);
                    return;
                }

                // 显示图像
                if (_pictureBox.Image != null)
                {
                    _pictureBox.Image.Dispose();
                    _pictureBox.Image = null;
                }
                _pictureBox.Image = e.Image.Clone() as Image;
            }
            catch { }
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
                SyncExposureToOtherCameras(exposureTime);
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
                    // 获取白平衡结果
                    var (red, green, blue) = CameraManager.GetBalanceRatio();
                    _txtRedRatio.Text = red.ToString("F2");
                    _txtGreenRatio.Text = green.ToString("F2");
                    _txtBlueRatio.Text = blue.ToString("F2");
                    
                    MessageBox.Show("一键白平衡执行成功", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                SyncWhiteBalanceToOtherCameras(red, green, blue);
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
                
                if (!int.TryParse(_txtOffsetX.Text, out int offsetX) || offsetX < 0 ||
                    !int.TryParse(_txtOffsetY.Text, out int offsetY) || offsetY < 0 ||
                    !int.TryParse(_txtWidth.Text, out int width) || width <= 0 ||
                    !int.TryParse(_txtHeight.Text, out int height) || height <= 0)
                {
                    MessageBox.Show("请输入有效的ROI参数", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
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
                    MessageBox.Show($"ROI设置成功: ({offsetX}, {offsetY}, {width}, {height})", "成功", 
                                  MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("ROI设置失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置ROI失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                SyncROIToOtherCameras(offsetX, offsetY, width, height);
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
                    // 更新UI显示
                    UpdateROIControls();
                    MessageBox.Show("ROI已恢复到最大分辨率", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        
        private void BtnDrawROI_Click(object sender, EventArgs e)
        {
            try
            {
                if (_isDrawingROI)
                {
                    _isDrawingROI = false;
                    _btnDrawROI.Text = "画框设置";
                    _pictureBox.Cursor = Cursors.Default;
                    MessageBox.Show("已退出画框模式", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    _isDrawingROI = true;
                    _btnDrawROI.Text = "退出画框";
                    _pictureBox.Cursor = Cursors.Cross;
                    MessageBox.Show("请在图像上拖拽鼠标画出ROI区域", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"画框模式切换失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                
            if (_roiRectangle.Width > 10 && _roiRectangle.Height > 10)
            {
                // 将PictureBox坐标转换为图像坐标
                if (_pictureBox.Image != null)
                {
                    var imageROI = ConvertPictureBoxROIToImageROI(_roiRectangle);
                    _txtOffsetX.Text = imageROI.X.ToString();
                    _txtOffsetY.Text = imageROI.Y.ToString();
                    _txtWidth.Text = imageROI.Width.ToString();
                    _txtHeight.Text = imageROI.Height.ToString();
                    
                    MessageBox.Show($"ROI区域已设置: ({imageROI.X}, {imageROI.Y}, {imageROI.Width}, {imageROI.Height})", 
                                  "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            
            _roiRectangle = new Rectangle();
            _pictureBox.Invalidate(); // 清除绘制
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
                    var (offsetX, offsetY, width, height) = CameraManager.GetROI();
                    _txtOffsetX.Text = offsetX.ToString();
                    _txtOffsetY.Text = offsetY.ToString();
                    _txtWidth.Text = width.ToString();
                    _txtHeight.Text = height.ToString();
                }
            }
            catch { }
        }
        
        // 同步方法（需要访问Form1中的其他CameraTab）
        private void SyncExposureToOtherCameras(float exposureTime)
        {
            var form1 = TabPage.Parent?.Parent as Form1;
            form1?.SyncExposureToOtherCameras(this, exposureTime);
        }
        
        private void SyncWhiteBalanceToOtherCameras(float red, float green, float blue)
        {
            var form1 = TabPage.Parent?.Parent as Form1;
            form1?.SyncWhiteBalanceToOtherCameras(this, red, green, blue);
        }
        
        private void SyncROIToOtherCameras(int offsetX, int offsetY, int width, int height)
        {
            var form1 = TabPage.Parent?.Parent as Form1;
            form1?.SyncROIToOtherCameras(this, offsetX, offsetY, width, height);
        }
        
        #endregion

        public void Dispose()
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
        }
    }
}
