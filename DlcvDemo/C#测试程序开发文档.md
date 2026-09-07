# C# 测试程序自动化入口

当前正式界面是 WinForms `MainWindow`。自动测试入口在保持原有无参数启动和 `infer` CLI 行为不变的前提下，增加：

```text
C# 测试程序.exe ui-test
  --model <模型路径>
  --image <图片路径>
  --output <状态 JSON 路径>
  [--inference-count <正整数>]
  [--threshold <0..1>]
  [--device <int>]
  [--interactive-dialogs <true|false>]
```

`interactive-dialogs=false` 是默认自动化主路径：启动真实 WinForms 窗口但不激活，直接复用产品内部的模型加载、单图推理、可视化和结果文本逻辑，不打开文件选择框。

`--inference-count` 默认为 `1`，必须是大于等于 `1` 的整数；设置为 `2` 时，同一窗口连续执行两次真实模型推理，截图和最终结果来自第二次推理。`interactive-dialogs=true` 只用于空闲时的真实文件框专项。外部探针只操作本次进程拥有的对话框，并只清理本次启动的进程。
`--screenshot` 为可选参数，使用 Win32 `PrintWindow` 截取包含标题栏的整个 WinForms 窗口，截图写入指定 PNG 路径。

状态 JSON 依次写入 `started`、`model_loaded`、`passed` 或 `failed`，包含模型、图片、阈值、设备、窗口标题、结果文本、请求次数、已完成真实推理次数及每次耗时。自动测试模式下产品错误写入界面和 JSON，不弹错误 MessageBox。

构建必须使用仓库的 VS Build skill：

```powershell
python ".cursor\skills\vs-build\scripts\build.py" "DlcvDemo\DlcvDemo.csproj" --configuration Debug --platform x64 --target Build --verbosity minimal
```

自动测试系统入口位于：

`C:\Users\Administrator\Desktop\全平台自动化测试设计\scripts\probe_winforms_demo.py`

该探针只截取目标窗口，生成三张关键 PNG 和目标窗口 MP4，并将证据写入当前 `platform_*\csharp_ui\<case_id>\`。
