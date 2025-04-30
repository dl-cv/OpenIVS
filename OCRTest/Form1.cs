using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using DLCV;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using static dlcv_infer_csharp.Utils;

namespace OCRTest
{
    public partial class Form1 : Form
    {
        private Model _detectModel; // 检测模型
        private Model _recognizeModel; // 识别模型
        private string _imagePath; // 图像路径
        private bool _isDetectModelLoaded = false; // 检测模型是否加载
        private bool _isRecognizeModelLoaded = false; // 识别模型是否加载
        private int _deviceId = 0; // GPU设备ID
        private PressureTestRunner _pressureTestRunner;
        private System.Windows.Forms.Timer _updateTimer;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // 初始化窗体和获取GPU设备信息
            TopMost = false;
            Thread thread = new Thread(GetDeviceInfo);
            thread.IsBackground = true;
            thread.Start();
        }

        /// <summary>
        /// 获取GPU设备信息并填充到下拉框
        /// </summary>
        private void GetDeviceInfo()
        {
            Thread.Sleep(1);
            JObject device_info = Utils.GetGpuInfo();

            Invoke((MethodInvoker)delegate
            {
                if (device_info.GetValue("code").ToString() == "0")
                {
                    foreach (var item in device_info.GetValue("devices"))
                    {
                        comboBoxDevices.Items.Add(item["device_name"].ToString());
                    }
                }
                else
                {
                    richTextBoxResult.Text = device_info.ToString();
                }

                if (comboBoxDevices.Items.Count == 0)
                {
                    comboBoxDevices.Items.Add("Unknown");
                }
                comboBoxDevices.SelectedIndex = 0;
            });
        }

        /// <summary>
        /// 加载检测模型
        /// </summary>
        private void btnLoadDetectModel_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.RestoreDirectory = true;

