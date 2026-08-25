#include "DlcvInferApi.h"

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include <Windows.h>

#include <atomic>
#include <cerrno>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cwchar>
#include <iomanip>
#include <iostream>
#include <limits>
#include <locale>
#include <map>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace {

enum ExitCode {
    ExitSuccess = 0,
    ExitCommandError = 2,
    ExitDllError = 3,
    ExitModelError = 4,
    ExitInferError = 5,
    ExitBenchmarkError = 6,
};

struct InferOptions {
    double threshold = 0.5;
    bool withMask = true;
    bool calcMean = false;
};

struct StableObjectResult {
    int categoryId = 0;
    std::string categoryName;
    float score = 0.0F;
    bool withBbox = false;
    float area = 0.0F;
    float x = 0.0F;
    float y = 0.0F;
    float w = 0.0F;
    float h = 0.0F;
    bool withMask = false;
    int maskWidth = 0;
    int maskHeight = 0;
    bool withAngle = false;
    float angle = 0.0F;
    bool withMean = false;
    double foregroundMean = 0.0;
    double backgroundMean = 0.0;
};

struct StableResultSummary {
    std::vector<std::vector<StableObjectResult>> samples;
};

std::string wideToUtf8(const std::wstring& value) {
    if (value.empty()) {
        return {};
    }
    const int length = WideCharToMultiByte(
        CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (length <= 0) {
        return {};
    }
    std::string result(static_cast<size_t>(length), '\0');
    WideCharToMultiByte(
        CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), length, nullptr, nullptr);
    return result;
}

std::string wideToAnsi(const std::wstring& value) {
    if (value.empty()) {
        return {};
    }
    const int length = WideCharToMultiByte(
        CP_ACP, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (length <= 0) {
        return {};
    }
    std::string result(static_cast<size_t>(length), '\0');
    WideCharToMultiByte(
        CP_ACP, 0, value.data(), static_cast<int>(value.size()), result.data(), length, nullptr, nullptr);
    return result;
}

std::string safeText(const char* value) {
    return value == nullptr ? std::string() : std::string(value);
}

std::string localText(const char* value) {
    if (value == nullptr || *value == '\0') {
        return {};
    }
    const int wideLength = MultiByteToWideChar(CP_ACP, 0, value, -1, nullptr, 0);
    if (wideLength <= 1) {
        return safeText(value);
    }
    std::wstring wide(static_cast<size_t>(wideLength), L'\0');
    MultiByteToWideChar(CP_ACP, 0, value, -1, wide.data(), wideLength);
    wide.pop_back();
    return wideToUtf8(wide);
}

void printHelp() {
    std::cout
        << "DLCV C API 命令行程序\n\n"
        << "命令可用 --then 依次连接，模型在同一进程内保持已加载状态。\n\n"
        << "命令：\n"
        << "  load-model <名称> <模型路径> [--device N]\n"
        << "  list-models\n"
        << "  model-info <名称>\n"
        << "  infer <名称> <图片路径> [--threshold F] [--with-mask true|false] [--calc-mean true|false]\n"
        << "  benchmark <名称> <图片路径> [--threads N] [--runs N]\n"
        << "  free-model <名称>\n"
        << "  free-all-models\n\n"
        << "示例：\n"
        << "  dlcv_infer_c_demo.exe load-model demo model.dvt --device 0 --then model-info demo --then infer demo image.png --then free-model demo\n";
}

bool parseInteger(const std::wstring& text, int minimum, int maximum, int& value) {
    wchar_t* end = nullptr;
    errno = 0;
    const long parsed = std::wcstol(text.c_str(), &end, 10);
    if (errno != 0 || end == text.c_str() || *end != L'\0' ||
        parsed < minimum || parsed > maximum) {
        return false;
    }
    value = static_cast<int>(parsed);
    return true;
}

bool parseDouble(const std::wstring& text, double minimum, double maximum, double& value) {
    wchar_t* end = nullptr;
    errno = 0;
    const double parsed = std::wcstod(text.c_str(), &end);
    if (errno != 0 || end == text.c_str() || *end != L'\0' ||
        !std::isfinite(parsed) || parsed < minimum || parsed > maximum) {
        return false;
    }
    value = parsed;
    return true;
}

bool parseBool(const std::wstring& text, bool& value) {
    if (text == L"true") {
        value = true;
        return true;
    }
    if (text == L"false") {
        value = false;
        return true;
    }
    return false;
}

cv::Mat readImageRgb(const std::wstring& path) {
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"rb") != 0 || file == nullptr) {
        return {};
    }

    _fseeki64(file, 0, SEEK_END);
    const __int64 size = _ftelli64(file);
    _fseeki64(file, 0, SEEK_SET);
    if (size <= 0 || static_cast<unsigned long long>(size) >
        static_cast<unsigned long long>(std::numeric_limits<size_t>::max())) {
        std::fclose(file);
        return {};
    }

    std::vector<unsigned char> bytes(static_cast<size_t>(size));
    const size_t readSize = std::fread(bytes.data(), 1, bytes.size(), file);
    std::fclose(file);
    if (readSize != bytes.size()) {
        return {};
    }

    cv::Mat image = cv::imdecode(bytes, cv::IMREAD_UNCHANGED);
    if (image.empty()) {
        return {};
    }
    if (image.channels() == 4) {
        cv::cvtColor(image, image, cv::COLOR_BGRA2RGB);
    } else if (image.channels() == 3) {
        cv::cvtColor(image, image, cv::COLOR_BGR2RGB);
    } else if (image.channels() != 1) {
        return {};
    }
    if (image.depth() != CV_8U) {
        image.convertTo(image, CV_MAKETYPE(CV_8U, image.channels()));
    }
    if (!image.isContinuous()) {
        image = image.clone();
    }
    return image;
}

