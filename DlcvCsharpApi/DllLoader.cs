using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;
using sntl_admin_csharp;

namespace dlcv_infer_csharp
{
    public class DllLoader
    {
        private string DllName;
        private string DllPath;
        private const CallingConvention calling_method = CallingConvention.StdCall;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr LoadModelDelegate(string config_str);
        public LoadModelDelegate dlcv_load_model;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr FreeModelDelegate(string config_str);
        public FreeModelDelegate dlcv_free_model;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr GetModelInfoDelegate(string config_str);
        public GetModelInfoDelegate dlcv_get_model_info;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr InferDelegate(string config_str);
        public InferDelegate dlcv_infer;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate void FreeModelResultDelegate(IntPtr config_str);
        public FreeModelResultDelegate dlcv_free_model_result;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate void FreeResultDelegate(IntPtr config_str);
        public FreeResultDelegate dlcv_free_result;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate void FreeAllModelsDelegate();
        public FreeAllModelsDelegate dlcv_free_all_models;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate int GetIndexTypeDelegate(int index);
        public GetIndexTypeDelegate dlcv_get_index_type_c;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr GetModelInfoByIndexDelegate(int index);
        public GetModelInfoByIndexDelegate dlcv_get_model_info_c;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate int RegisterFlowDelegate(IntPtr flowJsonUtf8);
        public RegisterFlowDelegate dlcv_register_flow_c;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr GetFlowInfoDelegate(int index);
        public GetFlowInfoDelegate dlcv_get_flow_info_c;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate int FreeFlowDelegate(int index);
        public FreeFlowDelegate dlcv_free_flow_c;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate int BindIndexDelegate(int index);
        public BindIndexDelegate dlcv_bind_index_c;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate int UnbindIndexDelegate(int index);
        public UnbindIndexDelegate dlcv_unbind_index_c;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr GetDeviceInfo();
        public GetDeviceInfo dlcv_get_device_info;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr GetGpuInfo();
        public GetGpuInfo dlcv_get_gpu_info;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr KeepMaxClock();
        public KeepMaxClock dlcv_keep_max_clock;

        private static DllLoader _instance;
        private static readonly object _lock = new object();

        public DogProvider LoadedDogProvider { get; private set; }
        public string LoadedNativeDllName { get; private set; }
        internal bool SupportsSharedFlowIndex
        {
            get
            {
                return dlcv_get_index_type_c != null &&
                       dlcv_register_flow_c != null &&
                       dlcv_get_flow_info_c != null &&
                       dlcv_free_flow_c != null &&
                       dlcv_bind_index_c != null &&
                       dlcv_unbind_index_c != null;
            }
        }

