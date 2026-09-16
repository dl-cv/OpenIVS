"""独立打包与安装的纯 Python 隔离测试，不执行构建或真实客户端。"""

import contextlib
import importlib.util
import io
import json
import os
import stat
import struct
import subprocess
import sys
import tempfile
import unittest
import warnings
import zipfile
import zlib
from pathlib import Path
from unittest.mock import patch


MODULE_PATH = Path(__file__).resolve().parents[1] / "SentinelManager" / "build_package.py"
SPEC = importlib.util.spec_from_file_location("sentinel_package", MODULE_PATH)
package = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = package
SPEC.loader.exec_module(package)

VERSION_TEXT = '''using System.Reflection;
[assembly: AssemblyVersion("2026.9.16.0")]
[assembly: AssemblyFileVersion("2026.9.16.0")]
[assembly: AssemblyInformationalVersion("2026.9.16.0a0")]
'''
VERSION = package.Version("2026.9.16.0", "2026.9.16.0a0")


def png_bytes(shade=0):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes([0, shade, shade, shade])))
            + chunk(b"IEND", b""))


def write_report(output):
    output.mkdir(parents=True, exist_ok=True)
    report = {
        "passed": True,
        "timestamp": "2026-09-16T12:00:00.1234567+08:00",
        "data_source": "固定测试数据，未连接真实服务",
        "checks": [{"name": "隔离检查", "passed": True}],
        "screenshots": list(package.SCREENSHOTS),
    }
    (output / "ui-test-results.json").write_text(json.dumps(report, ensure_ascii=False), encoding="utf-8")
    for index, name in enumerate(package.SCREENSHOTS):
        (output / name).write_bytes(png_bytes(index))
    return report


class IsolatedTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="sentinel-package-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.component = self.root / "SentinelManager"
        self.component.mkdir()
        (self.component / "Properties").mkdir()
        self.version_file = self.component / "Properties" / "AssemblyInfo.cs"
        self.version_file.write_text(VERSION_TEXT, encoding="utf-8")
        self.output = self.component / "bin" / "Release"
        self.output.mkdir(parents=True)
        (self.output / package.EXE_NAME).write_bytes(b"isolated executable")
        (self.component / "README.md").write_text("隔离测试文档", encoding="utf-8")
        self.dist = self.component / "dist"
        self.dist.mkdir()
        self.archive = self.dist / VERSION.archive_name
        self.local = self.root / "local"
        self.target = self.local / "Programs" / "OpenIVS" / "SentinelManager"
        self.env = patch.dict(os.environ, {"LOCALAPPDATA": str(self.local)})
        self.env.start()
        self.addCleanup(self.env.stop)

    def write_archive(self, extra=None, entries=None):
        if entries is None:
            entries = [(package.EXE_NAME, b"new executable"), ("README.md", b"new readme")]
            if extra:
                entries.extend(extra)
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(self.archive, "w", compression=zipfile.ZIP_DEFLATED) as archive:
                for name, content in entries:
                    archive.writestr(name, content)
        return self.archive


