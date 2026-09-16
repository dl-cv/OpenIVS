using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SentinelManager;

namespace SentinelManagerTest
{
    internal static class UiTests
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

        private sealed class FixedClient : ISentinelClient
        {
            public readonly List<string> Calls = new List<string>();
            public bool Fail;
            public string GetInfo()
            {
                Calls.Add("GetInfo");
                Thread.Sleep(50);
                if (Fail) throw new InvalidOperationException("测试服务不可访问");
                return "Sentinel 服务（hasplms）：运行中\n本机 ACC：可访问\nSentinel 加密狗数量：1\n\n"
                    + "加密狗 ID：TEST-0001（本机）\n类型：Sentinel HL Pro\nFeature 数量：5\n"
                    + "  Feature 0 | 测试基础功能 | 产品 10\n  Feature 12 | 测试推理功能 | 产品 10\n"
                    + "  Feature 42 | 测试训练功能 | 产品 10\n  Feature 80 | 测试扩展功能 | 产品 10\n  Feature 99 | 测试工具功能 | 产品 10\n"
                    + "\n网络访问配置：\n  访问远程授权：允许\n  广播搜索远程授权：允许\n  远程客户端访问：允许\n"
                    + "\n数据来源：固定测试数据，不连接真实服务";
            }
            public string CreateLmid() { Calls.Add("CreateLmid"); return "LMID 已更新并回读确认。\n原 LMID：" + new string('A', 40) + "\n新 LMID：" + new string('B', 40); }
            public string DisableNetwork() { Calls.Add("DisableNetwork"); return "测试三项网络设置已关闭。"; }
            public string ApplyLocalPatch() { Calls.Add("ApplyLocalPatch"); return "测试配置已写入，未操作真实配置或服务。"; }
            public string Repair() { Calls.Add("Repair"); return "测试一键修复完成，未操作真实配置或服务。"; }
        }

        private static object SizeReport(Size size)
        {
            return new { width = size.Width, height = size.Height };
        }

