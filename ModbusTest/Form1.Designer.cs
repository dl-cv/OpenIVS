namespace ModbusTest
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
            this.lblDeviceId = new System.Windows.Forms.Label();
            this.nudDeviceId = new System.Windows.Forms.NumericUpDown();
            this.btnConnect = new System.Windows.Forms.Button();
            this.btnRefreshPorts = new System.Windows.Forms.Button();
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
            this.gbSerialPort.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
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
            this.gbSerialPort.Controls.Add(this.lblDeviceId);
            this.gbSerialPort.Controls.Add(this.nudDeviceId);
            this.gbSerialPort.Location = new System.Drawing.Point(15, 15);
            this.gbSerialPort.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbSerialPort.Name = "gbSerialPort";
            this.gbSerialPort.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbSerialPort.Size = new System.Drawing.Size(756, 240);
            this.gbSerialPort.TabIndex = 0;
            this.gbSerialPort.TabStop = false;
            this.gbSerialPort.Text = "串口设置";
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
            this.cmbPort.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbPort.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPort.FormattingEnabled = true;
            this.cmbPort.Location = new System.Drawing.Point(120, 32);
            this.cmbPort.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbPort.Name = "cmbPort";
            this.cmbPort.Size = new System.Drawing.Size(611, 30);
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
            this.cmbBaudRate.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
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
            this.cmbBaudRate.Size = new System.Drawing.Size(610, 30);
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
            this.cmbDataBits.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbDataBits.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDataBits.FormattingEnabled = true;
            this.cmbDataBits.Items.AddRange(new object[] {
            7,
            8});
            this.cmbDataBits.Location = new System.Drawing.Point(121, 112);
            this.cmbDataBits.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbDataBits.Name = "cmbDataBits";
            this.cmbDataBits.Size = new System.Drawing.Size(610, 30);
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
            this.cmbStopBits.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbStopBits.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbStopBits.FormattingEnabled = true;
            this.cmbStopBits.Items.AddRange(new object[] {
            "One",
            "Two"});
            this.cmbStopBits.Location = new System.Drawing.Point(121, 152);
            this.cmbStopBits.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbStopBits.Name = "cmbStopBits";
            this.cmbStopBits.Size = new System.Drawing.Size(610, 30);
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
            this.cmbParity.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbParity.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbParity.FormattingEnabled = true;
            this.cmbParity.Items.AddRange(new object[] {
            "None",
            "Odd",
            "Even"});
            this.cmbParity.Location = new System.Drawing.Point(120, 192);
            this.cmbParity.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbParity.Name = "cmbParity";
            this.cmbParity.Size = new System.Drawing.Size(417, 30);
            this.cmbParity.TabIndex = 9;
            // 
            // lblDeviceId
            // 
            this.lblDeviceId.AutoSize = true;
            this.lblDeviceId.Location = new System.Drawing.Point(545, 194);
            this.lblDeviceId.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblDeviceId.Name = "lblDeviceId";
            this.lblDeviceId.Size = new System.Drawing.Size(98, 22);
            this.lblDeviceId.TabIndex = 10;
            this.lblDeviceId.Text = "设备ID：";
            // 
            // nudDeviceId
            // 
            this.nudDeviceId.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.nudDeviceId.Location = new System.Drawing.Point(651, 192);
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
            // btnConnect
            // 
            this.btnConnect.Location = new System.Drawing.Point(15, 265);
            this.btnConnect.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(150, 40);
            this.btnConnect.TabIndex = 1;
            this.btnConnect.Text = "打开串口";
            this.btnConnect.UseVisualStyleBackColor = true;
            this.btnConnect.Click += new System.EventHandler(this.BtnConnect_Click);
            // 
            // btnRefreshPorts
            // 
            this.btnRefreshPorts.Location = new System.Drawing.Point(175, 265);
            this.btnRefreshPorts.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnRefreshPorts.Name = "btnRefreshPorts";
            this.btnRefreshPorts.Size = new System.Drawing.Size(150, 40);
            this.btnRefreshPorts.TabIndex = 2;
            this.btnRefreshPorts.Text = "刷新串口";
            this.btnRefreshPorts.UseVisualStyleBackColor = true;
            this.btnRefreshPorts.Click += new System.EventHandler(this.BtnRefreshPorts_Click);
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
            this.gbRegister.Location = new System.Drawing.Point(786, 15);
            this.gbRegister.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbRegister.Name = "gbRegister";
            this.gbRegister.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbRegister.Size = new System.Drawing.Size(400, 240);
            this.gbRegister.TabIndex = 3;
            this.gbRegister.TabStop = false;
            this.gbRegister.Text = "寄存器操作";
            // 
            // lblAddress
            // 
            this.lblAddress.AutoSize = true;
            this.lblAddress.Location = new System.Drawing.Point(15, 35);
            this.lblAddress.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblAddress.Name = "lblAddress";
            this.lblAddress.Size = new System.Drawing.Size(76, 22);
            this.lblAddress.TabIndex = 0;
            this.lblAddress.Text = "地址：";
            // 
            // nudAddress
            // 
            this.nudAddress.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.nudAddress.Hexadecimal = true;
            this.nudAddress.Location = new System.Drawing.Point(99, 32);
            this.nudAddress.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.nudAddress.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.nudAddress.Name = "nudAddress";
            this.nudAddress.Size = new System.Drawing.Size(281, 32);
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
            this.lblOperation.Location = new System.Drawing.Point(15, 75);
            this.lblOperation.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblOperation.Name = "lblOperation";
            this.lblOperation.Size = new System.Drawing.Size(76, 22);
            this.lblOperation.TabIndex = 2;
            this.lblOperation.Text = "操作：";
            // 
            // cmbOperation
            // 
            this.cmbOperation.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbOperation.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbOperation.FormattingEnabled = true;
            this.cmbOperation.Items.AddRange(new object[] {
            "读线圈寄存器(01H)",
            "写单个线圈寄存器(05H)"});
            this.cmbOperation.Location = new System.Drawing.Point(99, 72);
            this.cmbOperation.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.cmbOperation.Name = "cmbOperation";
            this.cmbOperation.Size = new System.Drawing.Size(281, 30);
            this.cmbOperation.TabIndex = 3;
            // 
            // lblValue
            // 
            this.lblValue.AutoSize = true;
            this.lblValue.Location = new System.Drawing.Point(15, 115);
            this.lblValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblValue.Name = "lblValue";
            this.lblValue.Size = new System.Drawing.Size(54, 22);
            this.lblValue.TabIndex = 4;
            this.lblValue.Text = "值：";
            // 
            // nudValue
            // 
            this.nudValue.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.nudValue.Hexadecimal = true;
            this.nudValue.Location = new System.Drawing.Point(99, 112);
            this.nudValue.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.nudValue.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.nudValue.Name = "nudValue";
            this.nudValue.Size = new System.Drawing.Size(281, 32);
            this.nudValue.TabIndex = 5;
            // 
            // chkHex
            // 
            this.chkHex.AutoSize = true;
            this.chkHex.Checked = true;
            this.chkHex.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkHex.Location = new System.Drawing.Point(18, 155);
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
            this.btnSend.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSend.Location = new System.Drawing.Point(150, 150);
            this.btnSend.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(230, 40);
            this.btnSend.TabIndex = 7;
            this.btnSend.Text = "发送";
            this.btnSend.UseVisualStyleBackColor = true;
            this.btnSend.Click += new System.EventHandler(this.BtnSend_Click);
            // 
            // gbDirectOperations
            // 
            this.gbDirectOperations.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.gbDirectOperations.Controls.Add(this.lblRegValue);
            this.gbDirectOperations.Controls.Add(this.txtRegValue);
            this.gbDirectOperations.Controls.Add(this.btnReadReg);
            this.gbDirectOperations.Controls.Add(this.btnWriteReg);
            this.gbDirectOperations.Location = new System.Drawing.Point(666, 265);
            this.gbDirectOperations.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbDirectOperations.Name = "gbDirectOperations";
            this.gbDirectOperations.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbDirectOperations.Size = new System.Drawing.Size(520, 100);
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
            this.lblRegValue.Size = new System.Drawing.Size(54, 22);
            this.lblRegValue.TabIndex = 2;
            this.lblRegValue.Text = "值：";
            // 
            // txtRegValue
            // 
            this.txtRegValue.Location = new System.Drawing.Point(75, 32);
            this.txtRegValue.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.txtRegValue.Name = "txtRegValue";
            this.txtRegValue.Size = new System.Drawing.Size(138, 32);
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
            // gbLog
            // 
            this.gbLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.gbLog.Controls.Add(this.txtLog);
            this.gbLog.Controls.Add(this.btnClearLog);
            this.gbLog.Location = new System.Drawing.Point(15, 375);
            this.gbLog.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbLog.Name = "gbLog";
            this.gbLog.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.gbLog.Size = new System.Drawing.Size(1171, 329);
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
            this.txtLog.Size = new System.Drawing.Size(1141, 239);
            this.txtLog.TabIndex = 0;
            // 
            // btnClearLog
            // 
            this.btnClearLog.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClearLog.Location = new System.Drawing.Point(15, 279);
            this.btnClearLog.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btnClearLog.Name = "btnClearLog";
            this.btnClearLog.Size = new System.Drawing.Size(1141, 40);
            this.btnClearLog.TabIndex = 1;
            this.btnClearLog.Text = "清除日志";
            this.btnClearLog.UseVisualStyleBackColor = true;
            this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(11F, 22F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 719);
            this.Controls.Add(this.gbSerialPort);
            this.Controls.Add(this.btnConnect);
            this.Controls.Add(this.btnRefreshPorts);
            this.Controls.Add(this.gbRegister);
            this.Controls.Add(this.gbDirectOperations);
            this.Controls.Add(this.gbLog);
            this.Font = new System.Drawing.Font("宋体", 10.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.MinimumSize = new System.Drawing.Size(1000, 700);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Modbus 调试工具";
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
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.Button btnRefreshPorts;
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
        private System.Windows.Forms.GroupBox gbLog;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Button btnClearLog;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.Label lblBaudRate;
        private System.Windows.Forms.Label lblDataBits;
        private System.Windows.Forms.Label lblStopBits;
        private System.Windows.Forms.Label lblParity;
        private System.Windows.Forms.Label lblDeviceId;
        private System.Windows.Forms.Label lblAddress;
        private System.Windows.Forms.Label lblOperation;
        private System.Windows.Forms.Label lblValue;
    }
}

