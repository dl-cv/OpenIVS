using System;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenIVS2.Services;

namespace DlcvSequenceTest
{
    internal static class FlowTimingTests
    {
        public static void Run(Action<bool, string> assert)
        {
            var tracker = new FlowTimingTracker();
            var second = Stopwatch.Frequency;
            tracker.OnProgress("node_started", "run_A", "run_flow");
            assert(!tracker.OnProgress("node_completed", "run_A", "run_flow").HasValue,
                "没有模型调用记录时，不得将流程节点耗时显示为模型调用耗时");

            tracker.OnProgress("node_started", "wait_A", "wait_camera_image");
            tracker.RecordModelCall("A", 0, second * 10);
            assert(!tracker.OnProgress("node_completed", "wait_A", "wait_camera_image").HasValue,
                "取图节点不产生模型调用耗时");
            tracker.OnProgress("node_started", "run_A", "run_flow");
            tracker.OnProgress("node_started", "run_B", "run_flow");
            tracker.RecordModelCall("B", second * 20, second * 23);
            tracker.RecordModelCall("A", second * 10, second * 12);
            var a = tracker.OnProgress("node_completed", "run_A", "run_flow");
            var b = tracker.OnProgress("node_completed", "run_B", "run_flow");
            assert(a == 2000 && b == 3000, "相机交错完成时独立显示模型调用时间，不使用流程事件间隔");
            assert(!tracker.OnProgress("node_completed", "display_A", "update_display").HasValue,
                "显示节点不产生模型调用耗时");
            assert(!tracker.OnProgress("node_completed", "run_A", "run_flow").HasValue,
                "完成后消费记录，重复事件不复用旧耗时");

            foreach (var terminal in new[] { "node_failed", "node_cancelled" })
            {
                tracker.OnProgress("node_started", "run_A", "run_flow");
                tracker.RecordModelCall("A", second, second * 2);
                assert(!tracker.OnProgress(terminal, "run_A", "run_flow").HasValue &&
                    !tracker.OnProgress("node_completed", "run_A", "run_flow").HasValue,
                    terminal + " 丢弃模型调用耗时，不报告成功");
                tracker.RecordModelCall("A", second, second * 2);
                assert(!tracker.OnProgress("node_completed", "run_A", "run_flow").HasValue,
                    terminal + " 后忽略迟到的调用记录");
            }
            tracker.OnProgress("node_started", "run_A", "run_flow");
            tracker.RecordModelCall("A", second, second * 2);
            tracker.OnProgress("trigger_started", "trigger", "manual_trigger");
            assert(!tracker.OnProgress("node_completed", "run_A", "run_flow").HasValue,
                "新周期清除上一周期记录");
            tracker.OnProgress("node_started", "run_A", "run_flow");
            tracker.RecordModelCall("A", second * 4, second * 5);
            assert(tracker.OnProgress("node_completed", "run_A", "run_flow") == 1000,
                "重复周期仅使用本次模型调用记录");
            tracker.OnProgress("node_started", "run_A", "run_flow");
            tracker.RecordModelCall("A", second, second * 2);
            tracker.Reset();
            tracker.RecordModelCall("A", second, second * 2);
            assert(!tracker.OnProgress("node_completed", "run_A", "run_flow").HasValue,
                "停止后清除记录并忽略迟到的调用完成通知");
            tracker.OnProgress("node_started", "run_A", "run_flow");
            tracker.RecordModelCall("A", second, second * 2);
            tracker.OnProgress("node_started", "run_A", "run_flow");
            assert(!tracker.OnProgress("node_completed", "run_A", "run_flow").HasValue,
                "重新开始节点但未调用模型时，不显示旧值");
            Parallel.For(0, 6, i =>
            {
                var face = i.ToString();
                var node = "run_" + face;
                tracker.OnProgress("node_started", node, "run_flow");
                tracker.RecordModelCall(face, second * i, second * (i + 1));
                if (tracker.OnProgress("node_completed", node, "run_flow") != 1000)
                    throw new InvalidOperationException("并发模型调用计时串台");
            });
            assert(true, "六路并发调用记录安全");
        }
    }
}
