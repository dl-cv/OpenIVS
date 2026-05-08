using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using DlcvModules;
using dlcv_infer_csharp;

namespace DlcvCSharpTest
{
    internal static class Program
    {
        private const int SpeedWindowSeconds = 3;
        private const int LeakLoopCount = 10;
        private const int GpuDeviceId = 0;
        private const int FixedBatchSize = 1;
        private const bool TestSpeed = false;
        private const string ModelRoot = @"Y:\测试模型";
        private const string DefaultPressureModelPath = @"C:\Users\Administrator\Desktop\dvst速度优化\流程2-各项检测_120_50.dvst";
        private const string DefaultPressureImagePath = @"C:\Users\Administrator\Desktop\dvst速度优化\detect_20260401153742_0_6_2904_5248_627_804.jpg";
        private const int DefaultPressureThreadCount = 1;
        private const int DefaultPressureBatchSize = 128;
        private const int DefaultPressureRuns = 9;
        private const int DefaultPressureWarmup = 5;
        private const string UsLagModelPath = @"C:\Users\Administrator\Desktop\测试无监督\测试无监督-v5_120_50.dvt";
        private const string UsLagImagePath1 = @"C:\Users\Administrator\Desktop\测试无监督\NG1.png";
        private const string UsLagImagePath2 = @"C:\Users\Administrator\Desktop\测试无监督\NG3.png";
        private const string FlowInstanceSegFilterModelPath = @"Y:\zxc\模块化任务测试\实例分割筛选测试_120_50.dvst";
        private const string FlowInstanceSegFilterImagePath = @"Y:\zxc\模块化任务测试\实例分割\实例分割滑窗大图.png";

