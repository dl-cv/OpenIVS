"""通过项目原有入口检查 OpenCV 依赖错误，不编译源码。"""
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
ENV_NAMES = ("OpenCV_DIR", "OpenCV_INCLUDE_DIR", "OpenCV_LIB_DIR", "OpenCvIncludeDir", "OpenCvLibDir")


@unittest.skipUnless(os.name == "nt", "依赖检查使用 Windows Visual Studio 工具链")
class OpenCvDiagnosticsTest(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="opencv-diagnostics-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.include = self.directory / "最终头文件目录"
        self.library = self.directory / "最终库目录"
        self.include.mkdir()
        self.library.mkdir()
        self.environment = {key: value for key, value in os.environ.items()
                            if not key.lower().startswith("opencv")}
        self.environment.update({
            "OpenCvIncludeDir": str(self.include),
            "OpenCvLibDir": str(self.library),
            "TEMP": str(self.directory), "TMP": str(self.directory), "TMPDIR": str(self.directory),
            "PYTHONUTF8": "1", "PYTHONIOENCODING": "utf-8",
        })

    def validate(self, configuration="Debug"):
        return subprocess.run(
            [sys.executable, str(REPO_ROOT / "build_script/build.py"),
             str(REPO_ROOT / "dlcv_infer_cpp/dlcv_infer_cpp.vcxproj"),
             "--target", "ValidatePortableDeps", "--configuration", configuration,
             "--platform", "x64"],
            cwd=self.directory, env=self.environment,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="strict", timeout=60,
        )

    def assert_diagnostics(self, result):
        self.assertNotEqual(0, result.returncode, result.stdout)
        diagnostic = "\n".join(line for line in result.stdout.splitlines() if " error " in line.lower())
        self.assertTrue(diagnostic, result.stdout)
        for name in ENV_NAMES:
            value = self.environment.get(name, "")
            status = "已设置" if value else "未设置"
            self.assertIn(f'{name}：{status}，当前值="{value}"', diagnostic)
        self.assertIn(f'最终头文件目录：OpenCvIncludeDir="{self.include}"', diagnostic)
        self.assertIn(f'最终库目录：OpenCvLibDir="{self.library}"', diagnostic)
        self.assertIn("找到的 OpenCV 库", diagnostic)
        return diagnostic

    def create_header(self):
        header = self.include / "opencv2/core.hpp"
        header.parent.mkdir()
        header.touch()

    def test_unset_environment_is_reported_on_missing_header(self):
        diagnostic = self.assert_diagnostics(self.validate())
        self.assertIn("未找到 OpenCV 头文件", diagnostic)

    def test_process_environment_differs_from_final_project_paths(self):
        self.environment.update({
            "OpenCV_DIR": str(self.directory / "环境安装目录"),
            "OpenCV_INCLUDE_DIR": str(self.directory / "环境头文件目录"),
            "OpenCV_LIB_DIR": str(self.directory / "环境库目录"),
        })
        self.assert_diagnostics(self.validate())

    def test_only_4110_libraries_report_expected_4100_for_each_configuration(self):
        self.create_header()
        for name in ("opencv_world4110.lib", "opencv_world4110d.lib"):
            (self.library / name).touch()
        for configuration, required in (("Debug", "opencv_world4100d.lib"), ("Release", "opencv_world4100.lib")):
            with self.subTest(configuration=configuration):
                diagnostic = self.assert_diagnostics(self.validate(configuration))
                self.assertIn(f"未找到 OpenCV {configuration} 库 {required}", diagnostic)
                self.assertIn("opencv_world4110.lib", diagnostic)
                self.assertIn("opencv_world4110d.lib", diagnostic)

    def test_opencv_dir_candidates_are_listed_when_final_library_directory_is_missing(self):
        self.create_header()
        build = self.directory / "OpenCV/build"
        candidate = build / "x64/vc16/lib/opencv_world4110.lib"
        candidate.parent.mkdir(parents=True)
        candidate.touch()
        self.library = self.directory / "不存在的库目录"
        self.environment.update({"OpenCV_DIR": str(build), "OpenCvLibDir": str(self.library)})
        self.assertIn(str(candidate), self.assert_diagnostics(self.validate("Release")))

    def test_satisfied_dependencies_do_not_print_diagnostics(self):
        self.create_header()
        for name in ("opencv_world4100.lib", "opencv_world4100d.lib"):
            (self.library / name).touch()
        for configuration in ("Debug", "Release"):
            with self.subTest(configuration=configuration):
                result = self.validate(configuration)
                self.assertEqual(0, result.returncode, result.stdout)
                for text in ("当前值=", "最终头文件目录", "最终库目录", "找到的 OpenCV 库"):
                    self.assertNotIn(text, result.stdout)


if __name__ == "__main__":
    unittest.main()
