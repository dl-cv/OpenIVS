# C++ API 接口文档

**文档定位**：记录 `dlcv_infer_cpp_dll` 的精确函数签名、数据结构与调用流程。所有内容以当前源码实现为准。

---

## 1. 头文件与命名空间

| 头文件 | 说明 |
|--------|------|
| `dlcv_infer.h` | 主接口，包含 `Model`、`SlidingWindowModel`、`Utils`、`DllLoader`、`GetAllDogInfo` |
| `dlcv_sntl_admin.h` | 加密狗工具，包含 `sntl_admin::DogUtils`、`sntl_admin::DogProvider` |
| `flow/FlowGraphModel.h` | 流程图模型，包含 `dlcv_infer::flow::FlowGraphModel` |

命名空间层级：
- `dlcv_infer`：主命名空间
- `dlcv_infer::flow`：流程图（Flow）子命名空间
- `sntl_admin`：加密狗管理命名空间

---

## 2. 核心类与数据结构

### 2.1 结果数据结构

```cpp
namespace dlcv_infer {

struct ObjectResult {
    int categoryId;               // 类别 ID
    std::string categoryName;     // 类别名称（GBK 编码）
    float score;                  // 置信度
    float area;                   // 面积
    std::vector<double> bbox;     // bbox：水平框为 [x, y, w, h]，旋转框为 [cx, cy, w, h]
    bool withMask;                // 是否含 mask
    cv::Mat mask;                 // mask 图像（CV_8UC1）
    bool withBbox;                // 是否含 bbox
    bool withAngle;               // 是否含旋转角度
    float angle;                  // 旋转角度（弧度），-100 表示无效

    ObjectResult(int categoryId, const std::string& categoryName, float score,
                 float area, const std::vector<double>& bbox, bool withMask,
                 const cv::Mat& mask, bool withBbox, bool withAngle, float angle);
};

struct SampleResult {
    std::vector<ObjectResult> results;
    explicit SampleResult(std::vector<ObjectResult> results = {});
};

struct Result {
    std::vector<SampleResult> sampleResults;
    explicit Result(std::vector<SampleResult> sampleResults = {});
};

struct FlowNodeTiming {
    int nodeId = -1;              // 节点 ID
    std::string nodeType;         // 节点类型
    std::string nodeTitle;        // 节点标题
    double elapsedMs = 0.0;       // 耗时（毫秒）
};

} // namespace dlcv_infer
```

**字段约束**：
- `categoryName` 内部存储为 GBK 编码，便于 Windows UI 直接显示。
- `bbox` 长度约定：水平框 ≥4（`x,y,w,h`），旋转框 ≥4（`cx,cy,w,h`，`angle` 单独字段）。
- `angle` 有效值范围：`> -99.0f` 视为有效；`-100.0f` 视为无效。

### 2.2 流程图相关数据结构

```cpp
namespace dlcv_infer::flow {

struct FlowResultItem {
    int categoryId;
    std::string categoryName;
    float score;
    float area;
    std::vector<double> bbox;
    bool withMask;
    cv::Mat mask;
    bool withBbox;
    bool withAngle;
    float angle;

    static FlowResultItem FromJson(const json& j);
};

struct FlowBatchResult {
    std::vector<std::vector<FlowResultItem>> PerImageResults;
};

} // namespace dlcv_infer::flow
```

---

## 3. DllLoader（DLL 加载器）

```cpp
class DllLoader {
public:
    // 获取全局单例；首次调用自动检测加密狗类型并加载对应 DLL
    static DllLoader& Instance();

    // 根据模型头中的 dog_provider 字段，确保加载正确的 DLL
    static void EnsureForModel(const std::string& modelPath);
    static void EnsureForModel(const std::wstring& modelPath);

    sntl_admin::DogProvider GetDogProvider() const;
    std::string GetLoadedNativeDllName() const;

    // 底层 C 函数代理（通过 GetProcAddress 获取）
    LoadModelFuncType      GetLoadModelFunc();
    FreeModelFuncType      GetFreeModelFunc();
    GetModelInfoFuncType   GetModelInfoFunc();
    InferFuncType          GetInferFunc();
    FreeModelResultFuncType GetFreeModelResultFunc();
    FreeResultFuncType     GetFreeResultFunc();
    FreeAllModelsFuncType  GetFreeAllModelsFunc();
    GetDeviceInfoFuncType  GetDeviceInfoFunc();
    KeepMaxClockFuncType   GetKeepMaxClockFunc();

private:
    sntl_admin::DogProvider dogProvider;
    std::string dllName;
    std::string dllPath;
    void* hModule;
    static DllLoader* instance;

    DllLoader(sntl_admin::DogProvider provider);
    void LoadDll();
    static sntl_admin::DogProvider AutoDetectProvider();
};
```

