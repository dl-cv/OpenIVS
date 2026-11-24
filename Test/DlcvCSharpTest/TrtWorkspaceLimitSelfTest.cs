using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using dlcv_infer_csharp;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class TrtWorkspaceLimitSelfTest
    {
        public static int RunWithSdk(string[] args)
        {
            if (args.Length != 5 || !int.TryParse(args[2], out int expectedStatus) ||
                (expectedStatus != -1 && expectedStatus != 0))
            {
                Console.Error.WriteLine("用法: trt-workspace-limit-selftest <DLL路径> <-1|0> <模型路径> <图片路径>");
                return 2;
            }

            try
            {
                string expectedDll = Path.GetFullPath(args[1]);
                DllLoader.EnsureForModel(args[3]);
                var loader = DllLoader.Instance;
                if (!string.Equals(loader.LoadedNativeModulePath, expectedDll, StringComparison.OrdinalIgnoreCase) ||
                    (loader.dlcv_set_trt_workspace_limit != null) != (expectedStatus == 0))
                    throw new Exception("实际推理 DLL 的路径或导出与预期不符：" + loader.LoadedNativeModulePath);
                int status = Utils.SetTrtWorkspaceLimit(4);
                if (status != expectedStatus)
                    throw new Exception("设置工作空间上限返回值与预期不符：" + status);

                using (var bgr = Cv2.ImDecode(File.ReadAllBytes(args[4]), ImreadModes.Color))
                using (var rgb = new Mat())
                using (var model = new Model(args[3], 0))
                {
                    if (bgr.Empty()) throw new Exception("读取推理图片失败");
                    Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
                    if (model.Loader != loader || model.Infer(rgb).SampleResults?.Count != 1)
                        throw new Exception("设置上限后未使用同一推理 DLL 返回单张图片的结果");
                }
                Console.WriteLine("C#：工作空间设置返回 " + status + "，模型加载和图片推理通过：" + expectedDll);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

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
                if (Utils.SetTrtWorkspaceLimit(4) != -1)
                    throw new Exception("缺少导出时未返回 -1");
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
