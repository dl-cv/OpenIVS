#pragma once
// 与 C# DPM 的 float32 几何搜索及 ECC200 候选次序保持一致。
#include <cstdint>
#include <cstring>
#include <cmath>
#include <intrin.h>
#define DPM_POPCOUNT64(x) static_cast<int>(__popcnt64(x))
namespace dlcv_infer { namespace flow { namespace dpm {
static int solve_homography_4pts(const float src[8], const float dst[8], double H[9]) {
    double A[8][9];
    for (int i = 0; i < 4; i++) {
        double sx = src[2 * i], sy = src[2 * i + 1];
        double dx = dst[2 * i], dy = dst[2 * i + 1];
        A[2 * i][0] = sx; A[2 * i][1] = sy; A[2 * i][2] = 1.0;
        A[2 * i][3] = 0.0; A[2 * i][4] = 0.0; A[2 * i][5] = 0.0;
        A[2 * i][6] = -sx * dx; A[2 * i][7] = -sy * dx; A[2 * i][8] = dx;

        A[2 * i + 1][0] = 0.0; A[2 * i + 1][1] = 0.0; A[2 * i + 1][2] = 0.0;
        A[2 * i + 1][3] = sx; A[2 * i + 1][4] = sy; A[2 * i + 1][5] = 1.0;
        A[2 * i + 1][6] = -sx * dy; A[2 * i + 1][7] = -sy * dy; A[2 * i + 1][8] = dy;
    }
    for (int col = 0; col < 8; col++) {
        int piv = col;
        double max_v = fabs(A[col][col]);
        for (int row = col + 1; row < 8; row++) {
            double v = fabs(A[row][col]);
            if (v > max_v) { max_v = v; piv = row; }
        }
        if (max_v < 1e-12) return -1;
        if (piv != col) {
            for (int k = col; k <= 8; k++) {
                double tmp = A[col][k]; A[col][k] = A[piv][k]; A[piv][k] = tmp;
            }
        }
        double inv_p = 1.0 / A[col][col];
        for (int row = col + 1; row < 8; row++) {
            double f = A[row][col] * inv_p;
            for (int k = col + 1; k <= 8; k++) A[row][k] -= f * A[col][k];
        }
    }
    for (int row = 7; row >= 0; row--) {
        double s = A[row][8];
        for (int k = row + 1; k < 8; k++) s -= A[row][k] * H[k];
        H[row] = s / A[row][row];
    }
    H[8] = 1.0;
    return 0;
}

static inline float sample_bilinear(const float *img, int h, int w, float x, float y) {
    if (x < 0.0f || y < 0.0f || x >= (float)(w - 1) || y >= (float)(h - 1)) return 0.0f;
    int ix = (int)x, iy = (int)y;
    float fx = x - (float)ix, fy = y - (float)iy;
    const float *row0 = img + iy * w + ix;
    const float *row1 = row0 + w;
    float v0 = row0[0] + fx * (row0[1] - row0[0]);
    float v1 = row1[0] + fx * (row1[1] - row1[0]);
    return v0 + fy * (v1 - v0);
}

static inline void edge_to_corners(const float e[8], float c[8]) {
    c[0] = e[0] + 0.5f * e[4]; c[1] = e[2] - 0.5f * e[6];
    c[2] = e[1] + 0.5f * e[5]; c[3] = e[2] + 0.5f * e[6];
    c[4] = e[1] - 0.5f * e[5]; c[5] = e[3] + 0.5f * e[7];
    c[6] = e[0] - 0.5f * e[4]; c[7] = e[3] - 0.5f * e[7];
}

static inline void corners_to_edge(const float c[8], float e[8]) {
    e[0] = 0.5f * (c[0] + c[6]);
    e[1] = 0.5f * (c[2] + c[4]);
    e[2] = 0.5f * (c[1] + c[3]);
    e[3] = 0.5f * (c[7] + c[5]);
    e[4] = c[0] - c[6];
    e[5] = c[2] - c[4];
    e[6] = c[3] - c[1];
    e[7] = c[5] - c[7];
}

static double eval_loss_edge(
    const float *filt_clk, int h, int w, int n_mod,
    const float src_c[8], const float e[8]
) {
    float dst[8];
    edge_to_corners(e, dst);
    double H[9];
    if (solve_homography_4pts(src_c, dst, H) != 0) return 1e9;

    int ext_n = n_mod + 2;
    float ve[52 * 52];
    for (int r = -1; r <= n_mod; r++) {
        for (int c = -1; c <= n_mod; c++) {
            double denom = H[6] * c + (H[7] * r + 1.0);
            float px = (float)((H[0] * c + (H[1] * r + H[2])) / denom);
            float py = (float)((H[3] * c + (H[4] * r + H[5])) / denom);
            ve[(r + 1) * ext_n + (c + 1)] = sample_bilinear(filt_clk, h, w, px, py) - 0.52f;
        }
    }

    double qz_sum = 0.0;
    for (int i = 1; i <= n_mod; i++) {
        qz_sum += ve[0 * ext_n + i] + ve[(n_mod + 1) * ext_n + i];
        qz_sum += ve[i * ext_n + 0] + ve[i * ext_n + (n_mod + 1)];
    }
    double qz_pen = -qz_sum / (double)n_mod;

    double s_bot = 0.0, s_lft = 0.0, s_top = 0.0, s_rgt = 0.0;
    for (int i = 0; i < n_mod; i++) {
        s_bot += ve[n_mod * ext_n + (i + 1)];
        s_lft += ve[(i + 1) * ext_n + 1];
        double sgn_r0 = (i % 2 == 0) ? 1.0 : -1.0;
        double sgn_cr = (i % 2 == 1) ? 1.0 : -1.0;
        s_top += ve[1 * ext_n + (i + 1)] * sgn_r0;
        s_rgt += ve[(i + 1) * ext_n + n_mod] * sgn_cr;
    }
    double inv_n = 1.0 / (double)n_mod;
    double sc = (s_bot + s_lft + 1.4 * (s_top + s_rgt)) * inv_n;

    if (n_mod == 32 || n_mod == 36) {
        int half = n_mod / 2;
        double c_hm1 = 0.0, c_h = 0.0, c_alt_r = 0.0, c_alt_c = 0.0;
        for (int i = 0; i < n_mod; i++) {
            c_hm1 += ve[half * ext_n + (i + 1)];
            c_h   += ve[(i + 1) * ext_n + (half + 1)];
            double sgn_r0 = (i % 2 == 0) ? 1.0 : -1.0;
            double sgn_cr = (i % 2 == 1) ? 1.0 : -1.0;
            c_alt_r += ve[(half + 1) * ext_n + (i + 1)] * sgn_r0;
            c_alt_c += ve[(i + 1) * ext_n + half] * sgn_cr;
        }
        sc += (c_hm1 + c_h + 1.4 * (c_alt_r + c_alt_c)) * inv_n;
    }

    double eye_sum = 0.0;
    for (int r = 1; r <= n_mod; r++) {
        for (int c = 1; c <= n_mod; c++) {
            double av = fabs((double)ve[r * ext_n + c]);
            eye_sum += (av < 0.35) ? av : 0.35;
        }
    }
    double eye = eye_sum / (double)(n_mod * n_mod);
    return -(sc + 0.9 * qz_pen + 1.5 * eye);
}

void optimize_clock_8dof_c(
    const float *filt_clk, int h, int w, int n_mod,
    const float c_init[8], float mod_px, float out_dst[8]
) {
    float src_c[8] = {
        0.0f, 0.0f,
        (float)(n_mod - 1), 0.0f,
        (float)(n_mod - 1), (float)(n_mod - 1),
        0.0f, (float)(n_mod - 1)
    };
    float e_init[8];
    corners_to_edge(c_init, e_init);

    float lo[8], hi[8];
    for (int k = 0; k < 4; k++) {
        lo[k] = e_init[k] - 1.6f * mod_px;
        hi[k] = e_init[k] + 1.6f * mod_px;
    }
    for (int k = 4; k < 8; k++) {
        lo[k] = e_init[k] - 1.8f * mod_px;
        hi[k] = e_init[k] + 1.8f * mod_px;
    }

    float best_e[8];
    memcpy(best_e, e_init, sizeof(best_e));
    double best_loss = eval_loss_edge(filt_clk, h, w, n_mod, src_c, best_e);

    for (int pass = 0; pass < 2; pass++) {
        for (int k = 0; k < 8; k++) {
            float best_val = best_e[k];
            float span = (k < 4) ? 1.35f * mod_px : 1.15f * mod_px;
            for (int s = -7; s <= 7; s++) {
                float v = e_init[k] + (s / 7.0f) * span;
                if (v < lo[k] || v > hi[k]) continue;
                best_e[k] = v;
                double l = eval_loss_edge(filt_clk, h, w, n_mod, src_c, best_e);
                if (l < best_loss) { best_loss = l; best_val = v; }
            }
            best_e[k] = best_val;
        }
    }

    // 按 C# 顺序评估九个种子；同分时保留先找到的候选。
    float center_e[8];
    memcpy(center_e, best_e, sizeof(best_e));


    {
        float loc_best_e[8];
        memcpy(loc_best_e, best_e, sizeof(best_e));
        double loc_best_l = best_loss;

        int s_xr, s_yt;

        for (s_xr = -1; s_xr <= 1; s_xr++) {
            for (s_yt = -1; s_yt <= 1; s_yt++) {
                float e[8];
                memcpy(e, center_e, sizeof(e));
                e[1] += s_xr * 0.40f * mod_px;
                e[2] += s_yt * 0.40f * mod_px;
                for (int k = 0; k < 4; k++) {
                    if (e[k] < lo[k]) e[k] = lo[k];
                    if (e[k] > hi[k]) e[k] = hi[k];
                }
                double l = eval_loss_edge(filt_clk, h, w, n_mod, src_c, e);
                float step = 0.24f * mod_px;
                for (int it = 0; it < 4; it++) {
                    for (int d = 0; d < 8; d++) {
                        float st = (d < 4) ? step : step * 1.4f;
                        float orig = e[d];
                        if (orig + st <= hi[d]) {
                            e[d] = orig + st;
                            double lp = eval_loss_edge(filt_clk, h, w, n_mod, src_c, e);
                            if (lp < l) { l = lp; continue; }
                        }
                        if (orig - st >= lo[d]) {
                            e[d] = orig - st;
                            double lm = eval_loss_edge(filt_clk, h, w, n_mod, src_c, e);
                            if (lm < l) { l = lm; continue; }
                        }
                        e[d] = orig;
                    }
                    step *= 0.55f;
                }
                if (l < loc_best_l) {
                    loc_best_l = l;
                    memcpy(loc_best_e, e, sizeof(e));
                }
            }
        }

        {
            if (loc_best_l < best_loss) {
                best_loss = loc_best_l;
                memcpy(best_e, loc_best_e, sizeof(best_e));
            }
        }
    }

    float step = 0.10f * mod_px;
    float base_e[8];
    memcpy(base_e, best_e, sizeof(best_e));
    while (step > 0.004f) {
        float cand[8];
        memcpy(cand, best_e, sizeof(cand));
        int improved = 0;
        for (int d = 0; d < 8; d++) {
            float st = (d < 4) ? step : step * 1.4f;
            float orig = cand[d];
            if (orig + st <= hi[d]) {
                cand[d] = orig + st;
                double lp = eval_loss_edge(filt_clk, h, w, n_mod, src_c, cand);
                if (lp < best_loss) { best_loss = lp; improved = 1; continue; }
            }
            if (orig - st >= lo[d]) {
                cand[d] = orig - st;
                double lm = eval_loss_edge(filt_clk, h, w, n_mod, src_c, cand);
                if (lm < best_loss) { best_loss = lm; improved = 1; continue; }
            }
            cand[d] = orig;
        }
        if (improved) {
            float pat[8];
            for (int d = 0; d < 8; d++) {
                pat[d] = cand[d] + 0.75f * (cand[d] - base_e[d]);
                if (pat[d] < lo[d]) pat[d] = lo[d];
                if (pat[d] > hi[d]) pat[d] = hi[d];
            }
            memcpy(base_e, cand, sizeof(cand));
            memcpy(best_e, cand, sizeof(cand));
            double l_pat = eval_loss_edge(filt_clk, h, w, n_mod, src_c, pat);
            if (l_pat < best_loss) {
                best_loss = l_pat;
                memcpy(best_e, pat, sizeof(pat));
            }
        } else {
            step *= 0.50f;
            memcpy(base_e, best_e, sizeof(best_e));
        }
    }
    edge_to_corners(best_e, out_dst);
}

/* =========================================================================
 * Module 2: Deterministic GF(2) Binary Ordered Statistics Decoder (OSD-0/1/2/3)
 * ========================================================================= */

struct EccTables {
uint8_t gf_exp[512];
int16_t gf_log[256];
uint8_t gf_mul_tab[256][256];
uint8_t G_BIN[496][784];


EccTables() {
    int x = 1;
    gf_log[0] = -1;
    for (int i = 0; i < 255; i++) {
        gf_exp[i] = (uint8_t)x;
        gf_exp[i + 255] = (uint8_t)x;
        gf_log[x] = (int16_t)i;
        x <<= 1;
        if (x & 0x100) x ^= 0x12d;
    }
    for (int a = 0; a < 256; a++)
        for (int b = 0; b < 256; b++)
            gf_mul_tab[a][b] = (a && b) ? gf_exp[gf_log[a] + gf_log[b]] : 0;

    uint8_t g_hi[37];
    memset(g_hi, 0, sizeof(g_hi));
    g_hi[0] = 1;
    for (int i = 0; i < 36; i++) {
        uint8_t root = gf_exp[i + 1];
        for (int j = i + 1; j >= 1; j--) {
            g_hi[j] ^= gf_mul_tab[g_hi[j - 1]][root];
        }
    }

    for (int row = 0; row < 496; row++) {
        uint8_t cw[98], rem[36];
        memset(cw, 0, sizeof(cw));
        memset(rem, 0, sizeof(rem));
        cw[row >> 3] = (uint8_t)(1u << (7 - (row & 7)));
        for (int i = 0; i < 62; i++) {
            uint8_t fb = cw[i] ^ rem[0];
            memmove(&rem[0], &rem[1], 35);
            rem[35] = 0;
            if (fb) {
                const uint8_t *mt = gf_mul_tab[fb];
                for (int j = 0; j < 36; j++) rem[j] ^= mt[g_hi[j + 1]];
            }
        }
        memcpy(&cw[62], rem, 36);
        for (int col = 0; col < 784; col++) {
            G_BIN[row][col] = (cw[col >> 3] >> (7 - (col & 7))) & 1u;
        }
    }

}

};
static const EccTables& ecc_tables() { static const EccTables tables; return tables; }
static int check_ecc200_pad_strict(const uint8_t *cw) {
    const auto& gf_mul_tab = ecc_tables().gf_mul_tab;
    const auto& gf_exp = ecc_tables().gf_exp;
    int pad_ok = 0;
    if (cw[61] == 129) pad_ok = 1;
    for (int p = 4; p <= 57 && !pad_ok; p++) {
        if (cw[p] == 129) {
            int ok = 1;
            for (int j = p + 1; j < 62; j++) {
                int r = ((149 * (j + 1)) % 253) + 1;
                int expected = 129 + r;
                if (expected > 254) expected -= 254;
                if (cw[j] != (uint8_t)expected) { ok = 0; break; }
            }
            if (ok) pad_ok = 1;
        }
    }
    if (!pad_ok) return 0;
    for (int i = 0; i < 36; i++) {
        uint8_t s = 0;
        const uint8_t *mt = gf_mul_tab[gf_exp[i + 1]];
        for (int j = 0; j < 98; j++) s = mt[s] ^ cw[j];
        if (s != 0) return 0;
    }
    return 1;
}

static void reconstruct_and_pack(
    const int perm[784],
    const uint8_t c0_mrb[496],
    const uint64_t P_pak[496][5],
    const uint64_t c0_weak[5],
    int f1, int f2, int f3,
    uint8_t msg_out[98]
) {
    uint8_t bits_perm[784];
    memcpy(bits_perm, c0_mrb, 496);
    if (f1 >= 0) bits_perm[f1] ^= 1u;
    if (f2 >= 0) bits_perm[f2] ^= 1u;
    if (f3 >= 0) bits_perm[f3] ^= 1u;

    uint64_t w[5];
    for (int m = 0; m < 5; m++) {
        uint64_t v = c0_weak[m];
        if (f1 >= 0) v ^= P_pak[f1][m];
        if (f2 >= 0) v ^= P_pak[f2][m];
        if (f3 >= 0) v ^= P_pak[f3][m];
        w[m] = v;
    }
    for (int j = 0; j < 288; j++) {
        bits_perm[496 + j] = (uint8_t)((w[j >> 6] >> (j & 63)) & 1ULL);
    }

    uint8_t bits_orig[784];
    for (int j = 0; j < 784; j++) bits_orig[perm[j]] = bits_perm[j];

    memset(msg_out, 0, 98);
    for (int k = 0; k < 98; k++) {
        uint8_t bval = 0;
        for (int b = 0; b < 8; b++) bval = (uint8_t)((bval << 1) | bits_orig[k * 8 + b]);
        msg_out[k] = bval;
    }
}

int rs36_binary_osd_decode(
    const float *llrs,
    int max_bit_diffs,
    int osd3_tail,
    uint8_t *msg_out,
    int *out_order,
    int *out_byte_diffs,
    int *out_bit_diffs
) {
    const auto& G_BIN = ecc_tables().G_BIN;

    int perm[784];
    float rel[784];
    uint8_t hard_orig[784];
    uint8_t hard_bytes[98];

    for (int i = 0; i < 784; i++) {
        perm[i] = i;
        rel[i] = fabsf(llrs[i]);
        hard_orig[i] = (llrs[i] > 0.0f) ? 1u : 0u;
    }
    for (int k = 0; k < 98; k++) {
        uint8_t v = 0;
        for (int b = 0; b < 8; b++) v = (uint8_t)((v << 1) | hard_orig[k * 8 + b]);
        hard_bytes[k] = v;
    }
    for (int i = 1; i < 784; i++) {
        int key = perm[i];
        float kr = rel[key];
        int j = i - 1;
        while (j >= 0 && rel[perm[j]] < kr) {
            perm[j + 1] = perm[j];
            j--;
        }
        perm[j + 1] = key;
    }

    uint64_t M[496][13];
    memset(M, 0, sizeof(M));
    for (int r = 0; r < 496; r++) {
        const uint8_t *grow = G_BIN[r];
        for (int c = 0; c < 784; c++) {
            if (grow[perm[c]]) M[r][c >> 6] |= (1ULL << (c & 63));
        }
    }

    for (int col = 0; col < 496; col++) {
        int w_idx = col >> 6;
        uint64_t bit_mask = 1ULL << (col & 63);
        int piv = -1;
        for (int r = col; r < 496; r++) {
            if (M[r][w_idx] & bit_mask) { piv = r; break; }
        }
        if (piv < 0) {
            int swap_c = -1;
            for (int sc = 496; sc < 784; sc++) {
                int sw = sc >> 6;
                uint64_t sm = 1ULL << (sc & 63);
                for (int r = col; r < 496; r++) {
                    if (M[r][sw] & sm) { swap_c = sc; piv = r; break; }
                }
                if (swap_c >= 0) break;
            }
            if (swap_c < 0) return -1;
            int tmp_p = perm[col]; perm[col] = perm[swap_c]; perm[swap_c] = tmp_p;
            int sw = swap_c >> 6;
            uint64_t sm = 1ULL << (swap_c & 63);
            for (int r = 0; r < 496; r++) {
                uint64_t b1 = (M[r][w_idx] & bit_mask) ? 1ULL : 0ULL;
                uint64_t b2 = (M[r][sw] & sm) ? 1ULL : 0ULL;
                if (b1 != b2) {
                    M[r][w_idx] ^= bit_mask;
                    M[r][sw] ^= sm;
                }
            }
        }
        if (piv != col) {
            for (int w = 0; w < 13; w++) {
                uint64_t tmp = M[col][w]; M[col][w] = M[piv][w]; M[piv][w] = tmp;
            }
        }
        const uint64_t *prow = M[col];
        for (int r = 0; r < 496; r++) {
            if (r != col && (M[r][w_idx] & bit_mask)) {
                uint64_t *mrow = M[r];
                for (int w = w_idx; w < 13; w++) mrow[w] ^= prow[w];
            }
        }
    }

    uint64_t P_pak[496][5];
    uint64_t tail_mask4 = (1ULL << 32) - 1ULL;
    for (int r = 0; r < 496; r++) {
        for (int m = 0; m < 4; m++) P_pak[r][m] = (M[r][7 + m] >> 48) | (M[r][8 + m] << 16);
        P_pak[r][4] = ((M[r][11] >> 48) | (M[r][12] << 16)) & tail_mask4;
    }

    uint8_t y_mrb[496];
    uint64_t y_weak[5] = {0, 0, 0, 0, 0};
    uint64_t c0_weak[5] = {0, 0, 0, 0, 0};
    for (int i = 0; i < 496; i++) {
        uint8_t b = hard_orig[perm[i]];
        y_mrb[i] = b;
        if (b) {
            for (int m = 0; m < 5; m++) c0_weak[m] ^= P_pak[i][m];
        }
    }
    for (int j = 0; j < 288; j++) {
        if (hard_orig[perm[496 + j]]) y_weak[j >> 6] |= (1ULL << (j & 63));
    }
    uint64_t D0[5];
    for (int m = 0; m < 5; m++) D0[m] = y_weak[m] ^ c0_weak[m];

    #define POP5(a0,a1,a2,a3,a4) ( \
        DPM_POPCOUNT64(a0) + DPM_POPCOUNT64(a1) + \
        DPM_POPCOUNT64(a2) + DPM_POPCOUNT64(a3) + DPM_POPCOUNT64(a4) )

