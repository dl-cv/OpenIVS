using System;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpTest
{
    internal static class MemoryValidationSelfTest
    {
        internal static int Run()
        {
            try
            {
                JToken first = new JObject { ["result_list"] = new JArray(new JObject { ["bbox"] = new JArray(10, 20, 30, 40) }) };
                JToken flipped = new JObject { ["result_list"] = new JArray(new JObject { ["bbox"] = new JArray(70, 20, 90, 40) }) };
                JToken empty = new JObject { ["result_list"] = new JArray() };
                BackendReuseSelfTest.VerifyReferences(new[] { new[] { first, flipped }, new[] { first, flipped } }, new JArray());
                ExpectFailure(() => BackendReuseSelfTest.VerifyReferences(
                    new[] { new[] { first, first.DeepClone() }, new[] { first, flipped } }, new JArray()), "模型A两个输入的参考结果可区分");
                ExpectFailure(() => BackendReuseSelfTest.VerifyReferences(
                    new[] { new[] { first, flipped }, new[] { first, first.DeepClone() } }, new JArray()), "模型B两个输入的参考结果可区分");
                ExpectFailure(() => BackendReuseSelfTest.VerifyReferences(
                    new[] { new[] { empty, empty }, new[] { first, flipped } }, new JArray()), "模型A参考图片产生实际识别结果");
                ExpectFailure(() => BackendReuseSelfTest.VerifyReferences(
                    new[] { new[] { first, flipped }, new[] { empty, empty } }, new JArray()), "模型B参考图片产生实际识别结果");
                Require(GpuProcessMemorySample.Validate(new JObject(), false) == 0, "加载前及释放后允许无实例");
                Require(GpuProcessMemorySample.Validate(new JObject { ["gpu"] = 0L }, false) == 0, "允许零值阶段保留实际实例");
                ExpectFailure(() => GpuProcessMemorySample.Validate(new JObject(), true), "未找到当前 PID");
                ExpectFailure(() => GpuProcessMemorySample.Validate(new JObject { ["gpu"] = 0L }, true), "必须大于零");
                foreach (bool requirePositive in new[] { false, true })
                    ExpectFailure(() => GpuProcessMemorySample.Validate(new JObject { ["gpu"] = -1L }, requirePositive), "返回负数");
                Require(GpuProcessMemorySample.Validate(new JObject { ["gpu0"] = 1024L, ["gpu1"] = 2048L }, true) == 3072L,
                    "多个实例按字节求和");
                Console.WriteLine("参考结果区分度与显存采样判定回归通过");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void ExpectFailure(Action action, string message)
        {
            try { action(); }
            catch (InvalidOperationException error)
            {
                if (error.Message.Contains(message)) return;
                throw;
            }
            throw new InvalidOperationException("预期失败未发生: " + message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
