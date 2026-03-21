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

    const std::string model_path = R"(C:\Users\Administrator\Desktop\20260321测试\文字分类_120_50.dvst)";
    const std::string img_path = R"(C:\Users\Administrator\Desktop\20260321测试\Fail1_965_574.tif)";

    try {
        dlcv_infer::Model model(model_path, 0);

        cv::Mat img = cv::imread(img_path, cv::IMREAD_COLOR);
        if (img.empty()) {
            std::cerr << "读取图片失败: " << img_path << std::endl;
            return 1;
        }

        cv::Mat img_rgb;
        cv::cvtColor(img, img_rgb, cv::COLOR_BGR2RGB);

        const dlcv_infer::Result result = model.Infer(img_rgb);
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

                std::cout << std::endl;
            }
        }
    } catch (const std::exception& ex) {
        std::cerr << "推理失败: " << ex.what() << std::endl;
        return 1;
    }

    return 0;
}
