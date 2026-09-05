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
- `calc_mean=false`
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
