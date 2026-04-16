# `dlcv_infer_cpp_dll` C++ API 开发文档

## 1. 项目范围

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

## 2. 构建与输出

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
- 输出目录仅在 `x64` 配置中显式设置为 `$(SolutionDir)$(Configuration)\`
- Debug x64 链接库：`opencv_world4100d.lib`
- Release x64 链接库：`opencv_world4100.lib`

当前工程编译的源文件：

- `dlcv_infer.cpp`
- `dlcv_sntl_admin.cpp`
- `flow/GraphExecutor.cpp`
- `flow/FlowGraphModel.cpp`
- `flow/modules/ModelModules.cpp`
- `flow/modules/InputModules.cpp`
- `flow/modules/OutputModules.cpp`
- `flow/modules/SlidingModules.cpp`
- `flow/modules/FeatureModules.cpp`
- `flow/modules/PostProcessModules.cpp`
- `flow/modules/RegionStrokeVisualizeTemplateModules.cpp`

## 3. 构建期依赖解析

### 3.1 OpenCV 头文件目录解析顺序

- 环境变量 `OpenCV_INCLUDE_DIR`
- `OpenCV_DIR`
- `OpenCV_DIR\include`
- `OpenCV_DIR\..\..\include`
- `$(SolutionDir)third_party\opencv\build\include`
- `$(ProjectDir)..\third_party\opencv\build\include`
- `C:\OpenCV\build\include`

### 3.2 OpenCV 库目录解析顺序

- 环境变量 `OpenCV_LIB_DIR`
- `OpenCV_DIR`
- `OpenCV_DIR\lib`
- `OpenCV_DIR\x64\vc16\lib`
- `$(SolutionDir)third_party\opencv\build\x64\vc16\lib`
- `$(ProjectDir)..\third_party\opencv\build\x64\vc16\lib`
- `C:\OpenCV\build\x64\vc16\lib`

### 3.3 DLCV SDK 头文件目录解析顺序

- 环境变量 `DLCVPRO_INFER_INCLUDE`
- `$(SolutionDir)third_party\dlcvpro_infer\include`
- `$(ProjectDir)..\third_party\dlcvpro_infer\include`
- `C:\dlcv\Lib\site-packages\dlcvpro_infer\include`

### 3.4 构建前检查

`ValidatePortableDeps` 目标仅在 `x64` 构建前执行，检查：

- `$(OpenCvIncludeDir)\opencv2\core.hpp`
- Debug：`$(OpenCvLibDir)\opencv_world4100d.lib`
- 非 Debug：`$(OpenCvLibDir)\opencv_world4100.lib`

## 4. 运行期依赖与固定加载路径

### 4.1 推理 DLL

内部加载器 `DllLoader` 先查询 `sntl_admin::SNTL::GetFeatureList()`：

- 功能列表包含 `"1"`：保持 `dlcv_infer.dll`
- 功能列表不含 `"1"` 且包含 `"2"`：若 `dlcv_infer2.dll` 可用，则切换到 `dlcv_infer2.dll`

推理 DLL 查找顺序：

- 进程搜索路径中的 `dlcv_infer.dll` 或 `dlcv_infer2.dll`
- 固定路径：
  - `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll`
  - `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer2.dll`

当目标 DLL 不存在时：

- 弹出消息框：`需要先安装 dlcv_infer`
- 抛出异常：`need install dlcv_infer first`

### 4.2 加密狗管理 DLL

`sntl_admin::SNTLDllLoader` 查找顺序：

- 当前加载路径中的 `sntl_adminapi_windows_x64.dll`
- 固定路径 `C:\dlcv\bin\sntl_adminapi_windows_x64.dll`

加载失败时，内部函数指针切换为空代理实现：

- `sntl_admin_context_new` 返回 `SNTL_ADMIN_LM_NOT_FOUND`
- `sntl_admin_context_delete` 返回 `SNTL_ADMIN_STATUS_OK`
- `sntl_admin_get` 返回 `SNTL_ADMIN_LM_NOT_FOUND`
- `sntl_admin_free` 为空函数

### 4.3 NVML

`Utils::GetGpuInfo()`、`Utils::nvmlInit()`、`Utils::nvmlShutdown()`、`Utils::nvmlDeviceGetCount()`、`Utils::nvmlDeviceGetName()`、`Utils::nvmlDeviceGetHandleByIndex()` 运行时动态加载 `nvml.dll`。

## 5. 编码与路径规则

### 5.1 字符串转换函数

`dlcv_infer.h` 导出以下转换函数：

```cpp
std::wstring convertStringToWstring(const std::string& inputString);
std::string convertWstringToString(const std::wstring& inputWstring);
std::string convertWstringToUtf8(const std::wstring& inputWstring);
std::wstring convertUtf8ToWstring(const std::string& inputUtf8);
std::string convertWstringToGbk(const std::wstring& inputWstring);
std::wstring convertGbkToWstring(const std::string& inputGbk);
std::string convertUtf8ToGbk(const std::string& inputUtf8);
std::string convertGbkToUtf8(const std::string& inputGbk);
```

当前实现使用的编码页：

- 本地 ANSI：`CP_ACP`
- UTF-8：`CP_UTF8`
- GBK：`936`

### 5.2 模型路径解码

`Model(const std::string&, int)` 的路径处理流程：

- 先尝试将入参按 UTF-8 解码为 `std::wstring`
- 再回转为 UTF-8 校验
- 回转结果与原字符串一致时，按 UTF-8 路径处理
- 回转结果不一致时，按 GBK 路径处理

`Model(const std::wstring&, int)` 先转为 UTF-8，再进入相同逻辑。

### 5.3 Flow 模式路径规则

- `FlowGraph` 内部 `model_path` 使用 UTF-8 字符串
- `ModelPool` 创建 `dlcv_infer::Model` 前，将 UTF-8 路径转为 GBK

## 6. 对外数据结构

### 6.1 结果结构

```cpp
struct ObjectResult {
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
};

