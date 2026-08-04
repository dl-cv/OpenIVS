using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DLCV.Camera;
using Microsoft.Win32;
using OpenIVS2.Models;

namespace OpenIVS2
{
    public partial class SettingsWindow : Window
    {
        public sealed class CameraDeviceOption
        {
            public string SerialNumber { get; set; }
            public string DisplayName { get; set; }
        }

        private string _lastPlcMode;
        private bool _cameraDevicesLoaded;
        private bool _runtimeActionInProgress;
        private readonly Func<Task> _startSystem;
        private readonly Func<Task> _stopSystem;
        private readonly Func<Task> _triggerCycle;
        private readonly Func<bool> _isSystemRunning;
        private readonly Func<bool> _canManualTrigger;

        public AppSettings WorkingSettings { get; private set; }
        public ObservableCollection<CameraDeviceOption> AvailableCameras { get; private set; }

        public SettingsWindow(AppSettings settings)
            : this(settings, null, null, null, null, null)
        {
        }

        public SettingsWindow(
            AppSettings settings,
            Func<Task> startSystem,
            Func<Task> stopSystem,
            Func<Task> triggerCycle,
            Func<bool> isSystemRunning,
            Func<bool> canManualTrigger)
        {
            _startSystem = startSystem;
            _stopSystem = stopSystem;
            _triggerCycle = triggerCycle;
            _isSystemRunning = isSystemRunning;
            _canManualTrigger = canManualTrigger;
            AvailableCameras = new ObservableCollection<CameraDeviceOption>();
            InitializeComponent();
            WorkingSettings = (settings ?? AppSettings.CreateDefault()).Clone();
            CameraItemsControl.ItemsSource = WorkingSettings.Cameras;
            LoadGlobalFields();
            UpdatePlcUi();
            RefreshRuntimeControls();
        }

        private void CameraMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            var selectedItem = comboBox != null ? comboBox.SelectedItem as ComboBoxItem : null;
            var mode = selectedItem != null && selectedItem.Tag != null ? selectedItem.Tag.ToString() : "";
            if (string.Equals(mode, "hik", StringComparison.OrdinalIgnoreCase))
                LoadCameraDevices();
        }

