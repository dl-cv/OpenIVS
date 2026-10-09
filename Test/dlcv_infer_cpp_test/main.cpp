#include <windows.h>
#include <TlHelp32.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cctype>
#include <condition_variable>
#include <climits>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <exception>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iomanip>
#include <iostream>
#include <iterator>
#include <limits>
#include <memory>
#include <map>
#include <mutex>
#include <numeric>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <tuple>
#include <type_traits>
#include <utility>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "../../dlcv_infer_cpp/ImageInputUtils.h"
#include "../../dlcv_infer_cpp/flow/FlowGraphModel.h"
#include "../../dlcv_infer_cpp/flow/ModuleRegistry.h"
#include "../../dlcv_infer_cpp/flow/modules/ModelModules.h"
#include "../../dlcv_infer_cpp/flow/utils/MaskRleUtils.h"
#include "../DvsTempArtifactMonitor.h"
#include "dlcv_infer.h"
#include "RegionMaskSelfTest.h"
#include "MaskAreaSelfTest.h"
#include "SlidingMergeSelfTest.h"
#include "../common/NativeSharedIndexTestHelper.h"

namespace {
using json = nlohmann::json;
using Clock = std::chrono::steady_clock;

static_assert(std::is_constructible_v<dlcv_infer::Model, const char*>);
static_assert(std::is_constructible_v<dlcv_infer::Model, const wchar_t*>);
static_assert(std::is_constructible_v<dlcv_infer::Model, const std::string&>);
static_assert(std::is_constructible_v<dlcv_infer::Model, const std::wstring&>);
static_assert(std::is_constructible_v<dlcv_infer::Model, const std::string&, int>);
static_assert(std::is_constructible_v<dlcv_infer::Model, const std::wstring&, int>);

std::string Safe(const std::string& s) {
    std::string out = s.empty() ? "-" : s;
    for (auto& ch : out) {
        if (ch == '|') ch = '/';
        if (ch == '\n' || ch == '\r') ch = ' ';
    }
    return out;
}

std::string ToFixed(double v, int precision) {
    std::ostringstream oss;
    oss << std::fixed << std::setprecision(precision) << v;
    return oss.str();
}

std::string WideToUtf8(const std::wstring& w) {
    if (w.empty()) return {};
    int bytes = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()), nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return {};
    std::string out(static_cast<size_t>(bytes), '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()), out.data(), bytes, nullptr, nullptr);
    return out;
}

void WriteUtf8(std::ostream& stream, const std::string& text) {
#ifdef _WIN32
    stream << dlcv_infer::convertUtf8ToGbk(text);
#else
    stream << text;
#endif
}

void PrintUtf8(const std::string& text) {
    WriteUtf8(std::cout, text);
}

void PrintUtf8Line(const std::string& text) {
    WriteUtf8(std::cout, text);
    std::cout << "\n";
}

void PrintUtf8ErrorLine(const std::string& text) {
    WriteUtf8(std::cerr, text);
    std::cerr << "\n";
}

std::wstring Utf8ToWide(const std::string& s) {
    if (s.empty()) return {};
    int chars = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), nullptr, 0);
    if (chars <= 0) return {};
    std::wstring out(static_cast<size_t>(chars), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), out.data(), chars);
    return out;
}

void DisposeResultMasks(dlcv_infer::Result& out) {
    for (auto& sr : out.sampleResults) {
        for (auto& o : sr.results) {
            if (!o.mask.empty()) o.mask.release();
        }
    }
}

cv::Mat ReadImageRgb(const std::wstring& imagePath) {
    FILE* file = nullptr;
    if (_wfopen_s(&file, imagePath.c_str(), L"rb") != 0 || file == nullptr) return {};
    std::unique_ptr<FILE, decltype(&fclose)> holder(file, &fclose);
    if (fseek(file, 0, SEEK_END) != 0) return {};
    const long size = ftell(file);
    if (size <= 0 || fseek(file, 0, SEEK_SET) != 0) return {};
    std::vector<unsigned char> data(static_cast<size_t>(size));
    if (fread(data.data(), 1, data.size(), file) != data.size()) return {};

    cv::Mat decoded = cv::imdecode(data, cv::IMREAD_COLOR);
    if (decoded.empty()) return {};
    cv::Mat rgb;
    cv::cvtColor(decoded, rgb, cv::COLOR_BGR2RGB);
    return rgb;
}

void AppendSignatureText(std::ostringstream& oss, const std::string& value) {
    oss << value.size() << ":" << value << ";";
}

std::uint64_t CalculateMaskDigest(const cv::Mat& mask) {
    constexpr std::uint64_t kOffsetBasis = 1469598103934665603ULL;
    constexpr std::uint64_t kPrime = 1099511628211ULL;
    std::uint64_t digest = kOffsetBasis;
    if (mask.empty()) return digest;
    const size_t bytesPerRow = static_cast<size_t>(mask.cols) * mask.elemSize();
    for (int row = 0; row < mask.rows; ++row) {
        const unsigned char* data = mask.ptr<unsigned char>(row);
        for (size_t column = 0; column < bytesPerRow; ++column) {
            digest ^= data[column];
            digest *= kPrime;
        }
    }
    return digest;
}

std::uint64_t CalculateTextDigest(const std::string& text) {
    constexpr std::uint64_t kOffsetBasis = 1469598103934665603ULL;
    constexpr std::uint64_t kPrime = 1099511628211ULL;
    std::uint64_t digest = kOffsetBasis;
    for (const unsigned char ch : text) {
        digest ^= ch;
        digest *= kPrime;
    }
    return digest;
}

std::string ToHexDigest(std::uint64_t value) {
    std::ostringstream oss;
    oss << std::hex << std::setw(16) << std::setfill('0') << value;
    return oss.str();
}

std::string CanonicalJsonDump(const json& value) {
    return value.dump();
}

void AppendJsonExtraSignature(std::ostringstream& oss, const json& value) {
    if (value.is_array()) {
        oss << "[" << value.size() << "]";
        for (const auto& item : value) AppendJsonExtraSignature(oss, item);
        return;
    }
    if (!value.is_object()) return;

    const bool isResult = value.contains("category_id") || value.contains("category_name") || value.contains("score");
    if (isResult) {
        for (const char* key : {"extra_info", "extraInfo", "metadata"}) {
            oss << key << "=";
            if (value.contains(key)) {
                AppendSignatureText(oss, value.at(key).dump());
            } else {
                AppendSignatureText(oss, "<missing>");
            }
        }
    }
    for (auto it = value.begin(); it != value.end(); ++it) {
        AppendJsonExtraSignature(oss, it.value());
    }
}

std::string BuildResultSignature(const dlcv_infer::Result& result, const json& jsonResult = json()) {
    std::ostringstream oss;
    oss << std::setprecision(std::numeric_limits<double>::max_digits10);
    oss << "sample_count=" << result.sampleResults.size() << ";";
    for (size_t sampleIndex = 0; sampleIndex < result.sampleResults.size(); sampleIndex++) {
        oss << "sample=" << sampleIndex << ",object_count=" << result.sampleResults[sampleIndex].results.size() << ";";
        const auto& objects = result.sampleResults[sampleIndex].results;
        for (size_t objectIndex = 0; objectIndex < objects.size(); objectIndex++) {
            const auto& object = objects[objectIndex];
            oss << "object=" << objectIndex
                << ",category_id=" << object.categoryId
                << ",category_name=";
            AppendSignatureText(oss, object.categoryName);
            oss << ",score=" << object.score
                << ",area=" << object.area
                << ",with_mask=" << object.withMask
                << ",with_bbox=" << object.withBbox
                << ",with_angle=" << object.withAngle
                << ",angle=" << object.angle
                << ",with_mean=" << object.withMean
                << ",foreground_mean=" << object.foregroundMean
                << ",background_mean=" << object.backgroundMean
                << ",bbox_count=" << object.bbox.size() << ",bbox=";
            for (size_t bboxIndex = 0; bboxIndex < object.bbox.size(); bboxIndex++) {
                oss << object.bbox[bboxIndex] << ",";
            }
            oss << ",mask_rows=" << object.mask.rows
                << ",mask_cols=" << object.mask.cols
                << ",mask_type=" << object.mask.type()
                << ",mask_digest=" << CalculateMaskDigest(object.mask) << ";";
        }
    }
    if (!jsonResult.is_null()) {
        oss << "json_extra=";
        AppendJsonExtraSignature(oss, jsonResult);
    }
    return oss.str();
}

int RunDefaultDeviceSelfTest(int argc, wchar_t* argv[]) {
    if (argc != 5) {
        PrintUtf8ErrorLine("用法：default-device-selftest <模型路径> <图片路径> <预期对象数>");
        return 2;
    }
    try {
        const cv::Mat image = ReadImageRgb(argv[3]);
        if (image.empty()) throw std::runtime_error("测试图片读取失败");
        const size_t expectedCount = std::stoull(WideToUtf8(argv[4]));
        const std::string localPath = dlcv_infer::convertWstringToString(argv[2]);
        std::string expectedSignature;
        for (int pathType = 0; pathType < 3; ++pathType) {
            std::unique_ptr<dlcv_infer::Model> model;
            if (pathType == 0) {
                model = std::make_unique<dlcv_infer::Model>(std::wstring(argv[2]), 0);
            } else if (pathType == 1) {
                model = std::make_unique<dlcv_infer::Model>(argv[2]);
            } else {
                model = std::make_unique<dlcv_infer::Model>(localPath.c_str());
            }
            const dlcv_infer::Result result = model->InferBatch({image});
            if (result.sampleResults.size() != 1 ||
                result.sampleResults.front().results.size() != expectedCount) {
                throw std::runtime_error("默认设备推理的图片数或对象数不符合预期");
            }
            const std::string signature = BuildResultSignature(result);
            if (pathType == 0) expectedSignature = signature;
            else if (signature != expectedSignature) {
                throw std::runtime_error("省略设备参数的结果与显式设备 0 不一致");
            }
        }
        std::cout << "DEFAULT_DEVICE_SELFTEST|PASS|device_id=0|path_types=2|object_count="
                  << expectedCount << std::endl;
        return 0;
    } catch (const std::exception& e) {
        PrintUtf8ErrorLine(e.what());
        return 1;
    }
}

int RunDvsRgbSelfTest(int argc, wchar_t* argv[]) {
    if (argc < 4) {
        PrintUtf8Line("Usage: dlcv_infer_cpp_test dvs-rgb-selftest <modelPath> <imagePath> [require-preserved-mask|require-polyline]");
        return 2;
    }

    const std::wstring modelPath = argv[2];
    const std::wstring imagePath = argv[3];
    PrintUtf8Line("==== C++ DVS RGB selftest ====");
    PrintUtf8Line("model: " + WideToUtf8(modelPath));
    PrintUtf8Line("image: " + WideToUtf8(imagePath));

    try {
        cv::Mat rgb = ReadImageRgb(imagePath);
        if (rgb.empty()) {
            PrintUtf8Line("selftest failed: image decode failed");
            return 1;
        }

        dlcv_infer::Model model(modelPath, 0);
        json params = {
            {"threshold", 0.5},
            {"with_mask", true},
            {"batch_size", 1}
        };
        dlcv_infer::Result result = model.InferBatch({ rgb }, params);
        std::cout << "signature: " << BuildResultSignature(result) << "\n";
        if (result.sampleResults.empty() || result.sampleResults.front().results.empty()) {
            PrintUtf8Line("selftest failed: DVS flow returned an empty result");
            return 1;
        }
        const std::wstring validationMode = argc >= 5 ? std::wstring(argv[4]) : std::wstring();
        const bool requirePreservedMask = validationMode == L"require-preserved-mask" ||
            validationMode == L"require-original-mask";
        const bool requirePolyline = validationMode == L"require-polyline";
        if (requirePolyline) {
            const json jsonResult = model.InferOneOutJson(rgb, params);
            std::cout << "json_signature: " << BuildResultSignature(result, jsonResult) << "\n";
            if (!jsonResult.is_array() || jsonResult.empty() || !jsonResult.front().is_object()) {
                PrintUtf8Line("selftest failed: JSON result is empty");
                DisposeResultMasks(result);
                return 1;
            }
            const json& object = jsonResult.front();
            const bool hasPolyline = object.contains("extra_info") && object.at("extra_info").is_object() &&
                object.at("extra_info").contains("polyline") && object.at("extra_info").at("polyline").is_array() &&
                object.at("extra_info").at("polyline").size() >= 2;
            const bool hasAxisAlignedBbox = object.contains("bbox") && object.at("bbox").is_array() &&
                object.at("bbox").size() == 4;
            const bool hasOldGeometry = object.contains("polygon") || object.contains("poly") ||
                object.contains("mask_array") || object.contains("mask_rle") || object.contains("polyline");
            if (!hasPolyline || !hasAxisAlignedBbox || hasOldGeometry || object.value("with_mask", true)) {
                PrintUtf8Line(std::string("selftest failed: poly_filter JSON contract mismatch: ") + object.dump());
                DisposeResultMasks(result);
                return 1;
            }
            PrintUtf8Line("poly_filter JSON polyline preserved");
        }
        if (requirePreservedMask) {
            const auto& object = result.sampleResults.front().results.front();
            if (!object.withMask || object.mask.empty()) {
                PrintUtf8Line("selftest failed: segmentation mask was not preserved after OCR");
                DisposeResultMasks(result);
                return 1;
            }
            if (!object.withBbox || object.bbox.size() < 4) {
                PrintUtf8Line("selftest failed: segmentation bbox was not preserved after OCR");
                DisposeResultMasks(result);
                return 1;
            }

            const double x = object.bbox[0];
            const double y = object.bbox[1];
            const double width = object.bbox[2];
            const double height = object.bbox[3];
            constexpr double tolerance = 1.0;
            if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(width) || !std::isfinite(height) ||
                width <= 0.0 || height <= 100.0 ||
                x < -tolerance || y < -tolerance ||
                x + width > rgb.cols + tolerance || y + height > rgb.rows + tolerance) {
                std::ostringstream message;
                message << "selftest failed: segmentation bbox is not in original-image coordinates, image="
                    << rgb.cols << "x" << rgb.rows << ", bbox=";
                for (size_t i = 0; i < object.bbox.size(); i++) {
                    if (i > 0) message << ",";
                    message << object.bbox[i];
                }
                PrintUtf8Line(message.str());
                DisposeResultMasks(result);
                return 1;
            }
            if (object.categoryName.empty()) {
                PrintUtf8Line("selftest failed: OCR text was not merged into the segmentation result");
                DisposeResultMasks(result);
                return 1;
            }
            if (!std::isfinite(object.score)) {
                PrintUtf8Line("selftest failed: preserved segmentation score is not finite");
                DisposeResultMasks(result);
                return 1;
            }

            const bool fullImageMask = object.mask.cols == rgb.cols && object.mask.rows == rgb.rows;
            std::ostringstream message;
            message << "preserved_result: image=" << rgb.cols << "x" << rgb.rows << ", bbox=";
            for (size_t i = 0; i < object.bbox.size(); i++) {
                if (i > 0) message << ",";
                message << object.bbox[i];
            }
            message << ", mask=" << object.mask.cols << "x" << object.mask.rows
                << ", mask_space=" << (fullImageMask ? "full-image" : "roi")
                << ", category_name=";
            PrintUtf8(message.str());
            std::cout << object.categoryName;
            std::ostringstream suffix;
            suffix << ", score=" << ToFixed(object.score, 4);
            PrintUtf8Line(suffix.str());
        }
        DisposeResultMasks(result);
        PrintUtf8Line("C++ DVS RGB selftest passed");
        return 0;
    } catch (const std::exception& ex) {
        PrintUtf8Line(std::string("selftest exception: ") + ex.what());
        return 1;
    }
}

bool HasSharedFlowSdkForSelfTest(const dlcv_infer::Model& model);
void VerifyCachedModelInvalidationForSelfTest(const std::wstring& path, int deviceId,
                                             bool isFlow, const cv::Mat& image);

int RunDvsMemoryLoadingSelfTest(int argc, wchar_t* argv[]) {
    if (argc < 4 || argc > 5) {
        PrintUtf8Line("用法: dlcv_infer_cpp_test dvs-memory-loading-selftest <modelPath> <imagePath> [device]");
        return 2;
    }

    const std::wstring modelPath = argv[2];
    const std::wstring imagePath = argv[3];
    const int deviceId = argc == 5 ? _wtoi(argv[4]) : 0;
    const std::wstring extension = dvs_test::Lower(std::filesystem::path(modelPath).extension().wstring());
    if (extension != L".dvst" && extension != L".dvso") {
        PrintUtf8Line("模型必须为 .dvst 或 .dvso");
        return 2;
    }

    try {
        cv::Mat rgb = ReadImageRgb(imagePath);
        if (rgb.empty()) {
            PrintUtf8Line("图片读取失败");
            return 2;
        }

        dvs_test::TempArtifactMonitor monitor(dvs_test::ReadArchiveFileNames(modelPath));
        monitor.Start();
        std::string operationError;
        try {
            dlcv_infer::Model model(modelPath, deviceId);
            json params = {
                {"threshold", 0.5},
                {"with_mask", true},
                {"batch_size", 1}
            };
            dlcv_infer::Result result = model.Infer(rgb, params);
            DisposeResultMasks(result);
            if (HasSharedFlowSdkForSelfTest(model)) {
                auto restored = dlcv_infer::CreateModelFromIndex(model.modelIndex);
                (void)restored.GetDvsModelInfo();
                auto restoredResult = restored.Infer(rgb, params);
                DisposeResultMasks(restoredResult);
                restored.FreeModel();
                PrintUtf8Line("共享流程恢复、信息查询及推理已执行，持续检查归档文件");
            } else {
                PrintUtf8Line("共享流程恢复未执行：当前 SDK 缺少完整共享接口");
            }
            const bool sharedSdk = HasSharedFlowSdkForSelfTest(model);
            model.FreeModel();
            if (sharedSdk) VerifyCachedModelInvalidationForSelfTest(modelPath, deviceId, true, rgb);
        } catch (const std::exception& ex) {
            operationError = ex.what();
        } catch (...) {
            operationError = "加载、推理或释放时发生未知异常";
        }
        monitor.Stop();

        const bool hasArtifacts = monitor.HasArtifacts();
        PrintUtf8Line(hasArtifacts
            ? "系统临时目录出现流程归档文件: " + monitor.DescribeUtf8()
            : "系统临时目录未观察到流程归档文件");
        if (!operationError.empty()) PrintUtf8ErrorLine(operationError);
        if (!operationError.empty() || hasArtifacts) return 1;
        PrintUtf8Line("C++ 流程归档内存加载测试通过");
        return 0;
    } catch (const std::exception& ex) {
        PrintUtf8ErrorLine(ex.what());
        return 1;
    }
}

int RunDvspRejectSelfTest(int argc, wchar_t* argv[]) {
    if (argc < 3 || argc > 4) {
        PrintUtf8Line("用法: dlcv_infer_cpp_test dvsp-reject-selftest <modelPath> [device]");
        return 2;
    }

    const std::wstring modelPath = argv[2];
    const int deviceId = argc == 4 ? _wtoi(argv[3]) : 0;
    if (dvs_test::Lower(std::filesystem::path(modelPath).extension().wstring()) != L".dvsp") {
        PrintUtf8Line("模型必须为 .dvsp");
        return 2;
    }

    try {
        dvs_test::TempArtifactMonitor monitor({});
        monitor.Start();
        bool rejected = false;
        std::string errorMessage;
        try {
            dlcv_infer::Model model(modelPath, deviceId);
            model.FreeModel();
        } catch (const std::exception& ex) {
            errorMessage = ex.what();
            rejected = dvs_test::HasExplicitDvspUnsupportedMessage(errorMessage);
        } catch (...) {
            errorMessage = "加载 .dvsp 时发生未知异常";
        }
        monitor.Stop();

        if (!rejected) {
            PrintUtf8ErrorLine(".dvsp 未返回明确的不支持错误: " + errorMessage);
            return 1;
        }
        if (monitor.HasArtifacts()) {
            PrintUtf8ErrorLine("拒绝 .dvsp 时系统临时目录出现流程归档文件: " + monitor.DescribeUtf8());
            return 1;
        }
        PrintUtf8Line("C++ .dvsp 拒绝测试通过");
        return 0;
    } catch (const std::exception& ex) {
        PrintUtf8ErrorLine(ex.what());
        return 1;
    }
}

bool HasUndersizedModelMessage(const std::string& message) {
    return message.find("小于 1MB") != std::string::npos;
}

std::wstring BuildUndersizedSelfTestPath(const wchar_t* suffix) {
    wchar_t tempPath[MAX_PATH] = {0};
    const DWORD length = GetTempPathW(static_cast<DWORD>(sizeof(tempPath) / sizeof(tempPath[0])), tempPath);
    if (length == 0 || length >= sizeof(tempPath) / sizeof(tempPath[0])) {
        throw std::runtime_error("cannot get temp path");
    }
    static std::atomic<std::uint64_t> undersizedSequence{0};
    std::wstring path(tempPath, length);
    path += L"dlcv_cpp_undersized_";
    path += std::to_wstring(GetCurrentProcessId());
    path += L"_";
    path += std::to_wstring(undersizedSequence.fetch_add(1, std::memory_order_relaxed));
    path += suffix;
    return path;
}

void WriteUndersizedSelfTestFile(const std::wstring& path, size_t size) {
    std::ofstream ofs(path, std::ios::binary);
    if (!ofs) {
        throw std::runtime_error("cannot write temp model file");
    }
    std::vector<char> bytes(size, 'A');
    ofs.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
    if (!ofs) {
        throw std::runtime_error("cannot finish temp model file");
    }
}

std::string LoadUndersizedModelExpectingError(const std::wstring& path) {
    try {
        dlcv_infer::Model model(path, 0);
        model.FreeModel();
        return std::string();
    } catch (const std::exception& ex) {
        return ex.what();
    }
}

int RunUndersizedModelSelfTest() {
    try {
        const wchar_t* suffixes[] = { L".dvt", L".dvo", L".dvp", L".dvst", L".dvso" };
        std::vector<std::wstring> files;
        for (const wchar_t* suffix : suffixes) {
            std::wstring path = BuildUndersizedSelfTestPath(suffix);
            files.push_back(path);
            WriteUndersizedSelfTestFile(path, 512);
            const std::string error = LoadUndersizedModelExpectingError(path);
            if (!HasUndersizedModelMessage(error)) {
                PrintUtf8ErrorLine(std::string(WideToUtf8(suffix)) + " did not return undersized error: " + (error.empty() ? "loaded" : error));
                for (const auto& file : files) DeleteFileW(file.c_str());
                return 1;
            }
        }

        std::wstring dvspPath = BuildUndersizedSelfTestPath(L".dvsp");
        files.push_back(dvspPath);
        WriteUndersizedSelfTestFile(dvspPath, 512);
        const std::string dvspError = LoadUndersizedModelExpectingError(dvspPath);
        if (!dvs_test::HasExplicitDvspUnsupportedMessage(dvspError) || HasUndersizedModelMessage(dvspError)) {
            PrintUtf8ErrorLine(std::string(".dvsp undersized file should be unsupported: ") + (dvspError.empty() ? "loaded" : dvspError));
            for (const auto& file : files) DeleteFileW(file.c_str());
            return 1;
        }

        for (const auto& file : files) DeleteFileW(file.c_str());
        PrintUtf8Line("C++ 过小模型文件拒绝测试通过");
        return 0;
    } catch (const std::exception& ex) {
        PrintUtf8ErrorLine(ex.what());
        return 1;
    }
}

struct ScopedDvsSelfTestFile final {
    std::wstring path;

    explicit ScopedDvsSelfTestFile(std::wstring value) : path(std::move(value)) {}
    ~ScopedDvsSelfTestFile() {
        if (!path.empty()) DeleteFileW(path.c_str());
    }

    ScopedDvsSelfTestFile(const ScopedDvsSelfTestFile&) = delete;
    ScopedDvsSelfTestFile& operator=(const ScopedDvsSelfTestFile&) = delete;
};

std::wstring BuildDvsSelfTestFilePath(const wchar_t* suffix) {
    wchar_t tempPath[MAX_PATH] = {0};
    const DWORD length = GetTempPathW(static_cast<DWORD>(sizeof(tempPath) / sizeof(tempPath[0])), tempPath);
    if (length == 0 || length >= sizeof(tempPath) / sizeof(tempPath[0])) {
        throw std::runtime_error("无法获取系统临时目录");
    }

    static std::atomic<std::uint64_t> sequence{0};
    std::wstring path(tempPath, length);
    path += L"dlcv_cpp_dvs_";
    path += std::to_wstring(GetCurrentProcessId());
    path += L"_";
    path += std::to_wstring(sequence.fetch_add(1, std::memory_order_relaxed));
    path += L"_";
    path += suffix;
    path += L".dvst";
    return path;
}

void AppendBytes(std::vector<unsigned char>& output, const unsigned char* data, size_t size) {
    output.insert(output.end(), data, data + size);
}

std::vector<unsigned char> BuildDvsSelfTestArchive(
    const std::vector<std::string>& fileNames,
    const std::vector<std::vector<unsigned char>>& fileData) {
    if (fileNames.size() != fileData.size()) {
        throw std::invalid_argument("归档文件名与数据数量不一致");
    }

    json header = json::object();
    header["file_list"] = json::array();
    header["file_size"] = json::array();
    for (size_t i = 0; i < fileNames.size(); ++i) {
        header["file_list"].push_back(fileNames[i]);
        header["file_size"].push_back(fileData[i].size());
    }

    std::vector<unsigned char> archive;
    static constexpr unsigned char magic[] = {'D', 'V', '\n'};
    AppendBytes(archive, magic, sizeof(magic));
    const std::string headerLine = header.dump() + "\n";
    AppendBytes(
        archive,
        reinterpret_cast<const unsigned char*>(headerLine.data()),
        headerLine.size());
    for (const auto& data : fileData) {
        if (!data.empty()) AppendBytes(archive, data.data(), data.size());
    }
    return archive;
}

void WriteDvsSelfTestFile(const std::wstring& path, const std::vector<unsigned char>& data) {
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"wb") != 0 || file == nullptr) {
        throw std::runtime_error("无法写入 DVS 自测归档");
    }
    const size_t written = data.empty() ? 0 : std::fwrite(data.data(), 1, data.size(), file);
    const int closeResult = std::fclose(file);
    if (written != data.size() || closeResult != 0) {
        DeleteFileW(path.c_str());
        throw std::runtime_error("DVS 自测归档写入不完整");
    }
}

bool ContainsArchiveDuplicateError(const std::string& message, const std::string& name) {
    const std::string prefix = "归档中存在同名但内容不同的文件：";
    return message.find(prefix) != std::string::npos && message.find(name) != std::string::npos;
}

bool LoadDvsArchiveForSelfTest(const std::wstring& path, std::string& error) {
    try {
        dlcv_infer::Model model(path, 0);
        model.FreeModel();
        return true;
    } catch (const std::exception& ex) {
        error = ex.what();
        return false;
    } catch (...) {
        error = "加载 DVS 自测归档时发生未知异常";
        return false;
    }
}

bool VerifyDvsArchiveAccepted(
    const std::vector<std::string>& fileNames,
    const std::vector<std::vector<unsigned char>>& fileData,
    std::string& error) {
    ScopedDvsSelfTestFile file(BuildDvsSelfTestFilePath(L"accepted"));
    WriteDvsSelfTestFile(file.path, BuildDvsSelfTestArchive(fileNames, fileData));
    return LoadDvsArchiveForSelfTest(file.path, error);
}

bool VerifyDvsArchiveRejected(
    const std::vector<std::string>& fileNames,
    const std::vector<std::vector<unsigned char>>& fileData,
    const std::string& expectedName,
    std::string& error) {
    ScopedDvsSelfTestFile file(BuildDvsSelfTestFilePath(L"rejected"));
    WriteDvsSelfTestFile(file.path, BuildDvsSelfTestArchive(fileNames, fileData));
    if (LoadDvsArchiveForSelfTest(file.path, error)) {
        error = "同名但内容不同的归档未被拒绝";
        return false;
    }
    if (!ContainsArchiveDuplicateError(error, expectedName)) {
        error = "归档拒绝信息不符合预期: " + error;
        return false;
    }
    return true;
}

