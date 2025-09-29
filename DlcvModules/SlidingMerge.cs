using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

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

		public override Tuple<List<object>, List<Dictionary<string, object>>> Process(List<object> imageList = null, List<Dictionary<string, object>> resultList = null)
		{
			var inImages = imageList ?? new List<object>();
			var inResults = resultList ?? new List<Dictionary<string, object>>();

			// 将输入中含 transform==null 的视为“原图”，建立 origin_index->image 映射
			var originIndexToImage = new Dictionary<int, object>();
			for (int i = 0; i < inImages.Count; i++)
			{
				var (wrap, bmp) = Unwrap(inImages[i]);
				if (bmp == null) continue;
				int originIndex = wrap != null ? wrap.OriginalIndex : i;
				var st = wrap != null ? wrap.TransformState : null;
				if (st == null || st.AffineMatrix2x3 == null)
				{
					// 认为该图像与原图坐标系一致
					originIndexToImage[originIndex] = inImages[i];
				}
			}

			// 收集每个 origin_index 的所有局部结果，并合并（这里提供简单合并：直接拼接）
			var originIndexToSamples = new Dictionary<int, List<Dictionary<string, object>>>();
			foreach (var entry in inResults)
			{
				if (entry == null) continue;
				int originIndex = ReadInt(entry, "origin_index", ReadInt(entry, "index", 0));
				if (!originIndexToSamples.TryGetValue(originIndex, out List<Dictionary<string, object>> list))
				{
					list = new List<Dictionary<string, object>>();
					originIndexToSamples[originIndex] = list;
				}
				var srs = ReadSampleResults(entry);
				if (srs != null) list.AddRange(srs);
			}

			var outImages = new List<object>();
			var outResults = new List<Dictionary<string, object>>();
			int outIdx = 0;

			foreach (var kv in originIndexToImage)
			{
				int originIndex = kv.Key;
				outImages.Add(kv.Value);

				var samples = new List<Dictionary<string, object>>();
				if (originIndexToSamples.TryGetValue(originIndex, out List<Dictionary<string, object>> s))
				{
					// 简单合并：直接拼接；后续可替换为 IoU 去重与几何合并
					samples.AddRange(s);
				}

				var mergedEntry = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
				mergedEntry["type"] = "local";
				mergedEntry["index"] = outIdx;
				mergedEntry["origin_index"] = originIndex;
				mergedEntry["transform"] = null; // 已回到原图坐标
				mergedEntry["sample_results"] = samples;
				outResults.Add(mergedEntry);
				outIdx += 1;
			}

			return Tuple.Create(outImages, outResults);
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

		private static int ReadInt(Dictionary<string, object> d, string k, int dv)
		{
			if (d == null || k == null || !d.TryGetValue(k, out object v) || v == null) return dv;
			try { return Convert.ToInt32(v); } catch { return dv; }
		}

		private static List<Dictionary<string, object>> ReadSampleResults(Dictionary<string, object> d)
		{
			if (d == null || !d.TryGetValue("sample_results", out object v) || v == null) return null;
			return v as List<Dictionary<string, object>>;
		}
	}
}



