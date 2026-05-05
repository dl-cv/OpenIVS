# OpenIVS Agent 规则

## 文档原则

- **先阅读 README.md**：进入本项目时，优先阅读项目根目录的 `README.md`，再阅读其他文档。
- **只记录事实**：文档只记录项目当前实现与运行事实，不写建议、猜测、规划、风险判断、设计理由、判定依据。
- **不写问答与对话**：文档不写问答式内容，不直接回答对话中的临时问题；不写第一人称、第二人称对话。
- **不写历史与过程**：文档不写历史、过程、修改记录、拆分说明。
- **不重复**：同一事实只在最合适的位置出现一次。
- **不验证不写入**：任何写入本文件的事实必须经过验证（读取代码、运行命令、查看配置文件等），禁止把猜测或推断当作既定事实写入。
- **配置依赖，不拷贝源码**：项目间依赖应通过正确的构建配置解决，禁止建议通过拷贝源码来绕过依赖问题。

## 精确代码原则

- **禁止猜测字段名**：不得使用 `FirstStringByKeys` 或类似的多候选字段名列表去"碰运气"式读取数据结构。必须先通过运行测试程序、查阅文档或分析实际返回数据，拿到证据后再写精确字段访问代码。
- **禁止滥用 try-catch 做兜底猜测**：不得用 `try-catch` 包裹一堆不确定的字段名去尝试读取，以此掩盖对数据结构的无知。允许并鼓励使用 try-catch 做防御性编程（防止崩溃、处理意外输入、确保资源释放）。
- **对齐既有正确代码**：项目中已有正确实现的模块（如 Sentinel 加密狗的 `GetDeviceList`）是权威参考。新增或修改同类功能时，必须保持相同的代码风格、精度和严谨性。
- **先测试，后实现**：对于第三方 API（如加密狗、硬件设备）的返回格式，如不确定，必须先编写并运行测试程序获取实际返回 JSON/XML，基于证据再编写生产代码。

## 工程一致性原则

- **核心缺口必须在核心层补齐**：当核心逻辑（如加载器、解析器、资源绑定器）与输入数据格式不匹配时，必须在核心逻辑内部完成适配或提供默认行为，严禁在外围通过修改输入数据、写临时补丁文件、运行时打二进制补丁等方式绕过。外围修补会破坏数据完整性、引入隐藏依赖，且无法在日志和异常中体现真实问题。
- **修改核心逻辑必须同步所有入口**：调整模型加载、推理、资源绑定等核心行为时，必须同步检查并更新所有相关入口（如不同文件格式、不同协议、不同调用路径），确保行为一致。禁止仅在单一入口修复而在其他入口通过外围补丁兜底。

## 项目概览

OpenIVS 是一个 .NET WPF 工业视觉框架。**本 AGENTS.md 聚焦 API 层（C++ DLL + C# 封装层）与测试程序**，不展开 WPF 框架本身（相机/PLC/主循环）。WPF 主框架构建在 API 层之上。

- **技术栈**：C# .NET Framework 4.7.2 + C++17 + Qt + WPF
- **平台**：Windows 10+，x64
- **API 层**：
  - C++ API：`dlcv_infer_cpp_dll`（头文件 `dlcv_infer.h`、`dlcv_sntl_admin.h`、`flow/FlowGraphModel.h`）
  - C# API：`DlcvCsharpApi`（`Model.cs`、`Utils.cs`、`FlowGraphModel.cs`、`DvsModel.cs`、`DllLoader.cs`）
- **测试程序**：
  - C++：`dlcv_infer_cpp_qt_demo`（Qt 桌面应用）
  - C#：`DlcvDemo` / `DlcvDemo2` / `DlcvDemo3`（WinForms 桌面应用）
- **控制台测试**：`Test/DlcvCSharpTest`、`Test/dlcv_infer_cpp_test`

## 构建方式

