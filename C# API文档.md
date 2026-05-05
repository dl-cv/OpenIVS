# C# API 文档

**文档定位**：记录 `DlcvCsharpApi` 的工程结构、依赖、固定加载路径、运行模式与 C# 对外接口。所有内容以当前源码实现为准。

---

## 1. 项目结构

| 目录/文件 | 说明 |
|-----------|------|
| `DlcvCsharpApi/` | C# 封装层主目录 |
| `Model.cs` | `Model` 类，对接底层 C++ DLL |
| `Utils.cs` | `Utils` 类，结果类型与工具方法 |
| `DllLoader.cs` | `DllLoader` 类，DLL 加载与函数代理 |
| `flow/FlowGraphModel.cs` | `FlowGraphModel` 类，流程图推理 |
| `flow/DvsModel.cs` | `DvsModel` 类，DVS 归档加载 |
| `flow/runtime/ExecutionRuntime.cs` | `InferTiming` 类，推理计时 |

命名空间：
- `DlcvModules`：`Model`、`Utils`、`FlowGraphModel`、`DvsModel`、`InferTiming`
- `dlcv_infer_csharp`：`DllLoader`
- `sntl_admin_csharp`：`DogUtils`、`DogProvider`（加密狗工具，C# 同名封装）

---

## 2. 核心结果类型（Utils.cs）

### 2.1 CSharpObjectResult

```csharp
public partial class Utils
{
    public struct CSharpObjectResult
    {
        public int CategoryId { get; set; }           // 类别 ID
        public string CategoryName { get; set; }      // 类别名称
        public float Score { get; set; }              // 置信度
        public float Area { get; set; }               // 面积
        public List<double> Bbox { get; set; }        // 水平框: [x, y, w, h]; 旋转框: [cx, cy, w, h]
        public bool WithMask { get; set; }            // 是否含 mask
        public Mat Mask { get; set; }                 // OpenCV mask（CV_8UC1），未检出时可能为空 Mat
        public bool WithBbox { get; set; }            // 是否含 bbox
        public bool WithAngle { get; set; }           // 是否含旋转角度
        public float Angle { get; set; }              // 旋转角度（弧度），-100 表示无效
        public JObject ExtraInfo { get; set; }        // 额外信息（polyline 等）

        public CSharpObjectResult(
            int categoryId, string categoryName, float score, float area,
            List<double> bbox, bool withMask, Mat mask,
            bool withBbox = false, bool withAngle = false, float angle = -100, JObject extraInfo = null);
    }
}
```

**字段约束**：
- `Bbox` 长度约定：水平框 ≥4（`x,y,w,h`），旋转框 ≥4（`cx,cy,w,h`，`angle` 单独字段）。
- `Angle` 有效值范围：`> -99.0f` 视为有效；`-100.0f` 视为无效。
- `Mask` 为空时（`Mask == null || Mask.Empty()`），`WithMask` 应为 `false`。
- `ExtraInfo` 可包含 `polyline`（通过 `Utils.GetExtraInfoPolyline` / `Utils.SetExtraInfoPolyline` 读写）。

### 2.2 CSharpSampleResult

```csharp
public partial class Utils
{
    public struct CSharpSampleResult
    {
        public List<CSharpObjectResult> Results { get; set; }

        public CSharpSampleResult(List<CSharpObjectResult> results);
    }
}
```

### 2.3 CSharpResult

```csharp
public partial class Utils
{
    public struct CSharpResult
    {
        public List<CSharpSampleResult> SampleResults { get; set; }

        public CSharpResult(List<CSharpSampleResult> sampleResults);
    }
}
```

**结构层级**：`CSharpResult → List<CSharpSampleResult> → List<CSharpObjectResult>`

---

## 3. Model 类（Model.cs）

### 3.1 构造与加载

```csharp
public class Model : IDisposable
{
    public Model(string modelPath, int deviceId = 0, bool rpcMode = false, bool enableCache = false);
}
```

