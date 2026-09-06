#pragma once

#include <Windows.h>

#include <cstring>
#include <string>

#include "dlcv_infer_c_api.h"

class DlcvInferApi {
public:
    DlcvInferApi() = default;
    ~DlcvInferApi();

    DlcvInferApi(const DlcvInferApi&) = delete;
    DlcvInferApi& operator=(const DlcvInferApi&) = delete;

    bool load();
    void unload();
    bool isLoaded() const;
    const std::wstring& lastError() const;

    int loadModel(const char* modelPath, int deviceId) const;
    const char* getLastError() const;
    int freeModel(int modelIndex) const;
    DlcvCResult infer(int modelIndex, const DlcvCImageList* imageList) const;
    DlcvCResult inferWithParams(int modelIndex, const DlcvCImageList* imageList, const char* paramsJson) const;
    void freeModelResult(DlcvCResult* result) const;
    const char* getModelInfo(int modelIndex) const;
    const char* inferJson(int modelIndex, const DlcvCImage* image, const char* paramsJson) const;
    const char* getAllDogInfo() const;
    void freeString(const char* value) const;
    void freeAllModels() const;

    void keepMaxClock() const;
    const char* getGpuInfo() const;
    void freeSystemString(const char* value) const;

    int loadModelCompatible(const char* modelPath, int deviceId) const;
    int freeModelCompatible(int modelIndex) const;
    DlcvCResult inferCompatible(int modelIndex, const DlcvCImageList* imageList) const;
    void freeModelResultCompatible(DlcvCResult* result) const;

    const char* loadModelNative(const char* configStr) const;
    const char* freeModelNative(const char* configStr) const;
    const char* getModelInfoNative(const char* configStr) const;
    const char* inferNative(const char* configStr) const;
    void freeModelResultNative(const char* configStr) const;
    void freeResultNative(const char* configStr) const;
    void freeAllModelsNative() const;
    const char* getDeviceInfoNative() const;
    const char* getGpuInfoNative() const;
    void keepMaxClockNative() const;
    void resetMaxClockNative() const;
    void setGpuMaxClockNative(bool verbose) const;
    void resetGpuMaxClockNative(bool verbose) const;
    const char* getPowerSchemeGuidNative(int verbose) const;
    int setPowerSchemeGuidNative(const char* schemeGuid, int verbose) const;
    const char* getPowerSchemeNative(int verbose) const;
    int setPowerSchemeNative(const char* schemeName, int verbose) const;
    int setCurrentProcessAffinityToBigCoresNative(int verbose) const;
    int setCurrentProcessPriorityHighestNative(int preferRealtime, int verbose, int bindBigCores) const;

private:
    using LoadModelFunction = int(__cdecl*)(const char*, int);
    using GetLastErrorFunction = const char* (__cdecl*)();
    using FreeModelFunction = int(__cdecl*)(int);
    using InferFunction = DlcvCResult(__cdecl*)(int, const DlcvCImageList*);
    using InferWithParamsFunction = DlcvCResult(__cdecl*)(int, const DlcvCImageList*, const char*);
    using FreeModelResultFunction = void(__cdecl*)(DlcvCResult*);
    using GetModelInfoFunction = const char* (__cdecl*)(int);
    using InferJsonFunction = const char* (__cdecl*)(int, const DlcvCImage*, const char*);
    using GetAllDogInfoFunction = const char* (__cdecl*)();
    using FreeStringFunction = void(__cdecl*)(const char*);
    using FreeAllModelsFunction = void(__cdecl*)();

    using LoadModelCompatibleFunction = int(DLCV_C_NATIVE_CALL*)(const char*, int);
    using FreeModelCompatibleFunction = int(DLCV_C_NATIVE_CALL*)(int);
    using InferCompatibleFunction = DlcvCResult(DLCV_C_NATIVE_CALL*)(int, const DlcvCImageList*);
    using FreeModelResultCompatibleFunction = void(DLCV_C_NATIVE_CALL*)(DlcvCResult*);

