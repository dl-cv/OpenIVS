using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using DlcvModuleApi.Utils;

namespace DlcvModuleApi.Pipeline
{
    public abstract class ProcessModule
    {
        public string ModuleType { get; protected set; } = "operation";
        public ModuleConfig Config { get; protected set; }
        protected ProcessModule(ModuleConfig cfg)
        {
            Config = cfg ?? new ModuleConfig();
        }
        public abstract Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null);
        public virtual Task<PipelineIO> Invoke(PipelineIO inputs, Func<double, Task> update = null)
        {
            return Process(inputs, update);
        }
    }

    public abstract class ImageGeneratorModule : ProcessModule
    {
        protected ImageGeneratorModule(ModuleConfig cfg) : base(cfg)
        {
            ModuleType = "image_generator";
        }
    }

    public abstract class OperationModule : ProcessModule
    {
        public string TaskType { get; protected set; }
        protected OperationModule(ModuleConfig cfg) : base(cfg)
        {
            ModuleType = "operation";
            TaskType = cfg?.task_type ?? "det";
        }

        public async Task<PipelineIO> ProcessWithResultAndGenerator(
            PipelineIO inputs,
            ResultProcessModule resultProcessor,
            GeneratorModule generator,
            Func<double, Task> update = null)
        {
            var opResult = await Process(inputs, update);
            var processed = await resultProcessor.Process(opResult, update);

            // 检测/分割/旋转 合并
            if (resultProcessor is Modules.DetectionResultModule || resultProcessor is Modules.RotatedDetectionResultModule)
            {
                var combineThreshold = opResult.model_config.combine_ios_threshold;

                var combineModule = new Modules.CombineResultsModule(new ModuleConfig { combine_ios_threshold = combineThreshold });
                processed = await combineModule.Process(processed, update);
            }

            // 更新 result_dict（合入 current_result_dict）
            if (processed.result_dict != null && processed.result_dict.Count >= 0)
            {
                // nothing extra, already merged inside processors
            }

            var generated = await generator.Process(processed, update);
            return generated;
        }
    }

    public abstract class ResultProcessModule : ProcessModule
    {
        protected ResultProcessModule(ModuleConfig cfg) : base(cfg)
        {
            ModuleType = "result_processor";
        }
    }

    public class GeneratorModule : ProcessModule
    {
        public string TaskType { get; private set; }
        public GeneratorModule(ModuleConfig cfg = null) : base(cfg)
        {
            ModuleType = "image_generator_processor";
            TaskType = cfg?.task_type ?? "generator";
        }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            // 复刻 Python GeneratorModule 逻辑
            var resultDict = inputs.result_dict;
            var imgDict = inputs.img_dict;
            int currentRound = inputs.current_round;
            int totalRound = inputs.total_round;
            var oriImg = inputs.ori_img;
            var modelConfig = inputs.model_config ?? new ModuleConfig { task_type = TaskType };
            var originatingModule = inputs.originating_module ?? "model_inference";

            if (currentRound == (totalRound - 1))
            {
                return Task.FromResult(inputs);
            }

            var currentResultsKeys = new List<Tuple<int, int>>();
            foreach (var kv in resultDict)
            {
                if (kv.Key.Item1 == currentRound - 1)
                {
                    currentResultsKeys.Add(kv.Key);
                }
            }

            int keyCounter = 0;
            string originPath = imgDict.Count > 0 ? imgDict[new Tuple<int, int>(0, 0)].origin_path : "memory_image";

            if (originatingModule == "model_inference")
            {
                bool isClassification = modelConfig.task_type == "cls" || modelConfig.task_type == "分类" || modelConfig.task_type == "图像分类" || modelConfig.task_type == "ocr" || modelConfig.task_type == "OCR";
                if (isClassification)
                {
                    foreach (var key in currentResultsKeys)
                    {
                        foreach (var resultKv in resultDict[key].predictions)
                        {
                            var prediction = resultKv.Value;
                            if (prediction.metadata?["combine_flag"]?.Value<bool>() == true) continue;
                            var newKey = new Tuple<int, int>(currentRound, keyCounter++);

                            var src = imgDict[key];
                            imgDict[newKey] = new ImgDictEntry
                            {
                                origin_path = originPath,
                                parent_key = key,
                                global_x = src.global_x,
                                global_y = src.global_y,
                                slice_positions = src.slice_positions,
                                width = src.width,
                                height = src.height,
                                img = src.img,
                                infer_round = currentRound + 1,
                                status = "active",
                                generated_by = (currentRound, resultKv.Key)
                            };
                        }
                    }
                }
                else
                {
                    foreach (var key in currentResultsKeys)
                    {
                        foreach (var resultKv in resultDict[key].predictions)
                        {
                            var prediction = resultKv.Value;
                            if (prediction.metadata?["combine_flag"]?.Value<bool>() == true) continue;

                            var newKey = new Tuple<int, int>(currentRound + 1, keyCounter++);
                            var globalBboxToken = prediction.metadata?["global_bbox"];
                            bool isRotated = prediction.metadata?["is_rotated"]?.Value<bool>() ?? false;

                            OpenCvSharp.Mat crop;
                            int gx = 0, gy = 0;
                            if (isRotated && globalBboxToken != null && globalBboxToken.Type == JTokenType.Array && ((JArray)globalBboxToken).Count >= 5)
                            {
                                // [cx, cy, w, h, angle (radian)]
                                double cx = globalBboxToken[0].Value<double>();
                                double cy = globalBboxToken[1].Value<double>();
                                double w = globalBboxToken[2].Value<double>();
                                double h = globalBboxToken[3].Value<double>();
                                double angle = globalBboxToken[4].Value<double>();
                                double angleDeg = angle * 180.0 / Math.PI;
                                var rot = OpenCvSharp.Cv2.GetRotationMatrix2D(new OpenCvSharp.Point2f((float)cx, (float)cy), angleDeg, 1.0);
                                rot.Set(0, 2, rot.Get<double>(0, 2) + (w / 2.0) - cx);
                                rot.Set(1, 2, rot.Get<double>(1, 2) + (h / 2.0) - cy);
                                crop = new OpenCvSharp.Mat();
                                OpenCvSharp.Cv2.WarpAffine(oriImg, crop, rot, new OpenCvSharp.Size((int)w, (int)h));
                                gx = (int)(cx - w / 2.0);
                                gy = (int)(cy - h / 2.0);
                            }
                            else if (globalBboxToken != null && globalBboxToken.Type == JTokenType.Array && ((JArray)globalBboxToken).Count >= 4)
                            {
                                int x1 = (int)globalBboxToken[0].Value<double>();
                                int y1 = (int)globalBboxToken[1].Value<double>();
                                int x2 = (int)globalBboxToken[2].Value<double>();
                                int y2 = (int)globalBboxToken[3].Value<double>();
                                x2 = Math.Max(x1 + 1, x2);
                                y2 = Math.Max(y1 + 1, y2);
                                var rect = new OpenCvSharp.Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
                                crop = new OpenCvSharp.Mat(oriImg, rect).Clone();
                                gx = x1; gy = y1;
                            }
                            else
                            {
                                continue;
                            }

                            imgDict[newKey] = new ImgDictEntry
                            {
                                origin_path = originPath,
                                parent_key = key,
                                global_x = gx,
                                global_y = gy,
                                slice_positions = imgDict[key].slice_positions,
                                width = crop.Width,
                                height = crop.Height,
                                img = crop,
                                infer_round = currentRound + 1,
                                status = "active",
                                generated_by = (currentRound, resultKv.Key),
                                is_rotated = isRotated,
                                rotated_rect = isRotated ? new List<double> { gx + crop.Width / 2.0, gy + crop.Height / 2.0, crop.Width, crop.Height } : null
                            };
                        }
                    }
                }
            }
            else if (originatingModule == "image_ratio_adjust")
            {
                foreach (var tup in inputs.resized_images)
                {
                    var srcKey = tup.Item1;
                    var resized = tup.Item2;
                    var newSize = tup.Item3;

                    var newKey = new Tuple<int, int>(currentRound + 1, keyCounter++);
                    var src = imgDict[srcKey];
                    imgDict[newKey] = new ImgDictEntry
                    {
                        origin_path = src.origin_path,
                        parent_key = srcKey,
                        global_x = src.global_x,
                        global_y = src.global_y,
                        slice_positions = src.slice_positions,
                        width = newSize.Item1,
                        height = newSize.Item2,
                        img = resized,
                        infer_round = currentRound + 1,
                        status = "active",
                        generated_by = (currentRound, 0)
                    };
                }
            }

            return Task.FromResult(inputs);
        }
    }

    public abstract class FlowControlModule : ProcessModule
    {
        protected FlowControlModule(ModuleConfig cfg) : base(cfg)
        {
            ModuleType = "flow_control";
        }
    }
}


