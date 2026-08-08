using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenIVS2.Models;

namespace OpenIVS2.Services
{
    public sealed class ProductionLogService
    {
        private static readonly ConcurrentDictionary<string, object> FileLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly Encoding CsvEncoding = Encoding.GetEncoding(936);

        public string Append(AppSettings settings, DateTime time, CycleEvaluation evaluation)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (evaluation == null) throw new ArgumentNullException("evaluation");
            if (!settings.EnableProductionLog) return null;

            var directory = settings.ProductionLogDirectory;
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("生产日志目录不能为空");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, time.ToString("yyyyMMdd") + ".csv");
            var fileLock = FileLocks.GetOrAdd(path, new object());
            var enabledSlots = new HashSet<string>(settings.EnabledCameras().Select(x => x.Slot), StringComparer.OrdinalIgnoreCase);
            var results = (evaluation.Cameras ?? new List<CameraCycleResult>())
                .GroupBy(x => x.Slot, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var fields = new List<string>
            {
                time.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                new DateTimeOffset(time).ToUnixTimeMilliseconds().ToString()
            };
            for (var i = 0; i < 6; i++)
            {
                var slot = ((char)('A' + i)).ToString();
                CameraCycleResult cameraResult;
                fields.Add(!enabledSlots.Contains(slot) ? "-" :
                    results.TryGetValue(slot, out cameraResult) && cameraResult.Ok ? "OK" : "NG");
            }
            fields.Add(evaluation.Ok ? "OK" : "NG");
            var line = string.Join(",", fields.Select(Escape));

            lock (fileLock)
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path,
                        "时间,时间戳,相机A,相机B,相机C,相机D,相机E,相机F,总结果\r\n", CsvEncoding);
                }
                File.AppendAllText(path, line + "\r\n", CsvEncoding);
            }
            return path;
        }

        private static string Escape(string value)
        {
            var text = value ?? string.Empty;
            if (text.IndexOf('"') >= 0) text = text.Replace("\"", "\"\"");
            return text.IndexOf(',') >= 0 || text.IndexOf('"') >= 0 ||
                text.IndexOf('\r') >= 0 || text.IndexOf('\n') >= 0
                ? "\"" + text + "\""
                : text;
        }
    }
}
