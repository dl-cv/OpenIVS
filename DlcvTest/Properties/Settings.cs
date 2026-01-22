using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace DlcvTest.Properties
{
    public class Settings
    {
        private static Settings _default;
        private static readonly string ConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlcvTest",
            "user.config.json");
        private static readonly string LegacyConfigFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "user.config.json");

        public static Settings Default
        {
            get
            {
                if (_default == null) Load();
                return _default;
            }
        }

        public string LastModelPath { get; set; } = "";

        /// <summary>
        /// 最近使用的模型路径（MRU）。最新的在最前，最多保留 3 个。
        /// </summary>
        public List<string> RecentModelPaths { get; set; } = new List<string>();

        /// <summary>
        /// 记录最近使用的模型路径：去重（忽略大小写）、新路径放在最前、超出数量则移除最老的。
        /// </summary>
        public void RememberModelPath(string path, int maxCount = 3)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            string fullPath;
            try { fullPath = Path.GetFullPath(path); }
            catch { fullPath = path; }

            if (RecentModelPaths == null)
            {
                RecentModelPaths = new List<string>();
            }

            // 去重 + 清理空项
            for (int i = RecentModelPaths.Count - 1; i >= 0; i--)
            {
                var p = RecentModelPaths[i];
                if (string.IsNullOrWhiteSpace(p) ||
                    string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    RecentModelPaths.RemoveAt(i);
                }
            }

            // 最新放到最前
            RecentModelPaths.Insert(0, fullPath);

            // 超出最大数量则移除最老的
            int limit = Math.Max(0, maxCount);
            while (RecentModelPaths.Count > limit)
            {
                RecentModelPaths.RemoveAt(RecentModelPaths.Count - 1);
            }
        }
        
        public bool ShowOriginalPane { get; set; } = true;
        
        public bool ShowContours { get; set; } = true;

        public bool ShowMaskPane {get;set;} = true;

        public bool ShowBBoxPane { get; set; } = true;

        public bool ShowBlackBackgroundPane { get; set; } = false;

        public bool ShowTextPane { get; set; } = true;

        public bool ShowScorePane { get; set; } = true;

        public bool ShowTextOutOfBboxPane { get; set; } = true;

        public bool ShowTextShadowPane { get; set; } = false;

        public string SavedDataPath { get; set; } = "";

        public string SavedModelPath { get; set; } = "";

        /// <summary>
        /// 输出目录（批量预测导出 PNG / 结果文件使用）。为空表示使用默认输出目录（输入文件夹下的"导出"）。
        /// </summary>
        public string OutputDirectory { get; set; } = "";

        /// <summary>
        /// 批量推测：是否保存原图（无任何绘制）。
        /// </summary>
        public bool SaveOriginal { get; set; } = false;

        /// <summary>
        /// 批量推测：是否保存可视化图片（标注图 + 推理图拼接）。
        /// </summary>
        public bool SaveVisualization { get; set; } = false;

        /// <summary>
        /// 批量推测：预测完成后打开输出文件夹
        /// </summary>
        public bool OpenOutputFolderAfterBatch { get; set; } = true;

        public bool AutoLoadDataPath { get; set; } = true;

        public bool AutoLoadModel { get; set; } = true;

        // BBox 边框样式配置
        public string BBoxBorderColor { get; set; } = "#FF0000"; // 红色边框

        public double BBoxBorderThickness { get; set; } = 2.0; // 边框粗细

        public string BBoxFillColor { get; set; } = "#20FF0000"; // 半透明红色填充

        public bool BBoxFillEnabled { get; set; } = false; // 是否启用填充

        public double BBoxFillOpacity { get; set; } = 50.0; // 填充透明度（0-100，默认 50%）
        public bool ShowCenterPoint { get; set; } = true; // 是否显示中心点十字
        public double FontSize { get; set; } = 12.0; // 字体大小

        public string FontColor { get; set; } = "#FF00FF00"; // 字体颜色（绿色）

        public void Save()
        {
            try
            {
                // 确保目录存在
                string dir = Path.GetDirectoryName(ConfigFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] 保存设置失败: {ex.Message}");
            }
        }

        private static void Load()
        {
            if (TryLoadFromFile(ConfigFile, out var current))
            {
                _default = current;
                return;
            }

            if (!string.Equals(ConfigFile, LegacyConfigFile, StringComparison.OrdinalIgnoreCase) &&
                TryLoadFromFile(LegacyConfigFile, out var legacy))
            {
                _default = legacy;
                try { _default.Save(); } catch { }
                return;
            }

            _default = new Settings();
        }

        private static bool TryLoadFromFile(string path, out Settings settings)
        {
            settings = null;
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!File.Exists(path)) return false;

            try
            {
                string json = File.ReadAllText(path);
                settings = JsonConvert.DeserializeObject<Settings>(json);
                return settings != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
