# C# 线序与偏移量回归

## 运行

在仓库根目录使用既有构建入口，默认 Debug x64。构建日志和子进程 TEMP/TMP/TMPDIR 使用本 worktree 已忽略的 `temp/2209-build`；运行日志可保存在独占验证目录。

```powershell
python .cursor/skills/vs-build/scripts/build.py Test/DlcvCSharpTest/DlcvCSharpTest.csproj --configuration Debug --platform x64
& .\Test\DlcvCSharpTest\bin\x64\Debug\DlcvCSharpTest.exe algorithm-modules-selftest
```

自测使用内联构造图像、结果与流程，不加载模型、不启动 GUI、不读取客户数据。退出码 0 表示本专项通过，1 表示存在失败；不等价于真实模型或全量回归验收。

## 节点契约

- `post_process/wire_sequence_check`：`categories=[]`、`direction="horizontal"`（可选 `vertical`）。在原图坐标按框中心稳定排序，完整有序列表精确比较，保留重复类别、大小写和空格。空检测与空列表相等。
- `post_process/offset_distance`：`rules=[]`。规则为 `{"operator": ">" 或 "<", "value": 非负有限数值}`，全部 AND、严格大于/小于。恰好两个不同 `category_name`，各自按 score 取 top1，同分保留先出现项。空规则无距离限制，但仍必须两类；类别数不对或目标数值不可用为 `NG-Nan`。标签距离按 double 精确值舍入到六位小数（中点取偶，与 Python `.6f` 一致）、去末尾零，比较使用未舍入距离。
- 输入/输出均为 `image` 与 `results`。按原始 Mat **对象身份**汇总，保留首次出现的原图索引；不同 Mat 即使像素和索引相同也不合并。支持共享 wrapper 的旧 origin 位置别名，冲突/歧义归属报错。
- 每个 local 使用自身 transform 的逆仿射恢复原图中心，不使用当前 wrapper 的变换替代；未提供 transform 表示已在原图坐标。沿用 OpenIVS 格式：4 项数组是 XYWH，5 项是 cx/cy/w/h/angle（弧度）；对象框支持 x1/y1/x2/y2 或 cx/cy/w/h/angle（theta 别名）。OpenIVS 的 affine_2x3 已包含裁剪偏移，不额外叠加 crop_box。
- 输出 image 为原图，transform 为恒等；每张原图一个全幅分类目标，score=1.0、category_id 为 OK=0/NG=1，并输出 local 的 ok/reason。线序标签 OK/NG，偏移标签 OK-距离/NG-距离/NG-Nan。线序输出 XYWH 全幅框，偏移输出五项全幅旋转框，与参考节点的输出约定一致。

## 覆盖范围

注册和默认值、水平/竖直与重复完整线序、中心而非左边缘、同坐标稳定排序、各 local 裁剪/缩放/旋转、缺省 transform、延迟图像、不修改输入、原图身份分组与索引歧义、共享 wrapper 旧索引、真实 merge_results 两路共用图像、top1 与同分、零距、AND 与严格阈值、显示精度不影响比较、精确中点 1/128 及相邻浮点值、大数中心与距离、区域设置无关数值、空输入与非 local、非法类别/分数/框/规则/变换拒绝、标准 image_chan/result_chan 流程及 return_json 判定输出。

四项输入框的数值需按 OpenIVS XYWH 构造，不能把 Python XYXY 数组原样当作本语言输入。
