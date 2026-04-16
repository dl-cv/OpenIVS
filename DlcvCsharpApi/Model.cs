using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using System.Net.Http;
using System.IO;
using System.Diagnostics;
using System.IO.Pipes;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Linq;
using DlcvModules;

namespace dlcv_infer_csharp
{
    public class Model : IDisposable
    {
        public static bool EnableConsoleLog { get; set; } = true;

        private static void Log(string message)
        {
            if (EnableConsoleLog)
            {
                Console.WriteLine(message);
            }
        }

        public int modelIndex = -1;
        /// <summary>
        /// 是否拥有当前实例所绑定“底层模型资源”的释放权。
        /// - true（默认）：Dispose/FreeModel 时会释放底层模型
        /// - false：Dispose/FreeModel 时不会释放底层模型（用于“共享/借用模型”的场景）
        /// </summary>
        public bool OwnModelIndex { get; set; } = true;

        // DVP mode fields
        private bool _isDvpMode = false;
        private bool _isDvsMode = false;
        private DlcvModules.DvsModel _dvsModel;
        private bool _isRpcMode = false;
        private string _modelPath;
        private string _serverUrl = "http://127.0.0.1:9890";
        private HttpClient _httpClient;
        private bool _disposed = false;
        private readonly string _rpcPipeName = "DlcvModelRpcPipe";
        private static readonly string[] _rpcExecutableFixedPaths = new string[]
        {
            @"C:\dlcv\Lib\site-packages\dlcvpro_infer_csharp\AIModelRPC.exe"
        };

        // 模型缓存（按模型路径+设备+模式区分）
        private static readonly Dictionary<string, int> _modelCache = new Dictionary<string, int>();
        private static readonly HashSet<string> _loadingModels = new HashSet<string>();
        private static readonly object _cacheLock = new object();

        // 缓存模型信息（加载后即读取一次）
        private JObject _cachedModelInfo = null;
        private JArray _cachedMaxShape = null;
        private int _cachedMaxBatchSize = 1;
        private int _expectedChCache = -2;
        private readonly object _batchMetaLock = new object();
        private List<JObject> _cachedSubModelBatchItems = new List<JObject>();

        public Model()
        {

        }

        public Model(string modelPath, int device_id, bool rpc_mode = false, bool enableCache = false)
        {
            _modelPath = modelPath;

            // 根据模型文件后缀判断是否使用 DVP 模式
            if (string.IsNullOrEmpty(modelPath))
            {
                throw new ArgumentException("模型路径不能为空", nameof(modelPath));
            }

            string extension = Path.GetExtension(modelPath).ToLower();
            _isDvpMode = extension == ".dvp";
            _isDvsMode = extension == ".dvst" || extension == ".dvso" || extension == ".dvsp";
            _isRpcMode = rpc_mode;
            string cacheKey = BuildModelCacheKey(modelPath, device_id, _isDvpMode, _isDvsMode, _isRpcMode);

            if (enableCache)
            {
                while (true)
                {
                    lock (_cacheLock)
                    {
                        int cachedIndex;
                        if (_modelCache.TryGetValue(cacheKey, out cachedIndex))
                        {
                            modelIndex = cachedIndex;
                            TryCacheModelInfo();
                            return;
                        }
                        if (!_loadingModels.Contains(cacheKey))
                        {
                            _loadingModels.Add(cacheKey);
                            break;
                        }
                    }
                    System.Threading.Thread.Sleep(10);
                }
            }

            try
            {
                if (_isDvpMode)
                {
                    // DVP 模式：使用 HTTP API
                    InitializeDvpMode(modelPath, device_id);
                }
                else if (_isDvsMode)
                {
                    InitializeDvsMode(modelPath, device_id);
                }
                else if (_isRpcMode)
                {
                    // DVO/DVT RPC模式：使用本地RPC（命名管道+共享内存）
                    InitializeRpcMode(modelPath, device_id);
                }
                else
                {
                    // DVT 模式：使用原来的 DLL 接口
                    InitializeDvtMode(modelPath, device_id);
                }

                // 模型加载成功后立即读取并缓存 model_info/max_shape/max_batch_size
                TryCacheModelInfo();

                if (enableCache)
                {
                    lock (_cacheLock)
                    {
                        _modelCache[cacheKey] = modelIndex;
                        _loadingModels.Remove(cacheKey);
                    }
                }
            }
            catch
            {
                if (enableCache)
                {
                    lock (_cacheLock)
                    {
                        _loadingModels.Remove(cacheKey);
                    }
                }
                throw;
            }
        }

        private static string BuildModelCacheKey(string modelPath, int deviceId, bool isDvpMode, bool isDvsMode, bool isRpcMode)
        {
            string mode = isDvpMode ? "dvp" : (isDvsMode ? "dvs" : (isRpcMode ? "rpc" : "dvt"));
            string normalizedPath = Path.GetFullPath(modelPath).Trim().ToLowerInvariant();
            return $"{normalizedPath}|{deviceId}|{mode}";
        }

        public List<JObject> GetResolvedSubModelBatchItems()
        {
            lock (_batchMetaLock)
            {
                var subModels = new List<JObject>();
                foreach (var item in _cachedSubModelBatchItems)
                {
                    if (item != null)
                    {
                        subModels.Add((JObject)item.DeepClone());
                    }
                }
                return subModels;
            }
        }

        private void InitializeDvpMode(string modelPath, int device_id)
        {
            EnsureDvpHttpClient();

            // 检查后端服务是否启动
            if (!CheckBackendService())
            {
                // 启动后端服务
                StartBackendService();

                // 循环等待后端服务启动完成
                Log("正在等待后端服务启动...");
                WaitForBackendService();
            }

            // 加载模型到服务器
            try
            {
                var request = new
                {
                    model_path = modelPath
                };

                string jsonContent = JsonConvert.SerializeObject(request);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = _httpClient.PostAsync($"{_serverUrl}/load_model", content).Result;
                var responseJson = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"加载模型失败: {response.StatusCode} - {responseJson}");
                }

                var resultObject = JObject.Parse(responseJson);

                if (resultObject.ContainsKey("code") &&
                    resultObject["code"].Value<string>() == "00000")
                {
                    Log($"Model load result: {resultObject}");
                    modelIndex = 1; // DVP模式设置默认值表示模型已加载

                    // 模型加载成功后，调用 /version 接口
                    CallVersionAPI();
                }
                else
                {
                    string errorCode = resultObject.ContainsKey("code") ?
                        resultObject["code"].Value<string>() : "未知错误码";
                    throw new Exception($"加载模型失败，错误码: {errorCode}，详细信息：{resultObject}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"加载模型失败: {ex.Message}", ex);
            }
        }

        private void EnsureDvpHttpClient()
        {
            if (_httpClient == null)
            {
                _httpClient = new HttpClient();
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
            }
        }

        private void InitializeDvsMode(string modelPath, int device_id)
        {
            _dvsModel = new DlcvModules.DvsModel();
            try
            {
                var report = _dvsModel.Load(modelPath, device_id);
                int code = report != null && report["code"] != null ? (int)report["code"] : 1;
                if (code != 0)
                {
                    string msg = report != null ? report.ToString() : "Unknown error";
                    throw new Exception("DVS模型加载失败:\n" + msg);
                }
                modelIndex = 1; // 标记为已加载
            }
            catch (Exception ex)
            {
                _dvsModel.Dispose();
                _dvsModel = null;
                throw new Exception($"加载 DVS 模型失败: {ex.Message}", ex);
            }
        }

        private void InitializeDvtMode(string modelPath, int device_id)
        {
            var config = new JObject
            {
                ["model_path"] = modelPath,
                ["device_id"] = device_id
            };

            var setting = new JsonSerializerSettings() { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii };

            string jsonStr = JsonConvert.SerializeObject(config, setting);

            IntPtr resultPtr = DllLoader.Instance.dlcv_load_model(jsonStr);
            var resultJson = Marshal.PtrToStringAnsi(resultPtr);
            var resultObject = JObject.Parse(resultJson);

            Log("Model load result: " + resultObject.ToString());
            if (resultObject.ContainsKey("model_index"))
            {
                modelIndex = resultObject["model_index"].Value<int>();
            }
            else
            {
                throw new Exception("加载模型失败：" + resultObject.ToString());
            }
            DllLoader.Instance.dlcv_free_result(resultPtr);
        }

