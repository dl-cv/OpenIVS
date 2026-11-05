import argparse
import os
from pathlib import Path
import runpy
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import build_package
from Test import test_install_target


ROOT = Path(__file__).resolve().parents[1]


class CppPackageFilesTest(unittest.TestCase):
    def test_stages_native_program_and_deploys_matching_qt(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            output = root / "output"
            staging = root / "dlcvpro_infer_cpp"
            qt = root / "Qt" / "lib" / "cmake"
            qt_bin = root / "Qt" / "bin"
            output.mkdir()
            qt.mkdir(parents=True)
            qt_bin.mkdir()
            for name in ("dlcv_infer_cpp_qt_demo.exe", "dlcv_infer_cpp.dll", "ZXing.dll", "old.pdb"):
                (output / name).write_bytes(name.encode())
            for name in ("windeployqt.exe", "qtpaths.exe"):
                (qt_bin / name).touch()
            opencv = root / "opencv_world4100.dll"
            opencv.write_bytes(b"opencv")
            (root / "hasp_26146.ini").write_bytes(b"config")
            staging.mkdir()
            (staging / "old.dll").touch()

            def deploy(name, command, env):
                self.assertEqual(Path(command[0]), qt_bin / "windeployqt.exe")
                self.assertEqual(Path(command[-1]), staging / "C++ 测试程序.exe")
                self.assertIn("--release", command)
                self.assertEqual(env["PATH"].split(os.pathsep)[0], str(qt_bin))
                for name in ("Qt6Core.dll", "Qt6Gui.dll", "Qt6Widgets.dll", "platforms/qwindows.dll"):
                    target = staging / name
                    target.parent.mkdir(exist_ok=True)
                    target.write_bytes(b"qt")

            with (
                patch.object(build_package, "REPO_ROOT", root),
                patch.object(build_package, "CPP_DEMO_OUTPUT_DIR", output),
                patch.object(build_package, "CPP_STAGING_DIR", staging),
                patch.object(build_package, "CPP_OPENCV_DLL", opencv),
                patch.dict(os.environ, {"Qt6_DIR": str(qt)}),
                patch.object(build_package, "run_step", side_effect=deploy),
            ):
                executables = build_package.copy_cpp_package_files()
                self.assertEqual(executables, [staging / "C++ 测试程序.exe"])
                self.assertEqual((staging / "C++ 测试程序.exe").read_bytes(), b"dlcv_infer_cpp_qt_demo.exe")
                self.assertEqual(
                    {p.relative_to(staging).as_posix() for p in staging.rglob("*") if p.is_file()},
                    {"C++ 测试程序.exe", "dlcv_infer_cpp.dll", "ZXing.dll", "opencv_world4100.dll",
                     "hasp_26146.ini", "Qt6Core.dll", "Qt6Gui.dll", "Qt6Widgets.dll", "platforms/qwindows.dll"},
                )
                (output / "ZXing.dll").unlink()
                with self.assertRaises(FileNotFoundError):
                    build_package.copy_cpp_package_files()
                self.assertTrue((staging / "C++ 测试程序.exe").is_file())

    def test_staging_cleanup_rejects_outside_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            outside = root / "other" / "dlcvpro_infer_cpp"
            outside.mkdir(parents=True)
            marker = outside / "keep.txt"
            marker.touch()
            with patch.object(build_package, "REPO_ROOT", root), self.assertRaises(RuntimeError):
                build_package.rebuild_staging_directory(outside, "dlcvpro_infer_cpp")
            self.assertTrue(marker.is_file())

    def test_cpp_build_scope_and_install_use_generated_wheel(self):
        wheel = ROOT / "dist_cpp" / "generated.whl"
        with (
            patch.object(build_package, "parse_args", return_value=argparse.Namespace(install=True, package="cpp")),
            patch.object(build_package.os, "chdir"),
            patch.object(build_package, "require_file"),
            patch.object(build_package, "run_step") as run_step,
            patch.object(build_package, "copy_cpp_package_files", return_value=[]),
            patch.object(build_package, "copy_package_files") as csharp,
            patch.object(build_package, "find_signing_certificate_thumbprint", return_value="test"),
            patch.object(build_package, "snapshot_wheels", return_value={}),
            patch.object(build_package, "find_generated_wheel", return_value=wheel),
        ):
            self.assertEqual(build_package.main(), 0)
        csharp.assert_not_called()
        commands = [call.args[1] for call in run_step.call_args_list]
        projects = [Path(arg) for command in commands for arg in command if arg.endswith((".csproj", ".vcxproj", ".sln"))]
        self.assertEqual(projects, [ROOT / "dlcv_infer_cpp_qt_demo" / "dlcv_infer_cpp_qt_demo.vcxproj"])
        self.assertFalse(any("sync_assembly_version.py" in arg for command in commands for arg in command))
        build_call = next(call for call in run_step.call_args_list if call.args[1][1:3] == ["-m", "build"])
        self.assertEqual(build_call.kwargs["env"]["DLCV_PACKAGE_VARIANT"], "cpp")
        self.assertIn("--no-isolation", build_call.args[1])
        self.assertEqual(commands[-1][-1], str(wheel))

    def test_cpp_build_failure_returns_status_before_copying(self):
        with (
            patch.object(build_package, "parse_args", return_value=argparse.Namespace(install=False, package="cpp")),
            patch.object(build_package.os, "chdir"),
            patch.object(build_package, "require_file"),
            patch.object(build_package, "run_step", side_effect=subprocess.CalledProcessError(17, "build")),
            patch.object(build_package, "copy_cpp_package_files") as copy_files,
        ):
            self.assertEqual(build_package.main(), 17)
        copy_files.assert_not_called()


class PackageMetadataTest(unittest.TestCase):
    def test_cpp_metadata_includes_nested_plugins_and_preserves_csharp_outputs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            package = root / "dlcvpro_infer_cpp"
            (package / "platforms").mkdir(parents=True)
            (package / "C++ 测试程序.exe").touch()
            (package / "platforms" / "qwindows.dll").touch()
            for name in ("build", "dist", "dlcvpro_infer_csharp"):
                (root / name).mkdir()
                (root / name / "keep.txt").touch()
            previous = Path.cwd()
            try:
                os.chdir(root)
                with patch.dict(os.environ, {"DLCV_PACKAGE_VARIANT": "cpp"}), patch("setuptools.setup") as setup:
                    runpy.run_path(str(ROOT / "setup.py"), run_name="__main__")
            finally:
                os.chdir(previous)
            metadata = setup.call_args.kwargs
            self.assertEqual(metadata["name"], "dlcvpro_infer_cpp")
            self.assertEqual(metadata["version"], "2026.11.5.0a0")
            self.assertEqual(set(metadata["package_data"]["dlcvpro_infer_cpp"]),
                             {"C++ 测试程序.exe", "platforms/qwindows.dll"})
            for name in ("build", "dist", "dlcvpro_infer_csharp"):
                self.assertTrue((root / name / "keep.txt").is_file())


class CppInstallTargetTest(test_install_target.InstallTargetTest):
    def setUp(self):
        super().setUp()
        self.dist.rename(self.package / "dist_cpp")
        self.dist = self.package / "dist_cpp"
        self.script.write_bytes((ROOT / "2_安装新版(C++).bat").read_bytes())

    def wheel(self, name="latest package.whl", timestamp=20):
        return super().wheel("dlcvpro_infer_cpp-" + name, timestamp)

    def test_default_keeps_uninstall_install_and_pause(self):
        latest = self.wheel()
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.calls(), [self.expected_call("install", "--force-reinstall", latest)])

    def test_default_returns_install_error_after_pause(self):
        self.wheel()
        result = self.run_script(exit_code=31)
        self.assertEqual(result.returncode, 31, result.stdout + result.stderr)
        self.assertEqual(len(self.calls()), 1)

    def test_csharp_wheel_is_not_selected(self):
        latest = self.wheel()
        other = self.dist / "dlcvpro_infer_csharp-new.whl"
        other.touch()
        result = self.run_script('--target "{}"'.format(self.root / "target"))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.calls()[0]["argv"][-1], latest)


if __name__ == "__main__":
    unittest.main()
