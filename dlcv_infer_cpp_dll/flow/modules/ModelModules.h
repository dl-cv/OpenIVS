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

    std::shared_ptr<dlcv_infer::Model> Get(const std::string& modelPathUtf8, int deviceId);

    void Clear();

private:
    ModelPool() = default;
    std::mutex _mu;
    std::unordered_map<std::string, std::shared_ptr<dlcv_infer::Model>> _cache;
};

/// <summary>
/// 模型模块最小骨架：统一从输入 images 取 ModuleImage(Mat) 调用 dlcv_infer::Model。
/// </summary>
class BaseModelModule : public BaseModule {
protected:
    std::string _modelPathUtf8;
    int _deviceId = 0;
    std::shared_ptr<dlcv_infer::Model> _model;

public:
    BaseModelModule(int nodeId,
                    const std::string& title = std::string(),
                    const Json& properties = Json::object(),
                    ExecutionContext* context = nullptr)
        : BaseModule(nodeId, title, properties, context) {
        _modelPathUtf8 = ReadString("model_path", std::string());
        _deviceId = ReadInt("device_id", 0);
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

