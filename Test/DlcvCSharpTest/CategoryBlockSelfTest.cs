using System;
using System.Collections.Generic;
using System.IO;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class CategoryBlockSelfTest
    {
        public static int Run(string[] args)
        {
            if (args.Length != 2) throw new ArgumentException("category-block-selftest <cases.json>");
            new GraphExecutor(new List<Dictionary<string, object>>());
            var type = ModuleRegistry.Get("post_process/category_block");
            if (type == null) throw new Exception("category_block not registered");
            using (var mat = new Mat(4, 5, MatType.CV_8UC3, Scalar.All(7)))
            {
                var images = new List<ModuleImage> { new ModuleImage(mat, mat, new TransformationState(5, 4), 0),
                    new ModuleImage(mat, mat, new TransformationState(5, 4), 1) };
                foreach (JObject test in JArray.Parse(File.ReadAllText(args[1])))
                {
                    if (test["csharp_json_limitation"] != null)
                    {
                        Console.WriteLine("SKIP " + test["name"] + ": " + test["csharp_json_limitation"]);
                        continue;
                    }
                    var props = test["properties"].ToObject<Dictionary<string, object>>();
                    var module = (BaseModule)Activator.CreateInstance(type, 1, null, props, null);
                    var input = (JArray)test["input"];
                    var original = input.DeepClone();
                    for (int run = 0; run < 2; run++)
                    {
                        var output = module.Process(images, input);
                        if (!JToken.DeepEquals(output.ResultList, test["expected"]))
                        {
                            Console.WriteLine("FAIL " + test["name"] + " output mismatch: " + output.ResultList);
                            return 1;
                        }
                        if (!JToken.DeepEquals(input, original)) throw new Exception("upstream mutated");
                        if (!ReferenceEquals(output.ImageList, images) || !ReferenceEquals(output.ImageList[1], images[1]) || mat.At<Vec3b>(0, 0).Item0 != 7)
                            throw new Exception("image changed");
                    }
                    Console.WriteLine("PASS " + test["name"]);
                }
            }
            return 0;
        }
    }
}
