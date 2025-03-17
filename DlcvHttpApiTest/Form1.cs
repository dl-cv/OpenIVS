using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using DLCV;
using Newtonsoft.Json;
using System.Threading;

namespace DlcvHttpApiTest
{
    public partial class Form1 : Form
    {
        private DlcvHttpApi _api;
        private string _modelPath;
        private string _imagePath;
        private bool _isServerRunning = false;

        public Form1()
        {
            InitializeComponent();
            InitializeCustomComponents();
        }

        private void InitializeCustomComponents()
        {
            // 初始化UI组件 (假设你在设计器中已经创建了这些控件)
            if (btnSelectModel == null)
            {
                // 创建按钮：选择模型
                btnSelectModel = new Button();
                btnSelectModel.Text = "选择模型";
                btnSelectModel.Location = new Point(20, 20);
                btnSelectModel.Size = new Size(120, 30);
                btnSelectModel.Click += BtnSelectModel_Click;
                this.Controls.Add(btnSelectModel);
            }

            if (btnSelectImage == null)
            {
                // 创建按钮：选择图像
                btnSelectImage = new Button();
                btnSelectImage.Text = "选择图像";
                btnSelectImage.Location = new Point(150, 20);
                btnSelectImage.Size = new Size(120, 30);
                btnSelectImage.Click += BtnSelectImage_Click;
                this.Controls.Add(btnSelectImage);
            }

            if (btnCheckServer == null)
            {
                // 创建按钮：检查服务器
                btnCheckServer = new Button();
                btnCheckServer.Text = "检查服务器";
                btnCheckServer.Location = new Point(280, 20);
                btnCheckServer.Size = new Size(120, 30);
                btnCheckServer.Click += BtnCheckServer_Click;
                this.Controls.Add(btnCheckServer);
            }

            if (btnInfer == null)
            {
                // 创建按钮：推理
                btnInfer = new Button();
                btnInfer.Text = "单线程推理";
                btnInfer.Location = new Point(20, 70);
                btnInfer.Size = new Size(120, 30);
                btnInfer.Click += BtnInfer_Click;
                this.Controls.Add(btnInfer);
            }

            if (btnPressureTest == null)
            {
                // 创建按钮：压力测试
                btnPressureTest = new Button();
                btnPressureTest.Text = "多线程推理";
                btnPressureTest.Location = new Point(150, 70);
                btnPressureTest.Size = new Size(120, 30);
                btnPressureTest.Click += BtnPressureTest_Click;
                this.Controls.Add(btnPressureTest);
            }

            if (txtThreads == null)
            {
                // 创建文本框：线程数
                txtThreads = new TextBox();
                txtThreads.Text = "5";
                txtThreads.Location = new Point(280, 70);
                txtThreads.Size = new Size(50, 30);
                this.Controls.Add(txtThreads);

                Label lblThreads = new Label();
                lblThreads.Text = "线程数";
                lblThreads.Location = new Point(340, 75);
                lblThreads.AutoSize = true;
                this.Controls.Add(lblThreads);
            }

            if (txtRate == null)
            {
                // 创建文本框：请求速率
                txtRate = new TextBox();
                txtRate.Text = "10";
                txtRate.Location = new Point(400, 70);
                txtRate.Size = new Size(50, 30);
                this.Controls.Add(txtRate);

                Label lblRate = new Label();
                lblRate.Text = "每秒请求数";
                lblRate.Location = new Point(460, 75);
                lblRate.AutoSize = true;
                this.Controls.Add(lblRate);
            }

            if (txtResult == null)
            {
                // 创建文本框：结果
                txtResult = new RichTextBox();
                txtResult.Location = new Point(20, 120);
                txtResult.Size = new Size(540, 300);
                txtResult.ReadOnly = true;
                this.Controls.Add(txtResult);
            }

            // 初始化API
            _api = new DlcvHttpApi();

            // 设置窗体属性
            this.Text = "DLCV HTTP API 测试工具";
            this.Size = new Size(600, 500);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
        }

        // 控件成员变量
        private Button btnSelectModel;
        private Button btnSelectImage;
        private Button btnCheckServer;
        private Button btnInfer;
        private Button btnPressureTest;
        private TextBox txtThreads;
        private TextBox txtRate;
        private RichTextBox txtResult;

