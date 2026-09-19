using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace DlcvDemo
{
    /// <summary>
    /// 界面语言管理：以中文原文为键查英文字典，运行时切换并持久化用户选择。
    /// </summary>
    internal static class I18n
    {
        internal const string Chinese = "zh-CN";
        internal const string English = "en-US";

        private static readonly Dictionary<string, string> EnTranslations = new Dictionary<string, string>
        {
            // 界面控件（设计器文本）
            { "加载模型", "Load Model" },
            { "单次推理", "Infer" },
            { "推理 JSON", "Infer JSON" },
            { "多线程测试", "Thread Test" },
            { "一致性测试", "Consistency" },
            { "选择显卡", "Select GPU" },
            { "线程数", "Threads" },
            { "计算均值：默认", "Calc Mean: Default" },
            { "RPC 模式", "RPC Mode" },
            { "打开图片推理", "Open Image to Infer" },
            { "保存图像", "Save Image" },
            { "释放模型", "Free Model" },
            { "释放所有模型", "Free All" },
            { "检查环境", "Check Env" },
            { "检查加密狗", "Check Dongle" },
            { "文档", "Docs" },
            { "获取模型信息", "Model Info" },
            { "运行结果", "Result" },
            { "C# 测试程序", "C# Test Program" },

            // 可访问性名称与右键菜单项
            { "运行结果输出区", "Result output area" },
            { "图像显示区", "Image display area" },
            { "撤销", "Undo" },
            { "剪切", "Cut" },
            { "复制", "Copy" },
            { "粘贴", "Paste" },
            { "删除", "Delete" },
            { "全选", "Select All" },
            { "自动换行", "Word Wrap" },

            // 主窗口运行期文本
            { "设备信息读取失败：", "Failed to read device info: " },
            { "GPU信息获取失败：", "Failed to get GPU info: " },
            { "Sentinel加密狗ID", "Sentinel Dongle ID" },
            { "Sentinel加密狗特性", "Sentinel Dongle Features" },
            { "Virbox加密狗ID", "Virbox Dongle ID" },
            { "Virbox加密狗特性", "Virbox Dongle Features" },
            { "未检测到加密狗", "No Dongle Detected" },
            { "（{0}个）：", "({0}): " },
            { "模型: ", "Model: " },
            { "图片: ", "Image: " },
            { "AI模型 (*.dvt;*.dvp;*.dvo;*.dvst;*.dvso)", "AI Model (*.dvt;*.dvp;*.dvo;*.dvst;*.dvso)" },
            { "所有文件 (*.*)", "All Files (*.*)" },
            { "选择模型", "Select Model" },
            { "图片文件 (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif)", "Image Files (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif)" },
            { "选择图片文件", "Select Image" },
            { "不支持 .dvsp 模型推理", ".dvsp model inference is not supported" },
            { "请先加载模型文件！", "Please load a model file first!" },
            { "请先选择图片文件！", "Please select an image file first!" },
            { "图像解码失败！", "Image decoding failed!" },
            { "加载模型失败", "Failed to Load Model" },
            { "推理JSON失败", "JSON Inference Failed" },
            { "推理失败", "Inference Failed" },
            { "保存图像失败", "Failed to Save Image" },
            { "错误", "Error" },
            { "结果不一致", "Inconsistent Result" },
            { "检测到推理结果不一致，测试已停止！", "Inconsistent inference results detected. Test stopped!" },
            { "发现推理结果不一致！测试已停止。", "Inconsistent inference results detected! Test stopped." },
            { "=== 基准结果 ===", "=== Baseline Result ===" },
            { "\n=== 当前结果 ===", "\n=== Current Result ===" },
            { "基准结果:", "Baseline Result:" },
            { "推理错误: ", "Inference Error: " },
            { "压力测试", "Stress Test" },
            { "{0}过程中发生错误: {1}", "Error during {0}: {1}" },
            { "启动{0}失败: {1}", "Failed to start {0}: {1}" },
            { "停止", "Stop" },
            { "模型已释放", "Model Released" },
            { "所有模型已释放", "All Models Released" },
            { "正在检查环境，请稍候...", "Checking environment, please wait..." },
            { "环境检查失败：", "Environment check failed: " },
            { "图像已保存：", "Image saved: " },
            { "当前没有可保存的图像！", "No image available to save!" },
            { "JPEG 图像 (*.jpg)", "JPEG Image (*.jpg)" },
            { "保存可视化图像", "Save Visualization Image" },
            { "推理结果: {0}个", "Inference Results: {0}" },
            { "推理结果: ", "Inference Results: " },
            { "流程输出结果：OK", "Flow Output: OK" },
            { "流程输出结果：NG", "Flow Output: NG" },
            { "流程输出原因：", "Flow Output Reason: " },
            { "未检测到目标。", "No objects detected." },
            { "推理时间: {0:F2}ms", "Inference Time: {0:F2}ms" },
            { "自动 UI 测试失败", "Automated UI Test Failed" },
            { "模型加载失败: ", "Model loading failed: " },
            { "文件对话框选择的模型与预期不一致: ", "Model file chosen in dialog does not match expected: " },
            { "文件对话框选择的图片与预期不一致: ", "Image file chosen in dialog does not match expected: " },
            { "界面未生成推理结果: ", "No inference result in UI: " },
            { "计算均值：{0}", "Calc Mean: {0}" },
            { "默认", "Default" },
            { "是", "Yes" },
            { "否", "No" },

            // 环境信息收集
            { "NVIDIA 驱动", "NVIDIA Driver" },
            { "类型 | 版本 | 路径", "Type | Version | Path" },
            { "版本未知", "Unknown" },
            { "未加载", "Not Loaded" },

            // 压力测试统计（经 PressureTestRunner.LocalizeText 查同一字典）
            { "压力测试统计:", "Stress Test Statistics:" },
            { "线程数: {0}", "Threads: {0}" },
            { "批量大小: {0}", "Batch Size: {0}" },
            { "目标速率: {0} 请求/秒", "Target Rate: {0} req/s" },
            { "运行时间: {0:F2} 秒", "Elapsed: {0:F2} s" },
            { "完成请求: {0}", "Completed Requests: {0}" },
            { "平均延迟: {0:F2}ms", "Average Latency: {0:F2}ms" },
            { "平均延迟(SDK): {0:F2}ms", "Average Latency (SDK): {0:F2}ms" },
            { "实时速率: {0:F2} 请求/秒", "Recent Rate: {0:F2} req/s" },
            { "模块平均耗时:", "Average Module Time:" },
        };

        private sealed class TextSnapshot
        {
            internal TextSnapshot(string text) { Text = text; }
            internal readonly string Text;
        }

        // 首次应用语言时记录控件/菜单项的原始中文文本，保证中英往返切换正确。
        private static readonly ConditionalWeakTable<Control, TextSnapshot> ControlTexts = new ConditionalWeakTable<Control, TextSnapshot>();
        private static readonly ConditionalWeakTable<Control, TextSnapshot> AccessibleNames = new ConditionalWeakTable<Control, TextSnapshot>();
        private static readonly ConditionalWeakTable<ToolStripItem, TextSnapshot> MenuTexts = new ConditionalWeakTable<ToolStripItem, TextSnapshot>();

        internal static string CurrentLanguage { get; private set; } = Chinese;

        // 启动时读取已保存语言；未保存时系统界面语言以 en 开头为英文，否则中文。
        internal static void Initialize()
        {
            string saved = Properties.Settings.Default.UiLanguage;
            if (string.IsNullOrWhiteSpace(saved))
            {
                bool english = CultureInfo.CurrentUICulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase);
                SetLanguage(english ? English : Chinese, persist: false);
                return;
            }
            SetLanguage(saved, persist: false);
        }

        internal static void SetLanguage(string language, bool persist)
        {
            CurrentLanguage = string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ? English : Chinese;
            if (persist)
            {
                Properties.Settings.Default.UiLanguage = CurrentLanguage;
                Properties.Settings.Default.Save();
            }
        }

        internal static string T(string zhText)
        {
            if (CurrentLanguage != English || string.IsNullOrEmpty(zhText))
            {
                return zhText;
            }
            string enText;
            return EnTranslations.TryGetValue(zhText, out enText) ? enText : zhText;
        }

        // 递归应用语言到控件树与右键菜单项。窗体标题由窗体代码单独维护；
        // 下拉框、文本框、数值框的 Text 是运行期内容，不按设计器原文覆盖。
        internal static void ApplyTo(Control root)
        {
            if (root == null)
            {
                return;
            }
            ApplyToControl(root);
        }

        private static bool ShouldTranslateText(Control control)
        {
            return !(control is Form
                || control is ComboBox
                || control is TextBoxBase
                || control is NumericUpDown
                || Equals(control.Tag, "no-i18n"));
        }

        private static void ApplyToControl(Control control)
        {
            if (ShouldTranslateText(control))
            {
                TextSnapshot snapshot = ControlTexts.GetValue(control, c => new TextSnapshot(c.Text));
                control.Text = T(snapshot.Text);
            }
            if (!string.IsNullOrEmpty(control.AccessibleName))
            {
                TextSnapshot accessible = AccessibleNames.GetValue(control, c => new TextSnapshot(c.AccessibleName));
                control.AccessibleName = T(accessible.Text);
            }
            ContextMenuStrip menu = control.ContextMenuStrip;
            if (menu != null)
            {
                ApplyToMenuItems(menu.Items);
            }
            foreach (Control child in control.Controls)
            {
                ApplyToControl(child);
            }
        }

        private static void ApplyToMenuItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                TextSnapshot snapshot = MenuTexts.GetValue(item, i => new TextSnapshot(i.Text));
                item.Text = T(snapshot.Text);
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.DropDownItems.Count > 0)
                {
                    ApplyToMenuItems(menuItem.DropDownItems);
                }
            }
        }
    }
}
