#pragma once
namespace dlcv_infer { namespace flow { namespace dpm { namespace ecc {
constexpr int SRect=6, PadRect=18, WRect=228;
using Matrix=std::array<std::array<float,32>,32>;
struct Tables {
    std::array<std::array<int,8>,98> BitR32{}, BitC32{};
    std::vector<float> PsfAx=std::vector<float>(WRect*32);
    Matrix PsfG{}, PsfQ{}, PsfDenom{};
    float PsfInvLip=0;
    static constexpr float PsfReg=.14f;
    Tables() { InitUtahPlacement32(); PsfInvLip=InitKroneckerPsfSolver(.48,.50,PsfReg); }
    void InitUtahPlacement32()
    {
        const int nrow = 28, ncol = 28;
        std::array<std::array<int, ncol>, nrow> arr{};
        for (int i = 0; i < nrow; i++)
            for (int j = 0; j < ncol; j++)
                arr[i][j] = -1;

        auto SetMod = [&](int r, int c, int chIdx, int bit)
        {
            if (r < 0) { r += nrow; c += 4 - ((nrow + 4) % 8); }
            if (c < 0) { c += ncol; r += 4 - ((ncol + 4) % 8); }
            arr[r][c] = chIdx * 8 + bit;
        };

        auto Utah = [&](int r, int c, int chIdx)
        {
            SetMod(r - 2, c - 2, chIdx, 0); SetMod(r - 2, c - 1, chIdx, 1);
            SetMod(r - 1, c - 2, chIdx, 2); SetMod(r - 1, c - 1, chIdx, 3); SetMod(r - 1, c, chIdx, 4);
            SetMod(r, c - 2, chIdx, 5);     SetMod(r, c - 1, chIdx, 6);     SetMod(r, c, chIdx, 7);
        };

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
                if (rr < nrow && cc >= 0 && arr[rr][cc] == -1) { Utah(rr, cc, chr); chr++; }
                rr -= 2; cc += 2;
                if (!(rr >= 0 && cc < ncol)) break;
            }
            rr += 1; cc += 3;
            while (true)
            {
                if (rr >= 0 && cc < ncol && arr[rr][cc] == -1) { Utah(rr, cc, chr); chr++; }
                rr += 2; cc -= 2;
                if (!(rr < nrow && cc >= 0)) break;
            }
            rr += 3; cc += 1;
            if (rr >= nrow && cc >= ncol) break;
        }

        std::vector<int> keep(28);
        int ki = 0;
        for (int i = 0; i < 32; i++)
            if (i != 0 && i != 15 && i != 16 && i != 31)
                keep[ki++] = i;

        for (int r = 0; r < 28; r++)
        {
            for (int c = 0; c < 28; c++)
            {
                int v = arr[r][c];
                if (v >= 0)
                {
                    BitR32[v >> 3][v & 7] = keep[r];
                    BitC32[v >> 3][v & 7] = keep[c];
                }
            }
        }
    }

    double ErfApprox(double x)
    {
        double sign = x < 0 ? -1.0 : 1.0;
        x = std::abs(x);
        double t = 1.0 / (1.0 + 0.3275911 * x);
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * std::exp(-x * x);
        return sign * y;
    }

    float InitKroneckerPsfSolver(double sMod, double hwMod, double reg)
    {
        double s2 = std::sqrt(2.0) * (sMod * SRect);
        double hw = hwMod * SRect;
        std::array<std::array<double, 32>, WRect> ax{};
        for (int i = 0; i < WRect; i++)
        {
            for (int j = 0; j < 32; j++)
            {
                double cs = PadRect + (j + 0.5) * SRect;
                double dx = i - cs;
                double v = 0.5 * (ErfApprox((dx + hw) / s2) - ErfApprox((dx - hw) / s2));
                ax[i][j] = v;
                PsfAx[i * 32 + j] = (float)v;
            }
        }

        std::array<std::array<double, 32>, 32> g{};
        for (int i = 0; i < 32; i++)
        {
            for (int j = 0; j < 32; j++)
            {
                double s = 0;
                for (int k = 0; k < WRect; k++) s += ax[k][i] * ax[k][j];
                g[i][j] = s;
                PsfG[i][j] = (float)s;
            }
        }

        auto a = g;
        std::array<std::array<double, 32>, 32> q{};
        for (int i = 0; i < 32; i++) q[i][i] = 1.0;

        for (int sweep = 0; sweep < 60; sweep++)
        {
            double off = 0;
            for (int p = 0; p < 32; p++)
                for (int r = p + 1; r < 32; r++)
                    off += a[p][r] * a[p][r];
            if (off < 1e-24) break;

            for (int p = 0; p < 31; p++)
            {
                for (int r = p + 1; r < 32; r++)
                {
                    double apr = a[p][r];
                    if (std::abs(apr) < 1e-15) continue;
                    double tau = (a[r][r] - a[p][p]) / (2.0 * apr);
                    double t = (tau >= 0 ? 1.0 : -1.0) / (std::abs(tau) + std::sqrt(1.0 + tau * tau));
                    double c = 1.0 / std::sqrt(1.0 + t * t);
                    double s = t * c;

                    a[p][p] -= t * apr;
                    a[r][r] += t * apr;
                    a[p][r] = a[r][p] = 0.0;

                    for (int k = 0; k < 32; k++)
                    {
                        if (k != p && k != r)
                        {
                            double akp = a[k][p], akr = a[k][r];
                            a[k][p] = a[p][k] = c * akp - s * akr;
                            a[k][r] = a[r][k] = s * akp + c * akr;
                        }
                        double qkp = q[k][p], qkr = q[k][r];
                        q[k][p] = c * qkp - s * qkr;
                        q[k][r] = s * qkp + c * qkr;
                    }
                }
            }
        }

        double maxEig = 0;
        for (int i = 0; i < 32; i++)
        {
            if (a[i][i] > maxEig) maxEig = a[i][i];
            for (int j = 0; j < 32; j++)
            {
                PsfQ[i][j] = (float)q[i][j];
                PsfDenom[i][j] = (float)(a[i][i] * a[j][j] + reg);
            }
        }
        return (float)(1.0 / (maxEig * maxEig + reg));
    }

