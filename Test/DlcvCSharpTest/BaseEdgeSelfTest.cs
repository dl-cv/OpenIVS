using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class BaseEdgeSelfTest
    {
        public static int Run(string[] args)
        {
            if (args.Length != 3) throw new ArgumentException("base-edge-selftest <cases.json> <report.json>");
            new GraphExecutor(new List<Dictionary<string, object>>());
            var report = new JArray();
            int failures = 0;
            foreach (JObject test in JArray.Parse(File.ReadAllText(args[1])))
            {
                var item = new JObject { ["name"] = test["name"] };
                try
                {
                    var type = ModuleRegistry.Get(test.Value<string>("type"));
                    if (type == null && test["model_path"] == null) throw new InvalidOperationException("模块未注册");
                    using (var image = test["image_path"] != null
                        ? Cv2.ImDecode(File.ReadAllBytes(test.Value<string>("image_path")), ImreadModes.Color)
                        : new Mat(test.Value<int?>("height") ?? 420, test.Value<int?>("width") ?? 320,
                            MatType.CV_8UC3, Scalar.All(test.Value<int?>("background") ?? 110)))
                    {
                        if (test["image_path"] != null) Cv2.CvtColor(image, image, ColorConversionCodes.BGR2RGB);
                        if (test["model_path"] != null)
                        {
                            item["results"] = RunFlow(test, image);
                            item["passed"] = true;
                            report.Add(item);
                            Console.WriteLine("PASS " + test["name"]);
                            continue;
                        }
                        foreach (JArray rect in test["rectangles"] as JArray ?? new JArray())
                        {
                            var color = rect[4] is JArray rgb ? new Scalar((int)rgb[0], (int)rgb[1], (int)rgb[2]) : Scalar.All((int)rect[4]);
                            Cv2.Rectangle(image, new Point((int)rect[0], (int)rect[1]), new Point((int)rect[2], (int)rect[3]), color, -1);
                        }
                        var images = new List<ModuleImage>();
                        foreach (JObject wrap in test["images"] as JArray ?? new JArray(new JObject()))
                        {
                            var state = wrap["transform"] is JObject transform
                                ? TransformationState.FromDict(transform.ToObject<Dictionary<string, object>>())
                                : new TransformationState(image.Width, image.Height);
                            images.Add(new ModuleImage(image, image, state, wrap.Value<int?>("origin_index") ?? 0));
                        }
                        var input = test["results"] as JArray ?? new JArray();
                        string before = input.ToString();
                        var module = (BaseModule)Activator.CreateInstance(type, 1, null,
                            (test["properties"] as JObject)?.ToObject<Dictionary<string, object>>(), null);
                        ModuleIO output = null;
                        bool rejected = false;
                        try { output = module.Process(images, input); }
                        catch (ArgumentException) { if (test.Value<bool?>("reject") != true) throw; rejected = true; }
                        if (test.Value<bool?>("reject") == true && !rejected) throw new Exception("应拒绝无效配置");
                        if (input.ToString() != before) throw new Exception("修改了输入结果");
                        if (output != null)
                        {
                            item["results"] = output.ResultList;
                            Match(output.ResultList, test["expected"], "results");
                        }
                        item["passed"] = true;
                    }
                }
                catch (Exception ex) { failures++; item["passed"] = false; item["error"] = ex.Message; }
                report.Add(item);
                Console.WriteLine(item.Value<bool>("passed") ? "PASS " + test["name"] : "FAIL " + item);
            }
            File.WriteAllText(args[2], report.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"base-edge-selftest: {report.Count - failures}/{report.Count} passed");
            return failures == 0 ? 0 : 1;
        }

        private static JToken RunFlow(JObject test, Mat image)
        {
            using (var model = new dlcv_infer_csharp.Model(test.Value<string>("model_path"), test.Value<int?>("device") ?? 0, false, false))
            {
                var parameters = new JObject { ["threshold"] = 0.5, ["with_mask"] = false };
                var structured = model.Infer(image, parameters).SampleResults[0];
                var json = JToken.FromObject(model.InferOneOutJson(image, parameters));
                Match(json, test["expected"], "flow");
                var detections = (JArray)json["result_list"];
                if (structured.Results.Count != detections.Count || structured.Ok != json.Value<bool>("ok")) throw new Exception("结构化与 JSON 数量/判定不一致");
                for (int i = 0; i < detections.Count; i++)
                {
                    if (structured.Results[i].CategoryName != detections[i].Value<string>("category_name")) throw new Exception("结构化类别不一致");
                    Match(new JArray(structured.Results[i].Bbox), detections[i]["bbox"], "structured.bbox");
                }
                foreach (string reason in json["reason"].Values<string>())
                    if (structured.Reason == null || !structured.Reason.Contains(reason)) throw new Exception("结构化原因丢失");
                Match(structured.ExtraInfo, json["extra_info"], "structured.extra_info");
                return json;
            }
        }

        private static void Match(JToken actual, JToken expected, string path)
        {
            if (expected is JObject obj)
            {
                foreach (var property in obj.Properties()) Match(actual?[property.Name], property.Value, path + "." + property.Name);
            }
            else if (expected is JArray array)
            {
                if (!(actual is JArray values) || values.Count != array.Count) throw new Exception(path + " 数量不一致");
                for (int i = 0; i < array.Count; i++) Match(values[i], array[i], path + "[" + i + "]");
            }
            else if (expected != null && (expected.Type == JTokenType.Integer || expected.Type == JTokenType.Float))
            {
                if (actual == null || (actual.Type != JTokenType.Integer && actual.Type != JTokenType.Float)
                    || double.IsNaN(actual.Value<double>()) || Math.Abs(actual.Value<double>() - expected.Value<double>()) > 1e-9)
                    throw new Exception(path + " 数值不一致");
            }
            else if (!JToken.DeepEquals(actual, expected)) throw new Exception(path + " 不一致");
        }
    }
}
