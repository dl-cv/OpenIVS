using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

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

		public override Tuple<List<object>, List<Dictionary<string, object>>> Process(List<object> imageList = null, List<Dictionary<string, object>> resultList = null)
		{
			var images = imageList ?? new List<object>();
			var results = resultList ?? new List<Dictionary<string, object>>();

			string saveDir = ReadString("save_path", null);
			string suffix = ReadString("suffix", "_out");
			string fmt = ReadString("format", "png");
			if (!string.IsNullOrWhiteSpace(saveDir))
			{
				try { Directory.CreateDirectory(saveDir); } catch { }
			}

			for (int i = 0; i < images.Count; i++)
			{
				var (wrap, bmp) = Unwrap(images[i]);
				if (bmp == null) continue;
				string baseName = null;
				if (i < results.Count && results[i] != null && results[i].TryGetValue("filename", out object fn) && fn is string s && !string.IsNullOrWhiteSpace(s))
				{
					baseName = Path.GetFileNameWithoutExtension(s);
				}
				if (string.IsNullOrWhiteSpace(baseName)) baseName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
				string fileName = baseName + suffix + "." + (string.IsNullOrWhiteSpace(fmt) ? "png" : fmt);
				if (!string.IsNullOrWhiteSpace(saveDir))
				{
					string full = Path.Combine(saveDir, fileName);
					try { bmp.Save(full); } catch { }
				}
			}

			// 透传
			return Tuple.Create(images, results);
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

		private static Tuple<ModuleImage, Bitmap> Unwrap(object obj)
		{
			if (obj is ModuleImage mi)
			{
				if (mi.ImageObject is Bitmap bmp1) return Tuple.Create(mi, bmp1);
				return Tuple.Create(mi, mi.ImageObject as Bitmap);
			}
			return Tuple.Create<ModuleImage, Bitmap>(null, obj as Bitmap);
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



