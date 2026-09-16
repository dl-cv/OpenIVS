using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;

namespace DlcvCSharpCppTest
{
    public partial class MainForm : Form
    {
        private ModelSession session;
        private Properties.Settings pathSettings;

        private Properties.Settings PathSettings
        {
            get { return pathSettings ?? (pathSettings = Properties.Settings.Default); }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ModelSession Session
        {
            get
            {
                if (session == null)
                {
                    if (IsDisposed || Disposing)
                        throw new ObjectDisposedException(nameof(MainForm));
                    session = new ModelSession();
                }
                return session;
            }
        }

        public MainForm()
        {
            InitializeComponent();
        }

        internal MainForm(Properties.Settings settings) : this()
        {
            pathSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!DesignMode && LicenseManager.UsageMode != LicenseUsageMode.Designtime)
                RestoreModelPath();
        }

        internal void RestoreModelPath()
        {
            try { pathTextBox.Text = PathSettings.LastModelPath; }
            catch (Exception ex) { statusLabel.Text = "读取上次模型路径失败：" + ex.Message; }
        }

        internal OpenFileDialog CreateModelDialog()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择模型",
                Filter = "AI模型 (*.dvt;*.dvo;*.dvst;*.dvso)|*.dvt;*.dvo;*.dvst;*.dvso",
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            };
            try
            {
                string lastPath = PathSettings.LastModelPath;
                if (!string.IsNullOrWhiteSpace(lastPath))
                {
                    string directory = Path.GetDirectoryName(lastPath);
                    if (Directory.Exists(directory)) dialog.InitialDirectory = directory;
                    dialog.FileName = Path.GetFileName(lastPath);
                }
            }
            catch (Exception ex) { statusLabel.Text = "读取上次模型目录失败：" + ex.Message; }
            return dialog;
        }

        internal bool ApplyModelSelection(DialogResult result, string selectedPath)
        {
            if (result != DialogResult.OK)
            {
                statusLabel.Text = "已取消模型选择。";
                return false;
            }
            if (session != null && (session.HasCSharpModel || session.HasCppModel))
                throw new InvalidOperationException("请先释放 C# 和 C++ 模型，再选择模型。");
            string path = Path.GetFullPath(selectedPath);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".dvt" && extension != ".dvo" && extension != ".dvst" && extension != ".dvso")
                throw new NotSupportedException("仅支持本地 .dvt、.dvo、.dvst、.dvso 模型。");
            if (!File.Exists(path)) throw new FileNotFoundException("模型文件不存在。", path);
            pathTextBox.Text = path;
            try
            {
                PathSettings.LastModelPath = path;
                PathSettings.Save();
                statusLabel.Text = "已选择模型，可分别加载 C# 或 C++ 模型。";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "已选择模型，但保存路径失败：" + ex.Message;
            }
            return true;
        }

        private bool BrowseModel()
        {
            using (var dialog = CreateModelDialog())
                return ApplyModelSelection(dialog.ShowDialog(this), dialog.FileName);
        }

        private void BrowseModelButton_Click(object sender, EventArgs e)
        {
            ExecuteOperation(() => BrowseModel());
        }

        public void RefreshModelState()
        {
            bool hasCSharp = session != null && session.HasCSharpModel;
            bool hasCpp = session != null && session.HasCppModel;
            csharpStateLabel.Text = hasCSharp ? "已加载，编号：" + session.CSharpModelIndex : "未加载，编号：-1";
            cppStateLabel.Text = hasCpp ? (session.CppCreatedFromIndex ? "共享模型，编号：" : "文件加载，编号：") + session.CppModelIndex : "未加载，编号：-1";
            loadCSharpButton.Enabled = !hasCSharp;
            loadCppButton.Enabled = !hasCpp;
            browseModelButton.Enabled = !hasCSharp && !hasCpp;
            deviceNumericUpDown.Enabled = browseModelButton.Enabled;
            foreach (Button button in new[] { loadCSharpButton, loadCppButton })
                button.BackColor = button.Enabled ? System.Drawing.Color.FromArgb(37, 99, 235) :
                    System.Drawing.Color.FromArgb(226, 232, 240);
            convertToCppButton.Enabled = hasCSharp && !hasCpp;
            getCSharpInfoButton.Enabled = hasCSharp;
            getCppInfoButton.Enabled = hasCpp;
            releaseCSharpButton.Enabled = hasCSharp;
            releaseCppButton.Enabled = hasCpp;
            if (!hasCSharp)
                csharpInfoTextBox.Clear();
            if (!hasCpp)
                cppInfoTextBox.Clear();
        }

        private void LoadCSharpButton_Click(object sender, EventArgs e)
        {
            ExecuteOperation(LoadCSharp);
        }

        private void LoadCppButton_Click(object sender, EventArgs e)
        {
            ExecuteOperation(LoadCpp);
        }

        private void ConvertToCppButton_Click(object sender, EventArgs e)
        {
            ExecuteOperation(ConvertToCpp);
        }

        private void GetCSharpInfoButton_Click(object sender, EventArgs e)
        {
            ExecuteOperation(GetCSharpInfo);
        }

        private void GetCppInfoButton_Click(object sender, EventArgs e)
        {
            ExecuteOperation(GetCppInfo);
        }

        private void ReleaseCSharpButton_Click(object sender, EventArgs e)
        {
            ExecuteOperation(ReleaseCSharp);
        }

        private void ReleaseCppButton_Click(object sender, EventArgs e)
        {
            ExecuteOperation(ReleaseCpp);
        }

        private void LoadCSharp()
        {
            if (string.IsNullOrWhiteSpace(pathTextBox.Text) && !BrowseModel()) return;
            string path = pathTextBox.Text;
            Session.LoadCSharp(path, Decimal.ToInt32(deviceNumericUpDown.Value));
            csharpInfoTextBox.Clear();
            statusLabel.Text = "已加载 C# 模型。";
        }

        private void LoadCpp()
        {
            if (string.IsNullOrWhiteSpace(pathTextBox.Text) && !BrowseModel()) return;
            Session.LoadCpp(pathTextBox.Text, Decimal.ToInt32(deviceNumericUpDown.Value));
            cppInfoTextBox.Clear();
            statusLabel.Text = "已由 C++ 从文件加载模型。";
        }

        private void ConvertToCpp()
        {
            Session.ConvertToCpp();
            cppInfoTextBox.Clear();
            statusLabel.Text = "已通过相同编号创建 C++ 模型。";
        }

        private void GetCSharpInfo()
        {
            csharpInfoTextBox.Text = Session.GetCSharpInfo();
            statusLabel.Text = "已获取 C# 模型信息。";
        }

        private void GetCppInfo()
        {
            cppInfoTextBox.Text = Session.GetCppInfo();
            statusLabel.Text = "已获取 C++ 模型信息。";
        }

        private void ReleaseCSharp()
        {
            Session.ReleaseCSharp();
            statusLabel.Text = "已释放 C# 模型。";
        }

        private void ReleaseCpp()
        {
            Session.ReleaseCpp();
            statusLabel.Text = "已释放 C++ 模型。";
        }

        private void ExecuteOperation(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "操作失败：" + ex.Message;
            }
            finally
            {
                RefreshModelState();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ReleaseSession();
            base.OnFormClosed(e);
        }

        private void ReleaseSession()
        {
            try
            {
                if (session != null)
                    session.Dispose();
            }
            catch (Exception ex)
            {
                if (statusLabel != null && !statusLabel.IsDisposed)
                    statusLabel.Text = "释放失败：" + ex.Message;
            }
        }
    }
}
