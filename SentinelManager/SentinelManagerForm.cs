using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SentinelManager
{
    public enum SentinelOperation { GetInfo, CreateLmid, DisableNetwork, ApplyLocalPatch, Repair }

    public sealed class SentinelManagerForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(28, 46, 70);
        private static readonly Color Blue = Color.FromArgb(37, 99, 235);
        private readonly ISentinelClient client;
        private readonly Func<string, string, bool> confirm;
        private readonly TableLayoutPanel body = new TableLayoutPanel();
        private readonly TableLayoutPanel actions = new TableLayoutPanel();
        private readonly Panel sidebar = new Panel();
        private readonly Label sideTitle = new Label();
        private readonly Label sideNote = new Label();
        private readonly Button[] buttons = new Button[5];
        private readonly Label[] notes = new Label[4];
        private readonly Panel[] cards = new Panel[4];
        private readonly Label status = new Label();
        private readonly Label executed = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button copy;
        private readonly DataGridView details = new DataGridView();
        private readonly TextBox raw = new TextBox();
        private readonly TabControl tabs = new TabControl();
        private bool? compact;
        public bool Busy { get; private set; }

        public SentinelManagerForm(ISentinelClient client, Func<string, string, bool> confirm = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.confirm = confirm ?? ((dialogTitle, text) => MessageBox.Show(this, text, dialogTitle,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
            Text = "Sentinel 加密狗管理";
            Name = "SentinelManagerForm";
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Regular, GraphicsUnit.Pixel);
            ForeColor = Ink;
            BackColor = Color.FromArgb(243, 246, 251);
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(720, 480);
            ClientSize = new Size(1120, 860);
            StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 16), RowCount = 2, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons[4] = MakeButton("一键修复", "repairButton", true);
            buttons[4].Size = new Size(140, 48);
            heading.Controls.Add(buttons[4], 0, 0);
            var title = new Panel { Dock = DockStyle.Fill };
            title.Controls.Add(new Label { Text = "本机设备与服务管理", AutoSize = true, Location = new Point(0, 39), ForeColor = Color.SlateGray });
            title.Controls.Add(new Label { Text = "Sentinel 加密狗管理", AutoSize = true, Font = new Font(Font.FontFamily, 26, FontStyle.Bold, GraphicsUnit.Pixel), Location = new Point(0, 0) });
            heading.Controls.Add(title, 1, 0);
            root.Controls.Add(heading, 0, 0);
            body.Dock = DockStyle.Fill;
            body.Margin = Padding.Empty;
            root.Controls.Add(body, 0, 1);
            sidebar.BackColor = Color.White;
            sidebar.Padding = new Padding(14);
            sidebar.Dock = DockStyle.Fill;
            sidebar.Margin = new Padding(0, 0, 16, 0);
            sideTitle.Text = "管理操作";
            sideTitle.Font = new Font(Font, FontStyle.Bold);
            sideTitle.Dock = DockStyle.Top;
            sideTitle.Height = 34;
            sideNote.Text = "仅显示本机 Sentinel HL 设备";
            sideNote.ForeColor = Color.SlateGray;
            sideNote.Dock = DockStyle.Bottom;
            sideNote.Height = 40;
            actions.Dock = DockStyle.Fill;
            actions.Margin = Padding.Empty;
            string[] labels = { "获取加密狗信息", "创建新的LMID", "关闭网络访问", "应用本地服务补丁" };
            string[] names = { "infoButton", "lmidButton", "networkButton", "patchButton" };
            string[] descriptions = { "查看设备信息。服务未运行时自动启动。", "重新生成 LMID。", "关闭远程授权、广播搜索和远程访问。", "写入本地配置并重启服务，需管理员权限。" };
            for (int i = 0; i < 4; i++)
            {
                buttons[i] = MakeButton(labels[i], names[i], i == 0);
                if (i == 2) { buttons[i].BackColor = Color.FromArgb(255, 248, 237); buttons[i].ForeColor = Color.FromArgb(165, 77, 20); }
                cards[i] = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(246, 248, 252), Padding = new Padding(10), Margin = new Padding(0, 0, 0, 12) };
                buttons[i].Dock = DockStyle.Top;
                buttons[i].Height = 42;
                notes[i] = new Label { Text = descriptions[i], Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), ForeColor = Color.SlateGray };
                cards[i].Controls.Add(notes[i]);
                cards[i].Controls.Add(buttons[i]);
            }
            sidebar.Controls.Add(actions);
            sidebar.Controls.Add(sideTitle);
            sidebar.Controls.Add(sideNote);
            var result = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16), ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
            result.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            var resultHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            resultHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            resultHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
            status.Name = "statusLabel";
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(10, 0, 0, 0);
            SetStatus("就绪", false);
            copy = MakeButton("复制结果", "copyButton", false);
            copy.Dock = DockStyle.Fill;
            copy.Enabled = false;
            copy.Click += (s, e) => { try { Clipboard.SetText(raw.Text); } catch (System.Runtime.InteropServices.ExternalException) { SetStatus("剪贴板暂不可用，请稍后重试", true); } };
            resultHeader.Controls.Add(status, 0, 0);
            resultHeader.Controls.Add(copy, 1, 0);
            result.Controls.Add(resultHeader, 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Style = ProgressBarStyle.Marquee;
            progress.Visible = false;
            result.Controls.Add(progress, 0, 1);
            tabs.Name = "resultTabs";
            tabs.Dock = DockStyle.Fill;
            var overview = new TabPage("信息概览") { BackColor = Color.White };
            var original = new TabPage("原始结果") { BackColor = Color.White };
            tabs.TabPages.Add(overview);
            tabs.TabPages.Add(original);
            details.Name = "detailsGrid";
            details.Dock = DockStyle.Fill;
            details.ReadOnly = true;
            details.AllowUserToAddRows = false;
            details.AllowUserToDeleteRows = false;
            details.AllowUserToResizeRows = false;
            details.RowHeadersVisible = false;
            details.BackgroundColor = Color.White;
            details.BorderStyle = BorderStyle.None;
            details.CellBorderStyle = DataGridViewCellBorderStyle.None;
            details.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            details.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            details.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            details.DefaultCellStyle.Font = Font;
            details.DefaultCellStyle.Padding = new Padding(9, 2, 9, 2);
            details.DefaultCellStyle.SelectionBackColor = Color.FromArgb(223, 235, 255);
            details.DefaultCellStyle.SelectionForeColor = Ink;
            details.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 253);
            details.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 251);
            details.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(82, 101, 128);
            details.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            details.ColumnHeadersHeight = 40;
            details.EnableHeadersVisualStyles = false;
            details.Columns.Add("name", "信息项");
            details.Columns.Add("value", "内容");
            details.Columns[0].FillWeight = 36;
            details.Columns[1].FillWeight = 64;
            foreach (DataGridViewColumn column in details.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            overview.Controls.Add(details);
            raw.Name = "rawResult";
            raw.Dock = DockStyle.Fill;
            raw.Multiline = true;
            raw.ReadOnly = true;
            raw.ScrollBars = ScrollBars.Both;
            raw.WordWrap = true;
            raw.BorderStyle = BorderStyle.None;
            raw.BackColor = Color.White;
            raw.Text = "未执行：点击按钮获取加密狗信息。";
            original.Controls.Add(raw);
            result.Controls.Add(tabs, 0, 2);
            executed.Name = "executedLabel";
            executed.Text = "尚未执行 · 点击“获取加密狗信息”开始";
            executed.ForeColor = Color.SlateGray;
            executed.Dock = DockStyle.Fill;
            executed.TextAlign = ContentAlignment.BottomLeft;
            result.Controls.Add(executed, 0, 3);
            body.Controls.Add(sidebar);
            body.Controls.Add(result);
            for (int i = 0; i < buttons.Length; i++)
            {
                var operation = (SentinelOperation)i;
                buttons[i].Click += async (s, e) => await RunOperationAsync(operation);
            }
            Resize += (s, e) => ApplyResponsiveLayout();
            FormClosing += (s, e) => { if (Busy) { e.Cancel = true; SetStatus("正在处理，请等待完成后关闭", false); } };
            ShowDetails(raw.Text);
            ApplyResponsiveLayout();
            var area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width - 32), Math.Min(Height, area.Height - 32));
        }

        private static Button MakeButton(string text, string name, bool primary)
        {
            var button = new Button { Name = name, Text = text, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Ink,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold, GraphicsUnit.Pixel), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 4) };
            button.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(203, 215, 231);
            return button;
        }

        private void ApplyResponsiveLayout()
        {
            bool next = ClientSize.Width < 960;
            if (compact == next) return;
            compact = next;
            body.SuspendLayout();
            actions.SuspendLayout();
            body.ColumnStyles.Clear();
            body.RowStyles.Clear();
            body.ColumnCount = next ? 1 : 2;
            body.RowCount = next ? 2 : 1;
            body.ColumnStyles.Add(new ColumnStyle(next ? SizeType.Percent : SizeType.Absolute, next ? 100 : 256));
            if (!next) body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (next) body.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.SetCellPosition(body.Controls[1], next ? new TableLayoutPanelCellPosition(0, 1) : new TableLayoutPanelCellPosition(1, 0));
            sidebar.Margin = next ? new Padding(0, 0, 0, 12) : new Padding(0, 0, 16, 0);
            sidebar.Padding = new Padding(next ? 6 : 14);
            sideTitle.Visible = sideNote.Visible = !next;
            actions.Controls.Clear();
            actions.ColumnStyles.Clear();
            actions.RowStyles.Clear();
            actions.ColumnCount = next ? 2 : 1;
            actions.RowCount = next ? 2 : 5;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, next ? 50 : 100));
            if (next) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < (next ? 2 : 4); i++) actions.RowStyles.Add(new RowStyle(SizeType.Absolute, next ? 52 : 124));
            if (!next) actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++)
            {
                notes[i].Visible = !next;
                cards[i].Padding = new Padding(next ? 4 : 10);
                cards[i].Margin = next ? new Padding(0, 0, 6, 0) : new Padding(0, 0, 0, 12);
                buttons[i].Height = next ? 40 : 42;
                actions.Controls.Add(cards[i], next ? i % 2 : 0, next ? i / 2 : i);
            }
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
}
