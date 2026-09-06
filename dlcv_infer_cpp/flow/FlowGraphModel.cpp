#include "flow/FlowGraphModel.h"
#include "flow/FlowPayloadTypes.h"
#include "flow/modules/ModelModules.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <climits>
#include <fstream>
#include <stdexcept>
#include <unordered_map>
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

static std::string GetFileNameOnlyLocal(const std::string& path) {
    if (path.empty()) return std::string();
    const size_t pos = path.find_last_of("/\\");
    if (pos == std::string::npos) return path;
    return path.substr(pos + 1);
}

static std::string ReadStringField(const Json& obj, const char* key) {
    try {
        if (obj.is_object() && obj.contains(key) && obj.at(key).is_string()) {
            return obj.at(key).get<std::string>();
        }
    } catch (...) {}
    return std::string();
}

static std::string ResolveModelInfoKey(const Json& item) {
    std::string name = ReadStringField(item, "model_name");
    if (name.empty()) name = ReadStringField(item, "model_path_original");
    if (name.empty()) name = ReadStringField(item, "model_path");
    if (name.empty()) name = ReadStringField(item, "title");
    if (name.empty()) return std::string();

    const std::string fileName = GetFileNameOnlyLocal(name);
    return fileName.empty() ? name : fileName;
}

static int ReadIntField(const Json& object, const char* key, int defaultValue) {
    try {
        if (!object.is_object() || !object.contains(key)) return defaultValue;
        const Json& value = object.at(key);
        if (value.is_number_integer()) return value.get<int>();
        if (value.is_number()) return static_cast<int>(value.get<double>());
        if (value.is_string()) return std::stoi(value.get<std::string>());
    } catch (...) {}
    return defaultValue;
}

static int ReadIntValue(const Json& value, int defaultValue) {
    try {
        if (value.is_number_integer()) return value.get<int>();
        if (value.is_number()) return static_cast<int>(value.get<double>());
        if (value.is_string()) return std::stoi(value.get<std::string>());
    } catch (...) {}
    return defaultValue;
}

static std::string ReadStringFieldOrEmpty(const Json& object, const char* key) {
    return ReadStringField(object, key);
}

static bool IsModelMeta(const Json& item) {
    return item.is_object() && item.contains("model_info") && item.at("model_info").is_object();
}

static bool IsBeforeByOrderAndId(const Json& left, const Json& right) {
    const int leftOrder = ReadIntField(left, "order", INT_MAX);
    const int rightOrder = ReadIntField(right, "order", INT_MAX);
    if (leftOrder != rightOrder) return leftOrder < rightOrder;
    const int leftId = left.contains("node_id")
        ? ReadIntField(left, "node_id", INT_MAX)
        : ReadIntField(left, "id", INT_MAX);
    const int rightId = right.contains("node_id")
        ? ReadIntField(right, "node_id", INT_MAX)
        : ReadIntField(right, "id", INT_MAX);
    return leftId < rightId;
}

static std::vector<Json> ReadObjectArray(const Json& value) {
    std::vector<Json> result;
    if (value.is_array()) {
        for (const auto& item : value) {
            if (item.is_object()) result.push_back(item);
        }
    }
    return result;
}

