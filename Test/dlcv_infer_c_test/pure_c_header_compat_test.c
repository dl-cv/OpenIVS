#include "dlcv_infer/dlcv_data_type_c.h"
#include "dlcv_infer_cpp/dlcv_infer_c_api.h"

int dlcv_infer_pure_c_header_compat_test(void) {
    DlcvCResult result = {0};
    return result.code;
}
