using System;
using System.ComponentModel;
using System.Drawing;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;

namespace OpenIVSWPF
{
    /// <summary>
    /// MainWindow的ViewModel类，实现了INotifyPropertyChanged接口以支持数据绑定
    /// </summary>
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion

        #region 设备状态
        private string _deviceStatus = "未连接";
        public string DeviceStatus
        {
            get => _deviceStatus;
            set => SetProperty(ref _deviceStatus, value);
        }

        private SolidColorBrush _deviceStatusBackground = Brushes.Gray;
        public SolidColorBrush DeviceStatusBackground
        {
            get => _deviceStatusBackground;
            set => SetProperty(ref _deviceStatusBackground, value);
        }
        #endregion

        #region PLC状态
        private string _plcStatus = "未启用";
        public string PLCStatus
        {
            get => _plcStatus;
            set => SetProperty(ref _plcStatus, value);
        }

        private SolidColorBrush _plcStatusBackground = Brushes.Gray;
        public SolidColorBrush PLCStatusBackground
        {
            get => _plcStatusBackground;
            set => SetProperty(ref _plcStatusBackground, value);
        }
        #endregion

        #region 相机状态
        private string _cameraStatus = "未连接";
        public string CameraStatus
        {
            get => _cameraStatus;
            set => SetProperty(ref _cameraStatus, value);
        }

        private SolidColorBrush _cameraStatusBackground = Brushes.Gray;
        public SolidColorBrush CameraStatusBackground
        {
            get => _cameraStatusBackground;
            set => SetProperty(ref _cameraStatusBackground, value);
        }
        #endregion

        #region 模型状态
        private string _modelStatus = "未加载";
        public string ModelStatus
        {
            get => _modelStatus;
            set => SetProperty(ref _modelStatus, value);
        }

        private SolidColorBrush _modelStatusBackground = Brushes.Gray;
        public SolidColorBrush ModelStatusBackground
        {
            get => _modelStatusBackground;
            set => SetProperty(ref _modelStatusBackground, value);
        }
        #endregion

        #region 位置信息
        private string _currentPosition = "当前位置：0.0";
        public string CurrentPosition
        {
            get => _currentPosition;
            set => SetProperty(ref _currentPosition, value);
        }
        #endregion

        #region 状态信息
        private string _statusMessage = "就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        #endregion

        #region 检测结果
        private string _detectionResult = "";
        public string DetectionResult
        {
            get => _detectionResult;
            set => SetProperty(ref _detectionResult, value);
        }

        private string _currentResult = "等待结果";
        public string CurrentResult
        {
            get => _currentResult;
            set => SetProperty(ref _currentResult, value);
        }

        private SolidColorBrush _currentResultBackground = Brushes.Gray;
        public SolidColorBrush CurrentResultBackground
        {
            get => _currentResultBackground;
            set => SetProperty(ref _currentResultBackground, value);
        }
        #endregion

        #region 统计数据
        private string _totalCount = "总数: 0";
        public string TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        private string _okCount = "OK: 0";
        public string OkCount
        {
            get => _okCount;
            set => SetProperty(ref _okCount, value);
        }

        private string _ngCount = "NG: 0";
        public string NgCount
        {
            get => _ngCount;
            set => SetProperty(ref _ngCount, value);
        }

        private string _yieldRate = "良率: 0.0%";
        public string YieldRate
        {
            get => _yieldRate;
            set => SetProperty(ref _yieldRate, value);
        }
        #endregion

        #region 按钮状态
        private bool _startButtonEnabled = true;
        public bool StartButtonEnabled
        {
            get => _startButtonEnabled;
            set => SetProperty(ref _startButtonEnabled, value);
        }

        private bool _stopButtonEnabled = false;
        public bool StopButtonEnabled
        {
            get => _stopButtonEnabled;
            set => SetProperty(ref _stopButtonEnabled, value);
        }

