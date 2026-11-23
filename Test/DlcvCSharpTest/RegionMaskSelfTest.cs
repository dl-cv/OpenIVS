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

        private static void CheckLegacyPolygonTransforms(Mat image)
        {
            JArray Entry(JObject det, int index, int origin, TransformationState state)
            {
                var entries = Entries(det);
                entries[0]["index"] = index;
                entries[0]["origin_index"] = origin;
                entries[0]["transform"] = state == null ? (JToken)JValue.CreateNull() : JObject.FromObject(state.ToDict());
                return entries;
            }
            var stateA = new TransformationState(32, 32, affine2x3: new double[] { 1, 0, -3, 0, 1, 0 }, outputSize: new[] { 32, 32 });
            var stateB = new TransformationState(32, 32, affine2x3: new double[] { 1, 0, -1, 0, 1, 0 }, outputSize: new[] { 32, 32 });
            var target = JObject.Parse(@"{'bbox':[1,1,3,3]}");
            var region = JObject.Parse(@"{'bbox':[3,1,3,3],'polygon':[[3,1],[5,1],[5,3],[3,3]]}");
            var images = new List<ModuleImage> { new ModuleImage(image, image, stateA, 0), new ModuleImage(image, image, stateB, 0) };
            var module = new ResultFilterRegion(1);
            module.ExtraInputsIn.Add(new ModuleChannel(null, Entry(region, 1, 0, stateB)));
            var output = module.Process(images, Entry(target, 0, 0, stateA));
            if (output.ImageList.Count != 1 || !ReferenceEquals(output.ImageList[0], images[0]))
                throw new Exception("legacy polygon should match across crops of the same image");

            images[1] = new ModuleImage(image, image, stateA, 1);
            module = new ResultFilterRegion(1);
            module.ExtraInputsIn.Add(new ModuleChannel(null, Entry(region, 1, 1, stateA)));
            target["bbox"] = region["bbox"].DeepClone();
            output = module.Process(images, Entry(target, 0, 0, stateA));
            if (output.ImageList.Count != 0 || !ReferenceEquals(module.ExtraOutputs[0].ImageList[0], images[0]))
                throw new Exception("legacy polygon should not match another image with the same transform");

            var regions = Entry(target, 0, 0, stateA);
            var farRegion = JObject.Parse(@"{'bbox':[22,22,3,3],'polygon':[[22,22],[24,22],[24,24],[22,24]]}");
            regions.Add(Entry(farRegion, 1, 1, stateA)[0]);
            module = new ResultFilterRegion(1);
            module.ExtraInputsIn.Add(new ModuleChannel(null, regions));
            output = module.Process(images, Entry(target, 1, 1, stateA));
            if (output.ImageList.Count != 0 || !ReferenceEquals(module.ExtraOutputs[0].ImageList[0], images[1]))
                throw new Exception("legacy region rectangles must stay with their original image when polygons are present");

            module = new ResultFilterRegion(1);
            output = module.Process(images, Entry(region, 0, 0, null));
            var original = output.ResultList[0]["sample_results"][0];
            if (!JToken.DeepEquals(original["polygon"], region["polygon"]) || !JToken.DeepEquals(original["bbox"], region["bbox"]))
                throw new Exception("legacy original polygon should not be converted twice");
            Console.WriteLine("PASS legacy-polygon-transforms");
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
                    string filterMode = "mask";
                    Action<string, JObject, JArray, bool, string, double> check = (name, target, regions, expected, metric, threshold) =>
                    {
                        var props = new Dictionary<string, object> { ["filter_mode"] = filterMode, ["overlap_threshold"] = threshold, ["result_region_mode"] = regionMode };
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
                        var actual = entry["sample_results"][0];
                        if (filterMode == "mask" ? !JToken.DeepEquals(actual, target) :
                            !JToken.DeepEquals(actual["bbox"], target["bbox"]) || !JToken.DeepEquals(actual["mask_array"], target["mask_array"]) ||
                            !JToken.DeepEquals(actual["polygon"], target["polygon"]))
                            throw new Exception(name + ": bbox/mask/polygon changed");
                        Console.WriteLine("PASS " + name);
                        count++;
                    };
                    check("shape-not-bbox", Det(8, 9, 10, 10, 0, 4), Entries(Det(8, 9, 10, 10, 6, 10)), false, null, 0.5);
                    check("default-ios-xywh", Det(8, 9, 10, 10), Entries(Det(8, 9, 2, 2)), true, null, 0.5);
                    check("iou", Det(8, 9, 10, 10), Entries(Det(8, 9, 2, 2)), false, "iou", 0.5);
                    check("inclusive-threshold", Det(8, 9, 10, 10), Entries(Det(13, 9, 10, 10)), true, "IOS", 0.5);
                    check("above-threshold", Det(8, 9, 10, 10), Entries(Det(13, 9, 10, 10)), false, "ios", 0.51);
                    check("zero-needs-intersection", Det(8, 9, 10, 10), Entries(Det(18, 9, 10, 10)), false, "ios", 0);
                    var triangle = JObject.Parse(@"{'bbox':[8,9,10,10],'polygon':[[8,9],[17,9],[8,18]],'score':0.9}");
                    var corner = Det(16, 17, 2, 2);
                    var center = Det(9, 10, 2, 2);
                    var emptyPolygonMask = (JObject)triangle.DeepClone();
                    emptyPolygonMask["mask_array"] = new JArray();
                    foreach (string mode in new[] { "mask", "legacy" })
                    {
                        filterMode = mode;
                        check(mode + "-polygon-region-corner", corner, Entries(triangle), false, null, 0.5);
                        check(mode + "-polygon-target-corner", triangle, Entries(corner), false, null, 0.5);
                        check(mode + "-polygon-region-center", center, Entries(triangle), true, null, 0.5);
                        check(mode + "-polygon-target-center", triangle, Entries(center), true, null, 0.5);
                        check(mode + "-empty-mask-priority", center, Entries(emptyPolygonMask), false, null, 0.5);
                        foreach (int value in new[] { 1, 127, 128 })
                        {
                            var pixel = Det(9, 10, 1, 1);
                            pixel["mask_array"][0][0] = value;
                            check(mode + "-polygon-mask-" + value, pixel, Entries(triangle), value > 127, null, 0.5);
                        }
                    }
                    filterMode = "mask";
                    check("polygon-threshold-ios", triangle, Entries(center), true, "ios", 1);
                    check("polygon-threshold-iou", triangle, Entries(center), false, "iou", 0.5);
                    var malformed = (JObject)triangle.DeepClone();
                    malformed["polygon"][0][0] = 1e9 + 1;
                    foreach (string mode in new[] { "mask", "legacy" })
                    {
                        var module = new ResultFilterRegion(1, properties: new Dictionary<string, object> { ["filter_mode"] = mode });
                        module.ExtraInputsIn.Add(new ModuleChannel(null, Entries(triangle)));
                        try { module.Process(images, Entries(malformed)); throw new Exception(mode + " invalid polygon accepted"); }
                        catch (ArgumentException) { }
                    }
                    CheckLegacyPolygonTransforms(image);
                    var polygonOnly = (JObject)triangle.DeepClone(); polygonOnly.Remove("bbox");
                    check("polygon-without-bbox", center, Entries(polygonOnly), true, "ios", 1);
                    var missing = Det(8, 9, 10, 10); missing.Remove("mask_array");
                    check("missing-target-mask", missing, Entries(Det(8, 9, 10, 10)), true, null, 0.5);
                    check("missing-region-mask", Det(8, 9, 10, 10), Entries(missing), true, null, 0.5);
                    check("both-missing-iou", missing, Entries(missing), true, "iou", 1);
                    check("fallback-ios", missing, Entries(Det(8, 9, 2, 2)), true, "ios", 1);
                    check("fallback-iou", missing, Entries(Det(8, 9, 2, 2)), false, "iou", 0.5);
                    check("fallback-shape", missing, Entries(Det(8, 9, 10, 10, 0, 4)), false, "iou", 0.5);
                    var nullMask = (JObject)missing.DeepClone(); nullMask["mask_array"] = null; nullMask["mask_rle"] = null;
                    check("null-mask", nullMask, Entries(missing), true, "iou", 1);
                    var empty = (JObject)missing.DeepClone(); empty["mask_array"] = new JArray();
                    check("empty-array-no-fallback", empty, Entries(missing), false, null, 0);
                    check("empty-region-mask-no-fallback", missing, Entries(empty), false, null, 0);
                    var rle = (JObject)missing.DeepClone();
                    rle["mask_rle"] = new JObject { ["width"] = 10, ["height"] = 10, ["runs"] = new JArray(100) };
                    check("zero-rle-no-fallback", rle, Entries(missing), false, null, 0);
                    rle["mask_rle"]["runs"] = new JArray(0, 100);
                    check("rle-with-missing-array", rle, Entries(missing), true, "iou", 1);
                    foreach (var bbox in new[] { new JArray(-5, 9, 10, 10), new JArray(0.2, 9.2, 4.6, 9.6) })
                    {
                        var clipped = (JObject)missing.DeepClone(); clipped["bbox"] = bbox;
                        check("bbox-clipping-rounding", clipped, Entries(Det(0, 9, 5, 10)), true, "iou", 1);
                    }
                    foreach (var bbox in new[] { new JArray(), new JArray(0, 0, 0, 10), new JArray(0, 0, -1, 10),
                        new JArray(40, 40, 10, 10), new JArray("bad", 0, 10, 10), new JArray(double.NaN, 0, 10, 10) })
                    {
                        var invalid = (JObject)missing.DeepClone(); invalid["bbox"] = bbox;
                        check("unusable-bbox", invalid, Entries(missing), false, null, 0);
                    }
                    check("missing-bbox", new JObject(), Entries(missing), false, null, 0);
                    var huge = (JObject)missing.DeepClone(); huge["bbox"] = new JArray(-1e12, -1e12, 2e12, 2e12);
                    check("huge-bbox-clipped-before-allocation", huge, Entries(Det(0, 0, 32, 32)), true, "iou", 1);
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
                    var highMissing = (JObject)missing.DeepClone(); highMissing["score"] = 1; highMissing["bbox"] = new JArray(20, 20, 2, 2);
                    check("top1-includes-bbox-fallback", Det(8, 9, 2, 2), Entries(highMissing, Det(8, 9, 2, 2)), false, null, 1);
                    var highFar = Det(20, 20, 2, 2); highFar["score"] = 1;
                    check("top1-highest-valid-score", Det(8, 9, 2, 2), Entries(Det(8, 9, 2, 2), highFar), false, null, 1);
                    var noScore = Det(8, 9, 2, 2); noScore.Remove("score"); highFar.Remove("score");
                    check("top1-first-without-score", Det(8, 9, 2, 2), Entries(noScore, highFar), true, null, 1);
                    regionMode = "any_bbox";
                    global = true;
                    check("global-mask-iou", Det(8, 9, 10, 10), Entries(Det(8, 9, 2, 2)), false, "iou", 0.5);
                    check("global-bbox-fallback", missing, Entries(missing), true, "iou", 1);
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
                            var transformed = Entries((JObject)local.DeepClone()); transformed[0]["transform"] = transform;
                            check("affine-" + spec[0] + "-" + spec[1] + (native ? "-native" : "-python"), expected, transformed, true, "iou", 1);
                            using (var rectangle = new Mat(16, 16, MatType.CV_8UC1, Scalar.Black))
                            using (var roi = new Mat(rectangle, new Rect(5, 4, 6, 7)))
                            using (var rectangleReference = new Mat())
                            {
                                roi.SetTo(Scalar.All(255));
                                Cv2.WarpAffine(rectangle, rectangleReference, inverse, new Size(32, 32), InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.Black);
                                var expectedRectangle = Det(0, 0, 32, 32);
                                for (int iy = 0; iy < 32; iy++)
                                for (int ix = 0; ix < 32; ix++) expectedRectangle["mask_array"][iy][ix] = rectangleReference.At<byte>(iy, ix);
                                ((JObject)transformed[0]["sample_results"][0]).Remove("mask_array");
                                check("bbox-affine-" + spec[0] + "-" + spec[1] + (native ? "-native" : "-python"), expectedRectangle, transformed, true, "iou", 1);
                            }
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
                    foreach (bool badTransform in new[] { false, true })
                    {
                        var badInput = Entries((JObject)missing.DeepClone());
                        if (badTransform)
                            badInput[0]["transform"] = new JObject { ["original_width"] = 32, ["original_height"] = 32,
                                ["output_size"] = new JArray(32, 32), ["affine_2x3"] = new JArray(0, 0, 0, 0, 0, 0) };
                        else badInput[0]["sample_results"][0]["mask_array"] = new JArray(new JArray(256));
                        var invalidModule = new ResultFilterRegion(1, properties: new Dictionary<string, object> { ["filter_mode"] = "mask" });
                        invalidModule.ExtraInputsIn.Add(new ModuleChannel(null, Entries(Det(8, 9, 10, 10))));
                        bool failed = false;
                        try { invalidModule.Process(images, badInput); } catch (ArgumentException) { failed = true; }
                        if (!failed) throw new Exception("invalid mask/transform silently replaced by bbox");
                        Console.WriteLine("PASS invalid-mask-or-transform-no-fallback"); count++;
                    }
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
