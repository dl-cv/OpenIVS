using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace DlcvTest.WPFViewer
{
    /// <summary>
    /// 叠加层渲染器：在图像坐标系中绘制 bbox/旋转框/mask/文字等。
    /// 注意：外层会对 WorldLayer 做缩放/平移，因此这里的线宽/字号需要用 1/scale 抵消缩放，保证屏幕可读性。
    /// </summary>
    public sealed class OverlayRenderer : FrameworkElement
    {
        /// <summary>
        /// 导出硬边模式：用于 RenderTargetBitmap 离屏导出时获得更"硬边更锐"的结果。
        /// 默认 false（屏幕显示更偏顺滑/可读）。
        /// </summary>
        public bool HardEdgeExport
        {
            get => (bool)GetValue(HardEdgeExportProperty);
            set => SetValue(HardEdgeExportProperty, value);
        }
        public static readonly DependencyProperty HardEdgeExportProperty =
            DependencyProperty.Register(
                nameof(HardEdgeExport),
                typeof(bool),
                typeof(OverlayRenderer),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public WpfVisualize.VisualizeResult VisualizeResult
        {
            get => (WpfVisualize.VisualizeResult)GetValue(VisualizeResultProperty);
            set => SetValue(VisualizeResultProperty, value);
        }
        public static readonly DependencyProperty VisualizeResultProperty =
            DependencyProperty.Register(
                nameof(VisualizeResult),
                typeof(WpfVisualize.VisualizeResult),
                typeof(OverlayRenderer),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public double ViewScale
        {
            get => (double)GetValue(ViewScaleProperty);
            set => SetValue(ViewScaleProperty, value);
        }
        public static readonly DependencyProperty ViewScaleProperty =
            DependencyProperty.Register(
                nameof(ViewScale),
                typeof(double),
                typeof(OverlayRenderer),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool ShowStatusText
        {
            get => (bool)GetValue(ShowStatusTextProperty);
            set => SetValue(ShowStatusTextProperty, value);
        }
        public static readonly DependencyProperty ShowStatusTextProperty =
            DependencyProperty.Register(
                nameof(ShowStatusText),
                typeof(bool),
                typeof(OverlayRenderer),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        // 屏幕层（HUD）状态：在 WpfImageViewer 外部用 TextBlock 显示也可以；这里保留能力。
        public bool DrawHudInThisLayer
        {
            get => (bool)GetValue(DrawHudInThisLayerProperty);
            set => SetValue(DrawHudInThisLayerProperty, value);
        }
        public static readonly DependencyProperty DrawHudInThisLayerProperty =
            DependencyProperty.Register(
                nameof(DrawHudInThisLayer),
                typeof(bool),
                typeof(OverlayRenderer),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        // 基础屏幕字号/线宽（最终屏幕效果）；实际绘制时会除以 ViewScale 以抵消缩放。
        public double ScreenFontSize
        {
            get => (double)GetValue(ScreenFontSizeProperty);
            set => SetValue(ScreenFontSizeProperty, value);
        }
        public static readonly DependencyProperty ScreenFontSizeProperty =
            DependencyProperty.Register(
                nameof(ScreenFontSize),
                typeof(double),
                typeof(OverlayRenderer),
                new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double ScreenLineWidth
        {
            get => (double)GetValue(ScreenLineWidthProperty);
            set => SetValue(ScreenLineWidthProperty, value);
        }
        public static readonly DependencyProperty ScreenLineWidthProperty =
            DependencyProperty.Register(
                nameof(ScreenLineWidth),
                typeof(double),
                typeof(OverlayRenderer),
                new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool TextOutOfBbox
        {
            get => (bool)GetValue(TextOutOfBboxProperty);
            set => SetValue(TextOutOfBboxProperty, value);
        }
        public static readonly DependencyProperty TextOutOfBboxProperty =
            DependencyProperty.Register(
                nameof(TextOutOfBbox),
                typeof(bool),
                typeof(OverlayRenderer),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool ShowTextShadow
        {
            get => (bool)GetValue(ShowTextShadowProperty);
            set => SetValue(ShowTextShadowProperty, value);
        }
        public static readonly DependencyProperty ShowTextShadowProperty =
            DependencyProperty.Register(
                nameof(ShowTextShadow),
                typeof(bool),
                typeof(OverlayRenderer),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var vr = VisualizeResult;
            if (vr == null) return;

            double scale = ViewScale;
            if (!(scale > 0)) scale = 1.0;

            // 抵消缩放：保证屏幕看到的线宽/字号稳定
            double worldLine = ScreenLineWidth / scale;
            double worldFont = ScreenFontSize / scale;
            double shadowOffset = 1.0 / scale;

            bool hardEdge = HardEdgeExport;
            int strokePx = hardEdge ? Math.Max(1, (int)Math.Round(worldLine, 0, MidpointRounding.AwayFromZero)) : -1;
            double penThickness = hardEdge ? strokePx : Math.Max(0.5, worldLine);
            // WPF 的 stroke 居中绘制：奇数像素线宽需要 0.5 偏移才能更“像素对齐”
            double strokeAlignOffset = hardEdge && (strokePx % 2 == 1) ? 0.5 : 0.0;

            if (hardEdge)
            {
                // 硬边导出：字号/阴影偏移也取整，避免亚像素导致灰度过渡像素
                worldFont = Math.Max(6.0, Math.Round(worldFont, 0, MidpointRounding.AwayFromZero));
                shadowOffset = 1.0; // 导出固定 1px 阴影偏移（通常导出时已禁用阴影）
            }

            // 文字绘制 DPI：屏幕显示走系统 DPI；离屏导出强制 1.0（配合 RenderTargetBitmap 96dpi），避免高 DPI 系统导致导出发虚
            double pixelsPerDip = 1.0;
            if (!hardEdge)
            {
                try
                {
                    pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                }
                catch { pixelsPerDip = 1.0; }
            }

            // 1) 绘制每个目标
            if (vr.Items != null)
            {
                foreach (var item in vr.Items)
                {
                    if (item == null) continue;

                    var strokeBrush = new SolidColorBrush(item.StrokeColor);
                    if (strokeBrush.CanFreeze) strokeBrush.Freeze();
                    var pen = new Pen(strokeBrush, penThickness) { LineJoin = PenLineJoin.Miter };
                    if (pen.CanFreeze) pen.Freeze();

                    // 可选：GuidelineSet 进一步锁像素边界（硬边导出时）
                    if (hardEdge)
                    {
                        var gs = new GuidelineSet();
                        try
                        {
                            // 以 bboxRect 为主导，给 WPF 一个明确的像素对齐“参考线”
                            var b = SnapRect(item.BboxRect);
                            gs.GuidelinesX.Add(b.Left + strokeAlignOffset);
                            gs.GuidelinesX.Add(b.Right + strokeAlignOffset);
                            gs.GuidelinesY.Add(b.Top + strokeAlignOffset);
                            gs.GuidelinesY.Add(b.Bottom + strokeAlignOffset);
                            dc.PushGuidelineSet(gs);
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    // bbox 填充（先画，作为底色）
                    if (item.FillEnabled && item.FillAlpha > 0)
                    {
                        var fillColor = Color.FromArgb(item.FillAlpha, item.StrokeColor.R, item.StrokeColor.G, item.StrokeColor.B);
                        var fillBrush = new SolidColorBrush(fillColor);
                        if (fillBrush.CanFreeze) fillBrush.Freeze();
                        dc.DrawRectangle(fillBrush, null, hardEdge ? SnapRect(item.BboxRect) : item.BboxRect);
                    }

                    // mask（先画）
                    if (item.MaskContours != null && item.MaskContours.Count > 0 && item.MaskAlpha > 0)
                    {
                        var maskColor = Color.FromArgb(item.MaskAlpha, item.MaskColor.R, item.MaskColor.G, item.MaskColor.B);
                        var maskBrush = new SolidColorBrush(maskColor);
                        if (maskBrush.CanFreeze) maskBrush.Freeze();

                        foreach (var contour in item.MaskContours)
                        {
                            if (contour == null || contour.Length < 3) continue;
                            var pts = contour;
                            if (hardEdge)
                            {
                                pts = contour.Select(p => SnapPoint(p, 0.0)).ToArray();
                            }
                            var geo = new StreamGeometry();
                            using (var ctx = geo.Open())
                            {
                                ctx.BeginFigure(pts[0], isFilled: true, isClosed: true);
                                ctx.PolyLineTo(pts.Skip(1).ToArray(), isStroked: true, isSmoothJoin: false);
                            }
                            if (geo.CanFreeze) geo.Freeze();
                            dc.DrawGeometry(maskBrush, null, geo);
                        }
                    }
                    else if (item.MaskBitmap != null)
                    {
                        // 兼容旧逻辑：mask 贴图按 bboxRect 绘制
                        dc.DrawImage(item.MaskBitmap, hardEdge ? SnapRect(item.BboxRect) : item.BboxRect);
                    }

                    // bbox / rotbox
                    if (item.Polygon != null && item.Polygon.Length >= 2)
                    {
                        var pts = item.Polygon;
                        if (hardEdge)
                        {
                            pts = item.Polygon.Select(p => SnapPoint(p, strokeAlignOffset)).ToArray();
                        }
                        var geo = new StreamGeometry();
                        using (var ctx = geo.Open())
                        {
                            ctx.BeginFigure(pts[0], isFilled: false, isClosed: true);
                            ctx.PolyLineTo(pts.Skip(1).ToArray(), isStroked: true, isSmoothJoin: false);
                        }
                        if (geo.CanFreeze) geo.Freeze();
                        dc.DrawGeometry(null, pen, geo);
                    }

                    // contours（可选）
                    if (item.Contours != null)
                    {
                        foreach (var c in item.Contours)
                        {
                            if (c == null || c.Length < 3) continue;
                            var pts = c;
                            if (hardEdge)
                            {
                                pts = c.Select(p => SnapPoint(p, strokeAlignOffset)).ToArray();
                            }
                            var geo = new StreamGeometry();
                            using (var ctx = geo.Open())
                            {
                                ctx.BeginFigure(pts[0], isFilled: false, isClosed: true);
                                ctx.PolyLineTo(pts.Skip(1).ToArray(), isStroked: true, isSmoothJoin: false);
                            }
                            if (geo.CanFreeze) geo.Freeze();
                            dc.DrawGeometry(null, pen, geo);
                        }
                    }

                    // 文本
                    if (!string.IsNullOrEmpty(item.LabelText))
                    {
                        var fontBrush = new SolidColorBrush(item.FontColor);
                        if (fontBrush.CanFreeze) fontBrush.Freeze();
                        
                        var typeface = new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                        var ft = new FormattedText(
                            item.LabelText,
                            CultureInfo.CurrentUICulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            Math.Max(6.0, worldFont),
                            fontBrush,
                            pixelsPerDip);

                        // 文本位置：与 Visualize.cs 类似，默认放在 bbox 左上（或外侧）
                        double minX = item.Polygon != null && item.Polygon.Length > 0 ? item.Polygon.Min(p => p.X) : item.BboxRect.X;
                        double minY = item.Polygon != null && item.Polygon.Length > 0 ? item.Polygon.Min(p => p.Y) : item.BboxRect.Y;

                        double tx = minX;
                        double ty;
                        if (TextOutOfBbox)
                        {
                            ty = minY - ft.Height - (2.0 / scale);
                            if (ty < 0) ty = minY + (2.0 / scale);
                        }
                        else
                        {
                            // 放在框内
                            ty = minY + (2.0 / scale);
                        }

                        if (hardEdge)
                        {
                            tx = SnapScalar(tx);
                            ty = SnapScalar(ty);
                        }

                        // 背景
                        var bgBrush = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
                        if (bgBrush.CanFreeze) bgBrush.Freeze();
                        var bgRect = new Rect(tx, ty, ft.Width, ft.Height);
                        if (hardEdge)
                        {
                            // 背景也尽量对齐像素边界；宽高用 Ceiling 避免边缘被裁
                            bgRect = new Rect(
                                SnapScalar(bgRect.X),
                                SnapScalar(bgRect.Y),
                                Math.Ceiling(bgRect.Width),
                                Math.Ceiling(bgRect.Height));
                        }
                        dc.DrawRectangle(bgBrush, null, bgRect);

                        // 阴影（轻量）
                        if (ShowTextShadow)
                        {
                            var shadowBrush = new SolidColorBrush(Colors.Black);
                            if (shadowBrush.CanFreeze) shadowBrush.Freeze();
                            var ftShadow = new FormattedText(
                                item.LabelText,
                                CultureInfo.CurrentUICulture,
                                FlowDirection.LeftToRight,
                                typeface,
                                Math.Max(6.0, worldFont),
                                shadowBrush,
                                pixelsPerDip);
                            dc.DrawText(ftShadow, new Point(tx + shadowOffset, ty + shadowOffset));
                        }

                        dc.DrawText(ft, new Point(tx, ty));
                    }

                    // 中心点十字
                    if (item.CenterPoint.HasValue)
                    {
                        var cp = item.CenterPoint.Value;
                        double crossSize = Math.Min(item.BboxRect.Width, item.BboxRect.Height) * 0.2;
                        
                        // 水平线
                        dc.DrawLine(pen, 
                            new System.Windows.Point(cp.X - crossSize, cp.Y),
                            new System.Windows.Point(cp.X + crossSize, cp.Y));
                        
                        // 垂直线
                        dc.DrawLine(pen,
                            new System.Windows.Point(cp.X, cp.Y - crossSize),
                            new System.Windows.Point(cp.X, cp.Y + crossSize));
                    }

                    if (hardEdge)
                    {
                        // 与 PushGuidelineSet 成对弹栈（仅硬边导出时）
                        try { dc.Pop(); } catch { }
                    }
                }
            }

            // 2) 状态文本（在图像坐标里画时会随缩放平移；推荐用外部 HUD 画）
            if (DrawHudInThisLayer && ShowStatusText)
            {
                var txt = vr.StatusText ?? string.Empty;
                var brush = new SolidColorBrush(vr.StatusIsOk ? Colors.LimeGreen : Colors.Red);
                if (brush.CanFreeze) brush.Freeze();

                var typeface = new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                var ft = new FormattedText(
                    txt,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    24.0 / scale,
                    brush,
                    pixelsPerDip);

                dc.DrawText(ft, new Point(10.0 / scale, 10.0 / scale));
            }
        }

        private static double SnapScalar(double v)
        {
            return Math.Round(v, 0, MidpointRounding.AwayFromZero);
        }

        private static Point SnapPoint(Point p, double strokeAlignOffset)
        {
            return new Point(SnapScalar(p.X) + strokeAlignOffset, SnapScalar(p.Y) + strokeAlignOffset);
        }

        private static Rect SnapRect(Rect r)
        {
            // 用 floor/ceil，保证对齐同时尽量不缩小目标区域
            double left = Math.Floor(r.Left);
            double top = Math.Floor(r.Top);
            double right = Math.Ceiling(r.Right);
            double bottom = Math.Ceiling(r.Bottom);
            return new Rect(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));
        }
    }
}


