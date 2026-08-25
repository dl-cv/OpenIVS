# C++ 测试程序开发文档

**文档定位**：记录 `dlcv_infer_cpp_qt_demo` 的编译、运行方式与调试方法。所有内容以当前源码实现为准。

---

## 1. 项目概述

| 项目 | 说明 |
|------|------|
| 工程名称 | `dlcv_infer_cpp_qt_demo` |
| 路径 | `dlcv_infer_cpp_qt_demo/` |
| 类型 | Qt Widgets 桌面应用 |
| 用途 | C++ API 功能验证：模型加载、单图/批量推理、JSON输出、多线程压力测试、加密狗检测 |

核心文件：
- `main.cpp`：程序入口，初始化 Qt 应用，窗口关闭时调用 `FreeAllModels`
- `MainWindow.cpp`/`MainWindow.h`：主窗口，包含全部 UI 与业务逻辑
- `ImageViewerWidget.cpp`/`ImageViewerWidget.h`：图像与结果可视化组件

---

## 2. 编译步骤

### 2.1 依赖项

| 依赖 | 用途 |
|------|------|
| Qt 5/6 | UI 框架（QApplication、QMainWindow、QTimer 等） |
| OpenCV 4.x | 图像读取、通道转换、Mat 操作 |
| `dlcv_infer.h` + `dlcv_infer_cpp.lib` | C++ API 头文件与导入库 |
| `dlcv_infer_cpp.dll`（运行时） | OpenIVS C++ 与 C API 共用动态库 |

### 2.2 Visual Studio 编译

- 构建统一通过 `.cursor/skills/vs-build/scripts/build.py` 执行，目标为 `dlcv_infer_cpp_qt_demo/dlcv_infer_cpp_qt_demo.vcxproj`。
- 默认配置为 `Debug`、`x64`、`Build`、`minimal`；发布构建使用 `Release`、`x64`、`Build`、`minimal`。
- 项目通过 `ProjectReference` 构建 `dlcv_infer_cpp`；直接构建项目时从 `$(ProjectDir)..\dlcv_infer_cpp\$(Configuration)\` 解析导入库，解决方案构建时从 `$(SolutionDir)$(Configuration)\` 解析。
- Qt、OpenCV 与 DLCV SDK 依赖路径由工程属性解析；缺失时构建失败。

### 2.3 输出与部署

- 直接构建项目时，Debug 输出为 `dlcv_infer_cpp_qt_demo/Debug/dlcv_infer_cpp_qt_demo/dlcv_infer_cpp_qt_demo.exe`。
- 直接构建项目时，Release 输出为 `dlcv_infer_cpp_qt_demo/Release/dlcv_infer_cpp_qt_demo/dlcv_infer_cpp_qt_demo.exe`。
- 通过 `OpenIVS.sln` 构建时，EXE 输出位于解决方案根目录的 `Debug/dlcv_infer_cpp_qt_demo/` 或 `Release/dlcv_infer_cpp_qt_demo/`。
- 构建后事件把 `dlcv_infer_cpp.dll`、Qt Core/Gui/Widgets、平台插件和样式插件复制到 EXE 输出目录。
- 底层 `dlcv_infer.dll` 或 `dlcv_infer_v.dll` 仍按模型授权类型由 C++ API 从 SDK 路径加载。

---

## 3. 测试入口

### 3.1 程序入口（main.cpp）

- 无参数时初始化 `QApplication`、显示 `MainWindow`，退出前调用 `FreeAllModels()`。
- 有参数时解析 `infer`、`render`、`mask-visualization-selftest` 或 `--help`；命令行模式不进入主窗口事件循环，完成验证后直接返回退出码。
- Windows 控制台输入输出使用 UTF-8；模型路径通过 `std::wstring` 传给 C++ API。

### 3.2 命令行推理模式

```text
dlcv_infer_cpp_qt_demo.exe infer --model <path> --image <path> --threshold <0..1> [--device <int>] [--with-mask <true|false>] [--calc-mean <true|false>] [--output <jsonPath>]
dlcv_infer_cpp_qt_demo.exe render --model <path> --image <path> --threshold <0..1> --output <pngPath> [--device <int>] [--with-mask <true|false>]
dlcv_infer_cpp_qt_demo.exe mask-visualization-selftest
dlcv_infer_cpp_qt_demo.exe --help
```

- `--model`、`--image`、`--threshold` 为必填参数；`--device` 默认 `0`，`--with-mask` 默认 `true`，`--calc-mean` 默认 `false`。
- `--device=-1` 表示 CPU，非负整数表示 GPU 编号。
- 普通模型使用 `--threshold` 作为推理阈值；流程模型保留各模型节点自身的阈值，`--threshold` 只对最终对外结果进行筛选。
- 图片由 `QFile` 读取字节并通过 `cv::imdecode` 解码；BGR/BGRA 转为 RGB。
- 同一次命令分别调用 `Infer` 与 `InferOneOutJson`，输出字段与 C# 测试程序一致；开启均值计算时同时检查两种结果的均值字段。
- 输出中的 `inspection` 包含最近一次流程判定的 `present`、`ok`、`reason`，`inspection_consistent` 检查结构化与 JSON 两条路径的判定一致性。
- C++ 结构化结果的本地 GBK 类别名在 CLI 边界转换为 UTF-8，再写入 JSON。
- `infer --output` 使用 `QSaveFile` 原子写入 UTF-8 JSON；`render --output` 保存 `ImageViewerWidget` 的真实绘制结果，输出逻辑尺寸与原图一致。输出路径不得覆盖模型或图片，父目录必须存在。
- 退出码：`0` 为验证通过，`1` 为运行异常，`2` 为参数错误，`3` 为双路径不一致、存在低于阈值的结果或均值检查失败。
- `render` 使用原图作为底图，并把最终结果中的 ROI Mask 缩放到 bbox 后回贴到原图坐标；完整图 Mask 则从 `(0,0)` 绘制。
- `mask-visualization-selftest` 使用一个完整图 Mask 合成用例，检查其未被重复叠加 bbox 偏移；渲染结果写入系统临时目录的 `dlcv_mask_visualization_selftest.png`。

### 3.3 UI 布局

主窗口分为上下两部分：
- **上方控制栏**：按钮 + 参数调节控件
- **下方输出区**：左侧文本输出（`QPlainTextEdit`）+ 右侧图像可视化（`ImageViewerWidget`）

按钮列表：
| 按钮 | 功能 |
|------|------|
| 加载模型 | 打开文件对话框，选择 `.dvt`/`.dvo`/`.dvr`/`.dvst` |
| 获取模型信息 | 显示当前加载模型的元信息 JSON |
| 打开图片推理 | 选择图片并立即执行推理 |
| 单次推理 | 对当前已选图片执行推理 |
| 推理JSON | 以 JSON 格式输出单图推理结果 |
| 多线程测试 | 启动/停止压力测试 |
| 释放模型 | 释放当前模型 |
| 释放所有模型 | 调用 `FreeAllModels` |
| 文档 | 打开浏览器访问 `https://docs.dlcv.com.cn/deploy/sdk/csharp_sdk` |
| 检查加密狗 | 显示 Sentinel/Virbox 加密狗信息 |

