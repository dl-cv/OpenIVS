using System;
using System.Drawing;
using OpenIVS2.Services;

namespace DlcvSequenceTest
{
    internal static class CameraFrameTests
    {
        private static bool IsDisposed(Bitmap image)
        {
            try { image.GetPixel(0, 0); return false; }
            catch (ArgumentException) { return true; }
        }

        internal static void Run(Action<bool, string> assert)
        {
            var handle = new OpenIvsCameraHandle(new DLCV.Camera.DeviceInfoWrapper(), false);
            var camera = DLCV.Camera.CameraManager.LastCreated;
            try
            {
                camera.DeliverFrame(new Bitmap(3, 2));
                using (var image = (Bitmap)handle.GetFrame(30))
                    assert(image.Width == 3 && image.Height == 2, "真实适配器收到 ImageUpdated 后能取图");
                var timedOut = false;
                try { handle.GetFrame(10); }
                catch (TimeoutException) { timedOut = true; }
                assert(timedOut, "没有新图时仍然报告超时，不复用旧画面");
                camera.DeliverFrame(new Bitmap(4, 2));
                using (var image = (Bitmap)handle.GetFrame(30))
                    assert(image.Width == 4, "超时后的下一帧仍能读取");
                var oldest = new Bitmap(5, 2);
                camera.DeliverFrame(oldest);
                camera.DeliverFrame(new Bitmap(6, 2));
                camera.DeliverFrame(new Bitmap(7, 2));
                assert(IsDisposed(oldest), "队列淘汰帧已释放，不泄漏底层传来的图像");
                using (var first = (Bitmap)handle.GetFrame(30))
                using (var second = (Bitmap)handle.GetFrame(30))
                    assert(first.Width == 6 && second.Width == 7, "连续采集只保留最近两帧");
            }
            finally { handle.Release(); }
            assert(camera.UpdateSubscribers == 0 && camera.Disposed, "释放时解除实际图像事件订阅并关闭相机");
            handle = new OpenIvsCameraHandle(new DLCV.Camera.DeviceInfoWrapper(), true);
            camera = DLCV.Camera.CameraManager.LastCreated;
            try
            {
                using (var image = (Bitmap)handle.GetFrame(30))
                    assert(camera.SoftwareTrigger && camera.TriggerCount == 1 && image.Width == 8,
                        "软件触发图像同样通过 ImageUpdated 进入等待队列");
            }
            finally { handle.Release(); }
        }
    }
}

// 测试专用相机替身：编译链接真实 OpenIvsCameraHandle，不加载硬件 SDK。
// 和 CameraDevice.GrabProcess 一致，只发布 ImageUpdated，不发布 ImageCaptured。
namespace DLCV.Camera
{
    public sealed class DeviceInfoWrapper { }
    public sealed class ImageEventArgs : EventArgs
    {
        public Bitmap Image { get; private set; }
        public ImageEventArgs(Bitmap image) { Image = image; }
    }
    public sealed class CameraManager : IDisposable
    {
        public static CameraManager LastCreated { get; private set; }
        public event EventHandler<ImageEventArgs> ImageUpdated;
        public event EventHandler<ImageEventArgs> ImageCaptured { add { } remove { } }
        public int UpdateSubscribers { get { return ImageUpdated == null ? 0 : ImageUpdated.GetInvocationList().Length; } }
        public bool SoftwareTrigger { get; private set; }
        public int TriggerCount { get; private set; }
        public bool Disposed { get; private set; }
        public CameraManager() { LastCreated = this; }
        public bool ConnectDevice(DeviceInfoWrapper device) { return true; }
        public void SetSoftTrigger() { SoftwareTrigger = true; }
        public bool StartGrabbing() { return true; }
        public void StopGrabbing() { }
        public void TriggerOnce() { TriggerCount++; DeliverFrame(new Bitmap(8, 2)); }
        public void DeliverFrame(Bitmap image)
        {
            var handler = ImageUpdated;
            if (handler != null) handler(this, new ImageEventArgs(image));
            else image.Dispose();
        }
        public void Dispose() { Disposed = true; }
    }
}