int RunDvsArchiveDuplicateSelfTest() {
    try {
        const std::vector<unsigned char> emptyPipeline = {
            '{', '"', 'n', 'o', 'd', 'e', 's', '"', ':', '[', ']', '}'
        };
        const std::vector<unsigned char> modelBytes = {'s', 'a', 'm', 'e', '-', 'm', 'o', 'd', 'e', 'l'};
        const std::vector<unsigned char> otherModelBytes = {'s', 'a', 'm', 'e', '-', 'm', 'o', 'd', 'e', 'x'};
        std::string error;

        if (!VerifyDvsArchiveAccepted(
                {"pipeline.json", "model.dvt", " \t.\\./MODEL.DVT \r"},
                {emptyPipeline, modelBytes, modelBytes},
                error)) {
            PrintUtf8ErrorLine("DVS 同名模型同字节归档检查失败: " + error);
            return 1;
        }
        if (!VerifyDvsArchiveRejected(
                {"pipeline.json", "model.dvt", "./model.dvt"},
                {emptyPipeline, modelBytes, otherModelBytes},
                "model.dvt",
                error)) {
            PrintUtf8ErrorLine("DVS 同名模型不同字节归档检查失败: " + error);
            return 1;
        }
        if (!VerifyDvsArchiveAccepted(
                {"pipeline.json", " \t.\\./PIPELINE.JSON \r", "model.dvt"},
                {emptyPipeline, emptyPipeline, modelBytes},
                error)) {
            PrintUtf8ErrorLine("DVS 同名 pipeline.json 同字节归档检查失败: " + error);
            return 1;
        }
        const std::vector<unsigned char> otherPipeline = {
            '{', '"', 'n', 'o', 'd', 'e', 's', '"', ':', '[', '{', '}', ']', '}'
        };
        if (!VerifyDvsArchiveRejected(
                {"pipeline.json", "./pipeline.json"},
                {emptyPipeline, otherPipeline},
                "pipeline.json",
                error)) {
            PrintUtf8ErrorLine("DVS 同名 pipeline.json 不同字节归档检查失败: " + error);
            return 1;
        }

        PrintUtf8Line("DVS 同名归档成员内容检查通过");
        return 0;
    } catch (const std::exception& ex) {
        PrintUtf8ErrorLine(std::string("DVS 同名归档成员内容检查异常: ") + ex.what());
        return 1;
    } catch (...) {
        PrintUtf8ErrorLine("DVS 同名归档成员内容检查发生未知异常");
        return 1;
    }
}

std::vector<unsigned char> ReadBinaryForDvsPoolSelfTest(const std::wstring& path) {
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"rb") != 0 || file == nullptr) return {};
    std::unique_ptr<FILE, decltype(&fclose)> holder(file, &fclose);
    if (_fseeki64(file, 0, SEEK_END) != 0) return {};
    const __int64 size = _ftelli64(file);
    if (size <= 0 || _fseeki64(file, 0, SEEK_SET) != 0) return {};
    std::vector<unsigned char> data(static_cast<size_t>(size));
    if (std::fread(data.data(), 1, data.size(), file) != data.size()) return {};
    return data;
}

std::vector<unsigned char> BuildDvsModelPoolPipeline() {
    json pipeline = json::object();
    pipeline["nodes"] = json::array();
    for (const auto& item : std::vector<std::pair<int, std::string>>{
             {1, "model.dvt"}, {2, ".\\./MODEL.DVT"}}) {
        json node = json::object();
        node["id"] = item.first;
        node["type"] = "model/det";
        node["properties"] = json::object({
            {"model_path", item.second},
            {"model_index", INT_MAX}
        });
        pipeline["nodes"].push_back(std::move(node));
    }
    const std::string text = pipeline.dump();
    return std::vector<unsigned char>(text.begin(), text.end());
}

std::vector<unsigned char> BuildDvsModelPoolArchive(const std::vector<unsigned char>& modelData) {
    return BuildDvsSelfTestArchive(
        {"pipeline.json", "model.dvt"},
        {BuildDvsModelPoolPipeline(), modelData});
}

bool IsExpectedModelPoolStats(
    const dlcv_infer::flow::ModelPoolStats& stats,
    size_t totalEntries) {
    return stats.totalEntries == totalEntries &&
        stats.activeEntries == totalEntries &&
        stats.idleEntries == 0;
}

bool IsEmptyModelPoolStats(const dlcv_infer::flow::ModelPoolStats& stats) {
    return stats.totalEntries == 0 && stats.activeEntries == 0 && stats.idleEntries == 0;
}

int QueryNativeIndexTypeForSelfTest(int index);

void VerifyFlowLoadCleanupForSelfTest(const std::wstring& modelPath, int deviceId);

int RunDvsModelPoolSelfTest(int argc, wchar_t* argv[]) {
    if (argc < 3 || argc > 4) {
        PrintUtf8Line("用法: dlcv_infer_cpp_test dvs-model-pool-selftest <modelPath> [device]");
        return 2;
    }

    const std::wstring modelPath = argv[2];
    const int deviceId = argc == 4 ? _wtoi(argv[3]) : 0;
    try {
        const std::vector<unsigned char> modelData = ReadBinaryForDvsPoolSelfTest(modelPath);
        if (modelData.empty()) {
            PrintUtf8ErrorLine("模型文件为空或读取失败");
            return 2;
        }

        const ScopedDvsSelfTestFile firstArchive(BuildDvsSelfTestFilePath(L"pool_first"));
        const ScopedDvsSelfTestFile secondArchive(BuildDvsSelfTestFilePath(L"pool_second"));
        const std::vector<unsigned char> archive = BuildDvsModelPoolArchive(modelData);
        WriteDvsSelfTestFile(firstArchive.path, archive);
        WriteDvsSelfTestFile(secondArchive.path, archive);
        dlcv_infer::NativeApi::FreeAllModels();

        std::unique_ptr<dlcv_infer::Model> firstModel =
            std::make_unique<dlcv_infer::Model>(firstArchive.path, deviceId);
        const auto firstStats = dlcv_infer::flow::GetModelPoolStats();
        if (!IsExpectedModelPoolStats(firstStats, 1)) {
            PrintUtf8ErrorLine("一个归档内多个相同模型节点未复用为一个模型池项");
            return 1;
        }

        std::unique_ptr<dlcv_infer::Model> secondModel =
            std::make_unique<dlcv_infer::Model>(secondArchive.path, deviceId);
        const auto twoArchiveStats = dlcv_infer::flow::GetModelPoolStats();
        if (!IsExpectedModelPoolStats(twoArchiveStats, 2)) {
            PrintUtf8ErrorLine("两个独立归档加载实例未分别创建模型池项");
            return 1;
        }

        const auto firstMeta = firstModel->GetDvsModelInfo().at("loaded_model_meta");
        const auto secondMeta = secondModel->GetDvsModelInfo().at("loaded_model_meta");
        const int sharedModelIndex = firstMeta.at(0).at("model_index").get<int>();
        if (sharedModelIndex < 0 || sharedModelIndex == INT_MAX) {
            PrintUtf8ErrorLine("归档中的遗留 model_index 覆盖了包内子模型");
            return 1;
        }
        for (const auto& meta : { firstMeta, secondMeta }) {
            for (const auto& item : meta) {
                if (item.at("model_index").get<int>() != sharedModelIndex) {
                    PrintUtf8ErrorLine("相同内容的归档子模型未复用底层 model_index");
                    return 1;
                }
            }
        }

        firstModel->FreeModel();
        firstModel.reset();
        if (QueryNativeIndexTypeForSelfTest(sharedModelIndex) != 1) {
            PrintUtf8ErrorLine("释放首个归档后仍被使用的底层 model_index 已失效");
            return 1;
        }
        const auto afterFirstRelease = dlcv_infer::flow::GetModelPoolStats();
        if (!IsExpectedModelPoolStats(afterFirstRelease, 1)) {
            PrintUtf8ErrorLine("释放第一个归档实例后第二个实例未保持有效");
            return 1;
        }

        secondModel->FreeModel();
        secondModel.reset();
        const auto afterAllRelease = dlcv_infer::flow::GetModelPoolStats();
        if (!IsEmptyModelPoolStats(afterAllRelease)) {
            PrintUtf8ErrorLine("释放全部归档实例后模型池未清空");
            return 1;
        }

        if (QueryNativeIndexTypeForSelfTest(sharedModelIndex) != 0) {
            PrintUtf8ErrorLine("释放全部归档后底层 model_index 未清除");
            return 1;
        }
        dlcv_infer::NativeApi::FreeAllModels();
        VerifyFlowLoadCleanupForSelfTest(modelPath, deviceId);
        PrintUtf8Line("DVS 模型池复用、独立归档加载、共享 model_index 与释放检查通过");
        return 0;
    } catch (const std::exception& ex) {
        dlcv_infer::NativeApi::FreeAllModels();
        PrintUtf8ErrorLine(std::string("DVS 模型池检查失败: ") + ex.what());
        return 1;
    } catch (...) {
        dlcv_infer::NativeApi::FreeAllModels();
        PrintUtf8ErrorLine("DVS 模型池检查发生未知异常");
        return 1;
    }
}

std::string BuildTempRectCorrectionDir() {
    char tempPath[MAX_PATH] = {0};
    const DWORD n = GetTempPathA(static_cast<DWORD>(sizeof(tempPath)), tempPath);
    std::string base = (n > 0 && n < sizeof(tempPath)) ? std::string(tempPath) : std::string(".\\");
    const char last = base.empty() ? '\0' : base.back();
    if (last != '\\' && last != '/') base.push_back('\\');
    std::string dir = base + "dlcv_rect_image_correction_" + std::to_string(GetCurrentProcessId());
    CreateDirectoryA(dir.c_str(), nullptr);
    return dir;
}

std::string JoinPathA(const std::string& dir, const std::string& name) {
    if (dir.empty()) return name;
    const char last = dir.back();
    if (last == '\\' || last == '/') return dir + name;
    return dir + "\\" + name;
}

void DeleteFilesWithSuffix(const std::string& dir, const std::string& suffixWithExt) {
    WIN32_FIND_DATAA data{};
    const std::string pattern = JoinPathA(dir, "*");
    HANDLE h = FindFirstFileA(pattern.c_str(), &data);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) continue;
        const std::string name = data.cFileName;
        if (name.size() >= suffixWithExt.size() &&
            name.compare(name.size() - suffixWithExt.size(), suffixWithExt.size(), suffixWithExt) == 0) {
            DeleteFileA(JoinPathA(dir, name).c_str());
        }
    } while (FindNextFileA(h, &data));
    FindClose(h);
}

cv::Mat LoadSingleFileWithSuffix(const std::string& dir, const std::string& suffixWithExt) {
    cv::Mat loaded;
    int matchCount = 0;
    WIN32_FIND_DATAA data{};
    const std::string pattern = JoinPathA(dir, "*");
    HANDLE h = FindFirstFileA(pattern.c_str(), &data);
    if (h == INVALID_HANDLE_VALUE) return loaded;
    do {
        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) continue;
        const std::string name = data.cFileName;
        if (name.size() >= suffixWithExt.size() &&
            name.compare(name.size() - suffixWithExt.size(), suffixWithExt.size(), suffixWithExt) == 0) {
            matchCount += 1;
            loaded = cv::imread(JoinPathA(dir, name), cv::IMREAD_UNCHANGED);
        }
    } while (FindNextFileA(h, &data));
    FindClose(h);
    return matchCount == 1 ? loaded : cv::Mat();
}

int RunCurveTextAffineSample(int argc, wchar_t* argv[]) {
    if (argc != 5) throw std::invalid_argument("curve-text-affine-selftest <image> <polygons-json> <output-dir>");
    const std::filesystem::path directory(argv[4]);
    if (std::filesystem::exists(directory)) throw std::runtime_error("结果目录已存在");
    std::ifstream imageStream(std::filesystem::path(argv[2]), std::ios::binary);
    const std::vector<unsigned char> bytes((std::istreambuf_iterator<char>(imageStream)), {});
    const cv::Mat image = cv::imdecode(bytes, cv::IMREAD_COLOR);
    if (image.empty()) throw std::runtime_error("测试图片读取失败");
    std::ifstream jsonStream{std::filesystem::path(argv[3])};
    const json detections = json::parse(jsonStream);
    if (!detections.is_array()) throw std::invalid_argument("polygon 数据应为检测结果数组");
    dlcv_infer::flow::TransformationState state(image.cols, image.rows);
    std::vector<dlcv_infer::flow::ModuleImage> images = {
        dlcv_infer::flow::ModuleImage(image, image, state, 0)};
    const json results = json::array({{{"type", "local"}, {"index", 0}, {"sample_results", detections}}});
    const auto factory = dlcv_infer::flow::ModuleRegistry::Get("pre_process/curve_text_affine");
    auto module = factory(401, std::string(), json::object(), nullptr);
    const auto output = module->Process(images, results);
    if (output.ImageList.size() != detections.size()) throw std::runtime_error("展开区域数量错误");
    std::filesystem::create_directories(directory);
    for (size_t i = 0; i < output.ImageList.size(); i++) {
        const auto& affine = output.ImageList[i].AffineImage;
        std::vector<unsigned char> encoded;
        if (affine.empty() || !cv::imencode(".png", affine, encoded)) throw std::runtime_error("展开图为空或编码失败");
        std::ofstream file(directory / ("region-" + std::to_string(i) + ".png"), std::ios::binary);
        file.write(reinterpret_cast<const char*>(encoded.data()), encoded.size());
        if (!file) throw std::runtime_error("展开图保存失败");
    }
    std::ofstream report(directory / "result.json");
    report << output.ResultList.dump(2);
    if (!report) throw std::runtime_error("展开结果保存失败");
    PrintUtf8Line("实际图片展开完成，区域数: " + std::to_string(output.ImageList.size()));
    return 0;
}

int RunCurveTextAffineSelfTest() {
    auto fail = [](const std::string& message) -> int {
        PrintUtf8Line("curve_text_affine selftest failed: " + message);
        return 1;
    };

    cv::Mat probabilityMask(20, 30, CV_32FC1, cv::Scalar(0.01f));
    cv::rectangle(probabilityMask, cv::Rect(6, 4, 18, 12), cv::Scalar(0.9f), cv::FILLED);
    probabilityMask.at<float>(0, 0) = 0.5f;
    const json maskInfo = dlcv_infer::flow::MatToMaskInfo(probabilityMask);
    const cv::Mat decoded = dlcv_infer::flow::MaskInfoToMat(maskInfo);
    if (decoded.empty() || cv::countNonZero(decoded) != 217) {
        return fail("probability mask foreground count mismatch");
    }
    if (decoded.at<std::uint8_t>(0, 0) == 0
        || decoded.at<std::uint8_t>(3, 6) != 0
        || decoded.at<std::uint8_t>(4, 6) == 0) {
        return fail("probability mask threshold or boundary mismatch");
    }

    if (!dlcv_infer::flow::ModuleRegistry::Has("pre_process/curve_text_affine")) {
        return fail("pre_process/curve_text_affine is not registered");
    }

    // 坐标渐变图检查映射方向，不以输出数量和尺寸代替镜像检查。
    cv::Mat coordinates(256, 256, CV_8UC3);
    for (int y = 0; y < coordinates.rows; y++)
        for (int x = 0; x < coordinates.cols; x++)
            coordinates.at<cv::Vec3b>(y, x) = cv::Vec3b(static_cast<uchar>(x), static_cast<uchar>(y), 0);
    for (double angle : {0.0, 30.0, 89.9, 90.0, 90.1, 135.0, 180.0, 225.0, 269.9, 270.0, 270.1, 315.0})
    for (bool curved : {false, true}) {
        const double radians = angle * CV_PI / 180.0;
        json polygon = json::array();
        for (int i = 0; i < 16; i++) {
            const double x = -70 + (i < 8 ? i : 15 - i) * 20.0;
            const double y = (i < 8 ? -15 : 15) + (curved ? 20 * std::cos(x * CV_PI / 140) : 0);
            polygon.push_back({128 + x * std::cos(radians) - y * std::sin(radians),
                128 + x * std::sin(radians) + y * std::cos(radians)});
        }
        dlcv_infer::flow::TransformationState state(256, 256);
        std::vector<dlcv_infer::flow::ModuleImage> images = {
            dlcv_infer::flow::ModuleImage(coordinates, coordinates, state, 0)};
        json results = json::array({{{"type", "local"}, {"index", 0},
            {"sample_results", json::array({{{"polygon", polygon}}})}}});
        const auto factory = dlcv_infer::flow::ModuleRegistry::Get("pre_process/curve_text_affine");
        auto module = factory(401, std::string(), {{"out_height", 31}, {"sample_step", 10.0}}, nullptr);
        const auto output = module->Process(images, results);
        if (output.ImageList.size() != 1 || output.ImageList[0].AffineImage.empty())
            return fail("方向测试没有生成展开图: " + std::to_string(angle));
        const cv::Mat& affine = output.ImageList[0].AffineImage;
        for (int column : {affine.cols / 4, affine.cols / 2, affine.cols * 3 / 4}) {
            const cv::Vec3b left = affine.at<cv::Vec3b>(15, column - 2), right = affine.at<cv::Vec3b>(15, column + 2);
            const cv::Vec3b top = affine.at<cv::Vec3b>(4, column), bottom = affine.at<cv::Vec3b>(26, column);
            const int determinant = (right[0] - left[0]) * (bottom[1] - top[1])
                - (right[1] - left[1]) * (bottom[0] - top[0]);
            if (determinant <= 0) return fail("曲线展开发生镜像: angle=" + std::to_string(angle)
                + ", curved=" + std::to_string(curved) + ", determinant=" + std::to_string(determinant));
        }
    }
    PrintUtf8Line("曲线展开 24 组方向检查通过，每组检查三处局部方向");

    cv::Mat image(180, 360, CV_8UC3, cv::Scalar::all(0));
    cv::Mat mask(180, 360, CV_8UC1, cv::Scalar::all(0));
    const std::vector<cv::Point> polygon = {
        {20,72}, {80,48}, {150,38}, {230,48}, {340,78},
        {340,126}, {230,96}, {150,86}, {80,96}, {20,120}
    };
    cv::fillPoly(mask, std::vector<std::vector<cv::Point>>{polygon}, cv::Scalar::all(255));
    image.setTo(cv::Scalar(240, 240, 240), mask);
    for (int x = 45; x < 330; x += 28) {
        cv::line(image, cv::Point(x, 45), cv::Point(x, 125), cv::Scalar(20, 20, 20), 4);
    }

    dlcv_infer::flow::TransformationState state(image.cols, image.rows);
    std::vector<dlcv_infer::flow::ModuleImage> images = {
        dlcv_infer::flow::ModuleImage(image, image, state, 0)
    };
    json results = json::array({
        json::object({
            {"type", "local"}, {"index", 0}, {"origin_index", 0}, {"transform", state.ToJson()},
            {"sample_results", json::array({
                json::object({
                    {"bbox", json::array({0, 0, image.cols, image.rows})},
                    {"mask_rle", dlcv_infer::flow::MatToMaskInfo(mask)},
                    {"score", 0.99}, {"category_name", "text"}
                })
            })}
        })
    });
    const auto factory = dlcv_infer::flow::ModuleRegistry::Get("pre_process/curve_text_affine");
    auto module = factory(401, std::string(), json::object({
        {"out_height", 80}, {"sample_step", 10.0}, {"smooth_s", 10000.0},
        {"shrink_inside", 1.5}, {"method", "auto"}
    }), nullptr);
    const dlcv_infer::flow::ModuleIO output = module->Process(images, results);
    if (output.ImageList.size() != 1 || !output.ResultList.is_array() || output.ResultList.size() != 1) {
        return fail("curve output count mismatch");
    }
    const cv::Mat& affine = output.ImageList[0].AffineImage;
    if (affine.empty() || affine.rows != 80 || affine.cols < 250) {
        return fail("curve affine image is invalid");
    }

    PrintUtf8Line("curve_text_affine selftest passed");
    return 0;
}

bool MatsExactlyEqual(const cv::Mat& left, const cv::Mat& right) {
    if (left.empty() || right.empty()) return left.empty() && right.empty();
    if (left.size() != right.size() || left.type() != right.type()) return false;
    return cv::norm(left, right, cv::NORM_INF) == 0.0;
}

int RunAiOrientationAffineSelfTest() {
    auto fail = [](const std::string& message) -> int {
        PrintUtf8Line("ai_orientation_affine selftest failed: " + message);
        return 1;
    };

    if (!dlcv_infer::flow::ModuleRegistry::Has("pre_process/image_rotate_by_cls")) {
        return fail("pre_process/image_rotate_by_cls is not registered");
    }

    const auto factory = dlcv_infer::flow::ModuleRegistry::Get("pre_process/image_rotate_by_cls");
    const cv::Mat base = (cv::Mat_<std::uint8_t>(2, 3) << 1, 2, 3, 4, 5, 6);
    const cv::Mat affine = (cv::Mat_<std::uint8_t>(2, 3) << 11, 12, 13, 14, 15, 16);

    dlcv_infer::flow::TransformationState state(100, 80);
    state.CropBox = { 7, 9, 30, 20 };
    state.AffineMatrix2x3 = { 1, 0, 7, 0, 1, 9 };
    state.OutputSize = { base.cols, base.rows };

    const std::vector<std::pair<std::string, int>> cases = {
        { "0", 0 }, { "90", 90 }, { "180", 180 }, { "270", 270 }
    };
    for (const auto& testCase : cases) {
        dlcv_infer::flow::ModuleImage image(base.clone(), base.clone(), state, 3);
        image.AffineImage = affine.clone();
        image.UniqueId = "orientation-affine-test";
        image.SlidingMeta.Valid = true;
        image.SlidingMeta.GridX = 2;

        json results = json::array({
            json::object({
                {"type", "local"},
                {"index", 0},
                {"origin_index", 3},
                {"transform", state.ToJson()},
                {"sample_results", json::array({
                    json::object({
                        {"bbox", json::array({ 1, 0, 2, 2 })},
                        {"with_bbox", true},
                        {"with_mask", true},
                        {"mask_rle", json::object({ {"size", json::array({ 2, 3 })}, {"counts", "test"} })},
                        {"score", 0.98},
                        {"category_name", testCase.first}
                    })
                })}
            })
        });

        auto module = factory(402, std::string(), json::object({
            {"rotate90_labels", json::array({ "90" })},
            {"rotate180_labels", json::array({ "180" })},
            {"rotate270_labels", json::array({ "270" })},
            {"rotate_affine_img", true}
        }), nullptr);
        const dlcv_infer::flow::ModuleIO output = module->Process({ image }, results);
        if (output.ImageList.size() != 1) {
            return fail("affine mode image count mismatch for angle " + testCase.first);
        }
        const auto& outputImage = output.ImageList.front();
        if (!MatsExactlyEqual(outputImage.ImageObject, base)) {
            return fail("ImageObject changed in affine mode for angle " + testCase.first);
        }
        if (outputImage.TransformState.ToJson() != state.ToJson()) {
            return fail("TransformState changed in affine mode for angle " + testCase.first);
        }
        if (output.ResultList != results) {
            return fail("result list changed in affine mode for angle " + testCase.first);
        }
        if (outputImage.UniqueId != image.UniqueId
            || outputImage.SlidingMeta.Valid != image.SlidingMeta.Valid
            || outputImage.SlidingMeta.GridX != image.SlidingMeta.GridX) {
            return fail("ModuleImage metadata changed in affine mode for angle " + testCase.first);
        }

        cv::Mat expectedAffine;
        if (testCase.second == 90) cv::rotate(affine, expectedAffine, cv::ROTATE_90_COUNTERCLOCKWISE);
        else if (testCase.second == 180) cv::rotate(affine, expectedAffine, cv::ROTATE_180);
        else if (testCase.second == 270) cv::rotate(affine, expectedAffine, cv::ROTATE_90_CLOCKWISE);
        else expectedAffine = affine;
        if (!MatsExactlyEqual(outputImage.AffineImage, expectedAffine)) {
            return fail("AffineImage rotation mismatch for angle " + testCase.first);
        }
    }

    dlcv_infer::flow::ModuleImage affineOnlyImage;
    affineOnlyImage.AffineImage = affine.clone();
    affineOnlyImage.TransformState = state;
    affineOnlyImage.OriginalIndex = 3;
    auto affineOnlyModule = factory(403, std::string(), json::object({
        {"rotate90_labels", json::array({ "90" })},
        {"rotate180_labels", json::array({ "180" })},
        {"rotate270_labels", json::array({ "270" })},
        {"rotate_affine_img", true}
    }), nullptr);
    const json affineOnlyResults = json::array({
        json::object({
            {"type", "local"}, {"index", 0}, {"origin_index", 3}, {"transform", state.ToJson()},
            {"sample_results", json::array({
                json::object({
                    {"bbox", json::array({ 1, 0, 2, 2 })},
                    {"with_bbox", true},
                    {"with_mask", true},
                    {"mask_rle", json::object({ {"size", json::array({ 2, 3 })}, {"counts", "test"} })},
                    {"score", 0.98},
                    {"category_name", "90"}
                })
            })}
        })
    });
    const dlcv_infer::flow::ModuleIO affineOnlyOutput = affineOnlyModule->Process({ affineOnlyImage }, affineOnlyResults);
    cv::Mat expectedAffineOnly;
    cv::rotate(affine, expectedAffineOnly, cv::ROTATE_90_COUNTERCLOCKWISE);
    if (affineOnlyOutput.ImageList.size() != 1
        || !affineOnlyOutput.ImageList.front().ImageObject.empty()
        || !MatsExactlyEqual(affineOnlyOutput.ImageList.front().AffineImage, expectedAffineOnly)
        || affineOnlyOutput.ResultList != affineOnlyResults) {
        return fail("valid AffineImage was skipped when ImageObject was empty");
    }

    dlcv_infer::flow::ModuleImage fallbackImage(base.clone(), base.clone(), state, 3);
    const json fallbackResults = json::array({
        json::object({
            {"type", "local"}, {"index", 0}, {"origin_index", 3}, {"transform", state.ToJson()},
            {"sample_results", json::array({
                json::object({
                    {"bbox", json::array({ 1, 0, 2, 2 })},
                    {"with_bbox", true},
                    {"score", 0.98},
                    {"category_name", "90"}
                })
            })}
        })
    });
    auto fallbackModule = factory(403, std::string(), json::object({
        {"rotate90_labels", json::array({ "90" })},
        {"rotate180_labels", json::array({ "180" })},
        {"rotate270_labels", json::array({ "270" })},
        {"rotate_affine_img", true}
    }), nullptr);
    const dlcv_infer::flow::ModuleIO fallbackOutput = fallbackModule->Process({ fallbackImage }, fallbackResults);
    cv::Mat expectedFallback;
    cv::rotate(base, expectedFallback, cv::ROTATE_90_COUNTERCLOCKWISE);
    if (fallbackOutput.ImageList.size() != 1
        || !MatsExactlyEqual(fallbackOutput.ImageList.front().ImageObject, expectedFallback)) {
        return fail("missing AffineImage did not preserve ImageObject rotation fallback");
    }
    if (fallbackOutput.ImageList.front().TransformState.ToJson() == state.ToJson()
        || fallbackOutput.ResultList == fallbackResults) {
        return fail("missing AffineImage did not preserve transform/result fallback");
    }

    PrintUtf8Line("ai_orientation_affine selftest passed");
    return 0;
}

int RunImagePrepCheck() {
    auto fail = [](const std::string& message) -> int {
        PrintUtf8Line("imageprepcheck failed: " + message);
        return 1;
    };

    {
        cv::Mat gray16(1, 3, CV_16UC1);
        gray16.at<std::uint16_t>(0, 0) = 0;
        gray16.at<std::uint16_t>(0, 1) = 256;
        gray16.at<std::uint16_t>(0, 2) = 512;
        const cv::Mat rgb = dlcv_infer::image_input::NormalizeInferInputImage(gray16, 3);
        if (rgb.type() != CV_8UC3) {
            return fail("16-bit grayscale to RGB type mismatch");
        }
        const cv::Vec3b p0 = rgb.at<cv::Vec3b>(0, 0);
        const cv::Vec3b p1 = rgb.at<cv::Vec3b>(0, 1);
        const cv::Vec3b p2 = rgb.at<cv::Vec3b>(0, 2);
        if (p0 != cv::Vec3b(0, 0, 0) || p1 != cv::Vec3b(1, 1, 1) || p2 != cv::Vec3b(2, 2, 2)) {
            return fail("16-bit grayscale to RGB pixel value mismatch");
        }
    }

    {
        cv::Mat bgra(1, 1, CV_8UC4);
        bgra.at<cv::Vec4b>(0, 0) = cv::Vec4b(10, 20, 30, 200);
        const cv::Mat rgb = dlcv_infer::image_input::NormalizeInferInputImage(bgra, 3);
        if (rgb.type() != CV_8UC3) {
            return fail("BGRA to RGB type mismatch");
        }
        const cv::Vec3b pixel = rgb.at<cv::Vec3b>(0, 0);
        if (pixel != cv::Vec3b(30, 20, 10)) {
            return fail("BGRA to RGB pixel order mismatch");
        }
    }

    {
        cv::Mat rgb(1, 1, CV_8UC3);
        rgb.at<cv::Vec3b>(0, 0) = cv::Vec3b(30, 20, 10);
        cv::Mat expectedGray;
        cv::cvtColor(rgb, expectedGray, cv::COLOR_RGB2GRAY);
        const cv::Mat gray = dlcv_infer::image_input::NormalizeInferInputImage(rgb, 1);
        if (gray.type() != CV_8UC1) {
            return fail("RGB to gray type mismatch");
        }
        if (gray.at<std::uint8_t>(0, 0) != expectedGray.at<std::uint8_t>(0, 0)) {
            return fail("RGB to gray pixel value mismatch");
        }
    }

    PrintUtf8Line("imageprepcheck passed");
    return 0;
}

