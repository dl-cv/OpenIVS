using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelManager
{
    public interface ISentinelClient
    {
        string GetInfo();
        string CreateLmid();
        string DisableNetwork();
        string ApplyLocalPatch();
        string Repair();
    }

    public sealed class SentinelResponse
    {
        public SentinelResponse(int statusCode, byte[] body)
        {
            StatusCode = statusCode;
            Body = body;
        }
        public int StatusCode { get; private set; }
        public byte[] Body { get; private set; }
    }

    public interface ISentinelTransport
    {
        SentinelResponse Send(string path, byte[] body);
    }

    public sealed class HttpSentinelTransport : ISentinelTransport
    {
        private const string BaseUrl = "http://127.0.0.1:1947/";

        public SentinelResponse Send(string path, byte[] body)
        {
            // 只接受已知 ACC 路径，传输地址不能由响应或调用参数更换。
            bool readPath = path == "_int_/tab_diag2.html" || path == "tab_dev.html" || path == "_int_/conf_to.html" || path == "_int_/conf_from.html"
                || (path != null && Regex.IsMatch(path, @"\Atab_feat\.html\?haspid=[0-9]+\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            bool actionPath = path == "action.html?Create_new_lmid" && body == null;
            bool configurationPath = path == "action.html" && body != null;
            if ((!readPath || body != null) && !actionPath && !configurationPath)
                throw new SentinelException("不支持的 Sentinel ACC 请求路径或方法。");
            try
            {
                return SendAsync(path, body).GetAwaiter().GetResult();
            }
            catch (SentinelException) { throw; }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is OperationCanceledException)
            {
                throw new SentinelException("无法读取本机 Sentinel ACC，请检查服务与 1947 端口。", ex);
            }
        }

        private static async Task<SentinelResponse> SendAsync(string path, byte[] body)
        {
            using (var handler = new HttpClientHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                UseDefaultCredentials = false,
                UseCookies = false,
                Credentials = null
            })
            using (var client = new HttpClient(handler))
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, BaseUrl + path))
            {
                request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
                if (body != null)
                {
                    request.Content = new ByteArrayContent(body);
                    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
                }
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    int status = (int)response.StatusCode;
                    if (status != 200) return new SentinelResponse(status, new byte[0]);
                    if (response.Content.Headers.ContentLength > SentinelProtocol.MaximumResponseBytes)
                        throw new SentinelException("Sentinel 响应过大，已停止读取。");
                    using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        while (true)
                        {
                            int remaining = SentinelProtocol.MaximumResponseBytes + 1 - (int)output.Length;
                            int count = await source.ReadAsync(buffer, 0, Math.Min(buffer.Length, remaining), timeout.Token).ConfigureAwait(false);
                            if (count == 0) break;
                            output.Write(buffer, 0, count);
                            if (output.Length > SentinelProtocol.MaximumResponseBytes)
                                throw new SentinelException("Sentinel 响应过大，已停止读取。");
                        }
                        return new SentinelResponse(status, output.ToArray());
                    }
                }
            }
        }
    }

    public sealed class SentinelClient : ISentinelClient
    {
        private readonly ISentinelTransport transport;
        private readonly ILocalServicePatch localPatch;
        private readonly Action<TimeSpan> sleep;

        public SentinelClient() : this(new HttpSentinelTransport(), new LocalServicePatch()) { }

        public SentinelClient(ISentinelTransport transport, ILocalServicePatch localPatch, Action<TimeSpan> sleep = null)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.localPatch = localPatch ?? throw new ArgumentNullException(nameof(localPatch));
            this.sleep = sleep ?? (duration => Thread.Sleep(duration));
        }

        private string Request(string path, byte[] body = null)
        {
            try
            {
                SentinelResponse response = transport.Send(path, body);
                if (response == null) throw new SentinelException("Sentinel ACC 未返回响应。");
                if (response.StatusCode >= 300 && response.StatusCode < 400)
                    throw new SentinelException("Sentinel ACC 返回重定向，请检查本机服务及访问权限。");
                if (response.StatusCode != 200)
                    throw new SentinelException("Sentinel ACC 返回 HTTP " + response.StatusCode + "，请检查访问权限。");
                return SentinelProtocol.DecodeResponse(response.Body);
            }
            catch (SentinelException) { throw; }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is TimeoutException
                || ex is OperationCanceledException || ex is RegexMatchTimeoutException)
            {
                throw new SentinelException("无法读取本机 Sentinel ACC，请检查服务与 1947 端口。", ex);
            }
        }

        private bool[] NetworkState()
        {
            return SentinelProtocol.ReadNetworkState(Request("_int_/conf_to.html"), Request("_int_/conf_from.html"));
        }

        private static bool AnyEnabled(bool[] state)
        {
            return state[0] || state[1] || state[2];
        }

        private bool[] ReadyNetworkState()
        {
            // 服务刚启动时仅重试读取，不重复提交配置。
            for (int attempt = 0; ; attempt++)
            {
                try { return NetworkState(); }
                catch (SentinelException)
                {
                    if (attempt == 5) throw;
                    sleep(TimeSpan.FromMilliseconds(500));
                }
            }
        }

        public string GetInfo()
        {
            string service = localPatch.ReadServiceStatus();
            var lines = new List<string> { "Sentinel 服务（hasplms）：" + service };
            var devices = new List<Dictionary<string, object>>();
            var features = new List<Dictionary<string, object>>();
            try
            {
                bool waitForAcc = Array.IndexOf(new[] { "已停止", "正在启动", "正在停止", "正在恢复", "正在暂停", "已暂停" }, service) >= 0;
                if (waitForAcc)
                {
                    localPatch.EnsureRunning();
                    service = localPatch.ReadServiceStatus();
                    lines[0] = "Sentinel 服务（hasplms）：" + service;
                }
                if (service != "运行中") throw new SentinelException("服务未运行，请检查驱动或服务权限。");
                string response;
                for (int attempt = 0; ; attempt++)
                {
                    try { response = Request("tab_dev.html"); break; }
                    catch (SentinelException)
                    {
                        if (!waitForAcc || attempt == 5) throw;
                        sleep(TimeSpan.FromMilliseconds(500));
                    }
                }
                foreach (var device in SentinelProtocol.ReadRecords(response, "devices", "haspid", "typ", "isloc", "configuration"))
                    if (SentinelProtocol.IsLocalHardware(device)) devices.Add(device);
                foreach (var device in devices)
                {
                    response = Request("tab_feat.html?haspid=" + (string)device["haspid"]);
                    features.AddRange(SentinelProtocol.ReadRecords(response, "features", "haspid", "fid", "isloc", "prid"));
                }
            }
            catch (SentinelException ex)
            {
                throw new SentinelException(string.Join("\n", lines) + "\n" + ex.Message, ex);
            }
            lines.Add("本机 ACC：可访问");
            lines.Add("Sentinel 加密狗数量：" + devices.Count);
            foreach (var device in devices)
            {
                lines.Add("");
                lines.Add("加密狗 ID：" + (string)device["haspid"] + "（本机）");
                lines.Add("类型：" + SentinelProtocol.PlainText((string)device["typ"]));
                var matches = features.FindAll(feature => (string)feature["haspid"] == (string)device["haspid"] && (string)feature["isloc"] == (string)device["isloc"]);
                lines.Add("Feature 数量：" + matches.Count);
                foreach (var feature in matches)
                {
                    object rawName;
                    string name = feature.TryGetValue("fn", out rawName) ? SentinelProtocol.PlainText((string)rawName) : "";
                    lines.Add("  Feature " + (string)feature["fid"] + " | " + (name.Length == 0 ? "未命名" : name) + " | 产品 " + (string)feature["prid"]);
                }
            }
            if (devices.Count == 0) lines.Add("未检测到本机 Sentinel 普通硬件加密狗。");
            try
            {
                bool[] state = NetworkState();
                lines.Add("");
                lines.Add("网络访问配置：");
                string[] labels = { "访问远程授权", "广播搜索远程授权", "远程客户端访问" };
                for (int i = 0; i < labels.Length; i++) lines.Add("  " + labels[i] + "：" + (state[i] ? "允许" : "关闭"));
            }
            catch (SentinelException ex)
            {
                lines.Add("\n网络配置读取失败：" + ex.Message);
            }
            return string.Join("\n", lines);
        }

        public string ReadLmid()
        {
            return SentinelProtocol.ReadLmid(Request("_int_/tab_diag2.html"));
        }

        private sealed class LmidChange
        {
            public string Before;
            public string After;
            public bool ResponseFailed;

            public string Describe()
            {
                return "LMID 已更新并回读确认。\n原 LMID：" + Before + "\n新 LMID：" + After
                    + (ResponseFailed ? "\n重置响应未完整确认，已通过回读核验，未重复发送请求。" : "");
            }
        }

        private LmidChange ResetAndVerifyLmid()
        {
            string before;
            try { before = ReadLmid(); }
            catch (SentinelException ex)
            {
                throw new SentinelException("无法读取重置前 LMID，未发送重置请求。\n" + ex.Message, ex);
            }
            SentinelException responseError = null;
            // 修改请求仅发送一次，响应异常时也只回读实际状态。
            try { Request("action.html?Create_new_lmid"); }
            catch (SentinelException ex) { responseError = ex; }
            SentinelException readError = null;
            bool readSucceeded = false;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (attempt > 0) sleep(TimeSpan.FromMilliseconds(500));
                try
                {
                    string after = ReadLmid();
                    readSucceeded = true;
                    if (!string.Equals(before, after, StringComparison.Ordinal))
                        return new LmidChange { Before = before, After = after, ResponseFailed = responseError != null };
                }
                catch (SentinelException ex) { readError = ex; }
            }
            string reason = readSucceeded ? "LMID 未变化，未确认重置成功。" : "LMID 回读失败，无法确认重置结果。";
            reason += "已停止后续操作，未重复发送重置请求。";
            if (responseError != null) reason += "\n重置响应：" + responseError.Message;
            if (readError != null) reason += "\n回读：" + readError.Message;
            throw new SentinelException(reason);
        }

        private void VerifyLmidAfterRestart(string expected)
        {
            bool readSucceeded = false;
            SentinelException readError = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (attempt > 0) sleep(TimeSpan.FromMilliseconds(500));
                try
                {
                    string actual = ReadLmid();
                    readSucceeded = true;
                    if (string.Equals(actual, expected, StringComparison.Ordinal)) return;
                }
                catch (SentinelException ex) { readError = ex; }
            }
            string reason = readSucceeded ? "服务重启后 LMID 与已确认的新值不一致。" : "服务重启后 LMID 回读失败。";
            if (readError != null) reason += "\n" + readError.Message;
            throw new SentinelException(reason + "未重复发送重置请求。");
        }

        public string CreateLmid()
        {
            return ResetAndVerifyLmid().Describe();
        }

        public string DisableNetwork()
        {
            if (!AnyEnabled(NetworkState())) return "网络访问已关闭。";
            try
            {
                Request("action.html", Encoding.UTF8.GetBytes(SentinelProtocol.NetworkDisablePayload()));
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0) sleep(TimeSpan.FromMilliseconds(300));
                    if (!AnyEnabled(NetworkState()))
                        return "访问远程授权：关闭\n广播搜索远程授权：关闭\n远程客户端访问：关闭";
                }
                throw new SentinelException("回读结果仍包含开启选项。");
            }
            catch (SentinelException ex)
            {
                throw new SentinelException("网络设置未全部关闭，请检查 ACC。\n" + ex.Message, ex);
            }
        }

        public string ApplyLocalPatch()
        {
            return localPatch.Apply();
        }

        public string Repair()
        {
            var completed = new List<string>();
            try
            {
                localPatch.Prepare();
                localPatch.EnsureRunning();
                completed.Add("服务：运行中");
                ReadyNetworkState();
                completed.Add("网络设置：\n" + DisableNetwork());
                LmidChange change = ResetAndVerifyLmid();
                completed.Add(change.Describe());
                completed.Add(ApplyLocalPatch());
                VerifyLmidAfterRestart(change.After);
                completed.Add("LMID：服务重启后回读一致。");
                if (AnyEnabled(ReadyNetworkState()))
                    throw new SentinelException("服务重启后仍检测到开启的网络访问配置。");
                completed.Add(GetInfo());
            }
            catch (Exception ex)
            {
                completed.Add("一键修复未完成：" + ex.Message);
                throw new SentinelException(string.Join("\n\n", completed), ex);
            }
            return "修复流程完成：LMID 更新及重启后回读、网络设置和本地配置均已核验。\n\n" + string.Join("\n\n", completed);
        }
    }
}
