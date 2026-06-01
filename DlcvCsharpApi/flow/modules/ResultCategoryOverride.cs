using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DlcvModules
{
    /// <summary>
    /// 模块名称：类别继承
    /// 对齐 Python: post_process/result_category_override
    ///
    /// 行为：
    /// - 主输入为当前 Process 的 image_list/result_list；
    /// - 第二路输入取 ExtraInputsIn[0].ResultList（对应 Python 的 extra_inputs_in[1]）；
    /// - 按图像索引一一对应匹配覆盖：index > origin_index > transform 签名；
    /// - 若某条主结果找不到对应替换标签，保持原类别不变；
    /// - 仅覆盖主路里原本为字符串类型的 category_name。
    /// </summary>
    public class ResultCategoryOverride : BaseModule
    {
        static ResultCategoryOverride()
        {
            ModuleRegistry.Register("post_process/result_category_override", typeof(ResultCategoryOverride));
            ModuleRegistry.Register("features/result_category_override", typeof(ResultCategoryOverride));
        }

        public ResultCategoryOverride(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        private static string ValidCategoryName(JToken token)
        {
            if (token == null || token.Type != JTokenType.String) return null;
            string value = token.Value<string>();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string ExtractEntryCategoryName(JObject entry)
        {
            if (entry == null) return null;

            string entryName = ValidCategoryName(entry["category_name"]);
            if (entryName != null) return entryName;

            var dets = entry["sample_results"] as JArray;
            if (dets == null) return null;

            foreach (var detToken in dets)
            {
                var det = detToken as JObject;
                if (det == null) continue;

                string detName = ValidCategoryName(det["category_name"]);
                if (detName != null) return detName;
            }

            return null;
        }

        private static void BuildOverrideMap(
            JArray replaceResults,
            out Dictionary<int, string> indexMap,
            out Dictionary<int, string> originMap,
            out Dictionary<string, string> transformMap
        )
        {
            indexMap = new Dictionary<int, string>();
            originMap = new Dictionary<int, string>();
            transformMap = new Dictionary<string, string>();

            if (replaceResults == null) return;

            foreach (var token in replaceResults)
            {
                var entry = token as JObject;
                if (entry == null) continue;
                if (!string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase))
                    continue;

                string label = ExtractEntryCategoryName(entry);
                if (string.IsNullOrWhiteSpace(label)) continue;

                // index
                int idx = SafeInt(entry["index"], -1);
                if (idx >= 0 && !indexMap.ContainsKey(idx))
                    indexMap[idx] = label;

                // origin_index
                int oidx = SafeInt(entry["origin_index"], -1);
                if (oidx >= 0 && !originMap.ContainsKey(oidx))
                    originMap[oidx] = label;

                // transform
                var transform = entry["transform"];
                if (transform != null && transform.Type == JTokenType.Object)
                {
                    string tSig = transform.ToString(Formatting.None);
                    if (!transformMap.ContainsKey(tSig))
                        transformMap[tSig] = label;
                }
            }
        }

        private static string ResolveOverrideName(
            JObject entry,
            Dictionary<int, string> indexMap,
            Dictionary<int, string> originMap,
            Dictionary<string, string> transformMap
        )
        {
            if (entry == null) return null;

            // 1) index
            int idx = SafeInt(entry["index"], -1);
            if (idx >= 0 && indexMap.ContainsKey(idx))
                return indexMap[idx];

            // 2) origin_index
            int oidx = SafeInt(entry["origin_index"], -1);
            if (oidx >= 0 && originMap.ContainsKey(oidx))
                return originMap[oidx];

            // 3) transform
            var transform = entry["transform"];
            if (transform != null && transform.Type == JTokenType.Object)
            {
                string tSig = transform.ToString(Formatting.None);
                if (transformMap.ContainsKey(tSig))
                    return transformMap[tSig];
            }

            return null;
        }

        private static JArray OverrideSampleResults(JArray sampleResults, string overrideName, out bool changed)
        {
            changed = false;
            var newSampleResults = new JArray();

            foreach (var detToken in sampleResults)
            {
                var det = detToken as JObject;
                if (det == null)
                {
                    newSampleResults.Add(detToken);
                    continue;
                }

                if (det["category_name"] != null && det["category_name"].Type == JTokenType.String)
                {
                    var detCopy = (JObject)det.DeepClone();
                    detCopy["category_name"] = overrideName;
                    newSampleResults.Add(detCopy);
                    changed = true;
                }
                else
                {
                    newSampleResults.Add(det);
                }
            }

            return newSampleResults;
        }

        private static int SafeInt(JToken token, int defaultValue)
        {
            if (token == null) return defaultValue;
            try
            {
                return token.Value<int>();
            }
            catch
            {
                return defaultValue;
            }
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

            // Python 侧第二路来自 extra_inputs_in[1]；C# 执行器聚合后在 ExtraInputsIn[0]。
            var pair1 = (ExtraInputsIn != null && ExtraInputsIn.Count > 0) ? ExtraInputsIn[0] : null;
            var replaceResults = pair1 != null ? (pair1.ResultList ?? new JArray()) : new JArray();

            // 构建替换标签映射
            Dictionary<int, string> indexMap;
            Dictionary<int, string> originMap;
            Dictionary<string, string> transformMap;
            BuildOverrideMap(replaceResults, out indexMap, out originMap, out transformMap);

            if (indexMap.Count == 0 && originMap.Count == 0 && transformMap.Count == 0)
            {
                return new ModuleIO(images, results);
            }

            var newResults = new JArray();
            foreach (var token in results)
            {
                var entry = token as JObject;
                if (entry == null)
                {
                    newResults.Add(token);
                    continue;
                }

                // 非 local 条目透传
                if (!string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase))
                {
                    newResults.Add(entry);
                    continue;
                }

                string overrideName = ResolveOverrideName(entry, indexMap, originMap, transformMap);
                if (string.IsNullOrWhiteSpace(overrideName))
                {
                    newResults.Add(entry);
                    continue;
                }

                bool changed = false;
                JObject entryCopy = null;

                if (entry["category_name"] != null && entry["category_name"].Type == JTokenType.String)
                {
                    entryCopy = (JObject)entry.DeepClone();
                    entryCopy["category_name"] = overrideName;
                    changed = true;
                }

                var sampleResults = entry["sample_results"] as JArray;
                if (sampleResults != null)
                {
                    bool sampleChanged;
                    var newSampleResults = OverrideSampleResults(sampleResults, overrideName, out sampleChanged);
                    if (sampleChanged)
                    {
                        if (entryCopy == null)
                        {
                            entryCopy = (JObject)entry.DeepClone();
                        }
                        entryCopy["sample_results"] = newSampleResults;
                        changed = true;
                    }
                }

                newResults.Add(changed ? (JToken)entryCopy : entry);
            }

            return new ModuleIO(images, newResults);
        }
    }
}
