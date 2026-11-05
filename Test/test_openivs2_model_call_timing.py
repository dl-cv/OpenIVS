"""检查模型调用计时边界与 UI 接线；行为回归在 DlcvSequenceTest 中。"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class ModelCallTimingTests(unittest.TestCase):
    def test_only_model_call_is_between_timestamps(self):
        source = (ROOT / "OpenIVS2/Services/DlcvFlowRunner.cs").read_text(encoding="utf-8-sig")
        before, timed = source.split("var started = Stopwatch.GetTimestamp();", 1)
        call, after = timed.split("var completed = Stopwatch.GetTimestamp();", 1)
        self.assertEqual(call.strip(), "var result = model.Infer(rgb);")
        self.assertIn("using (var rgb = ToRgb(source))", before)
        self.assertLess(after.index("ModelCallCompleted?.Invoke(face, started, completed)"),
                        after.index("return result;"))

    def test_window_uses_model_record_not_node_clock(self):
        source = (ROOT / "OpenIVS2/MainWindow.xaml.cs").read_text(encoding="utf-8-sig")
        self.assertIn("ModelCallCompleted = _flowTiming.RecordModelCall", source)
        self.assertIn("_flowTiming.OnProgress(evt, nodeId, nodeType)", source)
        self.assertNotIn("Stopwatch.GetTimestamp()", source)
        self.assertNotIn("流程推理：", source)
        self.assertNotIn("Sequence 流程节点执行耗时", source)
        for text in ("模型调用：", "模型调用：-- ms", "模型调用：失败", "模型调用：已取消"):
            self.assertIn(text, source)


if __name__ == "__main__":
    unittest.main()
