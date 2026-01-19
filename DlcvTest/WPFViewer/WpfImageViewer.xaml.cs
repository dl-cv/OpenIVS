using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using dlcv_infer_csharp;

namespace DlcvTest.WPFViewer
{
    /// <summary>
    /// WPF 版图像查看器：GUI 叠加绘制 bbox/旋转框/mask/中文/状态，缩放时文字可保持可读性。
    /// </summary>
    public partial class WpfImageViewer : UserControl
    {
        private bool _isDragging;
        private Point _lastMouse;

        private ImageViewState _viewState;
        private bool _isApplyingViewState;
        private bool _isApplyViewStateQueued;
        private bool _isUpdateBaseFitScaleQueued;

        private WpfVisualize.VisualizeResult _externalOverlay;

        // Viewbox(Uniform) 负责“自动铺满”，这里仅记录它对应的基础缩放，用于让文字/线宽抵消总缩放
        private double _baseFitScale = 1.0;
        private double _lastOverlayViewScale = double.NaN;

        public WpfImageViewer()
        {
            InitializeComponent();

            Options = new WpfVisualize.Options();
            ViewState = new ImageViewState();

            // 交互事件（Preview：优先于外层 Border，避免与旧逻辑冲突）
            PreviewMouseWheel += WpfImageViewer_PreviewMouseWheel;
            PreviewMouseLeftButtonDown += WpfImageViewer_PreviewMouseLeftButtonDown;
            PreviewMouseMove += WpfImageViewer_PreviewMouseMove;
            PreviewMouseLeftButtonUp += WpfImageViewer_PreviewMouseLeftButtonUp;
            PreviewMouseRightButtonUp += WpfImageViewer_PreviewMouseRightButtonUp;
            PreviewKeyDown += WpfImageViewer_PreviewKeyDown;

            SizeChanged += (_, __) => UpdateBaseFitScale();
            Loaded += (_, __) => UpdateBaseFitScale();

            if (FitBox != null)
            {
                FitBox.SizeChanged += (_, __) => UpdateBaseFitScale();
            }
            if (ScaleTf != null)
            {
                ScaleTf.Changed += (_, __) => UpdateOverlayViewScale();
            }
        }

        public WpfVisualize.Options Options { get; }

        public double MaxScale { get; set; } = 100.0;

        public double MinScale { get; set; } = 0.05;

        public bool ShowVisualization { get; set; } = true;

        // 不再显示推理状态文字（OK/NG/No Result）
        public bool ShowStatusText { get; set; } = false;

        /// <summary>
        /// 屏幕层字号（最终显示效果）。内部会自动按 1/Scale 抵消缩放。
        /// </summary>
        public double ScreenFontSize
        {
            get => Overlay.ScreenFontSize;
            set => Overlay.ScreenFontSize = value;
        }

        /// <summary>
        /// 屏幕层线宽（最终显示效果）。内部会自动按 1/Scale 抵消缩放。
        /// </summary>
        public double ScreenLineWidth
        {
            get => Overlay.ScreenLineWidth;
            set => Overlay.ScreenLineWidth = value;
        }

        public ImageViewState ViewState
        {
            get => _viewState;
            set
            {
                if (ReferenceEquals(_viewState, value)) return;
                if (_viewState != null) _viewState.PropertyChanged -= ViewState_PropertyChanged;
                _viewState = value ?? new ImageViewState();
                _viewState.PropertyChanged += ViewState_PropertyChanged;
                ApplyViewState();
            }
        }

        public ImageSource Source
        {
            get => Img.Source;
            set
            {
                Img.Source = value;
                UpdateContentSize();
                UpdateBaseFitScale();

                RebuildOverlay();
            }
        }

        private Utils.CSharpResult? _result;

        public Utils.CSharpResult? Result
        {
            get => _result;
            set
            {
                _result = value;
                RebuildOverlay();
            }
        }

        /// <summary>
        /// 额外叠加层（例如标注 shapes）。会绘制在推理结果之下�?        /// </summary>
        public WpfVisualize.VisualizeResult ExternalOverlay
        {
            get => _externalOverlay;
            set
            {
                if (ReferenceEquals(_externalOverlay, value)) return;
                _externalOverlay = value;
                RebuildOverlay();
            }
        }

        public void ClearExternalOverlay()
        {
            ExternalOverlay = null;
        }

        public void ClearResults()
        {
            Result = null;
        }

        public void UpdateImage(ImageSource source)
        {
            Source = source;
        }

