# 模型测试工程开发文档

## 1. 目标

控制台测试工程（放在 `Test` 文件夹）：

- `DlcvCSharpTest`（C# / .NET Framework 4.7.2 / x64）
- `dlcv_infer_cpp_test`（C++ / VS2022 / x64）

用于自动化测试以下项目：

- 模型加载成功/失败判断
- 推理成功/失败判断
- 固定模型预测结果回归校验
- 推理结果类别列表输出（按出现次数展开）
- 3 秒平均推理速度
- Batch 推理速度（单独字段）
- 普通模型与流程模型的共享 index 查询、恢复推理和绑定生命周期
- 实际产品输入由模型加速器一次生成，每次只选择一种加密狗格式，同一产物及流程内子模型只包含该格式。非该生成流程得到的混合格式文件不属于产品输入，不用于扩展接口能力。
- 内存泄露专项：仅对 1 个实例分割模型执行
  - 加载/释放循环 10 次的内存增量
  - 推理 3 秒内存增量

默认模型目录：`Y:\测试模型`
测试 `.dvt` 模型。

## 2. 工程说明

### 2.1 C# 工程

- 工程名：`Test/DlcvCSharpTest`
- 入口文件：`Test/DlcvCSharpTest/Program.cs`
- 依赖项目：`DlcvCsharpApi`
- 关键点：
  - 推理前将图片从 BGR 转为 RGB
  - 结果中的 `Mask` 显式 `Dispose`
  - 内存采样使用 `GetProcessMemoryInfo`
  - 支持 `demo2-rgb-selftest` 子命令：反射驱动 `DlcvDemo2.Form1` 的私有图片加载与 `RunPipeline`，对同一张图分别执行“Demo2 实际入口 RGB”“手工 RGB”“原始 BGR”三条路径并输出签名比对结果
  - 支持 `shared-index-csharp-selftest` 子命令：验证 C# 加载普通模型后底层 index 查询、底层 C 接口加载普通模型后 C# 恢复、C# 加载 DVST 后流程注册信息读取与 C# 恢复，并检查借用实例释放后持有方仍可推理、持有方释放后 index 被清理

### 2.2 C++ 工程

- 工程名：`Test/dlcv_infer_cpp_test`
- 入口文件：`Test/dlcv_infer_cpp_test/main.cpp`
- 依赖项目：`dlcv_infer_cpp`
- 关键点：
  - 头文件通过工程依赖配置（`AdditionalIncludeDirectories`）引入，代码中使用 `#include "dlcv_infer.h"`，不使用相对路径包含
  - 使用 `GetProcessMemoryInfo` 采样私有内存与工作集
  - 中文路径图片读取使用 `fopen + imdecode`
  - **模型路径编码（非常关键）**：
    - 若调用 `dlcv_infer::Model(const std::string& modelPath, ...)`：`modelPath` 必须是 **GBK(936)/本地 ANSI** 字符串，不要传 UTF-8；否则路径会被二次转换，常见报错为 `load model failed: {"code":1,"message":"[ModelInternal::decode_file] Failed to open file"}`
    - 若调用 `dlcv_infer::Model(const std::wstring& modelPath, ...)`（推荐）：可直接传 Windows UTF-16 路径，内部处理转码，避免测试代码到处写转换函数
  - Windows 控制台保持 GBK。程序内部生成的 UTF-8 文本在输出前转换为 GBK，再通过 `cout` 或 `cerr` 输出，不设置控制台代码页；模型结果中已经是本地编码的类别名不重复转换。

## 3. 默认固定模型回归用例

- `测试无监督-v5_120_50_s.dvt` -> `1786969663716.jpg`
- `猫狗-分类_120_50_s.dvt` -> `猫狗-狗.jpg`
- `猫狗-分类_120_50_v.dvt` -> `猫狗-狗.jpg`
- `气球-大模型_20260830_010011_120_50_s.dvt` -> `气球.jpg`
- `气球-实例分割_120_50_s.dvt` -> `气球.jpg`
- `气球-实例分割_120_50_v.dvt` -> `气球.jpg`
- `气球-语义分割_120_50_s.dvt` -> `气球.jpg`
- `手机屏幕-实例分割_120_50_s.dvt` -> `手机屏幕.jpg`
- `引脚定位-目标检测_120_50_s.dvt` -> `引脚定位-目标检测.jpg`
- `AOI-旋转框检测_120_50_s.dvt` -> `AOI-测试.jpg`
- `OCR_120_50_s.dvt` -> `OCR-472.jpg`

