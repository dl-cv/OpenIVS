#include <limits.h>
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
typedef DlcvCResult (*DlcvExtendedInferCFunction)(
    int model_index,
    const DlcvCImageList* image_list);
typedef void (*DlcvExtendedFreeResultCFunction)(DlcvCResult* result);

typedef struct DlcvPureCApi {
    HMODULE module;
    DlcvInferCFunction infer_function;
    DlcvFreeResultCFunction free_function;
    DlcvExtendedInferCFunction extended_infer_function;
    DlcvExtendedFreeResultCFunction extended_free_function;
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
    api->extended_infer_function = (DlcvExtendedInferCFunction)GetProcAddress(
        api->module,
        "dlcv_infer_cpp_infer_c");
    api->extended_free_function = (DlcvExtendedFreeResultCFunction)GetProcAddress(
        api->module,
        "dlcv_infer_cpp_free_model_result_c");
    if (api->infer_function == NULL || api->free_function == NULL ||
        api->extended_infer_function == NULL || api->extended_free_function == NULL) {
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

static int check_result_and_release(
    DlcvCResult* result,
    int expected_code,
    const char* expected_message,
    DlcvFreeResultCFunction free_function,
    int expected_released_code,
    int failure_code) {
    if (result->code != expected_code || result->message == NULL ||
        strcmp(result->message, expected_message) != 0 ||
        result->sample_results != NULL || result->n != 0) {
        free_function(result);
        return failure_code;
    }
    free_function(result);
    if (result->code != expected_released_code || result->message != NULL ||
        result->sample_results != NULL || result->n != 0) {
        return failure_code - 1;
    }
    free_function(result);
    if (result->code != expected_released_code || result->message != NULL ||
        result->sample_results != NULL || result->n != 0) {
        return failure_code - 2;
    }
    return 0;
}

static int check_extended_result_and_release(
    DlcvCResult* result,
    int expected_code,
    const char* expected_message,
    DlcvExtendedFreeResultCFunction free_function,
    int failure_code) {
    if (result->code != expected_code || result->message == NULL ||
        strcmp(result->message, expected_message) != 0 ||
        result->sample_results != NULL || result->n != 0) {
        free_function(result);
        return failure_code;
    }
    free_function(result);
    if (result->code != 0 || result->message != NULL ||
        result->sample_results != NULL || result->n != 0) {
        return failure_code - 1;
    }
    free_function(result);
    if (result->code != 0 || result->message != NULL ||
        result->sample_results != NULL || result->n != 0) {
        return failure_code - 2;
    }
    return 0;
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
    unsigned char pixel[6] = {0, 0, 0, 0, 0, 0};
    DlcvPureCApi api;
    DlcvCImage image = {0};
    DlcvCImage images[2];
    DlcvCImageList image_list = {0};
    DlcvCResult result = {0};
    int load_result = load_pure_c_api(&api);
    int check_result;

    if (load_result != 0) return load_result;
    api.free_function(NULL);
    api.extended_free_function(NULL);

    result = api.infer_function(-1, NULL);
    check_result = check_result_and_release(
        &result, 1, "Invalid image list.", api.free_function, 1, -10);
    if (check_result != 0) goto done;

    image_list.images = NULL;
    image_list.n = -1;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 1, "Invalid image list.", api.free_function, 1, -20);
    if (check_result != 0) goto done;

    image.data_ptr = (long long)(uintptr_t)pixel;
    image.height = 1;
    image.width = 1;
    image.channel = 3;
    image_list.images = &image;
    image_list.n = 0;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 1, "Invalid image list.", api.free_function, 1, -30);
    if (check_result != 0) goto done;

    image_list.images = NULL;
    image_list.n = 1;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 1, "Invalid image list.", api.free_function, 1, -40);
    if (check_result != 0) goto done;

    image.data_ptr = 0;
    image_list.images = &image;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 1, "Invalid image data.", api.free_function, 1, -50);
    if (check_result != 0) goto done;

    image.data_ptr = (long long)(uintptr_t)pixel;
    image.height = 0;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 1, "Invalid image size.", api.free_function, 1, -60);
    if (check_result != 0) goto done;

    image.height = 1;
    image.width = 0;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 1, "Invalid image size.", api.free_function, 1, -70);
    if (check_result != 0) goto done;

    image.width = 1;
    image.channel = 0;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 1, "Invalid image type.", api.free_function, 1, -80);
    if (check_result != 0) goto done;

    image.channel = INT_MAX;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 1, "Invalid image type.", api.free_function, 1, -90);
    if (check_result != 0) goto done;

    image.height = INT_MAX;
    image.width = INT_MAX;
    image.channel = 512;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 1, "Invalid image size.", api.free_function, 1, -95);
    if (check_result != 0) goto done;

    images[0].data_ptr = (long long)(uintptr_t)pixel;
    images[0].height = 1;
    images[0].width = 1;
    images[0].channel = 3;
    images[1] = images[0];
    images[1].width = 0;
    image_list.images = images;
    image_list.n = 2;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 1, "Invalid image size.", api.free_function, 1, -100);
    if (check_result != 0) goto done;

    image_list.images = &image;
    image_list.n = 1;
    image.channel = INT_MAX;
    result = api.extended_infer_function(-1, &image_list);
    check_result = check_extended_result_and_release(
        &result, -1, "invalid image type", api.extended_free_function, -110);
    if (check_result != 0) goto done;

    image.channel = 3;
    result = api.infer_function(-1, &image_list);
    check_result = check_result_and_release(
        &result, 2, "Model not found.", api.free_function, 2, -120);

done:
    close_pure_c_api(&api);
    return check_result;
}
