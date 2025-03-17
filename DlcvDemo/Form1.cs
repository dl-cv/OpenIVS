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

        string image_path;
        int batch_size = 1;
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
                image_list.Add(image);
            }

            if (image.Empty())
            {
                Console.WriteLine("图像解码失败！");
                return;
            }
            lastTotalRuns = totalRuns;
            stopwatch.Restart();
            CSharpResult result = model.InferBatch(image_list);
            lastDelay = (double)(stopwatch.ElapsedTicks / (decimal)Stopwatch.Frequency * 1000);

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

            //richTextBox1.Text = result.ToString();

            Interlocked.Add(ref totalRuns, batch_size);
            UpdateSpeed();
        }

        //private Thread[] threads = new Thread[5];
        private List<Thread> threads = new List<Thread>();
        private bool isRunning = false;
        private long totalRuns = 0;
        private long lastTotalRuns = 0;
        private double lastDelay = 0;
        private Stopwatch stopwatch = new Stopwatch();

        private void UpdateSpeed()
        {
            stopwatch.Stop();
            long delta = totalRuns - lastTotalRuns;
            double elapsed_seconds = (double)(stopwatch.ElapsedTicks / (decimal)Stopwatch.Frequency);
            double speed = delta / elapsed_seconds;
            lastTotalRuns = totalRuns;

            Invoke((MethodInvoker)delegate
            {
                label_speed.Text = $"总运行次数: {totalRuns} 每秒速度: {speed:F2} 延迟：{lastDelay:F2} ms";
            });

            stopwatch.Restart();
        }

        private void RunWatchSpeed()
        {
            while (isRunning)
            {
                UpdateSpeed();
                Thread.Sleep(500);
            }
        }

        private void RunThread()
        {
            Mat image = Cv2.ImRead(image_path, ImreadModes.Color);
            Cv2.CvtColor(image, image, ColorConversionCodes.BGR2RGB);
            var image_list = new List<Mat>();
            for (int i = 0; i < batch_size; i++)
            {
                image_list.Add(image);
            }

            while (isRunning)
            {
                Stopwatch stopwatch_once = new Stopwatch();
                stopwatch_once.Start();

                Utils.CSharpResult result = model.InferBatch(image_list);

                stopwatch_once.Stop();
                lastDelay = (double)(stopwatch_once.ElapsedTicks / (decimal)Stopwatch.Frequency * 1000);

                Interlocked.Add(ref totalRuns, batch_size);
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
            if (!isRunning)
            {
                lastTotalRuns = totalRuns;
                batch_size = (int)numericUpDown_batch_size.Value;
                StartThreads();
            }
            else
            {
                StopThreads();
            }
        }

        private void StartThreads()
        {
            isRunning = true;
            stopwatch.Start();
            threads.Clear();

            for (int i = 0; i < numericUpDown_num_thread.Value; i++)
            {
                Thread thread = new Thread(() => RunThread());
                thread.IsBackground = true;
                threads.Add(thread);
                thread.Start();
            }

            Thread speed_thread = new Thread(RunWatchSpeed);
            speed_thread.IsBackground = true;
            threads.Add(speed_thread);
            speed_thread.Start();

            button_thread_test.Text = "停止";
        }

        private void StopThreads()
        {
            isRunning = false;
            foreach (var thread in threads)
            {
                if (thread != null && thread.IsAlive)
                {
                    thread.Abort(); // Abort the thread
                }
            }

            button_thread_test.Text = "多线程测试";
        }

        private void button_freemodel_Click(object sender, EventArgs e)
        {
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
            if (isRunning)
            {
                StopThreads();
            }
        }
    }
}
