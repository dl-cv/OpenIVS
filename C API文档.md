# C API 文档

## 1. 文档范围

本文件记录同一动态库中 C++ 与 C 接口的逐项对应关系：

1. `dlcv_infer_cpp` 提供的 `dlcv_infer::NativeApi` C++ 静态方法。
2. `dlcv_infer_cpp` 对外导出的 C 名称函数。

本任务包含：

- `dlcv_infer` 的 23 个非公共索引接口；
- `dlcv_infer_cpp` 的 11 个 `dlcv_infer_cpp_*_c` 扩展入口；
- 当前导出总数为 34 个。

以下 7 个公共索引接口归入“双语言 model index 互通”任务，本文件不把它们写成当前任务的缺失接口：

| 接口 |
| --- |
| `dlcv_get_index_type_c` |
| `dlcv_get_model_info_c` |
| `dlcv_register_flow_c` |
| `dlcv_get_flow_info_c` |
| `dlcv_free_flow_c` |
| `dlcv_bind_index_c` |
| `dlcv_unbind_index_c` |

全部 C 接口声明在 `dlcv_infer_c_api.h`。共享数据结构使用 `typedef struct`，C 模式下由该头文件引入 `<stdbool.h>`，结构化接口使用指针参数，头文件可由 C 或 C++ 编译器使用。

## 2. 两层封装关系

| 层级 | 所在工程 | 职责 |
| --- | --- | --- |
| 底层接口 | `dlcv_infer` | 提供 JSON、设备控制、系统设置和结构化 C 接口 |
| C++ 封装 | `dlcv_infer_cpp` | 通过 `NativeApi` 解析并调用底层接口，统一 DLL 选择和返回处理 |
| C 导出 | `dlcv_infer_cpp` | 将 19 个 JSON、设备和系统控制方法导出为 C 名称函数，并保留 4 个结构化兼容入口 |
| 扩展接口 | `dlcv_infer_cpp` | 提供基于 C++ `Model` 的 11 个 `dlcv_infer_cpp_*_c` 入口 |

23 个非公共索引接口均已增加 `NativeApi` 方法。JSON 模型接口对 `.dvt/.dvo` 请求继续直接转发 `NativeApi`；对 `.dvst/.dvso/.dvsp` 请求使用 `Model` 和同一模型表完成加载、推理、查询和释放。设备与系统接口继续直接转发。第 20～23 项在同一工程中提供结构化 C 接口实现，11 个扩展入口复用同一模型表。

## 3. 23 个非公共索引接口逐项对照

### 3.1 JSON 模型与结果接口

| # | 底层 `dlcv_infer` 接口 | C++ 封装方法 | C 导出 | 输入 | 输出 | 释放方式 | 一致性 |
| ---: | --- | --- | --- | --- | --- | --- | --- |
| 1 | `const char* dlcv_load_model(const char* config_str)` | `NativeApi::LoadModel(const char*)` | `dlcv_load_model(const char*)` | JSON 字符串：`model_path`，可选 `device_id`、`type`、`warm_up` | 新分配的 JSON 字符串，包含 `code`、`message` 和模型索引 | 用 `dlcv_free_result` 释放返回字符串 | `.dvt/.dvo` 原样转发；`.dvst/.dvso/.dvsp` 使用 `Model` 加载并返回同格式状态 |
| 2 | `const char* dlcv_free_model(const char* config_str)` | `NativeApi::FreeModel(const char*)` | `dlcv_free_model(const char*)` | JSON 字符串：`model_index` | 新分配的状态 JSON 字符串 | 用 `dlcv_free_result` 释放返回字符串 | 普通模型转发到底层；流程模型从统一模型表释放，结果码和消息格式一致 |
| 3 | `const char* dlcv_get_model_info(const char* config_str)` | `NativeApi::GetModelInfo(const char*)` | `dlcv_get_model_info(const char*)` | JSON 字符串：`model_index` 或 `model_path`，流程路径可选 `device_id` | 新分配的模型信息 JSON 字符串 | 用 `dlcv_free_result` 释放返回字符串 | 普通模型直接转发；流程模型支持索引和路径两种查询方式 |
| 4 | `const char* dlcv_infer(const char* config_str)` | `NativeApi::Infer(const char*)` | `dlcv_infer(const char*)` | JSON 字符串：`model_index`、`image_list` 和推理参数；图像 `dtype` 支持 `uint8/uint16/float32` | 新分配的推理结果 JSON 字符串 | 只调用 `dlcv_free_model_result`；该函数同时释放 mask 和外层字符串 | 普通模型原样转发；流程模型返回 `sample_results/results/mask_ptr` 格式并支持同索引并发调用 |
| 5 | `void dlcv_free_model_result(const char* config_str)` | `NativeApi::FreeModelResult(const char*)` | `dlcv_free_model_result(const char*)` | `dlcv_infer` 返回的结果字符串 | 无 | 普通模型结果交给底层释放；流程模型结果释放登记的 mask 和外层字符串 | 两类结果按分配来源释放 |
| 6 | `void dlcv_free_result(const char* config_str)` | `NativeApi::FreeResult(const char*)` | `dlcv_free_result(const char*)` | 由接口返回的字符串地址 | 无 | 释放外层字符串 | 只释放字符串，不处理推理结果内部资源 |
| 7 | `void dlcv_free_all_models()` | `NativeApi::FreeAllModels()` | `dlcv_free_all_models()` | 无 | 无 | 无返回内存；清理统一模型表和底层模型表 | 活动调用结束后释放全部普通模型与流程模型 |

