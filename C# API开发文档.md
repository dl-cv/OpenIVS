# C# API开发文档

## 项目概览

`DlcvCsharpApi` 是一个 .NET Framework 4.7.2 的 C# 类库工程，`OutputType` 为 `Library`，程序集名称为 `DlcvCsharpApi`，默认生成 `DlcvCsharpApi.dll`。项目定义了 `Debug|x64` 与 `Release|x64` 两个主要构建配置，`PlatformTarget` 为 `x64`，`LangVersion` 为 `7.3`。程序集版本为 `1.0.0.0`，`ComVisible` 为 `false`。

当前仓库内可通过以下命令生成发布版 DLL：

```bash
dotnet build DlcvCsharpApi.csproj -c Release -p:Platform=x64
```

发布版输出路径为 `DlcvCsharpApi\bin\x64\Release\DlcvCsharpApi.dll`。

代码中实际使用的命名空间包括：

- `dlcv_infer_csharp`
- `DlcvModules`
- `sntl_admin_csharp`

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
| `dlcv_infer.dll` | 优先按系统搜索路径加载，失败后回退到 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll` |
| `dlcv_infer2.dll` | 仅当授权特征列表包含字符串 `"2"` 时启用；回退路径为 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer2.dll` |
| `sntl_adminapi_windows_x64.dll` | 优先按系统搜索路径加载，失败后回退到 `C:\dlcv\bin\sntl_adminapi_windows_x64.dll` |
| `nvml.dll` | `Utils.GetGpuInfo()` 通过 `DllImport` 直接调用 |
| `DLCV Test.exe` | `Model` 的 DVP 模式固定从 `C:\dlcv\Lib\site-packages\dlcv_test\DLCV Test.exe` 启动后端服务 |
| `AIModelRPC.exe` | `Model` 的 RPC 模式优先从当前 AppDomain 目录查找，其次查找 `C:\dlcv\Lib\site-packages\dlcvpro_infer_csharp\AIModelRPC.exe` |

### 运行模式相关组件

| 模式 | 触发条件 | 当前实现中的通信方式 |
| --- | --- | --- |
| DVT | 文件后缀既不是 `.dvp`，也不是 `.dvst`/`.dvso`/`.dvsp`，且 `rpc_mode=false` | `dlcv_infer.dll` / `dlcv_infer2.dll` 导出的 C 接口 |
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

## 公共结果数据结构

### `Utils.CSharpObjectResult`

| 字段 | 类型 | 当前语义 |
| --- | --- | --- |
| `CategoryId` | `int` | 类别 ID |
| `CategoryName` | `string` | 类别名称 |
| `Score` | `float` | 置信度 |
| `Area` | `float` | 面积 |
| `WithBbox` | `bool` | 是否包含框 |
| `Bbox` | `List<double>` | 普通框为 `[x, y, w, h]`；旋转框为 `[cx, cy, w, h]` |
| `WithMask` | `bool` | 是否包含 mask |
| `Mask` | `Mat` | 单通道 8 位图；0 表示非目标像素，255 表示目标像素 |
| `Polyline` | `List<Point2d>` | 开放折线点集；坐标语义与当前结果中的 `bbox` / `poly` 保持一致 |
| `WithAngle` | `bool` | 是否包含角度 |
| `Angle` | `float` | 旋转框角度，弧度制；无角度时为 `-100` |

`Mask` 的尺寸与 `Bbox` 的宽高语义一致。`ToString()` 会输出类别名称、百分制分数、面积，并在存在时追加角度、框、mask 尺寸与折线点数量。

### `Utils.CSharpSampleResult`

`CSharpSampleResult` 表示单张图片的结果，唯一字段为：

- `Results`：`List<CSharpObjectResult>`

`ToString()` 逐条拼接 `CSharpObjectResult.ToString()` 的输出。

### `Utils.CSharpResult`

`CSharpResult` 表示批量结果，唯一字段为：

- `SampleResults`：`List<CSharpSampleResult>`

列表长度与输入图片数量一一对应。

## 核心模型类

### `Model`

