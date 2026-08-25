#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <iostream>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

#include <windows.h>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "dlcv_infer_c_api.h"

static std::string WideToUtf8(const std::wstring& value) {
    if (value.empty()) return {};
    const int bytes = WideCharToMultiByte(
        CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return {};
    std::string out(static_cast<size_t>(bytes), '\0');
    WideCharToMultiByte(
        CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), out.data(), bytes, nullptr, nullptr);
    return out;
}

static cv::Mat ReadImageRgb(const std::wstring& path) {
    FILE* fp = nullptr;
    if (_wfopen_s(&fp, path.c_str(), L"rb") != 0 || fp == nullptr) {
        return {};
    }

    _fseeki64(fp, 0, SEEK_END);
    const __int64 size = _ftelli64(fp);
    _fseeki64(fp, 0, SEEK_SET);
    if (size <= 0) {
        std::fclose(fp);
        return {};
    }

    std::vector<unsigned char> bytes(static_cast<size_t>(size));
    const size_t readSize = std::fread(bytes.data(), 1, bytes.size(), fp);
    std::fclose(fp);
    if (readSize != bytes.size()) return {};

    cv::Mat image = cv::imdecode(bytes, cv::IMREAD_UNCHANGED);
    if (image.empty()) return {};
    if (image.channels() == 4) {
        cv::cvtColor(image, image, cv::COLOR_BGRA2RGB);
    } else if (image.channels() == 3) {
        cv::cvtColor(image, image, cv::COLOR_BGR2RGB);
    }
    if (!image.isContinuous()) image = image.clone();
    return image;
}

static long long Quantize(double value, double scale) {
    return static_cast<long long>(std::llround(value * scale));
}

static std::string BuildResultFingerprint(const DlcvCResult& result) {
    std::ostringstream out;
    out << "samples=" << result.n;
    for (int sampleIndex = 0; sampleIndex < result.n; ++sampleIndex) {
        const DlcvCSampleResult& sample = result.sample_results[sampleIndex];
        std::vector<std::string> objects;
        objects.reserve(static_cast<size_t>(std::max(0, sample.n)));
        for (int objectIndex = 0; objectIndex < sample.n; ++objectIndex) {
            const DlcvCObjectResult& object = sample.results[objectIndex];
            std::ostringstream item;
            item << object.category_id << '|'
                 << (object.category_name == nullptr ? std::string() : std::string(object.category_name)) << '|'
                 << Quantize(object.score, 100000.0) << '|'
                 << static_cast<int>(object.with_bbox) << '|'
                 << Quantize(object.x, 1000.0) << ','
                 << Quantize(object.y, 1000.0) << ','
                 << Quantize(object.w, 1000.0) << ','
                 << Quantize(object.h, 1000.0) << '|'
                 << static_cast<int>(object.with_angle) << '|'
                 << Quantize(object.angle, 1000.0) << '|'
                 << static_cast<int>(object.with_mean) << '|'
                 << Quantize(object.foreground_mean, 1000.0) << '|'
                 << Quantize(object.background_mean, 1000.0);
            objects.push_back(item.str());
        }
        std::sort(objects.begin(), objects.end());
        out << ";objects=" << objects.size();
        for (const auto& item : objects) out << '[' << item << ']';
    }
    return out.str();
}

static bool InferFingerprint(
    int modelIndex,
    const cv::Mat& image,
    std::string& fingerprint,
    std::string& error) {
    DlcvCImage cImage{};
    cImage.data_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(image.data));
    cImage.height = image.rows;
    cImage.width = image.cols;
    cImage.channel = image.channels();

    DlcvCImageList imageList{};
    imageList.images = &cImage;
    imageList.n = 1;

    const char* params = R"({"threshold":0.5,"with_mask":false})";
    DlcvCResult result = dlcv_infer_cpp_infer_with_params_c(modelIndex, &imageList, params);
    if (result.code != 0) {
        error = result.message == nullptr ? "infer failed" : result.message;
        dlcv_infer_cpp_free_model_result_c(&result);
        return false;
    }
    if (result.n != 1 || result.sample_results == nullptr) {
        error = "sample result count mismatch";
        dlcv_infer_cpp_free_model_result_c(&result);
        return false;
    }

    fingerprint = BuildResultFingerprint(result);
    dlcv_infer_cpp_free_model_result_c(&result);
    return true;
}

