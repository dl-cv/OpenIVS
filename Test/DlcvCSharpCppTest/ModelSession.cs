using System;
using System.Collections.Generic;
using System.IO;
using DlcvCSharpCppBridge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CSharpModel = dlcv_infer_csharp.Model;

namespace DlcvCSharpCppTest
{
    public sealed class ModelSession : IDisposable
    {
        private CSharpModel csharpModel;
        private CppModel cppModel;
        private bool disposed;

        public bool HasCSharpModel { get { return csharpModel != null; } }
        public bool HasCppModel { get { return cppModel != null; } }
        public bool CppCreatedFromIndex { get; private set; }
        public bool CSharpCreatedFromIndex { get; private set; }
        public int CSharpModelIndex { get { return csharpModel == null ? -1 : csharpModel.modelIndex; } }
        public int CppModelIndex { get { return cppModel == null ? -1 : cppModel.ModelIndex; } }

        public void LoadCSharp(string path, int device)
        {
            ThrowIfDisposed();
            if (HasCSharpModel)
                throw new InvalidOperationException("C# 模型已存在，请先释放 C# 模型。");
            ValidateModelInput(path, device);

            CSharpModel candidate = null;
            try
            {
                candidate = new CSharpModel(path, device, false, false);
                if (candidate.modelIndex == -1)
                    throw new InvalidOperationException("加载失败：模型编号无效。");
                csharpModel = candidate;
                CSharpCreatedFromIndex = false;
                candidate = null;
            }
            finally
            {
                // 完成检查后才保存模型，失败时释放尚未保存的实例。
                if (candidate != null)
                    candidate.Dispose();
            }
        }

        private static void ValidateModelInput(string path, int device)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("模型路径不能为空。", nameof(path));
            if (device < -1)
                throw new ArgumentOutOfRangeException(nameof(device), "设备编号不能小于 -1。");

            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".dvt" && extension != ".dvo" && extension != ".dvst" && extension != ".dvso")
                throw new NotSupportedException("仅支持本地 .dvt、.dvo、.dvst、.dvso 模型。");
            if (!File.Exists(path))
                throw new FileNotFoundException("模型文件不存在。", path);
        }

        public void LoadCpp(string path, int device)
        {
            ThrowIfDisposed();
            if (HasCppModel)
                throw new InvalidOperationException("C++ 模型已存在，请先释放 C++ 模型。");
            ValidateModelInput(path, device);
            CppModel candidate = null;
            try
            {
                candidate = new CppModel(path, device);
                if (candidate.ModelIndex == -1)
                    throw new InvalidOperationException("加载失败：模型编号无效。");
                cppModel = candidate;
                CppCreatedFromIndex = false;
                candidate = null;
            }
            finally
            {
                if (candidate != null) candidate.Dispose();
            }
        }

        public void ConvertToCSharp()
        {
            ThrowIfDisposed();
            if (HasCSharpModel)
                throw new InvalidOperationException("C# 模型已存在，请先释放 C# 模型。");
            if (!HasCppModel)
                throw new InvalidOperationException("请先加载 C++ 模型。");

            CSharpModel candidate = null;
            try
            {
                int index = cppModel.ModelIndex;
                candidate = dlcv_infer_csharp.ModelFactory.CreateFromIndex(index);
                if (candidate.modelIndex != index)
                    throw new InvalidOperationException("共享失败：C# 模型编号与 C++ 模型编号不一致。");
                csharpModel = candidate;
                CSharpCreatedFromIndex = true;
                candidate = null;
            }
            finally
            {
                if (candidate != null) candidate.Dispose();
            }
        }

        public void ConvertToCpp()
        {
            ThrowIfDisposed();
            if (HasCppModel)
                throw new InvalidOperationException("C++ 模型已存在，请先释放 C++ 模型。");
            if (!HasCSharpModel)
                throw new InvalidOperationException("请先加载 C# 模型。");

            CppModel candidate = null;
            try
            {
                // 按已有编号共享模型，不再次从文件加载。
                candidate = new CppModel(csharpModel.modelIndex);
                if (candidate.ModelIndex != csharpModel.modelIndex)
                    throw new InvalidOperationException("转换失败：C++ 模型编号与 C# 模型编号不一致。");
                cppModel = candidate;
                CppCreatedFromIndex = true;
                candidate = null;
            }
            finally
            {
                if (candidate != null)
                    candidate.Dispose();
            }
        }

        public string GetCSharpInfo()
        {
            ThrowIfDisposed();
            if (!HasCSharpModel)
                throw new InvalidOperationException("C# 模型尚未加载。");
            JObject info = csharpModel.GetModelInfo();
            if (info == null)
                throw new InvalidOperationException("C# 模型信息为空。");
            return info.ToString(Formatting.Indented);
        }

        public string GetCppInfo()
        {
            ThrowIfDisposed();
            if (!HasCppModel)
                throw new InvalidOperationException("C++ 模型尚未创建。");
            return JObject.Parse(cppModel.GetModelInfo()).ToString(Formatting.Indented);
        }

        public void ReleaseCSharp()
        {
            CSharpModel model = csharpModel;
            csharpModel = null;
            CSharpCreatedFromIndex = false;
            if (model != null)
                model.Dispose();
        }

        public void ReleaseCpp()
        {
            CppModel model = cppModel;
            cppModel = null;
            CppCreatedFromIndex = false;
            if (model != null)
                model.Dispose();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            var errors = new List<Exception>();
            try
            {
                ReleaseCpp();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
            try
            {
                ReleaseCSharp();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
            if (errors.Count > 0)
                throw new AggregateException("模型释放失败。", errors);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ModelSession));
        }
    }
}
