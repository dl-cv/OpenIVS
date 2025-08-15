using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DlcvModuleApi.Pipeline;
using DlcvModuleApi.Utils;
using Newtonsoft.Json.Linq;

namespace DlcvModuleApi.Pipeline.Modules
{
    public class ClassificationResultModule : ResultProcessModule
    {
        public ClassificationResultModule(ModuleConfig cfg = null) : base(cfg) { }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            var currentImgKeys = inputs.current_img_keys;
            var imgDict = inputs.img_dict;
            var resultDict = inputs.result_dict;
            int currentRound = inputs.current_round;
            var modelConfig = inputs.model_config ?? new ModuleConfig { task_type = "classification_result" };
            string originating = inputs.originating_module ?? "model_inference";
            var categoryFilter = modelConfig.category_filter_list ?? new List<string>();

            var currentResultDict = new Dictionary<Tuple<int, int>, ResultEntry>();

            if (originating == "image_ratio_adjust")
            {
                foreach (var tup in inputs.resized_images)
                {
                    var key = tup.Item1;
                    var newSize = tup.Item3; // (w,h)
                    var newKey = new Tuple<int, int>(currentRound, key.Item2);
                    if (resultDict.ContainsKey(key))
                    {
                        var src = resultDict[key];
                        var copy = new ResultEntry
                        {
                            current_round = currentRound,
                            task_type = src.task_type,
                            predictions = new Dictionary<int, Prediction>()
                        };

                        foreach (var kv in src.predictions)
                        {
                            var p = kv.Value;
                            var bbox = p.bbox;
                            if (bbox != null && bbox.Count >= 4)
                            {
                                var wRatio = (double)newSize.Item1 / imgDict[key].width;
                                var hRatio = (double)newSize.Item2 / imgDict[key].height;
                                int nx1 = (int)(bbox[0] * wRatio);
                                int ny1 = (int)(bbox[1] * hRatio);
                                int nx2 = (int)(bbox[2] * wRatio);
                                int ny2 = (int)(bbox[3] * hRatio);
                                var nb = new List<double> { nx1, ny1, nx2, ny2 };
                                p.bbox = nb;
                                if (p.metadata != null && p.metadata["global_bbox"] != null)
                                {
                                    int gx = imgDict[key].global_x;
                                    int gy = imgDict[key].global_y;
                                    p.metadata["global_bbox"] = new JArray(nx1 + gx, ny1 + gy, nx2 + gx, ny2 + gy);
                                }
                            }
                            copy.predictions[copy.predictions.Count] = p;
                        }
                        currentResultDict[newKey] = copy;
                    }
                    else
                    {
                        var newW = newSize.Item1;
                        var newH = newSize.Item2;
                        var bbox = new List<double> { 0, 0, newW, newH };
                        int gx = imgDict[key].global_x;
                        int gy = imgDict[key].global_y;
                        var metadata = new JObject
                        {
                            ["combine_flag"] = false,
                            ["slice_index"] = new JArray(new JArray(imgDict[key].slice_positions[0].Item1, imgDict[key].slice_positions[0].Item2)),
                            ["global_x"] = gx,
                            ["global_y"] = gy,
                            ["global_bbox"] = new JArray(0 + gx, 0 + gy, newW + gx, newH + gy)
                        };
                        var pred = new Prediction
                        {
                            category_id = key.Item2,
                            category_name = $"adjusted_image_{key.Item2}",
                            score = 1.0f,
                            bbox = bbox,
                            area = newW * newH,
                            with_mask = false,
                            mask = null,
                            with_angle = false,
                            angle = -100f,
                            metadata = metadata
                        };
                        currentResultDict[newKey] = new ResultEntry
                        {
                            current_round = currentRound,
                            task_type = "image_ratio_adjust",
                            predictions = new Dictionary<int, Prediction> { { 0, pred } }
                        };
                    }
                }

                foreach (var kv in currentResultDict)
                {
                    inputs.result_dict[kv.Key] = kv.Value;
                }
                return Task.FromResult(inputs);
            }

            // normal classification/ocr path
            var resultList = _TempResultCarrier.Get(currentImgKeys);
            if (resultList == null || resultList.Count == 0)
            {
                return Task.FromResult(inputs);
            }

