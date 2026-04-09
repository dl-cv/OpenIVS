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

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "dlcv_infer.h"

void InitGbkConsole() {
    SetConsoleOutputCP(936);
    SetConsoleCP(936);
}

namespace {

struct InferCase {
    std::string ModelPath;
    std::string ImagePath;
};

struct Options {
    bool PressureMode = false;
    int DeviceId = 0;
    int ThreadCount = 1;
    int BatchSize = 1;
    int DurationSeconds = 10;
    std::vector<InferCase> Cases;
    std::string SingleModelPath;
    std::string SingleImagePath;
};

void PrintUsage(const char* exeName) {
    std::cout << "用法:\n"
              << "  单次验证（可多组）:\n"
              << "    " << exeName << " --case <model.dvst> <image.jpg> [--case <model2.dvst> <image2.jpg> ...] [--device 0]\n"
              << "    " << exeName << " --model <model.dvst> --image <image.jpg> [--device 0]\n\n"
              << "  压力测试:\n"
              << "    " << exeName << " --pressure --model <model.dvst> --image <image.jpg>\n"
              << "                [--threads 4] [--batch 2] [--seconds 30] [--device 0]\n\n"
              << "说明:\n"
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
            one.ModelPath = argv[++i];
            one.ImagePath = argv[++i];
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

    for (size_t i = 0; i < opt.Cases.size(); i++) {
        const InferCase& one = opt.Cases[i];
        std::cout << "\n=== 单次验证 Case " << (i + 1) << " ===" << std::endl;
        std::cout << "模型: " << one.ModelPath << std::endl;
        std::cout << "图片: " << one.ImagePath << std::endl;

        dlcv_infer::Model model(one.ModelPath, opt.DeviceId);
        const cv::Mat rgb = LoadRgbImage(one.ImagePath);

        const auto t0 = std::chrono::steady_clock::now();
        const dlcv_infer::Result result = model.Infer(rgb, inferParams);
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
        modelPath = opt.Cases[0].ModelPath;
        imagePath = opt.Cases[0].ImagePath;
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
    if (!opt.PressureMode && opt.Cases.empty()) {
        PrintUsage(argv[0]);
        return 1;
    }

    try {
        if (opt.PressureMode) {
            RunPressureTest(opt);
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
