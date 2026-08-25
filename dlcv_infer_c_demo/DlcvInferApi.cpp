#include "DlcvInferApi.h"

DlcvInferApi::~DlcvInferApi() {
    unload();
}

bool DlcvInferApi::load() {
    unload();
    module_ = LoadLibraryW(L"dlcv_infer_cpp.dll");
    if (module_ == nullptr) {
        lastLoadError_ = L"加载 dlcv_infer_cpp.dll 失败。系统错误：" +
            formatSystemError(GetLastError());
        return false;
    }

    if (!resolve("dlcv_infer_cpp_load_model_c", loadModel_) ||
        !resolve("dlcv_infer_cpp_get_last_error_c", getLastError_) ||
        !resolve("dlcv_infer_cpp_free_model_c", freeModel_) ||
        !resolve("dlcv_infer_cpp_infer_with_params_c", inferWithParams_) ||
        !resolve("dlcv_infer_cpp_free_model_result_c", freeModelResult_) ||
        !resolve("dlcv_infer_cpp_get_model_info_c", getModelInfo_) ||
        !resolve("dlcv_infer_cpp_free_string_c", freeString_) ||
        !resolve("dlcv_infer_cpp_free_all_models_c", freeAllModels_)) {
        FreeLibrary(module_);
        module_ = nullptr;
        clearFunctions();
        return false;
    }

    lastLoadError_.clear();
    return true;
}

void DlcvInferApi::unload() {
    if (module_ != nullptr) {
        FreeLibrary(module_);
        module_ = nullptr;
    }
    clearFunctions();
}

const std::wstring& DlcvInferApi::lastLoadError() const {
    return lastLoadError_;
}

int DlcvInferApi::loadModel(const char* modelPath, int deviceId) const {
    return loadModel_(modelPath, deviceId);
}

const char* DlcvInferApi::getLastError() const {
    return getLastError_();
}

int DlcvInferApi::freeModel(int modelIndex) const {
    return freeModel_(modelIndex);
}

DlcvCResult DlcvInferApi::inferWithParams(
    int modelIndex,
    const DlcvCImageList* imageList,
    const char* paramsJson) const {
    return inferWithParams_(modelIndex, imageList, paramsJson);
}

void DlcvInferApi::freeModelResult(DlcvCResult* result) const {
    freeModelResult_(result);
}

const char* DlcvInferApi::getModelInfo(int modelIndex) const {
    return getModelInfo_(modelIndex);
}

void DlcvInferApi::freeString(const char* value) const {
    if (value != nullptr) {
        freeString_(value);
    }
}

void DlcvInferApi::freeAllModels() const {
    freeAllModels_();
}

void DlcvInferApi::clearFunctions() {
    loadModel_ = nullptr;
    getLastError_ = nullptr;
    freeModel_ = nullptr;
    inferWithParams_ = nullptr;
    freeModelResult_ = nullptr;
    getModelInfo_ = nullptr;
    freeString_ = nullptr;
    freeAllModels_ = nullptr;
}

std::wstring DlcvInferApi::utf8ToWide(const char* value) {
    if (value == nullptr || *value == '\0') {
        return {};
    }
    const int length = MultiByteToWideChar(CP_UTF8, 0, value, -1, nullptr, 0);
    if (length <= 1) {
        return {};
    }
    std::wstring result(static_cast<size_t>(length), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value, -1, result.data(), length);
    result.pop_back();
    return result;
}

std::wstring DlcvInferApi::formatSystemError(DWORD errorCode) {
    LPWSTR message = nullptr;
    const DWORD length = FormatMessageW(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr,
        errorCode,
        0,
        reinterpret_cast<LPWSTR>(&message),
        0,
        nullptr);

    std::wstring text = L"错误码 " + std::to_wstring(errorCode);
    if (length != 0 && message != nullptr) {
        std::wstring detail(message, length);
        while (!detail.empty() && (detail.back() == L'\r' || detail.back() == L'\n')) {
            detail.pop_back();
        }
        text += L"（" + detail + L"）";
    }
    if (message != nullptr) {
        LocalFree(message);
    }
    return text;
}
