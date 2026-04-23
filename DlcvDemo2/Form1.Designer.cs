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
            this.labelExtractModel = new System.Windows.Forms.Label();
            this.txtExtractModelPath = new System.Windows.Forms.TextBox();
            this.btnBrowseExtractModel = new System.Windows.Forms.Button();
            this.btnLoadExtractModel = new System.Windows.Forms.Button();
            this.labelComponentModel = new System.Windows.Forms.Label();
            this.txtComponentModelPath = new System.Windows.Forms.TextBox();
            this.btnBrowseComponentModel = new System.Windows.Forms.Button();
            this.btnLoadComponentModel = new System.Windows.Forms.Button();
            this.labelIcModel = new System.Windows.Forms.Label();
            this.txtIcModelPath = new System.Windows.Forms.TextBox();
            this.btnBrowseIcModel = new System.Windows.Forms.Button();
            this.btnLoadIcModel = new System.Windows.Forms.Button();
            this.labelImage = new System.Windows.Forms.Label();
            this.txtImagePath = new System.Windows.Forms.TextBox();
            this.btnBrowseImage = new System.Windows.Forms.Button();
            this.btnInfer = new System.Windows.Forms.Button();
            this.labelWindowWidth = new System.Windows.Forms.Label();
            this.numWindowWidth = new System.Windows.Forms.NumericUpDown();
            this.labelWindowHeight = new System.Windows.Forms.Label();
            this.numWindowHeight = new System.Windows.Forms.NumericUpDown();
            this.labelOverlapX = new System.Windows.Forms.Label();
            this.numOverlapX = new System.Windows.Forms.NumericUpDown();
            this.labelOverlapY = new System.Windows.Forms.Label();
            this.numOverlapY = new System.Windows.Forms.NumericUpDown();
            this.btnReleaseModels = new System.Windows.Forms.Button();
            this.progressBarInference = new System.Windows.Forms.ProgressBar();
            this.lblInferenceProgress = new System.Windows.Forms.Label();
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            this.imagePanel1 = new DLCV.ImageViewer();
            ((System.ComponentModel.ISupportInitialize)(this.numWindowWidth)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numWindowHeight)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numOverlapX)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numOverlapY)).BeginInit();
            this.SuspendLayout();
            // 
            // labelExtractModel
            // 
            this.labelExtractModel.AutoSize = true;
            this.labelExtractModel.Location = new System.Drawing.Point(12, 17);
            this.labelExtractModel.Name = "labelExtractModel";
            this.labelExtractModel.Size = new System.Drawing.Size(118, 24);
            this.labelExtractModel.TabIndex = 0;
            this.labelExtractModel.Text = "元件提取模型";
            // 
            // txtExtractModelPath
            // 
            this.txtExtractModelPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtExtractModelPath.Location = new System.Drawing.Point(136, 13);
            this.txtExtractModelPath.Name = "txtExtractModelPath";
            this.txtExtractModelPath.Size = new System.Drawing.Size(862, 31);
            this.txtExtractModelPath.TabIndex = 1;
            // 
            // btnBrowseExtractModel
            // 
            this.btnBrowseExtractModel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseExtractModel.Location = new System.Drawing.Point(1004, 12);
            this.btnBrowseExtractModel.Name = "btnBrowseExtractModel";
            this.btnBrowseExtractModel.Size = new System.Drawing.Size(120, 34);
            this.btnBrowseExtractModel.TabIndex = 2;
            this.btnBrowseExtractModel.Text = "浏览...";
            this.btnBrowseExtractModel.UseVisualStyleBackColor = true;
            this.btnBrowseExtractModel.Click += new System.EventHandler(this.btnBrowseExtractModel_Click);
            // 
            // btnLoadExtractModel
            // 
            this.btnLoadExtractModel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLoadExtractModel.Location = new System.Drawing.Point(1130, 12);
            this.btnLoadExtractModel.Name = "btnLoadExtractModel";
            this.btnLoadExtractModel.Size = new System.Drawing.Size(120, 34);
            this.btnLoadExtractModel.TabIndex = 3;
            this.btnLoadExtractModel.Text = "加载模型";
            this.btnLoadExtractModel.UseVisualStyleBackColor = true;
            this.btnLoadExtractModel.Click += new System.EventHandler(this.btnLoadExtractModel_Click);
            // 
            // labelComponentModel
            // 
            this.labelComponentModel.AutoSize = true;
            this.labelComponentModel.Location = new System.Drawing.Point(12, 55);
            this.labelComponentModel.Name = "labelComponentModel";
            this.labelComponentModel.Size = new System.Drawing.Size(118, 24);
            this.labelComponentModel.TabIndex = 3;
            this.labelComponentModel.Text = "元件检测模型";
            // 
            // txtComponentModelPath
            // 
            this.txtComponentModelPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtComponentModelPath.Location = new System.Drawing.Point(136, 51);
            this.txtComponentModelPath.Name = "txtComponentModelPath";
            this.txtComponentModelPath.Size = new System.Drawing.Size(862, 31);
            this.txtComponentModelPath.TabIndex = 4;
            // 
            // btnBrowseComponentModel
            // 
            this.btnBrowseComponentModel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseComponentModel.Location = new System.Drawing.Point(1004, 50);
            this.btnBrowseComponentModel.Name = "btnBrowseComponentModel";
            this.btnBrowseComponentModel.Size = new System.Drawing.Size(120, 34);
            this.btnBrowseComponentModel.TabIndex = 5;
            this.btnBrowseComponentModel.Text = "浏览...";
            this.btnBrowseComponentModel.UseVisualStyleBackColor = true;
            this.btnBrowseComponentModel.Click += new System.EventHandler(this.btnBrowseComponentModel_Click);
            // 
            // btnLoadComponentModel
            // 
            this.btnLoadComponentModel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLoadComponentModel.Location = new System.Drawing.Point(1130, 50);
            this.btnLoadComponentModel.Name = "btnLoadComponentModel";
            this.btnLoadComponentModel.Size = new System.Drawing.Size(120, 34);
            this.btnLoadComponentModel.TabIndex = 6;
            this.btnLoadComponentModel.Text = "加载模型";
            this.btnLoadComponentModel.UseVisualStyleBackColor = true;
            this.btnLoadComponentModel.Click += new System.EventHandler(this.btnLoadComponentModel_Click);
            // 
            // labelIcModel
            // 
            this.labelIcModel.AutoSize = true;
            this.labelIcModel.Location = new System.Drawing.Point(12, 93);
            this.labelIcModel.Name = "labelIcModel";
            this.labelIcModel.Size = new System.Drawing.Size(99, 24);
            this.labelIcModel.TabIndex = 6;
            this.labelIcModel.Text = "IC检测模型";
            // 
            // txtIcModelPath
            // 
            this.txtIcModelPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtIcModelPath.Location = new System.Drawing.Point(136, 89);
            this.txtIcModelPath.Name = "txtIcModelPath";
            this.txtIcModelPath.Size = new System.Drawing.Size(862, 31);
            this.txtIcModelPath.TabIndex = 7;
            // 
            // btnBrowseIcModel
            // 
            this.btnBrowseIcModel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseIcModel.Location = new System.Drawing.Point(1004, 88);
            this.btnBrowseIcModel.Name = "btnBrowseIcModel";
            this.btnBrowseIcModel.Size = new System.Drawing.Size(120, 34);
            this.btnBrowseIcModel.TabIndex = 8;
            this.btnBrowseIcModel.Text = "浏览...";
            this.btnBrowseIcModel.UseVisualStyleBackColor = true;
            this.btnBrowseIcModel.Click += new System.EventHandler(this.btnBrowseIcModel_Click);
            // 
            // btnLoadIcModel
            // 
            this.btnLoadIcModel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLoadIcModel.Location = new System.Drawing.Point(1130, 88);
            this.btnLoadIcModel.Name = "btnLoadIcModel";
            this.btnLoadIcModel.Size = new System.Drawing.Size(120, 34);
            this.btnLoadIcModel.TabIndex = 9;
            this.btnLoadIcModel.Text = "加载模型";
            this.btnLoadIcModel.UseVisualStyleBackColor = true;
            this.btnLoadIcModel.Click += new System.EventHandler(this.btnLoadIcModel_Click);
            // 
            // labelImage
            // 
            this.labelImage.AutoSize = true;
            this.labelImage.Location = new System.Drawing.Point(12, 131);
            this.labelImage.Name = "labelImage";
            this.labelImage.Size = new System.Drawing.Size(82, 24);
            this.labelImage.TabIndex = 9;
            this.labelImage.Text = "图片路径";
            // 
            // txtImagePath
            // 
            this.txtImagePath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtImagePath.Location = new System.Drawing.Point(136, 127);
            this.txtImagePath.Name = "txtImagePath";
            this.txtImagePath.Size = new System.Drawing.Size(862, 31);
            this.txtImagePath.TabIndex = 10;
            // 
            // btnBrowseImage
            // 
            this.btnBrowseImage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseImage.Location = new System.Drawing.Point(1004, 124);
            this.btnBrowseImage.Name = "btnBrowseImage";
            this.btnBrowseImage.Size = new System.Drawing.Size(120, 34);
            this.btnBrowseImage.TabIndex = 11;
            this.btnBrowseImage.Text = "浏览...";
            this.btnBrowseImage.UseVisualStyleBackColor = true;
            this.btnBrowseImage.Click += new System.EventHandler(this.btnBrowseImage_Click);
            // 
            // btnInfer
            // 
            this.btnInfer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnInfer.Location = new System.Drawing.Point(1130, 126);
            this.btnInfer.Name = "btnInfer";
            this.btnInfer.Size = new System.Drawing.Size(120, 34);
            this.btnInfer.TabIndex = 12;
            this.btnInfer.Text = "执行推理";
            this.btnInfer.UseVisualStyleBackColor = true;
            this.btnInfer.Click += new System.EventHandler(this.btnInfer_Click);
            // 
            // labelWindowWidth
            // 
            this.labelWindowWidth.AutoSize = true;
            this.labelWindowWidth.Location = new System.Drawing.Point(12, 171);
            this.labelWindowWidth.Name = "labelWindowWidth";
            this.labelWindowWidth.Size = new System.Drawing.Size(64, 24);
            this.labelWindowWidth.TabIndex = 13;
            this.labelWindowWidth.Text = "窗口宽";
            // 
            // numWindowWidth
            // 
            this.numWindowWidth.Location = new System.Drawing.Point(82, 167);
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
            this.labelWindowHeight.Location = new System.Drawing.Point(183, 171);
            this.labelWindowHeight.Name = "labelWindowHeight";
            this.labelWindowHeight.Size = new System.Drawing.Size(64, 24);
            this.labelWindowHeight.TabIndex = 15;
            this.labelWindowHeight.Text = "窗口高";
            // 
            // numWindowHeight
            // 
            this.numWindowHeight.Location = new System.Drawing.Point(253, 167);
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
            this.labelOverlapX.Location = new System.Drawing.Point(354, 171);
            this.labelOverlapX.Name = "labelOverlapX";
            this.labelOverlapX.Size = new System.Drawing.Size(82, 24);
            this.labelOverlapX.TabIndex = 17;
            this.labelOverlapX.Text = "水平重叠";
            // 
            // numOverlapX
            // 
            this.numOverlapX.Location = new System.Drawing.Point(442, 167);
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
            this.labelOverlapY.Location = new System.Drawing.Point(543, 171);
            this.labelOverlapY.Name = "labelOverlapY";
            this.labelOverlapY.Size = new System.Drawing.Size(82, 24);
            this.labelOverlapY.TabIndex = 19;
            this.labelOverlapY.Text = "垂直重叠";
            // 
            // numOverlapY
            // 
            this.numOverlapY.Location = new System.Drawing.Point(631, 167);
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
            // btnReleaseModels
            // 
            this.btnReleaseModels.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnReleaseModels.Location = new System.Drawing.Point(732, 164);
            this.btnReleaseModels.Name = "btnReleaseModels";
            this.btnReleaseModels.Size = new System.Drawing.Size(120, 34);
            this.btnReleaseModels.TabIndex = 23;
            this.btnReleaseModels.Text = "释放模型";
            this.btnReleaseModels.UseVisualStyleBackColor = true;
            this.btnReleaseModels.Click += new System.EventHandler(this.btnReleaseModels_Click);
            // 
            // progressBarInference
            // 
            this.progressBarInference.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBarInference.Location = new System.Drawing.Point(136, 206);
            this.progressBarInference.Name = "progressBarInference";
            this.progressBarInference.Size = new System.Drawing.Size(770, 24);
            this.progressBarInference.TabIndex = 24;
            // 
            // lblInferenceProgress
            // 
            this.lblInferenceProgress.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblInferenceProgress.AutoSize = true;
            this.lblInferenceProgress.Location = new System.Drawing.Point(912, 207);
            this.lblInferenceProgress.Name = "lblInferenceProgress";
            this.lblInferenceProgress.Size = new System.Drawing.Size(78, 24);
            this.lblInferenceProgress.TabIndex = 25;
            this.lblInferenceProgress.Text = "0% 空闲";
            // 
            // richTextBox1
            // 
            this.richTextBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.richTextBox1.Location = new System.Drawing.Point(12, 236);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.ReadOnly = true;
            this.richTextBox1.Size = new System.Drawing.Size(432, 674);
            this.richTextBox1.TabIndex = 26;
            this.richTextBox1.Text = "";
            // 
            // imagePanel1
            // 
            this.imagePanel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.imagePanel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.imagePanel1.image = null;
            this.imagePanel1.LabelDisplayMode = DLCV.ImageViewer.LabelTextMode.CategoryAndScore;
            this.imagePanel1.LabelFontScale = 1F;
            this.imagePanel1.LabelFontScaleStep = 1.1F;
            this.imagePanel1.Location = new System.Drawing.Point(450, 236);
            this.imagePanel1.MaxLabelFontScale = 5F;
            this.imagePanel1.MaxScale = 100F;
            this.imagePanel1.MinLabelFontScale = 0.3F;
            this.imagePanel1.MinScale = 0.5F;
            this.imagePanel1.Name = "imagePanel1";
            this.imagePanel1.ShowLabelText = true;
            this.imagePanel1.ShowStatusText = false;
            this.imagePanel1.ShowVisualization = true;
            this.imagePanel1.Size = new System.Drawing.Size(934, 674);
            this.imagePanel1.TabIndex = 27;
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
            this.Controls.Add(this.numOverlapY);
            this.Controls.Add(this.labelOverlapY);
            this.Controls.Add(this.numOverlapX);
            this.Controls.Add(this.labelOverlapX);
            this.Controls.Add(this.numWindowHeight);
            this.Controls.Add(this.labelWindowHeight);
            this.Controls.Add(this.numWindowWidth);
            this.Controls.Add(this.labelWindowWidth);
            this.Controls.Add(this.btnInfer);
            this.Controls.Add(this.btnBrowseImage);
            this.Controls.Add(this.txtImagePath);
            this.Controls.Add(this.labelImage);
            this.Controls.Add(this.btnLoadIcModel);
            this.Controls.Add(this.btnBrowseIcModel);
            this.Controls.Add(this.txtIcModelPath);
            this.Controls.Add(this.labelIcModel);
            this.Controls.Add(this.btnLoadComponentModel);
            this.Controls.Add(this.btnBrowseComponentModel);
            this.Controls.Add(this.txtComponentModelPath);
            this.Controls.Add(this.labelComponentModel);
            this.Controls.Add(this.btnLoadExtractModel);
            this.Controls.Add(this.btnBrowseExtractModel);
            this.Controls.Add(this.txtExtractModelPath);
            this.Controls.Add(this.labelExtractModel);
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
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label labelExtractModel;
        private System.Windows.Forms.TextBox txtExtractModelPath;
        private System.Windows.Forms.Button btnBrowseExtractModel;
        private System.Windows.Forms.Button btnLoadExtractModel;
        private System.Windows.Forms.Label labelComponentModel;
        private System.Windows.Forms.TextBox txtComponentModelPath;
        private System.Windows.Forms.Button btnBrowseComponentModel;
        private System.Windows.Forms.Button btnLoadComponentModel;
        private System.Windows.Forms.Label labelIcModel;
        private System.Windows.Forms.TextBox txtIcModelPath;
        private System.Windows.Forms.Button btnBrowseIcModel;
        private System.Windows.Forms.Button btnLoadIcModel;
        private System.Windows.Forms.Label labelImage;
        private System.Windows.Forms.TextBox txtImagePath;
        private System.Windows.Forms.Button btnBrowseImage;
        private System.Windows.Forms.Button btnInfer;
        private System.Windows.Forms.Label labelWindowWidth;
        private System.Windows.Forms.NumericUpDown numWindowWidth;
        private System.Windows.Forms.Label labelWindowHeight;
        private System.Windows.Forms.NumericUpDown numWindowHeight;
        private System.Windows.Forms.Label labelOverlapX;
        private System.Windows.Forms.NumericUpDown numOverlapX;
        private System.Windows.Forms.Label labelOverlapY;
        private System.Windows.Forms.NumericUpDown numOverlapY;
        private System.Windows.Forms.Button btnReleaseModels;
        private System.Windows.Forms.ProgressBar progressBarInference;
        private System.Windows.Forms.Label lblInferenceProgress;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private DLCV.ImageViewer imagePanel1;
    }
}