**DLL 映射**：

| 加密狗类型 | DLL 名称 | 路径 |
|-----------|---------|------|
| Sentinel | `dlcv_infer.dll` | `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll` |
| Virbox | `dlcv_infer_v.dll` | `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer_v.dll` |

**自动检测优先级**：先检测 Sentinel，再检测 Virbox；均未检测到则回退到 Sentinel。

---

## 4. Model（模型推理类）

### 4.1 构造与析构

```cpp
class Model {
public:
    Model();                                      // 默认构造（空模型）
    Model(const std::string& modelPath, int device_id = 0);   // UTF-8 路径
    Model(const std::wstring& modelPath, int device_id = 0);  // 宽字符路径
    ~Model();

    // 移动语义（支持移动构造和移动赋值）
    Model(Model&& other) noexcept;
    Model& operator=(Model&& other) noexcept;

    // 禁止拷贝
    Model(const Model&) = delete;
    Model& operator=(const Model&) = delete;
};
```

**构造函数行为**：
1. 若路径以 `.dvst` / `.dvso` / `.dvsp` 结尾 → 进入 Flow/DVS 模式，解包归档并加载流程图。
2. 否则 → 普通模型模式，通过 `DllLoader` 调用底层 `dlcv_load_model`。
3. 构造失败时抛出 `std::runtime_error`，错误信息包含底层返回的 JSON。

### 4.2 模型信息

```cpp
json GetModelInfo();
```
- 返回模型元信息 JSON（包含 `model_info`、`input_shapes`、`dog_provider`、`loaded_model_meta` 等）。
- Flow 模式下调用 `_flowModel->GetModelInfo()`。
- 普通模式下通过 `dlcv_get_model_info` 获取。

### 4.3 单图推理

```cpp
Result Infer(const cv::Mat& image, const json& params_json = json::object());
```
- `image`：输入图像，调用层需确保为 RGB 格式（8UC3）。
- `params_json`：可选推理参数，常见字段见下表。
- 内部调用 `prepareInferInputBatch` 对图像做通道/位深归一化。
- 返回 `Result` 结构，包含 `sampleResults` 数组。

### 4.4 批量推理

```cpp
Result InferBatch(const std::vector<cv::Mat>& image_list, const json& params_json = json::object());
```
- `image_list`：输入图像列表，长度即 batch size。
- 返回结果中 `sampleResults` 长度与输入图像数量一致（Batch=1 时也为 1 个元素）。

### 4.5 JSON 单图输出

```cpp
json InferOneOutJson(const cv::Mat& image, const json& params_json = json::object());
```
- 返回 JSON 数组，每个元素为单个检测结果对象。
- 字段包含：`category_id`、`category_name`、`score`、`bbox`（`[x,y,w,h]`）、`with_bbox`、`with_angle`、`angle`、`mask`（点数组）、`with_mask`、`area`。
- 普通模式下将底层返回的 `mask_ptr` mask 转换为点数组形式。

### 4.6 释放模型

```cpp
void FreeModel();
```
- Flow 模式：删除 `_flowModel`。
- 普通模式且 `OwnModelIndex == true`：调用 `dlcv_free_model`。
- 普通模式且 `OwnModelIndex == false`：仅标记 `modelIndex = -1`，不释放底层模型。

### 4.7 计时查询

```cpp
static void GetLastInferTiming(double& dlcvInferMs, double& totalInferMs);
static std::vector<FlowNodeTiming> GetLastFlowNodeTimings();
```
- `dlcvInferMs`：SDK 核心推理耗时。
- `totalInferMs`：流程图总耗时（含前后处理）。
- `FlowNodeTimings`：流程图各节点耗时列表（仅 Flow 模式有效）。
- 数据存储在线程局部变量中，多线程场景下每个线程独立。

