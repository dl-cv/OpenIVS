using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Drawing;
using System.Net;
using System.Drawing.Imaging;
using dlcv_infer_csharp;
using System.Net.Sockets;

namespace DLCV
{
    /// <summary>
    /// DLCV HTTP API客户端，用于与DLCV推理服务器通信
    /// </summary>
    public class DlcvHttpApi : IDisposable
    {
        #region 私有字段

        private readonly string _serverUrl;
        private readonly object _sessionLock = new object();
        private readonly List<HttpClient> _clientPool;
        private int _currentClientIndex;
        private readonly int _maxPoolSize;
        private bool _isConnected;
        private bool _disposed;

        #endregion

        #region 构造函数

        /// <summary>
        /// 使用默认本地服务器地址初始化API客户端
        /// </summary>
        public DlcvHttpApi() : this("http://127.0.0.1:9890")
        {
        }

        /// <summary>
        /// 使用指定服务器地址初始化API客户端
        /// </summary>
        /// <param name="serverUrl">服务器地址，例如: http://127.0.0.1:9890</param>
        /// <param name="maxPoolSize">客户端连接池最大大小</param>
        public DlcvHttpApi(string serverUrl, int maxPoolSize = 5)
        {
            if (string.IsNullOrEmpty(serverUrl))
                throw new ArgumentNullException(nameof(serverUrl), "服务器地址不能为空");

            _serverUrl = serverUrl.TrimEnd('/');
            _maxPoolSize = Math.Max(1, maxPoolSize);
            _clientPool = new List<HttpClient>(_maxPoolSize);
            _currentClientIndex = 0;
            _isConnected = false;
            _disposed = false;

            // 初始化连接池
            InitializeClientPool();
        }

        #endregion

        #region 公共静态方法

        /// <summary>
        /// 检查本地DLCV服务器是否已启动
        /// </summary>
        /// <param name="port">服务器端口号，默认为9890</param>
        /// <returns>如果服务器已启动则返回true</returns>
        public static bool IsLocalServerRunning(int port = 9890)
        {
            // 首先检查端口是否开放
            try
            {
                using (var client = new TcpClient())
                {
                    // 尝试连接指定端口，超时设置为0.5秒
                    var result = client.BeginConnect("127.0.0.1", port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(0.5));

                    if (!success)
                    {
                        return false; // 连接超时
                    }

                    // 尝试完成连接
                    client.EndConnect(result);
                }

                // 端口开放，尝试访问文档页
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(2);
                    var response = httpClient.GetAsync($"http://127.0.0.1:{port}/docs").Result;
                    return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查本地DLCV服务器是否已启动 (同步方法保留异步版本，以便兼容)
        /// </summary>
        /// <param name="port">服务器端口号，默认为9890</param>
        /// <returns>如果服务器已启动则返回true的任务</returns>
        public static Task<bool> IsLocalServerRunningAsync(int port = 9890)
        {
            return Task.FromResult(IsLocalServerRunning(port));
        }

        #endregion

        #region 公共属性和方法

        /// <summary>
        /// 获取服务器连接状态
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 初始化并检查与服务器的连接
        /// </summary>
        /// <returns>如果服务器可用则返回true</returns>
        public bool Connect()
        {
            ThrowIfDisposed();

            try
            {
                // 检查服务器健康状态 - 只检查端口是否开放
                var uri = new Uri(_serverUrl);
                string host = uri.Host;
                int port = uri.Port;

                using (var client = new TcpClient())
                {
                    // 尝试连接，超时设置为2秒
                    var result = client.BeginConnect(host, port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));

                    if (!success)
                    {
                        _isConnected = false;
                        return false; // 连接超时
                    }

                    // 完成连接
                    client.EndConnect(result);
                    _isConnected = true;
                    return true;
                }
            }
            catch
            {
                _isConnected = false;
                return false;
            }
        }

        /// <summary>
        /// 初始化并检查与服务器的连接（异步版本，保留以兼容现有代码）
        /// </summary>
        /// <returns>如果服务器可用则返回true的任务</returns>
        public Task<bool> ConnectAsync()
        {
            return Task.FromResult(Connect());
        }

        /// <summary>
        /// 使用图像文件执行推理
        /// </summary>
        /// <param name="imagePath">图像文件路径</param>
        /// <param name="modelPath">模型文件路径</param>
        /// <returns>推理结果</returns>
        public Utils.CSharpResult InferImage(string imagePath, string modelPath)
        {
            ThrowIfDisposed();

            if (!_isConnected)
                throw new InvalidOperationException("未连接到服务器，请先调用Connect()方法");

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                throw new ArgumentException("图像文件路径无效或文件不存在", nameof(imagePath));

            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                throw new ArgumentException("模型文件路径无效或文件不存在", nameof(modelPath));

            // 将图像转换为Base64
            byte[] imageBytes = File.ReadAllBytes(imagePath);
            string base64Image = Convert.ToBase64String(imageBytes);

            // 创建请求
            var request = CreateDefaultInferenceRequest(base64Image, modelPath);

            // 发送请求
            return SendInferenceRequest(request);
        }

        /// <summary>
        /// 异步使用图像文件执行推理（保留以兼容现有代码）
        /// </summary>
        /// <param name="imagePath">图像文件路径</param>
        /// <param name="modelPath">模型文件路径</param>
        /// <returns>推理结果任务</returns>
        public Task<Utils.CSharpResult> InferImageAsync(string imagePath, string modelPath)
        {
            return Task.FromResult(InferImage(imagePath, modelPath));
        }

        /// <summary>
        /// 使用Bitmap对象执行推理
        /// </summary>
        /// <param name="bitmap">要处理的Bitmap图像</param>
        /// <param name="modelPath">模型文件路径</param>
        /// <returns>推理结果</returns>
        public Utils.CSharpResult InferBitmap(Bitmap bitmap, string modelPath)
        {
            ThrowIfDisposed();

            if (!_isConnected)
                throw new InvalidOperationException("未连接到服务器，请先调用Connect()方法");

            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap), "图像不能为空");

            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                throw new ArgumentException("模型文件路径无效或文件不存在", nameof(modelPath));