class VersionTest(IsolatedTest):
    def test_information_version_is_filename_source(self):
        self.assertEqual(VERSION, package.read_version(self.version_file))
        self.assertEqual("SentinelManager-2026.9.16.0a0-win-x64.zip", VERSION.archive_name)

    def test_release_and_comments(self):
        self.version_file.write_text(
            '// [assembly: AssemblyVersion("2000.1.1.0")]\n'
            + VERSION_TEXT.replace("2026.9.16.0a0", "2026.9.16.0")
            + '\n/* [assembly: AssemblyFileVersion("2000.1.1.0")] */', encoding="utf-8-sig")
        self.assertEqual("2026.9.16.0", package.read_version(self.version_file).informational)

    def test_rejects_invalid_versions(self):
        invalid = [
            VERSION_TEXT.replace("2026.9.16.0", "2026.2.30.0"),
            VERSION_TEXT.replace("2026.9.16.0", "2026.09.16.0"),
            VERSION_TEXT.replace("2026.9.16.0", "2026.9.16.65535"),
            VERSION_TEXT.replace('AssemblyVersion("2026.9.16.0")', 'AssemblyVersion("2026.9.16.1")'),
            VERSION_TEXT.replace("2026.9.16.0a0", "2026.9.16.1a0"),
            VERSION_TEXT.replace("2026.9.16.0a0", "2026.9.16.0a01"),
            VERSION_TEXT.replace("2026.9.16.0a0", "../../outside"),
            VERSION_TEXT.replace('AssemblyVersion("2026.9.16.0")', 'AssemblyVersion("2026.9.*")'),
            VERSION_TEXT + '\n[assembly: AssemblyFileVersion("2026.9.16.0")]',
            VERSION_TEXT.replace('[assembly: AssemblyVersion("2026.9.16.0")]', ""),
            VERSION_TEXT.replace('AssemblyVersion("2026.9.16.0")', 'AssemblyVersion(SOME_VALUE)'),
        ]
        for text in invalid:
            with self.subTest(text=text):
                self.version_file.write_text(text, encoding="utf-8")
                with self.assertRaises(package.PackageError):
                    package.read_version(self.version_file)

    def test_powershell_reads_literal_path_and_both_version_fields(self):
        exe = self.output / package.EXE_NAME
        result = subprocess.CompletedProcess([], 0, json.dumps({
            "FileVersion": VERSION.numeric, "ProductVersion": VERSION.informational,
        }), "")
        with patch.object(package.subprocess, "run", return_value=result) as run:
            self.assertEqual(VERSION, package.read_exe_version(exe))
        args, kwargs = run.call_args
        self.assertIn("-NonInteractive", args[0])
        self.assertIn("-LiteralPath $env:SENTINEL_PACKAGE_EXE", args[0][-1])
        self.assertEqual(str(exe.absolute()), kwargs["env"]["SENTINEL_PACKAGE_EXE"])
        self.assertNotIn(str(exe), args[0][-1])
        self.assertTrue(kwargs["check"])

    def test_version_mismatch_is_rejected(self):
        with patch.object(package, "read_exe_version", return_value=package.Version(VERSION.numeric, "old")):
            with self.assertRaises(package.PackageError):
                package.verify_exe_version(self.output / package.EXE_NAME, VERSION)


