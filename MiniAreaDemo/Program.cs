﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;

namespace MiniAreaDemo
{
    /// 主要功能：
    /// 1. 使用语义分割模型检测标签纸轮廓
    /// 2. 高级透视变换，专门处理透视变形
    /// 
    /// 透视变换方法：
    /// - 使用平均尺寸计算目标尺寸，更好地保持比例
    /// - 自动调整输出方向（横向/纵向）
    /// - 专门针对透视变形优化
    public class MaskInfo
    {
        public Mat Mask { get; set; }
        public int BboxX { get; set; }
        public int BboxY { get; set; }
        public int BboxW { get; set; }
        public int BboxH { get; set; }
    }
    
    /// 处理结果类，包含mask信息和透视变换后的图像
    public class ProcessingResult : IDisposable
    {
        public MaskInfo MaskInfo { get; set; }
        public Mat ProcessedImage { get; set; }
        public Point2f[] QuadPoints { get; set; }
        public double Angle { get; set; }

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                ProcessedImage?.Dispose();
                MaskInfo?.Mask?.Dispose();
                _disposed = true;
            }
        }
    }

    /// 可释放的处理结果列表
    public class DisposableProcessingResults : List<ProcessingResult>, IDisposable
    {
        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                foreach (var result in this)
                {
                    if (result != null)
                    {
                        result.ProcessedImage?.Dispose();
                        result.MaskInfo?.Mask?.Dispose();
                    }
                }
                Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 标签纸处理接口
    /// </summary>
    public interface ILabelProcessor : IDisposable
    {
        /// <summary>
        /// 处理图像中的标签纸（使用Mat对象）
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="modelPath">模型路径</param>
        /// <returns>处理结果列表</returns>
        DisposableProcessingResults ProcessLabels(Mat image, string modelPath);
    }

    /// <summary>
    /// 标签纸处理器实现
    /// </summary>
    public class LabelProcessor : ILabelProcessor, IDisposable
    {
        private Model _model = null;
        private string _currentModelPath = null;
        private bool _disposed = false;

        public LabelProcessor()
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _model?.Dispose();
                _model = null;
                _currentModelPath = null;
                _disposed = true;
            }
        }

        /// <summary>
        /// 加载模型，如果已加载相同路径的模型则复用
        /// </summary>
        private Model GetOrLoadModel(string modelPath)
        {
            // 如果模型路径相同且模型已加载，直接返回
            if (_model != null && _currentModelPath == modelPath)
            {
                Console.WriteLine("复用已加载的模型");
                return _model;
            }

            // 如果模型路径不同，先释放旧模型
            if (_model != null)
            {
                Console.WriteLine("释放旧模型，加载新模型");
                _model.Dispose();
                _model = null;
            }

            // 加载新模型
            Console.WriteLine($"加载模型: {modelPath}");
            _model = new Model(modelPath, 0);
            _currentModelPath = modelPath;
            
            return _model;
        }

        public DisposableProcessingResults ProcessLabels(Mat originalImage, string modelPath)
        {
            DisposableProcessingResults results = new DisposableProcessingResults();

            try
            {
                // 使用模型进行推理并获取mask和bbox信息
                var maskInfos = InferAndGetMasks(originalImage, modelPath);
                if (maskInfos.Count == 0)
                {
                    return results;
                }

                // 处理所有mask
                for (int i = 0; i < maskInfos.Count; i++)
                {
                    var maskInfo = maskInfos[i];
                    
                    // 将mask坐标转换到原图坐标系并获取最小外接四边形
                    Point2f[] quadPoints = GetMinAreaQuadInOriginalImage(maskInfo);

                    if (quadPoints.Length != 4)
                    {
                        // 立即释放无效的mask
                        maskInfo.Mask?.Dispose();
                        continue;
                    }

                    // 计算角度
                    double angleInRadians = GetAngleFromQuad(quadPoints);

                    // 对原图进行透视变换
                    Mat processedImage = AdvancedPerspectiveTransform(originalImage, quadPoints);

                    // 创建处理结果
                    var result = new ProcessingResult
                    {
                        MaskInfo = maskInfo,
                        ProcessedImage = processedImage,
                        QuadPoints = quadPoints,
                        Angle = angleInRadians
                    };

                    results.Add(result);
                }

                return results;
            }
            catch (Exception ex)
            {
                // 清理已创建的资源
                results.Dispose();
                throw;
            }
        }

        List<MaskInfo> InferAndGetMasks(Mat originalImage, string modelPath)
        {
            List<MaskInfo> maskInfos = new List<MaskInfo>();
            
            try
            {
                // 使用全局模型实例
                Model model = GetOrLoadModel(modelPath);
                
                // 进行推理
                var result = model.Infer(originalImage);
                
                // 从推理结果中提取mask和bbox信息
                foreach (var objectResult in result.SampleResults[0].Results)
                {
                    if (objectResult.WithBbox && objectResult.Bbox.Count >= 4)
                    {
                        int bboxX = (int)objectResult.Bbox[0];
                        int bboxY = (int)objectResult.Bbox[1];
                        int bboxW = (int)objectResult.Bbox[2];
                        int bboxH = (int)objectResult.Bbox[3];
                        
                        Mat mask;
                        
                        if (objectResult.WithMask && !objectResult.Mask.Empty())
                        {
                            // 如果有mask，使用原始mask并resize到bbox尺寸
                            mask = objectResult.Mask.Clone();
                            Cv2.Resize(mask, mask, new Size(bboxW, bboxH));
                            
                            // 确保mask是单通道8位图像
                            if (mask.Channels() != 1)
                            {
                                Cv2.CvtColor(mask, mask, ColorConversionCodes.BGR2GRAY);
                            }
                        }
                        else
                        {
                            // 如果没有mask，根据bbox尺寸创建全白mask
                            mask = new Mat(bboxH, bboxW, MatType.CV_8UC1, Scalar.All(255));
                        }
                        
                        maskInfos.Add(new MaskInfo
                        {
                            Mask = mask,
                            BboxX = bboxX,
                            BboxY = bboxY,
                            BboxW = bboxW,
                            BboxH = bboxH
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"模型推理失败: {ex.Message}", ex);
            }
            
            return maskInfos;
        }

        // 高级透视变换函数，专门处理透视变形
        static Mat AdvancedPerspectiveTransform(Mat src, Point2f[] quadPoints)
        {
            // 使用改进的四边形排序算法
            var sortedPoints = SortQuadPointsImproved(quadPoints);
            Point2f tl = sortedPoints[0];
            Point2f tr = sortedPoints[1];
            Point2f br = sortedPoints[2];
            Point2f bl = sortedPoints[3];

            // 计算四边形的实际尺寸（考虑透视变形）
            float topWidth = (float)Point2f.Distance(tl, tr);
            float bottomWidth = (float)Point2f.Distance(bl, br);
            float leftHeight = (float)Point2f.Distance(tl, bl);
            float rightHeight = (float)Point2f.Distance(tr, br);

            // 使用平均尺寸作为目标尺寸，这样可以更好地保持比例
            float targetWidth = (topWidth + bottomWidth) / 2.0f;
            float targetHeight = (leftHeight + rightHeight) / 2.0f;

            // 如果标签纸本身是横向的，确保输出也是横向
            if (targetWidth < targetHeight)
            {
                (targetWidth, targetHeight) = (targetHeight, targetWidth);
                // 重新排序点以匹配新的方向
                sortedPoints = new Point2f[] { bl, tl, tr, br };
                tl = sortedPoints[0];
                tr = sortedPoints[1];
                br = sortedPoints[2];
                bl = sortedPoints[3];
            }

            Point2f[] srcQuad = sortedPoints;
            Point2f[] dstPoints = new Point2f[]
            {
                new Point2f(0, 0),
                new Point2f(targetWidth - 1, 0),
                new Point2f(targetWidth - 1, targetHeight - 1),
                new Point2f(0, targetHeight - 1)
            };

            // 计算透视变换矩阵
            Mat transformMatrix = Cv2.GetPerspectiveTransform(srcQuad, dstPoints);
            
            // 应用透视变换
            Mat rotatedMat = new Mat();
            Cv2.WarpPerspective(src, rotatedMat, transformMatrix, new Size((int)targetWidth, (int)targetHeight));

            transformMatrix.Dispose();
            return rotatedMat;
        }

        static double GetAngleFromQuad(Point2f[] quadPoints)
        {
            var sortedPoints = SortQuadPointsImproved(quadPoints);
            Point2f tl = sortedPoints[0];
            Point2f tr = sortedPoints[1];
            // 计算上边缘的角度
            double angle = Math.Atan2(tr.Y - tl.Y, tr.X - tl.X);
            return angle;
        }

        static Point2f[] SortQuadPointsImproved(Point2f[] points)
        {
            if (points.Length != 4) return points;

            // 计算质心
            Point2f center = new Point2f(
                points.Average(p => p.X),
                points.Average(p => p.Y)
            );

            // 根据相对于质心的角度排序
            var sortedPoints = points.OrderBy(p => Math.Atan2(p.Y - center.Y, p.X - center.X)).ToArray();

            // 重新排列为左上、右上、右下、左下的顺序
            // 找到最靠近左上角的点
            Point2f tl = sortedPoints[0];
            Point2f tr = sortedPoints[1];
            Point2f br = sortedPoints[2];
            Point2f bl = sortedPoints[3];

            // 验证排序是否正确，如果不正确则调整
            // 左上角应该是最小的X+Y值
            var pointsList = sortedPoints.ToList();
            tl = pointsList.OrderBy(p => p.X + p.Y).First();
            br = pointsList.OrderBy(p => p.X + p.Y).Last();
            
            // 剩余两个点中，右上角是Y-X最小的，左下角是Y-X最大的
            var remaining = pointsList.Where(p => p != tl && p != br).ToArray();
            if (remaining.Length == 2)
            {
                tr = remaining.OrderBy(p => p.Y - p.X).First();
                bl = remaining.OrderBy(p => p.Y - p.X).Last();
            }

            return new Point2f[] { tl, tr, br, bl };
        }

        static Point2f[] GetMinAreaQuadInOriginalImage(MaskInfo maskInfo)
        {
            // 找到mask中的非零点
            Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(maskInfo.Mask, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours.Length == 0)
            {
                return new Point2f[0];
            }

            // 获取最大轮廓
            double maxArea = 0;
            int maxIndex = 0;
            for (int i = 0; i < contours.Length; i++)
            {
                double area = Cv2.ContourArea(contours[i]);
                if (area > maxArea)
                {
                    maxArea = area;
                    maxIndex = i;
                }
            }

            // 将轮廓点转换到原图坐标系
            Point[] originalContour = new Point[contours[maxIndex].Length];
            for (int i = 0; i < contours[maxIndex].Length; i++)
            {
                originalContour[i] = new Point(
                    contours[maxIndex][i].X + maskInfo.BboxX,
                    contours[maxIndex][i].Y + maskInfo.BboxY
                );
            }

            // 计算凸包
            Point[] hull = Cv2.ConvexHull(originalContour);

            // 使用更精确的四边形逼近
            double epsilon = Cv2.ArcLength(hull, true) * 0.02;
            Point[] quad = Cv2.ApproxPolyDP(hull, epsilon, true);

            // 如果逼近结果不是四边形，尝试调整epsilon值
            if (quad.Length != 4)
            {
                // 尝试不同的epsilon值
                for (double eps = 0.01; eps <= 0.1; eps += 0.01)
                {
                    quad = Cv2.ApproxPolyDP(hull, Cv2.ArcLength(hull, true) * eps, true);
                    if (quad.Length == 4)
                    {
                        break;
                    }
                }
            }

            // 如果仍然无法得到四边形，则使用最小外接矩形的顶点
            if (quad.Length != 4)
            {
                RotatedRect minRect = Cv2.MinAreaRect(originalContour);
                return minRect.Points();
            }

            return quad.Select(p => new Point2f(p.X, p.Y)).ToArray();
        }
    }

    // 示例使用类
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // 输入文件路径
                string imageDir = @"C:\Users\Administrator\Desktop\标签识别\5-测试图";
                string modelPath = @"C:\Users\Administrator\Desktop\标签识别\005.dvt";

                Console.WriteLine("开始处理...");
                Console.WriteLine($"处理目录: {imageDir}");
                Console.WriteLine($"模型路径: {modelPath}");

                // 检查目录是否存在
                if (!Directory.Exists(imageDir))
                {
                    Console.WriteLine($"目录不存在: {imageDir}");
                    return;
                }

                // 获取所有图像文件
                string[] imageExtensions = { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.tiff", "*.tif" };
                List<string> imageFiles = new List<string>();
                
                foreach (string extension in imageExtensions)
                {
                    imageFiles.AddRange(Directory.GetFiles(imageDir, extension, SearchOption.TopDirectoryOnly));
                }

                Console.WriteLine($"找到 {imageFiles.Count} 个图像文件");

                if (imageFiles.Count == 0)
                {
                    Console.WriteLine("未找到图像文件");
                    return;
                }

                // 创建处理器实例
                using (ILabelProcessor processor = new LabelProcessor())
                {
                    // 创建输出目录
                    string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "processed_images");
                    Directory.CreateDirectory(outputDir);

                    int totalProcessed = 0;
                    int totalLabels = 0;

                    // 处理每个图像文件
                    for (int fileIndex = 0; fileIndex < imageFiles.Count; fileIndex++)
                    {
                        string imagePath = imageFiles[fileIndex];
                        string fileName = Path.GetFileNameWithoutExtension(imagePath);
                        
                        Console.WriteLine($"\n{new string('=', 50)}");
                        Console.WriteLine($"处理图像 {fileIndex + 1}/{imageFiles.Count}: {fileName}");
                        Console.WriteLine($"图像路径: {imagePath}");

                        try
                        {
                            // 使用cv.read读取图像
                            using (Mat originalImage = Cv2.ImRead(imagePath))
                            {
                                if (originalImage.Empty())
                                {
                                    Console.WriteLine($"无法加载图像: {imagePath}");
                                    continue;
                                }

                                Console.WriteLine($"图像尺寸: {originalImage.Width}x{originalImage.Height}");

                                // 处理图像
                                using (var results = processor.ProcessLabels(originalImage, modelPath))
                                {
                                    Console.WriteLine($"检测到 {results.Count} 个标签纸");

                                    // 处理结果
                                    for (int i = 0; i < results.Count; i++)
                                    {
                                        var result = results[i];
                                        Console.WriteLine($"\n--- 标签纸 {i} ---");
                                        Console.WriteLine($"角度: {result.Angle * 180 / Math.PI:F2}度");
                                        
                                        // 打印四边形顶点
                                        Console.WriteLine("四边形顶点:");
                                        for (int j = 0; j < result.QuadPoints.Length; j++)
                                        {
                                            Console.WriteLine($"  点{j}: ({result.QuadPoints[j].X:F1}, {result.QuadPoints[j].Y:F1})");
                                        }
                                        
                                        // 保存处理后的图像
                                        string outputPath = Path.Combine(outputDir, $"{fileName}_label_{i}.png");
                                        Cv2.ImWrite(outputPath, result.ProcessedImage);
                                        Console.WriteLine($"已保存处理后的图像: {outputPath}");
                                        Console.WriteLine($"图像尺寸: {result.ProcessedImage.Width}x{result.ProcessedImage.Height}");
                                    }

                                    totalProcessed++;
                                    totalLabels += results.Count;
                                    
                                    Console.WriteLine($"处理完成，释放资源...");
                                } // results会自动释放
                            } // originalImage会自动释放
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"处理图像 {fileName} 时发生错误: {ex.Message}");
                        }
                    }

                    Console.WriteLine($"\n{new string('=', 50)}");
                    Console.WriteLine($"处理完成!");
                    Console.WriteLine($"成功处理图像: {totalProcessed}/{imageFiles.Count}");
                    Console.WriteLine($"总共检测到标签纸: {totalLabels} 个");
                    Console.WriteLine($"输出目录: {outputDir}");
                } // processor会自动释放，包括模型资源
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine($"堆栈: {ex.StackTrace}");
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }
    }
}
