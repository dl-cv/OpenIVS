using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using System.Linq;

namespace dlcv_infer_csharp
{
    /// <summary>
    /// OCR with Detection Model 封装类
    /// 结合检测模型和OCR识别模型进行端到端的文字检测和识别
    /// </summary>
    public class OcrWithDetModel : IDisposable
    {
        private Model _detModel;
        private Model _ocrModel;
        private bool _disposed = false;
        private float _horizontalScale = 1.0f;

        /// <summary>
        /// 获取检测模型是否已加载
        /// </summary>
        public bool IsDetModelLoaded => _detModel != null;

        /// <summary>
        /// 获取OCR模型是否已加载
        /// </summary>
        public bool IsOcrModelLoaded => _ocrModel != null;

        /// <summary>
        /// 获取两个模型是否都已加载
        /// </summary>
        public bool IsLoaded => IsDetModelLoaded && IsOcrModelLoaded;

        /// <summary>
        /// 设置水平缩放比例
        /// </summary>
        /// <param name="scale">水平缩放比例，默认1.0</param>
        public void SetHorizontalScale(float scale)
        {
            _horizontalScale = scale;
        }

        /// <summary>
        /// 获取当前水平缩放比例
        /// </summary>
        /// <returns>水平缩放比例</returns>
        public float GetHorizontalScale()
        {
            return _horizontalScale;
        }

