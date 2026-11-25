#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "DataMatrixNative.h"
#include "Ecc200Search.inl"

// 32×32 ECC200 包含 62 个数据码字和 36 个纠错码字。
enum { Side = 32, BitmapSide = 38, Codewords = 98, DataWords = 62, CheckWords = 36 };
static SRWLOCK searchLock = SRWLOCK_INIT;
static INIT_ONCE initialization = INIT_ONCE_STATIC_INIT;
static V parity[N];
static unsigned short bitLocation[N];
static unsigned char exponent[512], logarithm[256];

static int AppendWord(int row, int col, const int offset[8][2],
                      unsigned char visited[28][28], int count) {
    if (count + 8 > N) return -1;
    for (int i = 0; i < 8; ++i) {
        int y = row + offset[i][0], x = col + offset[i][1];
        if (y < 0) { y += 28; x += 4; }
        if (x < 0) { x += 28; y += 4; }
        if (y >= 28) y -= 28;
        if (y < 0 || y >= 28 || x < 0 || x >= 28 || visited[y][x]) return -1;
        visited[y][x] = 1;
        bitLocation[count++] = (unsigned short)((y / 14 * 16 + y % 14 + 1) * Side + x / 14 * 16 + x % 14 + 1);
    }
    return count;
}

static int BuildMapping(void) {
    static const int utah[8][2] = {{-2,-2}, {-2,-1}, {-1,-2}, {-1,-1}, {-1,0}, {0,-2}, {0,-1}, {0,0}};
    static const int firstCorner[8][2] = {{27,0}, {27,1}, {27,2}, {0,26}, {0,27}, {1,27}, {2,27}, {3,27}};
    static const int fourthCorner[8][2] = {{25,0}, {26,0}, {27,0}, {0,26}, {0,27}, {1,27}, {2,27}, {3,27}};
    unsigned char visited[28][28] = {{0}};
    int row = 4, col = 0, count = 0, first = 0, fourth = 0;
    do {
        if ((row == 28 && col == 0 && !first) || (row == 26 && col == 0 && !fourth)) {
            count = AppendWord(0, 0, row == 28 ? firstCorner : fourthCorner, visited, count);
            if (count < 0) return 0;
            if (row == 28) first = 1; else fourth = 1;
            row -= 2;
            col += 2;
        } else {
            do {
                if (row >= 0 && row < 28 && col >= 0 && col < 28 && !visited[row][col]) {
                    count = AppendWord(row, col, utah, visited, count);
                    if (count < 0) return 0;
                }
                row -= 2;
                col += 2;
            } while (row >= 0 && col < 28);
            row += 1;
            col += 3;
            do {
                if (row >= 0 && row < 28 && col >= 0 && col < 28 && !visited[row][col]) {
                    count = AppendWord(row, col, utah, visited, count);
                    if (count < 0) return 0;
                }
                row += 2;
                col -= 2;
            } while (row < 28 && col >= 0);
            row += 3;
            col += 1;
        }
    } while (row < 28 || col < 28);
    return count == N;
}

static unsigned char Multiply(unsigned char x, unsigned char y) {
    return x && y ? exponent[logarithm[x] + logarithm[y]] : 0;
}

