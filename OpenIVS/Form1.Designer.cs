namespace OpenIVS
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
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.panelMain = new System.Windows.Forms.Panel();
            this.imageViewer1 = new DLCV.ImageViewer();
            this.groupBoxControls = new System.Windows.Forms.GroupBox();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnStart = new System.Windows.Forms.Button();
            this.groupBoxStatus = new System.Windows.Forms.GroupBox();
            this.lblModelStatus = new System.Windows.Forms.Label();
            this.lblCameraStatus = new System.Windows.Forms.Label();
            this.lblDeviceStatus = new System.Windows.Forms.Label();
            this.lblResult = new System.Windows.Forms.Label();
            this.progressBarPosition = new System.Windows.Forms.ProgressBar();
            this.lblCurrentPosition = new System.Windows.Forms.Label();
            this.timerUpdateStatus = new System.Windows.Forms.Timer(this.components);
            this.statusStrip.SuspendLayout();
            this.panelMain.SuspendLayout();
            this.groupBoxControls.SuspendLayout();
            this.groupBoxStatus.SuspendLayout();
            this.SuspendLayout();
            // 
            // statusStrip
            // 
            this.statusStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblStatus});
            this.statusStrip.Location = new System.Drawing.Point(0, 644);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Padding = new System.Windows.Forms.Padding(2, 0, 21, 0);
            this.statusStrip.Size = new System.Drawing.Size(1200, 31);
            this.statusStrip.TabIndex = 0;
            this.statusStrip.Text = "statusStrip1";
            // 
            // lblStatus
            // 
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(112, 24);
            this.lblStatus.Text = "系统初始化...";
            // 
            // panelMain
            // 
            this.panelMain.Controls.Add(this.imageViewer1);
            this.panelMain.Controls.Add(this.groupBoxControls);
            this.panelMain.Controls.Add(this.groupBoxStatus);
            this.panelMain.Controls.Add(this.progressBarPosition);
            this.panelMain.Controls.Add(this.lblCurrentPosition);
            this.panelMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelMain.Location = new System.Drawing.Point(0, 0);
            this.panelMain.Margin = new System.Windows.Forms.Padding(4);
            this.panelMain.Name = "panelMain";
            this.panelMain.Padding = new System.Windows.Forms.Padding(15);
            this.panelMain.Size = new System.Drawing.Size(1200, 644);
            this.panelMain.TabIndex = 1;
            // 
            // imageViewer1
            // 
            this.imageViewer1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.imageViewer1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.imageViewer1.image = null;
            this.imageViewer1.Location = new System.Drawing.Point(22, 102);
            this.imageViewer1.MaxScale = 100F;
            this.imageViewer1.MinScale = 0.5F;
            this.imageViewer1.Name = "imageViewer1";
            this.imageViewer1.Size = new System.Drawing.Size(1159, 462);
            this.imageViewer1.TabIndex = 6;
            // 
            // groupBoxControls
            // 
            this.groupBoxControls.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBoxControls.Controls.Add(this.btnStop);
            this.groupBoxControls.Controls.Add(this.btnStart);
            this.groupBoxControls.Location = new System.Drawing.Point(1009, 571);
            this.groupBoxControls.Margin = new System.Windows.Forms.Padding(4);
            this.groupBoxControls.Name = "groupBoxControls";
            this.groupBoxControls.Padding = new System.Windows.Forms.Padding(4);
            this.groupBoxControls.Size = new System.Drawing.Size(178, 69);
            this.groupBoxControls.TabIndex = 5;
            this.groupBoxControls.TabStop = false;
            this.groupBoxControls.Text = "控制";
            // 
            // btnStop
            // 
            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(92, 30);
            this.btnStop.Margin = new System.Windows.Forms.Padding(4);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(78, 32);
            this.btnStop.TabIndex = 1;
            this.btnStop.Text = "停止";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            // 
            // btnStart
            // 
            this.btnStart.Enabled = false;
            this.btnStart.Location = new System.Drawing.Point(9, 30);
            this.btnStart.Margin = new System.Windows.Forms.Padding(4);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(78, 32);
            this.btnStart.TabIndex = 0;
            this.btnStart.Text = "启动";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // groupBoxStatus
            // 
            this.groupBoxStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBoxStatus.Controls.Add(this.lblModelStatus);
            this.groupBoxStatus.Controls.Add(this.lblCameraStatus);
            this.groupBoxStatus.Controls.Add(this.lblDeviceStatus);
            this.groupBoxStatus.Controls.Add(this.lblResult);
            this.groupBoxStatus.Location = new System.Drawing.Point(20, 20);
            this.groupBoxStatus.Margin = new System.Windows.Forms.Padding(4);
            this.groupBoxStatus.Name = "groupBoxStatus";
            this.groupBoxStatus.Padding = new System.Windows.Forms.Padding(4);
            this.groupBoxStatus.Size = new System.Drawing.Size(1161, 75);
            this.groupBoxStatus.TabIndex = 4;
            this.groupBoxStatus.TabStop = false;
            this.groupBoxStatus.Text = "设备状态";
            // 
            // lblModelStatus
            // 
            this.lblModelStatus.AutoSize = true;
            this.lblModelStatus.Location = new System.Drawing.Point(675, 33);
            this.lblModelStatus.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblModelStatus.Name = "lblModelStatus";
            this.lblModelStatus.Size = new System.Drawing.Size(170, 18);
            this.lblModelStatus.TabIndex = 2;
            this.lblModelStatus.Text = "模型状态：未初始化";
            // 
            // lblCameraStatus
            // 
            this.lblCameraStatus.AutoSize = true;
            this.lblCameraStatus.Location = new System.Drawing.Point(362, 33);
            this.lblCameraStatus.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblCameraStatus.Name = "lblCameraStatus";
            this.lblCameraStatus.Size = new System.Drawing.Size(170, 18);
            this.lblCameraStatus.TabIndex = 1;
            this.lblCameraStatus.Text = "相机状态：未初始化";
            // 
            // lblDeviceStatus
            // 
            this.lblDeviceStatus.AutoSize = true;
            this.lblDeviceStatus.Location = new System.Drawing.Point(32, 33);
            this.lblDeviceStatus.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblDeviceStatus.Name = "lblDeviceStatus";
            this.lblDeviceStatus.Size = new System.Drawing.Size(170, 18);
            this.lblDeviceStatus.TabIndex = 0;
            this.lblDeviceStatus.Text = "设备状态：未初始化";
            // 
            // lblResult
            // 
            this.lblResult.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblResult.AutoSize = true;
            this.lblResult.Location = new System.Drawing.Point(986, 25);
            this.lblResult.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblResult.Name = "lblResult";
            this.lblResult.Size = new System.Drawing.Size(116, 18);
            this.lblResult.TabIndex = 0;
            this.lblResult.Text = "检测结果：无";
            // 
            // progressBarPosition
            // 
            this.progressBarPosition.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBarPosition.Location = new System.Drawing.Point(20, 600);
            this.progressBarPosition.Margin = new System.Windows.Forms.Padding(4);
            this.progressBarPosition.Name = "progressBarPosition";
            this.progressBarPosition.Size = new System.Drawing.Size(974, 40);
            this.progressBarPosition.TabIndex = 3;
            // 
            // lblCurrentPosition
            // 
            this.lblCurrentPosition.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblCurrentPosition.AutoSize = true;
            this.lblCurrentPosition.Location = new System.Drawing.Point(19, 578);
            this.lblCurrentPosition.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblCurrentPosition.Name = "lblCurrentPosition";
            this.lblCurrentPosition.Size = new System.Drawing.Size(107, 18);
            this.lblCurrentPosition.TabIndex = 2;
            this.lblCurrentPosition.Text = "当前位置：0";
            // 
            // timerUpdateStatus
            // 
            this.timerUpdateStatus.Interval = 500;
            this.timerUpdateStatus.Tick += new System.EventHandler(this.timerUpdateStatus_Tick);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 675);
            this.Controls.Add(this.panelMain);
            this.Controls.Add(this.statusStrip);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "OpenIVS - 深度视觉 开源工业视觉系统";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.panelMain.ResumeLayout(false);
            this.panelMain.PerformLayout();
            this.groupBoxControls.ResumeLayout(false);
            this.groupBoxStatus.ResumeLayout(false);
            this.groupBoxStatus.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.Panel panelMain;
        private System.Windows.Forms.GroupBox groupBoxControls;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.GroupBox groupBoxStatus;
        private System.Windows.Forms.Label lblModelStatus;
        private System.Windows.Forms.Label lblCameraStatus;
        private System.Windows.Forms.Label lblDeviceStatus;
        private System.Windows.Forms.ProgressBar progressBarPosition;
        private System.Windows.Forms.Label lblCurrentPosition;
        private System.Windows.Forms.Label lblResult;
        private System.Windows.Forms.Timer timerUpdateStatus;
        private DLCV.ImageViewer imageViewer1;
    }
}

