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

`CSharpObjectResult` 把单个目标结果组织成四组信息：识别信息 `CategoryId`、`CategoryName`、`Score`、`Area`；几何信息 `WithBbox`、`Bbox`、`WithAngle`、`Angle`，其中普通框使用 `[x, y, w, h]`，旋转框使用 `[cx, cy, w, h]`，无角度时 `Angle = -100`；区域信息 `WithMask`、`Mask`，`Mask` 为与框宽高语义一致的单通道 8 位图，0 表示背景、255 表示目标；折线信息 `Polyline`，用于保存与当前 `bbox` / `poly` 同坐标系的开放折线。`ToString()` 会输出类别、百分制分数、面积，并在存在时追加角度、框、mask 尺寸与折线点数量。

### `Utils.CSharpSampleResult`

`CSharpSampleResult` 表示单张图片结果，核心字段只有 `Results`，类型为 `List<CSharpObjectResult>`；`ToString()` 逐条拼接其中每个目标结果的文本表示。

### `Utils.CSharpResult`

`CSharpResult` 表示批量结果，核心字段为 `SampleResults`，类型为 `List<CSharpSampleResult>`，长度与输入图片数量一一对应。

## 核心模型类

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

#### 图像通道处理

`Model` 在推理前会根据模型信息中解析出的输入通道数完成图像归一化：三通道模型统一转为 `RGB`，单通道模型统一转为灰度；输入若为 16 位、浮点或带符号整型深度，会先转换为 8 位深度。

#### 模式差异

| 模式 | 当前行为 |
| --- | --- |
| DVT | 通过 `dlcv_load_model`、`dlcv_get_model_info`、`dlcv_infer`、`dlcv_free_model_result`、`dlcv_free_model` 工作 |
| DVP | 自动检查后端服务；服务不可用时启动 `DLCV Test.exe --keep_alive`；推理请求固定附带 `return_polygon=true` |
| DVS | 内部创建 `DlcvModules.DvsModel`；`GetModelInfo()` 返回流程 JSON，并附加 `loaded_model_meta` |
| RPC | 自动启动 `AIModelRPC.exe`；图像通过共享内存传输；结果中的 mask 可通过共享内存回读 |

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

`FlowGraphModel` 是流程图推理封装类，实现了 `IDisposable`。

#### 公开面

`FlowGraphModel` 的公开接口包括流程加载 `Load()`、模型元信息访问 `GetLoadedModelMeta()` 与 `GetModelInfo()`、推理入口 `Infer()` / `InferBatch()` / `InferOneOutJson()`、测速入口 `Benchmark()` 和生命周期接口 `Dispose()`。

#### 输入约定

`FlowGraphModel` 在执行流程时会向 `ExecutionContext` 写入前端图像单张与批量入口、`frontend_image_color_space = "rgb"`、空的 `frontend_image_path`、`device_id` 和 `return_json_emit_poly`。流程入口图像按 RGB 语义透传，不在 `FlowGraphModel` 内部整图转换为 BGR。

#### 返回行为

- 单图时 `InferInternal()` 的 `result_list` 直接是结果数组。
- 多图时 `result_list` 为容器数组，每项形如 `{ "result_list": [...] }`。
- `InferBatch()` 会把 `result_list` 转成 `Utils.CSharpResult`。
- `InferOneOutJson()` 保留流程 JSON 输出中的 `poly` 语义。

### `DvsModel`

`DvsModel` 继承 `FlowGraphModel`，用于加载 `.dvst`、`.dvso`、`.dvsp` 文件。

当前实现中，`DvsModel` 会校验 `DV\n` 文件头，解析第二行 JSON 头，按 `file_list` 与 `file_size` 解包 `pipeline.json` 和其他模型文件，将非 `pipeline.json` 文件写入临时目录并把流程中的 `model_path` 重写到临时路径，再调用 `LoadFromRoot()` 完成加载，最后在 `finally` 中删除临时目录。流程节点会额外补充 `model_path_original` 与 `model_name`。

## JSON 输出与结构化输出约定

