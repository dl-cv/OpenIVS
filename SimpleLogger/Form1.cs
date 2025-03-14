using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;

namespace SimpleLogger
{
    public partial class Form1 : Form
    {
        private Logger _logger;
        
        public Form1()
        {
            InitializeComponent();
            
            // 初始化日志记录器
            _logger = new Logger();
            
            // 显示日志文件路径
            lblLogFilePath.Text = "日志文件路径: " + _logger.GetLogFilePath();
        }

        private void btnDebug_Click(object sender, EventArgs e)
        {
            string message = txtLogMessage.Text.Trim();
            if (!string.IsNullOrEmpty(message))
            {
                _logger.Debug(message);
                UpdateLogPreview("已记录调试信息");
            }
        }

        private void btnInfo_Click(object sender, EventArgs e)
        {
            string message = txtLogMessage.Text.Trim();
            if (!string.IsNullOrEmpty(message))
            {
                _logger.Info(message);
                UpdateLogPreview("已记录一般信息");
            }
        }

        private void btnWarning_Click(object sender, EventArgs e)
        {
            string message = txtLogMessage.Text.Trim();
            if (!string.IsNullOrEmpty(message))
            {
                _logger.Warning(message);
                UpdateLogPreview("已记录警告信息");
            }
        }

        private void btnError_Click(object sender, EventArgs e)
        {
            string message = txtLogMessage.Text.Trim();
            if (!string.IsNullOrEmpty(message))
            {
                _logger.Error(message);
                UpdateLogPreview("已记录错误信息");
            }
        }

        private void btnFatal_Click(object sender, EventArgs e)
        {
            string message = txtLogMessage.Text.Trim();
            if (!string.IsNullOrEmpty(message))
            {
                _logger.Fatal(message);
                UpdateLogPreview("已记录严重错误信息");
            }
        }

        private void btnException_Click(object sender, EventArgs e)
        {
            try
            {
                // 创建一个人为的异常用于演示
                throw new Exception("这是一个测试异常");
            }
            catch (Exception ex)
            {
                string message = txtLogMessage.Text.Trim();
                _logger.LogException(ex, message);
                UpdateLogPreview("已记录异常信息");
            }
        }

        private void btnViewLog_Click(object sender, EventArgs e)
        {
            try
            {
                string logFilePath = _logger.GetLogFilePath();
                if (File.Exists(logFilePath))
                {
                    // 读取日志文件内容
                    string logContent = File.ReadAllText(logFilePath);
                    
                    // 创建一个新窗口显示日志内容
                    Form logViewForm = new Form();
                    logViewForm.Text = "日志查看器";
                    logViewForm.Size = new Size(800, 600);
                    
                    TextBox textBox = new TextBox();
                    textBox.Multiline = true;
                    textBox.ReadOnly = true;
                    textBox.ScrollBars = ScrollBars.Both;
                    textBox.Dock = DockStyle.Fill;
                    textBox.Text = logContent;
                    textBox.Font = new Font("Consolas", 10);
                    
                    logViewForm.Controls.Add(textBox);
                    logViewForm.Show();
                }
                else
                {
                    MessageBox.Show("日志文件不存在", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("查看日志失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnOpenLogDir_Click(object sender, EventArgs e)
        {
            try
            {
                string logFilePath = _logger.GetLogFilePath();
                string logDirectory = Path.GetDirectoryName(logFilePath);
                
                if (Directory.Exists(logDirectory))
                {
                    System.Diagnostics.Process.Start("explorer.exe", logDirectory);
                }
                else
                {
                    MessageBox.Show("日志目录不存在", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开日志目录失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateLogPreview(string status)
        {
            lblStatus.Text = status;
        }
    }
}
