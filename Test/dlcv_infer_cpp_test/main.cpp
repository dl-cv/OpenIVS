#include <windows.h>
#include <psapi.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <functional>
#include <iomanip>
#include <iostream>
#include <memory>
#include <sstream>
#include <string>
#include <unordered_map>
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
const std::wstring kDefaultPressureModelPath = L"C:\\Users\\Administrator\\Desktop\\dvst速度优化\\流程2-各项检测_120_50.dvst";
const std::wstring kDefaultPressureImagePath = L"C:\\Users\\Administrator\\Desktop\\dvst速度优化\\detect_20260401153742_0_6_2904_5248_627_804.jpg";
constexpr int kDefaultPressureBatchSize = 128;
constexpr int kDefaultPressureRuns = 9;
constexpr int kDefaultPressureWarmup = 5;
const std::wstring kSlidingAlignModelPath = L"C:\\Users\\Administrator\\Desktop\\滑窗裁图分割合并\\model_120_50.dvst";
const std::wstring kSlidingAlignImagePath = L"C:\\Users\\Administrator\\Desktop\\滑窗裁图分割合并\\T000001-P000001-C22-I1-G9PHG5X0VFT0000HU8+4JKC-OK.png";
constexpr int kSlidingAlignBatchSize = 1;
constexpr float kSlidingAlignThreshold = 0.5f;
const std::wstring kDemo3Model1Path = L"C:\\Users\\Administrator\\Desktop\\dvst速度优化\\流程1-目标检测-降采样_120_50.dvst";
const std::wstring kDemo3Model2Path = L"C:\\Users\\Administrator\\Desktop\\dvst速度优化\\流程2-各项检测_120_50.dvst";
const std::wstring kDemo3ChainImagePath = L"C:\\Users\\Administrator\\Desktop\\dvst速度优化\\detect_20260401151406_0.jpg";
const std::wstring kDemo3Model2SingleImagePath = L"C:\\Users\\Administrator\\Desktop\\dvst速度优化\\detect_20260401153742_0_6_2904_5248_627_804.jpg";
constexpr int kDemo3CropWidth = 128;
constexpr int kDemo3CropHeight = 192;

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

struct Demo3CropContext {
    cv::Mat cropRgb;
    double translateX{0.0};
    double translateY{0.0};
};

struct Demo3ChainDebugResult {
    int model1ObjectCount{0};
    int cropCount{0};
    int model2BatchLimit{1};
    int finalObjectCount{0};
    std::vector<dlcv_infer::ObjectResult> finalObjects;
};

void DisposeResultMasks(dlcv_infer::Result& out);

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

std::wstring Utf8ToWide(const std::string& s) {
    if (s.empty()) return {};
    int chars = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), nullptr, 0);
    if (chars <= 0) return {};
    std::wstring out(static_cast<size_t>(chars), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), out.data(), chars);
    return out;
}

std::unordered_map<std::string, int> CountCategories(const std::vector<dlcv_infer::ObjectResult>& objects) {
    std::unordered_map<std::string, int> counts;
    for (const auto& obj : objects) {
        std::string category = dlcv_infer::convertGbkToUtf8(obj.categoryName);
        if (category.empty()) {
            category = "unknown";
        }
        counts[category] += 1;
    }
    return counts;
}

bool ContainsCategory(const std::unordered_map<std::string, int>& counts, const std::string& nameUtf8) {
    auto it = counts.find(nameUtf8);
    return it != counts.end() && it->second > 0;
}

void PrintCategorySummary(
    const std::string& title,
    const std::unordered_map<std::string, int>& counts,
    size_t maxRows = 30) {

    std::vector<std::pair<std::string, int>> rows;
    rows.reserve(counts.size());
    for (const auto& kv : counts) rows.push_back(kv);
    std::sort(rows.begin(), rows.end(), [](const std::pair<std::string, int>& a, const std::pair<std::string, int>& b) {
        if (a.second != b.second) return a.second > b.second;
        return a.first < b.first;
    });

    std::cout << title << "（共" << counts.size() << "类）\n";
    if (rows.empty()) {
        std::cout << "  - (空)\n";
        return;
    }
    const size_t show = std::min(maxRows, rows.size());
    for (size_t i = 0; i < show; ++i) {
        std::cout << "  - " << rows[i].first << ": " << rows[i].second << "\n";
    }
    if (rows.size() > show) {
        std::cout << "  ... 其余 " << (rows.size() - show) << " 类省略\n";
    }
}

