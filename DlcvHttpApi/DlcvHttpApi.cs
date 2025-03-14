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
            string url = $"http://127.0.0.1:{port}/api/health";
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(2);
                    var response = client.GetAsync(url).Result;
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 异步检查本地DLCV服务器是否已启动
        /// </summary>
        /// <param name="port">服务器端口号，默认为9890</param>
        /// <returns>如果服务器已启动则返回true的任务</returns>
        public static async Task<bool> IsLocalServerRunningAsync(int port = 9890)
        {
            string url = $"http://127.0.0.1:{port}/api/health";
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(2);
                    var response = await client.GetAsync(url);
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 公共属性和方法

        /// <summary>
        /// 获取服务器连接状态
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 异步初始化并检查与服务器的连接
        /// </summary>
        /// <returns>如果服务器可用则返回true的任务</returns>
        public async Task<bool> ConnectAsync()
        {
            ThrowIfDisposed();
            
            try
            {
                // 检查服务器健康状态
                var client = GetHttpClient();
                var response = await client.GetAsync($"{_serverUrl}/api/health");
                _isConnected = response.IsSuccessStatusCode;
                return _isConnected;
            }
            catch
            {
                _isConnected = false;
                return false;
            }
        }

        /// <summary>
        /// 异步使用图像文件执行推理
        /// </summary>
        /// <param name="imagePath">图像文件路径</param>
        /// <param name="modelPath">模型文件路径</param>
        /// <returns>推理结果任务</returns>
        public async Task<Utils.CSharpResult> InferImageAsync(string imagePath, string modelPath)
        {
            ThrowIfDisposed();
            
            if (!_isConnected)
                throw new InvalidOperationException("未连接到服务器，请先调用ConnectAsync()方法");

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
            return await SendInferenceRequestAsync(request);
        }

        /// <summary>
        /// 异步使用Bitmap对象执行推理
        /// </summary>
        /// <param name="bitmap">要处理的Bitmap图像</param>
        /// <param name="modelPath">模型文件路径</param>
        /// <returns>推理结果任务</returns>
        public async Task<Utils.CSharpResult> InferBitmapAsync(Bitmap bitmap, string modelPath)
        {
            ThrowIfDisposed();
            
            if (!_isConnected)
                throw new InvalidOperationException("未连接到服务器，请先调用ConnectAsync()方法");

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
                return await SendInferenceRequestAsync(request);
            }
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
        public Task<Utils.CSharpResult> InferCustomFormatAsync<TInput>(TInput input, string modelPath)
        {
            throw new NotImplementedException("自定义格式推理接口尚未实现");
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
        /// 异步发送推理请求并处理响应
        /// </summary>
        /// <param name="request">请求对象</param>
        /// <returns>处理后的推理结果任务</returns>
        private async Task<Utils.CSharpResult> SendInferenceRequestAsync(object request)
        {
            try
            {
                var client = GetHttpClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(request),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await client.PostAsync($"{_serverUrl}/api/inference", content);

                if (!response.IsSuccessStatusCode)
                {
                    HandleErrorResponse(response.StatusCode);
                }

                var responseString = await response.Content.ReadAsStringAsync();
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
                    // 解析边界框
                    var bbox = item["bbox"].ToObject<List<double>>();
                    
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

                    // 解析分类ID和分类名称
                    int categoryId = item["category_id"].Value<int>();
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