- **编译时优先使用项目 skill**：`.cursor/skills/vs-build/scripts/build.py`
- **禁止直接调用** `msbuild`、`dotnet`、`devenv` 或其他本地 shell 编译命令
- **默认构建参数**：`Debug`、`x64`、`Build`、`minimal`
- 用户明确指定 `Release`、`Rebuild`、`Clean` 等参数时，按指定值执行
- .NET 项目通过 Visual Studio 或 `dotnet build` 编译
- C++ 项目通过 Visual Studio 编译（x64 Release）
- Qt 项目需配置 Qt 路径和 OpenCV 路径
- 构建前需确保深度视觉 SDK 已正确安装（`dlcv_infer.dll` 可用）
- WPF 框架额外需要海康 MVS 安装

## 统一运行与验证输入规则

- 本仓库所有程序、测试程序、自测入口、验证入口和临时排查入口均**禁止使用命令行传参**覆盖模型路径、图片路径、batch、阈值、运行次数、线程数或其他业务输入。
- 需要切换验证输入时，只修改源码中的固定变量、常量字符串或配置对象字段。
- 控制台测试程序也遵守该规则；验证时不通过 shell 命令行追加参数。

## 核心模块与入口

| 关注点 | 文件 | 说明 |
|--------|------|------|
| C++ API 头文件 | `dlcv_infer_cpp_dll/dlcv_infer.h` | `Model`、`SlidingWindowModel`、`Utils`、`DllLoader`、`GetAllDogInfo` |
| C++ API 实现 | `dlcv_infer_cpp_dll/dlcv_infer.cpp` | 模型加载、推理、DVS 解包、结果解析 |
| C++ 加密狗 | `dlcv_infer_cpp_dll/dlcv_sntl_admin.cpp` | Sentinel/Virbox 设备与 feature 查询 |
| C++ 流程图 | `dlcv_infer_cpp_dll/flow/FlowGraphModel.h` | `FlowGraphModel` 类 |
| C# 封装层 | `DlcvCsharpApi/Model.cs` | `Model`：构造、加载、推理、释放 |
| C# 工具类 | `DlcvCsharpApi/Utils.cs` | 结果类型、编码转换、DLL 释放 |
| C# DLL 加载器 | `DlcvCsharpApi/DllLoader.cs` | 加密狗自动检测、DLL 路径解析、函数代理 |
| C# 流程图 | `DlcvCsharpApi/flow/FlowGraphModel.cs` | `FlowGraphModel`：加载、推理、JSON 输出 |
| C# DVS 模型 | `DlcvCsharpApi/flow/DvsModel.cs` | `.dvst/.dvso/.dvsp` 归档解包与加载 |
| C# 结果类型 | `DlcvCsharpApi/DataTypes.cs` | `CSharpObjectResult`、`CSharpSampleResult`、`CSharpResult` |
| C# 加密狗工具 | `DlcvCsharpApi/sntl_admin_csharp.cs` | `DogUtils`、`DogProvider` |
| C++ 图像输入 | `dlcv_infer_cpp_dll/ImageInputUtils.h` | 图像预处理与格式转换 |
| C++ 测试程序 | `dlcv_infer_cpp_qt_demo/MainWindow.cpp` | 模型加载、推理、压力测试、加密狗检测 |
| C# 测试程序 | `DlcvDemo/Form1.cs` | WinForms 测试程序主窗口 |
| C# 压力测试 | `PressureTestRunner/PressureTestRunner.cs` | 多线程/一致性测试框架 |

## 常见修改点

- **API 层修改**：
  - 新增结果字段 → 同步修改 `C++API接口文档.md`、`C# API接口文档.md`
  - 修改图像预处理逻辑 → 同步检查 C++ `dlcv_infer.cpp` 与 C# `Model.cs` 的 `PrepareInferImages`
  - 新增模型格式支持 → 同步更新 `DllLoader`（C++ 与 C#）的 `ResolveProviderFromHeader`
