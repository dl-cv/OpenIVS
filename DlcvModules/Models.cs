using System;
using System.Collections.Generic;
using System.Drawing;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;

namespace DlcvModules
{
	/// <summary>
	/// 模型模块最小骨架：统一从输入 images 取 Bitmap/ModuleImage，转为 Mat 调用 DlcvCsharpApi.Model。
	/// 为兼容 .NET 4.7.2，仅用到必要 API。
	/// </summary>
	public abstract class BaseModelModule : BaseModule
	{
		protected string _modelPath;
		protected int _deviceId;
		protected Model _model;

		protected BaseModelModule(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context)
		{
			_modelPath = ReadString("model_path", null);
			_deviceId = ReadInt("device_id", 0);
			// 简化：初始化在首次推理时完成
		}

		protected void EnsureModel()
		{
			if (_model == null)
			{
				_model = new Model(_modelPath, _deviceId);
			}
		}

		protected string ReadString(string key, string dv)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				var s = v.ToString();
				return string.IsNullOrWhiteSpace(s) ? dv : s;
			}
			return dv;
		}

		protected int ReadInt(string key, int dv)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				try { return Convert.ToInt32(v); } catch { return dv; }
			}
			return dv;
		}

		protected static Tuple<ModuleImage, Bitmap> Unwrap(object obj)
		{
			if (obj is ModuleImage mi)
			{
				if (mi.ImageObject is Bitmap bmp1) return Tuple.Create(mi, bmp1);
				return Tuple.Create(mi, mi.ImageObject as Bitmap);
			}
			return Tuple.Create<ModuleImage, Bitmap>(null, obj as Bitmap);
		}

		protected static Mat ToMat(Bitmap bmp)
		{
			if (bmp == null) return null;
			// 使用 OpenCvSharp 将 Bitmap 转 Mat：先保存到内存流避免 GDI 共享句柄复杂性
			System.IO.MemoryStream ms = new System.IO.MemoryStream();
			bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
			var bytes = ms.ToArray();
			ms.Dispose();
			return Cv2.ImDecode(bytes, ImreadModes.Color);
		}
	}

	/// <summary>
	/// model/det：检测/旋转框检测/实例分割等均可通过参数配置；此处最小实现做直通调用。
	/// </summary>
	public class DetModel : BaseModelModule
	{
		static DetModel()
		{
			ModuleRegistry.Register("model/det", typeof(DetModel));
		}

		public DetModel(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context)
		{
		}

		public override Tuple<List<object>, List<Dictionary<string, object>>> Process(List<object> imageList = null, List<Dictionary<string, object>> resultList = null)
		{
			var images = imageList ?? new List<object>();
			var outImages = new List<object>();
			var outResults = new List<Dictionary<string, object>>();
			EnsureModel();

			int outIndex = 0;
			for (int i = 0; i < images.Count; i++)
			{
				var tup = Unwrap(images[i]);
				var wrap = tup.Item1; var bmp = tup.Item2;
				if (bmp == null) continue;
				var mat = ToMat(bmp);
				if (mat == null || mat.Empty()) continue;

				var res = _model.Infer(mat, null); // 结构化结果
				mat.Dispose();

				// 输出：沿用输入图像对象；结果转为统一 local entry
				outImages.Add(images[i]);
				var entry = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
				entry["type"] = "local";
				entry["index"] = outIndex;
				entry["origin_index"] = wrap != null ? wrap.OriginalIndex : i;
				entry["transform"] = wrap != null && wrap.TransformState != null ? (object)wrap.TransformState.ToDict() : null;
				entry["sample_results"] = ConvertToLocalSamples(res);
				outResults.Add(entry);
				outIndex += 1;
			}

			return Tuple.Create(outImages, outResults);
		}

		private static List<Dictionary<string, object>> ConvertToLocalSamples(Utils.CSharpResult res)
		{
			var list = new List<Dictionary<string, object>>();
			if (res.SampleResults == null || res.SampleResults.Count == 0) return list;
			var sr = res.SampleResults[0];
			if (sr.Results == null) return list;
			foreach (var obj in sr.Results)
			{
				var o = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
				o["category_id"] = obj.CategoryId;
				o["category_name"] = obj.CategoryName;
				o["score"] = obj.Score;
				o["area"] = obj.Area;
				o["bbox"] = obj.Bbox != null ? (object)new List<double>(obj.Bbox) : null;
				o["with_bbox"] = obj.WithBbox;
				o["with_mask"] = obj.WithMask;
				o["with_angle"] = obj.WithAngle;
				o["angle"] = obj.Angle;
				list.Add(o);
			}
			return list;
		}
	}

	/// <summary>
	/// 其他模型类型以 DetModel 的骨架实现复用，后续可细化参数处理。
	/// </summary>
	public class RotatedBBoxModel : DetModel
	{
		static RotatedBBoxModel()
		{
			ModuleRegistry.Register("model/rotated_bbox", typeof(RotatedBBoxModel));
		}
		public RotatedBBoxModel(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }
	}

	public class InstanceSegModel : DetModel
	{
		static InstanceSegModel()
		{
			ModuleRegistry.Register("model/instance_seg", typeof(InstanceSegModel));
		}
		public InstanceSegModel(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }
	}

	public class SemanticSegModel : DetModel
	{
		static SemanticSegModel()
		{
			ModuleRegistry.Register("model/semantic_seg", typeof(SemanticSegModel));
		}
		public SemanticSegModel(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }
	}

	public class ClsModel : DetModel
	{
		static ClsModel()
		{
			ModuleRegistry.Register("model/cls", typeof(ClsModel));
		}
		public ClsModel(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }
	}

	public class OCRModel : DetModel
	{
		static OCRModel()
		{
			ModuleRegistry.Register("model/ocr", typeof(OCRModel));
		}
		public OCRModel(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }
	}
}



