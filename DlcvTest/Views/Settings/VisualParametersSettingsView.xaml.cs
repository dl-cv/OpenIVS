using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DlcvTest.Properties;

namespace DlcvTest
{
    public partial class VisualParametersSettingsView : UserControl
    {
        public VisualParametersSettingsView()
        {
            InitializeComponent();
            // 设置DataContext以支持绑定
            this.DataContext = Settings.Default;
            // 在 Loaded 事件中加载设置，确保在 UI 初始化完成后设置
            this.Loaded += VisualParametersSettingsView_Loaded;
        }

        private void VisualParametersSettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            // 加载设置（在 Loaded 事件中确保 UI 已完全初始化）
            RefreshSettings();
            
            // 加载预览图片
            LoadPreviewImage();
            
            // 延迟更新可视化，确保图片已加载
            Dispatcher.BeginInvoke(new Action(() => 
            {
                UpdatePreviewVisualization();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void LoadPreviewImage()
        {
            try
            {
                // 首先尝试使用资源路径
                string resourcePath = "pack://application:,,,/DlcvTest;component/Image/cat.png";
                var uri = new Uri(resourcePath, UriKind.Absolute);
                var bitmap = new BitmapImage(uri);
                bitmap.DownloadCompleted += (s, e) => UpdatePreviewVisualization();
                previewImage.Source = bitmap;
            }
            catch
            {
                try
                {
                    // 如果资源路径失败，尝试从文件系统加载
                    string imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Image", "cat.png");
                    if (System.IO.File.Exists(imagePath))
                    {
                        var bitmap = new BitmapImage(new Uri(imagePath, UriKind.Absolute));
                        bitmap.DownloadCompleted += (s, e) => UpdatePreviewVisualization();
                        previewImage.Source = bitmap;
                    }
                    else
                    {
                        // 尝试相对路径
                        string relativePath = System.IO.Path.Combine("Image", "cat.png");
                        if (System.IO.File.Exists(relativePath))
                        {
                            var bitmap = new BitmapImage(new Uri(System.IO.Path.GetFullPath(relativePath), UriKind.Absolute));
                            bitmap.DownloadCompleted += (s, e) => UpdatePreviewVisualization();
                            previewImage.Source = bitmap;
                        }
                    }
                }
                catch
                {
                    // 图片加载失败，静默处理
                }
            }
        }

        public void RefreshSettings()
        {
            // 从设置中刷新复选框的状态
            chkShowOriginalPane.IsChecked = Settings.Default.ShowOriginalPane;
            chkShowBBoxPane.IsChecked = Settings.Default.ShowBBoxPane;
            chkShowScorePane.IsChecked = Settings.Default.ShowScorePane;
            chkShowTextPane.IsChecked = Settings.Default.ShowTextPane;
            chkShowTextShadowPane.IsChecked = Settings.Default.ShowTextShadowPane;
            chkShowTextOutOfBboxPane.IsChecked = Settings.Default.ShowTextOutOfBboxPane;
            chkShowCenterPoint.IsChecked = Settings.Default.ShowCenterPoint;

            // 初始化数值显示
            txtBorderThickness.Text = Settings.Default.BBoxBorderThickness.ToString("F1");
            txtFontSize.Text = Settings.Default.FontSize.ToString("F0");
            txtFillOpacity.Text = Settings.Default.BBoxFillOpacity.ToString("F0");
            
            // 初始化填充bbox复选框
            chkFillBBox.IsChecked = Settings.Default.BBoxFillEnabled;

            // 初始化颜色显示
            try
            {
                var bboxColor = (Color)ColorConverter.ConvertFromString(Settings.Default.BBoxBorderColor);
                btnBBoxColor.Background = new SolidColorBrush(bboxColor);
                if (bBoxColorPicker != null)
                {
                    bBoxColorPicker.SelectedColor = bboxColor;
                }
            }
            catch
            {
                btnBBoxColor.Background = new SolidColorBrush(Colors.Red);
            }

            try
            {
                var fontColor = (Color)ColorConverter.ConvertFromString(Settings.Default.FontColor);
                btnFontColor.Background = new SolidColorBrush(fontColor);
                if (fontColorPicker != null)
                {
                    fontColorPicker.SelectedColor = fontColor;
                }
            }
            catch
            {
                btnFontColor.Background = new SolidColorBrush(Colors.LimeGreen);
            }

            // 刷新预览可视化
            UpdatePreviewVisualization();
        }

        private void ChkShowOriginalPane_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                // 保存设置
                Settings.Default.ShowOriginalPane = chkShowOriginalPane.IsChecked;
                Settings.Default.Save();
                
                // 通知 MainWindow 更新布局
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.UpdateImageLayout();
                }
            }
            catch (Exception ex)
            {
                // 捕获异常，防止布局更新时崩溃
                System.Diagnostics.Debug.WriteLine($"更新显示原图布局失败: {ex.Message}");
                // 静默处理，避免影响用户体验
            }
        }

