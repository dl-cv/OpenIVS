using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;

namespace DlcvModules
{
	/// <summary>
	/// 模型模块最小骨架：统一从输入 images 取 ModuleImage(Mat) 调用 DlcvCsharpApi.Model。
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
				// 从上下文/属性读取设备与模式
				int deviceId = _deviceId;
				try
				{
					if (Context != null)
					{
						deviceId = Context.Get<int>("device_id", deviceId);
					}
				}
				catch { }

				bool rpcMode = false;
				try
				{
					if (Properties != null && Properties.TryGetValue("rpc_mode", out object rv) && rv != null)
					{
						bool.TryParse(rv.ToString(), out rpcMode);
					}
					if (!rpcMode && Context != null)
					{
						rpcMode = Context.Get<bool>("rpc_mode", false);
					}
				}
				catch { }

				// 允许从上下文兜底获取模型路径
				if (string.IsNullOrWhiteSpace(_modelPath) && Context != null)
				{
					try { _modelPath = Context.Get<string>("model_path", null); } catch { }
				}

				_model = new Model(_modelPath, deviceId, rpcMode);
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

		protected static Tuple<ModuleImage, Mat> Unwrap(object obj)
		{
			if (obj is ModuleImage mi)
			{
				return Tuple.Create(mi, mi.ImageObject);
			}
			return Tuple.Create<ModuleImage, Mat>(null, obj as Mat);
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

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
			var outImages = new List<ModuleImage>();
			var outResults = new JArray();
			EnsureModel();

			// 透传推理参数
			var p = new JObject();
			TryAddParam(p, "threshold");
			TryAddParam(p, "iou_threshold");
			TryAddParam(p, "top_k");
			TryAddParam(p, "return_polygon");
			TryAddParam(p, "epsilon");
			TryAddParam(p, "batch_size");

			int outIndex = 0;
			for (int i = 0; i < images.Count; i++)
			{
				var tup = Unwrap(images[i]);
				var wrap = tup.Item1; var mat = tup.Item2;
				if (mat == null || mat.Empty()) continue;

				Utils.CSharpResult res;
				if (p.Count > 0)
				{
					res = _model.Infer(mat, p);
				}
				else
				{
					res = _model.Infer(mat, null);
				}

				// 输出：沿用输入图像对象；结果转为统一 local entry
				outImages.Add(images[i]);
				var entry = new JObject
				{
					["type"] = "local",
					["index"] = outIndex,
					["origin_index"] = wrap != null ? wrap.OriginalIndex : i,
					["transform"] = wrap != null && wrap.TransformState != null ? JObject.FromObject(wrap.TransformState.ToDict()) : null,
					["sample_results"] = ConvertToLocalSamples(res)
				};
				outResults.Add(entry);
				outIndex += 1;
			}

			return new ModuleIO(outImages, outResults);
		}

		private static JArray ConvertToLocalSamples(Utils.CSharpResult res)
		{
			var list = new JArray();
			if (res.SampleResults == null || res.SampleResults.Count == 0) return list;
			var sr = res.SampleResults[0];
			if (sr.Results == null) return list;
			foreach (var obj in sr.Results)
			{
				var o = new JObject
				{
					["category_id"] = obj.CategoryId,
					["category_name"] = obj.CategoryName,
					["score"] = obj.Score,
					["area"] = obj.Area,
					["bbox"] = obj.Bbox != null ? JArray.FromObject(obj.Bbox) : null,
					["with_bbox"] = obj.WithBbox,
					["with_mask"] = obj.WithMask,
					["with_angle"] = obj.WithAngle,
					["angle"] = obj.Angle
				};
				// 将mask以多边形点集的形式保留（绝对坐标），以便后续还原真实mask
				if (obj.WithMask && obj.Mask != null && !obj.Mask.Empty())
				{
					try
					{
						var mask = obj.Mask;
						var bbox = obj.Bbox;
						double x0 = bbox != null && bbox.Count > 0 ? bbox[0] : 0.0;
						double y0 = bbox != null && bbox.Count > 1 ? bbox[1] : 0.0;
						Point[][] contours = mask.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);
						if (contours.Length > 0 && contours[0].Length > 0)
						{
							var pointsJson = new JArray();
							foreach (var p in contours[0])
							{
								var pObj = new JObject
								{
									["x"] = (int)(p.X + x0),
									["y"] = (int)(p.Y + y0)
								};
								pointsJson.Add(pObj);
							}
							o["mask"] = pointsJson;
						}
					}
					catch { }
				}
				list.Add(o);
			}
			return list;
		}

		private void TryAddParam(JObject p, string key)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				try
				{
					// 按照原样塞入，保持与后端期望的类型兼容
					if (v is bool)
					{
						p[key] = (bool)v;
					}
					else if (v is int)
					{
						p[key] = (int)v;
					}
					else if (v is float)
					{
						p[key] = (float)v;
					}
					else if (v is double)
					{
						p[key] = (double)v;
					}
					else if (v is string)
					{
						// 尝试数字/布尔解析，失败则作为字符串
						if (double.TryParse((string)v, out double dv)) p[key] = dv;
						else if (bool.TryParse((string)v, out bool bv)) p[key] = bv;
						else p[key] = (string)v;
					}
					else
					{
						p[key] = v.ToString();
					}
				}
				catch { }
			}
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

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            var baseIo = base.Process(imageList, resultList);
            var imagesOut = baseIo != null ? (baseIo.ImageList ?? new List<ModuleImage>()) : new List<ModuleImage>();
            var resultsOut = baseIo != null ? (baseIo.ResultList ?? new JArray()) : new JArray();

            int n = Math.Min(resultsOut.Count, imagesOut.Count);
            for (int i = 0; i < n; i++)
            {
                var entry = resultsOut[i] as JObject;
                if (entry == null) continue;
                var samples = entry["sample_results"] as JArray;
                if (samples == null) continue;

                var imgMat = imagesOut[i] != null ? imagesOut[i].ImageObject : null;
                int iw = imgMat != null ? Math.Max(1, imgMat.Width) : 1;
                int ih = imgMat != null ? Math.Max(1, imgMat.Height) : 1;

                foreach (var s in samples)
                {
                    var so = s as JObject;
                    if (so == null) continue;
                    var bboxArr = so["bbox"] as JArray;
                    bool withBbox = so.Value<bool?>("with_bbox") ?? false;
                    bool validDims = false;
                    if (bboxArr != null && bboxArr.Count >= 4)
                    {
                        try
                        {
                            double bw = bboxArr[2].Value<double>();
                            double bh = bboxArr[3].Value<double>();
                            validDims = (bw > 0.0 && bh > 0.0);
                        }
                        catch { validDims = false; }
                    }
                    if (!withBbox || !validDims)
                    {
                        so["bbox"] = new JArray(0, 0, iw, ih);
                        so["with_bbox"] = true;
                        so["with_angle"] = false;
                        so["angle"] = -100.0;
                    }
                }
            }

            return new ModuleIO(imagesOut, resultsOut);
        }
	}
}




