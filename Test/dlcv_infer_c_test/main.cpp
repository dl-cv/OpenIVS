#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <iostream>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

#include <windows.h>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "dlcv_infer_cpp/dlcv_infer_c_api.h"

extern "C" int dlcv_infer_pure_c_header_test(void);

struct NativeCapi {
    using LoadModel = int(__stdcall*)(const char*, int);
    using FreeModel = int(__stdcall*)(int);
    using Infer = DlcvCResult(__stdcall*)(int, const DlcvCImageList*);
    using FreeResult = void(__stdcall*)(DlcvCResult*);

    HMODULE module = nullptr;
    LoadModel loadModel = nullptr;
    FreeModel freeModel = nullptr;
    Infer infer = nullptr;
    FreeResult freeResult = nullptr;

    ~NativeCapi() {
        if (module != nullptr) FreeLibrary(module);
    }
};

static std::string WideToUtf8(const std::wstring& value) {
    if (value.empty()) return {};
    const int bytes = WideCharToMultiByte(
        CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return {};
    std::string out(static_cast<size_t>(bytes), '\0');
    WideCharToMultiByte(
        CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), out.data(), bytes, nullptr, nullptr);
    return out;
}

static std::string WideToAnsi(const std::wstring& value) {
    if (value.empty()) return {};
    const int bytes = WideCharToMultiByte(
        CP_ACP, 0, value.c_str(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return {};
    std::string out(static_cast<size_t>(bytes), '\0');
    WideCharToMultiByte(
        CP_ACP, 0, value.c_str(), static_cast<int>(value.size()), out.data(), bytes, nullptr, nullptr);
    return out;
}

static cv::Mat ReadImageRgb(const std::wstring& path) {
    FILE* fp = nullptr;
    if (_wfopen_s(&fp, path.c_str(), L"rb") != 0 || fp == nullptr) {
        return {};
    }

    _fseeki64(fp, 0, SEEK_END);
    const __int64 size = _ftelli64(fp);
    _fseeki64(fp, 0, SEEK_SET);
    if (size <= 0) {
        std::fclose(fp);
        return {};
    }

    std::vector<unsigned char> bytes(static_cast<size_t>(size));
    const size_t readSize = std::fread(bytes.data(), 1, bytes.size(), fp);
    std::fclose(fp);
    if (readSize != bytes.size()) return {};

    cv::Mat image = cv::imdecode(bytes, cv::IMREAD_UNCHANGED);
    if (image.empty()) return {};
    if (image.channels() == 4) {
        cv::cvtColor(image, image, cv::COLOR_BGRA2RGB);
    } else if (image.channels() == 3) {
        cv::cvtColor(image, image, cv::COLOR_BGR2RGB);
    }
    if (!image.isContinuous()) image = image.clone();
    return image;
}

static long long Quantize(double value, double scale) {
    return static_cast<long long>(std::llround(value * scale));
}

static bool LoadNativeCapi(NativeCapi& api, std::string& error) {
    api.module = LoadLibraryW(L"C:\\dlcv\\Lib\\site-packages\\dlcvpro_infer\\dlcv_infer.dll");
    if (api.module == nullptr) {
        error = "dlcv_infer.dll 加载失败: " + std::to_string(GetLastError());
        return false;
    }
    api.loadModel = reinterpret_cast<NativeCapi::LoadModel>(GetProcAddress(api.module, "dlcv_load_model_c"));
    api.freeModel = reinterpret_cast<NativeCapi::FreeModel>(GetProcAddress(api.module, "dlcv_free_model_c"));
    api.infer = reinterpret_cast<NativeCapi::Infer>(GetProcAddress(api.module, "dlcv_infer_c"));
    api.freeResult = reinterpret_cast<NativeCapi::FreeResult>(GetProcAddress(api.module, "dlcv_free_model_result_c"));
    if (api.loadModel == nullptr || api.freeModel == nullptr || api.infer == nullptr || api.freeResult == nullptr) {
        error = "dlcv_infer.dll 结构化 C API 不完整";
        return false;
    }
    return true;
}

static bool CompareForwardedJsonCall(
    const char* name,
    const char* (DLCV_C_NATIVE_CALL* nativeCall)(const char*),
    const char* (DLCV_C_NATIVE_CALL* cCall)(const char*),
    void (DLCV_C_NATIVE_CALL* nativeFree)(const char*),
    void (DLCV_C_NATIVE_CALL* cFree)(const char*)) {
    const char* nativeResult = nativeCall("{}");
    const char* cResult = cCall("{}");
    const bool same = (nativeResult == nullptr && cResult == nullptr) ||
        (nativeResult != nullptr && cResult != nullptr && std::strcmp(nativeResult, cResult) == 0);
    if (!same) {
        std::cerr << "FAIL: " << name << " 透传失败输入返回不一致\n";
        std::cerr << "  原生 DLL: " << (nativeResult == nullptr ? "<null>" : nativeResult) << "\n";
        std::cerr << "  C API: " << (cResult == nullptr ? "<null>" : cResult) << "\n";
    }
    if (nativeResult != nullptr) nativeFree(nativeResult);
    if (cResult != nullptr) cFree(cResult);
    return same;
}

static bool RunCapiForwardingCheck(HMODULE cModule) {
    HMODULE nativeModule = GetModuleHandleW(L"dlcv_infer.dll");
    bool ownsNativeModule = false;
    if (nativeModule == nullptr) {
        nativeModule = LoadLibraryW(L"C:\\dlcv\\Lib\\site-packages\\dlcvpro_infer\\dlcv_infer.dll");
        ownsNativeModule = true;
    }
    if (nativeModule == nullptr) {
        std::cerr << "FAIL: 原生 dlcv_infer.dll 加载失败: " << GetLastError() << "\n";
        return false;
    }

    using NativeJsonCall = const char* (DLCV_C_NATIVE_CALL*)(const char*);
    using CJsonCall = const char* (DLCV_C_NATIVE_CALL*)(const char*);
    using NativeFreeResult = void (DLCV_C_NATIVE_CALL*)(const char*);
    using CFreeResult = void (DLCV_C_NATIVE_CALL*)(const char*);

    const auto nativeFreeResult = reinterpret_cast<NativeFreeResult>(
        GetProcAddress(nativeModule, "dlcv_free_result"));
    const auto cFreeResult = reinterpret_cast<CFreeResult>(
        GetProcAddress(cModule, "dlcv_free_result"));
    const char* jsonNames[] = {
        "dlcv_load_model",
        "dlcv_free_model",
        "dlcv_get_model_info",
        "dlcv_infer",
    };
    NativeJsonCall nativeCalls[] = {
        reinterpret_cast<NativeJsonCall>(GetProcAddress(nativeModule, "dlcv_load_model")),
        reinterpret_cast<NativeJsonCall>(GetProcAddress(nativeModule, "dlcv_free_model")),
        reinterpret_cast<NativeJsonCall>(GetProcAddress(nativeModule, "dlcv_get_model_info")),
        reinterpret_cast<NativeJsonCall>(GetProcAddress(nativeModule, "dlcv_infer")),
    };
    CJsonCall cCalls[] = {
        reinterpret_cast<CJsonCall>(GetProcAddress(cModule, "dlcv_load_model")),
        reinterpret_cast<CJsonCall>(GetProcAddress(cModule, "dlcv_free_model")),
        reinterpret_cast<CJsonCall>(GetProcAddress(cModule, "dlcv_get_model_info")),
        reinterpret_cast<CJsonCall>(GetProcAddress(cModule, "dlcv_infer")),
    };

    bool ok = nativeFreeResult != nullptr && cFreeResult != nullptr;
    if (!ok) {
        std::cerr << "FAIL: JSON 接口释放函数缺失\n";
    }
    for (size_t i = 0; i < 4; ++i) {
        if (nativeCalls[i] == nullptr || cCalls[i] == nullptr) {
            std::cerr << "FAIL: JSON 接口动态函数缺失 " << jsonNames[i] << "\n";
            ok = false;
            continue;
        }
        if (ok && !CompareForwardedJsonCall(
                jsonNames[i], nativeCalls[i], cCalls[i], nativeFreeResult, cFreeResult)) {
            ok = false;
        }
    }

    using NativeReadCall = const char* (DLCV_C_NATIVE_CALL*)();
    using CReadCall = const char* (DLCV_C_NATIVE_CALL*)();
    using NativePowerReadCall = const char* (DLCV_C_NATIVE_CALL*)(int);
    using CPowerReadCall = const char* (DLCV_C_NATIVE_CALL*)(int);
    const auto nativeGetDeviceInfo = reinterpret_cast<NativeReadCall>(
        GetProcAddress(nativeModule, "dlcv_get_device_info"));
    const auto cGetDeviceInfo = reinterpret_cast<CReadCall>(
        GetProcAddress(cModule, "dlcv_get_device_info"));
    const auto nativeGetGpuInfo = reinterpret_cast<NativeReadCall>(
        GetProcAddress(nativeModule, "dlcv_get_gpu_info"));
    const auto cGetGpuInfo = reinterpret_cast<CReadCall>(
        GetProcAddress(cModule, "dlcv_get_gpu_info"));
    const auto nativeGetPowerGuid = reinterpret_cast<NativePowerReadCall>(
        GetProcAddress(nativeModule, "dlcv_get_power_scheme_guid"));
    const auto cGetPowerGuid = reinterpret_cast<CPowerReadCall>(
        GetProcAddress(cModule, "dlcv_get_power_scheme_guid"));
    const auto nativeGetPowerScheme = reinterpret_cast<NativePowerReadCall>(
        GetProcAddress(nativeModule, "dlcv_get_power_scheme"));
    const auto cGetPowerScheme = reinterpret_cast<CPowerReadCall>(
        GetProcAddress(cModule, "dlcv_get_power_scheme"));

    if (nativeGetDeviceInfo == nullptr || cGetDeviceInfo == nullptr ||
        nativeGetGpuInfo == nullptr || cGetGpuInfo == nullptr ||
        nativeGetPowerGuid == nullptr || cGetPowerGuid == nullptr ||
        nativeGetPowerScheme == nullptr || cGetPowerScheme == nullptr) {
        std::cerr << "FAIL: 设备、GPU或电源读取接口动态函数缺失\n";
        ok = false;
    } else {
        const char* nativeDeviceInfo = nativeGetDeviceInfo();
        const char* cDeviceInfo = cGetDeviceInfo();
        const char* nativeGpuInfo = nativeGetGpuInfo();
        const char* cGpuInfo = cGetGpuInfo();
        const char* nativePowerGuid = nativeGetPowerGuid(0);
        const char* cPowerGuid = cGetPowerGuid(0);
        const char* nativePowerScheme = nativeGetPowerScheme(0);
        const char* cPowerScheme = cGetPowerScheme(0);

        const bool deviceInfoOk = nativeDeviceInfo != nullptr && nativeDeviceInfo[0] != '\0' &&
            cDeviceInfo != nullptr && cDeviceInfo[0] != '\0';
        const bool gpuInfoOk = nativeGpuInfo != nullptr && nativeGpuInfo[0] != '\0' &&
            cGpuInfo != nullptr && cGpuInfo[0] != '\0';
        const bool powerGuidOk = nativePowerGuid != nullptr && cPowerGuid != nullptr;
        const bool powerSchemeOk = nativePowerScheme != nullptr && cPowerScheme != nullptr;
        if (!deviceInfoOk || !gpuInfoOk || !powerGuidOk || !powerSchemeOk) {
            std::cerr << "FAIL: 设备、GPU或电源读取接口返回空结果\n";
            ok = false;
        }

        if (nativeDeviceInfo != nullptr) nativeFreeResult(nativeDeviceInfo);
        if (cDeviceInfo != nullptr) cFreeResult(cDeviceInfo);
        if (nativeGpuInfo != nullptr) nativeFreeResult(nativeGpuInfo);
        if (cGpuInfo != nullptr) cFreeResult(cGpuInfo);
        if (nativePowerGuid != nullptr) nativeFreeResult(nativePowerGuid);
        if (cPowerGuid != nullptr) cFreeResult(cPowerGuid);
        if (nativePowerScheme != nullptr) nativeFreeResult(nativePowerScheme);
        if (cPowerScheme != nullptr) cFreeResult(cPowerScheme);
    }

    if (ownsNativeModule) FreeLibrary(nativeModule);
    if (ok) {
        std::cout << "PASS: JSON 透传失败输入一致，设备/GPU/电源读取接口返回有效结果\n";
    }
    return ok;
}

static bool RunCapiExportCompletenessCheck() {
    HMODULE module = GetModuleHandleW(L"dlcv_infer_cpp.dll");
    bool ownsModule = false;
    if (module == nullptr) {
        module = LoadLibraryW(L"dlcv_infer_cpp.dll");
        ownsModule = true;
    }
    if (module == nullptr) {
        std::cerr << "FAIL: dlcv_infer_cpp.dll 加载失败: " << GetLastError() << "\n";
        return false;
    }

    static const char* expectedExports[] = {
        "dlcv_infer_cpp_load_model_c",
        "dlcv_infer_cpp_get_last_error_c",
        "dlcv_infer_cpp_free_model_c",
        "dlcv_infer_cpp_infer_c",
        "dlcv_infer_cpp_infer_with_params_c",
        "dlcv_infer_cpp_free_model_result_c",
        "dlcv_infer_cpp_get_model_info_c",
        "dlcv_infer_cpp_infer_json_c",
        "dlcv_infer_cpp_get_all_dog_info_c",
        "dlcv_infer_cpp_free_string_c",
        "dlcv_infer_cpp_free_all_models_c",
        "dlcv_load_model_c",
        "dlcv_free_model_c",
        "dlcv_infer_c",
        "dlcv_free_model_result_c",
        "dlcv_load_model",
        "dlcv_free_model",
        "dlcv_get_model_info",
        "dlcv_infer",
        "dlcv_free_model_result",
        "dlcv_free_result",
        "dlcv_free_all_models",
        "dlcv_get_device_info",
        "dlcv_get_gpu_info",
        "dlcv_keep_max_clock",
        "dlcv_reset_max_clock",
        "dlcv_set_gpu_max_clock",
        "dlcv_reset_gpu_max_clock",
        "dlcv_get_power_scheme_guid",
        "dlcv_set_power_scheme_guid",
        "dlcv_get_power_scheme",
        "dlcv_set_power_scheme",
        "dlcv_set_current_process_affinity_to_big_cores",
        "dlcv_set_current_process_priority_highest",
    };

    bool ok = true;
    bool exportsPresent = true;
    for (const char* name : expectedExports) {
        if (GetProcAddress(module, name) == nullptr) {
            std::cerr << "FAIL: dlcv_infer_cpp.dll 缺少 C 接口导出函数 " << name << "\n";
            ok = false;
            exportsPresent = false;
        }
    }

    using GetLastErrorFunc = const char* (*)();
    using GetJsonFunc = const char* (DLCV_C_NATIVE_CALL*)();
    using GetPowerSchemeFunc = const char* (DLCV_C_NATIVE_CALL*)(int);
    using FreeResultFunc = void (DLCV_C_NATIVE_CALL*)(const char*);

    if (ok) {
        const auto getLastError = reinterpret_cast<GetLastErrorFunc>(
            GetProcAddress(module, "dlcv_infer_cpp_get_last_error_c"));
        const auto getDeviceInfo = reinterpret_cast<GetJsonFunc>(
            GetProcAddress(module, "dlcv_get_device_info"));
        const auto getGpuInfo = reinterpret_cast<GetJsonFunc>(
            GetProcAddress(module, "dlcv_get_gpu_info"));
        const auto getPowerSchemeGuid = reinterpret_cast<GetPowerSchemeFunc>(
            GetProcAddress(module, "dlcv_get_power_scheme_guid"));
        const auto getPowerScheme = reinterpret_cast<GetPowerSchemeFunc>(
            GetProcAddress(module, "dlcv_get_power_scheme"));
        const auto freeResult = reinterpret_cast<FreeResultFunc>(
            GetProcAddress(module, "dlcv_free_result"));

        const char* lastError = getLastError();
        if (lastError == nullptr) {
            std::cerr << "FAIL: dlcv_infer_cpp_get_last_error_c 基础调用返回空指针\n";
            ok = false;
        }

        const char* deviceInfo = getDeviceInfo();
        const char* gpuInfo = getGpuInfo();
        const char* powerSchemeGuid = getPowerSchemeGuid(0);
        const char* powerScheme = getPowerScheme(0);
        if (deviceInfo == nullptr || gpuInfo == nullptr || powerSchemeGuid == nullptr || powerScheme == nullptr) {
            std::cerr << "FAIL: dlcv_infer_cpp.dll 只读信息接口基础调用返回空指针\n";
            ok = false;
        }
        if (deviceInfo != nullptr) freeResult(deviceInfo);
        if (gpuInfo != nullptr) freeResult(gpuInfo);
        if (powerSchemeGuid != nullptr) freeResult(powerSchemeGuid);
        if (powerScheme != nullptr) freeResult(powerScheme);
    }

    if (exportsPresent) ok = RunCapiForwardingCheck(module) && ok;
    if (ownsModule) FreeLibrary(module);
    if (ok) {
        std::cout << "PASS: dlcv_infer_cpp.dll 的 C 接口导出函数均存在，安全只读接口调用成功\n";
    }
    return ok;
}

static std::string BuildCompleteFingerprint(const DlcvCResult& result) {
    std::ostringstream out;
    out << result.code << '|'
        << (result.message == nullptr ? std::string() : std::string(result.message)) << '|'
        << result.n;
    for (int sampleIndex = 0; sampleIndex < result.n; ++sampleIndex) {
        const DlcvCSampleResult& sample = result.sample_results[sampleIndex];
        out << ";n=" << sample.n;
        for (int objectIndex = 0; objectIndex < sample.n; ++objectIndex) {
            const DlcvCObjectResult& object = sample.results[objectIndex];
            out << '[' << object.category_id << '|'
                << (object.category_name == nullptr ? std::string() : std::string(object.category_name)) << '|'
                << Quantize(object.score, 100000.0) << '|'
                << static_cast<int>(object.with_bbox) << '|'
                << Quantize(object.area, 1000.0) << '|'
                << Quantize(object.x, 1000.0) << ',' << Quantize(object.y, 1000.0) << ','
                << Quantize(object.w, 1000.0) << ',' << Quantize(object.h, 1000.0) << '|'
                << static_cast<int>(object.with_mask) << '|'
                << (object.mask.mask_ptr == 0 ? 0 : 1) << ',' << object.mask.height << ',' << object.mask.width << '|'
                << static_cast<int>(object.with_angle) << '|' << Quantize(object.angle, 1000.0) << '|'
                << static_cast<int>(object.with_mean) << '|'
                << Quantize(object.foreground_mean, 1000.0) << '|'
                << Quantize(object.background_mean, 1000.0) << ']';
        }
    }
    return out.str();
}

static bool IsReleasedResult(const DlcvCResult& result, int expectedCode) {
    return result.code == expectedCode && result.message == nullptr &&
        result.sample_results == nullptr && result.n == 0;
}

static bool RunNativeCompatibilityCheck(
    const std::wstring& modelPath,
    const cv::Mat& image) {
    NativeCapi native;
    std::string error;
    if (!LoadNativeCapi(native, error)) {
        std::cerr << "FAIL: " << error << "\n";
        return false;
    }

    const std::string ansiPath = WideToAnsi(modelPath);
    const int wrapperIndex = dlcv_load_model_c(ansiPath.c_str(), 0);
    const int nativeIndex = native.loadModel(ansiPath.c_str(), 0);
    if (wrapperIndex < 0 || nativeIndex < 0) {
        if (wrapperIndex >= 0) dlcv_free_model_c(wrapperIndex);
        if (nativeIndex >= 0) native.freeModel(nativeIndex);
        std::cerr << "FAIL: 两套结构化 C API 模型加载失败\n";
        return false;
    }

    DlcvCImage cImage{};
    cImage.data_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(image.data));
    cImage.height = image.rows;
    cImage.width = image.cols;
    cImage.channel = image.channels();
    DlcvCImageList imageList{};
    imageList.images = &cImage;
    imageList.n = 1;

    DlcvCResult wrapperResult = dlcv_infer_c(wrapperIndex, &imageList);
    DlcvCResult nativeResult = native.infer(nativeIndex, &imageList);
    const std::string wrapperSuccessFingerprint = BuildCompleteFingerprint(wrapperResult);
    const std::string nativeSuccessFingerprint = BuildCompleteFingerprint(nativeResult);
    const bool sameSuccessResult = wrapperSuccessFingerprint == nativeSuccessFingerprint;
    dlcv_free_model_result_c(&wrapperResult);
    native.freeResult(&nativeResult);
    const bool sameSuccessRelease = IsReleasedResult(wrapperResult, 0) && IsReleasedResult(nativeResult, 0);

    DlcvCResult wrapperMissing = dlcv_infer_c(-1, &imageList);
    DlcvCResult nativeMissing = native.infer(-1, &imageList);
    const std::string wrapperFailureFingerprint = BuildCompleteFingerprint(wrapperMissing);
    const std::string nativeFailureFingerprint = BuildCompleteFingerprint(nativeMissing);
    const bool sameFailureResult = wrapperFailureFingerprint == nativeFailureFingerprint &&
        wrapperMissing.sample_results == nullptr && nativeMissing.sample_results == nullptr &&
        wrapperMissing.n == 0 && nativeMissing.n == 0;
    dlcv_free_model_result_c(&wrapperMissing);
    native.freeResult(&nativeMissing);
    const bool sameFailureRelease = IsReleasedResult(wrapperMissing, 2) && IsReleasedResult(nativeMissing, 2);

    const int wrapperFirstFree = dlcv_free_model_c(wrapperIndex);
    const int wrapperSecondFree = dlcv_free_model_c(wrapperIndex);
    const int nativeFirstFree = native.freeModel(nativeIndex);
    const int nativeSecondFree = native.freeModel(nativeIndex);
    const bool wrapperFreeOk = wrapperFirstFree == 0 && wrapperSecondFree == -1;
    const bool nativeFreeOk = nativeFirstFree == 0 && nativeSecondFree == -1;
    if (!sameSuccessResult || !sameSuccessRelease || !sameFailureResult || !sameFailureRelease ||
        !wrapperFreeOk || !nativeFreeOk) {
        std::cerr << "FAIL: 两套结构化 C API 输入输出不一致\n";
        if (!sameSuccessResult) {
            std::cerr << "  C API 成功结果: " << wrapperSuccessFingerprint << "\n";
            std::cerr << "  dlcv_infer 成功结果: " << nativeSuccessFingerprint << "\n";
        }
        if (!sameFailureResult) {
            std::cerr << "  C API 失败结果: " << wrapperFailureFingerprint << "\n";
            std::cerr << "  dlcv_infer 失败结果: " << nativeFailureFingerprint << "\n";
        }
        if (!sameSuccessRelease || !sameFailureRelease) {
            std::cerr << "  结果释放状态不一致\n";
        }
        if (!wrapperFreeOk || !nativeFreeOk) {
            std::cerr << "  C API 模型释放返回值: " << wrapperFirstFree << ", " << wrapperSecondFree << "\n";
            std::cerr << "  dlcv_infer 模型释放返回值: " << nativeFirstFree << ", " << nativeSecondFree << "\n";
        }
        return false;
    }
    std::cout << "PASS: 两套结构化 C API 输入输出一致\n";
    return true;
}

static bool RunCompatibilityFlowCheck(
    const std::wstring& modelPath,
    const cv::Mat& image) {
    const std::string ansiPath = WideToAnsi(modelPath);
    const int modelIndex = dlcv_load_model_c(ansiPath.c_str(), 0);
    if (modelIndex < 0) {
        std::cerr << "FAIL: 同名兼容入口加载 dvst 失败\n";
        return false;
    }

    DlcvCImage cImage{};
    cImage.data_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(image.data));
    cImage.height = image.rows;
    cImage.width = image.cols;
    cImage.channel = image.channels();
    DlcvCImageList imageList{};
    imageList.images = &cImage;
    imageList.n = 1;

    DlcvCResult result = dlcv_infer_c(modelIndex, &imageList);
    const bool inferOk = result.code == 0 && result.message != nullptr &&
        std::strcmp(result.message, "Success") == 0 && result.n == 1 && result.sample_results != nullptr;
    dlcv_free_model_result_c(&result);
    const bool resultFreeOk = IsReleasedResult(result, 0);
    const bool modelFreeOk = dlcv_free_model_c(modelIndex) == 0 && dlcv_free_model_c(modelIndex) == -1;
    if (!inferOk || !resultFreeOk || !modelFreeOk) {
        std::cerr << "FAIL: 同名兼容入口 dvst 验证失败\n";
        return false;
    }
    std::cout << "PASS: 同名兼容入口支持 dvst\n";
    return true;
}

