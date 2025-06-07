using System;
using DlcvDvpHttpApi;
using dlcv_infer_csharp;

namespace DlcvDvpHttpApi
{
    /// <summary>
    /// 使用示例：演示如何使用HTTP版本的Model类
    /// </summary>
    public class Example
    {
        public static void Main()
        {
            // 示例代码 - 请勿运行，仅供参考
            
            // 1. 创建模型实例（与原CsharpApi完全兼容的接口）
            Model model = new Model(@"C:\path\to\your\model.dlcv");
            
            // 或者使用带device_id的构造函数（device_id会被忽略）
            // Model model = new Model(@"C:\path\to\your\model.dlcv", 0);
            
            try
            {
                // 2. 获取模型信息
                var modelInfo = model.GetModelInfo();
                Console.WriteLine("模型信息：" + Utils.jsonToString(modelInfo));
                
                // 3. 进行推理
                Utils.CSharpResult result = model.Infer(@"C:\path\to\your\image.jpg");
                
                // 4. 处理结果
                if (result.SampleResults != null && result.SampleResults.Count > 0)
                {
                    var sampleResult = result.SampleResults[0];
                    Console.WriteLine($"检测到 {sampleResult.Results.Count} 个对象:");
                    
                    foreach (var obj in sampleResult.Results)
                    {
                        Console.WriteLine(obj.ToString());
                    }
                }
            }
            finally
            {
                // 5. 释放模型资源
                model.Dispose(); // 或者 model.FreeModel();
            }
        }
    }
} 