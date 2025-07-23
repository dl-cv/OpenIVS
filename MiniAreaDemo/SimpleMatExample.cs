using System;
using System.Collections.Generic;
using OpenCvSharp;
using MiniAreaDemo;

namespace SimpleMatExample
{
    /// <summary>
    /// 直接使用Mat对象的示例
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // 设置模型路径
                string modelPath = @"C:\Users\Administrator\Desktop\标签识别\005.dvt";
                string outputDir = @"C:\Users\Administrator\Desktop\标签识别\处理结果";

                Console.WriteLine("直接使用Mat对象的标签纸处理示例");
                Console.WriteLine(new string('=', 50));

                // 创建输出目录
                System.IO.Directory.CreateDirectory(outputDir);

                // 创建处理器实例
                using (ILabelProcessor processor = new LabelProcessor())
                {
                    // 示例1：从文件读取图像
                    string imagePath = @"C:\Users\Administrator\Desktop\标签识别\5-测试图\test_image.png";
                    
                    using (Mat image = Cv2.ImRead(imagePath))
                    {
                        if (!image.Empty())
                        {
                            Console.WriteLine($"处理图像: {imagePath}");
                            Console.WriteLine($"图像尺寸: {image.Width}x{image.Height}");

                            // 直接传入Mat对象进行处理
                            using (var results = processor.ProcessLabels(image, modelPath))
                            {
                                Console.WriteLine($"检测到 {results.Count} 个标签纸");

                                for (int i = 0; i < results.Count; i++)
                                {
                                    var result = results[i];
                                    Console.WriteLine($"\n标签纸 {i}:");
                                    Console.WriteLine($"  角度: {result.Angle * 180 / Math.PI:F2}度");
                                    Console.WriteLine($"  处理后尺寸: {result.ProcessedImage.Width}x{result.ProcessedImage.Height}");

                                    // 保存结果
                                    string outputPath = System.IO.Path.Combine(outputDir, $"result_{i}.png");
                                    Cv2.ImWrite(outputPath, result.ProcessedImage);
                                    Console.WriteLine($"  已保存: {outputPath}");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"无法加载图像: {imagePath}");
                        }
                    }

                    // 示例2：创建测试图像
                    using (Mat testImage = new Mat(800, 1200, MatType.CV_8UC3, Scalar.All(255)))
                    {
                        Console.WriteLine($"\n处理测试图像");
                        Console.WriteLine($"图像尺寸: {testImage.Width}x{testImage.Height}");

                        // 在图像上绘制一些测试内容
                        Cv2.Rectangle(testImage, new Point(100, 100), new Point(700, 600), new Scalar(0, 0, 255), 2);
                        Cv2.PutText(testImage, "Test Label", new Point(200, 300), HersheyFonts.HersheySimplex, 2, new Scalar(255, 0, 0), 3);

                        // 直接传入Mat对象进行处理
                        using (var results = processor.ProcessLabels(testImage, modelPath))
                        {
                            Console.WriteLine($"检测到 {results.Count} 个标签纸");

                            for (int i = 0; i < results.Count; i++)
                            {
                                var result = results[i];
                                Console.WriteLine($"\n标签纸 {i}:");
                                Console.WriteLine($"  角度: {result.Angle * 180 / Math.PI:F2}度");
                                Console.WriteLine($"  处理后尺寸: {result.ProcessedImage.Width}x{result.ProcessedImage.Height}");

                                // 保存结果
                                string outputPath = System.IO.Path.Combine(outputDir, $"test_result_{i}.png");
                                Cv2.ImWrite(outputPath, result.ProcessedImage);
                                Console.WriteLine($"  已保存: {outputPath}");
                            }
                        }
                    }
                }

                Console.WriteLine("\n处理完成!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine($"堆栈: {ex.StackTrace}");
            }

            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }
} 