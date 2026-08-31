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
  - 支持 `demo2-rgb-selftest` 子命令：反射调用 `DlcvDemo2.Form1` 的 `PrepareImageForModelInput` 与 `RunPipeline`，对同一张图分别执行“Demo2 实际入口 RGB”“手工 RGB”“原始 BGR”三种处理并输出签名比对结果

### 2.2 C++ 工程

- 工程名：`Test/dlcv_infer_cpp_test`
- 入口文件：`Test/dlcv_infer_cpp_test/main.cpp`
- 依赖项目：`dlcv_infer_cpp_dll`
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

- 用例
- 加载（成功/失败 + 耗时 + 增量 + provider + DLL 名）
- 推理（成功/失败）
- 结果校验（结果一致/失败字段）
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
| `list-models` | 无参数 |
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
- `1`：模型加载、接口调用、图片读取、推理或模型释放失败。
- `2`：命令不存在、位置参数数量错误、参数值错误或命令不支持指定可选参数。
- `3`：保留给程序启动阶段的未处理状态。

工作流结束时，进程会释放仍在当前上下文中的模型。

### 5.4 DVST 完整示例

以下示例在一个 C++ 进程内完成加载、信息查询、流程信息查询、结构化推理、释放：

```powershell
.\Test\dlcv_infer_cpp_test\Debug\dlcv_infer_cpp_test.exe load-model m1 "C:\Users\Administrator\Desktop\旋转测试\螺丝头部外观_120_50_s.dvst" --device 0 --then model-info m1 --then dvs-model-info m1 --then infer m1 "C:\Users\Administrator\Desktop\旋转测试\CCD2.2026-07-22 10-07-16-2240.bmp" --threshold 0.5 --then free-model m1
```

以下示例使用 C# 程序执行相同生命周期：

```powershell
.\Test\DlcvCSharpTest\bin\x64\Debug\DlcvCSharpTest.exe load-model m1 "C:\Users\Administrator\Desktop\旋转测试\螺丝头部外观_120_50_s.dvst" --device 0 --then model-info m1 --then dvs-model-info m1 --then infer m1 "C:\Users\Administrator\Desktop\旋转测试\CCD2.2026-07-22 10-07-16-2240.bmp" --threshold 0.5 --then free-model m1
```

## 6. 构建与运行

### 6.1 统一测试入口

`Test\DlcvCSharpTest\RunAllTests.ps1 [日志路径]` 是日常完整验证入口。脚本隐藏启动一次 `DlcvCSharpTest.exe all-tests`，统一收集 C# 和原生库输出，测试结束后只在控制台显示各组测试状态、耗时及最终统计。全部原始输出保存到一个日志文件；未提供日志路径时，日志保存为程序目录下的 `bin\x64\Release\DlcvCSharpTest-all-tests.log`。

测试程序在单个进程内依次执行 14 项无外部参数自测和 11 个固定模型回归用例。完整清单执行结束后再返回，任一测试失败时返回 `1`，参数或日志路径无效时返回 `2`。日常完整验证只需启动脚本一次，无须分别调用各个 `*-selftest` 子命令。

- 解决方案级构建、项目级构建与发布前构建验证统一通过 `.cursor/skills/vs-build/scripts/build.py` 执行，入口见 `开发文档.md` 的“统一编译说明”
- 运行文件：
  - `Test\DlcvCSharpTest\bin\x64\Release\DlcvCSharpTest.exe`
  - `Release\dlcv_infer_cpp_test.exe`
- 通过 `OpenIVS.sln` 构建时，`dlcv_infer_cpp_dll` 与 `dlcv_infer_cpp_test` 的 x64 产物输出到解决方案目录下的 `Debug` 或 `Release`。
- `DlcvCSharpTest.exe` 当前支持的专项自测子命令包括：
  - `model-channel-order-selftest`
  - `count-results-selftest`
  - `dvs-rgb-selftest <modelPath> <imagePath>`
  - `demo2-rgb-selftest <extractModelPath> <componentModelPath> <icModelPath> <imagePath>`
  - `flow-batch-selftest <modelPath> <imagePath> [batch]`
  - `calc-mean-selftest`
  - `category-count-check-selftest`
- `DlcvCSharpTest.exe` 与 `dlcv_infer_cpp_test.exe` 各自提供 `get-model-info <model>`，构造指定模型并把 `GetModelInfo` 返回的完整 JSON 写入标准输出。
- `DlcvCSharpTest.exe` 与 `dlcv_infer_cpp_test.exe` 各自提供 `get-dvs-model-info <model>`，构造指定模型并把 `GetDvsModelInfo` 返回的完整 JSON 写入标准输出；普通模型不支持该接口时，异常写入标准错误并返回非零状态。
- `get-model-info` 接收单个普通模型或流程模型路径；`get-dvs-model-info` 按 C# 公共接口支持范围接收 `.dvst`、`.dvso` 流程模型路径。命令不包含针对指定模型内容的预期值。
- 测试时直接按需调用 C#、C++ 可执行程序的上述命令，检查命令返回状态及标准输出中的 JSON。
- 两个命令成功返回 `0`，模型加载或接口调用异常返回 `1`，参数数量错误返回 `2`。
- `dlcv_infer_cpp_test.exe` 支持 `count-results-selftest`，验证新配置闭区间、非法范围与旧配置兼容逻辑。
- `DlcvCSharpTest.exe category-count-check-selftest` 与 `dlcv_infer_cpp_test.exe category-count-check-selftest` 验证类型数量规则、同一原图局部结果聚合、粘性 `ok=false`、字符串或数组 `reason`、Flow 输出包装及旧流程兼容行为。
- `dlcv_infer_cpp_test.exe` 支持三模型加载计时子命令：
  - `load-three-models <extractModelPath> <componentModelPath> <icModelPath>`
  - 三个模型按参数顺序串行加载，实时输出各模型加载耗时，完成后输出三次耗时之和。
  - 命令行使用宽字符参数接收中文路径，固定使用 `device_id=0`，不加载单独的预热模型，也不执行额外推理。
  - 流程模型加载期间保留已经加载成功的模型模块，流程对象取得模型池引用后再释放临时模块；每个不同的子模型只执行一次原生加载。
  - 成功返回 `0`，模型加载异常返回 `1`，参数数量错误返回 `2`。
- `DlcvCSharpTest.exe calc-mean-selftest` 检查结果构造函数、均值字段，以及 Flow 节点默认值、入口显式覆盖和后续恢复。
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
