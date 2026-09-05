#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <deque>
#include <iomanip>
#include <iostream>
#include <map>
#include <memory>
#include <mutex>
#include <numeric>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>
#include <windows.h>
#include <psapi.h>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "dlcv_infer.h"

void InitGbkConsole() {
    SetConsoleOutputCP(936);
    SetConsoleCP(936);
}

namespace {

struct InferCase {
    std::string ModelFile;
    std::string ImageFile;
};

struct CaseRow {
    std::string ModelName;
    std::string LoadStatus;
    std::string InferStatus;
    std::string CategoryList;
    std::string SpeedText;
    std::string BatchText;
};

struct Options {
    bool PressureMode = false;
    bool DefaultCasesMode = false;
    int DeviceId = 0;
    int ThreadCount = 1;
    int BatchSize = 1;
    int DurationSeconds = 10;
    std::vector<InferCase> Cases;
    std::string SingleModelPath;
    std::string SingleImagePath;
};

class ProcessModelCleanup {
public:
    ~ProcessModelCleanup() {
        try {
            dlcv_infer::Utils::FreeAllModels();
        } catch (...) {
        }
    }
};

const std::string ModelRoot = R"(Y:\测试模型)";

const std::vector<InferCase> DefaultCases = {
    { "AOI-旋转框检测_120_50.dvt", "AOI-1.jpg" },
    { "AOI_120_50.dvst", "AOI-1.jpg" },
    { "猫狗-分类_120_50.dvt", "猫狗-猫.jpg" },
    { "猫狗-分类_120_50_v.dvt", "猫狗-猫.jpg" },
    { "气球-实例分割_120_50.dvt", "气球.jpg" },
    { "气球-实例分割_120_50_v.dvt", "气球.jpg" },
    { "气球-语义分割_120_50.dvt", "气球.jpg" },
    { "手机屏幕-实例分割_120_50.dvt", "手机屏幕.jpg" },
    { "引脚定位-目标检测_120_50.dvt", "引脚定位-目标检测.jpg" },
    { "OCR_120_50.dvt", "OCR-1.jpg" }
};

std::string JoinPath(const std::string& a, const std::string& b) {
    if (a.empty()) return b;
    if (b.empty()) return a;
    const char tail = a.back();
    if (tail == '\\' || tail == '/') return a + b;
    return a + "\\" + b;
}

bool FileExists(const std::string& path) {
    DWORD attr = GetFileAttributesA(path.c_str());
    return (attr != INVALID_FILE_ATTRIBUTES) && ((attr & FILE_ATTRIBUTE_DIRECTORY) == 0);
}

bool DirExists(const std::string& path) {
    DWORD attr = GetFileAttributesA(path.c_str());
    return (attr != INVALID_FILE_ATTRIBUTES) && ((attr & FILE_ATTRIBUTE_DIRECTORY) != 0);
}

double GetCurrentPrivateMemoryMb() {
    PROCESS_MEMORY_COUNTERS pmc = {};
    if (GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof(pmc))) {
        return static_cast<double>(pmc.WorkingSetSize) / (1024.0 * 1024.0);
    }
    return 0.0;
}

std::string TrimMessage(const std::string& s) {
    if (s.empty()) return "";
    std::string r;
    r.reserve(s.size());
    for (char c : s) {
        if (c == '\r' || c == '\n') r += ' ';
        else r += c;
    }
    if (r.size() > 64) {
        return r.substr(0, 64) + "...";
    }
    return r;
}

std::string SafeCell(const std::string& s) {
    if (s.empty()) return "-";
    std::string r;
    r.reserve(s.size());
    for (char c : s) {
        if (c == '|') r += '/';
        else if (c == '\r' || c == '\n') r += ' ';
        else r += c;
    }
    return r;
}

std::string BuildCategoryList(const dlcv_infer::Result& result) {
    const size_t maxShowCount = 20;
    if (result.sampleResults.empty()) return "";
    const auto& first = result.sampleResults[0];
    if (first.results.empty()) return "";

    std::vector<std::string> all;
    all.reserve(first.results.size());
    for (const auto& obj : first.results) {
        all.push_back(obj.categoryName.empty() ? "unknown" : obj.categoryName);
    }

    size_t showCount = std::min(maxShowCount, all.size());
    std::string text;
    for (size_t i = 0; i < showCount; ++i) {
        if (i > 0) text += "，";
        text += all[i];
    }
    if (all.size() > maxShowCount) {
        text += " ...(共" + std::to_string(all.size()) + "个)";
    }
    return text;
}

void PrintUsage(const char* exeName) {
    std::cout << "用法:\n"
              << "  默认测试（无参数）:\n"
              << "    " << exeName << "\n\n"
              << "  单次验证（可多组）:\n"
              << "    " << exeName << " --case <model.dvst> <image.jpg> [--case <model2.dvst> <image2.jpg> ...] [--device 0]\n"
              << "    " << exeName << " --model <model.dvst> --image <image.jpg> [--device 0]\n\n"
              << "  压力测试:\n"
              << "    " << exeName << " --pressure --model <model.dvst> --image <image.jpg>\n"
              << "                [--threads 4] [--batch 2] [--seconds 30] [--device 0]\n\n"
              << "说明:\n"
              << "  - 默认测试会按内置模型列表依次加载、推理并打印表格。\n"
              << "  - Flow 模型入口按 RGB 语义执行，demo 会把读取到的 BGR 图像转换为 RGB。\n"
              << "  - 压测统计口径对齐 C#：完成请求 = 完成批次数 * batch_size。\n";
}

