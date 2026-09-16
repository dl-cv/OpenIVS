#pragma once

#if defined(_WIN32)
#define OPENIVS_FLOW_CALL __stdcall
#ifdef DLCV_INFER_CPP_EXPORTS
#define OPENIVS_FLOW_API __declspec(dllexport)
#else
#define OPENIVS_FLOW_API __declspec(dllimport)
#endif
#else
#define OPENIVS_FLOW_CALL
#define OPENIVS_FLOW_API __attribute__((visibility("default")))
#endif

// 流程记录仅保存在 OpenIVS，module 为已加载的推理模块句柄。
// handle 小于 -1，-1 表示失败；进程内不复用已释放的 handle。
extern "C" {
OPENIVS_FLOW_API int OPENIVS_FLOW_CALL openivs_flow_register(void* module, const char* json);
OPENIVS_FLOW_API int OPENIVS_FLOW_CALL openivs_flow_contains(void* module, int index);
OPENIVS_FLOW_API const char* OPENIVS_FLOW_CALL openivs_flow_get_info(void* module, int index);
OPENIVS_FLOW_API int OPENIVS_FLOW_CALL openivs_flow_retain(void* module, int index);
OPENIVS_FLOW_API int OPENIVS_FLOW_CALL openivs_flow_release(void* module, int index, int owner);
OPENIVS_FLOW_API void OPENIVS_FLOW_CALL openivs_flow_free_all_models(void* module);
OPENIVS_FLOW_API void OPENIVS_FLOW_CALL openivs_flow_free_result(const char* result);
}
