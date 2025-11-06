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
                        ["filename"] = file
                    };
                    results.Add(entry);
                    index += 1;
                }
                catch
                {
                    // 跳过坏图
                }
            }

            return new ModuleIO(images, results);
        }

        private List<string> ResolveFileList()
        {
            var files = new List<string>();
            if (Properties != null)
            {
                if (Properties.TryGetValue("path", out object p) && p is string s && !string.IsNullOrWhiteSpace(s))
                {
                    files.Add(s);
                }
                if (Properties.TryGetValue("paths", out object ps))
                {
                    if (ps is IEnumerable<string> strEnum)
                    {
                        foreach (var one in strEnum)
                        {
                            if (!string.IsNullOrWhiteSpace(one)) files.Add(one);
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
}


