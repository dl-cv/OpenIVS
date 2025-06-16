using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;
using System.Diagnostics;

namespace DlcvDvpHttpApi
{
    /// <summary>
    /// HTTP版本的Model类，与DlcvCsharpApi.Model保持一致的接口
    /// </summary>
    public class Model : IDisposable
    {
        private string _modelPath;
        private string _serverUrl;
        private HttpClient _httpClient;
        private int _modelIndex = -1;
        private bool _disposed = false;

        /// <summary>
        /// 使用模型路径创建Model实例
        /// </summary>
        /// <param name="modelPath">模型文件路径</param>
        /// <param name="serverUrl">HTTP服务器地址，默认为http://127.0.0.1:9890</param>
        public Model(string modelPath, string serverUrl = "http://127.0.0.1:9890")
        {
            _modelPath = modelPath;
            _serverUrl = serverUrl.TrimEnd('/');
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // 5分钟超时

            // 加载模型
            LoadModel();
        }

        /// <summary>
        /// 使用模型路径和设备ID创建Model实例（兼容原API，但忽略device_id参数）
        /// </summary>
        /// <param name="modelPath">模型文件路径</param>
        /// <param name="device_id">设备ID（HTTP版本中此参数被忽略）</param>
        /// <param name="serverUrl">HTTP服务器地址，默认为http://127.0.0.1:9890</param>
        public Model(string modelPath, int device_id, string serverUrl = "http://127.0.0.1:9890") 
            : this(modelPath, serverUrl)
        {
            // device_id参数在HTTP版本中被忽略
        }

        ~Model()
        {
            Dispose(false);
        }

        /// <summary>
        /// 加载模型到服务器
        /// </summary>
        private void LoadModel()
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
                throw new ArgumentException("模型文件路径无效或文件不存在", nameof(_modelPath));

            // 检查后端服务是否启动
            if (!CheckBackendService())
            {
                // 启动后端服务
                StartBackendService();
                throw new Exception("检测到后端未启动，正在启动推理后端，请10秒钟后再次尝试");
            }