DlcvCImage makeCImage(const cv::Mat& image) {
    DlcvCImage result{};
    result.data_ptr = reinterpret_cast<long long>(image.data);
    result.height = image.rows;
    result.width = image.cols;
    result.channel = image.channels();
    return result;
}

std::string makeParamsJson(const InferOptions& options) {
    std::ostringstream stream;
    stream.imbue(std::locale::classic());
    stream << std::setprecision(8)
        << "{\"threshold\":" << options.threshold
        << ",\"with_mask\":" << (options.withMask ? "true" : "false")
        << ",\"calc_mean\":" << (options.calcMean ? "true" : "false")
        << "}";
    return stream.str();
}

bool buildStableSummary(
    const DlcvCResult& result,
    StableResultSummary& summary,
    std::string& error) {
    summary.samples.clear();
    if (result.n < 0 || (result.n > 0 && result.sample_results == nullptr)) {
        error = "样本数据无效";
        return false;
    }
    summary.samples.reserve(static_cast<size_t>(result.n));
    for (int sampleIndex = 0; sampleIndex < result.n; ++sampleIndex) {
        const DlcvCSampleResult& sourceSample = result.sample_results[sampleIndex];
        if (sourceSample.n < 0 || (sourceSample.n > 0 && sourceSample.results == nullptr)) {
            error = "第 " + std::to_string(sampleIndex) + " 个样本数据无效";
            return false;
        }
        std::vector<StableObjectResult> targetSample;
        targetSample.reserve(static_cast<size_t>(sourceSample.n));
        for (int objectIndex = 0; objectIndex < sourceSample.n; ++objectIndex) {
            const DlcvCObjectResult& source = sourceSample.results[objectIndex];
            StableObjectResult target;
            target.categoryId = source.category_id;
            target.categoryName = safeText(source.category_name);
            target.score = source.score;
            target.withBbox = source.with_bbox;
            target.area = source.area;
            target.x = source.x;
            target.y = source.y;
            target.w = source.w;
            target.h = source.h;
            target.withMask = source.with_mask;
            target.maskWidth = source.mask.width;
            target.maskHeight = source.mask.height;
            target.withAngle = source.with_angle;
            target.angle = source.angle;
            target.withMean = source.with_mean;
            target.foregroundMean = source.foreground_mean;
            target.backgroundMean = source.background_mean;
            targetSample.push_back(std::move(target));
        }
        summary.samples.push_back(std::move(targetSample));
    }
    return true;
}

bool nearlyEqual(double left, double right, double tolerance) {
    return std::fabs(left - right) <= tolerance;
}

