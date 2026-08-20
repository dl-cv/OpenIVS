using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
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
using System.Threading.Tasks;

namespace DlcvDemo
{
    public partial class MainWindow : System.Windows.Window
    {
        private readonly UiTestOptions uiTestOptions;
        internal int UiTestExitCode { get; private set; }

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

        public MainWindow() : this(null)
        {
        }

        internal MainWindow(UiTestOptions options)
        {
            uiTestOptions = options;
            InitializeComponent();
            if (uiTestOptions != null)
            {
                ShowActivated = uiTestOptions.InteractiveDialogs;
            }
        }

        private void Form1_Load(object sender, RoutedEventArgs e)
        {
            Topmost = false;
            MaxWidth = SystemParameters.WorkArea.Width;
            MaxHeight = SystemParameters.WorkArea.Height;
            this.Title = "C# 测试程序 v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            Thread thread = new Thread(InitializeDeviceAndUiTest);
            thread.IsBackground = true;
            thread.Start();
        }

        private void InitializeDeviceAndUiTest()
        {
            try
            {
                GetDeviceInfo();
            }
            finally
            {
                if (uiTestOptions != null)
                {
                    Dispatcher.BeginInvoke(new Action(RunUiTest), DispatcherPriority.ApplicationIdle);
                }
            }
        }

        private void GetDeviceInfo()
        {
            Thread.Sleep(1);
            JObject device_info = Utils.GetGpuInfo();

            // 启动时先做一次加密狗检测：都没有则只展示结果、不加载推理 DLL
            JObject allDogInfo = QueryAllDogInfo();
            bool hasDog = HasAnyDog(allDogInfo);

            Dispatcher.Invoke(delegate
            {
                // 清空现有项目和映射表
                comboBox1.Items.Clear();
                deviceNameToIdMap.Clear();

                // 按设备ID从小到大排列：OpenVINO(-2)、CPU(-1)、GPU(0...)
                comboBox1.Items.Add("OpenVINO");
                deviceNameToIdMap["OpenVINO"] = -2;
                comboBox1.Items.Add("CPU");
                deviceNameToIdMap["CPU"] = -1;

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
                    comboBox1.SelectedIndex = 2; // 选择第一个GPU
                }
                else
                {
                    comboBox1.SelectedIndex = 1; // 选择CPU
                }

                // 仅在检测不到加密狗时，把检测结果显示到界面提醒用户
                if (!hasDog)
                {
                    richTextBox1.Text = FormatDogInfoText(allDogInfo, prependNoDogHint: true);
                }
            });

            if (!hasDog)
            {
                return;
            }

            // 检测到加密狗后再加载对应推理 DLL，并保持最高时钟
            try
            {
                var info = Utils.GetDeviceInfo();
                Console.WriteLine(info.ToString());
                DllLoader.Instance.dlcv_keep_max_clock?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetDeviceInfo/keep_max_clock failed: " + ex.Message);
            }
        }

        private static JObject QueryAllDogInfo()
        {
            try
            {
                return sntl_admin_csharp.DogUtils.GetAllDogInfo();
            }
            catch
            {
                return new JObject
                {
                    ["sentinel"] = new JObject { ["devices"] = new JArray(), ["features"] = new JArray() },
                    ["virbox"] = new JObject { ["devices"] = new JArray(), ["features"] = new JArray() }
                };
            }
        }

        private static bool HasAnyDog(JObject allInfo)
        {
            JObject sentinel = allInfo["sentinel"] as JObject ?? new JObject();
            JObject virbox = allInfo["virbox"] as JObject ?? new JObject();
            JArray sentinelDevices = sentinel["devices"] as JArray ?? new JArray();
            JArray sentinelFeatures = sentinel["features"] as JArray ?? new JArray();
            JArray virboxDevices = virbox["devices"] as JArray ?? new JArray();
            JArray virboxFeatures = virbox["features"] as JArray ?? new JArray();
            return sentinelDevices.Count > 0 || sentinelFeatures.Count > 0
                || virboxDevices.Count > 0 || virboxFeatures.Count > 0;
        }