        private static object BoundsReport(Rectangle bounds)
        {
            return new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height };
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        private static bool FullyVisible(Control item)
        {
            if (!item.Visible || item.Width <= 0 || item.Height <= 0) return false;
            var bounds = item.RectangleToScreen(item.ClientRectangle);
            for (var parent = item.Parent; parent != null; parent = parent.Parent)
                if (!parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds))
                {
                    Console.Error.WriteLine("控件 {0} {1} 超出 {2} {3}。", item.Name, bounds, parent.GetType().Name,
                        parent.RectangleToScreen(parent.ClientRectangle));
                    return false;
                }
            return true;
        }

        private static string[] TextLayoutErrors(Form form)
        {
            var errors = new List<string>();
            var items = Descendants(form).Where(c => c.Visible && (c is Label || c is Button)).ToArray();
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Text)) continue;
                if (!FullyVisible(item)) errors.Add(item.Name + ": 控件显示不完整：" + item.Text);
                int border = item is Button ? 4 : 0;
                int width = item.ClientSize.Width - item.Padding.Horizontal - border;
                int height = item.ClientSize.Height - item.Padding.Vertical - border;
                var flags = TextFormatFlags.NoPrefix | (item is Button || ((Label)item).AutoSize
                    ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                using (var graphics = item.CreateGraphics())
                {
                    var measured = TextRenderer.MeasureText(graphics, item.Text, item.Font,
                        new Size(Math.Max(1, width), int.MaxValue), flags);
                    if (measured.Width > width || measured.Height > height)
                        errors.Add(item.Name + ": 文字显示不完整：" + item.Text);
                }
                if (items.Any(other => other != item && other.Parent == item.Parent && item.Bounds.IntersectsWith(other.Bounds)))
                    errors.Add(item.Name + ": 文字控件重叠：" + item.Text);
            }
            foreach (var tabs in Descendants(form).OfType<TabControl>().Where(c => c.Visible))
                for (int i = 0; i < tabs.TabCount; i++)
                {
                    var rect = tabs.GetTabRect(i);
                    using (var graphics = tabs.CreateGraphics())
                    {
                        var text = TextRenderer.MeasureText(graphics, tabs.TabPages[i].Text, tabs.Font,
                            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine);
                        if (!tabs.ClientRectangle.Contains(rect) || text.Width > rect.Width || text.Height > rect.Height)
                            errors.Add("页签文字显示不完整：" + tabs.TabPages[i].Text);
                    }
                }
            return errors.ToArray();
        }

        public static int Run(string output)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            int exitCode = 1;
            var checks = new List<object>();
            var screenshots = new List<string>();
            var metrics = new Dictionary<string, object>();
            var layouts = new Dictionary<string, object>();
            bool finished = false;
            var client = new FixedClient();
            bool accepted = false;
            int confirmations = 0;
            Action<string, bool> check = (name, passed) => {
                checks.Add(new { name, passed });
                if (!passed) throw new InvalidOperationException(name);
            };
            using (var form = new SentinelManagerForm(client, (title, text) => { confirmations++; return accepted; }))
            using (var timeout = new System.Windows.Forms.Timer { Interval = 20000 })
            {
                Func<string, Control> control = name => form.Controls.Find(name, true).Single();
                Action<string> save = name => {
                    // 直接绘制本窗体，不读取桌面像素。
                    using (var full = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(full, new Rectangle(Point.Empty, form.Size));
                        full.Save(Path.Combine(output, name), ImageFormat.Png);
                    }
                    screenshots.Add(name);
                };
                Action finish = () => {
                    if (finished) return;
                    finished = true;
                    timeout.Stop();
                    var report = new { passed = exitCode == 0, timestamp = DateTimeOffset.Now.ToString("o"),
                        data_source = "固定测试数据，未连接真实服务", metrics, layouts, checks, screenshots };
                    File.WriteAllText(Path.Combine(output, "ui-test-results.json"), new JavaScriptSerializer().Serialize(report), new UTF8Encoding(false));
                    form.Close();
                    Application.ExitThread();
                };
                timeout.Tick += (s, e) => {
                    checks.Add(new { name = "界面验证超时", passed = false });
                    exitCode = 1;
                    finish();
                };
                form.Shown += async (s, e) => {
                    timeout.Start();
                    try
                    {
                        await Task.Delay(50);
                        int dpi = form.DeviceDpi;
                        double scale = dpi / 96.0;
                        Func<int, int> pixels = logical => (int)Math.Round(logical * scale, MidpointRounding.AwayFromZero);
                        var area = Screen.FromControl(form).WorkingArea;
                        var repair = control("repairButton");
                        var threadContext = GetThreadDpiAwarenessContext();
                        var windowContext = GetWindowDpiAwarenessContext(form.Handle);
                        metrics["thread_per_monitor_v2"] = AreDpiAwarenessContextsEqual(threadContext, new IntPtr(-4));
                        metrics["window_per_monitor_v2"] = AreDpiAwarenessContextsEqual(windowContext, new IntPtr(-4));
                        metrics["thread_dpi_unaware"] = AreDpiAwarenessContextsEqual(threadContext, new IntPtr(-1));
                        metrics["window_dpi_unaware"] = AreDpiAwarenessContextsEqual(windowContext, new IntPtr(-1));
                        metrics["device_dpi"] = dpi;
                        metrics["scale_factor"] = scale;
                        metrics["initial_window_bounds"] = BoundsReport(form.Bounds);
                        metrics["initial_client_size"] = SizeReport(form.ClientSize);
                        metrics["screen_working_area"] = BoundsReport(area);
                        metrics["repair_button_size"] = SizeReport(repair.Size);
                        metrics["font_unit"] = form.Font.Unit.ToString();
                        metrics["font_size_points"] = form.Font.SizeInPoints;
                        metrics["repair_font_size_points"] = repair.Font.SizeInPoints;
                        metrics["auto_scale_mode"] = form.AutoScaleMode.ToString();
                        check("实际 DPI 有效", dpi > 0);
                        check("使用 DPI 自动缩放", form.AutoScaleMode == AutoScaleMode.Dpi);
                        check("基础字体为 12pt", form.Font.Unit == GraphicsUnit.Point && Math.Abs(form.Font.SizeInPoints - 12F) < 0.01F);
                        check("初始窗口位于工作区", area.Contains(form.Bounds));
                        check("修复按钮按 DPI 缩放", Math.Abs(repair.Width - 140 * scale) <= 2 && Math.Abs(repair.Height - 48 * scale) <= 2);
                        Action<string> layout = name => {
                            var errors = TextLayoutErrors(form);
                            layouts[name] = new { client_size = SizeReport(form.ClientSize), window_size = SizeReport(form.Size),
                                logical_width = form.ClientSize.Width / scale, compact = form.ClientSize.Width / scale < 960,
                                repair_button_size = SizeReport(repair.Size), text_errors = errors };
                            check(name + "文字与操作完整显示", errors.Length == 0);
                            check(name + "修复按钮尺寸正确", Math.Abs(repair.Width - 140 * scale) <= 2 && Math.Abs(repair.Height - 48 * scale) <= 2);
                        };
                        save("initial.png");
                        layout("initial");
                        var raw = (TextBox)control("rawResult");
                        var grid = (DataGridView)control("detailsGrid");
                        var tabs = (TabControl)control("resultTabs");
                        string[] buttonNames = { "infoButton", "lmidButton", "networkButton", "patchButton", "repairButton" };
                        check("启动不访问服务", client.Calls.Count == 0 && !form.Busy);
                        check("信息栏只读", raw.ReadOnly && grid.ReadOnly);
                        check("初始未执行", raw.Text.Contains("未执行"));
                        form.ClientSize = new Size(pixels(1120), pixels(860));
                        await Task.Delay(50);
                        check("宽屏按逻辑宽度显示", form.ClientSize.Width / scale >= 960 && Math.Abs(form.ClientSize.Width - pixels(1120)) <= 2);
                        var task = form.RunOperationAsync(SentinelOperation.GetInfo);
                        check("执行中禁用全部操作", form.Busy && buttonNames.All(n => !control(n).Enabled));
                        form.Close();
                        check("执行中不能关闭窗体", !form.IsDisposed);
                        await form.RunOperationAsync(SentinelOperation.GetInfo);
                        await task;
                        check("阻止重复请求", client.Calls.SequenceEqual(new[] { "GetInfo" }));
                        check("查询无需确认", confirmations == 0);
                        check("完整展示设备和网络信息", new[] { "TEST-0001", "Feature 99", "远程客户端访问", "本机 ACC", "Feature 数量：5" }.All(raw.Text.Contains));
                        check("替换初始结果", !raw.Text.Contains("未执行"));
                        check("结束恢复操作", !form.Busy && buttonNames.All(n => control(n).Enabled));
                        check("复制按钮已启用", control("copyButton").Enabled);
                        layout("result");
                        check("表格文字完整显示", grid.Rows.Cast<DataGridViewRow>().All(row =>
                            row.Height >= row.GetPreferredHeight(row.Index, DataGridViewAutoSizeRowMode.AllCells, true))
                            && grid.Columns.Cast<DataGridViewColumn>().All(column => {
                                var rect = grid.GetCellDisplayRectangle(column.Index, -1, false);
                                using (var graphics = grid.CreateGraphics())
                                {
                                    var measured = TextRenderer.MeasureText(graphics, column.HeaderText,
                                        grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font);
                                    return rect.Width >= measured.Width && rect.Height >= measured.Height;
                                }
                            }));
                        save("result.png");
                        Console.WriteLine("宽屏表格：高度 {0}，显示 {1}/{2} 行，首行高度 {3}。", grid.Height, grid.DisplayedRowCount(false), grid.Rows.Count, grid.Rows[0].Height);
                        check("宽屏显示完整表格", grid.DisplayedRowCount(false) == grid.Rows.Count);
                        check("初始与结果图不同", !File.ReadAllBytes(Path.Combine(output, "initial.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "result.png"))));
                        tabs.SelectedIndex = 1;
                        await Task.Delay(30);
                        layout("raw-result");
                        check("原始结果支持完整阅读", FullyVisible(raw) && raw.Multiline
                            && (raw.ScrollBars == ScrollBars.Both || raw.ScrollBars == ScrollBars.Vertical && raw.WordWrap));
                        save("raw-result.png");
                        tabs.SelectedIndex = 0;
                        form.ClientSize = new Size(pixels(752), pixels(536));
                        await Task.Delay(30);
                        check("紧凑窗口按逻辑宽度显示", form.ClientSize.Width / scale < 960 && Math.Abs(form.ClientSize.Width - pixels(752)) <= 2
                            && Math.Abs(control("infoButton").PointToScreen(Point.Empty).Y - control("lmidButton").PointToScreen(Point.Empty).Y) <= 2
                            && control("lmidButton").PointToScreen(Point.Empty).X > control("infoButton").PointToScreen(Point.Empty).X);
                        layout("compact");
                        check("紧凑窗口可滚动", grid.DisplayedRowCount(false) < grid.Rows.Count);
                        check("紧凑窗口操作可见", buttonNames.All(n => {
                            var item = control(n);
                            return FullyVisible(item);
                        }));
                        save("compact.png");
                        foreach (var operation in new[] { SentinelOperation.CreateLmid, SentinelOperation.DisableNetwork, SentinelOperation.ApplyLocalPatch, SentinelOperation.Repair })
                            await form.RunOperationAsync(operation);
                        check("取消四项修改不请求服务", confirmations == 4 && client.Calls.Count == 1);
                        accepted = true;
                        foreach (var operation in new[] { SentinelOperation.CreateLmid, SentinelOperation.DisableNetwork, SentinelOperation.ApplyLocalPatch, SentinelOperation.Repair })
                        {
                            await form.RunOperationAsync(operation);
                            if (operation == SentinelOperation.CreateLmid)
                                check("LMID 显示自动核验及前后值", raw.Text.Contains("LMID 已更新并回读确认")
                                    && raw.Text.Contains("原 LMID：" + new string('A', 40))
                                    && raw.Text.Contains("新 LMID：" + new string('B', 40))
                                    && !raw.Text.Contains("ACC 核实"));
                        }
                        check("四项修改确认后分别执行一次", confirmations == 8 && client.Calls.SequenceEqual(new[] { "GetInfo", "CreateLmid", "DisableNetwork", "ApplyLocalPatch", "Repair" }));
                        client.Fail = true;
                        await form.RunOperationAsync(SentinelOperation.GetInfo);
                        check("失败结果替换旧结果", raw.Text.Contains("测试服务不可访问") && !raw.Text.Contains("测试一键修复完成"));
                        check("失败后恢复操作", !form.Busy && buttonNames.All(n => control(n).Enabled) && control("statusLabel").Text == "执行失败");
                        client.Fail = false;
                        await form.RunOperationAsync(SentinelOperation.GetInfo);
                        check("失败后可再次查询", control("statusLabel").Text == "执行完成" && raw.Text.Contains("TEST-0001"));
                        if (!finished) exitCode = 0;
                    }
                    catch (Exception ex) { checks.Add(new { name = "异常：" + ex.Message, passed = false }); Console.Error.WriteLine(ex); }
                    finally { finish(); }
                };
                Application.Run(form);
            }
            Console.WriteLine(exitCode == 0 ? "界面验证通过。" : "界面验证失败。");
            return exitCode;
        }
    }
}
