using System;
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
            if (!needed.HasValue) return;
            if (_instance != null && _instance.LoadedDogProvider == needed.Value) return;
            lock (_lock)
            {
                if (_instance != null && _instance.LoadedDogProvider == needed.Value) return;
                _instance = CreateLoader(needed.Value);
            }
        }

        private static DllLoader CreateLoader(DogProvider provider)
        {
            var loader = new DllLoader();
            loader.LoadedDogProvider = provider;
            switch (provider)
            {
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
            try
            {
                var sentinel = DogUtils.GetSentinelInfo();
                if (sentinel != null && ((sentinel.Devices != null && sentinel.Devices.Count > 0) || (sentinel.Features != null && sentinel.Features.Count > 0)))
                    return DogProvider.Sentinel;
            }
            catch { }
            try
            {
                var virbox = DogUtils.GetVirboxInfo();
                if (virbox != null && ((virbox.Devices != null && virbox.Devices.Count > 0) || (virbox.Features != null && virbox.Features.Count > 0)))
                    return DogProvider.Virbox;
            }
            catch { }
            return DogProvider.Sentinel;
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
