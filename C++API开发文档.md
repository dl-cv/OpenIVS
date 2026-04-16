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

- 项目级构建与发布前构建验证统一通过 MCP 构建工具执行，解决方案级与项目级入口见 `开发文档.md` 的“统一编译说明”
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

## 6. 对外数据结构

### 6.1 结构化结果

| 类型 | 当前字段 |
| --- | --- |
| `ObjectResult` | `categoryId`、`categoryName`、`score`、`area`、`bbox`、`withMask`、`mask`、`withBbox`、`withAngle`、`angle` |
| `SampleResult` | `results` |
| `Result` | `sampleResults` |
| `FlowNodeTiming` | `nodeId`、`nodeType`、`nodeTitle`、`elapsedMs` |

结果语义固定为：普通框 `bbox=[x,y,w,h]`，旋转框 `bbox=[cx,cy,w,h]` 且角度放在 `angle`；`withAngle=false` 时 `angle=-100.0f`；`withMask=true` 时 `mask` 为 `CV_8UC1`；结构化结果中的 `categoryName` 会转为 GBK。

### 6.2 Flow 聚合结构

| 类型 | 当前字段或作用 |
| --- | --- |
| `FlowResultItem` | 标准字段为 `category_id`、`category_name`、`score`、`bbox`、`metadata`、`mask_rle`、`poly`，其余键进入 `Extra` |
| `FlowByImageEntry` | 保存 `origin_index`、原图尺寸和该图结果 |
| `FlowFrontendPayload` | 保存 `by_image` |
| `FlowFrontendByNodePayload` | 在 `FlowFrontendPayload` 外增加 `NodeOrder`、`FallbackOrder` |
| `FlowBatchResult` | 保存每张图的结果数组，并负责生成根级 `result_list` |

## 7. `Model`

### 7.1 公开面

`Model` 暴露字段 `modelIndex`、`OwnModelIndex`；公开构造为默认构造、`Model(const std::string&, int)`、`Model(const std::wstring&, int)`；禁用拷贝、支持移动；公开成员函数为 `FreeModel()`、`GetModelInfo()`、`Infer()`、`InferBatch()`、`InferOneOutJson()`、`GetLastInferTiming()`、`GetLastFlowNodeTimings()`。

### 7.2 加载、释放与信息查询

| 场景 | 当前行为 |
| --- | --- |
| 模式判定 | `.dvst/.dvso/.dvsp` 进入 FlowGraph 模式，其余走底层 `dlcv_infer.dll` 普通模型模式 |
| 普通模型加载 | 请求 JSON 为 `{ "model_path": "<utf8_path>", "device_id": <id> }`；调用 `dlcv_load_model`；返回含 `model_index` 时写入 `modelIndex`，否则抛出 `load model failed: <result_json>` |
| FlowGraph 加载 | 创建 `flow::FlowGraphModel`，解包归档、重写 `pipeline.json` 后调用 `FlowGraphModel::Load`；`code==0` 视为成功，成功后 `modelIndex=1`，失败时抛出 `failed to load dvs model: <message>` |
| `OwnModelIndex` | `true` 时析构或 `FreeModel()` 会释放底层模型；`false` 时只把当前对象的 `modelIndex` 置为 `-1` |
| `FreeModel()` | FlowGraph 模式删除 `_flowModel`；普通模式下 `modelIndex==-1` 直接返回，`OwnModelIndex=false` 仅清空索引，否则调用 `dlcv_free_model`；每次都会把 `_expectedChCache` 重置为 `-2` |
| `GetModelInfo()` | FlowGraph 模式返回 `FlowGraphModel::_root`；普通模式调用 `dlcv_get_model_info` 并原样返回 JSON |

### 7.3 推理前图像规整

推理入口 `Infer()`、`InferBatch()`、`InferOneOutJson()` 都会先执行 `prepareInferInputBatch()`。输入通道数从 `GetModelInfo()` 的 `model_info.input_shapes` 或根级 `input_shapes` 的 `max_shape` 解析，只识别 `1` 和 `3` 通道；缓存状态为 `-2=未解析`、`-1=未知按 3 通道处理`、`1=单通道`、`3=三通道`。位深转换规则为：`CV_8U` 直接使用，`CV_16U` 按 `1/256` 转 8 位，`CV_16S` 与非常规浮点值域按 `NORM_MINMAX` 转 8 位，`[0,1]` 浮点值域按 `*255` 转 8 位，其余类型 `convertTo(CV_8U)`。通道规整规则为：目标 3 通道时 `1->GRAY2RGB`、`4->RGBA2RGB`、未知多通道取第 0 通道再扩到 RGB；目标 1 通道时 `3->RGB2GRAY`、`4->RGBA2GRAY`、未知多通道取第 0 通道。FlowGraph 模式下若输入已经是 `CV_8U` 且通道数等于目标通道，则直接透传。

