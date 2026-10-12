using System;
using System.Collections.Generic;
using System.IO;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class BarcodeReaderSelfTest
    {
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
                using (var raw = Cv2.ImRead((string)item["path"], ImreadModes.Unchanged))
                using (var image = new Mat())
                {
                    if (raw.Empty()) throw new Exception("Missing input: " + item["path"]);
                    if (raw.Channels() == 3) Cv2.CvtColor(raw, image, ColorConversionCodes.BGR2RGB);
                    else if (raw.Channels() == 4) Cv2.CvtColor(raw, image, ColorConversionCodes.BGRA2RGBA);
                    else raw.CopyTo(image);
                    using (var padded = new Mat(image.Rows, image.Cols + 13, image.Type(), Scalar.All(255)))
                    using (var roi = new Mat(padded, new Rect(0, 0, image.Cols, image.Rows)))
                    {
                        image.CopyTo(roi);
                        var wrap = new ModuleImage(roi, roi, new TransformationState(roi.Width, roi.Height), 7) { UniqueId = "barcode-roi" };
                        var reader = new BarcodeReader(1);
                        var local = reader.Process(new List<ModuleImage> { wrap }).ResultList;
                        if ((int)local[0]["origin_index"] != 7 || (string)local[0]["unique_id"] != "barcode-roi") throw new Exception("Metadata lost");
                        JToken result = flow.InferOneOutJson(roi);
                        output.Add(new JObject { ["name"] = item["name"], ["local"] = local[0]["sample_results"], ["flow"] = result });
                        for (int i = 0; i < 20; ++i) reader.Process(new List<ModuleImage> { wrap });
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
