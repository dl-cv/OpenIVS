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
        private int _loadedModelIndex = -1; // 添加存储已加载模型索引的变量

        public Form1()
        {
            InitializeComponent();
            InitializeCustomComponents();
        }

        private void InitializeCustomComponents()
        {
            // 初始化API
            _api = new DlcvHttpApi();
        }


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

        // 推理按钮点击事件
        private void BtnInfer_Click(object sender, EventArgs e)
        {
            if (!CheckServerConnected())
                return;

            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
            {
                LogMessage("请先选择有效的模型文件！");
                return;
            }

            if (string.IsNullOrEmpty(_imagePath) || !File.Exists(_imagePath))
            {
                LogMessage("请先选择有效的图像文件！");
                return;
            }

            try
            {
                LogMessage("开始推理...");
                Stopwatch sw = Stopwatch.StartNew();
                
                // 如果模型已加载，使用索引进行推理，否则使用模型路径
                dlcv_infer_csharp.Utils.CSharpResult result;

                if (_loadedModelIndex >= 0)
                {
                    LogMessage($"使用已加载的模型 (索引: {_loadedModelIndex}) 进行推理");
                    
                    // 这里需要直接调用用模型索引的推理方法，但当前API未提供
                    // 为了兼容，仍使用模型路径进行推理
                    result = _api.InferImage(_imagePath, _modelPath);
                }
                else
                {
                    LogMessage("使用模型路径进行推理");
                    result = _api.InferImage(_imagePath, _modelPath);
                }
                
                sw.Stop();
                
                DisplayResult(result, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                LogMessage($"推理出错: {ex.Message}");
            }
        }

        // 多线程压力测试按钮点击事件
        private void BtnPressureTest_Click(object sender, EventArgs e)
        {
            if (!CheckServerConnected())
                return;

            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
            {
                LogMessage("请先选择有效的模型文件！");
                return;
            }

            if (string.IsNullOrEmpty(_imagePath) || !File.Exists(_imagePath))
            {
                LogMessage("请先选择有效的图像文件！");
                return;
            }

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

        // 加载模型按钮点击事件
        private void BtnLoadModel_Click(object sender, EventArgs e)
        {
            if (!CheckServerConnected())
                return;

            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
            {
                LogMessage("请先选择有效的模型文件！");
                return;
            }

            try
            {
                LogMessage("正在加载模型...");
                Stopwatch sw = Stopwatch.StartNew();
                
                var result = _api.LoadModel(_modelPath);
                
                sw.Stop();
                
                _loadedModelIndex = (int)result["model_index"];
                LogMessage($"模型加载成功！耗时: {sw.ElapsedMilliseconds} 毫秒");
                LogMessage($"模型索引: {_loadedModelIndex}");
                LogMessage(result.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogMessage($"加载模型出错: {ex.Message}");
            }
        }

        // 获取模型信息按钮点击事件
        private void BtnGetModelInfo_Click(object sender, EventArgs e)
        {
            if (!CheckServerConnected())
                return;

            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
            {
                LogMessage("请先选择有效的模型文件！");
                return;
            }

            try
            {
                LogMessage("正在获取模型信息...");
                Stopwatch sw = Stopwatch.StartNew();
                
                // 如果模型已加载，优先使用模型索引获取信息
                var result = _loadedModelIndex >= 0 
                    ? _api.GetModelInfo(_loadedModelIndex)
                    : _api.GetModelInfo(_modelPath);
                
                sw.Stop();
                
                LogMessage($"获取模型信息成功！耗时: {sw.ElapsedMilliseconds} 毫秒");
                LogMessage(result.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogMessage($"获取模型信息出错: {ex.Message}");
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

        // 检查服务器连接状态
        private bool CheckServerConnected()
        {
            if (!_isServerRunning)
            {
                LogMessage("请先检查服务器状态并确保连接成功！");
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
