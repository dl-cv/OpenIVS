#pragma once

#include <map>
#include <string>
#include <vector>

#include "flow/ExecutionContext.h"
#include "flow/FlowTypes.h"

namespace dlcv_infer {
namespace flow {

/// <summary>
/// 模块基类：统一签名 Process(image_list, result_list) -> ModuleIO
/// 约定：
/// - image_list 以 ModuleImage 承载（含 TransformState 与 OriginalImage）
/// - result_list / template_list 用 nlohmann::json 数组承载（保持通用、便于扩展）
/// - ExtraInputsIn / ExtraOutputs 由 GraphExecutor 填充
/// - ScalarInputs/Outputs 用于标量端口（bool/int/string等），GraphExecutor 负责注入与导出
/// </summary>
class BaseModule {
public:
    int NodeId = 0;
    std::string Title;
    Json Properties = Json::object();
    ExecutionContext* Context = nullptr;

    // 额外输入/输出对，索引与执行器的“扩展端口对”对应
    std::vector<ModuleChannel> ExtraInputsIn;
    std::vector<ModuleChannel> ExtraOutputs;

    // 主对模版输入（JSON 数组）
    Json MainTemplateList = Json::array();

    // 标量输入/输出（按索引与名称）
    std::map<int, Json> ScalarInputsByIndex;
    std::map<std::string, Json> ScalarInputsByName;
    std::map<std::string, Json> ScalarOutputsByName;

    BaseModule(int nodeId,
               std::string title = std::string(),
               Json properties = Json::object(),
               ExecutionContext* context = nullptr)
        : NodeId(nodeId),
          Title(std::move(title)),
          Properties(std::move(properties)),
          Context(context) {}

    virtual ~BaseModule() = default;

    /// <summary>
    /// 可选：模型预加载。默认不做任何事；model/* 模块可覆写以提前加载模型。
    /// </summary>
    virtual void LoadModel() {}

    /// <summary>
    /// 默认透传：不修改图像与结果，模板输出为空数组。
    /// </summary>
    virtual ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) {
        return ModuleIO(imageList, resultList, Json::array());
    }

protected:
    // ---- Properties helpers (best-effort) ----
    std::string ReadString(const std::string& key, const std::string& dv) const {
        try {
            if (Properties.is_object() && Properties.contains(key)) {
                const auto& v = Properties.at(key);
                if (v.is_string()) {
                    const std::string s = v.get<std::string>();
                    return s.empty() ? dv : s;
                }
                if (!v.is_null()) return v.dump();
            }
        } catch (...) {}
        return dv;
    }

    int ReadInt(const std::string& key, int dv) const {
        try {
            if (Properties.is_object() && Properties.contains(key)) {
                const auto& v = Properties.at(key);
                if (v.is_number_integer()) return v.get<int>();
                if (v.is_number()) return static_cast<int>(std::llround(v.get<double>()));
                if (v.is_string()) return std::stoi(v.get<std::string>());
            }
        } catch (...) {}
        return dv;
    }

    double ReadDouble(const std::string& key, double dv) const {
        try {
            if (Properties.is_object() && Properties.contains(key)) {
                const auto& v = Properties.at(key);
                if (v.is_number()) return v.get<double>();
                if (v.is_string()) return std::stod(v.get<std::string>());
            }
        } catch (...) {}
        return dv;
    }

    bool ReadBool(const std::string& key, bool dv) const {
        try {
            if (Properties.is_object() && Properties.contains(key)) {
                const auto& v = Properties.at(key);
                if (v.is_boolean()) return v.get<bool>();
                if (v.is_number_integer()) return v.get<int>() != 0;
                if (v.is_string()) {
                    const auto s = v.get<std::string>();
                    if (s == "1" || s == "true" || s == "True" || s == "TRUE") return true;
                    if (s == "0" || s == "false" || s == "False" || s == "FALSE") return false;
                }
            }
        } catch (...) {}
        return dv;
    }
};

/// <summary>
/// 输入模块基类：忽略输入，由 Generate 产生首对输出
/// </summary>
class BaseInputModule : public BaseModule {
public:
    using BaseModule::BaseModule;
    virtual ~BaseInputModule() = default;

    virtual ModuleIO Generate() {
        return ModuleIO(std::vector<ModuleImage>(), Json::array(), Json::array());
    }

    ModuleIO Process(const std::vector<ModuleImage>& /*imageList*/, const Json& /*resultList*/) override {
        return Generate();
    }
};

} // namespace flow
} // namespace dlcv_infer

