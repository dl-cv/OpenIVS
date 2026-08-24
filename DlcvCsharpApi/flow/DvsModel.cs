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
            return LoadCore(dvsPath, null, null, deviceId);
        }

        public JObject LoadFromModelBindings(
            string sourcePath,
            JObject savedPipeline,
            JArray modelBindings,
            int deviceId)
        {
            if (savedPipeline == null) throw new ArgumentNullException(nameof(savedPipeline));
            if (modelBindings == null) throw new ArgumentNullException(nameof(modelBindings));

            var bindings = new Dictionary<int, int>();
            foreach (var item in modelBindings)
            {
                var binding = item as JObject;
                if (binding == null || binding["node_id"] == null || binding["model_index"] == null)
                    throw new InvalidDataException("流程模型绑定格式无效");

                int nodeId;
                int modelIndex;
                try
                {
                    nodeId = binding["node_id"].Value<int>();
                    modelIndex = binding["model_index"].Value<int>();
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("流程模型绑定格式无效", ex);
                }
                if (nodeId < 0 || modelIndex < 0) throw new InvalidDataException("流程模型绑定索引无效");
                bindings[nodeId] = modelIndex;
            }
            if (bindings.Count == 0) throw new InvalidDataException("流程模型绑定为空");
            return LoadCore(sourcePath, savedPipeline, bindings, deviceId);
        }

        private JObject LoadCore(
            string dvsPath,
            JObject savedPipeline,
            Dictionary<int, int> modelBindings,
            int deviceId)
        {
            if (string.IsNullOrWhiteSpace(dvsPath)) throw new ArgumentException("文件路径为空", nameof(dvsPath));
            if (!File.Exists(dvsPath)) throw new FileNotFoundException("文件不存在", dvsPath);

            _dvsPath = dvsPath;

            // 1. 准备临时目录
            _tempDir = Path.Combine(Path.GetTempPath(), "DlcvDvs_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            JObject pipelineJson = savedPipeline != null ? (JObject)savedPipeline.DeepClone() : null;
            HashSet<string> borrowedModelFiles = modelBindings != null
                ? CollectModelArchiveNames(pipelineJson)
                : null;
            // 映射：原始文件名 -> 临时文件路径（Guid命名）
            var fileNameToTempPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Dictionary<int, Model> modelsByIndex = null;
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
                        if (fileName.Equals("pipeline.json", StringComparison.OrdinalIgnoreCase))
                        {
                            if (pipelineJson == null)
                            {
                                byte[] data = ReadExact(fs, size);
                                string jsonText = Encoding.UTF8.GetString(data);
                                pipelineJson = JObject.Parse(jsonText);
                            }
                            else
                            {
                                SkipExact(fs, size);
                            }
                        }
                        else if (borrowedModelFiles != null &&
                                 (borrowedModelFiles.Contains(fileName) || borrowedModelFiles.Contains(Path.GetFileName(fileName))))
                        {
                            SkipExact(fs, size);
                        }
                        else
                        {
                            // 其他文件（.dvt/.dvs/.dvo/.dvr 等）写入临时目录
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
                            fileNameToTempPath[fileName] = targetPath;
                        }
                    }
                }

                if (pipelineJson == null) throw new InvalidDataException("未找到 pipeline.json");
                JObject registrationPipeline = (JObject)pipelineJson.DeepClone();
                // 5. 更新流程中的模型引用
                var nodesToken = pipelineJson["nodes"] as JArray;
                if (nodesToken == null) throw new InvalidOperationException("Pipeline 缺少 nodes 数组");

                if (modelBindings == null)
                {
                    RewriteModelPaths(nodesToken, fileNameToTempPath);
                    modelsByIndex = LoadUniqueModels(nodesToken, deviceId);
                }
                else
                {
                    ApplyModelBindings(nodesToken, modelBindings);
                    modelsByIndex = CreateBorrowedModels(modelBindings);
                }

                JArray resolvedBindings = BuildResolvedModelBindings(nodesToken, modelsByIndex);

                // 6. 复用 FlowGraphModel 的核心加载逻辑（从已经修改好的 pipelineJson 中加载）
                var report = LoadFromRoot(pipelineJson, deviceId, modelsByIndex, registrationPipeline);
                SetModelBindings(resolvedBindings);
                modelsByIndex = null;
                return report;
            }
            finally
            {
                DisposeModels(modelsByIndex);
                // 8. 清理临时文件
                CleanupTemp();
            }
        }

        private static void RewriteModelPaths(JArray nodes, Dictionary<string, string> fileNameToTempPath)
        {
            foreach (var node in nodes)
            {
                if (!IsModelNode(node, out int nodeId)) continue;
                var props = EnsureProperties(node);
                if (props["model_path"] == null) continue;

                string originalPath = props["model_path"].ToString();
                props["model_path_original"] = originalPath;
                props["model_name"] = GetModelName(originalPath);
                string tempPath;
                if (fileNameToTempPath.TryGetValue(originalPath, out tempPath))
                {
                    props["model_path"] = tempPath;
                    continue;
                }

                string fileName = Path.GetFileName(originalPath);
                if (!string.IsNullOrWhiteSpace(fileName) && fileNameToTempPath.TryGetValue(fileName, out tempPath))
                {
                    props["model_path"] = tempPath;
                    continue;
                }
                throw new InvalidDataException("流程模型文件未找到: " + originalPath);
            }
        }

        private static byte[] ReadExact(Stream stream, long size)
        {
            if (size < 0 || size > int.MaxValue) throw new InvalidDataException("文件大小无效");
            byte[] data = new byte[(int)size];
            int offset = 0;
            while (offset < data.Length)
            {
                int read = stream.Read(data, offset, data.Length - offset);
                if (read <= 0) throw new EndOfStreamException("文件读取意外结束");
                offset += read;
            }
            return data;
        }

        private static void SkipExact(Stream stream, long size)
        {
            byte[] buffer = new byte[8192];
            long remaining = size;
            while (remaining > 0)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                if (read <= 0) throw new EndOfStreamException("文件读取意外结束");
                remaining -= read;
            }
        }

        private static HashSet<string> CollectModelArchiveNames(JObject pipeline)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nodes = pipeline != null ? pipeline["nodes"] as JArray : null;
            if (nodes == null) return names;
            foreach (var node in nodes)
            {
                if (!IsModelNode(node, out int nodeId)) continue;
                var properties = (node as JObject)?["properties"] as JObject;
                string modelPath = properties?["model_path"]?.ToString();
                if (string.IsNullOrWhiteSpace(modelPath)) continue;
                names.Add(modelPath);
                try
                {
                    string fileName = Path.GetFileName(modelPath);
                    if (!string.IsNullOrWhiteSpace(fileName)) names.Add(fileName);
                }
                catch
                {
                }
            }
            return names;
        }

        private static Dictionary<int, Model> LoadUniqueModels(JArray nodes, int deviceId)
        {
            var modelsByPath = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
            var modelsByIndex = new Dictionary<int, Model>();
            try
            {
                foreach (var node in nodes)
                {
                    if (!IsModelNode(node, out int nodeId)) continue;
                    var props = EnsureProperties(node);
                    string modelPath = props["model_path"] != null ? props["model_path"].ToString() : null;
                    if (string.IsNullOrWhiteSpace(modelPath)) throw new InvalidDataException("流程模型路径为空");

                    int nodeDeviceId = ReadDeviceId(props, deviceId);
                    string key = NormalizeModelPath(modelPath) + "|" + nodeDeviceId;
                    Model model;
                    if (!modelsByPath.TryGetValue(key, out model))
                    {
                        model = new Model(modelPath, nodeDeviceId, false, false);
                        Model existingModel;
                        if (modelsByIndex.TryGetValue(model.modelIndex, out existingModel))
                        {
                            if (!ReferenceEquals(existingModel, model))
                            {
                                model.Dispose();
                            }
                            model = existingModel;
                        }
                        else
                        {
                            modelsByIndex.Add(model.modelIndex, model);
                        }
                        modelsByPath.Add(key, model);
                    }
                    props["model_index"] = model.modelIndex;
                }
                return modelsByIndex;
            }
            catch
            {
                DisposeModels(modelsByIndex);
                throw;
            }
        }

        private static Dictionary<int, Model> CreateBorrowedModels(Dictionary<int, int> modelBindings)
        {
            var models = new Dictionary<int, Model>();
            foreach (var binding in modelBindings)
            {
                if (models.ContainsKey(binding.Value)) continue;
                models.Add(binding.Value, new Model { modelIndex = binding.Value, OwnModelIndex = false });
            }
            return models;
        }

        private static JArray BuildResolvedModelBindings(JArray nodes, Dictionary<int, Model> modelsByIndex)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (modelsByIndex == null) throw new ArgumentNullException(nameof(modelsByIndex));

            var bindings = new JArray();
            var nodeIds = new HashSet<int>();
            foreach (var node in nodes)
            {
                if (!IsModelNode(node, out int nodeId)) continue;
                if (!nodeIds.Add(nodeId))
                    throw new InvalidDataException("流程模型节点 ID 重复: " + nodeId);

                JObject properties = EnsureProperties(node);
                if (properties["model_index"] == null)
                    throw new InvalidDataException("流程模型节点缺少 model_index: " + nodeId);
                int modelIndex;
                try
                {
                    modelIndex = properties["model_index"].Value<int>();
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("流程模型节点 model_index 无效: " + nodeId, ex);
                }
                if (modelIndex < 0 || !modelsByIndex.ContainsKey(modelIndex))
                    throw new InvalidDataException("流程模型节点 index 未加载: " + modelIndex);

                bindings.Add(new JObject
                {
                    ["node_id"] = nodeId,
                    ["model_index"] = modelIndex
                });
            }
            if (bindings.Count == 0)
                throw new InvalidDataException("流程未包含模型节点");
            return bindings;
        }

        private static void ApplyModelBindings(JArray nodes, Dictionary<int, int> modelBindings)
        {
            var foundNodeIds = new HashSet<int>();
            foreach (var node in nodes)
            {
                if (!IsModelNode(node, out int nodeId)) continue;
                int modelIndex;
                if (!modelBindings.TryGetValue(nodeId, out modelIndex))
                    throw new InvalidDataException("流程模型节点缺少索引绑定: " + nodeId);

                var props = EnsureProperties(node);
                string modelPath = props["model_path"] != null ? props["model_path"].ToString() : null;
                if (!string.IsNullOrWhiteSpace(modelPath))
                {
                    props["model_path_original"] = modelPath;
                    props["model_name"] = GetModelName(modelPath);
                }
                props["model_index"] = modelIndex;
                foundNodeIds.Add(nodeId);
            }

            foreach (var binding in modelBindings)
            {
                if (!foundNodeIds.Contains(binding.Key))
                    throw new InvalidDataException("流程模型绑定节点不存在: " + binding.Key);
            }
        }

        private static bool IsModelNode(JToken node, out int nodeId)
        {
            nodeId = -1;
            var obj = node as JObject;
            if (obj == null) return false;
            string type = obj["type"] != null ? obj["type"].ToString() : null;
            if (string.IsNullOrWhiteSpace(type) || !type.StartsWith("model/", StringComparison.Ordinal)) return false;
            try
            {
                nodeId = obj["id"].Value<int>();
                return nodeId >= 0;
            }
            catch
            {
                throw new InvalidDataException("流程模型节点缺少有效 ID");
            }
        }

        private static JObject EnsureProperties(JToken node)
        {
            var obj = (JObject)node;
            var props = obj["properties"] as JObject;
            if (props == null)
            {
                props = new JObject();
                obj["properties"] = props;
            }
            return props;
        }

        private static int ReadDeviceId(JObject properties, int defaultDeviceId)
        {
            try { return properties["device_id"] != null ? properties["device_id"].Value<int>() : defaultDeviceId; }
            catch { return defaultDeviceId; }
        }

        private static string NormalizeModelPath(string modelPath)
        {
            try { return Path.GetFullPath(modelPath).Trim(); }
            catch { return modelPath != null ? modelPath.Trim() : string.Empty; }
        }

        private static string GetModelName(string modelPath)
        {
            try
            {
                string name = Path.GetFileName(modelPath);
                return string.IsNullOrWhiteSpace(name) ? modelPath : name;
            }
            catch
            {
                return modelPath;
            }
        }

        private static void DisposeModels(Dictionary<int, Model> models)
        {
            if (models == null) return;
            var disposedModels = new HashSet<Model>();
            foreach (var model in models.Values)
            {
                if (model == null || !disposedModels.Add(model)) continue;
                try { model.Dispose(); } catch { }
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
