using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using static dlcv_infer_csharp.Utils;

namespace DLCV
{
    public class ImageViewer : Panel
    {
        private Image _image;
        private float _scale = 1.0f;
        private System.Drawing.PointF _imagePosition = new PointF(0, 0);
        private System.Drawing.Point _lastMousePosition;
        private bool _isDragging = false;

        public Image image
        {
            get => _image;
            set
            {
                _image = value;
                if (_image != null)
                {
                    _image = _image.Clone() as Bitmap;
                    // 计算缩放比例以填充整个面板
                    float panelAspect = (float)this.Width / this.Height;
                    float imageAspect = (float)_image.Width / _image.Height;

                    if (panelAspect > imageAspect)
                    {
                        // 面板更宽，按高度填充
                        _scale = (float)this.Height / _image.Height;
                    }
                    else
                    {
                        // 面板更高，按宽度填充
                        _scale = (float)this.Width / _image.Width;
                    }

                    // 计算图像初始位置以居中
                    _imagePosition.X = (this.Width - _image.Width * _scale) / 2;
                    _imagePosition.Y = (this.Height - _image.Height * _scale) / 2;

                    // 计算MinScale
                    CalculateMinScale();
                }
                //Invalidate();
            }
        }


        public float MaxScale { get; set; } = 100.0f;
        public float MinScale { get; set; } = 0.5f;

        // 新增参数控制是否显示状态文本
        public bool ShowStatusText { get; set; } = false;

        // 控制是否显示可视化结果（框、Mask、文字等）
        public bool ShowVisualization { get; set; } = true;

        public ImageViewer()
        {
            this.DoubleBuffered = true; // Enable double buffering
            this.SetStyle(ControlStyles.ResizeRedraw, true); // Redraw on resize
            this.SetStyle(ControlStyles.Selectable, true); // Allow control to accept focus
            this.TabStop = true;
        }