struct SampleResult {
    std::vector<ObjectResult> results;
};

struct Result {
    std::vector<SampleResult> sampleResults;
};

struct FlowNodeTiming {
    int nodeId;
    std::string nodeType;
    std::string nodeTitle;
    double elapsedMs;
};
```

字段语义：

- `bbox`
  - 普通框：`[x, y, w, h]`
  - 旋转框：`[cx, cy, w, h]`，角度保存在 `angle`
- `withAngle=false` 时，`angle` 为 `-100.0f`
- `withMask=true` 时，`mask` 为 `CV_8UC1`
- `categoryName` 在结构化结果中会被转为 GBK 字符串

### 6.2 Flow 聚合结构

`flow/FlowPayloadTypes.h` 定义：

- `FlowResultItem`
- `FlowByImageEntry`
- `FlowFrontendPayload`
- `FlowFrontendByNodePayload`
- `FlowBatchResult`

`FlowResultItem` 的标准字段：

- `category_id`
- `category_name`
- `score`
- `bbox`
- `metadata`
- `mask_rle`
- `poly`
- 其余字段进入 `Extra`

## 7. `Model` 类

### 7.1 对外签名

```cpp
class Model {
public:
    int modelIndex = -1;
    bool OwnModelIndex = true;

    Model();
    Model(const std::string& modelPath, int device_id);
    Model(const std::wstring& modelPath, int device_id);

    Model(const Model&) = delete;
    Model& operator=(const Model&) = delete;

    Model(Model&& other) noexcept;
    Model& operator=(Model&& other) noexcept;

    virtual ~Model();

    void FreeModel();
    json GetModelInfo();
    Result Infer(const cv::Mat& image, const json& params_json = nullptr);
    Result InferBatch(const std::vector<cv::Mat>& image_list, const json& params_json = nullptr);
    json InferOneOutJson(const cv::Mat& image, const json& params_json = nullptr);

