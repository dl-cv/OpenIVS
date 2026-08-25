# C++ API 文档

**文档定位**：记录 `dlcv_infer_cpp_dll` 的精确函数签名、数据结构、工程结构、构建配置、依赖、编码路径规则与 C++ 对外接口。所有内容以当前源码实现为准。

---

## 1. 头文件与命名空间

| 头文件 | 说明 |
|--------|------|
| `dlcv_infer.h` | 主接口，包含 `Model`、`Utils`、`DllLoader`、`GetAllDogInfo` |
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
    bool withMean;                // 是否含前景与背景均值
    double foregroundMean;        // mask 前景区域的像素均值
    double backgroundMean;        // mask 背景区域的像素均值

    ObjectResult(int categoryId, const std::string& categoryName, float score,
                 float area, const std::vector<double>& bbox, bool withMask,
                 const cv::Mat& mask, bool withBbox, bool withAngle, float angle);
    ObjectResult(int categoryId, const std::string& categoryName, float score,
                 float area, const std::vector<double>& bbox, bool withMask,
                 const cv::Mat& mask, bool withBbox, bool withAngle, float angle,
                 bool withMean, double foregroundMean, double backgroundMean);
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
    // 获取全局单例；首次调用自动检测加密狗类型，有狗时加载对应 DLL，无狗时不加载
    static DllLoader& Instance();

    // 根据共享 index 返回所属 DLL，不修改全局单例
    static DllLoader& ResolveForIndex(int index, int& indexType);

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
| Unknown（无狗） | 不加载 | — |

**自动检测优先级**：先检测 Sentinel，再检测 Virbox；均未检测到则返回 `DogProvider::Unknown`，**不加载**任何推理 DLL，也不抛异常。真正加载模型时若仍无授权，再抛出 `未检测到授权`。

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
1. 若路径以 `.dvst` / `.dvso` 结尾 → 进入 Flow/DVS 模式，解包归档并加载流程图。
2. 若路径以 `.dvsp` 结尾 → 抛出 `std::invalid_argument`，不加载文件。
3. 否则 → 普通模型模式，通过 `DllLoader` 调用底层 `dlcv_load_model`。
4. 构造失败时抛出 `std::runtime_error`，错误信息包含底层返回的 JSON。

### 4.2 模型信息

```cpp
json GetModelInfo();
json GetDvsModelInfo();
```
- `GetModelInfo()` 对普通模型和流程模型返回相同层级。流程模型的输入通道和输入形状取首个模型，任务类型、类别列表和类别数量取最终输出可达的模型。
- `GetDvsModelInfo()` 支持流程模型，返回完整流程 JSON、`loaded_model_meta`、按模型文件名组织的 `model_info`，以及首模型和最终输出模型的节点编号。
- 普通模式下通过 `dlcv_get_model_info` 获取。
- 空对象设置有效的 `modelIndex` 后，首次调用按新版四段 index 规则选择对应 loader，并增加外部使用计数。普通模型读取共享模型信息；流程模型读取共享流程 JSON，以保存的 `pipeline` 为流程定义，按 `source_path` 解包归档资源，再按 `model_bindings` 为模型节点设置 `model_index`，随后创建本对象的 `FlowGraphModel`。该过程不查询加密狗，不访问另一 provider，也不修改 `DllLoader::Instance()`。
- 共享流程加载时为每个不同的子模型 index 创建一次借用 `Model` 并保存在 `FlowGraphModel`；后续推理中的模型节点直接复用这些已绑定对象，流程释放时统一解绑。

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
- 字段包含：`category_id`、`category_name`、`score`、`bbox`（`[x,y,w,h]`）、`with_bbox`、`with_angle`、`angle`、`mask`（点数组）、`with_mask`、`area`、`with_mean`、`foreground_mean`、`background_mean`。
- 普通模式下将底层返回的 `mask_ptr` mask 转换为点数组形式。

### 4.6 释放模型

```cpp
void FreeModel();
```
- Flow 模式且由当前对象注册：删除 `_flowModel` 后调用 `dlcv_free_flow_c`。
- Flow 模式且通过共享索引恢复：删除 `_flowModel` 后调用 `dlcv_unbind_index_c`。
- 普通模型且由当前对象加载：按现有 `dlcv_free_model` 释放规则处理。
- 通过共享 `modelIndex` 恢复的普通模型或流程模型：无论 `OwnModelIndex` 是否为 `false`，只减少当前对象增加的使用计数，不直接释放共享资源。

