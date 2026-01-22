using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DlcvTest.Properties;
using Ookii.Dialogs.Wpf;

namespace DlcvTest
{
    public partial class StandardSettingsView : UserControl
    {
        /// <summary>
        /// 初始化标志位，防止 Loaded 事件中设置 checkbox 值时触发保存
        /// </summary>
        private bool _isInitializing = true;

        public StandardSettingsView()
        {
            InitializeComponent();
            this.Loaded += StandardSettingsView_Loaded;
        }

        private void StandardSettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            try
            {
                // 加载保存的设置状态
                chkAutoLoadDataPath.IsChecked = Settings.Default.AutoLoadDataPath;
                chkAutoLoadModel.IsChecked = Settings.Default.AutoLoadModel;
                chkSaveOriginal.IsChecked = Settings.Default.SaveOriginal;
                chkSaveVisualization.IsChecked = Settings.Default.SaveVisualization;
                if (chkOpenOutputFolderAfterBatch != null)
                {
                    chkOpenOutputFolderAfterBatch.IsChecked = Settings.Default.OpenOutputFolderAfterBatch;
                }

                // 加载输出目录
                try
                {
                    if (txtOutputDir != null)
                    {
                        txtOutputDir.Text = Settings.Default.OutputDirectory ?? string.Empty;
                    }
                }
                catch
                {
                    // ignore
                }
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void ChkAutoLoadDataPath_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Settings.Default.AutoLoadDataPath = true;
            Settings.Default.Save();
        }

        private void ChkAutoLoadDataPath_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Settings.Default.AutoLoadDataPath = false;
            Settings.Default.Save();
        }

        private void ChkAutoLoadModel_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Settings.Default.AutoLoadModel = true;
            Settings.Default.Save();
        }

        private void ChkAutoLoadModel_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Settings.Default.AutoLoadModel = false;
            Settings.Default.Save();
        }

        private void ChkSaveOriginal_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Settings.Default.SaveOriginal = true;
            Settings.Default.Save();
        }

        private void ChkSaveOriginal_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Settings.Default.SaveOriginal = false;
            Settings.Default.Save();
        }

        private void ChkSaveVisualization_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Settings.Default.SaveVisualization = true;
            Settings.Default.Save();
        }

        private void ChkSaveVisualization_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Settings.Default.SaveVisualization = false;
            Settings.Default.Save();
        }

        private void ChkOpenOutputFolderAfterBatch_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Settings.Default.OpenOutputFolderAfterBatch = true;
            Settings.Default.Save();
        }

        private void ChkOpenOutputFolderAfterBatch_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Settings.Default.OpenOutputFolderAfterBatch = false;
            Settings.Default.Save();
        }

        private void BtnBrowseOutputDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new VistaFolderBrowserDialog
                {
                    Description = "选择输出目录",
                    UseDescriptionForTitle = true
                };

                // 尝试设置初始目录
                try
                {
                    var current = Settings.Default.OutputDirectory;
                    if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                    {
                        dialog.SelectedPath = current;
                    }
                }
                catch { }

                if (dialog.ShowDialog() == true)
                {
                    SetOutputDirectory(dialog.SelectedPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("选择输出目录失败: " + ex.Message);
            }
        }

        private void BtnClearOutputDir_Click(object sender, RoutedEventArgs e)
        {
            SetOutputDirectory(string.Empty);
        }

        private void TxtOutputDir_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                SetOutputDirectory(txtOutputDir != null ? txtOutputDir.Text : string.Empty);
            }
            catch
            {
                // ignore
            }
        }

        private void SetOutputDirectory(string path)
        {
            string value = (path ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                try { value = Path.GetFullPath(value); } catch { }
            }

            Settings.Default.OutputDirectory = value;
            Settings.Default.Save();

            try
            {
                if (txtOutputDir != null && txtOutputDir.Text != value)
                {
                    txtOutputDir.Text = value;
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}