**构造函数行为**：
1. 若路径以 `.dvst` / `.dvso` / `.dvsp` 结尾 → 进入 Flow/DVS 模式，实例化 `FlowGraphModel` 或 `DvsModel`。
2. 否则 → 普通模型模式，通过 `DllLoader` 调用底层 C API。
3. 构造失败时抛出 `Exception`（底层错误信息封装在异常消息中）。
4. 加载完成后可通过 `Loaded` 属性判断状态。

### 3.2 属性

```csharp
public int modelIndex;                // 底层模型索引（普通模型），字段
public bool OwnModelIndex { get; set; } = true;  // 是否拥有释放权
public DogProvider LoadedDogProvider { get; }      // 已加载的加密狗类型
public string LoadedNativeDllName { get; }         // 已加载的原生 DLL 名称
```

### 3.3 单图推理

```csharp
public CSharpResult Infer(Mat image, JObject paramsJson = null);
```
- `image`：输入图像，调用层需确保为 RGB 格式（8UC3）。
- `paramsJson`：可选推理参数，常见字段见第 10 节。
- 内部调用 `prepareInferInput` 对图像做通道/位深归一化。
- 返回 `CSharpResult`，`SampleResults` 长度恒为 1。

### 3.4 批量推理

```csharp
public CSharpResult InferBatch(List<Mat> imageList, JObject paramsJson = null);
```
- `imageList`：输入图像列表，长度即 batch size。
- 返回结果中 `SampleResults` 长度与输入图像数量一致。
- 每个 `CSharpSampleResult.Results` 对应该图像的检测结果列表。

### 3.5 JSON 单图输出

```csharp
public dynamic InferOneOutJson(Mat image, JObject paramsJson = null);
```
- 返回 `JArray`，每个元素为单个检测结果对象。
- 字段包含：`category_id`、`category_name`、`score`、`bbox`（`[x,y,w,h]`）、`with_bbox`、`with_angle`、`angle`、`poly`（多边形点数组）、`mask_rle`（RLE 编码 mask）、`area`。

### 3.6 测速

```csharp
public double Benchmark(Mat image, int warmup = 1, int runs = 10);
```
- 先执行 `warmup` 次预热，再执行 `runs` 次正式推理。
- 返回平均每次推理耗时（毫秒）。

### 3.7 模型信息

```csharp
public JObject GetModelInfo();
```
- 返回模型元信息 JSON（包含 `model_info`、`input_shapes`、`dog_provider`、`loaded_model_meta` 等）。

### 3.8 释放

```csharp
public void Dispose();
public void FreeModel();
```
- Flow 模式：释放 `FlowGraphModel`。
- 普通模式：调用 `dlcv_free_model` 释放底层模型。
- 析构函数自动调用 `Dispose()`。

---

## 4. FlowGraphModel 类（flow/FlowGraphModel.cs）

### 4.1 加载

```csharp
public class FlowGraphModel : IDisposable
{
    public JObject Load(string flowJsonPath, int deviceId = 0);
    protected JObject LoadFromRoot(JObject root, int deviceId);
}
```

**`Load` 行为**：
1. 读取流程 JSON 文件，解析 `nodes` 数组。
2. 通过 `GraphExecutor` 加载每个节点引用的模型。
3. 返回加载报告：`{"code": 0, "models": [{"model_path": "...", "status_code": 0, ...}]}`。
4. 若任一模型加载失败，`code != 0`，并提取第一个失败模型的错误信息到 `message` 字段。

### 4.2 推理

```csharp
public Utils.CSharpResult Infer(Mat image, JObject paramsJson = null);
public Utils.CSharpResult InferBatch(List<Mat> imageList, JObject paramsJson = null);
public dynamic InferOneOutJson(Mat image, JObject paramsJson = null);
```
- 接口与 `Model` 完全一致。
- `Infer` 内部调用 `InferBatch(new List<Mat> { image })`。
- `InferOneOutJson` 内部调用 `InferInternalCore(..., emitPoly: true)` 以保留 `poly` 字段。

