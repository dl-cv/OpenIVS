"""检查流程统计节点的注册与调用。"""
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[1]


def source(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


class StatisticsModuleSourceTest(unittest.TestCase):
    def test_standard_flow_port_types_are_routed(self):
        csharp = source("DlcvCsharpApi/flow/GraphExecutor.cs")
        cpp = source("dlcv_infer_cpp/flow/GraphExecutor.cpp")
        for name in ("image_chan", "result_chan"):
            self.assertIn('string.Equals(dtype, "' + name + '"', csharp)
            self.assertIn('dtypeLower == "' + name + '"', cpp)
        self.assertIn("TestFlowPortTypes", source("Test/DlcvCSharpTest/ForegroundBackgroundStatisticsSelfTest.cs"))
        self.assertIn("结果路由丢失", source("Test/dlcv_infer_cpp_test/main.cpp"))

    def test_c_abi_result_layout_is_unchanged(self):
        header = source("dlcv_infer_cpp/dlcv_infer_c_api.h")
        body = re.search(r"typedef struct DlcvCObjectResult \{(.*?)\} DlcvCObjectResult;",
            header, re.S).group(1)
        fields = re.sub(r"\s+", " ", body).strip()
        self.assertEqual(fields, "int category_id; char* category_name; float score; "
            "bool with_bbox; float area; float x, y, w, h; "
            "bool with_mask; DlcvCMask mask; bool with_angle; float angle; "
            "bool with_mean; double foreground_mean; double background_mean;")

    def test_statistics_result_members_keep_original_types_and_constructors(self):
        csharp = source("DlcvCsharpApi/DataTypes.cs")
        cpp = source("dlcv_infer_cpp/dlcv_infer.h")
        for field in ("WithMean", "WithMedian"):
            self.assertRegex(csharp, rf"public bool {field} \{{ get; set; \}}")
        for field in ("ForegroundMean", "BackgroundMean", "ForegroundMedian", "BackgroundMedian"):
            self.assertRegex(csharp, rf"public double {field} \{{ get; set; \}}")
        for field, kind in (("withMean", "bool"), ("foregroundMean", "double"),
                            ("backgroundMean", "double")):
            self.assertIn(f"{kind} {field};", cpp)
        self.assertIn("false, 0.0, 0.0", csharp)
        self.assertIn("false, 0.0, 0.0", cpp)
        self.assertNotIn("WriteStatistics(", csharp)
        self.assertNotIn("ReadStatistics(", csharp)
        self.assertNotIn("WriteStatistics(", cpp)
        self.assertNotIn("ReadStatistics(", cpp)

    def test_flow_outputs_node_statistics(self):
        for path in ("DlcvCsharpApi/flow/modules/Outputs.cs",
            "dlcv_infer_cpp/flow/modules/OutputModules.cpp"):
            text = source(path)
            for field in ("with_mean", "foreground_mean", "background_mean",
                "with_median", "foreground_median", "background_median"):
                with self.subTest(file=path, field=field):
                    self.assertIn('"' + field + '"', text)

    def test_statistics_nodes_are_registered_and_included_in_projects(self):
        module_type = "post_process/foreground_background_statistics"
        for path, project in (
            ("DlcvCsharpApi/flow/modules/ForegroundBackgroundStatistics.cs", "DlcvCsharpApi/DlcvCsharpApi.csproj"),
            ("dlcv_infer_cpp/flow/modules/ForegroundBackgroundStatisticsModule.cpp", "dlcv_infer_cpp/dlcv_infer_cpp.vcxproj"),
        ):
            with self.subTest(file=path):
                self.assertIn(module_type, source(path))
                self.assertIn(Path(path).name, source(project))
                self.assertNotRegex(source(path), r"(?i)cvtcolor|bgr2gray|rgb2gray")

    def test_statistics_sample_original_not_processed_image(self):
        csharp = source("DlcvCsharpApi/flow/modules/ForegroundBackgroundStatistics.cs")
        cpp = source("dlcv_infer_cpp/flow/modules/ForegroundBackgroundStatisticsModule.cpp")
        self.assertIn("Mat image = wrap.OriginalImage", csharp)
        self.assertIn("const cv::Mat& image = wrap.OriginalImage", cpp)
        self.assertIn("TransformationState.Inverse2x3(Matrix(source))", csharp)
        self.assertIn("source.matrix.inv() *", cpp)
        for text in (csharp, cpp):
            self.assertNotIn("wrap.ImageObject", text)
            self.assertNotIn("current.matrix * source.matrix.inv()", text)
        for path, interpolation in (
            ("Test/DlcvCSharpTest/ForegroundBackgroundStatisticsSelfTest.cs", "InterpolationFlags.Linear"),
            ("Test/dlcv_infer_cpp_test/main.cpp", "cv::INTER_LINEAR"),
        ):
            self.assertIn(interpolation, source(path), path)
            self.assertIn("OriginalImage", source(path), path)

    def test_cpp_selftest_reuses_node_through_project_sources(self):
        project = source("Test/dlcv_infer_cpp_test/dlcv_infer_cpp_test.vcxproj")
        main = source("Test/dlcv_infer_cpp_test/main.cpp")
        self.assertIn("ForegroundBackgroundStatisticsModule.cpp", project)
        self.assertNotIn('ForegroundBackgroundStatisticsModule.cpp"', main)

    def test_statistics_selftests_have_cli_entries(self):
        for path in ("Test/DlcvCSharpTest/Program.cs", "Test/dlcv_infer_cpp_test/main.cpp"):
            with self.subTest(file=path):
                self.assertIn("foreground-background-statistics-selftest", source(path))

    def test_c_qt_ui_test_keeps_native_window_for_full_capture(self):
        cli = source("dlcv_infer_c_qt_demo/CliRunner.cpp")
        window = source("dlcv_infer_c_qt_demo/MainWindow.cpp")
        self.assertIn('QStringLiteral("windows")', cli)
        self.assertIn("QTimer::singleShot(15000, &window, &QWidget::close)", cli)
        self.assertIn("return QApplication::exec()", cli)
        entry = window[window.index("bool MainWindow::runOffscreenInference("):window.index("void MainWindow::closeEvent(")]
        self.assertLess(entry.index("show();"), entry.index("if (!inferCurrentImage())"))
        self.assertNotIn("comboDevice_->addItem", entry)
        self.assertNotIn("deviceNameToId_.insert", entry)

    def test_flow_outputs_preserve_statistics_without_numeric_defaults(self):
        for path in ("DlcvCsharpApi/flow/modules/Outputs.cs", "dlcv_infer_cpp/flow/modules/OutputModules.cpp"):
            text = source(path)
            with self.subTest(file=path):
                self.assertIn('"with_median"', text)
                self.assertIn('"foreground_median"', text)
                self.assertIn('"background_median"', text)
                self.assertNotRegex(text, r'foreground_(?:mean|median)"\]\?\.Value<double>\(\) \?\? 0')


if __name__ == "__main__":
    unittest.main()
