using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpenIVS2.Services
{
    // Sequence 事件只管理显示生命周期，数值来自 Model.Infer 调用前后的时间戳。
    internal sealed class FlowTimingTracker
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, double?> _modelCalls = new Dictionary<string, double?>(StringComparer.Ordinal);

        public void RecordModelCall(string face, long started, long completed)
        {
            var nodeId = "run_" + face;
            var elapsedMs = (completed - started) * 1000.0 / Stopwatch.Frequency;
            lock (_sync)
            {
                if (_modelCalls.ContainsKey(nodeId)) _modelCalls[nodeId] = elapsedMs;
            }
        }

        public double? OnProgress(string evt, string nodeId, string nodeType)
        {
            lock (_sync)
            {
                if (evt == "trigger_started") _modelCalls.Clear();
                if (nodeType != "run_flow" || string.IsNullOrEmpty(nodeId)) return null;
                if (evt == "node_started")
                {
                    _modelCalls[nodeId] = null;
                    return null;
                }
                if (evt != "node_completed" && evt != "node_failed" && evt != "node_cancelled") return null;
                double? elapsedMs;
                if (!_modelCalls.TryGetValue(nodeId, out elapsedMs)) return null;
                _modelCalls.Remove(nodeId);
                return evt == "node_completed" ? elapsedMs : null;
            }
        }

        public void Reset()
        {
            lock (_sync) _modelCalls.Clear();
        }
    }
}
