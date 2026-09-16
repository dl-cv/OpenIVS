# C# / C++ 混编模型测试

## 工程

- `DlcvCSharpCppTest.csproj`：C# 7.3、.NET Framework 4.7.2、WinForms、x64。
- `Bridge/DlcvCSharpCppBridge.vcxproj`：VS 2022 v143 C++/CLI 动态库，原生模型实现单独以 C++17 编译。
- 两个项目位于 `OpenIVS.sln` 的 Test 分组。依赖通过工程引用构建，不复制 API 源码，不加入发布打包流程。
- `DlcvNativeRuntime.targets` 从引用工程取得本次 `dlcv_infer_cpp.dll`；桥接工程提供 OpenCV 运行库路径，由 C# 工程复制到 EXE 目录。

## Visual Studio 界面编辑

打开仓库根目录现有的 `OpenIVS.sln`，在 Test 分组的 `DlcvCSharpCppTest` 项目中右键 `MainForm.cs` → 查看设计器，或选中文件按 Shift+F7。控件位置、大小、文本和事件可通过 .NET Framework Windows Forms 设计器编辑。

- `MainForm.Designer.cs`：标准 `InitializeComponent()`、控件字段、布局和事件连接。
- `MainForm.resx`：窗体资源，由设计器维护。
- `MainForm.cs`：按钮处理、状态更新与模型释放。
- 窗体构造仅初始化控件，模型会话在运行操作时创建；设计器不加载模型，不调用原生推理 DLL。

## 模型选择与分组操作

按钮不使用自动换行，左右模型区域各自保持一排：C# 为“加载模型、从C++共享、获取信息、释放模型”，C++ 为“加载模型、从C#共享、获取信息、释放模型”。分组内等高、等宽，加载为主操作色，其余为浅色按钮；最小窗口为设计尺寸 960×540，实际尺寸随 DPI 缩放。

| 区域 / 按钮 | 行为 |
|---|---|
| C# / 加载模型 | 从已选文件建立 C# `Model`；没有选择文件时打开浏览窗口 |
| C++ / 加载模型 | 经 C++/CLI 调用原生 `dlcv_infer::Model(std::wstring, device)`，不依赖 C# 模型；没有选择文件时打开浏览窗口 |
| C# / 从C++共享 | 将已有 C++ `ModelIndex` 交给 C# `ModelFactory.CreateFromIndex`，不再次加载文件 |
| C++ / 从C#共享 | 将已有 C# `modelIndex` 交给 `CreateModelFromIndex`，不再次加载文件 |
| 两侧 / 获取信息 | 分别读取 C# 与 C++ 模型信息，保留原始 JSON，在各自区域显示 |
| 两侧 / 释放模型 | 只释放该侧持有关系并清空该侧信息；另一侧仍可使用 |

C# 和 C++ 均可先加载，也可分别从同一文件加载；已有该侧模型时禁止重复加载。两侧均只在目标侧为空、来源侧存在时允许共享，禁止覆盖已有模型。两侧均释放后才允许改变路径和设备。两侧状态均明确区分“文件加载”和“共享模型”。SDK 对相同内容和设备可能返回相同编号并分别增加持有计数，不能用编号相同判断是否通过共享按钮创建。

路径框旁提供独立的“浏览…”按钮，路径框只读，无需手填。浏览只选择文件，不加载模型。与 DlcvDemo 相同，使用 .NET 用户级 `LastModelPath` 设置，选择成功后立即 `Save()`；取消选择不改变原路径或记录。程序再次打开时恢复路径，文件窗口通过 `InitialDirectory` 和 `FileName` 定位上次目录与文件。记录属于本测试程序，不修改 DlcvDemo 的用户设置；设计器构造不读取设置。两侧模型全部释放后可以重新浏览选择。

仅接受本地 `.dvt`、`.dvo`、`.dvst`、`.dvso`；设备默认 0，-1 为 CPU。两侧尚未全部释放时不能替换已选文件。C++ 对象已存在时不能重复转换。

转换是同进程模型索引共享，不改变模型文件格式，不重复加载文件，也不保存新的模型文件。共享绑定成功后，可以先释放任一语言对象，再从另一语言获取信息；关闭窗口按 C++、C# 顺序释放。操作异常显示在底部状态区，不弹出错误对话框。程序不接收图片、不调用推理接口。

