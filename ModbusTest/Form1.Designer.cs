﻿namespace ModbusTest
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
            this.gbSerialPort = new System.Windows.Forms.GroupBox();
            this.btnConnect = new System.Windows.Forms.Button();
            this.btnRefreshPorts = new System.Windows.Forms.Button();
            this.lblDeviceId = new System.Windows.Forms.Label();
            this.lblPort = new System.Windows.Forms.Label();
            this.cmbPort = new System.Windows.Forms.ComboBox();
            this.lblBaudRate = new System.Windows.Forms.Label();
            this.cmbBaudRate = new System.Windows.Forms.ComboBox();
            this.lblDataBits = new System.Windows.Forms.Label();
            this.cmbDataBits = new System.Windows.Forms.ComboBox();
            this.lblStopBits = new System.Windows.Forms.Label();
            this.cmbStopBits = new System.Windows.Forms.ComboBox();
            this.lblParity = new System.Windows.Forms.Label();
            this.cmbParity = new System.Windows.Forms.ComboBox();
            this.nudDeviceId = new System.Windows.Forms.NumericUpDown();
            this.gbRegister = new System.Windows.Forms.GroupBox();
            this.lblAddress = new System.Windows.Forms.Label();
            this.nudAddress = new System.Windows.Forms.NumericUpDown();
            this.lblOperation = new System.Windows.Forms.Label();
            this.cmbOperation = new System.Windows.Forms.ComboBox();
            this.lblValue = new System.Windows.Forms.Label();
            this.nudValue = new System.Windows.Forms.NumericUpDown();
            this.chkHex = new System.Windows.Forms.CheckBox();
            this.btnSend = new System.Windows.Forms.Button();
            this.gbDirectOperations = new System.Windows.Forms.GroupBox();
            this.lblRegValue = new System.Windows.Forms.Label();
            this.txtRegValue = new System.Windows.Forms.TextBox();
            this.btnReadReg = new System.Windows.Forms.Button();
            this.btnWriteReg = new System.Windows.Forms.Button();
            this.lblFloatValue = new System.Windows.Forms.Label();
            this.txtFloatValue = new System.Windows.Forms.TextBox();
            this.btnReadFloat = new System.Windows.Forms.Button();
            this.btnWriteFloat = new System.Windows.Forms.Button();
            this.lblIntValue = new System.Windows.Forms.Label();
            this.txtIntValue = new System.Windows.Forms.TextBox();
            this.btnReadInt = new System.Windows.Forms.Button();
            this.btnWriteInt = new System.Windows.Forms.Button();
            this.gbLog = new System.Windows.Forms.GroupBox();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.btnClearLog = new System.Windows.Forms.Button();
            this.gbSerialPort.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudDeviceId)).BeginInit();
            this.gbRegister.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudAddress)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudValue)).BeginInit();
            this.gbDirectOperations.SuspendLayout();
            this.gbLog.SuspendLayout();
            this.SuspendLayout();
            // 
            // gbSerialPort
            // 
            this.gbSerialPort.Controls.Add(this.btnConnect);
            this.gbSerialPort.Controls.Add(this.btnRefreshPorts);
            this.gbSerialPort.Controls.Add(this.lblDeviceId);
            this.gbSerialPort.Controls.Add(this.lblPort);
            this.gbSerialPort.Controls.Add(this.cmbPort);
            this.gbSerialPort.Controls.Add(this.lblBaudRate);
            this.gbSerialPort.Controls.Add(this.cmbBaudRate);
            this.gbSerialPort.Controls.Add(this.lblDataBits);
            this.gbSerialPort.Controls.Add(this.cmbDataBits);
            this.gbSerialPort.Controls.Add(this.lblStopBits);
            this.gbSerialPort.Controls.Add(this.cmbStopBits);
            this.gbSerialPort.Controls.Add(this.lblParity);
            this.gbSerialPort.Controls.Add(this.cmbParity);
            this.gbSerialPort.Controls.Add(this.nudDeviceId);
            this.gbSerialPort.Location = new System.Drawing.Point(15, 15);
            this.gbSerialPort.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbSerialPort.Name = "gbSerialPort";
            this.gbSerialPort.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbSerialPort.Size = new System.Drawing.Size(330, 331);
            this.gbSerialPort.TabIndex = 0;
            this.gbSerialPort.TabStop = false;
            this.gbSerialPort.Text = "串口设置";
            // 
            // btnConnect
            // 
            this.btnConnect.Location = new System.Drawing.Point(5, 274);
            this.btnConnect.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(150, 40);
            this.btnConnect.TabIndex = 13;
            this.btnConnect.Text = "打开串口";
            this.btnConnect.UseVisualStyleBackColor = true;
            this.btnConnect.Click += new System.EventHandler(this.BtnConnect_Click);
            // 
            // btnRefreshPorts
            // 
            this.btnRefreshPorts.Location = new System.Drawing.Point(165, 274);
            this.btnRefreshPorts.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnRefreshPorts.Name = "btnRefreshPorts";
            this.btnRefreshPorts.Size = new System.Drawing.Size(150, 40);
            this.btnRefreshPorts.TabIndex = 14;
            this.btnRefreshPorts.Text = "刷新串口";
            this.btnRefreshPorts.UseVisualStyleBackColor = true;
            this.btnRefreshPorts.Click += new System.EventHandler(this.BtnRefreshPorts_Click);
            // 
            // lblDeviceId
            // 
            this.lblDeviceId.AutoSize = true;
            this.lblDeviceId.Location = new System.Drawing.Point(15, 236);
            this.lblDeviceId.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblDeviceId.Name = "lblDeviceId";
            this.lblDeviceId.Size = new System.Drawing.Size(98, 22);
            this.lblDeviceId.TabIndex = 12;
            this.lblDeviceId.Text = "设备ID：";
            // 
            // lblPort
            // 
            this.lblPort.AutoSize = true;
            this.lblPort.Location = new System.Drawing.Point(15, 35);
            this.lblPort.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(76, 22);
            this.lblPort.TabIndex = 0;
            this.lblPort.Text = "串口：";
            // 
            // cmbPort
            // 
            this.cmbPort.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPort.FormattingEnabled = true;
            this.cmbPort.Location = new System.Drawing.Point(120, 32);
            this.cmbPort.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbPort.Name = "cmbPort";
            this.cmbPort.Size = new System.Drawing.Size(195, 30);
            this.cmbPort.TabIndex = 1;
            this.cmbPort.SelectedIndexChanged += new System.EventHandler(this.cmbPort_SelectedIndexChanged);
            // 
            // lblBaudRate
            // 
            this.lblBaudRate.AutoSize = true;
            this.lblBaudRate.Location = new System.Drawing.Point(15, 75);
            this.lblBaudRate.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblBaudRate.Name = "lblBaudRate";
            this.lblBaudRate.Size = new System.Drawing.Size(98, 22);
            this.lblBaudRate.TabIndex = 2;
            this.lblBaudRate.Text = "波特率：";
            // 
            // cmbBaudRate
            // 
            this.cmbBaudRate.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbBaudRate.FormattingEnabled = true;
            this.cmbBaudRate.Items.AddRange(new object[] {
            4800,
            9600,
            19200,
            38400,
            57600,
            115200});
            this.cmbBaudRate.Location = new System.Drawing.Point(121, 72);
            this.cmbBaudRate.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbBaudRate.Name = "cmbBaudRate";
            this.cmbBaudRate.Size = new System.Drawing.Size(194, 30);
            this.cmbBaudRate.TabIndex = 3;
            // 
            // lblDataBits
            // 
            this.lblDataBits.AutoSize = true;
            this.lblDataBits.Location = new System.Drawing.Point(15, 115);
            this.lblDataBits.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblDataBits.Name = "lblDataBits";
            this.lblDataBits.Size = new System.Drawing.Size(98, 22);
            this.lblDataBits.TabIndex = 4;
            this.lblDataBits.Text = "数据位：";
            // 
            // cmbDataBits
            // 
            this.cmbDataBits.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDataBits.FormattingEnabled = true;
            this.cmbDataBits.Items.AddRange(new object[] {
            7,
            8});
            this.cmbDataBits.Location = new System.Drawing.Point(121, 112);
            this.cmbDataBits.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbDataBits.Name = "cmbDataBits";
            this.cmbDataBits.Size = new System.Drawing.Size(194, 30);
            this.cmbDataBits.TabIndex = 5;
            // 
            // lblStopBits
            // 
            this.lblStopBits.AutoSize = true;
            this.lblStopBits.Location = new System.Drawing.Point(15, 155);
            this.lblStopBits.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblStopBits.Name = "lblStopBits";
            this.lblStopBits.Size = new System.Drawing.Size(98, 22);
            this.lblStopBits.TabIndex = 6;
            this.lblStopBits.Text = "停止位：";
            // 
            // cmbStopBits
            // 
            this.cmbStopBits.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbStopBits.FormattingEnabled = true;
            this.cmbStopBits.Items.AddRange(new object[] {
            "One",
            "Two"});
            this.cmbStopBits.Location = new System.Drawing.Point(121, 152);
            this.cmbStopBits.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbStopBits.Name = "cmbStopBits";
            this.cmbStopBits.Size = new System.Drawing.Size(194, 30);
            this.cmbStopBits.TabIndex = 7;
            // 
            // lblParity
            // 
            this.lblParity.AutoSize = true;
            this.lblParity.Location = new System.Drawing.Point(15, 195);
            this.lblParity.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblParity.Name = "lblParity";
            this.lblParity.Size = new System.Drawing.Size(98, 22);
            this.lblParity.TabIndex = 8;
            this.lblParity.Text = "校验位：";
            // 
            // cmbParity
            // 
            this.cmbParity.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbParity.FormattingEnabled = true;
            this.cmbParity.Items.AddRange(new object[] {
            "None",
            "Odd",
            "Even"});
            this.cmbParity.Location = new System.Drawing.Point(120, 192);
            this.cmbParity.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbParity.Name = "cmbParity";
            this.cmbParity.Size = new System.Drawing.Size(195, 30);
            this.cmbParity.TabIndex = 9;
            // 
            // nudDeviceId
            // 
            this.nudDeviceId.Location = new System.Drawing.Point(121, 234);
            this.nudDeviceId.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.nudDeviceId.Maximum = new decimal(new int[] {
            255,
            0,
            0,
            0});
            this.nudDeviceId.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.nudDeviceId.Name = "nudDeviceId";
            this.nudDeviceId.Size = new System.Drawing.Size(80, 32);
            this.nudDeviceId.TabIndex = 11;
            this.nudDeviceId.Value = new decimal(new int[] {
            2,
            0,
            0,
            0});
            // 
            // gbRegister
            // 
            this.gbRegister.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.gbRegister.Controls.Add(this.lblAddress);
            this.gbRegister.Controls.Add(this.nudAddress);
            this.gbRegister.Controls.Add(this.lblOperation);
            this.gbRegister.Controls.Add(this.cmbOperation);
            this.gbRegister.Controls.Add(this.lblValue);
            this.gbRegister.Controls.Add(this.nudValue);
            this.gbRegister.Controls.Add(this.chkHex);
            this.gbRegister.Controls.Add(this.btnSend);
            this.gbRegister.Location = new System.Drawing.Point(590, 29);
            this.gbRegister.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbRegister.Name = "gbRegister";
            this.gbRegister.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbRegister.Size = new System.Drawing.Size(294, 203);
            this.gbRegister.TabIndex = 3;
            this.gbRegister.TabStop = false;
            this.gbRegister.Text = "寄存器操作";
            // 
            // lblAddress
            // 
            this.lblAddress.AutoSize = true;
            this.lblAddress.Location = new System.Drawing.Point(8, 34);
            this.lblAddress.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblAddress.Name = "lblAddress";
            this.lblAddress.Size = new System.Drawing.Size(76, 22);
            this.lblAddress.TabIndex = 0;
            this.lblAddress.Text = "地址：";
            // 
            // nudAddress
            // 
            this.nudAddress.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.nudAddress.Hexadecimal = true;
            this.nudAddress.Location = new System.Drawing.Point(92, 32);
            this.nudAddress.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.nudAddress.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.nudAddress.Name = "nudAddress";
            this.nudAddress.Size = new System.Drawing.Size(182, 32);
            this.nudAddress.TabIndex = 1;
            this.nudAddress.Value = new decimal(new int[] {
            1280,
            0,
            0,
            0});
            // 
            // lblOperation
            // 
            this.lblOperation.AutoSize = true;
            this.lblOperation.Location = new System.Drawing.Point(9, 75);
            this.lblOperation.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblOperation.Name = "lblOperation";
            this.lblOperation.Size = new System.Drawing.Size(76, 22);
            this.lblOperation.TabIndex = 2;
            this.lblOperation.Text = "操作：";
            // 
            // cmbOperation
            // 
            this.cmbOperation.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbOperation.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbOperation.FormattingEnabled = true;
            this.cmbOperation.Items.AddRange(new object[] {
            "读线圈寄存器(01H)",
            "写单个线圈寄存器(05H)"});
            this.cmbOperation.Location = new System.Drawing.Point(92, 72);
            this.cmbOperation.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbOperation.Name = "cmbOperation";
            this.cmbOperation.Size = new System.Drawing.Size(182, 30);
            this.cmbOperation.TabIndex = 3;
            // 
            // lblValue
            // 
            this.lblValue.AutoSize = true;
            this.lblValue.Location = new System.Drawing.Point(9, 114);
            this.lblValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblValue.Name = "lblValue";
            this.lblValue.Size = new System.Drawing.Size(54, 22);
            this.lblValue.TabIndex = 4;
            this.lblValue.Text = "值：";
            // 
            // nudValue
            // 
            this.nudValue.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.nudValue.Hexadecimal = true;
            this.nudValue.Location = new System.Drawing.Point(92, 112);
            this.nudValue.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.nudValue.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.nudValue.Name = "nudValue";
            this.nudValue.Size = new System.Drawing.Size(182, 32);
            this.nudValue.TabIndex = 5;
            // 
            // chkHex
            // 
            this.chkHex.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.chkHex.AutoSize = true;
            this.chkHex.Checked = true;
            this.chkHex.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkHex.Location = new System.Drawing.Point(13, 154);
            this.chkHex.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.chkHex.Name = "chkHex";
            this.chkHex.Size = new System.Drawing.Size(124, 26);
            this.chkHex.TabIndex = 6;
            this.chkHex.Text = "十六进制";
            this.chkHex.UseVisualStyleBackColor = true;
            this.chkHex.CheckedChanged += new System.EventHandler(this.chkHex_CheckedChanged);
            // 
            // btnSend
            // 
            this.btnSend.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSend.Location = new System.Drawing.Point(145, 150);
            this.btnSend.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(129, 40);
            this.btnSend.TabIndex = 7;
            this.btnSend.Text = "发送";
            this.btnSend.UseVisualStyleBackColor = true;
            this.btnSend.Click += new System.EventHandler(this.BtnSend_Click);
            // 
            // gbDirectOperations
            // 
            this.gbDirectOperations.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.gbDirectOperations.Controls.Add(this.lblRegValue);
            this.gbDirectOperations.Controls.Add(this.txtRegValue);
            this.gbDirectOperations.Controls.Add(this.btnReadReg);
            this.gbDirectOperations.Controls.Add(this.btnWriteReg);
            this.gbDirectOperations.Controls.Add(this.lblFloatValue);
            this.gbDirectOperations.Controls.Add(this.txtFloatValue);
            this.gbDirectOperations.Controls.Add(this.btnReadFloat);
            this.gbDirectOperations.Controls.Add(this.btnWriteFloat);
            this.gbDirectOperations.Controls.Add(this.lblIntValue);
            this.gbDirectOperations.Controls.Add(this.txtIntValue);
            this.gbDirectOperations.Controls.Add(this.btnReadInt);
            this.gbDirectOperations.Controls.Add(this.btnWriteInt);
            this.gbDirectOperations.Location = new System.Drawing.Point(364, 242);
            this.gbDirectOperations.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbDirectOperations.Name = "gbDirectOperations";
            this.gbDirectOperations.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbDirectOperations.Size = new System.Drawing.Size(519, 170);
            this.gbDirectOperations.TabIndex = 5;
            this.gbDirectOperations.TabStop = false;
            this.gbDirectOperations.Text = "直接操作";
            // 
            // lblRegValue
            // 
            this.lblRegValue.AutoSize = true;
            this.lblRegValue.Location = new System.Drawing.Point(15, 35);
            this.lblRegValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblRegValue.Name = "lblRegValue";
            this.lblRegValue.Size = new System.Drawing.Size(98, 22);
            this.lblRegValue.TabIndex = 2;
            this.lblRegValue.Text = "布尔值：";
            // 
            // txtRegValue
            // 
            this.txtRegValue.Location = new System.Drawing.Point(120, 32);
            this.txtRegValue.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.txtRegValue.Name = "txtRegValue";
            this.txtRegValue.Size = new System.Drawing.Size(93, 32);
            this.txtRegValue.TabIndex = 3;
            this.txtRegValue.Text = "0";
            // 
            // btnReadReg
            // 
            this.btnReadReg.Location = new System.Drawing.Point(225, 30);
            this.btnReadReg.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnReadReg.Name = "btnReadReg";
            this.btnReadReg.Size = new System.Drawing.Size(135, 40);
            this.btnReadReg.TabIndex = 4;
            this.btnReadReg.Text = "读取寄存器";
            this.btnReadReg.UseVisualStyleBackColor = true;
            this.btnReadReg.Click += new System.EventHandler(this.BtnReadReg_Click);
            // 
            // btnWriteReg
            // 
            this.btnWriteReg.Location = new System.Drawing.Point(370, 30);
            this.btnWriteReg.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnWriteReg.Name = "btnWriteReg";
            this.btnWriteReg.Size = new System.Drawing.Size(135, 40);
            this.btnWriteReg.TabIndex = 5;
            this.btnWriteReg.Text = "写入寄存器";
            this.btnWriteReg.UseVisualStyleBackColor = true;
            this.btnWriteReg.Click += new System.EventHandler(this.BtnWriteReg_Click);
            // 
            // lblFloatValue
            // 
            this.lblFloatValue.AutoSize = true;
            this.lblFloatValue.Location = new System.Drawing.Point(15, 75);
            this.lblFloatValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblFloatValue.Name = "lblFloatValue";
            this.lblFloatValue.Size = new System.Drawing.Size(98, 22);
            this.lblFloatValue.TabIndex = 6;
            this.lblFloatValue.Text = "浮点值：";
            // 
            // txtFloatValue
            // 
            this.txtFloatValue.Location = new System.Drawing.Point(120, 72);
            this.txtFloatValue.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.txtFloatValue.Name = "txtFloatValue";
            this.txtFloatValue.Size = new System.Drawing.Size(93, 32);
            this.txtFloatValue.TabIndex = 7;
            this.txtFloatValue.Text = "0.0";
            // 
            // btnReadFloat
            // 
            this.btnReadFloat.Location = new System.Drawing.Point(225, 70);
            this.btnReadFloat.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnReadFloat.Name = "btnReadFloat";
            this.btnReadFloat.Size = new System.Drawing.Size(135, 40);
            this.btnReadFloat.TabIndex = 8;
            this.btnReadFloat.Text = "读取浮点数";
            this.btnReadFloat.UseVisualStyleBackColor = true;
            this.btnReadFloat.Click += new System.EventHandler(this.BtnReadFloat_Click);
            // 
            // btnWriteFloat
            // 
            this.btnWriteFloat.Location = new System.Drawing.Point(370, 70);
            this.btnWriteFloat.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnWriteFloat.Name = "btnWriteFloat";
            this.btnWriteFloat.Size = new System.Drawing.Size(135, 40);
            this.btnWriteFloat.TabIndex = 9;
            this.btnWriteFloat.Text = "写入浮点数";
            this.btnWriteFloat.UseVisualStyleBackColor = true;
            this.btnWriteFloat.Click += new System.EventHandler(this.BtnWriteFloat_Click);
            // 
            // lblIntValue
            // 
            this.lblIntValue.AutoSize = true;
            this.lblIntValue.Location = new System.Drawing.Point(15, 115);
            this.lblIntValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblIntValue.Name = "lblIntValue";
            this.lblIntValue.Size = new System.Drawing.Size(98, 22);
            this.lblIntValue.TabIndex = 10;
            this.lblIntValue.Text = "整型值：";
            // 
            // txtIntValue
            // 
            this.txtIntValue.Location = new System.Drawing.Point(120, 112);
            this.txtIntValue.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.txtIntValue.Name = "txtIntValue";
            this.txtIntValue.Size = new System.Drawing.Size(93, 32);
            this.txtIntValue.TabIndex = 11;
            this.txtIntValue.Text = "0";
            // 
            // btnReadInt
            // 
            this.btnReadInt.Location = new System.Drawing.Point(225, 110);
            this.btnReadInt.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnReadInt.Name = "btnReadInt";
            this.btnReadInt.Size = new System.Drawing.Size(135, 40);
            this.btnReadInt.TabIndex = 12;
            this.btnReadInt.Text = "读取整型值";
            this.btnReadInt.UseVisualStyleBackColor = true;
            this.btnReadInt.Click += new System.EventHandler(this.BtnReadInt_Click);
            // 
            // btnWriteInt
            // 
            this.btnWriteInt.Location = new System.Drawing.Point(370, 110);
            this.btnWriteInt.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnWriteInt.Name = "btnWriteInt";
            this.btnWriteInt.Size = new System.Drawing.Size(135, 40);
            this.btnWriteInt.TabIndex = 13;
            this.btnWriteInt.Text = "写入整型值";
            this.btnWriteInt.UseVisualStyleBackColor = true;
            this.btnWriteInt.Click += new System.EventHandler(this.BtnWriteInt_Click);
            // 
            // gbLog
            // 
            this.gbLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.gbLog.Controls.Add(this.txtLog);
            this.gbLog.Controls.Add(this.btnClearLog);
            this.gbLog.Location = new System.Drawing.Point(15, 405);
            this.gbLog.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbLog.Name = "gbLog";
            this.gbLog.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbLog.Size = new System.Drawing.Size(868, 199);
            this.gbLog.TabIndex = 4;
            this.gbLog.TabStop = false;
            this.gbLog.Text = "通信日志";
            // 
            // txtLog
            // 
            this.txtLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtLog.Font = new System.Drawing.Font("宋体", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtLog.Location = new System.Drawing.Point(15, 30);
            this.txtLog.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(806, 49);
            this.txtLog.TabIndex = 0;
            // 
            // btnClearLog
            // 
            this.btnClearLog.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClearLog.Location = new System.Drawing.Point(15, 89);
            this.btnClearLog.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnClearLog.Name = "btnClearLog";
            this.btnClearLog.Size = new System.Drawing.Size(806, 40);
            this.btnClearLog.TabIndex = 1;
            this.btnClearLog.Text = "清除日志";
            this.btnClearLog.UseVisualStyleBackColor = true;
            this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(11F, 22F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(897, 619);
            this.Controls.Add(this.gbSerialPort);
            this.Controls.Add(this.gbRegister);
            this.Controls.Add(this.gbDirectOperations);
            this.Controls.Add(this.gbLog);
            this.Font = new System.Drawing.Font("宋体", 10.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.MinimumSize = new System.Drawing.Size(800, 600);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Modbus 调试工具";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.Resize += new System.EventHandler(this.Form1_Resize);
            this.gbSerialPort.ResumeLayout(false);
            this.gbSerialPort.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudDeviceId)).EndInit();
            this.gbRegister.ResumeLayout(false);
            this.gbRegister.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudAddress)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudValue)).EndInit();
            this.gbDirectOperations.ResumeLayout(false);
            this.gbDirectOperations.PerformLayout();
            this.gbLog.ResumeLayout(false);
            this.gbLog.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        // 将控件声明为类成员变量，以便在Form1.cs中访问
        private System.Windows.Forms.GroupBox gbSerialPort;
        private System.Windows.Forms.ComboBox cmbPort;
        private System.Windows.Forms.ComboBox cmbBaudRate;
        private System.Windows.Forms.ComboBox cmbDataBits;
        private System.Windows.Forms.ComboBox cmbStopBits;
        private System.Windows.Forms.ComboBox cmbParity;
        private System.Windows.Forms.NumericUpDown nudDeviceId;
        private System.Windows.Forms.GroupBox gbRegister;
        private System.Windows.Forms.NumericUpDown nudAddress;
        private System.Windows.Forms.ComboBox cmbOperation;
        private System.Windows.Forms.NumericUpDown nudValue;
        private System.Windows.Forms.CheckBox chkHex;
        private System.Windows.Forms.Button btnSend;
        private System.Windows.Forms.GroupBox gbDirectOperations;
        private System.Windows.Forms.Label lblRegValue;
        private System.Windows.Forms.TextBox txtRegValue;
        private System.Windows.Forms.Button btnReadReg;
        private System.Windows.Forms.Button btnWriteReg;
        private System.Windows.Forms.Label lblFloatValue;
        private System.Windows.Forms.TextBox txtFloatValue;
        private System.Windows.Forms.Button btnReadFloat;
        private System.Windows.Forms.Button btnWriteFloat;
        private System.Windows.Forms.GroupBox gbLog;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Button btnClearLog;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.Label lblBaudRate;
        private System.Windows.Forms.Label lblDataBits;
        private System.Windows.Forms.Label lblStopBits;
        private System.Windows.Forms.Label lblParity;
        private System.Windows.Forms.Label lblAddress;
        private System.Windows.Forms.Label lblOperation;
        private System.Windows.Forms.Label lblValue;
        private System.Windows.Forms.Label lblDeviceId;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.Button btnRefreshPorts;
        private System.Windows.Forms.Label lblIntValue;
        private System.Windows.Forms.TextBox txtIntValue;
        private System.Windows.Forms.Button btnReadInt;
        private System.Windows.Forms.Button btnWriteInt;
    }
}