int RunRectImageCorrectionSelfTest() {
    auto fail = [](const std::string& message) -> int {
        PrintUtf8Line("rect_image_correction selftest failed: " + message);
        return 1;
    };

    using namespace dlcv_infer::flow;
    const auto factory = ModuleRegistry::Get("pre_process/rect_image_correction");
    if (!factory) return fail("module not registered");
    for (const auto& size : { cv::Size(2, 3), cv::Size(3, 2), cv::Size(3, 3) }) {
        for (int setting : { 0, -1, 90, 180, 270 }) {
            cv::Mat image(size, CV_8UC1);
            for (int y = 0; y < size.height; ++y)
                for (int x = 0; x < size.width; ++x) image.at<uchar>(y, x) = static_cast<uchar>(y * 10 + x);
            TransformationState state(size.width + 4, size.height + 3);
            state.AffineMatrix2x3 = { 1,0,-4, 0,1,-3 };
            ModuleImage wrap(image, image, state, 7);
            wrap.AffineImage = image.clone();
            const json properties = {
                {"correction_mode", setting > 0 ? "direction" : "long_edge"},
                {"long_edge_orientation", setting == -1 ? "vertical" : "horizontal"},
                {"rotation_angle", setting > 0 ? setting : 90}
            };
            auto module = factory(1, "", properties, nullptr);
            auto output = module->Process({wrap}, json::array());
            if (output.ImageList.size() != 1 || !output.ResultList.empty()) return fail("channels");
            const auto& child = output.ImageList[0];
            if (child.OriginalIndex != 7 || child.UniqueId != wrap.UniqueId) return fail("identity");
            const int angle = setting > 0 ? setting :
                ((setting == -1 ? size.width > size.height : size.height > size.width) ? 90 : 0);
            if (angle == 0) {
                if (child.ImageObject.data != image.data) return fail("pass-through");
                continue;
            }
            const int flag = angle == 90 ? cv::ROTATE_90_CLOCKWISE : angle == 180 ? cv::ROTATE_180 : cv::ROTATE_90_COUNTERCLOCKWISE;
            cv::Mat expected;
            cv::rotate(image, expected, flag);
            if (child.ImageObject.size() != expected.size() || cv::norm(child.ImageObject, expected, cv::NORM_INF) != 0) return fail("pixels");
            if (child.AffineImage.empty() || cv::norm(child.AffineImage, expected, cv::NORM_INF) != 0) return fail("affine image");
            const auto& a = child.TransformState.AffineMatrix2x3;
            for (int y = 0; y < size.height; ++y) {
                for (int x = 0; x < size.width; ++x) {
                    int xx = static_cast<int>(std::round(a[0]*(x+4) + a[1]*(y+3) + a[2]));
                    int yy = static_cast<int>(std::round(a[3]*(x+4) + a[4]*(y+3) + a[5]));
                    if (child.ImageObject.at<uchar>(yy, xx) != image.at<uchar>(y, x)) return fail("transform");
                }
            }
        }
    }

    const std::string saveDir = BuildTempRectCorrectionDir();
    const std::string suffix = "_rect_image_correction_test";
    DeleteFilesWithSuffix(saveDir, suffix + ".png");

    const std::string flowPath = JoinPathA(saveDir, "rect_image_correction_flow.json");
    json flow = json::object();
    flow["nodes"] = json::array({
        {
            {"id", 1},
            {"order", 1},
            {"type", "input/frontend_image"},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array({101})}}),
                json::object({{"type", "result_chan"}, {"links", json::array({102})}})
            })}
        },
        {
            {"id", 2},
            {"order", 2},
            {"type", "pre_process/rect_image_correction"},
            {"properties", json::object({{"rotate_direction", "clockwise"}})},
            {"inputs", json::array({
                json::object({{"type", "image_chan"}, {"link", 101}}),
                json::object({{"type", "result_chan"}, {"link", 102}})
            })},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array({201})}}),
                json::object({{"type", "result_chan"}, {"links", json::array({202})}})
            })}
        },
        {
            {"id", 3},
            {"order", 3},
            {"type", "output/save_image"},
            {"properties", json::object({{"save_path", saveDir}, {"suffix", suffix}, {"format", "png"}})},
            {"inputs", json::array({
                json::object({{"type", "image_chan"}, {"link", 201}}),
                json::object({{"type", "result_chan"}, {"link", 202}})
            })},
            {"outputs", json::array()}
        }
    });

    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) return fail("cannot write temp flow file");
        ofs << flow.dump(2);
    }

    cv::Mat tall(3, 2, CV_8UC3);
    for (int y = 0; y < tall.rows; ++y) {
        for (int x = 0; x < tall.cols; ++x) {
            tall.at<cv::Vec3b>(y, x) = cv::Vec3b(static_cast<uchar>(10 + x), static_cast<uchar>(20 + y), 30);
        }
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            return fail(std::string("flow load failed: ") + loadReport.dump());
        }
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{tall}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            return fail(std::string("flow infer failed: ") + inferRoot.dump());
        }
    } catch (const std::exception& ex) {
        return fail(std::string("exception: ") + ex.what());
    }

    const cv::Mat saved = LoadSingleFileWithSuffix(saveDir, suffix + ".png");
    if (saved.empty()) {
        return fail("corrected image not saved");
    }
    if (saved.cols != 3 || saved.rows != 2) {
        return fail("portrait image not rotated to landscape");
    }

    DeleteFilesWithSuffix(saveDir, suffix + ".png");
    DeleteFileA(flowPath.c_str());
    PrintUtf8Line("rect_image_correction selftest passed");
    return 0;
}

int CountBBoxDedupDetections(const json& results) {
    if (!results.is_array()) return 0;
    int count = 0;
    for (const auto& entry : results) {
        if (entry.is_object() && entry.contains("sample_results") && entry.at("sample_results").is_array()) {
            count += static_cast<int>(entry.at("sample_results").size());
            continue;
        }
        if (entry.is_object() && entry.contains("bbox")) {
            count += 1;
        }
    }
    return count;
}

json BuildBBoxDedupFlow(bool crossModel) {
    json dedupProps = json::object({{"iou_threshold", 0.5}, {"per_category", true}});
    if (!crossModel) dedupProps["cross_model"] = false;

    return json::object({
        {"nodes", json::array({
            json::object({
                {"id", 1},
                {"order", 1},
                {"type", "input/frontend_image"},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({101})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({102})}})
                })}
            }),
            json::object({
                {"id", 2},
                {"order", 2},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "target"},
                    {"score", 0.99},
                    {"bbox_x1", 10.0},
                    {"bbox_y1", 10.0},
                    {"bbox_x2", 110.0},
                    {"bbox_y2", 110.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({201})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({202})}})
                })}
            }),
            json::object({
                {"id", 3},
                {"order", 3},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "target"},
                    {"score", 0.88},
                    {"bbox_x1", 20.0},
                    {"bbox_y1", 20.0},
                    {"bbox_x2", 100.0},
                    {"bbox_y2", 100.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({301})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({302})}})
                })}
            }),
            json::object({
                {"id", 4},
                {"order", 4},
                {"type", "post_process/merge_results"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 201}}),
                    json::object({{"type", "result_chan"}, {"link", 202}}),
                    json::object({{"type", "image_chan"}, {"link", 301}}),
                    json::object({{"type", "result_chan"}, {"link", 302}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({401})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({402})}})
                })}
            }),
            json::object({
                {"id", 5},
                {"order", 5},
                {"type", "post_process/bbox_iou_dedup"},
                {"properties", dedupProps},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 401}}),
                    json::object({{"type", "result_chan"}, {"link", 402}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({501})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({502})}})
                })}
            }),
            json::object({
                {"id", 6},
                {"order", 6},
                {"type", "output/return_json"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 501}}),
                    json::object({{"type", "result_chan"}, {"link", 502}})
                })},
                {"outputs", json::array()}
            })
        })}
    });
}

json BuildBBoxDedupNoneVsIdentityFlow() {
    const json dedupProps = json::object({
        {"metric", "iou"},
        {"iou_threshold", 0.5},
        {"per_category", true},
        {"cross_model", true}
    });

    return json::object({
        {"nodes", json::array({
            json::object({
                {"id", 1},
                {"order", 1},
                {"type", "input/frontend_image"},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({101})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({102})}})
                })}
            }),
            json::object({
                {"id", 2},
                {"order", 2},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "target"},
                    {"score", 0.99},
                    {"bbox_x", 10.0},
                    {"bbox_y", 10.0},
                    {"bbox_w", 100.0},
                    {"bbox_h", 100.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({201})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({202})}})
                })}
            }),
            json::object({
                {"id", 3},
                {"order", 3},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "target"},
                    {"score", 0.88},
                    {"bbox_x", 20.0},
                    {"bbox_y", 20.0},
                    {"bbox_w", 80.0},
                    {"bbox_h", 80.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({301})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({302})}})
                })}
            }),
            json::object({
                {"id", 4},
                {"order", 4},
                {"type", "post_process/sliding_merge"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 301}}),
                    json::object({{"type", "result_chan"}, {"link", 302}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({401})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({402})}})
                })}
            }),
            json::object({
                {"id", 5},
                {"order", 5},
                {"type", "post_process/merge_results"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 201}}),
                    json::object({{"type", "result_chan"}, {"link", 202}}),
                    json::object({{"type", "image_chan"}, {"link", 401}}),
                    json::object({{"type", "result_chan"}, {"link", 402}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({501})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({502})}})
                })}
            }),
            json::object({
                {"id", 6},
                {"order", 6},
                {"type", "post_process/bbox_iou_dedup"},
                {"properties", dedupProps},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 501}}),
                    json::object({{"type", "result_chan"}, {"link", 502}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({601})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({602})}})
                })}
            }),
            json::object({
                {"id", 7},
                {"order", 7},
                {"type", "output/return_json"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 601}}),
                    json::object({{"type", "result_chan"}, {"link", 602}})
                })},
                {"outputs", json::array()}
            })
        })}
    });
}

bool RunBBoxIoUDedupFlowCase(bool crossModel, int expectedCount, std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, crossModel ? "bbox_dedup_cross.json" : "bbox_dedup_strict.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildBBoxDedupFlow(crossModel).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        cv::Mat image(320, 320, CV_8UC3, cv::Scalar(0, 255, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            error = std::string("flow infer failed: ") + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const json results = inferRoot.contains("result_list") ? inferRoot.at("result_list") : json::array();
        const int kept = CountBBoxDedupDetections(results);
        if (kept != expectedCount) {
            error = std::string(crossModel ? "default cross_model=true" : "cross_model=false") +
                " kept count mismatch, actual=" + std::to_string(kept) +
                ", expected=" + std::to_string(expectedCount) +
                ", root=" + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = std::string("exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

bool RunBBoxIoUDedupNoneVsIdentityCase(std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "bbox_dedup_none_vs_identity.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildBBoxDedupNoneVsIdentityFlow().dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        cv::Mat image(320, 320, CV_8UC3, cv::Scalar(0, 255, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            error = std::string("flow infer failed: ") + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const json results = inferRoot.contains("result_list") ? inferRoot.at("result_list") : json::array();
        const int kept = CountBBoxDedupDetections(results);
        if (kept != 1) {
            error = "null/identity transform not grouped, actual=" + std::to_string(kept) +
                ", expected=1, root=" + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = std::string("exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

int RunBBoxIoUDedupSelfTest() {
    auto fail = [](const std::string& message) -> int {
        PrintUtf8Line("bbox_iou_dedup selftest failed: " + message);
        return 1;
    };

    std::string error;
    if (!RunBBoxIoUDedupFlowCase(true, 1, error)) return fail(error);
    if (!RunBBoxIoUDedupFlowCase(false, 2, error)) return fail(error);
    if (!RunBBoxIoUDedupNoneVsIdentityCase(error)) return fail(error);

    PrintUtf8Line("bbox_iou_dedup selftest passed");
    return 0;
}


int RunPolyFilterSelfTest() {
    auto fail = [](const std::string& message) -> int {
        PrintUtf8Line("poly_filter selftest failed: " + message);
        return 1;
    };

    const auto factory = dlcv_infer::flow::ModuleRegistry::Get("post_process/poly_filter");
    if (!factory) return fail("module is not registered");

    cv::Mat mask = cv::Mat::zeros(8, 9, CV_8UC1);
    const std::vector<cv::Point> contour = {
        cv::Point(1, 1), cv::Point(6, 1), cv::Point(7, 3),
        cv::Point(6, 6), cv::Point(2, 6), cv::Point(1, 4)
    };
    cv::fillPoly(mask, std::vector<std::vector<cv::Point>>{contour}, cv::Scalar(255));
    const json maskInfo = dlcv_infer::flow::MatToMaskInfo(mask);

    auto runCase = [&](const json& caseMaskInfo, const std::string& direction, bool fitLine,
                       int leftClip = 0, int rightClip = 0,
                       double bboxX = 100.0, double bboxY = 200.0,
                       double bboxWidth = 9.0, double bboxHeight = 8.0) -> json {
        const json properties = json::object({
            {"direction", direction},
            {"fit_line", fitLine},
            {"mask_threshold", 127},
            {"left_clip", leftClip},
            {"right_clip", rightClip}
        });
        auto module = factory(40, "poly_filter", properties, nullptr);
        const json input = json::array({
            json::object({
                {"type", "local"},
                {"index", 0},
                {"origin_index", 0},
                {"sample_results", json::array({
                    json::object({
                        {"bbox", json::array({bboxX, bboxY, bboxWidth, bboxHeight})},
                        {"score", 0.99},
                        {"category_name", "demo"},
                        {"mask", "legacy"},
                        {"mask_array", json::array({json::array({255})})},
                        {"mask_rle", caseMaskInfo},
                        {"with_mask", true}
                    })
                })}
            })
        });
        const auto output = module->Process({}, input);
        if (!output.ResultList.is_array() || output.ResultList.empty()) return json();
        const auto& entry = output.ResultList.at(0);
        if (!entry.is_object() || !entry.contains("sample_results") ||
            !entry.at("sample_results").is_array() || entry.at("sample_results").empty()) {
            return json();
        }
        return entry.at("sample_results").at(0);
    };

    for (const std::string direction : {"up", "down", "left", "right"}) {
        const json det = runCase(maskInfo, direction, false);
        if (!det.is_object()) return fail("empty output for " + direction);
        if (!det.contains("bbox") || !det.at("bbox").is_array() || det.at("bbox").size() != 4) {
            return fail("boundary output must keep a four-value bbox for " + direction);
        }
        if (!det.contains("extra_info") || !det.at("extra_info").is_object() ||
            !det.at("extra_info").contains("polyline") ||
            !det.at("extra_info").at("polyline").is_array() ||
            det.at("extra_info").at("polyline").size() < 2) {
            return fail("boundary output is missing extra_info.polyline for " + direction);
        }
        for (const char* key : {"polygon", "poly", "mask", "mask_array", "mask_rle"}) {
            if (det.contains(key)) return fail(std::string("stale geometry remains: ") + key);
        }
        if (!det.contains("with_mask") || det.at("with_mask") != false) {
            return fail("with_mask was not disabled for " + direction);
        }
        if (det.value("metadata", json::object()).value("poly_filter_direction", "") != direction ||
            det.value("metadata", json::object()).value("poly_filter_mode", "") != "boundary_line") {
            return fail("metadata mismatch for " + direction);
        }
    }

    const cv::Mat clipMask(120, 100, CV_8UC1, cv::Scalar(255));
    const json clipMaskInfo = dlcv_infer::flow::MatToMaskInfo(clipMask);
    for (const std::string direction : {"up", "down", "left", "right"}) {
        const json clipped = runCase(clipMaskInfo, direction, false, 25, 10, 0.0, 0.0, 100.0, 120.0);
        if (!clipped.is_object() || !clipped.contains("extra_info") ||
            !clipped.at("extra_info").is_object() ||
            !clipped.at("extra_info").contains("polyline") ||
            !clipped.at("extra_info").at("polyline").is_array() ||
            clipped.at("extra_info").at("polyline").size() < 2) {
            return fail("fixed-pixel clip produced empty output for " + direction);
        }

        const auto& points = clipped.at("extra_info").at("polyline");
        const size_t primaryAxis = direction == "up" || direction == "down" ? 0 : 1;
        double actualMin = std::numeric_limits<double>::max();
        double actualMax = std::numeric_limits<double>::lowest();
        for (const auto& point : points) {
            if (!point.is_array() || point.size() < 2) continue;
            const double value = point.at(primaryAxis).get<double>();
            actualMin = std::min(actualMin, value);
            actualMax = std::max(actualMax, value);
        }
        const double expectedMax = primaryAxis == 0 ? 89.0 : 109.0;
        if (std::abs(actualMin - 25.0) > 1e-6 || std::abs(actualMax - expectedMax) > 1e-6) {
            return fail("fixed-pixel clip range mismatch for " + direction);
        }
    }

    const json clippedFitted = runCase(clipMaskInfo, "right", true, 25, 10, 0.0, 0.0, 100.0, 120.0);
    if (!clippedFitted.is_object() || !clippedFitted.contains("bbox") ||
        !clippedFitted.at("bbox").is_array() || clippedFitted.at("bbox").size() != 5 ||
        std::abs(clippedFitted.at("bbox").at(2).get<double>() - 84.0) > 1e-5 ||
        std::abs(clippedFitted.at("bbox").at(3).get<double>() - 3.0) > 1e-6) {
        return fail("line fit did not use fixed-pixel clipped edge samples");
    }

    const json fitted = runCase(maskInfo, "right", true);
    if (!fitted.is_object() || !fitted.contains("bbox") ||
        !fitted.at("bbox").is_array() || fitted.at("bbox").size() != 5) {
        return fail("line fit must output only a five-value bbox");
    }
    if (std::abs(fitted.at("bbox").at(3).get<double>() - 3.0) > 1e-6) {
        return fail("line-fit RBox height is not 3 pixels");
    }
    if (fitted.contains("polyline") ||
        (fitted.contains("extra_info") && fitted.at("extra_info").is_object() &&
         fitted.at("extra_info").contains("polyline"))) {
        return fail("line fit still contains a polyline");
    }
    if (fitted.value("metadata", json::object()).value("poly_filter_mode", "") != "line_fit_rbox") {
        return fail("line-fit metadata mismatch");
    }

    PrintUtf8Line("poly_filter selftest passed");
    return 0;
}

json BuildCountResultsFlow(const json& properties, int total, bool usePassBranch) {
    const int imageOutputIndex = usePassBranch ? 2 : 4;
    const int resultOutputIndex = imageOutputIndex + 1;
    json countOutputs = json::array();
    for (int i = 0; i < 8; i++) {
        json output = json::object();
        if (i == imageOutputIndex) {
            output["type"] = "image_chan";
            output["links"] = json::array({301});
        } else if (i == resultOutputIndex) {
            output["type"] = "result_chan";
            output["links"] = json::array({302});
        } else if (i == 6) {
            output["name"] = "count";
            output["type"] = "int";
            output["links"] = json::array();
        } else if (i == 7) {
            output["name"] = "ok";
            output["type"] = "bool";
            output["links"] = json::array();
        } else {
            output["type"] = (i % 2 == 0) ? "image_chan" : "result_chan";
            output["links"] = json::array();
        }
        countOutputs.push_back(std::move(output));
    }

    json nodes = json::array({
        json::object({
            {"id", 1},
            {"order", 1},
            {"type", "input/frontend_image"},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array()}}),
                json::object({{"type", "result_chan"}, {"links", json::array()}})
            })}
        })
    });

    json mergeInputs = json::array();
    for (int i = 0; i < total; i++) {
        const int imageInputLink = 100 + i * 2;
        const int resultInputLink = imageInputLink + 1;
        const int imageOutputLink = 200 + i * 2;
        const int resultOutputLink = imageOutputLink + 1;
        nodes[0]["outputs"][0]["links"].push_back(imageInputLink);
        nodes[0]["outputs"][1]["links"].push_back(resultInputLink);
        nodes.push_back(json::object({
            {"id", 2 + i},
            {"order", 2 + i},
            {"type", "input/build_results"},
            {"properties", json::object({
                {"category_id", 1},
                {"category_name", "target"},
                {"score", 0.99},
                {"bbox_x", 10.0 + i},
                {"bbox_y", 10.0 + i},
                {"bbox_w", 20.0},
                {"bbox_h", 20.0}
            })},
            {"inputs", json::array({
                json::object({{"type", "image_chan"}, {"link", imageInputLink}}),
                json::object({{"type", "result_chan"}, {"link", resultInputLink}})
            })},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array({imageOutputLink})}}),
                json::object({{"type", "result_chan"}, {"links", json::array({resultOutputLink})}})
            })}
        }));
        mergeInputs.push_back(json::object({{"type", "image_chan"}, {"link", imageOutputLink}}));
        mergeInputs.push_back(json::object({{"type", "result_chan"}, {"link", resultOutputLink}}));
    }

    nodes.push_back(json::object({
        {"id", 100},
        {"order", 100},
        {"type", "post_process/merge_results"},
        {"inputs", std::move(mergeInputs)},
        {"outputs", json::array({
            json::object({{"type", "image_chan"}, {"links", json::array({901})}}),
            json::object({{"type", "result_chan"}, {"links", json::array({902})}})
        })}
    }));
    nodes.push_back(json::object({
        {"id", 101},
        {"order", 101},
        {"type", "post_process/count_results"},
        {"properties", properties},
        {"inputs", json::array({
            json::object({{"type", "image_chan"}, {"link", 901}}),
            json::object({{"type", "result_chan"}, {"link", 902}})
        })},
        {"outputs", std::move(countOutputs)}
    }));
    nodes.push_back(json::object({
        {"id", 102},
        {"order", 102},
        {"type", "output/return_json"},
        {"inputs", json::array({
            json::object({{"type", "image_chan"}, {"link", 301}}),
            json::object({{"type", "result_chan"}, {"link", 302}})
        })},
        {"outputs", json::array()}
    }));
    return json::object({{"nodes", std::move(nodes)}});
}

bool RunCountResultsFlowCase(const json& properties, int total, bool expectedOk, std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "count_results.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildCountResultsFlow(properties, total, expectedOk).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        cv::Mat image(64, 64, CV_8UC3, cv::Scalar(0, 255, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            error = std::string("flow infer failed: ") + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const json results = inferRoot.contains("result_list") ? inferRoot.at("result_list") : json::array();
        const int branchCount = CountBBoxDedupDetections(results);
        if (branchCount != total) {
            error = "count_results branch mismatch, actual=" + std::to_string(branchCount) +
                ", expected=" + std::to_string(total) + ", root=" + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = std::string("exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

bool RunCountResultsInvalidRangeCase(std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "count_results_invalid.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildCountResultsFlow(
            json::object({{"only_local", true}, {"min_count", 3}, {"max_count", 2}}),
            2,
            true).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
        cv::Mat image(64, 64, CV_8UC3, cv::Scalar(0, 255, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image, image}, json::object());
        if (inferRoot.is_object() && inferRoot.value("code", 0) == 0) {
            error = "min_count > max_count did not fail: " + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (...) {
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

int RunCountResultsSelfTest() {
    auto fail = [](const std::string& message) -> int {
        PrintUtf8Line("count_results selftest failed: " + message);
        return 1;
    };

    const std::vector<std::tuple<json, int, bool>> cases = {
        {json::object(), 1, true},
        {json::object({{"only_local", true}, {"min_count", 2}, {"max_count", 4}}), 2, true},
        {json::object({{"only_local", true}, {"min_count", 2}, {"max_count", 4}}), 4, true},
        {json::object({{"only_local", true}, {"min_count", 2}, {"max_count", 2}}), 2, true},
        {json::object({{"only_local", true}, {"min_count", 2}, {"max_count", 4}}), 1, false},
        {json::object({{"only_local", true}, {"count_type", "equal"}, {"only_count", 2}}), 2, true},
        {json::object({{"only_local", true}, {"count_type", "greater"}, {"min_count", 2}}), 2, false},
        {json::object({{"only_local", true}, {"count_type", "greater"}, {"min_count", 2}}), 3, true},
        {json::object({{"only_local", true}, {"count_type", "less"}, {"max_count", 2}}), 2, false},
        {json::object({{"only_local", true}, {"count_type", "less"}, {"max_count", 2}}), 1, true},
        {json::object({{"only_local", true}, {"count_type", "legacy_unknown"}, {"min_count", 2}}), 2, true},
        {json::object({{"only_local", true}, {"only_count", 99}, {"min_count", 2}}), 3, true}
    };

    std::string error;
    for (const auto& testCase : cases) {
        if (!RunCountResultsFlowCase(
                std::get<0>(testCase),
                std::get<1>(testCase),
                std::get<2>(testCase),
                error)) {
            return fail(error);
        }
    }
    if (!RunCountResultsInvalidRangeCase(error)) return fail(error);

    PrintUtf8Line("count_results selftest passed");
    return 0;
}


json BuildCategoryCountCheckFlow(const json& rules, int total) {
    json nodes = json::array({
        json::object({
            {"id", 1},
            {"order", 1},
            {"type", "input/frontend_image"},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array()}}),
                json::object({{"type", "result_chan"}, {"links", json::array()}})
            })}
        })
    });

    json mergeInputs = json::array();
    for (int i = 0; i < total; ++i) {
        const int imageInputLink = 100 + i * 2;
        const int resultInputLink = imageInputLink + 1;
        const int imageOutputLink = 200 + i * 2;
        const int resultOutputLink = imageOutputLink + 1;
        nodes[0]["outputs"][0]["links"].push_back(imageInputLink);
        nodes[0]["outputs"][1]["links"].push_back(resultInputLink);
        nodes.push_back(json::object({
            {"id", 2 + i},
            {"order", 2 + i},
            {"type", "input/build_results"},
            {"properties", json::object({
                {"category_id", 1},
                {"category_name", "黑块"},
                {"score", 0.99},
                {"bbox_x", 10.0 + i * 30.0},
                {"bbox_y", 10.0},
                {"bbox_w", 20.0},
                {"bbox_h", 20.0}
            })},
            {"inputs", json::array({
                json::object({{"type", "image_chan"}, {"link", imageInputLink}}),
                json::object({{"type", "result_chan"}, {"link", resultInputLink}})
            })},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array({imageOutputLink})}}),
                json::object({{"type", "result_chan"}, {"links", json::array({resultOutputLink})}})
            })}
        }));
        mergeInputs.push_back(json::object({{"type", "image_chan"}, {"link", imageOutputLink}}));
        mergeInputs.push_back(json::object({{"type", "result_chan"}, {"link", resultOutputLink}}));
    }

    nodes.push_back(json::object({
        {"id", 100},
        {"order", 100},
        {"type", "post_process/merge_results"},
        {"inputs", std::move(mergeInputs)},
        {"outputs", json::array({
            json::object({{"type", "image_chan"}, {"links", json::array({901})}}),
            json::object({{"type", "result_chan"}, {"links", json::array({902})}})
        })}
    }));
    nodes.push_back(json::object({
        {"id", 101},
        {"order", 101},
        {"type", "post_process/category_count_check"},
        {"properties", json::object({{"rules", rules}})},
        {"inputs", json::array({
            json::object({{"type", "image_chan"}, {"link", 901}}),
            json::object({{"type", "result_chan"}, {"link", 902}})
        })},
        {"outputs", json::array({
            json::object({{"type", "image_chan"}, {"links", json::array({301})}}),
            json::object({{"type", "result_chan"}, {"links", json::array({302})}}),
            json::object({{"name", "ok"}, {"type", "bool"}, {"links", json::array()}}),
            json::object({{"name", "reason"}, {"type", "string"}, {"links", json::array()}})
        })}
    }));
    nodes.push_back(json::object({
        {"id", 102},
        {"order", 102},
        {"type", "output/return_json"},
        {"inputs", json::array({
            json::object({{"type", "image_chan"}, {"link", 301}}),
            json::object({{"type", "result_chan"}, {"link", 302}})
        })},
        {"outputs", json::array()}
    }));
    return json::object({{"nodes", std::move(nodes)}});
}