        private static readonly List<ModelCase> DefaultCases = new List<ModelCase>
        {
            new ModelCase("AOI-旋转框检测_120_50.dvt", "AOI-1.jpg"),
            new ModelCase("AOI_120_50.dvst", "AOI-1.jpg"),
            new ModelCase("猫狗-分类_120_50.dvt", "猫狗-猫.jpg"),
            new ModelCase("猫狗-分类_120_50_v.dvt", "猫狗-猫.jpg"),
            new ModelCase("气球-实例分割_120_50.dvt", "气球.jpg"),
            new ModelCase("气球-实例分割_120_50_v.dvt", "气球.jpg"),
            new ModelCase("气球-语义分割_120_50.dvt", "气球.jpg"),
            new ModelCase("手机屏幕-实例分割_120_50.dvt", "手机屏幕.jpg"),
            new ModelCase("引脚定位-目标检测_120_50.dvt", "引脚定位-目标检测.jpg"),
            new ModelCase("OCR_120_50.dvt", "OCR-1.jpg")
        };

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Model.EnableConsoleLog = false;
            try
            {
                if (args != null && args.Length >= 1 && string.Equals(args[0], "model-channel-order-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunModelChannelOrderSelfTest();
                }

                if (args != null && args.Length >= 1 &&
                    (string.Equals(args[0], "dvs-rgb-selftest", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(args[0], "dvs-bgr-selftest", StringComparison.OrdinalIgnoreCase)))
                {
                    return RunDvsRgbSelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "maskrbox-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunMaskToRBoxSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "rect-image-correction-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunRectImageCorrectionSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "demo2-rgb-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunDemo2RgbSelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "demo2-route-rule-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunDemo2RouteRuleSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "bench", StringComparison.OrdinalIgnoreCase))
                {
                    return RunBenchmarkCommand(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "us-lag-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunUsLagSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "flow-instance-seg-filter-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunFlowInstanceSegFilterSelfTest();
                }

                if (args != null && args.Length >= 2)
                {
                    string modelPath = args[0];
                    string imagePath = args[1];
                    int batch = DefaultPressureBatchSize;
                    if (args.Length >= 3)
                    {
                        int.TryParse(args[2], out batch);
                        if (batch <= 0) batch = DefaultPressureBatchSize;
                    }
                    return RunSingleBatchValidation(modelPath, imagePath, batch);
                }

                return RunDefaultCases();
            }
            finally
            {
                try { Utils.FreeAllModels(); } catch { }
                ForceGc();
            }
        }

        private static int RunDefaultCases()
        {
            Console.WriteLine("==== C# 默认测试（DefaultCases） ====");
            Console.WriteLine("模型目录: " + ModelRoot);
            Console.WriteLine("固定设备: GPU(" + GpuDeviceId + ")");
            Console.WriteLine("固定Batch: " + FixedBatchSize);
            Console.WriteLine();

            bool modelRootOk = Directory.Exists(ModelRoot);
            if (!modelRootOk)
            {
                Console.WriteLine("模型目录不存在: " + ModelRoot);
            }

            // 内存泄露专项：只跑一个实例分割模型
            string leakModelPath = null;
            string leakImagePath = null;
            if (modelRootOk)
            {
                foreach (var c in DefaultCases)
                {
                    if (!c.ModelFile.Contains("实例分割")) continue;
                    string mp = Path.Combine(ModelRoot, c.ModelFile);
                    string ip = Path.Combine(ModelRoot, c.ImageFile);
                    if (!File.Exists(mp) || !File.Exists(ip)) continue;
                    leakModelPath = mp;
                    leakImagePath = ip;
                    break;
                }
            }

            var rows = new List<CaseRow>(DefaultCases.Count);
            int total = 0;
            int pass = 0;
            foreach (var c in DefaultCases)
            {
                string modelPath = Path.Combine(ModelRoot, c.ModelFile);
                string imagePath = Path.Combine(ModelRoot, c.ImageFile);
                if (!modelRootOk)
                {
                    rows.Add(new CaseRow
                    {
                        ModelName = c.ModelFile,
                        LoadStatus = "跳过",
                        InferStatus = "-",
                        CategoryList = "模型目录不存在",
                        SpeedText = "-",
                        BatchText = "-"
                    });
                    continue;
                }
                if (!File.Exists(modelPath) || !File.Exists(imagePath))
                {
                    rows.Add(new CaseRow
                    {
                        ModelName = Path.GetFileName(modelPath),
                        LoadStatus = "跳过",
                        InferStatus = "-",
                        CategoryList = "模型或图片不存在",
                        SpeedText = "-",
                        BatchText = "-"
                    });
                    continue;
                }

                total++;
                var row = RunCase(modelPath, imagePath);
                rows.Add(row);
                if (row.LoadStatus.StartsWith("成功") && row.InferStatus == "成功") pass++;
            }

            rows.Add(new CaseRow
            {
                ModelName = "汇总",
                LoadStatus = "总数=" + total,
                InferStatus = "成功=" + pass,
                CategoryList = "失败=" + (total - pass),
                SpeedText = "-",
                BatchText = "-"
            });

            PrintHeader();
            foreach (var r in rows)
            {
                PrintRow(r.ModelName, r.LoadStatus, r.InferStatus, r.CategoryList, r.SpeedText, r.BatchText);
            }
            PrintFooter();

            Console.WriteLine("==== 内存泄露专项(仅测1个实例分割模型) ====");
            if (!modelRootOk)
            {
                Console.WriteLine("跳过：模型目录不存在");
            }
            else if (string.IsNullOrEmpty(leakModelPath) || string.IsNullOrEmpty(leakImagePath))
            {
                Console.WriteLine("跳过：未找到可用实例分割模型");
            }
            else
            {
                Console.WriteLine("模型: " + Path.GetFileName(leakModelPath));
                try
                {
                    double inc = RunLoadFreeLeak(leakModelPath, GpuDeviceId);
                    Console.WriteLine("加载/释放循环" + LeakLoopCount + "次内存增量: " + inc.ToString("F2", CultureInfo.InvariantCulture) + "MB");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("加载/释放循环" + LeakLoopCount + "次内存增量: 错误:" + Trim(ex.Message));
                }

                try
                {
                    double inc = RunInferLeak3s(leakModelPath, leakImagePath, GpuDeviceId);
                    Console.WriteLine("推理" + SpeedWindowSeconds + "秒内存增量: " + inc.ToString("F2", CultureInfo.InvariantCulture) + "MB");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("推理" + SpeedWindowSeconds + "秒内存增量: 错误:" + Trim(ex.Message));
                }
            }
            if (!modelRootOk) return 2;
            return total == pass ? 0 : 1;
        }

        private static int RunDefaultPressureBenchmark()
        {
            string modelPath = DefaultPressureModelPath;
            string imagePath = DefaultPressureImagePath;
            int batch = DefaultPressureBatchSize;
            int runs = DefaultPressureRuns;
            int warmup = DefaultPressureWarmup;

            Console.WriteLine("模型: " + modelPath);
            Console.WriteLine("图片: " + imagePath);

            if (!File.Exists(modelPath))
            {
                Console.WriteLine("模型不存在");
                return 2;
            }
            if (!File.Exists(imagePath))
            {
                Console.WriteLine("图片不存在");
                return 2;
            }

            bool isFlowModel = IsFlowModelPath(modelPath);
            Model model = null;
            Mat bgr = null;
            Mat rgb = null;
            try
            {
                model = new Model(modelPath, GpuDeviceId, false, false);
                Console.WriteLine("provider=" + model.LoadedDogProvider + ", dll=" + model.LoadedNativeDllName);
                bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (bgr == null || bgr.Empty()) throw new Exception("图像解码失败");
                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

                var list = new List<Mat>(batch);
                for (int i = 0; i < batch; i++) list.Add(rgb);

                var p = new JObject
                {
                    ["threshold"] = 0.5,
                    ["with_mask"] = false,
                    ["batch_size"] = batch
                };

                for (int i = 0; i < warmup; i++)
                {
                    var warm = model.InferBatch(list, p);
                    DisposeResultMasks(warm);
                }

                double sdkSum = 0.0;
                double flowSum = 0.0;
                var nodeStats = new Dictionary<string, NodeTimingAggregate>(StringComparer.Ordinal);
                var total = Stopwatch.StartNew();

                for (int i = 0; i < runs; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var result = model.InferBatch(list, p);
                    sw.Stop();

                    double sdkMs = 0.0;
                    double flowMs = 0.0;
                    InferTiming.GetLast(out sdkMs, out flowMs);
                    if (sdkMs <= 0.0) sdkMs = sw.Elapsed.TotalMilliseconds;
                    if (flowMs <= 0.0) flowMs = sw.Elapsed.TotalMilliseconds;

                    sdkSum += sdkMs;
                    flowSum += flowMs;

                    if (isFlowModel)
                    {
                        var timings = InferTiming.GetLastFlowNodeTimings();
                        for (int j = 0; j < timings.Count; j++)
                        {
                            var timing = timings[j];
                            if (timing == null) continue;
                            string key = timing.NodeId.ToString() + "|" + timing.NodeType + "|" + timing.NodeTitle;
                            if (!nodeStats.TryGetValue(key, out NodeTimingAggregate aggregate))
                            {
                                aggregate = new NodeTimingAggregate(timing.NodeId, timing.NodeType, timing.NodeTitle);
                                nodeStats[key] = aggregate;
                            }
                            aggregate.Add(timing.ElapsedMs);
                        }
                    }

                    DisposeResultMasks(result);
                }

                total.Stop();
                double avgSdk = sdkSum / Math.Max(1, runs);
                double avgFlow = flowSum / Math.Max(1, runs);

                Console.WriteLine();
                Console.WriteLine("压力测试统计:");
                Console.WriteLine("线程数: " + DefaultPressureThreadCount);
                Console.WriteLine("批量大小: " + batch);
                Console.WriteLine("运行时间: " + total.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " 秒");
                Console.WriteLine("完成请求: " + ((long)runs * batch).ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("平均延迟(SDK): " + avgSdk.ToString("F2", CultureInfo.InvariantCulture) + "ms");
                Console.WriteLine("平均延迟(总时间): " + avgFlow.ToString("F2", CultureInfo.InvariantCulture) + "ms");
                Console.WriteLine("模块平均耗时:");

                if (!isFlowModel || nodeStats.Count == 0)
                {
                    Console.WriteLine("(无流程节点统计)");
                }
                else
                {
                    foreach (var item in nodeStats.Values.OrderByDescending(v => v.AverageMs))
                    {
                        double share = avgFlow > 0.0 ? item.AverageMs * 100.0 / avgFlow : 0.0;
                        Console.WriteLine(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "#{0} [{1}] {2}: {3:F2}ms ({4:F1}%)",
                                item.NodeId,
                                item.NodeType,
                                string.IsNullOrWhiteSpace(item.NodeTitle) ? "-" : item.NodeTitle,
                                item.AverageMs,
                                share));
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("压力测试异常: " + ex.Message);
                return 1;
            }
            finally
            {
                if (rgb != null) rgb.Dispose();
                if (bgr != null) bgr.Dispose();
                try { if (model != null) model.Dispose(); } catch { }
                ForceGc();
            }
        }

        private static int RunBenchmarkCommand(string[] args)
        {
            if (args == null || args.Length < 3)
            {
                Console.WriteLine("用法: DlcvCSharpTest bench <modelPath> <imagePath> [batch] [runs] [warmup]");
                return 2;
            }

            string modelPath = args[1];
            string imagePath = args[2];
            int batch = ParsePositiveIntArg(args, 3, 1);
            int runs = ParsePositiveIntArg(args, 4, 20);
            int warmup = ParsePositiveIntArg(args, 5, 5);

            Console.WriteLine("==== 基准测试 ====");
            Console.WriteLine("model: " + modelPath);
            Console.WriteLine("image: " + imagePath);
            Console.WriteLine("batch: " + batch);
            Console.WriteLine("runs: " + runs);
            Console.WriteLine("warmup: " + warmup);

            if (!File.Exists(modelPath))
            {
                Console.WriteLine("模型不存在");
                return 2;
            }
            if (!File.Exists(imagePath))
            {
                Console.WriteLine("图片不存在");
                return 2;
            }

            bool isFlowModel = IsFlowModelPath(modelPath);
            Model model = null;
            Mat bgr = null;
            Mat rgb = null;
            try
            {
                model = new Model(modelPath, GpuDeviceId, false, false);
                Console.WriteLine("provider=" + model.LoadedDogProvider + ", dll=" + model.LoadedNativeDllName);
                bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (bgr == null || bgr.Empty()) throw new Exception("图像解码失败");
                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

                var list = new List<Mat>(batch);
                for (int i = 0; i < batch; i++) list.Add(rgb);

                var p = new JObject
                {
                    ["threshold"] = 0.5,
                    ["with_mask"] = false,
                    ["batch_size"] = batch
                };

                for (int i = 0; i < warmup; i++)
                {
                    var warm = model.InferBatch(list, p);
                    DisposeResultMasks(warm);
                }

                double sdkSum = 0.0;
                double flowSum = 0.0;
                double outerSum = 0.0;
                int sampleCount = -1;
                var nodeStats = new Dictionary<string, NodeTimingAggregate>(StringComparer.Ordinal);

                for (int i = 0; i < runs; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var result = model.InferBatch(list, p);
                    sw.Stop();

                    if (sampleCount < 0)
                    {
                        sampleCount = result.SampleResults != null ? result.SampleResults.Count : 0;
                    }

                    double sdkMs = 0.0;
                    double flowMs = 0.0;
                    InferTiming.GetLast(out sdkMs, out flowMs);
                    if (sdkMs <= 0.0) sdkMs = sw.Elapsed.TotalMilliseconds;
                    if (flowMs <= 0.0) flowMs = sw.Elapsed.TotalMilliseconds;

                    sdkSum += sdkMs;
                    flowSum += flowMs;
                    outerSum += sw.Elapsed.TotalMilliseconds;

                    if (isFlowModel)
                    {
                        var timings = InferTiming.GetLastFlowNodeTimings();
                        for (int j = 0; j < timings.Count; j++)
                        {
                            var timing = timings[j];
                            if (timing == null) continue;
                            string key = timing.NodeId.ToString() + "|" + timing.NodeType + "|" + timing.NodeTitle;
                            if (!nodeStats.TryGetValue(key, out NodeTimingAggregate aggregate))
                            {
                                aggregate = new NodeTimingAggregate(timing.NodeId, timing.NodeType, timing.NodeTitle);
                                nodeStats[key] = aggregate;
                            }
                            aggregate.Add(timing.ElapsedMs);
                        }
                    }

                    DisposeResultMasks(result);
                }

                double avgSdk = sdkSum / Math.Max(1, runs);
                double avgFlow = flowSum / Math.Max(1, runs);
                double avgOuter = outerSum / Math.Max(1, runs);

                Console.WriteLine("sample_count: " + sampleCount);
                Console.WriteLine("avg_sdk_ms: " + avgSdk.ToString("F2", CultureInfo.InvariantCulture));
                Console.WriteLine("avg_flow_ms: " + avgFlow.ToString("F2", CultureInfo.InvariantCulture));
                Console.WriteLine("avg_outer_ms: " + avgOuter.ToString("F2", CultureInfo.InvariantCulture));
                Console.WriteLine("avg_overhead_ms: " + Math.Max(0.0, avgFlow - avgSdk).ToString("F2", CultureInfo.InvariantCulture));

                if (isFlowModel)
                {
                    Console.WriteLine("---- 节点平均耗时 ----");
                    foreach (var item in nodeStats.Values.OrderByDescending(v => v.AverageMs))
                    {
                        double share = avgFlow > 0.0 ? item.AverageMs * 100.0 / avgFlow : 0.0;
                        Console.WriteLine(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "#{0} [{1}] {2} -> avg={3:F2}ms, share={4:F1}%",
                                item.NodeId,
                                item.NodeType,
                                string.IsNullOrWhiteSpace(item.NodeTitle) ? "-" : item.NodeTitle,
                                item.AverageMs,
                                share));
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("基准异常: " + ex.Message);
                return 1;
            }
            finally
            {
                if (rgb != null) rgb.Dispose();
                if (bgr != null) bgr.Dispose();
                try { if (model != null) model.Dispose(); } catch { }
                ForceGc();
            }
        }

        private static int RunSingleBatchValidation(string modelPath, string imagePath, int batch)
        {
            Console.WriteLine("==== 单次批量验证 ====");
            Console.WriteLine("model: " + modelPath);
            Console.WriteLine("image: " + imagePath);
            Console.WriteLine("batch: " + batch);

            if (!File.Exists(modelPath))
            {
                Console.WriteLine("模型不存在");
                return 2;
            }
            if (!File.Exists(imagePath))
            {
                Console.WriteLine("图片不存在");
                return 2;
            }

            Model model = null;
            Mat bgr = null;
            Mat rgb = null;
            try
            {
                model = new Model(modelPath, GpuDeviceId, false, false);
                Console.WriteLine("provider=" + model.LoadedDogProvider + ", dll=" + model.LoadedNativeDllName);
                bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (bgr == null || bgr.Empty()) throw new Exception("图像解码失败");
                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

                var list = new List<Mat>();
                for (int i = 0; i < batch; i++) list.Add(rgb);
                var p = new JObject
                {
                    ["threshold"] = 0.5,
                    ["with_mask"] = true,
                    ["batch_size"] = batch
                };
                var r = model.InferBatch(list, p);

                int sampleCount = (r.SampleResults != null) ? r.SampleResults.Count : 0;
                var detCounts = new List<int>();
                if (r.SampleResults != null)
                {
                    foreach (var sr in r.SampleResults)
                    {
                        detCounts.Add((sr.Results != null) ? sr.Results.Count : 0);
                    }
                }

                Console.WriteLine("sample_count: " + sampleCount);
                Console.WriteLine("det_counts: [" + string.Join(", ", detCounts) + "]");

                int nonEmpty = detCounts.Count(x => x > 0);
                Console.WriteLine("non_empty_samples: " + nonEmpty);

                DisposeResultMasks(r);
                if (sampleCount != batch)
                {
                    Console.WriteLine("验证失败：sample 数量与 batch 不一致");
                    return 1;
                }
                if (nonEmpty <= 0)
                {
                    Console.WriteLine("验证失败：所有样本结果为空");
                    return 1;
                }
                Console.WriteLine("验证通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("验证异常: " + ex.Message);
                return 1;
            }
            finally
            {
                if (rgb != null) rgb.Dispose();
                if (bgr != null) bgr.Dispose();
                try { if (model != null) model.Dispose(); } catch { }
                ForceGc();
            }
        }

        private static int RunFlowInstanceSegFilterSelfTest()
        {
            Console.WriteLine("==== Flow 实例分割筛选自测 ====");
            Console.WriteLine("model: " + FlowInstanceSegFilterModelPath);
            Console.WriteLine("image: " + FlowInstanceSegFilterImagePath);

            if (!File.Exists(FlowInstanceSegFilterModelPath))
            {
                Console.WriteLine("模型不存在");
                return 2;
            }
            if (!File.Exists(FlowInstanceSegFilterImagePath))
            {
                Console.WriteLine("图片不存在");
                return 2;
            }

            Model model = null;
            Mat bgr = null;
            Mat rgb = null;
            Utils.CSharpResult result = default(Utils.CSharpResult);
            try
            {
                model = new Model(FlowInstanceSegFilterModelPath, GpuDeviceId, false, false);
                Console.WriteLine("provider=" + model.LoadedDogProvider + ", dll=" + model.LoadedNativeDllName);
                bgr = Cv2.ImRead(FlowInstanceSegFilterImagePath, ImreadModes.Color);
                if (bgr == null || bgr.Empty()) throw new Exception("图像解码失败");
                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

                var p = new JObject
                {
                    ["threshold"] = 0.5,
                    ["with_mask"] = true,
                    ["batch_size"] = 1
                };
                result = model.InferBatch(new List<Mat> { rgb }, p);

                int sampleCount = result.SampleResults != null ? result.SampleResults.Count : 0;
                int objectCount = sampleCount > 0 && result.SampleResults[0].Results != null
                    ? result.SampleResults[0].Results.Count
                    : 0;

                Console.WriteLine("sample_count: " + sampleCount);
                Console.WriteLine("det_counts: [" + objectCount + "]");

                bool ok = true;
                if (sampleCount != 1)
                {
                    Console.WriteLine("验证失败：期望 1 个 sample，实际 " + sampleCount);
                    ok = false;
                }
                if (objectCount != 2)
                {
                    Console.WriteLine("验证失败：期望 2 个目标，实际 " + objectCount);
                    ok = false;
                }

                ok = CheckFlowInstanceObject(result, 0, 211.0, 221.0, 160.0, 186.0) && ok;
                ok = CheckFlowInstanceObject(result, 1, 849.0, 220.0, 161.0, 185.0) && ok;

                if (ok)
                {
                    Console.WriteLine("Flow 实例分割筛选自测通过");
                    return 0;
                }

                Console.WriteLine("Flow 实例分割筛选自测失败");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Flow 实例分割筛选自测异常: " + ex.Message);
                return 1;
            }
            finally
            {
                DisposeResultMasks(result);
                if (rgb != null) rgb.Dispose();
                if (bgr != null) bgr.Dispose();
                try { if (model != null) model.Dispose(); } catch { }
                ForceGc();
            }
        }

        private static bool CheckFlowInstanceObject(Utils.CSharpResult result, int index, double x, double y, double w, double h)
        {
            if (result.SampleResults == null || result.SampleResults.Count == 0 ||
                result.SampleResults[0].Results == null || result.SampleResults[0].Results.Count <= index)
            {
                Console.WriteLine("验证失败：缺少目标 " + index);
                return false;
            }

            var obj = result.SampleResults[0].Results[index];
            Console.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] {1} score={2:F2} bbox=({3:F1}, {4:F1}, {5:F1}, {6:F1}) area={7:F1}",
                    index + 1,
                    obj.CategoryName,
                    obj.Score,
                    obj.Bbox != null && obj.Bbox.Count > 0 ? obj.Bbox[0] : 0.0,
                    obj.Bbox != null && obj.Bbox.Count > 1 ? obj.Bbox[1] : 0.0,
                    obj.Bbox != null && obj.Bbox.Count > 2 ? obj.Bbox[2] : 0.0,
                    obj.Bbox != null && obj.Bbox.Count > 3 ? obj.Bbox[3] : 0.0,
                    obj.Area));

            bool ok = true;
            if (obj.CategoryName != "杯子")
            {
                Console.WriteLine("验证失败：目标 " + index + " 类别错误: " + obj.CategoryName);
                ok = false;
            }
            if (Math.Abs(obj.Score - 1.0f) > 0.01f)
            {
                Console.WriteLine("验证失败：目标 " + index + " 分数错误: " + obj.Score.ToString(CultureInfo.InvariantCulture));
                ok = false;
            }
            if (!obj.WithBbox || obj.Bbox == null || obj.Bbox.Count < 4)
            {
                Console.WriteLine("验证失败：目标 " + index + " 缺少 bbox");
                return false;
            }
            if (Math.Abs(obj.Bbox[0] - x) > 1.0 ||
                Math.Abs(obj.Bbox[1] - y) > 1.0 ||
                Math.Abs(obj.Bbox[2] - w) > 1.0 ||
                Math.Abs(obj.Bbox[3] - h) > 1.0)
            {
                Console.WriteLine("验证失败：目标 " + index + " bbox 错误");
                ok = false;
            }
            return ok;
        }

        private static int RunRectImageCorrectionSelfTest()
        {
            Console.WriteLine("==== 矩形图像矫正自测 ====");

            try
            {
                using (var portrait = CreateIndexedMat(2, 3))
                {
                    var state = new TransformationState(portrait.Width, portrait.Height);
                    var wrap = new ModuleImage(portrait, portrait, state, 7);
                    var module = new RectImageCorrection(
                        41,
                        "矩形图像矫正",
                        new Dictionary<string, object> { { "rotate_direction", "clockwise" } });

                    var output = module.Process(new List<ModuleImage> { wrap }, new JArray { new JObject { ["sample_results"] = new JArray() } });
                    if (output.ImageList.Count != 1)
                    {
                        Console.WriteLine("自测失败：顺时针输出图像数量错误");
                        return 1;
                    }

                    var rotated = output.ImageList[0];
                    if (output.ResultList.Count != 0)
                    {
                        Console.WriteLine("自测失败：结果通道应为空");
                        return 1;
                    }
                    if (!AssertMatShape(rotated.ImageObject, 3, 2, "顺时针尺寸")) return 1;
                    if (!AssertMatPixels(rotated.ImageObject, new byte[,] { { 20, 10, 0 }, { 21, 11, 1 } }, "顺时针像素")) return 1;
                    if (!AssertArrayNear(rotated.TransformState.AffineMatrix2x3, new double[] { 0, -1, 2, 1, 0, 0 }, "顺时针 affine")) return 1;
                    if (!AssertIntArray(rotated.TransformState.OutputSize, new int[] { 3, 2 }, "顺时针 output_size")) return 1;
                    if (rotated.OriginalIndex != 7)
                    {
                        Console.WriteLine("自测失败：OriginalIndex 未保留");
                        return 1;
                    }
                }

                using (var portrait = CreateIndexedMat(2, 3))
                {
                    var wrap = new ModuleImage(portrait, portrait, new TransformationState(portrait.Width, portrait.Height), 0);
                    var module = new RectImageCorrection(
                        42,
                        null,
                        new Dictionary<string, object> { { "rotate_direction", "ccw" } });

                    var output = module.Process(new List<ModuleImage> { wrap }, null);
                    var rotated = output.ImageList[0];
                    if (!AssertMatShape(rotated.ImageObject, 3, 2, "逆时针尺寸")) return 1;
                    if (!AssertMatPixels(rotated.ImageObject, new byte[,] { { 1, 11, 21 }, { 0, 10, 20 } }, "逆时针像素")) return 1;
                    if (!AssertArrayNear(rotated.TransformState.AffineMatrix2x3, new double[] { 0, 1, 0, -1, 0, 1 }, "逆时针 affine")) return 1;
                }

                using (var landscape = CreateIndexedMat(4, 2))
                {
                    var wrap = new ModuleImage(landscape, landscape, new TransformationState(landscape.Width, landscape.Height), 3);
                    var module = new RectImageCorrection(43);
                    var output = module.Process(new List<ModuleImage> { wrap }, null);
                    if (output.ImageList.Count != 1 || !object.ReferenceEquals(output.ImageList[0], wrap))
                    {
                        Console.WriteLine("自测失败：横图应原样透传");
                        return 1;
                    }
                }

                Console.WriteLine("矩形图像矫正自测通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("矩形图像矫正自测异常: " + ex);
                return 1;
            }
        }

        private static Mat CreateIndexedMat(int width, int height)
        {
            var mat = new Mat(height, width, MatType.CV_8UC1);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    mat.Set(y, x, (byte)(y * 10 + x));
                }
            }
            return mat;
        }

        private static bool AssertMatShape(Mat mat, int expectedWidth, int expectedHeight, string label)
        {
            if (mat == null || mat.Empty() || mat.Width != expectedWidth || mat.Height != expectedHeight)
            {
                Console.WriteLine(label + "错误，expected=" + expectedWidth + "x" + expectedHeight
                    + ", actual=" + (mat == null ? "<null>" : mat.Width + "x" + mat.Height));
                return false;
            }
            return true;
        }

        private static bool AssertMatPixels(Mat mat, byte[,] expected, string label)
        {
            int rows = expected.GetLength(0);
            int cols = expected.GetLength(1);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    byte actual = mat.At<byte>(y, x);
                    if (actual != expected[y, x])
                    {
                        Console.WriteLine(label + "错误，位置(" + y + "," + x + ") expected="
                            + expected[y, x] + ", actual=" + actual);
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool AssertArrayNear(double[] actual, double[] expected, string label)
        {
            if (actual == null || actual.Length < expected.Length)
            {
                Console.WriteLine(label + "缺失");
                return false;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (Math.Abs(actual[i] - expected[i]) > 1e-9)
                {
                    Console.WriteLine(label + "错误，index=" + i + ", expected=" + expected[i] + ", actual=" + actual[i]);
                    return false;
                }
            }
            return true;
        }

        private static bool AssertIntArray(int[] actual, int[] expected, string label)
        {
            if (actual == null || actual.Length < expected.Length)
            {
                Console.WriteLine(label + "缺失");
                return false;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    Console.WriteLine(label + "错误，index=" + i + ", expected=" + expected[i] + ", actual=" + actual[i]);
                    return false;
                }
            }
            return true;
        }

        private static CaseRow RunCase(string modelPath, string imagePath)
        {
            var row = new CaseRow
            {
                ModelName = Path.GetFileName(modelPath),
                LoadStatus = "失败",
                InferStatus = "失败",
                CategoryList = "-",
                SpeedText = "-",
                BatchText = "-"
            };

            var memBefore = MemorySnapshot.Capture();
            var swLoad = Stopwatch.StartNew();
            Model model = null;
            try
            {
                model = new Model(modelPath, GpuDeviceId, false, false);
                row.LoadStatus = model != null && model.modelIndex != -1 ? "成功" : "失败";
            }
            catch (Exception ex)
            {
                row.LoadStatus = "失败";
                row.CategoryList = "错误:" + Trim(ex.Message);
            }
            swLoad.Stop();
            var memAfter = MemorySnapshot.Capture();
            row.LoadStatus = row.LoadStatus + "(" + swLoad.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture) + "ms,Δ"
                + (memAfter.PrivateMb - memBefore.PrivateMb).ToString("F2", CultureInfo.InvariantCulture) + "MB"
                + (model != null && model.modelIndex != -1 ? ",provider=" + model.LoadedDogProvider + ",dll=" + model.LoadedNativeDllName : "")
                + ")";

            if (model == null || model.modelIndex == -1)
            {
                return row;
            }

            Mat bgr = null;
            Mat rgb = null;
            try
            {
                bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (bgr.Empty()) throw new Exception("图像解码失败");
                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

                var p = new JObject { ["threshold"] = 0.05, ["with_mask"] = true };
                try
                {
                    var r = model.InferBatch(new List<Mat> { rgb }, p);
                    row.InferStatus = (r.SampleResults != null && r.SampleResults.Count > 0) ? "成功" : "失败";
                    row.CategoryList = BuildCategoryList(r);
                    if (string.IsNullOrWhiteSpace(row.CategoryList)) row.CategoryList = "(空)";
                    DisposeResultMasks(r);
                }
                catch (Exception ex)
                {
                    row.InferStatus = "失败";
                    row.CategoryList = "错误:" + Trim(ex.Message);
                }

                if (TestSpeed)
                {
                    var speed = RunSpeedTest(model, rgb, 1, false);
                    row.SpeedText = speed.Supported
                        ? ("均速 " + speed.Fps.ToString("F2", CultureInfo.InvariantCulture) + " 张/秒")
                        : "失败";

                    var batch = RunSpeedTest(model, rgb, FixedBatchSize, true);
                    row.BatchText = batch.Supported
                        ? ("均速 " + batch.Fps.ToString("F2", CultureInfo.InvariantCulture) + " 张/秒")
                        : "N/A";
                }
                else
                {
                    row.SpeedText = "-";
                    row.BatchText = "-";
                }
            }
            catch (Exception ex)
            {
                row.InferStatus = "失败";
                row.CategoryList = "错误:" + Trim(ex.Message);
            }
            finally
            {
                if (rgb != null) rgb.Dispose();
                if (bgr != null) bgr.Dispose();
            }

            try { model.Dispose(); } catch { }
            ForceGc();
            return row;
        }

        private static bool IsInstanceSegModel(string modelPath)
        {
            return Path.GetFileName(modelPath).Contains("实例分割");
        }

        private static double RunLoadFreeLeak(string modelPath, int deviceId)
        {
            ForceGc();
            var baseline = MemorySnapshot.Capture().PrivateMb;
            for (int i = 0; i < LeakLoopCount; i++)
            {
                Model m = null;
                try
                {
                    m = new Model(modelPath, deviceId, false, false);
                }
                finally
                {
                    try { if (m != null) m.Dispose(); } catch { }
                    ForceGc();
                }
            }
            return MemorySnapshot.Capture().PrivateMb - baseline;
        }

        private static double RunInferLeak3s(string modelPath, string imagePath, int deviceId)
        {
            ForceGc();
            var before = MemorySnapshot.Capture().PrivateMb;
            Model model = null;
            Mat bgr = null;
            Mat rgb = null;
            try
            {
                model = new Model(modelPath, deviceId, false, false);
                bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

                var p = new JObject { ["threshold"] = 0.05, ["with_mask"] = true };
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < SpeedWindowSeconds)
                {
                    var r = model.InferBatch(new List<Mat> { rgb }, p);
                    DisposeResultMasks(r);
                }
            }
            catch { }
            finally
            {
                if (rgb != null) rgb.Dispose();
                if (bgr != null) bgr.Dispose();
                try { if (model != null) model.Dispose(); } catch { }
                ForceGc();
            }
            return MemorySnapshot.Capture().PrivateMb - before;
        }

