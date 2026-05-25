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

## 修复方案：模型加载后使用 opt shape 预热推理

在 C# `Model` 加载完成后，立即使用模型 `max_shape`（即 opt shape）构造一张空白图像执行一次推理，走通 `PrepareInferImages` → `InferInternal` → `ParseToStructResult` 完整路径，让 .NET JIT 在业务首次真实推理前完成编译。

### 已修改文件

1. **`DlcvCsharpApi/Model.cs`**
   - 新增 `TryExtractSpatialDims`：从 `max_shape` 解析空间维度（支持 NCHW / NHWC / CHW / HWC）。
   - 新增 `WarmupInfer`：在模型加载成功后，使用解析出的 H/W 和通道数创建 `Mat.Zeros`，调用 `InferBatch` 执行一次预热推理并释放结果。
   - `Model` 构造函数：在 `TryCacheModelInfo()` 之后调用 `WarmupInfer()`。
   - `SlidingWindowModel` 构造函数：在 `TryCacheModelInfo()` 之后调用 `WarmupInfer()`。

2. **已移除的 NGEN 相关文件**
   - 删除 `ngen_installed_dlls.py`
   - 从 `1_编译打包.bat` 中移除 NGEN 预编译逻辑

## 验证方式

加载模型后立即执行两次推理，观察耗时是否一致（均应在 ~1ms 级别，不再出现首次 ~7ms、后续 ~1ms 的差距）。
