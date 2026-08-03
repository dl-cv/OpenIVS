using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using DLCV.SequenceGraph;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using dlcv_infer_csharp;

namespace OpenIVS2.Services
{
    public sealed class DlcvFlowRunner : IAiFlowRunner, IDisposable
    {
        private sealed class ModelEntry
        {
            public Model Model;
            public int ReferenceCount;
        }

        private readonly object _sync = new object();
        private readonly Dictionary<string, int> _deviceIds;
        private readonly Dictionary<string, ModelEntry> _models = new Dictionary<string, ModelEntry>(StringComparer.OrdinalIgnoreCase);

        public DlcvFlowRunner(Dictionary<string, int> deviceIds)
        {
            _deviceIds = deviceIds ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        public object Run(string flowPath, object image, string productType, string barcode)
        {
            return Run(flowPath, image, productType, barcode, null, null);
        }

        public object Run(string flowPath, object image, string productType, string barcode, string resultSourceNodeId)
        {
            return Run(flowPath, image, productType, barcode, resultSourceNodeId, null);
        }

        public object Run(string flowPath, object image, string productType, string barcode, string resultSourceNodeId, string face)
        {
            Model model;
            lock (_sync)
            {
                ModelEntry entry;
                if (!_models.TryGetValue(flowPath, out entry))
                    throw new InvalidOperationException("模型未预加载: " + flowPath);
                model = entry.Model;
            }
            using (var source = ToMat(image))
            using (var rgb = ToRgb(source))
                return model.Infer(rgb);
        }

        public IFlowHandle AcquireFlow(string flowPath)
        {
            return AcquireFlow(flowPath, null);
        }

        public IFlowHandle AcquireFlow(string flowPath, string face)
        {
            if (string.IsNullOrWhiteSpace(flowPath)) throw new InvalidOperationException("模型路径不能为空");
            if (!File.Exists(flowPath)) throw new FileNotFoundException("模型文件不存在", flowPath);
            lock (_sync)
            {
                ModelEntry entry;
                if (!_models.TryGetValue(flowPath, out entry))
                {
                    int deviceId;
                    if (!_deviceIds.TryGetValue(flowPath, out deviceId)) deviceId = 0;
                    entry = new ModelEntry { Model = new Model(flowPath, deviceId), ReferenceCount = 0 };
                    _models.Add(flowPath, entry);
                }
                entry.ReferenceCount++;
            }
            return new FlowHandle(flowPath, ReleaseFlow);
        }

        public void Dispose()
        {
            List<Model> models = new List<Model>();
            lock (_sync)
            {
                foreach (var entry in _models.Values) models.Add(entry.Model);
                _models.Clear();
            }
            foreach (var model in models)
            {
                try { model.Dispose(); } catch { }
            }
        }

        private void ReleaseFlow(string path)
        {
            Model model = null;
            lock (_sync)
            {
                ModelEntry entry;
                if (!_models.TryGetValue(path, out entry)) return;
                entry.ReferenceCount--;
                if (entry.ReferenceCount <= 0)
                {
                    model = entry.Model;
                    _models.Remove(path);
                }
            }
            if (model != null) model.Dispose();
        }

        private static Mat ToMat(object image)
        {
            var mat = image as Mat;
            if (mat != null)
            {
                if (mat.Empty()) throw new InvalidDataException("输入图像为空");
                return mat.Clone();
            }
            var bitmap = image as Bitmap;
            if (bitmap != null) return BitmapConverter.ToMat(bitmap);
            var path = image as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var loaded = Cv2.ImRead(path, ImreadModes.Unchanged);
                if (loaded.Empty())
                {
                    loaded.Dispose();
                    throw new InvalidDataException("无法读取图像: " + path);
                }
                return loaded;
            }
            throw new InvalidDataException("不支持的图像类型");
        }

        private static Mat ToRgb(Mat source)
        {
            var rgb = new Mat();
            if (source.Channels() == 4) Cv2.CvtColor(source, rgb, ColorConversionCodes.BGRA2RGB);
            else if (source.Channels() == 3) Cv2.CvtColor(source, rgb, ColorConversionCodes.BGR2RGB);
            else if (source.Channels() == 1) source.CopyTo(rgb);
            else throw new InvalidDataException("不支持的图像通道数: " + source.Channels());
            return rgb;
        }
    }
}
