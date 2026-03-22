#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <iostream>
#include <string>
#include <windows.h>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "dlcv_infer.h"

void InitGbkConsole() {
    SetConsoleOutputCP(936);
    SetConsoleCP(936);
}

int main() {
    InitGbkConsole();
    std::cout << "开始推理" << std::endl;
    dlcv_infer::Utils::KeepMaxClock();

    const std::string model_path = R"(C:\Users\Administrator\Desktop\测试模型\气球检测_20250407_223101_120_50.dvt)";
    const std::string img_path = R"(C:\Users\Administrator\Desktop\测试模型\balloon.jpg)";

    try {
        dlcv_infer::Model model(model_path, 0);

        cv::Mat img = cv::imread(img_path, cv::IMREAD_COLOR);
        if (img.empty()) {
            throw std::runtime_error("读取图片失败: " + img_path);
        }

        cv::Mat img_rgb;
        cv::cvtColor(img, img_rgb, cv::COLOR_BGR2RGB);

        dlcv_infer::json infer_params;
        infer_params["with_mask"] = true;
        const dlcv_infer::Result result = model.Infer(img_rgb, infer_params);
        for (const auto& sample_result : result.sampleResults) {
            for (const auto& object_result : sample_result.results) {
                std::cout << "类别名称: " << object_result.categoryName << std::endl;
                std::cout << "置信度: " << object_result.score << std::endl;

                if (object_result.bbox.size() >= 4) {
                    std::cout << "检测框: "
                              << object_result.bbox[0] << ", "
                              << object_result.bbox[1] << ", "
                              << object_result.bbox[2] << ", "
                              << object_result.bbox[3] << std::endl;
                } else {
                    std::cout << "检测框: 数据不完整" << std::endl;
                }
                if (object_result.withMask && !object_result.mask.empty()) {
                    std::cout << "Mask尺寸: "
                              << object_result.mask.cols << " x "
                              << object_result.mask.rows << std::endl;
                } else {
                    std::cout << "Mask尺寸: 无" << std::endl;
                }

                std::cout << std::endl;
            }
        }
    } catch (const std::exception& ex) {
        std::cerr << "推理失败: " << ex.what() << std::endl;
    }

    dlcv_infer::Utils::FreeAllModels();
    return 0;
}