- **测试程序修改**：
  - 新增推理参数 → 同步更新 C++ `MainWindow.cpp` 与 C# `Form1.cs` 的参数 JSON 构建
  - 修改可视化规则 → 同步检查 C++ `ImageViewerWidget` 与 C# `ImageViewer`

## 关键依赖路径

- **DLCV 推理 DLL（必须）**
  - `dlcv_infer.dll`（Sentinel）或 `dlcv_infer_v.dll`（Virbox）
  - 路径：`C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll`
  - 未安装时测试程序启动会弹窗提示"需要先安装 dlcv_infer"
- **海康 MVS DLL（WPF 框架使用）**
  - `C:\Program Files (x86)\MVS\Development\DotNet\win64\MvCameraControl.Net.dll`
  - 未安装 MVS 会出现找不到 `MvCameraControl` 的问题

## 模型文件类型

对外使用时，模型文件只分为两类：

1. **普通模型文件**：`.dvt`、`.dvo`。由 `Model` 直接加载，适合单模型推理。
2. **流程模型文件**：`.dvst`、`.dvso`。由 `FlowGraphModel` 或 `DvsModel` 加载，适合把多步处理组织成一条完整流程。

调用端不需要为这两类模型准备两套完全不同的调用方式。传入模型路径、设备和请求参数后，入口对象会完成对应的加载与执行。

## API 速查表

### C++ API 速查表

| 能力 | 类/函数 | 关键接口 |
|------|---------|----------|
| 普通模型 | `dlcv_infer::Model` | 构造（`std::string`/`std::wstring` + `device_id`）、`Infer()`、`InferBatch()`、`InferOneOutJson()`、`GetModelInfo()`、`FreeModel()` |
| 滑动窗口模型 | `dlcv_infer::SlidingWindowModel` | 继承 `Model`，构造参数含 `small_img_width/height`、`horizontal/vertical_overlap`、`threshold`、`iou_threshold`、`combine_ios_threshold` |
| 工具类 | `dlcv_infer::Utils` | `FreeAllModels()`、`GetDeviceInfo()`、`GetGpuInfo()`、`KeepMaxClock()`、`OcrInfer()`、`JsonToString()` |
| DLL 加载器 | `dlcv_infer::DllLoader` | `Instance()`、`EnsureForModel()`、`GetDogProvider()` |
| 流程图模型 | `dlcv_infer::flow::FlowGraphModel` | `Load()`、`InferInternal()`、`GetModelInfo()` |
| 加密狗查询 | `dlcv_infer::GetAllDogInfo()` | 返回 Sentinel + Virbox 设备与 feature 列表 |
| 编码转换 | `dlcv_infer::convert*()` | `Utf8↔Gbk`、`Utf8↔Wide`、`Ansi↔Wide` |

### C# API 速查表

| 能力 | 类/函数 | 关键接口 |
|------|---------|----------|
| 普通模型 | `DlcvModules.Model` | 构造（`string modelPath, int deviceId`）、`Infer()`、`InferBatch()`、`InferOneOutJson()`、`GetModelInfo()`、`Dispose()` |
| 流程图模型 | `DlcvModules.FlowGraphModel` | `Load()`、`Infer()`、`InferBatch()`、`InferOneOutJson()`、`GetModelInfo()`、`GetLoadedModelMeta()`、`Dispose()` |
| DVS 归档模型 | `DlcvModules.DvsModel` | 继承 `FlowGraphModel`，`Load(string dvsPath, int deviceId)` |
| 工具类 | `DlcvModules.Utils` | `FreeAllModels()`、`GetDeviceInfo()`、`GetGpuInfo()`、`KeepMaxClock()`、`GetAllDogInfo()`、JSON 格式化、可视化转换 |
| DLL 加载器 | `dlcv_infer_csharp.DllLoader` | `Instance`、`EnsureForModel()`、`LoadedDogProvider` |
| 推理计时 | `DlcvModules.InferTiming` | `GetLast(out double dlcvInferMs, out double flowInferMs)`、`GetLastFlowNodeTimings()` |
| 加密狗查询 | `sntl_admin_csharp.DogUtils` | `GetSentinelInfo()`、`GetVirboxInfo()`、`GetAllDogInfo()` |

