using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static partial class Program
    {
        private static int RunFlowInferParamsSelfTest()
        {
            Console.WriteLine("==== 流程推理参数类型自测 ====");
            RunFlowModuleConcurrencyChecks();
            RunFlowReturnedMaskChecks();

            var source = JObject.Parse(
                "{\"threshold\":0.3,\"top_k\":0,\"epsilon\":1," +
                "\"return_polygon\":false}");
            var properties = source.ToObject<Dictionary<string, object>>();
            if (!(properties["top_k"] is long) || !(properties["epsilon"] is long))
            {
                Console.WriteLine("测试输入未按 Int64 反序列化。");
                return 1;
            }

            var model = new InstanceSegModel(1, properties: properties);
            MethodInfo method = typeof(DetModel).GetMethod(
                "BuildInferParams",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                Console.WriteLine("未找到流程模型推理参数构造方法。");
                return 1;
            }

            var parameters = method.Invoke(model, null) as JObject;
            if (parameters == null)
            {
                Console.WriteLine("流程模型推理参数构造结果无效。");
                return 1;
            }

            if (parameters["top_k"] == null || parameters["top_k"].Type != JTokenType.Integer ||
                parameters.Value<long>("top_k") != 0)
            {
                Console.WriteLine("top_k 未保持整数类型: " + parameters.ToString());
                return 1;
            }
            if (parameters["epsilon"] == null || parameters["epsilon"].Type != JTokenType.Integer ||
                parameters.Value<long>("epsilon") != 1)
            {
                Console.WriteLine("epsilon 未保持整数类型: " + parameters.ToString());
                return 1;
            }
            if (parameters["threshold"] == null || parameters["threshold"].Type != JTokenType.Float ||
                Math.Abs(parameters.Value<double>("threshold") - 0.3) > 1e-9)
            {
                Console.WriteLine("threshold 类型或数值异常: " + parameters.ToString());
                return 1;
            }
            if (parameters["return_polygon"] == null || parameters["return_polygon"].Type != JTokenType.Boolean ||
                parameters.Value<bool>("return_polygon"))
            {
                Console.WriteLine("return_polygon 类型或数值异常: " + parameters.ToString());
                return 1;
            }

            Type cliType = Assembly.LoadFrom(ResolveDemoAssemblyPath()).GetType("DlcvDemo.CliRunner", true);
            MethodInfo resolveThreshold = cliType.GetMethod("ResolveModelThreshold", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string taskType in new[] { "分类", "cls" })
            {
                var info = new JObject { ["task_type"] = taskType, ["threshold"] = 0.5 };
                foreach (var input in new[] { info, new JObject { ["model_info"] = info } })
                {
                    if ((double)resolveThreshold.Invoke(null, new object[] { input }) != 0.0)
                    {
                        Console.WriteLine("分类默认阈值不应使用模型内保存值");
                        return 1;
                    }
                }
            }

            var defaultCls = (JObject)method.Invoke(new ClsModel(2), null);
            if (defaultCls["threshold"] == null || defaultCls.Value<double>("threshold") != 0.0)
            {
                Console.WriteLine("分类必须显式关闭默认分数过滤: " + defaultCls);
                return 1;
            }
            foreach (double threshold in new[] { 0.0, 0.5 })
            {
                var cls = new ClsModel(3, properties: new Dictionary<string, object>
                {
                    ["threshold"] = threshold, ["top_k"] = 2L
                });
                var explicitCls = (JObject)method.Invoke(cls, null);
                if (explicitCls.Value<double>("threshold") != threshold || explicitCls.Value<int>("top_k") != 2)
                {
                    Console.WriteLine("分类显式阈值或 TOP_K 被覆盖: " + explicitCls);
                    return 1;
                }
            }
            var defaultDet = (JObject)method.Invoke(new DetModel(4), null);
            if (defaultDet["threshold"] != null)
            {
                Console.WriteLine("检测默认参数不应改变: " + defaultDet);
                return 1;
            }

            Console.WriteLine("流程推理参数类型自测通过: " + parameters.ToString(Newtonsoft.Json.Formatting.None));
            return 0;
        }

        private static void RunFlowReturnedMaskChecks()
        {
            MethodInfo convert = typeof(DetModel).GetMethod("ConvertToLocalSamples", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo release = typeof(DetModel).GetMethod("ReleaseChunkMasks", BindingFlags.Static | BindingFlags.NonPublic);
            using (var input = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(0)))
            using (var original = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(0)))
            using (var affine = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(0)))
            using (var owned = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(255)))
            {
                var extra = new JObject { ["marker"] = 3 };
                var ownedSample = new dlcv_infer_csharp.Utils.CSharpSampleResult(
                    new List<dlcv_infer_csharp.Utils.CSharpObjectResult>
                    {
                        new dlcv_infer_csharp.Utils.CSharpObjectResult { Mask = owned, WithMask = true, ExtraInfo = extra }
                    });
                var json = (JArray)convert.Invoke(null, new object[] { ownedSample });
                extra["marker"] = 9;
                var borrowedSample = new dlcv_infer_csharp.Utils.CSharpSampleResult(
                    new List<dlcv_infer_csharp.Utils.CSharpObjectResult>
                    {
                        new dlcv_infer_csharp.Utils.CSharpObjectResult { Mask = input },
                        new dlcv_infer_csharp.Utils.CSharpObjectResult { Mask = original },
                        new dlcv_infer_csharp.Utils.CSharpObjectResult { Mask = affine }
                    });
                var chunk = new List<dlcv_infer_csharp.Utils.CSharpSampleResult> { ownedSample, borrowedSample };
                var chunks = new[] { chunk, null, chunk };
                var inputs = new List<ModuleImage> { new ModuleImage(input, original, null) { AffineImage = affine } };
                release.Invoke(null, new object[] { chunks, inputs });
                if (!owned.IsDisposed || input.IsDisposed || original.IsDisposed || affine.IsDisposed)
                    throw new InvalidOperationException("应释放已返回的 mask，并保留全部上游输入图像");
                if (json[0]["extra_info"]["marker"].Value<int>() != 3 ||
                    !JToken.DeepEquals(json[0]["mask_rle"]["runs"], new JArray(0, 4)))
                    throw new InvalidOperationException("结果 JSON 应独立保留附加字段和 mask 数值");
            }
        }

        private static void RunFlowModuleConcurrencyChecks()
        {
            MethodInfo resolve = typeof(BaseModelModule).GetMethod("ResolveThreadCount",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo execute = typeof(BaseModelModule).GetMethod("ExecuteChunks",
                BindingFlags.Static | BindingFlags.NonPublic);
            Action<bool, string> check = (condition, message) =>
            {
                if (!condition) throw new InvalidOperationException(message);
            };
            Func<Dictionary<string, object>, DlcvModules.ExecutionContext, int> read = (properties, context) =>
                (int)resolve.Invoke(new DetModel(20, properties: properties, context: context), null);
            check(read(null, null) == 1, "流程模型默认线程数应为 1");
            foreach (object value in new object[] { 1, 32L, new JValue(2) })
                check(read(new Dictionary<string, object> { ["num_threads"] = value }, null) == Convert.ToInt32(value is JValue j ? j.Value : value),
                    "流程模型整数线程数读取错误");
            foreach (object value in new object[] { true, 2.0, "2", 0, 33, null, new JValue(1.5), JValue.CreateNull(), ulong.MaxValue })
            {
                bool rejected = false;
                try { read(new Dictionary<string, object> { ["num_threads"] = value }, null); }
                catch (TargetInvocationException error) { rejected = error.InnerException is System.IO.InvalidDataException; }
                check(rejected, "流程模型应拒绝无效线程数");
            }
            var ctx = new DlcvModules.ExecutionContext();
            ctx.Set("infer_params", new JObject { ["num_threads"] = 3 });
            check(read(new Dictionary<string, object> { ["num_threads"] = "invalid" }, ctx) == 3,
                "请求线程数应优先于节点配置");
            ctx.Set("infer_params", new JObject { ["num_threads"] = false });
            bool invalidRequestRejected = false;
            try { read(new Dictionary<string, object> { ["num_threads"] = 2 }, ctx); }
            catch (TargetInvocationException error) { invalidRequestRejected = error.InnerException is System.IO.InvalidDataException; }
            check(invalidRequestRejected, "无效请求线程数应报错");

            var order = new List<int>();
            int caller = Thread.CurrentThread.ManagedThreadId;
            execute.Invoke(null, new object[] { 5, 1, (Action<int>)(index =>
            {
                check(Thread.CurrentThread.ManagedThreadId == caller, "单线程任务应在调用线程执行");
                order.Add(index);
            }) });
            check(string.Join(",", order) == "0,1,2,3,4", "单线程任务顺序错误");

            int active = 0, maximum = 0;
            var gate = new object();
            var completed = new int[8];
            using (var firstWave = new CountdownEvent(2))
            {
                execute.Invoke(null, new object[] { completed.Length, 2, (Action<int>)(index =>
                {
                    lock (gate) { active++; maximum = Math.Max(maximum, active); }
                    try
                    {
                        if (index < 2)
                        {
                            firstWave.Signal();
                            check(firstWave.Wait(5000), "指定的两个工作线程未同时执行");
                        }
                        Thread.Sleep(index % 2 == 0 ? 2 : 1);
                        completed[index] = index + 1;
                    }
                    finally { lock (gate) active--; }
                }) });
            }
            check(maximum == 2 && active == 0, "工作线程数量超出配置或未结束");
            for (int i = 0; i < completed.Length; ++i)
                check(completed[i] == i + 1, "并发任务结果未按原位置保存");

            var expected = new InvalidOperationException("批次推理失败");
            int started = 0, finished = 0;
            using (var bothStarted = new CountdownEvent(2))
            using (var throwNow = new ManualResetEventSlim(false))
            using (var finishNow = new ManualResetEventSlim(false))
            {
                var run = Task.Run(() =>
                {
                    try
                    {
                        execute.Invoke(null, new object[] { 2, 2, (Action<int>)(index =>
                        {
                            Interlocked.Increment(ref started);
                            bothStarted.Signal();
                            if (index == 0)
                            {
                                check(throwNow.Wait(5000), "异常任务等待超时");
                                throw expected;
                            }
                            check(finishNow.Wait(5000), "运行中的任务等待超时");
                            Interlocked.Increment(ref finished);
                        }) });
                        return (Exception)null;
                    }
                    catch (TargetInvocationException error) { return error.InnerException; }
                });
                try
                {
                    check(bothStarted.Wait(5000), "异常检查的两个任务未启动");
                    throwNow.Set();
                    check(!run.IsCompleted, "发生异常后应等待运行中的任务结束");
                }
                finally
                {
                    throwNow.Set();
                    finishNow.Set();
                    check(run.Wait(5000), "异常检查任务未结束");
                }
                check(run.Wait(5000) && ReferenceEquals(run.Result, expected), "应保留首个实际异常");
                check(started == 2 && finished == 1, "已开始的任务未全部结束");
            }
        }
    }
}
