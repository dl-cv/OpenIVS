"""通过实际工程的检查目标验证 OpenCV 版本自动匹配与诊断，不编译或复制运行库。"""
import os
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
ENV_NAMES = ("OpenCV_DIR", "OpenCV_INCLUDE_DIR", "OpenCV_LIB_DIR", "OpenCvIncludeDir",
             "OpenCvLibDir", "OpenCvBinDir", "OpenCV_BIN_DIR")
PROJECTS = tuple(f"{name}/{name}.vcxproj" for name in (
    "dlcv_infer_cpp", "dlcv_infer_cpp_dll_demo", "dlcv_infer_cpp_dll_demo2", "dlcv_infer_c_demo",
    "dlcv_infer_cpp_qt_demo", "dlcv_infer_cpp_qt_demo2", "dlcv_infer_cpp_qt_demo3", "dlcv_infer_c_qt_demo",
)) + ("Test/dlcv_infer_cpp_test/dlcv_infer_cpp_test.vcxproj",
      "Test/dlcv_infer_c_test/dlcv_infer_c_test.vcxproj",
      "Test/qt_demo/dlcv_infer_cpp_qt_mask_test.vcxproj",
      "Test/qt_demo/dlcv_infer_c_qt_mask_test.vcxproj",
      "DlcvCSharpCppTest/Bridge/DlcvCSharpCppBridge.vcxproj")
BRIDGE = PROJECTS[-1]
RUNTIME_PROJECTS = (PROJECTS[0], *PROJECTS[-3:])


@unittest.skipUnless(os.name == "nt", "依赖检查使用 Windows Visual Studio 工具链")
class OpenCvDiagnosticsTest(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="opencv-diagnostics-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.include, self.library, self.binary = (self.directory / name for name in ("头文件", "库", "运行库"))
        for directory in (self.include, self.library, self.binary):
            directory.mkdir()
        self.environment = {key: value for key, value in os.environ.items()
                            if not key.lower().startswith("opencv")}
        self.environment.update({
            "TEMP": str(self.directory), "TMP": str(self.directory), "TMPDIR": str(self.directory),
            "PYTHONUTF8": "1", "PYTHONIOENCODING": "utf-8",
            "DLCVPRO_INFER_INCLUDE": str(self.directory / "sdk"),
            "OpenCvNoLocalSearch": "true",
        })
        self.touch(self.directory / "sdk/json/json.hpp")

    def touch(self, path):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.touch()

    def validate(self, projects=(PROJECTS[0],), configuration="Debug", target="ValidateOpenCvDependencies",
                 properties=None):
        if properties is None:
            # 全局属性隔离固定安装目录；驱动只调用原工程目标。
            properties = {"OpenCvDiagnosticIncludeDir": self.include, "OpenCvDiagnosticLibDir": self.library,
                          "OpenCvDiagnosticBinDir": self.binary}
        document = ET.Element("Project")
        driver_target = ET.SubElement(document, "Target", Name="Validate")
        for project in projects:
            ET.SubElement(driver_target, "MSBuild", Projects=str(REPO_ROOT / project), Targets=target,
                          Properties=";".join(f"{key}={value}" for key, value in properties.items()),
                          ContinueOnError="ErrorAndContinue")
        driver = self.directory / "driver.csproj"
        ET.ElementTree(document).write(driver, encoding="utf-8", xml_declaration=True)
        return subprocess.run(
            [sys.executable, str(REPO_ROOT / "build_script/build.py"), str(driver),
             "--target", "Validate", "--configuration", configuration, "--platform", "x64"],
            cwd=self.directory, env=self.environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="strict", timeout=120,
        )

    def assert_diagnostics(self, result, projects=(PROJECTS[0],), expected=None):
        self.assertNotEqual(0, result.returncode, result.stdout)
        for project in projects:
            diagnostic = "\n".join(line for line in result.stdout.splitlines()
                                   if " error " in line.lower() and str(REPO_ROOT / project) in line)
            self.assertIn("OpenCV", diagnostic, result.stdout)
            if expected is not None:
                self.assertIn(expected, diagnostic, result.stdout)
            for name in ENV_NAMES:
                value = self.environment.get(name, "")
                status = "已设置" if value else "未设置"
                self.assertIn(f'{name}：{status}，当前值="{value}"', diagnostic)
            for label, directory in (("头文件", self.include), ("库", self.library), ("运行库", self.binary)):
                self.assertIn(f'最终{label}目录："{directory}"', diagnostic)
            self.assertIn("找到的 OpenCV 库", diagnostic)
        return result.stdout

    def assert_success(self, result):
        self.assertEqual(0, result.returncode, result.stdout)
        for text in ("当前值=", "最终头文件目录", "最终库目录", "找到的 OpenCV 库"):
            self.assertNotIn(text, result.stdout)

    def add_opencv(self, version="4110", debug=True, release=True):
        self.touch(self.include / "opencv2/core.hpp")
        if release:
            self.touch(self.library / f"opencv_world{version}.lib")
        if debug:
            self.touch(self.library / f"opencv_world{version}d.lib")

    def test_all_projects_report_missing_header_and_unset_environment(self):
        self.assert_diagnostics(self.validate(PROJECTS), PROJECTS)

    def test_process_environment_differs_from_final_project_paths(self):
        self.environment.update({name: str(self.directory / name) for name in ENV_NAMES})
        self.assert_diagnostics(self.validate())

    def test_opencv_4110_is_auto_matched_for_all_linking_projects(self):
        self.add_opencv("4110")
        for configuration in ("Debug", "Release"):
            with self.subTest(configuration=configuration):
                self.assert_success(self.validate(PROJECTS, configuration))

    def test_opencv_4100_is_still_accepted(self):
        self.add_opencv("4100")
        for configuration in ("Debug", "Release"):
            with self.subTest(configuration=configuration):
                self.assert_success(self.validate(PROJECTS, configuration))

    def test_missing_debug_library_reports_matched_release_name(self):
        self.add_opencv("4110", debug=False)
        output = self.assert_diagnostics(self.validate(PROJECTS[:-1], "Debug"), PROJECTS[:-1],
                                         "opencv_world4110d.lib")
        self.assertIn("opencv_world4110.lib", output)

    def test_bridge_requires_header_without_link_library(self):
        self.touch(self.include / "opencv2/core.hpp")
        self.assert_success(self.validate((BRIDGE,)))

    def test_missing_runtime_uses_auto_matched_name(self):
        self.add_opencv("4110")
        self.touch(self.binary / "opencv_world4110d.dll")
        self.assert_success(self.validate(RUNTIME_PROJECTS, "Debug", "ValidateOpenCvRuntime"))
        self.touch(self.binary / "opencv_world4110.dll")
        self.assert_success(self.validate(RUNTIME_PROJECTS, "Release", "ValidateOpenCvRuntime"))

    def test_opencv_dir_candidates_are_auto_matched(self):
        build = self.directory / "OpenCV/build"
        self.touch(build / "include/opencv2/core.hpp")
        self.touch(build / "x64/vc16/lib/opencv_world4110.lib")
        self.touch(build / "x64/vc16/lib/opencv_world4110d.lib")
        self.environment["OpenCV_DIR"] = str(build)
        self.assert_success(self.validate(properties={}))

    def test_original_validation_target_names_remain_callable(self):
        self.assert_diagnostics(self.validate(PROJECTS[:3], target="ValidatePortableDeps"), PROJECTS[:3])
        self.assert_diagnostics(self.validate((PROJECTS[0],), target="ValidateOpenCvDependencies"), (PROJECTS[0],))


if __name__ == "__main__":
    unittest.main()
