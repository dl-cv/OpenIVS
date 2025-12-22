using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    /// <summary>
    /// input/image：从磁盘读取图片，输出 (image_list, result_list)
    /// properties:
    /// - path: string 单文件路径
    /// - paths: List<string> 多文件路径
    /// </summary>
    public class InputImage : BaseInputModule
    {
        static InputImage()
        {
            ModuleRegistry.Register("input/image", typeof(InputImage));
        }

        public InputImage(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Generate()
        {
            var images = new List<ModuleImage>();
            var results = new JArray();

            // 优先从 ExecutionContext 注入的前端 BGR Mat 读取
            try
            {
                Mat matFromContext = null;
                try { matFromContext = Context != null ? Context.Get<Mat>("frontend_image_mat", null) : null; } catch { matFromContext = null; }
                if (matFromContext != null && !matFromContext.Empty())
                {
                    var bgr = matFromContext;
                    var state = new TransformationState(bgr.Width, bgr.Height);
                    var wrap = new ModuleImage(bgr, bgr, state, 0);
                    images.Add(wrap);

                    var entryCtx = new JObject
                    {
                        ["type"] = "local",
                        ["index"] = 0,
                        ["origin_index"] = 0,
                        ["transform"] = JObject.FromObject(state.ToDict()),
                        ["sample_results"] = new JArray(),
                        ["filename"] = "frontend_mat",
                        ["filepath"] = ""
                    };
                    results.Add(entryCtx);
                    try { ScalarOutputsByName["filename"] = "frontend_mat"; } catch { }
                    return new ModuleIO(images, results);
                }
            }
            catch { }

            var files = ResolveFileList();
            int index = 0;
            foreach (var file in files)
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    var bgr = Cv2.ImRead(file, ImreadModes.Color);
                    if (bgr.Empty()) { bgr.Dispose(); continue; }

                    var state = new TransformationState(bgr.Width, bgr.Height);
                    var wrap = new ModuleImage(bgr, bgr, state, index);
                    images.Add(wrap);

                    var entry = new JObject
                    {
                        ["type"] = "local",
                        ["index"] = index,
                        ["origin_index"] = index,
                        ["transform"] = JObject.FromObject(state.ToDict()),
                        ["sample_results"] = new JArray(),
                        ["filename"] = Path.GetFileNameWithoutExtension(file),
                        ["filepath"] = file
                    };
                    results.Add(entry);
                    index += 1;
                }
                catch
                {
                    // 跳过坏图
                }
            }

            try
            {
                if (images.Count == 1 && results.Count == 1)
                {
                    var fn = (results[0] as JObject)["filename"] != null ? (results[0] as JObject)["filename"].ToString() : null;
                    if (!string.IsNullOrEmpty(fn))
                    {
                        ScalarOutputsByName["filename"] = fn;
                    }
                }
            }
            catch { }

            return new ModuleIO(images, results);
        }

        private List<string> ResolveFileList()
        {
            var files = new List<string>();
            // 1) 运行时上下文优先
            try
            {
                var ctxKeys = new[] { "frontend_selected_image_path", "selected_image_path", "img_path", "frontend_image_path" };
                foreach (var k in ctxKeys)
                {
                    string v = Context != null ? Context.Get<string>(k, null) : null;
                    if (!string.IsNullOrWhiteSpace(v)) { files.Add(v); break; }
                }
            }
            catch { }

            // 2) 节点属性
            if (Properties != null)
            {
                if (Properties.TryGetValue("path", out object p) && p is string s && !string.IsNullOrWhiteSpace(s))
                {
                    files.Add(s);
                }
                if (Properties.TryGetValue("paths", out object ps))
                {
                    var en = ps as System.Collections.IEnumerable;
                    if (en != null)
                    {
                        foreach (var one in en)
                        {
                            var str = one != null ? one.ToString() : null;
                            if (!string.IsNullOrWhiteSpace(str)) files.Add(str);
                        }
                    }
                }
            }
            return files;
        }
    }

    /// <summary>
    /// input/frontend_image：从 ExecutionContext 读取前端提供的图片路径
    /// context keys:
    /// - frontend_image_path: string
    /// </summary>
    public class InputFrontendImage : BaseInputModule
    {
        static InputFrontendImage()
        {
            ModuleRegistry.Register("input/frontend_image", typeof(InputFrontendImage));
        }

        public InputFrontendImage(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Generate()
        {
            var images = new List<ModuleImage>();
            var results = new JArray();

            // 优先从 ExecutionContext 注入前端图像 Mat（BGR）
            try
            {
                Mat matFromContext = null;
                try { matFromContext = Context != null ? Context.Get<Mat>("frontend_image_mat", null) : null; } catch { matFromContext = null; }
                if (matFromContext != null && !matFromContext.Empty())
                {
                    var bgr = matFromContext;
                    var state = new TransformationState(bgr.Width, bgr.Height);
                    var wrap = new ModuleImage(bgr, bgr, state, 0);
                    images.Add(wrap);

                    var entry = new JObject
                    {
                        ["type"] = "local",
                        ["index"] = 0,
                        ["origin_index"] = 0,
                        ["transform"] = JObject.FromObject(state.ToDict()),
                        ["sample_results"] = new JArray(),
                        ["filename"] = "frontend_mat"
                    };
                    results.Add(entry);
                    return new ModuleIO(images, results);
                }
            }
            catch { }

            // 回退到从路径读取（BGR）
            string path = ResolvePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ModuleIO(images, results);
            }

            try
            {
                if (File.Exists(path))
                {
                    var bgr = Cv2.ImRead(path, ImreadModes.Color);
                    if (!bgr.Empty())
                    {
                        var state = new TransformationState(bgr.Width, bgr.Height);
                        var wrap = new ModuleImage(bgr, bgr, state, 0);
                        images.Add(wrap);

                        var entry = new JObject
                        {
                            ["type"] = "local",
                            ["index"] = 0,
                            ["origin_index"] = 0,
                            ["transform"] = JObject.FromObject(state.ToDict()),
                            ["sample_results"] = new JArray(),
                            ["filename"] = path
                        };
                        results.Add(entry);
                    }
                }
            }
            catch
            {
            }

            return new ModuleIO(images, results);
        }

        private string ResolvePath()
        {
            if (Properties != null && Properties.TryGetValue("path", out object p) && p is string s && !string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
            return Context != null ? Context.Get<string>("frontend_image_path", null) : null;
        }
    }

    /// <summary>
    /// input/build_results：构建结果输入节点。可选输入图像；若无则从属性/上下文读取或生成纯色图。
    /// properties:
    /// - image_path: string 可选图像路径
    /// - default_width/default_height: int 生成图像尺寸，默认640
    /// - default_color: string "R,G,B"，默认 "0,255,0"
    /// - category_id/category_name/score: 合成结果元信息
    /// - bbox_x1/bbox_y1/bbox_x2/bbox_y2/bbox_angle: 边界框参数（右下角坐标用于推导宽高）
    /// </summary>
    public class InputBuildResults : BaseModule
    {
        static InputBuildResults()
        {
            ModuleRegistry.Register("input/build_results", typeof(InputBuildResults));
        }

        public InputBuildResults(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var outImages = new List<ModuleImage>();
            var outResults = new JArray();

            // 选择图像：输入图像 > 属性/上下文路径 > 纯色图
            ModuleImage usedWrap = null;
            Mat img = null;
            int w = 0, h = 0;

            if (imageList != null && imageList.Count > 0 && imageList[0] != null && imageList[0].GetImage() != null && !imageList[0].GetImage().Empty())
            {
                usedWrap = imageList[0];
                img = usedWrap.GetImage();
                w = img.Width; h = img.Height;
            }
            else
            {
                string imagePath = null;
                try { if (Properties != null && Properties.TryGetValue("image_path", out object pv) && pv != null) imagePath = pv.ToString(); } catch { }
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    try
                    {
                        var keys = new[] { "frontend_selected_image_path", "selected_image_path", "img_path", "frontend_image_path" };
                        foreach (var k in keys)
                        {
                            var v = Context != null ? Context.Get<string>(k, null) : null;
                            if (!string.IsNullOrWhiteSpace(v)) { imagePath = v; break; }
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
                {
                    try
                    {
                        img = Cv2.ImRead(imagePath, ImreadModes.Color);
                        if (img != null && !img.Empty())
                        {
                            w = img.Width; h = img.Height;
                            var state = new TransformationState(w, h);
                            usedWrap = new ModuleImage(img, img, state, 0);
                        }
                    }
                    catch { img = null; usedWrap = null; }
                }
            }

            if (img == null || img.Empty())
            {
                int dw = 640, dh = 640; string colorStr = "0,255,0";
                try { if (Properties != null && Properties.TryGetValue("default_width", out object v) && v != null) dw = Convert.ToInt32(v); } catch { }
                try { if (Properties != null && Properties.TryGetValue("default_height", out object v) && v != null) dh = Convert.ToInt32(v); } catch { }
                try { if (Properties != null && Properties.TryGetValue("default_color", out object v) && v != null) colorStr = v.ToString(); } catch { }
                int r = 0, g = 255, b = 0;
                try
                {
                    var parts = colorStr.Split(',');
                    if (parts.Length >= 3)
                    {
                        r = Convert.ToInt32(parts[0]);
                        g = Convert.ToInt32(parts[1]);
                        b = Convert.ToInt32(parts[2]);
                    }
                }
                catch { r = 0; g = 255; b = 0; }
                img = new Mat(dh, dw, MatType.CV_8UC3, new Scalar(b, g, r)); // BGR
                w = dw; h = dh;
                var stateDef = new TransformationState(w, h);
                usedWrap = new ModuleImage(img, img, stateDef, 0);
            }

            outImages.Add(usedWrap);

            int categoryId = 0; string categoryName = "测试对象"; double score = 0.95;
            // 兼容属性输入为 XYXY（bbox_x1~bbox_y2）或 XYWH（bbox_x/bbox_y/bbox_w/bbox_h）
            double x1 = 100.0, y1 = 100.0, x2 = 300.0, y2 = 300.0;
            double bx = double.NaN, by = double.NaN, bwProp = double.NaN, bhProp = double.NaN;
            double angle = 0.0;
            try { if (Properties != null && Properties.TryGetValue("category_id", out object v) && v != null) categoryId = Convert.ToInt32(v); } catch { }
            try { if (Properties != null && Properties.TryGetValue("category_name", out object v) && v != null) categoryName = v.ToString(); } catch { }
            try { if (Properties != null && Properties.TryGetValue("score", out object v) && v != null) score = Convert.ToDouble(v); } catch { }
            try { if (Properties != null && Properties.TryGetValue("bbox_x1", out object v) && v != null) x1 = Convert.ToDouble(v); } catch { }
            try { if (Properties != null && Properties.TryGetValue("bbox_y1", out object v) && v != null) y1 = Convert.ToDouble(v); } catch { }
            try { if (Properties != null && Properties.TryGetValue("bbox_x2", out object v) && v != null) x2 = Convert.ToDouble(v); } catch { }
            try { if (Properties != null && Properties.TryGetValue("bbox_y2", out object v) && v != null) y2 = Convert.ToDouble(v); } catch { }
            try { if (Properties != null && Properties.TryGetValue("bbox_x", out object v) && v != null) bx = Convert.ToDouble(v); } catch { }
            try { if (Properties != null && Properties.TryGetValue("bbox_y", out object v) && v != null) by = Convert.ToDouble(v); } catch { }
            try { if (Properties != null && Properties.TryGetValue("bbox_w", out object v) && v != null) bwProp = Convert.ToDouble(v); } catch { }
            try { if (Properties != null && Properties.TryGetValue("bbox_h", out object v) && v != null) bhProp = Convert.ToDouble(v); } catch { }
            try { if (Properties != null && Properties.TryGetValue("bbox_angle", out object v) && v != null) angle = Convert.ToDouble(v); } catch { }

            // 与 Python 版本对齐：不做“按 640x640 缩放”的假设。
            // 若用户提供了 bbox_x/bbox_y/bbox_w/bbox_h，则优先按 XYWH 解释；否则按 XYXY 解释。
            if (!double.IsNaN(bx) && !double.IsNaN(by) && !double.IsNaN(bwProp) && !double.IsNaN(bhProp))
            {
                x1 = bx;
                y1 = by;
                x2 = bx + Math.Abs(bwProp);
                y2 = by + Math.Abs(bhProp);
            }
            else
            {
                // 顺序修正（XYXY）
                if (x2 < x1) { var t = x1; x1 = x2; x2 = t; }
                if (y2 < y1) { var t = y1; y1 = y2; y2 = t; }
            }

            // 约束至图像范围并转为 (x, y, w, h)
            x1 = Math.Max(0.0, Math.Min(x1, w));
            y1 = Math.Max(0.0, Math.Min(y1, h));
            x2 = Math.Max(0.0, Math.Min(x2, w));
            y2 = Math.Max(0.0, Math.Min(y2, h));
            // 保证至少 1 像素（下游通常要求 w>0,h>0）
            double bw = Math.Max(1.0, x2 - x1);
            double bh = Math.Max(1.0, y2 - y1);

            var det = new JObject
            {
                ["category_id"] = categoryId,
                ["category_name"] = categoryName,
                ["score"] = score,
                ["bbox"] = new JArray(x1, y1, bw, bh),
                ["with_bbox"] = true,
                ["with_angle"] = false,
                ["angle"] = -100.0
            };

            var entry = new JObject
            {
                ["type"] = "local",
                ["originating_module"] = "input/build_results",
                ["sample_results"] = new JArray(det),
                ["index"] = 0,
                ["origin_index"] = usedWrap != null ? usedWrap.OriginalIndex : 0,
                ["transform"] = usedWrap != null && usedWrap.TransformState != null ? JObject.FromObject(usedWrap.TransformState.ToDict()) : null
            };

            outResults.Add(entry);
            return new ModuleIO(outImages, outResults);
        }
    }
}


