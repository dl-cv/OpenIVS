using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;

namespace DlcvCSharpTest
{
    internal static partial class Program
    {
        private static string StageImageHash(Mat image)
        {
            if (!image.IsContinuous()) throw new InvalidOperationException("测试图像必须连续存储");
            var bytes = new byte[checked(image.Rows * image.Cols * image.Channels())];
            System.Runtime.InteropServices.Marshal.Copy(image.Data, bytes, 0, bytes.Length);
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        // 对真实流程的执行前缀分别增加结果出口，生产模型与归档保持不变。
        private static int RunFlowStageComparison(string[] args)
        {
            if (args.Length != 6)
                throw new ArgumentException("flow-stage-compare <model> <image> <device> <output-dir> <node-ids>");
            string output = Path.GetFullPath(args[4]);
            if (Directory.Exists(output)) throw new IOException("结果目录已存在");
            Directory.CreateDirectory(output);
            var selected = new HashSet<int>(args[5].Split(',').Select(int.Parse));
            using (var bgr = Cv2.ImRead(args[2], ImreadModes.Color))
            using (var rgb = new Mat())
            using (var owner = new Model(args[1], int.Parse(args[3])))
            {
                if (bgr.Empty()) throw new IOException("测试图片读取失败");
                Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
                var loader = ResolveAndValidateSharedIndex(owner.modelIndex, "dvs", "阶段比较");
                JObject descriptor = loader.GetDvsModel(owner.modelIndex);
                var pipeline = (JObject)descriptor["pipeline"];
                var nodes = ((JArray)pipeline["nodes"]).OfType<JObject>()
                    .OrderBy(n => n.Value<int?>("order") ?? int.MaxValue - 1)
                    .ThenBy(n => n.Value<int>("id")).ToList();
                var knownIds = new HashSet<int>(nodes.Select(n => n.Value<int>("id")));
                if (selected.Count == 0 || selected.Any(id => !knownIds.Contains(id)))
                    throw new ArgumentException("指定节点不存在");
                string imageHash = StageImageHash(rgb);
                var report = new JArray();
                foreach (JObject node in nodes.Where(n => selected.Contains(n.Value<int>("id"))))
                {
                    int target = node.Value<int>("id");
                    JObject variant = (JObject)pipeline.DeepClone();
                    var prefix = new JArray(nodes.Take(nodes.IndexOf(node) + 1)
                        .Where(n => n.Value<string>("type") != "output/return_json")
                        .Select(n => n.DeepClone()));
                    JObject source = prefix.OfType<JObject>().Single(n => n.Value<int>("id") == target);
                    var ports = (JArray)source["outputs"];
                    int imagePort = ports.Select((p, i) => new { p, i }).First(p => p.p.Value<string>("type") == "image_chan").i;
                    int resultPort = ports.Select((p, i) => new { p, i }).First(p => p.p.Value<string>("type") == "result_chan").i;
                    int maxLink = nodes.SelectMany(n => (n["outputs"] as JArray ?? new JArray()).OfType<JObject>())
                        .SelectMany(p => (p["links"] as JArray ?? new JArray()).Values<int>()).DefaultIfEmpty(0).Max();
                    int imageLink = checked(maxLink + 1), resultLink = checked(maxLink + 2);
                    int returnId = checked(knownIds.Max() + 1);
                    foreach (var pair in new[] { new[] { imagePort, imageLink }, new[] { resultPort, resultLink } })
                    {
                        var links = ports[pair[0]]["links"] as JArray ?? new JArray();
                        links.Add(pair[1]); ports[pair[0]]["links"] = links;
                    }
                    prefix.Add(new JObject {
                        ["id"] = returnId, ["type"] = "output/return_json", ["order"] = int.MaxValue,
                        ["properties"] = new JObject { ["poly_epsilon"] = 1.5, ["min_contour_area"] = 4 },
                        ["inputs"] = new JArray(
                            new JObject { ["name"] = "image", ["type"] = "image_chan", ["slot_index"] = 0, ["link"] = imageLink },
                            new JObject { ["name"] = "results", ["type"] = "result_chan", ["slot_index"] = 1, ["link"] = resultLink }),
                        ["outputs"] = new JArray()
                    });
                    variant["nodes"] = prefix;
                    var rootLinks = variant["links"] as JArray ?? new JArray();
                    rootLinks.Add(new JArray(imageLink, target, imagePort, returnId, 0, "image_chan"));
                    rootLinks.Add(new JArray(resultLink, target, resultPort, returnId, 1, "result_chan"));
                    variant["links"] = rootLinks;
                    var retained = new HashSet<int>(prefix.OfType<JObject>().Select(n => n.Value<int>("id")));
                    var request = new JObject {
                        ["schema_version"] = descriptor["schema_version"].DeepClone(),
                        ["dvs_type"] = descriptor["dvs_type"].DeepClone(),
                        ["model_path"] = descriptor["model_path"].DeepClone(),
                        ["device_id"] = descriptor["device_id"].DeepClone(),
                        ["pipeline"] = variant,
                        ["model_bindings"] = new JArray(((JArray)descriptor["model_bindings"]).OfType<JObject>()
                            .Where(b => retained.Contains(b.Value<int>("node_id"))).Select(b => b.DeepClone()))
                    };
                    int index = loader.RegisterDvsModel(request.ToString(Formatting.None));
                    Model csharp = null;
                    bool nativeHeld = false;
                    try
                    {
                        csharp = ModelFactory.CreateFromIndex(index);
                        var parameters = new JObject { ["threshold"] = 0.05, ["with_mask"] = true };
                        JToken cs = JToken.FromObject(csharp.InferOneOutJson(rgb, parameters));
                        if (StageImageHash(rgb) != imageHash) throw new InvalidOperationException("C# 执行后输入图像变化");
                        nativeHeld = true;
                        JObject nativeInfo = NativeCGetModelInfo(index, "阶段模型信息");
                        EnsureModelInfosMatch(csharp.GetModelInfo(), nativeInfo, "阶段模型信息");
                        JToken cpp = NativeCInferJson(index, rgb, parameters, "阶段推理");
                        if (StageImageHash(rgb) != imageHash) throw new InvalidOperationException("C++ 执行后输入图像变化");
                        string difference = null;
                        try { EnsureNativeJsonResultsMatch(cs, cpp, "节点 " + target); }
                        catch (Exception e) { difference = e.Message; }
                        var row = new JObject { ["node_id"] = target, ["type"] = node["type"],
                            ["device"] = int.Parse(args[3]), ["equal"] = difference == null,
                            ["input_sha256"] = imageHash, ["model_bindings"] = request["model_bindings"].DeepClone(),
                            ["image_output_port"] = imagePort, ["result_output_port"] = resultPort,
                            ["csharp"] = cs, ["cpp"] = cpp, ["difference"] = difference };
                        File.WriteAllText(Path.Combine(output, "node-" + target + ".json"), row.ToString(Formatting.Indented), new System.Text.UTF8Encoding(false));
                        report.Add(new JObject { ["node_id"] = target, ["type"] = node["type"], ["equal"] = difference == null });
                        Console.WriteLine("node=" + target + " equal=" + (difference == null));
                    }
                    finally
                    {
                        if (nativeHeld) NativeCFreeModel(index);
                        if (csharp != null) csharp.Dispose();
                        loader.FreeModelIndex(index);
                    }
                }
                var modules = new JArray();
                foreach (System.Diagnostics.ProcessModule module in System.Diagnostics.Process.GetCurrentProcess().Modules)
                {
                    if (!module.ModuleName.StartsWith("dlcv_infer", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = File.OpenRead(module.FileName))
                    using (var hash = System.Security.Cryptography.SHA256.Create())
                        modules.Add(new JObject { ["path"] = module.FileName,
                            ["sha256"] = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() });
                }
                File.WriteAllText(Path.Combine(output, "runtime.json"), modules.ToString(Formatting.Indented), new System.Text.UTF8Encoding(false));
                File.WriteAllText(Path.Combine(output, "summary.json"), report.ToString(Formatting.Indented), new System.Text.UTF8Encoding(false));
                return report.All(r => r.Value<bool>("equal")) ? 0 : 1;
            }
        }
    }
}
