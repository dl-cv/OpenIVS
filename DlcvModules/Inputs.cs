using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

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

        public override Tuple<List<object>, List<Dictionary<string, object>>> Generate()
        {
            var images = new List<object>();
            var results = new List<Dictionary<string, object>>();

            var files = ResolveFileList();
            int index = 0;
            foreach (var file in files)
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    using (var img = Image.FromFile(file))
                    {
                        // 克隆到内存，避免 using 释放后引用失效
                        var bitmap = new Bitmap(img);

                        var state = new TransformationState(bitmap.Width, bitmap.Height);
                        var wrap = new ModuleImage(bitmap, bitmap, state, index);
                        images.Add(wrap);

                        var entry = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        entry["type"] = "local";
                        entry["index"] = index;
                        entry["origin_index"] = index;
                        entry["transform"] = state.ToDict();
                        entry["sample_results"] = new List<Dictionary<string, object>>();
                        entry["filename"] = file;
                        results.Add(entry);
                    }
                    index += 1;
                }
                catch
                {
                    // 跳过坏图
                }
            }

            return Tuple.Create(images, results);
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

        public override Tuple<List<object>, List<Dictionary<string, object>>> Generate()
        {
            var images = new List<object>();
            var results = new List<Dictionary<string, object>>();

            string path = ResolvePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return Tuple.Create(images, results);
            }

            try
            {
                if (File.Exists(path))
                {
                    using (var img = Image.FromFile(path))
                    {
                        var bitmap = new Bitmap(img);
                        var state = new TransformationState(bitmap.Width, bitmap.Height);
                        var wrap = new ModuleImage(bitmap, bitmap, state, 0);
                        images.Add(wrap);

                        var entry = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        entry["type"] = "local";
                        entry["index"] = 0;
                        entry["origin_index"] = 0;
                        entry["transform"] = state.ToDict();
                        entry["sample_results"] = new List<Dictionary<string, object>>();
                        entry["filename"] = path;
                        results.Add(entry);
                    }
                }
            }
            catch
            {
            }

            return Tuple.Create(images, results);
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


