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

    def test_c_abi_public_result_only_has_generic_extra_info(self):
        header = source("dlcv_infer_cpp/dlcv_infer_c_api.h")
        body = re.search(r"typedef struct DlcvCObjectResult \{(.*?)\} DlcvCObjectResult;",
                         header, re.S).group(1)
        body = re.sub(r"//[^\n]*|/\*.*?\*/", "", body, flags=re.S)
        fields = re.sub(r"\s+", " ", body).strip()
        self.assertEqual(fields, "int category_id; char* category_name; float score; "
            "bool with_bbox; float area; float x, y, w, h; "
            "bool with_mask; DlcvCMask mask; bool with_angle; float angle; char* extra_info;")
        implementation = source("dlcv_infer_cpp/dlcv_infer_c_api.cpp")
        self.assertIn("obj.extraInfo.dump()", implementation)
        self.assertIn("delete[] object.extra_info;", implementation)
        self.assertIn("object.extra_info = nullptr;", implementation)

    def test_public_wrappers_only_have_generic_extension_members(self):
        csharp = source("DlcvCsharpApi/DataTypes.cs").split("public struct CSharpObjectResult", 1)[1]
        csharp = csharp.split("public struct CSharpSampleResult", 1)[0]
        cpp = source("dlcv_infer_cpp/dlcv_infer.h").split("struct ObjectResult {", 1)[1]
        cpp = cpp.split("struct SampleResult", 1)[0]
        self.assertIn("public JObject ExtraInfo { get; set; }", csharp)
        self.assertIn("json extraInfo;", cpp)
        self.assertEqual(1, csharp.count("public CSharpObjectResult("))
        self.assertIn("ExtraInfo = extraInfo;", csharp)
        self.assertNotIn("new JObject()", csharp)
        self.assertEqual(1, cpp.count("ObjectResult(int"))
        for name in ("WithMean", "ForegroundMean", "BackgroundMean", "WithMedian",
                     "ForegroundMedian", "BackgroundMedian"):
            self.assertNotRegex(csharp, rf"public (?:bool|double) {name}\b")
            self.assertNotRegex(csharp, rf"\b{name[0].lower() + name[1:]}\s*[,)]")
            self.assertNotRegex(cpp, rf"\b(?:bool|double) {name[0].lower() + name[1:]}\b")

    def test_statistics_nodes_write_groups_to_extra_info(self):
        csharp = source("DlcvCsharpApi/flow/modules/ForegroundBackgroundStatistics.cs")
        cpp = source("dlcv_infer_cpp/flow/modules/ForegroundBackgroundStatisticsModule.cpp")
        self.assertIn('var extraInfo = extraToken as JObject ?? new JObject();', csharp)
        self.assertIn('extraInfo.Remove(prefix + name);', csharp)
        self.assertIn('det.Remove(prefix + name);', csharp)
        self.assertIn('extraToken.Type != JTokenType.Null && !(extraToken is JObject)', csharp)
        self.assertIn('if (extraInfo.HasValues) det["extra_info"] = extraInfo;', csharp)
        self.assertIn('else det.Remove("extra_info");', csharp)
        self.assertIn('if (mean) SetStatistic(extraInfo, "mean"', csharp)
        self.assertIn('if (median) SetStatistic(extraInfo, "median"', csharp)
        self.assertIn('Json& extraInfo = detection["extra_info"];', cpp)
        self.assertIn('detection["extra_info"].erase(name);', cpp)
        self.assertIn('if (mean) extraInfo["with_mean"] = sampled;', cpp)
        self.assertIn('if (median) extraInfo["with_median"] = sampled;', cpp)

    def test_csharp_display_uses_existing_generic_formatter(self):
        result = source("DlcvCsharpApi/DataTypes.cs")
        gui = source("DlcvDemo/MainWindow.cs")
        self.assertIn("FormatExtraInfoForDisplay(ExtraInfo)", result)
        self.assertIn("Utils.FormatExtraInfoForDisplay(obj.ExtraInfo)", gui)
        for key in ("with_mean", "foreground_mean", "background_mean", "with_median",
                    "foreground_median", "background_median"):
            self.assertNotIn('"' + key + '"', gui)

    def test_csharp_result_parsers_only_forward_generic_extensions(self):
        for path in ("DlcvCsharpApi/Model.cs", "DlcvCsharpApi/flow/FlowGraphModel.cs",
                     "DlcvCsharpApi/flow/modules/Models.cs", "DlcvCsharpApi/Utils.cs"):
            with self.subTest(file=path):
                text = source(path)
                self.assertIn('"extra_info"', text)
                for key in ("with_mean", "foreground_mean", "background_mean", "with_median",
                            "foreground_median", "background_median"):
                    self.assertNotRegex(text, rf'\["{key}"\]\s*=')

    def test_csharp_cli_preserves_generic_extensions_without_statistics_arrays(self):
        cli = source("DlcvDemo/CliRunner.cs")
        self.assertIn("item.ExtraInfo, threshold", cli)
        self.assertIn('extraInfo = item["extra_info"] as JObject;', cli)
        self.assertIn('["extra_info"] = new JArray(ExtraInfos)', cli)
        self.assertIn("JToken.DeepEquals(left.ExtraInfos[i], right.ExtraInfos[i])", cli)
        for field in ("with_mean", "foreground_mean", "background_mean", "with_median", "foreground_median", "background_median"):
            self.assertNotIn('"' + field + '"', cli)

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

    def test_flow_outputs_preserve_generic_extensions_without_top_level_statistics(self):
        for path in ("DlcvCsharpApi/flow/modules/Outputs.cs", "dlcv_infer_cpp/flow/modules/OutputModules.cpp"):
            text = source(path)
            with self.subTest(file=path):
                self.assertIn('"extra_info"', text)
                for key in ("with_mean", "foreground_mean", "background_mean", "with_median",
                            "foreground_median", "background_median"):
                    self.assertNotIn('"' + key + '"', text)
        self.assertIn('extraInfoSource.DeepClone()', source("DlcvCsharpApi/flow/modules/Outputs.cs"))
        self.assertIn('Json extraInfo = detection.at("extra_info");',
                      source("dlcv_infer_cpp/flow/modules/OutputModules.cpp"))


if __name__ == "__main__":
    unittest.main()