    int w0 = POP5(D0[0], D0[1], D0[2], D0[3], D0[4]);
    if (w0 <= max_bit_diffs) {
        reconstruct_and_pack(perm, y_mrb, P_pak, c0_weak, -1, -1, -1, msg_out);
        if (check_ecc200_pad_strict(msg_out)) {
            int bd = 0;
            for (int k = 0; k < 98; k++) if (msg_out[k] != hard_bytes[k]) bd++;
            *out_order = 0; *out_byte_diffs = bd; *out_bit_diffs = w0;
            return 0;
        }
    }

    int lim1 = max_bit_diffs - 1;
    for (int i = 0; i < 496; i++) {
        int wt = POP5(D0[0]^P_pak[i][0], D0[1]^P_pak[i][1], D0[2]^P_pak[i][2], D0[3]^P_pak[i][3], D0[4]^P_pak[i][4]);
        if (wt <= lim1) {
            reconstruct_and_pack(perm, y_mrb, P_pak, c0_weak, i, -1, -1, msg_out);
            if (check_ecc200_pad_strict(msg_out)) {
                int bd = 0;
                for (int k = 0; k < 98; k++) if (msg_out[k] != hard_bytes[k]) bd++;
                *out_order = 1; *out_byte_diffs = bd; *out_bit_diffs = wt + 1;
                return 1;
            }
        }
    }

