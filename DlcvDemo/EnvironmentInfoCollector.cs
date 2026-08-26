using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DlcvDemo
{
    internal static class EnvironmentInfoCollector
    {
        private const int CommandTimeoutMilliseconds = 3000;

        internal static string Collect()
        {
            var results = new List<ProbeResult>
            {
                CheckSafely("Windows / .NET", CheckWindowsAndDotNet),
                CheckSafely("NVIDIA 驱动与 GPU", CheckNvidia),
                CheckSafely("CUDA Toolkit", CheckCudaToolkit),
                CheckSafely("cuDNN", CheckCudnn),
                CheckSafely("TensorRT", CheckTensorRt),
                CheckSafely("OpenCV", CheckOpenCv),
                CheckSafely("ONNX Runtime", CheckOnnxRuntime),
                CheckSafely("PyTorch / LibTorch", CheckLibTorch),
                CheckSafely("dlcv_infer", CheckDlcvInfer)
            };

            var text = new StringBuilder();
            text.AppendLine("环境检查结果");
            text.AppendLine("检查时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            for (int i = 0; i < results.Count; i++)
            {
                AppendResult(text, results[i]);
            }
            return text.ToString().TrimEnd();
        }

        private static ProbeResult CheckSafely(string name, Func<ProbeResult> check)
        {
            try
            {
                return check();
            }
            catch (Exception ex)
            {
                var result = new ProbeResult(name);
                result.Message = "检测异常：" + ex.Message;
                return result;
            }
        }

        private static void AppendResult(StringBuilder text, ProbeResult result)
        {
            text.AppendLine();
            text.AppendLine("【" + result.Name + "】");
            text.AppendLine("状态：" + (result.Found ? "已检测到" : "未检测到"));
            if (!string.IsNullOrWhiteSpace(result.Version))
            {
                text.AppendLine("版本：" + result.Version);
            }
            if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                text.AppendLine("信息：" + result.Detail);
            }
            if (!string.IsNullOrWhiteSpace(result.Location))
            {
                text.AppendLine("检测位置：" + result.Location);
            }
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                text.AppendLine("说明：" + result.Message);
            }
            text.AppendLine("已检查位置：" + JoinLocations(result.CheckedLocations));
        }

        private static string JoinLocations(List<string> locations)
        {
            if (locations == null || locations.Count == 0)
            {
                return "无";
            }
            return string.Join("；", locations.ToArray());
        }

        private static ProbeResult CheckWindowsAndDotNet()
        {
            var result = new ProbeResult("Windows / .NET");
            const string windowsRegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            result.CheckedLocations.Add(@"注册表 HKLM\" + windowsRegistryPath);
            result.CheckedLocations.Add(@"注册表 HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");

            string windowsName = Environment.OSVersion.VersionString;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(windowsRegistryPath))
                {
                    if (key != null)
                    {
                        string productName = Convert.ToString(key.GetValue("ProductName"));
                        string displayVersion = Convert.ToString(key.GetValue("DisplayVersion"));
                        string build = Convert.ToString(key.GetValue("CurrentBuild"));
                        if (!string.IsNullOrWhiteSpace(productName))
                        {
                            windowsName = productName;
                            if (!string.IsNullOrWhiteSpace(displayVersion))
                            {
                                windowsName += " " + displayVersion;
                            }
                            if (!string.IsNullOrWhiteSpace(build))
                            {
                                windowsName += "（内部版本 " + build + "）";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Message = "Windows 版本读取失败：" + ex.Message;
            }

            int release = ReadNetFrameworkRelease();
            string netFramework = release > 0
                ? GetNetFrameworkVersion(release) + "（Release " + release + "，CLR " + Environment.Version + "）"
                : "未读取到 Release 值（CLR " + Environment.Version + "）";

            result.Found = true;
            result.Version = windowsName;
            result.Detail = ".NET Framework：" + netFramework;
            result.Location = @"注册表 HKLM\" + windowsRegistryPath + @"；HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full";
            return result;
        }

        private static int ReadNetFrameworkRelease()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("Release");
                        if (value != null)
                        {
                            return Convert.ToInt32(value);
                        }
                    }
                }
            }
            catch
            {
            }
            return 0;
        }

        private static string GetNetFrameworkVersion(int release)
        {
            if (release >= 533320) return "4.8.1";
            if (release >= 528040) return "4.8";
            if (release >= 461808) return "4.7.2";
            if (release >= 461308) return "4.7.1";
            if (release >= 460798) return "4.7";
            if (release >= 394802) return "4.6.2";
            if (release >= 394254) return "4.6.1";
            if (release >= 393295) return "4.6";
            return "4.5 或更早版本";
        }

        private static ProbeResult CheckNvidia()
        {
            var result = new ProbeResult("NVIDIA 驱动与 GPU");
            result.CheckedLocations.Add("WMI Win32_VideoController");
            result.CheckedLocations.Add(@"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe");
            result.CheckedLocations.Add("PATH 中的 nvidia-smi.exe");

            string nvidiaSmiPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            string command = File.Exists(nvidiaSmiPath) ? nvidiaSmiPath : "nvidia-smi.exe";
            List<string> nvidiaSmiItems = ParseNvidiaSmiOutput(RunCommand(command, "--query-gpu=name,driver_version --format=csv,noheader"));
            var gpuItems = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
                {
                    searcher.Options.Timeout = TimeSpan.FromMilliseconds(CommandTimeoutMilliseconds);
                    using (ManagementObjectCollection collection = searcher.Get())
                    {
                        foreach (ManagementObject item in collection)
                        {
                            string name = Convert.ToString(item["Name"]);
                            if (string.IsNullOrWhiteSpace(name)
                                || name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                            string driverVersion = Convert.ToString(item["DriverVersion"]);
                            gpuItems.Add(name + (string.IsNullOrWhiteSpace(driverVersion) ? string.Empty : "，驱动 " + driverVersion));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Message = "WMI 查询失败：" + ex.Message;
            }

            if (nvidiaSmiItems.Count > 0 || gpuItems.Count > 0)
            {
                result.Found = true;
                List<string> primaryItems = nvidiaSmiItems.Count > 0 ? nvidiaSmiItems : gpuItems;
                result.Version = GetDriverVersion(primaryItems[0]);
                result.Location = nvidiaSmiItems.Count > 0
                    ? (File.Exists(nvidiaSmiPath) ? nvidiaSmiPath : "PATH 中的 nvidia-smi.exe")
                    : "WMI Win32_VideoController";
                result.Detail = nvidiaSmiItems.Count > 0
                    ? "nvidia-smi：" + string.Join("；", nvidiaSmiItems.ToArray())
                    : "WMI：" + string.Join("；", gpuItems.ToArray());
                if (gpuItems.Count > 0)
                {
                    if (nvidiaSmiItems.Count > 0)
                    {
                        result.Detail += "；WMI：" + string.Join("；", gpuItems.ToArray());
                    }
                }
            }
            return result;
        }

        private static List<string> ParseNvidiaSmiOutput(string output)
        {
            var items = new List<string>();
            if (string.IsNullOrWhiteSpace(output))
            {
                return items;
            }
            string[] lines = output.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    items.Add(lines[i].Trim());
                }
            }
            return items;
        }

        private static ProbeResult CheckCudaToolkit()
        {
            List<string> roots = GetCudaRoots();
            ProbeResult result = CheckNativeComponent(
                "CUDA Toolkit",
                new[] { "cudart64*.dll", "nvrtc64*.dll" },
                roots,
                new[] { "bin\\cudart64*.dll", "bin\\nvrtc64*.dll" },
                new[] { "version.txt", "include\\cuda.h" },
                new[] { "CUDA_VERSION" });

            if (result.VersionFromHeader)
            {
                string cudaVersion = GetCudaVersionFromHeader(result.Version);
                if (!string.IsNullOrWhiteSpace(cudaVersion))
                {
                    result.Version = cudaVersion;
                }
            }
            AddCheckedEnvironmentVariables(result, "CUDA_PATH、CUDA_PATH_V*");
            return result;
        }

        private static ProbeResult CheckCudnn()
        {
            List<string> roots = GetCudaRoots();
            AddRootAndChildren(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA", "CUDNN"));
            ProbeResult result = CheckNativeComponent(
                "cuDNN",
                new[] { "cudnn*.dll" },
                roots,
                new[] { "bin\\cudnn*.dll", "bin\\12.8\\cudnn*.dll", "bin\\x64\\cudnn*.dll", "lib\\cudnn*.dll", "lib\\12.8\\x64\\cudnn*.dll" },
                new[] { "include\\cudnn_version.h", "include\\12.8\\cudnn_version.h", "include\\cudnn.h" },
                new[] { "CUDNN_MAJOR", "CUDNN_MINOR", "CUDNN_PATCHLEVEL" });
            AddCheckedEnvironmentVariables(result, "CUDA_PATH、CUDA_PATH_V*");
            return result;
        }

        private static ProbeResult CheckTensorRt()
        {
            List<string> roots = GetRootsFromEnvironment("TENSORRT_ROOT", "TENSORRT_HOME", "TENSORRT_DIR");
            AddRootAndChildren(roots, @"C:\TensorRT");
            AddDirectoriesMatching(roots, @"C:\", "TensorRT-*");
            AddRootAndChildren(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA GPU Computing Toolkit", "TensorRT"));
            ProbeResult result = CheckNativeComponent(
                "TensorRT",
                new[] { "nvinfer.dll", "nvinfer_*.dll" },
                roots,
                new[] { "bin\\nvinfer.dll", "lib\\nvinfer.dll", "lib\\nvinfer_*.dll" },
                new[] { "include\\NvInferVersion.h" },
                new[] { "NV_TENSORRT_MAJOR", "NV_TENSORRT_MINOR", "NV_TENSORRT_PATCH" });
            AddCheckedEnvironmentVariables(result, "TENSORRT_ROOT、TENSORRT_HOME、TENSORRT_DIR");
            return result;
        }

        private static ProbeResult CheckOpenCv()
        {
            List<string> roots = GetRootsFromEnvironment("OPENCV_DIR", "OPENCV_ROOT");
            AddApplicationDirectory(roots);
            ProbeResult result = CheckNativeComponent(
                "OpenCV",
                new[] { "opencv_world*.dll", "OpenCvSharpExtern.dll", "opencv_*.dll", "opencv_ffmpeg*.dll" },
                roots,
                new[] { "opencv_world*.dll", "OpenCvSharpExtern.dll", "dll\\x64\\OpenCvSharpExtern.dll", "bin\\dll\\x64\\OpenCvSharpExtern.dll", "bin\\opencv_world*.dll", "bin\\OpenCvSharpExtern.dll", "..\\bin\\opencv_world*.dll", "..\\bin\\OpenCvSharpExtern.dll", "x64\\vc*\\bin\\opencv_world*.dll", "opencv_*.dll", "bin\\opencv_*.dll", "..\\bin\\opencv_*.dll" },
                new[] { "include\\opencv2\\core\\version.hpp", "..\\include\\opencv2\\core\\version.hpp", "..\\..\\..\\include\\opencv2\\core\\version.hpp" },
                new[] { "CV_VERSION_MAJOR", "CV_VERSION_MINOR", "CV_VERSION_REVISION" });
            AddCheckedEnvironmentVariables(result, "OPENCV_DIR、OPENCV_ROOT");
            return result;
        }

        private static ProbeResult CheckOnnxRuntime()
        {
            List<string> roots = GetRootsFromEnvironment("ONNXRUNTIME_ROOT", "ONNXRUNTIME_HOME", "ONNXRUNTIME_DIR");
            AddApplicationDirectory(roots);
            AddRootAndChildren(roots, @"C:\onnxruntime");
            AddRootAndChildren(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "onnxruntime"));
            AddRootAndChildren(roots, @"C:\dlcv\Lib\site-packages\onnxruntime\capi");
            ProbeResult result = CheckNativeComponent(
                "ONNX Runtime",
                new[] { "onnxruntime.dll", "onnxruntime_providers_*.dll", "dlcv_onnxruntime.dll", "dlcv_onnxruntime_providers_*.dll" },
                roots,
                new[] { "onnxruntime.dll", "dlcv_onnxruntime.dll", "bin\\onnxruntime.dll", "bin\\dlcv_onnxruntime.dll", "lib\\onnxruntime.dll", "lib\\dlcv_onnxruntime.dll" },
                new[] { "include\\onnxruntime\\core\\session\\onnxruntime_version.h" },
                new[] { "ORT_VERSION" });
            AddCheckedEnvironmentVariables(result, "ONNXRUNTIME_ROOT、ONNXRUNTIME_HOME、ONNXRUNTIME_DIR");
            return result;
        }

        private static ProbeResult CheckLibTorch()
        {
            List<string> roots = GetRootsFromEnvironment("LIBTORCH_ROOT", "LIBTORCH_DIR", "TORCH_HOME", "PYTORCH_HOME");
            AddApplicationDirectory(roots);
            AddRootAndChildren(roots, @"C:\libtorch");
            AddRootAndChildren(roots, @"C:\dlcv\Lib\site-packages\torch");
            ProbeResult result = CheckNativeComponent(
                "PyTorch / LibTorch",
                new[] { "torch_cpu.dll", "torch_cuda.dll", "c10.dll" },
                roots,
                new[] { "lib\\torch_cpu.dll", "lib\\torch_cuda.dll", "lib\\c10.dll", "torch_cpu.dll", "torch_cuda.dll" },
                new[] { "include\\torch\\csrc\\api\\include\\torch\\version.h" },
                new[] { "TORCH_VERSION_MAJOR", "TORCH_VERSION_MINOR", "TORCH_VERSION_PATCH" });
            if (!HasDetectedVersion(result.Version))
            {
                string pythonVersion = GetPythonTorchVersion(result.InstallationRoot);
                if (!string.IsNullOrWhiteSpace(pythonVersion))
                {
                    result.Version = pythonVersion;
                    result.Detail = "Python 版本文件：" + Path.Combine(result.InstallationRoot, "version.py");
                }
            }
            AddCheckedEnvironmentVariables(result, "LIBTORCH_ROOT、LIBTORCH_DIR、TORCH_HOME、PYTORCH_HOME");
            return result;
        }

        private static ProbeResult CheckDlcvInfer()
        {
            List<string> roots = GetRootsFromEnvironment("DLCV_INFER_PATH", "DLCV_INFER_ROOT");
            AddApplicationDirectory(roots);
            AddRootAndChildren(roots, @"C:\dlcv\Lib\site-packages\dlcvpro_infer");
            AddRootAndChildren(roots, @"C:\dlcv\Lib\site-packages\dlcvpro_infer_csharp");
            ProbeResult result = CheckNativeComponent(
                "dlcv_infer",
                new[] { "dlcv_infer.dll", "dlcv_infer_v.dll" },
                roots,
                new[] { "dlcv_infer.dll", "dlcv_infer_v.dll", "bin\\dlcv_infer.dll", "bin\\dlcv_infer_v.dll" },
                new string[0],
                new string[0]);
            if (result.Found && !HasDetectedVersion(result.Version))
            {
                string packageVersion = GetPythonPackageVersion(result.InstallationRoot, "dlcvpro_infer");
                if (!string.IsNullOrWhiteSpace(packageVersion))
                {
                    result.Version = packageVersion;
                    result.Detail = "Python 包版本：" + packageVersion;
                }
            }
            AddCheckedEnvironmentVariables(result, "DLCV_INFER_PATH、DLCV_INFER_ROOT");
            return result;
        }

        private static ProbeResult CheckNativeComponent(
            string name,
            string[] modulePatterns,
            List<string> roots,
            string[] binaryRelativePaths,
            string[] headerRelativePaths,
            string[] versionMacros)
        {
            var result = new ProbeResult(name);
            AddApplicationDirectory(roots);
            AddLocations(result.CheckedLocations, roots);
            result.CheckedLocations.Add("当前进程已加载模块：" + string.Join("、", modulePatterns));

            string foundFile = FindLoadedModule(modulePatterns);
            if (!string.IsNullOrWhiteSpace(foundFile))
            {
                string installationRoot = FindBestRootForFile(foundFile, roots, headerRelativePaths);
                ApplyFileResult(result, foundFile, installationRoot, headerRelativePaths, versionMacros);
                return result;
            }

            for (int i = 0; i < roots.Count; i++)
            {
                string installationRoot = roots[i];
                string binaryPath = FindFirstFileInRoot(installationRoot, binaryRelativePaths);
                string headerPath = FindFirstFileInRoot(installationRoot, headerRelativePaths);
                if (!string.IsNullOrWhiteSpace(binaryPath))
                {
                    ApplyFileResult(result, binaryPath, installationRoot, headerRelativePaths, versionMacros);
                    return result;
                }

                string headerVersion = GetVersionFromHeader(headerPath, versionMacros);
                if (!string.IsNullOrWhiteSpace(headerVersion))
                {
                    result.Found = true;
                    result.Location = headerPath;
                    result.InstallationRoot = installationRoot;
                    result.Version = headerVersion;
                    result.VersionFromHeader = true;
                    return result;
                }
            }
            return result;
        }

        private static void ApplyFileResult(
            ProbeResult result,
            string binaryPath,
            string installationRoot,
            string[] headerRelativePaths,
            string[] versionMacros)
        {
            result.Found = true;
            result.Location = binaryPath;
            result.InstallationRoot = installationRoot;
            result.Version = GetFileVersion(binaryPath);
            if (HasDetectedVersion(result.Version))
            {
                return;
            }

            string headerPath = FindFirstFileInRoot(installationRoot, headerRelativePaths);
            string headerVersion = GetVersionFromHeader(headerPath, versionMacros);
            if (!string.IsNullOrWhiteSpace(headerVersion))
            {
                result.Version = headerVersion;
                result.VersionFromHeader = true;
                result.Detail = "版本头文件：" + headerPath;
                return;
            }
            result.Version = "未读取到文件版本";
        }

        private static List<string> GetCudaRoots()
        {
            List<string> roots = GetRootsFromEnvironmentPrefix("CUDA_PATH");
            AddRootAndChildren(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA GPU Computing Toolkit", "CUDA"));
            return roots;
        }

        private static List<string> GetRootsFromEnvironment(params string[] names)
        {
            var roots = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                string value = Environment.GetEnvironmentVariable(names[i]);
                AddEnvironmentPaths(roots, value);
            }
            return roots;
        }

        private static List<string> GetRootsFromEnvironmentPrefix(string prefix)
        {
            var roots = new List<string>();
            IDictionary variables = Environment.GetEnvironmentVariables();
            foreach (DictionaryEntry variable in variables)
            {
                string name = Convert.ToString(variable.Key);
                if (name != null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    AddEnvironmentPaths(roots, Convert.ToString(variable.Value));
                }
            }
            return roots;
        }

        private static void AddEnvironmentPaths(List<string> roots, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            string[] paths = value.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < paths.Length; i++)
            {
                AddUniquePath(roots, paths[i]);
            }
        }

        private static void AddApplicationDirectory(List<string> roots)
        {
            AddUniquePath(roots, AppDomain.CurrentDomain.BaseDirectory);
        }

        private static void AddRootAndChildren(List<string> roots, string root)
        {
            AddUniquePath(roots, root);
            try
            {
                if (!Directory.Exists(root))
                {
                    return;
                }
                string[] children = Directory.GetDirectories(root);
                int count = Math.Min(children.Length, 20);
                for (int i = 0; i < count; i++)
                {
                    AddUniquePath(roots, children[i]);
                }
            }
            catch
            {
            }
        }

        private static void AddDirectoriesMatching(List<string> roots, string root, string searchPattern)
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    return;
                }
                string[] directories = Directory.GetDirectories(root, searchPattern, SearchOption.TopDirectoryOnly);
                for (int i = 0; i < directories.Length; i++)
                {
                    AddRootAndChildren(roots, directories[i]);
                }
            }
            catch
            {
            }
        }

        private static void AddUniquePath(List<string> roots, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            string normalized = path.Trim().Trim('"');
            for (int i = 0; i < roots.Count; i++)
            {
                if (string.Equals(roots[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            roots.Add(normalized);
        }

        private static void AddLocations(List<string> locations, List<string> roots)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(roots[i]))
                {
                    locations.Add(roots[i]);
                }
            }
        }

        private static void AddCheckedEnvironmentVariables(ProbeResult result, string names)
        {
            result.CheckedLocations.Add("环境变量：" + names);
        }

        private static string FindLoadedModule(string[] patterns)
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    for (int i = 0; i < patterns.Length; i++)
                    {
                        foreach (ProcessModule module in process.Modules)
                        {
                            if (MatchesPattern(module.ModuleName, patterns[i]))
                            {
                                return module.FileName;
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

        private static string FindFirstFileInRoot(string root, string[] relativePaths)
        {
            if (relativePaths == null)
            {
                return null;
            }
            for (int i = 0; i < relativePaths.Length; i++)
            {
                string path = FindFileInDirectory(root, relativePaths[i]);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }
            return null;
        }

        private static string FindContainingRoot(string filePath, List<string> roots)
        {
            string fullFilePath;
            try
            {
                fullFilePath = Path.GetFullPath(filePath);
            }
            catch
            {
                return Path.GetDirectoryName(filePath);
            }

            string matchedRoot = null;
            for (int i = 0; i < roots.Count; i++)
            {
                try
                {
                    string root = Path.GetFullPath(roots[i]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (fullFilePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && (matchedRoot == null || root.Length > matchedRoot.Length))
                    {
                        matchedRoot = root;
                    }
                }
                catch
                {
                }
            }
            return matchedRoot ?? Path.GetDirectoryName(fullFilePath);
        }

        private static string FindBestRootForFile(string filePath, List<string> roots, string[] headerRelativePaths)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                try
                {
                    string root = Path.GetFullPath(roots[i]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string fullFilePath = Path.GetFullPath(filePath);
                    if (fullFilePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(FindFirstFileInRoot(root, headerRelativePaths)))
                    {
                        return root;
                    }
                }
                catch
                {
                }
            }
            return FindContainingRoot(filePath, roots);
        }

        private static string FindFileInDirectory(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }
            try
            {
                string path = Path.Combine(root, relativePath);
                string fileName = Path.GetFileName(path);
                if (fileName.IndexOf('*') < 0 && File.Exists(path))
                {
                    return path;
                }
                string directory = Path.GetDirectoryName(path);
                if (Directory.Exists(directory))
                {
                    string[] files = Directory.GetFiles(directory, fileName, SearchOption.TopDirectoryOnly);
                    if (files.Length > 0)
                    {
                        return files[0];
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool MatchesPattern(string value, string pattern)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }
            int wildcardIndex = pattern.IndexOf('*');
            if (wildcardIndex < 0)
            {
                return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
            }
            string prefix = pattern.Substring(0, wildcardIndex);
            string suffix = pattern.Substring(wildcardIndex + 1);
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFileVersion(string path)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                if (IsUsefulVersion(info.ProductVersion))
                {
                    return info.ProductVersion;
                }
                if (IsUsefulVersion(info.FileVersion))
                {
                    return info.FileVersion;
                }
            }
            catch
            {
            }
            return null;
        }

        private static string GetPythonPackageVersion(string packageDirectoryPath, string packageName)
        {
            try
            {
                var packageDirectory = new DirectoryInfo(packageDirectoryPath);
                if (!packageDirectory.Exists
                    || !string.Equals(packageDirectory.Name, packageName, StringComparison.OrdinalIgnoreCase)
                    || packageDirectory.Parent == null)
                {
                    return null;
                }
                DirectoryInfo[] metadataDirectories = packageDirectory.Parent.GetDirectories(packageName + "-*.dist-info");
                for (int i = 0; i < metadataDirectories.Length; i++)
                {
                    string metadataPath = Path.Combine(metadataDirectories[i].FullName, "METADATA");
                    if (File.Exists(metadataPath))
                    {
                        string[] lines = File.ReadAllLines(metadataPath);
                        for (int j = 0; j < lines.Length; j++)
                        {
                            if (lines[j].StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                            {
                                return lines[j].Substring("Version:".Length).Trim();
                            }
                        }
                    }

                    string directoryName = metadataDirectories[i].Name;
                    string prefix = packageName + "-";
                    const string suffix = ".dist-info";
                    if (directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && directoryName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        return directoryName.Substring(prefix.Length, directoryName.Length - prefix.Length - suffix.Length);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static string GetPythonTorchVersion(string installationRoot)
        {
            if (string.IsNullOrWhiteSpace(installationRoot))
            {
                return null;
            }
            string versionFile = FindFileInDirectory(installationRoot, "version.py");
            if (string.IsNullOrWhiteSpace(versionFile))
            {
                return null;
            }
            try
            {
                string content = File.ReadAllText(versionFile);
                Match match = Regex.Match(content, "^\\s*__version__\\s*=\\s*['\\\"]([^'\\\"]+)", RegexOptions.Multiline);
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUsefulVersion(string version)
        {
            return !string.IsNullOrWhiteSpace(version)
                && !string.Equals(version, "0.0.0.0", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasDetectedVersion(string version)
        {
            return IsUsefulVersion(version) && !string.Equals(version, "未读取到文件版本", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetCudaVersionFromHeader(string value)
        {
            int versionNumber;
            if (int.TryParse(value, out versionNumber))
            {
                int major = versionNumber / 1000;
                int minor = versionNumber % 1000 / 10;
                return major + "." + minor;
            }
            return value;
        }

        private static string GetVersionFromHeader(string headerPath, string[] macros)
        {
            if (string.IsNullOrWhiteSpace(headerPath) || macros == null || macros.Length == 0)
            {
                return null;
            }
            var values = new List<string>();
            for (int i = 0; i < macros.Length; i++)
            {
                string value = GetHeaderMacroValue(headerPath, macros[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }
            return values.Count == 0 ? null : string.Join(".", values.ToArray());
        }

        private static string GetHeaderMacroValue(string headerPath, string macro)
        {
            if (string.IsNullOrWhiteSpace(headerPath) || !File.Exists(headerPath))
            {
                return null;
            }
            try
            {
                string content = File.ReadAllText(headerPath);
                Match match = Regex.Match(
                    content,
                    "^\\s*#\\s*define\\s+" + Regex.Escape(macro) + "\\s+\\\"?([^\\s\\\"/]+)",
                    RegexOptions.Multiline);
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetDriverVersion(string gpuItem)
        {
            if (string.IsNullOrWhiteSpace(gpuItem))
            {
                return null;
            }
            int driverIndex = gpuItem.LastIndexOf("驱动 ", StringComparison.OrdinalIgnoreCase);
            if (driverIndex >= 0)
            {
                return gpuItem.Substring(driverIndex + 3).Trim();
            }
            int commaIndex = gpuItem.LastIndexOf(',');
            return commaIndex >= 0 ? gpuItem.Substring(commaIndex + 1).Trim() : null;
        }

        private static string RunCommand(string fileName, string arguments)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    process.Start();
                    if (!process.WaitForExit(CommandTimeoutMilliseconds))
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(CommandTimeoutMilliseconds);
                        }
                        catch
                        {
                        }
                        return null;
                    }
                    return process.StandardOutput.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private sealed class ProbeResult
        {
            internal ProbeResult(string name)
            {
                Name = name;
                CheckedLocations = new List<string>();
            }

            internal string Name { get; private set; }
            internal bool Found { get; set; }
            internal string Version { get; set; }
            internal string Detail { get; set; }
            internal string Location { get; set; }
            internal string Message { get; set; }
            internal string InstallationRoot { get; set; }
            internal bool VersionFromHeader { get; set; }
            internal List<string> CheckedLocations { get; private set; }
        }
    }
}
