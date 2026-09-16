# SentinelManager

独立的 Windows x64 Sentinel 设备与服务管理组件，不属于根目录 wheel 发布流程。

## 依赖

- 运行环境：Windows x64、.NET Framework 4.7.2。
- 编译环境：Python 3.10 或更高版本、Visual Studio MSBuild、.NET Framework 4.7.2 开发工具包。工具链由仓库 `.cursor/skills/vs-build/scripts/build.py` 查找。
- 真实设备操作需要本机 Sentinel LDK Runtime、`hasplms` 服务及 ACC 可访问。隔离测试不连接真实设备或服务。

## 五项功能与操作确认

| 功能 | 确认与权限风险 |
| --- | --- |
| 获取设备与服务信息 | 无需界面确认；访问本机 Sentinel 服务。服务停止或暂停且具备管理员权限时，会启动或恢复服务，并非完全只读。 |
| 创建新的 LMID | 执行前确认；先读取当前 LMID，发送一次重置请求，再自动回读确认标识已变化。可能影响现有授权连接。 |
| 关闭网络访问 | 执行前确认，关闭访问远程授权、广播搜索及远程客户端访问，可能影响网络授权。 |
| 应用本地服务补丁 | 执行前确认，需要管理员权限；覆盖 `%LocalAppData%\SafeNet Sentinel\Sentinel LDK\hasp_26146.ini`，写入本机服务地址并关闭广播搜索，随后重启 `hasplms` 服务。 |
| 一键修复 | 执行前确认，需要管理员权限；执行网络配置修改、LMID 自动核验、本地配置写入和服务重启；重启后再次确认 LMID 保持新值，授权连接可能短暂中断。 |

程序启动不自动执行管理操作。除信息查询可能启动或恢复服务外，其余四项功能执行前均须经界面确认；隔离测试不启动真实客户端，也不提升权限或执行以上修改。

## 工程运行

使用 Visual Studio 打开 `OpenIVS.sln`，将 `SentinelManager` 设为启动项目后运行；命令行日常编译使用仓库共享 `.cursor/skills/vs-build/scripts/build.py`，不提供独立打包或安装入口。

`Properties/AssemblyInfo.cs` 为程序集版本来源。运行配置由 `App.config` 生成，运行时须与程序放在同一目录。

## 配置页面兼容

网络状态读取沿用 Python 原版按 HTML 输入控件 ID 查找目标设置的方式。同名控件重复出现时采用页面中最后出现的值，不因页面重复 ID 拒绝操作；目标控件缺失、类型不符或远程客户端选项无效时仍停止操作。

## LMID 自动核验

`GET /_int_/tab_diag2.html` 返回 `/*JSON:diagnostics*/` 诊断对象，其中 `srvguid` 为当前 LMID。已通过本机实际重置响应中的 old/new 标识及前后诊断读取确认。解析时只为裸字段名补充 JSON 引号，不执行页面脚本；要求唯一且有效的 `srvguid`。

创建流程先读取原值，失败时不发送修改。重置请求只发送一次，再最多六次回读、间隔 0.5 秒；新值与原值不同才报告已核验。即使重置请求响应异常，也只继续读取，不重复提交。回读失败或值未变化时显示具体错误并停止后续操作。

一键修复在服务重启后再次回读，确认与刚生成的新 LMID 相同，并检查网络状态、配置写入及设备查询后才报告完成。结果显示原值、新值和重启后的核验结果；失败保留已经完成的步骤。

## DPI 适配

界面采用 96 DPI 逻辑尺寸与点单位字体；窗口、按钮、页签及响应式布局按实际 DPI 缩放，初始窗口限制在当前屏幕工作区内。`App.config` 启用 PerMonitorV2，应用与测试使用同一来源配置。

## 非交互验证

构建后测试程序位于 `Test/SentinelManagerTest/bin/Release/SentinelManagerTest.exe`：

- 无参数：执行后端隔离测试，退出码为零才继续。
- `ui-test --output-dir <系统临时目录内的专用子目录>`：执行固定数据界面回归，不模拟鼠标键盘或控制桌面窗口。结果为严格 UTF-8 的 `ui-test-results.json`，截图为 `initial.png`、`result.png`、`raw-result.png`、`compact.png`。

### DPI 回归

构建后可执行独立 DPI 回归，不更改系统显示设置：

```powershell
python -B Test/run_sentinel_dpi_regression.py --output-dir <系统临时空子目录> --expected-native-dpi 144
```

`--expected-native-dpi` 可省略；指定 144 时要求本机实际为 150%。脚本串行运行正常 PerMonitorV2 进程和 `DpiAwareness=false` 的独立临时测试进程。后者为 Windows 返回 96 DPI 的虚拟化测试环境，不代表真实 100% 显示器。两组均校验窗口、按钮、字体、文字裁切和操作显示，保存各自四张 PNG，并在 `dpi-regression-results.json` 比较控件尺寸与 DPI 比例。测试进程结束后清理二进制副本，报告与截图保留在输出目录；未验证物理显示器切换。