    int lim2 = max_bit_diffs - 2;
    int lim2_w01 = 31;
    for (int i = 0; i < 496; i++) {
        uint64_t d0 = D0[0] ^ P_pak[i][0], d1 = D0[1] ^ P_pak[i][1];
        uint64_t d2 = D0[2] ^ P_pak[i][2], d3 = D0[3] ^ P_pak[i][3], d4 = D0[4] ^ P_pak[i][4];
        for (int j = i + 1; j < 496; j++) {
            int wt0 = DPM_POPCOUNT64(d0 ^ P_pak[j][0]);
            if (wt0 > 18) continue;
            int wt01 = wt0 + DPM_POPCOUNT64(d1 ^ P_pak[j][1]);
            if (wt01 > lim2_w01) continue;
            int wt = wt01 + DPM_POPCOUNT64(d2 ^ P_pak[j][2]) + DPM_POPCOUNT64(d3 ^ P_pak[j][3]) + DPM_POPCOUNT64(d4 ^ P_pak[j][4]);
            if (wt <= lim2) {
                reconstruct_and_pack(perm, y_mrb, P_pak, c0_weak, i, j, -1, msg_out);
                if (check_ecc200_pad_strict(msg_out)) {
                    int bd = 0;
                    for (int k = 0; k < 98; k++) if (msg_out[k] != hard_bytes[k]) bd++;
                    *out_order = 2; *out_byte_diffs = bd; *out_bit_diffs = wt + 2;
                    return 2;
                }
            }
        }
    }