static std::string BuildResultFingerprint(const DlcvCResult& result) {
    std::ostringstream out;
    out << "samples=" << result.n;
    for (int sampleIndex = 0; sampleIndex < result.n; ++sampleIndex) {
        const DlcvCSampleResult& sample = result.sample_results[sampleIndex];
        std::vector<std::string> objects;
        objects.reserve(static_cast<size_t>(std::max(0, sample.n)));
        for (int objectIndex = 0; objectIndex < sample.n; ++objectIndex) {
            const DlcvCObjectResult& object = sample.results[objectIndex];
            std::ostringstream item;
            item << object.category_id << '|'
                 << (object.category_name == nullptr ? std::string() : std::string(object.category_name)) << '|'
                 << Quantize(object.score, 100000.0) << '|'
                 << static_cast<int>(object.with_bbox) << '|'
                 << Quantize(object.x, 1000.0) << ','
                 << Quantize(object.y, 1000.0) << ','
                 << Quantize(object.w, 1000.0) << ','
                 << Quantize(object.h, 1000.0) << '|'
                 << static_cast<int>(object.with_angle) << '|'
                 << Quantize(object.angle, 1000.0) << '|'
                 << static_cast<int>(object.with_mean) << '|'
                 << Quantize(object.foreground_mean, 1000.0) << '|'
                 << Quantize(object.background_mean, 1000.0);
            objects.push_back(item.str());
        }
        std::sort(objects.begin(), objects.end());
        out << ";objects=" << objects.size();
        for (const auto& item : objects) out << '[' << item << ']';
    }
    return out.str();
}

