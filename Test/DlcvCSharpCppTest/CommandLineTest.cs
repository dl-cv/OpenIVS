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

        private static void CheckExpired(int index)
        {
            bool expired = false;
            try { using (var stale = new CppModel(index)) { } }
            catch (InvalidOperationException) { expired = true; }
            Check(expired, "释放后模型编号仍有效");
        }

        private static void RunDirectLoad(ModelSession session, string model, int device,
            string mode, string order, JObject report)
        {
            // 第一项必须直接进入原生文件构造，不先建立 C# 模型。
            session.LoadCpp(model, device);
            int cppIndex = session.CppModelIndex;
            Check(cppIndex >= 0 && !session.HasCSharpModel && !session.CppCreatedFromIndex,
                "C++ 独立加载状态错误");
            report["cpp_info"] = Info(session.GetCppInfo());
            report["cpp_index"] = cppIndex;
            if (mode == "cpp")
            {
                session.ReleaseCpp(); session.ReleaseCpp();
                CheckExpired(cppIndex);
                session.LoadCpp(model, device);
                report["reloaded_cpp_info"] = Info(session.GetCppInfo());
                int reloadedIndex = session.CppModelIndex;
                session.Dispose();
                CheckExpired(reloadedIndex);
            }
            else
            {
                session.LoadCSharp(model, device);
                int csIndex = session.CSharpModelIndex;
                // SDK 对相同内容和设备复用编号，每次文件加载增加持有计数。
                Check(csIndex >= 0 && !session.CppCreatedFromIndex, "分别加载的状态错误");
                report["csharp_index"] = csIndex;
                report["csharp_info"] = Info(session.GetCSharpInfo());
                if (order == "csharp-first")
                {
                    session.ReleaseCSharp();
                    report["retained_info"] = Info(session.GetCppInfo());
                    using (var retained = new CppModel(cppIndex)) Info(retained.GetModelInfo());
                    session.ReleaseCpp();
                }
                else
                {
                    session.ReleaseCpp();
                    report["retained_info"] = Info(session.GetCSharpInfo());
                    using (var retained = new CppModel(csIndex)) Info(retained.GetModelInfo());
                    session.ReleaseCSharp();
                }
                CheckExpired(csIndex);
                CheckExpired(cppIndex);
            }
            Check(!session.HasCSharpModel && !session.HasCppModel, "独立模型未释放");
            report["native_modules"] = NativeModules();
            report["final_index_expired"] = true;
        }

        private static void RunCppShared(ModelSession session, string model, int device, string order, JObject report)
        {
            session.LoadCpp(model, device);
            int index = session.CppModelIndex;
            Check(!session.HasCSharpModel, "反向共享前不应存在 C# 模型");
            report["cpp_info"] = Info(session.GetCppInfo());
            session.ConvertToCSharp();
            Check(session.CSharpModelIndex == index && session.CSharpCreatedFromIndex, "反向共享编号或来源错误");
            report["model_index"] = index;
            report["csharp_info"] = Info(session.GetCSharpInfo());
            report["native_modules"] = NativeModules();
            bool rejected = false;
            try { session.ConvertToCSharp(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && session.CSharpModelIndex == index, "重复反向共享未拒绝");
            if (order == "cpp-first")
            {
                session.ReleaseCpp(); session.ReleaseCpp();
                Check(session.HasCSharpModel && !session.HasCppModel, "C++ 释放后 C# 状态错误");
                report["retained_info"] = Info(session.GetCSharpInfo());
                using (var fresh = dlcv_infer_csharp.ModelFactory.CreateFromIndex(index))
                    Info(fresh.GetModelInfo().ToString());
                session.ConvertToCpp();
                session.ReleaseCSharp();
                Info(session.GetCppInfo());
                session.ReleaseCpp();
            }
            else
            {
                session.ReleaseCSharp(); session.ReleaseCSharp();
                Check(!session.HasCSharpModel && session.HasCppModel && !session.CSharpCreatedFromIndex,
                    "C# 释放后 C++ 状态错误");
                report["retained_info"] = Info(session.GetCppInfo());
                session.ConvertToCSharp();
                session.ReleaseCpp();
                Info(session.GetCSharpInfo());
                session.ReleaseCSharp();
            }
            Check(!session.HasCSharpModel && !session.HasCppModel, "反向共享未释放");
            CheckExpired(index);
            report["final_index_expired"] = true;
            // 同一入口同时验证窗口关闭所使用的会话释放路径。
            session.LoadCpp(model, device);
            session.ConvertToCSharp();
            int closingIndex = session.CSharpModelIndex;
            session.Dispose();
            Check(!session.HasCSharpModel && !session.HasCppModel, "反向共享会话关闭仍持有模型");
            CheckExpired(closingIndex);
            report["dispose_index_expired"] = true;
        }

        public static int Run(string[] args)
        {
            string output = null;
            var report = new JObject { ["status"] = "failed", ["scope"] = "model-command-line" };
            try
            {
                if (args.Length % 2 != 1) throw new ArgumentException("命令行参数必须成对提供");
                string model = null, order = "csharp-first", mode = "shared";
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
                        case "--load-mode": mode = args[i + 1]; break;
                        default: throw new ArgumentException("未知参数：" + args[i]);
                    }
                }
                if (model == null || output == null) throw new ArgumentException("必须指定 --model 和 --output");
                if (order != "csharp-first" && order != "cpp-first") throw new ArgumentException("释放顺序须为 csharp-first 或 cpp-first");
                if (mode != "shared" && mode != "cpp-shared" && mode != "cpp" && mode != "independent")
                    throw new ArgumentException("加载方式须为 shared、cpp-shared、cpp 或 independent");
                report["load_mode"] = mode;
                report["release_order"] = order;
                using (var session = new ModelSession())
                {
                    if (mode == "shared")
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
                    else if (mode == "cpp-shared") RunCppShared(session, model, device, order, report);
                    else RunDirectLoad(session, model, device, mode, order, report);
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
