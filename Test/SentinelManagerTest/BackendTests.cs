using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SentinelManager;

namespace SentinelManagerTest
{
    public static class BackendTests
    {
        private const string DisablePayload = "config\naccesstoremote=0\nbroadcastsearch=0\n"
            + "accessfromremote_local=1\naccessfromremote_secure=0\naccessfromremote_split=0\naccessfromremote_remote=0\n/config\n";
        private const string ConfigText = "serveraddr=127.0.0.1\r\nbroadcastsearch=0\r\n";
        private const string EmptyDevices = "/*JSON:devices*/{\"cnt\":0}";
        private const string Device = "{\"haspid\":\"123\",\"typ\":\"Sentinel HL Pro\",\"isloc\":\"1\",\"configuration\":\"sentinelhl\"}";

        public static int Run()
        {
            var cases = new List<KeyValuePair<string, Action>>
            {
                Case("默认构造无外部操作", DefaultConstruction),
                Case("UTF-8、BOM 与响应大小限制", EncodingAndSize),
                Case("登录、错误文本与非零状态拒绝", RejectedResponses),
                Case("JSON 数量、字段和状态验证", RecordValidation),
                Case("仅保留本机普通硬件设备", HardwareFilter),
                Case("HTML 属性与网络选项解析", NetworkParsing),
                Case("异常网络表单拒绝", InvalidNetworkForms),
                Case("固定本机地址与请求方法限制", RejectedPaths),
                Case("设备与 Feature 查询", InfoSuccess),
                Case("无设备与网络读取失败报告", InfoPartial),
                Case("服务不可用时不发送请求", InfoServiceFailure),
                Case("启动服务后等待 ACC 就绪", InfoAfterStart),
                Case("服务已运行时查询不重试", InfoNoRetry),
                Case("LMID 仅报告已返回", LmidSuccess),
                Case("LMID 失败不重复提交", LmidFailures),
                Case("网络已关闭时不提交", NetworkNoChange),
                Case("网络修改后等待回读确认", NetworkChange),
                Case("网络回读仍开启时报告失败", NetworkNotApplied),
                Case("网络提交异常不重复提交", NetworkSubmitFailures),
                Case("网络回读异常不重复提交", NetworkReadbackFailure),
                Case("表单验证失败时不提交", NetworkInvalidBeforeWrite),
                Case("补丁预检查不写文件", PatchPrepare),
                Case("非管理员不写文件或修改服务", PatchNoAdministrator),
                Case("无效配置目录拒绝", PatchInvalidDirectory),
                Case("服务缺失、拒绝访问及状态异常", PatchServiceFailures),
                Case("服务已运行时不重复启动", ServiceAlreadyRunning),
                Case("非管理员不能启动已停止服务", ServiceStartNeedsAdministrator),
                Case("暂停服务停止后启动并等待", ServicePaused),
                Case("服务状态等待超时", ServiceWaitTimeout),
                Case("临时配置写入、回读及服务重启", PatchSuccess),
                Case("配置写入失败时不重启", PatchWriteFailure),
                Case("服务重启失败时保留配置及进度", PatchRestartFailure),
                Case("重启等待超时不重复启动", PatchRestartTimeout),
                Case("修复正常流程与读取重试", RepairSuccess),
                Case("修复预检查失败时无修改", RepairPrecheckFailure),
                Case("修复 LMID 失败报告已完成步骤", RepairLmidFailure),
                Case("修复补丁失败报告已完成步骤", RepairPatchFailure),
                Case("修复后网络重新开启时拒绝成功", RepairNetworkReopened),
                Case("修复最终查询失败保留步骤", RepairFinalQueryFailure),
                Case("临时目录完整修复流程", RepairWithTemporaryConfiguration)
            };
            int failed = 0;
            foreach (var test in cases)
            {
                try
                {
                    test.Value();
                    Console.WriteLine("通过：" + test.Key);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("失败：" + test.Key + "\n" + ex);
                }
            }
            Console.WriteLine("后端测试：共 " + cases.Count + " 组，通过 " + (cases.Count - failed) + " 组，失败 " + failed + " 组。");
            return failed == 0 ? 0 : 1;
        }