bool ReadCategoryCheckStatus(
    const json& root,
    size_t sampleIndex,
    bool& ok,
    std::vector<std::string>& reasons) {
    const json* statusToken = &root;
    try {
        if (!(root.contains("ok") && root.at("ok").is_boolean())) {
            if (!root.contains("result_list") || !root.at("result_list").is_array() ||
                sampleIndex >= root.at("result_list").size()) return false;
            statusToken = &root.at("result_list").at(sampleIndex);
        }
        if (!statusToken->is_object() || !statusToken->contains("ok") ||
            !statusToken->at("ok").is_boolean()) return false;
        ok = statusToken->at("ok").get<bool>();
        reasons.clear();
        if (statusToken->contains("reason") && statusToken->at("reason").is_array()) {
            for (const auto& reason : statusToken->at("reason")) {
                if (reason.is_string()) reasons.push_back(reason.get<std::string>());
            }
        }
        return true;
    } catch (...) {
        return false;
    }
}

bool RunCategoryCountCheckFlowCase(
    const json& rules,
    int total,
    int imageCount,
    bool expectedOk,
    const std::string& expectedReason,
    std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "category_count_check.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildCategoryCountCheckFlow(rules, total).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const cv::Mat image(64, 320, CV_8UC3, cv::Scalar(0, 255, 0));
        std::vector<cv::Mat> images(static_cast<size_t>(imageCount), image);
        const json root = model.InferInternal(images, json::object());
        if (!root.is_object() || root.value("code", 1) != 0) {
            error = std::string("flow infer failed: ") + root.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        for (int i = 0; i < imageCount; ++i) {
            bool ok = false;
            std::vector<std::string> reasons;
            if (!ReadCategoryCheckStatus(root, static_cast<size_t>(i), ok, reasons)) {
                error = "missing inspection status: " + root.dump();
                DeleteFileA(flowPath.c_str());
                return false;
            }
            if (ok != expectedOk) {
                error = "inspection ok mismatch: " + root.dump();
                DeleteFileA(flowPath.c_str());
                return false;
            }
            if (expectedReason.empty()) {
                if (!reasons.empty()) {
                    error = "unexpected inspection reason: " + root.dump();
                    DeleteFileA(flowPath.c_str());
                    return false;
                }
            } else if (reasons.size() != 1 || reasons.front() != expectedReason) {
                error = "inspection reason mismatch: " + root.dump();
                DeleteFileA(flowPath.c_str());
                return false;
            }
        }

        if (imageCount == 1) {
            const json oneOut = model.InferOneOutJson(image, json::object());
            if (!oneOut.is_object() || oneOut.value("ok", !expectedOk) != expectedOk ||
                !oneOut.contains("result_list") || !oneOut.at("result_list").is_array()) {
                error = "InferOneOutJson wrapper mismatch: " + oneOut.dump();
                DeleteFileA(flowPath.c_str());
                return false;
            }
        }
    } catch (const std::exception& ex) {
        error = std::string("exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

bool RunCategoryCountCheckLegacyCompatibilityCase(std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "category_count_check_legacy.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write legacy temp flow file";
            return false;
        }
        ofs << BuildCountResultsFlow(json::object(), 1, true).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("legacy flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
        const cv::Mat image(64, 64, CV_8UC3, cv::Scalar(0, 255, 0));
        const json root = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (root.contains("ok") || root.contains("reason")) {
            error = "legacy root unexpectedly contains inspection status: " + root.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
        const json oneOut = model.InferOneOutJson(image, json::object());
        if (!oneOut.is_array()) {
            error = "legacy InferOneOutJson is not array: " + oneOut.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = std::string("legacy exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

int RunCategoryCountCheckSelfTest() {
    auto fail = [](const std::string& message) -> int {
        PrintUtf8Line("category_count_check selftest failed: " + message);
        return 1;
    };

    const json equalOne = json::array({
        json::object({{"category", "黑块"}, {"operator", "equal"}, {"expect", 1}})
    });
    const json equalEight = json::array({
        json::object({{"category", "黑块"}, {"operator", "equal"}, {"expect", 8}})
    });
    const std::string countOneReason = "类别黑块期望=8,实际1";
    std::string error;
    if (!RunCategoryCountCheckFlowCase(equalOne, 1, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(equalEight, 7, 1, false, countOneReason, error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(
            json::array({json::object({{"category", "黑块"}, {"operator", "gt"}, {"expect", 0}})}),
            2, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(
            json::array({json::object({{"category", "黑块"}, {"operator", "lt"}, {"expect", 2}})}),
            1, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(
            json::array({json::object({{"category", ""}, {"operator", "equal"}, {"expect", 1}})}),
            2, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(equalOne.dump(), 1, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(
            json::array({json::object({{"category", "黑块"}, {"operator", "invalid"}, {"expect", 1}})}),
            1, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(
            json::array({json::object({{"category", "黑块"}, {"operator", "equal"}, {"expect", "bad"}})}),
            1, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckLegacyCompatibilityCase(error)) return fail(error);

    PrintUtf8Line("category_count_check selftest passed");
    return 0;
}

json BuildImageGenerationExpandFlow(const std::string& saveDir,
                                    const std::string& suffix,
                                    const json& cropProperties,
                                    const json& resultProperties) {
    return json::object({
        {"nodes", json::array({
            json::object({
                {"id", 1},
                {"order", 1},
                {"type", "input/frontend_image"},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({101})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({102})}})
                })}
            }),
            json::object({
                {"id", 2},
                {"order", 2},
                {"type", "input/build_results"},
                {"properties", resultProperties},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({201})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({202})}})
                })}
            }),
            json::object({
                {"id", 3},
                {"order", 3},
                {"type", "features/image_generation"},
                {"properties", cropProperties},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 201}}),
                    json::object({{"type", "result_chan"}, {"link", 202}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({301})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({302})}})
                })}
            }),
            json::object({
                {"id", 4},
                {"order", 4},
                {"type", "output/save_image"},
                {"properties", json::object({{"save_path", saveDir}, {"suffix", suffix}, {"format", "png"}})},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 301}}),
                    json::object({{"type", "result_chan"}, {"link", 302}})
                })},
                {"outputs", json::array()}
            })
        })}
    });
}

bool AssertImageGenerationCrop(const std::string& caseName,
                               const std::string& tempDir,
                               const json& cropProperties,
                               const json& resultProperties,
                               int expectedWidth,
                               int expectedHeight,
                               std::string& error,
                               int imageWidth = 200,
                               int imageHeight = 200) {
    const std::string suffix = "_image_generation_expand_" + std::to_string(std::hash<std::string>{}(caseName));
    const std::string flowPath = JoinPathA(tempDir, suffix + ".json");
    DeleteFilesWithSuffix(tempDir, suffix + ".png");

    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = caseName + " cannot write temp flow file";
            return false;
        }
        ofs << BuildImageGenerationExpandFlow(tempDir, suffix, cropProperties, resultProperties).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = caseName + " flow load failed: " + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        cv::Mat image(imageHeight, imageWidth, CV_8UC3, cv::Scalar(0, 0, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            error = caseName + " flow infer failed: " + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = caseName + " exception: " + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    const cv::Mat saved = LoadSingleFileWithSuffix(tempDir, suffix + ".png");
    DeleteFilesWithSuffix(tempDir, suffix + ".png");
    DeleteFileA(flowPath.c_str());
    if (saved.empty()) {
        error = caseName + " cropped image not saved";
        return false;
    }

    if (saved.cols != expectedWidth || saved.rows != expectedHeight) {
        error = caseName + " crop size mismatch, actual=" + std::to_string(saved.cols) + "x" +
                std::to_string(saved.rows) + ", expected=" + std::to_string(expectedWidth) +
                "x" + std::to_string(expectedHeight);
        return false;
    }

    return true;
}

bool RunImageGenerationExpandRegression(std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const json axisResultProps = json::object({
        {"category_id", 1},
        {"category_name", "target"},
        {"score", 0.99},
        {"bbox_x", 50.0},
        {"bbox_y", 60.0},
        {"bbox_w", 40.0},
        {"bbox_h", 20.0}
    });

    if (!AssertImageGenerationCrop(
            "pixel_expand",
            tempDir,
            json::object({{"crop_expand", 5}, {"crop_shape", json::array()}, {"min_size", 1}}),
            axisResultProps,
            50,
            30,
            error)) {
        return false;
    }

    if (!AssertImageGenerationCrop(
            "percent_expand_axis",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 10}, {"crop_shape", json::array()}, {"min_size", 1}}),
            axisResultProps,
            48,
            24,
            error)) {
        return false;
    }

    if (!AssertImageGenerationCrop(
            "percent_no_round_to_32",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 50}, {"crop_shape", json::array()}, {"min_size", 1}}),
            axisResultProps,
            80,
            40,
            error)) {
        return false;
    }

    const json largeAxisResultProps = json::object({
        {"category_id", 1},
        {"category_name", "target"},
        {"score", 0.99},
        {"bbox_x", 50.0},
        {"bbox_y", 50.0},
        {"bbox_w", 200.0},
        {"bbox_h", 200.0}
    });
    if (!AssertImageGenerationCrop(
            "percent_default_pixel_limit",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 20}, {"crop_shape", json::array()}, {"min_size", 1}}),
            largeAxisResultProps,
            264,
            264,
            error,
            320,
            320)) {
        return false;
    }

    if (!AssertImageGenerationCrop(
            "percent_custom_pixel_limit",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 20}, {"crop_expand_percent_limit", 10}, {"crop_shape", json::array()}, {"min_size", 1}}),
            largeAxisResultProps,
            220,
            220,
            error,
            320,
            320)) {
        return false;
    }

    if (!AssertImageGenerationCrop(
            "fixed_size_priority",
            tempDir,
            json::object({{"crop_expand", 5}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 10}, {"crop_shape", json::array({30, 25})}, {"min_size", 1}}),
            axisResultProps,
            30,
            25,
            error)) {
        return false;
    }

    const json rotatedResultProps = json::object({
        {"category_id", 1},
        {"category_name", "target"},
        {"score", 0.99},
        {"bbox_cx", 100.0},
        {"bbox_cy", 100.0},
        {"bbox_w", 40.0},
        {"bbox_h", 20.0},
        {"with_angle", true},
        {"angle", 0.0}
    });
    if (!AssertImageGenerationCrop(
            "percent_expand_rotated",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 10}, {"crop_shape", json::array()}, {"min_size", 1}}),
            rotatedResultProps,
            48,
            24,
            error)) {
        return false;
    }

    const json largeRotatedResultProps = json::object({
        {"category_id", 1},
        {"category_name", "target"},
        {"score", 0.99},
        {"bbox_cx", 160.0},
        {"bbox_cy", 160.0},
        {"bbox_w", 200.0},
        {"bbox_h", 200.0},
        {"with_angle", true},
        {"angle", 0.0}
    });
    if (!AssertImageGenerationCrop(
            "rotated_percent_default_pixel_limit",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 20}, {"crop_shape", json::array()}, {"min_size", 1}}),
            largeRotatedResultProps,
            264,
            264,
            error,
            320,
            320)) {
        return false;
    }

    return true;
}

int RunImageGenerationExpandSelfTest() {
    PrintUtf8Line("==== image_generation expand selftest ====");
    std::string error;
    if (!RunImageGenerationExpandRegression(error)) {
        PrintUtf8Line("image_generation expand selftest failed: " + error);
        return 1;
    }

    PrintUtf8Line("image_generation expand selftest passed");
    return 0;
}

json BuildCrossModelLabelMergeFlow() {
    return json::object({
        {"nodes", json::array({
            json::object({
                {"id", 1},
                {"order", 1},
                {"type", "input/frontend_image"},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({101})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({102})}})
                })}
            }),
            json::object({
                {"id", 2},
                {"order", 2},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "base"},
                    {"score", 0.99},
                    {"bbox_x", 50.0},
                    {"bbox_y", 50.0},
                    {"bbox_w", 40.0},
                    {"bbox_h", 20.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({201})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({202})}})
                })}
            }),
            json::object({
                {"id", 3},
                {"order", 3},
                {"type", "features/image_generation"},
                {"properties", json::object({{"crop_expand", 0}, {"min_size", 1}})},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 201}}),
                    json::object({{"type", "result_chan"}, {"link", 202}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({301})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({302})}})
                })}
            }),
            json::object({
                {"id", 4},
                {"order", 4},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "suffix"},
                    {"score", 0.99},
                    {"bbox_x", 50.0},
                    {"bbox_y", 50.0},
                    {"bbox_w", 40.0},
                    {"bbox_h", 20.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({401})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({402})}})
                })}
            }),
            json::object({
                {"id", 5},
                {"order", 5},
                {"type", "post_process/cross_model_label_merge"},
                {"properties", json::object({{"fixed_text", "-"}})},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 301}}),
                    json::object({{"type", "result_chan"}, {"link", 302}}),
                    json::object({{"type", "image_chan"}, {"link", 301}}),
                    json::object({{"type", "result_chan"}, {"link", 402}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({501})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({502})}})
                })}
            }),
            json::object({
                {"id", 6},
                {"order", 6},
                {"type", "output/return_json"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 501}}),
                    json::object({{"type", "result_chan"}, {"link", 502}})
                })},
                {"outputs", json::array()}
            })
        })}
    });
}

