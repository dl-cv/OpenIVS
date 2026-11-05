import argparse
import os
from pathlib import Path
import runpy
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import build_cpp_package as build_package
from Test import test_install_target


ROOT = Path(__file__).resolve().parents[1]


class CppPackageFilesTest(unittest.TestCase):
    def test_stages_only_required_demo_files_and_reuses_shared_runtime(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            output = root / "output"
            staging = root / "dlcvpro_infer_cpp"
            output.mkdir()
            required = {"dlcv_infer_cpp.dll"}
            extras = {"old.pdb", "ZXing.dll", "opencv_world4100.dll", "opengl32sw.dll", "dxcompiler.dll",
                      "D3Dcompiler_47.dll", "Qt6Network.dll", "imageformats/qjpeg.dll", "Qt6Svg.dll",
                      "Qt6Core.dll", "Qt6Gui.dll", "Qt6Widgets.dll", "platforms/qwindows.dll",
                      "styles/qmodernwindowsstyle.dll"}
            for name in required | extras | {"dlcv_infer_cpp_qt_demo.exe"}:
                target = output / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(name.encode())
            runtime = root / "shared"
            runtime.mkdir()
            for name in ("ZXing.dll", "opencv_world4100.dll", "Qt6Core.dll", "Qt6Gui.dll", "Qt6Widgets.dll",
                         "qwindows.dll", "styles/qmodernwindowsstyle.dll"):
                target = runtime / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(b"shared runtime")
            (root / "hasp_26146.ini").write_bytes(b"config")
            staging.mkdir()
            (staging / "old.dll").touch()

            with (
                patch.object(build_package, "REPO_ROOT", root),
                patch.object(build_package, "DEMO_OUTPUT_DIR", output),
                patch.object(build_package, "STAGING_DIR", staging),
                patch.object(build_package, "SHARED_RUNTIME_DIR", runtime),
                patch.object(build_package, "run_step") as run_step,
            ):
                executable = build_package.copy_package_files()
                self.assertEqual(executable, staging / "C++ 测试程序.exe")
                self.assertEqual((staging / "C++ 测试程序.exe").read_bytes(), b"dlcv_infer_cpp_qt_demo.exe")
                self.assertEqual(
                    {p.relative_to(staging).as_posix() for p in staging.rglob("*") if p.is_file()},
                    required | {"C++ 测试程序.exe", "hasp_26146.ini"},
                )
                run_step.assert_not_called()
                for missing in (runtime / "opencv_world4100.dll", runtime / "Qt6Core.dll",
                                runtime / "qwindows.dll", runtime / "styles/qmodernwindowsstyle.dll", runtime / "ZXing.dll"):
                    with self.subTest(missing=missing.name):
                        content = missing.read_bytes()
                        missing.unlink()
                        with self.assertRaises(FileNotFoundError):
                            build_package.copy_package_files()
                        self.assertTrue((staging / "C++ 测试程序.exe").is_file())
                        missing.write_bytes(content)

    def test_staging_cleanup_rejects_outside_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            outside = root / "other" / "dlcvpro_infer_cpp"
            outside.mkdir(parents=True)
            marker = outside / "keep.txt"
            marker.touch()
            with (patch.object(build_package, "REPO_ROOT", root),
                  patch.object(build_package, "STAGING_DIR", outside), self.assertRaises(RuntimeError)):
                build_package.rebuild_staging_directory()
            self.assertTrue(marker.is_file())

    def test_cpp_build_scope_and_install_use_generated_wheel(self):
        wheel = ROOT / "dist_cpp" / "generated.whl"
        original_shared_mode = os.environ.get("DlcvUseSharedQt")
        with (
            patch.object(build_package, "parse_args", return_value=argparse.Namespace(install=True)),
            patch.object(build_package, "require_file"),
            patch.object(build_package, "run_step") as run_step,
            patch.object(build_package, "copy_package_files", return_value=ROOT / "dlcvpro_infer_cpp" / "C++ 测试程序.exe"),
            patch.object(build_package, "find_signing_certificate_thumbprint", return_value="test"),
            patch.object(build_package, "snapshot_wheels", return_value={}),
            patch.object(build_package, "find_generated_wheel", return_value=wheel),
        ):
            self.assertEqual(build_package.main(), 0)
        commands = [call.args[1] for call in run_step.call_args_list]
        projects = [Path(arg) for command in commands for arg in command if arg.endswith((".csproj", ".vcxproj", ".sln"))]
        self.assertEqual(projects, [ROOT / "dlcv_infer_cpp_qt_demo" / "dlcv_infer_cpp_qt_demo.vcxproj"])
        self.assertFalse(any("sync_assembly_version.py" in arg for command in commands for arg in command))
        build_call = next(call for call in run_step.call_args_list if call.args[1][1:3] == ["-m", "build"])
        self.assertEqual(build_call.kwargs["env"]["DLCV_PACKAGE_VARIANT"], "cpp")
        self.assertIn("--no-isolation", build_call.args[1])
        self.assertEqual(run_step.call_args_list[0].kwargs["env"]["DlcvUseSharedQt"], "true")
        self.assertEqual(os.environ.get("DlcvUseSharedQt"), original_shared_mode)
        self.assertEqual(commands[-1][-1], str(wheel))

    def test_cpp_build_failure_returns_status_before_copying(self):
        with (
            patch.object(build_package, "parse_args", return_value=argparse.Namespace(install=False)),
            patch.object(build_package, "require_file"),
            patch.object(build_package, "run_step", side_effect=subprocess.CalledProcessError(17, "build")),
            patch.object(build_package, "copy_package_files") as copy_files,
        ):
            self.assertEqual(build_package.main(), 17)
        copy_files.assert_not_called()


class PackageMetadataTest(unittest.TestCase):
    def test_cpp_metadata_preserves_csharp_outputs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            package = root / "dlcvpro_infer_cpp"
            package.mkdir()
            (package / "C++ 测试程序.exe").touch()
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
