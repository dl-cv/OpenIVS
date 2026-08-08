using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Newtonsoft.Json.Linq;
using dlcv_infer_csharp;
using Mat = OpenCvSharp.Mat;

namespace OpenIVS2.Controls
{
    public sealed class InteractiveImageViewer : Grid
    {
        private sealed class OverlayItem
        {
            public string Category;
            public double? Score;
            public double[] Bbox;
            public bool WithBbox;
            public bool WithAngle;
            public double Angle;
            public List<Point> Polyline;
            public BitmapSource Mask;
        }

        private readonly Canvas _scene;
        private readonly System.Windows.Controls.Image _image;
        private readonly Canvas _overlay;
        private readonly MatrixTransform _sceneTransform;
        private readonly List<OverlayItem> _items = new List<OverlayItem>();
        private Point _dragStart;
        private double _dragStartX;
        private double _dragStartY;
        private bool _dragging;
        private double _zoom = 1.0;
        private double _offsetX;
        private double _offsetY;
        private double _annotationScale = 1.0;
        private bool _overlaysVisible = true;

        public InteractiveImageViewer()
        {
            Focusable = true;
            ClipToBounds = true;
            Background = Brushes.Transparent;

            _sceneTransform = new MatrixTransform(Matrix.Identity);
            _scene = new Canvas
            {
                RenderTransform = _sceneTransform,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            _image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            _overlay = new Canvas { IsHitTestVisible = false };
            _scene.Children.Add(_image);
            _scene.Children.Add(_overlay);
            Children.Add(_scene);

            PreviewMouseDown += OnPreviewMouseDown;
            PreviewMouseMove += OnPreviewMouseMove;
            PreviewMouseUp += OnPreviewMouseUp;
            PreviewMouseWheel += OnPreviewMouseWheel;
            PreviewKeyDown += OnPreviewKeyDown;
            SizeChanged += OnViewerSizeChanged;
        }

        public bool HasImage { get { return _image.Source != null; } }
        public bool OverlaysVisible { get { return _overlaysVisible; } }
        public double AnnotationScale { get { return _annotationScale; } }
        public double EffectiveScreenLineWidth { get { return Math.Max(1.0, 2.0 * _annotationScale); } }
        public double Zoom { get { return _zoom; } }
        public int OverlayCount { get { return _items.Count; } }
        public string OverlayGeometrySignature
        {
            get
            {
                return string.Join("|", _items.Select(item =>
                    (item.Category ?? "") + ":" +
                    string.Join(",", item.Bbox != null
                        ? item.Bbox.Select(x => x.ToString("0.####", CultureInfo.InvariantCulture))
                        : Enumerable.Empty<string>()) + ":" +
                    item.Angle.ToString("0.####", CultureInfo.InvariantCulture)));
            }
        }

        public void SetFrame(BitmapSource source, object result)
        {
            _image.Source = source;
            _items.Clear();
            _overlay.Children.Clear();
            if (source == null)
            {
                _scene.Width = _scene.Height = 0;
                return;
            }

            _scene.Width = source.PixelWidth;
            _scene.Height = source.PixelHeight;
            _image.Width = source.PixelWidth;
            _image.Height = source.PixelHeight;
            _overlay.Width = source.PixelWidth;
            _overlay.Height = source.PixelHeight;
            ParseResult(result, _items);
            RebuildOverlay();
            Dispatcher.BeginInvoke(new Action(FitToView), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public void ClearFrame()
        {
            _image.Source = null;
            _items.Clear();
            _overlay.Children.Clear();
            _scene.Width = _scene.Height = 0;
            _zoom = 1.0;
            _offsetX = _offsetY = 0;
            ApplyTransform();
        }

        public BitmapSource RenderVisualization()
        {
            var source = _image.Source as BitmapSource;
            if (source == null) return null;
            var previousZoom = _zoom;
            var previousOffsetX = _offsetX;
            var previousOffsetY = _offsetY;
            var previousVisibility = _overlaysVisible;
            try
            {
                _zoom = 1.0;
                _offsetX = _offsetY = 0;
                _overlaysVisible = true;
                ApplyTransform();
                RebuildOverlay();
                _scene.UpdateLayout();
                var bitmap = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(_scene);
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                _zoom = previousZoom;
                _offsetX = previousOffsetX;
                _offsetY = previousOffsetY;
                _overlaysVisible = previousVisibility;
                ApplyTransform();
                RebuildOverlay();
            }
        }

        public void ToggleOverlays()
        {
            _overlaysVisible = !_overlaysVisible;
            _overlay.Visibility = _overlaysVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void IncreaseAnnotationScale()
        {
            SetAnnotationScale(_annotationScale * 1.1);
        }

        public void DecreaseAnnotationScale()
        {
            SetAnnotationScale(_annotationScale / 1.1);
        }

        public void FitToView()
        {
            var source = _image.Source as BitmapSource;
            if (source == null || ActualWidth <= 0 || ActualHeight <= 0) return;
            _zoom = Math.Min(ActualWidth / source.PixelWidth, ActualHeight / source.PixelHeight);
            if (double.IsNaN(_zoom) || double.IsInfinity(_zoom) || _zoom <= 0) _zoom = 1.0;
            _offsetX = (ActualWidth - source.PixelWidth * _zoom) / 2.0;
            _offsetY = (ActualHeight - source.PixelHeight * _zoom) / 2.0;
            ApplyTransform();
            RebuildOverlay();
        }

        public void ZoomAt(Point viewportPoint, double factor)
        {
            if (!HasImage || factor <= 0) return;
            var oldZoom = _zoom;
            var newZoom = Math.Max(0.05, Math.Min(30.0, oldZoom * factor));
            if (Math.Abs(newZoom - oldZoom) < 0.0001) return;
            var imageX = (viewportPoint.X - _offsetX) / oldZoom;
            var imageY = (viewportPoint.Y - _offsetY) / oldZoom;
            _zoom = newZoom;
            _offsetX = viewportPoint.X - imageX * newZoom;
            _offsetY = viewportPoint.Y - imageY * newZoom;
            ClampPosition();
            ApplyTransform();
            RebuildOverlay();
        }

        private void SetAnnotationScale(double value)
        {
            var normalized = Math.Max(0.3, Math.Min(5.0, value));
            if (Math.Abs(normalized - _annotationScale) < 0.0001) return;
            _annotationScale = normalized;
            RebuildOverlay();
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            if (e.ChangedButton == MouseButton.Right)
            {
                FitToView();
                e.Handled = true;
                return;
            }
            if (e.ChangedButton != MouseButton.Left || !HasImage) return;
            _dragging = true;
            _dragStart = e.GetPosition(this);
            _dragStartX = _offsetX;
            _dragStartY = _offsetY;
            CaptureMouse();
            e.Handled = true;
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var current = e.GetPosition(this);
            _offsetX = _dragStartX + current.X - _dragStart.X;
            _offsetY = _dragStartY + current.Y - _dragStart.Y;
            ClampPosition();
            ApplyTransform();
            e.Handled = true;
        }

        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || !_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!HasImage) return;
            ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.1 : 1.0 / 1.1);
            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.V:
                    ToggleOverlays();
                    e.Handled = true;
                    break;
                case Key.OemPlus:
                case Key.Add:
                    IncreaseAnnotationScale();
                    e.Handled = true;
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    DecreaseAnnotationScale();
                    e.Handled = true;
                    break;
            }
        }

