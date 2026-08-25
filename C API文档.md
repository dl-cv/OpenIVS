# dlcv_infer_c_dll 与 dlcv_infer C API 对照

## 1. 文档范围

| 项目 | 内容 |
| --- | --- |
| 日期 | 2026-08-25 |
| 平台 | Windows x64，MSVC v143 |
| `dlcv_infer` 头文件 | `dlcv_infer/dlcv_infer.h` |
| `dlcv_infer_c_dll` 头文件 | `dlcv_infer_c_dll/dlcv_infer_c_api.h` |
| 公共结构头 | `dlcv_infer/dlcv_data_type_c.h` |
| 验证模型 | `.dvt`、`.dvst` |

当前 `dlcv_infer` 头文件声明 30 个导出函数。`dlcv_infer_c_dll` 导出 10 个函数，其中 6 个是原有 `dlcv_infer_cpp_*_c` 扩展入口，4 个是与 `dlcv_infer` 同名的结构化兼容入口。

这些函数使用 C 导出名称，但公共结构包含 C++ `bool`，四项同名结构化接口还使用 C++ 引用参数，因此公共头文件需要由 C++ 编译器或具备对应 ABI 的外部语言绑定使用。

结论如下：

- 模型加载、模型释放、结构化推理、结构化结果释放四项接口完整，函数名称、参数、调用方式和结果语义一致。
- 原有 6 个 `dlcv_infer_cpp_*_c` 扩展入口保持不变。
- `dlcv_infer` 的 JSON、设备、GPU、电源、进程和公共 index 接口没有全部复制到 `dlcv_infer_c_dll`，完整情况在下表逐项记录。
- 本次兼容处理没有修改模型路径参数及其现有解析方式。

## 2. dlcv_infer 接口逐项对照

