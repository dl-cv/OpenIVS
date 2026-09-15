"""验证桌面程序回归脚本不会把错误结果计为通过。"""
import json
import struct
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_desktop_project_regression as runner


class DesktopRegressionTest(unittest.TestCase):
    def payload(self):
        return {"passed": True, "cases": [
            {"name": name, "passed": True, "count": 1, "categories": [case[2]], "scores": [case[3]], "release_check_passed": True, "render_check_passed": True}
            for name, case in runner.CASES.items()
        ]}

    def run_case(self, payload, code=0):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            def execute(command, directory, work, timeout):
                work.mkdir(parents=True)
                (work / "result.json").write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
                for name in runner.CASES:
                    (work / (name + ".png")).write_bytes(b"\x89PNG\r\n\x1a\n" + b"\x00" * 8 + struct.pack(">II", 500, 400))
                return code
            with patch.object(runner, "launch", side_effect=execute):
                return runner.run_wpf("OpenIVSWPF", root / "app.exe", root, root, root, 1)

    def test_valid_fixed_cases_pass(self):
        self.assertTrue(self.run_case(self.payload())["passed"])

    def test_flags_do_not_hide_wrong_results(self):
        for field, value in (("count", 1.0), ("count", True), ("categories", ["错误"]), ("scores", ["0.9951171875"]), ("scores", [float("nan")]), ("scores", [0.1])):
            payload = self.payload()
            payload["cases"][0][field] = value
            self.assertFalse(self.run_case(payload)["passed"], field)

    def test_missing_case_and_failed_stage_are_rejected(self):
        payload = self.payload()
        payload["cases"].pop()
        self.assertFalse(self.run_case(payload)["passed"])
        for flag in ("passed", "release_check_passed", "render_check_passed"):
            payload = self.payload()
            payload["cases"][0][flag] = False
            self.assertFalse(self.run_case(payload)["passed"], flag)

    def test_nonzero_exit_is_not_success(self):
        self.assertFalse(self.run_case(self.payload(), code=3)["passed"])


if __name__ == "__main__":
    unittest.main()
