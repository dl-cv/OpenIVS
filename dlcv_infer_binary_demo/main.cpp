// 只包含一个 DLCV 公开头文件；读图使用 Windows WIC。
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <chrono>
#include <climits>
#include <filesystem>
#include <iomanip>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <type_traits>
#include <vector>

#include "dlcv_infer_c_api.h"

namespace {

std::string ToUtf8(const std::wstring& text) {
    const int size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (!size) throw std::runtime_error("路径编码转换失败");
    std::string result(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, result.data(), size, nullptr, nullptr);
    result.pop_back();
    return result;
}

std::string LocalToUtf8(const char* text) {
    if (!text) return {};
    const int size = MultiByteToWideChar(CP_ACP, 0, text, -1, nullptr, 0);
    if (!size) throw std::runtime_error("结果编码转换失败");
    std::wstring wide(size, L'\0');
    MultiByteToWideChar(CP_ACP, 0, text, -1, wide.data(), size);
    wide.pop_back();
    return ToUtf8(wide);
}

std::string ModelPathAnsi(const wchar_t* path) {
    // 现有 C ABI 的模型路径约定为本地 ANSI，不能直接传 UTF-8。
    if (GetACP() == CP_UTF8) return ToUtf8(path);
    BOOL replaced = FALSE;
    const int size = WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, path, -1,
        nullptr, 0, nullptr, &replaced);
    if (!size || replaced) throw std::runtime_error("模型路径包含本地代码页无法表示的字符");
    std::string result(size, '\0');
    WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, path, -1,
        result.data(), size, nullptr, &replaced);
    if (replaced) throw std::runtime_error("模型路径包含本地代码页无法表示的字符");
    result.pop_back();
    return result;
}

void CheckHr(HRESULT value, const char* operation) {
    if (FAILED(value)) {
        throw std::runtime_error(std::string(operation) + "，HRESULT=" +
            std::to_string(static_cast<unsigned long>(value)));
    }
}

struct ComSession {
    ComSession() { CheckHr(CoInitializeEx(nullptr, COINIT_MULTITHREADED), "初始化 WIC 失败"); }
    ~ComSession() { CoUninitialize(); }
    ComSession(const ComSession&) = delete;
    ComSession& operator=(const ComSession&) = delete;
};

struct Image {
    std::vector<unsigned char> pixels;
    int width = 0;
    int height = 0;
};

Image ReadRgb(const wchar_t* path) {
    using Microsoft::WRL::ComPtr;
    ComPtr<IWICImagingFactory> factory;
    CheckHr(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(factory.GetAddressOf())), "创建读图组件失败");
    ComPtr<IWICBitmapDecoder> decoder;
    CheckHr(factory->CreateDecoderFromFilename(path, nullptr, GENERIC_READ,
        WICDecodeMetadataCacheOnLoad, decoder.GetAddressOf()), "读取图片失败");
    ComPtr<IWICBitmapFrameDecode> frame;
    CheckHr(decoder->GetFrame(0, frame.GetAddressOf()), "读取图片帧失败");
    ComPtr<IWICFormatConverter> converter;
    CheckHr(factory->CreateFormatConverter(converter.GetAddressOf()), "创建 RGB 转换器失败");
    CheckHr(converter->Initialize(frame.Get(), GUID_WICPixelFormat24bppRGB,
        WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom), "转换 RGB 失败");
    UINT width = 0, height = 0;
    CheckHr(converter->GetSize(&width, &height), "读取图片尺寸失败");
    const auto bytes = static_cast<unsigned long long>(width) * height * 3;
    if (!width || !height || width > INT_MAX || height > INT_MAX || bytes > UINT_MAX) {
        throw std::runtime_error("图片尺寸超出 WIC 缓冲区范围");
    }
    Image image;
    image.width = static_cast<int>(width);
    image.height = static_cast<int>(height);
    image.pixels.resize(static_cast<size_t>(bytes));
    CheckHr(converter->CopyPixels(nullptr, width * 3, static_cast<UINT>(bytes),
        image.pixels.data()), "读取 RGB 像素失败");
    return image;
}

class Api {
public:
    explicit Api(const std::filesystem::path& path)
        : module_(LoadLibraryExW(std::filesystem::absolute(path).c_str(), nullptr,
              LOAD_WITH_ALTERED_SEARCH_PATH), &FreeLibrary) {
        if (!module_) {
            throw std::runtime_error("加载 DLL 失败，Windows 错误码=" + std::to_string(GetLastError()) +
                "；检查指定 DLL、opencv_world4100.dll 和 VC++ 运行库");
        }
        loadModel = Resolve<decltype(loadModel)>("dlcv_infer_cpp_load_model_c");
        lastError = Resolve<decltype(lastError)>("dlcv_infer_cpp_get_last_error_c");
        freeModel = Resolve<decltype(freeModel)>("dlcv_infer_cpp_free_model_c");
        infer = Resolve<decltype(infer)>("dlcv_infer_cpp_infer_with_params_c");
        freeResult = Resolve<decltype(freeResult)>("dlcv_infer_cpp_free_model_result_c");
    }

