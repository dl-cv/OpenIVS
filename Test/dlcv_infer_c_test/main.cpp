#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iostream>
#include <iterator>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

#include <windows.h>
#include <psapi.h>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#define DLCV_NATIVE_C_API_SKIP_INFER_EXPORT
#define dlcv_infer dlcv_infer_json_impl
#include "dlcv_infer_cpp/dlcv_infer_c_api.h"
#undef dlcv_infer
#undef DLCV_NATIVE_C_API_SKIP_INFER_EXPORT
#include "dlcv_infer_cpp/dlcv_infer.h"
#include "dlcv_infer_cpp/flow/modules/ModelModules.h"

#pragma comment(lib, "psapi.lib")

extern "C" int dlcv_infer_pure_c_header_test(void);
extern "C" int dlcv_infer_pure_c_invalid_input_test(void);

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

struct NativeJsonApi {
    using StringCall = const char* (DLCV_C_NATIVE_CALL*)(const char*);
    using FreeString = void (DLCV_C_NATIVE_CALL*)(const char*);
    using FreeAll = void (DLCV_C_NATIVE_CALL*)();

    HMODULE module = nullptr;
    StringCall loadModel = nullptr;
    StringCall freeModel = nullptr;
    StringCall getModelInfo = nullptr;
    StringCall infer = nullptr;
    FreeString freeModelResult = nullptr;
    FreeString freeResult = nullptr;
    FreeAll freeAllModels = nullptr;

    ~NativeJsonApi() {
        if (module != nullptr) FreeLibrary(module);
    }
};

struct NativeJsonModelCleanup {
    ~NativeJsonModelCleanup() {
        dlcv_free_all_models();
    }
};

static bool LoadNativeJsonApi(NativeJsonApi& api, std::string& error);
static bool CopyJsonCallResult(
    NativeJsonApi::StringCall call,
    NativeJsonApi::FreeString release,
    const std::string& config,
    std::string& result,
    std::string& error);

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
    const int invalidInputCode = dlcv_infer_pure_c_invalid_input_test();
    if (invalidInputCode != 0) {
        std::cerr << "FAIL: C 接口异常输入兼容性检查失败，返回码="
                  << invalidInputCode << "\n";
        ok = false;
    } else {
        std::cout << "PASS: C 接口异常输入兼容性检查通过\n";
    }
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

static bool NearlyEqual(double left, double right, double tolerance = 1e-5) {
    return std::abs(left - right) <= tolerance;
}

static bool CompareMask(
    const cv::Mat& cppMask,
    const DlcvCMask& cMask,
    std::string& error) {
    if (cppMask.empty()) {
        if (cMask.mask_ptr != 0 || cMask.width != 0 || cMask.height != 0) {
            error = "C++ mask 为空，但 C mask 不为空";
            return false;
        }
        return true;
    }
    if (cppMask.type() != CV_8UC1) {
        error = "C++ mask 不是 CV_8UC1";
        return false;
    }
    if (cMask.mask_ptr == 0 || cMask.width != cppMask.cols || cMask.height != cppMask.rows) {
        std::ostringstream out;
        out << "mask 尺寸不一致: C++=" << cppMask.cols << 'x' << cppMask.rows
            << " C=" << cMask.width << 'x' << cMask.height;
        error = out.str();
        return false;
    }

    const auto* cData = reinterpret_cast<const unsigned char*>(
        static_cast<uintptr_t>(cMask.mask_ptr));
    const size_t rowBytes = static_cast<size_t>(cppMask.cols);
    for (int row = 0; row < cppMask.rows; ++row) {
        if (std::memcmp(cppMask.ptr<unsigned char>(row), cData + rowBytes * row, rowBytes) != 0) {
            error = "mask 像素内容不一致";
            return false;
        }
    }
    return true;
}

static bool CompareCppAndCResult(
    const dlcv_infer::Result& cppResult,
    const DlcvCResult& cResult,
    std::string& error) {
    if (cResult.code != 0 || cResult.n != static_cast<int>(cppResult.sampleResults.size())) {
        error = "返回码或样本数不一致";
        return false;
    }
    if (cResult.n > 0 && cResult.sample_results == nullptr) {
        error = "C 结果缺少样本数据";
        return false;
    }

    for (int sampleIndex = 0; sampleIndex < cResult.n; ++sampleIndex) {
        const auto& cppSample = cppResult.sampleResults[static_cast<size_t>(sampleIndex)];
        const DlcvCSampleResult& cSample = cResult.sample_results[sampleIndex];
        if (cSample.n != static_cast<int>(cppSample.results.size()) ||
            (cSample.n > 0 && cSample.results == nullptr)) {
            error = "目标数或目标数据不一致";
            return false;
        }

        for (int objectIndex = 0; objectIndex < cSample.n; ++objectIndex) {
            const auto& cppObject = cppSample.results[static_cast<size_t>(objectIndex)];
            const DlcvCObjectResult& cObject = cSample.results[objectIndex];
            const std::string cCategory = cObject.category_name == nullptr
                ? std::string()
                : std::string(cObject.category_name);
            const double cppX = cppObject.bbox.size() >= 4 ? cppObject.bbox[0] : 0.0;
            const double cppY = cppObject.bbox.size() >= 4 ? cppObject.bbox[1] : 0.0;
            const double cppW = cppObject.bbox.size() >= 4 ? cppObject.bbox[2] : 0.0;
            const double cppH = cppObject.bbox.size() >= 4 ? cppObject.bbox[3] : 0.0;

            if (cObject.category_id != cppObject.categoryId ||
                cCategory != cppObject.categoryName ||
                cObject.with_bbox != cppObject.withBbox ||
                cObject.with_mask != cppObject.withMask ||
                cObject.with_angle != cppObject.withAngle ||
                cObject.with_mean != cppObject.withMean ||
                !NearlyEqual(cObject.score, cppObject.score) ||
                !NearlyEqual(cObject.area, cppObject.area) ||
                !NearlyEqual(cObject.x, cppX) ||
                !NearlyEqual(cObject.y, cppY) ||
                !NearlyEqual(cObject.w, cppW) ||
                !NearlyEqual(cObject.h, cppH) ||
                !NearlyEqual(cObject.angle, cppObject.angle) ||
                !NearlyEqual(cObject.foreground_mean, cppObject.foregroundMean) ||
                !NearlyEqual(cObject.background_mean, cppObject.backgroundMean)) {
                std::ostringstream out;
                out << "样本 " << sampleIndex << " 目标 " << objectIndex << " 字段不一致";
                error = out.str();
                return false;
            }
            if (!CompareMask(cppObject.mask, cObject.mask, error)) {
                std::ostringstream out;
                out << "样本 " << sampleIndex << " 目标 " << objectIndex << ' ' << error;
                error = out.str();
                return false;
            }
        }
    }
    return true;
}