### 推理参数 JSON 字段（C++ / C# 通用）

| 字段名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `threshold` | float | 0.5 | 置信度阈值 |
| `with_mask` | bool | true | 是否输出 mask |
| `batch_size` | int | 1 | 批量大小 |
| `device_id` | int | 构造时传入 | GPU 设备 ID（-1 表示 CPU） |

### DLL 映射（C++ / C# 通用）

| 加密狗类型 | DLL 名称 | 固定路径 |
|-----------|---------|---------|
| Sentinel | `dlcv_infer.dll` | `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll` |
| Virbox | `dlcv_infer_v.dll` | `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer_v.dll` |

自动检测优先级：先检测 Sentinel，再检测 Virbox；均未检测到则回退到 Sentinel。每个 `Model` 实例在加载时绑定自己的 `_dllLoader`，后续所有操作都走该 loader。

## 输入图像处理约定

模型推理前，输入图像会被规整到模型可接受的格式。调用端只关心"把图传进去就能得到稳定结果"，底层负责完成以下处理：

| 输入情况 | 处理方式 | 对调用方表现 |
| --- | --- | --- |
| 空图或空批量 | 直接报错 | 不返回伪成功结果 |
| 16 位、浮点、带符号整型图像 | 转为 8 位 | 模型得到稳定输入 |
| 模型期望 3 通道 | 调用方负责把 OpenCV 颜色图整理为 RGB；接口再按模型输入把灰度图补成 3 通道并统一位深 | 颜色顺序职责留在调用方，通道数兜底在接口层完成 |
| 模型期望 1 通道 | 调用方负责把颜色顺序整理到约定输入；接口再按模型输入把 3/4 通道图转为灰度并统一位深 | 单通道兜底在接口层完成 |
| 批量输入 | 保持原顺序 | 返回结果与输入顺序一一对应 |
| 从磁盘读取的 OpenCV 图像 | OpenCV 按原始 BGR / 灰度语义解码；是否转 RGB 由调用方决定 | 读盘行为与推理解耦 |

**核心约定**：调用方负责 `BGR/BGRA → RGB` 颜色顺序转换；API 层只负责按模型期望通道数（1 或 3）做最小必要的补齐或压缩，并统一位深到 `CV_8U`。Flow 侧不再额外做 BGR→RGB，但保留按模型输入做最小通道规整。

## 结果与几何信息语义

系统对外的结果围绕四类信息组织：

| 信息层 | 内容 | 用途 |
| --- | --- | --- |
| 识别信息 | 类别 ID、类别名、分数 | 用于业务判定、界面显示、文本输出 |
| 几何信息 | 框、角度 | 用于定位目标、绘制可视化、裁图、去重、区域过滤 |
| 区域信息 | `mask`、`mask_rle`、`poly` | 用于实例分割、区域着色、边界提取、轮廓分析 |
| 扩展信息 | `extra_info`、`metadata` | 用于模块附加信息、业务扩展信息、调试信息 |

### 结构化结果层级

| 类型 | 作用 | 结构 |
| --- | --- | --- |
| `ObjectResult` | 表示一个目标的完整结果 | 一个类别 + 一个几何位置 + 一个区域信息集合 |
| `SampleResult` | 表示一张图上的所有目标 | `results` 列表 |
| `Result` | 表示一次批量请求的所有图片结果 | `sample_results` 列表 |

### `ObjectResult` 字段

