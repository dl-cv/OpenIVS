using System;
using System.IO;
using OpenCvSharp;

namespace DLCV.SequenceGraph
{
    public sealed class RisingEdgeTracker
    {
        public bool HasSample { get; private set; }
        public int LastValue { get; private set; }

        public bool Sample(int currentValue, int activeValue)
        {
            if (!HasSample)
            {
                LastValue = currentValue;
                HasSample = true;
                return false;
            }
            var rising = LastValue != activeValue && currentValue == activeValue;
            LastValue = currentValue;
            return rising;
        }

        public void Reset()
        {
            HasSample = false;
            LastValue = 0;
        }
    }

    public class VirtualCameraHandle : ICameraHandle
    {
        private readonly string _imagePath;
        private readonly Func<string, object> _loader;
        private readonly int _rotation;

        public VirtualCameraHandle(string imagePath, Func<string, object> loader = null, int rotation = 0)
        {
            _imagePath = imagePath;
            _loader = loader;
            _rotation = ValidateRotation(rotation);
        }

        public object GetFrame()
        {
            return GetFrame(5000);
        }

        public object GetFrame(int timeoutMs)
        {
            if (_loader != null)
            {
                var loaded = _loader(_imagePath);
                if (loaded == null)
                    throw new InvalidDataException("virtual camera image loader returned null: " + _imagePath);
                var mat = loaded as Mat;
                if (mat == null)
                {
                    if (_rotation != 0)
                        throw new InvalidDataException("virtual camera rotation requires decoded Mat: " + _imagePath);
                    return loaded;
                }
                if (mat.Empty())
                {
                    mat.Dispose();
                    throw new InvalidDataException("virtual camera image cannot be decoded: " + _imagePath);
                }
                return Rotate(mat, _rotation);
            }
            if (string.IsNullOrWhiteSpace(_imagePath) || !File.Exists(_imagePath))
                throw new FileNotFoundException("virtual camera image not found", _imagePath);
            if (_rotation != 0)
                throw new InvalidOperationException("virtual camera rotation requires an image loader");
            return _imagePath;
        }

        private static int ValidateRotation(int rotation)
        {
            if (rotation == 0 || rotation == 90 || rotation == 180 || rotation == 270)
                return rotation;
            throw new ArgumentOutOfRangeException("rotation", "camera rotation must be 0, 90, 180 or 270");
        }

        private static Mat Rotate(Mat source, int rotation)
        {
            if (rotation == 0) return source;
            var rotated = new Mat();
            try
            {
                switch (rotation)
                {
                    case 90:
                        Cv2.Rotate(source, rotated, RotateFlags.Rotate90Clockwise);
                        break;
                    case 180:
                        Cv2.Rotate(source, rotated, RotateFlags.Rotate180);
                        break;
                    case 270:
                        Cv2.Rotate(source, rotated, RotateFlags.Rotate90Counterclockwise);
                        break;
                }
                return rotated;
            }
            catch
            {
                rotated.Dispose();
                throw;
            }
            finally
            {
                source.Dispose();
            }
        }

        public void TriggerOnce()
        {
        }

        public void Release()
        {
        }
    }

