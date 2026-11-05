using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SentinelManager
{
    public sealed partial class SentinelManagerForm : Form
    {
        private readonly ISentinelClient client;
        private readonly Func<string, string, bool> confirm;
        private readonly Button[] buttons;
        private readonly Label[] notes;
        private readonly Panel[] cards;
        private bool? compact;
        private int layoutDpi;
        private bool layoutReady;
        public bool Busy { get; private set; }

        public SentinelManagerForm()
        {
            InitializeComponent();
            client = new SentinelClient();
            confirm = (dialogTitle, text) => MessageBox.Show(this, text, dialogTitle,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            buttons = new[] { infoButton, lmidButton, networkButton, patchButton, repairButton };
            notes = new[] { infoNote, lmidNote, networkNote, patchNote };
            cards = new[] { infoCard, lmidCard, networkCard, patchCard };
            ShowDetails(raw.Text);
            PerformAutoScale();
            layoutReady = true;
            ApplyResponsiveLayout();
        }

        public SentinelManagerForm(ISentinelClient client, Func<string, string, bool> confirm = null) : this()
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            if (confirm != null) this.confirm = confirm;
        }

        private async void HandleOperationClick(object sender, EventArgs e)
        {
            await RunOperationAsync((SentinelOperation)Array.IndexOf(buttons, (Button)sender));
        }

        private void HandleCopyClick(object sender, EventArgs e)
        {
            try { Clipboard.SetText(raw.Text); }
            catch (System.Runtime.InteropServices.ExternalException) { SetStatus("剪贴板暂不可用，请稍后重试", true); }
        }

        private void HandleResize(object sender, EventArgs e)
        {
            ApplyResponsiveLayout();
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            if (Busy) { e.Cancel = true; SetStatus("正在处理，请等待完成后关闭", false); }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyResponsiveLayout();
            FitToWorkingArea();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (!layoutReady || IsDisposed) return;
            // 等待框架完成控件缩放，再按逻辑尺寸重新计算动态布局。
            BeginInvoke(new Action(() => {
                if (IsDisposed) return;
                compact = null;
                ApplyResponsiveLayout();
                FitToWorkingArea();
            }));
        }

        private int Pixels(int logical)
        {
            return (int)Math.Round(logical * DeviceDpi / 96.0, MidpointRounding.AwayFromZero);
        }

        private void FitToWorkingArea()
        {
            var area = Screen.FromControl(this).WorkingArea;
            int margin = Pixels(16);
            int availableWidth = Math.Max(1, area.Width - margin * 2);
            int availableHeight = Math.Max(1, area.Height - margin * 2);
            MinimumSize = new Size(Math.Min(Pixels(720), availableWidth), Math.Min(Pixels(480), availableHeight));
            Size = new Size(Math.Min(Width, availableWidth), Math.Min(Height, availableHeight));
            Location = new Point(Math.Max(area.Left + margin, Math.Min(Left, area.Right - margin - Width)),
                Math.Max(area.Top + margin, Math.Min(Top, area.Bottom - margin - Height)));
        }

        private void ApplyResponsiveLayout()
        {
            if (!layoutReady) return;
            bool next = ClientSize.Width < Pixels(960);
            if (compact == next && layoutDpi == DeviceDpi) return;
            compact = next;
            layoutDpi = DeviceDpi;
            body.SuspendLayout();
            actions.SuspendLayout();
            body.ColumnStyles.Clear();
            body.RowStyles.Clear();
            body.ColumnCount = next ? 1 : 2;
            body.RowCount = next ? 2 : 1;
            body.ColumnStyles.Add(new ColumnStyle(next ? SizeType.Percent : SizeType.Absolute, next ? 100 : Pixels(256)));
            if (!next) body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (next) body.RowStyles.Add(new RowStyle(SizeType.Absolute, Pixels(128)));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.SetCellPosition(body.Controls[1], next ? new TableLayoutPanelCellPosition(0, 1) : new TableLayoutPanelCellPosition(1, 0));
            sidebar.Margin = next ? new Padding(0, 0, 0, Pixels(12)) : new Padding(0, 0, Pixels(16), 0);
            sidebar.Padding = new Padding(Pixels(next ? 6 : 14));
            sideTitle.Visible = sideNote.Visible = !next;
            actions.Controls.Clear();
            actions.ColumnStyles.Clear();
            actions.RowStyles.Clear();
            actions.ColumnCount = next ? 2 : 1;
            actions.RowCount = next ? 2 : 5;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, next ? 50 : 100));
            if (next) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < (next ? 2 : 4); i++) actions.RowStyles.Add(new RowStyle(SizeType.Absolute, Pixels(next ? 52 : 124)));
            if (!next) actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++)
            {
                notes[i].Visible = !next;
                notes[i].Padding = new Padding(0, Pixels(8), 0, 0);
                buttons[i].Margin = new Padding(0, 0, 0, Pixels(4));
                cards[i].Padding = new Padding(Pixels(next ? 4 : 10));
                cards[i].Margin = next ? new Padding(0, 0, Pixels(6), 0) : new Padding(0, 0, 0, Pixels(12));
                buttons[i].Height = Pixels(next ? 40 : 42);
                actions.Controls.Add(cards[i], next ? i % 2 : 0, next ? i / 2 : i);
            }
            sideNote.Height = Pixels(50);
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(Pixels(110), Pixels(36));
            details.ColumnHeadersHeight = Pixels(40);
            details.DefaultCellStyle.Padding = new Padding(Pixels(9), Pixels(2), Pixels(9), Pixels(2));
            actions.ResumeLayout(true);
            body.ResumeLayout(true);
        }

        private void SetStatus(string text, bool error)
        {
            status.Text = text;
            status.BackColor = error ? Color.FromArgb(255, 240, 237) : Color.FromArgb(229, 245, 239);
            status.ForeColor = error ? Color.FromArgb(178, 63, 47) : Color.FromArgb(30, 112, 88);
        }

        private void ShowDetails(string result)
        {
            details.Rows.Clear();
            bool first = true;
            foreach (var line in result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string text = line.Trim();
                if (text.Length == 0) continue;
                bool network = text == "网络访问配置：" || text == "网络配置：";
                if (first || text.StartsWith("加密狗 ID：", StringComparison.Ordinal) || network)
                {
                    int group = details.Rows.Add(network ? "网络访问配置" : first ? "服务与结果" : "设备信息", "");
                    details.Rows[group].DefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
                    details.Rows[group].DefaultCellStyle.BackColor = Color.FromArgb(241, 245, 251);
                    first = false;
                    if (network) continue;
                }
                int split = text.IndexOf('：');
                int length = 1;
                if (split < 0 && text.StartsWith("Feature ", StringComparison.Ordinal)) { split = text.IndexOf(" | ", StringComparison.Ordinal); length = 3; }
                details.Rows.Add(split < 0 ? "详细信息" : text.Substring(0, split), split < 0 ? text : text.Substring(split + length));
            }
            details.ClearSelection();
            if (details.Rows.Count > 0) details.FirstDisplayedScrollingRowIndex = 0;
        }

        public async Task RunOperationAsync(SentinelOperation operation)
        {
            if (Busy) return;
            string title = null, warning = null;
            const string patchWarning = "需要管理员权限，将覆盖配置文件：\n%LocalAppData%\\SafeNet Sentinel\\Sentinel LDK\\hasp_26146.ini\n\n写入内容：\nserveraddr=127.0.0.1\nbroadcastsearch=0\n\n重启 hasplms 服务，授权连接会短暂中断。";
            switch (operation)
            {
                case SentinelOperation.GetInfo: break;
                case SentinelOperation.CreateLmid: title = "创建新的 LMID"; warning = "将请求本机 Sentinel ACC 创建新的 LMID，可能影响现有授权连接。"; break;
                case SentinelOperation.DisableNetwork: title = "关闭网络访问"; warning = "将关闭访问远程授权、广播搜索及远程客户端访问。其他配置保持不变。"; break;
                case SentinelOperation.ApplyLocalPatch: title = "应用本地服务补丁"; warning = patchWarning; break;
                case SentinelOperation.Repair: title = "一键修复"; warning = "关闭三项网络访问、重置 LMID、写入本地配置并重启服务。\n\n" + patchWarning; break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
            if (title != null && !confirm(title, warning + "\n\n是否继续？")) return;
            Busy = true;
            foreach (var button in buttons) button.Enabled = false;
            progress.Visible = true;
            SetStatus("处理中…", false);
            bool success = false;
            string result;
            try
            {
                result = await Task.Run(() => {
                    switch (operation)
                    {
                        case SentinelOperation.GetInfo: return client.GetInfo();
                        case SentinelOperation.CreateLmid: return client.CreateLmid();
                        case SentinelOperation.DisableNetwork: return client.DisableNetwork();
                        case SentinelOperation.ApplyLocalPatch: return client.ApplyLocalPatch();
                        default: return client.Repair();
                    }
                });
                success = true;
            }
            catch (Exception ex) { result = "操作失败：" + ex.Message; }
            try
            {
                string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                raw.Text = "上次结果：\r\n" + result.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n\r\n时间：" + timestamp;
                raw.Select(0, 0);
                ShowDetails(result);
                executed.Text = "最近执行：" + timestamp;
                SetStatus(success ? "执行完成" : "执行失败", !success);
                copy.Enabled = true;
            }
            finally
            {
                Busy = false;
                progress.Visible = false;
                foreach (var button in buttons) button.Enabled = true;
            }
        }
    }

    public enum SentinelOperation { GetInfo, CreateLmid, DisableNetwork, ApplyLocalPatch, Repair }
}
