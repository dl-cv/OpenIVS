using System;
using OpenCvSharp;

namespace DlcvModules
{
    // 剪切校正、分位投影、反锐化和五阈值条带；只处理一维码裁图。
    internal static class BarcodeEnhancer
    {
        internal static readonly string[] Modes = { "central-p35", "central-p50", "upper-p35", "smooth-white2", "upper-white3" };

        internal static unsafe Mat Enhance(Mat gray, int attempt)
        {
            int w = gray.Cols, h = gray.Rows;
            bool upper = attempt == 2 || attempt == 4, softer = attempt == 3;
            int y0 = (int)(h * (upper ? .05 : softer ? .18 : .22));
            int y1 = Math.Min(h, Math.Max(y0 + 1, (int)(h * (upper ? .55 : softer ? .80 : .78))));
            int bh = y1 - y0, uw = w * 2;
            using (var band = new Mat(gray, new Rect(0, y0, w, bh)))
            using (var up = new Mat())
            using (var profile = new Mat(1, uw, MatType.CV_32FC1))
            using (var smooth = new Mat())
            using (var blur = new Mat())
            {
                byte[] clipped = new byte[bh * w];
                for (int y = 0; y < bh; ++y)
                {
                    byte* row = (byte*)band.Ptr(y);
                    for (int x = 0; x < w; ++x) clipped[y*w+x] = Math.Min(row[x], (byte)200);
                }
                float[] acc = new float[w];
                double best = 0, score = -1;
                for (int i = 0; i < 9; ++i)
                {
                    double shear = -6 + i * 1.5;
                    double energy = Energy(clipped, bh, w, shear, acc);
                    if (energy > score) { score = energy; best = shear; }
                }
                double center = best;
                foreach (double delta in new[] { -.75, -.375, .375, .75 })
                {
                    double shear = center + delta;
                    double energy = Energy(clipped, bh, w, shear, acc);
                    if (energy > score) { score = energy; best = shear; }
                }
                Cv2.Resize(band, up, new Size(uw, bh), 0, 0, InterpolationFlags.Cubic);
                int[] shifts = new int[bh];
                for (int y = 0; y < bh; ++y)
                    shifts[y] = (int)Math.Round((y - (bh - 1)*.5) * (best * 2 / bh));
                int k = (int)Math.Round((attempt == 1 ? .50 : softer ? .45 : .35) * (bh - 1));
                byte[] column = new byte[bh];
                float* prof = (float*)profile.DataPointer;
                for (int x = 0; x < uw; ++x)
                {
                    for (int y = 0; y < bh; ++y)
                        column[y] = ((byte*)up.Ptr(y))[Math.Max(0, Math.Min(uw - 1, x + shifts[y]))];
                    prof[x] = QuickSelectByte(column, 0, bh - 1, k);
                }
                Cv2.GaussianBlur(profile, smooth, new Size(0, 0), softer ? 2.8 : 1.6, 0);
                Cv2.GaussianBlur(smooth, blur, new Size(0, 0), softer ? 9.0 : 7.0, 0);
                float[] sharp = new float[uw];
                float* s = (float*)smooth.DataPointer;
                float* b = (float*)blur.DataPointer;
                for (int x = 0; x < uw; ++x) sharp[x] = s[x] + (softer ? 1.4f : 1.8f) * (s[x] - b[x]);
                float[] sorted = (float[])sharp.Clone();
                Array.Sort(sorted);
                float low = sorted[(int)(.10 * (uw - 1))];
                float span = Math.Max(sorted[(int)(.90 * (uw - 1))] - low, 1f);
                var output = new Mat(268, uw + 160, MatType.CV_8UC1, Scalar.All(255));
                float[] thresholds = softer ? new[] { .22f, .28f, .34f, .40f, .46f } : new[] { .18f, .22f, .26f, .30f, .34f };
                for (int bandIndex = 0; bandIndex < thresholds.Length; ++bandIndex)
                {
                    float threshold = low + thresholds[bandIndex] * span;
                    for (int y = 24 + bandIndex*44; y < 68 + bandIndex*44; ++y)
                    {
                        byte* row = (byte*)output.Ptr(y) + 80;
                        for (int x = 0; x < uw; ++x) row[x] = sharp[x] >= threshold ? (byte)255 : (byte)0;
                    }
                }
                // 黑条膨胀会吞掉窄白空，仅失败预设做小范围水平白色扩张。
                if (attempt >= 3)
                    using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(attempt == 3 ? 2 : 3, 1)))
                        Cv2.Dilate(output, output, kernel);
                return output;
            }
        }

        private static byte QuickSelectByte(byte[] arr, int left, int right, int k)
        {
            while (left < right)
            {
                byte pivot = arr[(left + right) >> 1];
                int i = left, j = right;
                while (i <= j)
                {
                    while (arr[i] < pivot) i++;
                    while (arr[j] > pivot) j--;
                    if (i <= j)
                    {
                        (arr[i], arr[j]) = (arr[j], arr[i]);
                        i++;
                        j--;
                    }
                }
                if (k <= j) right = j;
                else if (k >= i) left = i;
                else return arr[k];
            }
            return arr[k];
        }

        private static double Energy(byte[] clipped, int h, int w, double shear, float[] acc)
        {
            Array.Clear(acc, 0, w);
            for (int y = 0; y < h; ++y)
            {
                int shift = (int)Math.Round((y - (h - 1)*.5) * (shear / h));
                for (int x = 0; x < w; ++x)
                    acc[x] += clipped[y*w + Math.Max(0, Math.Min(w - 1, x + shift))];
            }
            double energy = 0;
            for (int x = 1; x < w; ++x) { double d = acc[x] - acc[x-1]; energy += d*d; }
            return energy;
        }
    }
}
