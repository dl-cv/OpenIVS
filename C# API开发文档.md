# C# API开发文档

## 项目概览

`DlcvCsharpApi` 是一个 .NET Framework 4.7.2 的 C# 类库工程，`OutputType` 为 `Library`，程序集名称为 `DlcvCsharpApi`，默认生成 `DlcvCsharpApi.dll`。项目定义了 `Debug|x64` 与 `Release|x64` 两个主要构建配置，`PlatformTarget` 为 `x64`，`LangVersion` 为 `7.3`。程序集版本为 `1.0.0.0`，`ComVisible` 为 `false`。

本项目的项目级构建与发布前构建验证统一通过 MCP 构建工具执行，解决方案级与项目级入口见 `开发文档.md` 的“统一编译说明”。

发布版输出路径为 `DlcvCsharpApi\bin\x64\Release\DlcvCsharpApi.dll`。

代码中实际使用的命名空间包括：

- `dlcv_infer_csharp`
- `DlcvModules`
- `sntl_admin_csharp`

## 源码目录结构

`DlcvCsharpApi` 当前源码按目录分层组织：

| 目录 | 当前内容 |
| --- | --- |
| `DlcvCsharpApi\` | 普通模型与外部绑定层：`Model.cs`、`Utils.cs`、`DataTypes.cs`、`DllLoader.cs`、`SlidingWindowModel.cs`、`OcrWithDetModel.cs`、`sntl_admin_csharp.cs` |
| `DlcvCsharpApi\flow\` | 流程入口与执行框架：`FlowGraphModel.cs`、`DvsModel.cs`、`GraphExecutor.cs` |
| `DlcvCsharpApi\flow\runtime\` | 流程运行时公共类型：`ExecutionRuntime.cs`、`ModuleRuntime.cs` |
| `DlcvCsharpApi\flow\modules\` | 流程模块实现：`Inputs.cs`、`Models.cs`、`Outputs.cs`、`Features.cs`、`SlidingWindow.cs`、`SlidingMerge.cs`、`PolyFilter.cs`、`ResultFilterRegion.cs`、`ResultCategoryOverride.cs`、`StrokeToPoints.cs`、`Templates.cs`、`Visualize.cs`、`MaskRleUtils.cs` |
| `DlcvCsharpApi\Properties\` | 程序集元数据：`AssemblyInfo.cs` |

## 依赖与运行组件

### NuGet 依赖

| 包名 | 版本 |
| --- | --- |
| `Newtonsoft.Json` | `13.0.3` |
| `OpenCvSharp4` | `4.10.0.20241108` |
| `OpenCvSharp4.Extensions` | `4.10.0.20241108` |
| `OpenCvSharp4.runtime.win` | `4.10.0.20241108` |
| `System.Buffers` | `4.5.1` |
| `System.Memory` | `4.5.5` |
| `System.Numerics.Vectors` | `4.5.0` |
| `System.Runtime.CompilerServices.Unsafe` | `6.0.0` |

### 托管配置

`app.config` 仅包含一条程序集绑定重定向：

- `System.Runtime.CompilerServices.Unsafe` 的 `oldVersion="0.0.0.0-6.0.0.0"` 重定向到 `newVersion="6.0.0.0"`

### 原生组件与固定路径

| 组件 | 当前实现中的加载方式 |
| --- | --- |
| `dlcv_infer.dll` | Sentinel 版本；优先按系统搜索路径加载，失败后回退到 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll` |
| `dlcv_infer_v.dll` | Virbox 版本；加载 DVT/DVO/DVR 模型前读取模型包 `header_json.dog_provider`，当 provider 为 `virbox` 时启用；回退路径为 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer_v.dll` |
| `sntl_adminapi_windows_x64.dll` | 优先按系统搜索路径加载，失败后回退到 `C:\dlcv\bin\sntl_adminapi_windows_x64.dll` |
| `nvml.dll` | `Utils.GetGpuInfo()` 通过 `DllImport` 直接调用 |
| `DLCV Test.exe` | `Model` 的 DVP 模式固定从 `C:\dlcv\Lib\site-packages\dlcv_test\DLCV Test.exe` 启动后端服务 |
| `AIModelRPC.exe` | `Model` 的 RPC 模式优先从当前 AppDomain 目录查找，其次查找 `C:\dlcv\Lib\site-packages\dlcvpro_infer_csharp\AIModelRPC.exe` |

### 运行模式

| 模式 | 触发条件 | 当前实现中的通信方式 |
| --- | --- | --- |
| DVT | 文件后缀既不是 `.dvp`，也不是 `.dvst`/`.dvso`/`.dvsp`，且 `rpc_mode=false` | `dlcv_infer.dll` / `dlcv_infer_v.dll` 导出的 C 接口 |
| DVP | 模型路径后缀为 `.dvp` | HTTP，固定服务地址 `http://127.0.0.1:9890` |
| DVS | 模型路径后缀为 `.dvst`、`.dvso` 或 `.dvsp` | `DvsModel` + `FlowGraphModel` |
| RPC | `rpc_mode=true` 且不属于 DVP/DVS 后缀 | 命名管道 `DlcvModelRpcPipe` + 共享内存 |

