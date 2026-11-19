// 最小 OpenIVS C++ 命令行：加载一个模型、读一张图片、推理并打印结果。
#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <windows.h>

#include <chrono>
#include <iomanip>
#include <iostream>
#include <string>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "dlcv_infer.h"

namespace {

const char* kDefaultModel = "Y:\\测试模型\\气球-实例分割_PLUS_s.dvt";
const char* kDefaultImage = "Y:\\测试模型\\气球.jpg";
const double kThreshold = 0.5;

void PrintResult(const dlcv_infer::Result& result) {
    size_t total = 0;
    for (const auto& sample : result.sampleResults) total += sample.results.size();
    std::cout << "推理结果: " << total << "个" << std::endl << std::endl;

    size_t index = 0;
    for (const auto& sample : result.sampleResults) {
        for (const auto& obj : sample.results) {
            ++index;
            std::cout << "[" << index << "] " << std::left << std::setw(16)
                      << (obj.categoryName.empty() ? "unknown" : obj.categoryName)
                      << std::right << "score=" << std::fixed << std::setprecision(2) << obj.score;
            if (obj.withBbox && obj.bbox.size() >= 4) {
                std::cout << "  bbox=(" << std::setprecision(1) << obj.bbox[0] << ", "
                          << obj.bbox[1] << ", " << obj.bbox[2] << ", " << obj.bbox[3] << ")";
            }
            if (obj.withAngle) {
                std::cout << "  angle=" << std::setprecision(1) << obj.angle;
            }
            std::cout << "  area=" << std::setprecision(1) << obj.area << std::endl;
        }
    }
}

}  // namespace

int main(int argc, char** argv) {
    // 类别名由 SDK 转成本地代码页输出，控制台同样使用 936。
    SetConsoleOutputCP(936);
    SetConsoleCP(936);

    const std::string modelPath = argc >= 2 ? argv[1] : kDefaultModel;
    const std::string imagePath = argc >= 3 ? argv[2] : kDefaultImage;

    std::cout << "模型: " << modelPath << std::endl;
    std::cout << "图片: " << imagePath << std::endl;
    std::cout << "批量大小: 1" << std::endl;
    std::cout << "threshold: " << std::fixed << std::setprecision(2) << kThreshold << std::endl;

    try {
        const cv::Mat bgr = cv::imread(imagePath, cv::IMREAD_COLOR);
        if (bgr.empty()) {
            std::cerr << "读取图片失败: " << imagePath << std::endl;
            return 1;
        }
        cv::Mat rgb;
        cv::cvtColor(bgr, rgb, cv::COLOR_BGR2RGB);

        // 第二个参数是 GPU 序号，-1 表示 CPU。
        dlcv_infer::Model model(modelPath, 0);
        if (model.modelIndex < 0) {
            std::cerr << "模型加载失败: " << modelPath << std::endl;
            return 1;
        }

        dlcv_infer::json params;
        params["threshold"] = kThreshold;

        const auto begin = std::chrono::steady_clock::now();
        const dlcv_infer::Result result = model.Infer(rgb, params);
        const double elapsedMs = std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - begin).count();

        std::cout << "推理时间: " << std::fixed << std::setprecision(2) << elapsedMs << "ms" << std::endl;
        PrintResult(result);

        model.FreeModel();
    } catch (const std::exception& ex) {
        std::cerr << "执行失败: " << ex.what() << std::endl;
        return 1;
    }
    return 0;
}