        private static SpeedResult RunSpeedTest(Model model, Mat rgb, int batch, bool allowNa)
        {
            try
            {
                var p = new JObject { ["threshold"] = 0.05, ["with_mask"] = true };
                var list = new List<Mat>();
                for (int i = 0; i < batch; i++) list.Add(rgb);
                for (int i = 0; i < 2; i++)
                {
                    var warm = model.InferBatch(list, p);
                    DisposeResultMasks(warm);
                }
                long inferCount = 0;
                var total = Stopwatch.StartNew();
                while (total.Elapsed.TotalSeconds < SpeedWindowSeconds)
                {
                    var r = model.InferBatch(list, p);
                    DisposeResultMasks(r);
                    inferCount++;
                }
                if (inferCount == 0) return allowNa ? SpeedResult.Na() : new SpeedResult(0, false);
                double fps = inferCount * batch / Math.Max(0.001, total.Elapsed.TotalSeconds);
                return new SpeedResult(fps, true);
            }
            catch
            {
                return allowNa ? SpeedResult.Na() : new SpeedResult(0, false);
            }
        }

        private static string BuildCategoryList(Utils.CSharpResult result)
        {
            const int maxShowCount = 20;
            if (result.SampleResults == null || result.SampleResults.Count == 0) return "";
            var first = result.SampleResults[0];
            if (first.Results == null || first.Results.Count == 0) return "";
            var all = first.Results.Select(r => string.IsNullOrWhiteSpace(r.CategoryName) ? "unknown" : r.CategoryName).ToList();
            var shown = all.Take(maxShowCount);
            string text = string.Join("，", shown);
            if (all.Count > maxShowCount)
            {
                text += " ...(共" + all.Count + "个)";
            }
            return text;
        }