            for (int i = 0; i < currentImgKeys.Count; i++)
            {
                var imgKey = currentImgKeys[i];
                var imgResult = resultList[i];
                var predictions = new Dictionary<int, Prediction>();
                int count = 0;
                var results = (JArray)imgResult["sample_results"][0]["results"];
                foreach (JObject each in results)
                {
                    var cname = each["category_name"]?.Value<string>();
                    if (categoryFilter.Contains(cname) && cname != "全选") continue;
                    int smallW = imgDict[imgKey].width;
                    int smallH = imgDict[imgKey].height;
                    var bbox = new List<double> { 0, 0, smallW, smallH };
                    int gx = imgDict[imgKey].global_x;
                    int gy = imgDict[imgKey].global_y;
                    var gb = new JArray(0 + gx, 0 + gy, smallW + gx, smallH + gy);
                    var meta = new JObject
                    {
                        ["combine_flag"] = false,
                        ["slice_index"] = new JArray(new JArray(imgDict[imgKey].slice_positions[0].Item1, imgDict[imgKey].slice_positions[0].Item2)),
                        ["global_x"] = gx,
                        ["global_y"] = gy,
                        ["global_bbox"] = gb
                    };
                    var pred = new Prediction
                    {
                        category_id = each["category_id"]?.Value<int>() ?? 0,
                        category_name = cname,
                        score = (float)(each["score"]?.Value<double>() ?? 0.0),
                        bbox = bbox,
                        area = each["area"]?.Value<int>(),
                        with_mask = each["with_mask"]?.Value<bool>() ?? false,
                        mask = null,
                        with_angle = each["with_angle"]?.Value<bool>() ?? false,
                        angle = (float)(each["angle"]?.Value<double>() ?? -100),
                        metadata = meta
                    };
                    predictions[count++] = pred;
                }
                if (predictions.Count > 0)
                {
                    currentResultDict[imgKey] = new ResultEntry
                    {
                        current_round = currentRound,
                        task_type = modelConfig.task_type,
                        predictions = predictions
                    };
                }
            }

