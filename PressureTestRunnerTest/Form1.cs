using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DLCV;
using System.IO;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using static dlcv_infer_csharp.Utils;

namespace PressureTestRunner
{
    public partial class Form1 : Form
    {
        private DLCV.PressureTestRunner _runner;
        private Timer _updateTimer;

        // 模型相关
        private Model _model;
        private string _modelPath;
        private int _deviceId = 0;

        // 测试图像相关
        private string _imagesFolderPath;
        private List<string> _imageFiles = new List<string>();

        // 测试结果
        private Dictionary<string, CSharpResult> _singleThreadResults = new Dictionary<string, CSharpResult>();
        private Dictionary<string, string> _resultPaths = new Dictionary<string, string>();
        private string _outputFolder;

        // 结果比较记录
        private List<ComparisonRecord> _comparisonRecords = new List<ComparisonRecord>();
        private object _recordsLock = new object();
        private StringBuilder _comparisonSummary = new StringBuilder();

        // UI控件
        private NumericUpDown nudThreadCount;
        private NumericUpDown nudBatchSize;
        private TextBox txtResults;
        private Button btnLoadModel;
        private Button btnSelectFolder;
        private Button btnSingleTest;
        private Button btnPressureTest;
        private ComboBox cmbDevices;
        private CheckBox chkSaveResults;
        private ProgressBar progressBar;

        public Form1()
        {
            InitializeComponent();
            InitializeCustomComponents();
        }

