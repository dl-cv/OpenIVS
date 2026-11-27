using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DlcvModules
{

/// <summary>
/// 纯 C# 高性能工业视觉底层算子库（零原生 C++ DLL 依赖，跨 Windows/Linux 开箱即用）：
/// 精确对齐 OpenCV (cv2) 的图像缩放 (INTER_AREA / INTER_LINEAR)、SIMD 预填充边界高斯滤波 (Reflect101)、
/// O(1) 游标中值滤波、增量式椭圆闭运算、8 连通域分析、最小外接旋转矩形 (Rotating Calipers)、
/// 最小外接圆、透视/仿射重采样与正交 8-DOF 亚像素时钟锁定。
/// </summary>
public static class FastVisionOps
{
    public readonly struct ComponentInfo
    {
        public int Label { get; }
        public int Area { get; }
        public int MinX { get; }
        public int MinY { get; }
        public int MaxX { get; }
        public int MaxY { get; }
        public List<(float X, float Y)> BoundaryPoints { get; }

        public ComponentInfo(int label, int area, int minX, int minY, int maxX, int maxY,
            List<(float X, float Y)> boundaryPoints)
        {
            Label = label; Area = area; MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            BoundaryPoints = boundaryPoints;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ClampIdx(int idx, int len) => idx < 0 ? 0 : (idx >= len ? len - 1 : idx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Reflect101(int idx, int len)
    {
        if (len <= 1) return 0;
        while (idx < 0 || idx >= len)
        {
            if (idx < 0) idx = -idx;
            if (idx >= len) idx = 2 * len - 2 - idx;
        }
        return idx;
    }

    public static float ComputeMedian(ReadOnlySpan<byte> values) =>
        ComputePercentileU8(values, 0.50);

    public static float ComputePercentileU8(ReadOnlySpan<byte> values, double q)
    {
        if (values.Length == 0) return 0f;
        Span<int> hist = stackalloc int[256];
        for (int i = 0; i < values.Length; i++) hist[values[i]]++;
        double pos = q * (values.Length - 1);
        int idxLo = (int)Math.Floor(pos);
        int idxHi = Math.Min(values.Length - 1, idxLo + 1);
        double frac = pos - idxLo;
        int acc = 0, vLo = -1, vHi = -1;
        for (int v = 0; v < 256; v++)
        {
            acc += hist[v];
            if (vLo < 0 && acc > idxLo) vLo = v;
            if (vHi < 0 && acc > idxHi) { vHi = v; break; }
        }
        if (vLo < 0) vLo = 255;
        if (vHi < 0) vHi = vLo;
        return (float)(vLo + (vHi - vLo) * frac);
    }


    public static (float P16, float P84) ComputePercentiles16And84(List<float> vals)
    {
        if (vals.Count == 0) return (0f, 1f);
        vals.Sort();
        return (InterpPercentile(vals, 0.16), InterpPercentile(vals, 0.84));
    }

    private static float InterpPercentile(List<float> sorted, double q)
    {
        if (sorted.Count == 1) return sorted[0];
        double pos = q * (sorted.Count - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(sorted.Count - 1, lo + 1);
        double frac = pos - lo;
        return (float)(sorted[lo] * (1.0 - frac) + sorted[hi] * frac);
    }

    /// <summary>
    /// 8-bit 图像 O(1) 游标滑动直方图中值滤波（BORDER_REPLICATE，精确匹配 cv2.medianBlur）。
    /// </summary>
    public static byte[] MedianBlurU8(byte[] src, int w, int h, int ksize)
    {
        int rad = ksize / 2;
        byte[] dst = new byte[w * h];
        int halfCount = (ksize * ksize) / 2;

        if (ksize == 3)
        {
            Span<byte> win = stackalloc byte[9];
            for (int y = 0; y < h; y++)
            {
                int y0 = ClampIdx(y - 1, h) * w;
                int y1 = y * w;
                int y2 = ClampIdx(y + 1, h) * w;
                for (int x = 0; x < w; x++)
                {
                    int x0 = ClampIdx(x - 1, w), x1 = x, x2 = ClampIdx(x + 1, w);
                    win[0] = src[y0 + x0]; win[1] = src[y0 + x1]; win[2] = src[y0 + x2];
                    win[3] = src[y1 + x0]; win[4] = src[y1 + x1]; win[5] = src[y1 + x2];
                    win[6] = src[y2 + x0]; win[7] = src[y2 + x1]; win[8] = src[y2 + x2];
                    for (int i = 1; i < 9; i++)
                    {
                        byte key = win[i];
                        int j = i - 1;
                        while (j >= 0 && win[j] > key) { win[j + 1] = win[j]; j--; }
                        win[j + 1] = key;
                    }
                    dst[y1 + x] = win[4];
                }
            }
            return dst;
        }

        Span<int> hist = stackalloc int[256];
        for (int y = 0; y < h; y++)
        {
            hist.Clear();
            for (int dy = -rad; dy <= rad; dy++)
            {
                int ry = ClampIdx(y + dy, h) * w;
                for (int dx = -rad; dx <= rad; dx++)
                    hist[src[ry + ClampIdx(dx, w)]]++;
            }

            int med = 0, ltCount = 0;
            while (ltCount + hist[med] <= halfCount)
            {
                ltCount += hist[med];
                med++;
            }
            dst[y * w] = (byte)med;

            for (int x = 1; x < w; x++)
            {
                int xRem = ClampIdx(x - 1 - rad, w);
                int xAdd = ClampIdx(x + rad, w);
                if (xRem != xAdd)
                {
                    for (int dy = -rad; dy <= rad; dy++)
                    {
                        int ry = ClampIdx(y + dy, h) * w;
                        byte vRem = src[ry + xRem];
                        byte vAdd = src[ry + xAdd];
                        hist[vRem]--;
                        if (vRem < med) ltCount--;
                        hist[vAdd]++;
                        if (vAdd < med) ltCount++;
                    }
                    while (ltCount > halfCount)
                    {
                        med--;
                        ltCount -= hist[med];
                    }
                    while (ltCount + hist[med] <= halfCount)
                    {
                        ltCount += hist[med];
                        med++;
                    }
                }
                dst[y * w + x] = (byte)med;
            }
        }
        return dst;
    }

    /// <summary>
    /// 预填充边界 + SIMD 向量化可分离高斯滤波（BORDER_REFLECT_101，精确对齐 cv2.GaussianBlur）。
    /// </summary>
    public static unsafe float[] GaussianBlurF32(float[] src, int w, int h, double sigma, int explicitKsize = 0)
    {
        if (sigma <= 0 && explicitKsize > 0)
            sigma = 0.3 * ((explicitKsize - 1) * 0.5 - 1.0) + 0.8;
        if (sigma <= 1e-5)
        {
            float[] copy = new float[src.Length];
            Array.Copy(src, copy, src.Length);
            return copy;
        }

        int rad = explicitKsize > 0 ? explicitKsize / 2 : Math.Max(1, (int)Math.Round(sigma * 4.0));
        int klen = 2 * rad + 1;
        float[] kernel = new float[klen];
        double inv2s2 = 1.0 / (2.0 * sigma * sigma);
        double sum = 0.0;
        for (int i = -rad; i <= rad; i++)
        {
            double v = Math.Exp(-(i * i) * inv2s2);
            kernel[i + rad] = (float)v;
            sum += v;
        }
        float invSum = (float)(1.0 / sum);
        for (int i = 0; i < klen; i++) kernel[i] *= invSum;

        int padW = w + 2 * rad;
        int[] mapX = new int[padW];
        for (int i = 0; i < padW; i++) mapX[i] = Reflect101(i - rad, w);

        int[] mapY = new int[h + 2 * rad];
        for (int i = 0; i < mapY.Length; i++) mapY[i] = Reflect101(i - rad, h) * w;

        float[] tmp = new float[w * h];
        float[] dst = new float[w * h];
        int vecLen = Vector<float>.Count;

        if (w * h * klen >= 600_000)
        {
            Parallel.For(0, h, y =>
            {
                float[] padRow = new float[padW];
                int rOff = y * w;
                for (int i = 0; i < padW; i++) padRow[i] = src[rOff + mapX[i]];
                for (int x = 0; x < w; x++)
                {
                    float acc = 0f;
                    int k = 0;
                    for (; k <= klen - vecLen; k += vecLen)
                        acc += Vector.Dot(new Vector<float>(padRow, x + k), new Vector<float>(kernel, k));
                    for (; k < klen; k++)
                        acc += padRow[x + k] * kernel[k];
                    tmp[rOff + x] = acc;
                }
            });

            Parallel.For(0, h, y =>
            {
                int rOff = y * w;
                for (int k = 0; k < klen; k++)
                {
                    float kw = kernel[k];
                    int sOff = mapY[y + k];
                    int x = 0;
                    var vKw = new Vector<float>(kw);
                    for (; x <= w - vecLen; x += vecLen)
                    {
                        var vAcc = new Vector<float>(dst, rOff + x) + new Vector<float>(tmp, sOff + x) * vKw;
                        vAcc.CopyTo(dst, rOff + x);
                    }
                    for (; x < w; x++)
                        dst[rOff + x] += tmp[sOff + x] * kw;
                }
            });
        }
        else
        {
            float[] padRow = new float[padW];
            fixed (float* pSrc = src, pTmp = tmp, pDst = dst, pKer = kernel, pPad = padRow)
            {
                for (int y = 0; y < h; y++)
                {
                    int rOff = y * w;
                    for (int i = 0; i < padW; i++) pPad[i] = pSrc[rOff + mapX[i]];
                    for (int x = 0; x < w; x++)
                    {
                        float acc = 0f;
                        int k = 0;
                        for (; k <= klen - vecLen; k += vecLen)
                            acc += Vector.Dot(new Vector<float>(padRow, x + k), new Vector<float>(kernel, k));
                        for (; k < klen; k++)
                            acc += pPad[x + k] * pKer[k];
                        pTmp[rOff + x] = acc;
                    }
                }
                for (int y = 0; y < h; y++)
                {
                    int rOff = y * w;
                    for (int k = 0; k < klen; k++)
                    {
                        float kw = pKer[k];
                        int sOff = mapY[y + k];
                        int x = 0;
                        var vKw = new Vector<float>(kw);
                        for (; x <= w - vecLen; x += vecLen)
                        {
                            var vAcc = new Vector<float>(dst, rOff + x) + new Vector<float>(tmp, sOff + x) * vKw;
                            vAcc.CopyTo(dst, rOff + x);
                        }
                        for (; x < w; x++)
                            pDst[rOff + x] += pTmp[sOff + x] * kw;
                    }
                }
            }
        }
        return dst;
    }

    public static byte[] GaussianBlurU8(byte[] src, int w, int h, int ksize)
    {
        float[] f = new float[src.Length];
        for (int i = 0; i < src.Length; i++) f[i] = src[i];
        float[] bf = GaussianBlurF32(f, w, h, 0.0, ksize);
        byte[] dst = new byte[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            int v = (int)DpmMathF.Round(bf[i]);
            dst[i] = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
        }
        return dst;
    }

    public static float[] ResizeAreaF32(float[] src, int srcW, int srcH, int dstW, int dstH)
    {
        float[] dst = new float[dstW * dstH];
        double scaleX = (double)srcW / dstW;
        double scaleY = (double)srcH / dstH;
        double invArea = 1.0 / (scaleX * scaleY);

        for (int dy = 0; dy < dstH; dy++)
        {
            double sy0 = dy * scaleY, sy1 = (dy + 1) * scaleY;
            int iy0 = (int)Math.Floor(sy0), iy1 = Math.Min(srcH, (int)Math.Ceiling(sy1));
            for (int dx = 0; dx < dstW; dx++)
            {
                double sx0 = dx * scaleX, sx1 = (dx + 1) * scaleX;
                int ix0 = (int)Math.Floor(sx0), ix1 = Math.Min(srcW, (int)Math.Ceiling(sx1));
                double sum = 0.0;
                for (int sy = iy0; sy < iy1; sy++)
                {
                    double wy = Math.Min(sy + 1.0, sy1) - Math.Max((double)sy, sy0);
                    int rOff = sy * srcW;
                    double rowSum = 0.0;
                    for (int sx = ix0; sx < ix1; sx++)
                    {
                        double wx = Math.Min(sx + 1.0, sx1) - Math.Max((double)sx, sx0);
                        rowSum += src[rOff + sx] * wx;
                    }
                    sum += rowSum * wy;
                }
                dst[dy * dstW + dx] = (float)(sum * invArea);
            }
        }
        return dst;
    }

    public static byte[] ResizeAreaU8(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        byte[] dst = new byte[dstW * dstH];
        double scaleX = (double)srcW / dstW;
        double scaleY = (double)srcH / dstH;
        double invArea = 1.0 / (scaleX * scaleY);

        Parallel.For(0, dstH, dy =>
        {
            double sy0 = dy * scaleY, sy1 = (dy + 1) * scaleY;
            int iy0 = (int)Math.Floor(sy0), iy1 = Math.Min(srcH, (int)Math.Ceiling(sy1));
            int dOff = dy * dstW;
            for (int dx = 0; dx < dstW; dx++)
            {
                double sx0 = dx * scaleX, sx1 = (dx + 1) * scaleX;
                int ix0 = (int)Math.Floor(sx0), ix1 = Math.Min(srcW, (int)Math.Ceiling(sx1));
                double sum = 0.0;
                for (int sy = iy0; sy < iy1; sy++)
                {
                    double wy = Math.Min(sy + 1.0, sy1) - Math.Max((double)sy, sy0);
                    int rOff = sy * srcW;
                    double rowSum = 0.0;
                    for (int sx = ix0; sx < ix1; sx++)
                    {
                        double wx = Math.Min(sx + 1.0, sx1) - Math.Max((double)sx, sx0);
                        rowSum += src[rOff + sx] * wx;
                    }
                    sum += rowSum * wy;
                }
                int v = (int)Math.Round(sum * invArea);
                dst[dOff + dx] = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
            }
        });
        return dst;
    }

    public static float[] ResizeBilinearF32(float[] src, int srcW, int srcH, int dstW, int dstH)
    {
        float[] dst = new float[dstW * dstH];
        float scaleX = (float)srcW / dstW;
        float scaleY = (float)srcH / dstH;

        for (int dy = 0; dy < dstH; dy++)
        {
            float sy = (dy + 0.5f) * scaleY - 0.5f;
            if (sy < 0f) sy = 0f;
            if (sy > srcH - 1) sy = srcH - 1;
            int iy0 = (int)DpmMathF.Floor(sy);
            int iy1 = Math.Min(srcH - 1, iy0 + 1);
            float fy = sy - iy0;

            int r0 = iy0 * srcW, r1 = iy1 * srcW, dOff = dy * dstW;
            for (int dx = 0; dx < dstW; dx++)
            {
                float sx = (dx + 0.5f) * scaleX - 0.5f;
                if (sx < 0f) sx = 0f;
                if (sx > srcW - 1) sx = srcW - 1;
                int ix0 = (int)DpmMathF.Floor(sx);
                int ix1 = Math.Min(srcW - 1, ix0 + 1);
                float fx = sx - ix0;

                float v0 = src[r0 + ix0] + fx * (src[r0 + ix1] - src[r0 + ix0]);
                float v1 = src[r1 + ix0] + fx * (src[r1 + ix1] - src[r1 + ix0]);
                dst[dOff + dx] = v0 + fy * (v1 - v0);
            }
        }
        return dst;
    }

    private static int[] BuildEllipseRadii(int diameter)
    {
        int r = diameter / 2;
        int[] js = new int[diameter];
        for (int dy = -r; dy <= r; dy++)
            js[dy + r] = (int)Math.Round(r * Math.Sqrt(Math.Max(0.0, 1.0 - (double)(dy * dy) / (r * r))));
        return js;
    }

    /// <summary>
    /// 增量式椭圆结构元素浮点形态学闭运算（每级半径 O(1) 递推，精确等价于 cv2.morphologyEx(MORPH_CLOSE, MORPH_ELLIPSE)）。
    /// </summary>
    public static float[] MorphCloseEllipseF32(float[] src, int w, int h, int diameter)
    {
        int[] radii = BuildEllipseRadii(diameter);
        float[] dil = MorphFilterEllipseF32(src, w, h, diameter, radii, isDilate: true);
        return MorphFilterEllipseF32(dil, w, h, diameter, radii, isDilate: false);
    }

    private static float[] MorphFilterEllipseF32(float[] src, int w, int h, int diameter, int[] radii, bool isDilate)
    {
        int r = diameter / 2;
        float[][] hSpan = new float[r + 1][];
        hSpan[0] = src;

        // O(1) incremental recurrence across radii 1..r
        for (int rad = 1; rad <= r; rad++)
        {
            float[] prev = hSpan[rad - 1];
            float[] cur = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                int rOff = y * w;
                for (int x = 0; x < w; x++)
                {
                    float vPrev = prev[rOff + x];
                    float vL = src[rOff + Math.Max(0, x - rad)];
                    float vR = src[rOff + Math.Min(w - 1, x + rad)];
                    if (isDilate)
                    {
                        float m = vL > vR ? vL : vR;
                        cur[rOff + x] = vPrev > m ? vPrev : m;
                    }
                    else
                    {
                        float m = vL < vR ? vL : vR;
                        cur[rOff + x] = vPrev < m ? vPrev : m;
                    }
                }
            }
            hSpan[rad] = cur;
        }

        float[] dst = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            int dOff = y * w;
            for (int x = 0; x < w; x++)
            {
                float best = isDilate ? float.NegativeInfinity : float.PositiveInfinity;
                for (int dy = -r; dy <= r; dy++)
                {
                    int yy = ClampIdx(y + dy, h);
                    float v = hSpan[radii[dy + r]][yy * w + x];
                    if (isDilate ? (v > best) : (v < best)) best = v;
                }
                dst[dOff + x] = best;
            }
        }
        return dst;
    }

    public static byte[] MorphOpenEllipseU8(byte[] srcBin, int w, int h, int diameter)
    {
        int[] radii = BuildEllipseRadii(diameter);
        byte[] ero = MorphFilterEllipseU8(srcBin, w, h, diameter, radii, isDilate: false);
        return MorphFilterEllipseU8(ero, w, h, diameter, radii, isDilate: true);
    }

    private static byte[] MorphFilterEllipseU8(byte[] srcBin, int w, int h, int diameter, int[] radii, bool isDilate)
    {
        int r = diameter / 2;
        byte[] dst = new byte[w * h];
        Parallel.For(0, h, y =>
        {
            int dOff = y * w;
            for (int x = 0; x < w; x++)
            {
                byte res = isDilate ? (byte)0 : (byte)255;
                bool done = false;
                for (int dy = -r; dy <= r && !done; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= h) continue;
                    int rx = radii[dy + r];
                    int x0 = Math.Max(0, x - rx), x1 = Math.Min(w - 1, x + rx);
                    int rOff = yy * w;
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        byte v = srcBin[rOff + xx];
                        if (isDilate && v != 0) { res = 255; done = true; break; }
                        if (!isDilate && v == 0) { res = 0; done = true; break; }
                    }
                }
                dst[dOff + x] = res;
            }
        });
        return dst;
    }

    public static byte[] MorphCloseRectU8(byte[] srcBin, int w, int h, int kw, int kh)
    {
        byte[] dil = MorphRectU8(srcBin, w, h, kw, kh, isDilate: true);
        return MorphRectU8(dil, w, h, kw, kh, isDilate: false);
    }

    public static byte[] DilateRect3x3(byte[] srcBin, int w, int h) =>
        MorphRectU8(srcBin, w, h, 3, 3, isDilate: true);

    private static byte[] MorphRectU8(byte[] srcBin, int w, int h, int kw, int kh, bool isDilate)
    {
        int rx = kw / 2, ry = kh / 2;
        byte[] dst = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int y0 = Math.Max(0, y - ry), y1 = Math.Min(h - 1, y + ry);
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - rx), x1 = Math.Min(w - 1, x + rx);
                byte res = isDilate ? (byte)0 : (byte)255;
                bool done = false;
                for (int yy = y0; yy <= y1 && !done; yy++)
                {
                    int rOff = yy * w;
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        byte v = srcBin[rOff + xx];
                        if (isDilate && v != 0) { res = 255; done = true; break; }
                        if (!isDilate && v == 0) { res = 0; done = true; break; }
                    }
                }
                dst[y * w + x] = res;
            }
        }
        return dst;
    }

    public static double ComputeOtsuThreshold(byte[] src)
    {
        Span<int> hist = stackalloc int[256];
        for (int i = 0; i < src.Length; i++) hist[src[i]]++;
        double invN = 1.0 / src.Length;
        double muTotal = 0.0;
        for (int i = 0; i < 256; i++) muTotal += i * (hist[i] * invN);

        double q1 = 0.0, sum1 = 0.0, maxSigma = -1.0;
        int bestThresh = 0;
        for (int t = 0; t < 256; t++)
        {
            double p = hist[t] * invN;
            q1 += p;
            sum1 += t * p;
            double q2 = 1.0 - q1;
            if (q1 < 1e-9 || q2 < 1e-9) continue;
            double mu1 = sum1 / q1;
            double mu2 = (muTotal - sum1) / q2;
            double diff = mu1 - mu2;
            double sigma = q1 * q2 * diff * diff;
            if (sigma > maxSigma) { maxSigma = sigma; bestThresh = t; }
        }
        return bestThresh;
    }

    public static (int[] Labels, List<ComponentInfo> Components) ConnectedComponents8(byte[] mask, int w, int h, bool collectBoundary)
    {
        int[] labels = new int[w * h];
        List<ComponentInfo> comps = new List<ComponentInfo>();
        int[] queue = new int[w * h];
        int curLabel = 0;

        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0 || labels[i] != 0) continue;
            curLabel++;
            int head = 0, tail = 0;
            queue[tail++] = i;
            labels[i] = curLabel;

            int minX = i % w, maxX = minX, minY = i / w, maxY = minY;
            List<(float X, float Y)> boundary = collectBoundary ? new List<(float X, float Y)>(256) : new List<(float X, float Y)>(0);

            while (head < tail)
            {
                int cur = queue[head++];
                int cx = cur % w, cy = cur / w;
                if (cx < minX) minX = cx;
                if (cx > maxX) maxX = cx;
                if (cy < minY) minY = cy;
                if (cy > maxY) maxY = cy;

                bool isBoundary = cx == 0 || cy == 0 || cx == w - 1 || cy == h - 1;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cy + dy;
                    if (ny < 0 || ny >= h) continue;
                    int rOff = ny * w;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx;
                        if (nx < 0 || nx >= w) continue;
                        int nIdx = rOff + nx;
                        if (mask[nIdx] == 0)
                        {
                            if (dx == 0 || dy == 0) isBoundary = true;
                        }
                        else if (labels[nIdx] == 0)
                        {
                            labels[nIdx] = curLabel;
                            queue[tail++] = nIdx;
                        }
                    }
                }
                if (collectBoundary && isBoundary)
                    boundary.Add((cx, cy));
            }
            comps.Add(new ComponentInfo(curLabel, tail, minX, minY, maxX, maxY, boundary));
        }
        return (labels, comps);
    }

    public static List<(float X, float Y)> ConvexHull(List<(float X, float Y)> pts)
    {
        if (pts.Count <= 2) return new List<(float X, float Y)>(pts);
        var sorted = new List<(float X, float Y)>(pts);
        sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

        double Cross((float X, float Y) o, (float X, float Y) a, (float X, float Y) b) =>
            ((double)a.X - o.X) * ((double)b.Y - o.Y) - ((double)a.Y - o.Y) * ((double)b.X - o.X);

        var lower = new List<(float X, float Y)>(sorted.Count);
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        var upper = new List<(float X, float Y)>(sorted.Count);
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    public static (float X, float Y)[] MinAreaRectBox(List<(float X, float Y)> pts)
    {
        var hull = ConvexHull(pts);
        int n = hull.Count;
        if (n == 0) return new (float, float)[4];
        if (n == 1)
        {
            var p = hull[0];
            return new[] { p, p, p, p };
        }

        double bestArea = double.PositiveInfinity;
        double bestUx = 1, bestUy = 0, bestMinU = 0, bestMaxU = 0, bestMinV = 0, bestMaxV = 0;

        for (int i = 0; i < n; i++)
        {
            var p0 = hull[i];
            var p1 = hull[(i + 1) % n];
            double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;
            double ux = dx / len, uy = dy / len;
            double vx = -uy, vy = ux;

            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
            for (int j = 0; j < n; j++)
            {
                double u = hull[j].X * ux + hull[j].Y * uy;
                double v = hull[j].X * vx + hull[j].Y * vy;
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }
            double area = (maxU - minU) * (maxV - minV);
            if (area < bestArea)
            {
                bestArea = area;
                bestUx = ux; bestUy = uy;
                bestMinU = minU; bestMaxU = maxU;
                bestMinV = minV; bestMaxV = maxV;
            }
        }

        double bvx = -bestUy, bvy = bestUx;
        (float X, float Y) Pt(double u, double v) =>
            ((float)(u * bestUx + v * bvx), (float)(u * bestUy + v * bvy));

        return new[]
        {
            Pt(bestMinU, bestMinV),
            Pt(bestMaxU, bestMinV),
            Pt(bestMaxU, bestMaxV),
            Pt(bestMinU, bestMaxV)
        };
    }

    public static (float Cx, float Cy, float Radius) MinEnclosingCircle(List<(float X, float Y)> pts)
    {
        var hull = ConvexHull(pts);
        int n = hull.Count;
        if (n == 0) return (0f, 0f, 0f);
        if (n == 1) return (hull[0].X, hull[0].Y, 0f);

        double bestR2 = double.PositiveInfinity, bestCx = 0, bestCy = 0;

        bool AllInside(double cx, double cy, double r2)
        {
            double tol = r2 + 1e-5;
            for (int k = 0; k < n; k++)
            {
                double dx = hull[k].X - cx, dy = hull[k].Y - cy;
                if (dx * dx + dy * dy > tol) return false;
            }
            return true;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double cx = 0.5 * (hull[i].X + hull[j].X);
                double cy = 0.5 * (hull[i].Y + hull[j].Y);
                double dx = hull[i].X - cx, dy = hull[i].Y - cy;
                double r2 = dx * dx + dy * dy;
                if (r2 < bestR2 && AllInside(cx, cy, r2))
                {
                    bestR2 = r2; bestCx = cx; bestCy = cy;
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            double ax = hull[i].X, ay = hull[i].Y;
            for (int j = i + 1; j < n; j++)
            {
                double bx = hull[j].X, by = hull[j].Y;
                for (int k = j + 1; k < n; k++)
                {
                    double cxp = hull[k].X, cyp = hull[k].Y;
                    double d = 2.0 * (ax * (by - cyp) + bx * (cyp - ay) + cxp * (ay - by));
                    if (Math.Abs(d) < 1e-9) continue;
                    double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cxp * cxp + cyp * cyp;
                    double ux = (a2 * (by - cyp) + b2 * (cyp - ay) + c2 * (ay - by)) / d;
                    double uy = (a2 * (cxp - bx) + b2 * (ax - cxp) + c2 * (bx - ax)) / d;
                    double r2 = (ax - ux) * (ax - ux) + (ay - uy) * (ay - uy);
                    if (r2 < bestR2 && AllInside(ux, uy, r2))
                    {
                        bestR2 = r2; bestCx = ux; bestCy = uy;
                    }
                }
            }
        }
        return ((float)bestCx, (float)bestCy, (float)Math.Sqrt(bestR2));
    }

    public static bool SolveHomography4Pts(ReadOnlySpan<float> src, ReadOnlySpan<float> dst, Span<double> H)
    {
        Span<double> A = stackalloc double[72];
        for (int i = 0; i < 4; i++)
        {
            double sx = src[2 * i], sy = src[2 * i + 1];
            double dx = dst[2 * i], dy = dst[2 * i + 1];
            int r0 = (2 * i) * 9, r1 = (2 * i + 1) * 9;
            A[r0 + 0] = sx; A[r0 + 1] = sy; A[r0 + 2] = 1.0;
            A[r0 + 6] = -sx * dx; A[r0 + 7] = -sy * dx; A[r0 + 8] = dx;

            A[r1 + 3] = sx; A[r1 + 4] = sy; A[r1 + 5] = 1.0;
            A[r1 + 6] = -sx * dy; A[r1 + 7] = -sy * dy; A[r1 + 8] = dy;
        }

        for (int col = 0; col < 8; col++)
        {
            int piv = col;
            double maxV = Math.Abs(A[col * 9 + col]);
            for (int row = col + 1; row < 8; row++)
            {
                double v = Math.Abs(A[row * 9 + col]);
                if (v > maxV) { maxV = v; piv = row; }
            }
            if (maxV < 1e-12) return false;
            if (piv != col)
            {
                for (int k = col; k <= 8; k++)
                    (A[col * 9 + k], A[piv * 9 + k]) = (A[piv * 9 + k], A[col * 9 + k]);
            }
            double invP = 1.0 / A[col * 9 + col];
            for (int row = col + 1; row < 8; row++)
            {
                double f = A[row * 9 + col] * invP;
                for (int k = col + 1; k <= 8; k++)
                    A[row * 9 + k] -= f * A[col * 9 + k];
            }
        }

        for (int row = 7; row >= 0; row--)
        {
            double s = A[row * 9 + 8];
            for (int k = row + 1; k < 8; k++) s -= A[row * 9 + k] * H[k];
            H[row] = s / A[row * 9 + row];
        }
        H[8] = 1.0;
        return true;
    }

    public static bool Invert3x3(ReadOnlySpan<double> m, Span<double> inv)
    {
        double a = m[0], b = m[1], c = m[2];
        double d = m[3], e = m[4], f = m[5];
        double g = m[6], h = m[7], i = m[8];

        double A = e * i - f * h;
        double B = -(d * i - f * g);
        double C = d * h - e * g;
        double det = a * A + b * B + c * C;
        if (Math.Abs(det) < 1e-15) return false;
        double invDet = 1.0 / det;

        inv[0] = A * invDet;
        inv[1] = -(b * i - c * h) * invDet;
        inv[2] = (b * f - c * e) * invDet;
        inv[3] = B * invDet;
        inv[4] = (a * i - c * g) * invDet;
        inv[5] = -(a * f - c * d) * invDet;
        inv[6] = C * invDet;
        inv[7] = -(a * h - b * g) * invDet;
        inv[8] = (a * e - b * d) * invDet;
        return true;
    }

    public static void PerspectiveTransformPts(ReadOnlySpan<float> pts, ReadOnlySpan<double> H, Span<float> dstPts)
    {
        int count = pts.Length / 2;
        for (int i = 0; i < count; i++)
        {
            double x = pts[2 * i], y = pts[2 * i + 1];
            double den = H[6] * x + H[7] * y + H[8];
            dstPts[2 * i] = (float)((H[0] * x + H[1] * y + H[2]) / den);
            dstPts[2 * i + 1] = (float)((H[3] * x + H[4] * y + H[5]) / den);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SampleBilinearZero(float[] img, int w, int h, float x, float y)
    {
        if (x < 0f || y < 0f || x >= w - 1 || y >= h - 1) return 0f;
        int ix = (int)x, iy = (int)y;
        float fx = x - ix, fy = y - iy;
        int r0 = iy * w + ix, r1 = r0 + w;
        float v0 = img[r0] + fx * (img[r0 + 1] - img[r0]);
        float v1 = img[r1] + fx * (img[r1 + 1] - img[r1]);
        return v0 + fy * (v1 - v0);
    }

    public static float[] WarpPerspectiveF32(float[] src, int srcW, int srcH, ReadOnlySpan<double> H, int dstW, int dstH)
    {
        float[] dst = new float[dstW * dstH];
        Span<double> invH = stackalloc double[9];
        if (!Invert3x3(H, invH)) return dst;

        for (int dy = 0; dy < dstH; dy++)
        {
            int dOff = dy * dstW;
            for (int dx = 0; dx < dstW; dx++)
            {
                double den = invH[6] * dx + invH[7] * dy + invH[8];
                float sx = (float)((invH[0] * dx + invH[1] * dy + invH[2]) / den);
                float sy = (float)((invH[3] * dx + invH[4] * dy + invH[5]) / den);
                dst[dOff + dx] = SampleBilinearZero(src, srcW, srcH, sx, sy);
            }
        }
        return dst;
    }

    public static float[] RemapGrid32(float[] src, int w, int h, ReadOnlySpan<float> gridPtsXY)
    {
        float[] dst = new float[1024];
        for (int i = 0; i < 1024; i++)
            dst[i] = SampleBilinearZero(src, w, h, gridPtsXY[2 * i], gridPtsXY[2 * i + 1]);
        return dst;
    }

    public static float[] Rot90Ccw(float[] m, int w, int k)
    {
        k = ((k % 4) + 4) % 4;
        if (k == 0) return m;
        float[] cur = m;
        for (int step = 0; step < k; step++)
        {
            float[] next = new float[w * w];
            for (int r = 0; r < w; r++)
                for (int c = 0; c < w; c++)
                    next[(w - 1 - c) * w + r] = cur[r * w + c];
            cur = next;
        }
        return cur;
    }

    public static double[] GetRotationMatrix2D(double cx, double cy, double angleDeg, double scale)
    {
        double rad = angleDeg * Math.PI / 180.0;
        double alpha = scale * Math.Cos(rad);
        double beta = scale * Math.Sin(rad);
        return new[]
        {
            alpha, beta, (1.0 - alpha) * cx - beta * cy,
            -beta, alpha, beta * cx + (1.0 - alpha) * cy
        };
    }

    public static double[] InvertAffine2x3(double[] m)
    {
        double det = m[0] * m[4] - m[1] * m[3];
        double invDet = Math.Abs(det) > 1e-15 ? 1.0 / det : 0.0;
        double i00 = m[4] * invDet, i01 = -m[1] * invDet;
        double i10 = -m[3] * invDet, i11 = m[0] * invDet;
        double i02 = -(i00 * m[2] + i01 * m[5]);
        double i12 = -(i10 * m[2] + i11 * m[5]);
        return new[] { i00, i01, i02, i10, i11, i12 };
    }

    public static byte[] WarpAffineReplicateU8(byte[] src, int srcW, int srcH, double[] mForward, int dstW, int dstH)
    {
        double[] inv = InvertAffine2x3(mForward);
        byte[] dst = new byte[dstW * dstH];

        for (int dy = 0; dy < dstH; dy++)
        {
            int dOff = dy * dstW;
            for (int dx = 0; dx < dstW; dx++)
            {
                double sx = inv[0] * dx + inv[1] * dy + inv[2];
                double sy = inv[3] * dx + inv[4] * dy + inv[5];

                int ix0 = (int)Math.Floor(sx), iy0 = (int)Math.Floor(sy);
                double fx = sx - ix0, fy = sy - iy0;

                int x0 = ClampIdx(ix0, srcW), x1 = ClampIdx(ix0 + 1, srcW);
                int y0 = ClampIdx(iy0, srcH) * srcW, y1 = ClampIdx(iy0 + 1, srcH) * srcW;

                double v0 = src[y0 + x0] + fx * (src[y0 + x1] - src[y0 + x0]);
                double v1 = src[y1 + x0] + fx * (src[y1 + x1] - src[y1 + x0]);
                int v = (int)Math.Round(v0 + fy * (v1 - v0));
                dst[dOff + dx] = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
            }
        }
        return dst;
    }

    // =========================================================================
    // 正交 8-DOF 边位置/边倾角亚像素时钟锁定优化器
    // =========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EdgeToCorners(ReadOnlySpan<float> e, Span<float> c)
    {
        c[0] = e[0] + 0.5f * e[4]; c[1] = e[2] - 0.5f * e[6];
        c[2] = e[1] + 0.5f * e[5]; c[3] = e[2] + 0.5f * e[6];
        c[4] = e[1] - 0.5f * e[5]; c[5] = e[3] + 0.5f * e[7];
        c[6] = e[0] - 0.5f * e[4]; c[7] = e[3] - 0.5f * e[7];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CornersToEdge(ReadOnlySpan<float> c, Span<float> e)
    {
        e[0] = 0.5f * (c[0] + c[6]);
        e[1] = 0.5f * (c[2] + c[4]);
        e[2] = 0.5f * (c[1] + c[3]);
        e[3] = 0.5f * (c[7] + c[5]);
        e[4] = c[0] - c[6];
        e[5] = c[2] - c[4];
        e[6] = c[3] - c[1];
        e[7] = c[5] - c[7];
    }

    private static unsafe double EvalLossEdge(float[] filtClk, int h, int w, int nMod, ReadOnlySpan<float> srcC, ReadOnlySpan<float> e)
    {
        Span<float> dst = stackalloc float[8];
        EdgeToCorners(e, dst);
        Span<double> H = stackalloc double[9];
        if (!SolveHomography4Pts(srcC, dst, H)) return 1e9;

        int extN = nMod + 2;
        Span<float> ve = stackalloc float[36 * 36];
        fixed (float* pImg = filtClk)
        {
            for (int r = -1; r <= nMod; r++)
            {
                int rOff = (r + 1) * extN;
                double h0r = H[1] * r + H[2], h3r = H[4] * r + H[5], h6r = H[7] * r + 1.0;
                for (int c = -1; c <= nMod; c++)
                {
                    double den = H[6] * c + h6r;
                    float px = (float)((H[0] * c + h0r) / den);
                    float py = (float)((H[3] * c + h3r) / den);
                    float val = 0f;
                    if (px >= 0f && py >= 0f && px < w - 1 && py < h - 1)
                    {
                        int ix = (int)px, iy = (int)py;
                        float fx = px - ix, fy = py - iy;
                        float* row0 = pImg + iy * w + ix;
                        float* row1 = row0 + w;
                        float v0 = row0[0] + fx * (row0[1] - row0[0]);
                        float v1 = row1[0] + fx * (row1[1] - row1[0]);
                        val = v0 + fy * (v1 - v0);
                    }
                    ve[rOff + (c + 1)] = val - 0.52f;
                }
            }
        }

        double qzSum = 0.0;
        for (int i = 1; i <= nMod; i++)
        {
            qzSum += ve[0 * extN + i] + ve[(nMod + 1) * extN + i];
            qzSum += ve[i * extN + 0] + ve[i * extN + (nMod + 1)];
        }
        double qzPen = -qzSum / nMod;

        double sBot = 0.0, sLft = 0.0, sTop = 0.0, sRgt = 0.0;
        for (int i = 0; i < nMod; i++)
        {
            sBot += ve[nMod * extN + (i + 1)];
            sLft += ve[(i + 1) * extN + 1];
            double sgnR0 = (i % 2 == 0) ? 1.0 : -1.0;
            double sgnCr = (i % 2 == 1) ? 1.0 : -1.0;
            sTop += ve[1 * extN + (i + 1)] * sgnR0;
            sRgt += ve[(i + 1) * extN + nMod] * sgnCr;
        }
        double invN = 1.0 / nMod;
        double sc = (sBot + sLft + 1.4 * (sTop + sRgt)) * invN;

        if (nMod == 32 || nMod == 36)
        {
            int half = nMod / 2;
            double cHm1 = 0.0, cH = 0.0, cAltR = 0.0, cAltC = 0.0;
            for (int i = 0; i < nMod; i++)
            {
                cHm1 += ve[half * extN + (i + 1)];
                cH += ve[(i + 1) * extN + (half + 1)];
                double sgnR0 = (i % 2 == 0) ? 1.0 : -1.0;
                double sgnCr = (i % 2 == 1) ? 1.0 : -1.0;
                cAltR += ve[(half + 1) * extN + (i + 1)] * sgnR0;
                cAltC += ve[(i + 1) * extN + half] * sgnCr;
            }
            sc += (cHm1 + cH + 1.4 * (cAltR + cAltC)) * invN;
        }

        double eyeSum = 0.0;
        for (int r = 1; r <= nMod; r++)
        {
            int rOff = r * extN;
            for (int c = 1; c <= nMod; c++)
            {
                double av = Math.Abs((double)ve[rOff + c]);
                eyeSum += av < 0.35 ? av : 0.35;
            }
        }
        double eye = eyeSum / (nMod * nMod);
        return -(sc + 0.9 * qzPen + 1.5 * eye);
    }

    public static float[] OptimizeClock8Dof(float[] filtClk, int h, int w, int nMod, float[] cInit, float modPx)
    {
        float[] srcC = { 0f, 0f, nMod - 1, 0f, nMod - 1, nMod - 1, 0f, nMod - 1 };
        float[] eInit = new float[8];
        CornersToEdge(cInit, eInit);

        float[] lo = new float[8], hi = new float[8];
        for (int k = 0; k < 4; k++) { lo[k] = eInit[k] - 1.6f * modPx; hi[k] = eInit[k] + 1.6f * modPx; }
        for (int k = 4; k < 8; k++) { lo[k] = eInit[k] - 1.8f * modPx; hi[k] = eInit[k] + 1.8f * modPx; }

        float[] bestE = (float[])eInit.Clone();
        double bestLoss = EvalLossEdge(filtClk, h, w, nMod, srcC, bestE);

        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = 0; k < 8; k++)
            {
                float bestVal = bestE[k];
                float span = k < 4 ? 1.35f * modPx : 1.15f * modPx;
                for (int s = -7; s <= 7; s++)
                {
                    float v = eInit[k] + (s / 7.0f) * span;
                    if (v < lo[k] || v > hi[k]) continue;
                    bestE[k] = v;
                    double l = EvalLossEdge(filtClk, h, w, nMod, srcC, bestE);
                    if (l < bestLoss) { bestLoss = l; bestVal = v; }
                }
                bestE[k] = bestVal;
            }
        }

        float[] centerE = (float[])bestE.Clone();
        object sync = new object();
        Parallel.For(0, 9, seedIdx =>
        {
            int sXr = (seedIdx / 3) - 1;
            int sYt = (seedIdx % 3) - 1;
            Span<float> e = stackalloc float[8];
            centerE.AsSpan().CopyTo(e);
            e[1] = DpmMath.Clamp(e[1] + sXr * 0.40f * modPx, lo[1], hi[1]);
            e[2] = DpmMath.Clamp(e[2] + sYt * 0.40f * modPx, lo[2], hi[2]);

            double l = EvalLossEdge(filtClk, h, w, nMod, srcC, e);
            float stepSeed = 0.24f * modPx;
            for (int it = 0; it < 4; it++)
            {
                for (int d = 0; d < 8; d++)
                {
                    float st = d < 4 ? stepSeed : stepSeed * 1.4f;
                    float orig = e[d];
                    if (orig + st <= hi[d])
                    {
                        e[d] = orig + st;
                        double lp = EvalLossEdge(filtClk, h, w, nMod, srcC, e);
                        if (lp < l) { l = lp; continue; }
                    }
                    if (orig - st >= lo[d])
                    {
                        e[d] = orig - st;
                        double lm = EvalLossEdge(filtClk, h, w, nMod, srcC, e);
                        if (lm < l) { l = lm; continue; }
                    }
                    e[d] = orig;
                }
                stepSeed *= 0.55f;
            }

            lock (sync)
            {
                if (l < bestLoss)
                {
                    bestLoss = l;
                    e.CopyTo(bestE);
                }
            }
        });

        float step = 0.10f * modPx;
        Span<float> baseE = stackalloc float[8];
        bestE.AsSpan().CopyTo(baseE);
        Span<float> cand = stackalloc float[8];
        Span<float> pat = stackalloc float[8];

        while (step > 0.004f)
        {
            bestE.AsSpan().CopyTo(cand);
            bool improved = false;
            for (int d = 0; d < 8; d++)
            {
                float st = d < 4 ? step : step * 1.4f;
                float orig = cand[d];
                if (orig + st <= hi[d])
                {
                    cand[d] = orig + st;
                    double lp = EvalLossEdge(filtClk, h, w, nMod, srcC, cand);
                    if (lp < bestLoss) { bestLoss = lp; improved = true; continue; }
                }
                if (orig - st >= lo[d])
                {
                    cand[d] = orig - st;
                    double lm = EvalLossEdge(filtClk, h, w, nMod, srcC, cand);
                    if (lm < bestLoss) { bestLoss = lm; improved = true; continue; }
                }
                cand[d] = orig;
            }
            if (improved)
            {
                for (int d = 0; d < 8; d++)
                    pat[d] = DpmMath.Clamp(cand[d] + 0.75f * (cand[d] - baseE[d]), lo[d], hi[d]);
                cand.CopyTo(baseE);
                cand.CopyTo(bestE);
                double lPat = EvalLossEdge(filtClk, h, w, nMod, srcC, pat);
                if (lPat < bestLoss)
                {
                    bestLoss = lPat;
                    pat.CopyTo(bestE);
                }
            }
            else
            {
                step *= 0.50f;
                bestE.AsSpan().CopyTo(baseE);
            }
        }

        float[] outDst = new float[8];
        EdgeToCorners(bestE, outDst);
        return outDst;
    }
}
}
