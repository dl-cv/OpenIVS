using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using OpenIVS2.Models;

namespace OpenIVS2.Services
{
    public sealed class ImageSaveService
    {
        public List<string> SaveCycle(
            AppSettings settings,
            bool ok,
            Dictionary<string, BitmapSource> images,
            Dictionary<string, BitmapSource> visualizations)
        {
            var paths = new List<string>();
            if (settings == null || images == null) return paths;
            if ((ok && !settings.SaveOkImages) || (!ok && !settings.SaveNgImages)) return paths;
            var status = ok ? "OK" : "NG";
            var directory = Path.Combine(settings.SaveDirectory, DateTime.Now.ToString("yyyyMMdd"), status);
            Directory.CreateDirectory(directory);
            var stamp = DateTime.Now.ToString("HHmmss_fff");
            foreach (var pair in images)
            {
                var slot = pair.Key.StartsWith("WIN_", StringComparison.OrdinalIgnoreCase) ? pair.Key.Substring(4) : pair.Key;
                var extension = NormalizeExtension(settings.ImageFormat);
                var path = Path.Combine(directory, stamp + "_" + slot + "_" + status + "." + extension);
                using (var stream = File.Create(path))
                {
                    var encoder = CreateEncoder(extension, settings.JpegQuality);
                    encoder.Frames.Add(BitmapFrame.Create(pair.Value));
                    encoder.Save(stream);
                }
                paths.Add(path);
                if (settings.SaveVisualizationImages && visualizations != null)
                {
                    BitmapSource visualization;
                    if (visualizations.TryGetValue(slot, out visualization) && visualization != null)
                    {
                        var visualizationExtension = NormalizeVisualizationExtension(settings.VisualizationImageFormat);
                        var visualizationPath = Path.Combine(directory,
                            stamp + "_" + slot + "_" + status + "_vis." + visualizationExtension);
                        using (var stream = File.Create(visualizationPath))
                        {
                            var encoder = CreateEncoder(visualizationExtension, settings.JpegQuality);
                            encoder.Frames.Add(BitmapFrame.Create(visualization));
                            encoder.Save(stream);
                        }
                        paths.Add(visualizationPath);
                    }
                }
            }
            return paths;
        }

        private static string NormalizeExtension(string format)
        {
            var value = (format ?? "PNG").Trim().ToUpperInvariant();
            if (value == "JPG" || value == "JPEG") return "jpg";
            if (value == "BMP") return "bmp";
            return "png";
        }

        private static BitmapEncoder CreateEncoder(string extension, int quality)
        {
            if (extension == "jpg") return new JpegBitmapEncoder { QualityLevel = Math.Max(1, Math.Min(100, quality)) };
            if (extension == "bmp") return new BmpBitmapEncoder();
            return new PngBitmapEncoder();
        }

        private static string NormalizeVisualizationExtension(string format)
        {
            var value = (format ?? "JPG").Trim().ToUpperInvariant();
            return value == "PNG" ? "png" : "jpg";
        }
    }
}
