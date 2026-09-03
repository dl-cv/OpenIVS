using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using dlcv_infer_csharp;

namespace DlcvModules
{
    /// <summary>
    /// 从 .dvst 或 .dvso 归档读取流程配置和子模型数据。
    /// </summary>
    public class DvsModel : FlowGraphModel
    {
        private const int MaxHeaderLength = 16 * 1024 * 1024;

        private sealed class ArchiveEntry
        {
            public string NormalizedName { get; private set; }
            public byte[] Data { get; private set; }

            public ArchiveEntry(string normalizedName, byte[] data)
            {
                NormalizedName = normalizedName;
                Data = data;
            }
        }

        public new JObject Load(string dvsPath, int deviceId = 0)
        {
            if (string.IsNullOrWhiteSpace(dvsPath))
                throw new ArgumentException("文件路径为空", nameof(dvsPath));

            string extension = Path.GetExtension(dvsPath).ToLowerInvariant();
            if (extension == ".dvsp")
                throw new NotSupportedException("当前不支持 .dvsp 模型，请使用 .dvst 或 .dvso");
            if (extension != ".dvst" && extension != ".dvso")
                throw new ArgumentException("DvsModel 仅支持 .dvst 和 .dvso 文件", nameof(dvsPath));
            if (!File.Exists(dvsPath))
                throw new FileNotFoundException("文件不存在", dvsPath);

            JObject pipelineJson;
            Dictionary<string, ArchiveEntry> entries;
            using (var stream = new FileStream(dvsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ReadArchive(stream, out pipelineJson, out entries);
            }

            string archiveCacheKey = Guid.NewGuid().ToString("N");
            Dictionary<int, FlowModelSource> modelSources = BuildModelSources(pipelineJson, entries, archiveCacheKey);
            return LoadFromRoot(pipelineJson, deviceId, modelSources);
        }

        public JObject LoadFromModelBindings(
            string sourcePath,
            JObject savedPipeline,
            JArray modelBindings,
            int deviceId)
        {
            if (savedPipeline == null) throw new ArgumentNullException(nameof(savedPipeline));
            if (modelBindings == null) throw new ArgumentNullException(nameof(modelBindings));

            var modelsByIndex = new Dictionary<int, Model>();
            var bindingsByNode = new Dictionary<int, int>();
            foreach (JToken token in modelBindings)
            {
                JObject binding = token as JObject;
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
                if (nodeId < 0 || modelIndex < 0 || bindingsByNode.ContainsKey(nodeId))
                    throw new InvalidDataException("流程模型绑定索引无效");
                bindingsByNode[nodeId] = modelIndex;
                modelsByIndex[modelIndex] = new Model
                {
                    modelIndex = modelIndex,
                    OwnModelIndex = false
                };
            }

            JObject root = (JObject)savedPipeline.DeepClone();
            JArray nodes = root["nodes"] as JArray;
            if (nodes == null) throw new InvalidDataException("流程配置缺少 nodes 数组");
            var foundNodeIds = new HashSet<int>();
            foreach (JObject node in nodes.OfType<JObject>())
            {
                string nodeType = node["type"]?.ToString() ?? string.Empty;
                if (!nodeType.StartsWith("model/", StringComparison.Ordinal)) continue;

                int nodeId = ReadNodeId(node, -1);
                if (!foundNodeIds.Add(nodeId))
                    throw new InvalidDataException("流程中存在重复模型节点编号：" + nodeId);
                int modelIndex;
                if (!bindingsByNode.TryGetValue(nodeId, out modelIndex))
                    throw new InvalidDataException("流程模型节点缺少索引绑定：" + nodeId);
                JObject properties = node["properties"] as JObject;
                if (properties == null)
                {
                    properties = new JObject();
                    node["properties"] = properties;
                }
                string modelPath = properties["model_path"]?.ToString();
                if (!string.IsNullOrWhiteSpace(modelPath))
                    properties["model_name"] = GetArchiveFileName(modelPath);
                properties["model_index"] = modelIndex;
            }
            foreach (int nodeId in bindingsByNode.Keys)
            {
                if (!foundNodeIds.Contains(nodeId))
                    throw new InvalidDataException("流程模型绑定节点不存在：" + nodeId);
            }
            return LoadFromRoot(root, deviceId, modelsByIndex, savedPipeline);
        }

        internal new DllLoader GetLoadedModelLoader(int modelIndex)
        {
            return base.GetLoadedModelLoader(modelIndex);
        }

        private static void ReadArchive(Stream stream, out JObject pipelineJson, out Dictionary<string, ArchiveEntry> entries)
        {
            byte[] magic = ReadExactBytes(stream, 3, "文件读取意外结束");
            if (magic[0] != (byte)'D' || magic[1] != (byte)'V' || magic[2] != (byte)'\n')
                throw new InvalidDataException("文件格式错误：缺少 DV 头部");

            string headerText = ReadHeaderLine(stream);
            JObject header;
            try
            {
                header = JObject.Parse(headerText);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("文件头信息损坏：JSON 无法解析", ex);
            }

            JArray fileList = header["file_list"] as JArray;
            JArray fileSize = header["file_size"] as JArray;
            if (fileList == null || fileSize == null || fileList.Count != fileSize.Count)
                throw new InvalidDataException("文件头信息损坏：file_list 或 file_size 缺失或数量不一致");

            pipelineJson = null;
            entries = new Dictionary<string, ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fileList.Count; i++)
            {
                string entryName = fileList[i]?.ToString();
                if (string.IsNullOrWhiteSpace(entryName))
                    throw new InvalidDataException($"文件头信息损坏：第 {i + 1} 个文件名为空");

                long declaredSize = ReadDeclaredSize(fileSize[i], i);
                byte[] data = ReadExactBytes(stream, declaredSize, $"读取 {entryName} 时文件意外结束");
                string normalizedName = NormalizeArchiveName(entryName);

                if (string.Equals(normalizedName, "pipeline.json", StringComparison.OrdinalIgnoreCase))
                {
                    if (pipelineJson != null)
                        throw new InvalidDataException("归档中存在多个 pipeline.json");
                    try
                    {
                        pipelineJson = JObject.Parse(Encoding.UTF8.GetString(data));
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("pipeline.json 无法解析", ex);
                    }
                    continue;
                }

                if (entries.ContainsKey(normalizedName))
                    throw new InvalidDataException($"归档中存在重复文件：{entryName}");
                entries[normalizedName] = new ArchiveEntry(normalizedName, data);
            }

            if (pipelineJson == null)
                throw new InvalidDataException("未找到 pipeline.json");
        }

