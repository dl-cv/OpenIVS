namespace DlcvHttpApiTest
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
            this.btnSelectModel = new System.Windows.Forms.Button();
            this.btnSelectImage = new System.Windows.Forms.Button();
            this.btnCheckServer = new System.Windows.Forms.Button();
            this.btnLoadModel = new System.Windows.Forms.Button();
            this.btnGetModelInfo = new System.Windows.Forms.Button();
            this.btnInfer = new System.Windows.Forms.Button();
            this.btnPressureTest = new System.Windows.Forms.Button();
            this.txtThreads = new System.Windows.Forms.TextBox();
            this.txtRate = new System.Windows.Forms.TextBox();
            this.txtResult = new System.Windows.Forms.RichTextBox();
            this.lblThreads = new System.Windows.Forms.Label();
            this.lblRate = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnSelectModel
            // 
            this.btnSelectModel.Location = new System.Drawing.Point(30, 28);
            this.btnSelectModel.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnSelectModel.Name = "btnSelectModel";
            this.btnSelectModel.Size = new System.Drawing.Size(180, 42);
            this.btnSelectModel.TabIndex = 0;
            this.btnSelectModel.Text = "选择模型";
            this.btnSelectModel.UseVisualStyleBackColor = true;
            this.btnSelectModel.Click += new System.EventHandler(this.BtnSelectModel_Click);
            // 
            // btnSelectImage
            // 
            this.btnSelectImage.Location = new System.Drawing.Point(225, 28);
            this.btnSelectImage.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnSelectImage.Name = "btnSelectImage";
            this.btnSelectImage.Size = new System.Drawing.Size(180, 42);
            this.btnSelectImage.TabIndex = 1;
            this.btnSelectImage.Text = "选择图像";
            this.btnSelectImage.UseVisualStyleBackColor = true;
            this.btnSelectImage.Click += new System.EventHandler(this.BtnSelectImage_Click);
            // 
            // btnCheckServer
            // 
            this.btnCheckServer.Location = new System.Drawing.Point(420, 28);
            this.btnCheckServer.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnCheckServer.Name = "btnCheckServer";
            this.btnCheckServer.Size = new System.Drawing.Size(180, 42);
            this.btnCheckServer.TabIndex = 2;
            this.btnCheckServer.Text = "检查服务器";
            this.btnCheckServer.UseVisualStyleBackColor = true;
            this.btnCheckServer.Click += new System.EventHandler(this.BtnCheckServer_Click);
            // 
            // btnLoadModel
            // 
            this.btnLoadModel.Location = new System.Drawing.Point(615, 28);
            this.btnLoadModel.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnLoadModel.Name = "btnLoadModel";
            this.btnLoadModel.Size = new System.Drawing.Size(180, 42);
            this.btnLoadModel.TabIndex = 3;
            this.btnLoadModel.Text = "加载模型";
            this.btnLoadModel.UseVisualStyleBackColor = true;
            this.btnLoadModel.Click += new System.EventHandler(this.BtnLoadModel_Click);
            // 
            // btnGetModelInfo
            // 
            this.btnGetModelInfo.Location = new System.Drawing.Point(810, 28);
            this.btnGetModelInfo.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnGetModelInfo.Name = "btnGetModelInfo";
            this.btnGetModelInfo.Size = new System.Drawing.Size(180, 42);
            this.btnGetModelInfo.TabIndex = 4;
            this.btnGetModelInfo.Text = "获取模型信息";
            this.btnGetModelInfo.UseVisualStyleBackColor = true;
            this.btnGetModelInfo.Click += new System.EventHandler(this.BtnGetModelInfo_Click);
            // 
            // btnInfer
            // 
            this.btnInfer.Location = new System.Drawing.Point(30, 97);
            this.btnInfer.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnInfer.Name = "btnInfer";
            this.btnInfer.Size = new System.Drawing.Size(180, 42);
            this.btnInfer.TabIndex = 5;
            this.btnInfer.Text = "单线程推理";
            this.btnInfer.UseVisualStyleBackColor = true;
            this.btnInfer.Click += new System.EventHandler(this.BtnInfer_Click);
            // 
            // btnPressureTest
            // 
            this.btnPressureTest.Location = new System.Drawing.Point(225, 97);
            this.btnPressureTest.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnPressureTest.Name = "btnPressureTest";
            this.btnPressureTest.Size = new System.Drawing.Size(180, 42);
            this.btnPressureTest.TabIndex = 6;
            this.btnPressureTest.Text = "多线程推理";
            this.btnPressureTest.UseVisualStyleBackColor = true;
            this.btnPressureTest.Click += new System.EventHandler(this.BtnPressureTest_Click);
            // 
            // txtThreads
            // 
            this.txtThreads.Location = new System.Drawing.Point(420, 97);
            this.txtThreads.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtThreads.Name = "txtThreads";
            this.txtThreads.Size = new System.Drawing.Size(73, 28);
            this.txtThreads.TabIndex = 7;
            this.txtThreads.Text = "5";
            // 
            // txtRate
            // 
            this.txtRate.Location = new System.Drawing.Point(600, 97);
            this.txtRate.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtRate.Name = "txtRate";
            this.txtRate.Size = new System.Drawing.Size(73, 28);
            this.txtRate.TabIndex = 9;
            this.txtRate.Text = "10";
            // 
            // txtResult
            // 
            this.txtResult.Location = new System.Drawing.Point(30, 166);
            this.txtResult.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtResult.Name = "txtResult";
            this.txtResult.ReadOnly = true;
            this.txtResult.Size = new System.Drawing.Size(960, 489);
            this.txtResult.TabIndex = 11;
            this.txtResult.Text = "";
            // 
            // lblThreads
            // 
            this.lblThreads.AutoSize = true;
            this.lblThreads.Location = new System.Drawing.Point(510, 104);
            this.lblThreads.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblThreads.Name = "lblThreads";
            this.lblThreads.Size = new System.Drawing.Size(62, 18);
            this.lblThreads.TabIndex = 8;
            this.lblThreads.Text = "线程数";
            // 
            // lblRate
            // 
            this.lblRate.AutoSize = true;
            this.lblRate.Location = new System.Drawing.Point(690, 104);
            this.lblRate.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblRate.Name = "lblRate";
            this.lblRate.Size = new System.Drawing.Size(98, 18);
            this.lblRate.TabIndex = 10;
            this.lblRate.Text = "每秒请求数";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1050, 692);
            this.Controls.Add(this.btnSelectModel);
            this.Controls.Add(this.btnSelectImage);
            this.Controls.Add(this.btnCheckServer);
            this.Controls.Add(this.btnLoadModel);
            this.Controls.Add(this.btnGetModelInfo);
            this.Controls.Add(this.btnInfer);
            this.Controls.Add(this.btnPressureTest);
            this.Controls.Add(this.txtThreads);
            this.Controls.Add(this.lblThreads);
            this.Controls.Add(this.txtRate);
            this.Controls.Add(this.lblRate);
            this.Controls.Add(this.txtResult);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "DLCV HTTP API 测试工具";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        // 控件成员变量声明
        private System.Windows.Forms.Button btnSelectModel;
        private System.Windows.Forms.Button btnSelectImage;
        private System.Windows.Forms.Button btnCheckServer;
        private System.Windows.Forms.Button btnLoadModel;
        private System.Windows.Forms.Button btnGetModelInfo;
        private System.Windows.Forms.Button btnInfer;
        private System.Windows.Forms.Button btnPressureTest;
        private System.Windows.Forms.TextBox txtThreads;
        private System.Windows.Forms.TextBox txtRate;
        private System.Windows.Forms.RichTextBox txtResult;
        private System.Windows.Forms.Label lblThreads;
        private System.Windows.Forms.Label lblRate;
    }
}
