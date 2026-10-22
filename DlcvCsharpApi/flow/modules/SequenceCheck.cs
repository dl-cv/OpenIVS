using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DlcvModules
{
    public class SequenceCheck : BaseModule
    {
        static SequenceCheck()
        {
            ModuleRegistry.Register("post_process/sequence_check", typeof(SequenceCheck));
            // 仅兼容旧流程类型；判定输出统一使用新主标识。
            ModuleRegistry.Register("post_process/wire_sequence_check", typeof(SequenceCheck));
        }

        public SequenceCheck(int nodeId, string title = null,
            Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            if (!(OriginalImageClassification.Property(Properties, "categories", new JArray()) is JArray categories)
                || categories.Any(item => item.Type != JTokenType.String))
                throw new ArgumentException("categories 必须为有序字符串列表");
            JToken direction = OriginalImageClassification.Property(Properties, "direction", new JValue("horizontal"));
            if (direction.Type != JTokenType.String || ((string)direction != "horizontal" && (string)direction != "vertical"))
                throw new ArgumentException("direction 必须为 horizontal 或 vertical");
            double[] originalToReference = null;
            var references = ExtraInputsIn.Count > 0 ? ExtraInputsIn[0]?.ImageList : null;
            if (references != null && references.Count > 0)
            {
                var reference = references[0];
                if (reference?.OriginalImage == null || reference.OriginalImage.Empty())
                    throw new ArgumentException("参考图像必须包含非空原图");
                var state = reference.TransformState;
                OriginalImageClassification.ReadMatrix(state == null ? null : JObject.FromObject(state.ToDict()), reference.OriginalImage);
                // C# 仿射已经包含裁剪平移，不再次减去 crop_box。
                originalToReference = state?.AffineMatrix2x3;
            }
            var groups = OriginalImageClassification.Collect(imageList, resultList, true);
            var images = new List<ModuleImage>();
            var results = new JArray();
            foreach (var group in groups)
            {
                var ordered = new List<KeyValuePair<double, string>>();
                foreach (var detection in group.Detections)
                {
                    if (detection.Value["category_name"]?.Type != JTokenType.String)
                        throw new ArgumentException("检测结果必须包含字符串 category_name");
                    var center = OriginalImageClassification.Center(detection);
                    if (originalToReference != null)
                    {
                        var a = originalToReference;
                        double x = a[0] * center.X + a[1] * center.Y + a[2];
                        double y = a[3] * center.X + a[4] * center.Y + a[5];
                        if (!OriginalImageClassification.Finite(x) || !OriginalImageClassification.Finite(y))
                            throw new ArgumentException("参考图像中心必须为有限坐标");
                        center = new OpenCvSharp.Point2d(x, y);
                    }
                    ordered.Add(new KeyValuePair<double, string>((string)direction == "horizontal" ? center.X : center.Y,
                        detection.Value.Value<string>("category_name")));
                }
                // OrderBy 保留同坐标项的输入顺序，不能去重或按类别集合比较。
                var current = ordered.OrderBy(item => item.Key).Select(item => item.Value).ToList();
                bool ok = current.SequenceEqual(categories.Values<string>(), StringComparer.Ordinal);
                string reason = "期望顺序：" + (categories.Count == 0 ? "（空）" : string.Join(" → ", categories.Values<string>()))
                    + "\n当前顺序：" + (current.Count == 0 ? "（空）" : string.Join(" → ", current));
                results.Add(new JObject
                {
                    ["type"] = "local", ["originating_module"] = "post_process/sequence_check", ["index"] = images.Count,
                    ["origin_index"] = group.Image.OriginalIndex, ["transform"] = JObject.FromObject(group.Image.TransformState.ToDict()),
                    ["ok"] = ok, ["reason"] = reason, ["sample_results"] = new JArray()
                });
                images.Add(group.Image);
            }
            return new ModuleIO(images, results);
        }
    }
}