        private static void DisposeResultMasks(Utils.CSharpResult result)
        {
            if (result.SampleResults == null) return;
            foreach (var sr in result.SampleResults)
            {
                if (sr.Results == null) continue;
                foreach (var obj in sr.Results)
                {
                    try
                    {
                        if (obj.Mask != null) obj.Mask.Dispose();
                    }
                    catch { }
                }
            }
        }

        private static void PrintHeader()
        {
            Console.WriteLine("| 模型 | 加载 | 推理 | 类别列表 | 3秒速度 | Batch速度 |");
            Console.WriteLine("|---|---|---|---|---|---|");
        }

        private static void PrintRow(string model, string load, string infer, string cats, string speed, string batch)
        {
            Console.WriteLine("| " + Safe(model) + " | " + Safe(load) + " | " + Safe(infer) + " | " + Safe(cats) + " | " + Safe(speed) + " | " + Safe(batch) + " |");
        }

        private static void PrintFooter()
        {
            Console.WriteLine();
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 64 ? s.Substring(0, 64) + "..." : s;
        }

        private static void ForceGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static bool IsFlowModelPath(string modelPath)
        {
            string ext = Path.GetExtension(modelPath) ?? string.Empty;
            return ext.Equals(".dvst", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".dvso", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".dvsp", StringComparison.OrdinalIgnoreCase);
        }

        private static int ParsePositiveIntArg(string[] args, int index, int defaultValue)
        {
            if (args == null || index < 0 || index >= args.Length) return defaultValue;
            if (int.TryParse(args[index], out int value) && value > 0) return value;
            return defaultValue;
        }

