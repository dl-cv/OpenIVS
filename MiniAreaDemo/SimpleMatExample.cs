using System;
using System.Collections.Generic;
using OpenCvSharp;
using MiniAreaDemo;

namespace SimpleMatExample
{
    /// <summary>
    /// ֱ��ʹ��Mat�����ʾ��
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // ����ģ��·��
                string modelPath = @"C:\Users\Administrator\Desktop\��ǩʶ��\005.dvt";
                string outputDir = @"C:\Users\Administrator\Desktop\��ǩʶ��\������";

                Console.WriteLine("ֱ��ʹ��Mat����ı�ǩֽ����ʾ��");
                Console.WriteLine(new string('=', 50));

                // �������Ŀ¼
                System.IO.Directory.CreateDirectory(outputDir);

                // ����������ʵ��
                using (ILabelProcessor processor = new LabelProcessor())
                {
                    // ʾ��1�����ļ���ȡͼ��
                    string imagePath = @"C:\Users\Administrator\Desktop\��ǩʶ��\5-����ͼ\test_image.png";
                    
                    using (Mat image = Cv2.ImRead(imagePath))
                    {
                        if (!image.Empty())
                        {
                            Console.WriteLine($"����ͼ��: {imagePath}");
                            Console.WriteLine($"ͼ��ߴ�: {image.Width}x{image.Height}");

                            // ֱ�Ӵ���Mat������д���
                            using (var results = processor.ProcessLabels(image, modelPath))
                            {
                                Console.WriteLine($"��⵽ {results.Count} ����ǩֽ");

                                for (int i = 0; i < results.Count; i++)
                                {
                                    var result = results[i];
                                    Console.WriteLine($"\n��ǩֽ {i}:");
                                    Console.WriteLine($"  �Ƕ�: {result.Angle * 180 / Math.PI:F2}��");
                                    Console.WriteLine($"  �����ߴ�: {result.ProcessedImage.Width}x{result.ProcessedImage.Height}");

                                    // ������
                                    string outputPath = System.IO.Path.Combine(outputDir, $"result_{i}.png");
                                    Cv2.ImWrite(outputPath, result.ProcessedImage);
                                    Console.WriteLine($"  �ѱ���: {outputPath}");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"�޷�����ͼ��: {imagePath}");
                        }
                    }

                    // ʾ��2����������ͼ��
                    using (Mat testImage = new Mat(800, 1200, MatType.CV_8UC3, Scalar.All(255)))
                    {
                        Console.WriteLine($"\n�������ͼ��");
                        Console.WriteLine($"ͼ��ߴ�: {testImage.Width}x{testImage.Height}");

                        // ��ͼ���ϻ���һЩ��������
                        Cv2.Rectangle(testImage, new Point(100, 100), new Point(700, 600), new Scalar(0, 0, 255), 2);
                        Cv2.PutText(testImage, "Test Label", new Point(200, 300), HersheyFonts.HersheySimplex, 2, new Scalar(255, 0, 0), 3);

                        // ֱ�Ӵ���Mat������д���
                        using (var results = processor.ProcessLabels(testImage, modelPath))
                        {
                            Console.WriteLine($"��⵽ {results.Count} ����ǩֽ");

                            for (int i = 0; i < results.Count; i++)
                            {
                                var result = results[i];
                                Console.WriteLine($"\n��ǩֽ {i}:");
                                Console.WriteLine($"  �Ƕ�: {result.Angle * 180 / Math.PI:F2}��");
                                Console.WriteLine($"  �����ߴ�: {result.ProcessedImage.Width}x{result.ProcessedImage.Height}");

                                // ������
                                string outputPath = System.IO.Path.Combine(outputDir, $"test_result_{i}.png");
                                Cv2.ImWrite(outputPath, result.ProcessedImage);
                                Console.WriteLine($"  �ѱ���: {outputPath}");
                            }
                        }
                    }
                }

                Console.WriteLine("\n�������!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"��������: {ex.Message}");
                Console.WriteLine($"��ջ: {ex.StackTrace}");
            }

            Console.WriteLine("��������˳�...");
            Console.ReadKey();
        }
    }
} 