        // 通用的复选框设置变更处理方法
        private void CheckBox_SettingChanged(object sender, RoutedEventArgs e)
        {
            // 支持 AnimatedCheckBox 和原生 CheckBox
            bool isChecked = false;
            string controlName = null;

            if (sender is Controls.AnimatedCheckBox animatedCheckBox)
            {
                isChecked = animatedCheckBox.IsChecked;
                controlName = animatedCheckBox.Name;
            }
            else if (sender is CheckBox checkBox)
            {
                isChecked = checkBox.IsChecked ?? false;
                controlName = checkBox.Name;
            }
            else
            {
                return; // 不支持的控件类型
            }

            if (string.IsNullOrEmpty(controlName)) return;

            // 根据 CheckBox 的 Name 设置对应的 Settings 属性
            switch (controlName)
            {
                case "chkShowBBoxPane":
                    Settings.Default.ShowBBoxPane = isChecked;
                    break;
                case "chkShowScorePane":
                    Settings.Default.ShowScorePane = isChecked;
                    break;
                case "chkShowTextPane":
                    Settings.Default.ShowTextPane = isChecked;
                    break;
                case "chkShowTextShadowPane":
                    Settings.Default.ShowTextShadowPane = isChecked;
                    break;
                case "chkShowTextOutOfBboxPane":
                    Settings.Default.ShowTextOutOfBboxPane = isChecked;
                    break;
                case "chkShowCenterPoint":
                    Settings.Default.ShowCenterPoint = isChecked;
                    break;
                case "chkFillBBox":
                    Settings.Default.BBoxFillEnabled = isChecked;
                    break;
                default:
                    return; // 未知的 CheckBox，不处理
            }

            // 保存设置并刷新图片
            Settings.Default.Save();
            
            // 更新预览可视化
            UpdatePreviewVisualization();
            
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RefreshImages();
            }
        }

        // 画布设置点击事件 - 实现互斥展开
        private void BtnCanvasSettings_Click(object sender, RoutedEventArgs e)
        {
            if (btnCanvasSettings.IsChecked == true && btnBrushSettings.IsChecked == true)
            {
                btnBrushSettings.IsChecked = false;
            }
        }

        // 画笔设置点击事件 - 实现互斥展开
        private void BtnBrushSettings_Click(object sender, RoutedEventArgs e)
        {
            if (btnBrushSettings.IsChecked == true && btnCanvasSettings.IsChecked == true)
            {
                btnCanvasSettings.IsChecked = false;
            }
        }

