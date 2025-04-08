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
    }

    // 为每个相机创建一个标签页类
    public class CameraTab : IDisposable
    {
        public TabPage TabPage { get; private set; }
        public CameraManager CameraManager { get; private set; }
        public int DeviceIndex { get; private set; }
        public string DeviceName { get; private set; }
        
        private PictureBox _pictureBox;
        private GroupBox _groupCamera;
        private ComboBox _comboTriggerMode;
        private Button _btnStartGrab, _btnStopGrab;
        private Button _btnTriggerOnce;
        private Label _lblInterval, _lblTriggerMode;
        private TextBox _txtInterval;
        private Button _btnStartContinuous, _btnStopContinuous;
        
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
            // 创建控件
            _pictureBox = new PictureBox()
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(10, 170),
                Size = new Size(TabPage.Width - 20, TabPage.Height - 180),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            
            _groupCamera = new GroupBox()
            {
                Text = "相机控制",
                Location = new Point(10, 10),
                Size = new Size(480, 155),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            
            _comboTriggerMode = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(115, 25),
                Size = new Size(180, 26)
            };
            
            _btnStartGrab = new Button()
            {
                Text = "开始采集",
                Location = new Point(15, 70),
                Size = new Size(110, 34)
            };
            
            _btnStopGrab = new Button()
            {
                Text = "停止采集",
                Location = new Point(135, 70),
                Size = new Size(110, 34),
                Enabled = false
            };
            
            _btnTriggerOnce = new Button()
            {
                Text = "单次触发",
                Location = new Point(15, 115),
                Size = new Size(110, 34),
                Enabled = false
            };
            
            _lblInterval = new Label()
            {
                Text = "间隔(ms):",
                Location = new Point(135, 120),
                AutoSize = true,
                Enabled = false
            };
            
            _txtInterval = new TextBox()
            {
                Text = "1000",
                Location = new Point(210, 115),
                Size = new Size(55, 28),
                Enabled = false
            };
            
            _btnStartContinuous = new Button()
            {
                Text = "开始",
                Location = new Point(270, 115),
                Size = new Size(70, 34),
                Enabled = false
            };
            
            _btnStopContinuous = new Button()
            {
                Text = "停止",
                Location = new Point(350, 115),
                Size = new Size(70, 34),
                Enabled = false
            };
            
            _lblTriggerMode = new Label()
            {
                Text = "触发模式:",
                Location = new Point(15, 28),
                AutoSize = true
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
            
            // 添加控件到标签页
            TabPage.Controls.Add(_groupCamera);
            TabPage.Controls.Add(_pictureBox);
            
            // 绑定事件
            _comboTriggerMode.SelectedIndexChanged += ComboTriggerMode_SelectedIndexChanged;
            _btnStartGrab.Click += BtnStartGrab_Click;
            _btnStopGrab.Click += BtnStopGrab_Click;
            _btnTriggerOnce.Click += BtnTriggerOnce_Click;
            _btnStartContinuous.Click += BtnStartContinuous_Click;
            _btnStopContinuous.Click += BtnStopContinuous_Click;
        }
        
        public bool ConnectCamera()
        {
            try
            {
                bool success = CameraManager.ConnectDevice(DeviceIndex);
                if (success)
                {
                    _groupCamera.Enabled = true;
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
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
