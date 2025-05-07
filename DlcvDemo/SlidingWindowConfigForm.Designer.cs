namespace DlcvDemo
{
    partial class SlidingWindowConfigForm
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
            this.lblWidth = new System.Windows.Forms.Label();
            this.txtWidth = new System.Windows.Forms.TextBox();
            this.lblHeight = new System.Windows.Forms.Label();
            this.txtHeight = new System.Windows.Forms.TextBox();
            this.lblHOverlap = new System.Windows.Forms.Label();
            this.txtHOverlap = new System.Windows.Forms.TextBox();
            this.lblVOverlap = new System.Windows.Forms.Label();
            this.txtVOverlap = new System.Windows.Forms.TextBox();
            this.lblThreshold = new System.Windows.Forms.Label();
            this.txtThreshold = new System.Windows.Forms.TextBox();
            this.lblIouThreshold = new System.Windows.Forms.Label();
            this.txtIouThreshold = new System.Windows.Forms.TextBox();
            this.lblCombineIosThreshold = new System.Windows.Forms.Label();
            this.txtCombineIosThreshold = new System.Windows.Forms.TextBox();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // lblWidth
            // 
            this.lblWidth.AutoSize = true;
            this.lblWidth.Location = new System.Drawing.Point(20, 20);
            this.lblWidth.Name = "lblWidth";
            this.lblWidth.Size = new System.Drawing.Size(65, 12);
            this.lblWidth.TabIndex = 0;
            this.lblWidth.Text = "小图像宽度:";
            // 
            // txtWidth
            // 
            this.txtWidth.Location = new System.Drawing.Point(150, 20);
            this.txtWidth.Name = "txtWidth";
            this.txtWidth.Size = new System.Drawing.Size(200, 21);
            this.txtWidth.TabIndex = 1;
            // 
            // lblHeight
            // 
            this.lblHeight.AutoSize = true;
            this.lblHeight.Location = new System.Drawing.Point(20, 60);
            this.lblHeight.Name = "lblHeight";
            this.lblHeight.Size = new System.Drawing.Size(65, 12);
            this.lblHeight.TabIndex = 2;
            this.lblHeight.Text = "小图像高度:";
            // 
            // txtHeight
            // 
            this.txtHeight.Location = new System.Drawing.Point(150, 60);
            this.txtHeight.Name = "txtHeight";
            this.txtHeight.Size = new System.Drawing.Size(200, 21);
            this.txtHeight.TabIndex = 3;
            // 
            // lblHOverlap
            // 
            this.lblHOverlap.AutoSize = true;
            this.lblHOverlap.Location = new System.Drawing.Point(20, 100);
            this.lblHOverlap.Name = "lblHOverlap";
            this.lblHOverlap.Size = new System.Drawing.Size(53, 12);
            this.lblHOverlap.TabIndex = 4;
            this.lblHOverlap.Text = "水平重叠:";
            // 
            // txtHOverlap
            // 
            this.txtHOverlap.Location = new System.Drawing.Point(150, 100);
            this.txtHOverlap.Name = "txtHOverlap";
            this.txtHOverlap.Size = new System.Drawing.Size(200, 21);
            this.txtHOverlap.TabIndex = 5;
            // 
            // lblVOverlap
            // 
            this.lblVOverlap.AutoSize = true;
            this.lblVOverlap.Location = new System.Drawing.Point(20, 140);
            this.lblVOverlap.Name = "lblVOverlap";
            this.lblVOverlap.Size = new System.Drawing.Size(53, 12);
            this.lblVOverlap.TabIndex = 6;
            this.lblVOverlap.Text = "垂直重叠:";
            // 
            // txtVOverlap
            // 
            this.txtVOverlap.Location = new System.Drawing.Point(150, 140);
            this.txtVOverlap.Name = "txtVOverlap";
            this.txtVOverlap.Size = new System.Drawing.Size(200, 21);
            this.txtVOverlap.TabIndex = 7;
            // 
            // lblThreshold
            // 
            this.lblThreshold.AutoSize = true;
            this.lblThreshold.Location = new System.Drawing.Point(20, 180);
            this.lblThreshold.Name = "lblThreshold";
            this.lblThreshold.Size = new System.Drawing.Size(29, 12);
            this.lblThreshold.TabIndex = 8;
            this.lblThreshold.Text = "阈值:";
            // 
            // txtThreshold
            // 
            this.txtThreshold.Location = new System.Drawing.Point(150, 180);
            this.txtThreshold.Name = "txtThreshold";
            this.txtThreshold.Size = new System.Drawing.Size(200, 21);
            this.txtThreshold.TabIndex = 9;
            // 
            // lblIouThreshold
            // 
            this.lblIouThreshold.AutoSize = true;
            this.lblIouThreshold.Location = new System.Drawing.Point(20, 220);
            this.lblIouThreshold.Name = "lblIouThreshold";
            this.lblIouThreshold.Size = new System.Drawing.Size(53, 12);
            this.lblIouThreshold.TabIndex = 10;
            this.lblIouThreshold.Text = "IOU阈值:";
            // 
            // txtIouThreshold
            // 
            this.txtIouThreshold.Location = new System.Drawing.Point(150, 220);
            this.txtIouThreshold.Name = "txtIouThreshold";
            this.txtIouThreshold.Size = new System.Drawing.Size(200, 21);
            this.txtIouThreshold.TabIndex = 11;
            // 
            // lblCombineIosThreshold
            // 
            this.lblCombineIosThreshold.AutoSize = true;
            this.lblCombineIosThreshold.Location = new System.Drawing.Point(20, 260);
            this.lblCombineIosThreshold.Name = "lblCombineIosThreshold";
            this.lblCombineIosThreshold.Size = new System.Drawing.Size(77, 12);
            this.lblCombineIosThreshold.TabIndex = 12;
            this.lblCombineIosThreshold.Text = "合并IOS阈值:";
            // 
            // txtCombineIosThreshold
            // 
            this.txtCombineIosThreshold.Location = new System.Drawing.Point(150, 260);
            this.txtCombineIosThreshold.Name = "txtCombineIosThreshold";
            this.txtCombineIosThreshold.Size = new System.Drawing.Size(200, 21);
            this.txtCombineIosThreshold.TabIndex = 13;
            // 
            // btnOK
            // 
            this.btnOK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnOK.Location = new System.Drawing.Point(150, 300);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(75, 23);
            this.btnOK.TabIndex = 14;
            this.btnOK.Text = "确定";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(250, 300);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 15;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // SlidingWindowConfigForm
            // 
            this.AcceptButton = this.btnOK;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(384, 361);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.txtCombineIosThreshold);
            this.Controls.Add(this.lblCombineIosThreshold);
            this.Controls.Add(this.txtIouThreshold);
            this.Controls.Add(this.lblIouThreshold);
            this.Controls.Add(this.txtThreshold);
            this.Controls.Add(this.lblThreshold);
            this.Controls.Add(this.txtVOverlap);
            this.Controls.Add(this.lblVOverlap);
            this.Controls.Add(this.txtHOverlap);
            this.Controls.Add(this.lblHOverlap);
            this.Controls.Add(this.txtHeight);
            this.Controls.Add(this.lblHeight);
            this.Controls.Add(this.txtWidth);
            this.Controls.Add(this.lblWidth);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SlidingWindowConfigForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "滑窗裁图模型参数配置";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        protected System.Windows.Forms.Label lblWidth;
        protected System.Windows.Forms.TextBox txtWidth;
        protected System.Windows.Forms.Label lblHeight;
        protected System.Windows.Forms.TextBox txtHeight;
        protected System.Windows.Forms.Label lblHOverlap;
        protected System.Windows.Forms.TextBox txtHOverlap;
        protected System.Windows.Forms.Label lblVOverlap;
        protected System.Windows.Forms.TextBox txtVOverlap;
        protected System.Windows.Forms.Label lblThreshold;
        protected System.Windows.Forms.TextBox txtThreshold;
        protected System.Windows.Forms.Label lblIouThreshold;
        protected System.Windows.Forms.TextBox txtIouThreshold;
        protected System.Windows.Forms.Label lblCombineIosThreshold;
        protected System.Windows.Forms.TextBox txtCombineIosThreshold;
        protected System.Windows.Forms.Button btnOK;
        protected System.Windows.Forms.Button btnCancel;
    }
} 