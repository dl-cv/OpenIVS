#ifndef DLCV_INFER_NATIVE_C_API_H
#define DLCV_INFER_NATIVE_C_API_H

#ifndef __cplusplus
#include <stdbool.h>
#endif

#if defined(_WIN32) || defined(__CYGWIN__)
#  ifdef DLCV_INFER_C_DLL_EXPORTS
#    define DLCV_NATIVE_C_API __declspec(dllexport)
#  else
#    define DLCV_NATIVE_C_API __declspec(dllimport)
#  endif
#else
#  define DLCV_NATIVE_C_API
#endif

#if defined(_WIN32) || defined(__CYGWIN__)
#  define DLCV_NATIVE_C_CALL __stdcall
#else
#  define DLCV_NATIVE_C_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

DLCV_NATIVE_C_API const char* DLCV_NATIVE_C_CALL dlcv_load_model(const char* config_str);
DLCV_NATIVE_C_API const char* DLCV_NATIVE_C_CALL dlcv_free_model(const char* config_str);
DLCV_NATIVE_C_API const char* DLCV_NATIVE_C_CALL dlcv_get_model_info(const char* config_str);
#ifndef DLCV_NATIVE_C_API_SKIP_INFER_EXPORT
DLCV_NATIVE_C_API const char* DLCV_NATIVE_C_CALL dlcv_infer(const char* config_str);
#endif
DLCV_NATIVE_C_API void DLCV_NATIVE_C_CALL dlcv_free_model_result(const char* config_str);
DLCV_NATIVE_C_API void DLCV_NATIVE_C_CALL dlcv_free_result(const char* config_str);
DLCV_NATIVE_C_API void DLCV_NATIVE_C_CALL dlcv_free_all_models();

DLCV_NATIVE_C_API const char* DLCV_NATIVE_C_CALL dlcv_get_device_info();
DLCV_NATIVE_C_API const char* DLCV_NATIVE_C_CALL dlcv_get_gpu_info();

DLCV_NATIVE_C_API void DLCV_NATIVE_C_CALL dlcv_keep_max_clock();
DLCV_NATIVE_C_API void DLCV_NATIVE_C_CALL dlcv_reset_max_clock();
DLCV_NATIVE_C_API void DLCV_NATIVE_C_CALL dlcv_set_gpu_max_clock(bool verbose);
DLCV_NATIVE_C_API void DLCV_NATIVE_C_CALL dlcv_reset_gpu_max_clock(bool verbose);

DLCV_NATIVE_C_API const char* DLCV_NATIVE_C_CALL dlcv_get_power_scheme_guid(int verbose);
DLCV_NATIVE_C_API int DLCV_NATIVE_C_CALL dlcv_set_power_scheme_guid(const char* scheme_guid, int verbose);
DLCV_NATIVE_C_API const char* DLCV_NATIVE_C_CALL dlcv_get_power_scheme(int verbose);
DLCV_NATIVE_C_API int DLCV_NATIVE_C_CALL dlcv_set_power_scheme(const char* scheme_name, int verbose);
DLCV_NATIVE_C_API int DLCV_NATIVE_C_CALL dlcv_set_current_process_affinity_to_big_cores(int verbose);
DLCV_NATIVE_C_API int DLCV_NATIVE_C_CALL dlcv_set_current_process_priority_highest(
    int prefer_realtime,
    int verbose,
    int bind_big_cores);

#ifdef __cplusplus
}
#endif

#endif
