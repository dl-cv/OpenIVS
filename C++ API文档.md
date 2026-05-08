# C++ API 文档

**文档定位**：记录 `dlcv_infer_cpp_dll` 的精确函数签名、数据结构、工程结构、构建配置、依赖、编码路径规则与 C++ 对外接口。所有内容以当前源码实现为准。

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

## 14. 项目范围

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

---

## 15. 构建与输出

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

## 16. 构建期依赖解析

| 项目 | 当前解析顺序或检查规则 |
| --- | --- |
| OpenCV 头文件目录 | `OpenCV_INCLUDE_DIR` -> `OpenCV_DIR` -> `OpenCV_DIR\include` -> `OpenCV_DIR\..\..\include` -> `$(SolutionDir)third_party\opencv\build\include` -> `$(ProjectDir)..\third_party\opencv\build\include` -> `C:\OpenCV\build\include` |
| OpenCV 库目录 | `OpenCV_LIB_DIR` -> `OpenCV_DIR` -> `OpenCV_DIR\lib` -> `OpenCV_DIR\x64\vc16\lib` -> `$(SolutionDir)third_party\opencv\build\x64\vc16\lib` -> `$(ProjectDir)..\third_party\opencv\build\x64\vc16\lib` -> `C:\OpenCV\build\x64\vc16\lib` |
| DLCV SDK 头文件目录 | `DLCVPRO_INFER_INCLUDE` -> `$(SolutionDir)third_party\dlcvpro_infer\include` -> `$(ProjectDir)..\third_party\dlcvpro_infer\include` -> `C:\dlcv\Lib\site-packages\dlcvpro_infer\include` |
| `ValidatePortableDeps` | 仅在 `x64` 构建前执行，检查 `opencv2\core.hpp` 是否存在，以及 Debug/Release 所需的 `opencv_world4100d.lib` / `opencv_world4100.lib` 是否存在 |

---

## 17. 运行期依赖与固定加载路径

| 组件 | 当前加载方式 | 缺失时行为 |
| --- | --- | --- |
| `dlcv_infer.dll` | Sentinel 版本；`DllLoader::ForProvider(DogProvider::Sentinel)` 加载；`Instance()` 与 `ForModel` 在未明确指定 provider 时，通过 `AutoDetectProvider()` 自动检测当前加密狗并按 Sentinel 优先、Virbox 第二选择；先按系统搜索路径查找，再回退到 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll` | 弹框 `需要先安装 dlcv_infer`，并抛出 `need install dlcv_infer first` |
| `dlcv_infer_v.dll` | Virbox 版本；`DllLoader::ForProvider(DogProvider::Virbox)` 加载；仅在模型头明确指定 `dog_provider=virbox` 或 `AutoDetectProvider()` 检测到 Virbox 且未检测到 Sentinel 时启用；查找顺序为系统搜索路径，再到 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer_v.dll` | 弹框 `需要先安装 dlcv_infer`，并抛出 `need install dlcv_infer first` |
| `sntl_adminapi_windows_x64.dll` | `SNTLDllLoader` 先按系统搜索路径查找，再回退到 `C:\dlcv\bin\sntl_adminapi_windows_x64.dll` | 切换为空代理：`context_new/get` 返回 `SNTL_ADMIN_LM_NOT_FOUND`，`context_delete` 返回成功，`free` 为空函数 |
| `nvml.dll` | `Utils::GetGpuInfo()` 与 NVML 包装函数运行时 `LoadLibraryA("nvml.dll")` | `GetGpuInfo()` 返回错误 JSON；初始化失败时 `code=1`，取设备数失败时 `code=2` |

---

## 18. 编码与路径规则

| 方向 | 导出函数 |
| --- | --- |
| 本地 ANSI 与 `wstring` | `convertStringToWstring()`、`convertWstringToString()` |
| UTF-8 与 `wstring` | `convertWstringToUtf8()`、`convertUtf8ToWstring()` |
| GBK 与 `wstring` | `convertWstringToGbk()`、`convertGbkToWstring()` |
| UTF-8 与 GBK | `convertUtf8ToGbk()`、`convertGbkToUtf8()` |

当前实现使用的编码页是 `CP_ACP`、`CP_UTF8` 和 `936`。`Model(const std::string&, int)` 先尝试把路径按 UTF-8 解码并回转校验，回转一致时按 UTF-8 处理，不一致时按 GBK 处理；`Model(const std::wstring&, int)` 先转 UTF-8 后走同一流程。Flow 内部 `model_path` 固定使用 UTF-8，`ModelPool` 创建 `dlcv_infer::Model` 前再转为 GBK。

---

## 19. C++ 对外类型

