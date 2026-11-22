# 一个 DLL 和一个头文件的 C++ Demo

`dlcv_infer_binary_demo` 加载一个模型、读取一张图片、推理并打印类别、分数、框、角度和面积。编译调用端只需要 `main.cpp`、`dlcv_infer_c_api.h` 和 Visual Studio C++ / Windows SDK；不编译 OpenIVS 实现，也不链接 DLCV `.lib`。

完整使用教程：[C 接口 DVST 模型推理例程](https://docs.dlcv.com.cn/deploy/sdk/c_demo/)

## 获取示例代码

示例代码位于本仓库的 `dlcv_infer_binary_demo` 目录：

* 国内：[https://gitee.com/dl-cv/OpenIVS](https://gitee.com/dl-cv/OpenIVS)
* 国外：[https://github.com/dl-cv/OpenIVS](https://github.com/dl-cv/OpenIVS)

头文件与 DLL 随深度视觉 AI 平台安装，位于 `C:\dlcv\Lib\site-packages\dlcvpro_infer`（`include\dlcv_infer_c_api.h`、`dlcv_infer_cpp.dll`），不需要另外准备。

## 运行

解压开发包后，在包内执行：

```powershell
.\dlcv_infer_binary_demo.exe "Y:\测试模型\模型1-元件提取_PLUS_s.dvst" "Y:\测试模型\AOI-3.jpg"
```

第三个参数可以指定 DLL；省略时使用 EXE 同目录的 `dlcv_infer_cpp.dll`，与当前工作目录无关：

```powershell
.\dlcv_infer_binary_demo.exe "Y:\测试模型\模型1-元件提取_PLUS_s.dvst" "Y:\测试模型\AOI-3.jpg" "C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer_cpp.dll"
```

GPU 固定为 `0`，阈值固定为 `0.5`，批量大小为 `1`，与原最小 Demo 一致。`--help` 显示用法。成功返回 `0`，执行失败返回 `1`，参数数量错误返回 `2`。推理耗时仅包含一次推理调用，不含读图、模型加载和预热。

元件提取流程模型对 `AOI-3.jpg` 的输出示例：

```text
模型: Y:\测试模型\模型1-元件提取_PLUS_s.dvst
图片: Y:\测试模型\AOI-3.jpg
批量大小: 1
threshold: 0.50
推理时间: 447.29ms
推理结果: 6个

[1] 电容0         score=0.67  bbox=(144.0, 28.0, 121.0, 63.0)  area=-1.0
[2] 二极管90      score=1.00  bbox=(91.0, 463.0, 335.0, 113.0)  area=-1.0
[3] 二极管90      score=1.00  bbox=(95.0, 222.0, 323.0, 115.0)  area=-1.0
[4] 二极管90      score=1.00  bbox=(93.0, 342.0, 328.0, 117.0)  area=-1.0
[5] 电容90        score=1.00  bbox=(39.0, 187.0, 63.0, 120.0)  area=-1.0
[6] 二极管90      score=1.00  bbox=(93.0, 104.0, 330.0, 114.0)  area=-1.0
```

SDK 调试日志使用本地代码页，在 UTF-8 控制台中会显示为乱码，不影响 Demo 自身的 UTF-8 输出。

## 开发包与运行依赖

| 文件或环境 | 用途 |
| --- | --- |
| `main.cpp` | 完整调用示例和 Windows WIC 读图 |
| `dlcv_infer_c_api.h` | 唯一 DLCV 公开头文件，必须与 DLL 配套；平台安装在 `C:\dlcv\Lib\site-packages\dlcvpro_infer\include` |
| `dlcv_infer_binary_demo.vcxproj` | 独立 VS 工程，仅链接 Windows 系统库 |
| `dlcv_infer_cpp.dll` | 已编译的 OpenIVS 推理包装层；平台安装在 `C:\dlcv\Lib\site-packages\dlcvpro_infer` |
| `dlcv_infer_binary_demo.exe` | 已编译的命令行程序 |
| 已安装的 DLCV SDK、OpenCV 与 VC++ 运行库 | DLL 内部仍需要的运行环境 |

调用端不需要 OpenCV、JSON 或 Qt 开发文件。**现有 DLL 不是单文件自包含引擎**：`dlcv_infer_cpp.dll` 仍依赖 `opencv_world4100.dll`、VC++ 运行库，以及底层 `dlcv_infer.dll` / `dlcv_infer_v.dll`。该包适用于已有 DLCV SDK 的 Windows x64 机器，不包含完整 SDK。需要读码流程时还需该版本使用的 `ZXing.dll`。

`opencv_world4100.dll` 随平台安装在 `C:\dlcv\bin`；底层 `dlcv_infer.dll` / `dlcv_infer_v.dll` 在 `C:\dlcv\Lib\site-packages\dlcvpro_infer`。程序提示缺少 DLL 时，把对应目录加入 `PATH`，或把 DLL 复制到程序目录。

Windows 加载器先从指定 DLL 所在目录解析依赖，再使用系统 DLL 搜索路径；可将配套运行库放在该 DLL 旁，或使用已配置好的 SDK 环境。不需要把 `dlcv_infer_cpp.lib` 放入包内。Demo 的 C++ 运行库采用静态链接，DLL 自身的运行库需求仍需满足。

## 调用设计

1. `wmain` 保留 Unicode 文件路径；WIC 读取图片第一帧并生成连续的 8 位 RGB 数据。
2. `LoadLibraryExW` 加载指定 DLL，`GetProcAddress` 解析五个现有 C 导出。
3. `dlcv_infer_cpp_load_model_c` 加载模型；模型路径使用本地 ANSI（GBK）字符串，Demo 把宽字符路径转为本地 ANSI 后传入。
4. `DlcvCImageList` 传递 RGB 像素地址、宽高和通道数；`dlcv_infer_cpp_infer_with_params_c` 接收 `{"threshold":0.5}`。
5. 遍历 `DlcvCResult` 打印结果。类别名由本地代码页转为 UTF-8，Demo 文本也输出 UTF-8。
6. 作用域退出时依次释放结果、模型和 DLL，异常路径执行同样的释放。DLL 分配的结果始终交还 DLL，调用端不使用 `delete` 释放。

函数指针类型使用公开头文件中的 `decltype(&函数名)`，不另抄一套 ABI 声明；头文件不暴露 `cv::Mat` 或 JSON 类型。官方头文件在仓库中只维护一份，打包时收集到包内。

WIC 与 OpenCV 是不同图片解码器。此 Demo 读取第一帧，不应用 EXIF 方向，逐像素输出与 OpenCV 解码可能不同；需要沿用应用自身读图逻辑时，直接将连续 RGB 缓冲区填入 `DlcvCImage` 即可。

## 调用要求

* 模型编号：加载成功返回非负整数编号，使用结束后用 `dlcv_infer_cpp_free_model_c` 释放；`-1` 表示加载失败。
* 图像数据：8 位、RGB、连续内存；图像内存在推理返回前保持有效。批量推理时 `DlcvCImageList` 内各图片宽、高一致。
* 推理参数：`params_json` 为 UTF-8 JSON 对象，Demo 传 `{"threshold":0.5}`；不传参数时使用 `dlcv_infer_cpp_infer_c`。
* 推理结果：每次推理返回的 `DlcvCResult` 都必须调用 `dlcv_infer_cpp_free_model_result_c` 释放。`category_name` 为本地代码页（GBK）字符串，`extra_info` 为 UTF-8 JSON，mask 为 8 位单通道；流程模型未提供面积时 `area` 为 -1。
* 释放函数：结构化结果用 `dlcv_infer_cpp_free_model_result_c`，信息接口返回的字符串用 `dlcv_infer_cpp_free_string_c`，`dlcv_infer_cpp_get_last_error_c()` 返回借用指针不需要释放。结果与模型可以按任意顺序释放，但都要在卸载 DLL 之前完成。
* 线程：错误信息与流程判定状态按线程保存；流程判定在推理成功返回后、下一次推理前、同一线程读取。

## 独立编译与重新打包

用 Visual Studio 2022 打开仓库内 `dlcv_infer_binary_demo/dlcv_infer_binary_demo.vcxproj`，选择 `Release / x64` 生成；工程从仓库的 `dlcv_infer_cpp` 目录读取头文件。自行新建工程时设置：C++17、附加包含目录 `C:\dlcv\Lib\site-packages\dlcvpro_infer\include`、命令行加 `/utf-8`、链接器附加依赖项 `windowscodecs.lib;ole32.lib`。

收到开发包后用 Visual Studio 2022 打开包内 `.vcxproj`，选择 `Release / x64` 并生成。工程优先使用同目录头文件，输出在 `bin\Release`。运行新 EXE 时传入包根目录 DLL 的绝对路径，或将 DLL 放到新 EXE 旁。

仓库内使用统一入口：

```powershell
python build_script/build.py dlcv_infer_binary_demo/dlcv_infer_binary_demo.vcxproj --configuration Release
python build_script/package_binary_demo.py --dll "D:\sdk\dlcv_infer_cpp.dll"
```

打包脚本不编译 DLL，收集当前公开头文件、Demo 源码、独立工程、已编译 EXE、本文和指定 DLL，生成 `dist/dlcv_infer_binary_demo.zip`。必须选择与头文件配套的 DLL；开发包不复制项目其他源码，也不引用仓库内构建 `.props` 或 `.targets`。

## 验证

`Test/test_binary_demo.py` 运行实际 EXE，覆盖帮助、参数错误、图片不存在或损坏、DLL 不存在或缺少导出、模型不存在及真实推理。通过以下环境变量指定材料：

```powershell
$env:DLCV_BINARY_DEMO_EXE = "D:\demo\dlcv_infer_binary_demo.exe"
$env:DLCV_BINARY_DEMO_DLL = "D:\demo\dlcv_infer_cpp.dll"
$env:DLCV_BINARY_DEMO_MODEL = "Y:\测试模型\模型1-元件提取_PLUS_s.dvst"
$env:DLCV_BINARY_DEMO_IMAGE = "Y:\测试模型\AOI-3.jpg"
$env:DLCV_BINARY_DEMO_EXPECTED = "D:\test\expected.json"
python -m unittest Test.test_binary_demo -v
```

`expected.json` 是独立确认的结果数组，每项包含 `name`、`score`、`bbox` 和 `area`，数值使用 Demo 打印精度。没有真实输入时对应检查明确跳过，不记为通过。
