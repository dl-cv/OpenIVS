using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DLCV.Camera;
using MvCameraControl;
using System.Xml;

namespace OpenIVSWPF
{
    /// <summary>
    /// SettingsWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // 相机管理器引用
        private CameraManager _cameraManager;
        
        // 设置属性
        public string SelectedPortName { get; private set; }
        public int BaudRate { get; private set; }
        public int DataBits { get; private set; }
        public StopBits StopBits { get; private set; }
        public Parity Parity { get; private set; }
        public int DeviceId { get; private set; }
        public int SelectedCameraIndex { get; private set; }
        public string CameraUserDefinedName { get; private set; }
        public bool UseTrigger { get; private set; }
        public bool UseSoftTrigger { get; private set; }
        public string ModelPath { get; private set; }
        public float Speed { get; private set; }
        
        // 设置结果
        public bool IsSettingsSaved { get; private set; }
        
        // 设置文件路径
        private readonly string _settingsFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings.xml");

        public SettingsWindow(Settings currentSettings, CameraManager cameraManager = null)
        {
            InitializeComponent();
            
            _cameraManager = cameraManager;
            
            // 加载当前设置
            LoadSettings(currentSettings);
            
            // 初始化界面
            InitializeUI();
        }

        private void InitializeUI()
        {
            // 初始化串口列表
            LoadSerialPorts();
            
            // 初始化相机列表
            LoadCameraList();
            
            // 设置触发模式选项的可用性
            UpdateTriggerOptions();
        }

        private void LoadSettings(Settings settings)
        {
            // Modbus设置
            SelectedPortName = settings.PortName;
            BaudRate = settings.BaudRate;
            DataBits = settings.DataBits;
            StopBits = settings.StopBits;
            Parity = settings.Parity;
            DeviceId = settings.DeviceId;
            
            // 相机设置
            SelectedCameraIndex = settings.CameraIndex;
            CameraUserDefinedName = settings.CameraUserDefinedName;
            UseTrigger = settings.UseTrigger;
            UseSoftTrigger = settings.UseSoftTrigger;
            
            // 模型设置
            ModelPath = settings.ModelPath;
            
            // 设备设置
            Speed = settings.Speed;

            // 更新UI
            cbPortName.Text = SelectedPortName;
            cbBaudRate.Text = BaudRate.ToString();
            cbDataBits.Text = DataBits.ToString();
            cbStopBits.Text = StopBits.ToString();
            cbParity.Text = Parity.ToString();

            cbCameraList.SelectedIndex = SelectedCameraIndex;

            chkUseTrigger.IsChecked = UseTrigger;
            rbSoftTrigger.IsChecked = UseSoftTrigger;
            rbHardTrigger.IsChecked = !UseSoftTrigger;

            txtModelPath.Text = ModelPath;
            txtSpeed.Text = Speed.ToString();
        }

        private void LoadSerialPorts()
        {
            cbPortName.Items.Clear();
            
            // 获取可用的串口
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                cbPortName.Items.Add(port);
            }
            
            // 选择当前串口
            if (!string.IsNullOrEmpty(SelectedPortName) && cbPortName.Items.Contains(SelectedPortName))
            {
                cbPortName.SelectedItem = SelectedPortName;
            }
            else if (cbPortName.Items.Count > 0)
            {
                cbPortName.SelectedIndex = 0;
            }
            
            // 设置波特率
            foreach (ComboBoxItem item in cbBaudRate.Items)
            {
                if (item.Content.ToString() == BaudRate.ToString())
                {
                    item.IsSelected = true;
                    break;
                }
            }
            
            // 设置数据位
            foreach (ComboBoxItem item in cbDataBits.Items)
            {
                if (item.Content.ToString() == DataBits.ToString())
                {
                    item.IsSelected = true;
                    break;
                }
            }
            
            // 设置停止位
            foreach (ComboBoxItem item in cbStopBits.Items)
            {
                if (item.Content.ToString() == StopBits.ToString())
                {
                    item.IsSelected = true;
                    break;
                }
            }
            
