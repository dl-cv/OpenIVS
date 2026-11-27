using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvDemo
{
    internal sealed class RemoteWorker : IDisposable
    {
        private readonly string root;
        private readonly IDemoExecution execution;
        private IDemoModel model;
        private IDemoPressureTest pressure;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetStdHandle(int handle, IntPtr value);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess,
            out SafeFileHandle target, uint access, bool inherit, uint options);

        [DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint mode);

        [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "_dup2")]
        private static extern int RedirectRuntimeOutput(int source, int target);

        internal static int Run(string[] args)
            => RunWithStreams((input, output) =>
            {
                if (args.Length != 3 || args[1] != "--root")
                    throw new ArgumentException("用法: remote-worker --root <会话绝对目录>");
                using (var worker = new RemoteWorker(args[2], new LocalDemoExecution()))
                    worker.Serve(input, output);
                return 0;
            });

        internal static int RunWithStreams(Func<TextReader, TextWriter, int> run)
        {
            const int stdout = -11;
            const int stderr = -12;
            const uint duplicateSameAccess = 2;
            SetErrorMode(0x8003);
            var utf8 = new UTF8Encoding(false);
            using (var error = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true })
            {
                Console.SetError(error);
                Console.SetOut(error);
                try
                {
                    var process = GetCurrentProcess();
                    if (!DuplicateHandle(process, GetStdHandle(stdout), process, out var protocol, 0, false, duplicateSameAccess))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    using (var outputStream = new FileStream(protocol, FileAccess.Write))
                    using (var output = new StreamWriter(outputStream, utf8) { AutoFlush = true })
                    using (var input = new StreamReader(Console.OpenStandardInput(), utf8))
                    {
                        // 保留专用协议流，再把运行库的日志输出切到标准错误。
                        if (!SetStdHandle(stdout, GetStdHandle(stderr)))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        RedirectRuntimeOutput(2, 1);
                        return run(input, output);
                    }
                }
                catch (Exception ex)
                {
                    error.WriteLine(ex.Message);
                    return 1;
                }
            }
        }

        internal RemoteWorker(string root, IDemoExecution execution)
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
                throw new ArgumentException("会话目录必须是绝对路径。");
            this.root = Path.GetFullPath(root);
            if (!string.Equals(this.root.TrimEnd('\\', '/'), root.TrimEnd('\\', '/').Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("会话目录必须是绝对路径。");
            for (DirectoryInfo dir = new DirectoryInfo(this.root); dir != null; dir = dir.Parent)
            {
                if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("会话目录必须存在且不能包含链接。");
            }
            this.execution = execution;
        }

        internal void Serve(TextReader input, TextWriter output)
        {
            string line;
            while ((line = ReadRequest(input)) != null)
            {
                JToken id = null;
                JObject response;
                bool exit = false;
                try
                {
                    if (line.Length > 32768) throw new ArgumentException("请求超过长度限制。");
                    var request = JObject.Parse(line, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (request["id"] == null || (request["id"].Type != JTokenType.String && request["id"].Type != JTokenType.Integer))
                        throw new ArgumentException("id 必须是字符串或整数。");
                    id = request["id"];
                    string action = RequiredString(request, "action");
                    JToken result = Execute(action, request);
                    response = new JObject { ["id"] = id, ["ok"] = true, ["result"] = result };
                    if (action == "infer_batch" || action == "infer_json" || response.ToString(Formatting.None).Length > 8192)
                    {
                        string fileName = "result-" + Guid.NewGuid().ToString("N") + ".json";
                        using (var stream = new FileStream(Path.Combine(root, fileName), FileMode.CreateNew, FileAccess.Write))
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                            writer.Write(result?.ToString(Formatting.None) ?? "null");
                        response.Remove("result");
                        response["result_file"] = fileName;
                    }
                    exit = action == "shutdown";
                }
                catch (Exception ex)
                {
                    response = new JObject { ["id"] = id, ["ok"] = false, ["error"] = ex.Message.Replace(root, "会话目录") };
                }
                output.WriteLine(response.ToString(Formatting.None));
                if (exit) break;
            }
        }

        private static string ReadRequest(TextReader reader)
        {
            const int limit = 32768;
            var line = new StringBuilder();
            int character;
            while ((character = reader.Read()) != -1 && character != '\n')
                if (line.Length <= limit) line.Append((char)character);
            return character == -1 && line.Length == 0 ? null : line.ToString();
        }

        private JToken Execute(string action, JObject request)
        {
            switch (action)
            {
                case "ping": return null;
                case "gpu_info": return execution.GetGpuInfo();
                case "dog_info": return execution.GetDogInfo();
                case "environment_info":
                    SetLanguage(request);
                    return execution.GetEnvironmentInfo();
                case "cuda_memory":
                    SetLanguage(request);
                    return execution.DescribeCudaMemory(Integer(request, "device_id", -2, int.MaxValue));
                case "load_model":
                    string path = SessionFile(request, "model", ".dvt", ".dvo");
                    int device = Integer(request, "device_id", -2, int.MaxValue);
                    if (Boolean(request, "rpc_mode", false))
                        throw new NotSupportedException("云端测试不支持本机 RPC 模式。");
                    FreeModel();
                    model = execution.LoadModel(path, device, false);
                    return model.GetCachedModelInfo();
                case "model_info": return RequireModel().GetModelInfo();
                case "infer_batch":
                    using (var image = ReadImage(request))
                    {
                        int batchSize = Integer(request, "batch_size", 1, 1024);
                        var batch = new List<Mat>(batchSize);
                        for (int i = 0; i < batchSize; i++) batch.Add(image);
                        var result = RequireModel().InferBatch(batch, InferParameters(request));
                        try { return DemoRemoteResult.Encode(result); }
                        finally { DemoRemoteResult.DisposeMasks(result); }
                    }
                case "infer_json":
                    using (var image = ReadImage(request))
                        return RequireModel().InferOneOutJson(image, InferParameters(request));
                case "pressure_start":
                    var current = RequireModel();
                    int threads = Integer(request, "thread_count", 1, 32);
                    int size = Integer(request, "batch_size", 1, 1024);
                    bool consistency = Boolean(request, "check_consistency");
                    using (var image = ReadImage(request))
                    {
                        StopPressure();
                        pressure = current.StartPressureTest(image, threads, size, consistency);
                    }
                    return null;
                case "pressure_status":
                    if (pressure == null) throw new InvalidOperationException("请先启动测试。");
                    var status = pressure.GetStatus(SetLanguage(request));
                    return new JObject
                    {
                        ["statistics"] = status.Statistics, ["baseline"] = status.Baseline,
                        ["different_result"] = status.DifferentResult, ["error"] = status.Error
                    };
                case "pressure_stop": StopPressure(); return null;
                case "free_model":
                case "shutdown": FreeModel(); return null;
                default: throw new ArgumentException("不支持此请求。");
            }
        }

        private static JObject InferParameters(JObject request)
        {
            if (!request.TryGetValue("parameters", out var value)) return null;
            if (!(value is JObject parameters)) throw new ArgumentException("parameters 必须是 JSON 对象。");
            foreach (var property in parameters.Properties())
            {
                if (property.Name == "with_mask" && property.Value.Type == JTokenType.Boolean) continue;
                if (property.Name == "threshold" && (property.Value.Type == JTokenType.Integer || property.Value.Type == JTokenType.Float))
                {
                    double threshold = property.Value.Value<double>();
                    if (!double.IsNaN(threshold) && threshold >= 0 && threshold <= 1) continue;
                }
                throw new ArgumentException("不支持此推理参数或参数值。");
            }
            return parameters;
        }

        private static string SetLanguage(JObject request)
        {
            string language = request["language"] == null ? I18n.Chinese : RequiredString(request, "language");
            if (language != I18n.Chinese && language != I18n.English) throw new ArgumentException("不支持此语言。");
            I18n.SetLanguage(language, persist: false);
            return language;
        }

        private static string RequiredString(JObject request, string name)
        {
            if (request[name]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(request.Value<string>(name)))
                throw new ArgumentException(name + " 必须是非空字符串。");
            return request.Value<string>(name);
        }

        private static int Integer(JObject request, string name, int min, int max)
        {
            if (request[name]?.Type != JTokenType.Integer) throw new ArgumentException(name + " 必须是整数。");
            long value = request.Value<long>(name);
            if (value < min || value > max) throw new ArgumentOutOfRangeException(name);
            return (int)value;
        }

        private static bool Boolean(JObject request, string name, bool? defaultValue = null)
        {
            if (request[name] == null && defaultValue.HasValue) return defaultValue.Value;
            if (request[name]?.Type != JTokenType.Boolean) throw new ArgumentException(name + " 必须是布尔值。");
            return request.Value<bool>(name);
        }

        private string SessionFile(JObject request, string name, params string[] extensions)
        {
            string fileName = RequiredString(request, name);
            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException(name + " 必须是会话目录中的 " + string.Join(" 或 ", extensions) + " 文件名。");
            string path = Path.Combine(root, fileName);
            var file = new FileInfo(path);
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new FileNotFoundException("会话目录中缺少指定文件。");
            return path;
        }

        private Mat ReadImage(JObject request)
        {
            var path = SessionFile(request, "image", ".png");
            var bytes = File.ReadAllBytes(path);
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (!bytes.Take(signature.Length).SequenceEqual(signature))
                throw new InvalidDataException("图片不是 PNG 文件。");
            var image = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
            if (!image.Empty()) return image;
            image.Dispose();
            throw new InvalidDataException("图像解码失败！");
        }

        private IDemoModel RequireModel() => model ?? throw new InvalidOperationException("请先加载模型文件！");

        private void StopPressure()
        {
            var current = pressure;
            pressure = null;
            current?.Dispose();
        }

        private void FreeModel()
        {
            try { StopPressure(); }
            finally
            {
                var current = model;
                model = null;
                current?.Dispose();
            }
        }

        public void Dispose() => FreeModel();
    }
}
