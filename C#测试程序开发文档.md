## DlcvDemo 开发文档（可复刻功能规格）

> 目标：任何开发者仅依据本文档，都能实现一个**功能与用户体验一致**的 `DlcvDemo`。允许代码结构不同，但最终呈现给用户的功能、交互、输出文本与可视化效果必须一致（除模型推理本身的不可控差异外）。

### 1. 文档范围与术语

- **软件名称**：`C# 测试程序`（主窗体标题与程序集名称一致）
- **项目/模块**：`DlcvDemo`（WinForms Demo）
- **DLCV SDK**：本 Demo 依赖的推理能力提供方（包含 `dlcv_infer_csharp`、`DlcvModules` 等）
- **模型文件**：深度视觉模型，扩展名包含 `.dvt/.dvo/.dvp/.dvst/.dvso/.dvsp`

### 2. 运行环境与依赖（必须满足）

- **操作系统**：Windows 10/11 x64
- **运行时**：.NET Framework **4.7.2**
- **UI 技术**：Windows Forms
- **图像处理**：OpenCvSharp4
- **JSON**：Newtonsoft.Json
- **本仓库内项目依赖**（`DlcvDemo` 必须引用）：
  - `DlcvCsharpApi`（提供 `dlcv_infer_csharp.Model`、`Utils` 等）
  - `ImageViewer`（提供控件 `DLCV.ImageViewer`）
  - `PressureTestRunner`（提供 `DLCV.PressureTestRunner`）
  - `DlcvModelRPC`（生成 `AIModelRPC.exe`，用于 RPC 模式；`DlcvDemo` 编译后需自动复制到输出目录）

#### 2.1 解决方案与编译配置（必须满足）

- **解决方案**：`OpenIVS.sln`
- **平台**：只提供 `x64`（Debug/Release），必须以 **x64** 方式编译与运行
- **启动项目**：`DlcvDemo`
- **输出**：
  - `DlcvDemo`：WinExe（无控制台窗口）
  - `DlcvModelRPC`：生成 `AIModelRPC.exe`（RPC 模式依赖）
  - 构建 `DlcvDemo` 后，必须将 `AIModelRPC.exe` 复制到 `DlcvDemo` 输出目录（与主 EXE 同目录）

#### 2.2 运行时外部依赖（必须满足/按功能启用）

> 这些依赖用于保证“加载模型/推理/设备枚举/加密狗检查”行为可用。若缺失，会导致对应功能失败或降级（必须与本文档描述一致）。

- **DLCV 推理 DLL（必须）**
  - `dlcv_infer.dll` 或 `dlcv_infer2.dll`：必须可被进程加载（通常位于输出目录或系统 PATH，或 SDK 固定路径）
- **OpenCvSharp 运行时（必须）**
  - `OpenCvSharpExtern.dll` + OpenCV 相关运行时 DLL（由 `OpenCvSharp4.runtime.win` 提供）
- **GPU 枚举（可选）**
  - `nvml.dll`（NVIDIA 驱动自带）：用于枚举 GPU 名称；失败/缺失时的 UI 表现见 **6.2**
- **加密狗检查（可选）**
  - `sntl_adminapi_windows_x64.dll`：用于读取加密狗信息
  - 缺失时：`检查加密狗` 输出为空数组（`[]`），不应崩溃
- **RPC 模式（按需）**
  - `AIModelRPC.exe`：优先从 `DlcvDemo` 输出目录启动；若不存在，可使用 SDK 固定路径（如 `C:\dlcv\Lib\site-packages\dlcvpro_infer_csharp\AIModelRPC.exe`）
- **DVP 模式（按需，加载 `.dvp` 时启用）**
  - 需要本机后端服务可访问 `http://127.0.0.1:9890`
  - 若服务未启动，SDK 可能尝试启动固定路径程序（例如 `C:\dlcv\Lib\site-packages\dlcv_test\DLCV Test.exe`）；不存在则加载失败

#### 2.3 从零搭建工程（建议步骤，便于复刻）