### 4.3 内部推理方法

```csharp
public Tuple<JObject, IntPtr> InferInternal(List<Mat> images, JObject paramsJson);
private Tuple<JObject, IntPtr> InferInternalCore(List<Mat> images, JObject paramsJson, bool emitPoly);
```
- `InferInternalCore` 是核心实现：
  1. 将输入图像放入 `ExecutionContext`（键：`frontend_image_mat`、`frontend_image_mats`、`frontend_image_mat_list`、`frontend_image_path`、`device_id`、`return_json_emit_poly`）。
  2. 执行 `GraphExecutor::Run()`。
  3. 从 `frontend_json` / `frontend_json_by_node` 收集各节点输出。
  4. 按 `origin_index` 或位置索引映射回原始图像结果。
  5. 返回 `{"result_list": [...]}` 格式 JSON。

### 4.4 模型信息

```csharp
public JObject GetModelInfo();
public JArray GetLoadedModelMeta();
```
- `GetModelInfo` 返回包含 `nodes`、`loaded_model_meta`、`model_info` 的完整 JSON。
- `GetLoadedModelMeta` 返回已加载模型的元信息数组。

### 4.5 释放

```csharp
public void Dispose();
```
- 标记 `_disposed = true`，不主动释放底层模型（由 `GraphExecutor` 生命周期管理）。

---

## 5. DvsModel 类（flow/DvsModel.cs）

```csharp
public class DvsModel : FlowGraphModel
{
    public new JObject Load(string dvsPath, int deviceId = 0);
}
```

**`Load` 行为**：
1. 打开 `.dvst`/`.dvso`/`.dvsp` 文件，校验头部 `DV\n`。
2. 读取 JSON 头行，解析 `file_list` 和 `file_size` 数组。
3. 将 `pipeline.json` 读入内存，其他文件解包到临时目录（临时文件名使用 `Guid.NewGuid().ToString("N")` + 原扩展名，避免中文路径问题）。
4. 修改 `pipeline.json` 中各节点的 `model_path` 为临时目录中的实际路径，保留原始路径到 `model_path_original` 和 `model_name`。
5. 调用 `LoadFromRoot(pipelineJson, deviceId)` 完成加载。
6. `finally` 中清理临时目录。

**异常**：
- 文件格式错误：`InvalidDataException`（"文件格式错误：缺少 DV 头部"）
- 头信息损坏：`InvalidDataException`
- 未找到 `pipeline.json`：`InvalidDataException`

---

## 6. DllLoader 类（DllLoader.cs）

```csharp
public class DllLoader
{
    // 全局单例
    public static DllLoader Instance { get; }

    // 根据模型头中的 dog_provider 字段，确保加载正确的 DLL
    public static void EnsureForModel(string modelPath);

    // 委托类型与字段（底层 C API 代理）
    public LoadModelDelegate      dlcv_load_model;
    public FreeModelDelegate      dlcv_free_model;
    public GetModelInfoDelegate   dlcv_get_model_info;
    public InferDelegate          dlcv_infer;
    public FreeModelResultDelegate dlcv_free_model_result;
    public FreeResultDelegate     dlcv_free_result;
    public FreeAllModelsDelegate  dlcv_free_all_models;
    public GetDeviceInfoDelegate  dlcv_get_device_info;
    public GetGpuInfoDelegate     dlcv_get_gpu_info;
    public KeepMaxClockDelegate   dlcv_keep_max_clock;

    public DogProvider LoadedDogProvider { get; }
    public string LoadedNativeDllName { get; }
}
```

**DLL 映射**：

| 加密狗类型 | DLL 名称 | 路径 |
|-----------|---------|------|
| Sentinel | `dlcv_infer.dll` | `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll` |
| Virbox | `dlcv_infer_v.dll` | `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer_v.dll` |

