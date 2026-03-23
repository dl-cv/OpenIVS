namespace DlcvDemo2
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
            this.labelWindowWidth = new System.Windows.Forms.Label();
            this.numWindowWidth = new System.Windows.Forms.NumericUpDown();
            this.labelWindowHeight = new System.Windows.Forms.Label();
            this.numWindowHeight = new System.Windows.Forms.NumericUpDown();
            this.labelOverlapX = new System.Windows.Forms.Label();
            this.numOverlapX = new System.Windows.Forms.NumericUpDown();
            this.labelOverlapY = new System.Windows.Forms.Label();
            this.numOverlapY = new System.Windows.Forms.NumericUpDown();
            this.labelSpeedRounds = new System.Windows.Forms.Label();
            this.numSpeedRounds = new System.Windows.Forms.NumericUpDown();
            this.btnReleaseModels = new System.Windows.Forms.Button();
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            this.imagePanel1 = new DLCV.ImageViewer();
            ((System.ComponentModel.ISupportInitialize)(this.numWindowWidth)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numWindowHeight)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numOverlapX)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numOverlapY)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSpeedRounds)).BeginInit();
            this.SuspendLayout();
            // 
            // labelModel1
            // 
            this.labelModel1.AutoSize = true;
            this.labelModel1.Location = new System.Drawing.Point(12, 17);
            this.labelModel1.Name = "labelModel1";
            this.labelModel1.Size = new System.Drawing.Size(84, 24);
            this.labelModel1.TabIndex = 0;
            this.labelModel1.Text = "模型1路径";
            // 
            // txtModel1Path
            // 
            this.txtModel1Path.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModel1Path.Location = new System.Drawing.Point(102, 13);
            this.txtModel1Path.Name = "txtModel1Path";
            this.txtModel1Path.Size = new System.Drawing.Size(804, 31);
            this.txtModel1Path.TabIndex = 1;
            // 
            // btnBrowseModel1
            // 
            this.btnBrowseModel1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseModel1.Location = new System.Drawing.Point(912, 12);
            this.btnBrowseModel1.Name = "btnBrowseModel1";
            this.btnBrowseModel1.Size = new System.Drawing.Size(95, 34);
            this.btnBrowseModel1.TabIndex = 2;
            this.btnBrowseModel1.Text = "浏览...";
            this.btnBrowseModel1.UseVisualStyleBackColor = true;
            this.btnBrowseModel1.Click += new System.EventHandler(this.btnBrowseModel1_Click);
            // 
            // btnLoadModel1
            // 
            this.btnLoadModel1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLoadModel1.Location = new System.Drawing.Point(1013, 12);
            this.btnLoadModel1.Name = "btnLoadModel1";
            this.btnLoadModel1.Size = new System.Drawing.Size(110, 34);
            this.btnLoadModel1.TabIndex = 3;
            this.btnLoadModel1.Text = "加载模型1";
            this.btnLoadModel1.UseVisualStyleBackColor = true;
            this.btnLoadModel1.Click += new System.EventHandler(this.btnLoadModel1_Click);
            // 
            // labelModel2
            // 
            this.labelModel2.AutoSize = true;
            this.labelModel2.Location = new System.Drawing.Point(12, 55);
            this.labelModel2.Name = "labelModel2";
            this.labelModel2.Size = new System.Drawing.Size(84, 24);
            this.labelModel2.TabIndex = 4;
            this.labelModel2.Text = "模型2路径";
            // 
            // txtModel2Path
            // 
            this.txtModel2Path.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModel2Path.Location = new System.Drawing.Point(102, 51);
            this.txtModel2Path.Name = "txtModel2Path";
            this.txtModel2Path.Size = new System.Drawing.Size(804, 31);
            this.txtModel2Path.TabIndex = 5;
            // 
            // btnBrowseModel2
            // 
            this.btnBrowseModel2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseModel2.Location = new System.Drawing.Point(912, 50);
            this.btnBrowseModel2.Name = "btnBrowseModel2";
            this.btnBrowseModel2.Size = new System.Drawing.Size(95, 34);
            this.btnBrowseModel2.TabIndex = 6;
            this.btnBrowseModel2.Text = "浏览...";
            this.btnBrowseModel2.UseVisualStyleBackColor = true;
            this.btnBrowseModel2.Click += new System.EventHandler(this.btnBrowseModel2_Click);
            // 
            // btnLoadModel2
            // 
            this.btnLoadModel2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLoadModel2.Location = new System.Drawing.Point(1013, 50);
            this.btnLoadModel2.Name = "btnLoadModel2";
            this.btnLoadModel2.Size = new System.Drawing.Size(110, 34);
            this.btnLoadModel2.TabIndex = 7;
            this.btnLoadModel2.Text = "加载模型2";
            this.btnLoadModel2.UseVisualStyleBackColor = true;
            this.btnLoadModel2.Click += new System.EventHandler(this.btnLoadModel2_Click);
            // 
            // labelImage
            // 
            this.labelImage.AutoSize = true;
            this.labelImage.Location = new System.Drawing.Point(12, 93);
            this.labelImage.Name = "labelImage";
            this.labelImage.Size = new System.Drawing.Size(84, 24);
            this.labelImage.TabIndex = 8;
            this.labelImage.Text = "图片路径";
            // 
            // txtImagePath
            // 
            this.txtImagePath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtImagePath.Location = new System.Drawing.Point(102, 89);
            this.txtImagePath.Name = "txtImagePath";
            this.txtImagePath.Size = new System.Drawing.Size(804, 31);
            this.txtImagePath.TabIndex = 9;
            // 
            // btnBrowseImage
            // 
            this.btnBrowseImage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseImage.Location = new System.Drawing.Point(912, 88);
            this.btnBrowseImage.Name = "btnBrowseImage";
            this.btnBrowseImage.Size = new System.Drawing.Size(95, 34);
            this.btnBrowseImage.TabIndex = 10;
            this.btnBrowseImage.Text = "浏览...";
            this.btnBrowseImage.UseVisualStyleBackColor = true;
            this.btnBrowseImage.Click += new System.EventHandler(this.btnBrowseImage_Click);
            // 
            // btnInfer
            // 
            this.btnInfer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnInfer.Location = new System.Drawing.Point(1013, 88);
            this.btnInfer.Name = "btnInfer";
            this.btnInfer.Size = new System.Drawing.Size(110, 34);
            this.btnInfer.TabIndex = 11;
            this.btnInfer.Text = "执行推理";
            this.btnInfer.UseVisualStyleBackColor = true;
            this.btnInfer.Click += new System.EventHandler(this.btnInfer_Click);
            // 
            // btnSpeedTest
            // 
            this.btnSpeedTest.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSpeedTest.Location = new System.Drawing.Point(1129, 88);
            this.btnSpeedTest.Name = "btnSpeedTest";
            this.btnSpeedTest.Size = new System.Drawing.Size(95, 34);
            this.btnSpeedTest.TabIndex = 12;
            this.btnSpeedTest.Text = "测速";
            this.btnSpeedTest.UseVisualStyleBackColor = true;
            this.btnSpeedTest.Click += new System.EventHandler(this.btnSpeedTest_Click);
            // 
            // labelWindowWidth
            // 
            this.labelWindowWidth.AutoSize = true;
            this.labelWindowWidth.Location = new System.Drawing.Point(12, 133);
            this.labelWindowWidth.Name = "labelWindowWidth";
            this.labelWindowWidth.Size = new System.Drawing.Size(64, 24);
            this.labelWindowWidth.TabIndex = 13;
            this.labelWindowWidth.Text = "窗口宽";
            // 
            // numWindowWidth
            // 
            this.numWindowWidth.Location = new System.Drawing.Point(82, 129);
            this.numWindowWidth.Maximum = new decimal(new int[] {
            30000,
            0,
            0,
            0});
            this.numWindowWidth.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numWindowWidth.Name = "numWindowWidth";
            this.numWindowWidth.Size = new System.Drawing.Size(95, 31);
            this.numWindowWidth.TabIndex = 14;
            this.numWindowWidth.Value = new decimal(new int[] {
            2560,
            0,
            0,
            0});
            // 
            // labelWindowHeight
            // 
            this.labelWindowHeight.AutoSize = true;
            this.labelWindowHeight.Location = new System.Drawing.Point(183, 133);
            this.labelWindowHeight.Name = "labelWindowHeight";
            this.labelWindowHeight.Size = new System.Drawing.Size(64, 24);
            this.labelWindowHeight.TabIndex = 15;
            this.labelWindowHeight.Text = "窗口高";
            // 
            // numWindowHeight
            // 
            this.numWindowHeight.Location = new System.Drawing.Point(253, 129);
            this.numWindowHeight.Maximum = new decimal(new int[] {
            30000,
            0,
            0,
            0});
            this.numWindowHeight.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numWindowHeight.Name = "numWindowHeight";
            this.numWindowHeight.Size = new System.Drawing.Size(95, 31);
            this.numWindowHeight.TabIndex = 16;
            this.numWindowHeight.Value = new decimal(new int[] {
            2560,
            0,
            0,
            0});
            // 
            // labelOverlapX
            // 
            this.labelOverlapX.AutoSize = true;
            this.labelOverlapX.Location = new System.Drawing.Point(354, 133);
            this.labelOverlapX.Name = "labelOverlapX";
            this.labelOverlapX.Size = new System.Drawing.Size(82, 24);
            this.labelOverlapX.TabIndex = 17;
            this.labelOverlapX.Text = "水平重叠";
            // 
            // numOverlapX
            // 
            this.numOverlapX.Location = new System.Drawing.Point(442, 129);
            this.numOverlapX.Maximum = new decimal(new int[] {
            30000,
            0,
            0,
            0});
            this.numOverlapX.Name = "numOverlapX";
            this.numOverlapX.Size = new System.Drawing.Size(95, 31);
            this.numOverlapX.TabIndex = 18;
            this.numOverlapX.Value = new decimal(new int[] {
            1024,
            0,
            0,
            0});
            // 
            // labelOverlapY
            // 
            this.labelOverlapY.AutoSize = true;
            this.labelOverlapY.Location = new System.Drawing.Point(543, 133);
            this.labelOverlapY.Name = "labelOverlapY";
            this.labelOverlapY.Size = new System.Drawing.Size(82, 24);
            this.labelOverlapY.TabIndex = 19;
            this.labelOverlapY.Text = "垂直重叠";
            // 
            // numOverlapY
            // 
            this.numOverlapY.Location = new System.Drawing.Point(631, 129);
            this.numOverlapY.Maximum = new decimal(new int[] {
            30000,
            0,
            0,
            0});
            this.numOverlapY.Name = "numOverlapY";
            this.numOverlapY.Size = new System.Drawing.Size(95, 31);
            this.numOverlapY.TabIndex = 20;
            this.numOverlapY.Value = new decimal(new int[] {
            1024,
            0,
            0,
            0});
            // 
            // labelSpeedRounds
            // 
            this.labelSpeedRounds.AutoSize = true;
            this.labelSpeedRounds.Location = new System.Drawing.Point(732, 133);
            this.labelSpeedRounds.Name = "labelSpeedRounds";
            this.labelSpeedRounds.Size = new System.Drawing.Size(82, 24);
            this.labelSpeedRounds.TabIndex = 21;
            this.labelSpeedRounds.Text = "测速轮数";
            // 
            // numSpeedRounds
            // 
            this.numSpeedRounds.Location = new System.Drawing.Point(820, 129);
            this.numSpeedRounds.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.numSpeedRounds.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numSpeedRounds.Name = "numSpeedRounds";
            this.numSpeedRounds.Size = new System.Drawing.Size(86, 31);
            this.numSpeedRounds.TabIndex = 22;
            this.numSpeedRounds.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            // 
            // btnReleaseModels
            // 
            this.btnReleaseModels.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnReleaseModels.Location = new System.Drawing.Point(912, 127);
            this.btnReleaseModels.Name = "btnReleaseModels";
            this.btnReleaseModels.Size = new System.Drawing.Size(212, 34);
            this.btnReleaseModels.TabIndex = 23;
            this.btnReleaseModels.Text = "释放模型";
            this.btnReleaseModels.UseVisualStyleBackColor = true;
            this.btnReleaseModels.Click += new System.EventHandler(this.btnReleaseModels_Click);
            // 
            // richTextBox1
            // 
            this.richTextBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)));
            this.richTextBox1.Location = new System.Drawing.Point(12, 168);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.ReadOnly = true;
            this.richTextBox1.Size = new System.Drawing.Size(432, 704);
            this.richTextBox1.TabIndex = 24;
            this.richTextBox1.Text = "";
            // 
            // imagePanel1
            // 
            this.imagePanel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.imagePanel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.imagePanel1.image = null;
            this.imagePanel1.Location = new System.Drawing.Point(450, 168);
            this.imagePanel1.MaxScale = 100F;
            this.imagePanel1.MinScale = 0.5F;
            this.imagePanel1.Name = "imagePanel1";
            this.imagePanel1.ShowStatusText = false;
            this.imagePanel1.ShowVisualization = true;
            this.imagePanel1.Size = new System.Drawing.Size(934, 704);
            this.imagePanel1.TabIndex = 25;
            this.imagePanel1.TabStop = true;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(11F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1396, 884);
            this.Controls.Add(this.imagePanel1);
            this.Controls.Add(this.richTextBox1);
            this.Controls.Add(this.btnReleaseModels);
            this.Controls.Add(this.numSpeedRounds);
            this.Controls.Add(this.labelSpeedRounds);
            this.Controls.Add(this.numOverlapY);
            this.Controls.Add(this.labelOverlapY);
            this.Controls.Add(this.numOverlapX);
            this.Controls.Add(this.labelOverlapX);
            this.Controls.Add(this.numWindowHeight);
            this.Controls.Add(this.labelWindowHeight);
            this.Controls.Add(this.numWindowWidth);
            this.Controls.Add(this.labelWindowWidth);
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
            this.Text = "C# 测试程序2";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numWindowWidth)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numWindowHeight)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numOverlapX)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numOverlapY)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSpeedRounds)).EndInit();
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
        private System.Windows.Forms.Label labelWindowWidth;
        private System.Windows.Forms.NumericUpDown numWindowWidth;
        private System.Windows.Forms.Label labelWindowHeight;
        private System.Windows.Forms.NumericUpDown numWindowHeight;
        private System.Windows.Forms.Label labelOverlapX;
        private System.Windows.Forms.NumericUpDown numOverlapX;
        private System.Windows.Forms.Label labelOverlapY;
        private System.Windows.Forms.NumericUpDown numOverlapY;
        private System.Windows.Forms.Label labelSpeedRounds;
        private System.Windows.Forms.NumericUpDown numSpeedRounds;
        private System.Windows.Forms.Button btnReleaseModels;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private DLCV.ImageViewer imagePanel1;
    }
}
