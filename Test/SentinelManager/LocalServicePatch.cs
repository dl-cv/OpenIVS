using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SentinelManager
{
    public sealed class ServiceCommandResult
    {
        public ServiceCommandResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output ?? "";
        }
        public int ExitCode { get; private set; }
        public string Output { get; private set; }
    }

    public interface ISentinelServiceCommands
    {
        ServiceCommandResult Run(string action);
    }

    public sealed class WindowsServiceCommands : ISentinelServiceCommands
    {
        public ServiceCommandResult Run(string action)
        {
            if (action != "query" && action != "start" && action != "stop")
                throw new ArgumentException("不支持的服务命令。", nameof(action));
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new SentinelException("本地服务操作仅支持 Windows。");
            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (string.IsNullOrWhiteSpace(systemDirectory) || !Path.IsPathRooted(systemDirectory))
                throw new SentinelException("无法确定 Windows 系统目录，未执行服务命令。");
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(systemDirectory, "sc.exe"),
                Arguments = action + " hasplms",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start()) throw new IOException("服务命令未启动");
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(10000))
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(1000);
                        }
                        catch (InvalidOperationException) { }
                        throw new TimeoutException("服务命令执行超时");
                    }
                    string text = output.GetAwaiter().GetResult();
                    error.GetAwaiter().GetResult();
                    return new ServiceCommandResult(process.ExitCode, text);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is TimeoutException || ex is System.ComponentModel.Win32Exception
                || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                throw new SentinelException("Sentinel 服务 " + action + " 失败或超时，请检查系统服务状态。", ex);
            }
        }
    }

    public interface ILocalServicePatch
    {
        string ReadServiceStatus();
        string Prepare();
        bool EnsureRunning();
        string Apply();
    }

    public sealed class LocalServicePatch : ILocalServicePatch
    {
        private const string ConfigurationText = "serveraddr=127.0.0.1\r\nbroadcastsearch=0\r\n";
        private readonly string localAppData;
        private readonly ISentinelServiceCommands commands;
        private readonly Func<bool> isAdministrator;
        private readonly Action<TimeSpan> sleep;
        private readonly Func<double> monotonicSeconds;

        public LocalServicePatch() : this(null, new WindowsServiceCommands(), CheckAdministrator) { }

        public LocalServicePatch(string localAppData, ISentinelServiceCommands commands, Func<bool> isAdministrator,
            Action<TimeSpan> sleep = null, Func<double> monotonicSeconds = null)
        {
            this.localAppData = localAppData;
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
            this.sleep = sleep ?? (duration => Thread.Sleep(duration));
            this.monotonicSeconds = monotonicSeconds ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        }

        private static bool CheckAdministrator()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new SentinelException("本地服务补丁仅支持 Windows。");
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private ServiceCommandResult Command(string action, params int[] accepted)
        {
            ServiceCommandResult result;
            try
            {
                result = commands.Run(action);
            }
            catch (SentinelException) { throw; }
            catch (Exception ex) when (ex is IOException || ex is TimeoutException || ex is UnauthorizedAccessException
                || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                throw new SentinelException("Sentinel 服务 " + action + " 失败或超时，请检查系统服务状态。", ex);
            }
            if (result == null) throw new SentinelException("Sentinel 服务命令未返回结果。");
            if (result.ExitCode == 1060)
                throw new SentinelException("Sentinel 服务（hasplms）未安装，请先安装驱动。");
            if (result.ExitCode == 5)
                throw new SentinelException("没有 Sentinel 服务控制权限，请以管理员身份运行程序。");
            if (result.ExitCode != 0 && Array.IndexOf(accepted, result.ExitCode) < 0)
                throw new SentinelException("Sentinel 服务 " + action + " 失败，系统返回 " + result.ExitCode + "。");
            return result;
        }

        private static int ParseServiceState(string output)
        {
            Match state = Regex.Match(output, @"\bSTATE\s*:\s*([1-7])\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!state.Success) throw new SentinelException("Sentinel 服务状态格式不受支持，未继续操作。");
            return state.Groups[1].Value[0] - '0';
        }

        private int State()
        {
            return ParseServiceState(Command("query").Output);
        }

        public string ReadServiceStatus()
        {
            try
            {
                int state = State();
                return new[] { "", "已停止", "正在启动", "正在停止", "运行中", "正在恢复", "正在暂停", "已暂停" }[state];
            }
            catch (SentinelException ex)
            {
                return "无法读取（" + ex.Message + "）";
            }
        }

        private int Wait(params int[] states)
        {
            double deadline = monotonicSeconds() + 30;
            while (true)
            {
                int state = State();
                if (Array.IndexOf(states, state) >= 0) return state;
                if (monotonicSeconds() >= deadline)
                    throw new SentinelException("等待 Sentinel 服务切换状态超时，当前状态编号：" + state + "。");
                sleep(TimeSpan.FromMilliseconds(250));
            }
        }

        public string Prepare()
        {
            // 权限检查早于目录创建、文件写入和服务修改。
            if (!isAdministrator())
                throw new SentinelException("需要管理员权限；请关闭窗口后，以管理员身份运行程序。尚未写入配置或重启服务。");
            string raw = localAppData ?? Environment.GetEnvironmentVariable("LOCALAPPDATA");
            string target;
            try
            {
                string root = string.IsNullOrWhiteSpace(raw) ? null : Path.GetPathRoot(raw);
                if (string.IsNullOrWhiteSpace(raw) || !Path.IsPathRooted(raw) || string.IsNullOrEmpty(root)
                    || root == "\\" || root == "/" || root.EndsWith(":", StringComparison.Ordinal))
                    throw new ArgumentException("目录必须为完整绝对路径");
                target = Path.Combine(Path.GetFullPath(raw), "SafeNet Sentinel", "Sentinel LDK", "hasp_26146.ini");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException || ex is SecurityException)
            {
                throw new SentinelException("无法确定当前用户的 LocalAppData 目录，未写入配置。", ex);
            }
            State();
            return target;
        }

        public bool EnsureRunning()
        {
            int state = Wait(1, 4, 7);
            if (state == 4) return false;
            if (!isAdministrator())
                throw new SentinelException("服务未启动，请以管理员身份运行后重试。");
            if (state == 7)
            {
                Command("stop", 1062);
                Wait(1);
            }
            Command("start", 1056);
            Wait(4);
            return true;
        }

        private void Restart()
        {
            int state = Wait(1, 4, 7);
            if (state != 1)
            {
                Command("stop", 1062);
                Wait(1);
            }
            Command("start", 1056);
            Wait(4);
        }

        public string Apply()
        {
            string target = Prepare();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                byte[] expected = Encoding.ASCII.GetBytes(ConfigurationText);
                File.WriteAllBytes(target, expected);
                byte[] actual = File.ReadAllBytes(target);
                if (actual.Length != expected.Length) throw new IOException("配置回读长度不一致");
                for (int i = 0; i < expected.Length; i++)
                    if (actual[i] != expected[i]) throw new IOException("配置回读内容不一致");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException
                || ex is ArgumentException || ex is NotSupportedException)
            {
                throw new SentinelException("本地配置写入或回读失败：" + target + "。文件可能已变化；未执行服务重启。\n" + ex.Message, ex);
            }
            try
            {
                Restart();
            }
            catch (SentinelException ex)
            {
                throw new SentinelException("配置已写入：" + target + "\n服务重启未完成，配置保留，请检查服务。\n" + ex.Message, ex);
            }
            return "配置已写入：" + target + "\nserveraddr=127.0.0.1\nbroadcastsearch=0\n"
                + "Sentinel 服务（hasplms）：已重启并确认运行中";
        }
    }
}
