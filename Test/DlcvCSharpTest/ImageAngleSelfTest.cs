using System;
using System.Collections.Generic;
using System.IO;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class ImageAngleSelfTest
    {
        public static int Run(string[] args)
        {
            if (args.Length != 2) throw new ArgumentException("image-angle-selftest <cases.json>");
            new GraphExecutor(new List<Dictionary<string, object>>());
            var type = ModuleRegistry.Get("post_process/image_angle");
            if (type == null) throw new Exception("image_angle not registered");
            using (var mat = new Mat(4, 5, MatType.CV_8UC3, Scalar.All(7)))
            {
                foreach (JObject test in JArray.Parse(File.ReadAllText(args[1])))
                {
                    var images = new List<ModuleImage>();
                    foreach (var matrix in (JArray)test["matrices"])
                        images.Add(new ModuleImage(mat, mat, new TransformationState(5, 4, affine2x3:
                            matrix.Type == JTokenType.Null ? null : matrix.ToObject<double[]>()), 7));
                    var module = (BaseModule)Activator.CreateInstance(type, 1, null, test["properties"].ToObject<Dictionary<string, object>>(), null);
                    var input = (JArray)test["input"];
                    var original = input.DeepClone();
                    for (int run = 0; run < 2; run++)
                    {
                        ModuleIO output = null;
                        bool rejected = false;
                        try { output = module.Process(images, input); }
                        catch (ArgumentException) { rejected = true; }
                        if (rejected != (test.Value<bool?>("error") ?? false)) throw new Exception(test["name"] + " rejection mismatch");
                        if (!rejected && (!JToken.DeepEquals(output.ResultList, test["expected"]) || !ReferenceEquals(output.ImageList, images)))
                            throw new Exception(test["name"] + " output mismatch: " + output.ResultList);
                        if (!JToken.DeepEquals(input, original) || mat.At<Vec3b>(0, 0).Item0 != 7) throw new Exception("upstream mutated");
                    }
                    Console.WriteLine("PASS " + test["name"]);
                }
            }
            return 0;
        }
    }
}
