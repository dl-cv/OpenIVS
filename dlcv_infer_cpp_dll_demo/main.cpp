#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <algorithm>
#include <atomic>
#include <chrono>
#include <deque>
#include <iomanip>
#include <iostream>
#include <mutex>
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
    dlcv_infer::Model* model = nullptr;
    try {
        model = new dlcv_infer::Model(modelPath, deviceId);
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

    try {
        delete model;
    } catch (...) {}
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

} // namespace

int main(int argc, char** argv) {
    InitGbkConsole();
    dlcv_infer::Utils::KeepMaxClock();

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
        dlcv_infer::Utils::FreeAllModels();
        return 2;
    }

    dlcv_infer::Utils::FreeAllModels();
    return 0;
}