### DVP 固定接口

`Model` 在 DVP 模式下使用以下固定接口：

- `GET /docs`
- `GET /version`
- `POST /load_model`
- `POST /get_model_info`
- `POST /api/inference`
- `POST /free_model`

### RPC 固定协议

`Model` 在 RPC 模式下使用命名管道 `DlcvModelRpcPipe` 发送 JSON 指令，已实现的动作包括：

- `ping`
- `load_model`
- `get_model_info`
- `infer`
- `free_model`

图像数据通过共享内存传递，图像共享内存名格式为 `DlcvModelMmf_<token>`，mask 共享内存名格式为 `DlcvModelMask_<token>`。

## C# 对外类型

共享结果语义、JSON 字段语义、模板对象语义和计时口径见 [模块、流程与模型推理标准文档](模块、流程与模型推理标准文档.md)。

### 命名约定

- C# 公共属性使用 `PascalCase`。
- 对外 JSON 字段继续使用标准文档中的 `snake_case` 语义。

### C# 结果类型

| C# 类型 | 对应共享对象 | 当前公开字段 |
| --- | --- | --- |
| `Utils.CSharpObjectResult` | `ObjectResult` | `CategoryId`、`CategoryName`、`Score`、`Area`、`WithBbox`、`Bbox`、`WithAngle`、`Angle`、`WithMask`、`Mask`、`ExtraInfo` |
| `Utils.CSharpSampleResult` | `SampleResult` | `Results` |
| `Utils.CSharpResult` | `Result` | `SampleResults` |

`CSharpObjectResult.ToString()` 输出友好文本。

## 核心类

### `Model`

`Model` 是基础推理封装类，实现了 `IDisposable`。

#### 公开面

`Model` 的公开面围绕四类能力组织：生命周期与状态包括空构造、带参构造、`EnableConsoleLog`、`modelIndex`、`OwnModelIndex`、`IsDvpMode`、`FreeModel()`、`Dispose()`；缓存与批量元信息包括 `GetCachedModelInfo()`、`GetCachedMaxShape()`、`GetMaxBatchSize()`、`GetResolvedSubModelBatchItems()`、`ClearModelCache()`；模型信息查询使用 `GetModelInfo()`；推理入口使用 `Infer()`、`InferBatch()` 与 `InferOneOutJson()`。

#### 模型加载规则

- `.dvp` 后缀进入 DVP 模式。
- `.dvst`、`.dvso`、`.dvsp` 后缀进入 DVS 模式。
- 其余后缀在 `rpc_mode=true` 时进入 RPC 模式。
- 其余情况进入 DVT 模式。

#### 模型缓存

当 `enableCache=true` 时，缓存键由模型绝对路径的小写规范化值、`device_id` 和运行模式标识 `dvp` / `dvs` / `rpc` / `dvt` 组成；命中后直接复用 `modelIndex`，`ClearModelCache()` 会清空静态模型缓存与加载中集合。

#### 当前实现中的模式差异

| 模式 | 当前行为 |
| --- | --- |
| DVT | 通过 `dlcv_load_model`、`dlcv_get_model_info`、`dlcv_infer`、`dlcv_free_model_result`、`dlcv_free_model` 工作 |
| DVP | 自动检查后端服务；服务不可用时启动 `DLCV Test.exe --keep_alive`；推理请求固定附带 `return_polygon=true` |
| DVS | 内部创建 `DlcvModules.DvsModel`；`GetModelInfo()` 返回流程 JSON，并附加 `loaded_model_meta` 与按模型文件名索引的 `model_info` |
| RPC | 自动启动 `AIModelRPC.exe`；图像通过共享内存传输；结果中的 mask 可通过共享内存回读 |

#### 输入与输出