- **创建解决方案与项目**
  - 创建 WinForms 项目 `DlcvDemo`（.NET Framework 4.7.2，平台 x64）
  - 创建/加入类库项目：`DlcvCsharpApi`、`ImageViewer`、`PressureTestRunner`
  - 创建控制台项目：`DlcvModelRPC`（输出名 `AIModelRPC.exe`，平台 x64）
- **NuGet 依赖（版本需与现有一致或兼容）**
  - `Newtonsoft.Json (13.0.3)`
  - `OpenCvSharp4 (4.10.0.20241108)` + `OpenCvSharp4.runtime.win` + `OpenCvSharp4.Extensions`
- **项目引用**
  - `DlcvDemo` 引用：`DlcvCsharpApi`、`ImageViewer`、`PressureTestRunner`、`DlcvModelRPC`（仅用于构建依赖与复制 EXE）
  - `ImageViewer` 引用：`DlcvCsharpApi`
  - `DlcvModelRPC` 引用：`DlcvCsharpApi`
- **编译设置**
  - `ImageViewer` 必须启用 `AllowUnsafeBlocks=true`（用于 mask 透明叠加）
  - 建议将 `DlcvDemo` 也统一按 x64 配置编译运行（与解决方案一致）

### 3. 功能边界（必须严格一致）

- **必须具备的功能**：
  - GPU/CPU 设备列表显示与选择
  - 加载模型（多后缀），可选 RPC 模式
  - 打开图片并立即推理
  - 单次推理（对已选择图片重复推理）
  - 推理 JSON（输出 JSON 数组到文本框）
  - 获取模型信息（按不同类型显示/摘要显示）
  - 多线程压力测试（性能测试）
  - 一致性测试（检测推理结果不一致并停止）
  - 释放模型、释放所有模型
  - 检查加密狗（显示加密狗ID与特性）
  - 打开在线文档链接
  - 图像结果可视化（框/Mask/标签），支持缩放/拖拽/重置/快捷键切换

- **明确不做的功能**（避免不同人实现“加功能”导致不一致）：
  - 不提供相机/视频流推理
  - 不提供批量文件夹推理（除内部压力测试使用 batch_size 重复同一张图）
  - 不提供结果导出/保存到文件
  - 不提供可配置的推理参数面板（除阈值、batch_size、线程数）
  - 不提供 GPU 列表“刷新”按钮
  - 不提供流程编辑器/可视化编辑

### 4. UI 规格（必须一致）

#### 4.1 主窗体 `Form1`

- **窗口标题**：`C# 测试程序`
- **启动位置**：屏幕居中
- **最小尺寸**：宽 ≥ 1200，高 ≥ 900
- **默认字体**：微软雅黑 9pt
- **窗口图标**：`c_sharp.ico`
- **DPI 感知**：PerMonitorV2（多显示器不同缩放下保持清晰；配置在 `App.config`）
- **布局原则**：
  - 顶部：一排/两排操作区（加载/推理/测试/释放/信息）
  - 下方：左侧为文本输出框（RichTextBox），右侧为图像显示区（ImageViewer）

##### 4.1.1 控件清单与默认值（文本必须一致）

- **按钮**：`加载模型`、`打开图片推理`、`单次推理`、`推理JSON`、`多线程测试`、`一致性测试`、`释放模型`、`释放所有模型`、`检查加密狗`、`文档`、`获取模型信息`。
- **下拉框**：设备选择。
- **Label**：`选择显卡`、`线程数`、`batch_size`、`threshold`。
- **数值输入**：
  - 线程数（1-32，默认1）。
  - Batch Size（1-1024，默认1）。
  - Threshold（0.05-1.00，步进0.05，默认0.05）。
- **复选框**：`RPC模式`（默认不勾选）。

- **文本输出**
  - `richTextBox1`：用于显示错误、模型信息、推理结果文本、测试统计信息（不要求富文本格式）

- **图像显示控件**
  - `imagePanel1` 类型：`DLCV.ImageViewer`
  - 默认属性：
    - `ShowStatusText=false`
    - `MaxScale=100`
    - `MinScale=0.5`（实际运行会根据图片动态计算 MinScale）

