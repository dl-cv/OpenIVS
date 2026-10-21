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

距离模块仅输出图片级 `ok/reason`，回归要求 `sample_results` 为空；顺序模块继续检查全幅分类。合并测试检查判定在普通检测之前或之后、通过 index 或 origin_index 关联时均不丢失，多个判定取 AND 且重复原因只保留一次。两张同尺寸、同变换图像使用三个 local 结果进入实际映射路径，验证空结果不复制另一图的两个目标。C# FlowGraphModel 的结构化与 JSON 路径同时覆盖空检测判定、保留原检测支路和正常顺序分类；C++ 专项通过公开 DLL API 检查相同结果及 `GetLastInspectionStatus`。

原因文本不附单位，通过时按配置顺序显示全部比较关系，失败时只显示未通过规则。回归包含 `>`、`<`、范围 AND、多规则失败、等号判 NG、空规则和六位舍入前比较；距离与阈值均检查六位偶数舍入及去除末尾零。

图片级 `extra_info.distance` 覆盖未舍入 double、有效零、无法计算的 JSON `null`、原有其他扩展字段保留及输入不变；结构化、JSON 和批量结果分别检查距离，并确认真实目标不携带图片级距离。原公开构造调用保持有效，没有扩展时不补字段。

参考图像覆盖水平镜像、旋转后的竖直排序、local 裁剪组合、空输入兼容和只使用首张（第二张无效仍忽略）；通过 `image_flip → reference_image` 完整流程验证新旧节点类型的 OK/NG 与 Reason。输出始终恢复原图。
