using System;
using System.Collections.Generic;
using DLCV.SequenceGraph;
using Newtonsoft.Json.Linq;
using OpenIVS2.Models;

namespace OpenIVS2.Services
{
    public static class SequenceGraphBuilder
    {
        public static SequenceGraphDocument Build(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            var cameras = settings.EnabledCameras();
            if (cameras.Count < 1 || cameras.Count > 6)
                throw new InvalidOperationException("启用相机数量必须为 1 到 6 台");

            var graph = new SequenceGraphDocument
            {
                Id = "openivs2-production",
                Name = "OpenIVS 2026 生产检测时序"
            };
            var tcpPlc = settings.UsePlc && string.Equals(settings.PlcMode, "tcp", StringComparison.OrdinalIgnoreCase);
            graph.Nodes.Add(tcpPlc
                ? Node("trigger", "modbus_tcp_input", new JObject
                {
                    { "host", settings.TcpHost },
                    { "port", settings.TcpPort },
                    { "device_id", settings.DeviceId },
                    { "photo_reg", settings.PhotoRegister },
                    { "photo_value", settings.TriggerValue },
                    { "barcode_count", 0 },
                    { "poll_interval_ms", settings.PollIntervalMs }
                })
                : Node("trigger", "manual_trigger"));

            foreach (var camera in cameras)
            {
                var slot = camera.Slot.ToUpperInvariant();
                graph.Resources.Items.Add(new ResourceItem
                {
                    Id = "CAM_" + slot,
                    Name = camera.Name,
                    Type = "camera",
                    Config = new JObject
                    {
                        { "mode", camera.Mode },
                        { "hw_id", string.Equals(camera.Mode, "virtual", StringComparison.OrdinalIgnoreCase) ? camera.VirtualImagePath : camera.CameraId },
                        { "rotation", camera.Rotation },
                        { "software_trigger", camera.SoftwareTrigger }
                    }
                });
                graph.Resources.Items.Add(new ResourceItem
                {
                    Id = "FLOW_" + slot,
                    Name = camera.Name + "模型",
                    Type = "flow",
                    Config = new JObject
                    {
                        { "path", camera.ModelPath },
                        { "face", slot },
                        { "device_id", camera.DeviceId }
                    }
                });
                graph.Resources.Items.Add(new ResourceItem
                {
                    Id = "WIN_" + slot,
                    Name = camera.Name + "画面",
                    Type = "window",
                    Config = new JObject()
                });
                graph.Nodes.Add(Node("wait_" + slot, "wait_camera_image", new JObject
                {
                    { "camera_id", "CAM_" + slot },
                    { "frame_timeout_ms", camera.FrameTimeoutMs }
                }));
                graph.Nodes.Add(Node("run_" + slot, "run_flow", new JObject
                {
                    { "flow_id", "FLOW_" + slot },
                    { "face", slot }
                }));
                graph.Nodes.Add(Node("display_" + slot, "update_display", new JObject
                {
                    { "window_id", "WIN_" + slot },
                    { "draw_mode", "overlay" },
                    { "display_bbox", true },
                    { "display_text", true },
                    { "display_score", true },
                    { "display_ok", true }
                }));
            }

            graph.Nodes.Add(Node("plc_clear", "modbus_write_register", new JObject
            {
                { "address_from_trigger_info", true },
                { "address_key", "photo_reg" },
                { "value", settings.ClearValue },
                { "host", settings.TcpHost },
                { "port", settings.TcpPort },
                { "device_id", settings.DeviceId }
            }));
            graph.Nodes.Add(Node("join", "wait_all"));
            graph.Edges.Add(Control("start", "trigger", "wait_" + cameras[0].Slot.ToUpperInvariant()));

            for (var i = 0; i < cameras.Count; i++)
            {
                var slot = cameras[i].Slot.ToUpperInvariant();
                graph.Edges.Add(Control("wait-run-" + slot, "wait_" + slot, "run_" + slot));
                graph.Edges.Add(Control("run-display-" + slot, "run_" + slot, "display_" + slot));
                graph.Edges.Add(Control("display-join-" + slot, "display_" + slot, "join"));
                graph.Edges.Add(Data("image-run-" + slot, "wait_" + slot, "out-1", "run_" + slot, "in-1"));
                graph.Edges.Add(Data("image-display-" + slot, "wait_" + slot, "out-1", "display_" + slot, "in-1"));
                graph.Edges.Add(Data("result-display-" + slot, "run_" + slot, "out-2", "display_" + slot, "in-2"));
                if (i + 1 < cameras.Count)
                    graph.Edges.Add(Control("wait-next-" + slot, "wait_" + slot, "wait_" + cameras[i + 1].Slot.ToUpperInvariant()));
                else
                    graph.Edges.Add(Control("wait-clear", "wait_" + slot, "plc_clear"));
            }
            graph.Edges.Add(Control("clear-join", "plc_clear", "join"));
            AddPorts(graph);
            GraphValidator.Validate(graph);
            return graph;
        }

        private static SequenceNodeInstance Node(string id, string type, JObject props = null)
        {
            return new SequenceNodeInstance { Id = id, Type = type, Label = id, Props = props ?? new JObject() };
        }

        private static SequenceEdge Control(string id, string source, string target)
        {
            return new SequenceEdge { Id = id, Source = source, SourceHandle = "out-0", Target = target, TargetHandle = "in-0", Type = "control" };
        }

        private static SequenceEdge Data(string id, string source, string sourceHandle, string target, string targetHandle)
        {
            return new SequenceEdge { Id = id, Source = source, SourceHandle = sourceHandle, Target = target, TargetHandle = targetHandle, Type = "data" };
        }

        private static void AddPorts(SequenceGraphDocument graph)
        {
            foreach (var node in graph.Nodes)
            {
                node.Ports = new NodePorts
                {
                    In = node.Id == "trigger" ? new List<PortDef>() : new List<PortDef> { Port("in-0", "control") },
                    Out = new List<PortDef> { Port("out-0", "control") }
                };
                if (node.Type == "wait_camera_image") node.Ports.Out.Add(Port("out-1", "data"));
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
    }
}
