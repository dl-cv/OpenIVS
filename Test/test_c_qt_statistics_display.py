"""检查 C Qt 从同次 C ABI 结果复制并显示通用扩展信息。"""
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

    def test_format_displays_copied_extensions_without_numeric_defaults(self):
        body = window_section("QString MainWindow::formatResultText(", "std::vector<DisplayObjectResult> MainWindow::copyFirstSample(")
        self.assertIn("object.extraInfo.is_object()", body)
        self.assertIn("QString::fromUtf8(object.extraInfo.dump().c_str())", body)
        self.assertIn('extra_info=%1', body)
        self.assertNotIn("object.withMean", body)
        self.assertNotIn("object.foregroundMean", body)
        self.assertNotIn("object.foregroundMedian", body)

    def test_extra_info_is_parsed_before_c_result_is_released(self):
        body = window_section("std::vector<DisplayObjectResult> MainWindow::copyFirstSample(", "void MainWindow::onLoadModel()")
        self.assertIn("if (source.extra_info != nullptr)", body)
        self.assertIn("target.extraInfo = nlohmann::json::parse(source.extra_info);", body)
        self.assertLess(body.index("json::parse(source.extra_info)"), body.index("output.push_back"))
        display = (ROOT / "dlcv_infer_c_qt_demo" / "DisplayResult.h").read_text(encoding="utf-8-sig")
        self.assertIn("nlohmann::json extraInfo;", display)
        for name in ("withMean", "foregroundMean", "backgroundMean", "withMedian", "foregroundMedian", "backgroundMedian"):
            self.assertNotIn(name, display)

    def test_cli_compares_complete_extension_objects_from_both_paths(self):
        for path, structured in (("dlcv_infer_c_qt_demo/CliRunner.cpp", "json::parse(object.extra_info)"),
                                 ("dlcv_infer_cpp_qt_demo/main.cpp", "object.extraInfo")):
            with self.subTest(file=path):
                cli = (ROOT / path).read_text(encoding="utf-8-sig")
                self.assertIn(structured, cli)
                self.assertIn('token.value("extra_info", json(nullptr))', cli)
                self.assertIn("ExtraInfoEquals(left.extraInfos, right.extraInfos", cli)
                for key in ("with_mean", "foreground_mean", "background_mean", "with_median", "foreground_median", "background_median"):
                    self.assertNotIn('"' + key + '"', cli)
                self.assertNotIn("object.with_mean", cli)
                self.assertNotIn("object.withMean", cli)

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
