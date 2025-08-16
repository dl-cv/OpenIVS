using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;
using System.Runtime.InteropServices;

namespace DlcvModelRPC
{
    internal static class Program
    {
        // 命名常量
        private const string PipeName = "DlcvModelRpcPipe";
        private const string MmfNamePrefix = "DlcvModelMmf_"; // 后跟token
        private const string MmfMaskPrefix = "DlcvModelMask_"; // 后跟token

        // 模型缓存: model_path -> dlcv_infer_csharp.Model
        private static readonly ConcurrentDictionary<string, Model> s_loadedModels = new ConcurrentDictionary<string, Model>(StringComparer.OrdinalIgnoreCase);

        private static CancellationTokenSource s_cts;
        private static readonly ConcurrentDictionary<string, MemoryMappedFile> s_maskMmfs = new ConcurrentDictionary<string, MemoryMappedFile>();
        private static readonly ManualResetEventSlim s_shutdownEvent = new ManualResetEventSlim(false);
        private static volatile bool s_isShuttingDown = false;
        private static ConsoleCtrlDelegate s_ctrlHandlerDelegate;

        private static void Main(string[] args)
        {
            try { Console.Title = "DlcvModelRPC"; } catch { }
            s_cts = new CancellationTokenSource();
            // Ctrl+C / Ctrl+Break（在无控制台时可能无效，做兼容处理）
            try { Console.CancelKeyPress += (s, e) => { e.Cancel = true; RequestShutdown(); }; } catch { }

            // 进程退出 & 未处理异常
            AppDomain.CurrentDomain.ProcessExit += (s, e) => RequestShutdown();
            AppDomain.CurrentDomain.UnhandledException += (s, e) => RequestShutdown();

            // 控制台窗口关闭 / 注销 / 关机
            TryRegisterConsoleCloseHandler();

            // 启动命名管道服务器(多实例)
            Task.Run(() => RunPipeListenerAsync(s_cts.Token));

            // 简单的心跳输出
            while (!s_cts.IsCancellationRequested)
            {
                Thread.Sleep(1000);
            }

            // 清理
            CleanupResources();
            s_shutdownEvent.Set();
        }

