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
                "\"calc_mean\":true,\"return_polygon\":false}");
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
            if (parameters["calc_mean"] == null || parameters["calc_mean"].Type != JTokenType.Boolean ||
                !parameters.Value<bool>("calc_mean"))
            {
                Console.WriteLine("calc_mean 类型或数值异常: " + parameters.ToString());
                return 1;
            }
            if (parameters["return_polygon"] == null || parameters["return_polygon"].Type != JTokenType.Boolean ||
                parameters.Value<bool>("return_polygon"))
            {
                Console.WriteLine("return_polygon 类型或数值异常: " + parameters.ToString());
                return 1;
            }

            Console.WriteLine("流程推理参数类型自测通过: " + parameters.ToString(Newtonsoft.Json.Formatting.None));
            return 0;
        }
    }
}
