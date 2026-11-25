using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace DlcvModules
{
    public class ImageAngle : BaseModule
    {
        static ImageAngle()
        {
            ModuleRegistry.Register("post_process/image_angle", typeof(ImageAngle));
        }

        public ImageAngle(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context) { }

        private static string AngleLabel(ModuleImage image, int places)
        {
            if (image?.TransformState == null)
                throw new ArgumentException("获取图像角度需要携带变换状态的图像");
            // 本地运行时用 null 表示未发生变换的单位矩阵。
            var m = image.TransformState.AffineMatrix2x3 ?? new double[] { 1, 0, 0, 0, 1, 0 };
            if (m.Length != 6) throw new ArgumentException("获取图像角度需要有限的 2×3 变换矩阵");
            foreach (double value in m)
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentException("获取图像角度需要有限的 2×3 变换矩阵");
            double xx = m[0] * m[0] + m[3] * m[3];
            double yy = m[1] * m[1] + m[4] * m[4];
            double xy = m[0] * m[1] + m[3] * m[4];
            double scale = (xx + yy) / 2;
            if (!(scale > 0) || double.IsInfinity(scale) || !(m[0] * m[4] - m[1] * m[3] > 0) ||
                Math.Abs(xx / scale - 1) > 1e-6 || Math.Abs(yy / scale - 1) > 1e-6 || Math.Abs(xy / scale) > 1e-6)
                throw new ArgumentException("获取图像角度仅支持旋转、平移和正等比缩放矩阵");
            // 当前图右轴映回原图；先消除半值附近三角计算噪声，再十进制四舍五入。
            double angle = Math.Atan2(m[1], m[0]) * 180 / Math.PI;
            angle = (angle % 360 + 360) % 360;
            decimal valueAngle = decimal.Parse(Math.Round(angle, 12).ToString("F12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            valueAngle = Math.Round(valueAngle, places, MidpointRounding.AwayFromZero) % 360;
            return valueAngle.ToString("F" + places, CultureInfo.InvariantCulture);
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();
            int places = 0;
            if (Properties.TryGetValue("decimal_places", out object raw))
            {
                var token = raw == null ? JValue.CreateNull() : JToken.FromObject(raw);
                if (token.Type != JTokenType.Integer || token.Value<long>() < 0 || token.Value<long>() > 2)
                    throw new ArgumentException("小数位数必须是 0～2 的整数");
                places = token.Value<int>();
            }
            var output = new JArray();
            foreach (var token in results)
            {
                var entry = token as JObject;
                var detections = entry?["sample_results"] as JArray;
                if (entry?["type"]?.Type != JTokenType.String || (string)entry["type"] != "local" || detections == null || detections.Count == 0)
                {
                    output.Add(token);
                    continue;
                }
                var index = entry["index"];
                if (index?.Type != JTokenType.Integer || index.Value<long>() < 0 || index.Value<long>() >= images.Count)
                    throw new ArgumentException("获取图像角度：结果 index 无法定位输入图像");
                string label = AngleLabel(images[index.Value<int>()], places);
                var copy = (JObject)entry.DeepClone();
                foreach (var detection in (JArray)copy["sample_results"])
                    if (detection is JObject obj) obj["category_name"] = label;
                if (copy.Property("category_name") != null) copy["category_name"] = label;
                output.Add(copy);
            }
            return new ModuleIO(images, output);
        }
    }
}