`Model` 是基础推理封装类，实现了 `IDisposable`。

#### 构造与公共成员

| 成员 | 当前实现 |
| --- | --- |
| `Model()` | 空构造，不加载模型 |
| `Model(string modelPath, int device_id, bool rpc_mode = false, bool enableCache = false)` | 按路径后缀与 `rpc_mode` 选择运行模式并加载模型 |
| `EnableConsoleLog` | 静态属性，默认 `true`；控制内部 `Console.WriteLine` 日志 |
| `modelIndex` | 公共字段；DVT 模式使用底层返回的 `model_index`，DVP/DVS/RPC 模式加载成功后置为 `1` |
| `OwnModelIndex` | 公共属性，默认 `true`；为 `false` 时 `FreeModel()` 与 `Dispose()` 不释放底层模型，只将 `modelIndex` 置为 `-1` |
| `IsDvpMode` | 只读属性，返回当前是否为 DVP 模式 |

#### 模型加载规则

- `.dvp` 后缀进入 DVP 模式。
- `.dvst`、`.dvso`、`.dvsp` 后缀进入 DVS 模式。
- 其余后缀在 `rpc_mode=true` 时进入 RPC 模式。
- 其余情况进入 DVT 模式。

#### 模型缓存

当 `enableCache=true` 时，缓存键为：

- 模型绝对路径的小写规范化值
- `device_id`
- 运行模式标识（`dvp` / `dvs` / `rpc` / `dvt`）

缓存命中后直接复用 `modelIndex`。`ClearModelCache()` 清空静态模型缓存与加载中集合。

#### 公共方法

| 方法 | 返回值 | 当前行为 |
| --- | --- | --- |
| `GetResolvedSubModelBatchItems()` | `List<JObject>` | 返回当前缓存的子模型批量信息列表，每项包含 `name`、`max_shape`、`max_batch_size` |
| `FreeModel()` | `void` | 按当前模式释放模型；DVP 走 HTTP，DVS 释放 `DvsModel`，RPC 走管道，DVT 调用 `dlcv_free_model` |
| `GetCachedModelInfo()` | `JObject` | 返回当前缓存的模型信息深拷贝 |
| `GetCachedMaxShape()` | `JArray` | 返回当前缓存的 `max_shape` 深拷贝 |
| `GetMaxBatchSize()` | `int` | 返回解析后的最大 batch，大于等于 `1` |
| `GetModelInfo()` | `JObject` | 按当前模式获取模型信息；若 `task_type` 为 `OCR`，会移除 `character`、`dict`、`classes` 字段 |
| `Infer(Mat image, JObject params_json = null)` | `Utils.CSharpResult` | 单张推理，返回结构化结果 |
| `InferBatch(List<Mat> image_list, JObject params_json = null)` | `Utils.CSharpResult` | 多张推理，返回结构化结果 |
| `InferOneOutJson(Mat image, JObject params_json = null)` | `dynamic` | 单张推理，返回 JSON 数组 |
| `Dispose()` | `void` | 调用 `FreeModel()` 并释放托管资源 |
| `ClearModelCache()` | `void` | 清空静态缓存 |

#### 图像通道处理

`Model` 在推理前会根据模型信息中解析出的输入通道数执行图像归一化：

- 期望 3 通道时：
  - 灰度图转 `RGB`
  - `BGRA` 转 `RGB`
  - `BGR` 转 `RGB`
- 期望 1 通道时：
  - `BGR` 转灰度
  - `BGRA` 转灰度

16 位、浮点、带符号整型深度会先转换为 8 位深度。

#### 模式差异

| 模式 | 当前行为 |
| --- | --- |
| DVT | 通过 `dlcv_load_model`、`dlcv_get_model_info`、`dlcv_infer`、`dlcv_free_model_result`、`dlcv_free_model` 工作 |
| DVP | 自动检查后端服务；服务不可用时启动 `DLCV Test.exe --keep_alive`；推理请求固定附带 `return_polygon=true` |
| DVS | 内部创建 `DlcvModules.DvsModel`；`GetModelInfo()` 返回流程 JSON，并附加 `loaded_model_meta` |
| RPC | 自动启动 `AIModelRPC.exe`；图像通过共享内存传输；结果中的 mask 可通过共享内存回读 |

