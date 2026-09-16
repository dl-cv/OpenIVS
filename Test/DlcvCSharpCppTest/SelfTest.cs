using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DlcvCSharpCppBridge;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpCppTest
{
    internal static class SelfTest
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Reject<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("操作未拒绝无效状态：" + typeof(T).Name);
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control nested in Descendants(child)) yield return nested;
            }
        }

        private static void CheckButtons(MainForm form)
        {
            form.RefreshModelState();
            bool cs = form.Session.HasCSharpModel, cpp = form.Session.HasCppModel;
            var expected = new Dictionary<string, bool>
            {
                ["loadCSharpButton"] = !cs && !cpp,
                ["browseModelButton"] = !cs && !cpp,
                ["convertToCppButton"] = cs && !cpp,
                ["getCSharpInfoButton"] = cs,
                ["getCppInfoButton"] = cpp,
                ["releaseCSharpButton"] = cs,
                ["releaseCppButton"] = cpp
            };
            var buttons = Descendants(form).OfType<Button>().ToArray();
            Require(buttons.Length == expected.Count, "六个模型操作按钮与浏览按钮数量不正确");
            foreach (var pair in expected)
                Require(buttons.Single(button => button.Name == pair.Key).Enabled == pair.Value,
                    "按钮状态错误：" + pair.Key);
        }

        private static JObject Info(string text)
        {
            var result = JObject.Parse(text);
            Require(result["code"]?.Type == JTokenType.Integer && result["code"].Value<int>() == 0,
                "模型信息未返回成功状态");
            Require(result["model_info"] is JObject, "模型信息缺少 model_info");
            return result;
        }

        private static void EqualInfo(string expected, string actual)
        {
            Require(JToken.DeepEquals(Info(expected), Info(actual)), "两次模型信息不一致");
        }

        private static void NormalizeOptionalFields(JObject info)
        {
            info.Remove("model_index");
            if ((string)info.SelectToken("model_info.task_type") == "OCR")
            {
                var modelInfo = (JObject)info["model_info"];
                // C# API 的 OCR 信息接口按既有实现过滤这三个字段。
                foreach (string key in new[] { "character", "dict", "classes" }) modelInfo.Remove(key);
            }
            // 两套接口对以下可选字段分别返回缺失或 null，不改变实际信息展示。
            foreach (string path in new[] { "input_shapes", "model_info.input_shapes", "data_info.image_size" })
            {
                JToken value = info.SelectToken(path);
                if (value != null && value.Type == JTokenType.Null)
                    ((JProperty)value.Parent).Remove();
            }
        }

        internal static void WriteReport(string path, JObject result)
        {
            string fullPath = Path.GetFullPath(path);
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("测试结果须为系统临时目录中的 JSON 文件");
            // 新建结果文件，不覆盖模型、已有报告或其他文件。
            using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true)))
                writer.Write(result.ToString());
        }

        public static int RunDesigner(string[] args)
        {
            if (args.Length != 3 || args[1] != "--output") return 1;
            var result = new JObject { ["status"] = "failed", ["scope"] = "designer-construction" };
            try
            {
                using (var form = new MainForm())
                {
                    Require(Descendants(form).OfType<Button>().Count() == 7, "设计器控件数量错误");
                    Require(!form.Visible, "测试不应显示窗口");
                    using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    {
                        foreach (System.Diagnostics.ProcessModule module in process.Modules)
                        {
                            string name = module.ModuleName.ToLowerInvariant();
                            Require(name != "dlcv_infer.dll" && name != "dlcv_infer_v.dll" &&
                                name != "dlcv_infer_cpp.dll" && name != "dlcvcsharpcppbridge.dll",
                                "窗体构造加载了原生模块：" + name);
                        }
                    }
                }
                result["status"] = "passed";
            }
            catch (Exception ex) { result["error"] = ex.ToString(); }
            try { WriteReport(args[2], result); }
            catch { return 1; }
            return (string)result["status"] == "passed" ? 0 : 1;
        }

        public static int Run(string[] args)
        {
            var result = new JObject { ["status"] = "failed" };
            var checks = new JArray();
            result["checks"] = checks;
            string output = null;
            string model = null;
            int device = 0;
            string screenshot = null;
            try
            {
                if (args[0] != "ui-test" || args.Length % 2 != 1)
                    throw new ArgumentException("参数：ui-test --output <JSON文件> [--model <模型文件>] [--device <编号>]");
                var seen = new HashSet<string>();
                for (int i = 1; i < args.Length; i += 2)
                {
                    if (!seen.Add(args[i])) throw new ArgumentException("参数重复：" + args[i]);
                    switch (args[i])
                    {
                        case "--output": output = Path.GetFullPath(args[i + 1]); break;
                        case "--model": model = Path.GetFullPath(args[i + 1]); break;
                        case "--device": device = int.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
                        case "--screenshot": screenshot = Path.GetFullPath(args[i + 1]); break;
                        default: throw new ArgumentException("未知参数：" + args[i]);
                    }
                }
                if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("必须指定 --output");
                if (device < -1) throw new ArgumentException("设备编号不能小于 -1");
                if (model != null && !File.Exists(model)) throw new FileNotFoundException("模型文件不存在");
                if (model != null && string.Equals(model, output, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("结果文件不能覆盖模型文件");

                ModelPathSelfTest.Run();
                checks.Add("浏览配置、选择即保存、取消不修改、重新打开恢复、再次选择及缺失文件检查通过");
                using (var form = new MainForm())
                {
                    var session = form.Session;
                    var buttons = Descendants(form).OfType<Button>().ToArray();
                    Require(buttons.Length == 7, "六个模型操作按钮与浏览按钮数量不正确");
                    Require(!session.HasCSharpModel && !session.HasCppModel, "初始状态错误");
                    CheckButtons(form);
                    Reject<InvalidOperationException>(() => session.ConvertToCpp());
                    Reject<InvalidOperationException>(() => session.GetCSharpInfo());
                    Reject<InvalidOperationException>(() => session.GetCppInfo());
                    session.ReleaseCSharp(); session.ReleaseCpp();
                    session.ReleaseCSharp(); session.ReleaseCpp();
                    Reject<ArgumentOutOfRangeException>(() => new CppModel(-1));
                    Reject<NotSupportedException>(() => session.LoadCSharp("unsupported.dvsp", device));
                    Reject<NotSupportedException>(() => session.LoadCSharp("unsupported.dvp", device));
                    checks.Add("六按钮、空模型操作、重复释放、负索引检查通过");

                    string invalid = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dvt");
                    try
                    {
                        File.WriteAllText(invalid, "invalid model", new UTF8Encoding(false));
                        Reject<Exception>(() => session.LoadCSharp(invalid, device));
                        Require(!session.HasCSharpModel && !session.HasCppModel, "加载失败后状态错误");
                    }
                    finally { if (File.Exists(invalid)) File.Delete(invalid); }
                    checks.Add("无效模型加载失败后状态保持为空");

                    if (model != null)
                    {
                        for (int order = 0; order < 2; order++)
                        {
                            session.LoadCSharp(model, device);
                            CheckButtons(form);
                            int index = session.CSharpModelIndex;
                            string csharp = session.GetCSharpInfo();
                            try { session.ConvertToCpp(); }
                            catch
                            {
                                Require(session.HasCSharpModel && !session.HasCppModel,
                                    "转换失败后两侧状态错误");
                                EqualInfo(csharp, session.GetCSharpInfo());
                                checks.Add("转换失败后 C# 信息保持可读，C++ 状态为空");
                                throw;
                            }
                            CheckButtons(form);
                            Require(index >= 0 && session.CppModelIndex == index, "共享模型索引不一致");
                            string cpp = session.GetCppInfo();
                            result["csharp_info"] = Info(csharp);
                            result["cpp_info"] = Info(cpp);
                            var csharpObject = Info(csharp);
                            var cppObject = Info(cpp);
                            // C++ 共享恢复补充顶层 model_index，C# 文件加载结果没有该字段。
                            Require(cppObject["model_index"]?.Type == JTokenType.Integer &&
                                cppObject["model_index"].Value<int>() >= 0, "C++ 信息编号无效");
                            string extension = Path.GetExtension(model).ToLowerInvariant();
                            // 流程兼容信息使用子模型编号，实例编号已在转换时比较。
                            if (extension == ".dvt" || extension == ".dvo")
                                Require(cppObject["model_index"].Value<int>() == index, "普通模型信息编号不一致");
                            NormalizeOptionalFields(cppObject);
                            NormalizeOptionalFields(csharpObject);
                            Require(JToken.DeepEquals(csharpObject, cppObject), "两侧模型信息字段不一致");
                            Reject<InvalidOperationException>(() => session.ConvertToCpp());
                            Reject<InvalidOperationException>(() => session.LoadCSharp(model, device));
                            using (var extra = new CppModel(index))
                            {
                                EqualInfo(cpp, extra.GetModelInfo());
                                extra.Dispose(); extra.Dispose();
                                Reject<ObjectDisposedException>(() => extra.GetModelInfo());
                            }
                            if (order == 0)
                            {
                                session.ReleaseCSharp(); session.ReleaseCSharp();
                                Require(!session.HasCSharpModel && session.HasCppModel, "释放 C# 后状态错误");
                                CheckButtons(form);
                                EqualInfo(cpp, session.GetCppInfo());
                                // 新建借用对象重新查询，避免仅由信息缓存判断资源是否仍存在。
                                using (var fresh = new CppModel(index)) EqualInfo(cpp, fresh.GetModelInfo());
                                session.ReleaseCpp(); session.ReleaseCpp();
                            }
                            else
                            {
                                session.ReleaseCpp(); session.ReleaseCpp();
                                Require(session.HasCSharpModel && !session.HasCppModel, "释放 C++ 后状态错误");
                                CheckButtons(form);
                                EqualInfo(csharp, session.GetCSharpInfo());
                                session.ConvertToCpp();
                                CheckButtons(form);
                                EqualInfo(cpp, session.GetCppInfo());
                                session.ReleaseCpp(); session.ReleaseCSharp();
                            }
                            CheckButtons(form);
                            Require(!session.HasCSharpModel && !session.HasCppModel, "释放后状态未清空");
                            Reject<Exception>(() => { using (var stale = new CppModel(index)) { } });
                            checks.Add(order == 0 ? "C# 先释放、C++ 信息获取及最终索引失效通过" : "C++ 先释放、C# 信息获取、再次转换及最终索引失效通过");
                        }
                        session.LoadCSharp(model, device);
                        session.ConvertToCpp();
                        int closingIndex = session.CppModelIndex;
                        form.Dispose();
                        Require(!session.HasCSharpModel && !session.HasCppModel, "窗口释放后仍持有模型");
                        Reject<Exception>(() => { using (var stale = new CppModel(closingIndex)) { } });
                        checks.Add("窗口关闭释放两侧模型通过");
                    }
                }
                result["ui"] = MainForm.RunUiTest(model, device, screenshot);
                checks.Add(model == null ? "空界面状态与默认、最小窗口布局通过" :
                    "实际按钮处理、两侧信息显示、状态变化、最小窗口布局及窗口释放通过");
                result["scope"] = model == null ? "ui-and-invalid-input" : "ui-and-real-model-lifecycle";
                result["status"] = "passed";
            }
            catch (Exception ex)
            {
                result["error"] = ex.ToString();
            }
            try
            {
                if (output != null && !string.Equals(output, model, StringComparison.OrdinalIgnoreCase))
                    WriteReport(output, result);
                else
                    Console.Error.WriteLine(result.ToString());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            return (string)result["status"] == "passed" ? 0 : 1;
        }
    }
}