模型与图片从 `Y:\测试模型` 读取。模型或图片缺失时用例失败。

每个用例依次校验样本数量、结果数量、`category_id`、`category_name`、`with_bbox`、`with_mask`、`with_angle`、score、bbox、area、angle 和 mask。数值容差如下：

- score：`0.002`
- bbox：`1.0`
- area：`128.0`
- angle：`0.05`
- mask 非零像素数：`128`

mask 校验包含单通道、宽度、高度和非零像素数。DVT 的 mask 通过 C# API 按 bbox 尺寸缩放，固定基准记录 `CSharpObjectResult.Mask` 的实际输出。

## 4. 输出格式

控制台输出 markdown 表格，列如下：

- 模型
- 加载（成功/失败 + 耗时 + 增量 + provider + DLL 名）
- 推理（成功/失败）
- 类别列表（例如：气球，气球）
- 3秒速度
- Batch速度（单独一列，不支持显示 N/A）

表格输出完成后，会追加输出一段“内存泄露专项(仅测1个实例分割模型)”，包含：

- 加载/释放循环10次内存增量
- 推理3秒内存增量

## 5. 可组合命令行

### 5.1 产物与调用方式

两套测试程序提供相同的工作流命令，命令在同一个进程内按顺序执行。`--then` 用于连接多个命令；模型名称只在当前进程内有效，模型释放或进程结束后失效。标准生命周期为“加载 → 信息 → 推理 → 释放”。

- C#：`Test\DlcvCSharpTest\bin\x64\Debug\DlcvCSharpTest.exe`
- C++：`Test\dlcv_infer_cpp_test\Debug\dlcv_infer_cpp_test.exe`

两套程序均提供以下命令：

- `help`
- `load-model`
- `list-models`
- `all-models`（C#，输出按实际已加载模块分组的底层资源快照）
- `model-info`
- `dvs-model-info`
- `infer`
- `infer-json`
- `infer-batch`
- `benchmark`
- `consistency-test`
- `free-model`
- `free-all-models`
- `device-info`
- `gpu-info`
- `dog-info`
- `keep-max-clock`

### 5.2 命令参数

通用参数如下，参数名和取值格式以程序帮助输出为准：

| 命令 | 参数 |
| --- | --- |
| `help` | 无位置参数、无可选参数 |
| `load-model` | `<名称> <模型路径>`；C# 支持 `--device N`、`--rpc true\|false`、`--replace true\|false`；C++ 支持 `--device N`、`--replace true\|false` |
| `list-models` | 无参数，显示当前工作流名称表 |
| `all-models` | 无参数；C# 输出 `Utils.GetAllModels()` JSON，不额外加载其他推理模块 |
| `model-info` | `<名称>` |
| `dvs-model-info` | `<名称>` |
| `infer` | `<名称> <图片>`；支持 `--threshold F`、`--with-mask true\|false`、`--calc-mean default\|true\|false` |
| `infer-json` | `<名称> <图片>`；支持 `--threshold F`、`--with-mask true\|false`、`--calc-mean default\|true\|false` |
| `infer-batch` | `<名称> <图片>`；支持 `--batch-size N`、`--threshold F`、`--with-mask true\|false`、`--calc-mean default\|true\|false` |
| `benchmark` | `<名称> <图片>`；支持 `--batch-size N`、`--warmup N`、`--runs N`、`--threads N`、`--threshold F`、`--with-mask true\|false`、`--calc-mean default\|true\|false` |
| `consistency-test`（C#） | `<名称> <图片>`；支持 `--batch-size N`、`--warmup N`、`--runs N`、`--threads N`、`--threshold F`、`--with-mask true\|false`、`--calc-mean default\|true\|false` |
| `consistency-test`（C++） | `<名称> <图片>`；支持 `--runs N`、`--threads N`、`--threshold F`、`--with-mask true\|false`、`--calc-mean default\|true\|false` |
| `free-model` | `<名称>` |
| `free-all-models` | 无参数 |
| `device-info`、`gpu-info`、`dog-info`、`keep-max-clock` | 无参数 |

