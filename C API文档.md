# C API 文档

## 1. 文档范围

本文记录 `dlcv_infer_cpp.dll` 当前公开的 C ABI。公开头文件为：

```text
dlcv_infer_cpp/dlcv_infer_c_api.h
```

该头文件可由 C 或 C++ 编译器直接包含，包含六个结构体定义、扩展 C 接口、结构化 C 接口和原生 JSON 转发接口。

## 2. 扩展 C 接口

| 函数 | 返回值 | 说明 |
| --- | --- | --- |
| `dlcv_infer_cpp_load_model_c` | 非负 index / `-1` | 加载普通模型或 `.dvst/.dvso` DVS |
| `dlcv_infer_cpp_get_last_error_c` | UTF-8 借用字符串 | 返回当前线程最近一次扩展接口错误 |
| `dlcv_infer_cpp_free_model_c` | `0` | 释放一次 C 包装持有；没有本地持有时返回成功 |
| `dlcv_infer_cpp_infer_c` | `DlcvCResult` | 使用默认参数执行结构化推理 |
| `dlcv_infer_cpp_infer_with_params_c` | `DlcvCResult` | 使用 JSON 参数执行结构化推理 |
| `dlcv_infer_cpp_free_model_result_c` | 无 | 释放 `DlcvCResult` 内部内存 |
| `dlcv_infer_cpp_get_model_info_c` | UTF-8 JSON / `nullptr` | 返回普通模型兼容信息 |
| `dlcv_infer_cpp_infer_json_c` | UTF-8 JSON / `nullptr` | 返回单图 JSON 结果 |
| `dlcv_infer_cpp_get_all_dog_info_c` | UTF-8 JSON / `nullptr` | 返回 Sentinel 与 Virbox 信息 |
| `dlcv_infer_cpp_get_all_models_c` | UTF-8 JSON / `nullptr` | 返回当前进程已加载推理模块的资源快照 |
| `dlcv_infer_cpp_free_string_c` | 无 | 释放扩展接口分配的字符串 |
| `dlcv_infer_cpp_free_all_models_c` | 无 | 清理本地模型表、流程模型池及全部已加载推理模块 |

每次成功加载或建立共享持有都应配对一次释放；相同索引可能对应多次持有，接口不识别调用者身份，不应把同一持有释放多次。

`dlcv_infer_cpp_get_last_error_c()` 返回线程局部借用指针，不调用 `dlcv_infer_cpp_free_string_c()`。其余 `dlcv_infer_cpp_*` 字符串返回值由调用方使用 `dlcv_infer_cpp_free_string_c()` 释放。

## 3. 结构化数据

公开结构体：

- `DlcvCImage`
- `DlcvCImageList`
- `DlcvCMask`
- `DlcvCObjectResult`
- `DlcvCSampleResult`
- `DlcvCResult`

`DlcvCResult` 及其内部 `message`、`sample_results`、`results`、`category_name`、mask 数据均由 DLL 分配。调用完成后必须执行：

```c
DlcvCResult result = dlcv_infer_cpp_infer_c(model_index, &images);
dlcv_infer_cpp_free_model_result_c(&result);
```

释放函数可接收空指针；同一个已经清空的 `DlcvCResult` 可再次传入。

## 4. 模型与 DVS index

- 普通模型和 DVS 均使用 `int` 非负 index。
- `-1` 只表示加载失败；其他负数同样是无效参数。
- 不按数值区段、`bit8`、模型文件头或资源类型推导所属模块。
- 共享恢复只查询当前进程实际已加载的 `dlcv_infer.dll` 与 `dlcv_infer_v.dll`，不为查询加载其他模块。
- index 在一个模块中命中时固定使用该模块；无结果、多个模块命中、查询错误、缺少当前接口或绑定失败均返回错误，不切换其他模块。
- 首次加载空 DVS 可按已有默认模块选择方式加载必要的底层 DLL。该行为不用于共享 index 查询或模型列表查询。

底层共享功能只使用以下五个接口：

```c
const char* dlcv_register_dvs_model(const char* config_json);
const char* dlcv_get_dvs_model(const char* config_json);
const char* dlcv_get_index_type(const char* config_json);
const char* dlcv_bind_index(const char* config_json);
const char* dlcv_get_all_models();
```

前四项返回 UTF-8 JSON，结果统一使用 `dlcv_free_result` 释放。登记输入为完整 DVS 描述；其他三项的输入严格为 `{"model_index":整数}`，不得增加其他字段。成功结果均含 `code=0` 和 `message`：登记返回 `model_index/resource_type=dvs`，类型查询返回 `model_index/resource_type=model|dvs`，绑定返回 `model_index/resource_type`。输入错误返回 `code=1`，合法但不存在返回 `code=2`，内部错误返回 `code=3`。

加载、DVS 登记和共享绑定成功各增加一次持有。共享持有只使用无后缀 `dlcv_free_model` 归还，最后一次释放销毁资源。底层 `_c` 接口只用于普通模型加载、推理、释放及结果释放，不提供 DVS 共享接口，也不保留旧名称兼容入口。

DVS 登记 JSON 必须包含：

```json
{
  "schema_version": 1,
  "dvs_type": "dvst",
  "model_path": "",
  "device_id": 0,
  "pipeline": {},
  "model_bindings": [
    {"node_id": 1, "model_index": 0}
  ]
}
```

