using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HalconDotNet;
using dlcv_infer_csharp;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace HalconDLL
{
    public class HalconInference
    {
        // 模型对象
        private Model model = null;
        // 模型路径
        private string modelPath = string.Empty;
        // GPU设备ID
        private int deviceId = 0;

        /// <summary>
        /// 加载深度学习模型
        /// </summary>
        /// <param name="modelPath">模型路径</param>
        /// <param name="deviceId">GPU设备ID，默认为0</param>
        /// <returns>是否成功</returns>
        public bool LoadModel(string modelPath, int deviceId = 0)
        {
            try
            {
                this.modelPath = modelPath;
                this.deviceId = deviceId;
                model = new Model(modelPath, deviceId);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载模型失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取模型信息
        /// </summary>
        /// <returns>模型信息字符串</returns>
        public string GetModelInfo()
        {
            if (model == null)
            {
                return "模型未加载";
            }

            try
            {
                JObject modelInfo = model.GetModelInfo();
                return modelInfo.ToString();
            }
            catch (Exception ex)
            {
                return $"获取模型信息失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 使用Halcon图像进行推理
        /// </summary>
        /// <param name="image">Halcon图像对象</param>
        /// <returns>推理结果</returns>
        public InferenceResult InferWithHalconImage(HObject image)
        {
            if (model == null)
            {
                return new InferenceResult { Success = false, ErrorMessage = "模型未加载" };
            }

            try
            {
                // 将Halcon图像转换为OpenCV Mat
                Mat opencvImage = HalconImageToMat(image);
                
                // 执行推理
                Utils.CSharpResult result = model.Infer(opencvImage);
                
                // 释放Mat资源
                opencvImage.Dispose();
                
                // 创建返回结果
                return CreateInferenceResult(result);
            }
            catch (Exception ex)
            {
                return new InferenceResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 释放模型资源
        /// </summary>
        public void FreeModel()
        {
            Utils.FreeAllModels();
            model = null;
        }

        /// <summary>
        /// 在Halcon环境中可视化结果
        /// </summary>
        /// <param name="window">Halcon窗口句柄</param>
        /// <param name="image">原始图像</param>
        /// <param name="result">推理结果</param>
        public void VisualizeResultsOnHalcon(HTuple window, HObject image, InferenceResult result)
        {
            if (!result.Success || result.Objects.Count == 0)
            {
                return;
            }

            try
            {
                // 获取图像尺寸
                HTuple width = new HTuple(), height = new HTuple();
                HOperatorSet.GetImageSize(image, out width, out height);

                // 设置显示属性
                HOperatorSet.SetColor(window, "green");
                HOperatorSet.SetLineWidth(window, 2);
                HOperatorSet.SetDraw(window, "margin");
                
                foreach (var obj in result.Objects)
                {
                    // 显示边界框
                    HObject rectangle;
                    double x1 = obj.Bbox[0];
                    double y1 = obj.Bbox[1];
                    double x2 = obj.Bbox[2];
                    double y2 = obj.Bbox[3];
                    HOperatorSet.GenRectangle1(out rectangle, y1, x1, y2, x2);
                    HOperatorSet.DispObj(rectangle, window);
                    rectangle.Dispose();

                    // 显示类别和置信度
                    string text = $"{obj.CategoryName}: {obj.Score:F2}";
                    HOperatorSet.SetTposition(window, y1 - 15, x1);
                    HOperatorSet.WriteString(window, text);

                    // 如果有掩码，显示掩码
                    if (obj.WithMask && obj.MaskData != null)
                    {
                        try
                        {
                            HObject mask;
                            // 从掩码数据创建Halcon区域
                            int maskWidth = obj.MaskWidth;
                            int maskHeight = obj.MaskHeight;
                            HOperatorSet.GenImageConst(out mask, "byte", maskWidth, maskHeight);
                            
                            // 将掩码数据设置到Halcon图像
                            HTuple ptr = new HTuple();
                            HTuple type = new HTuple();
                            HTuple maskWidth_h = new HTuple();
                            HTuple maskHeight_h = new HTuple();
                            HOperatorSet.GetImagePointer1(mask, out ptr, out type, out maskWidth_h, out maskHeight_h);
                            
                            // 使用Marshal.Copy将掩码数据复制到Halcon图像
                            IntPtr ptrIntPtr = (IntPtr)ptr.IP;
                            Marshal.Copy(obj.MaskData, 0, ptrIntPtr, obj.MaskData.Length);
                            
                            // 将掩码转为区域
                            HObject region;
                            HOperatorSet.Threshold(mask, out region, 1, 255);
                            
                            // 设置区域显示属性
                            HOperatorSet.SetColor(window, "red");
                            HOperatorSet.SetDraw(window, "fill");
                            HOperatorSet.DispObj(region, window);
                            
                            mask.Dispose();
                            region.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"掩码显示失败: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"可视化结果失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 将Halcon图像转换为OpenCV Mat
        /// </summary>
        private Mat HalconImageToMat(HObject halconImage)
        {
            // 获取Halcon图像的属性
            HTuple width = new HTuple(), height = new HTuple(), channels = new HTuple();
            HTuple type = new HTuple(), imagePointer = new HTuple();

            HOperatorSet.GetImageSize(halconImage, out width, out height);
            HOperatorSet.CountChannels(halconImage, out channels);

            if (channels.I == 1)
            {
                // 单通道灰度图
                HOperatorSet.GetImagePointer1(halconImage, out imagePointer, out type, out width, out height);
                Mat matGray = new Mat(height.I, width.I, MatType.CV_8UC1);
                int imageSize = width.I * height.I;
                unsafe
                {
                    CopyMemory(matGray.Data, new IntPtr((byte*)imagePointer.IP), imageSize);
                }

                return matGray;
            }
            else if (channels.I == 3)
            {
                // 三通道彩色图
                HTuple pointerR = new HTuple(), pointerG = new HTuple(), pointerB = new HTuple();
                HOperatorSet.GetImagePointer3(halconImage, out pointerR, out pointerG, out pointerB, out type, out width, out height);

                Mat matR = new Mat(height.I, width.I, MatType.CV_8UC1);
                Mat matG = new Mat(height.I, width.I, MatType.CV_8UC1);
                Mat matB = new Mat(height.I, width.I, MatType.CV_8UC1);
                int imageSize = width.I * height.I;
                unsafe
                {
                    CopyMemory(matR.Data, new IntPtr((byte*)pointerR.IP), imageSize);
                    CopyMemory(matG.Data, new IntPtr((byte*)pointerG.IP), imageSize);
                    CopyMemory(matB.Data, new IntPtr((byte*)pointerB.IP), imageSize);
                }
                Mat matRGB = new Mat();
                Cv2.Merge(new Mat[] { matR, matG, matB }, matRGB);

                // 释放中间Mat
                matR.Dispose();
                matG.Dispose();
                matB.Dispose();

                return matRGB;
            }
            else
            {
                throw new Exception($"不支持的通道数: {channels.I}");
            }
        }

        /// <summary>
        /// 从OpenCV推理结果创建自定义结果对象
        /// </summary>
        private InferenceResult CreateInferenceResult(Utils.CSharpResult result)
        {
            var inferenceResult = new InferenceResult
            {
                Success = true,
                Objects = new List<DetectedObject>()
            };

            if (result.SampleResults.Count == 0)
            {
                return inferenceResult;
            }

            // 处理第一个样本的结果
            var sampleResult = result.SampleResults[0];
            foreach (var obj in sampleResult.Results)
            {
                byte[] maskData = null;
                if (obj.WithMask && !obj.Mask.Empty())
                {
                    // 复制掩码数据
                    int maskSize = obj.Mask.Width * obj.Mask.Height;
                    maskData = new byte[maskSize];
                    Marshal.Copy(obj.Mask.Data, maskData, 0, maskSize);
                }

                var detectedObj = new DetectedObject
                {
                    CategoryId = obj.CategoryId,
                    CategoryName = obj.CategoryName,
                    Score = obj.Score,
                    Area = obj.Area,
                    Bbox = obj.Bbox,
                    WithMask = obj.WithMask,
                    MaskData = maskData,
                    MaskWidth = obj.WithMask ? obj.Mask.Width : 0,
                    MaskHeight = obj.WithMask ? obj.Mask.Height : 0
                };

                inferenceResult.Objects.Add(detectedObj);
            }

            return inferenceResult;
        }

        [DllImport("kernel32.dll")]
        private static extern void CopyMemory(IntPtr dest, IntPtr source, int size);
    }

    /// <summary>
    /// 推理结果类
    /// </summary>
    public class InferenceResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<DetectedObject> Objects { get; set; } = new List<DetectedObject>();
    }

    /// <summary>
    /// 检测到的对象类
    /// </summary>
    public class DetectedObject
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }
        public float Score { get; set; }
        public float Area { get; set; }
        public List<double> Bbox { get; set; }
        public bool WithMask { get; set; }
        public byte[] MaskData { get; set; }
        public int MaskWidth { get; set; }
        public int MaskHeight { get; set; }
    }
}
