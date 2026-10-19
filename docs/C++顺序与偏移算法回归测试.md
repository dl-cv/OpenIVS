# C++ 顺序与偏移算法回归测试

测试入口：`Test/dlcv_infer_cpp_test` 的 `algorithm-modules-selftest`。仅使用内联合成目标、内存图像和临时流程 JSON，不需要模型、加密狗或客户图片。

## 构建与运行

在仓库根目录通过既有构建入口运行，默认 Debug x64。

```powershell
New-Item -ItemType Directory -Force temp/2209-build | Out-Null
$env:TEMP = Join-Path $PWD 'temp/2209-build'
$env:TMP = $env:TEMP
$env:TMPDIR = $env:TEMP
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
python .cursor/skills/vs-build/scripts/build.py Test/dlcv_infer_cpp_test/dlcv_infer_cpp_test.vcxproj --configuration Debug --platform x64 *> temp/2209-build/build.log
```

确认构建退出码为 0 后运行 `Debug/dlcv_infer_cpp_test.exe algorithm-modules-selftest`。普通运行时将 TEMP/TMP/TMPDIR 设置为本任务独占的验证输出目录，并在该目录保存运行日志。测试将 `algorithm-shared-input-*.json` 合成流程及对应 `.dvst` 写入 TEMP，方便复查。成功打印 `algorithm-modules-selftest passed: ... assertions`，返回 0；断言失败返回 1。

相关回归入口：`category-count-check-selftest`、`cross-model-label-merge-selftest`、`foreground-background-statistics-selftest`。所有命令均不带模型路径。

## 覆盖范围

节点属性、坐标与输出契约见 [模块、流程与模型推理标准文档](模块、流程与模型推理标准文档.md#642-顺序比对与偏移量计算)。

专项覆盖水平／竖直完整顺序、重复类别与稳定排序、两类 score top1 与同分、AND 严格阈值、未舍入距离判断、六位小数中点、空输入及无效检测、原图身份分组、平移／缩放／旋转和各 local 独立变换、非法尺寸／负裁剪起点／奇异矩阵、输出全幅分类及输入不变。

同时验证测试 EXE 注册表和公开 DLL `FlowGraphModel`，以及公开 `Model` 加载合成 `.dvst`。顺序比对的新主类型和旧兼容类型均覆盖 OK/NG、图输出原因及保留原检测框；模块级额外核对新旧类型的 `originating_module`、空序列与输入不变。图级流程包括两路共享图像的 `MergeResults`，仅保留分类以及保留原检测框两种输出，经 `InferInternal` 与 `InferOneOutJson` 核对。

## 验证边界

合成测试不加载真实 SDK 模型；真实流程应另行与 Python 参考结果对照，不能以分类必须为 OK 作为成功标准。