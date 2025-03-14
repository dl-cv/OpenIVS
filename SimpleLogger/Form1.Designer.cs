namespace SimpleLogger
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
            this.txtLogMessage = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.btnDebug = new System.Windows.Forms.Button();
            this.btnInfo = new System.Windows.Forms.Button();
            this.btnWarning = new System.Windows.Forms.Button();
            this.btnError = new System.Windows.Forms.Button();
            this.btnFatal = new System.Windows.Forms.Button();
            this.btnException = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblLogFilePath = new System.Windows.Forms.Label();
            this.btnViewLog = new System.Windows.Forms.Button();
            this.btnOpenLogDir = new System.Windows.Forms.Button();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // txtLogMessage
            // 
            this.txtLogMessage.Location = new System.Drawing.Point(15, 43);
            this.txtLogMessage.Multiline = true;
            this.txtLogMessage.Name = "txtLogMessage";
            this.txtLogMessage.Size = new System.Drawing.Size(415, 114);
            this.txtLogMessage.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 27);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(65, 12);
            this.label1.TabIndex = 1;
            this.label1.Text = "日志消息：";
            // 
            // btnDebug
            // 
            this.btnDebug.Location = new System.Drawing.Point(15, 25);
            this.btnDebug.Name = "btnDebug";
            this.btnDebug.Size = new System.Drawing.Size(75, 23);
            this.btnDebug.TabIndex = 2;
            this.btnDebug.Text = "调试";
            this.btnDebug.UseVisualStyleBackColor = true;
            this.btnDebug.Click += new System.EventHandler(this.btnDebug_Click);
            // 
            // btnInfo
            // 
            this.btnInfo.Location = new System.Drawing.Point(96, 25);
            this.btnInfo.Name = "btnInfo";
            this.btnInfo.Size = new System.Drawing.Size(75, 23);
            this.btnInfo.TabIndex = 3;
            this.btnInfo.Text = "信息";
            this.btnInfo.UseVisualStyleBackColor = true;
            this.btnInfo.Click += new System.EventHandler(this.btnInfo_Click);
            // 
            // btnWarning
            // 
            this.btnWarning.Location = new System.Drawing.Point(177, 25);
            this.btnWarning.Name = "btnWarning";
            this.btnWarning.Size = new System.Drawing.Size(75, 23);
            this.btnWarning.TabIndex = 4;
            this.btnWarning.Text = "警告";
            this.btnWarning.UseVisualStyleBackColor = true;
            this.btnWarning.Click += new System.EventHandler(this.btnWarning_Click);
            // 
            // btnError
            // 
            this.btnError.Location = new System.Drawing.Point(258, 25);
            this.btnError.Name = "btnError";
            this.btnError.Size = new System.Drawing.Size(75, 23);
            this.btnError.TabIndex = 5;
            this.btnError.Text = "错误";
            this.btnError.UseVisualStyleBackColor = true;
            this.btnError.Click += new System.EventHandler(this.btnError_Click);
            // 
            // btnFatal
            // 
            this.btnFatal.Location = new System.Drawing.Point(339, 25);
            this.btnFatal.Name = "btnFatal";
            this.btnFatal.Size = new System.Drawing.Size(75, 23);
            this.btnFatal.TabIndex = 6;
            this.btnFatal.Text = "严重错误";
            this.btnFatal.UseVisualStyleBackColor = true;
            this.btnFatal.Click += new System.EventHandler(this.btnFatal_Click);
            // 
            // btnException
            // 
            this.btnException.Location = new System.Drawing.Point(15, 60);
            this.btnException.Name = "btnException";
            this.btnException.Size = new System.Drawing.Size(156, 23);
            this.btnException.TabIndex = 7;
            this.btnException.Text = "记录测试异常";
            this.btnException.UseVisualStyleBackColor = true;
            this.btnException.Click += new System.EventHandler(this.btnException_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(15, 350);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(65, 12);
            this.lblStatus.TabIndex = 8;
            this.lblStatus.Text = "状态：就绪";
            // 
            // lblLogFilePath
            // 
            this.lblLogFilePath.AutoSize = true;
            this.lblLogFilePath.Location = new System.Drawing.Point(15, 322);
            this.lblLogFilePath.Name = "lblLogFilePath";
            this.lblLogFilePath.Size = new System.Drawing.Size(83, 12);
            this.lblLogFilePath.TabIndex = 9;
            this.lblLogFilePath.Text = "日志文件路径：";
            // 
            // btnViewLog
            // 
            this.btnViewLog.Location = new System.Drawing.Point(258, 60);
            this.btnViewLog.Name = "btnViewLog";
            this.btnViewLog.Size = new System.Drawing.Size(75, 23);
            this.btnViewLog.TabIndex = 10;
            this.btnViewLog.Text = "查看日志";
            this.btnViewLog.UseVisualStyleBackColor = true;
            this.btnViewLog.Click += new System.EventHandler(this.btnViewLog_Click);
            // 
            // btnOpenLogDir
            // 
            this.btnOpenLogDir.Location = new System.Drawing.Point(339, 60);
            this.btnOpenLogDir.Name = "btnOpenLogDir";
            this.btnOpenLogDir.Size = new System.Drawing.Size(75, 23);
            this.btnOpenLogDir.TabIndex = 11;
            this.btnOpenLogDir.Text = "打开目录";
            this.btnOpenLogDir.UseVisualStyleBackColor = true;
            this.btnOpenLogDir.Click += new System.EventHandler(this.btnOpenLogDir_Click);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.txtLogMessage);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Location = new System.Drawing.Point(14, 12);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(446, 172);
            this.groupBox1.TabIndex = 12;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "日志内容";
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.btnDebug);
            this.groupBox2.Controls.Add(this.btnInfo);
            this.groupBox2.Controls.Add(this.btnOpenLogDir);
            this.groupBox2.Controls.Add(this.btnWarning);
            this.groupBox2.Controls.Add(this.btnViewLog);
            this.groupBox2.Controls.Add(this.btnError);
            this.groupBox2.Controls.Add(this.btnFatal);
            this.groupBox2.Controls.Add(this.btnException);
            this.groupBox2.Location = new System.Drawing.Point(14, 199);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(446, 100);
            this.groupBox2.TabIndex = 13;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "操作";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(474, 381);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.lblLogFilePath);
            this.Controls.Add(this.lblStatus);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "SimpleLogger 演示";
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox txtLogMessage;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btnDebug;
        private System.Windows.Forms.Button btnInfo;
        private System.Windows.Forms.Button btnWarning;
        private System.Windows.Forms.Button btnError;
        private System.Windows.Forms.Button btnFatal;
        private System.Windows.Forms.Button btnException;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblLogFilePath;
        private System.Windows.Forms.Button btnViewLog;
        private System.Windows.Forms.Button btnOpenLogDir;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
    }
}

