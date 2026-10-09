#include <QApplication>
#include <QByteArray>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QFont>
#include <QSettings>
#include <QTimer>
#include <QImage>
#include <QPixmap>
#include <QSaveFile>
#include <QStringList>

#include <cmath>
#include <cstdio>
#include <iostream>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

#include "ImageViewerWidget.h"
#include "MainWindow.h"
#include "dlcv_infer.h"

#ifdef _WIN32
#include <Windows.h>
#else
#include <dlfcn.h>
#endif

namespace {

using json = nlohmann::json;

#ifdef _WIN32
bool IsUsableStandardHandle(DWORD standardHandle) {
    const HANDLE handle = GetStdHandle(standardHandle);
    if (handle == nullptr || handle == INVALID_HANDLE_VALUE) {
        return false;
    }

    SetLastError(ERROR_SUCCESS);
    const DWORD fileType = GetFileType(handle);
    return fileType != FILE_TYPE_UNKNOWN || GetLastError() == ERROR_SUCCESS;
}

void InitializeConsoleForCommandLine() {
    const bool hasStdout = IsUsableStandardHandle(STD_OUTPUT_HANDLE);
    const bool hasStderr = IsUsableStandardHandle(STD_ERROR_HANDLE);
    if (hasStdout && hasStderr) {
        return;
    }

    if (!AttachConsole(ATTACH_PARENT_PROCESS) && GetLastError() != ERROR_ACCESS_DENIED) {
        return;
    }

    FILE* stream = nullptr;
    if (!hasStdout) {
        freopen_s(&stream, "CONOUT$", "w", stdout);
    }
    if (!hasStderr) {
        freopen_s(&stream, "CONOUT$", "w", stderr);
    }
    std::ios::sync_with_stdio(true);

    DWORD consoleMode = 0;
    if (GetConsoleMode(GetStdHandle(STD_OUTPUT_HANDLE), &consoleMode) ||
        GetConsoleMode(GetStdHandle(STD_ERROR_HANDLE), &consoleMode)) {
        SetConsoleOutputCP(CP_UTF8);
    }
}
#endif

struct InferOptions {
    float labelFontScale = 1.0f;
    QString resultView = "summary";
    bool hasLabelFontScale = false;
    bool hasResultView = false;
    QString modelPath;
    QString imagePath;
    QString outputPath;
    double threshold = 0.0;
    int device = 0;
    bool withMask = true;
    bool hasModel = false;
    bool hasImage = false;
    bool hasThreshold = false;
    bool hasDevice = false;
    bool hasWithMask = false;
    bool hasOutput = false;
};

struct PathSummary {
    int count = 0;
    json scores = json::array();
    json categories = json::array();
    json belowThreshold = json::array();
    json statistics = json::array();
    std::vector<double> comparableScores;
    std::vector<std::string> comparableCategories;
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
           " [--device <int>] [--with-mask <true|false>] [--output <jsonPath>]\n"
        << "  " << program
        << " render --model <path> --image <path> --threshold <0..1> --output <pngPath>"
           " [--device <int>] [--with-mask <true|false>]\n"
        << "  " << program
        << " ui-test --model <path> --image <path> --output <jsonPath>"
           " [--label-font-scale <0.3..5>] [--result-view <summary|json|model|release>]\n"
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
        } else if (args.at(1) == "ui-test" && option == "--label-font-scale") {
            bool ok = false;
            const float scale = value.toFloat(&ok);
            if (options.hasLabelFontScale || !ok || !std::isfinite(scale) || scale < 0.3f || scale > 5.0f) {
                error = "--label-font-scale 必须为 0.3 到 5 的数值，且不能重复";
                return false;
            }
            options.labelFontScale = scale;
            options.hasLabelFontScale = true;
        } else if (args.at(1) == "ui-test" && option == "--result-view") {
            if (options.hasResultView || (value != "summary" && value != "json" && value != "model" && value != "release")) {
                error = "--result-view 必须为 summary、json、model 或 release，且不能重复";
                return false;
            }
            options.resultView = value;
            options.hasResultView = true;
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
        error = QStringLiteral("不支持 .dvsp 模型推理");
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

json ReadStatisticsFields(const json& source) {
    json fields = json::object();
    for (const char* key : { "with_mean", "foreground_mean", "background_mean", "with_median", "foreground_median", "background_median" }) {
        if (source.contains(key)) fields[key] = source.at(key);
    }
    return fields;
}

void AddSummaryItem(
    PathSummary& summary,
    double score,
    const std::string& category,
    double threshold,
    const json& statistics) {
    const int index = summary.count;
    summary.count += 1;
    summary.categories.push_back(category);
    summary.comparableCategories.push_back(category);
    summary.comparableScores.push_back(score);
    summary.statistics.push_back(statistics);

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
            json statistics = json::object();
            object.WriteStatistics(statistics);
            AddSummaryItem(
                summary,
                static_cast<double>(object.score),
                dlcv_infer::convertGbkToUtf8(object.categoryName),
                threshold,
                statistics);
        }
    }
    return summary;
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
                threshold,
                json::object());
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
        AddSummaryItem(summary, score, category, threshold, ReadStatisticsFields(token));
    }
    return summary;
}