| 字段 | 含义 | 典型用途 |
| --- | --- | --- |
| `category_id` | 类别编号 | 程序内逻辑判断、分类统计 |
| `category_name` | 类别名称 | 界面显示、文本输出、模板匹配 |
| `score` | 置信度 | 阈值过滤、日志输出、排序 |
| `area` | 面积 | 后处理过滤、结果统计 |
| `bbox` | 框位置 | 绘制框、裁剪、去重、区域判断 |
| `with_bbox` | 是否有框 | 区分有定位结果和无定位结果 |
| `with_angle` | 是否有角度 | 区分普通框和旋转框 |
| `angle` | 旋转角度 | 绘制旋转框、旋转裁剪、回正 |
| `with_mask` | 是否有 mask | 区分检测结果和分割结果 |
| `mask` | 结构化结果中的局部 mask 图像 | 做进一步图像计算 |
| `extra_info` | 扩展信息对象 | 放置折线、业务附加信息 |
| `metadata` | 元信息对象 | 放置流程来源、模块附加信息 |

### JSON 结果字段

| JSON 字段 | 含义 | 说明 |
| --- | --- | --- |
| `category_id` | 类别编号 | 与结构化结果一致 |
| `category_name` | 类别名称 | 与结构化结果一致 |
| `score` | 置信度 | 与结构化结果一致 |
| `area` | 面积 | 与结构化结果一致 |
| `bbox` | 框位置 | 轴对齐框为 `[x, y, w, h]`，旋转框为 `[cx, cy, w, h]` |
| `with_bbox` | 是否有框 | 与结构化结果一致 |
| `with_angle` | 是否有角度 | 与结构化结果一致 |
| `angle` | 旋转角度 | 无角度时固定为 `-100` |
| `with_mask` | 是否有区域结果 | 与结构化结果一致 |
| `mask_rle` | RLE 编码区域 | 面向 JSON 传输和跨语言交换 |
| `poly` | 多边形轮廓数组 | 用于前端绘制、边界分析、折线提取 |
| `extra_info` | 扩展信息 | 例如 `extra_info.polyline` |
| `metadata` | 元信息 | 记录模块附加信息、运行信息 |

### 几何语义

| 内容 | 表达方式 | 坐标语义 |
| --- | --- | --- |
| 普通框 | `bbox=[x, y, w, h]` | 左上角与宽高 |
| 旋转框 | `bbox=[cx, cy, w, h]` + `angle` | 中心点、宽高、角度 |
| 没有角度 | `with_angle=false`，`angle=-100` | 固定语义，不靠缺字段判断 |
| `mask_rle` | RLE 编码后的区域 | 与结果对应区域一致 |
| `poly` | 轮廓点列表 | 原图坐标 |
| `extra_info.polyline` | 折线点列表 | 在进入最终 JSON 前还原到原图坐标 |

### 命名约定

| 场景 | 命名方式 | 示例 |
| --- | --- | --- |
| JSON 字段 | `snake_case` | `category_id`、`with_mask`、`mask_rle` |
| C# 公共属性 | `PascalCase` | `CategoryId`、`WithMask`、`MaskRle` |
| C++ 公共成员 | `camelCase` | `categoryId`、`withMask`、`maskRle` |

## Flow 模型与流程执行

### `FlowGraphModel`

| 能力 | 说明 |
| --- | --- |
| `Load()` | 读取流程 JSON，并预加载所有 `model/*` 节点 |
| `GetModelInfo()` | 返回流程根对象及流程元信息 |
| `GetLoadedModelMeta()` | 返回流程中每个模型节点的加载信息 |
| `Infer()` / `InferBatch()` | 返回标准结构化结果 |
| `InferOneOutJson()` | 返回单图 JSON 结果 |
| `Benchmark()` | 返回平均耗时，单位毫秒 |

### 一次 Flow 请求的执行路径

1. `FlowGraphModel` 把单图或批量图像写入 `ExecutionContext`。
2. 前端注入图像按调用方准备好的通道顺序进入流程，当前三通道约定为 RGB。
3. `GraphExecutor` 读取节点列表，按 `order` 和 `id` 排序。
4. 模块按链路依次执行，图像、结果、模板和标量在模块之间流动。
5. `output/return_json` 把局部结果还原到原图坐标并聚合成对外结果。
6. `FlowGraphModel` 从聚合结果读取 `result_list`，同时返回时间信息。

