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
- 项目通过 `ProjectReference` 构建并链接 `dlcv_infer_cpp`，不从其他输出目录查找同名导入库。
- Qt、OpenCV 与 DLCV SDK 依赖路径由工程属性解析；缺失时构建失败。

### 2.3 输出与部署

- 直接构建项目时，Debug 输出为 `dlcv_infer_cpp_qt_demo/Debug/dlcv_infer_cpp_qt_demo/dlcv_infer_cpp_qt_demo.exe`。
- 直接构建项目时，Release 输出为 `dlcv_infer_cpp_qt_demo/Release/dlcv_infer_cpp_qt_demo/dlcv_infer_cpp_qt_demo.exe`。
- 通过 `OpenIVS.sln` 构建时，EXE 输出位于解决方案根目录的 `Debug/dlcv_infer_cpp_qt_demo/` 或 `Release/dlcv_infer_cpp_qt_demo/`。
- `DlcvNativeRuntime.targets` 从工程引用取得本次 DLL 并复制至 EXE 目录；必需 Qt DLL 和平台插件复制失败会使构建失败，样式插件存在时复制。
- 底层 `dlcv_infer.dll` 或 `dlcv_infer_v.dll` 由首次普通模型的模型头确定为默认 DLL；后续普通模型沿用该 DLL并逐个检查授权。

---

## 3. 测试入口

### 3.1 程序入口（main.cpp）

- 无参数时按 Windows 图形界面子系统启动，初始化 `QApplication`、显示 `MainWindow`，不创建控制台窗口，退出前调用 `FreeAllModels()`。
- 有参数时连接父控制台；没有父控制台时创建控制台，再解析 `infer`、`render` 或 `--help`。命令行模式不进入主窗口事件循环，完成验证后直接返回退出码。
- Windows 控制台输入输出使用 UTF-8；模型路径通过 `std::wstring` 传给 C++ API。

### 3.2 命令行推理模式

```text
dlcv_infer_cpp_qt_demo.exe infer --model <path> --image <path> --threshold <0..1> [--device <int>] [--with-mask <true|false>] [--calc-mean <true|false>] [--output <jsonPath>]
dlcv_infer_cpp_qt_demo.exe render --model <path> --image <path> --threshold <0..1> --output <pngPath> [--device <int>] [--with-mask <true|false>]
dlcv_infer_cpp_qt_demo.exe --help
```

- `--model`、`--image`、`--threshold` 为必填参数；`--device` 默认 `0`，`--with-mask` 默认 `true`，`--calc-mean` 默认 `false`。
- `--device=-1` 表示 CPU，非负整数表示 GPU 编号。
- 普通模型使用 `--threshold` 作为推理阈值；流程模型保留各模型节点自身的阈值，`--threshold` 只对最终对外结果进行筛选。
- 图片由 `QFile` 读取字节并通过 `cv::imdecode` 解码；BGR/BGRA 转为 RGB。
- 同一次命令分别调用 `Infer` 与 `InferOneOutJson`，输出字段与 C# 测试程序一致；开启均值计算时同时检查两种结果的均值字段。
- 输出中的 `inspection` 包含最近一次流程判定的 `present`、`ok`、`reason`，`inspection_consistent` 检查结构化与 JSON 两条路径的判定一致性。
- C++ 结构化结果的本地 GBK 类别名在 CLI 输出时转换为 UTF-8，再写入 JSON。
- `infer --output` 使用 `QSaveFile` 写入 UTF-8 JSON；`render --output` 保存 `ImageViewerWidget` 的真实绘制结果，输出逻辑尺寸与原图一致。输出路径不得覆盖模型或图片，父目录必须存在。
- 退出码：`0` 为验证通过，`1` 为运行异常，`2` 为参数错误，`3` 为双路径不一致、存在低于阈值的结果或均值检查失败。
- `render` 使用原图作为底图，并把最终结果中的 ROI Mask 缩放到 bbox 后回贴到原图坐标；完整图 Mask 则从 `(0,0)` 绘制。
- Mask 合成用例和像素断言独立编入 `Test/qt_demo/dlcv_infer_c_qt_mask_test.vcxproj` 与 `Test/qt_demo/dlcv_infer_cpp_qt_mask_test.vcxproj`。两个工程通过构建配置引用各自 Demo 的真实控件源文件，不复制实现，不调用推理运行库；Demo 不包含测试代码或 `mask-visualization-selftest` 入口。独立测试命令为 `<测试EXE> --output <系统临时目录/mask.png>`，使用 Qt offscreen 平台，不显示窗口。

