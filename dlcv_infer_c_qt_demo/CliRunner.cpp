#include "CliRunner.h"

#include <QByteArray>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QSaveFile>

#include <cmath>
#include <cstdint>
#include <iostream>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "DlcvInferApi.h"
#include "json/json.hpp"

namespace {

using json = nlohmann::json;

struct InferOptions {
    QString modelPath;
    QString imagePath;
    QString outputPath;
    double threshold = 0.0;
    int device = 0;
    bool withMask = true;
    bool calcMean = false;
    bool hasModel = false;
    bool hasImage = false;
    bool hasThreshold = false;
    bool hasDevice = false;
    bool hasWithMask = false;
    bool hasCalcMean = false;
    bool hasOutput = false;
};

struct PathSummary {
    int count = 0;
    json scores = json::array();
    json categories = json::array();
    json belowThreshold = json::array();
    json withMeans = json::array();
    json foregroundMeans = json::array();
    json backgroundMeans = json::array();
    std::vector<double> comparableScores;
    std::vector<std::string> comparableCategories;
    std::vector<bool> comparableWithMeans;
    std::vector<double> comparableForegroundMeans;
    std::vector<double> comparableBackgroundMeans;
};

class ApiCleanupGuard {
public:
    explicit ApiCleanupGuard(DlcvInferApi& api) : api_(api) {}
    ~ApiCleanupGuard() {
        if (!api_.isLoaded()) {
            return;
        }
        std::ostringstream buffer;
        std::streambuf* previous = std::cout.rdbuf(buffer.rdbuf());
        try {
            api_.freeAllModels();
        } catch (...) {
        }
        std::cout.rdbuf(previous);
    }

    ApiCleanupGuard(const ApiCleanupGuard&) = delete;
    ApiCleanupGuard& operator=(const ApiCleanupGuard&) = delete;

private:
    DlcvInferApi& api_;
};

class CResultGuard {
public:
    CResultGuard(DlcvInferApi& api, DlcvCResult value) : api_(api), value_(value) {}
    ~CResultGuard() { api_.freeModelResult(&value_); }

    CResultGuard(const CResultGuard&) = delete;
    CResultGuard& operator=(const CResultGuard&) = delete;

    const DlcvCResult& get() const { return value_; }

private:
    DlcvInferApi& api_;
    DlcvCResult value_{};
};

class CStringGuard {
public:
    CStringGuard(DlcvInferApi& api, const char* value) : api_(api), value_(value) {}
    ~CStringGuard() { api_.freeString(value_); }

    CStringGuard(const CStringGuard&) = delete;
    CStringGuard& operator=(const CStringGuard&) = delete;

    const char* get() const { return value_; }

private:
    DlcvInferApi& api_;
    const char* value_ = nullptr;
};

class CoutSilencer {
public:
    CoutSilencer() : previous_(std::cout.rdbuf(buffer_.rdbuf())) {}
    ~CoutSilencer() { std::cout.rdbuf(previous_); }

