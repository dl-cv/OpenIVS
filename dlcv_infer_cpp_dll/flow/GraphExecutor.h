#pragma once

#include <map>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"

namespace dlcv_infer {
namespace flow {

struct NodePublicOutput final {
    std::vector<ModuleImage> ImageList;
    Json ResultList = Json::array();
    Json TemplateList = Json::array();
    std::map<int, Json> ScalarsByIndex; // outputPortIndex -> value
};

/// <summary>
/// GraphExecutor：按 nodes[*].inputs/outputs 的 link 进行最小路由，
/// 将多路输入聚合为主对+额外对（ExtraInputsIn），并将模块的 ExtraOutputs 与 outputs[*] 对齐。
/// 对齐 OpenIVS/DlcvCsharpApi/MainProcess.cs 的 GraphExecutor。
/// </summary>
class GraphExecutor final {
public:
    GraphExecutor(std::vector<Json> nodes, ExecutionContext* context = nullptr);

    std::unordered_map<int, NodePublicOutput> Run();

    /// <summary>
    /// 预加载模型：对 type 以 "model/" 开头的节点调用 module->LoadModel()，
    /// 并返回类似 C# 的 report：{code,message,models:[...]}。
    /// </summary>
    Json LoadModels();

private:
    struct NodeExecOutput final {
        ModuleChannel Main;
        std::vector<ModuleChannel> Extra;
    };

    std::vector<Json> _nodes;
    ExecutionContext* _context = nullptr;

    std::unordered_map<int, NodeExecOutput> _nodeExecMap;     // nodeId -> exec outputs (main+extra)
    std::unordered_map<int, NodePublicOutput> _publicOutputs; // nodeId -> image/result/template/scalars

    static int SafeToInt(const Json& v, int dv);
    static std::string SafeToString(const Json& v, const std::string& dv);
    static std::vector<Json> AsArrayOfObjects(const Json& v);

    static void NormalizeBboxProperties(Json& props);
    static std::unordered_map<int, std::pair<int, int>> BuildLinkSourceMap(const std::vector<Json>& nodesOrdered);

    std::map<int, ModuleChannel> CollectInputPairs(const Json& node, const std::unordered_map<int, std::pair<int, int>>& linkToSource);

    static bool IsScalarPortType(const std::string& tLower);
    static std::string ToLower(std::string s);
};

} // namespace flow
} // namespace dlcv_infer