---

## 5. SlidingWindowModel（滑动窗口模型）

```cpp
class SlidingWindowModel : public Model {
public:
    SlidingWindowModel(
        const std::string& modelPath,
        int device_id,
        int small_img_width,       // 切片宽度
        int small_img_height,      // 切片高度
        int horizontal_overlap,    // 水平重叠像素
        int vertical_overlap,      // 垂直重叠像素
        float threshold,           // 置信度阈值
        float iou_threshold,       // NMS IoU 阈值
        float combine_ios_threshold  // 合并 IoS 阈值
    );
};
```

- 内部通过 `type = "sliding_window_pipeline"` 调用 `dlcv_load_model`。
- 继承 `Model` 的全部推理接口（`Infer`、`InferBatch`、`InferOneOutJson` 等）。

---

## 6. Utils（工具类）

### 6.1 模型管理

```cpp
static void Utils::FreeAllModels();
```
- 调用底层 `dlcv_free_all_models`，释放当前进程加载的所有模型。

### 6.2 设备信息

```cpp
static json Utils::GetDeviceInfo();
```
- 调用底层 `dlcv_get_device_info`。
- 若函数不可用，返回 `{"code": -1, "message": "dlcv_get_device_info 不可用"}`。

### 6.3 GPU 信息

```cpp
static json Utils::GetGpuInfo();
```
- 通过 NVML 动态加载 `nvml.dll` 获取 GPU 列表。
- 返回格式：`{"code": 0, "devices": [{"device_id": 0, "device_name": "..."}, ...]}`。
- NVML 初始化失败时返回包含错误码和消息的 JSON。

### 6.4 锁定最大时钟

```cpp
static void Utils::KeepMaxClock();
```
- 调用底层 `dlcv_keep_max_clock`，建议推理前执行以稳定 GPU 频率。

### 6.5 OCR 推理

```cpp
static Result Utils::OcrInfer(Model& detectModel, Model& recognizeModel, const cv::Mat& image);
```
- 先用 `detectModel` 检测文本区域，再用 `recognizeModel` 识别每个 ROI。
- 识别结果写入原检测结果的 `categoryName` 字段。

### 6.6 JSON 格式化

```cpp
static std::string Utils::JsonToString(const json& j);
```
- 缩进为 4 的格式化 JSON 字符串。

---

## 7. FlowGraphModel（流程图模型）

```cpp
namespace dlcv_infer::flow {

class FlowGraphModel {
public:
    FlowGraphModel();

    // 从 JSON 文件加载流程图
    json Load(const std::string& flowJsonPath, int deviceId = 0);

    // 内部推理，返回 JSON 根对象和结果指针
    std::pair<json, void*> InferInternal(const std::vector<cv::Mat>& images, const json& params_json);

    // 获取模型信息（含 nodes、loaded_model_meta、model_info 等）
    json GetModelInfo();

    // 获取已加载模型的元信息
    json GetLoadedModelMeta();
};

} // namespace dlcv_infer::flow
```

**`Load` 行为**：
1. 解析流程 JSON 的 `nodes` 数组。
2. 通过 `GraphExecutor` 加载每个节点引用的模型。
3. 返回加载报告：`{"code": 0, "models": [{"model_path": "...", "status_code": 0, ...}]}`。
4. 若任一模型加载失败，`code != 0`，并提取第一个失败模型的错误信息。

**`InferInternal` 行为**：
1. 将输入图像放入 `ExecutionContext`（键：`frontend_image_mat`、`frontend_image_mats`、`frontend_image_mat_list`）。
2. 执行 `GraphExecutor::Run()`。
3. 从 `frontend_json` / `frontend_json_by_node` 收集各节点输出。
4. 按 `origin_index` 或位置索引映射回原始图像结果。
5. 返回 `{"result_list": [...]}` 格式 JSON。

---

## 8. 加密狗查询

