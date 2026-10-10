// 底层 SDK 与公开接口采用不同结果布局，测试时分别命名。
#define DlcvCImage DlcvNativeImage
#define DlcvCImageList DlcvNativeImageList
#define DlcvCMask DlcvNativeMask
#define DlcvCObjectResult DlcvNativeObjectResult
#define DlcvCSampleResult DlcvNativeSampleResult
#define DlcvCResult DlcvNativeResult
#include "dlcv_infer/dlcv_data_type_c.h"
#undef DlcvCImage
#undef DlcvCImageList
#undef DlcvCMask
#undef DlcvCObjectResult
#undef DlcvCSampleResult
#undef DlcvCResult
#include "dlcv_infer_cpp/dlcv_infer_c_api.h"

#if defined(_WIN64)
typedef char DlcvNativeObjectSizeCheck[(sizeof(DlcvNativeObjectResult) == 96) ? 1 : -1];
typedef char DlcvPublicObjectSizeCheck[(sizeof(DlcvCObjectResult) == 80) ? 1 : -1];
#endif

int dlcv_infer_pure_c_header_compat_test(void) {
    DlcvCResult result = {0};
    DlcvNativeResult native_result = {0};
    return result.code + native_result.code;
}
