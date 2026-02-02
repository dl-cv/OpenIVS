using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using DlcvTest.Properties;
using DlcvTest.WPFViewer;
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

                // 刷新图片以应用新的 top_k 设置
                RefreshImages();
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
                searchOriginalRootPath = selectedPath; // 保存原始目录路径用于搜索恢复
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

        /// <summary>
        /// 递归过滤单个节点，返回过滤后的新节点（不修改原节点）
        /// 同时匹配文件名和文件夹名
        /// </summary>
        private FileNode FilterNode(FileNode node, string keyword)
        {
            if (node == null) return null;

            bool nameMatches = node.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

            if (!node.IsDirectory)
            {
                // 文件：检查名称是否包含搜索词（忽略大小写）
                if (nameMatches)
                {
                    return new FileNode
                    {
                        Name = node.Name,
                        FullPath = node.FullPath,
                        IsDirectory = false
                    };
                }
                return null;
            }

            // 目录：如果目录名称匹配，保留整个目录及其所有子内容
            if (nameMatches)
            {
                return CloneNode(node);
            }

            // 目录名称不匹配时，递归过滤子节点
            var filteredChildren = new System.Collections.ObjectModel.ObservableCollection<FileNode>();
            foreach (var child in node.Children)
            {
                var filteredChild = FilterNode(child, keyword);
                if (filteredChild != null)
                {
                    filteredChildren.Add(filteredChild);
                }
            }

            // 只有包含匹配子节点的目录才保留
            if (filteredChildren.Count > 0)
            {
                return new FileNode
                {
                    Name = node.Name,
                    FullPath = node.FullPath,
                    IsDirectory = true,
                    IsExpanded = true, // 自动展开包含匹配项的目录
                    Children = filteredChildren
                };
            }

            return null;
        }

        /// <summary>
        /// 深拷贝一个FileNode节点及其所有子节点
        /// </summary>
        private FileNode CloneNode(FileNode node)
        {
            if (node == null) return null;

            var clonedNode = new FileNode
            {
                Name = node.Name,
                FullPath = node.FullPath,
                IsDirectory = node.IsDirectory,
                IsExpanded = true // 匹配的目录自动展开
            };

            foreach (var child in node.Children)
            {
                clonedNode.Children.Add(CloneNode(child));
            }

            return clonedNode;
        }

        /// <summary>
        /// 根据关键词过滤TreeView，只显示匹配的文件和包含匹配文件的目录
        /// </summary>
        private void FilterTreeByKeyword(string keyword)
        {
            if (string.IsNullOrEmpty(searchOriginalRootPath) || !Directory.Exists(searchOriginalRootPath))
                return;

            // 重新构建完整的树
            var fullRootNode = CreateFileNode(searchOriginalRootPath);

            // 过滤树
            var filteredNode = FilterNode(fullRootNode, keyword);

            if (filteredNode != null)
            {
                var nodes = new System.Collections.ObjectModel.ObservableCollection<FileNode>();
                filteredNode.IsExpanded = true;
                nodes.Add(filteredNode);
                tvFolders.ItemsSource = nodes;
            }
            else
            {
                // 没有匹配项时显示空树
                tvFolders.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<FileNode>();
            }
        }

        /// <summary>
        /// 恢复完整的树形结构
        /// </summary>
        private void RestoreFullTree()
        {
            if (string.IsNullOrEmpty(searchOriginalRootPath) || !Directory.Exists(searchOriginalRootPath))
                return;

            LoadFolderTree(searchOriginalRootPath);
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

        private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem item && item.DataContext is FileNode node && node.IsDirectory)
            {
                // 检查点击的元素是否直接属于当前 TreeViewItem（而不是子项）
                DependencyObject source = e.OriginalSource as DependencyObject;
                TreeViewItem clickedItem = FindVisualParent<TreeViewItem>(source);
                
                if (clickedItem == item)
                {
                    item.IsExpanded = !item.IsExpanded;
                    e.Handled = true;
                }
            }
        }

        private static T FindVisualParent<T>(DependencyObject obj) where T : DependencyObject
        {
            while (obj != null && !(obj is T))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }
            return obj as T;
        }

        // 对应搜索框回车事件和ESC事件
        // Enter键：执行过滤搜索或加载新目录
        // ESC键：清空搜索框并恢复完整树形结构
        private void txtFolderSearch_KeyDown(object sender, KeyEventArgs e)
        {
            // ESC键：清空搜索框并恢复完整树形结构
            if (e.Key == Key.Escape)
            {
                txtFolderSearch.Text = "";
                RestoreFullTree();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                string input = txtFolderSearch.Text.Trim();

                // 搜索框为空时恢复完整树形结构
                if (string.IsNullOrEmpty(input))
                {
                    RestoreFullTree();
                    return;
                }

                // 如果输入的是完整目录路径，则加载该目录
                if (Directory.Exists(input))
                {
                    searchOriginalRootPath = input;
                    LoadFolderTree(input);
                    txtDataPath.Text = input;
                    txtFolderSearch.Text = ""; // 加载新目录后清空搜索框
                    try
                    {
                        Properties.Settings.Default.SavedDataPath = input;
                        Properties.Settings.Default.Save();
                    }
                    catch { }
                    return;
                }

                // 如果输入的是完整文件路径，则加载该文件所在目录
                if (File.Exists(input))
                {
                    string dir = Path.GetDirectoryName(input);
                    searchOriginalRootPath = dir;
                    LoadFolderTree(dir);
                    txtDataPath.Text = dir;
                    txtFolderSearch.Text = ""; // 加载新目录后清空搜索框
                    try
                    {
                        Properties.Settings.Default.SavedDataPath = dir;
                        Properties.Settings.Default.Save();
                    }
                    catch { }
                    return;
                }

                // 否则执行关键词搜索过滤
                // 确保有有效的原始目录
                if (string.IsNullOrEmpty(searchOriginalRootPath))
                {
                    searchOriginalRootPath = txtDataPath.Text;
                }

                if (string.IsNullOrEmpty(searchOriginalRootPath) || !Directory.Exists(searchOriginalRootPath))
                {
                    return;
                }

                FilterTreeByKeyword(input);
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

        /// <summary>
        /// 防止全选操作时递归触发事件
        /// </summary>
        private bool isUpdatingCategoryFilter = false;

        /// <summary>
        /// 类别屏蔽：全选/取消全选复选框变化事件
        /// </summary>
        private void CategoryFilterSelectAll_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || isUpdatingCategoryFilter) return;
            if (!(sender is System.Windows.Controls.CheckBox chk)) return;

            isUpdatingCategoryFilter = true;
            try
            {
                bool selectAll = chk.IsChecked ?? false;
                foreach (var item in availableCategories)
                {
                    item.IsSelected = selectAll;
                }

                UpdateHiddenCategoriesFromUI();
                UpdateCategoryFilterDisplay();
                RefreshImages();
            }
            finally
            {
                isUpdatingCategoryFilter = false;
            }
        }

        /// <summary>
        /// 类别屏蔽：单个类别复选框变化事件
        /// </summary>
        private void CategoryFilterItem_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || isUpdatingCategoryFilter) return;

            UpdateHiddenCategoriesFromUI();
            UpdateCategoryFilterDisplay();
            UpdateSelectAllCheckState();
            RefreshImages();
        }

        /// <summary>
        /// 从 UI 更新 hiddenCategories 集合
        /// 注意：IsSelected=true 表示该类别被选中要屏蔽
        /// </summary>
        private void UpdateHiddenCategoriesFromUI()
        {
            hiddenCategories.Clear();
            foreach (var item in availableCategories)
            {
                if (item.IsSelected)
                {
                    hiddenCategories.Add(item.Name);
                }
            }

            // 同步到 WpfViewer 的 Options
            if (wpfViewer2 != null && wpfViewer2.Options != null)
            {
                wpfViewer2.Options.HiddenCategories = hiddenCategories;
            }
        }

        /// <summary>
        /// 更新类别屏蔽显示文本
        /// </summary>
        private void UpdateCategoryFilterDisplay()
        {
            if (txtCategoryFilterDisplay == null) return;

            int selectedCount = 0;
            foreach (var item in availableCategories)
            {
                if (item.IsSelected) selectedCount++;
            }

            if (selectedCount == 0)
            {
                txtCategoryFilterDisplay.Text = "无";
            }
            else if (selectedCount == 1)
            {
                foreach (var item in availableCategories)
                {
                    if (item.IsSelected)
                    {
                        txtCategoryFilterDisplay.Text = item.Name;
                        break;
                    }
                }
            }
            else
            {
                txtCategoryFilterDisplay.Text = $"{selectedCount}个类别";
            }
        }

        /// <summary>
        /// 更新全选复选框状态：只有全部选中时才显示勾，否则不显示
        /// </summary>
        private void UpdateSelectAllCheckState()
        {
            if (chkCategoryFilterSelectAll == null) return;
            if (availableCategories.Count == 0)
            {
                chkCategoryFilterSelectAll.IsChecked = false;
                return;
            }

            int selectedCount = 0;
            foreach (var item in availableCategories)
            {
                if (item.IsSelected) selectedCount++;
            }

            // 只有全部选中才显示勾，否则不显示
            chkCategoryFilterSelectAll.IsChecked = (selectedCount == availableCategories.Count);
        }

        /// <summary>
        /// 设置可用的类别列表（从推理结果中提取）
        /// </summary>
        public void SetAvailableCategories(IEnumerable<string> categories)
        {
            availableCategories.Clear();
            hiddenCategories.Clear();

            if (categories != null)
            {
                foreach (var cat in categories)
                {
                    if (!string.IsNullOrWhiteSpace(cat))
                    {
                        availableCategories.Add(new CategoryFilterItem { Name = cat, IsSelected = false });
                    }
                }
            }

            // 绑定到 ItemsControl
            if (lstCategoryFilter != null)
            {
                lstCategoryFilter.ItemsSource = availableCategories;
            }

            UpdateCategoryFilterDisplay();
            UpdateSelectAllCheckState();

            // 同步到 WpfViewer 的 Options
            if (wpfViewer2 != null && wpfViewer2.Options != null)
            {
                wpfViewer2.Options.HiddenCategories = hiddenCategories;
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

