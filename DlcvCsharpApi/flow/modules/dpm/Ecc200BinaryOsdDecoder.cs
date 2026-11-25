using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace DlcvModules
{

/// <summary>
/// ECC200 32x32 (RS(98, 62) over GF(256)) 二元域 GF(2) 有序统计软判决解码器 (OSD-0/1/2/3)
/// 与 228x228 可分离 Kronecker 2D PSF 反卷积求解器（100% 纯 C# 托管实现，硬件 POPCNT + 一维扁平内存加速）。
/// </summary>
public static class Ecc200BinaryOsdDecoder
{
    public static readonly int[,] BitR32 = new int[98, 8];
    public static readonly int[,] BitC32 = new int[98, 8];

    private static readonly byte[] GfExp = new byte[512];
    private static readonly short[] GfLog = new short[256];
    private static readonly byte[] GfMulTab = new byte[256 * 256];
    private static readonly byte[] GBin = new byte[496 * 784];

    // 228x228 Kronecker 2D PSF 预计算特征分解矩阵 (S_RECT=6, PAD_RECT=18)
    public const int SRect = 6;
    public const int PadRect = 18;
    public const int WRect = 32 * SRect + 2 * PadRect; // 228

    private static readonly float[] PsfAx = new float[WRect * 32];
    private static readonly float[,] PsfG = new float[32, 32];
    private static readonly float[,] PsfQ = new float[32, 32];
    private static readonly float[,] PsfDenom = new float[32, 32];
    private static readonly float PsfInvLip;
    private const float PsfReg = 0.14f;

    static Ecc200BinaryOsdDecoder()
    {
        InitUtahPlacement32();
        InitGaloisAndBinaryGenerator();
        PsfInvLip = InitKroneckerPsfSolver(0.48, 0.50, PsfReg);
    }

    private static void InitUtahPlacement32()
    {
        const int nrow = 28, ncol = 28;
        int[,] arr = new int[nrow, ncol];
        for (int i = 0; i < nrow; i++)
            for (int j = 0; j < ncol; j++)
                arr[i, j] = -1;

        void SetMod(int r, int c, int chIdx, int bit)
        {
            if (r < 0) { r += nrow; c += 4 - ((nrow + 4) % 8); }
            if (c < 0) { c += ncol; r += 4 - ((ncol + 4) % 8); }
            arr[r, c] = chIdx * 8 + bit;
        }

        void Utah(int r, int c, int chIdx)
        {
            SetMod(r - 2, c - 2, chIdx, 0); SetMod(r - 2, c - 1, chIdx, 1);
            SetMod(r - 1, c - 2, chIdx, 2); SetMod(r - 1, c - 1, chIdx, 3); SetMod(r - 1, c, chIdx, 4);
            SetMod(r, c - 2, chIdx, 5);     SetMod(r, c - 1, chIdx, 6);     SetMod(r, c, chIdx, 7);
        }

        int chr = 0, rr = 4, cc = 0;
        while (true)
        {
            if (rr == nrow && cc == 0)
            {
                SetMod(nrow - 1, 0, chr, 0); SetMod(nrow - 1, 1, chr, 1); SetMod(nrow - 1, 2, chr, 2);
                SetMod(0, ncol - 2, chr, 3); SetMod(0, ncol - 1, chr, 4); SetMod(1, ncol - 1, chr, 5);
                SetMod(2, ncol - 1, chr, 6); SetMod(3, ncol - 1, chr, 7); chr++;
            }
            else if (rr == nrow - 2 && cc == 0 && (ncol % 4) != 0)
            {
                SetMod(nrow - 3, 0, chr, 0); SetMod(nrow - 2, 0, chr, 1); SetMod(nrow - 1, 0, chr, 2);
                SetMod(0, ncol - 4, chr, 3); SetMod(0, ncol - 3, chr, 4); SetMod(0, ncol - 2, chr, 5);
                SetMod(0, ncol - 1, chr, 6); SetMod(1, ncol - 1, chr, 7); chr++;
            }
            else if (rr == nrow - 2 && cc == 0 && (ncol % 8) == 4)
            {
                SetMod(nrow - 3, 0, chr, 0); SetMod(nrow - 2, 0, chr, 1); SetMod(nrow - 1, 0, chr, 2);
                SetMod(0, ncol - 2, chr, 3); SetMod(0, ncol - 1, chr, 4); SetMod(1, ncol - 1, chr, 5);
                SetMod(2, ncol - 1, chr, 6); SetMod(3, ncol - 1, chr, 7); chr++;
            }
            else if (rr == nrow + 4 && cc == 2 && (ncol % 8) == 0)
            {
                SetMod(nrow - 1, 0, chr, 0); SetMod(nrow - 1, ncol - 1, chr, 1); SetMod(0, ncol - 3, chr, 2);
                SetMod(0, ncol - 2, chr, 3); SetMod(0, ncol - 1, chr, 4); SetMod(1, ncol - 3, chr, 5);
                SetMod(1, ncol - 2, chr, 6); SetMod(1, ncol - 1, chr, 7); chr++;
            }

            while (true)
            {
                if (rr < nrow && cc >= 0 && arr[rr, cc] == -1) { Utah(rr, cc, chr); chr++; }
                rr -= 2; cc += 2;
                if (!(rr >= 0 && cc < ncol)) break;
            }
            rr += 1; cc += 3;
            while (true)
            {
                if (rr >= 0 && cc < ncol && arr[rr, cc] == -1) { Utah(rr, cc, chr); chr++; }
                rr += 2; cc -= 2;
                if (!(rr < nrow && cc >= 0)) break;
            }
            rr += 3; cc += 1;
            if (rr >= nrow && cc >= ncol) break;
        }

        int[] keep = new int[28];
        int ki = 0;
        for (int i = 0; i < 32; i++)
            if (i != 0 && i != 15 && i != 16 && i != 31)
                keep[ki++] = i;

        for (int r = 0; r < 28; r++)
        {
            for (int c = 0; c < 28; c++)
            {
                int v = arr[r, c];
                if (v >= 0)
                {
                    BitR32[v >> 3, v & 7] = keep[r];
                    BitC32[v >> 3, v & 7] = keep[c];
                }
            }
        }
    }

    private static void InitGaloisAndBinaryGenerator()
    {
        int x = 1;
        GfLog[0] = -1;
        for (int i = 0; i < 255; i++)
        {
            GfExp[i] = (byte)x;
            GfExp[i + 255] = (byte)x;
            GfLog[x] = (short)i;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= 0x12d;
        }
        for (int a = 0; a < 256; a++)
            for (int b = 0; b < 256; b++)
                GfMulTab[(a << 8) | b] = (a != 0 && b != 0) ? GfExp[GfLog[a] + GfLog[b]] : (byte)0;

        byte[] gHi = new byte[37];
        gHi[0] = 1;
        for (int i = 0; i < 36; i++)
        {
            byte root = GfExp[i + 1];
            for (int j = i + 1; j >= 1; j--)
                gHi[j] ^= GfMulTab[(gHi[j - 1] << 8) | root];
        }

        byte[] cw = new byte[98];
        byte[] rem = new byte[36];
        for (int row = 0; row < 496; row++)
        {
            Array.Clear(cw, 0, 98);
            Array.Clear(rem, 0, 36);
            cw[row >> 3] = (byte)(1u << (7 - (row & 7)));
            for (int i = 0; i < 62; i++)
            {
                byte fb = (byte)(cw[i] ^ rem[0]);
                Buffer.BlockCopy(rem, 1, rem, 0, 35);
                rem[35] = 0;
                if (fb != 0)
                {
                    int fbOff = fb << 8;
                    for (int j = 0; j < 36; j++)
                        rem[j] ^= GfMulTab[fbOff | gHi[j + 1]];
                }
            }
            Buffer.BlockCopy(rem, 0, cw, 62, 36);
            int rOff = row * 784;
            for (int col = 0; col < 784; col++)
                GBin[rOff + col] = (byte)((cw[col >> 3] >> (7 - (col & 7))) & 1);
        }
    }

    private static double ErfApprox(double x)
    {
        double sign = x < 0 ? -1.0 : 1.0;
        x = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.3275911 * x);
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return sign * y;
    }

    private static float InitKroneckerPsfSolver(double sMod, double hwMod, double reg)
    {
        double s2 = Math.Sqrt(2.0) * (sMod * SRect);
        double hw = hwMod * SRect;
        double[,] ax = new double[WRect, 32];
        for (int i = 0; i < WRect; i++)
        {
            for (int j = 0; j < 32; j++)
            {
                double cs = PadRect + (j + 0.5) * SRect;
                double dx = i - cs;
                double v = 0.5 * (ErfApprox((dx + hw) / s2) - ErfApprox((dx - hw) / s2));
                ax[i, j] = v;
                PsfAx[i * 32 + j] = (float)v;
            }
        }

        double[,] g = new double[32, 32];
        for (int i = 0; i < 32; i++)
        {
            for (int j = 0; j < 32; j++)
            {
                double s = 0;
                for (int k = 0; k < WRect; k++) s += ax[k, i] * ax[k, j];
                g[i, j] = s;
                PsfG[i, j] = (float)s;
            }
        }

        double[,] a = (double[,])g.Clone();
        double[,] q = new double[32, 32];
        for (int i = 0; i < 32; i++) q[i, i] = 1.0;

        for (int sweep = 0; sweep < 60; sweep++)
        {
            double off = 0;
            for (int p = 0; p < 32; p++)
                for (int r = p + 1; r < 32; r++)
                    off += a[p, r] * a[p, r];
            if (off < 1e-24) break;

            for (int p = 0; p < 31; p++)
            {
                for (int r = p + 1; r < 32; r++)
                {
                    double apr = a[p, r];
                    if (Math.Abs(apr) < 1e-15) continue;
                    double tau = (a[r, r] - a[p, p]) / (2.0 * apr);
                    double t = (tau >= 0 ? 1.0 : -1.0) / (Math.Abs(tau) + Math.Sqrt(1.0 + tau * tau));
                    double c = 1.0 / Math.Sqrt(1.0 + t * t);
                    double s = t * c;

                    a[p, p] -= t * apr;
                    a[r, r] += t * apr;
                    a[p, r] = a[r, p] = 0.0;

                    for (int k = 0; k < 32; k++)
                    {
                        if (k != p && k != r)
                        {
                            double akp = a[k, p], akr = a[k, r];
                            a[k, p] = a[p, k] = c * akp - s * akr;
                            a[k, r] = a[r, k] = s * akp + c * akr;
                        }
                        double qkp = q[k, p], qkr = q[k, r];
                        q[k, p] = c * qkp - s * qkr;
                        q[k, r] = s * qkp + c * qkr;
                    }
                }
            }
        }

        double maxEig = 0;
        for (int i = 0; i < 32; i++)
        {
            if (a[i, i] > maxEig) maxEig = a[i, i];
            for (int j = 0; j < 32; j++)
            {
                PsfQ[i, j] = (float)q[i, j];
                PsfDenom[i, j] = (float)(a[i, i] * a[j, j] + reg);
            }
        }
        return (float)(1.0 / (maxEig * maxEig + reg));
    }

    public static float[] SolveKroneckerPsf32(float[] uRect, int nIsta = 30)
    {
        float[] tmp = new float[WRect * 32];
        for (int r = 0; r < WRect; r++)
        {
            int rOff = r * WRect;
            int tOff = r * 32;
            for (int c = 0; c < WRect; c++)
            {
                float u = uRect[rOff + c];
                if (u == 0f) continue;
                int axOff = c * 32;
                for (int j = 0; j < 32; j++)
                    tmp[tOff + j] += u * PsfAx[axOff + j];
            }
        }

        float[,] rhs = new float[32, 32];
        for (int i = 0; i < 32; i++)
        {
            for (int r = 0; r < WRect; r++)
            {
                float axi = PsfAx[r * 32 + i];
                if (axi == 0f) continue;
                int tOff = r * 32;
                for (int j = 0; j < 32; j++)
                    rhs[i, j] += axi * tmp[tOff + j];
            }
        }

        float[,] qtRhs = MatMul32(PsfQ, rhs, transposeA: true, transposeB: false);
        float[,] spec = MatMul32(qtRhs, PsfQ, transposeA: false, transposeB: false);
        for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                spec[i, j] /= PsfDenom[i, j];
        float[,] qSpec = MatMul32(PsfQ, spec, transposeA: false, transposeB: false);
        float[,] m = MatMul32(qSpec, PsfQ, transposeA: false, transposeB: true);

        for (int it = 0; it < nIsta; it++)
        {
            float[,] gm = MatMul32(PsfG, m, false, false);
            float[,] gmg = MatMul32(gm, PsfG, false, false);
            for (int i = 0; i < 32; i++)
            {
                for (int j = 0; j < 32; j++)
                {
                    float grad = gmg[i, j] + PsfReg * m[i, j] - rhs[i, j];
                    float v = m[i, j] - PsfInvLip * grad;
                    m[i, j] = v < -0.10f ? -0.10f : (v > 1.15f ? 1.15f : v);
                }
            }
        }

        float[] out32 = new float[1024];
        for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                out32[i * 32 + j] = m[i, j];
        return out32;
    }

    private static float[,] MatMul32(float[,] a, float[,] b, bool transposeA, bool transposeB)
    {
        float[,] c = new float[32, 32];
        for (int i = 0; i < 32; i++)
        {
            for (int k = 0; k < 32; k++)
            {
                float aik = transposeA ? a[k, i] : a[i, k];
                for (int j = 0; j < 32; j++)
                    c[i, j] += aik * (transposeB ? b[j, k] : b[k, j]);
            }
        }
        return c;
    }

    private static bool CheckEcc200PadStrict(ReadOnlySpan<byte> cw)
    {
        bool padOk = cw[61] == 129;
        for (int p = 4; p <= 57 && !padOk; p++)
        {
            if (cw[p] == 129)
            {
                bool ok = true;
                for (int j = p + 1; j < 62; j++)
                {
                    int r = ((149 * (j + 1)) % 253) + 1;
                    int expected = 129 + r;
                    if (expected > 254) expected -= 254;
                    if (cw[j] != (byte)expected) { ok = false; break; }
                }
                if (ok) padOk = true;
            }
        }
        if (!padOk) return false;

        for (int i = 0; i < 36; i++)
        {
            byte s = 0;
            int alphaOff = GfExp[i + 1] << 8;
            for (int j = 0; j < 98; j++)
                s = (byte)(GfMulTab[alphaOff | s] ^ cw[j]);
            if (s != 0) return false;
        }
        return true;
    }

    private static void ReconstructAndPack(
        int[] perm, byte[] yMrb, ulong[] pPak, ulong[] c0Weak,
        int f1, int f2, int f3, Span<byte> msgOut)
    {
        Span<byte> bitsPerm = stackalloc byte[784];
        yMrb.AsSpan(0, 496).CopyTo(bitsPerm);
        if (f1 >= 0) bitsPerm[f1] ^= 1;
        if (f2 >= 0) bitsPerm[f2] ^= 1;
        if (f3 >= 0) bitsPerm[f3] ^= 1;

        Span<ulong> w = stackalloc ulong[5];
        for (int m = 0; m < 5; m++)
        {
            ulong v = c0Weak[m];
            if (f1 >= 0) v ^= pPak[f1 * 5 + m];
            if (f2 >= 0) v ^= pPak[f2 * 5 + m];
            if (f3 >= 0) v ^= pPak[f3 * 5 + m];
            w[m] = v;
        }
        for (int j = 0; j < 288; j++)
            bitsPerm[496 + j] = (byte)((w[j >> 6] >> (j & 63)) & 1UL);

        Span<byte> bitsOrig = stackalloc byte[784];
        for (int j = 0; j < 784; j++)
            bitsOrig[perm[j]] = bitsPerm[j];

        for (int k = 0; k < 98; k++)
        {
            int bval = 0;
            int baseIdx = k * 8;
            for (int b = 0; b < 8; b++)
                bval = (bval << 1) | bitsOrig[baseIdx + b];
            msgOut[k] = (byte)bval;
        }
    }

    public static unsafe int DecodeBinaryOsd(
        float[] llrs, int maxBitDiffs, int osd3Tail,
        byte[] msgOut, out int outOrder, out int outByteDiffs, out int outBitDiffs)
    {
        outOrder = -1; outByteDiffs = 0; outBitDiffs = 0;

        int[] perm = new int[784];
        float[] rel = new float[784];
        byte[] hardOrig = new byte[784];
        byte[] hardBytes = new byte[98];

        for (int i = 0; i < 784; i++)
        {
            perm[i] = i;
            rel[i] = DpmMathF.Abs(llrs[i]);
            hardOrig[i] = llrs[i] > 0f ? (byte)1 : (byte)0;
        }
        for (int k = 0; k < 98; k++)
        {
            int v = 0;
            for (int b = 0; b < 8; b++) v = (v << 1) | hardOrig[k * 8 + b];
            hardBytes[k] = (byte)v;
        }
        for (int i = 1; i < 784; i++)
        {
            int key = perm[i];
            float kr = rel[key];
            int j = i - 1;
            while (j >= 0 && rel[perm[j]] < kr)
            {
                perm[j + 1] = perm[j];
                j--;
            }
            perm[j + 1] = key;
        }

        // 一维扁平 496 x 13 ulong 位并行高斯-若尔当消元
        ulong[] mat = new ulong[496 * 13];
        fixed (byte* pG = GBin)
        fixed (ulong* pM = mat)
        {
            for (int r = 0; r < 496; r++)
            {
                byte* gRow = pG + r * 784;
                ulong* mRow = pM + r * 13;
                for (int c = 0; c < 784; c++)
                {
                    if (gRow[perm[c]] != 0)
                        mRow[c >> 6] |= 1UL << (c & 63);
                }
            }

            for (int col = 0; col < 496; col++)
            {
                int wIdx = col >> 6;
                ulong bitMask = 1UL << (col & 63);
                int piv = -1;
                for (int r = col; r < 496; r++)
                {
                    if ((pM[r * 13 + wIdx] & bitMask) != 0) { piv = r; break; }
                }
                if (piv < 0)
                {
                    int swapC = -1;
                    for (int sc = 496; sc < 784; sc++)
                    {
                        int sw = sc >> 6;
                        ulong sm = 1UL << (sc & 63);
                        for (int r = col; r < 496; r++)
                        {
                            if ((pM[r * 13 + sw] & sm) != 0) { swapC = sc; piv = r; break; }
                        }
                        if (swapC >= 0) break;
                    }
                    if (swapC < 0) return -1;
                    (perm[col], perm[swapC]) = (perm[swapC], perm[col]);
                    int sw2 = swapC >> 6;
                    ulong sm2 = 1UL << (swapC & 63);
                    for (int r = 0; r < 496; r++)
                    {
                        ulong* mRow = pM + r * 13;
                        bool b1 = (mRow[wIdx] & bitMask) != 0;
                        bool b2 = (mRow[sw2] & sm2) != 0;
                        if (b1 != b2)
                        {
                            mRow[wIdx] ^= bitMask;
                            mRow[sw2] ^= sm2;
                        }
                    }
                }
                if (piv != col)
                {
                    ulong* rCol = pM + col * 13;
                    ulong* rPiv = pM + piv * 13;
                    for (int w = 0; w < 13; w++)
                    {
                        ulong tmp = rCol[w]; rCol[w] = rPiv[w]; rPiv[w] = tmp;
                    }
                }
                ulong* pRow = pM + col * 13;
                for (int r = 0; r < 496; r++)
                {
                    if (r != col)
                    {
                        ulong* mRow = pM + r * 13;
                        if ((mRow[wIdx] & bitMask) != 0)
                        {
                            for (int w = wIdx; w < 13; w++)
                                mRow[w] ^= pRow[w];
                        }
                    }
                }
            }
        }

        ulong[] pPak = new ulong[496 * 5];
        const ulong tailMask4 = (1UL << 32) - 1UL;
        for (int r = 0; r < 496; r++)
        {
            int mOff = r * 13, pOff = r * 5;
            for (int m = 0; m < 4; m++)
                pPak[pOff + m] = (mat[mOff + 7 + m] >> 48) | (mat[mOff + 8 + m] << 16);
            pPak[pOff + 4] = ((mat[mOff + 11] >> 48) | (mat[mOff + 12] << 16)) & tailMask4;
        }

        byte[] yMrb = new byte[496];
        ulong[] yWeak = new ulong[5];
        ulong[] c0Weak = new ulong[5];
        for (int i = 0; i < 496; i++)
        {
            byte b = hardOrig[perm[i]];
            yMrb[i] = b;
            if (b != 0)
            {
                int pOff = i * 5;
                for (int m = 0; m < 5; m++) c0Weak[m] ^= pPak[pOff + m];
            }
        }
        for (int j = 0; j < 288; j++)
        {
            if (hardOrig[perm[496 + j]] != 0)
                yWeak[j >> 6] |= 1UL << (j & 63);
        }
        ulong d00 = yWeak[0] ^ c0Weak[0];
        ulong d01 = yWeak[1] ^ c0Weak[1];
        ulong d02 = yWeak[2] ^ c0Weak[2];
        ulong d03 = yWeak[3] ^ c0Weak[3];
        ulong d04 = yWeak[4] ^ c0Weak[4];

        int Pop5(ulong a0, ulong a1, ulong a2, ulong a3, ulong a4) =>
            DpmBitOps.PopCount(a0) + DpmBitOps.PopCount(a1) +
            DpmBitOps.PopCount(a2) + DpmBitOps.PopCount(a3) + DpmBitOps.PopCount(a4);

        // OSD-0
        int w0 = Pop5(d00, d01, d02, d03, d04);
        if (w0 <= maxBitDiffs)
        {
            ReconstructAndPack(perm, yMrb, pPak, c0Weak, -1, -1, -1, msgOut);
            if (CheckEcc200PadStrict(msgOut))
            {
                outOrder = 0; outBitDiffs = w0; outByteDiffs = CountByteDiffs(msgOut, hardBytes);
                return 0;
            }
        }

        const int limPrefixW0 = 18;
        const int limPrefixW01 = 31;

        fixed (ulong* pP = pPak)
        {
            // OSD-1
            int lim1 = maxBitDiffs - 1;
            for (int i = 0; i < 496; i++)
            {
                ulong* pi = pP + i * 5;
                int wt = Pop5(d00 ^ pi[0], d01 ^ pi[1], d02 ^ pi[2], d03 ^ pi[3], d04 ^ pi[4]);
                if (wt <= lim1)
                {
                    ReconstructAndPack(perm, yMrb, pPak, c0Weak, i, -1, -1, msgOut);
                    if (CheckEcc200PadStrict(msgOut))
                    {
                        outOrder = 1; outBitDiffs = wt + 1; outByteDiffs = CountByteDiffs(msgOut, hardBytes);
                        return 1;
                    }
                }
            }

            // OSD-2
            int lim2 = maxBitDiffs - 2;
            for (int i = 0; i < 496; i++)
            {
                ulong* pi = pP + i * 5;
                ulong x0 = d00 ^ pi[0], x1 = d01 ^ pi[1];
                ulong x2 = d02 ^ pi[2], x3 = d03 ^ pi[3], x4 = d04 ^ pi[4];
                for (int j = i + 1; j < 496; j++)
                {
                    ulong* pj = pP + j * 5;
                    int wt0 = DpmBitOps.PopCount(x0 ^ pj[0]);
                    if (wt0 > limPrefixW0) continue;
                    int wt01 = wt0 + DpmBitOps.PopCount(x1 ^ pj[1]);
                    if (wt01 > limPrefixW01) continue;
                    int wt = wt01 + DpmBitOps.PopCount(x2 ^ pj[2]) + DpmBitOps.PopCount(x3 ^ pj[3]) + DpmBitOps.PopCount(x4 ^ pj[4]);
                    if (wt <= lim2)
                    {
                        ReconstructAndPack(perm, yMrb, pPak, c0Weak, i, j, -1, msgOut);
                        if (CheckEcc200PadStrict(msgOut))
                        {
                            outOrder = 2; outBitDiffs = wt + 2; outByteDiffs = CountByteDiffs(msgOut, hardBytes);
                            return 2;
                        }
                    }
                }
            }
        }

        // OSD-3 (并行搜索最弱 osd3Tail 个主基比特)
        if (osd3Tail <= 0) return -1;
        int iStart = Math.Max(2, 496 - osd3Tail);
        int lim3 = maxBitDiffs - 3;
        int foundFlag = 0;
        int bestBitD = 0;
        byte[] bestMsg = new byte[98];
        object sync = new object();

        Parallel.For(0, 496 - iStart, idxOff =>
        {
            if (Volatile.Read(ref foundFlag) != 0) return;
            int i = 495 - idxOff;
            int iOff = i * 5;
            ulong di0 = d00 ^ pPak[iOff], di1 = d01 ^ pPak[iOff + 1];
            ulong di2 = d02 ^ pPak[iOff + 2], di3 = d03 ^ pPak[iOff + 3], di4 = d04 ^ pPak[iOff + 4];
            Span<byte> cand = stackalloc byte[98];

            for (int j = 0; j < i && Volatile.Read(ref foundFlag) == 0; j++)
            {
                int jOff = j * 5;
                ulong dij0 = di0 ^ pPak[jOff], dij1 = di1 ^ pPak[jOff + 1];
                ulong dij2 = di2 ^ pPak[jOff + 2], dij3 = di3 ^ pPak[jOff + 3], dij4 = di4 ^ pPak[jOff + 4];
                for (int k = j + 1; k < i; k++)
                {
                    int kOff = k * 5;
                    int w0c = DpmBitOps.PopCount(dij0 ^ pPak[kOff]);
                    if (w0c > limPrefixW0) continue;
                    int w01c = w0c + DpmBitOps.PopCount(dij1 ^ pPak[kOff + 1]);
                    if (w01c > limPrefixW01) continue;
                    int wt = w01c + DpmBitOps.PopCount(dij2 ^ pPak[kOff + 2]) + DpmBitOps.PopCount(dij3 ^ pPak[kOff + 3]) + DpmBitOps.PopCount(dij4 ^ pPak[kOff + 4]);
                    if (wt <= lim3)
                    {
                        ReconstructAndPack(perm, yMrb, pPak, c0Weak, i, j, k, cand);
                        if (CheckEcc200PadStrict(cand))
                        {
                            lock (sync)
                            {
                                if (foundFlag == 0)
                                {
                                    foundFlag = 1;
                                    cand.CopyTo(bestMsg);
                                    bestBitD = wt + 3;
                                }
                            }
                            return;
                        }
                    }
                }
            }
        });

        if (foundFlag != 0)
        {
            Buffer.BlockCopy(bestMsg, 0, msgOut, 0, 98);
            outOrder = 3;
            outBitDiffs = bestBitD;
            outByteDiffs = CountByteDiffs(msgOut, hardBytes);
            return 3;
        }
        return -1;
    }

    private static int CountByteDiffs(byte[] a, byte[] b)
    {
        int d = 0;
        for (int i = 0; i < 98; i++) if (a[i] != b[i]) d++;
        return d;
    }

    public static byte[] CodewordsToBits32(byte[] cws)
    {
        byte[] b32 = new byte[1024];
        for (int k = 0; k < 98; k++)
        {
            byte v = cws[k];
            for (int b = 0; b < 8; b++)
                b32[BitR32[k, b] * 32 + BitC32[k, b]] = (byte)((v >> (7 - b)) & 1);
        }
        ApplyEcc200Clock32(b32);
        return b32;
    }

    public static void ApplyEcc200Clock32(byte[] b32)
    {
        for (int i = 0; i < 32; i++)
        {
            b32[15 * 32 + i] = 1;
            b32[31 * 32 + i] = 1;
            b32[i * 32 + 0] = 1;
            b32[i * 32 + 16] = 1;
            byte alt0 = (byte)(i % 2 == 0 ? 1 : 0);
            byte alt1 = (byte)(1 - alt0);
            b32[0 * 32 + i] = alt0;
            b32[16 * 32 + i] = alt0;
            b32[i * 32 + 15] = alt1;
            b32[i * 32 + 31] = alt1;
        }
    }
}
}
