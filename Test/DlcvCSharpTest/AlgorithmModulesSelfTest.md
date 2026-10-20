# C# 顺序与偏移量回归

## 运行

在仓库根目录使用既有构建入口，默认 Debug x64。

```powershell
python .cursor/skills/vs-build/scripts/build.py Test/DlcvCSharpTest/DlcvCSharpTest.csproj --configuration Debug --platform x64
& .\Test\DlcvCSharpTest\bin\x64\Debug\DlcvCSharpTest.exe algorithm-modules-selftest
```

自测使用内联构造图像、结果与流程，不加载模型、不启动 GUI、不读取客户数据。退出码 0 表示本专项通过，1 表示存在失败；不等价于真实模型或全量回归验收。

节点属性、坐标与输出格式见 [模块、流程与模型推理标准文档](../../docs/模块、流程与模型推理标准文档.md#642-顺序比对与偏移量计算)。

## 覆盖范围

注册和默认值、新旧类型在 OK/NG 及空序列下的分类主标识与原因一致、旧类型图流程加载、水平/竖直与重复完整顺序、中心而非左边缘、同坐标稳定排序、各 local 裁剪/缩放/旋转、缺省 transform、延迟图像、不修改输入、原图身份分组与索引歧义、共享 wrapper 旧索引、真实 merge_results 两路共用图像、top1 与同分、零距、AND 与严格阈值、显示精度不影响比较、精确中点 1/128 及相邻浮点值、大数中心与距离、区域设置无关数值、空输入与非 local、非法类别/分数/框/规则/变换拒绝、标准 image_chan/result_chan 流程及 return_json 判定输出。

普通四项输入框使用 OpenIVS XYWH；`with_angle=true` 且独立有限 `angle>-99` 时，四项框使用 `[cx,cy,w,h]`。回归固定使用宽度不同的目标，验证中心距离为 20 px 时 `<18` 判 NG、`>18` 判 OK，以及真实中心的顺序；同时检查缺少或无效独立角度的拒绝处理、`-100/-99` 哨兵值、非布尔 true 标志和五项旋转框不读取独立角度。

参考图像覆盖水平镜像、旋转后的竖直排序、local 裁剪组合、空输入兼容和只使用首张（第二张无效仍忽略）；通过 `image_flip → reference_image` 完整流程验证新旧节点类型的 OK/NG 与 Reason。输出始终恢复原图。
