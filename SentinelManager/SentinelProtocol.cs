using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SentinelManager
{
    public sealed class SentinelException : Exception
    {
        public SentinelException(string message) : base(message) { }
        public SentinelException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class SentinelProtocol
    {
        public const int MaximumResponseBytes = 8 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
        private static readonly string[] RadioNames =
        {
            "accessfromremote_local", "accessfromremote_secure",
            "accessfromremote_split", "accessfromremote_remote"
        };

        private static Regex Pattern(string pattern, RegexOptions options = RegexOptions.None)
        {
            return new Regex(pattern, options | RegexOptions.CultureInvariant, RegexTimeout);
        }

        public static string DecodeResponse(byte[] bytes)
        {
            if (bytes == null)
                throw new SentinelException("Sentinel 响应缺少正文。");
            if (bytes.Length > MaximumResponseBytes)
                throw new SentinelException("Sentinel 响应过大，已停止读取。");
            try
            {
                int offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                string text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
                CheckResponse(text);
                return text;
            }
            catch (DecoderFallbackException ex)
            {
                throw new SentinelException("Sentinel 响应不是有效的 UTF-8 文本。", ex);
            }
            catch (RegexMatchTimeoutException ex)
            {
                throw new SentinelException("Sentinel 响应解析超时，未继续操作。", ex);
            }
        }

        private static string RemoveScripts(string text)
        {
            return Pattern(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Replace(text, "");
        }

        public static string PlainText(string text)
        {
            if (text == null) return "";
            string plain = RemoveScripts(text);
            plain = Pattern(@"<br\s*/?>|</(?:p|tr|div)\s*>", RegexOptions.IgnoreCase).Replace(plain, "\n");
            return WebUtility.HtmlDecode(Pattern(@"<[^>]*>").Replace(plain, "")).Trim();
        }

        private static IEnumerable<Dictionary<string, string>> InputAttributes(string text)
        {
            string html = Pattern(@"<!--.*?-->", RegexOptions.Singleline).Replace(RemoveScripts(text), "");
            var tags = Pattern("<input\\b(?:\"[^\"]*\"|'[^']*'|[^'\">])*>", RegexOptions.IgnoreCase);
            var attributes = Pattern("(?<name>[^\\s=/'\"<>]+)(?:\\s*=\\s*(?:\"(?<double>[^\"]*)\"|'(?<single>[^']*)'|(?<bare>[^\\s\"'=<>`]+)))?");
            foreach (Match tag in tags.Matches(html))
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string body = tag.Value.Substring(6, tag.Length - 7);
                foreach (Match attribute in attributes.Matches(body))
                {
                    string name = attribute.Groups["name"].Value;
                    if (values.ContainsKey(name))
                        throw new SentinelException("Sentinel 配置包含重复属性，未继续操作。");
                    string value = attribute.Groups["double"].Success ? attribute.Groups["double"].Value
                        : attribute.Groups["single"].Success ? attribute.Groups["single"].Value
                        : attribute.Groups["bare"].Success ? attribute.Groups["bare"].Value : "";
                    values.Add(name, WebUtility.HtmlDecode(value));
                }
                yield return values;
            }
        }

        public static void CheckResponse(string text)
        {
            if (text == null) throw new SentinelException("Sentinel 响应缺少正文。");
            foreach (Match code in Pattern(@"<code\b[^>]*>\s*([+-]?[0-9]+)\s*</code\s*>", RegexOptions.IgnoreCase).Matches(text))
            {
                if (!Pattern(@"^[+-]?0+$").IsMatch(code.Groups[1].Value))
                    throw new SentinelException("Sentinel 返回非零状态，操作未确认成功。");
            }
            if (!text.TrimStart().StartsWith("/*JSON:", StringComparison.Ordinal)
                && Pattern(@"\b(?:ERROR|ACCESS DENIED|UNAUTHORIZED)\b", RegexOptions.IgnoreCase).IsMatch(PlainText(text)))
                throw new SentinelException("Sentinel 返回错误，请检查服务及 ACC 访问权限。");
            foreach (var attributes in InputAttributes(text))
            {
                string type;
                if (attributes.TryGetValue("type", out type) && string.Equals(type, "password", StringComparison.OrdinalIgnoreCase))
                    throw new SentinelException("Sentinel ACC 需要登录；此工具不保存或处理密码。");
            }
        }

        public static List<Dictionary<string, object>> ReadRecords(string text, string kind, params string[] required)
        {
            CheckResponse(text);
            string marker = "/*JSON:" + kind + "*/";
            string trimmed = text.TrimStart();
            if (!trimmed.StartsWith(marker, StringComparison.Ordinal))
                throw new SentinelException("Sentinel " + kind + " 响应格式不受支持。");
            string body = trimmed.Substring(marker.Length).Trim();
            body = Pattern(@"/\*\s*<admin_status>.*?</admin_status>\s*\*/\s*$", RegexOptions.Singleline).Replace(body, "");
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes + 2, RecursionLimit = 64 };
                object[] rows = serializer.DeserializeObject("[" + body + "]") as object[];
                if (rows == null || rows.Length == 0) throw new FormatException("缺少记录数量");
                var summary = rows[rows.Length - 1] as Dictionary<string, object>;
                object countValue;
                int count;
                if (summary == null || !summary.TryGetValue("cnt", out countValue)
                    || !(countValue is string || countValue is int || countValue is long)
                    || !int.TryParse(Convert.ToString(countValue, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out count)
                    || count != rows.Length - 1)
                    throw new FormatException("记录数量不一致");
                var result = new List<Dictionary<string, object>>();
                for (int i = 0; i < count; i++)
                {
                    var row = rows[i] as Dictionary<string, object>;
                    if (row == null) throw new FormatException("记录不是对象");
                    foreach (string field in required)
                    {
                        object value;
                        if (!row.TryGetValue(field, out value) || !(value is string))
                            throw new FormatException("缺少必要的文本字段");
                    }
                    object name;
                    if (kind == "features" && row.TryGetValue("fn", out name) && !(name is string))
                        throw new FormatException("Feature 名称不是文本");
                    result.Add(row);
                }
                return result;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                throw new SentinelException("Sentinel " + kind + " 数据不完整，不能作为有效结果。", ex);
            }
        }

        public static string ReadLmid(string text)
        {
            CheckResponse(text);
            const string marker = "/*JSON:diagnostics*/";
            string trimmed = text.TrimStart();
            if (!trimmed.StartsWith(marker, StringComparison.Ordinal))
                throw new SentinelException("Sentinel LMID 诊断响应格式不受支持。");
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes, RecursionLimit = 64 };
                int identifiers = 0;
                // ACC 诊断对象使用裸字段名，完整保留字符串，只为字段名补充 JSON 引号。
                var tokens = Pattern("(?<str>\"(?:\\\\.|[^\"\\\\\\r\\n])*\")(?<colon>\\s*:)?|(?<key>[A-Za-z_][A-Za-z0-9_]*)\\s*:");
                string json = tokens.Replace(trimmed.Substring(marker.Length), match =>
                {
                    if (match.Groups["key"].Success)
                    {
                        string name = match.Groups["key"].Value;
                        if (name == "srvguid") identifiers++;
                        return serializer.Serialize(name) + ":";
                    }
                    if (match.Groups["colon"].Success
                        && serializer.Deserialize<string>(match.Groups["str"].Value) == "srvguid") identifiers++;
                    return match.Value;
                });
                string structure = Pattern("\"(?:\\\\.|[^\"\\\\\\r\\n])*\"").Replace(json, "\"\"");
                if (structure.IndexOf('\'') >= 0)
                    throw new FormatException("诊断字符串必须使用 JSON 双引号");
                var fields = serializer.DeserializeObject(json) as Dictionary<string, object>;
                object value;
                if (identifiers != 1 || fields == null || !fields.TryGetValue("srvguid", out value)
                    || !(value is string) || !Pattern(@"\A[A-Za-z0-9+/]{40}\z").IsMatch((string)value))
                    throw new FormatException("LMID 字段缺失、重复或格式不符");
                return (string)value;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                throw new SentinelException("Sentinel LMID 数据不完整或格式不符，无法核验。", ex);
            }
        }

        private static Dictionary<string, Dictionary<string, string>> ReadInputs(string text)
        {
            CheckResponse(text);
            var inputs = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var attributes in InputAttributes(text))
            {
                string id;
                if (!attributes.TryGetValue("id", out id) || id.Length == 0) continue;
                // 与原版解析行为一致，同名控件采用页面中最后出现的值。
                inputs[id] = attributes;
            }
            return inputs;
        }

        private static Dictionary<string, string> RequiredInput(Dictionary<string, Dictionary<string, string>> inputs, string id, string type)
        {
            Dictionary<string, string> attributes;
            string actualType;
            if (!inputs.TryGetValue(id, out attributes) || !attributes.TryGetValue("type", out actualType)
                || !string.Equals(actualType, type, StringComparison.OrdinalIgnoreCase))
                throw new SentinelException("Sentinel 远程授权配置格式不受支持，未提交修改。");
            return attributes;
        }

        public static bool[] ReadNetworkState(string outgoingHtml, string incomingHtml)
        {
            var outgoing = ReadInputs(outgoingHtml);
            var incoming = ReadInputs(incomingHtml);
            bool access = RequiredInput(outgoing, "accesstoremote", "checkbox").ContainsKey("checked");
            bool broadcast = RequiredInput(outgoing, "broadcastsearch", "checkbox").ContainsKey("checked");
            int selected = -1;
            for (int i = 0; i < RadioNames.Length; i++)
            {
                if (!RequiredInput(incoming, RadioNames[i], "radio").ContainsKey("checked")) continue;
                if (selected != -1) throw new SentinelException("Sentinel 远程客户端选项异常，未提交修改。");
                selected = i;
            }
            if (selected == -1) throw new SentinelException("Sentinel 远程客户端选项异常，未提交修改。");
            return new[] { access, broadcast, selected != 0 };
        }

        public static string NetworkDisablePayload()
        {
            var payload = new StringBuilder("config\naccesstoremote=0\nbroadcastsearch=0\n");
            for (int i = 0; i < RadioNames.Length; i++)
                payload.Append(RadioNames[i]).Append(i == 0 ? "=1\n" : "=0\n");
            return payload.Append("/config\n").ToString();
        }

        public static bool IsLocalHardware(Dictionary<string, object> device)
        {
            object location, type, configuration, identifier;
            if (!device.TryGetValue("isloc", out location) || !device.TryGetValue("typ", out type)
                || !device.TryGetValue("configuration", out configuration) || !device.TryGetValue("haspid", out identifier)
                || !(location is string) || !(type is string) || !(configuration is string) || !(identifier is string)) return false;
            if ((string)location != "1" || !((string)type).StartsWith("Sentinel HL", StringComparison.Ordinal)) return false;
            bool hardware = Array.IndexOf(((string)configuration).Split(','), "sentinelhl") >= 0;
            string id = (string)identifier;
            bool positive = false;
            foreach (char c in id)
            {
                if (c < '0' || c > '9') return false;
                positive |= c != '0';
            }
            return hardware && positive;
        }
    }
}