class ArchiveTest(IsolatedTest):
    def test_only_declared_files_are_packaged(self):
        for name in ("SentinelManagerTest.exe", "private.dll", "debug.pdb", "ui-test-results.json"):
            (self.output / name).write_bytes(b"not packaged")
        (self.output / package.CONFIG_NAME).write_bytes(b"<configuration />")
        with patch.object(package, "verify_exe_version"):
            archive = package.create_archive(self.component, VERSION)
        self.assertEqual(self.archive, archive)
        contents = package.validate_archive(archive)
        self.assertEqual(package.ALLOWED_FILES, set(contents))
        self.assertEqual(b"isolated executable", contents[package.EXE_NAME])

    def test_config_is_optional(self):
        self.assertEqual(package.REQUIRED_FILES, set(package.package_files(self.component)))
        self.assertEqual(package.REQUIRED_FILES, set(package.validate_archive(self.write_archive())))

    def test_required_source_is_checked(self):
        (self.component / "README.md").unlink()
        with self.assertRaises(package.PackageError):
            package.package_files(self.component)

    def test_rejects_non_whitelist_and_abnormal_paths(self):
        for name in ("../outside", "folder/README.md", "/README.md", "C:/README.md", "C:\\README.md",
                     "..\\README.md", "README.md:stream", "README.md ", "readme.md", "folder/", "extra.dll"):
            with self.subTest(name=name):
                self.write_archive([(name, b"unexpected")])
                with self.assertRaises(package.PackageError):
                    package.validate_archive(self.archive)

    def test_rejects_duplicate_missing_and_empty_files(self):
        variants = [
            [(package.EXE_NAME, b"exe"), ("README.md", b"readme"), ("README.md", b"repeat")],
            [(package.EXE_NAME, b"exe")],
            [(package.EXE_NAME, b""), ("README.md", b"readme")],
        ]
        for entries in variants:
            with self.subTest(entries=entries):
                with self.assertRaises(package.PackageError):
                    package.validate_archive(self.write_archive(entries=entries))

    def test_rejects_links_and_directory_attributes(self):
        for mode, attributes in ((stat.S_IFLNK | 0o777, 0), (stat.S_IFDIR | 0o755, 0),
                                 (stat.S_IFREG | 0o644, 0x400), (stat.S_IFREG | 0o644, 0x10)):
            member = zipfile.ZipInfo(package.CONFIG_NAME)
            member.create_system = 3
            member.external_attr = (mode << 16) | attributes
            with self.subTest(mode=mode, attributes=attributes):
                with self.assertRaises(package.PackageError):
                    package.validate_archive(self.write_archive([(member, b"target")]))

    def test_rejects_corrupt_zip_and_crc(self):
        self.archive.write_bytes(b"not zip")
        with self.assertRaises(package.PackageError):
            package.validate_archive(self.archive)
        self.write_archive()
        with zipfile.ZipFile(self.archive, "r") as archive:
            member = archive.infolist()[0]
            offset = member.header_offset + 30 + len(member.filename.encode()) + len(member.extra)
        raw = bytearray(self.archive.read_bytes())
        raw[offset] ^= 0xFF
        self.archive.write_bytes(raw)
        with self.assertRaises(package.PackageError):
            package.validate_archive(self.archive)

    def test_rejects_oversized_archive_and_member(self):
        self.write_archive()
        with patch.object(package, "MAX_ARCHIVE_BYTES", 1):
            with self.assertRaises(package.PackageError):
                package.validate_archive(self.archive)
        with patch.object(package, "MAX_FILE_BYTES", 1):
            with self.assertRaises(package.PackageError):
                package.validate_archive(self.archive)

    def test_rejects_encrypted_flag(self):
        self.write_archive()
        raw = bytearray(self.archive.read_bytes())
        central = raw.index(b"PK\x01\x02")
        raw[central + 8] |= 1
        self.archive.write_bytes(raw)
        with self.assertRaises(package.PackageError):
            package.validate_archive(self.archive)

    def test_rejects_unsupported_compression(self):
        with zipfile.ZipFile(self.archive, "w", compression=zipfile.ZIP_BZIP2) as archive:
            archive.writestr(package.EXE_NAME, b"exe")
            archive.writestr("README.md", b"readme")
        with self.assertRaises(package.PackageError):
            package.validate_archive(self.archive)


class UiResultTest(IsolatedTest):
    def setUp(self):
        super().setUp()
        self.ui = self.root / "ui"
        self.report = write_report(self.ui)

    def test_valid_report(self):
        package.validate_ui_results(self.ui)

    def test_rejects_failed_or_incomplete_report(self):
        for field, value in (
            ("passed", False), ("passed", 1), ("checks", []),
            ("checks", [{"name": "检查", "passed": False}]),
            ("checks", [{"name": "检查", "passed": "true"}]),
            ("checks", [{"name": "", "passed": True}]),
            ("screenshots", ["../initial.png"]), ("screenshots", [None] * 4),
            ("timestamp", "not a date"), ("timestamp", "2026-09-16T12:00:00"),
            ("data_source", "unknown"),
        ):
            with self.subTest(field=field, value=value):
                report = dict(self.report, **{field: value})
                (self.ui / "ui-test-results.json").write_text(json.dumps(report), encoding="utf-8")
                with self.assertRaises(package.PackageError):
                    package.validate_ui_results(self.ui)

    def test_rejects_invalid_json_encoding_and_duplicate_fields(self):
        for content in (b"\xff", b"{broken", b'{"passed":true,"passed":true}', b'{"passed":NaN}'):
            with self.subTest(content=content):
                (self.ui / "ui-test-results.json").write_bytes(content)
                with self.assertRaises(package.PackageError):
                    package.validate_ui_results(self.ui)

    def test_rejects_missing_or_invalid_screenshot(self):
        for content in (None, b"not png"):
            write_report(self.ui)
            image = self.ui / "compact.png"
            if content is None:
                image.unlink()
            else:
                image.write_bytes(content)
            with self.assertRaises(package.PackageError):
                package.validate_ui_results(self.ui)

    def test_rejects_unchanged_result_image(self):
        (self.ui / "result.png").write_bytes((self.ui / "initial.png").read_bytes())
        with self.assertRaises(package.PackageError):
            package.validate_ui_results(self.ui)


