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
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.WindowsAPICodePack.Dialogs;

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
        private ModbusApi _modbusApi = ModbusInstance.Instance;

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

        // 拍照设置
        public int PreCaptureDelay { get; private set; }  // 拍照前等待时间（毫秒）

        // 设置结果
        public bool IsSettingsSaved { get; private set; }

        // 本地图像文件夹设置
        public bool UseLocalFolder { get; private set; }
        public string LocalFolderPath { get; private set; }

        // 加载标志
        private bool _isLoading = false;

        // 文件对话框 Helper
        private const int FOS_PICKFOLDERS = 0x20;

        public SettingsWindow()
        {
            // 初始化WPF组件
            InitializeComponent();

            // 加载当前设置
            LoadSettings();

            // 在UI初始化中只加载不会阻塞的组件（串口列表等）
            InitializeUIBasic();

            // 获取当前位置（不阻塞UI）
            Task.Run(() => GetCurrentPosition());

            // 相机加载完成后启动，使用Loaded事件确保所有控件已初始化
            this.Loaded += (s, e) =>
            {
                // 异步加载相机列表
                LoadCameraListAsync();
            };
        }

        private async void LoadCameraListAsync()
        {
            try
            {
                // 设置加载状态
                _isLoading = true;

                // 显示加载指示器
                this.Dispatcher.Invoke(() =>
                {
                    if (cameraLoadingIndicator != null)
                        cameraLoadingIndicator.Visibility = Visibility.Visible;

                    if (cbCameraList != null)
                        cbCameraList.IsEnabled = false;

                    if (btnRefreshCameras != null)
                        btnRefreshCameras.IsEnabled = false;
                });

                // 在后台线程执行相机扫描
                List<IDeviceInfo> deviceList = null;
                await Task.Run(() =>
                {
                    try
                    {
                        if (_cameraManager != null)
                        {
                            deviceList = _cameraManager.RefreshDeviceList();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"相机扫描异常: {ex.Message}");
                    }
                });

                // 更新UI
                this.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (cbCameraList != null)
                        {
                            // 清空当前列表
                            cbCameraList.Items.Clear();

                            // 添加扫描到的设备
                            if (deviceList != null && deviceList.Count > 0)
                            {
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
                        }

                        // 设置触发模式
                        if (chkUseTrigger != null)
                            chkUseTrigger.IsChecked = UseTrigger;

                        if (rbSoftTrigger != null)
                            rbSoftTrigger.IsChecked = UseSoftTrigger;

                        if (rbHardTrigger != null)
                            rbHardTrigger.IsChecked = !UseSoftTrigger;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"更新UI异常: {ex.Message}");
                    }
                    finally
                    {
                        // 隐藏加载指示器
                        if (cameraLoadingIndicator != null)
                            cameraLoadingIndicator.Visibility = Visibility.Collapsed;

                        // 恢复控件状态
                        if (cbCameraList != null)
                            cbCameraList.IsEnabled = true;

                        if (btnRefreshCameras != null)
                            btnRefreshCameras.IsEnabled = true;

                        // 重置加载状态
                        _isLoading = false;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"相机列表加载异常: {ex.Message}");

                // 确保UI状态恢复
                this.Dispatcher.Invoke(() =>
                {
                    if (cameraLoadingIndicator != null)
                        cameraLoadingIndicator.Visibility = Visibility.Collapsed;

                    if (cbCameraList != null)
                        cbCameraList.IsEnabled = true;

                    if (btnRefreshCameras != null)
                        btnRefreshCameras.IsEnabled = true;

                    _isLoading = false;
                });
            }
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
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
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
                else
                {
                    // Modbus未连接或连接失败，更新UI显示离线状态
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var control = FindName("txtCurrentPosition") as TextBlock;
                            if (control != null)
                            {
                                control.Text = "离线";
                            }
                        }
                        catch { /* 忽略UI更新错误 */ }
                    }));
                }
            }
            catch (Exception ex)
            {
                // 不再弹窗显示错误，改为记录日志
                System.Diagnostics.Debug.WriteLine($"获取当前位置时发生错误：{ex.Message}");
                
                // 更新UI显示离线状态
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var control = FindName("txtCurrentPosition") as TextBlock;
                        if (control != null)
                        {
                            control.Text = "离线";
                        }
                    }
                    catch { /* 忽略UI更新错误 */ }
                }));
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
                var settings = SettingsManager.Instance.Settings;
                if (string.IsNullOrEmpty(settings.PortName))
                {
                    // 如果未设置串口，尝试获取第一个可用的串口
                    string[] ports = SerialPort.GetPortNames();
                    if (ports.Length == 0)
                    {
                        // 无串口设备时不再弹出提示，支持离线模式
                        // 记录日志
                        System.Diagnostics.Debug.WriteLine("未检测到串口设备，将使用离线模式");
                        return;
                    }
                    settings.PortName = ports[0];
                }

                // 设置串口参数
                _modbusApi.SetSerialPort(
                    settings.PortName,  // 串口
                    settings.BaudRate,  // 波特率
                    settings.DataBits,  // 数据位
                    settings.StopBits,  // 停止位
                    settings.Parity,    // 校验位
                    (byte)settings.DeviceId // 设备ID
                );

                // 打开串口
                if (!_modbusApi.Open())
                {
                    // 不再弹窗提示，支持离线模式
                    System.Diagnostics.Debug.WriteLine("Modbus设备连接失败，将使用离线模式");
                    // 更新状态栏（如果需要）
                    UpdateStatus("Modbus设备未连接，将使用离线模式");
                }
            }
            catch (Exception ex)
            {
                // 不再弹窗提示错误，而是记录日志
                System.Diagnostics.Debug.WriteLine($"Modbus初始化错误：{ex.Message}");
                // 更新状态栏（如果需要）
                UpdateStatus("Modbus连接失败，将使用离线模式");
            }
        }

        private void InitializeUIBasic()
        {
            // 初始化串口列表
            LoadSerialPorts();

            // 设置触发模式选项的可用性
            UpdateTriggerOptions();

            // 更新图像源UI状态
            UpdateImageSourceUI();
        }

        private void LoadSettings()
        {
            var settings = SettingsManager.Instance.Settings;

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

            // 本地图像文件夹设置
            UseLocalFolder = settings.UseLocalFolder;
            LocalFolderPath = settings.LocalFolderPath;

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

            // 拍照设置
            PreCaptureDelay = settings.PreCaptureDelay;

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
            txtPreCaptureDelay.Text = PreCaptureDelay.ToString();

            txtSaveImagePath.Text = SavePath;
            chkSaveOKImage.IsChecked = SaveOKImage;
            chkSaveNGImage.IsChecked = SaveNGImage;
            cbImageFormat.SelectedItem = cbImageFormat.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == ImageFormat);
            txtJpegQuality.Text = JpegQuality;

            // 更新本地图像文件夹UI
            chkUseLocalFolder.IsChecked = UseLocalFolder;
            txtLocalFolderPath.Text = LocalFolderPath;

            // 根据是否使用本地文件夹更新UI状态
            UpdateImageSourceUI();
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

        private void UpdateTriggerOptions()
        {
            // 根据是否使用触发模式更新选项的可用性
            //spTriggerOptions.IsEnabled = chkUseTrigger.IsChecked == true;
        }

        private async void btnRefreshCameras_Click(object sender, RoutedEventArgs e)
        {
            // 防止重复点击
            if (_isLoading)
                return;

            // 直接调用异步加载方法
            LoadCameraListAsync();
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

                // 读取UI中的设置值到临时变量
                SaveSettings();

                // 保存设置到文件
                SettingsManager.Instance.SaveSettings();

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
            // 检查是否是离线模式（使用本地图像文件夹）
            bool isOfflineMode = chkUseLocalFolder.IsChecked == true;

            // 验证Modbus设置（仅在非离线模式下强制要求）
            if (!isOfflineMode && cbPortName.SelectedItem == null)
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

            // 在离线模式下，验证本地图像文件夹路径
            if (isOfflineMode && string.IsNullOrEmpty(txtLocalFolderPath.Text))
            {
                System.Windows.MessageBox.Show("请选择本地图像文件夹路径", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void SaveSettings()
        {
            var settings = SettingsManager.Instance.Settings;

            // Modbus设置
            settings.PortName = cbPortName.SelectedItem.ToString();
            settings.BaudRate = int.Parse(((ComboBoxItem)cbBaudRate.SelectedItem).Content.ToString());
            settings.DataBits = int.Parse(((ComboBoxItem)cbDataBits.SelectedItem).Content.ToString());
            settings.StopBits = (StopBits)Enum.Parse(typeof(StopBits), ((ComboBoxItem)cbStopBits.SelectedItem).Content.ToString());
            settings.Parity = (Parity)Enum.Parse(typeof(Parity), ((ComboBoxItem)cbParity.SelectedItem).Content.ToString());
            settings.DeviceId = int.Parse(txtDeviceId.Text);

            // 相机设置
            settings.CameraIndex = cbCameraList.SelectedIndex;
            settings.CameraUserDefinedName = cbCameraList.SelectedItem?.ToString() ?? string.Empty;
            settings.UseTrigger = chkUseTrigger.IsChecked == true;
            settings.UseSoftTrigger = rbSoftTrigger.IsChecked == true;

            // 本地图像文件夹设置
            settings.UseLocalFolder = chkUseLocalFolder.IsChecked == true;
            settings.LocalFolderPath = txtLocalFolderPath.Text;

            // 模型设置
            settings.ModelPath = txtModelPath.Text;

            // 设备设置
            settings.Speed = float.Parse(txtSpeed.Text);

            // 目标位置设置
            if (float.TryParse(txtTargetPosition.Text, out float targetPos))
            {
                settings.TargetPosition = targetPos;
            }

            // 拍照设置
            if (int.TryParse(txtPreCaptureDelay.Text, out int delay))
            {
                settings.PreCaptureDelay = Math.Max(0, delay); // 确保不会小于0
            }

            // 图像保存设置
            settings.SavePath = txtSaveImagePath.Text;
            settings.SaveOKImage = chkSaveOKImage.IsChecked == true;
            settings.SaveNGImage = chkSaveNGImage.IsChecked == true;
            settings.ImageFormat = ((ComboBoxItem)cbImageFormat.SelectedItem).Content.ToString();
            settings.JpegQuality = txtJpegQuality.Text;
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
            // 使用Windows选择文件夹对话框
            string selectedFolder = SelectFolder("选择图像保存路径", txtSaveImagePath.Text);
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                txtSaveImagePath.Text = selectedFolder;
            }
        }

        private string SelectFolder(string title, string initialFolder = "")
        {
            var dialog = new Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog
            {
                Title = title,
                IsFolderPicker = true,
                InitialDirectory = initialFolder,
                EnsurePathExists = true,
                AllowNonFileSystemItems = false
            };

            return dialog.ShowDialog() == Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok
                ? dialog.FileName
                : null;
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

                // 检查是否为离线模式
                bool isOfflineMode = chkUseLocalFolder.IsChecked == true;
                
                // 获取当前相机图像
                Bitmap image = null;

                if (isOfflineMode)
                {
                    // 尝试从本地文件夹加载测试图像
                    if (string.IsNullOrEmpty(txtLocalFolderPath.Text) || !Directory.Exists(txtLocalFolderPath.Text))
                    {
                        System.Windows.MessageBox.Show("请先选择有效的本地图像文件夹", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // 尝试从文件夹获取第一张图像
                    string[] imageFiles = Directory.GetFiles(txtLocalFolderPath.Text, "*.jpg")
                                .Concat(Directory.GetFiles(txtLocalFolderPath.Text, "*.jpeg"))
                                .Concat(Directory.GetFiles(txtLocalFolderPath.Text, "*.png"))
                                .Concat(Directory.GetFiles(txtLocalFolderPath.Text, "*.bmp"))
                                .ToArray();

                    if (imageFiles.Length == 0)
                    {
                        System.Windows.MessageBox.Show("本地图像文件夹中未找到图像文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    try
                    {
                        // 加载第一张图像
                        image = new Bitmap(imageFiles[0]);
                        UpdateStatus($"已从本地文件夹加载图像：{Path.GetFileName(imageFiles[0])}");
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"加载本地图像失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    // 使用相机捕获图像
                    if (_cameraManager == null || _cameraManager.ActiveDevice == null || !_cameraManager.ActiveDevice.IsOpen)
                    {
                        // 连接相机
                        try
                        {
                            // 刷新设备列表
                            List<IDeviceInfo> deviceList = _cameraManager.RefreshDeviceList();

                            if (deviceList.Count == 0)
                            {
                                UpdateStatus("未检测到相机设备，请检查相机连接");
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
                                UpdateStatus("相机连接失败，请检查相机状态");
                                return;
                            }

                            // 设置触发模式
                            TriggerConfig.TriggerMode mode = TriggerConfig.TriggerMode.Software;
                            _cameraManager.SetTriggerMode(mode);
                            _cameraManager.StartGrabbing();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"相机初始化错误：{ex.Message}");
                            UpdateStatus("相机初始化失败，请检查相机连接");
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
                            UpdateStatus("图像捕获超时，请检查相机是否正常工作");
                            return;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"图像捕获过程中发生错误：{ex.Message}");
                            UpdateStatus("图像捕获失败，请检查相机状态");
                            return;
                        }
                    }
                }

                if (image == null)
                {
                    UpdateStatus("无法获取图像，请确保相机正常工作或选择有效的本地图像文件夹");
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

                // 清理资源
                image.Dispose();

                // 更新状态
                UpdateStatus($"图像已保存到：{fullPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存图像时发生错误：{ex.Message}");
                UpdateStatus("保存图像过程中发生错误");
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
                // 检查是否为离线模式
                bool isOfflineMode = chkUseLocalFolder.IsChecked == true;
                
                if (isOfflineMode || _modbusApi == null || !IsModbusConnected())
                {
                    // 在离线模式下使用更友好的提示
                    UpdateStatus("当前处于离线模式，无法执行回原点操作");
                    return;
                }

                // 发送回原点命令（1到地址50）
                bool result = _modbusApi.WriteSingleRegister(50, 1);
                if (result)
                {
                    UpdateStatus("已发送回原点命令");
                }
                else
                {
                    UpdateStatus("发送回原点命令失败");
                }
            }
            catch (Exception ex)
            {
                // 记录日志并使用更友好的提示
                System.Diagnostics.Debug.WriteLine($"回原点操作发生错误：{ex.Message}");
                UpdateStatus("回原点操作失败，请检查设备连接");
            }
        }

        private async void btnGoToPosition_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 检查是否为离线模式
                bool isOfflineMode = chkUseLocalFolder.IsChecked == true;
                
                // 如果Modbus未连接，则尝试连接
                if (!isOfflineMode && (_modbusApi == null || !IsModbusConnected()))
                {
                    InitializeModbus();
                }

                if (isOfflineMode || _modbusApi == null || !IsModbusConnected())
                {
                    // 在离线模式下或连接失败时，使用更友好的提示
                    UpdateStatus("当前处于离线模式，无法执行移动操作");
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

                    // 更新日志状态
                    UpdateStatus($"开始移动到位置：{targetPosition}");

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
                            await Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    // 使用安全的方式访问控件
                                    txtCurrentPosition.Text = currentPosition.ToString("F1");
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
                            // 更新日志状态
                            UpdateStatus($"已到达位置：{targetPosition}");

                            // 保存到设置中
                            SettingsManager.Instance.Settings.TargetPosition = targetPosition;
                        }
                        else
                        {
                            // 更新日志状态
                            UpdateStatus($"移动超时，未到达目标位置：{targetPosition}");
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
                UpdateStatus("移动操作已取消");
            }
            catch (Exception ex)
            {
                // 使用更友好的方式处理错误
                System.Diagnostics.Debug.WriteLine($"移动过程中发生错误：{ex.Message}");
                UpdateStatus($"移动过程中发生错误，请检查设备连接");
            }
        }
        #endregion

        // 添加更新状态的方法，将消息传递给主窗口的状态栏
        private void UpdateStatus(string message)
        {
            // 通过Owner属性获取主窗口引用
            if (Owner is MainWindow mainWindow)
            {
                // 调用主窗口的UpdateStatus方法
                mainWindow.UpdateStatus(message);
            }
        }

        /// <summary>
        /// 更新图像源UI状态
        /// </summary>
        private void UpdateImageSourceUI()
        {
            bool useLocalFolder = chkUseLocalFolder.IsChecked == true;

            // 本地文件夹控件
            spLocalFolderOptions.IsEnabled = useLocalFolder;

            // 相机控件
            cbCameraList.IsEnabled = !useLocalFolder;
            btnRefreshCameras.IsEnabled = !useLocalFolder;
            chkUseTrigger.IsEnabled = !useLocalFolder;
            spTriggerOptions.IsEnabled = !useLocalFolder && chkUseTrigger.IsChecked == true;
        }

        private void chkUseLocalFolder_Checked(object sender, RoutedEventArgs e)
        {
            UpdateImageSourceUI();
        }

        private void chkUseLocalFolder_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateImageSourceUI();
        }

        private void btnBrowseLocalFolder_Click(object sender, RoutedEventArgs e)
        {
            // 使用统一文件夹选择对话框
            string selectedFolder = SelectFolder("选择图像文件夹", txtLocalFolderPath.Text);
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                txtLocalFolderPath.Text = selectedFolder;

                // 检查文件夹中是否有图像文件
                int imageCount = CountImageFiles(selectedFolder);
                UpdateStatus($"已在文件夹中找到 {imageCount} 个图像文件");
            }
        }

        /// <summary>
        /// 计算文件夹中图像文件的数量
        /// </summary>
        private int CountImageFiles(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return 0;

            try
            {
                int count = 0;
                count += Directory.GetFiles(folderPath, "*.jpg", SearchOption.TopDirectoryOnly).Length;
                count += Directory.GetFiles(folderPath, "*.jpeg", SearchOption.TopDirectoryOnly).Length;
                count += Directory.GetFiles(folderPath, "*.png", SearchOption.TopDirectoryOnly).Length;
                count += Directory.GetFiles(folderPath, "*.bmp", SearchOption.TopDirectoryOnly).Length;
                return count;
            }
            catch
            {
                return 0;
            }
        }
    }
}