static int SelectResultModelNodeId(
    const std::vector<Json>& nodes,
    const Json& loadedModelMeta) {
    if (!loadedModelMeta.is_array() || loadedModelMeta.empty()) return -1;

    std::unordered_map<int, Json> nodesById;
    std::unordered_map<int, std::pair<int, int>> linkSources;
    for (const auto& node : nodes) {
        if (!node.is_object()) continue;
        const int nodeId = ReadIntField(node, "id", -1);
        if (nodeId < 0) continue;
        nodesById[nodeId] = node;
        const auto outputs = ReadObjectArray(node.value("outputs", Json::array()));
        for (int outputIndex = 0; outputIndex < static_cast<int>(outputs.size()); ++outputIndex) {
            const Json& output = outputs[static_cast<size_t>(outputIndex)];
            if (!output.contains("links") || !output.at("links").is_array()) continue;
            for (const auto& link : output.at("links")) {
                const int linkId = ReadIntValue(link, -1);
                if (linkId >= 0) linkSources[linkId] = std::make_pair(nodeId, outputIndex);
            }
        }
    }

    std::vector<Json> returnOutputs;
    std::vector<Json> otherOutputs;
    for (const auto& node : nodes) {
        if (!node.is_object()) continue;
        const std::string type = ReadStringFieldOrEmpty(node, "type");
        if (type == "output/return_json") returnOutputs.push_back(node);
        else if (type.rfind("output/", 0) == 0) otherOutputs.push_back(node);
    }
    std::unordered_map<int, std::vector<int>> upstream;
    for (const auto& node : nodes) {
        if (!node.is_object()) continue;
        const int nodeId = ReadIntField(node, "id", -1);
        if (nodeId < 0) continue;
        const auto inputs = ReadObjectArray(node.value("inputs", Json::array()));
        for (const auto& input : inputs) {
            const int linkId = input.contains("link") ? ReadIntValue(input.at("link"), -1) : -1;
            auto source = linkSources.find(linkId);
            if (source != linkSources.end()) upstream[nodeId].push_back(source->second.first);
        }
    }

    auto findModelForOutput = [&](const Json& output) -> int {
        if (!output.is_object()) return -1;
        std::vector<int> reachableModels;
        std::unordered_set<int> visited;
        std::vector<int> stack;
        const auto inputs = ReadObjectArray(output.value("inputs", Json::array()));
        for (const auto& input : inputs) {
            const int linkId = input.contains("link") ? ReadIntValue(input.at("link"), -1) : -1;
            auto source = linkSources.find(linkId);
            if (source != linkSources.end()) stack.push_back(source->second.first);
        }
        while (!stack.empty()) {
            const int nodeId = stack.back();
            stack.pop_back();
            if (nodeId < 0 || !visited.insert(nodeId).second) continue;
            auto nodeIt = nodesById.find(nodeId);
            if (nodeIt == nodesById.end()) continue;
            const std::string type = ReadStringFieldOrEmpty(nodeIt->second, "type");
            if (type.rfind("model/", 0) == 0) {
                reachableModels.push_back(nodeId);
            }
            auto upstreamIt = upstream.find(nodeId);
            if (upstreamIt != upstream.end()) {
                stack.insert(stack.end(), upstreamIt->second.begin(), upstreamIt->second.end());
            }
        }
        if (reachableModels.empty()) return -1;

        Json selectedMeta;
        for (const auto& item : loadedModelMeta) {
            if (!IsModelMeta(item)) continue;
            const int nodeId = ReadIntField(item, "node_id", -1);
            if (std::find(reachableModels.begin(), reachableModels.end(), nodeId) == reachableModels.end()) continue;
            if (!selectedMeta.is_object() || IsBeforeByOrderAndId(selectedMeta, item)) selectedMeta = item;
        }
        return selectedMeta.is_object() ? ReadIntField(selectedMeta, "node_id", -1) : -1;
    };

    std::sort(returnOutputs.begin(), returnOutputs.end(), IsBeforeByOrderAndId);
    std::sort(otherOutputs.begin(), otherOutputs.end(), IsBeforeByOrderAndId);
    for (auto it = returnOutputs.rbegin(); it != returnOutputs.rend(); ++it) {
        const int selected = findModelForOutput(*it);
        if (selected >= 0) return selected;
    }
    for (auto it = otherOutputs.rbegin(); it != otherOutputs.rend(); ++it) {
        const int selected = findModelForOutput(*it);
        if (selected >= 0) return selected;
    }
    return -1;
}