        /// <summary>
        /// 检查后端服务是否已启动
        /// </summary>
        /// <returns>服务是否可用</returns>
        private bool CheckBackendService()
        {
            try
            {
                EnsureDvpHttpClient();
                var response = _httpClient.GetAsync($"{_serverUrl}/docs").GetAwaiter().GetResult();
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 启动后端服务程序
        /// </summary>
        private void StartBackendService()
        {
            try
            {
                string backendExePath = @"C:\dlcv\Lib\site-packages\dlcv_test\DLCV Test.exe";

                if (!File.Exists(backendExePath))
                {
                    throw new FileNotFoundException($"找不到后端服务程序: {backendExePath}");
                }

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = backendExePath,
                    Arguments = "--keep_alive",
                    WorkingDirectory = Path.GetDirectoryName(backendExePath),
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                Process.Start(processStartInfo);
                Log($"已启动后端推理服务: {backendExePath}");
            }
            catch (Exception ex)
            {
                throw new Exception($"启动后端服务失败: {ex.Message}", ex);
            }
        }

        // ===== RPC 本地服务支持 =====
        private void InitializeRpcMode(string modelPath, int device_id)
        {
            // 确保服务可用
            if (!CheckRpcService())
            {
                StartRpcService();
                WaitForRpcService();
            }

            // 加载模型
            var req = new JObject
            {
                ["action"] = "load_model",
                ["model_path"] = modelPath,
                ["device_id"] = device_id
            };
            var resp = SendRpc(req);
            if (resp == null || !(resp["ok"]?.Value<bool>() ?? false))
            {
                string err = resp != null ? resp["error"]?.ToString() : "rpc_no_response";
                throw new Exception($"RPC 加载模型失败: {err}");
            }
            modelIndex = 1; // 标记为已加载
        }

        private bool CheckRpcService()
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", _rpcPipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    client.Connect(200);
                    using (var writer = new StreamWriter(client, new UTF8Encoding(false), 8192, true) { AutoFlush = true })
                    using (var reader = new StreamReader(client, Encoding.UTF8, false, 8192, true))
                    {
                        var ping = new JObject { ["action"] = "ping" };
                        writer.WriteLine(ping.ToString(Formatting.None));
                        var line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line)) return false;
                        var resp = JObject.Parse(line);
                        return resp["pong"]?.Value<bool>() ?? false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private void StartRpcService()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new List<string>
                {
                    Path.Combine(baseDir, "AIModelRPC.exe")
                };

                // 仅按固定列表顺序追加
                candidates.AddRange(_rpcExecutableFixedPaths);

                var orderedUnique = candidates
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .Distinct()
                    .ToList();

                string exePath = orderedUnique.FirstOrDefault(File.Exists);
                if (string.IsNullOrEmpty(exePath))
                {
                    throw new FileNotFoundException("找不到 AIModelRPC.exe 文件。尝试路径: " + string.Join(" | ", orderedUnique));
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
                Log($"已启动 AIModelRPC 服务: {exePath}");
            }
            catch (Exception ex)
            {
                throw new Exception($"启动 AIModelRPC 失败: {ex.Message}", ex);
            }
        }

        private void WaitForRpcService()
        {
            const int maxWaitMs = 15000;
            const int stepMs = 200;
            int waited = 0;
            while (waited < maxWaitMs)
            {
                if (CheckRpcService()) return;
                System.Threading.Thread.Sleep(stepMs);
                waited += stepMs;
            }
            throw new Exception("等待RPC服务启动超时");
        }

        private JObject SendRpc(JObject req, int timeoutMs = 300000)
        {
            using (var client = new NamedPipeClientStream(".", _rpcPipeName, PipeDirection.InOut, PipeOptions.None))
            {
                client.Connect(timeoutMs);
                using (var writer = new StreamWriter(client, new UTF8Encoding(false), 8192, true) { AutoFlush = true })
                using (var reader = new StreamReader(client, Encoding.UTF8, false, 8192, true))
                {
                    writer.WriteLine(req.ToString(Formatting.None));
                    string line = reader.ReadLine();
                    if (line == null)
                    {
                        throw new Exception("RPC通信失败：未收到任何响应（line为null）");
                    }
                    if (string.IsNullOrEmpty(line)) return null;
                    return JObject.Parse(line);
                }
            }
        }

        /// <summary>
        /// 调用后端服务的 /version 接口
        /// </summary>
        private void CallVersionAPI()
        {
            try
            {
                EnsureDvpHttpClient();
                var response = _httpClient.GetAsync($"{_serverUrl}/version").GetAwaiter().GetResult();
                var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    Log($"后端版本信息: {responseJson}");
                }
                else
                {
                    Log($"获取版本信息失败: {response.StatusCode} - {responseJson}");
                }
            }
            catch (Exception ex)
            {
                Log($"调用版本接口失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 等待后端服务启动完成
        /// </summary>
        private void WaitForBackendService()
        {
            const int maxWaitTime = 30; // 最大等待30秒
            const double checkInterval = 0.2;
            double waitedTime = 0;

            while (waitedTime < maxWaitTime)
            {
                if (CheckBackendService())
                {
                    Log("后端服务已启动，继续加载模型...");
                    return;
                }

                Log($"等待后端服务启动中... ({waitedTime + checkInterval}/{maxWaitTime}秒)");
                System.Threading.Thread.Sleep((int)(checkInterval * 1000));
                waitedTime += checkInterval;
            }

            throw new Exception($"等待后端服务启动超时（{maxWaitTime}秒），请检查后端服务是否正常启动");
        }

        ~Model()
        {
            Dispose(false);
        }

        public void FreeModel()
        {
            _expectedChCache = -2;

            // 仅借用/共享模式，不释放底层模型，只标记无效
            if (!OwnModelIndex)
            {
                Log("[FreeModel] 共享/借用, 不释放，仅置为-1");
                modelIndex = -1;
                return;
            }

            if (_isDvpMode)
            {
                if (_disposed || modelIndex == -1)
                {
                    Log("[FreeModel][DVP] 已Disposed或modelIndex为-1，无需释放");
                    return;
                }
                try
                {
                    EnsureDvpHttpClient();
                    var request = new { model_index = modelIndex };
                    var content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                    var response = _httpClient.PostAsync($"{_serverUrl}/free_model", content).Result;
                    Log($"[FreeModel][DVP] HTTP释放，状态: {response.StatusCode}");
                    modelIndex = -1;
                }
                catch (Exception ex)
                {
                    Log($"[FreeModel][DVP] 释放失败: {ex.Message}");
                }
            }
            else if (_isDvsMode)
            {
                if (_disposed || modelIndex == -1)
                {
                    Log("[FreeModel][DVS] 已Disposed或modelIndex为-1，无需释放");
                    return;
                }
                _dvsModel?.Dispose();
                _dvsModel = null;
                modelIndex = -1;
                Log("[FreeModel][DVS] FlowGraph已释放");
            }
            else if (_isRpcMode)
            {
                if (_disposed || modelIndex == -1)
                {
                    Log("[FreeModel][RPC] 已Disposed或modelIndex为-1，无需释放");
                    return;
                }
                try
                {
                    var req = new JObject { ["action"] = "free_model", ["model_path"] = _modelPath };
                    SendRpc(req);
                    modelIndex = -1;
                    Log("[FreeModel][RPC] RPC模型已释放");
                }
                catch (Exception ex)
                {
                    Log($"[FreeModel][RPC] 释放失败: {ex.Message}");
                }
            }
            else
            {
                if (modelIndex == -1)
                {
                    Log("[FreeModel][DVT] modelIndex为-1，无需释放");
                    return;
                }
                var config = new JObject { ["model_index"] = modelIndex };
                IntPtr resultPtr = DllLoader.Instance.dlcv_free_model(config.ToString());
                string resultText = Marshal.PtrToStringAnsi(resultPtr);
                DllLoader.Instance.dlcv_free_result(resultPtr);
                Log($"[FreeModel][DVT] DVT模型释放结果: {resultText}");
                modelIndex = -1;
            }
        }

        private void TryCacheModelInfo()
        {
            try
            {
                JObject modelInfo = null;
                if (_isDvpMode)
                {
                    modelInfo = GetModelInfoDvp();
                }
                else if (_isDvsMode)
                {
                    modelInfo = _dvsModel != null ? _dvsModel.GetModelInfo() : null;
                    if (modelInfo != null && _dvsModel != null)
                    {
                        JArray loadedMeta = null;
                        try
                        {
                            loadedMeta = _dvsModel.GetLoadedModelMeta();
                        }
                        catch
                        {
                            loadedMeta = null;
                        }

                        if (loadedMeta != null && loadedMeta.Count > 0)
                        {
                            modelInfo["loaded_model_meta"] = loadedMeta;
                        }
                    }
                }
                else if (_isRpcMode)
                {
                    var req = new JObject
                    {
                        ["action"] = "get_model_info",
                        ["model_path"] = _modelPath
                    };
                    var resp = SendRpc(req);
                    if (resp != null && (resp["ok"]?.Value<bool>() ?? false))
                    {
                        modelInfo = resp["model_info"] as JObject;
                    }
                }
                else
                {
                    modelInfo = GetModelInfoDvt();
                }

                if (modelInfo != null)
                {
                    UpdateModelMetaCache(modelInfo);
                }
            }
            catch
            {
                // 缓存失败不影响模型可用性
            }
        }

        private static JArray ReadMaxShapeToken(JToken token)
        {
            var obj = token as JObject;
            if (obj == null) return null;
            var maxShape = obj["max_shape"] as JArray;
            return maxShape != null && maxShape.Count > 0 ? maxShape : null;
        }

        private static JArray ExtractMaxShapeFromModelInfo(JObject modelInfoRoot)
        {
            if (modelInfoRoot == null) return null;
            var modelInfo = modelInfoRoot["model_info"] as JObject;
            if (modelInfo == null) return null;
            var inputShapes = modelInfo["input_shapes"] as JObject;
            if (inputShapes == null) return null;

            // 优先 input_shapes.input.max_shape
            var preferred = ReadMaxShapeToken(inputShapes["input"]);
            if (preferred != null) return preferred;

            // 退回第一个输入张量的 max_shape
            foreach (var kv in inputShapes)
            {
                var fallback = ReadMaxShapeToken(kv.Value);
                if (fallback != null) return fallback;
            }
            return null;
        }

        private static int ParseMaxBatchSize(JArray maxShape)
        {
            if (maxShape == null || maxShape.Count == 0) return 1;
            try
            {
                int v = maxShape[0].Value<int>();
                return Math.Max(1, v);
            }
            catch
            {
                return 1;
            }
        }

        private static int ParsePositiveIntToken(JToken token)
        {
            if (token == null) return 0;
            try
            {
                int v = token.Value<int>();
                return v > 0 ? v : 0;
            }
            catch
            {
                try
                {
                    if (int.TryParse(token.ToString(), out int parsed) && parsed > 0)
                    {
                        return parsed;
                    }
                }
                catch
                {
                }
                return 0;
            }
        }

        private static string NormalizeDisplayName(string rawNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(rawNameOrPath))
            {
                return string.Empty;
            }

            string text = rawNameOrPath.Trim();
            try
            {
                if (text.IndexOf('\\') >= 0 || text.IndexOf('/') >= 0)
                {
                    string fileName = Path.GetFileName(text);
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        return fileName;
                    }
                }
            }
            catch
            {
            }

            return text;
        }