bool ParseIntArg(const std::string& text, int& out) {
    try {
        size_t idx = 0;
        int v = std::stoi(text, &idx);
        if (idx != text.size()) return false;
        out = v;
        return true;
    } catch (...) {
        return false;
    }
}

bool ParseArgs(int argc, char** argv, Options& opt) {
    for (int i = 1; i < argc; i++) {
        const std::string arg = argv[i];
        if (arg == "--pressure") {
            opt.PressureMode = true;
            continue;
        }
        if (arg == "--case") {
            if (i + 2 >= argc) return false;
            InferCase one;
            one.ModelFile = argv[++i];
            one.ImageFile = argv[++i];
            opt.Cases.push_back(std::move(one));
            continue;
        }
        if (arg == "--model") {
            if (i + 1 >= argc) return false;
            opt.SingleModelPath = argv[++i];
            continue;
        }
        if (arg == "--image") {
            if (i + 1 >= argc) return false;
            opt.SingleImagePath = argv[++i];
            continue;
        }
        if (arg == "--device") {
            if (i + 1 >= argc) return false;
            if (!ParseIntArg(argv[++i], opt.DeviceId)) return false;
            continue;
        }
        if (arg == "--threads") {
            if (i + 1 >= argc) return false;
            if (!ParseIntArg(argv[++i], opt.ThreadCount)) return false;
            continue;
        }
        if (arg == "--batch") {
            if (i + 1 >= argc) return false;
            if (!ParseIntArg(argv[++i], opt.BatchSize)) return false;
            continue;
        }
        if (arg == "--seconds") {
            if (i + 1 >= argc) return false;
            if (!ParseIntArg(argv[++i], opt.DurationSeconds)) return false;
            continue;
        }
        if (arg == "-h" || arg == "--help") {
            return false;
        }
        std::cerr << "未知参数: " << arg << std::endl;
        return false;
    }

    if (!opt.SingleModelPath.empty() && !opt.SingleImagePath.empty()) {
        opt.Cases.push_back(InferCase{ opt.SingleModelPath, opt.SingleImagePath });
    }

    if (!opt.PressureMode && opt.Cases.empty()) {
        opt.DefaultCasesMode = true;
    }

    opt.ThreadCount = std::max(1, opt.ThreadCount);
    opt.BatchSize = std::max(1, opt.BatchSize);
    opt.DurationSeconds = std::max(1, opt.DurationSeconds);
    return true;
}

cv::Mat LoadRgbImage(const std::string& imagePath) {
    cv::Mat bgr = cv::imread(imagePath, cv::IMREAD_COLOR);
    if (bgr.empty()) {
        throw std::runtime_error("读取图片失败: " + imagePath);
    }
    cv::Mat rgb;
    cv::cvtColor(bgr, rgb, cv::COLOR_BGR2RGB);
    return rgb;
}

CaseRow RunCase(const std::string& modelPath, const std::string& imagePath, int deviceId) {
    CaseRow row;
    row.ModelName = modelPath;
    row.LoadStatus = "失败";
    row.InferStatus = "失败";
    row.CategoryList = "-";
    row.SpeedText = "-";
    row.BatchText = "-";

    const size_t pos = modelPath.find_last_of("\\/");
    if (pos != std::string::npos) {
        row.ModelName = modelPath.substr(pos + 1);
    }

    double memBefore = GetCurrentPrivateMemoryMb();
    auto tLoad0 = std::chrono::steady_clock::now();
    std::unique_ptr<dlcv_infer::Model> model;
    try {
        model = std::make_unique<dlcv_infer::Model>(modelPath, deviceId);
        row.LoadStatus = (model != nullptr && model->modelIndex != -1) ? "成功" : "失败";
    } catch (const std::exception& ex) {
        row.LoadStatus = "失败";
        row.CategoryList = std::string("错误:") + TrimMessage(ex.what());
    }
    auto tLoad1 = std::chrono::steady_clock::now();
    double memAfter = GetCurrentPrivateMemoryMb();
    double loadMs = std::chrono::duration<double, std::milli>(tLoad1 - tLoad0).count();

    std::string providerInfo;
    if (model != nullptr && model->modelIndex != -1) {
        try {
            auto provider = model->LoadedDogProvider();
            auto dllName = model->LoadedNativeDllName();
            std::string providerName;
            switch (provider) {
                case sntl_admin::DogProvider::Sentinel: providerName = "Sentinel"; break;
                case sntl_admin::DogProvider::Virbox: providerName = "Virbox"; break;
                default: providerName = "Unknown"; break;
            }
            providerInfo = ",provider=" + providerName + ",dll=" + dllName;
        } catch (...) {}
    }

    std::ostringstream loadStatusSs;
    loadStatusSs << row.LoadStatus << "(" << std::fixed << std::setprecision(2) << loadMs
                 << "ms,Δ" << std::fixed << std::setprecision(2) << (memAfter - memBefore)
                 << "MB" << providerInfo << ")";
    row.LoadStatus = loadStatusSs.str();

    if (model == nullptr || model->modelIndex == -1) {
        return row;
    }

    try {
        cv::Mat bgr = cv::imread(imagePath, cv::IMREAD_COLOR);
        if (bgr.empty()) throw std::runtime_error("图像解码失败");
        cv::Mat rgb;
        cv::cvtColor(bgr, rgb, cv::COLOR_BGR2RGB);

        dlcv_infer::json inferParams;
        inferParams["threshold"] = 0.05;
        inferParams["with_mask"] = true;

        try {
            auto result = model->InferBatch(std::vector<cv::Mat>{ rgb }, inferParams);
            row.InferStatus = (!result.sampleResults.empty()) ? "成功" : "失败";
            row.CategoryList = BuildCategoryList(result);
            if (row.CategoryList.empty()) row.CategoryList = "(空)";
        } catch (const std::exception& ex) {
            row.InferStatus = "失败";
            row.CategoryList = std::string("错误:") + TrimMessage(ex.what());
        }
    } catch (const std::exception& ex) {
        row.InferStatus = "失败";
        row.CategoryList = std::string("错误:") + TrimMessage(ex.what());
    }

    return row;
}

