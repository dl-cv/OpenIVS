using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;

namespace MiniAreaDemo
{
    public class MaskInfo
    {
        public Mat Mask { get; set; }
        public int BboxX { get; set; }
        public int BboxY { get; set; }
        public int BboxW { get; set; }
        public int BboxH { get; set; }
    }
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // 创建输出目录
                string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_output");
                Directory.CreateDirectory(outputDir);

                // 输入文件路径
                string imagePath = @"C:\Users\Administrator\Desktop\20250718150607902_origin.png";
                string modelPath = @"Z:\3-保密-客户数据\C250705-西安恒盈晟-贴纸外轮廓提取\3-训练数据-语义分割\2-模型\贴子轮廓检测-语义分割_20250722_112221.dvt";

                Console.WriteLine("开始处理...");
                
                // 检查文件是否存在
                if (!File.Exists(imagePath))
                {
                    Console.WriteLine($"图片文件不存在: {imagePath}");
                    return;
                }

                if (!File.Exists(modelPath))
                {
                    Console.WriteLine($"模型文件不存在: {modelPath}");
                    return;
                }

                // 加载图片
                Mat originalImage = Cv2.ImRead(imagePath);
                if (originalImage.Empty())
                {
                    Console.WriteLine("无法加载图片");
                    return;
                }

                Console.WriteLine($"图片加载成功: {originalImage.Width}x{originalImage.Height}");

                // 使用模型进行推理并获取mask和bbox信息
                var maskInfos = InferAndGetMasks(originalImage, modelPath);
                if (maskInfos.Count == 0)
                {
                    Console.WriteLine("未找到有效的mask");
                    return;
                }

                // 处理所有mask
                for (int i = 0; i < maskInfos.Count; i++)
                {
                    var maskInfo = maskInfos[i];
                    Console.WriteLine($"\n=== 处理第{i}个Mask ===");
                    Console.WriteLine($"Mask信息: {maskInfo.Mask.Width}x{maskInfo.Mask.Height}, Bbox: [{maskInfo.BboxX},{maskInfo.BboxY},{maskInfo.BboxW},{maskInfo.BboxH}]");

                    // 将mask坐标转换到原图坐标系并获取最小外接四边形
                    Point2f[] quadPoints = GetMinAreaQuadInOriginalImage(maskInfo);

                    if (quadPoints.Length != 4)
                    {
                        Console.WriteLine("无法获取有效的四边形，跳过此mask。");
                        continue;
                    }

                    // 计算角度
                    double angleInRadians = GetAngleFromQuad(quadPoints);
                    Console.WriteLine($"原图坐标系最小外接四边形: 角度{angleInRadians:F4}弧度");

                    // 对原图进行旋转+裁剪一步到位
                    Mat rotatedMat = RotateAndCropInOneStep(originalImage, quadPoints);

                    // 打印前100个字符信息
                    PrintMatData(rotatedMat);

                    // 保存处理后的mat
                    string outputPath = Path.Combine(outputDir, $"rotated_mask_{i}.png");
                    Cv2.ImWrite(outputPath, rotatedMat);
                    Console.WriteLine($"已保存旋转后的mask: {outputPath}");
                    
                    rotatedMat.Dispose();
                }

                // 清理资源
                originalImage.Dispose();
                foreach (var info in maskInfos) info.Mask.Dispose();

