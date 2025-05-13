using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using sntl_admin_csharp;
using System.Linq;

namespace dlcv_infer_csharp
{
    public class DllLoader
    {
        private string DllName = "dlcv_infer.dll";
        private string DllName2 = "dlcv_infer2.dll";
        private string DllPath = @"C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll";
        private string DllPath2 = @"C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer2.dll";
        private const CallingConvention calling_method = CallingConvention.StdCall;

        // 定义导入方法的委托
        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr LoadModelDelegate(string config_str);
        public LoadModelDelegate dlcv_load_model;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr FreeModelDelegate(string config_str);
        public FreeModelDelegate dlcv_free_model;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr GetModelInfoDelegate(string config_str);
        public GetModelInfoDelegate dlcv_get_model_info;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr InferDelegate(string config_str);
        public InferDelegate dlcv_infer;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate void FreeModelResultDelegate(IntPtr config_str);
        public FreeModelResultDelegate dlcv_free_model_result;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate void FreeResultDelegate(IntPtr config_str);
        public FreeResultDelegate dlcv_free_result;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate void FreeAllModelsDelegate();
        public FreeAllModelsDelegate dlcv_free_all_models;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate IntPtr GetDeviceInfo();
        public GetDeviceInfo dlcv_get_device_info;

        private void LoadDll()
        {
            JArray feature_list = new JArray();
            try
            {
                SNTL sNTL = new SNTL();
                feature_list = sNTL.GetFeatureList();

                if (feature_list.Any(item => item.ToString() == "1"))
                {

                }
                else if (feature_list.Any(item => item.ToString() == "2"))
                {
                    DllName = DllName2;
                    DllPath = DllPath2;
                }
            }
            catch (Exception ex)
            {
                // 如果获取特征列表失败，则使用默认的 DLL 路径
            }

            IntPtr hModule = LoadLibrary(DllName);
            if (hModule == IntPtr.Zero)
            {
                // 如果当前目录下的 DLL 加载失败，尝试加载指定路径的 DLL
                hModule = LoadLibrary(DllPath);
                if (hModule == IntPtr.Zero)
                {
                    throw new Exception("无法加载 DLL");
                }
            }

            // 获取函数指针
            dlcv_load_model = GetDelegate<LoadModelDelegate>(hModule, "dlcv_load_model");
            dlcv_free_model = GetDelegate<FreeModelDelegate>(hModule, "dlcv_free_model");
            dlcv_get_model_info = GetDelegate<GetModelInfoDelegate>(hModule, "dlcv_get_model_info");
            dlcv_infer = GetDelegate<InferDelegate>(hModule, "dlcv_infer");
            dlcv_free_model_result = GetDelegate<FreeModelResultDelegate>(hModule, "dlcv_free_model_result");
            dlcv_free_result = GetDelegate<FreeResultDelegate>(hModule, "dlcv_free_result");
            dlcv_free_all_models = GetDelegate<FreeAllModelsDelegate>(hModule, "dlcv_free_all_models");
            dlcv_get_device_info = GetDelegate<GetDeviceInfo>(hModule, "dlcv_get_device_info");
        }

        private T GetDelegate<T>(IntPtr hModule, string procedureName) where T : Delegate
        {
            IntPtr pAddressOfFunctionToCall = GetProcAddress(hModule, procedureName);
            return (T)Marshal.GetDelegateForFunctionPointer(pAddressOfFunctionToCall, typeof(T));
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        private static readonly Lazy<DllLoader> _instance = new Lazy<DllLoader>(() => new DllLoader());

        public static DllLoader Instance => _instance.Value;
        private DllLoader()
        {
            LoadDll();
        }
    }
    public class Model
    {
        protected int modelIndex = -1;

        public Model()
        {

        }

        public Model(string modelPath, int device_id)
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

            Console.WriteLine("Model load result: " + resultObject.ToString());
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

