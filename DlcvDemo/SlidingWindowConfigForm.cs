using System;
using System.Windows.Forms;

namespace DlcvDemo
{
    // 这是一个部分类，与SlidingWindowConfigForm.Designer.cs中定义的部分类合并
    public partial class SlidingWindowConfigForm : Form
    {
        public int SmallImgWidth { get; private set; }
        public int SmallImgHeight { get; private set; }
        public int HorizontalOverlap { get; private set; }
        public int VerticalOverlap { get; private set; }
        public float Threshold { get; private set; }
        public float IouThreshold { get; private set; }
        public float CombineIosThreshold { get; private set; }

        public SlidingWindowConfigForm()
        {
            InitializeComponent();
            LoadDefaultValues();
        }

        private void LoadDefaultValues()
        {
            // 设置默认值
            SmallImgWidth = 832;
            SmallImgHeight = 704;
            HorizontalOverlap = 16;
            VerticalOverlap = 16;
            Threshold = 0.5f;
            IouThreshold = 0.2f;
            CombineIosThreshold = 0.2f;

            // 设置文本框的默认值
            txtWidth.Text = SmallImgWidth.ToString();
            txtHeight.Text = SmallImgHeight.ToString();
            txtHOverlap.Text = HorizontalOverlap.ToString();
            txtVOverlap.Text = VerticalOverlap.ToString();
            txtThreshold.Text = Threshold.ToString();
            txtIouThreshold.Text = IouThreshold.ToString();
            txtCombineIosThreshold.Text = CombineIosThreshold.ToString();
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            try
            {
                SmallImgWidth = int.Parse(txtWidth.Text);
                SmallImgHeight = int.Parse(txtHeight.Text);
                HorizontalOverlap = int.Parse(txtHOverlap.Text);
                VerticalOverlap = int.Parse(txtVOverlap.Text);
                Threshold = float.Parse(txtThreshold.Text);
                IouThreshold = float.Parse(txtIouThreshold.Text);
                CombineIosThreshold = float.Parse(txtCombineIosThreshold.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show("请输入有效的数值！\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.None;
            }
        }
    }
} 