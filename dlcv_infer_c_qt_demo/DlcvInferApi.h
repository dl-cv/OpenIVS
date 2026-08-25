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

private:
    using LoadModelFunction = int(__cdecl*)(const char*, int);
    using GetLastErrorFunction = const char* (__cdecl*)();
    using FreeModelFunction = int(__cdecl*)(int);
    using InferWithParamsFunction = DlcvCResult(__cdecl*)(int, const DlcvCImageList*, const char*);
    using FreeModelResultFunction = void(__cdecl*)(DlcvCResult*);
    using GetModelInfoFunction = const char* (__cdecl*)(int);
    using InferJsonFunction = const char* (__cdecl*)(int, const DlcvCImage*, const char*);
    using GetAllDogInfoFunction = const char* (__cdecl*)();
    using FreeStringFunction = void(__cdecl*)(const char*);
    using FreeAllModelsFunction = void(__cdecl*)();

    using KeepMaxClockFunction = void(__stdcall*)();
    using GetGpuInfoFunction = const char* (__stdcall*)();
    using FreeSystemStringFunction = void(__stdcall*)(const char*);

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
    InferWithParamsFunction inferWithParams_ = nullptr;
    FreeModelResultFunction freeModelResult_ = nullptr;
    GetModelInfoFunction getModelInfo_ = nullptr;
    InferJsonFunction inferJson_ = nullptr;
    GetAllDogInfoFunction getAllDogInfo_ = nullptr;
    FreeStringFunction freeString_ = nullptr;
    FreeAllModelsFunction freeAllModels_ = nullptr;
    KeepMaxClockFunction keepMaxClock_ = nullptr;
    GetGpuInfoFunction getGpuInfo_ = nullptr;
    FreeSystemStringFunction freeSystemString_ = nullptr;
};