JSON 接口返回的字符串由产生它的 DLL 分配，必须使用同一 DLL 提供的释放函数。推理结果字符串不能直接交给只释放外层字符串的 `dlcv_free_result`。

### 3.2 设备与系统控制接口

| # | 底层 `dlcv_infer` 接口 | C++ 封装方法 | C 导出 | 输入 | 输出 | 释放方式 | 一致性 |
| ---: | --- | --- | --- | --- | --- | --- | --- |
| 8 | `const char* dlcv_get_device_info()` | `NativeApi::GetDeviceInfo()` | `dlcv_get_device_info()` | 无 | 新分配的设备信息 JSON 字符串 | 用 `dlcv_free_result` 释放 | 返回结构和底层设备查询保持一致 |
| 9 | `const char* dlcv_get_gpu_info()` | `NativeApi::GetGpuInfo()` | `dlcv_get_gpu_info()` | 无 | 新分配的 GPU 信息 JSON 字符串 | 用 `dlcv_free_result` 释放 | 返回结构和底层 GPU 查询保持一致 |
| 10 | `void dlcv_keep_max_clock()` | `NativeApi::KeepMaxClock()` | `dlcv_keep_max_clock()` | 无 | 无 | 无 | 调用行为保持一致 |
| 11 | `void dlcv_reset_max_clock()` | `NativeApi::ResetMaxClock()` | `dlcv_reset_max_clock()` | 无 | 无 | 无 | 调用行为保持一致 |
| 12 | `void dlcv_set_gpu_max_clock(bool verbose)` | `NativeApi::SetGpuMaxClock(bool)` | `dlcv_set_gpu_max_clock(bool)` | `verbose`：是否输出详细信息 | 无 | 无 | 参数和执行行为保持一致 |
| 13 | `void dlcv_reset_gpu_max_clock(bool verbose)` | `NativeApi::ResetGpuMaxClock(bool)` | `dlcv_reset_gpu_max_clock(bool)` | `verbose`：是否输出详细信息 | 无 | 无 | 参数和执行行为保持一致 |
| 14 | `const char* dlcv_get_power_scheme_guid(int verbose)` | `NativeApi::GetPowerSchemeGuid(int)` | `dlcv_get_power_scheme_guid(int)` | `verbose`：是否输出详细信息 | 新分配的电源方案 GUID 字符串 | 用 `dlcv_free_result` 释放 | 参数、字符串编码和返回内容保持一致 |
| 15 | `int dlcv_set_power_scheme_guid(const char* scheme_guid, int verbose)` | `NativeApi::SetPowerSchemeGuid(const char*, int)` | `dlcv_set_power_scheme_guid(const char*, int)` | GUID 字符串、`verbose` | `int` 状态码 | 无 | 参数和状态码保持一致 |
| 16 | `const char* dlcv_get_power_scheme(int verbose)` | `NativeApi::GetPowerScheme(int)` | `dlcv_get_power_scheme(int)` | `verbose`：是否输出详细信息 | 新分配的电源方案名称字符串 | 用 `dlcv_free_result` 释放 | 参数、字符串编码和返回内容保持一致 |
| 17 | `int dlcv_set_power_scheme(const char* scheme_name, int verbose)` | `NativeApi::SetPowerScheme(const char*, int)` | `dlcv_set_power_scheme(const char*, int)` | 电源方案名称、`verbose` | `int` 状态码 | 无 | 参数和状态码保持一致 |
| 18 | `int dlcv_set_current_process_affinity_to_big_cores(int verbose)` | `NativeApi::SetCurrentProcessAffinityToBigCores(int)` | `dlcv_set_current_process_affinity_to_big_cores(int)` | `verbose`：是否输出详细信息 | `int` 状态码 | 无 | 参数和状态码保持一致 |
| 19 | `int dlcv_set_current_process_priority_highest(int prefer_realtime, int verbose, int bind_big_cores)` | `NativeApi::SetCurrentProcessPriorityHighest(int, int, int)` | `dlcv_set_current_process_priority_highest(int, int, int)` | 实时优先级开关、详细信息开关、大核绑定开关 | `int` 状态码 | 无 | 参数顺序、行为和状态码保持一致 |