- 推理前会根据模型信息中解析出的输入通道数完成图像归一化：三通道模型统一转为 `RGB`，单通道模型统一转为灰度；输入若为 16 位、浮点或带符号整型深度，会先转换为 8 位深度。
- `Infer()` 与 `InferBatch()` 统一返回 `Utils.CSharpResult`。
- `Model.InferOneOutJson()` 返回单图 JSON 结果数组。
- DVP 模式下可由 `polygon` 反算 `bbox` 和局部 `Mask`。
- DVT / RPC 模式下 `mask` 优先从共享内存或 `mask_ptr` 读取。
- 无 mask 时输出 `{ "height": -1, "mask_ptr": 0, "width": -1 }`。
- 无监督加速模型当前按实例分割结果契约返回：`task_type=实例分割` 且 `origin_task_type=us`。
- 无监督结果按 `bbox + mask` 的实例项进入 C#，当无异常区域时对应样本 `results` 为空数组。

### `SlidingWindowModel`

`SlidingWindowModel` 继承 `Model`，构造参数包括 `modelPath`、`device_id`、窗口宽高、横纵重叠、`threshold`、`iou_threshold`、`combine_ios_threshold`。该类直接向 `dlcv_load_model` 传入 `type = "sliding_window_pipeline"` 的 JSON 配置，加载成功后从返回 JSON 中读取 `model_index`。

### `OcrWithDetModel`

`OcrWithDetModel` 是由一个检测模型和一个 OCR 识别模型组成的组合封装，实现了 `IDisposable`。

#### 公开面

`OcrWithDetModel` 的状态由 `IsDetModelLoaded`、`IsOcrModelLoaded`、`IsLoaded` 表示；配置接口为 `SetHorizontalScale()` 与 `GetHorizontalScale()`；生命周期与加载接口为 `Load()`、`FreeModel()`、`Dispose()`；信息查询接口为 `GetModelInfo()`、`GetDetModelInfo()`、`GetOcrModelInfo()`；推理接口为 `Infer()`、`InferBatch()`、`InferOneOutJson()`。`GetModelInfo()` 返回 `det_model` 与 `ocr_model` 两部分信息，任一侧查询失败时，对应节点返回 `error`。

#### 推理行为

- 先执行检测模型推理。
- 对每个检测结果裁剪 ROI。
- 旋转框通过 `WarpAffine` 直接裁剪到 `[w, h]` 尺寸。
- 当水平缩放倍率不为 `1.0` 时，仅对裁剪图做水平缩放。
- OCR 模型结果的首个类别名称覆盖检测结果中的 `category_name`。
- 结构化返回结果中的 `bbox`、`with_bbox`、`with_mask`、`with_angle`、`angle` 继承检测模型结果。

### `FlowGraphModel`

`FlowGraphModel` 是流程图推理封装类，实现了 `IDisposable`。当前实现文件为 `DlcvCsharpApi\flow\FlowGraphModel.cs`。

共享的 Flow 节点分类、统一输入输出字段、模板对象与计时口径见 [模块、流程与模型推理标准文档](模块、流程与模型推理标准文档.md)。

C# 侧公开接口为 `Load()`、`GetLoadedModelMeta()`、`GetModelInfo()`、`Infer()`、`InferBatch()`、`InferOneOutJson()`、`Benchmark()`、`Dispose()`；执行时会把前端图像、`device_id` 和 `return_json_emit_poly` 写入 `ExecutionContext`，并把 `result_list` 转为结构化结果或 JSON 输出。

### `DvsModel`

`DvsModel` 继承 `FlowGraphModel`，用于加载 `.dvst`、`.dvso`、`.dvsp` 文件。当前实现文件为 `DlcvCsharpApi\flow\DvsModel.cs`。

C# 侧额外处理 `DV\n` 文件头校验、归档解包、`pipeline.json` 中 `model_path` 重写，以及临时目录清理。

## 工具类与辅助 API

### `Utils`

`Utils` 是 `partial class`。公开能力分为五类：JSON 序列化 `jsonToString(JObject)` 与 `jsonToString(JArray)`；`extra_info` 规范化、深拷贝、已知类型解析、折线读写与友好格式化；结构化结果转可视化 JSON 的 `ConvertToVisualizeFormat()` 与直接绘图的 `VisualizeResults()`；底层资源与设备查询的 `FreeAllModels()`、`GetDeviceInfo()`、`GetGpuInfo()`；以及组合式 OCR 推理 `OcrInfer(Model detectModel, Model recognizeModel, Mat image)`。其中 `ConvertToVisualizeFormat()` 把结构化结果中的 `category_id`、`category_name`、`score`、`bbox`、`with_angle`、`angle`、`with_mask`、`mask_rle` 和 `extra_info` 写入可视化 JSON。

### `DllLoader`