        private static string ReadHeaderLine(Stream stream)
        {
            var bytes = new List<byte>();
            while (true)
            {
                int value = stream.ReadByte();
                if (value < 0)
                    throw new InvalidDataException("文件头信息损坏：JSON 头行未结束");
                if (value == '\n')
                    break;
                if (bytes.Count >= MaxHeaderLength)
                    throw new InvalidDataException("文件头信息损坏：JSON 头行过大");
                bytes.Add((byte)value);
            }
            return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\r');
        }

        private static long ReadDeclaredSize(JToken token, int index)
        {
            if (token == null || token.Type != JTokenType.Integer)
                throw new InvalidDataException($"文件头信息损坏：第 {index + 1} 个文件大小不是整数");

            long size;
            try
            {
                size = token.Value<long>();
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"文件头信息损坏：第 {index + 1} 个文件大小超出范围", ex);
            }
            if (size < 0)
                throw new InvalidDataException($"文件头信息损坏：第 {index + 1} 个文件大小为负数");
            if (size > int.MaxValue)
                throw new InvalidDataException($"归档文件过大，无法读入内存：{size} 字节");
            return size;
        }

        private static byte[] ReadExactBytes(Stream stream, long size, string errorMessage)
        {
            byte[] data = new byte[checked((int)size)];
            int offset = 0;
            while (offset < data.Length)
            {
                int read = stream.Read(data, offset, data.Length - offset);
                if (read <= 0)
                    throw new EndOfStreamException(errorMessage);
                offset += read;
            }
            return data;
        }

