#include "flow/FlowGraphModel.h"
#include "flow/modules/ModelModules.h"

#include <chrono>
#include <fstream>
#include <stdexcept>

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

    Json merged = Json::array();
    for (size_t i = 0; i < images.size(); i++) {
        const cv::Mat& img = images[i];
        if (img.empty()) {
            merged.push_back(Json::array());
            continue;
        }

        // 约定：C++ 侧使用 OpenCV 默认 BGR，不做强制转换
        cv::Mat bgrMat = img;

        ExecutionContext ctx;
        ctx.Set<cv::Mat>("frontend_image_mat", bgrMat);
        ctx.Set<std::string>("frontend_image_path", std::string());
        ctx.Set<int>("device_id", _deviceId);

        GraphExecutor exec(_nodes, &ctx);
        (void)exec.Run();

        Json resultList = Json::array();
        Json feJson = ctx.Get<Json>("frontend_json", Json());
        try {
            if (feJson.is_object() && feJson.contains("last")) {
                const auto& lastPayload = feJson.at("last");
                if (lastPayload.is_object() && lastPayload.contains("by_image") && lastPayload.at("by_image").is_array()) {
                    for (const auto& item : lastPayload.at("by_image")) {
                        if (!item.is_object()) continue;
                        if (!item.contains("results")) continue;
                        const auto& resultsObj = item.at("results");
                        if (resultsObj.is_array()) {
                            for (const auto& r : resultsObj) resultList.push_back(r);
                        } else if (!resultsObj.is_null()) {
                            // 兜底：非数组当作单个结果
                            resultList.push_back(resultsObj);
                        }
                    }
                }
            }
        } catch (...) {
            // ignore
        }

        merged.push_back(resultList);
    }

    Json out = Json::object();
    if (images.size() == 1) {
        out["result_list"] = merged.size() > 0 ? merged.at(0) : Json::array();
    } else {
        out["result_list"] = merged;
    }
    (void)paramsJson; // 预留：未来可将 paramsJson 注入 ctx 或模块属性覆盖
    return out;
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

