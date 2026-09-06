#pragma once

#include <string>
#include <unordered_map>
#include <memory>
#include <typeinfo>

namespace dlcv_infer {
namespace flow {

/// <summary>
/// 轻量执行上下文：用于在流程执行期间传递共享参数与中间结果。
/// 设计目标：
/// - C++14 可用（不依赖 std::any/std::variant）
/// - 支持存放常用标量、nlohmann::json、cv::Mat 以及自定义类型
/// - Get<T> 在类型不匹配时返回默认值（不抛异常），便于流程容错
/// </summary>
class ExecutionContext final {
private:
    struct IValue {
        virtual ~IValue() = default;
        virtual const std::type_info& Type() const = 0;
        virtual std::shared_ptr<IValue> Clone() const = 0;
    };

    template <typename T>
    struct Value final : IValue {
        T V;
        explicit Value(const T& v) : V(v) {}
        explicit Value(T&& v) : V(std::move(v)) {}
        const std::type_info& Type() const override { return typeid(T); }
        std::shared_ptr<IValue> Clone() const override { return std::make_shared<Value<T>>(V); }
    };

    std::unordered_map<std::string, std::shared_ptr<IValue>> _map;

public:
    ExecutionContext() = default;
    ~ExecutionContext() = default;

    ExecutionContext(const ExecutionContext& other) {
        _map.reserve(other._map.size());
        for (const auto& kv : other._map) {
            _map.emplace(kv.first, kv.second ? kv.second->Clone() : nullptr);
        }
    }

    ExecutionContext& operator=(const ExecutionContext& other) {
        if (this == &other) return *this;
        _map.clear();
        _map.reserve(other._map.size());
        for (const auto& kv : other._map) {
            _map.emplace(kv.first, kv.second ? kv.second->Clone() : nullptr);
        }
        return *this;
    }

    ExecutionContext(ExecutionContext&&) noexcept = default;
    ExecutionContext& operator=(ExecutionContext&&) noexcept = default;

    bool Has(const std::string& key) const {
        return _map.find(key) != _map.end();
    }

    void Remove(const std::string& key) {
        _map.erase(key);
    }

    void Clear() {
        _map.clear();
    }

    template <typename T>
    void Set(const std::string& key, T value) {
        _map[key] = std::make_shared<Value<T>>(std::move(value));
    }

    template <typename T>
    T Get(const std::string& key, const T& defaultValue = T()) const {
        auto it = _map.find(key);
        if (it == _map.end() || !it->second) return defaultValue;
        auto pv = std::dynamic_pointer_cast<Value<T>>(it->second);
        if (!pv) return defaultValue;
        return pv->V;
    }
};

} // namespace flow
} // namespace dlcv_infer

