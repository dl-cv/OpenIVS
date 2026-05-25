# C# 首次推理延迟修复记录

## 问题描述

OpenIVS C# API 首次推理显著慢于 C++：
- 猫狗分类：C# ~15ms vs C++ ~1ms
- 气球分割：C# ~97ms vs C++ ~93ms

## 根因分析

通过在 `InferInternalDvt` / `InferInternal` / `InferBatch` 中插入 Stopwatch 分段计时，定位到额外开销来自 **.NET JIT（Just-In-Time）编译**：

| 阶段 | 首次调用耗时 | 说明 |
|---|---|---|
| `PrepareInferImages` | ~3 ms | JIT 编译图像预处理链 |
| `ParseToStructResult` | ~6 ms | JIT 编译结果解析大方法 |
| `InferInternal` 其他 | ~3 ms | `InferInternalDvt` 外其他首次调用开销 |

C++ 后端 `dlcv_infer` 本身仅 ~1.3ms，C# 层的 JIT 编译额外增加了 ~12ms。

## 修复方案：NGEN 预编译

使用 .NET Framework 自带的 `ngen.exe`（Native Image Generator）对关键 DLL 执行预编译，将 IL 代码提前编译为原生机器码，彻底消除首次调用的 JIT 开销。

### 已修改文件

1. **`ngen_installed_dlls.py`**（新增）
   - 通过 `pip show` 定位 `dlcvpro_infer_csharp` 的实际安装路径
   - 对以下 DLL 执行 `ngen.exe install`：
     - `DlcvCsharpApi.dll`
     - `OpenCvSharp.dll`
     - `Newtonsoft.Json.dll`

2. **`2_安装新版.bat`**
   - 在 `pip install` 完成后，调用 `ngen_installed_dlls.py`
   - 确保每次安装新版后自动对 site-packages 中的 DLL 做 NGEN

3. **`1_编译打包.bat`**
   - 在构建 wheel 前，对本地 `dlcvpro_infer_csharp\` 目录下的 DLL 执行 NGEN
   - 确保开发环境本地测试（如 `DlcvCSharpTest.exe`）也能享受优化

4. **`DlcvCsharpApi/Model.cs`**
   - 移除了调试用的 Stopwatch 分段计时和 `Console.WriteLine`

5. **`Test/DlcvCSharpTest/Program.cs`**
   - 在 `RunCase` 中为推理添加了 `Stopwatch` 计时打印

## 验证结果

| 模型 | NGEN 前 | NGEN 后 |
|---|---|---|
| 猫狗-分类 | 14.85 ms | **3.65 ms** |
| 气球-实例分割 | 96.21 ms | 94.04 ms（后端占主导，变化不明显） |

## 待排查残留

用户反馈某些入口仍存在首次推理 ~7ms、后续 ~1ms 的差距：

```
推理时间: 7.02ms
推理时间: 1.06ms
```

可能原因：
- 该入口加载的 `DlcvCsharpApi.dll` 路径未被 NGEN 覆盖
- 或存在其他首次调用开销（如 `DllLoader` 的首次 P/Invoke 加载、OpenCV 初始化等）
- 需要进一步在该入口执行路径上插入分段计时定位