bool TryMapObjectByTranslate(const dlcv_infer::ObjectResult& localObj, double dx, double dy, dlcv_infer::ObjectResult& mappedObj) {
    if (localObj.bbox.size() < 4) return false;
    mappedObj = localObj;
    mappedObj.bbox = localObj.bbox;
    mappedObj.bbox[0] += dx;
    mappedObj.bbox[1] += dy;
    mappedObj.withBbox = true;
    return true;
}

bool TryClampObjectToImage(const dlcv_infer::ObjectResult& inputObj, int imageW, int imageH, dlcv_infer::ObjectResult& outputObj) {
    if (inputObj.bbox.size() < 4 || imageW <= 0 || imageH <= 0) return false;
    outputObj = inputObj;
    outputObj.bbox = inputObj.bbox;
    const bool isRotated = inputObj.withAngle || inputObj.bbox.size() == 5;
    if (isRotated) {
        const double cx = inputObj.bbox[0];
        const double cy = inputObj.bbox[1];
        const double w = inputObj.bbox[2];
        const double h = inputObj.bbox[3];
        if (w <= 0.0 || h <= 0.0) return false;
        const double left = cx - w * 0.5;
        const double right = cx + w * 0.5;
        const double top = cy - h * 0.5;
        const double bottom = cy + h * 0.5;
        const double cl = std::max(0.0, left);
        const double ct = std::max(0.0, top);
        const double cr = std::min(static_cast<double>(imageW), right);
        const double cb = std::min(static_cast<double>(imageH), bottom);
        if (cr <= cl || cb <= ct) return false;
        outputObj.bbox[0] = (cl + cr) * 0.5;
        outputObj.bbox[1] = (ct + cb) * 0.5;
        outputObj.bbox[2] = cr - cl;
        outputObj.bbox[3] = cb - ct;
    } else {
        const double x = inputObj.bbox[0];
        const double y = inputObj.bbox[1];
        const double w = inputObj.bbox[2];
        const double h = inputObj.bbox[3];
        if (w <= 0.0 || h <= 0.0) return false;
        const double left = std::max(0.0, x);
        const double top = std::max(0.0, y);
        const double right = std::min(static_cast<double>(imageW), x + w);
        const double bottom = std::min(static_cast<double>(imageH), y + h);
        if (right <= left || bottom <= top) return false;
        outputObj.bbox[0] = left;
        outputObj.bbox[1] = top;
        outputObj.bbox[2] = right - left;
        outputObj.bbox[3] = bottom - top;
    }
    outputObj.withBbox = true;
    return true;
}

cv::Point2d GetObjectCenter(const dlcv_infer::ObjectResult& obj) {
    if (obj.bbox.size() < 4) return cv::Point2d(0.0, 0.0);
    const bool isRotated = obj.withAngle || obj.bbox.size() == 5;
    if (isRotated) {
        return cv::Point2d(obj.bbox[0], obj.bbox[1]);
    }
    return cv::Point2d(obj.bbox[0] + obj.bbox[2] * 0.5, obj.bbox[1] + obj.bbox[3] * 0.5);
}

bool BuildCenteredCropContext(
    const cv::Mat& fullImageRgb,
    const cv::Point2d& center,
    int cropW,
    int cropH,
    Demo3CropContext& outCtx) {

    const int requestLeft = static_cast<int>(std::llround(center.x - cropW * 0.5));
    const int requestTop = static_cast<int>(std::llround(center.y - cropH * 0.5));
    const cv::Rect requested(requestLeft, requestTop, cropW, cropH);
    const cv::Rect imageRect(0, 0, fullImageRgb.cols, fullImageRgb.rows);
    const cv::Rect src = requested & imageRect;
    if (src.width <= 0 || src.height <= 0) return false;

    cv::Mat crop(cropH, cropW, fullImageRgb.type(), cv::Scalar::all(0));
    const cv::Rect dst(src.x - requestLeft, src.y - requestTop, src.width, src.height);
    fullImageRgb(src).copyTo(crop(dst));

    outCtx.cropRgb = crop;
    outCtx.translateX = static_cast<double>(requestLeft);
    outCtx.translateY = static_cast<double>(requestTop);
    return true;
}