            try
            {
                var request = new
                {
                    model_path = _modelPath
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
                
                // 检查返回格式：{'code': '00000', 'model_path': model_path}
                if (resultObject.ContainsKey("code") && 
                    resultObject["code"].Value<string>() == "00000")
                {
                    Console.WriteLine($"Model load result: {resultObject}");
                    // HTTP版本不需要model_index，加载成功即可
                    _modelIndex = 1; // 设置一个默认值表示模型已加载
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
        
        /// <summary>
        /// 检查后端服务是否已启动
        /// </summary>
        /// <returns>服务是否可用</returns>
        private bool CheckBackendService()
        {
            try
            {
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
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = @"C:\dlcv\Lib\site-packages\dlcv_test\DLCV Test.exe",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                
                Process.Start(processStartInfo);
                Console.WriteLine("已启动后端推理服务");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动后端服务失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取模型信息
        /// </summary>
        /// <returns>模型信息</returns>
        public JObject GetModelInfo()
        {
            ThrowIfDisposed();

            if (_modelIndex == -1)
                throw new InvalidOperationException("模型未正确加载");

            try
            {
                // 创建获取模型信息的请求 - 后端期望的格式
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
                Console.WriteLine($"Model info: {resultObject}");
                return resultObject;
            }
            catch (Exception ex)
            {
                throw new Exception($"获取模型信息失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 对单张图像进行推理
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="params_json">推理参数（可选）</param>
        /// <returns>推理结果</returns>
        public Utils.CSharpResult Infer(Mat image, JObject params_json = null)
        {
            return InferBatch(new List<Mat> { image }, params_json);
        }

        /// <summary>
        /// 对图像文件进行推理
        /// </summary>
        /// <param name="imagePath">图像文件路径</param>
        /// <param name="params_json">推理参数（可选）</param>
        /// <returns>推理结果</returns>
        public Utils.CSharpResult Infer(string imagePath, JObject params_json = null)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                throw new ArgumentException("图像文件路径无效或文件不存在", nameof(imagePath));

            Mat image = Cv2.ImRead(imagePath);
            if (image.Empty())
                throw new ArgumentException("无法读取图像文件", nameof(imagePath));

            try
            {
                return Infer(image, params_json);
            }
            finally
            {
                image?.Dispose();
            }
        }

        /// <summary>
        /// 对多张图像进行批量推理
        /// </summary>
        /// <param name="imageList">图像列表</param>
        /// <param name="params_json">推理参数（可选）</param>
        /// <returns>推理结果</returns>
        public Utils.CSharpResult InferBatch(List<Mat> imageList, JObject params_json = null)
        {
            ThrowIfDisposed();

            if (_modelIndex == -1)
                throw new InvalidOperationException("模型未正确加载");

            if (imageList == null || imageList.Count == 0)
                throw new ArgumentException("图像列表不能为空", nameof(imageList));

            try
            {
                // 将图像转换为base64编码列表
                var base64Images = new List<string>();
                foreach (var image in imageList)
                {
                    if (image.Empty())
                        throw new ArgumentException("图像列表中包含空图像");

                    byte[] imageBytes = image.ToBytes(".jpg");
                    string base64Image = Convert.ToBase64String(imageBytes);
                    base64Images.Add(base64Image);
                }

                // 由于后端API期望单张图片，如果有多张图片需要分别处理
                var allResults = new List<Utils.CSharpSampleResult>();
                
                foreach (var base64Image in base64Images)
                {
                    // 创建推理请求 - 后端期望的格式
                    var request = new
                    {
                        img = base64Image,
                        model_path = _modelPath
                    };

                    string jsonContent = JsonConvert.SerializeObject(request);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = _httpClient.PostAsync($"{_serverUrl}/api/inference", content).Result;
                    var responseJson = response.Content.ReadAsStringAsync().Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"推理失败: {response.StatusCode} - {responseJson}");
                    }

                    var resultObject = JObject.Parse(responseJson);
                    var sampleResult = ParseSingleImageResult(resultObject);
                    allResults.Add(sampleResult);
                }

                return new Utils.CSharpResult(allResults);
            }
            catch (Exception ex)
            {
                throw new Exception($"推理失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 解析单张图片的推理结果
        /// </summary>
        /// <param name="resultObject">JSON结果对象</param>
        /// <returns>单张图片的推理结果</returns>
        private Utils.CSharpSampleResult ParseSingleImageResult(JObject resultObject)
        {
            var objectResults = new List<Utils.CSharpObjectResult>();

            // 根据实际返回的数据结构调整解析逻辑
            // 这里假设返回格式是 {"results": [...]} 
            JArray resultsArray = null;
            
            if (resultObject.ContainsKey("results") && resultObject["results"] is JArray)
            {
                resultsArray = resultObject["results"] as JArray;
            }

            if (resultsArray != null)
            {
                foreach (JObject obj in resultsArray)
                {
                    int categoryId = obj.ContainsKey("category_id") ? obj["category_id"].Value<int>() : 0;
                    string categoryName = obj.ContainsKey("category_name") ? obj["category_name"].Value<string>() : "";
                    float score = obj.ContainsKey("score") ? obj["score"].Value<float>() : 0.0f;
                    float area = obj.ContainsKey("area") ? obj["area"].Value<float>() : 0.0f;

                    List<double> bbox = null;
                    bool withBbox = false;
                    if (obj.ContainsKey("bbox") && obj["bbox"] is JArray bboxArray)
                    {
                        bbox = bboxArray.ToObject<List<double>>();
                        withBbox = true;
                    }

                    bool withMask = false;
                    Mat mask = null;
                    if (obj.ContainsKey("mask") && obj["mask"].Value<string>() != null)
                    {
                        withMask = true;
                        // 这里可以根据需要解码mask数据
                        // 目前创建一个空的Mat作为占位符
                        mask = new Mat();
                    }

                    bool withAngle = false;
                    float angle = -100;
                    if (obj.ContainsKey("angle"))
                    {
                        withAngle = true;
                        angle = obj["angle"].Value<float>();
                    }

                    var objectResult = new Utils.CSharpObjectResult(
                        categoryId, categoryName, score, area, bbox, withMask, mask, withBbox, withAngle, angle);
                    objectResults.Add(objectResult);
                }
            }

            return new Utils.CSharpSampleResult(objectResults);
        }

        /// <summary>
        /// 将JSON结果解析为C#结构体（保留原方法以备用）
        /// </summary>
        /// <param name="resultObject">JSON结果对象</param>
        /// <returns>C#结构化结果</returns>
        private Utils.CSharpResult ParseToStructResult(JObject resultObject)
        {
            // 这个方法保留作为备用，现在主要使用 ParseSingleImageResult
            var sampleResults = new List<Utils.CSharpSampleResult>();
            var sampleResult = ParseSingleImageResult(resultObject);
            sampleResults.Add(sampleResult);
            return new Utils.CSharpResult(sampleResults);
        }

        /// <summary>
        /// 释放模型资源
        /// </summary>
        public void FreeModel()
        {
            if (_disposed || _modelIndex == -1)
                return;

            try
            {
                var request = new
                {
                    model_index = _modelIndex
                };

                string jsonContent = JsonConvert.SerializeObject(request);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = _httpClient.PostAsync($"{_serverUrl}/free_model", content).Result;
                var responseJson = response.Content.ReadAsStringAsync().Result;

                Console.WriteLine($"Model free result: {responseJson}");
                _modelIndex = -1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"释放模型失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查对象是否已被释放
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Model));
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源的核心实现
        /// </summary>
        /// <param name="disposing">是否正在释放托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    FreeModel();
                    _httpClient?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