### 7.4 推理、结果与计时

| 场景 | 当前行为 |
| --- | --- |
| 普通模型请求 | 每次请求固定包含 `model_index` 和 `image_list[{width,height,channels,image_ptr}]`；`params_json` 为对象时直接并入根 JSON；底层返回 `code!=0` 时抛出 `Inference failed: <message>` |
| 普通模型结构化结果 | 从 `sample_results[*].results[*]` 读取 `category_id/category_name/score/area/bbox/with_mask/mask/with_bbox/with_angle/angle`；`categoryName` 转 GBK，`with_bbox` 缺失时按 `bbox.size()>=4`，`with_angle` 缺失时按 `bbox[4]` 或有效 `angle` 推断；`mask` 会 `clone()`，尺寸与框不一致时按 `INTER_NEAREST` 缩放；缺框但有 `mask` 时按非零区域补框 |
| `InferOneOutJson()` 普通模式 | 返回首张图的结果数组；`with_mask=true` 时把 `mask_ptr` 指向的 mask 提取轮廓，输出点数组 `[{x,y}, ...]` |
| `InferOneOutJson()` FlowGraph 模式 | 返回流程第一张图的标准结果数组，字段固定包含 `category_id`、`category_name`、`score`、`bbox`、`with_bbox`、`with_angle`、`angle`、`mask`、`with_mask`、`area`；无 mask 时输出 `{ "height": -1, "mask_ptr": 0, "width": -1 }` |
| 最近一次计时 | 线程局部变量为 `g_lastDlcvInferMs`、`g_lastTotalInferMs`、`g_lastFlowNodeTimings`；普通模式两项都等于本次 wall clock 耗时；FlowGraph 模式优先读取流程返回的 `timing.dlcv_infer_ms` 与 `timing.flow_infer_ms`，缺失时回退到 wall clock 耗时 |

## 8. `Utils`

`Utils` 的公开静态函数包括 `JsonToString()`、`FreeAllModels()`、`GetDeviceInfo()`、`OcrInfer()`、`GetGpuInfo()`、`KeepMaxClock()` 和 5 个 NVML 包装函数。其行为分别是：`JsonToString()` 使用 `dump(4)`；`FreeAllModels()` 直接调用 `dlcv_free_all_models`；`GetDeviceInfo()` 直接调用 `dlcv_get_device_info`；`KeepMaxClock()` 仅在底层导出 `dlcv_keep_max_clock` 时调用；`OcrInfer()` 先用检测模型跑整图，再按 `bbox` 裁 ROI 给识别模型，若识别结果存在，则用第 1 条识别结果的 `categoryName` 覆盖检测结果的 `categoryName`；`GetGpuInfo()` 成功时返回 `{code:0,message:"Success",devices:[{device_id,device_name}]}`，NVML 初始化失败时返回 `code=1`，获取设备数量失败时返回 `code=2`。

## 9. `sntl_admin`

公开类型为 `SntlAdminStatus`、`SNTLDllLoader`、`SNTLUtils`、`SNTL` 和 `ParseXmlToJson()`。固定 XML 常量中，`DefaultScope` 的厂商 ID 固定为 `26146`，`HaspIdFormat` 读取 `haspid`，`FeatureIdFormat` 读取 `featureid` 与 `haspid`。`SNTL` 构造时调用 `sntl_admin_context_new`，析构时调用 `Dispose()`，`Dispose()` 再调 `sntl_admin_context_delete`；`Get()` 调 `sntl_admin_get`，成功时返回 `{ "code": 0, "message": "成功", "data": ... }`，失败时返回 `{ "code": <status>, "message": "<状态描述>" }`。`SNTLUtils::GetDeviceList()` 返回加密狗 ID 数组，`GetFeatureList()` 返回特性 ID 数组，任一异常都返回空数组 `[]`。

## 10. DVS 归档格式与加载