### 5. 图像显示与可视化（必须一致）

控件：`DLCV.ImageViewer`（继承 Panel）

#### 5.1 交互行为

- **鼠标滚轮缩放**：
  - 放大：当 `scale < MaxScale` 时，`scale × 1.1`
  - 缩小：当 `scale > MinScale` 时，`scale ÷ 1.1`
  - 缩放中心：以鼠标指针位置为中心缩放
- **左键拖拽平移**：按住左键拖动图片
- **右键单击**：重置缩放为“适配面板并居中”（fit-to-panel）
- **键盘快捷键**：
  - 在控件获得焦点时（左键点击控件后）按 `V`：切换是否显示可视化结果（框/Mask/标签）

#### 5.2 绘制规则（框/文字/Mask）

- **输入**：仅可视化 `SampleResults[0]`（Batch中第一张图的结果）。
- **坐标**：基于**原图像素坐标**。
- **可视化输出**：
  - **边界框**：
    - 普通框：绘制矩形 `[x, y, w, h]`。
    - 旋转框：根据 `[cx, cy, w, h, angle]` 绘制四边形。
  - **Mask**：若存在，在框内叠加半透明蒙版（仅有效区域）。
  - **标签**：在框上方显示 `{category_name} {score:F2}`。
  - **颜色**：根据类别（OK/NG）区分颜色（如绿/红）。
  - **无结果状态**：当 `SampleResults.Count==0` 时，自动显示左上角状态文本 `No Result`（控件会将 `ShowStatusText` 置为 `true`）。

### 6. 主流程与状态（必须一致）

#### 6.1 全局状态变量（逻辑等价即可）

- **设备映射表**：设备名称 → device_id（CPU=-1，GPU从0递增）
- **当前模型**：`dynamic model`
  - 可能类型：`dlcv_infer_csharp.Model`
- **当前图片路径**：`string image_path`
- **batch_size**：与 UI 的 `numericUpDown_batch_size` 保持一致
- **压力测试**：`PressureTestRunner pressureTestRunner` + `Timer updateTimer`
- **一致性测试基准**：`baselineJsonResult`（首次推理写入；默认不会在停止测试时清空）
- **停止标志**：`shouldStopPressureTest`
- **模式标志**：`isConsistencyTestMode`

#### 6.2 启动时行为（Form Load）

- 置 `TopMost=false`
- 启动后台线程执行“设备信息获取”，完成后更新 UI：
  - 清空设备列表
  - **总是先添加 `CPU`**，并映射为 device_id=-1
  - 若 GPU 枚举成功：追加每张 GPU 的 `device_name`，映射为 device_id=0,1,2…
  - 默认选择：
    - 有 GPU：选择第一个 GPU（下拉索引 1）
    - 无 GPU：选择 CPU（下拉索引 0）
  - 若 GPU 枚举失败：在 `richTextBox1` 输出：
    - `GPU信息获取失败：\n{device_info_json}`
  - 调用一次 `dlcv_keep_max_clock()`（提升性能一致性）
- **设备选择生效规则（必须一致）**：
  - 主窗体下拉框的 device_id **只在加载时读取**（`加载模型`）
  - 加载完成后再切换下拉框，不会影响当前已加载模型的运行设备

#### 6.3 窗口关闭行为（FormClosing，必须一致）

- 关闭窗口时必须执行以下清理（顺序逻辑等价即可）：
  - 停止正在运行的压力测试/一致性测试（若有）
  - 若当前 `model` 实现 `IDisposable`：调用 `Dispose()` 并置空引用
  - 调用 `Utils.FreeAllModels()` 释放 SDK 侧所有模型
- 关闭过程中不应额外弹出提示框（除非发生未捕获异常，属于实现缺陷）

### 7. 详细功能规格（逐按钮）

> 说明：所有提示语、richTextBox 文本格式、按钮状态切换必须一致。

#### 7.1 加载模型（按钮：`加载模型`）