static Json BuildCompatibleModelInfo(
    const std::vector<Json>& nodes,
    const Json& loadedModelMeta) {
    Json first;
    Json last;
    const int selectedResultNodeId = SelectResultModelNodeId(nodes, loadedModelMeta);
    if (loadedModelMeta.is_array()) {
        for (const auto& item : loadedModelMeta) {
            if (!IsModelMeta(item)) continue;
            if (!first.is_object()) first = item;
            if (selectedResultNodeId < 0 || ReadIntField(item, "node_id", -1) == selectedResultNodeId) {
                last = item;
            }
        }
    }
    if (!last.is_object() && loadedModelMeta.is_array()) {
        for (const auto& item : loadedModelMeta) {
            if (IsModelMeta(item)) last = item;
        }
    }
    if (!first.is_object()) {
        return Json();
    }

    Json firstInfo = first.at("model_info");
    Json result = firstInfo;
    Json mergedInfo = firstInfo.contains("model_info") && firstInfo.at("model_info").is_object()
        ? firstInfo.at("model_info") : Json::object();
    if (last.is_object() && last.contains("model_info") && last.at("model_info").is_object()) {
        const Json& lastInfo = last.at("model_info");
        const Json& lastInner = lastInfo.contains("model_info") && lastInfo.at("model_info").is_object()
            ? lastInfo.at("model_info") : lastInfo;
        for (const char* key : {"task_type", "classes", "num_classes"}) {
            if (lastInner.contains(key)) mergedInfo[key] = lastInner.at(key);
        }
    }
    if (firstInfo.contains("model_info") && firstInfo.at("model_info").is_object() &&
        firstInfo.at("model_info").contains("in_channels")) {
        mergedInfo["in_channels"] = firstInfo.at("model_info").at("in_channels");
    }
    result["model_info"] = std::move(mergedInfo);

    if (firstInfo.contains("input_shapes")) result["input_shapes"] = firstInfo.at("input_shapes");
    return result;
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
    batch.PerImageStatuses.assign(static_cast<size_t>(imageCount), FlowInspectionStatus());

    const std::vector<FlowFrontendByNodePayload> payloads = CollectFrontendPayloads(ctx);
    for (const auto& payload : payloads) {
        const auto& byImage = payload.Payload.ByImage;
        for (int i = 0; i < static_cast<int>(byImage.size()); i++) {
            const FlowByImageEntry& item = byImage[static_cast<size_t>(i)];
            const int targetIndex = ResolvePerImageTargetIndex(item, i, imageCount);
            if (targetIndex < 0 || targetIndex >= imageCount) continue;
            AppendResultsDedup(batch.PerImageResults[static_cast<size_t>(targetIndex)], item.Results);
            if (item.HasOk) {
                FlowInspectionStatus& status = batch.PerImageStatuses[static_cast<size_t>(targetIndex)];
                if (!status.HasOk) {
                    status.HasOk = true;
                    status.Ok = item.Ok;
                    status.Reason = item.Reason;
                } else {
                    status.Ok = status.Ok && item.Ok;
                    Json merged = Json::array();
                    const auto appendReasons = [&merged](const Json& reasons) {
                        if (reasons.is_array()) {
                            for (const auto& reason : reasons) {
                                if (std::find(merged.begin(), merged.end(), reason) == merged.end()) merged.push_back(reason);
                            }
                        } else if (!reasons.is_null() &&
                                   std::find(merged.begin(), merged.end(), reasons) == merged.end()) {
                            merged.push_back(reasons);
                        }
                    };
                    appendReasons(status.Reason);
                    appendReasons(item.Reason);
                    status.Reason = merged.empty() ? Json() : std::move(merged);
                }
            }
        }
    }

    return batch;
}

static bool TryResolveFinalThreshold(const Json& paramsJson, double& threshold) {
    if (!paramsJson.is_object() || !paramsJson.contains("threshold")) return false;
    try {
        const Json& value = paramsJson.at("threshold");
        if (!value.is_number()) return false;
        threshold = value.get<double>();
        return std::isfinite(threshold);
    } catch (...) {
        return false;
    }
}

static bool ShouldKeepFinalResult(const Json& result, double threshold) {
    if (!result.is_object() || !result.contains("score")) return true;
    try {
        const Json& scoreValue = result.at("score");
        if (!scoreValue.is_number()) return true;
        const double score = scoreValue.get<double>();
        return !std::isfinite(score) || score >= threshold;
    } catch (...) {
        return true;
    }
}

static void FilterFinalResultArray(Json& results, double threshold) {
    if (!results.is_array()) return;
    for (auto it = results.begin(); it != results.end();) {
        if (ShouldKeepFinalResult(*it, threshold)) {
            ++it;
        } else {
            it = results.erase(it);
        }
    }
}

static void ApplyFinalThresholdFilter(Json& flowRoot, const Json& paramsJson) {
    double threshold = 0.0;
    if (!TryResolveFinalThreshold(paramsJson, threshold)) return;
    if (!flowRoot.is_object() || !flowRoot.contains("result_list")) return;

    Json& resultList = flowRoot["result_list"];
    if (!resultList.is_array()) return;

    bool isBatchContainer = false;
    if (!resultList.empty()) {
        const Json& first = resultList.front();
        isBatchContainer = first.is_object() &&
            first.contains("result_list") &&
            first.at("result_list").is_array();
    }

    if (!isBatchContainer) {
        FilterFinalResultArray(resultList, threshold);
        return;
    }

    for (auto& entry : resultList) {
        if (entry.is_object() && entry.contains("result_list") && entry["result_list"].is_array()) {
            FilterFinalResultArray(entry["result_list"], threshold);
        }
    }
}