static bool CompareCResults(
    const DlcvCResult& left,
    const DlcvCResult& right,
    std::string& error) {
    if (left.code != right.code ||
        (left.message == nullptr) != (right.message == nullptr) ||
        (left.message != nullptr && std::strcmp(left.message, right.message) != 0) ||
        left.n != right.n) {
        error = "返回码、消息或样本数不一致";
        return false;
    }
    if ((left.n > 0 && left.sample_results == nullptr) ||
        (right.n > 0 && right.sample_results == nullptr)) {
        error = "样本数据为空";
        return false;
    }

    for (int sampleIndex = 0; sampleIndex < left.n; ++sampleIndex) {
        const DlcvCSampleResult& leftSample = left.sample_results[sampleIndex];
        const DlcvCSampleResult& rightSample = right.sample_results[sampleIndex];
        if (leftSample.n != rightSample.n ||
            (leftSample.n > 0 && (leftSample.results == nullptr || rightSample.results == nullptr))) {
            error = "目标数或目标数据不一致";
            return false;
        }

        for (int objectIndex = 0; objectIndex < leftSample.n; ++objectIndex) {
            const DlcvCObjectResult& leftObject = leftSample.results[objectIndex];
            const DlcvCObjectResult& rightObject = rightSample.results[objectIndex];
            const std::string leftCategory = leftObject.category_name == nullptr
                ? std::string()
                : std::string(leftObject.category_name);
            const std::string rightCategory = rightObject.category_name == nullptr
                ? std::string()
                : std::string(rightObject.category_name);
            if (leftObject.category_id != rightObject.category_id ||
                leftCategory != rightCategory ||
                leftObject.with_bbox != rightObject.with_bbox ||
                leftObject.with_mask != rightObject.with_mask ||
                leftObject.with_angle != rightObject.with_angle ||
                leftObject.with_mean != rightObject.with_mean ||
                !NearlyEqual(leftObject.score, rightObject.score) ||
                !NearlyEqual(leftObject.area, rightObject.area) ||
                !NearlyEqual(leftObject.x, rightObject.x) ||
                !NearlyEqual(leftObject.y, rightObject.y) ||
                !NearlyEqual(leftObject.w, rightObject.w) ||
                !NearlyEqual(leftObject.h, rightObject.h) ||
                !NearlyEqual(leftObject.angle, rightObject.angle) ||
                !NearlyEqual(leftObject.foreground_mean, rightObject.foreground_mean) ||
                !NearlyEqual(leftObject.background_mean, rightObject.background_mean)) {
                error = "目标字段不一致";
                return false;
            }
            if (leftObject.mask.width != rightObject.mask.width ||
                leftObject.mask.height != rightObject.mask.height ||
                (leftObject.mask.mask_ptr == 0) != (rightObject.mask.mask_ptr == 0)) {
                error = "mask 尺寸或有效状态不一致";
                return false;
            }
            if (leftObject.mask.mask_ptr != 0) {
                const size_t bytes = static_cast<size_t>(leftObject.mask.width) *
                    static_cast<size_t>(leftObject.mask.height);
                const auto* leftData = reinterpret_cast<const unsigned char*>(
                    static_cast<uintptr_t>(leftObject.mask.mask_ptr));
                const auto* rightData = reinterpret_cast<const unsigned char*>(
                    static_cast<uintptr_t>(rightObject.mask.mask_ptr));
                if (std::memcmp(leftData, rightData, bytes) != 0) {
                    error = "mask 像素内容不一致";
                    return false;
                }
            }
        }
    }
    return true;
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
    std::string successCompareError;
    const bool sameSuccessResult = CompareCResults(
        wrapperResult, nativeResult, successCompareError);
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
            std::cerr << "  差异: " << successCompareError << "\n";
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
    dlcv_infer::Result cppResult(std::vector<dlcv_infer::SampleResult>{});
    try {
        dlcv_infer::Model cppModel(modelPath, 0);
        cppResult = cppModel.InferBatch({image}, dlcv_infer::json::object());
    } catch (const std::exception& ex) {
        std::cerr << "FAIL: C++ 接口执行 dvst 失败: " << ex.what() << "\n";
        return false;
    }

    const std::string ansiPath = WideToAnsi(modelPath);
    const int modelIndex = dlcv_infer_cpp_load_model_c(ansiPath.c_str(), 0);
    if (modelIndex < 0) {
        std::cerr << "FAIL: 扩展 C 接口加载 dvst 失败\n";
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

    const char* params = "{}";
    DlcvCResult result = dlcv_infer_cpp_infer_with_params_c(modelIndex, &imageList, params);
    const bool inferOk = result.code == 0 && result.message != nullptr &&
        std::strcmp(result.message, "success") == 0 && result.n == 1 && result.sample_results != nullptr;
    std::string compareError;
    const bool sameResult = inferOk && CompareCppAndCResult(cppResult, result, compareError);
    dlcv_infer_cpp_free_model_result_c(&result);
    const bool resultFreeOk = IsReleasedResult(result, 0);
    const bool modelFreeOk = dlcv_infer_cpp_free_model_c(modelIndex) == 0 &&
        dlcv_infer_cpp_free_model_c(modelIndex) == -1;
    if (!sameResult || !resultFreeOk || !modelFreeOk) {
        std::cerr << "FAIL: dvst C 与 C++ 结果比较失败";
        if (!compareError.empty()) std::cerr << ": " << compareError;
        std::cerr << "\n";
        return false;
    }
    std::cout << "PASS: dvst C 与 C++ 结果逐字段及 mask 内容一致\n";
    return true;
}

static bool RunAllCompatibilityFlowChecks() {
    struct FlowCase {
        const wchar_t* modelPath;
        const wchar_t* imagePath;
    };
    const FlowCase cases[] = {
        {L"Y:\\测试模型\\AOI_120_50_s.dvst", L"Y:\\测试模型\\AOI-1.jpg"},
        {L"Y:\\测试模型\\AOI-无CAD检测-20260721_120_50_s.dvst", L"Y:\\测试模型\\OK1.png"},
        {L"Y:\\测试模型\\模型1-元件提取-20260721_120_50_s.dvst", L"Y:\\测试模型\\OK1.png"},
        {L"Y:\\测试模型\\模型2-元件检测-20260721_120_50_s.dvst", L"Y:\\测试模型\\OK1.png"},
        {L"Y:\\测试模型\\模型3-IC检测-20260721_120_50_s.dvst", L"Y:\\测试模型\\OK1.png"},
    };

    bool ok = true;
    for (const FlowCase& testCase : cases) {
        const cv::Mat image = ReadImageRgb(testCase.imagePath);
        if (image.empty()) {
            std::cerr << "FAIL: dvst C 与 C++ 比较图片读取失败\n";
            ok = false;
            continue;
        }
        ok = RunCompatibilityFlowCheck(testCase.modelPath, image) && ok;
    }
    return ok;
}

static bool LoadWrapperNativeInfer(NativeJsonApi& api, std::string& error) {
    api.module = LoadLibraryW(L"dlcv_infer_cpp.dll");
    if (api.module == nullptr) {
        error = "dlcv_infer_cpp.dll 加载失败: " + std::to_string(GetLastError());
        return false;
    }
    api.infer = reinterpret_cast<NativeJsonApi::StringCall>(
        GetProcAddress(api.module, "dlcv_infer"));
    if (api.infer == nullptr) {
        error = "dlcv_infer_cpp.dll 缺少 dlcv_infer 导出";
        return false;
    }
    return true;
}

static std::string BuildNativeInferConfig(
    int modelIndex,
    const cv::Mat& image,
    bool withMask = true) {
    dlcv_infer::json config = {
        { "model_index", modelIndex },
        { "image_list", dlcv_infer::json::array({
            {
                { "width", image.cols },
                { "height", image.rows },
                { "channels", image.channels() },
                { "image_ptr", static_cast<uint64_t>(reinterpret_cast<uintptr_t>(image.data)) },
                { "dtype", "uint8" }
            }
        }) },
        { "threshold", 0.05 },
        { "with_mask", withMask }
    };
    return config.dump();
}

static bool ParseSuccessfulModelIndex(const std::string& value, int& modelIndex) {
    try {
        const dlcv_infer::json result = dlcv_infer::json::parse(value);
        if (!result.is_object() || result.value("code", -1) != 0 ||
            !result.contains("model_index") || !result.at("model_index").is_number_integer()) {
            return false;
        }
        modelIndex = result.at("model_index").get<int>();
        return modelIndex >= 0;
    } catch (...) {
        return false;
    }
}

static bool RunNativeJsonDvtByteRegression(
    const std::wstring& modelPath,
    const cv::Mat& image) {
    NativeJsonApi native;
    NativeJsonApi wrapper;
    std::string error;
    if (!LoadNativeJsonApi(native, error)) {
        std::cerr << "FAIL: " << error << "\n";
        return false;
    }
    if (!LoadWrapperNativeInfer(wrapper, error)) {
        std::cerr << "FAIL: " << error << "\n";
        return false;
    }
    NativeJsonModelCleanup modelCleanup;

    dlcv_free_all_models();
    native.freeAllModels();

    const std::string missingLoadConfig = dlcv_infer::json{
        { "model_path", "Z:\\\\dlcv_missing_model.dvt" },
        { "device_id", 0 }
    }.dump();
    std::string nativeMissingLoad;
    std::string wrapperMissingLoad;
    if (!CopyJsonCallResult(native.loadModel, native.freeResult, missingLoadConfig,
            nativeMissingLoad, error) ||
        !CopyJsonCallResult(dlcv_load_model, dlcv_free_result, missingLoadConfig,
            wrapperMissingLoad, error) ||
        nativeMissingLoad != wrapperMissingLoad) {
        std::cerr << "FAIL: dvt 加载失败结果未保持逐字节转发"
                  << (error.empty() ? "" : ": " + error) << "\n";
        return false;
    }

    const std::string loadConfig = dlcv_infer::json{
        { "model_path", WideToUtf8(modelPath) },
        { "device_id", 0 }
    }.dump();
    std::string loadResult;
    if (!CopyJsonCallResult(dlcv_load_model, dlcv_free_result, loadConfig, loadResult, error)) {
        std::cerr << "FAIL: dvt 原生 JSON 加载失败: " << error << "\n";
        return false;
    }
    int modelIndex = -1;
    if (!ParseSuccessfulModelIndex(loadResult, modelIndex)) {
        std::cerr << "FAIL: dvt 原生 JSON 加载结果无有效 model_index: " << loadResult << "\n";
        return false;
    }

    const std::string indexConfig = dlcv_infer::json{ { "model_index", modelIndex } }.dump();
    std::string nativeInfo;
    std::string wrapperInfo;
    bool ok = CopyJsonCallResult(native.getModelInfo, native.freeResult, indexConfig,
                  nativeInfo, error) &&
        CopyJsonCallResult(dlcv_get_model_info, dlcv_free_result, indexConfig,
                  wrapperInfo, error) &&
        nativeInfo == wrapperInfo;

    // 掩码地址由每次调用单独分配，逐字节比较时关闭掩码返回。
    const std::string inferConfig = BuildNativeInferConfig(modelIndex, image, false);
    std::string nativeInfer;
    std::string wrapperInfer;
    ok = CopyJsonCallResult(native.infer, native.freeModelResult, inferConfig,
             nativeInfer, error) && ok;
    ok = CopyJsonCallResult(wrapper.infer, dlcv_free_model_result, inferConfig,
             wrapperInfer, error) && ok;
    ok = nativeInfer == wrapperInfer && ok;

    const std::string missingIndexConfig = dlcv_infer::json{ { "model_index", -987654 } }.dump();
    std::string nativeMissingFree;
    std::string wrapperMissingFree;
    ok = CopyJsonCallResult(native.freeModel, native.freeResult, missingIndexConfig,
             nativeMissingFree, error) && ok;
    ok = CopyJsonCallResult(dlcv_free_model, dlcv_free_result, missingIndexConfig,
             wrapperMissingFree, error) && ok;
    ok = nativeMissingFree == wrapperMissingFree && ok;

    std::string freeResult;
    const bool validFreeOk = CopyJsonCallResult(
        dlcv_free_model, dlcv_free_result, indexConfig, freeResult, error);
    try {
        ok = validFreeOk && dlcv_infer::json::parse(freeResult).value("code", -1) == 0 && ok;
    } catch (...) {
        ok = false;
    }
    dlcv_free_all_models();
    if (!ok) {
        std::cerr << "FAIL: dvt 原生 JSON 逐字节回归失败"
                  << (error.empty() ? "" : ": " + error) << "\n";
        return false;
    }

    std::cout << "PASS: dvt 原生 JSON 加载、信息、推理和释放保持底层逐字节结果\n";
    return true;
}

static bool IsSuccessfulNativeInfo(const std::string& value) {
    try {
        const dlcv_infer::json result = dlcv_infer::json::parse(value);
        return result.is_object() && result.contains("code") &&
            result.at("code").is_number_integer() && result.at("code").get<int>() == 0 &&
            result.contains("message") && result.at("message").is_string() &&
            result.contains("model_info") && result.at("model_info").is_object();
    } catch (...) {
        return false;
    }
}

static bool IsSuccessfulNativeInfer(const std::string& value, bool requireResults) {
    try {
        const dlcv_infer::json result = dlcv_infer::json::parse(value);
        if (!result.is_object() || result.value("code", -1) != 0 ||
            !result.contains("sample_results") || !result.at("sample_results").is_array() ||
            result.at("sample_results").empty()) {
            return false;
        }
        bool hasResults = false;
        for (const auto& sample : result.at("sample_results")) {
            if (!sample.is_object() || !sample.contains("results") ||
                !sample.at("results").is_array()) {
                return false;
            }
            for (const auto& object : sample.at("results")) {
                if (!object.is_object()) return false;
                hasResults = true;
                if (!object.value("with_mask", false)) continue;
                if (!object.contains("mask") || !object.at("mask").is_object()) return false;
                const auto& mask = object.at("mask");
                if (!mask.contains("mask_ptr") || !mask.at("mask_ptr").is_number() ||
                    mask.at("mask_ptr").get<uint64_t>() == 0 ||
                    mask.value("height", 0) <= 0 || mask.value("width", 0) <= 0) {
                    return false;
                }
            }
        }
        return !requireResults || hasResults;
    } catch (...) {
        return false;
    }
}

static bool RunNativeJsonDvstCheck(
    const std::wstring& modelPath,
    const cv::Mat& image) {
    dlcv_free_all_models();
    NativeJsonApi wrapper;
    std::string error;
    if (!LoadWrapperNativeInfer(wrapper, error)) {
        std::cerr << "FAIL: " << error << "\n";
        return false;
    }
    NativeJsonModelCleanup modelCleanup;
    const std::string loadConfig = dlcv_infer::json{
        { "model_path", WideToUtf8(modelPath) },
        { "device_id", 0 }
    }.dump();
    std::string loadResult;
    if (!CopyJsonCallResult(dlcv_load_model, dlcv_free_result, loadConfig, loadResult, error)) {
        std::cerr << "FAIL: dvst 原生 JSON 加载失败: " << error << "\n";
        return false;
    }

    int modelIndex = -1;
    if (!ParseSuccessfulModelIndex(loadResult, modelIndex) || modelIndex < 10000) {
        std::cerr << "FAIL: dvst 原生 JSON 未返回流程模型索引: " << loadResult << "\n";
        return false;
    }

    const std::string infoConfig = dlcv_infer::json{ { "model_index", modelIndex } }.dump();
    const std::string pathInfoConfig = dlcv_infer::json{
        { "model_path", WideToUtf8(modelPath) }
    }.dump();
    const std::string inferConfig = BuildNativeInferConfig(modelIndex, image);
    std::string infoResult;
    std::string pathInfoResult;
    std::string inferResult;
    bool ok = CopyJsonCallResult(
                  dlcv_get_model_info, dlcv_free_result, infoConfig, infoResult, error) &&
        IsSuccessfulNativeInfo(infoResult);
    ok = CopyJsonCallResult(
             dlcv_get_model_info, dlcv_free_result, pathInfoConfig, pathInfoResult, error) &&
        IsSuccessfulNativeInfo(pathInfoResult) && ok;
    ok = CopyJsonCallResult(
             wrapper.infer, dlcv_free_model_result, inferConfig, inferResult, error) && ok;
    ok = IsSuccessfulNativeInfer(inferResult, true) && ok;

    std::atomic<int> failures{0};
    std::vector<std::thread> workers;
    for (int threadIndex = 0; threadIndex < 4; ++threadIndex) {
        workers.emplace_back([&, threadIndex]() {
            for (int iteration = 0; iteration < 5; ++iteration) {
                std::string value;
                std::string threadError;
                const bool readInfo = ((threadIndex + iteration) % 2) == 0;
                const bool callOk = readInfo
                    ? CopyJsonCallResult(
                        dlcv_get_model_info, dlcv_free_result, infoConfig, value, threadError)
                    : CopyJsonCallResult(
                        wrapper.infer, dlcv_free_model_result, inferConfig, value, threadError);
                const bool valueOk = readInfo
                    ? IsSuccessfulNativeInfo(value)
                    : IsSuccessfulNativeInfer(value, true);
                if (!callOk || !valueOk) failures.fetch_add(1, std::memory_order_relaxed);
            }
        });
    }
    for (auto& worker : workers) worker.join();
    ok = failures.load(std::memory_order_relaxed) == 0 && ok;

    std::string freeResult;
    const bool freeOk = CopyJsonCallResult(
        dlcv_free_model, dlcv_free_result, infoConfig, freeResult, error);
    try {
        ok = freeOk && dlcv_infer::json::parse(freeResult).value("code", -1) == 0 && ok;
    } catch (...) {
        ok = false;
    }
    dlcv_free_all_models();
    if (!ok) {
        std::cerr << "FAIL: dvst 原生 JSON 加载、信息、推理、并发或释放验证失败"
                  << (error.empty() ? "" : ": " + error) << "\n";
        return false;
    }

    std::cout << "PASS: dvst 原生 JSON 完整流程及同索引信息/推理并发验证成功\n";
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

static bool LoadNativeJsonApi(NativeJsonApi& api, std::string& error) {
    api.module = LoadLibraryW(L"C:\\dlcv\\Lib\\site-packages\\dlcvpro_infer\\dlcv_infer.dll");
    if (api.module == nullptr) {
        error = "dlcv_infer.dll 加载失败: " + std::to_string(GetLastError());
        return false;
    }
    api.loadModel = reinterpret_cast<NativeJsonApi::StringCall>(
        GetProcAddress(api.module, "dlcv_load_model"));
    api.freeModel = reinterpret_cast<NativeJsonApi::StringCall>(
        GetProcAddress(api.module, "dlcv_free_model"));
    api.getModelInfo = reinterpret_cast<NativeJsonApi::StringCall>(
        GetProcAddress(api.module, "dlcv_get_model_info"));
    api.infer = reinterpret_cast<NativeJsonApi::StringCall>(
        GetProcAddress(api.module, "dlcv_infer"));
    api.freeModelResult = reinterpret_cast<NativeJsonApi::FreeString>(
        GetProcAddress(api.module, "dlcv_free_model_result"));
    api.freeResult = reinterpret_cast<NativeJsonApi::FreeString>(
        GetProcAddress(api.module, "dlcv_free_result"));
    api.freeAllModels = reinterpret_cast<NativeJsonApi::FreeAll>(
        GetProcAddress(api.module, "dlcv_free_all_models"));
    if (api.loadModel == nullptr || api.freeModel == nullptr || api.getModelInfo == nullptr ||
        api.infer == nullptr || api.freeModelResult == nullptr || api.freeResult == nullptr ||
        api.freeAllModels == nullptr) {
        error = "dlcv_infer.dll 原生 JSON API 不完整";
        return false;
    }
    return true;
}

static bool CopyJsonCallResult(
    NativeJsonApi::StringCall call,
    NativeJsonApi::FreeString release,
    const std::string& config,
    std::string& result,
    std::string& error) {
    const char* value = call(config.c_str());
    if (value == nullptr) {
        error = "原生 JSON 接口返回空指针";
        return false;
    }
    result.assign(value);
    release(value);
    return true;
}

static bool ReadModelInfo(
    int modelIndex,
    std::string& modelInfo,
    std::string& error) {
    const char* value = dlcv_infer_cpp_get_model_info_c(modelIndex);
    if (value == nullptr) {
        const char* lastError = dlcv_infer_cpp_get_last_error_c();
        error = lastError == nullptr ? "获取模型信息失败" : lastError;
        return false;
    }
    modelInfo = value;
    dlcv_infer_cpp_free_string_c(value);
    if (modelInfo.empty()) {
        error = "模型信息为空";
        return false;
    }
    return true;
}

static bool RunConcurrentModelInfoAndInference(
    const std::string& label,
    int modelIndex,
    const cv::Mat& image,
    int threadCount,
    int iterationsPerThread) {
    std::atomic<int> ready{0};
    std::atomic<bool> start{false};
    std::atomic<int> failed{0};
    std::mutex resultMutex;
    std::string expectedModelInfo;
    std::string expectedFingerprint;
    std::string firstError;
    std::vector<std::thread> workers;
    workers.reserve(static_cast<size_t>(threadCount));

    for (int threadIndex = 0; threadIndex < threadCount; ++threadIndex) {
        workers.emplace_back([&, threadIndex]() {
            ready.fetch_add(1);
            while (!start.load()) std::this_thread::yield();
            for (int iteration = 0; iteration < iterationsPerThread; ++iteration) {
                std::string modelInfo;
                std::string fingerprint;
                std::string modelInfoError;
                std::string inferError;
                bool modelInfoOk = false;
                bool inferOk = false;
                if ((threadIndex & 1) == 0) {
                    modelInfoOk = ReadModelInfo(modelIndex, modelInfo, modelInfoError);
                    inferOk = InferFingerprint(modelIndex, image, fingerprint, inferError);
                } else {
                    inferOk = InferFingerprint(modelIndex, image, fingerprint, inferError);
                    modelInfoOk = ReadModelInfo(modelIndex, modelInfo, modelInfoError);
                }

                bool iterationOk = modelInfoOk && inferOk;
                std::string iterationError;
                {
                    std::lock_guard<std::mutex> lock(resultMutex);
                    if (modelInfoOk) {
                        if (expectedModelInfo.empty()) {
                            expectedModelInfo = modelInfo;
                        } else if (modelInfo != expectedModelInfo) {
                            iterationOk = false;
                            iterationError = "模型信息不一致";
                        }
                    }
                    if (inferOk) {
                        if (expectedFingerprint.empty()) {
                            expectedFingerprint = fingerprint;
                        } else if (fingerprint != expectedFingerprint) {
                            iterationOk = false;
                            if (iterationError.empty()) iterationError = "推理结果特征不一致";
                        }
                    }
                    if (!modelInfoOk && iterationError.empty()) iterationError = modelInfoError;
                    if (!inferOk && iterationError.empty()) iterationError = inferError;
                    if (!iterationOk && firstError.empty()) firstError = iterationError;
                }
                if (!iterationOk) {
                    failed.fetch_add(1);
                }
            }
        });
    }

    while (ready.load() != threadCount) std::this_thread::yield();
    start.store(true);
    for (auto& worker : workers) worker.join();

    if (failed.load() != 0 || expectedModelInfo.empty() || expectedFingerprint.empty()) {
        std::cerr << "FAIL: " << label << "，失败次数=" << failed.load()
                  << "，首个错误=" << firstError << "\n";
        return false;
    }

    std::string modelInfo;
    std::string fingerprint;
    std::string error;
    const bool modelInfoOk = ReadModelInfo(modelIndex, modelInfo, error);
    if (!modelInfoOk || modelInfo != expectedModelInfo) {
        std::cerr << "FAIL: " << label << "，并发后模型信息不一致: "
                  << (modelInfoOk ? "模型信息不一致" : error) << "\n";
        return false;
    }
    error.clear();
    const bool inferOk = InferFingerprint(modelIndex, image, fingerprint, error);
    if (!inferOk || fingerprint != expectedFingerprint) {
        std::cerr << "FAIL: " << label << "，获取模型信息后推理失败: "
                  << (inferOk ? "推理结果特征不一致" : error) << "\n";
        return false;
    }

    std::cout << "PASS: " << label << "，线程数=" << threadCount
              << "，每线程次数=" << iterationsPerThread << "\n";
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

static bool RunConcurrentModelLoadingCheck(
    const std::wstring& dvtPath,
    const std::wstring& dvstPath,
    int threadCount) {
    std::atomic<int> ready{0};
    std::atomic<bool> start{false};
    std::atomic<int> failed{0};
    std::mutex errorMutex;
    std::string firstError;
    std::vector<std::unique_ptr<dlcv_infer::Model>> models(static_cast<size_t>(threadCount));
    std::vector<std::thread> workers;
    workers.reserve(static_cast<size_t>(threadCount));

    for (int threadIndex = 0; threadIndex < threadCount; ++threadIndex) {
        workers.emplace_back([&, threadIndex]() {
            ready.fetch_add(1);
            while (!start.load()) std::this_thread::yield();
            try {
                const std::wstring& modelPath = (threadIndex & 1) == 0 ? dvtPath : dvstPath;
                auto model = std::make_unique<dlcv_infer::Model>(modelPath, 0);
                const dlcv_infer::json modelInfo = model->GetModelInfo();
                if (modelInfo.is_null() || modelInfo.empty()) {
                    throw std::runtime_error("模型信息为空");
                }
                models[static_cast<size_t>(threadIndex)] = std::move(model);
            } catch (const std::exception& ex) {
                failed.fetch_add(1);
                std::lock_guard<std::mutex> lock(errorMutex);
                if (firstError.empty()) firstError = ex.what();
            }
        });
    }

    while (ready.load() != threadCount) std::this_thread::yield();
    start.store(true);
    for (auto& worker : workers) worker.join();

    if (failed.load() != 0) {
        std::cerr << "FAIL: 普通模型与流程模型并发加载失败，失败次数=" << failed.load()
                  << "，首个错误=" << firstError << "\n";
        return false;
    }

    std::string loadedDllName;
    for (const auto& model : models) {
        if (!model) {
            std::cerr << "FAIL: 并发加载完成后存在空模型实例\n";
            return false;
        }
        const std::string currentDllName = model->LoadedNativeDllName();
        if (currentDllName.empty()) {
            std::cerr << "FAIL: 模型未记录底层 DLL 名称\n";
            return false;
        }
        if (loadedDllName.empty()) {
            loadedDllName = currentDllName;
        } else if (currentDllName != loadedDllName) {
            std::cerr << "FAIL: 同一进程内模型使用了不同底层 DLL："
                      << loadedDllName << " / " << currentDllName << "\n";
            return false;
        }
    }

    models.clear();
    std::cout << "PASS: 普通模型与流程模型并发加载成功，线程数=" << threadCount
              << "，底层 DLL=" << loadedDllName << "\n";
    return true;
}

struct ProcessMemorySnapshot {
    unsigned long long privateBytes = 0;
    unsigned long long workingSetBytes = 0;
};

static ProcessMemorySnapshot ReadProcessMemory() {
    PROCESS_MEMORY_COUNTERS_EX counters{};
    counters.cb = sizeof(counters);
    if (!GetProcessMemoryInfo(
            GetCurrentProcess(),
            reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters),
            sizeof(counters))) {
        return {};
    }
    ProcessMemorySnapshot snapshot;
    snapshot.privateBytes = static_cast<unsigned long long>(counters.PrivateUsage);
    snapshot.workingSetBytes = static_cast<unsigned long long>(counters.WorkingSetSize);
    return snapshot;
}

static double BytesToMiB(unsigned long long bytes) {
    return static_cast<double>(bytes) / (1024.0 * 1024.0);
}

static double MemoryDeltaMiB(unsigned long long current, unsigned long long baseline) {
    const long long delta = static_cast<long long>(current) - static_cast<long long>(baseline);
    return static_cast<double>(delta) / (1024.0 * 1024.0);
}

static void PrintMemorySnapshot(
    const std::string& label,
    const ProcessMemorySnapshot& current,
    const ProcessMemorySnapshot& baseline) {
    std::cout << label
              << ": private=" << BytesToMiB(current.privateBytes) << " MiB"
              << ", private_delta=" << MemoryDeltaMiB(current.privateBytes, baseline.privateBytes) << " MiB"
              << ", working_set=" << BytesToMiB(current.workingSetBytes) << " MiB"
              << ", working_set_delta=" << MemoryDeltaMiB(current.workingSetBytes, baseline.workingSetBytes) << " MiB\n";
}

static bool RunModelMemoryGrowthCheck(
    const std::string& label,
    const std::wstring& modelPath,
    const cv::Mat& image,
    int warmupRounds,
    int measuredRounds,
    double maxSameModelPrivateDeltaMiB,
    double maxRecreatePrivateDeltaMiB) {
    std::string baselineFingerprint;
    std::string error;

    int stableIndex = LoadModel(modelPath);
    if (stableIndex < 0) return false;
    if (!InferFingerprint(stableIndex, image, baselineFingerprint, error)) {
        std::cerr << "FAIL: " << label << " 内存测试基准推理失败: " << error << "\n";
        dlcv_infer_cpp_free_model_c(stableIndex);
        return false;
    }

    const ProcessMemorySnapshot sameModelBaseline = ReadProcessMemory();
    ProcessMemorySnapshot sameModelFinal = sameModelBaseline;
    PrintMemorySnapshot(label + " 同一实例起点", sameModelBaseline, sameModelBaseline);
    for (int round = 1; round <= measuredRounds; ++round) {
        std::string fingerprint;
        error.clear();
        if (!InferFingerprint(stableIndex, image, fingerprint, error) || fingerprint != baselineFingerprint) {
            std::cerr << "FAIL: " << label << " 同一实例第 " << round << " 轮失败: "
                      << (error.empty() ? "结果摘要不一致" : error) << "\n";
            dlcv_infer_cpp_free_model_c(stableIndex);
            return false;
        }
        sameModelFinal = ReadProcessMemory();
        PrintMemorySnapshot(
            label + " 同一实例第 " + std::to_string(round) + " 轮",
            sameModelFinal,
            sameModelBaseline);
    }
    if (dlcv_infer_cpp_free_model_c(stableIndex) != 0) {
        std::cerr << "FAIL: " << label << " 同一实例释放失败\n";
        return false;
    }
    const double sameModelPrivateDeltaMiB = MemoryDeltaMiB(
        sameModelFinal.privateBytes,
        sameModelBaseline.privateBytes);
    if (sameModelPrivateDeltaMiB > maxSameModelPrivateDeltaMiB) {
        std::cerr << "FAIL: " << label << " 同一实例私有内存增量 "
                  << sameModelPrivateDeltaMiB << " MiB，超过上限 "
                  << maxSameModelPrivateDeltaMiB << " MiB\n";
        return false;
    }

    for (int round = 1; round <= warmupRounds; ++round) {
        const int modelIndex = LoadModel(modelPath);
        if (modelIndex < 0) return false;
        std::string fingerprint;
        error.clear();
        const bool inferOk = InferFingerprint(modelIndex, image, fingerprint, error)
            && fingerprint == baselineFingerprint;
        const bool freeOk = dlcv_infer_cpp_free_model_c(modelIndex) == 0;
        if (!inferOk || !freeOk) {
            std::cerr << "FAIL: " << label << " 新建实例预热第 " << round << " 轮失败: "
                      << (error.empty() ? "结果摘要不一致或释放失败" : error) << "\n";
            return false;
        }
    }

    const ProcessMemorySnapshot recreateBaseline = ReadProcessMemory();
    ProcessMemorySnapshot recreateFinal = recreateBaseline;
    PrintMemorySnapshot(label + " 新建实例测量起点", recreateBaseline, recreateBaseline);
    for (int round = 1; round <= measuredRounds; ++round) {
        const int modelIndex = LoadModel(modelPath);
        if (modelIndex < 0) return false;
        std::string fingerprint;
        error.clear();
        const bool inferOk = InferFingerprint(modelIndex, image, fingerprint, error)
            && fingerprint == baselineFingerprint;
        const bool freeOk = dlcv_infer_cpp_free_model_c(modelIndex) == 0;
        if (!inferOk || !freeOk) {
            std::cerr << "FAIL: " << label << " 新建实例第 " << round << " 轮失败: "
                      << (error.empty() ? "结果摘要不一致或释放失败" : error) << "\n";
            return false;
        }
        recreateFinal = ReadProcessMemory();
        PrintMemorySnapshot(
            label + " 新建实例第 " + std::to_string(round) + " 轮",
            recreateFinal,
            recreateBaseline);
    }
    const double recreatePrivateDeltaMiB = MemoryDeltaMiB(
        recreateFinal.privateBytes,
        recreateBaseline.privateBytes);
    if (recreatePrivateDeltaMiB > maxRecreatePrivateDeltaMiB) {
        std::cerr << "FAIL: " << label << " 新建实例私有内存增量 "
                  << recreatePrivateDeltaMiB << " MiB，超过上限 "
                  << maxRecreatePrivateDeltaMiB << " MiB\n";
        return false;
    }
    std::cout << "PASS: " << label << " 内存增量未超过上限\n";
    return true;
}

static bool RunDvstIdleCacheClearCheck(const std::wstring& modelPath, const cv::Mat& image) {
    std::string baselineFingerprint;
    std::string error;

    const int firstIndex = LoadModel(modelPath);
    if (firstIndex < 0) return false;
    const bool firstInferOk = InferFingerprint(firstIndex, image, baselineFingerprint, error);
    const bool firstFreeOk = dlcv_infer_cpp_free_model_c(firstIndex) == 0;
    if (!firstInferOk || !firstFreeOk) {
        std::cerr << "FAIL: dvst 空闲缓存清理前推理或释放失败: "
                  << (error.empty() ? "结果摘要不一致或释放失败" : error) << "\n";
        dlcv_infer_cpp_free_all_models_c();
        return false;
    }

    const dlcv_infer::flow::ModelPoolStats beforeClear = dlcv_infer::flow::GetModelPoolStats();
    if (beforeClear.totalEntries == 0 || beforeClear.activeEntries != 0 ||
        beforeClear.idleEntries != beforeClear.totalEntries) {
        std::cerr << "FAIL: dvst 清理前池统计不符合预期"
                  << " total=" << beforeClear.totalEntries
                  << " active=" << beforeClear.activeEntries
                  << " idle=" << beforeClear.idleEntries << "\n";
        dlcv_infer_cpp_free_all_models_c();
        return false;
    }

    dlcv_infer_cpp_free_all_models_c();

    const dlcv_infer::flow::ModelPoolStats afterClear = dlcv_infer::flow::GetModelPoolStats();
    if (afterClear.totalEntries != 0 || afterClear.activeEntries != 0 || afterClear.idleEntries != 0) {
        std::cerr << "FAIL: dvst 清理后池中仍有模型"
                  << " total=" << afterClear.totalEntries
                  << " active=" << afterClear.activeEntries
                  << " idle=" << afterClear.idleEntries << "\n";
        return false;
    }

    const int secondIndex = LoadModel(modelPath);
    if (secondIndex < 0) return false;
    std::string secondFingerprint;
    error.clear();
    const bool secondInferOk = InferFingerprint(secondIndex, image, secondFingerprint, error)
        && secondFingerprint == baselineFingerprint;
    const bool secondFreeOk = dlcv_infer_cpp_free_model_c(secondIndex) == 0;
    const dlcv_infer::flow::ModelPoolStats afterReload = dlcv_infer::flow::GetModelPoolStats();
    dlcv_infer_cpp_free_all_models_c();
    if (!secondInferOk || !secondFreeOk || afterReload.totalEntries == 0 ||
        afterReload.activeEntries != 0 || afterReload.idleEntries != afterReload.totalEntries) {
        std::cerr << "FAIL: dvst 空闲缓存清理后重新加载失败: "
                  << (error.empty() ? "结果摘要不一致或释放失败" : error) << "\n";
        return false;
    }

    std::cout << "PASS: dvst 空闲缓存清理后重新加载成功\n";
    return true;
}

static std::vector<std::wstring> GetCurrentProcessDvsTempDirs() {
    wchar_t tempDir[MAX_PATH]{};
    const DWORD length = GetTempPathW(MAX_PATH, tempDir);
    if (length == 0 || length >= MAX_PATH) return {};

    const std::wstring pattern = std::wstring(tempDir)
        + L"DlcvDvs_*_"
        + std::to_wstring(GetCurrentProcessId());
    WIN32_FIND_DATAW findData{};
    HANDLE findHandle = FindFirstFileW(pattern.c_str(), &findData);
    if (findHandle == INVALID_HANDLE_VALUE) return {};

    std::vector<std::wstring> dirs;
    do {
        if ((findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
            dirs.push_back(std::wstring(tempDir) + findData.cFileName);
        }
    } while (FindNextFileW(findHandle, &findData));
    FindClose(findHandle);
    std::sort(dirs.begin(), dirs.end());
    return dirs;
}

static std::vector<std::wstring> GetNewDvsTempDirs(
    const std::vector<std::wstring>& before,
    const std::vector<std::wstring>& after) {
    std::vector<std::wstring> added;
    std::set_difference(
        after.begin(), after.end(),
        before.begin(), before.end(),
        std::back_inserter(added));
    return added;
}

static bool RunDvstTempDirectoryReuseCheck(const std::wstring& modelPath, const cv::Mat& image) {
    dlcv_infer_cpp_free_all_models_c();
    wchar_t tempDir[MAX_PATH]{};
    const DWORD tempDirLength = GetTempPathW(MAX_PATH, tempDir);
    if (tempDirLength == 0 || tempDirLength >= MAX_PATH) {
        std::cerr << "FAIL: 无法获取 dvst 内容复用测试临时目录\n";
        return false;
    }

    const std::wstring copiedPath = std::wstring(tempDir)
        + L"dlcv_dvst_same_content_"
        + std::to_wstring(GetCurrentProcessId())
        + L"_"
        + std::to_wstring(GetTickCount64())
        + L".dvst";
    if (!CopyFileW(modelPath.c_str(), copiedPath.c_str(), FALSE)) {
        std::cerr << "FAIL: 无法复制 dvst 内容复用测试文件，错误码=" << GetLastError() << "\n";
        return false;
    }

    const auto cleanup = [&copiedPath]() {
        dlcv_infer_cpp_free_all_models_c();
        DeleteFileW(copiedPath.c_str());
    };
    const std::vector<std::wstring> before = GetCurrentProcessDvsTempDirs();

    const int firstIndex = LoadModel(modelPath);
    if (firstIndex < 0) {
        cleanup();
        return false;
    }
    const std::vector<std::wstring> afterFirst = GetCurrentProcessDvsTempDirs();
    const std::vector<std::wstring> firstAdded = GetNewDvsTempDirs(before, afterFirst);
    const dlcv_infer::flow::ModelPoolStats firstStats = dlcv_infer::flow::GetModelPoolStats();
    if (firstAdded.size() != 1) {
        std::cerr << "FAIL: 首次加载 dvst 未生成唯一稳定目录\n";
        cleanup();
        return false;
    }

    const int secondIndex = LoadModel(copiedPath);
    if (secondIndex < 0) {
        cleanup();
        return false;
    }
    const std::vector<std::wstring> afterSecond = GetCurrentProcessDvsTempDirs();
    const dlcv_infer::flow::ModelPoolStats secondStats = dlcv_infer::flow::GetModelPoolStats();
    if (afterSecond != afterFirst || firstStats.totalEntries == 0 ||
        secondStats.totalEntries != firstStats.totalEntries) {
        std::cerr << "FAIL: 不同路径的相同 dvst 内容未复用解压目录或模型池\n";
        cleanup();
        return false;
    }

    std::string firstFingerprint;
    std::string error;
    if (!InferFingerprint(firstIndex, image, firstFingerprint, error)) {
        std::cerr << "FAIL: 首个 dvst 内容复用实例推理失败: " << error << "\n";
        cleanup();
        return false;
    }

    if (dlcv_infer_cpp_free_model_c(firstIndex) != 0 ||
        GetFileAttributesW(firstAdded.front().c_str()) == INVALID_FILE_ATTRIBUTES) {
        std::cerr << "FAIL: 首个实例释放后共享解压目录不可用\n";
        cleanup();
        return false;
    }

    std::string secondFingerprint;
    error.clear();
    const bool secondInferOk = InferFingerprint(secondIndex, image, secondFingerprint, error)
        && secondFingerprint == firstFingerprint;
    const bool secondFreeOk = dlcv_infer_cpp_free_model_c(secondIndex) == 0;
    const bool removedAfterLastRelease =
        GetFileAttributesW(firstAdded.front().c_str()) == INVALID_FILE_ATTRIBUTES;
    cleanup();
    if (!secondInferOk || !secondFreeOk || !removedAfterLastRelease) {
        std::cerr << "FAIL: 共享解压目录引用计数验证失败: "
                  << (error.empty() ? "目录清理状态异常" : error) << "\n";
        return false;
    }

    std::cout << "PASS: 不同路径的相同 dvst 内容复用解压目录且最后释放时清理\n";
    return true;
}

static bool RunDvstContentUpdateCheck(const std::wstring& sourcePath, const cv::Mat& image) {
    dlcv_infer_cpp_free_all_models_c();
    wchar_t tempDir[MAX_PATH]{};
    const DWORD tempDirLength = GetTempPathW(MAX_PATH, tempDir);
    if (tempDirLength == 0 || tempDirLength >= MAX_PATH) {
        std::cerr << "FAIL: 无法获取 dvst 内容更新测试临时目录\n";
        return false;
    }

    const std::wstring tempPath = std::wstring(tempDir)
        + L"dlcv_dvst_content_"
        + std::to_wstring(GetCurrentProcessId())
        + L"_"
        + std::to_wstring(GetTickCount64())
        + L".dvst";
    if (!CopyFileW(sourcePath.c_str(), tempPath.c_str(), FALSE)) {
        std::cerr << "FAIL: 无法复制 dvst 内容更新测试文件，错误码=" << GetLastError() << "\n";
        return false;
    }

    const auto cleanup = [&tempPath]() {
        dlcv_infer_cpp_free_all_models_c();
        DeleteFileW(tempPath.c_str());
    };

    FILE* markerFile = nullptr;
    if (_wfopen_s(&markerFile, tempPath.c_str(), L"ab") != 0 || markerFile == nullptr) {
        std::cerr << "FAIL: 无法打开 dvst 内容更新测试文件\n";
        cleanup();
        return false;
    }
    static constexpr unsigned char kFirstMarker[] = { 0x44, 0x4c, 0x43, 0x56 };
    const size_t firstMarkerWritten = std::fwrite(kFirstMarker, 1, sizeof(kFirstMarker), markerFile);
    std::fclose(markerFile);
    if (firstMarkerWritten != sizeof(kFirstMarker)) {
        std::cerr << "FAIL: 无法写入 dvst 初始内容标记\n";
        cleanup();
        return false;
    }

    WIN32_FILE_ATTRIBUTE_DATA stableAttributes{};
    if (!GetFileAttributesExW(tempPath.c_str(), GetFileExInfoStandard, &stableAttributes)) {
        std::cerr << "FAIL: 无法读取 dvst 内容更新测试文件属性\n";
        cleanup();
        return false;
    }

    std::string firstFingerprint;
    std::string error;
    const std::vector<std::wstring> dirsBeforeFirstLoad = GetCurrentProcessDvsTempDirs();
    const int firstIndex = LoadModel(tempPath);
    if (firstIndex < 0) {
        cleanup();
        return false;
    }
    const std::vector<std::wstring> firstAddedDirs = GetNewDvsTempDirs(
        dirsBeforeFirstLoad,
        GetCurrentProcessDvsTempDirs());
    const bool firstOk = InferFingerprint(firstIndex, image, firstFingerprint, error);
    const dlcv_infer::flow::ModelPoolStats firstStats = dlcv_infer::flow::GetModelPoolStats();
    if (!firstOk || firstAddedDirs.size() != 1 || firstStats.activeEntries == 0) {
        std::cerr << "FAIL: dvst 内容更新前加载状态异常\n";
        cleanup();
        return false;
    }

    FILE* updateFile = nullptr;
    if (_wfopen_s(&updateFile, tempPath.c_str(), L"r+b") != 0 || updateFile == nullptr) {
        std::cerr << "FAIL: 无法更新 dvst 内容测试文件\n";
        cleanup();
        return false;
    }
    const int seekResult = _fseeki64(updateFile, -static_cast<__int64>(sizeof(kFirstMarker)), SEEK_END);
    static constexpr unsigned char kSecondMarker[] = { 0x44, 0x4c, 0x43, 0x57 };
    const size_t secondMarkerWritten = seekResult == 0
        ? std::fwrite(kSecondMarker, 1, sizeof(kSecondMarker), updateFile)
        : 0;
    std::fclose(updateFile);
    if (secondMarkerWritten != sizeof(kSecondMarker)) {
        std::cerr << "FAIL: 无法写入 dvst 内容更新标记\n";
        cleanup();
        return false;
    }

    const HANDLE attributeHandle = CreateFileW(
        tempPath.c_str(),
        FILE_WRITE_ATTRIBUTES,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (attributeHandle == INVALID_HANDLE_VALUE ||
        !SetFileTime(attributeHandle, nullptr, nullptr, &stableAttributes.ftLastWriteTime)) {
        if (attributeHandle != INVALID_HANDLE_VALUE) CloseHandle(attributeHandle);
        std::cerr << "FAIL: 无法恢复 dvst 内容更新测试文件时间\n";
        cleanup();
        return false;
    }
    CloseHandle(attributeHandle);

    WIN32_FILE_ATTRIBUTE_DATA updatedAttributes{};
    const bool attributesStable = GetFileAttributesExW(
        tempPath.c_str(),
        GetFileExInfoStandard,
        &updatedAttributes)
        && updatedAttributes.nFileSizeHigh == stableAttributes.nFileSizeHigh
        && updatedAttributes.nFileSizeLow == stableAttributes.nFileSizeLow
        && updatedAttributes.ftLastWriteTime.dwHighDateTime == stableAttributes.ftLastWriteTime.dwHighDateTime
        && updatedAttributes.ftLastWriteTime.dwLowDateTime == stableAttributes.ftLastWriteTime.dwLowDateTime;
    if (!attributesStable) {
        std::cerr << "FAIL: dvst 内容更新测试未保持文件大小和修改时间\n";
        cleanup();
        return false;
    }

    const int secondIndex = LoadModel(tempPath);
    if (secondIndex < 0) {
        cleanup();
        return false;
    }
    std::string secondFingerprint;
    error.clear();
    const std::vector<std::wstring> secondAddedDirs = GetNewDvsTempDirs(
        dirsBeforeFirstLoad,
        GetCurrentProcessDvsTempDirs());
    const bool secondOk = InferFingerprint(secondIndex, image, secondFingerprint, error)
        && secondFingerprint == firstFingerprint;
    const dlcv_infer::flow::ModelPoolStats secondStats = dlcv_infer::flow::GetModelPoolStats();
    const bool identityChanged = secondStats.activeEntries > firstStats.activeEntries
        && secondStats.totalEntries > firstStats.totalEntries
        && secondAddedDirs.size() == 2
        && secondAddedDirs[0] != secondAddedDirs[1];
    const bool releasesOk = dlcv_infer_cpp_free_model_c(firstIndex) == 0
        && dlcv_infer_cpp_free_model_c(secondIndex) == 0;
    cleanup();
    if (!secondOk || !identityChanged || !releasesOk) {
        std::cerr << "FAIL: 同路径 dvst 内容更新后未创建新的池项"
                  << " before=" << firstStats.idleEntries
                  << " after=" << secondStats.idleEntries << "\n";
        return false;
    }

    std::cout << "PASS: 同路径 dvst 内容更新后使用新的模型标识\n";
    return true;
}

static bool RunModelPoolGenerationCheck(const std::wstring& modelPath, const cv::Mat& image) {
    dlcv_infer_cpp_free_all_models_c();
    std::string firstFingerprint;
    std::string error;
    const int oldIndex = LoadModel(modelPath);
    if (oldIndex < 0 || !InferFingerprint(oldIndex, image, firstFingerprint, error)) {
        dlcv_infer_cpp_free_all_models_c();
        return false;
    }

    dlcv_infer::NativeApi::FreeAllModels();
    const dlcv_infer::flow::ModelPoolStats afterNativeClear = dlcv_infer::flow::GetModelPoolStats();
    if (afterNativeClear.totalEntries != 0) {
        std::cerr << "FAIL: NativeApi::FreeAllModels 未清空流程模型池\n";
        dlcv_infer_cpp_free_all_models_c();
        return false;
    }

    const int newIndex = LoadModel(modelPath);
    if (newIndex < 0) {
        dlcv_infer_cpp_free_all_models_c();
        return false;
    }
    const dlcv_infer::flow::ModelPoolStats beforeOldRelease = dlcv_infer::flow::GetModelPoolStats();
    const bool oldReleaseOk = dlcv_infer_cpp_free_model_c(oldIndex) == 0;
    const dlcv_infer::flow::ModelPoolStats afterOldRelease = dlcv_infer::flow::GetModelPoolStats();
    const bool generationOk = oldReleaseOk
        && beforeOldRelease.totalEntries > 0
        && beforeOldRelease.activeEntries == beforeOldRelease.totalEntries
        && afterOldRelease.totalEntries == beforeOldRelease.totalEntries
        && afterOldRelease.activeEntries == beforeOldRelease.activeEntries
        && afterOldRelease.idleEntries == beforeOldRelease.idleEntries;

    std::string secondFingerprint;
    error.clear();
    const bool inferOk = InferFingerprint(newIndex, image, secondFingerprint, error)
        && secondFingerprint == firstFingerprint;
    const bool newReleaseOk = dlcv_infer_cpp_free_model_c(newIndex) == 0;
    dlcv_infer_cpp_free_all_models_c();
    if (!generationOk || !inferOk || !newReleaseOk) {
        std::cerr << "FAIL: Clear 前的租用释放影响了 Clear 后的同键模型\n";
        return false;
    }

    std::cout << "PASS: Clear 前后的同键模型身份互不影响\n";
    return true;
}

static bool RunFreeAllDuringInferenceCheck(const std::wstring& modelPath, const cv::Mat& image) {
    dlcv_infer_cpp_free_all_models_c();
    const int modelIndex = LoadModel(modelPath);
    if (modelIndex < 0) return false;

    std::mutex stateMutex;
    std::condition_variable stateChanged;
    bool readGuardHeld = false;
    bool releaseReadGuard = false;
    bool freeAllStarted = false;
    bool freeAllCompleted = false;
    bool inferOk = false;
    std::string inferError;
    std::thread worker([&]() {
        dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
        std::string fingerprint;
        inferOk = InferFingerprint(modelIndex, image, fingerprint, inferError);
        std::unique_lock<std::mutex> stateLock(stateMutex);
        readGuardHeld = true;
        stateChanged.notify_all();
        stateChanged.wait(stateLock, [&]() { return releaseReadGuard; });
    });

    {
        std::unique_lock<std::mutex> stateLock(stateMutex);
        stateChanged.wait(stateLock, [&]() { return readGuardHeld; });
    }

    std::thread freeAllWorker([&]() {
        {
            std::lock_guard<std::mutex> stateLock(stateMutex);
            freeAllStarted = true;
            stateChanged.notify_all();
        }
        dlcv_infer_cpp_free_all_models_c();
        {
            std::lock_guard<std::mutex> stateLock(stateMutex);
            freeAllCompleted = true;
            stateChanged.notify_all();
        }
    });

    bool completedWhileReadHeld = false;
    {
        std::unique_lock<std::mutex> stateLock(stateMutex);
        stateChanged.wait(stateLock, [&]() { return freeAllStarted; });
        completedWhileReadHeld = stateChanged.wait_for(
            stateLock,
            std::chrono::milliseconds(200),
            [&]() { return freeAllCompleted; });
        releaseReadGuard = true;
        stateChanged.notify_all();
    }
    worker.join();
    freeAllWorker.join();

    const dlcv_infer::flow::ModelPoolStats afterFreeAll = dlcv_infer::flow::GetModelPoolStats();
    if (!inferOk || completedWhileReadHeld || !freeAllCompleted || afterFreeAll.totalEntries != 0) {
        std::cerr << "FAIL: 活动推理与 FreeAllModels 同步失败: "
                  << (!inferError.empty() ? inferError
                      : (completedWhileReadHeld ? "共享锁释放前 FreeAllModels 已完成" : "模型池未清空"))
                  << "\n";
        return false;
    }

    std::cout << "PASS: FreeAllModels 等待活动推理完成后释放模型\n";
    return true;
}

static bool RunModelInfoConcurrencyChecks(
    const std::wstring& dvtPath,
    const cv::Mat& dvtImage,
    const std::wstring& dvstPath,
    const cv::Mat& dvstImage) {
    bool ok = true;

    const auto checkReleasedModelInfo = [](const std::string& label, const std::wstring& modelPath) {
        try {
            dlcv_infer::Model model(modelPath, 0);
            const dlcv_infer::json beforeRelease = model.GetModelInfo();
            if (beforeRelease.is_null() || beforeRelease.empty()) {
                std::cerr << "FAIL: " << label << " 释放前模型信息为空\n";
                return false;
            }
            model.FreeModel();
            try {
                const dlcv_infer::json afterRelease = model.GetModelInfo();
                std::cerr << "FAIL: " << label << " 释放后仍返回模型信息: "
                          << afterRelease.dump() << "\n";
                return false;
            } catch (const std::exception&) {
            }
            model.FreeModel();
            std::cout << "PASS: " << label << " 获取信息后释放不会返回旧缓存\n";
            return true;
        } catch (const std::exception& ex) {
            std::cerr << "FAIL: " << label << " 释放后模型信息检查失败: " << ex.what() << "\n";
            return false;
        }
    };

    ok = checkReleasedModelInfo("dvt", dvtPath) && ok;
    ok = checkReleasedModelInfo("dvst", dvstPath) && ok;

    const int dvtIndex = LoadModel(dvtPath);
    if (dvtIndex < 0) return false;
    ok = RunConcurrentModelInfoAndInference(
        "dvt 同一实例推理与模型信息并发", dvtIndex, dvtImage, 4, 10) && ok;
    const bool dvtReleaseOk = dlcv_infer_cpp_free_model_c(dvtIndex) == 0;
    if (!dvtReleaseOk) std::cerr << "FAIL: dvt 并发验证后释放失败\n";
    ok = dvtReleaseOk && ok;

    const int dvstIndex = LoadModel(dvstPath);
    if (dvstIndex < 0) return false;
    ok = RunConcurrentModelInfoAndInference(
        "dvst 同一实例推理与模型信息并发", dvstIndex, dvstImage, 4, 10) && ok;
    const bool dvstReleaseOk = dlcv_infer_cpp_free_model_c(dvstIndex) == 0;
    if (!dvstReleaseOk) std::cerr << "FAIL: dvst 并发验证后释放失败\n";
    return dvstReleaseOk && ok;
}

int main(int argc, char** argv) {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    const int pureCResultCode = dlcv_infer_pure_c_header_test();
    if (pureCResultCode != 2) {
        std::cerr << "FAIL: 纯 C 结构化接口调用失败，返回码=" << pureCResultCode << "\n";
        return 1;
    }
    std::cout << "PASS: 纯 C 结构化接口编译和调用成功\n";

    if (argc == 2 && std::strcmp(argv[1], "--c-api-invalid-input") == 0) {
        const int invalidInputCode = dlcv_infer_pure_c_invalid_input_test();
        if (invalidInputCode != 0) {
            std::cerr << "FAIL: C 接口异常输入兼容性检查失败，返回码="
                      << invalidInputCode << "\n";
            return 1;
        }
        std::cout << "PASS: C 接口异常输入兼容性检查通过\n";
        return 0;
    }

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

    if (argc == 2 && std::strcmp(argv[1], "--memory-growth") == 0) {
        const bool memoryOk = RunModelMemoryGrowthCheck("dvst", dvstPath, dvstImage, 10, 10, 64.0, 64.0);
        dlcv_infer_cpp_free_all_models_c();
        std::cout << (memoryOk ? "\n内存测试通过\n" : "\n内存测试失败\n");
        return memoryOk ? 0 : 1;
    }
    if (argc == 2 && std::strcmp(argv[1], "--memory-growth-dvt") == 0) {
        const bool memoryOk = RunModelMemoryGrowthCheck("dvt", dvtPath, dvtImage, 10, 10, 64.0, 64.0);
        dlcv_infer_cpp_free_all_models_c();
        std::cout << (memoryOk ? "\n内存测试通过\n" : "\n内存测试失败\n");
        return memoryOk ? 0 : 1;
    }
    if (argc == 2 && std::strcmp(argv[1], "--idle-cache-clear") == 0) {
        return RunDvstIdleCacheClearCheck(dvstPath, dvstImage) ? 0 : 1;
    }
    if (argc == 2 && std::strcmp(argv[1], "--dvst-temp-dir") == 0) {
        const bool directoryOk = RunDvstTempDirectoryReuseCheck(dvstPath, dvstImage)
            && RunDvstContentUpdateCheck(dvstPath, dvstImage);
        dlcv_infer_cpp_free_all_models_c();
        std::cout << (directoryOk ? "\ndvst 目录测试通过\n" : "\ndvst 目录测试失败\n");
        return directoryOk ? 0 : 1;
    }
    if (argc == 2 && std::strcmp(argv[1], "--model-info-concurrency") == 0) {
        const bool concurrencyOk = RunModelInfoConcurrencyChecks(
            dvtPath, dvtImage, dvstPath, dvstImage);
        dlcv_infer_cpp_free_all_models_c();
        std::cout << (concurrencyOk ? "\n并发测试通过\n" : "\n并发测试失败\n");
        return concurrencyOk ? 0 : 1;
    }
    if (argc == 2 && std::strcmp(argv[1], "--model-load-concurrency") == 0) {
        const bool concurrencyOk = RunConcurrentModelLoadingCheck(dvtPath, dvstPath, 4);
        dlcv_infer_cpp_free_all_models_c();
        std::cout << (concurrencyOk ? "\n模型加载并发测试通过\n" : "\n模型加载并发测试失败\n");
        return concurrencyOk ? 0 : 1;
    }

    bool ok = true;
    ok = RunConcurrentModelLoadingCheck(dvtPath, dvstPath, 4) && ok;
    dlcv_infer_cpp_free_all_models_c();
    ok = RunCapiExportCompletenessCheck() && ok;
    ok = RunNativeJsonDvtByteRegression(dvtPath, dvtImage) && ok;
    ok = RunNativeJsonDvstCheck(dvstPath, dvstImage) && ok;
    ok = RunNativeCompatibilityCheck(dvtPath, dvtImage) && ok;
    ok = RunAllCompatibilityFlowChecks() && ok;
    ok = RunDvstTempDirectoryReuseCheck(dvstPath, dvstImage) && ok;
    ok = RunDvstContentUpdateCheck(dvstPath, dvstImage) && ok;
    ok = RunModelPoolGenerationCheck(dvstPath, dvstImage) && ok;
    ok = RunFreeAllDuringInferenceCheck(dvstPath, dvstImage) && ok;

    ok = RunModelInfoConcurrencyChecks(dvtPath, dvtImage, dvstPath, dvstImage) && ok;

    dlcv_infer_cpp_free_all_models_c();
    std::cout << (ok ? "\nTest PASSED\n" : "\nTest FAILED\n");
    return ok ? 0 : 1;
}
