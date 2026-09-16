using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace DlcvCSharpCppTest
{
    public partial class MainForm : Form
    {
        private ModelSession session;

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

        public void RefreshModelState()
        {
            bool hasCSharp = session != null && session.HasCSharpModel;
            bool hasCpp = session != null && session.HasCppModel;
            csharpStateLabel.Text = hasCSharp ? "已加载，编号：" + session.CSharpModelIndex : "未加载，编号：-1";
            cppStateLabel.Text = hasCpp ? "已创建，编号：" + session.CppModelIndex : "未创建，编号：-1";
            loadCSharpButton.Enabled = !hasCSharp && !hasCpp;
            pathTextBox.Enabled = loadCSharpButton.Enabled;
            deviceNumericUpDown.Enabled = loadCSharpButton.Enabled;
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
            string path = pathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                using (var dialog = new OpenFileDialog
                {
                    Title = "选择模型文件",
                    Filter = "模型文件 (*.dvt;*.dvo;*.dvst;*.dvso)|*.dvt;*.dvo;*.dvst;*.dvso",
                    CheckFileExists = true,
                    Multiselect = false,
                    RestoreDirectory = true
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        statusLabel.Text = "已取消模型加载。";
                        return;
                    }
                    path = dialog.FileName;
                    pathTextBox.Text = path;
                }
            }
            Session.LoadCSharp(path, Decimal.ToInt32(deviceNumericUpDown.Value));
            csharpInfoTextBox.Clear();
            statusLabel.Text = "已加载 C# 模型。";
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