                Console.WriteLine("处理完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine($"堆栈: {ex.StackTrace}");
            }

            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }

        static List<MaskInfo> InferAndGetMasks(Mat originalImage, string modelPath)
        {
            List<MaskInfo> maskInfos = new List<MaskInfo>();
            Model model = null;
            
            try
            {
                Console.WriteLine("正在加载模型...");
                
                // 创建模型实例，使用设备ID 0
                model = new Model(modelPath, 0);
                
                Console.WriteLine("模型加载成功，开始推理...");
                
                // 进行推理
                var result = model.Infer(originalImage);
                
                Console.WriteLine($"推理完成，检测到 {result.SampleResults[0].Results.Count} 个对象");
                
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
                            
                            Console.WriteLine($"提取到mask并resize: {mask.Width}x{mask.Height}, bbox: [{bboxX},{bboxY},{bboxW},{bboxH}]");
                        }
                        else
                        {
                            // 如果没有mask，根据bbox尺寸创建全白mask
                            mask = new Mat(bboxH, bboxW, MatType.CV_8UC1, Scalar.All(255));
                            Console.WriteLine($"根据bbox创建mask: {mask.Width}x{mask.Height}, bbox: [{bboxX},{bboxY},{bboxW},{bboxH}]");
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
                
                Console.WriteLine($"总共提取了 {maskInfos.Count} 个mask");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"模型推理失败: {ex.Message}");
                Console.WriteLine($"堆栈: {ex.StackTrace}");
            }
            finally
            {
                // 释放模型资源
                model?.Dispose();
            }
            
            return maskInfos;
        }

        static Mat RotateAndCropInOneStep(Mat src, Point2f[] quadPoints)
        {
            var sortedPoints = SortQuadPoints(quadPoints);
            Point2f tl = sortedPoints[0];
            Point2f tr = sortedPoints[1];
            Point2f br = sortedPoints[2];
            Point2f bl = sortedPoints[3];

            float width = (float)(Point2f.Distance(tl, tr) + Point2f.Distance(bl, br)) / 2.0f;
            float height = (float)(Point2f.Distance(tl, bl) + Point2f.Distance(tr, br)) / 2.0f;

            Point2f[] srcQuad = sortedPoints;

            // Ensure the output is landscape by making width the longer side
            if (width < height)
            {
                (width, height) = (height, width);
                // Rotate source points by 90 degrees to match new orientation
                srcQuad = new Point2f[] { bl, tl, tr, br };
            }

            Point2f[] dstPoints = new Point2f[]
            {
                new Point2f(0, 0),
                new Point2f(width - 1, 0),
                new Point2f(width - 1, height - 1),
                new Point2f(0, height - 1)
            };

            Mat transformMatrix = Cv2.GetPerspectiveTransform(srcQuad, dstPoints);
            Mat rotatedMat = new Mat();
            Cv2.WarpPerspective(src, rotatedMat, transformMatrix, new Size((int)width, (int)height));

            transformMatrix.Dispose();
            return rotatedMat;
        }

        static double GetAngleFromQuad(Point2f[] quadPoints)
        {
            var sortedPoints = SortQuadPoints(quadPoints);
            Point2f tl = sortedPoints[0];
            Point2f tr = sortedPoints[1];
            // Angle of the top edge
            double angle = Math.Atan2(tr.Y - tl.Y, tr.X - tl.X);
            return angle;
        }

        static Point2f[] SortQuadPoints(Point2f[] points)
        {
            if (points.Length != 4) return points;

            // Sort by sum of coords to find top-left and bottom-right
            var sortedBySum = points.OrderBy(p => p.X + p.Y).ToArray();
            Point2f tl = sortedBySum[0];
            Point2f br = sortedBySum[3];

            // Sort by difference of coords to find top-right and bottom-left
            var sortedByDiff = points.OrderBy(p => p.Y - p.X).ToArray();
            Point2f tr = sortedByDiff[0];
            Point2f bl = sortedByDiff[3];

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
                Console.WriteLine("未找到轮廓");
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

            // 使用 ApproxPolyDP 逼近四边形
            Point[] quad = Cv2.ApproxPolyDP(hull, Cv2.ArcLength(hull, true) * 0.02, true);

            // 如果逼近结果不是四边形，则使用最小外接矩形的顶点
            if (quad.Length != 4)
            {
                Console.WriteLine("无法逼近四边形，将使用最小外接矩形。");
                RotatedRect minRect = Cv2.MinAreaRect(originalContour);
                return minRect.Points();
            }

            return quad.Select(p => new Point2f(p.X, p.Y)).ToArray();
        }

        static void PrintMatData(Mat mat)
        {
            Console.WriteLine("旋转后mat的前100个数据:");
            
            // 将mat转换为字节数组
            int totalElements = Math.Min(100, (int)mat.Total());
            byte[] data = new byte[totalElements];
            
            // 使用Indexer访问数据
            int index = 0;
            for (int y = 0; y < mat.Height && index < totalElements; y++)
            {
                for (int x = 0; x < mat.Width && index < totalElements; x++)
                {
                    data[index++] = mat.Get<byte>(y, x);
                }
            }
            
            // 打印前100个值
            for (int i = 0; i < Math.Min(100, data.Length); i++)
            {
                if (i > 0 && i % 10 == 0) Console.WriteLine();
                Console.Write($"{data[i]:D3} ");
            }
            Console.WriteLine();
        }
    }
}
