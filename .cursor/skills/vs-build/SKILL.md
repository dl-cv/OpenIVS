---
name: vs-build
description: 使用项目级 skill 内置的本地 Python 构建脚本执行编译、构建、重建与发布前构建验证。用户提到编译、构建、build、rebuild、重新编译、解决方案、项目、sln、csproj、发布前验证时使用。禁止切换到 dotnet、msbuild、powershell 或其他直接编译命令。
---

# VS Build

## 目的

把编译请求统一收敛到 skill 内置脚本 `.cursor/skills/vs-build/scripts/build.py`，避免误用 `dotnet build`、`msbuild`、`powershell` 等直接编译命令。

## 适用场景

当用户提出以下任一诉求时应用本 skill：

- 编译、构建、重建、重新编译
- 发布前构建验证
- 构建某个解决方案或某个 C# 项目
- 提到 `.sln`、`.csproj`、解决方案、项目路径

不适用于运行程序、执行测试、查看日志、安装依赖。

## 固定规则

1. 编译前先读取 `开发文档.md`；如已知是本项目任务，也同时遵守 `AGENTS.md`。
2. 构建只能通过 `.cursor/skills/vs-build/scripts/build.py` 执行。
3. 默认构建参数使用：
   - `configuration=Debug`
   - `platform=x64`
   - `target=Build`
   - `verbosity=minimal`
4. 用户明确要求 `Release`、`Rebuild`、`Clean`、`diagnostic` 等时，才覆盖默认值。
5. 脚本报错、MSBuild 不存在、目标路径无效或参数不明确时，立即停止并向用户报告。
6. 绝不回退到 `dotnet`、`msbuild`、`devenv`、`powershell` 或其他直接编译命令。
7. 不依赖外部 `C:/mcp-build-server.py` 或全局 MCP 配置。

## 目标判定

按下面顺序确定构建目标：

1. 用户明确给出 `.sln` 路径：构建该解决方案。
2. 用户明确给出 `.csproj` 路径：构建该项目。
3. 用户只说“编译这个项目”，但上下文已明确某个 C# 项目：解析到对应 `.csproj` 后构建该项目。
4. 用户只说“编译一下”“帮我构建”而未限定范围：优先使用仓库默认解决方案。
5. 若请求针对的不是 `.csproj`，不要猜测其他编译命令；优先转为包含它的解决方案构建，或先向用户确认范围。

## 执行流程

### 1. 识别范围

- 先判断是“整个解决方案”还是“单个 C# 项目”。
- 范围不清楚时，优先使用仓库默认解决方案，除非上下文已经清楚指向某个项目。

### 2. 调用脚本

统一执行：

```bash
python ".cursor/skills/vs-build/scripts/build.py" "<target-path>" --configuration Debug --platform x64 --target Build --verbosity minimal
```

按用户要求覆盖参数：

- 整体编译：`<target-path>` 使用 `.sln`
- 单个 C# 项目编译：`<target-path>` 使用 `.csproj`
- 重建：`--target Rebuild`
- 清理：`--target Clean`
- 发布前验证：按用户要求切换到对应目标或配置

### 3. 返回结果

- 成功时说明：构建目标、配置、平台、是否成功
- 失败时优先返回脚本错误或构建输出摘要
- 不补充任何“也可以试试 dotnet/msbuild”的替代建议

## 示例

### 示例 1

用户说：`编译一下`

处理方式：

- 读取 `开发文档.md`
- 运行 `python ".cursor/skills/vs-build/scripts/build.py"`
- 参数默认使用 `Debug + x64 + Build + minimal`

### 示例 2

用户说：`帮我编译某个 csproj`

处理方式：

- 将目标解析为对应 `.csproj`
- 运行 `python ".cursor/skills/vs-build/scripts/build.py" "<project-path>"`
- 参数默认使用 `Debug + x64 + Build + minimal`

### 示例 3

用户说：`重建某个 sln，用 Release`

处理方式：

- 运行：

```bash
python ".cursor/skills/vs-build/scripts/build.py" "<solution-path>" --configuration Release --platform x64 --target Rebuild
```

## 禁止事项

- 不先试 `dotnet build`
- 不先试 `msbuild`
- 不用 `powershell` 包一层再编译
- 不调用外部 `C:/mcp-build-server.py`
- 不因为脚本失败就自动改走其他编译命令
- 不把读取项目文件、解决方案文件、目录结构这类动作解释成“可以绕过 skill 脚本”
