using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HalconDotNet;
using OpenCvSharp;
using dlcv_infer_csharp;

namespace HalconToMatDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // 定义图片和模型路径
                string imagePath = @"C:\Users\che\Desktop\minghong\标注图像\1-2025-03-04_15-19-20-jpeg.jpg";
                string modelPath = @"C:\Users\che\Desktop\minghong\mr崩边_20250319_101114.dvt";

                Console.WriteLine("开始加载模型...");
                // 加载模型 (假设使用GPU 0)
                Model model = new Model(modelPath, 0);
                Console.WriteLine("模型加载成功！");

                // 1. 使用Halcon读取图片
                Console.WriteLine("使用Halcon读取图片...");
                HObject halconImage = new HObject();
                HTuple width = new HTuple(), height = new HTuple();
                HOperatorSet.ReadImage(out halconImage, imagePath);
                HOperatorSet.GetImageSize(halconImage, out width, out height);
                Console.WriteLine($"Halcon图片尺寸: {width.I} x {height.I}");

                // 2. 使用OpenCV读取同一张图片并转成RGB格式
                Console.WriteLine("使用OpenCV读取图片...");
                Mat opencvImage = Cv2.ImRead(imagePath);
                // 将BGR转为RGB
                Cv2.CvtColor(opencvImage, opencvImage, ColorConversionCodes.BGR2RGB);
                Console.WriteLine($"OpenCV图片尺寸: {opencvImage.Width} x {opencvImage.Height}");

                // 3. 把Halcon图像转化为Mat格式
                Console.WriteLine("将Halcon图像转换为Mat格式...");
                Mat halconAsMat = HalconImageToMat(halconImage);
                Console.WriteLine($"转换后的Mat尺寸: {halconAsMat.Width} x {halconAsMat.Height}");

                // 4. 两个图分别调用模型推理
                Console.WriteLine("\n开始使用OpenCV Mat进行推理...");
                Utils.CSharpResult opencvResult = model.Infer(opencvImage);
                
                Console.WriteLine("\n开始使用Halcon转换后的Mat进行推理...");
                Utils.CSharpResult halconResult = model.Infer(halconAsMat);

                // 5. 分别打印出两个模型的推理结果
                Console.WriteLine("\n------------ OpenCV Mat 推理结果 ------------");
                PrintResults(opencvResult);

                Console.WriteLine("\n------------ Halcon转换Mat 推理结果 ------------");
                PrintResults(halconResult);

                // 释放资源
                halconImage.Dispose();
                opencvImage.Dispose();
                halconAsMat.Dispose();
                Utils.FreeAllModels();
                
                Console.WriteLine("\n按任意键退出...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ReadKey();
            }
        }

        // Halcon图像转换为OpenCV Mat
        static Mat HalconImageToMat(HObject halconImage)
        {
            // 获取Halcon图像的属性
            HTuple width = new HTuple(), height = new HTuple(), channels = new HTuple();
            HTuple type = new HTuple(), imagePointer = new HTuple();

            HOperatorSet.GetImageSize(halconImage, out width, out height);
            HOperatorSet.CountChannels(halconImage, out channels);
            HOperatorSet.GetImagePointer1(halconImage, out imagePointer, out type, out width, out height);
            
            IntPtr ptr = new IntPtr(imagePointer.L);
            MatType matType;

            if (channels.I == 1)
            {
                // 单通道灰度图
                matType = MatType.CV_8UC1;
            }
            else if (channels.I == 3)
            {
                // 三通道彩色图
                HTuple pointerR = new HTuple(), pointerG = new HTuple(), pointerB = new HTuple();
                HOperatorSet.GetImagePointer3(halconImage, out pointerR, out pointerG, out pointerB, out type, out width, out height);
                
                // 创建一个空的三通道Mat
                Mat matRGB = new Mat(height.I, width.I, MatType.CV_8UC3);
                
                // 将三个通道的数据复制到Mat中
                unsafe
                {
                    byte* ptrR = (byte*)(pointerR.L);
                    byte* ptrG = (byte*)(pointerG.L);
                    byte* ptrB = (byte*)(pointerB.L);
                    
                    byte* matData = (byte*)matRGB.Data.ToPointer();
                    
                    for (int y = 0; y < height.I; y++)
                    {
                        for (int x = 0; x < width.I; x++)
                        {
                            int halconIndex = y * width.I + x;
                            int matIndex = (y * width.I + x) * 3;
                            
                            // 注意OpenCV是BGR顺序
                            matData[matIndex] = ptrR[halconIndex];     // 红色通道
                            matData[matIndex + 1] = ptrG[halconIndex]; // 绿色通道
                            matData[matIndex + 2] = ptrB[halconIndex]; // 蓝色通道
                        }
                    }
                }
                
                return matRGB;
            }
            else
            {
                throw new Exception($"不支持的通道数: {channels.I}");
            }

            // 创建Mat封装Halcon内存，注意这里不复制内存
            if (channels.I == 1)
            {
                // 单通道灰度图直接包装
                return Mat.FromPixelData(height.I, width.I, matType, ptr);
            }
            else
            {
                throw new Exception("未处理的图像类型");
            }
        }

        // 打印推理结果
        static void PrintResults(Utils.CSharpResult result)
        {
            if (result.SampleResults.Count == 0)
            {
                Console.WriteLine("没有结果");
                return;
            }

            var sampleResult = result.SampleResults[0]; // 假设只有一个样本结果
            Console.WriteLine($"检测到 {sampleResult.Results.Count} 个目标");
            
            foreach (var obj in sampleResult.Results)
            {
                Console.WriteLine($"类别: {obj.CategoryName} (ID: {obj.CategoryId})");
                Console.WriteLine($"置信度: {obj.Score:F4}");
                Console.WriteLine($"区域面积: {obj.Area:F2}");
                Console.WriteLine($"边界框: [{string.Join(", ", obj.Bbox.Select(v => v.ToString("F2")))}]");
                if (obj.WithMask)
                {
                    Console.WriteLine($"包含掩码: 尺寸 {obj.Mask.Width}x{obj.Mask.Height}");
                }
                Console.WriteLine("---");
            }
        }
    }
}