运行环境须安装深度视觉 SDK，且实际加载的推理 DLL 提供共享索引查询、绑定、解绑和信息接口。旧 SDK 缺少这些接口时，加载与 C# 信息查询可以成功，但转换会明确报错，不改变已有 C# 模型。编译成功不表示已安装 SDK 具有共享能力。

## HiDPI 与应用图标

- .NET Framework 4.7.2 的 App.config 启用 `DpiAwareness=PerMonitorV2`，应用清单声明 Windows 10 兼容性；不调用仅适用于现代 .NET 的 `Application.SetHighDpiMode`。
- 窗体使用 `AutoScaleMode.Dpi` 和 96 DPI 设计基准；保留标准 Designer/resx，按钮使用两组 TableLayoutPanel 固定单行。
- EXE 的 ApplicationIcon 与窗体 `$this.Icon` 资源共同引用仓库现有 `logo.ico`，不复制二进制图标，不依赖运行时文件路径。
- `appearance-test` 查询实际线程和窗体的 DPI awareness context，检查 PerMonitorV2，并逐像素比较 EXE 与窗体的 32×32 图标；按 100%、125%、150%、200% 相对 96 DPI 的比例做布局测试，覆盖默认与最小尺寸、文字区域和按钮不换行。
- 比例布局为进程内模拟，不修改系统显示设置、不发送 DPI 窗口消息；实际跨显示器拖动仍未验证。测试图片仅为未显示控件的内存绘制。

## Visual Studio 启动

- 统一入口为仓库根目录现有的 `OpenIVS.sln`；Test 分组包含 `DlcvCSharpCppTest` 和 `DlcvCSharpCppBridge`，Debug/Release x64 配置均已登记，不另建解决方案。
- C# 工程分别声明 `Debug|x64` 和 `Release|x64` 条件属性组，并分别设置平台与输出目录；只有默认 `Platform=x64` 而未声明配置组合时，MSBuild 可枚举平台列表为空，命令行指定平台的构建仍可能成功。
- C# 工程明确设置 `OutputType=WinExe`、`StartupObject=DlcvCSharpCppTest.Program`、`StartAction=Project`，启用混合调试；启动配置为 Debug x64。
- 根目录 `OpenIVS.slnLaunch` 提供“C# 与 C++ 混编模型测试”配置，只启动 C# 应用，不启动 C++/CLI 或原生 DLL 工程。`python Test/DlcvCSharpCppTest/update_launch_profile.py` 先确认应用已属于现有解决方案，再更新该启动配置；脚本不创建或改写 `.sln`。
- 已有 Visual Studio 会话仍使用此前启动选择时，右键 **DlcvCSharpCppTest** → **设为启动项目**，或选择上述共享启动配置。`DlcvCSharpCppBridge` 和 `dlcv_infer_cpp` 是 DLL，不是独立程序。
- 项目文件更新后，接受 Visual Studio 的重新加载提示；若当前会话仍保留旧配置，关闭并重新打开原 `OpenIVS.sln`。本工程不修改个人 `.suo` 或其他工程的调试设置。
- F5 / Ctrl+F5 的实际 IDE 交互仍需人工验证；命令行、配置检查与非交互 UI 结果不代表调试器启动已验收。

