# SentinelManager

独立的 Windows x64 Sentinel 设备与服务管理组件，不属于根目录 wheel 发布流程。

## 依赖

- 运行环境：Windows x64、.NET Framework 4.7.2。
- 编译环境：Python 3.10 或更高版本、Visual Studio MSBuild、.NET Framework 4.7.2 开发工具包。工具链由仓库 `.cursor/skills/vs-build/scripts/build.py` 查找。
- 安装版本检查使用 Windows PowerShell 的 `Get-Item ... VersionInfo`。
- 真实设备操作需要本机 Sentinel LDK Runtime、`hasplms` 服务及 ACC 可访问。组件安装不安装或修改这些依赖；隔离测试不连接真实设备或服务。

## 五项功能与操作确认

| 功能 | 确认与权限风险 |
| --- | --- |
| 获取设备与服务信息 | 无需界面确认；访问本机 Sentinel 服务。服务停止或暂停且具备管理员权限时，会启动或恢复服务，并非完全只读。 |
| 创建新的 LMID | 执行前确认，可能影响现有授权连接；以返回结果为准。 |
| 关闭网络访问 | 执行前确认，关闭访问远程授权、广播搜索及远程客户端访问，可能影响网络授权。 |
| 应用本地服务补丁 | 执行前确认，需要管理员权限；覆盖 `%LocalAppData%\SafeNet Sentinel\Sentinel LDK\hasp_26146.ini`，写入本机服务地址并关闭广播搜索，随后重启 `hasplms` 服务。 |
| 一键修复 | 执行前确认，需要管理员权限；执行网络配置修改、LMID 请求、本地配置写入和服务重启，授权连接可能短暂中断。 |

程序启动不自动执行管理操作。除信息查询可能启动或恢复服务外，其余四项功能执行前均须经界面确认；编译、测试、打包及安装脚本不启动真实客户端，也不提升权限或执行以上修改。

## 编号入口

从仓库根目录运行以下命令，脚本不要求当前工作目录固定，也不等待按键：

```powershell
& .\SentinelManager\1_编译打包.bat
& .\SentinelManager\2_安装新版.bat
```

1. `1_编译打包.bat` 调用同目录 `build_package.py build`。仅通过仓库构建脚本编译 `Test/SentinelManagerTest/SentinelManagerTest.csproj`，参数为 `Release x64 Build minimal`；工程引用自动编译应用。随后依次执行后端隔离测试、非交互 UI 回归及结果检查，通过后才生成 ZIP。
2. `2_安装新版.bat` 调用 `build_package.py install`。读取 `Properties/AssemblyInfo.cs` 的信息版本，只选择 `dist/SentinelManager-<信息版本>-win-x64.zip`，不扫描或选择其他版本，不触发编译。

`Properties/AssemblyInfo.cs` 是唯一版本来源。程序集版本与文件版本使用相同四段整数，信息版本可以追加 `aN`。打包前与安装前后均核验 EXE 的 `FileVersion` 和 `ProductVersion`，后者对应程序集信息版本。

ZIP 必须且只含 `SentinelManager.exe`、`SentinelManager.exe.config`、`README.md` 三个非空文件。运行配置由 `App.config` 生成；缺少配置或配置为空时拒绝打包和安装。不包含测试程序、PDB、源码、截图、JSON 结果、外部 DLL 或授权文件。不调用根目录 wheel、签名、上传流程，不修改密钥相关实现。

## DPI 适配

界面采用 96 DPI 逻辑尺寸与点单位字体；窗口、按钮、页签及响应式布局按实际 DPI 缩放，初始窗口限制在当前屏幕工作区内。`App.config` 启用 PerMonitorV2，应用与测试使用同一来源配置。

## 非交互验证

构建后测试程序位于 `Test/SentinelManagerTest/bin/Release/SentinelManagerTest.exe`：

- 无参数：执行后端隔离测试，退出码为零才继续。
- `ui-test --output-dir <系统临时目录内的专用子目录>`：执行固定数据界面回归，不模拟鼠标键盘或控制桌面窗口。结果为严格 UTF-8 的 `ui-test-results.json`，截图为 `initial.png`、`result.png`、`raw-result.png`、`compact.png`。
- 打包脚本检查进程退出码、JSON 总结果与每项检查、隔离数据来源、时间、截图清单与 PNG 文件；任何检查失败均停止，不报告成功。后端和 UI 测试顺序执行，不并行构建。
- 验证和安装中间文件使用 `tempfile.TemporaryDirectory` 写入系统临时目录，退出后清理。ZIP 保留在组件 `dist/`，不保留打包内部临时截图；用于 PR 的额外截图由独立验证流程保留在系统临时目录，不加入安装包。
- 纯 Python 隔离测试：在仓库根目录执行 `python -B -m unittest discover -s Test -p test_sentinel_package.py -v`，不会调用真实构建、安装或客户端。

### DPI 回归

构建后可执行独立 DPI 回归，不更改系统显示设置：

```powershell
python -B Test/run_sentinel_dpi_regression.py --output-dir <系统临时空子目录> --expected-native-dpi 144
```

`--expected-native-dpi` 可省略；指定 144 时要求本机实际为 150%。脚本串行运行正常 PerMonitorV2 进程和 `DpiAwareness=false` 的独立临时测试进程。后者为 Windows 返回 96 DPI 的虚拟化测试环境，不代表真实 100% 显示器。两组均校验窗口、按钮、字体、文字裁切和操作显示，保存各自四张 PNG，并在 `dpi-regression-results.json` 比较控件尺寸与 DPI 比例。测试进程结束后清理二进制副本，报告与截图保留在输出目录；未验证物理显示器切换。

## 安装位置与安全限制

安装到当前用户 `%LocalAppData%\Programs\OpenIVS\SentinelManager`，不读写生产数据。无需管理员权限，不修改其他组件、服务、注册表或系统 PATH，不创建自动启动配置，不启动应用。

安装前检查 ZIP 的平面文件白名单、必需文件、重复名称、路径、文件类型、大小、压缩格式和完整性。拒绝目录、链接、路径穿越、附加文件及加密 ZIP；安装位置及其父目录不得包含链接或重解析点。安装程序不校验发布者签名，仅使用可信本地构建的安装包。

写入前完成安装包 EXE 版本核验，并在系统临时目录保存即将覆盖的同名文件；写入后逐文件进行字节比对，再读取已安装 EXE 版本，通过后打印安装位置与两个版本。失败时尝试恢复本次覆盖的文件，仅删除本次新建的文件；恢复失败也会返回失败，不报告安装成功。

只覆盖清单内的同名文件，其他来源文件保留不动。安装包缺少运行配置时，在写入前停止，现有配置及其他文件保持不变。安装前应关闭正在运行的组件，文件被占用或权限不足时不得视为安装完成。