### `Model` / `OcrWithDetModel` 的结构化输出

`Infer()` 与 `InferBatch()` 返回 `Utils.CSharpResult`，`SampleResults` 长度等于输入图像数量，每个检测对象都映射为 `CSharpObjectResult`。DVP 模式下，原始 `bbox` 若为 `[x1, y1, x2, y2]` 会转成 `[x, y, w, h]`，缺失 `bbox` 但存在 `polygon` 时会由 `polygon` 反算轴对齐框，`with_mask=true` 且存在 `polygon` 时会进一步生成局部 `Mask`；DVT / RPC 模式下，`mask` 优先从共享内存读取，其次从 `mask_ptr` 读取，再按 `bbox` 尺寸缩放到局部 mask。`polyline` 同时支持 `[[x, y], ...]` 与 `[{ "x": x, "y": y }, ...]` 两种 JSON 形式。

### `Model.InferOneOutJson()`

返回值是单张图片的 JSON 结果数组，统一包含基础识别字段 `category_id`、`category_name`、`score`、`area`，几何字段 `bbox`、`with_bbox`、`angle`、`with_angle`，区域字段 `mask`、`with_mask`，以及存在折线时的 `polyline`。`mask` 在 DVP 模式下由 `polygon` 转成点对象数组，在 DVS 模式下由 `poly` 的首个轮廓转成点对象数组，在 DVT / RPC 模式下由有效 `mask_ptr` 对应的 mask 图像提取轮廓；没有 mask 时固定输出 `{ "height": -1, "mask_ptr": 0, "width": -1 }`。

### `FlowGraphModel.InferOneOutJson()`

`FlowGraphModel.InferOneOutJson()` 直接返回流程 `result_list` 的单图结果数组，不再经过 `Model.StandardizeJsonOutput()` 标准化。流程输出通常包含基础识别字段 `category_id`、`category_name`、`score`、`area`，几何字段 `bbox`、`with_bbox`、`with_angle`、`angle`，形状字段 `poly`、`polyline`、`mask_rle`，以及附加信息字段 `metadata`。

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

`Utils` 是 `partial class`。公开能力分为四类：JSON 序列化 `jsonToString(JObject)` 与 `jsonToString(JArray)`；结构化结果转可视化 JSON 的 `ConvertToVisualizeFormat()` 与直接绘图的 `VisualizeResults()`；底层资源与设备查询的 `FreeAllModels()`、`GetDeviceInfo()`、`GetGpuInfo()`；以及组合式 OCR 推理 `OcrInfer(Model detectModel, Model recognizeModel, Mat image)`。其中 `ConvertToVisualizeFormat()` 仅把结构化结果中的 `category_id`、`category_name`、`score`、`bbox`、`with_angle`、`angle`、`with_mask` 和 `mask_rle` 写入可视化 JSON。

### `DllLoader`

`DllLoader` 是单例原生入口分发器，`Instance` 首次访问时完成三组委托绑定：模型生命周期接口 `dlcv_load_model`、`dlcv_free_model`、`dlcv_free_all_models`；模型信息、推理和结果释放接口 `dlcv_get_model_info`、`dlcv_infer`、`dlcv_free_model_result`、`dlcv_free_result`；设备与时钟查询接口 `dlcv_get_device_info`、`dlcv_get_gpu_info`、`dlcv_keep_max_clock`。授权特征列表中包含字符串 `"2"` 时，优先改用 `dlcv_infer2.dll`。

### `sntl_admin_csharp`

`sntl_admin_csharp` 对外提供状态枚举 `SntlAdminStatus`、原生加载器 `SNTLDllLoader`、运行时访问类 `SNTL` 和工具类 `SNTLUtils`。`SNTL` 负责建立上下文并提供 `Get()`、`GetSntlInfo()`、`GetDeviceList()`、`GetFeatureList()`、`Dispose()`；`SNTLUtils` 提供静态的设备列表与特征列表查询。`DllLoader` 通过 `SNTL.GetFeatureList()` 读取授权特征列表，以决定是否切换到 `dlcv_infer2.dll`。

