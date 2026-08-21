using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace DlcvModules
{
    /// <summary>
    /// post_process/region_category_check：逐条 local 结果按区域和类别校验数量。
    /// </summary>
    public class RegionCategoryCheck : BaseModule
    {
        static RegionCategoryCheck()
        {
            ModuleRegistry.Register("post_process/region_category_check", typeof(RegionCategoryCheck));
        }

        public RegionCategoryCheck(
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
            string matchMode = ReadStringProperty("match_mode", "center_in_region");
            double iouThreshold = ReadDoubleProperty("iou_threshold", 0.3);
            bool generateVirtual = ReadBoolProperty("generate_virtual_box", true);
            bool markOutside = ReadBoolProperty("mark_outside_as_excess", true);
            bool allOk = true;
            var allReasons = new List<string>();

            foreach (JToken token in results)
            {
                var entry = token as JObject;
                if (entry == null || !string.Equals(entry["type"]?.ToString(), "local", StringComparison.Ordinal))
                {
                    continue;
                }

                var detections = entry["sample_results"] as JArray ?? new JArray();
                var sourceDetections = new List<JObject>();
                foreach (JToken detectionToken in detections)
                {
                    var detection = detectionToken as JObject;
                    if (detection != null) sourceDetections.Add(detection);
                }
                var violations = new List<string>();

                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    Rule rule = rules[ruleIndex];
                    var inRegion = new List<JObject>();
                    var outside = new List<JObject>();

                    for (int detectionIndex = 0; detectionIndex < sourceDetections.Count; detectionIndex++)
                    {
                        JObject detection = sourceDetections[detectionIndex];
                        if (!string.Equals(detection["category_name"]?.ToString(), rule.Category, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (IsInRegion(detection, rule, matchMode, iouThreshold))
                        {
                            inRegion.Add(detection);
                        }
                        else
                        {
                            outside.Add(detection);
                        }
                    }

                    if (inRegion.Count < rule.Expect)
                    {
                        int shortCount = rule.Expect - inRegion.Count;
                        for (int i = 0; i < shortCount; i++)
                        {
                            string reason = rule.Category + " 缺失";
                            if (generateVirtual)
                            {
                                detections.Add(new JObject
                                {
                                    ["category_name"] = rule.Category,
                                    ["score"] = 0,
                                    ["bbox"] = new JArray(rule.X, rule.Y, rule.Width, rule.Height),
                                    ["reason"] = reason,
                                    ["is_virtual"] = true
                                });
                            }
                            violations.Add(reason);
                        }
                    }

                    if (inRegion.Count > rule.Expect)
                    {
                        string reason = rule.Category + " 区域内超量";
                        for (int i = rule.Expect; i < inRegion.Count; i++)
                        {
                            inRegion[i]["reason"] = reason;
                            inRegion[i]["is_virtual"] = false;
                        }
                        violations.Add(reason);
                    }

                    if (markOutside && outside.Count > 0)
                    {
                        string reason = rule.Category + " 区域外检出";
                        for (int i = 0; i < outside.Count; i++)
                        {
                            outside[i]["reason"] = reason;
                            outside[i]["is_virtual"] = false;
                        }
                        violations.Add(reason);
                    }
                }

                bool moduleOk = violations.Count == 0;
                bool? existingOk = ReadNullableBool(entry["ok"]);
                bool entryOk = existingOk.HasValue
                    ? (moduleOk ? existingOk.Value : false)
                    : moduleOk;
                var existingReasons = entry["reason"] as JArray;
                var entryReasons = existingReasons != null
                    ? (JArray)existingReasons.DeepClone()
                    : new JArray();

                for (int i = 0; i < violations.Count; i++)
                {
                    entryReasons.Add(violations[i]);
                }

                entry["ok"] = entryOk;
                entry["reason"] = entryReasons.Count > 0
                    ? (JToken)entryReasons
                    : JValue.CreateNull();
                entry["sample_results"] = detections;

                if (!entryOk)
                {
                    allOk = false;
                    foreach (JToken reason in entryReasons)
                    {
                        if (reason != null && reason.Type != JTokenType.Null)
                        {
                            allReasons.Add(reason.ToString());
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

            JToken parsed = null;
            if (raw is string text)
            {
                try { parsed = JToken.Parse(text); } catch { return new List<Rule>(); }
            }
            else if (raw is JToken token)
            {
                parsed = token;
            }
            else if (raw is IEnumerable && !(raw is string))
            {
                try { parsed = JToken.FromObject(raw); } catch { return new List<Rule>(); }
            }
            else
            {
                try { parsed = JToken.FromObject(raw); } catch { return new List<Rule>(); }
            }

            var array = parsed as JArray;
            if (array == null && parsed is JObject single)
            {
                array = new JArray(single);
            }

            var rules = new List<Rule>();
            if (array == null) return rules;

            foreach (JToken token in array)
            {
                var item = token as JObject;
                if (item == null) continue;
                rules.Add(new Rule(
                    ReadDouble(item["x"], 0.0),
                    ReadDouble(item["y"], 0.0),
                    ReadDouble(item["w"], 0.0),
                    ReadDouble(item["h"], 0.0),
                    item["category"] == null || item["category"].Type == JTokenType.Null
                        ? string.Empty
                        : item["category"].ToString(),
                    ReadInt(item["expect"], 0)));
            }
            return rules;
        }

        private static bool IsInRegion(JObject detection, Rule rule, string mode, double iouThreshold)
        {
            var bbox = detection["bbox"] as JArray;
            if (bbox == null || bbox.Count != 4) return false;

            double x = ReadDouble(bbox[0], double.NaN);
            double y = ReadDouble(bbox[1], double.NaN);
            double width = ReadDouble(bbox[2], double.NaN);
            double height = ReadDouble(bbox[3], double.NaN);
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(width) || double.IsNaN(height)) return false;

            double x2 = x + width;
            double y2 = y + height;
            double regionX2 = rule.X + rule.Width;
            double regionY2 = rule.Y + rule.Height;

            if (string.Equals(mode, "center_in_region", StringComparison.Ordinal))
            {
                double centerX = x + width / 2.0;
                double centerY = y + height / 2.0;
                return rule.X <= centerX && centerX <= regionX2 &&
                    rule.Y <= centerY && centerY <= regionY2;
            }

            if (string.Equals(mode, "iou", StringComparison.Ordinal))
            {
                double intersectionWidth = Math.Max(0.0, Math.Min(x2, regionX2) - Math.Max(x, rule.X));
                double intersectionHeight = Math.Max(0.0, Math.Min(y2, regionY2) - Math.Max(y, rule.Y));
                double intersectionArea = intersectionWidth * intersectionHeight;
                double boxArea = Math.Max(1e-8, width * height);
                return intersectionArea / boxArea >= iouThreshold;
            }

            return !(x2 <= rule.X || x >= regionX2 || y2 <= rule.Y || y >= regionY2);
        }

        private string ReadStringProperty(string key, string defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object raw) && raw != null)
            {
                return raw.ToString();
            }
            return defaultValue;
        }

        private double ReadDoubleProperty(string key, double defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object raw) && raw != null)
            {
                try { return Convert.ToDouble(raw, CultureInfo.InvariantCulture); } catch { }
            }
            return defaultValue;
        }

        private bool ReadBoolProperty(string key, bool defaultValue)
        {
            if (Properties != null && Properties.TryGetValue(key, out object raw) && raw != null)
            {
                try
                {
                    if (raw is bool value) return value;
                    if (bool.TryParse(raw.ToString(), out bool parsed)) return parsed;
                    return Convert.ToInt32(raw, CultureInfo.InvariantCulture) != 0;
                }
                catch { }
            }
            return defaultValue;
        }

        private static double ReadDouble(JToken token, double defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            try { return Convert.ToDouble(token.ToString(), CultureInfo.InvariantCulture); }
            catch { return defaultValue; }
        }

        private static int ReadInt(JToken token, int defaultValue)
        {
            double value = ReadDouble(token, defaultValue);
            if (double.IsNaN(value) || double.IsInfinity(value)) return defaultValue;
            if (value >= int.MaxValue) return int.MaxValue;
            if (value <= int.MinValue) return int.MinValue;
            return (int)value;
        }

        private static bool? ReadNullableBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            try { return token.Value<bool>(); } catch { return null; }
        }

        private sealed class Rule
        {
            public double X { get; private set; }
            public double Y { get; private set; }
            public double Width { get; private set; }
            public double Height { get; private set; }
            public string Category { get; private set; }
            public int Expect { get; private set; }

            public Rule(double x, double y, double width, double height, string category, int expect)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
                Category = category;
                Expect = expect;
            }
        }
    }
}