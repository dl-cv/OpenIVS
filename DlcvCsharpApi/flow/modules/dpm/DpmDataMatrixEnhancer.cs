using System;
using System.Collections.Generic;
using System.Diagnostics;
using ZXingCpp;

namespace DlcvModules
{

/// <summary>
/// 单枚 DPM DataMatrix 增强、软判决恢复与原生 ZXing.Net 解码结果。
/// </summary>
public sealed class DpmEnhancementResult
{
    public byte[] EnhancedGray { get; set; }
    public int EnhancedWidth { get; set; }
    public int EnhancedHeight { get; set; }
    public int MatrixSize { get; set; }
    public float PixelsPerModule { get; set; }
    public bool NyquistSufficient { get; set; }
    public string DecodedText { get; set; }
    public string DecodeMode { get; set; }
    public double S1EnvelopeMs { get; set; }
    public double S2DogMs { get; set; }
    public double S3ClockLockMs { get; set; }
    public double S4OsdEccMs { get; set; }
    public double EnhanceTotalMs { get; set; }
    public double S5ZxingMs { get; set; }
    public double EndToEndMs { get; set; }
}

/// <summary>
/// 喷砂金属表面激光刻印 (DPM) DataMatrix 二维码四阶段盲增强与原生 ZXing.Net 识别引擎。
/// 直接支持工业相机裸 8-bit 灰度内存 buffer (byte[] / ReadOnlySpan&lt;byte&gt;) 或任意图像文件路径。
/// </summary>
public static class DpmDataMatrixEnhancer
{
    private static readonly float[] SrcCorners = { 0f, 0f, 31f, 0f, 31f, 31f, 0f, 31f };
    private static readonly float[] SrcAsym = { -0.65f, 0.05f, 31.05f, 0.05f, 31.05f, 31.65f, -0.65f, 31.65f };
    private static readonly float[] SrcOb1 = { -0.08f, -0.04f, 31.04f, 0.02f, 31.00f, 31.04f, -0.10f, 31.05f };
    private static readonly float[] SrcOb2 = { -0.03f, 0.00f, 30.98f, 0.01f, 31.10f, 31.09f, -0.11f, 31.15f };
    private static readonly float[] DstRect =
    {
        Ecc200BinaryOsdDecoder.PadRect + 3f, Ecc200BinaryOsdDecoder.PadRect + 3f,
        Ecc200BinaryOsdDecoder.PadRect + 31 * Ecc200BinaryOsdDecoder.SRect + 3f, Ecc200BinaryOsdDecoder.PadRect + 3f,
        Ecc200BinaryOsdDecoder.PadRect + 31 * Ecc200BinaryOsdDecoder.SRect + 3f, Ecc200BinaryOsdDecoder.PadRect + 31 * Ecc200BinaryOsdDecoder.SRect + 3f,
        Ecc200BinaryOsdDecoder.PadRect + 3f, Ecc200BinaryOsdDecoder.PadRect + 31 * Ecc200BinaryOsdDecoder.SRect + 3f
    };
    private static readonly float[] GridPts32 = BuildGridPts32();

    private static float[] BuildGridPts32()
    {
        float[] pts = new float[32 * 32 * 2];
        int idx = 0;
        for (int r = 0; r < 32; r++)
        {
            for (int c = 0; c < 32; c++)
            {
                pts[idx++] = c;
                pts[idx++] = r;
            }
        }
        return pts;
    }

    /// <summary>
    /// 用 ZXing-C++ 解码增强后的纯 DataMatrix 图像。
    /// </summary>
    private static string DecodeEnhancedWithZxingCpp(byte[] gray, int width, int height)
    {
        using (var reader = new ZXingCpp.BarcodeReader())
        {
            reader.TryRotate = false;
            reader.TryInvert = false;
            reader.TryDownscale = false;
            reader.TextMode = TextMode.HRI;
            if (!BarcodeFormats.TryParse("DataMatrix", out BarcodeFormats formats))
                throw new ArgumentException("Invalid barcode format: DataMatrix");
            reader.Formats = formats;
            var view = new ImageView(gray, width, height, ImageFormat.Lum, width);
            Barcode[] codes = reader.From(view);
            try
            {
                return codes != null && codes.Length > 0 ? codes[0].Text : null;
            }
            finally
            {
                if (codes != null)
                    foreach (var code in codes) code.Dispose();
                GC.KeepAlive(view);
            }
        }
    }