        private static async Task RunPipeListenerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(server, token));
            }
        }

        private static async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            using (pipe)
            using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 8192, true))
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 8192, true) { AutoFlush = true })
            {
                try
                {
                    while (pipe.IsConnected && !token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;

                        JObject request;
                        try { request = JObject.Parse(line); }
                        catch (Exception ex)
                        {
                            await writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = false, error = "bad_json: " + ex.Message })).ConfigureAwait(false);
                            continue;
                        }

                        string action = request.Value<string>("action") ?? string.Empty;
                        try
                        {
                            switch (action)
                            {
                                case "ping":
                                    await writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = true, pong = true })).ConfigureAwait(false);
                                    break;
                                case "load_model":
                                    await HandleLoadModelAsync(request, writer).ConfigureAwait(false);
                                    break;
                                case "get_model_info":
                                    await HandleGetModelInfoAsync(request, writer).ConfigureAwait(false);
                                    break;
                                case "free_model":
                                    await HandleFreeModelAsync(request, writer).ConfigureAwait(false);
                                    break;
                                case "infer":
                                    await HandleInferAsync(request, writer).ConfigureAwait(false);
                                    break;
                                default:
                                    await writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = false, error = "unknown_action" })).ConfigureAwait(false);
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            await writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = false, error = ex.Message })).ConfigureAwait(false);
                        }
                    }
                }
                catch { }
            }
        }

        private static void RequestShutdown()
        {
            if (s_isShuttingDown) return;
            s_isShuttingDown = true;
            try { s_cts?.Cancel(); } catch { }
        }

        private static void CleanupResources()
        {
            // 释放模型
            foreach (var kv in s_loadedModels)
            {
                try { kv.Value.FreeModel(); } catch { }
                try { kv.Value.Dispose(); } catch { }
            }

            // 释放临时mask的MMF
            foreach (var kv in s_maskMmfs)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            s_maskMmfs.Clear();
        }

        // 处理控制台窗口被关闭、注销、关机等事件
        private static void TryRegisterConsoleCloseHandler()
        {
            try
            {
                s_ctrlHandlerDelegate = ConsoleCtrlHandler; // 持有引用，避免GC
                SetConsoleCtrlHandler(s_ctrlHandlerDelegate, true);
            }
            catch { }
        }

        private static bool ConsoleCtrlHandler(CtrlTypes ctrlType)
        {
            // 尽量进行一次有序关闭，避免forrtl 200窗口关闭中止
            RequestShutdown();

            // 等待主循环设置退出标记（最多等待数秒）
            try { s_shutdownEvent.Wait(TimeSpan.FromSeconds(3)); } catch { }
            return true; // 表示我们处理了该事件
        }

        private delegate bool ConsoleCtrlDelegate(CtrlTypes ctrlType);

        private enum CtrlTypes
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        private static Task HandleLoadModelAsync(JObject req, StreamWriter writer)
        {
            string modelPath = req.Value<string>("model_path");
            int deviceId = req.Value<int?>("device_id") ?? 0;
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = false, error = "missing model_path" }));
            }

            var model = s_loadedModels.GetOrAdd(modelPath, p => new Model(p, deviceId));
            return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = true }));
        }

        private static Task HandleGetModelInfoAsync(JObject req, StreamWriter writer)
        {
            string modelPath = req.Value<string>("model_path");
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = false, error = "missing model_path" }));
            }

            if (!s_loadedModels.TryGetValue(modelPath, out var model))
            {
                return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = false, error = "model_not_loaded" }));
            }

            var info = model.GetModelInfo();
            return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = true, model_info = info }));
        }

        private static Task HandleFreeModelAsync(JObject req, StreamWriter writer)
        {
            string modelPath = req.Value<string>("model_path");
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = false, error = "missing model_path" }));
            }

            if (s_loadedModels.TryRemove(modelPath, out var model))
            {
                try { model.FreeModel(); model.Dispose(); } catch { }
            }

            return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = true }));
        }

        private static Task HandleInferAsync(JObject req, StreamWriter writer)
        {
            string modelPath = req.Value<string>("model_path");
            string mmfToken = req.Value<string>("mmf_token");
            int width = req.Value<int?>("width") ?? 0;
            int height = req.Value<int?>("height") ?? 0;
            int channels = req.Value<int?>("channels") ?? 3;
            var paramsJson = req.Value<JObject>("params_json");

            if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(mmfToken) || width <= 0 || height <= 0)
            {
                return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = false, error = "bad_args" }));
            }

            if (!s_loadedModels.TryGetValue(modelPath, out var model))
            {
                return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = false, error = "model_not_loaded" }));
            }

            string mmfName = MmfNamePrefix + mmfToken;
            try
            {
                using (var mmf = MemoryMappedFile.OpenExisting(mmfName, MemoryMappedFileRights.ReadWrite))
                using (var accessor = mmf.CreateViewAccessor(0, width * height * channels, MemoryMappedFileAccess.Read))
                {
                    byte[] buffer = new byte[width * height * channels];
                    accessor.ReadArray(0, buffer, 0, buffer.Length);

                    // 构建Mat (假设输入为BGR 8UC3 或灰度)，用 FromPixelData 生成临时视图再 Clone
                    Mat mat;
                    var matType = channels == 1 ? MatType.CV_8UC1 : MatType.CV_8UC3;
                    using (var matView = Mat.FromPixelData(height, width, matType, buffer))
                    {
                        mat = matView.Clone();
                    }

                    // 执行推理
                    var result = model.Infer(mat, paramsJson);
                    mat.Dispose();

                    // 返回JSON结果（mat结果若有，将由客户端另建MMF读取；这里先只返回JSON）
                    var sampleResultsArray = new JArray();
                    foreach (var sr in result.SampleResults)
                    {
                        var resultsArray = new JArray();
                        foreach (var r in sr.Results)
                        {
                            var jobj = new JObject
                            {
                                ["category_id"] = r.CategoryId,
                                ["category_name"] = r.CategoryName,
                                ["score"] = r.Score,
                                ["area"] = r.Area,
                                ["bbox"] = JArray.FromObject(r.Bbox),
                                ["with_bbox"] = r.WithBbox,
                                ["with_angle"] = r.WithAngle,
                                ["angle"] = r.Angle,
                                ["with_mask"] = r.WithMask
                            };

                            if (r.WithMask && r.Mask != null && !r.Mask.Empty())
                            {
                                var mask = r.Mask;
                                Mat maskCont = mask.IsContinuous() ? mask : mask.Clone();
                                try
                                {
                                    int mw = maskCont.Width;
                                    int mh = maskCont.Height;
                                    int mbytes = mw * mh;
                                    byte[] mBuf = new byte[mbytes];
                                    Marshal.Copy(maskCont.Data, mBuf, 0, mbytes);
                                    string mtoken = Guid.NewGuid().ToString("N");
                                    string mmfName2 = MmfMaskPrefix + mtoken;
                                    var mmf2 = MemoryMappedFile.CreateNew(mmfName2, mbytes);
                                    s_maskMmfs[mtoken] = mmf2;
                                    using (var acc2 = mmf2.CreateViewAccessor(0, mbytes, MemoryMappedFileAccess.Write))
                                    {
                                        acc2.WriteArray(0, mBuf, 0, mbytes);
                                    }
                                    _ = Task.Run(async () =>
                                    {
                                        try { await Task.Delay(30000); } catch { }
                                        if (s_maskMmfs.TryRemove(mtoken, out var mmfHold))
                                        {
                                            try { mmfHold.Dispose(); } catch { }
                                        }
                                    });

                                    jobj["mask"] = new JObject
                                    {
                                        ["mmf_token"] = mtoken,
                                        ["width"] = mw,
                                        ["height"] = mh
                                    };
                                }
                                finally
                                {
                                    if (!ReferenceEquals(maskCont, mask)) maskCont.Dispose();
                                }
                            }

                            resultsArray.Add(jobj);
                        }
                        sampleResultsArray.Add(new JObject { ["results"] = resultsArray });
                    }

                    var json = new JObject { ["sample_results"] = sampleResultsArray };

                    return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = true, result = json }));
                }
            }
            catch (Exception ex)
            {
                return writer.WriteLineAsync(JsonConvert.SerializeObject(new { ok = false, error = ex.Message }));
            }
        }
    }
}


