import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


class SlidingWindowApiRemovedTest(unittest.TestCase):
    def read_source(self, relative_path):
        return (REPOSITORY_ROOT / relative_path).read_text(encoding="utf-8")

    def test_legacy_sliding_window_model_is_absent(self):
        source_files = [
            "AGENTS.md",
            "C++ API文档.md",
            "C# API文档.md",
            "dlcv_infer_cpp/dlcv_infer.h",
            "dlcv_infer_cpp/dlcv_infer.cpp",
            "DlcvCsharpApi/Model.cs",
        ]
        for relative_path in source_files:
            with self.subTest(relative_path=relative_path):
                self.assertNotIn(
                    "SlidingWindowModel",
                    self.read_source(relative_path),
                    "旧独立滑窗模型 API 尚未清理。",
                )

    def test_halcon_legacy_configuration_entry_is_absent(self):
        source_files = [
            "HalconDemo/Form1.cs",
            "HalconDemo/Form1.Designer.cs",
            "HalconDemo/HalconDemo.csproj",
        ]
        for relative_path in source_files:
            with self.subTest(relative_path=relative_path):
                self.assertNotIn(
                    "SlidingWindowConfigForm",
                    self.read_source(relative_path),
                    "Halcon 旧滑窗配置入口仍被引用。",
                )

        for file_name in [
            "SlidingWindowConfigForm.cs",
            "SlidingWindowConfigForm.Designer.cs",
            "SlidingWindowConfigForm.resx",
        ]:
            self.assertFalse(
                (REPOSITORY_ROOT / "HalconDemo" / file_name).exists(),
                "Halcon 旧滑窗配置窗体文件仍存在。",
            )

    def test_flow_sliding_window_and_demo2_crop_are_retained(self):
        csharp_flow_source = self.read_source(
            "DlcvCsharpApi/flow/modules/SlidingWindow.cs"
        )
        self.assertIn("sliding_window", csharp_flow_source)

        cpp_flow_source = self.read_source(
            "dlcv_infer_cpp/flow/modules/SlidingModules.cpp"
        )
        self.assertIn(
            'DLCV_FLOW_REGISTER_MODULE("pre_process/sliding_window", SlidingWindowModule)',
            cpp_flow_source,
        )
        self.assertIn(
            'DLCV_FLOW_REGISTER_MODULE("features/sliding_window", SlidingWindowModule)',
            cpp_flow_source,
        )

        demo2_form_source = self.read_source("DlcvDemo2/Form1.cs")
        demo2_utils_source = self.read_source("DlcvDemo2/Utils/SlidingWindowUtils.cs")
        self.assertIn("SlidingWindowUtils.BuildSlidingWindows", demo2_form_source)
        self.assertIn("CropAndRotateRoi", demo2_form_source)
        self.assertIn("BuildSlidingWindows", demo2_utils_source)


if __name__ == "__main__":
    unittest.main()