其中 `N` 为整数，`F` 为 0 到 1 之间的数值。`--then` 本身不属于任何单独命令的可选参数。

### 5.3 返回状态

- `0`：命令或完整命令串执行成功。
- `1`：模型加载、接口调用、图片读取、推理或释放参数无效。除 `-1` 外的有效 `int` 编号的释放即使资源已不存在或底层报错也按成功处理。
- `2`：命令不存在、位置参数数量错误、参数值错误或命令不支持指定可选参数。
- `3`：保留给程序启动阶段的未处理状态。

工作流结束时，进程会释放仍在当前上下文中的模型。

### 5.4 DVST 完整示例

以下示例在一个 C++ 进程内完成加载、信息查询、流程信息查询、结构化推理、释放：

```powershell
.\Test\dlcv_infer_cpp_test\Debug\dlcv_infer_cpp_test.exe load-model m1 ".\test-data\model.dvst" --device 0 --then model-info m1 --then dvs-model-info m1 --then infer m1 ".\test-data\image.bmp" --threshold 0.5 --then free-model m1
```

以下示例使用 C# 程序执行相同生命周期：

```powershell
.\Test\DlcvCSharpTest\bin\x64\Debug\DlcvCSharpTest.exe load-model m1 ".\test-data\model.dvst" --device 0 --then model-info m1 --then dvs-model-info m1 --then infer m1 ".\test-data\image.bmp" --threshold 0.5 --then free-model m1
```

## 6. 构建与运行

- 产品打包使用根目录 `1_编译打包.bat`，仅构建 C# 发布所需项目，不构建控制台回归测试。
- 在 Visual Studio 中构建所需的 C++、C、C# 测试工程，再按需要运行以下测试命令。
- `DlcvCSharpTest` 以仅构建方式引用 `DlcvDemo`、`DlcvDemo2`。构建控制台测试工程时同步生成反射自测需要的两个程序；Demo 程序集不作为测试工程的编译引用。

### 6.1 统一测试入口

- 统一测试只使用产品实际 DLL 和现有产品接口。首次普通模型按模型头选择默认 DLL，后续普通模型沿用该 DLL并逐个检查授权；共享 index 按进程内实际加载 DLL 查询。
- 测试代码只放在测试工程，不要求生产 DLL 增加测试导出，不通过替换推理 DLL或另设测试 DLL 构造测试入口。实际模型测试输入遵循单一加密狗格式规则。

`Test\\DlcvCSharpTest\\RunAllTests.ps1 [日志路径]` 是完整验证入口。脚本启动一次 `DlcvCSharpTest.exe all-tests`，统一收集 C# 和原生库输出；测试结束后在控制台显示各组测试状态、耗时及最终统计，原始输出保存到一个日志文件。未提供日志路径时，日志保存为程序目录下的 `bin\\x64\\Release\\DlcvCSharpTest-all-tests.log`。

测试程序在单个进程内依次执行无外部参数自测和固定模型回归用例。完整清单执行结束后返回，任一测试失败时返回 `1`，参数或日志路径无效时返回 `2`。

- 单项目日常构建使用 `.cursor/skills/vs-build/scripts/build.py`；产品打包与完整回归构建分别使用上述两个独立入口。
- 运行文件：
  - `Test\DlcvCSharpTest\bin\x64\Release\DlcvCSharpTest.exe`
  - `Release\dlcv_infer_cpp_test.exe`
