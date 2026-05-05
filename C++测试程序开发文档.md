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
| `dlcv_infer.h` + `dlcv_infer.lib` | C++ API 头文件与导入库 |
| `dlcv_infer.dll`（运行时） | 底层推理 DLL |

### 2.2 Visual Studio 编译

**前提条件**：
1. 安装 Qt 并配置 VS Qt Tools（或手动配置 `QTDIR` 环境变量）。
2. 确保 OpenCV 路径已添加到项目属性（包含目录 + 库目录 + 链接器输入）。
3. 确保 `dlcv_infer_cpp_dll` 已编译，输出 `dlcv_infer.lib` 和 `dlcv_infer.h` 可用。

**编译流程**：
1. 打开 `OpenIVS.sln`，选择 `dlcv_infer_cpp_qt_demo` 项目。
2. 平台配置：**x64 Release**（Qt 和 OpenCV 均需匹配 x64）。
3. 链接器输入确认包含：
   - `dlcv_infer.lib`
   - OpenCV 核心库（`opencv_core`、`opencv_imgproc`、`opencv_imgcodecs` 等）
   - Qt 核心库（`Qt5Core.lib`、`Qt5Gui.lib`、`Qt5Widgets.lib`）
4. 生成解决方案（Ctrl+Shift+B）。

### 2.3 输出与部署

- 编译输出：`x64\Release\dlcv_infer_cpp_qt_demo.exe`
- 运行时需要将以下文件放在 exe 同级目录或系统 PATH 中：
  - `dlcv_infer.dll`（或 `dlcv_infer_v.dll`，取决于加密狗类型）
  - Qt 运行时 DLL（`Qt5Core.dll`、`Qt5Gui.dll`、`Qt5Widgets.dll` 等）
  - OpenCV 运行时 DLL（`opencv_core4x.dll`、`opencv_imgproc4x.dll`、`opencv_imgcodecs4x.dll`）
  - `nvml.dll`（可选，GPU 信息获取需要）

---

## 3. 测试入口

### 3.1 程序入口（main.cpp）

```cpp
int main(int argc, char* argv[]) {
    QApplication app(argc, argv);
    app.setApplicationName("C++测试程序");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));
    
    // 退出时释放所有模型
    QObject::connect(&app, &QCoreApplication::aboutToQuit, []() {
        dlcv_infer::Utils::FreeAllModels();
    });

    MainWindow w;
    w.show();
    return app.exec();
}
```

### 3.2 UI 布局

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

参数控件：
| 控件 | 范围 | 默认值 | 说明 |
|------|------|--------|------|
| 选择显卡（下拉框） | CPU + 检测到的 GPU | GPU 0 | 设备选择 |
| batch_size（整数框） | 1~1024 | 1 | 批量推理大小 |
| threshold（浮点框） | 0.0~1.0 | 0.5 | 置信度阈值 |
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
4. 结果在文本区显示（数量、每个目标的类别、score、bbox、area、angle）。
5. 图像区显示可视化结果（bbox 框 + mask 叠加）。

**代码路径**：`MainWindow::onInfer()`
- `prepareImageForInference`：将 OpenCV 读到的 BGR/BGRA 转换为 RGB。
- 若 `batchSize > 1`，将同一张图片复制为 batch。
- 参数 JSON：`{"threshold": ..., "with_mask": true, "batch_size": ...}`。

### 4.3 JSON 输出测试

1. 点击 **推理JSON**。
2. 调用 `model->InferOneOutJson()` 获取 JSON 数组结果。
3. 文本区显示格式化的 JSON（缩进 4）。

**代码路径**：`MainWindow::onInferJson()`
- 返回字段：`category_id`、`category_name`、`score`、`bbox`、`with_bbox`、`with_angle`、`angle`、`mask`（点数组）、`with_mask`、`area`。

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

**代码路径**：`MainWindow::onCheckDog()`
- 调用 `dlcv_infer::GetAllDogInfo()`。

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

*本文档只记录当前源码实现。如需了解 API 详细定义，参见 `C++API接口文档.md`。*
