using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using dlcv_infer_csharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class BackendReuseSelfTest
    {
        internal static int Run(string[] args)
        {
            if (args == null || args.Length != 5)
            {
                Console.Error.WriteLine("用法: backend-reuse-selftest <输出JSON> <模型A> <模型B> <图片>");
                return 2;
            }
            string output = Path.GetFullPath(args[1]);
            if (File.Exists(output)) throw new IOException("结果文件已存在: " + output);
            var checks = new JArray();
            var references = new JObject();
            var report = new JObject
            {
                ["pid"] = Process.GetCurrentProcess().Id,
                ["device_id"] = 0,
                ["model_a"] = Path.GetFullPath(args[2]),
                ["model_b"] = Path.GetFullPath(args[3]),
                ["image"] = Path.GetFullPath(args[4]),
                ["image_variants"] = new JArray("原图RGB", "水平翻转RGB"),
                ["threshold"] = 0.05,
                ["with_mask"] = true,
                ["references"] = references,
                ["checks"] = checks
            };
            try
            {
                string hashA = FileDigest(args[2]);
                string hashB = FileDigest(args[3]);
                report["model_a_sha256"] = hashA;
                report["model_b_sha256"] = hashB;
                Check(checks, "输入包密文不同", hashA != hashB, new JObject { ["expected"] = "different", ["actual"] = hashA == hashB ? "same" : "different" });
                using (var bgr = Cv2.ImRead(args[4], ImreadModes.Color))
                using (var rgb = new Mat())
                using (var flipped = new Mat())
                {
                    if (bgr.Empty()) throw new InvalidOperationException("图片读取失败");
                    Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
                    Cv2.Flip(rgb, flipped, FlipMode.Y);
                    var images = new[] { rgb, flipped };
                    var paths = new[] { args[2], args[3] };
                    var expected = new JToken[2][];
                    for (int i = 0; i < paths.Length; i++)
                    {
                        using (var model = new Model(paths[i], 0, false, false))
                        {
                            expected[i] = new[] { Infer(model, images[0]), Infer(model, images[1]) };
                            references["model_" + i] = new JArray(expected[i][0].DeepClone(), expected[i][1].DeepClone());
                        }
                    }
                    report["variant_results_differ"] = new JArray(
                        !JToken.DeepEquals(expected[0][0], expected[0][1]),
                        !JToken.DeepEquals(expected[1][0], expected[1][1]));
                    Check(checks, "参考图片产生实际识别结果", HasObjects(expected[0][0]) || HasObjects(expected[0][1]),
                        new JObject { ["expected"] = "至少一个输入产生非空结果" });
                    VerifyPair(paths, images, expected, false, checks);
                    VerifyPair(paths, images, expected, true, checks);
                    for (int i = 0; i < paths.Length; i++)
                    {
                        using (var model = new Model(paths[i], 0, false, false))
                        {
                            Compare(checks, "全部释放后重载模型" + i, expected[i][0], Infer(model, images[0]));
                            int index = model.modelIndex;
                            string resourceType;
                            DllLoader loader = DllLoader.ResolveForIndex(index, out resourceType);
                            model.Dispose();
                            int actualType = loader.GetIndexType(index);
                            Check(checks, "重载模型" + i + "释放后索引消失", actualType == 0,
                                new JObject { ["model_index"] = index, ["expected_index_type"] = 0, ["actual_index_type"] = actualType });
                        }
                    }
                }
                report["remaining_models"] = Utils.GetAllModels();
                report["success"] = true;
                Console.WriteLine("真实后端并发、结果独立及双向释放检查通过");
                return 0;
            }
            catch (Exception error)
            {
                report["success"] = false;
                report["error"] = error.ToString();
                Console.Error.WriteLine(error);
                return 1;
            }
            finally
            {
                File.WriteAllText(output, report.ToString(Formatting.Indented), new UTF8Encoding(false));
            }
        }

        private static void VerifyPair(string[] paths, Mat[] images, JToken[][] expected, bool releaseSecondFirst, JArray checks)
        {
            using (var first = new Model(paths[0], 0, false, false))
            using (var second = new Model(paths[1], 0, false, false))
            {
                var models = new[] { first, second };
                int[] indexes = { first.modelIndex, second.modelIndex };
                string resourceType;
                var loaders = new[]
                {
                    DllLoader.ResolveForIndex(indexes[0], out resourceType),
                    DllLoader.ResolveForIndex(indexes[1], out resourceType)
                };
                string label = releaseSecondFirst ? "先释放B" : "先释放A";
                Check(checks, label + "独立模型索引", indexes[0] != indexes[1],
                    new JObject { ["expected"] = "different", ["actual"] = new JArray(indexes) });
                var held = new[] { Infer(first, images[0]), Infer(second, images[0]) };
                var saved = new[] { held[0].DeepClone(), held[1].DeepClone() };
                for (int round = 0; round < 2; round++)
                {
                    for (int variant = 0; variant < images.Length; variant++)
                    {
                        Mat inputA = images[variant];
                        int variantB = 1 - variant;
                        Mat inputB = images[variantB];
                        using (var start = new Barrier(2))
                        {
                            Task<JToken> taskA = Task.Run(() => { start.SignalAndWait(); return Infer(first, inputA); });
                            Task<JToken> taskB = Task.Run(() => { start.SignalAndWait(); return Infer(second, inputB); });
                            Task.WaitAll(taskA, taskB);
                            Compare(checks, label + "并发模型A轮次" + round + "输入" + variant, expected[0][variant], taskA.Result);
                            Compare(checks, label + "并发模型B轮次" + round + "输入" + variantB, expected[1][variantB], taskB.Result);
                        }
                    }
                }
                for (int i = 0; i < held.Length; i++)
                    Compare(checks, label + "持有的模型" + i + "结果未被覆盖", saved[i], held[i]);
                int released = releaseSecondFirst ? 1 : 0;
                int survivor = 1 - released;
                models[released].Dispose();
                int releasedType = loaders[released].GetIndexType(indexes[released]);
                int survivorType = loaders[survivor].GetIndexType(indexes[survivor]);
                Check(checks, label + "已释放索引消失", releasedType == 0,
                    new JObject { ["model_index"] = indexes[released], ["expected_index_type"] = 0, ["actual_index_type"] = releasedType });
                Check(checks, label + "存活索引有效", survivorType == 1,
                    new JObject { ["model_index"] = indexes[survivor], ["expected_index_type"] = 1, ["actual_index_type"] = survivorType });
                for (int variant = 0; variant < images.Length; variant++)
                    Compare(checks, label + "存活模型输入" + variant, expected[survivor][variant], Infer(models[survivor], images[variant]));
                models[survivor].Dispose();
                for (int i = 0; i < indexes.Length; i++)
                {
                    int actualType = loaders[i].GetIndexType(indexes[i]);
                    Check(checks, label + "最后释放索引" + i, actualType == 0,
                        new JObject { ["model_index"] = indexes[i], ["expected_index_type"] = 0, ["actual_index_type"] = actualType });
                }
            }
        }

        private static JToken Infer(Model model, Mat image)
        {
            object result = model.InferOneOutJson(image, new JObject
            {
                ["threshold"] = 0.05,
                ["with_mask"] = true
            });
            return result as JToken ?? JToken.FromObject(result);
        }

        private static bool HasObjects(JToken result)
        {
            var items = result as JArray ?? result["result_list"] as JArray;
            return items != null && items.Count > 0;
        }

        private static void Compare(JArray checks, string name, JToken expected, JToken actual)
        {
            bool passed = JToken.DeepEquals(expected, actual);
            var evidence = new JObject
            {
                ["expected_sha256"] = JsonDigest(expected),
                ["actual_sha256"] = JsonDigest(actual)
            };
            if (!passed)
            {
                evidence["expected"] = expected.DeepClone();
                evidence["actual"] = actual.DeepClone();
            }
            Check(checks, name, passed, evidence);
        }

        private static void Check(JArray checks, string name, bool passed, JObject evidence)
        {
            evidence["name"] = name;
            evidence["passed"] = passed;
            checks.Add(evidence);
            if (!passed) throw new InvalidOperationException(name);
        }

        private static string FileDigest(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string JsonDigest(JToken value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToString(Formatting.None))))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
