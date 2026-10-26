# C# 测试程序自动化入口

`DlcvDemo` 无参数时启动 WinForms 主窗口；`infer` 执行无界面推理；`ui-test` 运行同一个主窗口并复用模型加载、推理、可视化与结果文本逻辑。

## 参数

```text
"C# 测试程序.exe" ui-test
  --model <模型路径>
  --image <图片路径>
  --output <状态 JSON 路径>
  [--threshold <0..1>]
  [--device <int>]
  [--interactive-dialogs <true|false>]
  [--screenshot <PNG 路径>]
  [--save-json <完整结果 JSON 路径>]
  [--language <zh-CN|en-US>]
  [--result-view <summary|json>]
  [--test-mode <infer|infer-json|pressure>]
  [--batch-size <1..1024>]
  [--thread-count <1..32>]
  [--pressure-duration-ms <500..60000>]
  [--capture-delay-ms <0..60000>]
  [--label-font-scale <0.3..5.0>]
```

- 自动验证使用 `--interactive-dialogs false`：不弹文件选择框、不激活窗口，按参数加载模型和图片，成功或失败后关闭窗口；`true` 为人工交互模式。
- `--test-mode` 默认 `infer`；`infer-json` 在单次推理后执行正式“推理 JSON”按钮；`pressure` 使用界面的多线程测试流程，时长默认 2000ms，结束时保存统计结果。批量大小和线程数默认均为 1。
- `--save-json` 用于 `infer` 和 `infer-json`，使用正式保存方法导出完整缓存，父目录须存在，路径不得与模型、图片、状态 JSON、状态临时文件或截图相同。结果中的 `json_export_check` 记录 UTF-8 与 JSON 内容核验、预览和全选耗时、缓存切换、2 MB 文本、空结果、错误路径、输入文件保护及保存快照检查。测试实现位于 `Test/desktop_projects/DemoJsonResultTests.cs`，通过项目引用编入现有 UI 测试入口。
- 启动测试时检查顶部实际控件几何：在中英文模式下检查默认、最小、中间宽度及原尺寸切换点相邻像素，分别缩小和放大；按钮尺寸保持不变，跨列按钮与下方两列具有相同左右端点，控件不得超出父容器，参数标签与输入框垂直居中。结果写入 `button_layout_check`，测试实现位于 `Test/desktop_projects/DemoButtonLayoutTests.cs`；结束后恢复原窗口尺寸及语言，不模拟鼠标键盘。
- `--language` 只影响本次运行，不持久化；`--result-view` 默认 `summary`，切换至 `json` 不重新推理。
- `--screenshot` 可选，必须使用 `.png`；通过 `Form.DrawToBitmap` 保存目标窗口，不操作鼠标键盘、不采集桌面。
- `--label-font-scale` 默认 1.0，设置正式图像控件已有的标签字号倍率，只影响本次验证。
- `--capture-delay-ms` 默认 0；成功结果生成后保持正式窗口消息循环指定时长，供隔离桌面采集完整标题栏、边框和客户区。采集时不传 `--screenshot`，以完整窗口 PNG 与 `--output` 的结果共同检查。完整窗口图可用 `python Test/verify_csharp_demo_button_center.py <PNG> --output <JSON>` 检查首行两个蓝色按钮的实际文字中心，水平、垂直偏差均不得超过 2 像素。
- 输出和截图写入系统临时目录，不得覆盖模型、图片或彼此。

## 输出与退出码

状态 JSON 依次写入 `started`、`model_loaded`、`passed` 或 `failed`，包含模型、图片、阈值、设备、测试模式、批量大小、线程数、窗口标题、结果文本、界面类型（`ui_framework`）、截图路径（`screenshot`）和错误信息。自动测试中的产品错误写入界面和 JSON，不弹错误 MessageBox。

进程退出码：`0` 通过、`1` 运行或验证失败、`2` 参数错误。判断时同时读取退出码和无 BOM UTF-8 JSON，不能用窗口出现或截图文件存在代替通过。

界面和持久化说明见 [C# 测试程序开发文档](C%23测试程序开发文档.md)，编译入口见 [开发文档](开发文档.md)。源码参数入口为 `DlcvDemo/UiTestOptions.cs`。
