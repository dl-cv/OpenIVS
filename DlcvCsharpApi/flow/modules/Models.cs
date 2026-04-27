using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
		protected JObject _modelInfo;
		protected JArray _maxShape;
		protected int _maxBatchSize = 1;

		protected BaseModelModule(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context)
		{
			_modelPath = ReadStringOrDefault("model_path", null);
			_deviceId = ReadInt("device_id", 0);
			// 简化：初始化在首次推理时完成
		}

		public void LoadModel()
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

				if (string.IsNullOrWhiteSpace(_modelPath) && Context != null)
				{
					try { _modelPath = Context.Get<string>("model_path", null); } catch { }
				}

				_model = new Model(_modelPath, deviceId, rpcMode, true);
				SyncModelMeta();
			}
			else
			{
				SyncModelMeta();
			}
		}

		protected void SyncModelMeta()
		{
			if (_model == null) return;
			try
			{
				_modelInfo = _model.GetCachedModelInfo();
				if (_modelInfo == null)
				{
					_modelInfo = _model.GetModelInfo();
				}
			}
			catch { }

			try { _maxShape = _model.GetCachedMaxShape(); } catch { _maxShape = null; }
			try { _maxBatchSize = _model.GetMaxBatchSize(); } catch { _maxBatchSize = 1; }

			// 将每个子模型加载时读取到的 batch 元信息写入流程上下文，供外层 DVS 包装模型汇总。
			try
			{
				if (Context != null)
				{
					var list = Context.Get<List<Dictionary<string, object>>>("loaded_model_meta", null);
					if (list == null)
					{
						list = new List<Dictionary<string, object>>();
						Context.Set("loaded_model_meta", list);
					}

					var entry = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
					{
						["node_id"] = NodeId,
						["title"] = Title ?? string.Empty,
						["model_path"] = _modelPath ?? string.Empty,
						["max_batch_size"] = _maxBatchSize,
						["max_shape"] = _maxShape != null ? (JArray)_maxShape.DeepClone() : null
					};

					string originalPath = null;
					string modelName = null;
					try
					{
						if (Properties != null)
						{
							if (Properties.TryGetValue("model_path_original", out object op) && op != null)
							{
								originalPath = op.ToString();
							}
							if (Properties.TryGetValue("model_name", out object mn) && mn != null)
							{
								modelName = mn.ToString();
							}
						}
					}
					catch
					{
					}

					if (string.IsNullOrWhiteSpace(originalPath))
					{
						originalPath = _modelPath ?? string.Empty;
					}
					if (string.IsNullOrWhiteSpace(modelName))
					{
						try
						{
							modelName = Path.GetFileName(originalPath);
						}
						catch
						{
							modelName = originalPath;
						}
					}

					entry["model_path_original"] = originalPath ?? string.Empty;
					entry["model_name"] = modelName ?? string.Empty;
					if (_modelInfo != null)
					{
						entry["model_info"] = (JObject)_modelInfo.DeepClone();
					}
					list.Add(entry);
				}
			}
			catch
			{
			}
		}

		protected int ResolveEffectiveBatchLimit()
		{
			int modelLimit = Math.Max(1, _maxBatchSize);
			int cfg = 0;
			try
			{
				if (Properties != null && Properties.TryGetValue("batch_size", out object bv) && bv != null)
				{
					cfg = Convert.ToInt32(bv);
				}
			}
			catch { cfg = 0; }
			if (cfg <= 0) return modelLimit;
			return Math.Max(1, Math.Min(modelLimit, cfg));
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
			LoadModel();

			// 透传推理参数
			var p = new JObject();
			TryAddParam(p, "threshold");
			TryAddParam(p, "iou_threshold");
			TryAddParam(p, "top_k");
			TryAddParam(p, "return_polygon");
			TryAddParam(p, "epsilon");
			TryAddParam(p, "batch_size");

			int effectiveBatch = ResolveEffectiveBatchLimit();
			p["batch_size"] = effectiveBatch;

			var rgbInputs = new List<Mat>();
			var convertedRgbToDispose = new List<Mat>();
			var wraps = new List<ModuleImage>();
			var sourceIndices = new List<int>();
			var buckets = new Dictionary<string, List<int>>();
			var bucketAreas = new Dictionary<string, int>();

			try
			{
				// 1) 收集可用输入并按 shape 分桶
				for (int i = 0; i < images.Count; i++)
				{
					var tup = Unwrap(images[i]);
					var wrap = tup.Item1;
					var mat = tup.Item2;
					if (mat == null || mat.Empty()) continue;

					// 调用方负责准备通道顺序；流程模型节点直接透传输入 Mat。
					Mat rgbMat = mat;

					int localIdx = rgbInputs.Count;
					rgbInputs.Add(rgbMat);
					wraps.Add(wrap);
					sourceIndices.Add(i);

					int h = Math.Max(0, rgbMat.Height);
					int w = Math.Max(0, rgbMat.Width);
					int c = Math.Max(1, rgbMat.Channels());
					string key = h.ToString() + "x" + w.ToString() + "x" + c.ToString();
					if (!buckets.ContainsKey(key))
					{
						buckets[key] = new List<int>();
						bucketAreas[key] = h * w;
					}
					buckets[key].Add(localIdx);
				}

				var sampleByLocal = new List<Utils.CSharpSampleResult>();
				for (int i = 0; i < rgbInputs.Count; i++)
				{
					sampleByLocal.Add(new Utils.CSharpSampleResult(new List<Utils.CSharpObjectResult>()));
				}

				// 2) 按桶面积从大到小执行 batch，并回填到 local 下标
				var bucketKeys = new List<string>(buckets.Keys);
				bucketKeys.Sort((a, b) => bucketAreas[b].CompareTo(bucketAreas[a]));
				foreach (var key in bucketKeys)
				{
					var localIndices = buckets[key];
					for (int start = 0; start < localIndices.Count; start += effectiveBatch)
					{
						int take = Math.Min(effectiveBatch, localIndices.Count - start);
						var chunkLocals = localIndices.GetRange(start, take);
						var chunkMats = new List<Mat>(chunkLocals.Count);
						for (int k = 0; k < chunkLocals.Count; k++)
						{
							chunkMats.Add(rgbInputs[chunkLocals[k]]);
						}

                        var inferSw = Stopwatch.StartNew();
                        Utils.CSharpResult res = p.Count > 0 ? _model.InferBatch(chunkMats, p) : _model.InferBatch(chunkMats, null);
                        inferSw.Stop();
                        InferTiming.AddDlcvInferMs(inferSw.Elapsed.TotalMilliseconds);
						var batchSamples = res.SampleResults ?? new List<Utils.CSharpSampleResult>();
						for (int k = 0; k < chunkLocals.Count; k++)
						{
							int localIdx = chunkLocals[k];
							if (k < batchSamples.Count)
							{
								sampleByLocal[localIdx] = batchSamples[k];
							}
							else
							{
								sampleByLocal[localIdx] = new Utils.CSharpSampleResult(new List<Utils.CSharpObjectResult>());
							}
						}
					}
				}

				// 3) 按原输入顺序回填结果
				int outIndex = 0;
				for (int localIdx = 0; localIdx < rgbInputs.Count; localIdx++)
				{
					int srcIdx = sourceIndices[localIdx];
					var wrap = wraps[localIdx];
					outImages.Add(images[srcIdx]);

					var entry = new JObject
					{
						["type"] = "local",
						["index"] = outIndex,
						["origin_index"] = wrap != null ? wrap.OriginalIndex : srcIdx,
						["transform"] = wrap != null && wrap.TransformState != null ? JObject.FromObject(wrap.TransformState.ToDict()) : null,
						["sample_results"] = ConvertToLocalSamples(sampleByLocal[localIdx])
					};
					outResults.Add(entry);
					outIndex += 1;
				}

				return new ModuleIO(outImages, outResults);
			}
			finally
			{
				for (int i = 0; i < convertedRgbToDispose.Count; i++)
				{
					try { convertedRgbToDispose[i].Dispose(); } catch { }
				}
			}
		}

		private static JArray ConvertToLocalSamples(Utils.CSharpSampleResult sr)
		{
			var list = new JArray();
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
				// 将 mask 以 RLE 的形式存储到 JSON（mask_rle），避免直接写入原始像素或多边形点集
				if (obj.WithMask && obj.Mask != null && !obj.Mask.Empty())
				{
					try
					{
						var maskInfo = MaskRleUtils.MatToMaskInfo(obj.Mask);
						o["mask_rle"] = maskInfo;
					}
					catch
					{
					}
				}
				var extraInfo = obj.ExtraInfo ?? new JObject();
				if (extraInfo.HasValues)
				{
					o["extra_info"] = extraInfo;
				}
				list.Add(o);
			}
			return list;
		}

		private static double ReadScoreForSort(JToken token)
		{
			var obj = token as JObject;
			if (obj == null) return 0.0;
			try
			{
				return obj.Value<double?>("score") ?? 0.0;
			}
			catch
			{
				double score;
				return double.TryParse(obj["score"] != null ? obj["score"].ToString() : null, out score) ? score : 0.0;
			}
		}

		protected static void KeepTopKByScore(JArray samples, int topK)
		{
			if (samples == null || topK <= 0 || samples.Count <= topK) return;

			var ordered = new List<JToken>();
			foreach (var sample in samples)
			{
				ordered.Add(sample);
			}

			ordered.Sort((a, b) => ReadScoreForSort(b).CompareTo(ReadScoreForSort(a)));

			samples.Clear();
			for (int i = 0; i < topK && i < ordered.Count; i++)
			{
				samples.Add(ordered[i]);
			}
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

		public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var baseIo = base.Process(imageList, resultList);
			var imagesOut = baseIo != null ? (baseIo.ImageList ?? new List<ModuleImage>()) : new List<ModuleImage>();
			var resultsOut = baseIo != null ? (baseIo.ResultList ?? new JArray()) : new JArray();
			int topK = Math.Max(0, ReadInt("top_k", 1));

			int n = Math.Min(resultsOut.Count, imagesOut.Count);
			for (int i = 0; i < n; i++)
			{
				var entry = resultsOut[i] as JObject;
				if (entry == null) continue;
				var samples = entry["sample_results"] as JArray;
				if (samples == null) continue;
				KeepTopKByScore(samples, topK);

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




