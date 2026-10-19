using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using dlcv_infer_csharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace FourImageConcurrencyRegression
{
    internal static class Regression
    {
        private static readonly string[] Lanes = { "A", "B", "C", "D" };

        public static int Run(Options options, StreamWriter log, JObject summary)
        {
            string apiPath = typeof(Model).Assembly.Location;
            summary["api"] = new JObject { ["path"] = apiPath, ["version"] = FileVersionInfo.GetVersionInfo(apiPath).FileVersion };
            summary["serial_completed"] = 0;
            summary["concurrent_completed"] = 0;
            summary["mismatches"] = 0;
            summary["serial_mismatches"] = 0;
            summary["concurrent_mismatches"] = 0;
            summary["exceptions"] = 0;
            var images = new Mat[4];
            var imageSignatures = new JToken[4];
            var clock = Stopwatch.StartNew();
            TextWriter consoleOutput = Console.Out;
            Console.SetOut(TextWriter.Null);
            try
            {
                Model.EnableConsoleLog = false;
                for (int lane = 0; lane < 4; lane++)
                {
                    string path = Path.Combine(options.Images, Lanes[lane] + ".bmp");
                    if (!File.Exists(path)) throw new FileNotFoundException("图片不存在", path);
                    using (Mat bgr = Cv2.ImRead(path, ImreadModes.Color))
                    {
                        if (bgr.Empty()) throw new InvalidOperationException("无法读取图片：" + path);
                        images[lane] = new Mat();
                        Cv2.CvtColor(bgr, images[lane], ColorConversionCodes.BGR2RGB);
                    }
                    imageSignatures[lane] = MatSignature(images[lane]);
                }
                summary["rgb_inputs"] = new JObject(Lanes.Select((name, lane) => new JProperty(name, imageSignatures[lane])));
                using (var model = new Model(options.Model, options.Device))
                {
                    summary["native_modules"] = new JArray(Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                        .Where(module => module.ModuleName.Equals("dlcv_infer.dll", StringComparison.OrdinalIgnoreCase)
                            || module.ModuleName.Equals("dlcv_infer_v.dll", StringComparison.OrdinalIgnoreCase))
                        .Select(module => new JObject { ["path"] = module.FileName, ["version"] = module.FileVersionInfo.FileVersion }));
                    var baselines = new JArray[4];
                    for (int lane = 0; lane < 4; lane++)
                    {
                        baselines[lane] = Infer(model, images[lane], imageSignatures[lane]);
                        Write(log, "baseline", 0, lane, baselines[lane], null, null);
                        VerifyFixture(lane, baselines[lane]);
                    }
                    summary["baselines"] = new JObject(Lanes.Select((name, lane) => new JProperty(name, baselines[lane])));
                    for (int round = 1; round <= options.SerialRounds; round++)
                    {
                        for (int lane = 0; lane < 4; lane++)
                        {
                            JArray actual;
                            try { actual = Infer(model, images[lane], imageSignatures[lane]); }
                            catch (Exception error)
                            {
                                summary["exceptions"] = (int)summary["exceptions"] + 1;
                                Write(log, "serial", round, lane, null, baselines[lane], error.ToString());
                                return 1;
                            }
                            Check(log, summary, "serial", round, lane, baselines[lane], actual);
                        }
                        summary["serial_completed"] = round;
                        if (round % 250 == 0 || round == options.SerialRounds) Progress(consoleOutput, summary, "串行", round, options.SerialRounds);
                    }

                    int workers, completion;
                    ThreadPool.GetMinThreads(out workers, out completion);
                    ThreadPool.SetMinThreads(Math.Max(workers, 4), completion);
                    for (int round = 1; round <= options.Rounds; round++)
                    {
                        var results = new JArray[4];
                        var errors = new Exception[4];
                        using (var ready = new CountdownEvent(4))
                        using (var start = new ManualResetEventSlim(false))
                        {
                            var tasks = new Task[4];
                            for (int lane = 0; lane < 4; lane++)
                            {
                                int currentLane = lane;
                                tasks[lane] = Task.Run(() =>
                                {
                                    ready.Signal();
                                    start.Wait();
                                    try { results[currentLane] = Infer(model, images[currentLane], imageSignatures[currentLane]); }
                                    catch (Exception error) { errors[currentLane] = error; }
                                });
                            }
                            ready.Wait();
                            start.Set();
                            Task.WaitAll(tasks);
                        }
                        for (int lane = 0; lane < 4; lane++)
                        {
                            if (errors[lane] == null) Check(log, summary, "concurrent", round, lane, baselines[lane], results[lane]);
                            else
                            {
                                summary["exceptions"] = (int)summary["exceptions"] + 1;
                                Write(log, "concurrent", round, lane, null, baselines[lane], errors[lane].ToString());
                            }
                        }
                        summary["concurrent_completed"] = round;
                        if (round % 250 == 0 || round == options.Rounds) Progress(consoleOutput, summary, "并发", round, options.Rounds);
                        if (errors.Any(error => error != null)) return 1;
                    }
                }
            }
            finally
            {
                Console.SetOut(consoleOutput);
                foreach (Mat image in images) image?.Dispose();
                summary["elapsed_ms"] = clock.ElapsedMilliseconds;
            }
            return (int)summary["mismatches"] == 0 ? 0 : 1;
        }

        private static JArray Infer(Model model, Mat original, JToken originalSignature)
        {
            using (Mat copy = original.Clone())
            {
                if (!JToken.DeepEquals(originalSignature, MatSignature(copy)))
                    throw new InvalidOperationException("调用前 RGB 副本内容与初始图像不一致");
                Utils.CSharpResult result = default(Utils.CSharpResult);
                try
                {
                    result = model.Infer(copy);
                    if (result.SampleResults == null || result.SampleResults.Count != 1 || result.SampleResults[0].Results == null)
                        throw new InvalidOperationException("单图推理未返回一组有效结果");
                    return new JArray(result.SampleResults[0].Results.Select(item => new JObject
                    {
                        ["category_id"] = item.CategoryId,
                        ["category_name"] = item.CategoryName,
                        ["score"] = item.Score,
                        ["bbox"] = item.Bbox == null ? JValue.CreateNull() : (JToken)new JArray(item.Bbox),
                        ["with_bbox"] = item.WithBbox,
                        ["area"] = item.Area,
                        ["with_angle"] = item.WithAngle,
                        ["angle"] = item.Angle,
                        ["with_mask"] = item.WithMask,
                        ["mask"] = MatSignature(item.Mask)
                    }).OrderBy(item => item.ToString(Formatting.None), StringComparer.Ordinal));
                }
                finally
                {
                    if (result.SampleResults != null)
                        foreach (var sample in result.SampleResults)
                            if (sample.Results != null)
                                foreach (var item in sample.Results) item.Mask?.Dispose();
                    JToken after = MatSignature(copy);
                    if (!JToken.DeepEquals(originalSignature, after))
                        throw new InvalidOperationException("推理改写了 RGB 输入内容，调用前："
                            + originalSignature.ToString(Formatting.None) + "，调用后：" + after.ToString(Formatting.None));
                }
            }
        }

        private static JToken MatSignature(Mat mask)
        {
            if (mask == null) return JValue.CreateNull();
            if (mask.Empty()) return new JObject { ["empty"] = true };
            int rowSize = checked(mask.Cols * (int)mask.ElemSize());
            var row = new byte[rowSize];
            using (SHA256 hash = SHA256.Create())
            {
                for (int y = 0; y < mask.Rows; y++)
                {
                    Marshal.Copy(mask.Ptr(y), row, 0, row.Length);
                    hash.TransformBlock(row, 0, row.Length, row, 0);
                }
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return new JObject
                {
                    ["width"] = mask.Cols, ["height"] = mask.Rows, ["type"] = mask.Type().ToString(),
                    ["sha256"] = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant()
                };
            }
        }

        private static void VerifyFixture(int lane, JArray baseline)
        {
            bool valid = lane == 1
                ? baseline.Count == 1 && (string)baseline[0]["category_name"] == "C2沾污"
                    && Math.Abs((double)baseline[0]["score"] - 0.60400390625) <= 0.000001
                : baseline.Count == 0;
            if (!valid) throw new InvalidOperationException("图片 " + Lanes[lane] + " 的串行基准不符合固定样例预期：" + baseline.ToString(Formatting.None));
        }

        private static void Check(StreamWriter log, JObject summary, string phase, int round, int lane, JArray expected, JArray actual)
        {
            bool matched = JToken.DeepEquals(expected, actual);
            if (!matched)
            {
                summary["mismatches"] = (int)summary["mismatches"] + 1;
                summary[phase + "_mismatches"] = (int)summary[phase + "_mismatches"] + 1;
            }
            Write(log, phase, round, lane, actual, matched ? null : expected, null);
        }

        private static void Write(StreamWriter log, string phase, int round, int lane, JArray result, JArray expected, string error)
        {
            log.WriteLine(new JObject
            {
                ["utc"] = DateTime.UtcNow, ["phase"] = phase, ["round"] = round, ["lane"] = Lanes[lane],
                ["count"] = result == null ? JValue.CreateNull() : new JValue(result.Count),
                ["matched"] = expected == null && error == null,
                ["input_unchanged"] = error == null ? new JValue(true) : JValue.CreateNull(),
                ["results"] = result, ["expected"] = expected, ["error"] = error
            }.ToString(Formatting.None));
        }

        private static void Progress(TextWriter output, JObject summary, string phase, int round, int total)
        {
            output.WriteLine("{0} {1}/{2}，不一致 {3}，异常 {4}", phase, round, total, summary["mismatches"], summary["exceptions"]);
        }
    }
}
