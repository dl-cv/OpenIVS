#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <iostream>
#include <string>
#include <windows.h>

#include "demo2_api.h"

dlcv_infer::Model global_model;

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
        LoadGlobalModel(model_path, 0);
        InferWithGlobalModel(img_path);
    } catch (const std::exception& ex) {
        if (std::string(ex.what()) == img_path) {
            std::cerr << "读取图片失败: " << img_path << std::endl;
        } else {
            std::cerr << "推理失败: " << ex.what() << std::endl;
        }
        return 1;
    }

    return 0;
}
