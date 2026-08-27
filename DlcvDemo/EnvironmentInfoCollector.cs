using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DlcvDemo
{
    internal static class EnvironmentInfoCollector
    {
        private const int NvmlSuccess = 0;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true, EntryPoint = "GetModuleHandleW")]
        private static extern IntPtr GetModuleHandleW(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int nvmlInit_v2();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern int nvmlSystemGetDriverVersion(StringBuilder version, uint length);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int nvmlShutdown();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetIntegerVersionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate UIntPtr GetUnsignedVersionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetCudaRuntimeVersionDelegate(out int version);

        internal static string Collect()
        {
            Dictionary<string, LoadedModule> modules = GetLoadedModules();
            var results = new List<ComponentInfo>
            {
                CheckSafely("NVIDIA 驱动", () => CheckNvidiaDriver(modules)),
                CheckSafely("dlcv_infer", () => CheckInferenceLibrary(modules)),
                CheckSafely("ONNX Runtime", () => CheckOnnxRuntime(modules)),
                CheckSafely("TensorRT", () => CheckTensorRt(modules)),
                CheckSafely("CUDA", () => CheckCuda(modules)),
                CheckSafely("cuDNN", () => CheckCudnn(modules)),
                CheckSafely("OpenCV", () => CheckOpenCv(modules)),
                CheckSafely("LibTorch", () => CheckLibTorch(modules))
            };

            var text = new StringBuilder();
            text.AppendLine("类型 | 版本 | 路径");
            for (int i = 0; i < results.Count; i++)
            {
                ComponentInfo result = results[i];
                text.Append(result.Type);
                text.Append(" | ");
                text.Append(string.IsNullOrWhiteSpace(result.Version) ? "版本未知" : result.Version);
                text.Append(" | ");
                text.AppendLine(string.IsNullOrWhiteSpace(result.Path) ? "-" : FormatPathForDisplay(result.Path));
            }
            return text.ToString().TrimEnd();
        }

        private static string FormatPathForDisplay(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string parentDirectory = Path.GetDirectoryName(fullPath);
            string baseDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);

            if (!string.IsNullOrWhiteSpace(parentDirectory)
                && string.Equals(
                    parentDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(fullPath);
            }

            return fullPath;
        }

        private static ComponentInfo CheckSafely(string type, Func<ComponentInfo> check)
        {
            try
            {
                return check() ?? new ComponentInfo(type, "未加载", null);
            }
            catch
            {
                return new ComponentInfo(type, "版本未知", null);
            }
        }

        private static ComponentInfo CheckNvidiaDriver(Dictionary<string, LoadedModule> modules)
        {
            LoadedModule nvmlModule = FindLoadedModule(modules, "nvml.dll");
            bool initialized = false;
            try
            {
                initialized = nvmlInit_v2() == NvmlSuccess;
                if (nvmlModule == null)
                {
                    nvmlModule = FindLoadedModule(GetLoadedModules(), "nvml.dll");
                }
                if (nvmlModule == null)
                {
                    return new ComponentInfo("NVIDIA 驱动", "未加载", null);
                }
                if (!initialized)
                {
                    return CreateLoadedComponent("NVIDIA 驱动", nvmlModule, null);
                }

                var version = new StringBuilder(96);
                int result = nvmlSystemGetDriverVersion(version, (uint)version.Capacity);
                return CreateLoadedComponent(
                    "NVIDIA 驱动",
                    nvmlModule,
                    result == NvmlSuccess ? NormalizeDriverVersion(version.ToString()) : null);
            }
            catch (DllNotFoundException)
            {
                return new ComponentInfo("NVIDIA 驱动", "未加载", null);
            }
            catch
            {
                return CreateLoadedComponent("NVIDIA 驱动", nvmlModule, null);
            }
            finally
            {
                if (initialized)
                {
                    try
                    {
                        nvmlShutdown();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static ComponentInfo CheckInferenceLibrary(Dictionary<string, LoadedModule> modules)
        {
            LoadedModule module = FindLoadedModule(modules, "dlcv_infer.dll", "dlcv_infer_v.dll");
            return CreateLoadedComponent("dlcv_infer", module, ReadProductVersion(module, 4));
        }

        private static ComponentInfo CheckOnnxRuntime(Dictionary<string, LoadedModule> modules)
        {
            LoadedModule module = FindLoadedModule(modules, "dlcv_onnxruntime.dll", "onnxruntime.dll");
            return CreateLoadedComponent("ONNX Runtime", module, ReadProductVersion(module, 3));
        }

        private static ComponentInfo CheckTensorRt(Dictionary<string, LoadedModule> modules)
        {
            LoadedModule module = FindLoadedModule(modules, "nvinfer_10.dll", "nvinfer.dll", "nvinfer_*.dll");
            return CreateLoadedComponent("TensorRT", module, ReadTensorRtVersion(module));
        }

        private static ComponentInfo CheckCuda(Dictionary<string, LoadedModule> modules)
        {
            LoadedModule module = FindLoadedModule(modules, "cudart64_12.dll", "cudart64_*.dll");
            return CreateLoadedComponent("CUDA", module, ReadCudaVersion(module));
        }

        private static ComponentInfo CheckCudnn(Dictionary<string, LoadedModule> modules)
        {
            LoadedModule module = FindLoadedModule(modules, "cudnn64_9.dll", "cudnn64_*.dll");
            return CreateLoadedComponent("cuDNN", module, ReadCudnnVersion(module));
        }

        private static ComponentInfo CheckOpenCv(Dictionary<string, LoadedModule> modules)
        {
            LoadedModule module = FindLoadedModule(modules, "opencv_world4110.dll", "opencv_world*.dll");
            return CreateLoadedComponent("OpenCV", module, ReadProductVersion(module, 3));
        }

        private static ComponentInfo CheckLibTorch(Dictionary<string, LoadedModule> modules)
        {
            LoadedModule module = FindLoadedModule(modules, "torch_cpu.dll");
            return CreateLoadedComponent("LibTorch", module, ReadProductVersion(module, 3));
        }

        private static string NormalizeDriverVersion(string value)
        {
            Match match = Regex.Match(value ?? string.Empty, @"\d+\.\d+(?:\.\d+)?");
            return match.Success ? match.Value : null;
        }

        private static string ReadTensorRtVersion(LoadedModule module)
        {
            int encodedVersion;
            if (!TryInvokeIntegerExport(module, "getInferLibVersion", out encodedVersion) || encodedVersion <= 0)
            {
                return null;
            }

            int major = encodedVersion / 10000;
            int minor = (encodedVersion % 10000) / 100;
            int patch = encodedVersion % 100;
            return major + "." + minor + "." + patch;
        }

        private static string ReadCudaVersion(LoadedModule module)
        {
            int encodedVersion;
            if (!TryInvokeCudaRuntimeVersion(module, out encodedVersion) || encodedVersion <= 0)
            {
                return null;
            }

            int major = encodedVersion / 1000;
            int minor = (encodedVersion % 1000) / 10;
            int patch = encodedVersion % 10;
            return major + "." + minor + "." + patch;
        }

        private static string ReadCudnnVersion(LoadedModule module)
        {
            ulong encodedVersion;
            if (!TryInvokeUnsignedExport(module, "cudnnGetVersion", out encodedVersion) || encodedVersion == 0)
            {
                return null;
            }

            ulong major = encodedVersion / 10000;
            ulong minor = (encodedVersion % 10000) / 100;
            ulong patch = encodedVersion % 100;
            return major + "." + minor + "." + patch;
        }

        private static bool TryInvokeIntegerExport(LoadedModule module, string exportName, out int value)
        {
            value = 0;
            if (module == null || module.BaseAddress == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                IntPtr address = GetProcAddress(module.BaseAddress, exportName);
                if (address == IntPtr.Zero)
                {
                    return false;
                }
                var function = (GetIntegerVersionDelegate)Marshal.GetDelegateForFunctionPointer(address, typeof(GetIntegerVersionDelegate));
                value = function();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeUnsignedExport(LoadedModule module, string exportName, out ulong value)
        {
            value = 0;
            if (module == null || module.BaseAddress == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                IntPtr address = GetProcAddress(module.BaseAddress, exportName);
                if (address == IntPtr.Zero)
                {
                    return false;
                }
                var function = (GetUnsignedVersionDelegate)Marshal.GetDelegateForFunctionPointer(address, typeof(GetUnsignedVersionDelegate));
                value = function().ToUInt64();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeCudaRuntimeVersion(LoadedModule module, out int value)
        {
            value = 0;
            if (module == null || module.BaseAddress == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                IntPtr address = GetProcAddress(module.BaseAddress, "cudaRuntimeGetVersion");
                if (address == IntPtr.Zero)
                {
                    return false;
                }
                var function = (GetCudaRuntimeVersionDelegate)Marshal.GetDelegateForFunctionPointer(address, typeof(GetCudaRuntimeVersionDelegate));
                return function(out value) == 0;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private static Dictionary<string, LoadedModule> GetLoadedModules()
        {
            var modules = new Dictionary<string, LoadedModule>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    foreach (ProcessModule processModule in process.Modules)
                    {
                        try
                        {
                            string moduleName = processModule.ModuleName;
                            if (string.IsNullOrWhiteSpace(moduleName) || modules.ContainsKey(moduleName))
                            {
                                continue;
                            }

                            modules.Add(moduleName, new LoadedModule(
                                processModule.FileName,
                                processModule.BaseAddress));
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
            return modules;
        }

        private static LoadedModule FindLoadedModule(Dictionary<string, LoadedModule> modules, params string[] patterns)
        {
            for (int i = 0; i < patterns.Length; i++)
            {
                string pattern = patterns[i];
                foreach (KeyValuePair<string, LoadedModule> item in modules)
                {
                    if (IsModuleNameMatch(item.Key, pattern)
                        && GetModuleHandleW(item.Key) != IntPtr.Zero)
                    {
                        return item.Value;
                    }
                }
            }
            return null;
        }

        private static bool IsModuleNameMatch(string moduleName, string pattern)
        {
            int wildcardIndex = pattern.IndexOf('*');
            if (wildcardIndex < 0)
            {
                return string.Equals(moduleName, pattern, StringComparison.OrdinalIgnoreCase);
            }

            string prefix = pattern.Substring(0, wildcardIndex);
            string suffix = pattern.Substring(wildcardIndex + 1);
            return moduleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && moduleName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        private static ComponentInfo CreateLoadedComponent(string type, LoadedModule module, string version)
        {
            if (module == null)
            {
                return new ComponentInfo(type, "未加载", null);
            }
            return new ComponentInfo(
                type,
                string.IsNullOrWhiteSpace(version) ? "版本未知" : version,
                module.Path);
        }

        private static string ReadProductVersion(LoadedModule module, int maximumParts)
        {
            return module == null || string.IsNullOrWhiteSpace(module.Path)
                ? null
                : ReadProductVersion(module.Path, maximumParts);
        }

        private static string ReadProductVersion(string path, int maximumParts)
        {
            try
            {
                System.Diagnostics.FileVersionInfo versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                string version = NormalizeVersion(versionInfo.ProductVersion, maximumParts);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }

                version = NormalizeVersion(versionInfo.FileVersion, maximumParts);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }

                version = FormatFixedVersion(
                    versionInfo.ProductMajorPart,
                    versionInfo.ProductMinorPart,
                    versionInfo.ProductBuildPart,
                    versionInfo.ProductPrivatePart,
                    maximumParts);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }

                return FormatFixedVersion(
                    versionInfo.FileMajorPart,
                    versionInfo.FileMinorPart,
                    versionInfo.FileBuildPart,
                    versionInfo.FilePrivatePart,
                    maximumParts);
            }
            catch
            {
                return null;
            }
        }

        private static string FormatFixedVersion(int major, int minor, int build, int privatePart, int maximumParts)
        {
            if (major < 0 || minor < 0 || build < 0 || privatePart < 0)
            {
                return null;
            }

            string[] parts = { major.ToString(), minor.ToString(), build.ToString(), privatePart.ToString() };
            int count = Math.Min(parts.Length, maximumParts);
            return count <= 0 ? null : string.Join(".", parts, 0, count);
        }

        private static string NormalizeVersion(string value, int maximumParts)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            Match match = Regex.Match(value, @"\d+(?:\.\d+){1,4}");
            if (!match.Success)
            {
                return null;
            }
            string[] parts = match.Value.Split('.');
            int count = Math.Min(parts.Length, maximumParts);
            return string.Join(".", parts, 0, count);
        }

        private sealed class ComponentInfo
        {
            internal ComponentInfo(string type, string version, string path)
            {
                Type = type;
                Version = version;
                Path = string.IsNullOrWhiteSpace(path) ? null : System.IO.Path.GetFullPath(path);
            }

            internal string Type { get; private set; }
            internal string Version { get; private set; }
            internal string Path { get; private set; }
        }

        private sealed class LoadedModule
        {
            internal LoadedModule(string path, IntPtr baseAddress)
            {
                Path = path;
                BaseAddress = baseAddress;
            }

            internal string Path { get; private set; }
            internal IntPtr BaseAddress { get; private set; }
        }
    }
}
