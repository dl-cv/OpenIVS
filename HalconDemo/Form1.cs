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
                try
                {
                    // 获取图像尺寸
                    HTuple width = new HTuple(), height = new HTuple();
                    HOperatorSet.GetImageSize(halconImage, out width, out height);
                    
                    // 计算适合的显示区域
                    double imageRatio = (double)width.I / height.I;
                    double windowRatio = (double)hWindowControl.Width / hWindowControl.Height;
                    
                    // 清空显示窗口
                    hWindowControl.HalconWindow.ClearWindow();
                    
                    // 设置显示窗口的大小
                    hWindowControl.WindowSize = new System.Drawing.Size(hWindowControl.Width, hWindowControl.Height);
                    
                    // 设置图像部分并显示
                    if (imageRatio >= windowRatio)
                    {
                        // 图像较宽，宽度撑满，高度自适应
                        int displayWidth = hWindowControl.Width;
                        int displayHeight = (int)(displayWidth / imageRatio);
                        
                        // 居中显示
                        int offsetY = (hWindowControl.Height - displayHeight) / 2;
                        
                        hWindowControl.ImagePart = new Rectangle(0, 0, width.I, height.I);
                        
                        // 调整显示区域
                        HOperatorSet.SetPart(hWindowControl.HalconWindow, 0, 0, hWindowControl.Height - 1, hWindowControl.Width - 1);
                    }
                    else
                    {
                        // 图像较高，高度撑满，宽度自适应
                        int displayHeight = hWindowControl.Height;
                        int displayWidth = (int)(displayHeight * imageRatio);
                        
                        // 居中显示
                        int offsetX = (hWindowControl.Width - displayWidth) / 2;
                        
                        hWindowControl.ImagePart = new Rectangle(0, 0, width.I, height.I);
                        
                        // 调整显示区域
                        HOperatorSet.SetPart(hWindowControl.HalconWindow, 0, 0, hWindowControl.Height - 1, hWindowControl.Width - 1);
                    }
                    
                    // 显示图像
                    HOperatorSet.DispObj(halconImage, hWindowControl.HalconWindow);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"刷新图像时出错: {ex.Message}");
                }
            }
        }

        // 选择图像按钮点击事件
        private void btnSelectImage_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "图像文件|*.png;*.bmp;*.tif;*.tiff|所有文件|*.*";
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
                catch (HalconDotNet.HOperatorException ex)
                {
                    // Halcon无法读取图像时，尝试使用OpenCV读取然后转换
                    MessageBox.Show($"Halcon无法直接读取该图像，尝试使用OpenCV读取：{ex.Message}", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    
                    try
                    {
                        // 使用OpenCV读取图像
                        using (Mat opencvImage = Cv2.ImRead(imagePath))
                        {
                            if (opencvImage.Empty())
                            {
                                throw new Exception("OpenCV无法读取图像");
                            }
                            
                            // 创建临时文件
                            string tempFile = Path.GetTempFileName() + ".png";
                            
                            // 保存为PNG格式（Halcon可以读取）
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
            try
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
            catch (Exception ex)
            {
                throw new Exception($"推理过程中发生错误：{ex.Message}");
            }
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
                if (result.SampleResults == null || result.SampleResults.Count == 0 || result.SampleResults[0].Results == null || result.SampleResults[0].Results.Count == 0)
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
                HOperatorSet.SetFont(hWindowControl.HalconWindow, "Arial-Bold-12");
                
                // 处理每个检测结果
                for (int i = 0; i < sampleResult.Results.Count; i++)
                {
                    var obj = sampleResult.Results[i];
                    string color = colors[i % colors.Length]; // 循环使用颜色
                    
                    // 设置当前的绘图颜色
                    HOperatorSet.SetColor(hWindowControl.HalconWindow, color);
                    
                    // 如果有边界框，绘制边界框
                    if (obj.Bbox != null && obj.Bbox.Count >= 4)
                    {
                        // 边界框坐标 [x_min, y_min, x_max, y_max]
                        double x1 = obj.Bbox[0];
                        double y1 = obj.Bbox[1];
                        double x2 = obj.Bbox[2];
                        double y2 = obj.Bbox[3];
                        
                        // 创建矩形ROI
                        HObject rectangle;
                        HOperatorSet.GenRectangle1(out rectangle, y1, x1, y2, x2);
                        
                        // 设置线宽
                        HOperatorSet.SetLineWidth(hWindowControl.HalconWindow, 2);
                        
                        // 绘制矩形
                        HOperatorSet.DispObj(rectangle, hWindowControl.HalconWindow);
                        
                        // 释放资源
                        rectangle.Dispose();
                        
                        // 显示类别和置信度
                        string text = $"{obj.CategoryName}: {obj.Score:F2}";
                        HOperatorSet.DispText(hWindowControl.HalconWindow, text, "image", y1 - 12, x1, color, new HTuple(), new HTuple());
                    }
                    else
                    {
                        // 如果没有边界框，只在图像上方显示类别和分数
                        string text = $"{obj.CategoryName}: {obj.Score:F2}";
                        HTuple width = new HTuple(), height = new HTuple();
                        HOperatorSet.GetImageSize(halconImage, out width, out height);
                        int yPos = 20 + i * 30; // 每行文本的垂直位置
                        HOperatorSet.DispText(hWindowControl.HalconWindow, text, "window", yPos, 10, color, new HTuple(), new HTuple());
                    }
                    
                    // 如果有掩码，显示掩码
                    if (obj.WithMask && obj.Mask != null && !obj.Mask.Empty())
                    {
                        try
                        {
                            // 将OpenCV掩码转换为Halcon区域
                            HObject maskRegion;
                            HTuple width = new HTuple(), height = new HTuple();
                            HOperatorSet.GetImageSize(halconImage, out width, out height);
                            
                            // 创建临时文件保存掩码
                            string tempMaskFile = Path.GetTempFileName() + ".png";
                            Cv2.ImWrite(tempMaskFile, obj.Mask);
                            
                            // 读取掩码为Halcon图像
                            HObject maskImage;
                            HOperatorSet.ReadImage(out maskImage, tempMaskFile);
                            
                            // 转换为区域
                            HOperatorSet.Threshold(maskImage, out maskRegion, 1, 255);
                            
                            // 设置显示属性
                            HOperatorSet.SetColor(hWindowControl.HalconWindow, color);
                            HOperatorSet.SetDraw(hWindowControl.HalconWindow, "margin");
                            HOperatorSet.SetLineWidth(hWindowControl.HalconWindow, 1);
                            
                            // 显示区域轮廓
                            HOperatorSet.DispObj(maskRegion, hWindowControl.HalconWindow);
                            
                            // 释放资源
                            maskRegion.Dispose();
                            maskImage.Dispose();
                            
                            // 删除临时文件
                            try { File.Delete(tempMaskFile); } catch { }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"显示掩码时出错: {ex.Message}");
                        }
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
    }
}
