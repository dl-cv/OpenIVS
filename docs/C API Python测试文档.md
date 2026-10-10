# dlcv_infer_cpp C API Python 测试

## 测试范围

`test_all_models.py` 通过 Python `ctypes` 直接调用 `dlcv_infer_cpp.dll` 的 C 接口。

测试格式：

- 普通模型：`.dvt`、`.dvo`
- 流程模型：`.dvst`、`.dvso`

`.dvr`、`.dvp`、`.dvsp` 不纳入 C 接口推理测试。

`AOI-旋转框检测_s.dvo` 依赖尚未支持的 ONNX Runtime 自定义算子 `MMCVRoIAlignRotated`，不纳入测试，不计入通过或预期失败数量。程序在控制台和结果汇总中记录排除原因；其他 `.dvo` 和旋转框 `.dvt` 模型仍按原范围测试。

每个模型依次执行：

1. `dlcv_infer_cpp_load_model_c`
2. `dlcv_infer_cpp_get_model_info_c`
3. `dlcv_infer_cpp_infer_with_params_c`
4. `dlcv_infer_cpp_infer_json_c`
5. 结构化结果和 JSON 字符串释放
6. `dlcv_infer_cpp_free_model_c`

程序结束前调用 `dlcv_infer_cpp_free_all_models_c`。

`dlcv_infer_c_test.exe --statistics-extra-info-selftest` 可单独验证四种统计开关、空采样、无统计节点、C/C++/JSON 扩展一致性，以及模型释放后扩展仍可读取、结果重复释放后清空。运行前通过 `DLCV_TEST_CORE_DLL` 指定本轮推理 DLL 的绝对路径。该自测使用内存图像和临时流程，不读取外部模型。

## 运行环境

- Windows x64
- `C:\dlcv\python.exe`
- Python 环境包含 `numpy` 和 `cv2`
- 已构建 `dlcv_infer_cpp.dll`

程序只加载 `dlcv_infer_cpp.dll`，该 DLL 包含 C API 实现及其 C++ API 依赖。

## 基本运行

在 OpenIVS 仓库根目录执行：

```powershell
C:\dlcv\python.exe Test\dlcv_infer_c_dll_test\test_all_models.py
```

直接运行时会在脚本目录生成 `dlcv_infer_c_api_test_result.json`，可从该文件查看汇总和逐模型结果。该结果文件已加入当前测试目录的忽略清单。

默认参数：

- 模型目录：`Y:\测试模型`
- 构建配置：`Debug`
- 设备编号：`0`
- 阈值：`0.5`
- `with_mask=false`
- 结果文件：`Test/dlcv_infer_c_dll_test/dlcv_infer_c_api_test_result.json`

程序自动查找以下核心 DLL：

1. `dlcv_infer_cpp/<配置>/dlcv_infer_cpp.dll`
2. `<配置>/dlcv_infer_cpp.dll`

## 指定 DLL 和结果文件

```powershell
C:\dlcv\python.exe Test\dlcv_infer_c_dll_test\test_all_models.py `
  --model-root "Y:\测试模型" `
  --dll "C:\path\to\dlcv_infer_cpp.dll" `
  --device 0 `
  --output "$env:TEMP\dlcv_c_api_model_results.json"
```

结果文件使用 UTF-8 JSON，包含每个模型的图片、各阶段状态、目标数、耗时和错误信息。

## 图片映射

程序内置 `Y:\测试模型` 当前模型名称与图片的匹配规则。新增其他模型时可通过 JSON 文件提供精确映射：

```json
{
  "新模型_120_50_s.dvt": "测试图片.jpg",
  "流程模型_120_50_s.dvst": "流程图片.png"
}
```

运行时增加：

```powershell
--image-map "C:\path\to\image_map.json"
```

映射值可以是绝对路径，也可以是相对模型目录的路径。未匹配模型可使用 `--default-image` 指定统一图片。

## 退出码

| 退出码 | 含义 |
|---:|---|
| `0` | 所有模型完成全部测试步骤 |
| `1` | 至少一个模型测试失败 |
| `2` | 参数、目录、DLL 或初始化失败 |

## 源码与结果比较检查

在仓库根目录执行以下检查，不加载推理 DLL：

```powershell
python -m unittest discover -s Test/dlcv_infer_c_dll_test -p test_result_compare.py -v
python -m unittest discover -s Test/dlcv_infer_c_dll_test -p test_mask_semantics_source.py -v
```

结果比较回归用内存中的 ctypes 缓冲区验证复制后独立读取，不加载 DLL、不执行推理：公开 `DlcvCObjectResult` 字段顺序、类型、大小和扩展指针偏移须与当前头文件一致；`extra_info` 按 UTF-8 解析为对象，并与 JSON 路径完整比较。覆盖均值／中值组四种启停组合、开启但无采样、有效 `0` 与 `null` 的区别、缺失组、折线及嵌套业务扩展；禁止统计返回目标一级，组不完整、类型错误和无效 UTF-8 均判为解析失败。mask 源码检查单独执行。

读取 `extra_info` 时在 `dlcv_infer_cpp_free_model_result_c` 前通过 `ctypes.string_at` 复制并以 UTF-8 解码，再由 `json.loads` 得到调用方拥有的对象；空指针表示没有扩展，不补统计键。结果释放后不再访问该指针，也不单独释放 `extra_info`。公开声明和释放要求见 [C API 文档](<C API文档.md>) 3，统计格式见 [结果标准](模块、流程与模型推理标准文档.md) 3.3.1。

```python
extra = json.loads(ctypes.string_at(object_result.extra_info).decode("utf-8")) \
    if object_result.extra_info else {}
if "with_mean" in extra:
    foreground_mean = extra["foreground_mean"]  # None 是无采样，0 是有效数值
```

源码检查与缓冲区回归不证明 DLL 推理或结果释放实现已通过运行验收；实际结构化／JSON 两路推理须使用配套构建产物另行执行。
