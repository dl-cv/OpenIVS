using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace DlcvModules
{
    /// <summary>
    /// post_process/category_count_check：按原图汇总 local 结果后按类别校验数量。
    /// </summary>
    public class CategoryCountCheck : BaseModule
    {
        static CategoryCountCheck()
        {
            ModuleRegistry.Register("post_process/category_count_check", typeof(CategoryCountCheck));
        }

        public CategoryCountCheck(
            int nodeId,
            string title = null,
            Dictionary<string, object> properties = null,
            ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();
            var rules = ParseRules();
            bool allOk = true;
            var allReasons = new List<string>();

            var groups = new Dictionary<int, List<JObject>>();
            var groupOrder = new List<int>();
            foreach (JToken token in results)
            {
                var entry = token as JObject;
                if (entry == null || !string.Equals(entry["type"]?.ToString(), "local", StringComparison.Ordinal))
                {
                    continue;
                }

                int key = ResolveGroupKey(entry);
                if (!groups.TryGetValue(key, out List<JObject> entries))
                {
                    entries = new List<JObject>();
                    groups[key] = entries;
                    groupOrder.Add(key);
                }
                entries.Add(entry);
            }

            for (int groupIndex = 0; groupIndex < groupOrder.Count; groupIndex++)
            {
                List<JObject> entries = groups[groupOrder[groupIndex]];
                var detections = new JArray();
                for (int i = 0; i < entries.Count; i++)
                {
                    var entryDetections = entries[i]["sample_results"] as JArray;
                    if (entryDetections == null) continue;
                    foreach (JToken detection in entryDetections)
                    {
                        detections.Add(detection);
                    }
                }

                bool moduleOk = true;
                var groupReasons = new List<string>();
                for (int i = 0; i < rules.Count; i++)
                {
                    Rule rule = rules[i];
                    int count = CountMatching(detections, rule.Category);
                    if (!Evaluate(count, rule.Operator, rule.Expect))
                    {
                        moduleOk = false;
                        groupReasons.Add(FormatReason(rule.Category, rule.Operator, rule.Expect, count));
                    }
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    JObject entry = entries[i];
                    bool? existingOk = ReadNullableBool(entry["ok"]);
                    var entryReasons = ReadExistingReasons(entry["reason"]);
                    bool entryOk = existingOk.HasValue
                        ? (moduleOk ? existingOk.Value : false)
                        : moduleOk;

                    if (!moduleOk)
                    {
                        for (int reasonIndex = 0; reasonIndex < groupReasons.Count; reasonIndex++)
                        {
                            AddUnique(entryReasons, groupReasons[reasonIndex]);
                        }
                    }

                    entry["ok"] = entryOk;
                    entry["reason"] = entryReasons.Count > 0
                        ? (JToken)new JArray(entryReasons)
                        : JValue.CreateNull();

                    if (!entryOk)
                    {
                        allOk = false;
                        for (int reasonIndex = 0; reasonIndex < entryReasons.Count; reasonIndex++)
                        {
                            AddUnique(allReasons, entryReasons[reasonIndex]);
                        }
                    }
                }
            }

            ScalarOutputsByName["ok"] = allOk;
            ScalarOutputsByName["reason"] = allReasons.Count > 0
                ? string.Join("; ", allReasons)
                : string.Empty;

            return new ModuleIO(images, results);
        }

        private List<Rule> ParseRules()
        {
            if (Properties == null || !Properties.TryGetValue("rules", out object raw) || raw == null)
            {
                return new List<Rule>();
            }

            JArray array = null;
            if (raw is string text)
            {
                try { array = JArray.Parse(text); } catch { return new List<Rule>(); }
            }
            else if (raw is JArray jArray)
            {
                array = jArray;
            }
            else if (raw is IEnumerable && !(raw is string))
            {
                try { array = JArray.FromObject(raw); } catch { return new List<Rule>(); }
            }

            var rules = new List<Rule>();
            if (array == null) return rules;

            foreach (JToken token in array)
            {
                var item = token as JObject;
                if (item == null) continue;

                string category = item["category"] == null || item["category"].Type == JTokenType.Null
                    ? string.Empty
                    : item["category"].ToString();
                string op = item["operator"] == null || item["operator"].Type == JTokenType.Null
                    ? "equal"
                    : item["operator"].ToString();
                if (op != "equal" && op != "gt" && op != "lt")
                {
                    op = "equal";
                }

                int expect;
                try
                {
                    JToken expectToken = item["expect"];
                    double rawExpect = expectToken == null || expectToken.Type == JTokenType.Null
                        ? 0.0
                        : Convert.ToDouble(expectToken.ToString(), CultureInfo.InvariantCulture);
                    expect = Math.Max(0, (int)rawExpect);
                }
                catch
                {
                    expect = 1;
                }

                rules.Add(new Rule(category, op, expect));
            }
            return rules;
        }

        private static int ResolveGroupKey(JObject entry)
        {
            int originIndex = ReadNonNegativeInt(entry["origin_index"]);
            if (originIndex >= 0) return originIndex;
            int index = ReadNonNegativeInt(entry["index"]);
            return index >= 0 ? index : -1;
        }

        private static int ReadNonNegativeInt(JToken token)
        {
            try
            {
                int value = token != null ? token.Value<int>() : -1;
                return value >= 0 ? value : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static int CountMatching(JArray detections, string category)
        {
            int count = 0;
            foreach (JToken token in detections)
            {
                var detection = token as JObject;
                if (detection == null) continue;
                if (string.IsNullOrEmpty(category) ||
                    string.Equals(detection["category_name"]?.ToString(), category, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        private static bool Evaluate(int count, string op, int expect)
        {
            if (op == "gt") return count > expect;
            if (op == "lt") return count < expect;
            return count == expect;
        }

        private static string FormatReason(string category, string op, int expect, int actual)
        {
            string displayCategory = string.IsNullOrEmpty(category) ? "全部类别" : category;
            string opLabel = op == "gt" ? ">" : op == "lt" ? "<" : "=";
            return "类别" + displayCategory + "期望" + opLabel + expect + ",实际" + actual;
        }

        private static List<string> ReadExistingReasons(JToken token)
        {
            var reasons = new List<string>();
            if (token == null || token.Type == JTokenType.Null) return reasons;
            var array = token as JArray;
            if (array == null)
            {
                reasons.Add(token.ToString());
                return reasons;
            }
            foreach (JToken item in array)
            {
                if (item != null && item.Type != JTokenType.Null)
                {
                    reasons.Add(item.ToString());
                }
            }
            return reasons;
        }

        private static bool? ReadNullableBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            try { return token.Value<bool>(); } catch { return null; }
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (!values.Contains(value)) values.Add(value);
        }

        private sealed class Rule
        {
            public string Category { get; private set; }
            public string Operator { get; private set; }
            public int Expect { get; private set; }

            public Rule(string category, string op, int expect)
            {
                Category = category;
                Operator = op;
                Expect = expect;
            }
        }
    }
}
