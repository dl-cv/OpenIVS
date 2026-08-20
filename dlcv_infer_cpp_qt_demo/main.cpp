#include <QApplication>
#include <QByteArray>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QFont>
#include <QSaveFile>
#include <QStringList>

#include <cmath>
#include <iostream>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

#include "MainWindow.h"
#include "dlcv_infer.h"

#ifdef _WIN32
#include <Windows.h>
#else
#include <dlfcn.h>
#endif

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

class FreeAllModelsGuard {
public:
    ~FreeAllModelsGuard() {
        try {
            dlcv_infer::Utils::FreeAllModels();
        } catch (...) {
        }
    }
};

class CoutSilencer {
public:
    CoutSilencer() : previous_(std::cout.rdbuf(buffer_.rdbuf())) {}

    ~CoutSilencer() {
        std::cout.rdbuf(previous_);
    }

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

QString FromExceptionMessage(const char* message) {
    if (message == nullptr) {
        return QStringLiteral("unknown error");
    }
    const QByteArray bytes(message);
    const QString utf8 = QString::fromUtf8(bytes);
    if (utf8.toUtf8() == bytes) {
        return utf8;
    }
    return QString::fromLocal8Bit(bytes);
}

void PrintHelp(const QString& programPath) {
    const std::string program = ToUtf8(programPath);
    std::cout
        << "Usage:\n"
        << "  " << program << "\n"
        << "  " << program
        << " infer --model <path> --image <path> --threshold <0..1>"
           " [--device <int>] [--with-mask <true|false>] [--calc-mean <true|false>] [--output <jsonPath>]\n"
        << "  " << program << " --help\n\n"
        << "Exit codes: 0=passed, 1=runtime error, 2=invalid arguments, 3=validation failed\n";
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
    for (int i = 2; i < args.size(); i++) {
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
                error = QStringLiteral("参数重复：--calc-mean");
                return false;
            }
            bool parsed = false;
            if (!ParseBool(value, parsed)) {
                error = QStringLiteral("--calc-mean 必须是 true 或 false");
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
    if (decodedImage.channels() == 3) {
        cv::Mat rgb;
        cv::cvtColor(decodedImage, rgb, cv::COLOR_BGR2RGB);
        return rgb;
    }
    if (decodedImage.channels() == 4) {
        cv::Mat rgb;
        cv::cvtColor(decodedImage, rgb, cv::COLOR_BGRA2RGB);
        return rgb;
    }
    return decodedImage.clone();
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
    summary.count += 1;
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

PathSummary SummarizeStructured(const dlcv_infer::Result& result, double threshold) {
    PathSummary summary;
    for (const auto& sample : result.sampleResults) {
        for (const auto& object : sample.results) {
            AddSummaryItem(
                summary,
                static_cast<double>(object.score),
                dlcv_infer::convertGbkToUtf8(object.categoryName),
                object.withMean,
                static_cast<double>(object.foregroundMean),
                static_cast<double>(object.backgroundMean),
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
        if (jsonValue.is_string()) {
            size_t consumed = 0;
            const std::string text = jsonValue.get<std::string>();
            value = std::stod(text, &consumed);
            return consumed == text.size() && std::isfinite(value);
        }
    } catch (...) {
    }
    return false;
}

bool TryReadJsonScore(const json& token, double& score) {
    try {
        if (!token.is_object() || !token.contains("score")) {
            return false;
        }
        const json& value = token.at("score");
        if (value.is_number()) {
            score = value.get<double>();
            return std::isfinite(score);
        }
        if (value.is_string()) {
            size_t consumed = 0;
            const std::string text = value.get<std::string>();
            score = std::stod(text, &consumed);
            return consumed == text.size() && std::isfinite(score);
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
        throw std::runtime_error("InferOneOutJson did not return a JSON array or result_list wrapper");
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
        (void)TryReadJsonScore(token, score);
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
    if (left.count != right.count) {
        return false;
    }
    if (left.comparableScores.size() != right.comparableScores.size() ||
        left.comparableCategories != right.comparableCategories ||
        left.comparableWithMeans != right.comparableWithMeans) {
        return false;
    }
    for (size_t i = 0; i < left.comparableScores.size(); i++) {
        if (!std::isfinite(left.comparableScores[i]) || !std::isfinite(right.comparableScores[i])) {
            return false;
        }
        if (std::abs(left.comparableScores[i] - right.comparableScores[i]) > 1e-6) {
            return false;
        }
        if (left.comparableWithMeans[i]) {
            if (!std::isfinite(left.comparableForegroundMeans[i]) ||
                !std::isfinite(right.comparableForegroundMeans[i]) ||
                !std::isfinite(left.comparableBackgroundMeans[i]) ||
                !std::isfinite(right.comparableBackgroundMeans[i]) ||
                std::abs(left.comparableForegroundMeans[i] - right.comparableForegroundMeans[i]) > 1e-6 ||
                std::abs(left.comparableBackgroundMeans[i] - right.comparableBackgroundMeans[i]) > 1e-6) {
                return false;
            }
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

int RunInferCommand(const InferOptions& options) {
    FreeAllModelsGuard cleanup;
    PathSummary structuredSummary;
    PathSummary jsonSummary;
    bool structuredHasInspection = false;
    bool structuredOk = false;
    std::vector<std::string> structuredReasons;
    bool jsonHasInspection = false;
    bool jsonOk = false;
    std::vector<std::string> jsonReasons;
    {
        CoutSilencer silenceApiLogs;
        dlcv_infer::Model model(options.modelPath.toStdWString(), options.device);
        const cv::Mat decoded = LoadImageUnicode(options.imagePath);
        const cv::Mat inferImage = PrepareImageForInference(decoded);
        if (inferImage.empty()) {
            throw std::runtime_error("input image channel conversion failed");
        }

    json params = {
        {"threshold", options.threshold},
        {"with_mask", options.withMask},
        {"calc_mean", options.calcMean}
    };

        const dlcv_infer::Result structuredResult = model.Infer(inferImage, params);
        structuredHasInspection = dlcv_infer::Model::GetLastInspectionStatus(
            structuredOk, structuredReasons, 0);
        const json jsonResult = model.InferOneOutJson(inferImage, params);
        jsonHasInspection = dlcv_infer::Model::GetLastInspectionStatus(jsonOk, jsonReasons, 0);
        structuredSummary = SummarizeStructured(structuredResult, options.threshold);
        jsonSummary = SummarizeJson(jsonResult, options.threshold);
    }

    const bool consistent = AreConsistent(structuredSummary, jsonSummary);
    const bool thresholdCheckPassed =
        structuredSummary.belowThreshold.empty() && jsonSummary.belowThreshold.empty();
    const bool meanCheckPassed =
        !options.calcMean || (HasCompleteMeans(structuredSummary) && HasCompleteMeans(jsonSummary));
    const bool inspectionConsistent =
        structuredHasInspection == jsonHasInspection &&
        (!structuredHasInspection ||
         (structuredOk == jsonOk && structuredReasons == jsonReasons));

    const json summary = {
        {"language", "cpp"},
        {"model", ToUtf8(options.modelPath)},
        {"image", ToUtf8(options.imagePath)},
        {"threshold", options.threshold},
        {"device", options.device},
        {"with_mask", options.withMask},
        {"calc_mean", options.calcMean},
        {"structured", PathSummaryToJson(structuredSummary)},
        {"json", PathSummaryToJson(jsonSummary)},
        {"inspection", json::object({
            {"present", jsonHasInspection},
            {"ok", jsonHasInspection ? json(jsonOk) : json()},
            {"reason", jsonHasInspection && !jsonReasons.empty() ? json(jsonReasons) : json()}
        })},
        {"consistent", consistent},
        {"inspection_consistent", inspectionConsistent},
        {"threshold_check_passed", thresholdCheckPassed},
        {"mean_check_passed", meanCheckPassed}
    };

    const std::string output = summary.dump(2) + "\n";
    std::cout << output;
    std::cout.flush();
    if (options.hasOutput) {
        WriteJsonFile(options.outputPath, output);
    }
    return consistent && inspectionConsistent && thresholdCheckPassed && meanCheckPassed ? 0 : 3;
}

std::string GetCppDllPath() {
#ifdef _WIN32
    char path[MAX_PATH];
    HMODULE hModule = nullptr;
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCSTR>(&dlcv_infer::Utils::FreeAllModels), &hModule)) {
        if (GetModuleFileNameA(hModule, path, MAX_PATH) > 0) {
            return path;
        }
    }
#else
    Dl_info info;
    if (dladdr(reinterpret_cast<void*>(&dlcv_infer::Utils::FreeAllModels), &info) && info.dli_fname) {
        return info.dli_fname;
    }
#endif
    return "";
}

}  // namespace

int main(int argc, char* argv[]) {
#ifdef _WIN32
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);
#endif

    QApplication app(argc, argv);
    app.setApplicationName("C++测试程序");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));

    const QStringList args = app.arguments();
    if (args.size() > 1) {
        if (args.at(1) == QStringLiteral("--help") ||
            (args.at(1) == QStringLiteral("infer") && args.contains(QStringLiteral("--help")))) {
            PrintHelp(args.at(0));
            return 0;
        }
        if (args.at(1) != QStringLiteral("infer")) {
            std::cerr << "error: expected 'infer' or '--help'\n";
            PrintHelp(args.at(0));
            return 2;
        }

        InferOptions options;
        QString error;
        if (!ParseInferOptions(args, options, error)) {
            std::cerr << "error: " << ToUtf8(error) << "\n";
            PrintHelp(args.at(0));
            return 2;
        }

        try {
            return RunInferCommand(options);
        } catch (const std::exception& ex) {
            const json errorJson = {
                {"language", "cpp"},
                {"model", ToUtf8(options.modelPath)},
                {"image", ToUtf8(options.imagePath)},
                {"threshold", options.threshold},
                {"calc_mean", options.calcMean},
                {"error", ToUtf8(FromExceptionMessage(ex.what()))}
            };
            std::cerr << errorJson.dump(2) << "\n";
            return 1;
        } catch (...) {
            const json errorJson = {
                {"language", "cpp"},
                {"model", ToUtf8(options.modelPath)},
                {"image", ToUtf8(options.imagePath)},
                {"threshold", options.threshold},
                {"calc_mean", options.calcMean},
                {"error", "unknown error"}
            };
            std::cerr << errorJson.dump(2) << "\n";
            return 1;
        }
    }

    QObject::connect(&app, &QCoreApplication::aboutToQuit, []() {
        dlcv_infer::Utils::FreeAllModels();
    });

    std::cout << "[dlcv_infer_cpp_dll] " << GetCppDllPath() << std::endl;

    MainWindow w;
    w.show();
    return app.exec();
}
