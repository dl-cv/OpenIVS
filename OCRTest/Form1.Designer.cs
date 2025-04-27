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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.btnLoadDetectModel = new System.Windows.Forms.Button();
            this.btnLoadRecognizeModel = new System.Windows.Forms.Button();
            this.btnOpenImage = new System.Windows.Forms.Button();
            this.btnInfer = new System.Windows.Forms.Button();
            this.btnFreeModel = new System.Windows.Forms.Button();
            this.comboBoxDevices = new System.Windows.Forms.ComboBox();
            this.labelDevice = new System.Windows.Forms.Label();
            this.imageViewer = new DLCV.ImageViewer();
            this.richTextBoxResult = new System.Windows.Forms.RichTextBox();
            this.labelDetectModel = new System.Windows.Forms.Label();
            this.labelRecognizeModel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnLoadDetectModel
            // 
            this.btnLoadDetectModel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnLoadDetectModel.Location = new System.Drawing.Point(14, 19);
            this.btnLoadDetectModel.Margin = new System.Windows.Forms.Padding(4);
            this.btnLoadDetectModel.Name = "btnLoadDetectModel";
            this.btnLoadDetectModel.Size = new System.Drawing.Size(140, 40);
            this.btnLoadDetectModel.TabIndex = 1;
            this.btnLoadDetectModel.Text = "加载检测模型";
            this.btnLoadDetectModel.UseVisualStyleBackColor = true;
            this.btnLoadDetectModel.Click += new System.EventHandler(this.btnLoadDetectModel_Click);
            // 
            // btnLoadRecognizeModel
            // 
            this.btnLoadRecognizeModel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnLoadRecognizeModel.Location = new System.Drawing.Point(14, 70);
            this.btnLoadRecognizeModel.Margin = new System.Windows.Forms.Padding(4);
            this.btnLoadRecognizeModel.Name = "btnLoadRecognizeModel";
            this.btnLoadRecognizeModel.Size = new System.Drawing.Size(140, 40);
            this.btnLoadRecognizeModel.TabIndex = 2;
            this.btnLoadRecognizeModel.Text = "加载识别模型";
            this.btnLoadRecognizeModel.UseVisualStyleBackColor = true;
            this.btnLoadRecognizeModel.Click += new System.EventHandler(this.btnLoadRecognizeModel_Click);
            // 
            // btnOpenImage
            // 
            this.btnOpenImage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpenImage.Location = new System.Drawing.Point(648, 19);
            this.btnOpenImage.Name = "btnOpenImage";
            this.btnOpenImage.Size = new System.Drawing.Size(140, 40);
            this.btnOpenImage.TabIndex = 3;
            this.btnOpenImage.Text = "打开图片";
            this.btnOpenImage.UseVisualStyleBackColor = true;
            this.btnOpenImage.Click += new System.EventHandler(this.btnOpenImage_Click);
            // 
            // btnInfer
            // 
            this.btnInfer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnInfer.Location = new System.Drawing.Point(648, 70);
            this.btnInfer.Name = "btnInfer";
            this.btnInfer.Size = new System.Drawing.Size(140, 40);
            this.btnInfer.TabIndex = 4;
            this.btnInfer.Text = "OCR推理";
            this.btnInfer.UseVisualStyleBackColor = true;
            this.btnInfer.Click += new System.EventHandler(this.btnInfer_Click);
            // 
            // btnFreeModel
            // 
            this.btnFreeModel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnFreeModel.Location = new System.Drawing.Point(14, 121);
            this.btnFreeModel.Margin = new System.Windows.Forms.Padding(4);
            this.btnFreeModel.Name = "btnFreeModel";
            this.btnFreeModel.Size = new System.Drawing.Size(140, 40);
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
            this.comboBoxDevices.Location = new System.Drawing.Point(249, 24);
            this.comboBoxDevices.Name = "comboBoxDevices";
            this.comboBoxDevices.Size = new System.Drawing.Size(393, 32);
            this.comboBoxDevices.TabIndex = 12;
            // 
            // labelDevice
            // 
            this.labelDevice.AutoSize = true;
            this.labelDevice.Location = new System.Drawing.Point(175, 27);
            this.labelDevice.Name = "labelDevice";
            this.labelDevice.Size = new System.Drawing.Size(68, 24);
            this.labelDevice.TabIndex = 13;
            this.labelDevice.Text = "GPU：";
            // 
            // imageViewer
            // 
            this.imageViewer.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.imageViewer.Location = new System.Drawing.Point(14, 169);
            this.imageViewer.MaxScale = 100F;
            this.imageViewer.MinScale = 0.5F;
            this.imageViewer.Name = "imageViewer";
            this.imageViewer.ShowStatusText = false;
            this.imageViewer.Size = new System.Drawing.Size(513, 269);
            this.imageViewer.TabIndex = 15;
            // 
            // richTextBoxResult
            // 
            this.richTextBoxResult.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.richTextBoxResult.Location = new System.Drawing.Point(533, 169);
            this.richTextBoxResult.Name = "richTextBoxResult";
            this.richTextBoxResult.Size = new System.Drawing.Size(255, 269);
            this.richTextBoxResult.TabIndex = 16;
            this.richTextBoxResult.Text = "";
            // 
            // labelDetectModel
            // 
            this.labelDetectModel.AutoSize = true;
            this.labelDetectModel.Location = new System.Drawing.Point(175, 78);
            this.labelDetectModel.Name = "labelDetectModel";
            this.labelDetectModel.Size = new System.Drawing.Size(154, 24);
            this.labelDetectModel.TabIndex = 18;
            this.labelDetectModel.Text = "检测模型：未加载";
            // 
            // labelRecognizeModel
            // 
            this.labelRecognizeModel.AutoSize = true;
            this.labelRecognizeModel.Location = new System.Drawing.Point(350, 78);
            this.labelRecognizeModel.Name = "labelRecognizeModel";
            this.labelRecognizeModel.Size = new System.Drawing.Size(154, 24);
            this.labelRecognizeModel.TabIndex = 19;
            this.labelRecognizeModel.Text = "识别模型：未加载";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(11F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.labelRecognizeModel);
            this.Controls.Add(this.labelDetectModel);
            this.Controls.Add(this.richTextBoxResult);
            this.Controls.Add(this.imageViewer);
            this.Controls.Add(this.labelDevice);
            this.Controls.Add(this.comboBoxDevices);
            this.Controls.Add(this.btnFreeModel);
            this.Controls.Add(this.btnInfer);
            this.Controls.Add(this.btnOpenImage);
            this.Controls.Add(this.btnLoadRecognizeModel);
            this.Controls.Add(this.btnLoadDetectModel);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
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
        private System.Windows.Forms.Button btnInfer;
        private System.Windows.Forms.Button btnFreeModel;
        private System.Windows.Forms.ComboBox comboBoxDevices;
        private System.Windows.Forms.Label labelDevice;
        private DLCV.ImageViewer imageViewer;
        private System.Windows.Forms.RichTextBox richTextBoxResult;
        private System.Windows.Forms.Label labelDetectModel;
        private System.Windows.Forms.Label labelRecognizeModel;
    }
}