        // 图片加载完成事件
        private void PreviewImage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePreviewVisualization();
        }

        // 图片尺寸变化事件
        private void PreviewImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePreviewVisualization();
        }

        // 更新预览可视化
        private void UpdatePreviewVisualization()
        {
            if (previewImage == null || visualizationCanvas == null || previewImage.Source == null)
                return;

            // 获取图片的实际显示尺寸和位置
            double imageWidth = previewImage.ActualWidth;
            double imageHeight = previewImage.ActualHeight;
            
            if (imageWidth <= 0 || imageHeight <= 0)
                return;

            // 计算图片在容器中的位置（居中显示）
            double containerWidth = previewContainer.ActualWidth;
            double containerHeight = previewContainer.ActualHeight;
            
            double imageLeft = (containerWidth - imageWidth) / 2;
            double imageTop = (containerHeight - imageHeight) / 2;

            // 假设猫头在图片的 20%, 20% 位置，宽高各 30%
            double bboxX = imageWidth * 0.2 + 20;
            double bboxY = imageHeight * 0.2;
            double bboxWidth = imageWidth * 0.3 - 10;
            double bboxHeight = imageHeight * 0.3;

            // 更新 bbox 边框
            if (Settings.Default.ShowBBoxPane)
            {
                bboxBorder.Visibility = Visibility.Visible;
                
                // 设置边框颜色
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(Settings.Default.BBoxBorderColor);
                    bboxBorder.BorderBrush = new SolidColorBrush(color);
                }
                catch
                {
                    bboxBorder.BorderBrush = Brushes.Red; // 默认红色
                }
                
                // 设置边框粗细
                bboxBorder.BorderThickness = new Thickness(Settings.Default.BBoxBorderThickness);
                
                // 设置填充（使用bbox框线颜色，透明度由BBoxFillOpacity控制）
                if (Settings.Default.BBoxFillEnabled)
                {
                    try
                    {
                        // 解析bbox框线颜色
                        var borderColor = (Color)ColorConverter.ConvertFromString(Settings.Default.BBoxBorderColor);
                        
                        // 根据BBoxFillOpacity计算alpha值（0-100映射到0-255）
                        double opacity = Math.Max(0.0, Math.Min(100.0, Settings.Default.BBoxFillOpacity));
                        byte alpha = (byte)(opacity / 100.0 * 255);
                        
                        // 使用框线颜色的RGB，但使用计算出的alpha值
                        var fillColor = Color.FromArgb(alpha, borderColor.R, borderColor.G, borderColor.B);
                        bboxBorder.Background = new SolidColorBrush(fillColor);
                    }
                    catch
                    {
                        // 默认半透明红色（50%透明度）
                        bboxBorder.Background = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0x00, 0x00));
                    }
                }
                else
                {
                    bboxBorder.Background = Brushes.Transparent;
                }
                
                // 设置位置和大小
                Canvas.SetLeft(bboxBorder, imageLeft + bboxX);
                Canvas.SetTop(bboxBorder, imageTop + bboxY);
                bboxBorder.Width = bboxWidth;
                bboxBorder.Height = bboxHeight;
            }
            else
            {
                bboxBorder.Visibility = Visibility.Collapsed;
            }

            // 更新标签文字和分数显示
            if (Settings.Default.ShowTextPane)
            {
                // 设置字体大小
                labelText.FontSize = Settings.Default.FontSize;
                scoreText.FontSize = Settings.Default.FontSize;

                labelBorder.Visibility = Visibility.Visible;
                
                // 分数显示在文字后面，只有显示文字时才显示分数
                if (Settings.Default.ShowScorePane)
                {
                    scoreText.Visibility = Visibility.Visible;
                }
                else
                {
                    scoreText.Visibility = Visibility.Collapsed;
                }
                
                // 应用文字阴影效果（文字背景是阴影色）
                if (Settings.Default.ShowTextShadowPane)
                {
                    // 设置背景为阴影色（深灰色）
                    labelBorder.Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)); // 深灰色背景
                    labelText.Foreground = Brushes.White; // 白色文字
                    scoreText.Foreground = Brushes.White; // 白色文字
                }
                else
                {
                    // 正常背景（白色）
                    labelBorder.Background = Brushes.White;
                    // 使用设置的字体颜色
                    try
                    {
                        var fontColor = (Color)ColorConverter.ConvertFromString(Settings.Default.FontColor);
                        labelText.Foreground = new SolidColorBrush(fontColor);
                        scoreText.Foreground = new SolidColorBrush(fontColor);
                    }
                    catch
                    {
                        labelText.Foreground = Brushes.Black; // 默认黑色文字
                        scoreText.Foreground = Brushes.Black; // 默认黑色文字
                    }
                }
                
                // 根据"标签写在框外"设置调整位置
                if (Settings.Default.ShowTextOutOfBboxPane)
                {
                    // 框外：显示在 bbox 上方
                    Canvas.SetLeft(labelBorder, imageLeft + bboxX);
                    Canvas.SetTop(labelBorder, imageTop + bboxY - labelBorder.ActualHeight - 5);
                }
                else
                {
                    // 框内：显示在 bbox 左上角内
                    Canvas.SetLeft(labelBorder, imageLeft + bboxX + 3);
                    Canvas.SetTop(labelBorder, imageTop + bboxY + 3);
                }
            }
            else
            {
                // 不显示文字时，也不显示分�?                labelBorder.Visibility = Visibility.Collapsed;
                scoreText.Visibility = Visibility.Collapsed;
            }

            // 绘制中心点十字
            if (Settings.Default.ShowCenterPoint)
            {
                // 计算中心点坐标（相对于图片）
                double centerX = bboxX + bboxWidth / 2;
                double centerY = bboxY + bboxHeight / 2;

                // 十字线长度为bbox较小边的30%
                double crossSize = Math.Min(bboxWidth, bboxHeight) * 0.3;

                // 清除之前的十字线
                var existingCrossLines = visualizationCanvas.Children.OfType<System.Windows.Shapes.Line>()
                    .Where(line => line.Tag != null && line.Tag.ToString() == "CenterCross").ToList();
                foreach (var line in existingCrossLines)
                {
                    visualizationCanvas.Children.Remove(line);
                }

                // 创建水平线
                var horizontalLine = new System.Windows.Shapes.Line
                {
                    X1 = imageLeft + centerX - crossSize / 2,
                    Y1 = imageTop + centerY,
                    X2 = imageLeft + centerX + crossSize / 2,
                    Y2 = imageTop + centerY,
                    Stroke = Brushes.Red,
                    StrokeThickness = 2,
                    Tag = "CenterCross"
                };

                // 创建垂直线
                var verticalLine = new System.Windows.Shapes.Line
                {
                    X1 = imageLeft + centerX,
                    Y1 = imageTop + centerY - crossSize / 2,
                    X2 = imageLeft + centerX,
                    Y2 = imageTop + centerY + crossSize / 2,
                    Stroke = Brushes.Red,
                    StrokeThickness = 2,
                    Tag = "CenterCross"
                };

                // 添加到画�?                visualizationCanvas.Children.Add(horizontalLine);
                visualizationCanvas.Children.Add(verticalLine);
            }
            else
            {
                // 不显示十字时，清除现有的十字线
                var existingCrossLines = visualizationCanvas.Children.OfType<System.Windows.Shapes.Line>()
                    .Where(line => line.Tag != null && line.Tag.ToString() == "CenterCross").ToList();
                foreach (var line in existingCrossLines)
                {
                    visualizationCanvas.Children.Remove(line);
                }
            }
        }

        // BBox框线宽度步进
        private void BtnBorderThicknessStepper_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tagStr && double.TryParse(tagStr, out double step))
            {
                double currentValue = Settings.Default.BBoxBorderThickness;
                double newValue = Math.Max(0.5, Math.Min(10.0, currentValue + step));
                Settings.Default.BBoxBorderThickness = newValue;
                Settings.Default.Save();
                txtBorderThickness.Text = newValue.ToString("F1");
                UpdatePreviewVisualization();

                // 通知主界面重新绘制图像
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshImages();
                }
            }
        }

        // 字体大小步进
        private void BtnFontSizeStepper_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tagStr && double.TryParse(tagStr, out double step))
            {
                double currentValue = Settings.Default.FontSize;
                double newValue = Math.Max(6.0, Math.Min(48.0, currentValue + step));
                Settings.Default.FontSize = newValue;
                Settings.Default.Save();
                txtFontSize.Text = newValue.ToString("F0");
                UpdatePreviewVisualization();

                // 通知主界面重新绘制图像
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshImages();
                }
            }
        }

        // 框线宽度文本框失去焦点
        private void TxtBorderThickness_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(txtBorderThickness.Text, out double value))
            {
                Settings.Default.BBoxBorderThickness = Math.Max(0.5, Math.Min(10.0, value));
                Settings.Default.Save();
                UpdatePreviewVisualization();

                // 通知主界面重新绘制图像
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshImages();
                }
            }
            else
            {
                txtBorderThickness.Text = Settings.Default.BBoxBorderThickness.ToString("F1");
            }
        }

        // 字体大小文本框失去焦点
        private void TxtFontSize_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(txtFontSize.Text, out double value))
            {
                Settings.Default.FontSize = Math.Max(6.0, Math.Min(48.0, value));
                Settings.Default.Save();
                UpdatePreviewVisualization();

                // 通知主界面重新绘制图像
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshImages();
                }
            }
            else
            {
                txtFontSize.Text = Settings.Default.FontSize.ToString("F0");
            }
        }

        // 填充透明度步进器
        private void BtnFillOpacityStepper_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tagStr && double.TryParse(tagStr, out double step))
            {
                double currentValue = Settings.Default.BBoxFillOpacity;
                double newValue = Math.Max(0.0, Math.Min(100.0, currentValue + step));
                Settings.Default.BBoxFillOpacity = newValue;
                Settings.Default.Save();
                txtFillOpacity.Text = newValue.ToString("F0");
                UpdatePreviewVisualization();

                // 通知主界面重新绘制图像
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshImages();
                }
            }
        }

        // 填充透明度文本框失去焦点
        private void TxtFillOpacity_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(txtFillOpacity.Text, out double value))
            {
                Settings.Default.BBoxFillOpacity = Math.Max(0.0, Math.Min(100.0, value));
                Settings.Default.Save();
                UpdatePreviewVisualization();

                // 通知主界面重新绘制图像
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshImages();
                }
            }
            else
            {
                txtFillOpacity.Text = Settings.Default.BBoxFillOpacity.ToString("F0");
            }
        }

        // bbox框线颜色按钮点击
        private void BtnBBoxColor_Click(object sender, RoutedEventArgs e)
        {
            ToggleColorPicker(bBoxColorPickerContainer, fontColorPickerContainer);
        }

        // 字体颜色按钮点击
        private void BtnFontColor_Click(object sender, RoutedEventArgs e)
        {
            ToggleColorPicker(fontColorPickerContainer, bBoxColorPickerContainer);
        }

        // 切换颜色选择器展开/收起状态
        private void ToggleColorPicker(Grid targetContainer, Grid otherContainer)
        {
            // 收起其他颜色选择
            if (otherContainer.Height > 0)
            {
                otherContainer.Height = 0;
            }

            // 切换目标颜色选择
            if (targetContainer.Height > 0)
            {
                targetContainer.Height = 0;
            }
            else
            {
                targetContainer.Height = 240;
            }
        }


        // bbox颜色选择器颜色改变
        private void BBoxColorPicker_ColorChanged(object sender, Color color)
        {
            btnBBoxColor.Background = new SolidColorBrush(color);
            string hexColor = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            Settings.Default.BBoxBorderColor = hexColor;
            Settings.Default.Save();
            UpdatePreviewVisualization();

            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RefreshImages();
            }
        }

        // bbox颜色选择器确认
        private void BBoxColorPicker_ColorConfirmed(object sender, Color color)
        {
            BBoxColorPicker_ColorChanged(sender, color);
            bBoxColorPickerContainer.Height = 0;
        }

        // bbox颜色选择器清除
        private void BBoxColorPicker_ColorCleared(object sender, EventArgs e)
        {
            // 恢复默认颜色
            Color defaultColor = Colors.Red;
            btnBBoxColor.Background = new SolidColorBrush(defaultColor);
            bBoxColorPicker.SelectedColor = defaultColor;
            Settings.Default.BBoxBorderColor = "#FF0000";
            Settings.Default.Save();
            UpdatePreviewVisualization();

            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RefreshImages();
            }
        }

        // 字体颜色选择器颜色改变
        private void FontColorPicker_ColorChanged(object sender, Color color)
        {
            btnFontColor.Background = new SolidColorBrush(color);
            string hexColor = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            Settings.Default.FontColor = hexColor;
            Settings.Default.Save();
            UpdatePreviewVisualization();

            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RefreshImages();
            }
        }

        // 字体颜色选择器确认
        private void FontColorPicker_ColorConfirmed(object sender, Color color)
        {
            FontColorPicker_ColorChanged(sender, color);
            fontColorPickerContainer.Height = 0;
        }

        // 字体颜色选择器清除
        private void FontColorPicker_ColorCleared(object sender, EventArgs e)
        {
            // 恢复默认颜色
            Color defaultColor = Colors.LimeGreen;
            btnFontColor.Background = new SolidColorBrush(defaultColor);
            fontColorPicker.SelectedColor = defaultColor;
            Settings.Default.FontColor = "#FF00FF00";
            Settings.Default.Save();
            UpdatePreviewVisualization();

            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RefreshImages();
            }
        }
    }
}