    CoutSilencer(const CoutSilencer&) = delete;
    CoutSilencer& operator=(const CoutSilencer&) = delete;

private:
    std::ostringstream buffer_;
    std::streambuf* previous_ = nullptr;
};

std::string ToUtf8(const QString& value) {
    const QByteArray bytes = value.toUtf8();
    return std::string(bytes.constData(), static_cast<size_t>(bytes.size()));
}

QString DecodeApiText(const char* value) {
    if (value == nullptr) {
        return QStringLiteral("unknown error");
    }
    const QByteArray bytes(value);
    const QString utf8 = QString::fromUtf8(bytes);
    if (utf8.toUtf8() == bytes) {
        return utf8;
    }
    return QString::fromLocal8Bit(bytes);
}

std::string CategoryToUtf8(const char* value) {
    if (value == nullptr) {
        return {};
    }
    return ToUtf8(QString::fromLocal8Bit(value));
}

bool ParseBool(const QString& value, bool& parsed) {
    if (value.compare(QStringLiteral("true"), Qt::CaseInsensitive) == 0) {
        parsed = true;
        return true;
    }
    if (value.compare(QStringLiteral("false"), Qt::CaseInsensitive) == 0) {
        parsed = false;
        return true;
    }
    return false;
}

bool ParseInferOptions(const QStringList& args, InferOptions& options, QString& error) {
    for (int i = 2; i < args.size(); ++i) {
        const QString option = args.at(i);
        if (i + 1 >= args.size()) {
            error = QStringLiteral("missing value for %1").arg(option);
            return false;
        }
        const QString value = args.at(++i);

        if (option == QStringLiteral("--model")) {
            if (options.hasModel) {
                error = QStringLiteral("duplicate option: --model");
                return false;
            }
            options.modelPath = value;
            options.hasModel = true;
        } else if (option == QStringLiteral("--image")) {
            if (options.hasImage) {
                error = QStringLiteral("duplicate option: --image");
                return false;
            }
            options.imagePath = value;
            options.hasImage = true;
        } else if (option == QStringLiteral("--threshold")) {
            if (options.hasThreshold) {
                error = QStringLiteral("duplicate option: --threshold");
                return false;
            }
            bool ok = false;
            const double parsed = value.toDouble(&ok);
            if (!ok || !std::isfinite(parsed) || parsed < 0.0 || parsed > 1.0) {
                error = QStringLiteral("--threshold must be a number in [0, 1]");
                return false;
            }
            options.threshold = parsed;
            options.hasThreshold = true;
        } else if (option == QStringLiteral("--device")) {
            if (options.hasDevice) {
                error = QStringLiteral("duplicate option: --device");
                return false;
            }
            bool ok = false;
            const int parsed = value.toInt(&ok);
            if (!ok) {
                error = QStringLiteral("--device must be an integer");
                return false;
            }
            options.device = parsed;
            options.hasDevice = true;
        } else if (option == QStringLiteral("--with-mask")) {
            if (options.hasWithMask) {
                error = QStringLiteral("duplicate option: --with-mask");
                return false;
            }
            bool parsed = false;
            if (!ParseBool(value, parsed)) {
                error = QStringLiteral("--with-mask must be true or false");
                return false;
            }
            options.withMask = parsed;
            options.hasWithMask = true;
        } else if (option == QStringLiteral("--calc-mean")) {
            if (options.hasCalcMean) {
                error = QStringLiteral("duplicate option: --calc-mean");
                return false;
            }
            bool parsed = false;
            if (!ParseBool(value, parsed)) {
                error = QStringLiteral("--calc-mean must be true or false");
                return false;
            }
            options.calcMean = parsed;
            options.hasCalcMean = true;
        } else if (option == QStringLiteral("--output")) {
            if (options.hasOutput) {
                error = QStringLiteral("duplicate option: --output");
                return false;
            }
            options.outputPath = value;
            options.hasOutput = true;
        } else {
            error = QStringLiteral("unknown option: %1").arg(option);
            return false;
        }
    }

    if (!options.hasModel || options.modelPath.isEmpty()) {
        error = QStringLiteral("--model is required");
        return false;
    }
    if (!options.hasImage || options.imagePath.isEmpty()) {
        error = QStringLiteral("--image is required");
        return false;
    }
    if (!options.hasThreshold) {
        error = QStringLiteral("--threshold is required");
        return false;
    }
    if (options.hasOutput && options.outputPath.isEmpty()) {
        error = QStringLiteral("--output cannot be empty");
        return false;
    }

    const QFileInfo modelInfo(options.modelPath);
    if (modelInfo.suffix().compare(QStringLiteral("dvsp"), Qt::CaseInsensitive) == 0) {
        error = QStringLiteral(".dvsp models are not supported by infer");
        return false;
    }
    if (!modelInfo.exists() || !modelInfo.isFile()) {
        error = QStringLiteral("model file does not exist");
        return false;
    }
    const QFileInfo imageInfo(options.imagePath);
    if (!imageInfo.exists() || !imageInfo.isFile()) {
        error = QStringLiteral("image file does not exist");
        return false;
    }

    if (options.hasOutput) {
        const QFileInfo outputInfo(options.outputPath);
        const QFileInfo outputDirectory(outputInfo.absolutePath());
        if (!outputDirectory.exists() || !outputDirectory.isDir()) {
            error = QStringLiteral("--output parent directory does not exist");
            return false;
        }

#ifdef _WIN32
        constexpr Qt::CaseSensitivity pathCaseSensitivity = Qt::CaseInsensitive;
#else
        constexpr Qt::CaseSensitivity pathCaseSensitivity = Qt::CaseSensitive;
#endif
        const QString outputFullPath = QDir::cleanPath(outputInfo.absoluteFilePath());
        const QString modelCanonicalPath = modelInfo.canonicalFilePath();
        const QString imageCanonicalPath = imageInfo.canonicalFilePath();
        const QString modelFullPath = QDir::cleanPath(
            modelCanonicalPath.isEmpty() ? modelInfo.absoluteFilePath() : modelCanonicalPath);
        const QString imageFullPath = QDir::cleanPath(
            imageCanonicalPath.isEmpty() ? imageInfo.absoluteFilePath() : imageCanonicalPath);
        const QString outputCanonicalPath = outputInfo.canonicalFilePath();
        const QString comparableOutputPath = QDir::cleanPath(
            outputCanonicalPath.isEmpty() ? outputFullPath : outputCanonicalPath);
        if (comparableOutputPath.compare(modelFullPath, pathCaseSensitivity) == 0 ||
            comparableOutputPath.compare(imageFullPath, pathCaseSensitivity) == 0) {
            error = QStringLiteral("--output cannot overwrite the model or image file");
            return false;
        }
        options.outputPath = outputFullPath;
    }
    return true;
}

cv::Mat LoadImageUnicode(const QString& imagePath) {
    QFile file(imagePath);
    if (!file.open(QIODevice::ReadOnly)) {
        throw std::runtime_error(ToUtf8(
            QStringLiteral("failed to open image: %1").arg(file.errorString())));
    }

    const QByteArray encoded = file.readAll();
    if (encoded.isEmpty()) {
        throw std::runtime_error("image file is empty");
    }
    if (encoded.size() > std::numeric_limits<int>::max()) {
        throw std::runtime_error("image file is too large");
    }

    const cv::Mat encodedMat(
        1,
        static_cast<int>(encoded.size()),
        CV_8UC1,
        const_cast<char*>(encoded.constData()));
    cv::Mat decoded = cv::imdecode(encodedMat, cv::IMREAD_UNCHANGED);
    if (decoded.empty()) {
        throw std::runtime_error("image decode failed");
    }
    return decoded;
}

cv::Mat PrepareImageForInference(const cv::Mat& decodedImage) {
    if (decodedImage.empty()) {
        return {};
    }

    cv::Mat output;
    if (decodedImage.channels() == 3) {
        cv::cvtColor(decodedImage, output, cv::COLOR_BGR2RGB);
    } else if (decodedImage.channels() == 4) {
        cv::cvtColor(decodedImage, output, cv::COLOR_BGRA2RGB);
    } else {
        output = decodedImage.clone();
    }
    if (!output.empty() && !output.isContinuous()) {
        output = output.clone();
    }
    return output;
}

DlcvCImage MakeCImage(const cv::Mat& image) {
    DlcvCImage result{};
    result.data_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(image.data));
    result.height = image.rows;
    result.width = image.cols;
    result.channel = image.channels();
    return result;
}

