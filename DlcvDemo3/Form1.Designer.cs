namespace DlcvDemo3
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.labelModel1 = new System.Windows.Forms.Label();
            this.txtModel1Path = new System.Windows.Forms.TextBox();
            this.btnBrowseModel1 = new System.Windows.Forms.Button();
            this.btnLoadModel1 = new System.Windows.Forms.Button();
            this.labelModel2 = new System.Windows.Forms.Label();
            this.txtModel2Path = new System.Windows.Forms.TextBox();
            this.btnBrowseModel2 = new System.Windows.Forms.Button();
            this.btnLoadModel2 = new System.Windows.Forms.Button();
            this.labelImage = new System.Windows.Forms.Label();
            this.txtImagePath = new System.Windows.Forms.TextBox();
            this.btnBrowseImage = new System.Windows.Forms.Button();
            this.btnInfer = new System.Windows.Forms.Button();
            this.btnSpeedTest = new System.Windows.Forms.Button();
            this.labelFixedCrop = new System.Windows.Forms.Label();
            this.labelModel2Threads = new System.Windows.Forms.Label();
            this.numModel2Threads = new System.Windows.Forms.NumericUpDown();
            this.btnReleaseModels = new System.Windows.Forms.Button();
            this.progressBarInference = new System.Windows.Forms.ProgressBar();
            this.lblInferenceProgress = new System.Windows.Forms.Label();
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            this.imagePanel1 = new DLCV.ImageViewer();
            ((System.ComponentModel.ISupportInitialize)(this.numModel2Threads)).BeginInit();
            this.SuspendLayout();
            // 
            // labelModel1
            // 
            this.labelModel1.AutoSize = true;
            this.labelModel1.Location = new System.Drawing.Point(12, 17);
            this.labelModel1.Name = "labelModel1";
            this.labelModel1.Size = new System.Drawing.Size(130, 24);
            this.labelModel1.TabIndex = 0;
            this.labelModel1.Text = "模型1路径(定位)";
            // 
            // txtModel1Path
            // 
            this.txtModel1Path.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModel1Path.Location = new System.Drawing.Point(148, 13);
            this.txtModel1Path.Name = "txtModel1Path";
            this.txtModel1Path.Size = new System.Drawing.Size(984, 31);
            this.txtModel1Path.TabIndex = 1;
            // 
            // btnBrowseModel1
            // 
            this.btnBrowseModel1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseModel1.Location = new System.Drawing.Point(1138, 12);
            this.btnBrowseModel1.Name = "btnBrowseModel1";
            this.btnBrowseModel1.Size = new System.Drawing.Size(120, 34);
            this.btnBrowseModel1.TabIndex = 2;
            this.btnBrowseModel1.Text = "浏览...";
            this.btnBrowseModel1.UseVisualStyleBackColor = true;
            this.btnBrowseModel1.Click += new System.EventHandler(this.btnBrowseModel1_Click);
            // 
            // btnLoadModel1
            // 
            this.btnLoadModel1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLoadModel1.Location = new System.Drawing.Point(1264, 12);
            this.btnLoadModel1.Name = "btnLoadModel1";
            this.btnLoadModel1.Size = new System.Drawing.Size(120, 34);
            this.btnLoadModel1.TabIndex = 3;
            this.btnLoadModel1.Text = "加载模型";
            this.btnLoadModel1.UseVisualStyleBackColor = true;
            this.btnLoadModel1.Click += new System.EventHandler(this.btnLoadModel1_Click);
            // 
            // labelModel2
            // 
            this.labelModel2.AutoSize = true;
            this.labelModel2.Location = new System.Drawing.Point(12, 55);
            this.labelModel2.Name = "labelModel2";
            this.labelModel2.Size = new System.Drawing.Size(130, 24);
            this.labelModel2.TabIndex = 4;
            this.labelModel2.Text = "模型2路径(识别)";
            // 
            // txtModel2Path
            // 
            this.txtModel2Path.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModel2Path.Location = new System.Drawing.Point(148, 51);
            this.txtModel2Path.Name = "txtModel2Path";
            this.txtModel2Path.Size = new System.Drawing.Size(984, 31);
            this.txtModel2Path.TabIndex = 5;
            // 
            // btnBrowseModel2
            // 
            this.btnBrowseModel2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseModel2.Location = new System.Drawing.Point(1138, 50);
            this.btnBrowseModel2.Name = "btnBrowseModel2";
            this.btnBrowseModel2.Size = new System.Drawing.Size(120, 34);
            this.btnBrowseModel2.TabIndex = 6;
            this.btnBrowseModel2.Text = "浏览...";
            this.btnBrowseModel2.UseVisualStyleBackColor = true;
            this.btnBrowseModel2.Click += new System.EventHandler(this.btnBrowseModel2_Click);
            // 
            // btnLoadModel2
            // 
            this.btnLoadModel2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLoadModel2.Location = new System.Drawing.Point(1264, 50);
            this.btnLoadModel2.Name = "btnLoadModel2";
            this.btnLoadModel2.Size = new System.Drawing.Size(120, 34);
            this.btnLoadModel2.TabIndex = 7;
            this.btnLoadModel2.Text = "加载模型";
            this.btnLoadModel2.UseVisualStyleBackColor = true;
            this.btnLoadModel2.Click += new System.EventHandler(this.btnLoadModel2_Click);
            // 
            // labelImage
            // 
            this.labelImage.AutoSize = true;
            this.labelImage.Location = new System.Drawing.Point(12, 93);
            this.labelImage.Name = "labelImage";
            this.labelImage.Size = new System.Drawing.Size(82, 24);
            this.labelImage.TabIndex = 8;
            this.labelImage.Text = "图片路径";
            // 
            // txtImagePath
            // 
            this.txtImagePath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtImagePath.Location = new System.Drawing.Point(148, 89);
            this.txtImagePath.Name = "txtImagePath";
            this.txtImagePath.Size = new System.Drawing.Size(782, 31);
            this.txtImagePath.TabIndex = 9;
            // 
            // btnBrowseImage
            // 
            this.btnBrowseImage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseImage.Location = new System.Drawing.Point(936, 88);
            this.btnBrowseImage.Name = "btnBrowseImage";
            this.btnBrowseImage.Size = new System.Drawing.Size(120, 34);
            this.btnBrowseImage.TabIndex = 10;
            this.btnBrowseImage.Text = "浏览...";
            this.btnBrowseImage.UseVisualStyleBackColor = true;
            this.btnBrowseImage.Click += new System.EventHandler(this.btnBrowseImage_Click);
            // 
            // btnInfer
            // 
            this.btnInfer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnInfer.Location = new System.Drawing.Point(1062, 88);
            this.btnInfer.Name = "btnInfer";
            this.btnInfer.Size = new System.Drawing.Size(120, 34);
            this.btnInfer.TabIndex = 11;
            this.btnInfer.Text = "执行推理";
            this.btnInfer.UseVisualStyleBackColor = true;
            this.btnInfer.Click += new System.EventHandler(this.btnInfer_Click);
            // 
            // btnSpeedTest
            // 
            this.btnSpeedTest.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSpeedTest.Location = new System.Drawing.Point(1188, 88);
            this.btnSpeedTest.Name = "btnSpeedTest";
            this.btnSpeedTest.Size = new System.Drawing.Size(196, 34);
            this.btnSpeedTest.TabIndex = 20;
            this.btnSpeedTest.Text = "速度测试";
            this.btnSpeedTest.UseVisualStyleBackColor = true;
            this.btnSpeedTest.Click += new System.EventHandler(this.btnSpeedTest_Click);
            // 
            // labelFixedCrop
            // 
            this.labelFixedCrop.AutoSize = true;
            this.labelFixedCrop.Location = new System.Drawing.Point(12, 133);
            this.labelFixedCrop.Name = "labelFixedCrop";
            this.labelFixedCrop.Size = new System.Drawing.Size(177, 24);
            this.labelFixedCrop.TabIndex = 12;
            this.labelFixedCrop.Text = "固定裁图大小: 128 x 192";
            // 
            // labelModel2Threads
            // 
            this.labelModel2Threads.AutoSize = true;
            this.labelModel2Threads.Location = new System.Drawing.Point(260, 133);
            this.labelModel2Threads.Name = "labelModel2Threads";
            this.labelModel2Threads.Size = new System.Drawing.Size(154, 24);
            this.labelModel2Threads.TabIndex = 13;
            this.labelModel2Threads.Text = "模型2线程数(1-32)";
            // 
            // numModel2Threads
            // 
            this.numModel2Threads.Location = new System.Drawing.Point(420, 129);
            this.numModel2Threads.Maximum = new decimal(new int[] {
            32,
            0,
            0,
            0});
            this.numModel2Threads.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numModel2Threads.Name = "numModel2Threads";
            this.numModel2Threads.Size = new System.Drawing.Size(96, 31);
            this.numModel2Threads.TabIndex = 14;
            this.numModel2Threads.Value = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.numModel2Threads.ValueChanged += new System.EventHandler(this.numModel2Threads_ValueChanged);
            // 
            // btnReleaseModels
            // 
            this.btnReleaseModels.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnReleaseModels.Location = new System.Drawing.Point(1062, 128);
            this.btnReleaseModels.Name = "btnReleaseModels";
            this.btnReleaseModels.Size = new System.Drawing.Size(322, 34);
            this.btnReleaseModels.TabIndex = 15;
            this.btnReleaseModels.Text = "释放模型";
            this.btnReleaseModels.UseVisualStyleBackColor = true;
            this.btnReleaseModels.Click += new System.EventHandler(this.btnReleaseModels_Click);
            // 
            // progressBarInference
            // 
            this.progressBarInference.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBarInference.Location = new System.Drawing.Point(148, 168);
            this.progressBarInference.Name = "progressBarInference";
            this.progressBarInference.Size = new System.Drawing.Size(1124, 24);
            this.progressBarInference.TabIndex = 16;
            // 
            // lblInferenceProgress
            // 
            this.lblInferenceProgress.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblInferenceProgress.AutoSize = true;
            this.lblInferenceProgress.Location = new System.Drawing.Point(1280, 169);
            this.lblInferenceProgress.Name = "lblInferenceProgress";
            this.lblInferenceProgress.Size = new System.Drawing.Size(70, 24);
            this.lblInferenceProgress.TabIndex = 17;
            this.lblInferenceProgress.Text = "0% 空闲";
            // 
            // richTextBox1
            // 
            this.richTextBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)));
            this.richTextBox1.Location = new System.Drawing.Point(12, 198);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.ReadOnly = true;
            this.richTextBox1.Size = new System.Drawing.Size(432, 712);
            this.richTextBox1.TabIndex = 18;
            this.richTextBox1.Text = "";
            // 
            // imagePanel1
            // 
            this.imagePanel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.imagePanel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.imagePanel1.image = null;
            this.imagePanel1.Location = new System.Drawing.Point(450, 198);
            this.imagePanel1.MaxScale = 100F;
            this.imagePanel1.MinScale = 0.5F;
            this.imagePanel1.Name = "imagePanel1";
            this.imagePanel1.ShowStatusText = false;
            this.imagePanel1.ShowVisualization = true;
            this.imagePanel1.Size = new System.Drawing.Size(934, 712);
            this.imagePanel1.TabIndex = 19;
            this.imagePanel1.TabStop = true;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(11F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1396, 922);
            this.Controls.Add(this.imagePanel1);
            this.Controls.Add(this.richTextBox1);
            this.Controls.Add(this.lblInferenceProgress);
            this.Controls.Add(this.progressBarInference);
            this.Controls.Add(this.btnReleaseModels);
            this.Controls.Add(this.numModel2Threads);
            this.Controls.Add(this.labelModel2Threads);
            this.Controls.Add(this.labelFixedCrop);
            this.Controls.Add(this.btnSpeedTest);
            this.Controls.Add(this.btnInfer);
            this.Controls.Add(this.btnBrowseImage);
            this.Controls.Add(this.txtImagePath);
            this.Controls.Add(this.labelImage);
            this.Controls.Add(this.btnLoadModel2);
            this.Controls.Add(this.btnBrowseModel2);
            this.Controls.Add(this.txtModel2Path);
            this.Controls.Add(this.labelModel2);
            this.Controls.Add(this.btnLoadModel1);
            this.Controls.Add(this.btnBrowseModel1);
            this.Controls.Add(this.txtModel1Path);
            this.Controls.Add(this.labelModel1);
            this.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.MinimumSize = new System.Drawing.Size(1200, 900);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "C# 测试程序3";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numModel2Threads)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label labelModel1;
        private System.Windows.Forms.TextBox txtModel1Path;
        private System.Windows.Forms.Button btnBrowseModel1;
        private System.Windows.Forms.Button btnLoadModel1;
        private System.Windows.Forms.Label labelModel2;
        private System.Windows.Forms.TextBox txtModel2Path;
        private System.Windows.Forms.Button btnBrowseModel2;
        private System.Windows.Forms.Button btnLoadModel2;
        private System.Windows.Forms.Label labelImage;
        private System.Windows.Forms.TextBox txtImagePath;
        private System.Windows.Forms.Button btnBrowseImage;
        private System.Windows.Forms.Button btnInfer;
        private System.Windows.Forms.Button btnSpeedTest;
        private System.Windows.Forms.Label labelFixedCrop;
        private System.Windows.Forms.Label labelModel2Threads;
        private System.Windows.Forms.NumericUpDown numModel2Threads;
        private System.Windows.Forms.Button btnReleaseModels;
        private System.Windows.Forms.ProgressBar progressBarInference;
        private System.Windows.Forms.Label lblInferenceProgress;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private DLCV.ImageViewer imagePanel1;
    }
}
