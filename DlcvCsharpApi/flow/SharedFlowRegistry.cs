using System;
using System.Runtime.InteropServices;

namespace dlcv_infer_csharp
{
    // 两种语言访问同一个 OpenIVS 原生模块，不在托管端另建流程表。
    internal static class SharedFlowRegistry
    {
        private const string Library = "dlcv_infer_cpp.dll";
        [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "openivs_flow_register")]
        internal static extern int Register(IntPtr module, IntPtr json);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "openivs_flow_contains")]
        internal static extern int Contains(IntPtr module, int index);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "openivs_flow_get_info")]
        internal static extern IntPtr GetInfo(IntPtr module, int index);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "openivs_flow_retain")]
        internal static extern int Retain(IntPtr module, int index);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "openivs_flow_release")]
        internal static extern int Release(IntPtr module, int index, int owner);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "openivs_flow_free_all_models")]
        internal static extern void FreeAllModels(IntPtr module);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "openivs_flow_free_result")]
        internal static extern void FreeResult(IntPtr result);
    }
}
