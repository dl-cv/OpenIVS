#include "dlcv_infer/dlcv_data_type_c.h"
#include "dlcv_infer_cpp/dlcv_infer_c_api.h"

typedef const char* (*DlcvGetLastErrorCFunction)(void);
typedef void (*DlcvFreeStringCFunction)(const char* value);

int dlcv_infer_pure_c_header_compat_test(void) {
    DlcvCResult result = {0};
    DlcvGetLastErrorCFunction get_last_error = dlcv_infer_cpp_get_last_error_c;
    DlcvFreeStringCFunction free_string = dlcv_infer_cpp_free_string_c;
    if (get_last_error == 0 || free_string == 0) return -1;
    return result.code;
}