void AddSummaryItem(
    PathSummary& summary,
    double score,
    const std::string& category,
    bool withMean,
    double foregroundMean,
    double backgroundMean,
    double threshold) {
    const int index = summary.count;
    ++summary.count;
    summary.categories.push_back(category);
    summary.comparableCategories.push_back(category);
    summary.comparableScores.push_back(score);
    summary.withMeans.push_back(withMean);
    summary.comparableWithMeans.push_back(withMean);
    summary.comparableForegroundMeans.push_back(foregroundMean);
    summary.comparableBackgroundMeans.push_back(backgroundMean);
    if (std::isfinite(foregroundMean) && std::isfinite(backgroundMean)) {
        summary.foregroundMeans.push_back(foregroundMean);
        summary.backgroundMeans.push_back(backgroundMean);
    } else {
        summary.foregroundMeans.push_back(nullptr);
        summary.backgroundMeans.push_back(nullptr);
    }

    if (!std::isfinite(score)) {
        summary.scores.push_back(nullptr);
        summary.belowThreshold.push_back(json{
            {"index", index},
            {"score", nullptr},
            {"category", category}
        });
        return;
    }

    summary.scores.push_back(score);
    if (score < threshold) {
        summary.belowThreshold.push_back(json{
            {"index", index},
            {"score", score},
            {"category", category}
        });
    }
}