- **前置条件**：无（可在未选设备时使用；默认CPU=-1）
- **文件选择对话框**：
  - 标题：`选择模型`
  - 过滤器：
    - `深度视觉模型 (*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp)|*.dvt;*.dvp;*.dvo;*.dvst;*.dvso;*.dvsp|所有文件 (*.*)|*.*`
  - 初始目录/默认文件名：
    - 尝试从 `LastModelPath` 提取（异常忽略）
- **加载逻辑**：
  - 若存在旧 `model`：置空并触发 GC
  - 读取当前设备ID（由下拉框映射；CPU=-1）
  - 读取 `RPC模式` 复选框：
    - 勾选：`rpc_mode=true`
    - 未勾选：`rpc_mode=false`
  - 创建新模型实例（等价行为即可）：`new Model(path, deviceId, rpc_mode)`
  - **模式说明（需保持一致）**：
    - 模型后缀为 `.dvp`：走 DVP 模式（HTTP 后端服务），`RPC模式` 勾选不影响行为
    - 模型后缀为 `.dvst/.dvso/.dvsp`：走 DVS 模式，`RPC模式` 勾选不影响行为
    - 其他（如 `.dvt/.dvo`）：默认走本地 DLL 推理；若勾选 `RPC模式`，则使用本地 RPC 服务（依赖 `AIModelRPC.exe`）
  - 加载成功后自动执行一次“获取模型信息”（同 7.4）
- **异常**：
  - 捕获异常后：`richTextBox1.Text = ex.Message`（不弹窗）
- **副作用**：
  - 保存 `LastModelPath=所选路径`

#### 7.4 获取模型信息（按钮：`获取模型信息`）

- 前置条件：已加载模型，否则弹窗 `请先加载模型文件！`
- 调用 `model.GetModelInfo()` 返回 JSON（可能格式多样），显示规则如下：
  - 若包含 `model_info`：显示 `model_info` 对象（`richTextBox1.Text = result["model_info"].ToString()`）

#### 7.5 打开图片推理（按钮：`打开图片推理`）

- 前置条件：必须已加载模型，否则弹窗 `请先加载模型文件！`
- 文件对话框：
  - 标题：`选择图片文件`
  - 过滤器：
    - `图片文件 (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif|所有文件 (*.*)|*.*`
  - 初始目录/默认文件名：尝试使用 `LastImagePath`
- 用户选定图片后：
  - 保存 `image_path`
  - 保存 `LastImagePath`
  - **立即触发一次“单次推理”**（同 7.6）

#### 7.6 单次推理（按钮：`单次推理`）

- **功能**：对当前图片执行推理并展示结果。
- **前置条件**：
  - 已加载模型。
  - 已选择图片。
- **输入**：
  - 图片：当前选择的图片（读取为RGB）。
  - 参数：UI设置的 Batch Size, Threshold，强制 `with_mask=true`。
- **输出**：
  - **图像**：在界面显示原图及可视化结果。
  - **文本**：输出推理耗时及结果详情；且 `richTextBox1` 文本中必须包含字段名 `推理时间:` 与 `推理结果:`。
- **异常处理**：若图片无效或推理失败，弹窗提示错误。

#### 7.7 推理 JSON（按钮：`推理JSON`）

- 前置条件同 7.6（模型与图片均必须存在）
- 处理流程：
  - 读取图片（BGR）
  - 若 `image.Empty()==true`：输出控制台 `图像解码失败！` 并直接返回（不弹窗、不更新 UI）
  - 转为 RGB（BGR→RGB）
  - 参数 JSON：
    - `threshold = numericUpDown_threshold`
    - `with_mask = true`
  - 调用：`model.InferOneOutJson(image_rgb, params)`
- 输出到 `richTextBox1`：`JsonConvert.SerializeObject(json, Formatting.Indented)`（输出内容根节点必须为 JSON 数组 `[]`，即使为空）
- **说明**：该功能只输出 JSON 文本，不更新 `imagePanel1` 的图像与可视化结果
- 异常处理：`ReportError("推理JSON失败", ex)`

#### 7.8 多线程测试（按钮：`多线程测试`）

