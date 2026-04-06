using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
        private const int FixedBatchSize = 8;
        private const string ModelRoot = @"Y:\测试模型";

        private static readonly List<ModelCase> DefaultCases = new List<ModelCase>
        {
            new ModelCase("AOI-旋转框检测.dvt", "AOI-测试.jpg"),
            new ModelCase("猫狗-分类.dvt", "猫狗-猫.jpg"),
            new ModelCase("气球-实例分割.dvt", "气球.jpg"),
            new ModelCase("气球-语义分割.dvt", "气球.jpg"),
            new ModelCase("手机屏幕-实例分割.dvt", "手机屏幕.jpg"),
            new ModelCase("引脚定位-目标检测.dvt", "引脚定位-目标检测.jpg"),
            new ModelCase("OCR.dvt", "OCR-1.jpg")
        };

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Model.EnableConsoleLog = false;

            if (args != null && args.Length >= 1 && string.Equals(args[0], "maskrbox-selftest", StringComparison.OrdinalIgnoreCase))
            {
                return RunMaskToRBoxSelfTest();
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "bench", StringComparison.OrdinalIgnoreCase))
            {
                return RunBenchmarkCommand(args);
            }

            if (args != null && args.Length >= 2)
            {
                string modelPath = args[0];
                string imagePath = args[1];
                int batch = 2;
                if (args.Length >= 3)
                {
                    int.TryParse(args[2], out batch);
                    if (batch <= 0) batch = 2;
                }
                return RunSingleBatchValidation(modelPath, imagePath, batch);
            }

            Console.WriteLine("==== C# 测试程序 ====");
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
            try { Utils.FreeAllModels(); } catch { }
            ForceGc();

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
                + (memAfter.PrivateMb - memBefore.PrivateMb).ToString("F2", CultureInfo.InvariantCulture) + "MB)";

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

                var speed = RunSpeedTest(model, rgb, 1, false);
                row.SpeedText = speed.Supported
                    ? ("均速 " + speed.Fps.ToString("F2", CultureInfo.InvariantCulture) + " 张/秒")
                    : "失败";

                var batch = RunSpeedTest(model, rgb, FixedBatchSize, true);
                row.BatchText = batch.Supported
                    ? ("均速 " + batch.Fps.ToString("F2", CultureInfo.InvariantCulture) + " 张/秒")
                    : "N/A";
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