bool compareStableSummary(
    const StableResultSummary& baseline,
    const StableResultSummary& current,
    std::string& error) {
    if (baseline.samples.size() != current.samples.size()) {
        error = "样本数量变化，基线=" + std::to_string(baseline.samples.size()) +
            "，当前=" + std::to_string(current.samples.size());
        return false;
    }
    for (size_t sampleIndex = 0; sampleIndex < baseline.samples.size(); ++sampleIndex) {
        const auto& expectedSample = baseline.samples[sampleIndex];
        const auto& actualSample = current.samples[sampleIndex];
        if (expectedSample.size() != actualSample.size()) {
            error = "第 " + std::to_string(sampleIndex) + " 个样本的目标数量变化，基线=" +
                std::to_string(expectedSample.size()) + "，当前=" +
                std::to_string(actualSample.size());
            return false;
        }
        for (size_t objectIndex = 0; objectIndex < expectedSample.size(); ++objectIndex) {
            const StableObjectResult& expected = expectedSample[objectIndex];
            const StableObjectResult& actual = actualSample[objectIndex];
            const std::string location = "第 " + std::to_string(sampleIndex) +
                " 个样本、第 " + std::to_string(objectIndex) + " 个目标";
            if (expected.categoryId != actual.categoryId ||
                expected.categoryName != actual.categoryName) {
                error = location + "的类别变化";
                return false;
            }
            if (!nearlyEqual(expected.score, actual.score, 0.00001)) {
                error = location + "的分数变化";
                return false;
            }
            if (expected.withBbox != actual.withBbox ||
                expected.withMask != actual.withMask ||
                expected.withAngle != actual.withAngle ||
                expected.withMean != actual.withMean) {
                error = location + "的结果标志变化";
                return false;
            }
            if (expected.withBbox &&
                (!nearlyEqual(expected.area, actual.area, 0.001) ||
                 !nearlyEqual(expected.x, actual.x, 0.001) ||
                 !nearlyEqual(expected.y, actual.y, 0.001) ||
                 !nearlyEqual(expected.w, actual.w, 0.001) ||
                 !nearlyEqual(expected.h, actual.h, 0.001))) {
                error = location + "的框或面积变化";
                return false;
            }
            if (expected.withMask &&
                (expected.maskWidth != actual.maskWidth ||
                 expected.maskHeight != actual.maskHeight)) {
                error = location + "的 mask 尺寸变化";
                return false;
            }
            if (expected.withAngle && !nearlyEqual(expected.angle, actual.angle, 0.001)) {
                error = location + "的角度变化";
                return false;
            }
            if (expected.withMean &&
                (!nearlyEqual(expected.foregroundMean, actual.foregroundMean, 0.000001) ||
                 !nearlyEqual(expected.backgroundMean, actual.backgroundMean, 0.000001))) {
                error = location + "的均值变化";
                return false;
            }
        }
    }
    return true;
}

std::string formatStableSummary(const StableResultSummary& summary) {
    size_t objectCount = 0;
    std::ostringstream categories;
    bool first = true;
    for (const auto& sample : summary.samples) {
        objectCount += sample.size();
        for (const StableObjectResult& object : sample) {
            if (!first) {
                categories << ", ";
            }
            categories << localText(object.categoryName.c_str());
            first = false;
        }
    }
    return "样本数=" + std::to_string(summary.samples.size()) +
        "，目标数=" + std::to_string(objectCount) +
        "，类别=[" + categories.str() + "]";
}

class CommandApp {
public:
    ~CommandApp() {
        if (apiLoaded_) {
            api_.freeAllModels();
        }
    }

    int initialize() {
        if (!api_.load()) {
            std::wcerr << L"错误：" << api_.lastLoadError() << L"\n";
            return ExitDllError;
        }
        apiLoaded_ = true;
        return ExitSuccess;
    }