- 该按钮为**开关**：
  - 若测试正在运行：点击即停止
  - 若未运行：点击即启动“性能测试模式”
- 前置条件同 7.6（模型与图片必须存在）
- 启动逻辑（性能测试模式）：
  - `isConsistencyTestMode=false`
  - `shouldStopPressureTest=false`
  - batch_size、线程数从 UI 读取
  - 读取图片并转 RGB
  - 构造 image_list（重复同一张 Mat）
  - 创建 `PressureTestRunner(threadCount, targetRate=1000000, batchSize=batch_size)`
  - 设置 action 为 `ModelInferAction(image_list)`
  - 启动 500ms 定时器刷新统计：
    - 调用 `pressureTestRunner.GetStatistics(false)`
    - 写入 `richTextBox1`
  - 启动 runner
  - 按钮文字切为 `停止`
  - **说明**：测试过程中只刷新统计文本，不更新 `imagePanel1`
- **启动失败处理**：
  - 弹窗：`启动压力测试失败: {ex.Message}`（标题 `错误`，Error）
- **运行中异常处理**：
  - 任一 worker 推理回调发生异常时必须立即停止测试，并：
    - 弹窗：`压力测试过程中发生错误: {ex.Message}`（标题 `错误`，Error）
    - `richTextBox1.Text = "推理错误: {ex.Message}"`

`pressureTestRunner.GetStatistics(false)` 输出模板（必须一致，含空格/单位/换行）：

```text
压力测试统计:
线程数: {threadCount}
批量大小: {batchSize}
运行时间: {elapsedSeconds:F2} 秒
完成请求: {completedRequestsTimesBatchSize}
平均延迟: {averageLatencyMs:F2}ms
实时速率: {recentRate:F2} 请求/秒
```

说明：模板中的 `完成请求` 为内部完成次数 × batch_size（即处理的图片数）；但字段名仍为“完成请求”。  
说明：模板中的 `实时速率` 同样按（最近窗口内完成次数 × batch_size）/ 时间窗口计算，但字段名仍显示为“请求/秒”。
- 停止逻辑：
  - `pressureTestRunner.Stop()`
  - 停止定时器
  - `shouldStopPressureTest=false`
  - 按钮文字恢复为 `多线程测试`
  - `isConsistencyTestMode=false`

#### 7.9 一致性测试（按钮：`一致性测试`）

- 该按钮为**开关**，与 7.8 类似，但模式为一致性检查
- 启动逻辑（一致性测试模式）：
  - `isConsistencyTestMode=true`
  - 其余启动过程同 7.8
  - 按钮文字切为 `停止`
  - 定时器输出统计时，若 `baselineJsonResult!=null`，则在统计文本后追加：
    - `\n\n基准结果:\n{baselineJsonIndented}`
- **启动失败/运行中异常处理**：同 7.8（仅将提示文案中的“压力测试”替换为“一致性测试”）
- 一致性检查规则（在 worker 线程中执行）：
  - 推理调用使用 `model.InferInternal(image_list, {with_mask:false})`
  - 第一次推理且 `baselineJsonResult==null`：
    - 将本次 JSON 结果保存为基准并直接返回（不比较）
  - 后续推理：
    - 将基准与当前结果分别序列化为**不缩进** JSON 字符串（Formatting.None）
    - 若字符串不一致：
      - 立即置 `shouldStopPressureTest=true`
      - 回到 UI 线程：
        - 停止测试
        - `richTextBox1` 输出（格式必须一致）：
          - `发现推理结果不一致！测试已停止。`
          - `=== 基准结果 ===`
          - `{baselineIndentedJson}`
          - 空行 + `=== 当前结果 ===`
          - `{currentIndentedJson}`
        - 弹窗：`检测到推理结果不一致，测试已停止！`（标题 `结果不一致`，Warning）
        - 将 `baselineJsonResult=null`

> 注意：基准结果默认不会在“手动停止测试/释放模型/重新开始测试”时清空；只在检测到不一致时清空。该行为需保持一致。

#### 7.10 释放模型（按钮：`释放模型`）