    static void GetLastInferTiming(double& dlcvInferMs, double& totalInferMs);
    static std::vector<FlowNodeTiming> GetLastFlowNodeTimings();
};
```

### 7.2 构造模式

- 默认构造不加载模型
- 后缀为 `.dvst`、`.dvso`、`.dvsp` 时进入 FlowGraph 模式
- 其他路径走底层 `dlcv_infer.dll` 的普通模型模式

### 7.3 普通模型加载

加载请求 JSON：

```json
{
  "model_path": "<utf8_path>",
  "device_id": 0
}
```

调用流程：

- 调用 `dlcv_load_model`
- 结果 JSON 中存在 `model_index` 时写入 `Model::modelIndex`
- 结果 JSON 中不存在 `model_index` 时抛出异常：`load model failed: <result_json>`

### 7.4 FlowGraph 模型加载

FlowGraph 模式下：

- 创建 `flow::FlowGraphModel`
- 将归档解包到临时目录
- 写出重写后的 `pipeline.json`
- 调用 `FlowGraphModel::Load`
- 返回 `code==0` 时视为加载成功
- 成功后 `modelIndex` 固定置为 `1`
- 加载失败时抛出异常：`failed to load dvs model: <message>`

### 7.5 `OwnModelIndex`

- `true`：`FreeModel()` 与析构函数会释放底层模型
- `false`：`FreeModel()` 与析构函数只把当前对象的 `modelIndex` 置为 `-1`

### 7.6 `FreeModel()`

行为分支：

- FlowGraph 模式：删除 `_flowModel`，将 `modelIndex` 置为 `-1`
- 普通模式且 `modelIndex==-1`：直接返回
- 普通模式且 `OwnModelIndex=false`：仅清空 `modelIndex`
- 普通模式且 `OwnModelIndex=true`：调用 `dlcv_free_model`

调用 `FreeModel()` 时会把 `_expectedChCache` 重置为 `-2`。

### 7.7 `GetModelInfo()`

- FlowGraph 模式：返回 `FlowGraphModel::_root`
- 普通模式：调用 `dlcv_get_model_info`，返回底层 JSON 原样结果

### 7.8 输入图像规整

`Infer()`、`InferBatch()`、`InferOneOutJson()` 在实际推理前统一调用 `prepareInferInputBatch()`。

通道数解析：

- 调用 `GetModelInfo()`
- 从 `model_info.input_shapes` 或根级 `input_shapes` 中解析 `max_shape`
- 仅识别 `1` 通道和 `3` 通道

缓存状态：

- `-2`：未解析
- `-1`：未识别出有效通道数，按 3 通道处理
- `1`：按单通道处理
- `3`：按三通道处理

位深转换规则：

- `CV_8U`：直接使用
- `CV_16U`：按 `1.0 / 256.0` 转 `CV_8U`
- `CV_16S`：`NORM_MINMAX` 转 `CV_8U`
- `CV_32F`、`CV_64F`
  - 值域位于 `[0,1]`：乘 `255.0`
  - 其他情况：`NORM_MINMAX` 转 `CV_8U`
- 其他深度：`convertTo(CV_8U)`

通道规整规则：

- 目标 3 通道：
  - 1 通道：`GRAY2RGB`
  - 4 通道：`RGBA2RGB`
  - 3 通道：直接使用
  - 其他通道数：提取第 0 通道，再 `GRAY2RGB`
- 目标 1 通道：
  - 1 通道：直接使用
  - 3 通道：`RGB2GRAY`
  - 4 通道：`RGBA2GRAY`
  - 其他通道数：提取第 0 通道

FlowGraph 模式下，当输入已经满足：

- `depth == CV_8U`
- `channels == expectedChannels`

则直接透传，不再重建 `cv::Mat`。

### 7.9 普通模型推理请求

普通模型模式下，每次推理构造：

```json
{
  "model_index": 1,
  "image_list": [
    {
      "width": 1920,
      "height": 1080,
      "channels": 3,
      "image_ptr": 123456789
    }
  ]
}
```

`params_json` 为对象时，顶层键值直接合并到请求 JSON。

底层返回 `code != 0` 时抛出异常：

- `Inference failed: <message>`

### 7.10 结构化结果解析

普通模型模式下，`ParseToStructResult()` 从 `sample_results[*].results[*]` 读取：

- `category_id`
- `category_name`
- `score`
- `area`
- `bbox`
- `with_mask`
- `mask`
- `with_bbox`
- `with_angle`
- `angle`

结果修正规则：

- `category_name` 从 UTF-8 转为 GBK
- `with_bbox` 缺失时按 `bbox.size() >= 4`
- `with_angle` 缺失时，若 `bbox.size() >= 5` 或 `angle > -99.0f`，则置为 `true`
- `mask` 指针转 `cv::Mat` 后立即 `clone()`
- `mask` 尺寸与 `bbox` 尺寸不一致时，按 `INTER_NEAREST` 缩放到 `bbox` 尺寸
- `bbox` 缺失但 `mask` 存在时，从非零区域求外接矩形补齐 `bbox`

### 7.11 `InferOneOutJson()`

普通模型模式：

- 返回 `sample_results[0].results`
- `with_mask=true` 时，将 `mask_ptr` 指向的 mask 提取轮廓，输出 `[{x,y}, ...]`

FlowGraph 模式：

- 从流程根结果提取第一张图的 `result_list`
- 输出标准数组
- 每条结果固定包含：
  - `category_id`
  - `category_name`
  - `score`
  - `bbox`
  - `with_bbox`
  - `with_angle`
  - `angle`
  - `mask`
  - `with_mask`
  - `area`
- 无 mask 时，`mask` 固定为：

```json
{
  "height": -1,
  "mask_ptr": 0,
  "width": -1
}
```

### 7.12 计时

`Model` 使用线程局部变量记录最近一次推理计时：

- `g_lastDlcvInferMs`
- `g_lastTotalInferMs`
- `g_lastFlowNodeTimings`

普通模型模式：

- `dlcvInferMs == totalInferMs == 本次调用 wall clock 毫秒数`

FlowGraph 模式：

- 优先读取流程返回中的 `timing.dlcv_infer_ms`
- 优先读取流程返回中的 `timing.flow_infer_ms`
- 缺失时回退为本次调用 wall clock 毫秒数

## 8. `Utils` 类

### 8.1 对外签名

```cpp
class Utils {
public:
    static std::string JsonToString(const json& j);
    static void FreeAllModels();
    static json GetDeviceInfo();
    static Result OcrInfer(Model& detectModel, Model& recognizeModel, const cv::Mat& image);
    static json GetGpuInfo();
    static void KeepMaxClock();
    static int nvmlInit();
    static int nvmlShutdown();
    static int nvmlDeviceGetCount(unsigned int* deviceCount);
    static int nvmlDeviceGetName(nvmlDevice_t device, char* name, unsigned int length);
    static int nvmlDeviceGetHandleByIndex(unsigned int index, nvmlDevice_t* device);
};
```

### 8.2 行为

- `JsonToString()` 使用 `dump(4)` 输出四空格缩进 JSON
- `FreeAllModels()` 直接调用底层 `dlcv_free_all_models`
- `GetDeviceInfo()` 直接调用底层 `dlcv_get_device_info`
- `KeepMaxClock()` 仅在底层导出 `dlcv_keep_max_clock` 时调用

### 8.3 `OcrInfer()`

执行流程：

- 使用 `detectModel.InferBatch()` 对整图检测
- 逐条结果按 `bbox` 提取 ROI
- 使用 `recognizeModel.InferBatch()` 对 ROI 识别
- 识别结果存在时，用识别结果第 1 条的 `categoryName` 覆盖检测结果的 `categoryName`

### 8.4 `GetGpuInfo()`

返回 JSON 结构：

```json
{
  "code": 0,
  "message": "Success",
  "devices": [
    {
      "device_id": 0,
      "device_name": "NVIDIA ..."
    }
  ]
}
```

错误返回：

- NVML 初始化失败：`code=1`，`message="Failed to initialize NVML."`
- 获取设备数量失败：`code=2`，`message="Failed to get device count."`

## 9. `sntl_admin` 接口

### 9.1 公开类型

```cpp
enum SntlAdminStatus;
class SNTLDllLoader;
class SNTLUtils;
class SNTL;
nlohmann::json ParseXmlToJson(const std::string& xml);
```

### 9.2 固定 XML 常量

- `SNTLUtils::DefaultScope`
  - 厂商 ID 固定为 `26146`
- `SNTLUtils::HaspIdFormat`
  - 读取 `haspid`
- `SNTLUtils::FeatureIdFormat`
  - 读取 `featureid` 与 `haspid`

### 9.3 `SNTL` 行为

- 构造时调用 `sntl_admin_context_new`
- 析构时调用 `Dispose()`
- `Dispose()` 调用 `sntl_admin_context_delete`
- `Get()` 调用 `sntl_admin_get`，成功时返回：

```json
{
  "code": 0,
  "message": "成功",
  "data": { ... }
}
```

- `Get()` 失败时返回：

```json
{
  "code": <status>,
  "message": "<状态描述>"
}
```

### 9.4 `SNTLUtils`

- `GetDeviceList()` 返回加密狗 ID 数组
- `GetFeatureList()` 返回特性 ID 数组
- 任一异常时返回空数组 `[]`

## 10. DVS 归档格式与加载

### 10.1 识别规则

以下后缀按 DVS 归档处理，大小写不敏感：

- `.dvst`
- `.dvso`
- `.dvsp`

### 10.2 文件格式

归档读取逻辑要求：

- 文件前 3 字节为 `D`, `V`, `\n`
- 下一行是单行 JSON 头
- 头 JSON 必须包含：
  - `file_list`
  - `file_size`
- `file_list.size() == file_size.size()`

### 10.3 解包流程

- 创建临时目录：`%TEMP%\DlcvDvs_<24位随机十六进制>`
- 读取归档内每个文件
- `pipeline.json` 直接读取文本并解析
- 其他文件写入临时目录，文件名改为 `32位随机十六进制 + 原扩展名`
- 用原始 `model_path` 和文件名两种键重写 `pipeline.json` 中各节点的 `properties.model_path`
- 在临时目录写出新的 `pipeline.json`
- 调用 `FlowGraphModel::Load`
- 解包目录由 `TempDirGuard` 递归删除

## 11. `FlowGraphModel`

### 11.1 对外签名

```cpp
class FlowGraphModel final {
public:
    FlowGraphModel() = default;
    ~FlowGraphModel();