单次结构化推理摘要按以下顺序显示模型路径、图片路径、`batch_size`、`threshold`、推理时间和推理结果数量。

参数控件：
| 控件 | 范围 | 默认值 | 说明 |
|------|------|--------|------|
| 选择显卡（下拉框） | CPU + 检测到的 GPU | GPU 0 | 设备选择 |
| batch_size（整数框） | 1~1024 | 1 | 批量推理大小 |
| threshold（浮点框） | 0.0~1.0 | 0.5 | 置信度阈值 |
| 计算均值（复选框） | 开启或关闭 | 关闭 | 是否计算实例分割目标的前景与背景均值 |
| 线程数（整数框） | 1~32 | 1 | 压力测试线程数 |

---

## 4. 常见测试场景

### 4.1 模型加载测试

1. 点击 **加载模型**，选择 `.dvt`（普通模型）或 `.dvst`（流程图归档）。
2. 加载成功后自动调用 **获取模型信息**，在文本区显示模型元信息。
3. 若加载失败，文本区显示异常消息（如加密狗不匹配、文件格式错误等）。

**代码路径**：`MainWindow::onLoadModel()`
- 使用 `QFileDialog` 选择文件，支持 `"AI模型 (*.dvt *.dvo *.dvr *.dvst);;所有文件 (*.*)"`。
- 释放旧模型后构造新 `dlcv_infer::Model`。
- 自动记录最近模型路径到 `QSettings`。

### 4.2 单图推理测试

1. 点击 **打开图片推理**，选择图片（`jpg/jpeg/png/bmp/gif/tiff/tif`）。
2. 图像经过 `prepareImageForInference` 转换为 RGB。
3. 调用 `model->InferBatch()` 执行推理。
4. 结果在文本区显示（数量、每个目标的类别、score、bbox、area、angle，以及可用的前景与背景均值）；流程失败原因存在时显示在预测结果下方。
5. 图像区显示可视化结果（bbox 框 + mask 叠加）；流程判定存在时左上角显示带阴影方块的绿色 `OK` 或红色 `NG`。

**代码路径**：`MainWindow::onInfer()`
- `prepareImageForInference`：将 OpenCV 读到的 BGR/BGRA 转换为 RGB。
- 若 `batchSize > 1`，将同一张图片复制为 batch。
- 参数 JSON：`{"threshold": ..., "with_mask": true, "calc_mean": ..., "batch_size": ...}`。

### 4.3 JSON 输出测试

1. 点击 **推理JSON**。
2. 调用 `model->InferOneOutJson()` 获取 JSON；未产生流程判定时为结果数组，产生判定时为包含 `result_list/ok/reason` 的包装对象。
3. 文本区显示格式化的 JSON（缩进 4）。

