using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DLCV.SequenceGraph;
using DLCV.SequenceGraph.Nodes;

namespace DLCV.SequenceGraph
{
    public class SequenceHost
    {
        public SequenceGraphExecutor Executor { get; private set; }
        public SequenceGraphDocument Graph { get; private set; }

        public void Load(string flowPath, IAiFlowRunner ai = null, IDisplaySink display = null, IModbusClient modbus = null, Func<string, object> imageLoader = null, Action<string, string, string> logSink = null, IProductTypeResolver productResolver = null, IFaceResultSink faceResultSink = null)
        {
            Load(SequenceGraphLoader.FromFile(flowPath), ai, display, modbus, imageLoader, logSink, productResolver, faceResultSink);
        }

        public void Load(SequenceGraphDocument graph, IAiFlowRunner ai = null, IDisplaySink display = null, IModbusClient modbus = null, Func<string, object> imageLoader = null, Action<string, string, string> logSink = null, IProductTypeResolver productResolver = null, IFaceResultSink faceResultSink = null)
        {
            LoadWithoutStart(graph, ai, display, modbus, imageLoader, logSink, productResolver, faceResultSink);
            Start();
        }

        /// <summary>
        /// 装载图与依赖，但不启动触发源（后台轮询）。便于调用方先设置 ModbusEnabled 等开关。
        /// </summary>
        public void LoadWithoutStart(SequenceGraphDocument graph, IAiFlowRunner ai = null, IDisplaySink display = null, IModbusClient modbus = null, Func<string, object> imageLoader = null, Action<string, string, string> logSink = null, IProductTypeResolver productResolver = null, IFaceResultSink faceResultSink = null)
        {
            if (graph == null)
                throw new ArgumentNullException("graph");
            Graph = graph;
            Executor = new SequenceGraphExecutor(Graph, DefaultNodeFactory.CreateDefault());
            Executor.AiFlowRunner = ai ?? new MockAiFlowRunner();
            Executor.DisplaySink = display ?? new CollectingDisplaySink();
            Executor.ModbusClient = modbus;
            Executor.ImageLoader = imageLoader ?? (path => path);
            Executor.LogSink = logSink;
            Executor.ProductTypeResolver = productResolver;
            Executor.FaceResultSink = faceResultSink;
        }

        public void Start()
        {
            Start(CancellationToken.None);
        }

        public void Start(CancellationToken cancellationToken)
        {
            if (Executor == null)
                throw new InvalidOperationException("SequenceHost not loaded");
            Executor.Start(cancellationToken);
        }

        public Task<string> TriggerAsync()
        {
            return TriggerAsync(null);
        }

        public Task<string> TriggerAsync(string triggerNodeId)
        {
            return TriggerAsync(triggerNodeId, new System.Collections.Generic.Dictionary<string, object>());
        }

        public Task<string> TriggerAsync(string triggerNodeId, System.Collections.Generic.Dictionary<string, object> triggerInfo)
        {
            return Executor.OnTriggerAsync(triggerNodeId, triggerInfo);
        }

        public string ResolveSimulateModbusTriggerNodeId(string triggerNodeId)
        {
            return Executor != null ? Executor.ResolveSimulateModbusTriggerNodeId(triggerNodeId) : triggerNodeId;
        }

        public void RequestStop()
        {
            if (Executor != null)
                Executor.RequestStop();
        }

        public Task WaitForIdleAsync()
        {
            return Executor != null ? Executor.WaitForIdleAsync() : Task.CompletedTask;
        }

        public Task WaitForIdleAsync(CancellationToken cancellationToken)
        {
            return Executor != null
                ? Executor.WaitForIdleAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public void Stop()
        {
            if (Executor != null)
                Executor.Stop();
        }
    }
}