void FlowGraphModel::ReleaseOwnedModelsNoexcept() {
    try { _acquiredModelLeases.clear(); } catch (...) {}
    _modelBinaryStore.reset();
}

FlowGraphModel::~FlowGraphModel() {
    ReleaseOwnedModelsNoexcept();
    _nodes.clear();
    _root = Json::object();
    _loadedModelMeta = Json::array();
    _loaded = false;
    _deviceId = 0;
    _flowJsonPath.clear();
    _acquiredModelLeases.clear();
}

FlowGraphModel::FlowGraphModel(FlowGraphModel&& other) noexcept {
    _nodes = std::move(other._nodes);
    _root = std::move(other._root);
    _loadedModelMeta = std::move(other._loadedModelMeta);
    _loaded = other._loaded;
    _deviceId = other._deviceId;
    _flowJsonPath = std::move(other._flowJsonPath);
    _acquiredModelLeases = std::move(other._acquiredModelLeases);
    _modelBinaryStore = std::move(other._modelBinaryStore);

    // moved-from：不再负责释放
    other._nodes.clear();
    other._root = Json::object();
    other._loadedModelMeta = Json::array();
    other._loaded = false;
    other._deviceId = 0;
    other._flowJsonPath.clear();
    other._acquiredModelLeases.clear();
}

FlowGraphModel& FlowGraphModel::operator=(FlowGraphModel&& other) noexcept {
    if (this == &other) return *this;

    // 先释放当前对象持有的模型引用
    ReleaseOwnedModelsNoexcept();

    _nodes = std::move(other._nodes);
    _root = std::move(other._root);
    _loadedModelMeta = std::move(other._loadedModelMeta);
    _loaded = other._loaded;
    _deviceId = other._deviceId;
    _flowJsonPath = std::move(other._flowJsonPath);
    _acquiredModelLeases = std::move(other._acquiredModelLeases);
    _modelBinaryStore = std::move(other._modelBinaryStore);

    other._nodes.clear();
    other._root = Json::object();
    other._loadedModelMeta = Json::array();
    other._loaded = false;
    other._deviceId = 0;
    other._flowJsonPath.clear();
    other._acquiredModelLeases.clear();

    return *this;
}

Json FlowGraphModel::Load(const std::string& flowJsonPath, int deviceId) {
    ModelLifecycleReadGuard lifecycleGuard;
    if (flowJsonPath.empty()) throw std::invalid_argument("flowJsonPath is empty");
    const std::string text = ReadAllTextUtf8(flowJsonPath);
    Json root = Json::parse(text);
    _flowJsonPath = flowJsonPath;
    return LoadFromRoot(root, deviceId, nullptr);
}

Json FlowGraphModel::LoadFromArchive(
    const Json& root,
    std::shared_ptr<const ModelBinaryStore> modelBinaryStore,
    int deviceId) {
    ModelLifecycleReadGuard lifecycleGuard;
    if (!modelBinaryStore) throw std::invalid_argument("流程模型字节存储为空");
    _flowJsonPath.clear();
    return LoadFromRoot(root, deviceId, std::move(modelBinaryStore));
}

