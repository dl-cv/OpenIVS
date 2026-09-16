using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DlcvCSharpCppBridge;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpCppTest
{
    internal static class CommandLineTest
    {
        internal static JArray NativeModules()
        {
            var modules = new JArray();
            using (var process = Process.GetCurrentProcess())
                foreach (ProcessModule module in process.Modules)
                {
                    string name = module.ModuleName.ToLowerInvariant();
                    if (name == "dlcv_infer.dll" || name == "dlcv_infer_v.dll" || name == "dlcv_infer_cpp.dll")
                        modules.Add(new JObject { ["name"] = name, ["path"] = module.FileName });
                }
            return modules;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static JObject Info(string text)
        {
            var info = JObject.Parse(text);
            Check(info["code"]?.Value<int>() == 0 && info["model_info"] is JObject, "模型信息不完整");
            return info;
        }

        public static int Run(string[] args)
        {
            string output = null;
            var report = new JObject { ["status"] = "failed", ["scope"] = "model-command-line" };
            try
            {
                if (args.Length % 2 != 1) throw new ArgumentException("命令行参数必须成对提供");
                string model = null, order = "csharp-first";
                int device = 0;
                var seen = new HashSet<string>();
                for (int i = 1; i < args.Length; i += 2)
                {
                    if (!seen.Add(args[i])) throw new ArgumentException("参数重复：" + args[i]);
                    switch (args[i])
                    {
                        case "--output": output = Path.GetFullPath(args[i + 1]); break;
                        case "--model": model = Path.GetFullPath(args[i + 1]); break;
                        case "--device": device = int.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
                        case "--release-order": order = args[i + 1]; break;
                        default: throw new ArgumentException("未知参数：" + args[i]);
                    }
                }
                if (model == null || output == null) throw new ArgumentException("必须指定 --model 和 --output");
                if (order != "csharp-first" && order != "cpp-first") throw new ArgumentException("释放顺序须为 csharp-first 或 cpp-first");
                report["release_order"] = order;
                using (var session = new ModelSession())
                {
                    session.LoadCSharp(model, device);
                    report["csharp_info"] = Info(session.GetCSharpInfo());
                    session.ConvertToCpp();
                    int index = session.CSharpModelIndex;
                    Check(index >= 0 && session.CppModelIndex == index, "两侧模型编号不一致");
                    report["model_index"] = index;
                    report["cpp_info"] = Info(session.GetCppInfo());
                    report["native_modules"] = NativeModules();
                    if (order == "csharp-first")
                    {
                        session.ReleaseCSharp(); session.ReleaseCSharp();
                        Check(!session.HasCSharpModel && session.HasCppModel, "释放 C# 后状态错误");
                        using (var fresh = new CppModel(index)) report["retained_info"] = Info(fresh.GetModelInfo());
                        session.ReleaseCpp(); session.ReleaseCpp();
                    }
                    else
                    {
                        session.ReleaseCpp(); session.ReleaseCpp();
                        Check(session.HasCSharpModel && !session.HasCppModel, "释放 C++ 后状态错误");
                        report["retained_info"] = Info(session.GetCSharpInfo());
                        session.ConvertToCpp(); session.ReleaseCpp(); session.ReleaseCSharp();
                    }
                    Check(!session.HasCSharpModel && !session.HasCppModel, "两侧状态未清理");
                    bool expired = false;
                    try { using (var stale = new CppModel(index)) { } }
                    catch (InvalidOperationException) { expired = true; }
                    Check(expired, "最终释放后共享编号仍有效");
                    report["final_index_expired"] = true;
                }
                report["status"] = "passed";
            }
            catch (Exception ex) { report["error"] = ex.ToString(); }
            try
            {
                if (output == null) Console.Error.WriteLine(report.ToString());
                else SelfTest.WriteReport(output, report);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            return (string)report["status"] == "passed" ? 0 : 1;
        }
    }
}
