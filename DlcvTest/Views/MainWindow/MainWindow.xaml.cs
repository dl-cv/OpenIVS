using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Animation;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using DlcvTest.WPFViewer;

namespace DlcvTest
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // 字段声明
        private dynamic model = null;
        private string _currentImagePath = null;

        /// <summary>
        /// 初始化标志位，防止 Loaded 事件中设置 checkbox 值时触发保存
        /// </summary>
        private bool _isInitializing = true;
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"
        };
        private static readonly object _diagLogLock = new object();

        /// <summary>
        /// 批量预测停止标志（volatile 确保多线程可见性）
        /// </summary>
        private volatile bool batchStopFlag = false;

        private int _folderImageCount = 0;
        public int FolderImageCount
        {
            get { return _folderImageCount; }
            private set
            {
                if (_folderImageCount != value)
                {
                    _folderImageCount = value;
                    OnPropertyChanged(nameof(FolderImageCount));
                }
            }
        }

        private bool _isBatchRunning = false;
        public bool IsBatchRunning
        {
            get { return _isBatchRunning; }
            private set
            {
                if (_isBatchRunning != value)
                {
                    _isBatchRunning = value;
                    OnPropertyChanged(nameof(IsBatchRunning));
                }
            }
        }

        private double _batchProgressValue = 0.0;
        public double BatchProgressValue
        {
            get { return _batchProgressValue; }
            private set
            {
                if (Math.Abs(_batchProgressValue - value) > 1e-6)
                {
                    _batchProgressValue = value;
                    OnPropertyChanged(nameof(BatchProgressValue));
                }
            }
        }

        public static readonly DependencyProperty BatchProgressVisualValueProperty =
            DependencyProperty.Register(
                nameof(BatchProgressVisualValue),
                typeof(double),
                typeof(MainWindow),
                new PropertyMetadata(0.0));

        public double BatchProgressVisualValue
        {
            get { return (double)GetValue(BatchProgressVisualValueProperty); }
            set { SetValue(BatchProgressVisualValueProperty, value); }
        }

        private string _batchProgressText = "批量预测";
        public string BatchProgressText
        {
            get { return _batchProgressText; }
            private set
            {
                if (!string.Equals(_batchProgressText, value, StringComparison.Ordinal))
                {
                    _batchProgressText = value;
                    OnPropertyChanged(nameof(BatchProgressText));
                }
            }
        }

        private void BeginBatchProgress(int total)
        {
            SetBatchProgressCore(0, total, isRunning: true);
        }

        private void UpdateBatchProgress(int current, int total)
        {
            SetBatchProgressCore(current, total, isRunning: true);
        }

        private void EndBatchProgress()
        {
            // 使用 Invoke 同步执行，确保在所有 BeginInvoke 的进度更新之后执行
            if (Dispatcher.CheckAccess())
            {
                IsBatchRunning = false;
                BatchProgressValue = 0.0;
                BatchProgressText = "批量预测";
                AnimateBatchProgressVisual(0.0);
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    IsBatchRunning = false;
                    BatchProgressValue = 0.0;
                    BatchProgressText = "批量预测";
                    AnimateBatchProgressVisual(0.0);
                });
            }
        }

        /// <summary>
        /// 取消批量预测按钮点击事件
        /// </summary>
        private void btnCancelBatch_Click(object sender, RoutedEventArgs e)
        {
            if (IsBatchRunning && !batchStopFlag)
            {
                batchStopFlag = true;
                BatchProgressText = "正在取消...";
            }
        }

        private void SetBatchProgressCore(int current, int total, bool isRunning)
        {
            if (total <= 0) total = 1;
            double percent = current * 100.0 / total;
            if (percent < 0.0) percent = 0.0;
            if (percent > 100.0) percent = 100.0;
            string text = $"正在预测({percent:0}%)";

            RunOnUiThread(() =>
            {
                IsBatchRunning = isRunning;
                BatchProgressValue = percent;
                BatchProgressText = text;
                AnimateBatchProgressVisual(percent);
            });
        }

        private void AnimateBatchProgressVisual(double targetPercent)
        {
            if (targetPercent < 0.0) targetPercent = 0.0;
            if (targetPercent > 100.0) targetPercent = 100.0;

            var animation = new DoubleAnimation
            {
                From = BatchProgressVisualValue,
                To = targetPercent,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(BatchProgressVisualValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private void RunOnUiThread(Action action)
        {
            if (action == null) return;
            if (Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// 批量预测：单张图片完成事件参数（预留外部存储/导出挂钩）。
        /// </summary>
        public sealed class BatchItemCompletedEventArgs : EventArgs
        {
            public string ImagePath { get; }
            public bool Success { get; }
            public Exception Error { get; }
            public double ElapsedMs { get; }
            public JObject InferenceParams { get; }
            public string ExportedImagePath { get; }
            public Utils.CSharpResult? Result { get; }

            public BatchItemCompletedEventArgs(
                string imagePath,
                bool success,
                Exception error,
                double elapsedMs,
                JObject inferenceParams,
                string exportedImagePath,
                Utils.CSharpResult? result)
            {
                ImagePath = imagePath;
                Success = success;
                Error = error;
                ElapsedMs = elapsedMs;
                InferenceParams = inferenceParams;
                ExportedImagePath = exportedImagePath;
                Result = result;
            }
        }

        /// <summary>
        /// 批量预测：整批完成事件参数（预留外部存储/导出挂钩）。
        /// </summary>
        public sealed class BatchCompletedEventArgs : EventArgs
        {
            public int Total { get; }
            public int Processed { get; }
            public int Exported { get; }
            public int Skipped { get; }
            public int Failed { get; }
            public string OutputDir { get; }
            public JObject InferenceParams { get; }

            public BatchCompletedEventArgs(int total, int processed, int exported, int skipped, int failed, string outputDir, JObject inferenceParams)
            {
                Total = total;
                Processed = processed;
                Exported = exported;
                Skipped = skipped;
                Failed = failed;
                OutputDir = outputDir;
                InferenceParams = inferenceParams;
            }
        }

        public event EventHandler<BatchItemCompletedEventArgs> BatchItemCompleted;
        public event EventHandler<BatchCompletedEventArgs> BatchCompleted;

        private void RaiseBatchItemCompleted(BatchItemCompletedEventArgs e)
        {
            try { BatchItemCompleted?.Invoke(this, e); } catch { }
        }

        private void RaiseBatchCompleted(BatchCompletedEventArgs e)
        {
            try { BatchCompleted?.Invoke(this, e); } catch { }
        }

        private sealed class RecentModelItem
        {
            public string DisplayName { get; set; }
            public string ModelPath { get; set; }
            public bool IsPlaceholder { get; set; }

            public override string ToString()
            {
                return DisplayName ?? string.Empty;
            }

            public static RecentModelItem CreatePlaceholder(string displayName)
            {
                return new RecentModelItem
                {
                    DisplayName = displayName,
                    ModelPath = null,
                    IsPlaceholder = true
                };
            }
        }

        private readonly ObservableCollection<RecentModelItem> _modelNameItems = new ObservableCollection<RecentModelItem>();
        private bool _isUpdatingModelCombo = false;
        private string _loadedModelPath = null;

        // 图片处理请求控制：保证"最新请求优先"，避免旧任务回写覆盖新图
        private int _imageProcessRequestId = 0;
        private int _imageProcessActiveRequestId = 0;
        private readonly object _imageProcessSync = new object();
        private CancellationTokenSource _imageProcessCts = null;

        // 高风险入口（设置页面高频触发刷新）需要去抖，避免堆积大量后台任务
        private System.Windows.Threading.DispatcherTimer _refreshDebounceTimer = null;

        // WPF 叠加显示：两个 Viewer 共享同一套缩放/平移状态
        private ImageViewState _wpfSharedViewState = null;

        // 业务逻辑已拆分到多个 partial 文件。
    }

    public sealed class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
            {
                return 0.0;
            }

            double width = 0.0;
            double progress = 0.0;

            if (values[0] is double w && !double.IsNaN(w) && !double.IsInfinity(w))
            {
                width = w;
            }

            if (values[1] is double p && !double.IsNaN(p) && !double.IsInfinity(p))
            {
                progress = p;
            }

            if (width <= 0.0)
            {
                return 0.0;
            }

            if (progress < 0.0) progress = 0.0;
            if (progress > 100.0) progress = 100.0;

            return width * progress / 100.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class FileNode : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public System.Collections.ObjectModel.ObservableCollection<FileNode> Children { get; set; } = new System.Collections.ObjectModel.ObservableCollection<FileNode>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged("IsExpanded");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