### `SlidingWindowModel`

`SlidingWindowModel` 继承 `Model`，构造函数为：

```csharp
SlidingWindowModel(
    string modelPath,
    int device_id,
    int small_img_width = 832,
    int small_img_height = 704,
    int horizontal_overlap = 16,
    int vertical_overlap = 16,
    float threshold = 0.5f,
    float iou_threshold = 0.2f,
    float combine_ios_threshold = 0.2f)
```

该类直接向 `dlcv_load_model` 传入 `type = "sliding_window_pipeline"` 的 JSON 配置，加载成功后从返回 JSON 中读取 `model_index`。

### `OcrWithDetModel`

`OcrWithDetModel` 是由一个检测模型和一个 OCR 识别模型组成的组合封装，实现了 `IDisposable`。

#### 状态属性

| 属性 | 当前语义 |
| --- | --- |
| `IsDetModelLoaded` | 检测模型是否已加载 |
| `IsOcrModelLoaded` | OCR 模型是否已加载 |
| `IsLoaded` | 两个模型是否均已加载 |

#### 公共方法

| 方法 | 返回值 | 当前行为 |
| --- | --- | --- |
| `SetHorizontalScale(float scale)` | `void` | 设置 OCR 裁剪图的水平缩放倍率 |
| `GetHorizontalScale()` | `float` | 返回当前水平缩放倍率 |
| `Load(string detModelPath, string ocrModelPath, int deviceId = 0, bool enableCache = false)` | `void` | 依次加载检测模型与 OCR 模型；任一失败时释放已加载模型 |
| `GetModelInfo()` | `JObject` | 返回 `det_model` 与 `ocr_model` 两部分信息；单边失败时对应节点返回 `error` |
| `Infer(Mat image, JObject paramsJson = null)` | `Utils.CSharpResult` | 单张 OCR 推理 |
| `InferBatch(List<Mat> imageList, JObject paramsJson = null)` | `Utils.CSharpResult` | 批量 OCR 推理 |
| `InferOneOutJson(Mat image, JObject params_json = null)` | `dynamic` | 返回单张图片的 JSON 结果数组 |
| `FreeModel()` | `void` | 释放两个底层 `Model` |
| `GetDetModelInfo()` | `JObject` | 返回检测模型信息 |
| `GetOcrModelInfo()` | `JObject` | 返回 OCR 模型信息 |
| `Dispose()` | `void` | 释放两个底层 `Model` |

#### 推理行为

- 先执行检测模型推理。
- 对每个检测结果裁剪 ROI。
- 旋转框通过 `WarpAffine` 直接裁剪到 `[w, h]` 尺寸。
- 当水平缩放倍率不为 `1.0` 时，仅对裁剪图做水平缩放。
- OCR 模型结果的首个类别名称覆盖检测结果中的 `category_name`。
- 结构化返回结果中的 `bbox`、`with_bbox`、`with_mask`、`with_angle`、`angle` 继承检测模型结果。

### `FlowGraphModel`

`FlowGraphModel` 是流程图推理封装类，实现了 `IDisposable`。

#### 公共方法

| 方法 | 返回值 | 当前行为 |
| --- | --- | --- |
| `Load(string flowJsonPath, int deviceId = 0)` | `JObject` | 从流程 JSON 文件加载流程图并初始化图内模型 |
| `GetLoadedModelMeta()` | `JArray` | 返回加载阶段收集到的模型元信息 |
| `GetModelInfo()` | `JObject` | 返回流程 JSON 根对象 |
| `Infer(Mat image, JObject paramsJson = null)` | `Utils.CSharpResult` | 单张流程图推理 |
| `InferBatch(List<Mat> imageList, JObject paramsJson = null)` | `Utils.CSharpResult` | 批量流程图推理 |
| `InferOneOutJson(Mat image, JObject paramsJson = null)` | `dynamic` | 返回单张图片对应的流程 `result_list` |
| `Benchmark(Mat image, int warmup = 1, int runs = 10)` | `double` | 先预热再重复推理，返回平均毫秒数 |
| `Dispose()` | `void` | 仅设置内部释放标志 |

