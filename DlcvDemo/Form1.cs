using System;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using static dlcv_infer_csharp.Utils;
using DLCV;
using System.Text;
using Newtonsoft.Json;

namespace DlcvDemo
{
    public partial class Form1 : Form
    {
        // 设备映射表：设备名称 -> 设备ID
        private Dictionary<string, int> deviceNameToIdMap = new Dictionary<string, int>();

        /// <summary>
        /// 获取当前选中的设备ID
        /// </summary>
        /// <returns>设备ID，如果没有选中则返回-1</returns>
        private int GetSelectedDeviceId()
        {
            if (comboBox1.SelectedItem == null)
            {
                return -1; // 默认使用CPU
            }

            string selectedDeviceName = comboBox1.SelectedItem.ToString();
            if (deviceNameToIdMap.ContainsKey(selectedDeviceName))
            {
                return deviceNameToIdMap[selectedDeviceName];
            }

            return -1; // 默认使用CPU
        }

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            TopMost = false;
            Thread thread = new Thread(GetDeviceInfo);
            thread.IsBackground = true;
            thread.Start();
        }

        private void GetDeviceInfo()
        {
            Thread.Sleep(1);
            JObject device_info = Utils.GetGpuInfo();

            Invoke((MethodInvoker)delegate
            {
                // 清空现有项目和映射表
                comboBox1.Items.Clear();
                deviceNameToIdMap.Clear();

                // 始终先添加CPU选项
                comboBox1.Items.Add("CPU");
                deviceNameToIdMap["CPU"] = -1; // CPU对应device_id = -1

                // 添加GPU设备
                bool hasGpu = false;
                if (device_info.GetValue("code").ToString() == "0")
                {
                    int gpuIndex = 0;
                    foreach (var item in device_info.GetValue("devices"))
                    {
                        string deviceName = item["device_name"].ToString();
                        comboBox1.Items.Add(deviceName);
                        deviceNameToIdMap[deviceName] = gpuIndex; // GPU设备名称对应device_id = 0, 1, 2...
                        gpuIndex++;
                        hasGpu = true;
                    }
                }
                else
                {
                    // 如果获取GPU信息失败，在richTextBox1中显示错误信息
                    richTextBox1.Text = "GPU信息获取失败：\n" + device_info.ToString();
                }

                // 默认选择第一个显卡，如果没有显卡则选择CPU
                if (hasGpu)
                {
                    comboBox1.SelectedIndex = 1; // 选择第一个GPU
                }
                else
                {
                    comboBox1.SelectedIndex = 0; // 选择CPU
                }
            });

            var info = Utils.GetDeviceInfo();
            Console.WriteLine(info.ToString());
        }

        private dynamic model;
        private string image_path;
        private int batch_size = 1;
        private PressureTestRunner pressureTestRunner;
        private System.Windows.Forms.Timer updateTimer;
        private dynamic baselineJsonResult = null;
        private volatile bool shouldStopPressureTest = false;
        private bool isConsistencyTestMode = false; // 控制是否进行一致性测试

        private void button_loadmodel_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.RestoreDirectory = true;

            openFileDialog.Filter = "深度视觉模型文件 (*.dvt;*.dvp;*.dvo)|*.dvt;*.dvp;*.dvo";
            openFileDialog.Title = "选择模型";
            try
            {
                openFileDialog.InitialDirectory = Path.GetDirectoryName(Properties.Settings.Default.LastModelPath);
                openFileDialog.FileName = Path.GetFileName(Properties.Settings.Default.LastModelPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string selectedFilePath = openFileDialog.FileName;
                Properties.Settings.Default.LastModelPath = selectedFilePath;
                Properties.Settings.Default.Save();
                int device_id = GetSelectedDeviceId();
                try
                {
                    if (model != null)
                    {
                        model = null;
                        GC.Collect();
                    }
                    model = new Model(selectedFilePath, device_id);
                    button_getmodelinfo_Click(sender, e);
                }
                catch (Exception ex)
                {
                    richTextBox1.Text = ex.Message;
                }
            }
        }

