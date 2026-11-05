using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SentinelManager;

namespace SentinelManagerTest
{
    internal static class DesignerTests
    {
        public static int Run()
        {
            var type = typeof(SentinelManagerForm);
            if (type.GetConstructor(Type.EmptyTypes) == null ||
                type.GetMethod("InitializeComponent", BindingFlags.Instance | BindingFlags.NonPublic) == null ||
                !type.Assembly.GetManifestResourceNames().Contains("SentinelManager.SentinelManagerForm.resources"))
                throw new InvalidOperationException("缺少标准窗体构造入口、InitializeComponent 或窗体资源");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var surface = new DesignSurface(typeof(SentinelManagerForm)))
            {
                if (!surface.IsLoaded || surface.LoadErrors.Count != 0)
                    throw new InvalidOperationException("设计器加载失败：" + string.Join("; ", surface.LoadErrors.Cast<object>()));
                var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost));
                var form = (SentinelManagerForm)host.RootComponent;
                if (!(surface.View is Control)) throw new InvalidOperationException("缺少可视化设计视图");
                foreach (var name in new[] { "infoButton", "lmidButton", "networkButton", "patchButton", "repairButton",
                    "copyButton", "detailsGrid", "rawResult", "resultTabs" })
                {
                    if (form.Controls.Find(name, true).Length != 1)
                        throw new InvalidOperationException("设计器缺少控件：" + name);
                }
                var tabs = (TabControl)form.Controls.Find("resultTabs", true).Single();
                if (tabs.TabPages.Count != 2 || tabs.TabPages[0].Text != "信息概览" || tabs.TabPages[1].Text != "原始结果")
                    throw new InvalidOperationException("设计器缺少结果页签");
                if (form.Busy || form.ClientSize.Width < 700 || form.ClientSize.Height < 400)
                    throw new InvalidOperationException("设计器初始状态或窗体尺寸不正确");
                Console.WriteLine("WinForms DesignSurface 加载通过，操作按钮、结果页签和表格均存在。");
            }
            return 0;
        }
    }
}
