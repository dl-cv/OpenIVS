using System;
using System.Collections.Generic;
using System.Reflection;
using DlcvModules;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpTest
{
    internal static partial class Program
    {
        private static int RunFlowInferParamsSelfTest()
        {
            Console.WriteLine("==== 流程推理参数类型自测 ====");

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
    }
}
