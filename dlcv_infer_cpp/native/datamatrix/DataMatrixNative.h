#pragma once
#include <stdint.h>
#if defined(DLCV_DATAMATRIX_BUILD)
#define DLCV_DATAMATRIX_NATIVE __declspec(dllexport)
#elif defined(DLCV_DATAMATRIX_IMPORT)
#define DLCV_DATAMATRIX_NATIVE __declspec(dllimport)
#else
#define DLCV_DATAMATRIX_NATIVE
#endif
#ifdef __cplusplus
extern "C" {
#endif

DLCV_DATAMATRIX_NATIVE int __cdecl dlcv_datamatrix_recover_v1(
    const float *scores, int iterations, uint32_t seed, unsigned char *corrected,
    unsigned char *observed, unsigned char *bitmap);

typedef int (__cdecl *DlcvDataMatrixVerify)(const unsigned char *bitmap, void *context);
DLCV_DATAMATRIX_NATIVE int __cdecl dlcv_datamatrix_read_v1(
    const unsigned char *gray, int width, int height, int stride,
    int flags, float *corners, DlcvDataMatrixVerify verify, void *context);

DLCV_DATAMATRIX_NATIVE int __cdecl dlcv_datamatrix_read_v2(
    const unsigned char *gray, int width, int height, int stride,
    const unsigned char *original, int originalWidth, int originalHeight, int originalStride,
    const float *roiToOriginal, int flags, float *corners, DlcvDataMatrixVerify verify, void *context);

#ifdef __cplusplus
}
#endif
