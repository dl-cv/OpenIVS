using System;
using System.IO;
using System.IO.Ports;

namespace OpenIVSWPF
{
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
        public int CaptureDelayTime { get; set; } // 拍照前等待时间（毫秒）
        
        // 图像保存设置
        public string SavePath { get; set; }
        public bool SaveOKImage { get; set; }
        public bool SaveNGImage { get; set; }
        public string ImageFormat { get; set; }
        public string JpegQuality { get; set; }
        
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
            CaptureDelayTime = 100; // 默认等待100毫秒
            
            // 图像保存设置
            SavePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");
            SaveOKImage = true;
            SaveNGImage = true;
            ImageFormat = "JPG";
            JpegQuality = "98";
        }
    }
} 