#include <windows.h>
#include <psapi.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <iomanip>
#include <iostream>
#include <memory>
#include <sstream>
#include <string>
#include <vector>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "dlcv_infer.h"

namespace {
using json = nlohmann::json;
using Clock = std::chrono::steady_clock;

constexpr int kSpeedWindowSeconds = 3;
constexpr int kLeakLoopCount = 10;
constexpr int kGpuDeviceId = 0;
constexpr int kFixedBatchSize = 8;
const std::wstring kModelRoot = L"Y:\\测试模型";

struct ModelCase {
    std::wstring modelFile;
    std::wstring imageFile;
};

const std::vector<ModelCase> kCases = {
    {L"AOI-旋转框检测.dvt", L"AOI-测试.jpg"},
    {L"猫狗-分类.dvt", L"猫狗-猫.jpg"},
    {L"气球-实例分割.dvt", L"气球.jpg"},
    {L"气球-语义分割.dvt", L"气球.jpg"},
    {L"手机屏幕-实例分割.dvt", L"手机屏幕.jpg"},
    {L"引脚定位-目标检测.dvt", L"引脚定位-目标检测.jpg"},
    {L"OCR.dvt", L"OCR-1.jpg"},
};

struct MemorySnapshot {
    double privateMb{0.0};
};

struct SpeedResult {
    double fps{0.0};
    bool supported{false};
};

struct CaseRow {
    std::string modelName;
    std::string loadText;
    std::string inferText;
    std::string categories;
    std::string speedText;
    std::string batchText;
};

std::string WideToUtf8(const std::wstring& w) {
    if (w.empty()) return {};
    int bytes = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()), nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return {};
    std::string out(static_cast<size_t>(bytes), '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()), out.data(), bytes, nullptr, nullptr);
    return out;
}

bool FileExistsW(const std::wstring& p) {
    DWORD attr = GetFileAttributesW(p.c_str());
    return attr != INVALID_FILE_ATTRIBUTES && !(attr & FILE_ATTRIBUTE_DIRECTORY);
}

bool DirExistsW(const std::wstring& p) {
    DWORD attr = GetFileAttributesW(p.c_str());
    return attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY);
}

MemorySnapshot CaptureMemory() {
    PROCESS_MEMORY_COUNTERS_EX pmc{};
    pmc.cb = sizeof(PROCESS_MEMORY_COUNTERS_EX);
    if (!GetProcessMemoryInfo(GetCurrentProcess(), reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&pmc), sizeof(pmc))) {
        return {};
    }
    return MemorySnapshot{static_cast<double>(pmc.PrivateUsage) / 1024.0 / 1024.0};
}