`.dvst`、`.dvso`、`.dvsp` 按 DVS 归档处理，大小写不敏感。归档格式要求文件前 3 字节为 `DV\n`，下一行是单行 JSON 头，且头对象必须同时包含 `file_list` 与 `file_size`，两者长度相等。解包流程为：创建 `%TEMP%\DlcvDvs_<24位随机十六进制>` 临时目录；读取归档内文件；`pipeline.json` 直接解析，其余文件写成 `32位随机十六进制 + 原扩展名`；用原始 `model_path` 和文件名两套键重写 `pipeline.json` 中各节点的 `properties.model_path`；把新 `pipeline.json` 写入临时目录并调用 `FlowGraphModel::Load`；最后由 `TempDirGuard` 递归删除解包目录。

## 11. `FlowGraphModel`

`FlowGraphModel` 公开接口为 `IsLoaded()`、`Load()`、`GetModelInfo()`、`InferOneOutJson()`、`InferInternal()`、`Benchmark()`，禁用拷贝、支持移动。

| 场景 | 当前行为 |
| --- | --- |
| `Load()` | 从 UTF-8 文件读取流程 JSON，要求根对象包含 `nodes`；执行 `GraphExecutor::LoadModels()`，且只对 `type` 以 `model/` 开头的节点调用 `LoadModel()`；成功时返回 `{code:0,message:"all models loaded",models:[...]}`，失败时把整体结果压缩为 `{code:1,message:<第一条失败消息或 report.message>}` |
| 清理 | 析构和移动赋值前都会调用 `ReleaseNoexcept()`；该函数只清理 `ModelPool::Instance().Clear()`，不调用 `Utils::FreeAllModels()` |
| `InferInternal()` 上下文 | 执行前写入 `frontend_image_mat`、`frontend_image_mats`、`frontend_image_mat_list`、`frontend_image_color_space="rgb"`、`frontend_image_path=""`、`device_id`、`infer_params`、`flow_dlcv_infer_ms_acc=0.0` |
| `InferInternal()` 返回 | 返回根 JSON，至少包含 `result_list` 与 `timing`；`timing` 包含 `flow_infer_ms`、`dlcv_infer_ms`、`node_timings[{node_id,node_type,node_title,elapsed_ms}]` |
| `Benchmark()` | 先跑 `warmup` 次 `InferOneOutJson()`，再跑 `runs` 次，返回平均毫秒数 |

## 12. `ExecutionContext`

`ExecutionContext` 是轻量键值容器，公开 `Set<T>()`、`Get<T>()`、`Has()`、`Remove()`、`Clear()`；内部用 `shared_ptr<IValue>` 持有值，拷贝构造和拷贝赋值执行深拷贝，`Get<T>()` 类型不匹配时返回默认值。

## 13. `GraphExecutor`

| 项目 | 当前规则 |
| --- | --- |
| 执行顺序 | 节点按 `order` 升序执行，`order` 相同时按 `id` 升序执行 |
| 链路路由 | 用 `outputs[*].links[*]` 建立 `linkId -> (srcNodeId, srcOutIdx)`；输入端口每两个组成一个 `ModuleChannel`，`0/1` 为主通道，`2/3`、`4/5` 为额外通道；通道类型固定为 `image_chan`、`result_chan`、`template_chan` |
| 标量端口 | 标量输入类型固定为 `bool`、`boolean`、`int`、`integer`、`str`、`string`、`scalar`；通过 `ScalarInputsByIndex`、`ScalarInputsByName` 注入，通过 `ScalarOutputsByName` 导出 |
| 属性修正 | 每个节点执行前把 `infer_params` 中 primitive 或 `null` 顶层字段覆盖到节点 `properties`；`with_mask` 不参与全局覆盖，但 `model/*` 仍会单独读取 `infer_params.with_mask`；当 `infer_params.with_mask=false` 且节点属性 `with_mask=true` 时，模型节点不再输出 `mask_rle`；若属性中存在 `bbox_x1..bbox_y2`，会自动补 `bbox_x`、`bbox_y`、`bbox_w`、`bbox_h` |
| 未注册节点 | `Run()` 中未注册的 `type` 直接跳过；`LoadModels()` 中未注册的 `model/*` 节点记为 `status_code=1`、`status_message="module_not_registered"` |
| 输出掩码 | 每个节点执行前把当前节点 `outputs[*].links` 转成位掩码写入 `__graph_current_output_mask`；当前使用该键的模块有 `output/save_image`、`output/preview`、`output/return_json`、`post_process/result_filter_advanced`、`pre_process/sliding_window` |

