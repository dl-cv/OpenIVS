using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
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
		protected int _maxBatchSize = 0;

		// 按 (modelPath|deviceId|rpcMode) 缓存 Model 实例，避免 Flow 每次推理重复加载
		private static readonly Dictionary<string, Model> _modelCache = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
		private static readonly object _modelCacheLock = new object();

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

				string normalizedPath = NormalizeModelPath(_modelPath);
				string cacheKey = (normalizedPath ?? "") + "|" + deviceId + "|" + rpcMode;
				lock (_modelCacheLock)
				{
					if (!_modelCache.TryGetValue(cacheKey, out _model) || _model == null)
					{
						_model = new Model(normalizedPath ?? _modelPath, deviceId, rpcMode, true);
						_modelCache[cacheKey] = _model;
					}
				}
				// 多个模块/面可共享同一路径模型实例；不在模块侧单独 Dispose。
				SyncModelMeta();
			}
			else
			{
				SyncModelMeta();
			}
		}

		public static void ClearModelCache()
		{
			lock (_modelCacheLock)
			{
				_modelCache.Clear();
			}
		}

		private static string NormalizeModelPath(string modelPath)
		{
			if (string.IsNullOrWhiteSpace(modelPath))
				return modelPath;
			try
			{
				return Path.GetFullPath(modelPath.Trim());
			}
			catch
			{
				return modelPath.Trim();
			}
		}

		protected void SyncModelMeta()
		{
			if (_model == null) return;
			// 只在首次获取时做 DeepClone，避免每次推理重复复制大对象（OCR 的 model_info 可能包含字符集，DeepClone 很慢）
			if (_modelInfo == null)
			{
				try
				{
					_modelInfo = _model.GetCachedModelInfo();
					if (_modelInfo == null)
					{
						_modelInfo = _model.GetModelInfo();
					}
				}
				catch { }
			}

			if (_maxShape == null)
			{
				try { _maxShape = _model.GetCachedMaxShape(); } catch { _maxShape = null; }
			}
			if (_maxBatchSize <= 0)
			{
				try { _maxBatchSize = _model.GetMaxBatchSize(); } catch { _maxBatchSize = 1; }
			}

			// loaded_model_meta 仅在 LoadModels() 加载阶段被 FlowGraphModel 读取，推理阶段无需重复写入
			// 跳过此处可避免每次推理创建 Dictionary/JObject 的开销，同时消除 OCR 大 model_info 的引用压力
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

			// 入口阈值在最终结果层处理；calc_mean 显式传入时只影响本次推理。
			var p = BuildInferParams();
			// 注意：with_mask 仅控制最终返回格式，不在这里传给子模型。
			// 流程内部需要始终保留 mask_rle，否则 mask_to_rbox 等后处理节点会丢失结果。
			// output/return_json 会根据 infer_params.with_mask 决定是否把 mask 暴露给前端/JSON。

			int effectiveBatch = ResolveEffectiveBatchLimit();
			p["batch_size"] = effectiveBatch;

			var rgbInputs = new List<Mat>();
			var convertedRgbToDispose = new List<Mat>();
			var wraps = new List<ModuleImage>();
			var sourceIndices = new List<int>();
			var buckets = new Dictionary<string, List<int>>();
			var bucketAreas = new Dictionary<string, int>();
			int inferCallCount = 0;
			int maxActualBatch = 0;

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
						inferCallCount += 1;
						maxActualBatch = Math.Max(maxActualBatch, chunkMats.Count);

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

				InferTiming.AddFlowModelBatchInfo(
					NodeId,
					ReadStringOrDefault("model_path_original", _modelPath),
					rgbInputs.Count,
					effectiveBatch,
					inferCallCount,
					maxActualBatch);

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
					["with_mean"] = obj.WithMean,
					["foreground_mean"] = obj.ForegroundMean,
					["background_mean"] = obj.BackgroundMean,
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

		private JObject BuildInferParams()
		{
			var p = new JObject();
			TryAddParam(p, "threshold");
			TryAddParam(p, "iou_threshold");
			TryAddParam(p, "calc_mean");
			TryOverrideInferParam(p, "calc_mean");
			TryAddParam(p, "top_k");
			TryAddParam(p, "return_polygon");
			TryAddParam(p, "epsilon");
			TryAddParam(p, "batch_size");
			return p;
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

		private void TryOverrideInferParam(JObject p, string key)
		{
			if (Context == null) return;
			try
			{
				var inferParams = Context.Get<JObject>("infer_params", null);
				var value = inferParams != null ? inferParams[key] : null;
				if (value != null && value.Type != JTokenType.Null)
				{
					p[key] = value.DeepClone();
				}
			}
			catch { }
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
			var sourceImages = imageList ?? new List<ModuleImage>();
			Dictionary<ModuleImage, ModuleImage> sourceByInferImage;
			var inferImages = PrepareInferenceImages(
				sourceImages,
				ReadBoolProperty("use_affine_img", true),
				out sourceByInferImage);

			var baseIo = base.Process(inferImages, resultList);
			var imagesOut = baseIo != null ? (baseIo.ImageList ?? new List<ModuleImage>()) : new List<ModuleImage>();
			var resultsOut = baseIo != null ? (baseIo.ResultList ?? new JArray()) : new JArray();

			if (sourceByInferImage != null)
			{
				for (int i = 0; i < imagesOut.Count; i++)
				{
					ModuleImage sourceImage;
					if (imagesOut[i] != null && sourceByInferImage.TryGetValue(imagesOut[i], out sourceImage))
					{
						imagesOut[i] = sourceImage;
					}
				}
			}
			int topK = Math.Max(0, ReadInt("top_k", 1));
			var categoryByIndex = new Dictionary<int, string>();

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
				JObject top1 = null;
				double top1Score = double.MinValue;

				foreach (var s in samples)
				{
					var so = s as JObject;
					if (so == null) continue;
					double score = so.Value<double?>("score") ?? 0.0;
					if (top1 == null || score > top1Score)
					{
						top1 = so;
						top1Score = score;
					}

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

				int index = entry.Value<int?>("index") ?? i;
				string categoryName = top1 != null ? top1.Value<string>("category_name") : null;
				if (categoryName != null) categoryByIndex[index] = categoryName;
			}

			if (resultList != null && resultList.Count > 0)
			{
				var overlaidResults = new JArray();
				foreach (var token in resultList)
				{
					var sourceEntry = token as JObject;
					var overlaidEntry = sourceEntry != null ? (JObject)sourceEntry.DeepClone() : null;
					int index = sourceEntry != null ? (sourceEntry.Value<int?>("index") ?? -1) : -1;
					if (overlaidEntry != null
						&& string.Equals(sourceEntry.Value<string>("type"), "local", StringComparison.Ordinal)
						&& categoryByIndex.TryGetValue(index, out string categoryName))
					{
						var samples = overlaidEntry["sample_results"] as JArray;
						if (samples != null)
						{
							foreach (var sample in samples)
							{
								var sampleObject = sample as JObject;
								if (sampleObject != null) sampleObject["category_name"] = categoryName;
							}
						}
					}
					overlaidResults.Add(overlaidEntry != null ? (JToken)overlaidEntry : token.DeepClone());
				}
				return new ModuleIO(imagesOut, overlaidResults);
			}

			return new ModuleIO(imagesOut, resultsOut);
		}

		private static List<ModuleImage> PrepareInferenceImages(
			List<ModuleImage> sourceImages,
			bool useAffineImage,
			out Dictionary<ModuleImage, ModuleImage> sourceByInferImage)
		{
			sourceByInferImage = null;
			if (!useAffineImage) return sourceImages;

			var inferImages = new List<ModuleImage>(sourceImages.Count);
			sourceByInferImage = new Dictionary<ModuleImage, ModuleImage>();
			foreach (var sourceImage in sourceImages)
			{
				if (sourceImage == null || sourceImage.AffineImage == null || sourceImage.AffineImage.Empty())
				{
					inferImages.Add(sourceImage);
					continue;
				}

				var inferImage = new ModuleImage(
					sourceImage.AffineImage,
					sourceImage.OriginalImage,
					sourceImage.TransformState,
					sourceImage.OriginalIndex)
				{
					AffineImage = sourceImage.AffineImage,
					UniqueId = sourceImage.UniqueId,
					SlidingMeta = sourceImage.SlidingMeta != null ? (JObject)sourceImage.SlidingMeta.DeepClone() : null
				};
				inferImages.Add(inferImage);
				sourceByInferImage[inferImage] = sourceImage;
			}
			return inferImages;
		}

		private bool ReadBoolProperty(string key, bool defaultValue)
		{
			if (Properties == null || !Properties.TryGetValue(key, out object raw) || raw == null) return defaultValue;
			try
			{
				if (raw is bool value) return value;
				if (raw is string text)
				{
					if (bool.TryParse(text, out bool parsedBool)) return parsedBool;
					if (int.TryParse(text, out int parsedInt)) return parsedInt != 0;
				}
				return Convert.ToBoolean(raw);
			}
			catch
			{
				return defaultValue;
			}
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
            var sourceImages = imageList ?? new List<ModuleImage>();
            var inferImages = sourceImages;
            Dictionary<ModuleImage, ModuleImage> sourceByInferImage = null;
            if (ReadBoolProperty("use_affine_img", true))
            {
                inferImages = new List<ModuleImage>(sourceImages.Count);
                sourceByInferImage = new Dictionary<ModuleImage, ModuleImage>();
                foreach (var sourceImage in sourceImages)
                {
                    if (sourceImage == null || sourceImage.AffineImage == null || sourceImage.AffineImage.Empty())
                    {
                        inferImages.Add(sourceImage);
                        continue;
                    }

                    var inferImage = new ModuleImage(
                        sourceImage.AffineImage,
                        sourceImage.OriginalImage,
                        sourceImage.TransformState,
                        sourceImage.OriginalIndex)
                    {
                        AffineImage = sourceImage.AffineImage,
                        UniqueId = sourceImage.UniqueId,
                        SlidingMeta = sourceImage.SlidingMeta != null ? (JObject)sourceImage.SlidingMeta.DeepClone() : null
                    };
                    inferImages.Add(inferImage);
                    sourceByInferImage[inferImage] = sourceImage;
                }
            }

            var baseIo = base.Process(inferImages, resultList);
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

            var ocrTextByIndex = new Dictionary<int, string>();
            foreach (var token in resultsOut)
            {
                var entry = token as JObject;
                var samples = entry != null ? entry["sample_results"] as JArray : null;
                if (entry == null || samples == null) continue;

                JObject best = null;
                double bestScore = double.MinValue;
                foreach (var sample in samples)
                {
                    var sampleObject = sample as JObject;
                    string text = sampleObject != null ? sampleObject.Value<string>("category_name") : null;
                    if (string.IsNullOrEmpty(text)) continue;
                    double score = sampleObject.Value<double?>("score") ?? 0.0;
                    if (best == null || score > bestScore)
                    {
                        best = sampleObject;
                        bestScore = score;
                    }
                }

                if (best != null)
                {
                    int index = entry.Value<int?>("index") ?? -1;
                    ocrTextByIndex[index] = best.Value<string>("category_name");
                }
            }

            if (sourceByInferImage != null)
            {
                for (int i = 0; i < imagesOut.Count; i++)
                {
                    ModuleImage sourceImage;
                    if (imagesOut[i] != null && sourceByInferImage.TryGetValue(imagesOut[i], out sourceImage))
                    {
                        imagesOut[i] = sourceImage;
                    }
                }
            }

            if (resultList != null && resultList.Count > 0)
            {
                var overlaidResults = new JArray();
                foreach (var token in resultList)
                {
                    var sourceEntry = token as JObject;
                    var overlaidEntry = sourceEntry != null ? (JObject)sourceEntry.DeepClone() : null;
                    int index = sourceEntry != null ? (sourceEntry.Value<int?>("index") ?? -1) : -1;
                    if (overlaidEntry != null
                        && string.Equals(sourceEntry.Value<string>("type"), "local", StringComparison.Ordinal)
                        && ocrTextByIndex.TryGetValue(index, out string ocrText))
                    {
                        var samples = overlaidEntry["sample_results"] as JArray;
                        if (samples != null)
                        {
                            foreach (var sample in samples)
                            {
                                var sampleObject = sample as JObject;
                                if (sampleObject != null) sampleObject["category_name"] = ocrText;
                            }
                        }
                    }
                    overlaidResults.Add(overlaidEntry != null ? (JToken)overlaidEntry : token.DeepClone());
                }
                return new ModuleIO(imagesOut, overlaidResults);
            }

            return new ModuleIO(imagesOut, resultsOut);
        }

        private bool ReadBoolProperty(string key, bool defaultValue)
        {
            if (Properties == null || !Properties.TryGetValue(key, out object raw) || raw == null) return defaultValue;
            try
            {
                if (raw is bool value) return value;
                if (raw is string text)
                {
                    if (bool.TryParse(text, out bool parsedBool)) return parsedBool;
                    if (int.TryParse(text, out int parsedInt)) return parsedInt != 0;
                }
                return Convert.ToBoolean(raw);
            }
            catch
            {
                return defaultValue;
            }
        }
	}
}




