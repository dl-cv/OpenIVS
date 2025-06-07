# DLCV HTTP API - C# 客户端库

这是一个与 `DlcvCsharpApi` 完全兼容的 HTTP 版本客户端库。它提供了完全相同的 API 接口，但底层使用 HTTP 协议与服务器通信，而不是直接调用 DLL。

## 主要特性

- **完全兼容的接口**: 与 `DlcvCsharpApi.Model` 保持相同的 API 接口
- **透明的 HTTP 通信**: 用户无需关心底层的 HTTP 实现细节
- **相同的数据结构**: 使用相同的 `Utils.CSharpResult` 等数据结构
- **参数兼容性**: 支持原有的构造函数参数（如 `device_id` 会被自动忽略）

## 使用方法

### 基本用法

```csharp
using DlcvDvpHttpApi;
using dlcv_infer_csharp;

// 1. 创建模型实例（与原API完全一致）
Model model = new Model(@"C:\path\to\your\model.dlcv");

// 2. 进行推理
Utils.CSharpResult result = model.Infer(@"C:\path\to\your\image.jpg");

// 3. 处理结果
if (result.SampleResults != null && result.SampleResults.Count > 0)
{
    var sampleResult = result.SampleResults[0];
    Console.WriteLine($"检测到 {sampleResult.Results.Count} 个对象");
    
    foreach (var obj in sampleResult.Results)
    {
        Console.WriteLine($"类别: {obj.CategoryName}, 置信度: {obj.Score:F2}");
    }
}

// 4. 释放资源
model.Dispose();
```

### 兼容性用法

```csharp
// 原来的构造函数调用方式完全兼容
Model model1 = new Model(modelPath);
Model model2 = new Model(modelPath, device_id); // device_id 会被忽略

// 所有原有的方法调用都支持
var modelInfo = model.GetModelInfo();
var result = model.Infer(imagePath);
var batchResult = model.InferBatch(imageList);
model.FreeModel();
```

### 自定义服务器地址

```csharp
// 可以指定自定义的服务器地址
Model model = new Model(modelPath, "http://192.168.1.100:9890");

// 或者使用带device_id的版本
Model model = new Model(modelPath, device_id, "http://192.168.1.100:9890");
```

## API 接口

### 构造函数

- `Model(string modelPath, string serverUrl = "http://127.0.0.1:9890")`
- `Model(string modelPath, int device_id, string serverUrl = "http://127.0.0.1:9890")`

### 主要方法

- `JObject GetModelInfo()` - 获取模型信息
- `Utils.CSharpResult Infer(Mat image, JObject params_json = null)` - 对Mat图像进行推理
- `Utils.CSharpResult Infer(string imagePath, JObject params_json = null)` - 对图像文件进行推理
- `Utils.CSharpResult InferBatch(List<Mat> imageList, JObject params_json = null)` - 批量推理
- `void FreeModel()` - 释放模型资源
- `void Dispose()` - 释放所有资源

## 服务器要求

此客户端库需要一个兼容的 DLCV HTTP 服务器运行在指定的地址上。服务器需要提供以下端点：

- `POST /load_model` - 加载模型
- `GET /model_info/{model_index}` - 获取模型信息
- `POST /infer` - 进行推理
- `POST /free_model` - 释放模型

## 注意事项

1. **设备ID参数**: 在HTTP版本中，`device_id` 参数会被忽略，因为设备管理由服务器端处理
2. **网络连接**: 确保客户端能够访问指定的HTTP服务器地址
3. **超时设置**: 默认HTTP超时时间为5分钟，适合大部分推理任务
4. **资源管理**: 建议使用 `using` 语句或手动调用 `Dispose()` 方法来确保资源正确释放

## 错误处理

所有API调用都会抛出适当的异常，建议使用try-catch块来处理可能的网络错误或服务器错误：

```csharp
try
{
    Model model = new Model(modelPath);
    var result = model.Infer(imagePath);
    // 处理结果...
}
catch (Exception ex)
{
    Console.WriteLine($"操作失败: {ex.Message}");
}
``` 