        private static string FormatDogInfoText(JObject allInfo, bool prependNoDogHint)
        {
            JObject sentinel = allInfo["sentinel"] as JObject ?? new JObject { ["devices"] = new JArray(), ["features"] = new JArray() };
            JObject virbox = allInfo["virbox"] as JObject ?? new JObject { ["devices"] = new JArray(), ["features"] = new JArray() };
            JArray sentinelDevices = sentinel["devices"] as JArray ?? new JArray();
            JArray sentinelFeatures = sentinel["features"] as JArray ?? new JArray();
            JArray virboxDevices = virbox["devices"] as JArray ?? new JArray();
            JArray virboxFeatures = virbox["features"] as JArray ?? new JArray();

            string text =
                "Sentinel加密狗ID：\n" + sentinelDevices.ToString() + "\n\n" +
                "Sentinel加密狗特性：\n" + sentinelFeatures.ToString() + "\n\n" +
                "Virbox加密狗ID：\n" + virboxDevices.ToString() + "\n\n" +
                "Virbox加密狗特性：\n" + virboxFeatures.ToString();

            if (prependNoDogHint)
            {
                text = "未检测到加密狗\n\n" + text;
            }
            return text;
        }

        /// <summary>
        /// 「检查加密狗」按钮：查询并显示 Sentinel/Virbox 信息。
        /// </summary>
        private void ShowDogInfo()
        {
            JObject allInfo = QueryAllDogInfo();
            bool noDog = !HasAnyDog(allInfo);
            richTextBox1.Text = FormatDogInfoText(allInfo, prependNoDogHint: noDog);
        }

        private dynamic model;
        private string model_path;
        private string image_path;
        private int batch_size = 1;
        private PressureTestRunner pressureTestRunner;
        private DispatcherTimer updateTimer;
        private dynamic baselineJsonResult = null;
        private volatile bool shouldStopPressureTest = false;
        private bool isConsistencyTestMode = false; // 控制是否进行一致性测试
        private bool isCurrentFlowModel = false; // 当前是否为流程模型(dvst/dvso/dvsp)

        private void DisposeCurrentModel()
        {
            try
            {
                var disposable = model as IDisposable;
                disposable?.Dispose();
            }
            catch (Exception ex)
            {
                // 不要因为释放失败影响后续加载
                Console.WriteLine("Dispose model failed: " + ex.Message);
            }
            finally
            {
                model = null;
            }
        }

        private void button_loadmodel_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.RestoreDirectory = true;

            openFileDialog.Filter = "AI模型 (*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp)|*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp|所有文件 (*.*)|*.*";
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

