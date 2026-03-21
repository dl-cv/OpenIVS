#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <fcntl.h>
#include <iostream>
#include <io.h>
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
    std::cout << dlcv_infer::convertUtf8ToGbk("开始推理") << std::endl;

    const std::wstring model_path = LR"(Z:\A251113-微组半导体-IC封装检测\99-模型打包\微组-文字分类_120_50.dvst)";
    const std::wstring img_path = LR"(Z:\A251113-微组半导体-IC封装检测\99-模型打包\Fail1_965_574.tif)";

    try {
        dlcv_infer::Model model(model_path, 0);

        cv::Mat img = cv::imread(dlcv_infer::convertWstringToGbk(img_path), cv::IMREAD_COLOR);
        if (img.empty()) {
            std::cerr << dlcv_infer::convertUtf8ToGbk("读取图片失败: ")
                      << dlcv_infer::convertWstringToGbk(img_path) << std::endl;
            return 1;
        }

        cv::Mat img_rgb;
        cv::cvtColor(img, img_rgb, cv::COLOR_BGR2RGB);

        const dlcv_infer::Result result = model.Infer(img_rgb);
        for (const auto& sample_result : result.sampleResults) {
            for (const auto& object_result : sample_result.results) {
                std::cout << dlcv_infer::convertUtf8ToGbk("类别名称: ")
                          << dlcv_infer::convertUtf8ToGbk(object_result.categoryName) << std::endl;
                std::cout << dlcv_infer::convertUtf8ToGbk("置信度: ") << object_result.score << std::endl;

                if (object_result.bbox.size() >= 4) {
                    std::cout << dlcv_infer::convertUtf8ToGbk("检测框: ")
                              << object_result.bbox[0] << ", "
                              << object_result.bbox[1] << ", "
                              << object_result.bbox[2] << ", "
                              << object_result.bbox[3] << std::endl;
                } else {
                    std::cout << dlcv_infer::convertUtf8ToGbk("检测框: 数据不完整") << std::endl;
                }

                std::cout << std::endl;
            }
        }
    } catch (const std::exception& ex) {
        std::cerr << dlcv_infer::convertUtf8ToGbk("推理失败: ")
                  << dlcv_infer::convertUtf8ToGbk(ex.what()) << std::endl;
        return 1;
    }

    return 0;
}
