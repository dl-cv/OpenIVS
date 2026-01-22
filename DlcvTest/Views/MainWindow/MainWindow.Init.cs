using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using dlcv_infer_csharp;
using DlcvTest.Properties;
using DlcvTest.WPFViewer;

namespace DlcvTest
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeModelCombo();
            InitializeWpfViewers();
            this.Loaded += MainWindow_Loaded;
        }

        private void InitializeWpfViewers()
        {
            _wpfSharedViewState = new ImageViewState();
            try
            {
                if (wpfViewer1 != null)
                {
                    wpfViewer1.ViewState = _wpfSharedViewState;
                    wpfViewer1.ShowStatusText = false; // 左侧默认不显示 OK/NG
                }
                if (wpfViewer2 != null)
                {
                    wpfViewer2.ViewState = _wpfSharedViewState;
                    wpfViewer2.ShowStatusText = false; // 不显示 OK/NG/No Result
                }
            }
            catch
            {
                // ignore
            }
        }

        private void InitializeModelCombo()
        {
            try
            {
                if (cmbModelName == null) return;
                cmbModelName.ItemsSource = _modelNameItems;
                string lastPath = null;
                try { lastPath = Properties.Settings.Default.LastModelPath; } catch { }
                if (string.IsNullOrWhiteSpace(lastPath))
                {
                    try { lastPath = Properties.Settings.Default.SavedModelPath; } catch { }
                }

                if (!string.IsNullOrWhiteSpace(lastPath))
                {
                    try
                    {
                        var recent = Properties.Settings.Default.RecentModelPaths;
                        if (recent == null || !recent.Any(p => PathEquals(p, lastPath)))
                        {
                            Properties.Settings.Default.RememberModelPath(lastPath, 3);
                            Properties.Settings.Default.Save();
                        }
                    }
                    catch { }
                }

                RefreshModelComboItems(selectedModelPath: lastPath);
            }
            catch
            {
                // ignore
            }
        }

        private static string NormalizeModelPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        private static bool PathEquals(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            string fa = NormalizeModelPath(a);
            string fb = NormalizeModelPath(b);
            return string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshModelComboItems(string selectedModelPath)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RefreshModelComboItems(selectedModelPath));
                return;
            }

            if (cmbModelName == null) return;

            _isUpdatingModelCombo = true;
            try
            {
                _modelNameItems.Clear();

                // 占位项：用于未加载模型时的显示（下拉列表中会隐藏）
                var placeholder = RecentModelItem.CreatePlaceholder("请加载模型");
                _modelNameItems.Add(placeholder);

                // 最多3个（MRU）：新进前置，最老淘汰
                var recent = Properties.Settings.Default.RecentModelPaths;
                if (recent != null)
                {
                    foreach (var p in recent)
                    {
                        var full = NormalizeModelPath(p);
                        if (string.IsNullOrWhiteSpace(full)) continue;

                        // 去重（忽略大小写）
                        if (_modelNameItems.Any(x => !x.IsPlaceholder && PathEquals(x.ModelPath, full)))
                            continue;

                        _modelNameItems.Add(new RecentModelItem
                        {
                            DisplayName = Path.GetFileName(full),
                            ModelPath = full,
                            IsPlaceholder = false
                        });

                        if (_modelNameItems.Count >= 1 + 3) break; // placeholder + 3
                    }
                }

                // 选中当前加载的模型（或占位）
                RecentModelItem toSelect = placeholder;
                if (!string.IsNullOrWhiteSpace(selectedModelPath))
                {
                    var fullSel = NormalizeModelPath(selectedModelPath);
                    toSelect = _modelNameItems.FirstOrDefault(x => !x.IsPlaceholder && PathEquals(x.ModelPath, fullSel)) ?? placeholder;
                }
                cmbModelName.SelectedItem = toSelect;
            }
            finally
            {
                _isUpdatingModelCombo = false;
            }
        }

        private async Task<bool> LoadModelAsync(string modelPath, bool showSuccessPopup)
        {
            string fullPath = NormalizeModelPath(modelPath);
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                MessageBox.Show("模型路径为空，无法加载。");
                return false;
            }
            if (!File.Exists(fullPath))
            {
                MessageBox.Show("模型文件不存在: " + fullPath);
                return false;
            }

            int deviceId = 0;
            try { deviceId = GetSelectedDeviceId(); }
            catch { deviceId = 0; }

            try
            {
                await Task.Run(() =>
                {
                    // 1. 释放旧模型
                    if (model != null)
                    {
                        try { ((IDisposable)model).Dispose(); } catch { }
                        model = null;
                    }
                    GC.Collect();

                    // 2. 直接使用 Model 类加载模型
                    model = new Model(fullPath, deviceId, false, false);
                });

                // 3) 成功后更新设置 + MRU（最多3个）
                try
                {
                    Properties.Settings.Default.LastModelPath = fullPath;
                    Properties.Settings.Default.SavedModelPath = fullPath;
                    Properties.Settings.Default.RememberModelPath(fullPath, 3);
                    Properties.Settings.Default.Save();
                }
                catch { }

                _loadedModelPath = fullPath;
                RefreshModelComboItems(fullPath);

                if (showSuccessPopup)
                {
                    MessageBox.Show($"模型加载成功: {fullPath}");
                }

                // 模型切换后自动刷新当前图片，确保推理结果同步更新
                if (!string.IsNullOrEmpty(_currentImagePath) && File.Exists(_currentImagePath))
                {
                    try
                    {
                        await ProcessSelectedImageAsync(_currentImagePath);
                    }
                    catch
                    {
                        // 刷新失败不影响模型加载结果
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载模型失败: " + ex.Message);

                // 失败时恢复到当前已加载模型（或占位）
                RefreshModelComboItems(_loadedModelPath);
                return false;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow_Loaded] 开始, _isInitializing 将设为 true");
            
            // 同步初始化部分：设置 checkbox 初始值，完成后立即解除初始化锁
            _isInitializing = true;
            try
            {
                // 如果设置了自动加载数据路径，则加载
                string savedDataPath = null;
                try { savedDataPath = Properties.Settings.Default.SavedDataPath; } catch { savedDataPath = null; }

                if (!string.IsNullOrEmpty(savedDataPath))
                {
                    txtDataPath.Text = savedDataPath;
                }
                else
                {
                    txtDataPath.Text = "";
                }

                if (Properties.Settings.Default.AutoLoadDataPath &&
                    !string.IsNullOrEmpty(savedDataPath) &&
                    Directory.Exists(savedDataPath))
                {
                    LoadFolderTree(savedDataPath);
                }

                // 设置 checkbox 初始值（必须在 await 之前完成）
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindow_Loaded] 设置 checkbox 初始值, ShowMaskPane={Settings.Default.ShowMaskPane}, ShowContours={Settings.Default.ShowContours}");
                    
                    if (chkShowMaskPane != null)
                    {
                        chkShowMaskPane.IsChecked = Settings.Default.ShowMaskPane;
                    }

                    if (chkShowEdgesPane != null)
                    {
                        chkShowEdgesPane.IsChecked = Settings.Default.ShowContours;
                    }
                }
                catch
                {
                    // ignore
                }
            }
            finally
            {
                // 立即解除初始化锁，确保用户后续操作可以正常保存
                _isInitializing = false;
                System.Diagnostics.Debug.WriteLine($"[MainWindow_Loaded] finally 块执行, _isInitializing 已设为 false");
            }

            System.Diagnostics.Debug.WriteLine($"[MainWindow_Loaded] 同步初始化完成, _isInitializing={_isInitializing}, 开始异步加载模型");

            // 异步加载模型（在 _isInitializing = false 之后进行，不影响用户操作）
            if (Properties.Settings.Default.AutoLoadModel &&
                !string.IsNullOrEmpty(Properties.Settings.Default.SavedModelPath) &&
                File.Exists(Properties.Settings.Default.SavedModelPath))
            {
                await LoadSavedModel(Properties.Settings.Default.SavedModelPath);
            }
            
            System.Diagnostics.Debug.WriteLine($"[MainWindow_Loaded] 完成");
        }

        private async Task LoadSavedModel(string modelPath)
        {
            await LoadModelAsync(modelPath, showSuccessPopup: false);
        }

        private int GetSelectedDeviceId()
        {
            return 0;
        }
    }
}

