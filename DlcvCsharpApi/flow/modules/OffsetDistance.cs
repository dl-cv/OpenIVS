using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace DlcvModules
{
    public class OffsetDistance : BaseModule
    {
        static OffsetDistance()
        {
            ModuleRegistry.Register("post_process/offset_distance", typeof(OffsetDistance));
        }

        public OffsetDistance(int nodeId, string title = null,
            Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var rules = ParseRules();
            var groups = OriginalImageClassification.Collect(imageList, resultList, false);
            var images = new List<ModuleImage>();
            var results = new JArray();
            foreach (var group in groups)
            {
                double distance = Distance(group.Detections);
                bool ok = OriginalImageClassification.Finite(distance);
                foreach (var rule in rules) ok &= rule.Key == ">" ? distance > rule.Value : distance < rule.Value;
                string text = OriginalImageClassification.Finite(distance)
                    ? FormatDistance(distance) : "Nan";
                string label = (ok ? "OK-" : "NG-") + text;
                string reason = OriginalImageClassification.Finite(distance) ? "距离：" + text + " px" : "距离：NaN";
                results.Add(OriginalImageClassification.Result(group, images.Count, "post_process/offset_distance", ok, label, true, reason));
                images.Add(group.Image);
            }
            return new ModuleIO(images, results);
        }

        private static string FormatDistance(double distance)
        {
            // Framework F6 中点远离零；按 double 的精确二进制值做六位十进制舍入，
            // 与 Python .6f 的 ties-to-even 一致，也避免先乘 1e6 引入二次舍入。
            long bits = BitConverter.DoubleToInt64Bits(distance);
            int exponent = (int)((bits >> 52) & 0x7ff);
            long significand = bits & 0x000fffffffffffffL;
            if (exponent != 0) significand |= 1L << 52;
            int shift = exponent == 0 ? -1074 : exponent - 1023 - 52;
            BigInteger scaled = new BigInteger(significand) * 1000000;
            if (shift >= 0) scaled <<= shift;
            else
            {
                BigInteger divisor = BigInteger.One << -shift;
                scaled = BigInteger.DivRem(scaled, divisor, out BigInteger remainder);
                int comparison = (remainder * 2).CompareTo(divisor);
                if (comparison > 0 || (comparison == 0 && !scaled.IsEven)) scaled++;
            }
            string digits = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(7, '0');
            return digits.Insert(digits.Length - 6, ".").TrimEnd('0').TrimEnd('.');
        }

        private List<KeyValuePair<string, double>> ParseRules()
        {
            if (!(OriginalImageClassification.Property(Properties, "rules", new JArray()) is JArray array))
                throw new ArgumentException("rules 必须为规则数组");
            var rules = new List<KeyValuePair<string, double>>();
            foreach (JToken token in array)
            {
                if (!(token is JObject rule) || rule["operator"]?.Type != JTokenType.String ||
                    ((string)rule["operator"] != ">" && (string)rule["operator"] != "<"))
                    throw new ArgumentException("rules.operator 只支持 > 或 <");
                if (!OriginalImageClassification.TryNumber(rule["value"], out double value) || value < 0)
                    throw new ArgumentException("rules.value 必须为非负有限数值（px）");
                rules.Add(new KeyValuePair<string, double>((string)rule["operator"], value));
            }
            return rules;
        }

        private static double Distance(List<OriginalImageClassification.Detection> detections)
        {
            var top = new Dictionary<string, KeyValuePair<double, OriginalImageClassification.Detection>>(StringComparer.Ordinal);
            foreach (var detection in detections)
            {
                JObject det = detection.Value;
                if (det["category_name"]?.Type != JTokenType.String || string.IsNullOrEmpty((string)det["category_name"]) ||
                    !OriginalImageClassification.TryNumber(det["score"], out double score)) return double.NaN;
                string category = (string)det["category_name"];
                if (!top.TryGetValue(category, out var previous) || score > previous.Key)
                    top[category] = new KeyValuePair<double, OriginalImageClassification.Detection>(score, detection);
            }
            if (top.Count != 2) return double.NaN;
            try
            {
                var selected = new List<KeyValuePair<double, OriginalImageClassification.Detection>>(top.Values);
                var first = OriginalImageClassification.Center(selected[0].Value);
                var second = OriginalImageClassification.Center(selected[1].Value);
                double dx = Math.Abs(first.X - second.X), dy = Math.Abs(first.Y - second.Y);
                // 缩放后求欧氏距，避免平方先溢出而误报 NG-Nan。
                double scale = Math.Max(dx, dy);
                double distance = scale == 0 ? 0 : scale * Math.Sqrt((dx / scale) * (dx / scale) + (dy / scale) * (dy / scale));
                return OriginalImageClassification.Finite(distance) ? distance : double.NaN;
            }
            catch (ArgumentException) { return double.NaN; }
        }
    }
}
