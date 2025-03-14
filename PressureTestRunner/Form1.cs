using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PressureTestRunner
{
    public partial class Form1 : Form
    {
        private PressureTestRunner _runner;
        private Timer _updateTimer;

        public Form1()
        {
            InitializeComponent();
            InitializeCustomComponents();
            SetupPressureTestRunner();
        }

        private void InitializeCustomComponents()
        {
            // 创建控件
            Label lblThreadCount = new Label { Text = "线程数量:", Location = new Point(20, 20), Width = 80 };
            NumericUpDown nudThreadCount = new NumericUpDown { Location = new Point(110, 20), Minimum = 1, Maximum = 100, Value = 10 };
            
            Label lblTargetRate = new Label { Text = "目标速率:", Location = new Point(20, 50), Width = 80 };
            NumericUpDown nudTargetRate = new NumericUpDown { Location = new Point(110, 50), Minimum = 1, Maximum = 10000, Value = 100 };
            
            Button btnStart = new Button { Text = "开始测试", Location = new Point(20, 90), Width = 100 };
            Button btnStop = new Button { Text = "停止测试", Location = new Point(130, 90), Width = 100 };
            btnStop.Enabled = false;
            
            TextBox txtResults = new TextBox { 
                Location = new Point(20, 130), 
                Width = 760, 
                Height = 280, 
                Multiline = true, 
                ScrollBars = ScrollBars.Vertical, 
                ReadOnly = true 
            };
            
            // 添加控件到表单
            this.Controls.AddRange(new Control[] { 
                lblThreadCount, nudThreadCount, 
                lblTargetRate, nudTargetRate, 
                btnStart, btnStop, 
                txtResults 
            });
            
            // 设置窗体属性
            this.Text = "压力测试工具";
            this.Size = new Size(800, 450);
            
            // 创建更新定时器
            _updateTimer = new Timer();
            _updateTimer.Interval = 300; // 每秒更新一次
            _updateTimer.Tick += (s, e) => {
                if (_runner != null && _runner.IsRunning)
                {
                    txtResults.Text = _runner.GetStatistics();
                }
            };
            
            // 添加事件处理
            btnStart.Click += (s, e) => {
                int threadCount = (int)nudThreadCount.Value;
                int targetRate = (int)nudTargetRate.Value;
                
                // 创建新的压力测试实例
                _runner = new PressureTestRunner(threadCount, targetRate);
                _runner.SetTestAction(TestMethod);
                
                // 启动测试
                _runner.Start();
                _updateTimer.Start();
                
                // 更新UI状态
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                nudThreadCount.Enabled = false;
                nudTargetRate.Enabled = false;
                
                txtResults.Text = "压力测试已启动...";
            };
            
            btnStop.Click += (s, e) => {
                // 停止测试
                if (_runner != null && _runner.IsRunning)
                {
                    _runner.Stop();
                    _updateTimer.Stop();
                    
                    txtResults.Text = _runner.GetStatistics();
                }
                
                // 更新UI状态
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                nudThreadCount.Enabled = true;
                nudTargetRate.Enabled = true;
            };
        }

        private void SetupPressureTestRunner()
        {
            // 这里可以进行额外的配置
        }

        /// <summary>
        /// 测试方法 - 将被执行作为压力测试的目标
        /// </summary>
        private void TestMethod(object parameter)
        {
            // 这里是您想要测试的实际代码
            // 例如：API调用、数据库操作等
            
            // 模拟一些工作量
            System.Threading.Thread.Sleep(10);
            
            // 可以使用以下代码记录详细信息（在实际应用中）
            // Console.WriteLine($"执行了测试方法，线程ID: {Thread.CurrentThread.ManagedThreadId}");
        }
    }
}
