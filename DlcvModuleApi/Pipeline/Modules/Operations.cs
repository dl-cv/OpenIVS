using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DlcvModuleApi.Pipeline;
using DlcvModuleApi.Utils;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModuleApi.Pipeline.Modules
{
    public class ModelInferenceModule : OperationModule
    {
        public ModelInferenceModule(ModuleConfig cfg) : base(cfg)
        {
        }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            var imgDict = inputs.img_dict;
            var resultDict = inputs.result_dict;
            var oriImg = inputs.ori_img;
            int currentRound = inputs.current_round;

            // model_config merge
            var modelConfig = inputs.model_config ?? new ModuleConfig();
            modelConfig.task_type = string.IsNullOrEmpty(modelConfig.task_type) ? (Config.task_type ?? "det") : modelConfig.task_type;
            modelConfig.threshold = Config.threshold;
            modelConfig.iou_threshold = Config.iou_threshold;
            modelConfig.return_polygon = Config.return_polygon;
            modelConfig.top_k = Config.top_k;
            modelConfig.epsilon = Config.epsilon;
            modelConfig.category_filter_list = Config.category_filter_list ?? new List<string>();
            modelConfig.combine_ios_threshold = Config.combine_ios_threshold;
            modelConfig.bbox_expand_pixels = Config.bbox_expand_pixels;
            inputs.model_config = modelConfig;

            var currentImgKeys = imgDict.Where(kv => kv.Value.infer_round == currentRound && (kv.Value.status ?? "active") == "active").Select(kv => kv.Key).ToList();
            inputs.current_img_keys = currentImgKeys;
            var imgList = currentImgKeys.Select(k => imgDict[k].img).ToList();

            var results = ProcessImageList(imgList, modelConfig, update);

            // 把当前结果临时放入 inputs（供 result_processor 使用）
            // 用 JObject 传递
            // 在 ResultProcessors 中读取 inputs.current_img_keys 与此结果
            // 我们直接通过 inputs 的扩展字段传递：用 metadata JSON 形式
            // 这里使用临时字段: we serialize results into JArray list
            inputs.originating_module = "model_inference";

            // 将 results 附加到 inputs 的 model_config 里以便下游获取（避免新增字段）
            // 但更清晰的做法：放到一个静态字典，或在 inputs 中临时保留
            // 为简洁，这里附加在 inputs 的 resized_images 作为黑箱通道不会被误用
            // 不这么做，改为返回前把结果写入一个静态缓存映射 current_img_keys -> results
            _TempResultCarrier.Set(currentImgKeys, results);

            return Task.FromResult(inputs);
        }

        private List<JObject> ProcessImageList(List<Mat> imgList, ModuleConfig modelConfig, Func<double, Task> update)
        {
            if (string.IsNullOrEmpty(Config.model_path))
                throw new ArgumentException("模型路径不能为空");

            var model = new dlcv_infer_csharp.Model(Config.model_path, 0);
            var resultList = new List<JObject>();
            int total = imgList.Count;
            for (int i = 0; i < total; i++)
            {
                var img = imgList[i];
                var param = new JObject
                {
                    ["threshold"] = modelConfig.threshold,
                    ["iou_threshold"] = modelConfig.iou_threshold,
                    ["top_k"] = modelConfig.top_k,
                    ["epsilon"] = modelConfig.epsilon,
                    ["return_polygon"] = true
                };
                var res = model.InferBatch(new List<Mat> { img }, param);

                // 转回 JSON（dlcv_infer_api 的 sample_results）
                // 由于 InferBatch 返回结构化结果，因此需要转 JSON 兼容 Python 处理器
                var json = new JObject();
                var sampleArr = new JArray();
                var sample = new JObject();
                var results = new JArray();
                foreach (var obj in res.SampleResults[0].Results)
                {
                    // 统一bbox到Python期望格式：
                    // - 非旋转： [x1,y1,x2,y2]
                    // - 旋转： [cx,cy,w,h,angle]
                    var rawBbox = obj.Bbox ?? new List<double>();
                    List<double> normBbox = new List<double>();
                    bool isRotatedTask = string.Equals(modelConfig.task_type, "rotated_det") || string.Equals(modelConfig.task_type, "旋转框检测");
                    if (isRotatedTask && obj.WithAngle)
                    {
                        if (rawBbox.Count >= 5)
                        {
                            normBbox = new List<double> { rawBbox[0], rawBbox[1], rawBbox[2], rawBbox[3], obj.Angle };
                        }
                        else if (rawBbox.Count >= 4)
                        {
                            // DVP: [x,y,w,h] -> x1,y1,x2,y2; DVT: [x1,y1,x2,y2]
                            double x1, y1, x2, y2;
                            if (model.IsDvpMode)
                            {
                                x1 = rawBbox[0]; y1 = rawBbox[1]; x2 = x1 + rawBbox[2]; y2 = y1 + rawBbox[3];
                            }
                            else
                            {
                                x1 = rawBbox[0]; y1 = rawBbox[1]; x2 = rawBbox[2]; y2 = rawBbox[3];
                            }
                            double w = Math.Max(1, x2 - x1);
                            double h = Math.Max(1, y2 - y1);
                            double cx = x1 + w / 2.0;
                            double cy = y1 + h / 2.0;
                            normBbox = new List<double> { cx, cy, w, h, obj.Angle };
                        }
                    }
                    else
                    {
                        if (rawBbox.Count >= 4)
                        {
                            if (model.IsDvpMode)
                            {
                                double x1 = rawBbox[0], y1 = rawBbox[1], x2 = rawBbox[0] + rawBbox[2], y2 = rawBbox[1] + rawBbox[3];
                                normBbox = new List<double> { x1, y1, x2, y2 };
                            }
                            else
                            {
                                normBbox = new List<double> { rawBbox[0], rawBbox[1], rawBbox[2], rawBbox[3] };
                            }
                        }
                    }

                    var item = new JObject
                    {
                        ["category_id"] = obj.CategoryId,
                        ["category_name"] = obj.CategoryName,
                        ["score"] = obj.Score,
                        ["area"] = obj.Area,
                        ["bbox"] = new JArray(ExpandBboxIfNeeded(normBbox, img.Cols, img.Rows, modelConfig.task_type, Config.bbox_expand_pixels))
                    };
                    if (obj.WithMask && obj.Mask != null && !obj.Mask.Empty())
                    {
                        item["with_mask"] = true;
                    }
                    if (obj.WithAngle)
                    {
                        item["with_angle"] = true;
                        item["angle"] = obj.Angle;
                    }
                    results.Add(item);
                }
                sample["results"] = results;
                sampleArr.Add(sample);
                json["sample_results"] = sampleArr;
                resultList.Add(json);

                if (update != null)
                {
                    update((i + 1.0) / total).GetAwaiter().GetResult();
                }
            }
            model.FreeModel();
            return resultList;
        }

        private static List<double> ExpandBboxIfNeeded(List<double> bbox, int imgW, int imgH, string taskType, int expand)
        {
            if (expand <= 0 || bbox == null) return bbox;
            bool isRotated = taskType == "rotated_det" || taskType == "旋转框检测" || bbox.Count >= 5;
            if (isRotated && bbox.Count >= 5)
            {
                double cx = bbox[0];
                double cy = bbox[1];
                double w = bbox[2] + 2 * expand;
                double h = bbox[3] + 2 * expand;
                // clamp by keeping center inside image
                double halfW = w / 2.0, halfH = h / 2.0;
                double maxExpandX = Math.Min(cx, imgW - cx);
                double maxExpandY = Math.Min(cy, imgH - cy);
                double maxExpand = Math.Min(maxExpandX, maxExpandY);
                if (halfW > maxExpand || halfH > maxExpand)
                {
                    double scale = maxExpand / Math.Max(halfW, halfH);
                    w = bbox[2] + 2 * expand * scale;
                    h = bbox[3] + 2 * expand * scale;
                }
                var nb = new List<double> { cx, cy, w, h, bbox[4] };
                if (bbox.Count > 5) nb.AddRange(bbox.Skip(5));
                return nb;
            }
            else if (bbox.Count >= 4)
            {
                double x1 = Math.Max(0, bbox[0] - expand);
                double y1 = Math.Max(0, bbox[1] - expand);
                double x2 = Math.Min(imgW, bbox[2] + expand);
                double y2 = Math.Min(imgH, bbox[3] + expand);
                var nb = new List<double> { x1, y1, x2, y2 };
                if (bbox.Count > 4) nb.AddRange(bbox.Skip(4));
                return nb;
            }
            return bbox;
        }
    }

    internal static class _TempResultCarrier
    {
        private static readonly Dictionary<string, List<JObject>> Map = new Dictionary<string, List<JObject>>();
        private static string KeyOf(List<Tuple<int, int>> keys)
        {
            return string.Join("|", keys.Select(k => $"{k.Item1},{k.Item2}"));
        }
        public static void Set(List<Tuple<int, int>> keys, List<JObject> results)
        {
            Map[KeyOf(keys)] = results;
        }
        public static List<JObject> Get(List<Tuple<int, int>> keys)
        {
            var k = KeyOf(keys);
            return Map.ContainsKey(k) ? Map[k] : new List<JObject>();
        }
    }

    public class ImageRatioAdjustModule : OperationModule
    {
        public ImageRatioAdjustModule(ModuleConfig cfg) : base(cfg) { }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            var imgDict = inputs.img_dict;
            int currentRound = inputs.current_round;
            var currentKeys = imgDict.Where(kv => kv.Value.infer_round == currentRound && (kv.Value.status ?? "active") == "active").Select(kv => kv.Key).ToList();
            var resized = new List<Tuple<Tuple<int, int>, Mat, Tuple<int, int>>>();
            foreach (var key in currentKeys)
            {
                var src = imgDict[key].img;
                int newW = (int)(src.Cols * Config.horizontal_ratio);
                int newH = (int)(src.Rows * Config.vertical_ratio);
                var dst = new Mat();
                Cv2.Resize(src, dst, new Size(newW, newH));
                resized.Add(new Tuple<Tuple<int, int>, Mat, Tuple<int, int>>(key, dst, new Tuple<int, int>(newW, newH)));
            }
            inputs.resized_images = resized;
            inputs.originating_module = "image_ratio_adjust";
            return Task.FromResult(inputs);
        }
    }

    public class BboxAreaFilterModule : OperationModule
    {
        public BboxAreaFilterModule(ModuleConfig cfg) : base(cfg) { }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            // 此模块在 ResultProcessors 中进行筛选构造 current_result_dict
            inputs.originating_module = "bbox_area_filter";
            return Task.FromResult(inputs);
        }
    }
}