    public class VirtualCameraResourceFactory : ICameraResourceFactory
    {
        public ICameraHandle Create(ResourceItem resource, Func<string, object> imageLoader)
        {
            if (resource == null)
                throw new ArgumentNullException("resource");
            var config = resource.Config;
            var mode = config != null && config["mode"] != null
                ? config["mode"].ToString()
                : "virtual";
            if (!string.Equals(mode, "virtual", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("camera mode requires an injected camera factory: " + mode);
            var path = config != null && config["hw_id"] != null
                ? config["hw_id"].ToString()
                : null;
            var rotation = config != null && config["rotation"] != null
                ? config["rotation"].ToObject<int>()
                : 0;
            return new VirtualCameraHandle(path, imageLoader, rotation);
        }

        public void Clear()
        {
        }
    }

    public class FlowHandle : IFlowHandle
    {
        private readonly Action<string> _onRelease;
        private bool _released;

        public string Path { get; private set; }

        public FlowHandle(string path, Action<string> onRelease)
        {
            Path = path;
            _onRelease = onRelease;
        }

        public void Release()
        {
            if (_released) return;
            _released = true;
            if (_onRelease != null)
            {
                try { _onRelease(Path); } catch { }
            }
        }
    }

    public class CollectingDisplaySink : IDisplaySink
    {
        public string LastWindowId { get; private set; }
        public object LastImage { get; private set; }
        public object LastResult { get; private set; }
        public int UpdateCount { get; private set; }
        public System.Collections.Generic.Dictionary<string, object> ImagesByWindow { get; private set; } =
            new System.Collections.Generic.Dictionary<string, object>();
        public System.Collections.Generic.List<DisplayUpdateRecord> History { get; private set; } =
            new System.Collections.Generic.List<DisplayUpdateRecord>();

        public void Update(string windowId, object image, object result)
        {
            LastWindowId = windowId;
            LastImage = image;
            LastResult = result;
            UpdateCount++;
            if (!string.IsNullOrEmpty(windowId))
                ImagesByWindow[windowId] = image;
            History.Add(new DisplayUpdateRecord
            {
                Index = UpdateCount,
                WindowId = windowId,
                Image = image,
                Result = result,
                ResultType = result != null ? result.GetType().FullName : null
            });
        }
    }

    public class DisplayUpdateRecord
    {
        public int Index;
        public string WindowId;
        public object Image;
        public object Result;
        public string ResultType;
    }

    public class MockAiFlowRunner : IAiFlowRunner
    {
        public int CallCount { get; private set; }
        public int AcquireCount { get; private set; }
        public string LastFlowPath { get; private set; }
        public string LastFace { get; private set; }
        public string LastResultSourceNodeId { get; private set; }
        public object LastImage { get; private set; }
        public object ResultToReturn { get; set; }

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
            CallCount++;
            LastFlowPath = flowPath;
            LastFace = face;
            LastResultSourceNodeId = resultSourceNodeId;
            LastImage = image;
            if (ResultToReturn != null)
                return ResultToReturn;
            return new System.Collections.Generic.Dictionary<string, object>
            {
                { "ok", true },
                { "flow_path", flowPath },
                { "product_type", productType ?? "" },
                { "barcode", barcode ?? "" },
                { "face", face ?? "" }
            };
        }

        public IFlowHandle AcquireFlow(string flowPath)
        {
            return AcquireFlow(flowPath, null);
        }

        public IFlowHandle AcquireFlow(string flowPath, string face)
        {
            AcquireCount++;
            LastFlowPath = flowPath;
            LastFace = face;
            return new FlowHandle(flowPath, null);
        }
    }

    public class MockProductTypeResolver : IProductTypeResolver
    {
        public string ResultToReturn { get; set; }
        public Exception ExceptionToThrow { get; set; }
        public string LastBarcode { get; private set; }
        public int CallCount { get; private set; }

        public string Resolve(string barcode)
        {
            CallCount++;
            LastBarcode = barcode;
            if (ExceptionToThrow != null)
                throw ExceptionToThrow;
            if (!string.IsNullOrEmpty(ResultToReturn))
                return ResultToReturn;
            return string.IsNullOrEmpty(barcode) ? "Unknown" : "MockProduct";
        }
    }

    public class MockFaceResultSink : IFaceResultSink
    {
        public int CallCount { get; private set; }
        public string LastWindowId { get; private set; }
        public object LastImage { get; private set; }
        public object LastFaceResult { get; private set; }

        public void Publish(string windowId, object image, object faceResult)
        {
            CallCount++;
            LastWindowId = windowId;
            LastImage = image;
            LastFaceResult = faceResult;
        }
    }

    public class MockModbusWriteRecord
    {
        public ushort Address { get; set; }
        public ushort Value { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class MockModbusClient : IModbusClient
    {
        private readonly object _sync = new object();
        public System.Collections.Generic.Dictionary<ushort, ushort> Registers { get; private set; } =
            new System.Collections.Generic.Dictionary<ushort, ushort>();
        public System.Collections.Generic.List<MockModbusWriteRecord> WriteHistory { get; private set; } =
            new System.Collections.Generic.List<MockModbusWriteRecord>();
        public bool Connected { get; private set; }
        public int ConnectCount { get; private set; }
        public int WriteCount { get; private set; }

        public bool Connect(string host, int port, byte deviceId)
        {
            ConnectCount++;
            Connected = true;
            return true;
        }

        public void Close()
        {
            Connected = false;
        }

        public ushort ReadHoldingRegister(ushort address)
        {
            lock (_sync)
            {
                ushort value;
                return Registers.TryGetValue(address, out value) ? value : (ushort)0;
            }
        }

        public ushort[] ReadHoldingRegisters(ushort address, ushort count)
        {
            var arr = new ushort[count];
            for (int i = 0; i < count; i++)
                arr[i] = ReadHoldingRegister((ushort)(address + i));
            return arr;
        }

        public void WriteSingleRegister(ushort address, ushort value)
        {
            lock (_sync)
            {
                WriteCount++;
                Registers[address] = value;
                WriteHistory.Add(new MockModbusWriteRecord
                {
                    Address = address,
                    Value = value,
                    Timestamp = DateTime.Now
                });
                var photoReg = (ushort)(address + 1);
                if (Registers.ContainsKey(photoReg))
                    Registers[photoReg] = 0;
            }
        }

        public void SetRegister(ushort address, ushort value)
        {
            lock (_sync)
                Registers[address] = value;
        }

        public bool HasWritten(ushort address)
        {
            lock (_sync)
                return WriteHistory.Exists(x => x.Address == address);
        }
    }
}
