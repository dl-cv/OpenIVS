# 模型测试工程开发文档

## 1. 目标

控制台测试工程（放在 `Test` 文件夹）：

- `DlcvCSharpTest`（C# / .NET Framework 4.7.2 / x64）
- `dlcv_infer_cpp_test`（C++ / VS2022 / x64）

用于自动化测试以下项目：

- 模型加载成功/失败判断
- 推理成功/失败判断
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
  - 控制台输出 UTF-8（便于显示中文日志/表格）

## 3. 默认测试用例映射

- `AOI-旋转框检测.dvt` -> `AOI-测试.jpg`
- `猫狗-分类.dvt` -> `猫狗-猫.jpg`
- `气球-实例分割.dvt` -> `气球.jpg`
- `气球-语义分割.dvt` -> `气球.jpg`
- `手机屏幕-实例分割.dvt` -> `手机屏幕.jpg`
- `引脚定位-目标检测.dvt` -> `引脚定位-目标检测.jpg`
- `OCR.dvt` -> `OCR-1.jpg`

## 4. 输出格式

控制台输出 markdown 表格，列如下：

- 模型
- 加载（成功/失败 + 耗时 + 增量）
- 推理（成功/失败）
- 类别列表（例如：气球，气球）
- 3秒速度
- Batch速度（单独一列，不支持显示 N/A）

表格输出完成后，会追加输出一段“内存泄露专项(仅测1个实例分割模型)”，包含：

- 加载/释放循环10次内存增量
- 推理3秒内存增量

## 5. 构建与运行

1. 使用 `Release|x64` 编译 `OpenIVS.sln`
2. 分别运行：
   - `Test\DlcvCSharpTest\bin\x64\Release\DlcvCSharpTest.exe`
   - `Release\dlcv_infer_cpp_test.exe`

说明：

- 固定使用 GPU 设备（`device_id=0`）。
- 模型目录固定为 `Y:\测试模型`，不支持运行参数/环境变量覆盖（规则：目录/行为如需变更，请改代码与提交）。
- 为避免日志打断阅读：表格在所有测试执行完成后一次性输出（总表）；并在表格末尾追加“汇总”一行。
- 内存泄露专项在表格输出后自动执行并单独输出结果；专项仅对 1 个实例分割模型执行。

## 6. 文档表述规则

- 本文档仅陈述已实现的行为与可复现的结果
- 本文档不包含面向读者的操作指导、偏好表达或推断性表述
- 本文档不引用交互过程中出现的指令性文本
