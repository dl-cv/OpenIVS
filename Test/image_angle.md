# 获取图像角度模块验证

范围为 `post_process/image_angle`，C# 与 C++ 输入同一组矩阵及 local 结果，预期由指定方向与十进制舍入规则独立给定，并经过 Python 模块核对。

```powershell
python Test/generate_image_angle_cases.py <包含新模块的dlcv_test源码目录>
Test/DlcvCSharpTest/bin/x64/Debug/DlcvCSharpTest.exe image-angle-selftest Test/image_angle_cases.json
Debug/dlcv_infer_cpp_test.exe image-angle-selftest Test/image_angle_cases.json
```

运行目录、输出和临时目录按工作区规则设置。测试工程通过项目 `build_script/build.py` 编译。

52组用例覆盖：右下左上方向、负角度、等比缩放与平移、默认整数及0/1/2位小数、半值舍入、360归0、结果重排及同图多结果、空结果、非法精度和索引、镜像、错切、非等比缩放及退化矩阵。每组重复执行，并检查输入结果、图像及目标来源字段保持不变。

## 真实流程的验证边界

真实流程中新增节点已在两端执行并输出角度标签；本次不修改上游模块。已观察到以下既有差异，不能将整条流程当作相同矩阵输入测试：

- Python 与 OpenIVS 的普通模型掩码缩放方式不同；C++ 的掩码转旋转框未采用 C# 的最大连通域及腐蚀处理。一个目标在角度节点前已有约315°与317°的差异。
- C# 裁图结果可采用四项框加独立角度，但输出模块仅以五项框识别旋转框，因此部分最终框型及位置不同。
- 分类标签也存在差异，尚未完成全部因果定位。

因此验收结论限于新模块在相同输入下的行为一致；完整流程逐目标一致性尚未通过。