### 3.3 结构化 C 接口

| # | 底层 `dlcv_infer` 接口 | C++ 封装方法 | C 导出 | 输入 | 输出 | 释放方式 | 一致性 |
| ---: | --- | --- | --- | --- | --- | --- | --- |
| 20 | `int dlcv_load_model_c(const char* model_path, int device_id)` | `NativeApi::LoadModelC(const char*, int)` | `dlcv_load_model_c(const char*, int)` | 当前路径规则支持的模型路径、设备编号 | 成功返回非负模型索引，失败返回 `-1` | 无返回字符串 | C++ 方法严格调用底层；同一工程中的 C 接口使用 `Model` 加载并支持 `.dvst/.dvso` |
| 21 | `int dlcv_free_model_c(int model_index)` | `NativeApi::FreeModelC(int)` | `dlcv_free_model_c(int)` | 模型索引 | 成功返回 `0`，失败返回负值 | 无 | C++ 方法严格调用底层；C 接口释放同一工程模型表中的对象 |
| 22 | `DlcvCResult dlcv_infer_c(int, const DlcvCImageList*)` | `NativeApi::InferC(int, const DlcvCImageList&)` | `dlcv_infer_c(int, const DlcvCImageList*)` | 模型索引、图像列表指针；输入图像内存由调用方持有 | `DlcvCResult`，包含状态、消息、样本结果、目标、框、mask、角度和均值 | 用 `dlcv_free_model_result_c` 释放返回结构中的字符串、数组和 mask | C++ 方法通过地址调用底层；同一工程中的 C 接口通过 `Model::InferBatch` 生成结果并转换为底层结果语义 |
| 23 | `void dlcv_free_model_result_c(DlcvCResult*)` | `NativeApi::FreeModelResultC(DlcvCResult&)` | `dlcv_free_model_result_c(DlcvCResult*)` | 当前 DLL 返回的 `DlcvCResult` 地址 | 无；释放后指针字段为空、数量为 `0`，兼容入口保留原 `code` | 只能使用生成结果的同一 DLL 释放 | C++ 方法通过地址释放底层结果；C 接口释放同一工程生成的结果，释放后字段一致 |

## 4. 结构化 C 数据类型

这些结构已直接定义在统一公开头文件 `dlcv_infer_c_api.h` 中，调用端不再需要额外包含 `dlcv_data_type_c.h`：

| 结构 | 字段 | 内存所有权 |
| --- | --- | --- |
| `DlcvCImage` | `data_ptr`、`height`、`width`、`channel` | 输入图像由调用方持有，接口不释放 |
| `DlcvCImageList` | `images`、`n` | 输入数组由调用方持有，接口不释放 |
| `DlcvCMask` | `mask_ptr`、`height`、`width` | 推理结果中的 mask 由结果释放函数释放 |
| `DlcvCObjectResult` | `category_id`、`category_name`、`score`、框、mask、角度和均值字段 | 结果字段由结果释放函数释放 |
| `DlcvCSampleResult` | `results`、`n` | 结果数组由结果释放函数释放 |
| `DlcvCResult` | `code`、`message`、`sample_results`、`n` | 结果消息、样本数组及其嵌套数据由结果释放函数释放 |

