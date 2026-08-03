using System;
using System.Collections.Generic;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DLCV.SequenceGraph;
using OpenCvSharp;
using OpenIVS2.Acceptance;
using OpenIVS2.Models;
using OpenIVS2.Services;

namespace OpenIVS2
{
    public partial class MainWindow : System.Windows.Window
    {
        private sealed class CameraCardControls
        {
            public Image Image;
            public Border StatusBorder;
            public TextBlock StatusText;
        }

        private readonly bool _acceptanceMode;
        private readonly SettingsService _settingsService = new SettingsService();
        private readonly ObservableCollection<string> _logs = new ObservableCollection<string>();
        private readonly Dictionary<string, CameraCardControls> _cameraCards = new Dictionary<string, CameraCardControls>(StringComparer.OrdinalIgnoreCase);
        private readonly ImageSaveService _imageSaveService = new ImageSaveService();
        private readonly object _logFileSync = new object();
        private AppSettings _settings;
        private SequenceHost _host;
        private OpenIvsCameraResourceFactory _cameraFactory;
        private IAiFlowRunner _flowRunner;
        private IModbusClient _modbus;
        private UiDisplaySink _displaySink;
        private CancellationTokenSource _plcCancellation;
        private Task _plcTask;
        private bool _running;
        private bool _closing;
        private bool _closeConfirmed;
        private int _totalCount;
        private int _okCount;
        private int _ngCount;
        private string _logFilePath;

        public MainWindow(bool acceptanceMode = false)
        {
            InitializeComponent();
            _acceptanceMode = acceptanceMode;
            _settings = _settingsService.Load();
            _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "OpenIVS2-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            LogList.ItemsSource = _logs;
            BuildCameraGrid();
        }

        internal int TotalCount { get { return _totalCount; } }
        internal int OkCount { get { return _okCount; } }
        internal int NgCount { get { return _ngCount; } }
        internal int CameraCardCount { get { return _cameraCards.Count; } }
        internal int CameraGridRows { get { return CameraGrid.Rows; } }
        internal int CameraGridColumns { get { return CameraGrid.Columns; } }
        internal bool IsRunning { get { return _running; } }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Log("info", "system", "OpenIVS2 已启动");
            if (!_acceptanceMode) return;
            try
            {
                var success = await AcceptanceRunner.RunAsync(this);
                Application.Current.Shutdown(success ? 0 : 1);
            }
            catch (Exception ex)
            {
                Log("error", "acceptance", ex.ToString());
                Application.Current.Shutdown(1);
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try { await StartSystemAsync(); }
            catch (Exception ex) { ShowError("启动失败", ex); }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await StopSystemAsync();
        }

