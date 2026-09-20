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
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using DlcvModules;
using dlcv_infer_csharp;
using sntl_admin_csharp;

namespace DlcvCSharpTest
{
    internal static partial class Program
    {
        private const int SpeedWindowSeconds = 3;
        private const int LeakLoopCount = 10;
        private const int DefaultLoadFreeMemoryLoopCount = 100;
        private const int DefaultLoadFreeMemorySampleInterval = 10;
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

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadNativeModule(string fileName);

        [DllImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetModuleFileNameW(IntPtr moduleHandle, StringBuilder fileName, uint size);

        [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetNativeProcAddress(IntPtr moduleHandle, string procedureName);

        private static readonly List<ModelRegressionCase> DefaultCases = ModelRegressionCases.Cases;
        private static readonly string[] SharedIndexNativeExports =
        {
            "dlcv_register_dvs_model",
            "dlcv_get_dvs_model",
            "dlcv_get_index_type",
            "dlcv_bind_index",
            "dlcv_get_all_models"
        };

        private sealed class UnifiedTestCase
        {
            public readonly string Name;
            public readonly Func<int> Run;

            public UnifiedTestCase(string name, Func<int> run)
            {
                Name = name;
                Run = run;
            }
        }

        private sealed class UnifiedTestResult
        {
            public string Name;
            public int ExitCode;
            public long ElapsedMilliseconds;
            public string Error;
        }

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Model.EnableConsoleLog = false;
            try
            {
                if (args != null && args.Length >= 1 && string.Equals(args[0], "all-tests", StringComparison.OrdinalIgnoreCase))
                {
                    return RunAllTests(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "model-channel-order-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunModelChannelOrderSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "flow-infer-params-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunFlowInferParamsSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "cli-anomaly-threshold-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunCliAnomalyThresholdSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "environment-cli-compatibility-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunEnvironmentCliCompatibilitySelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "ui-test-options-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunUiTestOptionsSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "winforms-mainwindow-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunWinFormsMainWindowSelfTest();
                }

                if (IsWorkflowCommand(args))
                {
                    return RunWorkflowCommands(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "get-model-info", StringComparison.OrdinalIgnoreCase))
                {
                    return RunGetModelInfoCommand(args, false);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "get-dvs-model-info", StringComparison.OrdinalIgnoreCase))
                {
                    return RunGetModelInfoCommand(args, true);
                }

                if (args != null && args.Length >= 1 &&
                    (string.Equals(args[0], "dvs-rgb-selftest", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(args[0], "dvs-bgr-selftest", StringComparison.OrdinalIgnoreCase)))
                {
                    return RunDvsRgbSelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "dvs-memory-loading-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunDvsMemoryLoadingSelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "model-load-free-memory-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunModelLoadFreeMemorySelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "model-load-free-stage-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunModelLoadFreeStageSelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "dvs-duplicate-entry-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return DvsArchiveDuplicateSelfTest.Run();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "dvsp-reject-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunDvspRejectSelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "undersized-model-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunUndersizedModelSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "dvsp-disabled-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunDvspDisabledSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "dvsp-parity-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunDvspParitySelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "maskrbox-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunMaskToRBoxSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "curve-text-affine-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunCurveTextAffineSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "ai-orientation-affine-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunAiOrientationAffineSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "mask-area-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return MaskAreaSelfTest.Run();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "region-mask-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RegionMaskSelfTest.Run();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "bbox-iou-dedup-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunBBoxIoUDedupSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "count-results-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunCountResultsSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "category-count-check-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunCategoryCountCheckSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "template-count-priority-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunTemplateCountPrioritySelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "image-generation-expand-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunImageGenerationExpandSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "cross-model-label-merge-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunCrossModelLabelMergeSelfTest();
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

                if (args != null && args.Length >= 1 && string.Equals(args[0], "flow-batch-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunFlowBatchSelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "with-mask-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunWithMaskSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "calc-mean-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunCalcMeanSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "us-lag-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunUsLagSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "flow-instance-seg-filter-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunFlowInstanceSegFilterSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "dvst-double-load-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunDvstDoubleLoadSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "flow-stage-compare", StringComparison.OrdinalIgnoreCase))
                    return RunFlowStageComparison(args);

                if (args != null && args.Length >= 1 && string.Equals(args[0], "shared-index-csharp-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunSharedIndexCSharpSelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "native-c-api-regression-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunNativeCApiRegressionSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "empty-dvs-first-load-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunEmptyDvsFirstLoadSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "shared-index-route-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunSharedIndexRouteSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "shared-index-native-rule-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunSharedIndexNativeRuleSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "free-all-modules-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunCsharpFreeAllModulesSelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "shared-index-review-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunSharedIndexReviewSelfTest(args);
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "shared-index-format-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunSharedIndexFormatSelfTest();
                }

                if (args != null && args.Length >= 1 && string.Equals(args[0], "shared-index-provider-model-selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return RunSharedIndexProviderModelSelfTest();
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

        private static int RunCliAnomalyThresholdSelfTest()
        {
            Console.WriteLine("==== CLI 异常分数阈值自测 ====");
            try
            {
                string demoAssemblyPath = ResolveDemoAssemblyPath();
                if (!File.Exists(demoAssemblyPath))
                {
                    throw new FileNotFoundException("未找到 DlcvDemo 可执行文件，请先构建 DlcvDemo.csproj。", demoAssemblyPath);
                }

                Assembly demoAssembly = Assembly.LoadFrom(demoAssemblyPath);
                Type cliRunnerType = demoAssembly.GetType("DlcvDemo.CliRunner", throwOnError: true);
                MethodInfo thresholdMethod = cliRunnerType.GetMethod(
                    "IsThresholdCheckPassed",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo validationMethod = cliRunnerType.GetMethod(
                    "IsValidationPassed",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo consistencyMethod = cliRunnerType.GetMethod(
                    "AreConsistent",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Type pathSummaryType = cliRunnerType.GetNestedType("PathSummary", BindingFlags.NonPublic);
                MethodInfo addSummaryItemMethod = pathSummaryType?.GetMethod(
                    "Add",
                    BindingFlags.Public | BindingFlags.Instance);
                if (thresholdMethod == null || validationMethod == null || consistencyMethod == null
                    || pathSummaryType == null || addSummaryItemMethod == null)
                {
                    throw new InvalidOperationException("未找到 CLI 阈值验证方法");
                }

                bool anomalyThresholdPassed = InvokeCliThresholdCheck(
                    thresholdMethod,
                    new List<string> { "异常分数" },
                    new List<double> { 0.035134196281433105 },
                    1,
                    new List<string> { "异常分数" },
                    new List<double> { 0.035134196281433105 },
                    1);
                RequireCliThreshold(
                    anomalyThresholdPassed && InvokeCliValidation(validationMethod, true, anomalyThresholdPassed, true),
                    "异常分数低于阈值时未通过验证");

                bool classificationThresholdPassed = InvokeCliThresholdCheck(
                    thresholdMethod,
                    new List<string> { "普通分类" },
                    new List<double> { 0.1 },
                    1,
                    new List<string> { "普通分类" },
                    new List<double> { 0.1 },
                    1);
                RequireCliThreshold(
                    !classificationThresholdPassed && !InvokeCliValidation(validationMethod, true, classificationThresholdPassed, true),
                    "普通分类低分未判定为失败");

                object structuredSummary = Activator.CreateInstance(pathSummaryType);
                object jsonSummary = Activator.CreateInstance(pathSummaryType);
                addSummaryItemMethod.Invoke(structuredSummary, new object[]
                {
                    0.035134196281433105,
                    "异常分数",
                    false,
                    false,
                    0.0,
                    0.0,
                    0.5
                });
                addSummaryItemMethod.Invoke(jsonSummary, new object[]
                {
                    0.045134196281433105,
                    "异常分数",
                    false,
                    false,
                    0.0,
                    0.0,
                    0.5
                });
                bool mismatchConsistent = (bool)consistencyMethod.Invoke(
                    null,
                    new[] { structuredSummary, jsonSummary });
                bool mismatchThresholdPassed = InvokeCliThresholdCheck(
                    thresholdMethod,
                    new List<string> { "异常分数" },
                    new List<double> { 0.035134196281433105 },
                    1,
                    new List<string> { "异常分数" },
                    new List<double> { 0.045134196281433105 },
                    1);
                RequireCliThreshold(
                    !mismatchConsistent
                    && mismatchThresholdPassed
                    && !InvokeCliValidation(validationMethod, mismatchConsistent, mismatchThresholdPassed, true),
                    "两路结果不一致时未判定为失败");

                bool nonFiniteThresholdPassed = InvokeCliThresholdCheck(
                    thresholdMethod,
                    new List<string> { "异常分数" },
                    new List<double> { double.NaN },
                    1,
                    new List<string> { "异常分数" },
                    new List<double> { double.NaN },
                    1);
                RequireCliThreshold(!nonFiniteThresholdPassed, "非有限异常分数未判定为失败");

                Console.WriteLine("CLI 异常分数阈值自测通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("CLI 异常分数阈值自测失败: " + ex.Message);
                return 1;
            }
        }

        private static int RunEnvironmentCliCompatibilitySelfTest()
        {
            Console.WriteLine("==== 环境提醒与 CLI 输出自测 ====");
            try
            {
                string demoAssemblyPath = ResolveDemoAssemblyPath();
                if (!File.Exists(demoAssemblyPath))
                {
                    throw new FileNotFoundException("未找到 DlcvDemo 可执行文件，请先构建 DlcvDemo.csproj。", demoAssemblyPath);
                }

                Assembly demoAssembly = Assembly.LoadFrom(demoAssemblyPath);
                Type mainWindowType = demoAssembly.GetType("DlcvDemo.MainWindow", throwOnError: true);
                MethodInfo environmentFormatter = mainWindowType.GetMethod(
                    "FormatEnvironmentInfoText",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo dogFormatter = mainWindowType.GetMethod(
                    "FormatDogInfoText",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Type cliRunnerType = demoAssembly.GetType("DlcvDemo.CliRunner", throwOnError: true);
                MethodInfo writeExceptionMethod = cliRunnerType.GetMethod(
                    "WriteException",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (environmentFormatter == null || dogFormatter == null || writeExceptionMethod == null)
                {
                    throw new InvalidOperationException("未找到兼容性验证方法");
                }

                var noDogInfo = new JObject
                {
                    ["sentinel"] = new JObject { ["devices"] = new JArray(), ["features"] = new JArray() },
                    ["virbox"] = new JObject { ["devices"] = new JArray(), ["features"] = new JArray() }
                };
                string noDogText = (string)environmentFormatter.Invoke(
                    null,
                    new object[] { "环境检查结果", noDogInfo });
                RequireCliThreshold(
                    noDogText.StartsWith("未检测到加密狗", StringComparison.Ordinal)
                    && noDogText.Contains("环境检查结果")
                    && noDogText.Contains("Sentinel加密狗ID（0个）："),
                    "无加密狗提醒未保留在环境检查结果前面");

                string emptyDogText = (string)dogFormatter.Invoke(null, new object[] { noDogInfo, false });
                RequireCliThreshold(
                    emptyDogText.Contains("Sentinel加密狗ID（0个）：")
                    && emptyDogText.Contains("Sentinel加密狗特性（0个）：")
                    && emptyDogText.Contains("Virbox加密狗ID（0个）：")
                    && emptyDogText.Contains("Virbox加密狗特性（0个）："),
                    "空列表未显示数量");

                var filledDogInfo = new JObject
                {
                    ["sentinel"] = new JObject
                    {
                        ["devices"] = new JArray { "id-1", "id-2" },
                        ["features"] = new JArray { "1", "2", "3" }
                    },
                    ["virbox"] = new JObject
                    {
                        ["devices"] = new JArray { "vb-1" },
                        ["features"] = new JArray { "10", "11" }
                    }
                };
                string filledDogText = (string)dogFormatter.Invoke(null, new object[] { filledDogInfo, false });
                string expectedFilled = JoinTextBoxLines(
                    "Sentinel加密狗ID（2个）：",
                    "[",
                    "  \"id-1\",",
                    "  \"id-2\"",
                    "]",
                    "",
                    "Sentinel加密狗特性（3个）：",
                    "[",
                    "  \"1\",",
                    "  \"2\",",
                    "  \"3\"",
                    "]",
                    "",
                    "Virbox加密狗ID（1个）：",
                    "[",
                    "  \"vb-1\"",
                    "]",
                    "",
                    "Virbox加密狗特性（2个）：",
                    "[",
                    "  \"10\",",
                    "  \"11\"",
                    "]");
                RequireCliThreshold(
                    string.Equals(filledDogText, expectedFilled, StringComparison.Ordinal)
                    && filledDogText.IndexOf('\n') >= 0
                    && filledDogText.Replace("\r\n", string.Empty).IndexOf('\n') < 0,
                    "加密狗列表未使用界面换行");
                RequireTextBoxLineCount(filledDogText, 23, "加密狗列表未在文本框中按行显示");
                RequireTextBoxLineCount(emptyDogText, 11, "空加密狗列表未在文本框中按行显示");

                var dogInfo = new JObject
                {
                    ["sentinel"] = new JObject { ["devices"] = new JArray { "123" }, ["features"] = new JArray() },
                    ["virbox"] = new JObject { ["devices"] = new JArray(), ["features"] = new JArray() }
                };
                string dogText = (string)environmentFormatter.Invoke(
                    null,
                    new object[] { "环境检查结果", dogInfo });
                RequireCliThreshold(
                    string.Equals(dogText, "环境检查结果", StringComparison.Ordinal),
                    "检测到加密狗时环境检查结果被额外改写");

                TextWriter originalOut = Console.Out;
                TextWriter originalError = Console.Error;
                try
                {
                    var capturedOut = new StringWriter(CultureInfo.InvariantCulture);
                    var capturedError = new StringWriter(CultureInfo.InvariantCulture);
                    Console.SetOut(capturedOut);
                    Console.SetError(capturedError);
                    writeExceptionMethod.Invoke(null, new object[] { new InvalidOperationException("测试异常") });
                    RequireCliThreshold(
                        string.IsNullOrEmpty(capturedOut.ToString())
                        && JObject.Parse(capturedError.ToString()).Value<string>("error") == "测试异常",
                        "运行异常 JSON 未写入标准错误");
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalError);
                }

                Console.WriteLine("环境提醒与 CLI 输出自测通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("环境提醒与 CLI 输出自测失败: " + ex.Message);
                return 1;
            }
        }

        private sealed class UiTestOptionsParseResult
        {
            public readonly bool Ok;
            public readonly object Options;
            public readonly string Error;

            public UiTestOptionsParseResult(bool ok, object options, string error)
            {
                Ok = ok;
                Options = options;
                Error = error;
            }
        }

        private static UiTestOptionsParseResult InvokeUiTestOptionsParse(MethodInfo method, string[] args)
        {
            object[] parameters = new object[] { args, null, null };
            bool ok = (bool)method.Invoke(null, parameters);
            return new UiTestOptionsParseResult(ok, parameters[1], parameters[2] as string);
        }

        private static int RunUiTestOptionsSelfTest()
        {
            Console.WriteLine("==== ui-test 参数解析自测 ====");
            try
            {
                return ExecuteUiTestOptionsSelfTest();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ui-test 参数解析自测失败: " + ex.Message);
                return 1;
            }
        }

        private static int ExecuteUiTestOptionsSelfTest()
        {
            string demoAssemblyPath = ResolveDemoAssemblyPath();
            if (!File.Exists(demoAssemblyPath))
            {
                Console.WriteLine("未找到 DlcvDemo 可执行文件: " + demoAssemblyPath);
                Console.WriteLine("请先构建 DlcvDemo.csproj。");
                return 2;
            }

            Assembly demoAssembly = Assembly.LoadFrom(demoAssemblyPath);
            Type optionsType = demoAssembly.GetType("DlcvDemo.UiTestOptions", throwOnError: true);
            MethodInfo tryParseMethod = optionsType.GetMethod(
                "TryParse",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            PropertyInfo screenshotPathProperty = optionsType.GetProperty(
                "ScreenshotPath",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (tryParseMethod == null || screenshotPathProperty == null)
            {
                throw new InvalidOperationException("未找到 UiTestOptions.TryParse 或 ScreenshotPath");
            }

            string root = Path.Combine(Path.GetTempPath(), "DlcvUiTestOptions_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string modelPath = Path.Combine(root, "model.dvt");
                string imagePath = Path.Combine(root, "image.jpg");
                string outputPath = Path.Combine(root, "result.json");
                File.WriteAllText(modelPath, string.Empty);
                File.WriteAllText(imagePath, string.Empty);

                // 正例：--screenshot 使用 .png 后缀。
                string screenshotPng = Path.Combine(root, "window.png");
                UiTestOptionsParseResult pngResult = InvokeUiTestOptionsParse(tryParseMethod, new[]
                {
                    "ui-test",
                    "--model", modelPath,
                    "--image", imagePath,
                    "--output", outputPath,
                    "--screenshot", screenshotPng
                });
                RequireWinFormsSelfTest(pngResult.Ok, "合法 --screenshot 参数被拒绝: " + pngResult.Error);
                string actualScreenshot = Convert.ToString(
                    screenshotPathProperty.GetValue(pngResult.Options, null),
                    CultureInfo.InvariantCulture);
                RequireWinFormsSelfTest(
                    string.Equals(
                        Path.GetFullPath(actualScreenshot),
                        Path.GetFullPath(screenshotPng),
                        StringComparison.OrdinalIgnoreCase),
                    "--screenshot 参数未被正确保存: " + actualScreenshot);

                // 正例：--screenshot 后缀大小写不敏感，且参数可省略。
                string screenshotUpper = Path.Combine(root, "window_upper.PNG");
                UiTestOptionsParseResult upperResult = InvokeUiTestOptionsParse(tryParseMethod, new[]
                {
                    "ui-test",
                    "--model", modelPath,
                    "--image", imagePath,
                    "--output", outputPath,
                    "--screenshot", screenshotUpper
                });
                RequireWinFormsSelfTest(upperResult.Ok, ".PNG 后缀参数被拒绝: " + upperResult.Error);
                UiTestOptionsParseResult noScreenshotResult = InvokeUiTestOptionsParse(tryParseMethod, new[]
                {
                    "ui-test",
                    "--model", modelPath,
                    "--image", imagePath,
                    "--output", outputPath
                });
                RequireWinFormsSelfTest(noScreenshotResult.Ok, "省略 --screenshot 时参数被拒绝: " + noScreenshotResult.Error);
                RequireWinFormsSelfTest(
                    screenshotPathProperty.GetValue(noScreenshotResult.Options, null) == null,
                    "省略 --screenshot 时 ScreenshotPath 应为空");

                // 负例：非 .png 后缀。
                string screenshotBmp = Path.Combine(root, "window.bmp");
                UiTestOptionsParseResult bmpResult = InvokeUiTestOptionsParse(tryParseMethod, new[]
                {
                    "ui-test",
                    "--model", modelPath,
                    "--image", imagePath,
                    "--output", outputPath,
                    "--screenshot", screenshotBmp
                });
                RequireWinFormsSelfTest(
                    !bmpResult.Ok && !string.IsNullOrEmpty(bmpResult.Error)
                        && bmpResult.Error.IndexOf("PNG", StringComparison.Ordinal) >= 0,
                    "非 PNG 的 --screenshot 未被拒绝: " + bmpResult.Error);

                // 负例：--screenshot 与模型、图片、JSON 输出或其临时输出路径相同。
                string[] collisionTargets =
                {
                    modelPath,
                    imagePath,
                    outputPath,
                    outputPath + ".tmp"
                };
                for (int i = 0; i < collisionTargets.Length; i++)
                {
                    UiTestOptionsParseResult collisionResult = InvokeUiTestOptionsParse(tryParseMethod, new[]
                    {
                        "ui-test",
                        "--model", modelPath,
                        "--image", imagePath,
                        "--output", outputPath,
                        "--screenshot", collisionTargets[i]
                    });
                    RequireWinFormsSelfTest(
                        !collisionResult.Ok && !string.IsNullOrEmpty(collisionResult.Error)
                            && collisionResult.Error.IndexOf("不能与输入文件或其他输出文件相同", StringComparison.Ordinal) >= 0,
                        "--screenshot 与输出路径相同未被拒绝: " + collisionTargets[i] + "，错误: " + collisionResult.Error);
                }

                // 负例：--output 的中间 .tmp 写入路径与 --model / --image 重合（避免写中间 JSON 覆盖输入）。
                string collideModelPath = Path.Combine(root, "collide_model.tmp");
                string collideImagePath = Path.Combine(root, "collide_image.tmp");
                File.WriteAllText(collideModelPath, string.Empty);
                File.WriteAllText(collideImagePath, string.Empty);
                string[] outputTempCollisionCases =
                {
                    collideModelPath + "|" + Path.Combine(root, "collide_model"),
                    collideImagePath + "|" + Path.Combine(root, "collide_image")
                };
                for (int i = 0; i < outputTempCollisionCases.Length; i++)
                {
                    string[] parts = outputTempCollisionCases[i].Split('|');
                    string[] outputTempArgs;
                    if (string.Equals(parts[0], collideModelPath, StringComparison.OrdinalIgnoreCase))
                    {
                        outputTempArgs = new[]
                        {
                            "ui-test",
                            "--model", parts[0],
                            "--image", imagePath,
                            "--output", parts[1]
                        };
                    }
                    else
                    {
                        outputTempArgs = new[]
                        {
                            "ui-test",
                            "--model", modelPath,
                            "--image", parts[0],
                            "--output", parts[1]
                        };
                    }
                    UiTestOptionsParseResult outputTempResult = InvokeUiTestOptionsParse(tryParseMethod, outputTempArgs);
                    RequireWinFormsSelfTest(
                        !outputTempResult.Ok && !string.IsNullOrEmpty(outputTempResult.Error)
                            && outputTempResult.Error.IndexOf("不能与输入文件或其他输出文件相同", StringComparison.Ordinal) >= 0,
                        "--output 的 .tmp 中间路径与输入重合未被拒绝: " + parts[0] + "，输出: " + parts[1] + "，错误: " + outputTempResult.Error);
                }

                Console.WriteLine("ui-test 参数解析自测通过");
                return 0;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static int RunWinFormsMainWindowSelfTest()
        {
            Console.WriteLine("==== WinForms 主窗口自测 ====");
            int exitCode = 1;
            Exception threadException = null;
            var thread = new Thread(() =>
            {
                try
                {
                    exitCode = ExecuteWinFormsMainWindowSelfTest();
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
                Console.WriteLine("WinForms 主窗口自测异常: " + threadException);
                return 1;
            }

            return exitCode;
        }

        private static int ExecuteWinFormsMainWindowSelfTest()
        {
            try
            {
                string demoAssemblyPath = ResolveDemoAssemblyPath();
                if (!File.Exists(demoAssemblyPath))
                {
                    Console.WriteLine("未找到 DlcvDemo 可执行文件: " + demoAssemblyPath);
                    Console.WriteLine("请先构建 DlcvDemo.csproj。");
                    return 2;
                }

                Assembly demoAssembly = Assembly.LoadFrom(demoAssemblyPath);
                Type mainWindowType = demoAssembly.GetType("DlcvDemo.MainWindow", throwOnError: true);
                RequireWinFormsSelfTest(
                    typeof(Form).IsAssignableFrom(mainWindowType),
                    "DlcvDemo.MainWindow 不是 WinForms Form（实际类型: " + mainWindowType.FullName
                        + "，基类: " + (mainWindowType.BaseType != null ? mainWindowType.BaseType.FullName : "无") + "）。");
                RequireWinFormsSelfTest(
                    mainWindowType.GetProperty("UiTestExitCode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null,
                    "未找到 MainWindow.UiTestExitCode 属性。");
                RequireWinFormsSelfTest(
                    mainWindowType.GetMethod("FormatEnvironmentInfoText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) != null,
                    "未找到 MainWindow.FormatEnvironmentInfoText 方法。");
                RequireWinFormsSelfTest(
                    mainWindowType.GetMethod("FormatDogInfoText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) != null,
                    "未找到 MainWindow.FormatDogInfoText 方法。");

                Type optionsType = demoAssembly.GetType("DlcvDemo.UiTestOptions", throwOnError: true);
                object options = Activator.CreateInstance(optionsType, true);
                ConstructorInfo optionsConstructor = mainWindowType.GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { optionsType },
                    null);
                RequireWinFormsSelfTest(optionsConstructor != null, "未找到 MainWindow(UiTestOptions) 构造方法。");

                var window = (Form)optionsConstructor.Invoke(new object[] { options });
                RequireWinFormsSelfTest(window != null, "MainWindow(UiTestOptions) 创建失败。");

                MethodInfo updateWindowTitleMethod = mainWindowType.GetMethod(
                    "UpdateWindowTitle",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                RequireWinFormsSelfTest(updateWindowTitleMethod != null, "未找到 MainWindow.UpdateWindowTitle 方法。");
                updateWindowTitleMethod.Invoke(window, null);

                var versionAttribute = demoAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string expectedVersion = versionAttribute != null
                    ? versionAttribute.InformationalVersion
                    : demoAssembly.GetName().Version.ToString();
                RequireWinFormsSelfTest(
                    window.Text.EndsWith(" v" + expectedVersion, StringComparison.Ordinal),
                    "MainWindow 标题未显示完整版本: " + window.Text);

                try
                {
                    // 不显示窗口：设备初始化只挂接在 Shown 之后，未显示则不应启用设备线程。
                    RequireWinFormsSelfTest(!window.Visible, "创建 MainWindow 后不应显示窗口。");
                    RequireWinFormsSelfTest(!window.IsHandleCreated, "创建 MainWindow 后不应创建设备初始化句柄。");

                    Button loadModelButton = RequireWindowButton(window, "button_load_model", "加载模型");
                    Button environmentButton = RequireWindowButton(window, "button_check_environment", "检查环境");
                    Button freeModelButton = RequireWindowButton(window, "button_free_model", "释放模型");
                    // 原生按钮三组配色：蓝色主操作、灰色辅助、红色危险；未禁用且 MouseOver/MouseDown 均不同于基色。
                    RequireNativeFlatButtonState(
                        loadModelButton,
                        "button_load_model",
                        RgbArgb(25, 118, 210),
                        RgbArgb(21, 101, 192),
                        RgbArgb(13, 71, 161),
                        RgbArgb(255, 255, 255));
                    RequireNativeFlatButtonState(
                        environmentButton,
                        "button_check_environment",
                        RgbArgb(231, 235, 239),
                        RgbArgb(216, 222, 228),
                        RgbArgb(199, 207, 215),
                        RgbArgb(38, 50, 56));
                    RequireNativeFlatButtonState(
                        freeModelButton,
                        "button_free_model",
                        RgbArgb(239, 83, 80),
                        RgbArgb(229, 57, 53),
                        RgbArgb(198, 40, 40),
                        RgbArgb(255, 255, 255));

                    CheckBox calcMeanCheckBox = RequireWindowCheckBox(window);
                    RequireWinFormsSelfTest(calcMeanCheckBox.ThreeState, "checkBox_calc_mean 应为标准三态 CheckBox。");
                    RequireWinFormsSelfTest(
                        calcMeanCheckBox.CheckState == CheckState.Indeterminate,
                        "checkBox_calc_mean 默认应为 Indeterminate。");
                    RequireWinFormsSelfTest(
                        string.Equals(calcMeanCheckBox.Text, "计算均值：默认", StringComparison.Ordinal),
                        "checkBox_calc_mean 默认文本异常: " + calcMeanCheckBox.Text);

                    var deviceComboBox = RequireWindowControl(window, "comboBox1") as ComboBox;
                    RequireWinFormsSelfTest(deviceComboBox != null, "comboBox1 不是 ComboBox。");
                    RequireWinFormsSelfTest(deviceComboBox.Items.Count == 0, "未显示窗口时设备列表应保持为空（设备线程未启用）。");
                    RequireWindowDeviceMapEmpty(window);
                    RequireWindowControl(window, "richTextBox1");

                    // 参数输入：三个 NumericUpDown 范围/默认值，threshold 步进 0.05。
                    RequireWindowNumericUpDown(window, "numericUpDown_batch_size", 1m, 1024m, 1m, 0, 1m);
                    RequireWindowNumericUpDown(window, "numericUpDown_threshold", 0m, 1m, 0.5m, 2, 0.05m);
                    RequireWindowNumericUpDown(window, "numericUpDown_num_thread", 1m, 32m, 1m, 0, 1m);

                    // 三态计算均值映射：Indeterminate 不写 calc_mean，Checked 写 true，Unchecked 写 false。
                    MethodInfo overrideMethod = mainWindowType.GetMethod("AddCalcMeanOverride", BindingFlags.NonPublic | BindingFlags.Instance);
                    RequireWinFormsSelfTest(overrideMethod != null, "未找到 MainWindow.AddCalcMeanOverride 方法。");
                    RequireWinFormsSelfTest(
                        !ApplyCalcMeanOverride(overrideMethod, window, CheckState.Indeterminate).ContainsKey("calc_mean"),
                        "三态默认 Indeterminate 不应写入 calc_mean。");
                    RequireWinFormsSelfTest(
                        ApplyCalcMeanOverride(overrideMethod, window, CheckState.Checked).Value<bool>("calc_mean") == true,
                        "Checked 状态应映射 calc_mean=true。");
                    RequireWinFormsSelfTest(
                        ApplyCalcMeanOverride(overrideMethod, window, CheckState.Unchecked).Value<bool>("calc_mean") == false,
                        "Unchecked 状态应映射 calc_mean=false。");

                    // 环境按钮可操作：按钮默认可用且点击处理方法保留即可，不执行真实环境检查。
                    RequireWinFormsSelfTest(
                        mainWindowType.GetMethod("button_check_environment_Click", BindingFlags.NonPublic | BindingFlags.Instance) != null,
                        "未找到环境按钮点击处理 button_check_environment_Click。");

                    // 最小窗口布局：创建句柄但不显示，threshold 与 calc_mean 应完整位于父容器 ClientRectangle 内。
                    RequireWinFormsSelfTest(window.Handle != IntPtr.Zero, "MainWindow 句柄创建失败。");
                    RequireWinFormsSelfTest(!window.Visible, "创建句柄后窗口仍不应显示。");
                    RequireWinFormsSelfTest(
                        ((ComboBox)RequireWindowControl(window, "comboBox1")).Items.Count == 0,
                        "创建句柄后设备列表应保持为空（设备线程未启用）。");
                    RequireWindowDeviceMapEmpty(window);
                    window.Size = window.MinimumSize;
                    window.PerformLayout();
                    RequireControlInsideParent(window, "numericUpDown_threshold");
                    RequireControlInsideParent(window, "checkBox_calc_mean");

                    // 尺寸计算方法：显式按 96/144 DPI 更新最小窗口与目标工作区上限，不显示窗口。
                    RequireWindowSizeLimits(window, 96, 0, 0, 1920, 1040, 1040, 500);
                    RequireWindowSizeLimits(window, 144, 0, 0, 2560, 1380, 1560, 750);

                    // 关闭窗口应释放：Close 后应已 Dispose。
                    window.Close();
                    RequireWinFormsSelfTest(window.IsDisposed, "关闭窗口后 MainWindow 应已 Dispose。");
                }
                finally
                {
                    if (window != null && !window.IsDisposed)
                    {
                        window.Dispose();
                    }
                }

                Console.WriteLine("WinForms 主窗口自测通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("WinForms 主窗口自测失败: " + ex.Message);
                return 1;
            }
        }

        private static void RequireWinFormsSelfTest(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static int RgbArgb(int r, int g, int b)
        {
            return unchecked((int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
        }

        private static void RequireNativeFlatButtonState(
            Button button,
            string fieldName,
            int expectedBackArgb,
            int expectedOverArgb,
            int expectedDownArgb,
            int expectedForeArgb)
        {
            RequireWinFormsSelfTest(button != null, "MainWindow 控件字段 " + fieldName + " 不是 Button。");
            RequireWinFormsSelfTest(button.Enabled, fieldName + " 按钮默认应 Enabled。");
            RequireWinFormsSelfTest(button.FlatStyle == FlatStyle.Flat, fieldName + " 应为原生 Flat 按钮。");
            RequireWinFormsSelfTest(
                button.BackColor.ToArgb() == expectedBackArgb,
                fieldName + " 基色异常: " + button.BackColor.ToArgb().ToString("X8"));
            RequireWinFormsSelfTest(
                button.ForeColor.ToArgb() == expectedForeArgb,
                fieldName + " 前景色异常: " + button.ForeColor.ToArgb().ToString("X8"));
            RequireWinFormsSelfTest(
                button.FlatAppearance.MouseOverBackColor.ToArgb() == expectedOverArgb,
                fieldName + " MouseOver 色异常: " + button.FlatAppearance.MouseOverBackColor.ToArgb().ToString("X8"));
            RequireWinFormsSelfTest(
                button.FlatAppearance.MouseDownBackColor.ToArgb() == expectedDownArgb,
                fieldName + " MouseDown 色异常: " + button.FlatAppearance.MouseDownBackColor.ToArgb().ToString("X8"));
            RequireWinFormsSelfTest(
                button.FlatAppearance.MouseOverBackColor.ToArgb() != button.BackColor.ToArgb()
                    && button.FlatAppearance.MouseDownBackColor.ToArgb() != button.BackColor.ToArgb()
                    && button.FlatAppearance.MouseOverBackColor.ToArgb() != button.FlatAppearance.MouseDownBackColor.ToArgb(),
                fieldName + " MouseOver/MouseDown 应与基色互不相同。");
        }

        private static void RequireWindowNumericUpDown(
            Form window,
            string fieldName,
            decimal minimum,
            decimal maximum,
            decimal value,
            int decimalPlaces,
            decimal increment)
        {
            var numeric = RequireWindowControl(window, fieldName) as NumericUpDown;
            RequireWinFormsSelfTest(numeric != null, fieldName + " 不是 NumericUpDown。");
            RequireWinFormsSelfTest(numeric.Enabled, fieldName + " 默认应 Enabled。");
            RequireWinFormsSelfTest(numeric.Minimum == minimum, fieldName + " Minimum 异常: " + numeric.Minimum);
            RequireWinFormsSelfTest(numeric.Maximum == maximum, fieldName + " Maximum 异常: " + numeric.Maximum);
            RequireWinFormsSelfTest(numeric.Value == value, fieldName + " 默认值异常: " + numeric.Value);
            RequireWinFormsSelfTest(numeric.DecimalPlaces == decimalPlaces, fieldName + " DecimalPlaces 异常: " + numeric.DecimalPlaces);
            RequireWinFormsSelfTest(numeric.Increment == increment, fieldName + " Increment 异常: " + numeric.Increment);
        }

        private static void RequireControlInsideParent(Form window, string fieldName)
        {
            Control control = RequireWindowControl(window, fieldName);
            Control parent = control.Parent;
            RequireWinFormsSelfTest(parent != null, fieldName + " 缺少父容器。");
            int left = control.Left;
            int top = control.Top;
            int right = left + control.Width;
            int bottom = top + control.Height;
            int clientRight = parent.ClientSize.Width;
            int clientBottom = parent.ClientSize.Height;
            RequireWinFormsSelfTest(
                left >= 0 && top >= 0 && right <= clientRight && bottom <= clientBottom,
                fieldName + " 未完整位于父容器 ClientRectangle 内: bounds=["
                    + left + "," + top + "," + right + "," + bottom + "] client=[0,0,"
                    + clientRight + "," + clientBottom + "]");
        }

        private static void RequireWindowSizeLimits(
            Form window,
            int dpi,
            int workX,
            int workY,
            int workWidth,
            int workHeight,
            int expectedMinWidth,
            int expectedMinHeight)
        {
            MethodInfo sizeLimitMethod = window.GetType().GetMethod(
                "UpdateWindowSizeLimits",
                BindingFlags.NonPublic | BindingFlags.Instance);
            RequireWinFormsSelfTest(sizeLimitMethod != null, "未找到 MainWindow.UpdateWindowSizeLimits 方法。");
            sizeLimitMethod.Invoke(
                window,
                new object[] { dpi, new System.Drawing.Rectangle(workX, workY, workWidth, workHeight) });
            RequireWinFormsSelfTest(
                window.MinimumSize.Width == expectedMinWidth && window.MinimumSize.Height == expectedMinHeight,
                dpi + " DPI 最小窗口异常: " + window.MinimumSize);
            RequireWinFormsSelfTest(
                window.MaximumSize.Width == workWidth && window.MaximumSize.Height == workHeight,
                dpi + " DPI 最大窗口异常: " + window.MaximumSize);
        }

        private static Control RequireWindowControl(Form window, string fieldName)
        {
            FieldInfo field = window.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            RequireWinFormsSelfTest(field != null, "MainWindow 缺少控件字段: " + fieldName + "。");
            var control = field.GetValue(window) as Control;
            RequireWinFormsSelfTest(control != null, "MainWindow 控件字段 " + fieldName + " 不是 Control。");
            RequireWinFormsSelfTest(
                string.Equals(control.Name, fieldName, StringComparison.Ordinal),
                "控件字段 " + fieldName + " 的 Name 不一致: " + (control.Name ?? string.Empty));
            return control;
        }

        private static Button RequireWindowButton(Form window, string fieldName, string expectedText)
        {
            var button = RequireWindowControl(window, fieldName) as Button;
            RequireWinFormsSelfTest(button != null, "MainWindow 控件字段 " + fieldName + " 不是 Button。");
            RequireWinFormsSelfTest(
                string.Equals(button.Text, expectedText, StringComparison.Ordinal),
                "MainWindow 控件字段 " + fieldName + " 文本异常: " + button.Text);
            return button;
        }

        private static CheckBox RequireWindowCheckBox(Form window)
        {
            const string fieldName = "checkBox_calc_mean";
            var checkBox = RequireWindowControl(window, fieldName) as CheckBox;
            RequireWinFormsSelfTest(checkBox != null, "MainWindow 控件字段 " + fieldName + " 不是 CheckBox。");
            return checkBox;
        }

        private static void RequireWindowDeviceMapEmpty(Form window)
        {
            FieldInfo mapField = window.GetType().GetField("deviceNameToIdMap", BindingFlags.NonPublic | BindingFlags.Instance);
            RequireWinFormsSelfTest(mapField != null, "MainWindow 缺少 deviceNameToIdMap 字段。");
            var map = mapField.GetValue(window) as System.Collections.IDictionary;
            RequireWinFormsSelfTest(map != null && map.Count == 0, "未显示窗口时设备映射应保持为空（设备线程未启用）。");
        }

        private static JObject ApplyCalcMeanOverride(MethodInfo method, Form window, CheckState state)
        {
            CheckBox checkBox = RequireWindowCheckBox(window);
            checkBox.CheckState = state;
            var data = new JObject();
            method.Invoke(window, new object[] { data });
            return data;
        }

        private static bool InvokeCliThresholdCheck(
            MethodInfo method,
            IList<string> structuredCategories,
            IList<double> structuredScores,
            int structuredBelowThresholdCount,
            IList<string> jsonCategories,
            IList<double> jsonScores,
            int jsonBelowThresholdCount)
        {
            return (bool)method.Invoke(null, new object[]
            {
                structuredCategories,
                structuredScores,
                structuredBelowThresholdCount,
                jsonCategories,
                jsonScores,
                jsonBelowThresholdCount
            });
        }

        private static bool InvokeCliValidation(
            MethodInfo method,
            bool consistent,
            bool thresholdCheckPassed,
            bool meanCheckPassed)
        {
            return (bool)method.Invoke(null, new object[]
            {
                consistent,
                thresholdCheckPassed,
                meanCheckPassed
            });
        }

        private static void RequireCliThreshold(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static string JoinTextBoxLines(params string[] lines)
        {
            return string.Join("\r\n", lines);
        }

        private static void RequireTextBoxLineCount(string text, int expectedLineCount, string message)
        {
            using (var box = new TextBox())
            {
                box.Multiline = true;
                box.Text = text;
                RequireCliThreshold(box.Lines.Length == expectedLineCount, message);
            }
        }

        private static int RunAllTests(string[] args)
        {
            if (args == null || (args.Length != 1 && args.Length != 2 && args.Length != 4))
            {
                Console.WriteLine("用法: all-tests [日志路径] 或 all-tests <日志路径> <dlcv_infer.dll绝对路径> <dlcv_infer_v.dll绝对路径>");
                return 2;
            }

            string logPath;
            try
            {
                logPath = args.Length >= 2
                    ? Path.GetFullPath(args[1])
                    : Path.Combine(Path.GetTempPath(), "OpenIVS-DlcvCSharpTest-" + Guid.NewGuid().ToString("N") + "-all-tests.log");
                string logDirectory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(logDirectory)) Directory.CreateDirectory(logDirectory);
            }
            catch (Exception ex)
            {
                Console.WriteLine("日志路径无效: " + ex.Message);
                return 2;
            }

            var tests = new List<UnifiedTestCase>
            {
                new UnifiedTestCase("模型通道顺序", RunModelChannelOrderSelfTest),
                new UnifiedTestCase("流程推理参数类型", RunFlowInferParamsSelfTest),
                new UnifiedTestCase("DVS 同名成员内容", DvsArchiveDuplicateSelfTest.Run),
                new UnifiedTestCase("过小模型文件拒绝", RunUndersizedModelSelfTest),
                new UnifiedTestCase("掩膜旋转框", RunMaskToRBoxSelfTest),
                new UnifiedTestCase("掩码面积与 JSON 输出", MaskAreaSelfTest.Run),
                new UnifiedTestCase("区域掩码筛选", RegionMaskSelfTest.Run),
                new UnifiedTestCase("曲线文字仿射变换", RunCurveTextAffineSelfTest),
                new UnifiedTestCase("AI方向仿射变换", RunAiOrientationAffineSelfTest),
                new UnifiedTestCase("检测框重复结果过滤", RunBBoxIoUDedupSelfTest),
                new UnifiedTestCase("结果数量检查", RunCountResultsSelfTest),
                new UnifiedTestCase("类别数量检查", RunCategoryCountCheckSelfTest),
                new UnifiedTestCase("模板数量优先级", RunTemplateCountPrioritySelfTest),
                new UnifiedTestCase("生成图扩展", RunImageGenerationExpandSelfTest),
                new UnifiedTestCase("跨模型标签合并", RunCrossModelLabelMergeSelfTest),
                new UnifiedTestCase("矩形图像矫正", RunRectImageCorrectionSelfTest),
                new UnifiedTestCase("Demo2路由规则", RunDemo2RouteRuleSelfTest),
                new UnifiedTestCase("掩膜输出开关", RunWithMaskSelfTest),
                new UnifiedTestCase("均值计算", RunCalcMeanSelfTest),
                new UnifiedTestCase("共享 index 审查检查", () => RunSharedIndexReviewSelfTest(new[] { "shared-index-review-selftest" })),
                new UnifiedTestCase("共享 index 纯规则与查询选择", RunSharedIndexRouteSelfTest),
                new UnifiedTestCase("C++ 共享规则与正式 C ABI", RunNativeRulesAndCApiRegressionSelfTest),
                new UnifiedTestCase("C# 全部 DLL 模块释放", () => RunCsharpFreeAllModulesSelfTest(new[] { "free-all-modules-selftest" })),
                new UnifiedTestCase("共享 index 格式覆盖", RunSharedIndexFormatSelfTest),
                new UnifiedTestCase("共享 index 双 provider 普通模型", RunSharedIndexProviderModelSelfTest),
                new UnifiedTestCase("环境提醒与 CLI 输出", RunEnvironmentCliCompatibilitySelfTest),
                new UnifiedTestCase("ui-test 参数解析", RunUiTestOptionsSelfTest),
                new UnifiedTestCase("WinForms 主窗口自测", RunWinFormsMainWindowSelfTest),
                new UnifiedTestCase("固定模型结果回归", RunDefaultCases)
            };

            var results = new List<UnifiedTestResult>(tests.Count);
            TextWriter consoleOutput = Console.Out;
            var totalWatch = Stopwatch.StartNew();
            int setupExitCode = 0;
            bool nativePathsValid = true;
            string originalWorkingDirectory = Environment.CurrentDirectory;
            Dictionary<string, string> selectedNativePaths = null;
            try
            {
                using (var log = new StreamWriter(logPath, false, new UTF8Encoding(false)))
                {
                    log.AutoFlush = true;
                    Console.SetOut(log);
                    Console.WriteLine("==== C# 统一测试详细输出 ====");
                    Console.WriteLine("开始时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    Console.WriteLine("用例数量: " + tests.Count);
                    Console.WriteLine("DLL选择模式: " + (args.Length == 4 ? "显式候选路径" : "默认 SDK"));

                    if (args.Length == 4)
                    {
                        try
                        {
                            selectedNativePaths = PrepareExplicitNativeDlls(args[2], args[3]);
                        }
                        catch (Exception ex)
                        {
                            setupExitCode = 1;
                            Console.WriteLine("显式 DLL 选择失败: " + ex.Message);
                            Console.WriteLine("完整测试未开始。");
                        }
                    }

                    if (setupExitCode == 0)
                    {
                        foreach (var test in tests)
                        {
                            Console.WriteLine();
                            Console.WriteLine("==== 开始: " + test.Name + " ====");
                            var watch = Stopwatch.StartNew();
                            int exitCode = 1;
                            string error = null;
                            try
                            {
                                exitCode = test.Run();
                            }
                            catch (Exception ex)
                            {
                                error = ex.ToString();
                                Console.WriteLine("测试异常: " + error);
                            }
                            finally
                            {
                                watch.Stop();
                                try
                                {
                                    Utils.FreeAllModels();
                                }
                                catch (Exception ex)
                                {
                                    exitCode = 1;
                                    error = (error == null ? string.Empty : error + Environment.NewLine) + ex;
                                    Console.WriteLine("释放模型异常: " + ex);
                                }
                                ForceGc();
                            }

                            results.Add(new UnifiedTestResult
                            {
                                Name = test.Name,
                                ExitCode = exitCode,
                                ElapsedMilliseconds = watch.ElapsedMilliseconds,
                                Error = error
                            });
                            Console.WriteLine("==== 结束: " + test.Name + "，状态=" + (exitCode == 0 ? "通过" : "失败")
                                + "，耗时=" + watch.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + "秒 ====");
                        }
                    }

                    if (selectedNativePaths != null)
                    {
                        try
                        {
                            ValidateExplicitNativeModulePaths(selectedNativePaths);
                        }
                        catch (Exception ex)
                        {
                            nativePathsValid = false;
                            Console.WriteLine("显式 DLL 路径检查失败: " + ex.Message);
                        }
                    }
                    try
                    {
                        ValidateLoaderNativeModulePaths();
                    }
                    catch (Exception ex)
                    {
                        nativePathsValid = false;
                        Console.WriteLine("Loader 实际路径检查失败: " + ex.Message);
                    }
                    WriteLoadedNativeModulePaths();
                    totalWatch.Stop();
                    Console.WriteLine();
                    Console.WriteLine("==== C# 统一测试详细输出结束 ====");
                }
            }
            catch (Exception ex)
            {
                totalWatch.Stop();
                Console.SetOut(consoleOutput);
                Console.WriteLine("统一测试无法写入日志: " + ex.Message);
                return 2;
            }
            finally
            {
                Console.SetOut(consoleOutput);
                Environment.CurrentDirectory = originalWorkingDirectory;
            }

            if (setupExitCode != 0)
            {
                Console.WriteLine("显式 DLL 选择失败，详细日志: " + logPath);
                return setupExitCode;
            }

            int passed = results.Count(r => r.ExitCode == 0);
            Console.WriteLine("==== C# 统一测试汇总 ====");
            Console.WriteLine("总数: " + results.Count + "，通过: " + passed + "，失败: " + (results.Count - passed));
            foreach (var result in results)
            {
                Console.WriteLine("[" + (result.ExitCode == 0 ? "通过" : "失败") + "] " + result.Name
                    + "，耗时=" + (result.ElapsedMilliseconds / 1000.0).ToString("F2", CultureInfo.InvariantCulture) + "秒"
                    + (string.IsNullOrEmpty(result.Error) ? string.Empty : "，异常已记录"));
            }
            Console.WriteLine("总耗时: " + totalWatch.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + "秒");
            Console.WriteLine("详细日志: " + logPath);
            if (!nativePathsValid) Console.WriteLine("DLL 实际路径检查失败，本轮测试结果无效。");
            return passed == results.Count && nativePathsValid ? 0 : 1;
        }

        private static Dictionary<string, string> PrepareExplicitNativeDlls(string sentinelPathArgument, string virboxPathArgument)
        {
            var requested = new[]
            {
                Tuple.Create(sentinelPathArgument, "dlcv_infer.dll"),
                Tuple.Create(virboxPathArgument, "dlcv_infer_v.dll")
            };
            var paths = new List<Tuple<string, string>>(requested.Length);

            foreach (Tuple<string, string> item in requested)
            {
                if (string.IsNullOrWhiteSpace(item.Item1))
                    throw new InvalidOperationException("显式 DLL 路径不能为空: " + item.Item2);

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(item.Item1);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("显式 DLL 路径无效: " + item.Item1, ex);
                }

                if (!File.Exists(fullPath))
                    throw new FileNotFoundException("显式 DLL 不存在: " + fullPath, fullPath);
                if (!string.Equals(Path.GetFileName(fullPath), item.Item2, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "显式 DLL 文件名错误: 需要 " + item.Item2 + "，实际为 " + Path.GetFileName(fullPath));
                }
                paths.Add(Tuple.Create(fullPath, item.Item2));
            }

            string nativeDirectory = Path.GetDirectoryName(paths[0].Item1);
            if (!string.Equals(nativeDirectory, Path.GetDirectoryName(paths[1].Item1), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("完整测试的两个推理 DLL 必须来自同一个 SDK 目录。");

            // C++ 普通加载优先读取当前工作目录，显式测试保持两种语言使用同一套 SDK。
            Environment.CurrentDirectory = nativeDirectory;
            Console.WriteLine("显式 SDK 工作目录: " + nativeDirectory);

            foreach (Tuple<string, string> item in paths)
            {
                IntPtr moduleHandle = LoadNativeModule(item.Item1);
                if (moduleHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        "无法加载显式 DLL " + item.Item1 + (error == 0 ? string.Empty : "，Win32错误=" + error));
                }

                string actualPath = GetActualNativeModulePath(moduleHandle);
                if (!string.Equals(actualPath, item.Item1, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "显式 DLL 实际加载路径不一致: 选择=" + item.Item1 + "，实际=" + actualPath);
                }

                var missing = new List<string>();
                foreach (string exportName in SharedIndexNativeExports)
                {
                    if (GetNativeProcAddress(moduleHandle, exportName) == IntPtr.Zero)
                        missing.Add(exportName);
                }
                if (missing.Count > 0)
                {
                    throw new MissingMethodException(
                        "显式 DLL 缺少完整共享接口: " + item.Item1 + "，缺少 " + string.Join("、", missing));
                }

                Console.WriteLine("显式选择 " + item.Item2 + " 实际路径: " + actualPath);
            }

            var selectedPaths = paths.ToDictionary(item => item.Item2, item => item.Item1, StringComparer.OrdinalIgnoreCase);
            selectedPaths.Add("dlcv_infer_cpp.dll", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dlcv_infer_cpp.dll"));
            ValidateExplicitNativeModulePaths(selectedPaths);
            return selectedPaths;
        }

        private static void ValidateExplicitNativeModulePaths(IDictionary<string, string> selectedPaths)
        {
            using (Process process = Process.GetCurrentProcess())
            {
                foreach (ProcessModule module in process.Modules)
                {
                    string selectedPath;
                    if (!selectedPaths.TryGetValue(module.ModuleName, out selectedPath)) continue;
                    string actualPath = GetActualNativeModulePath(module.BaseAddress);
                    if (!string.Equals(actualPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "进程加载了非本次选择的 DLL: " + actualPath + "，本次选择=" + selectedPath);
                    }
                }
            }
        }

        private static string GetActualNativeModulePath(IntPtr moduleHandle)
        {
            var buffer = new StringBuilder(32768);
            uint length = GetModuleFileNameW(moduleHandle, buffer, (uint)buffer.Capacity);
            if (length == 0)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    "无法读取已加载 DLL 的实际路径" + (error == 0 ? string.Empty : "，Win32错误=" + error));
            }
            return buffer.ToString();
        }

        private static void ValidateLoaderNativeModulePaths()
        {
            using (Process process = Process.GetCurrentProcess())
            {
                var modules = process.Modules.Cast<ProcessModule>().ToList();
                foreach (DllLoader loader in DllLoader.GetLoadedLoaders())
                {
                    ProcessModule module = modules.SingleOrDefault(candidate =>
                        string.Equals(candidate.FileName, loader.LoadedNativeModulePath, StringComparison.OrdinalIgnoreCase));
                    if (module == null || !string.Equals(GetActualNativeModulePath(module.BaseAddress),
                        loader.LoadedNativeModulePath, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("Loader 记录的路径不是实际已加载模块：" + loader.LoadedNativeModulePath);
                }
            }
        }

        private static void WriteLoadedNativeModulePaths()
        {
            Console.WriteLine();
            Console.WriteLine("==== 进程内实际已加载的推理 DLL ====");
            var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "dlcv_infer.dll",
                "dlcv_infer_v.dll",
                "dlcv_infer_cpp.dll"
            };

            try
            {
                var modules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                    .Where(module => targetNames.Contains(module.ModuleName))
                    .OrderBy(module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(module => module.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (modules.Count == 0)
                {
                    Console.WriteLine("未找到目标 DLL");
                    return;
                }

                foreach (ProcessModule module in modules)
                {
                    string actualPath;
                    try
                    {
                        actualPath = GetActualNativeModulePath(module.BaseAddress);
                    }
                    catch
                    {
                        actualPath = module.FileName;
                    }
                    Console.WriteLine(module.ModuleName + ": " + actualPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("读取进程内已加载 DLL 路径失败: " + ex.Message);
            }
        }

        private static int RunSharedIndexCSharpSelfTest(string[] args)
        {
            if (args == null || args.Length < 4)
            {
                Console.WriteLine("用法: DlcvCSharpTest shared-index-csharp-selftest <model.dvo> <flow.dvst> <image> [deviceId]");
                return 2;
            }

            string dvoPath = args[1];
            string dvstPath = args[2];
            string imagePath = args[3];
            int deviceId = GpuDeviceId;
            if (args.Length >= 5 && !int.TryParse(args[4], out deviceId))
            {
                Console.WriteLine("deviceId 必须是整数");
                return 2;
            }
            if (!File.Exists(dvoPath) || !File.Exists(dvstPath) || !File.Exists(imagePath))
            {
                Console.WriteLine("模型或图片不存在");
                return 2;
            }

            Mat bgr = null;
            Mat rgb = null;
            try
            {
                bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (bgr == null || bgr.Empty()) throw new Exception("图片解码失败");
                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
                var inferParams = new JObject
                {
                    ["threshold"] = 0.05,
                    ["with_mask"] = true
                };

                RunCSharpOwnerCppBorrowerCase(dvoPath, imagePath, rgb, inferParams, deviceId, "model", "C# owner DVO→C++");
                RunCppOwnerCSharpBorrowerCase(dvoPath, imagePath, rgb, inferParams, deviceId, "model", "C++ owner DVO→C#");
                RunCSharpOwnerCppBorrowerCase(dvstPath, imagePath, rgb, inferParams, deviceId, "dvs", "C# owner DVST→C++");
                RunCppOwnerCSharpBorrowerCase(dvstPath, imagePath, rgb, inferParams, deviceId, "dvs", "C++ owner DVST→C#");

                Console.WriteLine("shared-index-csharp-selftest 通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("shared-index-csharp-selftest 失败: " + ex);
                return 1;
            }
            finally
            {
                if (rgb != null) rgb.Dispose();
                if (bgr != null) bgr.Dispose();
            }
        }

        private static int RunSharedIndexFormatSelfTest()
        {
            string dvtPath = Path.Combine(ModelRoot, "猫狗-分类_PLUS_s.dvt");
            string dvoPath = Path.Combine(ModelRoot, "猫狗-分类_s.dvo");
            string dvstPath = Path.Combine(ModelRoot, "AOI-元件提取_PLUS_s.dvst");
            string modelImagePath = Path.Combine(ModelRoot, "猫狗-猫.jpg");
            string flowImagePath = Path.Combine(ModelRoot, "AOI-测试.jpg");
            foreach (string path in new[] { dvtPath, dvoPath, dvstPath, modelImagePath, flowImagePath })
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine("测试文件不存在: " + path);
                    return 2;
                }
            }

            try
            {
                RunSharedIndexFormatCase(dvtPath, modelImagePath, "model", "DVT 双向共享 index");
                Utils.FreeAllModels();
                RunSharedIndexFormatCase(dvoPath, modelImagePath, "model", "DVO 双向共享 index");
                Utils.FreeAllModels();
                RunSharedIndexFormatCase(dvstPath, flowImagePath, "dvs", "DVST 双向共享 index");
                Console.WriteLine("shared-index-format-selftest 通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("shared-index-format-selftest 失败: " + ex);
                return 1;
            }
            finally
            {
                Utils.FreeAllModels();
            }
        }

        private static int RunSharedIndexProviderModelSelfTest()
        {
            string sentinelPath = Path.Combine(ModelRoot, "猫狗-分类_PLUS_s.dvt");
            string virboxPath = Path.Combine(ModelRoot, "猫狗-分类_PLUS_v.dvt");
            string imagePath = Path.Combine(ModelRoot, "猫狗-猫.jpg");
            foreach (string path in new[] { sentinelPath, virboxPath, imagePath })
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine("测试文件不存在: " + path);
                    return 2;
                }
            }

            Model first = null;
            Model second = null;
            try
            {
                first = new Model(sentinelPath, GpuDeviceId, false, false);
                DllLoader defaultLoader = first.Loader;
                if (defaultLoader == null || defaultLoader.LoadedDogProvider != DogProvider.Sentinel)
                    throw new Exception("首次 Sentinel 模型头未选定 Sentinel 默认 DLL");
                first.GetModelInfo();
                first.Dispose();
                first = null;

                second = new Model(virboxPath, GpuDeviceId, false, false);
                if (!ReferenceEquals(second.Loader, defaultLoader) || !ReferenceEquals(DllLoader.Instance, defaultLoader))
                    throw new Exception("后续 Virbox 模型改变了已选定的默认 DLL");
                second.GetModelInfo();
                second.Dispose();
                second = null;

                RunSharedIndexFormatCase(sentinelPath, imagePath, "model", "Sentinel 头普通模型双向共享 index");
                Utils.FreeAllModels();
                RunSharedIndexFormatCase(virboxPath, imagePath, "model", "Virbox 头普通模型授权与双向共享 index");
                if (!ReferenceEquals(DllLoader.Instance, defaultLoader))
                    throw new Exception("双 provider 模型加载后默认 DLL 发生变化");
                Console.WriteLine("shared-index-provider-model-selftest 通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("shared-index-provider-model-selftest 失败: " + ex);
                return 1;
            }
            finally
            {
                second?.Dispose();
                first?.Dispose();
                Utils.FreeAllModels();
            }
        }

        private static void RunSharedIndexFormatCase(
            string modelPath,
            string imagePath,
            string expectedIndexType,
            string label)
        {
            using (Mat bgr = Cv2.ImRead(imagePath, ImreadModes.Color))
            using (var rgb = new Mat())
            {
                if (bgr == null || bgr.Empty()) throw new Exception(label + " 图片解码失败");
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
                var inferParams = new JObject
                {
                    ["threshold"] = 0.05,
                    ["with_mask"] = true
                };
                RunCSharpOwnerCppBorrowerCase(
                    modelPath,
                    imagePath,
                    rgb,
                    inferParams,
                    GpuDeviceId,
                    expectedIndexType,
                    label + "，C# 持有");
                RunCppOwnerCSharpBorrowerCase(
                    modelPath,
                    imagePath,
                    rgb,
                    inferParams,
                    GpuDeviceId,
                    expectedIndexType,
                    label + "，C++ 持有");
            }
        }

        private static int RunEmptyDvsFirstLoadSelfTest()
        {
            string directory = Path.Combine(Path.GetTempPath(), "OpenIVS-empty-dvs-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "empty.dvst");
            Model model = null;
            try
            {
                if (DllLoader.GetLoadedLoaders().Count != 0)
                    throw new Exception("空 DVS 首次加载检查须在推理 DLL 尚未加载时执行");

                Directory.CreateDirectory(directory);
                JObject pipeline = new JObject
                {
                    ["nodes"] = new JArray(),
                    ["edges"] = new JArray()
                };
                byte[] pipelineBytes = Encoding.UTF8.GetBytes(pipeline.ToString(Formatting.None));
                JObject header = new JObject
                {
                    ["file_list"] = new JArray("pipeline.json"),
                    ["file_size"] = new JArray(pipelineBytes.Length)
                };
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] prefix = Encoding.ASCII.GetBytes("DV\n");
                    byte[] headerBytes = Encoding.UTF8.GetBytes(header.ToString(Formatting.None) + "\n");
                    stream.Write(prefix, 0, prefix.Length);
                    stream.Write(headerBytes, 0, headerBytes.Length);
                    stream.Write(pipelineBytes, 0, pipelineBytes.Length);
                }

                model = new Model(path, GpuDeviceId);
                if (model.modelIndex < 0 || model.LoadedDogProvider != DogProvider.Sentinel)
                    throw new Exception("空 DVS 首次加载未选择 Sentinel 模块");
                JObject info = model.GetDvsModelInfo();
                ValidateDvsDescriptorShape(info, model.modelIndex, "空 DVS 信息");
                Console.WriteLine("empty-dvs-first-load-selftest 通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("empty-dvs-first-load-selftest 失败: " + ex);
                return 1;
            }
            finally
            {
                try { model?.Dispose(); } catch { }
                try { Utils.FreeAllModels(); } catch { }
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { }
            }
        }

        private static int RunSharedIndexRouteSelfTest()
        {
            try
            {
                Func<string, IntPtr> allocUtf8 = value =>
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(value + "\0");
                    IntPtr pointer = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, pointer, bytes.Length);
                    return pointer;
                };
                Func<int, IntPtr> modelInfo = index =>
                    Marshal.StringToHGlobalAnsi("{\"code\":0,\"message\":\"success\",\"model_info\":{}}");
                Func<string, IntPtr> freeResult = json =>
                    Marshal.StringToHGlobalAnsi("{\"code\":0,\"message\":\"success\"}");
                Func<IntPtr, int> readIndex = pointer =>
                {
                    JObject request = JObject.Parse(ReadUtf8String(pointer));
                    if (request.Count != 1 || request["model_index"] == null ||
                        request["model_index"].Type != JTokenType.Integer)
                    {
                        throw new InvalidDataException("共享索引请求必须只包含整数 model_index");
                    }
                    return request["model_index"].Value<int>();
                };
                Func<int, string, IntPtr> typeResult = (index, type) => allocUtf8(
                    "{\"code\":0,\"message\":\"success\",\"model_index\":" + index +
                    ",\"resource_type\":\"" + type + "\"}");
                Func<int, IntPtr> missingResult = index => allocUtf8(
                    "{\"code\":2,\"message\":\"not found\"}");
                DllLoader.SharedIndexJsonDelegate modelQuery = pointer =>
                {
                    int index = readIndex(pointer);
                    return index == 256 ? typeResult(index, "model") : missingResult(index);
                };
                DllLoader.SharedIndexJsonDelegate dvsQuery = pointer =>
                {
                    int index = readIndex(pointer);
                    return index == 512 ? typeResult(index, "dvs") : missingResult(index);
                };
                DllLoader.SharedIndexJsonDelegate bindIndex = pointer =>
                {
                    int index = readIndex(pointer);
                    return typeResult(index, index == 512 ? "dvs" : "model");
                };

                var modelLoader = new DllLoader
                {
                    dlcv_get_index_type = modelQuery,
                    dlcv_bind_index = bindIndex,
                    dlcv_get_model_info = json => modelInfo(256),
                    dlcv_free_model = json => freeResult(json),
                    dlcv_free_result = Marshal.FreeHGlobal
                };
                var dvsLoader = new DllLoader
                {
                    dlcv_get_index_type = dvsQuery,
                    dlcv_bind_index = bindIndex,
                    dlcv_get_model_info = json => modelInfo(512),
                    dlcv_free_model = json => freeResult(json),
                    dlcv_register_dvs_model = json => typeResult(512, "dvs"),
                    dlcv_get_dvs_model = pointer =>
                    {
                        int index = readIndex(pointer);
                        return allocUtf8(
                            "{\"code\":0,\"message\":\"success\",\"schema_version\":1," +
                            "\"dvs_type\":\"dvst\",\"model_path\":\"\",\"device_id\":0," +
                            "\"pipeline\":{\"nodes\":[],\"edges\":[]},\"model_bindings\":[]," +
                            "\"model_index\":" + index + ",\"resource_type\":\"dvs\"}");
                    },
                    dlcv_get_all_models = () => allocUtf8(
                        "{\"code\":0,\"message\":\"success\",\"provider\":\"sentinel\"," +
                        "\"models\":[{\"model_index\":512,\"resource_type\":\"dvs\"," +
                        "\"model_paths\":[],\"device_id\":0}]}"),
                    dlcv_free_result = Marshal.FreeHGlobal
                };

                string indexType;
                DllLoader selected = DllLoader.ResolveSharedIndexLoaderFromCandidates(
                    256, new List<DllLoader> { modelLoader }, out indexType);
                if (!ReferenceEquals(selected, modelLoader) || indexType != "model")
                    throw new Exception("普通模型唯一模块选择错误");

                selected = DllLoader.ResolveSharedIndexLoaderFromCandidates(
                    512, new List<DllLoader> { dvsLoader }, out indexType);
                if (!ReferenceEquals(selected, dvsLoader) || indexType != "dvs")
                    throw new Exception("DVS 唯一模块选择错误");
                if (!dvsLoader.SupportsDvsRegistration)
                    throw new Exception("最终 DVS 接口未被识别");

                EnsureThrows<InvalidOperationException>(
                    () => DllLoader.ResolveSharedIndexLoaderFromCandidates(
                        256,
                        new List<DllLoader>
                        {
                            modelLoader,
                            new DllLoader { dlcv_get_index_type = modelQuery, dlcv_free_result = Marshal.FreeHGlobal }
                        },
                        out indexType),
                    "多模块同编号未报告错误");

                bool secondQueried = false;
                EnsureThrows<InvalidOperationException>(
                    () => DllLoader.ResolveSharedIndexLoaderFromCandidates(
                        256,
                        new List<DllLoader>
                        {
                            new DllLoader
                            {
                                dlcv_get_index_type = pointer => throw new InvalidOperationException("query failed"),
                                dlcv_free_result = Marshal.FreeHGlobal
                            },
                            new DllLoader
                            {
                                dlcv_get_index_type = pointer =>
                                {
                                    secondQueried = true;
                                    return typeResult(readIndex(pointer), "model");
                                },
                                dlcv_free_result = Marshal.FreeHGlobal
                            }
                        },
                        out indexType),
                    "查询异常未直接返回");
                if (secondQueried)
                    throw new Exception("查询异常后切换到了其他模块");

                EnsureThrows<InvalidOperationException>(
                    () => DllLoader.ResolveSharedIndexLoaderFromCandidates(
                        256,
                        new List<DllLoader>
                        {
                            new DllLoader
                            {
                                dlcv_get_index_type = pointer => allocUtf8(
                                    "{\"code\":0,\"message\":\"success\",\"model_index\":256," +
                                    "\"resource_type\":\"unknown\"}"),
                                dlcv_free_result = Marshal.FreeHGlobal
                            }
                        },
                        out indexType),
                    "错误类型返回值未拒绝");
                EnsureThrows<NotSupportedException>(
                    () => DllLoader.ResolveSharedIndexLoaderFromCandidates(
                        256, new List<DllLoader> { new DllLoader() }, out indexType),
                    "缺少查询接口未拒绝");
                EnsureThrows<ArgumentOutOfRangeException>(
                    () => DllLoader.ResolveSharedIndexLoaderFromCandidates(
                        -1, new List<DllLoader> { modelLoader }, out indexType),
                    "负数 index 未拒绝");

                var incompleteDvs = new DllLoader
                {
                    dlcv_get_index_type = dvsQuery,
                    dlcv_bind_index = bindIndex,
                    dlcv_get_model_info = json => modelInfo(0),
                    dlcv_free_model = json => freeResult(json),
                    dlcv_free_result = Marshal.FreeHGlobal
                };
                if (incompleteDvs.SupportsDvsRegistration)
                    throw new Exception("缺少 DVS 登记接口时错误启用最终能力");
                EnsureThrows<NotSupportedException>(
                    () => incompleteDvs.EnsureSharedIndexSupport("dvs"),
                    "缺少 DVS 查询接口时未拒绝恢复");

                JObject descriptor = new JObject
                {
                    ["schema_version"] = 1,
                    ["dvs_type"] = "dvst",
                    ["model_path"] = string.Empty,
                    ["device_id"] = 0,
                    ["pipeline"] = new JObject { ["nodes"] = new JArray(), ["edges"] = new JArray() },
                    ["model_bindings"] = new JArray()
                };
                string registeredJson = null;
                dvsLoader.dlcv_register_dvs_model = pointer =>
                {
                    registeredJson = ReadUtf8String(pointer);
                    return typeResult(512, "dvs");
                };
                if (dvsLoader.RegisterDvsModel(descriptor.ToString(Formatting.None)) != 512)
                    throw new Exception("DVS 登记返回值错误");
                JObject registered = JObject.Parse(registeredJson);
                if (registered["model_index"] != null || registered["provider"] != null ||
                    registered["dvs_type"]?.ToString() != "dvst")
                    throw new Exception("DVS 登记字段不符合最终结构");

                JObject queried = dvsLoader.GetDvsModel(512);
                if (queried["model_index"]?.Value<int>() != 512 ||
                    queried["resource_type"]?.ToString() != "dvs")
                    throw new Exception("DVS 查询结果错误");
                JObject snapshot = dvsLoader.GetAllModelsSnapshot();
                if (!(snapshot["models"] is JArray models) || models.Count != 1)
                    throw new Exception("模型快照结果错误");

                int childBindCount = 0;
                int childFreeCount = 0;
                modelLoader.dlcv_bind_index = pointer =>
                {
                    childBindCount++;
                    return typeResult(readIndex(pointer), "model");
                };
                modelLoader.dlcv_free_model = json =>
                {
                    childFreeCount++;
                    return freeResult(json);
                };
                using (var child = Model.CreateBorrowedDvsChild(256, modelLoader))
                {
                    JObject info = child.GetModelInfo();
                    if (info["code"]?.Value<int>() != 0 || info["model_index"]?.Value<int>() != 256)
                        throw new Exception("DVS 子模型信息缺少普通模型 index");
                }
                if (childBindCount != 0 || childFreeCount != 0)
                    throw new Exception("DVS 子模型执行对象重复增加或释放了使用记录");

                int freeCalls = 0;
                modelLoader.dlcv_free_model = json =>
                {
                    freeCalls++;
                    return freeResult(json);
                };
                EnsureNativeJsonSuccess(modelLoader.FreeModelIndex(256), "第一次统一释放");
                EnsureNativeJsonSuccess(modelLoader.FreeModelIndex(256), "第二次统一释放");
                if (freeCalls != 2)
                    throw new Exception("统一释放入口调用次数错误");

                int getterCalls = 0;
                var missingResultFree = new DllLoader
                {
                    dlcv_get_model_info = json => { getterCalls++; return IntPtr.Zero; }
                };
                EnsureThrows<MissingMethodException>(
                    () => missingResultFree.GetModelInfoByIndex(0),
                    "缺少结果释放接口时未拒绝模型信息查询");
                if (getterCalls != 0)
                    throw new Exception("缺少结果释放接口时仍调用了模型信息函数");

                int exceptionalFreeCount = 0;
                var malformedDvs = new DllLoader
                {
                    dlcv_get_dvs_model = pointer => allocUtf8("{"),
                    dlcv_free_result = pointer =>
                    {
                        exceptionalFreeCount++;
                        Marshal.FreeHGlobal(pointer);
                    }
                };
                EnsureThrows<JsonReaderException>(
                    () => malformedDvs.GetDvsModel(0),
                    "DVS 查询格式异常未向上返回");
                if (exceptionalFreeCount != 1)
                    throw new Exception("DVS 查询格式异常时未释放底层结果");

                int sharedGetterCalls = 0;
                var missingSharedResultFree = new DllLoader
                {
                    dlcv_get_index_type = pointer =>
                    {
                        sharedGetterCalls++;
                        return IntPtr.Zero;
                    }
                };
                EnsureThrows<MissingMethodException>(
                    () => missingSharedResultFree.GetIndexType(0),
                    "缺少结果释放接口时未拒绝共享索引查询");
                if (sharedGetterCalls != 0)
                    throw new Exception("缺少结果释放接口时仍调用了共享索引函数");
                if (modelLoader.GetIndexType(999) != 0)
                    throw new Exception("code=2 未解析为索引不存在");

                RunSharedResultComparisonChecks();
                Console.WriteLine("shared-index-route-selftest 通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("shared-index-route-selftest 失败: " + ex);
                return 1;
            }
        }

        private static int RunSharedIndexNativeRuleSelfTest()
        {
            string executablePath = Path.Combine(
                ResolveRepoRoot(), "Release", "dlcv_infer_cpp_test.exe");
            if (!File.Exists(executablePath))
            {
                Console.WriteLine("shared-index-native-rule-selftest 未执行：缺少编号脚本产出的 Release 测试程序：" + executablePath);
                return 2;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "shared-index-rules-selftest",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
                    StandardErrorEncoding = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null) throw new InvalidOperationException("无法启动原生规则测试程序");
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (!string.IsNullOrWhiteSpace(stdout)) Console.Write(stdout);
                    if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.Write(stderr);
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine("shared-index-native-rule-selftest 失败，原生程序退出码=" + process.ExitCode);
                        return 1;
                    }
                }
                Console.WriteLine("shared-index-native-rule-selftest 通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("shared-index-native-rule-selftest 失败: " + ex.Message);
                return 1;
            }
        }

        private static void EnsureThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new Exception(message);
        }

        private static void RunCSharpOwnerCppBorrowerCase(
            string modelPath,
            string imagePath,
            Mat image,
            JObject inferParams,
            int deviceId,
            string expectedIndexType,
            string label)
        {
            int index = -1;
            DllLoader loader = null;
            Model owner = null;
            Model keeper = null;
            bool nativeBorrowActive = false;
            try
            {
                owner = new Model(modelPath, deviceId);
                index = owner.modelIndex;
                loader = ResolveAndValidateSharedIndex(index, expectedIndexType, label);
                JObject ownerInfo = owner.GetModelInfo();
                ValidateDvsInfoWhenNeeded(owner, ownerInfo, loader, index, expectedIndexType, label);
                JObject ownerSummary = InferAndSummarize(owner, image, inferParams);
                JToken ownerJson = JToken.FromObject(owner.InferOneOutJson(image, inferParams));

                // 正式 C 信息查询会在模型表中保留借用，直到显式释放。
                nativeBorrowActive = true;
                JObject nativeInfo = NativeCGetModelInfo(index, label + " 正式 C 模型信息");
                EnsureModelInfosMatch(ownerInfo, nativeInfo, label + " 正式 C 模型信息");
                JToken nativeJson = NativeCInferJson(index, image, inferParams, label + " 首次正式 C 推理");
                EnsureNativeJsonResultsMatch(ownerJson, nativeJson, label + " 首次正式 C 推理");

                keeper = CreateBoundCSharpBorrower(index, out JObject keeperInfo);
                EnsureModelInfosMatch(ownerInfo, keeperInfo, label + " C# 持续借用信息");
                JObject keeperBeforeFree = InferAndSummarize(keeper, image, inferParams);
                EnsureResultsMatch(ownerSummary, keeperBeforeFree, label + " C# 持续借用结果");

                owner.Dispose();
                owner = null;
                JObject keeperAfterFree = InferAndSummarize(keeper, image, inferParams);
                EnsureResultsMatch(ownerSummary, keeperAfterFree, label + " 持有方释放后 C# 借用结果");
                JToken nativeAfterOwnerFree = NativeCInferJson(
                    index, image, inferParams, label + " 持有方释放后正式 C 推理");
                EnsureNativeJsonResultsMatch(ownerJson, nativeAfterOwnerFree, label + " 持有方释放后正式 C 推理");

                nativeBorrowActive = false;
                if (NativeCFreeModel(index) != 0)
                    throw new Exception(label + " 正式 C 借用释放失败");
                EnsureResultsMatch(ownerSummary, InferAndSummarize(keeper, image, inferParams),
                    label + " 正式 C 借用释放后 C# 推理");

                keeper.Dispose();
                keeper = null;
                EnsureIndexRemoved(loader, index, label);
            }
            finally
            {
                try
                {
                    if (nativeBorrowActive)
                    {
                        nativeBorrowActive = false;
                        if (NativeCFreeModel(index) != 0)
                            throw new Exception(label + " 清理正式 C 借用失败");
                    }
                }
                finally
                {
                    try { keeper?.Dispose(); }
                    finally { owner?.Dispose(); }
                }
            }
        }

        private static void RunCppOwnerCSharpBorrowerCase(
            string modelPath,
            string imagePath,
            Mat image,
            JObject inferParams,
            int deviceId,
            string expectedIndexType,
            string label)
        {
            int index = NativeCLoadModel(modelPath, deviceId);
            bool ownerActive = true;
            bool nativeBorrowActive = false;
            DllLoader loader = null;
            Model borrowed = null;
            try
            {
                loader = ResolveAndValidateSharedIndex(index, expectedIndexType, label);
                JObject nativeInfo = NativeCGetModelInfo(index, label + " 正式 C 持有方模型信息");
                JToken nativeJson = NativeCInferJson(index, image, inferParams, label + " 正式 C 持有方推理");

                borrowed = CreateBoundCSharpBorrower(index, out JObject borrowedInfo);
                ValidateDvsInfoWhenNeeded(borrowed, borrowedInfo, loader, index, expectedIndexType, label);
                EnsureModelInfosMatch(nativeInfo, borrowedInfo, label + " 模型信息");
                JObject borrowedSummary = InferAndSummarize(borrowed, image, inferParams);
                JToken borrowedJson = JToken.FromObject(borrowed.InferOneOutJson(image, inferParams));
                EnsureNativeJsonResultsMatch(nativeJson, borrowedJson, label + " 首次 C# 推理");
                if (borrowedSummary.Value<int>("sample_count") <= 0)
                    throw new Exception(label + " C# 结构化结果为空");

                // 一次释放即结束本次持有，异常清理不能再次消耗同一持有。
                ownerActive = false;
                if (NativeCFreeModel(index) != 0)
                    throw new Exception(label + " 正式 C 持有方释放失败");

                JObject borrowedAfterFree = InferAndSummarize(borrowed, image, inferParams);
                EnsureResultsMatch(borrowedSummary, borrowedAfterFree, label + " 持有方释放后 C# 推理");
                // 持有方释放后，正式 C 推理会重新登记独立的借用。
                nativeBorrowActive = true;
                JToken nativeAfterOwnerFree = NativeCInferJson(
                    index, image, inferParams, label + " 持有方释放后正式 C 推理");
                EnsureNativeJsonResultsMatch(nativeJson, nativeAfterOwnerFree, label + " 持有方释放后正式 C 推理");

                nativeBorrowActive = false;
                if (NativeCFreeModel(index) != 0)
                    throw new Exception(label + " 正式 C 借用释放失败");
                EnsureResultsMatch(borrowedSummary, InferAndSummarize(borrowed, image, inferParams),
                    label + " 正式 C 借用释放后 C# 推理");

                borrowed.Dispose();
                borrowed = null;
                EnsureIndexRemoved(loader, index, label);
            }
            finally
            {
                try
                {
                    if (ownerActive || nativeBorrowActive)
                    {
                        ownerActive = false;
                        nativeBorrowActive = false;
                        if (NativeCFreeModel(index) != 0)
                            throw new Exception(label + " 清理正式 C 持有或借用失败");
                    }
                }
                finally
                {
                    borrowed?.Dispose();
                }
            }
        }

        private static DllLoader ResolveAndValidateSharedIndex(int index, string expectedIndexType, string label)
        {
            if (index < 0) throw new Exception(label + " 返回了负数 index: " + index);
            string indexType;
            DllLoader loader = DllLoader.ResolveForIndex(index, out indexType);
            if (!string.Equals(indexType, expectedIndexType, StringComparison.Ordinal))
                throw new Exception(label + " index 类型错误: " + indexType);
            Console.WriteLine(label + " index=" + index + ", type=" + indexType +
                ", provider=" + loader.LoadedDogProvider);
            return loader;
        }

        private static void ValidateDvsDescriptorShape(JObject descriptor, int index, string label)
        {
            EnsureNativeJsonSuccess(descriptor, label);
            if (descriptor["code"]?.Type != JTokenType.Integer || descriptor["code"].Value<int>() != 0 ||
                descriptor["message"]?.Type != JTokenType.String ||
                descriptor["schema_version"]?.Type != JTokenType.Integer ||
                descriptor["dvs_type"]?.Type != JTokenType.String ||
                descriptor["model_path"]?.Type != JTokenType.String ||
                descriptor["device_id"]?.Type != JTokenType.Integer ||
                !(descriptor["pipeline"] is JObject) ||
                !(descriptor["model_bindings"] is JArray) ||
                descriptor["model_index"]?.Value<int>() != index ||
                descriptor["resource_type"]?.ToString() != "dvs" ||
                descriptor["provider"] != null)
            {
                throw new Exception(label + "字段不完整");
            }
        }

        private static void ValidateDvsInfoWhenNeeded(
            Model model,
            JObject modelInfo,
            DllLoader loader,
            int index,
            string indexType,
            string label)
        {
            if (modelInfo == null) throw new Exception(label + " 模型信息为空");
            if (!string.Equals(indexType, "dvs", StringComparison.Ordinal)) return;

            JObject descriptor = loader.GetDvsModel(index);
            ValidateDvsDescriptorShape(descriptor, index, label + " 读取 DVS 信息");
            JObject fullInfo = model.GetDvsModelInfo();
            ValidateDvsDescriptorShape(fullInfo, index, label + " 完整 DVS 信息");
            if (!(fullInfo["loaded_model_meta"] is JArray) ||
                !(fullInfo["model_info"] is JObject) ||
                fullInfo["input_model_node_id"]?.Type != JTokenType.Integer ||
                fullInfo["output_model_node_id"]?.Type != JTokenType.Integer)
            {
                throw new Exception(label + " 完整 DVS 信息缺少运行信息");
            }
        }

        private static Model CreateBoundCSharpBorrower(int index, out JObject modelInfo)
        {
            Model borrowed = ModelFactory.CreateFromIndex(index);
            try
            {
                modelInfo = borrowed.GetModelInfo();
                if (modelInfo == null) throw new Exception("C# 共享模型信息为空");
                return borrowed;
            }
            catch
            {
                borrowed.Dispose();
                throw;
            }
        }

        private static readonly HashSet<string> WorkflowCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "help", "load-model", "list-models", "all-models", "model-info", "dvs-model-info", "infer", "infer-json",
            "infer-batch", "benchmark", "consistency-test", "free-model", "free-all-models", "device-info",
            "gpu-info", "dog-info", "keep-max-clock"
        };

        private static bool IsWorkflowCommand(string[] args)
        {
            return args != null && args.Length > 0 && WorkflowCommands.Contains(args[0]);
        }

        private static int RunWorkflowCommands(string[] args)
        {
            // 标准生命周期：加载 → 信息 → 推理 → 释放。
            var context = new WorkflowContext();
            int exitCode = 0;
            try
            {
                var segments = SplitWorkflowSegments(args);
                foreach (var segment in segments)
                {
                    ExecuteWorkflowCommand(context, segment);
                }
            }
            catch (WorkflowParameterException ex)
            {
                Console.Error.WriteLine("参数错误: " + ex.Message);
                exitCode = 2;
            }
            catch (WorkflowExecutionException ex)
            {
                Console.Error.WriteLine("执行失败: " + ex.Message);
                exitCode = 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("执行失败: " + ex.Message);
                exitCode = 1;
            }
            var cleanupFailures = context.DisposeAll();
            foreach (string failure in cleanupFailures)
            {
                Console.Error.WriteLine("自动清理失败: " + failure);
            }
            if (exitCode == 0 && cleanupFailures.Count > 0) exitCode = 1;
            return exitCode;
        }

        private static List<string[]> SplitWorkflowSegments(string[] args)
        {
            var segments = new List<string[]>();
            var current = new List<string>();
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--then", StringComparison.OrdinalIgnoreCase))
                {
                    if (current.Count == 0) throw new WorkflowParameterException("--then 前缺少命令");
                    segments.Add(current.ToArray());
                    current.Clear();
                    continue;
                }
                current.Add(arg);
            }
            if (current.Count == 0) throw new WorkflowParameterException("--then 后缺少命令");
            segments.Add(current.ToArray());
            return segments;
        }

        private static void ExecuteWorkflowCommand(WorkflowContext context, string[] args)
        {
            if (args == null || args.Length == 0) throw new WorkflowParameterException("缺少命令");
            string command = args[0].ToLowerInvariant();
            WorkflowArguments parsed = WorkflowArguments.Parse(args);
            switch (command)
            {
                case "help":
                    RequireNoArguments(parsed, command);
                    PrintWorkflowHelp();
                    return;
                case "load-model":
                    RunWorkflowLoadModel(context, parsed);
                    return;
                case "list-models":
                    RequireNoArguments(parsed, command);
                    RunWorkflowListModels(context);
                    return;
                case "all-models":
                    RequireNoArguments(parsed, command);
                    Console.WriteLine(Utils.GetAllModels().ToString(Formatting.Indented));
                    return;
                case "model-info":
                    RunWorkflowModelInfo(context, parsed, false);
                    return;
                case "dvs-model-info":
                    RunWorkflowModelInfo(context, parsed, true);
                    return;
                case "infer":
                    RunWorkflowInfer(context, parsed, WorkflowInferKind.Structured);
                    return;
                case "infer-json":
                    RunWorkflowInfer(context, parsed, WorkflowInferKind.Json);
                    return;
                case "infer-batch":
                    RunWorkflowInfer(context, parsed, WorkflowInferKind.Batch);
                    return;
                case "benchmark":
                    RunWorkflowBenchmark(context, parsed);
                    return;
                case "consistency-test":
                    RunWorkflowConsistencyTest(context, parsed);
                    return;
                case "free-model":
                    RunWorkflowFreeModel(context, parsed);
                    return;
                case "free-all-models":
                    {
                        RequireNoArguments(parsed, command);
                        var freeFailures = context.FreeAll();
                        if (freeFailures.Count == 0)
                        {
                            Console.WriteLine("已释放全部已加载模型");
                        }
                        else
                        {
                            foreach (string failure in freeFailures) Console.WriteLine("释放失败: " + failure);
                            throw new InvalidOperationException("存在模型释放失败");
                        }
                        return;
                    }
                case "device-info":
                    RequireNoArguments(parsed, command);
                    PrintDeviceInfo(Utils.GetDeviceInfo());
                    return;
                case "gpu-info":
                    RequireNoArguments(parsed, command);
                    PrintDeviceInfo(Utils.GetGpuInfo());
                    return;
                case "dog-info":
                    RequireNoArguments(parsed, command);
                    Console.WriteLine(sntl_admin_csharp.DogUtils.GetAllDogInfo().ToString(Formatting.Indented));
                    return;
                case "keep-max-clock":
                    RequireNoArguments(parsed, command);
                    RunWorkflowKeepMaxClock();
                    return;
                default:
                    throw new WorkflowParameterException("不支持的命令: " + args[0]);
            }
        }

        private static void PrintWorkflowHelp()
        {
            Console.WriteLine("标准生命周期：加载 → 信息 → 推理 → 释放。");
            Console.WriteLine("示例: load-model m1 <path> --device 0 --then model-info m1 --then infer m1 <image> --threshold 0.5 --then free-model m1");
            Console.WriteLine("名称 m1 仅在本次进程中有效，直到 free-model、free-all-models 或进程结束。");
            Console.WriteLine("以下命令可用 --then 串联，模型名称在本进程内有效：");
            Console.WriteLine("  load-model <名称> <模型路径> [--device N] [--rpc true|false] [--replace true|false]");
            Console.WriteLine("  list-models | all-models | model-info <名称> | dvs-model-info <名称>");
            Console.WriteLine("  infer <名称> <图片> [--threshold F] [--with-mask true|false] [--calc-mean default|true|false]");
            Console.WriteLine("  infer-json <名称> <图片> [--threshold F] [--with-mask true|false] [--calc-mean default|true|false]");
            Console.WriteLine("  infer-batch <名称> <图片> [--batch-size N] [--threshold F] [--with-mask true|false] [--calc-mean default|true|false]");
            Console.WriteLine("  benchmark <名称> <图片> [--batch-size N] [--warmup N] [--runs N] [--threads N] [--threshold F] [--with-mask true|false] [--calc-mean default|true|false]");
            Console.WriteLine("  consistency-test <名称> <图片> [--batch-size N] [--warmup N] [--runs N] [--threads N] [--threshold F] [--with-mask true|false] [--calc-mean default|true|false]");
            Console.WriteLine("  free-model <名称> | free-all-models | device-info | gpu-info | dog-info | keep-max-clock | help");
        }

        private static void RunWorkflowLoadModel(WorkflowContext context, WorkflowArguments args)
        {
            RequirePositionCount(args, "load-model", 2);
            ValidateOptions(args, "device", "rpc", "replace");
            string name = args.Positionals[0];
            string path = args.Positionals[1];
            int deviceId = args.GetAnyInt("device", 0);
            bool rpcMode = args.GetBool("rpc", false);
            bool replace = args.GetBool("replace", false);
            if (!File.Exists(path)) throw new WorkflowParameterException("模型文件不存在: " + path);
            WorkflowModelEntry oldEntry = null;
            if (context.Models.TryGetValue(name, out oldEntry))
            {
                if (!replace) throw new WorkflowParameterException("模型名称已存在: " + name);
            }

            var timer = Stopwatch.StartNew();
            Model model = null;
            try
            {
                model = new Model(path, deviceId, rpcMode, false);
                timer.Stop();
                var entry = new WorkflowModelEntry(name, path, deviceId, rpcMode, model, timer.Elapsed.TotalMilliseconds);
                if (oldEntry != null) context.Free(oldEntry.Name);
                context.Models.Add(name, entry);
                PrintModelHeader(entry);
                Console.WriteLine("名称: " + name);
                Console.WriteLine("设备: " + deviceId.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("model_index: " + model.modelIndex.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("加载耗时: " + FormatMs(entry.LoadMs));
                Console.WriteLine("provider: " + model.LoadedDogProvider);
                Console.WriteLine("DLL: " + (model.LoadedNativeDllName ?? string.Empty));
            }
            catch
            {
                try { model?.Dispose(); } catch { }
                throw;
            }
        }





        private static string ReadUtf8String(IntPtr value)
        {
            int length = 0;
            while (Marshal.ReadByte(value, length) != 0) length++;
            byte[] bytes = new byte[length];
            Marshal.Copy(value, bytes, 0, length);
            return new UTF8Encoding(false, true).GetString(bytes);
        }





        private static void EnsureModelInfosMatch(JObject left, JObject right, string operation)
        {
            JToken leftCore = CanonicalizeModelInfo(left);
            JToken rightCore = CanonicalizeModelInfo(right);
            if (!JToken.DeepEquals(leftCore, rightCore))
            {
                Console.WriteLine(operation + "：预期归一化 JSON=" + leftCore.ToString(Formatting.None));
                Console.WriteLine(operation + "：实际归一化 JSON=" + rightCore.ToString(Formatting.None));
                throw new Exception(operation + "不一致");
            }
        }

        private static JToken CanonicalizeModelInfo(JObject source)
        {
            if (source == null) throw new Exception("模型信息为空");
            var pipeline = source["pipeline"] as JObject;
            bool isFlow = pipeline != null || source["nodes"] is JArray;
            if (isFlow)
            {
                var flow = (JObject)(pipeline ?? source).DeepClone();
                flow.Remove("loaded_model_meta");
                flow.Remove("model_info");
                return flow;
            }

            var modelInfo = source["model_info"] as JObject;
            var result = (JObject)(modelInfo ?? source).DeepClone();
            if (result["input_shapes"]?.Type == JTokenType.Null)
                result.Remove("input_shapes");
            if (modelInfo != null) return result;
            result.Remove("code");
            result.Remove("message");
            result.Remove("model_index");
            return result;
        }

        private static void EnsureResultsMatch(JObject expected, JObject actual, string operation)
        {
            if (!ResultSummariesMatch(expected, actual))
            {
                throw new Exception(operation + "不一致\nexpected=" +
                    expected.ToString(Formatting.None) + "\nactual=" + actual.ToString(Formatting.None));
            }
        }



        private static void RunWorkflowListModels(WorkflowContext context)
        {
            if (context.Models.Count == 0)
            {
                Console.WriteLine("当前没有已加载模型");
                return;
            }
            foreach (var entry in context.Models.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                PrintModelHeader(entry);
                Console.WriteLine("名称: " + entry.Name);
                Console.WriteLine("设备: " + entry.DeviceId.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("model_index: " + entry.Model.modelIndex.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("provider: " + entry.Model.LoadedDogProvider);
                Console.WriteLine("DLL: " + (entry.Model.LoadedNativeDllName ?? string.Empty));
            }
        }

        private static void RunWorkflowModelInfo(WorkflowContext context, WorkflowArguments args, bool dvsInfo)
        {
            RequirePositionCount(args, dvsInfo ? "dvs-model-info" : "model-info", 1);
            RequireNoOptions(args);
            WorkflowModelEntry entry = context.Get(args.Positionals[0]);
            PrintModelHeader(entry);
            Console.WriteLine((dvsInfo ? entry.Model.GetDvsModelInfo() : entry.Model.GetModelInfo()).ToString(Formatting.Indented));
        }

        private static void RunWorkflowInfer(WorkflowContext context, WorkflowArguments args, WorkflowInferKind kind)
        {
            string command = kind == WorkflowInferKind.Structured ? "infer" : kind == WorkflowInferKind.Json ? "infer-json" : "infer-batch";
            RequirePositionCount(args, command, 2);
            ValidateOptions(args, kind == WorkflowInferKind.Batch
                ? new[] { "batch-size", "threshold", "with-mask", "calc-mean" }
                : new[] { "threshold", "with-mask", "calc-mean" });
            WorkflowModelEntry entry = context.Get(args.Positionals[0]);
            int batchSize = kind == WorkflowInferKind.Batch ? args.GetInt("batch-size", 1, true) : 1;
            JObject parameters = args.CreateInferParameters(batchSize);
            Mat bgr = null;
            Mat rgb = null;
            try
            {
                LoadRgbImage(args.Positionals[1], out bgr, out rgb);
                PrintModelHeader(entry);
                Console.WriteLine("图片: " + args.Positionals[1]);
                Console.WriteLine("batch_size: " + batchSize.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("threshold: " + args.GetDouble("threshold", 0.5).ToString("F4", CultureInfo.InvariantCulture));
                if (kind == WorkflowInferKind.Json)
                {
                    var timer = Stopwatch.StartNew();
                    object jsonOutput = entry.Model.InferOneOutJson(rgb, parameters);
                    timer.Stop();
                    PrintInferTiming(timer.Elapsed.TotalMilliseconds);
                    JToken jsonResult = jsonOutput as JToken;
                    Console.WriteLine((jsonResult ?? JToken.FromObject(jsonOutput)).ToString(Formatting.Indented));
                    return;
                }

                Utils.CSharpResult result;
                var stopwatch = Stopwatch.StartNew();
                if (kind == WorkflowInferKind.Structured)
                {
                    result = entry.Model.Infer(rgb, parameters);
                }
                else
                {
                    var images = new List<Mat>(batchSize);
                    for (int i = 0; i < batchSize; i++) images.Add(rgb);
                    result = entry.Model.InferBatch(images, parameters);
                }
                stopwatch.Stop();
                try
                {
                    PrintInferTiming(stopwatch.Elapsed.TotalMilliseconds);
                    PrintStructuredResult(result);
                    PrintFlowDetails();
                }
                finally
                {
                    DisposeResultMasks(result);
                }
            }
            finally
            {
                try { rgb?.Dispose(); } catch { }
                try { bgr?.Dispose(); } catch { }
            }
        }

        private static JObject InferAndSummarize(Model model, Mat image, JObject inferParams)
        {
            Utils.CSharpResult result = model.Infer(image, inferParams);
            try
            {
                return SummarizeResult(result);
            }
            finally
            {
                DisposeResultMasks(result);
            }
        }

        private static JObject SummarizeResult(Utils.CSharpResult result)
        {
            int sampleCount = result.SampleResults != null ? result.SampleResults.Count : 0;
            if (sampleCount <= 0)
                throw new Exception("推理结果为空");

            int objectCount = 0;
            var samples = new JArray();
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var objects = new JArray();
                List<Utils.CSharpObjectResult> sampleObjects = result.SampleResults[sampleIndex].Results;
                if (sampleObjects != null)
                {
                    objectCount += sampleObjects.Count;
                    for (int objectIndex = 0; objectIndex < sampleObjects.Count; objectIndex++)
                    {
                        Utils.CSharpObjectResult item = sampleObjects[objectIndex];
                        bool hasMaskData = item.Mask != null && !item.Mask.Empty();
                        objects.Add(new JObject
                        {
                            ["category_id"] = item.CategoryId,
                            ["category_name"] = item.CategoryName,
                            ["score"] = item.Score,
                            ["bbox"] = item.Bbox != null ? JArray.FromObject(item.Bbox) : new JArray(),
                            ["with_bbox"] = item.WithBbox,
                            ["with_mask"] = item.WithMask,
                            ["mask_width"] = hasMaskData ? item.Mask.Width : 0,
                            ["mask_height"] = hasMaskData ? item.Mask.Height : 0
                        });
                    }
                }
                samples.Add(new JObject
                {
                    ["object_count"] = objects.Count,
                    ["objects"] = objects
                });
            }
            if (objectCount <= 0)
                throw new Exception("推理结果为空");

            return new JObject
            {
                ["sample_count"] = sampleCount,
                ["object_count"] = objectCount,
                ["samples"] = samples
            };
        }

        private static bool ResultSummariesMatch(JObject left, JObject right)
        {
            if (!HasConsistentSummaryCounts(left) || !HasConsistentSummaryCounts(right)) return false;
            if ((int)left["sample_count"] != (int)right["sample_count"])
                return false;
            if ((int)left["object_count"] != (int)right["object_count"])
                return false;

            var leftSamples = left["samples"] as JArray;
            var rightSamples = right["samples"] as JArray;
            if (leftSamples == null || rightSamples == null || leftSamples.Count != rightSamples.Count)
                return false;
            for (int sampleIndex = 0; sampleIndex < leftSamples.Count; sampleIndex++)
            {
                var leftSample = leftSamples[sampleIndex] as JObject;
                var rightSample = rightSamples[sampleIndex] as JObject;
                if (leftSample == null || rightSample == null)
                    return false;
                if (leftSample["object_count"].Value<int>() != rightSample["object_count"].Value<int>())
                    return false;
                var leftObjects = leftSample["objects"] as JArray;
                var rightObjects = rightSample["objects"] as JArray;
                if (leftObjects == null || rightObjects == null || leftObjects.Count != rightObjects.Count)
                    return false;
                for (int objectIndex = 0; objectIndex < leftObjects.Count; objectIndex++)
                {
                    var leftObject = leftObjects[objectIndex] as JObject;
                    var rightObject = rightObjects[objectIndex] as JObject;
                    if (leftObject == null || rightObject == null)
                        return false;
                    if ((int)leftObject["category_id"] != (int)rightObject["category_id"])
                        return false;
                    if (!string.Equals((string)leftObject["category_name"], (string)rightObject["category_name"], StringComparison.Ordinal))
                        return false;
                    if (!SharedResultNumbersMatch((double)leftObject["score"], (double)rightObject["score"], 1e-4))
                        return false;

                    var leftBbox = leftObject["bbox"] as JArray;
                    var rightBbox = rightObject["bbox"] as JArray;
                    if (leftBbox == null || rightBbox == null || leftBbox.Count != rightBbox.Count)
                        return false;
                    for (int bboxIndex = 0; bboxIndex < leftBbox.Count; bboxIndex++)
                    {
                        if (!SharedResultNumbersMatch(leftBbox[bboxIndex].Value<double>(), rightBbox[bboxIndex].Value<double>(), 1e-3))
                            return false;
                    }
                    if ((bool)leftObject["with_bbox"] != (bool)rightObject["with_bbox"] ||
                        (bool)leftObject["with_mask"] != (bool)rightObject["with_mask"] ||
                        (int)leftObject["mask_width"] != (int)rightObject["mask_width"] ||
                        (int)leftObject["mask_height"] != (int)rightObject["mask_height"])
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool HasConsistentSummaryCounts(JObject summary)
        {
            var samples = summary?["samples"] as JArray;
            if (samples == null || summary.Value<int?>("sample_count") != samples.Count)
                return false;
            int objectCount = 0;
            foreach (JToken token in samples)
            {
                var sample = token as JObject;
                var objects = sample?["objects"] as JArray;
                if (objects == null || sample.Value<int?>("object_count") != objects.Count)
                    return false;
                objectCount += objects.Count;
            }
            return summary.Value<int?>("object_count") == objectCount;
        }

        private static bool SharedResultNumbersMatch(double left, double right, double tolerance)
        {
            return !double.IsNaN(left) && !double.IsInfinity(left) &&
                !double.IsNaN(right) && !double.IsInfinity(right) && Math.Abs(left - right) <= tolerance;
        }

        private static void RunSharedResultComparisonChecks()
        {
            var expected = JObject.Parse(@"{
                'sample_count': 1, 'object_count': 1,
                'samples': [{ 'object_count': 1, 'objects': [{
                    'category_id': 0, 'category_name': '测试类别', 'score': 0.75,
                    'bbox': [10, 20, 30, 40], 'with_bbox': true, 'with_mask': false,
                    'mask_width': 0, 'mask_height': 0
                }] }]
            }");
            if (!ResultSummariesMatch(expected, (JObject)expected.DeepClone()))
                throw new Exception("相同的有效结果被判为不同");
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var actual = (JObject)expected.DeepClone();
                actual["samples"][0]["objects"][0]["score"] = invalid;
                if (ResultSummariesMatch(expected, actual))
                    throw new Exception("无效分数被判为一致");
                actual = (JObject)expected.DeepClone();
                actual["samples"][0]["objects"][0]["bbox"][0] = invalid;
                if (ResultSummariesMatch(expected, actual))
                    throw new Exception("无效框坐标被判为一致");
            }
            var wrongCount = (JObject)expected.DeepClone();
            wrongCount["sample_count"] = 2;
            if (ResultSummariesMatch(wrongCount, (JObject)wrongCount.DeepClone()))
                throw new Exception("声明样本数与数组长度不一致仍被接受");
            wrongCount = (JObject)expected.DeepClone();
            wrongCount["samples"][0]["object_count"] = 2;
            if (ResultSummariesMatch(wrongCount, (JObject)wrongCount.DeepClone()))
                throw new Exception("声明目标数与数组长度不一致仍被接受");
            var flowInfo = JObject.Parse(@"{ 'nodes': [{ 'type': 'model/test',
                'properties': { 'device_id': 0, 'model_name': 'model-a', 'provider': 'sentinel' } }] }");
            foreach (string field in new[] { "device_id", "model_name", "provider" })
            {
                var changedFlow = (JObject)flowInfo.DeepClone();
                changedFlow["nodes"][0]["properties"][field] = "changed";
                if (JToken.DeepEquals(CanonicalizeModelInfo(flowInfo), CanonicalizeModelInfo(changedFlow)))
                    throw new Exception("流程配置字段 " + field + " 的变化被忽略");
            }
            var missingShapes = new JObject { ["model_info"] = new JObject { ["task_type"] = "分类" } };
            var nullShapes = (JObject)missingShapes.DeepClone();
            nullShapes["model_info"]["input_shapes"] = JValue.CreateNull();
            if (!JToken.DeepEquals(CanonicalizeModelInfo(missingShapes), CanonicalizeModelInfo(nullShapes)))
                throw new Exception("模型信息根部的可选 input_shapes 为 null 时未视为缺失");

            var firstShapes = (JObject)missingShapes.DeepClone();
            firstShapes["model_info"]["input_shapes"] = new JObject { ["input"] = new JArray(1, 3, 64, 64) };
            var differentShapes = (JObject)firstShapes.DeepClone();
            differentShapes["model_info"]["input_shapes"]["input"][0] = 2;
            if (JToken.DeepEquals(CanonicalizeModelInfo(firstShapes), CanonicalizeModelInfo(differentShapes)))
                throw new Exception("非空 input_shapes 的形状差异被忽略");

            var nestedNullShapes = (JObject)flowInfo.DeepClone();
            nestedNullShapes["nodes"][0]["properties"]["input_shapes"] = JValue.CreateNull();
            if (JToken.DeepEquals(CanonicalizeModelInfo(flowInfo), CanonicalizeModelInfo(nestedNullShapes)))
                throw new Exception("嵌套业务 input_shapes 的 null 与缺失被错误视为相同");

            EnsureThrows<Exception>(() => CanonicalizeModelInfo(null),
                "空模型信息未被拒绝");
        }

        private static void EnsureNativeJsonSuccess(JObject result, string operation)
        {
            int code = result != null && result["code"] != null ? result["code"].Value<int>() : 1;
            if (code != 0)
            {
                string message = result != null && result["message"] != null
                    ? result["message"].ToString()
                    : "未知错误";
                throw new Exception(operation + "失败: " + message);
            }
        }

        private static void EnsureIndexRemoved(DllLoader loader, int index, string label)
        {
            if (loader == null || index == -1) return;
            if (loader.GetIndexType(index) != 0)
                throw new Exception(label + "释放后 index 仍然存在: " + index);
        }

        private static void RunWorkflowBenchmark(WorkflowContext context, WorkflowArguments args)
        {
            RequirePositionCount(args, "benchmark", 2);
            ValidateOptions(args, "batch-size", "warmup", "runs", "threads", "threshold", "with-mask", "calc-mean");
            WorkflowModelEntry entry = context.Get(args.Positionals[0]);
            int batchSize = args.GetInt("batch-size", 1, true);
            int warmup = args.GetInt("warmup", 1, false);
            int runs = args.GetInt("runs", 10, true);
            int threads = args.GetInt("threads", 1, true);
            JObject parameters = args.CreateInferParameters(batchSize);
            string imagePath = args.Positionals[1];
            if (!File.Exists(imagePath)) throw new WorkflowParameterException("图片文件不存在: " + imagePath);

            var latencies = new List<double>();
            var sdkTimes = new List<double>();
            var flowTimes = new List<double>();
            var nodeStats = new Dictionary<string, NodeTimingAggregate>(StringComparer.Ordinal);
            Exception workerError = null;
            object sync = new object();
            int readyCount = 0;
            long totalSamples = 0;
            using (var ready = new ManualResetEvent(false))
            using (var start = new ManualResetEvent(false))
            {
                var workers = new List<Thread>(threads);
                for (int workerIndex = 0; workerIndex < threads; workerIndex++)
                {
                    var thread = new Thread(() =>
                    {
                        Model workerModel = entry.Model;
                        JObject workerParameters = null;
                        Mat bgr = null;
                        Mat rgb = null;
                        try
                        {
                            workerParameters = (JObject)parameters.DeepClone();
                            LoadRgbImage(imagePath, out bgr, out rgb);
                            var images = new List<Mat>(batchSize);
                            for (int i = 0; i < batchSize; i++) images.Add(rgb);
                            for (int i = 0; i < warmup; i++)
                            {
                                var warmResult = workerModel.InferBatch(images, workerParameters);
                                DisposeResultMasks(warmResult);
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (sync)
                            {
                                if (workerError == null) workerError = ex;
                            }
                        }
                        finally
                        {
                            lock (sync)
                            {
                                readyCount++;
                                if (readyCount == threads) ready.Set();
                            }
                        }

                        start.WaitOne();
                        try
                        {
                            if (workerError == null)
                            {
                                var images = new List<Mat>(batchSize);
                                for (int i = 0; i < batchSize; i++) images.Add(rgb);
                                for (int i = 0; i < runs; i++)
                                {
                                    var timer = Stopwatch.StartNew();
                                    var result = workerModel.InferBatch(images, workerParameters);
                                    timer.Stop();
                                    try
                                    {
                                        double sdkMs = 0.0;
                                        double flowMs = 0.0;
                                        InferTiming.GetLast(out sdkMs, out flowMs);
                                        lock (sync)
                                        {
                                            latencies.Add(timer.Elapsed.TotalMilliseconds);
                                            sdkTimes.Add(sdkMs > 0.0 ? sdkMs : timer.Elapsed.TotalMilliseconds);
                                            flowTimes.Add(flowMs > 0.0 ? flowMs : timer.Elapsed.TotalMilliseconds);
                                            totalSamples += batchSize;
                                            AddCurrentFlowNodeTimings(nodeStats);
                                        }
                                    }
                                    finally
                                    {
                                        DisposeResultMasks(result);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (sync)
                            {
                                if (workerError == null) workerError = ex;
                            }
                        }
                        finally
                        {
                            try { rgb?.Dispose(); } catch { }
                            try { bgr?.Dispose(); } catch { }
                        }
                    });
                    thread.IsBackground = false;
                    workers.Add(thread);
                    thread.Start();
                }

                ready.WaitOne();
                var totalTimer = Stopwatch.StartNew();
                start.Set();
                foreach (var worker in workers) worker.Join();
                totalTimer.Stop();
                if (workerError != null) throw new InvalidOperationException(workerError.Message, workerError);

                PrintModelHeader(entry);
                Console.WriteLine("图片: " + imagePath);
                Console.WriteLine("线程数: " + threads.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("batch_size: " + batchSize.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("预热次数: " + warmup.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("运行次数: " + runs.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("完成样本数: " + totalSamples.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("总耗时: " + FormatMs(totalTimer.Elapsed.TotalMilliseconds));
                PrintLatencyStatistics("外部耗时", latencies);
                PrintLatencyStatistics("底层推理耗时", sdkTimes);
                PrintLatencyStatistics("流程耗时", flowTimes);
                double seconds = totalTimer.Elapsed.TotalSeconds;
                double throughput = seconds > 0.0 ? totalSamples / seconds : 0.0;
                Console.WriteLine("吞吐量: " + throughput.ToString("F2", CultureInfo.InvariantCulture) + " 张/秒");
                PrintNodeAverages(nodeStats);
            }
        }

        private static void RunWorkflowConsistencyTest(WorkflowContext context, WorkflowArguments args)
        {
            RequirePositionCount(args, "consistency-test", 2);
            ValidateOptions(args, "batch-size", "warmup", "runs", "threads", "threshold", "with-mask", "calc-mean");
            WorkflowModelEntry entry = context.Get(args.Positionals[0]);
            int batchSize = args.GetInt("batch-size", 1, true);
            int warmup = args.GetInt("warmup", 1, false);
            int runs = args.GetInt("runs", 10, true);
            int threads = args.GetInt("threads", 1, true);
            JObject parameters = args.CreateInferParameters(batchSize);
            string imagePath = args.Positionals[1];
            if (!File.Exists(imagePath)) throw new WorkflowParameterException("图片文件不存在: " + imagePath);

            string structuredBaseline = null;
            string jsonBaseline = null;
            string structuredDifference = null;
            string jsonDifference = null;
            int structuredDifferenceRun = 0;
            int jsonDifferenceRun = 0;
            int structuredCompleted = 0;
            int jsonCompleted = 0;
            Exception workerError = null;
            object sync = new object();
            using (var ready = new ManualResetEvent(false))
            using (var start = new ManualResetEvent(false))
            {
                int readyCount = 0;
                var workers = new List<Thread>(threads);
                for (int workerIndex = 0; workerIndex < threads; workerIndex++)
                {
                    var thread = new Thread(() =>
                    {
                        Model workerModel = entry.Model;
                        JObject workerParameters = null;
                        Mat bgr = null;
                        Mat rgb = null;
                        try
                        {
                            workerParameters = (JObject)parameters.DeepClone();
                            LoadRgbImage(imagePath, out bgr, out rgb);
                            for (int i = 0; i < warmup; i++)
                            {
                                var warmResult = workerModel.Infer(rgb, workerParameters);
                                DisposeResultMasks(warmResult);
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (sync)
                            {
                                if (workerError == null) workerError = ex;
                            }
                        }
                        finally
                        {
                            lock (sync)
                            {
                                readyCount++;
                                if (readyCount == threads) ready.Set();
                            }
                        }

                        start.WaitOne();
                        try
                        {
                            if (workerError == null)
                            {
                                var images = new List<Mat>(batchSize);
                                for (int imageIndex = 0; imageIndex < batchSize; imageIndex++) images.Add(rgb);
                                for (int i = 0; i < runs; i++)
                                {
                                    var structuredResult = workerModel.InferBatch(images, workerParameters);
                                    try
                                    {
                                        string structuredSignature = BuildStructuredResultSignature(structuredResult);
                                        lock (sync)
                                        {
                                            UpdateConsistencySignature(ref structuredBaseline, ref structuredDifference, ref structuredDifferenceRun, structuredSignature, i + 1);
                                            structuredCompleted++;
                                        }
                                    }
                                    finally
                                    {
                                        DisposeResultMasks(structuredResult);
                                    }

                                    var jsonParameters = (JObject)workerParameters.DeepClone();
                                    jsonParameters["batch_size"] = 1;
                                    var jsonBatch = new JArray();
                                    for (int imageIndex = 0; imageIndex < batchSize; imageIndex++)
                                    {
                                        object jsonResult = workerModel.InferOneOutJson(rgb, jsonParameters);
                                        JToken jsonToken = jsonResult as JToken ?? JToken.FromObject(jsonResult);
                                        jsonBatch.Add(NormalizeWorkflowJson(jsonToken));
                                    }
                                    string jsonSignature = CanonicalizeSignatureToken(jsonBatch).ToString(Formatting.None);
                                    lock (sync)
                                    {
                                        UpdateConsistencySignature(ref jsonBaseline, ref jsonDifference, ref jsonDifferenceRun, jsonSignature, i + 1);
                                        jsonCompleted++;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (sync)
                            {
                                if (workerError == null) workerError = ex;
                            }
                        }
                        finally
                        {
                            try { rgb?.Dispose(); } catch { }
                            try { bgr?.Dispose(); } catch { }
                        }
                    });
                    thread.IsBackground = false;
                    workers.Add(thread);
                    thread.Start();
                }
                ready.WaitOne();
                var totalTimer = Stopwatch.StartNew();
                start.Set();
                foreach (var worker in workers) worker.Join();
                totalTimer.Stop();
                if (workerError != null) throw new InvalidOperationException(workerError.Message, workerError);

                PrintModelHeader(entry);
                Console.WriteLine("图片: " + imagePath);
                Console.WriteLine("线程数: " + threads.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("batch_size: " + batchSize.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("结构化比较次数: " + structuredCompleted.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("JSON 比较次数: " + jsonCompleted.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("总耗时: " + FormatMs(totalTimer.Elapsed.TotalMilliseconds));
                Console.WriteLine("结构化结果一致: " + (structuredDifference == null ? "是" : "否"));
                Console.WriteLine("JSON 结果一致: " + (jsonDifference == null ? "是" : "否"));
                if (structuredDifference != null)
                {
                    Console.WriteLine("结构化结果不一致: run=" + structuredDifferenceRun.ToString(CultureInfo.InvariantCulture) +
                        " baseline_hash=" + ComputeTextHash(structuredBaseline) +
                        " actual_hash=" + ComputeTextHash(structuredDifference));
                }
                if (jsonDifference != null)
                {
                    Console.WriteLine("JSON结果不一致: run=" + jsonDifferenceRun.ToString(CultureInfo.InvariantCulture) +
                        " baseline_hash=" + ComputeTextHash(jsonBaseline) +
                        " actual_hash=" + ComputeTextHash(jsonDifference));
                }
                if (structuredDifference != null || jsonDifference != null)
                {
                    throw new InvalidOperationException("一致性检查失败");
                }
            }
        }

        private static void RunWorkflowFreeModel(WorkflowContext context, WorkflowArguments args)
        {
            RequirePositionCount(args, "free-model", 1);
            RequireNoOptions(args);
            WorkflowModelEntry entry = context.Get(args.Positionals[0]);
            PrintModelHeader(entry);
            context.Free(entry.Name);
            Console.WriteLine("已释放模型: " + entry.Name);
        }

        private static void RunWorkflowKeepMaxClock()
        {
            JObject info = Utils.GetDeviceInfo();
            IntPtr result = IntPtr.Zero;
            var keepMaxClock = DllLoader.Instance.dlcv_keep_max_clock;
            if (keepMaxClock == null)
            {
                throw new InvalidOperationException("保持最高时钟接口不可用");
            }
            try
            {
                result = keepMaxClock.Invoke();
                Console.WriteLine("设备信息:");
                Console.WriteLine(info.ToString(Formatting.Indented));
                Console.WriteLine("保持最高时钟请求已发送");
                if (result != IntPtr.Zero)
                {
                    string text = Marshal.PtrToStringAnsi(result);
                    if (!string.IsNullOrWhiteSpace(text)) Console.WriteLine("返回信息: " + text);
                }
            }
            finally
            {
                if (result != IntPtr.Zero)
                {
                    var freeResult = DllLoader.Instance.dlcv_free_result;
                    if (freeResult == null) throw new InvalidOperationException("返回指针释放接口不可用");
                    freeResult(result);
                }
            }
        }

        private static void PrintDeviceInfo(JObject info)
        {
            Console.WriteLine(info.ToString(Formatting.Indented));
            int code = info["code"] != null ? info["code"].Value<int>() : 0;
            if (code != 0)
            {
                string message = info["message"]?.Value<string>() ?? "设备接口返回失败状态";
                throw new InvalidOperationException(message);
            }
        }

        private static void LoadRgbImage(string imagePath, out Mat bgr, out Mat rgb)
        {
            if (!File.Exists(imagePath)) throw new WorkflowParameterException("图片文件不存在: " + imagePath);
            bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (bgr == null || bgr.Empty()) throw new InvalidOperationException("图片解码失败: " + imagePath);
            rgb = new Mat();
            Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
        }

        private static void PrintModelHeader(WorkflowModelEntry entry)
        {
            Console.WriteLine("模型: " + entry.Path);
        }

        private static void PrintInferTiming(double outerMs)
        {
            double sdkMs = 0.0;
            double flowMs = 0.0;
            InferTiming.GetLast(out sdkMs, out flowMs);
            Console.WriteLine("外部耗时: " + FormatMs(outerMs));
            Console.WriteLine("底层推理耗时: " + FormatMs(sdkMs));
            Console.WriteLine("流程耗时: " + FormatMs(flowMs));
        }

        private static void PrintStructuredResult(Utils.CSharpResult result)
        {
            int sampleCount = result.SampleResults != null ? result.SampleResults.Count : 0;
            Console.WriteLine("样本数量: " + sampleCount.ToString(CultureInfo.InvariantCulture));
            if (result.SampleResults == null) return;
            for (int sampleIndex = 0; sampleIndex < result.SampleResults.Count; sampleIndex++)
            {
                var sample = result.SampleResults[sampleIndex];
                int count = sample.Results != null ? sample.Results.Count : 0;
                Console.WriteLine("样本 " + sampleIndex.ToString(CultureInfo.InvariantCulture) + " 目标数量: " + count.ToString(CultureInfo.InvariantCulture));
                if (sample.Ok.HasValue) Console.WriteLine("样本 " + sampleIndex.ToString(CultureInfo.InvariantCulture) + " 检查状态: " + (sample.Ok.Value ? "通过" : "未通过"));
                if (!string.IsNullOrWhiteSpace(sample.Reason)) Console.WriteLine("样本 " + sampleIndex.ToString(CultureInfo.InvariantCulture) + " 检查说明: " + sample.Reason);
                if (sample.Results == null) continue;
                for (int objectIndex = 0; objectIndex < sample.Results.Count; objectIndex++)
                {
                    var item = sample.Results[objectIndex];
                    string bbox = item.Bbox == null ? string.Empty : string.Join(", ", item.Bbox.Select(x => x.ToString("F3", CultureInfo.InvariantCulture)));
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  目标 {0}: category_id={1}, category_name={2}, score={3:F4}, area={4:F3}, bbox=[{5}], with_bbox={6}, with_angle={7}, angle={8:F4}, with_mask={9}, with_mean={10}",
                        objectIndex,
                        item.CategoryId,
                        item.CategoryName ?? string.Empty,
                        item.Score,
                        item.Area,
                        bbox,
                        item.WithBbox,
                        item.WithAngle,
                        item.Angle,
                        item.WithMask,
                        item.WithMean));
                }
            }
        }

        private static void PrintFlowDetails()
        {
            var timings = InferTiming.GetLastFlowNodeTimings();
            if (timings != null && timings.Count > 0)
            {
                Console.WriteLine("流程节点耗时:");
                foreach (var timing in timings)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  #{0} [{1}] {2}: {3:F2}ms", timing.NodeId, timing.NodeType, timing.NodeTitle, timing.ElapsedMs));
                }
            }
            var batchInfos = InferTiming.GetLastFlowModelBatchInfos();
            if (batchInfos != null && batchInfos.Count > 0)
            {
                Console.WriteLine("流程模型 batch 信息:");
                foreach (var info in batchInfos)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  #{0}: 输入={1}, 上限={2}, 调用={3}, 最大实际 batch={4}", info.NodeId, info.InputCount, info.BatchLimit, info.InferCallCount, info.MaxActualBatch));
                }
            }
        }

        private static void AddCurrentFlowNodeTimings(Dictionary<string, NodeTimingAggregate> nodeStats)
        {
            var timings = InferTiming.GetLastFlowNodeTimings();
            if (timings == null) return;
            foreach (var timing in timings)
            {
                if (timing == null) continue;
                string key = timing.NodeId.ToString(CultureInfo.InvariantCulture) + "|" + timing.NodeType + "|" + timing.NodeTitle;
                NodeTimingAggregate aggregate;
                if (!nodeStats.TryGetValue(key, out aggregate))
                {
                    aggregate = new NodeTimingAggregate(timing.NodeId, timing.NodeType, timing.NodeTitle);
                    nodeStats.Add(key, aggregate);
                }
                aggregate.Add(timing.ElapsedMs);
            }
        }

        private static void PrintLatencyStatistics(string name, List<double> values)
        {
            if (values == null || values.Count == 0)
            {
                Console.WriteLine(name + ": 无数据");
                return;
            }
            var ordered = values.OrderBy(x => x).ToList();
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: min={1:F2}ms, avg={2:F2}ms, p50={3:F2}ms, p95={4:F2}ms, max={5:F2}ms",
                name,
                ordered.First(),
                ordered.Average(),
                Percentile(ordered, 0.50),
                Percentile(ordered, 0.95),
                ordered.Last()));
        }

        private static double Percentile(List<double> values, double fraction)
        {
            if (values == null || values.Count == 0) return 0.0;
            int index = (int)Math.Ceiling(values.Count * fraction) - 1;
            index = Math.Max(0, Math.Min(values.Count - 1, index));
            return values[index];
        }

        private static string FormatMs(double value)
        {
            return Math.Max(0.0, value).ToString("F2", CultureInfo.InvariantCulture) + "ms";
        }

        private static void PrintNodeAverages(Dictionary<string, NodeTimingAggregate> nodeStats)
        {
            if (nodeStats == null || nodeStats.Count == 0)
            {
                Console.WriteLine("流程节点平均耗时: 无数据");
                return;
            }
            Console.WriteLine("流程节点平均耗时:");
            foreach (var item in nodeStats.Values.OrderByDescending(x => x.AverageMs))
            {
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  #{0} [{1}] {2}: {3:F2}ms",
                    item.NodeId,
                    item.NodeType,
                    item.NodeTitle,
                    item.AverageMs));
            }
        }

        private static void RequirePositionCount(WorkflowArguments args, string command, int count)
        {
            if (args.Positionals.Count != count)
            {
                throw new WorkflowParameterException(command + " 需要 " + count.ToString(CultureInfo.InvariantCulture) + " 个位置参数");
            }
        }

        private static void RequireNoArguments(WorkflowArguments args, string command)
        {
            if (args.Positionals.Count != 0 || args.Options.Count != 0)
            {
                throw new WorkflowParameterException(command + " 不接受参数");
            }
        }

        private static void RequireNoOptions(WorkflowArguments args)
        {
            if (args.Options.Count != 0) throw new WorkflowParameterException("该命令不接受可选参数");
        }

        private static void ValidateOptions(WorkflowArguments args, params string[] names)
        {
            var accepted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            foreach (string name in args.Options.Keys)
            {
                if (!accepted.Contains(name)) throw new WorkflowParameterException("不支持的可选参数: --" + name);
            }
        }

        private enum WorkflowInferKind
        {
            Structured,
            Json,
            Batch
        }

        private sealed class WorkflowParameterException : Exception
        {
            public WorkflowParameterException(string message) : base(message) { }
        }

        private sealed class WorkflowExecutionException : Exception
        {
            public WorkflowExecutionException(string message) : base(message) { }
        }

        private sealed class WorkflowArguments
        {
            public List<string> Positionals { get; private set; }
            public Dictionary<string, string> Options { get; private set; }

            private WorkflowArguments()
            {
                Positionals = new List<string>();
                Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            public static WorkflowArguments Parse(string[] args)
            {
                var result = new WorkflowArguments();
                for (int index = 1; index < args.Length; index++)
                {
                    string value = args[index];
                    if (!value.StartsWith("--", StringComparison.Ordinal))
                    {
                        result.Positionals.Add(value);
                        continue;
                    }
                    string name = value.Substring(2);
                    if (string.IsNullOrWhiteSpace(name)) throw new WorkflowParameterException("可选参数名称为空");
                    if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new WorkflowParameterException("可选参数 --" + name + " 缺少值");
                    }
                    if (result.Options.ContainsKey(name)) throw new WorkflowParameterException("可选参数重复: --" + name);
                    result.Options.Add(name, args[++index]);
                }
                return result;
            }

            public int GetInt(string name, int defaultValue, bool positive)
            {
                string text;
                if (!Options.TryGetValue(name, out text)) return defaultValue;
                int value;
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || (positive && value <= 0) || (!positive && value < 0))
                {
                    throw new WorkflowParameterException("--" + name + " 必须是" + (positive ? "正整数" : "非负整数"));
                }
                return value;
            }

            public int GetAnyInt(string name, int defaultValue)
            {
                string text;
                if (!Options.TryGetValue(name, out text)) return defaultValue;
                int value;
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    throw new WorkflowParameterException("--" + name + " 必须是整数");
                }
                return value;
            }

            public double GetDouble(string name, double defaultValue)
            {
                string text;
                if (!Options.TryGetValue(name, out text)) return defaultValue;
                double value;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0.0 || value > 1.0)
                {
                    throw new WorkflowParameterException("--" + name + " 必须在 0 到 1 之间");
                }
                return value;
            }

            public bool GetBool(string name, bool defaultValue)
            {
                string text;
                if (!Options.TryGetValue(name, out text)) return defaultValue;
                bool value;
                if (!bool.TryParse(text, out value)) throw new WorkflowParameterException("--" + name + " 必须为 true 或 false");
                return value;
            }

            public JObject CreateInferParameters(int batchSize)
            {
                var result = new JObject
                {
                    ["threshold"] = GetDouble("threshold", 0.5),
                    ["with_mask"] = GetBool("with-mask", true),
                    ["batch_size"] = batchSize
                };
                string calcMean;
                if (Options.TryGetValue("calc-mean", out calcMean))
                {
                    if (string.Equals(calcMean, "true", StringComparison.OrdinalIgnoreCase)) result["calc_mean"] = true;
                    else if (string.Equals(calcMean, "false", StringComparison.OrdinalIgnoreCase)) result["calc_mean"] = false;
                    else if (!string.Equals(calcMean, "default", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new WorkflowParameterException("--calc-mean 必须为 default、true 或 false");
                    }
                }
                return result;
            }
        }

        private sealed class WorkflowModelEntry
        {
            public string Name { get; private set; }
            public string Path { get; private set; }
            public int DeviceId { get; private set; }
            public bool RpcMode { get; private set; }
            public Model Model { get; private set; }
            public double LoadMs { get; private set; }

            public WorkflowModelEntry(string name, string path, int deviceId, bool rpcMode, Model model, double loadMs)
            {
                Name = name;
                Path = path;
                DeviceId = deviceId;
                RpcMode = rpcMode;
                Model = model;
                LoadMs = loadMs;
            }
        }

        private sealed class WorkflowContext
        {
            public Dictionary<string, WorkflowModelEntry> Models { get; private set; }

            public WorkflowContext()
            {
                Models = new Dictionary<string, WorkflowModelEntry>(StringComparer.OrdinalIgnoreCase);
            }

            public WorkflowModelEntry Get(string name)
            {
                WorkflowModelEntry entry;
                if (!Models.TryGetValue(name, out entry)) throw new WorkflowExecutionException("未找到已加载模型: " + name);
                return entry;
            }

            public void Free(string name)
            {
                WorkflowModelEntry entry = Get(name);
                Exception freeError = null;
                Exception disposeError = null;
                try { entry.Model.FreeModel(); }
                catch (Exception ex) { freeError = ex; }
                try { entry.Model.Dispose(); }
                catch (Exception ex) { disposeError = ex; }
                if (freeError == null && disposeError == null)
                {
                    Models.Remove(name);
                    return;
                }

                var messages = new List<string>();
                if (freeError != null) messages.Add("FreeModel: " + freeError.Message);
                if (disposeError != null) messages.Add("Dispose: " + disposeError.Message);
                throw new WorkflowExecutionException("模型 " + entry.Name + " 释放失败: " + string.Join("；", messages));
            }

            public List<string> FreeAll()
            {
                var failures = new List<string>();
                var entries = Models.Values.ToList();
                foreach (var entry in entries)
                {
                    try { Free(entry.Name); }
                    catch (Exception ex) { failures.Add(ex.Message); }
                }
                try { Utils.FreeAllModels(); }
                catch (Exception ex) { failures.Add("Utils.FreeAllModels: " + ex.Message); }
                return failures;
            }

            public List<string> DisposeAll()
            {
                var failures = new List<string>();
                foreach (var name in Models.Keys.ToList())
                {
                    try { Free(name); }
                    catch (Exception ex) { failures.Add(ex.Message); }
                }
                return failures;
            }
        }

        private static int RunDefaultCases()
        {
            Console.WriteLine("==== C# 固定模型结果回归测试 ====");
            Console.WriteLine("模型目录: " + ModelRoot);
            Console.WriteLine("固定设备: GPU(" + GpuDeviceId + ")");
            Console.WriteLine("固定Batch: " + FixedBatchSize);
            Console.WriteLine();

            bool modelRootOk = Directory.Exists(ModelRoot);
            if (!modelRootOk)
            {
                Console.WriteLine("模型目录不存在: " + ModelRoot);
            }

            // 内存检查只使用清单中的实例分割模型。
            string leakModelPath = null;
            string leakImagePath = null;
            if (modelRootOk)
            {
                foreach (var c in DefaultCases)
                {
                    if (!c.Name.Contains("实例分割")) continue;
                    string mp = Path.Combine(ModelRoot, c.ModelFile);
                    string ip = Path.Combine(ModelRoot, c.ImageFile);
                    if (!File.Exists(mp) || !File.Exists(ip)) continue;
                    leakModelPath = mp;
                    leakImagePath = ip;
                    break;
                }
            }

            var rows = new List<CaseRow>(DefaultCases.Count);
            int total = DefaultCases.Count;
            int pass = 0;
            foreach (var c in DefaultCases)
            {
                string modelPath = Path.Combine(ModelRoot, c.ModelFile);
                string imagePath = Path.Combine(ModelRoot, c.ImageFile);
                if (!modelRootOk)
                {
                    rows.Add(new CaseRow
                    {
                        ModelName = c.Name,
                        LoadStatus = "失败",
                        InferStatus = "未执行",
                        ResultStatus = "失败：模型目录不存在",
                        CategoryList = "-",
                        SpeedText = "-",
                        BatchText = "-"
                    });
                    continue;
                }
                if (!File.Exists(modelPath) || !File.Exists(imagePath))
                {
                    rows.Add(new CaseRow
                    {
                        ModelName = c.Name,
                        LoadStatus = "失败",
                        InferStatus = "未执行",
                        ResultStatus = "失败：" + (!File.Exists(modelPath) ? "模型不存在" : "图片不存在"),
                        CategoryList = "-",
                        SpeedText = "-",
                        BatchText = "-"
                    });
                    continue;
                }

                var row = RunCase(c, modelPath, imagePath);
                rows.Add(row);
                if (row.LoadStatus.StartsWith("成功") && row.InferStatus.StartsWith("成功") && row.ResultStatus == "结果一致") pass++;
            }

            rows.Add(new CaseRow
            {
                ModelName = "汇总",
                LoadStatus = "总数=" + total,
                InferStatus = "成功=" + pass,
                ResultStatus = "失败=" + (total - pass),
                CategoryList = "-",
                SpeedText = "-",
                BatchText = "-"
            });

            PrintHeader();
            foreach (var r in rows)
            {
                PrintRow(r.ModelName, r.LoadStatus, r.InferStatus, r.ResultStatus, r.CategoryList, r.SpeedText, r.BatchText);
            }
            PrintFooter();

            Console.WriteLine("==== 内存泄露专项(仅测1个实例分割模型) ====");
            if (!modelRootOk)
            {
                Console.WriteLine("失败：模型目录不存在");
            }
            else if (string.IsNullOrEmpty(leakModelPath) || string.IsNullOrEmpty(leakImagePath))
            {
                Console.WriteLine("失败：未找到可用实例分割模型");
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

        private static int RunFlowBatchSelfTest(string[] args)
        {
            if (args == null || args.Length < 3)
            {
                Console.WriteLine("用法: DlcvCSharpTest flow-batch-selftest <modelPath> <imagePath> [batch]");
                return 2;
            }

            string modelPath = args[1];
            string imagePath = args[2];
            int batch = ParsePositiveIntArg(args, 3, 1);
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

                var p = new JObject
                {
                    ["threshold"] = 0.5,
                    ["with_mask"] = false,
                    ["batch_size"] = 1
                };
                var images = new List<Mat>(batch);
                for (int i = 0; i < batch; i++) images.Add(rgb);
                p["batch_size"] = batch;
                var result = model.InferBatch(images, p);
                DisposeResultMasks(result);

                var infos = InferTiming.GetLastFlowModelBatchInfos();
                foreach (var info in infos.OrderBy(x => x.NodeId))
                {
                    Console.WriteLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "node={0}, model={1}, inputs={2}, limit={3}, calls={4}, max_actual_batch={5}",
                            info.NodeId,
                            Path.GetFileName(info.ModelPath),
                            info.InputCount,
                            info.BatchLimit,
                            info.InferCallCount,
                            info.MaxActualBatch));
                }

                var candidate = infos.OrderByDescending(x => x.InputCount).FirstOrDefault();
                if (candidate == null || candidate.InputCount <= 1)
                {
                    Console.WriteLine("SELFTEST FAILED: 流程没有生成多个二阶段输入，无法验证内部 batch");
                    return 1;
                }
                if (candidate.BatchLimit <= 1 || candidate.MaxActualBatch <= 1)
                {
                    Console.WriteLine("SELFTEST FAILED: 二阶段输入已聚合，但实际仍按 batch=1 推理");
                    return 1;
                }

                Console.WriteLine("SELFTEST PASSED");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SELFTEST ERROR: " + ex.Message);
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

        private static CaseRow RunCase(ModelRegressionCase regressionCase, string modelPath, string imagePath)
        {
            var row = new CaseRow
            {
                ModelName = regressionCase.Name,
                LoadStatus = "失败",
                InferStatus = "失败",
                ResultStatus = "未校验",
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
                try { if (model != null) model.Dispose(); } catch { }
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

                var p = new JObject { ["threshold"] = 0.5, ["with_mask"] = true };
                try
                {
                    var swInfer = Stopwatch.StartNew();
                    var r = model.InferBatch(new List<Mat> { rgb }, p);
                    swInfer.Stop();
                    try
                    {
                        row.InferStatus = (r.SampleResults != null && r.SampleResults.Count > 0) ? "成功(" + swInfer.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture) + "ms)" : "失败";
                        row.CategoryList = BuildCategoryList(r);
                        if (string.IsNullOrWhiteSpace(row.CategoryList)) row.CategoryList = "(空)";
                        string validationFailure;
                        if (ValidateRegressionResult(regressionCase, r, out validationFailure))
                        {
                            row.ResultStatus = "结果一致";
                        }
                        else
                        {
                            row.ResultStatus = "失败：" + validationFailure;
                        }
                    }
                    finally
                    {
                        DisposeResultMasks(r);
                    }
                }
                catch (Exception ex)
                {
                    row.InferStatus = "失败";
                    row.ResultStatus = "未校验";
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
                row.ResultStatus = "未校验";
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

        private static bool ValidateRegressionResult(ModelRegressionCase regressionCase, Utils.CSharpResult actual, out string failure)
        {
            var failures = new List<string>();
            int actualSampleCount = actual.SampleResults == null ? 0 : actual.SampleResults.Count;
            if (actualSampleCount != regressionCase.Samples.Count)
            {
                failures.Add(RegressionMismatch(regressionCase.Name, "samples.count", regressionCase.Samples.Count, actualSampleCount, 0));
            }

            int sampleCount = Math.Min(regressionCase.Samples.Count, actualSampleCount);
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var expectedSample = regressionCase.Samples[sampleIndex];
                var actualSample = actual.SampleResults[sampleIndex];
                int actualResultCount = actualSample.Results == null ? 0 : actualSample.Results.Count;
                if (actualResultCount != expectedSample.Results.Count)
                {
                    failures.Add(RegressionMismatch(regressionCase.Name, "samples[" + sampleIndex + "].results.count", expectedSample.Results.Count, actualResultCount, 0));
                }

                int resultCount = Math.Min(expectedSample.Results.Count, actualResultCount);
                for (int resultIndex = 0; resultIndex < resultCount; resultIndex++)
                {
                    ValidateRegressionObject(regressionCase.Name, sampleIndex, resultIndex,
                        expectedSample.Results[resultIndex], actualSample.Results[resultIndex], failures);
                }
            }

            failure = failures.Count == 0 ? string.Empty : string.Join("；", failures);
            return failures.Count == 0;
        }

        private static void ValidateRegressionObject(string caseName, int sampleIndex, int resultIndex,
            ModelRegressionResult expected, Utils.CSharpObjectResult actual, List<string> failures)
        {
            string path = "samples[" + sampleIndex + "].results[" + resultIndex + "]";
            if (actual.CategoryId != expected.CategoryId)
            {
                failures.Add(RegressionMismatch(caseName, path + ".category_id", expected.CategoryId, actual.CategoryId, 0));
            }
            if (!string.Equals(actual.CategoryName, expected.CategoryName, StringComparison.Ordinal))
            {
                failures.Add(RegressionMismatch(caseName, path + ".category_name", expected.CategoryName, actual.CategoryName, "无"));
            }
            if (actual.WithBbox != expected.WithBbox)
            {
                failures.Add(RegressionMismatch(caseName, path + ".with_bbox", expected.WithBbox, actual.WithBbox, "无"));
            }
            if (actual.WithMask != expected.WithMask)
            {
                failures.Add(RegressionMismatch(caseName, path + ".with_mask", expected.WithMask, actual.WithMask, "无"));
            }
            if (actual.WithAngle != expected.WithAngle)
            {
                failures.Add(RegressionMismatch(caseName, path + ".with_angle", expected.WithAngle, actual.WithAngle, "无"));
            }
            ValidateRegressionNumber(caseName, path + ".score", expected.Score, actual.Score, ModelRegressionCases.ScoreTolerance, failures);
            ValidateRegressionBbox(caseName, path, expected.Bbox, actual.Bbox, failures);
            ValidateRegressionNumber(caseName, path + ".area", expected.Area, actual.Area, ModelRegressionCases.AreaTolerance, failures);
            ValidateRegressionNumber(caseName, path + ".angle", expected.Angle, actual.Angle, ModelRegressionCases.AngleTolerance, failures);

            if (expected.WithMask)
            {
                ValidateRegressionMask(caseName, path + ".mask", expected.Mask, actual.Mask, failures);
            }
            else if (actual.Mask != null && !actual.Mask.Empty())
            {
                failures.Add(RegressionMismatch(caseName, path + ".mask", "空引用", "非空 Mat", "无"));
            }
        }

        private static void ValidateRegressionBbox(string caseName, string path, double[] expected, List<double> actual, List<string> failures)
        {
            int actualCount = actual == null ? 0 : actual.Count;
            if (actualCount != expected.Length)
            {
                failures.Add(RegressionMismatch(caseName, path + ".bbox.count", expected.Length, actualCount, 0));
            }
            int count = Math.Min(expected.Length, actualCount);
            for (int index = 0; index < count; index++)
            {
                ValidateRegressionNumber(caseName, path + ".bbox[" + index + "]", expected[index], actual[index], ModelRegressionCases.BboxTolerance, failures);
            }
        }

        private static void ValidateRegressionMask(string caseName, string path, ModelRegressionMask expected, Mat actual, List<string> failures)
        {
            if (actual == null)
            {
                failures.Add(RegressionMismatch(caseName, path, "非空 Mat", "空引用", "无"));
                return;
            }
            if (actual.Empty())
            {
                failures.Add(RegressionMismatch(caseName, path, "非空 Mat", "空 Mat", "无"));
                return;
            }
            if (actual.Width != expected.Width)
            {
                failures.Add(RegressionMismatch(caseName, path + ".width", expected.Width, actual.Width, 0));
            }
            if (actual.Height != expected.Height)
            {
                failures.Add(RegressionMismatch(caseName, path + ".height", expected.Height, actual.Height, 0));
            }
            if (actual.Channels() != 1)
            {
                failures.Add(RegressionMismatch(caseName, path + ".channels", 1, actual.Channels(), 0));
                return;
            }
            int actualNonZero = Cv2.CountNonZero(actual);
            if (Math.Abs(actualNonZero - expected.NonZero) > ModelRegressionCases.MaskNonZeroTolerance)
            {
                failures.Add(RegressionMismatch(caseName, path + ".nonzero", expected.NonZero, actualNonZero, ModelRegressionCases.MaskNonZeroTolerance));
            }
        }

        private static void ValidateRegressionNumber(string caseName, string path, double expected, double actual, double tolerance, List<string> failures)
        {
            if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(expected - actual) > tolerance)
            {
                failures.Add(RegressionMismatch(caseName, path, expected, actual, tolerance));
            }
        }

        private static string RegressionMismatch(string caseName, string path, object expected, object actual, object tolerance)
        {
            return caseName + " " + path + "，期望=" + FormatRegressionValue(expected)
                + "，实际=" + FormatRegressionValue(actual) + "，容差=" + FormatRegressionValue(tolerance);
        }

        private static string FormatRegressionValue(object value)
        {
            if (value == null) return "空引用";
            var number = value as IFormattable;
            return number == null ? value.ToString() : number.ToString(null, CultureInfo.InvariantCulture);
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
            Console.WriteLine("| 用例 | 加载 | 推理 | 结果校验 | 类别列表 | 3秒速度 | Batch速度 |");
            Console.WriteLine("|---|---|---|---|---|---|---|");
        }

        private static void PrintRow(string model, string load, string infer, string validation, string cats, string speed, string batch)
        {
            Console.WriteLine("| " + Safe(model) + " | " + Safe(load) + " | " + Safe(infer) + " | " + Safe(validation) + " | " + Safe(cats) + " | " + Safe(speed) + " | " + Safe(batch) + " |");
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
                || ext.Equals(".dvso", StringComparison.OrdinalIgnoreCase);
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

        private static int RunDvspDisabledSelfTest()
        {
            string modelPath = Path.Combine(Path.GetTempPath(), "unsupported_model.dvsp");
            try
            {
                using (var model = new Model(modelPath, GpuDeviceId, false, false))
                {
                }
                Console.WriteLine("DVSP 禁用自测失败：接口未拒绝 .dvsp");
                return 1;
            }
            catch (NotSupportedException ex)
            {
                Console.WriteLine("DVSP 禁用自测通过：" + ex.Message);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("DVSP 禁用自测失败：异常类型错误，" + ex.Message);
                return 1;
            }
        }

        private static int RunGetModelInfoCommand(string[] args, bool dvsInfo)
        {
            string command = dvsInfo ? "get-dvs-model-info" : "get-model-info";
            if (args == null || args.Length != 2)
            {
                Console.Error.WriteLine("用法: DlcvCSharpTest " + command + " <model>");
                return 2;
            }

            Model model = null;
            string resultJson = null;
            try
            {
                model = new Model(args[1], GpuDeviceId, false, false);
                JObject result = dvsInfo ? model.GetDvsModelInfo() : model.GetModelInfo();
                resultJson = result.ToString(Formatting.Indented);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            finally
            {
                try { if (model != null) model.Dispose(); } catch { }
                ForceGc();
            }

            Console.Out.WriteLine(resultJson);
            return 0;
        }

        private static int RunModelLoadFreeStageSelfTest(string[] args)
        {
            if (args == null || args.Length < 2 || args.Length > 4)
            {
                Console.Error.WriteLine("用法: DlcvCSharpTest model-load-free-stage-selftest <modelPath> [device] [loopCount]");
                return 2;
            }

            if (!TryNormalizeSelfTestPath(args[1], "模型", out string modelPath))
            {
                return 2;
            }
            if (!File.Exists(modelPath))
            {
                Console.Error.WriteLine("模型不存在: " + modelPath);
                return 2;
            }

            int deviceId = GpuDeviceId;
            int loopCount = 3;
            if (args.Length >= 3 && !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out deviceId))
            {
                Console.Error.WriteLine("device 必须为整数");
                return 2;
            }
            if (args.Length >= 4 && (!int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out loopCount) || loopCount <= 0))
            {
                Console.Error.WriteLine("loopCount 必须为正整数");
                return 2;
            }

            Console.WriteLine("==== 模型加载释放分阶段内存专项 ====");
            Console.WriteLine("模型: " + modelPath);
            Console.WriteLine("设备: " + deviceId.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("循环次数: " + loopCount.ToString(CultureInfo.InvariantCulture));

            ForceGc();
            if (!WriteMemoryStage("程序启动", 0))
            {
                return 1;
            }

            for (int i = 1; i <= loopCount; i++)
            {
                Model model = null;
                string releaseError = null;
                try
                {
                    model = new Model(modelPath, deviceId, false, false);
                    ForceGc();
                    if (!WriteMemoryStage("第" + i.ToString(CultureInfo.InvariantCulture) + "次加载完成", 1))
                    {
                        return 1;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("第" + i.ToString(CultureInfo.InvariantCulture) + "次加载失败: " + ex.Message);
                    return 1;
                }
                finally
                {
                    try { if (model != null) model.Dispose(); }
                    catch (Exception ex) { releaseError = ex.Message; }
                    ForceGc();
                }
                if (!string.IsNullOrEmpty(releaseError))
                {
                    Console.Error.WriteLine("第" + i.ToString(CultureInfo.InvariantCulture) + "次释放失败: " + releaseError);
                    return 1;
                }

                if (!WriteMemoryStage("第" + i.ToString(CultureInfo.InvariantCulture) + "次释放完成", 0))
                {
                    return 1;
                }
            }

            Console.WriteLine("模型加载释放分阶段内存专项完成");
            return 0;
        }

        private static bool WriteMemoryStage(string stage, int expectedActiveModelCount)
        {
            MemorySnapshot snapshot = MemorySnapshot.Capture();
            if (!TryGetActiveModelCount(out int activeModelCount, out string snapshotError))
            {
                Console.Error.WriteLine("读取活动模型数失败: " + snapshotError);
                return false;
            }

            Console.WriteLine(
                "阶段=" + stage +
                ", 私有内存=" + snapshot.PrivateMb.ToString("F2", CultureInfo.InvariantCulture) + "MB" +
                ", 工作集=" + snapshot.WorkingSetMb.ToString("F2", CultureInfo.InvariantCulture) + "MB" +
                ", 活动模型=" + activeModelCount.ToString(CultureInfo.InvariantCulture));
            if (activeModelCount != expectedActiveModelCount)
            {
                Console.Error.WriteLine(
                    stage + "活动模型数不符合预期，期望=" +
                    expectedActiveModelCount.ToString(CultureInfo.InvariantCulture) +
                    "，实际=" + activeModelCount.ToString(CultureInfo.InvariantCulture));
                return false;
            }
            return true;
        }

        private static int RunModelLoadFreeMemorySelfTest(string[] args)
        {
            if (args == null || args.Length < 2 || args.Length > 5)
            {
                Console.Error.WriteLine("用法: DlcvCSharpTest model-load-free-memory-selftest <modelPath> [device] [loopCount] [sampleInterval]");
                return 2;
            }

            if (!TryNormalizeSelfTestPath(args[1], "模型", out string modelPath))
            {
                return 2;
            }
            if (!File.Exists(modelPath))
            {
                Console.Error.WriteLine("模型不存在: " + modelPath);
                return 2;
            }

            int deviceId = GpuDeviceId;
            int loopCount = DefaultLoadFreeMemoryLoopCount;
            int sampleInterval = DefaultLoadFreeMemorySampleInterval;
            if (args.Length >= 3 && !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out deviceId))
            {
                Console.Error.WriteLine("device 必须为整数");
                return 2;
            }
            if (args.Length >= 4 && (!int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out loopCount) || loopCount <= 0))
            {
                Console.Error.WriteLine("loopCount 必须为正整数");
                return 2;
            }
            if (args.Length >= 5 && (!int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleInterval) || sampleInterval <= 0))
            {
                Console.Error.WriteLine("sampleInterval 必须为正整数");
                return 2;
            }

            Console.WriteLine("==== 模型加载释放内存专项 ====");
            Console.WriteLine("模型: " + modelPath);
            Console.WriteLine("设备: " + deviceId.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("循环次数: " + loopCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("采样间隔: " + sampleInterval.ToString(CultureInfo.InvariantCulture));

            ForceGc();
            MemorySnapshot baseline = MemorySnapshot.Capture();
            MemorySnapshot firstReleased = baseline;
            var privateMemorySamples = new List<double>(loopCount);
            bool activeModelFailure = false;

            for (int i = 1; i <= loopCount; i++)
            {
                Model model = null;
                string releaseError = null;
                try
                {
                    model = new Model(modelPath, deviceId, false, false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("第" + i.ToString(CultureInfo.InvariantCulture) + "次加载失败: " + ex.Message);
                    return 1;
                }
                finally
                {
                    try { if (model != null) model.Dispose(); }
                    catch (Exception ex) { releaseError = ex.Message; }
                    ForceGc();
                }
                if (!string.IsNullOrEmpty(releaseError))
                {
                    Console.Error.WriteLine("第" + i.ToString(CultureInfo.InvariantCulture) + "次释放失败: " + releaseError);
                    return 1;
                }

                MemorySnapshot current = MemorySnapshot.Capture();
                privateMemorySamples.Add(current.PrivateMb);
                if (i == 1)
                {
                    firstReleased = current;
                }

                if (i == 1 || i % sampleInterval == 0 || i == loopCount)
                {
                    if (!TryGetActiveModelCount(out int activeModelCount, out string snapshotError))
                    {
                        Console.Error.WriteLine("读取活动模型数失败: " + snapshotError);
                        return 1;
                    }
                    if (activeModelCount != 0)
                    {
                        activeModelFailure = true;
                    }
                    Console.WriteLine(
                        "循环=" + i.ToString(CultureInfo.InvariantCulture) +
                        ", 私有内存=" + current.PrivateMb.ToString("F2", CultureInfo.InvariantCulture) + "MB" +
                        ", 相对起点=" + (current.PrivateMb - baseline.PrivateMb).ToString("F2", CultureInfo.InvariantCulture) + "MB" +
                        ", 相对第1次=" + (current.PrivateMb - firstReleased.PrivateMb).ToString("F2", CultureInfo.InvariantCulture) + "MB" +
                        ", 工作集=" + current.WorkingSetMb.ToString("F2", CultureInfo.InvariantCulture) + "MB" +
                        ", 活动模型=" + activeModelCount.ToString(CultureInfo.InvariantCulture));
                }
            }

            MemorySnapshot finalSnapshot = MemorySnapshot.Capture();
            double slopeAfterFirst = CalculateMemorySlope(privateMemorySamples, 1);
            double slopeLastHalf = CalculateMemorySlope(privateMemorySamples, Math.Max(1, privateMemorySamples.Count / 2));
            Console.WriteLine("起点私有内存: " + baseline.PrivateMb.ToString("F2", CultureInfo.InvariantCulture) + "MB");
            Console.WriteLine("第1次释放后私有内存: " + firstReleased.PrivateMb.ToString("F2", CultureInfo.InvariantCulture) + "MB");
            Console.WriteLine("最终私有内存: " + finalSnapshot.PrivateMb.ToString("F2", CultureInfo.InvariantCulture) + "MB");
            Console.WriteLine("总增量: " + (finalSnapshot.PrivateMb - baseline.PrivateMb).ToString("F2", CultureInfo.InvariantCulture) + "MB");
            Console.WriteLine("排除第1次后的增量: " + (finalSnapshot.PrivateMb - firstReleased.PrivateMb).ToString("F2", CultureInfo.InvariantCulture) + "MB");
            Console.WriteLine("排除第1次后的线性变化: " + slopeAfterFirst.ToString("F4", CultureInfo.InvariantCulture) + "MB/次");
            Console.WriteLine("后半程线性变化: " + slopeLastHalf.ToString("F4", CultureInfo.InvariantCulture) + "MB/次");

            if (activeModelFailure)
            {
                Console.Error.WriteLine("释放后仍存在活动模型");
                return 1;
            }
            Console.WriteLine("模型加载释放内存专项完成");
            return 0;
        }

        private static bool TryGetActiveModelCount(out int count, out string error)
        {
            count = 0;
            error = null;
            try
            {
                JObject snapshot = Utils.GetAllModels();
                if (snapshot == null || snapshot["code"] == null || snapshot["code"].Value<int>() != 0)
                {
                    error = snapshot != null && snapshot["message"] != null
                        ? snapshot["message"].ToString()
                        : "模型快照无效";
                    return false;
                }

                JArray modules = snapshot["modules"] as JArray;
                if (modules == null)
                {
                    error = "模型快照缺少 modules";
                    return false;
                }
                foreach (JToken module in modules)
                {
                    JArray models = module["models"] as JArray;
                    if (models != null)
                    {
                        count += models.Count;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static double CalculateMemorySlope(IList<double> values, int startIndex)
        {
            if (values == null || values.Count - startIndex < 2)
            {
                return 0.0;
            }

            int count = values.Count - startIndex;
            double sumX = 0.0;
            double sumY = 0.0;
            double sumXY = 0.0;
            double sumXX = 0.0;
            for (int i = startIndex; i < values.Count; i++)
            {
                double x = i + 1;
                double y = values[i];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
            }
            double denominator = count * sumXX - sumX * sumX;
            return Math.Abs(denominator) < double.Epsilon
                ? 0.0
                : (count * sumXY - sumX * sumY) / denominator;
        }

        private static int RunDvsMemoryLoadingSelfTest(string[] args)
        {
            if (args == null || args.Length < 3 || args.Length > 4)
            {
                Console.Error.WriteLine("用法: DlcvCSharpTest dvs-memory-loading-selftest <modelPath> <imagePath> [device]");
                return 2;
            }

            if (!TryNormalizeSelfTestPath(args[1], "模型", out string modelPath)
                || !TryNormalizeSelfTestPath(args[2], "图片", out string imagePath))
            {
                return 2;
            }
            int deviceId = GpuDeviceId;
            if (args.Length == 4 && !int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out deviceId))
            {
                Console.Error.WriteLine("device 必须为整数");
                return 2;
            }
            string extension = Path.GetExtension(modelPath);
            if (!string.Equals(extension, ".dvst", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".dvso", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("模型必须为 .dvst 或 .dvso");
                return 2;
            }
            if (!File.Exists(modelPath) || !File.Exists(imagePath))
            {
                Console.Error.WriteLine("模型或图片不存在");
                return 2;
            }

            DvsTempArtifactMonitor monitor = null;
            Model model = null;
            Mat bgr = null;
            Mat rgb = null;
            Utils.CSharpResult result = default(Utils.CSharpResult);
            string operationError = null;
            try
            {
                monitor = new DvsTempArtifactMonitor(modelPath);
                model = new Model(modelPath, deviceId, false, false);
                bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (bgr == null || bgr.Empty()) throw new Exception("图片读取失败");
                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
                result = model.Infer(rgb, new JObject
                {
                    ["threshold"] = 0.5,
                    ["with_mask"] = true,
                    ["batch_size"] = 1
                });
            }
            catch (Exception ex)
            {
                operationError = ex.Message;
            }
            finally
            {
                DisposeResultMasks(result);
                if (rgb != null) rgb.Dispose();
                if (bgr != null) bgr.Dispose();
                try { if (model != null) model.Dispose(); } catch (Exception ex) { if (operationError == null) operationError = ex.Message; }
                ForceGc();
                if (monitor != null) monitor.Dispose();
            }

            if (!string.IsNullOrEmpty(operationError))
            {
                Console.Error.WriteLine(operationError);
                return 1;
            }
            if (monitor != null && (monitor.HasArtifacts || monitor.HasWatcherError))
            {
                Console.Error.WriteLine("系统临时目录出现流程归档文件: " + monitor.Describe());
                return 1;
            }
            Console.WriteLine("C# 流程归档内存加载测试通过");
            return 0;
        }

        private static int RunDvspRejectSelfTest(string[] args)
        {
            if (args == null || args.Length < 2 || args.Length > 3)
            {
                Console.Error.WriteLine("用法: DlcvCSharpTest dvsp-reject-selftest <modelPath> [device]");
                return 2;
            }

            if (!TryNormalizeSelfTestPath(args[1], "模型", out string modelPath))
            {
                return 2;
            }
            int deviceId = GpuDeviceId;
            if (args.Length == 3 && !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out deviceId))
            {
                Console.Error.WriteLine("device 必须为整数");
                return 2;
            }
            if (!string.Equals(Path.GetExtension(modelPath), ".dvsp", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("模型必须为 .dvsp");
                return 2;
            }
            if (!File.Exists(modelPath))
            {
                Console.Error.WriteLine("模型不存在");
                return 2;
            }

            DvsTempArtifactMonitor monitor = null;
            Model model = null;
            string errorMessage = null;
            try
            {
                monitor = new DvsTempArtifactMonitor(modelPath, false);
                model = new Model(modelPath, deviceId, false, false);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }
            finally
            {
                try { if (model != null) model.Dispose(); } catch { }
                ForceGc();
                if (monitor != null) monitor.Dispose();
            }

            if (!HasExplicitDvspUnsupportedMessage(errorMessage))
            {
                Console.Error.WriteLine(".dvsp 未返回明确的不支持错误: " + (errorMessage ?? string.Empty));
                return 1;
            }
            if (monitor != null && (monitor.HasArtifacts || monitor.HasWatcherError))
            {
                Console.Error.WriteLine("拒绝 .dvsp 时系统临时目录出现流程归档文件: " + monitor.Describe());
                return 1;
            }
            Console.WriteLine("C# .dvsp 拒绝测试通过");
            return 0;
        }

        private static bool TryNormalizeSelfTestPath(string value, string displayName, out string normalizedPath)
        {
            normalizedPath = (value ?? string.Empty).Trim();
            if (normalizedPath.Length >= 2)
            {
                char first = normalizedPath[0];
                char last = normalizedPath[normalizedPath.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    normalizedPath = normalizedPath.Substring(1, normalizedPath.Length - 2).Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                Console.Error.WriteLine(displayName + "路径为空");
                return false;
            }

            try
            {
                normalizedPath = Path.GetFullPath(normalizedPath);
                return true;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(displayName + "路径无效: " + ex.Message);
                return false;
            }
            catch (NotSupportedException ex)
            {
                Console.Error.WriteLine(displayName + "路径无效: " + ex.Message);
                return false;
            }
            catch (PathTooLongException ex)
            {
                Console.Error.WriteLine(displayName + "路径过长: " + ex.Message);
                return false;
            }
        }

        private static bool HasExplicitDvspUnsupportedMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            return message.IndexOf("dvsp", StringComparison.OrdinalIgnoreCase) >= 0
                && (message.IndexOf("不支持", StringComparison.Ordinal) >= 0
                    || message.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("not support", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasUndersizedModelMessage(string message)
        {
            return !string.IsNullOrWhiteSpace(message)
                && message.IndexOf("小于 1MB", StringComparison.Ordinal) >= 0;
        }

        private static string LoadModelExpectingError(string path)
        {
            Model model = null;
            try
            {
                model = new Model(path, GpuDeviceId, false, false);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                try { if (model != null) model.Dispose(); } catch { }
            }
        }

        private static int RunUndersizedModelSelfTest()
        {
            var files = new List<string>();
            try
            {
                string[] extensions = { ".dvt", ".dvo", ".dvp", ".dvst", ".dvso" };
                foreach (string ext in extensions)
                {
                    string path = Path.Combine(Path.GetTempPath(), "dlcv_undersized_" + Guid.NewGuid().ToString("N") + ext);
                    File.WriteAllBytes(path, new byte[512]);
                    files.Add(path);
                    string error = LoadModelExpectingError(path);
                    if (!HasUndersizedModelMessage(error))
                    {
                        Console.Error.WriteLine(ext + " 未返回过小文件错误: " + (error ?? "加载成功"));
                        return 1;
                    }
                }

                string dvspPath = Path.Combine(Path.GetTempPath(), "dlcv_undersized_" + Guid.NewGuid().ToString("N") + ".dvsp");
                File.WriteAllBytes(dvspPath, new byte[512]);
                files.Add(dvspPath);
                string dvspError = LoadModelExpectingError(dvspPath);
                if (!HasExplicitDvspUnsupportedMessage(dvspError) || HasUndersizedModelMessage(dvspError))
                {
                    Console.Error.WriteLine(".dvsp 过小文件应返回不支持错误: " + (dvspError ?? "加载成功"));
                    return 1;
                }

                Console.WriteLine("C# 过小模型文件拒绝测试通过");
                return 0;
            }
            finally
            {
                foreach (string path in files)
                {
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                }
            }
        }

        private static int RunDvspParitySelfTest(string[] args)
        {
            if (args == null || args.Length < 4)
            {
                Console.WriteLine("用法: DlcvCSharpTest dvsp-parity-selftest <modelPath> <imagePath> <expectedJson>");
                return 2;
            }

            string modelPath = args[1];
            string imagePath = args[2];
            string expectedPath = args[3];
            if (!File.Exists(modelPath) || !File.Exists(imagePath) || !File.Exists(expectedPath))
            {
                Console.WriteLine("模型、图片或基线 JSON 不存在");
                return 2;
            }

            Model model = null;
            Mat bgr = null;
            Mat rgb = null;
            Utils.CSharpResult result = default(Utils.CSharpResult);
            try
            {
                var expectedRoot = JObject.Parse(File.ReadAllText(expectedPath, Encoding.UTF8));
                var expected = expectedRoot["results"] as JArray;
                if (expected == null) throw new Exception("基线 JSON 缺少 results 目标数组");

                model = new Model(modelPath, GpuDeviceId, false, false);
                bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (bgr == null || bgr.Empty()) throw new Exception("图像解码失败");
                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

                var inferParams = new JObject
                {
                    ["threshold"] = 0.0,
                    ["with_mask"] = false,
                    ["batch_size"] = 1
                };
                result = model.InferBatch(new List<Mat> { rgb }, inferParams);
                if (result.SampleResults == null || result.SampleResults.Count != 1 ||
                    result.SampleResults[0].Results == null)
                    throw new Exception("单图批量推理未返回一个有效样本");
                var actual = result.SampleResults[0].Results;

                Console.WriteLine("expected_count=" + expected.Count + ", actual_count=" + actual.Count);
                bool ok = expected.Count == actual.Count;
                int count = Math.Min(expected.Count, actual.Count);
                for (int i = 0; i < count; i++)
                {
                    var exp = expected[i] as JObject ?? new JObject();
                    var expBox = exp["bbox"] as JArray ?? new JArray();
                    var act = actual[i];
                    var actBox = act.Bbox ?? new List<double>();
                    double ax1 = actBox.Count > 0 ? actBox[0] : double.NaN;
                    double ay1 = actBox.Count > 1 ? actBox[1] : double.NaN;
                    double ax2 = actBox.Count > 2 ? ax1 + actBox[2] : double.NaN;
                    double ay2 = actBox.Count > 3 ? ay1 + actBox[3] : double.NaN;
                    double ex1 = expBox.Count > 0 ? expBox[0].Value<double>() : double.NaN;
                    double ey1 = expBox.Count > 1 ? expBox[1].Value<double>() : double.NaN;
                    double ex2 = expBox.Count > 2 ? expBox[2].Value<double>() : double.NaN;
                    double ey2 = expBox.Count > 3 ? expBox[3].Value<double>() : double.NaN;
                    double expectedScore = exp.Value<double?>("score") ?? double.NaN;
                    string expectedCategory = exp.Value<string>("category_name") ?? "";

                    bool itemOk = string.Equals(expectedCategory, act.CategoryName ?? "", StringComparison.Ordinal)
                        && Math.Abs(expectedScore - act.Score) <= 1e-6
                        && Math.Abs(ex1 - ax1) <= 1e-6
                        && Math.Abs(ey1 - ay1) <= 1e-6
                        && Math.Abs(ex2 - ax2) <= 1e-6
                        && Math.Abs(ey2 - ay2) <= 1e-6;
                    ok &= itemOk;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "[{0}] {1} score expected={2:R} actual={3:R}; bbox expected=[{4:R},{5:R},{6:R},{7:R}] actual=[{8:R},{9:R},{10:R},{11:R}] {12}",
                        i, act.CategoryName, expectedScore, (double)act.Score, ex1, ey1, ex2, ey2, ax1, ay1, ax2, ay2, itemOk ? "OK" : "MISMATCH"));
                }

                Console.WriteLine(ok ? "SELFTEST PASSED" : "SELFTEST FAILED");
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SELFTEST ERROR: " + ex.Message);
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

        private sealed class ReviewCheck
        {
            public string Name;
            public string Expected;
            public string Actual;
            public bool Passed;
        }

        private static int RunSharedIndexReviewSelfTest(string[] args)
        {
            string modelPath = args != null && args.Length >= 2
                ? args[1]
                : Path.Combine(ModelRoot, "猫狗-分类_s.dvo");
            string dvsPath = args != null && args.Length >= 3
                ? args[2]
                : Path.Combine(ModelRoot, "AOI-元件提取_PLUS_s.dvst");
            foreach (string path in new[] { modelPath, dvsPath })
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine("测试文件不存在: " + path);
                    return 2;
                }
            }

            var checks = new List<ReviewCheck>
            {
                RunFinalSharedExportsCheck(modelPath),
                RunModelFactoryLifecycleCheck(modelPath, "model", "普通模型共享生命周期"),
                RunModelFactoryLifecycleCheck(dvsPath, "dvs", "DVS 共享生命周期"),
                RunDvsChildReferenceCheck(dvsPath),
                RunGetAllModelsReviewCheck(modelPath, dvsPath)
            };

            int failed = 0;
            Console.WriteLine();
            Console.WriteLine("| 检查项 | 期望 | 实际 | 状态 |");
            Console.WriteLine("|---|---|---|---|");
            foreach (ReviewCheck check in checks)
            {
                string status = check.Passed ? "通过" : "失败";
                if (!check.Passed) failed++;
                Console.WriteLine("| " + check.Name + " | " + check.Expected + " | " + check.Actual + " | " + status + " |");
            }
            Console.WriteLine("共享索引最终接口自测: " + (failed == 0 ? "全部通过" : "失败 " + failed + " 项"));
            return failed == 0 ? 0 : 1;
        }

        private static ReviewCheck RunFinalSharedExportsCheck(string modelPath)
        {
            var check = new ReviewCheck
            {
                Name = "底层五个新增接口",
                Expected = "实际加载模块提供 DVS 登记、查询、类型查询、绑定和全量列表"
            };
            try
            {
                using (var model = new Model(modelPath, GpuDeviceId))
                {
                    DllLoader loader = model.Loader;
                    check.Passed = loader != null && loader.SupportsDvsRegistration &&
                        loader.dlcv_get_all_models != null;
                    check.Actual = check.Passed
                        ? "五个接口均可用"
                        : "接口不完整";
                }
            }
            catch (Exception ex)
            {
                check.Actual = ex.Message;
            }
            finally
            {
                try { Utils.FreeAllModels(); } catch { }
            }
            return check;
        }

        private static ReviewCheck RunModelFactoryLifecycleCheck(
            string modelPath, string expectedType, string name)
        {
            var check = new ReviewCheck
            {
                Name = name,
                Expected = "两个共享方各持有一次，创建方先释放后仍可读取，最后释放后编号消失"
            };
            Model owner = null;
            Model first = null;
            Model second = null;
            try
            {
                Utils.FreeAllModels();
                owner = new Model(modelPath, GpuDeviceId);
                int index = owner.modelIndex;
                DllLoader loader = ResolveAndValidateSharedIndex(index, expectedType, name);
                first = ModelFactory.CreateFromIndex(index);
                second = ModelFactory.CreateFromIndex(index);
                if (expectedType == "dvs")
                {
                    first.GetDvsModelInfo();
                    second.GetDvsModelInfo();
                }
                owner.Dispose();
                owner = null;
                first.GetModelInfo();
                first.Dispose();
                first.Dispose();
                first = null;
                second.GetModelInfo();
                second.Dispose();
                second.Dispose();
                second = null;
                check.Passed = loader.GetIndexType(index) == 0;
                check.Actual = check.Passed ? "最终编号已释放" : "最终编号仍存在";
            }
            catch (Exception ex)
            {
                check.Actual = ex.Message;
            }
            finally
            {
                try { second?.Dispose(); } catch { }
                try { first?.Dispose(); } catch { }
                try { owner?.Dispose(); } catch { }
                try { Utils.FreeAllModels(); } catch { }
            }
            return check;
        }

        private static ReviewCheck RunDvsChildReferenceCheck(string dvsPath)
        {
            var check = new ReviewCheck
            {
                Name = "DVS 子模型引用",
                Expected = "创建方释放后子模型仍存在，DVS 最后释放后不同子模型各自消失"
            };
            Model owner = null;
            Model borrower = null;
            try
            {
                Utils.FreeAllModels();
                owner = new Model(dvsPath, GpuDeviceId);
                int index = owner.modelIndex;
                DllLoader loader = ResolveAndValidateSharedIndex(index, "dvs", check.Name);
                JObject descriptor = owner.GetDvsModelInfo();
                ValidateDvsDescriptorShape(descriptor, index, "读取 DVS 描述");
                int[] childIndexes = ((JArray)descriptor["model_bindings"])
                    .OfType<JObject>()
                    .Select(item => item["model_index"].Value<int>())
                    .Distinct()
                    .ToArray();
                if (childIndexes.Length == 0)
                    throw new Exception("DVS 没有子模型绑定");
                JObject compatibleInfo = owner.GetModelInfo();
                if (compatibleInfo["model_index"]?.Type != JTokenType.Integer ||
                    !childIndexes.Contains(compatibleInfo["model_index"].Value<int>()))
                    throw new Exception("DVS 普通兼容信息未保留子模型 index");
                borrower = ModelFactory.CreateFromIndex(index);
                owner.Dispose();
                owner = null;
                if (childIndexes.Any(child => loader.GetIndexType(child) != 1))
                    throw new Exception("DVS 仍被共享时子模型提前释放");
                borrower.GetDvsModelInfo();
                borrower.Dispose();
                borrower = null;
                check.Passed = loader.GetIndexType(index) == 0 &&
                    childIndexes.All(child => loader.GetIndexType(child) == 0);
                check.Actual = check.Passed
                    ? "DVS 与 " + childIndexes.Length + " 个不同子模型均完成释放"
                    : "最后释放后仍有资源存在";
            }
            catch (Exception ex)
            {
                check.Actual = ex.Message;
            }
            finally
            {
                try { borrower?.Dispose(); } catch { }
                try { owner?.Dispose(); } catch { }
                try { Utils.FreeAllModels(); } catch { }
            }
            return check;
        }

        private static ReviewCheck RunGetAllModelsReviewCheck(string modelPath, string dvsPath)
        {
            var check = new ReviewCheck
            {
                Name = "全量模型列表",
                Expected = "只枚举已加载模块，模块快照保留来源且普通模型和 DVS 分别列出"
            };
            Model model = null;
            Model dvs = null;
            try
            {
                Utils.FreeAllModels();
                model = new Model(modelPath, GpuDeviceId);
                dvs = new Model(dvsPath, GpuDeviceId);
                string[] before = GetLoadedEngineModulePaths();
                JObject snapshot = Utils.GetAllModels();
                string[] after = GetLoadedEngineModulePaths();
                if (snapshot["code"]?.Value<int>() != 0 || !(snapshot["modules"] is JArray modules))
                    throw new Exception("GetAllModels 返回失败");
                if (!before.SequenceEqual(after, StringComparer.OrdinalIgnoreCase))
                    throw new Exception("GetAllModels 额外加载了推理模块");
                int modelMatches = 0;
                int dvsMatches = 0;
                foreach (JObject module in modules)
                {
                    if (module["code"]?.Type != JTokenType.Integer || module["code"].Value<int>() != 0 ||
                        module["message"]?.Type != JTokenType.String ||
                        module["provider"]?.Type != JTokenType.String ||
                        module["module_path"]?.Type != JTokenType.String || !(module["models"] is JArray models))
                        throw new Exception("模块快照字段不完整");
                    foreach (JObject item in models)
                    {
                        int itemIndex = item["model_index"].Value<int>();
                        string resourceType = item["resource_type"].ToString();
                        if (itemIndex == model.modelIndex && resourceType == "model") modelMatches++;
                        if (itemIndex == dvs.modelIndex && resourceType == "dvs") dvsMatches++;
                    }
                }
                check.Passed = modelMatches == 1 && dvsMatches == 1;
                check.Actual = "普通模型=" + modelMatches + "，DVS=" + dvsMatches + "，模块=" + modules.Count;
            }
            catch (Exception ex)
            {
                check.Actual = ex.Message;
            }
            finally
            {
                try { dvs?.Dispose(); } catch { }
                try { model?.Dispose(); } catch { }
                try { Utils.FreeAllModels(); } catch { }
            }
            return check;
        }

        private static int RunCsharpFreeAllModulesSelfTest(string[] args)
        {
            if (args == null || (args.Length != 1 && args.Length != 3))
            {
                Console.WriteLine("用法: free-all-modules-selftest [Sentinel模型路径 Virbox模型路径]");
                return 2;
            }
            string sentinelModelPath = args.Length == 3 ? args[1] : Path.Combine(ModelRoot, "猫狗-分类_PLUS_s.dvt");
            string virboxModelPath = args.Length == 3 ? args[2] : Path.Combine(ModelRoot, "猫狗-分类_PLUS_v.dvt");
            try
            {
                DllLoader.EnsureForModel(sentinelModelPath);
                DllLoader defaultLoader = DllLoader.Instance;
                DllLoader sentinelLoader = LoadExplicitTestModule("dlcv_infer.dll");
                DllLoader virboxLoader = LoadExplicitTestModule("dlcv_infer_v.dll");
                int sentinelIndex = LoadNativeTestModel(sentinelLoader, sentinelModelPath);
                int virboxIndex = LoadNativeTestModel(virboxLoader, virboxModelPath);
                string[] before = GetLoadedEngineModulePaths();
                JObject allModels = Utils.GetAllModels();
                string[] after = GetLoadedEngineModulePaths();
                EnsureNativeJsonSuccess(allModels, "获取多模块模型列表");
                if (!before.SequenceEqual(after, StringComparer.OrdinalIgnoreCase))
                    throw new Exception("模型列表查询额外加载了推理模块");
                JArray modules = allModels["modules"] as JArray;
                if (modules == null || modules.Count < 2)
                    throw new Exception("模型列表未保留两个模块快照");
                foreach (JObject module in modules.OfType<JObject>())
                {
                    if (module["code"]?.Type != JTokenType.Integer || module["code"].Value<int>() != 0 ||
                        module["message"]?.Type != JTokenType.String ||
                        module["provider"]?.Type != JTokenType.String ||
                        module["module_path"]?.Type != JTokenType.String || !(module["models"] is JArray))
                        throw new Exception("多模块快照字段不完整");
                }
                int sentinelMatches = CountSnapshotResource(modules, sentinelLoader.LoadedNativeModulePath, sentinelIndex);
                int virboxMatches = CountSnapshotResource(modules, virboxLoader.LoadedNativeModulePath, virboxIndex);
                if (sentinelMatches != 1 || virboxMatches != 1)
                    throw new Exception("相同编号的多模块资源被合并或遗漏");
                Utils.FreeAllModels();
                if (sentinelLoader.GetIndexType(sentinelIndex) != 0 || virboxLoader.GetIndexType(virboxIndex) != 0)
                    throw new Exception("C# FreeAllModels 未清理全部已加载模块");
                if (!ReferenceEquals(DllLoader.Instance, defaultLoader))
                    throw new Exception("多模块操作改变了默认 DLL");
                Console.WriteLine("C# 多模块列表与全量释放检查通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("C# 多模块列表与全量释放检查失败：" + ex);
                return 1;
            }
            finally
            {
                try { Utils.FreeAllModels(); } catch { }
            }
        }

        private static int CountSnapshotResource(JArray modules, string modulePath, int index)
        {
            int count = 0;
            foreach (JObject module in modules.OfType<JObject>())
            {
                if (!string.Equals((string)module["module_path"], modulePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                JArray models = module["models"] as JArray;
                if (models == null) continue;
                count += models.OfType<JObject>().Count(item => item["model_index"]?.Value<int>() == index);
            }
            return count;
        }

        private static string[] GetLoadedEngineModulePaths()
        {
            return DllLoader.GetLoadedLoaders()
                .Select(loader => loader.LoadedNativeModulePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static DllLoader LoadExplicitTestModule(string fileName)
        {
            string directory = Path.GetDirectoryName(DllLoader.Instance.LoadedNativeModulePath);
            string fullPath = Path.GetFullPath(Path.Combine(directory, fileName));
            if (!File.Exists(fullPath)) throw new FileNotFoundException("测试 DLL 不存在", fullPath);
            DllLoader loader = DllLoader.GetLoadedLoaders().SingleOrDefault(candidate =>
                string.Equals(candidate.LoadedNativeModulePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (loader == null)
            {
                IntPtr module = LoadNativeModule(fullPath);
                if (module == IntPtr.Zero)
                    throw new Exception("无法加载测试 DLL：" + fullPath + "，Win32错误=" + Marshal.GetLastWin32Error());
                if (!string.Equals(GetActualNativeModulePath(module), fullPath, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("测试 DLL 实际路径与指定路径不一致");
                loader = DllLoader.GetLoadedLoaders().Single(candidate =>
                    string.Equals(candidate.LoadedNativeModulePath, fullPath, StringComparison.OrdinalIgnoreCase));
            }
            return loader;
        }

        private static int LoadNativeTestModel(DllLoader loader, string modelPath)
        {
            if (loader.dlcv_load_model == null || loader.dlcv_free_result == null)
                throw new NotSupportedException("测试 DLL 缺少加载或结果释放接口");
            var config = new JObject { ["model_path"] = modelPath, ["device_id"] = GpuDeviceId };
            string json = JsonConvert.SerializeObject(config,
                new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii });
            IntPtr result = loader.dlcv_load_model(json);
            if (result == IntPtr.Zero) throw new Exception("指定 DLL 加载模型返回空结果");
            try
            {
                JObject response = JObject.Parse(Marshal.PtrToStringAnsi(result));
                int? index = response.Value<int?>("model_index");
                if (!index.HasValue || index.Value < 0 || loader.GetIndexType(index.Value) != 1)
                    throw new Exception("指定 DLL 未创建有效模型：" + response.ToString(Formatting.None));
                return index.Value;
            }
            finally
            {
                loader.dlcv_free_result(result);
            }
        }

        private static bool ValidatePreservedSegmentationResult(Utils.CSharpObjectResult obj, Mat image, string pathName)
        {
            if (!obj.WithMask || obj.Mask == null || obj.Mask.Empty())
            {
                Console.WriteLine($"自测失败：{pathName} 路径在 OCR 后没有保留分割 mask");
                return false;
            }
            if (!obj.WithBbox || obj.Bbox == null || obj.Bbox.Count < 4)
            {
                Console.WriteLine($"自测失败：{pathName} 路径在 OCR 后没有保留分割 bbox");
                return false;
            }

            double x = obj.Bbox[0];
            double y = obj.Bbox[1];
            double width = obj.Bbox[2];
            double height = obj.Bbox[3];
            const double tolerance = 1.0;
            if (double.IsNaN(x) || double.IsInfinity(x)
                || double.IsNaN(y) || double.IsInfinity(y)
                || double.IsNaN(width) || double.IsInfinity(width)
                || double.IsNaN(height) || double.IsInfinity(height)
                || width <= 0.0 || height <= 100.0
                || x < -tolerance || y < -tolerance
                || x + width > image.Width + tolerance
                || y + height > image.Height + tolerance)
            {
                Console.WriteLine($"自测失败：{pathName} 路径 bbox 不在原图坐标系，image={image.Width}x{image.Height}, bbox={string.Join(",", obj.Bbox)}");
                return false;
            }
            if (string.IsNullOrWhiteSpace(obj.CategoryName))
            {
                Console.WriteLine($"自测失败：{pathName} 路径没有合并 OCR 文字");
                return false;
            }
            if (float.IsNaN(obj.Score) || float.IsInfinity(obj.Score))
            {
                Console.WriteLine($"自测失败：{pathName} 路径分割 score 非有限值");
                return false;
            }

            string maskSpace = obj.Mask.Width == image.Width && obj.Mask.Height == image.Height
                ? "full-image"
                : "roi";
            Console.WriteLine($"{pathName}: image={image.Width}x{image.Height}, bbox={string.Join(",", obj.Bbox)}, mask={obj.Mask.Width}x{obj.Mask.Height}, mask_space={maskSpace}, category_name={obj.CategoryName}, score={obj.Score:F4}");
            return true;
        }

        private static int RunDvsRgbSelfTest(string[] args)
        {
            if (args == null || args.Length < 3)
            {
                Console.WriteLine("用法: DlcvCSharpTest dvs-rgb-selftest <modelPath> <imagePath> [require-preserved-mask]");
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

                if (wrappedResult.SampleResults == null
                    || wrappedResult.SampleResults.Count == 0
                    || wrappedResult.SampleResults[0].Results == null
                    || wrappedResult.SampleResults[0].Results.Count == 0
                    || directResult.SampleResults == null
                    || directResult.SampleResults.Count == 0
                    || directResult.SampleResults[0].Results == null
                    || directResult.SampleResults[0].Results.Count == 0)
                {
                    Console.WriteLine("自测失败：DVS 流程返回空结果，不能仅凭两条路径相等判定通过");
                    return 1;
                }

                if (!string.Equals(wrappedSig, directSig, StringComparison.Ordinal))
                {
                    Console.WriteLine("自测失败：Model(.dvst) 与 DvsModel 直连 flow 的 RGB 结果不一致");
                    return 1;
                }

                bool requirePreservedMask = args.Length >= 4
                    && (string.Equals(args[3], "require-preserved-mask", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(args[3], "require-original-mask", StringComparison.OrdinalIgnoreCase));
                if (requirePreservedMask)
                {
                    var wrappedObject = wrappedResult.SampleResults[0].Results[0];
                    var directObject = directResult.SampleResults[0].Results[0];
                    if (!ValidatePreservedSegmentationResult(wrappedObject, rgb, "wrapped")
                        || !ValidatePreservedSegmentationResult(directObject, rgb, "direct"))
                    {
                        return 1;
                    }
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

        private static int RunCurveTextAffineSelfTest()
        {
            Console.WriteLine("==== curve_text_affine 自测 ====");

            using (var probabilityMask = new Mat(20, 30, MatType.CV_32FC1, Scalar.All(0.01)))
            {
                Cv2.Rectangle(probabilityMask, new Rect(6, 4, 18, 12), Scalar.All(0.9), -1);
                probabilityMask.Set(0, 0, 0.5f);
                JObject maskInfo = MaskRleUtils.MatToMaskInfo(probabilityMask);
                using (Mat decoded = MaskRleUtils.MaskInfoToMat(maskInfo))
                {
                    if (decoded.Empty() || Cv2.CountNonZero(decoded) != 217)
                    {
                        Console.WriteLine("概率 mask 二值化错误，前景像素数: " + (decoded.Empty() ? -1 : Cv2.CountNonZero(decoded)));
                        return 1;
                    }
                    if (decoded.At<byte>(0, 0) == 0 || decoded.At<byte>(3, 6) != 0 || decoded.At<byte>(4, 6) == 0)
                    {
                        Console.WriteLine("概率 mask 的 0.5 阈值或区域边界错误");
                        return 1;
                    }
                }
            }

            _ = new GraphExecutor(new List<Dictionary<string, object>>());
            Type moduleType = ModuleRegistry.Get("pre_process/curve_text_affine");
            if (moduleType == null)
            {
                Console.WriteLine("pre_process/curve_text_affine 未注册");
                return 1;
            }

            using (var image = new Mat(180, 360, MatType.CV_8UC3, Scalar.Black))
            using (var mask = new Mat(180, 360, MatType.CV_8UC1, Scalar.Black))
            {
                var polygon = new Point[]
                {
                    new Point(20, 72), new Point(80, 48), new Point(150, 38), new Point(230, 48), new Point(340, 78),
                    new Point(340, 126), new Point(230, 96), new Point(150, 86), new Point(80, 96), new Point(20, 120)
                };
                Cv2.FillPoly(mask, new[] { polygon }, Scalar.White);
                image.SetTo(new Scalar(240, 240, 240), mask);
                for (int x = 45; x < 330; x += 28)
                    Cv2.Line(image, new Point(x, 45), new Point(x, 125), new Scalar(20, 20, 20), 4);

                var state = new TransformationState(image.Width, image.Height);
                var images = new List<ModuleImage> { new ModuleImage(image, image, state, 0) };
                var results = new JArray
                {
                    new JObject
                    {
                        ["type"] = "local",
                        ["index"] = 0,
                        ["origin_index"] = 0,
                        ["transform"] = JObject.FromObject(state.ToDict()),
                        ["sample_results"] = new JArray
                        {
                            new JObject
                            {
                                ["bbox"] = new JArray(0, 0, image.Width, image.Height),
                                ["mask_rle"] = MaskRleUtils.MatToMaskInfo(mask),
                                ["score"] = 0.99,
                                ["category_name"] = "text"
                            }
                        }
                    }
                };
                var module = (BaseModule)Activator.CreateInstance(
                    moduleType,
                    new object[]
                    {
                        401,
                        null,
                        new Dictionary<string, object>
                        {
                            ["out_height"] = 80,
                            ["sample_step"] = 10.0,
                            ["shrink_inside"] = 1.5,
                            ["method"] = "auto"
                        },
                        null
                    });
                ModuleIO output = module.Process(images, results);
                if (output.ImageList.Count != 1 || output.ResultList.Count != 1)
                {
                    Console.WriteLine("曲线拉直输出数量错误");
                    return 1;
                }
                var affineProperty = typeof(ModuleImage).GetProperty("AffineImage");
                var affine = affineProperty != null ? affineProperty.GetValue(output.ImageList[0]) as Mat : null;
                if (affine == null || affine.Empty() || affine.Height != 80 || affine.Width < 250)
                {
                    Console.WriteLine("曲线拉直图像无效");
                    return 1;
                }

                var preview = new Preview(
                    402,
                    null,
                    new Dictionary<string, object>(),
                    null);
                ModuleIO previewOutput = preview.Process(output.ImageList, output.ResultList);
                if (previewOutput.ImageList.Count != 1
                    || ReferenceEquals(previewOutput.ImageList[0], output.ImageList[0])
                    || !ReferenceEquals(previewOutput.ImageList[0].ImageObject, affine)
                    || !ReferenceEquals(output.ImageList[0].ImageObject, image))
                {
                    Console.WriteLine("preview 未正确使用拉直图，或修改了原始 wrapper");
                    return 1;
                }

                string saveDir = Path.Combine(Path.GetTempPath(), "dlcv_curve_affine_" + Guid.NewGuid().ToString("N"));
                try
                {
                    var saveImage = new SaveImage(
                        403,
                        null,
                        new Dictionary<string, object>
                        {
                            ["save_path"] = saveDir,
                            ["suffix"] = "_affine",
                            ["format"] = "png"
                        },
                        null);
                    var saveResults = new JArray(new JObject { ["filename"] = "curve.png" });
                    saveImage.Process(output.ImageList, saveResults);
                    string savedPath = Path.Combine(saveDir, "curve_affine.png");
                    using (Mat saved = Cv2.ImRead(savedPath, ImreadModes.Unchanged))
                    {
                        if (saved.Empty() || saved.Width != affine.Width || saved.Height != affine.Height)
                        {
                            Console.WriteLine("save_image 未默认保存拉直图");
                            return 1;
                        }
                    }
                }
                finally
                {
                    try { if (Directory.Exists(saveDir)) Directory.Delete(saveDir, true); } catch { }
                }
            }

            Type visualizeType = ModuleRegistry.Get("output/visualize");
            if (visualizeType == null)
            {
                Console.WriteLine("output/visualize 未注册");
                return 1;
            }
            using (var original = new Mat(80, 100, MatType.CV_8UC3, Scalar.Black))
            using (var fullImageMask = new Mat(80, 100, MatType.CV_8UC1, Scalar.Black))
            {
                Cv2.Rectangle(fullImageMask, new Rect(10, 15, 12, 8), Scalar.White, -1);
                var state = new TransformationState(original.Width, original.Height);
                var visualizeImages = new List<ModuleImage> { new ModuleImage(original, original, state, 0) };
                var visualizeResults = new JArray
                {
                    new JObject
                    {
                        ["type"] = "local",
                        ["index"] = 0,
                        ["origin_index"] = 0,
                        ["transform"] = JObject.FromObject(state.ToDict()),
                        ["sample_results"] = new JArray
                        {
                            new JObject
                            {
                                ["bbox"] = new JArray(10, 15, 12, 8),
                                ["mask_rle"] = MaskRleUtils.MatToMaskInfo(fullImageMask),
                                ["with_mask"] = true,
                                ["category_name"] = "ocr-text",
                                ["score"] = 0.99
                            }
                        }
                    }
                };
                var visualize = (BaseModule)Activator.CreateInstance(
                    visualizeType,
                    new object[]
                    {
                        404,
                        null,
                        new Dictionary<string, object>
                        {
                            ["black_background"] = true,
                            ["display_mask"] = true,
                            ["display_contours"] = false,
                            ["display_bbox"] = false,
                            ["display_text"] = false
                        },
                        null
                    });
                ModuleIO visualizeOutput = visualize.Process(visualizeImages, visualizeResults);
                if (visualizeOutput.ImageList.Count != 1 || visualizeOutput.ImageList[0].ImageObject.Empty())
                {
                    Console.WriteLine("原图 mask 可视化没有输出图像");
                    return 1;
                }
                Mat visualized = visualizeOutput.ImageList[0].ImageObject;
                Vec3b expectedPixel = visualized.At<Vec3b>(18, 14);
                Vec3b doubleOffsetPixel = visualized.At<Vec3b>(33, 24);
                if (expectedPixel.Item0 == 0 && expectedPixel.Item1 == 0 && expectedPixel.Item2 == 0)
                {
                    Console.WriteLine("完整原图尺寸 mask 未绘制在原始坐标");
                    visualized.Dispose();
                    return 1;
                }
                if (doubleOffsetPixel.Item0 != 0 || doubleOffsetPixel.Item1 != 0 || doubleOffsetPixel.Item2 != 0)
                {
                    Console.WriteLine("完整原图尺寸 mask 被重复叠加 bbox 偏移");
                    visualized.Dispose();
                    return 1;
                }
                visualized.Dispose();
            }

            Console.WriteLine("curve_text_affine 自测通过");
            return 0;
        }

        private static int RunAiOrientationAffineSelfTest()
        {
            Console.WriteLine("==== AI 方向矫正 AffineImage 自测 ====");

            MethodInfo prepareMethod = typeof(ClsModel).GetMethod(
                "PrepareInferenceImages",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (prepareMethod == null)
            {
                Console.WriteLine("ClsModel 未提供 AffineImage 推理输入准备逻辑");
                return 1;
            }

            using (var sourceImage = new Mat(4, 6, MatType.CV_8UC1, Scalar.All(10)))
            using (var affineImage = new Mat(2, 3, MatType.CV_8UC1))
            {
                affineImage.Set(0, 0, (byte)1);
                affineImage.Set(0, 1, (byte)2);
                affineImage.Set(0, 2, (byte)3);
                affineImage.Set(1, 0, (byte)4);
                affineImage.Set(1, 1, (byte)5);
                affineImage.Set(1, 2, (byte)6);

                var state = new TransformationState(
                    20,
                    10,
                    new[] { 3, 2, sourceImage.Width, sourceImage.Height },
                    new[] { 1.0, 0.0, -3.0, 0.0, 1.0, -2.0 },
                    new[] { sourceImage.Width, sourceImage.Height });
                var source = new ModuleImage(sourceImage, sourceImage, state, 7)
                {
                    AffineImage = affineImage,
                    UniqueId = "orientation-affine-test",
                    SlidingMeta = new JObject { ["tile"] = 1 }
                };
                var sourceImages = new List<ModuleImage> { source };

                object[] prepareArgs = { sourceImages, true, null };
                var inferImages = prepareMethod.Invoke(null, prepareArgs) as List<ModuleImage>;
                var sourceByInferImage = prepareArgs[2] as Dictionary<ModuleImage, ModuleImage>;
                if (inferImages == null
                    || inferImages.Count != 1
                    || inferImages[0] == null
                    || !ReferenceEquals(inferImages[0].ImageObject, affineImage)
                    || sourceByInferImage == null
                    || !sourceByInferImage.TryGetValue(inferImages[0], out ModuleImage mappedSource)
                    || !ReferenceEquals(mappedSource, source))
                {
                    Console.WriteLine("ClsModel 未使用 AffineImage 作为推理输入，或未保留原 ModuleImage 映射");
                    return 1;
                }

                object[] disabledPrepareArgs = { sourceImages, false, null };
                var disabledInferImages = prepareMethod.Invoke(null, disabledPrepareArgs) as List<ModuleImage>;
                if (!ReferenceEquals(disabledInferImages, sourceImages) || disabledPrepareArgs[2] != null)
                {
                    Console.WriteLine("use_affine_img=false 时未保持原推理输入");
                    return 1;
                }

                var originalResults = new JArray
                {
                    new JObject
                    {
                        ["type"] = "local",
                        ["index"] = 0,
                        ["origin_index"] = source.OriginalIndex,
                        ["transform"] = JObject.FromObject(state.ToDict()),
                        ["sample_results"] = new JArray
                        {
                            new JObject
                            {
                                ["bbox"] = new JArray(1, 1, 2, 2),
                                ["category_name"] = "text",
                                ["score"] = 0.99
                            }
                        }
                    }
                };
                var resultsSnapshot = (JArray)originalResults.DeepClone();
                var clsResults = new JArray
                {
                    new JObject
                    {
                        ["type"] = "local",
                        ["index"] = 0,
                        ["origin_index"] = source.OriginalIndex,
                        ["transform"] = JObject.FromObject(state.ToDict()),
                        ["sample_results"] = new JArray
                        {
                            new JObject
                            {
                                ["category_name"] = "90",
                                ["score"] = 1.0
                            }
                        }
                    }
                };

                var rotateModule = new ImageRotateByClassification(
                    501,
                    null,
                    new Dictionary<string, object>
                    {
                        ["rotate90_labels"] = new[] { "90" },
                        ["rotate180_labels"] = new[] { "180" },
                        ["rotate270_labels"] = new[] { "270" },
                        ["rotate_affine_img"] = true
                    },
                    null);
                rotateModule.ExtraInputsIn.Add(new ModuleChannel(new List<ModuleImage>(), clsResults));

                ModuleIO output = rotateModule.Process(sourceImages, originalResults);
                if (output.ImageList.Count != 1 || output.ImageList[0] == null)
                {
                    Console.WriteLine("方向矫正没有输出图像");
                    return 1;
                }

                ModuleImage rotatedWrap = output.ImageList[0];
                if (!ReferenceEquals(rotatedWrap.ImageObject, sourceImage))
                {
                    Console.WriteLine("rotate_affine_img=true 时错误旋转了 ImageObject");
                    return 1;
                }
                if (!JToken.DeepEquals(
                    JObject.FromObject(rotatedWrap.TransformState.ToDict()),
                    JObject.FromObject(state.ToDict())))
                {
                    Console.WriteLine("rotate_affine_img=true 时错误修改了 TransformationState");
                    return 1;
                }
                if (!JToken.DeepEquals(output.ResultList, resultsSnapshot))
                {
                    Console.WriteLine("rotate_affine_img=true 时错误修改了原分割结果");
                    return 1;
                }

                using (var expectedAffine = new Mat())
                {
                    Cv2.Rotate(affineImage, expectedAffine, RotateFlags.Rotate90Counterclockwise);
                    if (!MatContentEquals(rotatedWrap.AffineImage, expectedAffine))
                    {
                        Console.WriteLine("AffineImage 未按分类结果逆时针旋转 90 度");
                        return 1;
                    }
                }

                var zeroClsResults = (JArray)clsResults.DeepClone();
                zeroClsResults[0]["sample_results"][0]["category_name"] = "0";
                var zeroModule = new ImageRotateByClassification(
                    502,
                    null,
                    new Dictionary<string, object>
                    {
                        ["rotate90_labels"] = new[] { "90" },
                        ["rotate180_labels"] = new[] { "180" },
                        ["rotate270_labels"] = new[] { "270" },
                        ["rotate_affine_img"] = true
                    },
                    null);
                zeroModule.ExtraInputsIn.Add(new ModuleChannel(new List<ModuleImage>(), zeroClsResults));
                ModuleIO zeroOutput = zeroModule.Process(sourceImages, originalResults);
                if (zeroOutput.ImageList.Count != 1
                    || !ReferenceEquals(zeroOutput.ImageList[0], source)
                    || !ReferenceEquals(zeroOutput.ImageList[0].AffineImage, affineImage)
                    || !JToken.DeepEquals(zeroOutput.ResultList, resultsSnapshot))
                {
                    Console.WriteLine("0 度方向矫正未保持 ImageObject、AffineImage 或原结果");
                    return 1;
                }

                var fallback = new ModuleImage(sourceImage, sourceImage, state.Clone(), source.OriginalIndex);
                var fallbackModule = new ImageRotateByClassification(
                    503,
                    null,
                    new Dictionary<string, object>
                    {
                        ["rotate90_labels"] = new[] { "90" },
                        ["rotate180_labels"] = new[] { "180" },
                        ["rotate270_labels"] = new[] { "270" },
                        ["rotate_affine_img"] = true
                    },
                    null);
                fallbackModule.ExtraInputsIn.Add(new ModuleChannel(new List<ModuleImage>(), clsResults));
                ModuleIO fallbackOutput = fallbackModule.Process(new List<ModuleImage> { fallback }, originalResults);
                if (fallbackOutput.ImageList.Count != 1
                    || fallbackOutput.ImageList[0].ImageObject.Width != sourceImage.Height
                    || fallbackOutput.ImageList[0].ImageObject.Height != sourceImage.Width
                    || JToken.DeepEquals(
                        JObject.FromObject(fallbackOutput.ImageList[0].TransformState.ToDict()),
                        JObject.FromObject(state.ToDict()))
                    || JToken.DeepEquals(fallbackOutput.ResultList, resultsSnapshot))
                {
                    Console.WriteLine("缺少 AffineImage 时未保持原 ImageObject 旋转回退逻辑");
                    fallbackOutput.ImageList[0].ImageObject.Dispose();
                    return 1;
                }
                fallbackOutput.ImageList[0].ImageObject.Dispose();
            }

            Console.WriteLine("AI 方向矫正 AffineImage 自测通过");
            return 0;
        }

        private static bool MatContentEquals(Mat left, Mat right)
        {
            if (left == null || right == null || left.Empty() || right.Empty()) return false;
            if (left.Width != right.Width || left.Height != right.Height || left.Type() != right.Type()) return false;
            using (var diff = new Mat())
            {
                Cv2.Absdiff(left, right, diff);
                return Cv2.CountNonZero(diff.Reshape(1)) == 0;
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

        private static int RunBBoxIoUDedupSelfTest()
        {
            Console.WriteLine("==== BBOX IoU 去重自测 ====");

            using (var image = new Mat(320, 320, MatType.CV_8UC3, new Scalar(0, 255, 0)))
            {
                var state = new TransformationState(image.Width, image.Height);
                var moduleImage = new ModuleImage(image, image, state, 0);
                var images = new List<ModuleImage> { moduleImage };

                var defaultModule = new BBoxIoUDedup(
                    101,
                    properties: new Dictionary<string, object>
                    {
                        ["iou_threshold"] = 0.5,
                        ["per_category"] = true
                    });
                ModuleIO defaultOutput = defaultModule.Process(images, BuildBBoxDedupResults());
                int defaultCount = CountBBoxDedupDetections(defaultOutput.ResultList);
                if (defaultCount != 1)
                {
                    Console.WriteLine("默认 cross_model=true 应跨 index 去重，实际保留数量: " + defaultCount);
                    return 1;
                }

                var strictModule = new BBoxIoUDedup(
                    102,
                    properties: new Dictionary<string, object>
                    {
                        ["iou_threshold"] = 0.5,
                        ["per_category"] = true,
                        ["cross_model"] = false
                    });
                ModuleIO strictOutput = strictModule.Process(images, BuildBBoxDedupResults());
                int strictCount = CountBBoxDedupDetections(strictOutput.ResultList);
                if (strictCount != 2)
                {
                    Console.WriteLine("cross_model=false 应恢复严格 index 分组，实际保留数量: " + strictCount);
                    return 1;
                }
            }

            Console.WriteLine("BBOX IoU 去重自测通过");
            return 0;
        }

        private static int RunCategoryCountCheckSelfTest()
        {
            Console.WriteLine("==== 类型数量校验自测 ====");

            _ = new GraphExecutor(new List<Dictionary<string, object>>());
            Type moduleType = ModuleRegistry.Get("post_process/category_count_check");
            if (moduleType == null)
            {
                Console.WriteLine("post_process/category_count_check 未注册");
                return 1;
            }

            using (var image = new Mat(100, 120, MatType.CV_8UC3, Scalar.Black))
            {
                var images = new List<ModuleImage>
                {
                    new ModuleImage(image, image, new TransformationState(120, 100), 0)
                };

                if (!VerifyCategoryCountCase(
                    moduleType,
                    new JArray
                    {
                        new JObject
                        {
                            ["category"] = "黑块",
                            ["operator"] = "equal",
                            ["expect"] = 2
                        }
                    },
                    BuildCategoryCountResults("local", "黑块", "黑块", "其他"),
                    true,
                    null))
                {
                    return 1;
                }

                if (!VerifyCategoryCountCase(
                    moduleType,
                    "[{\"category\":\"黑块\",\"operator\":\"equal\",\"expect\":2}]",
                    BuildCategoryCountResults("local", "黑块"),
                    false,
                    "类别黑块期望=2,实际1"))
                {
                    return 1;
                }

                if (!VerifyCategoryCountCase(
                    moduleType,
                    new JArray
                    {
                        new JObject
                        {
                            ["category"] = "",
                            ["operator"] = "gt",
                            ["expect"] = 2
                        },
                        new JObject
                        {
                            ["category"] = "黑块",
                            ["operator"] = "lt",
                            ["expect"] = 3
                        }
                    },
                    BuildCategoryCountResults("local", "黑块", "黑块", "其他"),
                    true,
                    null))
                {
                    return 1;
                }

                var groupedResults = new JArray
                {
                    BuildCategoryCountEntry(0, 0, "黑块", "黑块", "黑块", "黑块"),
                    BuildCategoryCountEntry(1, 0, "黑块", "黑块", "黑块", "黑块")
                };
                var groupedModule = (BaseModule)Activator.CreateInstance(
                    moduleType,
                    new object[]
                    {
                        209,
                        null,
                        new Dictionary<string, object>
                        {
                            ["rules"] = new JArray
                            {
                                new JObject
                                {
                                    ["category"] = "黑块",
                                    ["operator"] = "equal",
                                    ["expect"] = 8
                                }
                            }
                        },
                        null
                    });
                ModuleIO groupedOutput = groupedModule.Process(images, groupedResults);
                if (!Convert.ToBoolean(groupedModule.ScalarOutputsByName["ok"]) ||
                    groupedOutput.ResultList.Any(x => x["ok"] == null || !x["ok"].Value<bool>()))
                {
                    Console.WriteLine("同一 origin_index 的多个 local entry 未先汇总计数");
                    return 1;
                }

                var stickyResults = BuildCategoryCountResults("local", "黑块");
                var stickyEntry = (JObject)stickyResults[0];
                stickyEntry["ok"] = false;
                stickyEntry["reason"] = new JArray("上游失败");
                if (!VerifyCategoryCountCase(
                    moduleType,
                    new JArray
                    {
                        new JObject
                        {
                            ["category"] = "黑块",
                            ["operator"] = "equal",
                            ["expect"] = 1
                        }
                    },
                    stickyResults,
                    false,
                    "上游失败"))
                {
                    return 1;
                }

                var nonLocalResults = BuildCategoryCountResults("global", "黑块");
                var nonLocalModule = (BaseModule)Activator.CreateInstance(
                    moduleType,
                    new object[]
                    {
                        205,
                        null,
                        new Dictionary<string, object>
                        {
                            ["rules"] = new JArray
                            {
                                new JObject
                                {
                                    ["category"] = "黑块",
                                    ["operator"] = "equal",
                                    ["expect"] = 1
                                }
                            }
                        },
                        null
                    });
                ModuleIO nonLocalOutput = nonLocalModule.Process(images, nonLocalResults);
                var nonLocalEntry = nonLocalOutput.ResultList[0] as JObject;
                if (nonLocalEntry == null || nonLocalEntry.ContainsKey("ok") || nonLocalEntry.ContainsKey("reason"))
                {
                    Console.WriteLine("非 local 结果不应注入 ok/reason");
                    return 1;
                }

                var context = new DlcvModules.ExecutionContext();
                var ngModule = (BaseModule)Activator.CreateInstance(
                    moduleType,
                    new object[]
                    {
                        206,
                        null,
                        new Dictionary<string, object>
                        {
                            ["rules"] = new JArray
                            {
                                new JObject
                                {
                                    ["category"] = "黑块",
                                    ["operator"] = "equal",
                                    ["expect"] = 2
                                }
                            }
                        },
                        context
                    });
                ModuleIO checkedOutput = ngModule.Process(images, BuildCategoryCountResults("local", "黑块"));
                var returnJson = new ReturnJson(207, properties: new Dictionary<string, object>(), context: context);
                returnJson.Process(images, checkedOutput.ResultList);

                var frontendJson = context.Get<Dictionary<string, object>>("frontend_json", null);
                var last = frontendJson != null && frontendJson.ContainsKey("last")
                    ? frontendJson["last"] as Dictionary<string, object>
                    : null;
                var byImage = last != null && last.ContainsKey("by_image")
                    ? last["by_image"] as List<Dictionary<string, object>>
                    : null;
                if (byImage == null || byImage.Count != 1 ||
                    !byImage[0].ContainsKey("ok") || Convert.ToBoolean(byImage[0]["ok"]) ||
                    !byImage[0].ContainsKey("reason"))
                {
                    Console.WriteLine("ReturnJson 未透传 ok/reason");
                    return 1;
                }

                var legacyContext = new DlcvModules.ExecutionContext();
                var legacyReturnJson = new ReturnJson(208, properties: new Dictionary<string, object>(), context: legacyContext);
                legacyReturnJson.Process(images, BuildCategoryCountResults("local", "黑块"));
                var legacyFrontendJson = legacyContext.Get<Dictionary<string, object>>("frontend_json", null);
                var legacyLast = legacyFrontendJson != null && legacyFrontendJson.ContainsKey("last")
                    ? legacyFrontendJson["last"] as Dictionary<string, object>
                    : null;
                var legacyByImage = legacyLast != null && legacyLast.ContainsKey("by_image")
                    ? legacyLast["by_image"] as List<Dictionary<string, object>>
                    : null;
                if (legacyByImage == null || legacyByImage.Count != 1 ||
                    legacyByImage[0].ContainsKey("ok") || legacyByImage[0].ContainsKey("reason"))
                {
                    Console.WriteLine("旧结果不应凭空增加 ok/reason");
                    return 1;
                }

                var transformOnlyContext = new DlcvModules.ExecutionContext();
                var transformOnlyReturnJson = new ReturnJson(210, properties: new Dictionary<string, object>(), context: transformOnlyContext);
                var transformState = new TransformationState(
                    120,
                    100,
                    null,
                    new double[] { 1, 0, 0, 0, 1, 0 },
                    new int[] { 120, 100 });
                var transformOnlyImages = new List<ModuleImage>
                {
                    new ModuleImage(image, image, transformState, 0)
                };
                var transform = JObject.FromObject(transformState.ToDict());
                var transformOnlyResults = new JArray
                {
                    new JObject
                    {
                        ["type"] = "local",
                        ["index"] = -1,
                        ["origin_index"] = -1,
                        ["transform"] = transform.DeepClone(),
                        ["sample_results"] = BuildCategoryCountEntry(0, 0, "黑块")["sample_results"].DeepClone(),
                        ["ok"] = false,
                        ["reason"] = new JArray("旧流程失败")
                    },
                    new JObject
                    {
                        ["type"] = "local",
                        ["index"] = -1,
                        ["origin_index"] = -1,
                        ["transform"] = transform.DeepClone(),
                        ["sample_results"] = new JArray(),
                        ["ok"] = true
                    }
                };
                transformOnlyReturnJson.Process(transformOnlyImages, transformOnlyResults);
                var transformOnlyFrontendJson = transformOnlyContext.Get<Dictionary<string, object>>("frontend_json", null);
                var transformOnlyLast = transformOnlyFrontendJson != null && transformOnlyFrontendJson.ContainsKey("last")
                    ? transformOnlyFrontendJson["last"] as Dictionary<string, object>
                    : null;
                var transformOnlyByImage = transformOnlyLast != null && transformOnlyLast.ContainsKey("by_image")
                    ? transformOnlyLast["by_image"] as List<Dictionary<string, object>>
                    : null;
                var transformOnlyReasons = transformOnlyByImage != null && transformOnlyByImage.Count == 1 &&
                    transformOnlyByImage[0].ContainsKey("reason")
                    ? transformOnlyByImage[0]["reason"] as JArray
                    : null;
                if (transformOnlyByImage == null || transformOnlyByImage.Count != 1 ||
                    !transformOnlyByImage[0].ContainsKey("ok") || Convert.ToBoolean(transformOnlyByImage[0]["ok"]) ||
                    transformOnlyReasons == null || !transformOnlyReasons.Any(x => x.ToString() == "旧流程失败"))
                {
                    Console.WriteLine("旧流程通过 transform 对齐时丢失或覆盖 ok/reason");
                    return 1;
                }
            }

            Console.WriteLine("类型数量校验自测通过");
            return 0;
        }

        private static bool VerifyCategoryCountCase(
            Type moduleType,
            object rules,
            JArray results,
            bool expectedOk,
            string expectedReason)
        {
            var module = (BaseModule)Activator.CreateInstance(
                moduleType,
                new object[]
                {
                    204,
                    null,
                    new Dictionary<string, object> { ["rules"] = rules },
                    null
                });
            ModuleIO output = module.Process(new List<ModuleImage>(), results);
            var entry = output.ResultList.Count > 0 ? output.ResultList[0] as JObject : null;
            bool scalarOk = module.ScalarOutputsByName.ContainsKey("ok") &&
                Convert.ToBoolean(module.ScalarOutputsByName["ok"]);
            string scalarReason = module.ScalarOutputsByName.ContainsKey("reason")
                ? Convert.ToString(module.ScalarOutputsByName["reason"])
                : null;
            bool entryOk = entry != null && entry["ok"] != null && entry["ok"].Value<bool>();
            var reasons = entry != null ? entry["reason"] as JArray : null;

            bool reasonMatches = expectedReason == null
                ? reasons == null && string.IsNullOrEmpty(scalarReason)
                : reasons != null && reasons.Any(x => string.Equals(x.ToString(), expectedReason, StringComparison.Ordinal)) &&
                  scalarReason != null && scalarReason.Contains(expectedReason);

            if (entry == null || entryOk != expectedOk || scalarOk != expectedOk || !reasonMatches)
            {
                Console.WriteLine(
                    "类型数量校验结果不一致: entry_ok=" + entryOk +
                    ", scalar_ok=" + scalarOk +
                    ", reason=" + scalarReason);
                return false;
            }
            return true;
        }

        private static JObject BuildCategoryCountEntry(int index, int originIndex, params string[] categories)
        {
            var detections = new JArray();
            for (int i = 0; i < categories.Length; i++)
            {
                detections.Add(new JObject
                {
                    ["category_id"] = i,
                    ["category_name"] = categories[i],
                    ["score"] = 0.9,
                    ["bbox"] = new JArray(10 + i, 10, 20 + i, 20)
                });
            }
            return new JObject
            {
                ["type"] = "local",
                ["index"] = index,
                ["origin_index"] = originIndex,
                ["sample_results"] = detections
            };
        }

        private static JArray BuildCategoryCountResults(string type, params string[] categories)
        {
            var detections = new JArray();
            for (int i = 0; i < categories.Length; i++)
            {
                detections.Add(new JObject
                {
                    ["category_id"] = i,
                    ["category_name"] = categories[i],
                    ["score"] = 0.9,
                    ["bbox"] = new JArray(10 + i, 10, 20 + i, 20)
                });
            }

            return new JArray
            {
                new JObject
                {
                    ["type"] = type,
                    ["index"] = 0,
                    ["origin_index"] = 0,
                    ["sample_results"] = detections
                }
            };
        }

        private static int RunCountResultsSelfTest()
        {
            Console.WriteLine("==== 统计结果个数自测 ====");

            _ = new GraphExecutor(new List<Dictionary<string, object>>());
            Type moduleType = ModuleRegistry.Get("post_process/count_results");
            if (moduleType == null)
            {
                Console.WriteLine("post_process/count_results 未注册");
                return 1;
            }

            var cases = new[]
            {
                Tuple.Create(new Dictionary<string, object>(), 1, true),
                Tuple.Create(new Dictionary<string, object> { ["min_count"] = 2, ["max_count"] = 4 }, 2, true),
                Tuple.Create(new Dictionary<string, object> { ["min_count"] = 2, ["max_count"] = 4 }, 4, true),
                Tuple.Create(new Dictionary<string, object> { ["min_count"] = 2, ["max_count"] = 2 }, 2, true),
                Tuple.Create(new Dictionary<string, object> { ["min_count"] = 2, ["max_count"] = 4 }, 1, false),
                Tuple.Create(new Dictionary<string, object> { ["count_type"] = "equal", ["only_count"] = 2 }, 2, true),
                Tuple.Create(new Dictionary<string, object> { ["count_type"] = "greater", ["min_count"] = 2 }, 2, false),
                Tuple.Create(new Dictionary<string, object> { ["count_type"] = "greater", ["min_count"] = 2 }, 3, true),
                Tuple.Create(new Dictionary<string, object> { ["count_type"] = "less", ["max_count"] = 2 }, 2, false),
                Tuple.Create(new Dictionary<string, object> { ["count_type"] = "less", ["max_count"] = 2 }, 1, true),
                Tuple.Create(new Dictionary<string, object> { ["count_type"] = "legacy_unknown", ["min_count"] = 2 }, 2, true),
                Tuple.Create(new Dictionary<string, object> { ["only_count"] = 99, ["min_count"] = 2 }, 3, true)
            };

            foreach (var testCase in cases)
            {
                var properties = new Dictionary<string, object>(testCase.Item1)
                {
                    ["only_local"] = true
                };
                var module = (BaseModule)Activator.CreateInstance(
                    moduleType,
                    new object[] { 200, null, properties, null });
                ModuleIO output = module.Process(new List<ModuleImage>(), BuildCountResults(testCase.Item2));
                int actualCount = Convert.ToInt32(module.ScalarOutputsByName["count"]);
                bool actualOk = Convert.ToBoolean(module.ScalarOutputsByName["ok"]);
                if (actualCount != testCase.Item2 || actualOk != testCase.Item3)
                {
                    Console.WriteLine(
                        "计数结果不一致: count=" + actualCount +
                        ", ok=" + actualOk +
                        ", expected_count=" + testCase.Item2 +
                        ", expected_ok=" + testCase.Item3);
                    return 1;
                }
                if (output.ResultList.Count != 1 || module.ExtraOutputs.Count != 2)
                {
                    Console.WriteLine("主路或额外输出数量不正确");
                    return 1;
                }
                int passCount = CountCountResultsDetections(module.ExtraOutputs[0].ResultList);
                int failCount = CountCountResultsDetections(module.ExtraOutputs[1].ResultList);
                if ((actualOk && (passCount != testCase.Item2 || failCount != 0)) ||
                    (!actualOk && (passCount != 0 || failCount != testCase.Item2)))
                {
                    Console.WriteLine("通过/排除分流不正确");
                    return 1;
                }
            }

            try
            {
                var invalid = (BaseModule)Activator.CreateInstance(
                    moduleType,
                    new object[]
                    {
                        201,
                        null,
                        new Dictionary<string, object>
                        {
                            ["only_local"] = true,
                            ["min_count"] = 3,
                            ["max_count"] = 2
                        },
                        null
                    });
                invalid.Process(new List<ModuleImage>(), BuildCountResults(2));
                Console.WriteLine("min_count > max_count 未报错");
                return 1;
            }
            catch (ArgumentException)
            {
            }

            Console.WriteLine("统计结果个数自测通过");
            return 0;
        }

        private static JArray BuildCountResults(int count)
        {
            var detections = new JArray();
            for (int i = 0; i < count; i++)
            {
                detections.Add(new JObject { ["category_name"] = "target" });
            }
            return new JArray
            {
                new JObject
                {
                    ["type"] = "local",
                    ["index"] = 0,
                    ["origin_index"] = 0,
                    ["sample_results"] = detections
                }
            };
        }

        private static int CountCountResultsDetections(JArray results)
        {
            int count = 0;
            if (results == null) return count;
            foreach (JToken token in results)
            {
                var detections = (token as JObject)?["sample_results"] as JArray;
                if (detections != null) count += detections.Count;
            }
            return count;
        }

        private static JArray BuildBBoxDedupResults()
        {
            return new JArray
            {
                BuildBBoxDedupEntry(0, new JArray(10.0, 10.0, 100.0, 100.0)),
                BuildBBoxDedupEntry(1, new JArray(20.0, 20.0, 80.0, 80.0))
            };
        }

        private static JObject BuildBBoxDedupEntry(int index, JArray bbox)
        {
            return new JObject
            {
                ["type"] = "local",
                ["index"] = index,
                ["origin_index"] = index,
                ["sample_results"] = new JArray
                {
                    new JObject
                    {
                        ["bbox"] = bbox,
                        ["category_id"] = 1,
                        ["category_name"] = "target",
                        ["score"] = 0.9
                    }
                }
            };
        }

        private static int CountBBoxDedupDetections(JArray results)
        {
            int count = 0;
            if (results == null) return count;
            foreach (JToken token in results)
            {
                var entry = token as JObject;
                var dets = entry?["sample_results"] as JArray;
                if (dets != null) count += dets.Count;
            }
            return count;
        }

        private static int RunBBoxCropFixSelfTest()
        {
            Console.WriteLine("==== BBOX 去重与裁图修复自测 ====");

            if (!RunBBoxCropLogicRegression())
            {
                Console.WriteLine("BBOX 去重与裁图逻辑回归失败");
                return 1;
            }

            Console.WriteLine("BBOX 去重与裁图修复自测通过");
            return 0;
        }

        private static int RunImageGenerationExpandSelfTest()
        {
            Console.WriteLine("==== AI 裁图外扩参数自测 ====");
            return RunImageGenerationExpandRegression() ? 0 : 1;
        }

        private static int RunCrossModelLabelMergeSelfTest()
        {
            Console.WriteLine("==== 跨模型标签合并自测 ====");

            using (var img0 = new Mat(200, 200, MatType.CV_8UC3, new Scalar(0, 0, 0)))
            {
                var image0 = new ModuleImage(img0, img0, new TransformationState(200, 200), 0);
                image0.UniqueId = "uid-0";
                var images = new List<ModuleImage> { image0 };

                var baseResults = new JArray
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
                                ["category_name"] = "base",
                                ["score"] = 0.99
                            }
                        }
                    }
                };

                var suffixResults = new JArray
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
                                ["category_name"] = "suffix",
                                ["score"] = 0.99
                            }
                        }
                    }
                };

                var merger = new CrossModelLabelMerge(
                    301,
                    properties: new Dictionary<string, object>
                    {
                        ["fixed_text"] = "-"
                    });

                merger.ExtraInputsIn.Add(new ModuleChannel(images, suffixResults));

                ModuleIO output = merger.Process(images, baseResults);
                if (output.ResultList.Count != 1)
                {
                    Console.WriteLine("结果数量错误，实际=" + output.ResultList.Count);
                    return 1;
                }

                var entry = output.ResultList[0] as JObject;
                if (entry == null)
                {
                    Console.WriteLine("结果条目类型错误");
                    return 1;
                }

                var dets = entry["sample_results"] as JArray;
                if (dets == null || dets.Count != 1)
                {
                    Console.WriteLine("sample_results 数量错误");
                    return 1;
                }

                var det = dets[0] as JObject;
                string cat = det?["category_name"]?.Value<string>();
                if (cat != "base-suffix")
                {
                    Console.WriteLine("合并标签错误，实际=" + cat + "，期望=base-suffix");
                    return 1;
                }
            }

            Console.WriteLine("跨模型标签合并自测通过");
            return 0;
        }

        private static bool RunBBoxCropLogicRegression()
        {
            if (!RunImageGenerationExpandRegression())
            {
                return false;
            }

            using (var img0 = new Mat(320, 320, MatType.CV_8UC3, new Scalar(0, 0, 0)))
            using (var img1 = new Mat(320, 320, MatType.CV_8UC3, new Scalar(0, 0, 0)))
            {
                Cv2.Rectangle(img0, new Rect(10, 10, 10, 10), new Scalar(255, 255, 255), -1);
                Cv2.Rectangle(img1, new Rect(10, 10, 10, 10), new Scalar(255, 255, 255), -1);

                var image0 = new ModuleImage(img0, img0, new TransformationState(320, 320), 0);
                var image1 = new ModuleImage(img1, img1, new TransformationState(320, 320), 1);
                var images = new List<ModuleImage> { image0, image1 };

                var dedup = new BBoxIoUDedup(
                    201,
                    properties: new Dictionary<string, object>
                    {
                        ["metric"] = "iou",
                        ["iou_threshold"] = 0.5,
                        ["per_category"] = true,
                        ["cross_model"] = true
                    });

                ModuleIO eightToFour = dedup.Process(images, BuildEightToFourDedupResults());
                int kept = CountBBoxDedupDetections(eightToFour.ResultList);
                if (kept != 4)
                {
                    Console.WriteLine("8->4 去重失败，实际保留数量: " + kept);
                    return false;
                }

                ModuleIO dedupForCrop = dedup.Process(images, BuildDedupThenCropResults());
                int keptForCrop = CountBBoxDedupDetections(dedupForCrop.ResultList);
                if (keptForCrop != 4)
                {
                    Console.WriteLine("裁图前去重数量错误，实际保留数量: " + keptForCrop);
                    return false;
                }

                var cropper = new ImageGeneration(
                    202,
                    properties: new Dictionary<string, object>
                    {
                        ["crop_expand"] = 0,
                        ["crop_shape"] = new int[0],
                        ["min_size"] = 1
                    });

                ModuleIO cropOut = cropper.Process(dedupForCrop.ImageList, dedupForCrop.ResultList);
                if (cropOut.ImageList.Count != 4 || cropOut.ResultList.Count != 4)
                {
                    Console.WriteLine("AI 裁图应输出 4 张图和 4 条结果，实际 image="
                        + cropOut.ImageList.Count + ", result=" + cropOut.ResultList.Count);
                    return false;
                }

                var originCounts = new Dictionary<int, int>();
                foreach (JToken token in cropOut.ResultList)
                {
                    var entry = token as JObject;
                    if (entry == null) continue;
                    int origin = entry["origin_index"]?.Value<int?>() ?? -1;
                    originCounts[origin] = originCounts.ContainsKey(origin) ? originCounts[origin] + 1 : 1;
                }
                if (!originCounts.ContainsKey(0) || originCounts[0] != 1 ||
                    !originCounts.ContainsKey(1) || originCounts[1] != 3 ||
                    originCounts.Count != 2)
                {
                    Console.WriteLine("AI 裁图 origin 归属错误: " + string.Join(",", originCounts.Select(kv => kv.Key + ":" + kv.Value)));
                    return false;
                }

                var merger = new SlidingMergeResults(
                    203,
                    properties: new Dictionary<string, object>
                    {
                        ["dedup_results"] = true
                    });
                var mergeImages = new List<ModuleImage>
                {
                    new ModuleImage(img0, img0, new TransformationState(320, 320), 10),
                    new ModuleImage(img1, img1, new TransformationState(320, 320), 20)
                };
                ModuleIO mergeOut = merger.Process(mergeImages, new JArray());
                var indices = new List<int>();
                foreach (JToken token in mergeOut.ResultList)
                {
                    var entry = token as JObject;
                    if (entry != null && string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase))
                    {
                        indices.Add(entry["index"]?.Value<int?>() ?? -1);
                    }
                }
                if (indices.Count != 2 || indices[0] != 0 || indices[1] != 1)
                {
                    Console.WriteLine("滑窗合并输出 index 应为 [0,1]，实际: [" + string.Join(",", indices) + "]");
                    return false;
                }
            }

            Console.WriteLine("BBOX 去重与裁图逻辑回归通过");
            return true;
        }

        private static bool RunImageGenerationExpandRegression()
        {
            using (var img = new Mat(200, 200, MatType.CV_8UC3, new Scalar(0, 0, 0)))
            using (var img320 = new Mat(320, 320, MatType.CV_8UC3, new Scalar(0, 0, 0)))
            {
                var image = new ModuleImage(img, img, new TransformationState(200, 200), 0);
                var images = new List<ModuleImage> { image };
                var image320 = new ModuleImage(img320, img320, new TransformationState(320, 320), 0);
                var images320 = new List<ModuleImage> { image320 };
                string error;

                if (!AssertImageGenerationCrop(
                    "像素外扩",
                    images,
                    new Dictionary<string, object>
                    {
                        ["crop_expand"] = 5,
                        ["crop_shape"] = new int[0],
                        ["min_size"] = 1
                    },
                    BuildImageGenerationDet(50.0, 60.0, 40.0, 20.0),
                    50,
                    30,
                    out error))
                {
                    Console.WriteLine(error);
                    return false;
                }

                if (!AssertImageGenerationCrop(
                    "百分比外扩普通框",
                    images,
                    new Dictionary<string, object>
                    {
                        ["crop_expand"] = 0,
                        ["crop_expand_mode"] = "percent",
                        ["crop_expand_percent"] = 10,
                        ["crop_shape"] = new int[0],
                        ["min_size"] = 1
                    },
                    BuildImageGenerationDet(50.0, 60.0, 40.0, 20.0),
                    48,
                    24,
                    out error))
                {
                    Console.WriteLine(error);
                    return false;
                }

                if (!AssertImageGenerationCrop(
                    "百分比值不截断为32",
                    images,
                    new Dictionary<string, object>
                    {
                        ["crop_expand"] = 0,
                        ["crop_expand_mode"] = "percent",
                        ["crop_expand_percent"] = 50,
                        ["crop_shape"] = new int[0],
                        ["min_size"] = 1
                    },
                    BuildImageGenerationDet(50.0, 60.0, 40.0, 20.0),
                    80,
                    40,
                    out error))
                {
                    Console.WriteLine(error);
                    return false;
                }

                if (!AssertImageGenerationCrop(
                    "百分比默认像素上限",
                    images320,
                    new Dictionary<string, object>
                    {
                        ["crop_expand"] = 0,
                        ["crop_expand_mode"] = "percent",
                        ["crop_expand_percent"] = 20,
                        ["crop_shape"] = new int[0],
                        ["min_size"] = 1
                    },
                    BuildImageGenerationDet(50.0, 50.0, 200.0, 200.0),
                    264,
                    264,
                    out error))
                {
                    Console.WriteLine(error);
                    return false;
                }

                if (!AssertImageGenerationCrop(
                    "百分比自定义像素上限",
                    images320,
                    new Dictionary<string, object>
                    {
                        ["crop_expand"] = 0,
                        ["crop_expand_mode"] = "percent",
                        ["crop_expand_percent"] = 20,
                        ["crop_expand_percent_limit"] = 10,
                        ["crop_shape"] = new int[0],
                        ["min_size"] = 1
                    },
                    BuildImageGenerationDet(50.0, 50.0, 200.0, 200.0),
                    220,
                    220,
                    out error))
                {
                    Console.WriteLine(error);
                    return false;
                }

                if (!AssertImageGenerationCrop(
                    "固定尺寸优先",
                    images,
                    new Dictionary<string, object>
                    {
                        ["crop_expand"] = 5,
                        ["crop_expand_mode"] = "percent",
                        ["crop_expand_percent"] = 10,
                        ["crop_shape"] = new[] { 30, 25 },
                        ["min_size"] = 1
                    },
                    BuildImageGenerationDet(50.0, 60.0, 40.0, 20.0),
                    30,
                    25,
                    out error))
                {
                    Console.WriteLine(error);
                    return false;
                }

                if (!AssertImageGenerationCrop(
                    "百分比外扩旋转框",
                    images,
                    new Dictionary<string, object>
                    {
                        ["crop_expand"] = 0,
                        ["crop_expand_mode"] = "percent",
                        ["crop_expand_percent"] = 10,
                        ["crop_shape"] = new int[0],
                        ["min_size"] = 1
                    },
                    BuildImageGenerationDet(100.0, 100.0, 40.0, 20.0, true, 0.0),
                    48,
                    24,
                    out error))
                {
                    Console.WriteLine(error);
                    return false;
                }

                if (!AssertImageGenerationCrop(
                    "旋转框百分比默认像素上限",
                    images320,
                    new Dictionary<string, object>
                    {
                        ["crop_expand"] = 0,
                        ["crop_expand_mode"] = "percent",
                        ["crop_expand_percent"] = 20,
                        ["crop_shape"] = new int[0],
                        ["min_size"] = 1
                    },
                    BuildImageGenerationDet(160.0, 160.0, 200.0, 200.0, true, 0.0),
                    264,
                    264,
                    out error))
                {
                    Console.WriteLine(error);
                    return false;
                }
            }

            Console.WriteLine("AI 裁图外扩参数逻辑回归通过");
            return true;
        }

        private static bool AssertImageGenerationCrop(
            string caseName,
            List<ModuleImage> images,
            Dictionary<string, object> properties,
            JObject detection,
            int expectedWidth,
            int expectedHeight,
            out string error)
        {
            var cropper = new ImageGeneration(220, properties: properties);
            var resultList = new JArray
            {
                BuildBBoxCropLocalEntry(0, 0, null, detection)
            };
            ModuleIO output = cropper.Process(images, resultList);
            if (output.ImageList.Count != 1 || output.ResultList.Count != 1)
            {
                error = caseName + " 输出数量错误，image=" + output.ImageList.Count + ", result=" + output.ResultList.Count;
                return false;
            }

            Mat cropped = output.ImageList[0].ImageObject;
            int actualWidth = cropped != null ? cropped.Width : 0;
            int actualHeight = cropped != null ? cropped.Height : 0;
            if (actualWidth != expectedWidth || actualHeight != expectedHeight)
            {
                error = caseName + " 裁图尺寸错误，actual=" + actualWidth + "x" + actualHeight
                    + ", expected=" + expectedWidth + "x" + expectedHeight;
                return false;
            }

            error = null;
            return true;
        }

        private static JObject BuildImageGenerationDet(double x, double y, double w, double h, bool withAngle = false, double angle = -100.0)
        {
            var det = BuildBBoxCropDet(x, y, w, h, 0.99);
            det["with_angle"] = withAngle;
            det["angle"] = withAngle ? angle : -100.0;
            return det;
        }

        private static JArray BuildEightToFourDedupResults()
        {
            return new JArray
            {
                BuildBBoxCropLocalEntry(
                    0,
                    0,
                    BuildPythonIdentityTransform(320, 320),
                    BuildBBoxCropDet(10.0, 10.0, 100.0, 100.0, 0.99),
                    BuildBBoxCropDet(220.0, 220.0, 40.0, 40.0, 0.98),
                    BuildBBoxCropDet(70.0, 10.0, 40.0, 40.0, 0.97),
                    BuildBBoxCropDet(70.0, 70.0, 40.0, 40.0, 0.96)),
                BuildBBoxCropLocalEntry(
                    1,
                    1,
                    null,
                    BuildBBoxCropDet(20.0, 20.0, 80.0, 80.0, 0.88),
                    BuildBBoxCropDet(222.0, 222.0, 38.0, 38.0, 0.87),
                    BuildBBoxCropDet(72.0, 12.0, 38.0, 38.0, 0.86),
                    BuildBBoxCropDet(72.0, 72.0, 38.0, 38.0, 0.85))
            };
        }

        private static JArray BuildDedupThenCropResults()
        {
            return new JArray
            {
                BuildBBoxCropLocalEntry(
                    0,
                    0,
                    BuildPythonIdentityTransform(320, 320),
                    BuildBBoxCropDet(10.0, 10.0, 40.0, 40.0, 0.99)),
                BuildBBoxCropLocalEntry(
                    1,
                    1,
                    null,
                    BuildBBoxCropDet(70.0, 10.0, 40.0, 40.0, 0.98),
                    BuildBBoxCropDet(10.0, 70.0, 40.0, 40.0, 0.97),
                    BuildBBoxCropDet(70.0, 70.0, 40.0, 40.0, 0.96))
            };
        }

        private static JObject BuildBBoxCropLocalEntry(int index, int originIndex, JToken transform, params JObject[] detections)
        {
            var sampleResults = new JArray();
            if (detections != null)
            {
                foreach (var det in detections)
                {
                    if (det != null) sampleResults.Add(det);
                }
            }

            var entry = new JObject
            {
                ["type"] = "local",
                ["index"] = index,
                ["origin_index"] = originIndex,
                ["sample_results"] = sampleResults
            };
            entry["transform"] = transform != null ? transform.DeepClone() : JValue.CreateNull();
            return entry;
        }

        private static JObject BuildBBoxCropDet(double x, double y, double w, double h, double score)
        {
            return new JObject
            {
                ["category_id"] = 1,
                ["category_name"] = "元件",
                ["score"] = score,
                ["bbox"] = new JArray(x, y, w, h),
                ["with_bbox"] = true
            };
        }

        private static JObject BuildPythonIdentityTransform(int width, int height)
        {
            return new JObject
            {
                ["crop_box"] = new JArray(0, 0, width, height),
                ["affine_matrix"] = new JArray
                {
                    new JArray(1.0, 0.0, 0.0),
                    new JArray(0.0, 1.0, 0.0)
                },
                ["output_size"] = new JArray(width, height),
                ["original_size"] = new JArray(width, height)
            };
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

            MethodInfo prepareImageForModelInput = formType.GetMethod("PrepareImageForModelInput", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo runPipeline = formType.GetMethod("RunPipeline", BindingFlags.NonPublic | BindingFlags.Instance);
            if (prepareImageForModelInput == null || runPipeline == null)
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

                imageBgr = Cv2.ImRead(imagePath, ImreadModes.Unchanged);
                entryRgb = prepareImageForModelInput.Invoke(null, new object[] { imageBgr }) as Mat;
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
                    new { BaseName = "IC-BGA", Expected = true },
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

        private static string BuildStructuredResultSignature(Utils.CSharpResult result)
        {
            var samples = new JArray();
            if (result.SampleResults != null)
            {
                foreach (var sample in result.SampleResults)
                {
                    var normalizedSample = new JObject
                    {
                        ["ok"] = sample.Ok.HasValue ? new JValue(sample.Ok.Value) : JValue.CreateNull(),
                        ["reason"] = sample.Reason == null ? JValue.CreateNull() : new JValue(sample.Reason),
                        ["results"] = new JArray()
                    };
                    if (sample.Results != null)
                    {
                        foreach (var item in sample.Results)
                        {
                            ((JArray)normalizedSample["results"]).Add(new JObject
                            {
                                ["category_id"] = item.CategoryId,
                                ["category_name"] = item.CategoryName ?? string.Empty,
                                ["score"] = item.Score,
                                ["area"] = item.Area,
                                ["bbox"] = item.Bbox == null ? new JArray() : JArray.FromObject(item.Bbox),
                                ["with_mask"] = item.WithMask,
                                ["with_bbox"] = item.WithBbox,
                                ["with_angle"] = item.WithAngle,
                                ["angle"] = item.Angle,
                                ["with_mean"] = item.WithMean,
                                ["foreground_mean"] = item.ForegroundMean,
                                ["background_mean"] = item.BackgroundMean,
                                ["extra_info"] = CanonicalizeSignatureToken(item.ExtraInfo),
                                ["mask"] = BuildMaskSignature(item.Mask)
                            });
                        }
                    }
                    samples.Add(normalizedSample);
                }
            }
            return samples.ToString(Formatting.None);
        }

        private static JToken CanonicalizeSignatureToken(JToken value)
        {
            if (value == null) return JValue.CreateNull();
            var obj = value as JObject;
            if (obj != null)
            {
                var result = new JObject();
                foreach (var property in obj.Properties().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    result[property.Name] = CanonicalizeSignatureToken(property.Value);
                }
                return result;
            }
            var array = value as JArray;
            if (array != null)
            {
                var result = new JArray();
                foreach (var item in array) result.Add(CanonicalizeSignatureToken(item));
                return result;
            }
            return value.DeepClone();
        }

        private static JToken BuildMaskSignature(Mat mask)
        {
            if (mask == null || mask.Empty()) return JValue.CreateNull();
            Mat source = mask;
            Mat copy = null;
            try
            {
                if (!mask.IsContinuous())
                {
                    copy = mask.Clone();
                    source = copy;
                }
                long byteCountLong = source.Total() * source.ElemSize();
                if (byteCountLong < 0 || byteCountLong > int.MaxValue) throw new InvalidOperationException("Mask 数据过大");
                var pixels = new byte[(int)byteCountLong];
                Marshal.Copy(source.Data, pixels, 0, pixels.Length);
                return new JObject
                {
                    ["width"] = source.Width,
                    ["height"] = source.Height,
                    ["type"] = source.Type().ToString(),
                    ["pixel_count"] = pixels.Length,
                    ["pixel_hash"] = ComputePixelHash(pixels)
                };
            }
            finally
            {
                try { copy?.Dispose(); } catch { }
            }
        }

        private static string ComputePixelHash(byte[] pixels)
        {
            ulong hash = 14695981039346656037UL;
            for (int index = 0; index < pixels.Length; index++)
            {
                hash ^= pixels[index];
                hash *= 1099511628211UL;
            }
            return hash.ToString("X16", CultureInfo.InvariantCulture);
        }

        private static JToken NormalizeWorkflowJson(JToken value)
        {
            var array = value as JArray;
            if (array != null)
            {
                return new JObject
                {
                    ["sample_results"] = new JArray { new JObject { ["results"] = CanonicalizeSignatureToken(array) } }
                };
            }

            var root = value as JObject;
            if (root == null) return CanonicalizeSignatureToken(value);
            if (root["sample_results"] is JArray) return CanonicalizeSignatureToken(root);
            var resultList = root["result_list"] as JArray;
            if (resultList == null) return CanonicalizeSignatureToken(root);

            var normalized = new JObject();
            foreach (var property in root.Properties())
            {
                if (!string.Equals(property.Name, "result_list", StringComparison.Ordinal))
                {
                    normalized[property.Name] = CanonicalizeSignatureToken(property.Value);
                }
            }
            var samples = new JArray();
            bool nestedBatch = resultList.Count > 0 && resultList.All(x =>
            {
                var sample = x as JObject;
                return sample != null && sample["result_list"] is JArray;
            });
            if (nestedBatch)
            {
                foreach (var token in resultList)
                {
                    var source = (JObject)token;
                    var sample = new JObject();
                    foreach (var property in source.Properties())
                    {
                        sample[property.Name == "result_list" ? "results" : property.Name] = CanonicalizeSignatureToken(property.Value);
                    }
                    samples.Add(sample);
                }
            }
            else
            {
                samples.Add(new JObject { ["results"] = CanonicalizeSignatureToken(resultList) });
            }
            normalized["sample_results"] = samples;
            return CanonicalizeSignatureToken(normalized);
        }

        private static void UpdateConsistencySignature(ref string baseline, ref string firstDifference, ref int firstDifferenceRun, string signature, int runIndex)
        {
            if (baseline == null) baseline = signature;
            else if (firstDifference == null && !string.Equals(baseline, signature, StringComparison.Ordinal))
            {
                firstDifference = signature;
                firstDifferenceRun = runIndex;
            }
        }

        private static string ComputeTextHash(string text)
        {
            return ComputePixelHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
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


        private struct CaseRow
        {
            public string ModelName;
            public string LoadStatus;
            public string InferStatus;
            public string ResultStatus;
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
            public readonly double WorkingSetMb;
            public MemorySnapshot(double privateMb, double workingSetMb)
            {
                PrivateMb = privateMb;
                WorkingSetMb = workingSetMb;
            }
            public static MemorySnapshot Capture()
            {
                var proc = Process.GetCurrentProcess();
                PROCESS_MEMORY_COUNTERS_EX counters;
                if (!GetProcessMemoryInfo(proc.Handle, out counters, (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX))))
                {
                    return new MemorySnapshot(0, 0);
                }
                double pv = counters.PrivateUsage.ToInt64() / 1024.0 / 1024.0;
                double ws = counters.WorkingSetSize.ToInt64() / 1024.0 / 1024.0;
                return new MemorySnapshot(pv, ws);
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

        private static int RunDvstDoubleLoadSelfTest()
        {
            const string modelAPath = @"Y:\zxc\微组BUG测试\pipeline.dvst";
            const string modelBPath = @"Y:\zxc\微组BUG测试\实例分割筛选测试_120_50.dvst";
            const string imagePath = @"Y:\zxc\微组BUG测试\实例分割滑窗大图.png";
            const int deviceId = 0;

            Console.WriteLine("==== dvst 双模型加载-释放-再加载自测 ====");

            if (!File.Exists(modelAPath)) { Console.WriteLine("模型A不存在: " + modelAPath); return 2; }
            if (!File.Exists(modelBPath)) { Console.WriteLine("模型B不存在: " + modelBPath); return 2; }
            if (!File.Exists(imagePath))  { Console.WriteLine("图像不存在: " + imagePath); return 2; }

            Mat bgr = null;
            Mat rgb = null;
            try
            {
                bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (bgr == null || bgr.Empty()) { Console.WriteLine("图像解码失败"); return 2; }
                rgb = new Mat();
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
            }
            catch (Exception ex)
            {
                Console.WriteLine("图像读取异常: " + ex.Message);
                return 2;
            }

            var p = new JObject { ["threshold"] = 0.5, ["with_mask"] = true, ["batch_size"] = 1 };
            Model modelA = null;
            Model modelB = null;
            int step = 0;

            try
            {
                // 1. 加载A
                step = 1;
                Console.WriteLine("[" + step + "] 加载模型A...");
                modelA = new Model(modelAPath, deviceId, false, false);
                Console.WriteLine("    A loaded, provider=" + modelA.LoadedDogProvider + ", dll=" + modelA.LoadedNativeDllName);

                // 2. 加载B
                step = 2;
                Console.WriteLine("[" + step + "] 加载模型B...");
                modelB = new Model(modelBPath, deviceId, false, false);
                Console.WriteLine("    B loaded, provider=" + modelB.LoadedDogProvider + ", dll=" + modelB.LoadedNativeDllName);

                // 3. A推理
                step = 3;
                Console.WriteLine("[" + step + "] 模型A首次推理...");
                var ra1 = modelA.InferBatch(new List<Mat> { rgb }, p);
                Console.WriteLine("    A推理完成, sample_count=" + ra1.SampleResults.Count);
                DisposeResultMasks(ra1);

                // 4. B推理
                step = 4;
                Console.WriteLine("[" + step + "] 模型B首次推理...");
                var rb1 = modelB.InferBatch(new List<Mat> { rgb }, p);
                Console.WriteLine("    B推理完成, sample_count=" + rb1.SampleResults.Count);
                DisposeResultMasks(rb1);

                // 5. 释放A (FreeModel)
                step = 5;
                Console.WriteLine("[" + step + "] 释放模型A (FreeModel)...");
                modelA.FreeModel();
                Console.WriteLine("    A已释放");

                // 6. 再次加载A
                step = 6;
                Console.WriteLine("[" + step + "] 再次加载模型A...");
                modelA = new Model(modelAPath, deviceId, false, false);
                Console.WriteLine("    A再次加载完成, provider=" + modelA.LoadedDogProvider);

                // 7. 释放B (FreeModel)
                step = 7;
                Console.WriteLine("[" + step + "] 释放模型B (FreeModel)...");
                modelB.FreeModel();
                Console.WriteLine("    B已释放");

                // 8. 再次加载B
                step = 8;
                Console.WriteLine("[" + step + "] 再次加载模型B...");
                modelB = new Model(modelBPath, deviceId, false, false);
                Console.WriteLine("    B再次加载完成, provider=" + modelB.LoadedDogProvider);

                // 9. A再次推理
                step = 9;
                Console.WriteLine("[" + step + "] 模型A再次推理...");
                var ra2 = modelA.InferBatch(new List<Mat> { rgb }, p);
                Console.WriteLine("    A再次推理完成, sample_count=" + ra2.SampleResults.Count);
                DisposeResultMasks(ra2);

                // 10. B再次推理
                step = 10;
                Console.WriteLine("[" + step + "] 模型B再次推理...");
                var rb2 = modelB.InferBatch(new List<Mat> { rgb }, p);
                Console.WriteLine("    B再次推理完成, sample_count=" + rb2.SampleResults.Count);
                DisposeResultMasks(rb2);

                Console.WriteLine("==== dvst 双模型加载-释放-再加载自测 全部通过 ====");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[" + step + "] 异常: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
            finally
            {
                if (rgb != null) rgb.Dispose();
                if (bgr != null) bgr.Dispose();
                try { if (modelA != null) modelA.Dispose(); } catch { }
                try { if (modelB != null) modelB.Dispose(); } catch { }
                ForceGc();
            }
        }

        private static int RunCalcMeanSelfTest()
        {
            try
            {
                Type resultType = typeof(Utils.CSharpObjectResult);
                Type[] legacyConstructorTypes =
                {
                    typeof(int), typeof(string), typeof(float), typeof(float),
                    typeof(List<double>), typeof(bool), typeof(Mat), typeof(bool),
                    typeof(bool), typeof(float), typeof(JObject)
                };
                if (resultType.GetConstructor(legacyConstructorTypes) == null)
                {
                    throw new InvalidOperationException("未保留原有 CSharpObjectResult 构造函数签名。");
                }

                Type[] completeConstructorTypes =
                {
                    typeof(int), typeof(string), typeof(float), typeof(float),
                    typeof(List<double>), typeof(bool), typeof(Mat), typeof(bool),
                    typeof(bool), typeof(float), typeof(JObject), typeof(bool),
                    typeof(double), typeof(double)
                };
                if (resultType.GetConstructor(completeConstructorTypes) == null)
                {
                    throw new InvalidOperationException("缺少包含均值字段的完整构造函数签名。");
                }

                var defaultResult = new Utils.CSharpObjectResult(
                    1, "默认均值", 0.9f, 1.0f,
                    new List<double> { 1.0, 2.0, 3.0, 4.0 }, false, null);
                if (defaultResult.WithMean || defaultResult.ForegroundMean != 0.0 || defaultResult.BackgroundMean != 0.0)
                {
                    throw new InvalidOperationException("默认均值字段不符合 false/0.0 语义。");
                }

                var resultWithMean = new Utils.CSharpObjectResult(
                    2, "显式均值", 0.8f, 2.0f,
                    new List<double> { 5.0, 6.0, 7.0, 8.0 }, false, null,
                    false, false, -100f, null, true, 12.5, 34.75);
                if (!resultWithMean.WithMean
                    || Math.Abs(resultWithMean.ForegroundMean - 12.5) > 1e-12
                    || Math.Abs(resultWithMean.BackgroundMean - 34.75) > 1e-12)
                {
                    throw new InvalidOperationException("显式均值字段映射错误。");
                }

                MethodInfo buildParamsMethod = typeof(DetModel).GetMethod(
                    "BuildInferParams", BindingFlags.Instance | BindingFlags.NonPublic);
                if (buildParamsMethod == null)
                {
                    throw new InvalidOperationException("未找到 Flow 均值参数处理方法。");
                }

                var context = new DlcvModules.ExecutionContext();
                var model = new DetModel(
                    1, "均值参数测试",
                    new Dictionary<string, object> { ["calc_mean"] = true },
                    context);

                var nodeParams = (JObject)buildParamsMethod.Invoke(model, null);
                if (nodeParams.Value<bool?>("calc_mean") != true)
                {
                    throw new InvalidOperationException("Flow 节点均值参数未生效。");
                }

                context.Set("infer_params", new JObject { ["calc_mean"] = false });
                var overriddenParams = (JObject)buildParamsMethod.Invoke(model, null);
                if (overriddenParams.Value<bool?>("calc_mean") != false)
                {
                    throw new InvalidOperationException("Flow 入口均值参数未覆盖节点值。");
                }

                context.Set("infer_params", new JObject());
                var restoredParams = (JObject)buildParamsMethod.Invoke(model, null);
                if (restoredParams.Value<bool?>("calc_mean") != true)
                {
                    throw new InvalidOperationException("Flow 节点均值参数未恢复。");
                }

                Console.WriteLine("calc_mean 自测通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("calc_mean 自测失败: " + ex.Message);
                return 1;
            }
        }

        private static int RunWithMaskSelfTest()
        {
            Console.WriteLine("==== with_mask 参数透传自测 ====");

            var cases = new[]
            {
                new { Name = "seg测试", ModelPath = @"C:\Users\Administrator\Desktop\seg.dvt", ImagePath = @"C:\Users\Administrator\Desktop\seg.jpg" },
                new { Name = "实例分割-dvt", ModelPath = @"Y:\zxc\模块化任务测试\实例分割\实例分割.dvt", ImagePath = @"Y:\zxc\模块化任务测试\实例分割\实例分割滑窗大图.png" },
                new { Name = "实例分割-dvst", ModelPath = @"Y:\zxc\模块化任务测试\实例分割\滑窗测试_120_50.dvst", ImagePath = @"Y:\zxc\模块化任务测试\实例分割\实例分割滑窗大图.png" }
            };

            int globalFail = 0;
            foreach (var c in cases)
            {
                Console.WriteLine();
                Console.WriteLine("[" + c.Name + "]");
                Console.WriteLine("model: " + c.ModelPath);
                Console.WriteLine("image: " + c.ImagePath);

                if (!File.Exists(c.ModelPath))
                {
                    Console.WriteLine("模型不存在，跳过");
                    continue;
                }
                if (!File.Exists(c.ImagePath))
                {
                    Console.WriteLine("图片不存在，跳过");
                    continue;
                }

                Model model = null;
                Mat bgr = null;
                Mat rgb = null;
                try
                {
                    model = new Model(c.ModelPath, GpuDeviceId, false, false);
                    Console.WriteLine("加载成功: provider=" + model.LoadedDogProvider + ", dll=" + model.LoadedNativeDllName);

                    bgr = Cv2.ImRead(c.ImagePath, ImreadModes.Color);
                    if (bgr == null || bgr.Empty()) throw new Exception("图像解码失败");
                    rgb = new Mat();
                    Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

                    // 1) InferOneOutJson with_mask=true
                    var pTrue = new JObject { ["threshold"] = 0.5, ["with_mask"] = true, ["batch_size"] = 1 };
                    dynamic jsonResult = model.InferOneOutJson(rgb, pTrue);
                    var arr = jsonResult as JArray ?? new JArray();
                    int trueCount = 0;
                    foreach (JObject o in arr)
                    {
                        bool wm = o["with_mask"]?.Value<bool>() ?? false;
                        if (wm) trueCount++;
                    }
                    Console.WriteLine("InferOneOutJson(with_mask=true): 目标数=" + arr.Count + ", with_mask=true 数=" + trueCount);
                    if (arr.Count > 0)
                    {
                        try
                        {
                            var rawFirst = arr[0] as JObject;
                            Console.WriteLine("  标准化后首个对象: " + rawFirst?.ToString(Formatting.None)?.Substring(0, Math.Min(300, rawFirst?.ToString(Formatting.None)?.Length ?? 0)));
                        }
                        catch { }
                    }

                    // 2) Infer / InferBatch with_mask=true
                    var batchResult = model.InferBatch(new List<Mat> { rgb }, pTrue);
                    int batchTrueCount = 0;
                    if (batchResult.SampleResults != null && batchResult.SampleResults.Count > 0)
                    {
                        foreach (var obj in batchResult.SampleResults[0].Results)
                        {
                            if (obj.WithMask) batchTrueCount++;
                        }
                        Console.WriteLine("InferBatch(with_mask=true): 目标数=" + batchResult.SampleResults[0].Results.Count + ", WithMask=true 数=" + batchTrueCount);
                    }
                    DisposeResultMasks(batchResult);

                    // 3) InferOneOutJson with_mask=false
                    var pFalse = new JObject { ["threshold"] = 0.5, ["with_mask"] = false, ["batch_size"] = 1 };
                    dynamic jsonResultFalse = model.InferOneOutJson(rgb, pFalse);
                    var arrFalse = jsonResultFalse as JArray ?? new JArray();
                    int falseCount = 0;
                    foreach (JObject o in arrFalse)
                    {
                        bool wm = o["with_mask"]?.Value<bool>() ?? false;
                        if (!wm) falseCount++;
                    }
                    Console.WriteLine("InferOneOutJson(with_mask=false): 目标数=" + arrFalse.Count + ", with_mask=false 数=" + falseCount);

                    // 4) InferBatch with_mask=false
                    var batchFalseResult = model.InferBatch(new List<Mat> { rgb }, pFalse);
                    int batchFalseCount = 0;
                    if (batchFalseResult.SampleResults != null && batchFalseResult.SampleResults.Count > 0)
                    {
                        foreach (var obj in batchFalseResult.SampleResults[0].Results)
                        {
                            if (!obj.WithMask) batchFalseCount++;
                        }
                        Console.WriteLine("InferBatch(with_mask=false): 目标数=" + batchFalseResult.SampleResults[0].Results.Count + ", WithMask=false 数=" + batchFalseCount);
                    }
                    DisposeResultMasks(batchFalseResult);

                    // 判定：如果模型是实例/语义分割模型，with_mask=true 时应当有 mask
                    // 这里只做参数透传检查：true 请求不应被强制改为 false（除非模型本身不输出 mask）
                    // 打印原始底层 JSON 中是否有 mask_ptr，帮助定位
                    if (arr.Count > 0)
                    {
                        var first = arr[0] as JObject;
                        var maskTok = first["mask"];
                        if (maskTok is JObject maskObj)
                        {
                            long ptr = maskObj["mask_ptr"]?.Value<long>() ?? 0;
                            Console.WriteLine("底层 mask 对象: ptr=" + ptr + ", w=" + (maskObj["width"]?.Value<int>() ?? 0) + ", h=" + (maskObj["height"]?.Value<int>() ?? 0));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("异常: " + ex.Message);
                    Console.WriteLine(ex.StackTrace);
                    globalFail++;
                }
                finally
                {
                    if (rgb != null) rgb.Dispose();
                    if (bgr != null) bgr.Dispose();
                    try { if (model != null) model.Dispose(); } catch { }
                    ForceGc();
                }
            }

            Console.WriteLine();
            if (globalFail == 0)
            {
                Console.WriteLine("==== with_mask 参数透传自测 完成 ====");
                return 0;
            }
            Console.WriteLine("==== with_mask 参数透传自测 失败数=" + globalFail + " ====");
            return 1;
        }

        private static int RunTemplateCountPrioritySelfTest()
        {
            try
            {
                var golden = new SimpleTemplate
                {
                    TemplateName = "COUNT-PRIORITY",
                    ProductName = "COUNT-PRIORITY",
                    OCRResults = new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("A", 0, 0),
                        MakeTemplateSelfTestItem("A", 20, 0),
                        MakeTemplateSelfTestItem("B", 40, 0),
                        MakeTemplateSelfTestItem(" ", 60, 0),
                        null
                    }
                };

                var equalButDifferent = RunTemplateCountPriorityCase(
                    golden,
                    new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem(" a ", 100, 100),
                        MakeTemplateSelfTestItem("A", 120, 100),
                        MakeTemplateSelfTestItem("B", 140, 100)
                    },
                    true);
                RequireTemplateCountPriority(
                    equalButDifferent.Value<bool>("ok") &&
                    equalButDifferent["detail"]?["template_match_info"]?.Value<bool>("is_match") == true &&
                    equalButDifferent["detail"]?.Value<int>("expected_count") == 3 &&
                    equalButDifferent["detail"]?["ocr_results"] is JArray equalItems &&
                    equalItems.Count == 3 &&
                    equalItems.All(item => item?["match_status"]?.ToString() == "Correct"),
                    "count_priority=true should accept equal normalized category counts despite position changes");

                var categoryDistributionMismatch = RunTemplateCountPriorityCase(
                    golden,
                    new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("A", 0, 0),
                        MakeTemplateSelfTestItem("B", 20, 0),
                        MakeTemplateSelfTestItem("B", 40, 0)
                    },
                    true);
                var distributionDetail = categoryDistributionMismatch["detail"] as JObject;
                var distributionReason = distributionDetail?["template_match_info"]?["error_reason"]?.ToString();
                RequireTemplateCountPriority(
                    !categoryDistributionMismatch.Value<bool>("ok") &&
                    distributionDetail?["template_match_info"]?.Value<int>("perfect_matches") == 2 &&
                    distributionDetail?["template_match_info"]?.Value<int>("over_detections") == 1 &&
                    distributionDetail?["template_match_info"]?.Value<int>("missing_components") == 1 &&
					distributionReason == "缺：A×1；多：B×1" &&
                    distributionReason.IndexOf("已执行模版匹配", StringComparison.Ordinal) < 0,
                    "equal totals with different normalized category distributions should fall back and fail");

                var totalMismatch = RunTemplateCountPriorityCase(
                    golden,
                    new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("A", 0, 0),
                        MakeTemplateSelfTestItem("A", 20, 0)
                    },
                    true);
                RequireTemplateCountPriority(
                    !totalMismatch.Value<bool>("ok") &&
                    totalMismatch["detail"]?["template_match_info"]?.Value<int>("missing_components") == 1 &&
					totalMismatch["detail"]?["template_match_info"]?["error_reason"]?.ToString() == "缺：B×1",
                    "different totals should fall back to ordinary matching and fail");

                var disabled = RunTemplateCountPriorityCase(
                    golden,
                    new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("A", 100, 100),
                        MakeTemplateSelfTestItem("A", 120, 100),
                        MakeTemplateSelfTestItem("B", 140, 100)
                    },
                    false);
                RequireTemplateCountPriority(
                    !disabled.Value<bool>("ok") &&
                    disabled["detail"]?["template_match_info"]?.Value<int>("over_detections") == 3 &&
                    disabled["detail"]?["template_match_info"]?.Value<int>("missing_components") == 3 &&
                    disabled["detail"]?["template_match_info"]?["error_reason"]?.ToString() ==
						"缺：A×2、B×1；多：A×2、B×1",
                    "count_priority=false should preserve ordinary position-sensitive matching");

                var bGolden = new SimpleTemplate
                {
                    TemplateName = "EXACT-B",
                    OCRResults = new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("B", 0, 0)
                    }
                };
                var bVsEight = RunTemplateCountPriorityCase(
                    bGolden,
                    new List<SimpleOcrItem> { MakeTemplateSelfTestItem("8", 0, 0) },
                    true);
                RequireTemplateCountPriority(
                    !bVsEight.Value<bool>("ok") &&
					bVsEight["detail"]?["template_match_info"]?["error_reason"]?.ToString() ==
						"缺：B×1；多：8×1；误判：模版 B→检测 8×1",
                    "count_priority fallback must keep B distinct from 8");

                var okGolden = new SimpleTemplate
                {
                    TemplateName = "EXACT-OK",
                    OCRResults = new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("OK", 0, 0)
                    }
                };
                var okVsZeroK = RunTemplateCountPriorityCase(
                    okGolden,
                    new List<SimpleOcrItem> { MakeTemplateSelfTestItem("0K", 0, 0) },
                    true,
                    false);
                RequireTemplateCountPriority(
                    !okVsZeroK.Value<bool>("ok") &&
					okVsZeroK["detail"]?["template_match_info"]?["error_reason"]?.ToString() ==
						"缺：OK×1；多：0K×1",
                    "count_priority fallback must keep OK distinct from 0K");

                var legacyBVsEight = RunTemplateCountPriorityCase(
                    bGolden,
                    new List<SimpleOcrItem> { MakeTemplateSelfTestItem("8", 0, 0) },
                    false);
                RequireTemplateCountPriority(
                    legacyBVsEight.Value<bool>("ok"),
                    "count_priority=false should preserve ordinary ambiguous-character normalization");

                var deviationTemplateItem = MakeTemplateSelfTestItem("A", 0, 0);
                deviationTemplateItem.Width = 100;
                var deviationDetectionItem = MakeTemplateSelfTestItem("A", 40, 0);
                deviationDetectionItem.Width = 100;
                var deviationGolden = new SimpleTemplate
                {
                    TemplateName = "POSITION-REASON",
                    OCRResults = new List<SimpleOcrItem>
                    {
                        deviationTemplateItem,
                        MakeTemplateSelfTestItem("B", 200, 0)
                    }
                };
                var deviationMismatch = RunTemplateCountPriorityCase(
                    deviationGolden,
                    new List<SimpleOcrItem> { deviationDetectionItem },
                    true);
                RequireTemplateCountPriority(
                    !deviationMismatch.Value<bool>("ok") &&
					deviationMismatch["detail"]?["template_match_info"]?["error_reason"]?.ToString() ==
						"缺：B×1；位置偏差：A×1",
                    "ordinary fallback should include concrete position deviation details when the result is NG");

                var dGolden = new SimpleTemplate
                {
                    TemplateName = "COUNT-PRIORITY-D",
                    ProductName = "COUNT-PRIORITY-D",
                    CameraPosition = 3,
                    OCRResults = new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("OK", 0, 0),
                        MakeTemplateSelfTestItem("OK", 20, 0),
                        MakeTemplateSelfTestItem("OK", 40, 0),
                        MakeTemplateSelfTestItem("NG", 60, 0)
                    }
                };
                var dSameDistribution = RunTemplateCountPriorityCase(
                    dGolden,
                    new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("OK", 100, 100),
                        MakeTemplateSelfTestItem("OK", 120, 100),
                        MakeTemplateSelfTestItem("OK", 140, 100),
                        MakeTemplateSelfTestItem("NG", 160, 100)
                    },
                    true);
                RequireTemplateCountPriority(
                    dSameDistribution.Value<bool>("ok") &&
                    dSameDistribution["detail"]?["ocr_results"] is JArray dItems &&
                    dItems.All(item => item?["match_status"]?.ToString() == "Correct"),
                    "D count priority should accept OK x3 plus NG x1 only when category counts match");

                var dWrongDistribution = RunTemplateCountPriorityCase(
                    dGolden,
                    new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("OK", 0, 0),
                        MakeTemplateSelfTestItem("OK", 20, 0),
                        MakeTemplateSelfTestItem("OK", 40, 0),
                        MakeTemplateSelfTestItem("OK", 60, 0)
                    },
                    true);
                RequireTemplateCountPriority(
                    !dWrongDistribution.Value<bool>("ok") &&
                    ((dWrongDistribution["detail"]?["template_match_info"]?.Value<int>("over_detections") ?? 0) +
                     (dWrongDistribution["detail"]?["template_match_info"]?.Value<int>("missing_components") ?? 0) +
                     (dWrongDistribution["detail"]?["template_match_info"]?.Value<int>("misjudgments") ?? 0)) > 0 &&
					dWrongDistribution["detail"]?["template_match_info"]?["error_reason"]?.ToString() ==
						"缺：NG×1；多：OK×1；误判：模版 NG→检测 OK×1",
                    "D count priority must reject OK x4 when the template is OK x3 plus NG x1");

                VerifyTemplateSaveNgPolicy();

                Console.WriteLine("template count priority selftest passed");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("template count priority selftest failed: " + ex.Message);
                return 1;
            }
        }

        private static JObject RunTemplateCountPriorityCase(
            SimpleTemplate golden,
            List<SimpleOcrItem> detections,
            bool countPriority,
            bool checkPosition = true)
        {
            var module = new TemplateMatch(
                9001,
                properties: new Dictionary<string, object>
                {
                    { "count_priority", countPriority },
                    { "expected_count", 999 },
                    { "check_position", checkPosition },
                    { "min_confidence_threshold", 0.5 }
                });
            module.MainTemplateList = new List<SimpleTemplate>
            {
                new SimpleTemplate { OCRResults = detections }
            };
            module.ExtraInputsIn.Add(new ModuleChannel(
                new List<ModuleImage>(),
                new JArray(),
                new List<SimpleTemplate> { golden }));
            module.Process(new List<ModuleImage>(), new JArray());

            var ok = module.ScalarOutputsByName.ContainsKey("ok") &&
                Convert.ToBoolean(module.ScalarOutputsByName["ok"]);
            var detailText = module.ScalarOutputsByName.ContainsKey("detail")
                ? Convert.ToString(module.ScalarOutputsByName["detail"])
                : string.Empty;
            return new JObject
            {
                ["ok"] = ok,
                ["detail"] = string.IsNullOrWhiteSpace(detailText)
                    ? new JObject()
                    : JObject.Parse(detailText)
            };
        }

        private static SimpleOcrItem MakeTemplateSelfTestItem(string text, int x, int y)
        {
            return new SimpleOcrItem
            {
                Text = text,
                X = x,
                Y = y,
                Width = 10,
                Height = 10,
                Confidence = 0.99f
            };
        }

        private static void RequireTemplateCountPriority(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void VerifyTemplateSaveNgPolicy()
        {
            var root = Path.Combine(Path.GetTempPath(), "DlcvTemplateSaveNg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var context = new DlcvModules.ExecutionContext();
                context.Set("templates_dir", root);
                var dTemplate = new SimpleTemplate
                {
                    TemplateId = "D-NG-POLICY",
                    TemplateName = "D-NG-POLICY",
                    CameraPosition = 3,
                    OCRResults = new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("OK", 0, 0),
                        MakeTemplateSelfTestItem("OK", 20, 0),
                        MakeTemplateSelfTestItem("OK", 40, 0),
                        MakeTemplateSelfTestItem("NG", 60, 0)
                    }
                };
                var dSaver = new TemplateSave(
                    9101,
                    properties: new Dictionary<string, object> { { "file_name", "D-NG-POLICY" } },
                    context: context);
                dSaver.MainTemplateList = new List<SimpleTemplate> { dTemplate };
                dSaver.Process(new List<ModuleImage>(), new JArray());
                var savedD = JsonConvert.DeserializeObject<SimpleTemplate>(
                    File.ReadAllText(Path.Combine(root, "D-NG-POLICY.json")));
                RequireTemplateCountPriority(
                    savedD != null && savedD.OCRResults.Count == 4 && savedD.OCRResults.Count(x => x.Text == "NG") == 1,
                    "TemplateSave should preserve NG categories for D templates");

                var aTemplate = new SimpleTemplate
                {
                    TemplateId = "A-NG-POLICY",
                    TemplateName = "A-NG-POLICY",
                    CameraPosition = 0,
                    OCRResults = new List<SimpleOcrItem>
                    {
                        MakeTemplateSelfTestItem("PRINT", 0, 0),
                        MakeTemplateSelfTestItem("NG-PRINT", 20, 0)
                    }
                };
                var aSaver = new TemplateSave(
                    9102,
                    properties: new Dictionary<string, object> { { "file_name", "A-NG-POLICY" } },
                    context: context);
                aSaver.MainTemplateList = new List<SimpleTemplate> { aTemplate };
                aSaver.Process(new List<ModuleImage>(), new JArray());
                var savedA = JsonConvert.DeserializeObject<SimpleTemplate>(
                    File.ReadAllText(Path.Combine(root, "A-NG-POLICY.json")));
                RequireTemplateCountPriority(
                    savedA != null && savedA.OCRResults.Count == 1 && savedA.OCRResults[0].Text == "PRINT",
                    "TemplateSave should keep the existing A/B/C NG text filter");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr hProcess, out PROCESS_MEMORY_COUNTERS_EX counters, uint size);
    }
}
