#pragma once

#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>

#include "dlcv_infer.h"
#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include "flow/utils/MaskRleUtils.h"

namespace dlcv_infer {
namespace flow {

/// <summary>
/// 模型池：按 model_path+device_id 缓存 dlcv_infer::Model，避免重复加载。
/// 约定：FlowGraph 内部字符串使用 UTF-8；创建 Model 时会转换为 GBK 以兼容现有 Model 构造。
/// </summary>
class ModelPool final {
public:
    static ModelPool& Instance();

    /// 获取模型，增加该 key 的引用计数。
    /// 若缓存中已存在且有效，直接返回并 +1。
    /// 若不存在，创建新的 Model 对象，refCount = 1。
    std::shared_ptr<dlcv_infer::Model> Acquire(
        const std::string& modelPathUtf8, int deviceId);

    /// 释放一个引用。refCount 减 1；归零时从缓存移除。
    /// 若 key 不存在，无操作。
    void Release(const std::string& modelPathUtf8, int deviceId);

    /// 通过 key 释放引用（避免调用方重复拼接/解析）。
    void ReleaseByKey(const std::string& key);

    [[deprecated("Use Acquire/Release instead")]]
    void Clear();

    static std::string MakeKey(const std::string& modelPathUtf8, int deviceId);

private:
    struct Entry {
        std::shared_ptr<dlcv_infer::Model> model;
        int refCount = 0;
    };

    ModelPool() = default;
    std::mutex _mu;
    std::unordered_map<std::string, Entry> _cache;
};

/// <summary>
/// 模型模块最小骨架：统一从输入 images 取 ModuleImage(Mat) 调用 dlcv_infer::Model。
/// </summary>
class BaseModelModule : public BaseModule {
protected:
    std::string _modelPathUtf8;
    int _deviceId = 0;
    int _resolvedDeviceId = 0;
    std::shared_ptr<dlcv_infer::Model> _model;

public:
    BaseModelModule(int nodeId,
                    const std::string& title = std::string(),
                    const Json& properties = Json::object(),
                    ExecutionContext* context = nullptr)
        : BaseModule(nodeId, title, properties, context) {
        _modelPathUtf8 = ReadString("model_path", std::string());
        _deviceId = ReadInt("device_id", 0);
        _resolvedDeviceId = _deviceId;
    }

    ~BaseModelModule() {
        if (!_modelPathUtf8.empty() && _model) {
            try { ModelPool::Instance().Release(_modelPathUtf8, _resolvedDeviceId); } catch (...) {}
        }
    }

    void LoadModel() override;
};

/// <summary>
/// model/det：检测/旋转框检测/实例分割等均可通过参数配置；此处做通用直通调用。
/// </summary>
class DetModelModule : public BaseModelModule {
public:
    using BaseModelModule::BaseModelModule;
    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override;
};

class RotatedBBoxModelModule : public DetModelModule { public: using DetModelModule::DetModelModule; };
class InstanceSegModelModule : public DetModelModule { public: using DetModelModule::DetModelModule; };
class SemanticSegModelModule : public DetModelModule { public: using DetModelModule::DetModelModule; };

/// <summary>
/// 分类模型：复用 det 骨架，并确保 bbox 至少覆盖整图（对齐 C# ClsModel）。
/// </summary>
class ClsModelModule : public DetModelModule {
public:
    using DetModelModule::DetModelModule;
    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override;
};

/// <summary>
/// OCR 模型：复用 det 骨架，并确保 bbox 至少覆盖整图（对齐 C# OCRModel）。
/// </summary>
class OcrModelModule : public DetModelModule {
public:
    using DetModelModule::DetModelModule;
    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override;
};

} // namespace flow
} // namespace dlcv_infer