- 通过 `OpenIVS.sln` 构建时，`dlcv_infer_cpp` 与 `dlcv_infer_cpp_test` 的 x64 产物输出到解决方案目录下的 `Debug` 或 `Release`。
- `DlcvCSharpTest.exe` 当前支持的专项自测子命令包括：
  - `model-channel-order-selftest`
  - `cli-anomaly-threshold-selftest`
  - `count-results-selftest`
  - `dvs-rgb-selftest <modelPath> <imagePath>`
  - `demo2-rgb-selftest <extractModelPath> <componentModelPath> <icModelPath> <imagePath>`
  - `flow-batch-selftest <modelPath> <imagePath> [batch]`
  - `calc-mean-selftest`
  - `category-count-check-selftest`
  - `shared-index-csharp-selftest <model.dvo> <flow.dvst> <image> [deviceId]`
  - `shared-index-review-selftest [model.dvo] [flow.dvst]`
  - `shared-index-format-selftest`
  - `shared-index-provider-model-selftest`

  - `ui-test-options-selftest`
  - `winforms-mainwindow-selftest`
- `cli-anomaly-threshold-selftest` 不读取模型和图片，检查 CLI 对异常分数、普通分类低分、两路结果不一致及非有限分数的验证结果；运行前需先构建 `DlcvDemo.csproj`。
- `ui-test-options-selftest` 反射调用 `DlcvDemo.UiTestOptions.TryParse`，覆盖 `--screenshot` 的 `.png`/`.PNG` 与省略场景，非 `.png` 后缀拒绝，`--screenshot` 与 `--model`、`--image`、`--output`、`--output.tmp` 相同的输出碰撞拒绝，以及 `--output` 的中间 `.tmp` 路径与 `--model`、`--image` 重合拒绝；运行前需先构建 `DlcvDemo.csproj`。
- `winforms-mainwindow-selftest` 在 STA 线程内反射创建真实 `DlcvDemo.MainWindow`（不显示、不启用设备线程），校验 Form 类型、控件 Name/文本与默认 Enabled、三个 NumericUpDown 的范围与默认值及 threshold 步进 `0.05`、原生 Flat 按钮蓝/灰/红配色及 MouseOver/MouseDown 差异、三态计算均值 Indeterminate/true/false 映射，并将窗口设为 MinimumSize 后校验 threshold 与 calc_mean 完整位于父容器 ClientRectangle 内；运行前需先构建 `DlcvDemo.csproj`。
- `DlcvCSharpTest.exe` 与 `dlcv_infer_cpp_test.exe` 各自提供 `get-model-info <model>`，构造指定模型并把 `GetModelInfo` 返回的完整 JSON 写入标准输出。
- `DlcvCSharpTest.exe` 与 `dlcv_infer_cpp_test.exe` 各自提供 `get-dvs-model-info <model>`，构造指定模型并把 `GetDvsModelInfo` 返回的完整 JSON 写入标准输出；普通模型不支持该接口时，异常写入标准错误并返回非零状态。
- `get-model-info` 接收单个普通模型或流程模型路径；`get-dvs-model-info` 按 C# 公共接口支持范围接收 `.dvst`、`.dvso` 流程模型路径。命令不包含针对指定模型内容的预期值。
- 测试时直接按需调用 C#、C++ 可执行程序的上述命令，检查命令返回状态及标准输出中的 JSON。
- 两个命令成功返回 `0`，模型加载或接口调用异常返回 `1`，参数数量错误返回 `2`。

- `dlcv_infer_cpp_test.exe` 支持 `count-results-selftest`，验证新配置闭区间、非法范围与旧配置兼容逻辑。
- `dlcv_infer_cpp_test.exe` 还支持以下流程模型专项自测：
  - `dvs-rgb-selftest <modelPath> <imagePath> [require-preserved-mask]`
  - `dvs-memory-loading-selftest <modelPath> <imagePath> [device]`
  - `dvsp-reject-selftest <modelPath> [device]`
  - `undersized-model-selftest`：在临时目录生成小于 1MB 的 `.dvt/.dvo/.dvp/.dvst/.dvso`，检查返回损坏/不完整错误；`.dvsp` 仍返回不支持
  - `create-model-from-index-selftest <modelPath> [device]`