bool RunCrossModelLabelMergeCase(std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "cross_model_label_merge.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildCrossModelLabelMergeFlow().dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        cv::Mat image(200, 200, CV_8UC3, cv::Scalar(0, 0, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            error = std::string("flow infer failed: ") + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const json results = inferRoot.contains("result_list") ? inferRoot.at("result_list") : json::array();
        if (!results.is_array() || results.size() != 1) {
            error = "result count mismatch, actual=" + std::to_string(results.size()) + ", expected=1, root=" + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const json& det = results.at(0);
        if (!det.is_object() || !det.contains("category_name") || !det.at("category_name").is_string()) {
            error = "detection result missing category_name, det=" + det.dump() + ", root=" + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const std::string cat = det.at("category_name").get<std::string>();
        if (cat != "base-suffix") {
            error = "merged label mismatch, actual=" + cat + ", expected=base-suffix";
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = std::string("exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

int RunCrossModelLabelMergeSelfTest() {
    std::string error;
    if (!RunCrossModelLabelMergeCase(error)) {
        PrintUtf8Line("cross_model_label_merge selftest failed: " + error);
        return 1;
    }
    PrintUtf8Line("cross_model_label_merge selftest passed");
    return 0;
}

int RunThreeModelLoadTiming(int argc, wchar_t* argv[]) {
    if (argc != 5) {
        PrintUtf8Line("用法: dlcv_infer_cpp_test.exe load-three-models <元件提取模型> <元件检测模型> <IC检测模型>");
        return 2;
    }

    struct ModelSpec {
        const char* name;
        std::wstring path;
    };

    const std::vector<ModelSpec> specs = {
        {"元件提取模型", argv[2]},
        {"元件检测模型", argv[3]},
        {"IC检测模型", argv[4]},
    };

    std::vector<std::unique_ptr<dlcv_infer::Model>> models;
    models.reserve(specs.size());
    double totalSeconds = 0.0;

    try {
        for (const auto& spec : specs) {
            PrintUtf8Line(std::string("开始加载") + spec.name + ": " + WideToUtf8(spec.path));
            std::cout << std::flush;
            const auto start = Clock::now();
            auto model = std::make_unique<dlcv_infer::Model>(spec.path, 0);
            const double elapsedSeconds = std::chrono::duration<double>(Clock::now() - start).count();
            totalSeconds += elapsedSeconds;
            PrintUtf8Line(std::string(spec.name) + "加载完成，耗时 " + ToFixed(elapsedSeconds, 2) + " 秒");
            std::cout << std::flush;
            models.push_back(std::move(model));
        }

        PrintUtf8Line("三个模型加载完成，总耗时 " + ToFixed(totalSeconds, 2) + " 秒");
        std::cout << std::flush;
        return 0;
    } catch (const std::exception& ex) {
        PrintUtf8Line(std::string("模型加载失败: ") + ex.what());
        std::cout << std::flush;
        return 1;
    }
}

int RunForegroundBackgroundStatisticsSelfTest() {
    using namespace dlcv_infer::flow;
    try {
        const auto factory = ModuleRegistry::Get("post_process/foreground_background_statistics");
        if (!factory) throw std::runtime_error("统计节点未注册");
        auto check = [](bool condition, const std::string& message) {
            if (!condition) throw std::runtime_error(message);
        };
        auto number = [&](const json& value, double expected, const std::string& message) {
            check(value.is_number() && std::abs(value.get<double>() - expected) < 1e-9, message);
        };
        auto makeImage = [](const cv::Mat& image, int origin = 0) {
            return ModuleImage(image, image, TransformationState(image.cols, image.rows), origin);
        };
        auto detection = [](const cv::Mat& mask, const json& bbox) {
            return json{{"bbox", bbox}, {"mask_rle", MatToMaskInfo(mask)},
                {"category_name", "目标"}, {"score", 0.75}, {"metadata", {{"note", "保留"}}}};
        };
        auto entries = [](const json& item) {
            return json::array({{{"type", "local"}, {"index", 0}, {"origin_index", 0},
                {"sample_results", json::array({item})}}});
        };
        auto run = [&](const std::vector<ModuleImage>& images, const json& input, const json& properties) {
            auto module = factory(2033, "前景背景统计", properties, nullptr);
            return module->Process(images, input);
        };
        const json both{{"mean", true}, {"median", true}};
        const cv::Mat gray = (cv::Mat_<unsigned char>(2, 3) << 2, 4, 20, 6, 8, 60);
        const cv::Mat mask = (cv::Mat_<unsigned char>(2, 3) << 255, 255, 0, 255, 255, 0);
        const auto inputFactory = ModuleRegistry::Get("input/image");
        const auto cropFactory = ModuleRegistry::Get("pre_process/coordinate_crop");
        const auto flipFactory = ModuleRegistry::Get("pre_process/image_flip");
        check(inputFactory && cropFactory && flipFactory, "真实输入、裁剪或翻转节点未注册");
        const std::string flipPath = JoinPathA(BuildTempRectCorrectionDir(), "statistics-flip.png");
        for (bool cropped : {false, true})
        for (const auto& directions : std::vector<std::vector<std::string>>{{}, {"水平"}, {"竖直"}, {"水平", "竖直"}}) {
            const cv::Mat source = (cv::Mat_<unsigned char>(2, 3) << 1, 10, 100, 2, 20, 200);
            cv::Mat expected;
            cv::cvtColor(source, expected, cv::COLOR_GRAY2RGB);
            cv::Mat canvas(4, 5, CV_8UC3, cv::Scalar::all(240));
            expected.copyTo(canvas(cv::Rect(1, 1, 3, 2)));
            check(cv::imwrite(flipPath, cropped ? canvas : expected), "PNG 写入失败");
            auto inputModule = inputFactory(1, "PNG", json{{"path", flipPath}}, nullptr);
            auto* input = dynamic_cast<BaseInputModule*>(inputModule.get());
            check(input != nullptr, "输入节点不是实际输入模块");
            auto current = input->Generate();
            check(current.ImageList.size() == 1 && current.ImageList[0].ImageObject.type() == CV_8UC3,
                "PNG 未经过真实三通道输入模块");
            if (cropped) {
                auto crop = cropFactory(2, "裁剪", json{{"x", 1}, {"y", 1}, {"w", 3}, {"h", 2}}, nullptr);
                current = crop->Process(current.ImageList, current.ResultList);
            }
            for (const auto& direction : directions) {
                auto flip = flipFactory(3, "翻转", json{{"direction", direction}}, nullptr);
                current = flip->Process(current.ImageList, current.ResultList);
                cv::flip(expected, expected, direction == "水平" ? 1 : 0);
            }
            check(current.ImageList.size() == 1, "裁剪翻转丢失图像");
            const auto& image = current.ImageList[0];
            check(image.ImageObject.size() == expected.size() &&
                cv::norm(image.ImageObject, expected, cv::NORM_INF) == 0, "实际裁剪翻转像素错误");
            for (bool mixed : {false, true}) {
                cv::Mat sampleMask(2, 3, CV_8UC1, cv::Scalar::all(255));
                if (mixed) cv::inRange(image.ImageObject, cv::Scalar::all(0), cv::Scalar::all(20), sampleMask);
                auto inputResults = entries(detection(sampleMask, json::array({0, 0, 3, 2})));
                inputResults[0]["transform"] = image.TransformState.ToJson();
                const auto output = run(current.ImageList, inputResults, both);
                const auto& item = output.ResultList[0]["sample_results"][0];
                number(item.at("foreground_mean"), mixed ? 8.25 : 55.5, "实际翻转前景均值错误");
                number(item.at("foreground_median"), mixed ? 6 : 15, "实际翻转前景中值错误");
                if (mixed) {
                    number(item.at("background_mean"), 150, "实际翻转背景均值错误");
                    number(item.at("background_median"), 150, "实际翻转背景中值错误");
                } else {
                    check(item.at("background_mean").is_null() && item.at("background_median").is_null(),
                        "空背景未返回 null");
                }
            }
        }
        std::remove(flipPath.c_str());
        const std::pair<std::string, std::string> ports{"image_chan", "result_chan"};
        json nodes = json::array({
            {{"id", 1}, {"type", "input/frontend_image"}, {"outputs", {
                {{"type", ports.first}, {"links", {1}}},
                {{"type", ports.second}, {"links", {2}}}}}},
            {{"id", 2}, {"type", "input/build_results"},
                {"properties", {{"bbox_x", 0}, {"bbox_y", 0}, {"bbox_w", 3}, {"bbox_h", 2}}},
                {"inputs", {{{"type", ports.first}, {"link", 1}}, {{"type", ports.second}, {"link", 2}}}},
                {"outputs", {{{"type", ports.first}, {"links", {7}}}, {{"type", ports.second}, {"links", {8}}}}}},
            {{"id", 3}, {"type", "post_process/foreground_background_statistics"}, {"properties", both},
                {"inputs", {{{"type", ports.first}, {"link", 7}}, {{"type", ports.second}, {"link", 8}}}},
                {"outputs", {{{"type", ports.first}, {"links", {3, 5}}},
                             {{"type", ports.second}, {"links", {4, 6}}}}}}
        });
        for (int id : {4, 5}) {
            nodes.push_back({{"id", id}, {"type", id == 4 ? "output/preview" : "output/return_json"},
                {"inputs", {{{"type", ports.first}, {"link", id == 4 ? 3 : 5}},
                            {{"type", ports.second}, {"link", id == 4 ? 4 : 6}}}}});
        }
        const std::string path = JoinPathA(BuildTempRectCorrectionDir(), "statistics-port-" + ports.first + ".json");
        {
            std::ofstream file(path, std::ios::binary);
            check(static_cast<bool>(file), "无法保存临时端口回归流程");
            file << json{{"nodes", nodes}}.dump();
        }
        FlowGraphModel graph;
        check(graph.Load(path, -1).value("code", 1) == 0, "端口回归流程加载失败");
        const auto output = graph.InferInternal({gray});
        const auto& targets = output.at("result_list");
        check(targets.size() == 1, ports.second + " 结果路由丢失");
        const auto& item = targets[0];
        check(item.at("with_mean") == false && item.at("with_median") == false &&
            item.at("foreground_mean").is_null() && item.at("background_mean").is_null() &&
            item.at("foreground_median").is_null() && item.at("background_median").is_null(),
            "缺掩码目标的统计路由或 null 输出错误");
        std::remove(path.c_str());
        const cv::Mat savedImage = gray.clone();
        std::vector<ModuleImage> images{makeImage(gray)};
        images[0].UniqueId = "statistics-image";
        images[0].SlidingMeta.Valid = true;
        images[0].SlidingMeta.W = 3;
        images[0].AffineImage = gray;
        json input = entries(detection(mask, {0, 0, 3, 2}));
        for (const char* key : {"with_mean", "foreground_mean", "background_mean", "with_median", "foreground_median", "background_median"}) {
            input[0]["sample_results"][0][key] = 999;
        }
        const json savedInput = input;
        for (bool mean : {false, true}) {
            for (bool median : {false, true}) {
                const ModuleIO out = run(images, input, {{"mean", mean}, {"median", median}});
                const auto& item = out.ResultList[0]["sample_results"][0];
                for (const char* name : {"mean", "median"}) {
                    const bool enabled = std::string(name) == "mean" ? mean : median;
                    const std::string flag = "with_" + std::string(name);
                    check(item.contains(flag) == enabled, flag + " 开关错误");
                    for (const char* region : {"foreground_", "background_"}) {
                        const std::string field = std::string(region) + name;
                        check(item.contains(field) == enabled, field + " 关闭后未删除");
                        if (enabled) number(item.at(field), std::string(region) == "foreground_" ? 5 : 40, field);
                    }
                    if (enabled) check(item.at(flag) == true, flag + " 有采样时未置 true");
                }
                json restored = out.ResultList;
                for (const char* key : {"with_mean", "foreground_mean", "background_mean", "with_median", "foreground_median", "background_median"}) {
                    restored[0]["sample_results"][0][key] = 999;
                }
                check(restored == input, "目标或 entry 其他属性改变");
                check(out.ImageList.size() == 1 && out.ImageList[0].ToMeta() == images[0].ToMeta() &&
                    out.ImageList[0].SlidingMeta.ToJson() == images[0].SlidingMeta.ToJson() &&
                    out.ImageList[0].ImageObject.data == gray.data && out.ImageList[0].AffineImage.data == gray.data,
                    "图像及包装属性改变");
            }
        }
        const auto defaults = run(images, input, json::object()).ResultList[0]["sample_results"][0];
        number(defaults.at("foreground_mean"), 5, "默认均值");
        check(!defaults.contains("with_median"), "默认中值未关闭");
        const auto enabled = run(images, input, both);
        const auto disabled = run(images, enabled.ResultList, {{"mean", false}, {"median", false}});
        const auto repeated = run(images, disabled.ResultList, {{"mean", false}, {"median", true}});
        const auto& repeatedItem = repeated.ResultList[0]["sample_results"][0];
        check(!repeatedItem.contains("with_mean") && !repeatedItem.contains("foreground_mean") &&
            !repeatedItem.contains("background_mean"), "重复执行残留旧均值");
        number(repeatedItem.at("foreground_median"), 5, "重复执行中值");
        check(input == savedInput && cv::norm(gray, savedImage, cv::NORM_INF) == 0, "输入被修改");

        cv::Mat color(1, 2, CV_8UC3);
        color.at<cv::Vec3b>(0, 0) = cv::Vec3b(1, 5, 9);
        color.at<cv::Vec3b>(0, 1) = cv::Vec3b(2, 8, 20);
        const cv::Mat twoMask = (cv::Mat_<unsigned char>(1, 2) << 255, 0);
        const auto colorOut = run({makeImage(color)}, entries(detection(twoMask, {0, 0, 2, 1})), both);
        const auto& colorItem = colorOut.ResultList[0]["sample_results"][0];
        number(colorItem.at("foreground_mean"), 5, "彩色前景均值不是共同标量");
        number(colorItem.at("foreground_median"), 5, "彩色前景中值");
        number(colorItem.at("background_mean"), 10, "彩色背景均值");
        number(colorItem.at("background_median"), 8, "彩色背景中值");

        for (const auto& invalidMask : {json(), json::object(), json{{"width", 0}, {"height", 1}, {"runs", {0}}},
            json{{"width", 3}, {"height", 2}, {"runs", {1}}}, json{{"width", 3}, {"height", 2}, {"runs", {-1, 7}}},
            json{{"width", 3}, {"height", 2}, {"runs", {7}}}, json{{"width", 3}, {"height", 2}, {"runs", {6.0}}}}) {
            json invalid = input;
            invalid[0]["sample_results"][0]["mask_rle"] = invalidMask;
            const auto out = run(images, invalid, both);
            const auto& item = out.ResultList[0]["sample_results"][0];
            check(item.at("with_mean") == false && item.at("with_median") == false &&
                item.at("foreground_mean").is_null() && item.at("background_mean").is_null() &&
                item.at("foreground_median").is_null() && item.at("background_median").is_null(), "无效 mask 未置 false/null");
        }
        for (bool foreground : {false, true}) {
            const cv::Mat fullMask(gray.size(), CV_8UC1, cv::Scalar(foreground ? 255 : 0));
            const auto out = run(images, entries(detection(fullMask, {0, 0, 3, 2})), both);
            const auto& item = out.ResultList[0]["sample_results"][0];
            const std::string populated = foreground ? "foreground_" : "background_";
            const std::string empty = foreground ? "background_" : "foreground_";
            check(item.at("with_mean") == true && item.at("with_median") == true, "单侧采样标志错误");
            number(item.at(populated + "mean"), 50.0 / 3, "单侧均值");
            number(item.at(populated + "median"), 7, "偶数中值应为 6 和 8 的平均");
            check(item.at(empty + "mean").is_null() && item.at(empty + "median").is_null(), "空一侧未写 null");
        }
        for (const auto& bbox : {json{0, 0, 0, 2}, json{10, 10, 1, 1}, json::array(), json{0, 0, -1, 2}}) {
            const auto out = run(images, entries(detection(mask, bbox)), both);
            const auto& item = out.ResultList[0]["sample_results"][0];
            check(item.at("with_mean") == false && item.at("with_median") == false && item.at("foreground_mean").is_null(),
                "无效区域统计标志错误");
        }
        const cv::Mat localMask = (cv::Mat_<unsigned char>(2, 2) << 255, 0, 255, 0);
        const auto xywh = run(images, entries(detection(localMask, {1, 0, 2, 2})), both);
        number(xywh.ResultList[0]["sample_results"][0]["foreground_mean"], 6, "XYWH 前景定位错误");
        number(xywh.ResultList[0]["sample_results"][0]["background_mean"], 40, "XYWH 被当作 XYXY");
        const cv::Mat fullSourceMask = (cv::Mat_<unsigned char>(2, 3) << 0, 0, 255, 0, 0, 255);
        const auto fullSource = run(images, entries(detection(fullSourceMask, {1, 0, 2, 2})), both);
        number(fullSource.ResultList[0]["sample_results"][0]["foreground_mean"], 40, "全图 mask 未按 bbox 裁剪");
        number(fullSource.ResultList[0]["sample_results"][0]["background_mean"], 6, "全图 mask 背景错误");
        const cv::Mat rowImage = (cv::Mat_<unsigned char>(1, 3) << 10, 20, 30);
        json arrayInput = entries(detection(cv::Mat(1, 3, CV_8UC1, cv::Scalar(255)), {0, 0, 3, 1}));
        arrayInput[0]["sample_results"][0]["mask_array"] = {{1, 127, 128}};
        const json savedArray = arrayInput;
        const auto arrayOut = run({makeImage(rowImage)}, arrayInput, both);
        number(arrayOut.ResultList[0]["sample_results"][0]["foreground_mean"], 30, "mask_array 128 未作为前景或优先级错误");
        number(arrayOut.ResultList[0]["sample_results"][0]["background_mean"], 15, "mask_array 1/127 未作为背景");
        number(arrayOut.ResultList[0]["sample_results"][0]["background_median"], 15, "mask_array 偶数中值");
        check(arrayInput == savedArray, "mask_array 输入改变");
        for (const auto& array : {json(), json::object(), json::array(), json::array({json::array()}),
            json{{0, 255}, {1}}, json{{-1}}, json{{256}}, json{{128.0}}, json{{true}}, json{{"128"}}, json{128}}) {
            json invalidArray = arrayInput;
            invalidArray[0]["sample_results"][0]["mask_array"] = array;
            const auto out = run({makeImage(rowImage)}, invalidArray, both);
            const auto& item = out.ResultList[0]["sample_results"][0];
            check(item.at("with_mean") == false && item.at("with_median") == false &&
                item.at("foreground_mean").is_null() && item.at("background_mean").is_null() &&
                item.at("foreground_median").is_null() && item.at("background_median").is_null(), "非法 mask_array 未返回空 mask 或转用了 RLE");
        }
        const auto enormous = run({makeImage(rowImage)}, entries(detection(twoMask, {-1e12, 0, 2e12, 1})), both);
        const auto& enormousItem = enormous.ResultList[0]["sample_results"][0];
        check(enormousItem.at("with_mean") == true && enormousItem.at("foreground_mean").is_null(), "巨大图外 bbox 前景错误");
        number(enormousItem.at("background_mean"), 20, "巨大图外 bbox ROI 采样错误");
        cv::Mat indexedMask = cv::Mat::zeros(1, 90, CV_8UC1);
        indexedMask.at<unsigned char>(0, 62) = 255;
        const auto integerIndex = run({makeImage(rowImage)}, entries(detection(indexedMask, {-7, 0, 10, 1})), both);
        check(integerIndex.ResultList[0]["sample_results"][0]["foreground_mean"].is_null(), "最近邻索引 7*90/10 被算为 62");
        number(integerIndex.ResultList[0]["sample_results"][0]["background_mean"], 20, "完整 bbox 最近邻索引错误");
        cv::Mat wideMask = cv::Mat::zeros(1, 90, CV_8UC1);
        wideMask(cv::Rect(0, 0, 45, 1)).setTo(255);
        const auto hugeCoordinate = run({makeImage(rowImage)}, entries(detection(wideMask, {-1e307, 0, 2e307, 1})), both);
        check(hugeCoordinate.ResultList[0]["sample_results"][0]["foreground_mean"].is_null(), "巨大坐标乘法溢出后采样错误");
        number(hugeCoordinate.ResultList[0]["sample_results"][0]["background_mean"], 20, "巨大坐标 ROI 采样错误");
        const auto outside = run({makeImage(rowImage)}, entries(detection(twoMask, {-2, 0, 4, 1})), both);
        const auto& outsideItem = outside.ResultList[0]["sample_results"][0];
        check(outsideItem.at("with_mean") == true && outsideItem.at("foreground_mean").is_null(), "图外前景挤入图内");
        number(outsideItem.at("background_mean"), 15, "图外 bbox 完整缩放后裁剪错误");
        const auto fractional = run({makeImage(rowImage)}, entries(detection(twoMask, {-0.25, 0, 2.5, 1})), both);
        number(fractional.ResultList[0]["sample_results"][0]["foreground_mean"], 10, "非整数 bbox 下界错误");
        number(fractional.ResultList[0]["sample_results"][0]["background_mean"], 25, "非整数 bbox 上界错误");

        const cv::Mat rotatedImage = (cv::Mat_<unsigned char>(3, 3) << 100, 2, 100, 4, 10, 8, 100, 20, 100);
        cv::Mat rotatedMask = cv::Mat::zeros(3, 3, CV_8UC1);
        rotatedMask.at<unsigned char>(1, 1) = 255;
        const auto rotation = run({makeImage(rotatedImage)}, entries(detection(rotatedMask, {1, 1, 2, 2, CV_PI / 4})), both);
        const auto& rotationItem = rotation.ResultList[0]["sample_results"][0];
        number(rotationItem.at("foreground_mean"), 10, "旋转框前景");
        number(rotationItem.at("background_mean"), 8.5, "旋转框域包含框外像素");
        number(rotationItem.at("background_median"), 6, "旋转框背景偶数中值");
        const cv::Mat ones(3, 3, CV_8UC1, cv::Scalar(255));
        const auto halfOpen = run({makeImage(rotatedImage)}, entries(detection(ones, {1, 1, 2, 2, 0})), both);
        number(halfOpen.ResultList[0]["sample_results"][0]["foreground_mean"], 29, "旋转框域半开区间错误");
        number(halfOpen.ResultList[0]["sample_results"][0]["foreground_median"], 7, "旋转框域中值");

        struct RightAngleCase {
            double angle;
            std::vector<cv::Point> pixels;
            double mean;
            double median;
        };
        const std::vector<RightAngleCase> rightAngles{
            {0, {{1, 1}, {2, 1}, {1, 2}, {2, 2}}, 408, 288},
            {CV_PI / 2, {{2, 1}, {3, 1}, {2, 2}, {3, 2}}, 816, 576},
            {CV_PI, {{2, 2}, {3, 2}, {2, 3}, {3, 3}}, 13056, 9216},
            {3 * CV_PI / 2, {{1, 2}, {2, 2}, {1, 3}, {2, 3}}, 6528, 4608}
        };
        cv::Mat pixelIds(4, 4, CV_16UC1);
        for (int y = 0; y < 4; ++y) {
            for (int x = 0; x < 4; ++x) pixelIds.at<unsigned short>(y, x) = static_cast<unsigned short>(1u << (y * 4 + x));
        }
        const cv::Mat localOnes(2, 2, CV_8UC1, cv::Scalar(255));
        const cv::Mat fullOnes(4, 4, CV_8UC1, cv::Scalar(255));
        for (const auto& angle : rightAngles) {
            const json bbox{2, 2, 2, 2, angle.angle};
            for (const cv::Mat& shape : {localOnes, fullOnes}) {
                const auto out = run({makeImage(pixelIds)}, entries(detection(shape, bbox)), both);
                const auto& item = out.ResultList[0]["sample_results"][0];
                number(item.at("foreground_mean"), angle.mean, "直角旋转像素集合均值错误");
                number(item.at("foreground_median"), angle.median, "直角旋转像素集合中值错误");
                check(item.at("background_mean").is_null(), "全前景旋转域背景不为空");
            }
            // 单像素亮点逐一核查成员关系，四个直角方向各包含明确的四个像素。
            for (int y = 0; y < 4; ++y) {
                for (int x = 0; x < 4; ++x) {
                    cv::Mat onePixel = cv::Mat::zeros(4, 4, CV_8UC1);
                    onePixel.at<unsigned char>(y, x) = 1;
                    const auto out = run({makeImage(onePixel)}, entries(detection(localOnes, bbox)), both);
                    const bool inside = std::find(angle.pixels.begin(), angle.pixels.end(), cv::Point(x, y)) != angle.pixels.end();
                    number(out.ResultList[0]["sample_results"][0]["foreground_mean"], inside ? 0.25 : 0,
                        "直角旋转域像素成员错误，坐标=" + std::to_string(x) + "," + std::to_string(y));
                }
            }
        }
        cv::Mat edgeMask = cv::Mat::zeros(4, 4, CV_8UC1);
        edgeMask.at<unsigned char>(1, 3) = edgeMask.at<unsigned char>(2, 3) = 255;
        const auto rotatedEdge = run({makeImage(pixelIds)}, entries(detection(edgeMask, {2, 2, 2, 2, CV_PI / 2})), both);
        number(rotatedEdge.ResultList[0]["sample_results"][0]["foreground_mean"], 1088, "全图 mask 的 x=3 端点未直接索引");
        number(rotatedEdge.ResultList[0]["sample_results"][0]["background_mean"], 544, "直角旋转域混入 x=1 像素");

        cv::Mat resizeImage(1, 17, CV_8UC1, cv::Scalar(100));
        resizeImage.at<unsigned char>(0, 0) = 10;
        const cv::Mat resizeMask = (cv::Mat_<unsigned char>(1, 6) << 0, 0, 0, 255, 255, 255);
        const auto resizeOrder = run({makeImage(resizeImage)}, entries(detection(resizeMask, {-17, 0, 34, 1})), both);
        const auto& resizeItem = resizeOrder.ResultList[0]["sample_results"][0];
        number(resizeItem.at("foreground_mean"), 100, "最近邻浮点顺序导致首个背景进入前景");
        number(resizeItem.at("foreground_median"), 100, "最近邻前景中值错误");
        number(resizeItem.at("background_mean"), 10, "最近邻缩放后裁剪首个背景缺失");
        number(resizeItem.at("background_median"), 10, "最近邻背景中值错误");

        const cv::Mat original = (cv::Mat_<unsigned char>(1, 4) << 3, 10, 30, 90);
        TransformationState cropState(4, 1);
        cropState.CropBox = {1, 0, 3, 1};
        cropState.AffineMatrix2x3 = {1, 0, -1, 0, 1, 0};
        cropState.OutputSize = {3, 1};
        const ModuleImage cropped(original(cv::Rect(1, 0, 3, 1)), original, cropState);
        const cv::Mat originalMask = (cv::Mat_<unsigned char>(1, 4) << 0, 255, 0, 255);
        const auto fromOriginal = run({cropped}, entries(detection(originalMask, {0, 0, 4, 1})), both);
        number(fromOriginal.ResultList[0]["sample_results"][0]["foreground_mean"], 50, "缺少源 transform 时未使用原图 identity");
        number(fromOriginal.ResultList[0]["sample_results"][0]["background_mean"], 16.5, "原图背景遗漏当前裁图之外的像素");
        TransformationState targetState = cropState;
        targetState.CropBox = {2, 0, 2, 1};
        targetState.AffineMatrix2x3 = {1, 0, -2, 0, 1, 0};
        targetState.OutputSize = {2, 1};
        const ModuleImage target(original(cv::Rect(2, 0, 2, 1)), original, targetState);
        const cv::Mat sourceMask = (cv::Mat_<unsigned char>(1, 3) << 255, 0, 255);
        json source = entries(detection(sourceMask, {0, 0, 3, 1}));
        source[0]["transform"] = cropState.ToJson();
        const auto transformed = run({target}, source, both);
        number(transformed.ResultList[0]["sample_results"][0]["foreground_mean"], 50, "inverse(source) 未映射到完整原图前景");
        number(transformed.ResultList[0]["sample_results"][0]["background_mean"], 30, "不同 transform 背景错误");
        const cv::Mat scaleOriginal = (cv::Mat_<unsigned char>(1, 2) << 10, 20);
        const cv::Mat scaleCurrent = (cv::Mat_<unsigned char>(1, 4) << 10, 20, 20, 0);
        TransformationState scaleState(2, 1);
        scaleState.AffineMatrix2x3 = {2, 0, 0, 0, 1, 0};
        scaleState.OutputSize = {4, 1};
        const auto scaled = run({ModuleImage(scaleCurrent, scaleOriginal, scaleState)},
            entries(detection(twoMask, {0, 0, 2, 1})), both);
        number(scaled.ResultList[0]["sample_results"][0]["foreground_mean"], 10, "最近邻前景映射错误");
        number(scaled.ResultList[0]["sample_results"][0]["background_mean"], 20, "0.5 像素边缘或域外背景错误");
        number(scaled.ResultList[0]["sample_results"][0]["background_median"], 20, "映射后背景中值错误");
        // 原图前两列为前景 [2,10,4,14,6,18]，后两列为背景 [30,90,50,110,70,130]。
        // 结果坐标横向放大 2 倍、纵向放大 3 倍；插值和前置改色均不能改变原图统计。
        const cv::Mat rawPixels = (cv::Mat_<unsigned char>(3, 4) <<
            2, 10, 30, 90, 4, 14, 50, 110, 6, 18, 70, 130);
        const cv::Mat savedRawPixels = rawPixels.clone();
        cv::Mat interpolated, recolored;
        cv::resize(rawPixels, interpolated, cv::Size(8, 9), 0, 0, cv::INTER_LINEAR);
        cv::bitwise_not(interpolated, recolored);
        TransformationState nonuniformState(4, 3);
        nonuniformState.AffineMatrix2x3 = {2, 0, 0, 0, 3, 0};
        nonuniformState.OutputSize = {8, 9};
        cv::Mat sourceRegionMask(9, 8, CV_8UC1, cv::Scalar(0));
        sourceRegionMask(cv::Rect(0, 0, 4, 9)).setTo(cv::Scalar(255));
        const cv::Mat savedRegionMask = sourceRegionMask.clone();
        json nonuniformInput = entries(detection(sourceRegionMask, {0, 0, 8, 9}));
        nonuniformInput[0]["transform"] = nonuniformState.ToJson();
        const json savedNonuniformInput = nonuniformInput;
        for (const cv::Mat& currentPixels : {interpolated, recolored, cv::Mat()}) {
            const cv::Mat savedCurrentPixels = currentPixels.clone();
            const std::vector<ModuleImage> originalOnlyImages{
                ModuleImage(currentPixels, rawPixels, nonuniformState)};
            const json savedMeta = originalOnlyImages[0].ToMeta();
            const auto originalOnly = run(originalOnlyImages, nonuniformInput, both);
            const auto& item = originalOnly.ResultList[0]["sample_results"][0];
            check(item.at("with_mean") == true && item.at("with_median") == true, "原图采样统计标记错误");
            number(item.at("foreground_mean"), 9, "插值或改色影响原图前景均值");
            number(item.at("foreground_median"), 8, "插值或改色影响原图前景中值");
            number(item.at("background_mean"), 80, "插值或改色影响原图背景均值");
            number(item.at("background_median"), 80, "插值或改色影响原图背景中值");
            check(nonuniformInput == savedNonuniformInput &&
                cv::norm(rawPixels, savedRawPixels, cv::NORM_INF) == 0 &&
                cv::norm(sourceRegionMask, savedRegionMask, cv::NORM_INF) == 0,
                "原图统计修改了输入结果、原图或 mask");
            check(originalOnly.ImageList.size() == 1 && originalOnlyImages[0].ToMeta() == savedMeta &&
                originalOnly.ImageList[0].ToMeta() == savedMeta &&
                originalOnlyImages[0].OriginalImage.data == rawPixels.data &&
                originalOnly.ImageList[0].OriginalImage.data == rawPixels.data &&
                originalOnlyImages[0].ImageObject.data == currentPixels.data &&
                originalOnly.ImageList[0].ImageObject.data == currentPixels.data,
                "原图统计替换了输入或输出图像及变换信息");
            if (currentPixels.empty()) {
                check(originalOnlyImages[0].ImageObject.empty() && originalOnly.ImageList[0].ImageObject.empty(),
                    "原图统计生成了当前图像");
            } else {
                check(cv::norm(currentPixels, savedCurrentPixels, cv::NORM_INF) == 0,
                    "原图统计修改了插值或改色后的图像");
            }
        }
        json select = input;
        select[0]["transform"] = images[0].TransformState.ToJson();
        const cv::Mat dark = cv::Mat::zeros(gray.size(), CV_8UC1);
        const auto byOrigin = run({makeImage(dark, 1), images[0]}, select, both);
        number(byOrigin.ResultList[0]["sample_results"][0]["foreground_mean"], 5, "index 越过 origin 限定");
        json chooseTransform = entries(detection(sourceMask, {0, 0, 3, 1}));
        chooseTransform[0]["transform"] = targetState.ToJson();
        chooseTransform[0]["sample_results"][0]["bbox"] = {0, 0, 2, 1};
        chooseTransform[0]["sample_results"][0]["mask_rle"] = MatToMaskInfo(twoMask);
        const auto byTransform = run({cropped, target}, chooseTransform, both);
        number(byTransform.ResultList[0]["sample_results"][0]["foreground_mean"], 30, "transform 未优先于 index");
        number(byTransform.ResultList[0]["sample_results"][0]["background_mean"], 90, "transform 选择背景错误");
        select[0].erase("transform");
        select[0]["index"] = 1;
        const auto byIndex = run({makeImage(dark), images[0]}, select, both);
        number(byIndex.ResultList[0]["sample_results"][0]["foreground_mean"], 5, "同 origin 下 index 定位错误");
        chooseTransform[0].erase("origin_index");
        const auto byTransformOnly = run({cropped, target}, chooseTransform, both);
        number(byTransformOnly.ResultList[0]["sample_results"][0]["foreground_mean"], 30, "没有 origin 时未按 index 选择图像并转换坐标");
        ModuleImage deferred = target;
        deferred.ImageObject.release();
        const auto deferredOut = run({deferred}, source, both);
        number(deferredOut.ResultList[0]["sample_results"][0]["foreground_mean"], 50, "当前图像为空时未直接统计原图");
        check(deferred.ImageObject.empty() && deferredOut.ImageList[0].ImageObject.empty(), "统计改写延迟图像");

        auto rejects = [&](const std::vector<ModuleImage>& list, const json& result, const json& properties, const std::string& message) {
            bool rejected = false;
            try { run(list, result, properties); }
            catch (const std::exception&) { rejected = true; }
            check(rejected, message);
        };
        for (const char* name : {"mean", "median"}) {
            for (const auto& value : {json(1), json(0), json("true"), json(), json::array(), json::object()}) {
                rejects(images, input, json{{name, value}}, std::string(name) + " 接受非 bool");
            }
        }
        rejects(images, input, json::array(), "properties 接受数组");
        rejects(images, json::object(), both, "results 接受对象");
        rejects(images, json::array({1}), both, "entry 接受整数");
        json invalid = input;
        invalid[0]["sample_results"] = json::object();
        rejects(images, invalid, both, "sample_results 接受对象");
        invalid[0]["sample_results"] = json::array({1});
        rejects(images, invalid, both, "目标接受整数");
        invalid = input;
        invalid[0].erase("index");
        rejects({images[0], images[0]}, invalid, both, "多个候选未拒绝");
        invalid = input;
        invalid[0]["origin_index"] = 99;
        rejects(images, invalid, both, "不存在的 origin 未拒绝");
        invalid = input;
        invalid[0]["index"] = true;
        rejects(images, invalid, both, "index 接受 bool");
        invalid = source;
        invalid[0]["transform"]["affine_2x3"] = {0, 0, 0, 0, 0, 0};
        rejects({target}, invalid, both, "不可逆源 transform 未拒绝");
        invalid = source;
        invalid[0]["transform"]["original_width"] = 5;
        rejects({target}, invalid, both, "原图尺寸不一致未拒绝");
        for (int channels : {3, 4}) for (double value : {std::numeric_limits<double>::max(), -std::numeric_limits<double>::max(),
            std::numeric_limits<double>::denorm_min(), -std::numeric_limits<double>::denorm_min(), 0.0}) {
            const cv::Mat large(1, 1, CV_64FC(channels), cv::Scalar(value, value, value, value));
            const cv::Mat full(1, 1, CV_8UC1, cv::Scalar(255));
            const auto largeOut = run({makeImage(large)}, entries(detection(full, {0, 0, 1, 1})), both);
            const auto& largeItem = largeOut.ResultList[0]["sample_results"][0];
            check(largeItem["foreground_mean"].get<double>() == value && largeItem["foreground_median"].get<double>() == value,
                "有限极值统计发生溢出或下溢");
            check(largeItem["background_mean"].is_null() && largeItem["background_median"].is_null(), "极值空背景不为 null");
        }
        cv::Mat nonFinite(2, 3, CV_64FC1, cv::Scalar(1));
        nonFinite.at<double>(0, 0) = std::numeric_limits<double>::quiet_NaN();
        rejects({makeImage(nonFinite)}, input, both, "非有限采样未拒绝");
        const json other = json::array({{{"type", "global"}, {"sample_results", "保留"}}});
        check(run({}, other, both).ResultList == other, "非 local entry 被改变");
        const json noDetections = json::array({{{"type", "local"}, {"note", "保留"}}});
        const auto emptyLocal = run({}, noDetections, both);
        json expectedEmpty = noDetections;
        expectedEmpty[0]["sample_results"] = json::array();
        check(emptyLocal.ResultList == expectedEmpty && !noDetections[0].contains("sample_results"),
            "缺省 local.sample_results 未补空数组或输入改变");
        PrintUtf8Line("前景背景统计自测通过");
        return 0;
    } catch (const std::exception& error) {
        PrintUtf8Line(std::string("前景背景统计自测失败：") + error.what());
        return 1;
    }
}

int RunObjectMeanParsingSelfTest() {
    auto fail = [](const std::string& message) -> int {
        PrintUtf8Line("目标均值解析自测失败：" + message);
        return 1;
    };

    const dlcv_infer::ObjectResult defaultResult(
        1, "default", 0.9f, 1.0f,
        std::vector<double>{1.0, 2.0, 3.0, 4.0}, false, cv::Mat());
    if (defaultResult.withMean || defaultResult.foregroundMean != 0.0 || defaultResult.backgroundMean != 0.0
        || defaultResult.withMedian || defaultResult.foregroundMedian != 0.0 || defaultResult.backgroundMedian != 0.0) {
        return fail("默认统计不符合 false/0.0/0.0 输出格式");
    }

    const dlcv_infer::ObjectResult resultWithMean(
        2, "explicit", 0.8f, 2.0f,
        std::vector<double>{5.0, 6.0, 7.0, 8.0}, false, cv::Mat(),
        false, false, -100.0f, true, 12.5, 34.75);
    if (!resultWithMean.withMean
        || resultWithMean.foregroundMean != 12.5
        || resultWithMean.backgroundMean != 34.75
        || resultWithMean.withMedian || resultWithMean.foregroundMedian != 0.0 || resultWithMean.backgroundMedian != 0.0) {
        return fail("显式均值字段映射错误");
    }

    class ParseProbe final : public dlcv_infer::Model {
    public:
        using dlcv_infer::Model::ParseToStructResult;
    };

    const json resultJson = {
        {"sample_results", json::array({
            {
                {"results", json::array({
                    {
                        {"category_id", 3},
                        {"category_name", "mean"},
                        {"score", 0.7},
                        {"area", 4.0},
                        {"bbox", json::array({0.0, 0.0, 2.0, 2.0})},
                        {"with_mask", false},
                        {"mask", {{"width", 0}, {"height", 0}, {"mask_ptr", 0}}},
                        {"with_mean", true},
                        {"foreground_mean", 56.25},
                        {"background_mean", 78.5}
                    }
                })}
            }
        })}
    };
    ParseProbe probe;
    const dlcv_infer::Result parsed = probe.ParseToStructResult(resultJson);
    if (parsed.sampleResults.size() != 1 || parsed.sampleResults[0].results.size() != 1) {
        return fail("结构化结果数量错误");
    }
    const auto& parsedObject = parsed.sampleResults[0].results[0];
    if (!parsedObject.withMean
        || parsedObject.foregroundMean != 56.25
        || parsedObject.backgroundMean != 78.5) {
        return fail("结构化均值解析错误");
    }

    json missingMeanJson = resultJson;
    json& missingMeanObject = missingMeanJson["sample_results"][0]["results"][0];
    missingMeanObject.erase("with_mean");
    missingMeanObject.erase("foreground_mean");
    missingMeanObject.erase("background_mean");
    const dlcv_infer::Result parsedWithoutMean = probe.ParseToStructResult(missingMeanJson);
    const auto& objectWithoutMean = parsedWithoutMean.sampleResults[0].results[0];
    if (objectWithoutMean.withMean || objectWithoutMean.withMedian
        || objectWithoutMean.foregroundMean != 0.0 || objectWithoutMean.backgroundMean != 0.0
        || objectWithoutMean.foregroundMedian != 0.0 || objectWithoutMean.backgroundMedian != 0.0) {
        return fail("未计算统计时未恢复默认值");
    }

    const std::vector<json> statisticsCases{
        {{"with_mean", false}, {"foreground_mean", 0.0}, {"background_mean", 0.0}},
        {{"with_mean", true}, {"foreground_mean", 5.0}, {"background_mean", 40.0},
            {"with_median", true}, {"foreground_median", 5.0}, {"background_median", 40.0}},
        {{"with_mean", true}, {"foreground_mean", nullptr}, {"background_mean", 20.0},
            {"with_median", true}, {"foreground_median", nullptr}, {"background_median", 18.0}},
        {{"with_mean", false}, {"foreground_mean", nullptr}, {"background_mean", nullptr},
            {"with_median", false}, {"foreground_median", nullptr}, {"background_median", nullptr}},
        {{"with_median", true}, {"foreground_median", 2.5}, {"background_median", nullptr}},
        {{"with_mean", true}, {"foreground_mean", 7.0}, {"background_mean", nullptr}},
        json::object()
    };
    for (const auto& statistics : statisticsCases) {
        json typedJson = resultJson;
        auto& detectionJson = typedJson["sample_results"][0]["results"][0];
        for (const char* key : {"with_mean", "foreground_mean", "background_mean", "with_median", "foreground_median", "background_median"}) {
            detectionJson.erase(key);
        }
        for (auto it = statistics.begin(); it != statistics.end(); ++it) detectionJson[it.key()] = it.value();
        const auto typedParsed = probe.ParseToStructResult(typedJson);
        const auto& typed = typedParsed.sampleResults[0].results[0];
        if (typed.withMean != statistics.value("with_mean", false)
            || typed.withMedian != statistics.value("with_median", false)) {
            return fail("统计标志解析错误");
        }
        for (const auto& field : {std::make_pair("foreground_mean", typed.foregroundMean),
            std::make_pair("background_mean", typed.backgroundMean),
            std::make_pair("foreground_median", typed.foregroundMedian),
            std::make_pair("background_median", typed.backgroundMedian)}) {
            if (!statistics.contains(field.first)) {
                if (field.second != 0.0) return fail("未计算的统计值未恢复默认值");
            } else if (statistics.at(field.first).is_null()) {
                if (!std::isnan(field.second)) return fail("null 统计值未读取为 NaN");
            } else if (field.second != statistics.at(field.first).get<double>()) {
                return fail("统计值解析错误");
            }
        }
    }

    for (const std::string key : {"with_mean", "foreground_mean", "background_mean", "with_median", "foreground_median", "background_median"}) {
        const bool isFlag = key == "with_mean" || key == "with_median";
        const std::vector<json> invalidValues{json(), json("invalid"), json::array(), json::object(), isFlag ? json(1) : json(true)};
        for (const auto& invalidValue : invalidValues) {
            json tolerantJson = resultJson;
            auto& detectionJson = tolerantJson["sample_results"][0]["results"][0];
            detectionJson["with_median"] = true;
            detectionJson["foreground_median"] = 19.0;
            detectionJson["background_median"] = 23.0;
            detectionJson[key] = invalidValue;
            const auto tolerantParsed = probe.ParseToStructResult(tolerantJson);
            const auto& typed = tolerantParsed.sampleResults[0].results[0];
            if (typed.withMean != (key != "with_mean") || typed.withMedian != (key != "with_median")) {
                return fail(key + " 无效标志未单独恢复默认值");
            }
            for (const auto& field : {std::make_tuple("foreground_mean", typed.foregroundMean, 56.25),
                std::make_tuple("background_mean", typed.backgroundMean, 78.5),
                std::make_tuple("foreground_median", typed.foregroundMedian, 19.0),
                std::make_tuple("background_median", typed.backgroundMedian, 23.0)}) {
                if (key == std::get<0>(field)) {
                    if (invalidValue.is_null()) {
                        if (!std::isnan(std::get<1>(field))) return fail(key + " 的 null 未读取为 NaN");
                    } else if (std::get<1>(field) != 0.0) {
                        return fail(key + " 无效数值未单独恢复默认值");
                    }
                } else if (std::get<1>(field) != std::get<2>(field)) {
                    return fail(key + " 解析失败影响其他统计值");
                }
            }
        }
    }
    PrintUtf8Line("目标均值解析自测通过");
    return 0;
}

int RunDvspDisabledSelfTest() {
    try {
        dlcv_infer::Model model(L"unsupported_model.dvsp", 0);
        std::cout << "DVSP 禁用自测失败：接口未拒绝 .dvsp\n";
        return 1;
    } catch (const std::invalid_argument& ex) {
        std::cout << "DVSP 禁用自测通过：" << ex.what() << "\n";
        return 0;
    } catch (const std::exception& ex) {
        std::cout << "DVSP 禁用自测失败：异常类型错误，" << ex.what() << "\n";
        return 1;
    }
}

struct WorkflowOptions {
    int deviceId = 0;
    double threshold = 0.5;
    bool withMask = true;
    int batchSize = 1;
    int warmup = 1;
    int runs = 10;
    int threads = 1;
    bool replace = false;
};

struct LoadedWorkflowModel {
    std::wstring name;
    std::wstring path;
    int deviceId = 0;
    double loadMs = 0.0;
    std::unique_ptr<dlcv_infer::Model> model;
};

using WorkflowModelMap = std::map<std::string, LoadedWorkflowModel>;

std::string ToLowerAscii(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

std::string NormalizeModelName(const std::wstring& name) {
    return ToLowerAscii(WideToUtf8(name));
}

bool ParseInteger(const std::wstring& text, int& value) {
    try {
        size_t used = 0;
        const long long parsed = std::stoll(text, &used);
        if (used != text.size() || parsed < INT_MIN || parsed > INT_MAX) return false;
        value = static_cast<int>(parsed);
        return true;
    } catch (...) {
        return false;
    }
}

bool ParseDoubleValue(const std::wstring& text, double& value) {
    try {
        size_t used = 0;
        value = std::stod(text, &used);
        return used == text.size() && std::isfinite(value);
    } catch (...) {
        return false;
    }
}

bool ParseBoolValue(const std::wstring& text, bool& value) {
    const std::string normalized = ToLowerAscii(WideToUtf8(text));
    if (normalized == "true") {
        value = true;
        return true;
    }
    if (normalized == "false") {
        value = false;
        return true;
    }
    return false;
}

bool IsWorkflowOptionAllowed(const std::wstring& command, const std::wstring& option) {
    if (command == L"load-model") return option == L"--device" || option == L"--replace";
    if (command == L"infer" || command == L"infer-json") {
        return option == L"--threshold" || option == L"--with-mask";
    }
    if (command == L"infer-batch") {
        return option == L"--threshold" || option == L"--with-mask" || option == L"--batch-size";
    }
    if (command == L"benchmark") {
        return option == L"--threshold" || option == L"--with-mask"
            || option == L"--batch-size" || option == L"--warmup" || option == L"--runs" || option == L"--threads";
    }
    if (command == L"consistency-test") {
        return option == L"--threshold" || option == L"--with-mask"
            || option == L"--runs" || option == L"--threads";
    }
    return false;
}

bool ParseWorkflowOptions(
    const std::wstring& command,
    const std::vector<std::wstring>& segment,
    size_t firstArgument,
    std::vector<std::wstring>& positional,
    WorkflowOptions& options,
    std::string& error) {
    for (size_t i = firstArgument; i < segment.size(); ++i) {
        const std::wstring& token = segment[i];
        if (token.rfind(L"--", 0) != 0) {
            positional.push_back(token);
            continue;
        }
        if (!IsWorkflowOptionAllowed(command, token)) {
            error = "命令 " + WideToUtf8(command) + " 不支持参数：" + WideToUtf8(token);
            return false;
        }
        if (++i >= segment.size()) {
            error = "参数缺少取值：" + WideToUtf8(token);
            return false;
        }

        const std::wstring& value = segment[i];
        if (token == L"--device") {
            if (!ParseInteger(value, options.deviceId)) {
                error = "--device 必须是整数";
                return false;
            }
        } else if (token == L"--threshold") {
            if (!ParseDoubleValue(value, options.threshold) || options.threshold < 0.0 || options.threshold > 1.0) {
                error = "--threshold 必须在 0 到 1 之间";
                return false;
            }
        } else if (token == L"--with-mask") {
            if (!ParseBoolValue(value, options.withMask)) {
                error = "--with-mask 只能为 true 或 false";
                return false;
            }
        } else if (token == L"--batch-size") {
            if (!ParseInteger(value, options.batchSize) || options.batchSize <= 0) {
                error = "--batch-size 必须是正整数";
                return false;
            }
        } else if (token == L"--warmup") {
            if (!ParseInteger(value, options.warmup) || options.warmup < 0) {
                error = "--warmup 必须是非负整数";
                return false;
            }
        } else if (token == L"--runs") {
            if (!ParseInteger(value, options.runs) || options.runs <= 0) {
                error = "--runs 必须是正整数";
                return false;
            }
        } else if (token == L"--threads") {
            if (!ParseInteger(value, options.threads) || options.threads <= 0) {
                error = "--threads 必须是正整数";
                return false;
            }
        } else if (token == L"--replace") {
            if (!ParseBoolValue(value, options.replace)) {
                error = "--replace 只能为 true 或 false";
                return false;
            }
        } else {
            error = "未知参数：" + WideToUtf8(token);
            return false;
        }
    }
    return true;
}

json BuildInferParams(const WorkflowOptions& options, bool includeBatchSize) {
    json params = json::object();
    params["threshold"] = options.threshold;
    params["with_mask"] = options.withMask;
    if (includeBatchSize) params["batch_size"] = options.batchSize;
    return params;
}

std::string DogProviderText(sntl_admin::DogProvider provider) {
    switch (provider) {
    case sntl_admin::DogProvider::Sentinel:
        return "Sentinel";
    case sntl_admin::DogProvider::Virbox:
        return "Virbox";
    default:
        return "Unknown";
    }
}

std::string MatTypeText(const cv::Mat& mat) {
    if (mat.empty()) return "empty";
    const int depth = mat.depth();
    const char* depthName = "unknown";
    switch (depth) {
    case CV_8U: depthName = "8U"; break;
    case CV_8S: depthName = "8S"; break;
    case CV_16U: depthName = "16U"; break;
    case CV_16S: depthName = "16S"; break;
    case CV_32S: depthName = "32S"; break;
    case CV_32F: depthName = "32F"; break;
    case CV_64F: depthName = "64F"; break;
    }
    return std::string("CV_") + depthName + "C" + std::to_string(mat.channels());
}

void PrintModelHeader(const LoadedWorkflowModel& entry) {
    PrintUtf8Line("模型: " + WideToUtf8(entry.path));
}

void PrintTimingAndInspection(size_t sampleCount) {
    double sdkMs = 0.0;
    double totalMs = 0.0;
    dlcv_infer::Model::GetLastInferTiming(sdkMs, totalMs);
    PrintUtf8Line("SDK耗时(ms): " + ToFixed(sdkMs, 3));
    PrintUtf8Line("流程耗时(ms): " + ToFixed(totalMs, 3));

    const std::vector<dlcv_infer::FlowNodeTiming> timings = dlcv_infer::Model::GetLastFlowNodeTimings();
    if (!timings.empty()) {
        PrintUtf8Line("流程节点耗时:");
        for (const auto& item : timings) {
            std::string line = "  节点 " + std::to_string(item.nodeId) + " " + item.nodeType;
            if (!item.nodeTitle.empty()) line += " (" + item.nodeTitle + ")";
            line += ": " + ToFixed(item.elapsedMs, 3) + " ms";
            PrintUtf8Line(line);
        }
    }

    for (size_t i = 0; i < sampleCount; ++i) {
        bool ok = false;
        std::vector<std::string> reasons;
        if (!dlcv_infer::Model::GetLastInspectionStatus(ok, reasons, i)) continue;
        std::string line = "检查状态[" + std::to_string(i) + "]: " + (ok ? "通过" : "不通过");
        if (!reasons.empty()) {
            line += "，原因: ";
            for (size_t r = 0; r < reasons.size(); ++r) {
                if (r > 0) line += "；";
                line += reasons[r];
            }
        }
        PrintUtf8Line(line);
    }
}

void PrintStructuredResult(dlcv_infer::Result& result) {
    size_t objectCount = 0;
    for (const auto& sample : result.sampleResults) objectCount += sample.results.size();
    PrintUtf8Line("图片结果数: " + std::to_string(result.sampleResults.size()));
    PrintUtf8Line("目标数量: " + std::to_string(objectCount));
    for (size_t sampleIndex = 0; sampleIndex < result.sampleResults.size(); ++sampleIndex) {
        const auto& sample = result.sampleResults[sampleIndex];
        PrintUtf8Line("图片[" + std::to_string(sampleIndex) + "]目标数: " + std::to_string(sample.results.size()));
        for (size_t objectIndex = 0; objectIndex < sample.results.size(); ++objectIndex) {
            const auto& object = sample.results[objectIndex];
            PrintUtf8("  目标[" + std::to_string(objectIndex) + "] category_id=" + std::to_string(object.categoryId)
                + ", category_name=");
            std::cout << object.categoryName;
            std::cout << ", score=" << ToFixed(object.score, 6)
                << ", area=" << ToFixed(object.area, 3)
                << ", with_mask=" << (object.withMask ? "true" : "false")
                << ", with_bbox=" << (object.withBbox ? "true" : "false")
                << ", with_angle=" << (object.withAngle ? "true" : "false")
                << ", angle=" << ToFixed(object.angle, 3)
                << ", with_mean=" << (object.withMean ? "true" : "false")
                << ", foreground_mean=" << ToFixed(object.foregroundMean, 3)
                << ", background_mean=" << ToFixed(object.backgroundMean, 3);
            std::cout << ", bbox=[";
            for (size_t b = 0; b < object.bbox.size(); ++b) {
                if (b > 0) std::cout << ", ";
                std::cout << ToFixed(object.bbox[b], 3);
            }
            std::cout << "]";
            if (object.withMask && !object.mask.empty()) {
                std::cout << ", mask=" << object.mask.cols << "x" << object.mask.rows
                    << ", type=" << MatTypeText(object.mask);
            }
            std::cout << "\n";
        }
    }
}

bool RequireModel(
    WorkflowModelMap& models,
    const std::wstring& name,
    LoadedWorkflowModel*& entry,
    std::string& error) {
    const auto it = models.find(NormalizeModelName(name));
    if (it == models.end()) {
        error = "未找到模型名称：" + WideToUtf8(name);
        return false;
    }
    entry = &it->second;
    return true;
}

bool ReadWorkflowImage(const std::wstring& path, cv::Mat& image, std::string& error) {
    image = ReadImageRgb(path);
    if (!image.empty()) return true;
    error = "无法读取图片：" + WideToUtf8(path);
    return false;
}

std::vector<cv::Mat> RepeatImage(const cv::Mat& image, int batchSize) {
    return std::vector<cv::Mat>(static_cast<size_t>(batchSize), image);
}

void PrintWorkflowHelp() {
    PrintUtf8(
        "用法: dlcv_infer_cpp_test.exe <命令> [参数] [--then <命令> [参数] ...]\n"
        "标准生命周期: 加载 → 信息 → 推理 → 释放。\n"
        "命令可在同一进程内用 --then 串联，已加载模型按名称复用，名称不区分大小写。\n"
        "名称 m1 只在本次进程中有效，直到 free-model、free-all-models 或进程结束。\n"
        "  load-model <名称> <模型路径> [--device N] [--replace true|false]\n"
        "  list-models\n"
        "  model-info <名称>\n"
        "  dvs-model-info <名称>\n"
        "  infer <名称> <图片> [--threshold F --with-mask true|false]\n"
        "  infer-json <名称> <图片> [--threshold F --with-mask true|false]\n"
        "  infer-batch <名称> <图片> [--batch-size N --threshold F --with-mask true|false]\n"
        "  benchmark <名称> <图片> [--batch-size N --warmup N --runs N --threads N --threshold F --with-mask true|false]\n"
        "  consistency-test <名称> <图片> [--runs N --threads N --threshold F --with-mask true|false]\n"
        "  free-model <名称>\n"
        "  free-all-models\n"
        "  device-info | gpu-info | dog-info | keep-max-clock\n"
        "  help\n"
        "完整示例: load-model m1 <path> --device 0 --then model-info m1 --then infer m1 <image> --threshold 0.5 --then free-model m1\n");
}

struct BenchmarkRecord {
    double externalMs = 0.0;
    double sdkMs = 0.0;
    double flowMs = 0.0;
    std::vector<dlcv_infer::FlowNodeTiming> nodes;
};

class WorkerStartGate {
public:
    explicit WorkerStartGate(int expectedWorkers)
        : expectedWorkers_(expectedWorkers) {}

    bool ArriveAndWait() {
        std::unique_lock<std::mutex> lock(mutex_);
        ++arrivedWorkers_;
        condition_.notify_all();
        condition_.wait(lock, [this]() { return released_ || cancelled_; });
        return released_ && !cancelled_;
    }

    bool ReleaseWhenReady(Clock::time_point& startTime) {
        std::unique_lock<std::mutex> lock(mutex_);
        condition_.wait(lock, [this]() {
            return arrivedWorkers_ == expectedWorkers_ || cancelled_;
        });
        if (cancelled_) return false;
        startTime = Clock::now();
        released_ = true;
        lock.unlock();
        condition_.notify_all();
        return true;
    }

    void Cancel() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            cancelled_ = true;
        }
        condition_.notify_all();
    }

private:
    const int expectedWorkers_;
    std::mutex mutex_;
    std::condition_variable condition_;
    int arrivedWorkers_ = 0;
    bool released_ = false;
    bool cancelled_ = false;
};

class ReusableWorkerGate {
public:
    explicit ReusableWorkerGate(int expectedWorkers)
        : expectedWorkers_(expectedWorkers) {}

    bool ArriveAndWait() {
        std::unique_lock<std::mutex> lock(mutex_);
        if (cancelled_) return false;
        const int generation = generation_;
        ++arrivedWorkers_;
        if (arrivedWorkers_ == expectedWorkers_) {
            arrivedWorkers_ = 0;
            ++generation_;
            lock.unlock();
            condition_.notify_all();
            return true;
        }
        condition_.wait(lock, [this, generation]() {
            return cancelled_ || generation_ != generation;
        });
        return !cancelled_;
    }

    void Cancel() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            cancelled_ = true;
        }
        condition_.notify_all();
    }

private:
    const int expectedWorkers_;
    std::mutex mutex_;
    std::condition_variable condition_;
    int arrivedWorkers_ = 0;
    int generation_ = 0;
    bool cancelled_ = false;
};

struct ProviderLoadTestState {
    std::unique_ptr<dlcv_infer::Model> model;
    std::string error;
};

void ReleaseProviderLoadTestModel(std::unique_ptr<dlcv_infer::Model>& model) {
    if (!model) return;
    model->OwnModelIndex = false;
    try {
        model->FreeModel();
    } catch (...) {
    }
    model.reset();
}

bool IsModelInfoUnavailable(
    dlcv_infer::Model& model,
    std::string& detail) {
    try {
        const json info = model.GetModelInfo();
        if (!info.is_object() || info.empty()) {
            detail = "返回为空";
            return true;
        }
        if (info.contains("code") && info.at("code").is_number() &&
            info.at("code").get<int>() != 0) {
            return true;
        }
        detail = info.dump();
        return false;
    } catch (const std::exception& ex) {
        detail = ex.what();
        return true;
    } catch (...) {
        detail = "发生未知异常";
        return true;
    }
}

std::filesystem::path NativeModulePathForSelfTest(HMODULE module) {
    wchar_t path[32768]{};
    const DWORD length = GetModuleFileNameW(module, path, 32768);
    if (!module || length == 0 || length >= 32768) {
        throw std::runtime_error("无法读取已加载推理模块路径");
    }
    return std::filesystem::canonical(std::filesystem::path(path));
}

HMODULE ModelModuleForSelfTest(const dlcv_infer::Model& model) {
    const HMODULE module = GetModuleHandleA(model.LoadedNativeDllName().c_str());
    if (!module) throw std::runtime_error("未找到模型使用的默认推理模块");
    return module;
}

int QueryIndexTypeFromModuleForSelfTest(HMODULE module, int index) {
    const auto query = reinterpret_cast<dlcv_infer::JsonRequestFuncType>(
        GetProcAddress(module, "dlcv_get_index_type"));
    const auto freeResult = reinterpret_cast<dlcv_infer::JsonFreeFuncType>(
        GetProcAddress(module, "dlcv_free_result"));
    if (query == nullptr || freeResult == nullptr) {
        throw std::runtime_error("推理 DLL 缺少索引类型查询或结果释放接口");
    }
    const std::string request = json{{"model_index", index}}.dump();
    const char* resultPtr = query(request.c_str());
    if (resultPtr == nullptr) throw std::runtime_error("索引类型查询未返回结果");
    json result;
    try {
        result = json::parse(resultPtr);
        freeResult(resultPtr);
    } catch (...) {
        freeResult(resultPtr);
        throw;
    }
    if (!result.is_object() || !result.contains("code") ||
        !result.at("code").is_number_integer() ||
        !result.contains("message") || !result.at("message").is_string()) {
        throw std::runtime_error("索引类型查询返回结构无效");
    }
    const int code = result.at("code").get<int>();
    if (code == 2) return 0;
    if (code != 0 || !result.contains("model_index") ||
        !result.at("model_index").is_number_integer() ||
        result.at("model_index").get<int>() != index ||
        !result.contains("resource_type") || !result.at("resource_type").is_string()) {
        throw std::runtime_error("索引类型查询失败: " + result.dump());
    }
    const std::string resourceType = result.at("resource_type").get<std::string>();
    if (resourceType == "model") return 1;
    if (resourceType == "dvs") return 2;
    throw std::runtime_error("索引类型查询返回未知资源类型");
}

bool HasSharedFlowSdkForSelfTest(const dlcv_infer::Model& model) {
    const HMODULE module = ModelModuleForSelfTest(model);
    for (const char* name : {"dlcv_register_dvs_model", "dlcv_get_dvs_model",
             "dlcv_get_index_type", "dlcv_bind_index", "dlcv_get_all_models",
             "dlcv_free_model", "dlcv_free_result"}) {
        if (!GetProcAddress(module, name)) return false;
    }
    return true;
}

std::filesystem::path NormalizeModulePathForSelfTest(const std::string& value) {
    if (value.empty()) throw std::runtime_error("模型列表缺少 module_path");
    return std::filesystem::weakly_canonical(
        std::filesystem::path(dlcv_infer::convertUtf8ToWstring(value)));
}

bool GetAllModelsContainsForSelfTest(
    const json& allModels,
    HMODULE module,
    int index,
    const char* resourceType) {
    if (!allModels.is_object() || allModels.value("code", 1) != 0 ||
        !allModels.contains("modules") || !allModels.at("modules").is_array()) {
        throw std::runtime_error("GetAllModels 返回结构无效");
    }
    const auto expectedPath = NativeModulePathForSelfTest(module);
    for (const auto& snapshot : allModels.at("modules")) {
        if (!snapshot.is_object() || !snapshot.contains("module_path") ||
            !snapshot.at("module_path").is_string()) continue;
        const auto actualPath = NormalizeModulePathForSelfTest(
            snapshot.at("module_path").get<std::string>());
        if (_wcsicmp(expectedPath.c_str(), actualPath.c_str()) != 0) continue;
        if (!snapshot.contains("models") || !snapshot.at("models").is_array()) {
            throw std::runtime_error("底层模型快照缺少 models");
        }
        for (const auto& item : snapshot.at("models")) {
            if (item.is_object() && item.value("model_index", -1) == index &&
                item.value("resource_type", std::string()) == resourceType) {
                return true;
            }
        }
        return false;
    }
    return false;
}

void VerifyGetAllModelsDoesNotLoadModulesForSelfTest() {
    const HMODULE sentinelBefore = GetModuleHandleW(L"dlcv_infer.dll");
    const HMODULE virboxBefore = GetModuleHandleW(L"dlcv_infer_v.dll");
    for (int attempt = 0; attempt < 2; ++attempt) {
        const json allModels = dlcv_infer::Utils::GetAllModels();
        if (!allModels.is_object() || allModels.value("code", 1) != 0 ||
            allModels.value("message", std::string()) != "success" ||
            !allModels.contains("modules") || !allModels.at("modules").is_array()) {
            throw std::runtime_error("GetAllModels 空快照结构无效");
        }
    }
    if (GetModuleHandleW(L"dlcv_infer.dll") != sentinelBefore ||
        GetModuleHandleW(L"dlcv_infer_v.dll") != virboxBefore) {
        throw std::runtime_error("GetAllModels 调用加载了额外推理模块");
    }
}

template<class Operation>
void RequireInvalidIndexFailureForSelfTest(const std::string& label, Operation operation) {
    try {
        operation();
    } catch (const std::exception& ex) {
        const std::string message = ex.what();
        if (message.find("index") == std::string::npos && message.find("索引") == std::string::npos) {
            throw std::runtime_error(label + " 未明确报告失效索引: " + message);
        }
        PrintUtf8Line(label + " 已拒绝失效索引: " + message);
        return;
    }
    throw std::runtime_error(label + " 仍允许访问已释放索引");
}

void VerifyCachedModelInvalidationForSelfTest(const std::wstring& path, int deviceId,
                                             bool isFlow, const cv::Mat& image) {
    dlcv_infer::Model owner(path, deviceId);
    if (!HasSharedFlowSdkForSelfTest(owner)) {
        throw std::runtime_error("缓存失效测试需要完整共享 SDK");
    }
    auto borrowed = dlcv_infer::CreateModelFromIndex(owner.modelIndex);
    const HMODULE nativeModule = ModelModuleForSelfTest(owner);
    const int index = owner.modelIndex;
    // 先成功读取，确保后续访问经过已经填充的缓存和绑定状态。
    for (auto* model : {&owner, &borrowed}) {
        (void)model->GetModelInfo();
        if (isFlow) (void)model->GetDvsModelInfo();
        if (!image.empty()) {
            auto result = model->Infer(image);
            DisposeResultMasks(result);
        }
    }
    dlcv_infer::Utils::FreeAllModels();
    if (QueryIndexTypeFromModuleForSelfTest(nativeModule, index) != 0)
        throw std::runtime_error("全量释放后原生索引仍有效");
    for (auto* model : {&owner, &borrowed}) {
        const std::string label = model == &owner ? "原始对象" : "共享绑定对象";
        RequireInvalidIndexFailureForSelfTest(label + " GetModelInfo", [&] { (void)model->GetModelInfo(); });
        if (isFlow) {
            RequireInvalidIndexFailureForSelfTest(label + " GetDvsModelInfo", [&] { (void)model->GetDvsModelInfo(); });
        }
        if (!image.empty()) {
            RequireInvalidIndexFailureForSelfTest(label + " Infer", [&] {
                auto result = model->Infer(image);
                DisposeResultMasks(result);
            });
        }
    }
}

struct NativeModuleForSelfTest final {
    HMODULE module = nullptr;
    dlcv_infer::LoadModelCFuncType load = nullptr;
    dlcv_infer::JsonRequestFuncType getIndex = nullptr;
    dlcv_infer::JsonFreeFuncType freeResult = nullptr;
    dlcv_infer::FreeModelCFuncType freeModel = nullptr;
    dlcv_infer::FreeAllModelsFuncType freeAll = nullptr;

    explicit NativeModuleForSelfTest(const std::filesystem::path& path) {
        module = LoadLibraryExW(path.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (!module) throw std::runtime_error("无法加载同目录推理 DLL: " + WideToUtf8(path.wstring()));
        try {
            if (_wcsicmp(NativeModulePathForSelfTest(module).c_str(),
                         std::filesystem::canonical(path).c_str()) != 0) {
                throw std::runtime_error("实际推理 DLL 路径与指定 SDK 不一致");
            }
            load = reinterpret_cast<dlcv_infer::LoadModelCFuncType>(GetProcAddress(module, "dlcv_load_model_c"));
            getIndex = reinterpret_cast<dlcv_infer::JsonRequestFuncType>(GetProcAddress(module, "dlcv_get_index_type"));
            freeResult = reinterpret_cast<dlcv_infer::JsonFreeFuncType>(GetProcAddress(module, "dlcv_free_result"));
            freeModel = reinterpret_cast<dlcv_infer::FreeModelCFuncType>(GetProcAddress(module, "dlcv_free_model_c"));
            freeAll = reinterpret_cast<dlcv_infer::FreeAllModelsFuncType>(GetProcAddress(module, "dlcv_free_all_models"));
            if (!load || !getIndex || !freeResult || !freeModel || !freeAll)
                throw std::runtime_error("真实推理 DLL 缺少双模块测试接口");
        } catch (...) {
            FreeLibrary(module);
            module = nullptr;
            throw;
        }
    }
    NativeModuleForSelfTest(const NativeModuleForSelfTest&) = delete;
    NativeModuleForSelfTest& operator=(const NativeModuleForSelfTest&) = delete;
    ~NativeModuleForSelfTest() {
        if (module) {
            try { if (freeAll) freeAll(); } catch (...) {}
            FreeLibrary(module);
        }
    }
    int Load(const std::wstring& path) const {
        // 原生 C 路径入口使用本地 ANSI 编码，不能把中文 UTF-8 字节直接传入。
        const std::string modelPath = dlcv_infer::convertUtf8ToGbk(WideToUtf8(path));
        const int index = load(modelPath.c_str(), 0);
        if (index < 0 || QueryIndexTypeFromModuleForSelfTest(module, index) != 1)
            throw std::runtime_error("原生接口未建立有效模型资源: " + WideToUtf8(path));
        return index;
    }
};

bool VerifyNativeModulesForSelfTest(
    const std::wstring& sentinelModelPath,
    const std::wstring& virboxModelPath,
    bool useCppUtils) {
    try {
        // 普通加载只用于确定默认模块所在 SDK，不用于建立双模块测试资源。
        dlcv_infer::Model seed(sentinelModelPath, 0);
        const auto sdkDirectory = NativeModulePathForSelfTest(ModelModuleForSelfTest(seed)).parent_path();
        seed.FreeModel();
        NativeModuleForSelfTest sentinel(sdkDirectory / L"dlcv_infer.dll");
        NativeModuleForSelfTest virbox(sdkDirectory / L"dlcv_infer_v.dll");
        if (sentinel.module == virbox.module) throw std::runtime_error("两个 DLL 未形成独立模块实例");
        const int sentinelIndex = sentinel.Load(sentinelModelPath);
        const int virboxIndex = virbox.Load(virboxModelPath);
        if (QueryIndexTypeFromModuleForSelfTest(sentinel.module, sentinelIndex) != 1 || QueryIndexTypeFromModuleForSelfTest(virbox.module, virboxIndex) != 1) {
            throw std::runtime_error("全量释放前两个模块必须同时持有真实模型");
        }
        const json beforeRelease = dlcv_infer::Utils::GetAllModels();
        if (!GetAllModelsContainsForSelfTest(beforeRelease, sentinel.module, sentinelIndex, "model") ||
            !GetAllModelsContainsForSelfTest(beforeRelease, virbox.module, virboxIndex, "model")) {
            throw std::runtime_error("双模块模型列表缺少资源或 module_path 不正确");
        }

        dlcv_infer::Model sentinelBorrowed = dlcv_infer::CreateModelFromIndex(sentinelIndex);
        dlcv_infer::Model virboxBorrowed = dlcv_infer::CreateModelFromIndex(virboxIndex);
        if (ModelModuleForSelfTest(sentinelBorrowed) != sentinel.module ||
            ModelModuleForSelfTest(virboxBorrowed) != virbox.module) {
            throw std::runtime_error("共享恢复没有固定到真实所属模块");
        }

        if (useCppUtils) {
            dlcv_infer::Utils::FreeAllModels();
            if (QueryIndexTypeFromModuleForSelfTest(sentinel.module, sentinelIndex) != 0 || QueryIndexTypeFromModuleForSelfTest(virbox.module, virboxIndex) != 0) {
                throw std::runtime_error("Utils::FreeAllModels 未清除两个原生模型表");
            }
        } else {
            sentinel.freeAll();
            if (QueryIndexTypeFromModuleForSelfTest(sentinel.module, sentinelIndex) != 0 || QueryIndexTypeFromModuleForSelfTest(virbox.module, virboxIndex) != 1) {
                throw std::runtime_error("单模块释放影响了另一模块的模型表");
            }
            (void)virboxBorrowed.GetModelInfo();
            virbox.freeAll();
            if (QueryIndexTypeFromModuleForSelfTest(virbox.module, virboxIndex) != 0) throw std::runtime_error("Virbox 模型表未清除");
        }
        const json afterRelease = dlcv_infer::Utils::GetAllModels();
        if (GetAllModelsContainsForSelfTest(afterRelease, sentinel.module, sentinelIndex, "model") ||
            GetAllModelsContainsForSelfTest(afterRelease, virbox.module, virboxIndex, "model")) {
            throw std::runtime_error("全量释放后模型列表仍包含旧资源");
        }
        PrintUtf8Line(useCppUtils ? "真实双模块全部释放检查通过" : "真实双模块模型表隔离检查通过");
        return true;
    } catch (const std::exception& ex) {
        PrintUtf8ErrorLine(std::string("真实双模块检查失败: ") + ex.what());
        return false;
    }
}

bool VerifyProviderModuleMemoryIsolation(const std::wstring& sentinel, const std::wstring& virbox) {
    return VerifyNativeModulesForSelfTest(sentinel, virbox, false);
}

bool VerifyCppUtilsFreeAllModels(const std::wstring& sentinel, const std::wstring& virbox) {
    return VerifyNativeModulesForSelfTest(sentinel, virbox, true);
}

int QueryNativeIndexTypeForSelfTest(int index) {
    const HANDLE snapshot = CreateToolhelp32Snapshot(
        TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32,
        GetCurrentProcessId());
    if (snapshot == INVALID_HANDLE_VALUE) {
        throw std::runtime_error("无法枚举当前进程模块");
    }

    std::vector<HMODULE> modules;
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    BOOL hasEntry = Module32FirstW(snapshot, &entry);
    while (hasEntry) {
        const bool isTarget = _wcsicmp(entry.szModule, L"dlcv_infer.dll") == 0 ||
            _wcsicmp(entry.szModule, L"dlcv_infer_v.dll") == 0;
        if (isTarget && std::find(modules.begin(), modules.end(), entry.hModule) == modules.end()) {
            modules.push_back(entry.hModule);
        }
        hasEntry = Module32NextW(snapshot, &entry);
    }
    if (!hasEntry) {
        const DWORD error = GetLastError();
        if (error != ERROR_NO_MORE_FILES) {
            CloseHandle(snapshot);
            throw std::runtime_error("枚举当前进程模块失败: " + std::to_string(error));
        }
    }
    CloseHandle(snapshot);

    std::vector<int> validTypes;
    for (const HMODULE module : modules) {
        const auto getIndexType = reinterpret_cast<dlcv_infer::JsonRequestFuncType>(
            GetProcAddress(module, "dlcv_get_index_type"));
        const auto freeResult = reinterpret_cast<dlcv_infer::JsonFreeFuncType>(
            GetProcAddress(module, "dlcv_free_result"));
        if (getIndexType == nullptr || freeResult == nullptr) continue;
        const int indexType = QueryIndexTypeFromModuleForSelfTest(module, index);
        if (indexType == 0) continue;
        if (indexType != 1 && indexType != 2) {
            throw std::runtime_error("推理 DLL 返回未知 index 类型");
        }
        validTypes.push_back(indexType);
    }
    if (validTypes.empty()) return 0;
    if (validTypes.size() != 1) {
        throw std::runtime_error("index 在多个推理 DLL 中有效");
    }
    return validTypes.front();
}

int ReadFirstFlowModelIndexForSelfTest(const json& info) {
    if (!info.is_object() || !info.contains("loaded_model_meta") ||
        !info.at("loaded_model_meta").is_array()) {
        throw std::runtime_error("流程信息缺少 loaded_model_meta");
    }
    for (const auto& item : info.at("loaded_model_meta")) {
        if (!item.is_object() || !item.contains("model_index")) continue;
        const int modelIndex = item.at("model_index").get<int>();
        if (modelIndex >= 0) return modelIndex;
    }
    throw std::runtime_error("流程信息没有有效的子模型 index");
}

void VerifyFlowLoadCleanupForSelfTest(const std::wstring& modelPath, int deviceId) {
    using dlcv_infer::flow::FlowGraphModel;
    const ScopedDvsSelfTestFile pipelineFile(BuildDvsSelfTestFilePath(L"load_cleanup") + L".json");
    auto writePipeline = [&](const json& nodes) {
        const std::string text = json({{"nodes", nodes}}).dump();
        WriteDvsSelfTestFile(pipelineFile.path, std::vector<unsigned char>(text.begin(), text.end()));
    };
    auto node = [](int id, const json& properties) {
        return json{{"id", id}, {"type", "model/det"}, {"properties", properties}};
    };
    dlcv_infer::Utils::FreeAllModels();
    {
        dlcv_infer::Model owner(modelPath, deviceId);
        const int index = owner.modelIndex;
        if (QueryNativeIndexTypeForSelfTest(INT_MAX) != 0) {
            throw std::runtime_error("INT_MAX 已存在，无法建立无效索引测试条件");
        }
        writePipeline(json::array({node(1, {{"model_index", index}}),
                                   node(2, {{"model_index", INT_MAX}})}));
        FlowGraphModel flow;
        RequireInvalidIndexFailureForSelfTest("预绑定有效索引后加载无效索引", [&] {
            (void)flow.Load(WideToUtf8(pipelineFile.path), deviceId);
        });
        if (flow.IsLoaded()) throw std::runtime_error("加载异常后流程仍标记为已加载");
        owner.FreeModel();
        if (QueryNativeIndexTypeForSelfTest(index) != 0) {
            throw std::runtime_error("加载异常后流程仍持有首个有效索引");
        }
        if (!IsEmptyModelPoolStats(dlcv_infer::flow::GetModelPoolStats())) {
            throw std::runtime_error("预绑定失败后模型池未恢复");
        }
    }
    const ScopedDvsSelfTestFile missingModel(BuildDvsSelfTestFilePath(L"missing") + L".dvt");
    if (std::filesystem::exists(missingModel.path)) throw std::runtime_error("预期不存在的测试文件已经存在");
    writePipeline(json::array({node(1, {{"model_path", WideToUtf8(modelPath)}}),
                               node(2, {{"model_path", WideToUtf8(missingModel.path)}})}));
    {
        FlowGraphModel flow;
        const auto report = flow.Load(WideToUtf8(pipelineFile.path), deviceId);
        if (!report.contains("code") || report.at("code").get<int>() == 0 || flow.IsLoaded()) {
            throw std::runtime_error("部分模型加载失败未返回失败报告及未加载状态");
        }
        if (!IsEmptyModelPoolStats(dlcv_infer::flow::GetModelPoolStats())) {
            throw std::runtime_error("失败报告返回后模型池仍有资源");
        }
    }
    // 两个不同路径产生两个 pool key，文件内容保持相同以复用 native index。
    const ScopedDvsSelfTestFile firstFile(BuildDvsSelfTestFilePath(L"key_first") + L".dvt");
    const ScopedDvsSelfTestFile secondFile(BuildDvsSelfTestFilePath(L"key_second") + L".dvt");
    const auto bytes = ReadBinaryForDvsPoolSelfTest(modelPath);
    if (bytes.empty()) throw std::runtime_error("模型内容读取失败");
    WriteDvsSelfTestFile(firstFile.path, bytes);
    WriteDvsSelfTestFile(secondFile.path, bytes);
    writePipeline(json::array({node(1, {{"model_path", WideToUtf8(firstFile.path)}}),
                               node(2, {{"model_path", WideToUtf8(secondFile.path)}})}));
    int sharedIndex = -1;
    {
        FlowGraphModel flow;
        const auto report = flow.Load(WideToUtf8(pipelineFile.path), deviceId);
        if (report.at("code").get<int>() != 0 || !flow.IsLoaded()) {
            throw std::runtime_error("不同 pool key 的流程未加载成功: " + report.dump());
        }
        const auto meta = flow.GetDvsModelInfo().at("loaded_model_meta");
        if (meta.size() != 2) throw std::runtime_error("两个模型节点信息不完整");
        sharedIndex = meta.at(0).at("model_index").get<int>();
        if (sharedIndex != meta.at(1).at("model_index").get<int>() ||
            !IsExpectedModelPoolStats(dlcv_infer::flow::GetModelPoolStats(), 2)) {
            throw std::runtime_error("相同 native index 未保留两个独立 pool key");
        }
    }
    if (QueryNativeIndexTypeForSelfTest(sharedIndex) != 0 ||
        !IsEmptyModelPoolStats(dlcv_infer::flow::GetModelPoolStats())) {
        throw std::runtime_error("流程释放后不同 pool key 的资源未全部清理");
    }
    {
        FlowGraphModel flow;
        const auto report = flow.Load(WideToUtf8(pipelineFile.path), deviceId);
        if (report.at("code").get<int>() != 0) throw std::runtime_error("旧流程失效测试加载失败");
        dlcv_infer::Utils::FreeAllModels();
        bool rejected = false;
        try {
            (void)flow.InferInternal({cv::Mat(8, 8, CV_8UC3, cv::Scalar(0, 0, 0))});
        } catch (const std::exception& ex) {
            const std::string message = ex.what();
            rejected = message.find("已释放") != std::string::npos;
            if (!rejected) throw;
        }
        if (!rejected || !IsEmptyModelPoolStats(dlcv_infer::flow::GetModelPoolStats())) {
            throw std::runtime_error("FreeAll 后旧流程未拒绝推理或重新创建了模型池资源");
        }
    }
    PrintUtf8Line("流程异常清理、失败报告、同索引双 pool key 及旧流程失效检查通过");
}

bool VerifyCreateModelFromIndexReference(
    const std::wstring& modelPath,
    int deviceId,
    bool isFlowModel) {
    const auto verifyDvsInfo = [](const json& info, int index) {
        if (!info.is_object() || info.value("code", 1) != 0 ||
            info.value("message", std::string()) != "success" ||
            info.value("resource_type", std::string()) != "dvs" ||
            info.value("model_index", -1) != index ||
            info.value("schema_version", 0) != 1 ||
            !info.contains("dvs_type") || !info.at("dvs_type").is_string() ||
            !info.contains("model_path") || !info.at("model_path").is_string() ||
            !info.contains("device_id") || !info.at("device_id").is_number_integer() ||
            !info.contains("pipeline") || !info.at("pipeline").is_object() ||
            !info.contains("model_bindings") || !info.at("model_bindings").is_array() ||
            info.contains("provider")) {
            throw std::runtime_error("DVS 完整描述不符合最终结构");
        }
    };

    const auto runScenario = [&](bool releaseOwnerFirst) {
        std::unique_ptr<dlcv_infer::Model> owner;
        try {
            owner = std::make_unique<dlcv_infer::Model>(modelPath, deviceId);
            const int index = owner->modelIndex;
            if (index < 0) throw std::runtime_error("原始模型没有返回有效 index");
            const HMODULE module = ModelModuleForSelfTest(*owner);
            const int expectedType = isFlowModel ? 2 : 1;
            if (QueryNativeIndexTypeForSelfTest(index) != expectedType) {
                throw std::runtime_error("原始模型 index 类型不符合预期");
            }
            if (!GetAllModelsContainsForSelfTest(
                    dlcv_infer::Utils::GetAllModels(), module, index,
                    isFlowModel ? "dvs" : "model")) {
                throw std::runtime_error("模型列表未包含已加载资源或模块来源不正确");
            }

            dlcv_infer::Model borrowed = dlcv_infer::CreateModelFromIndex(index);
            if (borrowed.modelIndex != index || borrowed.OwnModelIndex) {
                throw std::runtime_error("工厂返回对象的共享索引状态不正确");
            }
            if (isFlowModel) {
                const json dvsInfo = borrowed.GetDvsModelInfo();
                verifyDvsInfo(dvsInfo, index);
                const json compatibleInfo = borrowed.GetModelInfo();
                if (dvsInfo.at("model_bindings").empty()) {
                    if (compatibleInfo.value("model_index", -1) == index) {
                        throw std::runtime_error("空 DVS 的兼容信息错误使用了 DVS index");
                    }
                } else {
                    if (!compatibleInfo.contains("model_index") ||
                        !compatibleInfo.at("model_index").is_number_integer()) {
                        throw std::runtime_error("DVS 兼容信息缺少子模型 model_index");
                    }
                    const int compatibleIndex = compatibleInfo.at("model_index").get<int>();
                    bool childMatched = false;
                    for (const auto& binding : dvsInfo.at("model_bindings")) {
                        if (binding.is_object() && binding.value("model_index", -1) == compatibleIndex) {
                            childMatched = true;
                            break;
                        }
                    }
                    if (!childMatched || compatibleIndex == index) {
                        throw std::runtime_error("DVS 兼容信息没有保留子模型 index");
                    }
                }
            } else {
                const json modelInfo = borrowed.GetModelInfo();
                if (modelInfo.value("model_index", -1) != index) {
                    throw std::runtime_error("普通模型公开信息缺少 model_index");
                }
            }

            if (releaseOwnerFirst) {
                owner.reset();
                if (isFlowModel) verifyDvsInfo(borrowed.GetDvsModelInfo(), index);
                else (void)borrowed.GetModelInfo();
            } else {
                borrowed.FreeModel();
                borrowed.FreeModel();
                if (isFlowModel) verifyDvsInfo(owner->GetDvsModelInfo(), index);
                else (void)owner->GetModelInfo();
            }

            if (QueryNativeIndexTypeForSelfTest(index) != expectedType ||
                !GetAllModelsContainsForSelfTest(
                    dlcv_infer::Utils::GetAllModels(), module, index,
                    isFlowModel ? "dvs" : "model")) {
                throw std::runtime_error("最后一次释放前资源已提前失效");
            }

            if (releaseOwnerFirst) borrowed.FreeModel();
            else owner.reset();
            borrowed.FreeModel();
            if (QueryNativeIndexTypeForSelfTest(index) != 0 ||
                GetAllModelsContainsForSelfTest(
                    dlcv_infer::Utils::GetAllModels(), module, index,
                    isFlowModel ? "dvs" : "model")) {
                throw std::runtime_error("最后一次释放后资源仍然可见");
            }

            bool invalidIndexRejected = false;
            try {
                auto invalidModel = dlcv_infer::CreateModelFromIndex(index);
                (void)invalidModel;
            } catch (const std::exception&) {
                invalidIndexRejected = true;
            }
            if (!invalidIndexRejected) {
                throw std::runtime_error("已释放索引未被工厂拒绝");
            }
            PrintUtf8Line(
                std::string("CreateModelFromIndex 释放顺序检查通过: ") +
                (releaseOwnerFirst ? "原始对象先释放" : "共享对象先释放") +
                "，index=" + std::to_string(index));
            return true;
        } catch (const std::exception& ex) {
            owner.reset();
            PrintUtf8ErrorLine(std::string("CreateModelFromIndex 引用检查失败: ") + ex.what());
            return false;
        } catch (...) {
            owner.reset();
            PrintUtf8ErrorLine("CreateModelFromIndex 引用检查发生未知异常");
            return false;
        }
    };

    return runScenario(true) && runScenario(false);
}

bool VerifyFlowModelPoolAfterFreeAll(
    const std::wstring& flowPath,
    int deviceId) {
    std::unique_ptr<dlcv_infer::Model> firstFlow;
    std::unique_ptr<dlcv_infer::Model> secondFlow;
    try {
        firstFlow = std::make_unique<dlcv_infer::Model>(flowPath, deviceId);
        const json firstInfo = firstFlow->GetDvsModelInfo();
        const int firstChildIndex = ReadFirstFlowModelIndexForSelfTest(firstInfo);
        const int firstChildType = QueryNativeIndexTypeForSelfTest(firstChildIndex);
        if (firstChildType != 1) {
            throw std::runtime_error("首次流程加载后的子模型 index 不可用");
        }
        PrintUtf8Line(
            "流程模型池检查：首次子模型 index=" + std::to_string(firstChildIndex) +
            "，类型=" + std::to_string(firstChildType));

        dlcv_infer::Utils::FreeAllModels();
        const int childTypeAfterFreeAll = QueryNativeIndexTypeForSelfTest(firstChildIndex);
        PrintUtf8Line(
            "流程模型池检查：FreeAllModels 后子模型类型=" +
            std::to_string(childTypeAfterFreeAll));
        if (childTypeAfterFreeAll != 0) {
            throw std::runtime_error("FreeAllModels 后子模型 index 仍然有效，未形成检查条件");
        }

        firstFlow->FreeModel();
        if (firstFlow->modelIndex != -1) {
            throw std::runtime_error("FreeAllModels 后旧流程对象未完成本地清理");
        }
        firstFlow->FreeModel();
        if (firstFlow->modelIndex != -1) {
            throw std::runtime_error("旧流程对象重复释放后本地状态异常");
        }
        PrintUtf8Line("流程释放检查：底层资源已清除时仍完成本地清理，重复释放成功");

        try {
            secondFlow = std::make_unique<dlcv_infer::Model>(flowPath, deviceId);
            const json secondInfo = secondFlow->GetDvsModelInfo();
            const int secondChildIndex = ReadFirstFlowModelIndexForSelfTest(secondInfo);
            const int secondChildType = QueryNativeIndexTypeForSelfTest(secondChildIndex);
            PrintUtf8Line(
                "流程模型池检查：再次加载子模型 index=" +
                std::to_string(secondChildIndex) + "，类型=" +
                std::to_string(secondChildType));
            if (secondChildIndex == firstChildIndex && secondChildType == 0) {
                throw std::runtime_error("再次加载复用了 FreeAllModels 后失效的子模型 index");
            }
            if (secondChildType != 1) {
                throw std::runtime_error("再次加载后的子模型 index 不可用");
            }
        } catch (const std::exception& ex) {
            PrintUtf8ErrorLine(
                std::string("流程模型池检查：FreeAllModels 后再次加载失败: ") + ex.what());
            return false;
        }
        return true;
    } catch (const std::exception& ex) {
        try { dlcv_infer::Utils::FreeAllModels(); } catch (...) {}
        firstFlow.reset();
        secondFlow.reset();
        PrintUtf8ErrorLine(std::string("流程模型池检查失败: ") + ex.what());
        return false;
    } catch (...) {
        try { dlcv_infer::Utils::FreeAllModels(); } catch (...) {}
        firstFlow.reset();
        secondFlow.reset();
        PrintUtf8ErrorLine("流程模型池检查发生未知异常");
        return false;
    }
}

int RunCreateModelFromIndexSelfTest(int argc, wchar_t* argv[]) {
    if (argc < 3 || argc > 4) {
        PrintUtf8ErrorLine(
            "用法: dlcv_infer_cpp_test.exe create-model-from-index-selftest <模型路径> [device]");
        return 2;
    }

    const std::wstring modelPath = argv[2];
    const int deviceId = argc == 4 ? _wtoi(argv[3]) : 0;
    const std::wstring extension = dvs_test::Lower(std::filesystem::path(modelPath).extension().wstring());
    const bool isFlowModel = extension == L".dvst" || extension == L".dvso";
    if (!isFlowModel && extension == L".dvsp") {
        PrintUtf8ErrorLine("模型必须为普通模型、.dvst 或 .dvso，不能为 .dvsp");
        return 2;
    }

    PrintUtf8Line("==== C++ CreateModelFromIndex 回归测试 ====");
    PrintUtf8Line("模型: " + WideToUtf8(modelPath));
    PrintUtf8Line(std::string("模型类型: ") + (isFlowModel ? "流程模型" : "普通模型"));

    try {
        VerifyGetAllModelsDoesNotLoadModulesForSelfTest();
        for (const int invalidIndex : {-1, -2, (std::numeric_limits<int>::min)()}) {
            bool invalidArgumentRejected = false;
            try {
                auto invalidModel = dlcv_infer::CreateModelFromIndex(invalidIndex);
                (void)invalidModel;
            } catch (const std::invalid_argument&) {
                invalidArgumentRejected = true;
            }
            if (!invalidArgumentRejected) {
                PrintUtf8ErrorLine(
                    "CreateModelFromIndex 未拒绝负 index: " + std::to_string(invalidIndex));
                return 1;
            }
        }
        PrintUtf8Line("CreateModelFromIndex 非法参数检查通过");

        if (!VerifyCreateModelFromIndexReference(modelPath, deviceId, isFlowModel)) {
            return 1;
        }
        if (isFlowModel && !VerifyFlowModelPoolAfterFreeAll(modelPath, deviceId)) {
            return 1;
        }
        VerifyCachedModelInvalidationForSelfTest(modelPath, deviceId, isFlowModel, cv::Mat());
        const ScopedDvsSelfTestFile emptyFlow(BuildDvsSelfTestFilePath(L"empty_shared"));
        const std::string emptyPipeline = "{\"nodes\":[]}";
        WriteDvsSelfTestFile(emptyFlow.path, BuildDvsSelfTestArchive(
            {"pipeline.json"}, {std::vector<unsigned char>(emptyPipeline.begin(), emptyPipeline.end())}));
        if (!VerifyCreateModelFromIndexReference(emptyFlow.path, deviceId, true)) {
            return 1;
        }
        VerifyCachedModelInvalidationForSelfTest(emptyFlow.path, deviceId, true,
                                                cv::Mat(8, 8, CV_8UC3, cv::Scalar(0, 0, 0)));
        PrintUtf8Line("C++ CreateModelFromIndex 回归测试结束");
        return 0;
    } catch (const std::exception& ex) {
        PrintUtf8ErrorLine(std::string("回归测试异常: ") + ex.what());
        return 1;
    } catch (...) {
        PrintUtf8ErrorLine("回归测试发生未知异常");
        return 1;
    }
}

int RunFreeAllModulesSelfTest(int argc, wchar_t* argv[]) {
    if (argc != 4) {
        PrintUtf8ErrorLine(
            "用法: dlcv_infer_cpp_test.exe free-all-modules-selftest <Sentinel模型路径> <Virbox模型路径>");
        return 2;
    }

    const std::wstring sentinelModelPath = argv[2];
    const std::wstring virboxModelPath = argv[3];
    if (!VerifyProviderModuleMemoryIsolation(sentinelModelPath, virboxModelPath)) {
        return 1;
    }
    if (!VerifyCppUtilsFreeAllModels(sentinelModelPath, virboxModelPath)) {
        PrintUtf8ErrorLine("C++ FreeAllModels 全部模块检查失败");
        return 1;
    }
    PrintUtf8Line("C++ FreeAllModels 全部模块检查通过");
    return 0;
}

int RunProviderLoaderSelfTest(int argc, wchar_t* argv[]) {
    if (argc != 4 && argc != 5) {
        PrintUtf8ErrorLine(
            "用法: dlcv_infer_cpp_test.exe provider-loader-selftest <Virbox模型路径> <另一模型路径> [轮数]");
        return 2;
    }

    int rounds = 8;
    if (argc == 5 && (!ParseInteger(argv[4], rounds) || rounds <= 0)) {
        PrintUtf8ErrorLine("轮数必须是正整数");
        return 2;
    }

    const std::wstring virboxModelPath = argv[2];
    const std::wstring secondModelPath = argv[3];
    std::unique_ptr<dlcv_infer::Model> baseline;
    try { baseline = std::make_unique<dlcv_infer::Model>(virboxModelPath, 0); }
    catch (const std::exception& ex) { PrintUtf8ErrorLine(ex.what()); return 1; }
    const auto expectedProvider = baseline->LoadedDogProvider();
    const HMODULE expectedModule = ModelModuleForSelfTest(*baseline);
    const auto expectedPath = NativeModulePathForSelfTest(expectedModule);
    if (expectedProvider != sntl_admin::DogProvider::Virbox || expectedModule == nullptr) {
        PrintUtf8ErrorLine("首次 Virbox 模型未选定有效的默认推理 DLL");
        return 1;
    }
    PrintUtf8Line("首次 Virbox 模型选定默认 provider: " + DogProviderText(expectedProvider) +
        "，DLL: " + WideToUtf8(expectedPath.wstring()));
    const ScopedDvsSelfTestFile firstArchive(BuildDvsSelfTestFilePath(L"stable_first"));
    const ScopedDvsSelfTestFile secondArchive(BuildDvsSelfTestFilePath(L"stable_second"));
    try {
        const auto firstBytes = ReadBinaryForDvsPoolSelfTest(virboxModelPath);
        const auto secondBytes = ReadBinaryForDvsPoolSelfTest(secondModelPath);
        if (firstBytes.empty() || secondBytes.empty()) throw std::runtime_error("模型文件读取失败");
        WriteDvsSelfTestFile(firstArchive.path, BuildDvsModelPoolArchive(firstBytes));
        WriteDvsSelfTestFile(secondArchive.path, BuildDvsModelPoolArchive(secondBytes));
    } catch (const std::exception& ex) { PrintUtf8ErrorLine(ex.what()); return 1; }
    baseline.reset();
    ProviderLoadTestState firstState;
    ProviderLoadTestState secondState;
    ReusableWorkerGate roundGate(2);

    auto loadWorker = [&](const std::wstring& modelPath,
                          const std::wstring& archivePath,
                          ProviderLoadTestState& state) {
        try {
            for (int round = 0; round < rounds; ++round) {
                if (!roundGate.ArriveAndWait()) return;
                auto model = std::make_unique<dlcv_infer::Model>(modelPath, 0);
                const auto actualProvider = model->LoadedDogProvider();
                if (actualProvider != expectedProvider || ModelModuleForSelfTest(*model) != expectedModule ||
                    NativeModulePathForSelfTest(ModelModuleForSelfTest(*model)) != expectedPath) {
                    throw std::runtime_error("普通文件加载改变了默认推理 DLL");
                }
                (void)model->GetModelInfo();
                {
                    // 真实归档子模型通过产品内存加载入口创建，不访问内部 DllLoader。
                    dlcv_infer::Model flow(archivePath, 0);
                    if (flow.LoadedDogProvider() != expectedProvider || ModelModuleForSelfTest(flow) != expectedModule) {
                        throw std::runtime_error("归档内存加载改变了默认推理 DLL");
                    }
                    const auto meta = flow.GetDvsModelInfo().at("loaded_model_meta");
                    if (meta.empty()) throw std::runtime_error("归档未加载真实子模型");
                    for (const auto& item : meta) {
                        auto child = dlcv_infer::CreateModelFromIndex(item.at("model_index").get<int>());
                        (void)child.GetModelInfo();
                        if (ModelModuleForSelfTest(child) != expectedModule) {
                            throw std::runtime_error("内存子模型未保存在默认模块");
                        }
                    }
                }
                // 再次走普通入口，检查内存加载未改变后续默认选择。
                dlcv_infer::Model afterMemory(modelPath, 0);
                if (ModelModuleForSelfTest(afterMemory) != expectedModule) {
                    throw std::runtime_error("内存加载后默认模块发生变化");
                }
                if (round + 1 == rounds) {
                    state.model = std::move(model);
                } else {
                    model->FreeModel();
                }
            }
        } catch (const std::exception& ex) {
            state.error = ex.what();
            roundGate.Cancel();
        } catch (...) {
            state.error = "并发加载时发生未知异常";
            roundGate.Cancel();
        }
    };

    std::thread firstWorker;
    std::thread secondWorker;
    try {
        firstWorker = std::thread(loadWorker, virboxModelPath, firstArchive.path, std::ref(firstState));
        secondWorker = std::thread(loadWorker, secondModelPath, secondArchive.path, std::ref(secondState));
    } catch (const std::exception& ex) {
        roundGate.Cancel();
        if (firstWorker.joinable()) firstWorker.join();
        if (secondWorker.joinable()) secondWorker.join();
        PrintUtf8ErrorLine(std::string("并发测试线程创建失败: ") + ex.what());
        return 1;
    }
    firstWorker.join();
    secondWorker.join();

    if (!firstState.error.empty() || !secondState.error.empty() ||
        !firstState.model || !secondState.model) {
        ReleaseProviderLoadTestModel(firstState.model);
        ReleaseProviderLoadTestModel(secondState.model);
        if (!firstState.error.empty()) {
            PrintUtf8ErrorLine("首个模型并发加载失败: " + firstState.error);
        }
        if (!secondState.error.empty()) {
            PrintUtf8ErrorLine("另一模型并发加载失败: " + secondState.error);
        }
        return 1;
    }

    PrintUtf8Line(
        "并发加载后的 provider: 首个模型=" +
        DogProviderText(firstState.model->LoadedDogProvider()) +
        "，另一模型=" + DogProviderText(secondState.model->LoadedDogProvider()));

    const int firstIndex = firstState.model->modelIndex;
    const int secondIndex = secondState.model->modelIndex;
    firstState.model->OwnModelIndex = false;
    secondState.model->OwnModelIndex = false;
    try {
        dlcv_infer::Utils::FreeAllModels();
    } catch (const std::exception& ex) {
        ReleaseProviderLoadTestModel(firstState.model);
        ReleaseProviderLoadTestModel(secondState.model);
        PrintUtf8ErrorLine(std::string("全量释放失败: ") + ex.what());
        return 1;
    } catch (...) {
        ReleaseProviderLoadTestModel(firstState.model);
        ReleaseProviderLoadTestModel(secondState.model);
        PrintUtf8ErrorLine("全量释放时发生未知异常");
        return 1;
    }

    std::string firstInfo;
    std::string secondInfo;
    const bool firstUnavailable = IsModelInfoUnavailable(*firstState.model, firstInfo);
    const bool secondUnavailable = IsModelInfoUnavailable(*secondState.model, secondInfo);
    const bool passed = firstUnavailable && secondUnavailable;
    PrintUtf8Line(
        "FreeAllModels 后 index 查询: 首个模型(" + std::to_string(firstIndex) + ")=" +
        (firstUnavailable ? "不可查询" : "仍可查询") +
        "，另一模型(" + std::to_string(secondIndex) + ")=" +
        (secondUnavailable ? "不可查询" : "仍可查询"));
    if (!firstUnavailable) {
        PrintUtf8ErrorLine("首个模型 index 查询结果: " + firstInfo);
    }
    if (!secondUnavailable) {
        PrintUtf8ErrorLine("另一模型 index 查询结果: " + secondInfo);
    }

    ReleaseProviderLoadTestModel(firstState.model);
    ReleaseProviderLoadTestModel(secondState.model);
    if (!passed) {
        PrintUtf8ErrorLine("固定默认 DLL 并发加载与清理检查失败");
        return 1;
    }
    PrintUtf8Line("固定默认 DLL 文件及内存并发加载与清理检查通过");
    return 0;
}

double Percentile(std::vector<double> values, double percent) {
    if (values.empty()) return 0.0;
    std::sort(values.begin(), values.end());
    const double position = percent * static_cast<double>(values.size() - 1);
    const size_t lower = static_cast<size_t>(std::floor(position));
    const size_t upper = static_cast<size_t>(std::ceil(position));
    const double ratio = position - static_cast<double>(lower);
    return values[lower] + (values[upper] - values[lower]) * ratio;
}

bool ReleaseAfterFreeAll(WorkflowModelMap& models, std::string&) {
    for (auto& item : models) {
        item.second.model->FreeModel();
        item.second.model->FreeModel();
    }
    models.clear();
    dlcv_infer::Utils::FreeAllModels();
    dlcv_infer::Utils::FreeAllModels();
    return true;
}

bool RunBenchmark(
    LoadedWorkflowModel& entry,
    const cv::Mat& image,
    const WorkflowOptions& options,
    std::string& error) {
    const json params = BuildInferParams(options, true);
    const std::vector<cv::Mat> batch = RepeatImage(image, options.batchSize);
    try {
        for (int i = 0; i < options.warmup; ++i) {
            dlcv_infer::Result warmupResult = entry.model->InferBatch(batch, params);
            DisposeResultMasks(warmupResult);
        }
    } catch (const std::exception& ex) {
        error = std::string("测速预热失败：") + ex.what();
        return false;
    }

    std::vector<BenchmarkRecord> records;
    std::mutex recordMutex;
    std::exception_ptr workerException;
    std::mutex errorMutex;
    std::atomic<bool> cancelRequested{false};
    WorkerStartGate startGate(options.threads);
    // 一个已加载模型没有并发调用保证，线程复用该模型时按次序进入推理。
    std::mutex modelInferMutex;
    std::vector<std::thread> workers;
    workers.reserve(static_cast<size_t>(options.threads));
    try {
        for (int threadIndex = 0; threadIndex < options.threads; ++threadIndex) {
            workers.emplace_back([&]() {
                try {
                    if (!startGate.ArriveAndWait()) return;
                    std::vector<BenchmarkRecord> local;
                    local.reserve(static_cast<size_t>(options.runs));
                    for (int run = 0; run < options.runs && !cancelRequested.load(); ++run) {
                        const auto begin = Clock::now();
                        std::unique_lock<std::mutex> modelLock(modelInferMutex);
                        if (cancelRequested.load()) break;
                        dlcv_infer::Result result = entry.model->InferBatch(batch, params);
                        modelLock.unlock();
                        const double externalMs = std::chrono::duration<double, std::milli>(Clock::now() - begin).count();
                        DisposeResultMasks(result);
                        BenchmarkRecord record;
                        record.externalMs = externalMs;
                        dlcv_infer::Model::GetLastInferTiming(record.sdkMs, record.flowMs);
                        record.nodes = dlcv_infer::Model::GetLastFlowNodeTimings();
                        local.push_back(std::move(record));
                    }
                    std::lock_guard<std::mutex> lock(recordMutex);
                    records.insert(records.end(), std::make_move_iterator(local.begin()), std::make_move_iterator(local.end()));
                } catch (...) {
                    cancelRequested.store(true);
                    {
                        std::lock_guard<std::mutex> lock(errorMutex);
                        if (!workerException) workerException = std::current_exception();
                    }
                    startGate.Cancel();
                }
            });
        }
    } catch (...) {
        cancelRequested.store(true);
        {
            std::lock_guard<std::mutex> lock(errorMutex);
            if (!workerException) workerException = std::current_exception();
        }
        startGate.Cancel();
    }
    Clock::time_point allStart;
    if (!startGate.ReleaseWhenReady(allStart)) {
        error = "测速线程未能完成启动";
    }
    for (auto& worker : workers) worker.join();
    if (workerException) {
        try {
            std::rethrow_exception(workerException);
        } catch (const std::exception& ex) {
            error = std::string("测速失败：") + ex.what();
        } catch (...) {
            error = "测速发生未知异常";
        }
        return false;
    }
    if (!error.empty()) return false;
    const double totalWallMs = std::chrono::duration<double, std::milli>(Clock::now() - allStart).count();

    std::vector<double> externalTimes;
    double sdkTotal = 0.0;
    double flowTotal = 0.0;
    std::map<std::tuple<int, std::string, std::string>, std::pair<double, size_t>> nodeTotals;
    for (const auto& record : records) {
        externalTimes.push_back(record.externalMs);
        sdkTotal += record.sdkMs;
        flowTotal += record.flowMs;
        for (const auto& node : record.nodes) {
            const auto key = std::make_tuple(node.nodeId, node.nodeType, node.nodeTitle);
            auto& total = nodeTotals[key];
            total.first += node.elapsedMs;
            total.second++;
        }
    }
    if (externalTimes.empty()) {
        error = "测速未产生结果";
        return false;
    }
    const double externalSum = std::accumulate(externalTimes.begin(), externalTimes.end(), 0.0);
    const double imageCount = static_cast<double>(records.size()) * options.batchSize;
    PrintModelHeader(entry);
    PrintUtf8Line("图片: " + std::to_string(image.cols) + "x" + std::to_string(image.rows));
    PrintUtf8Line("批量大小: " + std::to_string(options.batchSize)
        + "，预热次数: " + std::to_string(options.warmup)
        + "，正式次数: " + std::to_string(options.runs)
        + "，线程数: " + std::to_string(options.threads));
    PrintUtf8Line("外部耗时(ms): min=" + ToFixed(*std::min_element(externalTimes.begin(), externalTimes.end()), 3)
        + ", avg=" + ToFixed(externalSum / externalTimes.size(), 3)
        + ", p50=" + ToFixed(Percentile(externalTimes, 0.50), 3)
        + ", p95=" + ToFixed(Percentile(externalTimes, 0.95), 3)
        + ", max=" + ToFixed(*std::max_element(externalTimes.begin(), externalTimes.end()), 3));
    PrintUtf8Line("总墙钟耗时(ms): " + ToFixed(totalWallMs, 3));
    PrintUtf8Line("吞吐(张/秒): " + ToFixed(totalWallMs > 0.0 ? imageCount * 1000.0 / totalWallMs : 0.0, 3));
    PrintUtf8Line("SDK平均耗时(ms): " + ToFixed(sdkTotal / records.size(), 3));
    PrintUtf8Line("流程平均耗时(ms): " + ToFixed(flowTotal / records.size(), 3));
    if (!nodeTotals.empty()) {
        PrintUtf8Line("流程节点平均耗时:");
        for (const auto& pair : nodeTotals) {
            const auto& key = pair.first;
            const auto& total = pair.second;
            std::string line = "  节点 " + std::to_string(std::get<0>(key)) + " " + std::get<1>(key);
            if (!std::get<2>(key).empty()) line += " (" + std::get<2>(key) + ")";
            line += ": " + ToFixed(total.first / total.second, 3) + " ms";
            PrintUtf8Line(line);
        }
    }
    return true;
}

bool RunConsistencyTest(
    LoadedWorkflowModel& entry,
    const cv::Mat& image,
    const WorkflowOptions& options,
    std::string& error) {
    const json params = BuildInferParams(options, false);
    std::mutex signatureMutex;
    std::string referenceStructuredSignature;
    std::string referenceJsonDump;
    std::string firstDifference;
    std::exception_ptr workerException;
    std::mutex errorMutex;
    std::atomic<bool> cancelRequested{false};
    WorkerStartGate startGate(options.threads);
    // 流程节点会复用加载期保存的模块状态，同一模型实例按次序执行推理。
    std::mutex modelInferMutex;
    std::vector<std::thread> workers;
    workers.reserve(static_cast<size_t>(options.threads));
    try {
        for (int threadIndex = 0; threadIndex < options.threads; ++threadIndex) {
            workers.emplace_back([&, threadIndex]() {
                try {
                    if (!startGate.ArriveAndWait()) return;
                    for (int run = 0; run < options.runs && !cancelRequested.load(); ++run) {
                        std::unique_lock<std::mutex> structuredModelLock(modelInferMutex);
                        if (cancelRequested.load()) break;
                        dlcv_infer::Result result = entry.model->Infer(image, params);
                        structuredModelLock.unlock();
                        const std::string structuredSignature = BuildResultSignature(result);
                        DisposeResultMasks(result);

                        json jsonResult;
                        {
                            std::lock_guard<std::mutex> lock(modelInferMutex);
                            if (cancelRequested.load()) break;
                            jsonResult = entry.model->InferOneOutJson(image, params);
                        }
                        const std::string jsonDump = CanonicalJsonDump(jsonResult);

                        std::lock_guard<std::mutex> lock(signatureMutex);
                        if (referenceStructuredSignature.empty()) {
                            referenceStructuredSignature = structuredSignature;
                        } else if (firstDifference.empty() && structuredSignature != referenceStructuredSignature) {
                            firstDifference = "interface=struct,thread=" + std::to_string(threadIndex)
                                + ",run=" + std::to_string(run + 1)
                                + ",expected_hash=" + ToHexDigest(CalculateTextDigest(referenceStructuredSignature))
                                + ",actual_hash=" + ToHexDigest(CalculateTextDigest(structuredSignature));
                        }
                        if (referenceJsonDump.empty()) {
                            referenceJsonDump = jsonDump;
                        } else if (firstDifference.empty() && jsonDump != referenceJsonDump) {
                            firstDifference = "interface=json,thread=" + std::to_string(threadIndex)
                                + ",run=" + std::to_string(run + 1)
                                + ",expected_hash=" + ToHexDigest(CalculateTextDigest(referenceJsonDump))
                                + ",actual_hash=" + ToHexDigest(CalculateTextDigest(jsonDump));
                        }
                    }
                } catch (...) {
                    cancelRequested.store(true);
                    {
                        std::lock_guard<std::mutex> lock(errorMutex);
                        if (!workerException) workerException = std::current_exception();
                    }
                    startGate.Cancel();
                }
            });
        }
    } catch (...) {
        cancelRequested.store(true);
        {
            std::lock_guard<std::mutex> lock(errorMutex);
            if (!workerException) workerException = std::current_exception();
        }
        startGate.Cancel();
    }
    Clock::time_point startTime;
    const bool started = startGate.ReleaseWhenReady(startTime);
    for (auto& worker : workers) worker.join();
    if (workerException) {
        try {
            std::rethrow_exception(workerException);
        } catch (const std::exception& ex) {
            error = std::string("一致性测试失败：") + ex.what();
        } catch (...) {
            error = "一致性测试发生未知异常";
        }
        return false;
    }
    if (!started) {
        error = "一致性测试线程未能完成启动";
        return false;
    }
    PrintModelHeader(entry);
    PrintUtf8Line("图片: " + std::to_string(image.cols) + "x" + std::to_string(image.rows));
    PrintUtf8Line("线程数: " + std::to_string(options.threads) + "，每线程次数: " + std::to_string(options.runs));
    if (firstDifference.empty()) {
        PrintUtf8Line("一致性结果: 通过");
        std::cout << "struct_hash=" << ToHexDigest(CalculateTextDigest(referenceStructuredSignature)) << "\n";
        std::cout << "json_hash=" << ToHexDigest(CalculateTextDigest(referenceJsonDump)) << "\n";
        return true;
    }
    PrintUtf8Line("一致性结果: 不通过");
    std::cout << "first_difference=" << firstDifference << "\n";
    error = "推理结果不一致";
    return false;
}

bool IsWorkflowCommand(const std::wstring& command) {
    static const std::vector<std::wstring> commands = {
        L"help", L"load-model", L"list-models", L"model-info", L"dvs-model-info",
        L"infer", L"infer-json", L"infer-batch", L"benchmark", L"consistency-test",
        L"free-model", L"free-all-models", L"device-info", L"gpu-info", L"dog-info", L"keep-max-clock"
    };
    return std::find(commands.begin(), commands.end(), command) != commands.end();
}

int RunWorkflowCommand(
    const std::vector<std::wstring>& segment,
    WorkflowModelMap& models,
    std::string& error) {
    if (segment.empty()) {
        error = "--then 后缺少命令";
        return 2;
    }
    const std::wstring& command = segment.front();
    std::vector<std::wstring> positional;
    WorkflowOptions options;
    if (!ParseWorkflowOptions(command, segment, 1, positional, options, error)) return 2;

    auto requireCount = [&](size_t count) {
        if (positional.size() == count) return true;
        error = "命令 " + WideToUtf8(command) + " 的参数数量不正确";
        return false;
    };

    try {
        if (command == L"help") {
            if (!requireCount(0)) return 2;
            PrintWorkflowHelp();
            return 0;
        }
        if (command == L"load-model") {
            if (!requireCount(2)) return 2;
            const std::string key = NormalizeModelName(positional[0]);
            if (key.empty()) {
                error = "模型名称不能为空";
                return 2;
            }
            auto found = models.find(key);
            if (found != models.end()) {
                if (!options.replace) {
                    error = "模型名称已存在：" + WideToUtf8(positional[0]);
                    return 2;
                }
            }
            const auto begin = Clock::now();
            auto model = std::make_unique<dlcv_infer::Model>(positional[1], options.deviceId);
            const double loadMs = std::chrono::duration<double, std::milli>(Clock::now() - begin).count();
            if (found != models.end()) {
                found->second.model->FreeModel();
                models.erase(found);
            }
            LoadedWorkflowModel entry;
            entry.name = positional[0];
            entry.path = positional[1];
            entry.deviceId = options.deviceId;
            entry.loadMs = loadMs;
            entry.model = std::move(model);
            auto inserted = models.emplace(key, std::move(entry));
            const LoadedWorkflowModel& loaded = inserted.first->second;
            PrintModelHeader(loaded);
            PrintUtf8Line("名称: " + WideToUtf8(loaded.name));
            PrintUtf8Line("设备: " + std::to_string(loaded.deviceId));
            std::cout << "model_index: " << loaded.model->modelIndex << "\n";
            std::cout << "load_ms: " << ToFixed(loaded.loadMs, 3) << "\n";
            std::cout << "provider: " << DogProviderText(loaded.model->LoadedDogProvider()) << "\n";
            PrintUtf8Line("DLL: " + loaded.model->LoadedNativeDllName());
            return 0;
        }
        if (command == L"list-models") {
            if (!requireCount(0)) return 2;
            PrintUtf8Line("已加载模型数量: " + std::to_string(models.size()));
            for (const auto& pair : models) {
                const auto& item = pair.second;
                PrintUtf8Line("名称: " + WideToUtf8(item.name)
                    + "，模型: " + WideToUtf8(item.path)
                    + "，设备: " + std::to_string(item.deviceId)
                    + "，model_index: " + std::to_string(item.model->modelIndex));
            }
            return 0;
        }
        if (command == L"model-info" || command == L"dvs-model-info") {
            if (!requireCount(1)) return 2;
            LoadedWorkflowModel* entry = nullptr;
            if (!RequireModel(models, positional[0], entry, error)) return 1;
            PrintModelHeader(*entry);
            const json info = command == L"model-info" ? entry->model->GetModelInfo() : entry->model->GetDvsModelInfo();
            PrintUtf8Line(info.dump(2));
            return 0;
        }
        if (command == L"free-model") {
            if (!requireCount(1)) return 2;
            const auto found = models.find(NormalizeModelName(positional[0]));
            if (found == models.end()) {
                error = "未找到模型名称：" + WideToUtf8(positional[0]);
                return 1;
            }
            PrintModelHeader(found->second);
            found->second.model->FreeModel();
            PrintUtf8Line("已释放模型名称: " + WideToUtf8(found->second.name));
            models.erase(found);
            return 0;
        }
        if (command == L"free-all-models") {
            if (!requireCount(0)) return 2;
            const size_t count = models.size();
            if (!ReleaseAfterFreeAll(models, error)) return 1;
            PrintUtf8Line("已释放全部模型数量: " + std::to_string(count));
            return 0;
        }
        if (command == L"device-info" || command == L"gpu-info" || command == L"dog-info" || command == L"keep-max-clock") {
            if (!requireCount(0)) return 2;
            if (command == L"device-info") {
                PrintUtf8Line(dlcv_infer::Utils::GetDeviceInfo().dump(2));
            } else if (command == L"gpu-info") {
                PrintUtf8Line(dlcv_infer::Utils::GetGpuInfo().dump(2));
            } else if (command == L"dog-info") {
                PrintUtf8Line(dlcv_infer::GetAllDogInfo().dump(2));
            } else {
                dlcv_infer::Utils::KeepMaxClock();
                PrintUtf8Line("保持最高显卡频率的请求已发送");
            }
            return 0;
        }
        if (command == L"infer" || command == L"infer-json" || command == L"infer-batch" || command == L"benchmark" || command == L"consistency-test") {
            if (!requireCount(2)) return 2;
            LoadedWorkflowModel* entry = nullptr;
            if (!RequireModel(models, positional[0], entry, error)) return 1;
            cv::Mat image;
            if (!ReadWorkflowImage(positional[1], image, error)) return 1;
            if (command == L"benchmark") return RunBenchmark(*entry, image, options, error) ? 0 : 1;
            if (command == L"consistency-test") return RunConsistencyTest(*entry, image, options, error) ? 0 : 1;

            PrintModelHeader(*entry);
            PrintUtf8Line("图片: " + WideToUtf8(positional[1]) + " ("
                + std::to_string(image.cols) + "x" + std::to_string(image.rows) + ")");
            if (command == L"infer-json") {
                const auto begin = Clock::now();
                const json result = entry->model->InferOneOutJson(image, BuildInferParams(options, false));
                PrintUtf8Line("外部耗时(ms): " + ToFixed(std::chrono::duration<double, std::milli>(Clock::now() - begin).count(), 3));
                PrintUtf8Line(result.dump(2));
                // 在释放模型前输出完整 JSON，避免原生库日志插入缓冲中的结果。
                std::cout.flush();
                PrintTimingAndInspection(1);
                return 0;
            }

            const bool batch = command == L"infer-batch";
            const auto begin = Clock::now();
            dlcv_infer::Result result = batch
                ? entry->model->InferBatch(RepeatImage(image, options.batchSize), BuildInferParams(options, true))
                : entry->model->Infer(image, BuildInferParams(options, false));
            const double externalMs = std::chrono::duration<double, std::milli>(Clock::now() - begin).count();
            PrintUtf8Line("批量大小: " + std::to_string(batch ? options.batchSize : 1));
            PrintUtf8Line("阈值: " + ToFixed(options.threshold, 3));
            PrintUtf8Line("外部耗时(ms): " + ToFixed(externalMs, 3));
            PrintStructuredResult(result);
            PrintTimingAndInspection(result.sampleResults.size());
            DisposeResultMasks(result);
            return 0;
        }
        error = "未知命令：" + WideToUtf8(command);
        return 2;
    } catch (const std::exception& ex) {
        error = ex.what();
        return 1;
    } catch (...) {
        error = "命令执行时发生未知异常";
        return 1;
    }
}

int RunWorkflow(int argc, wchar_t* argv[]) {
    // 工作流模型的生命周期为加载、信息、推理和释放，模型表仅在当前进程内存活。
    std::vector<std::vector<std::wstring>> segments(1);
    for (int i = 1; i < argc; ++i) {
        const std::wstring token = argv[i];
        if (token == L"--then") {
            if (segments.back().empty()) {
                PrintUtf8ErrorLine("参数错误: --then 前缺少命令");
                return 2;
            }
            segments.emplace_back();
            continue;
        }
        segments.back().push_back(token);
    }
    if (segments.back().empty()) {
        PrintUtf8ErrorLine("参数错误: --then 后缺少命令");
        return 2;
    }

    WorkflowModelMap models;
    for (const auto& segment : segments) {
        std::string error;
        const int code = RunWorkflowCommand(segment, models, error);
        if (code != 0) {
            models.clear();
            PrintUtf8ErrorLine(std::string(code == 2 ? "参数错误: " : "执行失败: ") + error);
            return code;
        }
    }
    models.clear();
    PrintUtf8Line("命令串执行结束，剩余模型已自动释放");
    return 0;
}

int RunGetModelInfoCommand(int argc, wchar_t* argv[], bool dvsInfo) {
    const char* command = dvsInfo ? "get-dvs-model-info" : "get-model-info";
    if (argc != 3) {
        PrintUtf8ErrorLine(std::string("用法: dlcv_infer_cpp_test.exe ") + command + " <model>");
        return 2;
    }

    try {
        json result;
        {
            dlcv_infer::Model model(argv[2], 0);
            result = dvsInfo ? model.GetDvsModelInfo() : model.GetModelInfo();
        }
        PrintUtf8Line(result.dump(2));
        return 0;
    } catch (const std::exception& ex) {
        PrintUtf8ErrorLine(ex.what());
        return 1;
    } catch (...) {
        PrintUtf8ErrorLine("读取模型信息时发生未知异常");
        return 1;
    }
}
}  // namespace

int wmain(int argc, wchar_t* argv[]) {
    if (argc >= 2 && std::wstring(argv[1]) == L"default-device-selftest") {
        return RunDefaultDeviceSelfTest(argc, argv);
    }

    if (argc >= 2 && IsWorkflowCommand(std::wstring(argv[1]))) {
        return RunWorkflow(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"dvs-rgb-selftest") {
        return RunDvsRgbSelfTest(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"dvs-archive-duplicate-selftest") {
        return RunDvsArchiveDuplicateSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"dvs-model-pool-selftest") {
        return RunDvsModelPoolSelfTest(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"dvs-memory-loading-selftest") {
        return RunDvsMemoryLoadingSelfTest(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"dvsp-reject-selftest") {
        return RunDvspRejectSelfTest(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"undersized-model-selftest") {
        return RunUndersizedModelSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"curve-text-affine-selftest") {
        return argc == 2 ? RunCurveTextAffineSelfTest() : RunCurveTextAffineSample(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"ai-orientation-affine-selftest") {
        return RunAiOrientationAffineSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"imageprepcheck") {
        return RunImagePrepCheck();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"rect-image-correction-selftest") {
        return RunRectImageCorrectionSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"mask-area-selftest") {
        return RunMaskAreaSelfTest();
    }
    if (argc >= 2 && std::wstring(argv[1]) == L"region-mask-selftest") {
        return RunRegionMaskSelfTest();
    }
    if (argc >= 2 && std::wstring(argv[1]) == L"sliding-merge-selftest") {
        return RunSlidingMergeSelfTest();
    }
    if (argc >= 2 && std::wstring(argv[1]) == L"bbox-iou-dedup-selftest") {
        return RunBBoxIoUDedupSelfTest();
    }
    if (argc >= 2 && std::wstring(argv[1]) == L"poly-filter-selftest") {
        return RunPolyFilterSelfTest();
    }


    if (argc >= 2 && std::wstring(argv[1]) == L"count-results-selftest") {
        return RunCountResultsSelfTest();
    }


    if (argc >= 2 && std::wstring(argv[1]) == L"category-count-check-selftest") {
        return RunCategoryCountCheckSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"image-generation-expand-selftest") {
        return RunImageGenerationExpandSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"cross-model-label-merge-selftest") {
        return RunCrossModelLabelMergeSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"load-three-models") {
        return RunThreeModelLoadTiming(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"foreground-background-statistics-selftest") {
        return RunForegroundBackgroundStatisticsSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"object-mean-parsing-selftest") {
        return RunObjectMeanParsingSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"dvsp-disabled-selftest") {
        return RunDvspDisabledSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"free-all-modules-selftest") {
        return RunFreeAllModulesSelfTest(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"create-model-from-index-selftest") {
        return RunCreateModelFromIndexSelfTest(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"provider-loader-selftest") {
        return RunProviderLoaderSelfTest(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"shared-index-rules-selftest") {
        if (argc != 2) {
            PrintUtf8ErrorLine("用法: dlcv_infer_cpp_test.exe shared-index-rules-selftest");
            return 2;
        }
        const int resolverResult = dlcv_test::RunSharedIndexResolverSelfTest();
        const int modelIndexResult = dlcv_test::RunFlowModelIndexRulesSelfTest();
        const bool passed = resolverResult == 0 && modelIndexResult == 0;
        PrintUtf8Line(passed
            ? "共享索引选择、严格流程 model_index 与归档遗留 index 清理自测通过"
            : "共享索引选择、严格流程 model_index 或归档遗留 index 清理自测失败");
        return passed ? 0 : 1;
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"get-model-info") {
        return RunGetModelInfoCommand(argc, argv, false);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"get-dvs-model-info") {
        return RunGetModelInfoCommand(argc, argv, true);
    }

    PrintUtf8Line("Usage: " + (argc >= 1 ? WideToUtf8(argv[0]) : std::string("dlcv_infer_cpp_test")) + " <subcommand>");
    std::cout << "Available subcommands:\n";
    std::cout << "  default-device-selftest <modelPath> <imagePath> <expectedObjectCount>\n";
    std::cout << "  dvs-rgb-selftest <modelPath> <imagePath> [require-preserved-mask|require-polyline]\n";
    std::cout << "  dvs-archive-duplicate-selftest\n";
    std::cout << "  dvs-model-pool-selftest <modelPath> [device]\n";
    std::cout << "  dvs-memory-loading-selftest <modelPath> <imagePath> [device]\n";
    std::cout << "  dvsp-reject-selftest <modelPath> [device]\n";
    std::cout << "  undersized-model-selftest\n";
    std::cout << "  curve-text-affine-selftest\n";
    std::cout << "  ai-orientation-affine-selftest\n";
    std::cout << "  imageprepcheck\n";
    std::cout << "  rect-image-correction-selftest\n";
    std::cout << "  sliding-merge-selftest\n";
    std::cout << "  bbox-iou-dedup-selftest\n";
    std::cout << "  poly-filter-selftest\n";
    std::cout << "  count-results-selftest\n";
    std::cout << "  category-count-check-selftest\n";
    std::cout << "  image-generation-expand-selftest\n";
    std::cout << "  cross-model-label-merge-selftest\n";
    std::cout << "  load-three-models <extractModelPath> <componentModelPath> <icModelPath>\n";
    std::cout << "  foreground-background-statistics-selftest\n";
    std::cout << "  object-mean-parsing-selftest\n";
    std::cout << "  dvsp-disabled-selftest\n";
    std::cout << "  free-all-modules-selftest <SentinelModelPath> <VirboxModelPath>\n";
    std::cout << "  create-model-from-index-selftest <modelPath> [device]\n";
    std::cout << "  provider-loader-selftest <VirboxModelPath> <OtherModelPath> [rounds]\n";
    std::cout << "  shared-index-rules-selftest\n";
    std::cout << "  get-model-info <model>\n";
    std::cout << "  get-dvs-model-info <model>\n";
    PrintUtf8("\n工作流命令帮助:\n");
    PrintWorkflowHelp();
    return 2;
}
