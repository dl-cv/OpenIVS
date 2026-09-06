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
        public delegate IntPtr LoadModelBinaryDelegate(IntPtr model_data, UIntPtr model_size, string config_str);
        public LoadModelBinaryDelegate dlcv_load_model_binary;

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
        private static readonly Dictionary<DogProvider, DllLoader> _loaders =
            new Dictionary<DogProvider, DllLoader>();
        private static readonly Dictionary<IntPtr, DllLoader> _moduleLoaders =
            new Dictionary<IntPtr, DllLoader>();
        private static readonly object _lock = new object();

        private IntPtr _moduleHandle;
        private string _modulePath;

        public DogProvider LoadedDogProvider { get; private set; }
        public string LoadedNativeDllName { get; private set; }
        internal string LoadedNativeModulePath { get { return _modulePath; } }
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
                lock (_lock)
                {
                    if (_instance == null)
                        _instance = GetOrCreateLoaderLocked(AutoDetectProvider());
                    return _instance;
                }
            }
        }

        public static void EnsureForModel(string modelPath)
        {
            GetForModel(modelPath);
        }

        internal static DllLoader GetForModel(string modelPath)
        {
            DogProvider? needed = ResolveProviderFromHeader(modelPath);
            if (!needed.HasValue)
                return Instance;

            lock (_lock)
            {
                ValidateProviderAvailability(needed.Value);
                _instance = GetOrCreateLoaderLocked(needed.Value);
                return _instance;
            }
        }

        internal static DllLoader ForModel(byte[] modelData, string modelName)
        {
            DogProvider? needed = ResolveProviderFromHeader(modelData, modelName);
            if (!needed.HasValue)
                return Instance;

            lock (_lock)
            {
                ValidateProviderAvailability(needed.Value);
                _instance = GetOrCreateLoaderLocked(needed.Value);
                return _instance;
            }
        }

        internal static DllLoader GetExistingOrDefaultSentinel()
        {
            lock (_lock)
            {
                if (_instance != null)
                    return _instance;

                _instance = GetOrCreateLoaderLocked(DogProvider.Sentinel);
                return _instance;
            }
        }

        internal static List<DllLoader> GetLoadedLoaders()
        {
            lock (_lock)
            {
                return GetLoadedModuleLoadersLocked();
            }
        }

        private static string ProviderToDisplayName(DogProvider provider)
        {
            switch (provider)
            {
                case DogProvider.None:
                    return "无";
                case DogProvider.Sentinel:
                    return "Sentinel";
                case DogProvider.Virbox:
                    return "Virbox";
                default:
                    return provider.ToString();
            }
        }

        private static string FormatProviderNames(List<DogProvider> providers)
        {
            if (providers == null || providers.Count == 0)
                return "无";
            if (providers.Count == 1)
                return ProviderToDisplayName(providers[0]);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < providers.Count; i++)
            {
                if (i > 0)
                    sb.Append("、");
                sb.Append(ProviderToDisplayName(providers[i]));
            }
            return sb.ToString();
        }

        public static DllLoader ResolveForIndex(int index, out string indexType)
        {
            DllLoader loader = ResolveSharedIndexLoader(index, out indexType);
            loader.EnsureSharedIndexSupport(indexType);
            return loader;
        }

        public static DogProvider GetSharedIndexRoute(int index, out string indexType)
        {
            return ResolveSharedIndexLoader(index, out indexType).LoadedDogProvider;
        }

        internal static DllLoader ResolveSharedIndexLoader(int index, out string indexType)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "外部共享 index 不能为负数: " + index);

            List<DllLoader> candidates;
            lock (_lock)
            {
                candidates = GetLoadedModuleLoadersLocked();
            }

            return ResolveSharedIndexLoaderFromCandidates(index, candidates, out indexType);
        }

        internal static DllLoader ResolveSharedIndexLoaderFromCandidates(
            int index,
            IList<DllLoader> candidates,
            out string indexType)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "外部共享 index 不能为负数: " + index);
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            var queryableCandidates = new List<DllLoader>();
            foreach (DllLoader candidate in candidates)
            {
                if (candidate.dlcv_get_index_type_c != null)
                    queryableCandidates.Add(candidate);
            }

            if (queryableCandidates.Count == 0)
            {
                throw new NotSupportedException(
                    "进程内没有已加载且提供 dlcv_get_index_type_c 的推理 DLL，无法恢复共享 index");
            }

            var matches = new List<Tuple<DllLoader, int>>();
            foreach (DllLoader candidate in queryableCandidates)
            {
                int nativeType;
                try
                {
                    nativeType = candidate.GetIndexType(index);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "查询共享 index 所属 DLL 失败: " + candidate.GetModuleDisplayName(), ex);
                }

                if (nativeType == 0)
                    continue;
                if (nativeType != 1 && nativeType != 2)
                {
                    throw new InvalidOperationException(
                        "共享 index 查询返回未知类型: " + nativeType + "，DLL=" + candidate.GetModuleDisplayName());
                }

                matches.Add(Tuple.Create(candidate, nativeType));
            }

            if (matches.Count == 0)
            {
                throw new InvalidOperationException("进程内已加载的推理 DLL 均未找到共享 index: " + index);
            }
            if (matches.Count > 1)
            {
                var names = new List<string>();
                foreach (Tuple<DllLoader, int> match in matches)
                    names.Add(match.Item1.GetModuleDisplayName());
                throw new InvalidOperationException(
                    "共享 index 同时存在于多个 DLL，无法确定所属模块: " + string.Join("、", names));
            }

            indexType = IndexTypeName(matches[0].Item2);
            return matches[0].Item1;
        }

        private static string IndexTypeName(int nativeType)
        {
            if (nativeType == 1) return "model";
            if (nativeType == 2) return "flow";
            throw new InvalidOperationException("共享 index 类型无效: " + nativeType);
        }

        internal void EnsureSharedIndexSupport(string indexType)
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

        private static DllLoader GetOrCreateLoaderLocked(DogProvider provider)
        {
            DllLoader loader;
            if (_loaders.TryGetValue(provider, out loader))
                return loader;

            loader = CreateLoader(provider);
            _loaders.Add(provider, loader);
            if (loader._moduleHandle != IntPtr.Zero)
                _moduleLoaders[loader._moduleHandle] = loader;
            return loader;
        }

        private static List<DllLoader> GetLoadedModuleLoadersLocked()
        {
            var result = new List<DllLoader>();
            var seen = new HashSet<IntPtr>();

            foreach (IntPtr moduleHandle in EnumerateTargetModules())
            {
                if (moduleHandle == IntPtr.Zero || !seen.Add(moduleHandle))
                    continue;

                DllLoader loader;
                if (!_moduleLoaders.TryGetValue(moduleHandle, out loader))
                {
                    loader = AttachLoadedModuleLocked(moduleHandle);
                    if (loader != null)
                        _moduleLoaders[moduleHandle] = loader;
                }

                if (loader != null)
                    result.Add(loader);
            }

            foreach (DllLoader loader in _loaders.Values)
            {
                if (loader == null || loader._moduleHandle == IntPtr.Zero || !seen.Add(loader._moduleHandle))
                    continue;
                result.Add(loader);
            }

            return result;
        }

        private static DllLoader AttachLoadedModuleLocked(IntPtr moduleHandle)
        {
            string modulePath = GetModulePath(moduleHandle);
            string moduleName = string.IsNullOrEmpty(modulePath)
                ? null
                : Path.GetFileName(modulePath);
            DogProvider provider;
            if (string.Equals(moduleName, "dlcv_infer.dll", StringComparison.OrdinalIgnoreCase))
                provider = DogProvider.Sentinel;
            else if (string.Equals(moduleName, "dlcv_infer_v.dll", StringComparison.OrdinalIgnoreCase))
                provider = DogProvider.Virbox;
            else
                return null;

            IntPtr protectedHandle;
            if (!ProtectLoadedModule(modulePath, moduleHandle, out protectedHandle))
                throw new InvalidOperationException("无法保护已加载推理 DLL: " + (modulePath ?? moduleName));

            var loader = new DllLoader();
            loader.LoadedDogProvider = provider;
            loader.DllName = moduleName;
            loader.DllPath = modulePath;
            loader.LoadedNativeDllName = moduleName;
            loader._moduleHandle = protectedHandle;
            loader._modulePath = modulePath;
            loader.LoadDelegates(protectedHandle);
            return loader;
        }

        private string GetModuleDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(_modulePath))
                return _modulePath;
            if (!string.IsNullOrWhiteSpace(LoadedNativeDllName))
                return LoadedNativeDllName;
            return LoadedDogProvider.ToString();
        }

        private static void ValidateProviderAvailability(DogProvider needed)
        {
            List<DogProvider> availableProviders = DogUtils.GetAvailableProviders();
            if (availableProviders.Contains(needed))
                return;

            if (availableProviders.Count == 0)
                throw new Exception("未检测到授权");

            string current = FormatProviderNames(availableProviders);
            string neededName = ProviderToDisplayName(needed);
            throw new Exception($"当前使用的是 {current}，加载的模型是 {neededName} 格式，类型错误");
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
            return SelectPreferredProvider(DogUtils.GetAvailableProviders());
        }

        private static DogProvider SelectPreferredProvider(List<DogProvider> available)
        {
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

        private static DogProvider? ResolveProviderFromHeader(byte[] modelData, string modelName)
        {
            if (modelData == null || modelData.Length == 0)
                throw new ArgumentException("模型数据为空", nameof(modelData));

            string displayName = string.IsNullOrWhiteSpace(modelName) ? "内存模型" : modelName;
            if (modelData.Length < 3 || modelData[0] != (byte)'D' || modelData[1] != (byte)'V' || modelData[2] != (byte)'\n')
                throw new Exception($"模型文件格式错误：{displayName} 缺少 DV 头");

            int headerEnd = Array.IndexOf(modelData, (byte)'\n', 3);
            if (headerEnd < 0)
                throw new Exception($"模型文件格式错误：{displayName} 缺少 header_json");

            int headerLength = headerEnd - 3;
            if (headerLength <= 0)
                throw new Exception($"模型文件格式错误：{displayName} 缺少 header_json");

            string headerJsonStr = Encoding.UTF8.GetString(modelData, 3, headerLength).TrimEnd('\r');
            JObject headerJson = JObject.Parse(headerJsonStr);
            if (!headerJson.ContainsKey("dog_provider"))
                return null;

            string providerText = headerJson["dog_provider"]?.ToString()?.ToLowerInvariant() ?? "";
            if (providerText == "sentinel") return DogProvider.Sentinel;
            if (providerText == "virbox") return DogProvider.Virbox;
            throw new Exception($"模型头中的 dog_provider 无效：{providerText}");
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

            _moduleHandle = hModule;
            _modulePath = DllPath;
            LoadDelegates(hModule);
        }

        private void LoadDelegates(IntPtr hModule)
        {
            dlcv_load_model = GetDelegate<LoadModelDelegate>(hModule, "dlcv_load_model");
            dlcv_load_model_binary = GetDelegate<LoadModelBinaryDelegate>(hModule, "dlcv_load_model_binary");
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

        private static List<IntPtr> EnumerateTargetModules()
        {
            var result = new List<IntPtr>();
            IntPtr process = GetCurrentProcess();
            int capacity = 256;
            while (true)
            {
                var modules = new IntPtr[capacity];
                uint bytesNeeded;
                if (!EnumProcessModules(process, modules, (uint)(modules.Length * IntPtr.Size), out bytesNeeded))
                    throw new InvalidOperationException("枚举进程模块失败，无法确定共享 index 候选 DLL");

                int count = (int)(bytesNeeded / (uint)IntPtr.Size);
                if (count < modules.Length)
                {
                    for (int i = 0; i < count; i++)
                    {
                        string path = GetModulePath(process, modules[i]);
                        if (string.IsNullOrEmpty(path))
                            throw new InvalidOperationException("读取进程模块路径失败，无法确定共享 index 候选 DLL");
                        string name = Path.GetFileName(path);
                        if (string.Equals(name, "dlcv_infer.dll", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name, "dlcv_infer_v.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(modules[i]);
                        }
                    }
                    return result;
                }

                capacity *= 2;
                if (capacity > 32768)
                    throw new InvalidOperationException("进程模块数量超出枚举容量，无法确定共享 index 候选 DLL");
            }
        }

        private static string GetModulePath(IntPtr moduleHandle)
        {
            string path = GetModulePath(GetCurrentProcess(), moduleHandle);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("读取已加载推理 DLL 路径失败");
            return path;
        }

        private static string GetModulePath(IntPtr processHandle, IntPtr moduleHandle)
        {
            var buffer = new StringBuilder(32768);
            uint length = GetModuleFileNameEx(processHandle, moduleHandle, buffer, (uint)buffer.Capacity);
            return length == 0 ? null : buffer.ToString();
        }

        private static bool ProtectLoadedModule(string modulePath, IntPtr moduleHandle, out IntPtr protectedHandle)
        {
            if (!string.IsNullOrWhiteSpace(modulePath) &&
                GetModuleHandleEx(0, modulePath, out protectedHandle))
            {
                if (protectedHandle == moduleHandle)
                    return true;
                protectedHandle = IntPtr.Zero;
                return false;
            }

            if (!GetModuleHandleEx(
                GetModuleHandleExFlagFromAddress,
                moduleHandle,
                out protectedHandle))
            {
                return false;
            }
            if (protectedHandle != moduleHandle)
            {
                protectedHandle = IntPtr.Zero;
                return false;
            }
            return true;
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

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetModuleHandleEx(uint flags, string moduleName, out IntPtr moduleHandle);

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleExW", SetLastError = true)]
        private static extern bool GetModuleHandleEx(uint flags, IntPtr moduleAddress, out IntPtr moduleHandle);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EnumProcessModules(
            IntPtr processHandle,
            [Out] IntPtr[] moduleHandles,
            uint bytesAllocated,
            out uint bytesNeeded);

        [DllImport("psapi.dll", EntryPoint = "GetModuleFileNameExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetModuleFileNameEx(
            IntPtr processHandle,
            IntPtr moduleHandle,
            StringBuilder fileName,
            uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        [DllImport("kernel32.dll", EntryPoint = "SearchPathW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint SearchPath(string lpPath, string lpFileName, string lpExtension, uint nBufferLength, StringBuilder lpBuffer, IntPtr lpFilePart);

        [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private const uint GetModuleHandleExFlagFromAddress = 0x00000004;
    }
}
