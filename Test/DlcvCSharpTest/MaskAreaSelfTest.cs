using System;
using System.Collections.Generic;
using System.Reflection;
using DlcvModules;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class MaskAreaSelfTest
    {
        public static int Run()
        {
            int failures = 0;
            Action<string, Action> check = (name, test) =>
            {
                try { test(); Console.WriteLine("PASS " + name); }
                catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
            };
            var normalizeJson = typeof(Model).GetMethod("StandardizeJsonOutput", BindingFlags.Instance | BindingFlags.NonPublic);
            if (normalizeJson == null) throw new InvalidOperationException("未找到 JSON 结果转换方法");
            using (var model = new Model())
            using (var mask = new Mat(4, 4, MatType.CV_8UC1, Scalar.White))
            {
                foreach (var scenario in new[] { "resize", "unchanged-size", "empty-mask", "no-mask" })
                {
                    foreach (var areaCase in new[] { "provided", "zero", "missing", "null" })
                    {
                        check(scenario + "-" + areaCase, () =>
                        {
                            mask.SetTo(scenario == "empty-mask" ? Scalar.Black : Scalar.White);
                            bool withMask = scenario != "no-mask";
                            bool hasArea = areaCase == "provided" || areaCase == "zero";
                            double size = scenario == "unchanged-size" ? 4 : 2.8;
                            var obj = new JObject
                            {
                                ["category_id"] = 0, ["category_name"] = "target", ["score"] = 0.9,
                                ["bbox"] = new JArray(0, 0, size, size), ["with_mask"] = withMask,
                                ["mask"] = new JObject { ["width"] = 4, ["height"] = 4, ["mask_ptr"] = mask.Data.ToInt64() }
                            };
                            if (hasArea) obj["area"] = areaCase == "provided" ? 123.5 : 0;
                            else if (areaCase == "null") obj["area"] = JValue.CreateNull();
                            var original = obj.DeepClone();
                            var raw = new JObject { ["sample_results"] = new JArray(new JObject { ["results"] = new JArray(obj) }) };
                            var result = model.ParseToStructResult(raw).SampleResults[0].Results[0];
                            using (result.Mask)
                            {
                                int expectedSize = (int)size; // 保留 C# 历史截断规则。
                                int expectedMaskArea = scenario == "empty-mask" ? 0 : expectedSize * expectedSize;
                                double expectedArea = withMask ? expectedMaskArea : 0;
                                if (hasArea) expectedArea = areaCase == "provided" ? 123.5 : 0;
                                double expectedJsonArea = !hasArea && !withMask ? size * size : expectedArea;
                                if (result.Area != expectedArea) throw new Exception("area=" + result.Area + ", expected=" + expectedArea);
                                var json = (JObject)normalizeJson.Invoke(model, new object[] { obj, false });
                                if (Math.Abs(json.Value<double>("area") - expectedJsonArea) > 0.00001) throw new Exception("JSON area=" + json["area"] + ", expected=" + expectedJsonArea);
                                if (!JToken.DeepEquals(obj, original)) throw new Exception("输入结果被修改");
                                var expectedBbox = obj["bbox"].ToObject<List<double>>();
                                var jsonBbox = json["bbox"].ToObject<List<double>>();
                                if (jsonBbox.Count != expectedBbox.Count || result.Bbox.Count != expectedBbox.Count) throw new Exception("bbox 长度被修改");
                                for (int i = 0; i < expectedBbox.Count; i++)
                                {
                                    if (jsonBbox[i] != expectedBbox[i] || result.Bbox[i] != expectedBbox[i]) throw new Exception("bbox[" + i + "] 被修改");
                                }
                                if (hasArea && !JToken.DeepEquals(json["area"], obj["area"])) throw new Exception("JSON 未保留 SDK 面积字段");
                                if (!withMask && !result.Mask.Empty()) throw new Exception("关闭 mask 后仍返回掩码");
                                if (withMask && (result.Mask.Width != expectedSize || result.Mask.Height != expectedSize)) throw new Exception("mask geometry changed");
                                if (withMask && MaskRleUtils.CalculateMaskArea(MaskRleUtils.MatToMaskInfo(result.Mask)) != expectedMaskArea) throw new Exception("RLE area mismatch");
                            }
                        });
                    }
                }
            }
            using (var model = new Model())
            {
                foreach (bool sparse in new[] { false, true })
                {
                    check(sparse ? "json-sparse-nearest-area" : "json-original-coordinate-area", () =>
                    {
                        int sourceSize = sparse ? 2 : 4;
                        int outputSize = sparse ? 3 : 2;
                        int sourceArea = sparse ? 1 : 16;
                        int expectedMaskArea = sparse ? 1 : 4;
                        using (var mask = new Mat(sourceSize, sourceSize, MatType.CV_8UC1, sparse ? Scalar.Black : Scalar.White))
                        {
                            if (sparse) mask.Set(1, 1, (byte)255);
                            var obj = new JObject
                            {
                                ["category_id"] = 0, ["category_name"] = "target", ["score"] = 0.9,
                                ["area"] = sourceArea, ["bbox"] = new JArray(7, 9, outputSize, outputSize), ["with_mask"] = true,
                                ["mask"] = new JObject { ["width"] = sourceSize, ["height"] = sourceSize, ["mask_ptr"] = mask.Data.ToInt64() }
                            };
                            var raw = new JObject { ["sample_results"] = new JArray(new JObject { ["results"] = new JArray(obj) }) };
                            var parsed = model.ParseToStructResult(raw).SampleResults[0].Results[0];
                            using (parsed.Mask)
                            {
                                var json = (JObject)normalizeJson.Invoke(model, new object[] { obj, false });
                                if (parsed.Area != sourceArea || json.Value<double>("area") != sourceArea)
                                    throw new Exception("结构化与 JSON 面积必须保留 " + sourceArea + ", got " + parsed.Area + "/" + json["area"]);
                                if (parsed.Mask.Width != outputSize || parsed.Mask.Height != outputSize)
                                    throw new Exception("输出掩码尺寸错误");
                                if (Cv2.CountNonZero(parsed.Mask) != expectedMaskArea)
                                    throw new Exception("输出掩码像素错误");
                                if (Cv2.CountNonZero(mask) != sourceArea || obj.Value<double>("area") != sourceArea)
                                    throw new Exception("输入掩码或面积被修改");
                            }
                        }
                    });
                }
            }
            foreach (bool hasArea in new[] { true, false })
            {
                check(hasArea ? "merge-overwrites-area" : "merge-fills-area", () =>
                {
                    using (var image = new Mat(16, 16, MatType.CV_8UC3, Scalar.Black))
                    using (var mask = new Mat(2, 2, MatType.CV_8UC1, Scalar.White))
                    {
                        var images = new List<ModuleImage>();
                        var entries = new JArray();
                        for (int i = 0; i < 2; i++)
                        {
                            images.Add(new ModuleImage(image, image, new TransformationState(16, 16), 0)
                            {
                                SlidingMeta = new JObject { ["grid_x"] = i, ["grid_y"] = 0, ["slice_index"] = new JArray(0, i) }
                            });
                            var det = new JObject { ["category_id"] = 0, ["category_name"] = "target", ["score"] = 0.9,
                                ["bbox"] = new JArray(2 + i, 2, 4, 4), ["with_mask"] = true, ["mask_rle"] = MaskRleUtils.MatToMaskInfo(mask) };
                            if (hasArea) det["area"] = 99;
                            entries.Add(new JObject { ["type"] = "local", ["index"] = i, ["origin_index"] = 0, ["sample_results"] = new JArray(det) });
                        }
                        var result = new SlidingMergeResults(1).Process(images, entries);
                        var dets = (JArray)result.ResultList[0]["sample_results"];
                        if (dets.Count != 1) throw new Exception("expected one union");
                        var output = dets[0];
                        if (MaskRleUtils.CalculateMaskArea(output["mask_rle"]) != 20) throw new Exception("union geometry changed");
                        if (output.Value<double?>("area") != 20) throw new Exception("merged area must be 20, got " + output["area"]);
                    }
                });
            }
            Console.WriteLine("Mask area selftest failures: " + failures);
            return failures == 0 ? 0 : 1;
        }
    }
}