            // 设置校验位
            foreach (ComboBoxItem item in cbParity.Items)
            {
                if (item.Content.ToString() == Parity.ToString())
                {
                    item.IsSelected = true;
                    break;
                }
            }
            
            // 设置设备ID
            txtDeviceId.Text = DeviceId.ToString();
        }

        private void LoadCameraList()
        {
            cbCameraList.Items.Clear();
            
            if (_cameraManager != null)
            {
                List<IDeviceInfo> deviceList = _cameraManager.RefreshDeviceList();
                foreach (IDeviceInfo device in deviceList)
                {
                    cbCameraList.Items.Add(device.UserDefinedName);
                }
                
                // 选择当前相机
                if (!string.IsNullOrEmpty(CameraUserDefinedName))
                {
                    for (int i = 0; i < cbCameraList.Items.Count; i++)
                    {
                        if (cbCameraList.Items[i].ToString() == CameraUserDefinedName)
                        {
                            cbCameraList.SelectedIndex = i;
                            break;
                        }
                    }
                }
                
                // 如果没有找到匹配的相机名称，则尝试使用索引
                if (cbCameraList.SelectedIndex < 0)
                {
                    if (SelectedCameraIndex >= 0 && SelectedCameraIndex < cbCameraList.Items.Count)
                    {
                        cbCameraList.SelectedIndex = SelectedCameraIndex;
                    }
                    else if (cbCameraList.Items.Count > 0)
                    {
                        cbCameraList.SelectedIndex = 0;
                    }
                }
            }
            
            // 设置触发模式
            chkUseTrigger.IsChecked = UseTrigger;
            rbSoftTrigger.IsChecked = UseSoftTrigger;
            rbHardTrigger.IsChecked = !UseSoftTrigger;
        }

        private void UpdateTriggerOptions()
        {
            // 根据是否使用触发模式更新选项的可用性
            //spTriggerOptions.IsEnabled = chkUseTrigger.IsChecked == true;
        }

        private void btnRefreshCameras_Click(object sender, RoutedEventArgs e)
        {
            // 记住当前选中的索引
            int currentIndex = cbCameraList.SelectedIndex;
            
            // 重新加载相机列表
            LoadCameraList();
            
            // 如果有可能，保持原来的选择
            if (currentIndex >= 0 && currentIndex < cbCameraList.Items.Count)
            {
                cbCameraList.SelectedIndex = currentIndex;
            }
        }

        private void chkUseTrigger_Checked(object sender, RoutedEventArgs e)
        {
            UpdateTriggerOptions();
        }

        private void chkUseTrigger_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateTriggerOptions();
        }

        private void btnBrowseModel_Click(object sender, RoutedEventArgs e)
        {
            // 打开文件选择对话框
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择模型文件";
            dialog.Filter = "模型文件 (*.dvt)|*.dvt|所有文件 (*.*)|*.*";
            dialog.CheckFileExists = true;
            
            if (dialog.ShowDialog() == true)
            {
                txtModelPath.Text = dialog.FileName;
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 验证并保存设置
                if (!ValidateSettings())
                {
                    return;
                }
                
                // 读取设置值
                SaveSettings();
                
                // 保存设置到XML文件
                SaveSettingsToFile();
                
                // 标记为已保存
                IsSettingsSaved = true;
                
                // 关闭窗口
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateSettings()
        {
            // 验证Modbus设置
            if (cbPortName.SelectedItem == null)
            {
                MessageBox.Show("请选择串口", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (!int.TryParse(txtDeviceId.Text, out int deviceId) || deviceId <= 0)
            {
                MessageBox.Show("设备ID必须是正整数", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            // 验证模型路径
            if (string.IsNullOrEmpty(txtModelPath.Text))
            {
                MessageBox.Show("请选择模型文件", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            // 验证速度设置
            if (!float.TryParse(txtSpeed.Text, out float speed) || speed <= 0)
            {
                MessageBox.Show("速度必须是正数", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            return true;
        }

        private void SaveSettings()
        {
            // Modbus设置
            SelectedPortName = cbPortName.SelectedItem.ToString();
            BaudRate = int.Parse(((ComboBoxItem)cbBaudRate.SelectedItem).Content.ToString());
            DataBits = int.Parse(((ComboBoxItem)cbDataBits.SelectedItem).Content.ToString());
            StopBits = (StopBits)Enum.Parse(typeof(StopBits), ((ComboBoxItem)cbStopBits.SelectedItem).Content.ToString());
            Parity = (Parity)Enum.Parse(typeof(Parity), ((ComboBoxItem)cbParity.SelectedItem).Content.ToString());
            DeviceId = int.Parse(txtDeviceId.Text);
            
            // 相机设置
            SelectedCameraIndex = cbCameraList.SelectedIndex;
            CameraUserDefinedName = cbCameraList.SelectedItem?.ToString() ?? string.Empty;
            UseTrigger = chkUseTrigger.IsChecked == true;
            UseSoftTrigger = rbSoftTrigger.IsChecked == true;
            
            // 模型设置
            ModelPath = txtModelPath.Text;
            
            // 设备设置
            Speed = float.Parse(txtSpeed.Text);
        }

        private void SaveSettingsToFile()
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                XmlElement root;
                
                // 如果文件存在，则加载现有文件
                if (File.Exists(_settingsFilePath))
                {
                    doc.Load(_settingsFilePath);
                    root = doc.DocumentElement;
                }
                else
                {
                    // 创建新的XML文档
                    root = doc.CreateElement("Settings");
                    doc.AppendChild(root);
                }

                // 保存Modbus设置
                SetSettingValue(doc, root, "PortName", SelectedPortName);
                SetSettingValue(doc, root, "BaudRate", BaudRate.ToString());
                SetSettingValue(doc, root, "DataBits", DataBits.ToString());
                SetSettingValue(doc, root, "StopBits", StopBits.ToString());
                SetSettingValue(doc, root, "Parity", Parity.ToString());
                SetSettingValue(doc, root, "DeviceId", DeviceId.ToString());
                
                // 保存相机设置
                SetSettingValue(doc, root, "CameraIndex", SelectedCameraIndex.ToString());
                SetSettingValue(doc, root, "CameraUserDefinedName", CameraUserDefinedName);
                SetSettingValue(doc, root, "UseTrigger", UseTrigger.ToString());
                SetSettingValue(doc, root, "UseSoftTrigger", UseSoftTrigger.ToString());
                
                // 保存模型设置
                SetSettingValue(doc, root, "ModelPath", ModelPath);
                
                // 保存设备设置
                SetSettingValue(doc, root, "Speed", Speed.ToString());
                
                // 保存文件
                doc.Save(_settingsFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置文件时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SetSettingValue(XmlDocument doc, XmlElement root, string key, string value)
        {
            XmlNode node = root.SelectSingleNode(key);
            if (node == null)
            {
                // 如果节点不存在，则创建新节点
                node = doc.CreateElement(key);
                root.AppendChild(node);
            }
            node.InnerText = value;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            // 取消操作，不保存设置
            IsSettingsSaved = false;
            Close();
        }
    }

    /// <summary>
    /// 系统设置类，用于保存和加载设置
    /// </summary>
    public class Settings
    {
        // Modbus设置
        public string PortName { get; set; }
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public StopBits StopBits { get; set; }
        public Parity Parity { get; set; }
        public int DeviceId { get; set; }
        
        // 相机设置
        public int CameraIndex { get; set; }
        public string CameraUserDefinedName { get; set; }
        public bool UseTrigger { get; set; }
        public bool UseSoftTrigger { get; set; }
        
        // 模型设置
        public string ModelPath { get; set; }
        
        // 设备设置
        public float Speed { get; set; }
        
        public Settings()
        {
            // 默认设置
            PortName = "";
            BaudRate = 38400;
            DataBits = 8;
            StopBits = StopBits.One;
            Parity = Parity.None;
            DeviceId = 1;
            
            CameraIndex = 0;
            CameraUserDefinedName = string.Empty;
            UseTrigger = true;
            UseSoftTrigger = true;
            
            ModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "default.dvt");
            
            Speed = 100.0f;
        }
    }
} 