共享结果语义、JSON 字段语义、Flow 模块分类、模板对象语义和计时口径见 [模块、流程与模型推理标准文档](模块、流程与模型推理标准文档.md)。

### 19.1 对外类型名

| 类型 | 当前字段 |
| --- | --- |
| `ObjectResult` | `categoryId`、`categoryName`、`score`、`area`、`bbox`、`withMask`、`mask`、`withBbox`、`withAngle`、`angle` |
| `SampleResult` | `results` |
| `Result` | `sampleResults` |
| `FlowNodeTiming` | `nodeId`、`nodeType`、`nodeTitle`、`elapsedMs` |

以上为 C++ API 对外结构体字段名。C++ 公共成员命名使用 `camelCase`。

### 19.2 Flow 聚合结构

`FlowResultItem`、`FlowByImageEntry`、`FlowFrontendPayload`、`FlowFrontendByNodePayload`、`FlowBatchResult` 用于 Flow 结果聚合，属于 C++ 侧内部承载结构。

---

## 20. `Model`

### 20.1 公开面

`Model` 暴露字段 `modelIndex`、`OwnModelIndex`；公开构造为默认构造、`Model(const std::string&, int)`、`Model(const std::wstring&, int)`；禁用拷贝、支持移动；公开成员函数为 `FreeModel()`、`GetModelInfo()`、`Infer()`、`InferBatch()`、`InferOneOutJson()`、`GetLastInferTiming()`、`GetLastFlowNodeTimings()`。

### 20.2 加载、释放与信息查询

`.dvst/.dvso/.dvsp` 进入 FlowGraph 模式，其余走底层 `dlcv_infer.dll` 普通模型模式。普通模型通过 `dlcv_load_model` 加载，加载前由 `DllLoader::ForModel` 解析模型头并绑定对应 provider 的 loader：若模型头明确指定 `dog_provider`，则校验对应加密狗；若未指定，则通过 `AutoDetectProvider()` 按 Sentinel 优先、Virbox 第二自动检测。FlowGraph 模式创建 `flow::FlowGraphModel` 并完成归档解包后再加载，解包流程不得修改模型二进制数据。`FreeModel()` 会按 `OwnModelIndex` 决定释放底层资源还是仅清空索引；`GetModelInfo()` 在普通模式直接返回底层 JSON，在 FlowGraph 模式返回流程根对象，并附加 `loaded_model_meta` 与按模型文件名索引的 `model_info`。

### 20.3 推理前图像规整

`prepareInferInputBatch()` 会先从模型信息推断目标通道数，并用 `_expectedChCache` 缓存结果。当前入口会先统一位深到 `CV_8U`，再按模型输入做最小必要的通道规整：三通道模型会把单通道输入补成 `RGB`，单通道模型会把三/四通道输入压成灰度；接口不负责 `BGR/BGRA -> RGB` 颜色顺序整理，三通道颜色图仍由调用方先按 RGB 送入。

### 20.4 推理、结果与计时

普通模型请求固定组装 `model_index + image_list` 后调用底层推理，`code!=0` 时抛异常。结构化包装阶段会自动补推断 `with_bbox`、`with_angle`，并对 `mask` 做 `clone()`、必要时缩放或反推框。`InferOneOutJson()` 只返回首张图结果；最近一次计时保存在线程局部变量中，FlowGraph 模式优先使用流程返回的 `timing`。

---

## 21. `Utils`

`Utils` 的公开静态函数包括 `JsonToString()`、`FreeAllModels()`、`GetDeviceInfo()`、`OcrInfer()`、`GetGpuInfo()`、`KeepMaxClock()` 和 5 个 NVML 包装函数。其行为分别是：`JsonToString()` 使用 `dump(4)`；`FreeAllModels()` 直接调用 `dlcv_free_all_models`；`GetDeviceInfo()` 直接调用 `dlcv_get_device_info`；`KeepMaxClock()` 仅在底层导出 `dlcv_keep_max_clock` 时调用；`OcrInfer()` 先用检测模型跑整图，再按 `bbox` 裁 ROI 给识别模型，若识别结果存在，则用第 1 条识别结果的 `categoryName` 覆盖检测结果的 `categoryName`；`GetGpuInfo()` 成功时返回 `{code:0,message:"Success",devices:[{device_id,device_name}]}`，NVML 初始化失败时返回 `code=1`，获取设备数量失败时返回 `code=2`。

---

## 22. `sntl_admin`