        ~Model()
        {
            var config = new JObject
            {
                ["model_index"] = modelIndex
            };
            string jsonStr = config.ToString();
            IntPtr resultPtr = DllLoader.Instance.dlcv_free_model(jsonStr);
            var resultJson = Marshal.PtrToStringAnsi(resultPtr);
            var resultObject = JObject.Parse(resultJson);
            Console.WriteLine(
                "Model free result: " + resultObject.ToString());
            DllLoader.Instance.dlcv_free_result(resultPtr);
        }

        public void FreeModel()
        {
            var config = new JObject
            {
                ["model_index"] = modelIndex
            };
            string jsonStr = config.ToString();
            IntPtr resultPtr = DllLoader.Instance.dlcv_free_model(jsonStr);
            var resultJson = Marshal.PtrToStringAnsi(resultPtr);
            var resultObject = JObject.Parse(resultJson);
            Console.WriteLine(
                "Model free result: " + resultObject.ToString());
            DllLoader.Instance.dlcv_free_result(resultPtr);
        }

        public JObject GetModelInfo()
        {
            var config = new JObject
            {
                ["model_index"] = modelIndex
            };

            string jsonStr = config.ToString();
            IntPtr resultPtr = DllLoader.Instance.dlcv_get_model_info(jsonStr);
            var resultJson = Marshal.PtrToStringAnsi(resultPtr);
            var resultObject = JObject.Parse(resultJson);

            Console.WriteLine("Model info: " + resultObject.ToString());
            DllLoader.Instance.dlcv_free_result(resultPtr);
            return resultObject;
        }

        // 内部通用推理方法，处理单张或多张图像
        private Tuple<JObject, IntPtr> InferInternal(List<Mat> images, JObject params_json)
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
        private Utils.CSharpResult ParseToStructResult(JObject resultObject)
        {
            // 解析 json 结果
            var sampleResults = new List<Utils.CSharpSampleResult>();
            var sampleResultsArray = resultObject["sample_results"] as JArray;

            foreach (var sampleResult in sampleResultsArray)
            {
                var results = new List<Utils.CSharpObjectResult>();
                var resultsArray = sampleResult["results"] as JArray;

                foreach (var result in resultsArray)
                {
                    var categoryId = (int)result["category_id"];
                    var categoryName = (string)result["category_name"];
                    var score = (float)result["score"];
                    var area = (float)result["area"];
                    var bbox = result["bbox"].ToObject<List<double>>();
                    var withMask = (bool)result["with_mask"];
                    var angle = result.Contains("angle") ? (float)result["angle"] : float.NaN;

                    var mask = result["mask"];
                    int mask_width = (int)mask["width"];
                    int mask_height = (int)mask["height"];
                    Mat mask_img = new Mat();
                    if (withMask)
                    {
                        IntPtr mask_ptr = new IntPtr((long)mask["mask_ptr"]);
                        mask_img = Mat.FromPixelData(mask_height, mask_width, MatType.CV_8UC1, mask_ptr);
                        mask_img = mask_img.Clone();
                    }

                    var objectResult = new Utils.CSharpObjectResult(categoryId, categoryName, score, area, bbox, withMask, mask_img, angle);
                    results.Add(objectResult);
                }

                var sampleResultObj = new Utils.CSharpSampleResult(results);
                sampleResults.Add(sampleResultObj);
            }

            return new Utils.CSharpResult(sampleResults);
        }

        public Utils.CSharpResult Infer(Mat image, JObject params_json = null)
        {
            // 将单张图像放入列表中处理
            var resultTuple = InferInternal(new List<Mat> { image }, params_json);
            try
            {
                return ParseToStructResult(resultTuple.Item1);
            }
            finally
            {
                // 处理完后释放结果
                DllLoader.Instance.dlcv_free_model_result(resultTuple.Item2);
            }
        }

        public Utils.CSharpResult InferBatch(List<Mat> image_list, JObject params_json = null)
        {
            var resultTuple = InferInternal(image_list, params_json);
            try
            {
                return ParseToStructResult(resultTuple.Item1);
            }
            finally
            {
                // 处理完后释放结果
                DllLoader.Instance.dlcv_free_model_result(resultTuple.Item2);
            }
        }

