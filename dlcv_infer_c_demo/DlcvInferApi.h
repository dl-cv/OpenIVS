#pragma once

#include <Windows.h>

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
    const std::wstring& lastLoadError() const;

    int loadModel(const char* modelPath, int deviceId) const;
    const char* getLastError() const;
    int freeModel(int modelIndex) const;
    DlcvCResult inferWithParams(
        int modelIndex,
        const DlcvCImageList* imageList,
        const char* paramsJson) const;
    void freeModelResult(DlcvCResult* result) const;
    const char* getModelInfo(int modelIndex) const;
    void freeString(const char* value) const;
    void freeAllModels() const;

private:
    using LoadModelFunction = int(__cdecl*)(const char*, int);
    using GetLastErrorFunction = const char* (__cdecl*)();
    using FreeModelFunction = int(__cdecl*)(int);
    using InferWithParamsFunction = DlcvCResult(__cdecl*)(int, const DlcvCImageList*, const char*);
    using FreeModelResultFunction = void(__cdecl*)(DlcvCResult*);
    using GetModelInfoFunction = const char* (__cdecl*)(int);
    using FreeStringFunction = void(__cdecl*)(const char*);
    using FreeAllModelsFunction = void(__cdecl*)();

    template <typename Function>
    bool resolve(const char* name, Function& function) {
        function = reinterpret_cast<Function>(GetProcAddress(module_, name));
        if (function != nullptr) {
            return true;
        }
        lastLoadError_ = L"dlcv_infer_cpp.dll 缺少函数：" + utf8ToWide(name) +
            L"。系统错误：" + formatSystemError(GetLastError());
        return false;
    }

    void clearFunctions();
    static std::wstring utf8ToWide(const char* value);
    static std::wstring formatSystemError(DWORD errorCode);

    HMODULE module_ = nullptr;
    std::wstring lastLoadError_;
    LoadModelFunction loadModel_ = nullptr;
    GetLastErrorFunction getLastError_ = nullptr;
    FreeModelFunction freeModel_ = nullptr;
    InferWithParamsFunction inferWithParams_ = nullptr;
    FreeModelResultFunction freeModelResult_ = nullptr;
    GetModelInfoFunction getModelInfo_ = nullptr;
    FreeStringFunction freeString_ = nullptr;
    FreeAllModelsFunction freeAllModels_ = nullptr;
};
