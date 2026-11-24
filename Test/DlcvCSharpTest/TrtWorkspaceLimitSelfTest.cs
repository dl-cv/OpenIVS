using System;
using System.Reflection;
using System.Runtime.Serialization;
using dlcv_infer_csharp;

namespace DlcvCSharpTest
{
    internal static class TrtWorkspaceLimitSelfTest
    {
        public static int Run()
        {
            var instanceField = typeof(DllLoader).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            object original = instanceField.GetValue(null);
            var loader = (DllLoader)FormatterServices.GetUninitializedObject(typeof(DllLoader));
            int calls = 0;
            int received = 0;
            loader.dlcv_set_trt_workspace_limit = limit =>
            {
                calls++;
                received = limit;
                return 0;
            };
            try
            {
                instanceField.SetValue(null, loader);
                foreach (int limit in new[] { int.MinValue, -1, 0, 33, int.MaxValue })
                {
                    if (Utils.SetTrtWorkspaceLimit(limit) != -1 || calls != 0)
                        throw new Exception("非法上限调用了原生设置函数");
                }
                foreach (int limit in new[] { 1, 2, 4, 32 })
                {
                    int before = calls;
                    if (Utils.SetTrtWorkspaceLimit(limit) != 0 || calls != before + 1 || received != limit)
                        throw new Exception("有效上限未原样传给实际 DLL 的代理");
                }
                loader.dlcv_set_trt_workspace_limit = limit => -1;
                if (Utils.SetTrtWorkspaceLimit(4) != -1)
                    throw new Exception("未原样返回原生设置结果");
                loader.dlcv_set_trt_workspace_limit = null;
                try
                {
                    Utils.SetTrtWorkspaceLimit(4);
                    throw new Exception("缺少导出时未报告错误");
                }
                catch (EntryPointNotFoundException)
                {
                }
                Console.WriteLine("C# 工作空间设置：范围检查、参数转发、返回值与缺少导出检查通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                instanceField.SetValue(null, original);
            }
        }
    }
}
