"""检查 C# 测试程序参数排版及窗口宽度变化的源码约束。"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
DESIGNER = (ROOT / "DlcvDemo/MainWindow.Designer.cs").read_text(encoding="utf-8-sig")
WINDOW = (ROOT / "DlcvDemo/MainWindow.cs").read_text(encoding="utf-8-sig")


class CsharpDemoLayoutSourceTest(unittest.TestCase):
    def test_default_window_is_narrower_without_changing_height(self):
        self.assertIn("this.Size = new System.Drawing.Size(1160, 820);", DESIGNER)
        self.assertIn("this.MinimumSize = new System.Drawing.Size(1040, 500);", DESIGNER)

    def test_parameter_controls_share_vertical_center(self):
        controls = (
            "label_choose_gpu", "label_batch_size", "label_num_thread", "label_threshold",
            "numericUpDown_batch_size", "numericUpDown_num_thread", "numericUpDown_threshold",
            "checkBox_rpc_mode",
        )
        for name in controls:
            with self.subTest(control=name):
                self.assertIn(
                    f"this.{name}.Anchor = System.Windows.Forms.AnchorStyles.Left;", DESIGNER)
                match = re.search(
                    rf"this\.{name}\.Margin = new System.Windows.Forms.Padding\((\d+), (\d+), (\d+), (\d+)\);",
                    DESIGNER)
                self.assertIsNotNone(match)
                self.assertEqual(match[2], match[4], "上下边距须相等")

    def test_parameter_rows_have_equal_height_and_shared_columns(self):
        rows = re.findall(r"this\.paramsTable\.RowStyles\.Add\((.+)\);", DESIGNER)
        self.assertEqual(len(rows), 3)
        self.assertEqual(len(set(rows)), 1)
        self.assertIn("SizeType.Percent", rows[0])
        for label, field, row in (
            ("label_batch_size", "numericUpDown_batch_size", 1),
            ("label_num_thread", "numericUpDown_num_thread", 2),
        ):
            self.assertIn(f"this.paramsTable.Controls.Add(this.{label}, 0, {row});", DESIGNER)
            self.assertIn(f"this.paramsTable.Controls.Add(this.{field}, 1, {row});", DESIGNER)

    def test_control_sizes_do_not_depend_on_window_width(self):
        start = WINDOW.index("private void UpdateControlSizesForDpi()")
        end = WINDOW.index("private void MainWindow_Shown", start)
        method = WINDOW[start:end]
        for forbidden in ("ClientSize", "compact", "1160"):
            self.assertNotIn(forbidden, method)
        self.assertNotRegex(WINDOW, r"SizeChanged\s*\+=.*UpdateControlSizesForDpi")
        self.assertIn("DeviceDpi / 96F", method)
        self.assertIn("UpdateControlSizesForDpi();", WINDOW[WINDOW.index("private void MainWindow_DpiChanged"):start])

    def test_right_action_grid_keeps_language_at_left(self):
        self.assertIn("this.rightOpsTable.Controls.Add(this.button_language, 0, 0);", DESIGNER)
        self.assertIn("this.rightOpsTable.SetColumnSpan(this.button_open_image, 2);", DESIGNER)
        self.assertNotIn("button_github", DESIGNER + WINDOW)


if __name__ == "__main__":
    unittest.main()
