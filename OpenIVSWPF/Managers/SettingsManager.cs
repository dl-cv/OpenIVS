using System;
using System.IO;
using System.IO.Ports;
using System.Xml;

namespace OpenIVSWPF.Managers
{
    /// <summary>
    /// 设置管理器单例类，管理所有应用程序设置
    /// </summary>
    public class SettingsManager
    {
        #region 单例模式实现
        private static SettingsManager _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取SettingsManager的单例实例
        /// </summary>
        public static SettingsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new SettingsManager();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion

        // 设置存储对象
        public Settings Settings { get; private set; }

        // 设置文件路径
        private readonly string _settingsFilePath;

        // 设置变更事件
        public event EventHandler SettingsChanged;

        /// <summary>
        /// 私有构造函数，防止外部直接创建实例
        /// </summary>
        private SettingsManager()
        {
            // 设置文件路径
            _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");
            
            // 初始化设置
            Settings = new Settings();
            
            // 尝试从文件加载设置
            LoadSettings();
        }

        /// <summary>
        /// 从文件加载设置
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    LoadSettingsFromFile(_settingsFilePath);
                }
                else
                {
                    // 设置默认模型路径
                    string modelDefaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"test.dvt");
                    if (File.Exists(modelDefaultPath))
                    {
                        Settings.ModelPath = modelDefaultPath;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"加载设置时发生错误：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                Settings = new Settings();
            }
        }

        /// <summary>
        /// 从指定文件加载设置
        /// </summary>
        /// <param name="settingsFilePath">设置文件路径</param>
        private void LoadSettingsFromFile(string settingsFilePath)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(settingsFilePath);
                XmlElement root = doc.DocumentElement;

                // 加载Modbus设置
                Settings.PortName = GetSettingValue(root, "PortName", Settings.PortName);
                Settings.BaudRate = int.Parse(GetSettingValue(root, "BaudRate", Settings.BaudRate.ToString()));
                Settings.DataBits = int.Parse(GetSettingValue(root, "DataBits", Settings.DataBits.ToString()));
                Settings.StopBits = (StopBits)Enum.Parse(typeof(StopBits), GetSettingValue(root, "StopBits", Settings.StopBits.ToString()));
                Settings.Parity = (Parity)Enum.Parse(typeof(Parity), GetSettingValue(root, "Parity", Settings.Parity.ToString()));
                Settings.DeviceId = int.Parse(GetSettingValue(root, "DeviceId", Settings.DeviceId.ToString()));

                // 加载相机设置
                Settings.CameraIndex = int.Parse(GetSettingValue(root, "CameraIndex", Settings.CameraIndex.ToString()));
                Settings.CameraUserDefinedName = GetSettingValue(root, "CameraUserDefinedName", Settings.CameraUserDefinedName);
                Settings.UseTrigger = bool.Parse(GetSettingValue(root, "UseTrigger", Settings.UseTrigger.ToString()));
                Settings.UseSoftTrigger = bool.Parse(GetSettingValue(root, "UseSoftTrigger", Settings.UseSoftTrigger.ToString()));

                // 加载模型设置
                Settings.ModelPath = GetSettingValue(root, "ModelPath", Settings.ModelPath);

                // 加载设备设置
                Settings.Speed = float.Parse(GetSettingValue(root, "Speed", Settings.Speed.ToString()));

                // 加载目标位置设置
                string targetPositionStr = GetSettingValue(root, "TargetPosition", Settings.TargetPosition.ToString());
                if (!string.IsNullOrEmpty(targetPositionStr) && float.TryParse(targetPositionStr, out float targetPos))
                {
                    Settings.TargetPosition = targetPos;
                }

                // 加载图像保存设置
                Settings.SavePath = GetSettingValue(root, "SavePath", Settings.SavePath);
                Settings.SaveOKImage = bool.Parse(GetSettingValue(root, "SaveOKImage", Settings.SaveOKImage.ToString()));
                Settings.SaveNGImage = bool.Parse(GetSettingValue(root, "SaveNGImage", Settings.SaveNGImage.ToString()));
                Settings.ImageFormat = GetSettingValue(root, "ImageFormat", Settings.ImageFormat);
                Settings.JpegQuality = GetSettingValue(root, "JpegQuality", Settings.JpegQuality);
                
                // 加载拍照延迟设置
                string preCaptureDelayStr = GetSettingValue(root, "PreCaptureDelay", Settings.PreCaptureDelay.ToString());
                if (!string.IsNullOrEmpty(preCaptureDelayStr) && int.TryParse(preCaptureDelayStr, out int delay))
                {
                    Settings.PreCaptureDelay = delay;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"从文件加载设置时发生错误：{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取设置项值
        /// </summary>
        private string GetSettingValue(XmlElement root, string key, string defaultValue)
        {
            XmlNode node = root.SelectSingleNode(key);
            return node != null ? node.InnerText : defaultValue;
        }

        /// <summary>
        /// 保存设置到文件
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                XmlElement root;
                
                // 如果文件存在，则加载现有文件
                if (File.Exists(_settingsFilePath))
                {
                    doc.Load(_settingsFilePath);
                    root = doc.DocumentElement;
                }
                else
                {
                    // 创建新的XML文档
                    root = doc.CreateElement("Settings");
                    doc.AppendChild(root);
                }

                // 保存Modbus设置
                SetSettingValue(doc, root, "PortName", Settings.PortName);
                SetSettingValue(doc, root, "BaudRate", Settings.BaudRate.ToString());
                SetSettingValue(doc, root, "DataBits", Settings.DataBits.ToString());
                SetSettingValue(doc, root, "StopBits", Settings.StopBits.ToString());
                SetSettingValue(doc, root, "Parity", Settings.Parity.ToString());
                SetSettingValue(doc, root, "DeviceId", Settings.DeviceId.ToString());
                
                // 保存相机设置
                SetSettingValue(doc, root, "CameraIndex", Settings.CameraIndex.ToString());
                SetSettingValue(doc, root, "CameraUserDefinedName", Settings.CameraUserDefinedName);
                SetSettingValue(doc, root, "UseTrigger", Settings.UseTrigger.ToString());
                SetSettingValue(doc, root, "UseSoftTrigger", Settings.UseSoftTrigger.ToString());
                SetSettingValue(doc, root, "SavePath", Settings.SavePath);
                SetSettingValue(doc, root, "SaveOKImage", Settings.SaveOKImage.ToString());
                SetSettingValue(doc, root, "SaveNGImage", Settings.SaveNGImage.ToString());
                SetSettingValue(doc, root, "ImageFormat", Settings.ImageFormat);
                SetSettingValue(doc, root, "JpegQuality", Settings.JpegQuality);
                
                // 保存模型设置
                SetSettingValue(doc, root, "ModelPath", Settings.ModelPath);
                
                // 保存设备设置
                SetSettingValue(doc, root, "Speed", Settings.Speed.ToString());
                
                // 保存目标位置
                SetSettingValue(doc, root, "TargetPosition", Settings.TargetPosition.ToString());
                
                // 保存拍照设置
                SetSettingValue(doc, root, "PreCaptureDelay", Settings.PreCaptureDelay.ToString());
                
                // 保存文件
                doc.Save(_settingsFilePath);
                
                // 触发设置变更事件
                OnSettingsChanged();
            }
            catch (Exception ex)
            {
                throw new Exception($"保存设置文件时发生错误：{ex.Message}", ex);
            }
        }
        
        private void SetSettingValue(XmlDocument doc, XmlElement root, string key, string value)
        {
            XmlNode node = root.SelectSingleNode(key);
            if (node == null)
            {
                // 如果节点不存在，则创建新节点
                node = doc.CreateElement(key);
                root.AppendChild(node);
            }
            node.InnerText = value;
        }
        
        /// <summary>
        /// 触发设置变更事件
        /// </summary>
        private void OnSettingsChanged()
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 系统设置类，用于保存和加载设置
    /// </summary>
    public class Settings
    {
        // Modbus设置
        public string PortName { get; set; }
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public StopBits StopBits { get; set; }
        public Parity Parity { get; set; }
        public int DeviceId { get; set; }
        
        // 相机设置
        public int CameraIndex { get; set; }
        public string CameraUserDefinedName { get; set; }
        public bool UseTrigger { get; set; }
        public bool UseSoftTrigger { get; set; }
        
        // 模型设置
        public string ModelPath { get; set; }
        
        // 设备设置
        public float Speed { get; set; }
        public float TargetPosition { get; set; }
        
        // 图像保存设置
        public string SavePath { get; set; }
        public bool SaveOKImage { get; set; }
        public bool SaveNGImage { get; set; }
        public string ImageFormat { get; set; }
        public string JpegQuality { get; set; }
        
        // 拍照设置
        public int PreCaptureDelay { get; set; }  // 拍照前等待时间（毫秒）
        
        public Settings()
        {
            // 默认设置
            PortName = "";
            BaudRate = 38400;
            DataBits = 8;
            StopBits = StopBits.One;
            Parity = Parity.None;
            DeviceId = 1;
            
            CameraIndex = 0;
            CameraUserDefinedName = string.Empty;
            UseTrigger = true;
            UseSoftTrigger = true;
            
            ModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "default.dvt");
            
            Speed = 100.0f;
            TargetPosition = 0.0f;

            // 图像保存设置
            SavePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");
            SaveOKImage = true;
            SaveNGImage = true;
            ImageFormat = "JPG";
            JpegQuality = "98";

            // 拍照设置
            PreCaptureDelay = 100;  // 默认等待100ms
        }
    }
} 