        private static KeyValuePair<string, Action> Case(string name, Action action)
        {
            return new KeyValuePair<string, Action>(name, action);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), message + "；期望：" + expected + "；实际：" + actual);
        }

        private static void Contains(string text, string expected)
        {
            Check(text != null && text.Contains(expected), "结果缺少文本：" + expected);
        }

        private static SentinelException Reject(Action action, string expected)
        {
            try { action(); }
            catch (SentinelException ex)
            {
                Contains(ex.Message, expected);
                return ex;
            }
            throw new InvalidOperationException("预期失败但操作正常返回：" + expected);
        }

        private static string Outgoing(bool access, bool broadcast)
        {
            return "<input id='accesstoremote' type='checkbox'" + (access ? " checked" : "") + ">"
                + "<input id='broadcastsearch' type='checkbox'" + (broadcast ? " checked='checked'" : "") + ">";
        }

        private static string Incoming(int selected)
        {
            string[] names = { "local", "secure", "split", "remote" };
            var result = new StringBuilder();
            for (int i = 0; i < names.Length; i++)
                result.Append("<input type=radio id='accessfromremote_").Append(names[i]).Append("'")
                    .Append(i == selected ? " checked" : "").Append(">");
            return result.ToString();
        }

        private static void Network(ScriptTransport transport, bool enabled)
        {
            transport.Reply("_int_/conf_to.html", Outgoing(enabled, enabled));
            transport.Reply("_int_/conf_from.html", Incoming(enabled ? 3 : 0));
        }

        private static SentinelClient Client(ScriptTransport transport, FakePatch patch = null, FakeClock clock = null)
        {
            return new SentinelClient(transport, patch ?? new FakePatch(), (clock ?? new FakeClock()).Sleep);
        }

        private static void DefaultConstruction()
        {
            ISentinelClient client = new SentinelClient();
            Check(client != null && new LocalServicePatch() != null, "默认构造失败");
        }

        private static void EncodingAndSize()
        {
            Equal("中文", SentinelProtocol.DecodeResponse(Encoding.UTF8.GetBytes("中文")), "中文解码");
            Equal("中文", SentinelProtocol.DecodeResponse(new byte[] { 0xef, 0xbb, 0xbf, 0xe4, 0xb8, 0xad, 0xe6, 0x96, 0x87 }), "BOM 解码");
            foreach (byte[] invalid in new[] { new byte[] { 0xc0, 0xaf }, new byte[] { 0xe4, 0xb8 }, new byte[] { 0xed, 0xa0, 0x80 } })
                Reject(() => SentinelProtocol.DecodeResponse(invalid), "UTF-8");
            Reject(() => SentinelProtocol.DecodeResponse(null), "正文");
            Reject(() => SentinelProtocol.DecodeResponse(new byte[SentinelProtocol.MaximumResponseBytes + 1]), "响应过大");
            byte[] maximum = new byte[SentinelProtocol.MaximumResponseBytes];
            for (int i = 0; i < maximum.Length; i++) maximum[i] = (byte)'x';
            Equal(maximum.Length, SentinelProtocol.DecodeResponse(maximum).Length, "允许恰好 8 MiB");
        }

        private static void RejectedResponses()
        {
            foreach (string text in new[] { "<input type=password>", "<INPUT value='>' TYPE = 'PASSWORD'>", "<input id='login' type=\"password\" />" })
                Reject(() => SentinelProtocol.CheckResponse(text), "需要登录");
            foreach (string text in new[] { "<code>1</code>", "<code> -2 </code>", "/*<admin_status><code>15</code></admin_status>*/" })
                Reject(() => SentinelProtocol.CheckResponse(text), "非零状态");
            foreach (string text in new[] { "<p>ERROR</p>", "Access Denied", "UNAUTHORIZED" })
                Reject(() => SentinelProtocol.CheckResponse(text), "返回错误");
            SentinelProtocol.CheckResponse("<code>0</code>");
            SentinelProtocol.CheckResponse("<script>ERROR</script><style>ERROR</style>正常");
            Equal("甲&乙\n丙", SentinelProtocol.PlainText("<script>删除</script><p>甲&amp;乙</p><div>丙</div>"), "去除标签与实体解码");
        }

        private static void RecordValidation()
        {
            Equal(0, SentinelProtocol.ReadRecords(EmptyDevices, "devices", "haspid").Count, "空设备列表");
            string valid = "  /*JSON:devices*/" + Device + ",{\"cnt\":\"1\"}/*<admin_status><code>0</code></admin_status>*/";
            Equal(1, SentinelProtocol.ReadRecords(valid, "devices", "haspid", "typ", "isloc", "configuration").Count, "有效记录");
            string[] invalid =
            {
                "/*JSON:devices*/", "/*JSON:devices*/{}", "/*JSON:devices*/{\"cnt\":1}",
                "/*JSON:devices*/{\"cnt\":-1}", "/*JSON:devices*/{\"cnt\":false}", "/*JSON:devices*/{\"cnt\":0.5}",
                "/*JSON:devices*/null,{\"cnt\":1}", "/*JSON:devices*/{\"haspid\":1},{\"cnt\":1}",
                "/*JSON:devices*/{\"haspid\":null},{\"cnt\":1}", "/*JSON:devices*/{},{\"cnt\":1}",
                "/*JSON:devices*/{\"cnt\":0}后缀", "/*JSON:devices*/[1],{\"cnt\":1}"
            };
            foreach (string text in invalid) Reject(() => SentinelProtocol.ReadRecords(text, "devices", "haspid"), "数据不完整");
            Reject(() => SentinelProtocol.ReadRecords("<html></html>", "devices", "haspid"), "格式不受支持");
            Reject(() => SentinelProtocol.ReadRecords("/*JSON:features*/{\"fn\":3},{\"cnt\":1}", "features"), "数据不完整");
            Equal(1, SentinelProtocol.ReadRecords("/*JSON:features*/{\"fn\":\"ERROR\"},{\"cnt\":1}", "features").Count, "名称不按错误文本处理");
            Reject(() => SentinelProtocol.ReadRecords(EmptyDevices + "/*<admin_status><code>7</code></admin_status>*/", "devices"), "非零状态");
        }

        private static Dictionary<string, object> Hardware(string id = "123", string location = "1", string type = "Sentinel HL Pro", string configuration = "sentinelhl")
        {
            return new Dictionary<string, object> { { "haspid", id }, { "isloc", location }, { "typ", type }, { "configuration", configuration } };
        }

        private static void HardwareFilter()
        {
            Check(SentinelProtocol.IsLocalHardware(Hardware()), "普通设备应保留");
            Check(SentinelProtocol.IsLocalHardware(Hardware("000123", configuration: "foo,sentinelhl,bar")), "完整配置标记应保留");
            Check(SentinelProtocol.IsLocalHardware(Hardware(new string('9', 80))), "十进制设备编号不受整数溢出影响");
            foreach (string id in new[] { "", "0", "000", "-1", "+1", "１２３", "1/2", "1&x=2", "1\n" })
                Check(!SentinelProtocol.IsLocalHardware(Hardware(id)), "异常设备编号应排除");
            Check(!SentinelProtocol.IsLocalHardware(Hardware(location: "0")), "远程设备应排除");
            Check(!SentinelProtocol.IsLocalHardware(Hardware(type: "Sentinel SL")), "虚拟设备应排除");
            Check(!SentinelProtocol.IsLocalHardware(Hardware(configuration: "sentinelhlmaster")), "主设备应排除");
            Check(!SentinelProtocol.IsLocalHardware(Hardware(configuration: "sentinelhlvirtual")), "非普通配置应排除");
            Check(!SentinelProtocol.IsLocalHardware(new Dictionary<string, object>()), "字段不完整应排除");
        }

        private static void NetworkParsing()
        {
            bool[] state = SentinelProtocol.ReadNetworkState(Outgoing(true, false), Incoming(2));
            Check(state[0] && !state[1] && state[2], "读取三项网络状态");
            string html = "<!--<input id='accesstoremote' type=password>--><script>\"<input type=password>\"</script>"
                + "<INPUT title='a>b' ID=accesstoremote TYPE=CHECKBOX CHECKED='false'>"
                + "<input type=checkbox id=broadcastsearch >";
            state = SentinelProtocol.ReadNetworkState(html, Incoming(0));
            Check(state[0] && !state[1] && !state[2], "HTML 布尔属性以是否存在为准");
            Equal(DisablePayload, SentinelProtocol.NetworkDisablePayload(), "提交正文必须精确匹配");
        }

        private static void InvalidNetworkForms()
        {
            Reject(() => SentinelProtocol.ReadNetworkState("", Incoming(0)), "格式不受支持");
            Reject(() => SentinelProtocol.ReadNetworkState(Outgoing(false, false).Replace("checkbox", "text"), Incoming(0)), "格式不受支持");
            Reject(() => SentinelProtocol.ReadNetworkState(Outgoing(false, false), Incoming(-1)), "选项异常");
            Reject(() => SentinelProtocol.ReadNetworkState(Outgoing(false, false), Incoming(0).Replace("id='accessfromremote_secure'", "id='accessfromremote_secure' checked")), "选项异常");
            Reject(() => SentinelProtocol.ReadNetworkState(Outgoing(false, false) + Outgoing(false, false), Incoming(0)), "重复 ID");
            Reject(() => SentinelProtocol.ReadNetworkState(Outgoing(false, false).Replace("id='accesstoremote'", "id='accesstoremote' id='other'"), Incoming(0)), "重复属性");
            Reject(() => SentinelProtocol.ReadNetworkState(Outgoing(false, false), Incoming(0).Replace("type=radio", "type=text")), "格式不受支持");
        }

        private static void RejectedPaths()
        {
            var transport = new HttpSentinelTransport();
            foreach (string path in new[] { "http://example.invalid/", "//example.invalid/", "../action.html", "tab_feat.html?haspid=1&next=2", "tab_feat.html?haspid=1\n", "action.html", null })
                Reject(() => transport.Send(path, null), "请求路径或方法");
            Reject(() => transport.Send("tab_dev.html", new byte[0]), "请求路径或方法");
            Reject(() => transport.Send("action.html?Create_new_lmid", new byte[0]), "请求路径或方法");
        }

        private static void InfoSuccess()
        {
            var transport = new ScriptTransport();
            string remote = Device.Replace("\"isloc\":\"1\"", "\"isloc\":\"0\"");
            string master = Device.Replace("sentinelhl", "sentinelhlmaster");
            string virtualDevice = Device.Replace("Sentinel HL", "Sentinel SL");
            transport.Reply("tab_dev.html", "/*JSON:devices*/" + Device + "," + remote + "," + master + "," + virtualDevice + ",{\"cnt\":4}");
            transport.Reply("tab_feat.html?haspid=123", "/*JSON:features*/"
                + "{\"haspid\":\"123\",\"fid\":\"1\",\"isloc\":\"1\",\"prid\":\"2\",\"fn\":\"测试&amp;功能\"},"
                + "{\"haspid\":\"123\",\"fid\":\"9\",\"isloc\":\"0\",\"prid\":\"2\"},{\"cnt\":2}");
            Network(transport, false);
            string text = Client(transport).GetInfo();
            Contains(text, "Sentinel 加密狗数量：1");
            Contains(text, "Feature 数量：1");
            Contains(text, "测试&功能");
            Check(!text.Contains("Feature 9"), "远程 Feature 不应混入");
            transport.Done();
        }

        private static void InfoPartial()
        {
            var transport = new ScriptTransport();
            transport.Reply("tab_dev.html", EmptyDevices);
            transport.Fail("_int_/conf_to.html", new TimeoutException());
            string text = Client(transport).GetInfo();
            Contains(text, "未检测到本机");
            Contains(text, "网络配置读取失败");
            transport.Done();
        }

        private static void InfoServiceFailure()
        {
            var transport = new ScriptTransport();
            Reject(() => Client(transport, new FakePatch { Status = "未安装" }).GetInfo(), "服务未运行");
            Equal(0, transport.Calls.Count, "服务不可用时禁止请求");
        }

        private static void InfoAfterStart()
        {
            var transport = new ScriptTransport();
            var patch = new FakePatch { Status = "已停止" };
            var clock = new FakeClock();
            transport.Fail("tab_dev.html", new TimeoutException());
            transport.Reply("tab_dev.html", EmptyDevices);
            Network(transport, false);
            Contains(Client(transport, patch, clock).GetInfo(), "本机 ACC：可访问");
            Equal(1, patch.EnsureCount, "只启动一次");
            Equal(1, clock.SleepCount, "仅等待一次");
            transport.Done();
        }

        private static void InfoNoRetry()
        {
            var transport = new ScriptTransport();
            transport.Fail("tab_dev.html", new TimeoutException());
            Reject(() => Client(transport).GetInfo(), "Sentinel 服务（hasplms）：运行中");
            Equal(1, transport.Calls.Count, "正常查询异常不重试");
            transport.Done();
        }

        private static void LmidSuccess()
        {
            var transport = new ScriptTransport();
            transport.Reply("action.html?Create_new_lmid", "<p>已返回</p>");
            Contains(Client(transport).CreateLmid(), "未确认 LMID 是否变化");
            Equal(1, transport.Calls.Count, "LMID 只提交一次");
            transport.Done();
        }

        private static void LmidFailures()
        {
            foreach (int status in new[] { 302, 401, 403, 500 })
            {
                var transport = new ScriptTransport();
                transport.Reply("action.html?Create_new_lmid", "", status);
                Reject(() => Client(transport).CreateLmid(), "未确认 LMID 是否创建");
                Equal(1, transport.Calls.Count, "HTTP 异常不重复提交");
                transport.Done();
            }
            foreach (string body in new[] { "<input type=password>", "<code>5</code>", "ERROR" })
            {
                var transport = new ScriptTransport();
                transport.Reply("action.html?Create_new_lmid", body);
                Reject(() => Client(transport).CreateLmid(), "未确认 LMID 是否创建");
                transport.Done();
            }
            var timeout = new ScriptTransport();
            timeout.Fail("action.html?Create_new_lmid", new TimeoutException());
            Reject(() => Client(timeout).CreateLmid(), "未确认 LMID 是否创建");
            Equal(1, timeout.Calls.Count, "超时不重复提交");
            timeout.Done();
        }

        private static void NetworkNoChange()
        {
            var transport = new ScriptTransport();
            Network(transport, false);
            Contains(Client(transport).DisableNetwork(), "网络访问已关闭");
            Equal(0, transport.PostCount, "已关闭时不提交");
            transport.Done();
        }

        private static void NetworkChange()
        {
            var transport = new ScriptTransport();
            var clock = new FakeClock();
            Network(transport, true);
            transport.Reply("action.html", "<code>0</code>", payload: DisablePayload);
            Network(transport, true);
            Network(transport, false);
            Contains(Client(transport, clock: clock).DisableNetwork(), "远程客户端访问：关闭");
            Equal(1, transport.PostCount, "网络配置只提交一次");
            Equal(1, clock.SleepCount, "等待回读生效");
            transport.Done();
        }

        private static void NetworkNotApplied()
        {
            var transport = new ScriptTransport();
            Network(transport, true);
            transport.Reply("action.html", "", payload: DisablePayload);
            for (int i = 0; i < 3; i++) Network(transport, true);
            Reject(() => Client(transport).DisableNetwork(), "回读结果仍包含开启选项");
            Equal(1, transport.PostCount, "回读未生效不再次提交");
            transport.Done();
        }

        private static void NetworkSubmitFailures()
        {
            foreach (bool timeout in new[] { false, true })
            {
                var transport = new ScriptTransport();
                Network(transport, true);
                if (timeout) transport.Fail("action.html", new TimeoutException(), DisablePayload);
                else transport.Reply("action.html", "<code>7</code>", payload: DisablePayload);
                Reject(() => Client(transport).DisableNetwork(), "网络设置未全部关闭");
                Equal(1, transport.PostCount, "修改异常不重复提交");
                transport.Done();
            }
        }

        private static void NetworkReadbackFailure()
        {
            var transport = new ScriptTransport();
            Network(transport, true);
            transport.Reply("action.html", "", payload: DisablePayload);
            transport.Fail("_int_/conf_to.html", new TimeoutException());
            Reject(() => Client(transport).DisableNetwork(), "网络设置未全部关闭");
            Equal(1, transport.PostCount, "回读异常不重复提交");
            transport.Done();
        }

        private static void NetworkInvalidBeforeWrite()
        {
            var transport = new ScriptTransport();
            transport.Reply("_int_/conf_to.html", "<p>未知配置</p>");
            transport.Reply("_int_/conf_from.html", Incoming(0));
            Reject(() => Client(transport).DisableNetwork(), "未提交修改");
            Equal(0, transport.PostCount, "异常表单不提交");
            transport.Done();
        }

        private static LocalServicePatch Patch(TempDirectory temp, ScriptCommands commands, bool administrator = true, FakeClock clock = null)
        {
            var time = clock ?? new FakeClock();
            return new LocalServicePatch(temp.Path, commands, () => administrator, time.Sleep, () => time.Seconds);
        }

        private static void PatchPrepare()
        {
            using (var temp = new TempDirectory())
            {
                var commands = new ScriptCommands();
                commands.State(4);
                string target = Patch(temp, commands).Prepare();
                Equal(temp.Target, target, "配置路径");
                Check(!Directory.Exists(System.IO.Path.GetDirectoryName(target)), "预检查不能创建配置目录");
                commands.Done();
            }
        }

        private static void PatchNoAdministrator()
        {
            using (var temp = new TempDirectory())
            {
                var commands = new ScriptCommands();
                Reject(() => Patch(temp, commands, false).Apply(), "尚未写入配置或重启服务");
                Check(!File.Exists(temp.Target), "非管理员不能创建文件");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(temp.Target));
                File.WriteAllText(temp.Target, "原有测试配置");
                Reject(() => Patch(temp, commands, false).Apply(), "需要管理员权限");
                Equal("原有测试配置", File.ReadAllText(temp.Target), "非管理员不能覆盖已有文件");
                Equal(0, commands.Calls.Count, "权限检查早于服务命令");
            }
        }

        private static void PatchInvalidDirectory()
        {
            foreach (string path in new[] { "", "relative", "C:relative", "\\relative" })
            {
                var commands = new ScriptCommands();
                var patch = new LocalServicePatch(path, commands, () => true);
                Reject(() => patch.Prepare(), "LocalAppData");
                Equal(0, commands.Calls.Count, "无效目录不查询服务");
            }
        }

        private static void PatchServiceFailures()
        {
            using (var temp = new TempDirectory())
            {
                foreach (int code in new[] { 1060, 5, 999 })
                {
                    var commands = new ScriptCommands();
                    commands.Result("query", code);
                    Reject(() => Patch(temp, commands).Apply(), code == 1060 ? "未安装" : code == 5 ? "控制权限" : "999");
                    Check(!File.Exists(temp.Target), "服务检查失败不能写文件");
                    commands.Done();
                }
                var malformed = new ScriptCommands();
                malformed.Result("query", 0, "未知状态格式");
                Reject(() => Patch(temp, malformed).Apply(), "状态格式不受支持");
                malformed.Done();
                var timeout = new ScriptCommands();
                timeout.Failure("query", new TimeoutException());
                Reject(() => Patch(temp, timeout).Prepare(), "失败或超时");
                timeout.Done();
            }
        }

        private static void ServiceAlreadyRunning()
        {
            using (var temp = new TempDirectory())
            {
                var commands = new ScriptCommands();
                commands.State(4);
                Check(!Patch(temp, commands, false).EnsureRunning(), "运行中的服务不需要管理员启动");
                Equal("query", string.Join(",", commands.Calls), "只允许查询");
                commands.Done();
            }
        }

        private static void ServiceStartNeedsAdministrator()
        {
            using (var temp = new TempDirectory())
            {
                var commands = new ScriptCommands();
                commands.State(1);
                Reject(() => Patch(temp, commands, false).EnsureRunning(), "以管理员身份运行");
                Equal("query", string.Join(",", commands.Calls), "不执行启动命令");
                commands.Done();
            }
        }

        private static void ServicePaused()
        {
            using (var temp = new TempDirectory())
            {
                var commands = new ScriptCommands();
                commands.State(7);
                commands.Result("stop", 1062);
                commands.State(3);
                commands.State(1);
                commands.Result("start", 1056);
                commands.State(2);
                commands.State(4);
                var clock = new FakeClock();
                Check(Patch(temp, commands, clock: clock).EnsureRunning(), "暂停服务应完成停止和启动");
                Equal(2, clock.SleepCount, "等待停止与启动状态");
                Equal("query,stop,query,query,start,query,query", string.Join(",", commands.Calls), "服务命令顺序");
                commands.Done();
            }
        }

        private static void ServiceWaitTimeout()
        {
            using (var temp = new TempDirectory())
            {
                var commands = new ScriptCommands { RepeatedState = 2 };
                var clock = new FakeClock();
                Reject(() => Patch(temp, commands, clock: clock).EnsureRunning(), "切换状态超时");
                Check(clock.Seconds >= 30 && clock.Seconds < 31, "等待上限为 30 秒");
                Check(commands.Calls.TrueForAll(action => action == "query"), "等待期间只读服务状态");
            }
        }

        private static void RestartScript(ScriptCommands commands)
        {
            commands.State(4);
            commands.State(4);
            commands.Result("stop", 0);
            commands.State(3);
            commands.State(1);
            commands.Result("start", 0);
            commands.State(2);
            commands.State(4);
        }

        private static void PatchSuccess()
        {
            using (var temp = new TempDirectory())
            {
                var commands = new ScriptCommands();
                RestartScript(commands);
                Contains(Patch(temp, commands).Apply(), "已重启并确认运行中");
                Equal(ConfigText, Encoding.ASCII.GetString(File.ReadAllBytes(temp.Target)), "配置内容和 CRLF 编码");
                Equal(ConfigText.Length, File.ReadAllBytes(temp.Target).Length, "配置不能包含 BOM");
                commands.Done();
            }
        }

        private static void PatchWriteFailure()
        {
            using (var temp = new TempDirectory())
            {
                Directory.CreateDirectory(temp.Target);
                var commands = new ScriptCommands();
                commands.State(4);
                Reject(() => Patch(temp, commands).Apply(), "未执行服务重启");
                Equal("query", string.Join(",", commands.Calls), "写入失败不能重启");
                commands.Done();
            }
        }

        private static void PatchRestartFailure()
        {
            using (var temp = new TempDirectory())
            {
                var commands = new ScriptCommands();
                commands.State(4);
                commands.State(4);
                commands.Result("stop", 5);
                var failure = Reject(() => Patch(temp, commands).Apply(), "配置已写入");
                Contains(failure.Message, "服务重启未完成");
                Equal(ConfigText, File.ReadAllText(temp.Target), "重启失败保留已写入内容");
                Equal(1, commands.Calls.FindAll(action => action == "stop").Count, "停止命令不重试");
                commands.Done();
            }
        }

        private static void PatchRestartTimeout()
        {
            using (var temp = new TempDirectory())
            {
                var commands = new ScriptCommands { RepeatedState = 2 };
                commands.State(1);
                commands.State(1);
                commands.Result("start", 0);
                var failure = Reject(() => Patch(temp, commands).Apply(), "配置已写入");
                Contains(failure.Message, "切换状态超时");
                Equal(1, commands.Calls.FindAll(action => action == "start").Count, "启动命令不重试");
                Check(File.Exists(temp.Target), "超时保留配置");
                commands.Done();
            }
        }

        private static void RepairPrefix(ScriptTransport transport)
        {
            Network(transport, false);
            Network(transport, false);
        }

        private static void RepairSuccess()
        {
            var transport = new ScriptTransport();
            var patch = new FakePatch();
            transport.Fail("_int_/conf_to.html", new TimeoutException());
            RepairPrefix(transport);
            transport.Reply("action.html?Create_new_lmid", "已返回");
            Network(transport, false);
            transport.Reply("tab_dev.html", EmptyDevices);
            Network(transport, false);
            string text = Client(transport, patch).Repair();
            Contains(text, "修复流程完成");
            Contains(text, "LMID 是否变化仍需在 ACC 核实");
            Equal(1, patch.PrepareCount, "修复预检查次数");
            Equal(1, patch.EnsureCount, "修复启动次数");
            Equal(1, patch.ApplyCount, "补丁仅执行一次");
            Equal(1, transport.Calls.FindAll(call => call.Path == "action.html?Create_new_lmid").Count, "修复只提交一次 LMID");
            transport.Done();
        }

        private static void RepairPrecheckFailure()
        {
            var transport = new ScriptTransport();
            var patch = new FakePatch { PrepareFailure = new SentinelException("需要管理员权限") };
            Reject(() => Client(transport, patch).Repair(), "需要管理员权限");
            Equal(0, transport.Calls.Count, "预检查失败不发送请求");
            Equal(0, patch.EnsureCount + patch.ApplyCount, "预检查失败不修改服务");
        }

        private static void RepairLmidFailure()
        {
            var transport = new ScriptTransport();
            var patch = new FakePatch();
            RepairPrefix(transport);
            transport.Fail("action.html?Create_new_lmid", new TimeoutException());
            var failure = Reject(() => Client(transport, patch).Repair(), "一键修复未完成");
            Contains(failure.Message, "服务：运行中");
            Contains(failure.Message, "网络设置：");
            Contains(failure.Message, "未确认 LMID 是否创建");
            Equal(0, patch.ApplyCount, "LMID 异常不能继续补丁");
            transport.Done();
        }

        private static void RepairPatchFailure()
        {
            var transport = new ScriptTransport();
            var patch = new FakePatch { ApplyFailure = new SentinelException("配置已写入；服务重启未完成") };
            RepairPrefix(transport);
            transport.Reply("action.html?Create_new_lmid", "");
            var failure = Reject(() => Client(transport, patch).Repair(), "配置已写入");
            Contains(failure.Message, "网络设置：");
            Contains(failure.Message, "LMID：请求已返回");
            Check(!failure.Message.Contains("修复流程完成"), "失败不能报告修复成功");
            transport.Done();
        }

        private static void RepairNetworkReopened()
        {
            var transport = new ScriptTransport();
            RepairPrefix(transport);
            transport.Reply("action.html?Create_new_lmid", "");
            Network(transport, true);
            Reject(() => Client(transport).Repair(), "服务重启后仍检测到开启");
            transport.Done();
        }

        private static void RepairFinalQueryFailure()
        {
            var transport = new ScriptTransport();
            RepairPrefix(transport);
            transport.Reply("action.html?Create_new_lmid", "");
            Network(transport, false);
            transport.Fail("tab_dev.html", new TimeoutException());
            var failure = Reject(() => Client(transport).Repair(), "一键修复未完成");
            Contains(failure.Message, "测试配置已完成");
            Contains(failure.Message, "LMID：请求已返回");
            transport.Done();
        }

        private static void RepairWithTemporaryConfiguration()
        {
            using (var temp = new TempDirectory())
            {
                var commands = new ScriptCommands();
                commands.State(1);
                commands.State(1);
                commands.Result("start", 0);
                commands.State(2);
                commands.State(4);
                RestartScript(commands);
                commands.State(4);
                var transport = new ScriptTransport();
                RepairPrefix(transport);
                transport.Reply("action.html?Create_new_lmid", "");
                Network(transport, false);
                transport.Reply("tab_dev.html", EmptyDevices);
                Network(transport, false);
                var clock = new FakeClock();
                var client = new SentinelClient(transport, Patch(temp, commands, clock: clock), clock.Sleep);
                Contains(client.Repair(), "修复流程完成");
                Equal(ConfigText, File.ReadAllText(temp.Target), "完整修复写入临时配置");
                Equal(2, commands.Calls.FindAll(action => action == "start").Count, "首次启动和配置后重启各一次");
                commands.Done();
                transport.Done();
            }
        }

        private sealed class FakeClock
        {
            public double Seconds;
            public int SleepCount;
            public void Sleep(TimeSpan duration) { Seconds += duration.TotalSeconds; SleepCount++; }
        }

        private sealed class FakePatch : ILocalServicePatch
        {
            public string Status = "运行中";
            public int PrepareCount, EnsureCount, ApplyCount;
            public Exception PrepareFailure, ApplyFailure;
            public string ReadServiceStatus() { return Status; }
            public string Prepare()
            {
                PrepareCount++;
                if (PrepareFailure != null) throw PrepareFailure;
                return "测试目录";
            }
            public bool EnsureRunning() { EnsureCount++; Status = "运行中"; return true; }
            public string Apply()
            {
                ApplyCount++;
                if (ApplyFailure != null) throw ApplyFailure;
                return "测试配置已完成";
            }
        }

        private sealed class RequestCall
        {
            public string Path;
            public byte[] Body;
        }

        private sealed class ScriptTransport : ISentinelTransport
        {
            private readonly Queue<Func<string, byte[], SentinelResponse>> expected = new Queue<Func<string, byte[], SentinelResponse>>();
            public readonly List<RequestCall> Calls = new List<RequestCall>();
            public int PostCount { get { return Calls.FindAll(call => call.Body != null).Count; } }
            public void Reply(string path, string text, int status = 200, string payload = null)
            {
                expected.Enqueue((actual, body) =>
                {
                    MatchRequest(path, payload, actual, body);
                    return new SentinelResponse(status, Encoding.UTF8.GetBytes(text));
                });
            }
            public void Fail(string path, Exception exception, string payload = null)
            {
                expected.Enqueue((actual, body) => { MatchRequest(path, payload, actual, body); throw exception; });
            }
            private static void MatchRequest(string expectedPath, string payload, string actual, byte[] body)
            {
                Equal(expectedPath, actual, "请求路径和顺序");
                Equal(payload, body == null ? null : new UTF8Encoding(false, true).GetString(body), "请求方法及正文");
            }
            public SentinelResponse Send(string path, byte[] body)
            {
                Calls.Add(new RequestCall { Path = path, Body = body });
                Check(expected.Count > 0, "发生未安排的 HTTP 请求");
                return expected.Dequeue()(path, body);
            }
            public void Done() { Equal(0, expected.Count, "全部预期 HTTP 请求应执行"); }
        }

        private sealed class ScriptCommands : ISentinelServiceCommands
        {
            private readonly Queue<Func<string, ServiceCommandResult>> expected = new Queue<Func<string, ServiceCommandResult>>();
            public readonly List<string> Calls = new List<string>();
            public int? RepeatedState;
            public void State(int state) { Result("query", 0, "SERVICE_NAME: hasplms\r\n        STATE              : " + state + " TEST\r\n"); }
            public void Result(string action, int code, string output = "")
            {
                expected.Enqueue(actual => { Equal(action, actual, "服务命令顺序"); return new ServiceCommandResult(code, output); });
            }
            public void Failure(string action, Exception failure)
            {
                expected.Enqueue(actual => { Equal(action, actual, "服务命令顺序"); throw failure; });
            }
            public ServiceCommandResult Run(string action)
            {
                Calls.Add(action);
                if (expected.Count > 0) return expected.Dequeue()(action);
                Check(RepeatedState.HasValue && action == "query", "发生未安排的服务命令");
                return new ServiceCommandResult(0, "STATE : " + RepeatedState.Value);
            }
            public void Done() { Equal(0, expected.Count, "全部预期服务命令应执行"); }
        }

        private sealed class TempDirectory : IDisposable
        {
            public string Path { get; private set; }
            public string Target { get { return System.IO.Path.Combine(Path, "SafeNet Sentinel", "Sentinel LDK", "hasp_26146.ini"); } }
            public TempDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SentinelBackendTests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }
            public void Dispose()
            {
                string root = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar)
                    + System.IO.Path.DirectorySeparatorChar;
                string target = System.IO.Path.GetFullPath(Path);
                Check(target.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && System.IO.Path.GetFileName(target).StartsWith("SentinelBackendTests-", StringComparison.Ordinal), "清理范围必须位于专用临时目录");
                if (Directory.Exists(target)) Directory.Delete(target, true);
            }
        }
    }
}
