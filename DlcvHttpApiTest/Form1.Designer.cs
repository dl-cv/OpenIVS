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
            // 控件声明
            this.btnSelectModel = new System.Windows.Forms.Button();
            this.btnSelectImage = new System.Windows.Forms.Button();
            this.btnCheckServer = new System.Windows.Forms.Button();
            this.btnInfer = new System.Windows.Forms.Button();
            this.btnPressureTest = new System.Windows.Forms.Button();
            this.txtThreads = new System.Windows.Forms.TextBox();
            this.txtRate = new System.Windows.Forms.TextBox();
            this.txtResult = new System.Windows.Forms.RichTextBox();
            System.Windows.Forms.Label lblThreads = new System.Windows.Forms.Label();
            System.Windows.Forms.Label lblRate = new System.Windows.Forms.Label();
            
            this.SuspendLayout();
            
            // btnSelectModel
            this.btnSelectModel.Location = new System.Drawing.Point(20, 20);
            this.btnSelectModel.Name = "btnSelectModel";
            this.btnSelectModel.Size = new System.Drawing.Size(120, 30);
            this.btnSelectModel.TabIndex = 0;
            this.btnSelectModel.Text = "选择模型";
            this.btnSelectModel.UseVisualStyleBackColor = true;
            this.btnSelectModel.Click += new System.EventHandler(this.BtnSelectModel_Click);
            
            // btnSelectImage
            this.btnSelectImage.Location = new System.Drawing.Point(150, 20);
            this.btnSelectImage.Name = "btnSelectImage";
            this.btnSelectImage.Size = new System.Drawing.Size(120, 30);
            this.btnSelectImage.TabIndex = 1;
            this.btnSelectImage.Text = "选择图像";
            this.btnSelectImage.UseVisualStyleBackColor = true;
            this.btnSelectImage.Click += new System.EventHandler(this.BtnSelectImage_Click);
            
            // btnCheckServer
            this.btnCheckServer.Location = new System.Drawing.Point(280, 20);
            this.btnCheckServer.Name = "btnCheckServer";
            this.btnCheckServer.Size = new System.Drawing.Size(120, 30);
            this.btnCheckServer.TabIndex = 2;
            this.btnCheckServer.Text = "检查服务器";
            this.btnCheckServer.UseVisualStyleBackColor = true;
            this.btnCheckServer.Click += new System.EventHandler(this.BtnCheckServer_Click);
            
            // btnInfer
            this.btnInfer.Location = new System.Drawing.Point(20, 70);
            this.btnInfer.Name = "btnInfer";
            this.btnInfer.Size = new System.Drawing.Size(120, 30);
            this.btnInfer.TabIndex = 3;
            this.btnInfer.Text = "单线程推理";
            this.btnInfer.UseVisualStyleBackColor = true;
            this.btnInfer.Click += new System.EventHandler(this.BtnInfer_Click);
            
            // btnPressureTest
            this.btnPressureTest.Location = new System.Drawing.Point(150, 70);
            this.btnPressureTest.Name = "btnPressureTest";
            this.btnPressureTest.Size = new System.Drawing.Size(120, 30);
            this.btnPressureTest.TabIndex = 4;
            this.btnPressureTest.Text = "多线程推理";
            this.btnPressureTest.UseVisualStyleBackColor = true;
            this.btnPressureTest.Click += new System.EventHandler(this.BtnPressureTest_Click);
            
            // txtThreads
            this.txtThreads.Location = new System.Drawing.Point(280, 70);
            this.txtThreads.Name = "txtThreads";
            this.txtThreads.Size = new System.Drawing.Size(50, 20);
            this.txtThreads.TabIndex = 5;
            this.txtThreads.Text = "5";
            
            // lblThreads
            lblThreads.AutoSize = true;
            lblThreads.Location = new System.Drawing.Point(340, 75);
            lblThreads.Name = "lblThreads";
            lblThreads.Size = new System.Drawing.Size(41, 13);
            lblThreads.TabIndex = 6;
            lblThreads.Text = "线程数";
            
            // txtRate
            this.txtRate.Location = new System.Drawing.Point(400, 70);
            this.txtRate.Name = "txtRate";
            this.txtRate.Size = new System.Drawing.Size(50, 20);
            this.txtRate.TabIndex = 7;
            this.txtRate.Text = "10";
            
            // lblRate
            lblRate.AutoSize = true;
            lblRate.Location = new System.Drawing.Point(460, 75);
            lblRate.Name = "lblRate";
            lblRate.Size = new System.Drawing.Size(65, 13);
            lblRate.TabIndex = 8;
            lblRate.Text = "每秒请求数";
            
            // txtResult
            this.txtResult.Location = new System.Drawing.Point(20, 120);
            this.txtResult.Name = "txtResult";
            this.txtResult.Size = new System.Drawing.Size(540, 300);
            this.txtResult.TabIndex = 9;
            this.txtResult.ReadOnly = true;
            
            // Form1
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(600, 500);
            
            // 添加控件到窗体
            this.Controls.Add(this.btnSelectModel);
            this.Controls.Add(this.btnSelectImage);
            this.Controls.Add(this.btnCheckServer);
            this.Controls.Add(this.btnInfer);
            this.Controls.Add(this.btnPressureTest);
            this.Controls.Add(this.txtThreads);
            this.Controls.Add(lblThreads);
            this.Controls.Add(this.txtRate);
            this.Controls.Add(lblRate);
            this.Controls.Add(this.txtResult);
            
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
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
        private System.Windows.Forms.Button btnInfer;
        private System.Windows.Forms.Button btnPressureTest;
        private System.Windows.Forms.TextBox txtThreads;
        private System.Windows.Forms.TextBox txtRate;
        private System.Windows.Forms.RichTextBox txtResult;
    }
}