#### 输入约定

`FlowGraphModel` 在执行流程时向 `ExecutionContext` 写入以下固定键：

- `frontend_image_mat`
- `frontend_image_mats`
- `frontend_image_mat_list`
- `frontend_image_color_space = "rgb"`
- `frontend_image_path = ""`
- `device_id`
- `return_json_emit_poly`

流程入口图像按 RGB 语义透传，不在 `FlowGraphModel` 内部整图转换为 BGR。

#### 返回行为

- 单图时 `InferInternal()` 的 `result_list` 直接是结果数组。
- 多图时 `result_list` 为容器数组，每项形如 `{ "result_list": [...] }`。
- `InferBatch()` 会把 `result_list` 转成 `Utils.CSharpResult`。
- `InferOneOutJson()` 保留流程 JSON 输出中的 `poly` 语义。

### `DvsModel`

`DvsModel` 继承 `FlowGraphModel`，用于加载 `.dvst`、`.dvso`、`.dvsp` 文件。

当前实现中的加载流程为：

1. 校验文件头 `DV\n`
2. 读取第二行 JSON 头
3. 从 `file_list` 与 `file_size` 逐个解包文件
4. 将 `pipeline.json` 读入内存
5. 其他文件写入临时目录
6. 将流程中各节点的 `model_path` 重写到临时文件路径
7. 调用 `LoadFromRoot()` 继续完成流程图加载
8. 在 `finally` 中删除临时目录

流程节点中会为原始模型路径额外写入：

- `model_path_original`
- `model_name`

## JSON 输出与结构化输出约定

### `Model` / `OcrWithDetModel` 的结构化输出

`Infer()` 与 `InferBatch()` 返回 `Utils.CSharpResult`，其中：

- `SampleResults` 长度等于输入图像数量
- 每个检测对象都映射为 `CSharpObjectResult`
- DVP 模式下，如果原始 `bbox` 为 `[x1, y1, x2, y2]`，会转为 `[x, y, w, h]`
- DVP 模式下，如果 `bbox` 缺失但存在 `polygon`，会由 `polygon` 计算轴对齐框
- DVP 模式下 `with_mask=true` 且存在 `polygon` 时，会根据 `polygon` 与 `bbox` 生成局部 `Mask`
- DVT / RPC 模式下，`mask` 优先从共享内存读取，其次从 `mask_ptr` 读取，再按 `bbox` 尺寸缩放到局部 mask
- `polyline` 支持两种 JSON 形式：
  - `[[x, y], ...]`
  - `[{ "x": x, "y": y }, ...]`

### `Model.InferOneOutJson()`

返回单张图片的 JSON 数组。每个结果对象当前统一为以下字段集合：

- `category_id`
- `category_name`
- `score`
- `bbox`
- `with_bbox`
- `area`
- `angle`
- `with_angle`
- `mask`
- `with_mask`
- `polyline`（存在折线时输出）

`mask` 的输出规则：

- DVP 模式：`polygon` 转为点对象数组 `[{ "x": ..., "y": ... }, ...]`
- DVS 模式：`poly` 的首个轮廓转为点对象数组
- DVT / RPC 模式：若存在有效 `mask_ptr`，会从 mask 图像提取轮廓并输出点对象数组
- 无 mask 时固定输出 `{ "height": -1, "mask_ptr": 0, "width": -1 }`

### `FlowGraphModel.InferOneOutJson()`

`FlowGraphModel.InferOneOutJson()` 直接返回流程 `result_list` 的单图结果数组，不再经过 `Model.StandardizeJsonOutput()` 标准化。流程输出可能包含以下字段：

- `category_id`
- `category_name`
- `score`
- `bbox`
- `area`
- `with_bbox`
- `with_mask`
- `with_angle`
- `angle`
- `poly`
- `polyline`
- `metadata`
- `mask_rle`

### 坐标语义