        /// <summary>
        /// 对单张图片进行推理，返回JSON格式的结果
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="params_json">可选的推理参数，用于配置推理过程</param>
        /// <returns>
        /// 返回JSON格式的检测结果数组，每个元素包含：
        /// - category_id: 类别ID
        /// - category_name: 类别名称
        /// - score: 检测置信度
        /// - area: 检测框面积
        /// - bbox: 检测框坐标 [x,y,w,h] 或 [cx,cy,w,h]
        /// - with_mask: 是否包含mask
        /// - mask: 如果with_mask为true，则包含mask信息
        ///   - 对于实例分割/语义分割：返回mask的多边形点集
        ///   - 每个点包含x,y坐标，坐标是相对于原图的绝对坐标
        /// - angle: 如果是旋转框检测，则包含旋转角度（弧度）
        /// </returns>
        public dynamic InferOneOutJson(Mat image, JObject params_json = null)
        {
            var resultTuple = InferInternal(new List<Mat> { image }, params_json);
            try
            {
                var results = resultTuple.Item1["sample_results"][0]["results"] as JArray;
                foreach (var result in results)
                {
                    var bbox = result["bbox"].ToObject<List<double>>();
                    var withMask = (bool)result["with_mask"];

                    var mask = result["mask"];
                    int mask_width = (int)mask["width"];
                    int mask_height = (int)mask["height"];
                    int width = (int)bbox[2];
                    int height = (int)bbox[3];

                    Mat mask_img = new Mat();
                    if (withMask)
                    {
                        IntPtr mask_ptr = new IntPtr((long)mask["mask_ptr"]);
                        mask_img = Mat.FromPixelData(mask_height, mask_width, MatType.CV_8UC1, mask_ptr);

                        if (mask_img.Cols != width || mask_img.Rows != height)
                        {
                            mask_img = mask_img.Resize(new Size(width, height));
                        }

                        Point[][] points = mask_img.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                        JArray pointsJson = new JArray();
                        foreach (var point in points[0])
                        {
                            JObject point_obj = new JObject
                            {
                                ["x"] = (int)(point.X + bbox[0]),
                                ["y"] = (int)(point.Y + bbox[1])
                            };
                            pointsJson.Add(point_obj);
                        }
                        result["mask"] = pointsJson;
                    }
                }
                return results;
            }
            finally
            {
                // 处理完后释放结果
                DllLoader.Instance.dlcv_free_model_result(resultTuple.Item2);
            }
        }
    }

    public class SlidingWindowModel : Model
    {
        public SlidingWindowModel(
            string modelPath,
            int device_id,
            int small_img_width = 832,
            int small_img_height = 704,
            int horizontal_overlap = 16,
            int vertical_overlap = 16,
            float threshold = 0.5f,
            float iou_threshold = 0.2f,
            float combine_ios_threshold = 0.2f)
        {
            var config = new JObject
            {
                ["type"] = "sliding_window_pipeline",
                ["model_path"] = modelPath,
                ["device_id"] = device_id,
                ["small_img_width"] = small_img_width,
                ["small_img_height"] = small_img_height,
                ["horizontal_overlap"] = horizontal_overlap,
                ["vertical_overlap"] = vertical_overlap,
                ["threshold"] = threshold,
                ["iou_threshold"] = iou_threshold,
                ["combine_ios_threshold"] = combine_ios_threshold
            };

            var setting = new JsonSerializerSettings() { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii };

            string jsonStr = JsonConvert.SerializeObject(config, setting);

            IntPtr resultPtr = DllLoader.Instance.dlcv_load_model(jsonStr);
            var resultJson = Marshal.PtrToStringAnsi(resultPtr);
            var resultObject = JObject.Parse(resultJson);

            Console.WriteLine("SlidingWindowModel load result: " + resultObject.ToString());
            if (resultObject.ContainsKey("model_index"))
            {
                modelIndex = resultObject["model_index"].Value<int>();
            }
            else
            {
                throw new Exception("加载滑窗裁图模型失败：" + resultObject.ToString());
            }
            DllLoader.Instance.dlcv_free_result(resultPtr);
        }
    }

