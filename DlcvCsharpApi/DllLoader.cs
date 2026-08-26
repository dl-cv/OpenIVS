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
            lock (_lock)
            {
                List<DogProvider> availableProviders = DogUtils.GetAvailableProviders();
                if (needed.HasValue && !availableProviders.Contains(needed.Value))
                {
                    if (availableProviders.Count == 0)
                    {
                        throw new Exception("未检测到授权");
                    }
                    string current = FormatProviderNames(availableProviders);
                    string neededName = ProviderToDisplayName(needed.Value);
                    throw new Exception($"当前使用的是 {current}，加载的模型是 {neededName} 格式，类型错误");
                }

                // 原生 DLL 在进程内只按首次检测到的授权类型加载一次。
                // 模型头仅用于授权检查，不能据此更换已加载的 DLL。
                if (_instance == null)
                    _instance = CreateLoader(SelectPreferredProvider(availableProviders));
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
            if (ext == ".dvst" || ext == ".dvso" || ext == ".dvsp")
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
