using System;
using System.Collections.Generic;

namespace DlcvModules
{
    /// <summary>
    /// 轻量执行上下文：用于在执行图期间传递共享参数与回调
    /// </summary>
    public class ExecutionContext
    {
        private readonly Dictionary<string, object> _map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public ExecutionContext()
        {
        }

        public T Get<T>(string key, T defaultValue = default(T))
        {
            if (key == null) return defaultValue;
            if (_map.TryGetValue(key, out object value))
            {
                try
                {
                    if (value is T t) return t;
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        public void Set(string key, object value)
        {
            if (key == null) return;
            _map[key] = value;
        }
    }

    /// <summary>
    /// 模块注册表：按模块类型字符串获取对应的实现类型
    /// </summary>
    public static class ModuleRegistry
    {
        private static readonly Dictionary<string, Type> _registry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string moduleType, Type moduleClass)
        {
            if (string.IsNullOrWhiteSpace(moduleType)) throw new ArgumentException("moduleType is null or empty");
            if (moduleClass == null) throw new ArgumentNullException(nameof(moduleClass));
            _registry[moduleType] = moduleClass;
        }

        public static Type Get(string moduleType)
        {
            if (string.IsNullOrWhiteSpace(moduleType)) return null;
            _registry.TryGetValue(moduleType, out Type t);
            return t;
        }
    }

    public static class GlobalDebug
    {
#if DEBUG
        public static bool PrintDebug { get; set; } = true;
#else
        public static bool PrintDebug { get; set; } = false;
#endif
        public static void Log(string message)
        {
            if (PrintDebug)
            {
                Console.WriteLine($"[DEBUG] {message}");
            }
        }
    }

    public sealed class FlowNodeTiming
    {
        public int NodeId { get; private set; }
        public string NodeType { get; private set; }
        public string NodeTitle { get; private set; }
        public double ElapsedMs { get; private set; }

        public FlowNodeTiming(int nodeId, string nodeType, string nodeTitle, double elapsedMs)
        {
            NodeId = nodeId;
            NodeType = nodeType ?? string.Empty;
            NodeTitle = nodeTitle ?? string.Empty;
            ElapsedMs = Math.Max(0.0, elapsedMs);
        }

        public FlowNodeTiming Clone()
        {
            return new FlowNodeTiming(NodeId, NodeType, NodeTitle, ElapsedMs);
        }
    }

    /// <summary>
    /// 线程内推理计时：用于区分 dlcv_infer 耗时与流程总耗时。
    /// 同时记录每个流程节点的耗时，便于定位 batch 退化点。
    /// </summary>
    public static class InferTiming
    {
        [ThreadStatic] private static double _currentDlcvInferMs;
        [ThreadStatic] private static double _lastDlcvInferMs;
        [ThreadStatic] private static double _lastFlowInferMs;
        [ThreadStatic] private static List<FlowNodeTiming> _currentFlowNodeTimings;
        [ThreadStatic] private static List<FlowNodeTiming> _lastFlowNodeTimings;

        public static void BeginFlowRequest()
        {
            _currentDlcvInferMs = 0.0;
            if (_currentFlowNodeTimings == null) _currentFlowNodeTimings = new List<FlowNodeTiming>();
            _currentFlowNodeTimings.Clear();
        }

        public static void AddDlcvInferMs(double costMs)
        {
            if (costMs <= 0) return;
            _currentDlcvInferMs += costMs;
        }

        public static void AddFlowNodeMs(int nodeId, string nodeType, string nodeTitle, double costMs)
        {
            if (costMs < 0) costMs = 0.0;
            if (_currentFlowNodeTimings == null) _currentFlowNodeTimings = new List<FlowNodeTiming>();
            _currentFlowNodeTimings.Add(new FlowNodeTiming(nodeId, nodeType, nodeTitle, costMs));
        }

        public static void EndFlowRequest(double flowInferMs)
        {
            _lastDlcvInferMs = Math.Max(0.0, _currentDlcvInferMs);
            _lastFlowInferMs = Math.Max(0.0, flowInferMs);
            if (_currentFlowNodeTimings == null)
            {
                _lastFlowNodeTimings = new List<FlowNodeTiming>();
                return;
            }

            if (_lastFlowNodeTimings == null) _lastFlowNodeTimings = new List<FlowNodeTiming>();
            _lastFlowNodeTimings.Clear();
            for (int i = 0; i < _currentFlowNodeTimings.Count; i++)
            {
                var item = _currentFlowNodeTimings[i];
                if (item != null) _lastFlowNodeTimings.Add(item.Clone());
            }
        }

        public static void SetDirectRequest(double inferMs)
        {
            var ms = Math.Max(0.0, inferMs);
            _lastDlcvInferMs = ms;
            _lastFlowInferMs = ms;
        }

        public static void GetLast(out double dlcvInferMs, out double flowInferMs)
        {
            dlcvInferMs = Math.Max(0.0, _lastDlcvInferMs);
            flowInferMs = Math.Max(0.0, _lastFlowInferMs);
        }

        public static List<FlowNodeTiming> GetLastFlowNodeTimings()
        {
            var result = new List<FlowNodeTiming>();
            if (_lastFlowNodeTimings == null) return result;
            for (int i = 0; i < _lastFlowNodeTimings.Count; i++)
            {
                var item = _lastFlowNodeTimings[i];
                if (item != null) result.Add(item.Clone());
            }
            return result;
        }
    }
}
