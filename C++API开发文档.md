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

## 3. 构建期依赖解析

| 项目 | 当前解析顺序或检查规则 |
| --- | --- |
| OpenCV 头文件目录 | `OpenCV_INCLUDE_DIR` -> `OpenCV_DIR` -> `OpenCV_DIR\include` -> `OpenCV_DIR\..\..\include` -> `$(SolutionDir)third_party\opencv\build\include` -> `$(ProjectDir)..\third_party\opencv\build\include` -> `C:\OpenCV\build\include` |
| OpenCV 库目录 | `OpenCV_LIB_DIR` -> `OpenCV_DIR` -> `OpenCV_DIR\lib` -> `OpenCV_DIR\x64\vc16\lib` -> `$(SolutionDir)third_party\opencv\build\x64\vc16\lib` -> `$(ProjectDir)..\third_party\opencv\build\x64\vc16\lib` -> `C:\OpenCV\build\x64\vc16\lib` |
| DLCV SDK 头文件目录 | `DLCVPRO_INFER_INCLUDE` -> `$(SolutionDir)third_party\dlcvpro_infer\include` -> `$(ProjectDir)..\third_party\dlcvpro_infer\include` -> `C:\dlcv\Lib\site-packages\dlcvpro_infer\include` |
| `ValidatePortableDeps` | 仅在 `x64` 构建前执行，检查 `opencv2\core.hpp` 是否存在，以及 Debug/Release 所需的 `opencv_world4100d.lib` / `opencv_world4100.lib` 是否存在 |

## 4. 运行期依赖与固定加载路径

| 组件 | 当前加载方式 | 缺失时行为 |
| --- | --- | --- |
| `dlcv_infer.dll` | `DllLoader` 先读 `SNTL.GetFeatureList()`；特性含 `"1"` 时保持默认 DLL，随后先按系统搜索路径查找，再回退到 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll` | 弹框 `需要先安装 dlcv_infer`，并抛出 `need install dlcv_infer first` |
| `dlcv_infer2.dll` | 当特性不含 `"1"` 且含 `"2"`，并且 DLL 可找到时切换；查找顺序为系统搜索路径，再到 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer2.dll` | 切换失败时继续使用默认 DLL |
| `sntl_adminapi_windows_x64.dll` | `SNTLDllLoader` 先按系统搜索路径查找，再回退到 `C:\dlcv\bin\sntl_adminapi_windows_x64.dll` | 切换为空代理：`context_new/get` 返回 `SNTL_ADMIN_LM_NOT_FOUND`，`context_delete` 返回成功，`free` 为空函数 |
| `nvml.dll` | `Utils::GetGpuInfo()` 与 NVML 包装函数运行时 `LoadLibraryA("nvml.dll")` | `GetGpuInfo()` 返回错误 JSON；初始化失败时 `code=1`，取设备数失败时 `code=2` |

## 5. 编码与路径规则

| 方向 | 导出函数 |
| --- | --- |
| 本地 ANSI 与 `wstring` | `convertStringToWstring()`、`convertWstringToString()` |
| UTF-8 与 `wstring` | `convertWstringToUtf8()`、`convertUtf8ToWstring()` |
| GBK 与 `wstring` | `convertWstringToGbk()`、`convertGbkToWstring()` |
| UTF-8 与 GBK | `convertUtf8ToGbk()`、`convertGbkToUtf8()` |

当前实现使用的编码页是 `CP_ACP`、`CP_UTF8` 和 `936`。`Model(const std::string&, int)` 先尝试把路径按 UTF-8 解码并回转校验，回转一致时按 UTF-8 处理，不一致时按 GBK 处理；`Model(const std::wstring&, int)` 先转 UTF-8 后走同一流程。Flow 内部 `model_path` 固定使用 UTF-8，`ModelPool` 创建 `dlcv_infer::Model` 前再转为 GBK。

## 6. C++ 对外类型

