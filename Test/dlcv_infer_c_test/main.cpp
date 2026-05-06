#include <iostream>
#include <iomanip>
#include <string>
#include <vector>
#include <cmath>
#include <cstdio>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "dlcv_infer_c_api.h"
#include "dlcv_infer.h"

static std::string GbkToUtf8(const char* gbk) {
    if (!gbk) return {};
    return dlcv_infer::convertGbkToUtf8(std::string(gbk));
}

static std::string ToFixed(double v, int precision) {
    std::ostringstream oss;
    oss << std::fixed << std::setprecision(precision) << v;
    return oss.str();
}

static std::vector<unsigned char> ReadAllBytesByFopen(const wchar_t* path) {
    std::vector<unsigned char> buf;
    FILE* fp = nullptr;
    _wfopen_s(&fp, path, L"rb");
    if (!fp) return buf;
    if (fseek(fp, 0, SEEK_END) != 0) {
        fclose(fp);
        return buf;
    }
    long sz = ftell(fp);
    if (sz <= 0) {
        fclose(fp);
        return buf;
    }
    rewind(fp);
    buf.resize(static_cast<size_t>(sz));
    size_t n = fread(buf.data(), 1, buf.size(), fp);
    fclose(fp);
    if (n != buf.size()) buf.clear();
    return buf;
}

static cv::Mat LoadImageByDecode(const wchar_t* path) {
    auto bytes = ReadAllBytesByFopen(path);
    if (bytes.empty()) return {};
    cv::Mat raw(1, static_cast<int>(bytes.size()), CV_8UC1, bytes.data());
    return cv::imdecode(raw, cv::IMREAD_COLOR);
}

int main() {
    const char* model_path = "Y:\\zxc\\模块化任务测试\\实例分割筛选测试_120_50.dvst";
    const wchar_t* image_path_w = L"Y:\\zxc\\模块化任务测试\\实例分割\\实例分割滑窗大图.png";

    std::cout << "图片: Y:\\zxc\\模块化任务测试\\实例分割\\实例分割滑窗大图.png\n";
    std::cout << "batch_size: 1\n";
    std::cout << "threshold: 0.50\n";

    cv::Mat bgr = LoadImageByDecode(image_path_w);
    if (bgr.empty()) {
        std::cerr << "Failed to load image\n";
        return 1;
    }

    cv::Mat rgb;
    cv::cvtColor(bgr, rgb, cv::COLOR_BGR2RGB);

    DlcvCImage image{};
    image.data_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(rgb.data));
    image.height = rgb.rows;
    image.width = rgb.cols;
    image.channel = rgb.channels();

    DlcvCImageList image_list{};
    image_list.images = &image;
    image_list.n = 1;

    int model_idx = dlcv_infer_cpp_load_model_c(model_path, 0);
    if (model_idx < 0) {
        std::cerr << "Failed to load model: " << model_path << "\n";
        return 1;
    }

    auto t0 = std::chrono::steady_clock::now();
    DlcvCResult result = dlcv_infer_cpp_infer_c(model_idx, &image_list);
    auto t1 = std::chrono::steady_clock::now();
    double elapsed_ms = std::chrono::duration<double, std::milli>(t1 - t0).count();

    std::cout << "推理时间: " << ToFixed(elapsed_ms, 2) << "ms\n";

    if (result.code != 0) {
        std::cerr << "Inference failed: " << (result.message ? result.message : "unknown") << "\n";
        dlcv_infer_cpp_free_model_result_c(&result);
        dlcv_infer_cpp_free_model_c(model_idx);
        return 1;
    }

    int total_objects = 0;
    if (result.sample_results && result.n > 0) {
        for (int i = 0; i < result.n; ++i) {
            total_objects += result.sample_results[i].n;
        }
    }

    std::cout << "推理结果: " << total_objects << "个\n\n";

    int idx = 1;
    if (result.sample_results && result.n > 0) {
        for (int s = 0; s < result.n; ++s) {
            const DlcvCSampleResult& sr = result.sample_results[s];
            if (!sr.results) continue;
            for (int r = 0; r < sr.n; ++r) {
                const DlcvCObjectResult& o = sr.results[r];
                std::cout << "[" << idx << "] " << (o.category_name ? o.category_name : "?")
                          << "            score=" << ToFixed(static_cast<double>(o.score), 2)
                          << "  bbox=(" << ToFixed(o.x, 1) << ", " << ToFixed(o.y, 1)
                          << ", " << ToFixed(o.w, 1) << ", " << ToFixed(o.h, 1) << ")"
                          << "  area=" << ToFixed(static_cast<double>(o.area), 1) << "\n";
                idx++;
            }
        }
    }

    bool ok = true;
    if (result.n != 1) {
        std::cerr << "ERROR: expected 1 sample, got " << result.n << "\n";
        ok = false;
    }
    if (total_objects != 2) {
        std::cerr << "ERROR: expected exactly 2 objects, got " << total_objects << "\n";
        ok = false;
    }
    if (result.sample_results && result.n > 0 && result.sample_results[0].n >= 1) {
        const DlcvCObjectResult& o0 = result.sample_results[0].results[0];
        std::string name0 = GbkToUtf8(o0.category_name);
        if (name0 != "杯子") {
            std::cerr << "ERROR: object[0] category mismatch: " << name0 << "\n";
            ok = false;
        }
        if (std::abs(o0.score - 1.0f) > 0.01f) {
            std::cerr << "ERROR: score mismatch: " << o0.score << "\n";
            ok = false;
        }
        if (!o0.with_bbox) {
            std::cerr << "ERROR: with_bbox mismatch\n";
            ok = false;
        }
        if (std::abs(o0.x - 211.0f) > 1.0f || std::abs(o0.y - 221.0f) > 1.0f ||
            std::abs(o0.w - 160.0f) > 1.0f || std::abs(o0.h - 186.0f) > 1.0f) {
            std::cerr << "ERROR: bbox mismatch\n";
            ok = false;
        }
    } else {
        ok = false;
    }
    if (result.sample_results && result.n > 0 && result.sample_results[0].n >= 2) {
        const DlcvCObjectResult& o1 = result.sample_results[0].results[1];
        std::string name1 = GbkToUtf8(o1.category_name);
        if (name1 != "杯子") {
            std::cerr << "ERROR: object[1] category mismatch: " << name1 << "\n";
            ok = false;
        }
        if (std::abs(o1.score - 1.0f) > 0.01f) {
            std::cerr << "ERROR: object[1] score mismatch: " << o1.score << "\n";
            ok = false;
        }
        if (!o1.with_bbox) {
            std::cerr << "ERROR: object[1] with_bbox mismatch\n";
            ok = false;
        }
        if (std::abs(o1.x - 849.0f) > 1.0f || std::abs(o1.y - 220.0f) > 1.0f ||
            std::abs(o1.w - 161.0f) > 1.0f || std::abs(o1.h - 185.0f) > 1.0f) {
            std::cerr << "ERROR: object[1] bbox mismatch\n";
            ok = false;
        }
    } else {
        ok = false;
    }

    dlcv_infer_cpp_free_model_result_c(&result);
    dlcv_infer_cpp_free_model_c(model_idx);

    if (ok) {
        std::cout << "\nTest PASSED\n";
        return 0;
    } else {
        std::cerr << "\nTest FAILED\n";
        return 1;
    }
}
