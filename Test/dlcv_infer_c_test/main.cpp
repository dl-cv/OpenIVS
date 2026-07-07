#include <iostream>
#include <string>

#include <windows.h>

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

int main() {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

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
