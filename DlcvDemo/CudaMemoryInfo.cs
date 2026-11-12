using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DlcvDemo
{
    /// <summary>
    /// 通过 NVML 读取显卡当前显存占用，供「获取模型信息」显示。
    /// </summary>
    internal static class CudaMemoryInfo
    {
        private const int NvmlSuccess = 0;
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
            bool initialized = false;
            try
            {
                if (nvmlInit_v2() != NvmlSuccess)
                {
                    return I18n.T("CUDA 显存: 不可用");
                }
                initialized = true;

                IntPtr device;
                NvmlMemory memory;
                if (nvmlDeviceGetHandleByIndex_v2(deviceIndex, out device) != NvmlSuccess
                    || nvmlDeviceGetMemoryInfo(device, out memory) != NvmlSuccess)
                {
                    return I18n.T("CUDA 显存: 不可用");
                }

                return string.Format(
                    I18n.T("CUDA 显存: 已用 {0} / 共 {1}"),
                    FormatSize(memory.Used),
                    FormatSize(memory.Total));
            }
            catch (Exception)
            {
                // 显存信息只用于显示，驱动或接口异常不影响模型信息本身。
                return I18n.T("CUDA 显存: 不可用");
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
