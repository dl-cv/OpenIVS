using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;
using dlcv_infer_csharp;
using System.Diagnostics;
using System.Runtime.InteropServices;

#if HALCON_DOTNET
using HalconDotNet;
#endif

namespace HalconToMatDemo
{
    internal static class Program
    {
#if HALCON_DOTNET
        [DllImport("kernel32.dll")]
        public extern static long CopyMemory(IntPtr dest, IntPtr source, int size);


        static void Main(string[] args)
        {
            try
            {
                // 定义图片和模型路径
                string imagePath = @"C:\Users\Administrator\Desktop\测试模型\Scr_0049.png";
                string modelPath = @"C:\Users\Administrator\Desktop\测试模型\手机屏幕缺陷检测_20241010_173458.dvt";

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
                Console.WriteLine($"OpenCV图片尺寸: {opencvImage.Width} x {opencvImage.Height} x {opencvImage.Channels()}");

                // 3. 把Halcon图像转化为Mat格式
                Mat halconAsMat = new Mat();
                for (int i = 0; i < 3; i++)
                {
                    Console.WriteLine("将Halcon图像转换为Mat格式...");
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    halconAsMat = HalconImageToMat(halconImage);
                    stopwatch.Stop();
                    // 高精度计时器
                    double delay_ms = stopwatch.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
                    Console.WriteLine($"转换耗时: {delay_ms:F2} ms");
                }

                //Cv2.ImWrite("test.jpg", halconAsMat);
                //Mat diff = new Mat();
                //Cv2.Absdiff(halconAsMat, opencvImage, diff);
                //Cv2.ImWrite("diff.jpg", diff);

                Console.WriteLine($"转换后的Mat尺寸: {halconAsMat.Width} x {halconAsMat.Height} x {halconAsMat.Channels()}");

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

            Console.WriteLine($"Halcon图像通道数: {channels.I}");

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
                //Mat matRGB = new Mat(height.I, width.I, MatType.CV_8UC3);
                int imageSize = width.I * height.I;
                unsafe
                {
                    CopyMemory(matR.Data, new IntPtr((byte*)pointerR.IP), imageSize);
                    CopyMemory(matG.Data, new IntPtr((byte*)pointerG.IP), imageSize);
                    CopyMemory(matB.Data, new IntPtr((byte*)pointerB.IP), imageSize);
                }
                Mat matRGB = new Mat();
                Cv2.Merge(new Mat[] { matR, matG, matB }, matRGB);

                return matRGB;
            }
            else
            {
                throw new Exception($"不支持的通道数: {channels.I}");
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
#else
        // 该 Demo 依赖商业库 HalconDotNet。为保证开源仓库在未安装 Halcon 的机器上也能编译，
        // 默认不启用 HALCON_DOTNET 编译常量，仅给出运行时提示。
        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("HalconToMatDemo 未启用（缺少 HALCON_DOTNET）。");
            Console.WriteLine("如需运行该 Demo：");
            Console.WriteLine("- 安装 Halcon，并确保工程引用到 HalconDotNet.dll（或 halcondotnet）。");
            Console.WriteLine("- 在 HalconToMatDemo 项目里定义编译常量：HALCON_DOTNET");
        }
#endif
    }
}
