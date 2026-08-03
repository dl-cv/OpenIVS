using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DLCV.SequenceGraph.Nodes
{
    public class WaitCameraImageNode : SequenceNodeBase
    {
        public override string Type { get { return "wait_camera_image"; } }

        public override async Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            return await Task.Run(() =>
            {
                object image = null;
                if (ctx.TriggerInfo != null && ctx.TriggerInfo.ContainsKey("image"))
                    image = ctx.TriggerInfo["image"];
                if (image == null)
                {
                    var cameraId = node.Props != null && node.Props["camera_id"] != null ? node.Props["camera_id"].ToString() : null;
                    if (string.IsNullOrEmpty(cameraId))
                        throw new InvalidOperationException("wait_camera_image missing camera_id");
                    var handle = executor.GetResource(cameraId) as ICameraHandle;
                    if (handle == null)
                        throw new InvalidOperationException("camera resource unavailable: " + cameraId);
                    int timeoutMs = 5000;
                    if (node.Props != null && node.Props["frame_timeout_ms"] != null)
                        int.TryParse(node.Props["frame_timeout_ms"].ToString(), out timeoutMs);
                    image = handle.GetFrame(timeoutMs);
                }
                return image;
            });
        }
    }

    public class CameraSoftTriggerNode : SequenceNodeBase
    {
        public override string Type { get { return "camera_soft_trigger"; } }

        public override Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            var cameraId = node.Props != null && node.Props["camera_id"] != null ? node.Props["camera_id"].ToString() : null;
            if (string.IsNullOrEmpty(cameraId))
                throw new InvalidOperationException("camera_soft_trigger missing camera_id");
            var handle = executor.GetResource(cameraId) as ICameraHandle;
            if (handle == null)
                throw new InvalidOperationException("camera resource unavailable: " + cameraId);
            handle.TriggerOnce();
            return Task.FromResult<object>(null);
        }
    }

    public class RunFlowNode : SequenceNodeBase
    {
        public override string Type { get { return "run_flow"; } }

        public override async Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            return await Task.Run(() =>
            {
                var flowId = node.Props != null && node.Props["flow_id"] != null ? node.Props["flow_id"].ToString() : null;
                if (string.IsNullOrEmpty(flowId))
                    throw new InvalidOperationException("run_flow missing flow_id");
                var flow = executor.FindResource("flow", flowId);
                if (flow == null)
                    throw new InvalidOperationException("run_flow flow_id not found: " + flowId);
                var path = flow.Config != null && flow.Config["path"] != null ? flow.Config["path"].ToString() : flowId;
                var image = executor.GetDataInput(ctx, node.Id, "in-1");
                if (executor.AiFlowRunner == null)
                    throw new InvalidOperationException("IAiFlowRunner not injected");
                string productType = "";
                string barcode = "";
                if (ctx.TriggerInfo != null)
                {
                    if (ctx.TriggerInfo.ContainsKey("product_type") && ctx.TriggerInfo["product_type"] != null)
                        productType = ctx.TriggerInfo["product_type"].ToString();
                    if (ctx.TriggerInfo.ContainsKey("barcode") && ctx.TriggerInfo["barcode"] != null)
                        barcode = ctx.TriggerInfo["barcode"].ToString();
                }
                string resultSource = null;
                if (node.Props != null && node.Props["result_source_node_id"] != null)
                {
                    var raw = node.Props["result_source_node_id"].ToString();
                    if (!string.IsNullOrWhiteSpace(raw))
                        resultSource = raw.Trim();
                }
                var face = ResolveFace(node, flow);
                return executor.AiFlowRunner.Run(path, image, productType, barcode, resultSource, face);
            });
        }

        public static string ResolveFace(SequenceNodeInstance node, ResourceItem flow)
        {
            if (node != null && node.Props != null && node.Props["face"] != null)
            {
                var fromNode = node.Props["face"].ToString();
                if (!string.IsNullOrWhiteSpace(fromNode))
                    return fromNode.Trim().ToUpperInvariant();
            }
            if (flow != null && flow.Config != null && flow.Config["face"] != null)
            {
                var fromRes = flow.Config["face"].ToString();
                if (!string.IsNullOrWhiteSpace(fromRes))
                    return fromRes.Trim().ToUpperInvariant();
            }
            return "B";
        }
    }

    public class ResolveProductNode : SequenceNodeBase
    {
        public override string Type { get { return "resolve_product"; } }

        public override Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            if (ctx.TriggerInfo == null)
                ctx.TriggerInfo = new Dictionary<string, object>();

            var barcode = "";
            var hasTriggerBarcode = ctx.TriggerInfo != null && ctx.TriggerInfo.ContainsKey("barcode");
            if (hasTriggerBarcode)
            {
                barcode = ctx.TriggerInfo["barcode"] != null ? (ctx.TriggerInfo["barcode"].ToString() ?? "") : "";
            }
            else if (node.Props != null && node.Props["test_barcode"] != null)
            {
                var test = node.Props["test_barcode"].ToString();
                if (!string.IsNullOrWhiteSpace(test))
                    barcode = test.Trim();
            }

            ctx.TriggerInfo["barcode"] = barcode;

            string productType = "Unknown";
            if (node.Props != null && node.Props["fallback_product_type"] != null)
            {
                var fb = node.Props["fallback_product_type"].ToString();
                if (!string.IsNullOrWhiteSpace(fb))
                    productType = fb.Trim();
            }

            if (executor.ProductTypeResolver != null && !string.IsNullOrEmpty(barcode))
            {
                try
                {
                    var resolvedProductType = executor.ProductTypeResolver.Resolve(barcode);
                    if (string.IsNullOrWhiteSpace(resolvedProductType) ||
                        string.Equals(resolvedProductType.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("未找到条码对应型号");
                    productType = resolvedProductType.Trim();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("型号解析失败: " + ex.Message, ex);
                }
            }
            else if (string.IsNullOrEmpty(barcode))
            {
                productType = "Unknown";
            }

            ctx.TriggerInfo["product_type"] = productType;
            ctx.Log("info", node.Id, "barcode=" + barcode + " product_type=" + productType);
            return Task.FromResult<object>(new Dictionary<string, object>
            {
                { "barcode", barcode },
                { "product_type", productType }
            });
        }
    }

    public class UpdateDisplayNode : SequenceNodeBase
    {
        public override string Type { get { return "update_display"; } }

        public override Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            var windowId = node.Props != null && node.Props["window_id"] != null ? node.Props["window_id"].ToString() : null;
            var image = executor.GetDataInput(ctx, node.Id, "in-1");
            var result = executor.GetDataInput(ctx, node.Id, "in-2");
            Dictionary<string, object> okInfo;
            var vis = ResultOverlayDrawer.Draw(image, result, node.Props, out okInfo);
            if (executor.DisplaySink != null)
                executor.DisplaySink.Update(windowId, vis, result);
            var payload = new JObject();
            payload["ok"] = okInfo != null && okInfo.ContainsKey("ok") && Convert.ToBoolean(okInfo["ok"]);
            payload["reason"] = okInfo != null && okInfo.ContainsKey("reason") ? (okInfo["reason"] ?? "").ToString() : "";
            payload["det_count"] = okInfo != null && okInfo.ContainsKey("det_count") ? Convert.ToInt32(okInfo["det_count"]) : 0;
            payload["window_id"] = windowId;
            return Task.FromResult<object>(payload);
        }
    }

    public class PublishFaceResultNode : SequenceNodeBase
    {
        public override string Type { get { return "publish_face_result"; } }

        public override Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            var windowId = node.Props != null && node.Props["window_id"] != null ? node.Props["window_id"].ToString() : null;
            if (string.IsNullOrWhiteSpace(windowId))
                throw new InvalidOperationException("publish_face_result missing window_id");
            var image = executor.GetDataInput(ctx, node.Id, "in-1");
            var result = executor.GetDataInput(ctx, node.Id, "in-2");
            if (executor.FaceResultSink != null)
                executor.FaceResultSink.Publish(windowId, image, result);
            else
                executor.EmitLog("warn", node.Id, "IFaceResultSink not injected");
            return Task.FromResult<object>(new Dictionary<string, object>
            {
                { "window_id", windowId },
                { "published", executor.FaceResultSink != null }
            });
        }
    }

    public class WaitAllNode : SequenceNodeBase
    {
        public override string Type { get { return "wait_all"; } }

        public override Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            return Task.FromResult<object>(null);
        }
    }

    public class WaitAnyNode : SequenceNodeBase
    {
        public override string Type { get { return "wait_any"; } }

        public override Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            return Task.FromResult<object>(null);
        }
    }

    public class ManualTriggerNode : SequenceNodeBase
    {
        public override string Type { get { return "manual_trigger"; } }

        public override Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            return Task.FromResult<object>(ctx.TriggerInfo);
        }
    }

    public class NullOutputNode : SequenceNodeBase
    {
        public override string Type { get { return "null_output"; } }

        public override Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            return Task.FromResult<object>(null);
        }
    }

    public class StoreJsonNode : SequenceNodeBase
    {
        public override string Type { get { return "store_json"; } }

        public override Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            var resultKey = node.Props != null && node.Props["result_key"] != null ? node.Props["result_key"].ToString() : null;
            var pathTemplate = node.Props != null && node.Props["path_template"] != null
                ? node.Props["path_template"].ToString()
                : "results/{date}/{trigger_id}.json";
            if (string.IsNullOrEmpty(resultKey))
                throw new InvalidOperationException("store_json missing result_key");
            var data = ctx.GetResult(resultKey);
            if (data == null)
                throw new InvalidOperationException("store_json result_key not found: " + resultKey);
            var now = DateTime.Now;
            var path = pathTemplate
                .Replace("{date}", now.ToString("yyyyMMdd"))
                .Replace("{time}", now.ToString("HHmmss"))
                .Replace("{trigger_id}", ctx.TriggerId ?? "")
                .Replace("{run_id}", ctx.RunId ?? "");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented), Encoding.UTF8);
            ctx.Log("info", node.Id, "store json: " + path);
            return Task.FromResult<object>(path);
        }
    }

    public class ModbusTcpInputNode : SequenceNodeBase
    {
        public override string Type { get { return "modbus_tcp_input"; } }

        public override Task ArmAsync(SequenceGraphExecutor executor, string nodeId)
        {
            int controlIn;
            executor.ControlInCount.TryGetValue(nodeId, out controlIn);
            if (controlIn > 0)
            {
                executor.EmitLog("info", nodeId, "gated mode: wait control then photo signal");
                return Task.CompletedTask;
            }

            if (executor != null && !executor.ModbusEnabled)
            {
                executor.EmitLog("info", nodeId,
                    "Modbus disabled; waiting for injected trigger only");
                return Task.CompletedTask;
            }

            SequenceNodeInstance node;
            if (!executor.TryGetNode(nodeId, out node) || node == null)
                return Task.CompletedTask;

            var host = GetString(node, "host", "127.0.0.1");
            var port = GetInt(node, "port", 502);
            var deviceId = (byte)GetInt(node, "device_id", 1);
            var photoReg = GetInt(node, "photo_reg", 4111);
            if (executor.ModbusClient == null)
                throw new InvalidOperationException("IModbusClient not injected");
            if (!executor.ModbusClient.Connect(host, port, deviceId))
                throw new InvalidOperationException("modbus connect failed: " + host + ":" + port);
            var cts = new CancellationTokenSource();
            var task = Task.Run(() => PollLoop(executor, nodeId, node, cts.Token));
            executor.RegisterTriggerSource(nodeId, new ModbusPollHandle(cts, task));
            executor.EmitLog("info", nodeId,
                "start polling Modbus photo " + host + ":" + port + " reg=" + photoReg);
            return Task.CompletedTask;
        }

        public override Task<object> ExecuteAsync(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            int controlIn;
            executor.ControlInCount.TryGetValue(node.Id, out controlIn);
            if (controlIn > 0)
            {
                if (!executor.ModbusEnabled)
                {
                    var timeoutMs = GetInt(node, "timeout_ms", 5000);
                    return executor.WaitForInjectedGatedSignalAsync(node.Id, ctx, timeoutMs);
                }
                return Task.FromResult<object>(WaitPhotoSignal(node, ctx, executor));
            }

            var src = ctx.TriggerInfo != null && ctx.TriggerInfo.ContainsKey("source")
                ? (ctx.TriggerInfo["source"] ?? "").ToString()
                : "";
            var isModbusSignal = string.Equals(src, "modbus", StringComparison.Ordinal);
            var isInjectedSignal = !executor.ModbusEnabled &&
                string.Equals(src, "simulation", StringComparison.Ordinal);
            if (!isModbusSignal && !isInjectedSignal)
                throw new InvalidOperationException(
                    "modbus_tcp_input '" + node.Id + "' refuses soft-trigger (source=" + src + ")");

            if (ctx.TriggerInfo == null || ctx.TriggerInfo.Count == 0)
                return Task.FromResult<object>(new Dictionary<string, object> { { "source", "modbus" } });
            return Task.FromResult<object>(new Dictionary<string, object>(ctx.TriggerInfo));
        }

        private static void PollLoop(SequenceGraphExecutor executor, string nodeId, SequenceNodeInstance node, CancellationToken token)
        {
            var client = executor.ModbusClient;
            if (client == null) return;

            var host = GetString(node, "host", "127.0.0.1");
            var port = GetInt(node, "port", 502);
            var deviceId = (byte)GetInt(node, "device_id", 1);
            var photoReg = (ushort)GetInt(node, "photo_reg", 4111);
            var photoValue = (ushort)GetInt(node, "photo_value", 1);
            var barcodeReg = (ushort)GetInt(node, "barcode_reg", 4116);
            var barcodeCount = (ushort)GetInt(node, "barcode_count", 10);
            var pollMs = GetInt(node, "poll_interval_ms", 200);
            if (pollMs < 10) pollMs = 10;
            var edgeTracker = new RisingEdgeTracker();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!client.Connect(host, port, deviceId))
                    {
                        if (token.WaitHandle.WaitOne(pollMs))
                            break;
                        continue;
                    }
                    var photo = client.ReadHoldingRegister(photoReg);
                    if (edgeTracker.Sample(photo, photoValue))
                    {
                        var barcode = "";
                        if (barcodeCount > 0)
                        {
                            try { barcode = DecodeBarcode(client.ReadHoldingRegisters(barcodeReg, barcodeCount)); }
                            catch (Exception ex)
                            {
                                executor.EmitLog("error", nodeId,
                                    "read Modbus barcode failed: " + ex.Message);
                            }
                        }
                        var info = new Dictionary<string, object>
                        {
                            { "source", "modbus" },
                            { "photo", true },
                            { "barcode", barcode },
                            { "photo_reg", photoReg }
                        };
                        var accepted = executor.TryTriggerFromArmAsync(nodeId, info).GetAwaiter().GetResult();
                        if (!accepted)
                        {
                            executor.EmitLog("warn", nodeId,
                                "photo rising-edge not accepted; signal will be retried");
                            continue;
                        }
                        executor.EmitLog("info", nodeId,
                            "photo rising-edge accepted barcode=" + barcode + "; final result is written after inference");
                    }
                }
                catch (Exception ex)
                {
                    edgeTracker.Reset();
                    executor.EmitLog(token.IsCancellationRequested ? "warn" : "error", nodeId,
                        "Modbus poll failed: " + ex.Message);
                    try
                    {
                        client.Close();
                    }
                    catch (Exception closeError)
                    {
                        executor.EmitLog("error", nodeId,
                            "close Modbus client after poll failure failed: " + closeError.Message);
                    }
                }
                if (token.IsCancellationRequested) break;
                if (token.WaitHandle.WaitOne(pollMs))
                    break;
            }
        }

        private static Dictionary<string, object> WaitPhotoSignal(SequenceNodeInstance node, SequenceContext ctx, SequenceGraphExecutor executor)
        {
            var client = executor.ModbusClient;
            if (client == null)
                throw new InvalidOperationException("IModbusClient not injected");

            var host = GetString(node, "host", "127.0.0.1");
            var port = GetInt(node, "port", 502);
            var deviceId = (byte)GetInt(node, "device_id", 1);
            var photoReg = (ushort)GetInt(node, "photo_reg", 4111);
            var photoValue = (ushort)GetInt(node, "photo_value", 1);
            var barcodeReg = (ushort)GetInt(node, "barcode_reg", 4116);
            var barcodeCount = (ushort)GetInt(node, "barcode_count", 10);
            var timeoutMs = GetInt(node, "timeout_ms", 5000);
            var pollMs = GetInt(node, "poll_interval_ms", 50);

            if (!client.Connect(host, port, deviceId))
                throw new InvalidOperationException("modbus connect failed");

            var sw = Stopwatch.StartNew();
            var readyForRisingEdge = false;
            while (!ctx.Cancelled)
            {
                var photo = client.ReadHoldingRegister(photoReg);
                if (photo != photoValue)
                    readyForRisingEdge = true;
                else if (readyForRisingEdge)
                    break;
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException("modbus photo signal timeout");
                var remainingPollMs = pollMs;
                while (remainingPollMs > 0 && !ctx.Cancelled)
                {
                    var waitMs = Math.Min(remainingPollMs, 50);
                    Thread.Sleep(waitMs);
                    remainingPollMs -= waitMs;
                }
            }

            if (ctx.Cancelled)
                throw new OperationCanceledException("modbus wait cancelled");

            var info = new Dictionary<string, object>
            {
                { "source", "modbus" },
                { "photo", photoValue },
                { "photo_reg", photoReg }
            };
            if (barcodeCount > 0)
            {
                var barcode = "";
                try { barcode = DecodeBarcode(client.ReadHoldingRegisters(barcodeReg, barcodeCount)); }
                catch { }
                info["barcode"] = barcode;
            }
            foreach (var kv in info)
                ctx.TriggerInfo[kv.Key] = kv.Value;
            return info;
        }

        private class ModbusPollHandle : ITriggerSourceHandle
        {
            private readonly CancellationTokenSource _cts;
            private readonly Task _task;
            private readonly object _sync = new object();
            private bool _disarmed;

            public ModbusPollHandle(CancellationTokenSource cts, Task task)
            {
                _cts = cts;
                _task = task;
            }

            public void Disarm()
            {
                lock (_sync)
                {
                    if (_disarmed)
                        return;

                    _cts.Cancel();
                    var completed = false;
                    try
                    {
                        completed = _task.Wait(2000);
                    }
                    catch (AggregateException ex)
                    {
                        if (!_task.IsCanceled)
                        {
                            throw new AggregateException(
                                "Modbus poll task failed while disarming",
                                ex.Flatten().InnerExceptions);
                        }
                        completed = true;
                    }

                    if (!completed)
                        throw new TimeoutException("Modbus poll task did not exit within 2 seconds");
                    if (_task.IsFaulted)
                    {
                        throw new AggregateException(
                            "Modbus poll task failed while disarming",
                            _task.Exception.Flatten().InnerExceptions);
                    }

                    _cts.Dispose();
                    _disarmed = true;
                }
            }
        }

        private static string DecodeBarcode(ushort[] regs)
        {
            if (regs == null || regs.Length == 0)
                return "";
            var chars = new List<char>();
            foreach (var r in regs)
            {
                var hi = (char)((r >> 8) & 0xFF);
                var lo = (char)(r & 0xFF);
                if (hi != 0) chars.Add(hi);
                if (lo != 0) chars.Add(lo);
            }
            return new string(chars.ToArray()).Trim();
        }

        private static string GetString(SequenceNodeInstance node, string key, string def)
        {
            if (node.Props == null || node.Props[key] == null) return def;
            return node.Props[key].ToString();
        }

        private static int GetInt(SequenceNodeInstance node, string key, int def)
        {
            if (node.Props == null || node.Props[key] == null) return def;
            int v;
            return int.TryParse(node.Props[key].ToString(), out v) ? v : def;
        }
    }

    public class ModbusWriteRegisterNode : SequenceNodeBase
    {
        public override string Type { get { return "modbus_write_register"; } }

        public override Task<object> ExecuteAsync(
            SequenceNodeInstance node,
            SequenceContext ctx,
            SequenceGraphExecutor executor)
        {
            if (!executor.ModbusEnabled)
            {
                ctx.Log("info", node.Id, "Modbus disabled; register write skipped");
                return Task.FromResult<object>(new Dictionary<string, object>
                {
                    { "skipped", true }
                });
            }

            var client = executor.ModbusClient;
            if (client == null)
                throw new InvalidOperationException("IModbusClient not injected");
            var host = GetString(node, "host", "127.0.0.1");
            var port = GetInt(node, "port", 502);
            var deviceId = (byte)GetInt(node, "device_id", 1);
            var address = ResolveAddress(node, ctx);
            var value = (ushort)GetInt(node, "value", 0);
            if (!client.Connect(host, port, deviceId))
                throw new InvalidOperationException("modbus connect failed: " + host + ":" + port);
            client.WriteSingleRegister(address, value);
            ctx.Log("info", node.Id, "write Modbus register " + address + "=" + value);
            return Task.FromResult<object>(new Dictionary<string, object>
            {
                { "address", address },
                { "value", value }
            });
        }

        private static ushort ResolveAddress(SequenceNodeInstance node, SequenceContext ctx)
        {
            var fromTrigger = GetBool(node, "address_from_trigger_info", false);
            if (!fromTrigger)
                return (ushort)GetInt(node, "address", 0);

            var key = GetString(node, "address_key", "photo_reg");
            object raw;
            ushort address;
            if (ctx.TriggerInfo == null || !ctx.TriggerInfo.TryGetValue(key, out raw) || raw == null ||
                !ushort.TryParse(raw.ToString(), out address))
                throw new InvalidOperationException("trigger_info 缺少有效寄存器地址: " + key);
            return address;
        }

        private static string GetString(SequenceNodeInstance node, string key, string def)
        {
            return node.Props != null && node.Props[key] != null ? node.Props[key].ToString() : def;
        }

        private static int GetInt(SequenceNodeInstance node, string key, int def)
        {
            int value;
            return node.Props != null && node.Props[key] != null &&
                int.TryParse(node.Props[key].ToString(), out value) ? value : def;
        }

        private static bool GetBool(SequenceNodeInstance node, string key, bool def)
        {
            bool value;
            return node.Props != null && node.Props[key] != null &&
                bool.TryParse(node.Props[key].ToString(), out value) ? value : def;
        }
    }

    public static class DefaultNodeFactory
    {
        public static Dictionary<string, ISequenceNode> CreateDefault()
        {
            return new Dictionary<string, ISequenceNode>
            {
                { "wait_camera_image", new WaitCameraImageNode() },
                { "camera_soft_trigger", new CameraSoftTriggerNode() },
                { "run_flow", new RunFlowNode() },
                { "update_display", new UpdateDisplayNode() },
                { "publish_face_result", new PublishFaceResultNode() },
                { "wait_all", new WaitAllNode() },
                { "wait_any", new WaitAnyNode() },
                { "manual_trigger", new ManualTriggerNode() },
                { "null_output", new NullOutputNode() },
                { "store_json", new StoreJsonNode() },
                { "modbus_tcp_input", new ModbusTcpInputNode() },
                { "modbus_write_register", new ModbusWriteRegisterNode() },
                { "resolve_product", new ResolveProductNode() }
            };
        }
    }
}