        public void UpdateResults(Utils.CSharpResult result)
        {
            Result = result;
        }

        public void UpdateImageAndResult(ImageSource source, Utils.CSharpResult? result)
        {
            Source = source;
            Result = result;
        }

        private void UpdateContentSize()
        {
            try
            {
                if (!(Img.Source is System.Windows.Media.Imaging.BitmapSource bmp)) return;
                ContentLayer.Width = bmp.PixelWidth;
                ContentLayer.Height = bmp.PixelHeight;
                Overlay.Width = bmp.PixelWidth;
                Overlay.Height = bmp.PixelHeight;
            }
            catch
            {
                // ignore
            }
        }

        private void UpdateBaseFitScale()
        {
            try
            {
                if (!(Img.Source is System.Windows.Media.Imaging.BitmapSource bmp))
                {
                    _baseFitScale = 1.0;
                    UpdateOverlayViewScale();
                    return;
                }

                if (bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0) return;

                if (!TryGetViewportSize(out double viewW, out double viewH))
                {
                    QueueUpdateBaseFitScale();
                    return;
                }

                // Match Viewbox(Uniform) scaling based on actual viewport size.
                _baseFitScale = Math.Min(viewW / bmp.PixelWidth, viewH / bmp.PixelHeight);
                if (!(_baseFitScale > 0)) _baseFitScale = 1.0;
                UpdateOverlayViewScale();
            }
            catch
            {
                _baseFitScale = 1.0;
                UpdateOverlayViewScale();
            }
        }

        private void QueueUpdateBaseFitScale()
        {
            if (_isUpdateBaseFitScaleQueued) return;
            _isUpdateBaseFitScaleQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isUpdateBaseFitScaleQueued = false;
                UpdateBaseFitScale();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private bool TryGetViewportSize(out double width, out double height)
        {
            width = 0.0;
            height = 0.0;

            if (FitBox != null)
            {
                width = FitBox.ActualWidth;
                height = FitBox.ActualHeight;
            }

            if (width <= 1 || height <= 1)
            {
                width = ActualWidth;
                height = ActualHeight;
            }

            if (width <= 1 || height <= 1)
            {
                if (Root != null)
                {
                    width = Root.ActualWidth;
                    height = Root.ActualHeight;
                }
            }

            return width > 1 && height > 1;
        }

        private double GetUserScale()
        {
            double scale = ViewState != null ? ViewState.Scale : 1.0;
            if (ScaleTf != null)
            {
                double sx = Math.Abs(ScaleTf.ScaleX);
                double sy = Math.Abs(ScaleTf.ScaleY);
                if (sx > 0 && sy > 0)
                {
                    scale = (sx + sy) * 0.5;
                }
                else if (sx > 0)
                {
                    scale = sx;
                }
                else if (sy > 0)
                {
                    scale = sy;
                }
            }

            if (!(scale > 0)) scale = 1.0;
            return scale;
        }

        private void ViewState_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            QueueApplyViewState();
        }

