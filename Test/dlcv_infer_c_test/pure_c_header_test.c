#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <windows.h>

#include "dlcv_infer_c_api.h"

#if defined(_WIN64)
typedef char DlcvCImageSizeCheck[(sizeof(DlcvCImage) == 24) ? 1 : -1];
typedef char DlcvCImageListSizeCheck[(sizeof(DlcvCImageList) == 16) ? 1 : -1];
typedef char DlcvCMaskSizeCheck[(sizeof(DlcvCMask) == 16) ? 1 : -1];
typedef char DlcvCObjectResultSizeCheck[(sizeof(DlcvCObjectResult) == 96) ? 1 : -1];
typedef char DlcvCSampleResultSizeCheck[(sizeof(DlcvCSampleResult) == 16) ? 1 : -1];
typedef char DlcvCResultSizeCheck[(sizeof(DlcvCResult) == 32) ? 1 : -1];
#endif

typedef DlcvCResult (DLCV_C_NATIVE_CALL *DlcvInferCFunction)(
    int model_index,
    const DlcvCImageList* image_list);
typedef void (DLCV_C_NATIVE_CALL *DlcvFreeResultCFunction)(DlcvCResult* result);

typedef struct DlcvPureCApi {
    HMODULE module;
    DlcvInferCFunction infer_function;
    DlcvFreeResultCFunction free_function;
} DlcvPureCApi;

static int load_pure_c_api(DlcvPureCApi* api) {
    if (api == NULL) return -1;
    memset(api, 0, sizeof(*api));
    api->module = LoadLibraryW(L"dlcv_infer_cpp.dll");
    if (api->module == NULL) return -(int)GetLastError();
    api->infer_function = (DlcvInferCFunction)GetProcAddress(api->module, "dlcv_infer_c");
    api->free_function = (DlcvFreeResultCFunction)GetProcAddress(
        api->module,
        "dlcv_free_model_result_c");
    if (api->infer_function == NULL || api->free_function == NULL) {
        FreeLibrary(api->module);
        memset(api, 0, sizeof(*api));
        return -2;
    }
    return 0;
}

static void close_pure_c_api(DlcvPureCApi* api) {
    if (api != NULL && api->module != NULL) {
        FreeLibrary(api->module);
        memset(api, 0, sizeof(*api));
    }
}

int dlcv_infer_pure_c_header_test(void) {
    unsigned char pixel[3] = {0, 0, 0};
    DlcvPureCApi api;
    DlcvCImage image = {0};
    DlcvCImageList image_list = {0};
    DlcvCResult result = {0};
    int load_result = load_pure_c_api(&api);
    int result_code;

    if (load_result != 0) return load_result;
    image.data_ptr = (long long)(uintptr_t)pixel;
    image.height = 1;
    image.width = 1;
    image.channel = 3;
    image_list.images = &image;
    image_list.n = 1;
    result = api.infer_function(-1, &image_list);
    result_code = result.code;
    api.free_function(&result);
    close_pure_c_api(&api);
    return result_code;
}

int dlcv_infer_pure_c_invalid_input_test(void) {
    DlcvPureCApi api;
    DlcvCImageList image_list = {0};
    DlcvCResult result = {0};
    int load_result = load_pure_c_api(&api);

    if (load_result != 0) return load_result;

    result = api.infer_function(-1, NULL);
    if (result.code != 1) {
        api.free_function(&result);
        close_pure_c_api(&api);
        return -10;
    }
    if (result.message == NULL || strcmp(result.message, "Invalid image list.") != 0) {
        api.free_function(&result);
        close_pure_c_api(&api);
        return -11;
    }
    api.free_function(&result);
    if (result.code != 1 || result.message != NULL ||
        result.sample_results != NULL || result.n != 0) {
        close_pure_c_api(&api);
        return -12;
    }

    image_list.images = NULL;
    image_list.n = 1;
    result = api.infer_function(-1, &image_list);
    if (result.code != 2 || result.message == NULL ||
        strcmp(result.message, "Model not found.") != 0) {
        api.free_function(&result);
        close_pure_c_api(&api);
        return -13;
    }
    api.free_function(&result);
    close_pure_c_api(&api);
    return 0;
}