### 两个 Qt Demo 的自动回归

`Test/run_qt_demo_regression.py` 直接运行 C 和 C++ Qt Demo EXE，使用 `Test/qt_demo_regression_cases.json` 中的固定模型清单，不用控制台 API 工程替代 Demo。在 Visual Studio 中构建所需 Demo 或独立 Mask 测试工程（Release x64）。测试 EXE 输出到 `Test/qt_demo/Release/<工程名>/`，不加入 Demo 工程引用或发布流程。

```text
python Test/run_qt_demo_regression.py --c-exe <C_Demo.exe> --cpp-exe <CPP_Demo.exe> --c-mask-test-exe <C_Mask_Test.exe> --cpp-mask-test-exe <CPP_Mask_Test.exe> --dll <本次构建的dlcv_infer_cpp.dll> --model-root <测试模型目录> --core-dll-directory <推理DLL目录> --output <系统临时目录/report.json>
```

- 核对 EXE 所在目录的包装 DLL 与指定构建 DLL 的 SHA-256。
- 运行普通分类 DVT/DVO、分割 DVT 和流程 DVST，分别读取结构化和 JSON 结果，检查固定数量、类别、分数及两条路径一致性；固定分数容差为 `1e-6`。
- 检查帮助、缺少参数、阈值错误、缺少文件、拒绝 dvsp 和防止输出覆盖输入。
- `infer` 清理模型后连续释放仍须成功；结果中的 `release_check_passed` 记录该检查。C++ 检查本地编号清理，C 检查释放返回码及释放后信息接口拒绝旧编号。
- 两个独立 Mask 测试分别执行真实绘制控件的掩码像素断言，检查退出码和 500 × 400 PNG；报告记录测试 EXE 的 SHA-256。旧自测命令传给 Demo 时返回参数错误。PNG 只保存在系统临时目录。
- 超时、崩溃、非零退出码、缺少结果、无效 JSON、非有限数值或基准不符均不计为通过。此测试不覆盖主窗口文件对话框、交互操作或压力测试按钮。

### 3.3 C++ DLL 控制台示例

- 工程：`dlcv_infer_cpp_dll_demo`。
- 无参数时保留原有 `ModelRoot`、10 组 `DefaultCases` 和 `RunDefaultCases`，执行通用测试模型清单并输出汇总；缺失输入按原逻辑跳过。`-h`、`--help` 或 `help` 仅显示帮助并返回 0，不初始化推理运行库。
- 单次推理显式使用 `--case <model.dvst> <image.png>` 或 `--model <model.dvst> --image <image.png>`；多组输入重复添加 `--case`。
- 缺少成对的模型与图片参数返回 1；加载或推理异常返回 2。
- 图片按 BGR 转 RGB 后传入 `Model::InferBatch()`；阈值为 `0.05`，输出结构化结果摘要。原有通用测试清单保留；专用业务模型不加入默认清单，仅通过命令行显式传入，不内置业务名称或固定预期结果。
- `--pressure` 与组合命令入口保留；压力测试同样显式提供模型和图片。
- 推理输出可能包含输入路径、模型类别及运行库日志，仅保存在系统临时目录；PR 仅记录脱敏后的检查结果，不上传原始输出。
- 回归入口：`python -B -m unittest discover -s Test -p test_cpp_dll_demo_cli.py -v`；环境变量 `DLCV_CPP_DLL_DEMO_EXE` 指定本次构建的 EXE。未设置时，仅运行源码检查，实际 EXE 检查明确跳过。另设 `DLCV_CPP_DLL_DEMO_MODEL_ROOT` 后，验证原有无参数默认测试，并复用既有测试清单验证普通模型和流程模型的两种显式输入形式；未设置时推理检查明确跳过。

