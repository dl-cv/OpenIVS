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

namespace demo
{
    public partial class Form1 : Form
    {
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
                if (device_info.GetValue("code").ToString() == "0")
                {

                    foreach (var item in device_info.GetValue("devices"))
                    {
                        comboBox1.Items.Add(item["device_name"].ToString());
                    }
                }
                else
                {
                    richTextBox1.Text = device_info.ToString();
                }

                if (comboBox1.Items.Count == 0)
                {
                    comboBox1.Items.Add("Unknown");
                }
                comboBox1.SelectedIndex = 0;
            });
        }

        private Model model;
        private string image_path;
        private int batch_size = 1;
        private PressureTestRunner pressureTestRunner;
        private System.Windows.Forms.Timer updateTimer;

        private void button_loadmodel_Click(object sender, EventArgs e)
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
                int device_id = comboBox1.SelectedIndex;
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
            richTextBox1.Text = result["model_info"].ToString();
        }

        private void button_openimage_Click(object sender, EventArgs e)
        {
            if (model == null)
            {
                MessageBox.Show("请先加载模型文件！");
                return;
            }
            OpenFileDialog openFileDialog = new OpenFileDialog();

            openFileDialog.Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件 (*.*)|*.*";
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

            CSharpResult result = model.InferBatch(image_list);

            imagePanel1.UpdateImageAndResult(image, result);

            var a = result.SampleResults[0];
            string s = "";
            foreach (var b in a.Results)
            {
                s += b.CategoryName + ", " + b.Score.ToString();
                s += ", bbox: [";
                foreach (var x in b.Bbox)
                {
                    s += x + ", ";
                }
                s += "]\n";
            }
            richTextBox1.Text = s;
        }

        /// <summary>
        /// 执行模型推理的测试操作
        /// </summary>
        /// <param name="parameter">包含图像列表的参数</param>
        private void ModelInferAction(object parameter)
        {
            try
            {
                // 执行批量推理
                model.InferBatch((List<Mat>)parameter);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("模型推理出错: " + ex.Message);
            }
        }

        /// <summary>
        /// 定时更新UI上的统计信息
        /// </summary>
        private void UpdateStatisticsTimer_Tick(object sender, EventArgs e)
        {
            if (pressureTestRunner != null && pressureTestRunner.IsRunning)
            {
                // 在UI上显示压测统计信息
                Invoke((MethodInvoker)delegate
                {
                    richTextBox1.Text = pressureTestRunner.GetStatistics(false);
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
                StartPressureTest();
            }
        }

        /// <summary>
        /// 启动压力测试
        /// </summary>
        private void StartPressureTest()
        {
            try
            {
                // 准备测试数据
                batch_size = (int)numericUpDown_batch_size.Value;
                int threadCount = (int)numericUpDown_num_thread.Value;

                // 读取图像并创建批处理列表
                Mat image = Cv2.ImRead(image_path, ImreadModes.Color);
                Cv2.CvtColor(image, image, ColorConversionCodes.BGR2RGB);
                var image_list = new List<Mat>();
                for (int i = 0; i < batch_size; i++)
                {
                    image_list.Add(image);
                }

                // 创建压力测试实例
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
                button_thread_test.Text = "停止";
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动压力测试失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 停止压力测试
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

                // 显示最终统计信息
                button_thread_test.Text = "多线程测试";
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

        private void button_bbs_Click(object sender, EventArgs e)
        {
            Process.Start("https://bbs.dlcv.ai/t/topic/340");
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopPressureTest();
        }

    }
}