**自动检测优先级**：先检测 Sentinel，再检测 Virbox；均未检测到则回退到 Sentinel。

**模型级 Provider 解析**：
- `.dvt`/`.dvo` 文件：读取前两行（`DV` + header_json），解析 `dog_provider` 字段。
- `.dvp`/`.dvst`/`.dvso`/`.dvsp`：不支持通过 header 解析（DVP 由底层处理，DVS 由子模型加载时解析）。

---

## 7. InferTiming 类（flow/runtime/ExecutionRuntime.cs）

```csharp
public static class InferTiming
{
    public static void BeginFlowRequest();
    public static void EndFlowRequest(double flowMs);
    public static void SetDirectRequest(double inferMs);
    public static void GetLast(out double dlcvInferMs, out double flowInferMs);
    public static List<FlowNodeTiming> GetLastFlowNodeTimings();
}
```

**`FlowNodeTiming` 结构**：
```csharp
public class FlowNodeTiming
{
    public int NodeId;        // 节点 ID
    public string NodeType;   // 节点类型
    public string NodeTitle;  // 节点标题
    public double ElapsedMs;  // 耗时（毫秒）
}
```

---

## 8. Utils 工具方法（Utils.cs 静态方法）

```csharp
public static class Utils
{
    // 释放所有模型
    public static void FreeAllModels();

    // 获取设备信息
    public static JObject GetDeviceInfo();

    // 获取 GPU 信息
    public static JObject GetGpuInfo();

    // 锁定 GPU 最大时钟
    public static void KeepMaxClock();

    // 加密狗查询
    public static JObject GetAllDogInfo();

    // OCR 推理（检测+识别级联）
    public static CSharpResult OcrInfer(Model detectModel, Model recognizeModel, Mat image);

    // ExtraInfo polyline 读写
    public static List<Point2d> GetExtraInfoPolyline(JObject extraInfo, string key = "polyline");
    public static void SetExtraInfoPolyline(JObject extraInfo, List<Point2d> polyline, string key = "polyline");

    // 可视化
    public static JArray ConvertToVisualizeFormat(CSharpResult result);
    public static List<Mat> VisualizeResults(List<Mat> images, CSharpResult result, Dictionary<string, object> properties = null);
}
```

---

## 9. 调用流程

### 9.1 普通模型

```csharp
using DlcvModules;
using OpenCvSharp;
using Newtonsoft.Json.Linq;

// 1. 加载
var model = new Model(@"C:\models\my_model.dvt", deviceId: 0);

// 2. 准备图像
Mat image = Cv2.ImRead("test.jpg", ImreadModes.Unchanged);
Mat rgb = new Mat();
Cv2.CvtColor(image, rgb, ColorConversionCodes.BGR2RGB);

// 3. 推理
var paramsJson = new JObject();
paramsJson["threshold"] = 0.5;
paramsJson["with_mask"] = true;
paramsJson["batch_size"] = 1;

CSharpResult result = model.Infer(rgb, paramsJson);

// 4. 解析结果
foreach (var sample in result.SampleResults)
{
    foreach (var obj in sample.Results)
    {
        Console.WriteLine($"{obj.CategoryName} score={obj.Score}");
    }
}

// 5. 释放
model.Dispose();
Utils.FreeAllModels();
```

### 9.2 流程图/DVS 模型

```csharp
// 1. 加载（与普通模型接口完全一致）
var model = new Model(@"C:\models\pipeline.dvst", deviceId: 0);

// 2. 推理
CSharpResult result = model.Infer(rgb, paramsJson);

// 3. 获取模型信息（含流程图节点信息）
JObject info = model.GetModelInfo();
Console.WriteLine(info.ToString());
```

---

## 10. 推理参数 JSON 字段

| 字段名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `threshold` | float | 0.5 | 置信度阈值 |
| `with_mask` | bool | true | 是否输出 mask |
| `batch_size` | int | 1 | 批量大小 |
| `device_id` | int | 构造时传入 | GPU 设备 ID（-1 表示 CPU） |

