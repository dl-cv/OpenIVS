using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DlcvModuleApi.Utils
{
    public class ModuleConfig
    {
        public string module_type { get; set; }
        public string task_type { get; set; }
        public string model_path { get; set; }
        public float threshold { get; set; } = 0.5f;
        public float iou_threshold { get; set; } = 0.2f;
        public bool return_polygon { get; set; } = false;
        public int top_k { get; set; } = 1;
        public float epsilon { get; set; } = 1.0f;
        public float combine_ios_threshold { get; set; } = 0.2f;
        public List<string> category_filter_list { get; set; } = new List<string>();
        public int bbox_expand_pixels { get; set; } = 0;
        public int small_img_width { get; set; } = 640;
        public int small_img_height { get; set; } = 640;
        public int horizontal_overlap { get; set; } = 64;
        public int vertical_overlap { get; set; } = 64;
        public float horizontal_ratio { get; set; } = 1.0f;
        public float vertical_ratio { get; set; } = 1.0f;
        public int area_threshold { get; set; } = 1000;
        public string filter_mode { get; set; } = "greater";
    }

    public class ImgDictEntry
    {
        public string origin_path;
        public Tuple<int, int>? parent_key;
        public int global_x;
        public int global_y;
        public Tuple<int, int>[] slice_positions;
        public int width;
        public int height;
        public OpenCvSharp.Mat img;
        public int infer_round;
        public string status;
        public (int? model_index, int? result_index) generated_by;
        public bool is_rotated;
        public List<double> rotated_rect;
    }

    public class Prediction
    {
        public int category_id;
        public string category_name;
        public float score;
        public List<double> bbox;
        public int? area;
        public bool with_mask;
        public OpenCvSharp.Mat mask;
        public bool with_angle;
        public float angle;
        public JObject metadata; // contains combine_flag, slice_index, global_x, global_y, global_bbox, is_rotated
    }

    public class ResultEntry
    {
        public int current_round;
        public string task_type;
        public Dictionary<int, Prediction> predictions = new Dictionary<int, Prediction>();
    }

    public class PipelineIO
    {
        public Dictionary<Tuple<int, int>, ImgDictEntry> img_dict = new Dictionary<Tuple<int, int>, ImgDictEntry>();
        public Dictionary<Tuple<int, int>, ResultEntry> result_dict = new Dictionary<Tuple<int, int>, ResultEntry>();
        public int current_round;
        public int total_round;
        public OpenCvSharp.Mat ori_img;
        public ModuleConfig model_config = new ModuleConfig();
        public string originating_module = string.Empty;
        public bool has_image_ratio_adjust = false;
        public List<Tuple<Tuple<int, int>, OpenCvSharp.Mat, Tuple<int, int>>> resized_images = new List<Tuple<Tuple<int, int>, OpenCvSharp.Mat, Tuple<int, int>>>();
        public List<Tuple<int, int>> current_img_keys = new List<Tuple<int, int>>();
    }
}