static bool RunConcurrentConsistency(
    const std::string& label,
    int modelIndex,
    const cv::Mat& image,
    const std::string& expectedFingerprint,
    int threadCount,
    int iterationsPerThread) {
    std::atomic<int> ready{0};
    std::atomic<bool> start{false};
    std::atomic<int> failed{0};
    std::mutex errorMutex;
    std::string firstError;
    std::vector<std::thread> workers;
    workers.reserve(static_cast<size_t>(threadCount));

    for (int threadIndex = 0; threadIndex < threadCount; ++threadIndex) {
        workers.emplace_back([&]() {
            ready.fetch_add(1);
            while (!start.load()) std::this_thread::yield();
            for (int iteration = 0; iteration < iterationsPerThread; ++iteration) {
                std::string fingerprint;
                std::string error;
                const bool ok = InferFingerprint(modelIndex, image, fingerprint, error);
                if (!ok || fingerprint != expectedFingerprint) {
                    failed.fetch_add(1);
                    std::lock_guard<std::mutex> lock(errorMutex);
                    if (firstError.empty()) {
                        firstError = ok ? "result fingerprint mismatch" : error;
                    }
                }
            }
        });
    }

    while (ready.load() != threadCount) std::this_thread::yield();
    start.store(true);
    for (auto& worker : workers) worker.join();

    if (failed.load() != 0) {
        std::cerr << "FAIL: " << label << "，失败次数=" << failed.load()
                  << "，首个错误=" << firstError << "\n";
        return false;
    }
    std::cout << "PASS: " << label << "，总次数="
              << threadCount * iterationsPerThread << "\n";
    return true;
}

static int LoadModel(const std::wstring& path) {
    const std::string utf8Path = WideToUtf8(path);
    const int modelIndex = dlcv_infer_cpp_load_model_c(utf8Path.c_str(), 0);
    if (modelIndex < 0) {
        const char* error = dlcv_infer_cpp_get_last_error_c();
        std::cerr << "模型加载失败: " << (error == nullptr ? "unknown" : error) << "\n";
    }
    return modelIndex;
}

int main() {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    const std::wstring dvtPath = L"Y:\\测试模型\\猫狗-分类_120_50_s.dvt";
    const std::wstring dvtImagePath = L"Y:\\测试模型\\猫狗-狗.jpg";
    const std::wstring dvstPath = L"Y:\\测试模型\\AOI_120_50_s.dvst";
    const std::wstring dvstImagePath = L"Y:\\测试模型\\AOI-1.jpg";

    const cv::Mat dvtImage = ReadImageRgb(dvtImagePath);
    const cv::Mat dvstImage = ReadImageRgb(dvstImagePath);
    if (dvtImage.empty() || dvstImage.empty()) {
        std::cerr << "FAIL: 测试图片读取失败\n";
        return 1;
    }

    bool ok = true;

    int dvtIndex = LoadModel(dvtPath);
    if (dvtIndex < 0) return 1;
    std::string dvtBaseline;
    std::string error;
    if (!InferFingerprint(dvtIndex, dvtImage, dvtBaseline, error)) {
        std::cerr << "FAIL: dvt 基准推理失败: " << error << "\n";
        ok = false;
    } else {
        ok = RunConcurrentConsistency("dvt 同一实例并发", dvtIndex, dvtImage, dvtBaseline, 4, 10) && ok;
    }
    ok = (dlcv_infer_cpp_free_model_c(dvtIndex) == 0) && ok;

    int dvstIndex = LoadModel(dvstPath);
    if (dvstIndex < 0) return 1;
    std::string dvstBaseline;
    error.clear();
    if (!InferFingerprint(dvstIndex, dvstImage, dvstBaseline, error)) {
        std::cerr << "FAIL: dvst 基准推理失败: " << error << "\n";
        ok = false;
    } else {
        ok = RunConcurrentConsistency("dvst 同一实例并发", dvstIndex, dvstImage, dvstBaseline, 4, 10) && ok;
    }
    ok = (dlcv_infer_cpp_free_model_c(dvstIndex) == 0) && ok;

    int freshDvstIndex = LoadModel(dvstPath);
    if (freshDvstIndex < 0) return 1;
    ok = RunConcurrentConsistency(
        "dvst 全新实例首次并发", freshDvstIndex, dvstImage, dvstBaseline, 4, 10) && ok;
    ok = (dlcv_infer_cpp_free_model_c(freshDvstIndex) == 0) && ok;

    std::cout << (ok ? "\nTest PASSED\n" : "\nTest FAILED\n");
    return ok ? 0 : 1;
}
