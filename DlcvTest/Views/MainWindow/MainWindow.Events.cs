using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using DlcvTest.Properties;
using Ookii.Dialogs.Wpf;

namespace DlcvTest
{
    public partial class MainWindow
    {
        // 当前打开的设置窗口引用，用于点击遮罩层时关闭
        private SettingsWindow _currentSettingsWindow = null;
        // 步进器按钮点击事件（用于调整置信度和IOU阈值）
        private void Stepper0_1Button_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            // 找到对应的TextBox（通过按钮的父容器）
            var grid = btn.Parent as Grid;
            if (grid == null) return;

            TextBox textBox = null;
            foreach (var child in grid.Children)
            {
                if (child is TextBox tb)
                {
                    textBox = tb;
                    break;
                }
            }

            if (textBox == null) return;

            // 解析当前值
            if (double.TryParse(textBox.Text, out double value))
            {
                if (btn.Content.ToString() == "+")
                {
                    value += 0.1;
                    if (value > 1.0) value = 1.0;
                }
                else if (btn.Content.ToString() == "—" || btn.Content.ToString() == "-")
                {
                    value -= 0.1;
                    if (value < 0.0) value = 0.0;
                }
                textBox.Text = value.ToString("0.00");
            }
        }

        // 整数步进器按钮点击事件（用于调整 top_k）
        private void StepperIntButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            // 找到对应的TextBox（通过按钮的父容器）
            var grid = btn.Parent as Grid;
            if (grid == null) return;

            TextBox textBox = null;
            foreach (var child in grid.Children)
            {
                if (child is TextBox tb)
                {
                    textBox = tb;
                    break;
                }
            }

            if (textBox == null) return;

