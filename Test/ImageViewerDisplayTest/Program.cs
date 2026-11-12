using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;
using dlcv_infer_csharp;
using DLCV;
using Newtonsoft.Json;
using OpenCvSharp;

namespace ImageViewerDisplayTest
{
    // 大图显示回归：底图超过 GDI+ 整幅缩放上限时，控件应先转预览位图，框与整图 Mask 仍按原图坐标绘制。
    internal static class Program
    {
        private const int PanelWidth = 1200;
        private const int PanelHeight = 800;
        private const int SourceWidth = 24000;
        private const int SourceHeight = 24000;
        private const int BoxX = 12000;
        private const int BoxY = 12000;
        private const int BoxWidth = 4000;
        private const int BoxHeight = 3000;

        [STAThread]
        private static int Main(string[] args)
        {
            // 绘制异常直接抛回调用方，避免回归时停在默认的 .NET 未处理异常对话框。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);

            string outputDirectory = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.Combine(Path.GetTempPath(), "OpenIVS-ImageViewerDisplayTest");
            Directory.CreateDirectory(outputDirectory);

            var checks = new Dictionary<string, bool>();
            var failures = new List<string>();
            string status = "failed";
            Exception error = null;

            try
            {
                using (var viewer = new ImageViewer { Size = new System.Drawing.Size(PanelWidth, PanelHeight) })
                {
                    viewer.CreateControl();

                    using (var image = new Mat(SourceHeight, SourceWidth, MatType.CV_8UC3, new Scalar(30, 60, 90)))
                    using (var fullImageMask = new Mat(SourceHeight, SourceWidth, MatType.CV_8UC1, Scalar.All(0)))
                    {
                        Cv2.Rectangle(
                            image,
                            new OpenCvSharp.Rect(SourceWidth / 3, SourceHeight / 3, SourceWidth / 4, SourceHeight / 4),
                            new Scalar(200, 200, 200),
                            80);
                        using (var maskRegion = new Mat(fullImageMask, new OpenCvSharp.Rect(BoxX, BoxY, BoxWidth, BoxHeight)))
                        {
                            maskRegion.SetTo(Scalar.All(255));
                        }

                        var objects = new List<Utils.CSharpObjectResult>
                        {
                            new Utils.CSharpObjectResult(
                                1,
                                "框",
                                0.90f,
                                BoxWidth * BoxHeight,
                                new List<double> { BoxX, BoxY, BoxWidth, BoxHeight },
                                true,
                                fullImageMask,
                                true,
                                false,
                                -100f,
                                null),
                        };
                        var sample = new Utils.CSharpSampleResult(objects, false, "NG");
                        var inferenceResult = new Utils.CSharpResult(
                            new List<Utils.CSharpSampleResult> { sample }, "{}");

                        viewer.UpdateImageAndResult(image, inferenceResult);

                        float fitScale = Math.Min((float)PanelWidth / SourceWidth, (float)PanelHeight / SourceHeight);
                        float fitOffsetX = (PanelWidth - SourceWidth * fitScale) / 2f;
                        float fitOffsetY = (PanelHeight - SourceHeight * fitScale) / 2f;
                        float fitBoxLeft = fitOffsetX + BoxX * fitScale;
                        float fitBoxTop = fitOffsetY + BoxY * fitScale;
                        float fitBoxCenterX = fitBoxLeft + BoxWidth * fitScale / 2f;
                        float fitBoxCenterY = fitBoxTop + BoxHeight * fitScale / 2f;

                        string fitPath = Path.Combine(outputDirectory, "fixture-fit.png");
                        using (Bitmap fitBitmap = Render(viewer, fitPath))
                        {
                            checks["fit_image_drawn"] = IsImageDrawn(fitBitmap, fitBoxCenterX, fitBoxCenterY);
                            Check(checks, failures, "fit_image_drawn", "适配视图没有绘制底图");
                            bool boxDrawn = HasColorNear(fitBitmap, fitBoxLeft, fitBoxTop, 3, c => c.R > 190 && c.G < 90 && c.B < 90);
                            checks["fit_box_position"] = boxDrawn;
                            Check(checks, failures, "fit_box_position", "适配视图的框没有画在预期位置");
                            bool maskDrawn = HasColorNear(fitBitmap, fitBoxCenterX, fitBoxCenterY, 3, c => c.G > 120 && c.R < 140 && c.B < 120);
                            checks["fit_full_mask"] = maskDrawn;
                            Check(checks, failures, "fit_full_mask", "适配视图的整图 Mask 没有画到框内");
                        }

                        float regionX = BoxX - BoxWidth / 4f;
                        float regionY = BoxY - BoxHeight / 4f;
                        float regionWidth = BoxWidth * 1.5f;
                        float regionHeight = BoxHeight * 1.5f;
                        viewer.FocusImageRegion(new RectangleF(regionX, regionY, regionWidth, regionHeight));
                        float zoomScale = Math.Min((float)PanelWidth / regionWidth, (float)PanelHeight / regionHeight);
                        float regionCenterX = regionX + regionWidth / 2f;
                        float regionCenterY = regionY + regionHeight / 2f;
                        float zoomBoxLeft = PanelWidth / 2f + (BoxX - regionCenterX) * zoomScale;
                        float zoomBoxTop = PanelHeight / 2f + (BoxY - regionCenterY) * zoomScale;

                        string zoomPath = Path.Combine(outputDirectory, "fixture-zoom.png");
                        using (Bitmap zoomBitmap = Render(viewer, zoomPath))
                        {
                            bool boxDrawn = HasColorNear(zoomBitmap, zoomBoxLeft, zoomBoxTop, 3, c => c.R > 190 && c.G < 90 && c.B < 90);
                            checks["zoom_box_position"] = boxDrawn;
                            Check(checks, failures, "zoom_box_position", "放大视图的框没有画在预期位置");
                            bool imageDrawn = IsImageDrawn(zoomBitmap, PanelWidth / 2, PanelHeight / 2);
                            checks["zoom_image_drawn"] = imageDrawn;
                            Check(checks, failures, "zoom_image_drawn", "放大视图没有绘制底图");
                        }
                    }
                }

                bool allPassed = checks.Count > 0;
                foreach (bool value in checks.Values)
                {
                    if (!value)
                    {
                        allPassed = false;
                    }
                }
                status = allPassed ? "passed" : "failed";
            }
            catch (Exception ex)
            {
                error = ex;
                failures.Add(ex.ToString());
            }

            var result = new Dictionary<string, object>
            {
                ["status"] = status,
                ["checks"] = checks,
                ["failures"] = failures,
                ["output_directory"] = outputDirectory,
                ["source_size"] = new[] { SourceWidth, SourceHeight },
                ["panel_size"] = new[] { PanelWidth, PanelHeight },
                ["error"] = error == null ? null : error.ToString(),
            };
            string resultPath = Path.Combine(outputDirectory, "result.json");
            File.WriteAllText(resultPath, JsonConvert.SerializeObject(result, Formatting.Indented), new UTF8Encoding(false));
            Console.WriteLine(File.ReadAllText(resultPath));
            return status == "passed" ? 0 : 1;
        }

