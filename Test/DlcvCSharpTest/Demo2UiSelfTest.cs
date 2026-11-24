using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using dlcv_infer_csharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpTest
{
    internal static class Demo2UiSelfTest
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static int Run(string[] args)
        {
            try
            {
                if (args.Length != 2)
                    throw new ArgumentException("用法: demo2-ui-selftest <配置JSON>");
                var config = JObject.Parse(File.ReadAllText(Path.GetFullPath(args[1]), Encoding.UTF8));
                JToken workspaceLimit = config["workspaceLimit"];
                int limit;
                if (workspaceLimit != null && (workspaceLimit.Type != JTokenType.Integer ||
                    !int.TryParse(workspaceLimit.ToString(Formatting.None), out limit) || limit < 1 || limit > 32))
                    throw new ArgumentException("workspaceLimit 需要1到32的整数");
                foreach (string key in new[] { "demo2Exe", "extractModel", "componentModel", "icModel", "noCadModel", "image", "output" })
                {
                    string path = config.Value<string>(key);
                    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                        throw new ArgumentException(key + " 需要绝对路径");
                    config[key] = Path.GetFullPath(path);
                    if (key != "output" && !File.Exists(path))
                        throw new FileNotFoundException("输入文件不存在", path);
                }
                string output = config.Value<string>("output");
                if (File.Exists(output) || File.Exists(output + ".log"))
                    throw new IOException("输出文件已存在: " + output);
                if (new[] { "demo2Exe", "extractModel", "componentModel", "icModel", "noCadModel", "image" }
                    .Select(key => config.Value<string>(key)).Concat(new[] { Path.GetFullPath(args[1]) })
                    .Any(input => string.Equals(input, output, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(input, output + ".log", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("输出路径与输入重复");
                string directory = Path.GetDirectoryName(output);
                for (var parent = new DirectoryInfo(directory); parent != null; parent = parent.Parent)
                    if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("输出目录经过重解析点: " + parent.FullName);
                Directory.CreateDirectory(directory);
                int exitCode = 1;
                var thread = new Thread(() => exitCode = RunWindow(config));
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                return exitCode;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static int RunWindow(JObject config)
        {
            string output = config.Value<string>("output");
            var report = new JObject
            {
                ["status"] = "started",
                ["pid"] = Process.GetCurrentProcess().Id,
                ["inputs"] = config.DeepClone()
            };
            int exitCode = 1;
            using (var timeout = new System.Threading.Timer(state =>
            {
                File.WriteAllText(output, new JObject { ["status"] = "failed", ["error"] = "界面验证超过600秒" }.ToString(), new UTF8Encoding(false));
                Environment.Exit(1);
            }, null, 600000, Timeout.Infinite))
            {
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Assembly assembly = Assembly.LoadFrom(config.Value<string>("demo2Exe"));
                    string actualAssembly = Path.GetFullPath(assembly.Location);
                    report["actual_demo_assembly"] = actualAssembly;
                    if (!string.Equals(actualAssembly, config.Value<string>("demo2Exe"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("实际加载的 Demo2 程序集与输入路径不同: " + actualAssembly);
                    IsolateSettings(assembly);
                    Type formType = assembly.GetType("DlcvDemo2.Form1", true);
                    using (var window = (Form)Activator.CreateInstance(formType))
                    {
                        window.ClientSize = new System.Drawing.Size(1396, 1433);
                        window.StartPosition = FormStartPosition.CenterScreen;
                        window.Shown += async (sender, args) =>
                        {
                            try
                            {
                                string[] keys = { "extractModel", "componentModel", "icModel", "noCadModel", "image" };
                                string[] fields = { "txtExtractModelPath", "txtComponentModelPath", "txtIcModelPath", "txtNoCadModelPath", "txtImagePath" };
                                for (int index = 0; index < keys.Length; index++)
                                    ((TextBox)ReadField(window, fields[index])).Text = config.Value<string>(keys[index]);
                                formType.GetField("imagePath", InstanceFields).SetValue(window, config.Value<string>("image"));
                                foreach (string field in new[] { "numWindowWidth", "numWindowHeight", "numOverlapX", "numOverlapY" })
                                    ((NumericUpDown)ReadField(window, field)).Value = field.StartsWith("numWindow") ? 2560 : 1024;
                                object imageViewer = ReadField(window, "imagePanel1");
                                imageViewer.GetType().GetProperty("ShowLabelText").SetValue(imageViewer, false);

                                if (config["workspaceLimit"] != null)
                                {
                                    int workspaceLimit = config.Value<int>("workspaceLimit");
                                    report["workspace_limit"] = workspaceLimit;
                                    int status = await Task.Run(() => Utils.SetTrtWorkspaceLimit(workspaceLimit));
                                    if (status != 0)
                                        throw new InvalidOperationException("工作空间上限设置失败，返回值: " + status);
                                }
                                ((Button)ReadField(window, "btnLoadAllModels")).PerformClick();
                                await WaitUntilIdle(window, "isModelLoading");
                                using (var process = Process.GetCurrentProcess())
                                    report["native_modules"] = new JArray(process.Modules.Cast<ProcessModule>()
                                        .Where(module => string.Equals(module.ModuleName, "dlcv_infer.dll", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(module.ModuleName, "dlcv_infer_v.dll", StringComparison.OrdinalIgnoreCase))
                                        .Select(module => module.FileName));
                                string loadLog = ((RichTextBox)ReadField(window, "richTextBox1")).Text;
                                report["load_log"] = loadLog;
                                foreach (string field in new[] { "extractModel", "componentDetectModel", "icDetectModel", "noCadDetectModel" })
                                    if (ReadField(window, field) == null)
                                        throw new InvalidOperationException("模型加载失败: " + loadLog);

                                ((Button)ReadField(window, "btnInfer")).PerformClick();
                                await WaitUntilIdle(window, "isInferenceRunning");
                                string text = ((RichTextBox)ReadField(window, "richTextBox1")).Text;
                                File.WriteAllText(output + ".log", loadLog + Environment.NewLine + text, new UTF8Encoding(false));
                                if (((ProgressBar)ReadField(window, "progressBarInference")).Value != 100 || text.Contains("推理失败"))
                                    throw new InvalidOperationException("实际按钮推理失败: " + text);
                                foreach (string name in new[] { "滑窗数量", "元件提取模型合并后目标数", "元件检测模型结果数", "IC检测模型结果数", "最终结果数" })
                                {
                                    Match match = Regex.Match(text, Regex.Escape(name) + @":\s*(\d+)");
                                    if (!match.Success || int.Parse(match.Groups[1].Value) <= 0)
                                        throw new InvalidOperationException("推理结果缺少有效数量: " + name);
                                    report[name] = int.Parse(match.Groups[1].Value);
                                }
                                object viewer = ReadField(window, "imagePanel1");
                                object displayed = viewer.GetType().GetField("currentResults").GetValue(viewer);
                                if (displayed == null)
                                    throw new InvalidOperationException("正式图像控件没有结果");
                                var result = (Utils.CSharpResult)displayed;
                                report["objects"] = new JArray(result.SampleResults.SelectMany(sample => sample.Results).Select(item => new JObject
                                {
                                    ["category_name"] = item.CategoryName, ["score"] = item.Score,
                                    ["bbox"] = item.Bbox == null ? null : new JArray(item.Bbox),
                                    ["angle"] = item.Angle, ["extra_info"] = item.ExtraInfo?.DeepClone()
                                }));
                                if (report["objects"].Count() != report.Value<int>("最终结果数"))
                                    throw new InvalidOperationException("图像控件结果数与日志不同");
                                report["result_log"] = text;
                                report["status"] = "passed";
                                window.Text = "C#测试程序2 - 已完成";
                                window.Refresh();
                                report["window_title"] = window.Text;
                                File.WriteAllText(output, report.ToString(Formatting.Indented), new UTF8Encoding(false));
                                timeout.Change(Timeout.Infinite, Timeout.Infinite);
                                exitCode = 0;
                            }
                            catch (Exception error)
                            {
                                report["status"] = "failed";
                                report["error"] = error.ToString();
                                File.WriteAllText(output, report.ToString(Formatting.Indented), new UTF8Encoding(false));
                                Environment.Exit(1);
                            }
                        };
                        Application.Run(window);
                    }
                }
                catch (Exception error)
                {
                    report["status"] = "failed";
                    report["error"] = error.ToString();
                    File.WriteAllText(output, report.ToString(Formatting.Indented), new UTF8Encoding(false));
                }
            }
            return exitCode;
        }

        private static async Task WaitUntilIdle(Form window, string field)
        {
            await Task.Delay(100);
            while ((bool)ReadField(window, field))
                await Task.Delay(100);
        }

        private static object ReadField(object instance, string name)
        {
            return instance.GetType().GetField(name, InstanceFields).GetValue(instance);
        }

        private static void IsolateSettings(Assembly assembly)
        {
            var settings = (ApplicationSettingsBase)assembly.GetType("DlcvDemo2.Properties.Settings", true)
                .GetProperty("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var provider = new MemorySettingsProvider();
            provider.Initialize("Demo2TestSettings", null);
            settings.Providers.Clear();
            settings.Providers.Add(provider);
            foreach (SettingsProperty property in settings.Properties)
                property.Provider = provider;
            settings.Reload();
        }

        private sealed class MemorySettingsProvider : SettingsProvider
        {
            public override string ApplicationName { get; set; } = "Demo2UiSelfTest";

            public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties)
            {
                var values = new SettingsPropertyValueCollection();
                foreach (SettingsProperty property in properties)
                    values.Add(new SettingsPropertyValue(property) { SerializedValue = property.DefaultValue, IsDirty = false });
                return values;
            }

            public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection values)
            {
            }
        }
    }
}
