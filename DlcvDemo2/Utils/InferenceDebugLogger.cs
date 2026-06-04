using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CSharpObjectResult = dlcv_infer_csharp.Utils.CSharpObjectResult;

namespace DlcvDemo2
{
    internal static class InferenceDebugLogger
    {
        private static readonly object Lock = new object();
        private static string _logFilePath;
        private static bool _enabled = true;

        public static void Enable(string outputPath)
        {
            _enabled = true;
            _logFilePath = outputPath;
            try
            {
                File.WriteAllText(_logFilePath, $"# Inference Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n", Encoding.UTF8);
            }
            catch { }
        }

        public static void Disable()
        {
            _enabled = false;
        }

        public static void Log(string message)
        {
            if (!_enabled || string.IsNullOrEmpty(_logFilePath))
                return;
            lock (Lock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n", Encoding.UTF8);
                }
                catch { }
            }
        }

        public static void LogObject(string prefix, CSharpObjectResult obj)
        {
            string bboxStr = obj.Bbox == null ? "null" : string.Join(", ", obj.Bbox);
            Log($"{prefix}: cat={obj.CategoryName} score={obj.Score:F4} bbox=[{bboxStr}] withAngle={obj.WithAngle} angle={obj.Angle:F4}");
        }

        public static void LogObjects(string prefix, List<CSharpObjectResult> objects)
        {
            if (objects == null)
            {
                Log($"{prefix}: null");
                return;
            }
            Log($"{prefix}: count={objects.Count}");
            for (int i = 0; i < objects.Count; i++)
            {
                LogObject($"  [{i}]", objects[i]);
            }
        }
    }
}
