using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;
using DlcvModules;
using dlcv_infer_csharp;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static partial class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DlcvCImage
        {
            public long data_ptr;
            public int height;
            public int width;
            public int channel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DlcvCImageList
        {
            public IntPtr images;
            public int n;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DlcvCResult
        {
            public int code;
            public IntPtr message;
            public IntPtr sample_results;
            public int n;
        }

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, EntryPoint = "dlcv_infer_cpp_load_model_c")]
        private static extern int NativeCLoadModelRaw(IntPtr modelPathUtf8, int deviceId);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, EntryPoint = "dlcv_infer_cpp_get_last_error_c")]
        private static extern IntPtr NativeCGetLastErrorRaw();

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, EntryPoint = "dlcv_infer_cpp_free_model_c")]
        private static extern int NativeCFreeModelRaw(int modelIndex);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, EntryPoint = "dlcv_infer_cpp_get_model_info_c")]
        private static extern IntPtr NativeCGetModelInfoRaw(int modelIndex);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, EntryPoint = "dlcv_infer_cpp_infer_json_c")]
        private static extern IntPtr NativeCInferJsonRaw(
            int modelIndex,
            ref DlcvCImage image,
            IntPtr parametersJsonUtf8);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, EntryPoint = "dlcv_infer_cpp_free_string_c")]
        private static extern void NativeCFreeStringRaw(IntPtr value);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, EntryPoint = "dlcv_infer_cpp_free_all_models_c")]
        private static extern void NativeCFreeAllModelsRaw();

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.StdCall,
            ExactSpelling = true, EntryPoint = "dlcv_infer_c")]
        private static extern DlcvCResult NativeCompatInferRaw(
            int modelIndex,
            ref DlcvCImageList imageList);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.StdCall,
            ExactSpelling = true, EntryPoint = "dlcv_free_model_result_c")]
        private static extern void NativeCompatFreeModelResultRaw(ref DlcvCResult result);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.StdCall,
            ExactSpelling = true, EntryPoint = "dlcv_free_model")]
        private static extern IntPtr NativeLegacyFreeModelRaw(IntPtr configJsonUtf8);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.StdCall,
            ExactSpelling = true, EntryPoint = "dlcv_get_model_info")]
        private static extern IntPtr NativeLegacyGetModelInfoRaw(IntPtr configJsonUtf8);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.StdCall,
            ExactSpelling = true, EntryPoint = "dlcv_infer")]
        private static extern IntPtr NativeLegacyInferRaw(IntPtr configJsonUtf8);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.StdCall,
            ExactSpelling = true, EntryPoint = "dlcv_free_result")]
        private static extern void NativeLegacyFreeResultRaw(IntPtr value);

        [DllImport("dlcv_infer_cpp.dll", CallingConvention = CallingConvention.StdCall,
            ExactSpelling = true, EntryPoint = "dlcv_free_model_result")]
        private static extern void NativeLegacyFreeModelResultRaw(IntPtr value);

        private sealed class Utf8NativeBuffer : IDisposable
        {
            public IntPtr Pointer { get; private set; }

            public Utf8NativeBuffer(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes((value ?? string.Empty) + "\0");
                Pointer = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, Pointer, bytes.Length);
            }

            public void Dispose()
            {
                if (Pointer == IntPtr.Zero) return;
                Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }
        }

        private static int NativeCLoadModel(string modelPath, int deviceId)
        {
            using (var path = new Utf8NativeBuffer(modelPath))
            {
                int index = NativeCLoadModelRaw(path.Pointer, deviceId);
                if (index == -1)
                {
                    throw new InvalidOperationException(
                        "正式 C 接口加载失败: " + ReadNativeLastError());
                }
                return index;
            }
        }

        private static int NativeCFreeModel(int modelIndex)
        {
            return NativeCFreeModelRaw(modelIndex);
        }

        private static void NativeCFreeAllModels()
        {
            NativeCFreeAllModelsRaw();
        }

        private static JObject NativeCGetModelInfo(int modelIndex, string operation)
        {
            IntPtr result = NativeCGetModelInfoRaw(modelIndex);
            if (result == IntPtr.Zero)
            {
                throw new InvalidOperationException(operation + "失败: " + ReadNativeLastError());
            }
            try
            {
                JObject info = JObject.Parse(ReadUtf8String(result));
                if (info.Count == 0) throw new InvalidOperationException(operation + "返回空对象");
                return info;
            }
            finally
            {
                NativeCFreeStringRaw(result);
            }
        }

        private static JToken NativeCInferJson(int modelIndex, Mat image, JObject parameters, string operation)
        {
            if (image == null || image.Empty()) throw new ArgumentException(operation + "图像为空");
            var nativeImage = new DlcvCImage
            {
                data_ptr = image.Data.ToInt64(),
                height = image.Rows,
                width = image.Cols,
                channel = image.Channels()
            };
            using (var parameterBuffer = new Utf8NativeBuffer((parameters ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None)))
            {
                IntPtr result = NativeCInferJsonRaw(modelIndex, ref nativeImage, parameterBuffer.Pointer);
                if (result == IntPtr.Zero)
                {
                    throw new InvalidOperationException(operation + "失败: " + ReadNativeLastError());
                }
                try
                {
                    return JToken.Parse(ReadUtf8String(result));
                }
                finally
                {
                    NativeCFreeStringRaw(result);
                }
            }
        }

        private static JObject NativeLegacyFreeModel(int modelIndex)
        {
            return CallNativeLegacyJson(
                new JObject { ["model_index"] = modelIndex },
                NativeLegacyFreeModelRaw,
                NativeLegacyFreeResultRaw,
                "legacy C free_model");
        }

        private static JObject NativeLegacyGetModelInfo(JObject config)
        {
            return CallNativeLegacyJson(
                config,
                NativeLegacyGetModelInfoRaw,
                NativeLegacyFreeResultRaw,
                "legacy C get_model_info");
        }

        private static JObject NativeLegacyInfer(JObject config)
        {
            return CallNativeLegacyJson(
                config,
                NativeLegacyInferRaw,
                NativeLegacyFreeModelResultRaw,
                "legacy C infer");
        }

        private static JObject CallNativeLegacyJson(
            JObject config,
            Func<IntPtr, IntPtr> invoke,
            Action<IntPtr> release,
            string operation)
        {
            using (var configBuffer = new Utf8NativeBuffer((config ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None)))
            {
                IntPtr result = invoke(configBuffer.Pointer);
                if (result == IntPtr.Zero)
                    throw new InvalidOperationException(operation + "返回空指针");
                try
                {
                    return JObject.Parse(ReadUtf8String(result));
                }
                finally
                {
                    release(result);
                }
            }
        }

        private static string ReadNativeLastError()
        {
            IntPtr value = NativeCGetLastErrorRaw();
            return value == IntPtr.Zero ? string.Empty : ReadUtf8String(value);
        }

        private static int RunNativeCApiRegressionSelfTest()
        {
            try
            {
                RunNativeModelIndexJsonValidationChecks();
                RunNativeMissingModelChecks();
                RunNativeFreeIdempotenceCheck();
                RunManagedModelIndexValidationChecks();
                Console.WriteLine("native-c-api-regression-selftest 通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("native-c-api-regression-selftest 失败: " + ex);
                return 1;
            }
        }

        private static void RunNativeModelIndexJsonValidationChecks()
        {
            JToken[] invalidValues =
            {
                new JValue(-1),
                new JValue(1.5),
                new JValue("1"),
                new JValue(true),
                JValue.CreateNull(),
                new JValue((long)int.MaxValue + 1L)
            };
            foreach (JToken value in invalidValues)
            {
                var config = new JObject { ["model_index"] = value };
                EnsureNativeStatus(
                    NativeLegacyGetModelInfo(config),
                    1,
                    "model_index 必须是 int 范围内的非负整数",
                    "legacy get_model_info model_index=" + value.ToString(Newtonsoft.Json.Formatting.None));
                EnsureNativeStatus(
                    NativeLegacyInfer(config),
                    1,
                    "model_index 必须是 int 范围内的非负整数",
                    "legacy infer model_index=" + value.ToString(Newtonsoft.Json.Formatting.None));
                JObject freeResponse = CallNativeLegacyJson(
                    config,
                    NativeLegacyFreeModelRaw,
                    NativeLegacyFreeResultRaw,
                    "legacy C free_model model_index 校验");
                EnsureNativeStatus(
                    freeResponse,
                    1,
                    "model_index 必须是 int 范围内的非负整数",
                    "legacy free_model model_index=" + value.ToString(Newtonsoft.Json.Formatting.None));
            }
        }

        private static void RunNativeMissingModelChecks()
        {
            const int missingIndex = int.MaxValue;
            var config = new JObject { ["model_index"] = missingIndex };
            EnsureNativeStatus(
                NativeLegacyGetModelInfo(config), 2, "Model not found.",
                "legacy get_model_info 不存在 index");
            EnsureNativeStatus(
                NativeLegacyInfer(config), 2, "Model not found.",
                "legacy infer 不存在 index");

            using (var image = new Mat(1, 1, MatType.CV_8UC3, Scalar.All(0)))
            {
                var nativeImage = new DlcvCImage
                {
                    data_ptr = image.Data.ToInt64(),
                    height = image.Rows,
                    width = image.Cols,
                    channel = image.Channels()
                };
                int imageSize = Marshal.SizeOf(typeof(DlcvCImage));
                IntPtr imagePointer = Marshal.AllocHGlobal(imageSize);
                try
                {
                    Marshal.StructureToPtr(nativeImage, imagePointer, false);
                    var imageList = new DlcvCImageList { images = imagePointer, n = 1 };
                    DlcvCResult result = NativeCompatInferRaw(missingIndex, ref imageList);
                    try
                    {
                        string message = result.message == IntPtr.Zero ? string.Empty : ReadUtf8String(result.message);
                        if (result.code != 2 || !string.Equals(message, "Model not found.", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "兼容 infer_c 错误恢复不正确: code=" + result.code + ", message=" + message);
                        }
                    }
                    finally
                    {
                        NativeCompatFreeModelResultRaw(ref result);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(imagePointer);
                }
            }
        }

        private static void RunNativeFreeIdempotenceCheck()
        {
            const int missingIndex = int.MaxValue;
            if (NativeCFreeModel(missingIndex) != 0 || NativeCFreeModel(missingIndex) != 0)
                throw new InvalidOperationException("正式 C free_model 对不存在 index 未保持幂等成功");
        }

        private static void EnsureNativeStatus(
            JObject response,
            int expectedCode,
            string expectedMessage,
            string operation)
        {
            int? code = response != null ? response.Value<int?>("code") : null;
            string message = response != null ? response.Value<string>("message") : null;
            if (code != expectedCode || !string.Equals(message, expectedMessage, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    operation + "返回不正确: " + (response == null ? "null" : response.ToString(Newtonsoft.Json.Formatting.None)));
            }
        }

        private static void RunManagedModelIndexValidationChecks()
        {
            object[] validValues = { 0, int.MaxValue, new JValue(0), new JValue((long)int.MaxValue) };
            foreach (object value in validValues)
            {
                int parsed = BaseModelModule.ReadModelIndex(value);
                int expected = value is JValue ? Convert.ToInt32(((JValue)value).Value) : Convert.ToInt32(value);
                if (parsed != expected) throw new InvalidOperationException("C# model_index 解析结果不一致");
            }

            object[] invalidValues =
            {
                -1,
                (long)int.MaxValue + 1L,
                1.0,
                1.5m,
                "1",
                true,
                null,
                new JValue(-1),
                new JValue(1.0),
                new JValue("1"),
                JValue.CreateNull()
            };
            foreach (object value in invalidValues)
            {
                EnsureThrows<System.IO.InvalidDataException>(
                    () => BaseModelModule.ReadModelIndex(value),
                    "C# 流程模型接受了非负 int JSON 整数以外的 model_index");
            }
        }

        private static void EnsureNativeJsonResultsMatch(JToken expected, JToken actual, string operation)
        {
            JToken normalizedExpected = NormalizeNativeSharedResult(expected);
            JToken normalizedActual = NormalizeNativeSharedResult(actual);
            if (!NativeJsonTokensMatch(normalizedExpected, normalizedActual))
            {
                throw new InvalidOperationException(
                    operation + "结果不一致\nexpected=" + normalizedExpected.ToString(Newtonsoft.Json.Formatting.None) +
                    "\nactual=" + normalizedActual.ToString(Newtonsoft.Json.Formatting.None));
            }
        }

        private static JToken NormalizeNativeSharedResult(JToken value)
        {
            JToken result = NormalizeWorkflowJson(value);
            foreach (JObject item in result.SelectTokens("$..results[*]").OfType<JObject>())
            {
                var bbox = item["bbox"] as JArray;
                // 分类结果的四个 -1 表示无检测框，C# 旧 JSON 输出仍按数组长度设置 with_bbox。
                if (bbox != null && bbox.Count == 4 && bbox.All(x =>
                    (x.Type == JTokenType.Integer || x.Type == JTokenType.Float) && x.Value<double>() == -1.0))
                    item["with_bbox"] = false;
            }
            return result;
        }

        private static bool NativeJsonTokensMatch(JToken left, JToken right)
        {
            if (left == null || right == null) return left == right;
            if (left.Type == JTokenType.Integer || left.Type == JTokenType.Float)
            {
                if (right.Type != JTokenType.Integer && right.Type != JTokenType.Float) return false;
                return Math.Abs(left.Value<double>() - right.Value<double>()) <= 1e-3;
            }
            if (left.Type != right.Type) return false;

            var leftObject = left as JObject;
            var rightObject = right as JObject;
            if (leftObject != null && rightObject != null)
            {
                var leftProperties = new System.Collections.Generic.Dictionary<string, JToken>(StringComparer.Ordinal);
                foreach (JProperty property in leftObject.Properties()) leftProperties[property.Name] = property.Value;
                var rightProperties = new System.Collections.Generic.Dictionary<string, JToken>(StringComparer.Ordinal);
                foreach (JProperty property in rightObject.Properties()) rightProperties[property.Name] = property.Value;
                if (leftProperties.Count != rightProperties.Count) return false;
                foreach (var item in leftProperties)
                {
                    if (!rightProperties.TryGetValue(item.Key, out JToken value) ||
                        !NativeJsonTokensMatch(item.Value, value)) return false;
                }
                return true;
            }

            var leftArray = left as JArray;
            var rightArray = right as JArray;
            if (leftArray != null && rightArray != null)
            {
                if (leftArray.Count != rightArray.Count) return false;
                for (int index = 0; index < leftArray.Count; index++)
                    if (!NativeJsonTokensMatch(leftArray[index], rightArray[index])) return false;
                return true;
            }
            return JToken.DeepEquals(left, right);
        }

        private static int RunNativeRulesAndCApiRegressionSelfTest()
        {
            int nativeRuleCode = RunSharedIndexNativeRuleSelfTest();
            if (nativeRuleCode != 0) return nativeRuleCode;
            return RunNativeCApiRegressionSelfTest();
        }

    }
}