- 若测试运行中，先停止测试
- 将 `model=null`，触发一次 GC
- `richTextBox1.Text="模型已释放"`

#### 7.11 释放所有模型（按钮：`释放所有模型`）

- 若测试运行中，先停止测试
- 若 model 可 Dispose：Dispose
- `model=null`
- 调用 `Utils.FreeAllModels()`
- `richTextBox1.Text="所有模型已释放"`

#### 7.12 检查加密狗（按钮：`检查加密狗`）

- 调用：
  - `SNTLUtils.GetDeviceList()`
  - `SNTLUtils.GetFeatureList()`
- 输出到 `richTextBox1`（格式必须一致）：
  - `加密狗ID：\n{deviceList}\n\n加密狗特性：\n{featureList}`

#### 7.13 文档（按钮：`文档`）

- 使用系统默认浏览器打开链接：
  - `https://docs.dlcv.com.cn/deploy/sdk/csharp_sdk`

### 8. 错误处理规范（必须一致）

#### 8.1 统一错误输出 `ReportError(title, ex)`

- `richTextBox1.Text = title + "\n" + ex.ToString()`
- 弹窗：`{title}: {ex.Message}`（标题 `错误`，Error）

#### 8.2 必须出现的提示弹窗（文案必须一致）

- 未加载模型：`请先加载模型文件！`
- 未选择图片：`请先选择图片文件！`
- 一致性不一致：`检测到推理结果不一致，测试已停止！`（标题 `结果不一致`）

### 9. 配置与持久化（必须一致）

使用用户范围设置保存最近路径（跨次启动可复用）：

- `LastModelPath`
- `LastImagePath`

保存时机：

- 加载模型成功选择文件后：写 `LastModelPath`
- 打开图片选择后：写 `LastImagePath`

读取时机：

- 打开对话框时，尝试用对应 Last*Path 设置：
  - `InitialDirectory = Path.GetDirectoryName(Last*Path)`
  - `FileName = Path.GetFileName(Last*Path)`
  - 异常一律忽略（不提示）

### 10. 验收清单

- **设备列表**
  - 启动后，设备下拉框首项必须为 `CPU`，其 device_id=-1
  - 若检测到 GPU，默认选中第一个 GPU（索引1）
  - 若 GPU 枚举失败，`richTextBox1` 必须出现 `GPU信息获取失败：`

- **加载模型**
  - 选择 `.dvt/.dvo/.dvp/.dvst/.dvso/.dvsp` 任一文件均可尝试加载
  - 加载完成后点击/自动触发“获取模型信息”可在文本框看到 JSON（或摘要）

- **打开图片推理**
  - 选择图片后必须立即执行一次推理，并在图像区域看到框/Mask/标签（若模型有结果）
  - 文本框必须包含 `推理时间:` 与 `推理结果:`

- **推理JSON**
  - 文本框输出必须为 JSON（缩进格式），根为数组 `[]`
  - 点击“推理JSON”不会自动刷新 `imagePanel1`（不应改变当前图像/可视化显示）

- **多线程测试**
  - 点击后按钮文字变为 `停止`
  - 文本框每 0.5s 更新一次，文本格式必须与 **7.8 的统计输出模板**一致
  - 再点击一次停止，按钮文字恢复为 `多线程测试`

- **一致性测试**
  - 点击后按钮文字变为 `停止`
  - 若发生不一致：必须停止测试并弹 Warning，文本框包含“基准结果/当前结果”两段
  - 停止后按钮文字恢复为 `一致性测试`

- **设备切换生效规则**
  - 加载模型后仅切换“选择显卡”下拉框，不会自动重载模型、不会弹窗提示
  - 只有再次点击“加载模型”时，才会读取当前下拉框的 device_id 生效

- **释放**
  - 释放模型后文本框显示 `模型已释放`
  - 释放所有模型后文本框显示 `所有模型已释放`

- **ImageViewer交互**
  - 滚轮缩放、左键拖拽、右键重置均有效
  - 聚焦后按 `V` 可切换显示/隐藏可视化结果

