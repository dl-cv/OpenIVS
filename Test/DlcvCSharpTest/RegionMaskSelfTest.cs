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
                    string regionMode = "any_bbox";
                    bool global = false;
                    Action<string, JObject, JArray, bool, string, double> check = (name, target, regions, expected, metric, threshold) =>
                    {
                        var props = new Dictionary<string, object> { ["filter_mode"] = "mask", ["overlap_threshold"] = threshold, ["result_region_mode"] = regionMode };
                        if (metric != null) props["metric"] = metric;
                        ResultFilterRegion module = global ? new ResultFilterRegionGlobal(1, properties: props) : new ResultFilterRegion(1, properties: props);
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
                    foreach (int value in new[] { 1, 127, 128 })
                    {
                        var binary = Det(8, 9, 2, 2); binary["mask_array"] = new JArray(new JArray(value, value), new JArray(value, value));
                        check("foreground-" + value, binary, Entries(Det(8, 9, 2, 2)), value > 127, null, 1);
                    }
                    regionMode = "top1_bbox";
                    var highMissing = (JObject)missing.DeepClone(); highMissing["score"] = 1;
                    check("top1-skips-missing-mask", Det(8, 9, 2, 2), Entries(highMissing, Det(8, 9, 2, 2)), true, null, 1);
                    var highFar = Det(20, 20, 2, 2); highFar["score"] = 1;
                    check("top1-highest-valid-score", Det(8, 9, 2, 2), Entries(Det(8, 9, 2, 2), highFar), false, null, 1);
                    var noScore = Det(8, 9, 2, 2); noScore.Remove("score"); highFar.Remove("score");
                    check("top1-first-without-score", Det(8, 9, 2, 2), Entries(noScore, highFar), true, null, 1);
                    regionMode = "any_bbox";
                    global = true;
                    check("global-mask-iou", Det(8, 9, 10, 10), Entries(Det(8, 9, 2, 2)), false, "iou", 0.5);
                    global = false;
                    // Compare window warping to a full original-canvas reference, including crop and native affine semantics.
                    foreach (var spec in new[] { new[] { 17.0, 1.0 }, new[] { 45.0, 0.7 }, new[] { -33.0, 1.4 }, new[] { 90.0, 1.0 }, new[] { 180.0, 1.0 }, new[] { 0.0, -1.0 } })
                    using (var affine = Cv2.GetRotationMatrix2D(new Point2f(8, 8), spec[0], spec[1] < 0 ? 1 : spec[1]))
                    using (var forward = affine.Clone())
                    using (var inverse = new Mat())
                    using (var source = new Mat(16, 16, MatType.CV_8UC1, Scalar.Black))
                    using (var reference = new Mat())
                    {
                        if (spec[1] < 0) { affine.Set(0, 0, -1.0); affine.Set(0, 2, 15.0); affine.CopyTo(forward); }
                        forward.Set(0, 2, affine.At<double>(0, 2) - 2 * affine.At<double>(0, 0) - affine.At<double>(0, 1));
                        forward.Set(1, 2, affine.At<double>(1, 2) - 2 * affine.At<double>(1, 0) - affine.At<double>(1, 1));
                        Cv2.InvertAffineTransform(forward, inverse);
                        var local = Det(5, 4, 6, 7);
                        for (int iy = 0; iy < 7; iy++)
                        for (int ix = 0; ix < 6; ix++)
                        {
                            byte v = (byte)((iy < 3 && ix < 4 || iy >= 2 && ix >= 2) ? 255 : 0);
                            local["mask_array"][iy][ix] = v; source.Set(4 + iy, 5 + ix, v);
                        }
                        Cv2.WarpAffine(source, reference, inverse, new Size(32, 32), InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.Black);
                        var expected = Det(0, 0, 32, 32);
                        for (int iy = 0; iy < 32; iy++)
                        for (int ix = 0; ix < 32; ix++) expected["mask_array"][iy][ix] = reference.At<byte>(iy, ix);
                        foreach (bool native in new[] { false, true })
                        {
                            var transform = new JObject { ["crop_box"] = new JArray(2, 1, 16, 16), ["output_size"] = new JArray(16, 16) };
                            if (native)
                            {
                                transform["original_width"] = 32; transform["original_height"] = 32;
                                transform["affine_2x3"] = new JArray(forward.At<double>(0, 0), forward.At<double>(0, 1), forward.At<double>(0, 2), forward.At<double>(1, 0), forward.At<double>(1, 1), forward.At<double>(1, 2));
                            }
                            else
                            {
                                transform["original_size"] = new JArray(32, 32);
                                transform["affine_matrix"] = new JArray(new JArray(affine.At<double>(0, 0), affine.At<double>(0, 1), affine.At<double>(0, 2)), new JArray(affine.At<double>(1, 0), affine.At<double>(1, 1), affine.At<double>(1, 2)));
                            }
                            var transformed = Entries(local); transformed[0]["transform"] = transform;
                            check("affine-" + spec[0] + "-" + spec[1] + (native ? "-native" : "-python"), expected, transformed, true, "iou", 1);
                        }
                    }
                    // 分流后区域 index 可从 1 重排成 0，原图身份不能随 index 改变。
                    var multiImages = new List<ModuleImage> { images[0], new ModuleImage(image, image, new TransformationState(32, 32), 1) };
                    var multiTargets = Entries(Det(8, 9, 2, 2));
                    var second = (JObject)multiTargets[0].DeepClone(); second["index"] = 1; second["origin_index"] = 1;
                    multiTargets.Add(second);
                    var otherRegion = Entries(Det(8, 9, 2, 2)); otherRegion[0]["origin_index"] = 1;
                    var isolated = new ResultFilterRegion(1, properties: new Dictionary<string, object> { ["filter_mode"] = "mask" });
                    isolated.ExtraInputsIn.Add(new ModuleChannel(null, otherRegion));
                    var isolatedOutput = isolated.Process(multiImages, multiTargets);
                    if (isolatedOutput.ResultList.Count != 1 || isolatedOutput.ResultList[0]["origin_index"].Value<int>() != 1
                        || isolatedOutput.ImageList.Count != 1 || isolatedOutput.ImageList[0].OriginalIndex != 1
                        || isolatedOutput.ResultList[0]["index"].Value<int>() != 0
                        || isolated.ExtraOutputs[0].ResultList.Count != 1 || isolated.ExtraOutputs[0].ImageList[0].OriginalIndex != 0)
                        throw new Exception("origin-isolation-reindexed-region: wrong source image");
                    Console.WriteLine("PASS origin-isolation-reindexed-region"); count++;
                    foreach (var invalid in new[] { new Dictionary<string, object> { ["metric"] = "bad" }, new Dictionary<string, object> { ["overlap_threshold"] = -1 }, new Dictionary<string, object> { ["overlap_threshold"] = 2 }, new Dictionary<string, object> { ["overlap_threshold"] = double.NaN } })
                    {
                        invalid["filter_mode"] = "mask";
                        var invalidModule = new ResultFilterRegion(1, properties: invalid);
                        invalidModule.ExtraInputsIn.Add(new ModuleChannel(null, Entries(Det(0, 0, 2, 2))));
                        bool failed = false;
                        try { invalidModule.Process(images, Entries(Det(0, 0, 2, 2))); } catch (ArgumentException) { failed = true; }
                        if (!failed) throw new Exception("invalid-mask-parameter accepted");
                        Console.WriteLine("PASS invalid-mask-parameter"); count++;
                    }
                    var unknown = Entries(Det(8, 9, 2, 2)); unknown[0]["origin_index"] = 9;
                    var unknownModule = new ResultFilterRegion(1, properties: new Dictionary<string, object> { ["filter_mode"] = "mask" });
                    unknownModule.ExtraInputsIn.Add(new ModuleChannel(null, Entries(Det(8, 9, 2, 2))));
                    bool unknownRejected = false;
                    try { unknownModule.Process(images, unknown); } catch (ArgumentException) { unknownRejected = true; }
                    if (!unknownRejected) throw new Exception("unknown origin accepted");
                    Console.WriteLine("PASS unknown-target-origin"); count++;
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