## 14. Flow 结果聚合

| 项目 | 当前规则 |
| --- | --- |
| `output/return_json` | 把局部结果恢复到原图坐标，生成 `FlowFrontendPayload`，追加到 `ExecutionContext["frontend_payloads_by_node"]`；当前节点没有下游消费者时，主输出清空 |
| 聚合读取优先级 | `frontend_payloads_by_node` -> `frontend_json.by_node` -> `frontend_json_by_node` -> `frontend_json.last` -> `frontend_payload_last` |
| 聚合规则 | `frontend_payloads_by_node` 按 `NodeOrder`、`FallbackOrder` 排序；`output/return_json` 写入的 `NodeOrder` 等于当前节点 `NodeId`；`origin_index>=0` 时回写到对应原图索引，否则使用 `ByImage` 中的位置索引；去重签名为 `FlowResultItem::ToJson().dump()` |
| 根结果格式 | 单图时 `result_list` 直接是结果数组；多图时 `result_list` 为 `[{ "result_list": [...] }, ...]` |

## 15. 已注册 Flow 节点

已注册节点按输入、模型、特征/预处理、滑窗、后处理、输出/模板 6 组组织。表中“主要属性”只保留会直接改变行为的关键键；同类字段按用途合并描述。

### 15.1 输入节点

| 节点类型 | 别名 | 作用 | 主要属性 | 输出 |
| --- | --- | --- | --- | --- |
| `input/image` | 无 | 生成输入图像与空结果项，优先接前端注入 Mat，其次按路径读图 | 输入路径：`path`、`paths` | `image + result`，并写 `scalar filename` |
| `input/frontend_image` | 无 | 生成前端图像输入，优先接前端注入 Mat，回退到单路径读图 | 输入路径：`path` | `image + result` |
| `input/build_results` | 无 | 基于现有图像、指定路径或默认纯色图，直接构造一条测试结果 | 图像来源：`image_path`、默认尺寸/颜色；结果内容：类别、分数、框坐标；框同时支持 `bbox_x1..bbox_y2` 和 `bbox_x..bbox_h` | `image + result` |

### 15.2 模型节点

| 节点类型 | 别名 | 作用 | 主要属性 | 输出 |
| --- | --- | --- | --- | --- |
| `model/det` | 无 | 检测类模型批量推理 | 模型与设备：`model_path`、`device_id`；阈值与结果控制：`threshold`、`iou_threshold`、`top_k`、`with_mask`、`return_polygon`、`epsilon`；批处理：`batch_size` | `image + result` |
| `model/rotated_bbox`、`model/instance_seg`、`model/semantic_seg` | 无 | 当前实现与 `model/det` 相同，只使用不同节点类型名 | 同 `model/det` | `image + result` |
| `model/cls` | 无 | 在 `model/det` 骨架上整理分类结果，并补整图 bbox | 同 `model/det` | `image + result` |
| `model/ocr` | 无 | 在 `model/det` 骨架上整理 OCR 结果，并补整图 bbox | 同 `model/det` | `image + result` |

### 15.3 特征与预处理节点

| 节点类型 | 别名 | 作用 | 主要属性 | 输出 |
| --- | --- | --- | --- | --- |
| `features/image_generation` | 无 | 按检测框裁局部图，并同步局部结果坐标 | 裁图控制：`crop_expand`、`min_size`、`crop_shape` | `image + result` |
| `features/image_flip` | 无 | 水平或竖直翻转图像，并更新变换状态 | 方向：`direction` | `image` |
| `pre_process/coordinate_crop` | `features/coordinate_crop` | 按固定坐标裁图 | 坐标与尺寸：`x`、`y`、`w`、`h` | `image + result` |
| `pre_process/image_rescale` | `features/image_rescale` | 按比例缩放图像 | 缩放：`scale` | `image + result` |
| `features/image_rotate_by_cls` | 无 | 按分类标签旋转图像，并同步结果坐标 | 标签映射：`rotate90_labels`、`rotate180_labels`、`rotate270_labels` | `image + result` |
| `post_process/result_label_merge` | `features/result_label_merge` | 把一路 top1 标签拼到另一路结果类别名前 | 文本控制：`fixed_text`、`use_first_score_top1` | `image + result` |

### 15.4 滑窗节点

