using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DlcvDemo
{
    internal static class EnvironmentInfoCollector
    {
        private const uint LoadWithAlteredSearchPath = 0x00000008;
        private const int NvmlSuccess = 0;
        private static readonly object ComponentLibraryLock = new object();
        private static readonly Dictionary<string, IntPtr> ComponentLibraries =
            new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);

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
            var results = new List<ComponentInfo>
            {
                CheckSafely("NVIDIA 驱动", CheckNvidiaDriver),
                CheckSafely("dlcv_infer", CheckInferenceLibrary),
                CheckSafely("ONNX Runtime", CheckOnnxRuntime),
                CheckSafely("TensorRT", CheckTensorRt),
                CheckSafely("CUDA", CheckCuda),
                CheckSafely("cuDNN", CheckCudnn),
                CheckSafely("OpenCV", CheckOpenCv),
                CheckSafely("LibTorch", CheckLibTorch)
            };

            var text = new StringBuilder();
            text.AppendLine("类型 | 版本 | 路径");
            for (int i = 0; i < results.Count; i++)
            {
                ComponentInfo result = results[i];
                text.Append(result.Type);
                text.Append(" | ");
                text.Append(string.IsNullOrWhiteSpace(result.Version) ? "未检测到" : result.Version);
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
                return check() ?? new ComponentInfo(type, null, null);
            }
            catch
            {
                return new ComponentInfo(type, null, null);
            }
        }

        private static ComponentInfo CheckNvidiaDriver()
        {
            string nvmlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
            if (!File.Exists(nvmlPath))
            {
                return new ComponentInfo("NVIDIA 驱动", null, null);
            }

            bool initialized = false;
            try
            {
                initialized = nvmlInit_v2() == NvmlSuccess;
                if (!initialized)
                {
                    return new ComponentInfo("NVIDIA 驱动", null, nvmlPath);
                }

                var version = new StringBuilder(96);
                int result = nvmlSystemGetDriverVersion(version, (uint)version.Capacity);
                return new ComponentInfo(
                    "NVIDIA 驱动",
                    result == NvmlSuccess ? NormalizeDriverVersion(version.ToString()) : null,
                    nvmlPath);
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

        private static ComponentInfo CheckInferenceLibrary()
        {
            string path = FindApplicationFile("dlcv_infer.dll", "dlcv_infer_v.dll");
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ComponentInfo("dlcv_infer", null, null);
            }

            return new ComponentInfo("dlcv_infer", ReadProductVersion(path, 4), path);
        }

        private static ComponentInfo CheckOnnxRuntime()
        {
            string path = FindApplicationFile("dlcv_onnxruntime.dll", "onnxruntime.dll");
            return string.IsNullOrWhiteSpace(path)
                ? new ComponentInfo("ONNX Runtime", null, null)
                : new ComponentInfo("ONNX Runtime", ReadProductVersion(path, 3), path);
        }

        private static ComponentInfo CheckTensorRt()
        {
            string path = FindApplicationFile("nvinfer_10.dll", "nvinfer.dll", "nvinfer_*.dll");
            return string.IsNullOrWhiteSpace(path)
                ? new ComponentInfo("TensorRT", null, null)
                : new ComponentInfo("TensorRT", ReadTensorRtVersion(path), path);
        }

        private static ComponentInfo CheckCuda()
        {
            string path = FindApplicationFile("cudart64_12.dll", "cudart64_*.dll");
            return string.IsNullOrWhiteSpace(path)
                ? new ComponentInfo("CUDA", null, null)
                : new ComponentInfo("CUDA", ReadCudaVersion(path), path);
        }

        private static ComponentInfo CheckCudnn()
        {
            string path = FindApplicationFile("cudnn64_9.dll", "cudnn64_*.dll");
            return string.IsNullOrWhiteSpace(path)
                ? new ComponentInfo("cuDNN", null, null)
                : new ComponentInfo("cuDNN", ReadCudnnVersion(path), path);
        }

        private static ComponentInfo CheckOpenCv()
        {
            string path = FindApplicationFile("opencv_world4110.dll", "opencv_world*.dll");
            return string.IsNullOrWhiteSpace(path)
                ? new ComponentInfo("OpenCV", null, null)
                : new ComponentInfo("OpenCV", ReadProductVersion(path, 3), path);
        }

        private static ComponentInfo CheckLibTorch()
        {
            string path = FindApplicationFile("torch_cpu.dll");
            return string.IsNullOrWhiteSpace(path)
                ? new ComponentInfo("LibTorch", null, null)
                : new ComponentInfo("LibTorch", ReadLibTorchVersion(path), path);
        }

        private static string NormalizeDriverVersion(string value)
        {
            Match match = Regex.Match(value ?? string.Empty, @"\d+\.\d+(?:\.\d+)?");
            return match.Success ? match.Value : null;
        }

        private static string ReadTensorRtVersion(string libraryPath)
        {
            int encodedVersion;
            if (!TryInvokeIntegerExport(libraryPath, "getInferLibVersion", out encodedVersion) || encodedVersion <= 0)
            {
                return null;
            }

            int major = encodedVersion / 10000;
            int minor = (encodedVersion % 10000) / 100;
            int patch = encodedVersion % 100;
            return major + "." + minor + "." + patch;
        }

        private static string ReadCudaVersion(string libraryPath)
        {
            int encodedVersion;
            if (!TryInvokeCudaRuntimeVersion(libraryPath, out encodedVersion) || encodedVersion <= 0)
            {
                return null;
            }

            int major = encodedVersion / 1000;
            int minor = (encodedVersion % 1000) / 10;
            int patch = encodedVersion % 10;
            return major + "." + minor + "." + patch;
        }

        private static string ReadCudnnVersion(string libraryPath)
        {
            ulong encodedVersion;
            if (!TryInvokeUnsignedExport(libraryPath, "cudnnGetVersion", out encodedVersion) || encodedVersion == 0)
            {
                return null;
            }

            ulong major = encodedVersion / 10000;
            ulong minor = (encodedVersion % 10000) / 100;
            ulong patch = encodedVersion % 100;
            return major + "." + minor + "." + patch;
        }

        private static bool TryInvokeIntegerExport(string libraryPath, string exportName, out int value)
        {
            value = 0;
            IntPtr module = LoadComponentLibrary(libraryPath);
            if (module == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                IntPtr address = GetProcAddress(module, exportName);
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

        private static bool TryInvokeUnsignedExport(string libraryPath, string exportName, out ulong value)
        {
            value = 0;
            IntPtr module = LoadComponentLibrary(libraryPath);
            if (module == IntPtr.Zero)
            {
                return false;
            }

            LoadCudnnSupportingLibraries(libraryPath);
            try
            {
                IntPtr address = GetProcAddress(module, exportName);
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

        private static bool TryInvokeCudaRuntimeVersion(string libraryPath, out int value)
        {
            value = 0;
            IntPtr module = LoadComponentLibrary(libraryPath);
            if (module == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                IntPtr address = GetProcAddress(module, "cudaRuntimeGetVersion");
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

        private static IntPtr LoadComponentLibrary(string libraryPath)
        {
            string fullPath = Path.GetFullPath(libraryPath);
            lock (ComponentLibraryLock)
            {
                IntPtr module;
                if (ComponentLibraries.TryGetValue(fullPath, out module))
                {
                    return module;
                }

                module = LoadLibraryEx(fullPath, IntPtr.Zero, LoadWithAlteredSearchPath);
                if (module != IntPtr.Zero)
                {
                    // 推理过程继续使用 GPU 运行库，进程退出时由系统统一释放。
                    ComponentLibraries[fullPath] = module;
                }
                return module;
            }
        }

        private static List<IntPtr> LoadCudnnSupportingLibraries(string coreLibraryPath)
        {
            var handles = new List<IntPtr>();
            try
            {
                string directory = Path.GetDirectoryName(coreLibraryPath);
                string[] names =
                {
                    "cudnn_ops64_9.dll",
                    "cudnn_graph64_9.dll",
                    "cudnn_heuristic64_9.dll",
                    "cudnn_engines_precompiled64_9.dll",
                    "cudnn_engines_runtime_compiled64_9.dll",
                    "cudnn_cnn64_9.dll",
                    "cudnn_adv64_9.dll"
                };
                for (int i = 0; i < names.Length; i++)
                {
                    string path = Path.Combine(directory, names[i]);
                    if (!File.Exists(path))
                    {
                        continue;
                    }
                    IntPtr handle = LoadComponentLibrary(path);
                    if (handle != IntPtr.Zero)
                    {
                        handles.Add(handle);
                    }
                }
            }
            catch
            {
            }
            return handles;
        }

        private static string ReadLibTorchVersion(string libraryPath)
        {
            string localVersion = ReadLibTorchVersionFromRoot(AppDomain.CurrentDomain.BaseDirectory);
            if (!string.IsNullOrWhiteSpace(localVersion))
            {
                return localVersion;
            }

            const string knownRoot = @"C:\pytorch\torch";
            string knownLibrary = Path.Combine(knownRoot, "lib", "torch_cpu.dll");
            return FilesHaveSameSha256(libraryPath, knownLibrary)
                ? ReadLibTorchVersionFromRoot(knownRoot)
                : null;
        }

        private static string ReadLibTorchVersionFromRoot(string root)
        {
            string version = ReadVersionText(Path.Combine(root, "build-version"));
            if (!string.IsNullOrWhiteSpace(version))
            {
                return NormalizeVersion(version, 3);
            }

            string cmakePath = Path.Combine(root, "share", "cmake", "Torch", "TorchConfigVersion.cmake");
            if (File.Exists(cmakePath))
            {
                Match match = Regex.Match(File.ReadAllText(cmakePath), "PACKAGE_VERSION\\s+\\\"([^\\\"]+)\\\"");
                if (match.Success)
                {
                    return NormalizeVersion(match.Groups[1].Value, 3);
                }
            }

            string[] headers =
            {
                Path.Combine(root, "include", "torch", "version.h"),
                Path.Combine(root, "include", "torch", "csrc", "api", "include", "torch", "version.h")
            };
            for (int i = 0; i < headers.Length; i++)
            {
                version = ReadMacroVersion(headers[i], "TORCH_VERSION_MAJOR", "TORCH_VERSION_MINOR", "TORCH_VERSION_PATCH");
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }
            }
            return null;
        }

        private static bool FilesHaveSameSha256(string firstPath, string secondPath)
        {
            try
            {
                if (!File.Exists(firstPath) || !File.Exists(secondPath))
                {
                    return false;
                }
                byte[] firstHash = ComputeSha256(firstPath);
                byte[] secondHash = ComputeSha256(secondPath);
                if (firstHash.Length != secondHash.Length)
                {
                    return false;
                }
                for (int i = 0; i < firstHash.Length; i++)
                {
                    if (firstHash[i] != secondHash[i])
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] ComputeSha256(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return algorithm.ComputeHash(stream);
            }
        }

        private static string FindApplicationFile(params string[] patterns)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < patterns.Length; i++)
            {
                try
                {
                    string[] files = Directory.GetFiles(directory, patterns[i], SearchOption.TopDirectoryOnly);
                    if (files.Length > 0)
                    {
                        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                        return Path.GetFullPath(files[0]);
                    }
                }
                catch
                {
                }
            }
            return null;
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

        private static string ReadVersionText(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                Match match = Regex.Match(File.ReadAllText(path), @"\d+(?:\.\d+){1,4}(?:a\d+)?", RegexOptions.IgnoreCase);
                return match.Success ? match.Value : null;
            }
            catch
            {
                return null;
            }
        }

        private static string ReadMacroVersion(string path, string majorName, string minorName, string patchName)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                string text = File.ReadAllText(path);
                string major = ReadMacro(text, majorName);
                string minor = ReadMacro(text, minorName);
                string patch = ReadMacro(text, patchName);
                return major == null || minor == null || patch == null ? null : major + "." + minor + "." + patch;
            }
            catch
            {
                return null;
            }
        }

        private static string ReadMacro(string text, string name)
        {
            Match match = Regex.Match(text, @"#\s*define\s+" + Regex.Escape(name) + @"\s+(\d+)");
            return match.Success ? match.Groups[1].Value : null;
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
    }
}
