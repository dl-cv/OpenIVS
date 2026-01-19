using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DlcvTest.Controls
{
    /// <summary>
    /// 带对号滑入动画效果的 CheckBox 控件
    /// 模仿 HTML/CSS 示例的动画效�?
    /// </summary>
    public partial class AnimatedCheckBox : UserControl
    {
        // 依赖属性：IsChecked
        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register(
                nameof(IsChecked),
                typeof(bool),
                typeof(AnimatedCheckBox),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsCheckedChanged));

        public bool IsChecked
        {
            get => (bool)GetValue(IsCheckedProperty);
            set => SetValue(IsCheckedProperty, value);
        }

        // Checked 事件
        public event RoutedEventHandler Checked;
        public event RoutedEventHandler Unchecked;

        private const double AnimationDuration = 0.2; // �?

        public AnimatedCheckBox()
        {
            InitializeComponent();
            Loaded += AnimatedCheckBox_Loaded;
            
            // 鼠标悬停效果
            MouseEnter += (s, e) => AnimateBorderColor(Color.FromRgb(138, 86, 246), 0.15);
            MouseLeave += (s, e) => 
            {
                if (!IsChecked)
                {
                    AnimateBorderColor(Color.FromRgb(192, 192, 192), 0.15);
                }
            };
        }

        private void AnimatedCheckBox_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化状态（无动画）
            UpdateVisualState(false);
        }

        private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AnimatedCheckBox checkbox)
            {
                bool newValue = (bool)e.NewValue;
                checkbox.UpdateVisualState(true);
                
                // 触发事件
                if (newValue)
                {
                    checkbox.Checked?.Invoke(checkbox, new RoutedEventArgs());
                }
                else
                {
                    checkbox.Unchecked?.Invoke(checkbox, new RoutedEventArgs());
                }
            }
        }

        private void Grid_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 切换状�?
            IsChecked = !IsChecked;
            e.Handled = true;
        }

        private void UpdateVisualState(bool useTransition)
        {
            if (CheckScale == null || BorderColor == null || BorderBg == null || CheckMark == null)
                return;

            if (useTransition)
            {
                // 使用动画过渡
                AnimateCheckMark();
                AnimateBackground();
                AnimateBorderColor(IsChecked ? Color.FromRgb(138, 86, 246) : Color.FromRgb(192, 192, 192), AnimationDuration);
            }
            else
            {
                // 直接设置（无动画�?
                if (IsChecked)
                {
                    // 选中状态：对号可见
                    CheckScale.ScaleX = 1;
                    CheckScale.ScaleY = 1;
                    BorderBg.Background = new SolidColorBrush(Color.FromRgb(138, 86, 246));
                    BorderColor.Color = Color.FromRgb(138, 86, 246);
                }
                else
                {
                    // 未选中状态：对号隐藏
                    CheckScale.ScaleX = 0;
                    CheckScale.ScaleY = 0;
                    BorderBg.Background = Brushes.Transparent;
                    BorderColor.Color = Color.FromRgb(192, 192, 192);
                }
            }
        }

        private void AnimateCheckMark()
        {
            // 对号缩放动画（从中心点放�?缩小），使用更明显的回弹效果
            var scaleAnimation = new DoubleAnimation
            {
                To = IsChecked ? 1 : 0,
                Duration = TimeSpan.FromSeconds(AnimationDuration),
                EasingFunction = new BackEase 
                { 
                    EasingMode = EasingMode.EaseOut,
                    Amplitude = 0.6 // 更明显的回弹效果
                }
            };

            CheckScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            CheckScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        }

        private void AnimateBackground()
        {
            var animation = new ColorAnimation
            {
                To = IsChecked ? Color.FromRgb(138, 86, 246) : Colors.Transparent,
                Duration = TimeSpan.FromSeconds(AnimationDuration),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // 确保 Brush 不是冻结�?
            var brush = BorderBg.Background as SolidColorBrush;
            if (brush == null || brush.IsFrozen)
            {
                brush = new SolidColorBrush(brush?.Color ?? Colors.Transparent);
                BorderBg.Background = brush;
            }
            
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        private void AnimateBorderColor(Color targetColor, double duration)
        {
            if (BorderColor == null)
                return;
                
            var animation = new ColorAnimation
            {
                To = targetColor,
                Duration = TimeSpan.FromSeconds(duration),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // 确保 Brush 不是冻结�?
            if (BorderColor.IsFrozen)
            {
                var newBrush = new SolidColorBrush(BorderColor.Color);
                BorderBg.BorderBrush = newBrush;
                newBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
            }
            else
            {
                BorderColor.BeginAnimation(SolidColorBrush.ColorProperty, animation);
            }
        }
    }
}

