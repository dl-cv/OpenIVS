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
			// Python: @register_module("pre_process/sliding_merge", alias=["features/sliding_merge"])
			ModuleRegistry.Register("pre_process/sliding_merge", typeof(SlidingMergeResults));
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
			if (inImages.Count == 0)
			{
				return new ModuleIO(new List<ModuleImage>(), (JArray)inResults.DeepClone());
			}

			var transToSamples = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
			var indexToSamples = new Dictionary<int, List<JObject>>();
			var originToSamples = new Dictionary<int, List<JObject>>();
			var passthroughResults = new List<JToken>();

			foreach (var token in inResults)
			{
				var entry = token as JObject;
				if (entry == null || !string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase))
				{
					if (token != null) passthroughResults.Add(token.DeepClone());
					continue;
				}

				var dets = CloneSampleList(entry["sample_results"] as JArray);
				string sig = SerializeTransformSig(entry["transform"] as JObject);
				if (!string.IsNullOrEmpty(sig))
				{
					AppendSamples(transToSamples, sig, dets);
					continue;
				}

				int idx = SafeInt(entry["index"], -1);
				if (idx >= 0)
				{
					AppendSamples(indexToSamples, idx, dets);
					continue;
				}

				int originIdx = SafeInt(entry["origin_index"], -1);
				if (originIdx >= 0)
				{
					AppendSamples(originToSamples, originIdx, dets);
					continue;
				}

				passthroughResults.Add(entry.DeepClone());
			}

			var originToImage = new Dictionary<int, ModuleImage>();
			var originToItems = new Dictionary<int, List<JObject>>();

			// 对齐 Python：不要假设输入里存在 transform=null 的原图，而是从每个滑窗 wrapper 反构造原图输出。
			for (int i = 0; i < inImages.Count; i++)
			{
				var tuple = Unwrap(inImages[i]);
				var wrap = tuple.Item1;
				var mat = tuple.Item2;
				if (wrap == null || mat == null || mat.Empty()) continue;

				int originIndex = wrap.OriginalIndex;
				if (!originToImage.ContainsKey(originIndex))
				{
					var originWrap = BuildOriginWrap(wrap, originIndex);
					if (originWrap != null)
					{
						originToImage[originIndex] = originWrap;
					}
				}

				var localSamples = MatchSamplesForWindow(i, wrap, transToSamples, indexToSamples, originToSamples);
				if (localSamples.Count == 0) continue;

				if (!originToItems.TryGetValue(originIndex, out List<JObject> mappedItems))
				{
					mappedItems = new List<JObject>();
					originToItems[originIndex] = mappedItems;
				}
				mappedItems.AddRange(ToGlobalItems(localSamples, wrap));
			}

			var orderedOriginIndices = originToImage.Keys.ToList();
			orderedOriginIndices.Sort();

			var outImages = new List<ModuleImage>();
			var outResults = new JArray();
			int outIdx = 0;

			foreach (int originIndex in orderedOriginIndices)
			{
				outImages.Add(originToImage[originIndex]);
				var samples = new JArray();
				if (originToItems.TryGetValue(originIndex, out List<JObject> mapped))
				{
					foreach (var item in mapped)
					{
						samples.Add(item);
					}
				}

				outResults.Add(new JObject
				{
					["type"] = "local",
					["index"] = outIdx,
					["origin_index"] = originIndex,
					["transform"] = null,
					["sample_results"] = samples
				});
				outIdx += 1;
			}

			foreach (var token in passthroughResults)
			{
				if (token != null) outResults.Add(token);
			}

			return new ModuleIO(outImages, outResults);
		}

		private static List<JObject> MatchSamplesForWindow(
			int imageIndex,
			ModuleImage wrap,
			Dictionary<string, List<JObject>> transToSamples,
			Dictionary<int, List<JObject>> indexToSamples,
			Dictionary<int, List<JObject>> originToSamples)
		{
			string sig = SerializeTransformSig(wrap != null ? wrap.TransformState : null);
			if (!string.IsNullOrEmpty(sig) && transToSamples.TryGetValue(sig, out List<JObject> byTransform))
			{
				return CloneSampleList(byTransform);
			}
			if (indexToSamples.TryGetValue(imageIndex, out List<JObject> byIndex))
			{
				return CloneSampleList(byIndex);
			}
			if (wrap != null && originToSamples.TryGetValue(wrap.OriginalIndex, out List<JObject> byOrigin))
			{
				return CloneSampleList(byOrigin);
			}
			return new List<JObject>();
		}

		private static ModuleImage BuildOriginWrap(ModuleImage wrap, int originIndex)
		{
			if (wrap == null) return null;

			Mat original = wrap.OriginalImage;
			if (original == null || original.Empty())
			{
				original = wrap.ImageObject;
			}

			int w0 = 0;
			int h0 = 0;
			if (original != null && !original.Empty())
			{
				w0 = original.Width;
				h0 = original.Height;
			}
			else if (wrap.TransformState != null)
			{
				w0 = wrap.TransformState.OriginalWidth;
				h0 = wrap.TransformState.OriginalHeight;
			}

			if (w0 <= 0 || h0 <= 0)
			{
				return null;
			}

			if (original == null || original.Empty())
			{
				original = new Mat(h0, w0, MatType.CV_8UC3, Scalar.Black);
			}

			return new ModuleImage(original, original, new TransformationState(w0, h0), originIndex);
		}

		private static List<JObject> ToGlobalItems(List<JObject> localSamples, ModuleImage wrap)
		{
			var items = new List<JObject>();
			foreach (var det in localSamples)
			{
				var bbox = det["bbox"] as JArray;
				if (bbox == null || bbox.Count < 4) continue;

				var mappedDet = (JObject)det.DeepClone();
				var meta = det["metadata"] as JObject;
				var metaOut = meta != null ? (JObject)meta.DeepClone() : new JObject();

				if (IsRotated(det, bbox))
				{
					var rotated = MapRotatedBoxToOriginal(det, bbox, wrap);
					if (rotated == null) continue;

					mappedDet["bbox"] = new JArray { rotated[0], rotated[1], rotated[2], rotated[3] };
					mappedDet["angle"] = rotated[4];
					mappedDet["with_angle"] = true;
					metaOut["global_bbox"] = new JArray { rotated[0], rotated[1], rotated[2], rotated[3] };
					metaOut["is_rotated"] = true;
					mappedDet["metadata"] = metaOut;
					items.Add(mappedDet);
					continue;
				}

				int[] globalXyxy = MapAabbToOriginal(det, bbox, wrap);
				if (globalXyxy == null) continue;

				int x = globalXyxy[0];
				int y = globalXyxy[1];
				int w = Math.Max(1, globalXyxy[2] - globalXyxy[0]);
				int h = Math.Max(1, globalXyxy[3] - globalXyxy[1]);

				mappedDet["bbox"] = new JArray { x, y, w, h };
				metaOut["global_bbox"] = new JArray { x, y, w, h };
				mappedDet["metadata"] = metaOut;
				items.Add(mappedDet);
			}

			return items;
		}

		private static bool IsRotated(JObject det, JArray bbox)
		{
			if (bbox == null || bbox.Count < 4) return false;
			bool withAngle = det["with_angle"]?.Value<bool?>() ?? false;
			if (!withAngle) return bbox.Count >= 5;
			double angle = SafeToDouble(det["angle"]);
			return Math.Abs(angle) > 1e-8 || bbox.Count >= 5;
		}

		private static int[] MapAabbToOriginal(JObject det, JArray bbox, ModuleImage wrap)
		{
			if (bbox == null || bbox.Count < 4 || wrap == null) return null;

			double x = SafeToDouble(bbox[0]);
			double y = SafeToDouble(bbox[1]);
			double w = Math.Abs(SafeToDouble(bbox[2]));
			double h = Math.Abs(SafeToDouble(bbox[3]));
			int currentW, currentH, originalW, originalH;
			GetImageSizes(wrap, out currentW, out currentH, out originalW, out originalH);

			var bboxCurrent = ClampXYXY(x, y, x + w, y + h, currentW, currentH);
			int[] mapped = null;
			try
			{
				mapped = MapAabbToOriginalAndClamp(wrap.TransformState, bboxCurrent, originalW, originalH);
			}
			catch
			{
				mapped = null;
			}

			if (mapped != null) return mapped;

			var meta = det["metadata"] as JObject;
			var globalBbox = meta != null ? meta["global_bbox"] as JArray : null;
			return ParseGlobalBboxToAabb(globalBbox, originalW, originalH);
		}

		private static double[] MapRotatedBoxToOriginal(JObject det, JArray bbox, ModuleImage wrap)
		{
			if (bbox == null || bbox.Count < 4 || wrap == null) return null;

			double cx = SafeToDouble(bbox[0]);
			double cy = SafeToDouble(bbox[1]);
			double w = Math.Abs(SafeToDouble(bbox[2]));
			double h = Math.Abs(SafeToDouble(bbox[3]));
			double ang = SafeToDouble(det["angle"]);
			if (Math.Abs(ang) > 3.2) ang = ang * Math.PI / 180.0;

			double[] t = BuildTC2O(wrap.TransformState);
			double ncx = t[0] * cx + t[1] * cy + t[2];
			double ncy = t[3] * cx + t[4] * cy + t[5];

			double l00 = t[0], l01 = t[1];
			double l10 = t[3], l11 = t[4];
			double cos = Math.Cos(ang), sin = Math.Sin(ang);
			double ux = cos, uy = sin;
			double vx = -sin, vy = cos;

			double tuxX = l00 * ux + l01 * uy;
			double tuxY = l10 * ux + l11 * uy;
			double tvxX = l00 * vx + l01 * vy;
			double tvxY = l10 * vx + l11 * vy;
			double scaleW = Math.Sqrt(tuxX * tuxX + tuxY * tuxY);
			double scaleH = Math.Sqrt(tvxX * tvxX + tvxY * tvxY);

			return new[]
			{
				ncx,
				ncy,
				Math.Max(1.0, w * scaleW),
				Math.Max(1.0, h * scaleH),
				Math.Atan2(tuxY, tuxX)
			};
		}

		private static void GetImageSizes(ModuleImage wrap, out int currentW, out int currentH, out int originalW, out int originalH)
		{
			var current = wrap != null ? wrap.ImageObject : null;
			var original = wrap != null ? wrap.OriginalImage : null;

			currentW = current != null && !current.Empty() ? current.Width : 0;
			currentH = current != null && !current.Empty() ? current.Height : 0;
			originalW = original != null && !original.Empty() ? original.Width : 0;
			originalH = original != null && !original.Empty() ? original.Height : 0;

			if (wrap != null && wrap.TransformState != null)
			{
				if (currentW <= 0 && wrap.TransformState.OutputSize != null && wrap.TransformState.OutputSize.Length >= 2)
				{
					currentW = wrap.TransformState.OutputSize[0];
					currentH = wrap.TransformState.OutputSize[1];
				}
				if (originalW <= 0) originalW = wrap.TransformState.OriginalWidth;
				if (originalH <= 0) originalH = wrap.TransformState.OriginalHeight;
			}

			if (currentW <= 0) currentW = Math.Max(1, originalW);
			if (currentH <= 0) currentH = Math.Max(1, originalH);
			if (originalW <= 0) originalW = Math.Max(1, currentW);
			if (originalH <= 0) originalH = Math.Max(1, currentH);
		}

		private static string SerializeTransformSig(TransformationState st)
		{
			if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length < 6)
			{
				return null;
			}

			int cbx = 0, cby = 0, cbw = 0, cbh = 0;
			if (st.CropBox != null && st.CropBox.Length >= 4)
			{
				cbx = st.CropBox[0];
				cby = st.CropBox[1];
				cbw = st.CropBox[2];
				cbh = st.CropBox[3];
			}

			int outW = 0, outH = 0;
			if (st.OutputSize != null && st.OutputSize.Length >= 2)
			{
				outW = st.OutputSize[0];
				outH = st.OutputSize[1];
			}

			double[] a = st.AffineMatrix2x3;
			return string.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"cb:{0},{1},{2},{3}|os:{4},{5}|ori:{6},{7}|A:{8},{9},{10},{11},{12},{13}",
				cbx, cby, cbw, cbh,
				outW, outH,
				st.OriginalWidth, st.OriginalHeight,
				Round6(a[0]), Round6(a[1]), Round6(a[2]), Round6(a[3]), Round6(a[4]), Round6(a[5]));
		}

		private static string SerializeTransformSig(JObject stObj)
		{
			if (stObj == null) return null;
			try
			{
				int cbx = 0, cby = 0, cbw = 0, cbh = 0;
				var cb = stObj["crop_box"] as JArray;
				if (cb != null && cb.Count >= 4)
				{
					cbx = cb[0].Value<int>();
					cby = cb[1].Value<int>();
					cbw = cb[2].Value<int>();
					cbh = cb[3].Value<int>();
				}

				int outW = 0, outH = 0;
				var outputSize = stObj["output_size"] as JArray;
				if (outputSize != null && outputSize.Count >= 2)
				{
					outW = outputSize[0].Value<int>();
					outH = outputSize[1].Value<int>();
				}

				int originalW = stObj["original_width"]?.Value<int?>() ?? 0;
				int originalH = stObj["original_height"]?.Value<int?>() ?? 0;

				double[] a = null;
				var affine = stObj["affine_2x3"] as JArray;
				if (affine != null && affine.Count >= 6)
				{
					a = new double[6];
					for (int i = 0; i < 6; i++) a[i] = SafeToDouble(affine[i]);
				}

				if (a == null) return null;

				return string.Format(
					System.Globalization.CultureInfo.InvariantCulture,
					"cb:{0},{1},{2},{3}|os:{4},{5}|ori:{6},{7}|A:{8},{9},{10},{11},{12},{13}",
					cbx, cby, cbw, cbh,
					outW, outH,
					originalW, originalH,
					Round6(a[0]), Round6(a[1]), Round6(a[2]), Round6(a[3]), Round6(a[4]), Round6(a[5]));
			}
			catch
			{
				return null;
			}
		}

		private static string Round6(double value)
		{
			try
			{
				return Math.Round(value, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
			}
			catch
			{
				return "0";
			}
		}

		private static double[] BuildTC2O(TransformationState st)
		{
			if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length != 6)
			{
				return new double[] { 1, 0, 0, 0, 1, 0 };
			}
			try
			{
				return TransformationState.Inverse2x3(st.AffineMatrix2x3);
			}
			catch
			{
				return new double[] { 1, 0, 0, 0, 1, 0 };
			}
		}

		private static int[] ClampXYXY(double x1, double y1, double x2, double y2, int w, int h)
		{
			w = Math.Max(1, w);
			h = Math.Max(1, h);

			double minX = Math.Min(x1, x2);
			double maxX = Math.Max(x1, x2);
			double minY = Math.Min(y1, y2);
			double maxY = Math.Max(y1, y2);

			int ix1 = (int)Math.Max(0, Math.Min(w - 1, Math.Floor(minX)));
			int iy1 = (int)Math.Max(0, Math.Min(h - 1, Math.Floor(minY)));
			int ix2 = (int)Math.Max(ix1 + 1, Math.Min(w, Math.Ceiling(maxX)));
			int iy2 = (int)Math.Max(iy1 + 1, Math.Min(h, Math.Ceiling(maxY)));
			return new[] { ix1, iy1, ix2, iy2 };
		}

		private static int[] MapAabbToOriginalAndClamp(TransformationState st, int[] bboxCurrent, int originalW, int originalH)
		{
			if (bboxCurrent == null || bboxCurrent.Length < 4) return null;
			if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length != 6)
			{
				return ClampXYXY(bboxCurrent[0], bboxCurrent[1], bboxCurrent[2], bboxCurrent[3], originalW, originalH);
			}

			double[] inv = TransformationState.Inverse2x3(st.AffineMatrix2x3);
			int cropX = 0, cropY = 0;
			bool hasCropOffset = false;
			if (st.CropBox != null && st.CropBox.Length >= 4)
			{
				cropX = st.CropBox[0];
				cropY = st.CropBox[1];
				hasCropOffset = cropX != 0 || cropY != 0;
			}

			var pts = new[]
			{
				new Point2d(bboxCurrent[0], bboxCurrent[1]),
				new Point2d(bboxCurrent[2], bboxCurrent[1]),
				new Point2d(bboxCurrent[2], bboxCurrent[3]),
				new Point2d(bboxCurrent[0], bboxCurrent[3])
			};

			double minX = double.MaxValue, minY = double.MaxValue;
			double maxX = double.MinValue, maxY = double.MinValue;
			foreach (var pt in pts)
			{
				double ox = inv[0] * pt.X + inv[1] * pt.Y + inv[2];
				double oy = inv[3] * pt.X + inv[4] * pt.Y + inv[5];
				if (hasCropOffset)
				{
					ox += cropX;
					oy += cropY;
				}
				if (ox < minX) minX = ox;
				if (ox > maxX) maxX = ox;
				if (oy < minY) minY = oy;
				if (oy > maxY) maxY = oy;
			}

			return ClampXYXY(minX, minY, maxX, maxY, originalW, originalH);
		}

		private static int[] ParseGlobalBboxToAabb(JArray globalBbox, int originalW, int originalH)
		{
			if (globalBbox == null || globalBbox.Count < 4) return null;

			double a0 = SafeToDouble(globalBbox[0]);
			double a1 = SafeToDouble(globalBbox[1]);
			double a2 = SafeToDouble(globalBbox[2]);
			double a3 = SafeToDouble(globalBbox[3]);
			if (a2 > a0 && a3 > a1)
			{
				return ClampXYXY(a0, a1, a2, a3, originalW, originalH);
			}
			return ClampXYXY(a0, a1, a0 + Math.Abs(a2), a1 + Math.Abs(a3), originalW, originalH);
		}

		private static List<JObject> CloneSampleList(JArray array)
		{
			var list = new List<JObject>();
			if (array == null) return list;
			foreach (var token in array)
			{
				if (token is JObject obj)
				{
					list.Add((JObject)obj.DeepClone());
				}
			}
			return list;
		}

		private static List<JObject> CloneSampleList(List<JObject> samples)
		{
			var list = new List<JObject>();
			if (samples == null) return list;
			foreach (var sample in samples)
			{
				if (sample != null) list.Add((JObject)sample.DeepClone());
			}
			return list;
		}

		private static void AppendSamples<TKey>(Dictionary<TKey, List<JObject>> map, TKey key, List<JObject> samples)
		{
			if (map == null) return;
			if (!map.TryGetValue(key, out List<JObject> list))
			{
				list = new List<JObject>();
				map[key] = list;
			}
			if (samples == null) return;
			foreach (var sample in samples)
			{
				if (sample != null) list.Add((JObject)sample.DeepClone());
			}
		}

		private static int SafeInt(JToken token, int defaultValue)
		{
			if (token == null || token.Type == JTokenType.Null) return defaultValue;
			try { return token.Value<int>(); } catch { return defaultValue; }
		}

		private static double SafeToDouble(JToken token)
		{
			try
			{
				return token != null ? Convert.ToDouble(((JValue)token).Value, System.Globalization.CultureInfo.InvariantCulture) : 0.0;
			}
			catch
			{
				return 0.0;
			}
		}

		private static Tuple<ModuleImage, Mat> Unwrap(ModuleImage obj)
		{
			if (obj == null) return Tuple.Create<ModuleImage, Mat>(null, null);
			return Tuple.Create(obj, obj.ImageObject);
		}
	}
}




