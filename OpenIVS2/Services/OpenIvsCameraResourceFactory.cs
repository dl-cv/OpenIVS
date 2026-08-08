using System;
using System.Collections.Generic;
using DLCV.Camera;
using DLCV.SequenceGraph;

namespace OpenIVS2.Services
{
    public sealed class OpenIvsCameraResourceFactory : ICameraResourceFactory
    {
        private readonly object _sync = new object();
        private readonly List<ICameraHandle> _handles = new List<ICameraHandle>();

        public ICameraHandle Create(ResourceItem resource, Func<string, object> imageLoader)
        {
            if (resource == null) throw new ArgumentNullException("resource");
            var mode = resource.Config != null && resource.Config["mode"] != null
                ? resource.Config["mode"].ToString()
                : "virtual";
            ICameraHandle handle;
            if (string.Equals(mode, "virtual", StringComparison.OrdinalIgnoreCase))
            {
                var path = resource.Config != null && resource.Config["hw_id"] != null
                    ? resource.Config["hw_id"].ToString()
                    : null;
                var rotation = resource.Config != null && resource.Config["rotation"] != null
                    ? resource.Config["rotation"].ToObject<int>()
                    : 0;
                handle = new VirtualCameraHandle(path, imageLoader, rotation);
            }
            else if (string.Equals(mode, "hik", StringComparison.OrdinalIgnoreCase))
            {
                var cameraId = resource.Config != null && resource.Config["hw_id"] != null
                    ? resource.Config["hw_id"].ToString()
                    : null;
                var softwareTrigger = resource.Config != null && resource.Config["software_trigger"] != null &&
                    resource.Config["software_trigger"].ToObject<bool>();
                var device = FindDevice(cameraId);
                handle = new OpenIvsCameraHandle(device, softwareTrigger);
            }
            else
            {
                throw new InvalidOperationException("不支持的相机模式: " + mode);
            }
            lock (_sync) _handles.Add(handle);
            return handle;
        }

        public void Clear()
        {
            List<ICameraHandle> handles;
            lock (_sync)
            {
                handles = new List<ICameraHandle>(_handles);
                _handles.Clear();
            }
            foreach (var handle in handles)
            {
                try { handle.Release(); } catch { }
            }
        }

        private static DeviceInfoWrapper FindDevice(string cameraId)
        {
            var devices = CameraUtils.EnumerateDevices();
            if (devices.Count == 0) throw new InvalidOperationException("未发现海康相机");
            if (string.IsNullOrWhiteSpace(cameraId)) return devices[0];
            foreach (var device in devices)
            {
                if (string.Equals(device.SerialNumber, cameraId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(device.UserId, cameraId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(device.ToString(), cameraId, StringComparison.OrdinalIgnoreCase))
                    return device;
            }
            throw new InvalidOperationException("找不到相机: " + cameraId);
        }
    }
}
