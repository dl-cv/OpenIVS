"""实际 EXE 回归；真实模型与独立预期结果通过环境变量指定。"""
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import tempfile
import unittest


@unittest.skipUnless(os.environ.get("DLCV_BINARY_DEMO_EXE"), "未指定实际 EXE")
class BinaryDemoTest(unittest.TestCase):
    def setUp(self):
        self.exe = Path(os.environ["DLCV_BINARY_DEMO_EXE"]).resolve(strict=True)
        self.directory = tempfile.TemporaryDirectory(prefix="binary_demo_")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.image = self.root / "中文图片.bmp"
        # 3x1、24 位 BMP，9 字节像素加 3 字节行对齐；检查非四倍数宽度读图。
        pixels = bytes([0, 0, 255, 0, 255, 0, 255, 0, 0, 0, 0, 0])
        header = struct.pack("<2sIHHI", b"BM", 54 + len(pixels), 0, 0, 54)
        info = struct.pack("<IiiHHIIiiII", 40, 3, 1, 1, 24, 0, len(pixels), 0, 0, 0, 0)
        self.image.write_bytes(header + info + pixels)

    def run_cli(self, args, expected):
        result = subprocess.run([str(self.exe), *map(str, args)], cwd=self.root,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        # SDK 的原生调试日志可能使用本地代码页；Demo 的输出始终是 UTF-8。
        output = result.stdout.decode("utf-8", errors="replace").replace("\r\n", "\n")
        self.assertEqual(result.returncode, expected, output)
        return output

    def test_help_without_runtime(self):
        self.assertIn("用法:", self.run_cli(["--help"], 0))

    def test_missing_arguments(self):
        self.assertIn("用法:", self.run_cli([], 2))

    def test_missing_image(self):
        self.assertIn("读取图片失败", self.run_cli(["model.dvst", "missing.jpg"], 1))

    def test_invalid_image(self):
        invalid = self.root / "invalid.jpg"
        invalid.write_bytes(b"not an image")
        self.assertIn("读取图片失败", self.run_cli(["model.dvst", invalid], 1))

    def test_missing_dll_after_rgb_decode(self):
        self.assertIn("加载 DLL 失败", self.run_cli(["model.dvst", self.image, "missing.dll"], 1))

    def test_missing_export(self):
        dll = Path(os.environ["SystemRoot"]) / "System32" / "kernel32.dll"
        self.assertIn("DLL 缺少接口: dlcv_infer_cpp_load_model_c",
                      self.run_cli(["model.dvst", self.image, dll], 1))

    @unittest.skipUnless(os.environ.get("DLCV_BINARY_DEMO_DLL"), "未指定实际 DLL")
    def test_missing_model(self):
        self.assertIn("模型加载失败", self.run_cli(
            ["missing.dvst", self.image, os.environ["DLCV_BINARY_DEMO_DLL"]], 1))

    def test_default_dll_is_beside_exe(self):
        if not (self.exe.parent / "dlcv_infer_cpp.dll").is_file():
            self.skipTest("EXE 同目录没有 DLL")
        self.assertIn("模型加载失败", self.run_cli(["missing.dvst", self.image], 1))

    @unittest.skipUnless(all(os.environ.get(k) for k in (
        "DLCV_BINARY_DEMO_DLL", "DLCV_BINARY_DEMO_MODEL", "DLCV_BINARY_DEMO_IMAGE",
        "DLCV_BINARY_DEMO_EXPECTED")), "未指定真实推理材料及独立预期")
    def test_real_inference(self):
        output = self.run_cli([os.environ["DLCV_BINARY_DEMO_MODEL"],
                               os.environ["DLCV_BINARY_DEMO_IMAGE"],
                               os.environ["DLCV_BINARY_DEMO_DLL"]], 0)
        expected = json.loads(Path(os.environ["DLCV_BINARY_DEMO_EXPECTED"]).read_text(encoding="utf-8-sig"))
        targets = re.findall(r"^\[\d+\] (.+?)\s+score=([\d.]+)  bbox=\(([^)]+)\)  area=([\d.]+)$",
                             output, re.MULTILINE)
        self.assertEqual(len(targets), len(expected), output)
        self.assertIn(f"推理结果: {len(expected)}个", output)
        for actual, wanted in zip(targets, expected):
            name, score, bbox, area = actual
            self.assertEqual(name, wanted["name"])
            self.assertEqual(float(score), wanted["score"])
            self.assertEqual([float(x) for x in bbox.split(",")], wanted["bbox"])
            self.assertEqual(float(area), wanted["area"])


if __name__ == "__main__":
    unittest.main()
