using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using OpenCvSharp;
using Newtonsoft.Json.Linq;
using DlcvTest.Properties;
using DlcvTest.WPFViewer;

namespace DlcvTest
{
    public partial class MainWindow
    {
        // Mat �?Bitmap（用于保存文件）
        private Bitmap MatToBitmap(Mat mat)
        {
            return OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);
        }

        // 统一的文本绘制辅助方�?
        private void DrawUnifiedTextOnMat(Graphics g, Font font, string text, float bboxX, float bboxY, float bboxWidth, float bboxHeight, System.Drawing.Color textColor)
        {
            SizeF textSize = g.MeasureString(text, font);
            float textX;
            float textY;
            if (Settings.Default.ShowTextOutOfBboxPane)
            {
                textX = bboxX;
                textY = bboxY - textSize.Height - 2;
                if (textY < 0) textY = bboxY + 3;
            }
            else
            {
                textX = bboxX + 3;
                textY = bboxY + (int)Math.Round(12 * (font.Size / 12.0f));
            }

            SolidBrush backgroundBrush;
            SolidBrush textBrush;

            if (Settings.Default.ShowTextShadowPane)
            {
                // 文字阴影：背景是阴影色（深灰色），文字是白色
                backgroundBrush = new SolidBrush(System.Drawing.Color.FromArgb(255, 64, 64, 64));
                textBrush = new SolidBrush(System.Drawing.Color.White);
            }
            else
            {
                // 正常：背景是半透明黑色，绿色文�?
                backgroundBrush = new SolidBrush(System.Drawing.Color.FromArgb(160, 0, 0, 0));
                textBrush = new SolidBrush(textColor);
            }

            using (backgroundBrush)
            using (textBrush)
            {
                // 绘制背景
                g.FillRectangle(backgroundBrush, textX, textY, textSize.Width, textSize.Height);
                // 绘制文字
                g.DrawString(text, font, textBrush, textX, textY);
            }
        }

