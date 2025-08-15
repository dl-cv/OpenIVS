using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DlcvModuleApi.Utils;

namespace DlcvModuleApi.Pipeline
{
    public class ProcessPipeline : FlowControlModule
    {
        private readonly List<ProcessModule> _modules;
        public int TotalRound { get; private set; }
        public string TaskType { get; private set; }

        public ProcessPipeline(List<ProcessModule> modules, ModuleConfig cfg = null) : base(cfg)
        {
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
            if (_modules.Count == 0) throw new ArgumentException("模块列表不能为空");
            TotalRound = cfg?.small_img_width != 0 ? (cfg?.small_img_width ?? 1) : 1; // dummy default
            TaskType = cfg?.task_type ?? "pipeline";
        }

        public override async Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            var result = inputs;
            if (result.model_config == null) result.model_config = new ModuleConfig { task_type = TaskType };

            int moduleIndex = 0;
            int currentRound = 0;
            bool hasImageRatioAdjust = false;
            foreach (var module in _modules)
            {
                result.current_round = currentRound;
                if (module is Modules.ImageRatioAdjustModule) hasImageRatioAdjust = true;

                if (module is OperationModule op)
                {
                    ResultProcessModule resultProcessor;
                    if (op is Modules.ModelInferenceModule)
                    {
                        var tt = op.TaskType;
                        if (tt == "cls" || tt == "分类" || tt == "图像分类" || tt == "ocr" || tt == "OCR")
                            resultProcessor = new Modules.ClassificationResultModule();
                        else if (tt == "rotated_det" || tt == "旋转框检测")
                            resultProcessor = new Modules.RotatedDetectionResultModule();
                        else
                            resultProcessor = new Modules.DetectionResultModule();
                    }
                    else if (module is Modules.ImageRatioAdjustModule)
                    {
                        resultProcessor = new Modules.ClassificationResultModule();
                    }
                    else if (module is Modules.BboxAreaFilterModule)
                    {
                        resultProcessor = new Modules.BboxAreaFilterResultModule();
                    }
                    else
                    {
                        resultProcessor = new Modules.ClassificationResultModule();
                    }

                    var generator = new GeneratorModule();
                    result.originating_module = module.GetType().Name.ToLowerInvariant();
                    result.has_image_ratio_adjust = hasImageRatioAdjust;
                    result = await op.ProcessWithResultAndGenerator(result, resultProcessor, generator, update);
                }
                else
                {
                    result = await module.Process(result, update);
                }

                await Task.Yield();
                moduleIndex++;
                currentRound++;
                if (result.total_round <= 0) result.total_round = _modules.Count;
                if (result.model_config == null) result.model_config = new ModuleConfig { task_type = TaskType };
            }
            return result;
        }
    }
}


