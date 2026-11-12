using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DlcvDemo
{
    /// <summary>
    /// 读取本程序占用的显存和显卡总显存，供「获取模型信息」显示。
    /// </summary>
    internal static class CudaMemoryInfo
    {
        private const int NvmlSuccess = 0;
        private const string ProcessMemoryCategory = "GPU Process Memory";
        private const string ProcessMemoryCounter = "Dedicated Usage";
        private const double BytesPerMegabyte = 1024.0 * 1024.0;

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int nvmlInit_v2();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int nvmlShutdown();

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlMemory
        {
            public ulong Total;
            public ulong Free;
            public ulong Used;
        }

        internal static string Describe(uint deviceIndex)
        {
            ulong usedBytes;
            if (!TryGetProcessUsedBytes(out usedBytes))
            {
                return I18n.T("CUDA 显存: 不可用");
            }

            ulong totalBytes;
            if (TryGetDeviceTotalBytes(deviceIndex, out totalBytes))
            {
                return string.Format(
                    I18n.T("CUDA 显存: 本程序 {0} / 共 {1}"),
                    FormatSize(usedBytes),
                    FormatSize(totalBytes));
            }

            return string.Format(I18n.T("CUDA 显存: 本程序 {0}"), FormatSize(usedBytes));
        }

        // WDDM 下 NVML 返回的进程显存不可用，改用系统性能计数器读取本进程在显卡上的独占显存。
        private static bool TryGetProcessUsedBytes(out ulong usedBytes)
        {
            usedBytes = 0;
            try
            {
                int processId = Process.GetCurrentProcess().Id;
                string prefix = "pid_" + processId.ToString(CultureInfo.InvariantCulture) + "_";
                var category = new PerformanceCounterCategory(ProcessMemoryCategory);
                foreach (string instanceName in category.GetInstanceNames())
                {
                    if (!instanceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    using (var counter = new PerformanceCounter(ProcessMemoryCategory, ProcessMemoryCounter, instanceName, true))
                    {
                        long bytes = counter.RawValue;
                        if (bytes > 0)
                        {
                            usedBytes += (ulong)bytes;
                        }
                    }
                }
                return true;
            }
            catch (Exception)
            {
                // 性能计数器不可用时只影响显存显示，不影响模型信息本身。
                return false;
            }
        }

        private static bool TryGetDeviceTotalBytes(uint deviceIndex, out ulong totalBytes)
        {
            totalBytes = 0;
            bool initialized = false;
            try
            {
                if (nvmlInit_v2() != NvmlSuccess)
                {
                    return false;
                }
                initialized = true;

                IntPtr device;
                NvmlMemory memory;
                if (nvmlDeviceGetHandleByIndex_v2(deviceIndex, out device) != NvmlSuccess
                    || nvmlDeviceGetMemoryInfo(device, out memory) != NvmlSuccess)
                {
                    return false;
                }
                totalBytes = memory.Total;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (initialized)
                {
                    try
                    {
                        nvmlShutdown();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        internal static string FormatSize(ulong bytes)
        {
            double megabytes = Math.Round(bytes / BytesPerMegabyte, 2);
            if (megabytes < 1024.0)
            {
                return megabytes.ToString("F2", CultureInfo.InvariantCulture) + " MB";
            }
            return (megabytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
