using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DLCV.SequenceGraph;
using Microsoft.Win32;
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
            var runtimeLogs = Path.Combine(output, "runtime-logs");
            var productionLogs = Path.Combine(output, "production-logs");
            Directory.CreateDirectory(screenshots);
            Directory.CreateDirectory(sources);
            Directory.CreateDirectory(savedImages);
            Directory.CreateDirectory(runtimeLogs);
            Directory.CreateDirectory(productionLogs);
            window.SetAcceptanceLogPath(Path.Combine(output, "application.log"));

            var checks = new JArray();
            var success = true;
            var lifecycle = new List<Dictionary<string, object>>();
            try
            {
                var imagePaths = CreateVirtualImages(sources);
                var settings = CreateAcceptanceSettings(imagePaths, savedImages, runtimeLogs, productionLogs);

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
                success &= Check(checks, "camera_title_without_slot",
                    window.GetCameraTitleForAcceptance("A") == settings.Cameras[0].Name,
                    "相机标题只显示相机名称，不显示 [A] 等槽位");
                success &= Check(checks, "brand_logo_aspect_ratio",
                    window.BrandLogoKeepsAspectRatio,
                    "顶部 Logo 固定高度并按原始宽高比缩放");
                success &= Check(checks, "overall_result_header",
                    window.OverallResultHeader == "总结果",
                    "结果标题只显示总结果");
                success &= Check(checks, "camera_state_text",
                    MainWindow.FormatCameraState(CameraResourceState.Opened) == "READY" &&
                    MainWindow.FormatCameraState(CameraResourceState.NotOpened) == "NOT READY",
                    "相机状态使用 READY / NOT READY");
                success &= Check(checks, "product_display_name",
                    window.Title == "OpenIVS 2026 - 多相机工业视觉检测" &&
                    window.ProductDisplayName == "OpenIVS 2026",
                    "主窗口软件名称显示为 OpenIVS 2026");
                success &= Check(checks, "main_toolbar_settings_only",
                    window.MainToolbarButtonCount == 1,
                    "主界面操作区只保留设置按钮");
                success &= Check(checks, "automatic_start_policy",
                    MainWindow.ShouldStartAutomatically(false) && !MainWindow.ShouldStartAutomatically(true),
                    "正常启动自动运行，验收模式保持受控启动");

                var settingsPreviewData = settings.Clone();
                settingsPreviewData.Cameras[0].Mode = "hik";
                settingsPreviewData.Cameras[0].DeviceId = 3;
                settingsPreviewData.Cameras[0].Rotation = 270;
                settingsPreviewData.Cameras[0].SoftwareTrigger = false;
                settingsPreviewData.Cameras[0].FrameTimeoutMs = 4321;
                settingsPreviewData.PhotoRegister = 620;
                settingsPreviewData.TriggerValue = 7;
                settingsPreviewData.ClearValue = 2;
                settingsPreviewData.PollIntervalMs = 45;
                settingsPreviewData.StartWithWindows = true;
                var previewRunning = false;
                var previewTriggerCount = 0;
                var settingsPreview = new SettingsWindow(
                    settingsPreviewData,
                    () => { previewRunning = true; return Task.CompletedTask; },
                    () => { previewRunning = false; return Task.CompletedTask; },
                    () => { previewTriggerCount++; return Task.CompletedTask; },
                    () => previewRunning,
                    () => previewRunning) { Owner = window };
                try
                {
                    settingsPreview.Show();
                    await Task.Delay(250);
                    success &= Check(checks, "settings_product_name",
                        settingsPreview.Title == "OpenIVS 2026 设置",
                        "设置窗口软件名称显示为 OpenIVS 2026");
                    var cameraSettingsScreenshot = Path.Combine(screenshots, "settings-camera-model.png");
                    settingsPreview.SelectTabForAcceptance(0);
                    settingsPreview.CaptureScreenshot(cameraSettingsScreenshot);
                    success &= Check(checks, "camera_advanced_settings_hidden",
                        !settingsPreview.ContainsVisibleTextForAcceptance("设备 ID") &&
                        !settingsPreview.ContainsVisibleTextForAcceptance("旋转角度") &&
                        !settingsPreview.ContainsVisibleTextForAcceptance("触发方式") &&
                        !settingsPreview.ContainsVisibleTextForAcceptance("取图超时（毫秒）"),
                        "相机设备 ID、旋转角度、触发方式和取图超时不在设置界面显示");
                    settingsPreview.SelectTabForAcceptance(1);
                    await Task.Delay(120);
                    var plcSettingsScreenshot = Path.Combine(screenshots, "settings-plc-save.png");
                    settingsPreview.CaptureScreenshot(plcSettingsScreenshot);
                    success &= Check(checks, "plc_clear_contract_hidden",
                        !settingsPreview.ContainsVisibleTextForAcceptance("触发与清零契约") &&
                        !settingsPreview.ContainsVisibleTextForAcceptance("拍照寄存器") &&
                        !settingsPreview.ContainsVisibleTextForAcceptance("触发值") &&
                        !settingsPreview.ContainsVisibleTextForAcceptance("清零值") &&
                        !settingsPreview.ContainsVisibleTextForAcceptance("轮询间隔（毫秒）"),
                        "触发与清零契约不在设置界面显示");
                    success &= Check(checks, "plc_tab_scope",
                        !settingsPreview.ContainsVisibleTextForAcceptance("系统启动") &&
                        !settingsPreview.ContainsVisibleTextForAcceptance("运行调试"),
                        "PLC 与保存页签不混入系统设置和调试操作");
                    success &= Check(checks, "visualization_save_setting_ui",
                        settingsPreview.VisualizationSaveSelected &&
                        settingsPreview.VisualizationSaveLabelForAcceptance == "保存可视化图片",
                        "PLC 与保存页签显示无额外前缀的可视化图片存储选项");
                    settingsPreview.SetPlcModeForAcceptance("tcp");
                    await Task.Delay(120);
                    var tcpSettingsScreenshot = Path.Combine(screenshots, "settings-plc-tcp.png");
                    settingsPreview.CaptureScreenshot(tcpSettingsScreenshot);
                    settingsPreview.SelectTabForAcceptance(2);
                    await Task.Delay(120);
                    var systemSettingsScreenshot = Path.Combine(screenshots, "settings-system.png");
                    settingsPreview.CaptureScreenshot(systemSettingsScreenshot);
                    success &= Check(checks, "system_settings_tab",
                        settingsPreview.ContainsVisibleTextForAcceptance("系统启动") &&
                        settingsPreview.ContainsVisibleTextForAcceptance("运行时日志") &&
                        settingsPreview.ContainsVisibleTextForAcceptance("生产日志") &&
                        !settingsPreview.ContainsVisibleTextForAcceptance("运行调试") &&
                        settingsPreview.StartWithWindowsSelected &&
                        settingsPreview.RuntimeLogSelected && settingsPreview.ProductionLogSelected,
                        "开机自启动、运行日志和生产日志位于系统设置页签");
                    settingsPreview.SelectTabForAcceptance(3);
                    await Task.Delay(120);
                    var debugSettingsScreenshot = Path.Combine(screenshots, "settings-debug.png");
                    settingsPreview.CaptureScreenshot(debugSettingsScreenshot);
                    success &= Check(checks, "debug_settings_tab",
                        settingsPreview.ContainsVisibleTextForAcceptance("运行调试") &&
                        !settingsPreview.ContainsVisibleTextForAcceptance("系统启动") &&
                        settingsPreview.RuntimeControlsAvailable,
                        "开始、停止和单次触发位于独立调试页签");
                    success &= Check(checks, "settings_screenshots",
                        File.Exists(cameraSettingsScreenshot) && File.Exists(plcSettingsScreenshot) &&
                        File.Exists(tcpSettingsScreenshot) && File.Exists(systemSettingsScreenshot) &&
                        File.Exists(debugSettingsScreenshot),
                        "设置窗口相机、PLC、系统设置和调试页签截图");
                    success &= Check(checks, "startup_setting_ui",
                        settingsPreview.StartWithWindowsSelected,
                        "设置窗口显示开机自启动选项");
                    success &= Check(checks, "hidden_settings_preserved",
                        settingsPreview.WorkingSettings.Cameras[0].DeviceId == 3 &&
                        settingsPreview.WorkingSettings.Cameras[0].Rotation == 270 &&
                        !settingsPreview.WorkingSettings.Cameras[0].SoftwareTrigger &&
                        settingsPreview.WorkingSettings.Cameras[0].FrameTimeoutMs == 4321 &&
                        settingsPreview.WorkingSettings.PhotoRegister == 620 &&
                        settingsPreview.WorkingSettings.TriggerValue == 7 &&
                        settingsPreview.WorkingSettings.ClearValue == 2 &&
                        settingsPreview.WorkingSettings.PollIntervalMs == 45,
                        "隐藏选项的原有参数保持不变");
                    await settingsPreview.StartRuntimeForAcceptanceAsync();
                    await settingsPreview.TriggerRuntimeForAcceptanceAsync();
                    await settingsPreview.StopRuntimeForAcceptanceAsync();
                    success &= Check(checks, "settings_runtime_controls",
                        settingsPreview.RuntimeControlsAvailable && !previewRunning && previewTriggerCount == 1,
                        "设置窗口开始、停止和单次触发控制可用");
                }
                finally
                {
                    settingsPreview.Close();
                }

                foreach (var camera in settings.Cameras) camera.Enabled = string.CompareOrdinal(camera.Slot, "C") <= 0;
                settings.StartWithWindows = true;
                window.ApplySettingsForAcceptance(settings);
                var settingsPath = Path.Combine(output, "acceptance.settings.json");
                var settingsService = new SettingsService(settingsPath);
                settingsService.Save(settings);
                var loaded = settingsService.Load();
                success &= Check(checks, "settings_roundtrip",
                    loaded.EnabledCameras().Count == 3 && loaded.PhotoRegister == settings.PhotoRegister &&
                    loaded.StartWithWindows && loaded.EnableRuntimeLog && loaded.EnableProductionLog &&
                    loaded.RuntimeLogDirectory == runtimeLogs && loaded.ProductionLogDirectory == productionLogs &&
                    loaded.SaveVisualizationImages && loaded.VisualizationImageFormat == "JPG",
                    "设置保存与加载");
                success &= Check(checks, "runtime_log_rotation",
                    RuntimeLogRotationWorks(runtimeLogs),
                    "运行时日志按大小轮转并限制保留数量");
                success &= Check(checks, "startup_registry_roundtrip",
                    StartupRegistrationRoundtrip(output),
                    "开机自启动注册表写入和关闭");
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

                window.ApplySettingsForAcceptance(tcpSettings);
                await window.StartForAcceptanceAsync(new AcceptanceFlowRunner(), new UnavailableModbusClient());
                success &= Check(checks, "tcp_unavailable_runtime",
                    await WaitUntilAsync(() => window.IsRunning && window.IsModbusWaiting, 2000) && window.SettingsAvailable,
                    "TCP 服务器未连接时系统继续运行且设置可用");
                await window.StopForAcceptanceAsync();
                window.ApplySettingsForAcceptance(settings);

                success &= Check(checks, "camera_failure", VirtualCameraFailureIsReported(output), "虚拟相机缺图失败");
                success &= Check(checks, "model_failure", MissingModelFailureIsReported(output), "模型缺失失败");

                var flow = new AcceptanceFlowRunner();
                var plc = new MockModbusClient();
                await window.StartForAcceptanceAsync(flow, plc);
                await Task.Delay(150);
                var settingsButtonBrush = window.SettingsButtonBrushForAcceptance as System.Windows.Media.SolidColorBrush;
                success &= Check(checks, "running_settings_button_color",
                    settingsButtonBrush != null &&
                    settingsButtonBrush.Color == System.Windows.Media.Color.FromRgb(96, 125, 139),
                    "运行状态下设置按钮使用 RGB(96,125,139)");

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
                success &= Check(checks, "first_cycle_images",
                    window.VisibleCameraImageCount == 3 && window.BufferedCameraImageCount == 3,
                    "首个周期更新全部相机画面");
                success &= Check(checks, "no_detection_visualization_skipped",
                    await WaitUntilAsync(() => Directory.GetFiles(savedImages, "*.png", SearchOption.AllDirectories)
                        .Count(x => x.IndexOf("_vis.", StringComparison.OrdinalIgnoreCase) < 0) >= 3, 5000) &&
                    ValidJpegVisualizations(savedImages) == 0,
                    "没有检测结果的相机不保存可视化图片");
                var overallOkBrush = window.OverallResultBrushForAcceptance as System.Windows.Media.SolidColorBrush;
                var cameraOkBrush = window.GetCameraStatusBrushForAcceptance("A") as System.Windows.Media.SolidColorBrush;
                success &= Check(checks, "camera_ok_color_matches_overall",
                    overallOkBrush != null && cameraOkBrush != null &&
                    overallOkBrush.Color == System.Windows.Media.Color.FromRgb(76, 175, 80) &&
                    cameraOkBrush.Color == overallOkBrush.Color,
                    "相机 OK 与总结果 OK 使用相同绿色");

                flow.NgSlot = "B";
                await WaitUntilAsync(() => plc.ReadHoldingRegister(settings.PhotoRegister) == settings.ClearValue, 3000);
                await Task.Delay(120);
                plc.SetRegister(settings.PhotoRegister, settings.TriggerValue);
                success &= Check(checks, "cycle_start_clears_images",
                    await WaitUntilAsync(() => window.VisibleCameraImageCount == 0 && window.BufferedCameraImageCount == 0, 3000),
                    "新周期开始时清空上次画面和显示缓存");
                success &= Check(checks, "plc_ng_cycle", await WaitUntilAsync(() => window.TotalCount == 2, 10000), "PLC 触发第二个 NG 周期");
                success &= Check(checks, "plc_clear_ng", plc.ReadHoldingRegister(settings.PhotoRegister) == settings.ClearValue, "NG 周期 PLC 清零");
                success &= Check(checks, "ng_aggregate", window.OkCount == 1 && window.NgCount == 1, "任一相机 NG 时总结果 NG");
                success &= Check(checks, "image_save",
                    await WaitUntilAsync(() => Directory.GetFiles(savedImages, "*.png", SearchOption.AllDirectories)
                        .Count(x => x.IndexOf("_vis.", StringComparison.OrdinalIgnoreCase) < 0) >= 6, 5000),
                    "OK/NG 原图保存");
                success &= Check(checks, "visualization_jpg_save",
                    await WaitUntilAsync(() => ValidJpegVisualizations(savedImages) == 1, 5000),
                    "仅有检测结果的 ImageViewer 可视化以压缩 JPG 保存");
                success &= Check(checks, "production_csv",
                    await WaitUntilAsync(() => ProductionCsvIsValid(productionLogs), 5000),
                    "生产 CSV 每周期记录时间、时间戳、相机结果和总结果");

                var viewer = window.GetCameraViewerForAcceptance("B");
                success &= Check(checks, "interactive_viewer_result_layer",
                    viewer != null && viewer.HasImage && viewer.OverlayCount > 0,
                    "WPF 查看器接收原图和独立预测层");
                if (viewer != null)
                {
                    var geometry = viewer.OverlayGeometrySignature;
                    var initialScale = viewer.AnnotationScale;
                    var initialLineWidth = viewer.EffectiveScreenLineWidth;
                    viewer.ToggleOverlays();
                    var hidden = !viewer.OverlaysVisible && viewer.HasImage;
                    viewer.ToggleOverlays();
                    viewer.IncreaseAnnotationScale();
                    success &= Check(checks, "interactive_viewer_toggle",
                        hidden && viewer.OverlaysVisible,
                        "V 逻辑只切换预测层，原图保持显示");
                    success &= Check(checks, "interactive_viewer_annotation_scale",
                        viewer.AnnotationScale > initialScale &&
                        viewer.EffectiveScreenLineWidth > initialLineWidth &&
                        viewer.OverlayGeometrySignature == geometry,
                        "+/- 同步调整字号和框线宽且不改变框坐标");
                    var zoomBefore = viewer.Zoom;
                    viewer.ZoomAt(new System.Windows.Point(viewer.ActualWidth / 2.0, viewer.ActualHeight / 2.0), 1.1);
                    var zoomed = viewer.Zoom > zoomBefore;
                    viewer.FitToView();
                    success &= Check(checks, "interactive_viewer_zoom_reset", zoomed,
                        "滚轮缩放逻辑和右键复位逻辑可用");
                    viewer.DecreaseAnnotationScale();
                    success &= Check(checks, "visualization_png_save",
                        VisualizationPngSaveWorks(output, settings, viewer),
                        "ImageViewer 可视化支持压缩 PNG 保存");
                }

                await Task.Delay(250);
                window.CaptureScreenshot(Path.Combine(screenshots, "openivs2-3-camera-ng.png"));
                lifecycle = window.GetLifecycleEvents();
                File.WriteAllLines(Path.Combine(output, "lifecycle.log"), lifecycle.Select(JsonConvert.SerializeObject));

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

        private static AppSettings CreateAcceptanceSettings(
            IList<string> images,
            string saveDirectory,
            string runtimeLogDirectory,
            string productionLogDirectory)
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
            settings.SaveVisualizationImages = true;
            settings.VisualizationImageFormat = "JPG";
            settings.EnableRuntimeLog = true;
            settings.RuntimeLogDirectory = runtimeLogDirectory;
            settings.RuntimeLogMaxFileSizeMB = 10;
            settings.RuntimeLogMaxFileCount = 7;
            settings.EnableProductionLog = true;
            settings.ProductionLogDirectory = productionLogDirectory;
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

        private static bool RuntimeLogRotationWorks(string directory)
        {
            var path = Path.Combine(directory, "rotation.log");
            var service = new RuntimeLogService();
            service.ConfigureFile(path, 1, 2);
            var payload = new string('X', 600 * 1024);
            for (var i = 0; i < 4; i++)
            {
                if (!service.WriteLine("rotation-" + i + " " + payload)) return false;
            }
            var files = Directory.GetFiles(directory, "rotation*.log");
            return files.Length == 2 && files.Any(x => !string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        }

        private static int ValidJpegVisualizations(string directory)
        {
            var valid = 0;
            foreach (var path in Directory.GetFiles(directory, "*_vis.jpg", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8) valid++;
            }
            return valid;
        }

        private static bool ProductionCsvIsValid(string directory)
        {
            var path = Directory.GetFiles(directory, "*.csv").FirstOrDefault();
            if (path == null) return false;
            var lines = File.ReadAllLines(path, Encoding.GetEncoding(936));
            if (lines.Length < 3 || lines[0] != "时间,时间戳,相机A,相机B,相机C,相机D,相机E,相机F,总结果") return false;
            var ok = lines[1].Split(',');
            var ng = lines[2].Split(',');
            long okTimestamp;
            long ngTimestamp;
            return ok.Length == 9 && ng.Length == 9 &&
                long.TryParse(ok[1], out okTimestamp) && long.TryParse(ng[1], out ngTimestamp) &&
                ok[2] == "OK" && ok[3] == "OK" && ok[4] == "OK" && ok[5] == "-" && ok[8] == "OK" &&
                ng[2] == "OK" && ng[3] == "NG" && ng[4] == "OK" && ng[5] == "-" && ng[8] == "NG";
        }

        private static bool VisualizationPngSaveWorks(
            string output,
            AppSettings settings,
            OpenIVS2.Controls.InteractiveImageViewer viewer)
        {
            var visualization = viewer.RenderVisualization();
            if (visualization == null) return false;
            var pngSettings = settings.Clone();
            pngSettings.SaveDirectory = Path.Combine(output, "visualization-png");
            pngSettings.ImageFormat = "PNG";
            pngSettings.VisualizationImageFormat = "PNG";
            pngSettings.SaveVisualizationImages = true;
            var images = new Dictionary<string, System.Windows.Media.Imaging.BitmapSource>(StringComparer.OrdinalIgnoreCase)
            {
                { "B", visualization }
            };
            var paths = new ImageSaveService().SaveCycle(pngSettings, false, images, images);
            var path = paths.FirstOrDefault(x => x.EndsWith("_vis.png", StringComparison.OrdinalIgnoreCase));
            if (path == null || !File.Exists(path)) return false;
            var bytes = File.ReadAllBytes(path);
            return bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
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
                    graphics.DrawString("OpenIVS 2026 Virtual Camera " + slot, smallFont, Brushes.White, 70, 430);
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

        private static bool StartupRegistrationRoundtrip(string output)
        {
            var registryPath = @"Software\OpenIVS2-Acceptance-" + Guid.NewGuid().ToString("N");
            try
            {
                var executablePath = Path.Combine(output, "OpenIVS2.exe");
                using (var key = Registry.CurrentUser.CreateSubKey(registryPath))
                {
                    if (key == null) return false;
                    key.SetValue("OpenIVS2", WindowsStartupService.BuildCommand(executablePath), RegistryValueKind.String);
                }
                var service = new WindowsStartupService(registryPath, "OpenIVS 2026", executablePath, "OpenIVS2");
                service.SetEnabled(true);
                var enabled = service.IsEnabled();
                bool legacyRemoved;
                using (var key = Registry.CurrentUser.OpenSubKey(registryPath, false))
                    legacyRemoved = key != null && key.GetValue("OpenIVS2") == null;
                service.SetEnabled(false);
                return enabled && legacyRemoved && !service.IsEnabled();
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); } catch { }
            }
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

        private sealed class UnavailableModbusClient : IModbusClient
        {
            public bool Connect(string host, int port, byte deviceId)
            {
                throw new InvalidOperationException("模拟 Modbus TCP 服务器未启动");
            }

            public void Close() { }
            public ushort ReadHoldingRegister(ushort address) { throw new InvalidOperationException("Modbus TCP 未连接"); }
            public ushort[] ReadHoldingRegisters(ushort address, ushort count) { throw new InvalidOperationException("Modbus TCP 未连接"); }
            public void WriteSingleRegister(ushort address, ushort value) { throw new InvalidOperationException("Modbus TCP 未连接"); }
        }
    }
}