> **model_index 来源**：普通模型的 `modelIndex` 由底层 `dlcv_infer` 加载时返回。流程模型（`.dvst`/`.dvso`）先加载其中的子模型；推理 DLL提供完整共享接口时，再注册包含 `schema_version`、`flow_type`、`provider`、`source_path`、`device_id`、`pipeline`、`model_bindings` 的流程 JSON。旧 DLL缺少共享接口时使用本地流程 index，原有加载和推理行为保持不变。

| provider | 类型 | index 范围 |
|---|---|---|
| Sentinel | 普通模型 | `0～9999` |
| Sentinel | 流程 | `10000～19999` |
| Virbox | 普通模型 | `20000～29999` |
| Virbox | 流程 | `30000～39999` |

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

---

## 5. Utils（工具类）

### 5.1 模型管理

```cpp
static void Utils::FreeAllModels();
```
- 调用底层 `dlcv_free_all_models`，释放当前进程加载的所有模型。

### 5.2 设备信息

```cpp
static json Utils::GetDeviceInfo();
```
- 调用底层 `dlcv_get_device_info`。
- 若函数不可用，返回 `{"code": -1, "message": "dlcv_get_device_info 不可用"}`。

### 5.3 GPU 信息

```cpp
static json Utils::GetGpuInfo();
```
- 通过 NVML 动态加载 `nvml.dll` 获取 GPU 列表。
- 返回格式：`{"code": 0, "devices": [{"device_id": 0, "device_name": "..."}, ...]}`。
- NVML 初始化失败时返回包含错误码和消息的 JSON。

### 5.4 锁定最大时钟

```cpp
static void Utils::KeepMaxClock();
```
- 调用底层 `dlcv_keep_max_clock`，建议推理前执行以稳定 GPU 频率。

### 5.5 OCR 推理

```cpp
static Result Utils::OcrInfer(Model& detectModel, Model& recognizeModel, const cv::Mat& image);
```
- 先用 `detectModel` 检测文本区域，再用 `recognizeModel` 识别每个 ROI。
- 识别结果写入原检测结果的 `categoryName` 字段。

### 5.6 JSON 格式化

```cpp
static std::string Utils::JsonToString(const json& j);
```
- 缩进为 4 的格式化 JSON 字符串。

---

## 6. FlowGraphModel（流程图模型）

```cpp
namespace dlcv_infer::flow {

class FlowGraphModel {
public:
    FlowGraphModel();

    // 从 JSON 文件加载流程图
    json Load(const std::string& flowJsonPath, int deviceId = 0);

    // 内部推理，返回 JSON 根对象
    json InferInternal(const std::vector<cv::Mat>& images, const json& params_json);

    // 获取与普通模型相同结构的兼容模型信息
    json GetModelInfo() const;

    // 获取完整流程及全部子模型信息
    json GetDvsModelInfo() const;
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

## 7. 加密狗查询

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

## 8. 字符串编码转换工具

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

## 9. 图像输入预处理

```cpp
namespace dlcv_infer::image_input {

// 将输入图像归一化为模型期望的通道数和位深（CV_8U）
// 调用层已负责 BGR→RGB 转换；此函数负责 1/3/4 通道的补齐或压缩。
cv::Mat NormalizeInferInputImage(const cv::Mat& src, int expectedChannels);

} // namespace dlcv_infer::image_input
```

---

## 10. 调用流程

### 10.1 普通模型

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
params["calc_mean"] = false;
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

### 10.2 流程图/DVS 模型

```cpp
// 1. 加载（.dvst / .dvso）
dlcv_infer::Model model("C:/models/pipeline.dvst", 0);

// 2. 推理（与普通模型接口完全一致）
dlcv_infer::Result result = model.Infer(rgb, params);