### `DvsModel`

`DvsModel` 负责把 `.dvst`、`.dvso`、`.dvsp` 归档文件变成可直接执行的流程对象。对调用方来说，它的外部行为与 `FlowGraphModel` 一样，区别只在于加载入口是一个归档文件。

归档加载过程：
1. 检查文件头是否为 `DV\n`。
2. 读取第二行 JSON 头。
3. 从头信息中读取 `file_list` 和 `file_size`。
4. 解包 `pipeline.json` 和归档中的其他文件。
5. 把流程中的 `model_path` 重写到临时目录里的真实文件路径。
6. 记录 `model_path_original` 和 `model_name`。
7. 调用流程加载逻辑完成模型预加载。
8. 清理解包产生的临时目录。

### Flow 模块分类

流程中的模块按输入、模型、预处理/特征、后处理、输出与模板五组组织：

**输入模块**：`input/image`、`input/frontend_image`、`input/build_results`

**模型模块**：`model/det`、`model/rotated_bbox`、`model/instance_seg`、`model/semantic_seg`、`model/cls`、`model/ocr`

**预处理与特征模块**：`pre_process/sliding_window`、`pre_process/sliding_merge`、`features/image_generation`、`features/image_flip`、`features/coordinate_crop`、`features/image_rescale`、`features/image_rotate_by_cls`、`features/stroke_to_points`

**后处理模块**：`post_process/merge_results`、`post_process/result_filter`、`post_process/result_filter_advanced`、`post_process/text_replacement`、`post_process/mask_to_rbox`、`post_process/rbox_correction`、`post_process/result_label_merge`、`post_process/bbox_iou_dedup`、`post_process/result_filter_region`、`post_process/result_filter_region_global`、`post_process/result_category_override`、`post_process/poly_filter`

**输出与模板模块**：`output/save_image`、`output/preview`、`output/return_json`、`output/visualize`、`output/visualize_local`、`features/template_from_results`、`features/template_save`、`features/template_load`、`features/template_match`、`features/printed_template_match`

### 模板与模板匹配

模板能力围绕四个动作工作：从 OCR 结果生成模板、保存到文件、从文件读取、与当前识别结果比对。

核心对象：
- `SimpleOcrItem`：表示一条 OCR 项，包括文本、位置、尺寸、置信度和匹配状态
- `SimpleTemplate`：表示一个完整模板，包括模板标识、产品标识、相机位、OCR 项列表和匹配阈值
- `SimpleTemplateMatchDetail`：表示一次模板匹配的明细结果，包括通过项、偏差项、过检项、漏检项和误判配对

匹配结果阅读方式：
- `IsMatch`：直接给出通过或失败
- `Score`：整体接近程度
- `Reasons`：向界面/日志解释结果
- `CorrectItems`：命中的 OCR 项
- `DeviationItems`：命中但存在偏差
- `OverDetectionItems`：模板外的额外识别项
- `MissedTemplateItems`：模板里应出现但本次没识别到的项
- `MisjudgmentPairs`：模板项和识别项被配错的情况

## 计时与诊断

一次请求需要同时回答三个问题：本次请求总共用了多久；其中真正花在底层模型推理上的时间有多少；如果是流程推理，慢在流程的哪个节点。

| 统计项 | 对外名称 | 说明 | 用途 |
| --- | --- | --- | --- |
| 模型推理时间 | `dlcv_infer_ms` | 当前请求中所有底层模型推理调用消耗的时间总和 | 区分算法推理成本和流程调度成本 |
| 流程总时间 | `flow_infer_ms` | 从流程入口开始到最终结果组织完成的总时间 | 显示用户感知的整次请求耗时 |
| 节点耗时列表 | `node_timings` | 每个节点或流程阶段的耗时 | 定位慢模块、慢阶段 |