class InstallTest(IsolatedTest):
    def test_uses_exact_version_preserves_other_files_and_prints_versions(self):
        self.write_archive()
        (self.dist / "SentinelManager-9999.1.1.0-win-x64.zip").write_bytes(b"not selected")
        self.target.mkdir(parents=True)
        other = self.target / "other-source.txt"
        other.write_bytes(b"keep")
        output = io.StringIO()
        with patch.object(package, "read_exe_version", return_value=VERSION) as read, contextlib.redirect_stdout(output):
            self.assertEqual(self.target, package.install_package(self.component))
        self.assertEqual(b"keep", other.read_bytes())
        self.assertEqual(b"new executable", (self.target / package.EXE_NAME).read_bytes())
        self.assertEqual(2, read.call_count)
        self.assertEqual(self.target / package.EXE_NAME, read.call_args.args[0])
        self.assertFalse(read.call_args_list[0].args[0].exists())
        self.assertIn(VERSION.numeric, output.getvalue())
        self.assertIn(VERSION.informational, output.getvalue())

    def test_missing_matching_version_does_not_select_another_archive(self):
        self.write_archive().rename(self.dist / "SentinelManager-2026.9.16.1-win-x64.zip")
        with patch.object(package, "read_exe_version") as read:
            with self.assertRaises(package.PackageError):
                package.install_package(self.component)
        read.assert_not_called()
        self.assertFalse(self.target.exists())

    def test_rejects_bad_archive_before_installation(self):
        self.write_archive([("../outside", b"bad")])
        with self.assertRaises(package.PackageError):
            package.install_package(self.component)
        self.assertFalse(self.target.exists())

    def test_wrong_staged_version_does_not_write(self):
        self.write_archive()
        with patch.object(package, "read_exe_version", return_value=package.Version("wrong", "wrong")):
            with self.assertRaises(package.PackageError):
                package.install_package(self.component)
        self.assertFalse(self.target.exists())

    def test_failed_installed_version_restores_originals_and_removes_only_new_files(self):
        self.write_archive([(package.CONFIG_NAME, b"new config")])
        self.target.mkdir(parents=True)
        (self.target / package.EXE_NAME).write_bytes(b"old exe")
        (self.target / "other.txt").write_bytes(b"keep")
        output = io.StringIO()
        with patch.object(package, "read_exe_version", side_effect=[VERSION, package.Version("old", "old")]), contextlib.redirect_stdout(output):
            with self.assertRaises(package.PackageError):
                package.install_package(self.component)
        self.assertEqual({package.EXE_NAME, "other.txt"}, {p.name for p in self.target.iterdir()})
        self.assertEqual(b"old exe", (self.target / package.EXE_NAME).read_bytes())
        self.assertEqual(b"keep", (self.target / "other.txt").read_bytes())
        self.assertNotIn("安装成功", output.getvalue())

    def test_copy_failure_restores_previous_files(self):
        self.write_archive()
        self.target.mkdir(parents=True)
        for name in package.REQUIRED_FILES:
            (self.target / name).write_bytes(b"old")
        copy = package.shutil.copyfile

        def fail_once(source, target):
            if Path(source).parent.name == "package" and Path(target).name == package.EXE_NAME:
                Path(target).write_bytes(b"partial")
                raise OSError("隔离写入失败")
            return copy(source, target)

        with patch.object(package, "read_exe_version", return_value=VERSION), patch.object(package.shutil, "copyfile", side_effect=fail_once):
            with self.assertRaises(OSError):
                package.install_package(self.component)
        for name in package.REQUIRED_FILES:
            self.assertEqual(b"old", (self.target / name).read_bytes())

    def test_file_mismatch_restores_originals(self):
        self.write_archive()
        self.target.mkdir(parents=True)
        for name in package.REQUIRED_FILES:
            (self.target / name).write_bytes(b"old")
        copy = package.shutil.copyfile

        def corrupt(source, target):
            result = copy(source, target)
            if Path(source).parent.name == "package":
                Path(target).write_bytes(b"corrupt")
            return result

        with patch.object(package, "read_exe_version", return_value=VERSION), patch.object(package.shutil, "copyfile", side_effect=corrupt):
            with self.assertRaisesRegex(package.PackageError, "安装文件比对失败"):
                package.install_package(self.component)
        for name in package.REQUIRED_FILES:
            self.assertEqual(b"old", (self.target / name).read_bytes())

    def test_restoration_failure_is_reported_as_failure(self):
        self.write_archive()
        self.target.mkdir(parents=True)
        (self.target / package.EXE_NAME).write_bytes(b"old")
        copy = package.shutil.copyfile

        def fail_restore(source, target):
            if Path(source).parent.name == "backup":
                raise OSError("隔离恢复失败")
            return copy(source, target)

        output = io.StringIO()
        with patch.object(package, "read_exe_version", side_effect=[VERSION, package.Version("wrong", "wrong")]), patch.object(package.shutil, "copyfile", side_effect=fail_restore), contextlib.redirect_stdout(output):
            with self.assertRaisesRegex(package.PackageError, "部分文件恢复失败"):
                package.install_package(self.component)
        self.assertNotIn("安装成功", output.getvalue())

    def test_preserves_config_missing_from_package(self):
        self.write_archive()
        self.target.mkdir(parents=True)
        config = self.target / package.CONFIG_NAME
        config.write_bytes(b"existing config")
        with self.assertRaises(package.PackageError):
            package.install_package(self.component)
        self.assertEqual(b"existing config", config.read_bytes())

    def test_rejects_hardlink_target(self):
        self.write_archive()
        self.target.mkdir(parents=True)
        other = self.root / "other-exe"
        other.write_bytes(b"keep")
        os.link(other, self.target / package.EXE_NAME)
        with self.assertRaises(package.PackageError):
            package.install_package(self.component)
        self.assertEqual(b"keep", other.read_bytes())

    def test_rejects_reparse_point(self):
        attributes = type("Attributes", (), {"st_mode": stat.S_IFDIR, "st_file_attributes": 0x400})()
        with patch.object(Path, "lstat", return_value=attributes):
            with self.assertRaises(package.PackageError):
                package.check_plain_path(self.target)

    def test_requires_absolute_localappdata(self):
        for value in ("", "relative-path"):
            with patch.dict(os.environ, {"LOCALAPPDATA": value}):
                with self.assertRaises(package.PackageError):
                    package.default_install_dir()


