#include <iostream>
#include <cstdio>
#include <cstdlib>
#include <memory>
#include <string>
#include <vector>

#include <windows.h>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "../DvsTempArtifactMonitor.h"
#include "dlcv_infer_c_api.h"
#include "dlcv_infer.h"

static std::string WideToUtf8(const std::wstring& w) {
    if (w.empty()) return {};
    int bytes = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()), nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return {};
    std::string out(static_cast<size_t>(bytes), '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()), out.data(), bytes, nullptr, nullptr);
    return out;
}

static cv::Mat ReadImageRgb(const std::wstring& imagePath) {
    FILE* file = nullptr;
    if (_wfopen_s(&file, imagePath.c_str(), L"rb") != 0 || file == nullptr) return {};
    std::unique_ptr<FILE, decltype(&fclose)> holder(file, &fclose);
    if (fseek(file, 0, SEEK_END) != 0) return {};
    const long size = ftell(file);
    if (size <= 0 || fseek(file, 0, SEEK_SET) != 0) return {};
    std::vector<unsigned char> data(static_cast<size_t>(size));
    if (fread(data.data(), 1, data.size(), file) != data.size()) return {};
    cv::Mat decoded = cv::imdecode(data, cv::IMREAD_COLOR);
    if (decoded.empty()) return {};
    cv::Mat rgb;
    cv::cvtColor(decoded, rgb, cv::COLOR_BGR2RGB);
    return rgb;
}

static int RunDvsMemoryLoadingSelfTest(int argc, wchar_t* argv[]) {
    if (argc < 4 || argc > 5) {
        std::cerr << "用法: dlcv_infer_c_test dvs-memory-loading-selftest <modelPath> <imagePath> [device]\n";
        return 2;
    }
    const std::wstring modelPath = argv[2];
    const std::wstring imagePath = argv[3];
    const int deviceId = argc == 5 ? _wtoi(argv[4]) : 0;
    const std::wstring extension = dvs_test::Lower(std::filesystem::path(modelPath).extension().wstring());
    if (extension != L".dvst" && extension != L".dvso") {
        std::cerr << "模型必须为 .dvst 或 .dvso\n";
        return 2;
    }

    try {
        cv::Mat rgb = ReadImageRgb(imagePath);
        if (rgb.empty()) {
            std::cerr << "图片读取失败\n";
            return 2;
        }

        dvs_test::TempArtifactMonitor monitor(dvs_test::ReadArchiveFileNames(modelPath));
        monitor.Start();
        std::string operationError;
        int modelIndex = -1;
        DlcvCResult result{};
        try {
            const std::string modelPathUtf8 = WideToUtf8(modelPath);
            modelIndex = dlcv_infer_cpp_load_model_c(modelPathUtf8.c_str(), deviceId);
            if (modelIndex < 0) {
                const char* lastError = dlcv_infer_cpp_get_last_error_c();
                operationError = lastError ? lastError : "C 接口加载失败";
            } else {
                DlcvCImage image{};
                image.data_ptr = reinterpret_cast<long long>(rgb.data);
                image.height = rgb.rows;
                image.width = rgb.cols;
                image.channel = rgb.channels();
                DlcvCImageList imageList{&image, 1};
                result = dlcv_infer_cpp_infer_c(modelIndex, &imageList);
                if (result.code != 0) {
                    operationError = result.message ? result.message : "C 接口推理失败";
                }
            }
        } catch (const std::exception& ex) {
            operationError = ex.what();
        } catch (...) {
            operationError = "加载、推理或释放时发生未知异常";
        }
        dlcv_infer_cpp_free_model_result_c(&result);
        if (modelIndex >= 0 && dlcv_infer_cpp_free_model_c(modelIndex) != 0 && operationError.empty()) {
            const char* lastError = dlcv_infer_cpp_get_last_error_c();
            operationError = lastError ? lastError : "C 接口释放失败";
        }
        monitor.Stop();

        if (!operationError.empty()) {
            std::cerr << operationError << "\n";
            return 1;
        }
        if (monitor.HasArtifacts()) {
            std::cerr << "系统临时目录出现流程归档文件: " << monitor.DescribeUtf8() << "\n";
            return 1;
        }
        std::cout << "C 接口流程归档内存加载测试通过\n";
        return 0;
    } catch (const std::exception& ex) {
        std::cerr << ex.what() << "\n";
        return 1;
    }
}

static int RunDvspRejectSelfTest(int argc, wchar_t* argv[]) {
    if (argc < 3 || argc > 4) {
        std::cerr << "用法: dlcv_infer_c_test dvsp-reject-selftest <modelPath> [device]\n";
        return 2;
    }
    const std::wstring modelPath = argv[2];
    const int deviceId = argc == 4 ? _wtoi(argv[3]) : 0;
    if (dvs_test::Lower(std::filesystem::path(modelPath).extension().wstring()) != L".dvsp") {
        std::cerr << "模型必须为 .dvsp\n";
        return 2;
    }

    try {
        dvs_test::TempArtifactMonitor monitor({});
        monitor.Start();
        const std::string pathUtf8 = WideToUtf8(modelPath);
        const int modelIndex = dlcv_infer_cpp_load_model_c(pathUtf8.c_str(), deviceId);
        std::string errorMessage;
        if (modelIndex >= 0) {
            dlcv_infer_cpp_free_model_c(modelIndex);
        } else {
            const char* lastError = dlcv_infer_cpp_get_last_error_c();
            if (lastError) errorMessage = lastError;
        }
        monitor.Stop();

        if (modelIndex >= 0 || !dvs_test::HasExplicitDvspUnsupportedMessage(errorMessage)) {
            std::cerr << ".dvsp 未返回明确的不支持错误: " << errorMessage << "\n";
            return 1;
        }
        if (monitor.HasArtifacts()) {
            std::cerr << "拒绝 .dvsp 时系统临时目录出现流程归档文件: " << monitor.DescribeUtf8() << "\n";
            return 1;
        }
        std::cout << "C 接口 .dvsp 拒绝测试通过\n";
        return 0;
    } catch (const std::exception& ex) {
        std::cerr << ex.what() << "\n";
        return 1;
    }
}

