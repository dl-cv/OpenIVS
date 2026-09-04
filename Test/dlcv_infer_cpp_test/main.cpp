#include <windows.h>

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
#include <string>
#include <thread>
#include <tuple>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "../../dlcv_infer_cpp_dll/ImageInputUtils.h"
#include "../../dlcv_infer_cpp_dll/flow/FlowGraphModel.h"
#include "../../dlcv_infer_cpp_dll/flow/ModuleRegistry.h"
#include "../../dlcv_infer_cpp_dll/flow/utils/MaskRleUtils.h"
#include "../DvsTempArtifactMonitor.h"
#include "dlcv_infer.h"

namespace {
using json = nlohmann::json;
using Clock = std::chrono::steady_clock;

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

int RunDvsRgbSelfTest(int argc, wchar_t* argv[]) {
    if (argc < 4) {
        PrintUtf8Line("Usage: dlcv_infer_cpp_test dvs-rgb-selftest <modelPath> <imagePath> [require-preserved-mask]");
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
        const bool requirePreservedMask = argc >= 5 &&
            (std::wstring(argv[4]) == L"require-preserved-mask" ||
             std::wstring(argv[4]) == L"require-original-mask");
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
            model.FreeModel();
        } catch (const std::exception& ex) {
            operationError = ex.what();
        } catch (...) {
            operationError = "加载、推理或释放时发生未知异常";
        }
        monitor.Stop();

        if (!operationError.empty()) {
            PrintUtf8ErrorLine(operationError);
            return 1;
        }
        if (monitor.HasArtifacts()) {
            PrintUtf8ErrorLine("系统临时目录出现流程归档文件: " + monitor.DescribeUtf8());
            return 1;
        }
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
        {"out_height", 80}, {"sample_step", 10.0}, {"shrink_inside", 1.5}, {"method", "auto"}
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

int RunCalcMeanSelfTest() {
    auto fail = [](const std::string& message) -> int {
        PrintUtf8Line("calc_mean 自测失败：" + message);
        return 1;
    };

    const dlcv_infer::ObjectResult defaultResult(
        1, "default", 0.9f, 1.0f,
        std::vector<double>{1.0, 2.0, 3.0, 4.0}, false, cv::Mat());
    if (defaultResult.withMean || defaultResult.foregroundMean != 0.0 || defaultResult.backgroundMean != 0.0) {
        return fail("默认均值不符合 false/0.0 语义");
    }

    const dlcv_infer::ObjectResult resultWithMean(
        2, "explicit", 0.8f, 2.0f,
        std::vector<double>{5.0, 6.0, 7.0, 8.0}, false, cv::Mat(),
        false, false, -100.0f, true, 12.5, 34.75);
    if (!resultWithMean.withMean
        || resultWithMean.foregroundMean != 12.5
        || resultWithMean.backgroundMean != 34.75) {
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
    if (objectWithoutMean.withMean
        || objectWithoutMean.foregroundMean != 0.0
        || objectWithoutMean.backgroundMean != 0.0) {
        return fail("缺少均值字段时的默认值错误");
    }

    PrintUtf8Line("calc_mean 自测通过");
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
    bool calcMeanSpecified = false;
    bool calcMean = false;
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
        return option == L"--threshold" || option == L"--with-mask" || option == L"--calc-mean";
    }
    if (command == L"infer-batch") {
        return option == L"--threshold" || option == L"--with-mask" || option == L"--calc-mean" || option == L"--batch-size";
    }
    if (command == L"benchmark") {
        return option == L"--threshold" || option == L"--with-mask" || option == L"--calc-mean"
            || option == L"--batch-size" || option == L"--warmup" || option == L"--runs" || option == L"--threads";
    }
    if (command == L"consistency-test") {
        return option == L"--threshold" || option == L"--with-mask" || option == L"--calc-mean"
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
        } else if (token == L"--calc-mean") {
            const std::string normalized = ToLowerAscii(WideToUtf8(value));
            if (normalized == "default") {
                options.calcMeanSpecified = false;
            } else if (normalized == "true") {
                options.calcMeanSpecified = true;
                options.calcMean = true;
            } else if (normalized == "false") {
                options.calcMeanSpecified = true;
                options.calcMean = false;
            } else {
                error = "--calc-mean 只能为 default、true 或 false";
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
    if (options.calcMeanSpecified) params["calc_mean"] = options.calcMean;
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
        "  infer <名称> <图片> [--threshold F --with-mask true|false --calc-mean default|true|false]\n"
        "  infer-json <名称> <图片> [--threshold F --with-mask true|false --calc-mean default|true|false]\n"
        "  infer-batch <名称> <图片> [--batch-size N --threshold F --with-mask true|false --calc-mean default|true|false]\n"
        "  benchmark <名称> <图片> [--batch-size N --warmup N --runs N --threads N --threshold F --with-mask true|false --calc-mean default|true|false]\n"
        "  consistency-test <名称> <图片> [--runs N --threads N --threshold F --with-mask true|false --calc-mean default|true|false]\n"
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

int RunProviderLoaderSelfTest(int argc, wchar_t* argv[]) {
    if (argc != 4 && argc != 5) {
        PrintUtf8ErrorLine(
            "用法: dlcv_infer_cpp_test.exe provider-loader-selftest <Sentinel模型路径> <Virbox模型路径> [轮数]");
        return 2;
    }

    int rounds = 8;
    if (argc == 5 && (!ParseInteger(argv[4], rounds) || rounds <= 0)) {
        PrintUtf8ErrorLine("轮数必须是正整数");
        return 2;
    }

    const std::wstring sentinelModelPath = argv[2];
    const std::wstring virboxModelPath = argv[3];
    ProviderLoadTestState sentinelState;
    ProviderLoadTestState virboxState;
    ReusableWorkerGate roundGate(2);

    auto loadWorker = [&](const std::wstring& modelPath,
                          sntl_admin::DogProvider expectedProvider,
                          ProviderLoadTestState& state) {
        try {
            for (int round = 0; round < rounds; ++round) {
                if (!roundGate.ArriveAndWait()) return;
                auto model = std::make_unique<dlcv_infer::Model>(modelPath, 0);
                const auto actualProvider = model->LoadedDogProvider();
                if (actualProvider != expectedProvider) {
                    throw std::runtime_error(
                        "第 " + std::to_string(round + 1) + " 轮 provider 错误，期望 " +
                        DogProviderText(expectedProvider) + "，实际 " + DogProviderText(actualProvider));
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

    std::thread sentinelWorker(
        loadWorker, sentinelModelPath, sntl_admin::DogProvider::Sentinel, std::ref(sentinelState));
    std::thread virboxWorker(
        loadWorker, virboxModelPath, sntl_admin::DogProvider::Virbox, std::ref(virboxState));
    sentinelWorker.join();
    virboxWorker.join();

    if (!sentinelState.error.empty() || !virboxState.error.empty() ||
        !sentinelState.model || !virboxState.model) {
        ReleaseProviderLoadTestModel(sentinelState.model);
        ReleaseProviderLoadTestModel(virboxState.model);
        if (!sentinelState.error.empty()) {
            PrintUtf8ErrorLine("Sentinel 并发加载失败: " + sentinelState.error);
        }
        if (!virboxState.error.empty()) {
            PrintUtf8ErrorLine("Virbox 并发加载失败: " + virboxState.error);
        }
        return 1;
    }

    PrintUtf8Line(
        "并发加载后的 provider: Sentinel=" +
        DogProviderText(sentinelState.model->LoadedDogProvider()) +
        "，Virbox=" + DogProviderText(virboxState.model->LoadedDogProvider()));

    const int sentinelIndex = sentinelState.model->modelIndex;
    const int virboxIndex = virboxState.model->modelIndex;
    sentinelState.model->OwnModelIndex = false;
    virboxState.model->OwnModelIndex = false;
    try {
        dlcv_infer::Utils::FreeAllModels();
    } catch (const std::exception& ex) {
        ReleaseProviderLoadTestModel(sentinelState.model);
        ReleaseProviderLoadTestModel(virboxState.model);
        PrintUtf8ErrorLine(std::string("全量释放失败: ") + ex.what());
        return 1;
    } catch (...) {
        ReleaseProviderLoadTestModel(sentinelState.model);
        ReleaseProviderLoadTestModel(virboxState.model);
        PrintUtf8ErrorLine("全量释放时发生未知异常");
        return 1;
    }

    std::string sentinelInfo;
    std::string virboxInfo;
    const bool sentinelUnavailable = IsModelInfoUnavailable(*sentinelState.model, sentinelInfo);
    const bool virboxUnavailable = IsModelInfoUnavailable(*virboxState.model, virboxInfo);
    const bool passed = sentinelUnavailable && virboxUnavailable;
    PrintUtf8Line(
        "FreeAllModels 后 index 查询: Sentinel(" + std::to_string(sentinelIndex) + ")=" +
        (sentinelUnavailable ? "不可查询" : "仍可查询") +
        "，Virbox(" + std::to_string(virboxIndex) + ")=" +
        (virboxUnavailable ? "不可查询" : "仍可查询"));
    if (!sentinelUnavailable) {
        PrintUtf8ErrorLine("Sentinel index 查询结果: " + sentinelInfo);
    }
    if (!virboxUnavailable) {
        PrintUtf8ErrorLine("Virbox index 查询结果: " + virboxInfo);
    }

    ReleaseProviderLoadTestModel(sentinelState.model);
    ReleaseProviderLoadTestModel(virboxState.model);
    if (!passed) {
        PrintUtf8ErrorLine("provider loader 回归检查失败");
        return 1;
    }
    PrintUtf8Line("provider loader 回归检查通过");
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

bool ReleaseAfterFreeAll(WorkflowModelMap& models, std::string& error) {
    std::vector<std::string> failedNames;
    for (auto it = models.begin(); it != models.end();) {
        try {
            it->second.model->FreeModel();
            it = models.erase(it);
        } catch (...) {
            failedNames.push_back(WideToUtf8(it->second.name));
            ++it;
        }
    }
    if (!failedNames.empty()) {
        error = "释放模型失败：";
        for (size_t i = 0; i < failedNames.size(); ++i) {
            if (i > 0) error += "，";
            error += failedNames[i];
        }
        return false;
    }
    try {
        dlcv_infer::Utils::FreeAllModels();
    } catch (const std::exception& ex) {
        error = std::string("全局模型释放接口调用失败：") + ex.what();
        return false;
    } catch (...) {
        error = "全局模型释放接口调用失败";
        return false;
    }
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
    if (argc >= 2 && IsWorkflowCommand(std::wstring(argv[1]))) {
        return RunWorkflow(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"dvs-rgb-selftest") {
        return RunDvsRgbSelfTest(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"dvs-memory-loading-selftest") {
        return RunDvsMemoryLoadingSelfTest(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"dvsp-reject-selftest") {
        return RunDvspRejectSelfTest(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"curve-text-affine-selftest") {
        return RunCurveTextAffineSelfTest();
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

    if (argc >= 2 && std::wstring(argv[1]) == L"bbox-iou-dedup-selftest") {
        return RunBBoxIoUDedupSelfTest();
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

    if (argc >= 2 && std::wstring(argv[1]) == L"calc-mean-selftest") {
        return RunCalcMeanSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"dvsp-disabled-selftest") {
        return RunDvspDisabledSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"provider-loader-selftest") {
        return RunProviderLoaderSelfTest(argc, argv);
    }

    std::cout << "Usage: " << (argc >= 1 ? WideToUtf8(argv[0]) : "dlcv_infer_cpp_test") << " <subcommand>\n";
    if (argc >= 2 && std::wstring(argv[1]) == L"get-model-info") {
        return RunGetModelInfoCommand(argc, argv, false);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"get-dvs-model-info") {
        return RunGetModelInfoCommand(argc, argv, true);
    }

    PrintUtf8Line("Usage: " + (argc >= 1 ? WideToUtf8(argv[0]) : std::string("dlcv_infer_cpp_test")) + " <subcommand>");
    std::cout << "Available subcommands:\n";
    std::cout << "  dvs-rgb-selftest <modelPath> <imagePath> [require-preserved-mask]\n";
    std::cout << "  dvs-memory-loading-selftest <modelPath> <imagePath> [device]\n";
    std::cout << "  dvsp-reject-selftest <modelPath> [device]\n";
    std::cout << "  curve-text-affine-selftest\n";
    std::cout << "  ai-orientation-affine-selftest\n";
    std::cout << "  imageprepcheck\n";
    std::cout << "  rect-image-correction-selftest\n";
    std::cout << "  bbox-iou-dedup-selftest\n";
    std::cout << "  count-results-selftest\n";
    std::cout << "  category-count-check-selftest\n";
    std::cout << "  image-generation-expand-selftest\n";
    std::cout << "  cross-model-label-merge-selftest\n";
    std::cout << "  load-three-models <extractModelPath> <componentModelPath> <icModelPath>\n";
    std::cout << "  calc-mean-selftest\n";
    std::cout << "  dvsp-disabled-selftest\n";
    std::cout << "  provider-loader-selftest <SentinelModelPath> <VirboxModelPath> [rounds]\n";
    std::cout << "  get-model-info <model>\n";
    std::cout << "  get-dvs-model-info <model>\n";
    PrintUtf8("\n工作流命令帮助:\n");
    PrintWorkflowHelp();
    return 2;
}