        private static Dictionary<int, FlowModelSource> BuildModelSources(
            JObject pipelineJson,
            Dictionary<string, ArchiveEntry> entries,
            string archiveCacheKey)
        {
            JArray nodes = pipelineJson["nodes"] as JArray;
            if (nodes == null)
                throw new InvalidOperationException("Pipeline 缺少 nodes 数组");

            var entriesByFileName = new Dictionary<string, List<ArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (ArchiveEntry entry in entries.Values)
            {
                string fileName = GetArchiveFileName(entry.NormalizedName);
                if (!entriesByFileName.TryGetValue(fileName, out List<ArchiveEntry> sameNameEntries))
                {
                    sameNameEntries = new List<ArchiveEntry>();
                    entriesByFileName[fileName] = sameNameEntries;
                }
                sameNameEntries.Add(entry);
            }

            var sourcesByEntry = new Dictionary<string, FlowModelSource>(StringComparer.OrdinalIgnoreCase);
            var sourcesByNode = new Dictionary<int, FlowModelSource>();

            for (int i = 0; i < nodes.Count; i++)
            {
                JObject node = nodes[i] as JObject;
                if (node == null)
                    continue;

                string nodeType = node["type"]?.ToString() ?? string.Empty;
                if (!nodeType.StartsWith("model/", StringComparison.OrdinalIgnoreCase))
                    continue;

                JObject properties = node["properties"] as JObject;
                string originalPath = properties?["model_path"]?.ToString();
                int nodeId = ReadNodeId(node, i);
                if (string.IsNullOrWhiteSpace(originalPath))
                    throw new InvalidDataException($"模型节点 {nodeId} 缺少 model_path");

                ArchiveEntry entry = FindArchiveEntry(originalPath, entries, entriesByFileName, nodeId);
                string modelName = GetArchiveFileName(entry.NormalizedName);
                properties["model_path_original"] = originalPath;
                properties["model_name"] = modelName;

                if (!sourcesByEntry.TryGetValue(entry.NormalizedName, out FlowModelSource source))
                {
                    string cacheKey = archiveCacheKey + "|" + entry.NormalizedName.ToLowerInvariant();
                    source = new FlowModelSource(cacheKey, modelName, entry.Data);
                    sourcesByEntry[entry.NormalizedName] = source;
                }

                if (sourcesByNode.ContainsKey(nodeId))
                    throw new InvalidDataException($"流程中存在重复模型节点编号：{nodeId}");
                sourcesByNode[nodeId] = source;
            }

            return sourcesByNode;
        }

        private static ArchiveEntry FindArchiveEntry(
            string modelPath,
            Dictionary<string, ArchiveEntry> entries,
            Dictionary<string, List<ArchiveEntry>> entriesByFileName,
            int nodeId)
        {
            string normalizedPath = NormalizeArchiveName(modelPath);
            if (entries.TryGetValue(normalizedPath, out ArchiveEntry entry))
                return entry;

            string fileName = GetArchiveFileName(normalizedPath);
            if (!entriesByFileName.TryGetValue(fileName, out List<ArchiveEntry> candidates) || candidates.Count == 0)
                throw new InvalidDataException($"模型节点 {nodeId} 引用的归档文件不存在：{modelPath}");
            if (candidates.Count > 1)
                throw new InvalidDataException($"模型节点 {nodeId} 引用的文件名不唯一：{fileName}");
            return candidates[0];
        }

        private static int ReadNodeId(JObject node, int defaultValue)
        {
            try
            {
                int nodeId = node["id"] != null ? node["id"].Value<int>() : defaultValue;
                if (nodeId < 0)
                    throw new InvalidDataException("流程模型节点缺少有效 ID");
                return nodeId;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("流程模型节点缺少有效 ID", ex);
            }
        }

        private static string NormalizeArchiveName(string name)
        {
            string normalized = (name ?? string.Empty).Trim().Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }
            return normalized;
        }

        private static string GetArchiveFileName(string name)
        {
            string normalized = NormalizeArchiveName(name);
            int slashIndex = normalized.LastIndexOf('/');
            return slashIndex >= 0 ? normalized.Substring(slashIndex + 1) : normalized;
        }
    }
}