    /// <summary>
    /// 对单枚 8-bit 灰度 DPM DataMatrix ROI 执行四阶段盲增强并交由原生 ZXing.Net 解码。
    /// 工业相机集成可直接传入海康 MVS / Basler Pylon / OpenCvSharp Mat 的 8-bit 灰度数组。
    /// </summary>
    public static DpmEnhancementResult EnhanceAndDecode(
        byte[] grayRoi, int w, int h,
        int upscaleModulePx = 8, int quietZonePx = 32, bool pureBarcodeZxing = false)
    {
        long freq = Stopwatch.Frequency;
        double ticksToMs = 1000.0 / freq;
        long t0 = Stopwatch.GetTimestamp();

        // 三边静区 60 分位估计基底金属亮度，抗局部阴影与角落暗区
        int qzW = Math.Max(1, (int)(w * 0.90));
        int qzH = Math.Min(8, h);
        int lftW = Math.Min(8, w);
        byte[] qzSample = new byte[2 * qzW * qzH + lftW * h];
        int qzPos = 0;
        for (int y = 0; y < qzH; y++)
        {
            Buffer.BlockCopy(grayRoi, y * w, qzSample, qzPos, qzW);
            qzPos += qzW;
            Buffer.BlockCopy(grayRoi, (h - qzH + y) * w, qzSample, qzPos, qzW);
            qzPos += qzW;
        }
        for (int y = 0; y < h; y++)
        {
            Buffer.BlockCopy(grayRoi, y * w, qzSample, qzPos, lftW);
            qzPos += lftW;
        }
        float qzMed = FastVisionOps.ComputePercentileU8(qzSample, 0.60);
        byte qzFill = (byte)DpmMath.Clamp((int)DpmMathF.Round(qzMed), 0, 255);

        byte[] imC = (byte[])grayRoi.Clone();
        if (w >= 195)
        {
            int xCut = (int)(w * 0.95);
            for (int y = 0; y < h; y++)
            {
                int rOff = y * w;
                for (int x = xCut; x < w; x++) imC[rOff + x] = qzFill;
            }
        }

        // S1: 1/4 尺度快速形态学上包络参考 + 双级高光修复 + 5x5 正脉冲星点脱敏
        float p88 = FastVisionOps.ComputePercentileU8(imC, 0.88);
        float capPre = DpmMathF.Max(qzMed + 22.0f, p88);
        float[] imCapPre = new float[w * h];
        for (int i = 0; i < imC.Length; i++)
            imCapPre[i] = imC[i] < capPre ? imC[i] : capPre;

        int wQ4 = Math.Max(16, w / 4), hQ4 = Math.Max(16, h / 4);
        float[] imQ4 = FastVisionOps.ResizeAreaF32(imCapPre, w, h, wQ4, hQ4);
        float[] closeQ4 = FastVisionOps.MorphCloseEllipseF32(imQ4, wQ4, hQ4, 17);
        float[] blurQ4 = FastVisionOps.GaussianBlurF32(closeQ4, wQ4, hQ4, 14.0);
        float[] bgPre = FastVisionOps.ResizeBilinearF32(blurQ4, wQ4, hQ4, w, h);

        byte[] candGlare = new byte[w * h];
        int xGlintMax = (int)(w * 0.94);
        for (int y = 6; y < h - 6; y++)
        {
            int rOff = y * w;
            for (int x = 6; x < xGlintMax; x++)
            {
                int idx = rOff + x;
                if (imC[idx] > bgPre[idx] + 18.0f || imC[idx] > 235)
                    candGlare[idx] = 255;
            }
        }

        var (glareLbl, glareComps) = FastVisionOps.ConnectedComponents8(candGlare, w, h, collectBoundary: false);
        bool anySmall = false, anyLarge = false;
        byte[] smallGlint = new byte[w * h];
        byte[] largeGlare = new byte[w * h];
        if (glareComps.Count > 0)
        {
            byte[] compType = new byte[glareComps.Count + 1];
            foreach (var comp in glareComps)
            {
                if (comp.Area <= 45) { compType[comp.Label] = 1; anySmall = true; }
                else { compType[comp.Label] = 2; anyLarge = true; }
            }
            for (int i = 0; i < glareLbl.Length; i++)
            {
                byte t = compType[glareLbl[i]];
                if (t == 1) smallGlint[i] = 255;
                else if (t == 2) largeGlare[i] = 255;
            }
        }

        byte[] work = (byte[])imC.Clone();
        if (anySmall)
        {
            byte[] dilS = FastVisionOps.DilateRect3x3(smallGlint, w, h);
            byte[] med7 = FastVisionOps.MedianBlurU8(imC, w, h, 7);
            for (int i = 0; i < work.Length; i++)
                if (dilS[i] != 0) work[i] = med7[i];
        }
        if (anyLarge)
        {
            byte[] dilL = FastVisionOps.DilateRect3x3(largeGlare, w, h);
            byte[] med11 = FastVisionOps.MedianBlurU8(work, w, h, 11);
            for (int i = 0; i < work.Length; i++)
                if (dilL[i] != 0) work[i] = med11[i];
        }

        byte[] med5Pre = FastVisionOps.MedianBlurU8(work, w, h, 5);
        float[] glareFloat = new float[w * h];
        for (int i = 0; i < work.Length; i++)
        {
            bool isSpike = work[i] > med5Pre[i] + 26;
            if (isSpike) work[i] = med5Pre[i];
            glareFloat[i] = (smallGlint[i] != 0 ? 0.45f : 0f)
                          + (largeGlare[i] != 0 ? 1.50f : 0f)
                          + (isSpike ? 0.25f : 0f);
        }
        float[] glareMap = FastVisionOps.GaussianBlurF32(glareFloat, w, h, 2.4);

        byte[] med3U8 = FastVisionOps.MedianBlurU8(work, w, h, 3);
        byte[] med5U8 = FastVisionOps.MedianBlurU8(work, w, h, 5);
        float[] clCap = new float[w * h];
        float[] hetRaw = new float[w * h];
        for (int i = 0; i < clCap.Length; i++)
        {
            float c3 = med3U8[i], c5 = med5U8[i];
            float cMix = 0.50f * c3 + 0.50f * c5;
            float sparkle = imC[i] - c5 - 12.0f;
            hetRaw[i] = DpmMathF.Abs(c3 - c5) + (sparkle > 0f ? 0.35f * sparkle : 0f);
            float lim = bgPre[i] + 14.0f;
            clCap[i] = cMix < lim ? cMix : lim;
        }

        float[] hetMap = FastVisionOps.GaussianBlurF32(hetRaw, w, h, 1.2);
        int wH2 = Math.Max(16, w / 2), hH2 = Math.Max(16, h / 2);
        float[] cH2 = FastVisionOps.ResizeAreaF32(clCap, w, h, wH2, hH2);
        float[] closedH2 = FastVisionOps.MorphCloseEllipseF32(cH2, wH2, hH2, 33);
        float[] blurH2 = FastVisionOps.GaussianBlurF32(closedH2, wH2, hH2, 33.0 * 0.85);
        float[] bgFull = FastVisionOps.ResizeBilinearF32(blurH2, wH2, hH2, w, h);

        float[] diff = new float[w * h];
        float[] posDiff = new float[w * h];
        for (int i = 0; i < diff.Length; i++)
        {
            float d = bgFull[i] - clCap[i];
            diff[i] = d;
            posDiff[i] = d > 0f ? d : 0f;
        }
        long tS1 = Stopwatch.GetTimestamp();

        // S2: 连通域分割 + 码芯 [P16, P84] 归一化 + 保平顶截断高斯差分 (DoG)
        float[] uRough = FastVisionOps.GaussianBlurF32(posDiff, w, h, 3.6);
        byte[] uRoughU8 = new byte[w * h];
        for (int i = 0; i < uRough.Length; i++)
            uRoughU8[i] = (byte)DpmMath.Clamp((int)(uRough[i] * 3.0f), 0, 255);
        double otsuTh = FastVisionOps.ComputeOtsuThreshold(uRoughU8);

        byte[] coreCand = new byte[w * h];
        float coreCut = (float)(otsuTh * 0.85);
        for (int i = 0; i < uRough.Length; i++)
            if (uRough[i] * 3.0f > coreCut) coreCand[i] = 255;

        var (coreLabels, coreComps) = FastVisionOps.ConnectedComponents8(coreCand, w, h, collectBoundary: true);
        if (coreComps.Count == 0)
        {
            byte[] blank = new byte[w * h];
            Buffer.BlockCopy(grayRoi, 0, blank, 0, blank.Length);
            return new DpmEnhancementResult
            {
                EnhancedGray = blank, EnhancedWidth = w, EnhancedHeight = h,
                MatrixSize = 32, PixelsPerModule = 0f, NyquistSufficient = false,
                DecodedText = null, DecodeMode = "no_core_found",
                S1EnvelopeMs = (tS1 - t0) * ticksToMs, S2DogMs = 0, S3ClockLockMs = 0,
                S4OsdEccMs = 0, EnhanceTotalMs = (tS1 - t0) * ticksToMs, S5ZxingMs = 0, EndToEndMs = (tS1 - t0) * ticksToMs
            };
        }

        var bestCore = coreComps[0];
        for (int i = 1; i < coreComps.Count; i++)
            if (coreComps[i].Area > bestCore.Area) bestCore = coreComps[i];

        List<float> coreDiffVals = new List<float>(bestCore.Area);
        for (int i = 0; i < coreLabels.Length; i++)
            if (coreLabels[i] == bestCore.Label) coreDiffVals.Add(diff[i]);

        var (p16, p84) = FastVisionOps.ComputePercentiles16And84(coreDiffVals);
        float loNorm = p16 - 3.0f;
        float denomNorm = DpmMathF.Max(10.0f, (p84 + 6.0f) - loNorm);
        float[] u = new float[w * h];
        float[] posU = new float[w * h];
        for (int i = 0; i < u.Length; i++)
        {
            float v = (diff[i] - loNorm) / denomNorm;
            v = v < -0.05f ? -0.05f : (v > 1.05f ? 1.05f : v);
            u[i] = v;
            posU[i] = v > 0f ? v : 0f;
        }

        var box = FastVisionOps.MinAreaRectBox(bestCore.BoundaryPoints);
        int idxTl = 0, idxTr = 0, idxBr = 0, idxBl = 0;
        float minSum = float.PositiveInfinity, maxSum = float.NegativeInfinity;
        float minDiff = float.PositiveInfinity, maxDiff = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            float s = box[i].X + box[i].Y;
            float d = box[i].Y - box[i].X; // np.diff([x, y]) = y - x
            if (s < minSum) { minSum = s; idxTl = i; }
            if (s > maxSum) { maxSum = s; idxBr = i; }
            if (d < minDiff) { minDiff = d; idxTr = i; }
            if (d > maxDiff) { maxDiff = d; idxBl = i; }
        }
        (float X, float Y)[] ordered = { box[idxTl], box[idxTr], box[idxBr], box[idxBl] };