Json FlowGraphModel::LoadFromRoot(
    const Json& root,
    int deviceId,
    std::shared_ptr<const ModelBinaryStore> modelBinaryStore) {
    ModelLifecycleReadGuard lifecycleGuard;
    if (!root.is_object()) throw std::invalid_argument("flow root is not object");
    if (!root.contains("nodes") || !root.at("nodes").is_array()) {
        throw std::runtime_error("flow json missing nodes array");
    }

    ReleaseOwnedModelsNoexcept();
    _loaded = false;
    _nodes.clear();
    for (const auto& n : root.at("nodes")) {
        if (n.is_object()) _nodes.push_back(n);
    }
    _root = root;
    _deviceId = deviceId;

    try {
        ExecutionContext ctx;
        ctx.Set<int>("device_id", deviceId);
        if (modelBinaryStore) {
            ctx.Set<std::shared_ptr<const ModelBinaryStore>>(
                "model_binary_store", modelBinaryStore);
        }
        GraphExecutor exec(_nodes, &ctx);
        Json report = exec.LoadModels();
        _loadedModelMeta = ctx.Get<Json>("loaded_model_meta", Json::array());
        if (!_loadedModelMeta.is_array()) {
            _loadedModelMeta = Json::array();
        }

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
                        if (sc == 0) continue;

                        std::string statusMsg;
                        try {
                            if (m.contains("status_message")) {
                                const auto& sm = m.at("status_message");
                                statusMsg = sm.is_string() ? sm.get<std::string>() : sm.dump();
                            }
                        } catch (...) {}
                        if (statusMsg.empty()) statusMsg = "unknown";

                        std::string nodeType, nodeTitle, modelPath;
                        int nodeId = -1;
                        try { if (m.contains("type") && m.at("type").is_string()) nodeType = m.at("type").get<std::string>(); } catch (...) {}
                        try { if (m.contains("title") && m.at("title").is_string()) nodeTitle = m.at("title").get<std::string>(); } catch (...) {}
                        try { if (m.contains("node_id")) nodeId = m.at("node_id").get<int>(); } catch (...) {}
                        try { if (m.contains("model_path") && m.at("model_path").is_string()) modelPath = m.at("model_path").get<std::string>(); } catch (...) {}

                        simpleMessage = statusMsg + ": type=\"" + nodeType + "\" node_id=" + std::to_string(nodeId);
                        if (!nodeTitle.empty()) simpleMessage += " title=\"" + nodeTitle + "\"";
                        if (!modelPath.empty()) simpleMessage += " model_path=\"" + modelPath + "\"";
                        break;
                    }
                }
            } catch (...) {}
            if (simpleMessage.empty()) {
                try { if (report.contains("message")) simpleMessage = report.at("message").get<std::string>(); } catch (...) {}
            }
            if (simpleMessage.empty()) simpleMessage = "unknown error";
            report = Json::object({ {"code", 1}, {"message", simpleMessage} });
        }

        // 使用预加载阶段生成的模型池 key 保留模型引用。
        for (const auto& item : _loadedModelMeta) {
            if (!IsModelMeta(item)) continue;
            std::string key;
            try {
                if (item.contains("model_pool_key") && item.at("model_pool_key").is_string()) {
                    key = item.at("model_pool_key").get<std::string>();
                }
            } catch (...) {}
            if (key.empty()) continue;

            // 去重：同一流程可能多个节点引用同一模型
            const auto existing = std::find_if(
                _acquiredModelLeases.begin(), _acquiredModelLeases.end(),
                [&key](const ModelPoolLease& lease) { return lease.Key() == key; });
            if (existing == _acquiredModelLeases.end()) {
                auto lease = ModelPool::Instance().RetainByKey(key);
                if (!lease) {
                    throw std::runtime_error("流程模型预加载状态失效");
                }
                _acquiredModelLeases.push_back(std::move(lease));
            }
        }

        _modelBinaryStore = std::move(modelBinaryStore);
        _loaded = true;
        return report;
    } catch (...) {
        ReleaseOwnedModelsNoexcept();
        _loaded = false;
        throw;
    }
}

Json FlowGraphModel::GetModelInfo() const {
    ModelLifecycleReadGuard lifecycleGuard;
    if (!_loaded) throw std::runtime_error("flow graph not loaded");
    Json compatible = BuildCompatibleModelInfo(_nodes, _loadedModelMeta);
    return compatible.is_null() ? GetDvsModelInfo() : compatible;
}