        private void QueueApplyViewState()
        {
            if (_isApplyViewStateQueued) return;
            _isApplyViewStateQueued = true;

            // 合并同一帧内的多次 OffsetX/OffsetY/Scale 更新，避免高频交互造成重复 Apply/重绘
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isApplyViewStateQueued = false;
                ApplyViewState();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void ApplyViewState()
        {
            if (_isApplyingViewState) return;
            try
            {
                _isApplyingViewState = true;

                double s = ViewState != null ? ViewState.Scale : 1.0;
                double ox = ViewState != null ? ViewState.OffsetX : 0.0;
                double oy = ViewState != null ? ViewState.OffsetY : 0.0;

                if (Math.Abs(ScaleTf.ScaleX - s) >= 1e-12) ScaleTf.ScaleX = s;
                if (Math.Abs(ScaleTf.ScaleY - s) >= 1e-12) ScaleTf.ScaleY = s;
                if (Math.Abs(TransTf.X - ox) >= 1e-12) TransTf.X = ox;
                if (Math.Abs(TransTf.Y - oy) >= 1e-12) TransTf.Y = oy;

                UpdateOverlayViewScale();
            }
            finally
            {
                _isApplyingViewState = false;
            }
        }

        private void UpdateOverlayViewScale()
        {
            double total = _baseFitScale * GetUserScale();
            if (!(total > 0)) total = 1.0;

            // 只有缩放变化才需要重绘 Overlay（线宽/字号按 1/scale 抵消）；平移不应强制重绘
            if (!double.IsNaN(_lastOverlayViewScale) && Math.Abs(_lastOverlayViewScale - total) < 1e-12)
            {
                return;
            }

            _lastOverlayViewScale = total;
            Overlay.ViewScale = total;
        }

        private void RebuildOverlay()
        {
            if (!ShowVisualization)
            {
                Overlay.VisualizeResult = new WpfVisualize.VisualizeResult { StatusText = string.Empty, StatusIsOk = true };
                UpdateHud(Overlay.VisualizeResult);
                return;
            }

            // 推理层（用于 HUD 状态）
            WpfVisualize.VisualizeResult vrInfer;
            if (Result.HasValue)
            {
                vrInfer = WpfVisualize.Build(Result, Options);
            }
            else
            {
                // 仅显示图像/标注时，不应出现红色 “No Result”
                vrInfer = new WpfVisualize.VisualizeResult { StatusText = string.Empty, StatusIsOk = true };
            }

            // 外部层（例如标注）
            var vrExt = ExternalOverlay;

            // 合并绘制：先外部层，再推理层
            WpfVisualize.VisualizeResult vrDraw;
            if (vrExt == null || vrExt.Items == null || vrExt.Items.Count == 0)
            {
                vrDraw = vrInfer;
            }
            else
            {
                vrDraw = new WpfVisualize.VisualizeResult
                {
                    StatusText = vrInfer.StatusText,
                    StatusIsOk = vrInfer.StatusIsOk
                };

                // 先标注
                if (vrExt.Items != null && vrExt.Items.Count > 0)
                {
                    vrDraw.Items.AddRange(vrExt.Items);
                }
                // 再推理
                if (vrInfer.Items != null && vrInfer.Items.Count > 0)
                {
                    vrDraw.Items.AddRange(vrInfer.Items);
                }
            }

            Overlay.VisualizeResult = vrDraw;
            Overlay.ShowStatusText = ShowStatusText;
            Overlay.TextOutOfBbox = Options.TextOutOfBbox;
            Overlay.ShowTextShadow = Options.DisplayTextShadow;
            UpdateHud(vrInfer);
        }

        private void UpdateHud(WpfVisualize.VisualizeResult vr)
        {
            if (!ShowStatusText || vr == null)
            {
                HudBorder.Visibility = Visibility.Collapsed;
                return;
            }

            HudBorder.Visibility = Visibility.Visible;
            HudText.Text = vr.StatusText ?? string.Empty;
            HudText.Foreground = new SolidColorBrush(vr.StatusIsOk ? Colors.LimeGreen : Colors.Red);
        }

        private void WpfImageViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Img.Source == null) return;

            double oldScale = ViewState.Scale;
            double newScale = oldScale;
            if (e.Delta > 0) newScale = oldScale * 1.1;
            else if (e.Delta < 0) newScale = oldScale / 1.1;
            else return;

            newScale = Clamp(newScale, MinScale, MaxScale);
            if (Math.Abs(newScale - oldScale) < 1e-12) return;

            // 以鼠标位置为中心缩放（与 WinForms ImageViewer 同款公式）
            Point mouse = e.GetPosition(this);
            double scaleChange = newScale / oldScale;
            double newTx = mouse.X - scaleChange * (mouse.X - ViewState.OffsetX);
            double newTy = mouse.Y - scaleChange * (mouse.Y - ViewState.OffsetY);

            ViewState.SetView(newScale, newTx, newTy);
            e.Handled = true;
        }

        private void WpfImageViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Img.Source == null) return;
            Focus();
            _isDragging = true;
            _lastMouse = e.GetPosition(this);
            CaptureMouse();
            e.Handled = true;
        }

        private void WpfImageViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            Point cur = e.GetPosition(this);
            double dx = cur.X - _lastMouse.X;
            double dy = cur.Y - _lastMouse.Y;
            _lastMouse = cur;

            ViewState.SetView(ViewState.Scale, ViewState.OffsetX + dx, ViewState.OffsetY + dy);
            e.Handled = true;
        }

        private void WpfImageViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        private void WpfImageViewer_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 右键：重置视图（基础铺满，Viewbox(Uniform) 保证）
            ViewState.SetView(1.0, 0.0, 0.0);
            e.Handled = true;
        }

        private void WpfImageViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V)
            {
                ShowVisualization = !ShowVisualization;
                RebuildOverlay();
                e.Handled = true;
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}