公开类型为 `SntlAdminStatus`、`SNTLDllLoader`、`SNTL`、`SNTLUtils`、`Virbox`、`DogProvider`、`DogInfo`、`DogUtils` 和 `ParseXmlToJson()`。固定 XML 常量中，`DefaultScope` 的厂商 ID 固定为 `26146`，`HaspIdFormat` 读取 `haspid`，`FeatureIdFormat` 读取 `featureid` 与 `haspid`。`SNTL` 构造时调用 `sntl_admin_context_new`，析构时调用 `Dispose()`，`Dispose()` 再调 `sntl_admin_context_delete`；`Get()` 调 `sntl_admin_get`，成功时返回 `{ "code": 0, "message": "成功", "data": ... }`，失败时返回 `{ "code": <status>, "message": "<状态描述>" }`。`SNTLUtils::GetDeviceList()` 返回 Sentinel 加密狗 ID 数组，`GetFeatureList()` 返回 Sentinel 特性 ID 数组，任一异常都返回空数组 `[]`，不再自动回退到 Virbox。`Virbox` 提供独立的 Virbox 设备列表与特征列表查询。`DogUtils::GetAllDogInfo()` 返回同时包含 Sentinel 与 Virbox 信息的 JSON。

---

## 23. Flow 与 DVS 的 C++ 实现

### 23.1 DVS 归档加载

共享的 Flow 与归档语义见 [模块、流程与模型推理标准文档](模块、流程与模型推理标准文档.md)。C++ 侧额外处理 DVS 归档解包、`pipeline.json` 中 `model_path` 重写，以及临时目录清理。

### 23.2 `FlowGraphModel`

`FlowGraphModel` 公开接口为 `IsLoaded()`、`Load()`、`GetModelInfo()`、`InferOneOutJson()`、`InferInternal()`、`Benchmark()`，禁用拷贝、支持移动。`Load()` 从 UTF-8 流程 JSON 读取 `nodes` 并只预加载 `model/*` 节点；`InferInternal()` 在上下文中写入前端图像、设备和参数后返回 `result_list` 与 `timing`；清理阶段只清 `ModelPool`，不调用 `Utils::FreeAllModels()`。

### 23.3 `ExecutionContext`

`ExecutionContext` 是轻量键值容器，公开 `Set<T>()`、`Get<T>()`、`Has()`、`Remove()`、`Clear()`；内部用 `shared_ptr<IValue>` 持有值，拷贝时做深拷贝。

### 23.4 `GraphExecutor`

`GraphExecutor` 负责节点排序、链路路由、标量注入、`infer_params` 属性覆盖和 `model/*` 预加载。未注册普通节点会跳过，未注册模型节点会记录到加载报告；当前节点输出链路还会写入 `__graph_current_output_mask` 供部分模块读取。

### 23.5 Flow 结果聚合

聚合读取优先级为 `frontend_payloads_by_node -> frontend_json.by_node -> frontend_json_by_node -> frontend_json.last -> frontend_payload_last`。单图时 `result_list` 直接是结果数组，多图时为 `[{ "result_list": [...] }, ...]`。

### 23.6 已注册 Flow 节点

C++ Flow 节点实现位于 `flow/modules/InputModules.cpp`、`flow/modules/ModelModules.cpp`、`flow/modules/OutputModules.cpp`、`flow/modules/SlidingModules.cpp`、`flow/modules/FeatureModules.cpp`、`flow/modules/PostProcessModules.cpp`、`flow/modules/RegionStrokeVisualizeTemplateModules.cpp`。当前注册集覆盖输入、模型、预处理/特征、后处理、输出与模板模块；`features/printed_template_match` 由 `features/template_match` 兼容实现。当前实现中，`input/*` 从磁盘读图时会把三/四通道输入统一整理为 RGB 语义后再入 Flow，`model/*` 入口不再隐式执行 BGR→RGB 转换，但仍会按模型输入做最小必要的通道规整，`output/save_image` 按内部固定 RGB 语义把三通道/四通道图像转换回 OpenCV 写盘所需的 BGR 语义。

---

## 24. 仅 DLL 构建内部使用的类型

以下类型位于 `#ifdef DLCV_INFER_CPP_DLL_EXPORTS` 条件编译区域：

- `DllLoader`
- `SlidingWindowModel`

`SlidingWindowModel` 构造参数：

```cpp
SlidingWindowModel(
    const std::string& modelPath,
    int device_id,
    int small_img_width = 832,
    int small_img_height = 704,
    int horizontal_overlap = 16,
    int vertical_overlap = 16,
    float threshold = 0.5f,
    float iou_threshold = 0.2f,
    float combine_ios_threshold = 0.2f);
```

内部加载请求固定包含：

- `type = "sliding_window_pipeline"`
- `model_path`
- `device_id`
- `small_img_width`
- `small_img_height`
- `horizontal_overlap`
- `vertical_overlap`
- `threshold`
- `iou_threshold`
- `combine_ios_threshold`