        // 选择模型按钮点击事件
        private void BtnSelectModel_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "模型文件 (*.dvt;*.dvp)|*.dvt;*.dvp|所有文件 (*.*)|*.*";
                dialog.Title = "选择模型文件";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _modelPath = dialog.FileName;
                    LogMessage($"已选择模型: {_modelPath}");
                }
            }
        }

        // 选择图像按钮点击事件
        private void BtnSelectImage_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "图像文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|所有文件 (*.*)|*.*";
                dialog.Title = "选择图像文件";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _imagePath = dialog.FileName;
                    LogMessage($"已选择图像: {_imagePath}");
                }
            }
        }

        // 检查服务器按钮点击事件
        private void BtnCheckServer_Click(object sender, EventArgs e)
        {
            using (new WaitCursor(this))
            {
                try
                {
                    LogMessage("正在检查服务器状态...");
                    bool isRunning = DlcvHttpApi.IsLocalServerRunning();
                    
                    if (isRunning)
                    {
                        _isServerRunning = true;
                        bool isConnected = _api.Connect();
                        if (isConnected)
                        {
                            LogMessage("服务器运行正常，已连接成功！");
                        }
                        else
                        {
                            LogMessage("服务器运行正常，但连接失败！");
                        }
                    }
                    else
                    {
                        LogMessage("服务器未运行，请启动服务器后重试。");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"检查服务器出错: {ex.Message}");
                }
            }
        }

        // 推理按钮点击事件
        private void BtnInfer_Click(object sender, EventArgs e)
        {
            if (!CheckRequirements())
                return;

            using (new WaitCursor(this))
            {
                try
                {
                    LogMessage("开始推理...");
                    Stopwatch sw = Stopwatch.StartNew();
                    
                    var result = _api.InferImage(_imagePath, _modelPath);
                    
                    sw.Stop();
                    
                    DisplayResult(result, sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    LogMessage($"推理出错: {ex.Message}");
                }
            }
        }

        // 多线程压力测试按钮点击事件
        private void BtnPressureTest_Click(object sender, EventArgs e)
        {
            if (!CheckRequirements())
                return;

            try
            {
                // 获取线程数和速率
                if (!int.TryParse(txtThreads.Text, out int threadCount) || threadCount <= 0)
                {
                    LogMessage("请输入有效的线程数！");
                    return;
                }

                if (!int.TryParse(txtRate.Text, out int targetRate) || targetRate <= 0)
                {
                    LogMessage("请输入有效的请求速率！");
                    return;
                }

                LogMessage($"开始多线程推理测试 - 线程数: {threadCount}, 目标速率: {targetRate}/秒");

                // 创建压力测试运行器
                var testRunner = new DLCV.PressureTestRunner(threadCount, targetRate);
                
                // 设置测试动作
                testRunner.SetTestAction(obj => 
                {
                    try
                    {
                        _api.InferImage(_imagePath, _modelPath);
                    }
                    catch (Exception ex)
                    {
                        // 在UI线程上记录异常
                        this.BeginInvoke(new Action(() => 
                        {
                            LogMessage($"推理异常: {ex.Message}");
                        }));
                    }
                });

                // 启动测试
                testRunner.Start();

                // 创建定时器，定期更新统计信息
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = 300; // 每300豪秒更新一次
                
                // 记录开始时间
                DateTime startTime = DateTime.Now;
                
                timer.Tick += (s, args) => 
                {
                    // 获取统计信息
                    string stats = testRunner.GetStatistics();
                    
                    // 计算已运行时间
                    TimeSpan elapsed = DateTime.Now - startTime;
                    
                    // 显示统计信息
                    LogMessage($"已运行: {elapsed.TotalSeconds:F1}秒\n{stats}");
                    
                    // 10秒后停止测试
                    if (elapsed.TotalSeconds >= 10)
                    {
                        timer.Stop();
                        testRunner.Stop();
                        
                        // 显式触发垃圾回收以帮助识别内存泄漏
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        
                        LogMessage("多线程推理测试完成！");
                    }
                };
                
                timer.Start();
            }
            catch (Exception ex)
            {
                LogMessage($"压力测试出错: {ex.Message}");
            }
        }

        // 检查必要条件
        private bool CheckRequirements()
        {
            if (!_isServerRunning)
            {
                LogMessage("请先检查服务器状态并确保连接成功！");
                return false;
            }

            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
            {
                LogMessage("请先选择有效的模型文件！");
                return false;
            }

            if (string.IsNullOrEmpty(_imagePath) || !File.Exists(_imagePath))
            {
                LogMessage("请先选择有效的图像文件！");
                return false;
            }

            return true;
        }

        // 显示推理结果
        private void DisplayResult(dlcv_infer_csharp.Utils.CSharpResult result, long elapsedMs)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"推理完成，耗时: {elapsedMs} 毫秒");
            sb.AppendLine();

            // 显示结果的原始JSON格式
            var resultJson = ConvertResultToJson(result);
            sb.AppendLine("推理结果 (JSON格式):");
            sb.AppendLine(resultJson);
            sb.AppendLine();

            // 显示对象检测结果
            sb.AppendLine("检测结果详情:");
            foreach (var sampleResult in result.SampleResults)
            {
                foreach (var obj in sampleResult.Results)
                {
                    sb.AppendLine($"类别: {obj.CategoryName} (ID: {obj.CategoryId})");
                    sb.AppendLine($"置信度: {obj.Score:F4}");
                    sb.AppendLine($"区域: {obj.Area}");
                    sb.AppendLine($"边界框: [{string.Join(", ", obj.Bbox)}]");
                    sb.AppendLine($"有掩码: {obj.WithMask}");
                    sb.AppendLine();
                }
            }

            LogMessage(sb.ToString());
        }

        // 将推理结果转换为JSON格式
        private string ConvertResultToJson(dlcv_infer_csharp.Utils.CSharpResult result)
        {
            var jsonObj = new
            {
                code = "00000",
                task_type = "实例分割",
                results = result.SampleResults.SelectMany(s => s.Results.Select(r => new
                {
                    category_id = r.CategoryId,
                    category_name = r.CategoryName,
                    score = r.Score,
                    area = r.Area,
                    bbox = r.Bbox,
                    with_mask = r.WithMask
                })).ToArray()
            };

            return JsonConvert.SerializeObject(jsonObj, Formatting.Indented);
        }

        // 记录消息到结果文本框
        private void LogMessage(string message)
        {
            if (txtResult.InvokeRequired)
            {
                txtResult.BeginInvoke(new Action(() => LogMessage(message)));
            }
            else
            {
                txtResult.AppendText(message + Environment.NewLine);
                txtResult.ScrollToCaret();
            }
        }
    }
}
