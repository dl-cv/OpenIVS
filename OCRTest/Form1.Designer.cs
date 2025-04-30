namespace OCRTest
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.btnLoadDetectModel = new System.Windows.Forms.Button();
            this.btnLoadRecognizeModel = new System.Windows.Forms.Button();
            this.btnOpenImage = new System.Windows.Forms.Button();
            this.btnFreeModel = new System.Windows.Forms.Button();
            this.comboBoxDevices = new System.Windows.Forms.ComboBox();
            this.labelDevice = new System.Windows.Forms.Label();
            this.imageViewer = new DLCV.ImageViewer();
            this.richTextBoxResult = new System.Windows.Forms.RichTextBox();
            this.labelDetectModel = new System.Windows.Forms.Label();
            this.labelRecognizeModel = new System.Windows.Forms.Label();
            this.btnStartStressTest = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // btnLoadDetectModel
            // 
            this.btnLoadDetectModel.Location = new System.Drawing.Point(21, 12);
            this.btnLoadDetectModel.Name = "btnLoadDetectModel";
            this.btnLoadDetectModel.Size = new System.Drawing.Size(127, 32);
            this.btnLoadDetectModel.TabIndex = 1;
            this.btnLoadDetectModel.Text = "加载检测模型";
            this.btnLoadDetectModel.UseVisualStyleBackColor = true;
            this.btnLoadDetectModel.Click += new System.EventHandler(this.btnLoadDetectModel_Click);
            // 
            // btnLoadRecognizeModel
            // 
            this.btnLoadRecognizeModel.Location = new System.Drawing.Point(21, 52);
            this.btnLoadRecognizeModel.Name = "btnLoadRecognizeModel";
            this.btnLoadRecognizeModel.Size = new System.Drawing.Size(127, 33);
            this.btnLoadRecognizeModel.TabIndex = 2;
            this.btnLoadRecognizeModel.Text = "加载OCR模型";
            this.btnLoadRecognizeModel.UseVisualStyleBackColor = true;
            this.btnLoadRecognizeModel.Click += new System.EventHandler(this.btnLoadRecognizeModel_Click);
            // 
            // btnOpenImage
            // 
            this.btnOpenImage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpenImage.Location = new System.Drawing.Point(21, 93);
            this.btnOpenImage.Margin = new System.Windows.Forms.Padding(2);
            this.btnOpenImage.Name = "btnOpenImage";
            this.btnOpenImage.Size = new System.Drawing.Size(180, 32);
            this.btnOpenImage.TabIndex = 3;
            this.btnOpenImage.Text = "打开图片并推理";
            this.btnOpenImage.UseVisualStyleBackColor = true;
            this.btnOpenImage.Click += new System.EventHandler(this.btnOpenImage_Click);
            // 
            // btnFreeModel
            // 
            this.btnFreeModel.Location = new System.Drawing.Point(1101, 28);
            this.btnFreeModel.Name = "btnFreeModel";
            this.btnFreeModel.Size = new System.Drawing.Size(127, 30);
            this.btnFreeModel.TabIndex = 8;
            this.btnFreeModel.Text = "释放模型";
            this.btnFreeModel.UseVisualStyleBackColor = true;
            this.btnFreeModel.Click += new System.EventHandler(this.btnFreeModel_Click);
            // 
            // comboBoxDevices
            // 
            this.comboBoxDevices.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.comboBoxDevices.FormattingEnabled = true;
            this.comboBoxDevices.Location = new System.Drawing.Point(505, 137);
            this.comboBoxDevices.Margin = new System.Windows.Forms.Padding(2);
            this.comboBoxDevices.Name = "comboBoxDevices";
            this.comboBoxDevices.Size = new System.Drawing.Size(723, 26);
            this.comboBoxDevices.TabIndex = 12;
            // 
            // labelDevice
            // 
            this.labelDevice.AutoSize = true;
            this.labelDevice.Location = new System.Drawing.Point(435, 140);
            this.labelDevice.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.labelDevice.Name = "labelDevice";
            this.labelDevice.Size = new System.Drawing.Size(53, 18);
            this.labelDevice.TabIndex = 13;
            this.labelDevice.Text = "GPU：";
            // 
            // imageViewer
            // 
            this.imageViewer.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.imageViewer.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.imageViewer.image = null;
            this.imageViewer.Location = new System.Drawing.Point(274, 183);
            this.imageViewer.Margin = new System.Windows.Forms.Padding(2);
            this.imageViewer.MaxScale = 100F;
            this.imageViewer.MinScale = 0.5F;
            this.imageViewer.Name = "imageViewer";
            this.imageViewer.ShowStatusText = false;
            this.imageViewer.Size = new System.Drawing.Size(954, 883);
            this.imageViewer.TabIndex = 15;
            // 
            // richTextBoxResult
            // 
            this.richTextBoxResult.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.richTextBoxResult.Location = new System.Drawing.Point(19, 182);
            this.richTextBoxResult.Margin = new System.Windows.Forms.Padding(2);
            this.richTextBoxResult.Name = "richTextBoxResult";
            this.richTextBoxResult.Size = new System.Drawing.Size(248, 884);
            this.richTextBoxResult.TabIndex = 16;
            this.richTextBoxResult.Text = "";
            // 
            // labelDetectModel
            // 
            this.labelDetectModel.AutoSize = true;
            this.labelDetectModel.Location = new System.Drawing.Point(153, 19);
            this.labelDetectModel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.labelDetectModel.Name = "labelDetectModel";
            this.labelDetectModel.Size = new System.Drawing.Size(152, 18);
            this.labelDetectModel.TabIndex = 18;
            this.labelDetectModel.Text = "检测模型：未加载";
            // 
            // labelRecognizeModel
            // 
            this.labelRecognizeModel.AutoSize = true;
            this.labelRecognizeModel.Location = new System.Drawing.Point(162, 59);
            this.labelRecognizeModel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.labelRecognizeModel.Name = "labelRecognizeModel";
            this.labelRecognizeModel.Size = new System.Drawing.Size(143, 18);
            this.labelRecognizeModel.TabIndex = 19;
            this.labelRecognizeModel.Text = "OCR模型：未加载";
            // 
            // btnStartStressTest
            // 
            this.btnStartStressTest.Location = new System.Drawing.Point(21, 137);
            this.btnStartStressTest.Name = "btnStartStressTest";
            this.btnStartStressTest.Size = new System.Drawing.Size(180, 40);
            this.btnStartStressTest.TabIndex = 5;
            this.btnStartStressTest.Text = "开始压力测试";
            this.btnStartStressTest.UseVisualStyleBackColor = true;
            this.btnStartStressTest.Click += new System.EventHandler(this.btnStartStressTest_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1239, 1077);
            this.Controls.Add(this.richTextBoxResult);
            this.Controls.Add(this.labelRecognizeModel);
            this.Controls.Add(this.labelDetectModel);
            this.Controls.Add(this.imageViewer);
            this.Controls.Add(this.labelDevice);
            this.Controls.Add(this.comboBoxDevices);
            this.Controls.Add(this.btnFreeModel);
            this.Controls.Add(this.btnOpenImage);
            this.Controls.Add(this.btnLoadRecognizeModel);
            this.Controls.Add(this.btnLoadDetectModel);
            this.Controls.Add(this.btnStartStressTest);
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "Form1";
            this.Text = "OCR测试程序";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnLoadDetectModel;
        private System.Windows.Forms.Button btnLoadRecognizeModel;
        private System.Windows.Forms.Button btnOpenImage;
        private System.Windows.Forms.Button btnFreeModel;
        private System.Windows.Forms.ComboBox comboBoxDevices;
        private System.Windows.Forms.Label labelDevice;
        private DLCV.ImageViewer imageViewer;
        private System.Windows.Forms.RichTextBox richTextBoxResult;
        private System.Windows.Forms.Label labelDetectModel;
        private System.Windows.Forms.Label labelRecognizeModel;
        private System.Windows.Forms.Button btnStartStressTest;
    }
}