    FlowGraphModel(const FlowGraphModel&) = delete;
    FlowGraphModel& operator=(const FlowGraphModel&) = delete;
    FlowGraphModel(FlowGraphModel&& other) noexcept;
    FlowGraphModel& operator=(FlowGraphModel&& other) noexcept;

    bool IsLoaded() const;
    Json Load(const std::string& flowJsonPath, int deviceId = 0);
    Json GetModelInfo() const;
    Json InferOneOutJson(const cv::Mat& image, const Json& paramsJson = Json());
    Json InferInternal(const std::vector<cv::Mat>& images, const Json& paramsJson = Json());
    double Benchmark(const cv::Mat& image, int warmup = 1, int runs = 10);
};
```

### 11.2 `Load()`

- 从 UTF-8 文本读取流程 JSON
- 根对象必须包含 `nodes` 数组
- 执行 `GraphExecutor::LoadModels()`
- `LoadModels()` 仅对 `type` 以 `model/` 开头的节点调用 `LoadModel()`
- 返回结果结构：

```json
{
  "code": 0,
  "message": "all models loaded",
  "models": [
    {
      "node_id": 1,
      "type": "model/det",
      "title": "...",
      "model_path": "...",
      "status_code": 0,
      "status_message": "ok"
    }
  ]
}
```

- 若有节点加载失败，`FlowGraphModel` 会把整体结果压缩为：

```json
{
  "code": 1,
  "message": "<第一条失败消息或 report.message>"
}
```

### 11.3 FlowGraph 清理范围

- `FlowGraphModel` 析构与移动赋值前清理调用 `ReleaseNoexcept()`
- `ReleaseNoexcept()` 仅清理 `ModelPool::Instance().Clear()`
- `ReleaseNoexcept()` 不调用 `Utils::FreeAllModels()`

### 11.4 `InferInternal()`

执行前向 `ExecutionContext` 写入：

- `frontend_image_mat`
- `frontend_image_mats`
- `frontend_image_mat_list`
- `frontend_image_color_space = "rgb"`
- `frontend_image_path = ""`
- `device_id`
- `infer_params`
- `flow_dlcv_infer_ms_acc = 0.0`

执行后返回根对象：

```json
{
  "result_list": [...],
  "timing": {
    "flow_infer_ms": 0.0,
    "dlcv_infer_ms": 0.0,
    "node_timings": [
      {
        "node_id": 1,
        "node_type": "model/det",
        "node_title": "...",
        "elapsed_ms": 0.0
      }
    ]
  }
}
```

### 11.5 `Benchmark()`

- 先执行 `warmup` 次 `InferOneOutJson()`
- 再执行 `runs` 次 `InferOneOutJson()`
- 返回平均毫秒数

## 12. `ExecutionContext`

`ExecutionContext` 为轻量键值容器：

- `Set<T>(key, value)`
- `Get<T>(key, defaultValue)`
- `Has(key)`
- `Remove(key)`
- `Clear()`

语义：

- 存储对象通过 `shared_ptr<IValue>` 持有
- 拷贝构造与拷贝赋值执行深拷贝
- `Get<T>()` 类型不匹配时返回默认值

## 13. `GraphExecutor`

### 13.1 执行顺序

- 节点按 `order` 升序执行
- `order` 相同时按 `id` 升序执行

### 13.2 链路路由

- `outputs[*].links[*]` 建立 `linkId -> (srcNodeId, srcOutIdx)` 映射
- 输入端口按索引两两组成一个 `ModuleChannel`
  - `0/1` 为主通道
  - `2/3` 为第 1 个额外通道
  - `4/5` 为第 2 个额外通道
- 通道类型：
  - `image_chan`
  - `result_chan`
  - `template_chan`

### 13.3 标量端口

标量输入类型：

- `bool`
- `boolean`
- `int`
- `integer`
- `str`
- `string`
- `scalar`

标量通过 `ScalarInputsByIndex`、`ScalarInputsByName` 注入，通过 `ScalarOutputsByName` 导出。

### 13.4 属性修正

每个节点执行前：

- 将 `infer_params` 中 primitive 或 `null` 顶层字段覆盖到节点 `properties`
- 键名 `with_mask` 不参与这一步覆盖
- `model/*` 节点仍会单独读取 `infer_params.with_mask`
- `infer_params.with_mask=false` 且节点属性 `with_mask=true` 时，模型节点不再向结果写入 `mask_rle`
- 若存在 `bbox_x1`、`bbox_y1`、`bbox_x2`、`bbox_y2`，自动补齐：
  - `bbox_x`
  - `bbox_y`
  - `bbox_w`
  - `bbox_h`

### 13.5 未注册节点

- `Run()` 中未注册 `type` 的节点直接跳过
- `LoadModels()` 中未注册的 `model/*` 节点记为：
  - `status_code = 1`
  - `status_message = "module_not_registered"`

### 13.6 输出掩码

每个节点执行前，当前节点 `outputs[*].links` 会转换为位掩码并写入：

- `__graph_current_output_mask`

该键当前被以下模块使用：

- `output/save_image`
- `output/preview`
- `output/return_json`
- `post_process/result_filter_advanced`
- `pre_process/sliding_window`

## 14. Flow 结果聚合

### 14.1 `output/return_json`

`output/return_json` 的当前行为：

- 将局部结果恢复到原图坐标
- 生成 `FlowFrontendPayload`
- 追加到 `ExecutionContext["frontend_payloads_by_node"]`
- 当前节点没有下游消费者时，主输出清空

### 14.2 聚合优先级

`FlowGraphModel` 聚合前端结果时，读取顺序为：

- `frontend_payloads_by_node`
- `frontend_json.by_node`
- `frontend_json_by_node`
- `frontend_json.last`
- `frontend_payload_last`

### 14.3 聚合规则

- `frontend_payloads_by_node` 按 `NodeOrder`、`FallbackOrder` 排序
- `output/return_json` 写入的 `NodeOrder` 等于当前节点 `NodeId`
- `origin_index >= 0` 时，结果回写到对应原图索引
- `origin_index < 0` 时，使用 `ByImage` 中的位置索引
- 去重方法：`FlowResultItem::ToJson().dump()` 字符串完全相同时跳过

### 14.4 根结果格式

单图：

```json
{
  "result_list": [
    {
      "category_id": 1,
      "category_name": "OK",
      "score": 0.99
    }
  ]
}
```

多图：

```json
{
  "result_list": [
    {
      "result_list": [ ... ]
    },
    {
      "result_list": [ ... ]
    }
  ]
}
```

## 15. 已注册 Flow 节点

### 15.1 输入节点

- `input/image`
  - 作用：生成输入图像与空结果项
  - 主要属性：`path`、`paths`
  - 输出：`image + result`，并写 `scalar filename`
- `input/frontend_image`
  - 作用：生成前端图像输入
  - 主要属性：`path`
  - 输出：`image + result`
- `input/build_results`
  - 作用：构造一条测试检测结果
  - 主要属性：`image_path`、`default_width`、`default_height`、`default_color`、`category_id`、`category_name`、`score`、`bbox_x1`、`bbox_y1`、`bbox_x2`、`bbox_y2`、`bbox_x`、`bbox_y`、`bbox_w`、`bbox_h`
  - 输出：`image + result`

### 15.2 模型节点

- `model/det`
  - 作用：检测类模型批量推理
  - 主要属性：`model_path`、`device_id`、`threshold`、`iou_threshold`、`top_k`、`with_mask`、`return_polygon`、`epsilon`、`batch_size`
  - 输出：`image + result`
- `model/rotated_bbox`
  - 实现与 `model/det` 相同
- `model/instance_seg`
  - 实现与 `model/det` 相同
- `model/semantic_seg`
  - 实现与 `model/det` 相同
- `model/cls`
  - 作用：分类模型结果整理，保证结果带整图 bbox
  - 主要属性：与 `model/det` 相同
  - 输出：`image + result`
- `model/ocr`
  - 作用：OCR 模型结果整理，保证结果带整图 bbox
  - 主要属性：与 `model/det` 相同
  - 输出：`image + result`

### 15.3 特征与预处理节点

- `features/image_generation`
  - 作用：按检测框裁局部图并同步局部结果
  - 主要属性：`crop_expand`、`min_size`、`crop_shape`
  - 输出：`image + result`
- `features/image_flip`
  - 作用：水平或竖直翻转图像并更新变换
  - 主要属性：`direction`
  - 输出：`image`
- `pre_process/coordinate_crop`
  - 别名：`features/coordinate_crop`
  - 作用：按固定坐标裁图
  - 主要属性：`x`、`y`、`w`、`h`
  - 输出：`image + result`
- `pre_process/image_rescale`
  - 别名：`features/image_rescale`
  - 作用：按比例缩放图像
  - 主要属性：`scale`
  - 输出：`image + result`
- `features/image_rotate_by_cls`
  - 作用：按分类标签旋转图像并同步结果坐标
  - 主要属性：`rotate90_labels`、`rotate180_labels`、`rotate270_labels`
  - 输出：`image + result`
- `post_process/result_label_merge`
  - 别名：`features/result_label_merge`
  - 作用：把一路 top1 标签拼到另一路结果类别名前
  - 主要属性：`fixed_text`、`use_first_score_top1`
  - 输出：`image + result`

### 15.4 滑窗节点

- `pre_process/sliding_window`
  - 别名：`features/sliding_window`
  - 作用：将图像切为滑窗子图，并写入滑窗元数据
  - 主要属性：`min_size`、`window_size`、`overlap`
  - 输出：`image`
  - 结果端口已连接时，同时输出占位 `result`
- `pre_process/sliding_merge`
  - 别名：`features/sliding_merge`
  - 作用：将滑窗结果映射回原图坐标并合并
  - 主要属性：`iou_threshold`、`dedup_results`、`task_type`
  - 输出：`image + result`

### 15.5 后处理节点

- `post_process/merge_results`
  - 别名：`features/merge_results`
  - 作用：合并主输入与额外输入的图像和结果
  - 输出：`image + result`
- `post_process/result_filter`
  - 别名：`features/result_filter`
  - 作用：按类别名过滤结果
  - 主要属性：`categories`
  - 主输出：命中结果 `image + result`
  - 额外输出：未命中结果 `image + result`
  - 标量输出：`has_positive`
- `post_process/result_filter_advanced`
  - 别名：`features/result_filter_advanced`
  - 作用：按框宽高、框面积、mask 面积过滤结果
  - 主要属性：`enable_bbox_wh`、`enable_rbox_wh`、`enable_bbox_area`、`enable_mask_area`、`bbox_w_min`、`bbox_w_max`、`bbox_h_min`、`bbox_h_max`、`rbox_w_min`、`rbox_w_max`、`rbox_h_min`、`rbox_h_max`、`bbox_area_min`、`bbox_area_max`、`mask_area_min`、`mask_area_max`
  - 主输出：命中结果 `image + result`
  - 额外输出：未命中结果 `image + result`
  - 标量输出：`has_positive`
- `post_process/text_replacement`
  - 别名：`features/text_replacement`
  - 作用：按映射表替换 `category_name`
  - 主要属性：`mapping`
  - 输出：`image + result`
- `post_process/result_category_override`
  - 别名：`features/result_category_override`
  - 作用：用额外输入第一条类别名覆盖主输入结果类别名
  - 输出：`image + result`
- `post_process/mask_to_rbox`
  - 别名：`features/mask_to_rbox`
  - 作用：将 `mask_rle` 或 `mask_min_area_rect` 转为旋转框
  - 输出：`image + result`
- `post_process/rbox_correction`
  - 别名：`features/rbox_correction`
  - 作用：按旋转角纠正图像与 bbox
  - 主要属性：`fill_value`
  - 输出：`image + result`
- `post_process/bbox_iou_dedup`
  - 别名：`features/bbox_iou_dedup`
  - 作用：按 IoU 或 IoS 对普通框去重
  - 主要属性：`metric`、`iou_threshold`、`per_category`
  - 输出：`image + result`
  - 标量输出：`kept_count`、`removed_count`

### 15.6 输出与模板节点

- `output/save_image`
  - 作用：将图像保存到目录
  - 主要属性：`save_path`、`suffix`、`format`
  - 输出：`image + result`
- `output/preview`
  - 作用：纯透传
  - 输出：`image + result`
- `output/return_json`
  - 作用：生成前端结果载荷并回写上下文
  - 输出：`image + result` 或空输出
- `post_process/result_filter_region`
  - 别名：`features/result_filter_region`
  - 作用：按区域分流结果
  - 主要属性：`x`、`y`、`w`、`h`、`result_region_mode`
  - 主输出：区域内 `image + result`
  - 额外输出：区域外 `image + result`
  - 标量输出：`has_positive`
- `post_process/result_filter_region_global`
  - 别名：`features/result_filter_region_global`
  - 作用：按原图坐标系区域分流结果
  - 主要属性：与 `post_process/result_filter_region` 相同
  - 输出：与 `post_process/result_filter_region` 相同
- `features/stroke_to_points`
  - 作用：将 mask 区域转换为点框结果
  - 主要属性：`counts_dict`、`point_width`、`point_height`
  - 输出：`image + result`
- `output/visualize`
  - 作用：在原图上绘制结果
  - 主要属性：`black_background`、`display_bbox`、`display_text`、`display_score`、`font_scale`、`font_thickness`、`bbox_color`、`bbox_color_rot`
  - 输出：`image + result`
- `output/visualize_local`
  - 作用：在局部图上绘制普通框与标签
  - 主要属性：`bbox_color`、`font_scale`、`font_thickness`
  - 输出：`image + result`
- `features/template_from_results`
  - 作用：从结果生成模板 JSON
  - 主要属性：`product_name`、`product_id`、`template_name`
  - 输出：`image + result + template`
- `features/template_save`
  - 作用：保存模板 JSON 与首张图 PNG
  - 主要属性：`file_name`
  - 输出：空
- `features/template_load`
  - 作用：从 JSON 文件载入模板
  - 主要属性：`path`
  - 输出：`image + result + template`
- `features/template_match`
  - 别名：`features/printed_template_match`
  - 作用：比较主模板与额外输入模板的 OCR 项
  - 主要属性：`position_tolerance_x`、`position_tolerance_y`、`min_confidence_threshold`、`check_position`
  - 主输出：空
  - 标量输出：`ok`、`detail`

## 16. 仅 DLL 构建内部使用的类型

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