---

## 11. 图像预处理约定

1. **调用层责任**：读取图像后，将 BGR/BGRA 转换为 RGB（`COLOR_BGR2RGB` / `COLOR_BGRA2RGB`）。
2. **API 层责任**：按模型期望通道数（1 或 3）补齐或压缩通道，并统一位深到 `CV_8U`。
3. **输入约束**：`Model` 的 `Infer`/`InferBatch` 接受 `OpenCvSharp.Mat`，空图像或转换失败时抛出异常。

---

## 12. 错误处理约定

- 所有错误通过 C# 异常抛出（`Exception`、`ArgumentException`、`InvalidOperationException`、`FileNotFoundException` 等）。
- 底层 C API 返回的错误码封装在异常消息中。
- Flow 模式加载失败时，异常信息包含第一个失败模型的路径和底层错误。
- DVS 解包失败时抛出对应异常，包含具体错误步骤。

---

## 13. 工程结构

`DlcvCsharpApi` 是一个 .NET Framework 4.7.2 的 C# 类库工程，`OutputType` 为 `Library`，程序集名称为 `DlcvCsharpApi`，默认生成 `DlcvCsharpApi.dll`。项目定义了 `Debug|x64` 与 `Release|x64` 两个主要构建配置，`PlatformTarget` 为 `x64`，`LangVersion` 为 `7.3`。程序集版本为 `1.0.0.0`，`ComVisible` 为 `false`。

发布版输出路径为 `DlcvCsharpApi\bin\x64\Release\DlcvCsharpApi.dll`。

代码中实际使用的命名空间包括：

- `dlcv_infer_csharp`
- `DlcvModules`
- `sntl_admin_csharp`

### 13.1 源码目录结构

`DlcvCsharpApi` 当前源码按目录分层组织：

| 目录 | 当前内容 |
| --- | --- |
| `DlcvCsharpApi\` | 普通模型与外部绑定层：`Model.cs`、`Utils.cs`、`DataTypes.cs`、`DllLoader.cs`、`sntl_admin_csharp.cs` |
| `DlcvCsharpApi\flow\` | 流程入口与执行框架：`FlowGraphModel.cs`、`DvsModel.cs`、`GraphExecutor.cs` |
| `DlcvCsharpApi\flow\runtime\` | 流程运行时公共类型：`ExecutionRuntime.cs`、`ModuleRuntime.cs` |
| `DlcvCsharpApi\flow\modules\` | 流程模块实现：`Inputs.cs`、`Models.cs`、`Outputs.cs`、`Features.cs`、`SlidingWindow.cs`、`SlidingMerge.cs`、`PolyFilter.cs`、`ResultFilterRegion.cs`、`ResultCategoryOverride.cs`、`StrokeToPoints.cs`、`Templates.cs`、`Visualize.cs`、`MaskRleUtils.cs` |
| `DlcvCsharpApi\Properties\` | 程序集元数据：`AssemblyInfo.cs` |

### 13.2 依赖与运行组件

#### NuGet 依赖

| 包名 | 版本 |
| --- | --- |
| `Newtonsoft.Json` | `13.0.3` |
| `OpenCvSharp4` | `4.10.0.20241108` |
| `OpenCvSharp4.Extensions` | `4.10.0.20241108` |
| `OpenCvSharp4.runtime.win` | `4.10.0.20241108` |
| `System.Buffers` | `4.5.1` |
| `System.Memory` | `4.5.5` |
| `System.Numerics.Vectors` | `4.5.0` |
| `System.Runtime.CompilerServices.Unsafe` | `6.0.0` |

#### 托管配置

`app.config` 仅包含一条程序集绑定重定向：

- `System.Runtime.CompilerServices.Unsafe` 的 `oldVersion="0.0.0.0-6.0.0.0"` 重定向到 `newVersion="6.0.0.0"`

#### 原生组件与固定路径

| 组件 | 当前实现中的加载方式 |
| --- | --- |
| `dlcv_infer.dll` | Sentinel 版本；优先按系统搜索路径加载，失败后回退到 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer.dll` |
| `dlcv_infer_v.dll` | Virbox 版本；加载 DVT/DVO/DVR 模型前读取模型包 `header_json.dog_provider`，当 provider 为 `virbox` 时启用；回退路径为 `C:\dlcv\Lib\site-packages\dlcvpro_infer\dlcv_infer_v.dll` |
| `sntl_adminapi_windows_x64.dll` | 优先按系统搜索路径加载，失败后回退到 `C:\dlcv\bin\sntl_adminapi_windows_x64.dll` |
| `nvml.dll` | `Utils.GetGpuInfo()` 通过 `DllImport` 直接调用 |
| `DLCV Test.exe` | `Model` 的 DVP 模式固定从 `C:\dlcv\Lib\site-packages\dlcv_test\DLCV Test.exe` 启动后端服务 |
| `AIModelRPC.exe` | `Model` 的 RPC 模式优先从当前 AppDomain 目录查找，其次查找 `C:\dlcv\Lib\site-packages\dlcvpro_infer_csharp\AIModelRPC.exe` |