std::vector<unsigned char> ReadAllBytesByFopen(const std::wstring& path) {
    std::vector<unsigned char> buf;
    FILE* fp = nullptr;
    _wfopen_s(&fp, path.c_str(), L"rb");
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

cv::Mat LoadImageByDecode(const std::wstring& path) {
    auto bytes = ReadAllBytesByFopen(path);
    if (bytes.empty()) return {};
    cv::Mat raw(1, static_cast<int>(bytes.size()), CV_8UC1, bytes.data());
    return cv::imdecode(raw, cv::IMREAD_COLOR);
}

std::string Safe(const std::string& s) {
    std::string out = s.empty() ? "-" : s;
    for (auto& ch : out) {
        if (ch == '|') ch = '/';
        if (ch == '\n' || ch == '\r') ch = ' ';
    }
    return out;
}

std::string ToFixed(double v, int precision) {
    std::ostringstream oss;
    oss << std::fixed << std::setprecision(precision) << v;
    return oss.str();
}

std::wstring JoinPath(const std::wstring& root, const std::wstring& file) {
    if (root.empty()) return file;
    const wchar_t tail = root.back();
    if (tail == '\\' || tail == '/') return root + file;
    return root + L"\\" + file;
}

std::wstring BaseName(const std::wstring& p) {
    const size_t pos = p.find_last_of(L"\\/");
    if (pos == std::wstring::npos) return p;
    return p.substr(pos + 1);
}

std::string BuildCategoryList(const dlcv_infer::Result& out) {
    constexpr size_t kMaxShowCount = 20;
    if (out.sampleResults.empty()) return "";
    const auto& objs = out.sampleResults.front().results;
    if (objs.empty()) return "";
    std::string joined;
    const size_t showCount = std::min(objs.size(), kMaxShowCount);
    for (size_t i = 0; i < showCount; ++i) {
        std::string name = dlcv_infer::convertGbkToUtf8(objs[i].categoryName);
        if (name.empty()) name = "unknown";
        joined += name;
        if (i + 1 < showCount) joined += "，";
    }
    if (objs.size() > showCount) {
        joined += " ...(共";
        joined += std::to_string(objs.size());
        joined += "个)";
    }
    return joined;
}

void DisposeResultMasks(dlcv_infer::Result& out) {
    for (auto& sr : out.sampleResults) {
        for (auto& o : sr.results) {
            if (!o.mask.empty()) o.mask.release();
        }
    }
}

SpeedResult RunSpeed(dlcv_infer::Model& model, const cv::Mat& rgb, int batch, bool allowNa) {
    try {
        json p;
        p["threshold"] = 0.05;
        p["with_mask"] = true;
        std::vector<cv::Mat> imgs(batch, rgb);
        for (int i = 0; i < 2; ++i) {
            auto warm = model.InferBatch(imgs, p);
            DisposeResultMasks(warm);
        }
        long long count = 0;
        auto begin = Clock::now();
        while (std::chrono::duration<double>(Clock::now() - begin).count() < kSpeedWindowSeconds) {
            auto out = model.InferBatch(imgs, p);
            DisposeResultMasks(out);
            count++;
        }
        double elapsed = std::chrono::duration<double>(Clock::now() - begin).count();
        if (count <= 0 || elapsed <= 0.0) return allowNa ? SpeedResult{} : SpeedResult{0.0, true};
        return SpeedResult{static_cast<double>(count * batch) / elapsed, true};
    } catch (...) {
        return allowNa ? SpeedResult{} : SpeedResult{0.0, false};
    }
}

double RunLoadFreeLeak(const std::string& modelPathUtf8) {
    const double baseline = CaptureMemory().privateMb;
    for (int i = 0; i < kLeakLoopCount; ++i) {
        std::unique_ptr<dlcv_infer::Model> m(new dlcv_infer::Model(modelPathUtf8, kGpuDeviceId));
        m.reset();
    }
    return CaptureMemory().privateMb - baseline;
}

double RunInferLeak3s(const std::string& modelPathUtf8, const std::wstring& imagePath) {
    const double before = CaptureMemory().privateMb;
    try {
        std::unique_ptr<dlcv_infer::Model> model(new dlcv_infer::Model(modelPathUtf8, kGpuDeviceId));
        cv::Mat bgr = LoadImageByDecode(imagePath);
        if (bgr.empty()) return 0.0;
        cv::Mat rgb;
        cv::cvtColor(bgr, rgb, cv::COLOR_BGR2RGB);
        json p;
        p["threshold"] = 0.05;
        p["with_mask"] = true;
        std::vector<cv::Mat> imgs(1, rgb);
        auto begin = Clock::now();
        while (std::chrono::duration<double>(Clock::now() - begin).count() < kSpeedWindowSeconds) {
            auto out = model->InferBatch(imgs, p);
            DisposeResultMasks(out);
        }
    } catch (...) {}
    return CaptureMemory().privateMb - before;
}

CaseRow RunCase(const std::wstring& modelPath, const std::wstring& imagePath) {
    CaseRow row;
    const std::wstring modelNameW = BaseName(modelPath);
    row.modelName = WideToUtf8(modelNameW);
    row.loadText = "失败";
    row.inferText = "失败";
    row.categories = "-";
    row.speedText = "-";
    row.batchText = "-";

    const auto memBefore = CaptureMemory();
    const auto t0 = Clock::now();
    std::unique_ptr<dlcv_infer::Model> model;
    const std::string modelPathUtf8 = WideToUtf8(modelPath);
    try {
        model.reset(new dlcv_infer::Model(modelPathUtf8, kGpuDeviceId));
    } catch (const std::exception& e) {
        row.categories = Safe(e.what());
        return row;
    }
    const auto t1 = Clock::now();
    const auto memAfter = CaptureMemory();
    const bool loadOk = model && model->modelIndex != -1;
    row.loadText = std::string(loadOk ? "成功" : "失败") + "(" +
                   std::to_string(std::chrono::duration<double, std::milli>(t1 - t0).count()) + "ms,Δ" +
                   std::to_string(memAfter.privateMb - memBefore.privateMb) + "MB)";
    if (!loadOk) return row;

    cv::Mat bgr = LoadImageByDecode(imagePath);
    if (bgr.empty()) {
        row.inferText = "失败";
        row.categories = "图像解码失败";
    } else {
        cv::Mat rgb;
        cv::cvtColor(bgr, rgb, cv::COLOR_BGR2RGB);
        try {
            json p;
            p["threshold"] = 0.05;
            p["with_mask"] = true;
            auto out = model->InferBatch(std::vector<cv::Mat>{rgb}, p);
            row.inferText = out.sampleResults.empty() ? "失败" : "成功";
            row.categories = BuildCategoryList(out);
            if (row.categories.empty()) row.categories = "(空)";
            DisposeResultMasks(out);
        } catch (const std::exception& e) {
            row.inferText = "失败";
            row.categories = Safe(e.what());
        }
        auto s1 = RunSpeed(*model, rgb, 1, false);
        row.speedText = s1.supported ? ("均速 " + std::to_string(s1.fps) + " 张/秒") : "失败";
        auto sb = RunSpeed(*model, rgb, kFixedBatchSize, true);
        row.batchText = sb.supported ? ("均速 " + std::to_string(sb.fps) + " 张/秒") : "N/A";
    }

    model.reset();
    return row;
}

void PrintHeader() {
    std::cout << "| 模型 | 加载 | 推理 | 类别列表 | 3秒速度 | Batch速度 |\n";
    std::cout << "|---|---|---|---|---|---|\n";
}

void PrintRow(const CaseRow& r) {
    std::cout << "| " << Safe(r.modelName) << " | " << Safe(r.loadText) << " | " << Safe(r.inferText) << " | "
              << Safe(r.categories) << " | " << Safe(r.speedText) << " | " << Safe(r.batchText) << " |\n";
}
}  // namespace

