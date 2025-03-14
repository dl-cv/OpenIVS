using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp.Extensions;
using dlcv_infer_csharp;
using static dlcv_infer_csharp.Utils;
using OpenCvSharp;

namespace zhengtai_ui
{
    /// <summary>
    /// 负责模型推理的核心类，支持DLL模式和API模式的推理
    /// </summary>
    public class Inference
    {
        // 模型配置相关字段
        private string _apiModelPath;
        private Model _model;
        
        // 模式和状态标志
        private bool _apiFlag = false;    // API模式标志
        private bool _dllFlag = false;    // DLL模式标志
        private bool _inferFlag = false;  // 推理开关标志

        /// <summary>
        /// 设置API模式的模型路径
        /// </summary>
        /// <param name="path">模型文件路径</param>
        public void SetApiModelPath(string path)
        {
            _apiModelPath = path;
            _apiFlag = true; // 启用API模式
            _dllFlag = false; // 禁用DLL模式
        }

        /// <summary>
        /// 设置DLL模式的模型
        /// </summary>
        /// <param name="model">模型对象</param>
        public void SetModel(Model model)
        {
            _model = model;
            _dllFlag = true; // 启用DLL模式
            _apiFlag = false; // 禁用API模式
        }

        /// <summary>
        /// 禁用推理功能
        /// </summary>
        public void DisableInference()
        {
            _inferFlag = false;
        }

        /// <summary>
        /// 启用推理功能
        /// </summary>
        public void EnableInference()
        {
            _inferFlag = true;
        }

        /// <summary>
        /// 处理图像推理的主方法
        /// </summary>
        /// <param name="bitmap">待推理的位图</param>
        /// <param name="filePath">图像文件路径（可选）</param>
        /// <returns>推理结果，如果推理未启用则返回null</returns>
        public dynamic ProcessInference(Bitmap bitmap, string filePath)
        {
            if (!_inferFlag) return null;

            try
            {
                if (_dllFlag)
                {
                    using (var mat = BitmapConverter.ToMat(bitmap))
                    {
                        return _model.Infer(mat,true);
                    }
                }
                else if (_apiFlag)
                {
                    return !string.IsNullOrEmpty(filePath)
                        ? ProcessImagePath(filePath)
                        : ProcessMatImage(bitmap);
                }
                return null;
            }
            catch (Exception ex)
            {
                throw ex;
                //var errorType = _dllFlag ? "dll" : "Http";
                //throw new ApplicationException($"{errorType}推理失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 处理图像文件路径推理
        /// </summary>
        private dynamic ProcessImagePath(string imagePath)
        {
            // 验证模型路径和图片路径
            if (string.IsNullOrEmpty(_apiModelPath) || !File.Exists(_apiModelPath))
            {
                throw new Exception("模型路径无效或文件不存在");
            }

            if (!File.Exists(imagePath))
            {
                throw new Exception("图片路径无效或文件不存在");
            }

            string base64String = Convert.ToBase64String(File.ReadAllBytes(imagePath));
            return SendInferenceRequest(base64String);
        }

        /// <summary>
        /// 处理内存中位图的推理
        /// </summary>
        private dynamic ProcessMatImage(Bitmap matImage)
        {
            if (string.IsNullOrEmpty(_apiModelPath) || !File.Exists(_apiModelPath))
            {
                throw new Exception("模型路径无效或文件不存在");
            }

            using (var ms = new MemoryStream())
            {
                matImage.Save(ms, ImageFormat.Png);
                string base64String = Convert.ToBase64String(ms.ToArray());
                return SendInferenceRequest(base64String);
            }
        }

        /// <summary>
        /// 发送推理请求并处理响应
        /// </summary>
        /// <param name="base64Image">Base64编码的图像数据</param>
        /// <returns>处理后的推理结果</returns>
        private CSharpResult SendInferenceRequest(string base64Image)
        {
            var request = new
            {
                img = base64Image,
                model_path = _apiModelPath
            };

            using (var client = new HttpClient())
            using (var content = new StringContent(
                JsonConvert.SerializeObject(request),
                Encoding.UTF8,
                "application/json"))
            {
                var response = client.PostAsync(
                    "http://127.0.0.1:9890/api/inference",
                    content).Result;

                if (!response.IsSuccessStatusCode)
                {
                    HandleErrorResponse(response.StatusCode);
                }

                var responseString = response.Content.ReadAsStringAsync().Result;
                var result = JObject.Parse(responseString);

                if (result["code"]?.ToString() != "00000")
                {
                    throw new Exception($"API错误: {result["message"]?.ToString()}");
                }

                // 获取结果
                var results = result["results"];
                
                // 转换边界框格式从[x1,y1,x2,y2]到[x,y,w,h]
                ConvertBboxFormat(results);
                var sampleResults = new List<Utils.CSharpSampleResult>();
                var objectResults = new List<Utils.CSharpObjectResult>();

                foreach (var obj in results as JArray)
                {
                    // 解析基础字段
                    var categoryId = (int)obj["category_id"];
                    var categoryName = (string)obj["category_name"];
                    var score = (float)(double)obj["score"];
                    var area = (float)(double)obj["area"];
                    var bbox = obj["bbox"].ToObject<List<double>>();
                    var withMask = (bool)obj["with_mask"];

                    //// 处理 mask 数据
                    Mat maskImg = null;
                    //if (withMask)
                    //{
                    //    var mask = obj["mask"];
                    //    int width = (int)mask["width"];
                    //    int height = (int)mask["height"];
                    //    IntPtr maskPtr = new IntPtr((long)mask["mask_ptr"]);
                    //    maskImg = new Mat(height, width, MatType.CV_8UC1, maskPtr);
                    //    maskImg = maskImg.Clone(); // 克隆数据避免指针失效
                    //}

                    // 构建对象结果
                    var objectResult = new Utils.CSharpObjectResult(
                        categoryId,
                        categoryName,
                        score,
                        area,
                        bbox,
                        withMask,
                        maskImg
                    );
                    objectResults.Add(objectResult);
                }

                // 封装成 CSharpResult 结构
                sampleResults.Add(new Utils.CSharpSampleResult(objectResults));
                return new CSharpResult(sampleResults);
            }
        }

        /// <summary>
        /// 将边界框格式从[x1,y1,x2,y2]转换为[x,y,w,h]
        /// </summary>
        /// <param name="results">推理结果</param>
        private void ConvertBboxFormat(JToken results)
        {
            if (results == null) return;

            foreach (JObject item in results)
            {
                var bbox = item["bbox"] as JArray;
                if (bbox != null && bbox.Count == 4)
                {
                    float x1 = bbox[0].Value<float>();
                    float y1 = bbox[1].Value<float>();
                    float x2 = bbox[2].Value<float>();
                    float y2 = bbox[3].Value<float>();

                    // 计算宽高
                    float width = x2 - x1;
                    float height = y2 - y1;

                    // 替换原始数组
                    item["bbox"] = new JArray(x1, y1, width, height);
                }
            }
        }

        /// <summary>
        /// 处理HTTP错误响应
        /// </summary>
        private void HandleErrorResponse(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case (HttpStatusCode)429:
                    throw new Exception("触发速率限制，当前限制为1fps");
                case HttpStatusCode.BadRequest:
                    throw new Exception("请求参数错误");
                case HttpStatusCode.NotFound:
                    throw new Exception("API端点未找到");
                case HttpStatusCode.InternalServerError:
                    throw new Exception("服务器内部错误");
                default:
                    throw new Exception($"HTTP请求失败: {(int)statusCode}");
            }
        }
    }
}
