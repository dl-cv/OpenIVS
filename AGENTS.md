# OpenIVS Agent Rules

## 必读顺序

1. 开始任何项目任务前先读取 `开发文档.md`。
2. 处理具体工程或子模块时，在读取 `开发文档.md` 后再读取对应子文档。
3. 更新任何项目文档时，以 `开发文档.md` 中的文档规则为唯一规则来源，不在子文档重复书写这些规则。

## 编译规则

1. 任何编译、构建、重建与发布前构建验证，必须且只能通过项目级 skill 脚本 `.cursor/skills/vs-build/scripts/build.py` 执行。
2. 默认构建目标为 `OpenIVS.sln`。
3. 默认构建配置使用 `Debug`、`x64`、`Build`、`minimal`。
4. 用户明确指定 `.csproj`、`Release`、`Rebuild`、`Clean` 等参数时，按用户要求覆盖默认值。
5. 严禁直接使用 `msbuild`、`dotnet`、`powershell` 或其他本地 shell 命令进行编译。
6. skill 构建脚本不可用、报错或权限不足时，立即停止构建动作并先报告，不允许切换到其他命令链路。

## 项目实现约束

1. 项目使用 `.NET Framework 4.7.2`。
2. WinForms 工程的界面定义写在 `Form1.Designer.cs`，代码逻辑写在 `Form1.cs`。
3. WPF 工程优先使用 WPF 风格实现，例如绑定显示。
4. 代码保持规范、清晰、便于开源维护。
