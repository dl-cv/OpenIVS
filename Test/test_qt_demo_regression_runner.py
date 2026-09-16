"""验证双 Qt Demo 回归 runner。"""
import argparse
import binascii
import io
import json
import os
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_qt_demo_regression as runner

CRASH_CODE = 0xC0000409


def _chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", binascii.crc32(kind + data) & 0xFFFFFFFF)


def _png(width=500, height=400):
    raw = b"".join(b"\x00" + b"\x00\x00\x00\xff" * width for _ in range(height))
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return runner.PNG_SIGNATURE + _chunk(b"IHDR", ihdr) + _chunk(b"IDAT", zlib.compress(raw)) + _chunk(b"IEND", b"")


class QtDemoRegressionRunnerTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="qt_runner_test_")
        self.root = Path(self.temp.name)
        self.c_exe = self._write("c/bin/c_demo.exe", b"c-exe")
        self.cpp_exe = self._write("cpp/bin/cpp_demo.exe", b"cpp-exe")
        self.c_mask_test_exe = self._write("mask/c/c_mask_test.exe", b"c-mask-test-exe")
        self.cpp_mask_test_exe = self._write("mask/cpp/cpp_mask_test.exe", b"cpp-mask-test-exe")
        self.dll = self._write("build/dlcv_infer_cpp.dll", b"wrapper")
        self._write("c/bin/dlcv_infer_cpp.dll", b"wrapper")
        self._write("cpp/bin/dlcv_infer_cpp.dll", b"wrapper")
        self.model_root = self.root / "models"
        self.core_dir = self.root / "sdk"
        self.core_dir.mkdir()
        config, errors = runner._load_config(runner.CASES_PATH)
        self.assertFalse(errors)
        self.config = config
        self.by_model = {case["model"]: case for case in config["inference_cases"]}
        for case in config["inference_cases"]:
            self._write(f"models/{case['model']}", b"model")
            self._write(f"models/{case['image']}", b"image")

    def tearDown(self):
        self.temp.cleanup()

    def _write(self, relative, data):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def _args(self, **changes):
        values = {
            "c_exe": self.c_exe,
            "cpp_exe": self.cpp_exe,
            "c_mask_test_exe": self.c_mask_test_exe,
            "cpp_mask_test_exe": self.cpp_mask_test_exe,
            "dll": self.dll,
            "model_root": self.model_root,
            "output": self.root / "report.json",
            "timeout": 17.0,
            "core_dll_directory": self.core_dir,
        }
        values.update(changes)
        return argparse.Namespace(**values)

    def _payload(self, demo, case):
        expected = case["expected"]
        summary = {
            "count": expected["count"],
            "categories": list(expected["categories"]),
            "scores": list(expected["scores"]),
            "below_threshold": [],
            "with_mean": [False] * expected["count"],
            "foreground_mean": [0.0] * expected["count"],
            "background_mean": [0.0] * expected["count"],
        }
        value = {
            "language": demo,
            "threshold": 0.5,
            "device": 0,
            "with_mask": case["with_mask"],
            "calc_mean": False,
            "structured": json.loads(json.dumps(summary, ensure_ascii=False)),
            "json": json.loads(json.dumps(summary, ensure_ascii=False)),
            "consistent": True,
            "threshold_check_passed": True,
            "mean_check_passed": True,
            "release_check_passed": True,
        }
        if demo == "c":
            value.update(inspection_supported=False, inspection_consistent=None)
        else:
            value["inspection_consistent"] = True
        return value
    def _process(self, mode=None):
        calls = []

        def execute(command, **kwargs):
            calls.append((list(command), kwargs))
            self.assertEqual(17.0, kwargs["timeout"])
            self.assertEqual(str(self.core_dir.resolve()), kwargs["cwd"])
            self.assertEqual(str(Path(kwargs["env"]["TEMP"])), kwargs["env"]["TMP"])
            exe = Path(command[0]).resolve()
            if exe in (self.c_mask_test_exe.resolve(), self.cpp_mask_test_exe.resolve()):
                self.assertEqual("offscreen", kwargs["env"]["QT_QPA_PLATFORM"])
                self.assertEqual(3, len(command))
                self.assertEqual("--output", command[1])
                output = Path(command[2])
                if mode == "mask_timeout":
                    raise runner.subprocess.TimeoutExpired(command, 17.0)
                if mode == "mask_launch_error":
                    raise OSError("测试进程无法启动")
                if mode != "mask_no_png":
                    output.write_bytes(b"invalid" if mode == "mask_invalid_png" else _png())
                code = 1 if mode == "mask_nonzero" else 0
                return runner.subprocess.CompletedProcess(command, code)
            self.assertIn(exe, (self.c_exe.resolve(), self.cpp_exe.resolve()))
            self.assertEqual(os.environ.get("QT_QPA_PLATFORM"), kwargs["env"].get("QT_QPA_PLATFORM"))
            demo = "c" if exe == self.c_exe.resolve() else "cpp"
            if command[1] == "--help":
                kwargs["stdout"].write("Usage: 测试\n".encode("utf-8"))
                return runner.subprocess.CompletedProcess(command, 0)
            if command[1] == "mask-visualization-selftest":
                if mode == "demo_accepts_mask":
                    return runner.subprocess.CompletedProcess(command, 0)
                if mode == "demo_writes_mask":
                    Path(command[command.index("--output") + 1]).write_bytes(_png())
                return runner.subprocess.CompletedProcess(command, 2)
            if len(command) == 2:
                return runner.subprocess.CompletedProcess(command, 2)
            model = Path(command[command.index("--model") + 1])
            image = Path(command[command.index("--image") + 1])
            threshold = command[command.index("--threshold") + 1]
            output = Path(command[command.index("--output") + 1])
            if threshold == "1.1" or not model.is_file() or not image.is_file() or model.suffix.lower() == ".dvsp" or output.resolve() in (model.resolve(), image.resolve()):
                return runner.subprocess.CompletedProcess(command, 2)
            case = self.by_model[model.name]
            payload = self._payload(demo, case)
            selected = demo == "c" and case["id"] == "classification_dvt"
            if selected and mode == "timeout":
                raise runner.subprocess.TimeoutExpired(command, 17.0)
            if selected and mode == "no_json":
                return runner.subprocess.CompletedProcess(command, 0)
            if selected and mode == "invalid_json":
                output.write_bytes(b"{")
                return runner.subprocess.CompletedProcess(command, 0)
            if selected and mode == "invalid_utf8":
                output.write_bytes(b"\xff")
                return runner.subprocess.CompletedProcess(command, 0)
            if selected and mode == "nonfinite":
                text = json.dumps(payload, ensure_ascii=False).replace("0.9951171875", "1e999", 1)
                output.write_text(text, encoding="utf-8")
                return runner.subprocess.CompletedProcess(command, 0)
            if selected and mode == "error_json":
                payload["error"] = "failed"
            if selected and mode == "wrong_baseline":
                payload["structured"]["scores"][0] -= 0.01
                payload["json"]["scores"][0] -= 0.01
            if selected and mode == "float_integer_fields":
                payload["device"] = 0.0
                payload["structured"]["count"] = 1.0
                payload["json"]["count"] = 1.0
            if selected and mode == "c_inspection_true":
                payload["inspection_consistent"] = True
            if demo == "cpp" and case["id"] == "classification_dvt" and mode == "cpp_inspection_false":
                payload["inspection_consistent"] = False
            if selected and mode == "release_false":
                payload["release_check_passed"] = False
            output.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
            return_code = CRASH_CODE - (1 << 32) if selected and mode == "crash" else 0
            return runner.subprocess.CompletedProcess(command, return_code)

        return calls, execute

    def _run(self, mode=None, **changes):
        calls, execute = self._process(mode)
        with patch.object(runner.subprocess, "run", side_effect=execute):
            report = runner.run_regression(self._args(**changes))
        return report, calls

    def test_parser_defaults(self):
        args = runner._parser().parse_args(["--c-exe", "c.exe", "--cpp-exe", "cpp.exe", "--c-mask-test-exe", "c_mask.exe", "--cpp-mask-test-exe", "cpp_mask.exe", "--dll", "x.dll", "--model-root", "models", "--output", str(self.root / "report.json")])
        self.assertEqual(120.0, args.timeout)
        self.assertIsNone(args.core_dll_directory)
        self.assertEqual(Path("c_mask.exe"), args.c_mask_test_exe)
        self.assertEqual(Path("cpp_mask.exe"), args.cpp_mask_test_exe)

    def test_success_runs_fixed_serial_scope(self):
        report, calls = self._run()
        self.assertTrue(report["passed"])
        self.assertEqual(28, report["summary"]["case_total"])
        self.assertEqual(4, report["summary"]["comparison_total"])
        self.assertEqual(28, len(calls))
        self.assertEqual(["classification_dvt", "classification_dvo", "segmentation_dvt", "flow_dvst"], [case["id"] for case in report["cases"][:4]])
        masks = [case for case in report["cases"] if case["kind"] == "png-selftest"]
        self.assertEqual({"c", "cpp"}, {case["demo"] for case in masks})
        self.assertTrue(all(case["artifact"]["bytes"] > 0 for case in masks))
        mask_commands = [command for command, _ in calls if command[1] == "--output"]
        self.assertEqual([self.c_mask_test_exe.resolve(), self.cpp_mask_test_exe.resolve()], [Path(command[0]) for command in mask_commands])
        for demo, exe in (("c", self.c_mask_test_exe), ("cpp", self.cpp_mask_test_exe)):
            self.assertEqual(runner._sha256(exe), report["executables"][f"{demo}_mask_test"]["sha256"])
        removed = [case for case in report["cases"] if case["id"] == "removed_mask_selftest"]
        self.assertEqual({"c", "cpp"}, {case["demo"] for case in removed})
        self.assertTrue(all(case["passed"] and case["process"]["exit_code"] == 2 for case in removed))

    def test_parser_requires_both_mask_executables(self):
        base = ["--c-exe", "c.exe", "--cpp-exe", "cpp.exe", "--dll", "x.dll", "--model-root", "models", "--output", str(self.root / "report.json")]
        for missing in ("--c-mask-test-exe", "--cpp-mask-test-exe"):
            supplied = "--cpp-mask-test-exe" if missing == "--c-mask-test-exe" else "--c-mask-test-exe"
            with self.subTest(missing=missing), patch("sys.stderr", new_callable=io.StringIO) as stderr:
                with self.assertRaises(SystemExit) as error:
                    runner._parser().parse_args(base + [supplied, "mask.exe"])
                self.assertEqual(2, error.exception.code)
                self.assertIn(missing, stderr.getvalue())

    def test_mask_executable_missing_or_not_exe_stops_launch(self):
        for name in ("c_mask_test_exe", "cpp_mask_test_exe"):
            for invalid in (self.root / "missing.exe", self.root / "directory.exe", self._write("mask/not_exe.txt", b"test")):
                if invalid.name == "directory.exe":
                    invalid.mkdir(exist_ok=True)
                with self.subTest(name=name, path=invalid), patch.object(runner.subprocess, "run") as process:
                    report = runner.run_regression(self._args(**{name: invalid}))
                process.assert_not_called()
                self.assertFalse(report["passed"])
                self.assertTrue(report["errors"])

    def test_executable_paths_must_all_differ(self):
        names = ("c_exe", "cpp_exe", "c_mask_test_exe", "cpp_mask_test_exe")
        for index, name in enumerate(names):
            for other in names[index + 1:]:
                path = getattr(self, name)
                alias = path.parent / ".." / path.parent.name / path.name
                with self.subTest(name=name, other=other), patch.object(runner.subprocess, "run") as process:
                    report = runner.run_regression(self._args(**{other: alias}))
                process.assert_not_called()
                self.assertFalse(report["passed"])
                self.assertIn("必须指向不同文件", " ".join(report["errors"]))

    def test_mask_offscreen_overrides_parent_without_affecting_demo(self):
        for platform in (None, "windows"):
            with self.subTest(platform=platform), patch.dict(os.environ):
                if platform is None:
                    os.environ.pop("QT_QPA_PLATFORM", None)
                else:
                    os.environ["QT_QPA_PLATFORM"] = platform
                report, _ = self._run()
                self.assertTrue(report["passed"])
                self.assertEqual(platform, os.environ.get("QT_QPA_PLATFORM"))

    def test_mask_uses_own_directory_without_core_directory(self):
        calls, execute = self._process()

        def execute_from_exe_directory(command, **kwargs):
            self.assertEqual(str(Path(command[0]).parent), kwargs["cwd"])
            return execute(command, **{**kwargs, "cwd": str(self.core_dir.resolve())})

        with patch.object(runner.subprocess, "run", side_effect=execute_from_exe_directory):
            report = runner.run_regression(self._args(core_dll_directory=None))
        self.assertTrue(report["passed"])
        self.assertEqual(28, len(calls))

    def test_mask_process_and_png_failures(self):
        for mode, message in (
            ("mask_nonzero", "退出码与预期不符"),
            ("mask_no_png", "PNG 未生成"),
            ("mask_invalid_png", "有效非空 PNG"),
            ("mask_timeout", "执行超时"),
            ("mask_launch_error", "进程启动失败"),
        ):
            with self.subTest(mode=mode):
                report, _ = self._run(mode)
                self.assertFalse(report["passed"])
                masks = [case for case in report["cases"] if case["kind"] == "png-selftest"]
                self.assertEqual(2, len(masks))
                for case in masks:
                    self.assertFalse(case["passed"])
                    self.assertIn(message, " ".join(case["errors"]))
                self.assertTrue(all(case["passed"] for case in report["cases"] if case["kind"] != "png-selftest"))

    def test_demo_must_reject_removed_mask_command_without_png(self):
        for mode, message in (("demo_accepts_mask", "退出码与预期不符"), ("demo_writes_mask", "仍生成了输出文件")):
            with self.subTest(mode=mode):
                report, _ = self._run(mode)
                self.assertFalse(report["passed"])
                cases = [case for case in report["cases"] if case["id"] == "removed_mask_selftest"]
                self.assertEqual(2, len(cases))
                for case in cases:
                    self.assertFalse(case["passed"])
                    self.assertIn(message, " ".join(case["errors"]))

    def test_missing_fixture_fails_but_other_cases_continue(self):
        (self.model_root / "猫狗-分类_s.dvo").unlink()
        report, calls = self._run()
        self.assertFalse(report["passed"])
        missing = [case for case in report["cases"] if case["id"] == "classification_dvo"]
        self.assertEqual(2, len(missing))
        self.assertTrue(all(not case["process"]["launched"] for case in missing))
        self.assertGreater(len(calls), 20)
        self.assertGreater(report["summary"]["case_passed"], 0)

    def test_wrapper_hash_mismatch_stops_launch(self):
        (self.c_exe.parent / runner.WRAPPER_DLL_NAME).write_bytes(b"old")
        with patch.object(runner.subprocess, "run") as process:
            report = runner.run_regression(self._args())
        process.assert_not_called()
        self.assertFalse(report["passed"])
        self.assertIn("SHA256 不一致", " ".join(report["errors"]))
    def test_process_and_json_failures(self):
        cases = (
            ("crash", "退出码与预期不符"),
            ("timeout", "执行超时"),
            ("no_json", "未生成"),
            ("invalid_json", "格式无效"),
            ("invalid_utf8", "有效 UTF-8"),
            ("nonfinite", "非有限数值"),
            ("error_json", "包含 error 字段"),
            ("wrong_baseline", "与基准不符"),
        )
        for mode, message in cases:
            with self.subTest(mode=mode):
                report, _ = self._run(mode)
                self.assertFalse(report["passed"])
                text = " ".join(error for case in report["cases"] for error in case["errors"])
                self.assertIn(message, text)
        report, _ = self._run("crash")
        failed = next(case for case in report["cases"] if case["demo"] == "c" and case["id"] == "classification_dvt")
        self.assertEqual(CRASH_CODE, failed["process"]["exit_code"])

    def test_count_and_device_require_json_integers(self):
        report, _ = self._run("float_integer_fields")
        self.assertFalse(report["passed"])
        text = " ".join(error for case in report["cases"] for error in case["errors"])
        self.assertIn("structured.count", text)
        self.assertIn("json.count", text)
        self.assertIn("device 与调用参数不符", text)
    def test_language_specific_inspection_and_release_fields(self):
        for mode, message in (
            ("c_inspection_true", "C Demo inspection_consistent"),
            ("cpp_inspection_false", "C++ Demo inspection_consistent"),
            ("release_false", "release_check_passed"),
        ):
            with self.subTest(mode=mode):
                report, _ = self._run(mode)
                self.assertFalse(report["passed"])
                text = " ".join(error for case in report["cases"] for error in case["errors"])
                self.assertIn(message, text)

    def test_png_validation_rejects_wrong_size(self):
        path = self._write("wrong.png", _png(499, 400))
        self.assertIn("尺寸错误", " ".join(runner._png_errors(path)))

    def test_main_writes_strict_utf8_report_in_temp(self):
        output = self.root / "输出" / "回归报告.json"
        argv = [
            "--c-exe", str(self.c_exe), "--cpp-exe", str(self.cpp_exe),
            "--c-mask-test-exe", str(self.c_mask_test_exe),
            "--cpp-mask-test-exe", str(self.cpp_mask_test_exe),
            "--dll", str(self.dll), "--model-root", str(self.model_root),
            "--output", str(output), "--timeout", "17",
            "--core-dll-directory", str(self.core_dir),
        ]
        _, execute = self._process()
        with patch.object(runner.subprocess, "run", side_effect=execute):
            code = runner.main(argv)
        self.assertEqual(0, code)
        data = output.read_bytes()
        self.assertFalse(data.startswith(b"\xef\xbb\xbf"))
        value = json.loads(data.decode("utf-8"), parse_constant=lambda item: self.fail(item))
        self.assertTrue(value["passed"])
        self.assertIn("猫狗-分类_s.dvo", value["fixtures"])

    def test_main_rejects_report_outside_system_temp(self):
        outside = Path(__file__).resolve().with_name("forbidden-report.json")
        try:
            code = runner.main([
                "--c-exe", str(self.c_exe), "--cpp-exe", str(self.cpp_exe),
                "--c-mask-test-exe", str(self.c_mask_test_exe),
                "--cpp-mask-test-exe", str(self.cpp_mask_test_exe),
                "--dll", str(self.dll), "--model-root", str(self.model_root),
                "--output", str(outside),
            ])
            self.assertEqual(2, code)
            self.assertFalse(outside.exists())
        finally:
            if outside.exists():
                outside.unlink()


if __name__ == "__main__":
    unittest.main()