        private void button_getmodelinfo_Click(object sender, EventArgs e)
        {
            if (model == null)
            {
                MessageBox.Show("请先加载模型文件！");
                return;
            }
            JObject result = model.GetModelInfo();
            if (result.ContainsKey("model_info"))
            {
                result = (JObject)result["model_info"];
                richTextBox1.Text = result.ToString();
            }
            else if (result.ContainsKey("det_model"))
            {
                JObject new_result = new JObject();
                new_result["det_model"] = (JObject)result["det_model"]["model_info"];
                new_result["ocr_model"] = (JObject)result["ocr_model"]["model_info"];
                richTextBox1.Text = new_result.ToString();
            }
            else if (result.ContainsKey("code"))
            {
                richTextBox1.Text = result.ToString();
                return;
            }
        }

        private void button_openimage_Click(object sender, EventArgs e)
        {
            if (model == null)
            {
                MessageBox.Show("请先加载模型文件！");
                return;
            }
            OpenFileDialog openFileDialog = new OpenFileDialog();

            openFileDialog.Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif|所有文件 (*.*)|*.*";
            openFileDialog.Title = "选择图片文件";

            try
            {
                openFileDialog.InitialDirectory = Path.GetDirectoryName(Properties.Settings.Default.LastImagePath);
                openFileDialog.FileName = Path.GetFileName(Properties.Settings.Default.LastImagePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                image_path = openFileDialog.FileName;
                Properties.Settings.Default.LastImagePath = image_path;
                Properties.Settings.Default.Save();
                button_infer_Click(sender, e);
            }
        }

        private void button_infer_Click(object sender, EventArgs e)
        {
            if (model == null)
            {
                MessageBox.Show("请先加载模型文件！");
                return;
            }
            if (image_path == null)
            {
                MessageBox.Show("请先选择图片文件！");
                return;
            }

            Mat image = Cv2.ImRead(image_path, ImreadModes.Color);
            Mat image_rgb = new Mat();
            Cv2.CvtColor(image, image_rgb, ColorConversionCodes.BGR2RGB);

            batch_size = (int)numericUpDown_batch_size.Value;
            var image_list = new List<Mat>();
            for (int i = 0; i < batch_size; i++)
            {
                image_list.Add(image_rgb);
            }

            if (image.Empty())
            {
                Console.WriteLine("图像解码失败！");
                return;
            }

            JObject data = new JObject();
            data["threshold"] = float.Parse(textBox_threshold.Text);
            data["with_mask"] = true;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            CSharpResult result = model.InferBatch(image_list, data);

            stopwatch.Stop();
            double delay_ms = stopwatch.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine($"推理时间: {delay_ms:F2}ms");

            imagePanel1.UpdateImageAndResult(image, result);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"推理时间: {delay_ms:F2}ms\n");
            sb.AppendLine($"推理结果: ");
            sb.AppendLine(result.SampleResults[0].ToString());
            richTextBox1.Text = sb.ToString();
        }

