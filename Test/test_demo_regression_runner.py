"""验证 C++ Qt 主 Demo 专用回归脚本。"""

import argparse
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_demo_regression as runner

CATEGORY = "狗"
SCORE = 0.9951171875
CRASH_CODE = 0xC0000409


class DemoRegressionRunnerTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.exe = self._write("bin/demo.exe", b"exe")
        self.deployed_dll = self._write("bin/dlcv_infer_cpp.dll", b"wrapper")
        self.dll = self._write("build/dlcv_infer_cpp.dll", b"wrapper")
        self.model = self._write("input/model.dvt", b"model")
        self.image = self._write("input/image.jpg", b"image")

    def tearDown(self):
        self.temp.cleanup()

    def _write(self, relative, data):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def _args(self, **changes):
        values = {
            "exe": self.exe, "dll": self.dll, "model": self.model, "image": self.image,
            "category": CATEGORY, "score": SCORE, "count": 1, "timeout": 90.0,
        }
        values.update(changes)
        return argparse.Namespace(**values)

    @staticmethod
    def _payload(**changes):
        value = {
            "structured": {"count": 1, "categories": [CATEGORY], "scores": [SCORE]},
            "json": {"count": 1, "categories": [CATEGORY], "scores": [SCORE]},
            "consistent": True,
            "inspection_consistent": True,
            "threshold_check_passed": True,
            "mean_check_passed": True,
        }
        value.update(changes)
        return value

    def _process(self, payload=None, returncode=0, raw=None):
        def execute(command, **kwargs):
            output = Path(command[command.index("--output") + 1])
            if raw is not None:
                output.write_bytes(raw)
            elif payload is not None:
                output.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
            self.assertEqual("infer", command[1])
            self.assertEqual("0.5", command[command.index("--threshold") + 1])
            self.assertEqual(90.0, kwargs["timeout"])
            self.assertEqual(runner.subprocess.DEVNULL, kwargs["stdout"])
            self.assertEqual(runner.subprocess.DEVNULL, kwargs["stderr"])
            return runner.subprocess.CompletedProcess(command, returncode)
        return execute

    def test_cli_defaults(self):
        args = runner._parser().parse_args(["--exe", "a", "--dll", "b", "--model", "c",
                                            "--image", "d", "--category", CATEGORY,
                                            "--score", str(SCORE)])
        self.assertEqual(1, args.count)
        self.assertEqual(90, args.timeout)
        self.assertIsNone(args.output)

    def test_success(self):
        with patch.object(runner.subprocess, "run", side_effect=self._process(self._payload())):
            report = runner.run_regression(self._args())
        self.assertTrue(report["passed"])
        self.assertEqual(0, report["exit_code"])
        self.assertEqual(runner._sha256(self.dll), report["dll_sha256"])
        self.assertNotIn(str(self.root), json.dumps(report, ensure_ascii=False))

    def test_complete_json_still_fails_after_crash(self):
        signed_code = CRASH_CODE - (1 << 32)
        with patch.object(runner.subprocess, "run",
                          side_effect=self._process(self._payload(), signed_code)):
            report = runner.run_regression(self._args())
        self.assertFalse(report["passed"])
        self.assertEqual(CRASH_CODE, report["exit_code"])
        self.assertIn("退出码非零", " ".join(report["errors"]))

    def test_timeout(self):
        with patch.object(runner.subprocess, "run",
                          side_effect=runner.subprocess.TimeoutExpired(["demo"], 90)):
            report = runner.run_regression(self._args())
        self.assertFalse(report["passed"])
        self.assertIsNone(report["exit_code"])
        self.assertIn("执行超时", " ".join(report["errors"]))

    def test_missing_and_invalid_json(self):
        cases = ((None, None, "未生成"), (None, b"{", "格式无效"),
                 (None, b"\xff", "有效 UTF-8"))
        for payload, raw, message in cases:
            with self.subTest(message=message), patch.object(
                    runner.subprocess, "run", side_effect=self._process(payload, raw=raw)):
                report = runner.run_regression(self._args())
            self.assertFalse(report["passed"])
            self.assertIn(message, " ".join(report["errors"]))

    def test_wrong_baseline(self):
        payload = self._payload()
        payload["structured"]["scores"] = [SCORE - 0.01]
        with patch.object(runner.subprocess, "run", side_effect=self._process(payload)):
            report = runner.run_regression(self._args())
        self.assertFalse(report["passed"])
        self.assertIn("structured.scores", " ".join(report["errors"]))

    def test_false_consistency_signal(self):
        with patch.object(runner.subprocess, "run",
                          side_effect=self._process(self._payload(consistent=False))):
            report = runner.run_regression(self._args())
        self.assertFalse(report["passed"])
        self.assertIn("consistent 不为 true", report["errors"])

    def test_mismatched_deployed_dll_stops_before_launch(self):
        self.deployed_dll.write_bytes(b"old-wrapper")
        with patch.object(runner.subprocess, "run") as process:
            report = runner.run_regression(self._args())
        process.assert_not_called()
        self.assertFalse(report["passed"])
        self.assertIn("不配套", " ".join(report["errors"]))


if __name__ == "__main__":
    unittest.main()