### 3.4 UI 布局

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
3. 若两者均为空，表示当前未检测到加密狗；检查动作只显示授权信息，不选择推理 DLL。

**代码路径**：`MainWindow::onCheckDog()`
- 调用 `dlcv_infer::GetAllDogInfo()`。
- 默认 DLL 在首次普通模型加载时根据模型头选择；后续普通模型继续使用该 DLL，并逐个检查授权。

## 5. C++ 控制台 Demo 组合命令

`dlcv_infer_cpp_dll_demo` 是与 C++ API 对应的控制台程序，默认模式、`--case`、`--model` 和 `--pressure` 入口保持可用。新增组合命令通过 `--then` 在同一进程内保留已加载模型：

```text
dlcv_infer_cpp_dll_demo.exe load-model <名称> <模型路径> [--device N] --then model-info <名称> --then infer <名称> <图片路径>
dlcv_infer_cpp_dll_demo.exe load-model <名称> <模型路径> --then benchmark <名称> <图片路径> [--threads N] [--runs N]
```

组合命令包括 `load-model`、`list-models`、`model-info`、`dvs-model-info`、`infer`、`benchmark`、`free-model` 和 `free-all-models`。`benchmark` 使用同一模型和图片建立基准结果，再由多个线程重复推理并比较批量数量、目标数量、类别、框、分数、角度、面积、mask 和均值；线程数范围为 1～32。模型编号由实际加载或注册接口返回，不按数值区段推断资源类型或 DLL。

程序退出时释放当前模型和全部模型。按 index 释放时，无效参数可返回参数错误；有效编号即使已不存在或底层报错也返回成功并完成本地清理，重复释放同样成功，错误详情最多写入日志或消息。运行目录需要 `dlcv_infer_cpp.dll`、OpenCV、Visual C++ 运行库及首次普通模型头对应的底层 DLL。

## 6. C API 动态导出检查

`dlcv_infer_c_qt_demo.exe --check-c-api-exports` 在 Qt 应用初始化前执行动态库检查，不启动窗口。程序通过 `LoadLibraryW` 加载 `dlcv_infer_cpp.dll`，通过 `GetProcAddress` 解析全部 34 个 C 导出函数，检查通过时退出码为 0。

---

## 7. 调试技巧

### 7.1 图像解码问题

- 若图像显示为"图像解码失败"，检查：
  - 文件路径是否包含非 ASCII 字符（Qt 使用 `toLocal8Bit` 传给 OpenCV）。
  - OpenCV 运行时 DLL 是否缺失。

### 7.2 模型加载失败

- 检查加密狗是否插入并匹配模型要求的 provider。
- 检查 `dlcv_infer.dll` / `dlcv_infer_v.dll` 是否在 PATH 中。
- 查看文本区的异常堆栈，通常包含底层 C API 返回的错误 JSON。

### 7.3 推理结果为空

- 调低 **threshold**（如 0.1）再试。
- 检查输入图像通道：必须是 RGB（8UC3），程序内部已通过 `prepareImageForInference` 转换。

### 7.4 压力测试崩溃

- 检查 GPU 显存是否足够（batch_size × 线程数 × 单图显存）。
- 若使用 Flow 模式，检查各节点模型是否支持并发。

### 7.5 窗口位置异常

- 程序启动时检测窗口是否在所有屏幕外，若是则自动居中。
- 窗口几何状态保存在 `QSettings`（注册表），更换显示器后可能需手动调整。

