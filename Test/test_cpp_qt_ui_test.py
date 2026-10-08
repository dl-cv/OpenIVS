"""使用实际 C++ Qt 程序检查界面验证入口。"""
from __future__ import annotations

import argparse
from collections import Counter
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


EXE = MODEL = IMAGE = None


class QtUiTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if EXE is None:
            raise unittest.SkipTest("通过 --exe、--model、--image 指定实际验证材料")
        qt_dir = os.environ.get("Qt6_DIR")
        if not qt_dir:
            raise RuntimeError("界面验证需要已配置的 Qt6_DIR 环境变量")
        cls.qt_plugins = Path(qt_dir).parents[1] / "plugins"
        if not (cls.qt_plugins / "platforms" / "qoffscreen.dll").is_file():
            raise RuntimeError("Qt 安装目录缺少无界面验证所需的 qoffscreen.dll")

    def run_case(self, extra, expected_exit, *, output=True):
        with tempfile.TemporaryDirectory(prefix="dlcv-qt-ui-") as directory:
            result_path = Path(directory) / "result.json"
            command = [str(EXE), "ui-test", "--model", str(MODEL), "--image", str(IMAGE)]
            if output:
                command += ["--output", str(result_path)]
            command += extra
            env = os.environ.copy()
            env["QT_QPA_PLATFORM"] = "offscreen"
            env["QT_PLUGIN_PATH"] = str(self.qt_plugins)
            env["QT_QPA_PLATFORM_PLUGIN_PATH"] = str(self.qt_plugins / "platforms")
            process = subprocess.run(command, cwd=directory, env=env,
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=90)
            self.assertEqual(process.returncode, expected_exit, process.stderr.decode("utf-8", "replace"))
            return json.loads(result_path.read_text(encoding="utf-8")) if result_path.exists() else None

    def test_output_is_required(self):
        self.assertIsNone(self.run_case([], 2, output=False))

    def test_font_range_is_checked(self):
        self.assertIsNone(self.run_case(["--label-font-scale", "0.2"], 2))

    def test_duplicate_view_is_rejected(self):
        self.assertIsNone(self.run_case(["--result-view", "json", "--result-view", "summary"], 2))

    def test_missing_device_has_no_dialog(self):
        result = self.run_case(["--device", "999"], 1)
        self.assertFalse(result["passed"])
        self.assertIn("指定设备不可用", result["error"])

    def test_aoi_summary(self):
        result = self.run_case(["--label-font-scale", "0.5"], 0)
        self.assertTrue(result["passed"])
        self.assertEqual(result["object_count"], 10)
        self.assertEqual(Counter(result["categories"]),
            Counter({"字符90": 4, "二极管90": 5, "0402电阻90": 1}))
        self.assertAlmostEqual(result["label_font_scale"], 0.5)
        self.assertIn("推理结果: 10个", result["result_text"])
        self.assertGreater(result["input_width"], 0)
        self.assertGreater(result["input_height"], 0)

    def test_release_model_view(self):
        result = self.run_case(["--result-view", "release"], 0)
        self.assertFalse(result["model_loaded"])
        self.assertEqual(result["result_text"], "模型已释放")

    def test_aoi_model_info_view(self):
        result = self.run_case(["--result-view", "model"], 0)
        self.assertTrue(result["passed"])
        self.assertIsInstance(result["result_json"], dict)
        self.assertIn("input_shapes", result["result_json"])
        self.assertIn("task_type", result["result_json"])

    def test_aoi_json_view(self):
        result = self.run_case(["--result-view", "json"], 0)
        self.assertTrue(result["passed"])
        self.assertEqual(result["object_count"], 10)
        objects = result["result_json"]
        self.assertEqual(len(objects), 10)
        self.assertEqual(objects[0]["category_name"], "字符90")
        self.assertEqual(objects[0]["bbox"], [221.0, 375.0, 33.0, 50.0])
        self.assertEqual(json.loads(result["result_text"]), objects)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, required=True)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--image", type=Path, required=True)
    arguments, remaining = parser.parse_known_args()
    EXE = arguments.exe.resolve()
    MODEL = arguments.model.resolve()
    IMAGE = arguments.image.resolve()
    unittest.main(argv=[__file__, *remaining])
