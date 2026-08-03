using System;

namespace DLCV.SequenceGraph
{
    public enum CameraResourceState
    {
        NotOpened,
        Opening,
        Opened,
        Failed
    }

    public interface IResourceHandle
    {
        void Release();
    }

    public interface ICameraHandle : IResourceHandle
    {
        object GetFrame();
        object GetFrame(int timeoutMs);
        void TriggerOnce();
    }

    public interface IFlowHandle : IResourceHandle
    {
        string Path { get; }
    }

    public interface IAiFlowRunner
    {
        object Run(string flowPath, object image, string productType, string barcode);
        object Run(string flowPath, object image, string productType, string barcode, string resultSourceNodeId);
        object Run(string flowPath, object image, string productType, string barcode, string resultSourceNodeId, string face);
        IFlowHandle AcquireFlow(string flowPath);
        IFlowHandle AcquireFlow(string flowPath, string face);
    }

    public interface IProductTypeResolver
    {
        string Resolve(string barcode);
    }

    public interface IDisplaySink
    {
        void Update(string windowId, object image, object result);
    }

    public interface IFaceResultSink
    {
        void Publish(string windowId, object image, object faceResult);
    }

    public interface IModbusClient
    {
        bool Connect(string host, int port, byte deviceId);
        void Close();
        ushort ReadHoldingRegister(ushort address);
        ushort[] ReadHoldingRegisters(ushort address, ushort count);
        void WriteSingleRegister(ushort address, ushort value);
    }

    public interface ITriggerSourceHandle
    {
        void Disarm();
    }

    public interface ICameraResourceFactory
    {
        ICameraHandle Create(ResourceItem resource, Func<string, object> imageLoader);
        void Clear();
    }
}
