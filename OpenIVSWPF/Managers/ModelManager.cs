using System;
using System.Drawing;
using System.IO;
using System.Text;
using dlcv_infer_csharp;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using DLCV;
using System.Drawing.Imaging;
using System.Net.Http;

namespace OpenIVSWPF.Managers
{
    /// <summary>
    /// AI模型初始化和推理管理类
    /// </summary>
    public class ModelManager
    {
        private Model _model;
        private DlcvHttpApi _httpApi;
        private bool _isModelLoaded = false;
        private string _modelPath;
        private string _modelType;
        private Action<string> _statusCallback;
        private Action<string> _modelStatusCallback;
        private Action<Bitmap, object> _displayImageCallback;

        public bool IsLoaded => _isModelLoaded;
        public string ModelPath => _modelPath;
        public string ModelType => _modelType;

        public ModelManager(Action<string> statusCallback, Action<string> modelStatusCallback, Action<Bitmap, object> displayImageCallback)
        {
            _statusCallback = statusCallback;
            _modelStatusCallback = modelStatusCallback;
            _displayImageCallback = displayImageCallback;
        }

        /// <summary>
        /// 初始化AI模型
        /// </summary>
        /// <param name="settings">系统设置</param>
        public void InitializeModel(Settings settings)
        {
            try
            {
                _statusCallback?.Invoke("正在加载AI模型...");

                // 检查模型文件是否存在
                if (!File.Exists(settings.ModelPath))
                {
                    _statusCallback?.Invoke($"模型文件不存在：{settings.ModelPath}");
                    _modelStatusCallback?.Invoke("模型文件不存在");
                    return;
                }

                _modelPath = settings.ModelPath;
                _modelType = settings.ModelType;

                // 根据模型类型选择不同的加载方式
                if (_modelType == "DVT")
                {
                    // 原有的DVT模型加载方式
                    _model = new Model(settings.ModelPath, 0); // 使用第一个GPU设备
                    _httpApi = null; // 清空HTTP API引用
                }
                else if (_modelType == "DVP")
                {
                    // 新增的DVP模型HTTP加载方式
                    _httpApi = new DlcvHttpApi();
                    
                    // 检查服务器是否运行
                    if (!DlcvHttpApi.IsLocalServerRunning())
                    {
                        _statusCallback?.Invoke("HTTP推理服务器未运行，请先启动服务器");
                        _modelStatusCallback?.Invoke("服务器未运行");
                        return;
                    }
                    
                    // 连接服务器
                    if (!_httpApi.Connect())
                    {
                        _statusCallback?.Invoke("无法连接到HTTP推理服务器");
                        _modelStatusCallback?.Invoke("连接失败");
                        return;
                    }
                    
                    // 尝试加载模型到服务器
                    try
                    {
                        _statusCallback?.Invoke("正在加载模型到服务器...");
                        var loadResult = _httpApi.LoadModel(settings.ModelPath);
                        if (loadResult == null || loadResult["code"]?.ToString() != "00000")
                        {
                            _statusCallback?.Invoke("模型服务器加载失败");
                            _modelStatusCallback?.Invoke("加载失败");
                            return;
                        }
                        _statusCallback?.Invoke($"模型已加载到服务器: {loadResult["message"]}");
                    }
                    catch (Exception ex)
                    {
                        _statusCallback?.Invoke($"加载模型到服务器失败: {ex.Message}");
                        _modelStatusCallback?.Invoke("加载失败");
                        return;
                    }
                    
                    // 尝试加载模型，测试连接和模型有效性
                    try
                    {
                        var modelInfo = _httpApi.GetModelInfo(settings.ModelPath);
                        if (modelInfo == null)
                        {
                            _statusCallback?.Invoke("模型加载失败，无法获取模型信息");
                            _modelStatusCallback?.Invoke("加载失败");
                            return;
                        }
                        _statusCallback?.Invoke($"HTTP API模型已加载: {modelInfo["message"]}");
                    }
                    catch (Exception ex)
                    {
                        _statusCallback?.Invoke($"HTTP API模型加载失败: {ex.Message}");
                        _modelStatusCallback?.Invoke("加载失败");
                        return;
                    }
                    
                    _model = null; // 清空本地模型引用
                }
                else
                {
                    _statusCallback?.Invoke($"不支持的模型类型: {_modelType}");
                    _modelStatusCallback?.Invoke("不支持的类型");
                    return;
                }

                _isModelLoaded = true;
                _statusCallback?.Invoke($"AI模型已加载: {Path.GetFileName(settings.ModelPath)}");
                _modelStatusCallback?.Invoke("已加载");
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"AI模型加载错误：{ex.Message}");
                _modelStatusCallback?.Invoke("加载错误");
                throw;
            }
        }

