#include <stddef.h>
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

int dlcv_infer_pure_c_header_test(void) {
    HMODULE module = LoadLibraryW(L"dlcv_infer_cpp.dll");
    DlcvCImage image = {0};
    DlcvCImageList image_list = {0};
    DlcvCResult result = {0};
    DlcvInferCFunction infer_function;
    DlcvFreeResultCFunction free_function;
    int result_code;

    if (module == NULL) return -(int)GetLastError();
    infer_function = (DlcvInferCFunction)GetProcAddress(module, "dlcv_infer_c");
    free_function = (DlcvFreeResultCFunction)GetProcAddress(module, "dlcv_free_model_result_c");
    if (infer_function == NULL || free_function == NULL) {
        FreeLibrary(module);
        return -2;
    }

    image_list.images = &image;
    image_list.n = 1;
    result = infer_function(-1, &image_list);
    result_code = result.code;
    free_function(&result);
    FreeLibrary(module);
    return result_code;
}
