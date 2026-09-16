using System;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpCppTest
{
    internal static class ModelPathSelfTest
    {
        // 测试仅改变设置的存储位置，窗体继续使用相同的读取、选择和 Save 操作。
        private sealed class TemporarySettingsProvider : SettingsProvider
        {
            private readonly string file;
            public override string ApplicationName { get; set; } = "DlcvCSharpCppTest";

            public TemporarySettingsProvider(string file)
            {
                this.file = file;
                Initialize("TemporaryModelPath", new NameValueCollection());
            }

            public override SettingsPropertyValueCollection GetPropertyValues(
                SettingsContext context, SettingsPropertyCollection properties)
            {
                var result = new SettingsPropertyValueCollection();
                var saved = File.Exists(file) ? JObject.Parse(File.ReadAllText(file, Encoding.UTF8)) : new JObject();
                foreach (SettingsProperty property in properties)
                    result.Add(new SettingsPropertyValue(property)
                    {
                        PropertyValue = (string)saved[property.Name] ?? (string)property.DefaultValue,
                        IsDirty = false
                    });
                return result;
            }

            public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection values)
            {
                var saved = new JObject();
                foreach (SettingsPropertyValue value in values)
                    saved[value.Name] = (string)value.PropertyValue;
                File.WriteAllText(file, saved.ToString(), new UTF8Encoding(false, true));
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static TextBox PathBox(MainForm form)
        {
            return (TextBox)form.Controls.Find("pathTextBox", true).Single();
        }

        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "dlcv_model_path_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string config = Path.Combine(root, "settings.json");
                string first = Path.Combine(root, "首次选择 模型.dvt");
                string second = Path.Combine(root, "再次选择 模型.dvso");
                File.WriteAllText(first, "selection-only");
                File.WriteAllText(second, "selection-only");
                var settings = new Properties.Settings(new TemporarySettingsProvider(config));
                using (var form = new MainForm(settings))
                {
                    form.RestoreModelPath();
                    Check(PathBox(form).ReadOnly && PathBox(form).Text == "", "首次运行路径应为空且无需手填");
                    using (var dialog = form.CreateModelDialog())
                        Check(dialog.RestoreDirectory && dialog.CheckFileExists && !dialog.Multiselect,
                            "模型选择窗口配置错误");
                    Check(!form.ApplyModelSelection(DialogResult.Cancel, first), "取消选择返回错误");
                    Check(!File.Exists(config), "取消选择不应保存记录");
                    Check(form.ApplyModelSelection(DialogResult.OK, first), "首次选择失败");
                    Check(PathBox(form).Text == first && settings.LastModelPath == first, "选择后路径未更新");
                    Check(File.Exists(config), "选择后未立即保存");
                    string beforeCancel = File.ReadAllText(config, Encoding.UTF8);
                    form.ApplyModelSelection(DialogResult.Cancel, second);
                    Check(PathBox(form).Text == first && File.ReadAllText(config, Encoding.UTF8) == beforeCancel,
                        "取消选择改变了原路径或历史记录");
                }
                // 使用新的设置对象和窗体重新读取文件，验证跨窗体恢复，而非内存缓存。
                using (var form = new MainForm(new Properties.Settings(new TemporarySettingsProvider(config))))
                {
                    form.RestoreModelPath();
                    Check(PathBox(form).Text == first, "重新打开未恢复上次路径");
                    using (var dialog = form.CreateModelDialog())
                        Check(dialog.InitialDirectory == root && dialog.FileName == Path.GetFileName(first),
                            "浏览未定位到上次目录和文件名");
                    form.ApplyModelSelection(DialogResult.OK, second);
                }
                using (var form = new MainForm(new Properties.Settings(new TemporarySettingsProvider(config))))
                {
                    form.RestoreModelPath();
                    Check(PathBox(form).Text == second, "再次选择后历史记录未更新");
                    File.Delete(second);
                    using (var dialog = form.CreateModelDialog())
                        Check(dialog.InitialDirectory == root, "上次文件不存在时无法定位已有目录");
                    bool rejected = false;
                    try { form.ApplyModelSelection(DialogResult.OK, second); }
                    catch (FileNotFoundException) { rejected = true; }
                    Check(rejected, "不存在的模型文件未被拒绝");
                }
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
