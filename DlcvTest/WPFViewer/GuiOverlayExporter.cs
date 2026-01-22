using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using dlcv_infer_csharp;

namespace DlcvTest.WPFViewer
{
    /// <summary>
    /// GUI叠加导出：使用 WPF 的 Image + OverlayRenderer 进行离屏渲染，输出像素尺寸=原图尺寸。
    /// 说明：
    /// - 不使用当前界面的缩放/平移状态（避免导出为放大后的视图）。
    /// - 依赖 WpfVisualize.Build 生成可绘制元素，再由 OverlayRenderer 绘制。
    /// </summary>
    public static class GuiOverlayExporter
    {
        /// <summary>
        /// 导出渲染模式：HardEdge 用于“硬边更锐”（更接近 OpenCV/Python 风格），Smooth 用于顺滑抗锯齿。
        /// </summary>
        public enum ExportRenderMode
        {
            HardEdge = 0,
            Smooth = 1
        }

        /// <summary>
        /// 渲染“原图 + 推理叠加层”为 BitmapSource（RenderTargetBitmap）。
        /// </summary>
        /// <param name="baseImage">原图（像素尺寸将作为导出尺寸）。</param>
        /// <param name="result">推理结果。</param>
        /// <param name="opt">可视化选项。</param>
        /// <param name="screenLineWidth">导出线宽（像素单位）。</param>
        /// <param name="screenFontSize">导出字号（像素单位）。</param>
        public static BitmapSource Render(BitmapSource baseImage, Utils.CSharpResult result, WpfVisualize.Options opt, double screenLineWidth, double screenFontSize)
        {
            return Render(baseImage, result, opt, screenLineWidth, screenFontSize, ExportRenderMode.HardEdge);
        }

        /// <summary>
        /// 渲染“原图 + 推理叠加层”为 BitmapSource（RenderTargetBitmap）。
        /// </summary>
        public static BitmapSource Render(BitmapSource baseImage, Utils.CSharpResult result, WpfVisualize.Options opt, double screenLineWidth, double screenFontSize, ExportRenderMode mode)
        {
            if (opt == null) opt = new WpfVisualize.Options();
            var vr = WpfVisualize.Build(result, opt);
            return RenderInternal(baseImage, vr, opt, screenLineWidth, screenFontSize, mode);
        }

        /// <summary>
        /// 渲染“原图 + 指定叠加层”为 BitmapSource（RenderTargetBitmap）。
        /// </summary>
        public static BitmapSource RenderWithVisualizeResult(BitmapSource baseImage, WpfVisualize.VisualizeResult visualizeResult, WpfVisualize.Options opt, double screenLineWidth, double screenFontSize)
        {
            return RenderWithVisualizeResult(baseImage, visualizeResult, opt, screenLineWidth, screenFontSize, ExportRenderMode.HardEdge);
        }

        /// <summary>
        /// 渲染“原图 + 指定叠加层”为 BitmapSource（RenderTargetBitmap）。
        /// </summary>
        public static BitmapSource RenderWithVisualizeResult(BitmapSource baseImage, WpfVisualize.VisualizeResult visualizeResult, WpfVisualize.Options opt, double screenLineWidth, double screenFontSize, ExportRenderMode mode)
        {
            return RenderInternal(baseImage, visualizeResult, opt, screenLineWidth, screenFontSize, mode);
        }

