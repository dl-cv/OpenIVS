using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class BarcodeReaderSelfTest
    {
        private static void CheckSharedOriginals()
        {
            using (var roi = new Mat(40, 40, MatType.CV_8UC1, Scalar.All(128)))
            using (var first = new Mat(48, 48, MatType.CV_8UC3, Scalar.All(96)))
            using (var second = new Mat(48, 48, MatType.CV_8UC3, Scalar.All(160)))
            {
                var state = new TransformationState(48, 48, affine2x3: new double[] { 1, 0, 0, 0, 1, 0 });
                var images = new List<ModuleImage> {
                    new ModuleImage(roi, first, state, 0), new ModuleImage(roi, first, state, 0),
                    new ModuleImage(roi, second, state, 1) };
                var reader = new BarcodeReader(1, properties: new Dictionary<string, object> { ["enhance"] = true });
                var results = reader.Process(images).ResultList;
                if (results.Count != 3 || results.Where((entry, index) =>
                    (int)entry["index"] != index || entry["sample_results"].HasValues).Any())
                    throw new Exception("共享原图的结果顺序或空白图结果错误");
                images.Add(null);
                bool rejected = false;
                try { reader.Process(images); }
                catch (ArgumentException) { rejected = true; }
                if (!rejected) throw new Exception("无效图像未被拒绝");
                if (first.At<Vec3b>(0, 0).Item0 != 96 || second.At<Vec3b>(0, 0).Item0 != 160 || roi.At<byte>(0, 0) != 128)
                    throw new Exception("读码修改或释放了调用方图像");
            }
        }

        // barcode-selftest <manifest.json> <flow.json> <output.json>
        public static int Run(string[] args)
        {
            if (args.Length != 4) throw new ArgumentException("barcode-selftest <manifest.json> <flow.json> <output.json>");
            var output = new JArray();
            using (var flow = new FlowGraphModel())
            {
                var report = flow.Load(args[2]);
                if ((int)report["code"] != 0) throw new Exception(report.ToString());
                foreach (JObject item in JArray.Parse(File.ReadAllText(args[1])))
                using (var raw = Cv2.ImDecode(File.ReadAllBytes((string)item["path"]), ImreadModes.Unchanged))
                using (var image = new Mat())
                {
                    if (raw.Empty()) throw new Exception("Missing input: " + item["path"]);
                    if (raw.Channels() == 3) Cv2.CvtColor(raw, image, ColorConversionCodes.BGR2RGB);
                    else if (raw.Channels() == 4) Cv2.CvtColor(raw, image, ColorConversionCodes.BGRA2RGBA);
                    else raw.CopyTo(image);
                    using (var originalRaw = item["original_path"] == null ? new Mat() :
                        Cv2.ImDecode(File.ReadAllBytes((string)item["original_path"]), ImreadModes.Unchanged))
                    using (var original = new Mat())
                    using (var padded = new Mat(image.Rows, image.Cols + 13, image.Type(), Scalar.All(255)))
                    using (var roi = new Mat(padded, new Rect(0, 0, image.Cols, image.Rows)))
                    {
                        image.CopyTo(roi);
                        TransformationState transform = new TransformationState(roi.Width, roi.Height);
                        if (item["original_path"] != null)
                        {
                            if (originalRaw.Empty()) throw new Exception("Missing original: " + item["original_path"]);
                            if (originalRaw.Channels() == 3) Cv2.CvtColor(originalRaw, original, ColorConversionCodes.BGR2RGB);
                            else if (originalRaw.Channels() == 4) Cv2.CvtColor(originalRaw, original, ColorConversionCodes.BGRA2RGBA);
                            else originalRaw.CopyTo(original);
                            transform = new TransformationState(original.Width, original.Height,
                                affine2x3: item["original_to_roi"].Values<double>().ToArray());
                        }
                        var wrap = new ModuleImage(roi, original.Empty() ? roi : original, transform, 7) { UniqueId = "barcode-roi" };
                        var properties = new Dictionary<string, object>();
                        foreach (string key in new[] { "enhance", "formats", "try_rotate", "try_invert" })
                            if (item[key] != null) properties[key] = item[key];
                        var reader = new BarcodeReader(1, properties: properties);
                        var timer = Stopwatch.StartNew();
                        var local = reader.Process(new List<ModuleImage> { wrap }).ResultList;
                        double localMs = timer.Elapsed.TotalMilliseconds;
                        if ((int)local[0]["origin_index"] != 7 || (string)local[0]["unique_id"] != "barcode-roi") throw new Exception("Metadata lost");
                        if (item["expected_texts"] is JArray expected)
                        {
                            var actual = local[0]["sample_results"].Select(det => (string)det["category_name"]).OrderBy(text => text, StringComparer.Ordinal);
                            if (!actual.SequenceEqual(expected.Values<string>().OrderBy(text => text, StringComparer.Ordinal)))
                                throw new Exception("读码结果与预期不符：" + item["name"]);
                        }
                        timer.Restart();
                        JToken result = original.Empty() ? flow.InferOneOutJson(roi) : null;
                        double flowMs = timer.Elapsed.TotalMilliseconds;
                        output.Add(new JObject { ["name"] = item["name"], ["local"] = local[0]["sample_results"], ["flow"] = result,
                            ["local_ms"] = localMs, ["flow_ms"] = flowMs });
                        var filtered = new BarcodeReader(1, properties: new Dictionary<string, object> { ["formats"] = "DataMatrix" }).Process(new List<ModuleImage> { wrap });
                        foreach (var det in filtered.ResultList[0]["sample_results"])
                            if ((string)det["extra_info"]["barcode_format"] != "Data Matrix") throw new Exception("Format filter failed");
                        foreach (var invalidProperties in new[] {
                            new Dictionary<string, object> { ["formats"] = "invalid-format" },
                            new Dictionary<string, object> { ["try_rotate"] = "false" },
                            new Dictionary<string, object> { ["enhance"] = "false" } })
                        {
                            bool failed = false;
                            try { new BarcodeReader(1, properties: invalidProperties).Process(new List<ModuleImage> { wrap }); }
                            catch (ArgumentException) { failed = true; }
                            if (!failed) throw new Exception("Invalid properties accepted");
                        }
                    }
                }
            }
            if (new BarcodeReader(1).Process().ResultList.Count != 0) throw new Exception("Empty batch failed");
            CheckSharedOriginals();
            File.WriteAllText(args[3], output.ToString(), new System.Text.UTF8Encoding(false));
            Console.WriteLine("Barcode selftest passed: " + output.Count);
            return 0;
        }
    }
}