**代码路径**：`MainWindow::onInferJson()`
- 返回字段：`category_id`、`category_name`、`score`、`bbox`、`with_bbox`、`with_angle`、`angle`、`mask`（点数组）、`with_mask`、`area`、`with_mean`、`foreground_mean`、`background_mean`。

### 4.4 批量推理测试

1. 将 **batch_size** 设为 N（N > 1）。
2. 点击 **单次推理** 或 **打开图片推理**。
3. 程序将同一张图片复制 N 份作为 batch 输入。
4. 结果中 `sampleResults` 长度与 batch size 一致。

### 4.5 多线程压力测试

1. 设置 **batch_size** 和 **线程数**。
2. 点击 **多线程测试** 启动；按钮变为 **停止**。
3. 每个线程循环执行 `InferBatch`，直到点击 **停止**。
4. 每 500ms 更新统计信息：
   - 运行时间、完成请求数、平均延迟（ms）
   - 实时速率（请求/秒）
   - 若 Flow 模式，显示各节点平均耗时及占比

**代码路径**：`MainWindow::startPressureTest()` / `stopPressureTest()` / `updatePressureTestStatistics()`

**统计字段**：
```
压力测试统计:
线程数: 4
批量大小: 2
运行时间: 10.25 秒
完成请求: 5120
平均延迟: 15.32ms
实时速率: 498.12 请求/秒
模块平均耗时:
#0 [model] 检测模型: 8.45ms (55.1%)
#1 [postprocess] NMS: 2.12ms (13.8%)
```

### 4.6 加密狗检测

1. 点击 **检查加密狗**。
2. 文本区显示 Sentinel 和 Virbox 的设备和特性列表。
   - Virbox 查询同时覆盖实体设备描述和离线本地软锁描述；授权码软锁显示唯一锁号与许可 ID。
3. 若两者均为空，表示未检测到加密狗；此时 `DllLoader` 不加载推理 DLL。

**代码路径**：`MainWindow::onCheckDog()`
- 调用 `dlcv_infer::GetAllDogInfo()`。
- 底层 `AutoDetectProvider()`：Sentinel 优先、Virbox 第二；都没有返回 `Unknown`，不加载 `dlcv_infer.dll` / `dlcv_infer_v.dll`。

---

## 5. 调试技巧

### 5.1 图像解码问题

- 若图像显示为"图像解码失败"，检查：
  - 文件路径是否包含非 ASCII 字符（Qt 使用 `toLocal8Bit` 传给 OpenCV）。
  - OpenCV 运行时 DLL 是否缺失。

### 5.2 模型加载失败

- 检查加密狗是否插入并匹配模型要求的 provider。
- 检查 `dlcv_infer.dll` / `dlcv_infer_v.dll` 是否在 PATH 中。
- 查看文本区的异常堆栈，通常包含底层 C API 返回的错误 JSON。

### 5.3 推理结果为空

- 调低 **threshold**（如 0.1）再试。
- 检查输入图像通道：必须是 RGB（8UC3），程序内部已通过 `prepareImageForInference` 转换。

### 5.4 压力测试崩溃

- 检查 GPU 显存是否足够（batch_size × 线程数 × 单图显存）。
- 若使用 Flow 模式，检查各节点模型是否支持并发。

### 5.5 窗口位置异常

- 程序启动时检测窗口是否在所有屏幕外，若是则自动居中。
- 窗口几何状态保存在 `QSettings`（注册表），更换显示器后可能需手动调整。

---

## 6. 关键代码片段

### 6.1 图像预处理

```cpp
cv::Mat prepareImageForInference(const cv::Mat& decodedImage) {
    if (decodedImage.empty()) return {};
    if (decodedImage.channels() == 3) {
        cv::Mat rgb;
        cv::cvtColor(decodedImage, rgb, cv::COLOR_BGR2RGB);
        return rgb;
    }
    if (decodedImage.channels() == 4) {
        cv::Mat rgb;
        cv::cvtColor(decodedImage, rgb, cv::COLOR_BGRA2RGB);
        return rgb;
    }
    return decodedImage.clone();
}
```

### 6.2 推理调用

```cpp
json params;
params["threshold"] = spinThreshold_->value();
params["with_mask"] = true;
params["calc_mean"] = checkCalcMean_->isChecked();
params["batch_size"] = batchSize;

dlcv_infer::Result output = model_->InferBatch(imageList, params);
```

### 6.3 GPU 设备初始化

```cpp
// 在后台线程中调用，通过 QMetaObject::invokeMethod 回传结果到 UI 线程
dlcv_infer::Utils::KeepMaxClock();
json gpuInfo = dlcv_infer::Utils::GetGpuInfo();
```

---

*本文档只记录当前源码实现。如需了解 API 详细定义，参见 `C++ API文档.md`。*
