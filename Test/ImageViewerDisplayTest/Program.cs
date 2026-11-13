using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
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
    // 检查原图细节、可见区域的结果位置及原尺寸导出。
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
            var timings = new Dictionary<string, double>();

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

                        image.Set(17000, 20000, new Vec3b(255, 255, 255));
                        viewer.UpdateImageAndResult(image, inferenceResult);
                        checks["source_size_preserved"] = viewer.image.Width == SourceWidth && viewer.image.Height == SourceHeight;

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
                            checks["fit_image_drawn"] = HasColorNear(fitBitmap, fitOffsetX + 3000 * fitScale,
                                fitOffsetY + 3000 * fitScale, 1, c => c.R == 90 && c.G == 60 && c.B == 30);
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
                            bool imageDrawn = HasColorNear(zoomBitmap, 80, 100, 1,
                                c => c.R == 90 && c.G == 60 && c.B == 30);
                            checks["zoom_image_drawn"] = imageDrawn;
                            Check(checks, failures, "zoom_image_drawn", "放大视图没有绘制底图");
                        }

                        var watch = Stopwatch.StartNew();
                        for (int i = 0; i < 12; i++)
                        {
                            viewer.FocusImageRegion(new RectangleF(regionX + i * 10, regionY, regionWidth, regionHeight));
                            using (var frame = new Bitmap(PanelWidth, PanelHeight))
                                viewer.DrawToBitmap(frame, new Rectangle(0, 0, PanelWidth, PanelHeight));
                        }
                        timings["pan_frame_average_ms"] = watch.Elapsed.TotalMilliseconds / 12;
                        checks["pan_under_100ms"] = timings["pan_frame_average_ms"] < 100;

                        viewer.FocusImageRegion(new RectangleF(19995, 16995, 10, 10));
                        using (Bitmap detail = Render(viewer, Path.Combine(outputDirectory, "fixture-detail.png")))
                        {
                            checks["single_source_pixel_preserved"] = HasColorNear(detail, 620, 420, 1,
                                c => c.R == 255 && c.G == 255 && c.B == 255);
                            checks["detail_viewport_edge"] = detail.GetPixel(100, 790).ToArgb() == Color.FromArgb(90, 60, 30).ToArgb();
                        }

                        watch.Restart();
                        using (Bitmap export = viewer.CreateVisualizationBitmap())
                        {
                            checks["export_original_size"] = export.Width == SourceWidth && export.Height == SourceHeight;
                            checks["export_source_pixel"] = export.GetPixel(20000, 17000).ToArgb() == Color.White.ToArgb();
                            checks["export_block_edges"] = export.GetPixel(2047, 2047).ToArgb() == Color.FromArgb(90, 60, 30).ToArgb()
                                && export.GetPixel(2048, 2048).ToArgb() == Color.FromArgb(90, 60, 30).ToArgb();
                            Color maskColor = export.GetPixel(BoxX + 100, BoxY + 100);
                            checks["export_full_mask"] = maskColor.G > 120 && maskColor.R < 140 && maskColor.B < 120;
                        }
                        timings["export_ms"] = watch.Elapsed.TotalMilliseconds;
                        viewer.image = null;
                        viewer.ClearResults();
                    }
                }

                using (var viewer = new ImageViewer { Size = new System.Drawing.Size(PanelWidth, PanelHeight) })
                using (var image = new Mat(24000, 1000, MatType.CV_8UC3, new Scalar(30, 60, 90)))
                using (var mask = new Mat(24000, 1000, MatType.CV_8UC1, Scalar.All(0)))
                {
                    image.Set(23600, 500, new Vec3b(255, 255, 255));
                    using (var area = new Mat(mask, new OpenCvSharp.Rect(600, 23600, 10, 10)))
                        area.SetTo(Scalar.All(255));
                    var obj = new Utils.CSharpObjectResult(1, "框", 0.9f, 100,
                        new List<double> { 600, 23600, 10, 10 }, true, mask, true, false, -100f, null);
                    viewer.UpdateImageAndResult(image, new Utils.CSharpResult(
                        new List<Utils.CSharpSampleResult> { new Utils.CSharpSampleResult(new List<Utils.CSharpObjectResult> { obj }, false, "NG") }, "{}"));
                    viewer.FocusImageRegion(new RectangleF(0, 23200, 1000, 800));
                    using (Bitmap frame = Render(viewer, Path.Combine(outputDirectory, "fixture-tall.png")))
                    {
                        checks["tall_source_pixel"] = HasColorNear(frame, 600, 400, 1, c => c.R == 255 && c.G == 255 && c.B == 255);
                        checks["tall_mask_position"] = HasColorNear(frame, 705, 405, 1, c => c.G > 120 && c.R < 140 && c.B < 120);
                    }
                    viewer.image = null;
                    viewer.ClearResults();
                }

                using (var viewer = new ImageViewer { Size = new System.Drawing.Size(PanelWidth, PanelHeight) })
                using (var image = new Mat(20283, 28301, MatType.CV_8UC1, Scalar.All(90)))
                {
                    image.Set<byte>(19500, 20000, 255);
                    viewer.UpdateImage(image);
                    viewer.FocusImageRegion(new RectangleF(19500, 19100, 1000, 800));
                    using (Bitmap frame = Render(viewer, Path.Combine(outputDirectory, "fixture-wide-gray.png")))
                    {
                        checks["wide_gray_source_pixel"] = HasColorNear(frame, 600, 400, 1, c => c.R == 255 && c.G == 255 && c.B == 255);
                        checks["wide_gray_background"] = frame.GetPixel(50, 790).ToArgb() == Color.FromArgb(90, 90, 90).ToArgb();
                    }
                    viewer.image = null;
                }

                using (var viewer = new ImageViewer { Size = new System.Drawing.Size(PanelWidth, PanelHeight) })
                using (var image = new Mat(900, 1600, MatType.CV_8UC4, new Scalar(30, 60, 90, 0)))
                using (var mask = new Mat(10, 20, MatType.CV_8UC1, Scalar.All(0)))
                {
                    using (var area = new Mat(mask, new OpenCvSharp.Rect(0, 0, 10, 10)))
                        area.SetTo(Scalar.All(255));
                    var obj = new Utils.CSharpObjectResult(1, "框", 0.9f, 20000,
                        new List<double> { 400, 300, 200, 100 }, true, mask, true, false, -100f, null);
                    viewer.UpdateImageAndResult(image, new Utils.CSharpResult(
                        new List<Utils.CSharpSampleResult> { new Utils.CSharpSampleResult(new List<Utils.CSharpObjectResult> { obj }, false, "NG") }, "{}"));
                    using (Bitmap frame = Render(viewer, Path.Combine(outputDirectory, "fixture-small-roi.png")))
                    {
                        checks["small_bgra_background"] = frame.GetPixel(100, 200).ToArgb() == Color.FromArgb(90, 60, 30).ToArgb();
                        checks["roi_mask_left"] = frame.GetPixel(340, 325).G > 120;
                        checks["roi_mask_right"] = frame.GetPixel(420, 325).ToArgb() == Color.FromArgb(90, 60, 30).ToArgb();
                    }
                    using (Bitmap export = viewer.CreateVisualizationBitmap())
                        checks["small_export_original_size"] = export.Width == 1600 && export.Height == 900;
                    viewer.image = null;
                    viewer.ClearResults();
                }

                bool allPassed = checks.Count > 0;
                foreach (var check in checks)
                {
                    if (!check.Value)
                    {
                        allPassed = false;
                        failures.Add(check.Key);
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
                ["timings"] = timings,
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
