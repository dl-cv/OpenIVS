#include <stddef.h>

#include "dlcv_infer_c_api.h"
#include "dlcv_infer_native_c_api.h"

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
    DlcvCImage image = {0};
    DlcvCImageList image_list = {0};
    DlcvCResult result = {0};
    DlcvInferCFunction infer_function = dlcv_infer_c;
    DlcvFreeResultCFunction free_function = dlcv_free_model_result_c;

    image_list.images = &image;
    image_list.n = 1;
    result = infer_function(-1, &image_list);
    free_function(&result);
    return result.code;
}
