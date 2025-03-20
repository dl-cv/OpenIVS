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
using OpenIVSWPF.Managers;
using DLCV;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using WinForms = System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;

// 使用完全限定名避免命名冲突
using Window = System.Windows.Window;

namespace OpenIVSWPF
{
    /// <summary>
    /// SettingsWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // 相机管理器引用
        private CameraManager _cameraManager = CameraInstance.Instance;
        private ModbusApi _modbusApi = ModbusManager.Instance;
        
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
        public string SavePath { get; private set; }
        public bool SaveOKImage { get; private set; }
        public bool SaveNGImage { get; private set; }
        public string ImageFormat { get; private set; }
        public string JpegQuality { get; private set; }
        public float TargetPosition { get; private set; }
        
        // 设置结果
        public bool IsSettingsSaved { get; private set; }
        
        // 设置文件路径
        private readonly string _settingsFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings.xml");

        public SettingsWindow(Settings currentSettings)
        {
            InitializeComponent();
            
            // 加载当前设置
            LoadSettings(currentSettings);
            
            // 初始化界面
            InitializeUI();
            
            // 获取当前位置
            GetCurrentPosition();
        }

        private void GetCurrentPosition()
        {
            try
            {
                // 如果Modbus未连接，则尝试连接
                if (_modbusApi == null || !IsModbusConnected())
                {
                    InitializeModbus();
                }

                if (_modbusApi != null && IsModbusConnected())
                {
                    // 读取当前位置（地址32，浮点数）
                    float currentPosition = _modbusApi.ReadFloat(32);
                    
                    // 这里不使用直接引用UI控件，而是在UI定义完成后更新
                    Dispatcher.BeginInvoke(new Action(() => {
                        try {
                            // 使用安全的方式访问控件
                            var control = FindName("txtCurrentPosition") as TextBlock;
                            if (control != null)
                            {
                                control.Text = currentPosition.ToString("F1");
                            }
                        }
                        catch { /* 忽略UI更新错误 */ }
                    }));
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"获取当前位置时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 检查Modbus是否已连接
        private bool IsModbusConnected()
        {
            try
            {
                if (_modbusApi == null)
                    return false;
                
                // 尝试读取保持寄存器来测试连接
                _modbusApi.ReadHoldingRegisters(0, 1);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void InitializeModbus()
        {
            try
            {
                // 关闭已有连接
                if (_modbusApi != null && IsModbusConnected())
                {
                    _modbusApi.Close();
                }
                
                // 使用设置中的串口参数
                if (string.IsNullOrEmpty(SelectedPortName))
                {
                    // 如果未设置串口，尝试获取第一个可用的串口
                    string[] ports = SerialPort.GetPortNames();
                    if (ports.Length == 0)
                    {
                        System.Windows.MessageBox.Show("未检测到串口设备", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    SelectedPortName = ports[0];
                }

                // 设置串口参数
                _modbusApi.SetSerialPort(
                    SelectedPortName,  // 串口
                    BaudRate,         // 波特率
                    DataBits,         // 数据位
                    StopBits,         // 停止位
                    Parity,           // 校验位
                    (byte)DeviceId    // 设备ID
                );

                // 打开串口
                if (!_modbusApi.Open())
                {
                    System.Windows.MessageBox.Show("Modbus设备连接失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Modbus初始化错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            TargetPosition = settings.TargetPosition;

            // 图像保存设置
            SavePath = settings.SavePath;
            SaveOKImage = settings.SaveOKImage;
            SaveNGImage = settings.SaveNGImage;
            ImageFormat = settings.ImageFormat;
            JpegQuality = settings.JpegQuality;

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
            txtTargetPosition.Text = TargetPosition.ToString();

            txtSaveImagePath.Text = SavePath;
            chkSaveOKImage.IsChecked = SaveOKImage;
            chkSaveNGImage.IsChecked = SaveNGImage;
            cbImageFormat.SelectedItem = cbImageFormat.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == ImageFormat);
            txtJpegQuality.Text = JpegQuality;
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
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
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
                System.Windows.MessageBox.Show($"保存设置时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateSettings()
        {
            // 验证Modbus设置
            if (cbPortName.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("请选择串口", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (!int.TryParse(txtDeviceId.Text, out int deviceId) || deviceId <= 0)
            {
                System.Windows.MessageBox.Show("设备ID必须是正整数", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            // 验证模型路径
            if (string.IsNullOrEmpty(txtModelPath.Text))
            {
                System.Windows.MessageBox.Show("请选择模型文件", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            // 验证速度设置
            if (!float.TryParse(txtSpeed.Text, out float speed) || speed <= 0)
            {
                System.Windows.MessageBox.Show("速度必须是正数", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            
            // 目标位置设置
            if (float.TryParse(txtTargetPosition.Text, out float targetPos))
            {
                TargetPosition = targetPos;
            }

            // 图像保存设置
            SavePath = txtSaveImagePath.Text;
            SaveOKImage = chkSaveOKImage.IsChecked == true;
            SaveNGImage = chkSaveNGImage.IsChecked == true;
            ImageFormat = ((ComboBoxItem)cbImageFormat.SelectedItem).Content.ToString();
            JpegQuality = txtJpegQuality.Text;
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
                SetSettingValue(doc, root, "SavePath", SavePath);
                SetSettingValue(doc, root, "SaveOKImage", SaveOKImage.ToString());
                SetSettingValue(doc, root, "SaveNGImage", SaveNGImage.ToString());
                SetSettingValue(doc, root, "ImageFormat", ImageFormat);
                SetSettingValue(doc, root, "JpegQuality", JpegQuality);
                
                // 保存模型设置
                SetSettingValue(doc, root, "ModelPath", ModelPath);
                
                // 保存设备设置
                SetSettingValue(doc, root, "Speed", Speed.ToString());
                
                // 保存目标位置
                SetSettingValue(doc, root, "TargetPosition", TargetPosition.ToString());
                
                // 保存文件
                doc.Save(_settingsFilePath);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存设置文件时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

        #region 图像保存相关功能

        private void btnBrowseSavePath_Click(object sender, RoutedEventArgs e)
        {
            // 打开文件夹选择对话框
            WinForms.FolderBrowserDialog dialog = new WinForms.FolderBrowserDialog();
            dialog.Description = "选择图像保存路径";
            
            // 如果已有路径，则设置为初始路径
            if (!string.IsNullOrEmpty(txtSaveImagePath.Text))
            {
                dialog.SelectedPath = txtSaveImagePath.Text;
            }
            
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                txtSaveImagePath.Text = dialog.SelectedPath;
            }
        }

        private void cbImageFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 根据图像格式更新UI
            if (txtJpegQuality != null)
            {
                string selectedFormat = ((ComboBoxItem)cbImageFormat.SelectedItem).Content.ToString();
                bool isJpeg = selectedFormat == "JPG";
                
                // 只有JPG格式才显示质量设置
                txtJpegQuality.IsEnabled = isJpeg;
            }
        }

        private async void btnTestSaveImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 验证保存路径
                if (string.IsNullOrEmpty(txtSaveImagePath.Text))
                {
                    System.Windows.MessageBox.Show("请先选择图像保存路径", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 确保路径存在
                if (!Directory.Exists(txtSaveImagePath.Text))
                {
                    try
                    {
                        Directory.CreateDirectory(txtSaveImagePath.Text);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"创建目录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // 获取当前相机图像
                Bitmap image = null;
                
                if (_cameraManager == null || _cameraManager.ActiveDevice == null || !_cameraManager.ActiveDevice.IsOpen)
                {
                    // 连接相机
                    try
                    {
                        // 刷新设备列表
                        List<IDeviceInfo> deviceList = _cameraManager.RefreshDeviceList();

                        if (deviceList.Count == 0)
                        {
                            System.Windows.MessageBox.Show("未检测到相机设备", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        // 检查相机索引是否有效
                        int cameraIndex = SelectedCameraIndex;
                        if (cameraIndex < 0 || cameraIndex >= deviceList.Count)
                        {
                            cameraIndex = 0;
                        }

                        // 连接选中的相机
                        bool success = _cameraManager.ConnectDevice(cameraIndex);
                        if (!success)
                        {
                            System.Windows.MessageBox.Show("相机连接失败", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        // 设置触发模式
                        TriggerConfig.TriggerMode mode = TriggerConfig.TriggerMode.Software;
                        _cameraManager.SetTriggerMode(mode);
                        _cameraManager.StartGrabbing();
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"相机初始化错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // 捕获图像
                // 使用TaskCompletionSource等待图像更新事件
                var tcs = new TaskCompletionSource<Bitmap>();
                                    
                // 设置图像捕获事件处理
                EventHandler<ImageEventArgs> handler = null;
                handler = (s, args) =>
                {
                    // 捕获到图像后，转换为Bitmap
                    if (args.Image != null)
                    {
                        tcs.TrySetResult(args.Image.Clone() as Bitmap);
                    }
                    
                    // 移除事件处理器，防止多次触发
                    _cameraManager.ImageUpdated -= handler;
                };
                                    
                // 添加事件处理
                _cameraManager.ImageUpdated += handler;
                                    
                // 执行软触发
                _cameraManager.TriggerOnce();
                                    
                // 添加超时处理，2秒内如果没有图像返回，则取消
                using (var timeoutCts = new CancellationTokenSource(2000))
                {
                    try
                    {
                        // 注册取消操作
                        timeoutCts.Token.Register(() => 
                        {
                            _cameraManager.ImageUpdated -= handler;
                            tcs.TrySetCanceled();
                        });
                                            
                        // 等待图像或取消
                        image = await tcs.Task;
                    }
                    catch (OperationCanceledException)
                    {
                        System.Windows.MessageBox.Show("图像捕获超时或被取消", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"图像捕获过程中发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                if (image == null)
                {
                    System.Windows.MessageBox.Show("无法获取图像，请确保相机正常工作", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 生成文件名（使用时间戳）
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string extension = ((ComboBoxItem)cbImageFormat.SelectedItem).Content.ToString().ToLower();
                string filename = $"test_{timestamp}.{extension}";
                string fullPath = Path.Combine(txtSaveImagePath.Text, filename);

                // 使用OpenCV保存图像
                using (var mat = BitmapConverter.ToMat(image))
                {
                    if (extension == "jpg")
                    {
                        // 获取JPG质量设置
                        int quality = 90;
                        if (!string.IsNullOrEmpty(txtJpegQuality.Text) && int.TryParse(txtJpegQuality.Text, out int jpegQuality))
                        {
                            quality = Math.Min(100, Math.Max(1, jpegQuality)); // 确保在1-100范围内
                        }

                        // 保存为JPG格式
                        var parameters = new int[] 
                        { 
                            (int)OpenCvSharp.ImwriteFlags.JpegQuality, quality,
                            (int)OpenCvSharp.ImwriteFlags.JpegProgressive, 1
                        };
                        OpenCvSharp.Cv2.ImWrite(fullPath, mat, parameters);
                    }
                    else // BMP格式
                    {
                        OpenCvSharp.Cv2.ImWrite(fullPath, mat);
                    }
                }

                // 显示成功信息
                System.Windows.MessageBox.Show($"图像已保存到：\n{fullPath}", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存图像时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 获取指定图像格式的编码器
        private System.Drawing.Imaging.ImageCodecInfo GetEncoder(System.Drawing.Imaging.ImageFormat format)
        {
            foreach (var codec in System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders())
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
        #endregion

        #region 设备控制相关功能
        private void btnGoHome_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_modbusApi == null)
                {
                    System.Windows.MessageBox.Show("Modbus未连接，无法执行操作", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 发送回原点命令（1到地址50）
                bool result = _modbusApi.WriteSingleRegister(50, 1);
                if (result)
                {
                    System.Windows.MessageBox.Show("已发送回原点命令", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("发送回原点命令失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"回原点操作发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void btnGoToPosition_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 如果Modbus未连接，则尝试连接
                if (_modbusApi == null || !IsModbusConnected())
                {
                    InitializeModbus();
                }

                if (_modbusApi == null || !IsModbusConnected())
                {
                    System.Windows.MessageBox.Show("Modbus未连接，无法执行操作", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 获取目标位置
                if (!float.TryParse(txtTargetPosition.Text, out float targetPosition))
                {
                    System.Windows.MessageBox.Show("请输入有效的目标位置", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 禁用按钮，防止重复点击
                btnGoToPosition.IsEnabled = false;
                
                try
                {
                    // 设置目标位置（地址8，浮点数）
                    bool resultSetPosition = _modbusApi.WriteFloat(8, targetPosition);
                    if (!resultSetPosition)
                    {
                        System.Windows.MessageBox.Show("设置目标位置失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 发送移动命令（地址50，整数2）
                    bool resultCommand = _modbusApi.WriteSingleRegister(50, 2);
                    if (!resultCommand)
                    {
                        System.Windows.MessageBox.Show("发送移动命令失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 等待移动完成（轮询当前位置）
                    using (var cts = new CancellationTokenSource())
                    {
                        // 60秒超时
                        cts.CancelAfter(TimeSpan.FromSeconds(60));
                        
                        bool isReached = false;
                        while (!isReached && !cts.Token.IsCancellationRequested)
                        {
                            // 读取当前位置（地址32，浮点数）
                            float currentPosition = _modbusApi.ReadFloat(32);
                            
                            // 更新位置显示
                            Dispatcher.BeginInvoke(new Action(() => {
                                try {
                                    // 使用安全的方式访问控件
                                    var control = FindName("txtCurrentPosition") as TextBlock;
                                    if (control != null)
                                    {
                                        control.Text = currentPosition.ToString("F1");
                                    }
                                }
                                catch { /* 忽略UI更新错误 */ }
                            }));
                            
                            // 判断是否到达目标位置（允许一定误差）
                            if (Math.Abs(currentPosition - targetPosition) < 1.0f)
                            {
                                isReached = true;
                            }
                            else
                            {
                                // 等待100ms再次检查
                                await Task.Delay(100, cts.Token);
                            }
                        }

                        if (isReached)
                        {
                            System.Windows.MessageBox.Show($"已到达位置：{targetPosition}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                            
                            // 保存到设置中
                            TargetPosition = targetPosition;
                        }
                        else
                        {
                            System.Windows.MessageBox.Show("移动超时，未到达目标位置", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
                finally
                {
                    // 恢复按钮状态
                    btnGoToPosition.IsEnabled = true;
                }
            }
            catch (TaskCanceledException)
            {
                System.Windows.MessageBox.Show("移动操作已取消", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"移动过程中发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion
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
        public float TargetPosition { get; set; }
        
        // 图像保存设置
        public string SavePath { get; set; }
        public bool SaveOKImage { get; set; }
        public bool SaveNGImage { get; set; }
        public string ImageFormat { get; set; }
        public string JpegQuality { get; set; }
        
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
            TargetPosition = 0.0f;

            // 图像保存设置
            SavePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");
            SaveOKImage = true;
            SaveNGImage = true;
            ImageFormat = "JPG";
            JpegQuality = "98";
        }
    }
} 