        private static void AddUniqueSubModelItem(List<JObject> subModelItems, string nameOrPath, JArray maxShape, int maxBatchSize)
        {
            if (subModelItems == null)
            {
                return;
            }

            string name = NormalizeDisplayName(nameOrPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "未命名模型";
            }

            var shape = maxShape != null ? (JArray)maxShape.DeepClone() : null;
            int shapeBatch = ParseMaxBatchSize(shape);
            int resolvedBatch = Math.Max(1, Math.Max(maxBatchSize, shapeBatch));

            string key = name + "|" + (shape != null ? shape.ToString(Formatting.None) : "null");
            foreach (var existed in subModelItems)
            {
                if (existed == null) continue;
                string existedName = (string)existed["name"] ?? string.Empty;
                var existedShape = existed["max_shape"] as JArray;
                string existedKey = existedName + "|" + (existedShape != null ? existedShape.ToString(Formatting.None) : "null");
                if (string.Equals(key, existedKey, StringComparison.Ordinal))
                {
                    return;
                }
            }

            var item = new JObject
            {
                ["name"] = name,
                ["max_shape"] = shape,
                ["max_batch_size"] = resolvedBatch
            };
            subModelItems.Add(item);
        }

        private static JArray ParseMaxShapeToken(JToken maxShapeToken)
        {
            if (maxShapeToken == null) return null;

            if (maxShapeToken is JArray arr && arr.Count > 0)
            {
                return (JArray)arr.DeepClone();
            }

            if (maxShapeToken.Type == JTokenType.String)
            {
                try
                {
                    var parsed = JArray.Parse(maxShapeToken.Value<string>());
                    if (parsed.Count > 0)
                    {
                        return parsed;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        private static void CollectBatchCandidatesFromToken(JToken token, List<int> candidates)
        {
            if (token == null || candidates == null) return;

            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    string key = prop.Name ?? string.Empty;
                    var value = prop.Value;

                    if (string.Equals(key, "max_batch_size", StringComparison.OrdinalIgnoreCase))
                    {
                        int batch = ParsePositiveIntToken(value);
                        if (batch > 0 && !candidates.Contains(batch))
                        {
                            candidates.Add(batch);
                        }
                    }
                    else if (string.Equals(key, "max_shape", StringComparison.OrdinalIgnoreCase))
                    {
                        var shape = ParseMaxShapeToken(value);
                        if (shape != null && shape.Count > 0)
                        {
                            int batch = ParsePositiveIntToken(shape[0]);
                            if (batch > 0 && !candidates.Contains(batch))
                            {
                                candidates.Add(batch);
                            }
                        }
                    }

                    CollectBatchCandidatesFromToken(value, candidates);
                }
                return;
            }

            if (token is JArray jarr)
            {
                foreach (var item in jarr)
                {
                    CollectBatchCandidatesFromToken(item, candidates);
                }
            }
        }

        private static void CollectBatchCandidatesFromLoadedMeta(JObject modelInfoRoot, List<int> candidates, List<JObject> subModelItems)
        {
            if (modelInfoRoot == null || candidates == null) return;
            var loadedMeta = modelInfoRoot["loaded_model_meta"] as JArray;
            if (loadedMeta == null || loadedMeta.Count == 0) return;

            foreach (var token in loadedMeta)
            {
                var obj = token as JObject;
                if (obj == null) continue;

                int maxBatchSize = ParsePositiveIntToken(obj["max_batch_size"]);
                var maxShape = ParseMaxShapeToken(obj["max_shape"]);

                if (maxBatchSize > 0 && !candidates.Contains(maxBatchSize))
                {
                    candidates.Add(maxBatchSize);
                }
                if (maxShape != null && maxShape.Count > 0)
                {
                    int shapeBatch = ParsePositiveIntToken(maxShape[0]);
                    if (shapeBatch > 0 && !candidates.Contains(shapeBatch))
                    {
                        candidates.Add(shapeBatch);
                    }
                }

                string name = (string)obj["model_name"]
                    ?? (string)obj["model_path_original"]
                    ?? (string)obj["model_path"]
                    ?? (string)obj["title"]
                    ?? string.Empty;
                AddUniqueSubModelItem(subModelItems, name, maxShape, maxBatchSize);
            }
        }

        private void UpdateModelMetaCache(JObject modelInfo)
        {
            _cachedModelInfo = modelInfo != null ? (JObject)modelInfo.DeepClone() : null;
            var maxShape = ExtractMaxShapeFromModelInfo(_cachedModelInfo);
            _cachedMaxShape = maxShape != null ? (JArray)maxShape.DeepClone() : null;

            int parsedByMaxShape = ParseMaxBatchSize(_cachedMaxShape);
            var parsedCandidates = new List<int>();
            var parsedSubModelItems = new List<JObject>();

            // flow/dvs：优先用加载期收集到的子模型信息。
            CollectBatchCandidatesFromLoadedMeta(_cachedModelInfo, parsedCandidates, parsedSubModelItems);

            // 非 flow 或 flow 信息缺失时，回退到通用扫描。
            if (parsedCandidates.Count == 0)
            {
                CollectBatchCandidatesFromToken(_cachedModelInfo, parsedCandidates);
            }

            int parsedByModelInfo = 1;
            if (parsedCandidates.Count > 0)
            {
                int minGtOne = parsedCandidates.Where(x => x > 1).DefaultIfEmpty(0).Min();
                if (minGtOne > 1)
                {
                    parsedByModelInfo = minGtOne;
                }
                else
                {
                    int anyPositive = parsedCandidates.DefaultIfEmpty(0).Min();
                    parsedByModelInfo = anyPositive > 0 ? anyPositive : 1;
                }
            }

            int parsedBatch = Math.Max(parsedByMaxShape, parsedByModelInfo);

            // 修复：避免后续一次信息不全的刷新把正确 batch（>1）覆盖回 1。
            if (parsedBatch > 1)
            {
                _cachedMaxBatchSize = parsedBatch;
            }
            else if (_cachedMaxBatchSize <= 1)
            {
                _cachedMaxBatchSize = 1;
            }

            lock (_batchMetaLock)
            {
                _cachedSubModelBatchItems = parsedSubModelItems
                    .Where(x => x != null)
                    .Select(x => (JObject)x.DeepClone())
                    .ToList();
                if (_cachedSubModelBatchItems.Count == 0)
                {
                    AddUniqueSubModelItem(_cachedSubModelBatchItems, _modelPath, _cachedMaxShape, _cachedMaxBatchSize);
                }
            }

            _expectedChCache = -2;
        }

        private static bool IsLikelySpatialDim(int v)
        {
            return v >= 8 && v <= 65536;
        }

        private static int ReadJsonIntLike(JToken v, int dv)
        {
            if (v == null) return dv;
            try
            {
                if (v.Type == JTokenType.Integer) return v.Value<int>();
                if (v.Type == JTokenType.Float) return (int)Math.Round(v.Value<double>());
                if (v.Type == JTokenType.String) return int.Parse(v.Value<string>(), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
            }
            return dv;
        }

        private static int InferChannelCountFromMaxShape(JArray shapeArr)
        {
            if (shapeArr == null || shapeArr.Count == 0) return 0;
            int n = shapeArr.Count;
            if (n >= 4)
            {
                int d1 = ReadJsonIntLike(shapeArr[1], -1);
                int d2 = ReadJsonIntLike(shapeArr[2], -1);
                int d3 = ReadJsonIntLike(shapeArr[3], -1);
                if (IsLikelySpatialDim(d2) && IsLikelySpatialDim(d3))
                {
                    if (d1 == 1 || d1 == 3) return d1;
                }
                int d0 = ReadJsonIntLike(shapeArr[0], -1);
                if (IsLikelySpatialDim(d0) && IsLikelySpatialDim(d1))
                {
                    if (d3 == 1 || d3 == 3) return d3;
                }
            }
            if (n == 3)
            {
                int d0 = ReadJsonIntLike(shapeArr[0], -1);
                int d1 = ReadJsonIntLike(shapeArr[1], -1);
                int d2 = ReadJsonIntLike(shapeArr[2], -1);
                if ((d0 == 1 || d0 == 3) && IsLikelySpatialDim(d1) && IsLikelySpatialDim(d2))
                    return d0;
                if (IsLikelySpatialDim(d0) && IsLikelySpatialDim(d1) && (d2 == 1 || d2 == 3))
                    return d2;
            }
            return 0;
        }

        private static JObject FindInputShapesObject(JObject root)
        {
            if (root == null) return null;
            if (root["model_info"] is JObject mi)
            {
                if (mi["input_shapes"] is JObject ish) return ish;
                if (mi["model_info"] is JObject inner && inner["input_shapes"] is JObject ish2) return ish2;
            }
            if (root["input_shapes"] is JObject ish3) return ish3;
            return null;
        }

        private static int ParseInputChFromModelInfo(JObject modelInfoRoot)
        {
            JObject shapes = FindInputShapesObject(modelInfoRoot);
            if (shapes == null) return 0;

            int TryInputDesc(JToken inputDesc)
            {
                var jo = inputDesc as JObject;
                if (jo == null) return 0;
                var ms = jo["max_shape"] as JArray;
                if (ms == null) return 0;
                int ch = InferChannelCountFromMaxShape(ms);
                return (ch == 1 || ch == 3) ? ch : 0;
            }

            if (shapes["input"] != null)
            {
                int ch = TryInputDesc(shapes["input"]);
                if (ch != 0) return ch;
            }
            foreach (var prop in shapes.Properties())
            {
                int ch = TryInputDesc(prop.Value);
                if (ch != 0) return ch;
            }
            return 0;
        }

        private static MatType Mat8UTypeForChannels(int ch)
        {
            switch (ch)
            {
                case 1: return MatType.CV_8UC1;
                case 2: return MatType.CV_8UC2;
                case 3: return MatType.CV_8UC3;
                case 4: return MatType.CV_8UC4;
                default: return MatType.CV_8UC1;
            }
        }

        private static Mat ConvertMatDepthTo8U(Mat src, List<Mat> disposables)
        {
            if (src == null || src.Empty()) return src;
            int ch = src.Channels();
            MatType targetT = Mat8UTypeForChannels(ch);
            MatType st = src.Type();
            if (st == targetT)
                return src;

            Mat dst = new Mat();
            MatType depth = st.Depth;
            int targetRtype = targetT.ToInt32();
            if (depth == MatType.CV_16U)
            {
                src.ConvertTo(dst, targetT, 1.0 / 256.0);
            }
            else if (depth == MatType.CV_8S || depth == MatType.CV_16S)
            {
                Cv2.Normalize(src, dst, 0, 255, NormTypes.MinMax, targetRtype);
            }
            else if (depth == MatType.CV_32F || depth == MatType.CV_64F)
            {
                Cv2.MinMaxLoc(src, out double min, out double max);
                if (max <= 1.0 + 1e-6 && min >= -1e-6)
                    src.ConvertTo(dst, targetT, 255.0);
                else
                    Cv2.Normalize(src, dst, 0, 255, NormTypes.MinMax, targetRtype);
            }
            else
            {
                src.ConvertTo(dst, targetT);
            }

            disposables.Add(dst);
            return dst;
        }

        private int ResolveEffectiveInputCh()
        {
            if (_expectedChCache != -2)
                return (_expectedChCache == -1) ? 3 : _expectedChCache;
            try
            {
                if (modelIndex < 0 && !_isDvsMode && !_isDvpMode && !_isRpcMode)
                {
                    _expectedChCache = -1;
                    return 3;
                }
                if (_isDvsMode && (_dvsModel == null || !_dvsModel.IsLoaded))
                {
                    _expectedChCache = -1;
                    return 3;
                }
                int p = ParseInputChFromModelInfo(_cachedModelInfo);
                if (p == 1 || p == 3)
                {
                    _expectedChCache = p;
                    return p;
                }
            }
            catch
            {
            }
            _expectedChCache = -1;
            return 3;
        }

        private static Mat PrepareInferImage(Mat src, int expectedChannels, List<Mat> disposables)
        {
            if (src == null || src.Empty()) return src;
            int ec = (expectedChannels == 1 || expectedChannels == 3) ? expectedChannels : 3;
            Mat u8 = ConvertMatDepthTo8U(src, disposables);
            if (u8.Empty()) return u8;

            if (ec == 3)
            {
                if (u8.Channels() == 1)
                {
                    var rgb = new Mat();
                    Cv2.CvtColor(u8, rgb, ColorConversionCodes.GRAY2RGB);
                    disposables.Add(rgb);
                    return rgb;
                }
                if (u8.Channels() == 4)
                {
                    var rgb = new Mat();
                    Cv2.CvtColor(u8, rgb, ColorConversionCodes.BGRA2RGB);
                    disposables.Add(rgb);
                    return rgb;
                }
                if (u8.Channels() == 3)
                {
                    var rgb = new Mat();
                    Cv2.CvtColor(u8, rgb, ColorConversionCodes.BGR2RGB);
                    disposables.Add(rgb);
                    return rgb;
                }
                var rgbFallback = new Mat();
                Cv2.CvtColor(u8, rgbFallback, ColorConversionCodes.BGR2RGB);
                disposables.Add(rgbFallback);
                return rgbFallback;
            }

            if (u8.Channels() == 1)
                return u8;
            if (u8.Channels() == 3)
            {
                var gray = new Mat();
                Cv2.CvtColor(u8, gray, ColorConversionCodes.BGR2GRAY);
                disposables.Add(gray);
                return gray;
            }
            if (u8.Channels() == 4)
            {
                var gray = new Mat();
                Cv2.CvtColor(u8, gray, ColorConversionCodes.BGRA2GRAY);
                disposables.Add(gray);
                return gray;
            }
            var grayFallback = new Mat();
            Cv2.CvtColor(u8, grayFallback, ColorConversionCodes.BGR2GRAY);
            disposables.Add(grayFallback);
            return grayFallback;
        }

        private List<Mat> PrepareInferImages(IList<Mat> images, out List<Mat> disposables)
        {
            disposables = new List<Mat>();
            if (images == null) return null;
            int expCh = ResolveEffectiveInputCh();
            int ec = (expCh == 1 || expCh == 3) ? expCh : 3;
            var list = new List<Mat>(images.Count);
            foreach (var img in images)
                list.Add(PrepareInferImage(img, ec, disposables));
            return list;
        }

        private static void DisposeTempMats(List<Mat> disposables)
        {
            if (disposables == null) return;
            foreach (var m in disposables)
            {
                try
                {
                    m?.Dispose();
                }
                catch
                {
                }
            }
        }

        public JObject GetCachedModelInfo()
        {
            return _cachedModelInfo != null ? (JObject)_cachedModelInfo.DeepClone() : null;
        }

        public JArray GetCachedMaxShape()
        {
            return _cachedMaxShape != null ? (JArray)_cachedMaxShape.DeepClone() : null;
        }

        public int GetMaxBatchSize()
        {
            return Math.Max(1, _cachedMaxBatchSize);
        }

        /// <summary>
        /// 过滤OCR模型信息，移除不需要的字段
        /// </summary>
        /// <param name="modelInfo">原始OCR模型信息</param>
        /// <returns>过滤后的OCR模型信息</returns>
        private JObject FilterOcrModelInfo(JObject modelInfo)
        {
            if (modelInfo == null)
                return modelInfo;

            // 创建副本以避免修改原始对象
            var filteredInfo = (JObject)modelInfo.DeepClone();

            // 移除指定字段
            var fieldsToRemove = new[] { "character", "dict", "classes" };

            foreach (var field in fieldsToRemove)
            {
                if (filteredInfo.ContainsKey(field))
                {
                    filteredInfo.Remove(field);
                }
            }

            return filteredInfo;
        }

        public JObject GetModelInfo()
        {
            JObject modelInfo = null;
            if (_isDvpMode)
            {
                modelInfo = GetModelInfoDvp();
            }
            else if (_isDvsMode)
            {
                modelInfo = _dvsModel.GetModelInfo();
                if (modelInfo != null && _dvsModel != null)
                {
                    try
                    {
                        JArray loadedMeta = _dvsModel.GetLoadedModelMeta();
                        if (loadedMeta != null && loadedMeta.Count > 0)
                        {
                            modelInfo["loaded_model_meta"] = loadedMeta;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            else if (_isRpcMode)
            {
                var req = new JObject
                {
                    ["action"] = "get_model_info",
                    ["model_path"] = _modelPath
                };
                var resp = SendRpc(req);
                if (resp == null || !(resp["ok"]?.Value<bool>() ?? false))
                {
                    string err = resp != null ? resp["error"]?.ToString() : "rpc_no_response";
                    throw new Exception($"获取模型信息失败: {err}");
                }
                modelInfo = (JObject)resp["model_info"];
            }
            else
            {
                modelInfo = GetModelInfoDvt();
            }
            // 过滤OCR模型信息，移除不需要的字段
            if (modelInfo != null && modelInfo.ContainsKey("model_info"))
            {
                var taskTypeToken = modelInfo["model_info"] != null ? modelInfo["model_info"]["task_type"] : null;
                string task_type = taskTypeToken != null ? taskTypeToken.Value<string>() : null;
                if (task_type == "OCR")
                {
                    JObject real_model_info = modelInfo["model_info"] as JObject;
                    real_model_info = FilterOcrModelInfo(real_model_info);
                    modelInfo["model_info"] = real_model_info;
                }
            }
            UpdateModelMetaCache(modelInfo);
            return modelInfo;
        }

        private JObject GetModelInfoDvp()
        {
            try
            {
                EnsureDvpHttpClient();
                var request = new
                {
                    model_path = _modelPath
                };

                string jsonContent = JsonConvert.SerializeObject(request);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = _httpClient.PostAsync($"{_serverUrl}/get_model_info", content).Result;
                var responseJson = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"获取模型信息失败: {response.StatusCode} - {responseJson}");
                }

                var resultObject = JObject.Parse(responseJson);
                //Log($"Model info: {resultObject}");
                return resultObject;
            }
            catch (Exception ex)
            {
                throw new Exception($"获取模型信息失败: {ex.Message}", ex);
            }
        }

        private JObject GetModelInfoDvt()
        {
            var config = new JObject
            {
                ["model_index"] = modelIndex
            };

            string jsonStr = config.ToString();
            IntPtr resultPtr = DllLoader.Instance.dlcv_get_model_info(jsonStr);
            var resultJson = Marshal.PtrToStringAnsi(resultPtr);
            var resultObject = JObject.Parse(resultJson);

            //Log("Model info: " + resultObject.ToString());
            DllLoader.Instance.dlcv_free_result(resultPtr);
            return resultObject;
        }

        // 内部通用推理方法，处理单张或多张图像
        public Tuple<JObject, IntPtr> InferInternal(List<Mat> images, JObject params_json)
        {
            if (images == null)
                throw new ArgumentNullException(nameof(images));

            List<Mat> disposables = new List<Mat>();
            List<Mat> normalized;
            try
            {
                normalized = PrepareInferImages(images, out disposables);
            }
            catch
            {
                DisposeTempMats(disposables);
                throw;
            }

            try
            {
                if (_isDvpMode)
                {
                    return InferInternalDvp(normalized, params_json);
                }
                if (_isDvsMode)
                {
                    return _dvsModel.InferInternal(normalized, params_json);
                }
                if (_isRpcMode)
                {
                    return InferInternalRpc(normalized, params_json);
                }
                return InferInternalDvt(normalized, params_json);
            }
            finally
            {
                DisposeTempMats(disposables);
            }
        }

        private Tuple<JObject, IntPtr> InferInternalRpc(List<Mat> images, JObject params_json)
        {
            // 仅支持单张图像；多张时逐张合并
            var allResults = new JArray();
            foreach (var image in images)
            {
                if (image == null || image.Empty())
                {
                    allResults.Add(new JObject { ["results"] = new JArray() });
                    continue;
                }

                Mat mat = image;
                if (!mat.IsContinuous()) mat = image.Clone();

                int width = mat.Width, height = mat.Height, channels = mat.Channels();
                string token = Guid.NewGuid().ToString("N");
                string mmfName = "DlcvModelMmf_" + token;

                int bytes = width * height * channels;
                using (var mmf = MemoryMappedFile.CreateNew(mmfName, bytes))
                using (var accessor = mmf.CreateViewAccessor(0, bytes, MemoryMappedFileAccess.Write))
                {
                    // 将Mat数据写入MMF
                    byte[] buffer = new byte[bytes];
                    System.Runtime.InteropServices.Marshal.Copy(mat.Data, buffer, 0, bytes);
                    accessor.WriteArray(0, buffer, 0, bytes);

                    var req = new JObject
                    {
                        ["action"] = "infer",
                        ["model_path"] = _modelPath,
                        ["mmf_token"] = token,
                        ["width"] = width,
                        ["height"] = height,
                        ["channels"] = channels,
                    };
                    if (params_json != null)
                    {
                        req["params_json"] = params_json;
                    }

                    var resp = SendRpc(req);
                    if (resp == null || !(resp["ok"]?.Value<bool>() ?? false))
                    {
                        string err = resp != null ? resp["error"]?.ToString() : "rpc_no_response";
                        throw new Exception($"RPC 推理失败: {err}");
                    }
                    var resultObj = (JObject)resp["result"];

                    // 如果包含mask的mmf引用，在本地转回Mat以保持一致
                    var firstSample = resultObj["sample_results"][0]["results"] as JArray;
                    if (firstSample != null)
                    {
                        foreach (JObject o in firstSample)
                        {
                            if ((o["with_mask"]?.Value<bool>() ?? false) && o["mask"] is JObject mj)
                            {
                                string mtoken = mj.Value<string>("mmf_token");
                                int mw = mj.Value<int>("width");
                                int mh = mj.Value<int>("height");
                                if (!string.IsNullOrEmpty(mtoken) && mw > 0 && mh > 0)
                                {
                                    string mmfName2 = "DlcvModelMask_" + mtoken;
                                    try
                                    {
                                        using (var mmf2 = MemoryMappedFile.OpenExisting(mmfName2, MemoryMappedFileRights.Read))
                                        using (var acc2 = mmf2.CreateViewAccessor(0, mw * mh, MemoryMappedFileAccess.Read))
                                        {
                                            byte[] mBuf = new byte[mw * mh];
                                            acc2.ReadArray(0, mBuf, 0, mBuf.Length);
                                            using (var view = Mat.FromPixelData(mh, mw, MatType.CV_8UC1, mBuf))
                                            {
                                                // 为了统一，mask不在json里返回，后续 ParseToStructResult 会按DVP/DVT分支处理
                                                // 这里先替换成占位，客户端最终不使用此字段
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }

                    allResults.Add(new JObject { ["results"] = firstSample ?? new JArray() });
                }
                if (!ReferenceEquals(mat, image)) mat.Dispose();
            }

            var merged = new JObject { ["sample_results"] = new JArray() };
            foreach (JObject sr in allResults)
            {
                ((JArray)merged["sample_results"]).Add(new JObject { ["results"] = sr["results"] });
            }
            return new Tuple<JObject, IntPtr>(merged, IntPtr.Zero);
        }

        private Tuple<JObject, IntPtr> InferInternalDvp(List<Mat> images, JObject params_json)
        {
            try
            {
                EnsureDvpHttpClient();
                // DVP 模式只支持单张图片，如果有多张图片需要分别处理
                var allResults = new List<JObject>();

                foreach (var image in images)
                {
                    if (image.Empty())
                        throw new ArgumentException("图像列表中包含空图像");

                    // 将图像转换为 BGR 格式
                    Mat image_bgr = new Mat();
                    Cv2.CvtColor(image, image_bgr, ColorConversionCodes.RGB2BGR);
                    byte[] imageBytes = image_bgr.ToBytes(".png");
                    string base64Image = Convert.ToBase64String(imageBytes);

                    // 创建推理请求，添加 return_polygon=true 参数
                    var request = new JObject
                    {
                        ["img"] = base64Image,
                        ["model_path"] = _modelPath,
                        ["return_polygon"] = true
                    };

                    // 如果提供了参数JSON，合并到request
                    if (params_json != null)
                    {
                        foreach (var param in params_json)
                        {
                            request[param.Key] = param.Value;
                        }
                    }

                    string jsonContent = JsonConvert.SerializeObject(request);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = _httpClient.PostAsync($"{_serverUrl}/api/inference", content).Result;
                    var responseJson = response.Content.ReadAsStringAsync().Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"推理失败: {response.StatusCode} - {responseJson}");
                    }

                    var resultObject = JObject.Parse(responseJson);
                    allResults.Add(resultObject);
                }

                // 将多个结果合并为统一格式
                var mergedResult = new JObject();
                var sampleResults = new JArray();

                foreach (var result in allResults)
                {
                    var sampleResult = new JObject();
                    sampleResult["results"] = result["results"];
                    sampleResults.Add(sampleResult);
                }

                mergedResult["sample_results"] = sampleResults;

                // DVP 模式返回空指针，不需要释放
                return new Tuple<JObject, IntPtr>(mergedResult, IntPtr.Zero);
            }
            catch (AggregateException ae)
            {
                var innerException = ae.Flatten().InnerException ?? ae;
                throw new Exception($"DVP 推理失败: {innerException.Message}", innerException);
            }
            catch (Exception ex)
            {
                throw new Exception($"DVP 推理失败: {ex.Message}", ex);
            }
        }

        private Tuple<JObject, IntPtr> InferInternalDvt(List<Mat> images, JObject params_json)
        {
            var imageInfoList = new JArray();
            var processImages = new List<Tuple<Mat, bool>>();

            try
            {
                // 处理所有图像
                foreach (var image in images)
                {
                    // 检查图像是否连续，如果不连续则创建连续副本
                    Mat processImage = image;
                    bool needDispose = false;
                    if (!image.IsContinuous())
                    {
                        processImage = image.Clone();
                        needDispose = true;
                    }

                    processImages.Add(new Tuple<Mat, bool>(processImage, needDispose));

                    int width = processImage.Width;
                    int height = processImage.Height;
                    int channels = processImage.Channels();

                    var imageInfo = new JObject
                    {
                        ["width"] = width,
                        ["height"] = height,
                        ["channels"] = channels,
                        ["image_ptr"] = (ulong)processImage.Data.ToInt64()
                    };

                    imageInfoList.Add(imageInfo);
                }

                // 创建推理请求
                var inferRequest = new JObject
                {
                    ["model_index"] = modelIndex,
                    ["image_list"] = imageInfoList
                };

                // 如果提供了参数JSON，合并到inferRequest
                if (params_json != null)
                {
                    foreach (var param in params_json)
                    {
                        inferRequest[param.Key] = param.Value;
                    }
                }

                // 执行推理
                string jsonStr = inferRequest.ToString();
                IntPtr resultPtr = DllLoader.Instance.dlcv_infer(jsonStr);
                var resultJson = Marshal.PtrToStringAnsi(resultPtr);
                JObject resultObject = JObject.Parse(resultJson);

                // 检查是否返回错误
                if (resultObject["code"] != null && resultObject["code"].Value<int>() != 0)
                {
                    DllLoader.Instance.dlcv_free_model_result(resultPtr);
                    throw new Exception("Inference failed: " + resultObject["message"]);
                }

                // 不在这里释放结果，而是返回结果对象和指针
                return new Tuple<JObject, IntPtr>(resultObject, resultPtr);
            }
            finally
            {
                // 释放所有临时创建的图像资源
                foreach (var pair in processImages)
                {
                    if (pair.Item2) // 如果需要释放
                    {
                        pair.Item1.Dispose();
                    }
                }
            }
        }

        // 处理推理结果到CSharpResult对象
        public Utils.CSharpResult ParseToStructResult(JObject resultObject)
        {
            // 解析 json 结果
            var sampleResults = new List<Utils.CSharpSampleResult>();
            var sampleResultsArray = resultObject["sample_results"] as JArray;

            foreach (var sampleResult in sampleResultsArray)
            {
                var results = new List<Utils.CSharpObjectResult>();
                var resultsArray = sampleResult["results"] as JArray;

                foreach (JObject result in resultsArray)
                {
                    var categoryId = result["category_id"]?.Value<int>() ?? 0;
                    var categoryName = result["category_name"]?.Value<string>() ?? "";
                    var score = result["score"]?.Value<float>() ?? 0.0f;
                    var area = result["area"]?.Value<float>() ?? 0.0f;

                    var bbox = result["bbox"]?.ToObject<List<double>>() ?? new List<double>();

                    // DVP模式下bbox格式是[x1,y1,x2,y2]，需要转换为[x,y,w,h]
                    if (_isDvpMode && bbox != null && bbox.Count == 4)
                    {
                        double x1 = bbox[0];
                        double y1 = bbox[1];
                        double x2 = bbox[2];
                        double y2 = bbox[3];
                        bbox = new List<double> { x1, y1, x2 - x1, y2 - y1 };
                    }

                    // 补充逻辑：如果是DVP模式且bbox无效，尝试从polygon计算bbox
                    if (_isDvpMode && (bbox == null || bbox.Count < 4) && result.ContainsKey("polygon"))
                    {
                        var poly = result["polygon"] as JArray;
                        if (poly != null && poly.Count > 0)
                        {
                            double minX = double.MaxValue, minY = double.MaxValue;
                            double maxX = double.MinValue, maxY = double.MinValue;
                            bool hasPoints = false;

                            foreach (var pointArray in poly)
                            {
                                if (pointArray is JArray point && point.Count >= 2)
                                {
                                    double px = point[0].Value<double>();
                                    double py = point[1].Value<double>();
                                    if (px < minX) minX = px;
                                    if (px > maxX) maxX = px;
                                    if (py < minY) minY = py;
                                    if (py > maxY) maxY = py;
                                    hasPoints = true;
                                }
                            }

                            if (hasPoints)
                            {
                                bbox = new List<double> { minX, minY, maxX - minX, maxY - minY };
                            }
                        }
                    }

                    bool withBbox = false;
                    if (result.ContainsKey("with_bbox"))
                    {
                        withBbox = result["with_bbox"]?.Value<bool>() ?? false;
                    }
                    else
                    {
                        withBbox = bbox.Count() > 0;
                    }

                    var withMask = result["with_mask"]?.Value<bool>() ?? false;
                    Mat mask_img = new Mat();

                    if (withMask)
                    {
                        if (_isDvpMode && result.ContainsKey("polygon"))
                        {
                            // DVP 模式：从 polygon 数据生成 mask，使用转换后的bbox
                            mask_img = CreateMaskFromPolygon(result["polygon"] as JArray, bbox);
                        }
                        else if (!_isDvpMode && result["mask"] != null)
                        {
                            // DVT / RPC 模式：优先支持RPC的MMF传输，其次兼容指针
                            var mask = result["mask"] as JObject;
                            if (mask != null && mask.ContainsKey("mmf_token"))
                            {
                                string mtoken = mask.Value<string>("mmf_token");
                                int mask_width = mask.Value<int>("width");
                                int mask_height = mask.Value<int>("height");
                                if (!string.IsNullOrEmpty(mtoken) && mask_width > 0 && mask_height > 0)
                                {
                                    string mmfName2 = "DlcvModelMask_" + mtoken;
                                    try
                                    {
                                        using (var mmf2 = MemoryMappedFile.OpenExisting(mmfName2, MemoryMappedFileRights.Read))
                                        using (var acc2 = mmf2.CreateViewAccessor(0, mask_width * mask_height, MemoryMappedFileAccess.Read))
                                        {
                                            byte[] mBuf = new byte[mask_width * mask_height];
                                            acc2.ReadArray(0, mBuf, 0, mBuf.Length);
                                            using (var view = Mat.FromPixelData(mask_height, mask_width, MatType.CV_8UC1, mBuf))
                                            {
                                                mask_img = view.Clone();
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                            else if (mask != null)
                            {
                                long maskPtrValue = mask["mask_ptr"]?.Value<long>() ?? 0;
                                if (maskPtrValue != 0)
                                {
                                    IntPtr mask_ptr = new IntPtr(maskPtrValue);
                                    int mask_width = mask["width"]?.Value<int>() ?? 0;
                                    int mask_height = mask["height"]?.Value<int>() ?? 0;
                                    if (mask_width > 0 && mask_height > 0)
                                    {
                                        mask_img = Mat.FromPixelData(mask_height, mask_width, MatType.CV_8UC1, mask_ptr);
                                        mask_img = mask_img.Clone();
                                    }
                                }
                            }
                        }
                    }

                    // DVT 模式：将 mask resize 到 bbox 尺寸
                    // DVP 的 mask 已经是 bbox 尺寸，不需要 resize
                    if (!_isDvpMode && !mask_img.Empty() && bbox != null && bbox.Count >= 4)
                    {
                        int bboxW = (int)bbox[2];
                        int bboxH = (int)bbox[3];
                        if (bboxW > 0 && bboxH > 0 && (mask_img.Width != bboxW || mask_img.Height != bboxH))
                        {
                            using (var original = mask_img)
                            {
                                mask_img = new Mat();
                                Cv2.Resize(original, mask_img, new Size(bboxW, bboxH), 0, 0, InterpolationFlags.Nearest);
                            }
                        }
                    }

                    // 补充逻辑：如果bbox无效但有mask，尝试从mask计算bbox
                    if ((bbox == null || bbox.Count < 4) && !mask_img.Empty())
                    {
                        // 寻找非零像素
                        using (Mat points = new Mat())
                        {
                            Cv2.FindNonZero(mask_img, points);
                            if (points.Rows > 0 || points.Cols > 0)
                            {
                                Rect rect = Cv2.BoundingRect(points);
                                bbox = new List<double> { rect.X, rect.Y, rect.Width, rect.Height };
                                withBbox = true;
                            }
                        }
                    }

                    bool withAngle = false;
                    float angle = -100;

                    // JSON 字段读取
                    if (result.ContainsKey("with_angle") && (result["with_angle"]?.Value<bool>() ?? false))
                    {
                        withAngle = true;
                        if (result.ContainsKey("angle"))
                        {
                            angle = result["angle"]?.Value<float>() ?? -100f;
                        }
                    }

                    // DVP
                    if (!withAngle && bbox != null && bbox.Count == 5)
                    {
                        withAngle = true;
                        angle = (float)bbox[4];
                    }

                    var extraInfo = result["extra_info"] as JObject ?? new JObject();

                    var objectResult = new Utils.CSharpObjectResult(categoryId, categoryName, score, area, bbox,
                        withMask, mask_img, withBbox, withAngle, angle, extraInfo);
                    results.Add(objectResult);
                }

                var sampleResultObj = new Utils.CSharpSampleResult(results);
                sampleResults.Add(sampleResultObj);
            }

            return new Utils.CSharpResult(sampleResults);
        }

        /// <summary>
        /// 从多边形数据创建mask图像
        /// </summary>
        /// <param name="polygonArray">多边形点集</param>
        /// <param name="bbox">边界框信息 [x, y, w, h]</param>
        /// <returns>生成的mask图像</returns>
        private Mat CreateMaskFromPolygon(JArray polygonArray, List<double> bbox)
        {
            if (polygonArray == null || polygonArray.Count == 0 || bbox == null || bbox.Count < 4)
            {
                return new Mat();
            }

            try
            {
                // 解析边界框 [x, y, w, h]
                int x = (int)bbox[0];
                int y = (int)bbox[1];
                int width = (int)bbox[2];
                int height = (int)bbox[3];

                if (width <= 0 || height <= 0)
                {
                    return new Mat();
                }

                // 创建mask图像
                Mat mask = Mat.Zeros(height, width, MatType.CV_8UC1);

                // 解析多边形点
                var points = new List<Point>();
                foreach (var pointArray in polygonArray)
                {
                    if (pointArray is JArray point && point.Count >= 2)
                    {
                        int px = point[0].Value<int>() - x; // 转换为相对于bbox的坐标
                        int py = point[1].Value<int>() - y;

                        // 确保点在mask范围内
                        px = Math.Max(0, Math.Min(width - 1, px));
                        py = Math.Max(0, Math.Min(height - 1, py));

                        points.Add(new Point(px, py));
                    }
                }

                if (points.Count > 0)
                {
                    // 填充多边形
                    var pointsArray = new Point[][] { points.ToArray() };
                    Cv2.FillPoly(mask, pointsArray, Scalar.White);
                }

                return mask;
            }
            catch (Exception ex)
            {
                Log($"创建mask失败: {ex.Message}");
                return new Mat();
            }
        }

        public Utils.CSharpResult Infer(Mat image, JObject params_json = null)
        {
            Utils.CSharpResult result = default(Utils.CSharpResult);
            if (_isDvsMode)
            {
                List<Mat> dvsDisp;
                var normList = PrepareInferImages(new List<Mat> { image }, out dvsDisp);
                try
                {
                    result = _dvsModel.InferBatch(normList, params_json);
                }
                finally
                {
                    DisposeTempMats(dvsDisp);
                }
            }
            else
            {
                // 将单张图像放入列表中处理
                var resultTuple = InferInternal(new List<Mat> { image }, params_json);
                try
                {
                    result = ParseToStructResult(resultTuple.Item1);
                }
                finally
                {
                    // 处理完后释放结果，DVP模式下指针为空，不需要释放
                    if (resultTuple.Item2 != IntPtr.Zero)
                    {
                        DllLoader.Instance.dlcv_free_model_result(resultTuple.Item2);
                    }
                }
            }

            if (DlcvModules.GlobalDebug.PrintDebug && result.SampleResults != null)
            {
                string dims = (image == null || image.Empty()) ? "Empty" : $"{image.Width}x{image.Height}";
                var names = new List<string>();
                if (result.SampleResults != null)
                {
                    foreach (var sr in result.SampleResults)
                    {
                        if (sr.Results != null)
                        {
                            foreach (var obj in sr.Results) names.Add(obj.CategoryName);
                        }
                    }
                }
                DlcvModules.GlobalDebug.Log($"推理完成。输入图像尺寸: {dims}，推理结果: {string.Join(", ", names)}");
            }
            return result;
        }

        public Utils.CSharpResult InferBatch(List<Mat> image_list, JObject params_json = null)
        {
            if (_isDvsMode)
            {
                List<Mat> dvsDisp;
                var normList = PrepareInferImages(image_list, out dvsDisp);
                try
                {
                    return _dvsModel.InferBatch(normList, params_json);
                }
                finally
                {
                    DisposeTempMats(dvsDisp);
                }
            }

            var resultTuple = InferInternal(image_list, params_json);
            try
            {
                return ParseToStructResult(resultTuple.Item1);
            }
            finally
            {
                // 处理完后释放结果，DVP模式下指针为空，不需要释放
                if (resultTuple.Item2 != IntPtr.Zero)
                {
                    DllLoader.Instance.dlcv_free_model_result(resultTuple.Item2);
                }
            }
        }

        /// <summary>
        /// 对单张图片进行推理，返回JSON格式的结果
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="params_json">可选的推理参数，用于配置推理过程</param>
        /// <returns>
        /// 返回JSON格式的检测结果数组，格式如下：
        /// [
        ///   {
        ///     "angle": -100.0,
        ///     "area": 100.0,
        ///     "bbox": [x, y, w, h],
        ///     "category_id": 0,
        ///     "category_name": "name",
        ///     "mask": { "height": -1, "mask_ptr": 0, "width": -1 } 或 [{"x":1,"y":1}, ...],
        ///     "score": 0.99,
        ///     "with_angle": false,
        ///     "with_bbox": true,
        ///     "with_mask": false
        ///   }
        /// ]
        /// </returns>
        public dynamic InferOneOutJson(Mat image, JObject params_json = null)
        {
            JArray rawResults = null;

            if (_isDvsMode)
            {
                List<Mat> dvsDisp;
                var normList = PrepareInferImages(new List<Mat> { image }, out dvsDisp);
                Tuple<JObject, IntPtr> res;
                try
                {
                    res = _dvsModel.InferInternal(normList, params_json);
                }
                finally
                {
                    DisposeTempMats(dvsDisp);
                }
                rawResults = res.Item1["result_list"] as JArray ?? new JArray();
                
                // DVS 模式下不需要释放非托管资源，直接处理
                var finalResults = new JArray();
                foreach (var item in rawResults)
                {
                    if (item is JObject obj)
                    {
                        finalResults.Add(StandardizeJsonOutput(obj, false));
                    }
                }
                return finalResults;
            }

            var resultTuple = InferInternal(new List<Mat> { image }, params_json);
            try
            {
                if (resultTuple.Item1["sample_results"] is JArray sampleResults && sampleResults.Count > 0)
                {
                    rawResults = sampleResults[0]["results"] as JArray;
                }
                
                if (rawResults == null) rawResults = new JArray();

                var finalResults = new JArray();
                foreach (var item in rawResults)
                {
                    if (item is JObject obj)
                    {
                        finalResults.Add(StandardizeJsonOutput(obj, _isDvpMode));
                    }
                }
                return finalResults;
            }
            finally
            {
                // 处理完后释放结果
                if (resultTuple.Item2 != IntPtr.Zero)
                {
                    DllLoader.Instance.dlcv_free_model_result(resultTuple.Item2);
                }
            }
        }

        private JObject StandardizeJsonOutput(JObject item, bool isDvpMode)
        {
            var result = new JObject();

            // 1. Copy/Set basic fields
            result["category_id"] = item["category_id"] ?? 0;
            result["category_name"] = item["category_name"] ?? "";
            result["score"] = item["score"] ?? 0.0;

            // 2. Handle BBox
            var bbox = item["bbox"]?.ToObject<List<double>>() ?? new List<double>();

            // DVP Conversion [x1,y1,x2,y2] -> [x,y,w,h]
            if ((isDvpMode || _isDvsMode)&& bbox.Count == 4)
            {
                double x1 = bbox[0];
                double y1 = bbox[1];
                double x2 = bbox[2];
                double y2 = bbox[3];
                bbox = new List<double> { x1, y1, x2 - x1, y2 - y1 };
            }

            result["bbox"] = JArray.FromObject(bbox);
            result["with_bbox"] = bbox.Count > 0;

            // 3. Handle Area
            if (item.ContainsKey("area"))
            {
                result["area"] = item["area"];
            }
            else
            {
                result["area"] = (bbox.Count >= 4) ? (bbox[2] * bbox[3]) : 0.0;
            }

            // 4. Handle Angle
            bool withAngle = false;
            double angle = -100.0;

            if (item.ContainsKey("angle"))
            {
                angle = item["angle"].Value<double>();
                withAngle = (angle > -99.0);
            }
            
            // If bbox implies rotation (5 elements: [cx, cy, w, h, a])
            if (bbox.Count == 5)
            {
                angle = bbox[4];
                withAngle = true;
            }

            result["angle"] = angle;
            result["with_angle"] = withAngle;
            var extraInfo = item["extra_info"] as JObject ?? new JObject();
            if (extraInfo.HasValues)
            {
                result["extra_info"] = extraInfo;
            }

            // 5. Handle Mask
            bool withMask = false;
            JToken maskOutput = null;

            if (isDvpMode && item.ContainsKey("polygon"))
            {
                // DVP Polygon: [[x,y],...]
                var poly = item["polygon"] as JArray;
                if (poly != null && poly.Count > 0)
                {
                    var points = new JArray();
                    foreach (var pt in poly)
                    {
                        if (pt is JArray p && p.Count >= 2)
                        {
                            points.Add(new JObject { ["x"] = p[0], ["y"] = p[1] });
                        }
                    }
                    if (points.Count > 0)
                    {
                        maskOutput = points;
                        withMask = true;
                    }
                }
            }
            else if (item.ContainsKey("poly")) // DVS ReturnJson format
            {
                // poly: [[[x,y],...]] (List of contours)
                var polyOuter = item["poly"] as JArray;
                if (polyOuter != null && polyOuter.Count > 0)
                {
                    var points = new JArray();
                    // Take first contour
                    if (polyOuter[0] is JArray contour)
                    {
                        foreach (var pt in contour) // pt is [x,y] (List<float>)
                        {
                            if (pt is JArray p && p.Count >= 2)
                            {
                                points.Add(new JObject { ["x"] = (int)p[0].Value<float>(), ["y"] = (int)p[1].Value<float>() });
                            }
                        }
                    }
                    if (points.Count > 0)
                    {
                        maskOutput = points;
                        withMask = true;
                    }
                }
            }
            else if (item.ContainsKey("mask")) // DVT / RPC
            {
                var m = item["mask"];
                // Check if it is already points (JArray)
                if (m is JArray ma)
                {
                    if (ma.Count > 0 && ma[0] is JObject mo && mo.ContainsKey("x"))
                    {
                        maskOutput = ma;
                        withMask = true;
                    }
                }
                else if (m is JObject maskObj)
                {
                    // DVT Mask Object with ptr
                    long ptrVal = maskObj["mask_ptr"]?.Value<long>() ?? 0;
                    if (ptrVal != 0)
                    {
                        int mw = maskObj["width"]?.Value<int>() ?? 0;
                        int mh = maskObj["height"]?.Value<int>() ?? 0;
                        if (mw > 0 && mh > 0)
                        {
                            // Convert ptr to points using OpenCV
                            using (var maskImg = Mat.FromPixelData(mh, mw, MatType.CV_8UC1, new IntPtr(ptrVal)))
                            {
                                int bw = (bbox.Count > 2) ? (int)bbox[2] : 0;
                                int bh = (bbox.Count > 3) ? (int)bbox[3] : 0;
                                double bx = (bbox.Count > 0) ? bbox[0] : 0;
                                double by = (bbox.Count > 1) ? bbox[1] : 0;

                                Mat procImg = maskImg;
                                bool needDispose = false;
                                if (bw > 0 && bh > 0 && (maskImg.Cols != bw || maskImg.Rows != bh))
                                {
                                    procImg = maskImg.Resize(new Size(bw, bh));
                                    needDispose = true;
                                }

                                var contours = procImg.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                                if (contours.Length > 0)
                                {
                                    var points = new JArray();
                                    foreach (var pt in contours[0])
                                    {
                                        points.Add(new JObject { ["x"] = (int)(pt.X + bx), ["y"] = (int)(pt.Y + by) });
                                    }
                                    maskOutput = points;
                                    withMask = true;
                                }

                                if (needDispose) procImg.Dispose();
                            }
                        }
                    }
                }
            }

            if (withMask)
            {
                result["mask"] = maskOutput;
                result["with_mask"] = true;
            }
            else
            {
                result["mask"] = new JObject { ["height"] = -1, ["mask_ptr"] = 0, ["width"] = -1 };
                result["with_mask"] = false;
            }

            return result;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                    FreeModel();
                    _httpClient?.Dispose();
                }

                // 设置处置标志
                _disposed = true;
            }
        }

        /// <summary>
        /// 获取当前模型是否为DVP模式
        /// </summary>
        public bool IsDvpMode => _isDvpMode;

        /// <summary>
        /// 清空模型缓存
        /// </summary>
        public static void ClearModelCache()
        {
            lock (_cacheLock)
            {
                _modelCache.Clear();
                _loadingModels.Clear();
            }
        }
    }
}

