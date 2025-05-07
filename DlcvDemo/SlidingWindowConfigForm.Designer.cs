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
            this.lblWidth.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblWidth.Location = new System.Drawing.Point(30, 30);
            this.lblWidth.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblWidth.Name = "lblWidth";
            this.lblWidth.Size = new System.Drawing.Size(104, 24);
            this.lblWidth.TabIndex = 0;
            this.lblWidth.Text = "小图像宽度:";
            // 
            // txtWidth
            // 
            this.txtWidth.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtWidth.Location = new System.Drawing.Point(154, 26);
            this.txtWidth.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtWidth.Name = "txtWidth";
            this.txtWidth.Size = new System.Drawing.Size(298, 31);
            this.txtWidth.TabIndex = 1;
            // 
            // lblHeight
            // 
            this.lblHeight.AutoSize = true;
            this.lblHeight.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblHeight.Location = new System.Drawing.Point(30, 90);
            this.lblHeight.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblHeight.Name = "lblHeight";
            this.lblHeight.Size = new System.Drawing.Size(104, 24);
            this.lblHeight.TabIndex = 2;
            this.lblHeight.Text = "小图像高度:";
            // 
            // txtHeight
            // 
            this.txtHeight.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtHeight.Location = new System.Drawing.Point(154, 86);
            this.txtHeight.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtHeight.Name = "txtHeight";
            this.txtHeight.Size = new System.Drawing.Size(298, 31);
            this.txtHeight.TabIndex = 3;
            // 
            // lblHOverlap
            // 
            this.lblHOverlap.AutoSize = true;
            this.lblHOverlap.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblHOverlap.Location = new System.Drawing.Point(30, 150);
            this.lblHOverlap.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblHOverlap.Name = "lblHOverlap";
            this.lblHOverlap.Size = new System.Drawing.Size(86, 24);
            this.lblHOverlap.TabIndex = 4;
            this.lblHOverlap.Text = "水平重叠:";
            // 
            // txtHOverlap
            // 
            this.txtHOverlap.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtHOverlap.Location = new System.Drawing.Point(154, 146);
            this.txtHOverlap.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtHOverlap.Name = "txtHOverlap";
            this.txtHOverlap.Size = new System.Drawing.Size(298, 31);
            this.txtHOverlap.TabIndex = 5;
            // 
            // lblVOverlap
            // 
            this.lblVOverlap.AutoSize = true;
            this.lblVOverlap.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblVOverlap.Location = new System.Drawing.Point(30, 210);
            this.lblVOverlap.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblVOverlap.Name = "lblVOverlap";
            this.lblVOverlap.Size = new System.Drawing.Size(86, 24);
            this.lblVOverlap.TabIndex = 6;
            this.lblVOverlap.Text = "垂直重叠:";
            // 
            // txtVOverlap
            // 
            this.txtVOverlap.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtVOverlap.Location = new System.Drawing.Point(154, 206);
            this.txtVOverlap.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtVOverlap.Name = "txtVOverlap";
            this.txtVOverlap.Size = new System.Drawing.Size(298, 31);
            this.txtVOverlap.TabIndex = 7;
            // 
            // lblThreshold
            // 
            this.lblThreshold.AutoSize = true;
            this.lblThreshold.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblThreshold.Location = new System.Drawing.Point(30, 270);
            this.lblThreshold.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblThreshold.Name = "lblThreshold";
            this.lblThreshold.Size = new System.Drawing.Size(50, 24);
            this.lblThreshold.TabIndex = 8;
            this.lblThreshold.Text = "阈值:";
            // 
            // txtThreshold
            // 
            this.txtThreshold.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtThreshold.Location = new System.Drawing.Point(154, 266);
            this.txtThreshold.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtThreshold.Name = "txtThreshold";
            this.txtThreshold.Size = new System.Drawing.Size(298, 31);
            this.txtThreshold.TabIndex = 9;
            // 
            // lblIouThreshold
            // 
            this.lblIouThreshold.AutoSize = true;
            this.lblIouThreshold.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblIouThreshold.Location = new System.Drawing.Point(30, 330);
            this.lblIouThreshold.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblIouThreshold.Name = "lblIouThreshold";
            this.lblIouThreshold.Size = new System.Drawing.Size(83, 24);
            this.lblIouThreshold.TabIndex = 10;
            this.lblIouThreshold.Text = "IOU阈值:";
            // 
            // txtIouThreshold
            // 
            this.txtIouThreshold.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtIouThreshold.Location = new System.Drawing.Point(154, 326);
            this.txtIouThreshold.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtIouThreshold.Name = "txtIouThreshold";
            this.txtIouThreshold.Size = new System.Drawing.Size(298, 31);
            this.txtIouThreshold.TabIndex = 11;
            // 
            // lblCombineIosThreshold
            // 
            this.lblCombineIosThreshold.AutoSize = true;
            this.lblCombineIosThreshold.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblCombineIosThreshold.Location = new System.Drawing.Point(30, 390);
            this.lblCombineIosThreshold.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblCombineIosThreshold.Name = "lblCombineIosThreshold";
            this.lblCombineIosThreshold.Size = new System.Drawing.Size(116, 24);
            this.lblCombineIosThreshold.TabIndex = 12;
            this.lblCombineIosThreshold.Text = "合并IOS阈值:";
            // 
            // txtCombineIosThreshold
            // 
            this.txtCombineIosThreshold.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtCombineIosThreshold.Location = new System.Drawing.Point(154, 386);
            this.txtCombineIosThreshold.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtCombineIosThreshold.Name = "txtCombineIosThreshold";
            this.txtCombineIosThreshold.Size = new System.Drawing.Size(298, 31);
            this.txtCombineIosThreshold.TabIndex = 13;
            // 
            // btnOK
            // 
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnOK.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnOK.Location = new System.Drawing.Point(182, 445);
            this.btnOK.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(120, 40);
            this.btnOK.TabIndex = 14;
            this.btnOK.Text = "确定";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCancel.Location = new System.Drawing.Point(332, 445);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(120, 40);
            this.btnCancel.TabIndex = 15;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // SlidingWindowConfigForm
            // 
            this.AcceptButton = this.btnOK;
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(485, 515);
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
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
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