        private void DrawChineseTextOnMat(Mat mat, JArray detections, float confidenceThreshold = 0.5f, bool drawScore = true)
        {
            // 如果禁用了文本显示，直接返回
            if (!Settings.Default.ShowTextPane) return;

            if (mat == null || mat.Empty() || detections == null || detections.Count == 0) return;

            // 优化：使�?lockbits 方式直接操作像素数据，减少转换开销
            // 但由于需�?GDI+ 绘制文本，仍需�?Mat->Bitmap 转换
            // 优化点：复用 Font 对象，减少对象创建开销
            Bitmap bitmap = null;
            try
            {
                bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);

                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    // 绘制启用抗锯�?
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // 文本抗锯�?
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    // 高质量插�?
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    // 复用 Font 对象，避免在循环中重复创�?
                    float fontSize = (float)Settings.Default.FontSize;
                    using (System.Drawing.Font font = new System.Drawing.Font("Microsoft YaHei", fontSize, System.Drawing.FontStyle.Bold))
                    {
                        // 预创建常用的 Brush 对象
                        using (SolidBrush shadowBrush = new SolidBrush(System.Drawing.Color.Black))
                        using (SolidBrush backgroundBrush = new SolidBrush(System.Drawing.Color.FromArgb(160, 0, 0, 0)))
                        {
                            // 获取字体颜色
                            System.Drawing.Color fontColor;
                            try
                            {
                                var wpfColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Settings.Default.FontColor);
                                fontColor = System.Drawing.Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B);
                            }
                            catch
                            {
                                fontColor = System.Drawing.Color.LimeGreen; // 默认绿色
                            }

                            using (SolidBrush textBrush = new SolidBrush(fontColor))
                            {
                                foreach (var item in detections)
                                {
                                    var det = item as JObject;
                                    if (det == null) continue;

                                    float score = det["score"]?.Value<float>() ?? 0;
                                    if (score < confidenceThreshold) continue;

                                    string label = det["category_name"]?.ToString() ?? "";

                                    // 根据设置决定文本内容
                                    string text = label;
                                    if (drawScore && Settings.Default.ShowScorePane)
                                    {
                                        text = string.IsNullOrEmpty(label) ? $"{score * 100:F1}" : $"{label}: {score * 100:F1}";
                                    }

                                    var bbox = det["bbox"]?.ToObject<double[]>();
                                    if (bbox == null || bbox.Length < 4) continue;

                                    float x = (float)bbox[0];
                                    float y = (float)bbox[1];
                                    float w = (float)bbox[2];
                                    float h = (float)bbox[3];

                                    // 绘制bbox填充（在文本之前�?
                                    DrawBBoxFillOnMat(g, x, y, w, h);

                                    SizeF textSize = g.MeasureString(text, font);

                                    // 根据设置决定文本位置
                                    float textX;
                                    float textY;
                                    if (Settings.Default.ShowTextOutOfBboxPane)
                                    {
                                        // 文本显示在bbox外（上方�?
                                        textX = x;
                                        textY = y - textSize.Height - 2;
                                        if (textY < 0) textY = y + 2;
                                    }
                                    else
                                    {
                                        // 文本显示在bbox�?
                                        textX = x + 3;
                                        textY = y + (int)Math.Round(12 * (fontSize / 12.0f));
                                    }

                                    // 绘制文本阴影（如果启用）
                                    if (Settings.Default.ShowTextShadowPane)
                                    {
                                        g.DrawString(text, font, shadowBrush, textX + 1, textY + 1);
                                    }

                                    // 绘制半透明黑色背景
                                    g.FillRectangle(backgroundBrush, textX, textY, textSize.Width, textSize.Height);

                                    // 绘制文字（使用设置的字体颜色�?
                                    g.DrawString(text, font, textBrush, textX, textY);
                                }
                            }
                        }
                    }
                }

                // 优化：直接使�?ToMat 并复制，避免额外�?using �?
                var matFromBitmap = OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap);
                matFromBitmap.CopyTo(mat);
                matFromBitmap.Dispose();
            }
            finally
            {
                // 确保 Bitmap 被释�?
                bitmap?.Dispose();
            }
        }

        // 更新图片布局（根�?显示原图控件"设置�?
        public void UpdateImageLayout()
        {
            try
            {
                bool showOriginalPane = Settings.Default.ShowOriginalPane;

                // 检�?UI 元素是否存在
                if (border1 == null || border2 == null || dividerLine == null)
                {
                    System.Diagnostics.Debug.WriteLine("UpdateImageLayout: UI 元素未初始化");
                    return;
                }

                if (showOriginalPane)
                {
                    // 显示双图布局
                    border1.Visibility = Visibility.Visible;
                    dividerLine.Visibility = Visibility.Visible;
                    border2.Visibility = Visibility.Visible;
                    border2.SetValue(Grid.ColumnProperty, 2);
                    border2.SetValue(Grid.ColumnSpanProperty, 1);
                }
                else
                {
                    // 隐藏左侧和分割线，右侧占�?
                    border1.Visibility = Visibility.Collapsed;
                    dividerLine.Visibility = Visibility.Collapsed;
                    border2.Visibility = Visibility.Visible;
                    border2.SetValue(Grid.ColumnProperty, 0);
                    border2.SetValue(Grid.ColumnSpanProperty, 3);
                }

                // 注意：ShowContours 设置用于控制可视化中的轮廓显示，不控制UI布局
                // 如果需要控�?borderEdges 的显示，可以在这里添加逻辑
            }
            catch (Exception ex)
            {
                // 捕获异常，防止布局更新时崩�?
                System.Diagnostics.Debug.WriteLine($"UpdateImageLayout 失败: {ex.Message}");
            }
        }

        private void ApplyWpfViewerOptions(WpfImageViewer viewer, double threshold)
        {
            if (viewer == null) return;

            BuildWpfOptions(threshold, out var options, out double screenLineWidth, out double screenFontSize);
            ApplyWpfOptionsToViewer(viewer, options, screenLineWidth, screenFontSize);
        }

        private void BuildWpfOptions(double threshold, out WpfVisualize.Options options, out double screenLineWidth, out double screenFontSize)
        {
            options = new WpfVisualize.Options
            {
                ConfidenceThreshold = threshold,
                DisplayBbox = Settings.Default.ShowBBoxPane,
                DisplayMask = Settings.Default.ShowMaskPane,
                DisplayContours = Settings.Default.ShowContours,
                DisplayText = Settings.Default.ShowTextPane,
                DisplayScore = Settings.Default.ShowScorePane,
                DisplayTextShadow = Settings.Default.ShowTextShadowPane,
                TextOutOfBbox = Settings.Default.ShowTextOutOfBboxPane,
                DisplayCenterPoint = Settings.Default.ShowCenterPoint,
                BBoxFillEnabled = Settings.Default.BBoxFillEnabled,
                BBoxFillOpacity = Settings.Default.BBoxFillOpacity
            };

            var fixedColor = SafeParseColor(Settings.Default.BBoxBorderColor, System.Windows.Media.Colors.Red);
            options.BboxColorOk = fixedColor;
            options.BboxColorNg = fixedColor;

            var fontColor = SafeParseColor(Settings.Default.FontColor, System.Windows.Media.Colors.White);
            options.FontColor = fontColor;

            options.MaskColor = System.Windows.Media.Colors.LimeGreen;
            options.MaskAlpha = 128;

            double lineWidth = 2.0;
            try { lineWidth = Settings.Default.BBoxBorderThickness; } catch { lineWidth = 2.0; }
            screenLineWidth = Math.Max(1.0, Math.Min(20.0, lineWidth));

            double fontSize = 24.0;
            try { fontSize = Settings.Default.FontSize; } catch { fontSize = 24.0; }
            screenFontSize = Math.Max(10.0, Math.Min(48.0, fontSize));
        }

        private static void ApplyWpfOptionsToViewer(WpfImageViewer viewer, WpfVisualize.Options options, double screenLineWidth, double screenFontSize)
        {
            if (viewer == null || options == null) return;

            viewer.Options.ConfidenceThreshold = options.ConfidenceThreshold;
            viewer.Options.DisplayBbox = options.DisplayBbox;
            viewer.Options.DisplayMask = options.DisplayMask;
            viewer.Options.DisplayContours = options.DisplayContours;
            viewer.Options.DisplayText = options.DisplayText;
            viewer.Options.DisplayScore = options.DisplayScore;
            viewer.Options.DisplayTextShadow = options.DisplayTextShadow;
            viewer.Options.TextOutOfBbox = options.TextOutOfBbox;
            viewer.Options.DisplayCenterPoint = options.DisplayCenterPoint;
            viewer.Options.BBoxFillEnabled = options.BBoxFillEnabled;
            viewer.Options.BBoxFillOpacity = options.BBoxFillOpacity;
            viewer.Options.BboxColorOk = options.BboxColorOk;
            viewer.Options.BboxColorNg = options.BboxColorNg;
            viewer.Options.FontColor = options.FontColor;
            viewer.Options.MaskColor = options.MaskColor;
            viewer.Options.MaskAlpha = options.MaskAlpha;

            viewer.ScreenLineWidth = screenLineWidth;
            viewer.ScreenFontSize = screenFontSize;
        }

        private static System.Windows.Media.Color SafeParseColor(string colorText, System.Windows.Media.Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(colorText)) return fallback;
                var obj = System.Windows.Media.ColorConverter.ConvertFromString(colorText);
                if (obj is System.Windows.Media.Color c) return c;
            }
            catch { }
            return fallback;
        }

        // 绘制bbox填充
        private void DrawBBoxFillOnMat(Graphics g, float bboxX, float bboxY, float bboxWidth, float bboxHeight)
        {
            if (!Settings.Default.BBoxFillEnabled) return;

            try
            {
                // 解析bbox框线颜色
                System.Drawing.Color borderColor;
                try
                {
                    var wpfColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Settings.Default.BBoxBorderColor);
                    borderColor = System.Drawing.Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B);
                }
                catch
                {
                    // 默认红色
                    borderColor = System.Drawing.Color.Red;
                }

                // 根据BBoxFillOpacity计算alpha值（0-100映射�?-255�?
                double opacity = Math.Max(0.0, Math.Min(100.0, Settings.Default.BBoxFillOpacity));
                byte alpha = (byte)(opacity / 100.0 * 255);

                // 使用框线颜色的RGB，但使用计算出的alpha�?
                var fillColor = System.Drawing.Color.FromArgb(alpha, borderColor.R, borderColor.G, borderColor.B);

                using (var fillBrush = new SolidBrush(fillColor))
                {
                    g.FillRectangle(fillBrush, bboxX, bboxY, bboxWidth, bboxHeight);
                }
            }
            catch
            {
                // 填充绘制失败，静默处�?
            }
        }
    }
}