---

## 8. 关键代码片段

### 8.1 图像预处理

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

### 8.2 推理调用

```cpp
json params;
params["threshold"] = spinThreshold_->value();
params["with_mask"] = true;
params["calc_mean"] = checkCalcMean_->isChecked();
params["batch_size"] = batchSize;

dlcv_infer::Result output = model_->InferBatch(imageList, params);
```

### 8.3 GPU 设备初始化

```cpp
// 在后台线程中调用，通过 QMetaObject::invokeMethod 回传结果到 UI 线程
dlcv_infer::Utils::KeepMaxClock();
json gpuInfo = dlcv_infer::Utils::GetGpuInfo();
```

---

*本文档只记录当前源码实现。如需了解 API 详细定义，参见 `C++ API文档.md`。*

## 控制台归档与模型池检查

以下命令由 `Test/dlcv_infer_cpp_test` 提供，不启动 Qt 窗口。通过项目构建脚本生成 Debug/x64 测试程序后执行：

```text
Debug\dlcv_infer_cpp_test.exe dvs-archive-duplicate-selftest
Debug\dlcv_infer_cpp_test.exe dvs-model-pool-selftest <普通模型路径> [设备编号]
Debug\dlcv_infer_cpp_test.exe dvs-memory-loading-selftest <流程模型路径> <图片路径> [设备编号]
Debug\dlcv_infer_cpp_test.exe dvsp-reject-selftest <dvsp路径> [设备编号]
```

| 命令 | 输入与检查范围 |
|---|---|
| `dvs-archive-duplicate-selftest` | 在系统临时目录生成测试归档；检查模型成员和 `pipeline.json` 的规范化同名处理，相同字节允许读取，不同字节明确报错。模型差异用例使用等长数据且仅末字节不同；不执行模型推理 |
| `provider-loader-selftest <Virbox模型路径> <另一模型路径> [轮数]` | 首个模型必须是 Virbox 格式；检查按模型头选择默认 DLL，后续文件及内存加载保持该 DLL |
| `dvs-model-pool-selftest` | 读取现有普通 `.dvt`，生成包含两个相同模型节点的测试归档；只通过现有产品接口检查实例内复用和释放行为，不依赖生产 DLL 的测试导出。测试代码及检查状态全部保留在测试工程内 |
| `dvs-memory-loading-selftest` | 使用 `.dvst/.dvso` 及对应图片执行加载、推理、释放，并监测解包临时文件；参数为 `threshold=0.5`、`with_mask=true`、`batch_size=1`，不进行 mask 数值比较 |
| `dvsp-reject-selftest` | 检查 `.dvsp` 返回明确的不支持错误，且不生成归档临时文件；不执行推理 |

设备编号默认 `0`。模型池检查中的两个归档内容相同，但每次读取使用独立的 `StoreId`；包装层不额外进行整包内容缓存。临时测试归档由测试程序创建并在结束时删除。实际产品输入由模型加速器一次生成，每次只选择一种加密狗格式，同一产物及流程内子模型只包含该格式。非该生成流程得到的混合格式文件不属于产品输入，不用于扩展接口能力。

命令退出码 `0` 表示检查通过，非零表示失败或参数无效。这些命令不验证源模型转换、多设备运行、完整路径优先或短文件名多候选处理，也不代表 C++ 与 C# 的全部功能已经一致。

## 流程层共享

SharedFlowRegistry 位于既有 dlcv_infer_cpp 工程。C# 与 C++ 使用相同流程 index 读取已登记的 pipeline 和子模型编号，各自在流程层恢复执行对象，不重新读取归档。底层 dlcv_infer 仅管理子模型；流程登记、信息、持有、释放与全量清理由 OpenIVS 完成。流程内部字符串使用 openivs_flow_free_result 释放，不交给底层 dlcv_free_result。
