using System;
using System.Drawing;
using System.IO;
using System.Text;
using dlcv_infer_csharp;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using DLCV;
using System.Drawing.Imaging;

namespace OpenIVSWPF.Managers
{
    /// <summary>
    /// AI模型初始化和推理管理类
    /// </summary>
    public class ModelManager
    {
        private Model _model;
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
                    _model = new Model(settings.ModelPath, 0); // 使用第一个GPU设备
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
                    return PerformDvtInference(image);
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

                _isModelLoaded = false;
            }
        }
    }
} 