| # | `dlcv_infer` 接口 | 输入 | 输出 | `dlcv_infer_c_dll` 对应接口 | 结论 |
| --- | --- | --- | --- | --- | --- |
| 1 | `dlcv_load_model(const char*)` | JSON：模型路径、设备 ID、预热参数 | JSON 字符串：状态、消息、模型 index | 无 JSON 同名入口 | 未覆盖；结构化加载由第 20 项覆盖 |
| 2 | `dlcv_free_model(const char*)` | JSON：模型 index | JSON 字符串：状态和消息 | 无 JSON 同名入口 | 未覆盖；结构化释放由第 21 项覆盖 |
| 3 | `dlcv_get_model_info(const char*)` | JSON：模型路径或模型 index | JSON 字符串：模型信息 | 无 | 未覆盖 |
| 4 | `dlcv_infer(const char*)` | JSON：模型 index、图像列表和推理参数 | JSON 字符串：结构化推理结果 | 无 JSON 同名入口 | 未覆盖；结构化推理由第 22 项覆盖 |
| 5 | `dlcv_free_model_result(const char*)` | `dlcv_infer` 返回的 JSON 结果地址 | 无 | 无 | 未覆盖 |
| 6 | `dlcv_free_result(const char*)` | DLL 返回的字符串地址 | 无 | 无 | 未覆盖 |
| 7 | `dlcv_free_all_models()` | 无 | 无 | 无 | 未覆盖 |
| 8 | `dlcv_get_device_info()` | 无 | JSON 字符串：设备信息 | 无 | 未覆盖 |
| 9 | `dlcv_get_gpu_info()` | 无 | JSON 字符串：GPU 信息 | 无 | 未覆盖 |
| 10 | `dlcv_keep_max_clock()` | 无 | 无 | 无 | 未覆盖 |
| 11 | `dlcv_reset_max_clock()` | 无 | 无 | 无 | 未覆盖 |
| 12 | `dlcv_set_gpu_max_clock(bool)` | 是否输出详细信息 | 无 | 无 | 未覆盖 |
| 13 | `dlcv_reset_gpu_max_clock(bool)` | 是否输出详细信息 | 无 | 无 | 未覆盖 |
| 14 | `dlcv_get_power_scheme_guid(int)` | 详细信息开关 | 字符串：电源方案 GUID | 无 | 未覆盖 |
| 15 | `dlcv_set_power_scheme_guid(const char*, int)` | GUID、详细信息开关 | `int` 状态 | 无 | 未覆盖 |
| 16 | `dlcv_get_power_scheme(int)` | 详细信息开关 | 字符串：电源方案名称 | 无 | 未覆盖 |
| 17 | `dlcv_set_power_scheme(const char*, int)` | 电源方案名称、详细信息开关 | `int` 状态 | 无 | 未覆盖 |
| 18 | `dlcv_set_current_process_affinity_to_big_cores(int)` | 详细信息开关 | `int` 状态 | 无 | 未覆盖 |
| 19 | `dlcv_set_current_process_priority_highest(int, int, int)` | 实时优先级、详细信息、大核绑定开关 | `int` 状态 | 无 | 未覆盖 |
| 20 | `dlcv_load_model_c(const char*, int)` | 模型路径、设备 ID | 成功返回非负模型 index，失败返回 `-1` | `dlcv_load_model_c(const char*, int)` | 完整，输入输出一致；C DLL 额外支持 `.dvst/.dvso` |
| 21 | `dlcv_free_model_c(int)` | 当前 DLL 返回的模型 index | 成功返回 `0`，未找到返回 `-1` | `dlcv_free_model_c(int)` | 完整，普通调用输入输出一致 |
| 22 | `dlcv_infer_c(int, const DlcvCImageList&)` | 模型 index、图像列表 | `DlcvCResult` | `dlcv_infer_c(int, const DlcvCImageList&)` | 完整，输入输出逐字段一致 |
| 23 | `dlcv_free_model_result_c(DlcvCResult&)` | 当前 DLL 返回的结构化结果 | 无；清空结果内存字段 | `dlcv_free_model_result_c(DlcvCResult&)` | 完整，释放后状态一致 |
| 24 | `dlcv_get_index_type_c(int)` | 模型或流程 index | `int` 类型值 | 无 | 未覆盖 |
| 25 | `dlcv_get_model_info_c(int)` | 模型 index | JSON 字符串：状态和模型信息 | 无 | 未覆盖 |
| 26 | `dlcv_register_flow_c(const char*)` | 流程 JSON | 成功返回流程 index，失败返回负值 | 无 | 未覆盖 |
| 27 | `dlcv_get_flow_info_c(int)` | 流程 index | JSON 字符串：状态和流程信息 | 无 | 未覆盖 |
| 28 | `dlcv_free_flow_c(int)` | 流程 index | `int` 状态 | 无 | 未覆盖 |
| 29 | `dlcv_bind_index_c(int)` | 模型或流程 index | `int` 状态 | 无 | 未覆盖 |
| 30 | `dlcv_unbind_index_c(int)` | 模型或流程 index | `int` 状态 | 无 | 未覆盖 |

## 3. dlcv_infer_c_dll 导出接口逐项对照