class BuildFlowTest(IsolatedTest):
    def setUp(self):
        super().setUp()
        self.builder = self.root / ".cursor" / "skills" / "vs-build" / "scripts" / "build.py"
        self.builder.parent.mkdir(parents=True)
        self.builder.write_text("# 隔离测试文件", encoding="utf-8")
        self.project = self.root / "Test" / "SentinelManagerTest" / "SentinelManagerTest.csproj"
        self.project.parent.mkdir(parents=True)
        self.project.write_text("<Project />", encoding="utf-8")
        self.test_exe = self.project.parent / "bin" / "Release" / "SentinelManagerTest.exe"
        self.test_exe.parent.mkdir(parents=True)
        self.test_exe.write_bytes(b"isolated test exe")

    def test_builds_only_test_project_then_runs_backend_and_ui_serially(self):
        calls = []

        def execute(title, command, cwd, timeout):
            calls.append((command, cwd))
            if "ui-test" in command:
                write_report(Path(command[-1]))

        with patch.object(package, "run_step", side_effect=execute), patch.object(package, "verify_exe_version"):
            self.assertEqual(self.archive, package.build_package(self.component))
        self.assertEqual(3, len(calls))
        self.assertEqual([
            sys.executable, "-B", str(self.builder), str(self.project), "--configuration", "Release",
            "--platform", "x64", "--target", "Build", "--verbosity", "minimal",
        ], calls[0][0])
        self.assertEqual([str(self.test_exe)], calls[1][0])
        self.assertEqual([str(self.test_exe), "ui-test", "--output-dir"], calls[2][0][:-1])
        self.assertEqual(calls[1][1], calls[2][1])
        self.assertFalse(calls[1][1].exists())
        self.assertFalse(Path(calls[2][0][-1]).exists())

    def test_build_or_backend_failure_stops_packaging(self):
        for failed_step in (0, 1, 2):
            errors = [None] * failed_step + [subprocess.CalledProcessError(1, "isolated")]
            with self.subTest(failed_step=failed_step):
                with patch.object(package, "run_step", side_effect=errors) as run, patch.object(package, "verify_exe_version"), patch.object(package, "create_archive") as create:
                    with self.assertRaises(subprocess.CalledProcessError):
                        package.build_package(self.component)
                create.assert_not_called()
                self.assertEqual(failed_step + 1, run.call_count)
                if failed_step > 0:
                    self.assertFalse(run.call_args.args[2].exists())

    def test_missing_ui_report_stops_packaging(self):
        with patch.object(package, "run_step"), patch.object(package, "verify_exe_version"), patch.object(package, "create_archive") as create:
            with self.assertRaises(package.PackageError):
                package.build_package(self.component)
        create.assert_not_called()

    def test_version_change_during_validation_stops_packaging(self):
        def execute(title, command, cwd, timeout):
            if "ui-test" in command:
                write_report(Path(command[-1]))
                self.version_file.write_text(VERSION_TEXT.replace("2026.9.16.0", "2026.9.16.1"), encoding="utf-8")
        with patch.object(package, "run_step", side_effect=execute), patch.object(package, "verify_exe_version"), patch.object(package, "create_archive") as create:
            with self.assertRaises(package.PackageError):
                package.build_package(self.component)
        create.assert_not_called()

    def test_ui_timeout_cleans_temporary_output(self):
        output = []

        def execute(title, command, cwd, timeout):
            if "ui-test" in command:
                output.append(Path(command[-1]))
                write_report(output[0])
                raise subprocess.TimeoutExpired(command, timeout)

        with patch.object(package, "run_step", side_effect=execute), patch.object(package, "verify_exe_version"), patch.object(package, "create_archive") as create:
            with self.assertRaises(subprocess.TimeoutExpired):
                package.build_package(self.component)
        create.assert_not_called()
        self.assertFalse(output[0].exists())

    def test_run_step_checks_exit_code_and_timeout(self):
        with patch.object(package.subprocess, "run") as run:
            package.run_step("隔离执行检查", ["test"], self.root, 15)
        run.assert_called_once_with(["test"], cwd=self.root, check=True, timeout=15)

    def test_main_failure_returns_nonzero_without_success(self):
        output, error = io.StringIO(), io.StringIO()
        with patch.object(package, "build_package", side_effect=package.PackageError("隔离失败")), contextlib.redirect_stdout(output), contextlib.redirect_stderr(error):
            self.assertEqual(1, package.main(["build"]))
        self.assertNotIn("成功", output.getvalue())
        self.assertIn("操作失败", error.getvalue())


if __name__ == "__main__":
    unittest.main()