#### 运行模式

| 模式 | 触发条件 | 当前实现中的通信方式 |
| --- | --- | --- |
| DVT | 文件后缀既不是 `.dvp`，也不是 `.dvst`/`.dvso`/`.dvsp`，且 `rpc_mode=false` | `dlcv_infer.dll` / `dlcv_infer_v.dll` 导出的 C 接口 |
| DVP | 模型路径后缀为 `.dvp` | HTTP，固定服务地址 `http://127.0.0.1:9890` |
| DVS | 模型路径后缀为 `.dvst`、`.dvso` 或 `.dvsp` | `DvsModel` + `FlowGraphModel` |
| RPC | `rpc_mode=true` 且不属于 DVP/DVS 后缀 | 命名管道 `DlcvModelRpcPipe` + 共享内存 |

#### DVP 固定接口

`Model` 在 DVP 模式下使用以下固定接口：

- `GET /docs`
- `GET /version`
- `POST /load_model`
- `POST /get_model_info`
- `POST /api/inference`
- `POST /free_model`

#### RPC 固定协议

`Model` 在 RPC 模式下使用命名管道 `DlcvModelRpcPipe` 发送 JSON 指令，已实现的动作包括：

- `ping`
- `load_model`
- `get_model_info`
- `infer`
- `free_model`

图像数据通过共享内存传递，图像共享内存名格式为 `DlcvModelMmf_<token>`，mask 共享内存名格式为 `DlcvModelMask_<token>`。

---

## 14. 核心类

### 14.1 `Model`

`Model` 是基础推理封装类，实现了 `IDisposable`。

#### 公开面

`Model` 的公开面围绕四类能力组织：生命周期与状态包括空构造、带参构造、`EnableConsoleLog`、`modelIndex`、`OwnModelIndex`、`IsDvpMode`、`FreeModel()`、`Dispose()`；缓存与批量元信息包括 `GetCachedModelInfo()`、`GetCachedMaxShape()`、`GetMaxBatchSize()`、`GetResolvedSubModelBatchItems()`、`ClearModelCache()`；模型信息查询使用 `GetModelInfo()`；推理入口使用 `Infer()`、`InferBatch()` 与 `InferOneOutJson()`。

#### 模型加载规则

- `.dvp` 后缀进入 DVP 模式。
- `.dvst`、`.dvso`、`.dvsp` 后缀进入 DVS 模式。
- 其余后缀在 `rpc_mode=true` 时进入 RPC 模式。
- 其余情况进入 DVT 模式。

#### 模型缓存

当 `enableCache=true` 时，缓存键由模型绝对路径的小写规范化值、`device_id` 和运行模式标识 `dvp` / `dvs` / `rpc` / `dvt` 组成；命中后直接复用 `modelIndex`，`ClearModelCache()` 会清空静态模型缓存与加载中集合。

