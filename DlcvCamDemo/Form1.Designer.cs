namespace DlcvCamDemo
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
            this.cbDeviceList = new System.Windows.Forms.ComboBox();
            this.bnStopGrab = new System.Windows.Forms.Button();
            this.bnStartGrab = new System.Windows.Forms.Button();
            this.bnClose = new System.Windows.Forms.Button();
            this.bnOpen = new System.Windows.Forms.Button();
            this.bnSetSoftTrigger = new System.Windows.Forms.Button();
            this.bnSetHardTrigger = new System.Windows.Forms.Button();
            this.bnSortTriggerOnce = new System.Windows.Forms.Button();
            this.bnSoftTriggerLoop = new System.Windows.Forms.Button();
            this.numLoopInterval = new System.Windows.Forms.NumericUpDown();
            this.button5 = new System.Windows.Forms.Button();
            this.bnLoadModel = new System.Windows.Forms.Button();
            this.label_fps = new System.Windows.Forms.Label();
            this.bnSaveImg = new System.Windows.Forms.Button();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.btnLoadImage = new System.Windows.Forms.Button();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.checkBox2 = new System.Windows.Forms.CheckBox();
            this.cbImageFormat = new System.Windows.Forms.ComboBox();
            this.btnHttp = new System.Windows.Forms.Button();
            this.btnStressTest = new System.Windows.Forms.Button();
            this.nudRate = new System.Windows.Forms.NumericUpDown();
            this.lblStatus = new System.Windows.Forms.Label();
            this.imagePanel1 = new DLCV.ImageViewer();
            this.nudThread = new System.Windows.Forms.NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)(this.numLoopInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudRate)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudThread)).BeginInit();
            this.SuspendLayout();
            // 
            // cbDeviceList
            // 
            this.cbDeviceList.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cbDeviceList.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbDeviceList.FormattingEnabled = true;
            this.cbDeviceList.Location = new System.Drawing.Point(12, 59);
            this.cbDeviceList.Margin = new System.Windows.Forms.Padding(4);
            this.cbDeviceList.Name = "cbDeviceList";
            this.cbDeviceList.Size = new System.Drawing.Size(973, 26);
            this.cbDeviceList.TabIndex = 1;
            // 
            // bnStopGrab
            // 
            this.bnStopGrab.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bnStopGrab.Enabled = false;
            this.bnStopGrab.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.bnStopGrab.Location = new System.Drawing.Point(1125, 95);
            this.bnStopGrab.Margin = new System.Windows.Forms.Padding(4);
            this.bnStopGrab.Name = "bnStopGrab";
            this.bnStopGrab.Size = new System.Drawing.Size(120, 32);
            this.bnStopGrab.TabIndex = 5;
            this.bnStopGrab.Text = "Stop";
            this.bnStopGrab.UseVisualStyleBackColor = true;
            this.bnStopGrab.Click += new System.EventHandler(this.bnStopGrab_Click);
            // 
            // bnStartGrab
            // 
            this.bnStartGrab.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bnStartGrab.Enabled = false;
            this.bnStartGrab.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.bnStartGrab.Location = new System.Drawing.Point(993, 95);
            this.bnStartGrab.Margin = new System.Windows.Forms.Padding(4);
            this.bnStartGrab.Name = "bnStartGrab";
            this.bnStartGrab.Size = new System.Drawing.Size(120, 32);
            this.bnStartGrab.TabIndex = 4;
            this.bnStartGrab.Text = "Start";
            this.bnStartGrab.UseVisualStyleBackColor = true;
            this.bnStartGrab.Click += new System.EventHandler(this.bnStartGrab_Click_1);
            // 
            // bnClose
            // 
            this.bnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bnClose.Enabled = false;
            this.bnClose.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.bnClose.Location = new System.Drawing.Point(1125, 53);
            this.bnClose.Margin = new System.Windows.Forms.Padding(4);
            this.bnClose.Name = "bnClose";
            this.bnClose.Size = new System.Drawing.Size(120, 32);
            this.bnClose.TabIndex = 7;
            this.bnClose.Text = "Close Device";
            this.bnClose.UseVisualStyleBackColor = true;
            this.bnClose.Click += new System.EventHandler(this.bnClose_Click);
            // 
            // bnOpen
            // 
            this.bnOpen.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bnOpen.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.bnOpen.Location = new System.Drawing.Point(993, 53);
            this.bnOpen.Margin = new System.Windows.Forms.Padding(4);
            this.bnOpen.Name = "bnOpen";
            this.bnOpen.Size = new System.Drawing.Size(120, 32);
            this.bnOpen.TabIndex = 6;
            this.bnOpen.Text = "Open Device";
            this.bnOpen.UseVisualStyleBackColor = true;
            this.bnOpen.Click += new System.EventHandler(this.bnOpen_Click);
            // 
            // bnSetSoftTrigger
            // 
            this.bnSetSoftTrigger.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bnSetSoftTrigger.Location = new System.Drawing.Point(993, 136);
            this.bnSetSoftTrigger.Name = "bnSetSoftTrigger";
            this.bnSetSoftTrigger.Size = new System.Drawing.Size(120, 32);
            this.bnSetSoftTrigger.TabIndex = 8;
            this.bnSetSoftTrigger.Text = "设置软触发";
            this.bnSetSoftTrigger.UseVisualStyleBackColor = true;
            this.bnSetSoftTrigger.Click += new System.EventHandler(this.bnSetSoftTrigger_Click);
            // 
            // bnSetHardTrigger
            // 
            this.bnSetHardTrigger.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bnSetHardTrigger.Location = new System.Drawing.Point(1125, 136);
            this.bnSetHardTrigger.Name = "bnSetHardTrigger";
            this.bnSetHardTrigger.Size = new System.Drawing.Size(120, 32);
            this.bnSetHardTrigger.TabIndex = 9;
            this.bnSetHardTrigger.Text = "设置硬触发";
            this.bnSetHardTrigger.UseVisualStyleBackColor = true;
            this.bnSetHardTrigger.Click += new System.EventHandler(this.bnSetHardTrigger_Click);
            // 
            // bnSortTriggerOnce
            // 
            this.bnSortTriggerOnce.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bnSortTriggerOnce.Enabled = false;
            this.bnSortTriggerOnce.Location = new System.Drawing.Point(993, 178);
            this.bnSortTriggerOnce.Name = "bnSortTriggerOnce";
            this.bnSortTriggerOnce.Size = new System.Drawing.Size(120, 32);
            this.bnSortTriggerOnce.TabIndex = 10;
            this.bnSortTriggerOnce.Text = "软触发一次";
            this.bnSortTriggerOnce.UseVisualStyleBackColor = true;
            this.bnSortTriggerOnce.Click += new System.EventHandler(this.button3_Click);
            // 
            // bnSoftTriggerLoop
            // 
            this.bnSoftTriggerLoop.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bnSoftTriggerLoop.Enabled = false;
            this.bnSoftTriggerLoop.Location = new System.Drawing.Point(1125, 178);
            this.bnSoftTriggerLoop.Name = "bnSoftTriggerLoop";
            this.bnSoftTriggerLoop.Size = new System.Drawing.Size(120, 32);
            this.bnSoftTriggerLoop.TabIndex = 11;
            this.bnSoftTriggerLoop.Text = "软触发循环";
            this.bnSoftTriggerLoop.UseVisualStyleBackColor = true;
            this.bnSoftTriggerLoop.Click += new System.EventHandler(this.bnSoftTriggerLoop_Click);
            // 
            // numLoopInterval
            // 
            this.numLoopInterval.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.numLoopInterval.Location = new System.Drawing.Point(1126, 220);
            this.numLoopInterval.Maximum = new decimal(new int[] {
            2000,
            0,
            0,
            0});
            this.numLoopInterval.Minimum = new decimal(new int[] {
            15,
            0,
            0,
            0});
            this.numLoopInterval.Name = "numLoopInterval";
            this.numLoopInterval.Size = new System.Drawing.Size(120, 28);
            this.numLoopInterval.TabIndex = 12;
            this.numLoopInterval.Value = new decimal(new int[] {
            15,
            0,
            0,
            0});
            // 
            // button5
            // 
            this.button5.Location = new System.Drawing.Point(12, 12);
            this.button5.Name = "button5";
            this.button5.Size = new System.Drawing.Size(162, 40);
            this.button5.TabIndex = 13;
            this.button5.Text = "刷新摄像机列表";
            this.button5.UseVisualStyleBackColor = true;
            this.button5.Click += new System.EventHandler(this.button5_Click);
            // 
            // bnLoadModel
            // 
            this.bnLoadModel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.bnLoadModel.Location = new System.Drawing.Point(993, 599);
            this.bnLoadModel.Name = "bnLoadModel";
            this.bnLoadModel.Size = new System.Drawing.Size(160, 32);
            this.bnLoadModel.TabIndex = 14;
            this.bnLoadModel.Text = "高性能推理";
            this.bnLoadModel.UseVisualStyleBackColor = true;
            this.bnLoadModel.Click += new System.EventHandler(this.bnLoadModel_Click);
            // 
            // label_fps
            // 
            this.label_fps.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label_fps.AutoSize = true;
            this.label_fps.Location = new System.Drawing.Point(12, 717);
            this.label_fps.Name = "label_fps";
            this.label_fps.Size = new System.Drawing.Size(80, 18);
            this.label_fps.TabIndex = 16;
            this.label_fps.Text = "当前帧率";
            // 
            // bnSaveImg
            // 
            this.bnSaveImg.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.bnSaveImg.Location = new System.Drawing.Point(993, 637);
            this.bnSaveImg.Name = "bnSaveImg";
            this.bnSaveImg.Size = new System.Drawing.Size(160, 32);
            this.bnSaveImg.TabIndex = 17;
            this.bnSaveImg.Text = "存当前画面";
            this.toolTip1.SetToolTip(this.bnSaveImg, "快捷键：“S”");
            this.bnSaveImg.UseVisualStyleBackColor = true;
            this.bnSaveImg.Click += new System.EventHandler(this.bnSaveImage_Click);
            // 
            // toolTip1
            // 
            this.toolTip1.AutomaticDelay = 200;
            // 
            // btnLoadImage
            // 
            this.btnLoadImage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLoadImage.Location = new System.Drawing.Point(993, 256);
            this.btnLoadImage.Name = "btnLoadImage";
            this.btnLoadImage.Size = new System.Drawing.Size(120, 32);
            this.btnLoadImage.TabIndex = 21;
            this.btnLoadImage.Text = "加载测试图";
            this.toolTip1.SetToolTip(this.btnLoadImage, "加载本地的图片来测试");
            this.btnLoadImage.UseVisualStyleBackColor = true;
            this.btnLoadImage.Click += new System.EventHandler(this.btnLoadImage_Click);
            // 
            // checkBox1
            // 
            this.checkBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.checkBox1.Appearance = System.Windows.Forms.Appearance.Button;
            this.checkBox1.AutoSize = true;
            this.checkBox1.Location = new System.Drawing.Point(1159, 639);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(90, 28);
            this.checkBox1.TabIndex = 18;
            this.checkBox1.Text = "持续存图";
            this.checkBox1.UseVisualStyleBackColor = true;
            this.checkBox1.CheckedChanged += new System.EventHandler(this.checkBox1_CheckedChanged);
            // 
            // checkBox2
            // 
            this.checkBox2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.checkBox2.Appearance = System.Windows.Forms.Appearance.Button;
            this.checkBox2.AutoSize = true;
            this.checkBox2.Enabled = false;
            this.checkBox2.Location = new System.Drawing.Point(1159, 581);
            this.checkBox2.Name = "checkBox2";
            this.checkBox2.Size = new System.Drawing.Size(90, 28);
            this.checkBox2.TabIndex = 19;
            this.checkBox2.Text = "开始推理";
            this.checkBox2.UseVisualStyleBackColor = true;
            this.checkBox2.CheckedChanged += new System.EventHandler(this.checkBox2_CheckedChanged);
            // 
            // cbImageFormat
            // 
            this.cbImageFormat.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cbImageFormat.FormattingEnabled = true;
            this.cbImageFormat.Items.AddRange(new object[] {
            "BMP",
            "PNG",
            "JPG"});
            this.cbImageFormat.Location = new System.Drawing.Point(993, 673);
            this.cbImageFormat.Name = "cbImageFormat";
            this.cbImageFormat.Size = new System.Drawing.Size(102, 26);
            this.cbImageFormat.TabIndex = 20;
            this.cbImageFormat.Text = "存储格式";
            this.cbImageFormat.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            // 
            // btnHttp
            // 
            this.btnHttp.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnHttp.Location = new System.Drawing.Point(993, 561);
            this.btnHttp.Name = "btnHttp";
            this.btnHttp.Size = new System.Drawing.Size(160, 32);
            this.btnHttp.TabIndex = 22;
            this.btnHttp.Text = "调用免费API";
            this.btnHttp.UseVisualStyleBackColor = true;
            this.btnHttp.Click += new System.EventHandler(this.btnHttp_Click);
            // 
            // btnStressTest
            // 
            this.btnStressTest.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnStressTest.Location = new System.Drawing.Point(1125, 256);
            this.btnStressTest.Name = "btnStressTest";
            this.btnStressTest.Size = new System.Drawing.Size(120, 32);
            this.btnStressTest.TabIndex = 23;
            this.btnStressTest.Text = "压力测试";
            this.btnStressTest.UseVisualStyleBackColor = true;
            this.btnStressTest.Click += new System.EventHandler(this.btnStressTest_Click);
            // 
            // nudRate
            // 
            this.nudRate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.nudRate.Location = new System.Drawing.Point(1129, 294);
            this.nudRate.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.nudRate.Name = "nudRate";
            this.nudRate.Size = new System.Drawing.Size(120, 28);
            this.nudRate.TabIndex = 24;
            this.nudRate.Value = new decimal(new int[] {
            500,
            0,
            0,
            0});
            // 
            // lblStatus
            // 
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(993, 370);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(107, 18);
            this.lblStatus.TabIndex = 25;
            this.lblStatus.Text = "压力测试...";
            // 
            // imagePanel1
            // 
            this.imagePanel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.imagePanel1.BackColor = System.Drawing.SystemColors.ButtonShadow;
            this.imagePanel1.image = null;
            this.imagePanel1.Location = new System.Drawing.Point(12, 102);
            this.imagePanel1.MaxScale = 20F;
            this.imagePanel1.MinScale = 0.1F;
            this.imagePanel1.Name = "imagePanel1";
            this.imagePanel1.ShowStatusText = false;
            this.imagePanel1.ShowVisualization = true;
            this.imagePanel1.Size = new System.Drawing.Size(975, 597);
            this.imagePanel1.TabIndex = 0;
            this.imagePanel1.TabStop = true;
            // 
            // nudThread
            // 
            this.nudThread.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.nudThread.Location = new System.Drawing.Point(1129, 329);
            this.nudThread.Maximum = new decimal(new int[] {
            16,
            0,
            0,
            0});
            this.nudThread.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.nudThread.Name = "nudThread";
            this.nudThread.Size = new System.Drawing.Size(120, 28);
            this.nudThread.TabIndex = 26;
            this.nudThread.Value = new decimal(new int[] {
            2,
            0,
            0,
            0});
            this.nudThread.ValueChanged += new System.EventHandler(this.nudThread_ValueChanged);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1258, 744);
            this.Controls.Add(this.nudThread);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.nudRate);
            this.Controls.Add(this.btnStressTest);
            this.Controls.Add(this.btnHttp);
            this.Controls.Add(this.btnLoadImage);
            this.Controls.Add(this.cbImageFormat);
            this.Controls.Add(this.checkBox2);
            this.Controls.Add(this.checkBox1);
            this.Controls.Add(this.bnSaveImg);
            this.Controls.Add(this.label_fps);
            this.Controls.Add(this.bnLoadModel);
            this.Controls.Add(this.button5);
            this.Controls.Add(this.numLoopInterval);
            this.Controls.Add(this.bnSoftTriggerLoop);
            this.Controls.Add(this.bnSortTriggerOnce);
            this.Controls.Add(this.bnSetHardTrigger);
            this.Controls.Add(this.bnSetSoftTrigger);
            this.Controls.Add(this.bnClose);
            this.Controls.Add(this.bnOpen);
            this.Controls.Add(this.bnStopGrab);
            this.Controls.Add(this.bnStartGrab);
            this.Controls.Add(this.cbDeviceList);
            this.Controls.Add(this.imagePanel1);
            this.MinimumSize = new System.Drawing.Size(800, 720);
            this.Name = "Form1";
            this.Text = "Form1";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numLoopInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudRate)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudThread)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DLCV.ImageViewer imagePanel1;
        private System.Windows.Forms.ComboBox cbDeviceList;
        private System.Windows.Forms.Button bnStopGrab;
        private System.Windows.Forms.Button bnStartGrab;
        private System.Windows.Forms.Button bnClose;
        private System.Windows.Forms.Button bnOpen;
        private System.Windows.Forms.Button bnSetSoftTrigger;
        private System.Windows.Forms.Button bnSetHardTrigger;
        private System.Windows.Forms.Button bnSortTriggerOnce;
        private System.Windows.Forms.Button bnSoftTriggerLoop;
        private System.Windows.Forms.NumericUpDown numLoopInterval;
        private System.Windows.Forms.Button button5;
        private System.Windows.Forms.Button bnLoadModel;
        private System.Windows.Forms.Label label_fps;
        private System.Windows.Forms.Button bnSaveImg;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.CheckBox checkBox2;
        private System.Windows.Forms.ComboBox cbImageFormat;
        private System.Windows.Forms.Button btnLoadImage;
        private System.Windows.Forms.Button btnHttp;
        private System.Windows.Forms.Button btnStressTest;
        private System.Windows.Forms.NumericUpDown nudRate;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.NumericUpDown nudThread;
    }
}