- `dlcv_infer_cpp_test.exe sliding-merge-selftest` 为无模型回归，覆盖带滑窗元信息的单窗口小数框、正负半整数取整、多窗口合并框和分组首次出现顺序。
- `create-model-from-index-selftest` 检查 C++ 头文件内联辅助函数创建时增加 index 使用次数、对象释放时减少使用次数；流程模型还会检查 `FreeAllModels()` 清空模型池、旧流程对象在底层释放失败后完成本地清理，以及相同流程能够重新加载。
- `DlcvCSharpTest.exe category-count-check-selftest` 与 `dlcv_infer_cpp_test.exe category-count-check-selftest` 验证类型数量规则、同一原图局部结果聚合、粘性 `ok=false`、字符串或数组 `reason`、Flow 输出包装及旧流程兼容行为。
- `dlcv_infer_cpp_test.exe` 支持三模型加载计时子命令：
  - `load-three-models <extractModelPath> <componentModelPath> <icModelPath>`
  - 三个模型按参数顺序串行加载，实时输出各模型加载耗时，完成后输出三次耗时之和。
  - 命令行使用宽字符参数接收中文路径，固定使用 `device_id=0`，不加载单独的预热模型，也不执行额外推理。
  - 流程模型加载期间保留已经加载成功的模型模块，流程对象取得模型池引用后再释放临时模块；每个不同的子模型只执行一次原生加载。
  - 成功返回 `0`，模型加载异常返回 `1`，参数数量错误返回 `2`。
- `DlcvCSharpTest.exe calc-mean-selftest` 检查结果构造函数、均值字段，以及 Flow 节点默认值、入口显式覆盖和后续恢复。
- `DlcvCSharpTest.exe shared-index-format-selftest` 保持 DVT、DVO、DVST 的既有推理比较范围，分别检查 C# 持有/C++ 借用、C++ 持有/C# 借用两种方向。DVSO 的索引共享、描述恢复与释放由混编工程的四格式回归检查。
- `shared-index-csharp-selftest <普通模型> <DVSO流程> <图片> <设备编号>` 可单独验证 DVSO。正式加速器从 AOI DVSP 生成的 ONNX Runtime DVSO，在 CPU -1 与 GPU 0 下均通过双向共享及逐目标推理比较。同一加速配置生成的无 CAD DVSO 在 C++ 滑窗合并按端点取整并保持分组输出顺序后，CPU -1、GPU 0 的最终输出均为两种语言各 34 个目标，完整结果数组及顺序一致；CPU 双向共享推理检查通过。
- `DlcvCSharpTest.exe shared-index-provider-model-selftest` 使用两种加密狗格式各自独立生成的普通模型，验证实际加载 DLL 的索引归属、双向读取和推理结果；两个模型作为各自独立的实际产品输入，不组成混合格式流程，也不以编号数值推导资源类型。
  - `all-tests` 已加入上述共享 index 测试；任一专项返回非零时，统一测试返回失败。