| 节点类型 | 别名 | 作用 | 主要属性 | 输出 |
| --- | --- | --- | --- | --- |
| `pre_process/sliding_window` | `features/sliding_window` | 将图像切为滑窗子图，并写入滑窗元数据 | 窗口配置：`min_size`、`window_size`、`overlap` | 主输出 `image`；结果端口已连接时同时输出占位 `result` |
| `pre_process/sliding_merge` | `features/sliding_merge` | 将滑窗结果映射回原图坐标并合并 | 合并控制：`iou_threshold`、`dedup_results`、`task_type` | `image + result` |

### 15.5 后处理节点

| 节点类型 | 别名 | 作用 | 主要属性 | 输出 |
| --- | --- | --- | --- | --- |
| `post_process/merge_results` | `features/merge_results` | 合并主输入与额外输入的图像和结果 | 无 | `image + result` |
| `post_process/result_filter` | `features/result_filter` | 按类别名过滤结果 | 类别列表：`categories` | 主：命中 `image + result`；旁路：未命中 `image + result`；标量：`has_positive` |
| `post_process/result_filter_advanced` | `features/result_filter_advanced` | 按框宽高、框面积、mask 面积过滤结果 | 开关：`enable_bbox_wh`、`enable_rbox_wh`、`enable_bbox_area`、`enable_mask_area`；范围：bbox/rbox 宽高、bbox/mask 面积上下限 | 主：命中 `image + result`；旁路：未命中 `image + result`；标量：`has_positive` |
| `post_process/text_replacement` | `features/text_replacement` | 按映射表替换 `category_name` | 映射：`mapping` | `image + result` |
| `post_process/result_category_override` | `features/result_category_override` | 用额外输入第一条类别名覆盖主输入结果类别名 | 无 | `image + result` |
| `post_process/mask_to_rbox` | `features/mask_to_rbox` | 将 `mask_rle` 或 `mask_min_area_rect` 转为旋转框 | 无 | `image + result` |
| `post_process/rbox_correction` | `features/rbox_correction` | 按旋转角纠正图像与 bbox | 填充值：`fill_value` | `image + result` |
| `post_process/bbox_iou_dedup` | `features/bbox_iou_dedup` | 按 IoU 或 IoS 去重普通框 | 去重规则：`metric`、`iou_threshold`、`per_category` | `image + result`；标量：`kept_count`、`removed_count` |

### 15.6 输出与模板节点

| 节点类型 | 别名 | 作用 | 主要属性 | 输出 |
| --- | --- | --- | --- | --- |
| `output/save_image` | 无 | 将图像保存到目录 | 保存参数：`save_path`、`suffix`、`format` | `image + result` |
| `output/preview` | 无 | 纯透传 | 无 | `image + result` |
| `output/return_json` | 无 | 生成前端结果载荷并回写上下文 | 无 | `image + result` 或空输出 |
| `post_process/result_filter_region` | `features/result_filter_region` | 按区域分流结果 | 区域与模式：`x`、`y`、`w`、`h`、`result_region_mode` | 主：区域内 `image + result`；旁路：区域外 `image + result`；标量：`has_positive` |
| `post_process/result_filter_region_global` | `features/result_filter_region_global` | 按原图坐标系区域分流结果 | 同 `post_process/result_filter_region` | 输出同上 |
| `features/stroke_to_points` | 无 | 将 mask 区域转换为点框结果 | 点位控制：`counts_dict`、`point_width`、`point_height` | `image + result` |
| `output/visualize` | 无 | 在原图上绘制结果 | 显示开关、字体样式、框颜色：`black_background`、`display_*`、`font_*`、`bbox_color*` | `image + result` |
| `output/visualize_local` | 无 | 在局部图上绘制普通框与标签 | 绘制样式：`bbox_color`、`font_scale`、`font_thickness` | `image + result` |
| `features/template_from_results` | 无 | 从结果生成模板 JSON | 模板标识：`product_name`、`product_id`、`template_name` | `image + result + template` |
| `features/template_save` | 无 | 保存模板 JSON 与首张图 PNG | 文件名：`file_name` | 空 |
| `features/template_load` | 无 | 从 JSON 文件载入模板 | 路径：`path` | `image + result + template` |
| `features/template_match` | `features/printed_template_match` | 比较主模板与额外输入模板的 OCR 项 | 匹配阈值与位置检查：`position_tolerance_x`、`position_tolerance_y`、`min_confidence_threshold`、`check_position` | 主输出空；标量：`ok`、`detail` |

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
