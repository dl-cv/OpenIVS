# C# API 接口文档

**文档定位**：记录 `DlcvCsharpApi` 的精确类型、方法签名与调用约定。所有内容以当前源码实现为准。

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
| `InferTiming.cs` | `InferTiming` 类，推理计时 |

命名空间：
- `DlcvModules`：`Model`、`Utils`、`FlowGraphModel`、`DvsModel`、`InferTiming`
- `dlcv_infer_csharp`：`DllLoader`
- `sntl_admin_csharp`：`DogUtils`、`DogProvider`（加密狗工具，C# 同名封装）

---

## 2. 核心结果类型（Utils.cs）

### 2.1 CSharpObjectResult

```csharp
public class CSharpObjectResult
{
    public int CategoryId;           // 类别 ID
    public string CategoryName;      // 类别名称
    public float Score;              // 置信度
    public float Area;               // 面积
    public List<double> Bbox;        // 水平框: [x, y, w, h]; 旋转框: [cx, cy, w, h]
    public bool WithMask;            // 是否含 mask
    public Mat Mask;                 // OpenCV mask（CV_8UC1），未检出时可能为空 Mat
    public bool WithBbox;            // 是否含 bbox
    public bool WithAngle;           // 是否含旋转角度
    public float Angle;              // 旋转角度（弧度），-100 表示无效
    public JObject ExtraInfo;        // 额外信息（polyline 等）

    public CSharpObjectResult(
        int categoryId, string categoryName, float score, float area,
        List<double> bbox, bool withMask, Mat mask, bool withBbox,
        bool withAngle, float angle, JObject extraInfo);
}
```

**字段约束**：
- `Bbox` 长度约定：水平框 ≥4（`x,y,w,h`），旋转框 ≥4（`cx,cy,w,h`，`angle` 单独字段）。
- `Angle` 有效值范围：`> -99.0f` 视为有效；`-100.0f` 视为无效。
- `Mask` 为空时（`Mask == null || Mask.Empty()`），`WithMask` 应为 `false`。
- `ExtraInfo` 可包含 `polyline`（通过 `Utils.GetExtraInfoPolyline` / `Utils.SetExtraInfoPolyline` 读写）。

### 2.2 CSharpSampleResult

```csharp
public class CSharpSampleResult
{
    public List<CSharpObjectResult> Results;

    public CSharpSampleResult();
    public CSharpSampleResult(List<CSharpObjectResult> results);
}
```

### 2.3 CSharpResult

```csharp
public class CSharpResult
{
    public List<CSharpSampleResult> SampleResults;

    public CSharpResult();
    public CSharpResult(List<CSharpSampleResult> sampleResults);
}
```

**结构层级**：`CSharpResult → List<CSharpSampleResult> → List<CSharpObjectResult>`

---

## 3. Model 类（Model.cs）

### 3.1 构造与加载

```csharp
public class Model : IDisposable
{
    public Model(string modelPath, int deviceId = 0);
}
```

**构造函数行为**：
1. 若路径以 `.dvst` / `.dvso` / `.dvsp` 结尾 → 进入 Flow/DVS 模式，实例化 `FlowGraphModel` 或 `DvsModel`。
2. 否则 → 普通模型模式，通过 `DllLoader` 调用底层 C API。
3. 构造失败时抛出 `Exception`（底层错误信息封装在异常消息中）。
4. 加载完成后可通过 `Loaded` 属性判断状态。

### 3.2 属性

```csharp
public bool Loaded { get; }           // 模型是否成功加载
public int ModelIndex { get; }        // 底层模型索引（普通模型）
public bool IsFlowGraphModel { get; } // 是否为流程图模式
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

## 7. InferTiming 类（InferTiming.cs）

```csharp
public static class InferTiming
{
    public static void BeginFlowRequest();
    public static void EndFlowRequest(double flowMs);
    public static void SetSdkTiming(double sdkMs);
    public static double GetLastSdkMs();
    public static double GetLastFlowMs();
    public static void SetNodeTimings(List<FlowNodeTiming> timings);
    public static List<FlowNodeTiming> GetLastNodeTimings();
    public static void Reset();
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
    // 释放底层 C API 返回的 JSON 字符串内存
    public static void FreeResult(IntPtr ptr);

    // 释放底层 C API 返回的模型结果内存
    public static void FreeModelResult(IntPtr ptr);

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

    // ExtraInfo polyline 读写
    public static List<Point2d> GetExtraInfoPolyline(JObject extraInfo);
    public static void SetExtraInfoPolyline(JObject extraInfo, List<Point2d> polyline);
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

*本文档只记录当前源码实现。如需了解编译、运行或测试程序，参见对应测试程序文档。*