- `DlcvCSharpTest.exe native-c-api-regression-selftest` 使用正式 C ABI 检查不存在模型的 `code=2` 和 `Model not found.`、有效编号重复释放成功，以及 JSON 编号范围和类型。
- `DlcvCSharpTest.exe shared-index-native-rule-selftest` 调用编号脚本输出的 `Release/dlcv_infer_cpp_test.exe shared-index-rules-selftest`，检查退出码并按原生程序的 GBK 输出严格解码；跨语言模型加载与推理仍在同一进程使用正式 C ABI 验证。
- `DlcvCSharpTest.exe shared-index-csharp-selftest` 依次验证普通模型与传入 DVS 流程的双向共享：C# 文件加载后供正式 C/C++ 入口按 index 使用，以及 C/C++ 文件加载后由 `ModelFactory.CreateFromIndex` 恢复。每个方向均检查创建方先释放后共享方仍可读取和推理、共享方先释放后创建方仍可使用、重复释放不重复消耗持有、最终释放后 index 消失。
- `empty-dvs-first-load-selftest` 在没有预先加载推理模块的进程中创建空 DVS，检查默认模块选择、完整 DVS 信息和释放后的索引失效。
- `shared-index-route-selftest` 使用可控委托检查最终五接口、四个共享 JSON 接口的结果释放、唯一模块选择、多模块同编号错误、`code=2` 不存在结果、查询错误不改选、负数 index 拒绝、DVS 登记字段、DVS 查询结果、统一普通释放和 DVS 子模型执行对象不重复 bind/free。
- `shared-index-review-selftest` 使用实际普通模型与 DVST 检查两个共享方生命周期、DVS 子模型持有、最终释放、`Utils.GetAllModels()` 模块快照和列表查询不额外加载 DLL。
- `free-all-modules-selftest` 显式使用两个已加载推理模块，确认相同编号仍分别保留在各自模块快照中，并由 `Utils.FreeAllModels()` 清理全部模块。
- DVS 使用与普通模型相同的非负 `model_index`。恢复时先调用 `dlcv_bind_index`，再读取 `dlcv_get_dvs_model` 描述并创建 C# 执行对象；失败和正常释放均使用普通 `dlcv_free_model` 配对。`GetModelInfo()` 保持普通模型兼容结构，`GetDvsModelInfo()` 返回完整 DVS 信息。
- `dvsp-disabled-selftest` 检查 C# API 对 `.dvsp` 直接返回不支持错误。
- `dlcv_infer_cpp_test.exe calc-mean-selftest` 检查旧版 `ObjectResult` 构造函数的默认均值、新版构造函数的显式均值字段，以及结构化 JSON 结果的均值解析和缺失字段默认值。

说明：

- 固定使用 GPU 设备（`device_id=0`）。
- 默认批量测试的模型目录固定为 `Y:\测试模型`；`load-three-models` 的三个模型路径由命令行参数提供。
- 为避免日志打断阅读：表格在所有测试执行完成后一次性输出（总表）；并在表格末尾追加“汇总”一行。
- 内存泄露专项在表格输出后自动执行并单独输出结果；专项仅对 1 个实例分割模型执行。
- `demo2-rgb-selftest` 会输出 `entry_rgb_signature`、`manual_rgb_signature` 与 `raw_bgr_signature`；当 `entry_rgb_signature == manual_rgb_signature` 且与 `raw_bgr_signature` 不同时，判定 Demo2 当前入口保持 RGB 数据流。
- `flow-batch-selftest` 输出每个模型节点的输入数、batch 上限、底层调用次数与最大实际子批；存在多张二阶段输入且最大实际子批大于 1 时通过。

## 7. 文档表述规则

- 本文档仅陈述已实现的行为与可复现的结果
- 本文档不包含面向读者的操作指导、偏好表达或推断性表述
- 本文档不引用交互过程中出现的指令性文本

## C# 共享实现位置

C# 在 `DlcvCsharpApi/DllLoader.cs` 中直接解析实际已加载推理模块的最终五接口，不建立托管或 OpenIVS 原生流程记录表。`ModelFactory.CreateFromIndex` 负责模块查询、绑定、DVS 描述读取与失败配对释放；`DvsModel.LoadFromModelBindings` 根据描述建立 C# 执行对象。`Utils.GetAllModels()` 按模块保留底层快照并增加 `module_path`。`DlcvCsharpApi.csproj` 不引用 `dlcv_infer_cpp.vcxproj`，C# 发布清单不复制 `dlcv_infer_cpp.dll` 或其 OpenCV 运行库；混编测试工程仍保留自身所需的 C++/CLI 与包装 DLL 依赖。

### 流程阶段比较

`flow-stage-compare <model> <image> <device> <output-dir> <node-ids>` 在同一进程加载一次 DVS，复用底层子模型索引，为指定节点的执行前缀登记独立测试流程，选择该节点第一个图像与结果输出端口，以同一 RGB 图像和参数分别执行 C# 与正式 C/C++ JSON 入口。节点编号以逗号分隔，输出目录必须尚不存在。结果为 UTF-8 JSON，保存两种语言的结果与差异、子模型索引、实际模块路径与摘要，并检查输入图像在两次调用前后不变；原归档不修改。退出 0 表示所选节点一致，1 表示存在差异。