        /// <summary>
        /// 加载检测模型和OCR模型
        /// </summary>
        /// <param name="detModelPath">检测模型路径</param>
        /// <param name="ocrModelPath">OCR识别模型路径</param>
        /// <param name="deviceId">设备ID，默认为0</param>
        public void Load(string detModelPath, string ocrModelPath, int deviceId = 0, bool enableCache = false)
        {
            try
            {
                // 加载检测模型
                Console.WriteLine($"正在加载检测模型: {detModelPath}");
                _detModel = new Model(detModelPath, deviceId, false, enableCache);
                Console.WriteLine("检测模型加载成功");

                // 加载OCR模型
                Console.WriteLine($"正在加载OCR模型: {ocrModelPath}");
                _ocrModel = new Model(ocrModelPath, deviceId, false, enableCache);
                Console.WriteLine("OCR模型加载成功");
            }
            catch (Exception ex)
            {
                // 如果加载失败，释放已加载的模型
                _detModel?.Dispose();
                _ocrModel?.Dispose();
                _detModel = null;
                _ocrModel = null;
                throw new Exception($"加载模型失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取模型信息，包含检测模型和OCR模型的信息
        /// </summary>
        /// <returns>包含det和ocr模型信息的字典</returns>
        public JObject GetModelInfo()
        {
            if (!IsLoaded)
            {
                throw new InvalidOperationException("模型未加载，请先调用Load方法加载模型");
            }

            var modelInfo = new JObject();

            try
            {
                modelInfo["det_model"] = GetDetModelInfo();
            }
            catch (Exception ex)
            {
                modelInfo["det_model"] = new JObject
                {
                    ["error"] = $"获取检测模型信息失败: {ex.Message}"
                };
            }

            try
            {
                modelInfo["ocr_model"] = GetOcrModelInfo();
            }
            catch (Exception ex)
            {
                modelInfo["ocr_model"] = new JObject
                {
                    ["error"] = $"获取OCR模型信息失败: {ex.Message}"
                };
            }

            return modelInfo;
        }

        /// <summary>
        /// 内部通用推理方法，处理单张或多张图像，返回DVP格式结果
        /// </summary>
        /// <param name="images">输入图像列表</param>
        /// <param name="params_json">推理参数</param>
        /// <returns>推理结果和空指针的元组</returns>
        public Tuple<JObject, IntPtr> InferInternal(List<Mat> images, JObject params_json)
        {
            if (!IsLoaded)
            {
                throw new InvalidOperationException("模型未加载，请先调用Load方法加载模型");
            }

            if (images == null || images.Count == 0)
            {
                throw new ArgumentException("输入图像列表为空", nameof(images));
            }

            try
            {
                var allResults = new List<JObject>();

                foreach (var image in images)
                {
                    if (image == null || image.Empty())
                    {
                        // 空图像添加空结果
                        var emptyResult = new JObject
                        {
                            ["results"] = new JArray()
                        };
                        allResults.Add(emptyResult);
                        continue;
                    }

                    // 对单张图像进行OCR推理
                    var singleResult = OcrInferInternal(image, params_json);
                    allResults.Add(singleResult);
                }

                // 将多个结果合并为统一格式（参考DVP格式）
                var mergedResult = new JObject();
                var sampleResults = new JArray();

                foreach (var result in allResults)
                {
                    var sampleResult = new JObject();
                    sampleResult["results"] = result["results"];
                    sampleResults.Add(sampleResult);
                }

                mergedResult["sample_results"] = sampleResults;

                // 返回空指针，类似DVP模式
                return new Tuple<JObject, IntPtr>(mergedResult, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                throw new Exception($"OCR推理失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 内部OCR推理方法，处理单张图像
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="params_json">推理参数</param>
        /// <returns>单张图像的推理结果</returns>
        private JObject OcrInferInternal(Mat image, JObject params_json)
        {
            // 使用检测模型进行推理
            var detResult = _detModel.Infer(image, params_json);

            var resultsArray = new JArray();

            // 遍历检测结果
            if (detResult.SampleResults != null && detResult.SampleResults.Count > 0)
            {
                foreach (var detection in detResult.SampleResults[0].Results)
                {
                    try
                    {
                        Mat roiMat = null;
                        int globalX = 0;
                        int globalY = 0;

                        // 判断是否为旋转框
                        if (detection.WithAngle && detection.Angle != -100)
                        {
                            // 处理旋转框裁剪
                            var rotatedRoi = ExtractRotatedROI(image, detection, out globalX, out globalY);
                            roiMat = rotatedRoi;
                        }
                        else
                        {
                            // 处理普通边界框
                            double x = Math.Max(0, detection.Bbox[0]);
                            double y = Math.Max(0, detection.Bbox[1]);
                            double w = Math.Min(detection.Bbox[2], image.Width - x);
                            double h = Math.Min(detection.Bbox[3], image.Height - y);

                            if (w <= 0 || h <= 0)
                                continue;

                            globalX = (int)x;
                            globalY = (int)y;

                            Rect roi = new Rect((int)x, (int)y, (int)w, (int)h);
                            roiMat = new Mat(image, roi).Clone();
                        }

                        if (roiMat == null || roiMat.Empty())
                            continue;

                        // 如果水平缩放比例不是1.0，则进行水平缩放
                        if (Math.Abs(_horizontalScale - 1.0f) > 0.001f)
                        {
                            Mat scaledRoi = new Mat();
                            int newWidth = (int)(roiMat.Width * _horizontalScale);
                            int newHeight = roiMat.Height;
                            Cv2.Resize(roiMat, scaledRoi, new OpenCvSharp.Size(newWidth, newHeight));
                            roiMat.Dispose();
                            roiMat = scaledRoi;
                        }

                        // 使用识别模型进行推理
                        var recognizeResult = _ocrModel.Infer(roiMat, params_json);

                        // 构建结果对象
                        var resultObj = new JObject
                        {
                            ["category_id"] = detection.CategoryId,
                            ["score"] = detection.Score,
                            ["area"] = detection.Area,
                            ["bbox"] = new JArray(detection.Bbox),
                            ["with_bbox"] = detection.WithBbox,
                            ["with_mask"] = detection.WithMask,
                            ["with_angle"] = detection.WithAngle
                        };

                        if (detection.WithAngle)
                        {
                            resultObj["angle"] = detection.Angle;
                        }

                        // 如果识别模型有结果，使用识别结果的类别名称
                        if (recognizeResult.SampleResults.Count > 0 &&
                            recognizeResult.SampleResults[0].Results.Count > 0)
                        {
                            var topResult = recognizeResult.SampleResults[0].Results[0];
                            resultObj["category_name"] = topResult.CategoryName;
                        }
                        else
                        {
                            resultObj["category_name"] = detection.CategoryName;
                        }

                        // 添加全局坐标信息（用于后续处理）
                        var metadata = new JObject
                        {
                            ["global_x"] = globalX,
                            ["global_y"] = globalY
                        };

                        if (detection.WithAngle)
                        {
                            metadata["global_bbox"] = new JArray
                            {
                                detection.Bbox[0], detection.Bbox[1],
                                detection.Bbox[2], detection.Bbox[3],
                                detection.Angle
                            };
                        }

                        resultObj["metadata"] = metadata;

                        resultsArray.Add(resultObj);

                        roiMat?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"处理检测结果失败: {ex.Message}");
                        continue;
                    }
                }
            }

            return new JObject
            {
                ["results"] = resultsArray
            };
        }

        /// <summary>
        /// 提取旋转框ROI区域
        /// </summary>
        /// <param name="image">原始图像</param>
        /// <param name="detection">检测结果</param>
        /// <param name="globalX">输出全局X坐标</param>
        /// <param name="globalY">输出全局Y坐标</param>
        /// <returns>旋转裁剪后的图像</returns>
        private Mat ExtractRotatedROI(Mat image, Utils.CSharpObjectResult detection, out int globalX, out int globalY)
        {
            // 获取旋转框信息 [cx, cy, w, h]
            double centerX = detection.Bbox[0];
            double centerY = detection.Bbox[1];
            double width = detection.Bbox[2];
            double height = detection.Bbox[3];
            double angle = detection.Angle;

            // 将弧度转换为角度
            double angleDegree = angle * 180.0 / Math.PI;

            // 计算旋转矩阵
            var rotMat = Cv2.GetRotationMatrix2D(new Point2f((float)centerX, (float)centerY), angleDegree, 1.0);

            // 调整平移参数，使得旋转中心移动到输出图像中心
            rotMat.Set<double>(0, 2, rotMat.Get<double>(0, 2) + (width / 2) - centerX);
            rotMat.Set<double>(1, 2, rotMat.Get<double>(1, 2) + (height / 2) - centerY);

            // 一步完成旋转和裁剪
            Mat rotatedImage = new Mat();
            Cv2.WarpAffine(image, rotatedImage, rotMat, new Size((int)width, (int)height));

            // 计算全局坐标
            globalX = (int)(centerX - width / 2);
            globalY = (int)(centerY - height / 2);

            return rotatedImage;
        }

        /// <summary>
        /// 处理推理结果到CSharpResult对象
        /// </summary>
        /// <param name="resultObject">推理结果JSON对象</param>
        /// <returns>结构化结果对象</returns>
        public Utils.CSharpResult ParseToStructResult(JObject resultObject)
        {
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

                    bool withBbox = result["with_bbox"]?.Value<bool>() ?? (bbox.Count > 0);
                    bool withMask = result["with_mask"]?.Value<bool>() ?? false;
                    bool withAngle = result["with_angle"]?.Value<bool>() ?? false;
                    float angle = result["angle"]?.Value<float>() ?? -100f;

                    Mat mask_img = new Mat(); // OCR通常不需要mask

                    var objectResult = new Utils.CSharpObjectResult(categoryId, categoryName, score, area, bbox,
                        withMask, mask_img, withBbox, withAngle, angle);
                    results.Add(objectResult);
                }

                var sampleResultObj = new Utils.CSharpSampleResult(results);
                sampleResults.Add(sampleResultObj);
            }

            return new Utils.CSharpResult(sampleResults);
        }

        /// <summary>
        /// 执行推理，返回结构化结果
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="paramsJson">推理参数（可选）</param>
        /// <returns>OCR推理结果</returns>
        public Utils.CSharpResult Infer(Mat image, JObject paramsJson = null)
        {
            var resultTuple = InferInternal(new List<Mat> { image }, paramsJson);
            try
            {
                return ParseToStructResult(resultTuple.Item1);
            }
            finally
            {
                // 处理完后释放结果，这里返回的是空指针，不需要释放
                if (resultTuple.Item2 != IntPtr.Zero)
                {
                    // 这里实际上不会执行，因为我们返回的是空指针
                }
            }
        }

        /// <summary>
        /// 批量推理
        /// </summary>
        /// <param name="imageList">输入图像列表</param>
        /// <param name="paramsJson">推理参数（可选）</param>
        /// <returns>批量OCR推理结果</returns>
        public Utils.CSharpResult InferBatch(List<Mat> imageList, JObject paramsJson = null)
        {
            var resultTuple = InferInternal(imageList, paramsJson);
            try
            {
                return ParseToStructResult(resultTuple.Item1);
            }
            finally
            {
                // 处理完后释放结果，这里返回的是空指针，不需要释放
                if (resultTuple.Item2 != IntPtr.Zero)
                {
                    // 这里实际上不会执行，因为我们返回的是空指针
                }
            }
        }

        /// <summary>
        /// 对单张图片进行推理，返回JSON格式的结果
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="params_json">可选的推理参数</param>
        /// <returns>JSON格式的检测结果数组</returns>
        public dynamic InferOneOutJson(Mat image, JObject params_json = null)
        {
            var resultTuple = InferInternal(new List<Mat> { image }, params_json);
            try
            {
                var results = resultTuple.Item1["sample_results"][0]["results"] as JArray;
                return results;
            }
            finally
            {
                // 处理完后释放结果，这里返回的是空指针，不需要释放
                if (resultTuple.Item2 != IntPtr.Zero)
                {
                    // 这里实际上不会执行，因为我们返回的是空指针
                }
            }
        }

        /// <summary>
        /// 释放模型资源
        /// </summary>
        public void FreeModel()
        {
            _detModel?.FreeModel();
            _ocrModel?.FreeModel();
        }

        /// <summary>
        /// 获取检测模型信息
        /// </summary>
        /// <returns>检测模型信息</returns>
        public JObject GetDetModelInfo()
        {
            if (!IsDetModelLoaded)
            {
                throw new InvalidOperationException("检测模型未加载");
            }
            return _detModel.GetModelInfo();
        }

        /// <summary>
        /// 获取OCR模型信息
        /// </summary>
        /// <returns>OCR模型信息</returns>
        public JObject GetOcrModelInfo()
        {
            if (!IsOcrModelLoaded)
            {
                throw new InvalidOperationException("OCR模型未加载");
            }
            return _ocrModel.GetModelInfo();
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
                    _detModel?.Dispose();
                    _ocrModel?.Dispose();
                }

                _disposed = true;
            }
        }

        ~OcrWithDetModel()
        {
            Dispose(false);
        }
    }
}

