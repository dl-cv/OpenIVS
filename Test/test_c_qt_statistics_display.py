"""检查 C Qt 结果文本使用已复制的 C ABI 均值字段。"""
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
WINDOW = (ROOT / "dlcv_infer_c_qt_demo" / "MainWindow.cpp").read_text(encoding="utf-8-sig")


def window_section(start, end):
    start_index = WINDOW.index(start)
    return WINDOW[start_index:WINDOW.index(end, start_index)]


class CQtStatisticsDisplaySourceTest(unittest.TestCase):
    def test_single_inference_formats_copied_result_without_json(self):
        body = window_section("bool MainWindow::inferCurrentImage()", "void MainWindow::onInferJson()")
        self.assertEqual(body.count("api_.inferWithParams("), 1)
        self.assertNotIn("api_.inferJson(", body)
        self.assertIn("const std::vector<DisplayObjectResult> firstResults = copyFirstSample(result.get());", body)
        self.assertIn("text += formatResultText(firstResults);", body)
        self.assertLess(body.index("copyFirstSample(result.get())"), body.index("formatResultText(firstResults)"))
        self.assertLess(body.index("formatResultText(firstResults)"), body.index("outputText_->setPlainText(text)"))

    def test_mean_format_includes_nan_and_preserves_numeric_zero(self):
        body = window_section("QString MainWindow::formatResultText(", "std::vector<DisplayObjectResult> MainWindow::copyFirstSample(")
        self.assertIn(
            "if (object.withMean || std::isnan(object.foregroundMean) || std::isnan(object.backgroundMean))",
            " ".join(body.split()),
        )
        self.assertIn('QString("  前景均值=%1  背景均值=%2")', body)
        for field in ("foregroundMean", "backgroundMean"):
            with self.subTest(field=field):
                self.assertIn(
                    f"std::isfinite(object.{field}) ? QString::number(object.{field}, 'f', 4) : QStringLiteral(\"无采样\")",
                    " ".join(body.split()),
                )
        self.assertNotRegex(body, r"(?i)median")

    def test_mean_fields_are_copied_directly_from_c_abi(self):
        body = window_section("std::vector<DisplayObjectResult> MainWindow::copyFirstSample(", "void MainWindow::onLoadModel()")
        for target, source in (
            ("withMean", "with_mean"),
            ("foregroundMean", "foreground_mean"),
            ("backgroundMean", "background_mean"),
        ):
            with self.subTest(field=target):
                self.assertIn(f"target.{target} = source.{source};", body)
        display = (ROOT / "dlcv_infer_c_qt_demo" / "DisplayResult.h").read_text(encoding="utf-8-sig")
        self.assertNotRegex(display, r"(?i)median")

    def test_statistics_json_formatter_and_tooltips_are_removed(self):
        for removed in ("formatStatisticsText", "#include <QStringList>", "outputText_->setToolTip(", "统计（JSON）"):
            with self.subTest(text=removed):
                self.assertFalse(removed in WINDOW, removed)

    def test_json_button_keeps_original_c_api_output(self):
        body = window_section("void MainWindow::onInferJson()", "void MainWindow::onPressureTest()")
        self.assertEqual(body.count("api_.inferJson("), 1)
        self.assertNotIn("api_.inferWithParams(", body)
        self.assertIn("CStringGuard result(api_, api_.inferJson(modelIndex_, &image, paramsText.c_str()));", body)
        self.assertIn("if (result.get() == nullptr)", body)
        self.assertIn('reportError("推理JSON失败", lastCError());', body)
        self.assertIn("outputText_->setPlainText(prettyJson(result.get(), 4));", body)


if __name__ == "__main__":
    unittest.main()
