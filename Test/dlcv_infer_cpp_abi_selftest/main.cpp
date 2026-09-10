#include <Windows.h>

#include <cstdint>
#include <iostream>
#include <string>

#include "dlcv_infer.h"

namespace {

using json = nlohmann::json;

std::string WideToUtf8(const std::wstring& value) {
    if (value.empty()) {
        return {};
    }
    const int size = WideCharToMultiByte(
        CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (size <= 0) {
        return {};
    }
    std::string result(static_cast<std::size_t>(size), '\0');
    WideCharToMultiByte(
        CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), result.data(), size, nullptr, nullptr);
    return result;
}

std::string GetLoadedDllPath() {
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&dlcv_infer::GetModelAbiValue),
            &module)) {
        return {};
    }

    std::wstring path(32768, L'\0');
    const DWORD length = GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size()) {
        return {};
    }
    path.resize(length);
    return WideToUtf8(path);
}

std::uint64_t ReadDll(dlcv_infer::ModelAbiValue value) {
    return dlcv_infer::GetModelAbiValue(value);
}

}  // namespace

int main() {
    SetConsoleOutputCP(CP_UTF8);

    const json header = {
        {"layout_version", dlcv_infer::detail::ModelAbiLayoutVersion},
        {"build_configuration", dlcv_infer::detail::ModelAbiBuildConfiguration},
        {"iterator_debug_level", dlcv_infer::detail::ModelAbiIteratorDebugLevel},
        {"model_size", sizeof(dlcv_infer::Model)},
        {"model_alignment", alignof(dlcv_infer::Model)},
        {"object_result_size", sizeof(dlcv_infer::ObjectResult)},
        {"sample_result_size", sizeof(dlcv_infer::SampleResult)},
        {"result_size", sizeof(dlcv_infer::Result)},
        {"string_size", sizeof(std::string)},
        {"vector_size", sizeof(std::vector<double>)},
        {"json_size", sizeof(dlcv_infer::json)},
        {"mat_size", sizeof(cv::Mat)},
        {"mutex_size", sizeof(std::mutex)},
        {"shared_mutex_size", sizeof(std::shared_mutex)}
    };

    const json dll = {
        {"layout_version", ReadDll(dlcv_infer::ModelAbiValue::LayoutVersion)},
        {"build_configuration", ReadDll(dlcv_infer::ModelAbiValue::BuildConfiguration)},
        {"iterator_debug_level", ReadDll(dlcv_infer::ModelAbiValue::IteratorDebugLevel)},
        {"model_size", ReadDll(dlcv_infer::ModelAbiValue::ModelSize)},
        {"model_alignment", ReadDll(dlcv_infer::ModelAbiValue::ModelAlignment)},
        {"object_result_size", ReadDll(dlcv_infer::ModelAbiValue::ObjectResultSize)},
        {"sample_result_size", ReadDll(dlcv_infer::ModelAbiValue::SampleResultSize)},
        {"result_size", ReadDll(dlcv_infer::ModelAbiValue::ResultSize)},
        {"string_size", ReadDll(dlcv_infer::ModelAbiValue::StringSize)},
        {"vector_size", ReadDll(dlcv_infer::ModelAbiValue::VectorSize)},
        {"json_size", ReadDll(dlcv_infer::ModelAbiValue::JsonSize)},
        {"mat_size", ReadDll(dlcv_infer::ModelAbiValue::MatSize)},
        {"mutex_size", ReadDll(dlcv_infer::ModelAbiValue::MutexSize)},
        {"shared_mutex_size", ReadDll(dlcv_infer::ModelAbiValue::SharedMutexSize)}
    };

    const std::string dllPath = GetLoadedDllPath();
    const bool passed = !dllPath.empty() && header == dll;
    const json output = {
        {"test", "dlcv_infer_cpp_abi_selftest"},
        {"passed", passed},
        {"loaded_dll_path", dllPath},
        {"header", header},
        {"dll", dll}
    };

    std::cout << output.dump(2) << "\n";
    return passed ? 0 : 1;
}
