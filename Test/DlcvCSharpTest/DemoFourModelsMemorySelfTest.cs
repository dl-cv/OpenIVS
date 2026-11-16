using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using dlcv_infer_csharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class DemoFourModelsMemorySelfTest
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static int Run(string[] args)
        {
            try { return RunValidated(args); }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }

        private static int RunValidated(string[] args)
        {
            bool single = args[0] == "demo-single-model-memory-selftest";
            if (args.Length != (single ? 6 : 10))
                throw new ArgumentException(single ? "用法: demo-single-model-memory-selftest <输出JSON> <图片> <无CAD模型> <DlcvDemo.exe> <capture-delay-ms>" : "用法: demo-four-models-memory-selftest <输出JSON> <图片> <模型1> <模型2> <模型3> <模型4> <DlcvDemo.exe> <DlcvDemo2.exe> <capture-delay-ms>");
            string output = Path.GetFullPath(args[1]);
            if (File.Exists(output) || File.Exists(output + ".tmp")) throw new IOException("结果文件已存在: " + output);
            string[] inputs = args.Skip(2).Take(single ? 3 : 7).Select(Path.GetFullPath).ToArray();
            foreach (string path in inputs)
                if (!File.Exists(path)) throw new FileNotFoundException("输入文件不存在", path);
            if (inputs.Any(path => string.Equals(path, output, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, output + ".tmp", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("输出路径与输入重复");
            int delay;
            if (!int.TryParse(args[args.Length - 1], out delay) || delay < 0) throw new ArgumentException("capture-delay-ms 需要非负整数");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            int exitCode = 1;
            var thread = new Thread(() => exitCode = RunWindow(output, inputs, delay, single));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return exitCode;
        }

        private static int RunWindow(string output, string[] inputs, int delay, bool single)
        {
            var report = new JObject
            {
                ["status"] = "started", ["pid"] = Process.GetCurrentProcess().Id,
                ["api_assembly"] = typeof(Model).Assembly.Location, ["image"] = inputs[0],
                ["models"] = new JArray(inputs.Skip(1).Take(single ? 1 : 4)),
                ["demo_assembly"] = inputs[single ? 2 : 5], ["demo2_assembly"] = single ? null : inputs[6],
                ["device_id"] = 0, ["capture_delay_ms"] = delay,
                ["sliding_window"] = single ? null : new JObject { ["width"] = 2560, ["height"] = 2560, ["overlap_x"] = 1024, ["overlap_y"] = 1024 }
            };
            var models = new List<Model>();
            var results = new List<Utils.CSharpResult>();
            var stages = new JArray();
            report["stages"] = stages;
            int exitCode = 1;
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Type windowType = Assembly.LoadFrom(inputs[single ? 2 : 5]).GetType("DlcvDemo.MainWindow", true);
                Type pipelineType = single ? null : Assembly.LoadFrom(inputs[6]).GetType("DlcvDemo2.Form1", true);
                Type optionsType = windowType.Assembly.GetType("DlcvDemo.UiTestOptions", true);
                object[] parseArgs = { new[] { "ui-test", "--model", inputs[1], "--image", inputs[0], "--output", output,
                    "--interactive-dialogs", "false", "--language", "zh-CN" }, null, null };
                if (!(bool)optionsType.GetMethod("TryParse", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, parseArgs))
                    throw new ArgumentException((string)parseArgs[2]);
                using (var window = (Form)Activator.CreateInstance(windowType, PrivateInstance, null, new[] { parseArgs[1] }, null))
                using (var pipelineForm = single ? null : (Form)Activator.CreateInstance(pipelineType))
                {
                    // 测试驱动负责初始化及执行时序，防止原 Shown 启动第二个设备初始化线程。
                    window.Shown -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), window,
                        windowType.GetMethod("MainWindow_Shown", PrivateInstance));
                    window.Load -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), window,
                        windowType.GetMethod("Form1_Load", PrivateInstance));
                    IntPtr handle = window.Handle;
                    Invoke(window, "Form1_Load", window, EventArgs.Empty);
                    using (var context = new ApplicationContext())
                    {
                        window.BeginInvoke(new Action(async () =>
                        {
                            try
                            {
                                await Task.Run(() => Invoke(window, "GetDeviceInfo"));
                                Sample(stages, "before_load");
                                for (int i = 1; i <= (single ? 1 : 4); i++)
                                {
                                    string path = inputs[i];
                                    var timer = Stopwatch.StartNew();
                                    models.Add(await Task.Run(() => new Model(path, 0, false, false)));
                                    timer.Stop();
                                    await Task.Delay(500);
                                    Sample(stages, "loaded_" + i);
                                    stages.Last["load_ms"] = timer.Elapsed.TotalMilliseconds;
                                }
                                SetField(window, "model", models[0]);
                                SetField(window, "model_path", inputs[1]);
                                SetField(window, "image_path", inputs[0]);
                                using (var bgr = Cv2.ImRead(inputs[0], ImreadModes.Unchanged))
                                {
                                    if (bgr.Empty()) throw new InvalidDataException("整板图像读取失败");
                                    report["image_width"] = bgr.Width;
                                    report["image_height"] = bgr.Height;
                                    using (var rgb = new Mat())
                                    {
                                        if (bgr.Channels() == 3) Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
                                        else if (bgr.Channels() == 4) Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGRA2RGB);
                                        else if (bgr.Channels() == 1) bgr.CopyTo(rgb);
                                        else throw new InvalidDataException("图像通道数不支持");
                                        Utils.CSharpResult display = default(Utils.CSharpResult);
                                        var timer = new Stopwatch();
                                        if (!single)
                                        {
                                            string[] fields = { "extractModel", "componentDetectModel", "icDetectModel" };
                                            for (int i = 0; i < fields.Length; i++) SetField(pipelineForm, fields[i], models[i]);
                                            Type configType = pipelineType.GetNestedType("SlidingWindowConfig", BindingFlags.NonPublic);
                                            object config = Activator.CreateInstance(configType);
                                            foreach (string field in new[] { "WindowWidth", "WindowHeight", "OverlapX", "OverlapY" })
                                                configType.GetProperty(field).SetValue(config, field.StartsWith("Window") ? 2560 : 1024);
                                            MethodInfo run = pipelineType.GetMethod("RunPipeline", PrivateInstance);
                                            if (run.GetParameters().Length != 3) throw new MissingMethodException("需要正式三参数 RunPipeline");
                                            timer.Start();
                                            object pipeline = await Task.Run(() => run.Invoke(pipelineForm, new object[] { rgb, config, null }));
                                            timer.Stop();
                                            report["three_model_elapsed_ms"] = timer.Elapsed.TotalMilliseconds;
                                            display = (Utils.CSharpResult)ReadProperty(pipeline, "DisplayResult");
                                            results.Add(display);
                                            var logs = (List<string>)ReadProperty(pipeline, "Logs");
                                            report["pipeline_logs"] = new JArray(logs);
                                            foreach (string name in new[] { "SlidingWindowCount", "MergedExtractCount", "ComponentModelResultCount", "IcModelResultCount" })
                                                report[name] = JToken.FromObject(ReadProperty(pipeline, name));
                                            report["three_model_result"] = SerializeResult(display);
                                            if (logs.Any(log => log.Contains("推理失败"))) throw new InvalidOperationException("局部模型推理失败，详见 pipeline_logs");
                                            if (report.Value<int>("ComponentModelResultCount") <= 0 || report.Value<int>("IcModelResultCount") <= 0)
                                                throw new InvalidOperationException("整板需要产生元件与 IC 两路实际结果");
                                            Sample(stages, "after_three_model_infer");
                                        }
                                        timer.Restart();
                                        var noCad = await Task.Run(() => models[single ? 0 : 3].Infer(rgb, new JObject { ["with_mask"] = false }));
                                        timer.Stop();
                                        results.Add(noCad);
                                        report["no_cad_elapsed_ms"] = timer.Elapsed.TotalMilliseconds;
                                        report["no_cad_result"] = SerializeResult(noCad);
                                        if (noCad.SampleResults.Sum(sample => sample.Results?.Count ?? 0) == 0)
                                            throw new InvalidOperationException("无 CAD 整板推理结果为空");
                                        if (single) display = noCad;
                                        object viewer = windowType.GetField("imagePanel1", PrivateInstance).GetValue(window);
                                        var labelMode = viewer.GetType().GetProperty("LabelDisplayMode");
                                        labelMode.SetValue(viewer, Enum.Parse(labelMode.PropertyType, "None"));
                                        viewer.GetType().GetMethod("UpdateImageAndResult").Invoke(viewer, new object[] { bgr, display });
                                    }
                                    await Task.Delay(500);
                                    Sample(stages, single ? "after_single_model_infer" : "after_four_model_infer");
                                    var holdings = new JArray();
                                    foreach (var model in models)
                                    {
                                        string resourceType;
                                        var loader = DllLoader.ResolveForIndex(model.modelIndex, out resourceType);
                                        int indexType = loader.GetIndexType(model.modelIndex);
                                        if (indexType != 1 && indexType != 2) throw new InvalidOperationException("模型索引失效");
                                        holdings.Add(new JObject { ["path"] = inputs[holdings.Count + 1], ["model_index"] = model.modelIndex,
                                            ["resource_type"] = resourceType, ["index_type"] = indexType,
                                            ["module_path"] = (string)typeof(DllLoader).GetProperty("LoadedNativeModulePath", PrivateInstance).GetValue(loader),
                                            ["dvs_info"] = model.GetDvsModelInfo() });
                                    }
                                    if (holdings.Select(item => item.Value<string>("module_path") + ":" + item.Value<int>("model_index")).Distinct().Count() != (single ? 1 : 4))
                                        throw new InvalidOperationException("实际模型索引数与场景不符");
                                    report["holdings"] = holdings;
                                    report["all_four_loaded"] = !single;
                                    report["model_count"] = models.Count;
                                    report["loaded_models"] = Utils.GetAllModels();
                                    var modules = new JArray();
                                    foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
                                        if (module.ModuleName.Equals("dlcv_infer.dll", StringComparison.OrdinalIgnoreCase)
                                            || module.ModuleName.Equals("dlcv_infer_v.dll", StringComparison.OrdinalIgnoreCase))
                                            modules.Add(new JObject { ["path"] = module.FileName, ["version"] = module.FileVersionInfo.FileVersion });
                                    report["native_modules"] = modules;
                                    Invoke(window, "button_getmodelinfo_Click", window, EventArgs.Empty);
                                    report["gpu_dedicated_bytes"] = stages.Last["gpu_dedicated_bytes"];
                                    report["status"] = "passed";
                                    report["completed_utc"] = DateTime.UtcNow.ToString("O");
                                    window.Refresh();
                                    WriteReport(output, report);
                                    exitCode = 0;
                                    window.Show();
                                    await Task.Delay(delay);
                                }
                            }
                            catch (Exception error)
                            {
                                report["status"] = "failed";
                                report["error"] = error.ToString();
                                WriteReport(output, report);
                                exitCode = 1;
                            }
                            finally
                            {
                                // 先释放测试持有，随后原窗口正常执行自身关闭逻辑。
                                foreach (var model in models) model.Dispose();
                                models.Clear();
                                if (pipelineForm != null) foreach (string field in new[] { "extractModel", "componentDetectModel", "icDetectModel" }) SetField(pipelineForm, field, null);
                                SetField(window, "model", null);
                                window.Close();
                                context.ExitThread();
                            }
                        }));
                        Application.Run(context);
                    }
                }
            }
            catch (Exception error)
            {
                report["status"] = "failed";
                report["error"] = error.ToString();
                WriteReport(output, report);
            }
            finally
            {
                foreach (var model in models) model.Dispose();
                foreach (var result in results)
                    if (result.SampleResults != null)
                        foreach (var sample in result.SampleResults)
                            foreach (var item in sample.Results ?? new List<Utils.CSharpObjectResult>()) item.Mask?.Dispose();
            }
            return exitCode;
        }

        private static void WriteReport(string path, JObject report)
        {
            File.WriteAllText(path + ".tmp", report.ToString(Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            return target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args);
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        }

        private static object ReadProperty(object target, string name)
        {
            return target.GetType().GetProperty(name).GetValue(target);
        }

        private static void Sample(JArray stages, string stage)
        {
            long bytes;
            var instances = GpuProcessMemorySample.Read(stage != "before_load", out bytes);
            stages.Add(new JObject { ["stage"] = stage, ["utc"] = DateTime.UtcNow.ToString("O"),
                ["gpu_dedicated_bytes"] = bytes, ["instances"] = instances });
        }

        private static JArray SerializeResult(Utils.CSharpResult result)
        {
            if (result.SampleResults == null || result.SampleResults.Count == 0) throw new InvalidOperationException("推理结果缺少 SampleResults");
            var samples = new JArray();
            foreach (var sample in result.SampleResults)
            {
                var objects = new JArray();
                foreach (var item in sample.Results ?? new List<Utils.CSharpObjectResult>())
                    objects.Add(new JObject { ["category_id"] = item.CategoryId, ["category_name"] = item.CategoryName,
                        ["score"] = item.Score, ["area"] = item.Area, ["bbox"] = item.Bbox == null ? null : new JArray(item.Bbox),
                        ["with_bbox"] = item.WithBbox, ["with_angle"] = item.WithAngle, ["angle"] = item.Angle,
                        ["with_mask"] = item.WithMask, ["extra_info"] = item.ExtraInfo?.DeepClone() });
                samples.Add(new JObject { ["results"] = objects, ["ok"] = sample.Ok, ["reason"] = sample.Reason, ["extra_info"] = sample.ExtraInfo?.DeepClone() });
            }
            return samples;
        }
    }
}
