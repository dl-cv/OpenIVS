using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DlcvModuleApi.Pipeline;
using DlcvModuleApi.Pipeline.Modules;
using DlcvModuleApi.Utils;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModuleApi.Api
{
    public class ModuleFactory
    {
        public static ProcessModule Create(JObject cfg)
        {
            string moduleType = cfg.Value<string>("module_type") ?? "model_inference";
            var mc = cfg.ToObject<ModuleConfig>();

            switch (moduleType)
            {
                case "model_inference":
                    if (string.IsNullOrEmpty(mc.model_path))
                        throw new ArgumentException("模型推理模块配置缺少必要的model_path参数");
                    return new ModelInferenceModule(mc);
                case "sliding_window":
                    return new SlideWindowModule(mc);
                case "single_image":
                    return new SingleImageModule(mc);
                case "image_ratio_adjust":
                    return new ImageRatioAdjustModule(mc);
                case "bbox_area_filter":
                    return new BboxAreaFilterModule(mc);
                case "classification_result":
                    return new ClassificationResultModule(mc);
                case "detection_result":
                    return new DetectionResultModule(mc);
                case "rotated_detection_result":
                    return new RotatedDetectionResultModule(mc);
                case "combine_results":
                    return new CombineResultsModule(mc);
                case "visualization":
                    return new VisualizationModule(mc);
                case "save_image":
                    return new SaveImageModule(mc);
                case "pipeline":
                    var modulesArr = cfg["modules"] as JArray;
                    if (modulesArr == null) throw new ArgumentException("流水线模块配置缺少必要的modules参数");
                    var modules = new List<ProcessModule>();
                    foreach (var m in modulesArr)
                    {
                        modules.Add(Create((JObject)m));
                    }
                    return new ProcessPipeline(modules, mc);
                default:
                    throw new ArgumentException($"未知的模块类型: {moduleType}");
            }
        }
    }

    public class DlcvModulePipelineApi
    {
        public static JObject PredictWithModulesList(
            string imgPath,
            JArray modules_list,
            bool return_vis = false,
            int? module_index = null,
            JObject data = null)
        {
            var img = ImageUtils.ImreadAny(imgPath);

            // 确保第一个模块是滑窗或单图，若不是则补一个 single_image
            var ml = modules_list.ToList();
            if (ml.Count == 0 || (((JObject)ml[0]).Value<string>("module_type") != "sliding_window" && ((JObject)ml[0]).Value<string>("module_type") != "single_image"))
            {
                ml.Insert(0, JObject.FromObject(new { module_type = "single_image", task_type = "single_image" }));
            }

            // 为 model_inference 自动插入 auto process 的 combine 设置（与 Python 优化一致）
            var optimized = new List<JObject>();
            int i = 0;
            while (i < ml.Count)
            {
                var cur = (JObject)ml[i].DeepClone();
                if ((cur.Value<string>("module_type") == "model_inference") && i + 2 < ml.Count)
                {
                    var next1 = (JObject)ml[i + 1];
                    var next2 = (JObject)ml[i + 2];
                    if ((next1.Value<string>("module_type") == "detection_result" || next1.Value<string>("module_type") == "classification_result") && next2.Value<string>("module_type") == "combine_results")
                    {
                        if (!cur.ContainsKey("auto_process_results")) cur["auto_process_results"] = true;
                        if (next2.ContainsKey("combine_ios_threshold")) cur["combine_ios_threshold"] = next2.Value<double>("combine_ios_threshold");
                        optimized.Add(cur);
                        i += 3;
                        continue;
                    }
                }
                optimized.Add(cur);
                i += 1;
            }

            // 构造 pipeline
            var modules = new List<ProcessModule>();
            foreach (var m in optimized)
            {
                modules.Add(ModuleFactory.Create((JObject)m));
            }
            var pipeline = new ProcessPipeline(modules, new ModuleConfig { task_type = "pipeline" });

            var io = new PipelineIO
            {
                ori_img = img,
                current_round = 0,
                total_round = modules.Count,
                model_config = new ModuleConfig()
            };

            var res = pipeline.Process(io).GetAwaiter().GetResult();

            // 构建 dlcv_infer_api 格式输出
            var sampleResults = BuildSampleResults(res.result_dict, res.img_dict, res.ori_img, module_index);
            var finalResult = new JObject
            {
                ["code"] = "00000",
                ["sample_results"] = sampleResults,
                ["all_small_img_pos_list"] = new JArray()
            };

            if (return_vis)
            {
                // 可选：此处可加可视化，暂不实现（与 Python 对齐结构，不影响兼容）
            }

            return finalResult;
        }

        private static JArray BuildSampleResults(Dictionary<Tuple<int, int>, ResultEntry> resultDict, Dictionary<Tuple<int, int>, ImgDictEntry> imgDict, Mat ori, int? moduleIndex)
        {
            // 复刻 Python utils_image.build_sample_results 的关键字段
            int maxRound = moduleIndex ?? -2;
            if (maxRound == -2)
            {
                foreach (var kv in resultDict)
                {
                    maxRound = Math.Max(maxRound, kv.Value.current_round);
                }
            }

            var lastRound = new Dictionary<Tuple<int, int>, List<JObject>>();
            foreach (var kv in resultDict)
            {
                if (kv.Value.current_round == maxRound)
                {
                    foreach (var p in kv.Value.predictions)
                    {
                        var pred = p.Value;
                        if (pred.metadata?["combine_flag"]?.Value<bool>() == true) continue;
                        var item = new JObject
                        {
                            ["category_id"] = pred.category_id,
                            ["category_name"] = pred.category_name,
                            ["score"] = pred.score,
                            ["bbox"] = pred.metadata?["global_bbox"] ?? new JArray(pred.bbox ?? new List<double> { 0, 0, 0, 0 })
                        };
                        if (pred.with_mask && pred.mask != null && !pred.mask.Empty())
                        {
                            item["with_mask"] = true;
                        }
                        if (!lastRound.ContainsKey(kv.Key)) lastRound[kv.Key] = new List<JObject>();
                        lastRound[kv.Key].Add(item);
                    }
                }
            }

            var all = new List<JObject>();
            foreach (var kv in lastRound)
            {
                all.AddRange(kv.Value);
            }
            var sample = new JObject
            {
                ["results"] = new JArray(all)
            };
            var arr = new JArray { sample };
            return arr;
        }
    }
}