        private static int RunUsLagSelfTest()
        {
            Console.WriteLine("==== US 滞后一帧自测 ====");
            Console.WriteLine("model: " + UsLagModelPath);
            Console.WriteLine("image_1: " + UsLagImagePath1);
            Console.WriteLine("image_2: " + UsLagImagePath2);

            string[] requiredFiles =
            {
                UsLagModelPath,
                UsLagImagePath1,
                UsLagImagePath2
            };
            for (int i = 0; i < requiredFiles.Length; i++)
            {
                if (!File.Exists(requiredFiles[i]))
                {
                    Console.WriteLine("文件不存在: " + requiredFiles[i]);
                    return 2;
                }
            }

            int exitCode = 1;
            Exception threadException = null;
            var thread = new Thread(() =>
            {
                try
                {
                    exitCode = ExecuteUsLagSelfTest();
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (threadException != null)
            {
                Console.WriteLine("US 滞后一帧自测异常: " + threadException);
                return 1;
            }

            return exitCode;
        }

        private static int ExecuteUsLagSelfTest()
        {
            string[] sequence =
            {
                UsLagImagePath1,
                UsLagImagePath2,
                UsLagImagePath1,
                UsLagImagePath2
            };

            List<string> nativeSigs;
            List<string> csharpSigs;
            int modelCheckCode = RunUsLagModelLayerCheck(sequence, out nativeSigs, out csharpSigs);
            if (modelCheckCode != 0)
            {
                return modelCheckCode;
            }

            List<string> demoViewerSigs;
            List<string> demoJsonSigs;
            int demoCheckCode = RunUsLagDlcvDemoCheck(sequence, out demoViewerSigs, out demoJsonSigs);
            if (demoCheckCode != 0)
            {
                return demoCheckCode;
            }

            int nativeLagHits = CountCrossImageRepeat(sequence, nativeSigs);
            int csharpLagHits = CountCrossImageRepeat(sequence, csharpSigs);
            int viewerLagHits = CountCrossImageRepeat(sequence, demoViewerSigs);
            int jsonLagHits = CountCrossImageRepeat(sequence, demoJsonSigs);
            int nativeSameImageMismatch = CountSameImageMismatch(sequence, nativeSigs);
            int csharpSameImageMismatch = CountSameImageMismatch(sequence, csharpSigs);
            int viewerSameImageMismatch = CountSameImageMismatch(sequence, demoViewerSigs);
            int jsonSameImageMismatch = CountSameImageMismatch(sequence, demoJsonSigs);

            Console.WriteLine("native_lag_hits: " + nativeLagHits);
            Console.WriteLine("csharp_lag_hits: " + csharpLagHits);
            Console.WriteLine("demo_viewer_lag_hits: " + viewerLagHits);
            Console.WriteLine("demo_json_lag_hits: " + jsonLagHits);
            Console.WriteLine("native_same_image_mismatch: " + nativeSameImageMismatch);
            Console.WriteLine("csharp_same_image_mismatch: " + csharpSameImageMismatch);
            Console.WriteLine("demo_viewer_same_image_mismatch: " + viewerSameImageMismatch);
            Console.WriteLine("demo_json_same_image_mismatch: " + jsonSameImageMismatch);

            if (nativeLagHits > 0 || csharpLagHits > 0 || viewerLagHits > 0 || jsonLagHits > 0 ||
                nativeSameImageMismatch > 0 || csharpSameImageMismatch > 0 ||
                viewerSameImageMismatch > 0 || jsonSameImageMismatch > 0)
            {
                Console.WriteLine("US 滞后一帧自测失败");
                return 1;
            }

            Console.WriteLine("US 滞后一帧自测通过");
            return 0;
        }

        private static int RunUsLagModelLayerCheck(string[] sequence, out List<string> nativeSigs, out List<string> csharpSigs)
        {
            nativeSigs = new List<string>();
            csharpSigs = new List<string>();

            Model model = null;
            try
            {
                model = new Model(UsLagModelPath, GpuDeviceId, false, false);
                JObject inferParams = new JObject
                {
                    ["threshold"] = 0.5,
                    ["with_mask"] = true,
                    ["batch_size"] = 1
                };

                for (int i = 0; i < sequence.Length; i++)
                {
                    string imagePath = sequence[i];
                    using (var bgr = Cv2.ImRead(imagePath, ImreadModes.Unchanged))
                    {
                        if (bgr == null || bgr.Empty())
                        {
                            throw new Exception("图像解码失败: " + imagePath);
                        }

                        using (var inferInput = PrepareDemo2ExpectedInput(bgr))
                        {
                            var tuple = model.InferInternal(new List<Mat> { inferInput }, inferParams);
                            try
                            {
                                string nativeSig = BuildNativeResultSignature(tuple.Item1 as JObject);
                                nativeSigs.Add(nativeSig);

                                Utils.CSharpResult parsed = model.ParseToStructResult(tuple.Item1 as JObject);
                                string csharpSig = BuildCSharpResultArraySignature(parsed);
                                csharpSigs.Add(csharpSig);

                                Console.WriteLine(
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "round={0}, image={1}",
                                        i + 1,
                                        Path.GetFileName(imagePath)));
                                Console.WriteLine("  native_sig: " + nativeSig);
                                Console.WriteLine("  csharp_sig: " + csharpSig);

                                DisposeResultMasks(parsed);
                            }
                            finally
                            {
                                if (tuple.Item2 != IntPtr.Zero)
                                {
                                    DllLoader.Instance.dlcv_free_model_result(tuple.Item2);
                                }
                            }
                        }
                    }
                }

                int mismatch = 0;
                for (int i = 0; i < nativeSigs.Count && i < csharpSigs.Count; i++)
                {
                    if (!string.Equals(nativeSigs[i], csharpSigs[i], StringComparison.Ordinal))
                    {
                        mismatch++;
                    }
                }
                Console.WriteLine("native_vs_csharp_mismatch_rounds: " + mismatch);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("模型层对比异常: " + ex.Message);
                return 1;
            }
            finally
            {
                try { if (model != null) model.Dispose(); } catch { }
                ForceGc();
            }
        }

        private static int RunUsLagDlcvDemoCheck(string[] sequence, out List<string> viewerSigs, out List<string> jsonSigs)
        {
            viewerSigs = new List<string>();
            jsonSigs = new List<string>();

            string demoAssemblyPath = ResolveDemoAssemblyPath();
            if (!File.Exists(demoAssemblyPath))
            {
                Console.WriteLine("未找到 DlcvDemo 可执行文件: " + demoAssemblyPath);
                Console.WriteLine("请先构建 DlcvDemo.csproj。");
                return 2;
            }

            Assembly demoAssembly = Assembly.LoadFrom(demoAssemblyPath);
            Type formType = demoAssembly.GetType("DlcvDemo.Form1", throwOnError: true);
            MethodInfo inferClick = formType.GetMethod("button_infer_Click", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo inferJsonClick = formType.GetMethod("button_infer_json_Click", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo modelField = formType.GetField("model", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo imagePathField = formType.GetField("image_path", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo richTextField = formType.GetField("richTextBox1", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo imagePanelField = formType.GetField("imagePanel1", BindingFlags.NonPublic | BindingFlags.Instance);
            if (inferClick == null || inferJsonClick == null ||
                modelField == null || imagePathField == null || richTextField == null || imagePanelField == null)
            {
                Console.WriteLine("DlcvDemo 关键成员反射失败");
                return 1;
            }

            object form = null;
            Model model = null;
            try
            {
                form = Activator.CreateInstance(formType);
                model = new Model(UsLagModelPath, GpuDeviceId, false, false);
                modelField.SetValue(form, model);

                object richTextObj = richTextField.GetValue(form);
                object imagePanelObj = imagePanelField.GetValue(form);
                if (richTextObj == null || imagePanelObj == null)
                {
                    Console.WriteLine("DlcvDemo 控件实例无效");
                    return 1;
                }
                PropertyInfo richTextProperty = richTextObj.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                if (richTextProperty == null)
                {
                    Console.WriteLine("DlcvDemo richTextBox1 缺少 Text 属性");
                    return 1;
                }

                for (int i = 0; i < sequence.Length; i++)
                {
                    string imagePath = sequence[i];
                    imagePathField.SetValue(form, imagePath);

                    inferClick.Invoke(form, new object[] { null, EventArgs.Empty });
                    string inferText = Convert.ToString(richTextProperty.GetValue(richTextObj, null), CultureInfo.InvariantCulture) ?? string.Empty;
                    string viewerSig = ExtractImagePanelCurrentResultSignature(imagePanelObj);
                    viewerSigs.Add(viewerSig);

                    inferJsonClick.Invoke(form, new object[] { null, EventArgs.Empty });
                    string jsonText = Convert.ToString(richTextProperty.GetValue(richTextObj, null), CultureInfo.InvariantCulture) ?? string.Empty;
                    string jsonSig = BuildJsonTextSignature(jsonText);
                    jsonSigs.Add(jsonSig);

                    Console.WriteLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "demo_round={0}, image={1}, text_contains_path={2}",
                            i + 1,
                            Path.GetFileName(imagePath),
                            inferText.IndexOf(imagePath, StringComparison.OrdinalIgnoreCase) >= 0));
                    Console.WriteLine("  demo_viewer_sig: " + viewerSig);
                    Console.WriteLine("  demo_json_sig: " + jsonSig);
                }

                return 0;
            }
            catch (TargetInvocationException ex)
            {
                string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                Console.WriteLine("DlcvDemo 链路对比异常: " + msg);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("DlcvDemo 链路对比异常: " + ex.Message);
                return 1;
            }
            finally
            {
                try { if (model != null) model.Dispose(); } catch { }
                TryDispose(form);
                ForceGc();
            }
        }

        private static int CountCrossImageRepeat(string[] sequence, List<string> signatures)
        {
            int hits = 0;
            if (sequence == null || signatures == null) return 0;
            int n = Math.Min(sequence.Length, signatures.Count);
            for (int i = 1; i < n; i++)
            {
                bool imageChanged = !string.Equals(sequence[i], sequence[i - 1], StringComparison.OrdinalIgnoreCase);
                bool resultRepeated = string.Equals(signatures[i], signatures[i - 1], StringComparison.Ordinal);
                if (imageChanged && resultRepeated)
                {
                    hits++;
                }
            }
            return hits;
        }

        private static int CountSameImageMismatch(string[] sequence, List<string> signatures)
        {
            if (sequence == null || signatures == null) return 0;
            int mismatch = 0;
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int n = Math.Min(sequence.Length, signatures.Count);
            for (int i = 0; i < n; i++)
            {
                string imagePath = sequence[i] ?? string.Empty;
                string signature = signatures[i] ?? string.Empty;
                if (!seen.TryGetValue(imagePath, out string firstSignature))
                {
                    seen[imagePath] = signature;
                    continue;
                }
                if (!string.Equals(firstSignature, signature, StringComparison.Ordinal))
                {
                    mismatch++;
                }
            }
            return mismatch;
        }

        private static string BuildNativeResultSignature(JObject inferResult)
        {
            if (inferResult == null) return string.Empty;
            var sampleResults = inferResult["sample_results"] as JArray;
            if (sampleResults == null || sampleResults.Count == 0) return string.Empty;
            var firstSample = sampleResults[0] as JObject;
            if (firstSample == null) return string.Empty;
            var results = firstSample["results"] as JArray;
            return BuildJsonResultArraySignature(results);
        }

        private static string BuildCSharpResultArraySignature(Utils.CSharpResult result)
        {
            if (result.SampleResults == null || result.SampleResults.Count == 0) return string.Empty;
            var first = result.SampleResults[0];
            if (first.Results == null || first.Results.Count == 0) return string.Empty;

            var items = new List<string>(first.Results.Count);
            for (int i = 0; i < first.Results.Count; i++)
            {
                var obj = first.Results[i];
                items.Add(FormatResultSignatureItem(
                    obj.CategoryId,
                    obj.CategoryName ?? string.Empty,
                    obj.Score,
                    obj.Bbox,
                    obj.WithAngle,
                    obj.Angle));
            }
            items.Sort(StringComparer.Ordinal);
            return string.Join(";", items);
        }

        private static string BuildJsonTextSignature(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            try
            {
                JToken token = JToken.Parse(text);
                if (token is JArray array)
                {
                    return BuildJsonResultArraySignature(array);
                }

                var obj = token as JObject;
                if (obj != null && obj["sample_results"] is JArray sampleResults && sampleResults.Count > 0)
                {
                    var firstSample = sampleResults[0] as JObject;
                    if (firstSample != null)
                    {
                        return BuildJsonResultArraySignature(firstSample["results"] as JArray);
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string BuildJsonResultArraySignature(JArray array)
        {
            if (array == null || array.Count == 0) return string.Empty;

            var items = new List<string>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                var obj = array[i] as JObject;
                if (obj == null) continue;
                int categoryId = obj["category_id"] != null ? obj["category_id"].Value<int>() : 0;
                string categoryName = obj["category_name"] != null ? obj["category_name"].Value<string>() : string.Empty;
                double score = obj["score"] != null ? obj["score"].Value<double>() : 0.0;
                List<double> bbox = ParseBbox(obj["bbox"]);
                bool withAngle = obj["with_angle"] != null && obj["with_angle"].Value<bool>();
                double angle = obj["angle"] != null ? obj["angle"].Value<double>() : -100.0;
                items.Add(FormatResultSignatureItem(categoryId, categoryName, score, bbox, withAngle, angle));
            }

            items.Sort(StringComparer.Ordinal);
            return string.Join(";", items);
        }

        private static List<double> ParseBbox(JToken bboxToken)
        {
            var bbox = new List<double>();
            var arr = bboxToken as JArray;
            if (arr == null) return bbox;
            for (int i = 0; i < arr.Count; i++)
            {
                try
                {
                    bbox.Add(arr[i].Value<double>());
                }
                catch
                {
                    bbox.Add(0.0);
                }
            }
            return bbox;
        }

        private static string FormatResultSignatureItem(int categoryId, string categoryName, double score, IList<double> bbox, bool withAngle, double angle)
        {
            var bboxParts = new List<string>();
            if (bbox != null)
            {
                for (int i = 0; i < bbox.Count; i++)
                {
                    bboxParts.Add(bbox[i].ToString("F3", CultureInfo.InvariantCulture));
                }
            }
            string bboxSig = string.Join(",", bboxParts);
            double usedAngle = withAngle ? angle : -100.0;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1:F4}|{2}|{3}|{4:F4}",
                categoryId,
                score,
                categoryName ?? string.Empty,
                bboxSig,
                usedAngle);
        }

        private static string ExtractImagePanelCurrentResultSignature(object imagePanelObj)
        {
            if (imagePanelObj == null) return string.Empty;
            Type panelType = imagePanelObj.GetType();
            FieldInfo currentResultsField = panelType.GetField("currentResults", BindingFlags.Public | BindingFlags.Instance);
            if (currentResultsField == null) return string.Empty;
            object boxed = currentResultsField.GetValue(imagePanelObj);
            if (boxed is Utils.CSharpResult)
            {
                return BuildCSharpResultArraySignature((Utils.CSharpResult)boxed);
            }
            return string.Empty;
        }

        private static string ResolveDemoAssemblyPath()
        {
            string repoRoot = ResolveRepoRoot();
            string[] candidates =
            {
                Path.Combine(repoRoot, "DlcvDemo", "bin", "C# 测试程序.exe"),
                Path.Combine(repoRoot, "DlcvDemo", "bin", "x64", "Debug", "C# 测试程序.exe"),
                Path.Combine(repoRoot, "DlcvDemo", "bin", "Debug", "C# 测试程序.exe")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return candidates[0];
        }

        private static int RunModelChannelOrderSelfTest()
        {
            Console.WriteLine("==== Model 通道顺序自测 ====");

            var helper = typeof(Model).GetMethod("PrepareInferImage", BindingFlags.NonPublic | BindingFlags.Static);
            if (helper == null)
            {
                Console.WriteLine("未找到 PrepareInferImage");
                return 1;
            }

            var disposables = new List<Mat>();
            using (var src = new Mat(1, 1, MatType.CV_8UC3, new Scalar(11, 22, 33)))
            {
                try
                {
                    var prepared = helper.Invoke(null, new object[] { src, 3, disposables }) as Mat;
                    if (prepared == null || prepared.Empty())
                    {
                        Console.WriteLine("PrepareInferImage 返回空图");
                        return 1;
                    }

                    Vec3b inputPixel = src.At<Vec3b>(0, 0);
                    Vec3b outputPixel = prepared.At<Vec3b>(0, 0);

                    Console.WriteLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "input_pixel: [{0}, {1}, {2}]",
                            inputPixel.Item0,
                            inputPixel.Item1,
                            inputPixel.Item2));
                    Console.WriteLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "output_pixel: [{0}, {1}, {2}]",
                            outputPixel.Item0,
                            outputPixel.Item1,
                            outputPixel.Item2));

                    if (inputPixel.Item0 != outputPixel.Item0 ||
                        inputPixel.Item1 != outputPixel.Item1 ||
                        inputPixel.Item2 != outputPixel.Item2)
                    {
                        Console.WriteLine("自测失败：PrepareInferImage 改变了三通道输入顺序");
                        return 1;
                    }

                    Console.WriteLine("Model 通道顺序自测通过");
                    return 0;
                }
                catch (TargetInvocationException ex)
                {
                    string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    Console.WriteLine("Model 通道顺序自测异常: " + msg);
                    return 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Model 通道顺序自测异常: " + ex.Message);
                    return 1;
                }
                finally
                {
                    for (int i = 0; i < disposables.Count; i++)
                    {
                        try { disposables[i]?.Dispose(); } catch { }
                    }
                }
            }
        }

        private static int RunDvsRgbSelfTest(string[] args)
        {
            if (args == null || args.Length < 3)
            {
                Console.WriteLine("用法: DlcvCSharpTest dvs-rgb-selftest <modelPath> <imagePath>");
                return 2;
            }

            string modelPath = args[1];
            string imagePath = args[2];

            Console.WriteLine("==== DVS RGB 自测 ====");
            Console.WriteLine("model: " + modelPath);
            Console.WriteLine("image: " + imagePath);

            if (!File.Exists(modelPath))
            {
                Console.WriteLine("模型不存在");
                return 2;
            }
            if (!File.Exists(imagePath))
            {
                Console.WriteLine("图片不存在");
                return 2;
            }

            Model model = null;
            Mat bgr = null;
            Mat rgb = null;
            DvsModel directModel = null;
            Utils.CSharpResult wrappedResult = default(Utils.CSharpResult);
            Utils.CSharpResult directResult = default(Utils.CSharpResult);
            try
            {
                model = new Model(modelPath, GpuDeviceId, false, false);
                directModel = new DvsModel();
                directModel.Load(modelPath, GpuDeviceId);
                bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (bgr == null || bgr.Empty()) throw new Exception("图像解码失败");

                var inferParams = new JObject
                {
                    ["threshold"] = 0.5,
                    ["with_mask"] = true,
                    ["batch_size"] = 1
                };

                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

                wrappedResult = model.InferBatch(new List<Mat> { rgb }, inferParams);
                directResult = directModel.InferBatch(new List<Mat> { rgb }, inferParams);

                string wrappedSig = BuildResultSignature(wrappedResult);
                string directSig = BuildResultSignature(directResult);

                Console.WriteLine("wrapped_signature: " + wrappedSig);
                Console.WriteLine("direct_signature: " + directSig);

                if (!string.Equals(wrappedSig, directSig, StringComparison.Ordinal))
                {
                    Console.WriteLine("自测失败：Model(.dvst) 与 DvsModel 直连 flow 的 RGB 结果不一致");
                    return 1;
                }

                Console.WriteLine("DVS RGB 自测通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("DVS RGB 自测异常: " + ex.Message);
                return 1;
            }
            finally
            {
                DisposeResultMasks(wrappedResult);
                DisposeResultMasks(directResult);
                if (rgb != null) rgb.Dispose();
                if (bgr != null) bgr.Dispose();
                try { if (directModel != null) directModel.Dispose(); } catch { }
                try { if (model != null) model.Dispose(); } catch { }
                ForceGc();
            }
        }

        private static int RunMaskToRBoxSelfTest()
        {
            Console.WriteLine("==== mask_to_rbox 自测 ====");

            using (var mask = new Mat(64, 64, MatType.CV_8UC1, Scalar.Black))
            {
                Cv2.Rectangle(mask, new Rect(6, 12, 12, 28), Scalar.White, -1);
                Cv2.Rectangle(mask, new Rect(28, 20, 20, 12), Scalar.White, -1);

                var maskInfo = MaskRleUtils.MatToMaskInfo(mask);
                var bbox = new JArray(100.0, 200.0, mask.Cols, mask.Rows);

                RotatedRect expected;
                using (var baselineMask = MaskRleUtils.MaskInfoToMat(maskInfo))
                using (var points = new Mat())
                {
                    Cv2.FindNonZero(baselineMask, points);
                    if (points.Empty())
                    {
                        Console.WriteLine("基线算法未找到非零点");
                        return 1;
                    }
                    expected = Cv2.MinAreaRect(points);
                }

                RotatedRect actual;
                if (!MaskRleUtils.TryComputeMinAreaRectFromMaskInfo(maskInfo, out actual))
                {
                    Console.WriteLine("优化算法未得到旋转框");
                    return 1;
                }

                AssertNear(expected.Center.X, actual.Center.X, 0.01, "center.x");
                AssertNear(expected.Center.Y, actual.Center.Y, 0.01, "center.y");
                AssertNear(expected.Size.Width, actual.Size.Width, 0.01, "size.width");
                AssertNear(expected.Size.Height, actual.Size.Height, 0.01, "size.height");
                AssertNear(NormalizeAngleDeg(expected.Angle), NormalizeAngleDeg(actual.Angle), 0.01, "angle");

                var module = new MaskToRBox(39);
                var resultList = new JArray
                {
                    new JObject
                    {
                        ["type"] = "local",
                        ["index"] = 0,
                        ["origin_index"] = 0,
                        ["sample_results"] = new JArray
                        {
                            new JObject
                            {
                                ["bbox"] = bbox,
                                ["score"] = 0.99,
                                ["category_name"] = "demo",
                                ["mask_rle"] = maskInfo
                            }
                        }
                    }
                };

                var output = module.Process(new List<ModuleImage>(), resultList);
                var det = (((output.ResultList[0] as JObject)?["sample_results"] as JArray)?[0]) as JObject;
                if (det == null)
                {
                    Console.WriteLine("模块输出为空");
                    return 1;
                }
                if (det["mask_rle"] != null)
                {
                    Console.WriteLine("mask_rle 未被移除");
                    return 1;
                }
                if ((string)det["category_name"] != "demo")
                {
                    Console.WriteLine("category_name 未保留");
                    return 1;
                }
                if (Math.Abs(det["score"].Value<double>() - 0.99) > 1e-6)
                {
                    Console.WriteLine("score 未保留");
                    return 1;
                }

                Console.WriteLine("mask_to_rbox 自测通过");
                return 0;
            }
        }

        private static int RunDemo2RgbSelfTest(string[] args)
        {
            if (args == null || args.Length < 5)
            {
                Console.WriteLine("用法: DlcvCSharpTest demo2-rgb-selftest <extractModelPath> <componentModelPath> <icModelPath> <imagePath>");
                return 2;
            }

            string extractModelPath = args[1];
            string componentModelPath = args[2];
            string icModelPath = args[3];
            string imagePath = args[4];

            Console.WriteLine("==== Demo2 RGB 闭环自测 ====");
            Console.WriteLine("extract_model: " + extractModelPath);
            Console.WriteLine("component_model: " + componentModelPath);
            Console.WriteLine("ic_model: " + icModelPath);
            Console.WriteLine("image: " + imagePath);

            string[] requiredFiles =
            {
                extractModelPath,
                componentModelPath,
                icModelPath,
                imagePath
            };
            for (int i = 0; i < requiredFiles.Length; i++)
            {
                if (!File.Exists(requiredFiles[i]))
                {
                    Console.WriteLine("文件不存在: " + requiredFiles[i]);
                    return 2;
                }
            }

            int exitCode = 1;
            Exception threadException = null;
            var thread = new Thread(() =>
            {
                try
                {
                    exitCode = ExecuteDemo2RgbSelfTest(extractModelPath, componentModelPath, icModelPath, imagePath);
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (threadException != null)
            {
                Console.WriteLine("Demo2 RGB 自测异常: " + threadException);
                return 1;
            }

            return exitCode;
        }

        private static int ExecuteDemo2RgbSelfTest(
            string extractModelPath,
            string componentModelPath,
            string icModelPath,
            string imagePath)
        {
            string demo2AssemblyPath = ResolveDemo2AssemblyPath();
            if (!File.Exists(demo2AssemblyPath))
            {
                Console.WriteLine("未找到 Demo2 可执行文件: " + demo2AssemblyPath);
                Console.WriteLine("请先构建 DlcvDemo2.csproj。");
                return 2;
            }

            Assembly demo2Assembly = Assembly.LoadFrom(demo2AssemblyPath);
            Type formType = demo2Assembly.GetType("DlcvDemo2.Form1", throwOnError: true);
            Type configType = formType.GetNestedType("SlidingWindowConfig", BindingFlags.NonPublic);
            if (configType == null)
            {
                Console.WriteLine("未找到 Demo2.SlidingWindowConfig");
                return 1;
            }

            MethodInfo tryLoadImageForInfer = formType.GetMethod("TryLoadImageForInfer", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo runPipeline = formType.GetMethod("RunPipeline", BindingFlags.NonPublic | BindingFlags.Instance);
            if (tryLoadImageForInfer == null || runPipeline == null)
            {
                Console.WriteLine("未找到 Demo2 关键私有方法");
                return 1;
            }

            FieldInfo extractField = formType.GetField("extractModel", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo componentField = formType.GetField("componentDetectModel", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo icField = formType.GetField("icDetectModel", BindingFlags.NonPublic | BindingFlags.Instance);
            if (extractField == null || componentField == null || icField == null)
            {
                Console.WriteLine("未找到 Demo2 模型字段");
                return 1;
            }

            object form = null;
            object extractModel = null;
            object componentModel = null;
            object icModel = null;
            Mat imageBgr = null;
            Mat entryRgb = null;
            Mat manualRgb = null;
            object entryRunResult = null;
            object manualRunResult = null;
            object rawBgrRunResult = null;

            try
            {
                form = Activator.CreateInstance(formType);

                extractModel = Activator.CreateInstance(extractField.FieldType, new object[] { extractModelPath, GpuDeviceId, false, false });
                componentModel = Activator.CreateInstance(componentField.FieldType, new object[] { componentModelPath, GpuDeviceId, false, false });
                icModel = Activator.CreateInstance(icField.FieldType, new object[] { icModelPath, GpuDeviceId, false, false });

                extractField.SetValue(form, extractModel);
                componentField.SetValue(form, componentModel);
                icField.SetValue(form, icModel);

                object[] loadArgs = { imagePath, null, null, string.Empty };
                bool loaded = (bool)tryLoadImageForInfer.Invoke(null, loadArgs);
                if (!loaded)
                {
                    Console.WriteLine("TryLoadImageForInfer 失败: " + Convert.ToString(loadArgs[3], CultureInfo.InvariantCulture));
                    return 1;
                }

                imageBgr = loadArgs[1] as Mat;
                entryRgb = loadArgs[2] as Mat;
                if (imageBgr == null || imageBgr.Empty() || entryRgb == null || entryRgb.Empty())
                {
                    Console.WriteLine("Demo2 加载图片后得到空图");
                    return 1;
                }

                manualRgb = PrepareDemo2ExpectedInput(imageBgr);
                object config = CreateDemo2SlidingWindowConfig(configType, 2560, 2560, 1024, 1024);

                entryRunResult = runPipeline.Invoke(form, new object[] { entryRgb, config, null });
                manualRunResult = runPipeline.Invoke(form, new object[] { manualRgb, config, null });
                rawBgrRunResult = runPipeline.Invoke(form, new object[] { imageBgr, config, null });

                string entrySig = BuildDemo2PipelineSignature(entryRunResult);
                string manualSig = BuildDemo2PipelineSignature(manualRunResult);
                string rawBgrSig = BuildDemo2PipelineSignature(rawBgrRunResult);

                Console.WriteLine("entry_rgb_signature: " + entrySig);
                Console.WriteLine("manual_rgb_signature: " + manualSig);
                Console.WriteLine("raw_bgr_signature: " + rawBgrSig);

                if (!string.Equals(entrySig, manualSig, StringComparison.Ordinal))
                {
                    Console.WriteLine("自测失败：Demo2 实际入口结果与手工 RGB 结果不一致");
                    return 1;
                }

                if (string.Equals(entrySig, rawBgrSig, StringComparison.Ordinal))
                {
                    Console.WriteLine("自测失败：当前样例未把 RGB 与 BGR 路径区分开，无法形成有效闭环");
                    return 1;
                }

                Console.WriteLine("Demo2 RGB 闭环自测通过");
                return 0;
            }
            catch (TargetInvocationException ex)
            {
                string message = ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString();
                Console.WriteLine("Demo2 RGB 自测异常: " + message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Demo2 RGB 自测异常: " + ex);
                return 1;
            }
            finally
            {
                DisposePipelineResultMasks(entryRunResult);
                DisposePipelineResultMasks(manualRunResult);
                DisposePipelineResultMasks(rawBgrRunResult);

                if (manualRgb != null) manualRgb.Dispose();
                if (entryRgb != null) entryRgb.Dispose();
                if (imageBgr != null) imageBgr.Dispose();

                TryDispose(icModel);
                TryDispose(componentModel);
                TryDispose(extractModel);
                TryDispose(form);
                ForceGc();
            }
        }

        private static int RunDemo2RouteRuleSelfTest()
        {
            Console.WriteLine("==== Demo2 分流规则自测 ====");

            string demo2AssemblyPath = ResolveDemo2AssemblyPath();
            if (!File.Exists(demo2AssemblyPath))
            {
                Console.WriteLine("未找到 Demo2 可执行文件: " + demo2AssemblyPath);
                Console.WriteLine("请先构建 DlcvDemo2.csproj。");
                return 2;
            }

            try
            {
                Assembly demo2Assembly = Assembly.LoadFrom(demo2AssemblyPath);
                Type formType = demo2Assembly.GetType("DlcvDemo2.Form1", throwOnError: true);
                MethodInfo routeMethod = formType.GetMethod("ShouldUseIcDetectModel", BindingFlags.NonPublic | BindingFlags.Static);
                if (routeMethod == null)
                {
                    Console.WriteLine("未找到 Demo2 分流规则方法");
                    return 1;
                }

                var cases = new[]
                {
                    new { BaseName = "IC", Expected = true },
                    new { BaseName = "ic", Expected = true },
                    new { BaseName = "BGA", Expected = true },
                    new { BaseName = "座子", Expected = true },
                    new { BaseName = "开关", Expected = true },
                    new { BaseName = "晶振", Expected = true },
                    new { BaseName = "电阻", Expected = false },
                    new { BaseName = "", Expected = false },
                    new { BaseName = (string)null, Expected = false }
                };

                for (int i = 0; i < cases.Length; i++)
                {
                    bool actual = (bool)routeMethod.Invoke(null, new object[] { cases[i].BaseName });
                    string baseNameText = cases[i].BaseName ?? "<null>";
                    Console.WriteLine("base_name: " + baseNameText + ", expected: " + cases[i].Expected + ", actual: " + actual);
                    if (actual != cases[i].Expected)
                    {
                        Console.WriteLine("自测失败：Demo2 分流规则与预期不一致");
                        return 1;
                    }
                }

                Console.WriteLine("Demo2 分流规则自测通过");
                return 0;
            }
            catch (TargetInvocationException ex)
            {
                string message = ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString();
                Console.WriteLine("Demo2 分流规则自测异常: " + message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Demo2 分流规则自测异常: " + ex);
                return 1;
            }
        }

        private static Mat PrepareDemo2ExpectedInput(Mat image)
        {
            if (image == null || image.Empty())
            {
                return image;
            }

            int channels = image.Channels();
            if (channels == 1)
            {
                return image.Clone();
            }

            if (channels == 3)
            {
                var rgb = new Mat();
                Cv2.CvtColor(image, rgb, ColorConversionCodes.BGR2RGB);
                return rgb;
            }

            if (channels == 4)
            {
                var rgb = new Mat();
                Cv2.CvtColor(image, rgb, ColorConversionCodes.BGRA2RGB);
                return rgb;
            }

            return image.Clone();
        }

        private static object CreateDemo2SlidingWindowConfig(Type configType, int width, int height, int overlapX, int overlapY)
        {
            object config = Activator.CreateInstance(configType);
            configType.GetProperty("WindowWidth")?.SetValue(config, width);
            configType.GetProperty("WindowHeight")?.SetValue(config, height);
            configType.GetProperty("OverlapX")?.SetValue(config, overlapX);
            configType.GetProperty("OverlapY")?.SetValue(config, overlapY);
            return config;
        }

        private static string BuildDemo2PipelineSignature(object pipelineRunResult)
        {
            if (pipelineRunResult == null)
            {
                return string.Empty;
            }

            PropertyInfo finalObjectsProperty = pipelineRunResult.GetType().GetProperty("FinalObjects", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (finalObjectsProperty == null)
            {
                return string.Empty;
            }

            var items = new List<string>();
            var enumerable = finalObjectsProperty.GetValue(pipelineRunResult, null) as System.Collections.IEnumerable;
            if (enumerable == null)
            {
                return string.Empty;
            }

            foreach (object obj in enumerable)
            {
                if (obj == null) continue;
                items.Add(BuildReflectedObjectSignature(obj));
            }

            items.Sort(StringComparer.Ordinal);
            return string.Join(";", items);
        }

        private static string BuildReflectedObjectSignature(object obj)
        {
            Type t = obj.GetType();
            int categoryId = ReadReflectedValue<int>(obj, t, "CategoryId");
            string categoryName = ReadReflectedValue<string>(obj, t, "CategoryName") ?? string.Empty;
            float score = ReadReflectedValue<float>(obj, t, "Score");
            bool withAngle = ReadReflectedValue<bool>(obj, t, "WithAngle");
            float angle = ReadReflectedValue<float>(obj, t, "Angle");

            string bboxSignature = string.Empty;
            object bboxValue = t.GetProperty("Bbox")?.GetValue(obj, null);
            var bboxEnumerable = bboxValue as System.Collections.IEnumerable;
            if (bboxEnumerable != null)
            {
                var bboxParts = new List<string>();
                foreach (object item in bboxEnumerable)
                {
                    double value = Convert.ToDouble(item, CultureInfo.InvariantCulture);
                    bboxParts.Add(value.ToString("F3", CultureInfo.InvariantCulture));
                }
                bboxSignature = string.Join(",", bboxParts);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1:F4}|{2}|{3}|{4:F4}",
                categoryId,
                score,
                categoryName,
                bboxSignature,
                withAngle ? angle : -100.0f);
        }

        private static T ReadReflectedValue<T>(object instance, Type type, string propertyName)
        {
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                return default(T);
            }

            object value = property.GetValue(instance, null);
            if (value == null)
            {
                return default(T);
            }

            if (value is T typed)
            {
                return typed;
            }

            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }

        private static void DisposePipelineResultMasks(object pipelineRunResult)
        {
            if (pipelineRunResult == null)
            {
                return;
            }

            PropertyInfo displayResultProperty = pipelineRunResult.GetType().GetProperty("DisplayResult", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (displayResultProperty == null)
            {
                return;
            }

            var displayResult = displayResultProperty.GetValue(pipelineRunResult, null) as Utils.CSharpResult?;
            if (displayResult.HasValue)
            {
                DisposeResultMasks(displayResult.Value);
            }
        }

        private static void TryDispose(object obj)
        {
            try
            {
                (obj as IDisposable)?.Dispose();
            }
            catch
            {
            }
        }

        private static string ResolveDemo2AssemblyPath()
        {
            string repoRoot = ResolveRepoRoot();
            string[] candidates =
            {
                Path.Combine(repoRoot, "DlcvDemo2", "bin", "C# 测试程序2.exe"),
                Path.Combine(repoRoot, "DlcvDemo2", "bin", "x64", "Debug", "C# 测试程序2.exe"),
                Path.Combine(repoRoot, "DlcvDemo2", "bin", "Debug", "C# 测试程序2.exe")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return candidates[0];
        }

        private static string ResolveRepoRoot()
        {
            string root = TryFindRepoRoot(Environment.CurrentDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                return root;
            }

            root = TryFindRepoRoot(AppDomain.CurrentDomain.BaseDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                return root;
            }

            throw new DirectoryNotFoundException("未找到 OpenIVS.sln，无法定位仓库根目录。");
        }

        private static string TryFindRepoRoot(string startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(startPath);
            var dir = new DirectoryInfo(fullPath);
            if (!dir.Exists && dir.Parent != null)
            {
                dir = dir.Parent;
            }

            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OpenIVS.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }

        private static string BuildResultSignature(Utils.CSharpResult result)
        {
            var parts = new List<string>();
            if (result.SampleResults == null)
            {
                return string.Empty;
            }

            for (int sampleIndex = 0; sampleIndex < result.SampleResults.Count; sampleIndex++)
            {
                var sample = result.SampleResults[sampleIndex];
                var sampleParts = new List<string>();
                if (sample.Results != null)
                {
                    foreach (var obj in sample.Results)
                    {
                        string bboxSig = string.Empty;
                        if (obj.Bbox != null && obj.Bbox.Count > 0)
                        {
                            var bboxParts = new List<string>(obj.Bbox.Count);
                            for (int i = 0; i < obj.Bbox.Count; i++)
                            {
                                bboxParts.Add(obj.Bbox[i].ToString("F3", CultureInfo.InvariantCulture));
                            }
                            bboxSig = string.Join(",", bboxParts);
                        }

                        sampleParts.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}|{1:F4}|{2}|{3}|{4:F4}",
                            obj.CategoryId,
                            obj.Score,
                            obj.CategoryName ?? string.Empty,
                            bboxSig,
                            obj.WithAngle ? obj.Angle : -100.0f));
                    }
                }

                sampleParts.Sort(StringComparer.Ordinal);
                parts.Add("sample" + sampleIndex.ToString(CultureInfo.InvariantCulture) + ":" + string.Join(";", sampleParts));
            }

            return string.Join(" || ", parts);
        }

        private static void AssertNear(double expected, double actual, double tolerance, string label)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} mismatch, expected={1:F4}, actual={2:F4}, tol={3:F4}",
                        label,
                        expected,
                        actual,
                        tolerance));
            }
        }

        private static double NormalizeAngleDeg(double angleDeg)
        {
            double x = angleDeg % 180.0;
            if (x < -90.0) x += 180.0;
            if (x >= 90.0) x -= 180.0;
            return x;
        }


        private struct ModelCase
        {
            public readonly string ModelFile;
            public readonly string ImageFile;
            public ModelCase(string modelFile, string imageFile) { ModelFile = modelFile; ImageFile = imageFile; }
        }

        private struct CaseRow
        {
            public string ModelName;
            public string LoadStatus;
            public string InferStatus;
            public string CategoryList;
            public string SpeedText;
            public string BatchText;
        }

        private struct SpeedResult
        {
            public readonly double Fps;
            public readonly bool Supported;
            public SpeedResult(double fps, bool supported) { Fps = fps; Supported = supported; }
            public static SpeedResult Na() { return new SpeedResult(0, false); }
        }

        private sealed class NodeTimingAggregate
        {
            public int NodeId { get; private set; }
            public string NodeType { get; private set; }
            public string NodeTitle { get; private set; }
            public int Count { get; private set; }
            public double TotalMs { get; private set; }
            public double AverageMs { get { return Count > 0 ? TotalMs / Count : 0.0; } }

            public NodeTimingAggregate(int nodeId, string nodeType, string nodeTitle)
            {
                NodeId = nodeId;
                NodeType = nodeType ?? string.Empty;
                NodeTitle = nodeTitle ?? string.Empty;
                Count = 0;
                TotalMs = 0.0;
            }

            public void Add(double elapsedMs)
            {
                Count += 1;
                TotalMs += Math.Max(0.0, elapsedMs);
            }
        }

        private struct MemorySnapshot
        {
            public readonly double PrivateMb;
            public MemorySnapshot(double privateMb) { PrivateMb = privateMb; }
            public static MemorySnapshot Capture()
            {
                var proc = Process.GetCurrentProcess();
                PROCESS_MEMORY_COUNTERS_EX counters;
                if (!GetProcessMemoryInfo(proc.Handle, out counters, (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX))))
                {
                    return new MemorySnapshot(0);
                }
                double pv = counters.PrivateUsage.ToInt64() / 1024.0 / 1024.0;
                return new MemorySnapshot(pv);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS_EX
        {
            public uint cb;
            public uint PageFaultCount;
            public IntPtr PeakWorkingSetSize;
            public IntPtr WorkingSetSize;
            public IntPtr QuotaPeakPagedPoolUsage;
            public IntPtr QuotaPagedPoolUsage;
            public IntPtr QuotaPeakNonPagedPoolUsage;
            public IntPtr QuotaNonPagedPoolUsage;
            public IntPtr PagefileUsage;
            public IntPtr PeakPagefileUsage;
            public IntPtr PrivateUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr hProcess, out PROCESS_MEMORY_COUNTERS_EX counters, uint size);
    }
}
