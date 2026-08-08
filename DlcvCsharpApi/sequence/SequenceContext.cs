using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DLCV.SequenceGraph
{
    public class SequenceContext
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly object _sync = new object();

        public string TriggerId { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 16);
        public Dictionary<string, object> TriggerInfo { get; set; } = new Dictionary<string, object>();
        public string RunId { get; set; } = "";
        public Dictionary<string, object> Results { get; private set; } = new Dictionary<string, object>();
        public Dictionary<string, int> BranchStates { get; private set; } = new Dictionary<string, int>();
        public Dictionary<string, string> Status { get; private set; } = new Dictionary<string, string>();
        public List<Dictionary<string, object>> Logs { get; private set; } = new List<Dictionary<string, object>>();
        public bool Cancelled { get; set; }
        public bool HasUnrecoveredFailure { get; set; }
        public string FailureNodeId { get; set; }
        public Exception FailureException { get; set; }
        public Action<string, string, string> LogSink { get; set; }

        public int ElapsedMs
        {
            get { return (int)_sw.ElapsedMilliseconds; }
        }

        public void SetResult(string key, object value)
        {
            lock (_sync)
                Results[key] = value;
        }

        public object GetResult(string key)
        {
            lock (_sync)
            {
                object value;
                return Results.TryGetValue(key, out value) ? value : null;
            }
        }

        public bool IsDataReady(IEnumerable<string> keys)
        {
            lock (_sync)
            {
                foreach (var key in keys)
                {
                    if (!Results.ContainsKey(key))
                        return false;
                }
                return true;
            }
        }

        public Dictionary<string, object> GetResultsSnapshot()
        {
            lock (_sync)
                return new Dictionary<string, object>(Results);
        }

        public int IncrementBranchArrival(string nodeId)
        {
            lock (_sync)
            {
                int arrived;
                BranchStates.TryGetValue(nodeId, out arrived);
                arrived++;
                BranchStates[nodeId] = arrived;
                return arrived;
            }
        }

        public void SetBranchState(string nodeId, int value)
        {
            lock (_sync)
                BranchStates[nodeId] = value;
        }

        public void SetStatus(string nodeId, string value)
        {
            lock (_sync)
                Status[nodeId] = value;
        }

        public void Log(string level, string nodeId, string message)
        {
            lock (_sync)
            {
                Logs.Add(new Dictionary<string, object>
                {
                    { "timestamp", DateTime.UtcNow },
                    { "level", level },
                    { "node_id", nodeId },
                    { "message", message }
                });
            }
            if (LogSink != null)
                LogSink(level, nodeId, message);
        }
    }
}
