namespace MultiCameraTest
{
    partial class CameraTabView
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

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.mainLayout = new System.Windows.Forms.TableLayoutPanel();
            this.controlsPanel = new System.Windows.Forms.TableLayoutPanel();
            this._groupCamera = new System.Windows.Forms.GroupBox();
            this._lblTriggerMode = new System.Windows.Forms.Label();
            this._comboTriggerMode = new System.Windows.Forms.ComboBox();
            this._btnStartGrab = new System.Windows.Forms.Button();
            this._btnStopGrab = new System.Windows.Forms.Button();
            this._btnTriggerOnce = new System.Windows.Forms.Button();
            this._lblInterval = new System.Windows.Forms.Label();
            this._txtInterval = new System.Windows.Forms.TextBox();
            this._btnStartContinuous = new System.Windows.Forms.Button();
            this._btnStopContinuous = new System.Windows.Forms.Button();
            this._groupExposure = new System.Windows.Forms.GroupBox();
            this._lblExposureTime = new System.Windows.Forms.Label();
            this._txtExposureTime = new System.Windows.Forms.TextBox();
            this._btnSetExposure = new System.Windows.Forms.Button();
            this._btnSyncExposure = new System.Windows.Forms.Button();
            this._lblExposureRange = new System.Windows.Forms.Label();
            this._groupWhiteBalance = new System.Windows.Forms.GroupBox();
            this._btnAutoWhiteBalance = new System.Windows.Forms.Button();
            this._btnSyncWhiteBalance = new System.Windows.Forms.Button();
            this._lblRedRatio = new System.Windows.Forms.Label();
            this._txtRedRatio = new System.Windows.Forms.TextBox();
            this._lblGreenRatio = new System.Windows.Forms.Label();
            this._txtGreenRatio = new System.Windows.Forms.TextBox();
            this._lblBlueRatio = new System.Windows.Forms.Label();
            this._txtBlueRatio = new System.Windows.Forms.TextBox();
            this._lblRatioRange = new System.Windows.Forms.Label();
            this._groupROI = new System.Windows.Forms.GroupBox();
            this._lblOffsetX = new System.Windows.Forms.Label();
            this._txtOffsetX = new System.Windows.Forms.TextBox();
            this._lblOffsetY = new System.Windows.Forms.Label();
            this._txtOffsetY = new System.Windows.Forms.TextBox();
            this._lblWidth = new System.Windows.Forms.Label();
            this._txtWidth = new System.Windows.Forms.TextBox();
            this._lblHeight = new System.Windows.Forms.Label();
            this._txtHeight = new System.Windows.Forms.TextBox();
            this._btnSetROI = new System.Windows.Forms.Button();
            this._btnSyncROI = new System.Windows.Forms.Button();
            this._btnRestoreROI = new System.Windows.Forms.Button();
            this._lblROITip = new System.Windows.Forms.Label();
            this._pictureBox = new System.Windows.Forms.PictureBox();
            this.mainLayout.SuspendLayout();
            this.controlsPanel.SuspendLayout();
            this._groupCamera.SuspendLayout();
            this._groupExposure.SuspendLayout();
            this._groupWhiteBalance.SuspendLayout();
            this._groupROI.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._pictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // mainLayout
            // 
            this.mainLayout.ColumnCount = 1;
            this.mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainLayout.Controls.Add(this.controlsPanel, 0, 0);
            this.mainLayout.Controls.Add(this._pictureBox, 0, 1);
            this.mainLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainLayout.Location = new System.Drawing.Point(0, 0);
            this.mainLayout.Margin = new System.Windows.Forms.Padding(0);
            this.mainLayout.Name = "mainLayout";
            this.mainLayout.RowCount = 2;
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 165F));
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainLayout.Size = new System.Drawing.Size(1000, 700);
            this.mainLayout.TabIndex = 0;
            // 
            // controlsPanel
            // 
            this.controlsPanel.ColumnCount = 4;
            this.controlsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.controlsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.controlsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.controlsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.controlsPanel.Controls.Add(this._groupCamera, 0, 0);
            this.controlsPanel.Controls.Add(this._groupExposure, 1, 0);
            this.controlsPanel.Controls.Add(this._groupWhiteBalance, 2, 0);
            this.controlsPanel.Controls.Add(this._groupROI, 3, 0);
            this.controlsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.controlsPanel.Location = new System.Drawing.Point(5, 5);
            this.controlsPanel.Margin = new System.Windows.Forms.Padding(5);
            this.controlsPanel.Name = "controlsPanel";
            this.controlsPanel.RowCount = 1;
            this.controlsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.controlsPanel.Size = new System.Drawing.Size(990, 155);
            this.controlsPanel.TabIndex = 1;
            // 
            // _groupCamera
            // 
            this._groupCamera.Controls.Add(this._lblTriggerMode);
            this._groupCamera.Controls.Add(this._comboTriggerMode);
            this._groupCamera.Controls.Add(this._btnStartGrab);
            this._groupCamera.Controls.Add(this._btnStopGrab);
            this._groupCamera.Controls.Add(this._btnTriggerOnce);
            this._groupCamera.Controls.Add(this._lblInterval);
            this._groupCamera.Controls.Add(this._txtInterval);
            this._groupCamera.Controls.Add(this._btnStartContinuous);
            this._groupCamera.Controls.Add(this._btnStopContinuous);
            this._groupCamera.Dock = System.Windows.Forms.DockStyle.Fill;
            this._groupCamera.Location = new System.Drawing.Point(3, 3);
            this._groupCamera.Name = "_groupCamera";
            this._groupCamera.Size = new System.Drawing.Size(241, 149);
            this._groupCamera.TabIndex = 0;
            this._groupCamera.TabStop = false;
            this._groupCamera.Text = "相机控制";
            // 
            // _lblTriggerMode
            // 
            this._lblTriggerMode.AutoSize = true;
            this._lblTriggerMode.Location = new System.Drawing.Point(6, 22);
            this._lblTriggerMode.Name = "_lblTriggerMode";
            this._lblTriggerMode.Size = new System.Drawing.Size(65, 12);
            this._lblTriggerMode.TabIndex = 0;
            this._lblTriggerMode.Text = "触发模式:";
            // 
            // _comboTriggerMode
            // 
            this._comboTriggerMode.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._comboTriggerMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._comboTriggerMode.FormattingEnabled = true;
            this._comboTriggerMode.Items.AddRange(new object[] {
            "关闭触发",
            "软触发",
            "Line0",
            "Line1"});
            this._comboTriggerMode.Location = new System.Drawing.Point(77, 19);
            this._comboTriggerMode.Name = "_comboTriggerMode";
            this._comboTriggerMode.Size = new System.Drawing.Size(158, 20);
            this._comboTriggerMode.TabIndex = 1;
            // 
            // _btnStartGrab
            // 
            this._btnStartGrab.Location = new System.Drawing.Point(8, 48);
            this._btnStartGrab.Name = "_btnStartGrab";
            this._btnStartGrab.Size = new System.Drawing.Size(75, 23);
            this._btnStartGrab.TabIndex = 2;
            this._btnStartGrab.Text = "开始采集";
            this._btnStartGrab.UseVisualStyleBackColor = true;
            // 
            // _btnStopGrab
            // 
            this._btnStopGrab.Enabled = false;
            this._btnStopGrab.Location = new System.Drawing.Point(89, 48);
            this._btnStopGrab.Name = "_btnStopGrab";
            this._btnStopGrab.Size = new System.Drawing.Size(75, 23);
            this._btnStopGrab.TabIndex = 3;
            this._btnStopGrab.Text = "停止采集";
            this._btnStopGrab.UseVisualStyleBackColor = true;
            // 
            // _btnTriggerOnce
            // 
            this._btnTriggerOnce.Enabled = false;
            this._btnTriggerOnce.Location = new System.Drawing.Point(8, 77);
            this._btnTriggerOnce.Name = "_btnTriggerOnce";
            this._btnTriggerOnce.Size = new System.Drawing.Size(75, 23);
            this._btnTriggerOnce.TabIndex = 4;
            this._btnTriggerOnce.Text = "单次触发";
            this._btnTriggerOnce.UseVisualStyleBackColor = true;
            // 
            // _lblInterval
            // 
            this._lblInterval.AutoSize = true;
            this._lblInterval.Enabled = false;
            this._lblInterval.Location = new System.Drawing.Point(87, 82);
            this._lblInterval.Name = "_lblInterval";
            this._lblInterval.Size = new System.Drawing.Size(59, 12);
            this._lblInterval.TabIndex = 5;
            this._lblInterval.Text = "间隔(ms):";
            // 
            // _txtInterval
            // 
            this._txtInterval.Enabled = false;
            this._txtInterval.Location = new System.Drawing.Point(152, 79);
            this._txtInterval.Name = "_txtInterval";
            this._txtInterval.Size = new System.Drawing.Size(50, 21);
            this._txtInterval.TabIndex = 6;
            this._txtInterval.Text = "1000";
            // 
            // _btnStartContinuous
            // 
            this._btnStartContinuous.Enabled = false;
            this._btnStartContinuous.Location = new System.Drawing.Point(8, 106);
            this._btnStartContinuous.Name = "_btnStartContinuous";
            this._btnStartContinuous.Size = new System.Drawing.Size(75, 23);
            this._btnStartContinuous.TabIndex = 7;
            this._btnStartContinuous.Text = "开始连续";
            this._btnStartContinuous.UseVisualStyleBackColor = true;
            // 
            // _btnStopContinuous
            // 
            this._btnStopContinuous.Enabled = false;
            this._btnStopContinuous.Location = new System.Drawing.Point(89, 106);
            this._btnStopContinuous.Name = "_btnStopContinuous";
            this._btnStopContinuous.Size = new System.Drawing.Size(75, 23);
            this._btnStopContinuous.TabIndex = 8;
            this._btnStopContinuous.Text = "停止连续";
            this._btnStopContinuous.UseVisualStyleBackColor = true;
            // 
            // _groupExposure
            // 
            this._groupExposure.Controls.Add(this._lblExposureTime);
            this._groupExposure.Controls.Add(this._txtExposureTime);
            this._groupExposure.Controls.Add(this._btnSetExposure);
            this._groupExposure.Controls.Add(this._btnSyncExposure);
            this._groupExposure.Controls.Add(this._lblExposureRange);
            this._groupExposure.Dock = System.Windows.Forms.DockStyle.Fill;
            this._groupExposure.Location = new System.Drawing.Point(250, 3);
            this._groupExposure.Name = "_groupExposure";
            this._groupExposure.Size = new System.Drawing.Size(241, 149);
            this._groupExposure.TabIndex = 1;
            this._groupExposure.TabStop = false;
            this._groupExposure.Text = "曝光时间设置";
            // 
            // _lblExposureTime
            // 
            this._lblExposureTime.AutoSize = true;
            this._lblExposureTime.Location = new System.Drawing.Point(6, 22);
            this._lblExposureTime.Name = "_lblExposureTime";
            this._lblExposureTime.Size = new System.Drawing.Size(83, 12);
            this._lblExposureTime.TabIndex = 0;
            this._lblExposureTime.Text = "曝光时间(μs):";
            // 
            // _txtExposureTime
            // 
            this._txtExposureTime.Location = new System.Drawing.Point(8, 40);
            this._txtExposureTime.Name = "_txtExposureTime";
            this._txtExposureTime.Size = new System.Drawing.Size(100, 21);
            this._txtExposureTime.TabIndex = 1;
            this._txtExposureTime.Text = "10000";
            // 
            // _btnSetExposure
            // 
            this._btnSetExposure.Location = new System.Drawing.Point(114, 38);
            this._btnSetExposure.Name = "_btnSetExposure";
            this._btnSetExposure.Size = new System.Drawing.Size(50, 23);
            this._btnSetExposure.TabIndex = 2;
            this._btnSetExposure.Text = "设置";
            this._btnSetExposure.UseVisualStyleBackColor = true;
            // 
            // _btnSyncExposure
            // 
            this._btnSyncExposure.Location = new System.Drawing.Point(8, 67);
            this._btnSyncExposure.Name = "_btnSyncExposure";
            this._btnSyncExposure.Size = new System.Drawing.Size(120, 23);
            this._btnSyncExposure.TabIndex = 3;
            this._btnSyncExposure.Text = "同步到其他相机";
            this._btnSyncExposure.UseVisualStyleBackColor = true;
            // 
            // _lblExposureRange
            // 
            this._lblExposureRange.AutoSize = true;
            this._lblExposureRange.ForeColor = System.Drawing.Color.Gray;
            this._lblExposureRange.Location = new System.Drawing.Point(6, 100);
            this._lblExposureRange.Name = "_lblExposureRange";
            this._lblExposureRange.Size = new System.Drawing.Size(107, 12);
            this._lblExposureRange.TabIndex = 4;
            this._lblExposureRange.Text = "范围: 0~33000μs";
            // 
            // _groupWhiteBalance
            // 
            this._groupWhiteBalance.Controls.Add(this._btnAutoWhiteBalance);
            this._groupWhiteBalance.Controls.Add(this._btnSyncWhiteBalance);
            this._groupWhiteBalance.Controls.Add(this._lblRedRatio);
            this._groupWhiteBalance.Controls.Add(this._txtRedRatio);
            this._groupWhiteBalance.Controls.Add(this._lblGreenRatio);
            this._groupWhiteBalance.Controls.Add(this._txtGreenRatio);
            this._groupWhiteBalance.Controls.Add(this._lblBlueRatio);
            this._groupWhiteBalance.Controls.Add(this._txtBlueRatio);
            this._groupWhiteBalance.Controls.Add(this._lblRatioRange);
            this._groupWhiteBalance.Dock = System.Windows.Forms.DockStyle.Fill;
            this._groupWhiteBalance.Location = new System.Drawing.Point(497, 3);
            this._groupWhiteBalance.Name = "_groupWhiteBalance";
            this._groupWhiteBalance.Size = new System.Drawing.Size(241, 149);
            this._groupWhiteBalance.TabIndex = 2;
            this._groupWhiteBalance.TabStop = false;
            this._groupWhiteBalance.Text = "白平衡设置";
            // 
            // _btnAutoWhiteBalance
            // 
            this._btnAutoWhiteBalance.Location = new System.Drawing.Point(8, 19);
            this._btnAutoWhiteBalance.Name = "_btnAutoWhiteBalance";
            this._btnAutoWhiteBalance.Size = new System.Drawing.Size(100, 25);
            this._btnAutoWhiteBalance.TabIndex = 0;
            this._btnAutoWhiteBalance.Text = "一键白平衡";
            this._btnAutoWhiteBalance.UseVisualStyleBackColor = true;
            // 
            // _btnSyncWhiteBalance
            // 
            this._btnSyncWhiteBalance.Location = new System.Drawing.Point(114, 19);
            this._btnSyncWhiteBalance.Name = "_btnSyncWhiteBalance";
            this._btnSyncWhiteBalance.Size = new System.Drawing.Size(120, 25);
            this._btnSyncWhiteBalance.TabIndex = 1;
            this._btnSyncWhiteBalance.Text = "同步到其他相机";
            this._btnSyncWhiteBalance.UseVisualStyleBackColor = true;
            // 
            // _lblRedRatio
            // 
            this._lblRedRatio.AutoSize = true;
            this._lblRedRatio.Location = new System.Drawing.Point(6, 52);
            this._lblRedRatio.Name = "_lblRedRatio";
            this._lblRedRatio.Size = new System.Drawing.Size(59, 12);
            this._lblRedRatio.TabIndex = 2;
            this._lblRedRatio.Text = "红色(int):";
            // 
            // _txtRedRatio
            // 
            this._txtRedRatio.Location = new System.Drawing.Point(8, 67);
            this._txtRedRatio.Name = "_txtRedRatio";
            this._txtRedRatio.ReadOnly = true;
            this._txtRedRatio.Size = new System.Drawing.Size(60, 21);
            this._txtRedRatio.TabIndex = 3;
            // 
            // _lblGreenRatio
            // 
            this._lblGreenRatio.AutoSize = true;
            this._lblGreenRatio.Location = new System.Drawing.Point(72, 52);
            this._lblGreenRatio.Name = "_lblGreenRatio";
            this._lblGreenRatio.Size = new System.Drawing.Size(59, 12);
            this._lblGreenRatio.TabIndex = 4;
            this._lblGreenRatio.Text = "绿色(int):";
            // 
            // _txtGreenRatio
            // 
            this._txtGreenRatio.Location = new System.Drawing.Point(74, 67);
            this._txtGreenRatio.Name = "_txtGreenRatio";
            this._txtGreenRatio.ReadOnly = true;
            this._txtGreenRatio.Size = new System.Drawing.Size(60, 21);
            this._txtGreenRatio.TabIndex = 5;
            // 
            // _lblBlueRatio
            // 
            this._lblBlueRatio.AutoSize = true;
            this._lblBlueRatio.Location = new System.Drawing.Point(138, 52);
            this._lblBlueRatio.Name = "_lblBlueRatio";
            this._lblBlueRatio.Size = new System.Drawing.Size(59, 12);
            this._lblBlueRatio.TabIndex = 6;
            this._lblBlueRatio.Text = "蓝色(int):";
            // 
            // _txtBlueRatio
            // 
            this._txtBlueRatio.Location = new System.Drawing.Point(140, 67);
            this._txtBlueRatio.Name = "_txtBlueRatio";
            this._txtBlueRatio.ReadOnly = true;
            this._txtBlueRatio.Size = new System.Drawing.Size(60, 21);
            this._txtBlueRatio.TabIndex = 7;
            // 
            // _lblRatioRange
            // 
            this._lblRatioRange.AutoSize = true;
            this._lblRatioRange.ForeColor = System.Drawing.Color.Gray;
            this._lblRatioRange.Location = new System.Drawing.Point(6, 100);
            this._lblRatioRange.Name = "_lblRatioRange";
            this._lblRatioRange.Size = new System.Drawing.Size(119, 12);
            this._lblRatioRange.TabIndex = 8;
            this._lblRatioRange.Text = "比例值范围: 1~16376";
            // 
            // _groupROI
            // 
            this._groupROI.Controls.Add(this._lblOffsetX);
            this._groupROI.Controls.Add(this._txtOffsetX);
            this._groupROI.Controls.Add(this._lblOffsetY);
            this._groupROI.Controls.Add(this._txtOffsetY);
            this._groupROI.Controls.Add(this._lblWidth);
            this._groupROI.Controls.Add(this._txtWidth);
            this._groupROI.Controls.Add(this._lblHeight);
            this._groupROI.Controls.Add(this._txtHeight);
            this._groupROI.Controls.Add(this._btnSetROI);
            this._groupROI.Controls.Add(this._btnSyncROI);
            this._groupROI.Controls.Add(this._btnRestoreROI);
            this._groupROI.Controls.Add(this._lblROITip);
            this._groupROI.Dock = System.Windows.Forms.DockStyle.Fill;
            this._groupROI.Location = new System.Drawing.Point(744, 3);
            this._groupROI.Name = "_groupROI";
            this._groupROI.Size = new System.Drawing.Size(243, 149);
            this._groupROI.TabIndex = 3;
            this._groupROI.TabStop = false;
            this._groupROI.Text = "ROI设置";
            // 
            // _lblOffsetX
            // 
            this._lblOffsetX.AutoSize = true;
            this._lblOffsetX.Location = new System.Drawing.Point(6, 22);
            this._lblOffsetX.Name = "_lblOffsetX";
            this._lblOffsetX.Size = new System.Drawing.Size(41, 12);
            this._lblOffsetX.TabIndex = 0;
            this._lblOffsetX.Text = "X偏移:";
            // 
            // _txtOffsetX
            // 
            this._txtOffsetX.Location = new System.Drawing.Point(48, 19);
            this._txtOffsetX.Name = "_txtOffsetX";
            this._txtOffsetX.Size = new System.Drawing.Size(60, 21);
            this._txtOffsetX.TabIndex = 1;
            // 
            // _lblOffsetY
            // 
            this._lblOffsetY.AutoSize = true;
            this._lblOffsetY.Location = new System.Drawing.Point(114, 22);
            this._lblOffsetY.Name = "_lblOffsetY";
            this._lblOffsetY.Size = new System.Drawing.Size(41, 12);
            this._lblOffsetY.TabIndex = 2;
            this._lblOffsetY.Text = "Y偏移:";
            // 
            // _txtOffsetY
            // 
            this._txtOffsetY.Location = new System.Drawing.Point(156, 19);
            this._txtOffsetY.Name = "_txtOffsetY";
            this._txtOffsetY.Size = new System.Drawing.Size(60, 21);
            this._txtOffsetY.TabIndex = 3;
            // 
            // _lblWidth
            // 
            this._lblWidth.AutoSize = true;
            this._lblWidth.Location = new System.Drawing.Point(6, 49);
            this._lblWidth.Name = "_lblWidth";
            this._lblWidth.Size = new System.Drawing.Size(29, 12);
            this._lblWidth.TabIndex = 4;
            this._lblWidth.Text = "宽度:";
            // 
            // _txtWidth
            // 
            this._txtWidth.Location = new System.Drawing.Point(48, 46);
            this._txtWidth.Name = "_txtWidth";
            this._txtWidth.Size = new System.Drawing.Size(60, 21);
            this._txtWidth.TabIndex = 5;
            // 
            // _lblHeight
            // 
            this._lblHeight.AutoSize = true;
            this._lblHeight.Location = new System.Drawing.Point(114, 49);
            this._lblHeight.Name = "_lblHeight";
            this._lblHeight.Size = new System.Drawing.Size(29, 12);
            this._lblHeight.TabIndex = 6;
            this._lblHeight.Text = "高度:";
            // 
            // _txtHeight
            // 
            this._txtHeight.Location = new System.Drawing.Point(156, 46);
            this._txtHeight.Name = "_txtHeight";
            this._txtHeight.Size = new System.Drawing.Size(60, 21);
            this._txtHeight.TabIndex = 7;
            // 
            // _btnSetROI
            // 
            this._btnSetROI.Location = new System.Drawing.Point(8, 73);
            this._btnSetROI.Name = "_btnSetROI";
            this._btnSetROI.Size = new System.Drawing.Size(70, 23);
            this._btnSetROI.TabIndex = 8;
            this._btnSetROI.Text = "设置ROI";
            this._btnSetROI.UseVisualStyleBackColor = true;
            // 
            // _btnSyncROI
            // 
            this._btnSyncROI.Location = new System.Drawing.Point(84, 73);
            this._btnSyncROI.Name = "_btnSyncROI";
            this._btnSyncROI.Size = new System.Drawing.Size(70, 23);
            this._btnSyncROI.TabIndex = 9;
            this._btnSyncROI.Text = "同步ROI";
            this._btnSyncROI.UseVisualStyleBackColor = true;
            // 
            // _btnRestoreROI
            // 
            this._btnRestoreROI.Location = new System.Drawing.Point(160, 73);
            this._btnRestoreROI.Name = "_btnRestoreROI";
            this._btnRestoreROI.Size = new System.Drawing.Size(70, 23);
            this._btnRestoreROI.TabIndex = 10;
            this._btnRestoreROI.Text = "还原ROI";
            this._btnRestoreROI.UseVisualStyleBackColor = true;
            // 
            // _lblROITip
            // 
            this._lblROITip.AutoSize = true;
            this._lblROITip.Font = new System.Drawing.Font("宋体", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this._lblROITip.ForeColor = System.Drawing.Color.Gray;
            this._lblROITip.Location = new System.Drawing.Point(6, 106);
            this._lblROITip.Name = "_lblROITip";
            this._lblROITip.Size = new System.Drawing.Size(165, 11);
            this._lblROITip.TabIndex = 11;
            this._lblROITip.Text = "提示: 点击设置ROI可画框选择区域";
            // 
            // _pictureBox
            // 
            this._pictureBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._pictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this._pictureBox.Location = new System.Drawing.Point(3, 168);
            this._pictureBox.Name = "_pictureBox";
            this._pictureBox.Size = new System.Drawing.Size(994, 529);
            this._pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this._pictureBox.TabIndex = 2;
            this._pictureBox.TabStop = false;
            // 
            // CameraTabView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.mainLayout);
            this.Name = "CameraTabView";
            this.Size = new System.Drawing.Size(1000, 700);
            this.mainLayout.ResumeLayout(false);
            this.controlsPanel.ResumeLayout(false);
            this._groupCamera.ResumeLayout(false);
            this._groupCamera.PerformLayout();
            this._groupExposure.ResumeLayout(false);
            this._groupExposure.PerformLayout();
            this._groupWhiteBalance.ResumeLayout(false);
            this._groupWhiteBalance.PerformLayout();
            this._groupROI.ResumeLayout(false);
            this._groupROI.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this._pictureBox)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel mainLayout;
        private System.Windows.Forms.TableLayoutPanel controlsPanel;
        private System.Windows.Forms.GroupBox _groupCamera;
        private System.Windows.Forms.Label _lblTriggerMode;
        private System.Windows.Forms.ComboBox _comboTriggerMode;
        private System.Windows.Forms.Button _btnStartGrab;
        private System.Windows.Forms.Button _btnStopGrab;
        private System.Windows.Forms.Button _btnTriggerOnce;
        private System.Windows.Forms.Label _lblInterval;
        private System.Windows.Forms.TextBox _txtInterval;
        private System.Windows.Forms.Button _btnStartContinuous;
        private System.Windows.Forms.Button _btnStopContinuous;
        private System.Windows.Forms.GroupBox _groupExposure;
        private System.Windows.Forms.Label _lblExposureTime;
        internal System.Windows.Forms.TextBox _txtExposureTime;
        private System.Windows.Forms.Button _btnSetExposure;
        private System.Windows.Forms.Button _btnSyncExposure;
        private System.Windows.Forms.Label _lblExposureRange;
        private System.Windows.Forms.GroupBox _groupWhiteBalance;
        private System.Windows.Forms.Button _btnAutoWhiteBalance;
        private System.Windows.Forms.Button _btnSyncWhiteBalance;
        private System.Windows.Forms.Label _lblRedRatio;
        internal System.Windows.Forms.TextBox _txtRedRatio;
        private System.Windows.Forms.Label _lblGreenRatio;
        internal System.Windows.Forms.TextBox _txtGreenRatio;
        private System.Windows.Forms.Label _lblBlueRatio;
        internal System.Windows.Forms.TextBox _txtBlueRatio;
        private System.Windows.Forms.Label _lblRatioRange;
        private System.Windows.Forms.GroupBox _groupROI;
        private System.Windows.Forms.Label _lblOffsetX;
        internal System.Windows.Forms.TextBox _txtOffsetX;
        private System.Windows.Forms.Label _lblOffsetY;
        internal System.Windows.Forms.TextBox _txtOffsetY;
        private System.Windows.Forms.Label _lblWidth;
        internal System.Windows.Forms.TextBox _txtWidth;
        private System.Windows.Forms.Label _lblHeight;
        internal System.Windows.Forms.TextBox _txtHeight;
        private System.Windows.Forms.Button _btnSetROI;
        private System.Windows.Forms.Button _btnSyncROI;
        private System.Windows.Forms.Button _btnRestoreROI;
        private System.Windows.Forms.Label _lblROITip;
        private System.Windows.Forms.PictureBox _pictureBox;
    }
} 