        float d01 = DpmMathF.Sqrt((ordered[1].X - ordered[0].X) * (ordered[1].X - ordered[0].X) + (ordered[1].Y - ordered[0].Y) * (ordered[1].Y - ordered[0].Y));
        float d03 = DpmMathF.Sqrt((ordered[3].X - ordered[0].X) * (ordered[3].X - ordered[0].X) + (ordered[3].Y - ordered[0].Y) * (ordered[3].Y - ordered[0].Y));
        float span = 0.5f * (d01 + d03);
        int wTmp = Math.Max(128, (int)DpmMathF.Round(span));

        float[] ordSrc = { ordered[0].X, ordered[0].Y, ordered[1].X, ordered[1].Y, ordered[2].X, ordered[2].Y, ordered[3].X, ordered[3].Y };
        float[] ordDst = { 0f, 0f, wTmp, 0f, wTmp, wTmp, 0f, wTmp };
        Span<double> hTmp = stackalloc double[9];
        FastVisionOps.SolveHomography4Pts(ordSrc, ordDst, hTmp);
        float[] uTmp = FastVisionOps.WarpPerspectiveF32(posU, w, h, hTmp, wTmp, wTmp);

        int sw0 = Math.Max(5, wTmp / 10);
        int bestK90 = 0;
        double bestLScore = double.NegativeInfinity;
        for (int k = 0; k < 4; k++)
        {
            float[] rot = FastVisionOps.Rot90Ccw(uTmp, wTmp, k);
            double maxBot = double.NegativeInfinity, maxTop = double.NegativeInfinity;
            for (int r = wTmp - sw0; r < wTmp; r++)
            {
                double s = 0;
                for (int c = 0; c < wTmp; c++) s += rot[r * wTmp + c];
                if (s / wTmp > maxBot) maxBot = s / wTmp;
            }
            for (int r = 0; r < sw0; r++)
            {
                double s = 0;
                for (int c = 0; c < wTmp; c++) s += rot[r * wTmp + c];
                if (s / wTmp > maxTop) maxTop = s / wTmp;
            }
            double maxLeft = double.NegativeInfinity, maxRight = double.NegativeInfinity;
            for (int c = 0; c < sw0; c++)
            {
                double s = 0;
                for (int r = 0; r < wTmp; r++) s += rot[r * wTmp + c];
                if (s / wTmp > maxLeft) maxLeft = s / wTmp;
            }
            for (int c = wTmp - sw0; c < wTmp; c++)
            {
                double s = 0;
                for (int r = 0; r < wTmp; r++) s += rot[r * wTmp + c];
                if (s / wTmp > maxRight) maxRight = s / wTmp;
            }
            double score = maxBot + maxLeft - maxTop - maxRight;
            if (score > bestLScore) { bestLScore = score; bestK90 = k; }
        }

