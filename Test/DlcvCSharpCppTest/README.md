# C# / C++ 混编模型测试

## 工程

- `DlcvCSharpCppTest.csproj`：C# 7.3、.NET Framework 4.7.2、WinForms、x64。
- `Bridge/DlcvCSharpCppBridge.vcxproj`：VS 2022 v143 C++/CLI 动态库，原生模型实现单独以 C++17 编译。
- 两个项目位于 `OpenIVS.sln` 的 Test 分组。依赖通过工程引用构建，不复制 API 源码，不加入发布打包流程。
- `DlcvNativeRuntime.targets` 从引用工程取得本次 `dlcv_infer_cpp.dll`；桥接工程提供 OpenCV 运行库路径，由 C# 工程复制到 EXE 目录。

## Visual Studio 界面编辑

打开 `OpenIVS.sln`，在 Test 分组的 `DlcvCSharpCppTest` 项目中右键 `MainForm.cs` → 查看设计器，或选中文件按 Shift+F7。控件位置、大小、文本和事件可通过 .NET Framework Windows Forms 设计器编辑。

- `MainForm.Designer.cs`：标准 `InitializeComponent()`、控件字段、布局和事件连接。
- `MainForm.resx`：窗体资源，由设计器维护。
- `MainForm.cs`：按钮处理、状态更新与模型释放。
- 窗体构造仅初始化控件，模型会话在运行操作时创建；设计器不加载模型，不调用原生推理 DLL。

## 模型选择与六个操作按钮

| 按钮 | 行为 |
|---|---|
| 加载C#模型 | 使用已选择的模型和设备编号创建 C# `Model`；尚未选择时打开文件选择窗口 |
| 转换为C++模型 | 将 C# `modelIndex` 传给 C++/CLI，通过 `CreateModelFromIndex` 创建 C++ `Model` |
| 获取C#模型信息 | 调用 C# `GetModelInfo()`，在左侧显示格式化 JSON |
| 获取C++模型信息 | 调用 C++ `GetModelInfo()`，按严格 UTF-8 转为托管字符串，在右侧显示 JSON |
| 释放C#模型 | 释放 C# 持有关系，清空左侧状态和信息 |
| 释放C++模型 | 销毁 C++ 对象并解除共享绑定，清空右侧状态和信息 |

路径框旁提供独立的“浏览…”按钮，路径框只读，无需手填。浏览只选择文件，不加载模型。与 DlcvDemo 相同，使用 .NET 用户级 `LastModelPath` 设置，选择成功后立即 `Save()`；取消选择不改变原路径或记录。程序再次打开时恢复路径，文件窗口通过 `InitialDirectory` 和 `FileName` 定位上次目录与文件。记录属于本测试程序，不修改 DlcvDemo 的用户设置；设计器构造不读取设置。两侧模型全部释放后可以重新浏览选择。

仅接受本地 `.dvt`、`.dvo`、`.dvst`、`.dvso`；设备默认 0，-1 为 CPU。不存在任一模型时可以加载，两侧尚未全部释放时不能替换模型。C++ 对象已存在时不能重复转换。

转换是同进程模型索引共享，不改变模型文件格式，不重复加载文件，也不保存新的模型文件。共享绑定成功后，可以先释放任一语言对象，再从另一语言获取信息；关闭窗口按 C++、C# 顺序释放。操作异常显示在底部状态区，不弹出错误对话框。程序不接收图片、不调用推理接口。

运行环境须安装深度视觉 SDK，且实际加载的推理 DLL 提供共享索引查询、绑定、解绑和信息接口。旧 SDK 缺少这些接口时，加载与 C# 信息查询可以成功，但转换会明确报错，不改变已有 C# 模型。编译成功不表示已安装 SDK 具有共享能力。

## 构建

在仓库根目录使用日常构建入口：

```powershell
python .cursor/skills/vs-build/scripts/build.py Test/DlcvCSharpCppTest/DlcvCSharpCppTest.csproj --configuration Debug --platform x64 --target Build --verbosity minimal
```

输出：`Test/DlcvCSharpCppTest/bin/x64/Debug/DlcvCSharpCppTest.exe`。双击或不带参数运行进入界面。`Test/1_编译测试.bat` 同时包含本工程的 Release x64 构建。

## 非交互验证

```powershell
& .\Test\DlcvCSharpCppTest\bin\x64\Debug\DlcvCSharpCppTest.exe designer-test --output "$env:TEMP\mixed-designer.json"
& .\Test\DlcvCSharpCppTest\bin\x64\Debug\DlcvCSharpCppTest.exe ui-test --output "$env:TEMP\mixed-ui.json"
& .\Test\DlcvCSharpCppTest\bin\x64\Debug\DlcvCSharpCppTest.exe ui-test --model "<模型文件>" --device 0 --output "$env:TEMP\mixed-model.json"
```

- `designer-test` 仅构造和释放未显示的窗体，检查六个模型操作按钮和浏览按钮，并检查进程未加载 C++/CLI 桥接或原生推理模块。
- `ui-test` 无 `--model` 时检查浏览按钮和六个操作按钮的可用状态、选择即保存、取消不改变、重建窗体与设置对象恢复记录、上次目录与文件名、空模型操作、重复释放、无效模型、禁止的模型格式和负索引。
- 指定模型时额外验证两侧索引一致、两侧信息、两种释放顺序、释放后再次转换、最终索引失效及窗口对象释放。
- 测试仅创建未显示的 WinForms 对象，不执行桌面自动化、键鼠模拟、按钮点击或窗口控制；不验证人工文件选择和界面绘制。路径记忆测试使用系统临时文件设置提供器，仅改变设置存储位置，执行窗体实际读取、选择与保存方法，不读取或修改真实用户设置。
- 两套信息保留原始 JSON。跨语言比较时单独检查 `model_index`：普通模型与实例索引一致，流程兼容信息中的编号属于子模型；`input_shapes`、`model_info.input_shapes`、`data_info.image_size` 的缺失和 null 视为相同，OCR 信息按 C# API 已有行为过滤 `character`、`dict`、`classes`，其余字段严格比较。
- 退出码 0 表示通过，1 表示参数、运行或验证失败；结果写入 `--output` 指定的无 BOM 严格 UTF-8 JSON。模型文件只读；结果必须为系统临时目录中新建的 `.json`，已有文件不会被覆盖。