时间数据读取入口：
- C#：`DlcvModules.InferTiming.GetLast(...)`、`DlcvModules.InferTiming.GetLastFlowNodeTimings()`
- C++：`dlcv_infer::Model::GetLastInferTiming(...)`、`dlcv_infer::Model::GetLastFlowNodeTimings()`

数据存储在线程局部变量中，多线程场景下每个线程独立。

## 测试程序概览

### C++ Qt Demo（`dlcv_infer_cpp_qt_demo`）

- **工程**：`dlcv_infer_cpp_qt_demo/dlcv_infer_cpp_qt_demo.vcxproj`
- **技术栈**：Qt5/6 + OpenCV 4.x + `dlcv_infer_cpp_dll`
- **功能**：模型加载、单图/批量推理、JSON 输出、多线程压力测试、加密狗检测
- **UI**：主窗口分为上方控制栏（按钮 + 参数调节）+ 下方输出区（左侧文本 + 右侧图像可视化）
- **按钮**：加载模型、获取模型信息、打开图片推理、单次推理、推理JSON、多线程测试、释放模型、释放所有模型、文档、检查加密狗
- **参数控件**：选择显卡（下拉）、batch_size（1~1024，默认1）、threshold（0.0~1.0，默认0.5）、线程数（1~32，默认1）
- **图像预处理**：`prepareImageForInference` 将 BGR/BGRA 转为 RGB
- **压力测试**：每 500ms 更新统计，包含运行时间、完成请求数、平均延迟、实时速率、各节点平均耗时

### C++ Qt Demo3（`dlcv_infer_cpp_qt_demo3`）

- **工程**：`dlcv_infer_cpp_qt_demo3/dlcv_infer_cpp_qt_demo3.vcxproj`
- **业务**：两模型串联 + 原图固定裁图 `128 x 192` + 模型2 batch 推理 + 结果统一回写原图坐标
- **界面**：去掉设备选择和文档按钮；保留模型1路径、模型2路径、图片路径、固定裁图大小标签、模型2线程数输入框
- **交互**：浏览模型后自动加载；选择图片后自动触发推理
- **设备策略**：自动选择第一个可用 GPU，无 GPU 时退回 CPU，不在界面暴露

### C# WinForms Demo（`DlcvDemo`）

- **工程**：`DlcvDemo/DlcvDemo.csproj`
- **技术栈**：.NET Framework 4.7.2 + WinForms + OpenCvSharp4 + Newtonsoft.Json
- **窗口标题**：`C# 测试程序`
- **按钮**：加载模型、打开图片推理、单次推理、推理JSON、多线程测试、一致性测试、释放模型、释放所有模型、检查加密狗、文档、获取模型信息
- **参数**：设备选择（CPU 为首项 device_id=-1）、线程数（1-32，默认1）、batch_size（1-1024，默认1）、threshold（0.00-1.00，默认0.50）、RPC模式复选框
- **图像显示控件**：`DLCV.ImageViewer`，支持滚轮缩放、左键拖拽、右键复位、`V` 切换可视化、`C` 切换标签模式、`+`/`-`/`0` 调整标签字体倍率、`Ctrl+滚轮` 只调标签
- **交互规则**：
  - 设备选择下拉框的 `device_id` **只在加载时读取**
  - 打开图片后**立即触发**单次推理
  - threshold 调整且模型与图片就绪时**立即重新推理**
  - 多线程测试按钮为**开关**（运行中显示"停止"）
  - 一致性测试：首次推理保存基准，后续不一致时立即停止并弹 Warning
- **关闭窗口**：停止测试 → Dispose 模型 → `Utils.FreeAllModels()`

### C# WinForms Demo2（`DlcvDemo2`）

