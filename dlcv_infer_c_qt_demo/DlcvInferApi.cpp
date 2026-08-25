#include "DlcvInferApi.h"

#include <cstring>

DlcvInferApi::~DlcvInferApi() {
    unload();
}

bool DlcvInferApi::load() {
    unload();

    module_ = LoadLibraryW(L"dlcv_infer_cpp.dll");
    if (module_ == nullptr) {
        lastError_ = L"加载 dlcv_infer_cpp.dll 失败。系统错误：" + formatSystemError(GetLastError());
        return false;
    }

    if (!resolve("dlcv_infer_cpp_load_model_c", loadModel_) ||
        !resolve("dlcv_infer_cpp_get_last_error_c", getLastError_) ||
        !resolve("dlcv_infer_cpp_free_model_c", freeModel_) ||
        !resolve("dlcv_infer_cpp_infer_with_params_c", inferWithParams_) ||
        !resolve("dlcv_infer_cpp_free_model_result_c", freeModelResult_) ||
        !resolve("dlcv_infer_cpp_get_model_info_c", getModelInfo_) ||
        !resolve("dlcv_infer_cpp_infer_json_c", inferJson_) ||
        !resolve("dlcv_infer_cpp_get_all_dog_info_c", getAllDogInfo_) ||
        !resolve("dlcv_infer_cpp_free_string_c", freeString_) ||
        !resolve("dlcv_infer_cpp_free_all_models_c", freeAllModels_) ||
        !resolve("dlcv_keep_max_clock", keepMaxClock_) ||
        !resolve("dlcv_get_gpu_info", getGpuInfo_) ||
        !resolve("dlcv_free_result", freeSystemString_)) {
        FreeLibrary(module_);
        module_ = nullptr;
        clearFunctions();
        return false;
    }

    lastError_.clear();
    return true;
}

void DlcvInferApi::unload() {
    if (module_ != nullptr) {
        FreeLibrary(module_);
        module_ = nullptr;
    }
    clearFunctions();
}

bool DlcvInferApi::isLoaded() const {
    return module_ != nullptr;
}

const std::wstring& DlcvInferApi::lastError() const {
    return lastError_;
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

const char* DlcvInferApi::inferJson(int modelIndex, const DlcvCImage* image, const char* paramsJson) const {
    return inferJson_(modelIndex, image, paramsJson);
}

const char* DlcvInferApi::getAllDogInfo() const {
    return getAllDogInfo_();
}

void DlcvInferApi::freeString(const char* value) const {
    if (value != nullptr) {
        freeString_(value);
    }
}

void DlcvInferApi::freeAllModels() const {
    freeAllModels_();
}

void DlcvInferApi::keepMaxClock() const {
    keepMaxClock_();
}

const char* DlcvInferApi::getGpuInfo() const {
    return getGpuInfo_();
}

void DlcvInferApi::freeSystemString(const char* value) const {
    if (value != nullptr) {
        freeSystemString_(value);
    }
}

void DlcvInferApi::clearFunctions() {
    loadModel_ = nullptr;
    getLastError_ = nullptr;
    freeModel_ = nullptr;
    inferWithParams_ = nullptr;
    freeModelResult_ = nullptr;
    getModelInfo_ = nullptr;
    inferJson_ = nullptr;
    getAllDogInfo_ = nullptr;
    freeString_ = nullptr;
    freeAllModels_ = nullptr;
    keepMaxClock_ = nullptr;
    getGpuInfo_ = nullptr;
    freeSystemString_ = nullptr;
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