json PathSummaryToJson(const PathSummary& summary) {
    json result = {
        {"count", summary.count}, {"scores", summary.scores}, {"categories", summary.categories},
        {"below_threshold", summary.belowThreshold}, {"statistics", summary.statistics}
    };
    for (const char* kind : { "mean", "median" }) {
        const std::string flag = std::string("with_") + kind;
        const std::string fg = std::string("foreground_") + kind;
        const std::string bg = std::string("background_") + kind;
        bool present = false;
        json flags = json::array(), foreground = json::array(), background = json::array();
        for (const auto& fields : summary.statistics) {
            present = present || fields.contains(flag);
            flags.push_back(fields.contains(flag) ? fields.at(flag) : json(nullptr));
            foreground.push_back(fields.contains(fg) ? fields.at(fg) : json(nullptr));
            background.push_back(fields.contains(bg) ? fields.at(bg) : json(nullptr));
        }
        if (present) {
            result[flag] = flags; result[fg] = foreground; result[bg] = background;
        }
    }
    return result;
}

bool AreConsistent(const PathSummary& left, const PathSummary& right) {
    if (left.count != right.count || left.comparableScores.size() != right.comparableScores.size() ||
        left.comparableCategories != right.comparableCategories) return false;
    for (size_t i = 0; i < left.comparableScores.size(); ++i) {
        if (!std::isfinite(left.comparableScores[i]) || !std::isfinite(right.comparableScores[i]) ||
            std::abs(left.comparableScores[i] - right.comparableScores[i]) > 1e-6) return false;
        const auto& leftFields = left.statistics.at(i);
        const auto& rightFields = right.statistics.at(i);
        if (leftFields.value("with_mean", false) != rightFields.value("with_mean", false) ||
            leftFields.value("with_median", false) != rightFields.value("with_median", false)) return false;
        for (const char* key : { "foreground_mean", "background_mean", "foreground_median", "background_median" }) {
            const json leftValue = leftFields.value(key, json(0.0));
            const json rightValue = rightFields.value(key, json(0.0));
            if (leftValue.is_number() && rightValue.is_number()) {
                const double leftNumber = leftValue.get<double>(), rightNumber = rightValue.get<double>();
                if (!std::isfinite(leftNumber) || !std::isfinite(rightNumber) ||
                    std::abs(leftNumber - rightNumber) > 1e-6) return false;
            } else if (leftValue != rightValue) return false;
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
    bool releaseCheckPassed = false;
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
            {"with_mask", options.withMask}
        };

        const dlcv_infer::Result structuredResult = model.Infer(inferImage, params);
        structuredHasInspection = dlcv_infer::Model::GetLastInspectionStatus(
            structuredOk, structuredReasons, 0);
        const json jsonResult = model.InferOneOutJson(inferImage, params);
        jsonHasInspection = dlcv_infer::Model::GetLastInspectionStatus(jsonOk, jsonReasons, 0);
        structuredSummary = SummarizeStructured(structuredResult, options.threshold);
        jsonSummary = SummarizeJson(jsonResult, options.threshold);
        model.FreeModel();
        model.FreeModel();
        releaseCheckPassed = model.modelIndex == -1;
    }

    const bool consistent = AreConsistent(structuredSummary, jsonSummary);
    const bool thresholdCheckPassed =
        structuredSummary.belowThreshold.empty() && jsonSummary.belowThreshold.empty();
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
        {"structured", PathSummaryToJson(structuredSummary)},
        {"json", PathSummaryToJson(jsonSummary)},
        {"inspection", json::object({
            {"present", jsonHasInspection},
            {"ok", jsonHasInspection ? json(jsonOk) : json()},
            {"reason", jsonHasInspection && !jsonReasons.empty() ? json(jsonReasons) : json()}
        })},
        {"consistent", consistent},
        {"inspection_consistent", inspectionConsistent},
        {"release_check_passed", releaseCheckPassed},
        {"threshold_check_passed", thresholdCheckPassed}
    };

    const std::string output = summary.dump(2) + "\n";
    std::cout << output;
    std::cout.flush();
    if (options.hasOutput) {
        WriteJsonFile(options.outputPath, output);
    }
    return consistent && inspectionConsistent && thresholdCheckPassed && releaseCheckPassed ? 0 : 3;
}