        private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!HasImage) return;
            ClampPosition();
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            _sceneTransform.Matrix = new Matrix(_zoom, 0, 0, _zoom, _offsetX, _offsetY);
        }

        private void ClampPosition()
        {
            var source = _image.Source as BitmapSource;
            if (source == null || ActualWidth <= 0 || ActualHeight <= 0) return;
            _offsetX = ClampAxis(_offsetX, source.PixelWidth * _zoom, ActualWidth);
            _offsetY = ClampAxis(_offsetY, source.PixelHeight * _zoom, ActualHeight);
        }

        private static double ClampAxis(double value, double scaledSize, double viewportSize)
        {
            var visible = Math.Min(100.0, viewportSize / 2.0);
            var min = visible - scaledSize;
            var max = viewportSize - visible;
            if (min > max) return (viewportSize - scaledSize) / 2.0;
            return Math.Max(min, Math.Min(max, value));
        }

        private void RebuildOverlay()
        {
            _overlay.Children.Clear();
            _overlay.Visibility = _overlaysVisible ? Visibility.Visible : Visibility.Collapsed;
            if (!HasImage || _items.Count == 0) return;

            var safeZoom = Math.Max(_zoom, 0.0001);
            var lineWidth = Math.Max(1.0 / safeZoom, 2.0 * _annotationScale / safeZoom);
            const double baseFont = 20.0;
            const double maxScreenFont = 128.0;
            var screenFont = Math.Min(Math.Max(baseFont * safeZoom, baseFont), maxScreenFont) * _annotationScale;
            var fontSize = Math.Max(8.0, screenFont / safeZoom);
            var topLeftIndex = 0;
            var okBrush = (Brush)FindResource("SuccessBrush");

            foreach (var item in _items)
            {
                var color = !string.IsNullOrEmpty(item.Category) &&
                    item.Category.IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0
                    ? okBrush
                    : Brushes.Red;

                if (item.Polyline != null && item.Polyline.Count >= 2)
                {
                    var polyline = new Polyline
                    {
                        Stroke = color,
                        StrokeThickness = lineWidth,
                        Points = new PointCollection(item.Polyline)
                    };
                    _overlay.Children.Add(polyline);
                }

                if (!item.WithBbox || item.Bbox == null || item.Bbox.Length < 4)
                {
                    AddLabel(item, 10.0 / safeZoom,
                        10.0 / safeZoom + topLeftIndex * (fontSize + 6.0 / safeZoom),
                        color, fontSize);
                    topLeftIndex++;
                    continue;
                }

                if (item.WithAngle)
                {
                    var points = RotatedPoints(item.Bbox, item.Angle);
                    _overlay.Children.Add(new Polygon
                    {
                        Stroke = color,
                        StrokeThickness = lineWidth,
                        Fill = Brushes.Transparent,
                        Points = new PointCollection(points)
                    });
                    AddLabel(item, points.Min(x => x.X), points.Min(x => x.Y) - fontSize - 2.0 / safeZoom,
                        color, fontSize);
                }
                else
                {
                    var x = item.Bbox[0];
                    var y = item.Bbox[1];
                    var width = Math.Max(1, item.Bbox[2]);
                    var height = Math.Max(1, item.Bbox[3]);
                    if (item.Mask != null)
                    {
                        var mask = new System.Windows.Controls.Image
                        {
                            Source = item.Mask,
                            Width = width,
                            Height = height,
                            Stretch = Stretch.Fill,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(mask, x);
                        Canvas.SetTop(mask, y);
                        _overlay.Children.Add(mask);
                    }
                    var rectangle = new Rectangle
                    {
                        Width = width,
                        Height = height,
                        Stroke = color,
                        StrokeThickness = lineWidth,
                        Fill = Brushes.Transparent
                    };
                    Canvas.SetLeft(rectangle, x);
                    Canvas.SetTop(rectangle, y);
                    _overlay.Children.Add(rectangle);
                    var labelY = y - fontSize - 2.0 / safeZoom;
                    if (labelY < 0) labelY = y + 2.0 / safeZoom;
                    AddLabel(item, x, labelY, color, fontSize);
                }
            }
        }

        private void AddLabel(OverlayItem item, double x, double y, Brush color, double fontSize)
        {
            var text = item.Category ?? "";
            if (item.Score.HasValue)
                text += " " + item.Score.Value.ToString("0.00", CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text)) return;
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = color,
                    FontFamily = new FontFamily("Microsoft YaHei"),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = fontSize,
                    Padding = new Thickness(2, 0, 2, 0)
                }
            };
            Canvas.SetLeft(label, Math.Max(0, x));
            Canvas.SetTop(label, Math.Max(0, y));
            _overlay.Children.Add(label);
        }

        private static List<Point> RotatedPoints(double[] bbox, double angle)
        {
            var cx = bbox[0];
            var cy = bbox[1];
            var halfW = bbox[2] / 2.0;
            var halfH = bbox[3] / 2.0;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var offsets = new[]
            {
                new Point(-halfW, -halfH), new Point(halfW, -halfH),
                new Point(halfW, halfH), new Point(-halfW, halfH)
            };
            return offsets.Select(p => new Point(
                cx + p.X * cos - p.Y * sin,
                cy + p.X * sin + p.Y * cos)).ToList();
        }

        private static void ParseResult(object result, List<OverlayItem> output)
        {
            if (result == null) return;
            if (result is Utils.CSharpResult)
            {
                ParseStructured((Utils.CSharpResult)result, output);
                return;
            }

            var detailProperty = result.GetType().GetProperty("Detail");
            if (detailProperty != null)
            {
                var detail = detailProperty.GetValue(result, null) as string;
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    try { ParseJson(JToken.Parse(detail), output); } catch { }
                    return;
                }
            }

            try
            {
                var token = result as JToken ?? JToken.FromObject(result);
                ParseJson(token, output);
            }
            catch { }
        }

        private static void ParseStructured(Utils.CSharpResult result, List<OverlayItem> output)
        {
            if (result.SampleResults == null || result.SampleResults.Count == 0) return;
            var sample = result.SampleResults[0];
            if (sample.Results == null) return;
            foreach (var raw in sample.Results)
            {
                var item = new OverlayItem
                {
                    Category = raw.CategoryName,
                    Score = raw.Score,
                    Bbox = raw.Bbox != null ? raw.Bbox.ToArray() : null,
                    WithBbox = raw.WithBbox,
                    WithAngle = raw.WithAngle,
                    Angle = raw.Angle,
                    Mask = raw.WithMask ? CreateMaskSource(raw.Mask) : null
                };
                var points = Utils.GetExtraInfoPolyline(raw.ExtraInfo);
                if (points != null && points.Count > 0)
                    item.Polyline = points.Select(p => new Point(p.X, p.Y)).ToList();
                output.Add(item);
            }
        }

        private static void ParseJson(JToken token, List<OverlayItem> output)
        {
            if (token == null) return;
            var array = token as JArray;
            if (array != null)
            {
                foreach (var child in array) ParseJson(child, output);
                return;
            }
            var obj = token as JObject;
            if (obj == null) return;
            var bbox = obj["bbox"] as JArray;
            if (bbox != null || obj["category_name"] != null || obj["category"] != null)
            {
                var values = bbox != null ? bbox.Select(x => x.ToObject<double>()).ToArray() : null;
                var withAngle = ValueOrDefault(obj["with_angle"], false) || (values != null && values.Length >= 5);
                var item = new OverlayItem
                {
                    Category = obj["category_name"] != null ? obj["category_name"].ToString() :
                        (obj["category"] != null ? obj["category"].ToString() : ""),
                    Score = obj["score"] != null ? (double?)obj["score"].ToObject<double>() : null,
                    Bbox = values,
                    WithBbox = ValueOrDefault(obj["with_bbox"], values != null && values.Length >= 4),
                    WithAngle = withAngle,
                    Angle = obj["angle"] != null ? obj["angle"].ToObject<double>() :
                        (withAngle && values != null && values.Length >= 5 ? values[4] : -100)
                };
                var polyline = obj.SelectToken("extra_info.polyline") as JArray ?? obj["polyline"] as JArray;
                if (polyline != null)
                {
                    item.Polyline = new List<Point>();
                    foreach (var point in polyline.OfType<JArray>())
                    {
                        if (point.Count >= 2)
                            item.Polyline.Add(new Point(point[0].ToObject<double>(), point[1].ToObject<double>()));
                    }
                }
                output.Add(item);
                return;
            }

            var keys = new[] { "result_list", "by_image", "sample_results", "results" };
            foreach (var key in keys)
            {
                if (obj[key] != null) ParseJson(obj[key], output);
            }
        }

        private static bool ValueOrDefault(JToken token, bool defaultValue)
        {
            if (token == null) return defaultValue;
            try { return token.ToObject<bool>(); } catch { return defaultValue; }
        }

        private static BitmapSource CreateMaskSource(Mat mask)
        {
            if (mask == null || mask.Empty() || mask.Channels() != 1) return null;
            var width = mask.Width;
            var height = mask.Height;
            var pixels = new byte[width * height * 4];
            var row = new byte[width];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(mask.Data + y * (int)mask.Step(), row, 0, width);
                for (var x = 0; x < width; x++)
                {
                    var value = row[x];
                    if (value == 0) continue;
                    var index = (y * width + x) * 4;
                    pixels[index] = 0;
                    pixels[index + 1] = 255;
                    pixels[index + 2] = 0;
                    pixels[index + 3] = (byte)Math.Min(160, (int)value);
                }
            }
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            source.Freeze();
            return source;
        }
    }
}