    public class Utils
    {
        /// <summary>
        /// 单个检测物体的结果
        /// </summary>
        public struct CSharpObjectResult
        {
            /// <summary>
            /// 类别ID
            /// </summary>
            public int CategoryId { get; set; }

            /// <summary>
            /// 类别名称
            /// </summary>
            public string CategoryName { get; set; }

            /// <summary>
            /// 检测置信度
            /// </summary>
            public float Score { get; set; }

            /// <summary>
            /// 检测框的面积
            /// </summary>
            public float Area { get; set; }

            /// <summary>
            /// 检测框坐标
            /// 对于普通目标检测/实例分割/语义分割：[x, y, w, h]，其中(x,y)为左上角坐标
            /// 对于旋转框检测：[cx, cy, w, h]，其中(cx,cy)为中心点坐标
            /// </summary>
            public List<double> Bbox { get; set; }

            /// <summary>
            /// 是否包含mask信息（仅实例分割和语义分割任务会有）
            /// </summary>
            public bool WithMask { get; set; }

            /// <summary>
            /// 实例分割或语义分割的mask
            /// 0表示非目标像素，255表示目标像素
            /// 尺寸与Bbox中的宽高一致
            /// </summary>
            public Mat Mask { get; set; }

            /// <summary>
            /// 旋转框的角度（弧度制）
            /// 仅旋转框检测任务会有此值，其他任务为NaN
            /// </summary>
            public float Angle { get; set; }

            public CSharpObjectResult(int categoryId, string categoryName, float score, float area, List<double> bbox, bool withMask, Mat mask, float angle = float.NaN)
            {
                CategoryId = categoryId;
                CategoryName = categoryName;
                Score = score;
                Area = area;
                Bbox = bbox;
                WithMask = withMask;
                Mask = mask;
                Angle = angle;
            }
        }

        /// <summary>
        /// 单张图片的检测结果
        /// 包含该图片中所有检测到的物体信息
        /// </summary>
        public struct CSharpSampleResult
        {
            /// <summary>
            /// 该图片中所有检测到的物体列表
            /// </summary>
            public List<CSharpObjectResult> Results { get; set; }

            public CSharpSampleResult(List<CSharpObjectResult> results)
            {
                Results = results;
            }
        }

        /// <summary>
        /// 批量图片的检测结果
        /// 每个元素对应一张图片的检测结果
        /// </summary>
        public struct CSharpResult
        {
            /// <summary>
            /// 所有图片的检测结果列表
            /// </summary>
            public List<CSharpSampleResult> SampleResults { get; set; }

            public CSharpResult(List<CSharpSampleResult> sampleResults)
            {
                SampleResults = sampleResults;
            }
        }

        public static String jsonToString(JObject json)
        {
            var settings = new JsonSerializerSettings
            {
                StringEscapeHandling = StringEscapeHandling.EscapeNonAscii // 关键配置
            };
            string unicodeJson = JsonConvert.SerializeObject(json, Formatting.Indented, settings);
            return unicodeJson;
        }

        public static String jsonToString(JArray json)
        {
            var settings = new JsonSerializerSettings
            {
                StringEscapeHandling = StringEscapeHandling.EscapeNonAscii // 关键配置
            };
            string unicodeJson = JsonConvert.SerializeObject(json, Formatting.Indented, settings);
            return unicodeJson;
        }

        public static void FreeAllModels()
        {
            DllLoader.Instance.dlcv_free_all_models();
        }

        public static JObject GetDeviceInfo()
        {
            IntPtr resultPtr = DllLoader.Instance.dlcv_get_device_info();
            var resultJson = Marshal.PtrToStringAnsi(resultPtr);
            var resultObject = JObject.Parse(resultJson);
            DllLoader.Instance.dlcv_free_result(resultPtr);
            return resultObject;
        }