// 3. 获取计时（Flow 模式会返回节点级耗时）
double sdkMs, totalMs;
dlcv_infer::Model::GetLastInferTiming(sdkMs, totalMs);
auto nodes = dlcv_infer::Model::GetLastFlowNodeTimings();
```

流程模型的 `threshold` 有明确的两层语义：流程内每个 `model/*` 节点始终使用流程文件中自身的 `threshold`；调用 `Infer` / `InferBatch` / `InferOneOutJson` 时传入的 `params["threshold"]` 只在流程执行和结果聚合完成后，对最终对外结果按 `score >= threshold` 进行筛选。该入口参数不会改写任何流程节点属性。

---

## 11. 推理参数 JSON 字段

| 字段名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `threshold` | float | 普通模型为 0.5；流程未传时不追加过滤 | 普通模型的推理阈值；流程模型中仅筛选最终对外结果，不覆盖节点自身阈值 |
| `with_mask` | bool | true | 是否输出 mask |
| `calc_mean` | bool | false | 是否计算实例分割目标的前景与背景均值 |
| `batch_size` | int | 1 | 批量大小 |
| `device_id` | int | 构造时传入 | GPU 设备 ID（-1 表示 CPU） |

---

## 12. 错误处理

- 所有错误通过 C++ 异常抛出（`std::runtime_error`、`std::invalid_argument` 等）。
- 底层 C API 返回的错误码封装在异常消息中。
- Flow 模式加载失败时，异常信息包含第一个失败模型的路径和底层错误。
- DVS 解包失败时抛出 `std::runtime_error`，包含具体错误步骤（如 "invalid dvst format"、"pipeline.json not found"）。

---

## 13. 项目范围

- 项目目录：`dlcv_infer_cpp_dll`
- 工程文件：`dlcv_infer_cpp_dll/dlcv_infer_cpp_dll.vcxproj`
- 工程类型：Windows 动态库
- 根命名空间：`dlcvinfercppdll`
- 主要命名空间：
  - `dlcv_infer`
  - `dlcv_infer::flow`
  - `sntl_admin`
- 对外头文件：
  - `dlcv_infer_cpp_dll/dlcv_infer.h`
  - `dlcv_infer_cpp_dll/flow/FlowGraphModel.h`
  - `dlcv_infer_cpp_dll/dlcv_sntl_admin.h`
- C API 项目目录：`dlcv_infer_c_dll`
- C API 工程文件：`dlcv_infer_c_dll/dlcv_infer_c_dll.vcxproj`
- C API 对外头文件：`dlcv_infer_c_dll/dlcv_infer_c_api.h`
- C API 工程通过 `dlcv_infer_cpp_dll.lib` 显式依赖 C++ API 工程。
- C API 保留 `dlcv_infer_cpp_infer_c` 默认参数入口，并提供 `dlcv_infer_cpp_infer_with_params_c` 接收 JSON 参数；调用端可传入 `threshold`、`calc_mean` 等字段覆盖本次推理参数。
- C API 为每个 `.dvst`、`.dvso` 模型句柄保存独立推理互斥量；同一句柄的多线程请求依次执行，保护首次模型信息读取、流程推理、结构化结果复制和结果释放。不同流程句柄仍可同时执行。
- `.dvt`、`.dvo` 普通模型不使用上述流程互斥量，保持原有并发调用方式。

---

## 14. 构建与输出

- 项目级构建与发布前构建验证统一通过 MCP 构建工具执行，解决方案级与项目级入口见 `开发文档.md` 的“统一编译说明”。
- 工程配置：
  - `Debug|Win32`
  - `Release|Win32`
  - `Debug|x64`
  - `Release|x64`
- 平台工具集：`v143`
- Windows SDK 版本：`10.0`
- 字符集：`Unicode`
- 预处理宏：
  - Win32 Debug：`WIN32;_DEBUG;_WINDOWS;_USRDLL;DLCV_INFER_CPP_DLL_EXPORTS`
  - Win32 Release：`WIN32;NDEBUG;_WINDOWS;_USRDLL;DLCV_INFER_CPP_DLL_EXPORTS`
  - x64 Debug：`_DEBUG;_WINDOWS;_USRDLL;DLCV_INFER_CPP_DLL_EXPORTS`
  - x64 Release：`NDEBUG;_WINDOWS;_USRDLL;DLCV_INFER_CPP_DLL_EXPORTS`
- 输出目录仅在 `x64` 配置中显式设置为 `$(SolutionDir)$(Configuration)\`。
- Debug x64 链接库：`opencv_world4100d.lib`
- Release x64 链接库：`opencv_world4100.lib`
- `dlcv_infer_c_dll` 工程配置为 `Debug|x64` 和 `Release|x64`，输出目录为 `$(SolutionDir)$(Configuration)\`。
- `dlcv_infer_c_dll` 编译时定义 `DLCV_INFER_C_DLL_EXPORTS`，链接 `dlcv_infer_cpp_dll.lib` 与对应配置的 OpenCV 库。

当前工程的编译单元按“入口绑定 -> Flow 执行框架 -> 节点实现”三层拆分：

| 分组 | 文件 | 当前职责 |
| --- | --- | --- |
| 入口与外部绑定 | `dlcv_infer.cpp` | `Model`、`Utils`、底层 `dlcv_infer.dll` 绑定、DVS 归档解包、普通模型与 Flow 结果转换 |
| 入口与外部绑定 | `dlcv_sntl_admin.cpp` | 加密狗管理 DLL 绑定、XML 转 JSON、设备与特性查询 |
| Flow 执行框架 | `flow/GraphExecutor.cpp` | 节点排序、链路路由、属性覆盖、标量端口注入、节点计时 |
| Flow 执行框架 | `flow/FlowGraphModel.cpp` | Flow JSON 加载、`model/*` 预加载、执行上下文初始化、前端结果聚合 |
| Flow 节点实现 | `flow/modules/InputModules.cpp` | 输入图像读取、前端图像接入、测试结果构造 |
| Flow 节点实现 | `flow/modules/ModelModules.cpp` | `model/*` 节点、模型池、批量推理调用 |
| Flow 节点实现 | `flow/modules/OutputModules.cpp` | 图片保存、预览透传、`return_json` 前端结果回写 |
| Flow 节点实现 | `flow/modules/SlidingModules.cpp` | 滑窗切图、滑窗结果回写与合并 |
| Flow 节点实现 | `flow/modules/FeatureModules.cpp` | 裁图、翻转、缩放、按分类旋转、标签拼接等通用图像/结果处理 |
| Flow 节点实现 | `flow/modules/PostProcessModules.cpp` | 结果合并、过滤、替换、覆盖、去重、mask/rbox 互转 |
| Flow 节点实现 | `flow/modules/RegionStrokeVisualizeTemplateModules.cpp` | 区域过滤、描边转点、可视化、模板生成/保存/加载/匹配 |

---

## 15. 构建期依赖解析

| 项目 | 当前解析顺序或检查规则 |
| --- | --- |
| OpenCV 头文件目录 | `OpenCV_INCLUDE_DIR` -> `OpenCV_DIR` -> `OpenCV_DIR\include` -> `OpenCV_DIR\..\..\include` -> `$(SolutionDir)third_party\opencv\build\include` -> `$(ProjectDir)..\third_party\opencv\build\include` -> `C:\OpenCV\build\include` |
| OpenCV 库目录 | `OpenCV_LIB_DIR` -> `OpenCV_DIR` -> `OpenCV_DIR\lib` -> `OpenCV_DIR\x64\vc16\lib` -> `$(SolutionDir)third_party\opencv\build\x64\vc16\lib` -> `$(ProjectDir)..\third_party\opencv\build\x64\vc16\lib` -> `C:\OpenCV\build\x64\vc16\lib` |
| DLCV SDK 头文件目录 | `DLCVPRO_INFER_INCLUDE` -> `$(SolutionDir)third_party\dlcvpro_infer\include` -> `$(ProjectDir)..\third_party\dlcvpro_infer\include` -> `C:\dlcv\Lib\site-packages\dlcvpro_infer\include` |
| `ValidatePortableDeps` | 仅在 `x64` 构建前执行，检查 `opencv2\core.hpp` 是否存在，以及 Debug/Release 所需的 `opencv_world4100d.lib` / `opencv_world4100.lib` 是否存在 |

---

## 16. 运行期依赖与固定加载路径

| 组件 | 当前加载方式 | 缺失时行为 |
| --- | --- | --- |
| `dlcv_infer.dll` | Sentinel 版本；`AutoDetectProvider()` 检测到 Sentinel 时加载；先按系统搜索路径查找，再回退到 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll` | 弹框 `需要先安装 dlcv_infer`，并抛出 `need install dlcv_infer first` |
| `dlcv_infer_v.dll` | Virbox 版本；仅在模型头明确指定 `dog_provider=virbox`，或 `AutoDetectProvider()` 检测到 Virbox 且未检测到 Sentinel 时启用；查找顺序为系统搜索路径，再到 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer_v.dll` | 弹框 `需要先安装 dlcv_infer`，并抛出 `need install dlcv_infer first` |
| 无加密狗 | `AutoDetectProvider()` 返回 `DogProvider::Unknown`；创建空 `DllLoader`，不 `LoadLibrary` 任何推理 DLL | 不弹框、不抛异常；加载模型时再报「未检测到授权」 |
| `sntl_adminapi_windows_x64.dll` | `SNTLDllLoader` 先按系统搜索路径查找，再回退到 `C:\dlcv\bin\sntl_adminapi_windows_x64.dll` | 切换为空代理：`context_new/get` 返回 `SNTL_ADMIN_LM_NOT_FOUND`，`context_delete` 返回成功，`free` 为空函数 |
| `nvml.dll` | `Utils::GetGpuInfo()` 与 NVML 包装函数运行时 `LoadLibraryA("nvml.dll")` | `GetGpuInfo()` 返回错误 JSON；初始化失败时 `code=1`，取设备数失败时 `code=2` |

---

## 17. 编码与路径规则

| 方向 | 导出函数 |
| --- | --- |
| 本地 ANSI 与 `wstring` | `convertStringToWstring()`、`convertWstringToString()` |
| UTF-8 与 `wstring` | `convertWstringToUtf8()`、`convertUtf8ToWstring()` |
| GBK 与 `wstring` | `convertWstringToGbk()`、`convertGbkToWstring()` |
| UTF-8 与 GBK | `convertUtf8ToGbk()`、`convertGbkToUtf8()` |

当前实现使用的编码页是 `CP_ACP`、`CP_UTF8` 和 `936`。`Model(const std::string&, int)` 先尝试把路径按 UTF-8 解码并回转校验，回转一致时按 UTF-8 处理，不一致时按 GBK 处理；`Model(const std::wstring&, int)` 先转 UTF-8 后走同一流程。Flow 内部 `model_path` 固定使用 UTF-8，`ModelPool` 创建 `dlcv_infer::Model` 前再转为 GBK。

---

## 18. C++ 对外类型

共享结果语义、JSON 字段语义、Flow 模块分类、模板对象语义和计时口径见 [模块、流程与模型推理标准文档](模块、流程与模型推理标准文档.md)。

### 19.1 对外类型名

| 类型 | 当前字段 |
| --- | --- |
| `ObjectResult` | `categoryId`、`categoryName`、`score`、`area`、`bbox`、`withMask`、`mask`、`withBbox`、`withAngle`、`angle`、`withMean`、`foregroundMean`、`backgroundMean` |
| `SampleResult` | `results` |
| `Result` | `sampleResults` |
| `FlowNodeTiming` | `nodeId`、`nodeType`、`nodeTitle`、`elapsedMs` |

以上为 C++ API 对外结构体字段名。C++ 公共成员命名使用 `camelCase`。

### 19.2 Flow 聚合结构

`FlowResultItem`、`FlowByImageEntry`、`FlowFrontendPayload`、`FlowFrontendByNodePayload`、`FlowBatchResult` 用于 Flow 结果聚合，属于 C++ 侧内部承载结构。

---

## 19. `Model`

### 19.1 公开面

`Model` 暴露字段 `modelIndex`、`OwnModelIndex`；公开构造为默认构造、`Model(const std::string&, int)`、`Model(const std::wstring&, int)`；禁用拷贝、支持移动；公开成员函数为 `FreeModel()`、`GetModelInfo()`、`GetDvsModelInfo()`、`Infer()`、`InferBatch()`、`InferOneOutJson()`、`GetLastInferTiming()`、`GetLastFlowNodeTimings()`、`GetLastInspectionStatus()`。

### 19.2 加载、释放与信息查询

`.dvst/.dvso` 进入 FlowGraph 模式，`.dvsp` 当前直接返回不支持错误，其余走底层 `dlcv_infer.dll` 普通模型模式。普通模型通过 `dlcv_load_model` 加载，加载前由 `DllLoader::ForModel` 解析模型头并绑定对应 provider 的 loader：若模型头明确指定 `dog_provider`，则校验对应加密狗；若未指定，则通过 `AutoDetectProvider()` 按 Sentinel 优先、Virbox 第二自动检测。FlowGraph 模式创建 `flow::FlowGraphModel`，完成归档解包后加载全部模型节点，解包过程不得修改模型二进制数据。流程直接复用子模型实际使用的 loader；无模型节点时复用现有 loader，没有现有 loader 时直接选择 Sentinel，不执行 provider 检测。共享接口完整时登记共享流程，旧 DLL缺少共享接口时使用本地流程 index。`FreeModel()` 会按 `OwnModelIndex` 决定释放底层资源还是仅清空索引。`GetModelInfo()` 在普通模式直接返回底层 JSON，在 FlowGraph 模式返回普通模型兼容结构，并附加 `loaded_model_meta` 与按模型文件名索引的 `model_info`；`GetDvsModelInfo()` 返回完整流程及全部子模型信息。

### 19.3 推理前图像规整

`prepareInferInputBatch()` 会先从模型信息推断目标通道数，并用 `_expectedChCache` 缓存结果。当前入口会先统一位深到 `CV_8U`，再按模型输入做最小必要的通道规整：三通道模型会把单通道输入补成 `RGB`，单通道模型会把三/四通道输入压成灰度；接口不负责 `BGR/BGRA -> RGB` 颜色顺序整理，三通道颜色图仍由调用方先按 RGB 送入。

### 19.4 推理、结果与计时

普通模型请求固定组装 `model_index + image_list` 后调用底层推理，`code!=0` 时抛异常。结构化包装阶段会自动补推断 `with_bbox`、`with_angle`，读取 `with_mean`、`foreground_mean`、`background_mean`，并对 `mask` 做 `clone()`、必要时缩放或反推框。`InferOneOutJson()` 只返回首张图结果；未产生流程判定时返回原结果数组，产生判定时返回 `{"result_list":[...],"ok":true|false,"reason":null|[...]}`。最近一次计时和流程判定状态保存在当前线程；`GetLastInspectionStatus(bool&, std::vector<std::string>&, size_t)` 按图片索引读取最近一次 `Infer`、`InferBatch` 或 `InferOneOutJson` 的状态，未产生状态时返回 `false`。FlowGraph 模式的计时优先使用流程返回的 `timing`。

---

## 20. `Utils`

`Utils` 的公开静态函数包括 `JsonToString()`、`FreeAllModels()`、`GetDeviceInfo()`、`OcrInfer()`、`GetGpuInfo()`、`KeepMaxClock()` 和 5 个 NVML 包装函数。其行为分别是：`JsonToString()` 使用 `dump(4)`；`FreeAllModels()` 直接调用 `dlcv_free_all_models`；`GetDeviceInfo()` 直接调用 `dlcv_get_device_info`；`KeepMaxClock()` 仅在底层导出 `dlcv_keep_max_clock` 时调用；`OcrInfer()` 先用检测模型跑整图，再按 `bbox` 裁 ROI 给识别模型，若识别结果存在，则用第 1 条识别结果的 `categoryName` 覆盖检测结果的 `categoryName`；`GetGpuInfo()` 成功时返回 `{code:0,message:"Success",devices:[{device_id,device_name}]}`，NVML 初始化失败时返回 `code=1`，获取设备数量失败时返回 `code=2`。

---

## 21. `sntl_admin`

公开类型为 `SntlAdminStatus`、`SNTLDllLoader`、`SNTL`、`SNTLUtils`、`Virbox`、`DogProvider`、`DogInfo`、`DogUtils` 和 `ParseXmlToJson()`。固定 XML 常量中，`DefaultScope` 的厂商 ID 固定为 `26146`，`HaspIdFormat` 读取 `haspid`，`FeatureIdFormat` 读取 `featureid` 与 `haspid`。`SNTL` 构造时调用 `sntl_admin_context_new`，析构时调用 `Dispose()`，`Dispose()` 再调 `sntl_admin_context_delete`；`Get()` 调 `sntl_admin_get`，成功时返回 `{ "code": 0, "message": "成功", "data": ... }`，失败时返回 `{ "code": <status>, "message": "<状态描述>" }`。`SNTLUtils::GetDeviceList()` 返回 Sentinel 加密狗 ID 数组，`GetFeatureList()` 返回 Sentinel 特性 ID 数组，任一异常都返回空数组 `[]`，不再自动回退到 Virbox。`Virbox` 合并 `slm_ctrl_get_all_description` 返回的设备描述与 `slm_ctrl_get_offline_local_desc` 返回的本地软锁描述；授权码软锁使用 `user_guid` 作为唯一锁号，特性列表通过 `slm_ctrl_get_license_id` 读取。`DogUtils::GetAllDogInfo()` 返回同时包含 Sentinel 与 Virbox 信息的 JSON。

---

## 22. Flow 与 DVS 的 C++ 实现

### 22.1 DVS 归档加载

共享的 Flow 与归档语义见 [模块、流程与模型推理标准文档](模块、流程与模型推理标准文档.md)。C++ 侧额外处理 DVS 归档解包、`pipeline.json` 中 `model_path` 重写，以及临时目录清理。

### 22.2 `FlowGraphModel`

`FlowGraphModel` 公开接口为 `IsLoaded()`、`Load()`、`GetModelInfo()`、`GetDvsModelInfo()`、`InferOneOutJson()`、`InferInternal()`、`Benchmark()`，禁用拷贝、支持移动。`Load()` 从 UTF-8 流程 JSON 读取 `nodes` 并预加载 `model/*` 节点，同时保存每个模型节点的普通模型信息。

### 22.3 `ExecutionContext`

`ExecutionContext` 是轻量键值容器，公开 `Set<T>()`、`Get<T>()`、`Has()`、`Remove()`、`Clear()`；内部用 `shared_ptr<IValue>` 持有值，拷贝时做深拷贝。

### 22.4 `GraphExecutor`

`GraphExecutor` 负责节点排序、链路路由、标量注入、`infer_params` 属性覆盖和 `model/*` 预加载。未注册普通节点会跳过，未注册模型节点会记录到加载报告；当前节点输出链路还会写入 `__graph_current_output_mask` 供部分模块读取。

### 22.5 Flow 结果聚合

聚合读取优先级为 `frontend_payloads_by_node -> frontend_json.by_node -> frontend_json_by_node -> frontend_json.last -> frontend_payload_last`。单图时 `result_list` 直接是结果数组，存在流程判定时根对象同时包含 `ok/reason`；多图时为 `[{ "result_list": [...], "ok": ..., "reason": ... }, ...]`，未产生判定状态的图片不增加这些字段。

### 22.6 已注册 Flow 节点

C++ Flow 节点实现位于 `flow/modules/InputModules.cpp`、`flow/modules/ModelModules.cpp`、`flow/modules/OutputModules.cpp`、`flow/modules/SlidingModules.cpp`、`flow/modules/FeatureModules.cpp`、`flow/modules/PostProcessModules.cpp`、`flow/modules/RegionStrokeVisualizeTemplateModules.cpp`。当前注册集覆盖输入、模型、预处理/特征、后处理、输出与模板模块；`features/printed_template_match` 由 `features/template_match` 兼容实现，不包含 C# Flow 的 `defer_template_creation` 候选事务、`count_priority` 数量优先和相应扩展结果字段。当前实现中，`input/*` 从磁盘读图时会把三/四通道输入统一整理为 RGB 语义后再入 Flow，`model/*` 入口不再隐式执行 BGR→RGB 转换，但仍会按模型输入做最小必要的通道规整，`output/save_image` 按内部固定 RGB 语义把三通道/四通道图像转换回 OpenCV 写盘所需的 BGR 语义。

---

## 23. 仅 DLL 构建内部使用的类型

以下类型位于 `#ifdef DLCV_INFER_CPP_DLL_EXPORTS` 条件编译区域：

- `DllLoader`

---

## 24. 同进程共享索引测试导出

以下函数由 `dlcv_infer_cpp_dll.dll` 导出，仅用于控制台测试工程中的跨语言共享索引验证，不属于生产调用入口：

```cpp
int dlcv_shared_index_test_load_c(const wchar_t* model_path, int device_id);
const char* dlcv_shared_index_test_infer_c(int index, const wchar_t* image_path);
int dlcv_shared_index_test_free_c(int index);
void dlcv_shared_index_test_free_string_c(const char* result);
```

- `dlcv_shared_index_test_load_c` 使用 C++ `Model` 加载模型并在 DLL 内保存所有者，成功返回 `modelIndex`，失败返回 `-1`。
- `dlcv_shared_index_test_infer_c` 在单次调用内构造空 `Model`，设置 `modelIndex` 和 `OwnModelIndex=false`，依次执行 `GetModelInfo()`、`Infer()`、`InferOneOutJson()`；返回 UTF-8 JSON，包含 `code`、`index`、`model_info`、`structured` 和 `infer_json`。`structured` 只保留样本数量、目标数量、类别、分数、bbox、mask 标志及 mask 尺寸。
- `dlcv_shared_index_test_free_c` 释放 DLL 内保存的 C++ 所有者，成功返回 `0`，失败返回 `-1`。
- `dlcv_shared_index_test_free_string_c` 释放 `dlcv_shared_index_test_infer_c` 返回的字符串。
