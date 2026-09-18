# C# 测试程序自动化入口

当前正式界面是 WPF `MainWindow`。自动测试入口在保持原有无参数启动和 `infer` CLI 行为不变的前提下，增加：

```text
C# 测试程序.exe ui-test
  --model <模型路径>
  --image <图片路径>
  --output <状态 JSON 路径>
  [--threshold <0..1>]
  [--device <int>]
  [--interactive-dialogs <true|false>]
```

`interactive-dialogs=false` 是默认自动化主路径：启动真实 WPF 窗口但不激活，直接复用产品内部的模型加载、单图推理、可视化和结果文本逻辑，不打开文件选择框。

`interactive-dialogs=true` 只用于空闲时的真实文件框专项。外部探针只操作本次进程拥有的对话框，并只清理本次启动的进程。

状态 JSON 依次写入 `started`、`model_loaded`、`passed` 或 `failed`，包含模型、图片、阈值、设备、窗口标题、结果文本和错误信息。自动测试模式下产品错误写入界面和 JSON，不弹错误 MessageBox。

构建必须使用仓库的 VS Build skill：

```powershell
python ".cursor\skills\vs-build\scripts\build.py" "DlcvDemo\DlcvDemo.csproj" --configuration Debug --platform x64 --target Build --verbosity minimal
```

自动测试系统入口位于：

`C:\Users\Administrator\Desktop\全平台自动化测试设计\scripts\probe_winforms_demo.py`

该探针只截取目标窗口，生成三张关键 PNG 和目标窗口 MP4，并将证据写入当前 `platform_*\csharp_ui\<case_id>\`。

## 界面语言

- 参数区右侧提供语言组合框（中文 / English），切换后界面即时刷新，选择持久化到用户设置 `Properties.Settings` 的 `UiLanguage`；默认中文，空值回退中文。
- 实现位于 `DlcvDemo/I18n.cs`：以中文原文为键查英文字典，首次应用时用 `ConditionalWeakTable` 记录按钮/标签/菜单项的原始中文文本，保证中英往返切换正确；窗体标题由 `MainWindow.UpdateWindowTitle` 单独维护。下拉框、结果文本框和数值框的运行期内容不按设计器原文覆盖。
- 设计器文件中的控件文本保持中文常量不动（中文即中性文本），运行时统一经 `I18n.ApplyTo` 翻译；控制台输出、日志与 JSON 字段保持中文不改。
- `PressureTestRunner` 的统计文本经 `PressureTestRunner.LocalizeText` 委托（默认原样返回）查同一字典，`DlcvDemo` 构造函数注入 `I18n.T`，不影响 DlcvDemo3 等其他调用方。
- `ui-test --interactive-dialogs false` 支持可选 `--language zh-CN|en-US` 指定本次运行界面语言（不持久化），非法值退出码 2。
