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
        !resolve("dlcv_infer_cpp_infer_c", infer_) ||
        !resolve("dlcv_infer_cpp_infer_with_params_c", inferWithParams_) ||
        !resolve("dlcv_infer_cpp_free_model_result_c", freeModelResult_) ||
        !resolve("dlcv_infer_cpp_get_model_info_c", getModelInfo_) ||
        !resolve("dlcv_infer_cpp_infer_json_c", inferJson_) ||
        !resolve("dlcv_infer_cpp_get_all_dog_info_c", getAllDogInfo_) ||
        !resolve("dlcv_infer_cpp_free_string_c", freeString_) ||
        !resolve("dlcv_infer_cpp_free_all_models_c", freeAllModels_) ||
        !resolve("dlcv_load_model_c", loadModelCompatible_) ||
        !resolve("dlcv_free_model_c", freeModelCompatible_) ||
        !resolve("dlcv_infer_c", inferCompatible_) ||
        !resolve("dlcv_free_model_result_c", freeModelResultCompatible_) ||
        !resolve("dlcv_load_model", loadModelNative_) ||
        !resolve("dlcv_free_model", freeModelNative_) ||
        !resolve("dlcv_get_model_info", getModelInfoNative_) ||
        !resolve("dlcv_infer", inferNative_) ||
        !resolve("dlcv_free_model_result", freeModelResultNative_) ||
        !resolve("dlcv_free_result", freeResultNative_) ||
        !resolve("dlcv_free_all_models", freeAllModelsNative_) ||
        !resolve("dlcv_get_device_info", getDeviceInfoNative_) ||
        !resolve("dlcv_get_gpu_info", getGpuInfoNative_) ||
        !resolve("dlcv_keep_max_clock", keepMaxClockNative_) ||
        !resolve("dlcv_reset_max_clock", resetMaxClockNative_) ||
        !resolve("dlcv_set_gpu_max_clock", setGpuMaxClockNative_) ||
        !resolve("dlcv_reset_gpu_max_clock", resetGpuMaxClockNative_) ||
        !resolve("dlcv_get_power_scheme_guid", getPowerSchemeGuidNative_) ||
        !resolve("dlcv_set_power_scheme_guid", setPowerSchemeGuidNative_) ||
        !resolve("dlcv_get_power_scheme", getPowerSchemeNative_) ||
        !resolve("dlcv_set_power_scheme", setPowerSchemeNative_) ||
        !resolve("dlcv_set_current_process_affinity_to_big_cores", setCurrentProcessAffinityToBigCoresNative_) ||
        !resolve("dlcv_set_current_process_priority_highest", setCurrentProcessPriorityHighestNative_)) {
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

DlcvCResult DlcvInferApi::infer(int modelIndex, const DlcvCImageList* imageList) const {
    return infer_(modelIndex, imageList);
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
    keepMaxClockNative();
}

const char* DlcvInferApi::getGpuInfo() const {
    return getGpuInfoNative();
}

void DlcvInferApi::freeSystemString(const char* value) const {
    freeResultNative(value);
}

int DlcvInferApi::loadModelCompatible(const char* modelPath, int deviceId) const {
    return loadModelCompatible_(modelPath, deviceId);
}

int DlcvInferApi::freeModelCompatible(int modelIndex) const {
    return freeModelCompatible_(modelIndex);
}

DlcvCResult DlcvInferApi::inferCompatible(int modelIndex, const DlcvCImageList* imageList) const {
    return inferCompatible_(modelIndex, imageList);
}

void DlcvInferApi::freeModelResultCompatible(DlcvCResult* result) const {
    freeModelResultCompatible_(result);
}

const char* DlcvInferApi::loadModelNative(const char* configStr) const {
    return loadModelNative_(configStr);
}

const char* DlcvInferApi::freeModelNative(const char* configStr) const {
    return freeModelNative_(configStr);
}

const char* DlcvInferApi::getModelInfoNative(const char* configStr) const {
    return getModelInfoNative_(configStr);
}

const char* DlcvInferApi::inferNative(const char* configStr) const {
    return inferNative_(configStr);
}

void DlcvInferApi::freeModelResultNative(const char* configStr) const {
    freeModelResultNative_(configStr);
}

void DlcvInferApi::freeResultNative(const char* configStr) const {
    if (configStr != nullptr) {
        freeResultNative_(configStr);
    }
}

void DlcvInferApi::freeAllModelsNative() const {
    freeAllModelsNative_();
}

const char* DlcvInferApi::getDeviceInfoNative() const {
    return getDeviceInfoNative_();
}

const char* DlcvInferApi::getGpuInfoNative() const {
    return getGpuInfoNative_();
}

void DlcvInferApi::keepMaxClockNative() const {
    keepMaxClockNative_();
}

void DlcvInferApi::resetMaxClockNative() const {
    resetMaxClockNative_();
}

void DlcvInferApi::setGpuMaxClockNative(bool verbose) const {
    setGpuMaxClockNative_(verbose);
}

void DlcvInferApi::resetGpuMaxClockNative(bool verbose) const {
    resetGpuMaxClockNative_(verbose);
}

const char* DlcvInferApi::getPowerSchemeGuidNative(int verbose) const {
    return getPowerSchemeGuidNative_(verbose);
}

int DlcvInferApi::setPowerSchemeGuidNative(const char* schemeGuid, int verbose) const {
    return setPowerSchemeGuidNative_(schemeGuid, verbose);
}

const char* DlcvInferApi::getPowerSchemeNative(int verbose) const {
    return getPowerSchemeNative_(verbose);
}

int DlcvInferApi::setPowerSchemeNative(const char* schemeName, int verbose) const {
    return setPowerSchemeNative_(schemeName, verbose);
}

int DlcvInferApi::setCurrentProcessAffinityToBigCoresNative(int verbose) const {
    return setCurrentProcessAffinityToBigCoresNative_(verbose);
}

int DlcvInferApi::setCurrentProcessPriorityHighestNative(
    int preferRealtime,
    int verbose,
    int bindBigCores) const {
    return setCurrentProcessPriorityHighestNative_(preferRealtime, verbose, bindBigCores);
}

void DlcvInferApi::clearFunctions() {
    loadModel_ = nullptr;
    getLastError_ = nullptr;
    freeModel_ = nullptr;
    infer_ = nullptr;
    inferWithParams_ = nullptr;
    freeModelResult_ = nullptr;
    getModelInfo_ = nullptr;
    inferJson_ = nullptr;
    getAllDogInfo_ = nullptr;
    freeString_ = nullptr;
    freeAllModels_ = nullptr;
    loadModelCompatible_ = nullptr;
    freeModelCompatible_ = nullptr;
    inferCompatible_ = nullptr;
    freeModelResultCompatible_ = nullptr;
    loadModelNative_ = nullptr;
    freeModelNative_ = nullptr;
    getModelInfoNative_ = nullptr;
    inferNative_ = nullptr;
    freeModelResultNative_ = nullptr;
    freeResultNative_ = nullptr;
    freeAllModelsNative_ = nullptr;
    getDeviceInfoNative_ = nullptr;
    getGpuInfoNative_ = nullptr;
    keepMaxClockNative_ = nullptr;
    resetMaxClockNative_ = nullptr;
    setGpuMaxClockNative_ = nullptr;
    resetGpuMaxClockNative_ = nullptr;
    getPowerSchemeGuidNative_ = nullptr;
    setPowerSchemeGuidNative_ = nullptr;
    getPowerSchemeNative_ = nullptr;
    setPowerSchemeNative_ = nullptr;
    setCurrentProcessAffinityToBigCoresNative_ = nullptr;
    setCurrentProcessPriorityHighestNative_ = nullptr;
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
