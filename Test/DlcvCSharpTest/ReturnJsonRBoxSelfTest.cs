using System;
using System.Collections.Generic;
using System.Reflection;
using dlcv_infer_csharp;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class ReturnJsonRBoxSelfTest
    {
        public static int Run()
        {
            int failures = 0;
            Action<string, Action> check = (name, test) =>
            {
                try { test(); Console.WriteLine("PASS " + name); }
                catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
            };
            using (var image = new Mat(150, 200, MatType.CV_8UC3, Scalar.Black))
            {
                var images = new List<ModuleImage> { new ModuleImage(image, image,
                    new TransformationState(200, 150, affine2x3: new double[] { 0, 2, 10, -2, 0, 90 }), 0) };
                foreach (bool fallback in new[] { false, true })
                {
                    foreach (bool packed in new[] { true, false })
                    {
                        check("旋转平移缩放 " + (packed ? "五项框" : "四项框+angle") + " fallback=" + fallback, () =>
                        {
                            var det = Detection(new JArray(30, 50, 40, 20), true, 0);
                            if (packed) ((JArray)det["bbox"]).Add(0);
                            var input = Entries(det, fallback ? 7 : 0);
                            var before = input.DeepClone();
                            var result = Output(images, input);
                            Require(result["metadata"].Value<bool>("is_rotated"), "旋转框被识别为水平框");
                            var box = (JArray)result["bbox"];
                            Require(box.Count == 5, "缺少旋转角");
                            double[] expected = { 20, 10, 20, 10, Math.PI / 2 };
                            for (int i = 0; i < expected.Length; i++) Near(box[i].Value<double>(), expected[i]);
                            Require(JToken.DeepEquals(before, input), "修改了上游框");
                            CheckStructured(result);
                        });
                    }
                }
                foreach (var flags in new[] { Tuple.Create(false, 0.4), Tuple.Create(true, -100.0) })
                    check("水平框保留 with_angle=" + flags.Item1 + " angle=" + flags.Item2, () =>
                    {
                        var result = Output(images, Entries(Detection(new JArray(30, 50, 40, 20), flags.Item1, flags.Item2)));
                        Require(!result["metadata"].Value<bool>("is_rotated"), "水平框被误判");
                        Require(JToken.DeepEquals(result["bbox"], new JArray(10, 10, 20, 30)), "水平框映射改变");
                    });
                check("裁图到输出恢复原图旋转框", () =>
                {
                    var original = new List<ModuleImage> { new ModuleImage(image, image, new TransformationState(200, 150), 0) };
                    var entries = Entries(Detection(new JArray(100, 80, 40, 20), true, -Math.PI / 4));
                    var cropped = new ImageGeneration(1).Process(original, entries);
                    try
                    {
                        Require(cropped.ImageList.Count == 1, "裁图数量错误");
                        var result = Output(cropped.ImageList, cropped.ResultList);
                        Require(result["metadata"].Value<bool>("is_rotated"), "裁图结果丢失旋转框");
                        var box = (JArray)result["bbox"];
                        Near(box[0].Value<double>(), 100, 0.001);
                        Near(box[1].Value<double>(), 80, 0.001);
                        var actualCorners = Corners(box);
                        foreach (var expected in Corners(new JArray(100, 80, 40, 20, -Math.PI / 4)))
                        {
                            double distance = double.MaxValue;
                            foreach (var actual in actualCorners)
                                distance = Math.Min(distance, Math.Sqrt(Math.Pow(actual.X - expected.X, 2) + Math.Pow(actual.Y - expected.Y, 2)));
                            Require(distance < 0.001, "旋转框顶点偏移 " + distance);
                        }
                        CheckStructured(result);
                    }
                    finally { foreach (var crop in cropped.ImageList) crop.ImageObject.Dispose(); }
                });
            }
            return failures == 0 ? 0 : 1;
        }

        private static JObject Detection(JArray box, bool withAngle, double angle) => new JObject
        {
            ["bbox"] = box, ["with_bbox"] = true, ["with_angle"] = withAngle, ["angle"] = angle,
            ["category_name"] = "315", ["category_id"] = 2, ["score"] = 0.9
        };

        private static JArray Entries(JObject det, int index = 0) => new JArray(new JObject
        {
            ["type"] = "local", ["index"] = index, ["origin_index"] = 0, ["sample_results"] = new JArray(det)
        });

        private static JObject Output(List<ModuleImage> images, JArray entries)
        {
            var context = new DlcvModules.ExecutionContext();
            new ReturnJson(2, context: context).Process(images, entries);
            var last = JObject.FromObject(context.Get<Dictionary<string, object>>("frontend_json")["last"]);
            return (JObject)last["by_image"][0]["results"][0];
        }

        private static void CheckStructured(JObject result)
        {
            using (var model = new FlowGraphModel())
            {
                var objects = new List<Utils.CSharpObjectResult>();
                typeof(FlowGraphModel).GetMethod("ParseNewFormatEntry", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(model, new object[] { result, objects, false });
                try
                {
                    Require(objects.Count == 1 && objects[0].WithAngle, "结构化结果丢失旋转标志");
                    Near(objects[0].Angle, result["bbox"][4].Value<double>(), 0.000001);
                    Require(objects[0].CategoryName == "315", "角度标签改变");
                }
                finally { foreach (var obj in objects) obj.Mask?.Dispose(); }
            }
        }

        private static List<Point2d> Corners(JArray box)
        {
            double angle = box[4].Value<double>(), c = Math.Cos(angle), s = Math.Sin(angle);
            var points = new List<Point2d>();
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
            {
                double dx = x * box[2].Value<double>() / 2, dy = y * box[3].Value<double>() / 2;
                points.Add(new Point2d(box[0].Value<double>() + c * dx - s * dy, box[1].Value<double>() + s * dx + c * dy));
            }
            return points;
        }

        private static void Near(double actual, double expected, double tolerance = 1e-8) =>
            Require(Math.Abs(actual - expected) < tolerance, "实际=" + actual + " 预期=" + expected);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
