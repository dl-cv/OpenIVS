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
  [--calc-mean <true|false>]
  [--interactive-dialogs <true|false>]
  [--screenshot <PNG 路径>]
  [--language <zh-CN|en-US>]
  [--result-view <summary|json>]
  [--test-mode <infer|pressure>]
  [--batch-size <1..1024>]
  [--thread-count <1..32>]
  [--pressure-duration-ms <500..60000>]
```

- 自动验证使用 `--interactive-dialogs false`：不弹文件选择框、不激活窗口，按参数加载模型和图片，成功或失败后关闭窗口；`true` 为人工交互模式。
- `--test-mode` 默认 `infer`；`pressure` 使用界面的多线程测试流程，时长默认 2000ms，结束时保存统计结果。批量大小和线程数默认均为 1。
- `--language` 只影响本次运行，不持久化；`--result-view` 默认 `summary`，切换至 `json` 不重新推理。
- `--screenshot` 可选，必须使用 `.png`；通过 `Form.DrawToBitmap` 保存目标窗口，不操作鼠标键盘、不采集桌面。
- 输出和截图写入系统临时目录，不得覆盖模型、图片或彼此。

## 输出与退出码

状态 JSON 依次写入 `started`、`model_loaded`、`passed` 或 `failed`，包含模型、图片、阈值、设备、测试模式、批量大小、线程数、窗口标题、结果文本、界面类型（`ui_framework`）、截图路径（`screenshot`）和错误信息。自动测试中的产品错误写入界面和 JSON，不弹错误 MessageBox。

进程退出码：`0` 通过、`1` 运行或验证失败、`2` 参数错误。判断时同时读取退出码和无 BOM UTF-8 JSON，不能用窗口出现或截图文件存在代替通过。

界面和持久化说明见 [C# 测试程序开发文档](C%23测试程序开发文档.md)，编译入口见 [开发文档](开发文档.md)。源码参数入口为 `DlcvDemo/UiTestOptions.cs`。