int RunDefaultCases(int deviceId) {
    std::cout << "==== C++ 默认测试（DefaultCases） ====" << std::endl;
    std::cout << "模型目录: " << ModelRoot << std::endl;
    std::cout << "固定设备: GPU(" << deviceId << ")" << std::endl;
    std::cout << std::endl;

    bool modelRootOk = DirExists(ModelRoot);
    if (!modelRootOk) {
        std::cout << "模型目录不存在: " << ModelRoot << std::endl;
    }

    std::vector<CaseRow> rows;
    rows.reserve(DefaultCases.size());
    int total = 0;
    int pass = 0;

    for (const auto& c : DefaultCases) {
        std::string modelPath = JoinPath(ModelRoot, c.ModelFile);
        std::string imagePath = JoinPath(ModelRoot, c.ImageFile);

        if (!modelRootOk) {
            rows.push_back(CaseRow{
                c.ModelFile, "跳过", "-",
                "模型目录不存在", "-", "-"
            });
            continue;
        }
        if (!FileExists(modelPath) || !FileExists(imagePath)) {
            rows.push_back(CaseRow{
                c.ModelFile, "跳过", "-",
                "模型或图片不存在", "-", "-"
            });
            continue;
        }

        total++;
        auto row = RunCase(modelPath, imagePath, deviceId);
        rows.push_back(row);
        if (row.LoadStatus.rfind("成功", 0) == 0 && row.InferStatus.rfind("成功", 0) == 0) {
            pass++;
        }
    }

    rows.push_back(CaseRow{
        "汇总",
        "总数=" + std::to_string(total),
        "成功=" + std::to_string(pass),
        "失败=" + std::to_string(total - pass),
        "-", "-"
    });

    std::cout << "| 模型 | 加载 | 推理 | 类别列表 | 3秒速度 | Batch速度 |" << std::endl;
    std::cout << "|---|---|---|---|---|---|" << std::endl;
    for (const auto& r : rows) {
        std::cout << "| " << SafeCell(r.ModelName)
                  << " | " << SafeCell(r.LoadStatus)
                  << " | " << SafeCell(r.InferStatus)
                  << " | " << SafeCell(r.CategoryList)
                  << " | " << SafeCell(r.SpeedText)
                  << " | " << SafeCell(r.BatchText)
                  << " |" << std::endl;
    }
    std::cout << std::endl;

    if (!modelRootOk) return 2;
    return total == pass ? 0 : 1;
}

void PrintSingleResultSummary(const dlcv_infer::Result& result) {
    size_t objectCount = 0;
    if (!result.sampleResults.empty()) {
        objectCount = result.sampleResults[0].results.size();
    }
    std::cout << "结果数量: " << objectCount << std::endl;

    if (result.sampleResults.empty()) return;

    const auto& objects = result.sampleResults[0].results;
    for (size_t i = 0; i < objects.size(); i++) {
        const auto& obj = objects[i];
        std::cout << "  #" << (i + 1)
                  << " 类别=" << obj.categoryName
                  << " 分数=" << std::fixed << std::setprecision(4) << obj.score;
        if (obj.bbox.size() >= 4) {
            std::cout << " bbox=["
                      << obj.bbox[0] << ","
                      << obj.bbox[1] << ","
                      << obj.bbox[2] << ","
                      << obj.bbox[3] << "]";
        }
        if (obj.withAngle && obj.angle > -99.0f) {
            std::cout << " angle=" << obj.angle;
        }
        std::cout << std::endl;
    }
}