            openFileDialog.Filter = "深度视觉加速模型文件 (*.dvt)|*.dvt";
            openFileDialog.Title = "选择检测模型";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string selectedFilePath = openFileDialog.FileName;
                _deviceId = comboBoxDevices.SelectedIndex;
                try
                {
                    // 释放之前的模型资源（如果有）
                    if (_isDetectModelLoaded)
                    {
                        _detectModel = null;
                        GC.Collect();
                    }

                    // 加载新模型
                    richTextBoxResult.Text = "模型加载中";
                    _detectModel = new Model(selectedFilePath, _deviceId);
                    _isDetectModelLoaded = true;
                    labelDetectModel.Text = $"检测模型：{Path.GetFileName(selectedFilePath)}";
                    richTextBoxResult.Text = "检测模型加载成功！";
                }
                catch (Exception ex)
                {
                    richTextBoxResult.Text = $"检测模型加载错误：{ex.Message}";
                }
            }
        }

        /// <summary>
        /// 加载识别模型
        /// </summary>
        private void btnLoadRecognizeModel_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.RestoreDirectory = true;

            openFileDialog.Filter = "深度视觉加速模型文件 (*.dvt)|*.dvt";
            openFileDialog.Title = "选择识别模型";
            //labelRecognizeModel.Text = "模型加载中";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                
                string selectedFilePath = openFileDialog.FileName;
                _deviceId = comboBoxDevices.SelectedIndex;
                
                try
                {
                    // 释放之前的模型资源（如果有）
                    if (_isRecognizeModelLoaded)
                    {
                        _recognizeModel = null;
                        GC.Collect();
                    }

                    // 加载新模型
                    
                    _recognizeModel = new Model(selectedFilePath, _deviceId);
                    _isRecognizeModelLoaded = true;
                    labelRecognizeModel.Text = $"识别模型：{Path.GetFileName(selectedFilePath)}";
                    richTextBoxResult.Text = "OCR模型加载成功！";
                }
                catch (Exception ex)
                {
                    richTextBoxResult.Text = $"OCR模型加载错误：{ex.Message}";
                }
            }
        }

        /// <summary>
        /// 打开图片，并执行OCR推理
        /// </summary>
        private void btnOpenImage_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件 (*.*)|*.*";
            openFileDialog.Title = "选择图片文件";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                _imagePath = openFileDialog.FileName;
                try
                {
                    Mat image = Cv2.ImRead(_imagePath, ImreadModes.Color);
                    // 加载并显示图像

                    imageViewer.UpdateImage(image);
                    richTextBoxResult.Text = $"已加载图片：{Path.GetFileName(_imagePath)}";

                }
                catch (Exception ex)
                {
                    richTextBoxResult.Text = $"加载图片失败：{ex.Message}";
                }
            }

            if (!_isDetectModelLoaded || !_isRecognizeModelLoaded)
            {
                MessageBox.Show("请先加载目标检测模型和OCR识别模型！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_imagePath))
            {
                MessageBox.Show("请先选择图片文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // 加载图像
                Mat image = Cv2.ImRead(_imagePath, ImreadModes.Color);
                Mat imageRgb = new Mat();
                Cv2.CvtColor(image, imageRgb, ColorConversionCodes.BGR2RGB);

                if (image.Empty())
                {
                    richTextBoxResult.Text = "图像解码失败！";
                    return;
                }

                // 执行OCR推理
                richTextBoxResult.Text = "开始执行OCR推理...";
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                CSharpResult result = Utils.OcrInfer(_detectModel, _recognizeModel, imageRgb);
                stopwatch.Stop();

                // 更新图像显示
                imageViewer.UpdateImageAndResult(image, result);


                // 显示结果
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("OCR推理结果：");
                sb.AppendLine("----------------------");

                if (result.SampleResults.Count > 0)
                {
                    var sampleResult = result.SampleResults[0];
                    foreach (var item in sampleResult.Results)
                    {
                        sb.AppendLine($"文本：{item.CategoryName}");
                        sb.AppendLine($"置信度：{item.Score:F2}");
                        sb.AppendLine($"位置：[{item.Bbox[0]:F0},{item.Bbox[1]:F0},{item.Bbox[2]:F0},{item.Bbox[3]:F0}]");
                        sb.AppendLine("----------------------");
                    }
                }
                else
                {
                    sb.AppendLine("未检测到文本");
                }

                richTextBoxResult.Text = sb.ToString() + $"\n推理耗时：{stopwatch.ElapsedMilliseconds}毫秒";
            }
            catch (Exception ex)
            {
                richTextBoxResult.Text = $"OCR推理失败：{ex.Message}";
            }
        }



        /// <summary>
        /// 释放模型资源
        /// </summary>
        private void btnFreeModel_Click(object sender, EventArgs e)
        {
            try
            {
                if (_isDetectModelLoaded)
                {
                    _detectModel = null;
                    _isDetectModelLoaded = false;
                    labelDetectModel.Text = "检测模型：未加载";
                }

                if (_isRecognizeModelLoaded)
                {
                    _recognizeModel = null;
                    _isRecognizeModelLoaded = false;
                    labelRecognizeModel.Text = "识别模型：未加载";
                }

                GC.Collect();
                richTextBoxResult.Text = "模型已释放";
            }
            catch (Exception ex)
            {
                richTextBoxResult.Text = $"释放模型失败：{ex.Message}";
            }
        }

        /// <summary>
        /// 窗体关闭时释放资源
        /// </summary>
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 释放模型资源
            if (_isDetectModelLoaded)
            {
                _detectModel = null;
                _isDetectModelLoaded = false;
            }

            if (_isRecognizeModelLoaded)
            {
                _recognizeModel = null;
                _isRecognizeModelLoaded = false;
            }

            GC.Collect();
        }

        private void btnStartStressTest_Click(object sender, EventArgs e)
        {
            if (_pressureTestRunner != null && _pressureTestRunner.IsRunning)
            {
                StopPressureTest();
            }
            else
            {
                StartPressureTest();
            }
        }

        private void StartPressureTest()
        {
            if (!_isDetectModelLoaded || !_isRecognizeModelLoaded)
            {
                MessageBox.Show("请先加载目标检测模型和OCR识别模型！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_imagePath))
            {
                MessageBox.Show("请先选择图片文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // 准备测试数据
                int batchSize = 1; // 可根据需求调整批量大小
                int threadCount = 1; // 可根据需求调整线程数量

                // 读取图像并创建批处理列表
                Mat image = Cv2.ImRead(_imagePath, ImreadModes.Color);
                Cv2.CvtColor(image, image, ColorConversionCodes.BGR2RGB);
                //var imageList = new List<Mat>();
                //for (int i = 0; i < batchSize; i++)
                //{
                //    imageList.Add(image);
                //}

                // 创建压力测试实例
                _pressureTestRunner = new PressureTestRunner(threadCount, 1000, batchSize);
                //_pressureTestRunner.SetTestAction(ModelInferAction, image);
                _pressureTestRunner.SetTestAction(o => ModelInferAction((Mat)o), image);

                // 创建并启动定时器更新UI
                if (_updateTimer == null)
                {
                    _updateTimer = new System.Windows.Forms.Timer();
                    _updateTimer.Interval = 500;
                    _updateTimer.Tick += UpdateStatisticsTimer_Tick;
                }
                _updateTimer.Start();

                // 启动测试
                _pressureTestRunner.Start();
                btnStartStressTest.Text = "停止压力测试";
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动压力测试失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void StopPressureTest()
        {
            if (_pressureTestRunner != null)
            {
                _pressureTestRunner.Stop();

                // 停止定时器
                if (_updateTimer != null)
                {
                    _updateTimer.Stop();
                }

                // 显示最终统计信息
                btnStartStressTest.Text = "开始压力测试";
                richTextBoxResult.Text = _pressureTestRunner.GetStatistics();
            }
        }

        private void ModelInferAction(Mat parameter)
        {
            try
            {
                // 执行批量推理
                //var result = _detectModel.InferBatch((List<Mat>)parameter); //_recognizeModel
                CSharpResult result = Utils.OcrInfer(_detectModel, _recognizeModel, parameter);


                // 推理成功，无需额外处理
            }
            catch (Exception ex)
            {
                Debug.WriteLine("模型推理出错: " + ex.Message);
                Invoke((MethodInvoker)delegate
                {
                    StopPressureTest();
                    MessageBox.Show($"压力测试过程中发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    richTextBoxResult.Text = $"推理错误: {ex.Message}";
                });
            }
        }


        private void UpdateStatisticsTimer_Tick(object sender, EventArgs e)
        {
            if (_pressureTestRunner != null && _pressureTestRunner.IsRunning)
            {
                Invoke((MethodInvoker)delegate
                {
                    richTextBoxResult.Text = _pressureTestRunner.GetStatistics(false);
                });
            }
        }
    }
}