共享结果语义、JSON 字段语义、Flow 模块分类、模板对象语义和计时口径见 [模块、流程与模型推理标准文档](模块、流程与模型推理标准文档.md)。

### 6.1 对外类型名

| 类型 | 当前字段 |
| --- | --- |
| `ObjectResult` | `categoryId`、`categoryName`、`score`、`area`、`bbox`、`withMask`、`mask`、`withBbox`、`withAngle`、`angle` |
| `SampleResult` | `results` |
| `Result` | `sampleResults` |
| `FlowNodeTiming` | `nodeId`、`nodeType`、`nodeTitle`、`elapsedMs` |

以上为 C++ API 对外结构体字段名。C++ 公共成员命名使用 `camelCase`。

### 6.2 Flow 聚合结构

`FlowResultItem`、`FlowByImageEntry`、`FlowFrontendPayload`、`FlowFrontendByNodePayload`、`FlowBatchResult` 用于 Flow 结果聚合，属于 C++ 侧内部承载结构。

## 7. `Model`

### 7.1 公开面

`Model` 暴露字段 `modelIndex`、`OwnModelIndex`；公开构造为默认构造、`Model(const std::string&, int)`、`Model(const std::wstring&, int)`；禁用拷贝、支持移动；公开成员函数为 `FreeModel()`、`GetModelInfo()`、`Infer()`、`InferBatch()`、`InferOneOutJson()`、`GetLastInferTiming()`、`GetLastFlowNodeTimings()`。

### 7.2 加载、释放与信息查询

`.dvst/.dvso/.dvsp` 进入 FlowGraph 模式，其余走底层 `dlcv_infer.dll` 普通模型模式。普通模型通过 `dlcv_load_model` 加载；FlowGraph 模式创建 `flow::FlowGraphModel` 并完成归档解包后再加载。`FreeModel()` 会按 `OwnModelIndex` 决定释放底层资源还是仅清空索引；`GetModelInfo()` 在普通模式直接返回底层 JSON，在 FlowGraph 模式返回流程根对象，并附加 `loaded_model_meta` 与按模型文件名索引的 `model_info`。

### 7.3 推理前图像规整

`prepareInferInputBatch()` 会先从模型信息推断目标通道数，并用 `_expectedChCache` 缓存结果。当前入口会先统一位深到 `CV_8U`，再按模型输入做最小必要的通道规整：三通道模型会把单通道输入补成 `RGB`，单通道模型会把三/四通道输入压成灰度；接口不负责 `BGR/BGRA -> RGB` 颜色顺序整理，三通道颜色图仍由调用方先按 RGB 送入。

### 7.4 推理、结果与计时

普通模型请求固定组装 `model_index + image_list` 后调用底层推理，`code!=0` 时抛异常。结构化包装阶段会自动补推断 `with_bbox`、`with_angle`，并对 `mask` 做 `clone()`、必要时缩放或反推框。`InferOneOutJson()` 只返回首张图结果；最近一次计时保存在线程局部变量中，FlowGraph 模式优先使用流程返回的 `timing`。

## 8. `Utils`

`Utils` 的公开静态函数包括 `JsonToString()`、`FreeAllModels()`、`GetDeviceInfo()`、`OcrInfer()`、`GetGpuInfo()`、`KeepMaxClock()` 和 5 个 NVML 包装函数。其行为分别是：`JsonToString()` 使用 `dump(4)`；`FreeAllModels()` 直接调用 `dlcv_free_all_models`；`GetDeviceInfo()` 直接调用 `dlcv_get_device_info`；`KeepMaxClock()` 仅在底层导出 `dlcv_keep_max_clock` 时调用；`OcrInfer()` 先用检测模型跑整图，再按 `bbox` 裁 ROI 给识别模型，若识别结果存在，则用第 1 条识别结果的 `categoryName` 覆盖检测结果的 `categoryName`；`GetGpuInfo()` 成功时返回 `{code:0,message:"Success",devices:[{device_id,device_name}]}`，NVML 初始化失败时返回 `code=1`，获取设备数量失败时返回 `code=2`。

