using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DLCV.SequenceGraph;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenIVS2.Models;
using OpenIVS2.Services;

namespace OpenIVS2.Acceptance
{
    internal static class AcceptanceRunner
    {
        public static async Task<bool> RunAsync(MainWindow window)
        {
            var root = FindRepositoryRoot();
            var output = Path.Combine(root, "TestResults", "OpenIVS2", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var screenshots = Path.Combine(output, "screenshots");
            var sources = Path.Combine(output, "virtual-images");
            var savedImages = Path.Combine(output, "saved-images");
            Directory.CreateDirectory(screenshots);
            Directory.CreateDirectory(sources);
            Directory.CreateDirectory(savedImages);
            window.SetAcceptanceLogPath(Path.Combine(output, "application.log"));

            var checks = new JArray();
            var success = true;
            var lifecycle = new List<Dictionary<string, object>>();
            try
            {
                var imagePaths = CreateVirtualImages(sources);
                var settings = CreateAcceptanceSettings(imagePaths, savedImages);

                for (var count = 1; count <= 6; count++)
                {
                    foreach (var camera in settings.Cameras) camera.Enabled = string.CompareOrdinal(camera.Slot, ((char)('A' + count - 1)).ToString()) <= 0;
                    window.ApplySettingsForAcceptance(settings);
                    var expectedRows = count <= 3 ? 1 : 2;
                    var expectedColumns = count == 1 ? 1 : count == 2 ? 2 : count == 3 ? 3 : count == 4 ? 2 : 3;
                    success &= Check(checks, "layout_" + count,
                        window.CameraCardCount == count && window.CameraGridRows == expectedRows && window.CameraGridColumns == expectedColumns,
                        count + " 台相机布局");
                }

                var settingsPreviewData = settings.Clone();
                settingsPreviewData.Cameras[0].Mode = "hik";
                var settingsPreview = new SettingsWindow(settingsPreviewData) { Owner = window };
                try
                {
                    settingsPreview.Show();
                    await Task.Delay(250);
                    var cameraSettingsScreenshot = Path.Combine(screenshots, "settings-camera-model.png");
                    settingsPreview.SelectTabForAcceptance(0);
                    settingsPreview.CaptureScreenshot(cameraSettingsScreenshot);
                    settingsPreview.SelectTabForAcceptance(1);
                    await Task.Delay(120);
                    var plcSettingsScreenshot = Path.Combine(screenshots, "settings-plc-save.png");
                    settingsPreview.CaptureScreenshot(plcSettingsScreenshot);
                    settingsPreview.SetPlcModeForAcceptance("tcp");
                    await Task.Delay(120);
                    var tcpSettingsScreenshot = Path.Combine(screenshots, "settings-plc-tcp.png");
                    settingsPreview.CaptureScreenshot(tcpSettingsScreenshot);
                    success &= Check(checks, "settings_screenshots",
                        File.Exists(cameraSettingsScreenshot) && File.Exists(plcSettingsScreenshot) && File.Exists(tcpSettingsScreenshot),
                        "设置窗口相机、PLC 和 TCP 配置截图");
                }
                finally
                {
                    settingsPreview.Close();
                }

                foreach (var camera in settings.Cameras) camera.Enabled = string.CompareOrdinal(camera.Slot, "C") <= 0;
                window.ApplySettingsForAcceptance(settings);
                var settingsPath = Path.Combine(output, "acceptance.settings.json");
                var settingsService = new SettingsService(settingsPath);
                settingsService.Save(settings);
                var loaded = settingsService.Load();
                success &= Check(checks, "settings_roundtrip", loaded.EnabledCameras().Count == 3 && loaded.PhotoRegister == settings.PhotoRegister, "设置保存与加载");
                var mockGraph = SequenceGraphBuilder.Build(loaded);
                success &= Check(checks, "mock_trigger_unchanged",
                    mockGraph.Nodes.Any(x => x.Id == "trigger" && x.Type == "manual_trigger"),
                    "Mock 模式保持原有触发入口");

                var tcpSettings = loaded.Clone();
                tcpSettings.UsePlc = true;
                tcpSettings.PlcMode = "tcp";
                tcpSettings.TcpHost = "127.0.0.1";
                tcpSettings.TcpPort = 502;
                tcpSettings.PhotoRegister = 4111;
                var tcpGraph = SequenceGraphBuilder.Build(tcpSettings);
                var tcpTrigger = tcpGraph.Nodes.FirstOrDefault(x => x.Id == "trigger");
                success &= Check(checks, "tcp_runtime_trigger",
                    tcpTrigger != null &&
                    tcpTrigger.Type == "modbus_tcp_input" &&
                    tcpTrigger.Props.Value<string>("host") == "127.0.0.1" &&
                    tcpTrigger.Props.Value<int>("port") == 502 &&
                    tcpTrigger.Props.Value<int>("photo_reg") == 4111,
                    "TCP 模式运行时图直接监听 Modbus TCP");
                success &= Check(checks, "camera_failure", VirtualCameraFailureIsReported(output), "虚拟相机缺图失败");
                success &= Check(checks, "model_failure", MissingModelFailureIsReported(output), "模型缺失失败");

                var flow = new AcceptanceFlowRunner();
                var plc = new MockModbusClient();
                await window.StartForAcceptanceAsync(flow, plc);
                await Task.Delay(150);

                var runtimeSequencePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime_sequence.json");
                var runtimeSequenceExists = File.Exists(runtimeSequencePath);
                var runtimeGraph = runtimeSequenceExists ? SequenceGraphLoader.FromFile(runtimeSequencePath) : null;
                success &= Check(checks, "runtime_sequence_snapshot",
                    runtimeGraph != null &&
                    runtimeGraph.Nodes.Any(x => x.Id == "trigger" && x.Type == "manual_trigger") &&
                    runtimeGraph.Nodes.Any(x => x.Id == "plc_clear") &&
                    runtimeGraph.Nodes.Any(x => x.Id == "wait_A") &&
                    runtimeGraph.Nodes.Any(x => x.Id == "join"),
                    "程序目录保存实际运行时序 JSON");

                plc.SetRegister(settings.PhotoRegister, settings.TriggerValue);
                success &= Check(checks, "plc_ok_cycle", await WaitUntilAsync(() => window.TotalCount == 1, 10000), "PLC 触发首个 OK 周期");
                success &= Check(checks, "plc_clear_ok", plc.ReadHoldingRegister(settings.PhotoRegister) == settings.ClearValue, "OK 周期 PLC 清零");
                success &= Check(checks, "ok_aggregate", window.OkCount == 1 && window.NgCount == 0, "全部相机 OK 时总结果 OK");

                flow.NgSlot = "B";
                await WaitUntilAsync(() => plc.ReadHoldingRegister(settings.PhotoRegister) == settings.ClearValue, 3000);
                await Task.Delay(120);
                plc.SetRegister(settings.PhotoRegister, settings.TriggerValue);
                success &= Check(checks, "plc_ng_cycle", await WaitUntilAsync(() => window.TotalCount == 2, 10000), "PLC 触发第二个 NG 周期");
                success &= Check(checks, "plc_clear_ng", plc.ReadHoldingRegister(settings.PhotoRegister) == settings.ClearValue, "NG 周期 PLC 清零");
                success &= Check(checks, "ng_aggregate", window.OkCount == 1 && window.NgCount == 1, "任一相机 NG 时总结果 NG");
                success &= Check(checks, "image_save", await WaitUntilAsync(() => Directory.GetFiles(savedImages, "*.*", SearchOption.AllDirectories).Length >= 6, 5000), "OK/NG 图片保存");

                await Task.Delay(250);
                window.CaptureScreenshot(Path.Combine(screenshots, "openivs2-3-camera-ng.png"));
                lifecycle = window.GetLifecycleEvents();
                File.WriteAllLines(Path.Combine(output, "lifecycle.log"), lifecycle.Select(JsonConvert.SerializeObject));

                window.ResetCountsForAcceptance();
                success &= Check(checks, "counter_reset", window.TotalCount == 0 && window.OkCount == 0 && window.NgCount == 0, "计数清零");
                await window.StopForAcceptanceAsync();
                success &= Check(checks, "resource_release", !window.IsRunning, "停止并释放资源");
            }
            catch (Exception ex)
            {
                success = false;
                checks.Add(new JObject { { "id", "unhandled" }, { "passed", false }, { "message", ex.ToString() } });
                try { await window.StopForAcceptanceAsync(); } catch { }
            }
            finally
            {
                var summary = new JObject
                {
                    { "success", success },
                    { "timestamp", DateTime.Now.ToString("O") },
                    { "output_directory", output },
                    { "checks", checks },
                    { "passed", checks.Count(x => x["passed"] != null && x["passed"].ToObject<bool>()) },
                    { "failed", checks.Count(x => x["passed"] == null || !x["passed"].ToObject<bool>()) }
                };
                File.WriteAllText(Path.Combine(output, "summary.json"), summary.ToString(Formatting.Indented));
                Console.WriteLine(summary.ToString(Formatting.None));
            }
            return success;
        }

        private static AppSettings CreateAcceptanceSettings(IList<string> images, string saveDirectory)
        {
            var settings = AppSettings.CreateDefault();
            settings.UsePlc = true;
            settings.PlcMode = "mock";
            settings.PhotoRegister = 500;
            settings.TriggerValue = 1;
            settings.ClearValue = 0;
            settings.PollIntervalMs = 30;
            settings.SaveDirectory = saveDirectory;
            settings.SaveOkImages = true;
            settings.SaveNgImages = true;
            settings.ImageFormat = "PNG";
            for (var i = 0; i < settings.Cameras.Count; i++)
            {
                settings.Cameras[i].Enabled = i < 3;
                settings.Cameras[i].Mode = "virtual";
                settings.Cameras[i].VirtualImagePath = images[i];
                settings.Cameras[i].ModelPath = "mock-" + settings.Cameras[i].Slot + ".flow";
                settings.Cameras[i].Rotation = i % 2 == 0 ? 0 : 90;
            }
            return settings;
        }

        private static List<string> CreateVirtualImages(string directory)
        {
            var paths = new List<string>();
            for (var i = 0; i < 6; i++)
            {
                var slot = ((char)('A' + i)).ToString();
                var path = Path.Combine(directory, slot + ".png");
                using (var bitmap = new Bitmap(800, 520))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var font = new Font("Microsoft YaHei UI", 96, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var smallFont = new Font("Microsoft YaHei UI", 24, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.FromArgb(31 + i * 12, 50 + i * 8, 64 + i * 10));
                    using (var brush = new SolidBrush(Color.FromArgb(33, 150, 243))) graphics.FillEllipse(brush, 80 + i * 15, 90, 260, 260);
                    graphics.DrawString(slot, font, Brushes.White, 150 + i * 15, 150);
                    graphics.DrawString("OpenIVS2 Virtual Camera " + slot, smallFont, Brushes.White, 70, 430);
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                paths.Add(path);
            }
            return paths;
        }

        private static bool VirtualCameraFailureIsReported(string output)
        {
            try
            {
                var handle = new VirtualCameraHandle(Path.Combine(output, "missing.png"), path => null);
                handle.GetFrame();
                return false;
            }
            catch (Exception) { return true; }
        }

        private static bool MissingModelFailureIsReported(string output)
        {
            try
            {
                using (var runner = new DlcvFlowRunner(new Dictionary<string, int>()))
                    runner.AcquireFlow(Path.Combine(output, "missing.dvt"));
                return false;
            }
            catch (FileNotFoundException) { return true; }
        }

        private static bool Check(JArray checks, string id, bool passed, string message)
        {
            checks.Add(new JObject { { "id", id }, { "passed", passed }, { "message", message } });
            return passed;
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            var start = Environment.TickCount;
            while (Environment.TickCount - start < timeoutMs)
            {
                if (predicate()) return true;
                await Task.Delay(50);
            }
            return predicate();
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "OpenIVS.sln"))) return current.FullName;
                current = current.Parent;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private sealed class AcceptanceFlowRunner : IAiFlowRunner
        {
            public string NgSlot { get; set; }

            public object Run(string flowPath, object image, string productType, string barcode)
            {
                return Run(flowPath, image, productType, barcode, null, null);
            }

            public object Run(string flowPath, object image, string productType, string barcode, string resultSourceNodeId)
            {
                return Run(flowPath, image, productType, barcode, resultSourceNodeId, null);
            }

            public object Run(string flowPath, object image, string productType, string barcode, string resultSourceNodeId, string face)
            {
                Thread.Sleep(120);
                var ng = string.Equals(face, NgSlot, StringComparison.OrdinalIgnoreCase);
                var detections = new JArray();
                if (ng)
                {
                    detections.Add(new JObject
                    {
                        { "category_name", "虚拟缺陷" },
                        { "score", 0.99 },
                        { "bbox", new JArray(120, 100, 220, 160) }
                    });
                }
                return new JObject
                {
                    { "ok", !ng },
                    { "sample_results", new JArray(new JObject { { "results", detections } }) }
                };
            }

            public IFlowHandle AcquireFlow(string flowPath) { return AcquireFlow(flowPath, null); }
            public IFlowHandle AcquireFlow(string flowPath, string face) { return new FlowHandle(flowPath, null); }
        }
    }
}