static BOOL CALLBACK Initialize(PINIT_ONCE once, PVOID parameter, PVOID *context) {
    (void)once;
    (void)parameter;
    (void)context;
    if (!BuildMapping()) return FALSE;
    unsigned int x = 1;
    for (int i = 0; i < 255; ++i) {
        exponent[i] = (unsigned char)x;
        logarithm[x] = (unsigned char)i;
        x <<= 1;
        if (x & 256u) x ^= 0x12du;
    }
    for (int i = 255; i < 512; ++i) exponent[i] = exponent[i - 255];
    unsigned char generator[CheckWords + 1] = {1};
    for (int i = 1; i <= CheckWords; ++i)
        for (int j = i; j > 0; --j)
            generator[j] ^= Multiply(generator[j - 1], exponent[i]);
    for (int i = 0; i < DataWords * 8; ++i) {
        unsigned char word[Codewords] = {0};
        word[i / 8] = (unsigned char)(128 >> (i % 8));
        for (int j = 0; j < DataWords; ++j) {
            const unsigned char factor = word[j];
            if (factor)
                for (int k = 1; k <= CheckWords; ++k)
                    word[j + k] ^= Multiply(factor, generator[k]);
        }
        for (int j = 0; j < M; ++j)
            if (word[DataWords + j / 8] & (128 >> (j % 8))) mark(&parity[i], j);
    }
    for (int j = 0; j < M; ++j) mark(&parity[DataWords * 8 + j], j);
    return TRUE;
}

static int ValidWord(const unsigned char *word) {
    for (int i = 1; i <= CheckWords; ++i) {
        unsigned char remainder = word[0];
        for (int j = 1; j < Codewords; ++j) remainder = Multiply(remainder, exponent[i]) ^ word[j];
        if (remainder) return 0;
    }
    return 1;
}

static void DrawBitmap(const unsigned char *bits, unsigned char *bitmap) {
    memset(bitmap, 255, BitmapSide * BitmapSide);
    for (int y = 3; y <= 19; y += 16) for (int x = 3; x <= 19; x += 16)
        for (int i = 0; i < 16; ++i) {
            bitmap[(y + i) * BitmapSide + x] = 0;
            bitmap[(y + 15) * BitmapSide + x + i] = 0;
            if (!(i % 2)) bitmap[y * BitmapSide + x + i] = 0;
            if (i % 2) bitmap[(y + i) * BitmapSide + x + 15] = 0;
        }
    for (int i = 0; i < N; ++i) {
        const int y = bitLocation[i] / Side, x = bitLocation[i] % Side;
        if (bits[i]) bitmap[(y + 3) * BitmapSide + x + 3] = 0;
    }
}

// 置信度按行存放，输出两组 98 字节码字及 38×38 黑白位图。
// 调用方须比较独立的硬判结果，再用 ZXing 确认对应位图。
int __cdecl dlcv_datamatrix_recover_v1(
    const float *scores, int iterations, uint32_t seed, unsigned char *corrected,
    unsigned char *observed, unsigned char *bitmap) {
    if (!scores || !corrected || !observed || !bitmap || iterations < 1 || iterations > 16) return 0;
    if (!InitOnceExecuteOnce(&initialization, Initialize, NULL, NULL)) return 0;
    float confidence[N], max = 0;
    for (int i = 0; i < Side * Side; ++i)
        if (!isfinite(scores[i])) return 0;
    for (int i = 0; i < N; ++i) {
        confidence[i] = scores[bitLocation[i]];
        const float value = fabsf(confidence[i]);
        if (value > max) max = value;
    }
    if (max < 1.0e-5f) return 0;
    unsigned char bits[N] = {0}, word[Codewords] = {0}, raw[Codewords] = {0};
    float cost = 1e9f;
    AcquireSRWLockExclusive(&searchLock);
    Search(parity[0].a, confidence, iterations, 496, 6, seed ? seed : 1u, bits, &cost);
    ReleaseSRWLockExclusive(&searchLock);
    if (!(cost < 1e9f)) return 0;
    int errors = 0;
    for (int i = 0; i < N; ++i) {
        const int black = confidence[i] < 0;
        errors += black != bits[i];
        word[i / 8] |= (unsigned char)(bits[i] << (7 - i % 8));
        raw[i / 8] |= (unsigned char)(black << (7 - i % 8));
    }
    if (errors > 85 || !ValidWord(word)) return 0;
    memcpy(corrected, word, sizeof(word));
    memcpy(observed, raw, sizeof(raw));
    DrawBitmap(bits, bitmap);
    return 1;
}
