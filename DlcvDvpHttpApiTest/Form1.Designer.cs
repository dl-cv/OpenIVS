namespace DlcvDvpHttpApiTest
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
            this.groupBoxModel = new System.Windows.Forms.GroupBox();
            this.btnSelectModel = new System.Windows.Forms.Button();
            this.txtModelPath = new System.Windows.Forms.TextBox();
            this.lblModelPath = new System.Windows.Forms.Label();
            this.txtServerUrl = new System.Windows.Forms.TextBox();
            this.lblServerUrl = new System.Windows.Forms.Label();
            this.btnLoadModel = new System.Windows.Forms.Button();
            this.btnGetModelInfo = new System.Windows.Forms.Button();
            this.groupBoxImage = new System.Windows.Forms.GroupBox();
            this.btnSelectImage = new System.Windows.Forms.Button();
            this.txtImagePath = new System.Windows.Forms.TextBox();
            this.lblImagePath = new System.Windows.Forms.Label();
            this.btnInfer = new System.Windows.Forms.Button();
            this.groupBoxResults = new System.Windows.Forms.GroupBox();
            this.txtResults = new System.Windows.Forms.TextBox();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.toolStripStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.btnFreeModel = new System.Windows.Forms.Button();
            this.groupBoxModel.SuspendLayout();
            this.groupBoxImage.SuspendLayout();
            this.groupBoxResults.SuspendLayout();
            this.statusStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBoxModel
            // 
            this.groupBoxModel.Controls.Add(this.btnFreeModel);
            this.groupBoxModel.Controls.Add(this.btnGetModelInfo);
            this.groupBoxModel.Controls.Add(this.btnLoadModel);
            this.groupBoxModel.Controls.Add(this.txtServerUrl);
            this.groupBoxModel.Controls.Add(this.lblServerUrl);
            this.groupBoxModel.Controls.Add(this.btnSelectModel);
            this.groupBoxModel.Controls.Add(this.txtModelPath);
            this.groupBoxModel.Controls.Add(this.lblModelPath);
            this.groupBoxModel.Location = new System.Drawing.Point(12, 12);
            this.groupBoxModel.Name = "groupBoxModel";
            this.groupBoxModel.Size = new System.Drawing.Size(760, 120);
            this.groupBoxModel.TabIndex = 0;
            this.groupBoxModel.TabStop = false;
            this.groupBoxModel.Text = "模型配置";
            // 
            // btnSelectModel
            // 
            this.btnSelectModel.Location = new System.Drawing.Point(679, 23);
            this.btnSelectModel.Name = "btnSelectModel";
            this.btnSelectModel.Size = new System.Drawing.Size(75, 23);
            this.btnSelectModel.TabIndex = 2;
            this.btnSelectModel.Text = "选择...";
            this.btnSelectModel.UseVisualStyleBackColor = true;
            this.btnSelectModel.Click += new System.EventHandler(this.btnSelectModel_Click);
            // 
            // txtModelPath
            // 
            this.txtModelPath.Location = new System.Drawing.Point(85, 25);
            this.txtModelPath.Name = "txtModelPath";
            this.txtModelPath.Size = new System.Drawing.Size(588, 21);
            this.txtModelPath.TabIndex = 1;
            // 
            // lblModelPath
            // 
            this.lblModelPath.AutoSize = true;
            this.lblModelPath.Location = new System.Drawing.Point(15, 28);
            this.lblModelPath.Name = "lblModelPath";
            this.lblModelPath.Size = new System.Drawing.Size(65, 12);
            this.lblModelPath.TabIndex = 0;
            this.lblModelPath.Text = "模型路径：";
            // 
            // txtServerUrl
            // 
            this.txtServerUrl.Location = new System.Drawing.Point(85, 52);
            this.txtServerUrl.Name = "txtServerUrl";
            this.txtServerUrl.Size = new System.Drawing.Size(200, 21);
            this.txtServerUrl.TabIndex = 4;
            this.txtServerUrl.Text = "http://127.0.0.1:9890";
            // 
            // lblServerUrl
            // 
            this.lblServerUrl.AutoSize = true;
            this.lblServerUrl.Location = new System.Drawing.Point(15, 55);
            this.lblServerUrl.Name = "lblServerUrl";
            this.lblServerUrl.Size = new System.Drawing.Size(77, 12);
            this.lblServerUrl.TabIndex = 3;
            this.lblServerUrl.Text = "服务器地址：";
            // 
            // btnLoadModel
            // 
            this.btnLoadModel.Location = new System.Drawing.Point(291, 50);
            this.btnLoadModel.Name = "btnLoadModel";
            this.btnLoadModel.Size = new System.Drawing.Size(75, 23);
            this.btnLoadModel.TabIndex = 5;
            this.btnLoadModel.Text = "加载模型";
            this.btnLoadModel.UseVisualStyleBackColor = true;
            this.btnLoadModel.Click += new System.EventHandler(this.btnLoadModel_Click);
            // 
            // btnGetModelInfo
            // 
            this.btnGetModelInfo.Location = new System.Drawing.Point(372, 50);
            this.btnGetModelInfo.Name = "btnGetModelInfo";
            this.btnGetModelInfo.Size = new System.Drawing.Size(90, 23);
            this.btnGetModelInfo.TabIndex = 6;
            this.btnGetModelInfo.Text = "获取模型信息";
            this.btnGetModelInfo.UseVisualStyleBackColor = true;
            this.btnGetModelInfo.Click += new System.EventHandler(this.btnGetModelInfo_Click);
            // 
            // groupBoxImage
            // 
            this.groupBoxImage.Controls.Add(this.btnInfer);
            this.groupBoxImage.Controls.Add(this.btnSelectImage);
            this.groupBoxImage.Controls.Add(this.txtImagePath);
            this.groupBoxImage.Controls.Add(this.lblImagePath);
            this.groupBoxImage.Location = new System.Drawing.Point(12, 138);
            this.groupBoxImage.Name = "groupBoxImage";
            this.groupBoxImage.Size = new System.Drawing.Size(760, 80);
            this.groupBoxImage.TabIndex = 1;
            this.groupBoxImage.TabStop = false;
            this.groupBoxImage.Text = "图像推理";
            // 
            // btnSelectImage
            // 
            this.btnSelectImage.Location = new System.Drawing.Point(679, 23);
            this.btnSelectImage.Name = "btnSelectImage";
            this.btnSelectImage.Size = new System.Drawing.Size(75, 23);
            this.btnSelectImage.TabIndex = 2;
            this.btnSelectImage.Text = "选择...";
            this.btnSelectImage.UseVisualStyleBackColor = true;
            this.btnSelectImage.Click += new System.EventHandler(this.btnSelectImage_Click);
            // 
            // txtImagePath
            // 
            this.txtImagePath.Location = new System.Drawing.Point(85, 25);
            this.txtImagePath.Name = "txtImagePath";
            this.txtImagePath.Size = new System.Drawing.Size(588, 21);
            this.txtImagePath.TabIndex = 1;
            // 
            // lblImagePath
            // 
            this.lblImagePath.AutoSize = true;
            this.lblImagePath.Location = new System.Drawing.Point(15, 28);
            this.lblImagePath.Name = "lblImagePath";
            this.lblImagePath.Size = new System.Drawing.Size(65, 12);
            this.lblImagePath.TabIndex = 0;
            this.lblImagePath.Text = "图像路径：";
            // 
            // btnInfer
            // 
            this.btnInfer.Location = new System.Drawing.Point(85, 52);
            this.btnInfer.Name = "btnInfer";
            this.btnInfer.Size = new System.Drawing.Size(75, 23);
            this.btnInfer.TabIndex = 3;
            this.btnInfer.Text = "开始推理";
            this.btnInfer.UseVisualStyleBackColor = true;
            this.btnInfer.Click += new System.EventHandler(this.btnInfer_Click);
            // 
            // groupBoxResults
            // 
            this.groupBoxResults.Controls.Add(this.txtResults);
            this.groupBoxResults.Location = new System.Drawing.Point(12, 224);
            this.groupBoxResults.Name = "groupBoxResults";
            this.groupBoxResults.Size = new System.Drawing.Size(760, 300);
            this.groupBoxResults.TabIndex = 2;
            this.groupBoxResults.TabStop = false;
            this.groupBoxResults.Text = "推理结果";
            // 
            // txtResults
            // 
            this.txtResults.Location = new System.Drawing.Point(17, 20);
            this.txtResults.Multiline = true;
            this.txtResults.Name = "txtResults";
            this.txtResults.ReadOnly = true;
            this.txtResults.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtResults.Size = new System.Drawing.Size(737, 274);
            this.txtResults.TabIndex = 0;
            // 
            // statusStrip1
            // 
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabel});
            this.statusStrip1.Location = new System.Drawing.Point(0, 538);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(784, 22);
            this.statusStrip1.TabIndex = 3;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabel
            // 
            this.toolStripStatusLabel.Name = "toolStripStatusLabel";
            this.toolStripStatusLabel.Size = new System.Drawing.Size(56, 17);
            this.toolStripStatusLabel.Text = "就绪状态";
            // 
            // btnFreeModel
            // 
            this.btnFreeModel.Location = new System.Drawing.Point(468, 50);
            this.btnFreeModel.Name = "btnFreeModel";
            this.btnFreeModel.Size = new System.Drawing.Size(75, 23);
            this.btnFreeModel.TabIndex = 7;
            this.btnFreeModel.Text = "释放模型";
            this.btnFreeModel.UseVisualStyleBackColor = true;
            this.btnFreeModel.Click += new System.EventHandler(this.btnFreeModel_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 560);
            this.Controls.Add(this.statusStrip1);
            this.Controls.Add(this.groupBoxResults);
            this.Controls.Add(this.groupBoxImage);
            this.Controls.Add(this.groupBoxModel);
            this.Name = "Form1";
            this.Text = "DLCV HTTP API 测试工具";
            this.groupBoxModel.ResumeLayout(false);
            this.groupBoxModel.PerformLayout();
            this.groupBoxImage.ResumeLayout(false);
            this.groupBoxImage.PerformLayout();
            this.groupBoxResults.ResumeLayout(false);
            this.groupBoxResults.PerformLayout();
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBoxModel;
        private System.Windows.Forms.Button btnSelectModel;
        private System.Windows.Forms.TextBox txtModelPath;
        private System.Windows.Forms.Label lblModelPath;
        private System.Windows.Forms.TextBox txtServerUrl;
        private System.Windows.Forms.Label lblServerUrl;
        private System.Windows.Forms.Button btnLoadModel;
        private System.Windows.Forms.Button btnGetModelInfo;
        private System.Windows.Forms.GroupBox groupBoxImage;
        private System.Windows.Forms.Button btnSelectImage;
        private System.Windows.Forms.TextBox txtImagePath;
        private System.Windows.Forms.Label lblImagePath;
        private System.Windows.Forms.Button btnInfer;
        private System.Windows.Forms.GroupBox groupBoxResults;
        private System.Windows.Forms.TextBox txtResults;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel;
        private System.Windows.Forms.Button btnFreeModel;
    }
}

