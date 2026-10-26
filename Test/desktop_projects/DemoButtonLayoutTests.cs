using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace DlcvDemo
{
    public partial class MainWindow
    {
        private JObject buttonLayoutCheck;

        private JObject RunButtonLayoutUiChecks()
        {
            Size originalSize = Size;
            string originalLanguage = I18n.CurrentLanguage;
            int samples = 0;
            var testWidths = new SortedSet<int> { originalSize.Width, MinimumSize.Width,
                MinimumSize.Width + 1, (originalSize.Width + MinimumSize.Width) / 2 };
            int previousBreakpoint = (int)Math.Round(1160 * DeviceDpi / 96.0) + Width - topCard.ClientSize.Width;
            for (int offset = -1; offset <= 1; offset++)
                testWidths.Add(Math.Max(MinimumSize.Width, Math.Min(originalSize.Width, previousBreakpoint + offset)));
            try
            {
                foreach (string language in new[] { I18n.Chinese, I18n.English })
                {
                    I18n.SetLanguage(language, persist: false);
                    I18n.ApplyTo(this);
                    RefreshDynamicTexts();
                    UpdateControlSizesForDpi();
                    var widths = new Dictionary<Control, int>();
                    foreach (TableLayoutPanel table in new[] { leftOpsTable, rightOpsTable })
                    {
                        foreach (Control control in table.Controls)
                        {
                            if (control is Button) widths.Add(control, control.Width);
                        }
                    }
                    // 覆盖旧尺寸切换点相邻像素，以及最小和默认宽度的往返变化。
                    for (int direction = 0; direction < 2; direction++)
                    {
                        var orderedWidths = new List<int>(testWidths);
                        if (direction == 0) orderedWidths.Reverse();
                        foreach (int width in orderedWidths)
                        {
                            Width = width;
                            PerformLayout();
                            foreach (var entry in widths)
                            {
                                Control control = entry.Key;
                                RequireButtonLayout(control.Width == entry.Value, control.Name + " 随窗口宽度改变尺寸。");
                                RequireButtonLayout(control.Parent.ClientRectangle.Contains(control.Bounds), control.Name + " 超出父容器。");
                                var button = (Button)control;
                                RequireButtonLayout(button.TextAlign == ContentAlignment.MiddleCenter
                                    && button.Padding.Left == button.Padding.Right
                                    && button.Padding.Top == button.Padding.Bottom, button.Name + " 文字未使用居中布局。");
                            }
                            CheckSpanningButton(button_load_model, button_infer, button_infer_json);
                            CheckSpanningButton(button_open_image, button_free_model, button_free_all_model);
                            CheckParameterCenter(label_batch_size, numericUpDown_batch_size);
                            CheckParameterCenter(label_num_thread, numericUpDown_num_thread);
                            CheckParameterCenter(label_threshold, numericUpDown_threshold);
                            samples++;
                        }
                    }
                }
                return new JObject { ["passed"] = true, ["dpi"] = DeviceDpi, ["window_width_samples"] = samples };
            }
            finally
            {
                Size = originalSize;
                I18n.SetLanguage(originalLanguage, persist: false);
                I18n.ApplyTo(this);
                RefreshDynamicTexts();
                UpdateControlSizesForDpi();
                PerformLayout();
            }
        }

        private static void CheckSpanningButton(Button spanning, Button first, Button last)
        {
            RequireButtonLayout(spanning.Left == first.Left && spanning.Right == last.Right,
                spanning.Name + " 未与下方两列使用相同左右端点。");
        }

        private static void CheckParameterCenter(Label label, NumericUpDown input)
        {
            RequireButtonLayout(Math.Abs(2 * label.Top + label.Height - 2 * input.Top - input.Height) <= 2,
                label.Name + " 与输入框的垂直中心不同。");
            RequireButtonLayout(input.Parent.ClientRectangle.Contains(input.Bounds), input.Name + " 超出参数区。");
        }

        private static void RequireButtonLayout(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
