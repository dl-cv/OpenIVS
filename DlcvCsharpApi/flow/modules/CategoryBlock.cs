using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DlcvModules
{
    public class CategoryBlock : BaseModule
    {
        private static readonly JObject UnicodeData = LoadUnicodeData();

        static CategoryBlock()
        {
            ModuleRegistry.Register("post_process/category_block", typeof(CategoryBlock));
        }

        public CategoryBlock(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context) { }

        private static JObject LoadUnicodeData()
        {
            using (var stream = typeof(CategoryBlock).Assembly.GetManifestResourceStream("CategoryBlockUnicode.json"))
            using (var reader = new StreamReader(stream)) return JObject.Parse(reader.ReadToEnd());
        }

        private static bool InRanges(string property, int value)
        {
            foreach (JArray range in UnicodeData[property])
            {
                if (value < (int)range[0]) return false;
                if (value <= (int)range[1]) return true;
            }
            return false;
        }

        private static bool CasedAt(List<int> chars, int index, int step)
        {
            for (; index >= 0 && index < chars.Count; index += step)
                if (!InRanges("ignorable", chars[index])) return InRanges("cased", chars[index]);
            return false;
        }

        private static string Normalize(string value)
        {
            var chars = new List<int>();
            for (int i = 0; i < value.Length; i++)
            {
                int cp = char.IsSurrogatePair(value, i) ? char.ConvertToUtf32(value, i++) : value[i];
                chars.Add(cp);
            }
            int start = 0, end = chars.Count;
            while (start < end && InRanges("space", chars[start])) start++;
            while (end > start && InRanges("space", chars[end - 1])) end--;
            var output = new StringBuilder();
            for (int i = start; i < end; i++)
            {
                int cp = chars[i];
                if (cp == 0x3a3 && CasedAt(chars, i - 1, -1) && !CasedAt(chars, i + 1, 1)) cp = 0x3c2;
                var lower = UnicodeData["lower"][cp.ToString(CultureInfo.InvariantCulture)] as JArray;
                if (lower != null) foreach (int mapped in lower) output.Append(char.ConvertFromUtf32(mapped));
                else if (cp <= 0xffff) output.Append((char)cp);
                else output.Append(char.ConvertFromUtf32(cp));
            }
            return output.ToString();
        }

        private static string PythonString(JToken value)
        {
            if (value == null) return "";
            if (value.Type == JTokenType.Null) return "None";
            if (value.Type == JTokenType.String) return value.Value<string>();
            if (value.Type == JTokenType.Boolean) return value.Value<bool>() ? "True" : "False";
            if (value.Type == JTokenType.Float) return PythonFloat(value.Value<double>());
            return value.ToString(Formatting.None);
        }

        private static string PythonFloat(double value)
        {
            if (double.IsNaN(value)) return "nan";
            if (double.IsInfinity(value)) return value < 0 ? "-inf" : "inf";
            string sign = BitConverter.DoubleToInt64Bits(value) < 0 ? "-" : "";
            value = Math.Abs(value);
            if (value == 0) return sign + "0.0";
            string text = null;
            for (int precision = 1; precision <= 17; precision++)
            {
                text = value.ToString("G" + precision, CultureInfo.InvariantCulture);
                if (double.Parse(text, CultureInfo.InvariantCulture) == value) break;
            }
            var parts = text.ToLowerInvariant().Split('e');
            int exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
            int point = parts[0].IndexOf('.');
            if (point < 0) point = parts[0].Length;
            string digits = parts[0].Replace(".", "");
            int leading = digits.Length - digits.TrimStart('0').Length;
            digits = digits.TrimStart('0').TrimEnd('0');
            exponent += point - leading - 1;
            if (exponent >= -4 && exponent < 16)
            {
                int position = exponent + 1;
                if (position <= 0) return sign + "0." + new string('0', -position) + digits;
                if (position >= digits.Length) return sign + digits + new string('0', position - digits.Length) + ".0";
                return sign + digits.Insert(position, ".");
            }
            return sign + digits[0] + (digits.Length > 1 ? "." + digits.Substring(1) : "") +
                "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();
            var categories = new HashSet<string>(StringComparer.Ordinal);
            if (Properties.TryGetValue("categories", out object raw) && raw != null)
            {
                var values = raw as JArray ?? JToken.FromObject(raw) as JArray;
                if (values != null) foreach (var value in values)
                {
                    if (value.Type != JTokenType.String && value.Type != JTokenType.Integer &&
                        value.Type != JTokenType.Float && value.Type != JTokenType.Boolean) continue;
                    string name = Normalize(PythonString(value));
                    if (name.Length > 0) categories.Add(name);
                }
            }
            if (categories.Count == 0) return new ModuleIO(images, results);
            var output = new JArray();
            foreach (var token in results)
            {
                var entry = token as JObject;
                var detections = entry?["sample_results"] as JArray;
                if (entry?["type"]?.Type != JTokenType.String || (string)entry["type"] != "local" || detections == null)
                {
                    output.Add(token);
                    continue;
                }
                var filtered = new JArray();
                foreach (var detection in detections)
                    if (!(detection is JObject obj) || !categories.Contains(Normalize(PythonString(obj["category_name"]))))
                        filtered.Add(detection);
                var copy = (JObject)entry.DeepClone();
                copy["sample_results"] = filtered;
                output.Add(copy);
            }
            return new ModuleIO(images, output);
        }
    }
}