int GetModelMaxBatchSize(dlcv_infer::Model& model) {
    try {
        const json info = model.GetModelInfo();
        int best = 1;
        std::function<void(const json&)> walk = [&](const json& node) {
            if (node.is_object()) {
                auto pullInt = [&](const char* key) {
                    if (!node.contains(key)) return;
                    const auto& v = node.at(key);
                    if (v.is_number_integer()) {
                        best = std::max(best, std::max(1, v.get<int>()));
                    } else if (v.is_number_float()) {
                        best = std::max(best, std::max(1, static_cast<int>(std::llround(v.get<double>()))));
                    } else if (v.is_string()) {
                        try {
                            best = std::max(best, std::max(1, std::stoi(v.get<std::string>())));
                        } catch (...) {
                        }
                    }
                };
                pullInt("max_batch_size");
                pullInt("max_batch");
                pullInt("batch_size");
                for (auto it = node.begin(); it != node.end(); ++it) {
                    walk(it.value());
                }
            } else if (node.is_array()) {
                for (const auto& x : node) walk(x);
            }
        };
        walk(info);
        return std::max(1, best);
    } catch (...) {
        return 1;
    }
}

std::vector<std::vector<Demo3CropContext>> SplitCropChunks(const std::vector<Demo3CropContext>& source, int chunkSize) {
    std::vector<std::vector<Demo3CropContext>> chunks;
    if (source.empty()) return chunks;
    const int n = std::max(1, chunkSize);
    for (int i = 0; i < static_cast<int>(source.size()); i += n) {
        const int count = std::min(n, static_cast<int>(source.size()) - i);
        chunks.emplace_back(source.begin() + i, source.begin() + i + count);
    }
    return chunks;
}

Demo3ChainDebugResult RunDemo3ChainReference(
    dlcv_infer::Model& model1,
    dlcv_infer::Model& model2,
    const cv::Mat& fullImageRgb,
    bool model2WithMask) {

    Demo3ChainDebugResult out;
    json params;
    params["with_mask"] = false;
    dlcv_infer::Result model1Result = model1.Infer(fullImageRgb, params);

    std::vector<dlcv_infer::ObjectResult> model1Objects;
    if (!model1Result.sampleResults.empty()) {
        for (const auto& obj : model1Result.sampleResults.front().results) {
            dlcv_infer::ObjectResult clamped = obj;
            if (TryClampObjectToImage(obj, fullImageRgb.cols, fullImageRgb.rows, clamped)) {
                model1Objects.push_back(clamped);
            }
        }
    }
    out.model1ObjectCount = static_cast<int>(model1Objects.size());
    DisposeResultMasks(model1Result);

    std::vector<Demo3CropContext> cropContexts;
    cropContexts.reserve(model1Objects.size());
    for (const auto& obj : model1Objects) {
        Demo3CropContext ctx;
        if (BuildCenteredCropContext(fullImageRgb, GetObjectCenter(obj), kDemo3CropWidth, kDemo3CropHeight, ctx)) {
            cropContexts.push_back(std::move(ctx));
        }
    }
    out.cropCount = static_cast<int>(cropContexts.size());

    out.model2BatchLimit = GetModelMaxBatchSize(model2);
    const auto chunks = SplitCropChunks(cropContexts, out.model2BatchLimit);
    json params2;
    params2["with_mask"] = model2WithMask;
    for (const auto& chunk : chunks) {
        std::vector<cv::Mat> mats;
        mats.reserve(chunk.size());
        for (const auto& one : chunk) mats.push_back(one.cropRgb);
        dlcv_infer::Result batchResult = model2.InferBatch(mats, params2);
        for (int i = 0; i < static_cast<int>(chunk.size()); ++i) {
            if (i >= static_cast<int>(batchResult.sampleResults.size())) continue;
            for (const auto& localObj : batchResult.sampleResults[static_cast<size_t>(i)].results) {
                dlcv_infer::ObjectResult mapped = localObj;
                if (!TryMapObjectByTranslate(localObj, chunk[static_cast<size_t>(i)].translateX, chunk[static_cast<size_t>(i)].translateY, mapped)) {
                    continue;
                }
                dlcv_infer::ObjectResult clamped = mapped;
                if (TryClampObjectToImage(mapped, fullImageRgb.cols, fullImageRgb.rows, clamped)) {
                    out.finalObjects.push_back(clamped);
                }
            }
        }
        DisposeResultMasks(batchResult);
    }

    out.finalObjectCount = static_cast<int>(out.finalObjects.size());
    return out;
}