        private async void TriggerButton_Click(object sender, RoutedEventArgs e)
        {
            await TriggerCycleAsync("manual");
        }

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_running) await StopSystemAsync();
            var window = new SettingsWindow(_settings) { Owner = this };
            if (window.ShowDialog() == true)
            {
                _settings = window.WorkingSettings;
                _settingsService.Save(_settings);
                BuildCameraGrid();
                Log("info", "settings", "设置已保存");
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetCounts();
        }

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_closeConfirmed) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            try
            {
                await StopSystemAsync();
            }
            catch (Exception ex)
            {
                Log("error", "system", "关闭软件时释放资源失败: " + ex.Message);
            }
            finally
            {
                _closeConfirmed = true;
                await Dispatcher.BeginInvoke(new Action(Close));
            }
        }

        internal void ApplySettingsForAcceptance(AppSettings settings)
        {
            _settings = settings.Clone();
            BuildCameraGrid();
        }

        internal Task StartForAcceptanceAsync(IAiFlowRunner flowRunner, IModbusClient modbus)
        {
            return StartSystemAsync(flowRunner, modbus);
        }

        internal Task StopForAcceptanceAsync()
        {
            return StopSystemAsync();
        }

        internal void ResetCountsForAcceptance()
        {
            ResetCounts();
        }

        internal void SetAcceptanceLogPath(string path)
        {
            _logFilePath = path;
        }

        internal List<Dictionary<string, object>> GetLifecycleEvents()
        {
            return _host != null && _host.Executor != null
                ? _host.Executor.GetEventsSnapshot()
                : new List<Dictionary<string, object>>();
        }

        internal void CaptureScreenshot(string path)
        {
            UpdateLayout();
            var width = Math.Max(1, (int)ActualWidth);
            var height = Math.Max(1, (int)ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            using (var stream = File.Create(path))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }
        }

        private async Task StartSystemAsync(IAiFlowRunner flowRunnerOverride = null, IModbusClient modbusOverride = null)
        {
            if (_running) return;
            ValidateSettings(flowRunnerOverride != null);
            SetBusy(true, "正在加载相机与模型...");
            try
            {
                var graph = SequenceGraphBuilder.Build(_settings);
                SaveRuntimeSequence(graph);
                var deviceIds = _settings.EnabledCameras()
                    .GroupBy(x => x.ModelPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.First().DeviceId, StringComparer.OrdinalIgnoreCase);
                _flowRunner = flowRunnerOverride ?? new DlcvFlowRunner(deviceIds);
                _modbus = modbusOverride ?? (_settings.UsePlc && string.Equals(_settings.PlcMode, "serial", StringComparison.OrdinalIgnoreCase)
                    ? (IModbusClient)new SerialModbusClient(_settings)
                    : new MockModbusClient());
                _cameraFactory = new OpenIvsCameraResourceFactory();
                _displaySink = new UiDisplaySink(Dispatcher, UpdateCameraFrame);
                _host = new SequenceHost();
                _host.LoadWithoutStart(graph, _flowRunner, _displaySink, _modbus, LoadImage, Log);
                _host.Executor.CameraFactory = _cameraFactory;
                _host.Executor.ModbusEnabled = true;
                _host.Executor.TriggerCompleted = OnTriggerCompletedAsync;
                _host.Executor.TriggerFailed = OnTriggerFailedAsync;
                _host.Executor.ProgressSink = OnProgress;
                _host.Executor.CameraStateChanged = OnCameraStateChanged;
                await Task.Run(() => _host.Start());
                _running = true;
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                TriggerButton.IsEnabled = true;
                SettingsButton.IsEnabled = false;
                CameraIndicator.Fill = Brushes.Green;
                ModelIndicator.Fill = Brushes.Green;
                StatusText.Text = "系统运行中";
                Log("info", "system", "相机与模型加载完成，系统进入运行状态");
                if (_settings.UsePlc) StartPlcPolling();
                else PlcIndicator.Fill = Brushes.Gray;
            }
            catch
            {
                await StopSystemAsync();
                throw;
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void SaveRuntimeSequence(SequenceGraphDocument graph)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime_sequence.json");
            File.WriteAllText(path, SequenceGraphLoader.ToJson(graph, true));
            Log("info", "sequence", "运行时序已保存: " + path);
        }

        private async Task StopSystemAsync()
        {
            if (_plcCancellation != null)
            {
                _plcCancellation.Cancel();
                try { if (_plcTask != null) await _plcTask; } catch (OperationCanceledException) { }
                _plcCancellation.Dispose();
                _plcCancellation = null;
                _plcTask = null;
            }
            if (_host != null)
            {
                try
                {
                    _host.RequestStop();
                    await _host.WaitForIdleAsync();
                    await Task.Run(() => _host.Stop());
                }
                catch (Exception ex) { Log("warn", "system", "停止时序时发生错误: " + ex.Message); }
                _host = null;
            }
            var disposableRunner = _flowRunner as IDisposable;
            if (disposableRunner != null)
            {
                try { disposableRunner.Dispose(); } catch { }
            }
            _flowRunner = null;
            if (_modbus != null)
            {
                try { _modbus.Close(); } catch { }
                _modbus = null;
            }
            _cameraFactory = null;
            _displaySink = null;
            _running = false;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            TriggerButton.IsEnabled = false;
            SettingsButton.IsEnabled = true;
            PlcIndicator.Fill = Brushes.Gray;
            CameraIndicator.Fill = Brushes.Gray;
            ModelIndicator.Fill = Brushes.Gray;
            StatusText.Text = "系统已停止";
        }

        private void StartPlcPolling()
        {
            _plcCancellation = new CancellationTokenSource();
            var token = _plcCancellation.Token;
            _plcTask = Task.Run(async () =>
            {
                var edge = new RisingEdgeTracker();
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!_modbus.Connect(null, 0, (byte)_settings.DeviceId))
                            throw new InvalidOperationException("PLC 连接失败");
                        SetIndicator(PlcIndicator, Brushes.Green);
                        var value = _modbus.ReadHoldingRegister(_settings.PhotoRegister);
                        if (edge.Sample(value, _settings.TriggerValue))
                            await TriggerCycleAsync("plc");
                    }
                    catch (Exception ex)
                    {
                        SetIndicator(PlcIndicator, Brushes.Red);
                        Log("error", "plc", ex.Message);
                    }
                    await Task.Delay(_settings.PollIntervalMs, token);
                }
            }, token);
        }

        private async Task TriggerCycleAsync(string source)
        {
            var host = _host;
            if (!_running || host == null) return;
            try
            {
                SetStatus("检测周期执行中");
                var triggerId = await host.TriggerAsync("trigger", new Dictionary<string, object>
                {
                    { "source", source },
                    { "photo_reg", _settings.PhotoRegister }
                });
                if (triggerId == null) Log("warn", "trigger", "已有周期运行，本次触发未重复统计");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("error", "trigger", ex.Message); }
        }

        private Task OnTriggerCompletedAsync()
        {
            var evaluation = ResultEvaluator.Evaluate(_settings, _host.Executor.LastResults);
            List<string> saved = null;
            Dispatcher.Invoke(() =>
            {
                CommitCycle(evaluation);
                saved = _imageSaveService.SaveCycle(_settings, evaluation.Ok, _displaySink.GetImagesSnapshot());
            });
            if (saved.Count > 0) Log("info", "save", "已保存 " + saved.Count + " 张周期图片");
            ReleaseCycleObjects(_host.Executor.LastResults);
            return Task.CompletedTask;
        }

        private Task OnTriggerFailedAsync(SequenceTriggerFailure failure)
        {
            try { _modbus.WriteSingleRegister(_settings.PhotoRegister, _settings.ClearValue); }
            catch (Exception ex) { Log("error", "plc_clear", "失败后再次清零 PLC 未成功: " + ex.Message); }
            Dispatcher.Invoke(() =>
            {
                var evaluation = new CycleEvaluation
                {
                    Ok = false,
                    Cameras = _settings.EnabledCameras().Select(x => new CameraCycleResult
                    {
                        Slot = x.Slot,
                        Ok = false,
                        Reason = string.Equals("run_" + x.Slot, failure.FailureNodeId, StringComparison.OrdinalIgnoreCase)
                            || string.Equals("wait_" + x.Slot, failure.FailureNodeId, StringComparison.OrdinalIgnoreCase)
                            ? failure.Exception.Message : "周期失败"
                    }).ToList()
                };
                CommitCycle(evaluation);
            });
            Log("error", failure.FailureNodeId ?? "sequence", failure.Exception.Message);
            ReleaseCycleObjects(failure.Results);
            return Task.CompletedTask;
        }

        private static void ReleaseCycleObjects(Dictionary<string, object> results)
        {
            if (results == null) return;
            foreach (var value in results.Values)
            {
                var mat = value as Mat;
                if (mat != null)
                {
                    mat.Dispose();
                    continue;
                }
                var bitmap = value as System.Drawing.Bitmap;
                if (bitmap != null)
                {
                    bitmap.Dispose();
                    continue;
                }
                if (value == null) continue;
                var samplesProperty = value.GetType().GetProperty("SampleResults");
                var samples = samplesProperty != null ? samplesProperty.GetValue(value, null) as IEnumerable : null;
                if (samples == null) continue;
                foreach (var sample in samples)
                {
                    if (sample == null) continue;
                    var objectsProperty = sample.GetType().GetProperty("Results");
                    var objects = objectsProperty != null ? objectsProperty.GetValue(sample, null) as IEnumerable : null;
                    if (objects == null) continue;
                    foreach (var item in objects)
                    {
                        if (item == null) continue;
                        var maskProperty = item.GetType().GetProperty("Mask");
                        var mask = maskProperty != null ? maskProperty.GetValue(item, null) as Mat : null;
                        if (mask != null) mask.Dispose();
                    }
                }
            }
        }

        private void CommitCycle(CycleEvaluation evaluation)
        {
            _totalCount++;
            if (evaluation.Ok) _okCount++; else _ngCount++;
            TotalCountText.Text = _totalCount.ToString();
            OkCountText.Text = _okCount.ToString();
            NgCountText.Text = _ngCount.ToString();
            YieldText.Text = (_totalCount == 0 ? 0 : _okCount * 100.0 / _totalCount).ToString("0.00") + "%";
            OverallResultText.Text = evaluation.Ok ? "OK" : "NG";
            OverallResultCard.Background = evaluation.Ok ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) : new SolidColorBrush(Color.FromRgb(244, 67, 54));
            OverallIndicator.Fill = evaluation.Ok ? Brushes.Green : Brushes.Red;
            CycleDetailText.Text = string.Join("  ", evaluation.Cameras.Select(x => x.Slot + ":" + (x.Ok ? "OK" : "NG")));
            foreach (var item in evaluation.Cameras) SetCameraStatus(item.Slot, item.Ok ? "OK" : "NG", item.Ok ? Brushes.Green : Brushes.Red);
            StatusText.Text = "周期完成：" + (evaluation.Ok ? "OK" : "NG");
            Log("info", "result", "周期总结果=" + (evaluation.Ok ? "OK" : "NG") + "，" + CycleDetailText.Text);
        }

        private void UpdateCameraFrame(string windowId, BitmapSource image, object result)
        {
            var slot = windowId != null && windowId.StartsWith("WIN_", StringComparison.OrdinalIgnoreCase) ? windowId.Substring(4) : windowId;
            CameraCardControls card;
            if (slot != null && _cameraCards.TryGetValue(slot, out card))
            {
                card.Image.Source = image;
                SetCameraStatus(slot, "已更新", Brushes.DodgerBlue);
            }
        }

        private void OnProgress(string type, string evt, string nodeId, string nodeType)
        {
            if (evt == "node_started" || evt == "node_failed" || type == "completed")
                Log(evt == "node_failed" ? "error" : "debug", nodeId, evt ?? type);
        }

        private void OnCameraStateChanged(ResourceItem resource, CameraResourceState state)
        {
            var slot = resource.Id.StartsWith("CAM_", StringComparison.OrdinalIgnoreCase) ? resource.Id.Substring(4) : resource.Id;
            var brush = state == CameraResourceState.Opened ? Brushes.Green : state == CameraResourceState.Failed ? Brushes.Red : Brushes.Orange;
            Dispatcher.BeginInvoke(new Action(() => SetCameraStatus(slot, state.ToString(), brush)));
        }

        private object LoadImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("虚拟相机图片不存在", path);
            var image = Cv2.ImRead(path, ImreadModes.Unchanged);
            if (image.Empty())
            {
                image.Dispose();
                throw new InvalidDataException("虚拟相机图片无法解码: " + path);
            }
            return image;
        }

        private void BuildCameraGrid()
        {
            CameraGrid.Children.Clear();
            _cameraCards.Clear();
            var cameras = _settings.EnabledCameras();
            var count = Math.Max(1, cameras.Count);
            CameraGrid.Rows = count <= 3 ? 1 : 2;
            CameraGrid.Columns = count == 1 ? 1 : count == 2 ? 2 : count == 3 ? 3 : count == 4 ? 2 : 3;
            foreach (var camera in cameras) CameraGrid.Children.Add(CreateCameraCard(camera));
        }

        private UIElement CreateCameraCard(CameraSettings camera)
        {
            var image = new Image { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var statusText = new TextBlock { Text = "待机", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold };
            var statusBorder = new Border { Background = Brushes.Gray, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3), Child = statusText };
            var header = new Grid { Margin = new Thickness(4, 2, 4, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = camera.Name + "  [" + camera.Slot + "]", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(38, 50, 56)), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(statusBorder, 1);
            header.Children.Add(statusBorder);
            var imageBorder = new Border { Background = new SolidColorBrush(Color.FromRgb(25, 31, 35)), CornerRadius = new CornerRadius(5), Child = image };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(header);
            Grid.SetRow(imageBorder, 1);
            grid.Children.Add(imageBorder);
            var border = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(7), Margin = new Thickness(5), Padding = new Thickness(8), BorderBrush = new SolidColorBrush(Color.FromRgb(224, 230, 233)), BorderThickness = new Thickness(1), Child = grid };
            _cameraCards[camera.Slot] = new CameraCardControls { Image = image, StatusBorder = statusBorder, StatusText = statusText };
            return border;
        }

        private void SetCameraStatus(string slot, string text, Brush brush)
        {
            CameraCardControls card;
            if (!_cameraCards.TryGetValue(slot, out card)) return;
            card.StatusText.Text = text;
            card.StatusBorder.Background = brush;
        }

        private void ValidateSettings(bool mockFlow)
        {
            var cameras = _settings.EnabledCameras();
            if (cameras.Count < 1 || cameras.Count > 6) throw new InvalidOperationException("启用相机数量必须为 1 到 6 台");
            foreach (var camera in cameras)
            {
                if (string.Equals(camera.Mode, "virtual", StringComparison.OrdinalIgnoreCase) && !File.Exists(camera.VirtualImagePath))
                    throw new FileNotFoundException(camera.Name + " 的虚拟图片不存在", camera.VirtualImagePath);
                if (!mockFlow && !File.Exists(camera.ModelPath))
                    throw new FileNotFoundException(camera.Name + " 的模型不存在", camera.ModelPath);
            }
            if (string.IsNullOrWhiteSpace(_settings.SaveDirectory)) throw new InvalidOperationException("图片保存目录不能为空");
        }

        private void ResetCounts()
        {
            _totalCount = _okCount = _ngCount = 0;
            TotalCountText.Text = OkCountText.Text = NgCountText.Text = "0";
            YieldText.Text = "0.00%";
            OverallResultText.Text = "待机";
            CycleDetailText.Text = "等待触发";
            OverallResultCard.Background = new SolidColorBrush(Color.FromRgb(144, 164, 174));
            OverallIndicator.Fill = Brushes.Gray;
            Log("info", "counter", "生产计数已清零");
        }

        private void SetBusy(bool busy, string status)
        {
            StartButton.IsEnabled = !busy && !_running;
            SettingsButton.IsEnabled = !busy && !_running;
            if (!string.IsNullOrWhiteSpace(status)) StatusText.Text = status;
        }

        private void SetStatus(string status)
        {
            Dispatcher.BeginInvoke(new Action(() => StatusText.Text = status));
        }

        private void SetIndicator(System.Windows.Shapes.Ellipse indicator, Brush brush)
        {
            Dispatcher.BeginInvoke(new Action(() => indicator.Fill = brush));
        }

        private void Log(string level, string source, string message)
        {
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level.ToUpperInvariant() + "] [" + source + "] " + message;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _logs.Add(line);
                while (_logs.Count > 500) _logs.RemoveAt(0);
                if (_logs.Count > 0) LogList.ScrollIntoView(_logs[_logs.Count - 1]);
            }));
            try
            {
                lock (_logFileSync)
                {
                    var directory = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
            }
            catch { }
        }

        private void ShowError(string title, Exception ex)
        {
            Log("error", "ui", title + ": " + ex.Message);
            MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
