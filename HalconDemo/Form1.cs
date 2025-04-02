using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using HalconDotNet;
using OpenCvSharp;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using System.IO;

namespace HalconDemo
{
    public partial class Form1: Form
    {
        private HObject halconImage = null;  // Halcon图像对象
        private string currentImagePath = ""; // 当前图像路径
        private string currentModelPath = ""; // 当前模型路径
        private Model model = null; // DLCV模型对象
        private int deviceId = 0;  // 默认使用GPU 0
        
        // 保存原始图像尺寸
        private int imageWidth = 0;
        private int imageHeight = 0;
        
        // 保存图像显示的变换信息
        private double scaleX = 1.0;  // X方向缩放比例
        private double scaleY = 1.0;  // Y方向缩放比例
        private double offsetX = 0.0;  // X方向偏移
        private double offsetY = 0.0;  // Y方向偏移
        
        // 边界框容差值
        private const double BOX_TOLERANCE = 1.0;  // 允许边界框尺寸有1像素的容差

        public Form1()
        {
            InitializeComponent();
            
            // 初始化Halcon图像对象
            halconImage = new HObject();
            
            // 设置异常处理
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => 
            {
                MessageBox.Show($"发生未处理的异常：{e.ExceptionObject}", "严重错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
        }

        // 窗口大小改变事件处理
        private void Form1_Resize(object sender, EventArgs e)
        {
            // 重新显示图像
            RefreshDisplayImage();
        }

        // HWindowControl大小改变事件处理
        private void hWindowControl_SizeChanged(object sender, EventArgs e)
        {
            // 更新HWindowControl的WindowSize属性，确保与实际大小一致
            hWindowControl.WindowSize = new System.Drawing.Size(hWindowControl.Width, hWindowControl.Height);
            
            // 重新显示图像
            RefreshDisplayImage();
        }

        // 刷新显示图像
        private void RefreshDisplayImage()
        {
            if (halconImage != null && halconImage.IsInitialized())
            {
                // 获取图像尺寸
                HTuple width = new HTuple(), height = new HTuple();
                HOperatorSet.GetImageSize(halconImage, out width, out height);
                
                // 保存原始图像尺寸以便后续处理
                imageWidth = width.I;
                imageHeight = height.I;
                
                // 清空显示窗口
                hWindowControl.HalconWindow.ClearWindow();
                
                // 设置窗口显示区域为图像的实际尺寸，控件会自动处理缩放
                HOperatorSet.SetPart(hWindowControl.HalconWindow, 0, 0, height.I - 1, width.I - 1);
                
                // 显示图像
                HOperatorSet.DispObj(halconImage, hWindowControl.HalconWindow);
            }
        }

        // 选择图像按钮点击事件
        private void btnSelectImage_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "图像文件|*.png;*.bmp;*.tif;*.tiff;*.jpg;*.jpeg|所有文件|*.*";
                openFileDialog.Title = "选择图像文件";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    currentImagePath = openFileDialog.FileName;
                    txtImagePath.Text = currentImagePath;
                    
                    // 清空之前的结果
                    lblResult.Text = "推理结果：";
                    
                    // 加载并显示图像
                    LoadAndDisplayImage(currentImagePath);
                }
            }
        }

        // 选择模型按钮点击事件
        private void btnSelectModel_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "模型文件|*.dvt|所有文件|*.*";
                openFileDialog.Title = "选择模型文件";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    currentModelPath = openFileDialog.FileName;
                    txtModelPath.Text = currentModelPath;
                    
                    // 释放之前的模型（如果有）
                    if (model != null)
                    {
                        Utils.FreeAllModels();
                        model = null;
                    }
                    
