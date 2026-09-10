using System;
using System.Collections.Generic;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class RegionMaskSelfTest
    {
        private static JObject Det(int x, int y, int w, int h, int left = 0, int right = -1)
        {
            var rows = new JArray();
            for (int iy = 0; iy < h; iy++)
            {
                var row = new JArray();
                for (int ix = 0; ix < w; ix++) row.Add(ix >= left && (right < 0 || ix < right) ? 255 : 0);
                rows.Add(row);
            }
            return new JObject { ["bbox"] = new JArray(x, y, w, h), ["mask_array"] = rows, ["score"] = 0.9 };
        }

        private static JArray Entries(params JObject[] dets)
        {
            return new JArray(new JObject { ["type"] = "local", ["index"] = 0, ["origin_index"] = 0,
                ["transform"] = null, ["sample_results"] = new JArray(dets) });
        }

        public static int Run()
        {
            try
            {
                using (var image = new Mat(32, 32, MatType.CV_8UC3, Scalar.Black))
                {
                    var images = new List<ModuleImage> { new ModuleImage(image, image, new TransformationState(32, 32), 0) };
                    int count = 0;
                    Action<string, JObject, JArray, bool, string, double> check = (name, target, regions, expected, metric, threshold) =>
                    {
                        var props = new Dictionary<string, object> { ["filter_mode"] = "mask", ["overlap_threshold"] = threshold };
                        if (metric != null) props["metric"] = metric;
                        var module = new ResultFilterRegion(1, properties: props);
                        module.ExtraInputsIn.Add(new ModuleChannel(null, regions));
                        var input = Entries(target);
                        var before = input.DeepClone();
                        var output = module.Process(images, input);
                        if ((output.ResultList.Count > 0) != expected || (module.ExtraOutputs[0].ResultList.Count > 0) == expected)
                            throw new Exception(name + ": wrong branch");
                        if (Convert.ToBoolean(module.ScalarOutputsByName["has_positive"]) != expected)
                            throw new Exception(name + ": wrong scalar");
                        if (!JToken.DeepEquals(before, input)) throw new Exception(name + ": input changed");
                        var entry = (expected ? output.ResultList : module.ExtraOutputs[0].ResultList)[0];
                        if (!JToken.DeepEquals(entry["sample_results"][0], target)) throw new Exception(name + ": bbox/mask changed");
                        Console.WriteLine("PASS " + name);
                        count++;
                    };
                    check("shape-not-bbox", Det(8, 9, 10, 10, 0, 4), Entries(Det(8, 9, 10, 10, 6, 10)), false, null, 0.5);
                    check("default-ios-xywh", Det(8, 9, 10, 10), Entries(Det(8, 9, 2, 2)), true, null, 0.5);
                    check("iou", Det(8, 9, 10, 10), Entries(Det(8, 9, 2, 2)), false, "iou", 0.5);
                    check("inclusive-threshold", Det(8, 9, 10, 10), Entries(Det(13, 9, 10, 10)), true, "IOS", 0.5);
                    check("above-threshold", Det(8, 9, 10, 10), Entries(Det(13, 9, 10, 10)), false, "ios", 0.51);
                    check("zero-needs-intersection", Det(8, 9, 10, 10), Entries(Det(18, 9, 10, 10)), false, "ios", 0);
                    var missing = Det(8, 9, 10, 10); missing.Remove("mask_array");
                    check("missing-target-mask", missing, Entries(Det(8, 9, 10, 10)), false, null, 0.5);
                    check("missing-region-mask", Det(8, 9, 10, 10), Entries(missing), false, null, 0.5);
                    check("empty-mask", Det(8, 9, 10, 10, 0, 0), Entries(Det(8, 9, 10, 10)), false, null, 0.5);
                    check("empty-region", Det(8, 9, 10, 10), new JArray(), false, null, 0.5);
                    check("source-clipping", Det(-5, 9, 10, 10), Entries(Det(0, 9, 5, 10)), true, null, 1);
                    var low = Det(8, 9, 2, 2); low["bbox"] = new JArray(8, 9, 10, 10);
                    check("resize-nearest", low, Entries(Det(8, 9, 10, 10)), true, null, 1);
                    var full = Det(0, 0, 32, 32, 8, 10); full["bbox"] = new JArray(8, 0, 2, 32);
                    check("full-image-mask", full, Entries(Det(8, 0, 2, 32)), true, null, 1);
                    var unconnected = new ResultFilterRegion(1, properties: new Dictionary<string, object> { ["filter_mode"] = "mask" });
                    bool rejected = false;
                    try { unconnected.Process(images, Entries(Det(0, 0, 2, 2))); } catch (ArgumentException) { rejected = true; }
                    if (!rejected) throw new Exception("unconnected mask region accepted");
                    Console.WriteLine("Region mask selftest passed: " + (count + 1));
                }
                return 0;
            }
            catch (Exception ex) { Console.WriteLine(ex); return 1; }
        }
    }
}
