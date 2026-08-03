using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DLCV.SequenceGraph;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace OpenIVS2.Services
{
    public sealed class UiDisplaySink : IDisplaySink
    {
        private readonly object _sync = new object();
        private readonly Dispatcher _dispatcher;
        private readonly Action<string, BitmapSource, object> _update;
        private readonly Dictionary<string, BitmapSource> _images = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        public UiDisplaySink(Dispatcher dispatcher, Action<string, BitmapSource, object> update)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException("dispatcher");
            _update = update ?? throw new ArgumentNullException("update");
        }

        public void Update(string windowId, object image, object result)
        {
            var bytes = ToPngBytes(image);
            if (bytes == null) return;
            Action commit = () =>
            {
                var source = FromPngBytes(bytes);
                lock (_sync) _images[windowId] = source;
                _update(windowId, source, result);
            };
            if (_dispatcher.CheckAccess()) commit();
            else _dispatcher.Invoke(commit);
        }

        public Dictionary<string, BitmapSource> GetImagesSnapshot()
        {
            lock (_sync) return new Dictionary<string, BitmapSource>(_images, StringComparer.OrdinalIgnoreCase);
        }

        private static byte[] ToPngBytes(object image)
        {
            try
            {
                var mat = image as Mat;
                if (mat != null)
                {
                    using (var bitmap = BitmapConverter.ToBitmap(mat))
                    using (var stream = new MemoryStream())
                    {
                        bitmap.Save(stream, ImageFormat.Png);
                        return stream.ToArray();
                    }
                }
                var bitmapImage = image as Bitmap;
                if (bitmapImage != null)
                {
                    using (var stream = new MemoryStream())
                    {
                        bitmapImage.Save(stream, ImageFormat.Png);
                        return stream.ToArray();
                    }
                }
                var path = image as string;
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            finally
            {
                var ownedMat = image as Mat;
                if (ownedMat != null) ownedMat.Dispose();
            }
        }

        private static BitmapSource FromPngBytes(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            {
                var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return decoder.Frames[0];
            }
        }
    }
}
