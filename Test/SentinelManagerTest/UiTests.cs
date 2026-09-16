using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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
            public string CreateLmid() { Calls.Add("CreateLmid"); return "测试请求已返回，未确认 LMID 是否变化。"; }
            public string DisableNetwork() { Calls.Add("DisableNetwork"); return "测试三项网络设置已关闭。"; }
            public string ApplyLocalPatch() { Calls.Add("ApplyLocalPatch"); return "测试配置已写入，未操作真实配置或服务。"; }
            public string Repair() { Calls.Add("Repair"); return "测试一键修复完成，未操作真实配置或服务。"; }
        }

        public static int Run(string output)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            int exitCode = 1;
            var checks = new List<object>();
            var screenshots = new List<string>();
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
                    timeout.Stop();
                    var report = new { passed = exitCode == 0, timestamp = DateTimeOffset.Now.ToString("o"),
                        data_source = "固定测试数据，未连接真实服务", checks, screenshots };
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
                        form.ClientSize = new Size(1120, 860);
                        await Task.Delay(50);
                        var raw = (TextBox)control("rawResult");
                        var grid = (DataGridView)control("detailsGrid");
                        var tabs = (TabControl)control("resultTabs");
                        string[] buttonNames = { "infoButton", "lmidButton", "networkButton", "patchButton", "repairButton" };
                        check("启动不访问服务", client.Calls.Count == 0 && !form.Busy);
                        check("信息栏只读", raw.ReadOnly && grid.ReadOnly);
                        check("初始未执行", raw.Text.Contains("未执行"));
                        save("initial.png");
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
                        save("result.png");
                        Console.WriteLine("宽屏表格：高度 {0}，显示 {1}/{2} 行，首行高度 {3}。", grid.Height, grid.DisplayedRowCount(false), grid.Rows.Count, grid.Rows[0].Height);
                        check("宽屏显示完整表格", grid.DisplayedRowCount(false) == grid.Rows.Count);
                        check("初始与结果图不同", !File.ReadAllBytes(Path.Combine(output, "initial.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "result.png"))));
                        tabs.SelectedIndex = 1;
                        await Task.Delay(30);
                        save("raw-result.png");
                        tabs.SelectedIndex = 0;
                        form.ClientSize = new Size(752, 536);
                        await Task.Delay(30);
                        check("紧凑窗口可滚动", grid.DisplayedRowCount(false) < grid.Rows.Count);
                        check("紧凑窗口操作可见", buttonNames.All(n => {
                            var item = control(n);
                            var rect = form.RectangleToClient(item.RectangleToScreen(item.ClientRectangle));
                            return item.Visible && form.ClientRectangle.Contains(rect);
                        }));
                        save("compact.png");
                        foreach (var operation in new[] { SentinelOperation.CreateLmid, SentinelOperation.DisableNetwork, SentinelOperation.ApplyLocalPatch, SentinelOperation.Repair })
                            await form.RunOperationAsync(operation);
                        check("取消四项修改不请求服务", confirmations == 4 && client.Calls.Count == 1);
                        accepted = true;
                        foreach (var operation in new[] { SentinelOperation.CreateLmid, SentinelOperation.DisableNetwork, SentinelOperation.ApplyLocalPatch, SentinelOperation.Repair })
                            await form.RunOperationAsync(operation);
                        check("四项修改确认后分别执行一次", confirmations == 8 && client.Calls.SequenceEqual(new[] { "GetInfo", "CreateLmid", "DisableNetwork", "ApplyLocalPatch", "Repair" }));
                        client.Fail = true;
                        await form.RunOperationAsync(SentinelOperation.GetInfo);
                        check("失败结果替换旧结果", raw.Text.Contains("测试服务不可访问") && !raw.Text.Contains("测试一键修复完成"));
                        check("失败后恢复操作", !form.Busy && buttonNames.All(n => control(n).Enabled) && control("statusLabel").Text == "执行失败");
                        client.Fail = false;
                        await form.RunOperationAsync(SentinelOperation.GetInfo);
                        check("失败后可再次查询", control("statusLabel").Text == "执行完成" && raw.Text.Contains("TEST-0001"));
                        exitCode = 0;
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
