using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace DlcvModules
{
	/// <summary>
	/// 将 local 结果中的按类别(N/P)聚合的 mask（点集多边形）转换为等间距点集（局部坐标），并以点为中心生成固定大小的矩形框（XYWH）。
	/// 注册名：features/stroke_to_points
	/// properties:
	/// - counts_dict(dict, category_name -> count)
	/// - point_width(int, default 10)
	/// - point_height(int, default 10)
	/// 输入：image_list
	/// 输出：result_list 中仅保留转换后的对应条目（index/origin_index/transform 透传；sample_results 为点框集合）
	/// </summary>
	public class StrokeToPoints : BaseModule
	{
		static StrokeToPoints()
		{
			ModuleRegistry.Register("features/stroke_to_points", typeof(StrokeToPoints));
		}

		public StrokeToPoints(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context)
		{
		}

		public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
			var results = resultList ?? new JArray();

			var counts = ReadCountsDict();
			int pointW = Math.Max(1, ReadInt("point_width", 10));
			int pointH = Math.Max(1, ReadInt("point_height", 10));

			var outResults = new JArray();
			if (counts == null || counts.Count == 0) return new ModuleIO(images, outResults);

			foreach (var t in results)
			{
				var entry = t as JObject;
				if (entry == null) continue;
				string et = entry["type"]?.ToString();
				if (!string.Equals(et, "local", StringComparison.OrdinalIgnoreCase)) continue;

				// 尺寸来源：transform.output_size 作为局部坐标系尺寸
				var stObj = entry["transform"] as JObject;
				if (stObj == null) continue;
				// output_size 结构为 { "original_width": 360, "original_height": 800 }
				if (stObj == null) continue;
				int W = ClampToInt(stObj["original_width"] != null ? stObj["original_width"].Value<double>() : 0);
				int H = ClampToInt(stObj["original_height"] != null ? stObj["original_height"].Value<double>() : 0);
				if (W <= 0 || H <= 0) continue;

				var maskByCat = new Dictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);
				var srs = entry["sample_results"] as JArray;
				if (srs != null && srs.Count > 0)
				{
					for (int i = 0; i < srs.Count; i++)
					{
						var s = srs[i] as JObject;
						if (s == null) continue;
						string cat = s["category_name"]?.ToString();
						if (string.IsNullOrEmpty(cat)) continue;
						if (!counts.ContainsKey(cat)) continue;

						// 使用无损 RLE 掩膜（mask_rle），先按 bbox 尺寸缩放到 bbox 坐标系，再贴到整图掩膜中
						var maskInfoTok = s["mask_rle"];
						if (maskInfoTok != null)
						{
							try
							{
								using (var localMask = MaskRleUtils.MaskInfoToMat(maskInfoTok))
								{
									if (localMask != null && !localMask.Empty())
									{
										// 默认使用 RLE 自身的尺寸；若提供 bbox，则根据 bbox 推导 ROI 尺寸并进行缩放
										int x0 = 0;
										int y0 = 0;
										int roiW = localMask.Cols;
										int roiH = localMask.Rows;

										var bboxArr = s["bbox"] as JArray;
										if (bboxArr != null && bboxArr.Count >= 4)
										{
											try
											{
												if (bboxArr.Count == 4)
												{
													// 标准 bbox: [x, y, w, h]，左上角与宽高
													x0 = ClampToInt(bboxArr[0].Value<double>());
													y0 = ClampToInt(bboxArr[1].Value<double>());
													roiW = Math.Max(0, ClampToInt(Math.Abs(bboxArr[2].Value<double>())));
													roiH = Math.Max(0, ClampToInt(Math.Abs(bboxArr[3].Value<double>())));
												}
												else if (bboxArr.Count >= 5)
												{
													// 旋转框格式: [cx, cy, w, h, angle](如有angle忽略), 转成xywh
													double cx = bboxArr[0].Value<double>();
													double cy = bboxArr[1].Value<double>();
													double w = Math.Abs(bboxArr[2].Value<double>());
													double h = Math.Abs(bboxArr[3].Value<double>());
													// 忽略angle
													x0 = ClampToInt(cx - w / 2.0);
													y0 = ClampToInt(cy - h / 2.0);
													roiW = Math.Max(0, ClampToInt(w));
													roiH = Math.Max(0, ClampToInt(h));
												}
											}
											catch
											{
												x0 = 0;
												y0 = 0;
												roiW = localMask.Cols;
												roiH = localMask.Rows;
											}
										}

										if (roiW <= 0 || roiH <= 0)
										{
											continue;
										}

										// 如 mask 尺寸与 bbox 尺寸不一致，则调整到 bbox 尺度（例如 mask=32, bbox 高=512）
										using (var resized = new Mat())
										{
											Mat patch = localMask;
											if (patch.Cols != roiW || patch.Rows != roiH)
											{
												Cv2.Resize(patch, resized, new Size(roiW, roiH), 0, 0, InterpolationFlags.Nearest);
												patch = resized;
											}

											int pw = patch.Cols;
											int ph = patch.Rows;

											// 计算与整图的交集区域（全局坐标），并相应裁剪 patch（局部坐标）
											int ix0 = Math.Max(0, x0);
											int iy0 = Math.Max(0, y0);
											int ix1 = Math.Min(W, x0 + pw);
											int iy1 = Math.Min(H, y0 + ph);
											int rw = ix1 - ix0;
											int rh = iy1 - iy0;
											if (rw > 0 && rh > 0)
											{
												int sx0 = ix0 - x0;
												int sy0 = iy0 - y0;
												Mat dst;
												if (!maskByCat.TryGetValue(cat, out dst) || dst == null)
												{
													dst = new Mat(H, W, MatType.CV_8UC1, Scalar.Black);
													maskByCat[cat] = dst;
												}
												using (var roiDst = dst.SubMat(new Rect(ix0, iy0, rw, rh)))
												using (var roiSrc = patch.SubMat(new Rect(sx0, sy0, rw, rh)))
												{
													Cv2.BitwiseOr(roiDst, roiSrc, roiDst);
												}
											}
										}
									}
								}
							}
							catch
							{
								// 单条 mask 故障时忽略该条，避免影响其它结果
							}
						}
					}
				}

				// 生成点 -> 生成 XYWH 框
				var pointsItems = new JArray();
				foreach (var kv in counts)
				{
					Mat m;
					if (!maskByCat.TryGetValue(kv.Key, out m) || m == null || m.Empty()) continue;
					var pts = GeneratePointsFromMask(m, kv.Value);
					AddPointBoxes(pointsItems, pts, kv.Key, pointW, pointH, W, H);
				}

				if (pointsItems.Count == 0) continue;

				int idx = entry["index"]?.Value<int?>() ?? 0;
				int originIdx = entry["origin_index"]?.Value<int?>() ?? idx;
				var outEntry = new JObject
				{
					["type"] = "local",
					["originating_module"] = "features/stroke_to_points",
					["index"] = idx,
					["origin_index"] = originIdx,
					["transform"] = stObj != null ? (JToken)stObj.DeepClone() : null,
					["sample_results"] = pointsItems
				};
				outResults.Add(outEntry);
			}

			return new ModuleIO(images, outResults);
		}


		private static List<Tuple<int, int>> GeneratePointsFromMask(Mat mask, int countK)
		{
			var points = new List<Tuple<int, int>>();
			if (mask == null || mask.Empty() || countK <= 0) return points;
			int H = mask.Rows;
			int W = mask.Cols;

			// 使用 OpenCV 快速提取非零像素坐标（避免显式双重循环）
			var nz = new Mat();
			Cv2.FindNonZero(mask, nz);
			if (nz.Empty()) return points;
			if (!nz.IsContinuous()) nz = nz.Clone();
			int nPts = nz.Rows;
			var raw = new int[nPts * 2]; // CV_32SC2: x,y 交错
			Marshal.Copy(nz.Data, raw, 0, raw.Length);

			// 单次遍历：同时计算 y 范围与每行聚合
			int yTop = int.MaxValue;
			int yBottom = int.MinValue;
			var sumXArr = new double[H];
			var cntArr = new int[H];
			for (int i = 0; i < nPts; i++)
			{
				int baseIndex = i * 2; // CV_32SC2: [x, y]
				int xx = raw[baseIndex];
				int yy = raw[baseIndex + 1];
				if (yy < yTop) yTop = yy;
				if (yy > yBottom) yBottom = yy;
				cntArr[yy] += 1;
				sumXArr[yy] += xx;
			}
			if (yTop == int.MaxValue || yBottom == int.MinValue) return points;

			var yList = LinspaceInt(yTop, yBottom, countK);
			// 计算每行均值并收集有效行用于插值与兜底
			var xMeanByY = new Dictionary<int, double>();
			var means = new List<double>();
			for (int yy = yTop; yy <= yBottom; yy++)
			{
				int c = cntArr[yy];
				if (c > 0)
				{
					double m = sumXArr[yy] / c;
					xMeanByY[yy] = m;
					means.Add(m);
				}

			}
			var rowsSorted = xMeanByY.Keys.OrderBy(v => v).ToArray();
			double fallbackX = means.Count > 0 ? Median(means) : (W / 2.0);

			for (int i = 0; i < yList.Count; i++)
			{
				int yy = Math.Max(0, Math.Min(H - 1, yList[i]));
				double x = InterpolateXForRow(yy, rowsSorted, xMeanByY, fallbackX);
				int xx = ClampToInt(x);
				xx = Math.Max(0, Math.Min(W - 1, xx));
				points.Add(Tuple.Create(xx, yy));
			}
			return points;
		}

		private static List<int> LinspaceInt(int y0, int y1, int n)
		{
			var list = new List<int>();
			if (n <= 0) return list;
			if (n == 1)
			{
				list.Add(ClampToInt(0.5 * (y0 + y1)));
				return list;
			}
			double a = y0;
			double b = y1;
			double step = (b - a) / (n - 1);
			for (int i = 0; i < n; i++)
			{
				double v = a + i * step;
				list.Add(ClampToInt(v));
			}
			return list;
		}

		private static double InterpolateXForRow(int targetY, int[] rowsSorted, Dictionary<int, double> xMeanByRow, double fallbackX)
		{
			if (xMeanByRow.ContainsKey(targetY)) return xMeanByRow[targetY];
			if (rowsSorted == null || rowsSorted.Length == 0) return fallbackX;
			// 找到插入位置
			int pos = Array.BinarySearch(rowsSorted, targetY);
			if (pos >= 0) return xMeanByRow[rowsSorted[pos]];
			int ip = ~pos;
			int yHi = (ip < rowsSorted.Length) ? rowsSorted[ip] : int.MaxValue;
			int yLo = (ip - 1 >= 0) ? rowsSorted[ip - 1] : int.MinValue;
			if (yLo == int.MinValue && yHi == int.MaxValue) return fallbackX;
			if (yLo == int.MinValue) return xMeanByRow[yHi];
			if (yHi == int.MaxValue) return xMeanByRow[yLo];
			double xLo = xMeanByRow[yLo];
			double xHi = xMeanByRow[yHi];
			if (yHi == yLo) return xLo;
			double t = (targetY - yLo) / Math.Max(1.0, (double)(yHi - yLo));
			return xLo + t * (xHi - xLo);
		}

		private static void AddPointBoxes(JArray outArr, List<Tuple<int, int>> pts, string category, int pw, int ph, int W, int H)
		{
			double halfW = pw / 2.0;
			double halfH = ph / 2.0;
			for (int i = 0; i < pts.Count; i++)
			{
				int x = pts[i].Item1;
				int y = pts[i].Item2;
				int x1 = (int)Math.Floor(x - halfW);
				int y1 = (int)Math.Floor(y - halfH);
				int x2 = (int)Math.Ceiling(x + halfW);
				int y2 = (int)Math.Ceiling(y + halfH);
				// 裁剪边界并确保非零尺寸（XYWH 格式，宽高以像素数计）
				x1 = Math.Max(0, Math.Min(W - 1, x1));
				y1 = Math.Max(0, Math.Min(H - 1, y1));
				x2 = Math.Max(0, Math.Min(W - 1, x2));
				y2 = Math.Max(0, Math.Min(H - 1, y2));
				if (x2 <= x1) x2 = Math.Min(W - 1, x1 + 1);
				if (y2 <= y1) y2 = Math.Min(H - 1, y1 + 1);

				var obj = new JObject
				{
					["category_name"] = category,
					["score"] = 1.0,
					["bbox"] = new JArray(x1, y1, x2 - x1, y2 - y1)
				};
				outArr.Add(obj);
			}
		}

		private Dictionary<string, int> ReadCountsDict()
		{
			var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			if (Properties != null && Properties.TryGetValue("counts_dict", out object v) && v != null)
			{
				var jobj = v as JObject;
				if (jobj != null)
				{
					foreach (var p in jobj)
					{
						if (TryConvertToInt(p.Value, out int c)) dict[p.Key] = c;
					}
				}
				else
				{
					var dso = v as Dictionary<string, object>;
					if (dso != null)
					{
						foreach (var kv in dso)
						{
							if (TryConvertToInt(kv.Value, out int c)) dict[kv.Key] = c;
						}
					}
					else
					{
						var idict = v as System.Collections.IDictionary;
						if (idict != null)
						{
							foreach (var keyObj in idict.Keys)
							{
								string key = keyObj != null ? keyObj.ToString() : null;
								if (string.IsNullOrEmpty(key)) continue;
								if (TryConvertToInt(idict[keyObj], out int c)) dict[key] = c;
							}
						}
					}
				}
			}

			if (dict.Count == 0) { dict["N"] = ReadInt("count_N", 1); dict["P"] = ReadInt("count_P", 1); }
			return dict;
		}

		private static bool TryConvertToInt(object v, out int value)
		{
			try
			{
				if (v == null)
				{
					value = 0;
					return false;
				}
				value = Convert.ToInt32(Convert.ToDouble(v));
				return true;
			}
			catch
			{
				value = 0;
				return false;
			}
		}

		private int ReadInt(string key, int dv)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				try { return Convert.ToInt32(Convert.ToDouble(v)); } catch { return dv; }
			}
			return dv;
		}

		private static int ClampToInt(double v)
		{
			if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
			if (v > int.MaxValue) return int.MaxValue;
			if (v < int.MinValue) return int.MinValue;
			return (int)Math.Round(v);
		}

		private static double Median(IEnumerable<double> values)
		{
			var arr = values != null ? values.ToList() : new List<double>();
			if (arr.Count == 0) return 0.0;
			arr.Sort();
			int mid = arr.Count / 2;
			if ((arr.Count % 2) == 1) return arr[mid];
			return 0.5 * (arr[mid - 1] + arr[mid]);
		}

	}
}



