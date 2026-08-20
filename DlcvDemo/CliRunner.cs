using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;
using DlcvModules;

namespace DlcvDemo
{
    internal static class CliRunner
    {
        private const uint AttachParentProcess = 0xFFFFFFFF;
        private const int ErrorAccessDenied = 5;
        private const double ScoreConsistencyTolerance = 1e-6;
        private const double MeanConsistencyTolerance = 1e-6;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        internal static void InitializeConsole()
        {
            try
            {
                if (!AttachConsole(AttachParentProcess))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != ErrorAccessDenied)
                    {
                        AllocConsole();
                    }
                }

                var utf8 = new UTF8Encoding(false);
                var stdout = Console.OpenStandardOutput();
                if (stdout != null && stdout.CanWrite)
                {
                    var writer = new StreamWriter(stdout, utf8) { AutoFlush = true };
                    Console.SetOut(writer);
                }

                var stderr = Console.OpenStandardError();
                if (stderr != null && stderr.CanWrite)
                {
                    var writer = new StreamWriter(stderr, utf8) { AutoFlush = true };
                    Console.SetError(writer);
                }
            }
            catch
            {
                // 无控制台时仍允许 --output 写入验证结果。
            }
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                WriteParameterError("缺少 CLI 命令。");
                return 2;
            }

            if (args.Length == 1 && IsHelpToken(args[0]))
            {
                PrintHelp();
                return 0;
            }

            if (args.Length == 1 && IsVersionToken(args[0]))
            {
                Console.Out.WriteLine(GetVersion());
                return 0;
            }

            if (!string.Equals(args[0], "infer", StringComparison.OrdinalIgnoreCase))
            {
                WriteParameterError("未知命令: " + args[0]);
                return 2;
            }

            if (args.Length == 2 && IsHelpToken(args[1]))
            {
                PrintHelp();
                return 0;
            }

            if (!TryParseInferOptions(args, out CliOptions options, out string error))
            {
                WriteParameterError(error);
                return 2;
            }

            try
            {
                JObject summary = ExecuteInference(options);
                string json = summary.ToString(Formatting.Indented);

                // 先写 stdout，即使后续输出文件失败也能看到本次验证摘要。
                Console.Out.WriteLine(json);

                if (!string.IsNullOrWhiteSpace(options.OutputPath))
                {
                    File.WriteAllText(options.OutputPath, json + Environment.NewLine, new UTF8Encoding(false));
                }

                bool consistent = summary.Value<bool>("consistent");
                bool thresholdCheckPassed = summary.Value<bool>("threshold_check_passed");
                bool meanCheckPassed = summary.Value<bool>("mean_check_passed");
                return consistent && thresholdCheckPassed && meanCheckPassed ? 0 : 3;
            }
            catch (Exception ex)
            {
                WriteException(ex);
                return 1;
            }
        }

        private static JObject ExecuteInference(CliOptions options)
        {
            Model model = null;
            Mat decodedImage = null;
            Mat inferImage = null;
            try
            {
                Model.EnableConsoleLog = false;
                GlobalDebug.PrintDebug = false;
                model = new Model(options.ModelPath, options.DeviceId, false, false);

                byte[] imageBytes = File.ReadAllBytes(options.ImagePath);
                decodedImage = Cv2.ImDecode(imageBytes, ImreadModes.Unchanged);
                if (decodedImage == null || decodedImage.Empty())
                {
                    throw new InvalidOperationException("图像解码失败。");
                }

                inferImage = PrepareImageForInference(decodedImage);
                if (inferImage == null || inferImage.Empty())
                {
                    throw new InvalidOperationException("图像预处理失败。");
                }

                JObject inferParams = new JObject
                {
                    ["threshold"] = (float)options.Threshold,
                    ["with_mask"] = options.WithMask
                };
                if (options.CalcMean.HasValue)
                {
                    inferParams["calc_mean"] = options.CalcMean.Value;
                }

                PathSummary structuredSummary;
                Utils.CSharpResult structuredResult = default(Utils.CSharpResult);
                bool structuredResultCreated = false;
                try
                {
                    structuredResult = model.Infer(inferImage, (JObject)inferParams.DeepClone());
                    structuredResultCreated = true;
                    structuredSummary = BuildStructuredSummary(structuredResult, options.Threshold);
                }
                finally
                {
                    if (structuredResultCreated)
                    {
                        DisposeResultMasks(structuredResult);
                    }
                }

                object rawJsonResult = model.InferOneOutJson(inferImage, (JObject)inferParams.DeepClone());
                var jsonResults = rawJsonResult as JArray;
                if (jsonResults == null)
                {
                    throw new InvalidOperationException("InferOneOutJson 未返回 JSON 数组。");
                }

                PathSummary jsonSummary = BuildJsonSummary(jsonResults, options.Threshold);
                bool consistent = AreConsistent(structuredSummary, jsonSummary);
                bool thresholdCheckPassed = structuredSummary.BelowThresholdCount == 0
                    && jsonSummary.BelowThresholdCount == 0;
                bool bothResultsEmpty = structuredSummary.Count == 0 && jsonSummary.Count == 0;
                bool meanCheckPassed = !options.CalcMean.HasValue
                    || bothResultsEmpty
                    || (options.CalcMean.Value
                        ? structuredSummary.AllMaskResultsHaveMean && jsonSummary.AllMaskResultsHaveMean
                        : !structuredSummary.AnyHaveMean && !jsonSummary.AnyHaveMean);

                return new JObject
                {
                    ["language"] = "csharp",
                    ["model"] = options.ModelPath,
                    ["image"] = options.ImagePath,
                    ["device"] = options.DeviceId,
                    ["threshold"] = options.Threshold,
                    ["with_mask"] = options.WithMask,
                    ["calc_mean"] = options.CalcMean.HasValue
                        ? new JValue(options.CalcMean.Value)
                        : JValue.CreateNull(),
                    ["structured"] = structuredSummary.ToJson(),
                    ["json"] = jsonSummary.ToJson(),
                    ["consistent"] = consistent,
                    ["threshold_check_passed"] = thresholdCheckPassed,
                    ["mean_check_passed"] = meanCheckPassed
                };
            }
            finally
            {
                if (inferImage != null && !ReferenceEquals(inferImage, decodedImage))
                {
                    try { inferImage.Dispose(); } catch { }
                }
                if (decodedImage != null)
                {
                    try { decodedImage.Dispose(); } catch { }
                }
                if (model != null)
                {
                    try { model.Dispose(); } catch { }
                }
                try { Utils.FreeAllModels(); } catch { }
            }
        }

        private static Mat PrepareImageForInference(Mat image)
        {
            int channels = image.Channels();
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
            return image;
        }

        private static PathSummary BuildStructuredSummary(Utils.CSharpResult result, double threshold)
        {
            var summary = new PathSummary();
            if (result.SampleResults == null)
            {
                return summary;
            }

            foreach (var sample in result.SampleResults)
            {
                if (sample.Results == null) continue;
                foreach (var item in sample.Results)
                {
                    summary.Add(
                        item.Score,
                        item.CategoryName,
                        item.WithMask,
                        item.WithMean,
                        item.ForegroundMean,
                        item.BackgroundMean,
                        threshold);
                }
            }
            return summary;
        }

        private static PathSummary BuildJsonSummary(JArray resultArray, double threshold)
        {
            var summary = new PathSummary();
            for (int i = 0; i < resultArray.Count; i++)
            {
                var item = resultArray[i] as JObject;
                double score = double.NaN;
                string category = string.Empty;
                bool withMask = false;
                bool withMean = false;
                double foregroundMean = 0.0;
                double backgroundMean = 0.0;
                if (item != null)
                {
                    TryReadScore(item["score"], out score);
                    category = item["category_name"] != null ? item["category_name"].ToString() : string.Empty;
                    withMask = item.Value<bool?>("with_mask") ?? false;
                    withMean = item.Value<bool?>("with_mean") ?? false;
                    foregroundMean = item.Value<double?>("foreground_mean") ?? 0.0;
                    backgroundMean = item.Value<double?>("background_mean") ?? 0.0;
                }
                summary.Add(score, category, withMask, withMean, foregroundMean, backgroundMean, threshold);
            }
            return summary;
        }

        private static bool TryReadScore(JToken token, out double score)
        {
            score = double.NaN;
            if (token == null) return false;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                score = token.Value<double>();
                return true;
            }
            return double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out score);
        }

        private static bool AreConsistent(PathSummary left, PathSummary right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                double leftScore = left.Scores[i];
                double rightScore = right.Scores[i];
                if (!IsFinite(leftScore) || !IsFinite(rightScore)) return false;
                if (Math.Abs(leftScore - rightScore) > ScoreConsistencyTolerance) return false;
                if (!string.Equals(left.Categories[i], right.Categories[i], StringComparison.Ordinal)) return false;
                if (left.WithMeans[i] != right.WithMeans[i]) return false;
                if (left.WithMeans[i])
                {
                    if (!IsFinite(left.ForegroundMeans[i]) || !IsFinite(right.ForegroundMeans[i])) return false;
                    if (!IsFinite(left.BackgroundMeans[i]) || !IsFinite(right.BackgroundMeans[i])) return false;
                    if (Math.Abs(left.ForegroundMeans[i] - right.ForegroundMeans[i]) > MeanConsistencyTolerance) return false;
                    if (Math.Abs(left.BackgroundMeans[i] - right.BackgroundMeans[i]) > MeanConsistencyTolerance) return false;
                }
            }
            return true;
        }

        private static void DisposeResultMasks(Utils.CSharpResult result)
        {
            if (result.SampleResults == null) return;
            foreach (var sample in result.SampleResults)
            {
                if (sample.Results == null) continue;
                foreach (var item in sample.Results)
                {
                    if (item.Mask != null)
                    {
                        try { item.Mask.Dispose(); } catch { }
                    }
                }
            }
        }

        private static bool TryParseInferOptions(string[] args, out CliOptions options, out string error)
        {
            options = new CliOptions
            {
                DeviceId = 0,
                WithMask = true
            };
            error = null;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < args.Length; i += 2)
            {
                string key = args[i];
                if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("--", StringComparison.Ordinal))
                {
                    error = "无效参数名: " + (key ?? string.Empty);
                    return false;
                }
                if (i + 1 >= args.Length)
                {
                    error = "参数缺少值: " + key;
                    return false;
                }
                if (!seen.Add(key))
                {
                    error = "参数重复: " + key;
                    return false;
                }

                string value = args[i + 1];
                switch (key.ToLowerInvariant())
                {
                    case "--model":
                        options.ModelPath = value;
                        break;
                    case "--image":
                        options.ImagePath = value;
                        break;
                    case "--threshold":
                        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold)
                            || !IsFinite(threshold)
                            || threshold < 0.0
                            || threshold > 1.0)
                        {
                            error = "--threshold 必须是 0 到 1 之间的数字。";
                            return false;
                        }
                        options.Threshold = threshold;
                        options.HasThreshold = true;
                        break;
                    case "--device":
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceId)
                            || deviceId < -2)
                        {
                            error = "--device 必须是 -2（OpenVINO）、-1（CPU）或非负整数（GPU 编号）。";
                            return false;
                        }
                        options.DeviceId = deviceId;
                        break;
                    case "--with-mask":
                        if (!bool.TryParse(value, out bool withMask))
                        {
                            error = "--with-mask 必须是 true 或 false。";
                            return false;
                        }
                        options.WithMask = withMask;
                        break;
                    case "--calc-mean":
                        if (!bool.TryParse(value, out bool calcMean))
                        {
                            error = "--calc-mean 必须是 true 或 false。";
                            return false;
                        }
                        options.CalcMean = calcMean;
                        break;
                    case "--output":
                        options.OutputPath = value;
                        break;
                    default:
                        error = "未知参数: " + key;
                        return false;
                }
            }

            if (string.IsNullOrWhiteSpace(options.ModelPath))
            {
                error = "缺少必填参数 --model。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(options.ImagePath))
            {
                error = "缺少必填参数 --image。";
                return false;
            }
            if (!options.HasThreshold)
            {
                error = "缺少必填参数 --threshold。";
                return false;
            }
            if (!File.Exists(options.ModelPath))
            {
                error = "模型文件不存在。";
                return false;
            }
            if (!File.Exists(options.ImagePath))
            {
                error = "图片文件不存在。";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                try
                {
                    string outputFullPath = Path.GetFullPath(options.OutputPath);
                    string modelFullPath = Path.GetFullPath(options.ModelPath);
                    string imageFullPath = Path.GetFullPath(options.ImagePath);
                    if (string.Equals(outputFullPath, modelFullPath, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(outputFullPath, imageFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "--output 不能覆盖模型或图片文件。";
                        return false;
                    }

                    string outputDirectory = Path.GetDirectoryName(outputFullPath);
                    if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                    {
                        error = "--output 的父目录不存在。";
                        return false;
                    }
                    options.OutputPath = outputFullPath;
                }
                catch (Exception ex)
                {
                    error = "--output 路径无效: " + ex.Message;
                    return false;
                }
            }

            return true;
        }

        private static bool IsHelpToken(string value)
        {
            return string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "/?", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVersionToken(string value)
        {
            return string.Equals(value, "--version", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "version", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetVersion()
        {
            Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            object[] attributes = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
            if (attributes.Length > 0)
            {
                var info = attributes[0] as AssemblyInformationalVersionAttribute;
                if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
                {
                    return info.InformationalVersion;
                }
            }
            Version version = assembly.GetName().Version;
            return version != null ? version.ToString() : "unknown";
        }

        private static void PrintHelp()
        {
            Console.Out.WriteLine("Usage:");
            Console.Out.WriteLine("  \"C# 测试程序.exe\" infer --model <path> --image <path> --threshold <0..1> [--device <int>] [--with-mask <true|false>] [--calc-mean <true|false>] [--output <jsonPath>]");
            Console.Out.WriteLine("  \"C# 测试程序.exe\" ui-test --model <path> --image <path> --output <jsonPath> [--threshold <0..1>] [--device <int>] [--calc-mean <true|false>] [--interactive-dialogs <true|false>]");
            Console.Out.WriteLine("  \"C# 测试程序.exe\" --help");
            Console.Out.WriteLine("  \"C# 测试程序.exe\" --version");
            Console.Out.WriteLine();
            Console.Out.WriteLine("Flow archives: --threshold filters final output only; model-node thresholds come from the flow.");
            Console.Out.WriteLine("Exit codes: 0=passed, 1=runtime error, 2=invalid arguments, 3=validation failed");
        }

        private static void WriteParameterError(string message)
        {
            Console.Error.WriteLine("参数错误: " + message);
            PrintHelp();
        }

        private static void WriteException(Exception ex)
        {
            var error = new JObject
            {
                ["language"] = "csharp",
                ["error"] = ex.Message,
                ["exception_type"] = ex.GetType().FullName
            };
            Console.Error.WriteLine(error.ToString(Formatting.Indented));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class CliOptions
        {
            public string ModelPath { get; set; }
            public string ImagePath { get; set; }
            public double Threshold { get; set; }
            public bool HasThreshold { get; set; }
            public int DeviceId { get; set; }
            public bool WithMask { get; set; }
            public bool? CalcMean { get; set; }
            public string OutputPath { get; set; }
        }

        private sealed class PathSummary
        {
            private readonly JArray _belowThreshold = new JArray();

            public List<double> Scores { get; } = new List<double>();
            public List<string> Categories { get; } = new List<string>();
            public List<bool> WithMasks { get; } = new List<bool>();
            public List<bool> WithMeans { get; } = new List<bool>();
            public List<double> ForegroundMeans { get; } = new List<double>();
            public List<double> BackgroundMeans { get; } = new List<double>();
            public int Count { get { return Scores.Count; } }
            public int BelowThresholdCount { get { return _belowThreshold.Count; } }
            public bool AllMaskResultsHaveMean
            {
                get
                {
                    return WithMasks
                        .Select((withMask, index) => !withMask || WithMeans[index])
                        .All(value => value);
                }
            }
            public bool AnyHaveMean { get { return WithMeans.Any(value => value); } }

            public void Add(
                double score,
                string category,
                bool withMask,
                bool withMean,
                double foregroundMean,
                double backgroundMean,
                double threshold)
            {
                int index = Scores.Count;
                string normalizedCategory = category ?? string.Empty;
                Scores.Add(score);
                Categories.Add(normalizedCategory);
                WithMasks.Add(withMask);
                WithMeans.Add(withMean);
                ForegroundMeans.Add(foregroundMean);
                BackgroundMeans.Add(backgroundMean);

                if (!IsFinite(score) || score < threshold)
                {
                    _belowThreshold.Add(new JObject
                    {
                        ["index"] = index,
                        ["score"] = IsFinite(score) ? new JValue(score) : JValue.CreateNull(),
                        ["category"] = normalizedCategory
                    });
                }
            }

            public JObject ToJson()
            {
                var scores = new JArray();
                var foregroundMeans = new JArray();
                var backgroundMeans = new JArray();
                foreach (double score in Scores)
                {
                    scores.Add(IsFinite(score) ? new JValue(score) : JValue.CreateNull());
                }
                foreach (double foregroundMean in ForegroundMeans)
                {
                    foregroundMeans.Add(IsFinite(foregroundMean) ? new JValue(foregroundMean) : JValue.CreateNull());
                }
                foreach (double backgroundMean in BackgroundMeans)
                {
                    backgroundMeans.Add(IsFinite(backgroundMean) ? new JValue(backgroundMean) : JValue.CreateNull());
                }

                return new JObject
                {
                    ["count"] = Count,
                    ["scores"] = scores,
                    ["categories"] = new JArray(Categories),
                    ["with_mask"] = new JArray(WithMasks),
                    ["with_mean"] = new JArray(WithMeans),
                    ["foreground_mean"] = foregroundMeans,
                    ["background_mean"] = backgroundMeans,
                    ["below_threshold"] = _belowThreshold.DeepClone()
                };
            }
        }
    }
}
