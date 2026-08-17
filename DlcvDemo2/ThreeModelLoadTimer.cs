using System;
using System.Diagnostics;
using dlcv_infer_csharp;

namespace DlcvDemo2
{
    internal static class ThreeModelLoadTimer
    {
        public static Model Load(string modelDisplayName, string modelPath, Action<string> report, out TimeSpan elapsed)
        {
            ReportSafely(report, $"开始加载{modelDisplayName}: {modelPath}");

            Stopwatch stopwatch = Stopwatch.StartNew();
            Model model = new Model(modelPath, 0, false, false);
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed;

            ReportSafely(report, $"{modelDisplayName}加载完成，耗时 {elapsed.TotalSeconds:F2} 秒");
            return model;
        }

        private static void ReportSafely(Action<string> report, string message)
        {
            if (report == null)
            {
                return;
            }

            try
            {
                report(message);
            }
            catch
            {
            }
        }
    }
}
