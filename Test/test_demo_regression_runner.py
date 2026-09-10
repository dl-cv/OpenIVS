"""验证 Demo 回归执行器的配置、进程退出和输出检查。"""

import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))

import run_demo_regression as runner


CRASH_CODE = 0xC0000409
SIGNED_CRASH_CODE = CRASH_CODE - (1 << 32)
CATEGORY = "示例类别"


class DemoRegressionRunnerTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        self.exe = self._write("bin/demo.exe", b"exe")
        self.dll = self._write("bin/runtime.dll", b"dll")
        self.model = self._write("inputs/model.dvst", b"model")
        self.image = self._write("inputs/image.png", b"image")

    def tearDown(self):
        self.temp.cleanup()

    def _write(self, relative, data):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    @staticmethod
    def _baseline(count=1, categories=None, count_text=None):
        value = {"count": count, "categories": categories or [CATEGORY]}
        if count_text is not None:
            value["count_text"] = count_text
        return value

    @classmethod
    def _json_expect(cls, expected=None):
        value = expected or {
            "success": True,
            "structured": {"count": 1, "categories": [CATEGORY]},
        }
        return {"json": value, "result_baseline": cls._baseline()}

    @staticmethod
    def _successful_json(extra=None):
        value = {
            "success": True,
            "structured": {"count": 1, "categories": [CATEGORY]},
        }
        if extra:
            value.update(extra)
        return value

    def _config(
        self,
        *,
        kind="cpp_qt",
        command="infer",
        options=None,
        expect=None,
        extra_case=None,
        stdout_encoding=None,
    ):
        if options is None:
            options = {"threshold": 0.5}
        if expect is None:
            expect = self._json_expect()
        case = {
            "name": "regression",
            "program": "demo",
            "command": command,
            "model": "model",
            "image": "image",
            "timeout_seconds": 3,
            "options": options,
            "expect": expect,
        }
        if extra_case:
            case.update(extra_case)
        program = {
            "kind": kind,
            "executable": str(self.exe),
            "dlls": [{"name": "runtime", "path": str(self.dll)}],
        }
        if kind == "c_console":
            program["stdout_encoding"] = stdout_encoding or "utf-8"
        data = {
            "configuration": "Release",
            "programs": {"demo": program},
            "models": {"model": {"path": str(self.model)}},
            "images": {"image": {"path": str(self.image)}},
            "cases": [case],
        }
        path = self.root / "config.json"
        path.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
        return path

    @staticmethod
    def _output_from_arguments(arguments):
        index = list(arguments).index("--output")
        return Path(arguments[index + 1])

    @staticmethod
    def _write_streams(stdout_path, stderr_path, stdout=b"", stderr=b""):
        if isinstance(stdout, str):
            stdout = stdout.encode("utf-8")
        if isinstance(stderr, str):
            stderr = stderr.encode("utf-8")
        stdout_path.write_bytes(stdout)
        stderr_path.write_bytes(stderr)

    def _write_success_json(self, arguments, extra=None):
        self._output_from_arguments(arguments).write_text(
            json.dumps(self._successful_json(extra), ensure_ascii=False), encoding="utf-8")

    def test_windows_crash_code_fails_even_with_complete_consistent_json(self):
        config = self._config(expect=self._json_expect({
            "success": True,
            "consistent": True,
            "structured": {"count": 1, "categories": [CATEGORY]},
        }))

        def crash_process(arguments, stdout_path, stderr_path, timeout_seconds):
            self._write_streams(stdout_path, stderr_path)
            self._write_success_json(arguments, {"consistent": True})
            return runner.ProcessResult(SIGNED_CRASH_CODE, 17, False)

        _, report = runner.run_config(config, temp_base=self.root, process_runner=crash_process)
        case = report["cases"][0]
        self.assertFalse(case["passed"])
        self.assertEqual(CRASH_CODE, case["exit_code"])
        self.assertEqual("0xC0000409", case["exit_code_hex"])
        self.assertIn("0xC0000409", case["errors"][0])

    def test_nonzero_exit_fails(self):
        config = self._config()

        def failing_process(arguments, stdout_path, stderr_path, timeout_seconds):
            self._write_streams(stdout_path, stderr_path)
            self._write_success_json(arguments)
            return runner.ProcessResult(7, 2, False)

        _, report = runner.run_config(config, temp_base=self.root, process_runner=failing_process)
        self.assertFalse(report["cases"][0]["passed"])
        self.assertEqual(7, report["cases"][0]["exit_code"])

    def test_execute_process_times_out(self):
        stdout_path = self.root / "stdout.txt"
        stderr_path = self.root / "stderr.txt"
        result = runner._execute_process(
            [sys.executable, "-c", "import time; time.sleep(2)"],
            stdout_path,
            stderr_path,
            0.05,
        )
        self.assertTrue(result.timed_out)
        self.assertIsNone(result.exit_code)

    def test_exit_zero_without_json_fails(self):
        config = self._config()

        def no_json(arguments, stdout_path, stderr_path, timeout_seconds):
            self._write_streams(stdout_path, stderr_path)
            return runner.ProcessResult(0, 1, False)

        _, report = runner.run_config(config, temp_base=self.root, process_runner=no_json)
        self.assertFalse(report["cases"][0]["passed"])
        self.assertIn("JSON", " ".join(report["cases"][0]["errors"]))

    def test_stale_json_is_deleted_and_cannot_be_reused(self):
        config = self._config()
        original_build = runner.build_command
        observed = {"deleted": False}

        def build_with_stale_file(*args, **kwargs):
            spec = original_build(*args, **kwargs)
            spec.json_output.write_text(
                json.dumps(self._successful_json(), ensure_ascii=False), encoding="utf-8")
            return spec

        def no_replacement(arguments, stdout_path, stderr_path, timeout_seconds):
            self._write_streams(stdout_path, stderr_path)
            observed["deleted"] = not self._output_from_arguments(arguments).exists()
            return runner.ProcessResult(0, 1, False)

        with patch.object(runner, "build_command", side_effect=build_with_stale_file):
            _, report = runner.run_config(config, temp_base=self.root, process_runner=no_replacement)
        self.assertTrue(observed["deleted"])
        self.assertFalse(report["cases"][0]["passed"])

    def test_false_consistency_signal_fails_when_baseline_matches(self):
        config = self._config(expect=self._json_expect({
            "success": True,
            "structured": {"count": 1, "categories": [CATEGORY]},
        }))

        def inconsistent(arguments, stdout_path, stderr_path, timeout_seconds):
            self._write_streams(stdout_path, stderr_path)
            self._write_success_json(arguments, {"consistent": False})
            return runner.ProcessResult(0, 1, False)

        _, report = runner.run_config(config, temp_base=self.root, process_runner=inconsistent)
        self.assertFalse(report["cases"][0]["passed"])
        self.assertIn("consistent", " ".join(report["cases"][0]["errors"]))

    def test_success_only_expect_is_rejected_without_result_baseline(self):
        config = self._config(expect={"json": {"success": True}})
        with self.assertRaisesRegex(runner.ConfigError, "result_baseline"):
            runner.load_config(config)

    def test_empty_category_baseline_is_rejected(self):
        config = self._config(expect={
            "json": {"success": True},
            "result_baseline": {"count": 1, "categories": []},
        })
        with self.assertRaises(runner.ConfigError):
            runner.load_config(config)

    def test_empty_expect_is_rejected(self):
        config = self._config(expect={})
        with self.assertRaisesRegex(runner.ConfigError, "expect"):
            runner.load_config(config)

    def test_custom_arguments_field_is_rejected(self):
        config = self._config(extra_case={"arguments": ["--unknown"]})
        with self.assertRaisesRegex(runner.ConfigError, "不支持"):
            runner.load_config(config)

    def test_report_contains_hashes_but_not_absolute_paths(self):
        config = self._config()

        def success(arguments, stdout_path, stderr_path, timeout_seconds):
            self._write_streams(stdout_path, stderr_path)
            self._write_success_json(arguments)
            return runner.ProcessResult(0, 4, False)

        report_path, report = runner.run_config(config, temp_base=self.root, process_runner=success)
        report_text = report_path.read_text(encoding="utf-8")
        self.assertNotIn(str(self.root), report_text)
        program = report["cases"][0]["program_binary"]
        self.assertEqual(runner._sha256(self.exe), program["executable"]["sha256"])
        self.assertEqual(runner._sha256(self.dll), program["dlls"][0]["sha256"])

    def test_cpp_ui_test_command_uses_fixed_json_output_arguments(self):
        expect = {
            "json": {"success": True, "first_result_count": 1, "categories": [CATEGORY]},
            "result_baseline": self._baseline(),
        }
        config = self._config(
            command="ui-test",
            options={
                "threshold": 0.25,
                "device": -1,
                "batch_size": 8,
                "calc_mean": True,
                "device_timeout_ms": 9000,
            },
            expect=expect,
        )
        loaded = runner.load_config(config)
        case_dir = self.root / "case"
        case_dir.mkdir()
        spec = runner.build_command(
            loaded.programs["demo"], loaded.cases[0], loaded.models["model"], loaded.images["image"], case_dir)
        self.assertEqual("ui-test", spec.arguments[1])
        self.assertEqual(str(case_dir / "result.json"), spec.arguments[spec.arguments.index("--output") + 1])
        self.assertIn("--device-timeout-ms", spec.arguments)

    def test_cpp_ui_test_requires_success_in_expected_subset(self):
        config = self._config(
            command="ui-test",
            options={"threshold": 0.5},
            expect={
                "json": {"first_result_count": 1, "categories": [CATEGORY]},
                "result_baseline": self._baseline(),
            },
        )
        loaded = runner.load_config(config)
        with self.assertRaisesRegex(runner.ConfigError, "success"):
            runner.build_command(
                loaded.programs["demo"], loaded.cases[0], loaded.models["model"], loaded.images["image"], self.root)

    def test_c_console_command_sequence(self):
        config = self._config(
            kind="c_console",
            command="infer",
            options={"alias": "sample", "device": 0, "threshold": 0.4, "with_mask": False, "calc_mean": True},
            expect={
                "stdout_contains": "推理成功",
                "result_baseline": self._baseline(count_text="目标数=1"),
            },
        )
        loaded = runner.load_config(config)
        spec = runner.build_command(
            loaded.programs["demo"], loaded.cases[0], loaded.models["model"], loaded.images["image"], self.root)
        self.assertIn("--then", spec.arguments)
        self.assertIn("free-model", spec.arguments)
        self.assertIsNone(spec.json_output)

    def test_c_console_requires_explicit_stdout_encoding(self):
        config = self._config(
            kind="c_console",
            expect={
                "stdout_contains": "推理成功",
                "result_baseline": self._baseline(count_text="目标数=1"),
            },
        )
        data = json.loads(config.read_text(encoding="utf-8"))
        del data["programs"]["demo"]["stdout_encoding"]
        config.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
        with self.assertRaisesRegex(runner.ConfigError, "stdout_encoding"):
            runner.load_config(config)

    def test_c_console_mixed_encoding_fails_without_replacement(self):
        config = self._config(
            kind="c_console",
            stdout_encoding="utf-8",
            expect={
                "stdout_contains": "推理成功",
                "result_baseline": self._baseline(count_text="目标数=1"),
            },
        )

        def mixed_output(arguments, stdout_path, stderr_path, timeout_seconds):
            valid = "推理成功\n目标数=1，类别=[示例类别]\n".encode("utf-8")
            self._write_streams(stdout_path, stderr_path, valid + b"\x81")
            return runner.ProcessResult(0, 3, False)

        _, report = runner.run_config(config, temp_base=self.root, process_runner=mixed_output)
        case = report["cases"][0]
        self.assertFalse(case["passed"])
        self.assertFalse(case["stdout_encoding"]["valid"])
        self.assertIn("严格解码", " ".join(case["errors"]))

    def test_c_console_valid_encoding_checks_text_and_baseline(self):
        config = self._config(
            kind="c_console",
            stdout_encoding="utf-8",
            expect={
                "stdout_contains": ["推理成功", "模型已释放"],
                "result_baseline": self._baseline(count_text="目标数=1"),
            },
        )

        def valid_output(arguments, stdout_path, stderr_path, timeout_seconds):
            text = "推理成功\n目标数=1，类别=[示例类别]\n模型已释放\n"
            self._write_streams(stdout_path, stderr_path, text)
            return runner.ProcessResult(0, 3, False)

        _, report = runner.run_config(config, temp_base=self.root, process_runner=valid_output)
        case = report["cases"][0]
        self.assertTrue(case["passed"])
        self.assertTrue(case["stdout_encoding"]["valid"])

    def test_artifact_minimum_size_and_sha256(self):
        artifact = self._write("output/artifact.bin", b"artifact-data")
        expected_hash = runner._sha256(artifact)
        self.assertEqual([], runner._validate_artifact(artifact, {"min_size": 4, "sha256": expected_hash}))
        self.assertTrue(runner._validate_artifact(artifact, {"min_size": 100}))

    def test_help_contains_baseline_and_encoding_requirements(self):
        help_text = runner._build_parser().format_help()
        self.assertIn("JSON 配置示例", help_text)
        self.assertIn('"result_baseline"', help_text)
        self.assertIn('"stdout_encoding"', help_text)
        self.assertIn('"device_timeout_ms": 30000', help_text)


if __name__ == "__main__":
    unittest.main()