void RunSingleCases(const Options& opt) {
    if (opt.Cases.empty()) {
        throw std::invalid_argument("未提供 --case 或 --model/--image");
    }

    dlcv_infer::json inferParams;
    inferParams["with_mask"] = true;
    inferParams["threshold"] = 0.05;

    for (size_t i = 0; i < opt.Cases.size(); i++) {
        const InferCase& one = opt.Cases[i];
        std::cout << "\n=== 单次验证 Case " << (i + 1) << " ===" << std::endl;
        std::cout << "模型: " << one.ModelFile << std::endl;
        std::cout << "图片: " << one.ImageFile << std::endl;

        dlcv_infer::Model model(one.ModelFile, opt.DeviceId);
        const cv::Mat rgb = LoadRgbImage(one.ImageFile);

        const auto t0 = std::chrono::steady_clock::now();
        const dlcv_infer::Result result = model.InferBatch(std::vector<cv::Mat>{ rgb }, inferParams);
        const auto t1 = std::chrono::steady_clock::now();

        const double ms = std::chrono::duration<double, std::milli>(t1 - t0).count();
        std::cout << "推理耗时: " << std::fixed << std::setprecision(3) << ms << " ms" << std::endl;
        PrintSingleResultSummary(result);
    }
}

void RunPressureTest(const Options& opt) {
    std::string modelPath;
    std::string imagePath;
    if (!opt.Cases.empty()) {
        modelPath = opt.Cases[0].ModelFile;
        imagePath = opt.Cases[0].ImageFile;
    } else {
        modelPath = opt.SingleModelPath;
        imagePath = opt.SingleImagePath;
    }
    if (modelPath.empty() || imagePath.empty()) {
        throw std::invalid_argument("压力测试需要 --model 和 --image（或至少一个 --case）");
    }

    dlcv_infer::Model model(modelPath, opt.DeviceId);
    const cv::Mat rgb = LoadRgbImage(imagePath);
    std::vector<cv::Mat> batchImages(static_cast<size_t>(opt.BatchSize), rgb);

    dlcv_infer::json inferParams;
    inferParams["with_mask"] = true;
    inferParams["batch_size"] = opt.BatchSize;
    inferParams["threshold"] = 0.05;

    std::atomic<bool> running{ true };
    std::atomic<long long> completedBatches{ 0 };
    std::atomic<long long> failedBatches{ 0 };

    std::mutex windowMu;
    std::deque<std::chrono::steady_clock::time_point> recentBatchDone;
    const auto rateWindow = std::chrono::seconds(3);

    auto worker = [&]() {
        while (running.load(std::memory_order_relaxed)) {
            try {
                (void)model.InferBatch(batchImages, inferParams);
                completedBatches.fetch_add(1, std::memory_order_relaxed);

                const auto now = std::chrono::steady_clock::now();
                std::lock_guard<std::mutex> lk(windowMu);
                recentBatchDone.push_back(now);
                while (!recentBatchDone.empty() && (now - recentBatchDone.front()) > rateWindow) {
                    recentBatchDone.pop_front();
                }
            } catch (...) {
                failedBatches.fetch_add(1, std::memory_order_relaxed);
            }
        }
    };

    std::vector<std::thread> workers;
    workers.reserve(static_cast<size_t>(opt.ThreadCount));

    const auto begin = std::chrono::steady_clock::now();
    for (int i = 0; i < opt.ThreadCount; i++) {
        workers.emplace_back(worker);
    }

    std::this_thread::sleep_for(std::chrono::seconds(opt.DurationSeconds));
    running.store(false, std::memory_order_relaxed);

    for (auto& t : workers) {
        if (t.joinable()) t.join();
    }
    const auto end = std::chrono::steady_clock::now();

    const double elapsedSec = std::max(1e-6, std::chrono::duration<double>(end - begin).count());
    const long long doneBatches = completedBatches.load(std::memory_order_relaxed);
    const long long doneRequests = doneBatches * static_cast<long long>(opt.BatchSize);

    double recentRate = 0.0;
    {
        std::lock_guard<std::mutex> lk(windowMu);
        if (!recentBatchDone.empty()) {
            const auto now = std::chrono::steady_clock::now();
            const double actualWindow = std::chrono::duration<double>(now - recentBatchDone.front()).count();
            if (actualWindow > 0.0) {
                recentRate = (static_cast<double>(recentBatchDone.size()) * static_cast<double>(opt.BatchSize)) / actualWindow;
            }
        }
    }

    const double avgRate = static_cast<double>(doneRequests) / elapsedSec;

    std::cout << "\n=== 压力测试结果 ===" << std::endl;
    std::cout << "模型: " << modelPath << std::endl;
    std::cout << "图片: " << imagePath << std::endl;
    std::cout << "线程数: " << opt.ThreadCount << std::endl;
    std::cout << "批量大小: " << opt.BatchSize << std::endl;
    std::cout << "运行时间: " << std::fixed << std::setprecision(2) << elapsedSec << " 秒" << std::endl;
    std::cout << "完成请求: " << doneRequests << " (完成批次数=" << doneBatches << ")" << std::endl;
    std::cout << "失败批次: " << failedBatches.load(std::memory_order_relaxed) << std::endl;
    std::cout << "平均速率: " << std::fixed << std::setprecision(2) << avgRate << " 请求/秒" << std::endl;
    std::cout << "实时速率(最近窗口): " << std::fixed << std::setprecision(2) << recentRate << " 请求/秒" << std::endl;
}

struct CommandOptions {
    int DeviceId = 0;
    double Threshold = 0.05;
    bool WithMask = true;
    bool CalcMean = false;
    int Threads = 1;
    int Runs = 10;
    int BatchSize = 1;
};

constexpr int MaxCommandThreads = 32;

struct LoadedCommandModel {
    std::string Name;
    std::string Path;
    int DeviceId = 0;
    std::unique_ptr<dlcv_infer::Model> Model;
};

