using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpCppTest
{
    internal static class AppearanceSelfTest
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr instance, IntPtr name, uint type, int width, int height, uint flags);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icon);

        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static bool SameIcon(Icon first, Icon second)
        {
            using (var a = new Icon(first, 32, 32))
            using (var b = new Icon(second, 32, 32))
            using (Bitmap x = a.ToBitmap())
            using (Bitmap y = b.ToBitmap())
            {
                if (x.Size != y.Size) return false;
                for (int row = 0; row < x.Height; row++)
                    for (int col = 0; col < x.Width; col++)
                        if (x.GetPixel(col, row) != y.GetPixel(col, row)) return false;
                return true;
            }
        }

        public static int Run(string[] args)
        {
            string output = null, screenshot = null;
            var report = new JObject { ["status"] = "failed", ["scope"] = "dpi-awareness-icon-and-scaled-layout",
                ["physical_monitor_switch_tested"] = false };
            try
            {
                if (args.Length != 3 && args.Length != 5) throw new ArgumentException("参数：appearance-test --output <JSON> [--screenshot <PNG>]");
                if (args[1] != "--output") throw new ArgumentException("必须指定 --output");
                output = args[2];
                if (args.Length == 5)
                {
                    if (args[3] != "--screenshot") throw new ArgumentException("未知参数");
                    screenshot = args[4];
                }
                var scales = new JArray();
                foreach (int percent in new[] { 100, 125, 150, 200 })
                {
                    using (var form = new MainForm())
                    {
                        Check(form.AutoScaleMode == AutoScaleMode.Dpi, "窗体未使用 DPI 缩放");
                        IntPtr window = form.Handle;
                        Check(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), new IntPtr(-4)),
                            "UI 线程未启用 PerMonitorV2");
                        Check(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(window), new IntPtr(-4)),
                            "窗体未启用 PerMonitorV2");
                        Check(form.Icon != null && !SameIcon(form.Icon, SystemIcons.Application), "窗体仍使用默认图标");
                        // 按固定尺寸读取编译器写入的图标资源，不受 Shell 的 DPI 尺寸选择影响。
                        IntPtr iconHandle = LoadImage(GetModuleHandle(null), new IntPtr(32512), 1, 32, 32, 0);
                        Check(iconHandle != IntPtr.Zero, "EXE 未包含应用图标资源");
                        try
                        {
                            using (Icon executableIcon = Icon.FromHandle(iconHandle))
                                Check(SameIcon(form.Icon, executableIcon), "EXE 与窗体图标不一致");
                        }
                        finally { DestroyIcon(iconHandle); }
                        string image = screenshot == null ? null : Path.Combine(Path.GetDirectoryName(screenshot),
                            Path.GetFileNameWithoutExtension(screenshot) + "-" + percent + ".png");
                        scales.Add(form.CheckScaledLayout(percent, image));
                    }
                }
                report["dpi_awareness"] = "PerMonitorV2";
                report["icon"] = "executable-and-form-match";
                report["layout_simulation"] = scales;
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