```cpp
json dlcv_infer::GetAllDogInfo();
```
- 返回 `{"sentinel": {...}, "virbox": {...}}`。
- 每个子对象包含 `devices`（设备列表）和 `features`（特性列表）。

```cpp
// sntl_admin::DogProvider 枚举
enum class DogProvider { Unknown, Sentinel, Virbox };

// sntl_admin::DogUtils 静态方法
static DogInfo GetSentinelInfo();
static DogInfo GetVirboxInfo();
static json GetAllDogInfo();
```

---

## 9. 字符串编码转换工具

```cpp
namespace dlcv_infer {

std::wstring convertStringToWstring(const std::string& inputString);   // ANSI → Wide
std::string  convertWstringToString(const std::wstring& inputWstring); // Wide → ANSI
std::string  convertWstringToUtf8(const std::wstring& inputWstring);   // Wide → UTF-8
std::wstring convertUtf8ToWstring(const std::string& inputUtf8);     // UTF-8 → Wide
std::string  convertWstringToGbk(const std::wstring& inputWstring);   // Wide → GBK
std::wstring convertGbkToWstring(const std::string& inputGbk);       // GBK → Wide
std::string  convertUtf8ToGbk(const std::string& inputUtf8);         // UTF-8 → GBK
std::string  convertGbkToUtf8(const std::string& inputGbk);         // GBK → UTF-8

} // namespace dlcv_infer
```

---

## 10. 图像输入预处理

```cpp
namespace dlcv_infer::image_input {

// 将输入图像归一化为模型期望的通道数和位深（CV_8U）
// 调用层已负责 BGR→RGB 转换；此函数负责 1/3/4 通道的补齐或压缩。
cv::Mat NormalizeInferInputImage(const cv::Mat& src, int expectedChannels);

} // namespace dlcv_infer::image_input
```

---

## 11. 调用流程

### 11.1 普通模型

```cpp
#include "dlcv_infer.h"

// 1. 加载
dlcv_infer::Model model("C:/models/my_model.dvt", 0);  // device_id=0

// 2. 推理
cv::Mat image = cv::imread("test.jpg", cv::IMREAD_UNCHANGED);
cv::Mat rgb;
cv::cvtColor(image, rgb, cv::COLOR_BGR2RGB);

nlohmann::json params;
params["threshold"] = 0.5;
params["with_mask"] = true;
params["batch_size"] = 1;

dlcv_infer::Result result = model.Infer(rgb, params);

// 3. 解析结果
for (const auto& sample : result.sampleResults) {
    for (const auto& obj : sample.results) {
        std::cout << obj.categoryName << " score=" << obj.score << std::endl;
    }
}

// 4. 释放（析构自动调用，或显式调用）
model.FreeModel();
dlcv_infer::Utils::FreeAllModels();  // 释放所有模型
```

### 11.2 流程图/DVS 模型

```cpp
// 1. 加载（.dvst / .dvso / .dvsp）
dlcv_infer::Model model("C:/models/pipeline.dvst", 0);

// 2. 推理（与普通模型接口完全一致）
dlcv_infer::Result result = model.Infer(rgb, params);

// 3. 获取计时（Flow 模式会返回节点级耗时）
double sdkMs, totalMs;
dlcv_infer::Model::GetLastInferTiming(sdkMs, totalMs);
auto nodes = dlcv_infer::Model::GetLastFlowNodeTimings();
```

---

## 12. 推理参数 JSON 字段

| 字段名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `threshold` | float | 0.5 | 置信度阈值 |
| `with_mask` | bool | true | 是否输出 mask |
| `batch_size` | int | 1 | 批量大小 |
| `device_id` | int | 构造时传入 | GPU 设备 ID（-1 表示 CPU） |

---

## 13. 错误处理约定

- 所有错误通过 C++ 异常抛出（`std::runtime_error`、`std::invalid_argument` 等）。
- 底层 C API 返回的错误码封装在异常消息中。
- Flow 模式加载失败时，异常信息包含第一个失败模型的路径和底层错误。
- DVS 解包失败时抛出 `std::runtime_error`，包含具体错误步骤（如 "invalid dvst format"、"pipeline.json not found"）。

---

*本文档只记录当前源码实现。如需了解编译、运行或测试程序，参见对应测试程序文档。*