PathSummary SummarizeStructured(const DlcvCResult& result, double threshold) {
    PathSummary summary;
    if (result.sample_results == nullptr || result.n <= 0) {
        return summary;
    }
    for (int sampleIndex = 0; sampleIndex < result.n; ++sampleIndex) {
        const DlcvCSampleResult& sample = result.sample_results[sampleIndex];
        if (sample.results == nullptr || sample.n <= 0) {
            continue;
        }
        for (int objectIndex = 0; objectIndex < sample.n; ++objectIndex) {
            const DlcvCObjectResult& object = sample.results[objectIndex];
            AddSummaryItem(
                summary,
                static_cast<double>(object.score),
                CategoryToUtf8(object.category_name),
                object.with_mean,
                static_cast<double>(object.foreground_mean),
                static_cast<double>(object.background_mean),
                threshold);
        }
    }
    return summary;
}

bool TryReadJsonBool(const json& token, const char* key, bool& value) {
    try {
        if (token.is_object() && token.contains(key) && token.at(key).is_boolean()) {
            value = token.at(key).get<bool>();
            return true;
        }
    } catch (...) {
    }
    return false;
}

bool TryReadJsonNumber(const json& token, const char* key, double& value) {
    try {
        if (!token.is_object() || !token.contains(key)) {
            return false;
        }
        const json& jsonValue = token.at(key);
        if (jsonValue.is_number()) {
            value = jsonValue.get<double>();
            return std::isfinite(value);
        }

    } catch (...) {
    }
    return false;
}

PathSummary SummarizeJson(const json& result, double threshold) {
    const json* resultList = &result;
    if (result.is_object() && result.contains("result_list") && result.at("result_list").is_array()) {
        resultList = &result.at("result_list");
    }
    if (!resultList->is_array()) {
        throw std::runtime_error("C JSON inference did not return an array or result_list wrapper");
    }

    PathSummary summary;
    for (const auto& token : *resultList) {
        if (!token.is_object()) {
            AddSummaryItem(
                summary,
                std::numeric_limits<double>::quiet_NaN(),
                std::string(),
                false,
                std::numeric_limits<double>::quiet_NaN(),
                std::numeric_limits<double>::quiet_NaN(),
                threshold);
            continue;
        }

        std::string category;
        try {
            if (token.contains("category_name") && token.at("category_name").is_string()) {
                category = token.at("category_name").get<std::string>();
            }
        } catch (...) {
        }

        double score = std::numeric_limits<double>::quiet_NaN();
        (void)TryReadJsonNumber(token, "score", score);
        bool withMean = false;
        double foregroundMean = 0.0;
        double backgroundMean = 0.0;
        (void)TryReadJsonBool(token, "with_mean", withMean);
        if (withMean) {
            foregroundMean = std::numeric_limits<double>::quiet_NaN();
            backgroundMean = std::numeric_limits<double>::quiet_NaN();
            (void)TryReadJsonNumber(token, "foreground_mean", foregroundMean);
            (void)TryReadJsonNumber(token, "background_mean", backgroundMean);
        }
        AddSummaryItem(summary, score, category, withMean, foregroundMean, backgroundMean, threshold);
    }
    return summary;
}

json PathSummaryToJson(const PathSummary& summary) {
    return json{
        {"count", summary.count},
        {"scores", summary.scores},
        {"categories", summary.categories},
        {"below_threshold", summary.belowThreshold},
        {"with_mean", summary.withMeans},
        {"foreground_mean", summary.foregroundMeans},
        {"background_mean", summary.backgroundMeans}
    };
}

bool AreConsistent(const PathSummary& left, const PathSummary& right) {
    if (left.count != right.count ||
        left.comparableScores.size() != right.comparableScores.size() ||
        left.comparableCategories != right.comparableCategories ||
        left.comparableWithMeans != right.comparableWithMeans) {
        return false;
    }
    for (size_t i = 0; i < left.comparableScores.size(); ++i) {
        if (!std::isfinite(left.comparableScores[i]) || !std::isfinite(right.comparableScores[i]) ||
            std::abs(left.comparableScores[i] - right.comparableScores[i]) > 1e-6) {
            return false;
        }
        if (left.comparableWithMeans[i] &&
            (!std::isfinite(left.comparableForegroundMeans[i]) ||
             !std::isfinite(right.comparableForegroundMeans[i]) ||
             !std::isfinite(left.comparableBackgroundMeans[i]) ||
             !std::isfinite(right.comparableBackgroundMeans[i]) ||
             std::abs(left.comparableForegroundMeans[i] - right.comparableForegroundMeans[i]) > 1e-6 ||
             std::abs(left.comparableBackgroundMeans[i] - right.comparableBackgroundMeans[i]) > 1e-6)) {
            return false;
        }
    }
    return true;
}

