using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DlcvTest.Controls
{
    /// <summary>
    /// �?YES/NO 文字显示的动画开关控�?
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

        private const double AnimationDuration = 0.3; // �?
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
            // 切换状�?
            IsChecked = !IsChecked;
            e.Handled = true;
        }

        private void UpdateVisualState(bool useTransition)
        {
            if (Thumb == null || TrackBrush == null || txtYes == null || txtNo == null)
                return;

            double targetPosition = IsChecked ? ThumbCheckedPosition : ThumbUncheckedPosition;
            Color targetColor = IsChecked ? Color.FromRgb(138, 86, 246) : Color.FromRgb(233, 233, 234); // 紫色 #8A56F6

            if (useTransition)
            {
                // 使用动画过渡
                AnimateThumbPosition(targetPosition);
                AnimateTrackColor(targetColor);
                AnimateTextVisibility();
            }
            else
            {
                // 直接设置（无动画�?
                Canvas.SetLeft(Thumb, targetPosition);
                TrackBrush.Color = targetColor;
                
                // 设置文字状�?
                txtYes.Opacity = IsChecked ? 1 : 0;
                txtNo.Opacity = IsChecked ? 0 : 1;
            }
        }

        private void AnimateThumbPosition(double targetPosition)
        {
            // 创建弹性动�?- 使用 KeySpline 实现贝塞尔曲线效�?
            var animation = new DoubleAnimationUsingKeyFrames();
            
            // 添加关键帧，使用 SplineDoubleKeyFrame 实现自定义贝塞尔曲线
            // KeySpline="0.175,0.885 0.32,1.275" 对应 CSS cubic-bezier(0.175, 0.885, 0.32, 1.275)
            animation.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(AnimationDuration)),
                Value = targetPosition,
                KeySpline = new KeySpline(0.175, 0.885, 0.32, 1.275) // 贝塞尔曲�?
            });

            // 应用动画�?Thumb �?Canvas.Left 属�?
            Thumb.BeginAnimation(Canvas.LeftProperty, animation);
        }

        private void AnimateTrackColor(Color targetColor)
        {
            // 颜色渐变动画 - 使用弹性缓动函�?
            var animation = new ColorAnimation
            {
                To = targetColor,
                Duration = TimeSpan.FromSeconds(AnimationDuration),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            // 应用动画�?TrackBrush �?Color 属�?
            TrackBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        private void AnimateTextVisibility()
        {
            // YES 文字动画
            var yesAnimation = new DoubleAnimationUsingKeyFrames();
            yesAnimation.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(AnimationDuration)),
                Value = IsChecked ? 1 : 0,
                KeySpline = new KeySpline(0.175, 0.885, 0.32, 1.275)
            });
            txtYes.BeginAnimation(UIElement.OpacityProperty, yesAnimation);

            // NO 文字动画
            var noAnimation = new DoubleAnimationUsingKeyFrames();
            noAnimation.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(AnimationDuration)),
                Value = IsChecked ? 0 : 1,
                KeySpline = new KeySpline(0.175, 0.885, 0.32, 1.275)
            });
            txtNo.BeginAnimation(UIElement.OpacityProperty, noAnimation);

            // YES 文字位置微调动画（向左滑出效果）
            var yesMarginAnimation = new ThicknessAnimationUsingKeyFrames();
            yesMarginAnimation.KeyFrames.Add(new SplineThicknessKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(AnimationDuration)),
                Value = IsChecked ? new Thickness(0, 0, 8, 0) : new Thickness(-5, 0, 8, 0),
                KeySpline = new KeySpline(0.175, 0.885, 0.32, 1.275)
            });
            txtYes.BeginAnimation(FrameworkElement.MarginProperty, yesMarginAnimation);

            // NO 文字位置微调动画（向右滑出效果）
            var noMarginAnimation = new ThicknessAnimationUsingKeyFrames();
            noMarginAnimation.KeyFrames.Add(new SplineThicknessKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(AnimationDuration)),
                Value = IsChecked ? new Thickness(13, 0, 0, 0) : new Thickness(8, 0, 0, 0),
                KeySpline = new KeySpline(0.175, 0.885, 0.32, 1.275)
            });
            txtNo.BeginAnimation(FrameworkElement.MarginProperty, noMarginAnimation);
        }
    }
}

