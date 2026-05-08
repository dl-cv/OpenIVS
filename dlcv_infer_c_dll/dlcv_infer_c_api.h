#ifndef DLCV_INFER_C_DLL_C_API_H
#define DLCV_INFER_C_DLL_C_API_H

#include "dlcv_infer/dlcv_data_type_c.h"

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) || defined(__CYGWIN__)
#  ifdef DLCV_INFER_C_DLL_EXPORTS
#    define DLCV_C_API __declspec(dllexport)
#  else
#    define DLCV_C_API __declspec(dllimport)
#  endif
#else
#  define DLCV_C_API
#endif

DLCV_C_API int dlcv_infer_cpp_load_model_c(const char* model_path, int device_id);
DLCV_C_API int dlcv_infer_cpp_free_model_c(int model_index);
DLCV_C_API DlcvCResult dlcv_infer_cpp_infer_c(int model_index, const DlcvCImageList* image_list);
DLCV_C_API void dlcv_infer_cpp_free_model_result_c(DlcvCResult* result);

#ifdef __cplusplus
}
#endif

#endif
