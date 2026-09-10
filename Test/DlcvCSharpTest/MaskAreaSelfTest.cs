using System;
using System.Collections.Generic;
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
            using (var model = new Model())
            using (var mask = new Mat(4, 4, MatType.CV_8UC1, Scalar.White))
            {
                foreach (var scenario in new[] { "resize", "unchanged-size", "empty-mask", "no-mask" })
                {
                    check(scenario, () =>
                    {
                        mask.SetTo(scenario == "empty-mask" ? Scalar.Black : Scalar.White);
                        bool withMask = scenario != "no-mask";
                        double size = scenario == "unchanged-size" ? 4 : 2.8;
                        var obj = new JObject
                        {
                            ["category_id"] = 0, ["category_name"] = "target", ["score"] = 0.9,
                            ["area"] = 99, ["bbox"] = new JArray(0, 0, size, size), ["with_mask"] = withMask,
                            ["mask"] = new JObject { ["width"] = 4, ["height"] = 4, ["mask_ptr"] = mask.Data.ToInt64() }
                        };
                        var raw = new JObject { ["sample_results"] = new JArray(new JObject { ["results"] = new JArray(obj) }) };
                        var result = model.ParseToStructResult(raw).SampleResults[0].Results[0];
                        using (result.Mask)
                        {
                            int expectedSize = (int)size; // 保留 C# 历史截断规则。
                            double expectedArea = !withMask ? 99 : scenario == "empty-mask" ? 0 : expectedSize * expectedSize;
                            if (result.Area != expectedArea) throw new Exception("area=" + result.Area + ", expected=" + expectedArea);
                            if (withMask && (result.Mask.Width != expectedSize || result.Mask.Height != expectedSize)) throw new Exception("mask geometry changed");
                            if (withMask && result.Area != MaskRleUtils.CalculateMaskArea(MaskRleUtils.MatToMaskInfo(result.Mask))) throw new Exception("RLE area mismatch");
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
