using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DlcvDemo
{
    internal static class EnvironmentInfoCollector
    {
        private const int OperationTimeoutMilliseconds = 3000;

        private const uint LoadLibrarySearchDllLoadDirectory = 0x00000100;
        private const uint LoadLibrarySearchDefaultDirectories = 0x00001000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetIntegerVersionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate UIntPtr GetUnsignedVersionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetCudaRuntimeVersionDelegate(out int version);

        internal static string Collect()
        {
            ComponentInfo inferenceLibrary = CheckSafely("推理主库", CheckInferenceLibrary);
            var context = new ProbeContext(inferenceLibrary.Path);
            var results = new List<ComponentInfo>
            {
                CheckSafely("NVIDIA 驱动", CheckNvidiaDriver),
                inferenceLibrary,
                CheckSafely("ONNX Runtime", () => CheckOnnxRuntime(context)),
                CheckSafely("TensorRT", () => CheckTensorRt(context)),
                CheckSafely("CUDA", () => CheckCuda(context)),
                CheckSafely("cuDNN", () => CheckCudnn(context)),
                CheckSafely("OpenCV", () => CheckOpenCv(context)),
                CheckSafely("LibTorch", () => CheckLibTorch(context))
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
                text.AppendLine(string.IsNullOrWhiteSpace(result.Path) ? "-" : result.Path);
            }
            return text.ToString().TrimEnd();
        }

        internal static int RunVersionHelper(string[] args)
        {
            if (args == null || args.Length != 3 || !File.Exists(args[2]))
            {
                return 2;
            }
            try
            {
                if (string.Equals(args[1], "tensorrt", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(InvokeIntegerExport(args[2], "getInferLibVersion"));
                    return 0;
                }
                if (string.Equals(args[1], "cuda", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(InvokeCudaRuntimeVersion(args[2]));
                    return 0;
                }
                if (string.Equals(args[1], "cudnn", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(InvokeUnsignedExport(args[2], "cudnnGetVersion"));
                    return 0;
                }
            }
            catch
            {
                return 1;
            }
            return 2;
        }

        private static ComponentInfo CheckSafely(string type, Func<ComponentInfo> check)
        {
            try
            {
                ComponentInfo result = check();
                return result ?? new ComponentInfo(type, null, null);
            }
            catch
            {
                return new ComponentInfo(type, null, null);
            }
        }

        private static ComponentInfo CheckNvidiaDriver()
        {
            string path = FindExecutable(
                "nvidia-smi.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"));
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ComponentInfo("NVIDIA 驱动", null, null);
            }

            string output = RunCommand(path, "--query-gpu=driver_version --format=csv,noheader");
            Match match = Regex.Match(output ?? string.Empty, @"\b\d+\.\d+(?:\.\d+)?\b");
            return new ComponentInfo("NVIDIA 驱动", match.Success ? match.Value : null, path);
        }

        private static ComponentInfo CheckInferenceLibrary()
        {
            string path = FindComponentFile(
                new[] { "dlcv_infer.dll", "dlcv_infer_v.dll" },
                new[] { "DLCV_INFER_PATH", "DLCV_INFER_ROOT" },
                new[]
                {
                    @"C:\dlcv\Lib\site-packages\dlcvpro_infer",
                    @"C:\dlcv\Lib\site-packages\dlcvpro_infer_csharp"
                },
                new[] { string.Empty, "bin" });
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ComponentInfo("推理主库", null, null);
            }

            string version = ReadVersionText(Path.Combine(Path.GetDirectoryName(path), "version.txt"));
            if (string.IsNullOrWhiteSpace(version))
            {
                version = ReadPackageVersion(path, new[] { "dlcvpro_infer" });
            }
            if (string.IsNullOrWhiteSpace(version))
            {
                version = ReadProductVersion(path);
            }
            return new ComponentInfo("推理主库", version, path);
        }

        private static ComponentInfo CheckOnnxRuntime(ProbeContext context)
        {
            string path = FindComponentFile(
                new[] { "dlcv_onnxruntime.dll", "onnxruntime.dll" },
                new string[0],
                context.GetKnownDirectories(@"C:\dlcv\Lib\site-packages\onnxruntime\capi"),
                new[] { string.Empty, "capi", "bin", "lib", @"onnxruntime\capi" });
            if (string.IsNullOrWhiteSpace(path))
            {
                path = FindComponentFile(
                    new[] { "dlcv_onnxruntime.dll", "onnxruntime.dll" },
                    new[] { "ONNXRUNTIME_ROOT", "ONNXRUNTIME_HOME", "ONNXRUNTIME_DIR" },
                    new[] { @"C:\onnxruntime" },
                    new[] { string.Empty, "capi", "bin", "lib", @"onnxruntime\capi" });
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ComponentInfo("ONNX Runtime", null, null);
            }

            string version = ReadProductVersion(path);
            return new ComponentInfo("ONNX Runtime", NormalizeVersion(version, 3), path);
        }

        private static ComponentInfo CheckTensorRt(ProbeContext context)
        {
            string path = FindComponentFile(
                new[] { "nvinfer_10.dll", "nvinfer.dll", "nvinfer_*.dll" },
                new[] { "TENSORRT_ROOT", "TENSORRT_HOME", "TENSORRT_DIR" },
                context.GetKnownDirectories(@"C:\dlcv\bin", @"C:\TensorRT"),
                new[] { string.Empty, "bin", "lib" });
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ComponentInfo("TensorRT", null, null);
            }

            string version = ReadTensorRtVersionFromLibrary(path);
            if (string.IsNullOrWhiteSpace(version))
            {
                version = ReadTensorRtVersionFromHeader(path);
            }
            if (string.IsNullOrWhiteSpace(version))
            {
                string executable = Path.Combine(Path.GetDirectoryName(path), "trtexec.exe");
                if (File.Exists(executable))
                {
                    Match match = Regex.Match(RunCommand(executable, "--version") ?? string.Empty, @"TensorRT\s+v(\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
                    version = match.Success ? match.Groups[1].Value : null;
                }
            }
            return new ComponentInfo("TensorRT", version, path);
        }

        private static ComponentInfo CheckCuda(ProbeContext context)
        {
            string path = FindComponentFile(
                new[] { "cudart64_12.dll", "cudart64_*.dll" },
                new[] { "CUDA_PATH", "CUDA_PATH_V12_8", "CUDA_PATH_V12_3" },
                context.GetKnownDirectories(
                    @"C:\dlcv\Lib\site-packages\torch\lib",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA GPU Computing Toolkit", "CUDA", "v12.8", "bin"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA GPU Computing Toolkit", "CUDA", "v12.3", "bin")),
                new[] { string.Empty, "bin", "lib", @"lib\x64" });
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ComponentInfo("CUDA", null, null);
            }

            string version = ReadCudaVersionFromLibrary(path);
            if (string.IsNullOrWhiteSpace(version))
            {
                version = ReadCudaToolkitVersion(path);
            }
            return new ComponentInfo("CUDA", version, path);
        }

        private static ComponentInfo CheckCudnn(ProbeContext context)
        {
            string path = FindComponentFile(
                new[] { "cudnn64_9.dll", "cudnn*.dll" },
                new[] { "CUDNN_PATH", "CUDNN_ROOT", "CUDA_PATH" },
                context.GetKnownDirectories(
                    @"C:\dlcv\Lib\site-packages\torch\lib",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA", "CUDNN")),
                new[] { string.Empty, "bin", "lib", @"lib\x64", @"bin\12.8", @"bin\12.3" });
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ComponentInfo("cuDNN", null, null);
            }

            return new ComponentInfo("cuDNN", ReadCudnnVersionFromLibrary(path), path);
        }

        private static ComponentInfo CheckOpenCv(ProbeContext context)
        {
            string path = FindComponentFile(
                new[] { "opencv_world4110.dll", "opencv_world*.dll", "cv2.pyd" },
                new[] { "OPENCV_DIR", "OPENCV_ROOT" },
                context.GetKnownDirectories(@"C:\dlcv\Lib\site-packages\cv2", @"C:\dlcv\bin"),
                new[] { string.Empty, "bin", @"x64\vc17\bin", @"x64\vc16\bin", "cv2" });
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ComponentInfo("OpenCV", null, null);
            }

            string version = ReadProductVersion(path);
            return new ComponentInfo("OpenCV", NormalizeVersion(version, 3), path);
        }

        private static ComponentInfo CheckLibTorch(ProbeContext context)
        {
            string path = FindComponentFile(
                new[] { "torch_cpu.dll" },
                new[] { "LIBTORCH_DIR", "LIBTORCH_ROOT" },
                context.GetKnownDirectories(@"C:\dlcv\Lib\site-packages\torch\lib", @"C:\libtorch\lib"),
                new[] { string.Empty, "lib", "bin", "libtorch", @"libtorch\lib", @"libtorch\bin" });
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ComponentInfo("LibTorch", null, null);
            }

            return new ComponentInfo("LibTorch", ReadLibTorchVersion(path), path);
        }

        private static string ReadTensorRtVersionFromLibrary(string path)
        {
            int code;
            if (!TryInvokeIntegerExport(path, "getInferLibVersion", out code) || code <= 0)
            {
                return null;
            }

            int major = code / 10000;
            int minor = (code % 10000) / 100;
            int patch = code % 100;
            return major + "." + minor + "." + patch;
        }

        private static string ReadTensorRtVersionFromHeader(string libraryPath)
        {
            string directory = Path.GetDirectoryName(libraryPath);
            string[] candidates =
            {
                Path.Combine(directory, "NvInferVersion.h"),
                Path.Combine(directory, "include", "NvInferVersion.h"),
                Path.Combine(directory, "..", "include", "NvInferVersion.h")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                string version = ReadMacroVersion(candidates[i], "NV_TENSORRT_MAJOR", "NV_TENSORRT_MINOR", "NV_TENSORRT_PATCH");
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }
            }
            return null;
        }

        private static string ReadCudaVersionFromLibrary(string path)
        {
            int encodedVersion;
            if (!TryInvokeCudaRuntimeVersion(path, out encodedVersion) || encodedVersion <= 0)
            {
                return null;
            }

            int major = encodedVersion / 1000;
            int minor = (encodedVersion % 1000) / 10;
            int patch = encodedVersion % 10;
            return major + "." + minor + "." + patch;
        }

        private static string ReadCudaToolkitVersion(string libraryPath)
        {
            string directory = Path.GetDirectoryName(libraryPath);
            string root = string.Equals(Path.GetFileName(directory), "bin", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(directory)
                : directory;
            string[] versionFiles =
            {
                Path.Combine(root, "version.json"),
                Path.Combine(root, "version.txt")
            };
            for (int i = 0; i < versionFiles.Length; i++)
            {
                string version = ReadVersionText(versionFiles[i]);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return NormalizeVersion(version, 3);
                }
            }

            string nvccPath = Path.Combine(root, "bin", "nvcc.exe");
            if (File.Exists(nvccPath))
            {
                Match match = Regex.Match(RunCommand(nvccPath, "--version") ?? string.Empty, @"release\s+(\d+\.\d+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value + ".0";
                }
            }
            return null;
        }

        private static string ReadCudnnVersionFromLibrary(string path)
        {
            ulong encodedVersion;
            if (!TryInvokeUnsignedExport(path, "cudnnGetVersion", out encodedVersion) || encodedVersion == 0)
            {
                return null;
            }

            ulong major = encodedVersion / 10000;
            ulong minor = (encodedVersion % 10000) / 100;
            ulong patch = encodedVersion % 100;
            return major + "." + minor + "." + patch;
        }

        private static string ReadLibTorchVersion(string libraryPath)
        {
            string libraryDirectory = Path.GetDirectoryName(libraryPath);
            string root = string.Equals(Path.GetFileName(libraryDirectory), "lib", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(libraryDirectory), "bin", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(libraryDirectory)
                : libraryDirectory;
            string version = ReadLibTorchVersionFromRoot(root);
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }

            string[] candidateRoots =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libtorch"),
                @"C:\pytorch\torch",
            };
            for (int i = 0; i < candidateRoots.Length; i++)
            {
                string candidateRoot = candidateRoots[i];
                if (string.IsNullOrWhiteSpace(candidateRoot))
                {
                    continue;
                }
                string[] candidateLibraries =
                {
                    Path.Combine(candidateRoot, "lib", "torch_cpu.dll"),
                    Path.Combine(candidateRoot, "bin", "torch_cpu.dll"),
                    Path.Combine(candidateRoot, "torch_cpu.dll")
                };
                for (int j = 0; j < candidateLibraries.Length; j++)
                {
                    if (!FilesHaveSameSha256(libraryPath, candidateLibraries[j]))
                    {
                        continue;
                    }
                    version = ReadLibTorchVersionFromRoot(candidateRoot);
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        return version;
                    }
                }
            }
            return null;
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

        private static string NormalizeLibTorchRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            string fullPath = NormalizeDirectory(path.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return null;
            }
            string name = Path.GetFileName(fullPath);
            return string.Equals(name, "lib", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(fullPath)
                : fullPath;
        }

        private static bool FilesHaveSameSha256(string firstPath, string secondPath)
        {
            try
            {
                if (!File.Exists(firstPath) || !File.Exists(secondPath))
                {
                    return false;
                }
                using (SHA256 algorithm = SHA256.Create())
                using (FileStream firstStream = File.Open(firstPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] firstHash = algorithm.ComputeHash(firstStream);
                    using (FileStream secondStream = File.Open(secondPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        byte[] secondHash = algorithm.ComputeHash(secondStream);
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
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeIntegerExport(string libraryPath, string exportName, out int value)
        {
            value = 0;
            string output = RunVersionHelperCommand("tensorrt", libraryPath);
            Match match = Regex.Match(output ?? string.Empty, @"(?m)^\s*(\d+)\s*$");
            return match.Success && int.TryParse(match.Groups[1].Value, out value);
        }

        private static int InvokeIntegerExport(string libraryPath, string exportName)
        {
            IntPtr module = LoadComponentLibrary(libraryPath);
            if (module == IntPtr.Zero)
            {
                return 0;
            }
            try
            {
                IntPtr address = GetProcAddress(module, exportName);
                if (address == IntPtr.Zero)
                {
                    return 0;
                }
                var function = (GetIntegerVersionDelegate)Marshal.GetDelegateForFunctionPointer(address, typeof(GetIntegerVersionDelegate));
                return function();
            }
            finally
            {
                FreeLibrary(module);
            }
        }

        private static bool TryInvokeUnsignedExport(string libraryPath, string exportName, out ulong value)
        {
            value = 0;
            string output = RunVersionHelperCommand("cudnn", libraryPath);
            Match match = Regex.Match(output ?? string.Empty, @"(?m)^\s*(\d+)\s*$");
            return match.Success && ulong.TryParse(match.Groups[1].Value, out value);
        }

        private static ulong InvokeUnsignedExport(string libraryPath, string exportName)
        {
            IntPtr module = LoadComponentLibrary(libraryPath);
            if (module == IntPtr.Zero)
            {
                return 0;
            }
            try
            {
                IntPtr address = GetProcAddress(module, exportName);
                if (address == IntPtr.Zero)
                {
                    return 0;
                }
                var function = (GetUnsignedVersionDelegate)Marshal.GetDelegateForFunctionPointer(address, typeof(GetUnsignedVersionDelegate));
                return function().ToUInt64();
            }
            finally
            {
                FreeLibrary(module);
            }
        }

        private static bool TryInvokeCudaRuntimeVersion(string libraryPath, out int value)
        {
            value = 0;
            string output = RunVersionHelperCommand("cuda", libraryPath);
            Match match = Regex.Match(output ?? string.Empty, @"(?m)^\s*(\d+)\s*$");
            return match.Success && int.TryParse(match.Groups[1].Value, out value);
        }

        private static int InvokeCudaRuntimeVersion(string libraryPath)
        {
            IntPtr module = LoadComponentLibrary(libraryPath);
            if (module == IntPtr.Zero)
            {
                return 0;
            }
            try
            {
                IntPtr address = GetProcAddress(module, "cudaRuntimeGetVersion");
                if (address == IntPtr.Zero)
                {
                    return 0;
                }
                var function = (GetCudaRuntimeVersionDelegate)Marshal.GetDelegateForFunctionPointer(address, typeof(GetCudaRuntimeVersionDelegate));
                int version;
                return function(out version) == 0 ? version : 0;
            }
            finally
            {
                FreeLibrary(module);
            }
        }

        private static IntPtr LoadComponentLibrary(string libraryPath)
        {
            return LoadLibraryEx(
                libraryPath,
                IntPtr.Zero,
                LoadLibrarySearchDllLoadDirectory | LoadLibrarySearchDefaultDirectories);
        }

        private static string RunVersionHelperCommand(string kind, string libraryPath)
        {
            string executable = typeof(EnvironmentInfoCollector).Assembly.Location;
            string arguments = "environment-version " + QuoteArgument(kind) + " " + QuoteArgument(libraryPath);
            return RunCommand(executable, arguments, Path.GetDirectoryName(libraryPath));
        }

        private static string RunCommand(string executable, string arguments)
        {
            return RunCommand(executable, arguments, null);
        }

        private static string RunCommand(string executable, string arguments, string additionalPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(executable) ?? AppDomain.CurrentDomain.BaseDirectory
                };
                if (!string.IsNullOrWhiteSpace(additionalPath))
                {
                    string currentPath = startInfo.EnvironmentVariables["PATH"] ?? string.Empty;
                    startInfo.EnvironmentVariables["PATH"] = additionalPath + ";" + currentPath;
                }
                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start())
                    {
                        return null;
                    }
                    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                    Task<string> standardError = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(OperationTimeoutMilliseconds))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                        return null;
                    }
                    Task.WaitAll(new Task[] { standardOutput, standardError }, 500);
                    string output = standardOutput.IsCompleted ? standardOutput.Result : string.Empty;
                    string error = standardError.IsCompleted ? standardError.Result : string.Empty;
                    return output + Environment.NewLine + error;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string FindComponentFile(string[] patterns, string[] environmentNames, string[] knownDirectories, string[] relativeDirectories)
        {
            string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return FindFileInDirectories(new[] { applicationDirectory }, patterns);
        }

        private static string FindLoadedModule(string[] patterns)
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        for (int i = 0; i < patterns.Length; i++)
                        {
                            if (WildcardMatches(module.ModuleName, patterns[i]))
                            {
                                return Path.GetFullPath(module.FileName);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool WildcardMatches(string value, string pattern)
        {
            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value ?? string.Empty, regexPattern, RegexOptions.IgnoreCase);
        }

        private static string FindFileInDirectories(IEnumerable<string> directories, string[] patterns)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawDirectory in directories)
            {
                string directory = NormalizeDirectory(rawDirectory);
                if (string.IsNullOrWhiteSpace(directory) || !visited.Add(directory) || !Directory.Exists(directory))
                {
                    continue;
                }
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
            }
            return null;
        }

        private static void AddEnvironmentDirectories(List<string> directories, string value, string[] relativeDirectories)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            string[] parts = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                AddDirectoryAndRelatives(directories, parts[i].Trim().Trim('"'), relativeDirectories);
            }
        }

        private static void AddDirectoryAndRelatives(List<string> directories, string root, string[] relativeDirectories)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }
            if (File.Exists(root))
            {
                directories.Add(Path.GetDirectoryName(root));
                return;
            }
            for (int i = 0; i < relativeDirectories.Length; i++)
            {
                directories.Add(string.IsNullOrWhiteSpace(relativeDirectories[i]) ? root : Path.Combine(root, relativeDirectories[i]));
            }
        }

        private static string NormalizeDirectory(string path)
        {
            try
            {
                return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }

        private static string FindExecutable(string fileName, params string[] knownPaths)
        {
            string applicationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (File.Exists(applicationPath))
            {
                return Path.GetFullPath(applicationPath);
            }

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] directories = pathValue.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < directories.Length; i++)
            {
                try
                {
                    string candidate = Path.Combine(directories[i].Trim().Trim('"'), fileName);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                }
            }
            for (int i = 0; i < knownPaths.Length; i++)
            {
                if (File.Exists(knownPaths[i]))
                {
                    return Path.GetFullPath(knownPaths[i]);
                }
            }
            return null;
        }

        private static string ReadPackageVersion(string binaryPath, string[] packageNames)
        {
            string sitePackages = FindAncestor(binaryPath, "site-packages");
            if (string.IsNullOrWhiteSpace(sitePackages))
            {
                return null;
            }
            for (int i = 0; i < packageNames.Length; i++)
            {
                try
                {
                    string[] directories = Directory.GetDirectories(sitePackages, packageNames[i] + "-*.dist-info", SearchOption.TopDirectoryOnly);
                    Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
                    for (int j = directories.Length - 1; j >= 0; j--)
                    {
                        string metadataPath = Path.Combine(directories[j], "METADATA");
                        if (!File.Exists(metadataPath))
                        {
                            continue;
                        }
                        foreach (string line in File.ReadLines(metadataPath))
                        {
                            if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                            {
                                return line.Substring("Version:".Length).Trim();
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        private static string FindAncestor(string path, string directoryName)
        {
            DirectoryInfo directory = new FileInfo(path).Directory;
            while (directory != null)
            {
                if (string.Equals(directory.Name, directoryName, StringComparison.OrdinalIgnoreCase))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            return null;
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

        private static string ReadProductVersion(string path)
        {
            try
            {
                return NormalizeVersion(FileVersionInfo.GetVersionInfo(path).ProductVersion, 4);
            }
            catch
            {
                return null;
            }
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

        private sealed class ProbeContext
        {
            private readonly List<string> suiteDirectories = new List<string>();

            internal ProbeContext(string inferenceLibraryPath)
            {
                if (string.IsNullOrWhiteSpace(inferenceLibraryPath))
                {
                    return;
                }
                string sitePackages = FindAncestor(inferenceLibraryPath, "site-packages");
                if (string.IsNullOrWhiteSpace(sitePackages))
                {
                    return;
                }
                suiteDirectories.Add(Path.Combine(sitePackages, "onnxruntime", "capi"));
                suiteDirectories.Add(Path.Combine(sitePackages, "torch", "lib"));
                suiteDirectories.Add(Path.Combine(sitePackages, "cv2"));
                DirectoryInfo sitePackagesDirectory = new DirectoryInfo(sitePackages);
                if (sitePackagesDirectory.Parent != null && sitePackagesDirectory.Parent.Parent != null)
                {
                    suiteDirectories.Add(Path.Combine(sitePackagesDirectory.Parent.Parent.FullName, "bin"));
                }
            }

            internal string[] GetKnownDirectories(params string[] additionalDirectories)
            {
                var result = new List<string>(suiteDirectories);
                result.AddRange(additionalDirectories);
                return result.ToArray();
            }
        }
    }
}
