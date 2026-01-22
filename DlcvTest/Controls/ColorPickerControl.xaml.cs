using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DlcvTest.Controls
{
    public partial class ColorPickerControl : UserControl
    {
        private bool _isColorAreaDragging = false;
        private bool _isHueSliderDragging = false;
        
        private double _hue = 0.0; // 0-360
        private double _saturation = 1.0; // 0-1
        private double _value = 1.0; // 0-1 (亮度)
        
        public event EventHandler<Color> ColorChanged;
        public event EventHandler<Color> ColorConfirmed;
        public event EventHandler ColorCleared;
        
        private Color _currentColor = Colors.Red;
        
        public Color SelectedColor
        {
            get => _currentColor;
            set
            {
                _currentColor = value;
                UpdateFromColor(value);
                UpdateColorArea();
                UpdateUI();
            }
        }
        
        public ColorPickerControl()
        {
            InitializeComponent();
            InitializeColorArea();
            UpdateUI();
        }
        
        private void InitializeColorArea()
        {
            UpdateColorArea();
        }
        
        private void UpdateColorArea()
        {
            // 使用更高效的方法：创建垂直渐变（亮度）和水平渐变（饱和度）的组合
            // 先创建垂直方向的亮度渐变（从白色到黑色）
            LinearGradientBrush valueBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            valueBrush.GradientStops.Add(new GradientStop(Colors.White, 0));
            valueBrush.GradientStops.Add(new GradientStop(Colors.Black, 1));
            
            // 创建水平方向的饱和度渐变（从当前色相到灰色）
            Color hueColor = HsvToRgb(_hue, 1.0, 1.0);
            LinearGradientBrush saturationBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            saturationBrush.GradientStops.Add(new GradientStop(hueColor, 0));
            saturationBrush.GradientStops.Add(new GradientStop(Colors.Gray, 1));
            
            // 使用组合画刷：先应用饱和度，再应用亮度
            // 由于WPF不支持直接组合，我们使用更简单的方法：创建一个包含多个渐变停止点的LinearGradientBrush
            // 但实际上，我们需要一个二维渐变，这在WPF中比较复杂
            // 所以使用一个简化的方法：创建多个水平渐变条
            DrawingGroup drawingGroup = new DrawingGroup();
            
            // 创建20行渐变（每行10像素高），减少计算量
            for (int y = 0; y < 20; y++)
            {
                double val = 1.0 - (y / 20.0); // 垂直方向：亮度 1-0
                
                // 水平方向：饱和度渐变
                LinearGradientBrush rowBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0)
                };
                
                // 添加几个关键点的渐变停止
                for (int x = 0; x <= 10; x++)
                {
                    double sat = x / 10.0;
                    Color color = HsvToRgb(_hue, sat, val);
                    rowBrush.GradientStops.Add(new GradientStop(color, sat));
                }
                
                GeometryDrawing drawing = new GeometryDrawing(
                    rowBrush, null,
                    new RectangleGeometry(new Rect(0, y * 10, 200, 10))
                );
                drawingGroup.Children.Add(drawing);
            }
            
            DrawingBrush brush = new DrawingBrush(drawingGroup)
            {
                Viewport = new Rect(0, 0, 200, 200),
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.None,
                Stretch = Stretch.None
            };
            
            colorAreaRect.Fill = brush;
        }
        
        private void ColorArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isColorAreaDragging = true;
            UpdateColorFromPosition(e.GetPosition(colorAreaCanvas));
            colorAreaCanvas.CaptureMouse();
        }
        
        private void ColorArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isColorAreaDragging)
            {
                UpdateColorFromPosition(e.GetPosition(colorAreaCanvas));
            }
        }
        
        private void ColorArea_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isColorAreaDragging)
            {
                UpdateColorFromPosition(e.GetPosition(colorAreaCanvas));
                _isColorAreaDragging = false;
                colorAreaCanvas.ReleaseMouseCapture();
            }
        }
        
        private void UpdateColorFromPosition(Point position)
        {
            double x = Math.Max(0, Math.Min(199, position.X));
            double y = Math.Max(0, Math.Min(199, position.Y));
            
            _saturation = x / 199.0;
            _value = 1.0 - (y / 199.0);
            
            _currentColor = HsvToRgb(_hue, _saturation, _value);
            UpdateUI();
            ColorChanged?.Invoke(this, _currentColor);
        }
        
        private void HueSlider_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isHueSliderDragging = true;
            UpdateHueFromPosition(e.GetPosition(hueSliderGrid));
            hueSliderGrid.CaptureMouse();
        }
        
        private void HueSlider_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isHueSliderDragging)
            {
                UpdateHueFromPosition(e.GetPosition(hueSliderGrid));
            }
        }
        
        private void HueSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isHueSliderDragging)
            {
                UpdateHueFromPosition(e.GetPosition(hueSliderGrid));
                _isHueSliderDragging = false;
                hueSliderGrid.ReleaseMouseCapture();
            }
        }
        
        private void UpdateHueFromPosition(Point position)
        {
            double y = Math.Max(0, Math.Min(199, position.Y));
            _hue = (y / 199.0) * 360.0;
            
            UpdateColorArea();
            _currentColor = HsvToRgb(_hue, _saturation, _value);
            UpdateUI();
            ColorChanged?.Invoke(this, _currentColor);
        }
        
        private void UpdateUI()
        {
            // 更新颜色指示器位置
            double x = _saturation * 199.0;
            double y = (1.0 - _value) * 199.0;
            Canvas.SetLeft(colorIndicator, x);
            Canvas.SetTop(colorIndicator, y);
            
            // 更新色相指示器位置
            double hueY = (_hue / 360.0) * 199.0;
            Canvas.SetTop(hueIndicator, hueY);
            
            // 更新十六进制颜色显示
            string hex = $"#{_currentColor.A:X2}{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}";
            if (txtHexColor.Text != hex)
            {
                txtHexColor.Text = hex;
            }
        }
        
        private void UpdateFromColor(Color color)
        {
            // 将RGB转换为HSV
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            
            // 计算色相
            if (delta == 0)
            {
                _hue = 0;
            }
            else if (max == r)
            {
                _hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                _hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                _hue = 60 * (((r - g) / delta) + 4);
            }
            
            if (_hue < 0) _hue += 360;
            
            // 计算饱和度
            _saturation = max == 0 ? 0 : delta / max;
            
            // 计算亮度
            _value = max;
            
            UpdateColorArea();
        }
        
        private Color HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            
            double r = 0, g = 0, b = 0;
            
            if (h < 60)
            {
                r = c; g = x; b = 0;
            }
            else if (h < 120)
            {
                r = x; g = c; b = 0;
            }
            else if (h < 180)
            {
                r = 0; g = c; b = x;
            }
            else if (h < 240)
            {
                r = 0; g = x; b = c;
            }
            else if (h < 300)
            {
                r = x; g = 0; b = c;
            }
            else
            {
                r = c; g = 0; b = x;
            }
            
            return Color.FromRgb(
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255)
            );
        }
        
        private void TxtHexColor_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 实时更新颜色（但不触发事件，避免循环）
        }
        
        private void TxtHexColor_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = txtHexColor.Text.Trim();
                if (!text.StartsWith("#"))
                {
                    text = "#" + text;
                }
                
                Color color = (Color)ColorConverter.ConvertFromString(text);
                SelectedColor = color;
                ColorChanged?.Invoke(this, _currentColor);
            }
            catch
            {
                // 无效颜色，恢复原值
                UpdateUI();
            }
        }
        
        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            ColorConfirmed?.Invoke(this, _currentColor);
        }
        
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ColorCleared?.Invoke(this, EventArgs.Empty);
        }
    }
}


