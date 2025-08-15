using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DlcvModuleApi.Utils;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModuleApi.Pipeline.Modules
{
    public class SlideWindowModule : ImageGeneratorModule
    {
        public SlideWindowModule(ModuleConfig cfg) : base(cfg)
        {
        }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            // inputs 可以是已经包含 ori_img 和 img_path 的 PipelineIO
            var ori = inputs.ori_img;
            var imgPath = inputs.img_dict.Count > 0 ? inputs.img_dict[new Tuple<int, int>(0, 0)].origin_path : "memory_image";
            int currentRound = 0;
            inputs.current_round = currentRound;

            int sw = Config.small_img_width;
            int sh = Config.small_img_height;
            int ho = Config.horizontal_overlap;
            int vo = Config.vertical_overlap;

            Cv2.CvtColor(ori, ori, ColorConversionCodes.BGR2BGR); // no-op ensure valid

            int imgH = ori.Rows;
            int imgW = ori.Cols;

            if (sh <= 0 || sw <= 0)
            {
                throw new ArgumentException("裁剪尺寸必须大于零");
            }

            int rowNum, colNum;
            if (sh >= imgH) rowNum = 1; else { int effH = sh - vo; rowNum = imgH / effH + ((imgH % effH) > 0 ? 1 : 0); }
            if (sw >= imgW) colNum = 1; else { int effW = sw - ho; colNum = imgW / effW + ((imgW % effW) > 0 ? 1 : 0); }

            sw = Math.Min(sw, imgW);
            sh = Math.Min(sh, imgH);

            int counter = 0;
            var imgDict = inputs.img_dict;
            var resDict = inputs.result_dict;

            for (int i = 0; i < rowNum; i++)
            {
                for (int j = 0; j < colNum; j++)
                {
                    int startX = j * (sw - ho);
                    int startY = i * (sh - vo);
                    if (startX + sw > imgW) startX = imgW - sw;
                    if (startY + sh > imgH) startY = imgH - sh;
                    if (startX < 0) startX = 0;
                    if (startY < 0) startY = 0;

                    int endX = startX + sw;
                    int endY = startY + sh;

                    var rect = new Rect(startX, startY, sw, sh);
                    var crop = new Mat(ori, rect).Clone();
                    var entry = new ImgDictEntry
                    {
                        origin_path = imgPath,
                        parent_key = null,
                        global_x = startX,
                        global_y = startY,
                        slice_positions = new[] { new Tuple<int, int>(i, j) },
                        width = sw,
                        height = sh,
                        img = crop,
                        infer_round = 1,
                        status = "active",
                        generated_by = (null, null)
                    };
                    var key = new Tuple<int, int>(0, counter);
                    imgDict[key] = entry;

                    // 初始结果
                    var bbox = new List<double> { 0, 0, sw, sh };
                    var globalBbox = new JArray { startX, startY, endX, endY };
                    var metadata = new JObject
                    {
                        ["combine_flag"] = false,
                        ["slice_index"] = new JArray(new JArray(i, j)),
                        ["global_x"] = startX,
                        ["global_y"] = startY,
                        ["global_bbox"] = globalBbox
                    };
                    var pred = new Prediction
                    {
                        category_id = counter,
                        category_name = $"slice_{counter}",
                        score = 1.0f,
                        bbox = bbox,
                        area = sw * sh,
                        with_mask = false,
                        mask = null,
                        with_angle = false,
                        angle = -100f,
                        metadata = metadata
                    };
                    var resultEntry = new ResultEntry
                    {
                        current_round = 0,
                        task_type = "image_generate",
                        predictions = new Dictionary<int, Prediction> { { 0, pred } }
                    };
                    resDict[new Tuple<int, int>(-1, counter)] = resultEntry;

                    counter++;
                }
            }

            inputs.current_img_keys = new List<Tuple<int, int>>(imgDict.Keys);
            return Task.FromResult(inputs);
        }
    }

    public class SingleImageModule : ImageGeneratorModule
    {
        public SingleImageModule(ModuleConfig cfg = null) : base(cfg)
        {
        }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            var ori = inputs.ori_img;
            string imgPath = "memory_image";
            int currentRound = 0;
            inputs.current_round = currentRound;

            if (ori.Channels() == 1)
            {
                var c3 = new Mat();
                Cv2.CvtColor(ori, c3, ColorConversionCodes.GRAY2BGR);
                inputs.ori_img = c3;
                ori = c3;
            }

            var entry = new ImgDictEntry
            {
                origin_path = imgPath,
                parent_key = null,
                global_x = 0,
                global_y = 0,
                slice_positions = new[] { new Tuple<int, int>(0, 0) },
                width = ori.Cols,
                height = ori.Rows,
                img = ori,
                infer_round = 1,
                status = "active",
                generated_by = (null, null)
            };
            var key = new Tuple<int, int>(currentRound, 0);
            inputs.img_dict[key] = entry;

            // 初始结果
            var bbox = new List<double> { 0, 0, ori.Cols, ori.Rows };
            var metadata = new JObject
            {
                ["combine_flag"] = false,
                ["slice_index"] = new JArray(new JArray(0, 0)),
                ["global_x"] = 0,
                ["global_y"] = 0,
                ["global_bbox"] = new JArray(0, 0, ori.Cols, ori.Rows)
            };

            var pred = new Prediction
            {
                category_id = 0,
                category_name = "whole_image",
                score = 1.0f,
                bbox = bbox,
                area = ori.Cols * ori.Rows,
                with_mask = false,
                mask = null,
                with_angle = false,
                angle = -100f,
                metadata = metadata
            };

            inputs.result_dict[key] = new ResultEntry
            {
                current_round = currentRound,
                task_type = "image_generate",
                predictions = new Dictionary<int, Prediction> { { 0, pred } }
            };

            inputs.current_img_keys = new List<Tuple<int, int>>(inputs.img_dict.Keys);
            return Task.FromResult(inputs);
        }
    }
}