共享头文件在 C 和 C++ 中使用相同字段顺序。`Test/dlcv_infer_c_test/pure_c_header_test.c` 强制按 C 编译，只包含公开头，并通过 `LoadLibraryW/GetProcAddress` 调用统一 DLL，同时检查 Windows x64 下六个结构的大小。

输入图像要求：

- `data_ptr` 指向连续图像内存；
- `height`、`width`、`channel` 与实际内存一致；
- 当前结构没有数据类型和行跨度字段，调用方需先完成必要转换；
- 调用方不能在推理完成前释放或修改输入图像内存。

## 5. 11 个扩展入口

这些函数不经过 `NativeApi`，使用 `dlcv_infer_cpp` 工程中的 C++ `Model` 对象管理模型。前 6 个入口用于结构化 C 推理，后 5 个入口为 Qt C 测试程序提供模型信息、JSON 推理、授权设备查询、字符串释放和全部模型释放能力。

| # | 扩展入口 | C++ 封装方法 | 输入 | 输出 | 释放方式 | 与 23 个接口的关系 |
| ---: | --- | --- | --- | --- | --- | --- |
| 1 | `dlcv_infer_cpp_load_model_c(const char*, int)` | 无；直接创建 `Model` | 模型路径、设备编号 | 成功返回模型索引，失败返回 `-1` | 无返回字符串 | 独立扩展；支持 C++ DLL 当前支持的普通模型和流程模型 |
| 2 | `dlcv_infer_cpp_get_last_error_c()` | 无；读取当前线程错误 | 无 | 当前线程最近一次加载错误字符串 | 不由调用方释放；指针由当前线程错误存储维护 | 独立扩展 |
| 3 | `dlcv_infer_cpp_free_model_c(int)` | 无；释放扩展入口保存的 `Model` | 模型索引 | 成功返回 `0`，未找到返回 `-1` | 无 | 独立扩展 |
| 4 | `dlcv_infer_cpp_infer_c(int, const DlcvCImageList*)` | 无；调用保存的 `Model::InferBatch` | 模型索引、图像列表指针 | `DlcvCResult` | 用 `dlcv_infer_cpp_free_model_result_c` 释放 | 独立扩展；缺省推理参数 |
| 5 | `dlcv_infer_cpp_infer_with_params_c(int, const DlcvCImageList*, const char*)` | 无；调用保存的 `Model::InferBatch` | 模型索引、图像列表指针、参数 JSON | `DlcvCResult` | 用 `dlcv_infer_cpp_free_model_result_c` 释放 | 独立扩展；支持本次推理参数 |
| 6 | `dlcv_infer_cpp_free_model_result_c(DlcvCResult*)` | 无；释放扩展入口生成的结构化结果 | 结果指针 | 无；释放后指针字段为空、数量为 `0`，`code` 置为 `0` | 建议用于扩展入口生成的结果 | 与兼容释放函数的内存处理相同，释放后的 `code` 行为不同 |
| 7 | `dlcv_infer_cpp_get_model_info_c(int)` | 无；调用保存的 `Model::GetModelInfo` | 模型索引 | UTF-8 JSON 字符串；失败返回空指针 | 用 `dlcv_infer_cpp_free_string_c` 释放 | 支持普通模型和流程模型 |
| 8 | `dlcv_infer_cpp_infer_json_c(int, const DlcvCImage*, const char*)` | 无；调用保存的 `Model::InferOneOutJson` | 模型索引、单张连续图像、参数 JSON | UTF-8 JSON 字符串；失败返回空指针 | 用 `dlcv_infer_cpp_free_string_c` 释放 | 与结构化扩展入口共用模型表和流程模型并发保护 |
| 9 | `dlcv_infer_cpp_get_all_dog_info_c()` | 无；调用 `GetAllDogInfo` | 无 | Sentinel 与 Virbox 信息 JSON 字符串 | 用 `dlcv_infer_cpp_free_string_c` 释放 | 供 C 调用端查询授权设备 |
| 10 | `dlcv_infer_cpp_free_string_c(const char*)` | 无；释放扩展接口字符串 | 扩展接口返回的字符串 | 无 | 仅用于第 7～9 项返回值 | 字符串由 `dlcv_infer_cpp.dll` 分配和释放 |
| 11 | `dlcv_infer_cpp_free_all_models_c()` | 无；清空扩展模型表并调用全部模型释放接口 | 无 | 无 | 无 | 释放普通模型和流程模型实例 |

