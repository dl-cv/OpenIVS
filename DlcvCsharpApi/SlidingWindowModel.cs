using System;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace dlcv_infer_csharp
{
    public class SlidingWindowModel : Model
    {
        public SlidingWindowModel(
            string modelPath,
            int device_id,
            int small_img_width = 832,
            int small_img_height = 704,
            int horizontal_overlap = 16,
            int vertical_overlap = 16,
            float threshold = 0.5f,
            float iou_threshold = 0.2f,
            float combine_ios_threshold = 0.2f)
        {
            var config = new JObject
            {
                ["type"] = "sliding_window_pipeline",
                ["model_path"] = modelPath,
                ["device_id"] = device_id,
                ["small_img_width"] = small_img_width,
                ["small_img_height"] = small_img_height,
                ["horizontal_overlap"] = horizontal_overlap,
                ["vertical_overlap"] = vertical_overlap,
                ["threshold"] = threshold,
                ["iou_threshold"] = iou_threshold,
                ["combine_ios_threshold"] = combine_ios_threshold
            };

            var setting = new JsonSerializerSettings() { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii };

            string jsonStr = JsonConvert.SerializeObject(config, setting);

            IntPtr resultPtr = DllLoader.Instance.dlcv_load_model(jsonStr);
            var resultJson = Marshal.PtrToStringAnsi(resultPtr);
            var resultObject = JObject.Parse(resultJson);

            Console.WriteLine("SlidingWindowModel load result: " + resultObject.ToString());
            if (resultObject.ContainsKey("model_index"))
            {
                modelIndex = resultObject["model_index"].Value<int>();
            }
            else
            {
                throw new Exception("加载滑窗裁图模型失败：" + resultObject.ToString());
            }
            DllLoader.Instance.dlcv_free_result(resultPtr);
        }
    }
}