    int execute(const std::vector<std::wstring>& command) {
        if (command.empty()) {
            std::cerr << "错误：--then 前后都必须有命令。\n";
            return ExitCommandError;
        }
        const std::wstring& name = command.front();
        if (name == L"load-model") return loadModel(command);
        if (name == L"list-models") return listModels(command);
        if (name == L"model-info") return modelInfo(command);
        if (name == L"infer") return infer(command);
        if (name == L"benchmark") return benchmark(command);
        if (name == L"free-model") return freeModel(command);
        if (name == L"free-all-models") return freeAllModels(command);
        if (name == L"help" || name == L"--help" || name == L"-h") {
            printHelp();
            return ExitSuccess;
        }
        std::cerr << "错误：未知命令 " << wideToUtf8(name) << "。\n";
        return ExitCommandError;
    }

private:
    int requireModel(const std::wstring& name, int& modelIndex) const {
        const auto it = models_.find(name);
        if (it == models_.end()) {
            std::cerr << "错误：未加载名称为 " << wideToUtf8(name) << " 的模型。\n";
            return ExitModelError;
        }
        modelIndex = it->second;
        return ExitSuccess;
    }

    int loadModel(const std::vector<std::wstring>& args) {
        if (args.size() < 3) {
            std::cerr << "错误：load-model 需要模型名称和模型路径。\n";
            return ExitCommandError;
        }
        const std::wstring& name = args[1];
        const std::wstring& path = args[2];
        if (models_.find(name) != models_.end()) {
            std::cerr << "错误：模型名称已存在：" << wideToUtf8(name) << "。\n";
            return ExitModelError;
        }

        int deviceId = 0;
        for (size_t i = 3; i < args.size(); i += 2) {
            if (i + 1 >= args.size() || args[i] != L"--device" ||
                !parseInteger(args[i + 1], -1, std::numeric_limits<int>::max(), deviceId)) {
                std::cerr << "错误：load-model 只接受 --device N。\n";
                return ExitCommandError;
            }
        }

        const std::string localPath = wideToAnsi(path);
        if (localPath.empty()) {
            std::cerr << "错误：模型路径无法转换为系统字符编码。\n";
            return ExitModelError;
        }
        const int modelIndex = api_.loadModel(localPath.c_str(), deviceId);
        if (modelIndex < 0) {
            std::cerr << "错误：模型加载失败：" << localText(api_.getLastError()) << "\n";
            return ExitModelError;
        }
        models_.emplace(name, modelIndex);
        std::cout << "模型加载成功：名称=" << wideToUtf8(name)
            << "，index=" << modelIndex << "，device=" << deviceId << "。\n";
        return ExitSuccess;
    }

    int listModels(const std::vector<std::wstring>& args) const {
        if (args.size() != 1) {
            std::cerr << "错误：list-models 不接受参数。\n";
            return ExitCommandError;
        }
        if (models_.empty()) {
            std::cout << "当前没有已加载模型。\n";
            return ExitSuccess;
        }
        std::cout << "已加载模型数量：" << models_.size() << "\n";
        for (const auto& item : models_) {
            std::cout << "  名称=" << wideToUtf8(item.first) << "，index=" << item.second << "\n";
        }
        return ExitSuccess;
    }

    int modelInfo(const std::vector<std::wstring>& args) const {
        if (args.size() != 2) {
            std::cerr << "错误：model-info 需要模型名称。\n";
            return ExitCommandError;
        }
        int modelIndex = -1;
        const int status = requireModel(args[1], modelIndex);
        if (status != ExitSuccess) return status;

        const char* info = api_.getModelInfo(modelIndex);
        if (info == nullptr) {
            std::cerr << "错误：读取模型信息失败：" << localText(api_.getLastError()) << "\n";
            return ExitModelError;
        }
        std::cout << "模型信息：名称=" << wideToUtf8(args[1]) << "，index=" << modelIndex << "\n"
            << info << "\n";
        api_.freeString(info);
        return ExitSuccess;
    }

