using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DlcvModules
{
    public class BBoxEdgeDistance : BaseModule
    {
        private static readonly string[] Sides = { "top", "bottom", "left", "right" };
        private static readonly string[] Labels = { "上边距", "下边距", "左边距", "右边距" };

        static BBoxEdgeDistance() { ModuleRegistry.Register("post_process/bbox_edge_distance", typeof(BBoxEdgeDistance)); }

        public BBoxEdgeDistance(int nodeId, string title = null,
            Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            string first = Category("category_1", "文本"), second = Category("category_2", "底座");
            var selected = new List<int>();
            for (int i = 0; i < Sides.Length; i++) if (Selected(Sides[i])) selected.Add(i);
            if (selected.Count == 0) throw new ArgumentException("至少勾选一个边距方向");
            var images = new List<ModuleImage>();
            var results = new JArray();
            foreach (var group in OriginalImageClassification.Collect(imageList, resultList, false))
            {
                bool ok = true;
                string reason;
                var visible = new JArray();
                var extra = (JObject)group.ExtraInfo.DeepClone();
                extra["edge_distances"] = new JObject();
                try
                {
                    var a = Top1(group.Detections, first);
                    var b = Top1(group.Detections, second);
                    double[] box1 = OriginalBox(a), box2 = OriginalBox(b);
                    var distances = new JObject();
                    var reasons = new List<string>();
                    foreach (int side in selected)
                    {
                        double value = side == 0 ? box1[1] - box2[1] : side == 1 ? box2[3] - box1[3]
                            : side == 2 ? box1[0] - box2[0] : box2[2] - box1[2];
                        if (!OriginalImageClassification.Finite(value)) throw new ArgumentException("边距不是有限数值");
                        distances[Sides[side]] = value;
                        string number = Math.Abs(value) < 0.0000005 ? "0" : (value < 0 ? "-" : "") + OffsetDistance.FormatDistance(Math.Abs(value));
                        reasons.Add(Labels[side] + "：" + number + "px");
                    }
                    reason = string.Join("；", reasons);
                    extra["category_1"] = first; extra["category_2"] = second;
                    extra["bbox_1"] = new JArray(box1); extra["bbox_2"] = new JArray(box2);
                    extra["edge_distances"] = distances;
                    visible.Add(Visible(a.Value, box1)); visible.Add(Visible(b.Value, box2));
                }
                catch (ArgumentException ex) { ok = false; reason = "无法计算边距：" + ex.Message; }
                results.Add(new JObject
                {
                    ["type"] = "local", ["originating_module"] = "post_process/bbox_edge_distance", ["index"] = images.Count,
                    ["origin_index"] = group.Image.OriginalIndex, ["transform"] = JObject.FromObject(group.Image.TransformState.ToDict()),
                    ["ok"] = ok, ["reason"] = reason, ["extra_info"] = extra, ["sample_results"] = visible
                });
                images.Add(group.Image);
            }
            return new ModuleIO(images, results);
        }

        private string Category(string key, string fallback)
        {
            string value = OriginalImageClassification.Property(Properties, key, new JValue(fallback)).ToString().Trim();
            if (value.Length == 0) throw new ArgumentException(key + " 不能为空");
            return value;
        }

        private bool Selected(string key)
        {
            var value = OriginalImageClassification.Property(Properties, key, new JValue(true));
            if (value.Type == JTokenType.Boolean) return value.Value<bool>();
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return value.Value<double>() != 0;
            string text = value.ToString().Trim().ToLowerInvariant();
            return text == "1" || text == "true" || text == "yes" || text == "on";
        }

        private static OriginalImageClassification.Detection Top1(List<OriginalImageClassification.Detection> detections, string category)
        {
            OriginalImageClassification.Detection best = null;
            double bestScore = double.NegativeInfinity;
            foreach (var detection in detections)
            {
                if ((detection.Value.Value<string>("category_name") ?? "").Trim() != category) continue;
                if (!OriginalImageClassification.TryNumber(detection.Value["score"], out double score)) throw new ArgumentException("类别“" + category + "”存在无效 score");
                if (best == null || score > bestScore) { best = detection; bestScore = score; }
            }
            if (best == null) throw new ArgumentException("未找到类别“" + category + "”的目标");
            return best;
        }

        private static JObject Visible(JObject source, double[] box)
        {
            var result = (JObject)source.DeepClone();
            foreach (string key in new[] { "rbox", "poly", "polygon", "mask_array", "mask_rle" }) result.Remove(key);
            result["bbox"] = new JArray(box[0], box[1], box[2] - box[0], box[3] - box[1]);
            result["with_angle"] = false; result["angle"] = -100; result["with_mask"] = false;
            result["is_distance_target"] = true;
            return result;
        }

        private static double[] OriginalBox(OriginalImageClassification.Detection detection)
        {
            JToken bbox = detection.Value["bbox"];
            bool xyxy = false;
            if (bbox is JObject obj)
            {
                xyxy = obj["x1"] != null && obj["y1"] != null && obj["x2"] != null && obj["y2"] != null;
                bbox = xyxy ? new JArray(obj["x1"], obj["y1"], obj["x2"], obj["y2"])
                    : new JArray(obj["cx"], obj["cy"], obj["w"], obj["h"], obj["angle"] ?? obj["theta"] ?? new JValue(0));
            }
            if (!(bbox is JArray array) || (array.Count != 4 && array.Count != 5)) throw new ArgumentException("bbox 格式无效");
            var v = new double[5];
            for (int i = 0; i < array.Count; i++)
                if (!OriginalImageClassification.TryNumber(array[i], out v[i])) throw new ArgumentException("bbox 必须包含有限坐标");
            bool rotated = array.Count == 5;
            if (!xyxy && !rotated && detection.Value["with_angle"]?.Type == JTokenType.Boolean && detection.Value.Value<bool>("with_angle"))
            {
                if (!OriginalImageClassification.TryNumber(detection.Value["angle"], out v[4])) throw new ArgumentException("旋转框缺少有效 angle");
                rotated = v[4] > -99;
            }
            double width = xyxy ? v[2] - v[0] : v[2], height = xyxy ? v[3] - v[1] : v[3];
            if (width <= 0 || height <= 0) throw new ArgumentException("bbox 宽高必须大于零");
            double cx = rotated ? v[0] : v[0] + width / 2, cy = rotated ? v[1] : v[1] + height / 2;
            double cos = rotated ? Math.Cos(v[4]) : 1, sin = rotated ? Math.Sin(v[4]) : 0;
            var box = new[] { double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity };
            double[] t = detection.CurrentToOriginal;
            foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 })
            {
                double x = cx + sx * width / 2 * cos - sy * height / 2 * sin;
                double y = cy + sx * width / 2 * sin + sy * height / 2 * cos;
                double ox = t[0] * x + t[1] * y + t[2], oy = t[3] * x + t[4] * y + t[5];
                if (!OriginalImageClassification.Finite(ox) || !OriginalImageClassification.Finite(oy)) throw new ArgumentException("bbox 原图坐标不是有限数值");
                box[0] = Math.Min(box[0], ox); box[1] = Math.Min(box[1], oy);
                box[2] = Math.Max(box[2], ox); box[3] = Math.Max(box[3], oy);
            }
            if (box[2] <= box[0] || box[3] <= box[1]) throw new ArgumentException("bbox 宽高必须大于零");
            return box;
        }
    }
}
