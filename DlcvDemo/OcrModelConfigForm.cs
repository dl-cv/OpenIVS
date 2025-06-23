using System;
using System.IO;
using System.Windows.Forms;

namespace DlcvDemo
{
    public partial class OcrModelConfigForm : Form
    {
        public string DetModelPath { get; private set; }
        public string OcrModelPath { get; private set; }
        public int DeviceId { get; private set; }
        public float HorizontalScale { get; private set; }

        public OcrModelConfigForm()
        {
            InitializeComponent();
            InitializeForm();
        }

        public OcrModelConfigForm(int defaultDeviceId) : this()
        {
            numDevice.Value = defaultDeviceId;
        }

        private void InitializeForm()
        {
            // 设置默认值
            DeviceId = 0;
            HorizontalScale = 1.0f;
            
            // 设置文本框的ToolTip提示
            ToolTip toolTip = new ToolTip();
            toolTip.SetToolTip(txtDetModel, "选择用于文本检测的模型文件");
            toolTip.SetToolTip(txtOcrModel, "选择用于文本识别的OCR模型文件");
            toolTip.SetToolTip(numDevice, "选择用于推理的GPU设备ID（0表示第一个GPU）");
            toolTip.SetToolTip(numHorizontalScale, "水平缩放比例，默认1.0。当OCR文字比较密集时，建议设置为1.5");
        }

        private void btnBrowseDet_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "深度视觉模型文件 (*.dvt;*.dvp)|*.dvt;*.dvp|所有文件 (*.*)|*.*";
                openFileDialog.Title = "选择检测模型文件";
                openFileDialog.RestoreDirectory = true;
                
                // 尝试设置初始目录
                try
                {
                    if (!string.IsNullOrEmpty(Properties.Settings.Default.LastModelPath))
                    {
                        openFileDialog.InitialDirectory = Path.GetDirectoryName(Properties.Settings.Default.LastModelPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"设置初始目录失败: {ex.Message}");
                }

                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtDetModel.Text = openFileDialog.FileName;
                    DetModelPath = openFileDialog.FileName;
                    
                    // 保存路径到设置
                    Properties.Settings.Default.LastModelPath = openFileDialog.FileName;
                    Properties.Settings.Default.Save();
                }
            }
        }

        private void btnBrowseOcr_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "深度视觉模型文件 (*.dvt;*.dvp)|*.dvt;*.dvp|所有文件 (*.*)|*.*";
                openFileDialog.Title = "选择OCR模型文件";
                openFileDialog.RestoreDirectory = true;
                
                // 尝试设置初始目录
                try
                {
                    if (!string.IsNullOrEmpty(Properties.Settings.Default.LastModelPath))
                    {
                        openFileDialog.InitialDirectory = Path.GetDirectoryName(Properties.Settings.Default.LastModelPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"设置初始目录失败: {ex.Message}");
                }

                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtOcrModel.Text = openFileDialog.FileName;
                    OcrModelPath = openFileDialog.FileName;
                }
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            // 验证输入
            if (string.IsNullOrEmpty(txtDetModel.Text))
            {
                MessageBox.Show("请选择检测模型文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(txtOcrModel.Text))
            {
                MessageBox.Show("请选择OCR模型文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 检查文件是否存在
            if (!File.Exists(txtDetModel.Text))
            {
                MessageBox.Show("检测模型文件不存在！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!File.Exists(txtOcrModel.Text))
            {
                MessageBox.Show("OCR模型文件不存在！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 设置返回值
            DetModelPath = txtDetModel.Text;
            OcrModelPath = txtOcrModel.Text;
            DeviceId = (int)numDevice.Value;
            HorizontalScale = (float)numHorizontalScale.Value;

            // 关闭对话框
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
} 