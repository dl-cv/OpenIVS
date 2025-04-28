using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
                    richTextBoxResult.Text = "识别模型加载成功！";
                }
                catch (Exception ex)
                {
                    richTextBoxResult.Text = $"识别模型加载错误：{ex.Message}";
                }
            }
        }

        /// <summary>
        /// 打开图片
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
        }

        /// <summary>
        /// 执行OCR推理
        /// </summary>
        private void btnInfer_Click(object sender, EventArgs e)
        {
            if (!_isDetectModelLoaded || !_isRecognizeModelLoaded)
            {
                MessageBox.Show("请先加载检测模型和识别模型！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                CSharpResult result = Utils.OcrInfer(_detectModel, _recognizeModel, imageRgb);

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

                richTextBoxResult.Text = sb.ToString();
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
    }
}
