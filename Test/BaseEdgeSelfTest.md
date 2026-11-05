# 底座与边距跨语言回归

用同一份 `Test/fixtures/base_edge_cases.json` 测试 C# 与 C++，不加载模型、不启动界面。先使用项目构建入口编译两个测试工程，再运行：

```powershell
python .cursor/skills/vs-build/scripts/build.py Test/DlcvCSharpTest/DlcvCSharpTest.csproj
python .cursor/skills/vs-build/scripts/build.py Test/dlcv_infer_cpp_test/dlcv_infer_cpp_test.vcxproj
& Test/DlcvCSharpTest/bin/x64/Debug/DlcvCSharpTest.exe base-edge-selftest Test/fixtures/base_edge_cases.json <输出目录>/csharp.json
& Debug/dlcv_infer_cpp_test.exe base-edge-selftest Test/fixtures/base_edge_cases.json <输出目录>/cpp.json
```

输出目录须已存在。退出码 0 表示全部用例通过，1 表示失败。报告保留每个用例的实际输出，不以打印日志代替断言。

覆盖黑白底座、亮件过渡位置、调参、RGB 灰度转换、空白和小图；边距默认全选、部分选择、正负和零值、最高分及同分、缺失/非法目标、旋转框、裁剪缩放回原图、图片级扩展、无效变换、无方向配置和输入不变。合成底座期望由 Python 参考算法计算后固定在用例中；测试运行不依赖 Python 参考仓库。

外部真实图片可用同一命令验证。每个 JSON 用例指定 `name`、`type`、`image_path`、`properties` 和 `expected`；图像按 BGR 读取并转成 API 约定的 RGB。`expected` 对对象按所列字段比较，对数组严格检查长度，对数值使用 `1e-9` 容差，底座整数框等价于逐像素一致。

完整流程用例额外指定 `model_path`（`.dvst`）、可选 `device`（默认 0）及 `type="flow"`。通过公开 Model 接口分别执行结构化和 JSON 推理，检查最终框、类别、图片级判定、原因与扩展一致，再检查 `expected`。这条路径独立加载完整归档，不依赖共享索引的模型元信息比较。

真实验证应分开记录两类差异：底座框与 Python 相同参数应完全一致；加速前后的检测框和由其计算的边距可有模型数值差异。`sample_results.bbox` 是 XYWH，诊断 `extra_info.bbox_1/bbox_2` 是 XYXY，比较前须统一格式。真实模型和客户图片不放入仓库。