## 项目启动配置检查

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Test/DlcvCSharpCppTest/check_startup_configuration.ps1
```

通过本机 Visual Studio 的 MSBuild API 只读求值，验证可枚举平台、Debug/Release 配置、原解决方案映射、共享启动配置、应用入口及各配置输出路径。不调用构建目标，不启动或控制 Visual Studio，不改安装文件或个人设置；JSON 明确标记 `ide_debugger_tested=false`。该检查在修正前会因缺少可枚举 x64 平台失败，并由串行回归入口在实际 EXE 测试之前执行。

## 构建

在 Visual Studio 的 `OpenIVS.sln` 中选择 Debug 或 Release、x64，右键 `DlcvCSharpCppTest` → **生成**；工程引用自动构建 C++/CLI 桥接和原生依赖。设为启动项目后可用 F5 调试或 Ctrl+F5 运行。

自动化单项目编译使用仓库既有入口：

```powershell
python .cursor/skills/vs-build/scripts/build.py Test/DlcvCSharpCppTest/DlcvCSharpCppTest.csproj --configuration Debug --platform x64 --target Build --verbosity minimal
```

输出：`Test/DlcvCSharpCppTest/bin/x64/Debug/DlcvCSharpCppTest.exe`。双击或不带参数运行进入界面。

## 命令行模型验证

`model-test` 不创建 WinForms 对象，执行 C# 加载、C++ 共享、两侧信息读取、指定释放顺序和最终索引失效检查。`--release-order` 接受 `csharp-first`（默认）或 `cpp-first`；`--device` 默认为 0。`--load-mode shared` 为默认共享流程，`cpp-shared` 从 C++ 加载并共享到 C#，验证两种释放顺序、再次共享及关闭释放；`cpp` 只从 C++ 加载并验证释放、重载和最终编号失效，`independent` 分别从 C++、C# 文件构造并验证两种释放顺序。

```powershell
& .\Test\DlcvCSharpCppTest\bin\x64\Debug\DlcvCSharpCppTest.exe model-test --model "<模型文件>" --release-order csharp-first --output "$env:TEMP\mixed-cli.json"
```

## 非交互 UI 验证

```powershell
& .\Test\DlcvCSharpCppTest\bin\x64\Debug\DlcvCSharpCppTest.exe designer-test --output "$env:TEMP\mixed-designer.json"
& .\Test\DlcvCSharpCppTest\bin\x64\Debug\DlcvCSharpCppTest.exe ui-test --output "$env:TEMP\mixed-ui.json"
& .\Test\DlcvCSharpCppTest\bin\x64\Debug\DlcvCSharpCppTest.exe ui-test --model "<模型文件>" --device 0 --output "$env:TEMP\mixed-model.json" --screenshot "$env:TEMP\mixed-model.png"
```

- `designer-test` 仅构造和释放未显示的窗体，检查八个模型操作按钮和浏览按钮，并检查进程未加载 C++/CLI 桥接或原生推理模块。
- `ui-test` 无 `--model` 时检查浏览按钮和六个操作按钮的可用状态、选择即保存、取消不改变、重建窗体与设置对象恢复记录、上次目录与文件名、空模型操作、重复释放、无效模型、禁止的模型格式和负索引。
- 指定模型时额外验证两侧索引一致、两侧信息、两种释放顺序、释放后再次转换、最终索引失效及窗口对象释放。
- UI 测试创建未显示的真实 WinForms 对象，调用实际模型按钮处理方法，检查两侧 JSON 文本、状态编号、按钮可用状态、释放后清空和关闭清理；检查默认与最小窗口布局。可选 `--screenshot` 通过控件自身绘制生成 PNG，并检查文字像素。不执行桌面自动化、键鼠模拟、点击消息或窗口控制；不验证人工文件对话框和 VS 调试器交互。路径记忆测试使用系统临时文件设置提供器，仅改变设置存储位置，执行窗体实际读取、选择与保存方法，不读取或修改真实用户设置。
- 两套信息保留原始 JSON。跨语言比较时单独检查 `model_index`：普通模型与实例索引一致，流程兼容信息中的编号属于子模型；`input_shapes`、`model_info.input_shapes`、`data_info.image_size` 的缺失和 null 视为相同，OCR 信息按 C# API 已有行为过滤 `character`、`dict`、`classes`，其余字段严格比较。
- 退出码 0 表示通过，1 表示参数、运行或验证失败；结果写入 `--output` 指定的无 BOM 严格 UTF-8 JSON。模型文件只读；结果必须为系统临时目录中新建的 `.json`，已有文件不会被覆盖。

## 串行回归入口

```powershell
python Test/DlcvCSharpCppTest/run_tests.py --exe Test/DlcvCSharpCppTest/bin/x64/Debug/DlcvCSharpCppTest.exe --model "<普通模型>" --model "<流程模型>" --output-dir "$env:TEMP\mixed-regression"
```

`--model` 可重复指定；输出目录必须是系统临时目录下的空目录。每个模型运行两个共享方向各两种释放顺序、C++ 直接加载、分别加载的两种释放顺序，以及非交互 UI；另测启动配置、设计器、DPI 与图标、四档比例布局、空界面、错误参数与报告覆盖保护。UI 检查包括 C++ 先加载和 C# 先加载、重复加载拒绝及另一侧信息可继续读取。结果 JSON 严格 UTF-8 解析，PNG 检查格式和尺寸；进程实际模块路径须来自 `--sdk-directory`（默认 SDK 安装目录），包装 DLL 须来自本次 EXE 目录。不复制或更换推理 SDK，报告和图片不进入仓库。
