using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenIVS2.Models;

namespace OpenIVS2
{
    public partial class SettingsWindow : Window
    {
        public AppSettings WorkingSettings { get; private set; }

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            WorkingSettings = (settings ?? AppSettings.CreateDefault()).Clone();
            CameraItemsControl.ItemsSource = WorkingSettings.Cameras;
            LoadGlobalFields();
            UpdatePlcUi();
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
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择检测图片保存目录";
                dialog.SelectedPath = Directory.Exists(SaveDirectoryBox.Text) ? SaveDirectoryBox.Text : "";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    SaveDirectoryBox.Text = dialog.SelectedPath;
            }
        }

        private void UsePlcCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePlcUi();
        }

        private void PlcModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePlcUi();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WorkingSettings.UsePlc = UsePlcCheck.IsChecked == true;
                WorkingSettings.PlcMode = SelectedText(PlcModeCombo, "mock");
                WorkingSettings.PortName = PortNameBox.Text.Trim();
                WorkingSettings.BaudRate = ParseInt(BaudRateBox.Text, "波特率", 1, int.MaxValue);
                WorkingSettings.DataBits = ParseInt(DataBitsBox.Text, "数据位", 5, 8);
                WorkingSettings.DeviceId = ParseInt(DeviceIdBox.Text, "设备 ID", 0, 255);
                WorkingSettings.StopBits = SelectedText(StopBitsCombo, "One");
                WorkingSettings.Parity = SelectedText(ParityCombo, "None");
                WorkingSettings.PhotoRegister = ParseUShort(PhotoRegisterBox.Text, "拍照寄存器");
                WorkingSettings.TriggerValue = ParseUShort(TriggerValueBox.Text, "触发值");
                WorkingSettings.ClearValue = ParseUShort(ClearValueBox.Text, "清零值");
                WorkingSettings.PollIntervalMs = ParseInt(PollIntervalBox.Text, "轮询间隔", 20, 60000);
                WorkingSettings.SaveDirectory = SaveDirectoryBox.Text.Trim();
                WorkingSettings.SaveOkImages = SaveOkCheck.IsChecked == true;
                WorkingSettings.SaveNgImages = SaveNgCheck.IsChecked == true;
                WorkingSettings.ImageFormat = SelectedText(ImageFormatCombo, "PNG");
                WorkingSettings.JpegQuality = ParseInt(JpegQualityBox.Text, "JPEG 质量", 1, 100);
                WorkingSettings.Normalize();
                ValidateCameraSettings();
                if (string.IsNullOrWhiteSpace(WorkingSettings.SaveDirectory))
                    throw new InvalidOperationException("图片保存目录不能为空");
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
            PortNameBox.Text = WorkingSettings.PortName;
            BaudRateBox.Text = WorkingSettings.BaudRate.ToString();
            DataBitsBox.Text = WorkingSettings.DataBits.ToString();
            DeviceIdBox.Text = WorkingSettings.DeviceId.ToString();
            SelectByText(StopBitsCombo, WorkingSettings.StopBits);
            SelectByText(ParityCombo, WorkingSettings.Parity);
            PhotoRegisterBox.Text = WorkingSettings.PhotoRegister.ToString();
            TriggerValueBox.Text = WorkingSettings.TriggerValue.ToString();
            ClearValueBox.Text = WorkingSettings.ClearValue.ToString();
            PollIntervalBox.Text = WorkingSettings.PollIntervalMs.ToString();
            SaveDirectoryBox.Text = WorkingSettings.SaveDirectory;
            SaveOkCheck.IsChecked = WorkingSettings.SaveOkImages;
            SaveNgCheck.IsChecked = WorkingSettings.SaveNgImages;
            SelectByText(ImageFormatCombo, WorkingSettings.ImageFormat);
            JpegQualityBox.Text = WorkingSettings.JpegQuality.ToString();
        }

        private void UpdatePlcUi()
        {
            if (SerialSettingsPanel == null || PlcModeCombo == null || UsePlcCheck == null) return;
            var serialEnabled = UsePlcCheck.IsChecked == true &&
                string.Equals(SelectedText(PlcModeCombo, "mock"), "serial", StringComparison.OrdinalIgnoreCase);
            SerialSettingsPanel.IsEnabled = serialEnabled;
            DeviceIdBox.IsEnabled = UsePlcCheck.IsChecked == true;
            PlcModeCombo.IsEnabled = UsePlcCheck.IsChecked == true;
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

        private static int ParseInt(string text, string name, int min, int max)
        {
            int value;
            if (!int.TryParse(text, out value) || value < min || value > max)
                throw new InvalidOperationException(name + "必须在 " + min + " 到 " + max + " 之间");
            return value;
        }

        private static ushort ParseUShort(string text, string name)
        {
            ushort value;
            if (!ushort.TryParse(text, out value))
                throw new InvalidOperationException(name + "必须是 0 到 65535 的整数");
            return value;
        }
    }
}
