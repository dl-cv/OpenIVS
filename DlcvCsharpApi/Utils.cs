using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
            return SerializeJson(json);
        }

        public static String jsonToString(JArray json)
        {
            return SerializeJson(json);
        }

        public static bool TryGetExtraInfoPoint2dList(JObject extraInfo, string key, out List<Point2d> values)
        {
            values = new List<Point2d>();
            if (!TryGetExtraInfoToken(extraInfo, key, out JToken token))
            {
                return false;
            }
            return TryParsePoint2dToken(token, out values);
        }

        public static List<Point2d> GetExtraInfoPolyline(JObject extraInfo, string key = "polyline")
        {
            if (TryGetExtraInfoPoint2dList(extraInfo, key, out List<Point2d> points))
            {
                return points;
            }
            return new List<Point2d>();
        }

        public static void SetExtraInfoPolyline(JObject extraInfo, List<Point2d> polyline, string key = "polyline")
        {
            if (extraInfo == null) return;

            if (polyline == null || polyline.Count == 0)
            {
                extraInfo.Remove(key);
                return;
            }

            var line = new JArray();
            for (int i = 0; i < polyline.Count; i++)
            {
                var p = polyline[i];
                line.Add(new JArray(p.X, p.Y));
            }
            extraInfo[key] = line;
        }

        public static string FormatExtraInfoForDisplay(JObject extraInfo, int maxCollectionItems = 12)
        {
            if (extraInfo == null || !extraInfo.HasValues)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var property in extraInfo.Properties())
            {
                string formatted = FormatExtraInfoToken(property.Value, maxCollectionItems);
                parts.Add($"{property.Name}: {formatted}");
            }

            return string.Join(", ", parts);
        }

        private static bool TryGetExtraInfoToken(JObject extraInfo, string key, out JToken token)
        {
            token = null;
            if (extraInfo == null || string.IsNullOrWhiteSpace(key) || !extraInfo.ContainsKey(key))
            {
                return false;
            }
            token = extraInfo[key];
            return token != null && token.Type != JTokenType.Null;
        }

        private static bool TryConvertTokenToDouble(JToken token, out double value)
        {
            value = 0.0;
            if (token == null) return false;
            try
            {
                if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                {
                    value = token.Value<double>();
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static bool TryParsePoint2dToken(JToken token, out List<Point2d> points)
        {
            points = new List<Point2d>();
            if (!(token is JArray arr))
            {
                return false;
            }
            return TryParsePoint2dArray(arr, out points);
        }

        private static bool TryParsePoint2dArray(JArray arr, out List<Point2d> points)
        {
            points = new List<Point2d>();
            if (arr == null || arr.Count == 0) return false;

            for (int i = 0; i < arr.Count; i++)
            {
                var item = arr[i];
                if (item is JArray pa && pa.Count >= 2)
                {
                    if (!TryConvertTokenToDouble(pa[0], out double x) || !TryConvertTokenToDouble(pa[1], out double y))
                    {
                        points.Clear();
                        return false;
                    }
                    points.Add(new Point2d(x, y));
                    continue;
                }

                if (item is JObject po && po.ContainsKey("x") && po.ContainsKey("y"))
                {
                    if (!TryConvertTokenToDouble(po["x"], out double x) || !TryConvertTokenToDouble(po["y"], out double y))
                    {
                        points.Clear();
                        return false;
                    }
                    points.Add(new Point2d(x, y));
                    continue;
                }

                points.Clear();
                return false;
            }

            return points.Count > 0;
        }

        private static string FormatExtraInfoToken(JToken token, int maxCollectionItems)
        {
            if (token == null || token.Type == JTokenType.Null) return "null";

            switch (token.Type)
            {
                case JTokenType.Integer:
                    return token.Value<long>().ToString(CultureInfo.InvariantCulture);
                case JTokenType.Float:
                    return FormatDouble(token.Value<double>());
                case JTokenType.String:
                    return token.Value<string>();
                case JTokenType.Boolean:
                    return token.Value<bool>() ? "true" : "false";
            }

            if (token is JArray arr)
            {
                if (TryParsePoint2dArray(arr, out List<Point2d> points))
                {
                    return FormatPointList(points, maxCollectionItems);
                }
                if (TryFormatScalarList(arr, maxCollectionItems, out string scalarListText))
                {
                    return scalarListText;
                }
                return arr.ToString(Formatting.None);
            }

            if (token is JObject obj)
            {
                string nested = FormatExtraInfoForDisplay(obj, maxCollectionItems);
                return "{" + nested + "}";
            }

            return token.ToString(Formatting.None);
        }

        private static bool TryFormatScalarList(JArray arr, int maxCollectionItems, out string formatted)
        {
            formatted = null;
            if (arr == null)
            {
                return false;
            }

            var textItems = new List<string>();
            bool allString = true;
            bool allNumber = true;

            int limit = Math.Min(arr.Count, Math.Max(1, maxCollectionItems));
            for (int i = 0; i < limit; i++)
            {
                var token = arr[i];
                if (token == null)
                {
                    allString = false;
                    allNumber = false;
                    break;
                }

                if (token.Type == JTokenType.String)
                {
                    textItems.Add(token.Value<string>());
                    allNumber = false;
                }
                else if (token.Type == JTokenType.Integer)
                {
                    textItems.Add(token.Value<long>().ToString(CultureInfo.InvariantCulture));
                    allString = false;
                }
                else if (token.Type == JTokenType.Float)
                {
                    double number = token.Value<double>();
                    textItems.Add(FormatDouble(number));
                    allString = false;
                }
                else
                {
                    allString = false;
                    allNumber = false;
                    break;
                }
            }

            if (!allString && !allNumber)
            {
                return false;
            }

            if (arr.Count > limit)
            {
                textItems.Add("...");
            }

            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < textItems.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                if (allString)
                {
                    sb.Append("\"");
                    sb.Append(textItems[i]);
                    sb.Append("\"");
                }
                else
                {
                    sb.Append(textItems[i]);
                }
            }
            sb.Append("]");

            formatted = sb.ToString();
            return true;
        }

        private static string FormatPointList(List<Point2d> points, int maxCollectionItems)
        {
            int limit = Math.Min(points.Count, Math.Max(1, maxCollectionItems));
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < limit; i++)
            {
                if (i > 0) sb.Append(", ");
                var p = points[i];
                sb.Append("[");
                sb.Append(FormatDouble(p.X));
                sb.Append(", ");
                sb.Append(FormatDouble(p.Y));
                sb.Append("]");
            }
            if (points.Count > limit)
            {
                if (limit > 0) sb.Append(", ");
                sb.Append("...");
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string FormatDouble(double value)
        {
            if (Math.Abs(value - Math.Round(value)) < 1e-9)
            {
                return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            }
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string SerializeJson(JToken token)
        {
            var settings = new JsonSerializerSettings
            {
                StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
            };
            return JsonConvert.SerializeObject(token, Formatting.Indented, settings);
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
                            ["with_mask"] = obj.WithMask,
                            ["extra_info"] = obj.ExtraInfo ?? new JObject()
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
            DllLoader.Instance.dlcv_free_all_models?.Invoke();
        }

        public static JObject GetDeviceInfo()
        {
            var loader = DllLoader.Instance;
            IntPtr resultPtr = IntPtr.Zero;
            if (loader.dlcv_get_device_info != null)
            {
                resultPtr = loader.dlcv_get_device_info();
            }
            else if (loader.dlcv_get_gpu_info != null)
            {
                resultPtr = loader.dlcv_get_gpu_info();
            }
            else
            {
                return JObject.FromObject(new { code = -1, message = "dlcv_get_device_info 与 dlcv_get_gpu_info 均不可用" });
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
                                detection.Mask,
                                detection.WithBbox,
                                detection.WithAngle,
                                detection.Angle,
                                detection.ExtraInfo
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