    int infer(const std::vector<std::wstring>& args) const {
        if (args.size() < 3) {
            std::cerr << "错误：infer 需要模型名称和图片路径。\n";
            return ExitCommandError;
        }
        int modelIndex = -1;
        const int status = requireModel(args[1], modelIndex);
        if (status != ExitSuccess) return status;

        InferOptions options;
        for (size_t i = 3; i < args.size(); i += 2) {
            if (i + 1 >= args.size()) {
                std::cerr << "错误：infer 选项缺少参数值。\n";
                return ExitCommandError;
            }
            if (args[i] == L"--threshold") {
                if (!parseDouble(args[i + 1], 0.0, 1.0, options.threshold)) {
                    std::cerr << "错误：--threshold 必须是 0 到 1 之间的数字。\n";
                    return ExitCommandError;
                }
            } else if (args[i] == L"--with-mask") {
                if (!parseBool(args[i + 1], options.withMask)) {
                    std::cerr << "错误：--with-mask 必须是 true 或 false。\n";
                    return ExitCommandError;
                }
            } else if (args[i] == L"--calc-mean") {
                if (!parseBool(args[i + 1], options.calcMean)) {
                    std::cerr << "错误：--calc-mean 必须是 true 或 false。\n";
                    return ExitCommandError;
                }
            } else {
                std::cerr << "错误：infer 不支持选项 " << wideToUtf8(args[i]) << "。\n";
                return ExitCommandError;
            }
        }

        cv::Mat image;
        try {
            image = readImageRgb(args[2]);
        } catch (const cv::Exception& exception) {
            std::cerr << "错误：图片解码失败：" << exception.what() << "\n";
            return ExitInferError;
        }
        if (image.empty()) {
            std::cerr << "错误：无法读取图片：" << wideToUtf8(args[2]) << "。\n";
            return ExitInferError;
        }

        DlcvCImage cImage = makeCImage(image);
        DlcvCImageList imageList{&cImage, 1};
        const std::string params = makeParamsJson(options);
        const auto start = std::chrono::steady_clock::now();
        DlcvCResult result = api_.inferWithParams(modelIndex, &imageList, params.c_str());
        const auto elapsed = std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - start).count();

        if (result.code != 0) {
            std::cerr << "错误：推理失败，code=" << result.code
                << "，message=" << localText(result.message) << "。\n";
            api_.freeModelResult(&result);
            return ExitInferError;
        }

        size_t objectCount = 0;
        for (int sampleIndex = 0; sampleIndex < result.n; ++sampleIndex) {
            const DlcvCSampleResult& sample = result.sample_results[sampleIndex];
            objectCount += static_cast<size_t>(sample.n);
        }
        std::cout << std::fixed << std::setprecision(3)
            << "推理成功：名称=" << wideToUtf8(args[1])
            << "，index=" << modelIndex
            << "，图片=" << wideToUtf8(args[2])
            << "，耗时=" << elapsed << " 毫秒"
            << "，样本数=" << result.n
            << "，目标数=" << objectCount << "。\n";

