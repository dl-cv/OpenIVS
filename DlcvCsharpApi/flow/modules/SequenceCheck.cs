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
            // 仅兼容旧流程类型；分类输出统一使用新主标识。
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
                    ordered.Add(new KeyValuePair<double, string>((string)direction == "horizontal" ? center.X : center.Y,
                        detection.Value.Value<string>("category_name")));
                }
                // OrderBy 保留同坐标项的输入顺序，不能去重或按类别集合比较。
                var current = ordered.OrderBy(item => item.Key).Select(item => item.Value).ToList();
                bool ok = current.SequenceEqual(categories.Values<string>(), StringComparer.Ordinal);
                string reason = "期望顺序：" + (categories.Count == 0 ? "（空）" : string.Join(" → ", categories.Values<string>()))
                    + "\n当前顺序：" + (current.Count == 0 ? "（空）" : string.Join(" → ", current));
                results.Add(OriginalImageClassification.Result(group, images.Count, "post_process/sequence_check", ok, ok ? "OK" : "NG", false, reason));
                images.Add(group.Image);
            }
            return new ModuleIO(images, results);
        }
    }
}
