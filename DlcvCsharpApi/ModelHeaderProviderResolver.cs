using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using sntl_admin_csharp;

namespace dlcv_infer_csharp
{
    public static class ModelHeaderProviderResolver
    {
        public static DogProvider ResolveProvider(string modelPath)
        {
            if (TryResolveExplicitProvider(modelPath, out DogProvider provider))
            {
                return provider;
            }
            return DogProvider.Sentinel;
        }

        /// <summary>
        /// 尝试从模型头解析明确指定的 dog_provider。
        /// 返回 true 表示模型头中明确写入了 dog_provider；
        /// 返回 false 表示模型头未指定 dog_provider（调用方应走自动检测逻辑）。
        /// </summary>
        public static bool TryResolveExplicitProvider(string modelPath, out DogProvider provider)
        {
            provider = DogProvider.Sentinel;
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("模型路径不能为空", nameof(modelPath));
            }

            string extension = Path.GetExtension(modelPath).ToLower();
            if (extension == ".dvp")
            {
                throw new NotSupportedException("DVP 模式不通过 header 解析 provider");
            }
            if (extension == ".dvst" || extension == ".dvso" || extension == ".dvsp")
            {
                throw new NotSupportedException("DVS 模式在子模型加载时解析 header provider");
            }

            using (FileStream fs = new FileStream(modelPath, FileMode.Open, FileAccess.Read))
            using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
            {
                string header = reader.ReadLine();
                if (header != "DV")
                {
                    throw new Exception("模型文件格式错误：缺少 DV 头");
                }

                string headerJsonStr = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(headerJsonStr))
                {
                    throw new Exception("模型文件格式错误：缺少 header_json");
                }

                JObject headerJson = JObject.Parse(headerJsonStr);
                if (!headerJson.ContainsKey("dog_provider"))
                {
                    return false;
                }

                string p = headerJson["dog_provider"]?.ToString()?.ToLower() ?? "";
                if (p == "sentinel")
                {
                    provider = DogProvider.Sentinel;
                    return true;
                }
                if (p == "virbox")
                {
                    provider = DogProvider.Virbox;
                    return true;
                }

                throw new Exception($"invalid dog provider in header_json: {p}");
            }
        }
    }
}