        private void LoadCameraDevices()
        {
            if (_cameraDevicesLoaded) return;
            _cameraDevicesLoaded = true;
            AvailableCameras.Clear();
            try
            {
                foreach (var device in CameraUtils.EnumerateDevices())
                {
                    var name = string.IsNullOrWhiteSpace(device.UserId) ? device.ModelName : device.UserId;
                    AvailableCameras.Add(new CameraDeviceOption
                    {
                        SerialNumber = device.SerialNumber,
                        DisplayName = name + " (" + device.SerialNumber + ")"
                    });
                }
            }
            catch (Exception ex)
            {
                AvailableCameras.Add(new CameraDeviceOption
                {
                    SerialNumber = "",
                    DisplayName = "相机枚举失败: " + ex.Message
                });
            }

            foreach (var cameraId in WorkingSettings.Cameras
                .Where(x => !string.IsNullOrWhiteSpace(x.CameraId))
                .Select(x => x.CameraId)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (AvailableCameras.Any(x => string.Equals(x.SerialNumber, cameraId, StringComparison.OrdinalIgnoreCase)))
                    continue;
                AvailableCameras.Add(new CameraDeviceOption
                {
                    SerialNumber = cameraId,
                    DisplayName = "已配置但未连接 (" + cameraId + ")"
                });
            }

            if (AvailableCameras.Count == 0)
            {
                AvailableCameras.Add(new CameraDeviceOption
                {
                    SerialNumber = "",
                    DisplayName = "未发现可用真实相机"
                });
            }
        }

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var camera = (sender as FrameworkElement)?.DataContext as CameraSettings;
            if (camera == null) return;
            var dialog = new OpenFileDialog
            {
                Title = "选择 " + camera.Name + " 的虚拟图片",
                Filter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件|*.*",
                FileName = camera.VirtualImagePath
            };
            if (dialog.ShowDialog(this) == true)
            {
                camera.VirtualImagePath = dialog.FileName;
                CameraItemsControl.Items.Refresh();
            }
        }

        private void BrowseModel_Click(object sender, RoutedEventArgs e)
        {
            var camera = (sender as FrameworkElement)?.DataContext as CameraSettings;
            if (camera == null) return;
            var dialog = new OpenFileDialog
            {
                Title = "选择 " + camera.Name + " 的模型",
                Filter = "DLCV 模型|*.dvt;*.dvo;*.dvst;*.dvso;*.dvsp|所有文件|*.*",
                FileName = camera.ModelPath
            };
            if (dialog.ShowDialog(this) == true)
            {
                camera.ModelPath = dialog.FileName;
                CameraItemsControl.Items.Refresh();
            }
        }

        private void BrowseSaveDirectory_Click(object sender, RoutedEventArgs e)
        {
            BrowseDirectory(SaveDirectoryBox, "选择检测图片保存目录");
        }

        private void BrowseRuntimeLogDirectory_Click(object sender, RoutedEventArgs e)
        {
            BrowseDirectory(RuntimeLogDirectoryBox, "选择运行时日志目录");
        }

        private void BrowseProductionLogDirectory_Click(object sender, RoutedEventArgs e)
        {
            BrowseDirectory(ProductionLogDirectoryBox, "选择生产 CSV 日志目录");
        }

        private void BrowseDirectory(TextBox target, string description)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = description;
                dialog.SelectedPath = Directory.Exists(target.Text) ? target.Text : "";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    target.Text = dialog.SelectedPath;
            }
        }

        private void UsePlcCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePlcUi();
        }

        private void PlcModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var mode = SelectedText(PlcModeCombo, "mock");
            if (string.Equals(mode, "tcp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_lastPlcMode, "tcp", StringComparison.OrdinalIgnoreCase) &&
                WorkingSettings != null &&
                WorkingSettings.PhotoRegister == 500 &&
                WorkingSettings.TriggerValue == 1 &&
                WorkingSettings.ClearValue == 0)
            {
                WorkingSettings.PhotoRegister = 4111;
            }
            _lastPlcMode = mode;
            UpdatePlcUi();
        }

        private async void StartRun_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteRuntimeActionAsync(_startSystem, "启动失败");
        }

        private async void StopRun_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteRuntimeActionAsync(_stopSystem, "停止失败");
        }

        private async void ManualTrigger_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteRuntimeActionAsync(_triggerCycle, "单次触发失败");
        }

        private async Task ExecuteRuntimeActionAsync(Func<Task> action, string errorTitle)
        {
            if (action == null || _runtimeActionInProgress) return;
            _runtimeActionInProgress = true;
            RefreshRuntimeControls();
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _runtimeActionInProgress = false;
                RefreshRuntimeControls();
            }
        }

        private void RefreshRuntimeControls()
        {
            if (StartRunButton == null || StopRunButton == null || ManualTriggerButton == null || RuntimeStatusText == null) return;
            var running = _isSystemRunning != null && _isSystemRunning();
            StartRunButton.IsEnabled = !_runtimeActionInProgress && !running && _startSystem != null;
            StopRunButton.IsEnabled = !_runtimeActionInProgress && running && _stopSystem != null;
            ManualTriggerButton.IsEnabled = !_runtimeActionInProgress && running && _triggerCycle != null &&
                (_canManualTrigger == null || _canManualTrigger());
            SaveSettingsButton.IsEnabled = !_runtimeActionInProgress;
            CancelSettingsButton.IsEnabled = !_runtimeActionInProgress;
            RuntimeStatusText.Text = running ? "当前状态：运行中" : "当前状态：已停止";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WorkingSettings.UsePlc = UsePlcCheck.IsChecked == true;
                WorkingSettings.PlcMode = SelectedText(PlcModeCombo, "mock");
                WorkingSettings.TcpHost = TcpHostBox.Text.Trim();
                WorkingSettings.TcpPort = ParseInt(TcpPortBox.Text, "TCP 端口", 1, 65535);
                WorkingSettings.PortName = PortNameBox.Text.Trim();
                WorkingSettings.BaudRate = ParseInt(BaudRateBox.Text, "波特率", 1, int.MaxValue);
                WorkingSettings.DataBits = ParseInt(DataBitsBox.Text, "数据位", 5, 8);
                WorkingSettings.DeviceId = ParseInt(DeviceIdBox.Text, "设备 ID", 0, 255);
                WorkingSettings.StopBits = SelectedText(StopBitsCombo, "One");
                WorkingSettings.Parity = SelectedText(ParityCombo, "None");
                WorkingSettings.SaveDirectory = SaveDirectoryBox.Text.Trim();
                WorkingSettings.SaveOkImages = SaveOkCheck.IsChecked == true;
                WorkingSettings.SaveNgImages = SaveNgCheck.IsChecked == true;
                WorkingSettings.ImageFormat = SelectedText(ImageFormatCombo, "PNG");
                WorkingSettings.JpegQuality = ParseInt(JpegQualityBox.Text, "JPEG 质量", 1, 100);
                WorkingSettings.SaveVisualizationImages = SaveVisualizationCheck.IsChecked == true;
                WorkingSettings.VisualizationImageFormat = SelectedText(VisualizationFormatCombo, "JPG");
                WorkingSettings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
                WorkingSettings.EnableRuntimeLog = EnableRuntimeLogCheck.IsChecked == true;
                WorkingSettings.RuntimeLogDirectory = RuntimeLogDirectoryBox.Text.Trim();
                WorkingSettings.RuntimeLogMaxFileSizeMB = ParseInt(RuntimeLogMaxSizeBox.Text, "运行日志单文件上限", 1, 1024);
                WorkingSettings.RuntimeLogMaxFileCount = ParseInt(RuntimeLogMaxCountBox.Text, "运行日志保留文件数", 1, 365);
                WorkingSettings.EnableProductionLog = EnableProductionLogCheck.IsChecked == true;
                WorkingSettings.ProductionLogDirectory = ProductionLogDirectoryBox.Text.Trim();
                WorkingSettings.Normalize();
                ValidateCameraSettings();
                if (WorkingSettings.UsePlc &&
                    string.Equals(WorkingSettings.PlcMode, "tcp", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(WorkingSettings.TcpHost))
                    throw new InvalidOperationException("TCP 地址不能为空");
                if (string.IsNullOrWhiteSpace(WorkingSettings.SaveDirectory))
                    throw new InvalidOperationException("图片保存目录不能为空");
                if (WorkingSettings.EnableRuntimeLog && string.IsNullOrWhiteSpace(WorkingSettings.RuntimeLogDirectory))
                    throw new InvalidOperationException("运行时日志目录不能为空");
                if (WorkingSettings.EnableProductionLog && string.IsNullOrWhiteSpace(WorkingSettings.ProductionLogDirectory))
                    throw new InvalidOperationException("生产日志目录不能为空");
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "设置校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        internal void SelectTabForAcceptance(int index)
        {
            SettingsTabs.SelectedIndex = index;
            UpdateLayout();
        }

        internal void SetPlcModeForAcceptance(string mode)
        {
            UsePlcCheck.IsChecked = true;
            SelectByText(PlcModeCombo, mode);
            UpdatePlcUi();
            UpdateLayout();
        }

        internal bool StartWithWindowsSelected { get { return StartWithWindowsCheck.IsChecked == true; } }
        internal bool RuntimeLogSelected { get { return EnableRuntimeLogCheck.IsChecked == true; } }
        internal bool ProductionLogSelected { get { return EnableProductionLogCheck.IsChecked == true; } }
        internal bool VisualizationSaveSelected { get { return SaveVisualizationCheck.IsChecked == true; } }
        internal string VisualizationSaveLabelForAcceptance { get { return SaveVisualizationCheck.Content as string; } }
        internal bool RuntimeControlsAvailable
        {
            get { return StartRunButton != null && StopRunButton != null && ManualTriggerButton != null; }
        }

        internal Task StartRuntimeForAcceptanceAsync()
        {
            return ExecuteRuntimeActionAsync(_startSystem, "启动失败");
        }

        internal Task StopRuntimeForAcceptanceAsync()
        {
            return ExecuteRuntimeActionAsync(_stopSystem, "停止失败");
        }

        internal Task TriggerRuntimeForAcceptanceAsync()
        {
            return ExecuteRuntimeActionAsync(_triggerCycle, "单次触发失败");
        }

        internal bool ContainsVisibleTextForAcceptance(string text)
        {
            return ContainsText(this, text);
        }

        internal void CaptureScreenshot(string path)
        {
            UpdateLayout();
            var width = Math.Max(1, (int)ActualWidth);
            var height = Math.Max(1, (int)ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            using (var stream = File.Create(path))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }
        }

        private void LoadGlobalFields()
        {
            UsePlcCheck.IsChecked = WorkingSettings.UsePlc;
            SelectByText(PlcModeCombo, WorkingSettings.PlcMode);
            TcpHostBox.Text = WorkingSettings.TcpHost;
            TcpPortBox.Text = WorkingSettings.TcpPort.ToString();
            PortNameBox.Text = WorkingSettings.PortName;
            BaudRateBox.Text = WorkingSettings.BaudRate.ToString();
            DataBitsBox.Text = WorkingSettings.DataBits.ToString();
            DeviceIdBox.Text = WorkingSettings.DeviceId.ToString();
            SelectByText(StopBitsCombo, WorkingSettings.StopBits);
            SelectByText(ParityCombo, WorkingSettings.Parity);
            SaveDirectoryBox.Text = WorkingSettings.SaveDirectory;
            SaveOkCheck.IsChecked = WorkingSettings.SaveOkImages;
            SaveNgCheck.IsChecked = WorkingSettings.SaveNgImages;
            SelectByText(ImageFormatCombo, WorkingSettings.ImageFormat);
            JpegQualityBox.Text = WorkingSettings.JpegQuality.ToString();
            SaveVisualizationCheck.IsChecked = WorkingSettings.SaveVisualizationImages;
            SelectByText(VisualizationFormatCombo, WorkingSettings.VisualizationImageFormat);
            StartWithWindowsCheck.IsChecked = WorkingSettings.StartWithWindows;
            EnableRuntimeLogCheck.IsChecked = WorkingSettings.EnableRuntimeLog;
            RuntimeLogDirectoryBox.Text = WorkingSettings.RuntimeLogDirectory;
            RuntimeLogMaxSizeBox.Text = WorkingSettings.RuntimeLogMaxFileSizeMB.ToString();
            RuntimeLogMaxCountBox.Text = WorkingSettings.RuntimeLogMaxFileCount.ToString();
            EnableProductionLogCheck.IsChecked = WorkingSettings.EnableProductionLog;
            ProductionLogDirectoryBox.Text = WorkingSettings.ProductionLogDirectory;
        }

        private void UpdatePlcUi()
        {
            if (SerialSettingsPanel == null || TcpSettingsPanel == null || PlcModeCombo == null || UsePlcCheck == null) return;
            var plcEnabled = UsePlcCheck.IsChecked == true;
            var mode = SelectedText(PlcModeCombo, "mock");
            var tcpSelected = string.Equals(mode, "tcp", StringComparison.OrdinalIgnoreCase);
            var serialEnabled = plcEnabled && string.Equals(mode, "serial", StringComparison.OrdinalIgnoreCase);
            SerialSettingsPanel.Visibility = tcpSelected ? Visibility.Collapsed : Visibility.Visible;
            SerialSettingsPanel.IsEnabled = serialEnabled;
            TcpSettingsPanel.Visibility = tcpSelected ? Visibility.Visible : Visibility.Collapsed;
            TcpSettingsPanel.IsEnabled = plcEnabled && tcpSelected;
            DeviceIdBox.IsEnabled = plcEnabled;
            PlcModeCombo.IsEnabled = plcEnabled;
        }

        private void ValidateCameraSettings()
        {
            var enabled = WorkingSettings.EnabledCameras();
            if (enabled.Count < 1 || enabled.Count > 6)
                throw new InvalidOperationException("请启用 1 到 6 台相机");
            foreach (var camera in enabled)
            {
                if (string.IsNullOrWhiteSpace(camera.Name))
                    throw new InvalidOperationException("相机 " + camera.Slot + " 的显示名称不能为空");
                if (camera.FrameTimeoutMs <= 0)
                    throw new InvalidOperationException(camera.Name + " 的取图超时必须大于 0");
                if (string.Equals(camera.Mode, "virtual", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(camera.VirtualImagePath))
                    throw new InvalidOperationException(camera.Name + " 尚未选择虚拟图片");
                if (string.Equals(camera.Mode, "hik", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(camera.CameraId))
                    throw new InvalidOperationException(camera.Name + " 尚未选择真实相机");
                if (string.IsNullOrWhiteSpace(camera.ModelPath))
                    throw new InvalidOperationException(camera.Name + " 尚未选择模型");
            }
        }

        private static void SelectByText(ComboBox comboBox, string value)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (string.Equals(item.Content != null ? item.Content.ToString() : "", value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
            comboBox.SelectedIndex = 0;
        }

        private static string SelectedText(ComboBox comboBox, string fallback)
        {
            var item = comboBox.SelectedItem as ComboBoxItem;
            return item != null && item.Content != null ? item.Content.ToString() : fallback;
        }

        private static bool ContainsText(DependencyObject parent, string text)
        {
            var textBlock = parent as TextBlock;
            if (textBlock != null && string.Equals(textBlock.Text, text, StringComparison.Ordinal)) return true;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                if (ContainsText(VisualTreeHelper.GetChild(parent, i), text)) return true;
            }
            return false;
        }

        private static int ParseInt(string text, string name, int min, int max)
        {
            int value;
            if (!int.TryParse(text, out value) || value < min || value > max)
                throw new InvalidOperationException(name + "必须在 " + min + " 到 " + max + " 之间");
            return value;
        }

    }
}
