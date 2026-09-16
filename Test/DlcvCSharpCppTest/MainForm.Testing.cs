using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpCppTest
{
    public partial class MainForm
    {
        private static void UiCheck(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private void CheckLayout()
        {
            PerformLayout();
            inputLayoutPanel.PerformLayout();
            buttonsFlowLayoutPanel.PerformLayout();
            modelsLayoutPanel.PerformLayout();
            Button[] buttons = { browseModelButton, loadCSharpButton, convertToCppButton,
                getCSharpInfoButton, getCppInfoButton, releaseCSharpButton, releaseCppButton };
            foreach (Button button in buttons)
            {
                UiCheck(button.Width > 0 && button.Height > 0 && button.Left >= 0 && button.Top >= 0 &&
                    button.Right <= button.Parent.ClientSize.Width && button.Bottom <= button.Parent.ClientSize.Height,
                    "按钮超出容器：" + button.Name);
                foreach (Button other in buttons.Where(item => item != button && item.Parent == button.Parent))
                    UiCheck(!button.Bounds.IntersectsWith(other.Bounds), "按钮位置重叠：" + button.Name);
            }
            UiCheck(pathTextBox.Width > 80 && pathTextBox.ReadOnly, "模型路径显示区域无效");
            UiCheck(csharpInfoTextBox.Width > 100 && cppInfoTextBox.Width > 100 &&
                csharpInfoTextBox.Height > 50 && cppInfoTextBox.Height > 50, "信息显示区域过小");
        }

        private static void CreateRenderHandles(Control control)
        {
            // 未显示的父窗体不会自动创建子控件句柄，绘制前显式准备控件。
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) CreateRenderHandles(child);
            control.PerformLayout();
        }

        private void RenderUi(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("界面图片须为系统临时目录中新建的 PNG");
            CreateRenderHandles(this);
            using (var image = new Bitmap(Width, Height))
            using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                DrawToBitmap(image, new Rectangle(Point.Empty, Size));
                CheckRenderedText(image, browseModelButton);
                if (csharpInfoTextBox.TextLength > 0) CheckRenderedText(image, csharpInfoTextBox);
                if (cppInfoTextBox.TextLength > 0) CheckRenderedText(image, cppInfoTextBox);
                image.Save(stream, ImageFormat.Png);
            }
        }

        private void CheckRenderedText(Bitmap image, Control control)
        {
            Point origin = PointToClient(control.PointToScreen(Point.Empty));
            Point clientOffset = PointToScreen(Point.Empty);
            Point windowOrigin = new Point(Left, Top);
            origin.Offset(clientOffset.X - windowOrigin.X, clientOffset.Y - windowOrigin.Y);
            int darkPixels = 0;
            for (int y = Math.Max(0, origin.Y + 4); y < Math.Min(image.Height, origin.Y + control.Height - 4); y++)
                for (int x = Math.Max(0, origin.X + 4); x < Math.Min(image.Width, origin.X + control.Width - 4); x++)
                {
                    Color color = image.GetPixel(x, y);
                    if (color.R < 200 && color.G < 200 && color.B < 200) darkPixels++;
                }
            UiCheck(darkPixels > 25, "界面图片缺少控件文字：" + control.Name);
        }

        internal static JObject RunUiTest(string model, int device, string screenshot)
        {
            string root = Path.Combine(Path.GetTempPath(), "dlcv_mixed_ui_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var settings = new Properties.Settings(new ModelPathSelfTest.TemporarySettingsProvider(Path.Combine(root, "settings.json")));
                using (var form = new MainForm(settings))
                {
                    // 调用窗体自身的方法，不显示窗口、不发送点击消息、不模拟键鼠。
                    form.OnLoad(EventArgs.Empty);
                    form.CreateControl();
                    form.CheckLayout();
                    Size fullSize = form.Size;
                    form.Size = form.MinimumSize;
                    form.CheckLayout();
                    form.Size = fullSize;
                    form.CheckLayout();
                    UiCheck(form.pathTextBox.Text.Length == 0 && form.browseModelButton.Enabled &&
                        !form.convertToCppButton.Enabled && !form.getCSharpInfoButton.Enabled &&
                        !form.getCppInfoButton.Enabled, "初始控件状态错误");
                    var result = new JObject { ["layout"] = "passed", ["visible_window"] = false,
                        ["minimum_width"] = form.MinimumSize.Width, ["minimum_height"] = form.MinimumSize.Height };
                    if (model != null)
                    {
                        UiCheck(form.ApplyModelSelection(DialogResult.OK, model), "模型选择失败");
                        form.deviceNumericUpDown.Value = device;
                        form.LoadCSharpButton_Click(form.loadCSharpButton, EventArgs.Empty);
                        UiCheck(form.Session.HasCSharpModel && !form.browseModelButton.Enabled &&
                            form.convertToCppButton.Enabled, "加载按钮处理失败：" + form.statusLabel.Text);
                        form.GetCSharpInfoButton_Click(form.getCSharpInfoButton, EventArgs.Empty);
                        UiCheck(JToken.DeepEquals(JObject.Parse(form.csharpInfoTextBox.Text),
                            JObject.Parse(form.Session.GetCSharpInfo())), "C# 信息显示错误");
                        form.ConvertToCppButton_Click(form.convertToCppButton, EventArgs.Empty);
                        UiCheck(form.Session.HasCppModel && form.getCppInfoButton.Enabled,
                            "转换按钮处理失败：" + form.statusLabel.Text);
                        form.GetCppInfoButton_Click(form.getCppInfoButton, EventArgs.Empty);
                        UiCheck(JToken.DeepEquals(JObject.Parse(form.cppInfoTextBox.Text),
                            JObject.Parse(form.Session.GetCppInfo())), "C++ 信息显示错误");
                        UiCheck(form.csharpStateLabel.Text.Contains(form.Session.CSharpModelIndex.ToString()) &&
                            form.cppStateLabel.Text.Contains(form.Session.CppModelIndex.ToString()), "状态编号显示错误");
                        result["model_index"] = form.Session.CSharpModelIndex;
                        result["csharp_text_length"] = form.csharpInfoTextBox.TextLength;
                        result["cpp_text_length"] = form.cppInfoTextBox.TextLength;
                        result["native_modules"] = CommandLineTest.NativeModules();
                    }
                    if (screenshot != null)
                    {
                        form.RenderUi(screenshot);
                        result["screenshot"] = screenshot;
                    }
                    if (model != null)
                    {
                        form.ReleaseCSharpButton_Click(form.releaseCSharpButton, EventArgs.Empty);
                        UiCheck(!form.Session.HasCSharpModel && form.Session.HasCppModel &&
                            form.csharpInfoTextBox.TextLength == 0 && !form.getCSharpInfoButton.Enabled,
                            "释放 C# 后界面状态错误");
                        form.GetCppInfoButton_Click(form.getCppInfoButton, EventArgs.Empty);
                        UiCheck(form.cppInfoTextBox.TextLength > 0 && !form.statusLabel.Text.StartsWith("操作失败"),
                            "C# 释放后 C++ 信息按钮不可用");
                        form.ReleaseCppButton_Click(form.releaseCppButton, EventArgs.Empty);
                        UiCheck(!form.Session.HasCppModel && form.cppInfoTextBox.TextLength == 0 &&
                            form.browseModelButton.Enabled && form.loadCSharpButton.Enabled,
                            "释放 C++ 后界面状态错误");
                        form.LoadCSharpButton_Click(form.loadCSharpButton, EventArgs.Empty);
                        form.ConvertToCppButton_Click(form.convertToCppButton, EventArgs.Empty);
                        UiCheck(form.Session.HasCSharpModel && form.Session.HasCppModel, "再次加载失败");
                        ModelSession session = form.Session;
                        form.Dispose();
                        UiCheck(!session.HasCSharpModel && !session.HasCppModel, "窗体释放仍持有模型");
                    }
                    return result;
                }
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
