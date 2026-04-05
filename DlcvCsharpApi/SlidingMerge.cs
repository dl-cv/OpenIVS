using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
	/// <summary>
	/// 对齐 Python sliding_merge.py：
	/// 1. 先按 transform/index/origin 绑定每个窗口的 sample_results
	/// 2. 无 sliding_meta 时只做坐标回投
	/// 3. 有 sliding_meta 时只比较右/下邻窗，并按 IOS 做合并
	/// 
	/// 约束：
	/// - C# 轴对齐框保持 xywh
	/// - metadata.global_bbox 记录 xyxy，便于后续兼容
	/// </summary>
	public class SlidingMergeResults : BaseModule
	{
		private sealed class UnionFind
		{
			private readonly Dictionary<Tuple<int, int>, Tuple<int, int>> _parent = new Dictionary<Tuple<int, int>, Tuple<int, int>>();

			public void Add(Tuple<int, int> key)
			{
				if (key == null) return;
				if (!_parent.ContainsKey(key)) _parent[key] = key;
			}

			public Tuple<int, int> Find(Tuple<int, int> key)
			{
				if (key == null) return null;
				Add(key);
				if (!_parent[key].Equals(key))
				{
					_parent[key] = Find(_parent[key]);
				}
				return _parent[key];
			}

			public void Union(Tuple<int, int> a, Tuple<int, int> b)
			{
				var ra = Find(a);
				var rb = Find(b);
				if (ra == null || rb == null || ra.Equals(rb)) return;
				_parent[rb] = ra;
			}
		}

		static SlidingMergeResults()
		{
			ModuleRegistry.Register("pre_process/sliding_merge", typeof(SlidingMergeResults));
			ModuleRegistry.Register("features/sliding_merge", typeof(SlidingMergeResults));
		}

		public SlidingMergeResults(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context)
		{
		}

		public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var wrappers = imageList ?? new List<ModuleImage>();
			var results = resultList ?? new JArray();
			if (wrappers.Count == 0)
			{
				return new ModuleIO(new List<ModuleImage>(), CloneJArray(results));
			}

			double iouTh = ReadDoubleProperty("iou_threshold", 0.2);
			bool dedupResults = ReadBoolProperty("dedup_results", true);
			string taskType = ReadStringProperty("task_type", "auto").Trim().ToLowerInvariant();

			var transToSamples = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
			var indexToSamples = new Dictionary<int, List<JObject>>();
			var originToSamples = new Dictionary<int, List<JObject>>();
			var otherResults = new List<JToken>();

			foreach (var token in results)
			{
				var entry = token as JObject;
				if (entry == null || !string.Equals(entry.Value<string>("type"), "local", StringComparison.OrdinalIgnoreCase))
				{
					if (token != null) otherResults.Add(token.DeepClone());
					continue;
				}

				var dets = entry["sample_results"] as JArray;
				if (dets == null) continue;

				string sig = SerializeTransform(entry["transform"] as JObject);
				if (!string.IsNullOrEmpty(sig))
				{
					AddSamplesRef(transToSamples, sig, dets);
					continue;
				}

				int idx = SafeInt(entry["index"], -1);
				if (idx >= 0)
				{
					AddSamplesRef(indexToSamples, idx, dets);
					continue;
				}

				int originIdx = SafeInt(entry["origin_index"], -1);
				if (originIdx >= 0)
				{
					AddSamplesRef(originToSamples, originIdx, dets);
					continue;
				}

				otherResults.Add(entry.DeepClone());
			}

			var windowDets = new Dictionary<int, List<JObject>>();
			for (int i = 0; i < wrappers.Count; i++)
			{
				var wrap = wrappers[i];
				var dets = new List<JObject>();
				string sig = SerializeTransform(wrap != null ? wrap.TransformState : null);
				if (!string.IsNullOrEmpty(sig) && transToSamples.TryGetValue(sig, out List<JObject> byTransform))
				{
					dets = CloneDetList(byTransform);
				}
				else if (indexToSamples.TryGetValue(i, out List<JObject> byIndex))
				{
					dets = CloneDetList(byIndex);
				}
				else if (wrap != null && originToSamples.TryGetValue(wrap.OriginalIndex, out List<JObject> byOrigin))
				{
					dets = CloneDetList(byOrigin);
				}
				windowDets[i] = dets;
			}

			var originIdxToImgwrap = new SortedDictionary<int, ModuleImage>();
			foreach (var wrap in wrappers)
			{
				if (wrap == null || originIdxToImgwrap.ContainsKey(wrap.OriginalIndex)) continue;
				var originWrap = BuildOriginWrap(wrap, wrap.OriginalIndex);
				if (originWrap != null) originIdxToImgwrap[wrap.OriginalIndex] = originWrap;
			}

			bool hasSlidingMeta = wrappers.Any(w => w != null && w.SlidingMeta != null);
			var tCache = new Dictionary<ModuleImage, double[]>();

			double[] getT(ModuleImage wrap)
			{
				if (wrap == null) return new double[] { 1, 0, 0, 0, 1, 0 };
				if (!tCache.TryGetValue(wrap, out double[] t))
				{
					t = BuildTC2O(wrap.TransformState);
					tCache[wrap] = t;
				}
				return t;
			}

			Point2f[] polyFromGlobalBbox(JToken token)
			{
				var bbox = token as JArray;
				if (bbox == null || bbox.Count < 4) return null;

				if (bbox.Count >= 5)
				{
					double cx = SafeDouble(bbox[0]);
					double cy = SafeDouble(bbox[1]);
					double w = Math.Abs(SafeDouble(bbox[2]));
					double h = Math.Abs(SafeDouble(bbox[3]));
					double angle = ReadAngle(bbox, null);
					if (w <= 0.0 || h <= 0.0) return null;
					var rect = new RotatedRect(
						new Point2f((float)cx, (float)cy),
						new Size2f((float)w, (float)h),
						(float)(angle * 180.0 / Math.PI));
					return Cv2.BoxPoints(rect);
				}

				double a0 = SafeDouble(bbox[0]);
				double a1 = SafeDouble(bbox[1]);
				double a2 = SafeDouble(bbox[2]);
				double a3 = SafeDouble(bbox[3]);
				double x1 = a0;
				double y1 = a1;
				double x2 = a2 > a0 && a3 > a1 ? a2 : a0 + Math.Abs(a2);
				double y2 = a2 > a0 && a3 > a1 ? a3 : a1 + Math.Abs(a3);
				return new[]
				{
					new Point2f((float)x1, (float)y1),
					new Point2f((float)x2, (float)y1),
					new Point2f((float)x2, (float)y2),
					new Point2f((float)x1, (float)y2)
				};
			}

			Point2f[] polyForDet(JObject det, ModuleImage wrap, double[] t)
			{
				if (det == null || wrap == null) return null;
				var bbox = det["bbox"] as JArray;
				if (bbox == null || bbox.Count < 4) return null;

				if (t == null) t = getT(wrap);
				if (IsRotatedDet(det))
				{
					double cx = SafeDouble(bbox[0]);
					double cy = SafeDouble(bbox[1]);
					double w = Math.Abs(SafeDouble(bbox[2]));
					double h = Math.Abs(SafeDouble(bbox[3]));
					double angle = ReadAngle(bbox, det);
					if (w <= 0.0 || h <= 0.0) return null;
					var rect = new RotatedRect(
						new Point2f((float)cx, (float)cy),
						new Size2f((float)w, (float)h),
						(float)(angle * 180.0 / Math.PI));
					return TransformPoints(t, Cv2.BoxPoints(rect));
				}

				double x = SafeDouble(bbox[0]);
				double y = SafeDouble(bbox[1]);
				double w0 = Math.Abs(SafeDouble(bbox[2]));
				double h0 = Math.Abs(SafeDouble(bbox[3]));
				if (w0 <= 0.0 || h0 <= 0.0) return null;
				return TransformPoints(t, new[]
				{
					new Point2f((float)x, (float)y),
					new Point2f((float)(x + w0), (float)y),
					new Point2f((float)(x + w0), (float)(y + h0)),
					new Point2f((float)x, (float)(y + h0))
				});
			}

			var detPolyCache = new Dictionary<Tuple<int, int>, Point2f[]>();
			Point2f[] getDetPoly(int winIdx, int detIdx, JObject det, ModuleImage wrap)
			{
				var key = Tuple.Create(winIdx, detIdx);
				if (!detPolyCache.TryGetValue(key, out Point2f[] poly))
				{
					poly = polyForDet(det, wrap, getT(wrap)) ?? polyFromGlobalBbox(ReadGlobalBbox(det));
					detPolyCache[key] = poly;
				}
				return poly;
			}

			JObject mappedAabbDet(JObject det, double[] aabb, bool setCombineFlag, List<JToken> sliceIndexTokens)
			{
				if (det == null || aabb == null) return null;

				int x1 = (int)Math.Round(aabb[0]);
				int y1 = (int)Math.Round(aabb[1]);
				int x2 = (int)Math.Round(aabb[2]);
				int y2 = (int)Math.Round(aabb[3]);
				int w = Math.Max(1, x2 - x1);
				int h = Math.Max(1, y2 - y1);

				var d2 = (JObject)det.DeepClone();
				d2["bbox"] = new JArray { x1, y1, w, h };
				d2["with_bbox"] = true;
				d2["with_angle"] = false;

				var meta = d2["metadata"] as JObject;
				if (meta == null)
				{
					meta = new JObject();
					d2["metadata"] = meta;
				}
				meta["global_bbox"] = new JArray { x1, y1, x2, y2 };
				if (setCombineFlag) meta["combine_flag"] = false;
				if (sliceIndexTokens != null && sliceIndexTokens.Count > 0)
				{
					var sliceArray = new JArray();
					foreach (var token in sliceIndexTokens) if (token != null) sliceArray.Add(token);
					meta["slice_index"] = sliceArray;
				}
				return d2;
			}

			JObject mappedRotatedDet(JObject det, double[] rbox)
			{
				if (det == null || rbox == null || rbox.Length < 5) return null;

				var d2 = (JObject)det.DeepClone();
				d2["bbox"] = new JArray { rbox[0], rbox[1], rbox[2], rbox[3] };
				d2["with_bbox"] = true;
				d2["with_angle"] = true;
				d2["angle"] = rbox[4];

				var meta = d2["metadata"] as JObject;
				if (meta == null)
				{
					meta = new JObject();
					d2["metadata"] = meta;
				}
				meta["global_bbox"] = new JArray { rbox[0], rbox[1], rbox[2], rbox[3], rbox[4] };
				meta["is_rotated"] = true;
				return d2;
			}

			List<JObject> toGlobalItemsForWindow(int winIdx, ModuleImage wrap)
			{
				var items = new List<JObject>();
				if (wrap == null || !windowDets.TryGetValue(winIdx, out List<JObject> dets)) return items;
				var t = getT(wrap);

				foreach (var det in dets)
				{
					if (det == null) continue;
					if (IsRotatedDet(det))
					{
						var rbox = RBoxLocalToGlobal(det, wrap, t);
						if (rbox == null)
						{
							var gb = ReadGlobalBbox(det) as JArray;
							if (gb != null && gb.Count >= 5)
							{
								rbox = new[]
								{
									SafeDouble(gb[0]),
									SafeDouble(gb[1]),
									Math.Abs(SafeDouble(gb[2])),
									Math.Abs(SafeDouble(gb[3])),
									ReadAngle(gb, null)
								};
							}
						}
						if (rbox != null) items.Add(mappedRotatedDet(det, rbox));
						continue;
					}

					var poly = polyForDet(det, wrap, t) ?? polyFromGlobalBbox(ReadGlobalBbox(det));
					if (poly == null) continue;
					items.Add(mappedAabbDet(det, PolyAabb(poly), false, null));
				}

				return items;
			}

			var groups = new Dictionary<int, List<int>>();
			for (int i = 0; i < wrappers.Count; i++)
			{
				var wrap = wrappers[i];
				if (wrap == null) continue;
				if (!groups.TryGetValue(wrap.OriginalIndex, out List<int> list))
				{
					list = new List<int>();
					groups[wrap.OriginalIndex] = list;
				}
				list.Add(i);
			}

			var originIdxToItems = new Dictionary<int, List<JObject>>();
			if (!dedupResults || !hasSlidingMeta)
			{
				for (int i = 0; i < wrappers.Count; i++)
				{
					var wrap = wrappers[i];
					if (wrap == null) continue;
					AddRange(originIdxToItems, wrap.OriginalIndex, toGlobalItemsForWindow(i, wrap));
				}
				return BuildOutput(originIdxToImgwrap, originIdxToItems, otherResults);
			}

			foreach (var group in groups)
			{
				var idxList = group.Value;
				var gridToIdx = new Dictionary<Tuple<int, int>, int>();
				foreach (int idx in idxList)
				{
					int gx, gy;
					if (TryGetGrid(wrappers[idx] != null ? wrappers[idx].SlidingMeta : null, out gx, out gy))
					{
						gridToIdx[Tuple.Create(gx, gy)] = idx;
					}
				}

				var removed = new Dictionary<int, HashSet<int>>();
				foreach (int idx in idxList) removed[idx] = new HashSet<int>();
				var uf = new UnionFind();

				foreach (var kv in gridToIdx)
				{
					int gx = kv.Key.Item1;
					int gy = kv.Key.Item2;
					int curIdx = kv.Value;

					foreach (var nbKey in new[] { Tuple.Create(gx + 1, gy), Tuple.Create(gx, gy + 1) })
					{
						if (!gridToIdx.TryGetValue(nbKey, out int nbIdx)) continue;
						var detsA = windowDets[curIdx];
						var detsB = windowDets[nbIdx];
						if (detsA.Count == 0 || detsB.Count == 0) continue;

						for (int ia = 0; ia < detsA.Count; ia++)
						{
							var da = detsA[ia];
							var polyA = getDetPoly(curIdx, ia, da, wrappers[curIdx]);
							if (polyA == null) continue;
							bool aIsRot = IsRotatedDet(da);

							for (int ib = 0; ib < detsB.Count; ib++)
							{
								var db = detsB[ib];
								if (!SameCategory(da, db)) continue;
								var polyB = getDetPoly(nbIdx, ib, db, wrappers[nbIdx]);
								if (polyB == null) continue;
								bool bIsRot = IsRotatedDet(db);
								bool modeRotate = taskType == "rotate" || (taskType == "auto" && aIsRot && bIsRot);

								if (modeRotate)
								{
									double riou = ComputeIoU(PolyAabb(polyA), PolyAabb(polyB));
									if (riou > iouTh)
									{
										if (SafeDouble(da["score"]) < SafeDouble(db["score"])) removed[curIdx].Add(ia);
										else removed[nbIdx].Add(ib);
									}
									continue;
								}

								if (ComputeIoS(PolyAabb(polyA), PolyAabb(polyB)) > iouTh)
								{
									var uidA = Tuple.Create(curIdx, ia);
									var uidB = Tuple.Create(nbIdx, ib);
									uf.Add(uidA);
									uf.Add(uidB);
									uf.Union(uidA, uidB);
								}
							}
						}
					}
				}

				if (!originIdxToItems.TryGetValue(group.Key, out List<JObject> outItems))
				{
					outItems = new List<JObject>();
					originIdxToItems[group.Key] = outItems;
				}

				var allUids = new List<Tuple<int, int>>();
				foreach (int idx in idxList)
				{
					for (int j = 0; j < windowDets[idx].Count; j++) allUids.Add(Tuple.Create(idx, j));
				}

				foreach (var uid in allUids)
				{
					var det = windowDets[uid.Item1][uid.Item2];
					if (!IsRotatedDet(det)) continue;
					if (removed.TryGetValue(uid.Item1, out HashSet<int> removedSet) && removedSet.Contains(uid.Item2)) continue;

					var rbox = RBoxLocalToGlobal(det, wrappers[uid.Item1], getT(wrappers[uid.Item1]));
					if (rbox == null)
					{
						var poly = getDetPoly(uid.Item1, uid.Item2, det, wrappers[uid.Item1]);
						if (poly == null) continue;
						rbox = PolyMinAreaRect(poly);
					}
					if (rbox != null) outItems.Add(mappedRotatedDet(det, rbox));
				}

				var rootToMembers = new Dictionary<Tuple<int, int>, List<Tuple<int, int>>>();
				foreach (var uid in allUids)
				{
					var det = windowDets[uid.Item1][uid.Item2];
					if (IsRotatedDet(det)) continue;
					uf.Add(uid);
					var root = uf.Find(uid);
					if (!rootToMembers.TryGetValue(root, out List<Tuple<int, int>> members))
					{
						members = new List<Tuple<int, int>>();
						rootToMembers[root] = members;
					}
					members.Add(uid);
				}

				foreach (var members in rootToMembers.Values)
				{
					double[] unionAabb = null;
					double mergedScore = 0.0;
					JToken mergedCatId = null;
					string mergedCatName = null;
					var sliceIndexUnion = new HashSet<string>(StringComparer.Ordinal);
					var sliceIndexTokens = new List<JToken>();
					JObject seedDet = null;

					foreach (var uid in members)
					{
						var det = windowDets[uid.Item1][uid.Item2];
						var poly = getDetPoly(uid.Item1, uid.Item2, det, wrappers[uid.Item1]);
						if (poly == null) continue;

						if (seedDet == null) seedDet = det;
						unionAabb = CombineAabb(unionAabb, PolyAabb(poly));
						double score = SafeDouble(det["score"]);
						if (score > mergedScore) mergedScore = score;
						if (mergedCatId == null && det["category_id"] != null) mergedCatId = det["category_id"].DeepClone();
						if (mergedCatName == null) mergedCatName = det.Value<string>("category_name");

						var meta = det["metadata"] as JObject;
						var sliceIndex = meta != null ? meta["slice_index"] : null;
						if (sliceIndex != null)
						{
							string key = sliceIndex.ToString(Newtonsoft.Json.Formatting.None);
							if (sliceIndexUnion.Add(key)) sliceIndexTokens.Add(sliceIndex.DeepClone());
						}
					}

					if (unionAabb == null || seedDet == null) continue;
					var merged = mappedAabbDet(seedDet, unionAabb, true, sliceIndexTokens);
					if (merged != null)
					{
						merged["score"] = mergedScore;
						merged["category_id"] = mergedCatId != null ? mergedCatId : JValue.CreateNull();
						merged["category_name"] = mergedCatName;
						outItems.Add(merged);
					}
				}
			}

			return BuildOutput(originIdxToImgwrap, originIdxToItems, otherResults);
		}

		private ModuleIO BuildOutput(
			SortedDictionary<int, ModuleImage> originIdxToImgwrap,
			Dictionary<int, List<JObject>> originIdxToItems,
			List<JToken> otherResults)
		{
			var outImages = new List<ModuleImage>();
			var outResults = new JArray();
			int outIdx = 0;

			foreach (var kv in originIdxToImgwrap)
			{
				if (kv.Value == null) continue;
				outImages.Add(kv.Value);

				var dets = new JArray();
				if (originIdxToItems.TryGetValue(kv.Key, out List<JObject> items))
				{
					foreach (var item in items) dets.Add(item);
				}

				outResults.Add(new JObject
				{
					["type"] = "local",
					["index"] = outIdx,
					["origin_index"] = kv.Key,
					["transform"] = null,
					["sample_results"] = dets
				});
				outIdx += 1;
			}

			foreach (var token in otherResults)
			{
				if (token != null) outResults.Add(token);
			}

			return new ModuleIO(outImages, outResults);
		}

		private double ReadDoubleProperty(string key, double defaultValue)
		{
			if (Properties != null && Properties.TryGetValue(key, out object value) && value != null)
			{
				try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); } catch { }
			}
			return defaultValue;
		}

		private bool ReadBoolProperty(string key, bool defaultValue)
		{
			if (Properties != null && Properties.TryGetValue(key, out object value) && value != null)
			{
				if (value is bool b) return b;
				if (bool.TryParse(value.ToString(), out bool parsed)) return parsed;
				if (int.TryParse(value.ToString(), out int parsedInt)) return parsedInt != 0;
			}
			return defaultValue;
		}

		private string ReadStringProperty(string key, string defaultValue)
		{
			if (Properties != null && Properties.TryGetValue(key, out object value) && value != null)
			{
				string s = value.ToString();
				if (!string.IsNullOrWhiteSpace(s)) return s;
			}
			return defaultValue;
		}

		private static ModuleImage BuildOriginWrap(ModuleImage wrap, int originIdx)
		{
			if (wrap == null) return null;
			var origin = wrap.OriginalImage;
			int w0 = origin != null && !origin.Empty() ? origin.Width : 0;
			int h0 = origin != null && !origin.Empty() ? origin.Height : 0;
			if ((w0 <= 0 || h0 <= 0) && wrap.TransformState != null)
			{
				w0 = wrap.TransformState.OriginalWidth;
				h0 = wrap.TransformState.OriginalHeight;
			}
			if (w0 <= 0 || h0 <= 0) return null;
			if (origin == null || origin.Empty())
			{
				origin = new Mat(h0, w0, MatType.CV_8UC3, Scalar.Black);
			}
			return new ModuleImage(origin, origin, new TransformationState(w0, h0), originIdx);
		}

		private static JArray CloneJArray(JArray array)
		{
			return array != null ? (JArray)array.DeepClone() : new JArray();
		}

		private static List<JObject> CloneDetList(JArray array)
		{
			var list = new List<JObject>(array != null ? array.Count : 0);
			if (array == null) return list;
			foreach (var token in array)
			{
				if (token is JObject obj) list.Add((JObject)obj.DeepClone());
			}
			return list;
		}

		private static List<JObject> CloneDetList(List<JObject> list)
		{
			var clone = new List<JObject>(list != null ? list.Count : 0);
			if (list == null) return clone;
			foreach (var item in list)
			{
				if (item != null) clone.Add((JObject)item.DeepClone());
			}
			return clone;
		}

		private static void AddSamplesRef<TKey>(Dictionary<TKey, List<JObject>> map, TKey key, JArray samples)
		{
			if (!map.TryGetValue(key, out List<JObject> list))
			{
				list = new List<JObject>();
				map[key] = list;
			}
			if (samples == null) return;
			foreach (var sample in samples)
			{
				if (sample is JObject obj) list.Add(obj);
			}
		}

		private static void AddSamplesRef<TKey>(Dictionary<TKey, List<JObject>> map, TKey key, List<JObject> samples)
		{
			if (!map.TryGetValue(key, out List<JObject> list))
			{
				list = new List<JObject>();
				map[key] = list;
			}
			if (samples == null) return;
			foreach (var sample in samples)
			{
				if (sample != null) list.Add(sample);
			}
		}

		private static void AddRange(Dictionary<int, List<JObject>> map, int originIdx, List<JObject> items)
		{
			if (!map.TryGetValue(originIdx, out List<JObject> list))
			{
				list = new List<JObject>();
				map[originIdx] = list;
			}
			if (items != null) list.AddRange(items);
		}

		private static string SerializeTransform(TransformationState st)
		{
			if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length < 6) return null;

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

			return string.Format(
				CultureInfo.InvariantCulture,
				"cb:{0},{1},{2},{3}|os:{4},{5}|ori:{6},{7}|A:{8},{9},{10},{11},{12},{13}",
				cbx, cby, cbw, cbh,
				outW, outH,
				st.OriginalWidth, st.OriginalHeight,
				Round6(st.AffineMatrix2x3[0]), Round6(st.AffineMatrix2x3[1]), Round6(st.AffineMatrix2x3[2]),
				Round6(st.AffineMatrix2x3[3]), Round6(st.AffineMatrix2x3[4]), Round6(st.AffineMatrix2x3[5]));
		}

		private static string SerializeTransform(JObject st)
		{
			if (st == null) return null;
			try
			{
				int cbx = 0, cby = 0, cbw = 0, cbh = 0;
				var crop = st["crop_box"] as JArray;
				if (crop != null && crop.Count >= 4)
				{
					cbx = crop[0].Value<int>();
					cby = crop[1].Value<int>();
					cbw = crop[2].Value<int>();
					cbh = crop[3].Value<int>();
				}

				int outW = 0, outH = 0;
				var outputSize = st["output_size"] as JArray;
				if (outputSize != null && outputSize.Count >= 2)
				{
					outW = outputSize[0].Value<int>();
					outH = outputSize[1].Value<int>();
				}

				var a = st["affine_2x3"] as JArray;
				if (a == null || a.Count < 6) return null;

				return string.Format(
					CultureInfo.InvariantCulture,
					"cb:{0},{1},{2},{3}|os:{4},{5}|ori:{6},{7}|A:{8},{9},{10},{11},{12},{13}",
					cbx, cby, cbw, cbh,
					outW, outH,
					st["original_width"]?.Value<int?>() ?? 0,
					st["original_height"]?.Value<int?>() ?? 0,
					Round6(SafeDouble(a[0])), Round6(SafeDouble(a[1])), Round6(SafeDouble(a[2])),
					Round6(SafeDouble(a[3])), Round6(SafeDouble(a[4])), Round6(SafeDouble(a[5])));
			}
			catch
			{
				return null;
			}
		}

		private static string Round6(double value)
		{
			try { return Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture); }
			catch { return "0"; }
		}

		private static JToken ReadGlobalBbox(JObject det)
		{
			var meta = det != null ? det["metadata"] as JObject : null;
			return meta != null ? meta["global_bbox"] : null;
		}

		private static bool IsRotatedDet(JObject det)
		{
			if (det == null) return false;
			bool withAngle = det["with_angle"]?.Value<bool?>() ?? false;
			double angle = SafeDouble(det["angle"]);
			if (withAngle && Math.Abs(angle - (-100.0)) > 1e-8) return true;

			var bbox = det["bbox"] as JArray;
			if (bbox != null && bbox.Count >= 5) return true;

			var meta = det["metadata"] as JObject;
			return meta != null && (meta["is_rotated"]?.Value<bool?>() ?? false);
		}

		private static double ReadAngle(JArray bbox, JObject det)
		{
			if (bbox != null && bbox.Count >= 5)
			{
				double a = SafeDouble(bbox[4]);
				return Math.Abs(a) > 3.2 ? a * Math.PI / 180.0 : a;
			}
			double angle = det != null ? SafeDouble(det["angle"]) : 0.0;
			return Math.Abs(angle) > 3.2 ? angle * Math.PI / 180.0 : angle;
		}

		private static double[] BuildTC2O(TransformationState st)
		{
			if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length != 6)
			{
				return new double[] { 1, 0, 0, 0, 1, 0 };
			}
			try { return TransformationState.Inverse2x3(st.AffineMatrix2x3); }
			catch { return new double[] { 1, 0, 0, 0, 1, 0 }; }
		}

		private static Point2f[] TransformPoints(double[] t, Point2f[] pts)
		{
			if (t == null || t.Length != 6 || pts == null) return null;
			var res = new Point2f[pts.Length];
			for (int i = 0; i < pts.Length; i++)
			{
				double x = pts[i].X;
				double y = pts[i].Y;
				double nx = t[0] * x + t[1] * y + t[2];
				double ny = t[3] * x + t[4] * y + t[5];
				res[i] = new Point2f((float)nx, (float)ny);
			}
			return res;
		}

		private static double[] PolyAabb(Point2f[] poly)
		{
			if (poly == null || poly.Length == 0) return null;
			double minX = double.MaxValue, minY = double.MaxValue;
			double maxX = double.MinValue, maxY = double.MinValue;
			foreach (var p in poly)
			{
				if (p.X < minX) minX = p.X;
				if (p.X > maxX) maxX = p.X;
				if (p.Y < minY) minY = p.Y;
				if (p.Y > maxY) maxY = p.Y;
			}
			return new[] { minX, minY, maxX, maxY };
		}

		private static double[] CombineAabb(double[] a, double[] b)
		{
			if (a == null) return b != null ? (double[])b.Clone() : null;
			if (b == null) return (double[])a.Clone();
			return new[]
			{
				Math.Min(a[0], b[0]),
				Math.Min(a[1], b[1]),
				Math.Max(a[2], b[2]),
				Math.Max(a[3], b[3])
			};
		}

		private static double IntersectionArea(double[] a, double[] b)
		{
			if (a == null || b == null) return 0.0;
			double x1 = Math.Max(a[0], b[0]);
			double y1 = Math.Max(a[1], b[1]);
			double x2 = Math.Min(a[2], b[2]);
			double y2 = Math.Min(a[3], b[3]);
			return Math.Max(0.0, x2 - x1) * Math.Max(0.0, y2 - y1);
		}

		private static double ComputeIoU(double[] a, double[] b)
		{
			double inter = IntersectionArea(a, b);
			if (inter <= 0.0) return 0.0;
			double areaA = Math.Max(0.0, a[2] - a[0]) * Math.Max(0.0, a[3] - a[1]);
			double areaB = Math.Max(0.0, b[2] - b[0]) * Math.Max(0.0, b[3] - b[1]);
			double union = areaA + areaB - inter;
			return union > 0.0 ? inter / union : 0.0;
		}

		private static double ComputeIoS(double[] a, double[] b)
		{
			double inter = IntersectionArea(a, b);
			if (inter <= 0.0) return 0.0;
			double areaA = Math.Max(0.0, a[2] - a[0]) * Math.Max(0.0, a[3] - a[1]);
			double areaB = Math.Max(0.0, b[2] - b[0]) * Math.Max(0.0, b[3] - b[1]);
			double smaller = Math.Min(areaA, areaB);
			return smaller > 0.0 ? inter / smaller : 0.0;
		}

		private static bool TryGetGrid(JObject slidingMeta, out int gx, out int gy)
		{
			gx = -1;
			gy = -1;
			if (slidingMeta == null) return false;

			var sliceIndex = slidingMeta["slice_index"] as JArray;
			if (sliceIndex != null)
			{
				if (sliceIndex.Count >= 2 && !(sliceIndex[0] is JArray))
				{
					gy = SafeInt(sliceIndex[0], -1);
					gx = SafeInt(sliceIndex[1], -1);
				}
				else if (sliceIndex.Count > 0 && sliceIndex[0] is JArray nested && nested.Count >= 2)
				{
					gy = SafeInt(nested[0], -1);
					gx = SafeInt(nested[1], -1);
				}
			}

			if (gx < 0 || gy < 0)
			{
				gx = SafeInt(slidingMeta["grid_x"], -1);
				gy = SafeInt(slidingMeta["grid_y"], -1);
			}
			return gx >= 0 && gy >= 0;
		}

		private static bool SameCategory(JObject a, JObject b)
		{
			if (a == null || b == null) return false;
			if (a["category_id"] != null && b["category_id"] != null)
			{
				try { return a["category_id"].Value<int>() == b["category_id"].Value<int>(); } catch { }
			}
			string ca = (a.Value<string>("category_name") ?? string.Empty).Trim().ToLowerInvariant();
			string cb = (b.Value<string>("category_name") ?? string.Empty).Trim().ToLowerInvariant();
			if (ca.Length > 0 || cb.Length > 0) return ca == cb;
			return true;
		}

		private static double[] RBoxLocalToGlobal(JObject det, ModuleImage wrap, double[] t = null)
		{
			if (det == null || wrap == null) return null;
			var bbox = det["bbox"] as JArray;
			if (bbox == null || bbox.Count < 4) return null;

			double cx = SafeDouble(bbox[0]);
			double cy = SafeDouble(bbox[1]);
			double w = Math.Abs(SafeDouble(bbox[2]));
			double h = Math.Abs(SafeDouble(bbox[3]));
			if (w <= 0.0 || h <= 0.0) return null;

			double ang = ReadAngle(bbox, det);
			if (t == null) t = BuildTC2O(wrap.TransformState);
			double ncx = t[0] * cx + t[1] * cy + t[2];
			double ncy = t[3] * cx + t[4] * cy + t[5];

			double ux = Math.Cos(ang);
			double uy = Math.Sin(ang);
			double vx = -Math.Sin(ang);
			double vy = Math.Cos(ang);
			double tuxX = t[0] * ux + t[1] * uy;
			double tuxY = t[3] * ux + t[4] * uy;
			double tvxX = t[0] * vx + t[1] * vy;
			double tvxY = t[3] * vx + t[4] * vy;
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

		private static double[] PolyMinAreaRect(Point2f[] poly)
		{
			if (poly == null || poly.Length == 0) return null;
			var rr = Cv2.MinAreaRect(poly);
			return new[]
			{
				(double)rr.Center.X,
				(double)rr.Center.Y,
				Math.Max(1.0, rr.Size.Width),
				Math.Max(1.0, rr.Size.Height),
				(double)(rr.Angle * Math.PI / 180.0)
			};
		}

		private static int SafeInt(JToken token, int defaultValue)
		{
			if (token == null || token.Type == JTokenType.Null) return defaultValue;
			try { return token.Value<int>(); } catch { return defaultValue; }
		}

		private static double SafeDouble(JToken token)
		{
			if (token == null || token.Type == JTokenType.Null) return 0.0;
			try { return Convert.ToDouble(((JValue)token).Value, CultureInfo.InvariantCulture); } catch { return 0.0; }
		}
	}
}
