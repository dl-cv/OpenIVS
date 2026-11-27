# 云端 C# 测试程序接入

云端项目引用 `DlcvDemo.csproj`，通过 `DlcvDemo.Program.Run(string[] args, IDemoExecution execution)` 启动原窗口及 `ui-test`；窗口也可以通过 `new MainWindow(execution)` 创建。原程序直接启动时使用现有本机执行方式。

```csharp
public interface IDemoExecution
{
    JObject GetGpuInfo();
    JObject GetDogInfo();
    string GetEnvironmentInfo();
    string DescribeCudaMemory(int deviceId);
    IDemoModel LoadModel(string path, int deviceId, bool rpcMode);
    void FreeAllModels();
}

public interface IDemoModel : IDisposable
{
    JObject GetModelInfo();
    JObject GetCachedModelInfo();
    Utils.CSharpResult InferBatch(List<Mat> images, JObject parameters);
    JToken InferOneOutJson(Mat image, JObject parameters);
    IDemoPressureTest StartPressureTest(Mat image, int threadCount, int batchSize, bool checkConsistency);
}

public interface IDemoPressureTest : IDisposable
{
    DemoPressureStatus GetStatus(string language);
}
```

`DemoPressureStatus` 提供公开读写属性：`string Statistics`、`JObject Baseline`、`JObject DifferentResult`、`string Error`。`DifferentResult` 或 `Error` 非空表示节点已停止计时，窗口显示对应结果。`Dispose()` 停止测试。

## 节点命令

节点仅在 EXE 同目录存在 `remote-worker-v1.txt` 且内容为 `1` 时启动。正式分发使用 `C# 测试程序.exe`，源码构建使用 `DlcvDemo.exe`。执行 `DlcvDemo.exe remote-worker --root <会话绝对目录>` 时重定向 stdin、stdout 和 stderr。命令无需图形环境。stdin 和 stdout 使用 UTF-8，每行一个 JSON 对象；stderr 接收运行日志。节点按顺序处理请求，一次启动对应一个会话。云端窗口仅接受 `.dvt` 和 `.dvo`。选择本机 RPC 模式会在上传前提示该模式不受支持，因为原本机 RPC 服务的进程和管道在会话间共享。目前每会话只加载一个 `.dvt` 或 `.dvo` 模型。

请求为 `{"id":1,"action":"gpu_info"}`；`id` 支持字符串或整数，其余字段见下表。成功返回 `{"id":1,"ok":true,"result":...}`，失败返回 `{"id":1,"ok":false,"error":"..."}`。EOF 或 `shutdown` 停止测试并释放当前模型。节点在会话目录准备文件并负责清理；请求只传目录内已经写好的一层文件名。

两种推理的成功响应，以及超过 8192 个字符的其他成功响应，返回 `{"id":1,"ok":true,"result_file":"result-<随机编号>.json"}`。文件保存为会话目录中的 UTF-8 JSON，内容就是下表对应的 `result`，没有额外包装。节点传回结果文件后可删除该文件。

| `action` | 请求附加字段 | `result` |
| --- | --- | --- |
| `ping` | 无 | `null` |
| `gpu_info` | 无 | `Utils.GetGpuInfo()` 的原始对象 |
| `dog_info` | 无 | 与测试程序相同的加密狗信息对象 |
| `environment_info` | `language`：可选 `zh-CN` 或 `en-US` | 检查环境的原始文本 |
| `cuda_memory` | `device_id`：整数；`language`：可选 `zh-CN` 或 `en-US` | 指定设备显存文本 |
| `load_model` | `model`：`.dvt` 或 `.dvo` 文件名；`device_id`：整数；`rpc_mode`：可选布尔值，仅接受 `false`（默认值） | 已缓存模型信息对象 |
| `model_info` | 无 | 当前模型信息对象 |
| `infer_batch` | `image`：PNG 文件名；`batch_size`：整数；`parameters`：可选 JSON 对象 | 可还原 `CSharpResult` 的对象，结构见下文 |
| `infer_json` | `image`：PNG 文件名；`parameters`：可选 JSON 对象 | 原 `InferOneOutJson` 返回值 |
| `pressure_start` | `image`：PNG 文件名；`thread_count`：整数；`batch_size`：整数；`check_consistency`：布尔值 | `null` |
| `pressure_status` | `language`：可选 `zh-CN` 或 `en-US`，默认 `zh-CN` | `{"statistics":"...","baseline":null,"different_result":null,"error":null}` |
| `pressure_stop` | 无 | `null` |
| `free_model` | 无 | `null` |
| `shutdown` | 无 | `null`，发送响应后进程退出 |