| 场景 | 当前语义 |
| --- | --- |
| 普通结构化框 | `[x, y, w, h]` |
| 旋转结构化框 | `Bbox = [cx, cy, w, h]`，角度在 `Angle` |
| 流程 `ReturnJson` 的轴对齐框 | `[x1, y1, x2, y2]` 或转换后的 `xywh`，取决于调用路径 |
| 流程 `ReturnJson` 的旋转框 | 5 元组 `[cx, cy, w, h, angle]` |
| `mask_rle` | RLE 编码 mask 信息 |
| `poly` | 多边形轮廓列表 |
| `polyline` | 开放折线列表 |

## 工具类与辅助 API

### `Utils`

`Utils` 是 `partial class`，当前公开方法如下：

| 方法 | 返回值 | 当前行为 |
| --- | --- | --- |
| `jsonToString(JObject json)` | `string` | 以 `EscapeNonAscii` 方式序列化对象 |
| `jsonToString(JArray json)` | `string` | 以 `EscapeNonAscii` 方式序列化数组 |
| `ConvertToVisualizeFormat(CSharpResult result)` | `JArray` | 把结构化结果转成 `VisualizeOnOriginal` 使用的 `sample_results` JSON |
| `VisualizeResults(List<Mat> images, CSharpResult result, Dictionary<string, object> properties = null)` | `List<Mat>` | 构造 `ModuleImage`，调用 `VisualizeOnOriginal` 返回绘制后的图像 |
| `FreeAllModels()` | `void` | 调用底层 `dlcv_free_all_models()` |
| `GetDeviceInfo()` | `JObject` | 优先调用导出的 `dlcv_get_gpu_info`，否则调用 `dlcv_get_device_info` |
| `OcrInfer(Model detectModel, Model recognizeModel, Mat image)` | `CSharpResult` | 先检测，再对 ROI 做识别，用识别结果的首个类别名覆盖检测结果 |
| `GetGpuInfo()` | `JObject` | 通过 NVML 枚举 GPU，返回 `code`、`message`、`devices` |

`ConvertToVisualizeFormat()` 在结构化结果中仅序列化：

- `category_id`
- `category_name`
- `score`
- `bbox`
- `with_angle`
- `angle`
- `with_mask`
- `mask_rle`

### `DllLoader`

`DllLoader` 是单例类，`Instance` 首次访问时加载原生 DLL，并绑定以下委托：

- `dlcv_load_model`
- `dlcv_free_model`
- `dlcv_get_model_info`
- `dlcv_infer`
- `dlcv_free_model_result`
- `dlcv_free_result`
- `dlcv_free_all_models`
- `dlcv_get_device_info`
- `dlcv_get_gpu_info`
- `dlcv_keep_max_clock`

授权特征列表中包含字符串 `"2"` 时，优先改用 `dlcv_infer2.dll`。

### `sntl_admin_csharp`

当前公开类型包括：

- `SntlAdminStatus`
- `SNTLDllLoader`
- `SNTL`
- `SNTLUtils`

`SNTL` 的公开方法包括：

- `Get(string scope, string format)`
- `GetSntlInfo()`
- `GetDeviceList()`
- `GetFeatureList()`
- `Dispose()`

`SNTLUtils` 的公开静态方法包括：

- `GetDeviceList()`
- `GetFeatureList()`

`DllLoader` 通过 `SNTL.GetFeatureList()` 读取授权特征列表，以决定是否切换到 `dlcv_infer2.dll`。

## 流程图执行框架

### 基础运行时类型

| 类型 | 当前职责 |
| --- | --- |
| `ExecutionContext` | 区分大小写不敏感的运行时键值容器；公开 `Get<T>()` 与 `Set()` |
| `ModuleRegistry` | 维护 `moduleType -> Type` 映射；公开 `Register()` 与 `Get()` |
| `GlobalDebug` | 控制是否打印调试日志；公开 `PrintDebug` 与 `Log()` |
| `InferTiming` | 记录流程请求耗时与底层推理耗时 |
| `TransformationState` | 保存原图尺寸、裁剪框、仿射矩阵、输出尺寸，并支持克隆与矩阵运算 |
| `ModuleImage` | 将 `Mat`、原图、变换状态、原图序号、滑窗元数据打包 |
| `ModuleIO` | 一次模块调用的图像、结果、模板输出容器 |
| `ModuleChannel` | 图像、结果、模板三类通道的中间容器 |

