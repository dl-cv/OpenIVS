#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <filesystem>
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
#include "../common/NativeSharedIndexTestHelper.h"

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

static bool GetConfiguredCoreDllPath(std::wstring& path, std::string& error) {
    constexpr wchar_t kEnvironmentName[] = L"DLCV_TEST_CORE_DLL";
    const DWORD required = GetEnvironmentVariableW(kEnvironmentName, nullptr, 0);
    if (required == 0) {
        error = "未设置 DLCV_TEST_CORE_DLL，必须指定本轮 dlcv_infer.dll 的绝对路径";
        return false;
    }

    std::vector<wchar_t> buffer(static_cast<size_t>(required));
    const DWORD written = GetEnvironmentVariableW(
        kEnvironmentName, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (written == 0 || written >= buffer.size()) {
        error = "读取 DLCV_TEST_CORE_DLL 失败: " + std::to_string(GetLastError());
        return false;
    }

    const std::filesystem::path configuredPath(buffer.data());
    if (!configuredPath.is_absolute()) {
        error = "DLCV_TEST_CORE_DLL 必须是绝对路径";
        return false;
    }

    std::error_code fileError;
    if (!std::filesystem::is_regular_file(configuredPath, fileError)) {
        error = "DLCV_TEST_CORE_DLL 指定的文件不存在或不是普通文件: "
            + WideToUtf8(configuredPath.wstring());
        return false;
    }

    path = configuredPath.lexically_normal().wstring();
    return true;
}

static std::wstring GetLoadedModulePath(HMODULE module) {
    std::vector<wchar_t> buffer(32768);
    const DWORD written = GetModuleFileNameW(
        module, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (written == 0 || written >= buffer.size()) return {};
    return std::wstring(buffer.data(), written);
}

static HMODULE LoadConfiguredCoreDll(std::string& error) {
    std::wstring configuredPath;
    if (!GetConfiguredCoreDllPath(configuredPath, error)) return nullptr;

    HMODULE module = LoadLibraryW(configuredPath.c_str());
    if (module == nullptr) {
        error = "DLCV_TEST_CORE_DLL 加载失败: " + std::to_string(GetLastError())
            + "，路径=" + WideToUtf8(configuredPath);
        return nullptr;
    }

    const std::wstring loadedPath = GetLoadedModulePath(module);
    if (loadedPath.empty()) {
        error = "读取已加载核心 DLL 路径失败: " + std::to_string(GetLastError());
        FreeLibrary(module);
        return nullptr;
    }

    std::cout << "[DLCV_TEST_CORE_DLL] loaded: " << WideToUtf8(loadedPath) << "\n";
    return module;
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
    api.module = LoadConfiguredCoreDll(error);
    if (api.module == nullptr) {
        if (error.empty()) {
            error = "dlcv_infer.dll 加载失败: " + std::to_string(GetLastError());
        }
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
    std::string loadError;
    HMODULE nativeModule = LoadConfiguredCoreDll(loadError);
    const bool ownsNativeModule = nativeModule != nullptr;
    if (nativeModule == nullptr) {
        std::cerr << "FAIL: " << loadError << "\n";
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
        "dlcv_infer_cpp_get_all_models_c",
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

    DlcvCResult wrapperMissing = dlcv_infer_c(INT_MAX, &imageList);
    DlcvCResult nativeMissing = native.infer(INT_MAX, &imageList);
    const std::string wrapperFailureFingerprint = BuildCompleteFingerprint(wrapperMissing);
    const std::string nativeFailureFingerprint = BuildCompleteFingerprint(nativeMissing);
    const bool sameFailureResult = wrapperFailureFingerprint == nativeFailureFingerprint &&
        wrapperMissing.code == 2 && nativeMissing.code == 2 &&
        wrapperMissing.message != nullptr && nativeMissing.message != nullptr &&
        std::strcmp(wrapperMissing.message, "Model not found.") == 0 &&
        std::strcmp(nativeMissing.message, "Model not found.") == 0 &&
        wrapperMissing.sample_results == nullptr && nativeMissing.sample_results == nullptr &&
        wrapperMissing.n == 0 && nativeMissing.n == 0;
    dlcv_free_model_result_c(&wrapperMissing);
    native.freeResult(&nativeMissing);
    const bool sameFailureRelease = IsReleasedResult(wrapperMissing, 2) && IsReleasedResult(nativeMissing, 2);

    const int wrapperFirstFree = dlcv_free_model_c(wrapperIndex);
    const int wrapperSecondFree = dlcv_free_model_c(wrapperIndex);
    const int nativeFirstFree = native.freeModel(nativeIndex);
    const bool wrapperFreeOk = wrapperFirstFree == 0 && wrapperSecondFree == 0;
    const bool nativeFreeOk = nativeFirstFree == 0;
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
            std::cerr << "  dlcv_infer 模型释放返回值: " << nativeFirstFree << "\n";
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
    if (modelIndex == -1) {
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
        dlcv_infer_cpp_free_model_c(modelIndex) == 0;
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
        {L"Y:\\测试模型\\AOI-元件提取_PLUS_s.dvst", L"Y:\\测试模型\\AOI-1.jpg"},
        {L"Y:\\测试模型\\AOI-无CAD检测_PLUS_s.dvst", L"Y:\\测试模型\\OK1.png"},
        {L"Y:\\测试模型\\模型1-元件提取_PLUS_s.dvst", L"Y:\\测试模型\\OK1.png"},
        {L"Y:\\测试模型\\模型2-元件检测_PLUS_s.dvst", L"Y:\\测试模型\\OK1.png"},
        {L"Y:\\测试模型\\模型3-IC检测_PLUS_s.dvst", L"Y:\\测试模型\\OK1.png"},
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
        return modelIndex != -1;
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
    bool missingLoadRejected = CopyJsonCallResult(
        native.loadModel, native.freeResult, missingLoadConfig, nativeMissingLoad, error);
    missingLoadRejected = CopyJsonCallResult(
        dlcv_load_model, dlcv_free_result, missingLoadConfig, wrapperMissingLoad, error) && missingLoadRejected;
    try {
        missingLoadRejected = dlcv_infer::json::parse(nativeMissingLoad).value("code", 0) != 0 &&
            dlcv_infer::json::parse(wrapperMissingLoad).value("code", 0) != 0 && missingLoadRejected;
    } catch (...) {
        missingLoadRejected = false;
    }
    if (!missingLoadRejected) {
        std::cerr << "FAIL: dvt 不存在路径未被授权检查或底层加载拒绝"
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
    const bool nativeInfoRead = CopyJsonCallResult(
        native.getModelInfo, native.freeResult, indexConfig, nativeInfo, error);
    const bool wrapperInfoRead = CopyJsonCallResult(
        dlcv_get_model_info, dlcv_free_result, indexConfig, wrapperInfo, error);
    bool ok = nativeInfoRead && wrapperInfoRead && nativeInfo == wrapperInfo;
    if (!ok) {
        std::cerr << "FAIL: dvt 原生 JSON 模型信息逐字节比较，native_read="
                  << nativeInfoRead << "，wrapper_read=" << wrapperInfoRead
                  << "，bytes=" << nativeInfo.size() << "/" << wrapperInfo.size() << "\n";
    }

    bool structuredInfoOk = false;
    const char* structuredInfo = dlcv_infer_cpp_get_model_info_c(modelIndex);
    if (structuredInfo != nullptr) {
        try {
            const auto info = dlcv_infer::json::parse(structuredInfo);
            structuredInfoOk = info.is_object() && info.value("code", 0) == 0 &&
                info.contains("model_index") && info.at("model_index").is_number_integer() &&
                info.at("model_index").get<int>() == modelIndex;
        } catch (...) {
            structuredInfoOk = false;
        }
        dlcv_infer_cpp_free_string_c(structuredInfo);
    }
    if (!structuredInfoOk) {
        std::cerr << "FAIL: dvt 扩展 C 信息未保留当前 model_index\n";
    }
    ok = structuredInfoOk && ok;
    std::string wrapperInfoAfterStructured;
    const bool rereadInfo = CopyJsonCallResult(dlcv_get_model_info, dlcv_free_result,
        indexConfig, wrapperInfoAfterStructured, error);
    const bool unchangedInfo = rereadInfo && nativeInfo == wrapperInfoAfterStructured;
    if (!unchangedInfo) {
        std::cerr << "FAIL: dvt 扩展 C 信息查询后原生 JSON 字节发生变化\n";
    }
    ok = unchangedInfo && ok;

    // 掩码地址由每次调用单独分配，逐字节比较时关闭掩码返回。
    const std::string inferConfig = BuildNativeInferConfig(modelIndex, image, false);
    std::string nativeInfer;
    std::string wrapperInfer;
    ok = CopyJsonCallResult(native.infer, native.freeModelResult, inferConfig,
             nativeInfer, error) && ok;
    ok = CopyJsonCallResult(wrapper.infer, dlcv_free_model_result, inferConfig,
             wrapperInfer, error) && ok;
    if (nativeInfer != wrapperInfer) {
        std::cerr << "FAIL: dvt 原生 JSON 推理逐字节比较，bytes="
                  << nativeInfer.size() << "/" << wrapperInfer.size() << "\n";
    }
    ok = nativeInfer == wrapperInfer && ok;

    const std::string missingIndexConfig = dlcv_infer::json{ { "model_index", -987654 } }.dump();
    std::string nativeMissingFree;
    std::string wrapperMissingFree;
    ok = CopyJsonCallResult(native.freeModel, native.freeResult, missingIndexConfig,
             nativeMissingFree, error) && ok;
    ok = CopyJsonCallResult(dlcv_free_model, dlcv_free_result, missingIndexConfig,
             wrapperMissingFree, error) && ok;
    try {
        ok = dlcv_infer::json::parse(nativeMissingFree).value("code", 0) != 0 &&
            dlcv_infer::json::parse(wrapperMissingFree).value("code", 0) != 0 && ok;
    } catch (...) {
        ok = false;
    }

    std::string freeResult;
    std::string repeatedFreeResult;
    const bool validFreeOk = CopyJsonCallResult(
        dlcv_free_model, dlcv_free_result, indexConfig, freeResult, error);
    const bool repeatedFreeOk = CopyJsonCallResult(
        dlcv_free_model, dlcv_free_result, indexConfig, repeatedFreeResult, error);
    try {
        ok = validFreeOk && repeatedFreeOk &&
            dlcv_infer::json::parse(freeResult).value("code", -1) == 0 &&
            dlcv_infer::json::parse(repeatedFreeResult).value("code", -1) == 0 && ok;
    } catch (...) {
        ok = false;
    }
    dlcv_free_all_models();
    if (!ok) {
        std::cerr << "FAIL: dvt 原生 JSON 逐字节回归失败"
                  << (error.empty() ? "" : ": " + error) << "\n";
        return false;
    }

    std::cout << "PASS: dvt 原生 JSON 成功结果保持逐字节转发，非法索引返回错误，重复释放返回成功\n";
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
    if (!ParseSuccessfulModelIndex(loadResult, modelIndex)) {
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

static bool CheckStructuredSharedIndex(int modelIndex, const cv::Mat& image) {
    bool ok = true;
    const char* info = dlcv_infer_cpp_get_model_info_c(modelIndex);
    if (info == nullptr) {
        ok = false;
    } else {
        try {
            const auto parsed = dlcv_infer::json::parse(info);
            ok = parsed.is_object() && parsed.value("code", 0) == 0;
        } catch (...) {
            ok = false;
        }
        dlcv_infer_cpp_free_string_c(info);
    }

    DlcvCImage input{};
    input.data_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(image.data));
    input.height = image.rows;
    input.width = image.cols;
    input.channel = image.channels();
    DlcvCImageList images{};
    images.images = &input;
    images.n = 1;
    const char* params = R"({"threshold":0.5,"with_mask":true})";
    DlcvCResult result = dlcv_infer_cpp_infer_with_params_c(modelIndex, &images, params);
    ok = result.code == 0 && result.n == 1 && result.sample_results != nullptr && ok;
    const int expectedObjects = result.code == 0 && result.n == 1 && result.sample_results != nullptr
        ? result.sample_results[0].n : -1;
    if (!ok) {
        std::cerr << "FAIL: 结构化共享调用，index=" << modelIndex
                  << "，code=" << result.code << "，samples=" << result.n << "\n";
    }
    dlcv_infer_cpp_free_model_result_c(&result);
    ok = result.message == nullptr && result.sample_results == nullptr && result.n == 0 && ok;

    const char* infer = dlcv_infer_cpp_infer_json_c(modelIndex, &input, params);
    if (infer == nullptr) {
        ok = false;
    } else {
        try {
            const auto parsed = dlcv_infer::json::parse(infer);
            const auto& objects = parsed.is_array() ? parsed : parsed.at("result_list");
            bool jsonOk = objects.is_array() && expectedObjects >= 0 &&
                objects.size() == static_cast<std::size_t>(expectedObjects);
            if (parsed.is_object()) {
                jsonOk = parsed.at("ok").is_boolean() && parsed.contains("reason") && jsonOk;
            }
            for (const auto& object : objects) {
                jsonOk = object.is_object() && object.at("category_id").is_number_integer() &&
                    object.at("category_name").is_string() && object.at("score").is_number() &&
                    object.at("bbox").is_array() && object.at("bbox").size() == 4 && jsonOk;
            }
            if (!jsonOk) {
                std::cerr << "FAIL: 单图 JSON 结构或数量不一致，index=" << modelIndex
                          << "，期望目标数=" << expectedObjects << "\n";
            }
            ok = jsonOk && ok;
        } catch (...) {
            ok = false;
        }
        dlcv_infer_cpp_free_string_c(infer);
    }
    return ok;
}


static bool IsSuccessfulNativeStatus(const std::string& value) {
    try {
        const auto result = dlcv_infer::json::parse(value);
        return result.is_object() && result.contains("code") &&
            result.at("code").is_number_integer() && result.at("code").get<int>() == 0;
    } catch (...) {
        return false;
    }
}

static bool IsRejectedNativeStatus(const std::string& value) {
    try {
        const auto result = dlcv_infer::json::parse(value);
        return result.is_object() && result.contains("code") &&
            result.at("code").is_number_integer() && result.at("code").get<int>() != 0;
    } catch (...) {
        return false;
    }
}

static bool IsMissingModelNativeStatus(const std::string& value) {
    try {
        const auto result = dlcv_infer::json::parse(value);
        return result.is_object() && result.contains("code") &&
            result.at("code").is_number_integer() && result.at("code").get<int>() == 2 &&
            result.contains("message") && result.at("message").is_string() &&
            result.at("message").get<std::string>() == "Model not found.";
    } catch (...) {
        return false;
    }
}

static int LoadCapiReference(const std::wstring& modelPath, bool nativeJson) {
    const std::string utf8Path = WideToUtf8(modelPath);
    if (!nativeJson) return dlcv_infer_cpp_load_model_c(utf8Path.c_str(), 0);
    const std::string config = dlcv_infer::json{
        { "model_path", utf8Path }, { "device_id", 0 }
    }.dump();
    std::string response;
    std::string error;
    int modelIndex = -1;
    if (!CopyJsonCallResult(dlcv_load_model, dlcv_free_result, config, response, error) ||
        !ParseSuccessfulModelIndex(response, modelIndex)) {
        std::cerr << "FAIL: 原生 JSON 持有加载失败: " << response << " " << error << "\n";
        return -1;
    }
    return modelIndex;
}

static bool FreeCapiReference(int modelIndex, bool nativeJson) {
    if (!nativeJson) return dlcv_infer_cpp_free_model_c(modelIndex) == 0;
    const std::string config = dlcv_infer::json{ { "model_index", modelIndex } }.dump();
    std::string response;
    std::string error;
    return CopyJsonCallResult(dlcv_free_model, dlcv_free_result, config, response, error) &&
        IsSuccessfulNativeStatus(response);
}

static bool CheckCapiIndexReleased(int modelIndex) {
    const char* info = dlcv_infer_cpp_get_model_info_c(modelIndex);
    bool ok = info == nullptr;
    if (info != nullptr) dlcv_infer_cpp_free_string_c(info);
    const std::string config = dlcv_infer::json{ { "model_index", modelIndex } }.dump();
    std::string response;
    std::string error;
    ok = CopyJsonCallResult(dlcv_get_model_info, dlcv_free_result, config, response, error) &&
        IsMissingModelNativeStatus(response) && ok;
    ok = FreeCapiReference(modelIndex, false) && ok;
    ok = FreeCapiReference(modelIndex, true) && ok;
    ok = dlcv_test::QueryLoadedSharedIndexType(modelIndex) == 0 && ok;
    return ok;
}

static bool RunRepeatedCapiReferenceCheck(const std::wstring& modelPath, const cv::Mat& image) {
    struct Scenario {
        const char* label;
        bool firstNative;
        bool secondNative;
    };
    const Scenario scenarios[] = {
        { "结构化重复加载", false, false },
        { "原生 JSON 重复加载", true, true },
        { "结构化后原生 JSON 加载", false, true },
        { "原生 JSON 后结构化加载", true, false }
    };
    bool allOk = true;
    for (const auto& scenario : scenarios) {
        dlcv_free_all_models();
        NativeJsonModelCleanup cleanup;
        const int first = LoadCapiReference(modelPath, scenario.firstNative);
        const int second = LoadCapiReference(modelPath, scenario.secondNative);
        bool ok = first >= 0 && second == first;
        if (first >= 0 && second >= 0) {
            ok = CheckStructuredSharedIndex(first, image) && ok;
            ok = FreeCapiReference(second, true) && ok;
            ok = CheckStructuredSharedIndex(first, image) && ok;
            ok = FreeCapiReference(first, false) && ok;
            ok = CheckCapiIndexReleased(first) && ok;
            const int reloaded = LoadCapiReference(modelPath, scenario.firstNative);
            ok = reloaded >= 0 && reloaded != first && ok;
            if (reloaded >= 0) ok = FreeCapiReference(reloaded, true) && ok;
        }
        std::cout << (ok ? "PASS: " : "FAIL: ") << scenario.label
                  << "，index=" << first << "/" << second
                  << "，两次持有逐次释放，旧 index 不复用\n";
        allOk = ok && allOk;
    }
    return allOk;
}

static bool RunConcurrentCapiReferenceReleaseCheck(
    const std::wstring& modelPath, const cv::Mat& image) {
    dlcv_free_all_models();
    NativeJsonModelCleanup cleanup;
    const int ownerIndex = dlcv_test::LoadOwnedModel(modelPath, 0);
    if (ownerIndex == -1) return false;
    constexpr int referenceCount = 4;
    constexpr int releaseThreadCount = referenceCount * 2;
    bool ok = true;
    for (int i = 0; i < referenceCount; ++i) {
        ok = LoadCapiReference(modelPath, (i & 1) != 0) == ownerIndex && ok;
    }
    std::atomic<int> ready{0};
    std::atomic<bool> start{false};
    std::atomic<int> successes{0};
    std::vector<std::thread> workers;
    for (int i = 0; i < releaseThreadCount; ++i) {
        workers.emplace_back([&, i]() {
            ready.fetch_add(1);
            while (!start.load()) std::this_thread::yield();
            if (FreeCapiReference(ownerIndex, (i & 1) != 0)) successes.fetch_add(1);
        });
    }
    while (ready.load() != releaseThreadCount) std::this_thread::yield();
    start.store(true);
    for (auto& worker : workers) worker.join();
    ok = successes.load() == releaseThreadCount && ok;
    // C API 的持有耗尽后，多余释放不能消耗外部所有者的持有。
    const std::string ownerInfo = dlcv_test::GetBorrowedModelInfoResult(ownerIndex);
    ok = IsSuccessfulNativeStatus(ownerInfo) && ok;
    ok = CheckStructuredSharedIndex(ownerIndex, image) && ok;
    ok = FreeCapiReference(ownerIndex, false) && ok;
    ok = dlcv_test::ReleaseOwnedModel(ownerIndex) && ok;
    ok = CheckCapiIndexReleased(ownerIndex) && ok;
    std::cout << (ok ? "PASS: " : "FAIL: ")
              << "并发释放，持有数=" << referenceCount
              << "，释放成功数=" << successes.load()
              << "，重复释放返回成功且不影响外部所有者\n";
    return ok;
}

static bool RunNativeIndexRangeCheck(
    const char* label, const std::wstring& modelPath, const cv::Mat& image) {
    dlcv_free_all_models();
    NativeJsonModelCleanup cleanup;
    NativeJsonApi wrapper;
    std::string error;
    if (!LoadWrapperNativeInfer(wrapper, error)) return false;
    const int modelIndex = LoadCapiReference(modelPath, false);
    if (modelIndex == -1) return false;
    const auto validConfig = dlcv_infer::json::parse(BuildNativeInferConfig(modelIndex, image));
    const dlcv_infer::json invalidIndices[] = {
        static_cast<uint64_t>(modelIndex) + (uint64_t{1} << 32),
        static_cast<int64_t>(modelIndex) - (int64_t{1} << 32),
        uint64_t{2147483648ULL}, uint64_t{18446744073709551615ULL},
        int64_t{-1}, int64_t{-2}, static_cast<int64_t>((std::numeric_limits<int>::min)()),
        1.5, "0", true, nullptr
    };
    struct Call {
        const char* name;
        NativeJsonApi::StringCall invoke;
        NativeJsonApi::FreeString release;
    };
    const Call calls[] = {
        { "信息", dlcv_get_model_info, dlcv_free_result },
        { "推理", wrapper.infer, dlcv_free_model_result },
        { "释放", dlcv_free_model, dlcv_free_result }
    };
    bool ok = true;
    for (const auto& invalidIndex : invalidIndices) {
        auto config = validConfig;
        config["model_index"] = invalidIndex;
        for (const auto& call : calls) {
            std::string response;
            const bool rejected = CopyJsonCallResult(call.invoke, call.release,
                config.dump(), response, error) && IsRejectedNativeStatus(response);
            if (!rejected) {
                std::cerr << "FAIL: " << label << "，" << call.name
                          << "未拒绝非法 index=" << invalidIndex.dump() << "\n";
            }
            ok = rejected && ok;
        }
        ok = CheckStructuredSharedIndex(modelIndex, image) && ok;
    }
    ok = FreeCapiReference(modelIndex, false) && ok;
    ok = CheckCapiIndexReleased(modelIndex) && ok;
    std::cout << (ok ? "PASS: " : "FAIL: ") << label
              << "，JSON model_index 仅接受 int 范围内的非负整数，合法 index=" << modelIndex
              << " 的持有及推理不受影响\n";
    return ok;
}

static bool RunCapiFreeAllEntryPointsCheck(
    const std::wstring& modelPath, const cv::Mat& image, int expectedIndexType) {
    struct EntryPoint {
        const char* label;
        void (*clear)();
    };
    const EntryPoint entryPoints[] = {
        { "扩展 C", []() { dlcv_infer_cpp_free_all_models_c(); } },
        { "原生 JSON C", []() { dlcv_free_all_models(); } },
        { "C++ Utils", []() { dlcv_infer::Utils::FreeAllModels(); } },
        { "C++ NativeApi", []() { dlcv_infer::NativeApi::FreeAllModels(); } }
    };
    bool allOk = true;
    for (const auto& entryPoint : entryPoints) {
        dlcv_free_all_models();
        NativeJsonModelCleanup cleanup;
        const int modelIndex = LoadCapiReference(modelPath, true);
        const int repeatedIndex = LoadCapiReference(modelPath, false);
        bool ok = true;
        const auto check = [&](bool passed, const char* stage) {
            if (!passed) {
                std::cerr << "FAIL: " << entryPoint.label << "，" << stage
                          << "，resource_type=" << expectedIndexType
                          << "，index=" << modelIndex << "/" << repeatedIndex << "\n";
            }
            ok = passed && ok;
        };
        check(modelIndex >= 0 && repeatedIndex >= 0, "两次加载成功");
        // 普通模型重复加载复用资源；每次归档加载独立登记 DVS。
        check(expectedIndexType == 1 ? repeatedIndex == modelIndex :
            expectedIndexType == 2 && repeatedIndex != modelIndex, "重复加载编号规则");
        for (const int index : { modelIndex, repeatedIndex }) {
            if (index >= 0) {
                check(dlcv_test::QueryLoadedSharedIndexType(index) == expectedIndexType,
                    "释放前资源类型");
                check(CheckStructuredSharedIndex(index, image), "释放前信息与推理");
            }
        }
        entryPoint.clear();
        for (const int index : { modelIndex, repeatedIndex }) {
            if (index >= 0) check(CheckCapiIndexReleased(index), "全量释放后旧编号失效");
        }
        const int reloaded = LoadCapiReference(modelPath, true);
        check(reloaded >= 0 && reloaded != modelIndex && reloaded != repeatedIndex,
            "重新加载不复用任一旧编号");
        for (const int index : { modelIndex, repeatedIndex }) {
            if (index >= 0) check(CheckCapiIndexReleased(index), "重新加载后旧编号仍失效");
        }
        if (reloaded >= 0) {
            check(dlcv_test::QueryLoadedSharedIndexType(reloaded) == expectedIndexType,
                "重新加载资源类型");
            check(CheckStructuredSharedIndex(reloaded, image), "重新加载信息与推理");
            check(FreeCapiReference(reloaded, false), "重新加载持有释放");
            check(CheckCapiIndexReleased(reloaded), "最后持有释放后失效");
        }
        std::cout << (ok ? "PASS: " : "FAIL: ") << entryPoint.label
                  << " 全量释放清除 C ABI 持有及缓存，重新加载不复用 index"
                  << "，resource_type=" << expectedIndexType
                  << "，index=" << modelIndex << "/" << repeatedIndex
                  << "，reloaded=" << reloaded << "\n";
        allOk = ok && allOk;
    }
    return allOk;
}

static bool RunNativeLoadLifecycleCheck() {
    std::mutex mutex;
    std::condition_variable cv;
    bool started = false;
    bool finished = false;
    bool finishedUnderWriteLock = false;
    bool rejected = false;
    std::thread worker;
    {
        dlcv_infer::flow::ModelLifecycleWriteGuard lifecycleGuard;
        worker = std::thread([&]() {
            {
                std::lock_guard<std::mutex> lock(mutex);
                started = true;
            }
            cv.notify_all();
            std::string response;
            std::string error;
            const bool callOk = CopyJsonCallResult(dlcv_load_model, dlcv_free_result,
                R"({"model_path":"","device_id":0})", response, error);
            {
                std::lock_guard<std::mutex> lock(mutex);
                rejected = callOk && IsRejectedNativeStatus(response);
                finished = true;
            }
            cv.notify_all();
        });
        std::unique_lock<std::mutex> lock(mutex);
        cv.wait(lock, [&]() { return started; });
        finishedUnderWriteLock = cv.wait_for(lock, std::chrono::milliseconds(250), [&]() { return finished; });
    }
    worker.join();
    const bool ok = !finishedUnderWriteLock && rejected;
    std::cout << (ok ? "PASS: " : "FAIL: ")
              << "原生 JSON 加载等待全量释放的生命周期写锁\n";
    return ok;
}

static bool RunNativeJsonStructuredRecoveryCheck(
    const char* label, const std::wstring& modelPath, const cv::Mat& image) {
    const std::string loadConfig = dlcv_infer::json{
        { "model_path", WideToUtf8(modelPath) }, { "device_id", 0 }
    }.dump();
    std::string response;
    std::string error;
    NativeJsonApi wrapper;
    if (!LoadWrapperNativeInfer(wrapper, error)) {
        std::cerr << "FAIL: " << error << "\n";
        return false;
    }
    int modelIndex = -1;
    if (!CopyJsonCallResult(dlcv_load_model, dlcv_free_result, loadConfig, response, error) ||
        !ParseSuccessfulModelIndex(response, modelIndex)) {
        std::cerr << "FAIL: " << label << "，原生 JSON 加载失败\n";
        return false;
    }

    bool ok = CheckStructuredSharedIndex(modelIndex, image);
    const std::string indexConfig = dlcv_infer::json{ { "model_index", modelIndex } }.dump();
    const std::string inferConfig = BuildNativeInferConfig(modelIndex, image, true);
    ok = CopyJsonCallResult(wrapper.infer, dlcv_free_model_result,
             inferConfig, response, error) && IsSuccessfulNativeInfer(response, true) && ok;
    const bool freed = dlcv_infer_cpp_free_model_c(modelIndex) == 0;
    ok = freed && ok;
    // 两种持有均应释放，不能仅删除包装后留下原生模型。
    bool rejected = false;
    if (CopyJsonCallResult(dlcv_get_model_info, dlcv_free_result,
            indexConfig, response, error)) {
        rejected = IsMissingModelNativeStatus(response);
    }
    ok = rejected && ok;
    if (!freed) {
        const char* result = dlcv_free_model(indexConfig.c_str());
        if (result != nullptr) dlcv_free_result(result);
    }
    std::cout << (ok ? "PASS: " : "FAIL: ") << label
              << "，原生 JSON 载入后结构化信息、推理、字符串和掩码释放检查\n";
    return ok;
}

static bool RunUnknownIndexWithoutModuleCheck() {
    const auto hasInferModule = []() {
        return GetModuleHandleW(L"dlcv_infer.dll") != nullptr ||
            GetModuleHandleW(L"dlcv_infer_v.dll") != nullptr;
    };
    if (hasInferModule()) {
        std::cerr << "FAIL: 无模块索引检查需在未加载推理 DLL 的进程运行\n";
        return false;
    }
    NativeJsonApi wrapper;
    std::string error;
    if (!LoadWrapperNativeInfer(wrapper, error)) {
        std::cerr << "FAIL: " << error << "\n";
        return false;
    }

    constexpr int missingIndex = INT_MAX;
    cv::Mat image(1, 1, CV_8UC3, cv::Scalar(0, 0, 0));
    DlcvCImage cImage{};
    cImage.data_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(image.data));
    cImage.height = image.rows;
    cImage.width = image.cols;
    cImage.channel = image.channels();
    DlcvCImageList imageList{};
    imageList.images = &cImage;
    imageList.n = 1;

    bool ok = true;
    for (int attempt = 0; attempt < 2; ++attempt) {
        const char* allModelsText = dlcv_infer_cpp_get_all_models_c();
        bool listOk = false;
        if (allModelsText != nullptr) {
            try {
                const auto allModels = dlcv_infer::json::parse(allModelsText);
                listOk = allModels.is_object() && allModels.value("code", 1) == 0 &&
                    allModels.value("message", std::string()) == "success" &&
                    allModels.contains("modules") && allModels.at("modules").is_array() &&
                    allModels.at("modules").empty();
            } catch (...) {}
            dlcv_infer_cpp_free_string_c(allModelsText);
        }
        ok = listOk && !hasInferModule() && ok;
    }

    DlcvCResult structuredMissing = dlcv_infer_c(missingIndex, &imageList);
    const bool structuredMissingOk = structuredMissing.code == 2 &&
        structuredMissing.message != nullptr &&
        std::strcmp(structuredMissing.message, "Model not found.") == 0 &&
        structuredMissing.sample_results == nullptr && structuredMissing.n == 0;
    dlcv_free_model_result_c(&structuredMissing);
    ok = structuredMissingOk && IsReleasedResult(structuredMissing, 2) && ok;

    const std::string missingInfoConfig = dlcv_infer::json{
        { "model_index", missingIndex }
    }.dump();
    const std::string missingInferConfig = BuildNativeInferConfig(missingIndex, image, false);
    std::string response;
    ok = CopyJsonCallResult(dlcv_get_model_info, dlcv_free_result,
             missingInfoConfig, response, error) &&
        IsMissingModelNativeStatus(response) && ok;
    ok = CopyJsonCallResult(wrapper.infer, dlcv_free_model_result,
             missingInferConfig, response, error) &&
        IsMissingModelNativeStatus(response) && ok;

    for (int attempt = 0; attempt < 2; ++attempt) {
        ok = CopyJsonCallResult(dlcv_free_model, dlcv_free_result,
                 missingInfoConfig, response, error) &&
            IsSuccessfulNativeStatus(response) && ok;
    }
    ok = dlcv_infer_cpp_free_model_c(missingIndex) == 0 && ok;
    ok = dlcv_infer_cpp_free_model_c(missingIndex) == 0 && ok;

    const char* invalidConfigs[] = {
        R"({"model_index":2147483648})",
        R"({"model_index":4294967338})",
        R"({"model_index":-4294967254})",
        R"({"model_index":18446744073709551615})",
        R"({"model_index":-1})",
        R"({"model_index":-2})",
        R"({"model_index":0.5})",
        R"({"model_index":"42"})",
        R"({"model_index":true})",
        R"({"model_index":null})"
    };
    struct Call {
        NativeJsonApi::StringCall invoke;
        NativeJsonApi::FreeString release;
    };
    const Call calls[] = {
        { dlcv_get_model_info, dlcv_free_result },
        { wrapper.infer, dlcv_free_model_result },
        { dlcv_free_model, dlcv_free_result }
    };
    for (const char* config : invalidConfigs) {
        for (const auto& call : calls) {
            const bool rejected = CopyJsonCallResult(call.invoke, call.release,
                config, response, error) && IsRejectedNativeStatus(response);
            ok = rejected && !hasInferModule() && ok;
        }
    }

    const char* info = dlcv_infer_cpp_get_model_info_c(missingIndex);
    ok = info == nullptr && !hasInferModule() && ok;
    if (info != nullptr) dlcv_infer_cpp_free_string_c(info);
    std::cout << (ok ? "PASS: " : "FAIL: ")
              << "模型列表查询未加载推理 DLL；不存在的 index 返回精确缺失结果，重复释放成功，非法 JSON index 被拒绝\n";
    return ok;
}

static bool RunExternalSharedIndexRecoveryScenario(
    const char* label,
    const std::wstring& modelPath,
    const cv::Mat& image,
    bool releaseOwnerFirst) {
    const int modelIndex = dlcv_test::LoadOwnedModel(modelPath, 0);
    if (modelIndex == -1) {
        std::cerr << "FAIL: " << label << "，C++ 外部模型加载失败\n";
        return false;
    }

    bool ownerFreed = false;
    bool wrapperFreed = false;
    const auto releaseWrapper = [&]() {
        if (wrapperFreed) return true;
        const std::string config = dlcv_infer::json{{"model_index", modelIndex}}.dump();
        const char* result = dlcv_free_model(config.c_str());
        if (result == nullptr) return false;
        bool released = false;
        try { released = dlcv_infer::json::parse(result).value("code", -1) == 0; } catch (...) {}
        dlcv_free_result(result);
        wrapperFreed = released;
        return released;
    };
    const auto cleanup = [&]() {
        if (!wrapperFreed) (void)releaseWrapper();
        if (!ownerFreed) {
            ownerFreed = dlcv_test::ReleaseOwnedModel(modelIndex);
        }
    };

    const std::string indexConfig = dlcv_infer::json{{"model_index", modelIndex}}.dump();
    const std::string inferConfig = BuildNativeInferConfig(modelIndex, image, false);
    std::string response;
    std::string error;
    NativeJsonApi wrapper;
    if (!LoadWrapperNativeInfer(wrapper, error)) {
        std::cerr << "FAIL: " << error << "\n";
        cleanup();
        return false;
    }

    bool ok = CheckStructuredSharedIndex(modelIndex, image);
    ok = CopyJsonCallResult(
        dlcv_get_model_info, dlcv_free_result, indexConfig, response, error) &&
        IsSuccessfulNativeInfo(response) && ok;
    ok = CopyJsonCallResult(
        wrapper.infer, dlcv_free_model_result, inferConfig, response, error) &&
        IsSuccessfulNativeInfer(response, true) && ok;

    if (releaseOwnerFirst) {
        ownerFreed = dlcv_test::ReleaseOwnedModel(modelIndex);
        ok = ownerFreed && CheckStructuredSharedIndex(modelIndex, image) && ok;
        ok = CopyJsonCallResult(
            wrapper.infer, dlcv_free_model_result, inferConfig, response, error) &&
            IsSuccessfulNativeInfer(response, true) && ok;
        ok = releaseWrapper() && ok;
    } else {
        ok = releaseWrapper() && ok;
        try {
            const auto ownerVisible = dlcv_infer::json::parse(
                dlcv_test::GetBorrowedModelInfoResult(modelIndex));
            ok = ownerVisible.value("code", 1) == 0 &&
                dlcv_test::QueryLoadedSharedIndexType(modelIndex) != 0 && ok;
        } catch (...) {
            ok = false;
        }
        ownerFreed = dlcv_test::ReleaseOwnedModel(modelIndex);
        ok = ownerFreed && ok;
    }

    ok = CheckCapiIndexReleased(modelIndex) && ok;
    if (!ok) {
        std::cerr << "FAIL: " << label << "，外部索引恢复或释放顺序检查失败"
                  << (error.empty() ? "" : ": " + error) << "\n";
        cleanup();
        return false;
    }

    std::cout << "PASS: " << label << "，"
              << (releaseOwnerFirst ? "外部对象先释放" : "C 包装持有先释放")
              << "时资源均在最后一次释放后失效\n";
    return true;
}

static bool RunExternalSharedIndexRecoveryCheck(
    const char* label,
    const std::wstring& modelPath,
    const cv::Mat& image) {
    return RunExternalSharedIndexRecoveryScenario(label, modelPath, image, true) &&
        RunExternalSharedIndexRecoveryScenario(label, modelPath, image, false);
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
    api.module = LoadConfiguredCoreDll(error);
    if (api.module == nullptr) {
        if (error.empty()) {
            error = "dlcv_infer.dll 加载失败: " + std::to_string(GetLastError());
        }
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
    if (modelIndex == -1) {
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
    if (stableIndex == -1) return false;
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
        if (modelIndex == -1) return false;
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
        if (modelIndex == -1) return false;
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

static bool RunDvstPoolReleaseCheck(const std::wstring& modelPath, const cv::Mat& image) {
    dlcv_infer_cpp_free_all_models_c();
    std::string baselineFingerprint;
    std::string error;

    const int firstIndex = LoadModel(modelPath);
    if (firstIndex == -1) return false;
    const bool firstInferOk = InferFingerprint(firstIndex, image, baselineFingerprint, error);
    const bool firstFreeOk = dlcv_infer_cpp_free_model_c(firstIndex) == 0;
    const dlcv_infer::flow::ModelPoolStats afterFirstRelease = dlcv_infer::flow::GetModelPoolStats();
    if (!firstInferOk || !firstFreeOk || afterFirstRelease.totalEntries != 0 ||
        afterFirstRelease.activeEntries != 0 || afterFirstRelease.idleEntries != 0) {
        std::cerr << "FAIL: dvst 首次加载、推理、释放或模型池清理失败: "
                  << (error.empty() ? "状态不符合预期" : error) << "\n";
        dlcv_infer_cpp_free_all_models_c();
        return false;
    }

    const int secondIndex = LoadModel(modelPath);
    if (secondIndex == -1) {
        dlcv_infer_cpp_free_all_models_c();
        return false;
    }
    const dlcv_infer::flow::ModelPoolStats afterReload = dlcv_infer::flow::GetModelPoolStats();
    std::string secondFingerprint;
    error.clear();
    const bool secondInferOk = InferFingerprint(secondIndex, image, secondFingerprint, error)
        && secondFingerprint == baselineFingerprint;
    const bool secondFreeOk = dlcv_infer_cpp_free_model_c(secondIndex) == 0;
    const dlcv_infer::flow::ModelPoolStats afterSecondRelease = dlcv_infer::flow::GetModelPoolStats();
    dlcv_infer_cpp_free_all_models_c();
    if (!secondInferOk || !secondFreeOk || afterReload.totalEntries == 0 ||
        afterReload.activeEntries != afterReload.totalEntries || afterReload.idleEntries != 0 ||
        afterSecondRelease.totalEntries != 0 || afterSecondRelease.activeEntries != 0 ||
        afterSecondRelease.idleEntries != 0) {
        std::cerr << "FAIL: dvst 释放后重新加载或模型池统计失败: "
                  << (error.empty() ? "状态不符合预期" : error) << "\n";
        return false;
    }

    std::cout << "PASS: dvst 释放后重新加载成功且没有空闲模型池项\n";
    return true;
}

static bool RunModelPoolGenerationCheck(const std::wstring& modelPath, const cv::Mat& image) {
    dlcv_infer_cpp_free_all_models_c();
    std::string firstFingerprint;
    std::string error;
    const int oldIndex = LoadModel(modelPath);
    if (oldIndex == -1 || !InferFingerprint(oldIndex, image, firstFingerprint, error)) {
        dlcv_infer_cpp_free_all_models_c();
        return false;
    }

    dlcv_infer::NativeApi::FreeAllModels();
    const dlcv_infer::flow::ModelPoolStats afterNativeClear = dlcv_infer::flow::GetModelPoolStats();
    if (afterNativeClear.totalEntries != 0 || afterNativeClear.activeEntries != 0 ||
        afterNativeClear.idleEntries != 0) {
        std::cerr << "FAIL: NativeApi::FreeAllModels 未清空流程模型池\n";
        dlcv_infer_cpp_free_all_models_c();
        return false;
    }

    const int newIndex = LoadModel(modelPath);
    if (newIndex == -1) {
        dlcv_infer_cpp_free_all_models_c();
        return false;
    }
    const dlcv_infer::flow::ModelPoolStats beforeOldRelease = dlcv_infer::flow::GetModelPoolStats();
    // NativeApi 全量释放已同步清空 C 表，旧索引必须拒绝查询和再次释放。
    const bool oldIndexReleased = CheckCapiIndexReleased(oldIndex);
    const dlcv_infer::flow::ModelPoolStats afterOldRelease = dlcv_infer::flow::GetModelPoolStats();
    const bool generationOk = oldIndexReleased
        && beforeOldRelease.totalEntries > 0
        && beforeOldRelease.activeEntries == beforeOldRelease.totalEntries
        && beforeOldRelease.idleEntries == 0
        && afterOldRelease.totalEntries == beforeOldRelease.totalEntries
        && afterOldRelease.activeEntries == beforeOldRelease.activeEntries
        && afterOldRelease.idleEntries == 0;

    std::string secondFingerprint;
    error.clear();
    const bool inferOk = InferFingerprint(newIndex, image, secondFingerprint, error)
        && secondFingerprint == firstFingerprint;
    const bool newReleaseOk = dlcv_infer_cpp_free_model_c(newIndex) == 0;
    dlcv_infer_cpp_free_all_models_c();
    if (!generationOk || !inferOk || !newReleaseOk) {
        std::cerr << "FAIL: Clear 后旧索引释放或新模型持有检查失败: "
                  << "old_index_released=" << oldIndexReleased
                  << ", pool_unchanged=" << generationOk
                  << ", infer_ok=" << inferOk
                  << ", new_release_ok=" << newReleaseOk << "\n";
        return false;
    }

    std::cout << "PASS: Clear 前后的同键模型身份互不影响\n";
    return true;
}

static bool RunFreeAllDuringInferenceCheck(const std::wstring& modelPath, const cv::Mat& image) {
    dlcv_infer_cpp_free_all_models_c();
    const int modelIndex = LoadModel(modelPath);
    if (modelIndex == -1) return false;

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
    if (dvtIndex == -1) return false;
    ok = RunConcurrentModelInfoAndInference(
        "dvt 同一实例推理与模型信息并发", dvtIndex, dvtImage, 4, 10) && ok;
    const bool dvtReleaseOk = dlcv_infer_cpp_free_model_c(dvtIndex) == 0;
    if (!dvtReleaseOk) std::cerr << "FAIL: dvt 并发验证后释放失败\n";
    ok = dvtReleaseOk && ok;

    const int dvstIndex = LoadModel(dvstPath);
    if (dvstIndex == -1) return false;
    ok = RunConcurrentModelInfoAndInference(
        "dvst 同一实例推理与模型信息并发", dvstIndex, dvstImage, 4, 10) && ok;
    const bool dvstReleaseOk = dlcv_infer_cpp_free_model_c(dvstIndex) == 0;
    if (!dvstReleaseOk) std::cerr << "FAIL: dvst 并发验证后释放失败\n";
    return dvstReleaseOk && ok;
}

int main(int argc, char** argv) {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);
    if (argc == 2 && std::strcmp(argv[1], "--shared-index-rules-selftest") == 0) {
        const bool noModuleOk = RunUnknownIndexWithoutModuleCheck();
        const int resolverResult = dlcv_test::RunSharedIndexResolverSelfTest();
        const int modelIndexResult = dlcv_test::RunFlowModelIndexRulesSelfTest();
        const bool passed = resolverResult == 0 && modelIndexResult == 0 && noModuleOk;
        std::cout << (passed
            ? "PASS: 共享索引选择、严格流程 model_index、归档遗留 index 清理及 C ABI 缺失模型自测通过\n"
            : "FAIL: 共享索引与 model_index 规则自测失败\n");
        return passed ? 0 : 1;
    }

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

    const std::wstring dvtPath = L"Y:\\测试模型\\猫狗-分类_PLUS_s.dvt";
    const std::wstring dvtImagePath = L"Y:\\测试模型\\猫狗-狗.jpg";
    const std::wstring dvstPath = L"Y:\\测试模型\\AOI-元件提取_PLUS_s.dvst";
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
    if (argc == 2 && std::strcmp(argv[1], "--dvst-pool-release") == 0) {
        return RunDvstPoolReleaseCheck(dvstPath, dvstImage) ? 0 : 1;
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
    ok = RunRepeatedCapiReferenceCheck(dvtPath, dvtImage) && ok;
    ok = RunConcurrentCapiReferenceReleaseCheck(dvtPath, dvtImage) && ok;
    ok = RunNativeIndexRangeCheck("普通模型", dvtPath, dvtImage) && ok;
    ok = RunNativeIndexRangeCheck("流程模型", dvstPath, dvstImage) && ok;
    ok = RunCapiFreeAllEntryPointsCheck(dvtPath, dvtImage, 1) && ok;
    ok = RunCapiFreeAllEntryPointsCheck(dvstPath, dvstImage, 2) && ok;
    ok = RunNativeLoadLifecycleCheck() && ok;
    ok = RunNativeJsonStructuredRecoveryCheck("普通模型", dvtPath, dvtImage) && ok;
    ok = RunNativeJsonStructuredRecoveryCheck("流程模型", dvstPath, dvstImage) && ok;
    ok = RunExternalSharedIndexRecoveryCheck("普通模型", dvtPath, dvtImage) && ok;
    ok = RunExternalSharedIndexRecoveryCheck("流程模型", dvstPath, dvstImage) && ok;
    ok = RunNativeCompatibilityCheck(dvtPath, dvtImage) && ok;
    ok = RunAllCompatibilityFlowChecks() && ok;
    ok = RunModelPoolGenerationCheck(dvstPath, dvstImage) && ok;
    ok = RunFreeAllDuringInferenceCheck(dvstPath, dvstImage) && ok;

    ok = RunModelInfoConcurrencyChecks(dvtPath, dvtImage, dvstPath, dvstImage) && ok;

    dlcv_infer_cpp_free_all_models_c();
    std::cout << (ok ? "\nTest PASSED\n" : "\nTest FAILED\n");
    return ok ? 0 : 1;
}