前 6 个扩展入口和四项结构化兼容入口都由 `dlcv_infer_cpp.dll` 分配结果内存，内部字段使用同一分配方式。为保持释放后的 `code` 行为，调用方应按入口名称配套使用结果释放函数。`dlcv_infer.dll` 直接生成的结构化结果使用另一套分配方式，不能交给 `dlcv_infer_cpp.dll` 释放。

## 6. 导出清单

`dlcv_infer_cpp.dll` 的导出分组如下：

| 分组 | 数量 | 接口范围 |
| --- | ---: | --- |
| NativeApi 转出的 JSON、设备和系统控制接口 | 19 | 第 1～19 项 |
| 结构化兼容接口 | 4 | 第 20～23 项；C++ 层同时提供对应 `NativeApi` 方法 |
| C++ Model 扩展入口 | 11 | 第 5 节 |
| 合计 | 34 | 已通过 Debug 导出检查，不含公共索引接口 |

公共索引接口另行记录在“双语言 model index 互通”任务文档中。

### 6.1 动态调用方式

C 调用端只需包含 `dlcv_infer_c_api.h`，不需要链接 `dlcv_infer_cpp.lib`。Windows 调用端可按以下顺序加载和解析接口：

1. 使用 `LoadLibraryW(L"dlcv_infer_cpp.dll")` 加载统一动态库。
2. 使用 `GetProcAddress` 获取所需 C 名称函数地址。
3. 调用模型加载、模型信息、推理和释放函数。
4. 使用同一动态库导出的释放函数释放字符串和结构化结果。
5. 进程结束前调用 `dlcv_infer_cpp_free_all_models_c`，再调用 `FreeLibrary`。

`dlcv_infer_cpp.dll` 内部按模型授权类型加载 `dlcv_infer.dll` 或 `dlcv_infer_v.dll`。调用端仍需准备 DLCV SDK、OpenCV、Visual C++ 运行库和对应授权组件；动态加载只取消了对 C 导入库的静态链接，不会取消这些运行依赖。

### 6.2 单头文件交付

`dlcvpro_infer` wheel `2026.8.26.1a0` 包含：

- `dlcv_infer_cpp.dll`
- `include/dlcv_infer_c_api.h`

调用端复制或安装这两个交付文件即可使用 C 接口声明和统一动态库；OpenCV、Visual C++ 运行库、底层推理 DLL 及模型授权环境仍按既有 SDK 环境提供。

## 7. 验证范围

| 检查项 | 结果 |
| --- | --- |
| Windows x64 Debug 构建与测试 | 通过 |
| Windows x64 Release 构建与测试 | 通过，完整测试输出 `Test PASSED`，退出码 0 |
| C API 导出检查 | Debug 下 `dlcv_infer_cpp.dll` 的 34 个函数均存在，包含 `dlcv_infer` 和 5 个 Qt Demo 扩展函数 |
| JSON 接口安全失败输入比较 | 两层返回字符串一致 |
| 设备、GPU、电源方案读取 | 返回有效字符串并由所属 DLL 释放 |
| `.dvt` 结构化结果比较 | 输入相同，结果逐字段一致 |
| `.dvst` 结构化兼容入口 | 加载、推理和释放通过 |
| `.dvt` 原生 JSON | 有效模型加载、索引信息、`dtype=uint8` 推理和释放通过；关闭 mask 后与底层结果逐字节一致 |
| `.dvst` 原生 JSON | 索引与路径信息、推理、mask 字段、释放以及同索引 4 线程信息/推理并发通过 |
| `.dvt/.dvst` 并发 | 三组 4 线程各 40 次检查通过 |
| `dlcv_infer_c_qt_demo` | Debug x64 构建和启动通过，窗口标题及控件完整；新增字符串与设备查询接口调用通过 |
| `dlcv_infer_c_demo` | Debug / Release x64 构建通过；普通模型与流程模型的加载、模型信息、推理、释放和多线程结果比较通过 |
| `dlcv_infer_c_qt_demo --check-c-api-exports` | 34 个 C 导出函数均可由 `LoadLibraryW` 和 `GetProcAddress` 解析，退出码为 0 |

当前构建工程只验证 Windows x64，不将 Win32 写为已验证能力。GPU 时钟设置、电源方案设置、进程亲和性和优先级接口会改变系统状态，测试只检查导出存在和参数转发代码，不执行设置操作。