        /// <summary>
        /// 执行AI模型推理
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <returns>推理结果文本</returns>
        public string PerformInference(Bitmap image)
        {
            try
            {
                if (!_isModelLoaded || image == null)
                    return "无效的输入";

                // 根据模型类型选择不同的推理方式
                if (_modelType == "DVT" && _model != null)
                {
                    // 原有的DVT模型推理方式
                    return PerformDvtInference(image);
                }
                else if (_modelType == "DVP" && _httpApi != null)
                {
                    // 新增的DVP模型HTTP推理方式
                    return PerformDvpInference(image);
                }
                else
                {
                    _statusCallback?.Invoke($"模型未正确加载或不支持的模型类型: {_modelType}");
                    return "模型未正确加载";
                }
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"AI推理过程中发生错误：{ex.Message}");
                return $"推理错误: {ex.Message}";
            }
        }

        /// <summary>
        /// 使用DVT模型执行推理
        /// </summary>
        private string PerformDvtInference(Bitmap image)
        {
            // 将Bitmap转换为Mat
            Mat mat = BitmapToMat(image);
            if (mat == null) return "图像转换失败";

            // 创建批处理列表
            var imageList = new System.Collections.Generic.List<Mat> { mat };

            // 执行推理
            dlcv_infer_csharp.Utils.CSharpResult result = _model.InferBatch(imageList);

            // 提取结果文本
            StringBuilder sb = new StringBuilder();
            var sampleResults = result.SampleResults[0];

            foreach (var item in sampleResults.Results)
            {
                sb.AppendLine($"{item.CategoryName}: {item.Score:F2}");
            }

            // 更新ImageViewer以显示检测结果
            _displayImageCallback?.Invoke(image, result);

            // 返回结果文本
            return sb.ToString();
        }

        /// <summary>
        /// 使用DVP模型通过HTTP API执行推理
        /// </summary>
        private string PerformDvpInference(Bitmap image)
        {
            try
            {
                // 执行HTTP推理
                dlcv_infer_csharp.Utils.CSharpResult result = _httpApi.InferBitmap(image, _modelPath);

                // 提取结果文本
                StringBuilder sb = new StringBuilder();
                if ( result.SampleResults.Count > 0)
                {
                    var sampleResults = result.SampleResults[0];

                    foreach (var item in sampleResults.Results)
                    {
                        sb.AppendLine($"{item.CategoryName}: {item.Score:F2}");
                    }

                    // 更新ImageViewer以显示检测结果
                    _displayImageCallback?.Invoke(image, result);
                }
                else
                {
                    sb.AppendLine("未检测到结果");
                }

                // 返回结果文本
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"HTTP API推理错误: {ex.Message}");
                return $"HTTP推理错误: {ex.Message}";
            }
        }

        /// <summary>
        /// 将Bitmap转换为Mat
        /// </summary>
        /// <param name="bitmap">输入Bitmap图像</param>
        /// <returns>OpenCV的Mat图像</returns>
        private Mat BitmapToMat(Bitmap bitmap)
        {
            try
            {
                return BitmapConverter.ToMat(bitmap);
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"图像转换错误：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 释放模型资源
        /// </summary>
        public void Dispose()
        {
            if (_isModelLoaded)
            {
                if (_model != null)
                {
                    _model = null;
                }

                if (_httpApi != null)
                {
                    _httpApi.Dispose();
                    _httpApi = null;
                }

                _isModelLoaded = false;
            }
        }
    }
} 