            foreach (var kv in currentResultDict)
            {
                inputs.result_dict[kv.Key] = kv.Value;
            }
            return Task.FromResult(inputs);
        }
    }

    public class DetectionResultModule : ResultProcessModule
    {
        public DetectionResultModule(ModuleConfig cfg = null) : base(cfg) { }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            var currentImgKeys = inputs.current_img_keys;
            var imgDict = inputs.img_dict;
            var resultDict = inputs.result_dict;
            int currentRound = inputs.current_round;
            var modelConfig = inputs.model_config ?? new ModuleConfig { task_type = "detection_result" };
            var categoryFilter = modelConfig.category_filter_list ?? new List<string>();

            var resultList = _TempResultCarrier.Get(currentImgKeys);
            if (resultList == null || resultList.Count == 0)
            {
                return Task.FromResult(inputs);
            }

            var currentResultDict = new Dictionary<Tuple<int, int>, ResultEntry>();
            foreach (var pair in currentImgKeys.Select((k, idx) => new { k, idx }))
            {
                var imgKey = pair.k;
                var imgResult = resultList[pair.idx];
                var preds = new Dictionary<int, Prediction>();
                int cnt = 0;
                var arr = (JArray)imgResult["sample_results"][0]["results"];
                foreach (JObject each in arr)
                {
                    var cname = each["category_name"]?.Value<string>();
                    if (categoryFilter.Contains(cname) && cname != "全选") continue;
                    var bboxToken = each["bbox"] as JArray;
                    if (bboxToken == null || bboxToken.Count != 4) continue;
                    double x1 = bboxToken[0].Value<double>();
                    double y1 = bboxToken[1].Value<double>();
                    double x2 = bboxToken[2].Value<double>();
                    double y2 = bboxToken[3].Value<double>();

                    int pad = 0;
                    if (modelConfig != null)
                    {
                        // optional detect_model_padding (not present in ModuleConfig, keep 0)
                    }

                    int gx = imgDict[imgKey].global_x;
                    int gy = imgDict[imgKey].global_y;
                    var gb = new JArray((int)(x1 + gx), (int)(y1 + gy), (int)(x2 + gx), (int)(y2 + gy));
                    var meta = new JObject
                    {
                        ["combine_flag"] = false,
                        ["slice_index"] = new JArray(new JArray(imgDict[imgKey].slice_positions[0].Item1, imgDict[imgKey].slice_positions[0].Item2)),
                        ["global_x"] = gx,
                        ["global_y"] = gy,
                        ["global_bbox"] = gb
                    };

                    var pred = new Prediction
                    {
                        category_id = each["category_id"]?.Value<int>() ?? 0,
                        category_name = cname,
                        score = (float)(each["score"]?.Value<double>() ?? 0.0),
                        bbox = new List<double> { x1, y1, x2, y2 },
                        area = each["area"]?.Value<int>(),
                        with_mask = each["with_mask"]?.Value<bool>() ?? false,
                        mask = null,
                        with_angle = false,
                        angle = -100f,
                        metadata = meta
                    };
                    preds[cnt++] = pred;
                }
                if (cnt > 0)
                {
                    currentResultDict[imgKey] = new ResultEntry
                    {
                        current_round = currentRound,
                        task_type = modelConfig.task_type,
                        predictions = preds
                    };
                }
            }

            foreach (var kv in currentResultDict)
            {
                inputs.result_dict[kv.Key] = kv.Value;
            }
            return Task.FromResult(inputs);
        }
    }

    public class RotatedDetectionResultModule : ResultProcessModule
    {
        public RotatedDetectionResultModule(ModuleConfig cfg = null) : base(cfg) { }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            var currentImgKeys = inputs.current_img_keys;
            var imgDict = inputs.img_dict;
            int currentRound = inputs.current_round;
            var modelConfig = inputs.model_config ?? new ModuleConfig { task_type = "rotated_detection_result" };
            var categoryFilter = modelConfig.category_filter_list ?? new List<string>();

            var resultList = _TempResultCarrier.Get(currentImgKeys);
            if (resultList == null || resultList.Count == 0)
            {
                return Task.FromResult(inputs);
            }

            var currentResultDict = new Dictionary<Tuple<int, int>, ResultEntry>();
            foreach (var pair in currentImgKeys.Select((k, idx) => new { k, idx }))
            {
                var imgKey = pair.k;
                var imgResult = resultList[pair.idx];
                var preds = new Dictionary<int, Prediction>();
                int cnt = 0;
                var arr = (JArray)imgResult["sample_results"][0]["results"];
                foreach (JObject each in arr)
                {
                    var cname = each["category_name"]?.Value<string>();
                    if (categoryFilter.Contains(cname) && cname != "全选") continue;
                    var bboxToken = each["bbox"] as JArray;
                    if (bboxToken == null || bboxToken.Count < 5) continue;
                    double cx = bboxToken[0].Value<double>();
                    double cy = bboxToken[1].Value<double>();
                    double w = bboxToken[2].Value<double>();
                    double h = bboxToken[3].Value<double>();
                    double angle = bboxToken[4].Value<double>();

                    int gx = imgDict[imgKey].global_x;
                    int gy = imgDict[imgKey].global_y;
                    var globalRot = new JArray(cx + gx, cy + gy, w, h, angle);
                    var meta = new JObject
                    {
                        ["combine_flag"] = false,
                        ["slice_index"] = new JArray(new JArray(imgDict[imgKey].slice_positions[0].Item1, imgDict[imgKey].slice_positions[0].Item2)),
                        ["global_x"] = gx,
                        ["global_y"] = gy,
                        ["global_bbox"] = globalRot,
                        ["is_rotated"] = true
                    };

                    var pred = new Prediction
                    {
                        category_id = each["category_id"]?.Value<int>() ?? 0,
                        category_name = cname,
                        score = (float)(each["score"]?.Value<double>() ?? 0.0),
                        bbox = new List<double> { cx, cy, w, h, angle },
                        area = (int)(w * h),
                        with_mask = each["with_mask"]?.Value<bool>() ?? false,
                        mask = null,
                        with_angle = true,
                        angle = (float)angle,
                        metadata = meta
                    };
                    preds[cnt++] = pred;
                }
                if (cnt > 0)
                {
                    currentResultDict[imgKey] = new ResultEntry
                    {
                        current_round = currentRound,
                        task_type = modelConfig.task_type,
                        predictions = preds
                    };
                }
            }

            foreach (var kv in currentResultDict)
            {
                inputs.result_dict[kv.Key] = kv.Value;
            }
            return Task.FromResult(inputs);
        }
    }

    public class BboxAreaFilterResultModule : ResultProcessModule
    {
        public BboxAreaFilterResultModule(ModuleConfig cfg = null) : base(cfg) { }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            // 根据上一轮结果进行面积筛选
            var resultDict = inputs.result_dict;
            int currentRound = inputs.current_round;
            var curr = new Dictionary<Tuple<int, int>, ResultEntry>();
            int totalPred = 0, filtered = 0;
            foreach (var kv in resultDict)
            {
                if (kv.Value.current_round == currentRound - 1)
                {
                    var filteredDict = new Dictionary<int, Prediction>();
                    foreach (var p in kv.Value.predictions)
                    {
                        var pr = p.Value;
                        totalPred++;
                        if (pr.metadata?["combine_flag"]?.Value<bool>() == true) continue;
                        double area;
                        if (pr.metadata?["is_rotated"]?.Value<bool>() == true && pr.bbox.Count >= 4)
                        {
                            area = pr.bbox[2] * pr.bbox[3];
                        }
                        else if (pr.bbox != null && pr.bbox.Count >= 4)
                        {
                            area = Math.Abs(pr.bbox[2] - pr.bbox[0]) * Math.Abs(pr.bbox[3] - pr.bbox[1]);
                        }
                        else continue;

                        bool keep = Config.filter_mode == "less" ? area < Config.area_threshold : area > Config.area_threshold;
                        if (keep)
                        {
                            filteredDict[filteredDict.Count] = pr;
                        }
                        else
                        {
                            filtered++;
                        }
                    }
                    if (filteredDict.Count > 0)
                    {
                        curr[new Tuple<int, int>(currentRound, kv.Key.Item2)] = new ResultEntry
                        {
                            current_round = currentRound,
                            task_type = kv.Value.task_type,
                            predictions = filteredDict
                        };
                    }
                }
            }

            foreach (var kv in curr)
            {
                inputs.result_dict[kv.Key] = kv.Value;
            }
            return Task.FromResult(inputs);
        }
    }
}


