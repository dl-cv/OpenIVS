#include "flow/GraphExecutor.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdint>
#include <stdexcept>

namespace dlcv_infer {
namespace flow {

static int ReadNodeOrder(const Json& node) {
    try {
        if (node.is_object() && node.contains("order")) {
            const auto& v = node.at("order");
            if (v.is_number_integer()) return v.get<int>();
            if (v.is_number()) return static_cast<int>(v.get<double>());
        }
    } catch (...) {}
    return INT32_MAX - 1;
}

GraphExecutor::GraphExecutor(std::vector<Json> nodes, ExecutionContext* context)
    : _nodes(std::move(nodes)), _context(context) {
    if (_context == nullptr) {
        // 注意：GraphExecutor 不拥有 context 生命周期；但为方便使用，允许传入 nullptr 代表“自带一个”
        static thread_local ExecutionContext s_ctx;
        _context = &s_ctx;
    }
}

int GraphExecutor::SafeToInt(const Json& v, int dv) {
    try {
        if (v.is_number_integer()) return v.get<int>();
        if (v.is_number()) return static_cast<int>(std::llround(v.get<double>()));
        if (v.is_string()) return std::stoi(v.get<std::string>());
    } catch (...) {}
    return dv;
}

std::string GraphExecutor::SafeToString(const Json& v, const std::string& dv) {
    try {
        if (v.is_string()) return v.get<std::string>();
        if (!v.is_null()) return v.dump();
    } catch (...) {}
    return dv;
}

std::vector<Json> GraphExecutor::AsArrayOfObjects(const Json& v) {
    std::vector<Json> out;
    if (!v.is_array()) return out;
    out.reserve(v.size());
    for (const auto& it : v) {
        if (it.is_object()) out.push_back(it);
    }
    return out;
}

std::string GraphExecutor::ToLower(std::string s) {
    for (size_t i = 0; i < s.size(); i++) {
        s[i] = static_cast<char>(std::tolower(static_cast<unsigned char>(s[i])));
    }
    return s;
}

bool GraphExecutor::IsScalarPortType(const std::string& tLower) {
    return (tLower == "bool" || tLower == "boolean" ||
            tLower == "int" || tLower == "integer" ||
            tLower == "str" || tLower == "string" ||
            tLower == "scalar");
}

void GraphExecutor::NormalizeBboxProperties(Json& props) {
    if (!props.is_object()) return;
    if (!(props.contains("bbox_x1") && props.contains("bbox_y1") && props.contains("bbox_x2") && props.contains("bbox_y2"))) return;

    double x1, y1, x2, y2;
    try { x1 = props.at("bbox_x1").get<double>(); } catch (...) { return; }
    try { y1 = props.at("bbox_y1").get<double>(); } catch (...) { return; }
    try { x2 = props.at("bbox_x2").get<double>(); } catch (...) { return; }
    try { y2 = props.at("bbox_y2").get<double>(); } catch (...) { return; }

    const double bx = std::min(x1, x2);
    const double by = std::min(y1, y2);
    const double bw = std::abs(x2 - x1);
    const double bh = std::abs(y2 - y1);

    if (!props.contains("bbox_x")) props["bbox_x"] = bx;
    if (!props.contains("bbox_y")) props["bbox_y"] = by;
    if (!props.contains("bbox_w")) props["bbox_w"] = bw;
    if (!props.contains("bbox_h")) props["bbox_h"] = bh;
}

std::unordered_map<int, std::pair<int, int>> GraphExecutor::BuildLinkSourceMap(const std::vector<Json>& nodesOrdered) {
    std::unordered_map<int, std::pair<int, int>> map; // linkId -> (srcNodeId, srcOutIdx)
    for (const auto& n : nodesOrdered) {
        if (!n.is_object()) continue;
        const int nid = n.contains("id") ? SafeToInt(n.at("id"), -1) : -1;
        if (nid < 0) continue;
        if (!n.contains("outputs")) continue;
        const auto outPorts = AsArrayOfObjects(n.at("outputs"));
        for (int oi = 0; oi < static_cast<int>(outPorts.size()); oi++) {
            const auto& o = outPorts[static_cast<size_t>(oi)];
            if (!o.is_object()) continue;
            if (!o.contains("links")) continue;
            const auto& lv = o.at("links");
            if (!lv.is_array()) continue;
            for (const auto& lidObj : lv) {
                const int lid = SafeToInt(lidObj, -1);
                if (lid >= 0 && map.find(lid) == map.end()) {
                    map[lid] = std::make_pair(nid, oi);
                }
            }
        }
    }
    return map;
}

std::map<int, ModuleChannel> GraphExecutor::CollectInputPairs(
    const Json& node,
    const std::unordered_map<int, std::pair<int, int>>& linkToSource) {

    std::map<int, ModuleChannel> pairs;
    if (!node.is_object()) return pairs;
    if (!node.contains("inputs")) return pairs;

    const auto inMetaList = AsArrayOfObjects(node.at("inputs"));
    if (inMetaList.empty()) return pairs;

    for (int ii = 0; ii < static_cast<int>(inMetaList.size()); ii++) {
        const auto& inp = inMetaList[static_cast<size_t>(ii)];
        if (!inp.is_object()) continue;

        const int linkId = inp.contains("link") ? SafeToInt(inp.at("link"), -1) : -1;
        const std::string dtype = inp.contains("type") ? SafeToString(inp.at("type"), "") : "";
        const std::string dtypeLower = ToLower(dtype);

        if (IsScalarPortType(dtypeLower)) {
            // 标量在 Run() 中单独注入
            continue;
        }

        if (linkId < 0) continue;
        auto itSrc = linkToSource.find(linkId);
        if (itSrc == linkToSource.end()) continue;

        const int pairIdx = ii / 2;
        ModuleChannel ch = pairs.count(pairIdx) ? pairs[pairIdx] : ModuleChannel(std::vector<ModuleImage>(), Json::array(), Json::array());

        const int srcNodeId = itSrc->second.first;
        const int srcOutIdx = itSrc->second.second;
        auto itOut = _nodeExecMap.find(srcNodeId);
        if (itOut == _nodeExecMap.end()) continue;

        const int srcPairIdx = srcOutIdx / 2;
        const NodeExecOutput& srcOut = itOut->second;

        const ModuleChannel* picked = nullptr;
        if (srcPairIdx == 0) {
            picked = &srcOut.Main;
        } else {
            const int ei = srcPairIdx - 1;
            if (ei >= 0 && ei < static_cast<int>(srcOut.Extra.size())) {
                picked = &srcOut.Extra[static_cast<size_t>(ei)];
            }
        }
        if (picked == nullptr) continue;

        if (dtypeLower == "image_chan") {
            ch.ImageList = picked->ImageList;
        } else if (dtypeLower == "result_chan") {
            ch.ResultList = picked->ResultList;
        } else if (dtypeLower == "template_chan" || dtypeLower == "template") {
            ch.TemplateList = picked->TemplateList;
        } else {
            // 未知通道类型：忽略
        }

        pairs[pairIdx] = std::move(ch);
    }

    return pairs;
}

std::unordered_map<int, NodePublicOutput> GraphExecutor::Run() {
    _nodeExecMap.clear();
    _publicOutputs.clear();

    // 1) 排序：按 order，其次按 id
    std::vector<Json> ordered = _nodes;
    std::sort(ordered.begin(), ordered.end(), [](const Json& a, const Json& b) {
        const int ao = ReadNodeOrder(a);
        const int bo = ReadNodeOrder(b);
        if (ao != bo) return ao < bo;
        int aid = 0, bid = 0;
        try { if (a.is_object() && a.contains("id")) aid = GraphExecutor::SafeToInt(a.at("id"), 0); } catch (...) {}
        try { if (b.is_object() && b.contains("id")) bid = GraphExecutor::SafeToInt(b.at("id"), 0); } catch (...) {}
        return aid < bid;
    });

    // 2) linkId -> (srcNodeId, srcOutIdx)
    const auto linkToSource = BuildLinkSourceMap(ordered);

    // 3) 遍历执行
    for (int i = 0; i < static_cast<int>(ordered.size()); i++) {
        const Json& node = ordered[static_cast<size_t>(i)];
        if (!node.is_object()) continue;

        const std::string type = node.contains("type") ? SafeToString(node.at("type"), "") : "";
        const int nodeId = node.contains("id") ? SafeToInt(node.at("id"), i) : i;
        const std::string title = node.contains("title") ? SafeToString(node.at("title"), "") : "";

        Json props = Json::object();
        try {
            if (node.contains("properties") && node.at("properties").is_object()) {
                props = node.at("properties");
            }
        } catch (...) { props = Json::object(); }

        try { NormalizeBboxProperties(props); } catch (...) {}

        auto factory = ModuleRegistry::Get(type);
        if (!factory) {
            continue; // 未注册模块：跳过
        }

        std::unique_ptr<BaseModule> module = factory(nodeId, title, props, _context);
        if (!module) continue;

        // 聚合输入（主对 + 额外对）
        const auto inputPairs = CollectInputPairs(node, linkToSource);
        ModuleChannel mainCh;
        auto itMain = inputPairs.find(0);
        if (itMain != inputPairs.end()) mainCh = itMain->second;

        std::vector<ModuleChannel> extraChannels;
        for (const auto& kv : inputPairs) {
            if (kv.first <= 0) continue;
            extraChannels.push_back(kv.second);
        }

        module->ExtraInputsIn.clear();
        module->ExtraInputsIn.insert(module->ExtraInputsIn.end(), extraChannels.begin(), extraChannels.end());
        module->MainTemplateList = mainCh.TemplateList;

        // 标量输入注入（按索引与名称）
        std::map<int, Json> scalarInputsByIdx;
        std::map<std::string, Json> scalarInputsByName;
        try {
            if (node.contains("inputs")) {
                const auto inMetaList = AsArrayOfObjects(node.at("inputs"));
                for (int ii = 0; ii < static_cast<int>(inMetaList.size()); ii++) {
                    const auto& inp = inMetaList[static_cast<size_t>(ii)];
                    if (!inp.is_object()) continue;
                    const int linkId = inp.contains("link") ? SafeToInt(inp.at("link"), -1) : -1;
                    const std::string dtype = inp.contains("type") ? SafeToString(inp.at("type"), "") : "";
                    const std::string kind = ToLower(dtype);
                    if (!IsScalarPortType(kind)) continue;
                    if (linkId < 0) continue;
                    auto itSrc = linkToSource.find(linkId);
                    if (itSrc == linkToSource.end()) continue;
                    const int srcNodeId2 = itSrc->second.first;
                    const int srcOutIdx2 = itSrc->second.second;

                    auto itPub = _publicOutputs.find(srcNodeId2);
                    if (itPub == _publicOutputs.end()) continue;
                    auto itVal = itPub->second.ScalarsByIndex.find(srcOutIdx2);
                    if (itVal == itPub->second.ScalarsByIndex.end()) continue;

                    scalarInputsByIdx[ii] = itVal->second;
                    if (inp.contains("name") && inp.at("name").is_string()) {
                        const std::string inName = inp.at("name").get<std::string>();
                        if (!inName.empty()) scalarInputsByName[inName] = itVal->second;
                    }
                }
            }
        } catch (...) {
            // 忽略标量注入失败
        }
        module->ScalarInputsByIndex = std::move(scalarInputsByIdx);
        module->ScalarInputsByName = std::move(scalarInputsByName);

        // 执行当前节点
        ModuleIO io = module->Process(mainCh.ImageList, mainCh.ResultList);

        // 保存该节点的全部输出通道（供后续路由）
        NodeExecOutput nodeOut;
        nodeOut.Main = ModuleChannel(io.ImageList, io.ResultList, io.TemplateList);
        nodeOut.Extra = module->ExtraOutputs;
        _nodeExecMap[nodeId] = nodeOut;

        // 对外暴露主通道（与 C# 一致）
        NodePublicOutput pub;
        pub.ImageList = io.ImageList;
        pub.ResultList = io.ResultList;
        pub.TemplateList = io.TemplateList;

        // 标量输出：依据节点 outputs 元信息，从 module->ScalarOutputsByName 取值并按索引写入
        try {
            if (node.contains("outputs")) {
                const auto outPorts = AsArrayOfObjects(node.at("outputs"));
                for (int oi = 0; oi < static_cast<int>(outPorts.size()); oi++) {
                    const auto& meta = outPorts[static_cast<size_t>(oi)];
                    if (!meta.is_object()) continue;
                    const std::string otype = meta.contains("type") ? ToLower(SafeToString(meta.at("type"), "")) : "";
                    if (!IsScalarPortType(otype)) continue;

                    std::string oname;
                    if (meta.contains("name") && meta.at("name").is_string()) oname = meta.at("name").get<std::string>();

                    Json val;
                    bool has = false;
                    if (!oname.empty()) {
                        auto it = module->ScalarOutputsByName.find(oname);
                        if (it != module->ScalarOutputsByName.end()) { val = it->second; has = true; }
                    }
                    if (!has) {
                        auto it2 = module->ScalarOutputsByName.find(std::to_string(oi));
                        if (it2 != module->ScalarOutputsByName.end()) { val = it2->second; has = true; }
                    }

                    // 类型规范化
                    if (otype == "bool" || otype == "boolean") {
                        bool b = false;
                        try { b = val.is_boolean() ? val.get<bool>() : (val.is_number_integer() ? (val.get<int>() != 0) : (val.is_string() ? (val.get<std::string>() == "true" || val.get<std::string>() == "1") : false)); } catch (...) { b = false; }
                        pub.ScalarsByIndex[oi] = b;
                    } else if (otype == "int" || otype == "integer") {
                        int x = 0;
                        try { x = val.is_number_integer() ? val.get<int>() : (val.is_number() ? static_cast<int>(std::llround(val.get<double>())) : (val.is_string() ? std::stoi(val.get<std::string>()) : 0)); } catch (...) { x = 0; }
                        pub.ScalarsByIndex[oi] = x;
                    } else {
                        // str/string/scalar -> string
                        std::string s;
                        try { s = val.is_string() ? val.get<std::string>() : val.dump(); } catch (...) { s = ""; }
                        pub.ScalarsByIndex[oi] = s;
                    }
                }
            }
        } catch (...) {}

        _publicOutputs[nodeId] = pub;
    }

    return _publicOutputs;
}

Json GraphExecutor::LoadModels() {
    // 排序与 Run 一致
    std::vector<Json> ordered = _nodes;
    std::sort(ordered.begin(), ordered.end(), [](const Json& a, const Json& b) {
        const int ao = ReadNodeOrder(a);
        const int bo = ReadNodeOrder(b);
        if (ao != bo) return ao < bo;
        int aid = 0, bid = 0;
        try { if (a.is_object() && a.contains("id")) aid = GraphExecutor::SafeToInt(a.at("id"), 0); } catch (...) {}
        try { if (b.is_object() && b.contains("id")) bid = GraphExecutor::SafeToInt(b.at("id"), 0); } catch (...) {}
        return aid < bid;
    });

    Json report = Json::object();
    Json items = Json::array();
    int failCount = 0;

    for (int i = 0; i < static_cast<int>(ordered.size()); i++) {
        const Json& node = ordered[static_cast<size_t>(i)];
        if (!node.is_object()) continue;

        const std::string type = node.contains("type") ? SafeToString(node.at("type"), "") : "";
        if (type.rfind("model/", 0) != 0) {
            continue; // 仅预加载 model/*
        }

        const int nodeId = node.contains("id") ? SafeToInt(node.at("id"), i) : i;
        const std::string title = node.contains("title") ? SafeToString(node.at("title"), "") : "";

        Json props = Json::object();
        try {
            if (node.contains("properties") && node.at("properties").is_object()) {
                props = node.at("properties");
            }
        } catch (...) { props = Json::object(); }

        std::string modelPath;
        try {
            if (props.is_object() && props.contains("model_path")) {
                modelPath = SafeToString(props.at("model_path"), "");
            }
        } catch (...) { modelPath.clear(); }

        Json item = Json::object();
        item["node_id"] = nodeId;
        item["type"] = type;
        item["title"] = title;
        item["model_path"] = modelPath;

        auto factory = ModuleRegistry::Get(type);
        if (!factory) {
            // 未注册模块：视为失败
            failCount++;
            item["status_code"] = 1;
            item["status_message"] = "module_not_registered";
            items.push_back(item);
            continue;
        }

        try {
            std::unique_ptr<BaseModule> module = factory(nodeId, title, props, _context);
            if (!module) throw std::runtime_error("module_factory_returned_null");
            module->LoadModel();
            item["status_code"] = 0;
            item["status_message"] = "ok";
        } catch (const std::exception& ex) {
            failCount++;
            item["status_code"] = 1;
            item["status_message"] = std::string(ex.what());
        } catch (...) {
            failCount++;
            item["status_code"] = 1;
            item["status_message"] = "unknown_exception";
        }

        items.push_back(item);
    }

    report["code"] = (failCount == 0) ? 0 : 1;
    report["message"] = (failCount == 0) ? "all models loaded" : ("models loaded with " + std::to_string(failCount) + " error(s)");
    report["models"] = items;
    return report;
}

} // namespace flow
} // namespace dlcv_infer

