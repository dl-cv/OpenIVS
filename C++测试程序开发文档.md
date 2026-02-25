# `dlcv_infer_cpp_qt_demo`（C++测试程序）开发文档

目标：按本文步骤构建并运行出与当前源码 **完全一致** 的 Qt Widgets 测试程序（UI 文案/默认值/行为对齐 `dlcv_infer_cpp_qt_demo/MainWindow.cpp`、`main.cpp`、`ImageViewerWidget.cpp`）。

## 源码与产物

- **源码目录**：`dlcv_infer_cpp_qt_demo/`
- **核心文件**：`main.cpp`、`MainWindow.{h,cpp}`、`ImageViewerWidget.{h,cpp}`、`resources.qrc`、`dlcv_demo_icon.svg`
- **可执行文件名**：`dlcv_infer_cpp_qt_demo.exe`
- **默认输出目录**：`{SolutionDir}\{Configuration}\`（例如 `Release\dlcv_infer_cpp_qt_demo.exe`）

## 依赖（构建期 + 运行期）

- **Visual Studio**：VS2022（v143），建议 **x64**
- **C++ 标准**：C++17
- **Qt**：Qt6（用到 `Core/Gui/Widgets`）
- **OpenCV**：4.10（工程默认链接 `opencv_world4100(.lib)`）
- **DLCV SDK 头文件**：工程默认包含 `C:\dlcv\Lib\site-packages\dlcvpro_infer\include`

说明：以上路径来自 `dlcv_infer_cpp_qt_demo/dlcv_infer_cpp_qt_demo.vcxproj` 的默认配置；如果你的 Qt/OpenCV/SDK 安装路径不同，需要在工程属性里同步修改。

## 构建（Visual Studio 解决方案）

- 打开解决方案：`OpenIVS.sln`
- 选择配置：`Release|x64`（或 `Debug|x64`）
- 生成项目：`dlcv_infer_cpp_qt_demo`

### Qt 路径约定（非常关键）

该工程通过环境变量/宏 `Qt6_DIR` 推导 Qt 安装目录，并假设其值指向 **`...\lib\cmake`**（而不是 `...\lib\cmake\Qt6`）。示例：

- `Qt6_DIR=C:\Qt\6.10.2\msvc2022_64\lib\cmake`

构建前会调用 `rcc.exe` 生成 `qrc_resources.cpp`；构建后会尝试执行 `windeployqt.exe` 将 Qt 运行时部署到 exe 同目录（若提示找不到 `windeployqt.exe`，请先修正 `Qt6_DIR` 或手动对 `dlcv_infer_cpp_qt_demo.exe` 执行一次 `windeployqt`）。

### 工程默认路径（来自 `*.vcxproj`，便于排错）

- **OpenCV Include**：`C:\OpenCV\build\include`
- **OpenCV Lib（x64, vc16）**：`C:\OpenCV\build\x64\vc16\lib`
- **DLCV SDK Include**：`C:\dlcv\Lib\site-packages\dlcvpro_infer\include`
- **链接库**：
  - Debug：`dlcv_infer_cpp_dll.lib;opencv_world4100d.lib;Qt6Widgetsd.lib;Qt6Guid.lib;Qt6Cored.lib`
  - Release：`dlcv_infer_cpp_dll.lib;opencv_world4100.lib;Qt6Widgets.lib;Qt6Gui.lib;Qt6Core.lib`

> 注：工程文件同时包含 `Win32` 配置，但本文档按实际使用建议以 `x64` 为准。

## 运行

- 直接运行 `Release\dlcv_infer_cpp_qt_demo.exe`（或对应配置目录下的 exe）。

## UI/文案/行为（必须一致）

### 窗口与持久化

- **标题**：`C++测试程序`
- **图标**：资源 `:/dlcv_demo_icon.svg`（来自 `resources.qrc`）
- **最小尺寸**：`860 x 500`
- **字体**：`Microsoft YaHei`，9pt（`main.cpp`）
- **窗口位置/状态**：
  - 若存在 `Geometry/WindowState`：恢复；若恢复后窗口不与任意屏幕相交则重新居中
  - 否则：主屏幕居中
- **设置存储**：`QSettings("dlcv","DlcvDemoQt")`
  - `Geometry`、`WindowState`、`LastModelPath`、`LastImagePath`

### 顶部控制区（3 行，从左到右，间距统一）

控件统一规格：按钮 **最小宽** 120、**固定高** 36；`comboDevice` 横向可伸缩；根布局 `margins=12`、`spacing=8`。

- **第 1 行**：`加载模型` ｜ `选择显卡` + 设备下拉框（包含 `CPU` 与 GPU 列表）｜ `打开图片推理`
- **第 2 行**：`单次推理` ｜ `推理JSON` ｜ `batch_size`(1-1024, 默认 1) ｜ `threshold`(0.05-1.00, step 0.05, 默认 0.05) ｜ `释放模型` ｜ `释放所有模型`
- **第 3 行**：`多线程测试`（运行中按钮文案为 `停止`）｜ `线程数`(1-32, 默认 1) ｜ `文档` ｜ `获取模型信息`

### 内容区（`QSplitter`）

- 左：只读文本输出 `QPlainTextEdit`
- 右：`ImageViewerWidget`（默认 `showStatusText=false`、`showVisualization=true`）
- Splitter 初始尺寸：`{360, 740}`

### 设备列表初始化（启动即异步执行）

- 下拉框填充顺序：`CPU`（id=-1）→ `dlcv_infer::Utils::GetGpuInfo()` 返回的每个设备（使用 `device_name/device_id`）
- 默认选中：有 GPU 则选第 1 个 GPU；否则选 `CPU`
- 若获取 GPU 信息返回 warning：左侧文本输出以 `GPU信息获取失败：\n` 开头的内容

### 模型/图片/推理

- **加载模型**：
  - 文件对话框标题：`选择模型`
  - 过滤器：`AI模型 (*.dvt *.dvo *.dvr *.dvst);;所有文件 (*.*)`
  - 成功后自动执行一次 `获取模型信息` 并输出 JSON
- **打开图片推理**：选图后立即触发一次 `单次推理`
  - 对话框标题：`选择图片文件`
  - 过滤器：`图片文件 (*.jpg *.jpeg *.png *.bmp *.gif *.tiff *.tif);;所有文件 (*.*)`
- **单次推理**：
  - `batch_size` 通过重复同一张图组成 batch
  - **输入像素格式**：按 **RGB** 传入
  - 成功时左侧输出（格式固定）：

```text
推理时间: {elapsedMs:F2}ms

