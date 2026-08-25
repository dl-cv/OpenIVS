# C 测试程序开发文档

## 1. 项目概述

| 项目 | 内容 |
| --- | --- |
| 工程名称 | `dlcv_infer_c_qt_demo` |
| 工程路径 | `dlcv_infer_c_qt_demo/` |
| 窗口标题 | `C测试程序` |
| 类型 | Qt Widgets x64 桌面应用 |
| 用途 | 使用 `dlcv_infer_c_dll` 的 C ABI 验证模型加载、结构化推理、JSON 推理、模型信息、设备信息、多线程调用和结果释放 |

程序界面与 `dlcv_infer_cpp_qt_demo` 保持一致。Qt 界面源码使用 C++17，推理相关代码不包含 `dlcv_infer.h`，不使用 `dlcv_infer::Model` 或其他 C++ API 类型。

核心文件：

- `main.cpp`：初始化 Qt 应用并显示主窗口。
- `MainWindow.cpp` / `MainWindow.h`：窗口控件、C API 调用和多线程测试。
- `DisplayResult.h`：图像显示使用的程序内部结果类型。
- `ImageViewerWidget.cpp` / `ImageViewerWidget.h`：图像、框、旋转框和 mask 显示。
- `dlcv_infer_c_qt_demo.vcxproj`：Debug / Release x64 工程配置。

## 2. 依赖与调用方式

| 依赖 | 用途 |
| --- | --- |
| Qt 6 | 窗口、控件、文件选择和定时刷新 |
| OpenCV 4.10 | 图片读取、BGR/BGRA 转 RGB、mask 复制 |
| `dlcv_infer_c_api.h` | 结构化推理和扩展 C 接口 |
| `dlcv_infer_native_c_api.h` | GPU 信息和时钟接口 |
| `dlcv_infer_c_dll.lib` | 程序唯一直接链接的 DLCV 导入库 |
| `dlcv_infer_c_dll.dll` | C API 运行库 |
| `dlcv_infer_cpp_dll.dll` | C DLL 的运行依赖，由构建后事件复制 |

主要功能映射：

| 界面功能 | C 接口 |
| --- | --- |
| 加载模型 | `dlcv_infer_cpp_load_model_c` |
| 获取加载错误 | `dlcv_infer_cpp_get_last_error_c` |
| 获取模型信息 | `dlcv_infer_cpp_get_model_info_c` |
| 结构化推理 | `dlcv_infer_cpp_infer_with_params_c` |
| JSON 推理 | `dlcv_infer_cpp_infer_json_c` |
| 释放结构化结果 | `dlcv_infer_cpp_free_model_result_c` |
| 释放 JSON 字符串 | `dlcv_infer_cpp_free_string_c` |
| 释放当前模型 | `dlcv_infer_cpp_free_model_c` |
| 释放全部模型 | `dlcv_infer_cpp_free_all_models_c` |
| GPU 信息 | `dlcv_get_gpu_info`，返回值使用 `dlcv_free_result` 释放 |
| 授权设备信息 | `dlcv_infer_cpp_get_all_dog_info_c` |

## 3. 界面与交互

主窗口上方包含三行控件，下方使用水平分隔区显示文本结果和图像结果。

按钮：

| 按钮 | 行为 |
| --- | --- |
| 加载模型 | 选择普通模型或流程模型，加载成功后显示模型信息 |
| 打开图片推理 | 选择图片后立即执行结构化推理 |
| 单次推理 | 使用当前图片、批量大小、阈值和均值参数执行推理 |
| 推理JSON | 调用 JSON C 接口并格式化显示返回内容 |
| 多线程测试 | 按线程数持续调用结构化 C 接口，每 500ms 更新请求数、平均延迟和实时速率 |
| 释放模型 | 释放当前模型索引 |
| 释放所有模型 | 清空 C DLL 保存的全部普通模型和流程模型 |
| 文档 | 打开 SDK 文档地址 |
| 检查加密狗 | 显示 Sentinel 与 Virbox 的设备和特性信息 |
| 获取模型信息 | 显示当前模型信息 JSON |

参数控件：

| 控件 | 范围 | 默认值 |
| --- | --- | --- |
| 选择显卡 | CPU 和检测到的 GPU | 首个 GPU；没有 GPU 时为 CPU |
| `batch_size` | 1～1024 | 1 |
| `threshold` | 0.00～1.00 | 0.50 |
| 计算均值 | 开启或关闭 | 关闭 |
| 线程数 | 1～32 | 1 |

图片由 OpenCV 按原始位深和通道读取。三通道 BGR 与四通道 BGRA 在调用 C 接口前转换为连续 RGB 数据；单通道图片保持单通道。输入内存在接口返回前保持有效。

结构化结果在释放前复制为显示数据。类别、分数、普通框、旋转框、mask、面积、角度和均值字段均可显示。mask 在调用结果释放函数前复制到程序内部的 `cv::Mat`。

## 4. 构建与输出

构建命令统一通过项目脚本执行：

```text
python ".cursor/skills/vs-build/scripts/build.py" "dlcv_infer_c_qt_demo/dlcv_infer_c_qt_demo.vcxproj" --configuration Debug --platform x64 --target Build --verbosity minimal
```

直接构建项目时，Debug 可执行文件位于：

```text
dlcv_infer_c_qt_demo/Debug/dlcv_infer_c_qt_demo/dlcv_infer_c_qt_demo.exe
```

工程通过 `ProjectReference` 构建 `dlcv_infer_c_dll`。构建后事件复制 C DLL、其 C++ 运行依赖、Qt Core/Gui/Widgets 运行库以及平台和样式插件。

## 5. 已验证能力

| 检查项 | 结果 |
| --- | --- |
| Debug x64 构建 | 通过 |
| 程序启动 | 通过 |
| 窗口标题 | `C测试程序` |
| 控件读取 | 19 个控件完整，顺序与 C++ Qt Demo 一致 |
| C DLL 导出 | 34 个函数存在 |
| GPU 查询 | 返回 `code=0` 和 1 个 GPU |
| 授权设备查询 | 返回 `sentinel` 与 `virbox` |
| C API 控制台测试 | JSON、结构化结果、普通模型、流程模型和三组并发检查通过 |