    decltype(&dlcv_infer_cpp_load_model_c) loadModel = nullptr;
    decltype(&dlcv_infer_cpp_get_last_error_c) lastError = nullptr;
    decltype(&dlcv_infer_cpp_free_model_c) freeModel = nullptr;
    decltype(&dlcv_infer_cpp_infer_with_params_c) infer = nullptr;
    decltype(&dlcv_infer_cpp_free_model_result_c) freeResult = nullptr;

private:
    template<class T> T Resolve(const char* name) {
        const auto address = GetProcAddress(module_.get(), name);
        if (!address) throw std::runtime_error(std::string("DLL 缺少接口: ") + name);
        return reinterpret_cast<T>(address);
    }

    std::unique_ptr<std::remove_pointer_t<HMODULE>, decltype(&FreeLibrary)> module_;
};

struct ModelOwner {
    Api& api;
    int index;
    ModelOwner(Api& value, const std::string& path) : api(value), index(api.loadModel(path.c_str(), 0)) {
        if (index < 0) {
            const char* error = api.lastError();
            throw std::runtime_error(std::string("模型加载失败: ") + (error ? error : "未知错误"));
        }
    }
    ~ModelOwner() { api.freeModel(index); }
    ModelOwner(const ModelOwner&) = delete;
    ModelOwner& operator=(const ModelOwner&) = delete;
};

struct ResultOwner {
    Api& api;
    DlcvCResult value;
    ResultOwner(Api& apiValue, int index, const DlcvCImageList& images)
        : api(apiValue), value(api.infer(index, &images, "{\"threshold\":0.5}")) {}
    ~ResultOwner() { api.freeResult(&value); }
    ResultOwner(const ResultOwner&) = delete;
    ResultOwner& operator=(const ResultOwner&) = delete;
};

void PrintResult(const DlcvCResult& result) {
    size_t total = 0;
    for (int i = 0; i < result.n; ++i) total += result.sample_results[i].n;
    std::cout << "推理结果: " << total << "个\n\n";
    size_t index = 0;
    for (int i = 0; i < result.n; ++i) {
        const auto& sample = result.sample_results[i];
        for (int j = 0; j < sample.n; ++j) {
            const auto& obj = sample.results[j];
            const std::string name = LocalToUtf8(obj.category_name);
            std::cout << "[" << ++index << "] " << std::left << std::setw(16)
                      << (name.empty() ? "unknown" : name) << std::right
                      << "score=" << std::fixed << std::setprecision(2) << obj.score;
            if (obj.with_bbox) {
                std::cout << "  bbox=(" << std::setprecision(1) << obj.x << ", " << obj.y
                          << ", " << obj.w << ", " << obj.h << ")";
            }
            if (obj.with_angle) std::cout << "  angle=" << std::setprecision(1) << obj.angle;
            std::cout << "  area=" << std::setprecision(1) << obj.area << '\n';
        }
    }
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    SetConsoleOutputCP(CP_UTF8);
    if (argc == 2 && std::wstring(argv[1]) == L"--help") {
        std::cout << "用法: dlcv_infer_binary_demo.exe <模型> <图片> [DLL路径]\n"
                     "GPU=0，threshold=0.5，批量大小=1；默认 DLL 位于 EXE 同目录。\n";
        return 0;
    }
    if (argc < 3 || argc > 4) {
        std::cerr << "用法: dlcv_infer_binary_demo.exe <模型> <图片> [DLL路径]\n";
        return 2;
    }
    try {
        std::filesystem::path dllPath;
        if (argc == 4) {
            dllPath = argv[3];
        } else {
            std::wstring exePath(32768, L'\0');
            const DWORD length = GetModuleFileNameW(nullptr, exePath.data(), static_cast<DWORD>(exePath.size()));
            if (!length || length >= exePath.size()) throw std::runtime_error("读取 EXE 路径失败");
            exePath.resize(length);
            dllPath = std::filesystem::path(exePath).parent_path() / L"dlcv_infer_cpp.dll";
        }
        const ComSession com;
        const Image image = ReadRgb(argv[2]);
        Api api(dllPath);
        const ModelOwner model(api, ModelPathAnsi(argv[1]));
        DlcvCImage input{reinterpret_cast<long long>(image.pixels.data()), image.height, image.width, 3};
        const DlcvCImageList images{&input, 1};
        std::cout << "模型: " << ToUtf8(argv[1]) << "\n图片: " << ToUtf8(argv[2])
                  << "\n批量大小: 1\nthreshold: 0.50\n";
        const auto begin = std::chrono::steady_clock::now();
        const ResultOwner result(api, model.index, images);
        const double elapsedMs = std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - begin).count();
        if (result.value.code != 0) {
            throw std::runtime_error(std::string("推理失败: ") +
                (result.value.message ? result.value.message : "未知错误"));
        }
        std::cout << "推理时间: " << std::fixed << std::setprecision(2) << elapsedMs << "ms\n";
        PrintResult(result.value);
    } catch (const std::exception& ex) {
        std::cerr << "执行失败: " << ex.what() << '\n';
        return 1;
    }
    return 0;
}