        private void InitializeCustomComponents()
        {
            // 设置窗体属性
            this.Text = "DLCV推理测试工具";
            this.Size = new System.Drawing.Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 创建控件
            int yPos = 20;
            int padding = 30;

            // 设备选择
            Label lblDevice = new Label { Text = "选择设备:", Location = new System.Drawing.Point(20, yPos), Width = 80 };
            cmbDevices = new ComboBox { Location = new System.Drawing.Point(110, yPos), Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            this.Controls.AddRange(new Control[] { lblDevice, cmbDevices });
            yPos += padding;

            // 模型加载
            btnLoadModel = new Button { Text = "加载模型", Location = new System.Drawing.Point(20, yPos), Width = 100 };
            Label lblModelPath = new Label { Text = "未选择模型", Location = new System.Drawing.Point(130, yPos), Width = 650, AutoEllipsis = true };
            this.Controls.AddRange(new Control[] { btnLoadModel, lblModelPath });
            yPos += padding;

            // 选择图像文件夹
            btnSelectFolder = new Button { Text = "选择图像文件夹", Location = new System.Drawing.Point(20, yPos), Width = 120 };
            Label lblFolderPath = new Label { Text = "未选择文件夹", Location = new System.Drawing.Point(150, yPos), Width = 630, AutoEllipsis = true };
            this.Controls.AddRange(new Control[] { btnSelectFolder, lblFolderPath });
            yPos += padding;

            // 线程数量和批处理大小
            Label lblThreadCount = new Label { Text = "线程数量:", Location = new System.Drawing.Point(20, yPos), Width = 80 };
            nudThreadCount = new NumericUpDown { Location = new System.Drawing.Point(110, yPos), Minimum = 1, Maximum = 100, Value = 4, Width = 80 };

            Label lblBatchSize = new Label { Text = "批处理大小:", Location = new System.Drawing.Point(220, yPos), Width = 80 };
            nudBatchSize = new NumericUpDown { Location = new System.Drawing.Point(310, yPos), Minimum = 1, Maximum = 64, Value = 1, Width = 80 };

            chkSaveResults = new CheckBox { Text = "保存结果", Location = new System.Drawing.Point(420, yPos), Checked = true };

            this.Controls.AddRange(new Control[] { lblThreadCount, nudThreadCount, lblBatchSize, nudBatchSize, chkSaveResults });
            yPos += padding;

            // 测试按钮
            btnSingleTest = new Button { Text = "单线程测试", Location = new System.Drawing.Point(20, yPos), Width = 120 };
            btnPressureTest = new Button { Text = "多线程压力测试", Location = new System.Drawing.Point(150, yPos), Width = 140 };

            this.Controls.AddRange(new Control[] { btnSingleTest, btnPressureTest });
            yPos += padding;

            // 进度条
            progressBar = new ProgressBar { Location = new System.Drawing.Point(20, yPos), Width = 760, Height = 20 };
            this.Controls.Add(progressBar);
            yPos += 30;

            // 结果显示
            txtResults = new TextBox
            {
                Location = new System.Drawing.Point(20, yPos),
                Width = 760,
                Height = 330,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true
            };
            this.Controls.Add(txtResults);

            // 创建更新定时器
            _updateTimer = new Timer();
            _updateTimer.Interval = 300;
            _updateTimer.Tick += UpdateTimer_Tick;

            // 添加事件处理
            btnLoadModel.Click += BtnLoadModel_Click;
            btnSelectFolder.Click += BtnSelectFolder_Click;
            btnSingleTest.Click += BtnSingleTest_Click;
            btnPressureTest.Click += BtnPressureTest_Click;

            // 初始化设备信息
            InitializeDeviceInfo();
        }

        private void InitializeDeviceInfo()
        {
            try
            {
                JObject deviceInfo = Utils.GetGpuInfo();
                if (deviceInfo["code"].ToString() == "0")
                {
                    foreach (var item in deviceInfo["devices"])
                    {
                        cmbDevices.Items.Add(item["device_name"].ToString());
                    }
                }
                else
                {
                    AppendLog("获取设备信息失败: " + deviceInfo.ToString());
                }

                if (cmbDevices.Items.Count == 0)
                {
                    cmbDevices.Items.Add("CPU");
                }
                cmbDevices.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                AppendLog("初始化设备信息异常: " + ex.Message);
            }
        }

        private void BtnLoadModel_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "深度视觉加速模型文件 (*.dvt)|*.dvt",
                Title = "选择模型"
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _modelPath = openFileDialog.FileName;
                    _deviceId = cmbDevices.SelectedIndex;

                    // 释放之前加载的模型
                    if (_model != null)
                    {
                        _model = null;
                        GC.Collect();
                    }

                    // 加载新模型
                    _model = new Model(_modelPath, _deviceId);

                    // 获取模型信息
                    JObject modelInfo = _model.GetModelInfo();

                    // 更新UI
                    Control lblModelPath = this.Controls.Find("lblModelPath", false).FirstOrDefault();
                    if (lblModelPath != null)
                    {
                        lblModelPath.Text = _modelPath;
                    }

                    AppendLog("模型加载成功: " + Path.GetFileName(_modelPath));
                    AppendLog("模型信息: " + modelInfo["model_info"].ToString());
                }
                catch (Exception ex)
                {
                    AppendLog("模型加载失败: " + ex.Message);
                }
            }
        }

        private void BtnSelectFolder_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog folderDialog = new FolderBrowserDialog
            {
                Description = "选择包含图像的文件夹"
            };

            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _imagesFolderPath = folderDialog.SelectedPath;
                    _imageFiles.Clear();

                    // 获取文件夹下所有图像文件
                    string[] extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                    foreach (string ext in extensions)
                    {
                        _imageFiles.AddRange(Directory.GetFiles(_imagesFolderPath, "*" + ext, SearchOption.TopDirectoryOnly));
                    }

                    // 创建输出文件夹
                    _outputFolder = Path.Combine(_imagesFolderPath, "results_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    if (chkSaveResults.Checked && !Directory.Exists(_outputFolder))
                    {
                        Directory.CreateDirectory(_outputFolder);
                    }

                    // 更新UI
                    Control lblFolderPath = this.Controls.Find("lblFolderPath", false).FirstOrDefault();
                    if (lblFolderPath != null)
                    {
                        lblFolderPath.Text = _imagesFolderPath + " (共" + _imageFiles.Count + "张图像)";
                    }

                    AppendLog("已选择图像文件夹: " + _imagesFolderPath);
                    AppendLog("找到图像文件数量: " + _imageFiles.Count);
                }
                catch (Exception ex)
                {
                    AppendLog("选择文件夹失败: " + ex.Message);
                }
            }
        }

        private void BtnSingleTest_Click(object sender, EventArgs e)
        {
            if (!ValidateBeforeTest())
                return;

            try
            {
                AppendLog("开始单线程测试...");
                _singleThreadResults.Clear();
                _resultPaths.Clear();
                progressBar.Maximum = _imageFiles.Count;
                progressBar.Value = 0;

                // 禁用按钮
                SetButtonsEnabled(false);

                // 启动后台线程执行测试
                BackgroundWorker worker = new BackgroundWorker();
                worker.WorkerReportsProgress = true;
                worker.DoWork += SingleThreadTest_DoWork;
                worker.ProgressChanged += Worker_ProgressChanged;
                worker.RunWorkerCompleted += SingleThreadTest_Completed;
                worker.RunWorkerAsync();
            }
            catch (Exception ex)
            {
                AppendLog("单线程测试异常: " + ex.Message);
                SetButtonsEnabled(true);
            }
        }

        private void SingleThreadTest_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            int counter = 0;

            foreach (string imagePath in _imageFiles)
            {
                try
                {
                    // 读取图像
                    Mat image = Cv2.ImRead(imagePath, ImreadModes.Color);
                    Mat imageRgb = new Mat();
                    Cv2.CvtColor(image, imageRgb, ColorConversionCodes.BGR2RGB);

                    if (image.Empty())
                    {
                        AppendLogThreadSafe("图像读取失败: " + imagePath);
                        continue;
                    }

                    // 设置推理参数
                    JObject data = new JObject
                    {
                        ["threshold"] = 0.5f,
                        ["with_mask"] = true
                    };

                    // 执行推理
                    CSharpResult result = _model.Infer(imageRgb, data);

                    // 保存结果
                    string fileName = Path.GetFileName(imagePath);
                    _singleThreadResults[fileName] = result;

                    // 保存结果到文件
                    if (chkSaveResults.Checked)
                    {
                        string resultPath = Path.Combine(_outputFolder, Path.GetFileNameWithoutExtension(fileName) + "_result.json");
                        SaveResultToFile(result, resultPath);
                        _resultPaths[fileName] = resultPath;
                    }

                    counter++;
                    worker.ReportProgress(counter);
                }
                catch (Exception ex)
                {
                    AppendLogThreadSafe("处理图像失败 " + imagePath + ": " + ex.Message);
                }
            }
        }

        private void SingleThreadTest_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            AppendLog("单线程测试完成，共处理 " + _singleThreadResults.Count + " 张图像");
            SetButtonsEnabled(true);
        }

        private void BtnPressureTest_Click(object sender, EventArgs e)
        {
            if (!ValidateBeforeTest())
                return;

            // 检查是否已完成单线程测试
            if (_singleThreadResults.Count == 0)
            {
                DialogResult result = MessageBox.Show(
                    "尚未进行单线程基准测试，无法进行结果对比。是否继续？",
                    "确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                    return;
            }

            int threadCount = (int)nudThreadCount.Value;
            int batchSize = (int)nudBatchSize.Value;

            AppendLog($"开始多线程压力测试 (线程数: {threadCount}, 批量大小: {batchSize})...");

            try
            {
                // 清空比较记录
                _comparisonRecords.Clear();
                _comparisonSummary.Clear();
                
                // 禁用按钮
                SetButtonsEnabled(false);

                // 创建新的压力测试实例
                _runner = new DLCV.PressureTestRunner(threadCount, 1000000, batchSize);

                // 创建参数对象
                PressureTestParameters parameters = new PressureTestParameters
                {
                    ImageFiles = _imageFiles,
                    Model = _model,
                    SingleThreadResults = _singleThreadResults,
                    OutputFolder = _outputFolder,
                    SaveResults = chkSaveResults.Checked
                };

                // 设置测试动作
                _runner.SetTestAction(ModelInferAndCheckAction, parameters);

                // 启动测试
                _runner.Start();
                _updateTimer.Start();

                // 更新UI
                btnPressureTest.Text = "停止测试";
                btnPressureTest.Enabled = true;
                btnPressureTest.Click -= BtnPressureTest_Click;
                btnPressureTest.Click += BtnStopPressureTest_Click;
            }
            catch (Exception ex)
            {
                AppendLog("启动压力测试失败: " + ex.Message);
                SetButtonsEnabled(true);
            }
        }

        private void BtnStopPressureTest_Click(object sender, EventArgs e)
        {
            if (_runner != null && _runner.IsRunning)
            {
                _runner.Stop();
                _updateTimer.Stop();

                AppendLog("压力测试已停止，最终统计信息:");
                AppendLog(_runner.GetStatistics());
                
                // 保存比较记录汇总
                if (chkSaveResults.Checked && _comparisonRecords.Count > 0)
                {
                    SaveComparisonResults();
                }

                // 恢复UI状态
                btnPressureTest.Text = "多线程压力测试";
                btnPressureTest.Click -= BtnStopPressureTest_Click;
                btnPressureTest.Click += BtnPressureTest_Click;
                SetButtonsEnabled(true);
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_runner != null && _runner.IsRunning)
            {
                // 更新UI显示
                string stats = _runner.GetStatistics(false);
                AppendLog(stats, true);
            }
        }

        /// <summary>
        /// 执行模型推理并检查结果是否与单线程结果一致
        /// </summary>
        private void ModelInferAndCheckAction(object parameter)
        {
            PressureTestParameters parameters = (PressureTestParameters)parameter;

            try
            {
                // 随机选择一张图像
                Random random = new Random();
                int index = random.Next(parameters.ImageFiles.Count);
                string imagePath = parameters.ImageFiles[index];
                string fileName = Path.GetFileName(imagePath);

                // 读取图像
                Mat image = Cv2.ImRead(imagePath, ImreadModes.Color);
                Mat imageRgb = new Mat();
                Cv2.CvtColor(image, imageRgb, ColorConversionCodes.BGR2RGB);

                if (image.Empty())
                {
                    return;
                }

                // 设置推理参数
                JObject data = new JObject
                {
                    ["threshold"] = 0.5f,
                    ["with_mask"] = true
                };

                // 执行推理
                CSharpResult result = parameters.Model.Infer(imageRgb, data);

                // 对比结果
                if (parameters.SingleThreadResults.ContainsKey(fileName))
                {
                    CSharpResult singleThreadResult = parameters.SingleThreadResults[fileName];
                    ComparisonResult comparison = CompareResultsDetailed(singleThreadResult, result);
                    
                    // 创建比较记录
                    ComparisonRecord record = new ComparisonRecord
                    {
                        ImageId = fileName,
                        IsConsistent = comparison.IsConsistent,
                        SingleThreadResultSummary = BuildResultSummary(singleThreadResult),
                        MultiThreadResultSummary = BuildResultSummary(result),
                        DifferenceSummary = comparison.Differences
                    };
                    
                    // 添加到记录集合
                    lock (_recordsLock)
                    {
                        _comparisonRecords.Add(record);
                    }
                    
                    // 记录日志
                    if (!comparison.IsConsistent)
                    {
                        string message = $"警告: 图像 {fileName} 的多线程结果与单线程结果不一致!\n" +
                                         $"单线程结果: {record.SingleThreadResultSummary}\n" +
                                         $"多线程结果: {record.MultiThreadResultSummary}\n" +
                                         $"差异: {comparison.Differences}";
                        
                        AppendLogThreadSafe(message);

                        // 保存不一致的结果
                        if (parameters.SaveResults)
                        {
                            // 保存多线程结果
                            string multiThreadResultPath = Path.Combine(parameters.OutputFolder,
                                Path.GetFileNameWithoutExtension(fileName) + "_multithread_" +
                                DateTime.Now.Ticks.ToString() + ".json");
                            SaveResultToFile(result, multiThreadResultPath);
                            
                            // 保存比较结果
                            string comparisonPath = Path.Combine(parameters.OutputFolder,
                                Path.GetFileNameWithoutExtension(fileName) + "_comparison_" +
                                DateTime.Now.Ticks.ToString() + ".txt");
                            
                            File.WriteAllText(comparisonPath, message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 在压力测试中捕获异常但不中断
                Console.WriteLine("推理异常: " + ex.Message);
            }
        }

        private class ComparisonResult
        {
            public bool IsConsistent { get; set; }
            public string Differences { get; set; }
        }
        
        private class ComparisonRecord
        {
            public string ImageId { get; set; }
            public bool IsConsistent { get; set; }
            public string SingleThreadResultSummary { get; set; }
            public string MultiThreadResultSummary { get; set; }
            public string DifferenceSummary { get; set; }
        }

        private ComparisonResult CompareResultsDetailed(CSharpResult result1, CSharpResult result2)
        {
            ComparisonResult comparison = new ComparisonResult
            {
                IsConsistent = true,
                Differences = ""
            };
            
            StringBuilder differences = new StringBuilder();
            
            // 比较样本数量
            if (result1.SampleResults.Count != result2.SampleResults.Count)
            {
                comparison.IsConsistent = false;
                differences.AppendLine($"样本数量不一致: {result1.SampleResults.Count} vs {result2.SampleResults.Count}");
                return comparison;
            }
            
            for (int i = 0; i < result1.SampleResults.Count; i++)
            {
                var sample1 = result1.SampleResults[i];
                var sample2 = result2.SampleResults[i];
                
                // 比较结果数量
                if (sample1.Results.Count != sample2.Results.Count)
                {
                    comparison.IsConsistent = false;
                    differences.AppendLine($"样本 {i} 检测结果数量不一致: {sample1.Results.Count} vs {sample2.Results.Count}");
                    continue;
                }
                
                // 详细比较每个检测结果
                for (int j = 0; j < sample1.Results.Count; j++)
                {
                    var obj1 = sample1.Results[j];
                    var obj2 = sample2.Results[j];
                    
                    // 比较类别ID
                    if (obj1.CategoryId != obj2.CategoryId)
                    {
                        comparison.IsConsistent = false;
                        differences.AppendLine($"样本 {i}, 结果 {j} 类别ID不一致: {obj1.CategoryId} vs {obj2.CategoryId}");
                    }
                    
                    // 比较类别名称
                    if (obj1.CategoryName != obj2.CategoryName)
                    {
                        comparison.IsConsistent = false;
                        differences.AppendLine($"样本 {i}, 结果 {j} 类别名称不一致: {obj1.CategoryName} vs {obj2.CategoryName}");
                    }
                    
                    // 比较分数
                    float scoreDiff = Math.Abs(obj1.Score - obj2.Score);
                    if (scoreDiff > 0.001f)
                    {
                        comparison.IsConsistent = false;
                        differences.AppendLine($"样本 {i}, 结果 {j} 分数不一致: {obj1.Score:F4} vs {obj2.Score:F4}");
                    }
                    
                    // 比较区域
                    float areaDiff = Math.Abs(obj1.Area - obj2.Area);
                    if (areaDiff > 0.1f)
                    {
                        comparison.IsConsistent = false;
                        differences.AppendLine($"样本 {i}, 结果 {j} 区域不一致: {obj1.Area:F1} vs {obj2.Area:F1}");
                    }
                    
                    // 比较边界框
                    if (obj1.Bbox.Count != obj2.Bbox.Count)
                    {
                        comparison.IsConsistent = false;
                        differences.AppendLine($"样本 {i}, 结果 {j} 边界框维度不一致");
                    }
                    else
                    {
                        for (int k = 0; k < obj1.Bbox.Count; k++)
                        {
                            double bboxDiff = Math.Abs(obj1.Bbox[k] - obj2.Bbox[k]);
                            if (bboxDiff > 0.1)
                            {
                                comparison.IsConsistent = false;
                                differences.AppendLine($"样本 {i}, 结果 {j} 边界框坐标 {k} 不一致: {obj1.Bbox[k]:F1} vs {obj2.Bbox[k]:F1}");
                            }
                        }
                    }
                }
            }
            
            comparison.Differences = differences.ToString();
            return comparison;
        }
        
        private string BuildResultSummary(CSharpResult result)
        {
            StringBuilder summary = new StringBuilder();
            
            foreach (var sample in result.SampleResults)
            {
                summary.Append($"[{sample.Results.Count}个结果] ");
                
                foreach (var obj in sample.Results)
                {
                    summary.Append($"({obj.CategoryName}, {obj.Score:F3}, bbox:[");
                    for (int i = 0; i < obj.Bbox.Count; i++)
                    {
                        summary.Append($"{obj.Bbox[i]:F1}");
                        if (i < obj.Bbox.Count - 1)
                            summary.Append(",");
                    }
                    summary.Append("]) ");
                }
            }
            
            return summary.ToString();
        }
        
        private void SaveComparisonResults()
        {
            try
            {
                string comparisonSummaryPath = Path.Combine(_outputFolder, "comparison_summary.csv");
                
                // 创建CSV文件
                using (StreamWriter writer = new StreamWriter(comparisonSummaryPath))
                {
                    // 写入标题行
                    writer.WriteLine("图片ID,结果是否一致,单线程结果,多线程结果,差异详情");
                    
                    // 写入每一条记录
                    foreach (var record in _comparisonRecords)
                    {
                        // 处理CSV中的逗号和换行
                        string singleThreadResult = EscapeCsvField(record.SingleThreadResultSummary);
                        string multiThreadResult = EscapeCsvField(record.MultiThreadResultSummary);
                        string differences = EscapeCsvField(record.DifferenceSummary);
                        
                        writer.WriteLine($"{record.ImageId},{record.IsConsistent},{singleThreadResult},{multiThreadResult},{differences}");
                    }
                }
                
                AppendLog($"已保存结果比较汇总到: {comparisonSummaryPath}");
                
                // 创建一个更易读的TXT版本
                string txtSummaryPath = Path.Combine(_outputFolder, "comparison_summary.txt");
                using (StreamWriter writer = new StreamWriter(txtSummaryPath))
                {
                    writer.WriteLine("DLCV多线程推理结果比较汇总");
                    writer.WriteLine("==========================");
                    writer.WriteLine($"日期时间: {DateTime.Now}");
                    writer.WriteLine($"总测试图像数: {_comparisonRecords.Count}");
                    
                    int inconsistentCount = _comparisonRecords.Count(r => !r.IsConsistent);
                    writer.WriteLine($"结果不一致图像数: {inconsistentCount}");
                    writer.WriteLine("==========================");
                    
                    foreach (var record in _comparisonRecords)
                    {
                        if (!record.IsConsistent)
                        {
                            writer.WriteLine($"\n图片: {record.ImageId}");
                            writer.WriteLine($"单线程结果: {record.SingleThreadResultSummary}");
                            writer.WriteLine($"多线程结果: {record.MultiThreadResultSummary}");
                            writer.WriteLine($"差异: {record.DifferenceSummary}");
                            writer.WriteLine("----------------------------");
                        }
                    }
                }
                
                AppendLog($"已保存可读版结果比较汇总到: {txtSummaryPath}");
            }
            catch (Exception ex)
            {
                AppendLog($"保存比较结果失败: {ex.Message}");
            }
        }
        
        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "";
                
            // 如果字段包含逗号、引号或换行，需要用引号包围并将内部引号转义
            bool needQuotes = field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r");
            
            if (needQuotes)
            {
                // 将字段中的引号替换为两个引号(CSV转义规则)
                field = field.Replace("\"", "\"\"");
                return $"\"{field}\"";
            }
            
            return field;
        }

        private void SaveResultToFile(CSharpResult result, string filePath)
        {
            try
            {
                // 构建结果JSON
                JObject resultJson = new JObject();
                JArray sampleResults = new JArray();

                foreach (var sample in result.SampleResults)
                {
                    JObject sampleJson = new JObject();
                    JArray resultsArray = new JArray();

                    foreach (var obj in sample.Results)
                    {
                        JObject objJson = new JObject
                        {
                            ["category_id"] = obj.CategoryId,
                            ["category_name"] = obj.CategoryName,
                            ["score"] = obj.Score,
                            ["area"] = obj.Area,
                            ["bbox"] = JArray.FromObject(obj.Bbox)
                        };

                        resultsArray.Add(objJson);
                    }

                    sampleJson["results"] = resultsArray;
                    sampleResults.Add(sampleJson);
                }

                resultJson["sample_results"] = sampleResults;

                // 保存到文件
                File.WriteAllText(filePath, resultJson.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine("保存结果失败: " + ex.Message);
            }
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar.Value = e.ProgressPercentage;
        }

        private bool ValidateBeforeTest()
        {
            if (_model == null)
            {
                MessageBox.Show("请先加载模型！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (_imageFiles.Count == 0)
            {
                MessageBox.Show("请先选择包含图像的文件夹！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private void SetButtonsEnabled(bool enabled)
        {
            btnLoadModel.Enabled = enabled;
            btnSelectFolder.Enabled = enabled;
            btnSingleTest.Enabled = enabled;
            btnPressureTest.Enabled = enabled;
            cmbDevices.Enabled = enabled;
            nudThreadCount.Enabled = enabled;
            nudBatchSize.Enabled = enabled;
            chkSaveResults.Enabled = enabled;
        }

        private void AppendLog(string message, bool replace = false)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, bool>(AppendLog), message, replace);
                return;
            }

            if (replace)
                txtResults.Text = message;
            else
                txtResults.AppendText(DateTime.Now.ToString("[HH:mm:ss] ") + message + Environment.NewLine);

            // 滚动到底部
            txtResults.SelectionStart = txtResults.Text.Length;
            txtResults.ScrollToCaret();
        }

        private void AppendLogThreadSafe(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(AppendLogThreadSafe), message);
                return;
            }

            txtResults.AppendText(DateTime.Now.ToString("[HH:mm:ss] ") + message + Environment.NewLine);
            txtResults.SelectionStart = txtResults.Text.Length;
            txtResults.ScrollToCaret();
        }

        private class PressureTestParameters
        {
            public List<string> ImageFiles { get; set; }
            public Model Model { get; set; }
            public Dictionary<string, CSharpResult> SingleThreadResults { get; set; }
            public string OutputFolder { get; set; }
            public bool SaveResults { get; set; }
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_runner != null && _runner.IsRunning)
            {
                _runner.Stop();
                _updateTimer.Stop();
            }

            if (_model != null)
            {
                _model = null;
                GC.Collect();
            }

            base.OnFormClosing(e);
        }
    }
}