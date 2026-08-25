#pragma once

#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>

#include "flow/BaseModule.h"

namespace dlcv_infer {
namespace flow {

/// <summary>
/// 模块注册表：按模块类型字符串获取对应的实现工厂
/// </summary>
class ModuleRegistry final {
public:
    using Factory = std::function<std::unique_ptr<BaseModule>(int /*nodeId*/,
                                                              const std::string& /*title*/,
                                                              const Json& /*properties*/,
                                                              ExecutionContext* /*context*/)>; 

    static void Register(const std::string& moduleType, Factory factory) {
        if (moduleType.empty()) return;
        auto& map = RegistryMap();
        std::lock_guard<std::mutex> lk(RegistryMutex());
        map[moduleType] = std::move(factory);
    }

    static Factory Get(const std::string& moduleType) {
        if (moduleType.empty()) return Factory();
        auto& map = RegistryMap();
        std::lock_guard<std::mutex> lk(RegistryMutex());
        auto it = map.find(moduleType);
        if (it == map.end()) return Factory();
        return it->second;
    }

    static bool Has(const std::string& moduleType) {
        if (moduleType.empty()) return false;
        auto& map = RegistryMap();
        std::lock_guard<std::mutex> lk(RegistryMutex());
        return map.find(moduleType) != map.end();
    }

private:
    static std::unordered_map<std::string, Factory>& RegistryMap() {
        static std::unordered_map<std::string, Factory> s_map;
        return s_map;
    }

    static std::mutex& RegistryMutex() {
        static std::mutex s_mu;
        return s_mu;
    }
};

/// <summary>
/// 注册宏：在静态初始化阶段自动注册模块。
/// </summary>
#define DLCV_FLOW_CONCAT_INNER(a, b) a##b
#define DLCV_FLOW_CONCAT(a, b) DLCV_FLOW_CONCAT_INNER(a, b)
// 说明：
// - 同一个 MODULE_CLASS 可能需要注册多个别名（例如 pre_process/sliding_window 与 features/sliding_window）。
// - 因此注册器类型名必须“每次调用都唯一”，这里用 __LINE__ 生成唯一标识，避免重定义。
#define DLCV_FLOW_REGISTER_MODULE(MODULE_TYPE_STR, MODULE_CLASS)                                \
    namespace {                                                                                 \
        struct DLCV_FLOW_CONCAT(DlcvFlowReg_, __LINE__) {                                       \
            DLCV_FLOW_CONCAT(DlcvFlowReg_, __LINE__)() {                                        \
                ::dlcv_infer::flow::ModuleRegistry::Register(                                   \
                    (MODULE_TYPE_STR),                                                          \
                    [] (int nodeId, const std::string& title, const ::dlcv_infer::flow::Json& props, \
                        ::dlcv_infer::flow::ExecutionContext* ctx) -> std::unique_ptr<::dlcv_infer::flow::BaseModule> { \
                        return std::unique_ptr<::dlcv_infer::flow::BaseModule>(                 \
                            new MODULE_CLASS(nodeId, title, props, ctx));                       \
                    }                                                                            \
                );                                                                               \
            }                                                                                    \
        };                                                                                       \
        static DLCV_FLOW_CONCAT(DlcvFlowReg_, __LINE__) DLCV_FLOW_CONCAT(g_DlcvFlowReg_, __LINE__); \
    }

} // namespace flow
} // namespace dlcv_infer