#### 当前实现中的模式差异

| 模式 | 当前行为 |
| --- | --- |
| DVT | 通过 `dlcv_load_model`、`dlcv_get_model_info`、`dlcv_infer`、`dlcv_free_model_result`、`dlcv_free_model` 工作 |
| DVP | 自动检查后端服务；服务不可用时启动 `DLCV Test.exe --keep_alive`；推理请求固定附带 `return_polygon=true` |
| DVS | 内部创建 `DlcvModules.DvsModel`；`GetModelInfo()` 返回流程 JSON，并附加 `loaded_model_meta` 与按模型文件名索引的 `model_info` |
| RPC | 自动启动 `AIModelRPC.exe`；图像通过共享内存传输；结果中的 mask 可通过共享内存回读 |

#### 输入与输出

- 推理前会根据模型信息中解析出的输入通道数完成图像归一化：三通道模型统一转为 `RGB`，单通道模型统一转为灰度；输入若为 16 位、浮点或带符号整型深度，会先转换为 8 位深度。
- `Infer()` 与 `InferBatch()` 统一返回 `Utils.CSharpResult`。
- `Model.InferOneOutJson()` 返回单图 JSON 结果数组。
- DVP 模式下可由 `polygon` 反算 `bbox` 和局部 `Mask`。
- DVT / RPC 模式下 `mask` 优先从共享内存或 `mask_ptr` 读取。
- 无 mask 时输出 `{ "height": -1, "mask_ptr": 0, "width": -1 }`。
- 无监督加速模型当前按实例分割结果契约返回：`task_type=实例分割` 且 `origin_task_type=us`。
- 无监督结果按 `bbox + mask` 的实例项进入 C#，当无异常区域时对应样本 `results` 为空数组。

### 14.2 `FlowGraphModel`

`FlowGraphModel` 是流程图推理封装类，实现了 `IDisposable`。当前实现文件为 `DlcvCsharpApi\flow\FlowGraphModel.cs`。

共享的 Flow 节点分类、统一输入输出字段、模板对象与计时口径见 [模块、流程与模型推理标准文档](模块、流程与模型推理标准文档.md)。

C# 侧公开接口为 `Load()`、`GetLoadedModelMeta()`、`GetModelInfo()`、`Infer()`、`InferBatch()`、`InferOneOutJson()`、`Benchmark()`、`Dispose()`；执行时会把前端图像、`device_id` 和 `return_json_emit_poly` 写入 `ExecutionContext`，并把 `result_list` 转为结构化结果或 JSON 输出。

### 14.3 `DvsModel`

`DvsModel` 继承 `FlowGraphModel`，用于加载 `.dvst`、`.dvso`、`.dvsp` 文件。当前实现文件为 `DlcvCsharpApi\flow\DvsModel.cs`。

C# 侧额外处理 `DV\n` 文件头校验、归档解包、`pipeline.json` 中 `model_path` 重写，以及临时目录清理。

### 14.4 `DllLoader`

`DllLoader` 是 provider-aware 原生入口分发器。`ForProvider(DogProvider)` 按 provider 返回对应 loader：`sentinel` 加载 `dlcv_infer.dll`，`virbox` 加载 `dlcv_infer_v.dll`。

`ForModel(string)` 的加载策略：
- 先通过 `ModelHeaderProviderResolver.TryResolveExplicitProvider` 判断模型头是否**明确指定**了 `dog_provider`。
- 若**明确指定**（`sentinel` 或 `virbox`），则校验对应加密狗是否存在；不存在时抛出异常，不静默 fallback。
- 若**未指定**（旧模型或省略该字段），则调用 `AutoDetectProvider()` 自动检测当前插入的加密狗，按 **Sentinel 优先、Virbox 第二** 的顺序选择 Provider；检测不到任何狗时，默认使用 Sentinel。

