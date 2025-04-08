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
        private CameraManager _cameraManager;
        private List<DeviceInfoWrapper> _deviceList;

        public Form1()
        {
            InitializeComponent();
            
            // 确保所有控件已正确初始化
            if (_comboTriggerMode == null || _comboDevices == null || 
                _btnTriggerOnce == null || _btnStartContinuous == null || 
                _btnStopContinuous == null || _pictureBox == null)
            {
                MessageBox.Show("控件初始化失败，程序可能无法正常工作", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            // 添加触发模式选项
            _comboTriggerMode.Items.Clear(); // 确保列表为空
            _comboTriggerMode.Items.Add("关闭触发");
            _comboTriggerMode.Items.Add("软触发");
            _comboTriggerMode.Items.Add("Line0");
            _comboTriggerMode.Items.Add("Line1");
            
            // 创建相机管理器并绑定事件
            _cameraManager = new CameraManager();
            _cameraManager.ImageUpdated += CameraManager_ImageUpdated;
            
            // 禁用相机控制直到连接相机
            _groupCamera.Enabled = false;
            
            // 在所有初始化完成后，设置默认选项
            _comboTriggerMode.SelectedIndex = 0;
            
            // 绑定窗体事件
            this.FormClosing += Form1_FormClosing;
            this.Load += Form1_Load;
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
                        _comboDevices.Items.Add(device.ToString());
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
                if (_cameraManager == null || _cameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
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
                if (_cameraManager == null || _cameraManager.ActiveDevice == null)
                {
                    return;
                }
                
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
                
                // 若相机未连接，仅设置UI状态，不执行相机操作
                bool isCameraConnected = _cameraManager != null && _cameraManager.ActiveDevice != null;
                
                // 根据不同模式设置UI状态
                switch (index)
                {
                    case 0: // 关闭触发
                        if (isCameraConnected)
                            _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Off);
                        
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 1: // 软触发
                        if (isCameraConnected)
                            _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Software);
                        
                        _btnTriggerOnce.Enabled = isCameraConnected;
                        _lblInterval.Enabled = isCameraConnected;
                        _txtInterval.Enabled = isCameraConnected;
                        _btnStartContinuous.Enabled = isCameraConnected;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 2: // Line0
                        if (isCameraConnected)
                            _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Line0);
                        
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 3: // Line1
                        if (isCameraConnected)
                            _cameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Line1);
                        
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                }
                
                if (isCameraConnected)
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
                if (_cameraManager == null || _cameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
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
                if (_cameraManager == null || _cameraManager.ActiveDevice == null)
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
                if (_cameraManager == null || _cameraManager.ActiveDevice == null)
                {
                    return;
                }
                
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
