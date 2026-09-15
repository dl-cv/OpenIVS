"""检查 C++ 控制台示例仅通过显式参数读取推理输入。"""
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "dlcv_infer_cpp_dll_demo" / "main.cpp"


class CliSourceTest(unittest.TestCase):
    def test_no_fixed_model_or_image_paths(self):
        source = SOURCE.read_text(encoding="utf-8")
        literals = re.findall(r'"([^"\n]*)"', source)
        for literal in literals:
            literal = re.sub(r"<[^<>]+>", "", literal)
            self.assertNotRegex(literal, r"(?i)\.(?:dvt|dvo|dvst|dvso|bmp|jpg|png)\b")
        self.assertNotIn("GetModuleFileName", source)
        self.assertNotIn("DefaultCases", source)
        self.assertNotIn("ModelRoot", source)

    def test_help_precedes_runtime_initialization(self):
        main = SOURCE.read_text(encoding="utf-8").split("int main(", 1)[1]
        self.assertLess(main.index("return 0;"), main.index("ProcessModelCleanup"))
        self.assertLess(main.index("return 0;"), main.index("KeepMaxClock"))


@unittest.skipUnless(os.environ.get("DLCV_CPP_DLL_DEMO_EXE"), "未指定实际 EXE")
class CliExecutableTest(unittest.TestCase):
    def run_cli(self, args, expected):
        exe = Path(os.environ["DLCV_CPP_DLL_DEMO_EXE"]).resolve(strict=True)
        with tempfile.TemporaryDirectory(prefix="cpp_cli_test_") as directory:
            result = subprocess.run(
                [str(exe), *args], cwd=directory,
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30,
            )
            self.assertEqual(result.returncode, expected)
            output = (result.stdout + result.stderr).decode("gb18030", errors="strict")
            self.assertIn("用法", output)
            self.assertNotIn(str(exe.parent), output)
            self.assertEqual(list(Path(directory).iterdir()), [])
            return output

    def test_no_arguments(self):
        output = self.run_cli([], 0)
        self.assertIn("无参数仅显示帮助", output)

    def test_help_aliases(self):
        for alias in ("-h", "--help", "help"):
            with self.subTest(alias=alias):
                self.run_cli([alias], 0)

    @unittest.skipUnless(os.environ.get("DLCV_CPP_DLL_DEMO_MODEL_ROOT"), "未指定推理测试数据目录")
    def test_explicit_inference(self):
        exe = Path(os.environ["DLCV_CPP_DLL_DEMO_EXE"]).resolve(strict=True)
        data = Path(os.environ["DLCV_CPP_DLL_DEMO_MODEL_ROOT"])
        config = json.loads((ROOT / "Test" / "qt_demo_regression_cases.json").read_text(encoding="utf-8"))
        cases = [case for case in config["inference_cases"]
                 if case["id"] in ("classification_dvt", "segmentation_dvt", "flow_dvst")]
        self.assertEqual(len(cases), 3)
        with tempfile.TemporaryDirectory(prefix="cpp_cli_infer_") as directory:
            for case in cases:
                model, image = data / case["model"], data / case["image"]
                self.assertTrue(model.is_file() and image.is_file(), "缺少测试输入")
                for mode in ("case", "pair"):
                    with self.subTest(case=case["id"], mode=mode):
                        args = (["--case", str(model), str(image)] if mode == "case" else
                                ["--model", str(model), "--image", str(image)])
                        result = subprocess.run([str(exe), *args], cwd=directory,
                                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                                timeout=120)
                        self.assertEqual(result.returncode, 0)
                        # 示例按 .936 编译，API 类别转为 GBK；仅解析示例结果摘要，其他运行库日志不参与判断。
                        lines = [line.decode("gb18030", errors="strict")
                                 for line in result.stdout.splitlines()
                                 if line.startswith("结果数量:".encode("gb18030"))
                                 or re.match(rb"  #\d+ ", line)]
                        counts = [line for line in lines if line.startswith("结果数量:")]
                        self.assertEqual(counts, [f"结果数量: {case['expected']['count']}"])
                        rows = [re.search(r"类别=(.*?) 分数=([0-9.]+)", line).groups()
                                for line in lines if line.startswith("  #")]
                        self.assertEqual(len(rows), case["expected"]["count"])
                        if case["id"] != "flow_dvst":
                            self.assertTrue([row[0] for row in rows] == case["expected"]["categories"],
                                            "类别与基准不一致")
                            self.assertEqual(len(rows), len(case["expected"]["scores"]))
                            self.assertTrue(all(abs(float(row[1]) - score) <= 0.00005
                                                for row, score in zip(rows, case["expected"]["scores"])),
                                            "四位小数分数与基准不一致")

    def test_missing_inputs(self):
        cases = [
            ["--device", "0"], ["--pressure"], ["--case"],
            ["--case", "model.dvst"], ["--model", "model.dvst"],
            ["--image", "image.png"],
            ["--case", "model.dvst", "image.png", "--model", "other.dvst"],
        ]
        for args in cases:
            with self.subTest(args=args):
                self.run_cli(args, 1)


if __name__ == "__main__":
    unittest.main()
