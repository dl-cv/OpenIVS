using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    // 不改写上游 wrapper 或结果，避免影响其他分支。
    internal static class OriginalImageClassification
    {
        internal sealed class Detection
        {
            public JObject Value;
            public double[] CurrentToOriginal;
        }

        internal sealed class Group
        {
            public ModuleImage Image;
            public readonly List<Detection> Detections = new List<Detection>();
        }

        internal static List<Group> Collect(List<ModuleImage> images, JArray results, bool allowNullSamples)
        {
            images = images ?? new List<ModuleImage>();
            var groups = new List<Group>();
            var wrapperGroups = new List<int>();
            foreach (ModuleImage wrapper in images)
            {
                Mat original = wrapper?.OriginalImage;
                if (original == null || original.Empty()) throw new ArgumentException("OriginalImage 必须为非空原图");
                if (wrapper.OriginalIndex < 0) throw new ArgumentException("OriginalIndex 必须为非负整数");
                int group = groups.FindIndex(item => ReferenceEquals(item.Image.OriginalImage, original));
                if (group < 0)
                {
                    group = groups.Count;
                    groups.Add(new Group { Image = new ModuleImage(original, original,
                        new TransformationState(original.Width, original.Height), wrapper.OriginalIndex) });
                }
                wrapperGroups.Add(group);
            }
            foreach (JToken token in results ?? new JArray())
            {
                if (!(token is JObject entry)) throw new ArgumentException("result_list 必须包含对象");
                if (entry.Value<string>("type") != "local") continue;
                Group group = groups[SelectGroup(images, wrapperGroups, entry)];
                double[] matrix = ReadMatrix(entry["transform"], group.Image.OriginalImage);
                JToken samples = entry["sample_results"];
                if (samples == null || (allowNullSamples && samples.Type == JTokenType.Null)) samples = new JArray();
                if (!(samples is JArray detections)) throw new ArgumentException("sample_results 必须为对象数组");
                foreach (JToken detection in detections)
                {
                    if (!(detection is JObject value)) throw new ArgumentException("sample_results 必须为对象数组");
                    group.Detections.Add(new Detection { Value = value, CurrentToOriginal = matrix });
                }
            }
            return groups;
        }

        private static int SelectGroup(List<ModuleImage> images, List<int> groups, JObject entry)
        {
            int? index = ReadIndex(entry["index"], "index");
            int? imageIndex = ReadIndex(entry["image_index"], "image_index");
            int? origin = ReadIndex(entry["origin_index"], "origin_index");
            if (imageIndex.HasValue)
            {
                if (index.HasValue && index != imageIndex) throw new ArgumentException("index 与 image_index 冲突");
                index = imageIndex;
            }
            if (index.HasValue && index.Value < images.Count)
            {
                ModuleImage selected = images[index.Value];
                if (origin.HasValue && selected.OriginalIndex != origin.Value)
                {
                    // Python 合并支路可能复用同一个 wrapper，旧 origin 仍指向列表位置。
                    bool sharedAlias = origin.Value < images.Count && ReferenceEquals(images[origin.Value], selected)
                        && images.FindAll(item => ReferenceEquals(item, selected)).Count > 1;
                    if (!sharedAlias) throw new ArgumentException("index 与 origin_index 指向不同原图");
                }
                return groups[index.Value];
            }
            var candidates = new HashSet<int>();
            for (int i = 0; i < images.Count; i++)
                if (!origin.HasValue || images[i].OriginalIndex == origin.Value) candidates.Add(groups[i]);
            if (candidates.Count == 1 && (origin.HasValue || !index.HasValue))
                foreach (int candidate in candidates) return candidate;
            throw new ArgumentException("无法确定结果所属原图，请提供有效 index/image_index 或 origin_index");
        }

        private static int? ReadIndex(JToken token, string name)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (!TryNumber(token, out double value) || value < 0 || value > int.MaxValue || value != Math.Floor(value))
                throw new ArgumentException(name + " 必须为非负整数");
            return (int)value;
        }

        internal static double[] ReadMatrix(JToken token, Mat original)
        {
            var identity = new double[] { 1, 0, 0, 0, 1, 0 };
            if (token == null || token.Type == JTokenType.Null) return identity;
            if (!(token is JObject state)) throw new ArgumentException("transform 必须为对象");
            if (ReadIndex(state["original_width"], "transform.original_width") != original.Width ||
                ReadIndex(state["original_height"], "transform.original_height") != original.Height)
                throw new ArgumentException("transform 与原图尺寸不一致");
            foreach (string name in new[] { "crop_box", "output_size" })
            {
                if (state[name] == null) continue;
                int length = name == "crop_box" ? 4 : 2;
                if (!(state[name] is JArray size) || size.Count != length) throw new ArgumentException("transform." + name + " 尺寸无效");
                for (int i = 0; i < length; i++)
                {
                    int? value = ReadIndex(size[i], "transform." + name);
                    if (!value.HasValue || (i >= length - 2 && value.Value == 0)) throw new ArgumentException("transform." + name + " 尺寸无效");
                }
            }
            if (state["affine_2x3"] == null) return identity;
            if (!(state["affine_2x3"] is JArray values) || values.Count != 6) throw new ArgumentException("transform.affine_2x3 必须含 6 个有限数值");
            var affine = new double[6];
            for (int i = 0; i < 6; i++)
            {
                if ((values[i].Type != JTokenType.Float && values[i].Type != JTokenType.Integer) || !TryNumber(values[i], out affine[i]))
                    throw new ArgumentException("transform.affine_2x3 必须含 6 个有限数值");
            }
            double determinant = affine[0] * affine[4] - affine[1] * affine[3];
            if (!Finite(determinant) || Math.Abs(determinant) < 1e-12) throw new ArgumentException("transform.affine_2x3 不可逆");
            return TransformationState.Inverse2x3(affine);
        }

        internal static Point2d Center(Detection detection)
        {
            JToken bbox = detection.Value["bbox"];
            bool xyxy = false;
            if (bbox is JObject box)
            {
                if (box["x1"] != null && box["y1"] != null && box["x2"] != null && box["y2"] != null)
                {
                    bbox = new JArray(box["x1"], box["y1"], box["x2"], box["y2"]);
                    xyxy = true;
                }
                else if (box["cx"] != null && box["cy"] != null && box["w"] != null && box["h"] != null)
                    bbox = new JArray(box["cx"], box["cy"], box["w"], box["h"], box["angle"] ?? box["theta"] ?? new JValue(0));
            }
            if (!(bbox is JArray array) || (array.Count != 4 && array.Count != 5)) throw new ArgumentException("bbox 必须为普通矩形框或旋转框");
            var values = new double[array.Count];
            for (int i = 0; i < values.Length; i++)
                if (!TryNumber(array[i], out values[i])) throw new ArgumentException("bbox 必须包含有限坐标");
            if (xyxy ? values[2] <= values[0] || values[3] <= values[1] : values[2] <= 0 || values[3] <= 0)
                throw new ArgumentException("bbox 宽高必须大于零");
            bool rotated = values.Length == 5;
            if (!xyxy && !rotated && detection.Value["with_angle"]?.Type == JTokenType.Boolean && detection.Value.Value<bool>("with_angle"))
            {
                if (!TryNumber(detection.Value["angle"], out double angle)) throw new ArgumentException("旋转框 angle 必须为有限数值");
                rotated = angle > -99;
            }
            // 四项旋转框使用独立 angle，前两项已经是中心坐标。
            double x = values[0], y = values[1];
            if (xyxy) { x = x / 2 + values[2] / 2; y = y / 2 + values[3] / 2; }
            else if (!rotated) { x += values[2] / 2; y += values[3] / 2; }
            double[] matrix = detection.CurrentToOriginal;
            var center = new Point2d(matrix[0] * x + matrix[1] * y + matrix[2], matrix[3] * x + matrix[4] * y + matrix[5]);
            if (!Finite(center.X) || !Finite(center.Y)) throw new ArgumentException("bbox 原图中心必须为有限坐标");
            return center;
        }

        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        internal static bool TryNumber(JToken token, out double value)
        {
            value = double.NaN;
            if (token == null) return false;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<double>();
                return Finite(value);
            }
            return token.Type == JTokenType.String
                && double.TryParse((string)token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && Finite(value);
        }

        internal static JToken Property(Dictionary<string, object> properties, string name, JToken fallback)
        {
            if (!properties.TryGetValue(name, out object value)) return fallback;
            return value == null ? JValue.CreateNull() : value as JToken ?? JToken.FromObject(value);
        }

        internal static JObject Result(Group group, int index, string type, bool ok, string label, bool rotated, string reason)
        {
            int w = group.Image.OriginalImage.Width, h = group.Image.OriginalImage.Height;
            return new JObject
            {
                ["type"] = "local", ["originating_module"] = type, ["index"] = index,
                ["origin_index"] = group.Image.OriginalIndex, ["transform"] = JObject.FromObject(group.Image.TransformState.ToDict()),
                ["ok"] = ok, ["reason"] = reason,
                ["sample_results"] = new JArray(new JObject
                {
                    ["category_name"] = label, ["category_id"] = ok ? 0 : 1, ["score"] = 1.0,
                    ["bbox"] = rotated ? new JArray(w / 2.0, h / 2.0, (double)w, (double)h, 0.0) : new JArray(0, 0, w, h)
                })
            };
        }
    }
}
