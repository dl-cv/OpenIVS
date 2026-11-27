using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class BarcodeReaderSelfTest
    {
        private static JToken WithoutTiming(JToken result)
        {
            var copy = result.DeepClone();
            foreach (var detection in copy) ((JObject)detection["extra_info"]).Remove("barcode_enhance_ms");
            return copy;
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
                    if (item["properties"]?["enhance"]?.Value<bool>() == true)
                    using (var gray = new Mat())
                    {
                        if (raw.Channels() == 1) raw.CopyTo(gray);
                        else Cv2.CvtColor(raw, gray, raw.Channels() == 3 ? ColorConversionCodes.BGR2GRAY : ColorConversionCodes.BGRA2GRAY);
                        for (int attempt = 0; attempt < 5; ++attempt)
                        using (var projected = BarcodeEnhancer.Enhance(gray, attempt))
                        {
                            Cv2.ImEncode(".png", projected, out byte[] bytes);
                            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(args[3]), item["name"] + "-" + attempt + ".png"), bytes);
                        }
                    }
                    using (var padded = new Mat(image.Rows, image.Cols + 13, image.Type(), Scalar.All(255)))
                    using (var roi = new Mat(padded, new Rect(0, 0, image.Cols, image.Rows)))
                    {
                        image.CopyTo(roi);
                        var wrap = new ModuleImage(roi, roi, new TransformationState(roi.Width, roi.Height), 7) { UniqueId = "barcode-roi" };
                        var reader = new BarcodeReader(1, properties: item["properties"]?.ToObject<Dictionary<string, object>>());
                        var local = reader.Process(new List<ModuleImage> { wrap }).ResultList;
                        if ((int)local[0]["origin_index"] != 7 || (string)local[0]["unique_id"] != "barcode-roi") throw new Exception("Metadata lost");
                        JToken result = flow.InferOneOutJson(roi);
                        var clock = Stopwatch.StartNew();
                        for (int i = 0; i < 20; ++i) reader.Process(new List<ModuleImage> { wrap });
                        clock.Stop();
                        double meanMs = clock.Elapsed.TotalMilliseconds / 20;
                        clock.Restart();
                        var concurrent = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                            reader.Process(new List<ModuleImage> { wrap }).ResultList[0]["sample_results"])).ToArray();
                        Task.WaitAll(concurrent);
                        double fourMs = clock.Elapsed.TotalMilliseconds;
                        foreach (var task in concurrent)
                            if (!JToken.DeepEquals(WithoutTiming(local[0]["sample_results"]), WithoutTiming(task.Result))) throw new Exception("Concurrent result changed");
                        if (Cv2.Norm(image, roi, NormTypes.INF) != 0) throw new Exception("Input modified");
                        if (item["expected"] != null && !JToken.DeepEquals(item["expected"],
                            new JArray(local[0]["sample_results"].Select(d => (string)d["category_name"]).OrderBy(s => s)))) throw new Exception("Unexpected decoded text: " + item["name"]);
                        output.Add(new JObject { ["name"] = item["name"], ["local"] = local[0]["sample_results"], ["flow"] = result,
                            ["mean_ms"] = meanMs, ["four_concurrent_ms"] = fourMs });
                        var qrOnly = new BarcodeReader(1, properties: new Dictionary<string, object> { ["formats"] = "QRCode", ["enhance"] = true }).Process(new List<ModuleImage> { wrap });
                        foreach (var det in qrOnly.ResultList[0]["sample_results"])
                            if (!((string)det["extra_info"]["barcode_format"]).Contains("QR")) throw new Exception("Enhanced format filter failed");
                        var filtered = new BarcodeReader(1, properties: new Dictionary<string, object> { ["formats"] = "DataMatrix" }).Process(new List<ModuleImage> { wrap });
                        foreach (var det in filtered.ResultList[0]["sample_results"])
                            if ((string)det["extra_info"]["barcode_format"] != "Data Matrix") throw new Exception("Format filter failed");
                        foreach (var properties in new[] {new Dictionary<string, object> { ["formats"] = "invalid-format" }, new Dictionary<string, object> { ["try_rotate"] = "false" }})
                        {
                            bool failed = false;
                            try { new BarcodeReader(1, properties: properties).Process(new List<ModuleImage> { wrap }); }
                            catch (ArgumentException) { failed = true; }
                            if (!failed) throw new Exception("Invalid properties accepted");
                        }
                    }
                }
            }
            if (new BarcodeReader(1).Process().ResultList.Count != 0) throw new Exception("Empty batch failed");
            File.WriteAllText(args[3], output.ToString(), new System.Text.UTF8Encoding(false));
            Console.WriteLine("Barcode selftest passed: " + output.Count);
            return 0;
        }
    }
}