图片由窗口传入已经准备好的 `Mat`。客户端直接用 `Cv2.ImEncode(".png", mat, out bytes)`，节点用 `ImreadModes.Unchanged` 解码，保持数据通道顺序。批量操作使用同一张图重复指定次数，线程数范围 `1–32`，批量范围 `1–1024`。测速与一致性比较均在节点运行，查询统计不计入测试时间。`parameters` 允许 `threshold`（可选数值，范围 0–1）和 `with_mask`（可选布尔值）；缺省时由原推理接口处理。

`infer_batch` 结果为 `{"json_text":string|null,"sample_results":[...]}`；每个样本字段包括 `results`、`ok`、`reason`、`extra_info`。每个目标字段为 `category_id`、`category_name`、`score`、`area`、`with_bbox`、`bbox`、`with_mask`、`mask_png`、`with_angle`、`angle`、`extra_info`。`mask_png` 为无损 PNG 的 Base64 或 `null`。OpenIVS 提供 `public static Utils.CSharpResult DemoRemoteResult.Decode(JObject result)`，供客户端还原完整结果和遮罩。

以下响应字段均会输出，可空字段按原推理结果返回 `null`。

| 位置与字段 | 类型 | 含义 |
| --- | --- | --- |
| 根 `json_text` | string 或 null | 完整原始 JSON，移除当前进程的 `mask_ptr` 地址 |
| 根 `sample_results` | array | 本次批量的样本结果 |
| 样本 `results` | array | 目标列表 |
| 样本 `ok`、`reason`、`extra_info` | boolean 或 null、string 或 null、object 或 null | 流程判定、说明和扩展数据 |
| 目标 `category_id`、`category_name` | integer、string 或 null | 类别编号与名称 |
| 目标 `score`、`area`、`angle` | number | 分数、面积、角度，保持原 API 数值 |
| 目标 `with_bbox`、`with_mask`、`with_angle` | boolean | 对应结果是否有效 |
| 目标 `bbox` | number array 或 null | 原 API 的框坐标 |
| 目标 `mask_png` | string 或 null | 单通道遮罩的无损 PNG Base64 |
| 目标 `extra_info` | object 或 null | 原目标扩展数据 |

所有请求都必须提供 `id`（string 或 integer）及 `action`（string）；表中未注明可选的附加字段均为必填。`device_id` 取值为 -2 至 2147483647，含义沿用原设备接口。统计结果的四个字段均存在：`statistics` 为 string，`baseline`、`different_result` 为 object 或 null，`error` 为 string 或 null。

客户端可用 `public static string DlcvDemo.Program.CurrentLanguage { get; }` 为环境检查和显存查询传入当前显示语言。

## 压力测试运行器

`DLCV.PressureTestRunner` 新增以下方法。原 `Stop()` 和两种 `GetStatistics` 调用继续有效。

```csharp
public void RequestStop();
public void Stop(bool waitForCompletion);
public string GetStatistics(bool target_rate, bool includeTestParameters, Func<string, string> translate);
```

`RequestStop()` 停止继续分配推理工作，可在执行线程内调用。`Stop(waitForCompletion: true)` 等所有推理线程释放资源后返回。`GetStatistics` 使用 `translate` 格式化原有字段。

## 自测

使用仓库常规构建的 `DlcvDemo.exe`。在独立测试目录中运行 `DlcvDemo.exe remote-worker-selftest --root <现有测试绝对目录>`，重定向 stdin、stdout、stderr。自测无需加密狗和实际模型，退出码为 `0` 且 stdout 返回 `{"status":"passed"}` 时通过。覆盖会话目录限制、本机 RPC 拒绝、图片像素和批量参数传递、遮罩像素往返、结果文件、语言及 EOF 释放。

在 OpenIVS 仓库根目录用 PowerShell 7 运行以下命令，可在已有依赖下检查源码类型和调用：

```powershell
pwsh -NoProfile -File ./Test/desktop_projects/CheckRemoteWorkerSources.ps1 -ReferenceDirectory '<已有 DlcvDemo 构建输出绝对目录>'
```

脚本从已安装的 .NET SDK 查找与 PowerShell 兼容的 Roslyn，按 C# 7.3 检查 `DlcvDemo.csproj` 列出的源文件和当前 `PressureTestRunner.cs`。引用来自已安装的 .NET Framework 4.7.2 参考程序集和 `ReferenceDirectory` 中已有的托管 DLL；压力测试使用当前源码，不引用目录中的 `PressureTestRunner.dll`。已有依赖须与待查源码匹配。本检查只读取 Roslyn 的诊断，不生成程序集；worker 的运行结果由上面的自测验证。