        private static BitmapSource RenderInternal(BitmapSource baseImage, WpfVisualize.VisualizeResult visualizeResult, WpfVisualize.Options opt, double screenLineWidth, double screenFontSize, ExportRenderMode mode)
        {
            if (baseImage == null) return null;
            if (opt == null) opt = new WpfVisualize.Options();

            int width = baseImage.PixelWidth;
            int height = baseImage.PixelHeight;
            if (width <= 0 || height <= 0) return null;

            var vr = visualizeResult ?? new WpfVisualize.VisualizeResult { StatusText = string.Empty, StatusIsOk = true };

            // 使用独立的视觉树，避免受到 UI 缩放/平移影响
            var root = new Grid
            {
                Width = width,
                Height = height,
                Background = Brushes.Transparent,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            // 统一一份“导出期望”配置：硬边更锐 or 顺滑抗锯齿
            bool hardEdge = mode == ExportRenderMode.HardEdge;
            if (hardEdge)
            {
                // 导出期望"硬边"：关闭矢量抗锯齿，避免在字边缘出现彩边与模糊
                RenderOptions.SetEdgeMode(root, EdgeMode.Aliased);
                TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(root, TextRenderingMode.Aliased);
                TextOptions.SetTextHintingMode(root, TextHintingMode.Fixed);
            }
            else
            {
                // 顺滑：允许抗锯齿（离屏通常是灰度 AA）                RenderOptions.SetEdgeMode(root, EdgeMode.Unspecified);
                TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
                TextOptions.SetTextHintingMode(root, TextHintingMode.Auto);
            }

            var img = new Image
            {
                Source = baseImage,
                Width = width,
                Height = height,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };
            if (hardEdge)
            {
                // 保险起见：避免任何插值缩放（即使未来改动导致出现缩放）                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                RenderOptions.SetEdgeMode(img, EdgeMode.Aliased);
            }
            else
            {
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                RenderOptions.SetEdgeMode(img, EdgeMode.Unspecified);
            }

            double exportLineWidth = screenLineWidth;
            double exportFontSize = screenFontSize;
            if (hardEdge)
            {
                // 硬边导出：强制整数像素线宽/字号，减少灰度过渡像素                exportLineWidth = Math.Max(1.0, Math.Round(screenLineWidth, 0, MidpointRounding.AwayFromZero));
                exportFontSize = Math.Max(6.0, Math.Round(screenFontSize, 0, MidpointRounding.AwayFromZero));
            }

            var overlay = new OverlayRenderer
            {
                Width = width,
                Height = height,
                SnapsToDevicePixels = true,
                VisualizeResult = vr,
                ViewScale = 1.0, // 导出不抵消缩放：直接按像素绘制                ScreenLineWidth = exportLineWidth,
                ScreenFontSize = exportFontSize,
                TextOutOfBbox = opt.TextOutOfBbox,
                // 硬边导出建议关闭阴影：阴影会显著加重"厚/糊"观感                ShowTextShadow = hardEdge ? false : opt.DisplayTextShadow,
                ShowStatusText = false,
                DrawHudInThisLayer = false,
                IsHitTestVisible = false
            };
            overlay.HardEdgeExport = hardEdge;
            if (hardEdge)
            {
                RenderOptions.SetEdgeMode(overlay, EdgeMode.Aliased);
                RenderOptions.SetBitmapScalingMode(overlay, BitmapScalingMode.NearestNeighbor);
                TextOptions.SetTextFormattingMode(overlay, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(overlay, TextRenderingMode.Aliased);
                TextOptions.SetTextHintingMode(overlay, TextHintingMode.Fixed);
            }
            else
            {
                RenderOptions.SetEdgeMode(overlay, EdgeMode.Unspecified);
                RenderOptions.SetBitmapScalingMode(overlay, BitmapScalingMode.HighQuality);
                TextOptions.SetTextFormattingMode(overlay, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(overlay, TextRenderingMode.Grayscale);
                TextOptions.SetTextHintingMode(overlay, TextHintingMode.Auto);
            }

            root.Children.Add(img);
            root.Children.Add(overlay);

            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            if (rtb.CanFreeze) rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// 左右拼接两张图片（不缩放）。
        /// </summary>
        public static BitmapSource ConcatenateHorizontal(BitmapSource left, BitmapSource right)
        {
            if (left == null && right == null) return null;
            if (left == null) return right;
            if (right == null) return left;

            int width = left.PixelWidth + right.PixelWidth;
            int height = Math.Max(left.PixelHeight, right.PixelHeight);
            if (width <= 0 || height <= 0) return null;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

                double leftY = (height - left.PixelHeight) / 2.0;
                double rightY = (height - right.PixelHeight) / 2.0;

                dc.DrawImage(left, new Rect(0, leftY, left.PixelWidth, left.PixelHeight));
                dc.DrawImage(right, new Rect(left.PixelWidth, rightY, right.PixelWidth, right.PixelHeight));
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            if (rtb.CanFreeze) rtb.Freeze();
            return rtb;
        }

        public static void SavePng(BitmapSource source, string path)
        {
            if (source == null) return;
            if (string.IsNullOrWhiteSpace(path)) return;

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                encoder.Save(fs);
            }
        }
    }
}


