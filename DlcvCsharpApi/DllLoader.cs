using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;
using sntl_admin_csharp;
using System.Linq;

namespace dlcv_infer_csharp
{
    public class DllLoader
    {
        private string DllName;
        private string DllPath;
        private const CallingConvention calling_method = CallingConvention.StdCall;

        // 定义导入方法的委托
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

        // 追踪所有已创建的 loader
        private static readonly List<DllLoader> _allLoaders = new List<DllLoader>();
        private static readonly object _loaderLock = new object();

        public DogProvider LoadedDogProvider { get; private set; }
        public string LoadedNativeDllName { get; private set; }

        // 为兼容旧代码保留 Instance：返回第一个已创建的 loader，若没有则创建一个默认 Sentinel
        private static DllLoader _legacyInstance;
        public static DllLoader Instance
        {
            get
            {
                if (_legacyInstance == null)
                {
                    lock (_loaderLock)
                    {
                        if (_legacyInstance == null)
                        {
                            if (_allLoaders.Count > 0)
                            {
                                _legacyInstance = _allLoaders[0];
                            }
                            else
                            {
                                _legacyInstance = ForProvider(AutoDetectProvider());
                            }
                        }
                    }
                }
                return _legacyInstance;
            }
        }

        public static IReadOnlyList<DllLoader> GetAllLoaders()
        {
            lock (_loaderLock)
            {
                return new List<DllLoader>(_allLoaders);
            }
        }

        private DllLoader(DogProvider provider)
        {
            LoadedDogProvider = provider;
            switch (provider)
            {
                case DogProvider.Sentinel:
                    DllName = "dlcv_infer.dll";
                    DllPath = @"C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll";
                    break;
                case DogProvider.Virbox:
                    DllName = "dlcv_infer_v.dll";
                    DllPath = @"C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer_v.dll";
                    break;
                default:
                    throw new ArgumentException("不支持的 dog provider: " + provider);
            }
            LoadedNativeDllName = DllName;
            LoadDll();
            lock (_loaderLock)
            {
                _allLoaders.Add(this);
            }
        }

        public static DllLoader ForProvider(DogProvider provider)
        {
            return new DllLoader(provider);
        }

        public static DllLoader ForModel(string modelPath)
        {
            DogProvider provider;
            if (!ModelHeaderProviderResolver.TryResolveExplicitProvider(modelPath, out provider))
            {
                // 模型未明确指定 provider，按 Sentinel 优先、Virbox 第二自动检测当前加密狗
                provider = AutoDetectProvider();
            }
            else
            {
                // 模型明确指定了 provider，验证对应加密狗是否存在
                DogInfo dogInfo = provider == DogProvider.Sentinel ? DogUtils.GetSentinelInfo() : DogUtils.GetVirboxInfo();
                if (dogInfo == null || ((dogInfo.Devices == null || dogInfo.Devices.Count == 0) && (dogInfo.Features == null || dogInfo.Features.Count == 0)))
                {
                    throw new Exception($"模型要求 provider {provider}，但未检测到对应的加密狗设备或特性");
                }
            }
            return ForProvider(provider);
        }

        /// <summary>
        /// 自动检测当前插入的加密狗，按 Sentinel 优先、Virbox 第二返回 Provider。
        /// 若均未检测到，默认返回 Sentinel。
        /// </summary>
        private static DogProvider AutoDetectProvider()
        {
            try
            {
                var sentinel = DogUtils.GetSentinelInfo();
                if (sentinel != null && ((sentinel.Devices != null && sentinel.Devices.Count > 0) || (sentinel.Features != null && sentinel.Features.Count > 0)))
                {
                    return DogProvider.Sentinel;
                }
            }
            catch { }

            try
            {
                var virbox = DogUtils.GetVirboxInfo();
                if (virbox != null && ((virbox.Devices != null && virbox.Devices.Count > 0) || (virbox.Features != null && virbox.Features.Count > 0)))
                {
                    return DogProvider.Virbox;
                }
            }
            catch { }

            return DogProvider.Sentinel;
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
                // 如果当前目录下的 DLL 加载失败，尝试加载指定路径的 DLL
                hModule = LoadLibrary(DllPath);
                if (hModule == IntPtr.Zero)
                {
                    throw new Exception("无法加载 DLL");
                }
            }

            // 获取函数指针
            dlcv_load_model = GetDelegate<LoadModelDelegate>(hModule, "dlcv_load_model");
            dlcv_free_model = GetDelegate<FreeModelDelegate>(hModule, "dlcv_free_model");
            dlcv_get_model_info = GetDelegate<GetModelInfoDelegate>(hModule, "dlcv_get_model_info");
            dlcv_infer = GetDelegate<InferDelegate>(hModule, "dlcv_infer");
            dlcv_free_model_result = GetDelegate<FreeModelResultDelegate>(hModule, "dlcv_free_model_result");
            dlcv_free_result = GetDelegate<FreeResultDelegate>(hModule, "dlcv_free_result");
            dlcv_free_all_models = GetDelegate<FreeAllModelsDelegate>(hModule, "dlcv_free_all_models");
            IntPtr gpuInfoPtr = GetProcAddress(hModule, "dlcv_get_gpu_info");
            if (gpuInfoPtr != IntPtr.Zero)
            {
                dlcv_get_gpu_info = (GetGpuInfo)Marshal.GetDelegateForFunctionPointer(gpuInfoPtr, typeof(GetGpuInfo));
            }
            else
            {
                dlcv_get_gpu_info = null;
            }

            IntPtr devInfoPtr = GetProcAddress(hModule, "dlcv_get_device_info");
            if (devInfoPtr != IntPtr.Zero)
            {
                dlcv_get_device_info = (GetDeviceInfo)Marshal.GetDelegateForFunctionPointer(devInfoPtr, typeof(GetDeviceInfo));
            }
            else
            {
                dlcv_get_device_info = null;
            }
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
            if (result == 0 || result >= (uint)buffer.Capacity)
            {
                return null;
            }

            return buffer.ToString();
        }

        private T GetDelegate<T>(IntPtr hModule, string procedureName) where T : Delegate
        {
            IntPtr pAddressOfFunctionToCall = GetProcAddress(hModule, procedureName);
            if (pAddressOfFunctionToCall == IntPtr.Zero)
            {
                return null;
            }
            return (T)Marshal.GetDelegateForFunctionPointer(pAddressOfFunctionToCall, typeof(T));
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