### 模块基类

| 类型 | 当前行为 |
| --- | --- |
| `BaseModule` | 保存节点 ID、标题、属性、上下文、扩展输入、扩展输出、模板列表、标量输入输出；默认 `Process()` 返回空图像与空结果 |
| `BaseInputModule` | 继承 `BaseModule`；`Process()` 固定调用 `Generate()` |

### `GraphExecutor`

`GraphExecutor` 的构造函数为：

```csharp
GraphExecutor(List<Dictionary<string, object>> nodes, ExecutionContext context)
```

当前执行机制如下：

1. 对所有非抽象 `BaseModule` 子类执行静态构造，触发 `ModuleRegistry.Register()`
2. 读取节点的 `id`、`type`、`title`、`order`、`properties`、`inputs`、`outputs`
3. 先按 `order`，再按 `id` 排序
4. 通过 `outputs[*].links` 建立 `linkId -> (源节点, 源输出端口)` 映射
5. 实例化模块并填入 `NodeId`、`Title`、`Properties`、`Context`
6. 每两个输入端口为一组：
   - 第 0 组为主通道
   - 第 1 组及以后写入 `ExtraInputsIn`
7. 标量端口从上游 `scalars` 读取，写入：
   - `ScalarInputsByIndex`
   - `ScalarInputsByName`
8. 调用 `Process(mainImages, mainResults)`
9. 将主输出写回：
   - `image_list`
   - `result_list`
   - `template_list`
10. 若节点输出端口声明为标量类型，则从 `ScalarOutputsByName` 写回 `scalars`

`NormalizeBboxProperties()` 会在节点属性中存在 `bbox_x1`、`bbox_y1`、`bbox_x2`、`bbox_y2` 且未显式给出 `bbox_x`、`bbox_y`、`bbox_w`、`bbox_h` 时，自动补齐 `xywh`。

### 流程加载

`GraphExecutor.LoadModels()` 仅对 `BaseModelModule` 实例调用 `LoadModel()`，并返回加载报告 JSON。每个模型节点的加载元信息会追加到 `ExecutionContext` 的 `loaded_model_meta` 列表中。

## 模块注册表

### 输入模块

| 注册类型 | 类名 | 当前行为 |
| --- | --- | --- |
| `input/image` | `InputImage` | 优先从 `ExecutionContext` 读取 `frontend_image_mats` / `frontend_image_mat_list` / `frontend_image_mat`；否则按 `path` / `paths` 读盘；`ImRead` 使用 `Color` 模式，得到 BGR 图像 |
| `input/frontend_image` | `InputFrontendImage` | 优先读取上下文中的前端图像；否则按 `path` 或 `frontend_image_path` 读盘 |
| `input/build_results` | `InputBuildResults` | 生成默认图像或复用输入图，并按节点属性组装一条 `sample_results` |

### 模型模块

| 注册类型 | 类名 | 当前行为 |
| --- | --- | --- |
| `model/det` | `DetModel` | 对图像按形状分桶、按面积排序、按批次调用 `_model.InferBatch()` |
| `model/rotated_bbox` | `RotatedBBoxModel` | 继承 `DetModel`，无额外重写 |
| `model/instance_seg` | `InstanceSegModel` | 继承 `DetModel`，无额外重写 |
| `model/semantic_seg` | `SemanticSegModel` | 继承 `DetModel`，无额外重写 |
| `model/cls` | `ClsModel` | 在 `DetModel` 基础上按 `top_k` 裁剪结果，并在缺框时补整图框 |
| `model/ocr` | `OCRModel` | 在 `DetModel` 基础上为缺框项补整图框 |

### 输出模块