            // 解析当前值
            if (int.TryParse(textBox.Text, out int value))
            {
                if (btn.Content.ToString() == "+")
                {
                    value += 1;
                }
                else if (btn.Content.ToString() == "—" || btn.Content.ToString() == "-")
                {
                    value = Math.Max(1, value - 1);
                }
                textBox.Text = value.ToString();
            }
        }

        // 窗口控制按钮左键拖拽事件
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        // 窗口最小化
        private void btnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        // 窗口最大化
        private void btnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        // 窗口关闭
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        //选择模型按钮
        private async void btnSelectModel_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog()
            {
                Title = "选择模型",
                Filter = "深度视觉模型 (*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp)|*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            try
            {
                var last = Properties.Settings.Default.LastModelPath;
                if (!string.IsNullOrEmpty(last))
                {
                    string dir = Path.GetDirectoryName(last);
                    if (Directory.Exists(dir))
                    {
                        openFileDialog.InitialDirectory = dir;
                        openFileDialog.FileName = Path.GetFileName(last);
                    }
                }
            }
            catch { }

            bool? ok = openFileDialog.ShowDialog();
            if (ok != true) return;

            string selectedModelPath = openFileDialog.FileName;
            await LoadModelAsync(selectedModelPath, showSuccessPopup: true);
        }

        private async void cmbModelName_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingModelCombo) return;
            if (cmbModelName == null) return;

            if (!(cmbModelName.SelectedItem is RecentModelItem item)) return;
            if (item.IsPlaceholder || string.IsNullOrWhiteSpace(item.ModelPath)) return;

            // 已加载同一路径则不重复加载
            if (!string.IsNullOrWhiteSpace(_loadedModelPath) && PathEquals(_loadedModelPath, item.ModelPath))
                return;

            await LoadModelAsync(item.ModelPath, showSuccessPopup: false);
        }

        // 选择文件夹按钮
        private void btnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "选择图片文件夹",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() == true)
            {
                string selectedPath = dialog.SelectedPath;
                txtDataPath.Text = selectedPath;
                LoadFolderTree(selectedPath);

                // 保存路径到设置中
                Properties.Settings.Default.SavedDataPath = selectedPath;
                Properties.Settings.Default.Save();
            }
        }

        // 设置工具栏按钮
        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsWindow();
        }

        private void btnVisualParams_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsWindow(openVisualParams: true);
        }

        /// <summary>
        /// 显示设置窗口（非模态），支持点击遮罩层关闭
        /// </summary>
        private void ShowSettingsWindow(bool openVisualParams = false)
        {
            // 如果已有设置窗口打开，先关闭它
            if (_currentSettingsWindow != null)
            {
                _currentSettingsWindow.Close();
                _currentSettingsWindow = null;
            }

            try
            {
                // 显示遮罩层
                if (overlayMask != null)
                {
                    overlayMask.Visibility = Visibility.Visible;
                }

                _currentSettingsWindow = new SettingsWindow(openVisualParams);
                _currentSettingsWindow.Owner = this;
                _currentSettingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _currentSettingsWindow.Closed += async (s, args) =>
                {
                    // 设置窗口关闭时隐藏遮罩层
                    if (overlayMask != null)
                    {
                        overlayMask.Visibility = Visibility.Collapsed;
                    }

                    // 检查是否需要开始批量预测
                    var window = s as SettingsWindow;
                    if (window != null && window.StartBatchPredictRequested)
                    {
                        await RunBatchInferJsonAsync();
                    }

                    _currentSettingsWindow = null;
                };
                
                // 使用 Show() 而不是 ShowDialog()，这样用户可以点击遮罩层关闭
                _currentSettingsWindow.Show();
            }
            catch
            {
                try
                {
                    if (overlayMask != null)
                    {
                        overlayMask.Visibility = Visibility.Collapsed;
                    }
                }
                catch { }
                _currentSettingsWindow = null;
            }
        }

        /// <summary>
        /// 遮罩层点击事件 - 关闭设置窗口
        /// </summary>
        private void OverlayMask_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentSettingsWindow != null)
            {
                _currentSettingsWindow.Close();
            }
        }

        // 批量推理按钮
        private void btnInferBatchJson_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsWindow();
        }

        private void UpdateFolderImageCount(string folderPath)
        {
            int count = 0;
            if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                try
                {
                    count = Directory.EnumerateFiles(folderPath)
                        .Count(file => ImageExtensions.Contains(Path.GetExtension(file)));
                }
                catch
                {
                    count = 0;
                }
            }

            FolderImageCount = count;
        }

        private void LoadFolderTree(string rootPath)
        {
            UpdateFolderImageCount(rootPath);
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

            var nodes = new System.Collections.ObjectModel.ObservableCollection<FileNode>();
            var rootNode = CreateFileNode(rootPath);
            rootNode.IsExpanded = true;
            nodes.Add(rootNode);

            tvFolders.ItemsSource = nodes;
        }

        private FileNode CreateFileNode(string path)
        {
            var node = new FileNode
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                IsDirectory = true
            };

            try
            {
                foreach (var dir in Directory.GetDirectories(path))
                {
                    node.Children.Add(CreateFileNode(dir));
                }

                var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
                var imageFiles = Directory.GetFiles(path).Where(f => extensions.Contains(Path.GetExtension(f).ToLower()));
                foreach (var file in imageFiles)
                {
                    node.Children.Add(new FileNode
                    {
                        Name = Path.GetFileName(file),
                        FullPath = file,
                        IsDirectory = false
                    });
                }
            }
            catch { }
            return node;
        }

        private async void tvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is FileNode selectedNode)
            {
                if (!selectedNode.IsDirectory && File.Exists(selectedNode.FullPath))
                {
                    _currentImagePath = selectedNode.FullPath;
                    await ProcessSelectedImageAsync(selectedNode.FullPath);
                }
            }
            else
            {
                _currentImagePath = null;
            }
        }

        // 对应搜索框回车事件
        // LIKE风格模糊搜索
        // 这里只是去重新加载了当前的树，目前功能没有完成，看后续方案
        private void txtFolderSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string input = txtFolderSearch.Text.Trim();
                if (string.IsNullOrEmpty(input)) return;

                if (Directory.Exists(input))
                {
                    LoadFolderTree(input);
                    txtDataPath.Text = input;
                    try
                    {
                        Properties.Settings.Default.SavedDataPath = input;
                        Properties.Settings.Default.Save();
                    }
                    catch { }
                }
                else if (File.Exists(input))
                {
                    string dir = Path.GetDirectoryName(input);
                    LoadFolderTree(dir);
                    txtDataPath.Text = dir;
                    try
                    {
                        Properties.Settings.Default.SavedDataPath = dir;
                        Properties.Settings.Default.Save();
                    }
                    catch { }
                }
                else
                {
                    // 搜索当前目录中的文件（LIKE 匹配：文件名包含输入）
                    string currentDir = txtDataPath.Text;
                    if (string.IsNullOrEmpty(currentDir) || !Directory.Exists(currentDir))
                    {
                        //MessageBox.Show("请先选择有效的图片文件夹！");
                        return;
                    }
                    var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
                    var matchingFiles = Directory.GetFiles(currentDir, "*", SearchOption.AllDirectories)
                        .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()) && Path.GetFileName(f).IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                    if (matchingFiles.Any())
                    {
                        // 这里只是去重新加载了当前的树，目前功能没有完成，看后续方案
                        LoadFolderTree(currentDir);
                        txtDataPath.Text = currentDir;
                        try
                        {
                            Properties.Settings.Default.SavedDataPath = currentDir;
                            Properties.Settings.Default.Save();
                        }
                        catch { }
                    }
                    else
                    {
                        //MessageBox.Show("未找到匹配的文件或文件夹！");
                    }
                }
            }
        }

        //显示边缘
        public void chkShowEdgesPane_Checked(object sender, RoutedEventArgs e)
        {
            bool isChecked;
            if (sender is Controls.AnimatedCheckBox animatedCheckBox)
            {
                isChecked = animatedCheckBox.IsChecked;
            }
            else if (sender is System.Windows.Controls.CheckBox checkBox)
            {
                isChecked = checkBox.IsChecked ?? true;
            }
            else
            {
                return;
            }

            // 初始化期间不保存，避免覆盖用户设置
            if (!_isInitializing)
            {
                Settings.Default.ShowContours = isChecked;
                Settings.Default.Save();
            }
            // 刷新图片以应用设置
            RefreshImages();
        }

        //显示mask
        public void chkShowMaskPane_Checked(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[chkShowMaskPane_Checked] 事件触发, _isInitializing={_isInitializing}");
            
            bool isChecked;
            if (sender is Controls.AnimatedCheckBox animatedCheckBox)
            {
                isChecked = animatedCheckBox.IsChecked;
            }
            else if (sender is System.Windows.Controls.CheckBox checkBox)
            {
                isChecked = checkBox.IsChecked ?? true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[chkShowMaskPane_Checked] sender 类型不匹配，跳过");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[chkShowMaskPane_Checked] isChecked={isChecked}");

            // 初始化期间不保存，避免覆盖用户设置
            if (!_isInitializing)
            {
                Settings.Default.ShowMaskPane = isChecked;
                Settings.Default.Save();
                System.Diagnostics.Debug.WriteLine($"[chkShowMaskPane_Checked] 已保存 ShowMaskPane={isChecked}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[chkShowMaskPane_Checked] 跳过保存，因为 _isInitializing=true");
            }
            // 刷新图片以应用设置
            RefreshImages();
        }

        // 参数 TextBox 文本改变时触发更新
        private async void ParameterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // 直接触发更新，重新进行模型推理
            if (model != null && !string.IsNullOrEmpty(_currentImagePath) && File.Exists(_currentImagePath))
            {
                await ProcessSelectedImageAsync(_currentImagePath);
            }
        }

        // 参数 TextBox 失去焦点时触发更新
        private async void ParameterTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (model != null && !string.IsNullOrEmpty(_currentImagePath) && File.Exists(_currentImagePath))
            {
                await ProcessSelectedImageAsync(_currentImagePath);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 清理模型
            if (model != null)
            {
                try { ((IDisposable)model).Dispose(); } catch { }
                model = null;
            }
        }
    }
}