        public static DllLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = CreateLoader(AutoDetectProvider());
                    }
                }
                return _instance;
            }
        }

        public static void EnsureForModel(string modelPath)
        {
            DogProvider? needed = ResolveProviderFromHeader(modelPath);
            if (!needed.HasValue) return;
            if (_instance != null && _instance.LoadedDogProvider == needed.Value) return;
            lock (_lock)
            {
                if (_instance != null && _instance.LoadedDogProvider == needed.Value) return;

                _instance = CreateLoader(needed.Value);
            }
        }

        internal static DllLoader GetExistingOrDefaultSentinel()
        {
            lock (_lock)
            {
                if (_instance != null)
                    return _instance;

                _instance = CreateLoader(DogProvider.Sentinel);
                return _instance;
            }
        }

        public static DllLoader ResolveForIndex(int index, out string indexType)
        {
            DogProvider provider = GetSharedIndexRoute(index, out indexType);
            DllLoader current = _instance;
            DllLoader loader = current != null && current.LoadedDogProvider == provider
                ? current
                : CreateLoader(provider);
            loader.EnsureSharedIndexSupport(indexType);
            return loader;
        }

        public static DogProvider GetSharedIndexRoute(int index, out string indexType)
        {
            if (index >= 0 && index < 10000)
            {
                indexType = "model";
                return DogProvider.Sentinel;
            }
            if (index >= 10000 && index < 20000)
            {
                indexType = "flow";
                return DogProvider.Sentinel;
            }
            if (index >= 20000 && index < 30000)
            {
                indexType = "model";
                return DogProvider.Virbox;
            }
            if (index >= 30000 && index < 40000)
            {
                indexType = "flow";
                return DogProvider.Virbox;
            }

            throw new ArgumentOutOfRangeException(nameof(index), "外部共享 index 不在支持范围内: " + index);
        }

        private void EnsureSharedIndexSupport(string indexType)
        {
            var missing = new List<string>();
            if (dlcv_get_index_type_c == null) missing.Add("dlcv_get_index_type_c");
            if (dlcv_bind_index_c == null) missing.Add("dlcv_bind_index_c");
            if (dlcv_unbind_index_c == null) missing.Add("dlcv_unbind_index_c");
            if (string.Equals(indexType, "model", StringComparison.Ordinal) && dlcv_get_model_info_c == null)
                missing.Add("dlcv_get_model_info_c");
            if (string.Equals(indexType, "flow", StringComparison.Ordinal) && dlcv_get_flow_info_c == null)
                missing.Add("dlcv_get_flow_info_c");
            if (missing.Count > 0)
            {
                throw new NotSupportedException(
                    "当前 dlcv_infer 不支持外部共享 index，缺少接口: " + string.Join(", ", missing));
            }
        }

        public int GetIndexType(int index)
        {
            EnsureDelegate(dlcv_get_index_type_c, "dlcv_get_index_type_c");
            return dlcv_get_index_type_c(index);
        }

        public JObject GetModelInfoByIndex(int index)
        {
            return InvokeJson(() =>
            {
                EnsureDelegate(dlcv_get_model_info_c, "dlcv_get_model_info_c");
                return dlcv_get_model_info_c(index);
            }, "获取模型信息");
        }

        public int RegisterFlow(string flowJson)
        {
            if (flowJson == null)
                throw new ArgumentNullException(nameof(flowJson));
            EnsureDelegate(dlcv_register_flow_c, "dlcv_register_flow_c");
            return InvokeUtf8(flowJson, dlcv_register_flow_c);
        }

        public JObject GetFlowInfo(int index)
        {
            return InvokeJson(() =>
            {
                EnsureDelegate(dlcv_get_flow_info_c, "dlcv_get_flow_info_c");
                return dlcv_get_flow_info_c(index);
            }, "获取流程信息");
        }

        public int FreeFlow(int index)
        {
            EnsureDelegate(dlcv_free_flow_c, "dlcv_free_flow_c");
            return dlcv_free_flow_c(index);
        }

        public int BindIndex(int index)
        {
            EnsureDelegate(dlcv_bind_index_c, "dlcv_bind_index_c");
            return dlcv_bind_index_c(index);
        }

        public int UnbindIndex(int index)
        {
            EnsureDelegate(dlcv_unbind_index_c, "dlcv_unbind_index_c");
            return dlcv_unbind_index_c(index);
        }

        private delegate IntPtr JsonCall();

        private JObject InvokeJson(JsonCall call, string operation)
        {
            IntPtr resultPtr = call();
            if (resultPtr == IntPtr.Zero)
                throw new Exception(operation + "失败：返回结果为空");

            try
            {
                string json = ReadUtf8String(resultPtr);
                if (string.IsNullOrWhiteSpace(json))
                    throw new Exception(operation + "失败：返回 JSON 为空");
                return JObject.Parse(json);
            }
            finally
            {
                if (dlcv_free_result == null)
                    throw new MissingMethodException("未找到 dlcv_free_result");
                dlcv_free_result(resultPtr);
            }
        }

        private static int InvokeUtf8(string value, RegisterFlowDelegate call)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value + "\0");
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return call(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private static string ReadUtf8String(IntPtr value)
        {
            int length = 0;
            while (Marshal.ReadByte(value, length) != 0)
                length++;
            byte[] bytes = new byte[length];
            Marshal.Copy(value, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static void EnsureDelegate(Delegate value, string name)
        {
            if (value == null)
                throw new MissingMethodException("未找到 " + name);
        }

        private static DllLoader CreateLoader(DogProvider provider)
        {
            var loader = new DllLoader();
            loader.LoadedDogProvider = provider;
            switch (provider)
            {
                case DogProvider.None:
                    // 只执行加密狗检测，不加载推理 DLL
                    loader.DllName = null;
                    loader.DllPath = null;
                    loader.LoadedNativeDllName = null;
                    return loader;
                case DogProvider.Sentinel:
                    loader.DllName = "dlcv_infer.dll";
                    loader.DllPath = @"C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll";
                    break;
                case DogProvider.Virbox:
                    loader.DllName = "dlcv_infer_v.dll";
                    loader.DllPath = @"C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer_v.dll";
                    break;
                default:
                    throw new ArgumentException("不支持的 dog provider: " + provider);
            }
            loader.LoadedNativeDllName = loader.DllName;
            loader.LoadDll();
            return loader;
        }

        private static DogProvider AutoDetectProvider()
        {
            // 只做一次加密狗检测：先 Sentinel 再 Virbox；都没有则不加载任何推理 DLL，也不抛异常
            List<DogProvider> available = DogUtils.GetAvailableProviders();
            if (available.Contains(DogProvider.Sentinel))
                return DogProvider.Sentinel;
            if (available.Contains(DogProvider.Virbox))
                return DogProvider.Virbox;
            return DogProvider.None;
        }

        private static DogProvider? ResolveProviderFromHeader(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("模型路径不能为空", nameof(modelPath));

            string ext = Path.GetExtension(modelPath).ToLower();
            if (ext == ".dvp")
                throw new NotSupportedException("DVP 模式不通过 header 解析 provider");
            if (ext == ".dvsp")
                throw new NotSupportedException("不支持 .dvsp 模型推理");
            if (ext == ".dvst" || ext == ".dvso")
                throw new NotSupportedException("DVS 模式在子模型加载时解析 header provider");

            using (var fs = new FileStream(modelPath, FileMode.Open, FileAccess.Read))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string header = reader.ReadLine();
                if (header != "DV")
                    throw new Exception("模型文件格式错误：缺少 DV 头");

                string headerJsonStr = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(headerJsonStr))
                    throw new Exception("模型文件格式错误：缺少 header_json");

                JObject headerJson = JObject.Parse(headerJsonStr);
                if (!headerJson.ContainsKey("dog_provider"))
                    return null;

                string p = headerJson["dog_provider"]?.ToString()?.ToLower() ?? "";
                if (p == "sentinel") return DogProvider.Sentinel;
                if (p == "virbox") return DogProvider.Virbox;
                throw new Exception($"invalid dog provider in header_json: {p}");
            }
        }

        private void LoadDll()
        {
            if (!DllExists(DllName, DllPath))
            {
                MessageBox(IntPtr.Zero, "需要先安装 dlcv_infer", "提示", 0x00000030u);
                throw new Exception("需要先安装 dlcv_infer");
            }

            IntPtr hModule = LoadLibrary(DllName);
            if (hModule == IntPtr.Zero)
            {
                hModule = LoadLibrary(DllPath);
                if (hModule == IntPtr.Zero)
                    throw new Exception("无法加载 DLL");
            }

            dlcv_load_model = GetDelegate<LoadModelDelegate>(hModule, "dlcv_load_model");
            dlcv_free_model = GetDelegate<FreeModelDelegate>(hModule, "dlcv_free_model");
            dlcv_get_model_info = GetDelegate<GetModelInfoDelegate>(hModule, "dlcv_get_model_info");
            dlcv_infer = GetDelegate<InferDelegate>(hModule, "dlcv_infer");
            dlcv_free_model_result = GetDelegate<FreeModelResultDelegate>(hModule, "dlcv_free_model_result");
            dlcv_free_result = GetDelegate<FreeResultDelegate>(hModule, "dlcv_free_result");
            dlcv_free_all_models = GetDelegate<FreeAllModelsDelegate>(hModule, "dlcv_free_all_models");
            dlcv_get_index_type_c = GetDelegate<GetIndexTypeDelegate>(hModule, "dlcv_get_index_type_c");
            dlcv_get_model_info_c = GetDelegate<GetModelInfoByIndexDelegate>(hModule, "dlcv_get_model_info_c");
            dlcv_register_flow_c = GetDelegate<RegisterFlowDelegate>(hModule, "dlcv_register_flow_c");
            dlcv_get_flow_info_c = GetDelegate<GetFlowInfoDelegate>(hModule, "dlcv_get_flow_info_c");
            dlcv_free_flow_c = GetDelegate<FreeFlowDelegate>(hModule, "dlcv_free_flow_c");
            dlcv_bind_index_c = GetDelegate<BindIndexDelegate>(hModule, "dlcv_bind_index_c");
            dlcv_unbind_index_c = GetDelegate<UnbindIndexDelegate>(hModule, "dlcv_unbind_index_c");
            IntPtr gpuInfoPtr = GetProcAddress(hModule, "dlcv_get_gpu_info");
            dlcv_get_gpu_info = gpuInfoPtr != IntPtr.Zero ? (GetGpuInfo)Marshal.GetDelegateForFunctionPointer(gpuInfoPtr, typeof(GetGpuInfo)) : null;
            IntPtr devInfoPtr = GetProcAddress(hModule, "dlcv_get_device_info");
            dlcv_get_device_info = devInfoPtr != IntPtr.Zero ? (GetDeviceInfo)Marshal.GetDelegateForFunctionPointer(devInfoPtr, typeof(GetDeviceInfo)) : null;
            dlcv_keep_max_clock = GetDelegate<KeepMaxClock>(hModule, "dlcv_keep_max_clock");
        }

        private static bool DllExists(string dllName, string dllPath)
        {
            return !string.IsNullOrEmpty(SearchDllPath(dllName)) || File.Exists(dllPath);
        }

        private static string SearchDllPath(string dllName)
        {
            var buffer = new StringBuilder(32767);
            uint result = SearchPath(null, dllName, null, (uint)buffer.Capacity, buffer, IntPtr.Zero);
            return result == 0 || result >= (uint)buffer.Capacity ? null : buffer.ToString();
        }

        private T GetDelegate<T>(IntPtr hModule, string procedureName) where T : Delegate
        {
            IntPtr p = GetProcAddress(hModule, procedureName);
            return p == IntPtr.Zero ? null : (T)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        [DllImport("kernel32.dll", EntryPoint = "SearchPathW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint SearchPath(string lpPath, string lpFileName, string lpExtension, uint nBufferLength, StringBuilder lpBuffer, IntPtr lpFilePart);

        [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    }
}