## 9. `sntl_admin`

公开类型为 `SntlAdminStatus`、`SNTLDllLoader`、`SNTLUtils`、`SNTL` 和 `ParseXmlToJson()`。固定 XML 常量中，`DefaultScope` 的厂商 ID 固定为 `26146`，`HaspIdFormat` 读取 `haspid`，`FeatureIdFormat` 读取 `featureid` 与 `haspid`。`SNTL` 构造时调用 `sntl_admin_context_new`，析构时调用 `Dispose()`，`Dispose()` 再调 `sntl_admin_context_delete`；`Get()` 调 `sntl_admin_get`，成功时返回 `{ "code": 0, "message": "成功", "data": ... }`，失败时返回 `{ "code": <status>, "message": "<状态描述>" }`。`SNTLUtils::GetDeviceList()` 返回加密狗 ID 数组，`GetFeatureList()` 返回特性 ID 数组，任一异常都返回空数组 `[]`。

## 10. Flow 与 DVS 的 C++ 实现

### 10.1 DVS 归档加载

共享的 Flow 与归档语义见 [模块、流程与模型推理标准文档](模块、流程与模型推理标准文档.md)。C++ 侧额外处理 DVS 归档解包、`pipeline.json` 中 `model_path` 重写，以及临时目录清理。

### 10.2 `FlowGraphModel`

`FlowGraphModel` 公开接口为 `IsLoaded()`、`Load()`、`GetModelInfo()`、`InferOneOutJson()`、`InferInternal()`、`Benchmark()`，禁用拷贝、支持移动。`Load()` 从 UTF-8 流程 JSON 读取 `nodes` 并只预加载 `model/*` 节点；`InferInternal()` 在上下文中写入前端图像、设备和参数后返回 `result_list` 与 `timing`；清理阶段只清 `ModelPool`，不调用 `Utils::FreeAllModels()`。

### 10.3 `ExecutionContext`

`ExecutionContext` 是轻量键值容器，公开 `Set<T>()`、`Get<T>()`、`Has()`、`Remove()`、`Clear()`；内部用 `shared_ptr<IValue>` 持有值，拷贝时做深拷贝。

### 10.4 `GraphExecutor`

`GraphExecutor` 负责节点排序、链路路由、标量注入、`infer_params` 属性覆盖和 `model/*` 预加载。未注册普通节点会跳过，未注册模型节点会记录到加载报告；当前节点输出链路还会写入 `__graph_current_output_mask` 供部分模块读取。

### 10.5 Flow 结果聚合

聚合读取优先级为 `frontend_payloads_by_node -> frontend_json.by_node -> frontend_json_by_node -> frontend_json.last -> frontend_payload_last`。单图时 `result_list` 直接是结果数组，多图时为 `[{ "result_list": [...] }, ...]`。

### 10.6 已注册 Flow 节点

C++ Flow 节点实现位于 `flow/modules/InputModules.cpp`、`flow/modules/ModelModules.cpp`、`flow/modules/OutputModules.cpp`、`flow/modules/SlidingModules.cpp`、`flow/modules/FeatureModules.cpp`、`flow/modules/PostProcessModules.cpp`、`flow/modules/RegionStrokeVisualizeTemplateModules.cpp`。当前注册集覆盖输入、模型、预处理/特征、后处理、输出与模板模块；`features/printed_template_match` 由 `features/template_match` 兼容实现。当前实现中，`input/*` 从磁盘读图时会把三/四通道输入统一整理为 RGB 语义后再入 Flow，`model/*` 入口不再隐式执行 BGR→RGB 转换，但仍会按模型输入做最小必要的通道规整，`output/save_image` 按内部固定 RGB 语义把三通道/四通道图像转换回 OpenCV 写盘所需的 BGR 语义。

## 11. 仅 DLL 构建内部使用的类型

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
