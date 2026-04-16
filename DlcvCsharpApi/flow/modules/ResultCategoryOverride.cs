using System;
using System.Collections.Generic;
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
    /// - 从第二路结果中提取“第一个有效 category_name”（先 entry 级，再 sample_results 级）；
    /// - 若未提取到有效类别名，直接透传主路 results；
    /// - 若提取到有效类别名，仅覆盖主路里原本为字符串类型的 category_name。
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

        private static string ExtractFirstCategoryName(JArray replaceResults)
        {
            if (replaceResults == null) return null;

            foreach (var token in replaceResults)
            {
                var entry = token as JObject;
                if (entry == null) continue;

                string entryName = ValidCategoryName(entry["category_name"]);
                if (entryName != null) return entryName;

                var dets = entry["sample_results"] as JArray;
                if (dets == null) continue;

                foreach (var detToken in dets)
                {
                    var det = detToken as JObject;
                    if (det == null) continue;

                    string detName = ValidCategoryName(det["category_name"]);
                    if (detName != null) return detName;
                }
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

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

            // Python 侧第二路来自 extra_inputs_in[1]；C# 执行器聚合后在 ExtraInputsIn[0]。
            var pair1 = (ExtraInputsIn != null && ExtraInputsIn.Count > 0) ? ExtraInputsIn[0] : null;
            var replaceResults = pair1 != null ? (pair1.ResultList ?? new JArray()) : new JArray();

            string overrideName = ExtractFirstCategoryName(replaceResults);
            if (string.IsNullOrWhiteSpace(overrideName))
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
