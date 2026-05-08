# dlcv_infer_c_dll 实施记录

## 改动记录

- 新增 `dlcv_infer_c_dll` C++ 动态库工程，并加入 `OpenIVS.sln` 的 `Debug|x64` 与 `Release|x64` 配置。
- 将 C API 头文件与实现从 `dlcv_infer_cpp_dll` 拆分到 `dlcv_infer_c_dll`：
  - `dlcv_infer_c_dll/dlcv_infer_c_api.h`
  - `dlcv_infer_c_dll/dlcv_infer_c_api.cpp`
- 从 `dlcv_infer_cpp_dll/dlcv_infer_cpp_dll.vcxproj` 移除旧的 C API 编译项与头文件项。
- 删除 `dlcv_infer_cpp_dll/dlcv_infer_c_api.cpp` 与 `dlcv_infer_cpp_dll/dlcv_infer_c_api.h`。
- `dlcv_infer_c_dll` 继续导出原有 4 个 C ABI 符号：
  - `dlcv_infer_cpp_load_model_c`
  - `dlcv_infer_cpp_free_model_c`
  - `dlcv_infer_cpp_infer_c`
  - `dlcv_infer_cpp_free_model_result_c`
- `dlcv_infer_c_dll` 显式链接 `dlcv_infer_cpp_dll.lib`，并对 `dlcv_infer_cpp_dll.dll` 使用 delay-load，避免 Python 仅加载 C DLL 时立即触发 C++ DLL 初始化。
- 更新 `Test/dlcv_infer_c_test/dlcv_infer_c_test.vcxproj`，使 C API 测试链接新的 `dlcv_infer_c_dll.lib`。
- 更新 `AGENTS.md`、`开发文档.md`、`C++ API文档.md` 中 C API 工程归属相关事实。

## 发现问题记录

### Python 直接加载 Debug/Release 根目录 DLL 时找不到依赖

直接运行：

```text
python "Y:\zxc\test_load_dlcv_dll.py" "C:\Users\Administrator\Desktop\OpenIVS\Debug\dlcv_infer_c_dll.dll"
python "Y:\zxc\test_load_dlcv_dll.py" "C:\Users\Administrator\Desktop\OpenIVS\Release\dlcv_infer_c_dll.dll"
```

结果为 `FileNotFoundError`，脚本输出确认目标 DLL 文件存在，失败原因是二级依赖 DLL 未在加载目录或搜索路径中。

处理方式：

- 按验证建议将测试脚本与 `dlcv_infer_c_dll.dll` 复制到 `dlcv_infer_cpp_qt_demo` 输出目录运行。
- Release 目录额外复制 `opencv_world4100.dll`。

### Release 下 C++ DLL 初始化失败

在 `Release/dlcv_infer_cpp_qt_demo` 目录加载 `dlcv_infer_c_dll.dll` 后，错误变为 `WinError 1114`。

进一步验证同目录的 `dlcv_infer_cpp_dll.dll` 也会在 Python 直接加载时触发 `WinError 1114`。该问题发生在 C++ DLL 初始化阶段，不是 C API DLL 文件缺失。

处理方式：

- `dlcv_infer_c_dll` 保持通过 `.lib` 显式链接 `dlcv_infer_cpp_dll`。
- 在 `dlcv_infer_c_dll.vcxproj` 中为 `dlcv_infer_cpp_dll.dll` 配置 delay-load。
- Python 仅导入 `dlcv_infer_c_dll.dll` 时不立即初始化 `dlcv_infer_cpp_dll.dll`；实际调用 C API 时再加载 C++ DLL。

### Debug 全解决方案构建存在无关错误

`Debug|x64` 构建 `OpenIVS.sln` 时，已有项目 `HalconDemo` 报错：

```text
HalconDemo\Form1.cs(1336,45): error CS0246: 找不到类型或命名空间名 SlidingWindowModel
```

本次相关项目在该次构建中已生成：

- `Debug/dlcv_infer_cpp_dll.dll`
- `Debug/dlcv_infer_c_dll.dll`
- `Debug/dlcv_infer_c_test.exe`

## 构建验证记录

### Debug 构建

命令：

```text
python ".cursor/skills/vs-build/scripts/build.py" "OpenIVS.sln" --configuration Debug --platform x64 --target Build --verbosity minimal
```

结果：

- `OpenIVS.sln` 最终 exit_code 为 `1`。
- 失败点为 `HalconDemo` 的既有 C# 编译错误。
- 本次相关 C++/C API 项目已完成生成。

### Release 构建

命令：

```text
python ".cursor/skills/vs-build/scripts/build.py" "OpenIVS.sln" --configuration Release --platform x64 --target Build --verbosity minimal
```

结果：

- `OpenIVS.sln` 构建成功。
- exit_code 为 `0`。
- 已生成 `Release/dlcv_infer_c_dll.dll` 与 `Release/dlcv_infer_c_test.exe`。

## Python 导入验证记录

### Debug

执行目录：

```text
C:\Users\Administrator\Desktop\OpenIVS\Debug\dlcv_infer_cpp_qt_demo
```

命令：

```text
python "test_load_dlcv_dll.py" "C:\Users\Administrator\Desktop\OpenIVS\Debug\dlcv_infer_cpp_qt_demo\dlcv_infer_c_dll.dll"
```

结果：

```text
Load OK
```

### Release

执行目录：

```text
C:\Users\Administrator\Desktop\OpenIVS\Release\dlcv_infer_cpp_qt_demo
```

命令：

```text
python "test_load_dlcv_dll.py" "C:\Users\Administrator\Desktop\OpenIVS\Release\dlcv_infer_cpp_qt_demo\dlcv_infer_c_dll.dll"
```

结果：

```text
Load OK
```

## C API 控制台测试记录

### Release

命令：

```text
$env:PATH="C:\Users\Administrator\Desktop\OpenIVS\Release;C:\Users\Administrator\Desktop\OpenIVS\Release\dlcv_infer_cpp_qt_demo;C:\OpenCV\build\x64\vc16\bin;" + $env:PATH
& "C:\Users\Administrator\Desktop\OpenIVS\Release\dlcv_infer_c_test.exe"
```

关键输出：

```text
推理结果: 2个
Test PASSED
```

### Debug

命令：

```text
$env:PATH="C:\Users\Administrator\Desktop\OpenIVS\Debug;C:\Users\Administrator\Desktop\OpenIVS\Debug\dlcv_infer_cpp_qt_demo;C:\OpenCV\build\x64\vc16\bin;" + $env:PATH
& "C:\Users\Administrator\Desktop\OpenIVS\Debug\dlcv_infer_c_test.exe"
```

关键输出：

```text
推理结果: 2个
Test PASSED
```

## 当前验证结论

- `dlcv_infer_c_dll.dll` 在 Debug 与 Release 下均可被 Python 正常导入。
- `Test/dlcv_infer_c_test` 在 Debug 与 Release 下均能通过新 `dlcv_infer_c_dll` 完成模型加载、推理、结果校验和资源释放。
- `Debug|x64` 全解决方案构建仍受 `HalconDemo` 既有编译错误影响；该错误不属于本次 C API DLL 拆分改动。