输入: RGB

推理结果:
{formatResultText}
```

- **推理JSON**：正常情况下输出推理结果 JSON（缩进 4）；若结果为空数组，则输出调试对象 `{"input":"RGB","one_out":[]}`（缩进 2）；若图像解码失败则静默返回（无弹窗/无输出刷新）
- **通用前置条件提示**（弹窗标题均为 `提示`）：
  - 未加载模型：`请先加载模型文件！`
  - 未选择图片：`请先选择图片文件！`
- **其他按钮**：
  - `获取模型信息`：输出 JSON；若包含 `model_info` 字段则输出该字段内容
  - `释放模型`：左侧输出 `模型已释放`
  - `释放所有模型`：左侧输出 `所有模型已释放`
  - `文档`：打开 `https://docs.dlcv.com.cn/deploy/sdk/csharp_sdk`

### 多线程测试（按钮：`多线程测试`/`停止`）

- **前置条件**：同上（未满足直接弹窗提示）
- **启动行为**：
  - 读取当前图像，保存 `batch_size/threshold/threadCount`
  - **输入像素格式**：按 **RGB**
  - 禁用：模型加载/模型信息/打开图/单次推理/推理JSON/设备下拉框/三个数值输入
  - 创建 `线程数` 个 worker：循环调用 `Model::InferBatch()`（`with_mask=false`），并累计：
    - `completedRequests`（每次 infer +1）
    - `totalLatencyUs`（每次 infer 的耗时微秒）
  - UI 每 500ms 刷新一次左侧统计（模板固定）：

```text
压力测试统计:
线程数: {threadCount}
批量大小: {batchSize}
运行时间: {elapsedSeconds:F2} 秒
完成请求: {completedRequests * batchSize}
平均延迟: {averageLatencyMs:F2}ms
实时速率: {recentRate:F2} 请求/秒
```

- **停止行为**：点击 `停止` / 窗口关闭 / 加载模型 / 打开图片推理 / 释放模型 / 释放所有模型 时必须停止并 join 所有 worker，恢复按钮文案为 `多线程测试`，恢复 UI 可用。
- **异常处理**：worker 遇到任意异常立即停止测试，并弹 `QMessageBox::critical`（标题 `错误`），同时左侧输出 `title + "\n" + detail`。

### 图像交互（右侧）

- 滚轮缩放；左键拖拽平移；右键单击：适配窗口；`V`：显示/隐藏可视化叠加
