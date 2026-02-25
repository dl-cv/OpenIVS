#include "flow/modules/ModelModules.h"

#include <algorithm>
#include <cmath>
#include <stdexcept>

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

static Json ConvertToLocalSamples(const dlcv_infer::Result& res) {
    Json list = Json::array();
    if (res.sampleResults.empty()) return list;
    const auto& sr = res.sampleResults[0];
    for (const auto& obj : sr.results) {
        Json o = Json::object();
        o["category_id"] = obj.categoryId;
        // dlcv_infer::Model 侧把 categoryName 从 UTF-8 转为 GBK 了；FlowGraph 内统一使用 UTF-8
        o["category_name"] = dlcv_infer::convertGbkToUtf8(obj.categoryName);
        o["score"] = obj.score;
        o["area"] = obj.area;
        o["bbox"] = obj.bbox;
        o["with_bbox"] = obj.withBbox;
        o["with_mask"] = obj.withMask;
        o["with_angle"] = obj.withAngle;
        o["angle"] = obj.withAngle ? obj.angle : -100.0;

        if (obj.withMask && !obj.mask.empty()) {
            try {
                o["mask_rle"] = MatToMaskInfo(obj.mask);
            } catch (...) {
                // ignore
            }
        }
        list.push_back(o);
    }
    return list;
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
    TryAddParam(p, this->Properties, "return_polygon");
    TryAddParam(p, this->Properties, "epsilon");
    TryAddParam(p, this->Properties, "batch_size");

    int outIndex = 0;
    for (size_t i = 0; i < images.size(); i++) {
        const ModuleImage& wrap = images[i];
        const cv::Mat& mat = wrap.ImageObject;
        if (mat.empty()) continue;

        // C++ 侧约定使用 BGR；不做 RGB/BGR 强制转换
        dlcv_infer::json paramsToPass = p.empty() ? dlcv_infer::json(nullptr) : dlcv_infer::json(p);
        dlcv_infer::Result res = _model->Infer(mat, paramsToPass);

        outImages.push_back(wrap);
        Json entry = Json::object();
        entry["type"] = "local";
        entry["index"] = outIndex;
        entry["origin_index"] = wrap.OriginalIndex;
        entry["transform"] = wrap.TransformState.ToJson();
        entry["sample_results"] = ConvertToLocalSamples(res);
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

