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

真实流程中新增节点已在两端执行并输出角度标签；另修复 C# 输出层对旋转框表示的识别。以下上游差异仍保留，不能将整条流程当作相同矩阵输入测试：

- Python 与 OpenIVS 的普通模型掩码缩放方式不同；C++ 的掩码转旋转框未采用 C# 的最大连通域及腐蚀处理。一个目标在角度节点前已有约315°与317°的差异。
- 分类标签也存在差异，尚未完成全部因果定位。

因此验收结论限于新模块在相同输入下的行为一致；完整流程逐目标一致性尚未通过。

## C# 旋转框输出回归

`DlcvCSharpTest.exe return-json-rbox-selftest` 覆盖四项框加独立角度、五项框、平移旋转缩放、结果索引回退、水平框、结构化结果解析及裁图到原图的顶点恢复。修复前四项旋转框及裁图链路失败；修复后7项通过。`flow-mask-transform-selftest` 和 `image-generation-expand-selftest` 验证既有掩码映射与裁图行为。

修复仅在 C# `output/return_json` 归一化 `[cx,cy,w,h] + with_angle/angle`，复用已有五项旋转框映射；不将中心当左上角，不改写上游检测。普通水平框和 `angle=-100` 的无旋转约定保持不变。
