using System;
using System.IO;
using System.Windows.Forms;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvDemo2
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "--test-detect")
            {
                RunDetectOnlyTest(args);
                return;
            }

            if (args.Length >= 4)
            {
                RunHeadless(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        private static void RunDetectOnlyTest(string[] args)
        {
            // args: --test-detect model_path image_path
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: --test-detect <model_path> <image_path>");
                Environment.ExitCode = 1;
                return;
            }

            string modelPath = args[1];
            string imagePath = args[2];

            try
            {
                var model = new Model(modelPath, 0, false);
                Mat imageBgr = Cv2.ImRead(imagePath, ImreadModes.Unchanged);
                if (imageBgr == null || imageBgr.Empty())
                {
                    Console.WriteLine("Image load failed");
                    Environment.ExitCode = 1;
                    return;
                }
                Mat imageRgb = Form1.PrepareImageForModelInput(imageBgr);

                // Test Infer (used by DlcvDemo2)
                JObject inferParams = new JObject { ["with_mask"] = false };
                var resultInfer = model.Infer(imageRgb, inferParams);
                Console.WriteLine("=== Infer (DlcvDemo2 style) ===");
                Console.WriteLine($"Results: {resultInfer.SampleResults[0].Results.Count}");
                for (int i = 0; i < resultInfer.SampleResults[0].Results.Count; i++)
                {
                    var obj = resultInfer.SampleResults[0].Results[i];
                    string bbox = obj.Bbox != null ? string.Join(",", obj.Bbox) : "null";
                    Console.WriteLine($"[{i}] {obj.CategoryName} score={obj.Score:F2} bbox=[{bbox}]");
                }

                // Test InferOneOutJson (used by DlcvDemo)
                JObject inferParams2 = new JObject { ["threshold"] = 0.5f, ["with_mask"] = true };
                dynamic resultJson = model.InferOneOutJson(imageRgb, inferParams2);
                Console.WriteLine("=== InferOneOutJson (DlcvDemo style) ===");
                var jsonResults = resultJson as Newtonsoft.Json.Linq.JArray;
                if (jsonResults != null)
                {
                    Console.WriteLine($"Results: {jsonResults.Count}");
                    for (int i = 0; i < jsonResults.Count; i++)
                    {
                        var item = jsonResults[i];
                        string cat = item["category_name"]?.ToString() ?? "unknown";
                        double score = item["score"]?.Value<double>() ?? 0;
                        var bboxArr = item["bbox"] as Newtonsoft.Json.Linq.JArray;
                        string bbox = bboxArr != null ? string.Join(",", bboxArr) : "null";
                        Console.WriteLine($"[{i}] {cat} score={score:F2} bbox=[{bbox}]");
                    }
                }

                imageRgb.Dispose();
                imageBgr.Dispose();
                model.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
                Environment.ExitCode = 1;
            }
        }

        private static void RunHeadless(string[] args)
        {
            string extractModelPath = args[0];
            string componentModelPath = args[1];
            string icModelPath = args[2];
            string imagePath = args[3];
            int windowWidth = args.Length > 4 ? int.Parse(args[4]) : 2560;
            int windowHeight = args.Length > 5 ? int.Parse(args[5]) : 2560;
            int overlapX = args.Length > 6 ? int.Parse(args[6]) : 1024;
            int overlapY = args.Length > 7 ? int.Parse(args[7]) : 1024;

            var config = new Form1.SlidingWindowConfig
            {
                WindowWidth = windowWidth,
                WindowHeight = windowHeight,
                OverlapX = overlapX,
                OverlapY = overlapY
            };

            try
            {
                var result = Form1.RunHeadless(extractModelPath, componentModelPath, icModelPath, imagePath, config);
                Console.WriteLine($"SlidingWindowCount: {result.SlidingWindowCount}");
                Console.WriteLine($"MergedExtractCount: {result.MergedExtractCount}");
                Console.WriteLine($"ComponentModelResultCount: {result.ComponentModelResultCount}");
                Console.WriteLine($"IcModelResultCount: {result.IcModelResultCount}");
                Console.WriteLine($"FinalObjects: {result.FinalObjects.Count}");
                for (int i = 0; i < result.FinalObjects.Count; i++)
                {
                    var obj = result.FinalObjects[i];
                    string bbox = obj.Bbox != null ? string.Join(",", obj.Bbox) : "null";
                    Console.WriteLine($"[{i}] {obj.CategoryName} score={obj.Score:F2} bbox=[{bbox}]");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
                Environment.ExitCode = 1;
            }
        }
    }
}