| # | `dlcv_infer_c_dll` 接口 | 输入 | 输出 | 与 `dlcv_infer` 的关系 |
| --- | --- | --- | --- | --- |
| 1 | `dlcv_infer_cpp_load_model_c(const char*, int)` | 模型路径、设备 ID | 非负模型 index 或 `-1` | 原有扩展入口，能力对应 `dlcv_load_model_c` |
| 2 | `dlcv_infer_cpp_get_last_error_c()` | 无 | 当前线程最近一次加载错误字符串 | C DLL 扩展，`dlcv_infer` 无独立入口 |
| 3 | `dlcv_infer_cpp_free_model_c(int)` | 模型 index | 成功 `0`，未找到 `-1` | 原有扩展入口，能力对应 `dlcv_free_model_c` |
| 4 | `dlcv_infer_cpp_infer_c(int, const DlcvCImageList*)` | 模型 index、图像列表指针 | `DlcvCResult` | 原有扩展入口，参数形式和部分缺省值与底层不同 |
| 5 | `dlcv_infer_cpp_infer_with_params_c(int, const DlcvCImageList*, const char*)` | 模型 index、图像列表指针、参数 JSON | `DlcvCResult` | C DLL 扩展，支持本次推理参数 |
| 6 | `dlcv_infer_cpp_free_model_result_c(DlcvCResult*)` | 结果指针 | 无；释放并清空结果 | 原有扩展入口，释放后把 `code` 设为 `0` |
| 7 | `dlcv_load_model_c(const char*, int)` | 模型路径、设备 ID | 非负模型 index 或 `-1` | 与底层同名、同参数、同返回语义 |
| 8 | `dlcv_free_model_c(int)` | 模型 index | 成功 `0`，未找到 `-1` | 与底层同名、同参数、同返回语义 |
| 9 | `dlcv_infer_c(int, const DlcvCImageList&)` | 模型 index、图像列表 | `DlcvCResult` | 与底层同名、同参数，结果语义一致 |
| 10 | `dlcv_free_model_result_c(DlcvCResult&)` | 结构化结果 | 无；释放结果并保留原 `code` | 与底层同名、同参数，释放后状态一致 |

## 4. 输入结构对照

两侧当前使用内容相同的 `dlcv_data_type_c.h`。以下大小来自 MSVC v143、Windows x64、默认 packing。

| 结构 | 字段 | 大小 | 一致性 |
| --- | --- | --- | --- |
| `DlcvCImage` | `data_ptr`、`height`、`width`、`channel` | 24 字节 | 一致 |
| `DlcvCImageList` | `images`、`n` | 16 字节 | 一致 |
| `DlcvCMask` | `mask_ptr`、`height`、`width` | 16 字节 | 一致 |
| `DlcvCObjectResult` | 类别、分数、框、mask、角度和均值字段 | 96 字节 | 一致 |
| `DlcvCSampleResult` | `results`、`n` | 16 字节 | 一致 |
| `DlcvCResult` | `code`、`message`、`sample_results`、`n` | 32 字节 | 一致 |

图像数据要求如下：

- `data_ptr` 指向连续、紧密排列的 8 位交错通道图像数据。
- `height`、`width`、`channel` 必须与实际内存一致。
- 接口不接管输入图像内存。
- 结构中没有数据类型和行跨度字段，其他数据类型或带行填充的数据需要先转换。
- 颜色次序按模型预处理要求提供，当前验证使用 RGB 数据。

## 5. 四项同名接口输入输出对照

### 5.1 加载模型

| 检查项 | `dlcv_infer` | `dlcv_infer_c_dll` | 结论 |
| --- | --- | --- | --- |
| 函数签名 | `int dlcv_load_model_c(const char*, int)` | 相同 | 一致 |
| 模型路径 | `const char*` | `const char*` | 一致，现有路径处理未修改 |
| 设备 ID | `int` | `int` | 一致 |
| 成功返回 | 非负模型 index | 非负模型 index | 一致 |
| 失败返回 | `-1` | `-1` | 一致 |
| 模型范围 | `.dvt/.dvo` | `.dvt/.dvo/.dvst/.dvso` | C DLL 增加流程模型能力 |

### 5.2 释放模型

| 检查项 | `dlcv_infer` | `dlcv_infer_c_dll` | 结论 |
| --- | --- | --- | --- |
| 函数签名 | `int dlcv_free_model_c(int)` | 相同 | 一致 |
| 成功返回 | `0` | `0` | 一致 |
| 普通重复释放 | `-1` | `-1` | 一致 |

### 5.3 结构化推理