using CommandModelMap = std::map<std::string, LoadedCommandModel>;

bool ParseDoubleArg(const std::string& text, double& out) {
    try {
        size_t used = 0;
        const double value = std::stod(text, &used);
        if (used != text.size() || !std::isfinite(value)) return false;
        out = value;
        return true;
    } catch (...) {
        return false;
    }
}

bool ParseBoolArg(const std::string& text, bool& out) {
    if (text == "true") {
        out = true;
        return true;
    }
    if (text == "false") {
        out = false;
        return true;
    }
    return false;
}

bool IsCommandName(const std::string& text) {
    return text == "help" || text == "--help" || text == "load-model" || text == "list-models"
        || text == "model-info" || text == "dvs-model-info" || text == "infer"
        || text == "benchmark" || text == "free-model" || text == "free-all-models";
}

bool IsCommandOptionAllowed(const std::string& command, const std::string& option) {
    if (command == "load-model") return option == "--device";
    if (command == "infer") {
        return option == "--threshold" || option == "--with-mask" || option == "--calc-mean";
    }
    if (command == "benchmark") {
        return option == "--threads" || option == "--runs" || option == "--batch-size";
    }
    return false;
}

bool ParseCommandArguments(
    const std::vector<std::string>& segment,
    std::vector<std::string>& positional,
    CommandOptions& options,
    std::string& error) {
    const std::string& command = segment.front();
    for (size_t index = 1; index < segment.size(); ++index) {
        const std::string& token = segment[index];
        if (token.rfind("--", 0) != 0) {
            positional.push_back(token);
            continue;
        }
        if (!IsCommandOptionAllowed(command, token)) {
            error = "命令 " + command + " 不支持参数: " + token;
            return false;
        }
        if (++index >= segment.size()) {
            error = "参数缺少取值: " + token;
            return false;
        }
        const std::string& value = segment[index];
        if (token == "--device") {
            if (!ParseIntArg(value, options.DeviceId)) {
                error = "--device 必须是整数";
                return false;
            }
        } else if (token == "--threshold") {
            if (!ParseDoubleArg(value, options.Threshold) || options.Threshold < 0.0 || options.Threshold > 1.0) {
                error = "--threshold 必须在 0 到 1 之间";
                return false;
            }
        } else if (token == "--with-mask") {
            if (!ParseBoolArg(value, options.WithMask)) {
                error = "--with-mask 只能为 true 或 false";
                return false;
            }
        } else if (token == "--calc-mean") {
            if (!ParseBoolArg(value, options.CalcMean)) {
                error = "--calc-mean 只能为 true 或 false";
                return false;
            }
        } else if (token == "--threads") {
            if (!ParseIntArg(value, options.Threads) || options.Threads <= 0 || options.Threads > MaxCommandThreads) {
                error = "--threads 必须是 1 到 " + std::to_string(MaxCommandThreads) + " 之间的整数";
                return false;
            }
        } else if (token == "--runs") {
            if (!ParseIntArg(value, options.Runs) || options.Runs <= 0) {
                error = "--runs 必须是正整数";
                return false;
            }
        } else if (token == "--batch-size") {
            if (!ParseIntArg(value, options.BatchSize) || options.BatchSize <= 0) {
                error = "--batch-size 必须是正整数";
                return false;
            }
        }
    }
    return true;
}

dlcv_infer::json BuildCommandInferParams(const CommandOptions& options, bool includeBatchSize) {
    dlcv_infer::json params;
    params["threshold"] = options.Threshold;
    params["with_mask"] = options.WithMask;
    params["calc_mean"] = options.CalcMean;
    if (includeBatchSize) params["batch_size"] = options.BatchSize;
    return params;
}

void PrintCommandHelp(const char* exeName) {
    std::cout << "命令串用法:\n"
              << "  " << exeName << " load-model <名称> <模型> [--device N] --then list-models\n"
              << "  " << exeName << " load-model <名称> <模型> --then model-info <名称> --then infer <名称> <图片>\n"
              << "\n命令可用 --then 串联，模型名称只在本次进程内有效。\n"
              << "  load-model <名称> <模型> [--device N]\n"
              << "  list-models\n"
              << "  model-info <名称>\n"
              << "  dvs-model-info <名称>\n"
              << "  infer <名称> <图片> [--threshold F] [--with-mask true|false] [--calc-mean true|false]\n"
              << "  benchmark <名称> <图片> [--threads N，范围 1-" << MaxCommandThreads
              << "] [--runs N] [--batch-size N]\n"
              << "  free-model <名称>\n"
              << "  free-all-models\n"
              << "  help\n"
              << "\n退出码: 0 成功，1 执行失败，2 参数错误。\n";
}

bool FindCommandModel(CommandModelMap& models, const std::string& name, LoadedCommandModel*& out, std::string& error) {
    const auto found = models.find(name);
    if (found == models.end()) {
        error = "未找到模型名称: " + name;
        return false;
    }
    out = &found->second;
    return true;
}

void ReleaseCommandModels(CommandModelMap& models) {
    for (auto& pair : models) {
        try {
            pair.second.Model->FreeModel();
        } catch (...) {
        }
    }
    models.clear();
}