        float[] cornersOuter = new float[8];
        for (int i = 0; i < 4; i++)
        {
            var pt = ordered[(i + bestK90) % 4];
            cornersOuter[2 * i] = pt.X;
            cornersOuter[2 * i + 1] = pt.Y;
        }
        float modPx = span / 32.0f;

        float[] u1Clk = FastVisionOps.GaussianBlurF32(u, w, h, 0.17 * modPx);
        float[] g2Clk = FastVisionOps.GaussianBlurF32(u, w, h, 0.58 * modPx);
        float[] filtClk = new float[w * h];
        for (int i = 0; i < filtClk.Length; i++)
            filtClk[i] = u1Clk[i] + 2.4f * (u1Clk[i] - g2Clk[i]);

        float[] u1Fd1 = FastVisionOps.GaussianBlurF32(u, w, h, 0.175 * modPx);
        float[] g2Fd1 = FastVisionOps.GaussianBlurF32(u, w, h, 0.605 * modPx);
        float[] fd1 = new float[w * h];
        for (int i = 0; i < fd1.Length; i++)
        {
            float v = u1Fd1[i] + 2.6f * (u1Fd1[i] - g2Fd1[i]);
            fd1[i] = v < -0.15f ? -0.15f : (v > 1.12f ? 1.12f : v);
        }
        long tS2 = Stopwatch.GetTimestamp();