`Instance`（兼容旧代码的单例）在首次创建时同样调用 `AutoDetectProvider()`，而非硬编码 Sentinel。

每个 `Model` 实例在加载时绑定自己的 `_dllLoader`，后续 `GetModelInfoDvt`、`InferInternalDvt`、`FreeModel` 都走该 loader。`Utils` 的 `FreeAllModels`、`GetDeviceInfo`、`KeepMaxClock` 遍历所有已创建 loader 执行。

### 14.5 `sntl_admin_csharp`

`sntl_admin_csharp` 对外提供状态枚举 `SntlAdminStatus`、原生加载器 `SNTLDllLoader`、运行时访问类 `SNTL`、工具类 `SNTLUtils`、`DogProvider` 枚举、`DogInfo` 与 `DogUtils`。`SNTL` 负责建立上下文并提供 `Get()`、`GetSntlInfo()`、`GetDeviceList()`、`GetFeatureList()`、`Dispose()`；`SNTLUtils` 提供静态的 Sentinel 设备列表与特征列表查询，不再自动回退到 Virbox。`Virbox` 提供独立的 Virbox 设备列表与特征列表查询。`DogUtils` 提供 `GetSentinelInfo()`、`GetVirboxInfo()`、`GetAvailableProviders()` 与 `GetAllDogInfo()`，用于同时查询两类加密狗信息。OpenIVS 不解密模型包内 `dlcv.json`。

---

## 15. Flow 的 C# 实现入口

### 15.1 执行框架

`ExecutionContext`、`ModuleRegistry`、`GlobalDebug`、`InferTiming`、`TransformationState`、`ModuleImage`、`ModuleIO`、`ModuleChannel` 位于 `DlcvCsharpApi\flow\runtime\ExecutionRuntime.cs` 与 `DlcvCsharpApi\flow\runtime\ModuleRuntime.cs`。`BaseModule` / `BaseInputModule` 提供模块基类。`GraphExecutor` 位于 `DlcvCsharpApi\flow\GraphExecutor.cs`，负责节点排序、链路路由、标量注入、`NormalizeBboxProperties()` 和模型节点预加载；`LoadModels()` 仅对 `BaseModelModule` 调用 `LoadModel()`，并把加载元信息写入 `ExecutionContext.loaded_model_meta`。

### 15.2 模块实现文件

当前模块实现代码位于 `DlcvCsharpApi\flow\modules\`，主要文件包括 `Inputs.cs`、`Models.cs`、`Outputs.cs`、`Features.cs`、`SlidingWindow.cs`、`SlidingMerge.cs`、`PolyFilter.cs`、`ResultFilterRegion.cs`、`ResultCategoryOverride.cs`、`StrokeToPoints.cs`、`Templates.cs`、`Visualize.cs`。

### 15.3 C# 层补充约定

- `ReturnJson` 会把结果聚合到 `ExecutionContext.frontend_json` 的 `last` 与 `by_node`。
- `VisualizeOnOriginal` 在原图上绘制 mask、轮廓、框和文本。
- `SlidingWindow` 负责切图并写入 `sliding_meta`；`SlidingMergeResults` 负责把结果映射回原图并合并。
- `PolyFilter` 会更新 `extra_info.polyline` 以及相关 `metadata`。
- 读盘和写盘遵循 OpenCV 的 BGR 语义；调用方负责把三/四通道颜色图整理为 RGB；`Model` 与 `FlowGraphModel` 入口会按模型输入自动做最小必要的通道规整，例如把灰度图补成 RGB、或把三/四通道图压成灰度，但不负责 BGR/BGRA 到 RGB 的颜色顺序转换。
- 当前显式使用的标量键包括 `filename`、`has_positive`、`ok`、`detail`、`kept_count`、`removed_count`。
- 模板相关类型为 `SimpleOcrItem`、`SimpleTemplate` 和 `SimpleTemplateMatchDetail`。
