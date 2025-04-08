namespace CameraManagerTest
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
            this._pictureBox = new System.Windows.Forms.PictureBox();
            this._comboDevices = new System.Windows.Forms.ComboBox();
            this._btnConnect = new System.Windows.Forms.Button();
            this._btnStartGrab = new System.Windows.Forms.Button();
            this._btnStopGrab = new System.Windows.Forms.Button();
            this._btnTriggerOnce = new System.Windows.Forms.Button();
            this._btnStartContinuous = new System.Windows.Forms.Button();
            this._btnStopContinuous = new System.Windows.Forms.Button();
            this._comboTriggerMode = new System.Windows.Forms.ComboBox();
            this._groupCamera = new System.Windows.Forms.GroupBox();
            this._lblTriggerMode = new System.Windows.Forms.Label();
            this._txtInterval = new System.Windows.Forms.TextBox();
            this._lblInterval = new System.Windows.Forms.Label();
            this._lblStatus = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this._pictureBox)).BeginInit();
            this._groupCamera.SuspendLayout();
            this.SuspendLayout();
            // 
            // _pictureBox
            // 
            this._pictureBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._pictureBox.Location = new System.Drawing.Point(12, 200);
            this._pictureBox.Name = "_pictureBox";
            this._pictureBox.Size = new System.Drawing.Size(776, 328);
            this._pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this._pictureBox.TabIndex = 0;
            this._pictureBox.TabStop = false;
            this._pictureBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _comboDevices
            // 
            this._comboDevices.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._comboDevices.Location = new System.Drawing.Point(12, 12);
            this._comboDevices.Name = "_comboDevices";
            this._comboDevices.Size = new System.Drawing.Size(300, 26);
            this._comboDevices.TabIndex = 1;
            this._comboDevices.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
            // 
            // _btnConnect
            // 
            this._btnConnect.Location = new System.Drawing.Point(320, 12);
            this._btnConnect.Name = "_btnConnect";
            this._btnConnect.Size = new System.Drawing.Size(80, 28);
            this._btnConnect.TabIndex = 2;
            this._btnConnect.Text = "连接";
            this._btnConnect.UseVisualStyleBackColor = true;
            this._btnConnect.Click += new System.EventHandler(this.BtnConnect_Click);
            this._btnConnect.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top)));
            // 
            // _btnStartGrab
            // 
            this._btnStartGrab.Location = new System.Drawing.Point(15, 60);
            this._btnStartGrab.Name = "_btnStartGrab";
            this._btnStartGrab.Size = new System.Drawing.Size(100, 28);
            this._btnStartGrab.TabIndex = 1;
            this._btnStartGrab.Text = "开始采集";
            this._btnStartGrab.UseVisualStyleBackColor = true;
            this._btnStartGrab.Click += new System.EventHandler(this.BtnStartGrab_Click);
            this._btnStartGrab.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
            // 
            // _btnStopGrab
            // 
            this._btnStopGrab.Enabled = false;
            this._btnStopGrab.Location = new System.Drawing.Point(125, 60);
            this._btnStopGrab.Name = "_btnStopGrab";
            this._btnStopGrab.Size = new System.Drawing.Size(100, 28);
            this._btnStopGrab.TabIndex = 2;
            this._btnStopGrab.Text = "停止采集";
            this._btnStopGrab.UseVisualStyleBackColor = true;
            this._btnStopGrab.Click += new System.EventHandler(this.BtnStopGrab_Click);
            this._btnStopGrab.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
            // 
            // _btnTriggerOnce
            // 
            this._btnTriggerOnce.Enabled = false;
            this._btnTriggerOnce.Location = new System.Drawing.Point(15, 95);
            this._btnTriggerOnce.Name = "_btnTriggerOnce";
            this._btnTriggerOnce.Size = new System.Drawing.Size(100, 28);
            this._btnTriggerOnce.TabIndex = 3;
            this._btnTriggerOnce.Text = "单次触发";
            this._btnTriggerOnce.UseVisualStyleBackColor = true;
            this._btnTriggerOnce.Click += new System.EventHandler(this.BtnTriggerOnce_Click);
            this._btnTriggerOnce.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
            // 
            // _btnStartContinuous
            // 
            this._btnStartContinuous.Enabled = false;
            this._btnStartContinuous.Location = new System.Drawing.Point(250, 95);
            this._btnStartContinuous.Name = "_btnStartContinuous";
            this._btnStartContinuous.Size = new System.Drawing.Size(60, 28);
            this._btnStartContinuous.TabIndex = 6;
            this._btnStartContinuous.Text = "开始";
            this._btnStartContinuous.UseVisualStyleBackColor = true;
            this._btnStartContinuous.Click += new System.EventHandler(this.BtnStartContinuous_Click);
            this._btnStartContinuous.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
            // 
            // _btnStopContinuous
            // 
            this._btnStopContinuous.Enabled = false;
            this._btnStopContinuous.Location = new System.Drawing.Point(315, 95);
            this._btnStopContinuous.Name = "_btnStopContinuous";
            this._btnStopContinuous.Size = new System.Drawing.Size(60, 28);
            this._btnStopContinuous.TabIndex = 7;
            this._btnStopContinuous.Text = "停止";
            this._btnStopContinuous.UseVisualStyleBackColor = true;
            this._btnStopContinuous.Click += new System.EventHandler(this.BtnStopContinuous_Click);
            this._btnStopContinuous.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
            // 
            // _comboTriggerMode
            // 
            this._comboTriggerMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._comboTriggerMode.Location = new System.Drawing.Point(90, 22);
            this._comboTriggerMode.Name = "_comboTriggerMode";
            this._comboTriggerMode.Size = new System.Drawing.Size(150, 26);
            this._comboTriggerMode.TabIndex = 0;
            this._comboTriggerMode.SelectedIndexChanged += new System.EventHandler(this.ComboTriggerMode_SelectedIndexChanged);
            this._comboTriggerMode.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
            // 
            // _groupCamera
            // 
            this._groupCamera.Controls.Add(this._lblTriggerMode);
            this._groupCamera.Controls.Add(this._btnStopContinuous);
            this._groupCamera.Controls.Add(this._btnStartContinuous);
            this._groupCamera.Controls.Add(this._txtInterval);
            this._groupCamera.Controls.Add(this._lblInterval);
            this._groupCamera.Controls.Add(this._btnTriggerOnce);
            this._groupCamera.Controls.Add(this._btnStopGrab);
            this._groupCamera.Controls.Add(this._btnStartGrab);
            this._groupCamera.Controls.Add(this._comboTriggerMode);
            this._groupCamera.Enabled = false;
            this._groupCamera.Location = new System.Drawing.Point(12, 50);
            this._groupCamera.Name = "_groupCamera";
            this._groupCamera.Size = new System.Drawing.Size(450, 140);
            this._groupCamera.TabIndex = 3;
            this._groupCamera.TabStop = false;
            this._groupCamera.Text = "相机控制";
            this._groupCamera.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _lblTriggerMode
            // 
            this._lblTriggerMode.AutoSize = true;
            this._lblTriggerMode.Location = new System.Drawing.Point(15, 25);
            this._lblTriggerMode.Name = "_lblTriggerMode";
            this._lblTriggerMode.Size = new System.Drawing.Size(89, 18);
            this._lblTriggerMode.TabIndex = 8;
            this._lblTriggerMode.Text = "触发模式:";
            this._lblTriggerMode.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
            // 
            // _txtInterval
            // 
            this._txtInterval.Enabled = false;
            this._txtInterval.Location = new System.Drawing.Point(190, 95);
            this._txtInterval.Name = "_txtInterval";
            this._txtInterval.Size = new System.Drawing.Size(50, 28);
            this._txtInterval.TabIndex = 5;
            this._txtInterval.Text = "1000";
            this._txtInterval.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
            // 
            // _lblInterval
            // 
            this._lblInterval.AutoSize = true;
            this._lblInterval.Enabled = false;
            this._lblInterval.Location = new System.Drawing.Point(125, 98);
            this._lblInterval.Name = "_lblInterval";
            this._lblInterval.Size = new System.Drawing.Size(89, 18);
            this._lblInterval.TabIndex = 4;
            this._lblInterval.Text = "间隔(ms):";
            this._lblInterval.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left))));
            // 
            // _lblStatus
            // 
            this._lblStatus.AutoSize = false;
            this._lblStatus.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this._lblStatus.Location = new System.Drawing.Point(0, 569);
            this._lblStatus.Name = "_lblStatus";
            this._lblStatus.Size = new System.Drawing.Size(800, 30);
            this._lblStatus.TabIndex = 4;
            this._lblStatus.Text = "准备就绪";
            this._lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this._lblStatus.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this._lblStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 600);
            this.Controls.Add(this._groupCamera);
            this.Controls.Add(this._btnConnect);
            this.Controls.Add(this._comboDevices);
            this.Controls.Add(this._pictureBox);
            this.Controls.Add(this._lblStatus);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            this.MinimumSize = new System.Drawing.Size(820, 650);
            this.MaximizeBox = true;
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "相机测试";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this._pictureBox)).EndInit();
            this._groupCamera.ResumeLayout(false);
            this._groupCamera.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox _pictureBox;
        private System.Windows.Forms.ComboBox _comboDevices;
        private System.Windows.Forms.Button _btnConnect;
        private System.Windows.Forms.Button _btnStartGrab;
        private System.Windows.Forms.Button _btnStopGrab;
        private System.Windows.Forms.Button _btnTriggerOnce;
        private System.Windows.Forms.Button _btnStartContinuous;
        private System.Windows.Forms.Button _btnStopContinuous;
        private System.Windows.Forms.ComboBox _comboTriggerMode;
        private System.Windows.Forms.GroupBox _groupCamera;
        private System.Windows.Forms.TextBox _txtInterval;
        private System.Windows.Forms.Label _lblInterval;
        private System.Windows.Forms.Label _lblStatus;
        private System.Windows.Forms.Label _lblTriggerMode;
    }
}

