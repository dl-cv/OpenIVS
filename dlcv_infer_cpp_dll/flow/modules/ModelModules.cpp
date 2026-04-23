#include "flow/modules/ModelModules.h"

#include <algorithm>
#include <cmath>
#include <mutex>
#include <stdexcept>
#include <unordered_map>

#include "opencv2/imgproc.hpp"

#if defined(_MSC_VER) && defined(_DEBUG)
#pragma optimize("gt", on)
#endif

namespace dlcv_infer {
namespace flow {

static std::string MakeModelKey(const std::string& modelPathUtf8, int deviceId) {
    return modelPathUtf8 + "|dev:" + std::to_string(deviceId);
}

ModelPool& ModelPool::Instance() {
    static ModelPool s;
    return s;
}

std::shared_ptr<dlcv_infer::Model> ModelPool::Get(const std::string& modelPathUtf8, int deviceId) {
    if (modelPathUtf8.empty()) {
        throw std::invalid_argument("model_path is empty");
    }
    const std::string key = MakeModelKey(modelPathUtf8, deviceId);
    std::lock_guard<std::mutex> lk(_mu);
    auto it = _cache.find(key);
    if (it != _cache.end() && it->second) {
        return it->second;
    }

    // FlowGraph 内部按 UTF-8 存储；现有 dlcv_infer::Model 构造函数按“输入为 GBK”处理
    const std::string gbkPath = dlcv_infer::convertUtf8ToGbk(modelPathUtf8);
    auto model = std::make_shared<dlcv_infer::Model>(gbkPath, deviceId);
    _cache[key] = model;
    return model;
}

void ModelPool::Clear() {
    std::lock_guard<std::mutex> lk(_mu);
    _cache.clear();
}

void BaseModelModule::LoadModel() {
    if (_model) return;

    int deviceId = _deviceId;
    try {
        if (Context != nullptr) {
            deviceId = Context->Get<int>("device_id", deviceId);
        }
    } catch (...) {}

    _model = ModelPool::Instance().Get(_modelPathUtf8, deviceId);
}

static void TryAddParam(Json& p, const Json& props, const std::string& key) {
    if (!props.is_object() || !props.contains(key)) return;
    const Json& v = props.at(key);
    if (v.is_null()) return;

    // 尽量保持类型：bool/int/double/string
    if (v.is_boolean()) { p[key] = v.get<bool>(); return; }
    if (v.is_number_integer()) { p[key] = v.get<long long>(); return; }
    if (v.is_number()) { p[key] = v.get<double>(); return; }
    if (v.is_string()) {
        const std::string s = v.get<std::string>();
        // 尝试数字/布尔解析，失败则作为字符串
        if (s == "true" || s == "True" || s == "TRUE") { p[key] = true; return; }
        if (s == "false" || s == "False" || s == "FALSE") { p[key] = false; return; }
        try { size_t idx = 0; double dv = std::stod(s, &idx); if (idx == s.size()) { p[key] = dv; return; } } catch (...) {}
        p[key] = s;
        return;
    }
    // 兜底：直接塞入 JSON
    p[key] = v;
}

static Json ConvertToLocalSamples(
    const dlcv_infer::Result& res,
    bool includeMask,
    bool emitMaskRle,
    bool emitMaskDerivedMeta) {
    Json list = Json::array();
    if (res.sampleResults.empty()) return list;
    const auto& sr = res.sampleResults[0];
    std::unordered_map<std::string, std::string> categoryNameCache;
    categoryNameCache.reserve(16);
    for (const auto& obj : sr.results) {
        Json o = Json::object();
        o["category_id"] = obj.categoryId;
        // dlcv_infer::Model 侧把 categoryName 从 UTF-8 转为 GBK 了；FlowGraph 内统一使用 UTF-8
        auto itCachedName = categoryNameCache.find(obj.categoryName);
        if (itCachedName == categoryNameCache.end()) {
            const std::string utf8Name = dlcv_infer::convertGbkToUtf8(obj.categoryName);
            itCachedName = categoryNameCache.emplace(obj.categoryName, utf8Name).first;
        }
        o["category_name"] = itCachedName->second;
        o["score"] = obj.score;
        o["area"] = obj.area;
        o["bbox"] = obj.bbox;
        o["with_bbox"] = obj.withBbox;
        const bool withMask = includeMask && obj.withMask;
        o["with_mask"] = withMask;
        o["with_angle"] = obj.withAngle;
        o["angle"] = obj.withAngle ? obj.angle : -100.0;

        if (withMask && !obj.mask.empty()) {
            if (emitMaskDerivedMeta) {
                double maskArea = static_cast<double>(obj.area);
                if (maskArea <= 0.0) {
                    try {
                        if (obj.mask.channels() == 1) {
                            maskArea = static_cast<double>(cv::countNonZero(obj.mask));
                        } else {
                            cv::Mat gray;
                            cv::cvtColor(obj.mask, gray, cv::COLOR_BGR2GRAY);
                            maskArea = static_cast<double>(cv::countNonZero(gray));
                        }
                    } catch (...) {
                        maskArea = 0.0;
                    }
                }
                o["mask_area"] = maskArea;
                cv::RotatedRect rr;
                if (TryComputeMinAreaRect(obj.mask, rr)) {
                    o["mask_min_area_rect"] = Json::array({
                        rr.center.x,
                        rr.center.y,
                        rr.size.width,
                        rr.size.height,
                        rr.angle
                    });
                }
            }
            if (emitMaskRle) {
                try {
                    o["mask_rle"] = MatToMaskInfo(obj.mask);
                } catch (...) {
                    // ignore
                }
            }
        }
        list.push_back(o);
    }
    return list;
}

static Json ConvertSampleResultToLocalSamples(
    const dlcv_infer::SampleResult& sr,
    bool includeMask,
    bool emitMaskRle,
    bool emitMaskDerivedMeta) {
    Json list = Json::array();
    std::unordered_map<std::string, std::string> categoryNameCache;
    categoryNameCache.reserve(16);
    for (const auto& obj : sr.results) {
        Json o = Json::object();
        o["category_id"] = obj.categoryId;
        // dlcv_infer::Model 侧把 categoryName 从 UTF-8 转为 GBK 了；FlowGraph 内统一使用 UTF-8
        auto itCachedName = categoryNameCache.find(obj.categoryName);
        if (itCachedName == categoryNameCache.end()) {
            const std::string utf8Name = dlcv_infer::convertGbkToUtf8(obj.categoryName);
            itCachedName = categoryNameCache.emplace(obj.categoryName, utf8Name).first;
        }
        o["category_name"] = itCachedName->second;
        o["score"] = obj.score;
        o["area"] = obj.area;
        o["bbox"] = obj.bbox;
        o["with_bbox"] = obj.withBbox;
        const bool withMask = includeMask && obj.withMask;
        o["with_mask"] = withMask;
        o["with_angle"] = obj.withAngle;
        o["angle"] = obj.withAngle ? obj.angle : -100.0;

        if (withMask && !obj.mask.empty()) {
            if (emitMaskDerivedMeta) {
                double maskArea = static_cast<double>(obj.area);
                if (maskArea <= 0.0) {
                    try {
                        if (obj.mask.channels() == 1) {
                            maskArea = static_cast<double>(cv::countNonZero(obj.mask));
                        } else {
                            cv::Mat gray;
                            cv::cvtColor(obj.mask, gray, cv::COLOR_BGR2GRAY);
                            maskArea = static_cast<double>(cv::countNonZero(gray));
                        }
                    } catch (...) {
                        maskArea = 0.0;
                    }
                }
                o["mask_area"] = maskArea;
                cv::RotatedRect rr;
                if (TryComputeMinAreaRect(obj.mask, rr)) {
                    o["mask_min_area_rect"] = Json::array({
                        rr.center.x,
                        rr.center.y,
                        rr.size.width,
                        rr.size.height,
                        rr.angle
                    });
                }
            }
            if (emitMaskRle) {
                try {
                    o["mask_rle"] = MatToMaskInfo(obj.mask);
                } catch (...) {
                    // ignore
                }
            }
        }
        list.push_back(o);
    }
    return list;
}

static int ReadIntLike(const Json& v, int dv) {
    try {
        if (v.is_number_integer()) return v.get<int>();
        if (v.is_number()) return static_cast<int>(std::llround(v.get<double>()));
        if (v.is_string()) return std::stoi(v.get<std::string>());
    } catch (...) {}
    return dv;
}

static int FindMaxBatchSizeRecursively(const Json& token, int current) {
    int best = std::max(1, current);
    try {
        if (token.is_object()) {
            if (token.contains("max_batch_size")) {
                best = std::max(best, std::max(1, ReadIntLike(token.at("max_batch_size"), 1)));
            }
            if (token.contains("max_batch")) {
                best = std::max(best, std::max(1, ReadIntLike(token.at("max_batch"), 1)));
            }
            if (token.contains("batch_size")) {
                best = std::max(best, std::max(1, ReadIntLike(token.at("batch_size"), 1)));
            }
            if (token.contains("max_shape")) {
                const Json& ms = token.at("max_shape");
                if (ms.is_array() && !ms.empty()) {
                    best = std::max(best, std::max(1, ReadIntLike(ms.at(0), 1)));
                }
            }
            for (auto it = token.begin(); it != token.end(); ++it) {
                best = std::max(best, FindMaxBatchSizeRecursively(it.value(), best));
            }
        } else if (token.is_array()) {
            for (const auto& one : token) {
                best = std::max(best, FindMaxBatchSizeRecursively(one, best));
            }
        }
    } catch (...) {}
    return std::max(1, best);
}

static int GetCachedModelBatchLimit(const std::shared_ptr<dlcv_infer::Model>& model) {
    if (!model) return 1;
    static std::mutex s_mu;
    static std::unordered_map<int, int> s_modelLimit;

    const int modelIndex = model->modelIndex;
    {
        std::lock_guard<std::mutex> lk(s_mu);
        auto it = s_modelLimit.find(modelIndex);
        if (it != s_modelLimit.end()) return std::max(1, it->second);
    }

    int limit = 1;
    try {
        const Json info = model->GetModelInfo();
        limit = FindMaxBatchSizeRecursively(info, 1);
    } catch (...) {
        limit = 1;
    }
    limit = std::max(1, limit);

    {
        std::lock_guard<std::mutex> lk(s_mu);
        s_modelLimit[modelIndex] = limit;
    }
    return limit;
}

static int ResolveEffectiveBatchLimit(const std::shared_ptr<dlcv_infer::Model>& model, const Json& props) {
    int cfg = 0;
    try {
        if (props.is_object() && props.contains("batch_size")) {
            cfg = ReadIntLike(props.at("batch_size"), 0);
        }
    } catch (...) {
        cfg = 0;
    }
    const int modelLimit = GetCachedModelBatchLimit(model);
    if (cfg <= 0) return modelLimit;
    return std::max(1, std::min(modelLimit, cfg));
}

static double ReadScoreForSort(const Json& token) {
    try {
        if (token.is_object() && token.contains("score")) {
            const Json& score = token.at("score");
            if (score.is_number()) return score.get<double>();
            if (score.is_string()) return std::stod(score.get<std::string>());
        }
    } catch (...) {}
    return 0.0;
}

static void KeepTopKByScore(Json& samples, int topK) {
    if (!samples.is_array() || topK <= 0 || static_cast<int>(samples.size()) <= topK) return;

    std::vector<Json> ordered;
    ordered.reserve(samples.size());
    for (const auto& sample : samples) {
        ordered.push_back(sample);
    }

    std::stable_sort(ordered.begin(), ordered.end(), [](const Json& a, const Json& b) {
        return ReadScoreForSort(a) > ReadScoreForSort(b);
    });

    Json trimmed = Json::array();
    for (int i = 0; i < topK && i < static_cast<int>(ordered.size()); i++) {
        trimmed.push_back(ordered[static_cast<size_t>(i)]);
    }
    samples = std::move(trimmed);
}

ModuleIO DetModelModule::Process(const std::vector<ModuleImage>& imageList, const Json& /*resultList*/) {
    const std::vector<ModuleImage>& images = imageList;
    std::vector<ModuleImage> outImages;
    Json outResults = Json::array();

    LoadModel();

    // 透传推理参数（与 C# 对齐）
    Json p = Json::object();
    TryAddParam(p, this->Properties, "threshold");
    TryAddParam(p, this->Properties, "iou_threshold");
    TryAddParam(p, this->Properties, "top_k");
    TryAddParam(p, this->Properties, "with_mask");
    TryAddParam(p, this->Properties, "return_polygon");
    TryAddParam(p, this->Properties, "epsilon");
    TryAddParam(p, this->Properties, "batch_size");
    const bool includeMask = p.value("with_mask", true);
    bool requestMaskOutput = includeMask;
    if (includeMask && Context != nullptr) {
        try {
            const Json inferParams = Context->Get<Json>("infer_params", Json::object());
            if (inferParams.is_object() && inferParams.contains("with_mask")) {
                const Json& withMaskToken = inferParams.at("with_mask");
                if (withMaskToken.is_boolean()) {
                    requestMaskOutput = withMaskToken.get<bool>();
                } else if (withMaskToken.is_number_integer()) {
                    requestMaskOutput = (withMaskToken.get<int>() != 0);
                } else if (withMaskToken.is_string()) {
                    const std::string s = withMaskToken.get<std::string>();
                    requestMaskOutput = (s == "1" || s == "true" || s == "True" || s == "TRUE");
                }
            }
        } catch (...) {}
    }
    // 内存敏感场景下，with_mask=false 时不构建 mask_rle；同时也不再计算派生 mask 元数据。
    const bool emitMaskRle = includeMask && requestMaskOutput;
    const bool emitMaskDerivedMeta = false;

    const int effectiveBatch = ResolveEffectiveBatchLimit(_model, this->Properties);
    p["batch_size"] = effectiveBatch;

    std::vector<cv::Mat> rgbInputs;
    std::vector<ModuleImage> wraps;
    std::vector<int> sourceIndices;
    std::unordered_map<std::string, std::vector<int>> buckets;
    std::unordered_map<std::string, int> bucketAreas;

    // 1) 收集可用输入并按 shape 分桶
    for (size_t i = 0; i < images.size(); i++) {
        const ModuleImage& wrap = images[i];
        const cv::Mat& mat = wrap.ImageObject;
        if (mat.empty()) continue;

        // 调用方负责准备通道顺序；流程模型节点直接透传输入 Mat。
        cv::Mat rgbMat = mat;

        const int localIdx = static_cast<int>(rgbInputs.size());
        rgbInputs.push_back(rgbMat);
        wraps.push_back(wrap);
        sourceIndices.push_back(static_cast<int>(i));

        const int h = std::max(0, rgbMat.rows);
        const int w = std::max(0, rgbMat.cols);
        const int c = std::max(1, rgbMat.channels());
        const std::string key = std::to_string(h) + "x" + std::to_string(w) + "x" + std::to_string(c);
        auto it = buckets.find(key);
        if (it == buckets.end()) {
            buckets[key] = std::vector<int>();
            bucketAreas[key] = h * w;
        }
        buckets[key].push_back(localIdx);
    }

    std::vector<Json> sampleByLocal(rgbInputs.size(), Json::array());
    dlcv_infer::json paramsToPass = p.empty() ? dlcv_infer::json(nullptr) : dlcv_infer::json(p);

    // 2) 按桶面积从大到小执行 batch，并回填到 local 下标
    std::vector<std::string> bucketKeys;
    bucketKeys.reserve(buckets.size());
    for (const auto& kv : buckets) bucketKeys.push_back(kv.first);
    std::sort(bucketKeys.begin(), bucketKeys.end(), [&bucketAreas](const std::string& a, const std::string& b) {
        const int aa = bucketAreas.count(a) ? bucketAreas.at(a) : 0;
        const int bb = bucketAreas.count(b) ? bucketAreas.at(b) : 0;
        if (aa != bb) return aa > bb;
        return a < b;
    });

    for (const auto& key : bucketKeys) {
        const auto& localIndices = buckets[key];
        for (int start = 0; start < static_cast<int>(localIndices.size()); start += effectiveBatch) {
            const int take = std::min(effectiveBatch, static_cast<int>(localIndices.size()) - start);
            std::vector<int> chunkLocals;
            std::vector<cv::Mat> chunkMats;
            chunkLocals.reserve(static_cast<size_t>(take));
            chunkMats.reserve(static_cast<size_t>(take));
            for (int k = 0; k < take; k++) {
                const int localIdx = localIndices[static_cast<size_t>(start + k)];
                chunkLocals.push_back(localIdx);
                chunkMats.push_back(rgbInputs[static_cast<size_t>(localIdx)]);
            }

            dlcv_infer::Result res = _model->InferBatch(chunkMats, paramsToPass);
            try {
                if (Context != nullptr) {
                    double prev = Context->Get<double>("flow_dlcv_infer_ms_acc", 0.0);
                    double sdkMs = 0.0;
                    double totalMs = 0.0;
                    dlcv_infer::Model::GetLastInferTiming(sdkMs, totalMs);
                    if (sdkMs <= 0.0) sdkMs = totalMs;
                    if (sdkMs > 0.0) {
                        Context->Set<double>("flow_dlcv_infer_ms_acc", prev + sdkMs);
                    }
                }
            } catch (...) {}
            const auto& batchSamples = res.sampleResults;
            for (int k = 0; k < static_cast<int>(chunkLocals.size()); k++) {
                const int localIdx = chunkLocals[static_cast<size_t>(k)];
                if (k < static_cast<int>(batchSamples.size())) {
                    sampleByLocal[static_cast<size_t>(localIdx)] =
                        ConvertSampleResultToLocalSamples(
                            batchSamples[static_cast<size_t>(k)],
                            includeMask,
                            emitMaskRle,
                            emitMaskDerivedMeta);
                } else {
                    sampleByLocal[static_cast<size_t>(localIdx)] = Json::array();
                }
            }
        }
    }

    // 3) 按原输入顺序回填结果
    int outIndex = 0;
    for (int localIdx = 0; localIdx < static_cast<int>(rgbInputs.size()); localIdx++) {
        const int srcIdx = sourceIndices[static_cast<size_t>(localIdx)];
        const ModuleImage& wrap = wraps[static_cast<size_t>(localIdx)];
        outImages.push_back(images[static_cast<size_t>(srcIdx)]);

        Json entry = Json::object();
        entry["type"] = "local";
        entry["index"] = outIndex;
        entry["origin_index"] = wrap.OriginalIndex;
        entry["transform"] = wrap.TransformState.ToJson();
        entry["sample_results"] = sampleByLocal[static_cast<size_t>(localIdx)];
        outResults.push_back(entry);
        outIndex += 1;
    }

    return ModuleIO(std::move(outImages), std::move(outResults), Json::array());
}

static void EnsureBboxForAllSamples(std::vector<ModuleImage>& imagesOut, Json& resultsOut) {
    if (!resultsOut.is_array()) return;
    const size_t n = std::min(resultsOut.size(), imagesOut.size());
    for (size_t i = 0; i < n; i++) {
        if (!resultsOut.at(i).is_object()) continue;
        Json& entry = resultsOut.at(i);
        if (!entry.contains("sample_results") || !entry["sample_results"].is_array()) continue;
        Json& samples = entry["sample_results"];

        const cv::Mat& imgMat = imagesOut[i].ImageObject;
        const int iw = imgMat.empty() ? 1 : std::max(1, imgMat.cols);
        const int ih = imgMat.empty() ? 1 : std::max(1, imgMat.rows);

        for (auto& s : samples) {
            if (!s.is_object()) continue;
            Json& so = s;
            bool withBbox = false;
            try { withBbox = so.contains("with_bbox") ? so["with_bbox"].get<bool>() : false; } catch (...) { withBbox = false; }
            bool validDims = false;
            try {
                if (so.contains("bbox") && so["bbox"].is_array() && so["bbox"].size() >= 4) {
                    const double bw = std::abs(so["bbox"][2].get<double>());
                    const double bh = std::abs(so["bbox"][3].get<double>());
                    validDims = (bw > 0.0 && bh > 0.0);
                }
            } catch (...) { validDims = false; }

            if (!withBbox || !validDims) {
                so["bbox"] = Json::array({ 0, 0, iw, ih });
                so["with_bbox"] = true;
                so["with_angle"] = false;
                so["angle"] = -100.0;
            }
        }
    }
}

ModuleIO ClsModelModule::Process(const std::vector<ModuleImage>& imageList, const Json& resultList) {
    ModuleIO baseIo = DetModelModule::Process(imageList, resultList);
    const int topK = std::max(0, ReadInt("top_k", 1));
    if (topK > 0 && baseIo.ResultList.is_array()) {
        for (auto& token : baseIo.ResultList) {
            if (!token.is_object()) continue;
            Json& entry = token;
            if (!entry.contains("sample_results") || !entry["sample_results"].is_array()) continue;
            KeepTopKByScore(entry["sample_results"], topK);
        }
    }
    EnsureBboxForAllSamples(baseIo.ImageList, baseIo.ResultList);
    return baseIo;
}

ModuleIO OcrModelModule::Process(const std::vector<ModuleImage>& imageList, const Json& resultList) {
    ModuleIO baseIo = DetModelModule::Process(imageList, resultList);
    EnsureBboxForAllSamples(baseIo.ImageList, baseIo.ResultList);
    return baseIo;
}

// 注册
DLCV_FLOW_REGISTER_MODULE("model/det", DetModelModule)
DLCV_FLOW_REGISTER_MODULE("model/rotated_bbox", RotatedBBoxModelModule)
DLCV_FLOW_REGISTER_MODULE("model/instance_seg", InstanceSegModelModule)
DLCV_FLOW_REGISTER_MODULE("model/semantic_seg", SemanticSegModelModule)
DLCV_FLOW_REGISTER_MODULE("model/cls", ClsModelModule)
DLCV_FLOW_REGISTER_MODULE("model/ocr", OcrModelModule)

} // namespace flow
} // namespace dlcv_infer