int RunRenderCommand(const InferOptions& options) {
    if (!options.hasOutput) {
        std::cerr << "render failed: --output is required\n";
        return 2;
    }

    FreeAllModelsGuard cleanup;
    dlcv_infer::Model model(options.modelPath.toStdWString(), options.device);
    const cv::Mat decoded = LoadImageUnicode(options.imagePath);
    const cv::Mat inferImage = PrepareImageForInference(decoded);
    if (inferImage.empty()) {
        throw std::runtime_error("input image channel conversion failed");
    }

    const json params = {
        {"threshold", options.threshold},
        {"with_mask", options.withMask},
        {"batch_size", 1}
    };
    const dlcv_infer::Result result = model.InferBatch({inferImage}, params);
    if (result.sampleResults.empty() || result.sampleResults.front().results.empty()) {
        std::cerr << "render failed: DVS flow returned an empty result\n";
        return 3;
    }

    const auto& results = result.sampleResults.front().results;
    ImageViewerWidget viewer;
    viewer.resize(decoded.cols, decoded.rows);
    viewer.setShowStatusText(false);
    try {
        json info = model.GetModelInfo();
        std::string task = info.contains("model_info") ? info["model_info"].value("task_type", "") : info.value("task_type", "");
        viewer.setLabelDisplayMode(task == "OCR" ? ImageViewerWidget::LabelTextMode::CategoryOnly : ImageViewerWidget::LabelTextMode::CategoryAndScore);
    } catch (...) {
    }
    viewer.setImageAndResults(decoded, results);
    viewer.show();
    QApplication::processEvents();

    QImage rendered = viewer.grab().toImage();
    if (rendered.width() != decoded.cols || rendered.height() != decoded.rows) {
        rendered = rendered.scaled(decoded.cols, decoded.rows, Qt::IgnoreAspectRatio, Qt::SmoothTransformation);
    }
    if (rendered.isNull() || !rendered.save(options.outputPath)) {
        std::cerr << "render failed: could not save output image\n";
        return 1;
    }

    std::cout << "rendered: " << ToUtf8(options.outputPath)
        << ", source=" << decoded.cols << "x" << decoded.rows
        << ", output=" << rendered.width() << "x" << rendered.height()
        << ", objects=" << results.size() << "\n";
    for (size_t i = 0; i < results.size(); ++i) {
        const auto& object = results[i];
        std::cout << "object[" << i << "]: category_name=" << dlcv_infer::convertGbkToUtf8(object.categoryName)
            << ", score=" << object.score << ", bbox=";
        for (size_t j = 0; j < object.bbox.size(); ++j) {
            if (j > 0) std::cout << ",";
            std::cout << object.bbox[j];
        }
        if (object.withMask && !object.mask.empty()) {
            const bool fullImageMask = object.mask.cols == decoded.cols && object.mask.rows == decoded.rows;
            std::cout << ", mask=" << object.mask.cols << "x" << object.mask.rows
                << ", mask_space=" << (fullImageMask ? "full-image" : "roi");
        } else {
            std::cout << ", mask=none";
        }
        std::cout << "\n";
    }
    return 0;
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
    if (argc > 1) {
        InitializeConsoleForCommandLine();
    }
#endif

    QApplication app(argc, argv);
    app.setApplicationName("C++测试程序");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));

    QStringList args = app.arguments();
    const bool uiTestMode = args.size() > 1 && args.at(1) == "ui-test";
    InferOptions uiOptions;
    if (uiTestMode && args.contains("--help")) {
        PrintHelp(args.at(0));
        return 0;
    }
    if (uiTestMode) {
        if (!args.contains("--threshold")) args << "--threshold" << "0.5";
        QString error;
        if (!ParseInferOptions(args, uiOptions, error) || !uiOptions.hasOutput || !uiOptions.withMask) {
            std::cerr << "ui-test 参数错误：" << ToUtf8(error.isEmpty()
                ? QStringLiteral("需要 --output，且 --with-mask 必须为 true") : error) << "\n";
            return 2;
        }
        const QString settingsPath = QFileInfo(uiOptions.outputPath).absolutePath() + "/settings";
        QSettings::setPath(QSettings::IniFormat, QSettings::UserScope, settingsPath);
        QSettings::setPath(QSettings::IniFormat, QSettings::SystemScope, settingsPath);
    } else if (args.size() > 1) {
        if (args.at(1) == QStringLiteral("--help") ||
            ((args.at(1) == QStringLiteral("infer") || args.at(1) == QStringLiteral("render"))
             && args.contains(QStringLiteral("--help")))) {
            PrintHelp(args.at(0));
            return 0;
        }
        const bool isInferCommand = args.at(1) == QStringLiteral("infer");
        const bool isRenderCommand = args.at(1) == QStringLiteral("render");
        if (!isInferCommand && !isRenderCommand) {
            std::cerr << "error: expected 'infer', 'render', or '--help'\n";
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
            return isRenderCommand ? RunRenderCommand(options) : RunInferCommand(options);
        } catch (const std::exception& ex) {
            const json errorJson = {
                {"language", "cpp"},
                {"model", ToUtf8(options.modelPath)},
                {"image", ToUtf8(options.imagePath)},
                {"threshold", options.threshold},
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
                {"error", "unknown error"}
            };
            std::cerr << errorJson.dump(2) << "\n";
            return 1;
        }
    }

    QObject::connect(&app, &QCoreApplication::aboutToQuit, []() {
        dlcv_infer::Utils::FreeAllModels();
    });

    std::cout << "[dlcv_infer_cpp] " << GetCppDllPath() << std::endl;

    MainWindow w(nullptr, uiTestMode);
    if (uiTestMode) {
        w.resize(1280, 930);
        QTimer timer;
        QObject::connect(&timer, &QTimer::timeout, &w, [&]() {
            if (!w.devicesReadyForUiTest()) return;
            timer.stop();
            try {
                json report = w.runUiTest(uiOptions.modelPath, uiOptions.imagePath, uiOptions.device,
                    uiOptions.threshold, uiOptions.labelFontScale, uiOptions.resultView);
                report["passed"] = true;
                WriteJsonFile(uiOptions.outputPath, report.dump(2));
                w.show();
                QTimer::singleShot(15000, &app, &QCoreApplication::quit);
            } catch (const std::exception& error) {
                try { WriteJsonFile(uiOptions.outputPath,
                    json({{"passed", false}, {"error", ToUtf8(FromExceptionMessage(error.what()))}}).dump(2)); }
                catch (...) {}
                app.exit(1);
            }
        });
        QTimer::singleShot(60000, &app, [&]() {
            if (timer.isActive()) app.exit(1);
        });
        timer.start(100);
        return app.exec();
    }
    w.show();
    return app.exec();
}