    static Matrix MatMul32(const Matrix& a, const Matrix& b, bool transposeA, bool transposeB)
    {
        std::array<std::array<float, 32>, 32> c{};
        for (int i = 0; i < 32; i++)
        {
            for (int k = 0; k < 32; k++)
            {
                float aik = transposeA ? a[k][i] : a[i][k];
                for (int j = 0; j < 32; j++)
                    c[i][j] += aik * (transposeB ? b[j][k] : b[k][j]);
            }
        }
        return c;
    }

    std::vector<float> SolveKroneckerPsf32(const std::vector<float>& uRect, int nIsta = 30) const
    {
        std::vector<float> tmp(WRect * 32);
        for (int r = 0; r < WRect; r++)
        {
            int rOff = r * WRect;
            int tOff = r * 32;
            for (int c = 0; c < WRect; c++)
            {
                float u = uRect[rOff + c];
                if (u == 0.f) continue;
                int axOff = c * 32;
                for (int j = 0; j < 32; j++)
                    tmp[tOff + j] += u * PsfAx[axOff + j];
            }
        }

        std::array<std::array<float, 32>, 32> rhs{};
        for (int i = 0; i < 32; i++)
        {
            for (int r = 0; r < WRect; r++)
            {
                float axi = PsfAx[r * 32 + i];
                if (axi == 0.f) continue;
                int tOff = r * 32;
                for (int j = 0; j < 32; j++)
                    rhs[i][j] += axi * tmp[tOff + j];
            }
        }

        auto qtRhs = MatMul32(PsfQ, rhs, true, false);
        auto spec = MatMul32(qtRhs, PsfQ, false, false);
        for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                spec[i][j] /= PsfDenom[i][j];
        auto qSpec = MatMul32(PsfQ, spec, false, false);
        auto m = MatMul32(qSpec, PsfQ, false, true);

        for (int it = 0; it < nIsta; it++)
        {
            auto gm = MatMul32(PsfG, m, false, false);
            auto gmg = MatMul32(gm, PsfG, false, false);
            for (int i = 0; i < 32; i++)
            {
                for (int j = 0; j < 32; j++)
                {
                    float grad = gmg[i][j] + PsfReg * m[i][j] - rhs[i][j];
                    float v = m[i][j] - PsfInvLip * grad;
                    m[i][j] = v < -0.10f ? -0.10f : (v > 1.15f ? 1.15f : v);
                }
            }
        }

        std::vector<float> out32(1024);
        for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                out32[i * 32 + j] = m[i][j];
        return out32;
    }

    static void ApplyEcc200Clock32(std::vector<uint8_t>& b32)
    {
        for (int i = 0; i < 32; i++)
        {
            b32[15 * 32 + i] = 1;
            b32[31 * 32 + i] = 1;
            b32[i * 32 + 0] = 1;
            b32[i * 32 + 16] = 1;
            uint8_t alt0 = (uint8_t)(i % 2 == 0 ? 1 : 0);
            uint8_t alt1 = (uint8_t)(1 - alt0);
            b32[0 * 32 + i] = alt0;
            b32[16 * 32 + i] = alt0;
            b32[i * 32 + 15] = alt1;
            b32[i * 32 + 31] = alt1;
        }
    }

    std::vector<uint8_t> CodewordsToBits32(const std::vector<uint8_t>& cws) const
    {
        std::vector<uint8_t> b32(1024);
        for (int k = 0; k < 98; k++)
        {
            uint8_t v = cws[k];
            for (int b = 0; b < 8; b++)
                b32[BitR32[k][b] * 32 + BitC32[k][b]] = (uint8_t)((v >> (7 - b)) & 1);
        }
        ApplyEcc200Clock32(b32);
        return b32;
    }
};
const Tables& Get() { static const Tables tables; return tables; }
int DecodeBinaryOsd(const std::vector<float>& llrs, int limit, int tail, std::vector<uint8_t>& msg, int& order, int& bd, int& bits) {
    order=-1; bd=0; bits=0;
    return rs36_binary_osd_decode(llrs.data(),limit,tail,msg.data(),&order,&bd,&bits);
}
}}}}