        // S3: 正交 8-DOF 亚像素时钟边锁定
        Span<double> hAsym = stackalloc double[9];
        FastVisionOps.SolveHomography4Pts(SrcAsym, cornersOuter, hAsym);
        float[] cInit = new float[8];
        FastVisionOps.PerspectiveTransformPts(SrcCorners, hAsym, cInit);
        float[] dstRaw = FastVisionOps.OptimizeClock8Dof(filtClk, h, w, 32, cInit, modPx);
        long tS3 = Stopwatch.GetTimestamp();

        // S4: 惰性多带宽评估 + 二元域 GF(2) OSD 软判决恢复
        Span<double> hm1 = stackalloc double[9];
        FastVisionOps.SolveHomography4Pts(SrcOb1, dstRaw, hm1);
        float[] pr1 = new float[32 * 32 * 2];
        FastVisionOps.PerspectiveTransformPts(GridPts32, hm1, pr1);

        float[] hetGrid1 = FastVisionOps.RemapGrid32(hetMap, w, h, pr1);
        float[] v1 = FastVisionOps.RemapGrid32(fd1, w, h, pr1);

        float[] glareGrid1 = FastVisionOps.RemapGrid32(glareMap, w, h, pr1);
        float[] wH1 = new float[784];
        float[] llrPrimary = new float[784];
        for (int k = 0; k < 98; k++)
        {
            for (int b = 0; b < 8; b++)
            {
                int idx32 = Ecc200BinaryOsdDecoder.BitR32[k, b] * 32 + Ecc200BinaryOsdDecoder.BitC32[k, b];
                int bitIdx = k * 8 + b;
                float weight = 1.0f / ((1.0f + 0.09f * hetGrid1[idx32]) * (1.0f + 6.0f * glareGrid1[idx32]));
                wH1[bitIdx] = weight;
                llrPrimary[bitIdx] = (v1[idx32] - 0.50f) * weight;
            }
        }