        private static void Check(IDictionary<string, bool> checks, IList<string> failures, string name, string message)
        {
            if (!checks.TryGetValue(name, out bool passed) || !passed)
            {
                failures.Add(message);
            }
        }

        private static Bitmap Render(ImageViewer viewer, string path)
        {
            var bitmap = new Bitmap(PanelWidth, PanelHeight);
            try
            {
                viewer.DrawToBitmap(bitmap, new Rectangle(0, 0, PanelWidth, PanelHeight));
                bitmap.Save(path, ImageFormat.Png);
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static bool IsImageDrawn(Bitmap bitmap, float x, float y)
        {
            Color background = bitmap.GetPixel(2, 2);
            int left = Math.Max(0, (int)Math.Round(x) - 8);
            int top = Math.Max(0, (int)Math.Round(y) - 8);
            return HasPixel(bitmap, left, top, 16, c => c != background);
        }

        private static bool HasColorNear(Bitmap bitmap, float x, float y, int radius, Func<Color, bool> predicate)
        {
            return HasPixel(bitmap, (int)Math.Round(x) - radius, (int)Math.Round(y) - radius, radius * 2 + 1, predicate);
        }

        private static bool HasPixel(Bitmap bitmap, int left, int top, int size, Func<Color, bool> predicate)
        {
            for (int py = top; py < top + size; py++)
            {
                for (int px = left; px < left + size; px++)
                {
                    if (px < 0 || py < 0 || px >= bitmap.Width || py >= bitmap.Height)
                    {
                        continue;
                    }
                    if (predicate(bitmap.GetPixel(px, py)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
