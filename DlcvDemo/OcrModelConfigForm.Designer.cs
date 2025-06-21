namespace DlcvDemo
{
    partial class OcrModelConfigForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblDetModel = new System.Windows.Forms.Label();
            this.txtDetModel = new System.Windows.Forms.TextBox();
            this.btnBrowseDet = new System.Windows.Forms.Button();
            this.lblOcrModel = new System.Windows.Forms.Label();
            this.txtOcrModel = new System.Windows.Forms.TextBox();
            this.btnBrowseOcr = new System.Windows.Forms.Button();
            this.lblDevice = new System.Windows.Forms.Label();
            this.numDevice = new System.Windows.Forms.NumericUpDown();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            ((System.ComponentModel.ISupportInitialize)(this.numDevice)).BeginInit();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblDetModel
            // 
            this.lblDetModel.AutoSize = true;
            this.lblDetModel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblDetModel.Location = new System.Drawing.Point(15, 30);
            this.lblDetModel.Name = "lblDetModel";
            this.lblDetModel.Size = new System.Drawing.Size(68, 17);
            this.lblDetModel.TabIndex = 0;
            this.lblDetModel.Text = "检测模型：";
            // 
            // txtDetModel
            // 
            this.txtDetModel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtDetModel.Location = new System.Drawing.Point(90, 27);
            this.txtDetModel.Name = "txtDetModel";
            this.txtDetModel.ReadOnly = true;
            this.txtDetModel.Size = new System.Drawing.Size(350, 23);
            this.txtDetModel.TabIndex = 1;
            // 
            // btnBrowseDet
            // 
            this.btnBrowseDet.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnBrowseDet.Location = new System.Drawing.Point(450, 25);
            this.btnBrowseDet.Name = "btnBrowseDet";
            this.btnBrowseDet.Size = new System.Drawing.Size(75, 28);
            this.btnBrowseDet.TabIndex = 2;
            this.btnBrowseDet.Text = "浏览...";
            this.btnBrowseDet.UseVisualStyleBackColor = true;
            this.btnBrowseDet.Click += new System.EventHandler(this.btnBrowseDet_Click);
            // 
            // lblOcrModel
            // 
            this.lblOcrModel.AutoSize = true;
            this.lblOcrModel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblOcrModel.Location = new System.Drawing.Point(15, 70);
            this.lblOcrModel.Name = "lblOcrModel";
            this.lblOcrModel.Size = new System.Drawing.Size(68, 17);
            this.lblOcrModel.TabIndex = 3;
            this.lblOcrModel.Text = "OCR模型：";
            // 
            // txtOcrModel
            // 
            this.txtOcrModel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtOcrModel.Location = new System.Drawing.Point(90, 67);
            this.txtOcrModel.Name = "txtOcrModel";
            this.txtOcrModel.ReadOnly = true;
            this.txtOcrModel.Size = new System.Drawing.Size(350, 23);
            this.txtOcrModel.TabIndex = 4;
            // 
            // btnBrowseOcr
            // 
            this.btnBrowseOcr.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnBrowseOcr.Location = new System.Drawing.Point(450, 65);
            this.btnBrowseOcr.Name = "btnBrowseOcr";
            this.btnBrowseOcr.Size = new System.Drawing.Size(75, 28);
            this.btnBrowseOcr.TabIndex = 5;
            this.btnBrowseOcr.Text = "浏览...";
            this.btnBrowseOcr.UseVisualStyleBackColor = true;
            this.btnBrowseOcr.Click += new System.EventHandler(this.btnBrowseOcr_Click);
            // 
            // lblDevice
            // 
            this.lblDevice.AutoSize = true;
            this.lblDevice.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblDevice.Location = new System.Drawing.Point(15, 30);
            this.lblDevice.Name = "lblDevice";
            this.lblDevice.Size = new System.Drawing.Size(56, 17);
            this.lblDevice.TabIndex = 6;
            this.lblDevice.Text = "设备ID：";
            // 
            // numDevice
            // 
            this.numDevice.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.numDevice.Location = new System.Drawing.Point(90, 28);
            this.numDevice.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numDevice.Name = "numDevice";
            this.numDevice.Size = new System.Drawing.Size(80, 23);
            this.numDevice.TabIndex = 7;
            // 
            // btnOK
            // 
            this.btnOK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnOK.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnOK.Location = new System.Drawing.Point(180, 270);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(90, 35);
            this.btnOK.TabIndex = 8;
            this.btnOK.Text = "确定";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCancel.Location = new System.Drawing.Point(290, 270);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 35);
            this.btnCancel.TabIndex = 9;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.lblDetModel);
            this.groupBox1.Controls.Add(this.txtDetModel);
            this.groupBox1.Controls.Add(this.btnBrowseDet);
            this.groupBox1.Controls.Add(this.lblOcrModel);
            this.groupBox1.Controls.Add(this.txtOcrModel);
            this.groupBox1.Controls.Add(this.btnBrowseOcr);
            this.groupBox1.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.groupBox1.Location = new System.Drawing.Point(20, 20);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(540, 110);
            this.groupBox1.TabIndex = 10;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "模型文件选择";
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.lblDevice);
            this.groupBox2.Controls.Add(this.numDevice);
            this.groupBox2.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.groupBox2.Location = new System.Drawing.Point(20, 150);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(540, 70);
            this.groupBox2.TabIndex = 11;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "推理设置";
            // 
            // OcrModelConfigForm
            // 
            this.AcceptButton = this.btnOK;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(580, 330);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OcrModelConfigForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "OCR模型配置";
            ((System.ComponentModel.ISupportInitialize)(this.numDevice)).EndInit();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblDetModel;
        private System.Windows.Forms.TextBox txtDetModel;
        private System.Windows.Forms.Button btnBrowseDet;
        private System.Windows.Forms.Label lblOcrModel;
        private System.Windows.Forms.TextBox txtOcrModel;
        private System.Windows.Forms.Button btnBrowseOcr;
        private System.Windows.Forms.Label lblDevice;
        private System.Windows.Forms.NumericUpDown numDevice;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
    }
} 