namespace HalconDemo
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
            this.components = new System.ComponentModel.Container();
            this.hWindowControl = new HalconDotNet.HWindowControl();
            this.btnSelectImage = new System.Windows.Forms.Button();
            this.btnSelectModel = new System.Windows.Forms.Button();
            this.btnInfer = new System.Windows.Forms.Button();
            this.lblImagePath = new System.Windows.Forms.Label();
            this.lblModelPath = new System.Windows.Forms.Label();
            this.txtImagePath = new System.Windows.Forms.TextBox();
            this.txtModelPath = new System.Windows.Forms.TextBox();
            this.panel1 = new System.Windows.Forms.Panel();
            this.lblResult = new System.Windows.Forms.Label();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // hWindowControl
            // 
            this.hWindowControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.hWindowControl.BackColor = System.Drawing.Color.Black;
            this.hWindowControl.BorderColor = System.Drawing.Color.Black;
            this.hWindowControl.ImagePart = new System.Drawing.Rectangle(0, 0, 640, 480);
            this.hWindowControl.Location = new System.Drawing.Point(12, 12);
            this.hWindowControl.Name = "hWindowControl";
            this.hWindowControl.Size = new System.Drawing.Size(776, 323);
            this.hWindowControl.TabIndex = 0;
            this.hWindowControl.WindowSize = new System.Drawing.Size(776, 323);
            // 
            // btnSelectImage
            // 
            this.btnSelectImage.Location = new System.Drawing.Point(690, 14);
            this.btnSelectImage.Name = "btnSelectImage";
            this.btnSelectImage.Size = new System.Drawing.Size(75, 23);
            this.btnSelectImage.TabIndex = 1;
            this.btnSelectImage.Text = "选择图像";
            this.btnSelectImage.UseVisualStyleBackColor = true;
            this.btnSelectImage.Click += new System.EventHandler(this.btnSelectImage_Click);
            // 
            // btnSelectModel
            // 
            this.btnSelectModel.Location = new System.Drawing.Point(690, 43);
            this.btnSelectModel.Name = "btnSelectModel";
            this.btnSelectModel.Size = new System.Drawing.Size(75, 23);
            this.btnSelectModel.TabIndex = 2;
            this.btnSelectModel.Text = "选择模型";
            this.btnSelectModel.UseVisualStyleBackColor = true;
            this.btnSelectModel.Click += new System.EventHandler(this.btnSelectModel_Click);
            // 
            // btnInfer
            // 
            this.btnInfer.Location = new System.Drawing.Point(690, 72);
            this.btnInfer.Name = "btnInfer";
            this.btnInfer.Size = new System.Drawing.Size(75, 23);
            this.btnInfer.TabIndex = 3;
            this.btnInfer.Text = "开始推理";
            this.btnInfer.UseVisualStyleBackColor = true;
            this.btnInfer.Click += new System.EventHandler(this.btnInfer_Click);
            // 
            // lblImagePath
            // 
            this.lblImagePath.AutoSize = true;
            this.lblImagePath.Location = new System.Drawing.Point(12, 17);
            this.lblImagePath.Name = "lblImagePath";
            this.lblImagePath.Size = new System.Drawing.Size(65, 12);
            this.lblImagePath.TabIndex = 4;
            this.lblImagePath.Text = "图像路径：";
            // 
            // lblModelPath
            // 
            this.lblModelPath.AutoSize = true;
            this.lblModelPath.Location = new System.Drawing.Point(12, 48);
            this.lblModelPath.Name = "lblModelPath";
            this.lblModelPath.Size = new System.Drawing.Size(65, 12);
            this.lblModelPath.TabIndex = 5;
            this.lblModelPath.Text = "模型路径：";
            // 
            // txtImagePath
            // 
            this.txtImagePath.Location = new System.Drawing.Point(83, 14);
            this.txtImagePath.Name = "txtImagePath";
            this.txtImagePath.Size = new System.Drawing.Size(601, 21);
            this.txtImagePath.TabIndex = 6;
            // 
            // txtModelPath
            // 
            this.txtModelPath.Location = new System.Drawing.Point(83, 45);
            this.txtModelPath.Name = "txtModelPath";
            this.txtModelPath.Size = new System.Drawing.Size(601, 21);
            this.txtModelPath.TabIndex = 7;
            // 
            // panel1
            // 
            this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(this.lblResult);
            this.panel1.Controls.Add(this.lblImagePath);
            this.panel1.Controls.Add(this.txtModelPath);
            this.panel1.Controls.Add(this.btnSelectImage);
            this.panel1.Controls.Add(this.txtImagePath);
            this.panel1.Controls.Add(this.btnSelectModel);
            this.panel1.Controls.Add(this.lblModelPath);
            this.panel1.Controls.Add(this.btnInfer);
            this.panel1.Location = new System.Drawing.Point(12, 341);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(776, 97);
            this.panel1.TabIndex = 8;
            // 
            // lblResult
            // 
            this.lblResult.AutoSize = true;
            this.lblResult.Location = new System.Drawing.Point(81, 77);
            this.lblResult.Name = "lblResult";
            this.lblResult.Size = new System.Drawing.Size(65, 12);
            this.lblResult.TabIndex = 8;
            this.lblResult.Text = "推理结果：";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.hWindowControl);
            this.Name = "Form1";
            this.Text = "Halcon推理演示";
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private HalconDotNet.HWindowControl hWindowControl;
        private System.Windows.Forms.Button btnSelectImage;
        private System.Windows.Forms.Button btnSelectModel;
        private System.Windows.Forms.Button btnInfer;
        private System.Windows.Forms.Label lblImagePath;
        private System.Windows.Forms.Label lblModelPath;
        private System.Windows.Forms.TextBox txtImagePath;
        private System.Windows.Forms.TextBox txtModelPath;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label lblResult;
    }
}