`DllLoader` 是 provider-aware 原生入口分发器。`ForProvider(DogProvider)` 按 provider 返回对应 loader：`sentinel` 加载 `dlcv_infer.dll`，`virbox` 加载 `dlcv_infer_v.dll`。

`ForModel(string)` 的加载策略：
- 先通过 `ModelHeaderProviderResolver.TryResolveExplicitProvider` 判断模型头是否**明确指定**了 `dog_provider`。
- 若**明确指定**（`sentinel` 或 `virbox`），则校验对应加密狗是否存在；不存在时抛出异常，不静默 fallback。
- 若**未指定**（旧模型或省略该字段），则调用 `AutoDetectProvider()` 自动检测当前插入的加密狗，按 **Sentinel 优先、Virbox 第二** 的顺序选择 Provider；检测不到任何狗时，默认使用 Sentinel。

`Instance`（兼容旧代码的单例）在首次创建时同样调用 `AutoDetectProvider()`，而非硬编码 Sentinel。

每个 `Model` 实例在加载时绑定自己的 `_dllLoader`，后续 `GetModelInfoDvt`、`InferInternalDvt`、`FreeModel` 都走该 loader。`Utils` 的 `FreeAllModels`、`GetDeviceInfo`、`KeepMaxClock` 遍历所有已创建 loader 执行。

### `sntl_admin_csharp`

`sntl_admin_csharp` 对外提供状态枚举 `SntlAdminStatus`、原生加载器 `SNTLDllLoader`、运行时访问类 `SNTL`、工具类 `SNTLUtils`、`DogProvider` 枚举、`DogInfo` 与 `DogUtils`。`SNTL` 负责建立上下文并提供 `Get()`、`GetSntlInfo()`、`GetDeviceList()`、`GetFeatureList()`、`Dispose()`；`SNTLUtils` 提供静态的 Sentinel 设备列表与特征列表查询，不再自动回退到 Virbox。`Virbox` 提供独立的 Virbox 设备列表与特征列表查询。`DogUtils` 提供 `GetSentinelInfo()`、`GetVirboxInfo()`、`GetAvailableProviders()` 与 `GetAllDogInfo()`，用于同时查询两类加密狗信息。OpenIVS 不解密模型包内 `dlcv.json`。

## Flow 的 C# 实现入口

### 执行框架

`ExecutionContext`、`ModuleRegistry`、`GlobalDebug`、`InferTiming`、`TransformationState`、`ModuleImage`、`ModuleIO`、`ModuleChannel` 位于 `DlcvCsharpApi\flow\runtime\ExecutionRuntime.cs` 与 `DlcvCsharpApi\flow\runtime\ModuleRuntime.cs`。`BaseModule` / `BaseInputModule` 提供模块基类。`GraphExecutor` 位于 `DlcvCsharpApi\flow\GraphExecutor.cs`，负责节点排序、链路路由、标量注入、`NormalizeBboxProperties()` 和模型节点预加载；`LoadModels()` 仅对 `BaseModelModule` 调用 `LoadModel()`，并把加载元信息写入 `ExecutionContext.loaded_model_meta`。

### 模块实现文件

当前模块实现代码位于 `DlcvCsharpApi\flow\modules\`，主要文件包括 `Inputs.cs`、`Models.cs`、`Outputs.cs`、`Features.cs`、`SlidingWindow.cs`、`SlidingMerge.cs`、`PolyFilter.cs`、`ResultFilterRegion.cs`、`ResultCategoryOverride.cs`、`StrokeToPoints.cs`、`Templates.cs`、`Visualize.cs`。

### C# 层补充约定

- `ReturnJson` 会把结果聚合到 `ExecutionContext.frontend_json` 的 `last` 与 `by_node`。
- `VisualizeOnOriginal` 在原图上绘制 mask、轮廓、框和文本。
- `SlidingWindow` 负责切图并写入 `sliding_meta`；`SlidingMergeResults` 负责把结果映射回原图并合并。
- `PolyFilter` 会更新 `extra_info.polyline` 以及相关 `metadata`。
- 读盘和写盘遵循 OpenCV 的 BGR 语义；调用方负责把三/四通道颜色图整理为 RGB；`Model` 与 `FlowGraphModel` 入口会按模型输入自动做最小必要的通道规整，例如把灰度图补成 RGB、或把三/四通道图压成灰度，但不负责 BGR/BGRA 到 RGB 的颜色顺序转换。
- 当前显式使用的标量键包括 `filename`、`has_positive`、`ok`、`detail`、`kept_count`、`removed_count`。
- 模板相关类型为 `SimpleOcrItem`、`SimpleTemplate` 和 `SimpleTemplateMatchDetail`。