bool HasCompleteMeans(const PathSummary& summary) {
    for (size_t i = 0; i < summary.comparableWithMeans.size(); ++i) {
        if (!summary.comparableWithMeans[i] ||
            !std::isfinite(summary.comparableForegroundMeans[i]) ||
            !std::isfinite(summary.comparableBackgroundMeans[i])) {
            return false;
        }
    }
    return true;
}

void WriteJsonFile(const QString& outputPath, const std::string& jsonText) {
    QSaveFile file(outputPath);
    if (!file.open(QIODevice::WriteOnly)) {
        throw std::runtime_error(ToUtf8(
            QStringLiteral("failed to open output: %1").arg(file.errorString())));
    }
    const QByteArray bytes(jsonText.data(), static_cast<int>(jsonText.size()));
    if (file.write(bytes) != bytes.size()) {
        throw std::runtime_error(ToUtf8(
            QStringLiteral("failed to write output: %1").arg(file.errorString())));
    }
    if (!file.commit()) {
        throw std::runtime_error(ToUtf8(
            QStringLiteral("failed to commit output: %1").arg(file.errorString())));
    }
}

std::string LastApiError(DlcvInferApi& api) {
    return ToUtf8(DecodeApiText(api.getLastError()));
}

int RunInferCommand(const InferOptions& options) {
    DlcvInferApi api;
    if (!api.load()) {
        throw std::runtime_error(ToUtf8(QString::fromStdWString(api.lastError())));
    }
    ApiCleanupGuard cleanup(api);

    PathSummary structuredSummary;
    PathSummary jsonSummary;
    bool releaseCheckPassed = false;
    {
        CoutSilencer silenceApiLogs;
        const QByteArray modelPath = options.modelPath.toLocal8Bit();
        const int modelIndex = api.loadModel(modelPath.constData(), options.device);
        if (modelIndex == -1) {
            throw std::runtime_error(LastApiError(api));
        }

        const cv::Mat decoded = LoadImageUnicode(options.imagePath);
        if (decoded.depth() != CV_8U) {
            throw std::runtime_error("the formal C image API requires 8-bit input");
        }
        const cv::Mat inferImage = PrepareImageForInference(decoded);
        if (inferImage.empty()) {
            throw std::runtime_error("input image channel conversion failed");
        }
        DlcvCImage image = MakeCImage(inferImage);
        DlcvCImageList imageList{};
        imageList.images = &image;
        imageList.n = 1;
        const json params = {
            {"threshold", options.threshold},
            {"with_mask", options.withMask},
            {"calc_mean", options.calcMean}
        };
        const std::string paramsText = params.dump();

        {
            CResultGuard structuredResult(
                api,
                api.inferWithParams(modelIndex, &imageList, paramsText.c_str()));
            if (structuredResult.get().code != 0) {
                throw std::runtime_error(ToUtf8(DecodeApiText(structuredResult.get().message)));
            }
            structuredSummary = SummarizeStructured(structuredResult.get(), options.threshold);

            CStringGuard jsonResult(api, api.inferJson(modelIndex, &image, paramsText.c_str()));
            if (jsonResult.get() == nullptr) {
                throw std::runtime_error(LastApiError(api));
            }
            jsonSummary = SummarizeJson(json::parse(jsonResult.get()), options.threshold);
        }

        const int firstFreeResult = api.freeModel(modelIndex);
        const std::string firstFreeError = api.getLastError() == nullptr ? std::string{} : api.getLastError();
        const int secondFreeResult = api.freeModel(modelIndex);
        const std::string secondFreeError = api.getLastError() == nullptr ? std::string{} : api.getLastError();
        CStringGuard modelInfoAfterFree(api, api.getModelInfo(modelIndex));
        releaseCheckPassed = firstFreeResult == 0 && firstFreeError.empty() &&
            secondFreeResult == 0 && secondFreeError.empty() && modelInfoAfterFree.get() == nullptr;
    }

    const bool consistent = AreConsistent(structuredSummary, jsonSummary);
    const bool thresholdCheckPassed =
        structuredSummary.belowThreshold.empty() && jsonSummary.belowThreshold.empty();
    const bool meanCheckPassed =
        !options.calcMean || (HasCompleteMeans(structuredSummary) && HasCompleteMeans(jsonSummary));
    const json summary = {
        {"language", "c"},
        {"model", ToUtf8(options.modelPath)},
        {"image", ToUtf8(options.imagePath)},
        {"threshold", options.threshold},
        {"device", options.device},
        {"with_mask", options.withMask},
        {"calc_mean", options.calcMean},
        {"structured", PathSummaryToJson(structuredSummary)},
        {"json", PathSummaryToJson(jsonSummary)},
        {"inspection", json::object({
            {"present", false},
            {"ok", nullptr},
            {"reason", nullptr}
        })},
        {"inspection_supported", false},
        {"consistent", consistent},
        {"inspection_consistent", nullptr},
        {"release_check_passed", releaseCheckPassed},
        {"threshold_check_passed", thresholdCheckPassed},
        {"mean_check_passed", meanCheckPassed}
    };

    const std::string output = summary.dump(2) + "\n";
    std::cout << output;
    std::cout.flush();
    if (options.hasOutput) {
        WriteJsonFile(options.outputPath, output);
    }
    return consistent && releaseCheckPassed && thresholdCheckPassed && meanCheckPassed ? 0 : 3;
}

