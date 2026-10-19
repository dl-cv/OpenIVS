# C# 线序与偏移量回归

## 运行

在仓库根目录使用既有构建入口，默认 Debug x64。

```powershell
python .cursor/skills/vs-build/scripts/build.py Test/DlcvCSharpTest/DlcvCSharpTest.csproj --configuration Debug --platform x64
& .\Test\DlcvCSharpTest\bin\x64\Debug\DlcvCSharpTest.exe algorithm-modules-selftest
```

自测使用内联构造图像、结果与流程，不加载模型、不启动 GUI、不读取客户数据。退出码 0 表示本专项通过，1 表示存在失败；不等价于真实模型或全量回归验收。

节点属性、坐标与输出契约见 [模块、流程与模型推理标准文档](../../docs/模块、流程与模型推理标准文档.md#642-线序比对与偏移量计算)。

## 覆盖范围

注册和默认值、水平/竖直与重复完整线序、中心而非左边缘、同坐标稳定排序、各 local 裁剪/缩放/旋转、缺省 transform、延迟图像、不修改输入、原图身份分组与索引歧义、共享 wrapper 旧索引、真实 merge_results 两路共用图像、top1 与同分、零距、AND 与严格阈值、显示精度不影响比较、精确中点 1/128 及相邻浮点值、大数中心与距离、区域设置无关数值、空输入与非 local、非法类别/分数/框/规则/变换拒绝、标准 image_chan/result_chan 流程及 return_json 判定输出。

四项输入框的数值需按 OpenIVS XYWH 构造，不能把 Python XYXY 数组原样当作本语言输入。
