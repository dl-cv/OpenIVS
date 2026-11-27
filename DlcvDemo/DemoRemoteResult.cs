using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using static dlcv_infer_csharp.Utils;

namespace DlcvDemo
{
    public static class DemoRemoteResult
    {
        internal static JObject Encode(CSharpResult result)
        {
            var samples = new JArray();
            foreach (var sample in result.SampleResults)
            {
                var objects = new JArray();
                foreach (var item in sample.Results)
                {
                    string mask = null;
                    if (item.WithMask && item.Mask != null && !item.Mask.Empty())
                    {
                        if (!Cv2.ImEncode(".png", item.Mask, out var bytes, (int[])null))
                            throw new InvalidDataException("遮罩编码失败。");
                        mask = Convert.ToBase64String(bytes);
                    }
                    objects.Add(new JObject
                    {
                        ["category_id"] = item.CategoryId, ["category_name"] = item.CategoryName,
                        ["score"] = item.Score, ["area"] = item.Area,
                        ["with_bbox"] = item.WithBbox, ["bbox"] = item.Bbox == null ? null : JArray.FromObject(item.Bbox),
                        ["with_mask"] = item.WithMask, ["mask_png"] = mask,
                        ["with_angle"] = item.WithAngle, ["angle"] = item.Angle, ["extra_info"] = item.ExtraInfo
                    });
                }
                samples.Add(new JObject
                {
                    ["results"] = objects, ["ok"] = sample.Ok, ["reason"] = sample.Reason, ["extra_info"] = sample.ExtraInfo
                });
            }
            string jsonText = result.JsonText;
            if (!string.IsNullOrWhiteSpace(jsonText))
            {
                var json = JObject.Parse(jsonText);
                foreach (var pointer in json.Descendants().OfType<JProperty>().Where(p => p.Name == "mask_ptr").ToArray())
                    pointer.Remove();
                jsonText = json.ToString(Formatting.Indented);
            }
            return new JObject { ["json_text"] = jsonText, ["sample_results"] = samples };
        }

        public static CSharpResult Decode(JObject result)
        {
            var samples = new List<CSharpSampleResult>();
            var masks = new List<Mat>();
            try
            {
                foreach (var sample in (JArray)result["sample_results"])
                {
                    var objects = new List<CSharpObjectResult>();
                    foreach (var item in (JArray)sample["results"])
                    {
                        var encoded = item.Value<string>("mask_png");
                        var mask = encoded == null ? new Mat() : Cv2.ImDecode(Convert.FromBase64String(encoded), ImreadModes.Unchanged);
                        masks.Add(mask);
                        if (encoded != null && (mask.Empty() || mask.Type() != MatType.CV_8UC1))
                            throw new InvalidDataException("遮罩解码失败。");
                        objects.Add(new CSharpObjectResult(item.Value<int>("category_id"), item.Value<string>("category_name"),
                            item.Value<float>("score"), item.Value<float>("area"), (item["bbox"] as JArray)?.ToObject<List<double>>(),
                            item.Value<bool>("with_mask"), mask, item.Value<bool>("with_bbox"),
                            item.Value<bool>("with_angle"), item.Value<float>("angle"), item["extra_info"] as JObject));
                    }
                    samples.Add(new CSharpSampleResult(objects, sample.Value<bool?>("ok"), sample.Value<string>("reason"), sample["extra_info"] as JObject));
                }
                return new CSharpResult(samples, result.Value<string>("json_text"));
            }
            catch
            {
                foreach (var mask in masks) mask.Dispose();
                throw;
            }
        }

        internal static void DisposeMasks(CSharpResult result)
        {
            foreach (var sample in result.SampleResults)
            foreach (var item in sample.Results)
                item.Mask?.Dispose();
        }
    }
}
