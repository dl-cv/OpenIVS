#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include <opencv2/imgproc.hpp>

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <limits>
#include <memory>
#include <stdexcept>

namespace dlcv_infer {
namespace flow {
namespace {

// C ABI declaration copyright: 2023 siiky, 2023 Axel Waggershauser.
// Private Windows x64 C ABI subset of ZXing-C++ v3.1.1 ZXingC.h (Apache-2.0).
// No STL objects cross the DLL boundary. See third_party/zxing-cpp/README.md.
struct BarcodePoint { int x, y; };
struct BarcodePosition { BarcodePoint topLeft, topRight, bottomRight, bottomLeft; };
static_assert(sizeof(BarcodePosition) == 32, "ZXing C ABI position layout");

class BarcodeApi final {
    struct LibraryDeleter { void operator()(HINSTANCE__* p) const { if (p) FreeLibrary(p); } };
    std::unique_ptr<HINSTANCE__, LibraryDeleter> library;

    template<class T> T Symbol(const char* name) {
        auto symbol = GetProcAddress(library.get(), name);
        if (!symbol) throw std::runtime_error(std::string("ZXing.dll missing export: ") + name);
        return reinterpret_cast<T>(symbol);
    }
    static HMODULE Load() {
        HMODULE owner = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(&Load), &owner))
            throw std::runtime_error("Cannot locate barcode module");
        std::wstring path(32768, L'\0');
        const auto length = GetModuleFileNameW(owner, &path[0], static_cast<DWORD>(path.size()));
        if (!length || length >= path.size()) throw std::runtime_error("Cannot locate barcode module path");
        path.resize(length);
        path.resize(path.find_last_of(L"\\/") + 1);
        path += L"ZXing.dll";
        HMODULE handle = LoadLibraryExW(path.c_str(), nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!handle) throw std::runtime_error("Cannot load adjacent ZXing.dll (restore ZXingCpp 0.5.3 and rebuild), Win32=" + std::to_string(GetLastError()));
        return handle;
    }
public:
#define ZXING_FN(name, result, ...) using name##Fn = result (__cdecl*)(__VA_ARGS__); name##Fn name = Symbol<name##Fn>("ZXing_" #name)
    ZXING_FN(free, void, void*);
    ZXING_FN(LastErrorMsg, char*);
    ZXING_FN(ImageView_new, void*, const uint8_t*, int, int, int, int, int);
    ZXING_FN(ImageView_delete, void, void*);
    ZXING_FN(ReaderOptions_new, void*);
    ZXING_FN(ReaderOptions_delete, void, void*);
    ZXING_FN(ReaderOptions_setTryRotate, void, void*, bool);
    ZXING_FN(ReaderOptions_setTryInvert, void, void*, bool);
    ZXING_FN(ReaderOptions_setTextMode, void, void*, int);
    ZXING_FN(ReaderOptions_setFormats, void, void*, const int*, int);
    ZXING_FN(BarcodeFormatsFromString, int*, const char*, int*);
    ZXING_FN(BarcodeFormatToString, char*, int);
    ZXING_FN(ReadBarcodes, void*, const void*, const void*);
    ZXING_FN(Barcodes_size, int, const void*);
    ZXING_FN(Barcodes_at, const void*, const void*, int);
    ZXING_FN(Barcodes_delete, void, void*);
    ZXING_FN(Barcode_text, char*, const void*);
    ZXING_FN(Barcode_format, int, const void*);
    ZXING_FN(Barcode_position, BarcodePosition, const void*);
#undef ZXING_FN
    BarcodeApi() : library(Load()) {}
    using Owned = std::unique_ptr<void, void (__cdecl*)(void*)>;
    std::string Text(char* value) {
        Owned owned(value, free);
        return value ? std::string(value) : std::string();
    }
    void Check(const void* value) {
        if (!value) throw std::runtime_error("ZXing: " + Text(LastErrorMsg()));
    }
};

bool BarcodeOption(const Json& properties, const char* key) {
    if (!properties.contains(key)) return true;
    if (!properties.at(key).is_boolean()) throw std::invalid_argument(std::string(key) + " must be a boolean");
    return properties.at(key).get<bool>();
}

class BarcodeReaderModule final : public BaseModule {
public:
    using BaseModule::BaseModule;
    ModuleIO Process(const std::vector<ModuleImage>& images, const Json&) override {
        std::string formats;
        if (Properties.contains("formats")) {
            if (!Properties.at("formats").is_string()) throw std::invalid_argument("formats must be a comma-separated string");
            formats = Properties.at("formats").get<std::string>();
        }
        const bool rotate = BarcodeOption(Properties, "try_rotate"), invert = BarcodeOption(Properties, "try_invert");
        Json results = Json::array();
        if (images.empty()) return ModuleIO(images, results);
        static BarcodeApi api;
        BarcodeApi::Owned options(api.ReaderOptions_new(), api.ReaderOptions_delete);
        api.Check(options.get());
        api.ReaderOptions_setTryRotate(options.get(), rotate);
        api.ReaderOptions_setTryInvert(options.get(), invert);
        api.ReaderOptions_setTextMode(options.get(), 2); // HRI
        if (formats.find_first_not_of(" \t\r\n") != std::string::npos) {
            int count = 0;
            BarcodeApi::Owned parsed(api.BarcodeFormatsFromString(formats.c_str(), &count), api.free);
            api.Check(parsed.get());
            api.ReaderOptions_setFormats(options.get(), static_cast<const int*>(parsed.get()), count);
        }
        for (size_t i = 0; i < images.size(); ++i) {
            const auto& image = images[i].GetImage();
            if (image.empty()) throw std::invalid_argument("Barcode reader requires a non-empty image");
            const int channels = image.channels();
            if (image.depth() != CV_8U || (channels != 1 && channels != 3 && channels != 4))
                throw std::invalid_argument("Barcode reader requires uint8 gray, RGB or RGBA images");
            if (image.step > static_cast<size_t>(std::numeric_limits<int>::max())) throw std::invalid_argument("Barcode image stride is too large");
            const int format = channels == 1 ? 0x01000000 : channels == 3 ? 0x03000102 : 0x04000102;
            BarcodeApi::Owned view(api.ImageView_new(image.data, image.cols, image.rows, format, static_cast<int>(image.step), 0), api.ImageView_delete);
            api.Check(view.get());
            BarcodeApi::Owned codes(api.ReadBarcodes(view.get(), options.get()), api.Barcodes_delete);
            api.Check(codes.get());
            BarcodeApi::Owned retryView(nullptr, api.ImageView_delete);
            cv::Mat gray, enhanced;
            if (api.Barcodes_size(codes.get()) == 0) {
                if (channels == 1) {
                    cv::equalizeHist(image, enhanced);
                } else {
                    cv::cvtColor(image, gray, channels == 3 ? cv::COLOR_RGB2GRAY : cv::COLOR_RGBA2GRAY);
                    cv::equalizeHist(gray, enhanced);
                }
                retryView.reset(api.ImageView_new(enhanced.data, enhanced.cols, enhanced.rows, 0x01000000,
                    static_cast<int>(enhanced.step), 0));
                api.Check(retryView.get());
                codes.reset(api.ReadBarcodes(retryView.get(), options.get()));
                api.Check(codes.get());
            }
            Json detections = Json::array();
            for (int j = 0; j < api.Barcodes_size(codes.get()); ++j) {
                const auto* code = api.Barcodes_at(codes.get(), j);
                api.Check(code);
                const auto pos = api.Barcode_position(code);
                const std::array<BarcodePoint, 4> points = {pos.topLeft, pos.topRight, pos.bottomRight, pos.bottomLeft};
                int x = points[0].x, y = points[0].y, xmax = x, ymax = y;
                double area = 0;
                Json line = Json::array();
                for (size_t k = 0; k < 4; ++k) {
                    const auto& p = points[k]; const auto& next = points[(k + 1) % 4];
                    x = std::min(x, p.x); y = std::min(y, p.y);
                    xmax = std::max(xmax, p.x); ymax = std::max(ymax, p.y);
                    area += static_cast<double>(p.x) * next.y - static_cast<double>(p.y) * next.x;
                    line.push_back(Json::array({p.x, p.y}));
                }
                line.push_back(line[0]);
                detections.push_back(Json{{"category_id", 0}, {"category_name", api.Text(api.Barcode_text(code))}, {"score", 1.0},
                    {"bbox", Json::array({x, y, xmax - x, ymax - y})}, {"area", std::abs(area) / 2},
                    {"with_bbox", true}, {"with_angle", false}, {"angle", -100}, {"with_mask", false},
                    {"extra_info", {{"barcode_format", api.Text(api.BarcodeFormatToString(api.Barcode_format(code)))}, {"polyline", line}}}});
            }
            auto entry = images[i].ToMeta();
            entry["type"] = "local"; entry["index"] = i; entry["sample_results"] = std::move(detections);
            results.push_back(std::move(entry));
        }
        return ModuleIO(images, std::move(results));
    }
};
DLCV_FLOW_REGISTER_MODULE("features/barcode_reader", BarcodeReaderModule)
} // namespace
} // namespace flow
} // namespace dlcv_infer