bool IsSameBenchmarkResult(
    const dlcv_infer::Result& baseline,
    const dlcv_infer::Result& candidate,
    std::string& difference) {
    if (candidate.sampleResults.size() != baseline.sampleResults.size()) {
        difference = "批次大小不一致，基线=" + std::to_string(baseline.sampleResults.size())
            + "，当前=" + std::to_string(candidate.sampleResults.size());
        return false;
    }
    for (size_t sampleIndex = 0; sampleIndex < baseline.sampleResults.size(); ++sampleIndex) {
        const auto& baselineSample = baseline.sampleResults[sampleIndex];
        const auto& candidateSample = candidate.sampleResults[sampleIndex];
        if (candidateSample.results.size() != baselineSample.results.size()) {
            difference = "图片[" + std::to_string(sampleIndex) + "]结果数量不一致，基线="
                + std::to_string(baselineSample.results.size()) + "，当前=" + std::to_string(candidateSample.results.size());
            return false;
        }
        for (size_t objectIndex = 0; objectIndex < baselineSample.results.size(); ++objectIndex) {
            const auto& baselineObject = baselineSample.results[objectIndex];
            const auto& candidateObject = candidateSample.results[objectIndex];
            if (candidateObject.categoryId != baselineObject.categoryId
                || candidateObject.categoryName != baselineObject.categoryName
                || candidateObject.withBbox != baselineObject.withBbox
                || candidateObject.withAngle != baselineObject.withAngle
                || candidateObject.withMask != baselineObject.withMask
                || candidateObject.withMean != baselineObject.withMean) {
                difference = "图片[" + std::to_string(sampleIndex) + "]目标[" + std::to_string(objectIndex)
                    + "]稳定字段不一致";
                return false;
            }
            if (candidateObject.bbox.size() != baselineObject.bbox.size()) {
                difference = "图片[" + std::to_string(sampleIndex) + "]目标[" + std::to_string(objectIndex)
                    + "]定位框长度不一致";
                return false;
            }
            for (size_t bboxIndex = 0; bboxIndex < baselineObject.bbox.size(); ++bboxIndex) {
                if (std::abs(candidateObject.bbox[bboxIndex] - baselineObject.bbox[bboxIndex]) > 1e-4f) {
                    difference = "图片[" + std::to_string(sampleIndex) + "]目标[" + std::to_string(objectIndex)
                        + "]定位框不一致";
                    return false;
                }
            }
            if (candidateObject.mask.rows != baselineObject.mask.rows
                || candidateObject.mask.cols != baselineObject.mask.cols
                || candidateObject.mask.type() != baselineObject.mask.type()) {
                difference = "图片[" + std::to_string(sampleIndex) + "]目标[" + std::to_string(objectIndex)
                    + "]mask 尺寸或类型不一致";
                return false;
            }
            if (!baselineObject.mask.empty()
                && cv::norm(candidateObject.mask, baselineObject.mask, cv::NORM_INF) != 0.0) {
                difference = "图片[" + std::to_string(sampleIndex) + "]目标[" + std::to_string(objectIndex)
                    + "]mask 像素不一致";
                return false;
            }
            if (std::abs(candidateObject.score - baselineObject.score) > 1e-4f
                || std::abs(candidateObject.angle - baselineObject.angle) > 1e-4f
                || std::abs(candidateObject.area - baselineObject.area) > 1e-3f
                || std::abs(candidateObject.foregroundMean - baselineObject.foregroundMean) > 1e-4f
                || std::abs(candidateObject.backgroundMean - baselineObject.backgroundMean) > 1e-4f) {
                difference = "图片[" + std::to_string(sampleIndex) + "]目标[" + std::to_string(objectIndex)
                    + "]数值字段不一致";
                return false;
            }
        }
    }
    return true;
}