static bool InferFingerprint(
    int modelIndex,
    const cv::Mat& image,
    std::string& fingerprint,
    std::string& error) {
    DlcvCImage cImage{};
    cImage.data_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(image.data));
    cImage.height = image.rows;
    cImage.width = image.cols;
    cImage.channel = image.channels();

    DlcvCImageList imageList{};
    imageList.images = &cImage;
    imageList.n = 1;

    const char* params = R"({"threshold":0.5,"with_mask":false})";
    DlcvCResult result = dlcv_infer_cpp_infer_with_params_c(modelIndex, &imageList, params);
    if (result.code != 0) {
        error = result.message == nullptr ? "infer failed" : result.message;
        dlcv_infer_cpp_free_model_result_c(&result);
        return false;
    }
    if (result.n != 1 || result.sample_results == nullptr) {
        error = "sample result count mismatch";
        dlcv_infer_cpp_free_model_result_c(&result);
        return false;
    }

    fingerprint = BuildResultFingerprint(result);
    dlcv_infer_cpp_free_model_result_c(&result);
    return true;
}

static bool RunConcurrentConsistency(
    const std::string& label,
    int modelIndex,
    const cv::Mat& image,
    const std::string& expectedFingerprint,
    int threadCount,
    int iterationsPerThread) {
    std::atomic<int> ready{0};
    std::atomic<bool> start{false};
    std::atomic<int> failed{0};
    std::mutex errorMutex;
    std::string firstError;
    std::vector<std::thread> workers;
    workers.reserve(static_cast<size_t>(threadCount));

    for (int threadIndex = 0; threadIndex < threadCount; ++threadIndex) {
        workers.emplace_back([&]() {
            ready.fetch_add(1);
            while (!start.load()) std::this_thread::yield();
            for (int iteration = 0; iteration < iterationsPerThread; ++iteration) {
                std::string fingerprint;
                std::string error;
                const bool ok = InferFingerprint(modelIndex, image, fingerprint, error);
                if (!ok || fingerprint != expectedFingerprint) {
                    failed.fetch_add(1);
                    std::lock_guard<std::mutex> lock(errorMutex);
                    if (firstError.empty()) {
                        firstError = ok ? "result fingerprint mismatch" : error;
                    }
                }
            }
        });
    }

    while (ready.load() != threadCount) std::this_thread::yield();
    start.store(true);
    for (auto& worker : workers) worker.join();

    if (failed.load() != 0) {
        std::cerr << "FAIL: " << label << "，失败次数=" << failed.load()
                  << "，首个错误=" << firstError << "\n";
        return false;
    }
    std::cout << "PASS: " << label << "，总次数="
              << threadCount * iterationsPerThread << "\n";
    return true;
}

