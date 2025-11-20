using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
	/// <summary>
	/// features/sliding_merge：将滑窗局部结果映射回原图坐标并合并（最小可编译骨架）。
	/// 目标：与统一 I/O 对齐，输出每张原图一个 local 条目（transform=null）。
	/// </summary>
	public class SlidingMergeResults : BaseModule
	{
		static SlidingMergeResults()
		{
			ModuleRegistry.Register("features/sliding_merge", typeof(SlidingMergeResults));
		}

		public SlidingMergeResults(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context)
		{
		}

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var inImages = imageList ?? new List<ModuleImage>();
			var inResults = resultList ?? new JArray();

			// 将输入中含 transform==null 的视为“原图”，建立 origin_index->image 映射
			var originIndexToImage = new Dictionary<int, ModuleImage>();
			for (int i = 0; i < inImages.Count; i++)
			{
				var (wrap, mat) = Unwrap(inImages[i]);
                if (mat == null || mat.Empty()) continue;
				int originIndex = wrap != null ? wrap.OriginalIndex : i;
				var st = wrap != null ? wrap.TransformState : null;
				if (st == null || st.AffineMatrix2x3 == null)
				{
					// 认为该图像与原图坐标系一致
					originIndexToImage[originIndex] = inImages[i];
				}
			}

			// 收集每个 origin_index 的所有局部结果，并合并（这里提供简单合并：直接拼接）
			var originIndexToSamples = new Dictionary<int, List<JObject>>();
			foreach (var token in inResults)
			{
				var entry = token as JObject;
				if (entry == null) continue;
				int originIndex = entry["origin_index"]?.Value<int?>() ?? (entry["index"]?.Value<int?>() ?? 0);
                if (!originIndexToSamples.TryGetValue(originIndex, out List<JObject> list))
				{
                    list = new List<JObject>();
					originIndexToSamples[originIndex] = list;
				}
				var srs = entry["sample_results"] as JArray;
                if (srs != null)
                {
					foreach (var o in srs) if (o is JObject oj) list.Add(oj);
                }
			}

			var outImages = new List<ModuleImage>();
			var outResults = new JArray();
			int outIdx = 0;

			foreach (var kv in originIndexToImage)
			{
				int originIndex = kv.Key;
				outImages.Add(kv.Value);

                var samples = new List<JObject>();
                if (originIndexToSamples.TryGetValue(originIndex, out List<JObject> s))
				{
					// 简单合并：直接拼接；后续可替换为 IoU 去重与几何合并
					samples.AddRange(s);
				}

				var mergedEntry = new JObject
                {
                    ["type"] = "local",
                    ["index"] = outIdx,
                    ["origin_index"] = originIndex,
                    ["transform"] = null,
                    ["sample_results"] = new JArray(samples)
                };
				outResults.Add(mergedEntry);
				outIdx += 1;
			}

			return new ModuleIO(outImages, outResults);
		}

        private static Tuple<ModuleImage, Mat> Unwrap(ModuleImage obj)
		{
			if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
			return Tuple.Create(obj, obj.ImageObject);
		}
	}
}




