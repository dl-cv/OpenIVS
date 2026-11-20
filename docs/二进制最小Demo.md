# 一个 DLL 和一个头文件的 C++ Demo

`dlcv_infer_binary_demo` 加载一个模型、读取一张图片、推理并打印类别、分数、框、角度和面积。编译调用端只需要 `main.cpp`、`dlcv_infer_c_api.h` 和 Visual Studio C++ / Windows SDK；不编译 OpenIVS 实现，也不链接 DLCV `.lib`。

## 运行

解压开发包后，在包内执行：

```powershell
.\dlcv_infer_binary_demo.exe "D:\models\model.dvst" "D:\images\image.jpg"
```

第三个参数可以指定 DLL；省略时使用 EXE 同目录的 `dlcv_infer_cpp.dll`，与当前工作目录无关：

```powershell
.\dlcv_infer_binary_demo.exe "D:\models\model.dvst" "D:\images\image.jpg" "D:\sdk\dlcv_infer_cpp.dll"
```

GPU 固定为 `0`，阈值固定为 `0.5`，批量大小为 `1`，与原最小 Demo 一致。`--help` 显示用法。成功返回 `0`，执行失败返回 `1`，参数数量错误返回 `2`。推理耗时仅包含一次推理调用，不含读图、模型加载和预热。

## 开发包与运行依赖

| 文件或环境 | 用途 |
| --- | --- |
| `main.cpp` | 完整调用示例和 Windows WIC 读图 |
| `dlcv_infer_c_api.h` | 唯一 DLCV 公开头文件，必须与 DLL 配套 |
| `dlcv_infer_binary_demo.vcxproj` | 独立 VS 工程，仅链接 Windows 系统库 |
| `dlcv_infer_cpp.dll` | 已编译的 OpenIVS 推理包装层 |
| `dlcv_infer_binary_demo.exe` | 已编译的命令行程序 |
| 已安装的 DLCV SDK、OpenCV 与 VC++ 运行库 | DLL 内部仍需要的运行环境 |

调用端不需要 OpenCV、JSON 或 Qt 开发文件。**现有 DLL 不是单文件自包含引擎**：`dlcv_infer_cpp.dll` 仍依赖 `opencv_world4100.dll`、VC++ 运行库，以及底层 `dlcv_infer.dll` / `dlcv_infer_v.dll` 和对应模型授权。该包适用于已有 DLCV SDK 的 Windows x64 机器，不包含完整 SDK。需要读码流程时还需该版本使用的 `ZXing.dll`。

Windows 加载器先从指定 DLL 所在目录解析依赖，再使用系统 DLL 搜索路径；可将配套运行库放在该 DLL 旁，或使用已配置好的 SDK 环境。不需要把 `dlcv_infer_cpp.lib` 放入包内。Demo 的 C++ 运行库采用静态链接，DLL 自身的运行库需求仍需满足。

## 调用设计

1. `wmain` 保留 Unicode 文件路径；WIC 读取图片第一帧并生成连续的 8 位 RGB 数据。
2. `LoadLibraryExW` 加载指定 DLL，`GetProcAddress` 解析五个现有 C 导出。
3. `dlcv_infer_cpp_load_model_c` 加载模型；模型路径按现有接口约定转为本地 ANSI，无法表示的字符直接报错。
4. `DlcvCImageList` 传递 RGB 像素地址、宽高和通道数；`dlcv_infer_cpp_infer_with_params_c` 接收 `{"threshold":0.5}`。
5. 遍历 `DlcvCResult` 打印结果。类别名由本地代码页转为 UTF-8，Demo 文本也输出 UTF-8。
6. 作用域退出时依次释放结果、模型和 DLL，异常路径执行同样的释放。DLL 分配的结果始终交还 DLL，调用端不使用 `delete` 释放。

函数指针类型使用公开头文件中的 `decltype(&函数名)`，不另抄一套 ABI 声明；头文件不暴露 `cv::Mat` 或 JSON 类型。官方头文件在仓库中只维护一份，打包时收集到包内。

WIC 与 OpenCV 是不同图片解码器。此 Demo 读取第一帧，不应用 EXIF 方向、不承诺所有 JPEG 的逐像素输出与 OpenCV 完全一致；需要沿用应用自身读图逻辑时，直接将连续 RGB 缓冲区填入 `DlcvCImage` 即可。SDK 自身的调试日志可能使用本地代码页，不属于 Demo 的 UTF-8 输出。

## 独立编译与重新打包

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
$env:DLCV_BINARY_DEMO_MODEL = "D:\models\model.dvst"
$env:DLCV_BINARY_DEMO_IMAGE = "D:\images\image.jpg"
$env:DLCV_BINARY_DEMO_EXPECTED = "D:\test\expected.json"
python -m unittest Test.test_binary_demo -v
```

`expected.json` 是独立确认的结果数组，每项包含 `name`、`score`、`bbox` 和 `area`，数值使用 Demo 打印精度。没有真实输入时对应检查明确跳过，不记为通过。