static int LoadModel(const std::wstring& path) {
    const std::string utf8Path = WideToUtf8(path);
    const int modelIndex = dlcv_infer_cpp_load_model_c(utf8Path.c_str(), 0);
    if (modelIndex < 0) {
        const char* error = dlcv_infer_cpp_get_last_error_c();
        std::cerr << "模型加载失败: " << (error == nullptr ? "unknown" : error) << "\n";
    }
    return modelIndex;
}

int main() {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    if (dlcv_infer_pure_c_header_test() != 2) {
        std::cerr << "FAIL: 纯 C 结构化接口调用失败\n";
        return 1;
    }
    std::cout << "PASS: 纯 C 结构化接口编译和调用成功\n";

    const std::wstring dvtPath = L"Y:\\测试模型\\猫狗-分类_120_50_s.dvt";
    const std::wstring dvtImagePath = L"Y:\\测试模型\\猫狗-狗.jpg";
    const std::wstring dvstPath = L"Y:\\测试模型\\AOI_120_50_s.dvst";
    const std::wstring dvstImagePath = L"Y:\\测试模型\\AOI-1.jpg";

    const cv::Mat dvtImage = ReadImageRgb(dvtImagePath);
    const cv::Mat dvstImage = ReadImageRgb(dvstImagePath);
    if (dvtImage.empty() || dvstImage.empty()) {
        std::cerr << "FAIL: 测试图片读取失败\n";
        return 1;
    }

    bool ok = true;
    ok = RunCapiExportCompletenessCheck() && ok;
    ok = RunNativeCompatibilityCheck(dvtPath, dvtImage) && ok;
    ok = RunCompatibilityFlowCheck(dvstPath, dvstImage) && ok;

    int dvtIndex = LoadModel(dvtPath);
    if (dvtIndex < 0) return 1;
    std::string dvtBaseline;
    std::string error;
    if (!InferFingerprint(dvtIndex, dvtImage, dvtBaseline, error)) {
        std::cerr << "FAIL: dvt 基准推理失败: " << error << "\n";
        ok = false;
    } else {
        ok = RunConcurrentConsistency("dvt 同一实例并发", dvtIndex, dvtImage, dvtBaseline, 4, 10) && ok;
    }
    ok = (dlcv_infer_cpp_free_model_c(dvtIndex) == 0) && ok;

    int dvstIndex = LoadModel(dvstPath);
    if (dvstIndex < 0) return 1;
    std::string dvstBaseline;
    error.clear();
    if (!InferFingerprint(dvstIndex, dvstImage, dvstBaseline, error)) {
        std::cerr << "FAIL: dvst 基准推理失败: " << error << "\n";
        ok = false;
    } else {
        ok = RunConcurrentConsistency("dvst 同一实例并发", dvstIndex, dvstImage, dvstBaseline, 4, 10) && ok;
    }
    ok = (dlcv_infer_cpp_free_model_c(dvstIndex) == 0) && ok;

    int freshDvstIndex = LoadModel(dvstPath);
    if (freshDvstIndex < 0) return 1;
    ok = RunConcurrentConsistency(
        "dvst 全新实例首次并发", freshDvstIndex, dvstImage, dvstBaseline, 4, 10) && ok;
    ok = (dlcv_infer_cpp_free_model_c(freshDvstIndex) == 0) && ok;

    std::cout << (ok ? "\nTest PASSED\n" : "\nTest FAILED\n");
    return ok ? 0 : 1;
}
