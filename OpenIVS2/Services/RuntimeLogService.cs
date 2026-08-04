using System;
using System.IO;
using System.Linq;
using System.Text;
using OpenIVS2.Models;

namespace OpenIVS2.Services
{
    public sealed class RuntimeLogService
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _sync = new object();
        private bool _enabled;
        private string _directory;
        private string _explicitFilePath;
        private int _maxFileSizeMB = 10;
        private int _maxFileCount = 7;

        public void Configure(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            lock (_sync)
            {
                _enabled = settings.EnableRuntimeLog;
                _directory = settings.RuntimeLogDirectory;
                _explicitFilePath = null;
                _maxFileSizeMB = Math.Max(1, settings.RuntimeLogMaxFileSizeMB);
                _maxFileCount = Math.Max(1, settings.RuntimeLogMaxFileCount);
            }
        }

        internal void ConfigureFile(string filePath, int maxFileSizeMB = 10, int maxFileCount = 7)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("日志文件路径不能为空", "filePath");
            lock (_sync)
            {
                _enabled = true;
                _directory = Path.GetDirectoryName(filePath);
                _explicitFilePath = filePath;
                _maxFileSizeMB = Math.Max(1, maxFileSizeMB);
                _maxFileCount = Math.Max(1, maxFileCount);
            }
        }

        public bool WriteLine(string line)
        {
            lock (_sync)
            {
                if (!_enabled) return false;
                try
                {
                    var path = GetActiveFilePath();
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                    var content = (line ?? string.Empty) + Environment.NewLine;
                    RotateIfNeeded(path, Utf8NoBom.GetByteCount(content));
                    File.AppendAllText(path, content, Utf8NoBom);
                    CleanupOldFiles(path);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private string GetActiveFilePath()
        {
            if (!string.IsNullOrWhiteSpace(_explicitFilePath)) return _explicitFilePath;
            var directory = string.IsNullOrWhiteSpace(_directory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs")
                : _directory;
            return Path.Combine(directory, "OpenIVS-2026_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }

        private void RotateIfNeeded(string path, int incomingBytes)
        {
            var file = new FileInfo(path);
            var maxBytes = _maxFileSizeMB * 1024L * 1024L;
            if (!file.Exists || file.Length == 0 || file.Length + incomingBytes <= maxBytes) return;
            var directory = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var suffix = DateTime.Now.ToString("HHmmss_fff");
            var rotated = Path.Combine(directory, name + "_" + suffix + extension);
            var sequence = 1;
            while (File.Exists(rotated))
            {
                rotated = Path.Combine(directory, name + "_" + suffix + "_" + sequence.ToString("D3") + extension);
                sequence++;
            }
            File.Move(path, rotated);
        }

        private void CleanupOldFiles(string activePath)
        {
            var directory = Path.GetDirectoryName(activePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
            var prefix = _explicitFilePath == null ? "OpenIVS-2026_" : Path.GetFileNameWithoutExtension(activePath);
            var extension = Path.GetExtension(activePath);
            var files = Directory.GetFiles(directory, prefix + "*" + extension)
                .OrderBy(File.GetLastWriteTimeUtc)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (var i = 0; i < files.Length - _maxFileCount; i++) File.Delete(files[i]);
        }
    }
}
