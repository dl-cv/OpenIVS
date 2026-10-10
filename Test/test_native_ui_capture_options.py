"""检查正式窗口采集参数与离屏验证分开执行。"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]

def read_source(relative):
    return (ROOT / relative).read_text(encoding="utf-8-sig")

class NativeUiCaptureSourceTest(unittest.TestCase):
    def test_light_canvas_and_labels_follow_application_colors(self):
        for app in ("dlcv_infer_cpp_qt_demo", "dlcv_infer_c_qt_demo"):
            viewer = read_source(app + "/ImageViewerWidget.cpp")
            self.assertEqual(viewer.count("usePaletteBackground_ ? palette().color(QPalette::Base)"), 2)
            self.assertIn("bool usePaletteBackground_ = false;", read_source(app + "/ImageViewerWidget.h"))
            self.assertIn("imageViewer_->setUsePaletteBackground(true);", read_source(app + "/MainWindow.cpp"))
        designer = read_source("DlcvDemo/MainWindow.Designer.cs")
        self.assertIn("this.imagePanel1.BackColor = Color.White;", designer)
        self.assertIn("this.imagePanel1.VisualizationLabelBackgroundColor = Color.White;", designer)
        self.assertEqual(read_source("ImageViewer/ImageViewer.cs").count("new SolidBrush(VisualizationLabelBackgroundColor)"), 3)

    def test_csharp_delay_is_optional_and_bounded(self):
        source = read_source("DlcvDemo/UiTestOptions.cs")
        self.assertIn("internal int CaptureDelayMs { get; private set; }", source)
        self.assertIn('case "--capture-delay-ms":', source)
        self.assertIn("captureDelayMs < 0 || captureDelayMs > 60000", source)
        self.assertIn("options.CaptureDelayMs = captureDelayMs;", source)

    def test_csharp_label_scale_reuses_live_viewer_property(self):
        options = read_source("DlcvDemo/UiTestOptions.cs")
        self.assertIn("float.IsNaN(labelFontScale) || labelFontScale < 0.3f || labelFontScale > 5.0f", options)
        self.assertIn("imagePanel1.LabelFontScale = uiTestOptions.LabelFontScale;", read_source("DlcvDemo/MainWindow.cs"))

    def test_csharp_waits_only_after_success(self):
        source = read_source("DlcvDemo/MainWindow.cs")
        self.assertIn("UiTestExitCode == 0 && uiTestOptions.CaptureDelayMs > 0", source)
        self.assertLess(source.index("WriteUiTestResult(\"passed\", null);"), source.index("await Task.Delay(uiTestOptions.CaptureDelayMs);"))

    def test_native_cqt_writes_real_result_json_not_widget_png(self):
        source = read_source("dlcv_infer_c_qt_demo/MainWindow.cpp")
        start = source.index("bool MainWindow::runOffscreenInference(")
        source = source[start:source.index("void MainWindow::closeEvent", start)]
        self.assertIn('QGuiApplication::platformName() == QStringLiteral("offscreen")', source)
        self.assertEqual(source.count("grab()"), 1)
        self.assertLess(source.index("QApplication::processEvents();", source.index("resize(1280, 800);")), source.index("if (!inferCurrentImage())"))
        native = source.split("    } else {", 1)[1]
        self.assertNotIn("grab()", native)
        self.assertIn('{"result_text", outputText_->toPlainText().toUtf8().toStdString()}', native)
        self.assertIn('{"result_count", offscreenResultCount_}', native)

    def test_cqt_json_view_uses_existing_button_logic(self):
        source = read_source("dlcv_infer_c_qt_demo/MainWindow.cpp")
        self.assertIn('if (resultView == QStringLiteral("json")) {\n        onInferJson();', source)
        self.assertIn('const json& results = result.is_array() ? result : result.at("result_list");', source)
        cli = read_source("dlcv_infer_c_qt_demo/CliRunner.cpp")
        self.assertIn('options.hasResultView || (value != QStringLiteral("summary") && value != QStringLiteral("json"))', cli)
        self.assertIn("artifact.exists() || options.hasWithMask", cli)
        self.assertIn("options.hasScreenshot || options.hasResultView", cli)

if __name__ == "__main__":
    unittest.main()
