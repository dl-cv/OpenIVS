#pragma once

#include <string>
#include <vector>

#include "dlcv_infer.h"
#include "flow/ExecutionContext.h"
#include "flow/FlowTypes.h"
#include "flow/GraphExecutor.h"

namespace dlcv_infer {
namespace flow {

/// <summary>
/// 流程图推理模型封装：与普通模型一致的调用方式（先加载，再推理/测速）。
/// 对齐 OpenIVS/DlcvCsharpApi/FlowGraphModel.cs 的接口风格，但为纯 C++ 实现。
/// </summary>
class FlowGraphModel final {
public:
    FlowGraphModel() = default;
    DLCV_INFER_CPP_DLL_API ~FlowGraphModel();

    FlowGraphModel(const FlowGraphModel&) = delete;
    FlowGraphModel& operator=(const FlowGraphModel&) = delete;
    DLCV_INFER_CPP_DLL_API FlowGraphModel(FlowGraphModel&& other) noexcept;
    DLCV_INFER_CPP_DLL_API FlowGraphModel& operator=(FlowGraphModel&& other) noexcept;

    bool IsLoaded() const { return _loaded; }

    /// <summary>
    /// 从流程 JSON 文件加载流程图并预加载模型（model/*）。
    /// 返回：{code,message,models:[...]}（与 C# GraphExecutor.LoadModels 对齐）
    /// </summary>
    DLCV_INFER_CPP_DLL_API Json Load(const std::string& flowJsonPath, int deviceId = 0);

    /// <summary>
    /// 获取加载时保存的流程 JSON 根对象
    /// </summary>
    DLCV_INFER_CPP_DLL_API Json GetModelInfo() const;

    /// <summary>
    /// 对单张图片进行推理，返回 JSON 格式的结果数组：
    /// [
    ///   { ... 单个检测结果 ... },
    ///   ...
    /// ]
    /// </summary>
    DLCV_INFER_CPP_DLL_API Json InferOneOutJson(const cv::Mat& image, const Json& paramsJson = Json());

    /// <summary>
    /// 对多张图片进行推理，返回 { \"result_list\": ... }。
    /// 若 images.size()==1，则 result_list 为数组；否则为数组的数组。
    /// </summary>
    DLCV_INFER_CPP_DLL_API Json InferInternal(const std::vector<cv::Mat>& images, const Json& paramsJson = Json());

    /// <summary>
    /// 性能测试：返回平均耗时(ms)
    /// </summary>
    DLCV_INFER_CPP_DLL_API double Benchmark(const cv::Mat& image, int warmup = 1, int runs = 10);

private:
    std::vector<Json> _nodes;
    Json _root = Json::object();
    bool _loaded = false;
    // 是否由本对象负责在析构时触发全局释放（ModelPool 清理 + Utils::FreeAllModels）。
    // 注意：这是“全局释放”，若同进程中还有其它模型在用，不应让本对象过早析构。
    bool _ownsGlobalModels = false;
    int _deviceId = 0;
    std::string _flowJsonPath;

    void ReleaseNoexcept();
    Json LoadFromRoot(const Json& root, int deviceId);
};

} // namespace flow
} // namespace dlcv_infer