int main() {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    std::cout << "==== C++ 测试程序 ====\n";
    std::cout << "模型目录: " << WideToUtf8(kModelRoot) << "\n";
    std::cout << "固定设备: GPU(" << kGpuDeviceId << ")\n";
    std::cout << "固定Batch: " << kFixedBatchSize << "\n\n";

    const bool modelRootOk = DirExistsW(kModelRoot);
    if (!modelRootOk) {
        std::cout << "模型目录不存在: " << WideToUtf8(kModelRoot) << "\n";
    }

    // 内存泄露专项：只跑一个实例分割模型
    std::wstring leakModelPath;
    std::wstring leakImagePath;
    std::wstring leakModelFile;
    if (modelRootOk) {
        for (const auto& c : kCases) {
            if (c.modelFile.find(L"实例分割") == std::wstring::npos) continue;
            const std::wstring mp = JoinPath(kModelRoot, c.modelFile);
            const std::wstring ip = JoinPath(kModelRoot, c.imageFile);
            if (!FileExistsW(mp) || !FileExistsW(ip)) continue;
            leakModelPath = mp;
            leakImagePath = ip;
            leakModelFile = c.modelFile;
            break;
        }
    }

    int total = 0;
    int pass = 0;
    int skipped = 0;
    std::vector<CaseRow> rows;
    rows.reserve(kCases.size());
    for (const auto& c : kCases) {
        const std::wstring modelPath = JoinPath(kModelRoot, c.modelFile);
        const std::wstring imagePath = JoinPath(kModelRoot, c.imageFile);
        if (!modelRootOk) {
            rows.push_back(CaseRow{WideToUtf8(c.modelFile), "跳过", "-", "模型目录不存在", "-", "-"});
            skipped++;
            continue;
        }
        if (!FileExistsW(modelPath) || !FileExistsW(imagePath)) {
            rows.push_back(CaseRow{WideToUtf8(c.modelFile), "跳过", "-", "模型或图片不存在", "-", "-"});
            skipped++;
            continue;
        }
        total++;
        auto row = RunCase(modelPath, imagePath);
        rows.push_back(row);
        if (row.loadText.find("成功") == 0 && row.inferText == "成功") pass++;
    }

    try { dlcv_infer::Utils::FreeAllModels(); } catch (...) {}

    rows.push_back(CaseRow{"汇总",
                           "总数=" + std::to_string(total),
                           "成功=" + std::to_string(pass),
                           "失败=" + std::to_string(total - pass),
                           "-",
                           "-"});

    PrintHeader();
    for (const auto& r : rows) PrintRow(r);

    std::cout << "\n==== 内存泄露专项(仅测1个实例分割模型) ====\n";
    if (!modelRootOk) {
        std::cout << "跳过：模型目录不存在\n";
    } else if (leakModelPath.empty() || leakImagePath.empty()) {
        std::cout << "跳过：未找到可用实例分割模型\n";
    } else {
        std::cout << "模型: " << WideToUtf8(BaseName(leakModelPath)) << "\n";
        const std::string leakModelPathUtf8 = WideToUtf8(leakModelPath);
        try {
            const double inc = RunLoadFreeLeak(leakModelPathUtf8);
            std::cout << "加载/释放循环" << kLeakLoopCount << "次内存增量: " << ToFixed(inc, 2) << "MB\n";
        } catch (...) {
            std::cout << "加载/释放循环" << kLeakLoopCount << "次内存增量: 错误\n";
        }
        try {
            const double inc = RunInferLeak3s(leakModelPathUtf8, leakImagePath);
            std::cout << "推理" << kSpeedWindowSeconds << "秒内存增量: " << ToFixed(inc, 2) << "MB\n";
        } catch (...) {
            std::cout << "推理" << kSpeedWindowSeconds << "秒内存增量: 错误\n";
        }
    }

    if (!modelRootOk) return 2;
    if (total == 0 && skipped > 0) return 2;
    return total == pass ? 0 : 1;
}
