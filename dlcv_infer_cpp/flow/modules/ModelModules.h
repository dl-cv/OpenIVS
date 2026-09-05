#pragma once

#include <cstddef>
#include <cstdint>
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

class DLCV_INFER_CPP_API ModelLifecycleReadGuard final {
public:
    ModelLifecycleReadGuard();
    ~ModelLifecycleReadGuard();
    ModelLifecycleReadGuard(const ModelLifecycleReadGuard&) = delete;
    ModelLifecycleReadGuard& operator=(const ModelLifecycleReadGuard&) = delete;

private:
    bool _ownsSharedLock = false;
};

class DLCV_INFER_CPP_API ModelLifecycleWriteGuard final {
public:
    ModelLifecycleWriteGuard();
    ~ModelLifecycleWriteGuard();
    ModelLifecycleWriteGuard(const ModelLifecycleWriteGuard&) = delete;
    ModelLifecycleWriteGuard& operator=(const ModelLifecycleWriteGuard&) = delete;

private:
    bool _ownsExclusiveLock = false;
};

struct ModelBinaryStore final {
    uint64_t StoreId = 0;
    std::unordered_map<std::string, std::shared_ptr<const std::vector<unsigned char>>> Buffers;
    std::unordered_map<std::string, std::string> Aliases;
};

class ModelPool;

class ModelPoolLease final {
public:
    ModelPoolLease() = default;
    DLCV_INFER_CPP_API ~ModelPoolLease();
    DLCV_INFER_CPP_API ModelPoolLease(ModelPoolLease&& other) noexcept;
    DLCV_INFER_CPP_API ModelPoolLease& operator=(ModelPoolLease&& other) noexcept;
    ModelPoolLease(const ModelPoolLease&) = delete;
    ModelPoolLease& operator=(const ModelPoolLease&) = delete;

    DLCV_INFER_CPP_API void Reset() noexcept;
    const std::shared_ptr<dlcv_infer::Model>& Model() const { return _model; }
    const std::string& Key() const { return _key; }
    explicit operator bool() const { return _model != nullptr; }

private:
    friend class ModelPool;
    DLCV_INFER_CPP_API ModelPoolLease(
        std::shared_ptr<dlcv_infer::Model> model,
        std::string key,
        std::uint64_t entryIdentity);

    std::shared_ptr<dlcv_infer::Model> _model;
    std::string _key;
    std::uint64_t _entryIdentity = 0;
};

struct ModelPoolStats {
    size_t totalEntries = 0;
    size_t activeEntries = 0;
    size_t idleEntries = 0;
};

DLCV_INFER_CPP_API ModelPoolStats GetModelPoolStats();

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
    ModelPoolLease Acquire(
        const std::string& modelPathUtf8,
        int deviceId);

    ModelPoolLease AcquireBinary(
        const std::shared_ptr<const ModelBinaryStore>& store,
        const std::string& bufferKey,
        const std::string& modelName,
        int deviceId);
    ModelPoolLease RetainByKey(const std::string& key);
    static std::string MakeBinaryKey(uint64_t storeId, const std::string& bufferKey, int deviceId);

    void Clear();
    ModelPoolStats GetStats();

    static std::string MakeKey(const std::string& modelIdentityUtf8, int deviceId);

private:
    struct Entry {
        std::shared_ptr<dlcv_infer::Model> model;
        int refCount = 0;
        std::uint64_t identity = 0;
    };

    ModelPool() = default;
    friend class ModelPoolLease;
    void ReleaseByKey(const std::string& key, std::uint64_t entryIdentity);

    std::mutex _mu;
    std::unordered_map<std::string, Entry> _cache;
    std::uint64_t _entrySequence = 0;
};

/// <summary>
/// 模型模块最小骨架：统一从输入 images 取 ModuleImage(Mat) 调用 dlcv_infer::Model。
/// </summary>
class BaseModelModule : public BaseModule {
protected:
    std::string _modelPathUtf8;
    int _deviceId = 0;
    int _resolvedDeviceId = 0;
    std::string _modelBufferKey;
    ModelPoolLease _modelLease;

public:
    BaseModelModule(int nodeId,
                    const std::string& title = std::string(),
                    const Json& properties = Json::object(),
                    ExecutionContext* context = nullptr)
        : BaseModule(nodeId, title, properties, context) {
        _modelPathUtf8 = ReadString("model_path", std::string());
        _modelBufferKey = ReadString("model_buffer_key", std::string());
        _deviceId = ReadInt("device_id", 0);
        _resolvedDeviceId = _deviceId;
    }

    ~BaseModelModule() = default;

    void LoadModel() override;

    const std::string& ModelPathUtf8() const { return _modelPathUtf8; }
    const std::string& ModelPoolKey() const { return _modelLease.Key(); }
    int ResolvedDeviceId() const { return _resolvedDeviceId; }
    const std::shared_ptr<dlcv_infer::Model>& LoadedModel() const { return _modelLease.Model(); }
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

