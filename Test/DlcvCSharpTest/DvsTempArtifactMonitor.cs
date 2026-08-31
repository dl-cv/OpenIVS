using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpTest
{
    internal sealed class DvsTempArtifactMonitor : IDisposable
    {
        private readonly object _sync = new object();
        private readonly string _tempRoot;
        private readonly HashSet<string> _archiveFileNames;
        private readonly HashSet<string> _baseline;
        private readonly HashSet<string> _found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly FileSystemWatcher _watcher;
        private string _watcherError;
        private bool _disposed;

        public DvsTempArtifactMonitor(string archivePath, bool readArchiveFileNames = true)
        {
            _tempRoot = Path.GetFullPath(Path.GetTempPath());
            _archiveFileNames = readArchiveFileNames
                ? ReadArchiveFileNames(archivePath)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _baseline = ScanSuspiciousPaths();
            _watcher = new FileSystemWatcher(_tempRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024
            };
            _watcher.Created += OnCreated;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;
        }

        public bool HasArtifacts
        {
            get
            {
                lock (_sync)
                {
                    return _found.Count > 0;
                }
            }
        }

        public bool HasWatcherError
        {
            get
            {
                lock (_sync)
                {
                    return !string.IsNullOrEmpty(_watcherError);
                }
            }
        }

        public string Describe()
        {
            lock (_sync)
            {
                var parts = _found.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
                if (!string.IsNullOrEmpty(_watcherError)) parts.Add("监测异常: " + _watcherError);
                return string.Join("; ", parts);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watcher.EnableRaisingEvents = false;
            Capture(ScanSuspiciousPaths());
            _watcher.Dispose();
        }

        private static HashSet<string> ReadArchiveFileNames(string archivePath)
        {
            string headerLine;
            using (var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, false))
            {
                string magic = reader.ReadLine();
                headerLine = reader.ReadLine();
                if (!string.Equals(magic, "DV", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(headerLine))
                {
                    throw new InvalidDataException("流程归档头信息不完整");
                }
            }

            JObject header = JObject.Parse(headerLine);
            JArray fileList = header["file_list"] as JArray;
            if (fileList == null) throw new InvalidDataException("流程归档缺少 file_list");
            return new HashSet<string>(
                fileList.Values<string>()
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(Path.GetFileName),
                StringComparer.OrdinalIgnoreCase);
        }

        private HashSet<string> ScanSuspiciousPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string directory in Directory.EnumerateDirectories(_tempRoot, "DlcvDvs_*", SearchOption.TopDirectoryOnly))
                {
                    paths.Add(Path.GetFullPath(directory));
                    try
                    {
                        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                        {
                            paths.Add(Path.GetFullPath(file));
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                foreach (string file in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    if (_archiveFileNames.Contains(Path.GetFileName(file))) paths.Add(Path.GetFullPath(file));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return paths;
        }

        private void OnCreated(object sender, FileSystemEventArgs args)
        {
            CapturePath(args.FullPath);
        }

        private void OnRenamed(object sender, RenamedEventArgs args)
        {
            CapturePath(args.FullPath);
        }

        private void OnError(object sender, ErrorEventArgs args)
        {
            lock (_sync)
            {
                _watcherError = args.GetException()?.Message ?? "文件系统事件监测失败";
            }
        }

        private void Capture(IEnumerable<string> paths)
        {
            foreach (string path in paths) CapturePath(path);
        }

        private void CapturePath(string path)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }
            if (!IsSuspicious(fullPath) || _baseline.Contains(fullPath)) return;
            lock (_sync)
            {
                _found.Add(fullPath);
            }
        }

        private bool IsSuspicious(string fullPath)
        {
            string relative = fullPath.StartsWith(_tempRoot, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(_tempRoot.Length)
                : fullPath;
            string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(part => part.StartsWith("DlcvDvs_", StringComparison.OrdinalIgnoreCase))) return true;
            return _archiveFileNames.Contains(Path.GetFileName(fullPath));
        }
    }
}
