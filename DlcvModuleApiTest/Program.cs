using System;
using Newtonsoft.Json.Linq;
using DlcvModuleApi.Api;

namespace DlcvModuleApiTest
{
    class Program
    {
        static void Main(string[] args)
        {
            // 构造与 Python 兼容的 modules_list
            var modules_list = new JArray
            {
                // 可注释掉以测试 SingleImage 自动补齐
                new JObject
                {
                    ["module_type"] = "sliding_window",
                    ["task_type"] = "sliding_window",
                    ["small_img_width"] = 400,
                    ["small_img_height"] = 400,
                    ["horizontal_overlap"] = 64,
                    ["vertical_overlap"] = 64
                },
                new JObject
                {
                    ["module_type"] = "model_inference",
                    ["model_path"] = @"C:\Users\Administrator\Desktop\三代目测试目录\实例分割\实例分割.dvt",
                    ["task_type"] = "det",
                    ["threshold"] = 0.5,
                    ["iou_threshold"] = 0.2,
                    ["top_k"] = 1,
                    ["epsilon"] = 1.0,
                    ["category_filter_list"] = new JArray(),
                    ["combine_ios_threshold"] = 0.2
                },
                new JObject { ["module_type"] = "detection_result", ["task_type"] = "detection_result" },
                new JObject { ["module_type"] = "combine_results", ["combine_ios_threshold"] = 0.2 }
            };

            string testImg = @"C:\Users\Administrator\Desktop\三代目测试目录\实例分割\实例分割.png"; // 替换为你的测试图片
            try
            {
                var result = DlcvModulePipelineApi.PredictWithModulesList(testImg, modules_list, return_vis: false, module_index: null, data: null);
                Console.WriteLine(result.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("Done");
        }
    }
}


