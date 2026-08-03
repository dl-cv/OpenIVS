using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DLCV.SequenceGraph;
using OpenCvSharp;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;

namespace DlcvSequenceTest
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Run(TestCanvasPositionArrayLoads);
            Run(TestCycleIsRejected);
            Run(TestOneToSixCameraGraphsValidate);
            Run(TestCSharpResultDrivesNgOverlay);
            RunAsync(TestPipelineOverlapsCaptureAndInferenceAsync);
            RunAsync(TestConcurrentTriggerIsRejectedAsync);
            Run(TestHardwareCameraNeedsInjectedFactory);

            Console.WriteLine("Passed={0} Failed={1}", _passed, _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void TestCanvasPositionArrayLoads()
        {
            const string json = "{\"version\":\"1.0\",\"id\":\"g\",\"name\":\"g\",\"resources\":{\"items\":[]},\"nodes\":[{\"id\":\"start\",\"type\":\"manual_trigger\",\"pos\":[85,80],\"ports\":{\"in\":[],\"out\":[]},\"props\":{}}],\"edges\":[],\"settings\":{}}";
            var graph = SequenceGraphLoader.FromJson(json);
            Assert(graph != null && graph.Nodes.Count == 1 && graph.Nodes[0].Pos is JArray,
                "画布坐标数组可以加载");
        }

        private static void TestCycleIsRejected()
        {
            var graph = BuildPipelineGraph(1);
            graph.Edges.Add(Control("cycle", "join", "wait_A"));
            var rejected = false;
            try { GraphValidator.Validate(graph); }
            catch (SequenceGraphValidationException) { rejected = true; }
            Assert(rejected, "控制流环路被拒绝");
        }

        private static void TestOneToSixCameraGraphsValidate()
        {
            for (var count = 1; count <= 6; count++)
                GraphValidator.Validate(BuildPipelineGraph(count));
            Assert(true, "1到6相机图全部通过校验");
        }

        private static void TestCSharpResultDrivesNgOverlay()
        {
            var detected = new Utils.CSharpObjectResult(
                1, "缺陷", 0.99f, 100,
                new List<double> { 10, 10, 20, 20 }, false, null,
                withBbox: true);
            var result = new Utils.CSharpResult(new List<Utils.CSharpSampleResult>
            {
                new Utils.CSharpSampleResult(new List<Utils.CSharpObjectResult> { detected })
            });
            using (var image = new Mat(80, 80, MatType.CV_8UC3, Scalar.Black))
            {
                Dictionary<string, object> okInfo;
                using (var visualized = ResultOverlayDrawer.Draw(image, result, new JObject(), out okInfo))
                {
                    Assert(!Convert.ToBoolean(okInfo["ok"]) && Convert.ToInt32(okInfo["det_count"]) == 1,
                        "CSharpResult有目标时画面判定NG");
                }
            }
        }

        private static async Task TestPipelineOverlapsCaptureAndInferenceAsync()
        {
            var cameraFactory = new TrackingCameraFactory();
            var flowRunner = new PipelineFlowRunner(cameraFactory);
            var display = new LockedDisplaySink();
            var modbus = new MockModbusClient();
            var host = new SequenceHost();
            host.LoadWithoutStart(BuildPipelineGraph(3), flowRunner, display, modbus, path => path);
            host.Executor.CameraFactory = cameraFactory;
            host.Start();
            try
            {
                var triggerInfo = new Dictionary<string, object>
                {
                    { "source", "simulation" },
                    { "photo_reg", 500 }
                };
                var triggerId = await host.TriggerAsync("trigger", triggerInfo);
                Assert(!string.IsNullOrEmpty(triggerId), "流水线触发完成");
                Assert(flowRunner.AObservedBWait, "A推理期间B已经开始等待图像");
                Assert(flowRunner.BObservedCWait, "B推理期间C已经开始等待图像");
                Assert(display.UpdateCount == 3, "三个相机画面均已更新");
                Assert(modbus.WriteHistory.Count == 1 &&
                    modbus.WriteHistory[0].Address == 500 &&
                    modbus.WriteHistory[0].Value == 0,
                    "最后取图后PLC拍照寄存器清零");
            }
            finally
            {
                host.Stop();
            }
        }

        private static async Task TestConcurrentTriggerIsRejectedAsync()
        {
            var cameraFactory = new TrackingCameraFactory();
            var runner = new BlockingFlowRunner();
            var host = new SequenceHost();
            host.LoadWithoutStart(BuildPipelineGraph(1), runner, new LockedDisplaySink(), new MockModbusClient(), path => path);
            host.Executor.CameraFactory = cameraFactory;
            host.Start();
            try
            {
                var info = new Dictionary<string, object> { { "photo_reg", 500 } };
                var first = host.TriggerAsync("trigger", info);
                Assert(runner.Started.Wait(2000), "首个触发进入推理");
                var second = await host.TriggerAsync("trigger", info);
                Assert(second == null, "运行中拒绝并发触发");
                runner.Release.Set();
                await first;
            }
            finally
            {
                runner.Release.Set();
                await host.WaitForIdleAsync();
                host.Stop();
            }
        }

        private static void TestHardwareCameraNeedsInjectedFactory()
        {
            var graph = BuildPipelineGraph(1);
            graph.Resources.Items.First(x => x.Type == "camera").Config["mode"] = "hik";
            var host = new SequenceHost();
            host.LoadWithoutStart(graph, new MockAiFlowRunner(), new LockedDisplaySink(), new MockModbusClient(), path => path);
            var rejected = false;
            try { host.Start(); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "硬件相机必须由应用层注入工厂");
        }

        private static SequenceGraphDocument BuildPipelineGraph(int cameraCount)
        {
            var graph = new SequenceGraphDocument
            {
                Id = "pipeline-" + cameraCount,
                Name = "pipeline-" + cameraCount
            };
            graph.Nodes.Add(Node("trigger", "manual_trigger"));
            var faces = Enumerable.Range(0, cameraCount).Select(i => ((char)('A' + i)).ToString()).ToList();

            foreach (var face in faces)
            {
                graph.Resources.Items.Add(new ResourceItem
                {
                    Id = "CAM_" + face,
                    Type = "camera",
                    Config = new JObject { { "mode", "virtual" }, { "hw_id", face + ".png" } }
                });
                graph.Resources.Items.Add(new ResourceItem
                {
                    Id = "FLOW_" + face,
                    Type = "flow",
                    Config = new JObject { { "path", face + ".flow" }, { "face", face } }
                });
                graph.Resources.Items.Add(new ResourceItem
                {
                    Id = "WIN_" + face,
                    Type = "window",
                    Config = new JObject()
                });
                graph.Nodes.Add(Node("wait_" + face, "wait_camera_image",
                    new JObject { { "camera_id", "CAM_" + face } }));
                graph.Nodes.Add(Node("run_" + face, "run_flow",
                    new JObject { { "flow_id", "FLOW_" + face }, { "face", face } }));
                graph.Nodes.Add(Node("display_" + face, "update_display",
                    new JObject { { "window_id", "WIN_" + face }, { "draw_mode", "none" } }));
            }

            graph.Nodes.Add(Node("plc_clear", "modbus_write_register",
                new JObject
                {
                    { "address_from_trigger_info", true },
                    { "address_key", "photo_reg" },
                    { "value", 0 }
                }));
            graph.Nodes.Add(Node("join", "wait_all"));
            graph.Edges.Add(Control("start", "trigger", "wait_" + faces[0]));

            for (var i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                graph.Edges.Add(Control("wait-run-" + face, "wait_" + face, "run_" + face));
                graph.Edges.Add(Control("run-display-" + face, "run_" + face, "display_" + face));
                graph.Edges.Add(Control("display-join-" + face, "display_" + face, "join"));
                graph.Edges.Add(Data("image-run-" + face, "wait_" + face, "out-1", "run_" + face, "in-1"));
                graph.Edges.Add(Data("image-display-" + face, "wait_" + face, "out-1", "display_" + face, "in-1"));
                graph.Edges.Add(Data("result-display-" + face, "run_" + face, "out-2", "display_" + face, "in-2"));
                if (i + 1 < faces.Count)
                    graph.Edges.Add(Control("wait-next-" + face, "wait_" + face, "wait_" + faces[i + 1]));
                else
                    graph.Edges.Add(Control("wait-clear", "wait_" + face, "plc_clear"));
            }
            graph.Edges.Add(Control("clear-join", "plc_clear", "join"));
            AddPorts(graph);
            return graph;
        }

        private static SequenceNodeInstance Node(string id, string type, JObject props = null)
        {
            return new SequenceNodeInstance
            {
                Id = id,
                Type = type,
                Label = id,
                Props = props ?? new JObject()
            };
        }

        private static SequenceEdge Control(string id, string source, string target)
        {
            return new SequenceEdge
            {
                Id = id,
                Source = source,
                SourceHandle = "out-0",
                Target = target,
                TargetHandle = "in-0",
                Type = "control"
            };
        }

        private static SequenceEdge Data(string id, string source, string sourceHandle, string target, string targetHandle)
        {
            return new SequenceEdge
            {
                Id = id,
                Source = source,
                SourceHandle = sourceHandle,
                Target = target,
                TargetHandle = targetHandle,
                Type = "data"
            };
        }

        private static void AddPorts(SequenceGraphDocument graph)
        {
            foreach (var node in graph.Nodes)
            {
                node.Ports = new NodePorts
                {
                    In = node.Id == "trigger"
                        ? new List<PortDef>()
                        : new List<PortDef> { Port("in-0", "control") },
                    Out = new List<PortDef> { Port("out-0", "control") }
                };
                if (node.Type == "wait_camera_image")
                    node.Ports.Out.Add(Port("out-1", "data"));
                if (node.Type == "run_flow")
                {
                    node.Ports.In.Add(Port("in-1", "data"));
                    node.Ports.Out.Add(Port("out-2", "data"));
                }
                if (node.Type == "update_display")
                {
                    node.Ports.In.Add(Port("in-1", "data"));
                    node.Ports.In.Add(Port("in-2", "data"));
                }
            }
        }

        private static PortDef Port(string id, string type)
        {
            return new PortDef { Id = id, Type = type, DataType = type };
        }

        private static void Run(Action test)
        {
            try { test(); }
            catch (Exception ex) { Assert(false, test.Method.Name + ": " + ex); }
        }

        private static void RunAsync(Func<Task> test)
        {
            try { test().GetAwaiter().GetResult(); }
            catch (Exception ex) { Assert(false, test.Method.Name + ": " + ex); }
        }

        private static void Assert(bool condition, string name)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name);
            }
        }

        private sealed class TrackingCameraFactory : ICameraResourceFactory
        {
            public ManualResetEventSlim BWaitStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim CWaitStarted { get; } = new ManualResetEventSlim(false);

            public ICameraHandle Create(ResourceItem resource, Func<string, object> imageLoader)
            {
                var face = resource.Id.Substring(resource.Id.Length - 1);
                return new TrackingCameraHandle(face, this);
            }

            public void Clear()
            {
            }
        }

        private sealed class TrackingCameraHandle : ICameraHandle
        {
            private readonly string _face;
            private readonly TrackingCameraFactory _owner;

            public TrackingCameraHandle(string face, TrackingCameraFactory owner)
            {
                _face = face;
                _owner = owner;
            }

            public object GetFrame() { return GetFrame(5000); }

            public object GetFrame(int timeoutMs)
            {
                if (_face == "B") _owner.BWaitStarted.Set();
                if (_face == "C") _owner.CWaitStarted.Set();
                return "Frame-" + _face;
            }

            public void TriggerOnce() { }
            public void Release() { }
        }

        private sealed class PipelineFlowRunner : IAiFlowRunner
        {
            private readonly TrackingCameraFactory _cameras;
            public bool AObservedBWait { get; private set; }
            public bool BObservedCWait { get; private set; }

            public PipelineFlowRunner(TrackingCameraFactory cameras)
            {
                _cameras = cameras;
            }

            public object Run(string path, object image, string productType, string barcode)
            {
                return Run(path, image, productType, barcode, null, null);
            }

            public object Run(string path, object image, string productType, string barcode, string resultSourceNodeId)
            {
                return Run(path, image, productType, barcode, resultSourceNodeId, null);
            }

            public object Run(string path, object image, string productType, string barcode, string resultSourceNodeId, string face)
            {
                if (face == "A") AObservedBWait = _cameras.BWaitStarted.Wait(2000);
                if (face == "B") BObservedCWait = _cameras.CWaitStarted.Wait(2000);
                return new Dictionary<string, object> { { "ok", true }, { "face", face } };
            }

            public IFlowHandle AcquireFlow(string path) { return AcquireFlow(path, null); }
            public IFlowHandle AcquireFlow(string path, string face) { return new FlowHandle(path, null); }
        }

        private sealed class BlockingFlowRunner : IAiFlowRunner
        {
            public ManualResetEventSlim Started { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim Release { get; } = new ManualResetEventSlim(false);

            public object Run(string path, object image, string productType, string barcode)
            {
                return Run(path, image, productType, barcode, null, null);
            }

            public object Run(string path, object image, string productType, string barcode, string resultSourceNodeId)
            {
                return Run(path, image, productType, barcode, resultSourceNodeId, null);
            }

            public object Run(string path, object image, string productType, string barcode, string resultSourceNodeId, string face)
            {
                Started.Set();
                if (!Release.Wait(5000)) throw new TimeoutException("test inference release timeout");
                return new Dictionary<string, object> { { "ok", true } };
            }

            public IFlowHandle AcquireFlow(string path) { return AcquireFlow(path, null); }
            public IFlowHandle AcquireFlow(string path, string face) { return new FlowHandle(path, null); }
        }

        private sealed class LockedDisplaySink : IDisplaySink
        {
            private int _updateCount;
            public int UpdateCount { get { return Volatile.Read(ref _updateCount); } }

            public void Update(string windowId, object image, object result)
            {
                Interlocked.Increment(ref _updateCount);
            }
        }
    }
}