## 流程图执行框架

### 基础运行时类型

`ExecutionContext` 提供不区分大小写的运行时键值访问；`ModuleRegistry` 维护 `moduleType -> Type` 注册表；`GlobalDebug` 与 `InferTiming` 分别承担调试输出和耗时记录；`TransformationState` 保存原图尺寸、裁剪框、仿射矩阵和输出尺寸；`ModuleImage` 将 `Mat`、原图、变换状态、原图序号和滑窗元数据打包；`ModuleIO` 与 `ModuleChannel` 分别作为模块调用输出容器和通道中间容器。

### 模块基类

`BaseModule` 保存节点 ID、标题、属性、上下文、扩展输入、扩展输出、模板列表以及标量输入输出，默认 `Process()` 返回空图像与空结果；`BaseInputModule` 继承 `BaseModule`，并把 `Process()` 固定为调用 `Generate()`。

### `GraphExecutor`

`GraphExecutor` 的构造函数为：

```csharp
GraphExecutor(List<Dictionary<string, object>> nodes, ExecutionContext context)
```

执行时会先触发所有非抽象 `BaseModule` 子类的静态注册，再读取节点的 `id`、`type`、`title`、`order`、`properties`、`inputs`、`outputs`，按 `order` 再按 `id` 排序，并通过 `outputs[*].links` 建立 `linkId -> (源节点, 源输出端口)` 映射。实例化模块后，每两个输入端口为一组，第 0 组作为主通道，其余写入 `ExtraInputsIn`；标量输入从上游 `scalars` 写入 `ScalarInputsByIndex` 和 `ScalarInputsByName`。`Process(mainImages, mainResults)` 执行完成后，主输出回写 `image_list`、`result_list`、`template_list`，标量输出则从 `ScalarOutputsByName` 回写 `scalars`。`NormalizeBboxProperties()` 会在节点属性中给出 `bbox_x1`、`bbox_y1`、`bbox_x2`、`bbox_y2` 但未显式给出 `bbox_x`、`bbox_y`、`bbox_w`、`bbox_h` 时自动补齐 `xywh`。

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

| 类名 | 注册类型 | 当前行为 |
| --- | --- | --- |
| `SlidingWindow` | `pre_process/sliding_window`，`features/sliding_window` | 按窗口尺寸与重叠率切图 |
| `SlidingMergeResults` | `pre_process/sliding_merge`，`features/sliding_merge` | 合并滑窗结果 |
| `ImageGeneration` | `features/image_generation` | 按检测结果裁剪生成子图 |
| `ImageFlip` | `features/image_flip` | 对图像做水平或竖直翻转并更新变换状态 |
| `MaskToRBox` | `post_process/mask_to_rbox`，`features/mask_to_rbox` | 由 mask 计算旋转框 |
| `MergeResults` | `post_process/merge_results`，`features/merge_results` | 合并主路与扩展路结果 |
| `ResultFilter` | `post_process/result_filter`，`features/result_filter` | 按类别拆分结果，未命中项进入 `ExtraOutputs[0]` |
| `ResultFilterAdvanced` | `post_process/result_filter_advanced`，`features/result_filter_advanced` | 按 bbox、rbox、bbox area、mask area 过滤 |
| `CoordinateCrop` | `pre_process/coordinate_crop`，`features/coordinate_crop` | 按 `x,y,w,h` 裁剪图像并派生子变换 |
| `ImageRescale` | `pre_process/image_rescale`，`features/image_rescale` | 按比例缩放图像并更新变换状态 |
| `ImageRotateByClassification` | `features/image_rotate_by_cls` | 根据分类标签将图像旋转 0/90/180/270 度，并同步更新结果几何 |
| `TextReplacement` | `post_process/text_replacement`，`features/text_replacement` | 按 `mapping` 替换 `category_name` |
| `RBoxCorrection` | `post_process/rbox_correction`，`features/rbox_correction` | 根据参考角度回正图像并同步结果几何 |
| `ResultLabelMerge` | `post_process/result_label_merge`，`features/result_label_merge` | 用主路标签与第二路标签拼接新类别名 |
| `BBoxIoUDedup` | `post_process/bbox_iou_dedup`，`features/bbox_iou_dedup` | 对轴对齐框按 IoU 或 IoS 去重 |
| `ResultFilterRegion` | `post_process/result_filter_region`，`features/result_filter_region` | 按 ROI 判断检测是否落在区域内 |
| `ResultFilterRegionGlobal` | `post_process/result_filter_region_global`，`features/result_filter_region_global` | 按原图坐标判定区域过滤，输出坐标系不改为原图 |
| `ResultCategoryOverride` | `post_process/result_category_override`，`features/result_category_override` | 用第二路结果覆盖主路已有字符串类别名 |
| `PolyFilter` | `post_process/poly_filter`，`features/poly_filter` | 从 polygon / mask 提取上沿或下沿折线并写回 `polyline` |
| `StrokeToPoints` | `features/stroke_to_points` | 从 mask 沿笔画方向生成等间距点框 |
| `TemplateFromResults` | `features/template_from_results` | 从 OCR 结果构建 `SimpleTemplate` |
| `TemplateSave` | `features/template_save` | 将模板写为 JSON，可选同时写 PNG |
| `TemplateLoad` | `features/template_load` | 从 JSON 读取模板 |
| `TemplateMatch` | `features/template_match` | 比较主模板与第二路模板，输出 `ok` 与 `detail` |
| `PrintedTemplateMatch` | `features/printed_template_match` | 按产品类型组织模板生成、保存、加载与匹配流程 |

