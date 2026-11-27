using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class DpmSelfTest
    {
        private static JToken WithoutTimings(JToken value)
        {
            if (value is JObject obj)
            {
                var result = new JObject();
                foreach (var property in obj.Properties())
                    if (!property.Name.EndsWith("_ms", StringComparison.Ordinal))
                        result[property.Name] = WithoutTimings(property.Value);
                return result;
            }
            if (value is JArray array)
            {
                var result = new JArray();
                foreach (var item in array) result.Add(WithoutTimings(item));
                return result;
            }
            return value.DeepClone();
        }

        private static JObject ReadCrop(JObject item, string outputDir, bool save)
        {
            using (var gray = Cv2.ImDecode(File.ReadAllBytes((string)item["path"]), ImreadModes.Grayscale))
            {
                byte[] bytes = new byte[gray.Width * gray.Height];
                Marshal.Copy(gray.Data, bytes, 0, bytes.Length);
                var enhanced = DpmDataMatrixEnhancer.EnhanceAndDecode(bytes, gray.Width, gray.Height);
                if (save)
                using (var image = new Mat(enhanced.EnhancedHeight, enhanced.EnhancedWidth, MatType.CV_8UC1))
                {
                    Marshal.Copy(enhanced.EnhancedGray, 0, image.Data, enhanced.EnhancedGray.Length);
                    Cv2.ImEncode(".png", image, out byte[] png);
                    File.WriteAllBytes(Path.Combine(outputDir, (string)item["name"] + ".png"), png);
                }
                // 非连续输入须走实际模块，覆盖图像步长、enhance 开关和输出坐标。
                using (var padded = new Mat(gray.Height, gray.Width + 13, MatType.CV_8UC1))
                using (var roi = new Mat(padded, new Rect(0, 0, gray.Width, gray.Height)))
                {
                    gray.CopyTo(roi);
                    var wrap = new ModuleImage(roi, roi, new TransformationState(roi.Width, roi.Height), 7);
                    var reader = new BarcodeReader(1, properties: new Dictionary<string, object> { ["enhance"] = true, ["formats"] = "DataMatrix" });
                    var local = reader.Process(new List<ModuleImage> { wrap }).ResultList[0]["sample_results"];
                    if (Cv2.Norm(roi, gray, NormTypes.INF) != 0) throw new Exception("DPM modified input");
                    return new JObject { ["name"] = item["name"], ["mode"] = enhanced.DecodeMode,
                        ["enhanced"] = Convert.ToBase64String(enhanced.EnhancedGray), ["text"] = enhanced.DecodedText,
                        ["ms"] = enhanced.EndToEndMs, ["local"] = local };
                }
            }
        }

        public static int Run(string[] args)
        {
            if (args.Length != 3) throw new ArgumentException("dpm-selftest <manifest.json> <output.json>");
            var manifest = JObject.Parse(File.ReadAllText(args[1]));
            string outputDir = Path.GetDirectoryName(Path.GetFullPath(args[2]));
            Directory.CreateDirectory(outputDir);
            var crops = (JArray)manifest["crops"];
            if (crops.Count < 4) throw new ArgumentException("DPM manifest requires at least four crops");
            var concurrent = new JObject[4];
            var watch = Stopwatch.StartNew();
            Parallel.For(0, 4, i => concurrent[i] = ReadCrop((JObject)crops[i], outputDir, false));
            double concurrentMs = watch.Elapsed.TotalMilliseconds;
            var output = new JArray();
            foreach (JObject crop in crops) output.Add(ReadCrop(crop, outputDir, true));
            for (int i = 0; i < 4; ++i)
                if ((string)concurrent[i]["enhanced"] != (string)output[i]["enhanced"] ||
                    (string)concurrent[i]["mode"] != (string)output[i]["mode"] ||
                    !JToken.DeepEquals(WithoutTimings(concurrent[i]["local"]), WithoutTimings(output[i]["local"])))
                    throw new Exception("Concurrent DPM mismatch");
            var flows = new JArray();
            if (manifest["model"] != null)
            using (var model = new dlcv_infer_csharp.Model((string)manifest["model"], 0))
            {
                foreach (JObject item in (JArray)manifest["images"])
                using (var bgr = Cv2.ImDecode(File.ReadAllBytes((string)item["path"]), ImreadModes.Color))
                using (var rgb = new Mat())
                {
                    Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
                    model.InferOneOutJson(rgb);
                    watch.Restart();
                    var result = JToken.FromObject(model.InferOneOutJson(rgb));
                    flows.Add(new JObject { ["name"] = item["name"], ["ms"] = watch.Elapsed.TotalMilliseconds, ["result"] = result });
                }
            }
            var report = new JObject { ["crops"] = output, ["flow"] = flows, ["concurrent_ms"] = concurrentMs };
            File.WriteAllText(args[2], report.ToString(), new System.Text.UTF8Encoding(false));
            Console.WriteLine("DPM C# selftest: " + output.Count);
            return 0;
        }
    }
}
