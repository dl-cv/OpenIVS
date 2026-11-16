using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using dlcv_infer_csharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpTest
{
    internal static class FlowModelsMemorySelfTest
    {
        internal static int Run(string[] args)
        {
            if (args.Length < 3) throw new ArgumentException("用法: flow-models-memory-selftest <输出JSON> <模型1> [模型2 ...]");
            string output = Path.GetFullPath(args[1]);
            if (File.Exists(output)) throw new IOException("结果文件已存在: " + output);
            for (int i = 2; i < args.Length; i++)
                if (!File.Exists(args[i])) throw new FileNotFoundException("模型文件不存在", args[i]);

            var models = new List<Model>();
            var stages = new JArray();
            var report = new JObject
            {
                ["pid"] = Process.GetCurrentProcess().Id,
                ["api_assembly"] = typeof(Model).Assembly.Location,
                ["device_id"] = 0,
                ["stages"] = stages
            };
            try
            {
                Sample(stages, "before_load", null);
                for (int i = 2; i < args.Length; i++)
                {
                    var watch = Stopwatch.StartNew();
                    models.Add(new Model(args[i], 0, false, false));
                    watch.Stop();
                    Sample(stages, "loaded_" + models.Count, args[i]);
                    ((JObject)stages[stages.Count - 1])["load_ms"] = watch.Elapsed.TotalMilliseconds;
                }
                report["loaded_models"] = Utils.GetAllModels();
                var nativeModules = new JArray();
                foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
                    if (module.ModuleName == "dlcv_infer.dll" || module.ModuleName == "dlcv_infer_v.dll")
                        nativeModules.Add(new JObject { ["path"] = module.FileName, ["version"] = module.FileVersionInfo.FileVersion });
                report["native_modules"] = nativeModules;
                report["success"] = true;
                return 0;
            }
            catch (Exception error)
            {
                report["success"] = false;
                report["error"] = error.ToString();
                throw;
            }
            finally
            {
                for (int i = models.Count - 1; i >= 0; i--) models[i].Dispose();
                Sample(stages, "after_release", null);
                report["remaining_models"] = Utils.GetAllModels();
                File.WriteAllText(output, report.ToString(Formatting.Indented), new UTF8Encoding(false));
            }
        }

        private static void Sample(JArray stages, string stage, string model)
        {
            Thread.Sleep(1500);
            long total;
            var instances = GpuProcessMemorySample.Read(model != null, out total);
            using (var process = Process.GetCurrentProcess())
            {
                stages.Add(new JObject
                {
                    ["stage"] = stage, ["model"] = model,
                    ["gpu_dedicated_bytes"] = total,
                    ["gpu_instances"] = instances,
                    ["private_bytes"] = process.PrivateMemorySize64,
                    ["working_set_bytes"] = process.WorkingSet64
                });
            }
            Console.WriteLine(stage + ": " + (total / 1048576.0).ToString("F2") + " MiB");
        }
    }
}