| 注册类型 | 类名 | 当前行为 |
| --- | --- | --- |
| `output/save_image` | `SaveImage` | 将当前图像写入磁盘；默认后缀 `_out`，默认格式 `png` |
| `output/preview` | `Preview` | 透传图像与结果 |
| `output/return_json` | `ReturnJson` | 将检测结果写入 `ExecutionContext.frontend_json`，并维护 `last` 与 `by_node` |
| `output/visualize` | `VisualizeOnOriginal` | 在原图坐标系上绘制框、mask、轮廓和文字 |
| `output/visualize_local` | `VisualizeOnLocal` | 在当前图像上直接绘制绿色矩形框 |

### 预处理、特征处理与后处理模块

| 注册类型 | 类名 | 当前行为 |
| --- | --- | --- |
| `pre_process/sliding_window` | `SlidingWindow` | 按窗口尺寸与重叠率切图 |
| `features/sliding_window` | `SlidingWindow` | 同上 |
| `pre_process/sliding_merge` | `SlidingMergeResults` | 合并滑窗结果 |
| `features/sliding_merge` | `SlidingMergeResults` | 同上 |
| `features/image_generation` | `ImageGeneration` | 按检测结果裁剪生成子图 |
| `features/image_flip` | `ImageFlip` | 对图像做水平或竖直翻转并更新变换状态 |
| `post_process/mask_to_rbox` | `MaskToRBox` | 由 mask 计算旋转框 |
| `features/mask_to_rbox` | `MaskToRBox` | 同上 |
| `post_process/merge_results` | `MergeResults` | 合并主路与扩展路结果 |
| `features/merge_results` | `MergeResults` | 同上 |
| `post_process/result_filter` | `ResultFilter` | 按类别拆分结果，未命中项进入 `ExtraOutputs[0]` |
| `features/result_filter` | `ResultFilter` | 同上 |
| `post_process/result_filter_advanced` | `ResultFilterAdvanced` | 按 bbox、rbox、bbox area、mask area 过滤 |
| `features/result_filter_advanced` | `ResultFilterAdvanced` | 同上 |
| `pre_process/coordinate_crop` | `CoordinateCrop` | 按 `x,y,w,h` 裁剪图像并派生子变换 |
| `features/coordinate_crop` | `CoordinateCrop` | 同上 |
| `pre_process/image_rescale` | `ImageRescale` | 按比例缩放图像并更新变换状态 |
| `features/image_rescale` | `ImageRescale` | 同上 |
| `features/image_rotate_by_cls` | `ImageRotateByClassification` | 根据分类标签将图像旋转 0/90/180/270 度，并同步更新结果几何 |
| `post_process/text_replacement` | `TextReplacement` | 按 `mapping` 替换 `category_name` |
| `features/text_replacement` | `TextReplacement` | 同上 |
| `post_process/rbox_correction` | `RBoxCorrection` | 根据参考角度回正图像并同步结果几何 |
| `features/rbox_correction` | `RBoxCorrection` | 同上 |
| `post_process/result_label_merge` | `ResultLabelMerge` | 用主路标签与第二路标签拼接新类别名 |
| `features/result_label_merge` | `ResultLabelMerge` | 同上 |
| `post_process/bbox_iou_dedup` | `BBoxIoUDedup` | 对轴对齐框按 IoU 或 IoS 去重 |
| `features/bbox_iou_dedup` | `BBoxIoUDedup` | 同上 |
| `post_process/result_filter_region` | `ResultFilterRegion` | 按 ROI 判断检测是否落在区域内 |
| `features/result_filter_region` | `ResultFilterRegion` | 同上 |
| `post_process/result_filter_region_global` | `ResultFilterRegionGlobal` | 按原图坐标判定区域过滤，输出坐标系不改为原图 |
| `features/result_filter_region_global` | `ResultFilterRegionGlobal` | 同上 |
| `post_process/result_category_override` | `ResultCategoryOverride` | 用第二路结果覆盖主路已有字符串类别名 |
| `features/result_category_override` | `ResultCategoryOverride` | 同上 |
| `post_process/poly_filter` | `PolyFilter` | 从 polygon / mask 提取上沿或下沿折线并写回 `polyline` |
| `features/poly_filter` | `PolyFilter` | 同上 |
| `features/stroke_to_points` | `StrokeToPoints` | 从 mask 沿笔画方向生成等间距点框 |
| `features/template_from_results` | `TemplateFromResults` | 从 OCR 结果构建 `SimpleTemplate` |
| `features/template_save` | `TemplateSave` | 将模板写为 JSON，可选同时写 PNG |
| `features/template_load` | `TemplateLoad` | 从 JSON 读取模板 |
| `features/template_match` | `TemplateMatch` | 比较主模板与第二路模板，输出 `ok` 与 `detail` |
| `features/printed_template_match` | `PrintedTemplateMatch` | 按产品类型组织模板生成、保存、加载与匹配流程 |