        /// <summary>
        /// 执行模型推理的测试操作
        /// </summary>
        /// <param name="parameter">包含图像列表的参数</param>
        private void ModelInferAction(object parameter)
        {
            try
            {
                // 如果需要停止测试，立即返回，不执行后续推理
                if (shouldStopPressureTest)
                {
                    return;
                }

                var image_list = (List<Mat>)parameter;
                if (image_list == null)
                {
                    return;
                }

                // 调用InferInternal进行推理

                JObject infer_config = new JObject();
                infer_config["with_mask"] = false;

                var resultTuple = model.InferInternal(image_list, infer_config);
                IntPtr currentResultPtr = resultTuple.Item2;

                try
                {
                    // 根据模式决定是否进行一致性检查
                    if (isConsistencyTestMode)
                    {
                        String thread_id = Thread.CurrentThread.ManagedThreadId.ToString("00");
                        // 一致性测试模式：比较推理结果
                        // 第一次推理时获取基准结果
                        if (baselineJsonResult == null)
                        {
                            Debug.WriteLine($"线程{thread_id}基准结果为空，写入基准结果……");
                            baselineJsonResult = resultTuple.Item1;
                            string baselineJson = JsonConvert.SerializeObject(baselineJsonResult, Formatting.None);
                            Debug.WriteLine($"线程{thread_id}写入基准结果完成。");
                            Debug.WriteLine($"线程{thread_id}基准结果：" + baselineJson);
                            return; // 第一次推理，获取基准后直接返回
                        }
                        else
                        {
                            // 再次检查是否需要停止测试（可能在其他线程中被设置）
                            if (shouldStopPressureTest)
                            {
                                return;
                            }

                            // 直接比较JSON字符串
                            string baselineJson = JsonConvert.SerializeObject(baselineJsonResult, Formatting.None);
                            string currentJson = JsonConvert.SerializeObject(resultTuple.Item1, Formatting.None);

                            if (baselineJson != currentJson)
                            {
                                Debug.WriteLine($"线程{thread_id}基准结果与当前结果不一致");
                                Debug.WriteLine($"线程{thread_id}基准结果：" + baselineJson);
                                Debug.WriteLine($"线程{thread_id}当前结果：" + currentJson);

                                // 立即设置停止标志，防止其他线程继续执行
                                shouldStopPressureTest = true;

                                // 发现不一致，向主线程报告
                                Invoke((MethodInvoker)delegate
                                {
                                    // 立即停止测试
                                    StopPressureTest();

                                    StringBuilder sb = new StringBuilder();
                                    sb.AppendLine("发现推理结果不一致！测试已停止。");
                                    sb.AppendLine("=== 基准结果 ===");
                                    sb.AppendLine(JsonConvert.SerializeObject(baselineJsonResult, Formatting.Indented));
                                    sb.AppendLine("\n=== 当前结果 ===");
                                    sb.AppendLine(JsonConvert.SerializeObject(resultTuple.Item1, Formatting.Indented));
                                    string s = sb.ToString();

                                    richTextBox1.Text = s;
                                    MessageBox.Show("检测到推理结果不一致，测试已停止！", "结果不一致", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                                    baselineJsonResult = null;
                                });
                            }
                        }
                    }
                    // 性能测试模式：不保存或比较结果，直接继续
                }
                finally
                {
                    if (currentResultPtr != IntPtr.Zero)
                    {
                        DllLoader.Instance.dlcv_free_model_result(currentResultPtr);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("模型推理出错: " + ex.Message);
                // 设置停止标志
                shouldStopPressureTest = true;

                // 向主线程报告错误
                Invoke((MethodInvoker)delegate
                {
                    StopPressureTest();
                    string testType = isConsistencyTestMode ? "一致性测试" : "压力测试";
                    MessageBox.Show($"{testType}过程中发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    richTextBox1.Text = $"推理错误: {ex.Message}";
                });
            }
        }

        /// <summary>
        /// 定时更新UI上的统计信息
        /// </summary>
        private void UpdateStatisticsTimer_Tick(object sender, EventArgs e)
        {
            if (pressureTestRunner != null && pressureTestRunner.IsRunning)
            {
                // 在UI上显示统计信息
                Invoke((MethodInvoker)delegate
                {
                    string stats = pressureTestRunner.GetStatistics(false);
                    if (isConsistencyTestMode && baselineJsonResult != null)
                    {
                        stats = stats + "\n\n" +
                                "基准结果:\n" +
                                JsonConvert.SerializeObject(baselineJsonResult, Formatting.Indented);
                    }
                    richTextBox1.Text = stats;
                });
            }
        }

        private void button_threadtest_Click(object sender, EventArgs e)
        {
            if (model == null)
            {
                MessageBox.Show("请先加载模型文件！");
                return;
            }
            if (image_path == null)
            {
                MessageBox.Show("请先选择图片文件！");
                return;
            }

            if (pressureTestRunner != null && pressureTestRunner.IsRunning)
            {
                StopPressureTest();
            }
            else
            {
                StartPressureTest(false); // 性能测试模式
            }
        }

        /// <summary>
        /// 启动测试
        /// </summary>
        private void StartPressureTest(bool consistencyTestMode)
        {
            try
            {
                // 设置测试模式
                this.isConsistencyTestMode = consistencyTestMode;

                // 重置测试停止标志
                shouldStopPressureTest = false;

                // 准备测试数据
                batch_size = (int)numericUpDown_batch_size.Value;
                int threadCount = (int)numericUpDown_num_thread.Value;

                // 读取图像并转换为RGB格式
                Mat image = Cv2.ImRead(image_path, ImreadModes.Color);
                Mat image_rgb = new Mat();
                Cv2.CvtColor(image, image_rgb, ColorConversionCodes.BGR2RGB);

                // 创建批量图像列表
                var image_list = new List<Mat>();
                for (int i = 0; i < batch_size; i++)
                {
                    image_list.Add(image_rgb);
                }

                // 创建测试实例
                pressureTestRunner = new PressureTestRunner(threadCount, 1000000, batch_size);
                pressureTestRunner.SetTestAction(ModelInferAction, image_list);

                // 创建并启动定时器更新UI
                if (updateTimer == null)
                {
                    updateTimer = new System.Windows.Forms.Timer();
                    updateTimer.Interval = 500;
                    updateTimer.Tick += UpdateStatisticsTimer_Tick;
                }
                updateTimer.Start();

                // 启动测试
                pressureTestRunner.Start();

                // 根据模式设置按钮文本
                if (consistencyTestMode)
                {
                    button_consistency_test.Text = "停止";
                }
                else
                {
                    button_thread_test.Text = "停止";
                }
            }
            catch (Exception ex)
            {
                string testType = consistencyTestMode ? "一致性测试" : "压力测试";
                MessageBox.Show($"启动{testType}失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 停止测试
        /// </summary>
        private void StopPressureTest()
        {
            if (pressureTestRunner != null)
            {
                pressureTestRunner.Stop();

                // 停止定时器
                if (updateTimer != null)
                {
                    updateTimer.Stop();
                }

                // 清理资源
                shouldStopPressureTest = false; // 重置测试停止标志

                // 根据模式重置按钮文本
                if (isConsistencyTestMode)
                {
                    button_consistency_test.Text = "一致性测试";
                }
                else
                {
                    button_thread_test.Text = "多线程测试";
                }

                // 重置测试模式
                isConsistencyTestMode = false;
            }
        }

        /// <summary>
        /// 一致性测试按钮点击事件
        /// </summary>
        private void button_consistency_test_Click(object sender, EventArgs e)
        {
            if (model == null)
            {
                MessageBox.Show("请先加载模型文件！");
                return;
            }
            if (image_path == null)
            {
                MessageBox.Show("请先选择图片文件！");
                return;
            }

            if (pressureTestRunner != null && pressureTestRunner.IsRunning)
            {
                StopPressureTest();
            }
            else
            {
                StartPressureTest(true); // 一致性测试模式
            }
        }

        private void button_freemodel_Click(object sender, EventArgs e)
        {
            // 如果存在正在运行的压力测试，先停止它
            StopPressureTest();

            model = null;
            GC.Collect();
            richTextBox1.Text = "模型已释放";
        }

        private void button_github_Click(object sender, EventArgs e)
        {
            Process.Start("https://docs.dlcv.com.cn/deploy/csharp_sdk");
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopPressureTest();
            // 释放模型
            if (model != null && model is IDisposable disposable)
            {
                model.Dispose();
                model = null;
            }
            Utils.FreeAllModels();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            JArray sntl_info = sntl_admin_csharp.SNTLUtils.GetDeviceList();
            JArray sntl_features = sntl_admin_csharp.SNTLUtils.GetFeatureList();
            richTextBox1.Text = "加密狗ID：\n" + sntl_info.ToString() + "\n\n" +
                "加密狗特性：\n" + sntl_features.ToString();
        }

        private void button_load_sliding_window_model_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.RestoreDirectory = true;

            openFileDialog.Filter = "深度视觉加速模型文件 (*.dvt)|*.dvt";
            openFileDialog.Title = "选择模型";
            try
            {
                openFileDialog.InitialDirectory = Path.GetDirectoryName(Properties.Settings.Default.LastModelPath);
                openFileDialog.FileName = Path.GetFileName(Properties.Settings.Default.LastModelPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string selectedFilePath = openFileDialog.FileName;
                Properties.Settings.Default.LastModelPath = selectedFilePath;
                Properties.Settings.Default.Save();
                int device_id = GetSelectedDeviceId();

                // 显示参数配置窗口
                using (var configForm = new SlidingWindowConfigForm())
                {
                    if (configForm.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            if (model != null)
                            {
                                model = null;
                                GC.Collect();
                            }
                            model = new SlidingWindowModel(
                                selectedFilePath,
                                device_id,
                                configForm.SmallImgWidth,
                                configForm.SmallImgHeight,
                                configForm.HorizontalOverlap,
                                configForm.VerticalOverlap,
                                configForm.Threshold,
                                configForm.IouThreshold,
                                configForm.CombineIosThreshold
                            );
                            button_getmodelinfo_Click(sender, e);
                        }
                        catch (Exception ex)
                        {
                            richTextBox1.Text = ex.Message;
                        }
                    }
                }
            }
        }

        private void button_load_ocr_model_Click(object sender, EventArgs e)
        {
            // 使用新的OCR模型配置窗口
            using (var ocrConfigForm = new OcrModelConfigForm(GetSelectedDeviceId()))
            {
                if (ocrConfigForm.ShowDialog(this) == DialogResult.OK)
                {
                    string detModelPath = ocrConfigForm.DetModelPath;
                    string ocrModelPath = ocrConfigForm.OcrModelPath;
                    int deviceId = ocrConfigForm.DeviceId;
                    float horizontalScale = ocrConfigForm.HorizontalScale;

                    try
                    {
                        // 释放旧模型
                        if (model != null)
                        {
                            if (model is IDisposable disposable)
                            {
                                disposable.Dispose();
                            }
                            model = null;
                        }

                        // 创建新的OCR模型
                        model = new OcrWithDetModel();
                        model.Load(detModelPath, ocrModelPath, deviceId);
                        model.SetHorizontalScale(horizontalScale);

                        richTextBox1.Text = "OCR模型加载成功！\n" +
                                          $"检测模型: {Path.GetFileName(detModelPath)}\n" +
                                          $"OCR模型: {Path.GetFileName(ocrModelPath)}\n" +
                                          $"设备ID: {deviceId}\n" +
                                          $"水平缩放比例: {horizontalScale}";
                    }
                    catch (Exception ex)
                    {
                        richTextBox1.Text = $"加载OCR模型失败：{ex.Message}";
                        if (model != null && model is IDisposable disposable)
                        {
                            model.Dispose();
                            model = null;
                        }
                        MessageBox.Show($"加载OCR模型失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void button_free_all_model_Click(object sender, EventArgs e)
        {
            // 如果存在正在运行的压力测试，先停止它
            StopPressureTest();

            // 释放模型
            if (model != null && model is IDisposable disposable)
            {
                model.Dispose();
            }
            model = null;
            Utils.FreeAllModels();
            richTextBox1.Text = "所有模型已释放";
        }
    }
}
