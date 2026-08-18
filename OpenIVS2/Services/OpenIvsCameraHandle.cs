using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using DLCV.Camera;
using DLCV.SequenceGraph;

namespace OpenIVS2.Services
{
    public sealed class OpenIvsCameraHandle : ICameraHandle
    {
        private readonly object _sync = new object();
        private readonly Queue<Bitmap> _frames = new Queue<Bitmap>();
        private readonly AutoResetEvent _frameReady = new AutoResetEvent(false);
        private readonly CameraManager _camera;
        private readonly bool _softwareTrigger;
        private bool _released;

        public OpenIvsCameraHandle(DeviceInfoWrapper device, bool softwareTrigger)
        {
            if (device == null) throw new ArgumentNullException("device");
            _softwareTrigger = softwareTrigger;
            _camera = new CameraManager();
            _camera.ImageCaptured += Camera_ImageCaptured;
            if (!_camera.ConnectDevice(device))
                throw new InvalidOperationException("相机连接失败: " + device);
            if (_softwareTrigger) _camera.SetSoftTrigger();
            if (!_camera.StartGrabbing())
                throw new InvalidOperationException("相机开始采集失败: " + device);
        }

        public object GetFrame()
        {
            return GetFrame(5000);
        }

        public object GetFrame(int timeoutMs)
        {
            ThrowIfReleased();
            if (_softwareTrigger) _camera.TriggerOnce();
            var deadline = Environment.TickCount + Math.Max(1, timeoutMs);
            while (true)
            {
                lock (_sync)
                {
                    if (_frames.Count > 0) return _frames.Dequeue();
                }
                var remaining = deadline - Environment.TickCount;
                if (remaining <= 0 || !_frameReady.WaitOne(remaining))
                    throw new TimeoutException("等待相机画面超时");
            }
        }

        public void TriggerOnce()
        {
            ThrowIfReleased();
            _camera.TriggerOnce();
        }

        public void Release()
        {
            if (_released) return;
            _released = true;
            _camera.ImageCaptured -= Camera_ImageCaptured;
            try { _camera.StopGrabbing(); } catch { }
            try { _camera.Dispose(); } catch { }
            lock (_sync)
            {
                while (_frames.Count > 0) _frames.Dequeue().Dispose();
            }
            _frameReady.Set();
            _frameReady.Dispose();
        }

        private void Camera_ImageCaptured(object sender, ImageEventArgs e)
        {
            if (_released || e == null || e.Image == null) return;
            var copy = new Bitmap(e.Image);
            lock (_sync)
            {
                while (_frames.Count >= 2) _frames.Dequeue().Dispose();
                _frames.Enqueue(copy);
            }
            _frameReady.Set();
        }

        private void ThrowIfReleased()
        {
            if (_released) throw new ObjectDisposedException("OpenIvsCameraHandle");
        }
    }
}
