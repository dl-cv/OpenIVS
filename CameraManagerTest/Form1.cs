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
using MvCameraControl;

namespace CameraManagerTest
{
    public partial class Form1 : Form
    {
        private CameraManager _cameraManager;
        private List<IDeviceInfo> _deviceList;
        private PictureBox _pictureBox;
        private ComboBox _comboDevices;
        private Button _btnConnect;
        private Button _btnStartGrab;
        private Button _btnStopGrab;
        private Button _btnTriggerOnce;
        private Button _btnStartContinuous;
        private Button _btnStopContinuous;
        private ComboBox _comboTriggerMode;
        private GroupBox _groupCamera;
        private TextBox _txtInterval;
        private Label _lblInterval;
        private Label _lblStatus;

        public Form1()
        {
            InitializeComponent();
            InitializeCustomComponents();
        }

        private void InitializeCustomComponents()
        {
            // 设置窗体属性
            this.Text = "相机管理器测试";
            this.Size = new Size(800, 600);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += Form1_FormClosing;
            this.Load += Form1_Load;

            // 创建状态标签
            _lblStatus = new Label
            {
                Text = "准备就绪",
                Location = new Point(12, 540),
                AutoSize = true
            };
            this.Controls.Add(_lblStatus);

            // 创建相机管理器
            _cameraManager = new CameraManager();
            _cameraManager.ImageUpdated += CameraManager_ImageUpdated;

            // 创建设备选择组合框
            _comboDevices = new ComboBox
            {
                Location = new Point(12, 12),
                Width = 300,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            this.Controls.Add(_comboDevices);

            // 创建连接按钮
            _btnConnect = new Button
            {
                Text = "连接",
                Location = new Point(320, 12),
                Width = 80
            };
            _btnConnect.Click += BtnConnect_Click;
            this.Controls.Add(_btnConnect);

            // 创建相机控制组
            _groupCamera = new GroupBox
            {
                Text = "相机控制",
                Location = new Point(12, 50),
                Size = new Size(400, 140),
                Enabled = false
            };
            this.Controls.Add(_groupCamera);

            // 创建触发模式选择
            Label lblTriggerMode = new Label
            {
                Text = "触发模式:",
                Location = new Point(15, 25),
                AutoSize = true
            };
            _groupCamera.Controls.Add(lblTriggerMode);

            _comboTriggerMode = new ComboBox
            {
                Location = new Point(90, 22),
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _comboTriggerMode.Items.Add("关闭触发");
            _comboTriggerMode.Items.Add("软触发");
            _comboTriggerMode.Items.Add("Line0");
            _comboTriggerMode.Items.Add("Line1");
            _comboTriggerMode.SelectedIndex = 0;
            _comboTriggerMode.SelectedIndexChanged += ComboTriggerMode_SelectedIndexChanged;
            _groupCamera.Controls.Add(_comboTriggerMode);

            // 创建开始/停止采集按钮
            _btnStartGrab = new Button
            {
                Text = "开始采集",
                Location = new Point(15, 60),
                Width = 100
            };
            _btnStartGrab.Click += BtnStartGrab_Click;
            _groupCamera.Controls.Add(_btnStartGrab);

            _btnStopGrab = new Button
            {
                Text = "停止采集",
                Location = new Point(125, 60),
                Width = 100,
                Enabled = false
            };
            _btnStopGrab.Click += BtnStopGrab_Click;
            _groupCamera.Controls.Add(_btnStopGrab);

            // 创建触发控制
            _btnTriggerOnce = new Button
            {
                Text = "单次触发",
                Location = new Point(15, 95),
                Width = 100,
                Enabled = false
            };
            _btnTriggerOnce.Click += BtnTriggerOnce_Click;
            _groupCamera.Controls.Add(_btnTriggerOnce);

            _lblInterval = new Label
            {
                Text = "间隔(ms):",
                Location = new Point(125, 98),
                AutoSize = true,
                Enabled = false
            };
            _groupCamera.Controls.Add(_lblInterval);

            _txtInterval = new TextBox
            {
                Text = "1000",
                Location = new Point(190, 95),
                Width = 50,
                Enabled = false
            };
            _groupCamera.Controls.Add(_txtInterval);

            _btnStartContinuous = new Button
            {
                Text = "开始",
                Location = new Point(250, 95),
                Width = 60,
                Enabled = false
            };
            _btnStartContinuous.Click += BtnStartContinuous_Click;
            _groupCamera.Controls.Add(_btnStartContinuous);

            _btnStopContinuous = new Button
            {
                Text = "停止",
                Location = new Point(315, 95),
                Width = 60,
                Enabled = false
            };
            _btnStopContinuous.Click += BtnStopContinuous_Click;
            _groupCamera.Controls.Add(_btnStopContinuous);

            // 创建图像显示区域
            _pictureBox = new PictureBox
            {
                Location = new Point(12, 200),
                Size = new Size(760, 320),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(_pictureBox);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                RefreshDeviceList();
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
                _cameraManager?.Dispose();
                CameraUtils.ReleaseSDK();
            }
            catch { }
        }

        private void RefreshDeviceList()
        {
            try
            {
                _comboDevices.Items.Clear();
                _deviceList = _cameraManager.RefreshDeviceList();

                if (_deviceList.Count == 0)
                {
                    _comboDevices.Items.Add("未检测到设备");
                    _comboDevices.SelectedIndex = 0;
                    _comboDevices.Enabled = false;
                    _btnConnect.Enabled = false;
                    UpdateStatus("未检测到相机设备");
                }
                else
                {
                    foreach (var device in _deviceList)
                    {
                        string deviceName = $"{device.ManufacturerName} {device.ModelName} ({device.SerialNumber})";
                        _comboDevices.Items.Add(deviceName);
                    }
                    _comboDevices.SelectedIndex = 0;
                    _comboDevices.Enabled = true;
                    _btnConnect.Enabled = true;
                    UpdateStatus($"检测到 {_deviceList.Count} 个相机设备");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取设备列表失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (_cameraManager.ActiveDevice != null)
            {
                try
                {
                    _cameraManager.DisconnectDevice();
                    _btnConnect.Text = "连接";
                    _groupCamera.Enabled = false;
                    UpdateStatus("已断开相机连接");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"断开设备失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                try
                {
                    int index = _comboDevices.SelectedIndex;
                    if (index >= 0 && index < _deviceList.Count)
                    {
                        bool success = _cameraManager.ConnectDevice(index);
                        if (success)
                        {
                            _btnConnect.Text = "断开";
                            _groupCamera.Enabled = true;
                            UpdateStatus("已连接相机");
                        }
                        else
                        {
                            MessageBox.Show("连接设备失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"连接设备失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnStartGrab_Click(object sender, EventArgs e)
        {
            try
            {
                bool success = _cameraManager.StartGrabbing();
                if (success)
                {
                    _btnStartGrab.Enabled = false;
                    _btnStopGrab.Enabled = true;
                    UpdateStatus("开始图像采集");
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
                _cameraManager.StopGrabbing();
                _btnStartGrab.Enabled = true;
                _btnStopGrab.Enabled = false;
                UpdateStatus("停止图像采集");
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
                switch (index)
                {
                    case 0: // 关闭触发
                        _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Off);
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 1: // 软触发
                        _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Software);
                        _btnTriggerOnce.Enabled = true;
                        _lblInterval.Enabled = true;
                        _txtInterval.Enabled = true;
                        _btnStartContinuous.Enabled = true;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 2: // Line0
                        _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Line0);
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 3: // Line1
                        _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Line1);
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                }
                UpdateStatus($"触发模式已切换到: {_comboTriggerMode.SelectedItem}");
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
                _cameraManager.TriggerOnce();
                UpdateStatus("执行一次软触发");
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
                if (!int.TryParse(_txtInterval.Text, out int interval) || interval <= 0)
                {
                    MessageBox.Show("请输入有效的间隔时间", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _btnStartContinuous.Enabled = false;
                _btnStopContinuous.Enabled = true;
                UpdateStatus($"开始连续触发, 间隔: {interval}ms");

                await _cameraManager.StartContinuousTriggerAsync(interval);
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
                _cameraManager.StopContinuousTrigger();
                _btnStartContinuous.Enabled = true;
                _btnStopContinuous.Enabled = false;
                UpdateStatus("停止连续触发");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停止连续触发失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CameraManager_ImageUpdated(object sender, ImageEventArgs e)
        {
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action<object, ImageEventArgs>(CameraManager_ImageUpdated), sender, e);
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
}
