using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenIVS2.Models
{
    public sealed class CameraSettings
    {
        public string Slot { get; set; }
        public bool Enabled { get; set; }
        public string Name { get; set; }
        public string Mode { get; set; }
        public string CameraId { get; set; }
        public string VirtualImagePath { get; set; }
        public string ModelPath { get; set; }
        public int DeviceId { get; set; }
        public int Rotation { get; set; }
        public bool SoftwareTrigger { get; set; }
        public int FrameTimeoutMs { get; set; }

        public CameraSettings Clone()
        {
            return (CameraSettings)MemberwiseClone();
        }
    }

    public sealed class AppSettings
    {
        public bool UsePlc { get; set; }
        public string PlcMode { get; set; }
        public string TcpHost { get; set; }
        public int TcpPort { get; set; }
        public string PortName { get; set; }
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public string StopBits { get; set; }
        public string Parity { get; set; }
        public int DeviceId { get; set; }
        public ushort PhotoRegister { get; set; }
        public ushort TriggerValue { get; set; }
        public ushort ClearValue { get; set; }
        public int PollIntervalMs { get; set; }
        public string SaveDirectory { get; set; }
        public bool SaveOkImages { get; set; }
        public bool SaveNgImages { get; set; }
        public string ImageFormat { get; set; }
        public int JpegQuality { get; set; }
        public List<CameraSettings> Cameras { get; set; }

        public static AppSettings CreateDefault()
        {
            var settings = new AppSettings
            {
                UsePlc = false,
                PlcMode = "mock",
                TcpHost = "127.0.0.1",
                TcpPort = 502,
                PortName = "COM1",
                BaudRate = 9600,
                DataBits = 8,
                StopBits = "One",
                Parity = "None",
                DeviceId = 1,
                PhotoRegister = 500,
                TriggerValue = 1,
                ClearValue = 0,
                PollIntervalMs = 50,
                SaveDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SavedImages"),
                SaveOkImages = true,
                SaveNgImages = true,
                ImageFormat = "PNG",
                JpegQuality = 90,
                Cameras = new List<CameraSettings>()
            };
            for (var i = 0; i < 6; i++)
            {
                var slot = ((char)('A' + i)).ToString();
                settings.Cameras.Add(new CameraSettings
                {
                    Slot = slot,
                    Enabled = i < 3,
                    Name = "相机" + slot,
                    Mode = "virtual",
                    CameraId = "",
                    VirtualImagePath = "",
                    ModelPath = "",
                    DeviceId = 0,
                    Rotation = 0,
                    SoftwareTrigger = false,
                    FrameTimeoutMs = 5000
                });
            }
            return settings;
        }

        public AppSettings Clone()
        {
            return new AppSettings
            {
                UsePlc = UsePlc,
                PlcMode = PlcMode,
                TcpHost = TcpHost,
                TcpPort = TcpPort,
                PortName = PortName,
                BaudRate = BaudRate,
                DataBits = DataBits,
                StopBits = StopBits,
                Parity = Parity,
                DeviceId = DeviceId,
                PhotoRegister = PhotoRegister,
                TriggerValue = TriggerValue,
                ClearValue = ClearValue,
                PollIntervalMs = PollIntervalMs,
                SaveDirectory = SaveDirectory,
                SaveOkImages = SaveOkImages,
                SaveNgImages = SaveNgImages,
                ImageFormat = ImageFormat,
                JpegQuality = JpegQuality,
                Cameras = (Cameras ?? new List<CameraSettings>()).Select(x => x.Clone()).ToList()
            };
        }

        public List<CameraSettings> EnabledCameras()
        {
            return (Cameras ?? new List<CameraSettings>())
                .Where(x => x.Enabled)
                .OrderBy(x => x.Slot, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
        }

        public void Normalize()
        {
            if (Cameras == null) Cameras = new List<CameraSettings>();
            for (var i = 0; i < 6; i++)
            {
                var slot = ((char)('A' + i)).ToString();
                var existing = Cameras.FirstOrDefault(x => string.Equals(x.Slot, slot, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = CreateDefault().Cameras[i];
                    existing.Enabled = false;
                    Cameras.Add(existing);
                }
                if (string.IsNullOrWhiteSpace(existing.Name)) existing.Name = "相机" + slot;
                if (string.IsNullOrWhiteSpace(existing.Mode)) existing.Mode = "virtual";
                if (existing.FrameTimeoutMs <= 0) existing.FrameTimeoutMs = 5000;
            }
            Cameras = Cameras.OrderBy(x => x.Slot, StringComparer.OrdinalIgnoreCase).Take(6).ToList();
            if (PollIntervalMs < 20) PollIntervalMs = 20;
            if (JpegQuality < 1 || JpegQuality > 100) JpegQuality = 90;
            if (string.IsNullOrWhiteSpace(ImageFormat)) ImageFormat = "PNG";
            if (string.IsNullOrWhiteSpace(PlcMode)) PlcMode = "mock";
            if (string.IsNullOrWhiteSpace(TcpHost)) TcpHost = "127.0.0.1";
            if (TcpPort < 1 || TcpPort > 65535) TcpPort = 502;
        }
    }
}
