using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using dlcv_infer_csharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvDemo
{
    public partial class MainWindow
    {
        private readonly List<Model> fourModelObjects = new List<Model>();
        private JObject fourModelScenario;
        private Utils.CSharpResult fourModelDisplayResult;
        private Utils.CSharpResult fourModelNoCadResult;

        private async Task RunFourModelMemoryTest()
        {
            fourModelScenario = new JObject { ["stage"] = "读取配置" };
            string configPath = Path.GetFullPath(uiTestOptions.ScenarioConfigPath);
            JObject settings = JObject.Parse(File.ReadAllText(configPath));
            string[] paths = { Path.GetFullPath(uiTestOptions.ModelPath),
                ReadFourModelPath(settings, "model2"), ReadFourModelPath(settings, "model3"), ReadFourModelPath(settings, "model4") };
            string assemblyPath = ReadFourModelPath(settings, "demo2_assembly");
            string cropsPath = ReadFourModelPath(settings, "crops_directory");
            if (settings["comparison_label"]?.Type != JTokenType.String)
                throw new InvalidDataException("comparison_label 需要字符串");
            string label = settings.Value<string>("comparison_label");
            if (string.IsNullOrWhiteSpace(label)) throw new InvalidDataException("comparison_label 需要比较对象名称");
            foreach (string path in paths.Concat(new[] { assemblyPath }))
                if (!File.Exists(path)) throw new FileNotFoundException("场景输入文件不存在", path);
            string outputPath = Path.GetFullPath(uiTestOptions.OutputPath);
            if (paths.Concat(new[] { assemblyPath, configPath, Path.GetFullPath(uiTestOptions.ImagePath) })
                .Any(path => string.Equals(path, outputPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, outputPath + ".tmp", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("场景输出与输入文件路径相同");
            if (File.Exists(cropsPath) || (Directory.Exists(cropsPath) && Directory.EnumerateFileSystemEntries(cropsPath).Any()))
                throw new IOException("裁图输出目录需要为空: " + cropsPath);

            var crops = new JArray();
            var tiles = new JArray();
            var stages = new JArray();
            fourModelScenario["comparison_label"] = label;
            fourModelScenario["models"] = new JArray(paths);
            fourModelScenario["demo2_assembly"] = assemblyPath;
            fourModelScenario["crops_directory"] = cropsPath;
            fourModelScenario["roi_crops"] = crops;
            fourModelScenario["sliding_window_crops"] = tiles;
            fourModelScenario["stages"] = stages;
            fourModelScenario["pid"] = Process.GetCurrentProcess().Id;
            fourModelScenario["sliding_window"] = new JObject { ["width"] = 2560, ["height"] = 2560, ["overlap_x"] = 1024, ["overlap_y"] = 1024 };
            Directory.CreateDirectory(Path.Combine(cropsPath, "元件"));
            Directory.CreateDirectory(Path.Combine(cropsPath, "IC"));
            Directory.CreateDirectory(Path.Combine(cropsPath, "滑窗"));
            richTextBox1.Text = label + "\n正在加载四个 PLUS 模型";
            WriteUiTestResult("started", null);
            try
            {
                SampleFourModelMemory(stages, "before_load");
                for (int i = 0; i < paths.Length; i++)
                {
                    int modelNumber = i;
                    Model loaded = await Task.Run(() => new Model(paths[modelNumber], uiTestOptions.DeviceId, false, false));
                    fourModelObjects.Add(loaded);
                    SampleFourModelMemory(stages, "loaded_" + fourModelObjects.Count);
                }
                model = fourModelObjects[0];
                model_path = paths[0];
                image_path = Path.GetFullPath(uiTestOptions.ImagePath);
                fourModelScenario["stage"] = "三模型推理";
                WriteUiTestResult("model_loaded", null);
                var assembly = Assembly.LoadFrom(assemblyPath);
                Type formType = assembly.GetType("DlcvDemo2.Form1", true);
                Type configType = formType.GetNestedType("SlidingWindowConfig", BindingFlags.NonPublic);
                MethodInfo runPipeline = formType.GetMethod("RunPipeline", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo prepareInput = formType.GetMethod("PrepareImageForModelInput", BindingFlags.NonPublic | BindingFlags.Static);
                if (configType == null || runPipeline == null || runPipeline.GetParameters().Length != 5 || prepareInput == null)
                    throw new MissingMethodException("Demo2 需要包含五参数 RunPipeline 和 RGB 图像入口");
                object config = Activator.CreateInstance(configType);
                foreach (var pair in new Dictionary<string, int> { ["WindowWidth"] = 2560, ["WindowHeight"] = 2560, ["OverlapX"] = 1024, ["OverlapY"] = 1024 })
                    configType.GetProperty(pair.Key).SetValue(config, pair.Value);
                using (var pipelineForm = (IDisposable)Activator.CreateInstance(formType))
                using (var imageBgr = Cv2.ImRead(image_path, ImreadModes.Unchanged))
                {
                    if (imageBgr.Empty()) throw new InvalidOperationException("整板图像读取失败");
                    fourModelScenario["image_width"] = imageBgr.Width;
                    fourModelScenario["image_height"] = imageBgr.Height;
                    string[] fields = { "extractModel", "componentDetectModel", "icDetectModel" };
                    for (int i = 0; i < fields.Length; i++)
                        formType.GetField(fields[i], BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pipelineForm, fourModelObjects[i]);
                    using (var imageRgb = (Mat)prepareInput.Invoke(null, new object[] { imageBgr }))
                    {
                        Action<Mat, Utils.CSharpObjectResult, bool, double[]> observer = (roi, target, ic, affine) =>
                            SaveFourModelRoi(cropsPath, crops, roi, target, ic, affine);
                        Action<Mat, Rect, int> tileObserver = (tile, rect, sequence) =>
                            SaveFourModelTile(cropsPath, tiles, tile, rect, sequence);
                        var timer = Stopwatch.StartNew();
                        object pipeline = await Task.Run(() => runPipeline.Invoke(pipelineForm, new object[] { imageRgb, config, null, observer, tileObserver }));
                        timer.Stop();
                        fourModelScenario["three_model_elapsed_ms"] = timer.Elapsed.TotalMilliseconds;
                        fourModelDisplayResult = (Utils.CSharpResult)ReadFourModelProperty(pipeline, "DisplayResult");
                        var logs = (List<string>)ReadFourModelProperty(pipeline, "Logs");
                        fourModelScenario["pipeline_logs"] = new JArray(logs);
                        foreach (string property in new[] { "SlidingWindowCount", "MergedExtractCount", "ComponentModelResultCount", "IcModelResultCount" })
                            fourModelScenario[property] = JToken.FromObject(ReadFourModelProperty(pipeline, property));
                        if (tiles.Count != fourModelScenario.Value<int>("SlidingWindowCount"))
                            throw new InvalidOperationException("实际保存的滑窗裁图数量与推理窗口数不一致");
                        fourModelScenario["three_model_result"] = SerializeFourModelResult(fourModelDisplayResult);
                        SampleFourModelMemory(stages, "after_three_model_infer");
                        if (logs.Any(log => log.Contains("推理失败")))
                            throw new InvalidOperationException("三模型流程出现局部模型推理失败，详见 pipeline_logs");
                        if (!crops.OfType<JObject>().Any(crop => crop.Value<string>("route") == "元件")
                            || !crops.OfType<JObject>().Any(crop => crop.Value<string>("route") == "IC"))
                            throw new InvalidOperationException("整板输入需要实际进入元件和 IC 两个分流，详见 roi_crops");
                        fourModelScenario["stage"] = "无CAD整板推理";
                        WriteUiTestResult("three_models_inferred", null);
                        timer.Restart();
                        fourModelNoCadResult = await Task.Run(() => fourModelObjects[3].Infer(imageRgb, new JObject { ["with_mask"] = false }));
                        timer.Stop();
                        fourModelScenario["no_cad_elapsed_ms"] = timer.Elapsed.TotalMilliseconds;
                        fourModelScenario["no_cad_result"] = SerializeFourModelResult(fourModelNoCadResult);
                    }
                    await Task.Delay(500);
                    SampleFourModelMemory(stages, "after_four_model_infer");
                    if (stages[stages.Count - 1].Value<long>("gpu_dedicated_bytes") <= 0)
                        throw new InvalidOperationException("四模型推理后未获取到当前进程的专用显存数值");
                    var holdings = new JArray();
                    for (int i = 0; i < fourModelObjects.Count; i++)
                    {
                        Model item = fourModelObjects[i];
                        string resourceType;
                        DllLoader loader = DllLoader.ResolveForIndex(item.modelIndex, out resourceType);
                        int indexType = loader.GetIndexType(item.modelIndex);
                        if (indexType != 1 && indexType != 2) throw new InvalidOperationException("四模型推理后存在失效索引");
                        holdings.Add(new JObject { ["path"] = paths[i], ["model_index"] = item.modelIndex, ["resource_type"] = resourceType, ["index_type"] = indexType,
                            ["dvs_info"] = item.GetDvsModelInfo() });
                    }
                    if (holdings.Count != 4) throw new InvalidOperationException("实际持有模型数量不是四个");
                    fourModelScenario["holdings"] = holdings;
                    fourModelScenario["all_four_loaded"] = true;
                    fourModelScenario["loaded_models"] = Utils.GetAllModels();
                    var modules = new JArray();
                    foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
                        if (string.Equals(module.ModuleName, "dlcv_infer.dll", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(module.ModuleName, "dlcv_infer_v.dll", StringComparison.OrdinalIgnoreCase))
                            modules.Add(new JObject { ["path"] = module.FileName, ["version"] = module.FileVersionInfo.FileVersion });
                    fourModelScenario["native_modules"] = modules;
                    imagePanel1.LabelDisplayMode = DLCV.ImageViewer.LabelTextMode.None;
                    imagePanel1.UpdateImageAndResult(imageBgr, fourModelDisplayResult);
                    if (uiTestOptions.ViewRegion.HasValue) imagePanel1.FocusImageRegion(uiTestOptions.ViewRegion.Value);
                    string memory = CudaMemoryInfo.Describe((uint)Math.Max(0, uiTestOptions.DeviceId));
                    if (memory.Contains("不可用")) throw new InvalidOperationException("当前进程显存读取失败");
                    modelInfoAfterInferText = memory;
                    int noCadCount = CountFourModelObjects(fourModelNoCadResult);
                    var text = new StringBuilder();
                    text.AppendLine(label);
                    text.AppendLine("① " + memory);
                    text.AppendLine("4 个 PLUS 模型同时驻留，整板推理完成");
                    text.AppendLine("1 元件提取：" + fourModelScenario.Value<int>("MergedExtractCount") + " 个");
                    text.AppendLine("2 元件检测：" + fourModelScenario.Value<int>("ComponentModelResultCount") + " 个");
                    text.AppendLine("3 IC 检测：" + fourModelScenario.Value<int>("IcModelResultCount") + " 个");
                    text.AppendLine("4 无 CAD：" + noCadCount + " 个");
                    text.AppendLine("原图：" + imageBgr.Width + " × " + imageBgr.Height);
                    text.AppendLine("滑窗：2560 × 2560，重叠 1024");
                    text.AppendLine("窗口：" + fourModelScenario.Value<int>("SlidingWindowCount") + "，三模型结果：" + CountFourModelObjects(fourModelDisplayResult));
                    text.AppendLine("实际 ROI 裁图：元件 " + crops.OfType<JObject>().Count(crop => crop.Value<string>("route") == "元件")
                        + "，IC " + crops.OfType<JObject>().Count(crop => crop.Value<string>("route") == "IC"));
                    richTextBox1.Text = text.ToString();
                    richTextBox1.Select(0, 0);
                    richTextBox1.ScrollToCaret();
                    fourModelScenario["gpu_dedicated_bytes"] = stages[stages.Count - 1]["gpu_dedicated_bytes"];
                    fourModelScenario["stage"] = "四模型推理完成";
                    Refresh();
                }
            }
            finally
            {
                File.WriteAllText(Path.Combine(cropsPath, "裁图清单.json"), crops.ToString(Formatting.Indented), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(cropsPath, "滑窗清单.json"), tiles.ToString(Formatting.Indented), new UTF8Encoding(false));
            }
        }

        private static string ReadFourModelPath(JObject config, string field)
        {
            if (config[field]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(config.Value<string>(field))
                || !Path.IsPathRooted(config.Value<string>(field)))
                throw new InvalidDataException(field + " 需要绝对路径");
            return Path.GetFullPath(config.Value<string>(field));
        }

        private static object ReadFourModelProperty(object source, string name)
        {
            return source.GetType().GetProperty(name).GetValue(source);
        }

        private static void SaveFourModelRoi(string directory, JArray manifest, Mat roi, Utils.CSharpObjectResult target, bool useIc, double[] affine)
        {
            string route = useIc ? "IC" : "元件";
            string relative = Path.Combine(route, (manifest.Count + 1).ToString("D5") + ".png");
            string path = Path.Combine(directory, relative);
            if (File.Exists(path)) throw new IOException("裁图文件已存在: " + path);
            using (var bgr = new Mat())
            {
                if (roi.Channels() == 3) Cv2.CvtColor(roi, bgr, ColorConversionCodes.RGB2BGR);
                else roi.CopyTo(bgr);
                if (!Cv2.ImWrite(path, bgr)) throw new IOException("归一化 ROI 保存失败: " + path);
            }
            manifest.Add(new JObject { ["image"] = relative, ["route"] = route, ["category_id"] = target.CategoryId,
                ["category_name"] = target.CategoryName, ["full_image_bbox"] = JToken.FromObject(target.Bbox),
                ["angle"] = target.Angle, ["norm_to_full_affine"] = new JArray(affine), ["width"] = roi.Width, ["height"] = roi.Height });
        }

        private static void SaveFourModelTile(string directory, JArray manifest, Mat tile, Rect rect, int sequence)
        {
            string relative = Path.Combine("滑窗", sequence.ToString("D5") + ".png");
            string path = Path.Combine(directory, relative);
            if (File.Exists(path)) throw new IOException("滑窗裁图文件已存在: " + path);
            using (var bgr = new Mat())
            {
                if (tile.Channels() == 3) Cv2.CvtColor(tile, bgr, ColorConversionCodes.RGB2BGR);
                else tile.CopyTo(bgr);
                if (!Cv2.ImWrite(path, bgr)) throw new IOException("滑窗裁图保存失败: " + path);
            }
            manifest.Add(new JObject { ["image"] = relative, ["sequence"] = sequence,
                ["full_image_rect"] = new JArray(rect.X, rect.Y, rect.Width, rect.Height),
                ["width"] = tile.Width, ["height"] = tile.Height });
        }

        private static void SampleFourModelMemory(JArray stages, string stage)
        {
            string prefix = "pid_" + Process.GetCurrentProcess().Id + "_";
            long bytes = 0;
            var instances = new JObject();
            var category = new PerformanceCounterCategory("GPU Process Memory");
            foreach (string instance in category.GetInstanceNames())
            {
                if (!instance.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                using (var counter = new PerformanceCounter("GPU Process Memory", "Dedicated Usage", instance, true))
                {
                    long actual = counter.RawValue;
                    if (actual < 0) throw new InvalidOperationException("显存计数器返回负数");
                    bytes += actual;
                    instances[instance] = actual;
                }
            }
            stages.Add(new JObject { ["stage"] = stage, ["gpu_dedicated_bytes"] = bytes, ["instances"] = instances });
        }

        private static JArray SerializeFourModelResult(Utils.CSharpResult result)
        {
            var samples = new JArray();
            if (result.SampleResults == null || result.SampleResults.Count == 0)
                throw new InvalidOperationException("模型结果缺少 SampleResults");
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

        private static int CountFourModelObjects(Utils.CSharpResult result)
        {
            return result.SampleResults?.Sum(sample => sample.Results?.Count ?? 0) ?? 0;
        }

        private void ReleaseFourModelMemoryTest()
        {
            foreach (Model item in fourModelObjects) item.Dispose();
            fourModelObjects.Clear();
            foreach (var result in new[] { fourModelDisplayResult, fourModelNoCadResult })
                if (result.SampleResults != null)
                    foreach (var sample in result.SampleResults)
                        if (sample.Results != null)
                            foreach (var item in sample.Results) item.Mask?.Dispose();
        }
    }
}
