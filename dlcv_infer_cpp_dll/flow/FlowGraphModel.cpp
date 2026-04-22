#include "flow/FlowGraphModel.h"
#include "flow/FlowPayloadTypes.h"
#include "flow/modules/ModelModules.h"

#include <algorithm>
#include <chrono>
#include <fstream>
#include <stdexcept>
#include <unordered_set>

#if defined(_MSC_VER) && defined(_DEBUG)
#pragma optimize("gt", on)
#endif

namespace dlcv_infer {
namespace flow {

static std::string ReadAllTextUtf8(const std::string& path) {
    std::ifstream ifs(path, std::ios::binary);
    if (!ifs) throw std::runtime_error("flow json not found: " + path);
    std::string s;
    ifs.seekg(0, std::ios::end);
    const std::streamoff len = ifs.tellg();
    ifs.seekg(0, std::ios::beg);
    if (len > 0) {
        s.resize(static_cast<size_t>(len));
        ifs.read(&s[0], len);
    }
    return s;
}

static int ParseNodeOrder(const std::string& nodeKey) {
    if (nodeKey.empty()) return INT_MAX;
    try {
        return std::stoi(nodeKey);
    } catch (...) {
        return INT_MAX;
    }
}

static int ResolvePerImageTargetIndex(const FlowByImageEntry& item, int position, int imageCount) {
    if (imageCount <= 0) return -1;
    if (item.OriginIndex >= 0) {
        return item.OriginIndex % imageCount;
    }
    return position >= 0 ? (position % imageCount) : -1;
}

static std::string BuildResultSignature(const FlowResultItem& item) {
    Json j = item.ToJson();
    try {
        return j.dump();
    } catch (...) {
        return std::string();
    }
}

static void AppendResultsDedup(std::vector<FlowResultItem>& target, const std::vector<FlowResultItem>& source) {
    if (source.empty()) return;
    std::unordered_set<std::string> seen;
    seen.reserve(target.size() + source.size());

    for (const auto& item : target) {
        const std::string sig = BuildResultSignature(item);
        if (!sig.empty()) seen.insert(sig);
    }

    for (const auto& item : source) {
        const std::string sig = BuildResultSignature(item);
        if (!sig.empty()) {
            if (seen.find(sig) != seen.end()) {
                continue;
            }
            seen.insert(sig);
        }
        target.push_back(item);
    }
}

static std::vector<FlowFrontendByNodePayload> CollectFrontendPayloads(ExecutionContext& ctx) {
    std::vector<FlowFrontendByNodePayload> payloads;

    // 优先原生 payload（由 output/return_json 直接写入）
    try {
        std::vector<FlowFrontendByNodePayload> typed = ctx.Get<std::vector<FlowFrontendByNodePayload>>(
            "frontend_payloads_by_node", std::vector<FlowFrontendByNodePayload>());
        if (!typed.empty()) {
            std::sort(typed.begin(), typed.end(), [](const FlowFrontendByNodePayload& a, const FlowFrontendByNodePayload& b) {
                if (a.NodeOrder != b.NodeOrder) return a.NodeOrder < b.NodeOrder;
                return a.FallbackOrder < b.FallbackOrder;
            });
            return typed;
        }
    } catch (...) {}

    // 回退 JSON by_node
    Json feJson = ctx.Get<Json>("frontend_json", Json::object());
    Json byNode = Json::object();
    try {
        if (feJson.is_object() && feJson.contains("by_node") && feJson.at("by_node").is_object()) {
            byNode = feJson.at("by_node");
        }
    } catch (...) {}
    if (!byNode.is_object() || byNode.empty()) {
        try {
            byNode = ctx.Get<Json>("frontend_json_by_node", Json::object());
        } catch (...) {
            byNode = Json::object();
        }
    }

    if (byNode.is_object() && !byNode.empty()) {
        int fallbackOrder = 0;
        for (auto it = byNode.begin(); it != byNode.end(); ++it) {
            if (!it.value().is_object()) continue;
            FlowFrontendByNodePayload one;
            one.NodeOrder = ParseNodeOrder(it.key());
            one.FallbackOrder = fallbackOrder++;
            one.Payload = FlowFrontendPayload::FromJson(it.value());
            payloads.push_back(std::move(one));
        }
        std::sort(payloads.begin(), payloads.end(), [](const FlowFrontendByNodePayload& a, const FlowFrontendByNodePayload& b) {
            if (a.NodeOrder != b.NodeOrder) return a.NodeOrder < b.NodeOrder;
            return a.FallbackOrder < b.FallbackOrder;
        });
        return payloads;
    }

    // 回退 last
    try {
        if (feJson.is_object() && feJson.contains("last") && feJson.at("last").is_object()) {
            FlowFrontendByNodePayload lastPayload;
            lastPayload.NodeOrder = INT_MAX;
            lastPayload.FallbackOrder = 0;
            lastPayload.Payload = FlowFrontendPayload::FromJson(feJson.at("last"));
            payloads.push_back(std::move(lastPayload));
            return payloads;
        }
    } catch (...) {}

    try {
        FlowFrontendPayload lastTyped = ctx.Get<FlowFrontendPayload>("frontend_payload_last", FlowFrontendPayload());
        if (!lastTyped.ByImage.empty()) {
            FlowFrontendByNodePayload one;
            one.NodeOrder = INT_MAX;
            one.FallbackOrder = 0;
            one.Payload = std::move(lastTyped);
            payloads.push_back(std::move(one));
        }
    } catch (...) {}

    return payloads;
}

static FlowBatchResult AggregateFrontendResults(ExecutionContext& ctx, int imageCount) {
    FlowBatchResult batch;
    if (imageCount <= 0) return batch;
    batch.PerImageResults.assign(static_cast<size_t>(imageCount), std::vector<FlowResultItem>());

    const std::vector<FlowFrontendByNodePayload> payloads = CollectFrontendPayloads(ctx);
    for (const auto& payload : payloads) {
        const auto& byImage = payload.Payload.ByImage;
        for (int i = 0; i < static_cast<int>(byImage.size()); i++) {
            const FlowByImageEntry& item = byImage[static_cast<size_t>(i)];
            const int targetIndex = ResolvePerImageTargetIndex(item, i, imageCount);
            if (targetIndex < 0 || targetIndex >= imageCount) continue;
            AppendResultsDedup(batch.PerImageResults[static_cast<size_t>(targetIndex)], item.Results);
        }
    }

    return batch;
}

void FlowGraphModel::ReleaseNoexcept() {
    if (!_ownsGlobalModels) return;

    // 仅清理 FlowGraph 侧缓存，避免释放同进程中其它非 FlowGraph 模型。
    // 全局释放（Utils::FreeAllModels）应由上层在明确需要时显式调用。
    try { ModelPool::Instance().Clear(); } catch (...) {}

    _ownsGlobalModels = false;
}

FlowGraphModel::~FlowGraphModel() {
    ReleaseNoexcept();
    _nodes.clear();
    _root = Json::object();
    _loaded = false;
    _deviceId = 0;
    _flowJsonPath.clear();
}

FlowGraphModel::FlowGraphModel(FlowGraphModel&& other) noexcept {
    _nodes = std::move(other._nodes);
    _root = std::move(other._root);
    _loaded = other._loaded;
    _ownsGlobalModels = other._ownsGlobalModels;
    _deviceId = other._deviceId;
    _flowJsonPath = std::move(other._flowJsonPath);

    // moved-from：不再负责释放（避免析构二次触发全局释放）
    other._nodes.clear();
    other._root = Json::object();
    other._loaded = false;
    other._ownsGlobalModels = false;
    other._deviceId = 0;
    other._flowJsonPath.clear();
}

FlowGraphModel& FlowGraphModel::operator=(FlowGraphModel&& other) noexcept {
    if (this == &other) return *this;

    // 先释放当前对象持有的全局释放责任
    ReleaseNoexcept();

    _nodes = std::move(other._nodes);
    _root = std::move(other._root);
    _loaded = other._loaded;
    _ownsGlobalModels = other._ownsGlobalModels;
    _deviceId = other._deviceId;
    _flowJsonPath = std::move(other._flowJsonPath);

    other._nodes.clear();
    other._root = Json::object();
    other._loaded = false;
    other._ownsGlobalModels = false;
    other._deviceId = 0;
    other._flowJsonPath.clear();

    return *this;
}

Json FlowGraphModel::Load(const std::string& flowJsonPath, int deviceId) {
    if (flowJsonPath.empty()) throw std::invalid_argument("flowJsonPath is empty");
    const std::string text = ReadAllTextUtf8(flowJsonPath);
    Json root = Json::parse(text);
    _flowJsonPath = flowJsonPath;
    return LoadFromRoot(root, deviceId);
}

Json FlowGraphModel::LoadFromRoot(const Json& root, int deviceId) {
    if (!root.is_object()) throw std::invalid_argument("flow root is not object");
    if (!root.contains("nodes") || !root.at("nodes").is_array()) {
        throw std::runtime_error("flow json missing nodes array");
    }

    _nodes.clear();
    for (const auto& n : root.at("nodes")) {
        if (n.is_object()) _nodes.push_back(n);
    }
    _root = root;
    _deviceId = deviceId;

    ExecutionContext ctx;
    ctx.Set<int>("device_id", deviceId);
    GraphExecutor exec(_nodes, &ctx);
    Json report = exec.LoadModels();

    // 简化错误信息（与 C# FlowGraphModel.LoadFromRoot 的思路一致）
    int code = 1;
    try { code = report.contains("code") ? report.at("code").get<int>() : 1; } catch (...) { code = 1; }
    if (code != 0) {
        std::string simpleMessage;
        try {
            if (report.contains("models") && report.at("models").is_array()) {
                for (const auto& m : report.at("models")) {
                    if (!m.is_object()) continue;
                    int sc = 0;
                    try { sc = m.contains("status_code") ? m.at("status_code").get<int>() : 0; } catch (...) { sc = 0; }
                    if (sc != 0) {
                        try {
                            if (m.contains("status_message")) simpleMessage = m.at("status_message").dump();
                        } catch (...) {}
                        break;
                    }
                }
            }
        } catch (...) {}
        if (simpleMessage.empty()) {
            try { if (report.contains("message")) simpleMessage = report.at("message").get<std::string>(); } catch (...) {}
        }
        if (simpleMessage.empty()) simpleMessage = "unknown error";
        report = Json::object({ {"code", 1}, {"message", simpleMessage} });
    }

    _loaded = true;
    // 只要执行过 LoadModels，就认为本对象负责在析构时做一次“全局释放”收尾。
    //（即使部分模型加载失败，也可能有已加载的资源需要释放）
    _ownsGlobalModels = true;
    return report;
}

Json FlowGraphModel::GetModelInfo() const {
    if (!_loaded) throw std::runtime_error("flow graph not loaded");
    return _root;
}

Json FlowGraphModel::InferInternal(const std::vector<cv::Mat>& images, const Json& paramsJson) {
    if (!_loaded) throw std::runtime_error("flow graph not loaded");
    if (images.empty()) throw std::invalid_argument("images is empty");

    // 入口与 C# 对齐：前端输入语义为 RGB。
    // 当前流程节点按 frontend_image_color_space 决定是否转换，因此此处仅透传。
    std::vector<cv::Mat> rgbBatch;
    rgbBatch.reserve(images.size());
    for (const auto& img : images) {
        rgbBatch.push_back(img);
    }

    ExecutionContext ctx;
    ctx.Set<cv::Mat>("frontend_image_mat", rgbBatch.empty() ? cv::Mat() : rgbBatch[0]); // 兼容旧单图入口
    ctx.Set<std::vector<cv::Mat>>("frontend_image_mats", rgbBatch);
    ctx.Set<std::vector<cv::Mat>>("frontend_image_mat_list", rgbBatch);
    ctx.Set<std::string>("frontend_image_color_space", "rgb");
    ctx.Set<std::string>("frontend_image_path", std::string());
    ctx.Set<int>("device_id", _deviceId);
    ctx.Set<Json>("infer_params", paramsJson.is_object() ? paramsJson : Json::object());
    ctx.Set<double>("flow_dlcv_infer_ms_acc", 0.0);

    GraphExecutor exec(_nodes, &ctx);
    const auto runStart = std::chrono::steady_clock::now();
    (void)exec.Run();
    const auto runEnd = std::chrono::steady_clock::now();

    const FlowBatchResult batch = AggregateFrontendResults(ctx, static_cast<int>(images.size()));
    Json root = batch.ToFlowRootJson();

    // 未注册模块：控制台已经在 GraphExecutor 中报警，这里把 code/message 暴露到返回 JSON，
    // 方便上层（C++ API / C# 包装）在日志或 UI 中提示“请检查模型是否正确”。
    const std::vector<GraphExecutor::UnregisteredNodeInfo> unregistered = exec.GetLastUnregisteredNodes();
    if (!unregistered.empty()) {
        std::string msg = "以下节点模块未注册，已被跳过，请检查模型/流程 JSON 是否正确：";
        Json details = Json::array();
        for (size_t i = 0; i < unregistered.size(); i++) {
            const auto& u = unregistered[i];
            Json d = Json::object();
            d["node_id"] = u.NodeId;
            d["type"] = u.NodeType;
            d["title"] = u.NodeTitle;
            details.push_back(std::move(d));

            if (i > 0) msg += "; ";
            msg += "type=\"" + u.NodeType + "\", node_id=" + std::to_string(u.NodeId)
                + (u.NodeTitle.empty() ? std::string() : (", title=\"" + u.NodeTitle + "\""));
        }
        root["code"] = 1;
        root["message"] = msg;
        root["unregistered_modules"] = std::move(details);
    } else {
        root["code"] = 0;
        root["message"] = "ok";
    }

    const std::vector<GraphExecutor::NodeTiming> nodeTimings = exec.GetLastNodeTimings();
    Json timing = Json::object();
    timing["flow_infer_ms"] = std::chrono::duration<double, std::milli>(runEnd - runStart).count();

    double dlcvInferMs = 0.0;
    Json timingItems = Json::array();
    for (const auto& item : nodeTimings) {
        Json one = Json::object();
        one["node_id"] = item.NodeId;
        one["node_type"] = item.NodeType;
        one["node_title"] = item.NodeTitle;
        one["elapsed_ms"] = item.ElapsedMs;
        timingItems.push_back(std::move(one));
        if (item.NodeType.rfind("model/", 0) == 0) {
            dlcvInferMs += item.ElapsedMs;
        }
    }
    const double inferMsAcc = ctx.Get<double>("flow_dlcv_infer_ms_acc", 0.0);
    if (inferMsAcc > 0.0) {
        dlcvInferMs = inferMsAcc;
    }
    timing["dlcv_infer_ms"] = dlcvInferMs;
    timing["node_timings"] = std::move(timingItems);
    root["timing"] = std::move(timing);
    return root;
}

Json FlowGraphModel::InferOneOutJson(const cv::Mat& image, const Json& paramsJson) {
    if (image.empty()) throw std::invalid_argument("image is empty");
    Json root = InferInternal(std::vector<cv::Mat>{ image }, paramsJson);
    try {
        if (root.is_object() && root.contains("result_list")) {
            const auto& rl = root.at("result_list");
            if (rl.is_array()) return rl;
        }
    } catch (...) {}
    return Json::array();
}

double FlowGraphModel::Benchmark(const cv::Mat& image, int warmup, int runs) {
    if (image.empty()) throw std::invalid_argument("image is empty");
    if (warmup < 0) warmup = 0;
    if (runs < 1) runs = 1;

    for (int i = 0; i < warmup; i++) {
        (void)InferOneOutJson(image, Json());
    }

    const auto t0 = std::chrono::steady_clock::now();
    for (int i = 0; i < runs; i++) {
        (void)InferOneOutJson(image, Json());
    }
    const auto t1 = std::chrono::steady_clock::now();
    const double ms = std::chrono::duration<double, std::milli>(t1 - t0).count();
    return ms / static_cast<double>(runs);
}

} // namespace flow
} // namespace dlcv_infer