bool RunCommandBenchmark(LoadedCommandModel& entry, const cv::Mat& image, const CommandOptions& options, std::string& error) {
    const std::vector<cv::Mat> batch(static_cast<size_t>(options.BatchSize), image);
    const dlcv_infer::json params = BuildCommandInferParams(options, true);
    dlcv_infer::Result baseline(std::vector<dlcv_infer::SampleResult>{});
    try {
        baseline = entry.Model->InferBatch(batch, params);
    } catch (const std::exception& ex) {
        error = std::string("生成基线结果失败: ") + ex.what();
        return false;
    } catch (...) {
        error = "生成基线结果时发生未知异常";
        return false;
    }
    if (baseline.sampleResults.size() != static_cast<size_t>(options.BatchSize)) {
        error = "基线批次大小错误，期望=" + std::to_string(options.BatchSize)
            + "，实际=" + std::to_string(baseline.sampleResults.size());
        return false;
    }
    size_t baselineObjectCount = 0;
    for (const auto& sample : baseline.sampleResults) baselineObjectCount += sample.results.size();
    std::cout << "测速基线: 批次大小=" << baseline.sampleResults.size()
              << "，结果数量=" << baselineObjectCount << std::endl;

    std::atomic<int> nextRun{ 0 };
    std::atomic<int> failedRuns{ 0 };
    std::atomic<bool> stopWorkers{ false };
    std::mutex errorMutex;
    std::string firstError;
    std::vector<double> elapsedMs(static_cast<size_t>(options.Runs), 0.0);
    std::vector<std::thread> workers;
    workers.reserve(static_cast<size_t>(options.Threads));

    const auto begin = std::chrono::steady_clock::now();
    try {
        for (int threadIndex = 0; threadIndex < options.Threads; ++threadIndex) {
            workers.emplace_back([&]() {
                for (;;) {
                    if (stopWorkers.load(std::memory_order_relaxed)) return;
                const int runIndex = nextRun.fetch_add(1, std::memory_order_relaxed);
                if (runIndex >= options.Runs) return;
                try {
                    const auto runBegin = std::chrono::steady_clock::now();
                    const auto result = entry.Model->InferBatch(batch, params);
                    std::string difference;
                    if (!IsSameBenchmarkResult(baseline, result, difference)) {
                        failedRuns.fetch_add(1, std::memory_order_relaxed);
                        stopWorkers.store(true, std::memory_order_relaxed);
                        std::lock_guard<std::mutex> lock(errorMutex);
                        if (firstError.empty()) firstError = difference;
                        return;
                    }
                    elapsedMs[static_cast<size_t>(runIndex)] = std::chrono::duration<double, std::milli>(
                        std::chrono::steady_clock::now() - runBegin).count();
                } catch (const std::exception& ex) {
                    failedRuns.fetch_add(1, std::memory_order_relaxed);
                    std::lock_guard<std::mutex> lock(errorMutex);
                    if (firstError.empty()) firstError = ex.what();
                } catch (...) {
                    failedRuns.fetch_add(1, std::memory_order_relaxed);
                    std::lock_guard<std::mutex> lock(errorMutex);
                    if (firstError.empty()) firstError = "未知异常";
                }
            }
            });
        }
    } catch (const std::exception& ex) {
        stopWorkers.store(true, std::memory_order_relaxed);
        for (auto& worker : workers) {
            if (worker.joinable()) worker.join();
        }
        error = std::string("创建测速线程失败: ") + ex.what();
        return false;
    } catch (...) {
        stopWorkers.store(true, std::memory_order_relaxed);
        for (auto& worker : workers) {
            if (worker.joinable()) worker.join();
        }
        error = "创建测速线程时发生未知异常";
        return false;
    }
    for (auto& worker : workers) {
        if (worker.joinable()) worker.join();
    }
    const double totalMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - begin).count();

    const int failed = failedRuns.load(std::memory_order_relaxed);
    const int succeeded = options.Runs - failed;
    if (failed > 0) {
        error = "测速失败 " + std::to_string(failed) + " 次: " + firstError;
        return false;
    }
    const double sumMs = std::accumulate(elapsedMs.begin(), elapsedMs.end(), 0.0);
    const double totalSeconds = std::max(0.001, totalMs / 1000.0);
    std::cout << "测速结果:\n"
              << "  模型名称: " << entry.Name << "\n"
              << "  线程数: " << options.Threads << "\n"
              << "  运行次数: " << succeeded << "\n"
              << "  批量大小: " << options.BatchSize << "\n"
              << "  平均单次耗时: " << std::fixed << std::setprecision(3) << (sumMs / succeeded) << " ms\n"
              << "  总耗时: " << totalMs << " ms\n"
              << "  吞吐量: " << (static_cast<double>(succeeded) * options.BatchSize / totalSeconds) << " 图片/秒\n";
    return true;
}

