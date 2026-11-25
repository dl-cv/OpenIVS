using System;
using System.Runtime.InteropServices;
using OpenCvSharp;
using ZXingCpp;

namespace DlcvModules
{
    internal static class DataMatrixRecovery
    {
        internal sealed class Result
        {
            internal string Text;
            internal OpenCvSharp.Point[] Corners;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int VerifyBitmap(IntPtr bitmap, IntPtr context);

        [DllImport("dlcv_datamatrix_native.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "dlcv_datamatrix_read_v1")]
        private static extern int ReadNative(IntPtr gray, int width, int height, int stride,
            int flags, [Out] float[] corners, VerifyBitmap verify, IntPtr context);

        [DllImport("dlcv_datamatrix_native.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "dlcv_datamatrix_read_v2")]
        private static extern int ReadNativeWithOriginal(IntPtr gray, int width, int height, int stride,
            IntPtr original, int originalWidth, int originalHeight, int originalStride,
            float[] roiToOriginal, int flags, [Out] float[] corners, VerifyBitmap verify, IntPtr context);

        internal static Result Recover(Mat gray, bool rotate, bool invert, Mat original = null, float[] roiToOriginal = null)
        {
            if (gray.Rows < 32 || gray.Cols < 32) return null;
            using (var pure = new ZXingCpp.BarcodeReader { Formats = BarcodeFormat.DataMatrix, TryDownscale = false,
                TryRotate = false, TryInvert = false, IsPure = true, TextMode = TextMode.HRI })
            {
                string text = null;
                Exception verifyError = null;
                VerifyBitmap verify = (bitmap, context) =>
                {
                    try
                    {
                        var view = new ImageView(bitmap, 38, 38, ImageFormat.Lum, 38);
                        var codes = pure.From(view) ?? Array.Empty<Barcode>();
                        try
                        {
                            foreach (var code in codes)
                                if (code.IsValid && code.Format == BarcodeFormat.DataMatrix)
                                {
                                    text = code.Text;
                                    return 1;
                                }
                            return 0;
                        }
                        finally { foreach (var code in codes) code.Dispose(); }
                    }
                    catch (Exception error) { verifyError = error; return 0; }
                };
                var corners = new float[8];
                int flags = (rotate ? 1 : 0) | (invert ? 2 : 0);
                int found = original == null ? ReadNative(gray.Data, gray.Cols, gray.Rows, checked((int)gray.Step()),
                    flags, corners, verify, IntPtr.Zero) :
                    ReadNativeWithOriginal(gray.Data, gray.Cols, gray.Rows, checked((int)gray.Step()),
                        original.Data, original.Cols, original.Rows, checked((int)original.Step()),
                        roiToOriginal, flags, corners, verify, IntPtr.Zero);
                GC.KeepAlive(original);
                GC.KeepAlive(verify);
                GC.KeepAlive(gray);
                if (verifyError != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(verifyError).Throw();
                if (found == 0) return null;
                if (found < 0 || text == null) throw new InvalidOperationException("Data Matrix 恢复失败");
                var points = new OpenCvSharp.Point[4];
                for (int i = 0; i < 4; ++i)
                    points[i] = new OpenCvSharp.Point((int)Math.Round(corners[2 * i]), (int)Math.Round(corners[2 * i + 1]));
                return new Result { Text = text, Corners = points };
            }
        }
    }
}