        // 处理键盘事件
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.V)
            {
                ShowVisualization = !ShowVisualization;
                Invalidate(); // 重绘
            }
        }

        private readonly object _Lock = new object();

        protected override void OnPaint(PaintEventArgs e)
        {
            lock (_Lock)
            {
                base.OnPaint(e);
                if (_image != null)
                {
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

                    e.Graphics.TranslateTransform(_imagePosition.X, _imagePosition.Y);
                    e.Graphics.ScaleTransform(_scale, _scale);
                    e.Graphics.DrawImage(_image, 0, 0);
                }
            }

            if (currentResults != null)
            {
                DrawResults(e);
            }
        }

        public void UpdateImageAndResult(dynamic image, CSharpResult currentResults)
        {
            lock (_Lock)
            {
                UpdateImage(image);
                UpdateResults(currentResults);
                Update();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_image == null) return;

            float oldScale = _scale;

            if (e.Delta > 0 && _scale < MaxScale)
            {
                _scale *= 1.1f;
            }
            else if (e.Delta < 0 && _scale > MinScale)
            {
                _scale /= 1.1f;
            }

            // Calculate the new image position to zoom around the mouse pointer
            float scaleChange = _scale / oldScale;
            _imagePosition.X = e.X - scaleChange * (e.X - _imagePosition.X);
            _imagePosition.Y = e.Y - scaleChange * (e.Y - _imagePosition.Y);

            AdjustImagePosition();
            //UpdateLabels();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            this.Focus(); // 获取焦点以便接收键盘事件
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _lastMousePosition = e.Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging)
            {
                float dx = e.X - _lastMousePosition.X;
                float dy = e.Y - _lastMousePosition.Y;
                _imagePosition.X += dx;
                _imagePosition.Y += dy;
                _lastMousePosition = e.Location;

                AdjustImagePosition();
                //UpdateLabels();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = false;
            }
        }

        private void AdjustImagePosition()
        {
            if (_image == null) return;

            int panelWidth = Width;
            int panelHeight = Height;
            float scaledWidth = _image.Width * _scale;
            float scaledHeight = _image.Height * _scale;

            // 自定义 Clamp 方法
            float Clamp(float value, float min, float max)
            {
                return value < min ? min : (value > max ? max : value);
            }

            // 分轴独立计算坐标边界
            void CalculateAxisBoundary(float position, float scaledSize, int panelSize, out float newPosition)
            {
                // 计算图片右/下边界
                float farEdge = position + scaledSize;

                // 核心逻辑：确保图片始终在可视区域内至少露出 100px
                float minEdge = 100 - scaledSize;  // 当图片完全左/上移时，右/下边界至少露出 100px
                float maxEdge = panelSize - 100;   // 当图片完全右/下移时，左/上边界至少露出 100px

                newPosition = Clamp(position, minEdge, maxEdge);

                // 特殊场景优化：当图片尺寸小于 Panel 时，强制居中（可选）
                //if (scaledSize < panelSize)
                //{
                //    newPosition = Clamp(position, 100, panelSize - scaledSize - 100);
                //}
            }

            // 计算 X 轴位置
            CalculateAxisBoundary(_imagePosition.X, scaledWidth, panelWidth, out float newX);

            // 计算 Y 轴位置
            CalculateAxisBoundary(_imagePosition.Y, scaledHeight, panelHeight, out float newY);

            _imagePosition = new PointF(newX, newY);
        }

        public CSharpResult? currentResults;
        // 外部可以调用的，更新图像内容
        public void UpdateImage(Image image)
        {
            this.image = image;
            CalculateMinScale(); // 新图像加载时计算MinScale
        }

        // 支持 opencv 的 Mat 类型
        public void UpdateImage(Mat image)
        {
            Bitmap bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(image);
            UpdateImage(bitmap);
        }

        public void UpdateResults(dynamic result)
        {
            currentResults = result;
        }



        public void ClearResults()
        {
            currentResults = null;
        }

        public void DrawResults(PaintEventArgs e)
        {
            if (currentResults == null || !ShowVisualization) return;

            float borderWidth = Math.Max(1, 2 / _scale); // 更细的边框
            float fontSize = Math.Max(8, 24 / _scale);
            string _statusText = "OK";

            // 遍历结构体的嵌套结构
            if (currentResults.Value.SampleResults.Count == 0)
            {
                _statusText = "No Result";
                ShowStatusText = true;
            }
            else
            {
                var sampleResult = currentResults.Value.SampleResults[0];

                foreach (var objResult in sampleResult.Results)
                {
                    // 获取对象属性
                    string categoryName = objResult.CategoryName;
                    float score = objResult.Score;
                    var bbox = objResult.Bbox;

                    if (bbox.Count < 4)
                    {
                        // 这个是分类结果，没有bbox
                        _statusText = categoryName;

                        // 根据分类结果设置状态
                        string classificationResultLower = categoryName.ToLower();
                        if (!classificationResultLower.Contains("ok"))
                        {
                            _statusText = "NG";
                        }
                        else
                        {
                            _statusText = "OK";
                        }
                        break;
                    }

                    // 颜色处理（根据结果内容设置颜色）
                    Color color = Color.Red; // 默认红色

                    // 判断categoryName内容（忽略大小写）
                    string categoryNameLower = categoryName.ToLower();
                    if (categoryNameLower.Contains("ok"))
                    {
                        color = Color.Green;
                    }
                    else if (categoryNameLower.Contains("ng"))
                    {
                        color = Color.Red;
                    }
                    // 其他情况保持默认红色

                    // 判断是否显示NG状态
                    if (!categoryNameLower.Contains("ok"))
                        _statusText = "NG";

                    // 处理旋转框检测
                    if (objResult.WithAngle)
                    {
                        // 旋转框检测：[cx, cy, w, h]
                        float cx = (float)bbox[0];
                        float cy = (float)bbox[1];
                        float w = (float)bbox[2];
                        float h = (float)bbox[3];
                        float angle = objResult.Angle;

                        // 计算旋转框的四个角点
                        PointF[] points = new PointF[4];
                        float cos = (float)Math.Cos(angle);
                        float sin = (float)Math.Sin(angle);

                        // 计算相对于中心点的偏移
                        float[] offsets = new float[] {
                            -w/2, -h/2,  // 左上
                            w/2, -h/2,   // 右上
                            w/2, h/2,    // 右下
                            -w/2, h/2    // 左下
                        };

                        // 计算旋转后的四个角点
                        for (int i = 0; i < 4; i++)
                        {
                            float x = offsets[i * 2];
                            float y = offsets[i * 2 + 1];
                            points[i] = new PointF(
                                cx + x * cos - y * sin,
                                cy + x * sin + y * cos
                            );
                        }

                        // 绘制旋转框
                        using (Pen pen = new Pen(color, borderWidth))
                        {
                            // 绘制四条边
                            for (int i = 0; i < 4; i++)
                            {
                                e.Graphics.DrawLine(pen, points[i], points[(i + 1) % 4]);
                            }
                        }

                        // 绘制标签文本
                        string label = $"{categoryName} {score:F2}";
                        using (Font font = new Font("Microsoft YaHei", fontSize))
                        {
                            SizeF textSize = e.Graphics.MeasureString(label, font);
                            // 将文本位置放在旋转框上方
                            float textX = cx - textSize.Width / 2;
                            float textY = cy - h / 2 - textSize.Height - 2;

                            // 绘制半透明黑色背景
                            using (SolidBrush backgroundBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                            {
                                e.Graphics.FillRectangle(backgroundBrush, textX, textY, textSize.Width, textSize.Height);
                            }

                            // 绘制文字
                            using (SolidBrush textBrush = new SolidBrush(color))
                            {
                                e.Graphics.DrawString(label, font, textBrush, textX, textY);
                            }
                        }
                    }
                    else
                    {
                        // 普通检测框：[x, y, w, h]
                        float x = (float)bbox[0];
                        float y = (float)bbox[1];
                        float w = (float)bbox[2];
                        float h = (float)bbox[3];

                        // 处理Mask
                        if (objResult.WithMask && objResult.Mask != null)
                        {
                            using (var maskBitmap = CreateTransparentMaskDirect(objResult.Mask))
                            {
                                e.Graphics.DrawImage(maskBitmap, x, y, w, h);
                            }
                        }

                        // 绘制边界框
                        using (Pen pen = new Pen(color, borderWidth))
                        {
                            e.Graphics.DrawRectangle(pen, x, y, w, h);
                        }

                        // 绘制标签文本
                        string label = $"{categoryName} {score:F2}";
                        using (Font font = new Font("Microsoft YaHei", fontSize))
                        {
                            SizeF textSize = e.Graphics.MeasureString(label, font);
                            float textY = y - textSize.Height - 2;

                            // 绘制半透明黑色背景
                            using (SolidBrush backgroundBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                            {
                                e.Graphics.FillRectangle(backgroundBrush, x, textY, textSize.Width, textSize.Height);
                            }

                            // 绘制文字
                            using (SolidBrush textBrush = new SolidBrush(color))
                            {
                                e.Graphics.DrawString(label, font, textBrush, x, textY);
                            }
                        }
                    }
                }

            }

            // 绘制状态文本
            if (ShowStatusText)
            {
                var originalTransform = e.Graphics.Transform;
                e.Graphics.ResetTransform();
                using (Font font = new Font("微软雅黑", 24))
                using (SolidBrush brush = new SolidBrush(_statusText == "OK" ? Color.Green : Color.Red))
                {
                    e.Graphics.DrawString(_statusText, font, brush, 10, 10);
                }
                e.Graphics.Transform = originalTransform;
            }
        }

        // 操作Mat数据创建透明蒙版
        private unsafe Bitmap CreateTransparentMaskDirect(Mat mask)
        {
            int width = mask.Width;
            int height = mask.Height;

            // 创建目标Bitmap
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData bmpData = result.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                // 直接访问Mat数据（假设mask是8UC1格式）
                byte* maskPtr = (byte*)mask.DataPointer;
                byte* bmpPtr = (byte*)bmpData.Scan0;

                // 预定义颜色值（ARGB格式，半透明绿色）
                const int alpha = 128;
                const int blue = 0;
                const int green = 255;
                const int red = 0;

                // 并行处理每个像素
                Parallel.For(0, height, y =>
                {
                    byte* maskRow = maskPtr + y * mask.Step();
                    byte* bmpRow = bmpPtr + y * bmpData.Stride;

                    for (int x = 0; x < width; x++)
                    {
                        int bmpPos = x * 4;
                        if (maskRow[x] > 0) // 掩码有效区域
                        {
                            bmpRow[bmpPos] = blue;     // B
                            bmpRow[bmpPos + 1] = green; // G
                            bmpRow[bmpPos + 2] = red;   // R
                            bmpRow[bmpPos + 3] = alpha; // A
                        }
                        else // 透明区域
                        {
                            *(int*)(bmpRow + bmpPos) = 0; // 一次性置零
                        }
                    }
                });
            }
            finally
            {
                result.UnlockBits(bmpData);
            }

            return result;
        }

        new public void Update()
        {
            Invalidate();
        }

        // 新增的计算MinScale的方法
        private void CalculateMinScale()
        {
            if (_image == null) return;

            // 计算面板的最短边长度
            float panelMinDimension = Math.Min(this.Width, this.Height);

            // 计算图像的最长边长度
            float imageMaxDimension = Math.Max(_image.Width, _image.Height);

            // 计算MinScale，使得图像能缩小到panel最短边的一半大小
            MinScale = (panelMinDimension / 2) / imageMaxDimension;
        }

        // 当Panel大小改变时重新计算MinScale
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            CalculateMinScale();
        }

        // 添加右键点击事件处理
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Right && _image != null)
            {
                // 计算缩放比例以填充整个面板，与image设置时的逻辑一致
                float panelAspect = (float)this.Width / this.Height;
                float imageAspect = (float)_image.Width / _image.Height;

                if (panelAspect > imageAspect)
                {
                    // 面板更宽，按高度填充
                    _scale = (float)this.Height / _image.Height;
                }
                else
                {
                    // 面板更高，按宽度填充
                    _scale = (float)this.Width / _image.Width;
                }

                // 重新计算图像位置以居中
                _imagePosition.X = (this.Width - _image.Width * _scale) / 2;
                _imagePosition.Y = (this.Height - _image.Height * _scale) / 2;

                // 调整位置确保图像始终可见
                AdjustImagePosition();

                // 重绘
                Invalidate();
            }
        }
    }
}