`dvs_type` 只接受 `dvst` 或 `dvso`；`device_id` 接受 `-1` 到 `INT_MAX`；子模型 index 必须是非负整数。登记数据不包含 `provider` 或外部分配的 `model_index`。

登记成功结果：

```json
{"code":0,"message":"success","model_index":12,"resource_type":"dvs"}
```

类型查询和绑定请求均为：

```json
{"model_index":12}
```

类型查询成功结果中的 `resource_type` 为 `model` 或 `dvs`；绑定成功结果返回实际资源类型。合法编号不存在时返回 `{"code":2,"message":"..."}`，不返回整数类型标量。`dlcv_get_dvs_model` 使用同一请求结构，成功结果在完整登记描述上增加状态和资源字段。

## 5. 模型信息

`dlcv_infer_cpp_get_model_info_c()` 返回普通兼容模型信息：

- 普通模型信息补充当前普通模型的 `model_index`。
- DVS 信息从子模型生成；输入信息取首个子模型，任务类型和类别信息取最终输出可达子模型。
- DVS 兼容信息中的 `model_index` 保持子模型编号，不替换为 DVS 编号。
- 空 DVS 没有子模型编号，不使用 DVS 编号补充兼容信息。

C ABI 的模型信息入口为 `dlcv_infer_cpp_get_model_info_c()`；完整 DVS 描述由 C++ `Model::GetDvsModelInfo()` 提供。

## 6. 推理与结果释放

结构化入口：

```c
DlcvCResult dlcv_infer_cpp_infer_c(
    int model_index,
    const DlcvCImageList* image_list);

DlcvCResult dlcv_infer_cpp_infer_with_params_c(
    int model_index,
    const DlcvCImageList* image_list,
    const char* params_json);
```

`params_json` 为 UTF-8 JSON 对象，可包含 `threshold`、`with_mask`、`calc_mean`、`batch_size` 等现有字段。图像数据在调用期间必须保持有效。

JSON 入口：

```c
const char* dlcv_infer_cpp_infer_json_c(
    int model_index,
    const DlcvCImage* image,
    const char* params_json);
```

返回字符串使用 `dlcv_infer_cpp_free_string_c()` 释放。

## 7. 模型列表

当前签名为：

```c
const char* dlcv_infer_cpp_get_all_models_c();
void dlcv_infer_cpp_free_string_c(const char* value);
```

`dlcv_infer_cpp_get_all_models_c()` 返回：

```json
{
  "code": 0,
  "message": "success",
  "modules": [
    {
      "code": 0,
      "message": "success",
      "provider": "sentinel",
      "module_path": "C:/.../dlcv_infer.dll",
      "models": [
        {
          "model_index": 0,
          "resource_type": "model",
          "model_paths": ["C:/models/a.dvt"],
          "device_id": 0
        }
      ]
    }
  ]
}
```

行为：

- 只枚举当前进程已经加载的目标模块，不主动加载 DLL。
- 每个模块保留底层完整快照，并增加实际 `module_path`。
- 不合并不同模块中的同编号资源。
- 模块返回错误或快照结构无效时，外层 `code` 为非零，并保留此前已收集的模块结果。
- 不用空数组把成功快照中的缺失字段改写为成功状态。

## 8. 原生 JSON 转发接口

普通模型的 `dlcv_get_model_info` 保留底层返回的原始 JSON 字节，不增加 `model_index` 或重新序列化。扩展 C 信息接口 `dlcv_infer_cpp_get_model_info_c` 使用 C++ `Model::GetModelInfo()`，其普通模型信息包含当前 `model_index`；两类接口的返回格式分别保持各自语义。

头文件继续公开 `dlcv_load_model`、`dlcv_free_model`、`dlcv_get_model_info`、`dlcv_infer`、`dlcv_free_model_result`、`dlcv_free_result`、`dlcv_free_all_models` 以及设备和系统控制接口。

字符串释放方式：

- `dlcv_load_model`、`dlcv_free_model`、`dlcv_get_model_info`、设备信息及模型列表底层结果使用 `dlcv_free_result`。
- `dlcv_infer` 返回值使用 `dlcv_free_model_result`。
- 结构化 `DlcvCResult` 使用 `dlcv_free_model_result_c` 或扩展层对应释放函数。
- 不混用上述释放函数。

## 9. 全量释放

以下入口最终执行相同的多模块清理：

```c
dlcv_infer_cpp_free_all_models_c();
dlcv_free_all_models();
```

清理顺序包括：

1. 清空扩展 C 模型表。
2. 清空流程模型池。
3. 枚举当前进程已加载的目标模块。
4. 分别调用各模块的 `dlcv_free_all_models`。

全量释放不重置底层编号计数器；后续重新加载不复用已经发放的旧 index。

## 10. 动态调用 Demo

`dlcv_infer_c_demo` 通过 `LoadLibraryW` 和 `GetProcAddress` 调用扩展 C 接口，支持：

```text
load-model
list-models
list-sdk-models
model-info
infer
benchmark
free-model
free-all-models
```

`list-models` 显示 Demo 本地名称表；`list-sdk-models` 显示进程内底层模块快照。

## 11. 测试工程

主要测试位置：

- `Test/dlcv_infer_c_test`
- `Test/dlcv_infer_cpp_test`
- `Test/dlcv_infer_c_dll_test`

覆盖范围包括普通模型和 DVS 的恢复与释放顺序、最后一次释放后失效、双模块归属、全量释放、空 DVS、恢复失败清理、模型列表不额外加载模块以及 `module_path` 来源。
