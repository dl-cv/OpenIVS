using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DlcvDemo
{
    public class NumericBox : Control
    {
        private TextBox _textBox;

        static NumericBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(NumericBox), new FrameworkPropertyMetadata(typeof(NumericBox)));
        }

        public NumericBox()
        {
            Focusable = true;
            Height = 34;
            MinWidth = 72;
        }

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(decimal), typeof(NumericBox),
            new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(decimal), typeof(NumericBox), new PropertyMetadata(0m, OnRangeChanged));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(decimal), typeof(NumericBox), new PropertyMetadata(100m, OnRangeChanged));

        public static readonly DependencyProperty IncrementProperty = DependencyProperty.Register(
            nameof(Increment), typeof(decimal), typeof(NumericBox), new PropertyMetadata(1m));

        public static readonly DependencyProperty DecimalPlacesProperty = DependencyProperty.Register(
            nameof(DecimalPlaces), typeof(int), typeof(NumericBox), new PropertyMetadata(0, OnDecimalPlacesChanged, CoerceDecimalPlaces));

        public decimal Value
        {
            get => (decimal)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public decimal Minimum
        {
            get => (decimal)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public decimal Maximum
        {
            get => (decimal)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public decimal Increment
        {
            get => (decimal)GetValue(IncrementProperty);
            set => SetValue(IncrementProperty, value);
        }

        public int DecimalPlaces
        {
            get => (int)GetValue(DecimalPlacesProperty);
            set => SetValue(DecimalPlacesProperty, value);
        }

        public event EventHandler ValueChanged;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (_textBox != null)
            {
                _textBox.LostKeyboardFocus -= TextBox_LostKeyboardFocus;
                _textBox.KeyDown -= TextBox_KeyDown;
            }
            _textBox = GetTemplateChild("PART_TextBox") as TextBox;
            if (_textBox != null)
            {
                _textBox.LostKeyboardFocus += TextBox_LostKeyboardFocus;
                _textBox.KeyDown += TextBox_KeyDown;
                UpdateText();
            }
        }

        internal void ChangeValue(int direction)
        {
            CommitText();
            Value += Increment * direction;
        }

        private void TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            CommitText();
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitText();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                ChangeValue(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                ChangeValue(-1);
                e.Handled = true;
            }
        }

        private void CommitText()
        {
            if (_textBox == null) return;
            decimal parsed;
            if (decimal.TryParse(_textBox.Text, out parsed))
            {
                Value = parsed;
            }
            UpdateText();
        }

        private void UpdateText()
        {
            if (_textBox != null)
            {
                _textBox.Text = Value.ToString("F" + DecimalPlaces);
            }
        }

        private static object CoerceValue(DependencyObject d, object baseValue)
        {
            var control = (NumericBox)d;
            var value = (decimal)baseValue;
            if (value < control.Minimum) return control.Minimum;
            if (value > control.Maximum) return control.Maximum;
            return decimal.Round(value, control.DecimalPlaces);
        }

        private static object CoerceDecimalPlaces(DependencyObject d, object baseValue)
        {
            int value = (int)baseValue;
            return Math.Max(0, Math.Min(6, value));
        }

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            d.CoerceValue(ValueProperty);
        }

        private static void OnDecimalPlacesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            d.CoerceValue(ValueProperty);
            ((NumericBox)d).UpdateText();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (NumericBox)d;
            control.UpdateText();
            control.ValueChanged?.Invoke(control, EventArgs.Empty);
        }
    }

    public class NumericBoxButton : Button
    {
        public int Direction { get; set; }

        protected override void OnClick()
        {
            base.OnClick();
            DependencyObject current = this;
            while (current != null)
            {
                var numeric = current as NumericBox;
                if (numeric != null)
                {
                    numeric.ChangeValue(Direction);
                    return;
                }
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
        }
    }
}
