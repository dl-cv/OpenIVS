"""启动相机检查接线回归；不启动 GUI 或访问设备。"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class CameraStartupTests(unittest.TestCase):
    def test_missing_camera_returns_before_starting_resources(self):
        source = (ROOT / "OpenIVS2/MainWindow.xaml.cs").read_text(encoding="utf-8-sig")
        start = source.index("private async Task StartSystemAsync(")
        end = source.index("private void SaveRuntimeSequence", start)
        startup = source[start:end]
        self.assertIn("CameraStartupCheck.FindMissing", startup)
        check = startup.index("CameraStartupCheck.FindMissing")
        resources = startup.index("SequenceGraphBuilder.Build")
        branch = startup[check:resources]
        self.assertIn("missingCameras.Count > 0", branch)
        self.assertIn("MessageBoxImage.Warning", branch)
        self.assertIn("return;", branch)
        self.assertNotIn("throw", branch)
        self.assertNotIn("Shutdown", startup)
        self.assertIn("SetBusy(false, null)", startup)


if __name__ == "__main__":
    unittest.main()
