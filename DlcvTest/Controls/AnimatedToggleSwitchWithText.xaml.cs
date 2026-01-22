using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DlcvTest.Controls
{
    /// <summary>
    /// 带 YES/NO 文字显示的动画开关控件
    /// 文字和滑块都有贝塞尔曲线动画效果
    /// </summary>
    public partial class AnimatedToggleSwitchWithText : UserControl
    {
        // 依赖属性：IsChecked
        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register(
                nameof(IsChecked),
                typeof(bool),
                typeof(AnimatedToggleSwitchWithText),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsCheckedChanged));

        public bool IsChecked
        {
            get => (bool)GetValue(IsCheckedProperty);
            set => SetValue(IsCheckedProperty, value);
        }

        // Checked 事件
        public event RoutedEventHandler Checked;
        public event RoutedEventHandler Unchecked;

        private const double AnimationDuration = 0.3; // 秒
        private const double ThumbCheckedPosition = 30; // 选中时的位置
        private const double ThumbUncheckedPosition = 2; // 未选中时的位置

        public AnimatedToggleSwitchWithText()
        {
            InitializeComponent();
            Loaded += AnimatedToggleSwitchWithText_Loaded;
        }

        private void AnimatedToggleSwitchWithText_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化状态（无动画）
            UpdateVisualState(false);
        }

        private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AnimatedToggleSwitchWithText toggle)
            {
                bool newValue = (bool)e.NewValue;
                toggle.UpdateVisualState(true);
                
                // 触发事件
                if (newValue)
                {
                    toggle.Checked?.Invoke(toggle, new RoutedEventArgs());
                }
                else
                {
                    toggle.Unchecked?.Invoke(toggle, new RoutedEventArgs());
                }
            }
        }

        private void Grid_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 切换状态
            IsChecked = !IsChecked;
            e.Handled = true;
        }

        private void UpdateVisualState(bool useTransition)
        {
            if (Thumb == null || TrackBrush == null)
                return;

            double targetPosition = IsChecked ? ThumbCheckedPosition : ThumbUncheckedPosition;
            Color targetColor = IsChecked ? Color.FromRgb(138, 86, 246) : Color.FromRgb(233, 233, 234); // 紫色 #8A56F6

            if (useTransition)
            {
                // 使用动画过渡
                AnimateThumbPosition(targetPosition);
                AnimateTrackColor(targetColor);
            }
            else
            {
                // 直接设置（无动画）
                Canvas.SetLeft(Thumb, targetPosition);
                TrackBrush.Color = targetColor;
            }
        }

        private void AnimateThumbPosition(double targetPosition)
        {
            // 创建弹性动画 - 使用 KeySpline 实现贝塞尔曲线效果
            var animation = new DoubleAnimationUsingKeyFrames();
            
            // 添加关键帧，使用 SplineDoubleKeyFrame 实现自定义贝塞尔曲线
            // KeySpline="0.175,0.885 0.32,1.275" 对应 CSS cubic-bezier(0.175, 0.885, 0.32, 1.275)
            animation.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(AnimationDuration)),
                Value = targetPosition,
                KeySpline = new KeySpline(0.175, 0.885, 0.32, 1.275) // 贝塞尔曲线
            });

            // 应用动画到 Thumb 的 Canvas.Left 属性
            Thumb.BeginAnimation(Canvas.LeftProperty, animation);
        }

        private void AnimateTrackColor(Color targetColor)
        {
            // 颜色渐变动画 - 使用弹性缓动函数
            var animation = new ColorAnimation
            {
                To = targetColor,
                Duration = TimeSpan.FromSeconds(AnimationDuration),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            // 应用动画到 TrackBrush 的 Color 属性
            TrackBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

    }
}