                    // 立即加载模型
                    try
                    {
                        Cursor = Cursors.WaitCursor;
                        lblResult.Text = "正在加载模型...";
                        Application.DoEvents();
                        
                        LoadModel();
                        
                        Cursor = Cursors.Default;
                    }
                    catch (Exception ex)
                    {
                        Cursor = Cursors.Default;
                        MessageBox.Show($"加载模型时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        lblResult.Text = "模型加载失败";
                    }
                }
            }
        }

        // 开始推理按钮点击事件
        private void btnInfer_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentImagePath))
            {
                MessageBox.Show("请先选择图像文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (halconImage == null || !halconImage.IsInitialized())
            {
                MessageBox.Show("图像未正确加载！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            if (string.IsNullOrEmpty(currentModelPath) || model == null)
            {
                MessageBox.Show("请先选择并加载模型文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                lblResult.Text = "推理结果：正在推理中...";
                Application.DoEvents();
                
                // 执行推理
                Utils.CSharpResult result = InferWithHalconImage(halconImage);
                
                // 显示推理结果
                DisplayResults(result);
                
                Cursor = Cursors.Default;
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show($"推理过程中发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblResult.Text = "推理结果：推理失败";
            }
        }

        // 加载并显示图像
        private void LoadAndDisplayImage(string imagePath)
        {
            try
            {
                // 检查文件是否存在
                if (!File.Exists(imagePath))
                {
                    MessageBox.Show($"文件不存在：{imagePath}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                // 清空之前的图像
                if (halconImage != null && halconImage.IsInitialized())
                {
                    halconImage.Dispose();
                }
                
                try
                {
                    // 读取Halcon图像
                    HOperatorSet.ReadImage(out halconImage, imagePath);
                }
                catch (HalconDotNet.HOperatorException)
                {
                    // Halcon无法读取图像时，尝试使用OpenCV读取然后转换
                    try
                    {
                        // 使用OpenCV读取图像
                        using (Mat opencvImage = Cv2.ImRead(imagePath))
                        {
                            if (opencvImage.Empty())
                            {
                                throw new Exception("无法读取图像");
                            }
                            
                            // 创建临时文件
                            string tempFile = Path.GetTempFileName() + ".png";
                            
                            // 保存为PNG格式
                            Cv2.ImWrite(tempFile, opencvImage);
                            
                            // 读取临时文件
                            HOperatorSet.ReadImage(out halconImage, tempFile);
                            
                            // 删除临时文件
                            try { File.Delete(tempFile); } catch { }
                        }
                    }
                    catch (Exception cvEx)
                    {
                        throw new Exception($"无法读取图像：{cvEx.Message}");
                    }
                }
                
                // 记录图像尺寸
                HTuple width = new HTuple(), height = new HTuple();
                HOperatorSet.GetImageSize(halconImage, out width, out height);
                imageWidth = width.I;
                imageHeight = height.I;
                
                // 刷新显示图像
                RefreshDisplayImage();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图像时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 加载模型
        private void LoadModel()
        {
            try
            {
                if (string.IsNullOrEmpty(currentModelPath))
                {
                    throw new Exception("模型路径为空");
                }
                
                // 检查文件是否存在
                if (!File.Exists(currentModelPath))
                {
                    throw new Exception($"模型文件不存在：{currentModelPath}");
                }
                
                // 释放之前的模型（如果有）
                if (model != null)
                {
                    Utils.FreeAllModels();
                }
                
                // 创建新模型
                model = new Model(currentModelPath, deviceId);
                
                // 获取模型信息
                JObject modelInfo = model.GetModelInfo();
                if (modelInfo == null)
                {
                    throw new Exception("无法获取模型信息");
                }
                
                string modelType = modelInfo["model_type"]?.ToString() ?? "未知";
                
                lblResult.Text = $"推理结果：模型加载成功，类型：{modelType}";
            }
            catch (Exception ex)
            {
                model = null;
                throw new Exception($"加载模型失败：{ex.Message}");
            }
        }

        // 使用Halcon图像进行推理
        private Utils.CSharpResult InferWithHalconImage(HObject halconImage)
        {
            if (model == null)
            {
                throw new Exception("模型未加载");
            }
            
            // 将Halcon图像转换为OpenCV Mat
            Mat halconAsMat = HalconImageToMat(halconImage);
            
            if (halconAsMat == null || halconAsMat.Empty())
            {
                throw new Exception("图像转换失败");
            }
            
            // 执行推理
            Utils.CSharpResult result = model.Infer(halconAsMat);
            
            // 释放Mat资源
            halconAsMat.Dispose();
            
            return result;
        }

        // Halcon图像转换为OpenCV Mat（参考自程序示例）
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

                // 释放临时Mat
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

        // 在Halcon窗口上显示推理结果
        private void DisplayResults(Utils.CSharpResult result)
        {
            try
            {
                // 首先重新显示原始图像
                RefreshDisplayImage();
                
                // 如果没有结果，直接返回
                if (result.SampleResults == null || result.SampleResults.Count == 0 || 
                    result.SampleResults[0].Results == null || result.SampleResults[0].Results.Count == 0)
                {
                    lblResult.Text = "推理结果：未检测到任何目标";
                    return;
                }

                var sampleResult = result.SampleResults[0]; // 假设只有一个样本结果
                int objectCount = sampleResult.Results.Count;
                lblResult.Text = $"推理结果：检测到 {objectCount} 个目标";

                // 为每个检测结果设置不同的颜色
                string[] colors = new string[] {
                    "red", "green", "blue", "yellow", "cyan", "magenta", "orange", 
                    "deep pink", "medium sea green", "slate blue"
                };

                // 设置文本显示参数
                HOperatorSet.SetFont(hWindowControl.HalconWindow, "Arial-Bold-16");
                
                // 处理每个检测结果
                for (int i = 0; i < sampleResult.Results.Count; i++)
                {
                    var obj = sampleResult.Results[i];
                    string color = colors[i % colors.Length]; // 循环使用颜色
                    
                    // 设置当前的绘图颜色
                    HOperatorSet.SetColor(hWindowControl.HalconWindow, color);
                    
                    // 如果有边界框，处理边界框和掩码
                    if (obj.Bbox != null && obj.Bbox.Count >= 4)
                    {
                        // 边界框坐标 [x, y, width, height]
                        double x = obj.Bbox[0];
                        double y = obj.Bbox[1];
                        double width = obj.Bbox[2];
                        double height = obj.Bbox[3];
                        
                        // 验证坐标值的有效性
                        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(width) || double.IsNaN(height))
                        {
                            continue;
                        }
                        
                        // 验证宽度和高度
                        if (width <= BOX_TOLERANCE || height <= BOX_TOLERANCE)
                        {
                            // 修复太小的边界框
                            if (width <= BOX_TOLERANCE) width = BOX_TOLERANCE;
                            if (height <= BOX_TOLERANCE) height = BOX_TOLERANCE;
                        }
                        
                        // 生成矩形区域
                        HObject rectangle;
                        HOperatorSet.GenRectangle1(out rectangle, y, x, y + height, x + width);
                        
                        // 设置线宽和绘制样式
                        HOperatorSet.SetLineWidth(hWindowControl.HalconWindow, 2);
                        HOperatorSet.SetDraw(hWindowControl.HalconWindow, "margin");
                        
                        // 准备类别和置信度文本
                        string text = $"{obj.CategoryName}: {obj.Score:F2}";
                        
                        // 处理掩码
                        if (obj.WithMask && obj.Mask != null && !obj.Mask.Empty())
                        {
                            try
                            {
                                // 创建与原图像尺寸相同的空白Mat
                                Mat fullSizeMask = new Mat(imageHeight, imageWidth, MatType.CV_8UC1, Scalar.Black);
                                
                                // 将掩码调整为目标尺寸
                                Mat resizedMask = new Mat();
                                Cv2.Resize(obj.Mask, resizedMask, new OpenCvSharp.Size(width, height));
                                
                                // 如果掩码全为0，则应用阈值处理
                                double minVal, maxVal;
                                OpenCvSharp.Point minLoc, maxLoc;
                                Cv2.MinMaxLoc(resizedMask, out minVal, out maxVal, out minLoc, out maxLoc);
                                if (maxVal < 1)
                                {
                                    Cv2.Threshold(resizedMask, resizedMask, 0, 255, ThresholdTypes.Binary);
                                }
                                
                                // 创建目标区域的ROI
                                Rect roiRect = new Rect(
                                    (int)Math.Max(0, Math.Min(x, imageWidth - 1)), 
                                    (int)Math.Max(0, Math.Min(y, imageHeight - 1)),
                                    (int)Math.Min(width, imageWidth - (int)x),
                                    (int)Math.Min(height, imageHeight - (int)y)
                                );
                                
                                // 确保ROI有效
                                if (roiRect.Width > 0 && roiRect.Height > 0)
                                {
                                    Mat maskRoi = resizedMask[
                                        new Rect(0, 0, Math.Min(resizedMask.Width, roiRect.Width), 
                                                 Math.Min(resizedMask.Height, roiRect.Height))];
                                    
                                    // 复制到目标位置
                                    maskRoi.CopyTo(fullSizeMask[roiRect]);
                                    
                                    // 转换为Halcon图像和区域
                                    HObject maskHalcon = MatToHalcon(fullSizeMask);
                                    HObject maskRegion;
                                    HOperatorSet.Threshold(maskHalcon, out maskRegion, 1, 255);
                                    
                                    // 检查区域是否为空
                                    HTuple area, row, column;
                                    HOperatorSet.AreaCenter(maskRegion, out area, out row, out column);
                                    
                                    if (area.D <= 0)
                                    {
                                        HOperatorSet.Threshold(maskHalcon, out maskRegion, 0, 255);
                                        HOperatorSet.AreaCenter(maskRegion, out area, out row, out column);
                                    }
                                    
                                    // 显示掩码
                                    if (area.D > 0)
                                    {
                                        // 根据颜色名称设置RGB值
                                        int r = 0, g = 0, b = 0;
                                        switch (color.ToLower())
                                        {
                                            case "red": r = 255; g = 0; b = 0; break;
                                            case "green": r = 0; g = 255; b = 0; break;
                                            case "blue": r = 0; g = 0; b = 255; break;
                                            case "yellow": r = 255; g = 255; b = 0; break;
                                            case "cyan": r = 0; g = 255; b = 255; break;
                                            case "magenta": r = 255; g = 0; b = 255; break;
                                            case "orange": r = 255; g = 165; b = 0; break;
                                            case "deep pink": r = 255; g = 20; b = 147; break;
                                            case "medium sea green": r = 60; g = 179; b = 113; break;
                                            case "slate blue": r = 106; g = 90; b = 205; break;
                                            default: r = 255; g = 0; b = 0; break; // 默认红色
                                        }
                                        
                                        // 创建相应颜色的多通道图像
                                        HObject redChannel, greenChannel, blueChannel;
                                        HOperatorSet.GenImageConst(out redChannel, "byte", imageWidth, imageHeight);
                                        HOperatorSet.GenImageConst(out greenChannel, "byte", imageWidth, imageHeight);
                                        HOperatorSet.GenImageConst(out blueChannel, "byte", imageWidth, imageHeight);
                                        
                                        // 在各个通道上绘制区域
                                        HObject redFilled, greenFilled, blueFilled;
                                        HOperatorSet.PaintRegion(maskRegion, redChannel, out redFilled, r, "fill");
                                        HOperatorSet.PaintRegion(maskRegion, greenChannel, out greenFilled, g, "fill");
                                        HOperatorSet.PaintRegion(maskRegion, blueChannel, out blueFilled, b, "fill");
                                        
                                        // 合并为彩色图像
                                        HObject coloredMask;
                                        HOperatorSet.Compose3(redFilled, greenFilled, blueFilled, out coloredMask);
                                        
                                        // 设置绘制模式并显示掩码
                                        HOperatorSet.SetDraw(hWindowControl.HalconWindow, "fill");
                                        HOperatorSet.SetColor(hWindowControl.HalconWindow, color);
                                        HOperatorSet.DispObj(maskRegion, hWindowControl.HalconWindow);
                                        
                                        // 释放资源
                                        coloredMask.Dispose();
                                        redChannel.Dispose();
                                        greenChannel.Dispose();
                                        blueChannel.Dispose();
                                        redFilled.Dispose();
                                        greenFilled.Dispose();
                                        blueFilled.Dispose();
                                    }
                                    
                                    // 释放资源
                                    maskHalcon.Dispose();
                                    maskRegion.Dispose();
                                }
                                
                                // 释放资源
                                fullSizeMask.Dispose();
                                resizedMask.Dispose();
                            }
                            catch (Exception)
                            {
                                // 出错时继续，显示边界框和文字
                            }
                        }
                        
                        // 显示边界框
                        HOperatorSet.SetColor(hWindowControl.HalconWindow, color);
                        HOperatorSet.SetDraw(hWindowControl.HalconWindow, "margin");
                        HOperatorSet.SetLineWidth(hWindowControl.HalconWindow, 2);
                        HOperatorSet.DispObj(rectangle, hWindowControl.HalconWindow);
                        
                        // 显示类别和置信度 - 使用白色
                        HOperatorSet.SetColor(hWindowControl.HalconWindow, "white");
                        HOperatorSet.DispText(hWindowControl.HalconWindow, text, "window", Math.Max(0, y - 25), x, "white", new HTuple(), new HTuple());
                        
                        // 释放矩形资源
                        rectangle.Dispose();
                    }
                    else
                    {
                        // 如果没有边界框，只在图像上方显示类别和分数
                        string text = $"{obj.CategoryName}: {obj.Score:F2}";
                        int yPos = 20 + i * 30; // 每行文本的垂直位置
                        HOperatorSet.SetColor(hWindowControl.HalconWindow, "white");
                        HOperatorSet.DispText(hWindowControl.HalconWindow, text, "image", yPos, 10, "white", new HTuple(), new HTuple());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"显示结果时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // CopyMemory方法，用于内存复制
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern void CopyMemory(IntPtr dest, IntPtr source, int size);

        // Mat转换为Halcon图像
        private HObject MatToHalcon(Mat mat)
        {
            HObject hImage = new HObject();
            
            try
            {
                if (mat.Channels() == 1)
                {
                    // 单通道灰度图处理
                    IntPtr ptr = mat.Data;
                    int width = mat.Cols;
                    int height = mat.Rows;
                    HOperatorSet.GenImage1(out hImage, "byte", width, height, ptr);
                }
                else if (mat.Channels() == 3)
                {
                    // 三通道彩色图处理
                    Mat[] channels = new Mat[3];
                    Cv2.Split(mat, out channels);
                    
                    IntPtr ptrR = channels[0].Data;
                    IntPtr ptrG = channels[1].Data;
                    IntPtr ptrB = channels[2].Data;
                    
                    int width = mat.Cols;
                    int height = mat.Rows;
                    
                    HOperatorSet.GenImage3(out hImage, "byte", width, height, ptrR, ptrG, ptrB);
                    
                    // 释放通道资源
                    foreach (Mat channel in channels)
                    {
                        channel.Dispose();
                    }
                }
                else
                {
                    throw new Exception($"不支持的通道数：{mat.Channels()}");
                }
            }
            catch (Exception ex)
            {
                if (hImage != null && hImage.IsInitialized())
                {
                    hImage.Dispose();
                }
                throw new Exception($"Mat转Halcon图像失败: {ex.Message}");
            }
            
            return hImage;
        }
    }
}