        byte[] msgOut = new byte[98];
        byte[] wonBits32 = null;
        string wonMode = "unsolved";

        int ret = Ecc200BinaryOsdDecoder.DecodeBinaryOsd(llrPrimary, 62, 0, msgOut, out int ordOut, out int bdOut, out _);
        if (ret >= 0)
        {
            wonBits32 = Ecc200BinaryOsdDecoder.CodewordsToBits32(msgOut);
            wonMode = $"OSD-{ordOut}(fd1,bd={bdOut})";
        }
        else
        {
            float[] u1Fd3 = FastVisionOps.GaussianBlurF32(u, w, h, 0.255 * modPx);
            float[] g2Fd3 = FastVisionOps.GaussianBlurF32(u, w, h, 0.66 * modPx);
            float[] fd3 = new float[w * h];
            for (int i = 0; i < fd3.Length; i++)
            {
                float v = u1Fd3[i] + 3.8f * (u1Fd3[i] - g2Fd3[i]);
                fd3[i] = v < -0.15f ? -0.15f : (v > 1.12f ? 1.12f : v);
            }

            float[] imgPts = new float[8];
            FastVisionOps.PerspectiveTransformPts(SrcCorners, hm1, imgPts);
            Span<double> hRect = stackalloc double[9];
            FastVisionOps.SolveHomography4Pts(imgPts, DstRect, hRect);
            float[] uRect = FastVisionOps.WarpPerspectiveF32(posU, w, h, hRect, Ecc200BinaryOsdDecoder.WRect, Ecc200BinaryOsdDecoder.WRect);
            float[] mp2 = Ecc200BinaryOsdDecoder.SolveKroneckerPsf32(uRect);
            float[] vFd3 = FastVisionOps.RemapGrid32(fd3, w, h, pr1);

            float[] llrEns = new float[784];
            float[] llrFd46 = new float[784];
            for (int k = 0; k < 98; k++)
            {
                for (int b = 0; b < 8; b++)
                {
                    int idx32 = Ecc200BinaryOsdDecoder.BitR32[k, b] * 32 + Ecc200BinaryOsdDecoder.BitC32[k, b];
                    int bitIdx = k * 8 + b;
                    float ensVal = 0.5f * vFd3[idx32] + 0.5f * mp2[idx32];
                    llrEns[bitIdx] = (ensVal - 0.48f) * wH1[bitIdx];
                    llrFd46[bitIdx] = (v1[idx32] - 0.46f) * wH1[bitIdx];
                }
            }

            Span<double> hm2 = stackalloc double[9];
            FastVisionOps.SolveHomography4Pts(SrcOb2, dstRaw, hm2);
            float[] pr2 = new float[32 * 32 * 2];
            FastVisionOps.PerspectiveTransformPts(GridPts32, hm2, pr2);
            float[] hetGrid2 = FastVisionOps.RemapGrid32(hetMap, w, h, pr2);
            float[] glareGrid2 = FastVisionOps.RemapGrid32(glareMap, w, h, pr2);
            float[] v2Fd1 = FastVisionOps.RemapGrid32(fd1, w, h, pr2);
            float[] llrOb2 = new float[784];
            for (int k = 0; k < 98; k++)
            {
                for (int b = 0; b < 8; b++)
                {
                    int idx32 = Ecc200BinaryOsdDecoder.BitR32[k, b] * 32 + Ecc200BinaryOsdDecoder.BitC32[k, b];
                    int bitIdx = k * 8 + b;
                    llrOb2[bitIdx] = (v2Fd1[idx32] - 0.48f) / ((1.0f + 0.09f * hetGrid2[idx32]) * (1.0f + 6.0f * glareGrid2[idx32]));
                }
            }

            (string Name, float[] Llr)[] candidates =
            {
                ("ens3", llrEns),
                ("fd1_46", llrFd46),
                ("ob2_fd1", llrOb2),
                ("fd1_50", llrPrimary)
            };

            foreach (int tail in new[] { 0, 200 })
            {
                foreach (var (cName, llrC) in candidates)
                {
                    if (tail == 0 && cName == "fd1_50") continue;
                    ret = Ecc200BinaryOsdDecoder.DecodeBinaryOsd(llrC, 66, tail, msgOut, out ordOut, out bdOut, out _);
                    if (ret >= 0)
                    {
                        wonBits32 = Ecc200BinaryOsdDecoder.CodewordsToBits32(msgOut);
                        wonMode = $"OSD-{ordOut}({cName},bd={bdOut})";
                        break;
                    }
                }
                if (wonBits32 != null) break;
            }
        }