- **工程**：`DlcvDemo2/DlcvDemo2.csproj`
- **业务**：三模型固定推理流程——元件提取模型整板滑窗检测 → 全图合并 → 类别名拆分与角度解析 → ROI 裁剪与逆时针归一 → 按类别分流到元件检测模型 / IC 检测模型 → 二级检测结果映射回元件提取类别 → 坐标回写 → 最终结果合并展示
- **界面**：去掉 GPU 选择、JSON 输出、一致性测试、测速入口
- **滑窗参数**：窗口宽 2560、窗口高 2560、水平重叠 1024、垂直重叠 1024
- **IC 分流规则**：`base_name` 为 `IC`、`BGA`、`座子`、`开关`、`晶振` 时走 IC 检测模型，其余走元件检测模型
- **二级映射规则**：主体类别 `元件` 或兼容老版本的 `IC` 映射回元件提取模型的原始 `category_name`；细分类别（`焊点`、`引脚`、`文字`）保持原结果不变

### C# WinForms Demo3（`DlcvDemo3`）

- **工程**：`DlcvDemo3/DlcvDemo3.csproj`
- **业务**：两模型串联 + 原图固定裁图 `128 x 192` + 模型2 batch 推理 + 结果统一回写原图坐标
- **界面**：与 Demo2 风格一致，但删去滑窗参数区，增加"速度测试"按钮
- **模型2线程数**：UI 中设置，默认 4，范围 1~32
- **Batch 策略**：自动读取 `model2.GetMaxBatchSize()`，不提供 UI 配置
- **速度测试**：使用 `PressureTestRunner`，`ThreadCount=1`、`batchSize=1`，定时器 500ms 刷新统计

### 控制台测试工程

- **C#**：`Test/DlcvCSharpTest`，入口 `Program.cs`
- **C++**：`Test/dlcv_infer_cpp_test`，入口 `main.cpp`
- **测试范围**：模型加载成功/失败、推理成功/失败、推理结果类别列表、3 秒平均推理速度、Batch 推理速度、内存泄露专项（仅对 1 个实例分割模型执行加载/释放循环 10 次 + 推理 3 秒内存增量）
- **默认模型目录**：`Y:\测试模型`
- **C++ 中文路径**：推荐调用 `dlcv_infer::Model(const std::wstring& modelPath, ...)`；若传 `std::string` 则必须是 GBK/本地 ANSI，不要传 UTF-8
- **C# 自测子命令**：`model-channel-order-selftest`、`dvs-rgb-selftest`、`demo2-rgb-selftest`

## 项目间依赖

- **底层推理引擎**：`dlcv_infer` 是 OpenIVS API 层的底层依赖。OpenIVS 的 C++ API（`dlcv_infer_cpp_dll`）和 C# API（`DlcvCsharpApi`）均通过加载 `dlcv_infer.dll`（Sentinel）或 `dlcv_infer_v.dll`（Virbox）调用推理能力。
- **加密模型文件**：`dlcv_deploy` 产出的 `.dvt`/`.dvo`/`.dvst`/`.dvso` 等文件是 OpenIVS 测试程序与 WPF 框架的输入。
- **接口边界**：OpenIVS 不解密模型包内 `dlcv.json` 来选择 provider；`DllLoader` 只读取模型包 `header_json.dog_provider`，Sentinel 使用 `dlcv_infer.dll`，Virbox 使用 `dlcv_infer_v.dll`。

## 运行验证方式

- 构建完成后运行 `DlcvDemo` 或 `dlcv_infer_cpp_qt_demo` 加载模型并执行单次推理验证。
- 使用 `Test/DlcvCSharpTest` 或 `Test/dlcv_infer_cpp_test` 执行自动化控制台测试（模型加载、推理、速度、内存）。
- C# 测试支持 `demo2-rgb-selftest` 验证 Demo2 入口 RGB 数据流一致性。
- 验证时禁止通过命令行传参覆盖模型路径或图片路径；需修改源码中的固定变量或配置对象字段。
