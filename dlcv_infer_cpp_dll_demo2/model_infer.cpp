#include "demo2_api.h"

#include <iostream>
#include <stdexcept>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

void InferTest(const std::string& img_path) {
    if (global_model.modelIndex == -1) {
        throw std::runtime_error("global_model is not loaded");
    }

    cv::Mat img = cv::imread(img_path, cv::IMREAD_COLOR);
    if (img.empty()) {
        throw std::runtime_error(img_path);
    }

    cv::Mat img_rgb;
    cv::cvtColor(img, img_rgb, cv::COLOR_BGR2RGB);

    const dlcv_infer::Result result = global_model.Infer(img_rgb);
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
}