        /// <summary>
        /// OCR推理函数，使用检测模型和识别模型进行推理
        /// </summary>
        /// <param name="detectModel">检测模型</param>
        /// <param name="recognizeModel">识别模型</param>
        /// <param name="image">输入图像</param>
        /// <returns>包含OCR结果的CSharpResult</returns>
        public static CSharpResult OcrInfer(Model detectModel, Model recognizeModel, Mat image)
        {
            try
            {
                // 使用检测模型进行推理
                var imageList = new List<Mat> { image };
                CSharpResult result = detectModel.InferBatch(imageList);

                // 遍历第一个模型的检测结果
                foreach (var sampleResult in result.SampleResults)
                {
                    for (int i = 0; i < sampleResult.Results.Count; i++)
                    {
                        var detection = sampleResult.Results[i];

                        // 获取边界框坐标 (x, y, w, h)
                        double x = detection.Bbox[0];
                        double y = detection.Bbox[1];
                        double w = detection.Bbox[2];
                        double h = detection.Bbox[3];

                        // 确保坐标在有效范围内
                        x = Math.Max(0, x);
                        y = Math.Max(0, y);
                        w = Math.Min(w, image.Width - x);
                        h = Math.Min(h, image.Height - y);

                        if (w <= 0 || h <= 0)
                            continue;

                        // 提取ROI区域
                        Rect roi = new Rect((int)x, (int)y, (int)w, (int)h);
                        // 创建连续的Mat对象（Clone确保内存连续）
                        Mat roiMat = new Mat(image, roi).Clone();

                        // 使用识别模型进行推理
                        var roiList = new List<Mat> { roiMat };
                        var recognizeResult = recognizeModel.InferBatch(roiList);

                        // 如果识别模型有检测结果，更新检测模型的分类名称
                        if (recognizeResult.SampleResults.Count > 0 &&
                            recognizeResult.SampleResults[0].Results.Count > 0)
                        {
                            // 获取识别模型的第一个检测结果
                            var topResult = recognizeResult.SampleResults[0].Results[0];

                            // 更新原始检测结果的分类名称
                            var updatedDetection = new CSharpObjectResult(
                                detection.CategoryId,
                                topResult.CategoryName, // 使用识别模型的分类名称
                                detection.Score,
                                detection.Area,
                                detection.Bbox,
                                detection.WithMask,
                                detection.Mask
                            );

                            // 替换原始检测结果
                            sampleResult.Results[i] = updatedDetection;
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR推理失败：{ex.Message}");
                throw;
            }
        }

        // 新增方法：获取 GPU 信息
        public static JObject GetGpuInfo()
        {
            var devices = new List<Dictionary<string, object>>();

            int result = nvmlInit();
            if (result != 0)
            {
                return JObject.FromObject(new
                {
                    code = 1,
                    message = "Failed to initialize NVML."
                });
            }

            int deviceCount = 0;
            result = nvmlDeviceGetCount(ref deviceCount);
            if (result != 0)
            {
                nvmlShutdown();
                return JObject.FromObject(new
                {
                    code = 2,
                    message = "Failed to get device count."
                });
            }

            for (uint i = 0; i < deviceCount; i++)
            {
                IntPtr device;
                result = nvmlDeviceGetHandleByIndex(i, out device);
                if (result != 0)
                {
                    continue; // Skip this device if we fail to get its handle
                }

                uint length = 64; // Allocate enough space for the name
                char[] name = new char[length];
                result = nvmlDeviceGetName(device, name, ref length);
                if (result == 0)
                {
                    string gpuName = new string(name, 0, (int)length);
                    devices.Add(new Dictionary<string, object>
                {
                    { "device_id", i },
                    { "device_name", gpuName }
                });
                }
            }

            nvmlShutdown();

            return JObject.FromObject(new
            {
                code = 0,
                message = "Success",
                devices
            });
        }

        // NVML API 的 P/Invoke 声明
        [DllImport("nvml.dll")]
        public static extern int nvmlInit();

        [DllImport("nvml.dll")]
        public static extern int nvmlShutdown();

        [DllImport("nvml.dll")]
        public static extern int nvmlDeviceGetCount(ref int deviceCount);

        [DllImport("nvml.dll")]
        public static extern int nvmlDeviceGetName(IntPtr device, [Out] char[] name, ref uint length);

        [DllImport("nvml.dll")]
        public static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);
    }
}
