"""检查实际测试程序拒绝无效的显示区域和窗口尺寸。"""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ARGS = None

class ViewRegionTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if ARGS is None:
            raise unittest.SkipTest("通过命令行指定实际测试程序、模型和图片")

    def test_invalid_regions(self):
        for exe in (ARGS.cpp_exe, ARGS.csharp_exe):
            for region in ("1,2,3", "-1,2,3,4", "1,2,0,4", "1,2,nan,4", "1,2,inf,4"):
                with self.subTest(exe=exe.name, region=region):
                    self.check_arguments(exe, ["--view-region", region])
            self.check_arguments(exe, ["--view-region", "1,2,3,4", "--view-region", "1,2,3,4"])

    def test_invalid_window_sizes(self):
        for size in ("640", "639,480", "640,479", "nan,480", "640,480,1"):
            with self.subTest(size=size):
                self.check_arguments(ARGS.csharp_exe, ["--window-size", size])

    def check_arguments(self, exe, extra):
        with tempfile.TemporaryDirectory(prefix="openivs-view-region-") as directory:
            command = [str(exe), "ui-test", "--model", str(ARGS.model), "--image", str(ARGS.image),
                       "--output", str(Path(directory) / "result.json"), *extra]
            env = os.environ.copy()
            env["QT_QPA_PLATFORM"] = "offscreen"
            qt_plugins = Path(os.environ["Qt6_DIR"]).parents[1] / "plugins"
            env["QT_PLUGIN_PATH"] = str(qt_plugins)
            env["QT_QPA_PLATFORM_PLUGIN_PATH"] = str(qt_plugins / "platforms")
            result = subprocess.run(command, cwd=directory, env=env, capture_output=True, timeout=20)
            self.assertEqual(result.returncode, 2, result.stderr.decode("utf-8", "replace"))
            self.assertFalse((Path(directory) / "result.json").exists())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("cpp-exe", "csharp-exe", "model", "image"):
        parser.add_argument("--" + name, type=lambda value: Path(value).resolve(), required=True)
    ARGS, remaining = parser.parse_known_args()
    unittest.main(argv=[__file__, *remaining])