            // 将Bitmap转换为Base64
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                string base64Image = Convert.ToBase64String(ms.ToArray());

                // 创建请求
                var request = CreateDefaultInferenceRequest(base64Image, modelPath);

                // 发送请求
                return SendInferenceRequest(request);
            }
        }

        /// <summary>
        /// 异步使用Bitmap对象执行推理（保留以兼容现有代码）
        /// </summary>
        /// <param name="bitmap">要处理的Bitmap图像</param>
        /// <param name="modelPath">模型文件路径</param>
        /// <returns>推理结果任务</returns>
        public Task<Utils.CSharpResult> InferBitmapAsync(Bitmap bitmap, string modelPath)
        {
            return Task.FromResult(InferBitmap(bitmap, modelPath));
        }

        /// <summary>
        /// 定义其他输入格式的接口（预留）
        /// </summary>
        /// <remarks>
        /// 这是一个预留的接口，用于支持其他格式的输入和转换
        /// 实际实现将在后续版本中添加
        /// </remarks>
        /// <typeparam name="TInput">输入类型</typeparam>
        /// <param name="input">输入对象</param>
        /// <param name="modelPath">模型路径</param>
        /// <returns>推理结果</returns>
        public Utils.CSharpResult InferCustomFormat<TInput>(TInput input, string modelPath)
        {
            throw new NotImplementedException("自定义格式推理接口尚未实现");
        }

        /// <summary>
        /// 异步版本保留接口（尚未实现）
        /// </summary>
        public Task<Utils.CSharpResult> InferCustomFormatAsync<TInput>(TInput input, string modelPath)
        {
            throw new NotImplementedException("自定义格式推理接口尚未实现");
        }

        /// <summary>
        /// 加载模型到服务器
        /// </summary>
        /// <param name="modelPath">模型文件路径</param>
        /// <returns>返回模型加载结果，包含model_index等信息</returns>
        public JObject LoadModel(string modelPath)
        {
            ThrowIfDisposed();

            if (!_isConnected)
                throw new InvalidOperationException("未连接到服务器，请先调用Connect()方法");

            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                throw new ArgumentException("模型文件路径无效或文件不存在", nameof(modelPath));

            try
            {
                var client = GetHttpClient();
                
                // 使用JSON请求体提交模型路径，与 dlcv_infer_csharp 保持一致
                var request = new
                {
                    model_path = modelPath
                };
                var content = new StringContent(
                    JsonConvert.SerializeObject(request),
                    System.Text.Encoding.UTF8,
                    "application/json");
                var response = client.PostAsync($"{_serverUrl}/load_model", content).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    HandleErrorResponse(response.StatusCode);
                }

                var responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var jsonResult = JObject.Parse(responseString);

                if (jsonResult["code"]?.ToString() != "00000")
                {
                    throw new DlcvApiException($"API错误: {jsonResult["message"]?.ToString()}");
                }

                // 加载成功后调用 /version 接口以确保后端状态就绪
                CallVersionApi();

                return jsonResult;
            }
            catch (Exception ex)
            {
                if (ex is DlcvApiException)
                    throw;

                throw new DlcvApiException("加载模型请求处理失败", ex);
            }
        }

        /// <summary>
        /// 异步加载模型到服务器（保留以兼容现有代码）
        /// </summary>
        /// <param name="modelPath">模型文件路径</param>
        /// <returns>返回模型加载结果任务</returns>
        public Task<JObject> LoadModelAsync(string modelPath)
        {
            return Task.FromResult(LoadModel(modelPath));
        }

        /// <summary>
        /// 获取已加载模型的信息
        /// </summary>
        /// <param name="modelIndex">模型索引，通过LoadModel方法获取</param>
        /// <returns>返回模型信息</returns>
        public JObject GetModelInfo(int modelIndex)
        {
            ThrowIfDisposed();

            if (!_isConnected)
                throw new InvalidOperationException("未连接到服务器，请先调用Connect()方法");

            if (modelIndex < 0)
                throw new ArgumentException("模型索引无效", nameof(modelIndex));

            try
            {
                var client = GetHttpClient();
                
                // 使用GET请求和查询参数，与Python调用保持一致
                string url = $"{_serverUrl}/get_model_info?model_index={modelIndex}";
                var response = client.GetAsync(url).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    HandleErrorResponse(response.StatusCode);
                }

                var responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var jsonResult = JObject.Parse(responseString);

                if (jsonResult["code"]?.ToString() != "00000")
                {
                    throw new DlcvApiException($"API错误: {jsonResult["message"]?.ToString()}");
                }

                return jsonResult;
            }
            catch (Exception ex)
            {
                if (ex is DlcvApiException)
                    throw;

                throw new DlcvApiException("获取模型信息请求处理失败", ex);
            }
        }

        /// <summary>
        /// 异步获取已加载模型的信息（保留以兼容现有代码）
        /// </summary>
        /// <param name="modelIndex">模型索引，通过LoadModel方法获取</param>
        /// <returns>返回模型信息任务</returns>
        public Task<JObject> GetModelInfoAsync(int modelIndex)
        {
            return Task.FromResult(GetModelInfo(modelIndex));
        }

        /// <summary>
        /// 获取模型文件的信息，无需先加载模型
        /// </summary>
        /// <param name="modelPath">模型文件路径</param>
        /// <returns>返回模型信息</returns>
        public JObject GetModelInfo(string modelPath)
        {
            ThrowIfDisposed();

            if (!_isConnected)
                throw new InvalidOperationException("未连接到服务器，请先调用Connect()方法");

            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                throw new ArgumentException("模型文件路径无效或文件不存在", nameof(modelPath));

            // 创建请求
            var request = new
            {
                model_path = modelPath
            };

            try
            {
                var client = GetHttpClient();
                
                // 根据后端代码，get_model_info是GET请求，需要使用查询参数
                string url = $"{_serverUrl}/get_model_info?model_path={Uri.EscapeDataString(modelPath)}";
                var response = client.GetAsync(url).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    HandleErrorResponse(response.StatusCode);
                }

                var responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var jsonResult = JObject.Parse(responseString);

                if (jsonResult["code"]?.ToString() != "00000")
                {
                    throw new DlcvApiException($"API错误: {jsonResult["message"]?.ToString()}");
                }

                return jsonResult;
            }
            catch (Exception ex)
            {
                if (ex is DlcvApiException)
                    throw;

                throw new DlcvApiException("获取模型信息请求处理失败", ex);
            }
        }

        /// <summary>
        /// 异步获取模型文件的信息（保留以兼容现有代码）
        /// </summary>
        /// <param name="modelPath">模型文件路径</param>
        /// <returns>返回模型信息任务</returns>
        public Task<JObject> GetModelInfoAsync(string modelPath)
        {
            return Task.FromResult(GetModelInfo(modelPath));
        }

        /// <summary>
        /// 使用已加载的模型索引进行图像推理
        /// </summary>
        /// <param name="imagePath">图像文件路径</param>
        /// <param name="modelIndex">模型索引，通过LoadModel获取</param>
        /// <returns>推理结果</returns>
        public Utils.CSharpResult InferImageWithModelIndex(string imagePath, int modelIndex)
        {
            ThrowIfDisposed();

            if (!_isConnected)
                throw new InvalidOperationException("未连接到服务器，请先调用Connect()方法");

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                throw new ArgumentException("图像文件路径无效或文件不存在", nameof(imagePath));

            if (modelIndex < 0)
                throw new ArgumentException("模型索引无效", nameof(modelIndex));

            // 将图像转换为Base64
            byte[] imageBytes = File.ReadAllBytes(imagePath);
            string base64Image = Convert.ToBase64String(imageBytes);

            // 创建请求，使用模型索引代替模型路径
            var request = new
            {
                img = base64Image,
                model_index = modelIndex
            };

            // 发送请求
            return SendInferenceRequest(request);
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 初始化HTTP客户端连接池
        /// </summary>
        private void InitializeClientPool()
        {
            lock (_sessionLock)
            {
                for (int i = 0; i < _maxPoolSize; i++)
                {
                    var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(30);
                    _clientPool.Add(client);
                }
            }
        }

        /// <summary>
        /// 从连接池获取一个HTTP客户端
        /// </summary>
        /// <returns>可用的HTTP客户端</returns>
        private HttpClient GetHttpClient()
        {
            lock (_sessionLock)
            {
                // 简单的轮询策略
                int index = _currentClientIndex;
                _currentClientIndex = (_currentClientIndex + 1) % _maxPoolSize;
                return _clientPool[index];
            }
        }

        /// <summary>
        /// 创建默认的推理请求对象
        /// </summary>
        /// <param name="base64Image">Base64编码的图像</param>
        /// <param name="modelPath">模型路径</param>
        /// <returns>请求对象</returns>
        private object CreateDefaultInferenceRequest(string base64Image, string modelPath)
        {
            return new
            {
                img = base64Image,
                model_path = modelPath
            };
        }

        /// <summary>
        /// 发送推理请求并处理响应
        /// </summary>
        /// <param name="request">请求对象</param>
        /// <returns>处理后的推理结果</returns>
        private Utils.CSharpResult SendInferenceRequest(object request)
        {
            try
            {
                var client = GetHttpClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(request),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = client.PostAsync($"{_serverUrl}/api/inference", content).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    HandleErrorResponse(response.StatusCode);
                }

                var responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var jsonResult = JObject.Parse(responseString);

                if (jsonResult["code"]?.ToString() != "00000")
                {
                    throw new DlcvApiException($"API错误: {jsonResult["message"]?.ToString()}");
                }

                // 将JSON结果转换为C#对象
                return ParseInferenceResponse(jsonResult);
            }
            catch (Exception ex)
            {
                if (ex is DlcvApiException)
                    throw;

                throw new DlcvApiException("推理请求处理失败", ex);
            }
        }

        /// <summary>
        /// 异步发送推理请求并处理响应（保留以供内部使用）
        /// </summary>
        /// <param name="request">请求对象</param>
        /// <returns>处理后的推理结果任务</returns>
        private Task<Utils.CSharpResult> SendInferenceRequestAsync(object request)
        {
            return Task.FromResult(SendInferenceRequest(request));
        }

        /// <summary>
        /// 将JSON推理结果解析为C#对象
        /// </summary>
        /// <param name="jsonResult">JSON响应</param>
        /// <returns>推理结果对象</returns>
        private Utils.CSharpResult ParseInferenceResponse(JObject jsonResult)
        {
            var results = jsonResult["results"] as JArray;
            var objectResults = new List<Utils.CSharpObjectResult>();

            if (results != null)
            {
                foreach (JObject item in results)
                {
                    List<double> bbox;
                    if (item.ContainsKey("bbox"))
                    {
                        // 解析边界框
                        bbox = item["bbox"].ToObject<List<double>>();

                        // 确保边界框格式是 [x, y, width, height]
                        if (bbox.Count == 4)
                        {
                            // 默认格式是 [x1, y1, x2, y2]，需要转换为 [x, y, width, height]
                            var x1 = bbox[0];
                            var y1 = bbox[1];
                            var x2 = bbox[2];
                            var y2 = bbox[3];
                            bbox = new List<double> { x1, y1, x2 - x1, y2 - y1 };
                        }
                    }
                    else
                    {
                        bbox = new List<double> { 0, 0, 0, 0 };
                    }

                    // 解析分类ID和分类名称
                    int categoryId = item.ContainsKey("category_id") ? item["category_id"].Value<int>() : 0;
                    string categoryName = item["category_name"].Value<string>();

                    // 解析置信度得分
                    float score = item["score"].Value<float>();

                    // 解析区域面积
                    float area = item.ContainsKey("area") ? item["area"].Value<float>() : 0;

                    // 检查是否有掩码
                    bool withMask = item.ContainsKey("with_mask") && item["with_mask"].Value<bool>();

                    // 创建结果对象
                    var objectResult = new Utils.CSharpObjectResult(
                        categoryId,
                        categoryName,
                        score,
                        area,
                        bbox,
                        withMask,
                        null // 掩码暂时设为null，未来可以扩展
                    );

                    objectResults.Add(objectResult);
                }
            }

            // 创建样本结果
            var sampleResults = new List<Utils.CSharpSampleResult> { new Utils.CSharpSampleResult(objectResults) };

            // 返回推理结果
            return new Utils.CSharpResult(sampleResults);
        }

        /// <summary>
        /// 处理HTTP错误响应
        /// </summary>
        /// <param name="statusCode">HTTP状态码</param>
        private void HandleErrorResponse(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case (HttpStatusCode)429:
                    throw new DlcvApiException("触发速率限制，当前限制为1fps");
                case HttpStatusCode.BadRequest:
                    throw new DlcvApiException("请求参数错误");
                case HttpStatusCode.NotFound:
                    throw new DlcvApiException("API端点未找到");
                case HttpStatusCode.InternalServerError:
                    throw new DlcvApiException("服务器内部错误");
                default:
                    throw new DlcvApiException($"HTTP请求失败: {(int)statusCode}");
            }
        }

        /// <summary>
        /// 调用后端 /version 接口，忽略返回，仅用于触发状态初始化
        /// </summary>
        private void CallVersionApi()
        {
            try
            {
                var client = GetHttpClient();
                var response = client.GetAsync($"{_serverUrl}/version").GetAwaiter().GetResult();
                // 忽略非成功状态，不阻断主流程
            }
            catch
            {
                // 忽略异常，不影响主流程
            }
        }

        /// <summary>
        /// 如果对象已处置，则抛出异常
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name, "此对象已被处置，不能再使用");
            }
        }

        #endregion

        #region IDisposable实现

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing">是否正在处置托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                    lock (_sessionLock)
                    {
                        foreach (var client in _clientPool)
                        {
                            client.Dispose();
                        }
                        _clientPool.Clear();
                    }
                }

                // 设置处置标志
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// DLCV API异常类
    /// </summary>
    public class DlcvApiException : Exception
    {
        public DlcvApiException(string message) : base(message)
        {
        }

        public DlcvApiException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