## 主要模块行为补充

### `ReturnJson`

`ReturnJson` 会将当前节点的结果按图片聚合为：

```json
{
  "by_image": [
    {
      "origin_index": 0,
      "original_size": { "width": 0, "height": 0 },
      "results": []
    }
  ]
}
```

聚合结果写入 `ExecutionContext.frontend_json`，并维护：

- `frontend_json["last"]`
- `frontend_json["by_node"][NodeId]`
- `frontend_json_by_node`

当上下文中 `return_json_emit_poly = true` 或节点属性显式要求输出 `poly` 时，结果中保留 `poly`。

### `VisualizeOnOriginal`

`VisualizeOnOriginal` 在原图上绘制：

- `mask_rle` 解码后的填充区域
- 轮廓
- 普通框与旋转框
- GDI+ 文本

其颜色配置输入为 RGB 语义，绘制时转换为 OpenCV 使用的 BGR。

### `SlidingWindow`

`SlidingWindow` 会为每个窗口生成：

- 子图 `ModuleImage`
- 一条 `local` 结果
- `sliding_meta`

`sliding_meta` 包含：

- `grid_x`
- `grid_y`
- `grid_size`
- `win_size`
- `slice_index`
- `x`
- `y`
- `w`
- `h`

### `SlidingMergeResults`

`SlidingMergeResults` 会把窗口内结果映射回原图坐标，并按原图索引输出一张合并后的图像包装与一条 `local` 结果。合并逻辑支持：

- 轴对齐框
- 旋转框
- mask union
- bbox union

### `PolyFilter`

`PolyFilter` 的输出结果会更新：

- `polyline`
- `bbox`
- `metadata.poly_filter_direction`
- `metadata.poly_filter_source`
- `metadata.poly_filter_mode = "boundary_line"`

## 图像与颜色约定

| 场景 | 当前颜色语义 |
| --- | --- |
| `InputImage` / `InputFrontendImage` 从磁盘读取 | BGR |
| `InputBuildResults` 生成默认图像 | BGR |
| `Model` 进入三通道模型推理前 | 转为 RGB |
| `FlowGraphModel` 入口 | 直接按 RGB 语义透传 |
| `SaveImage` 写盘 | 按 BGR 语义写出 |
| `VisualizeOnOriginal` / `VisualizeOnLocal` 绘制 | OpenCV BGR |

## 标量输出

当前模块中显式使用的标量输出包括：

| 模块 | 标量键 |
| --- | --- |
| `InputImage` | `filename` |
| `ResultFilter` | `has_positive` |
| `ResultFilterRegion` | `has_positive` |
| `TemplateMatch` | `ok`、`detail` |
| `PrintedTemplateMatch` | `ok`、`detail` |
| `BBoxIoUDedup` | `kept_count`、`removed_count` |

## 模板数据结构

### `SimpleOcrItem`

字段包括：

- `Text`
- `Polygon`
- `Confidence`
- `CategoryName`

### `SimpleTemplate`

字段包括：

- `TemplateName`
- `ProductName`
- `ProductId`
- `CameraPosition`
- `OCRResults`
- `template_id`

### `SimpleTemplateMatchDetail`

模板匹配详情对象当前用于保存：

- `ocr_results`
- `missing_template_items`
- `deviation_template_items`
- `misjudgment_pairs`
- `template_match_info`