int wmain(int argc, wchar_t* argv[]) {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    if (argc >= 2 && std::wstring(argv[1]) == L"dvs-memory-loading-selftest") {
        return RunDvsMemoryLoadingSelfTest(argc, argv);
    }
    if (argc >= 2 && std::wstring(argv[1]) == L"dvsp-reject-selftest") {
        return RunDvspRejectSelfTest(argc, argv);
    }

    constexpr int kFlowIndexBase = 10000;
    const std::wstring dvstPathW = L"Y:\\测试模型\\AOI_120_50_s.dvst";
    const std::wstring dvtCatDogPathW = L"Y:\\测试模型\\猫狗-分类_120_50_s.dvt";
    const std::wstring dvtBalloonPathW = L"Y:\\测试模型\\气球-实例分割_120_50_s.dvt";
    const std::string dvstPath = WideToUtf8(dvstPathW);
    const std::string dvtCatDogPath = WideToUtf8(dvtCatDogPathW);
    const std::string dvtBalloonPath = WideToUtf8(dvtBalloonPathW);

    std::cout << "==== dvst modelIndex 撞键验证 (C API / g_models) ====\n";
    std::cout << "dvst: " << dvstPath << "\n";
    std::cout << "dvt1: " << dvtCatDogPath << "\n";
    std::cout << "dvt2: " << dvtBalloonPath << "\n";
    std::cout << "预期: dvst model_index >= " << kFlowIndexBase
              << "，dvt model_index 在 [0," << kFlowIndexBase << ")，三者互不相同\n\n";

    int idx_dvt1 = dlcv_infer_cpp_load_model_c(dvtCatDogPath.c_str(), 0);
    int idx_dvt2 = dlcv_infer_cpp_load_model_c(dvtBalloonPath.c_str(), 0);
    int idx_dvst = dlcv_infer_cpp_load_model_c(dvstPath.c_str(), 0);

    std::cout << "模型加载结果:\n";
    std::cout << "  dvst model_index = " << idx_dvst << "\n";
    std::cout << "  dvt1 model_index = " << idx_dvt1 << "\n";
    std::cout << "  dvt2 model_index = " << idx_dvt2 << "\n\n";

    bool ok = true;
    if (idx_dvst < 0 || idx_dvt1 < 0 || idx_dvt2 < 0) {
        std::cerr << "FAIL: 有模型加载失败\n";
        const char* err = dlcv_infer_cpp_get_last_error_c();
        if (err) std::cerr << "  last_error: " << err << "\n";
        ok = false;
    }
    if (idx_dvst >= 0 && idx_dvst < kFlowIndexBase) {
        std::cerr << "FAIL: dvst model_index=" << idx_dvst << " 应 >= " << kFlowIndexBase
                  << "（修复前写死 1，会与拿到 model_index=1 的 dvt 在 g_models 撞键、互相覆盖）\n";
        ok = false;
    } else if (idx_dvst >= kFlowIndexBase) {
        std::cout << "PASS: dvst model_index=" << idx_dvst << " >= " << kFlowIndexBase << "\n";
    }
    if (idx_dvt1 >= 0 && idx_dvt1 >= kFlowIndexBase) {
        std::cerr << "FAIL: dvt1 model_index=" << idx_dvt1 << " 应 < " << kFlowIndexBase << "\n";
        ok = false;
    }
    if (idx_dvt2 >= 0 && idx_dvt2 >= kFlowIndexBase) {
        std::cerr << "FAIL: dvt2 model_index=" << idx_dvt2 << " 应 < " << kFlowIndexBase << "\n";
        ok = false;
    }
    if (idx_dvst >= 0 && idx_dvt1 >= 0 && idx_dvt2 >= 0) {
        if (idx_dvst == idx_dvt1 || idx_dvst == idx_dvt2 || idx_dvt1 == idx_dvt2) {
            std::cerr << "FAIL: model_index 撞键！g_models 全局表互相覆盖，推理传 index 会路由到错误模型\n";
            ok = false;
        } else {
            std::cout << "PASS: 三模型 model_index 互不相同，g_models 表不撞键\n";
        }
    }

    if (idx_dvst >= 0) dlcv_infer_cpp_free_model_c(idx_dvst);
    if (idx_dvt1 >= 0) dlcv_infer_cpp_free_model_c(idx_dvt1);
    if (idx_dvt2 >= 0) dlcv_infer_cpp_free_model_c(idx_dvt2);

    std::cout << (ok ? "\nTest PASSED\n" : "\nTest FAILED\n");
    return ok ? 0 : 1;
}
