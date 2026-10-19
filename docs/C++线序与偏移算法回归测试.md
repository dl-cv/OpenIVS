# C++ 线序与偏移算法回归测试

测试入口：`Test/dlcv_infer_cpp_test` 的 `algorithm-modules-selftest`。仅使用内联合成目标、内存图像和临时流程 JSON，不需要模型、加密狗或客户图片。

## 构建与运行

在独立 worktree 根目录执行，先取得共享编译槽。构建与依赖工程使用仓库脚本，禁止直接调用编译器；NuGet 依赖只读复用。

```powershell
New-Item -ItemType Directory -Force temp/2209-build | Out-Null
$env:TEMP = Join-Path $PWD 'temp/2209-build'
$env:TMP = $env:TEMP
$env:TMPDIR = $env:TEMP
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
python .cursor/skills/vs-build/scripts/build.py Test/dlcv_infer_cpp_test/dlcv_infer_cpp_test.vcxproj --configuration Debug --platform x64 *> temp/2209-build/build.log
```

确认构建退出码为 0 后运行 `Debug/dlcv_infer_cpp_test.exe algorithm-modules-selftest`。普通运行时将 TEMP/TMP/TMPDIR 设置为本任务独占的验证输出目录，并在该目录保存运行日志。测试将四份 `algorithm-shared-input-*.json` 合成流程写入 TEMP，方便复查。成功打印 `algorithm-modules-selftest passed: ... assertions`，返回 0；断言失败返回 1。

相关回归入口：`category-count-check-selftest`、`cross-model-label-merge-selftest`、`foreground-background-statistics-selftest`。所有命令均不带模型路径。

## 行为与覆盖

- `post_process/wire_sequence_check`：属性 `categories`（默认空数组）和 `direction`（默认 `horizontal`，另支持 `vertical`）。比较完整有序列表，保留重复类别，同中心稳定排序，不修剪空格或变换大小写。
- `post_process/offset_distance`：属性 `rules`（默认空数组），仅支持严格 `>`/`<`，全部规则 AND。必须恰有两类，每类 score top1，同分选首项；空规则不免除两类要求。无有效距离时输出 `NG-Nan`。
- 距离使用各 local 变换恢复后的原图中心。比较原始 double；标签六位小数去尾零，包括精确中点 `1/128` → `0.007812` 的回归。
- 按原图存储身份归组，不按像素内容或索引合并；输出原图及 identity transform、全幅分类、score=1.0、local 顶层 `ok/reason`。测试覆盖不同原图、同原图多个索引、空输入和输入不变。
- 覆盖平移、缩放、旋转、各 local 独立变换、非法尺寸、负裁剪起点、非有限/奇异矩阵、阈值边界、低分无效框与 top1 无效框。
- 同时验证测试 EXE 注册表和公开 DLL `FlowGraphModel`。图级流程的 MergeResults 两路 image 均来自节点 3，results 来自节点 3/4；测试仅分类输出及末尾合并原 A/B 检测框两种路径，并验证 `InferInternal`、`InferOneOutJson`。

## C++ 坐标协议

C++ 普通 bbox 数组为 `[x,y,w,h]`，旋转框为 `[cx,cy,w,h,angle]`（亦接受 native 四项加 `with_angle/angle`）。测试不可直接把 Python 的普通 xyxy 数组原样作为 C++ 输入。

C++ `affine_2x3` 是完整 original→current 变换，已包含裁剪平移，恢复中心时直接求逆，不重复加 crop_box。原图尺寸字段为 `original_width/original_height`；`TransformationState::ToJson()` 会省略默认的 crop_box/output_size/affine_2x3，因此省略字段按 native identity/default 语义处理。显式裁剪起点必须非负，宽高严格为正；local transform 缺失或 null 表示原图坐标，不使用 wrapper 当前变换代替。

## 验证边界

合成测试通过不代表真实 SDK 模型验收。真实流程应与 Python 参考的类别、距离和原检测框一致，不能以分类必须为 OK 作为成功标准。编译、专项运行、相关回归和真实流程验收分别记录。