## 主要模块行为补充

### `ReturnJson`

`ReturnJson` 会按图片把结果聚合到 `by_image`，每项至少包含 `origin_index`、`original_size` 和 `results`，并同步写入 `ExecutionContext.frontend_json` 的 `last`、`by_node[NodeId]` 与 `frontend_json_by_node`。当上下文中 `return_json_emit_poly = true` 或节点属性显式要求输出 `poly` 时，结果中保留 `poly`。

### `VisualizeOnOriginal`

`VisualizeOnOriginal` 会在原图上绘制 `mask_rle` 解码后的填充区域、轮廓、普通框、旋转框和 GDI+ 文本；颜色配置输入使用 RGB 语义，绘制时转换为 OpenCV 使用的 BGR。

### `SlidingWindow`

`SlidingWindow` 会为每个窗口生成子图 `ModuleImage`、一条 `local` 结果和 `sliding_meta`。`sliding_meta` 记录窗口在网格中的位置与尺寸信息，包括 `grid_x`、`grid_y`、`grid_size`、`win_size`、`slice_index` 以及窗口坐标 `x`、`y`、`w`、`h`。

### `SlidingMergeResults`

`SlidingMergeResults` 会把窗口内结果映射回原图坐标，并按原图索引输出一张合并后的图像包装与一条 `local` 结果。当前合并逻辑同时支持轴对齐框、旋转框、`mask union` 和 `bbox union`。

### `PolyFilter`

`PolyFilter` 会更新 `polyline`、`bbox` 和 `metadata` 中的 `poly_filter_direction`、`poly_filter_source`、`poly_filter_mode = "boundary_line"`。

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

`SimpleOcrItem` 表示模板中的单条 OCR 项，`Text` 保存文本内容，`Polygon` 保存对应区域轮廓，`Confidence` 保存该项置信度，`CategoryName` 保存该项类别名。

### `SimpleTemplate`

`SimpleTemplate` 是可保存、可加载、可匹配的模板对象，由模板标识 `TemplateName`、`template_id`，产品标识 `ProductName`、`ProductId`，相机位 `CameraPosition` 和 OCR 项列表 `OCRResults` 组成。

### `SimpleTemplateMatchDetail`

`SimpleTemplateMatchDetail` 是模板匹配明细容器：`ocr_results` 保存本次识别结果，`missing_template_items` 保存缺失项，`deviation_template_items` 保存偏差项，`misjudgment_pairs` 保存误判配对，`template_match_info` 保存本次匹配汇总信息。

