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
            this.tabControl = new System.Windows.Forms.TabControl();
            this.tabImage = new System.Windows.Forms.TabPage();
            this.tabCamera = new System.Windows.Forms.TabPage();
            this.groupCameraControl = new System.Windows.Forms.GroupBox();
            this.btnInferLive = new System.Windows.Forms.Button();
            this.btnStartLive = new System.Windows.Forms.Button();
            this.btnConnectCamera = new System.Windows.Forms.Button();
            this.cmbCameraDevice = new System.Windows.Forms.ComboBox();
            this.lblCameraDevice = new System.Windows.Forms.Label();
            this.btnRefreshCameras = new System.Windows.Forms.Button();
            this.cmbCameraInterface = new System.Windows.Forms.ComboBox();
            this.lblCameraInterface = new System.Windows.Forms.Label();
            this.panelTop = new System.Windows.Forms.Panel();
            this.panel1.SuspendLayout();
            this.tabControl.SuspendLayout();
            this.tabImage.SuspendLayout();
            this.tabCamera.SuspendLayout();
            this.groupCameraControl.SuspendLayout();
            this.panelTop.SuspendLayout();
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
            this.hWindowControl.Location = new System.Drawing.Point(18, 92);
            this.hWindowControl.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.hWindowControl.Name = "hWindowControl";
            this.hWindowControl.Size = new System.Drawing.Size(1164, 411);
            this.hWindowControl.TabIndex = 0;
            this.hWindowControl.WindowSize = new System.Drawing.Size(1164, 411);
            this.hWindowControl.SizeChanged += new System.EventHandler(this.hWindowControl_SizeChanged);
            this.hWindowControl.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.hWindowControl_MouseWheel);
            // 
            // btnSelectImage
            // 
            this.btnSelectImage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSelectImage.Location = new System.Drawing.Point(888, 21);
            this.btnSelectImage.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnSelectImage.Name = "btnSelectImage";
            this.btnSelectImage.Size = new System.Drawing.Size(112, 34);
            this.btnSelectImage.TabIndex = 1;
            this.btnSelectImage.Text = "选择图像";
            this.btnSelectImage.UseVisualStyleBackColor = true;
            this.btnSelectImage.Click += new System.EventHandler(this.btnSelectImage_Click);
            // 
            // btnSelectModel
            // 
            this.btnSelectModel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSelectModel.Location = new System.Drawing.Point(1028, 22);
            this.btnSelectModel.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnSelectModel.Name = "btnSelectModel";
            this.btnSelectModel.Size = new System.Drawing.Size(128, 34);
            this.btnSelectModel.TabIndex = 2;
            this.btnSelectModel.Text = "选择模型";
            this.btnSelectModel.UseVisualStyleBackColor = true;
            this.btnSelectModel.Click += new System.EventHandler(this.btnSelectModel_Click);
            // 
            // btnInfer
            // 
            this.btnInfer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnInfer.Location = new System.Drawing.Point(1008, 21);
            this.btnInfer.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnInfer.Name = "btnInfer";
            this.btnInfer.Size = new System.Drawing.Size(128, 34);
            this.btnInfer.TabIndex = 3;
            this.btnInfer.Text = "开始推理";
            this.btnInfer.UseVisualStyleBackColor = true;
            this.btnInfer.Click += new System.EventHandler(this.btnInfer_Click);
            // 
            // lblImagePath
            // 
            this.lblImagePath.AutoSize = true;
            this.lblImagePath.Location = new System.Drawing.Point(9, 26);
            this.lblImagePath.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblImagePath.Name = "lblImagePath";
            this.lblImagePath.Size = new System.Drawing.Size(98, 18);
            this.lblImagePath.TabIndex = 4;
            this.lblImagePath.Text = "图像路径：";
            // 
            // lblModelPath
            // 
            this.lblModelPath.AutoSize = true;
            this.lblModelPath.Location = new System.Drawing.Point(18, 27);
            this.lblModelPath.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblModelPath.Name = "lblModelPath";
            this.lblModelPath.Size = new System.Drawing.Size(98, 18);
            this.lblModelPath.TabIndex = 5;
            this.lblModelPath.Text = "模型路径：";
            // 
            // txtImagePath
            // 
            this.txtImagePath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtImagePath.Location = new System.Drawing.Point(116, 21);
            this.txtImagePath.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtImagePath.Name = "txtImagePath";
            this.txtImagePath.Size = new System.Drawing.Size(764, 28);
            this.txtImagePath.TabIndex = 6;
            // 
            // txtModelPath
            // 
            this.txtModelPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModelPath.Location = new System.Drawing.Point(124, 22);
            this.txtModelPath.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtModelPath.Name = "txtModelPath";
            this.txtModelPath.Size = new System.Drawing.Size(892, 28);
            this.txtModelPath.TabIndex = 7;
            // 
            // panel1
            // 
            this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(this.lblResult);
            this.panel1.Controls.Add(this.tabControl);
            this.panel1.Location = new System.Drawing.Point(18, 512);
            this.panel1.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1163, 190);
            this.panel1.TabIndex = 8;
            // 
            // lblResult
            // 
            this.lblResult.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblResult.AutoSize = true;
            this.lblResult.Location = new System.Drawing.Point(9, 158);
            this.lblResult.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblResult.Name = "lblResult";
            this.lblResult.Size = new System.Drawing.Size(98, 18);
            this.lblResult.TabIndex = 8;
            this.lblResult.Text = "推理结果：";
            // 
            // tabControl
            // 
            this.tabControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl.Controls.Add(this.tabImage);
            this.tabControl.Controls.Add(this.tabCamera);
            this.tabControl.Location = new System.Drawing.Point(4, 4);
            this.tabControl.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(1152, 148);
            this.tabControl.TabIndex = 9;
            // 
            // tabImage
            // 
            this.tabImage.Controls.Add(this.lblImagePath);
            this.tabImage.Controls.Add(this.txtImagePath);
            this.tabImage.Controls.Add(this.btnSelectImage);
            this.tabImage.Controls.Add(this.btnInfer);
            this.tabImage.Location = new System.Drawing.Point(4, 28);
            this.tabImage.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.tabImage.Name = "tabImage";
            this.tabImage.Padding = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.tabImage.Size = new System.Drawing.Size(1144, 116);
            this.tabImage.TabIndex = 0;
            this.tabImage.Text = "图像";
            this.tabImage.UseVisualStyleBackColor = true;
            // 
            // tabCamera
            // 
            this.tabCamera.Controls.Add(this.groupCameraControl);
            this.tabCamera.Location = new System.Drawing.Point(4, 28);
            this.tabCamera.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.tabCamera.Name = "tabCamera";
            this.tabCamera.Padding = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.tabCamera.Size = new System.Drawing.Size(1144, 116);
            this.tabCamera.TabIndex = 1;
            this.tabCamera.Text = "摄像机";
            this.tabCamera.UseVisualStyleBackColor = true;
            // 
            // groupCameraControl
            // 
            this.groupCameraControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupCameraControl.Controls.Add(this.btnInferLive);
            this.groupCameraControl.Controls.Add(this.btnStartLive);
            this.groupCameraControl.Controls.Add(this.btnConnectCamera);
            this.groupCameraControl.Controls.Add(this.cmbCameraDevice);
            this.groupCameraControl.Controls.Add(this.lblCameraDevice);
            this.groupCameraControl.Controls.Add(this.btnRefreshCameras);
            this.groupCameraControl.Controls.Add(this.cmbCameraInterface);
            this.groupCameraControl.Controls.Add(this.lblCameraInterface);
            this.groupCameraControl.Location = new System.Drawing.Point(9, 9);
            this.groupCameraControl.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.groupCameraControl.Name = "groupCameraControl";
            this.groupCameraControl.Padding = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.groupCameraControl.Size = new System.Drawing.Size(1122, 92);
            this.groupCameraControl.TabIndex = 0;
            this.groupCameraControl.TabStop = false;
            this.groupCameraControl.Text = "摄像机控制";
            // 
            // btnInferLive
            // 
            this.btnInferLive.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnInferLive.Enabled = false;
            this.btnInferLive.Location = new System.Drawing.Point(986, 30);
            this.btnInferLive.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnInferLive.Name = "btnInferLive";
            this.btnInferLive.Size = new System.Drawing.Size(128, 34);
            this.btnInferLive.TabIndex = 7;
            this.btnInferLive.Text = "实时推理";
            this.btnInferLive.UseVisualStyleBackColor = true;
            this.btnInferLive.Click += new System.EventHandler(this.btnInferLive_Click);
            // 
            // btnStartLive
            // 
            this.btnStartLive.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnStartLive.Enabled = false;
            this.btnStartLive.Location = new System.Drawing.Point(864, 30);
            this.btnStartLive.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnStartLive.Name = "btnStartLive";
            this.btnStartLive.Size = new System.Drawing.Size(112, 34);
            this.btnStartLive.TabIndex = 6;
            this.btnStartLive.Text = "开始实时";
            this.btnStartLive.UseVisualStyleBackColor = true;
            this.btnStartLive.Click += new System.EventHandler(this.btnStartLive_Click);
            // 
            // btnConnectCamera
            // 
            this.btnConnectCamera.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnConnectCamera.Location = new System.Drawing.Point(742, 30);
            this.btnConnectCamera.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnConnectCamera.Name = "btnConnectCamera";
            this.btnConnectCamera.Size = new System.Drawing.Size(112, 34);
            this.btnConnectCamera.TabIndex = 5;
            this.btnConnectCamera.Text = "连接";
            this.btnConnectCamera.UseVisualStyleBackColor = true;
            this.btnConnectCamera.Click += new System.EventHandler(this.btnConnectCamera_Click);
            // 
            // cmbCameraDevice
            // 
            this.cmbCameraDevice.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbCameraDevice.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbCameraDevice.FormattingEnabled = true;
            this.cmbCameraDevice.Location = new System.Drawing.Point(408, 32);
            this.cmbCameraDevice.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.cmbCameraDevice.Name = "cmbCameraDevice";
            this.cmbCameraDevice.Size = new System.Drawing.Size(324, 26);
            this.cmbCameraDevice.TabIndex = 4;
            // 
            // lblCameraDevice
            // 
            this.lblCameraDevice.AutoSize = true;
            this.lblCameraDevice.Location = new System.Drawing.Point(338, 36);
            this.lblCameraDevice.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblCameraDevice.Name = "lblCameraDevice";
            this.lblCameraDevice.Size = new System.Drawing.Size(62, 18);
            this.lblCameraDevice.TabIndex = 3;
            this.lblCameraDevice.Text = "设备：";
            // 
            // btnRefreshCameras
            // 
            this.btnRefreshCameras.Location = new System.Drawing.Point(270, 30);
            this.btnRefreshCameras.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnRefreshCameras.Name = "btnRefreshCameras";
            this.btnRefreshCameras.Size = new System.Drawing.Size(58, 34);
            this.btnRefreshCameras.TabIndex = 2;
            this.btnRefreshCameras.Text = "刷新";
            this.btnRefreshCameras.UseVisualStyleBackColor = true;
            this.btnRefreshCameras.Click += new System.EventHandler(this.btnRefreshCameras_Click);
            // 
            // cmbCameraInterface
            // 
            this.cmbCameraInterface.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbCameraInterface.FormattingEnabled = true;
            this.cmbCameraInterface.Location = new System.Drawing.Point(80, 32);
            this.cmbCameraInterface.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.cmbCameraInterface.Name = "cmbCameraInterface";
            this.cmbCameraInterface.Size = new System.Drawing.Size(180, 26);
            this.cmbCameraInterface.TabIndex = 1;
            this.cmbCameraInterface.SelectedIndexChanged += new System.EventHandler(this.cmbCameraInterface_SelectedIndexChanged);
            // 
            // lblCameraInterface
            // 
            this.lblCameraInterface.AutoSize = true;
            this.lblCameraInterface.Location = new System.Drawing.Point(9, 36);
            this.lblCameraInterface.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblCameraInterface.Name = "lblCameraInterface";
            this.lblCameraInterface.Size = new System.Drawing.Size(62, 18);
            this.lblCameraInterface.TabIndex = 0;
            this.lblCameraInterface.Text = "接口：";
            // 
            // panelTop
            // 
            this.panelTop.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelTop.Controls.Add(this.lblModelPath);
            this.panelTop.Controls.Add(this.txtModelPath);
            this.panelTop.Controls.Add(this.btnSelectModel);
            this.panelTop.Location = new System.Drawing.Point(18, 18);
            this.panelTop.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(1164, 66);
            this.panelTop.TabIndex = 9;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 720);
            this.Controls.Add(this.panelTop);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.hWindowControl);
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.MinimumSize = new System.Drawing.Size(1213, 750);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Halcon推理演示";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.Resize += new System.EventHandler(this.Form1_Resize);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.tabControl.ResumeLayout(false);
            this.tabImage.ResumeLayout(false);
            this.tabImage.PerformLayout();
            this.tabCamera.ResumeLayout(false);
            this.groupCameraControl.ResumeLayout(false);
            this.groupCameraControl.PerformLayout();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
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
        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage tabImage;
        private System.Windows.Forms.TabPage tabCamera;
        private System.Windows.Forms.GroupBox groupCameraControl;
        private System.Windows.Forms.Button btnInferLive;
        private System.Windows.Forms.Button btnStartLive;
        private System.Windows.Forms.Button btnConnectCamera;
        private System.Windows.Forms.ComboBox cmbCameraDevice;
        private System.Windows.Forms.Label lblCameraDevice;
        private System.Windows.Forms.Button btnRefreshCameras;
        private System.Windows.Forms.ComboBox cmbCameraInterface;
        private System.Windows.Forms.Label lblCameraInterface;
        private System.Windows.Forms.Panel panelTop;
    }
}

