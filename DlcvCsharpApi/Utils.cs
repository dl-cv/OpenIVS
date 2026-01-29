using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using DlcvModules;

namespace dlcv_infer_csharp
{
    public partial class Utils
    {
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

        public static JArray ConvertToVisualizeFormat(CSharpResult result)
        {
            var array = new JArray();
            if(result.SampleResults == null)return array;

            for(int i = 0; i < result.SampleResults.Count; i++)
            {
                var sample = result.SampleResults[i];
                var sampleResults = new JArray();

                if(sample.Results != null)
                {
                    foreach(var obj in sample.Results)
                    {
                        var item = new JObject
                        {
                            ["category_id"] = obj.CategoryId,
                            ["category_name"] = obj.CategoryName,
                            ["score"] = obj.Score,
                            ["bbox"] = obj.Bbox != null ? JArray.FromObject(obj.Bbox) : null,
                            ["with_angle"] = obj.WithAngle,
                            ["angle"] = obj.Angle,
                            ["with_mask"] = obj.WithMask
                        };
                        // 将 mask 以 RLE 的形式存储到 JSON（mask_rle）
                        if (obj.WithMask && obj.Mask != null && !obj.Mask.Empty())
                        {
                            item["mask_rle"] = MaskRleUtils.MatToMaskInfo(obj.Mask);
                        }
                        sampleResults.Add(item);
                    }
                }

                var entry = new JObject
                {
                    ["index"] = i,
                    ["sample_results"] = sampleResults
                };
                array.Add(entry);

            }
            
            return array;
        }

        /// <summary>
        /// 将推理结果绘制到原图上
        /// </summary>
        /// <param name="images">输入图像列表</param>
        /// <param name="result">推理结果</param>
        /// <param name="properties">可视化配置（可选）</param>
        /// <returns>绘制后的图像列表</returns>
        public static List<Mat> VisualizeResults(
            List<Mat> images,
            CSharpResult result,
            Dictionary<string, object> properties = null)
        {
            var jArray = ConvertToVisualizeFormat(result);
            var moduleImages = new List<ModuleImage>();
            for (int i = 0; i < images.Count; i++)
            {
                var img = images[i];
                var state = new TransformationState(img.Width, img.Height);
                moduleImages.Add(new ModuleImage(img, img, state, i));
            }
            var visualizer = new VisualizeOnOriginal(0, null, properties);
            var output = visualizer.Process(moduleImages, jArray);
            return output.ImageList.Select(m => m.ImageObject).ToList();
        }

        public static void FreeAllModels()
        {
            DllLoader.Instance.dlcv_free_all_models();
        }

        public static JObject GetDeviceInfo()
        {
            var loader = DllLoader.Instance;
            IntPtr resultPtr = IntPtr.Zero;
            if (loader.dlcv_get_gpu_info != null)
            {
                resultPtr = loader.dlcv_get_gpu_info();
            }
            else if (loader.dlcv_get_device_info != null)
            {
                resultPtr = loader.dlcv_get_device_info();
            }
            else
            {
                return JObject.FromObject(new { code = -1, message = "dlcv_get_gpu_info 与 dlcv_get_device_info 均不可用" });
            }

            var resultJson = Marshal.PtrToStringAnsi(resultPtr);
            var resultObject = JObject.Parse(resultJson);
            loader.dlcv_free_result(resultPtr);
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
            try
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
            catch (DllNotFoundException)
            {
                // nvml.dll 未安装
                return JObject.FromObject(new
                {
                    code = -1,
                    message = "NVML library (nvml.dll) not found. NVIDIA driver may not be installed.",
                    devices = new List<Dictionary<string, object>>()
                });
            }
            catch (Exception ex)
            {
                // 其他异常
                return JObject.FromObject(new
                {
                    code = -2,
                    message = $"Error getting GPU info: {ex.Message}",
                    devices = new List<Dictionary<string, object>>()
                });
            }
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