            if (openFileDialog.ShowDialog(this) == true)
            {
                try
                {
                    LoadModelFromPath(openFileDialog.FileName, null, true);
                }
                catch (Exception ex)
                {
                    richTextBox1.Text = ex.Message;
                }
            }
        }

        private void LoadModelFromPath(string selectedFilePath, int? deviceOverride, bool saveLastPath)
        {
            if (saveLastPath)
            {
                Properties.Settings.Default.LastModelPath = selectedFilePath;
                Properties.Settings.Default.Save();
            }
            string ext = Path.GetExtension(selectedFilePath) ?? "";
            isCurrentFlowModel =
                ext.Equals(".dvst", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".dvso", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".dvsp", StringComparison.OrdinalIgnoreCase);
            if (model != null)
            {
                DisposeCurrentModel();
            }
            bool rpc_mode = false;
            try
            {
                rpc_mode = this.checkBox_rpc_mode != null && this.checkBox_rpc_mode.IsChecked == true;
            }
            catch { }

            model = new Model(selectedFilePath, deviceOverride ?? GetSelectedDeviceId(), rpc_mode);
            model_path = selectedFilePath;
            button_getmodelinfo_Click(this, EventArgs.Empty);
        }

		private void button_infer_json_Click(object sender, EventArgs e)
		{
			try
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

				Mat image = Cv2.ImRead(image_path, ImreadModes.Unchanged);
				if (image.Empty())
				{
					Console.WriteLine("图像解码失败！");
					return;
				}
				JObject data = new JObject();
				data["threshold"] = (float)numericUpDown_threshold.Value;
				data["with_mask"] = true;
				AddCalcMeanOverride(data);

				Mat inferImage = PrepareImageForModelInput(image);
				try
				{
					var json = model.InferOneOutJson(inferImage, data);
					richTextBox1.Text = JsonConvert.SerializeObject(json, Formatting.Indented);
				}
				finally
				{
					if (!object.ReferenceEquals(inferImage, image))
					{
						inferImage.Dispose();
					}
				}
			}
			catch (Exception ex)
			{
				ReportError("推理JSON失败", ex);
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
                richTextBox1.Text = result["model_info"].ToString();
            }
            else
            {
                // 未知格式，直接显示原始 JSON
                richTextBox1.Text = result.ToString();
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
            if (openFileDialog.ShowDialog(this) == true)
            {
                OpenImageFromPath(openFileDialog.FileName, true);
            }
        }

        private void OpenImageFromPath(string selectedImagePath, bool saveLastPath)
        {
            image_path = selectedImagePath;
            if (saveLastPath)
            {
                Properties.Settings.Default.LastImagePath = image_path;
                Properties.Settings.Default.Save();
            }
            button_infer_Click(this, EventArgs.Empty);
        }

        private async void RunUiTest()
        {
            try
            {
                numericUpDown_threshold.Value = uiTestOptions.Threshold;
                checkBox_calc_mean.IsChecked = uiTestOptions.CalcMean;
                WriteUiTestResult("started", null);

                if (uiTestOptions.InteractiveDialogs)
                {
                    button_loadmodel_Click(this, EventArgs.Empty);
                }
                else
                {
                    LoadModelFromPath(uiTestOptions.ModelPath, uiTestOptions.DeviceId, false);
                }
                if (model == null)
                {
                    throw new InvalidOperationException("模型加载失败: " + richTextBox1.Text);
                }
                if (uiTestOptions.InteractiveDialogs
                    && !PathsEqual(model_path, uiTestOptions.ModelPath))
                {
                    throw new InvalidOperationException("文件对话框选择的模型与预期不一致: " + model_path);
                }

                WriteUiTestResult("model_loaded", null);
                await Task.Delay(600);

                if (uiTestOptions.InteractiveDialogs)
                {
                    button_openimage_Click(this, EventArgs.Empty);
                }
                else
                {
                    OpenImageFromPath(uiTestOptions.ImagePath, false);
                }
                if (uiTestOptions.InteractiveDialogs
                    && !PathsEqual(image_path, uiTestOptions.ImagePath))
                {
                    throw new InvalidOperationException("文件对话框选择的图片与预期不一致: " + image_path);
                }
                if (string.IsNullOrWhiteSpace(richTextBox1.Text)
                    || !richTextBox1.Text.Contains("推理结果:"))
                {
                    throw new InvalidOperationException("界面未生成推理结果: " + richTextBox1.Text);
                }

                await Dispatcher.Yield(DispatcherPriority.Render);
                UiTestExitCode = 0;
                WriteUiTestResult("passed", null);
            }
            catch (Exception ex)
            {
                UiTestExitCode = 1;
                richTextBox1.Text = "自动 UI 测试失败\n" + ex;
                WriteUiTestResult("failed", ex);
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private void WriteUiTestResult(string status, Exception error)
        {
            string outputPath = Path.GetFullPath(uiTestOptions.OutputPath);
            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            var result = new JObject
            {
                ["status"] = status,
                ["ok"] = status == "passed",
                ["model"] = model_path ?? uiTestOptions.ModelPath,
                ["image"] = image_path ?? uiTestOptions.ImagePath,
                ["threshold"] = uiTestOptions.Threshold,
                ["calc_mean"] = uiTestOptions.CalcMean.HasValue
                    ? new JValue(uiTestOptions.CalcMean.Value)
                    : JValue.CreateNull(),
                ["device"] = uiTestOptions.DeviceId,
                ["interactive_dialogs"] = uiTestOptions.InteractiveDialogs,
                ["window_title"] = Title,
                ["result_text"] = richTextBox1.Text ?? string.Empty,
                ["error"] = error == null ? null : error.ToString()
            };
            string temporaryPath = outputPath + ".tmp";
            File.WriteAllText(temporaryPath, result.ToString(Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            File.Move(temporaryPath, outputPath);
        }

        private void numericUpDown_threshold_ValueChanged(object sender, EventArgs e)
        {
            if (model == null || string.IsNullOrEmpty(image_path) || !File.Exists(image_path))
            {
                return;
            }
            if (pressureTestRunner != null && pressureTestRunner.IsRunning)
            {
                return;
            }

            button_infer_Click(sender, e);
        }

        private void AddCalcMeanOverride(JObject data)
        {
            bool? calcMean = checkBox_calc_mean.IsChecked;
            if (calcMean.HasValue)
            {
                data["calc_mean"] = calcMean.Value;
            }
        }

        private void checkBox_calc_mean_StateChanged(object sender, RoutedEventArgs e)
        {
            if (checkBox_calc_mean == null)
            {
                return;
            }

            string stateText = !checkBox_calc_mean.IsChecked.HasValue
                ? "默认"
                : (checkBox_calc_mean.IsChecked.Value ? "是" : "否");
            checkBox_calc_mean.Content = $"计算均值：{stateText}";
        }

        private void resultTextWordWrapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as System.Windows.Controls.MenuItem;
            if (menuItem == null)
            {
                return;
            }

            bool wordWrapEnabled = menuItem.IsChecked;
            richTextBox1.TextWrapping = wordWrapEnabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
            richTextBox1.HorizontalScrollBarVisibility = wordWrapEnabled
                ? System.Windows.Controls.ScrollBarVisibility.Disabled
                : System.Windows.Controls.ScrollBarVisibility.Auto;
        }

        private void button_infer_Click(object sender, EventArgs e)
        {
            try
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

                Mat image = Cv2.ImRead(image_path, ImreadModes.Unchanged);
                if (image.Empty())
                {
                    throw new Exception("图像解码失败！");
                }
                batch_size = (int)numericUpDown_batch_size.Value;
                JObject data = new JObject();
                data["threshold"] = (float)numericUpDown_threshold.Value;
                data["with_mask"] = true;
                AddCalcMeanOverride(data);

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

				Mat inferImage = PrepareImageForModelInput(image);
				CSharpResult result;
				try
				{
					var inferImageList = new List<Mat>();
					for (int i = 0; i < batch_size; i++)
					{
						inferImageList.Add(inferImage);
					}
					result = model.InferBatch(inferImageList, data);
				}
				finally
				{
					if (!object.ReferenceEquals(inferImage, image))
					{
						inferImage.Dispose();
					}
				}

                stopwatch.Stop();
                double delay_ms = stopwatch.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
                Console.WriteLine($"推理时间: {delay_ms:F2}ms");

                imagePanel1.UpdateImageAndResult(image, result);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("图片: " + image_path);
                sb.AppendLine($"batch_size: {batch_size}");
                sb.AppendLine($"threshold: {(float)numericUpDown_threshold.Value:F2}");
                sb.AppendLine($"推理时间: {delay_ms:F2}ms");

                List<CSharpObjectResult> objects = null;
                bool? inspectionOk = null;
                string reason = null;
                if (result.SampleResults != null && result.SampleResults.Count > 0)
                {
                    objects = result.SampleResults[0].Results;
                    inspectionOk = result.SampleResults[0].Ok;
                    reason = result.SampleResults[0].Reason;
                }
                if (objects == null)
                {
                    objects = new List<CSharpObjectResult>();
                }

                sb.AppendLine($"推理结果: {objects.Count}个");
                if (inspectionOk.HasValue)
                {
                    sb.AppendLine(inspectionOk.Value ? "流程输出结果：OK" : "流程输出结果：NG");
                }
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    sb.AppendLine("流程输出原因：" + reason);
                }
                if (objects.Count == 0)
                {
                    sb.AppendLine("未检测到目标。");
                }
                else
                {
                    sb.AppendLine();
                    for (int i = 0; i < objects.Count; i++)
                    {
                        sb.AppendLine(BuildObjectResultText(i + 1, objects[i]));
                    }
                }
                richTextBox1.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                ReportError("推理失败", ex);
            }
        }

        private static string BuildObjectResultText(int index, CSharpObjectResult obj)
        {
            StringBuilder line = new StringBuilder();
            line.AppendFormat("[{0}] {1,-12}", index, obj.CategoryName ?? string.Empty);
            line.AppendFormat("  score={0:F2}", obj.Score);
            line.Append("  ");
            line.Append(BuildResultLocationText(obj));
            line.AppendFormat("  area={0:F1}", obj.Area);
            if (obj.WithMean)
            {
                line.AppendFormat("  foreground_mean={0:F4}", obj.ForegroundMean);
                line.AppendFormat("  background_mean={0:F4}", obj.BackgroundMean);
            }
            string angleText = BuildResultAngleText(obj);
            if (!string.IsNullOrWhiteSpace(angleText))
            {
                line.Append("  ");
                line.Append(angleText);
            }

            string extraInfoText = Utils.FormatExtraInfoForDisplay(obj.ExtraInfo);
            if (!string.IsNullOrWhiteSpace(extraInfoText))
            {
                line.AppendFormat("  extra_info={{ {0} }}", extraInfoText);
            }

            return line.ToString();
        }

        private static string BuildResultLocationText(CSharpObjectResult obj)
        {
            if (!obj.WithBbox || obj.Bbox == null || obj.Bbox.Count < 4)
            {
                return "bbox=(N/A)";
            }

            bool isRotated = obj.WithAngle || obj.Bbox.Count >= 5;
            if (isRotated)
            {
                float angle;
                TryGetResultAngle(obj, out angle);
                return string.Format(
                    "rbox=(cx={0:F1}, cy={1:F1}, w={2:F1}, h={3:F1}, angle={4:F3})",
                    obj.Bbox[0], obj.Bbox[1], obj.Bbox[2], obj.Bbox[3], angle);
            }

            return string.Format(
                "bbox=({0:F1}, {1:F1}, {2:F1}, {3:F1})",
                obj.Bbox[0], obj.Bbox[1], obj.Bbox[2], obj.Bbox[3]);
        }

        private static string BuildResultAngleText(CSharpObjectResult obj)
        {
            float angle;
            if (!TryGetResultAngle(obj, out angle))
            {
                return string.Empty;
            }

            double degrees = angle * 180.0 / Math.PI;
            return string.Format("angle={0:F3}rad({1:F1}deg)", angle, degrees);
        }

        private static bool TryGetResultAngle(CSharpObjectResult obj, out float angle)
        {
            if (obj.WithAngle)
            {
                angle = obj.Angle;
                return true;
            }

            if (obj.Bbox != null && obj.Bbox.Count >= 5)
            {
                angle = (float)obj.Bbox[4];
                return true;
            }

            angle = 0.0f;
            return false;
        }

        private static Mat PrepareImageForModelInput(Mat image)
        {
            if (image == null || image.Empty())
            {
                return image;
            }

            int channels = image.Channels();
            if (channels == 3)
            {
                var rgb = new Mat();
                Cv2.CvtColor(image, rgb, ColorConversionCodes.BGR2RGB);
                return rgb;
            }

            if (channels == 4)
            {
                var rgb = new Mat();
                Cv2.CvtColor(image, rgb, ColorConversionCodes.BGRA2RGB);
                return rgb;
            }

            // 灰度图按原样送入推理。
            return image;
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

                var inferSw = Stopwatch.StartNew();
                var resultTuple = model.InferInternal(image_list, infer_config);
                inferSw.Stop();
                IntPtr currentResultPtr = resultTuple.Item2;
                double dlcvInferMs = 0.0;
                double totalInferMs = 0.0;
                DlcvModules.InferTiming.GetLast(out dlcvInferMs, out totalInferMs);
                if (totalInferMs <= 0.0)
                {
                    totalInferMs = inferSw.Elapsed.TotalMilliseconds;
                }
                if (dlcvInferMs <= 0.0)
                {
                    dlcvInferMs = totalInferMs;
                }
                var flowNodeTimings = new List<PressureNodeTiming>();
                var rawNodeTimings = DlcvModules.InferTiming.GetLastFlowNodeTimings();
                for (int i = 0; i < rawNodeTimings.Count; i++)
                {
                    var timing = rawNodeTimings[i];
                    if (timing == null) continue;
                    flowNodeTimings.Add(new PressureNodeTiming(
                        timing.NodeId,
                        timing.NodeType,
                        timing.NodeTitle,
                        timing.ElapsedMs));
                }
                pressureTestRunner?.RecordLatencyBreakdown(
                    dlcvInferMs,
                    totalInferMs,
                    flowNodeTimings);

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
                                Dispatcher.Invoke(delegate
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
                                    MessageBox.Show(this, "检测到推理结果不一致，测试已停止！", "结果不一致", MessageBoxButton.OK, MessageBoxImage.Warning);

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
                Dispatcher.Invoke(delegate
                {
                    StopPressureTest();
                    string testType = isConsistencyTestMode ? "一致性测试" : "压力测试";
                    MessageBox.Show(this, $"{testType}过程中发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Dispatcher.Invoke(delegate
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

                Mat image = Cv2.ImRead(image_path, ImreadModes.Unchanged);
                Mat inferImage = PrepareImageForModelInput(image);

                var image_list = new List<Mat>();
                for (int i = 0; i < batch_size; i++)
                {
                    image_list.Add(inferImage);
                }

                // 创建测试实例
                pressureTestRunner = new PressureTestRunner(threadCount, 1000000, batch_size);
                pressureTestRunner.SetFlowModelTiming(isCurrentFlowModel);
                pressureTestRunner.SetTestAction(ModelInferAction, image_list);

                // 创建并启动定时器更新UI
                if (updateTimer == null)
                {
                    updateTimer = new DispatcherTimer();
                    updateTimer.Interval = TimeSpan.FromMilliseconds(500);
                    updateTimer.Tick += UpdateStatisticsTimer_Tick;
                }
                updateTimer.Start();

                // 启动测试
                pressureTestRunner.Start();

                // 根据模式设置按钮文本
                if (consistencyTestMode)
                {
                    button_consistency_test.Content = "停止";
                }
                else
                {
                    button_thread_test.Content = "停止";
                }
            }
            catch (Exception ex)
            {
                string testType = consistencyTestMode ? "一致性测试" : "压力测试";
                MessageBox.Show(this, $"启动{testType}失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    button_consistency_test.Content = "一致性测试";
                }
                else
                {
                    button_thread_test.Content = "多线程测试";
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
            DisposeCurrentModel();
            richTextBox1.Text = "模型已释放";
        }

        private void button_github_Click(object sender, EventArgs e)
        {
            Process.Start("https://docs.dlcv.com.cn/deploy/sdk/csharp_sdk");
        }

        private void Form1_FormClosing(object sender, CancelEventArgs e)
        {
            StopPressureTest();
            // 释放模型
            var disposable = model as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
                model = null;
            }
            Utils.FreeAllModels();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ShowDogInfo();
        }

        private void button_free_all_model_Click(object sender, EventArgs e)
        {
            // 如果存在正在运行的压力测试，先停止它
            StopPressureTest();

            // 释放模型
            var disposable = model as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
            }
            model = null;
            Utils.FreeAllModels();
            richTextBox1.Text = "所有模型已释放";
        }

		/// <summary>
		/// 统一错误输出：写入 richTextBox1 并弹窗提示
		/// </summary>
		private void ReportError(string title, Exception ex)
		{
			try
			{
				richTextBox1.Text = title + "\n" + ex.ToString();
			}
			catch { }
			if (uiTestOptions == null)
			{
				MessageBox.Show(this, title + ": " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

        private void button_save_img_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(image_path))
                {
                    MessageBox.Show("请先选择图片文件！");
                    return;
                }

                if (imagePanel1.image == null)
                {
                    MessageBox.Show("当前没有可保存的图像！");
                    return;
                }

                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Filter = "JPEG 图像 (*.jpg)|*.jpg";
                saveFileDialog.Title = "保存可视化图像";
                saveFileDialog.DefaultExt = "jpg";
                saveFileDialog.AddExtension = true;
                saveFileDialog.OverwritePrompt = true;
                saveFileDialog.RestoreDirectory = true;

                try
                {
                    saveFileDialog.InitialDirectory = Path.GetDirectoryName(image_path);
                    saveFileDialog.FileName = Path.GetFileNameWithoutExtension(image_path) + "_vis.jpg";
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                if (saveFileDialog.ShowDialog(this) != true)
                {
                    return;
                }

                using (var bitmap = imagePanel1.CreateVisualizationBitmap())
                {
                    bitmap.Save(saveFileDialog.FileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                }

                richTextBox1.Text = "图像已保存：\n" + saveFileDialog.FileName;
            }
            catch (Exception ex)
            {
                ReportError("保存图像失败", ex);
            }
        }
    }
}
