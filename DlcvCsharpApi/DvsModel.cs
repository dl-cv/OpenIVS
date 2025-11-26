using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;

namespace DlcvModules
{
    /// <summary>
    /// 支持加载 .dvst/dvso/dvsp 格式（包含 pipeline.json + 多个 .dvt/.dvs/.dvp 模型）
    /// 解包 -> 临时存储 -> 加载 -> 清理
    /// </summary>
    public class DvsModel : FlowGraphModel
    {
        private string _dvsPath;
        // 临时文件夹路径，用于清理
        private string _tempDir = null;

        public new JObject Load(string dvsPath, int deviceId = 0)
        {
            if (string.IsNullOrWhiteSpace(dvsPath)) throw new ArgumentException("文件路径为空", nameof(dvsPath));
            if (!File.Exists(dvsPath)) throw new FileNotFoundException("文件不存在", dvsPath);

            _dvsPath = dvsPath;

            // 1. 准备临时目录
            _tempDir = Path.Combine(Path.GetTempPath(), "DlcvDvs_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            JObject pipelineJson = null;
            var extractedFiles = new Dictionary<string, string>(); // filename -> fullPath
            // 映射：原始文件名 -> 临时文件路径（Guid命名）
            var fileNameToTempPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (FileStream fs = new FileStream(dvsPath, FileMode.Open, FileAccess.Read))
                {
                    // 2. 校验头部 "DV\n"
                    byte[] magic = new byte[3];
                    if (fs.Read(magic, 0, 3) != 3 || magic[0] != 'D' || magic[1] != 'V' || magic[2] != '\n')
                    {
                        throw new InvalidDataException("文件格式错误：缺少 DV 头部");
                    }

                    // 3. 读取 JSON 头行
                    List<byte> lineBytes = new List<byte>();
                    int b;
                    while ((b = fs.ReadByte()) != -1)
                    {
                        if (b == '\n') break;
                        lineBytes.Add((byte)b);
                    }
                    string headerStr = Encoding.UTF8.GetString(lineBytes.ToArray());
                    JObject header = JObject.Parse(headerStr);

                    JArray fileList = header["file_list"] as JArray;
                    JArray fileSize = header["file_size"] as JArray;

                    if (fileList == null || fileSize == null || fileList.Count != fileSize.Count)
                    {
                        throw new InvalidDataException("文件头信息损坏：file_list 或 file_size 缺失/不匹配");
                    }

                    // 4. 遍历解包
                    for (int i = 0; i < fileList.Count; i++)
                    {
                        string fileName = fileList[i].ToString();
                        long size = (long)fileSize[i];

                        // 如果是 pipeline.json，直接读取到内存
                        if (fileName == "pipeline.json")
                        {
                            byte[] data = new byte[size];
                            if (fs.Read(data, 0, (int)size) != size) throw new EndOfStreamException("文件读取意外结束");
                            string jsonText = Encoding.UTF8.GetString(data);
                            pipelineJson = JObject.Parse(jsonText);
                        }
                        else
                        {
                            // 其他文件（.dvt）写入临时目录
                            // 关键修改：为了避免中文文件名导致的底层库打开失败，我们将临时文件重命名为纯英文（Guid）
                            // 保留原始扩展名以便识别
                            string ext = Path.GetExtension(fileName);
                            if (string.IsNullOrEmpty(ext)) ext = ".tmp";
                            string safeName = Guid.NewGuid().ToString("N") + ext;
                            string targetPath = Path.Combine(_tempDir, safeName);
                            
                            using (FileStream outFs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                            {
                                byte[] buffer = new byte[8192];
                                long remaining = size;
                                while (remaining > 0)
                                {
                                    int read = fs.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                                    if (read == 0) throw new EndOfStreamException("文件读取意外结束");
                                    outFs.Write(buffer, 0, read);
                                    remaining -= read;
                                }
                            }
                            extractedFiles[fileName] = targetPath;
                            fileNameToTempPath[fileName] = targetPath;
                        }
                    }
                }

                if (pipelineJson == null) throw new InvalidDataException("未找到 pipeline.json");
                // 5. 修改 Pipeline 中的 model_path
                var nodesToken = pipelineJson["nodes"] as JArray;
                if (nodesToken == null) throw new InvalidOperationException("Pipeline 缺少 nodes 数组");

                foreach (var node in nodesToken)
                {
                    if (node["properties"] is JObject props)
                    {
                        if (props.ContainsKey("model_path"))
                        {
                            string originalPath = props["model_path"].ToString();
                            
                            // 尝试匹配策略：
                            // 1. 原始路径是否就是文件名且在映射中？
                            if (fileNameToTempPath.ContainsKey(originalPath))
                            {
                                props["model_path"] = fileNameToTempPath[originalPath];
                            }
                            else
                            {
                                // 2. 尝试提取原始路径的文件名部分进行匹配
                                try 
                                {
                                    string justName = Path.GetFileName(originalPath);
                                    if (!string.IsNullOrEmpty(justName) && fileNameToTempPath.ContainsKey(justName))
                                    {
                                        props["model_path"] = fileNameToTempPath[justName];
                                    }
                                }
                                catch {}
                            }
                        }
                    }
                }

                // 6. 复用 FlowGraphModel 的核心加载逻辑（从已经修改好的 pipelineJson 中加载）
                var report = LoadFromRoot(pipelineJson, deviceId);
                return report;
            }
            catch (Exception)
            {
                // 加载失败，立即清理（如果成功加载，finally块中不清理？不，题目要求加载完即删除）
                // 但如果 MainProcess 正在占用文件句柄，这里删除会失败。
                // 假设底层库是 read-all-bytes 或者是 Copy-to-Memory，则可以删除。
                // 如果是 File Mapping，则不能删除。
                // 根据用户指示：“加载完之后删除此临时目录即可”，我们尝试删除。
                throw;
            }
            finally
            {
                // 8. 清理临时文件
                CleanupTemp();
            }
        }

        private void CleanupTemp()
        {
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DvsModel] Warning: Failed to cleanup temp dir {_tempDir}: {ex.Message}");
                }
                _tempDir = null;
            }
        }

        /// <summary>
        /// DVS 批量推理：与其它模式保持一致，一张输入图像对应一个 SampleResult。
        /// 这里复用基类的单张推理逻辑，避免再次实现 JSON 解析代码。
        /// </summary>
        public new Utils.CSharpResult InferBatch(List<Mat> imageList, JObject paramsJson = null)
        {
            if (imageList == null || imageList.Count == 0)
                throw new ArgumentException("输入图像列表为空", nameof(imageList));

            if (!IsLoaded) throw new InvalidOperationException("模型未加载");

            var samples = new List<Utils.CSharpSampleResult>();
            for (int i = 0; i < imageList.Count; i++)
            {
                var single = base.Infer(imageList[i], paramsJson);
                if (single.SampleResults != null && single.SampleResults.Count > 0)
                {
                    // 按照其它模式的约定：一张图像使用第一个 SampleResult
                    samples.Add(single.SampleResults[0]);
                }
                else
                {
                    samples.Add(new Utils.CSharpSampleResult(new List<Utils.CSharpObjectResult>()));
                }
            }

            return new Utils.CSharpResult(samples);
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            CleanupTemp();
        }

        ~DvsModel()
        {
            Dispose(false);
        }
    }
}