int RunCommandSegment(const std::vector<std::string>& segment, CommandModelMap& models, std::string& error) {
    if (segment.empty()) {
        error = "--then 后缺少命令";
        return 2;
    }
    const std::string command = segment.front() == "--help" ? "help" : segment.front();
    if (!IsCommandName(command)) {
        error = "未知命令: " + command;
        return 2;
    }
    std::vector<std::string> positional;
    CommandOptions options;
    if (!ParseCommandArguments(segment, positional, options, error)) return 2;
    const auto requireCount = [&](size_t count) {
        if (positional.size() == count) return true;
        error = "命令 " + command + " 的参数数量不正确";
        return false;
    };

    try {
        if (command == "help") {
            if (!requireCount(0)) return 2;
            PrintCommandHelp("dlcv_infer_cpp_dll_demo.exe");
            return 0;
        }
        if (command == "load-model") {
            if (!requireCount(2)) return 2;
            if (positional[0].empty()) {
                error = "模型名称不能为空";
                return 2;
            }
            if (models.find(positional[0]) != models.end()) {
                error = "模型名称已存在: " + positional[0];
                return 2;
            }
            const auto begin = std::chrono::steady_clock::now();
            auto model = std::make_unique<dlcv_infer::Model>(positional[1], options.DeviceId);
            if (model->modelIndex == -1) {
                error = "模型加载失败: " + positional[1];
                return 1;
            }
            const double loadMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - begin).count();
            LoadedCommandModel loaded;
            loaded.Name = positional[0];
            loaded.Path = positional[1];
            loaded.DeviceId = options.DeviceId;
            loaded.Model = std::move(model);
            const int modelIndex = loaded.Model->modelIndex;
            models.emplace(loaded.Name, std::move(loaded));
            std::cout << "加载成功: 名称=" << positional[0] << "，设备=" << options.DeviceId
                      << "，模型索引=" << modelIndex << "，耗时=" << std::fixed << std::setprecision(3)
                      << loadMs << " ms" << std::endl;
            return 0;
        }
        if (command == "list-models") {
            if (!requireCount(0)) return 2;
            std::cout << "已加载模型数量: " << models.size() << std::endl;
            for (const auto& pair : models) {
                const auto& item = pair.second;
                std::cout << "  名称=" << item.Name << "，模型=" << item.Path << "，设备="
                          << item.DeviceId << "，模型索引=" << item.Model->modelIndex << std::endl;
            }
            return 0;
        }
        if (command == "model-info" || command == "dvs-model-info") {
            if (!requireCount(1)) return 2;
            LoadedCommandModel* entry = nullptr;
            if (!FindCommandModel(models, positional[0], entry, error)) return 1;
            const auto info = command == "model-info" ? entry->Model->GetModelInfo() : entry->Model->GetDvsModelInfo();
            std::cout << info.dump(2) << std::endl;
            return 0;
        }
        if (command == "infer") {
            if (!requireCount(2)) return 2;
            LoadedCommandModel* entry = nullptr;
            if (!FindCommandModel(models, positional[0], entry, error)) return 1;
            const cv::Mat image = LoadRgbImage(positional[1]);
            const auto begin = std::chrono::steady_clock::now();
            const auto result = entry->Model->Infer(image, BuildCommandInferParams(options, false));
            const double elapsedMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - begin).count();
            std::cout << "推理成功，耗时: " << std::fixed << std::setprecision(3) << elapsedMs << " ms" << std::endl;
            PrintSingleResultSummary(result);
            return 0;
        }
        if (command == "benchmark") {
            if (!requireCount(2)) return 2;
            LoadedCommandModel* entry = nullptr;
            if (!FindCommandModel(models, positional[0], entry, error)) return 1;
            const cv::Mat image = LoadRgbImage(positional[1]);
            return RunCommandBenchmark(*entry, image, options, error) ? 0 : 1;
        }
        if (command == "free-model") {
            if (!requireCount(1)) return 2;
            const auto found = models.find(positional[0]);
            if (found == models.end()) {
                error = "未找到模型名称: " + positional[0];
                return 1;
            }
            found->second.Model->FreeModel();
            std::cout << "已释放模型: " << found->second.Name << std::endl;
            models.erase(found);
            return 0;
        }
        if (command == "free-all-models") {
            if (!requireCount(0)) return 2;
            const size_t count = models.size();
            ReleaseCommandModels(models);
            dlcv_infer::Utils::FreeAllModels();
            std::cout << "已释放全部模型: " << count << std::endl;
            return 0;
        }
    } catch (const std::exception& ex) {
        error = ex.what();
        return 1;
    } catch (...) {
        error = "命令执行时发生未知异常";
        return 1;
    }
    error = "未知命令: " + command;
    return 2;
}

int RunCommandWorkflow(int argc, char** argv) {
    CommandModelMap models;
    const auto finish = [&](int code) {
        ReleaseCommandModels(models);
        try {
            dlcv_infer::Utils::FreeAllModels();
        } catch (...) {
        }
        return code;
    };

    std::vector<std::vector<std::string>> segments(1);
    try {
        for (int index = 1; index < argc; ++index) {
            const std::string token = argv[index];
            if (token == "--then") {
                if (segments.back().empty()) {
                    std::cerr << "参数错误: --then 前缺少命令" << std::endl;
                    return finish(2);
                }
                segments.emplace_back();
            } else {
                segments.back().push_back(token);
            }
        }
        if (segments.back().empty()) {
            std::cerr << "参数错误: --then 后缺少命令" << std::endl;
            return finish(2);
        }

        for (const auto& segment : segments) {
            std::string error;
            const int code = RunCommandSegment(segment, models, error);
            if (code != 0) {
                std::cerr << (code == 2 ? "参数错误: " : "执行失败: ") << error << std::endl;
                return finish(code);
            }
        }
        std::cout << "命令串执行结束，剩余模型已释放" << std::endl;
        return finish(0);
    } catch (const std::exception& ex) {
        std::cerr << "执行失败: " << ex.what() << std::endl;
        return finish(1);
    } catch (...) {
        std::cerr << "执行失败: 命令串发生未知异常" << std::endl;
        return finish(1);
    }
}

bool IsLegacyFirstArgument(const std::string& text) {
    return text == "--pressure" || text == "--case" || text == "--model" || text == "--image"
        || text == "--device" || text == "--threads" || text == "--batch" || text == "--seconds" || text == "-h";
}

} // namespace

int main(int argc, char** argv) {
    InitGbkConsole();
    ProcessModelCleanup processModelCleanup;
    dlcv_infer::Utils::KeepMaxClock();

    if (argc >= 2 && !IsLegacyFirstArgument(argv[1])) {
        return RunCommandWorkflow(argc, argv);
    }

    Options opt;
    if (!ParseArgs(argc, argv, opt)) {
        PrintUsage(argv[0]);
        return 1;
    }

    try {
        if (opt.PressureMode) {
            RunPressureTest(opt);
        } else if (opt.DefaultCasesMode) {
            return RunDefaultCases(opt.DeviceId);
        } else {
            RunSingleCases(opt);
        }
    } catch (const std::exception& ex) {
        std::cerr << "执行失败: " << ex.what() << std::endl;
        return 2;
    }

    return 0;
}