int RunDemo3Validation(
    const std::wstring& model1Path,
    const std::wstring& model2Path,
    const std::wstring& chainImagePath,
    const std::wstring& model2SingleImagePath) {

    std::cout << "==== Demo3 串联专项验证 ====\n";
    std::cout << "model1: " << WideToUtf8(model1Path) << "\n";
    std::cout << "model2: " << WideToUtf8(model2Path) << "\n";
    std::cout << "chain_image: " << WideToUtf8(chainImagePath) << "\n";
    std::cout << "model2_single_image: " << WideToUtf8(model2SingleImagePath) << "\n";

    if (!FileExistsW(model1Path) || !FileExistsW(model2Path) || !FileExistsW(chainImagePath) || !FileExistsW(model2SingleImagePath)) {
        std::cout << "输入文件不存在，请检查路径。\n";
        return 2;
    }

    try {
        dlcv_infer::Model model1(model1Path, kGpuDeviceId);
        dlcv_infer::Model model2(model2Path, kGpuDeviceId);

        cv::Mat singleBgr = LoadImageByDecode(model2SingleImagePath);
        if (singleBgr.empty()) throw std::runtime_error("模型2单图解码失败");
        cv::Mat singleRgb;
        cv::cvtColor(singleBgr, singleRgb, cv::COLOR_BGR2RGB);

        json singleParams;
        singleParams["threshold"] = 0.05;
        singleParams["with_mask"] = true;
        singleParams["batch_size"] = 1;
        dlcv_infer::Result singleResult = model2.InferBatch(std::vector<cv::Mat>{singleRgb}, singleParams);
        std::vector<dlcv_infer::ObjectResult> singleObjects;
        if (!singleResult.sampleResults.empty()) {
            singleObjects = singleResult.sampleResults.front().results;
        }
        const auto singleCounts = CountCategories(singleObjects);
        PrintCategorySummary("模型2单图类别统计", singleCounts);
        const bool singleHasLed = ContainsCategory(singleCounts, "LED");
        const bool singleHasPad = ContainsCategory(singleCounts, "焊盘");
        std::cout << "单图包含 LED: " << (singleHasLed ? "是" : "否")
                  << "，包含 焊盘: " << (singleHasPad ? "是" : "否") << "\n";
        DisposeResultMasks(singleResult);

        cv::Mat chainBgr = LoadImageByDecode(chainImagePath);
        if (chainBgr.empty()) throw std::runtime_error("串联图像解码失败");
        cv::Mat chainRgb;
        cv::cvtColor(chainBgr, chainRgb, cv::COLOR_BGR2RGB);

        Demo3ChainDebugResult chainWithMaskFalse = RunDemo3ChainReference(model1, model2, chainRgb, false);
        const auto chainFalseCounts = CountCategories(chainWithMaskFalse.finalObjects);
        std::cout << "\n-- 串联(模型2 with_mask=false) --\n";
        std::cout << "模型1目标数: " << chainWithMaskFalse.model1ObjectCount
                  << "，有效裁图数: " << chainWithMaskFalse.cropCount
                  << "，模型2最大Batch: " << chainWithMaskFalse.model2BatchLimit
                  << "，最终结果数: " << chainWithMaskFalse.finalObjectCount << "\n";
        PrintCategorySummary("串联类别统计(with_mask=false)", chainFalseCounts);
        const bool chainFalseHasLed = ContainsCategory(chainFalseCounts, "LED");
        const bool chainFalseHasPad = ContainsCategory(chainFalseCounts, "焊盘");
        std::cout << "串联(with_mask=false)包含 LED: " << (chainFalseHasLed ? "是" : "否")
                  << "，包含 焊盘: " << (chainFalseHasPad ? "是" : "否") << "\n";

        Demo3ChainDebugResult chainWithMaskTrue = RunDemo3ChainReference(model1, model2, chainRgb, true);
        const auto chainTrueCounts = CountCategories(chainWithMaskTrue.finalObjects);
        std::cout << "\n-- 串联(模型2 with_mask=true) --\n";
        std::cout << "模型1目标数: " << chainWithMaskTrue.model1ObjectCount
                  << "，有效裁图数: " << chainWithMaskTrue.cropCount
                  << "，模型2最大Batch: " << chainWithMaskTrue.model2BatchLimit
                  << "，最终结果数: " << chainWithMaskTrue.finalObjectCount << "\n";
        PrintCategorySummary("串联类别统计(with_mask=true)", chainTrueCounts);
        const bool chainTrueHasLed = ContainsCategory(chainTrueCounts, "LED");
        const bool chainTrueHasPad = ContainsCategory(chainTrueCounts, "焊盘");
        std::cout << "串联(with_mask=true)包含 LED: " << (chainTrueHasLed ? "是" : "否")
                  << "，包含 焊盘: " << (chainTrueHasPad ? "是" : "否") << "\n";

        bool mismatchDetected = false;
        if (singleHasLed && !chainFalseHasLed) mismatchDetected = true;
        if (singleHasPad && !chainFalseHasPad) mismatchDetected = true;

        std::cout << "\n结论: "
                  << (mismatchDetected ? "检测到串联链路相对单图基线存在类别丢失。" : "未检测到串联链路类别丢失。")
                  << "\n";
        return mismatchDetected ? 1 : 0;
    } catch (const std::exception& e) {
        std::cout << "专项验证异常: " << e.what() << "\n";
        return 1;
    }
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

double RunLoadFreeLeak(const std::wstring& modelPathW) {
    const double baseline = CaptureMemory().privateMb;
    for (int i = 0; i < kLeakLoopCount; ++i) {
        std::unique_ptr<dlcv_infer::Model> m(new dlcv_infer::Model(modelPathW, kGpuDeviceId));
        m.reset();
    }
    return CaptureMemory().privateMb - baseline;
}

double RunInferLeak3s(const std::wstring& modelPathW, const std::wstring& imagePath) {
    const double before = CaptureMemory().privateMb;
    try {
        std::unique_ptr<dlcv_infer::Model> model(new dlcv_infer::Model(modelPathW, kGpuDeviceId));
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
    try {
        model.reset(new dlcv_infer::Model(modelPath, kGpuDeviceId));
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

struct NodeTimingAggregate {
    int nodeId{-1};
    std::string nodeType;
    std::string nodeTitle;
    int count{0};
    double totalMs{0.0};
    double AverageMs() const { return count > 0 ? totalMs / static_cast<double>(count) : 0.0; }
    void Add(double elapsedMs) {
        count += 1;
        totalMs += std::max(0.0, elapsedMs);
    }
};

int ParsePositiveIntArg(const char* s, int fallback) {
    if (s == nullptr) return fallback;
    try {
        const int v = std::stoi(std::string(s));
        return v > 0 ? v : fallback;
    } catch (...) {
        return fallback;
    }
}

int RunBenchmark(const std::wstring& modelPath, const std::wstring& imagePath, int batch, int runs, int warmup) {
    std::cout << "==== 基准测试 ====\n";
    std::cout << "model: " << WideToUtf8(modelPath) << "\n";
    std::cout << "image: " << WideToUtf8(imagePath) << "\n";
    std::cout << "batch: " << batch << "\n";
    std::cout << "runs: " << runs << "\n";
    std::cout << "warmup: " << warmup << "\n";

    if (!FileExistsW(modelPath)) {
        std::cout << "模型不存在\n";
        return 2;
    }
    if (!FileExistsW(imagePath)) {
        std::cout << "图片不存在\n";
        return 2;
    }

    try {
        dlcv_infer::Model model(modelPath, kGpuDeviceId);
        cv::Mat bgr = LoadImageByDecode(imagePath);
        if (bgr.empty()) throw std::runtime_error("图像解码失败");
        cv::Mat rgb;
        cv::cvtColor(bgr, rgb, cv::COLOR_BGR2RGB);

        std::vector<cv::Mat> list;
        list.reserve(static_cast<size_t>(batch));
        for (int i = 0; i < batch; ++i) list.push_back(rgb);

        json params;
        params["threshold"] = 0.5;
        params["with_mask"] = false;
        params["batch_size"] = batch;

        for (int i = 0; i < warmup; ++i) {
            auto warm = model.InferBatch(list, params);
            DisposeResultMasks(warm);
        }

        double sdkSum = 0.0;
        double flowSum = 0.0;
        double outerSum = 0.0;
        int sampleCount = -1;
        std::unordered_map<std::string, NodeTimingAggregate> nodeStats;

        for (int i = 0; i < runs; ++i) {
            const auto t0 = Clock::now();
            auto out = model.InferBatch(list, params);
            const auto t1 = Clock::now();

            if (sampleCount < 0) {
                sampleCount = static_cast<int>(out.sampleResults.size());
            }

            double sdkMs = 0.0;
            double flowMs = 0.0;
            dlcv_infer::Model::GetLastInferTiming(sdkMs, flowMs);
            const double outerMs = std::chrono::duration<double, std::milli>(t1 - t0).count();
            if (sdkMs <= 0.0) sdkMs = outerMs;
            if (flowMs <= 0.0) flowMs = outerMs;
            sdkSum += sdkMs;
            flowSum += flowMs;
            outerSum += outerMs;

            const auto timings = dlcv_infer::Model::GetLastFlowNodeTimings();
            for (const auto& timing : timings) {
                const std::string key = std::to_string(timing.nodeId) + "|" + timing.nodeType + "|" + timing.nodeTitle;
                auto& agg = nodeStats[key];
                if (agg.count == 0) {
                    agg.nodeId = timing.nodeId;
                    agg.nodeType = timing.nodeType;
                    agg.nodeTitle = timing.nodeTitle;
                }
                agg.Add(timing.elapsedMs);
            }

            DisposeResultMasks(out);
        }

        const double avgSdk = sdkSum / std::max(1, runs);
        const double avgFlow = flowSum / std::max(1, runs);
        const double avgOuter = outerSum / std::max(1, runs);
        const double avgOverhead = std::max(0.0, avgFlow - avgSdk);

        std::cout << "sample_count: " << sampleCount << "\n";
        std::cout << "avg_sdk_ms: " << ToFixed(avgSdk, 2) << "\n";
        std::cout << "avg_flow_ms: " << ToFixed(avgFlow, 2) << "\n";
        std::cout << "avg_outer_ms: " << ToFixed(avgOuter, 2) << "\n";
        std::cout << "avg_overhead_ms: " << ToFixed(avgOverhead, 2) << "\n";

        std::vector<NodeTimingAggregate> rows;
        rows.reserve(nodeStats.size());
        for (const auto& kv : nodeStats) rows.push_back(kv.second);
        std::sort(rows.begin(), rows.end(), [](const NodeTimingAggregate& a, const NodeTimingAggregate& b) {
            return a.AverageMs() > b.AverageMs();
        });

        if (!rows.empty()) {
            std::cout << "---- 节点平均耗时 ----\n";
            for (const auto& item : rows) {
                const double share = avgFlow > 0.0 ? item.AverageMs() * 100.0 / avgFlow : 0.0;
                std::cout << "#" << item.nodeId << " [" << item.nodeType << "] "
                          << (item.nodeTitle.empty() ? "-" : item.nodeTitle)
                          << " -> avg=" << ToFixed(item.AverageMs(), 2)
                          << "ms, share=" << ToFixed(share, 1) << "%\n";
            }
        }
        return 0;
    } catch (const std::exception& e) {
        std::cout << "基准异常: " << e.what() << "\n";
        return 1;
    }
}

int RunSlidingAlignOnce(const std::wstring& modelPath, const std::wstring& imagePath) {
    std::cout << "图片: " << WideToUtf8(imagePath) << "\n";
    std::cout << "batch_size: " << kSlidingAlignBatchSize << "\n";
    std::cout << "threshold: " << ToFixed(kSlidingAlignThreshold, 2) << "\n";
    if (!FileExistsW(modelPath) || !FileExistsW(imagePath)) {
        std::cout << "模型或图片不存在，请检查路径。\n";
        return 2;
    }

    try {
        dlcv_infer::Model model(modelPath, kGpuDeviceId);
        cv::Mat bgr = LoadImageByDecode(imagePath);
        if (bgr.empty()) throw std::runtime_error("图像解码失败");
        cv::Mat rgb;
        cv::cvtColor(bgr, rgb, cv::COLOR_BGR2RGB);

        json params;
        params["threshold"] = kSlidingAlignThreshold;
        params["with_mask"] = true;
        params["batch_size"] = kSlidingAlignBatchSize;

        const auto t0 = Clock::now();
        auto out = model.InferBatch(std::vector<cv::Mat>{rgb}, params);
        const auto t1 = Clock::now();

        const double elapsedMs = std::chrono::duration<double, std::milli>(t1 - t0).count();
        std::cout << "\n推理时间: " << ToFixed(elapsedMs, 2) << "ms\n\n";
        std::cout << "输入: RGB\n\n";
        std::cout << "推理结果:\n";
        if (out.sampleResults.empty() || out.sampleResults.front().results.empty()) {
            std::cout << "(空)\n";
        } else {
            const auto& objs = out.sampleResults.front().results;
            for (const auto& obj : objs) {
                std::cout << dlcv_infer::convertGbkToUtf8(obj.categoryName)
                          << ", Score: " << ToFixed(static_cast<double>(obj.score) * 100.0, 1)
                          << ", Area: " << ToFixed(static_cast<double>(obj.area), 1);
                if (obj.bbox.size() >= 4) {
                    std::cout << ", Bbox: [" << ToFixed(obj.bbox[0], 1)
                              << ", " << ToFixed(obj.bbox[1], 1)
                              << ", " << ToFixed(obj.bbox[2], 1)
                              << ", " << ToFixed(obj.bbox[3], 1) << "]";
                }
                std::cout << ", \n";
            }
        }
        DisposeResultMasks(out);
        return 0;
    } catch (const std::exception& e) {
        std::cout << "推理异常: " << e.what() << "\n";
        return 1;
    }
}
}  // namespace

int main(int argc, char* argv[]) {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    if (argc <= 1) {
        return RunSlidingAlignOnce(kSlidingAlignModelPath, kSlidingAlignImagePath);
    }

    if (argc >= 2 && std::string(argv[1]) == "demo3check") {
        const std::wstring model1Path = (argc >= 3) ? Utf8ToWide(argv[2]) : kDemo3Model1Path;
        const std::wstring model2Path = (argc >= 4) ? Utf8ToWide(argv[3]) : kDemo3Model2Path;
        const std::wstring chainImagePath = (argc >= 5) ? Utf8ToWide(argv[4]) : kDemo3ChainImagePath;
        const std::wstring model2SingleImagePath =
            (argc >= 6) ? Utf8ToWide(argv[5]) : kDemo3Model2SingleImagePath;
        return RunDemo3Validation(model1Path, model2Path, chainImagePath, model2SingleImagePath);
    }

    if (argc >= 2 && std::string(argv[1]) == "bench") {
        if (argc < 4) {
            std::cout << "用法: dlcv_infer_cpp_test bench <modelPath> <imagePath> [batch] [runs] [warmup]\n";
            return 2;
        }
        const std::wstring modelPath = Utf8ToWide(argv[2]);
        const std::wstring imagePath = Utf8ToWide(argv[3]);
        const int batch = (argc >= 5) ? ParsePositiveIntArg(argv[4], 1) : 1;
        const int runs = (argc >= 6) ? ParsePositiveIntArg(argv[5], 20) : 20;
        const int warmup = (argc >= 7) ? ParsePositiveIntArg(argv[6], 5) : 5;
        return RunBenchmark(modelPath, imagePath, batch, runs, warmup);
    }

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
        try {
            const double inc = RunLoadFreeLeak(leakModelPath);
            std::cout << "加载/释放循环" << kLeakLoopCount << "次内存增量: " << ToFixed(inc, 2) << "MB\n";
        } catch (...) {
            std::cout << "加载/释放循环" << kLeakLoopCount << "次内存增量: 错误\n";
        }
        try {
            const double inc = RunInferLeak3s(leakModelPath, leakImagePath);
            std::cout << "推理" << kSpeedWindowSeconds << "秒内存增量: " << ToFixed(inc, 2) << "MB\n";
        } catch (...) {
            std::cout << "推理" << kSpeedWindowSeconds << "秒内存增量: 错误\n";
        }
    }

    if (!modelRootOk) return 2;
    if (total == 0 && skipped > 0) return 2;
    return total == pass ? 0 : 1;
}