    if (osd3_tail <= 0) return -1;
    int i_start = 496 - osd3_tail;
    if (i_start < 2) i_start = 2;
    int lim3 = max_bit_diffs - 3;
    int lim3_w0 = 18;
    int found3 = 0;

    int i;

    for (i = 495; i >= i_start; i--) {
        if (found3) continue;
        uint64_t di0 = D0[0] ^ P_pak[i][0], di1 = D0[1] ^ P_pak[i][1];
        uint64_t di2 = D0[2] ^ P_pak[i][2], di3 = D0[3] ^ P_pak[i][3], di4 = D0[4] ^ P_pak[i][4];
        for (int j = 0; j < i; j++) {
            if (found3) break;
            uint64_t dij0 = di0 ^ P_pak[j][0], dij1 = di1 ^ P_pak[j][1];
            uint64_t dij2 = di2 ^ P_pak[j][2], dij3 = di3 ^ P_pak[j][3], dij4 = di4 ^ P_pak[j][4];
            for (int k = j + 1; k < i; k++) {
                int w0_c = DPM_POPCOUNT64(dij0 ^ P_pak[k][0]);
                if (w0_c > lim3_w0) continue;
                int w01_c = w0_c + DPM_POPCOUNT64(dij1 ^ P_pak[k][1]);
                if (w01_c > lim2_w01) continue;
                int wt = w01_c + DPM_POPCOUNT64(dij2 ^ P_pak[k][2]) + DPM_POPCOUNT64(dij3 ^ P_pak[k][3]) + DPM_POPCOUNT64(dij4 ^ P_pak[k][4]);
                if (wt <= lim3) {
                    uint8_t cand_msg[98];
                    reconstruct_and_pack(perm, y_mrb, P_pak, c0_weak, i, j, k, cand_msg);
                    if (check_ecc200_pad_strict(cand_msg)) {

                        {
                            if (!found3) {
                                found3 = 1;
                                memcpy(msg_out, cand_msg, 98);
                                int bd = 0;
                                for (int m = 0; m < 98; m++) if (cand_msg[m] != hard_bytes[m]) bd++;
                                *out_order = 3; *out_byte_diffs = bd; *out_bit_diffs = wt + 3;
                            }
                        }
                        break;
                    }
                }
            }
        }
    }
    return found3 ? 3 : -1;
}
#undef POP5
#undef DPM_POPCOUNT64
}}}