        for (int sampleIndex = 0; sampleIndex < result.n; ++sampleIndex) {
            const DlcvCSampleResult& sample = result.sample_results[sampleIndex];
            for (int objectIndex = 0; objectIndex < sample.n; ++objectIndex) {
                const DlcvCObjectResult& object = sample.results[objectIndex];
                std::cout << "  样本 " << sampleIndex << "，目标 " << objectIndex
                    << "：类别=" << localText(object.category_name)
                    << "，类别编号=" << object.category_id
                    << "，分数=" << object.score;
                if (object.with_bbox) {
                    std::cout << "，框=[" << object.x << ", " << object.y << ", "
                        << object.w << ", " << object.h << "]";
                }
                if (object.with_mask) {
                    std::cout << "，mask=" << object.mask.width << "x" << object.mask.height;
                }
                if (object.with_mean) {
                    std::cout << "，前景均值=" << object.foreground_mean
                        << "，背景均值=" << object.background_mean;
                }
                std::cout << "。\n";
            }
        }
        api_.freeModelResult(&result);
        return ExitSuccess;
    }

    int benchmark(const std::vector<std::wstring>& args) const {
        if (args.size() < 3) {
            std::cerr << "错误：benchmark 需要模型名称和图片路径。\n";
            return ExitCommandError;
        }
        int modelIndex = -1;
        const int status = requireModel(args[1], modelIndex);
        if (status != ExitSuccess) return status;

        int threadCount = 1;
        int runs = 10;
        for (size_t i = 3; i < args.size(); i += 2) {
            if (i + 1 >= args.size()) {
                std::cerr << "错误：benchmark 选项缺少参数值。\n";
                return ExitCommandError;
            }
            if (args[i] == L"--threads") {
                if (!parseInteger(args[i + 1], 1, 256, threadCount)) {
                    std::cerr << "错误：--threads 必须是 1 到 256 之间的整数。\n";
                    return ExitCommandError;
                }
            } else if (args[i] == L"--runs") {
                if (!parseInteger(args[i + 1], 1, 1000000, runs)) {
                    std::cerr << "错误：--runs 必须是正整数。\n";
                    return ExitCommandError;
                }
            } else {
                std::cerr << "错误：benchmark 不支持选项 " << wideToUtf8(args[i]) << "。\n";
                return ExitCommandError;
            }
        }

        cv::Mat image;
        try {
            image = readImageRgb(args[2]);
        } catch (const cv::Exception& exception) {
            std::cerr << "错误：图片解码失败：" << exception.what() << "\n";
            return ExitBenchmarkError;
        }
        if (image.empty()) {
            std::cerr << "错误：无法读取图片：" << wideToUtf8(args[2]) << "。\n";
            return ExitBenchmarkError;
        }

        InferOptions options;
        options.withMask = false;
        const std::string params = makeParamsJson(options);
        DlcvCImage baselineImage = makeCImage(image);
        DlcvCImageList baselineImageList{&baselineImage, 1};
        DlcvCResult baselineResult = api_.inferWithParams(
            modelIndex, &baselineImageList, params.c_str());
        if (baselineResult.code != 0) {
            std::cerr << "错误：测速基线推理失败，code=" << baselineResult.code
                << "，message=" << localText(baselineResult.message) << "。\n";
            api_.freeModelResult(&baselineResult);
            return ExitBenchmarkError;
        }
        StableResultSummary baselineSummary;
        std::string baselineError;
        if (!buildStableSummary(baselineResult, baselineSummary, baselineError)) {
            std::cerr << "错误：测速基线摘要生成失败：" << baselineError << "。\n";
            api_.freeModelResult(&baselineResult);
            return ExitBenchmarkError;
        }
        api_.freeModelResult(&baselineResult);
        std::cout << "测速基线：index=" << modelIndex << "，"
            << formatStableSummary(baselineSummary) << "。\n"
            << "测速方式：所有线程共享调用 index=" << modelIndex
            << "；流程模型可能在库内部串行执行。\n";

        std::atomic<long long> successCount{0};
        std::atomic<long long> apiFailureCount{0};
        std::atomic<long long> mismatchCount{0};
        std::atomic<long long> latencyNanoseconds{0};
        std::mutex errorMutex;
        std::string firstError;
        std::vector<std::thread> workers;
        workers.reserve(static_cast<size_t>(threadCount));

        const auto totalStart = std::chrono::steady_clock::now();
        for (int threadIndex = 0; threadIndex < threadCount; ++threadIndex) {
            workers.emplace_back([&, threadIndex]() {
                (void)threadIndex;
                DlcvCImage cImage = makeCImage(image);
                DlcvCImageList imageList{&cImage, 1};
                for (int runIndex = 0; runIndex < runs; ++runIndex) {
                    const auto start = std::chrono::steady_clock::now();
                    DlcvCResult result = api_.inferWithParams(modelIndex, &imageList, params.c_str());
                    const auto elapsed = std::chrono::duration_cast<std::chrono::nanoseconds>(
                        std::chrono::steady_clock::now() - start).count();
                    if (result.code == 0) {
                        StableResultSummary currentSummary;
                        std::string compareError;
                        if (buildStableSummary(result, currentSummary, compareError) &&
                            compareStableSummary(baselineSummary, currentSummary, compareError)) {
                            successCount.fetch_add(1, std::memory_order_relaxed);
                            latencyNanoseconds.fetch_add(elapsed, std::memory_order_relaxed);
                        } else {
                            mismatchCount.fetch_add(1, std::memory_order_relaxed);
                            std::lock_guard<std::mutex> lock(errorMutex);
                            if (firstError.empty()) {
                                firstError = "结果不一致：" + compareError;
                            }
                        }
                    } else {
                        apiFailureCount.fetch_add(1, std::memory_order_relaxed);
                        std::lock_guard<std::mutex> lock(errorMutex);
                        if (firstError.empty()) {
                            firstError = "code=" + std::to_string(result.code) +
                                "，message=" + localText(result.message);
                        }
                    }
                    api_.freeModelResult(&result);
                }
            });
        }
        for (std::thread& worker : workers) {
            worker.join();
        }
        const double totalMilliseconds = std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - totalStart).count();
        const long long successes = successCount.load(std::memory_order_relaxed);
        const long long apiFailures = apiFailureCount.load(std::memory_order_relaxed);
        const long long mismatches = mismatchCount.load(std::memory_order_relaxed);
        const long long failures = apiFailures + mismatches;
        const double averageMilliseconds = successes == 0 ? 0.0 :
            static_cast<double>(latencyNanoseconds.load(std::memory_order_relaxed)) /
                1000000.0 / static_cast<double>(successes);
        const double throughput = totalMilliseconds <= 0.0 ? 0.0 :
            static_cast<double>(successes) * 1000.0 / totalMilliseconds;

        std::cout << std::fixed << std::setprecision(3)
            << "测速完成：名称=" << wideToUtf8(args[1])
            << "，index=" << modelIndex
            << "，线程数=" << threadCount
            << "，每线程次数=" << runs
            << "，成功=" << successes
            << "，接口失败=" << apiFailures
            << "，结果不一致=" << mismatches
            << "，总耗时=" << totalMilliseconds << " 毫秒"
            << "，平均延迟=" << averageMilliseconds << " 毫秒"
            << "，吞吐=" << throughput << " 次/秒。\n";
        if (failures != 0) {
            std::cerr << "错误：测速验证失败：" << firstError << "。\n";
            return ExitBenchmarkError;
        }
        return ExitSuccess;
    }

    int freeModel(const std::vector<std::wstring>& args) {
        if (args.size() != 2) {
            std::cerr << "错误：free-model 需要模型名称。\n";
            return ExitCommandError;
        }
        const auto it = models_.find(args[1]);
        if (it == models_.end()) {
            std::cerr << "错误：未加载名称为 " << wideToUtf8(args[1]) << " 的模型。\n";
            return ExitModelError;
        }
        const int modelIndex = it->second;
        if (api_.freeModel(modelIndex) != 0) {
            std::cerr << "错误：释放模型失败：" << localText(api_.getLastError()) << "\n";
            return ExitModelError;
        }
        models_.erase(it);
        std::cout << "模型已释放：名称=" << wideToUtf8(args[1])
            << "，index=" << modelIndex << "。\n";
        return ExitSuccess;
    }

    int freeAllModels(const std::vector<std::wstring>& args) {
        if (args.size() != 1) {
            std::cerr << "错误：free-all-models 不接受参数。\n";
            return ExitCommandError;
        }
        api_.freeAllModels();
        const size_t count = models_.size();
        models_.clear();
        std::cout << "全部模型已释放，名称数量=" << count << "。\n";
        return ExitSuccess;
    }

    DlcvInferApi api_;
    bool apiLoaded_ = false;
    std::map<std::wstring, int> models_;
};

bool splitCommands(
    int argc,
    wchar_t* argv[],
    std::vector<std::vector<std::wstring>>& commands) {
    commands.clear();
    commands.emplace_back();
    for (int i = 1; i < argc; ++i) {
        if (std::wstring(argv[i]) == L"--then") {
            if (commands.back().empty()) {
                return false;
            }
            commands.emplace_back();
        } else {
            commands.back().emplace_back(argv[i]);
        }
    }
    return !commands.empty() && !commands.back().empty();
}

}

int wmain(int argc, wchar_t* argv[]) {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    if (argc <= 1) {
        printHelp();
        return ExitSuccess;
    }

    std::vector<std::vector<std::wstring>> commands;
    if (!splitCommands(argc, argv, commands)) {
        std::cerr << "错误：--then 前后都必须有命令。\n";
        return ExitCommandError;
    }

    CommandApp app;
    const int initializeStatus = app.initialize();
    if (initializeStatus != ExitSuccess) {
        return initializeStatus;
    }
    for (const auto& command : commands) {
        const int status = app.execute(command);
        if (status != ExitSuccess) {
            return status;
        }
    }
    return ExitSuccess;
}