        private SolidColorBrush _startButtonBackground = Brushes.LightGreen;
        public SolidColorBrush StartButtonBackground
        {
            get => _startButtonBackground;
            set => SetProperty(ref _startButtonBackground, value);
        }

        private SolidColorBrush _stopButtonBackground = Brushes.LightGray;
        public SolidColorBrush StopButtonBackground
        {
            get => _stopButtonBackground;
            set => SetProperty(ref _stopButtonBackground, value);
        }

        private bool _settingsButtonEnabled = true;
        public bool SettingsButtonEnabled
        {
            get => _settingsButtonEnabled;
            set => SetProperty(ref _settingsButtonEnabled, value);
        }

        private bool _resetButtonEnabled = true;
        public bool ResetButtonEnabled
        {
            get => _resetButtonEnabled;
            set => SetProperty(ref _resetButtonEnabled, value);
        }
        #endregion

        /// <summary>
        /// 更新设备状态
        /// </summary>
        public void UpdateDeviceStatus(string status)
        {
            DeviceStatus = $"设备状态：{status}";
            DeviceStatusBackground = status == "已连接" ? Brushes.ForestGreen : Brushes.Gray;
        }

        /// <summary>
        /// 更新PLC状态
        /// </summary>
        public void UpdatePLCStatus(bool isEnabled)
        {
            PLCStatus = isEnabled ? "已启用" : "未启用";
            PLCStatusBackground = isEnabled ? Brushes.ForestGreen : Brushes.Orange;
        }

        /// <summary>
        /// 更新相机状态
        /// </summary>
        public void UpdateCameraStatus(string status)
        {
            CameraStatus = $"相机状态：{status}";
            CameraStatusBackground = status == "已连接" ? Brushes.ForestGreen : Brushes.Gray;
        }

        /// <summary>
        /// 更新模型状态
        /// </summary>
        public void UpdateModelStatus(string status)
        {
            ModelStatus = $"模型状态：{status}";
            ModelStatusBackground = status == "已加载" ? Brushes.ForestGreen : Brushes.Gray;
        }

        /// <summary>
        /// 更新当前位置显示
        /// </summary>
        public void UpdatePosition(float position)
        {
            CurrentPosition = $"当前位置：{position:F1}";
        }

        /// <summary>
        /// 更新状态信息
        /// </summary>
        public void UpdateStatus(string message)
        {
            StatusMessage = $"{DateTime.Now.ToString("HH:mm:ss")} - {message}";
        }

        /// <summary>
        /// 更新检测结果
        /// </summary>
        public void UpdateDetectionResult(string result)
        {
            DetectionResult = $"检测结果：\n{result}";
        }

        /// <summary>
        /// 更新统计信息
        /// </summary>
        public void UpdateStatistics(int totalCount, int okCount, int ngCount, double yieldRate)
        {
            TotalCount = $"总数: {totalCount}";
            OkCount = $"OK: {okCount}";
            NgCount = $"NG: {ngCount}";
            YieldRate = $"良率: {yieldRate:F1}%";
        }

        /// <summary>
        /// 更新检测结果状态（OK/NG）
        /// </summary>
        public void UpdateCurrentResult(bool isOK)
        {
            CurrentResult = isOK ? "OK" : "NG";
            CurrentResultBackground = isOK ? Brushes.ForestGreen : Brushes.Crimson;
        }

        /// <summary>
        /// 更新运行状态控件
        /// </summary>
        public void UpdateRunningState(bool isRunning)
        {
            StartButtonEnabled = !isRunning;
            StopButtonEnabled = isRunning;
            SettingsButtonEnabled = !isRunning;
            
            StartButtonBackground = !isRunning ? 
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)) : // 绿色 #4CAF50
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204)); // 灰色 #CCCCCC
                
            StopButtonBackground = isRunning ? 
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)) : // 红色 #F44336
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204)); // 灰色 #CCCCCC
        }
    }
} 