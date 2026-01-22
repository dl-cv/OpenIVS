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

        // 基础铺满缩放比例（等价于原先 Viewbox(Uniform) 的自动铺满）
        private double _baseFitScale = 1.0;
        private double _baseFitOffsetX = 0.0;
        private double _baseFitOffsetY = 0.0;
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

            SizeChanged += (_, __) => FitToView();
            Loaded += (_, __) => FitToView();
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible) FitToView();
            };
            UpdateOverlayViewScale();
        }

        public WpfVisualize.Options Options { get; }

        public double MaxScale { get; set; } = 100.0;

        public double MinScale { get; set; } = 0.05;

        public double ZoomFactor { get; set; } = 1.15;

        public int MinZoom { get; set; } = -4;

        public bool ShowVisualization { get; set; } = true;

        /// <summary>
        /// Python/Qt 风格：叠加层随图像缩放，不做“屏幕恒定线宽/字号”补偿。
        /// </summary>
        public bool OverlayScaleWithImage { get; set; } = false;

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
                ResetViewToFit();

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
        /// 额外叠加层（例如标注 shapes）。会绘制在推理结果之下。
        /// </summary>
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

        public void FitToView()
        {
            QueueUpdateBaseFitScale();
        }

        public void ResetViewToFit()
        {
            if (ViewState != null)
            {
                ViewState.SetView(1.0, 0.0, 0.0);
                ViewState.ZoomLevel = 0;
            }
            FitToView();
        }

        private void UpdateContentSize()
        {
            try
            {
                if (!(Img.Source is System.Windows.Media.Imaging.BitmapSource bmp)) return;
                double width = bmp.PixelWidth;
                double height = bmp.PixelHeight;
                ContentLayer.Width = width;
                ContentLayer.Height = height;
                Img.Width = width;
                Img.Height = height;
                Overlay.Width = width;
                Overlay.Height = height;
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
                    _baseFitOffsetX = 0.0;
                    _baseFitOffsetY = 0.0;
                    ApplyViewState();
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

                double scaledW = bmp.PixelWidth * _baseFitScale;
                double scaledH = bmp.PixelHeight * _baseFitScale;
                _baseFitOffsetX = (viewW - scaledW) * 0.5;
                _baseFitOffsetY = (viewH - scaledH) * 0.5;
                if (double.IsNaN(_baseFitOffsetX) || double.IsInfinity(_baseFitOffsetX)) _baseFitOffsetX = 0.0;
                if (double.IsNaN(_baseFitOffsetY) || double.IsInfinity(_baseFitOffsetY)) _baseFitOffsetY = 0.0;
                ApplyViewState();
            }
            catch
            {
                _baseFitScale = 1.0;
                _baseFitOffsetX = 0.0;
                _baseFitOffsetY = 0.0;
                ApplyViewState();
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
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private bool TryGetViewportSize(out double width, out double height)
        {
            width = 0.0;
            height = 0.0;

            if (Root != null)
            {
                width = Root.ActualWidth;
                height = Root.ActualHeight;
            }

            if (width <= 1 || height <= 1)
            {
                width = Root != null ? Root.RenderSize.Width : 0.0;
                height = Root != null ? Root.RenderSize.Height : 0.0;
            }

            if (width <= 1 || height <= 1)
            {
                width = ActualWidth;
                height = ActualHeight;
            }

            if (width <= 1 || height <= 1)
            {
                width = RenderSize.Width;
                height = RenderSize.Height;
            }

            if (double.IsNaN(width) || double.IsInfinity(width) || double.IsNaN(height) || double.IsInfinity(height))
            {
                width = 0.0;
                height = 0.0;
                return false;
            }

            return width > 1 && height > 1;
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

                double userScale = ViewState != null ? ViewState.Scale : 1.0;
                double s = _baseFitScale * userScale;
                if (!(s > 0)) s = 1.0;
                double ox = (ViewState != null ? ViewState.OffsetX : 0.0) + _baseFitOffsetX;
                double oy = (ViewState != null ? ViewState.OffsetY : 0.0) + _baseFitOffsetY;

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
            if (Overlay == null) return;

            if (OverlayScaleWithImage)
            {
                const double fixedScale = 1.0;
                if (!double.IsNaN(_lastOverlayViewScale) && Math.Abs(_lastOverlayViewScale - fixedScale) < 1e-12)
                {
                    return;
                }

                _lastOverlayViewScale = fixedScale;
                Overlay.ViewScale = fixedScale;
                return;
            }

            double total = _baseFitScale * (ViewState != null ? ViewState.Scale : 1.0);
            if (!(total > 0)) total = 1.0;

            // 只有缩放变化才需要重绘 Overlay（屏幕恒定线宽/字号模式下）；平移不应强制重绘
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
                if (Overlay != null) Overlay.Visibility = Visibility.Collapsed;
                Overlay.VisualizeResult = new WpfVisualize.VisualizeResult { StatusText = string.Empty, StatusIsOk = true };
                UpdateHud(Overlay.VisualizeResult);
                return;
            }
            if (Overlay != null) Overlay.Visibility = Visibility.Visible;

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
            if (ViewState == null) return;

            // 计算缩放方向和下一个 zoom level
            int nextZoom;
            if (e.Delta > 0)
            {
                nextZoom = ViewState.ZoomLevel + 1;
            }
            else if (e.Delta < 0)
            {
                nextZoom = ViewState.ZoomLevel - 1;
            }
            else
            {
                return;
            }

            // 边界检查：限制最小缩放级别
            if (nextZoom < MinZoom)
            {
                ViewState.ZoomLevel = MinZoom;
                e.Handled = true;
                return;
            }

            // 统一的缩放计算：所有情况都使用固定缩放因子，确保每次缩放幅度一致
            double factor = (e.Delta > 0) ? ZoomFactor : (1.0 / ZoomFactor);
            double oldScale = ViewState.Scale;
            double newScale = oldScale * factor;
            newScale = Clamp(newScale, MinScale, MaxScale);

            if (Math.Abs(newScale - oldScale) < 1e-12)
            {
                e.Handled = true;
                return;
            }

            // 鼠标锚点缩放：使用"总平移"(user + baseFit) 保持中心稳定
            Point mouse = e.GetPosition(this);
            double scaleChange = newScale / oldScale;
            double oldTx = ViewState.OffsetX + _baseFitOffsetX;
            double oldTy = ViewState.OffsetY + _baseFitOffsetY;
            double newTx = mouse.X - scaleChange * (mouse.X - oldTx) - _baseFitOffsetX;
            double newTy = mouse.Y - scaleChange * (mouse.Y - oldTy) - _baseFitOffsetY;

            ViewState.SetView(newScale, newTx, newTy);
            ViewState.ZoomLevel = nextZoom;
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
            ResetViewToFit();
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