Json FlowGraphModel::GetDvsModelInfo() const {
    ModelLifecycleReadGuard lifecycleGuard;
    if (!_loaded) throw std::runtime_error("flow graph not loaded");
    Json root = _root.is_object() ? _root : Json::object();
    try {
        if (root.contains("nodes") && root.at("nodes").is_array()) {
            for (auto& node : root["nodes"]) {
                if (!node.is_object() || !node.contains("properties") || !node["properties"].is_object()) continue;
                auto& properties = node["properties"];
                if (properties.contains("model_path_original") && properties.at("model_path_original").is_string()) {
                    properties["model_path"] = properties.at("model_path_original");
                }
                properties.erase("model_buffer_key");
                properties.erase("model_pool_key");
            }
        }
    } catch (...) {}
    if (_loadedModelMeta.is_array() && !_loadedModelMeta.empty()) {
        Json publicMeta = _loadedModelMeta;
        for (auto& item : publicMeta) {
            if (!item.is_object()) continue;
            if (item.contains("model_path_original") && item.at("model_path_original").is_string()) {
                item["model_path"] = item.at("model_path_original");
            }
            item.erase("model_pool_key");
        }
        root["loaded_model_meta"] = std::move(publicMeta);
    }

    Json modelInfo = Json::object();
    if (_loadedModelMeta.is_array()) {
        for (const auto& item : _loadedModelMeta) {
            if (!item.is_object() || !item.contains("model_info")) continue;
            std::string key = ResolveModelInfoKey(item);
            if (key.empty()) continue;
            if (modelInfo.contains(key)) {
                const std::string baseKey = key;
                const int nodeId = ReadIntField(item, "node_id", -1);
                key = nodeId >= 0 ? baseKey + "#" + std::to_string(nodeId) : baseKey + "#2";
                int suffix = 2;
                while (modelInfo.contains(key)) {
                    key = baseKey + "#" + std::to_string(suffix++);
                }
            }
            modelInfo[key] = item.at("model_info");
        }
    }
    if (!modelInfo.empty()) {
        root["model_info"] = std::move(modelInfo);
    }

    if (_loadedModelMeta.is_array() && !_loadedModelMeta.empty()) {
        for (const auto& item : _loadedModelMeta) {
            if (!IsModelMeta(item)) continue;
            root["input_model_node_id"] = ReadIntField(item, "node_id", -1);
            break;
        }
        root["output_model_node_id"] = SelectResultModelNodeId(_nodes, _loadedModelMeta);
    }

    return root;
}

Json FlowGraphModel::InferInternal(const std::vector<cv::Mat>& images, const Json& paramsJson) {
    ModelLifecycleReadGuard lifecycleGuard;
    if (!_loaded) throw std::runtime_error("flow graph not loaded");
    if (images.empty()) throw std::invalid_argument("images is empty");

    // 入口与 C# 对齐：前端输入语义为 RGB。
    // 调用方负责准备通道顺序；FlowGraph 入口仅透传。
    std::vector<cv::Mat> rgbBatch;
    rgbBatch.reserve(images.size());
    for (const auto& img : images) {
        rgbBatch.push_back(img);
    }

    ExecutionContext ctx;
    ctx.Set<cv::Mat>("frontend_image_mat", rgbBatch.empty() ? cv::Mat() : rgbBatch[0]); // 兼容旧单图入口
    ctx.Set<std::vector<cv::Mat>>("frontend_image_mats", rgbBatch);
    ctx.Set<std::vector<cv::Mat>>("frontend_image_mat_list", rgbBatch);
    ctx.Set<std::string>("frontend_image_path", std::string());
    ctx.Set<int>("device_id", _deviceId);
    if (_modelBinaryStore) {
        ctx.Set<std::shared_ptr<const ModelBinaryStore>>(
            "model_binary_store", _modelBinaryStore);
    }
    ctx.Set<Json>("infer_params", paramsJson.is_object() ? paramsJson : Json::object());
    ctx.Set<double>("flow_dlcv_infer_ms_acc", 0.0);

    GraphExecutor exec(_nodes, &ctx);
    const auto runStart = std::chrono::steady_clock::now();
    (void)exec.Run();
    const auto runEnd = std::chrono::steady_clock::now();

    const FlowBatchResult batch = AggregateFrontendResults(ctx, static_cast<int>(images.size()));
    Json root = batch.ToFlowRootJson();
    ApplyFinalThresholdFilter(root, paramsJson);

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
    ModelLifecycleReadGuard lifecycleGuard;
    if (image.empty()) throw std::invalid_argument("image is empty");
    Json root = InferInternal(std::vector<cv::Mat>{ image }, paramsJson);
    try {
        if (root.is_object() && root.contains("result_list") && root.at("result_list").is_array()) {
            if (root.contains("ok") && root.at("ok").is_boolean()) {
                Json out = Json::object();
                out["result_list"] = root.at("result_list");
                out["ok"] = root.at("ok");
                if (root.contains("reason")) out["reason"] = root.at("reason");
                return out;
            }
            return root.at("result_list");
        }
    } catch (...) {}
    return Json::array();
}

double FlowGraphModel::Benchmark(const cv::Mat& image, int warmup, int runs) {
    ModelLifecycleReadGuard lifecycleGuard;
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

