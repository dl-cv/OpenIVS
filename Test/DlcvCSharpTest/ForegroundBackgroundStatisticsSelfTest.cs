using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DlcvModules;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class ForegroundBackgroundStatisticsSelfTest
    {
        private static readonly string[] Fields =
        {
            "with_mean", "foreground_mean", "background_mean",
            "with_median", "foreground_median", "background_median"
        };

        public static int Run()
        {
            int failures = 0;
            Action<string, Action> check = (name, test) =>
            {
                try { test(); Console.WriteLine("通过 " + name); }
                catch (Exception ex) { failures++; Console.WriteLine("失败 " + name + ": " + ex.Message); }
            };

            check("四种独立开关与默认值", TestOptions);
            check("标准流程通道及分支路由", TestFlowPortTypes);
            check("严格布尔类型", TestPropertyTypes);
            check("非有限像素与采样域", TestNonFinite);
            check("有限极大通道值不溢出", TestLargeFinite);
            check("无效变换及定位数值拒绝", TestInvalidTransforms);
            check("数组掩码阈值与非法数组", TestMaskArray);
            check("统计包装类型、独立开关与 null 往返", TestWrapperRoundTrip);
            check("公共解析入口保留默认值、中值及空侧", TestPublicParserRoundTrip);
            check("普通模型结果不补统计字段", TestModelOutput);
            check("统计文字无采样与中值", TestWrapperText);
            check("彩色全部通道共同统计", TestColor);
            check("全前景、全背景与无采样域", TestEmptySides);
            check("XYWH 位置及完整缩放后裁图", TestClippedMask);
            check("图外 bbox 完整缩放逆比例取样", TestResizeRounding);
            check("整图 RLE 与目标局部域", TestFullImageMask);
            check("结果选区逆变换至原图", TestAffine);
            check("PNG 实际裁剪和翻转链路", TestPngFlipChain);
            check("非均匀原图缩放及前置处理不影响统计", TestOriginalImageAfterResize);
            check("延迟图像仍采样原图且输入不变", TestDeferredImage);
            check("原图编号选择及错配拒绝", TestOriginIndex);
            check("弧度旋转框域", TestRotatedDomain);
            check("四个直角旋转半开域及 mask 边缘", TestCardinalRotatedDomains);
            check("重复运行清理旧字段与非 local 透传", TestRepeatedCleanup);
            Console.WriteLine(failures == 0 ? "前景背景统计节点自测通过" : "前景背景统计节点自测失败: " + failures);
            return failures == 0 ? 0 : 1;
        }

        private static void TestOptions()
        {
            // 前景 [1,3]：均值/中值 2；背景 [9,5,7,31]：均值 13，中值 8。
            using (var image = Gray(new byte[,] { { 1, 3, 9 }, { 5, 7, 31 } }))
            using (var mask = Gray(new byte[,] { { 255, 255, 0 }, { 0, 0, 0 } }))
            {
                var images = Images(image);
                var input = Entries(Detection(new JArray(0, 0, 3, 2), mask));
                foreach (bool mean in new[] { false, true })
                foreach (bool median in new[] { false, true })
                {
                    var target = Target(Execute(images, input, Options(mean, median)));
                    Statistics(target, "mean", mean, true, 2, 13);
                    Statistics(target, "median", median, true, 2, 8);
                }
                var defaults = Target(Execute(images, input));
                Statistics(defaults, "mean", true, true, 2, 13);
                Statistics(defaults, "median", false, false, null, null);
                var medianOnlyProperty = Target(Execute(images, input,
                    new Dictionary<string, object> { ["median"] = true }));
                Statistics(medianOnlyProperty, "mean", true, true, 2, 13);
                Statistics(medianOnlyProperty, "median", true, true, 2, 8);
                var meanOnlyProperty = Target(Execute(images, input,
                    new Dictionary<string, object> { ["mean"] = false }));
                Statistics(meanOnlyProperty, "mean", false, false, null, null);
                Statistics(meanOnlyProperty, "median", false, false, null, null);
                Require(ModuleRegistry.Get("post_process/foreground_background_statistics") ==
                    typeof(ForegroundBackgroundStatistics), "统计节点未注册到正式类型名称");
            }
        }

        private static void TestFlowPortTypes()
        {
            using (var image = Gray(new byte[,] { { 1, 3 } }))
            {
                var ports = new[] { "image_chan", "result_chan" };
                var context = new DlcvModules.ExecutionContext();
                context.Set("frontend_image_mat", image);
                var nodes = new JArray
                {
                    new JObject { ["id"] = 1, ["type"] = "input/frontend_image", ["outputs"] = new JArray
                    {
                        new JObject { ["type"] = ports[0], ["links"] = new JArray(1) },
                        new JObject { ["type"] = ports[1], ["links"] = new JArray(2) }
                    } },
                    new JObject { ["id"] = 2, ["type"] = "input/build_results",
                        ["properties"] = new JObject { ["bbox_x"] = 0, ["bbox_y"] = 0, ["bbox_w"] = 2, ["bbox_h"] = 1 },
                        ["inputs"] = new JArray
                        {
                            new JObject { ["type"] = ports[0], ["link"] = 1 },
                            new JObject { ["type"] = ports[1], ["link"] = 2 }
                        }, ["outputs"] = new JArray
                        {
                            new JObject { ["type"] = ports[0], ["links"] = new JArray(7) },
                            new JObject { ["type"] = ports[1], ["links"] = new JArray(8) }
                        } },
                    new JObject { ["id"] = 3, ["type"] = "post_process/foreground_background_statistics",
                        ["properties"] = new JObject { ["mean"] = true, ["median"] = true },
                        ["inputs"] = new JArray
                        {
                            new JObject { ["type"] = ports[0], ["link"] = 7 },
                            new JObject { ["type"] = ports[1], ["link"] = 8 }
                        }, ["outputs"] = new JArray
                        {
                            new JObject { ["type"] = ports[0], ["links"] = new JArray(3, 5) },
                            new JObject { ["type"] = ports[1], ["links"] = new JArray(4, 6) }
                        } }
                };
                foreach (int id in new[] { 4, 5 })
                    nodes.Add(new JObject { ["id"] = id,
                        ["type"] = id == 4 ? "output/preview" : "output/return_json",
                        ["inputs"] = new JArray
                        {
                            new JObject { ["type"] = ports[0], ["link"] = id == 4 ? 3 : 5 },
                            new JObject { ["type"] = ports[1], ["link"] = id == 4 ? 4 : 6 }
                        } });
                var output = new GraphExecutor(nodes.ToObject<List<Dictionary<string, object>>>(), context).Run();
                foreach (int id in new[] { 3, 4, 5 })
                {
                    var images = (List<ModuleImage>)output[id]["image_list"];
                    var results = (JArray)output[id]["result_list"];
                    Require(images.Count == 1 && ReferenceEquals(images[0].OriginalImage, image), ports[0] + " 图像路由丢失");
                    Require(results.Count == 1 && results[0].Value<string>("type") == "local"
                        && ((JArray)results[0]["sample_results"]).Count == 1, ports[1] + " 结果路由丢失");
                    Statistics(Target(results), "mean", true, false, null, null);
                    Statistics(Target(results), "median", true, false, null, null);
                }
            }
        }

        private static void TestPropertyTypes()
        {
            using (var image = Gray(new byte[,] { { 1, 3 } }))
            using (var mask = Gray(new byte[,] { { 255, 0 } }))
            {
                foreach (string name in new[] { "mean", "median" })
                foreach (object value in new object[] { "true", "false", 0, 1, 1.0, null, new JArray(true) })
                {
                    var properties = Options(false, false);
                    properties[name] = value;
                    Reject(() => Execute(Images(image), Entries(Detection(new JArray(0, 0, 2, 1), mask)), properties),
                        name + " 接受了非布尔类型");
                }
            }
        }

        private static void TestNonFinite()
        {
            using (var image = new Mat(1, 3, MatType.CV_64FC1))
            using (var mask = Gray(new byte[,] { { 255, 0 } }))
            {
                image.Set(0, 0, 2.0);
                image.Set(0, 1, 6.0);
                foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    image.Set(0, 2, value);
                    var images = Images(image);
                    var inside = Entries(Detection(new JArray(1, 0, 2, 1), mask));
                    foreach (var options in new[] { Options(true, false), Options(false, true), Options(true, true) })
                        Reject(() => Execute(images, inside, options), "采样域中的非有限像素未拒绝");
                    var outside = Target(Execute(images,
                        Entries(Detection(new JArray(0, 0, 2, 1), mask)), Options(true, true)));
                    Statistics(outside, "mean", true, true, 2, 6);
                    Statistics(outside, "median", true, true, 2, 6);
                }
            }
        }

        private static void TestLargeFinite()
        {
            using (var mask = new Mat(1, 1, MatType.CV_8UC1, Scalar.White))
            foreach (int channels in new[] { 3, 4 })
            using (var image = new Mat(1, 1, channels == 3 ? MatType.CV_64FC3 : MatType.CV_64FC4))
            {
                foreach (double value in new[] { double.MaxValue, -double.MaxValue, double.Epsilon, -double.Epsilon, 0.0 })
                {
                    if (channels == 3) image.Set(0, 0, new Vec3d(value, value, value));
                    else image.Set(0, 0, new Vec4d(value, value, value, value));
                    var target = Target(Execute(Images(image),
                        Entries(Detection(new JArray(0, 0, 1, 1), mask)), Options(true, true)));
                    Statistics(target, "mean", true, true, value, null);
                    Statistics(target, "median", true, true, value, null);
                    Require(target.Value<double>("foreground_mean") == value && target.Value<double>("foreground_median") == value,
                        "有限极值统计发生溢出或下溢");
                }
            }
        }

        private static void TestInvalidTransforms()
        {
            using (var image = Gray(new byte[,] { { 1, 3 } }))
            using (var mask = Gray(new byte[,] { { 255, 0 } }))
            {
                var state = new TransformationState(2, 1);
                var valid = JObject.FromObject(state.ToDict());
                Action<string, string, JToken> invalidSource = (name, field, value) =>
                {
                    var transform = (JObject)valid.DeepClone();
                    transform[field] = value;
                    var input = Entries(Detection(new JArray(0, 0, 2, 1), mask));
                    input[0]["transform"] = transform;
                    Reject(() => Execute(Images(image), input, Options(true, true)), name);
                };
                invalidSource("缺少原图尺寸未拒绝", "original_width", JValue.CreateNull());
                invalidSource("原图尺寸错配未拒绝", "original_width", new JValue(3));
                invalidSource("原图尺寸非整数未拒绝", "original_width", new JValue(2.5));
                invalidSource("输出尺寸为零未拒绝", "output_size", new JArray(0, 1));
                invalidSource("输出尺寸非整数未拒绝", "output_size", new JArray(2, 1.5));
                invalidSource("输出尺寸长度错误未拒绝", "output_size", new JArray(2));
                invalidSource("仿射矩阵长度错误未拒绝", "affine_2x3", new JArray(1, 0));
                invalidSource("仿射矩阵字符串未拒绝", "affine_2x3", new JArray("1", 0, 0, 0, 1, 0));
                invalidSource("不可逆仿射矩阵未拒绝", "affine_2x3", new JArray(1, 0, 0, 0, 0, 0));
                foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    invalidSource("非有限仿射矩阵未拒绝", "affine_2x3", new JArray(1, 0, value, 0, 1, 0));
                    Reject(() => Execute(Images(image), Entries(Detection(new JArray(value, 0, 2, 1), mask)),
                        Options(true, true)), "非有限 bbox 未拒绝");
                }
                var wrongType = Entries(Detection(new JArray(0, 0, 2, 1), mask));
                wrongType[0]["transform"] = new JArray(1, 2);
                Reject(() => Execute(Images(image), wrongType, Options(true, true)), "非对象 transform 未拒绝");
                foreach (var invalid in new[]
                {
                    new TransformationState(3, 1),
                    new TransformationState(2, 1, outputSize: new[] { 2 }),
                    new TransformationState(2, 1, affine2x3: new double[] { 1, 0 }),
                    new TransformationState(2, 1, affine2x3: new double[] { 1, 0, 0, 0, 0, 0 }),
                    new TransformationState(2, 1, affine2x3: new double[] { 1, 0, double.NaN, 0, 1, 0 })
                })
                {
                    var images = new List<ModuleImage> { new ModuleImage(image, image, invalid, 0) };
                    Reject(() => Execute(images, Entries(Detection(new JArray(0, 0, 2, 1), mask)),
                        Options(true, true)), "无效当前图像 transform 未拒绝");
                }
            }
        }

        private static void TestMaskArray()
        {
            using (var image = Gray(new byte[,] { { 10, 20, 90 } }))
            using (var conflictingRle = new Mat(1, 3, MatType.CV_8UC1, Scalar.White))
            {
                var det = Detection(new JArray(0, 0, 3, 1), conflictingRle);
                det["mask_array"] = new JArray { new JArray(1, 127, 128) };
                var target = Target(Execute(Images(image), Entries(det), Options(true, true)));
                Statistics(target, "mean", true, true, 90, 15);
                Statistics(target, "median", true, true, 90, 15);
                foreach (JToken array in new JToken[]
                {
                    new JArray(), new JArray { new JArray() }, new JArray(1, 127, 128),
                    new JArray { new JArray(1, 127), new JArray(128) },
                    new JArray { new JArray(-1, 127, 128) }, new JArray { new JArray(1, 127, 256) },
                    new JArray { new JArray(1, 127, 128.0) }, new JArray { new JArray(1, 127, "128") },
                    new JArray { new JArray(1, 127, true) }, new JArray { new JArray(1, 127, JValue.CreateNull()) },
                    new JObject(), JValue.CreateNull()
                })
                {
                    det["mask_array"] = array;
                    target = Target(Execute(Images(image), Entries(det), Options(true, true)));
                    Statistics(target, "mean", true, false, null, null);
                    Statistics(target, "median", true, false, null, null);
                }
            }
        }

        private static void TestWrapperRoundTrip()
        {
            foreach (string property in new[] { "WithMean", "WithMedian" })
                Require(typeof(Utils.CSharpObjectResult).GetProperty(property).PropertyType == typeof(bool), property + " 类型异常");
            foreach (string property in new[] { "ForegroundMean", "BackgroundMean", "ForegroundMedian", "BackgroundMedian" })
                Require(typeof(Utils.CSharpObjectResult).GetProperty(property).PropertyType == typeof(double), property + " 类型异常");
            var defaults = Wrapper();
            Require(!defaults.WithMean && !defaults.WithMedian && defaults.ForegroundMean == 0 && defaults.BackgroundMean == 0 &&
                defaults.ForegroundMedian == 0 && defaults.BackgroundMedian == 0, "原有默认构造函数初始化异常");
            CheckRoundTrip(defaults, new JObject());
            var original = new Utils.CSharpObjectResult(2, "目标", 0.9f, 0, new List<double> { 0, 0, 3, 2 }, false, null,
                true, false, -100, null, true, 2, 13);
            Require(original.WithMean && original.ForegroundMean == 2 && original.BackgroundMean == 13 &&
                !original.WithMedian && original.ForegroundMedian == 0 && original.BackgroundMedian == 0,
                "原有均值构造函数初始化异常");
            CheckRoundTrip(original, new JObject { ["with_mean"] = true, ["foreground_mean"] = 2.0, ["background_mean"] = 13.0 });
            using (var image = Gray(new byte[,] { { 1, 3, 9 }, { 5, 7, 31 } }))
            using (var mask = Gray(new byte[,] { { 255, 255, 0 }, { 0, 0, 0 } }))
            {
                foreach (bool mean in new[] { false, true })
                foreach (bool median in new[] { false, true })
                {
                    var target = Target(Execute(Images(image), Entries(Detection(new JArray(0, 0, 3, 2), mask)), Options(mean, median)));
                    var wrapper = Wrapper();
                    wrapper.ReadStatistics(target);
                    Require(wrapper.WithMean == mean && wrapper.WithMedian == median, "包装均值与中值开关异常");
                    Require(wrapper.ForegroundMean == (mean ? 2 : 0) && wrapper.BackgroundMean == (mean ? 13 : 0),
                        "包装均值数值异常");
                    Require(wrapper.ForegroundMedian == (median ? 2 : 0) && wrapper.BackgroundMedian == (median ? 8 : 0),
                        "包装中值数值异常");
                    CheckRoundTrip(wrapper, target);
                }
            }
            var reused = Wrapper();
            foreach (var target in new[]
            {
                new JObject { ["with_mean"] = false, ["foreground_mean"] = null, ["background_mean"] = null,
                    ["with_median"] = false, ["foreground_median"] = null, ["background_median"] = null },
                new JObject { ["with_mean"] = true, ["foreground_mean"] = 0.0, ["background_mean"] = null,
                    ["with_median"] = true, ["foreground_median"] = 0.0, ["background_median"] = null },
                new JObject { ["with_mean"] = false, ["foreground_mean"] = 0.0, ["background_mean"] = 0.0 },
                new JObject()
            })
            {
                reused.ReadStatistics(target);
                CheckRoundTrip(reused, target);
            }
            Require(!reused.WithMean && !reused.WithMedian && reused.ForegroundMean == 0 && reused.BackgroundMean == 0 &&
                reused.ForegroundMedian == 0 && reused.BackgroundMedian == 0,
                "缺少统计字段后包装对象保留了旧统计");
        }

        private static void TestPublicParserRoundTrip()
        {
            using (var model = new dlcv_infer_csharp.Model())
            {
                foreach (var statistics in new[]
                {
                    new JObject(),
                    new JObject { ["with_mean"] = false, ["foreground_mean"] = 0.0, ["background_mean"] = 0.0 },
                    new JObject { ["with_median"] = true, ["foreground_median"] = 2.5, ["background_median"] = null },
                    new JObject { ["with_mean"] = true, ["foreground_mean"] = null, ["background_mean"] = 4.5,
                        ["with_median"] = true, ["foreground_median"] = null, ["background_median"] = 3.5 },
                    new JObject { ["with_mean"] = true, ["foreground_mean"] = 0.0, ["background_mean"] = null },
                    new JObject { ["with_mean"] = true, ["with_median"] = true },
                    new JObject { ["with_mean"] = false, ["foreground_mean"] = null, ["background_mean"] = 0.0 },
                    new JObject { ["with_median"] = false, ["foreground_median"] = 0.0, ["background_median"] = null },
                    new JObject { ["with_mean"] = false, ["foreground_mean"] = null, ["background_mean"] = null,
                        ["with_median"] = false, ["foreground_median"] = null, ["background_median"] = null }
                })
                {
                    var target = (JObject)statistics.DeepClone();
                    target["bbox"] = new JArray(0, 0, 1, 1);
                    target["with_mask"] = false;
                    var input = new JObject { ["sample_results"] = new JArray(
                        new JObject { ["results"] = new JArray(target) }) };
                    var parsed = model.ParseToStructResult(input).SampleResults[0].Results[0];
                    var normalize = typeof(Model).GetMethod("StandardizeJsonOutput", BindingFlags.NonPublic | BindingFlags.Instance);
                    var flowMode = typeof(Model).GetField("_isDvsMode", BindingFlags.NonPublic | BindingFlags.Instance);
                    JObject normalized;
                    flowMode.SetValue(model, true);
                    try { normalized = (JObject)normalize.Invoke(model, new object[] { target, false }); }
                    finally { flowMode.SetValue(model, false); }
                    foreach (string key in Fields)
                        Require(JToken.DeepEquals(target[key], normalized[key]), "JSON 标准化改变原有统计字段");
                    try
                    {
                        Require(parsed.WithMean == (target.Value<bool?>("with_mean") ?? false) &&
                            parsed.WithMedian == (target.Value<bool?>("with_median") ?? false), "公共解析改变统计开关");
                        foreach (var statistic in new[]
                        {
                            new KeyValuePair<string, double>("foreground_mean", parsed.ForegroundMean),
                            new KeyValuePair<string, double>("background_mean", parsed.BackgroundMean),
                            new KeyValuePair<string, double>("foreground_median", parsed.ForegroundMedian),
                            new KeyValuePair<string, double>("background_median", parsed.BackgroundMedian)
                        })
                        {
                            JToken value = target[statistic.Key];
                            Require(value?.Type == JTokenType.Null ? double.IsNaN(statistic.Value) :
                                statistic.Value == (value?.Value<double>() ?? 0.0), "公共解析数值异常: " + statistic.Key);
                        }
                        CheckRoundTrip(parsed, target);
                    }
                    finally { parsed.Mask?.Dispose(); }
                }
            }
        }

        private static void TestModelOutput()
        {
            var target = new JObject
            {
                ["bbox"] = new JArray(0, 0, 1, 1), ["score"] = 0.75,
                ["with_mean"] = false, ["foreground_mean"] = 0.0, ["background_mean"] = 0.0
            };
            using (var model = new Model())
            {
                var normalize = typeof(Model).GetMethod("StandardizeJsonOutput", BindingFlags.NonPublic | BindingFlags.Instance);
                var result = (JObject)normalize.Invoke(model, new object[] { target, false });
                foreach (string key in Fields) Require(result[key] == null, "普通模型 JSON 补入统计字段: " + key);
                Require(result.Value<double>("score") == 0.75, "普通模型结果内容改变");
            }
            var sample = new Utils.CSharpSampleResult(new List<Utils.CSharpObjectResult> { new Utils.CSharpObjectResult() });
            var convert = typeof(DetModel).GetMethod("ConvertToLocalSamples", BindingFlags.NonPublic | BindingFlags.Static);
            var local = (JArray)convert.Invoke(null, new object[] { sample });
            foreach (string key in Fields) Require(local[0][key] == null, "模型节点补入统计字段: " + key);
        }

        private static void TestWrapperText()
        {
            var wrapper = Wrapper();
            wrapper.ReadStatistics(new JObject
            {
                ["with_mean"] = false, ["foreground_mean"] = null, ["background_mean"] = null,
                ["with_median"] = false, ["foreground_median"] = null, ["background_median"] = null
            });
            string text = wrapper.ToString();
            Require(text.Contains("前景均值: 无采样") && text.Contains("背景均值: 无采样") &&
                text.Contains("前景中值: 无采样") && text.Contains("背景中值: 无采样"), "无采样文字缺失");
            wrapper.ReadStatistics(new JObject
            {
                ["with_median"] = true, ["foreground_median"] = 2.5, ["background_median"] = null
            });
            text = wrapper.ToString();
            Require(!text.Contains("均值") && text.Contains("前景中值: " + 2.5.ToString("F4")) &&
                text.Contains("背景中值: 无采样"), "中值单独展示或空侧文字异常");
            wrapper.ReadStatistics(new JObject
            {
                ["with_mean"] = true, ["foreground_mean"] = 0.0, ["background_mean"] = null
            });
            text = wrapper.ToString();
            Require(text.Contains("前景均值: " + 0.0.ToString("F4")) && text.Contains("背景均值: 无采样"), "零值被误显示为无采样");
            wrapper.ReadStatistics(new JObject());
            text = wrapper.ToString();
            Require(!text.Contains("均值") && !text.Contains("中值") && !text.Contains("无采样"), "关闭统计后仍显示统计文字");
        }

        private static Utils.CSharpObjectResult Wrapper()
        {
            return new Utils.CSharpObjectResult(2, "目标", 0.9f, 0, new List<double> { 0, 0, 3, 2 }, false, null);
        }

        private static void CheckRoundTrip(Utils.CSharpObjectResult wrapper, JObject target)
        {
            var written = new JObject { ["marker"] = "保留其他内容" };
            foreach (string field in Fields)
                written[field] = field.StartsWith("with_", StringComparison.Ordinal) ? (JToken)new JValue(true) : new JValue(-1);
            wrapper.WriteStatistics(written);
            Require(written.Value<string>("marker") == "保留其他内容", "写统计时改写其他字段");
            written.Remove("marker");
            var expected = new JObject();
            foreach (string group in new[] { "mean", "median" })
            {
                string with = "with_" + group, foreground = "foreground_" + group, background = "background_" + group;
                if ((target.Value<bool?>(with) ?? false) || target[foreground]?.Type == JTokenType.Null || target[background]?.Type == JTokenType.Null)
                {
                    expected[with] = target.Value<bool?>(with) ?? false;
                    expected[foreground] = target[foreground]?.DeepClone() ?? new JValue(0.0);
                    expected[background] = target[background]?.DeepClone() ?? new JValue(0.0);
                }
            }
            written = JObject.Parse(written.ToString(Newtonsoft.Json.Formatting.None));
            Require(JToken.DeepEquals(expected, written), "统计 JSON 往返改变开关、null 或未计算字段");
            var result = new Utils.CSharpResult(new List<Utils.CSharpSampleResult>
                { new Utils.CSharpSampleResult(new List<Utils.CSharpObjectResult> { wrapper }) });
            var visual = (JObject)Utils.ConvertToVisualizeFormat(result)[0]["sample_results"][0];
            var visualStatistics = new JObject();
            foreach (string field in Fields)
                if (visual[field] != null) visualStatistics[field] = visual[field].DeepClone();
            Require(JToken.DeepEquals(expected, visualStatistics), "可视化格式改变统计开关或 null");
            var signatureMethod = typeof(Program).GetMethod("BuildStructuredResultSignature", BindingFlags.NonPublic | BindingFlags.Static);
            var signature = JArray.Parse((string)signatureMethod.Invoke(null, new object[] { result }));
            var signatureStatistics = new JObject();
            foreach (string field in Fields)
                if (signature[0]["results"][0][field] != null) signatureStatistics[field] = signature[0]["results"][0][field].DeepClone();
            Require(JToken.DeepEquals(expected, signatureStatistics), "一致性签名改变统计开关或 null");
            var printMethod = typeof(Program).GetMethod("PrintStructuredResult", BindingFlags.NonPublic | BindingFlags.Static);
            var previousOut = Console.Out;
            using (var capture = new StringWriter(CultureInfo.InvariantCulture))
            {
                try
                {
                    Console.SetOut(capture);
                    printMethod.Invoke(null, new object[] { result });
                }
                finally { Console.SetOut(previousOut); }
                string printed = capture.ToString();
                Require(expected.HasValues
                    ? printed.Contains("statistics=" + expected.ToString(Newtonsoft.Json.Formatting.None))
                    : !printed.Contains("statistics="), "CLI 统计诊断改变统计开关或 null");
            }
            var resolveDemo = typeof(Program).GetMethod("ResolveDemoAssemblyPath", BindingFlags.NonPublic | BindingFlags.Static);
            var demoAssembly = Assembly.LoadFrom((string)resolveDemo.Invoke(null, null));
            var cliType = demoAssembly.GetType("DlcvDemo.CliRunner", true);
            var structuredMethod = cliType.GetMethod("BuildStructuredSummary", BindingFlags.NonPublic | BindingFlags.Static);
            var jsonMethod = cliType.GetMethod("BuildJsonSummary", BindingFlags.NonPublic | BindingFlags.Static);
            var consistentMethod = cliType.GetMethod("AreConsistent", BindingFlags.NonPublic | BindingFlags.Static);
            var jsonTarget = (JObject)target.DeepClone();
            jsonTarget["score"] = wrapper.Score;
            jsonTarget["category_name"] = wrapper.CategoryName;
            jsonTarget["with_mask"] = wrapper.WithMask;
            var structuredSummary = structuredMethod.Invoke(null, new object[] { result, 0.5 });
            var jsonSummary = jsonMethod.Invoke(null, new object[] { new JArray(jsonTarget), 0.5 });
            Require((bool)consistentMethod.Invoke(null, new[] { structuredSummary, jsonSummary }), "CLI 两路统计摘要不一致");
            foreach (string field in Fields)
            {
                var mismatched = (JObject)jsonTarget.DeepClone();
                if (field.StartsWith("with_", StringComparison.Ordinal))
                    mismatched[field] = !(jsonTarget.Value<bool?>(field) ?? false);
                else
                    mismatched[field] = jsonTarget[field]?.Type == JTokenType.Null ? new JValue(0.0) : JValue.CreateNull();
                var mismatchSummary = jsonMethod.Invoke(null, new object[] { new JArray(mismatched), 0.5 });
                Require(!(bool)consistentMethod.Invoke(null, new[] { structuredSummary, mismatchSummary }),
                    "CLI 未识别统计开关或空值差异: " + field);
            }
            if (!expected.HasValues)
            {
                var reflectedSummary = Activator.CreateInstance(structuredSummary.GetType());
                reflectedSummary.GetType().GetMethod("Add").Invoke(reflectedSummary, new object[]
                {
                    wrapper.Score, wrapper.CategoryName, wrapper.WithMask, false, 0.0, 0.0, false, 0.0, 0.0, 0.5
                });
                Require((bool)consistentMethod.Invoke(null, new[] { structuredSummary, reflectedSummary }),
                    "十参数反射调用的默认统计值异常");
            }
            foreach (var summary in new[] { structuredSummary, jsonSummary })
            {
                var summaryJson = (JObject)summary.GetType().GetMethod("ToJson").Invoke(summary, null);
                summaryJson = JObject.Parse(summaryJson.ToString(Newtonsoft.Json.Formatting.None));
                Require(JToken.DeepEquals(expected, summaryJson["statistics"][0]), "CLI 摘要写回改变开关或 null");
                foreach (string field in Fields)
                    Require(expected[field] == null ? summaryJson[field] == null :
                        JToken.DeepEquals(expected[field], summaryJson[field]?[0]), "CLI 统计数组数值异常: " + field);
            }
            var parsed = Wrapper();
            parsed.ReadStatistics(written);
            Require(parsed.WithMean == wrapper.WithMean && parsed.WithMedian == wrapper.WithMedian &&
                parsed.ForegroundMean.Equals(wrapper.ForegroundMean) && parsed.BackgroundMean.Equals(wrapper.BackgroundMean) &&
                parsed.ForegroundMedian.Equals(wrapper.ForegroundMedian) && parsed.BackgroundMedian.Equals(wrapper.BackgroundMedian),
                "二次读取统计字段数值改变");
        }

        private static void TestColor()
        {
            using (var image = new Mat(1, 3, MatType.CV_8UC3))
            using (var mask = Gray(new byte[,] { { 255, 255, 0 } }))
            {
                image.Set(0, 0, new Vec3b(0, 10, 80));
                image.Set(0, 1, new Vec3b(30, 40, 60));
                image.Set(0, 2, new Vec3b(90, 120, 150));
                // 前景六个通道值和 220，中间两个值 30、40；不转换灰度。
                var target = Target(Execute(Images(image), Entries(Detection(new JArray(0, 0, 3, 1), mask)), Options(true, true)));
                Statistics(target, "mean", true, true, 110.0 / 3, 120);
                Statistics(target, "median", true, true, 35, 120);
            }
        }

        private static void TestEmptySides()
        {
            using (var image = Gray(new byte[,] { { 1, 3, 9 }, { 5, 7, 31 } }))
            using (var full = new Mat(2, 3, MatType.CV_8UC1, Scalar.White))
            using (var zero = new Mat(2, 3, MatType.CV_8UC1, Scalar.Black))
            using (var empty = new Mat())
            {
                var images = Images(image);
                var box = new JArray(0, 0, 3, 2);
                var foreground = Target(Execute(images, Entries(Detection(box, full)), Options(true, true)));
                Statistics(foreground, "mean", true, true, 28.0 / 3, null);
                Statistics(foreground, "median", true, true, 6, null);
                var foregroundWrapper = Wrapper();
                foregroundWrapper.ReadStatistics(foreground);
                CheckRoundTrip(foregroundWrapper, foreground);
                var background = Target(Execute(images, Entries(Detection(box, zero)), Options(true, true)));
                Statistics(background, "mean", true, true, null, 28.0 / 3);
                Statistics(background, "median", true, true, null, 6);
                var backgroundWrapper = Wrapper();
                backgroundWrapper.ReadStatistics(background);
                CheckRoundTrip(backgroundWrapper, background);
                foreach (var det in new[]
                {
                    Detection(box, empty), Detection(box, null),
                    Detection(new JArray(10, 0, 3, 2), full),
                    Detection(new JArray(0, 0, 0, 2), full)
                })
                {
                    var target = Target(Execute(images, Entries(det), Options(true, true)));
                    Statistics(target, "mean", true, false, null, null);
                    Statistics(target, "median", true, false, null, null);
                    var wrapper = Wrapper();
                    wrapper.ReadStatistics(target);
                    Require(!wrapper.WithMean && !wrapper.WithMedian && double.IsNaN(wrapper.ForegroundMean) &&
                        double.IsNaN(wrapper.BackgroundMean) && double.IsNaN(wrapper.ForegroundMedian) && double.IsNaN(wrapper.BackgroundMedian),
                        "无采样结果未保留 false 与 NaN");
                    CheckRoundTrip(wrapper, target);
                }
            }
        }

        private static void TestClippedMask()
        {
            using (var image = Gray(new byte[,] { { 10, 20, 30, 40 } }))
            using (var mask = Gray(new byte[,] { { 255, 0 } }))
            {
                // bbox [-1,0,4,1]：完整 mask 放大成 [255,255,0,0]，裁掉首像素。
                var target = Target(Execute(Images(image), Entries(Detection(new JArray(-1, 0, 4, 1), mask)), Options(true, true)));
                Statistics(target, "mean", true, true, 10, 25);
                Statistics(target, "median", true, true, 10, 25);
                // x=1、width=2，仅取原图的 20、30，不能把 width 当右端坐标。
                target = Target(Execute(Images(image), Entries(Detection(new JArray(1, 0, 2, 1), mask)), Options(true, true)));
                Statistics(target, "mean", true, true, 20, 30);
                Statistics(target, "median", true, true, 20, 30);
            }
        }

        private static void TestResizeRounding()
        {
            using (var image = new Mat(1, 17, MatType.CV_8UC1, new Scalar(100)))
            using (var mask = Gray(new byte[,] { { 0, 0, 0, 255, 255, 255 } }))
            {
                image.Set(0, 0, (byte)10);
                // 完整宽 34 放大六列，裁掉左侧 17 列；首个可见位置仍取源第 2 列背景。
                var target = Target(Execute(Images(image),
                    Entries(Detection(new JArray(-17, 0, 34, 1), mask)), Options(true, true)));
                Statistics(target, "mean", true, true, 100, 10);
                Statistics(target, "median", true, true, 100, 10);
            }
        }

        private static void TestFullImageMask()
        {
            using (var image = Gray(new byte[,] { { 10, 20, 30, 40 } }))
            using (var mask = Gray(new byte[,] { { 0, 255, 0, 255 } }))
            {
                var target = Target(Execute(Images(image), Entries(Detection(new JArray(1, 0, 2, 1), mask)), Options(true, true)));
                Statistics(target, "mean", true, true, 20, 30);
                Statistics(target, "median", true, true, 20, 30);
            }
        }

        private static void TestAffine()
        {
            using (var original = Gray(new byte[,] { { 100, 101, 102, 103, 104 } }))
            using (var current = Gray(new byte[,] { { 7, 50, 90 } }))
            using (var mask = Gray(new byte[,] { { 255, 0 } }))
            {
                // OpenIVS affine_2x3 已包含裁图平移；结果图 x=1 对应原图 x=2，不采样当前图的 7、50。
                var currentState = new TransformationState(5, 1, new[] { 2, 0, 3, 1 },
                    new double[] { 1, 0, -2, 0, 1, 0 }, new[] { 3, 1 });
                var sourceState = new TransformationState(5, 1, new[] { 1, 0, 4, 1 },
                    new double[] { 1, 0, -1, 0, 1, 0 }, new[] { 4, 1 });
                var images = new List<ModuleImage> { new ModuleImage(current, original, currentState, 7) };
                var target = Target(Execute(images,
                    Entries(Detection(new JArray(1, 0, 2, 1), mask), 0, 7, sourceState), Options(true, true)));
                Statistics(target, "mean", true, true, 102, 103);
                Statistics(target, "median", true, true, 102, 103);
                foreach (var identitySource in new TransformationState[] { null, new TransformationState(5, 1) })
                {
                    target = Target(Execute(images,
                        Entries(Detection(new JArray(2, 0, 2, 1), mask), 0, 7, identitySource), Options(true, true)));
                    Statistics(target, "mean", true, true, 102, 103);
                    Statistics(target, "median", true, true, 102, 103);
                }
                // 结果图为原图二倍宽，仅逆缩放回原图，不复合当前图裁图平移。
                var scaledSource = new TransformationState(5, 1, affine2x3: new double[] { 2, 0, 0, 0, 1, 0 },
                    outputSize: new[] { 10, 1 });
                target = Target(Execute(images,
                    Entries(Detection(new JArray(4, 0, 4, 1), mask), 0, 7, scaledSource), Options(true, true)));
                Statistics(target, "mean", true, true, 102, 103);
                Statistics(target, "median", true, true, 102, 103);
            }
        }

        private static void TestPngFlipChain()
        {
            string temp = Path.GetFullPath(Path.GetTempPath());
            string directory = Path.GetFullPath(Path.Combine(temp, "dlcv-statistics-flip-" + Guid.NewGuid().ToString("N")));
            Require(directory.StartsWith(temp, StringComparison.OrdinalIgnoreCase), "测试目录必须位于系统临时目录");
            Directory.CreateDirectory(directory);
            try
            {
                foreach (bool cropped in new[] { false, true })
                foreach (string[] directions in new[] { new string[0], new[] { "水平" }, new[] { "竖直" }, new[] { "水平", "竖直" } })
                using (var source = Gray(new byte[,] { { 1, 10, 100 }, { 2, 20, 200 } }))
                using (var canvas = new Mat(4, 5, MatType.CV_8UC3, Scalar.All(240)))
                using (var expected = new Mat())
                {
                    Cv2.CvtColor(source, expected, ColorConversionCodes.GRAY2RGB);
                    using (var region = new Mat(canvas, new Rect(1, 1, 3, 2))) expected.CopyTo(region);
                    string path = Path.Combine(directory, "input.png");
                    Require(Cv2.ImWrite(path, cropped ? canvas : expected), "PNG 写入失败");
                    var owned = new HashSet<Mat>();
                    try
                    {
                        ModuleIO current = new InputImage(1, properties: new Dictionary<string, object> { ["path"] = path }).Generate();
                        Require(current.ImageList.Count == 1 && current.ImageList[0].ImageObject.Type() == MatType.CV_8UC3,
                            "PNG 未经过真实三通道输入模块");
                        owned.Add(current.ImageList[0].ImageObject);
                        if (cropped)
                        {
                            current = new CoordinateCrop(2, properties: new Dictionary<string, object>
                                { ["x"] = 1, ["y"] = 1, ["w"] = 3, ["h"] = 2 }).Process(current.ImageList, current.ResultList);
                            owned.Add(current.ImageList[0].ImageObject);
                        }
                        foreach (string direction in directions)
                        {
                            current = new ImageFlip(3, properties: new Dictionary<string, object> { ["direction"] = direction })
                                .Process(current.ImageList, current.ResultList);
                            owned.Add(current.ImageList[0].ImageObject);
                            Cv2.Flip(expected, expected, direction == "水平" ? FlipMode.Y : FlipMode.X);
                        }
                        var image = current.ImageList[0];
                        Require(EqualBytes(Bytes(expected), Bytes(image.ImageObject)), "实际裁剪翻转像素错误");
                        foreach (bool mixed in new[] { false, true })
                        using (var mask = new Mat(2, 3, MatType.CV_8UC1, Scalar.All(255)))
                        {
                            if (mixed) Cv2.InRange(image.ImageObject, Scalar.All(0), Scalar.All(20), mask);
                            var input = Entries(Detection(new JArray(0, 0, 3, 2), mask), state: image.TransformState);
                            var target = Target(Execute(current.ImageList, input, Options(true, true)));
                            Statistics(target, "mean", true, true, mixed ? 8.25 : 55.5, mixed ? (double?)150 : null);
                            Statistics(target, "median", true, true, mixed ? 6 : 15, mixed ? (double?)150 : null);
                        }
                    }
                    finally { foreach (var image in owned) image.Dispose(); }
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void TestOriginalImageAfterResize()
        {
            using (var original = Gray(new byte[,] { { 0, 10, 30, 200 } }))
            using (var resized = new Mat())
            using (var processed = new Mat(1, 8, MatType.CV_8UC1, new Scalar(255)))
            using (var mask = Gray(new byte[,] { { 255, 255, 255, 255, 0, 0, 0, 0 } }))
            {
                Cv2.Resize(original, resized, new Size(8, 1), 0, 0, InterpolationFlags.Linear);
                var state = new TransformationState(4, 1,
                    affine2x3: new double[] { 2, 0, 0, 0, 1, 0 }, outputSize: new[] { 8, 1 });
                // 原图前景 [0,10]，背景 [30,200]；插值值与前置处理后的 255 均不能参与。
                foreach (Mat current in new[] { resized, processed })
                {
                    var output = Execute(new List<ModuleImage> { new ModuleImage(current, original, state) },
                        Entries(Detection(new JArray(0, 0, 8, 1), mask), state: state), Options(true, true));
                    Statistics(Target(output), "mean", true, true, 5, 115);
                    Statistics(Target(output), "median", true, true, 5, 115);
                }
            }
            using (var original = new Mat(1, 4, MatType.CV_8UC3))
            using (var processed = new Mat(1, 8, MatType.CV_8UC1, new Scalar(255)))
            using (var mask = Gray(new byte[,] { { 255, 255, 255, 255, 0, 0, 0, 0 } }))
            {
                original.Set(0, 0, new Vec3b(0, 10, 80));
                original.Set(0, 1, new Vec3b(30, 40, 60));
                original.Set(0, 2, new Vec3b(90, 120, 150));
                original.Set(0, 3, new Vec3b(200, 210, 220));
                var state = new TransformationState(4, 1,
                    affine2x3: new double[] { 2, 0, 0, 0, 1, 0 }, outputSize: new[] { 8, 1 });
                // 即使当前图已转单通道，仍取原图三通道；前景和220，背景和990。
                var output = Execute(new List<ModuleImage> { new ModuleImage(processed, original, state) },
                    Entries(Detection(new JArray(0, 0, 8, 1), mask), state: state), Options(true, true));
                Statistics(Target(output), "mean", true, true, 110.0 / 3, 165);
                Statistics(Target(output), "median", true, true, 35, 175);
            }
        }

        private static void TestDeferredImage()
        {
            using (var original = Gray(new byte[,] { { 100, 101, 102, 103, 104 } }))
            using (var mask = Gray(new byte[,] { { 255, 0 } }))
            using (var empty = new Mat())
            {
                var current = new TransformationState(5, 1, new[] { 2, 0, 3, 1 },
                    new double[] { 1, 0, -2, 0, 1, 0 }, new[] { 3, 1 });
                var source = new TransformationState(5, 1, new[] { 1, 0, 4, 1 },
                    new double[] { 1, 0, -1, 0, 1, 0 }, new[] { 4, 1 });
                foreach (Mat pixels in new Mat[] { null, empty })
                {
                    var wrap = new ModuleImage(pixels, original, current, 7);
                    var output = Execute(new List<ModuleImage> { wrap },
                        Entries(Detection(new JArray(1, 0, 2, 1), mask), 0, 7, source), Options(true, true));
                    Statistics(Target(output), "mean", true, true, 102, 103);
                    Statistics(Target(output), "median", true, true, 102, 103);
                    Require(ReferenceEquals(wrap.ImageObject, pixels), "统计改写延迟图像");
                }
            }
        }

        private static void TestOriginIndex()
        {
            using (var first = Gray(new byte[,] { { 1, 3 } }))
            using (var second = Gray(new byte[,] { { 10, 30 } }))
            using (var mask = Gray(new byte[,] { { 255, 0 } }))
            {
                var state = new TransformationState(2, 1);
                var images = new List<ModuleImage>
                {
                    new ModuleImage(first, first, state, 4),
                    new ModuleImage(second, second, state.Clone(), 9)
                };
                // index 指向第一个槽位，而 origin_index 指定第二张原图，不能串图。
                var target = Target(Execute(images,
                    Entries(Detection(new JArray(0, 0, 2, 1), mask), 0, 9, state), Options(true, true)));
                Statistics(target, "mean", true, true, 10, 30);
                Statistics(target, "median", true, true, 10, 30);
                Reject(() => Execute(images,
                    Entries(Detection(new JArray(0, 0, 2, 1), mask), 0, 99, state), Options(true, true)),
                    "不存在的 origin_index 未拒绝");
            }
        }

        private static void TestRotatedDomain()
        {
            using (var image = new Mat(5, 5, MatType.CV_8UC1, new Scalar(200)))
            using (var mask = new Mat(4, 4, MatType.CV_8UC1, Scalar.Black))
            {
                // 2x2 框旋转 π/4 后，仅中心和四个相邻整数像素属于域；外侧 200 排除。
                image.Set(2, 2, (byte)10);
                image.Set(1, 2, (byte)20);
                image.Set(2, 3, (byte)30);
                image.Set(3, 2, (byte)40);
                image.Set(2, 1, (byte)50);
                mask.Set(2, 2, (byte)255);
                var target = Target(Execute(Images(image),
                    Entries(Detection(new JArray(2, 2, 2, 2, Math.PI / 4), mask)), Options(true, true)));
                Statistics(target, "mean", true, true, 10, 35);
                Statistics(target, "median", true, true, 10, 35);
            }
        }

        private static void TestCardinalRotatedDomains()
        {
            using (var image = new Mat(5, 5, MatType.CV_8UC1))
            using (var full = new Mat(5, 5, MatType.CV_8UC1, Scalar.White))
            using (var selected = new Mat(5, 5, MatType.CV_8UC1, Scalar.Black))
            using (var local = Gray(new byte[,] { { 0, 255 }, { 0, 0 } }))
            {
                for (int y = 0; y < 5; y++)
                for (int x = 0; x < 5; x++) image.Set(y, x, (byte)(10 * y + x));
                // 各方向域：0 x{1,2}y{1,2}；π/2 x{2,3}y{1,2}；π x{2,3}y{2,3}；-π/2 x{1,2}y{2,3}。
                var angles = new[] { 0.0, Math.PI / 2, Math.PI, -Math.PI / 2 };
                var expected = new[] { 16.5, 17.5, 27.5, 26.5 };
                for (int i = 0; i < angles.Length; i++)
                {
                    var target = Target(Execute(Images(image),
                        Entries(Detection(new JArray(2, 2, 2, 2, angles[i]), full)), Options(true, true)));
                    Statistics(target, "mean", true, true, expected[i], null);
                    Statistics(target, "median", true, true, expected[i], null);
                }
                selected.Set(2, 3, (byte)255);
                var included = Target(Execute(Images(image),
                    Entries(Detection(new JArray(2, 2, 2, 2, Math.PI / 2), selected)), Options(true, true)));
                Statistics(included, "mean", true, true, 23, 47.0 / 3);
                Statistics(included, "median", true, true, 23, 13);
                selected.SetTo(Scalar.Black);
                selected.Set(1, 1, (byte)255);
                var excluded = Target(Execute(Images(image),
                    Entries(Detection(new JArray(2, 2, 2, 2, Math.PI / 2), selected)), Options(true, true)));
                Statistics(excluded, "mean", true, true, null, 17.5);
                Statistics(excluded, "median", true, true, null, 17.5);
                // 候选域多出的 x=3 取原局部 mask 最右列，不能把两列重新拉伸为三列。
                var edge = Target(Execute(Images(image),
                    Entries(Detection(new JArray(2, 2, 2, 2, Math.PI / 2), local)), Options(true, true)));
                Statistics(edge, "mean", true, true, 12.5, 22.5);
                Statistics(edge, "median", true, true, 12.5, 22.5);
            }
        }

        private static void TestRepeatedCleanup()
        {
            using (var image = Gray(new byte[,] { { 1, 3 } }))
            using (var mask = Gray(new byte[,] { { 255, 0 } }))
            {
                var det = Detection(new JArray(0, 0, 2, 1), mask);
                foreach (string field in Fields) det[field] = field.StartsWith("with_", StringComparison.Ordinal) ? new JValue(true) : new JValue(999);
                det["extra_info"] = new JObject { ["note"] = "保留模块结果" };
                var input = Entries(det);
                input[0]["with_mean"] = "保留 entry 信息";
                var nonLocal = new JObject { ["type"] = "global", ["sample_results"] = new JArray(det.DeepClone()) };
                input.Add(nonLocal);
                var images = Images(image);
                var first = Execute(images, input, Options(true, false));
                Statistics(Target(first), "mean", true, true, 1, 3);
                Statistics(Target(first), "median", false, false, null, null);
                Require(JToken.DeepEquals(first[1], nonLocal), "非 local 结果被改写");
                Require(first[0].Value<string>("with_mean") == "保留 entry 信息", "统计字段写入了 entry");
                var second = Execute(images, first, Options(false, true));
                Statistics(Target(second), "mean", false, false, null, null);
                Statistics(Target(second), "median", true, true, 1, 3);
                var third = Execute(images, second, Options(false, false));
                Statistics(Target(third), "mean", false, false, null, null);
                Statistics(Target(third), "median", false, false, null, null);
                // 缺少 mask 时，旧 true/999 也必须变成 false/null，而不是沿用旧结果。
                det.Remove("mask_rle");
                var unavailable = Target(Execute(images, Entries(det), Options(true, true)));
                Statistics(unavailable, "mean", true, false, null, null);
                Statistics(unavailable, "median", true, false, null, null);
            }
        }

        private static Dictionary<string, object> Options(bool mean, bool median)
        {
            return new Dictionary<string, object> { ["mean"] = mean, ["median"] = median };
        }

        private static Mat Gray(byte[,] values)
        {
            var mat = new Mat(values.GetLength(0), values.GetLength(1), MatType.CV_8UC1);
            for (int y = 0; y < mat.Rows; y++)
            for (int x = 0; x < mat.Cols; x++) mat.Set(y, x, values[y, x]);
            return mat;
        }

        private static List<ModuleImage> Images(Mat image)
        {
            return new List<ModuleImage> { new ModuleImage(image, image, new TransformationState(image.Cols, image.Rows), 0) };
        }

        private static JObject Detection(JArray bbox, Mat mask)
        {
            var det = new JObject { ["bbox"] = bbox.DeepClone(), ["score"] = 0.9, ["category_id"] = 2 };
            if (mask != null) det["mask_rle"] = MaskRleUtils.MatToMaskInfo(mask);
            return det;
        }

        private static JArray Entries(JObject det, int index = 0, int originIndex = 0, TransformationState state = null)
        {
            return new JArray(new JObject
            {
                ["type"] = "local", ["index"] = index, ["origin_index"] = originIndex,
                ["transform"] = state == null ? JValue.CreateNull() : JToken.FromObject(state.ToDict()),
                ["sample_results"] = new JArray(det)
            });
        }

        private static JArray Execute(List<ModuleImage> images, JArray input, Dictionary<string, object> properties = null)
        {
            var before = input.DeepClone();
            var pixels = new List<byte[]>();
            var originalPixels = new List<byte[]>();
            var states = new List<JToken>();
            foreach (var image in images)
            {
                pixels.Add(Bytes(image.ImageObject));
                originalPixels.Add(Bytes(image.OriginalImage));
                states.Add(JToken.FromObject(image.TransformState));
            }
            var module = new ForegroundBackgroundStatistics(2033, "统计节点自测", properties);
            ModuleIO output;
            try { output = module.Process(images, input); }
            finally
            {
                Require(JToken.DeepEquals(before, input), "输入结果或原目标被修改");
                for (int i = 0; i < images.Count; i++)
                {
                    Require(EqualBytes(pixels[i], Bytes(images[i].ImageObject)), "当前图像像素被修改");
                    Require(EqualBytes(originalPixels[i], Bytes(images[i].OriginalImage)), "原图像素被修改");
                    Require(JToken.DeepEquals(states[i], JToken.FromObject(images[i].TransformState)), "输入变换被修改");
                }
            }
            Require(output.ImageList.Count == images.Count, "图像数量被修改");
            Require(output.ResultList.Count == input.Count, "结果数量被修改");
            for (int i = 0; i < images.Count; i++)
                Require(ReferenceEquals(output.ImageList[i], images[i]), "输出未保留原图像对象");
            for (int i = 0; i < input.Count; i++)
            {
                if (input[i].Value<string>("type") != "local") continue;
                var oldTargets = (JArray)input[i]["sample_results"];
                var newTargets = (JArray)output.ResultList[i]["sample_results"];
                Require(oldTargets.Count == newTargets.Count, "目标数量被修改");
                for (int j = 0; j < oldTargets.Count; j++)
                {
                    var oldDet = (JObject)oldTargets[j].DeepClone();
                    var newDet = (JObject)newTargets[j].DeepClone();
                    foreach (string field in Fields) { oldDet.Remove(field); newDet.Remove(field); }
                    Require(JToken.DeepEquals(oldDet, newDet), "统计以外的目标字段被修改");
                }
            }
            return output.ResultList;
        }

        private static byte[] Bytes(Mat mat)
        {
            if (mat == null) return null;
            if (mat.Empty()) return Array.Empty<byte>();
            Require(mat.IsContinuous(), "自测图像必须连续存储");
            var bytes = new byte[checked(mat.Rows * mat.Cols * (int)mat.ElemSize())];
            Marshal.Copy(mat.Data, bytes, 0, bytes.Length);
            return bytes;
        }

        private static bool EqualBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null) return ReferenceEquals(left, right);
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private static JObject Target(JArray entries)
        {
            return (JObject)entries[0]["sample_results"][0];
        }

        private static void Statistics(JObject target, string name, bool enabled, bool available, double? foreground, double? background)
        {
            string with = "with_" + name;
            string fg = "foreground_" + name;
            string bg = "background_" + name;
            if (!enabled)
            {
                Require(target[with] == null && target[fg] == null && target[bg] == null, name + " 关闭后键未完整移除");
                return;
            }
            Require(target[with] != null && target[with].Type == JTokenType.Boolean && target.Value<bool>(with) == available,
                with + " 类型或数值异常");
            Number(target, fg, foreground);
            Number(target, bg, background);
        }

        private static void Number(JObject target, string field, double? expected)
        {
            var value = target[field];
            Require(value != null, field + " 缺失");
            if (!expected.HasValue)
            {
                Require(value.Type == JTokenType.Null, field + " 应为 JSON null");
                return;
            }
            Require(value.Type == JTokenType.Float || value.Type == JTokenType.Integer, field + " 应为数值");
            double actual = value.Value<double>();
            Require(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected.Value) < 1e-9,
                field + " 实际=" + actual + "，期望=" + expected.Value);
        }

        private static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); }
            catch (ArgumentException) { rejected = true; }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