json MakeRuntimeError(const InferOptions& options, const QString& error) {
    return json{
        {"language", "c"},
        {"model", ToUtf8(options.modelPath)},
        {"image", ToUtf8(options.imagePath)},
        {"threshold", options.threshold},
        {"calc_mean", options.calcMean},
        {"error", ToUtf8(error)}
    };
}

int CheckCApiExports() {
    DlcvInferApi api;
    if (api.load()) {
        std::cout << "C API export check passed\n";
        return 0;
    }
    std::cerr << ToUtf8(QString::fromStdWString(api.lastError())) << "\n";
    return 1;
}

}  // namespace

void PrintCliHelp(const QString& programPath) {
    const std::string program = ToUtf8(programPath);
    std::cout
        << "Usage:\n"
        << "  " << program << "\n"
        << "  " << program
        << " infer --model <path> --image <path> --threshold <0..1>"
           " [--device <int>] [--with-mask <true|false>] [--calc-mean <true|false>] [--output <jsonPath>]\n"
        << "  " << program << " --check-c-api-exports\n"
        << "  " << program << " --help\n\n"
        << "Exit codes: 0=passed, 1=runtime error, 2=invalid arguments, 3=validation failed\n";
}

int RunCliCommand(const QStringList& args) {
    if (args.size() <= 1) {
        return 2;
    }
    if (args.at(1) == QStringLiteral("--help") ||
        (args.at(1) == QStringLiteral("infer") && args.contains(QStringLiteral("--help")))) {
        PrintCliHelp(args.at(0));
        return 0;
    }
    if (args.at(1) == QStringLiteral("--check-c-api-exports")) {
        if (args.size() != 2) {
            std::cerr << "error: --check-c-api-exports does not accept arguments\n";
            PrintCliHelp(args.at(0));
            return 2;
        }
        return CheckCApiExports();
    }
    if (args.at(1) != QStringLiteral("infer")) {
        std::cerr << "error: expected 'infer', '--check-c-api-exports', or '--help'\n";
        PrintCliHelp(args.at(0));
        return 2;
    }

    InferOptions options;
    QString error;
    if (!ParseInferOptions(args, options, error)) {
        std::cerr << "error: " << ToUtf8(error) << "\n";
        PrintCliHelp(args.at(0));
        return 2;
    }

    try {
        return RunInferCommand(options);
    } catch (const std::exception& ex) {
        std::cerr << MakeRuntimeError(options, DecodeApiText(ex.what())).dump(2) << "\n";
        return 1;
    } catch (...) {
        std::cerr << MakeRuntimeError(options, QStringLiteral("unknown error")).dump(2) << "\n";
        return 1;
    }
}
