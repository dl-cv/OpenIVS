using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
	/// <summary>
	/// output/save_image：将输入图像按同序 result_list 的 filename 或时间戳保存到磁盘。
	/// properties:
	/// - save_path(string)
	/// - suffix(string, default "_out")
	/// - format(string, default "png")
	/// </summary>
	public class SaveImage : BaseModule
	{
		static SaveImage()
		{
			ModuleRegistry.Register("output/save_image", typeof(SaveImage));
		}

		public SaveImage(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
			var results = resultList ?? new JArray();

			string saveDir = ReadString("save_path", null);
			string suffix = ReadString("suffix", "_out");
			string fmt = ReadString("format", "png");
			if (!string.IsNullOrWhiteSpace(saveDir))
			{
				try { Directory.CreateDirectory(saveDir); } catch { }
			}

			for (int i = 0; i < images.Count; i++)
			{
				var (wrap, matRgb) = Unwrap(images[i]);
                if (matRgb == null || matRgb.Empty()) continue;
				string baseName = null;
				if (i < results.Count && results[i] != null && ((JObject)results[i])["filename"] != null && !string.IsNullOrWhiteSpace(((JObject)results[i])["filename"].ToString()))
				{
					baseName = Path.GetFileNameWithoutExtension(((JObject)results[i])["filename"].ToString());
				}
				if (string.IsNullOrWhiteSpace(baseName)) baseName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
				string fileName = baseName + suffix + "." + (string.IsNullOrWhiteSpace(fmt) ? "png" : fmt);
				if (!string.IsNullOrWhiteSpace(saveDir))
				{
					string full = Path.Combine(saveDir, fileName);
                    try
                    {
                        // 输出前从RGB转BGR
                        using (var matBgr = new Mat())
                        {
                            Cv2.CvtColor(matRgb, matBgr, ColorConversionCodes.RGB2BGR);
                            Cv2.ImWrite(full, matBgr);
                        }
                    } catch { }
				}
			}

			// 透传
			return new ModuleIO(images, results);
		}

		private string ReadString(string key, string dv)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				var s = v.ToString();
				return string.IsNullOrWhiteSpace(s) ? dv : s;
			}
			return dv;
		}

        private static Tuple<ModuleImage, Mat> Unwrap(ModuleImage obj)
		{
			if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
			return Tuple.Create(obj, obj.ImageObject);
		}
	}

	/// <summary>
	/// output/preview：透传。
	/// </summary>
	public class Preview : BaseModule
	{
		static Preview()
		{
			ModuleRegistry.Register("output/preview", typeof(Preview));
		}
		public Preview(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }
	}
}