| 检查项 | `dlcv_infer` | `dlcv_infer_c_dll` | 结论 |
| --- | --- | --- | --- |
| 函数签名 | `DlcvCResult dlcv_infer_c(int, const DlcvCImageList&)` | 相同 | 一致 |
| 成功状态 | `code=0`、`message="Success"` | 相同 | 一致 |
| 模型不存在 | `code=2`、`message="Model not found."` | 相同 | 一致 |
| 失败样本 | `sample_results=nullptr`、`n=0` | 相同 | 一致 |
| 无框 | `area=-1`、`x/y/w/h=-1` | 相同 | 一致 |
| 无 mask | `mask_ptr=0`、`height/width=-1` | 相同 | 一致 |
| 无角度 | `angle=-100` | 相同 | 一致 |
| 其他推理失败 | `code=1` | 相同 | 一致 |
| 有效结果 | 类别、分数、框、mask、角度和均值 | 逐字段相同 | 一致 |

### 5.4 释放结构化结果

| 检查项 | `dlcv_infer` | `dlcv_infer_c_dll` | 结论 |
| --- | --- | --- | --- |
| 函数签名 | `void dlcv_free_model_result_c(DlcvCResult&)` | 相同 | 一致 |
| `message` | 释放后为 `nullptr` | 相同 | 一致 |
| `sample_results` | 释放后为 `nullptr` | 相同 | 一致 |
| `n` | 释放后为 `0` | 相同 | 一致 |
| `code` | 保留释放前数值 | 相同 | 一致 |

结果必须调用生成该结果的 DLL 所提供的释放函数，不能交给另一个 DLL 释放。

## 6. 原有扩展入口说明

| 检查项 | `dlcv_infer_cpp_*_c` 原有入口 | 四项同名兼容入口 |
| --- | --- | --- |
| 成功消息 | `"success"` | `"Success"` |
| 模型不存在 | `code=-1`、`message="model not found"` | `code=2`、`message="Model not found."` |
| 无框缺省值 | 数值为 `0` | `x/y/w/h=-1` |
| 无 mask 缺省值 | 尺寸为 `0` | `height/width=-1` |
| 无角度缺省值 | 保留 C++ 结果值 | `angle=-100` |
| 释放后 `code` | 设为 `0` | 保留释放前数值 |
| 参数 JSON | `dlcv_infer_cpp_infer_with_params_c` 支持 | 同名推理入口不增加参数 |

两类入口同时保留。现有 `dlcv_infer_cpp_*_c` 调用无需修改，新调用可选用四项同名兼容入口。

## 7. 验证结果

| 测试项 | 结果 |
| --- | --- |
| Debug x64 构建 | 通过 |
| Debug x64 C API 测试 | `Test PASSED`，退出码 0 |
| Release x64 构建 | 通过 |
| Release x64 C API 测试 | `Test PASSED`，退出码 0 |
| Release DLL 导出检查 | 原有 6 个与新增 4 个均存在，共 10 个 |
| `.dvt` 成功结果逐字段比较 | 一致 |
| `.dvt` 模型不存在结果比较 | 一致 |
| `.dvt` 结果释放状态比较 | 一致 |
| `.dvst` 同名入口加载、推理和释放 | 通过 |
| `.dvt` 同一实例 4 线程共 40 次 | 一致 |
| `.dvst` 同一实例 4 线程共 40 次 | 一致 |
| 全新 `.dvst` 实例首次 4 线程共 40 次 | 一致 |

## 8. 最终结论

`dlcv_infer_c_dll` 与 `dlcv_infer` 的四项结构化模型接口已经完整对应，输入结构、函数参数、返回码、消息、结果字段和释放后状态一致。原有 6 个扩展入口保持不变。

若检查 `dlcv_infer` 当前全部 30 个导出函数，`dlcv_infer_c_dll` 没有覆盖 JSON、设备、GPU、电源、进程和公共 index 接口，不能将这 30 个函数写成全部对应。第 2 节已经逐项记录每个函数的当前情况。
