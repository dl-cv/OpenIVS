using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace DlcvTest.WPFViewer
{
    /// <summary>
    /// Mat 转 BitmapSource 的转换工具（net472/WPF）。
    /// 说明：为了线程安全与生命周期安全，这里使用 WritePixels 进行一次性拷贝，并对结果 Freeze()。
    /// </summary>
    public static class MatBitmapSource
    {
        public static BitmapSource ToBitmapSource(Mat mat, bool freeze = true)
        {
            if (mat == null || mat.Empty()) return null;

            // 仅支持常见格式；其他格式尽量转为 8UC3。
            Mat src = mat;
            PixelFormat pf;
            if (mat.Type() == MatType.CV_8UC1)
            {
                pf = PixelFormats.Gray8;
            }
            else if (mat.Type() == MatType.CV_8UC3)
            {
                pf = PixelFormats.Bgr24;
            }
            else if (mat.Type() == MatType.CV_8UC4)
            {
                pf = PixelFormats.Bgra32;
            }
            else
            {
                // 尽量转换为 8UC3（BGR）。
                src = new Mat();
                try
                {
                    if (mat.Channels() == 1)
                    {
                        mat.ConvertTo(src, MatType.CV_8UC1);
                        pf = PixelFormats.Gray8;
                    }
                    else
                    {
                        mat.ConvertTo(src, MatType.CV_8UC3);
                        pf = PixelFormats.Bgr24;
                    }
                }
                catch
                {
                    src.Dispose();
                    return null;
                }
            }

            try
            {
                int width = src.Cols;
                int height = src.Rows;
                int stride = (int)src.Step();
                int bufferSize = stride * height;

                var wb = new WriteableBitmap(width, height, 96, 96, pf, null);
                wb.WritePixels(new Int32Rect(0, 0, width, height), src.Data, bufferSize, stride);
                if (freeze && wb.CanFreeze) wb.Freeze();
                return wb;
            }
            finally
            {
                if (!ReferenceEquals(src, mat))
                {
                    try { src.Dispose(); } catch { }
                }
            }
        }
    }
}