    using LoadModelNativeFunction = const char* (DLCV_NATIVE_C_CALL*)(const char*);
    using FreeModelNativeFunction = const char* (DLCV_NATIVE_C_CALL*)(const char*);
    using GetModelInfoNativeFunction = const char* (DLCV_NATIVE_C_CALL*)(const char*);
    using InferNativeFunction = const char* (DLCV_NATIVE_C_CALL*)(const char*);
    using FreeModelResultNativeFunction = void(DLCV_NATIVE_C_CALL*)(const char*);
    using FreeResultNativeFunction = void(DLCV_NATIVE_C_CALL*)(const char*);
    using FreeAllModelsNativeFunction = void(DLCV_NATIVE_C_CALL*)();
    using GetDeviceInfoNativeFunction = const char* (DLCV_NATIVE_C_CALL*)();
    using GetGpuInfoNativeFunction = const char* (DLCV_NATIVE_C_CALL*)();
    using KeepMaxClockNativeFunction = void(DLCV_NATIVE_C_CALL*)();
    using ResetMaxClockNativeFunction = void(DLCV_NATIVE_C_CALL*)();
    using SetGpuMaxClockNativeFunction = void(DLCV_NATIVE_C_CALL*)(bool);
    using ResetGpuMaxClockNativeFunction = void(DLCV_NATIVE_C_CALL*)(bool);
    using GetPowerSchemeGuidNativeFunction = const char* (DLCV_NATIVE_C_CALL*)(int);
    using SetPowerSchemeGuidNativeFunction = int(DLCV_NATIVE_C_CALL*)(const char*, int);
    using GetPowerSchemeNativeFunction = const char* (DLCV_NATIVE_C_CALL*)(int);
    using SetPowerSchemeNativeFunction = int(DLCV_NATIVE_C_CALL*)(const char*, int);
    using SetCurrentProcessAffinityToBigCoresNativeFunction = int(DLCV_NATIVE_C_CALL*)(int);
    using SetCurrentProcessPriorityHighestNativeFunction = int(DLCV_NATIVE_C_CALL*)(int, int, int);

    template <typename Function>
    bool resolve(const char* name, Function& function) {
        function = reinterpret_cast<Function>(GetProcAddress(module_, name));
        if (function != nullptr) {
            return true;
        }
        lastError_ = L"在 dlcv_infer_cpp.dll 中找不到导出函数 \"" +
            std::wstring(name, name + std::strlen(name)) + L"\"。系统错误：" + formatSystemError(GetLastError());
        return false;
    }

    void clearFunctions();
    static std::wstring formatSystemError(DWORD errorCode);

    HMODULE module_ = nullptr;
    std::wstring lastError_;

    LoadModelFunction loadModel_ = nullptr;
    GetLastErrorFunction getLastError_ = nullptr;
    FreeModelFunction freeModel_ = nullptr;
    InferFunction infer_ = nullptr;
    InferWithParamsFunction inferWithParams_ = nullptr;
    FreeModelResultFunction freeModelResult_ = nullptr;
    GetModelInfoFunction getModelInfo_ = nullptr;
    InferJsonFunction inferJson_ = nullptr;
    GetAllDogInfoFunction getAllDogInfo_ = nullptr;
    FreeStringFunction freeString_ = nullptr;
    FreeAllModelsFunction freeAllModels_ = nullptr;
    LoadModelCompatibleFunction loadModelCompatible_ = nullptr;
    FreeModelCompatibleFunction freeModelCompatible_ = nullptr;
    InferCompatibleFunction inferCompatible_ = nullptr;
    FreeModelResultCompatibleFunction freeModelResultCompatible_ = nullptr;
    LoadModelNativeFunction loadModelNative_ = nullptr;
    FreeModelNativeFunction freeModelNative_ = nullptr;
    GetModelInfoNativeFunction getModelInfoNative_ = nullptr;
    InferNativeFunction inferNative_ = nullptr;
    FreeModelResultNativeFunction freeModelResultNative_ = nullptr;
    FreeResultNativeFunction freeResultNative_ = nullptr;
    FreeAllModelsNativeFunction freeAllModelsNative_ = nullptr;
    GetDeviceInfoNativeFunction getDeviceInfoNative_ = nullptr;
    GetGpuInfoNativeFunction getGpuInfoNative_ = nullptr;
    KeepMaxClockNativeFunction keepMaxClockNative_ = nullptr;
    ResetMaxClockNativeFunction resetMaxClockNative_ = nullptr;
    SetGpuMaxClockNativeFunction setGpuMaxClockNative_ = nullptr;
    ResetGpuMaxClockNativeFunction resetGpuMaxClockNative_ = nullptr;
    GetPowerSchemeGuidNativeFunction getPowerSchemeGuidNative_ = nullptr;
    SetPowerSchemeGuidNativeFunction setPowerSchemeGuidNative_ = nullptr;
    GetPowerSchemeNativeFunction getPowerSchemeNative_ = nullptr;
    SetPowerSchemeNativeFunction setPowerSchemeNative_ = nullptr;
    SetCurrentProcessAffinityToBigCoresNativeFunction setCurrentProcessAffinityToBigCoresNative_ = nullptr;
    SetCurrentProcessPriorityHighestNativeFunction setCurrentProcessPriorityHighestNative_ = nullptr;
};
