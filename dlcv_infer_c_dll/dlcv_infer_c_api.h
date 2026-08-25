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

#if defined(_WIN32) || defined(__CYGWIN__)
#  define DLCV_C_NATIVE_CALL __stdcall
#else
#  define DLCV_C_NATIVE_CALL
#endif

DLCV_C_API int dlcv_infer_cpp_load_model_c(const char* model_path, int device_id);
DLCV_C_API const char* dlcv_infer_cpp_get_last_error_c();
DLCV_C_API int dlcv_infer_cpp_free_model_c(int model_index);
DLCV_C_API DlcvCResult dlcv_infer_cpp_infer_c(int model_index, const DlcvCImageList* image_list);
DLCV_C_API DlcvCResult dlcv_infer_cpp_infer_with_params_c(
    int model_index,
    const DlcvCImageList* image_list,
    const char* params_json);
DLCV_C_API void dlcv_infer_cpp_free_model_result_c(DlcvCResult* result);
DLCV_C_API const char* dlcv_infer_cpp_get_model_info_c(int model_index);
DLCV_C_API const char* dlcv_infer_cpp_infer_json_c(
    int model_index,
    const DlcvCImage* image,
    const char* params_json);
DLCV_C_API const char* dlcv_infer_cpp_get_all_dog_info_c();
DLCV_C_API void dlcv_infer_cpp_free_string_c(const char* value);
DLCV_C_API void dlcv_infer_cpp_free_all_models_c();

// 与 dlcv_infer 结构化 C API 同名、同参数和同结果语义的兼容入口。
DLCV_C_API int DLCV_C_NATIVE_CALL dlcv_load_model_c(const char* model_path, int device_id);
DLCV_C_API int DLCV_C_NATIVE_CALL dlcv_free_model_c(int model_index);
DLCV_C_API DlcvCResult DLCV_C_NATIVE_CALL dlcv_infer_c(
    int model_index,
    const DlcvCImageList& image_list);
DLCV_C_API void DLCV_C_NATIVE_CALL dlcv_free_model_result_c(DlcvCResult& result);

#ifdef __cplusplus
}
#endif

#endif
