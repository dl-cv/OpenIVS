using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DLCV.SequenceGraph
{
    public class NodeExecutionException : Exception
    {
        public NodeExecutionException(string message) : base(message) { }
        public NodeExecutionException(string message, Exception innerException) : base(message, innerException) { }
    }

    public sealed class SequenceTriggerFailure
    {
        public string TriggerId { get; internal set; }
        public string TriggerNodeId { get; internal set; }
        public string FailureNodeId { get; internal set; }
        public Exception Exception { get; internal set; }
        public Dictionary<string, object> TriggerInfo { get; internal set; }
        public Dictionary<string, object> Results { get; internal set; }
    }

    public class SequenceGraphExecutor
    {
        public const int DefaultNodeTimeoutMs = 30000;

        public SequenceGraphDocument Graph { get; private set; }
        public Dictionary<string, ISequenceNode> NodeImpls { get; private set; }
        public Dictionary<string, IResourceHandle> ResourceHandles { get; private set; }
        public Dictionary<string, object> LastResults { get; private set; }
        public Dictionary<string, int> ControlInCount
        {
            get { return _controlInCount; }
        }

        public IAiFlowRunner AiFlowRunner { get; set; }
        public IDisplaySink DisplaySink { get; set; }
        public IFaceResultSink FaceResultSink { get; set; }
        public IModbusClient ModbusClient { get; set; }
        public ICameraResourceFactory CameraFactory { get; set; }
        /// <summary>
        /// 是否允许 modbus_tcp_input 连接并读写 PLC。
        /// 关闭时，根触发由 TriggerAsync 注入，流程内门控由 TryInjectGatedSignal 注入。
        /// </summary>
        public bool ModbusEnabled { get; set; } = true;

        [Obsolete("Use ModbusEnabled")]
        public bool AllowModbusBackgroundPoll
        {
            get { return ModbusEnabled; }
            set { ModbusEnabled = value; }
        }
        public IProductTypeResolver ProductTypeResolver { get; set; }
        public Func<string, object> ImageLoader { get; set; }
        public Action<string, string, string> LogSink { get; set; }
        public Func<Task> TriggerCompleted { get; set; }
        public Func<SequenceTriggerFailure, Task> TriggerFailed { get; set; }
        public SequenceTriggerFailure LastFailure { get; private set; }
        public Action<string, string, string, string> ProgressSink { get; set; }
        public Action<ResourceItem, CameraResourceState> CameraStateChanged { get; set; }
        public Action<bool, string> ModbusStateChanged { get; set; }
        public List<Dictionary<string, object>> Events { get; private set; }
        public List<string> TriggerNodeIds { get; private set; }

        public void EmitLog(string level, string nodeId, string message)
        {
            if (LogSink != null)
                LogSink(level, nodeId, message);
        }

        private void EmitLogSafely(string level, string nodeId, string message)
        {
            try
            {
                EmitLog(level, nodeId, message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(
                    "SequenceGraph log sink failed while reporting '{0}': {1}",
                    message,
                    ex.Message);
            }
        }

        public bool IsTriggerRunning
        {
            get { return Interlocked.CompareExchange(ref _runningTrigger, 0, 0) == 1; }
        }

        private SequenceContext _activeCtx;
        private TaskCompletionSource<bool> _idleCompletion;
        private int _runningTrigger;
        private volatile bool _stopping;
        private bool _completionCommitted;
        private bool _resourcesReleased = true;
        private readonly object _lifecycleSync = new object();
        private readonly object _eventsSync = new object();
        private readonly object _triggerSourcesSync = new object();
        private readonly object _gatedSignalsSync = new object();
        private readonly Dictionary<string, ITriggerSourceHandle> _triggerSources =
            new Dictionary<string, ITriggerSourceHandle>(StringComparer.Ordinal);
        private readonly List<TriggerSourceStopFailure> _triggerSourceStopFailures =
            new List<TriggerSourceStopFailure>();
        private readonly Dictionary<string, TaskCompletionSource<Dictionary<string, object>>> _gatedSignalWaiters =
            new Dictionary<string, TaskCompletionSource<Dictionary<string, object>>>(StringComparer.Ordinal);

        private sealed class TriggerSourceStopFailure
        {
            public string NodeId { get; set; }
            public ITriggerSourceHandle Handle { get; set; }
            public Exception Error { get; set; }
        }

        private static readonly string[] TriggerTypes =
        {
            "manual_trigger",
            "wait_io_input",
            "wait_camera_image",
            "modbus_tcp_input",
            "timer",
            "startup"
        };

        private readonly Dictionary<string, SequenceNodeInstance> _nodes;
        private readonly Dictionary<string, List<Tuple<string, string, string>>> _controlOut;
        private readonly Dictionary<string, int> _controlInCount;
        private readonly Dictionary<string, List<Tuple<string, string, string>>> _dataInputs;

        public SequenceGraphExecutor(SequenceGraphDocument graph, Dictionary<string, ISequenceNode> nodeImpls)
        {
            Graph = graph;
            NodeImpls = nodeImpls ?? new Dictionary<string, ISequenceNode>();
            ResourceHandles = new Dictionary<string, IResourceHandle>();
            LastResults = new Dictionary<string, object>();
            Events = new List<Dictionary<string, object>>();
            CameraFactory = new VirtualCameraResourceFactory();
            _nodes = graph.Nodes.ToDictionary(n => n.Id);
            _controlOut = new Dictionary<string, List<Tuple<string, string, string>>>();
            _controlInCount = new Dictionary<string, int>();
            _dataInputs = new Dictionary<string, List<Tuple<string, string, string>>>();
            TriggerNodeIds = CollectTriggerNodeIds(graph);

            foreach (var edge in graph.Edges)
            {
                if (edge.Type == "control")
                {
                    List<Tuple<string, string, string>> list;
                    if (!_controlOut.TryGetValue(edge.Source, out list))
                    {
                        list = new List<Tuple<string, string, string>>();
                        _controlOut[edge.Source] = list;
                    }
                    list.Add(Tuple.Create(edge.Target, edge.SourceHandle, edge.TargetHandle));
                    if (!_controlInCount.ContainsKey(edge.Target))
                        _controlInCount[edge.Target] = 0;
                    _controlInCount[edge.Target]++;
                }
                else if (edge.Type == "data")
                {
                    List<Tuple<string, string, string>> list;
                    if (!_dataInputs.TryGetValue(edge.Target, out list))
                    {
                        list = new List<Tuple<string, string, string>>();
                        _dataInputs[edge.Target] = list;
                    }
                    list.Add(Tuple.Create(edge.TargetHandle, edge.Source, GetOutputKey(edge.Source)));
                }
            }
        }

        public static List<string> CollectTriggerNodeIds(SequenceGraphDocument graph)
        {
            var result = new List<string>();
            if (graph == null || graph.Nodes == null)
                return result;

            var hasControlIn = new HashSet<string>(StringComparer.Ordinal);
            if (graph.Edges != null)
            {
                foreach (var edge in graph.Edges)
                {
                    if (edge != null && edge.Type == "control" && !string.IsNullOrEmpty(edge.Target))
                        hasControlIn.Add(edge.Target);
                }
            }

            var typed = new List<Tuple<int, string>>();
            foreach (var node in graph.Nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.Type) || string.IsNullOrEmpty(node.Id))
                    continue;
                if (hasControlIn.Contains(node.Id))
                    continue;
                int idx = Array.IndexOf(TriggerTypes, node.Type);
                if (idx < 0) continue;
                typed.Add(Tuple.Create(idx, node.Id));
            }
            typed.Sort((a, b) =>
            {
                int c = a.Item1.CompareTo(b.Item1);
                return c != 0 ? c : string.CompareOrdinal(a.Item2, b.Item2);
            });
            return typed.Select(t => t.Item2).ToList();
        }

        public string ResolveTriggerNodeId(string triggerNodeId)
        {
            if (!string.IsNullOrWhiteSpace(triggerNodeId))
            {
                if (!_nodes.ContainsKey(triggerNodeId))
                    throw new InvalidOperationException("trigger node not found: " + triggerNodeId);
                return triggerNodeId;
            }
            if (TriggerNodeIds == null || TriggerNodeIds.Count == 0)
                throw new InvalidOperationException("no available trigger source node");
            return TriggerNodeIds[0];
        }

        public string ResolveSimulateModbusTriggerNodeId(string triggerNodeId)
        {
            if (!string.IsNullOrWhiteSpace(triggerNodeId))
                return ResolveTriggerNodeId(triggerNodeId);

            if (TriggerNodeIds != null)
            {
                foreach (var nid in TriggerNodeIds)
                {
                    SequenceNodeInstance node;
                    if (!_nodes.TryGetValue(nid, out node) || node == null) continue;
                    if (!string.Equals(node.Type, "modbus_tcp_input", StringComparison.Ordinal)) continue;
                    int cin;
                    _controlInCount.TryGetValue(nid, out cin);
                    if (cin == 0) return nid;
                }
            }

            foreach (var node in Graph.Nodes)
            {
                if (node == null || !string.Equals(node.Type, "modbus_tcp_input", StringComparison.Ordinal)) continue;
                int cin;
                _controlInCount.TryGetValue(node.Id, out cin);
                if (cin == 0) return node.Id;
            }

            return ResolveTriggerNodeId(null);
        }

        public bool TryGetNode(string nodeId, out SequenceNodeInstance node)
        {
            return _nodes.TryGetValue(nodeId, out node);
        }

        public void RegisterTriggerSource(string nodeId, ITriggerSourceHandle handle)
        {
            if (string.IsNullOrEmpty(nodeId) || handle == null) return;
            lock (_triggerSourcesSync)
            {
                if (_stopping)
                {
                    try
                    {
                        handle.Disarm();
                        ClearTriggerSourceStopFailureLocked(handle);
                    }
                    catch (Exception ex)
                    {
                        RecordTriggerSourceStopFailureLocked(nodeId, handle, ex);
                        EmitLogSafely("error", nodeId, "disarm late trigger source failed: " + ex.Message);
                    }
                    return;
                }

                ITriggerSourceHandle old;
                if (_triggerSources.TryGetValue(nodeId, out old))
                {
                    try
                    {
                        old.Disarm();
                        ClearTriggerSourceStopFailureLocked(old);
                    }
                    catch (Exception ex)
                    {
                        RecordTriggerSourceStopFailureLocked(nodeId, old, ex);
                        EmitLogSafely("error", nodeId, "disarm replaced trigger source failed: " + ex.Message);
                    }
                }
                _triggerSources[nodeId] = handle;
            }
        }

        private void RecordTriggerSourceStopFailureLocked(
            string nodeId,
            ITriggerSourceHandle handle,
            Exception error)
        {
            var recordedError = new InvalidOperationException(
                "trigger source '" + nodeId + "' disarm failed",
                error);
            var existing = _triggerSourceStopFailures.FirstOrDefault(
                item => object.ReferenceEquals(item.Handle, handle));
            if (existing == null)
            {
                _triggerSourceStopFailures.Add(new TriggerSourceStopFailure
                {
                    NodeId = nodeId,
                    Handle = handle,
                    Error = recordedError
                });
                return;
            }

            existing.NodeId = nodeId;
            existing.Error = recordedError;
        }

        private void ClearTriggerSourceStopFailureLocked(ITriggerSourceHandle handle)
        {
            _triggerSourceStopFailures.RemoveAll(
                item => object.ReferenceEquals(item.Handle, handle));
        }

        private List<KeyValuePair<string, ITriggerSourceHandle>> GetTriggerSourcesToDisarm()
        {
            lock (_triggerSourcesSync)
            {
                var result = _triggerSources.ToList();
                foreach (var failure in _triggerSourceStopFailures)
                {
                    if (!result.Any(item => object.ReferenceEquals(item.Value, failure.Handle)))
                    {
                        result.Add(new KeyValuePair<string, ITriggerSourceHandle>(
                            failure.NodeId,
                            failure.Handle));
                    }
                }
                return result;
            }
        }

        private void TryDisarmTriggerSource(string nodeId, ITriggerSourceHandle handle)
        {
            try
            {
                handle.Disarm();
                lock (_triggerSourcesSync)
                {
                    var keys = _triggerSources
                        .Where(item => object.ReferenceEquals(item.Value, handle))
                        .Select(item => item.Key)
                        .ToList();
                    foreach (var key in keys)
                        _triggerSources.Remove(key);
                    ClearTriggerSourceStopFailureLocked(handle);
                }
            }
            catch (Exception ex)
            {
                lock (_triggerSourcesSync)
                {
                    RecordTriggerSourceStopFailureLocked(nodeId, handle, ex);
                }
                EmitLogSafely("error", nodeId, "disarm trigger source failed: " + ex.Message);
            }
        }

        private AggregateException GetTriggerSourceStopException()
        {
            lock (_triggerSourcesSync)
            {
                if (_triggerSourceStopFailures.Count == 0)
                    return null;
                return new AggregateException(
                    "SequenceHost 触发源停止失败",
                    _triggerSourceStopFailures.Select(item => item.Error).ToList());
            }
        }

        /// <summary>
        /// 向已经进入等待的流程内门控节点注入一次信号。
        /// 节点尚未进入等待时返回 false，信号不会预存。
        /// </summary>
        public bool TryInjectGatedSignal(string nodeId, Dictionary<string, object> signalInfo = null)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new ArgumentException("gated node id is required", "nodeId");

            var info = signalInfo != null
                ? new Dictionary<string, object>(signalInfo)
                : new Dictionary<string, object>();
            if (!info.ContainsKey("source"))
                info["source"] = "simulation";

            lock (_gatedSignalsSync)
            {
                TaskCompletionSource<Dictionary<string, object>> waiter;
                if (!_gatedSignalWaiters.TryGetValue(nodeId, out waiter) || waiter == null)
                    return false;
                return waiter.TrySetResult(info);
            }
        }

        internal async Task<object> WaitForInjectedGatedSignalAsync(
            string nodeId,
            SequenceContext ctx,
            int timeoutMs)
        {
            if (ctx == null)
                throw new ArgumentNullException("ctx");
            if (timeoutMs <= 0)
                timeoutMs = 5000;

            var waiter = new TaskCompletionSource<Dictionary<string, object>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gatedSignalsSync)
            {
                if (_stopping || ctx.Cancelled)
                    throw new OperationCanceledException("gated signal wait cancelled");
                if (_gatedSignalWaiters.ContainsKey(nodeId))
                    throw new InvalidOperationException("gated node is already waiting: " + nodeId);
                _gatedSignalWaiters[nodeId] = waiter;
            }

            EmitLog("info", nodeId, "waiting for injected gated signal");
            try
            {
                var completed = await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs));
                if (completed != waiter.Task)
                    throw new TimeoutException("gated signal timeout");

                var info = await waiter.Task;
                foreach (var kv in info)
                    ctx.TriggerInfo[kv.Key] = kv.Value;
                return info;
            }
            finally
            {
                lock (_gatedSignalsSync)
                {
                    TaskCompletionSource<Dictionary<string, object>> current;
                    if (_gatedSignalWaiters.TryGetValue(nodeId, out current) &&
                        object.ReferenceEquals(current, waiter))
                    {
                        _gatedSignalWaiters.Remove(nodeId);
                    }
                }
            }
        }

        public async Task<bool> TryTriggerFromArmAsync(string triggerNodeId, Dictionary<string, object> triggerInfo)
        {
            return await TryTriggerFromArmAsync(triggerNodeId, triggerInfo, null);
        }

        public async Task<bool> TryTriggerFromArmAsync(
            string triggerNodeId,
            Dictionary<string, object> triggerInfo,
            Action onAccepted)
        {
            if (!TryEnterTrigger())
                return false;
            try
            {
                try
                {
                    if (onAccepted != null)
                        onAccepted();
                }
                catch (Exception ex)
                {
                    EmitLogSafely("error", triggerNodeId, "trigger acceptance callback failed: " + ex.Message);
                    return false;
                }

                try
                {
                    await RunTriggerBodyAsync(triggerNodeId, triggerInfo);
                }
                catch (OperationCanceledException)
                {
                    EmitLogSafely("warn", triggerNodeId, "accepted trigger cancelled during shutdown");
                }
                catch (Exception ex)
                {
                    EmitLogSafely("error", triggerNodeId, "accepted trigger failed: " + ex.Message);
                }
                return true;
            }
            finally
            {
                ExitTrigger();
            }
        }

        private bool TryEnterTrigger()
        {
            lock (_lifecycleSync)
            {
                if (_stopping || _runningTrigger == 1)
                    return false;
                _runningTrigger = 1;
                _completionCommitted = false;
                _idleCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                return true;
            }
        }

        private void ExitTrigger()
        {
            TaskCompletionSource<bool> idleCompletion;
            lock (_lifecycleSync)
            {
                _runningTrigger = 0;
                _completionCommitted = false;
                idleCompletion = _idleCompletion;
                _idleCompletion = null;
            }
            if (idleCompletion != null)
                idleCompletion.TrySetResult(true);
        }

        public Task WaitForIdleAsync()
        {
            Task idleTask;
            lock (_lifecycleSync)
            {
                idleTask = _runningTrigger == 0 || _idleCompletion == null
                    ? Task.CompletedTask
                    : _idleCompletion.Task;
            }

            var triggerSourceStopError = GetTriggerSourceStopException();
            return triggerSourceStopError == null
                ? idleTask
                : WaitForIdleThenThrowAsync(idleTask, triggerSourceStopError);
        }

        private static async Task WaitForIdleThenThrowAsync(Task idleTask, Exception error)
        {
            await idleTask;
            throw error;
        }

        public async Task WaitForIdleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var idleTask = WaitForIdleAsync();
            if (idleTask.IsCompleted || !cancellationToken.CanBeCanceled)
            {
                await idleTask;
                return;
            }

            var cancelled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetCanceled()))
            {
                var completed = await Task.WhenAny(idleTask, cancelled.Task);
                await completed;
            }
        }

        public string GetOutputKey(string nodeId)
        {
            SequenceNodeInstance node;
            if (!_nodes.TryGetValue(nodeId, out node) || node == null)
                return nodeId;
            if (node.Type == "store_json")
                return nodeId;
            if (node.Props != null)
            {
                JToken token;
                if (node.Props.TryGetValue("result_key", out token) && token != null && token.Type != JTokenType.Null)
                {
                    var key = token.ToString();
                    if (!string.IsNullOrEmpty(key))
                        return key;
                }
            }
            return nodeId;
        }

        public void Start()
        {
            Start(CancellationToken.None);
        }

        public void Start(CancellationToken cancellationToken)
        {
            lock (_lifecycleSync)
            {
                if (_runningTrigger == 1)
                    throw new InvalidOperationException("SequenceHost 正在执行触发，不能重新启动");
                _stopping = false;
                _resourcesReleased = false;
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                GraphValidator.Validate(Graph);
                AcquireResources(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ArmTriggerSources(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception startError)
            {
                try
                {
                    Stop();
                }
                catch (Exception stopError)
                {
                    throw new AggregateException("SequenceHost 启动失败且清理未完成", startError, stopError);
                }
                throw;
            }
        }

        public void RequestStop()
        {
            lock (_lifecycleSync)
            {
                _stopping = true;
                if (_activeCtx != null && !_completionCommitted)
                    _activeCtx.Cancelled = true;
            }

            foreach (var kv in GetTriggerSourcesToDisarm())
                TryDisarmTriggerSource(kv.Key, kv.Value);

            List<TaskCompletionSource<Dictionary<string, object>>> gatedWaiters;
            lock (_gatedSignalsSync)
            {
                gatedWaiters = _gatedSignalWaiters.Values.ToList();
                _gatedSignalWaiters.Clear();
            }
            foreach (var waiter in gatedWaiters)
                waiter.TrySetCanceled();
        }

        public void Stop()
        {
            RequestStop();
            var triggerSourceStopError = GetTriggerSourceStopException();
            List<KeyValuePair<string, IResourceHandle>> handles;
            lock (_lifecycleSync)
            {
                if (_runningTrigger == 1)
                    throw new InvalidOperationException("SequenceHost 正在执行触发，不能释放流程与模型资源");
                if (triggerSourceStopError != null)
                    throw triggerSourceStopError;
                if (_resourcesReleased)
                    return;
                handles = ResourceHandles.ToList();
                _activeCtx = null;
            }

            var releaseErrors = new List<Exception>();
            foreach (var item in handles)
            {
                try
                {
                    item.Value.Release();
                    lock (_lifecycleSync)
                    {
                        IResourceHandle current;
                        if (ResourceHandles.TryGetValue(item.Key, out current) &&
                            object.ReferenceEquals(current, item.Value))
                        {
                            ResourceHandles.Remove(item.Key);
                        }
                    }
                    var cameraResource = FindResource("camera", item.Key);
                    if (cameraResource != null)
                        NotifyCameraState(cameraResource, CameraResourceState.NotOpened);
                }
                catch (Exception ex)
                {
                    var cameraResource = FindResource("camera", item.Key);
                    if (cameraResource != null)
                        NotifyCameraState(cameraResource, CameraResourceState.Failed);
                    releaseErrors.Add(ex);
                }
            }
            if (releaseErrors.Count == 0)
            {
                try
                {
                    if (CameraFactory != null)
                        CameraFactory.Clear();
                }
                catch (Exception ex)
                {
                    releaseErrors.Add(ex);
                }
            }
            if (releaseErrors.Count == 0)
            {
                lock (_lifecycleSync)
                {
                    _resourcesReleased = ResourceHandles.Count == 0;
                }
            }
            if (releaseErrors.Count > 0)
                throw new AggregateException("SequenceHost 资源释放失败", releaseErrors);
        }

        public IResourceHandle GetResource(string resourceId)
        {
            IResourceHandle handle;
            if (!ResourceHandles.TryGetValue(resourceId, out handle))
                throw new InvalidOperationException("resource unavailable: " + resourceId);
            return handle;
        }

        public ResourceItem FindResource(string type, string id)
        {
            return Graph.Resources.Items.FirstOrDefault(r => r.Type == type && r.Id == id);
        }

        public object GetDataInput(SequenceContext ctx, string nodeId, string portLabel)
        {
            List<Tuple<string, string, string>> list;
            if (!_dataInputs.TryGetValue(nodeId, out list))
                return null;
            foreach (var item in list)
            {
                if (item.Item1 == portLabel)
                    return ctx.GetResult(item.Item3);
            }
            return null;
        }

        public async Task<string> OnTriggerAsync(string triggerNodeId, Dictionary<string, object> triggerInfo)
        {
            if (!TryEnterTrigger())
                return null;
            try
            {
                return await RunTriggerBodyAsync(triggerNodeId, triggerInfo);
            }
            finally
            {
                ExitTrigger();
            }
        }

        private async Task<string> RunTriggerBodyAsync(string triggerNodeId, Dictionary<string, object> triggerInfo)
        {
            triggerNodeId = ResolveTriggerNodeId(triggerNodeId);
            var ctx = new SequenceContext
            {
                TriggerInfo = triggerInfo ?? new Dictionary<string, object>(),
                LogSink = EmitLog
            };
            lock (_lifecycleSync)
            {
                _activeCtx = ctx;
                if (_stopping)
                    ctx.Cancelled = true;
            }
            try
            {
                LastFailure = null;
                ctx.Log("info", triggerNodeId, "trigger");
                Publish("progress", "trigger_started", triggerNodeId, null, ctx);
                await ExecuteNodeAsync(ctx, triggerNodeId);
                lock (_lifecycleSync)
                {
                    if (ctx.Cancelled || _stopping)
                        throw new OperationCanceledException("sequence trigger cancelled");
                    _completionCommitted = true;
                }
                Publish("completed", null, triggerNodeId, null, ctx);
                LastResults = ctx.GetResultsSnapshot();
                if (TriggerCompleted != null)
                {
                    try { await TriggerCompleted(); }
                    catch (Exception ex) { EmitLogSafely("error", triggerNodeId, "TriggerCompleted: " + ex.Message); }
                }
                return ctx.TriggerId;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastResults = ctx.GetResultsSnapshot();
                var failureException = ctx.FailureException ?? ex;
                LastFailure = new SequenceTriggerFailure
                {
                    TriggerId = ctx.TriggerId,
                    TriggerNodeId = triggerNodeId,
                    FailureNodeId = ctx.FailureNodeId,
                    Exception = failureException,
                    TriggerInfo = new Dictionary<string, object>(ctx.TriggerInfo ?? new Dictionary<string, object>()),
                    Results = ctx.GetResultsSnapshot()
                };
                Publish("failed", failureException.Message, triggerNodeId, ctx.FailureNodeId, ctx);
                if (TriggerFailed != null)
                {
                    try { await TriggerFailed(LastFailure); }
                    catch (Exception callbackError)
                    {
                        EmitLogSafely("error", triggerNodeId, "TriggerFailed: " + callbackError.Message);
                    }
                }
                throw;
            }
            finally
            {
                lock (_lifecycleSync)
                {
                    if (object.ReferenceEquals(_activeCtx, ctx))
                        _activeCtx = null;
                }
            }
        }

        private void ArmTriggerSources(CancellationToken cancellationToken)
        {
            if (TriggerNodeIds == null) return;
            foreach (var nid in TriggerNodeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SequenceNodeInstance node;
                if (!_nodes.TryGetValue(nid, out node) || node == null) continue;
                ISequenceNode impl;
                if (!NodeImpls.TryGetValue(node.Type, out impl) || impl == null) continue;
                try
                {
                    impl.ArmAsync(this, nid).GetAwaiter().GetResult();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    EmitLog("error", nid, "arm failed: " + ex.Message);
                    throw new InvalidOperationException("trigger source arm failed: " + nid, ex);
                }
            }
        }

        private void AcquireResources(CancellationToken cancellationToken)
        {
            foreach (var item in Graph.Resources.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ResourceHandles.ContainsKey(item.Id))
                    continue;
                if (item.Type == "camera")
                {
                    NotifyCameraState(item, CameraResourceState.Opening);
                    try
                    {
                        if (CameraFactory == null)
                            throw new InvalidOperationException("ICameraResourceFactory not injected");
                        ResourceHandles[item.Id] = CameraFactory.Create(item, ImageLoader);
                        NotifyCameraState(item, CameraResourceState.Opened);
                    }
                    catch
                    {
                        NotifyCameraState(item, CameraResourceState.Failed);
                        throw;
                    }
                    continue;
                }
                if (item.Type == "flow")
                {
                    if (AiFlowRunner == null)
                        throw new InvalidOperationException("IAiFlowRunner not injected for flow resource: " + item.Id);
                    var path = item.Config != null && item.Config["path"] != null
                        ? item.Config["path"].ToString()
                        : item.Id;
                    if (string.IsNullOrWhiteSpace(path))
                        throw new InvalidOperationException("flow resource missing path: " + item.Id);
                    string face = null;
                    if (item.Config != null && item.Config["face"] != null)
                    {
                        var raw = item.Config["face"].ToString();
                        if (!string.IsNullOrWhiteSpace(raw))
                            face = raw.Trim().ToUpperInvariant();
                    }
                    var handle = AiFlowRunner.AcquireFlow(path, face);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ResourceHandles[item.Id] = handle;
                    }
                    catch
                    {
                        if (handle != null)
                            handle.Release();
                        throw;
                    }
                }
            }
        }

        private void NotifyCameraState(ResourceItem resource, CameraResourceState state)
        {
            var callback = CameraStateChanged;
            if (callback == null || resource == null)
                return;
            try
            {
                callback(resource, state);
            }
            catch (Exception ex)
            {
                EmitLogSafely("error", resource.Id, "camera state callback failed: " + ex.Message);
            }
        }

        private async Task FireControlAsync(SequenceContext ctx, string sourceNodeId, string sourcePortId)
        {
            List<Tuple<string, string, string>> outs;
            if (!_controlOut.TryGetValue(sourceNodeId, out outs))
                return;
            var nexts = outs.Where(t => t.Item2 == sourcePortId || (sourcePortId == "on_error" && t.Item2 == "out-1")).Select(t => t.Item1).ToList();
            if (nexts.Count == 0)
                return;
            if (nexts.Count == 1)
            {
                await OnControlArriveAsync(ctx, nexts[0]);
                return;
            }
            var tasks = nexts.Select(id => OnControlArriveAsync(ctx, id)).ToArray();
            await Task.WhenAll(tasks);
        }

        private async Task OnControlArriveAsync(SequenceContext ctx, string nodeId)
        {
            var node = _nodes[nodeId];
            if (node.Type == "wait_all" || node.Type == "wait_any")
            {
                var arrived = ctx.IncrementBranchArrival(nodeId);
                int expected;
                _controlInCount.TryGetValue(nodeId, out expected);
                ctx.Log("info", nodeId, "control arrive " + arrived + "/" + expected);
                if (node.Type == "wait_all" && arrived < expected)
                    return;
                if (node.Type == "wait_any")
                {
                    if (arrived > 1)
                        return;
                    ctx.SetBranchState(nodeId, expected + 1);
                }
                else
                {
                    ctx.SetBranchState(nodeId, 0);
                }
            }
            await ExecuteNodeAsync(ctx, nodeId);
        }

        private async Task ExecuteNodeAsync(SequenceContext ctx, string nodeId)
        {
            if (ctx.Cancelled || _stopping)
                throw new OperationCanceledException("sequence execution cancelled");

            var node = _nodes[nodeId];
            ISequenceNode impl;
            if (!NodeImpls.TryGetValue(node.Type, out impl) || impl == null)
            {
                ctx.Log("error", nodeId, "??????: " + node.Type);
                var recovered = await HandleErrorAsync(ctx, nodeId);
                if (recovered) return;
                var error = new NodeExecutionException("sequence node implementation not found: " + node.Type);
                ctx.HasUnrecoveredFailure = true;
                ctx.FailureNodeId = nodeId;
                ctx.FailureException = error;
                throw error;
            }

            ctx.SetStatus(nodeId, "running");
            Publish("progress", "node_started", nodeId, node.Type, ctx);
            try
            {
                EnsureDataInputs(ctx, nodeId);
                var result = await impl.ExecuteAsync(node, ctx, this);
                if (ctx.Cancelled || _stopping)
                    throw new OperationCanceledException("sequence execution cancelled");
                if (result != null)
                    ctx.SetResult(GetOutputKey(nodeId), result);
                ctx.SetStatus(nodeId, "completed");
                Publish("progress", "node_completed", nodeId, node.Type, ctx);
                await FireControlAsync(ctx, nodeId, "out-0");
            }
            catch (OperationCanceledException) when (ctx.Cancelled || _stopping)
            {
                ctx.SetStatus(nodeId, "cancelled");
                Publish("progress", "node_cancelled", nodeId, node.Type, ctx);
                throw;
            }
            catch (Exception ex)
            {
                ctx.SetStatus(nodeId, "failed");
                ctx.Log("error", nodeId, ex.Message);
                Publish("progress", "node_failed", nodeId, node.Type, ctx);
                var recovered = await HandleErrorAsync(ctx, nodeId);
                if (recovered) return;

                if (!ctx.HasUnrecoveredFailure)
                {
                    ctx.HasUnrecoveredFailure = true;
                    ctx.FailureNodeId = nodeId;
                    ctx.FailureException = ex;
                }
                throw new NodeExecutionException(
                    "sequence node failed without recovery: " + nodeId + " (" + node.Type + ")",
                    ex);
            }
        }

        private void EnsureDataInputs(SequenceContext ctx, string nodeId)
        {
            List<Tuple<string, string, string>> list;
            if (!_dataInputs.TryGetValue(nodeId, out list))
                return;
            foreach (var item in list)
            {
                if (!ctx.IsDataReady(new[] { item.Item3 }))
                {
                    throw new NodeExecutionException(
                        "???????: ?? " + nodeId + " ? " + item.Item1 +
                        " ?????? " + item.Item2 + " ??? (key=" + item.Item3 + ")");
                }
            }
        }

        private async Task<bool> HandleErrorAsync(SequenceContext ctx, string nodeId)
        {
            var node = _nodes[nodeId];
            var continueOnError = false;
            if (node.Props != null && node.Props["continue_on_error"] != null)
                continueOnError = node.Props["continue_on_error"].ToObject<bool>();
            if (continueOnError)
            {
                await FireControlAsync(ctx, nodeId, "out-0");
                return true;
            }
            if (node.Type == "run_flow")
            {
                var hasOnError = Graph.Edges.Any(e => e.Type == "control" && e.Source == nodeId && (e.SourceHandle == "out-1" || e.SourceHandle == "on_error"));
                if (hasOnError)
                {
                    await FireControlAsync(ctx, nodeId, "on_error");
                    return true;
                }
            }
            ctx.Log("warn", nodeId, "??????????");
            return false;
        }

        public List<Dictionary<string, object>> GetEventsSnapshot()
        {
            lock (_eventsSync)
            {
                return Events.Select(x => new Dictionary<string, object>(x)).ToList();
            }
        }

        private void Publish(string type, string evt, string nodeId, string nodeType, SequenceContext ctx)
        {
            lock (_eventsSync)
            {
                Events.Add(new Dictionary<string, object>
                {
                    { "type", type },
                    { "event", evt },
                    { "node_id", nodeId },
                    { "node_type", nodeType },
                    { "trigger_id", ctx.TriggerId },
                    { "elapsed_ms", ctx.ElapsedMs }
                });
            }
            if (ProgressSink != null)
            {
                try { ProgressSink(type, evt, nodeId, nodeType); }
                catch (Exception ex) { EmitLog("error", nodeId, "ProgressSink: " + ex.Message); }
            }
        }
    }
}
