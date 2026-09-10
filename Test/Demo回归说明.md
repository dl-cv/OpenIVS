# C、C++、C# Demo 回归检查

## 入口与范围

| 入口 | 检查内容 |
|---|---|
| `dlcv_infer_cpp_abi_selftest.exe` | 比较调用端与实际加载 DLL 的 ABI 代次、构建配置、迭代器级别及类型尺寸，输出实际 DLL 路径和 JSON |
| C++ Qt 主 Demo `ui-test` | 设备初始化、堆上模型加载、信息读取、批量推理、结果复制、绘制、释放和关闭 |
| `Test/run_demo_regression.py` | 从外部进程检查退出码、超时、独立输出文件、UTF-8 JSON、固定类别与数量基准 |
| `Test/test_demo_regression_runner.py` | 执行器的成功、异常退出、超时、缺失输出、编码和基准检查 |
| C 测试 `--c-api-invalid-input` / `--c-api-release-error` | 异常输入、释放失败及错误信息更新 |
| C Qt `--check-c-api-exports` | C 动态导出是否齐全，不代表模型推理通过 |
| C# 测试 `all-tests` | 当前 20 组检查，包含固定模型、掩膜与失败传播检查 |

构建使用项目指定的 `.cursor/skills/vs-build/scripts/build.py`。逐个工程串行构建，不构建包含其他组件的整个解决方案。日常默认 Debug/x64。

单项目与解决方案构建的输出目录可能不同。运行路径以本次构建日志的实际 EXE 输出为准，不能使用旧目录中同名程序代替。

`DlcvNativeRuntime.targets` 从 `ProjectReference` 的 `GetTargetPath` 取得本次 DLL，复制至调用程序目录；必需的 Qt 文件复制失败会使构建失败。C++ 客户端和 DLL 的 ABI 标签不兼容时，必须重新编译客户端，不能仅替换 DLL。

## 外部进程执行器

在仓库目录执行：

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest discover -s Test -p test_demo_regression_runner.py
python Test/run_demo_regression.py --schema-example
python Test/run_demo_regression.py <系统临时目录中的回归配置.json>
```

`--schema-example` 输出与执行器字段相符的 JSON 示例。配置包含构建类型、EXE、声明的 DLL、模型和图片路径、命令、超时与固定结果基准。路径可以按 Debug/Release 分别指定。示例类别不是本项目测试基准，执行前需填入实际已确认值。

每次执行创建新的系统临时目录，返回 `report.json` 路径。报告记录 EXE、声明 DLL、模型、图片和输出文件的 SHA256。声明 DLL 的哈希不是实际加载证明；ABI 自检另外报告实际加载 DLL 路径。

程序退出码非零、超时、缺少输出、JSON 无效、业务状态失败或结果基准不符，均不能通过。即使 JSON 表示成功，最终异常退出仍然失败。需要解析标准输出时，配置须指定实际编码，执行器严格解码，不用替换字符掩盖乱码。

## 本轮固定输入与基准

本轮使用现有 Sentinel 模型，不执行模型转换或加速。

| 模型 | 图片 | 参数 | 固定结果 |
|---|---|---|---|
| 猫狗-分类_120_50_s.dvt | 猫狗-狗.jpg | threshold=0.5，device=0，batch=1 | 首图 1 个结果，类别狗，score=0.9951171875，无掩膜 |
| 气球-实例分割_120_50_s.dvt | 气球.jpg | threshold=0.5，device=0，batch=4 | 4 个样本；首图 1 个气球，掩膜非零像素 227891 |

两项均检查设备初始化和关闭完成、最终退出码 0。当前批量固定数值基准针对首图，未声称逐张像素比较。

C 完整测试还要求 `DLCV_TEST_CORE_DLL` 指定本轮底层 `dlcv_infer.dll` 的绝对路径；缺失时明确失败。C# `RunAllTests.ps1` 使用 Release 测试程序，并拒绝早于相关源码的 EXE；本轮 Debug 测试直接调用本次构建的测试 EXE。

## 能识别故障的对照

- 现有异常 Release EXE 推理 JSON 和类别基准正确，但进程最终退出 `0xC0000409`，外部执行器判为失败。
- 同一个 C 释放错误测试配旧 DLL 失败，配修复后的 DLL 通过。
- 旧 C++ 客户端与带新 ABI 标签的 DLL 混用时，进程以 `0xC0000139` 拒绝加载，不进入错误尺寸的 Model 构造。

## 尚未覆盖

- 本轮未重新构建、验证 Release。
- C++ Demo2/3 已构建，尚未使用实际二级故障模型验证完整失败流程。
- 未验证系统文件选择窗口、所有 GPU/加密狗组合或全部并发释放场景。
- 原生标准输出存在混合编码，不能将完整日志标为编码检查通过。
- 本轮未配置 GitHub 必需检查，未修改仓库合并权限。
