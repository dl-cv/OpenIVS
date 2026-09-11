# C++ Demo 配套构建回归

## 修复范围

仅修改原生工程的导入库引用和运行 DLL 复制方式。C、C++、C# 的业务源码、公开 API 和既有调用方式均保持原主分支状态。

`DlcvNativeRuntime.targets` 从 `ProjectReference` 的 `GetTargetPath` 取得本次 DLL，复制到 EXE 目录；C++ 导入库由工程引用提供。必需 DLL 或 Qt 平台插件复制失败时，构建失败。单项目与解决方案构建可能输出到不同目录，执行文件以本次构建日志为准。

## 专用检查

继续调用已有的 C++ Qt `infer`，不增加窗口测试入口或业务辅助类型。专用脚本执行前比较 EXE 同目录 DLL 与指定构建 DLL 的 SHA256，再检查最终进程退出码、UTF-8 JSON、两种推理结果的类别、数量和分数，以及原有一致性状态。

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest discover -s Test -p test_demo_regression_runner.py
python Test/run_demo_regression.py --exe <本次Demo.exe> --dll <本次构建的dlcv_infer_cpp.dll> --model <模型.dvt> --image <图片.jpg> --category <类别> --score <固定分数>
```

目标数量默认 1，阈值固定 0.5，超时默认 90 秒；可用 `--count`、`--timeout` 调整。报告默认写入系统临时目录，也可通过 `--output` 指定报告文件。报告记录退出码、程序和输入哈希、失败原因，不包含本机绝对路径。

## 已有固定基准

以下值来自 `Test/DlcvCSharpTest/ModelRegressionCases.cs`，未按本次结果修改。

| 模型 | 图片 | 类别 | 数量 | 分数 |
|---|---|---|---|---|
| 猫狗-分类_120_50_v.dvt | 猫狗-狗.jpg | 狗 | 1 | 0.9951171875 |
| 气球-实例分割_120_50_v.dvt | 气球.jpg | 气球 | 1 | 0.9990234375 |

## 检查范围

- 非零退出码优先判为失败，不能由正确 JSON 抵消。
- 同目录 DLL 与指定构建 DLL 不同、超时、缺失输出、无效编码、基准不符均失败。
- 单元测试覆盖成功与上述失败情况，其中包含完整正确 JSON 但进程最终崩溃的情况。
- 本检查针对 Demo 加载、推理及释放时的进程异常，不比较 mask 像素，不代表压力测试、系统文件选择窗口或全部业务流程通过。
- 不支持混用任意旧 EXE 与新 DLL；C++ 头文件、导入库、DLL 和调用端需要配套构建。本轮未增加公开 ABI 标签或查询接口。