        if (wonBits32 == null)
        {
            wonBits32 = new byte[1024];
            for (int i = 0; i < 1024; i++) wonBits32[i] = v1[i] > 0.50f ? (byte)1 : (byte)0;
            Ecc200BinaryOsdDecoder.ApplyEcc200Clock32(wonBits32);
        }

        // 渲染带标准白色静区的超清晰二值化 DataMatrix 图像供原生 ZXing.Net 解码
        int symW = 32 * upscaleModulePx;
        int outW = symW + 2 * quietZonePx;
        byte[] enhImg = new byte[outW * outW];
        for (int i = 0; i < enhImg.Length; i++) enhImg[i] = 255;
        for (int r = 0; r < 32; r++)
        {
            for (int c = 0; c < 32; c++)
            {
                if (wonBits32[r * 32 + c] == 0) continue;
                int y0 = quietZonePx + r * upscaleModulePx;
                int x0 = quietZonePx + c * upscaleModulePx;
                for (int dy = 0; dy < upscaleModulePx; dy++)
                {
                    int rOff = (y0 + dy) * outW + x0;
                    for (int dx = 0; dx < upscaleModulePx; dx++)
                        enhImg[rOff + dx] = 0;
                }
            }
        }
        long tS4 = Stopwatch.GetTimestamp();

        string decodedText = DecodeEnhancedWithZxingCpp(enhImg, outW, outW);
        long tS5 = Stopwatch.GetTimestamp();

        return new DpmEnhancementResult
        {
            EnhancedGray = enhImg,
            EnhancedWidth = outW,
            EnhancedHeight = outW,
            MatrixSize = 32,
            PixelsPerModule = modPx,
            NyquistSufficient = modPx >= 2.5f,
            DecodedText = decodedText,
            DecodeMode = wonMode,
            S1EnvelopeMs = (tS1 - t0) * ticksToMs,
            S2DogMs = (tS2 - tS1) * ticksToMs,
            S3ClockLockMs = (tS3 - tS2) * ticksToMs,
            S4OsdEccMs = (tS4 - tS3) * ticksToMs,
            EnhanceTotalMs = (tS4 - t0) * ticksToMs,
            S5ZxingMs = (tS5 - tS4) * ticksToMs,
            EndToEndMs = (tS5 - t0) * ticksToMs
        };
    }

}
}
