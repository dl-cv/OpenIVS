using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
	/// <summary>
	/// 轻量模版与匹配数据模型（OpenIVS 内部实现，避免引用 PrintMatch 工程）。
	/// </summary>
	[JsonObject]
	public class SimpleOcrItem
	{
		[JsonProperty("text")] public string Text { get; set; }
		[JsonProperty("confidence")] public float Confidence { get; set; }
		[JsonProperty("x")] public int X { get; set; }
		[JsonProperty("y")] public int Y { get; set; }
		[JsonProperty("width")] public int Width { get; set; }
		[JsonProperty("height")] public int Height { get; set; }
		[JsonProperty("recognition_time")] public DateTime RecognitionTime { get; set; } = DateTime.Now;
		[JsonProperty("match_status")] public int MatchStatus { get; set; } = 0;
	}

	public class SimpleTemplate
	{
		[JsonProperty("template_id")] public string TemplateId { get; set; }
		[JsonProperty("template_name")] public string TemplateName { get; set; }
		[JsonProperty("product_id")] public string ProductId { get; set; }
		[JsonProperty("product_name")] public string ProductName { get; set; }
		[JsonProperty("OCRResults")] public List<SimpleOcrItem> OCRResults { get; set; } = new List<SimpleOcrItem>();
		[JsonProperty("camera_position")] public int CameraPosition { get; set; } = 0;
		[JsonProperty("expected_x")] public int ExpectedX { get; set; } = 0;
		[JsonProperty("expected_y")] public int ExpectedY { get; set; } = 0;
		[JsonProperty("expected_width")] public int ExpectedWidth { get; set; } = 0;
		[JsonProperty("expected_height")] public int ExpectedHeight { get; set; } = 0;
		[JsonProperty("expected_text")] public string ExpectedText { get; set; } = string.Empty;
		[JsonProperty("position_tolerance")] public int PositionTolerance { get; set; } = 10;
		[JsonProperty("size_tolerance")] public int SizeTolerance { get; set; } = 5;
		[JsonProperty("min_confidence")] public double MinConfidence { get; set; } = 0.8;
		[JsonProperty("strict_text_match")] public bool StrictTextMatch { get; set; } = true;
		[JsonProperty("created_time")] public DateTime CreatedTime { get; set; } = DateTime.Now;
		[JsonProperty("updated_time")] public DateTime UpdatedTime { get; set; } = DateTime.Now;
		[JsonProperty("is_enabled")] public bool IsEnabled { get; set; } = true;
		[JsonProperty("image_path")] public string ImagePath { get; set; }
	}

	public class SimpleTemplateMatchDetail
	{
		public bool IsMatch { get; set; }
		public double Score { get; set; }
		public List<string> Reasons { get; set; } = new List<string>();
		public List<JObject> CorrectItems { get; set; } = new List<JObject>();
		public List<JObject> DeviationItems { get; set; } = new List<JObject>();
		public List<JObject> OverDetectionItems { get; set; } = new List<JObject>();
		public List<JObject> MissedTemplateItems { get; set; } = new List<JObject>();
		public List<JObject> MisjudgmentPairs { get; set; } = new List<JObject>();
	}

	internal static class SimpleTemplateUtils
	{
		public static string NormalizeText(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return string.Empty;
			string s = text.Trim();
			s = s.Replace(" ", "");
			s = s.Replace('l', 'I').Replace('O', '0').Replace('o', '0').Replace('1', 'I');
			return s.ToUpperInvariant();
		}

		public static double Distance(int x1, int y1, int x2, int y2)
		{
			int dx = x1 - x2; int dy = y1 - y2; return Math.Sqrt(dx * dx + dy * dy);
		}

		public static string MakeSafeFileName(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) return "Unknown";
			string s = name.Trim();
			foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
			s = s.Replace(' ', '_');
			return string.IsNullOrWhiteSpace(s) ? "Unknown" : s;
		}
	}

	/// <summary>
	/// 1) 转化模版：从 local OCR 结果构建 SimpleTemplate
	/// type: features/template_from_results
	/// properties: template_name(string), product_id(string), product_name(string)
	/// 输出：result_list 仅包含一个 { "type":"template", "template": {...} }
	/// </summary>
	public class TemplateFromResults : BaseModule
	{
		static TemplateFromResults()
		{
			ModuleRegistry.Register("features/template_from_results", typeof(TemplateFromResults));
		}

		public TemplateFromResults(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }

		public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var results = resultList ?? new JArray();
			string productName = ReadString("product_name", "");
			string productId = ReadString("product_id", null);
			string templateName = ReadString("template_name", productName);

			var tpl = new SimpleTemplate
			{
				ProductName = productName,
				ProductId = productId,
				TemplateName = templateName
			};

			// 优先从上下文读取条码，覆盖 product_id（旧版要求以条码作为 product_id）
			try
			{
				string barcode = this.Context != null ? this.Context.Get<string>("barcode_text", null) : null;
				if (!string.IsNullOrWhiteSpace(barcode))
				{
					tpl.ProductId = barcode;
				}
				else if (string.IsNullOrWhiteSpace(tpl.ProductId))
				{
					tpl.ProductId = string.Empty;
				}
			}
			catch { }

			// 写入相机位置（A/B/C/D -> 0/1/2/3），默认 0
			try
			{
				string face = this.Context != null ? this.Context.Get<string>("face", null) : null;
				if (!string.IsNullOrWhiteSpace(face))
				{
					char c = char.ToUpperInvariant(face[0]);
					switch (c)
					{
						case 'A': tpl.CameraPosition = 0; break;
						case 'B': tpl.CameraPosition = 1; break;
						case 'C': tpl.CameraPosition = 2; break;
						case 'D': tpl.CameraPosition = 3; break;
						default: tpl.CameraPosition = 0; break;
					}
				}
			}
			catch { }

			// 收集所有 local entries 的 OCR，转换为原图坐标并去重
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var token in results)
			{
				var entry = token as JObject; if (entry == null) continue;
				string et = entry["type"]?.ToString();
				if (!string.Equals(et, "local", StringComparison.OrdinalIgnoreCase)) continue;
				var srs = entry["sample_results"] as JArray; if (srs == null) continue;
				var stDict = entry["transform"] as JObject;
				TransformationState st = null;
				try { if (stDict != null) st = TransformationState.FromDict(stDict.ToObject<Dictionary<string, object>>()); } catch { st = null; }
				double[] inv = null;
				try { if (st != null && st.AffineMatrix2x3 != null) inv = TransformationState.Inverse2x3(st.AffineMatrix2x3); } catch { inv = null; }

				foreach (var s in srs)
				{
					var so = s as JObject; if (so == null) continue;
					var bbox = so["bbox"] as JArray; if (bbox == null || bbox.Count < 4) continue;
					bool withAngle = so["with_angle"]?.Value<bool>() ?? false;
					double angle = so["angle"]?.Value<double?>() ?? -100.0; // 弧度

					// 构造当前坐标系的四角点
					double[] xs, ys;
					if (withAngle && angle != -100.0)
					{
						double cx = bbox[0].Value<double>();
						double cy = bbox[1].Value<double>();
						double ww = Math.Max(1.0, bbox[2].Value<double>());
						double hh = Math.Max(1.0, bbox[3].Value<double>());
						double hw = ww / 2.0, hh2 = hh / 2.0;
						double c = Math.Cos(angle), sgn = Math.Sin(angle);
						double[] lx = new double[] { -hw, hw, hw, -hw };
						double[] ly = new double[] { -hh2, -hh2, hh2, hh2 };
						xs = new double[4]; ys = new double[4];
						for (int i = 0; i < 4; i++)
						{
							double rx = c * lx[i] - sgn * ly[i];
							double ry = sgn * lx[i] + c * ly[i];
							xs[i] = cx + rx;
							ys[i] = cy + ry;
						}
					}
					else
					{
						double x0 = bbox[0].Value<double>();
						double y0 = bbox[1].Value<double>();
						double w0 = Math.Max(1.0, bbox[2].Value<double>());
						double h0 = Math.Max(1.0, bbox[3].Value<double>());
						xs = new double[] { x0, x0 + w0, x0 + w0, x0 };
						ys = new double[] { y0, y0, y0 + h0, y0 + h0 };
					}

					// 变换到原图坐标系
					double minx = double.PositiveInfinity, miny = double.PositiveInfinity, maxx = double.NegativeInfinity, maxy = double.NegativeInfinity;
					for (int i = 0; i < 4; i++)
					{
						double gx = xs[i], gy = ys[i];
						if (inv != null)
						{
							double ox = inv[0] * gx + inv[1] * gy + inv[2];
							double oy = inv[3] * gx + inv[4] * gy + inv[5];
							gx = ox; gy = oy;
						}
						if (gx < minx) minx = gx; if (gy < miny) miny = gy; if (gx > maxx) maxx = gx; if (gy > maxy) maxy = gy;
					}
					int ix = ClampToInt(minx);
					int iy = ClampToInt(miny);
					int iw = Math.Max(1, ClampToInt(maxx - minx));
					int ih = Math.Max(1, ClampToInt(maxy - miny));

					string text =
						(so["category_name"] != null ? so["category_name"].ToString() : null) ??
						(so["text"] != null ? so["text"].ToString() : null) ??
						(so["recognized_text"] != null ? so["recognized_text"].ToString() : null) ??
						(so["name"] != null ? so["name"].ToString() : null) ??
						string.Empty;
					text = text != null ? text.Trim() : string.Empty;
					float conf = SafeToFloat(so["score"]);
					string key = SimpleTemplateUtils.NormalizeText(text) + "|" + ix + "," + iy + "," + iw + "," + ih;
						if (seen.Add(key))
						{
							tpl.OCRResults.Add(new SimpleOcrItem { Text = text, Confidence = conf, X = ix, Y = iy, Width = iw, Height = ih, RecognitionTime = DateTime.Now, MatchStatus = 0 });
						}
				}
			}

			if (string.IsNullOrWhiteSpace(tpl.TemplateId))
			{
				string baseName = !string.IsNullOrWhiteSpace(tpl.TemplateName) ? tpl.TemplateName : 
								  !string.IsNullOrWhiteSpace(tpl.ProductName) ? tpl.ProductName : "Template";
				tpl.TemplateId = SimpleTemplateUtils.MakeSafeFileName(baseName);
			}

			var outTemplates = new List<SimpleTemplate> { tpl };
			return new ModuleIO(imageList ?? new List<ModuleImage>(), results, outTemplates);
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

		private static int ClampToInt(double v)
		{
			if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
			if (v > int.MaxValue) return int.MaxValue;
			if (v < int.MinValue) return int.MinValue;
			return (int)Math.Round(v);
		}

		private static float SafeToFloat(JToken t)
		{
			try { return t != null ? Convert.ToSingle(((JValue)t).Value) : 0f; } catch { return 0f; }
		}
	}

	/// <summary>
	/// 2) 存储模版：将 SimpleTemplate 写入 JSON 文件；如有首张图像则保存 PNG 同名文件。
	/// type: features/template_save
	/// properties: file_name(string 可选，默认 TemplateId)
	/// 输入：result_list 中需包含 {type: "template"}
	/// 输出：透传 + 在 result_list 末尾附加 {type:"template_saved", path: full_json_path}
	/// </summary>
	public class TemplateSave : BaseModule
	{
		static TemplateSave()
		{
			ModuleRegistry.Register("features/template_save", typeof(TemplateSave));
		}

		public TemplateSave(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }

		public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
			var results = resultList != null ? new JArray(resultList) : new JArray();
			string dir = this.Context != null ? this.Context.Get<string>("templates_dir", null) : null;
			string effDir = dir;
			try
			{
				if (string.IsNullOrWhiteSpace(effDir))
				{
					effDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "模版");
				}
				else if (!Path.IsPathRooted(effDir))
				{
					effDir = Path.GetFullPath(effDir);
				}
			}
			catch { }
			try { if (!string.IsNullOrWhiteSpace(effDir)) Directory.CreateDirectory(effDir); } catch { }

			// 从模版主通道读取
			SimpleTemplate tpl = null;
			if (this.MainTemplateList != null && this.MainTemplateList.Count > 0)
			{
				tpl = this.MainTemplateList[0];
			}
			if (tpl == null)
			{
				return new ModuleIO(images, results);
			}
			string fileName = ReadString("file_name", null);
			if (string.IsNullOrWhiteSpace(fileName)) fileName = tpl?.TemplateId ?? "Template";
			fileName = SimpleTemplateUtils.MakeSafeFileName(fileName);
			string jsonPath = Path.Combine(effDir ?? string.Empty, fileName + ".json");

			// 如有首张原图，另存 PNG
			if (images.Count > 0 && images[0] != null && images[0].ImageObject != null && !images[0].ImageObject.Empty())
			{
				string pngPath = Path.Combine(effDir ?? string.Empty, fileName + ".png");
				try
				{
					Cv2.ImWrite(pngPath, images[0].ImageObject);
					tpl.ImagePath = Path.GetFileName(pngPath);
				}
				catch { }
			}

			// 落盘前过滤：临时替换 OCR 列表，写入后恢复，避免影响内存与后续判定
			var original = tpl.OCRResults;
			try
			{
				if (original != null)
				{
					var filtered = original.Where(x => x == null || ((x.Text ?? string.Empty).IndexOf("NG", StringComparison.OrdinalIgnoreCase) < 0)).ToList();
					tpl.OCRResults = filtered;
				}
			}
			catch { }
			File.WriteAllText(jsonPath, JsonConvert.SerializeObject(tpl, Formatting.Indented));
			try { tpl.OCRResults = original; } catch { }

			// 无输出：返回空主通道
			return new ModuleIO(new List<ModuleImage>(), new JArray(), new List<SimpleTemplate>());
		}

		private static JToken FindTemplateToken(JArray results)
		{
			foreach (var t in results)
			{
				var o = t as JObject; if (o == null) continue;
				if (string.Equals(o["type"]?.ToString(), "template", StringComparison.OrdinalIgnoreCase))
				{
					return o["template"];
				}
			}
			return null;
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
	}

	/// <summary>
	/// 3) 读取模版：从路径加载 SimpleTemplate
	/// type: features/template_load
	/// properties: path(string)
	/// 输出：result_list 追加 {type:"template", template:{...}}
	/// </summary>
		public class TemplateLoad : BaseModule
	{
		static TemplateLoad()
		{
			ModuleRegistry.Register("features/template_load", typeof(TemplateLoad));
		}

		public TemplateLoad(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }

		public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
			var results = resultList != null ? new JArray(resultList) : new JArray();
			string path = ReadString("path", null);
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				return new ModuleIO(images, results);
			}
			try
			{
				var json = File.ReadAllText(path);
				var tpl = JsonConvert.DeserializeObject<SimpleTemplate>(json);
				var tlist = new List<SimpleTemplate> { tpl };
				return new ModuleIO(images, results, tlist);
			}
			catch { }
			return new ModuleIO(images, results);
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
	}

	/// <summary>
	/// 4) 模版匹配：输入需要匹配的模版 与 正确模版，输出 bool 与匹配详情。
	/// type: features/template_match
	/// 输入：
	/// - 主对 result_list：待匹配图像的 local OCR 结果
	/// - 额外输入对0（ExtraInputsIn[0]）的 result_list：包含 {type:"template"} 的正确模版
	/// 输出：
	/// - result_list 末尾追加 {type:"template_match", ok:bool, score:double, detail:{...}}
	/// </summary>
	public class TemplateMatch : BaseModule
	{
		static TemplateMatch()
		{
			ModuleRegistry.Register("features/template_match", typeof(TemplateMatch));
		}

		public TemplateMatch(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }

			public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
			{
				var images = imageList ?? new List<ModuleImage>();
				var results = resultList != null ? new JArray(resultList) : new JArray();
				
				// 主通道：待匹配模版；额外输入第 0 对：黄金模版
				SimpleTemplate toCheck = null;
				if (this.MainTemplateList != null && this.MainTemplateList.Count > 0) toCheck = this.MainTemplateList[0];
				SimpleTemplate golden = null;
				if (ExtraInputsIn != null && ExtraInputsIn.Count > 0 && ExtraInputsIn[0] != null)
				{
					var lst = ExtraInputsIn[0].TemplateList;
					if (lst != null && lst.Count > 0) golden = lst[0];
				}
				
				if (toCheck == null || golden == null)
				{
					return new ModuleIO(images, results);
				}
				
				// 读取容差与阈值
				double posTolX = ReadDoubleOr("position_tolerance_x", 20.0);
				double posTolY = ReadDoubleOr("position_tolerance_y", 20.0);
				double minConf = ReadDoubleOr("min_confidence_threshold", 0.5);
				bool checkPosition = ReadBoolOr("check_position", true);
				
				// 使用与 PrintMatch 一致的匹配逻辑与输出结构
				var pmDetail = MatchAsPrintMatch(golden, toCheck.OCRResults ?? new List<SimpleOcrItem>(), posTolX, posTolY, minConf, checkPosition);
				bool ok = pmDetail?["template_match_info"] != null && pmDetail["template_match_info"]["is_match"] != null && pmDetail["template_match_info"]["is_match"].Value<bool>();
				try
				{
					this.ScalarOutputsByName["ok"] = ok;
					this.ScalarOutputsByName["detail"] = pmDetail != null ? pmDetail.ToString(Formatting.None) : "{}";
				}
				catch { }
				return new ModuleIO(new List<ModuleImage>(), new JArray(), new List<SimpleTemplate>());
			}

			private static string NormalizeTextPM(string text)
			{
				if (string.IsNullOrWhiteSpace(text)) return string.Empty;
				string s = text.Replace("\t", "").Replace("\r", "").Replace("\n", "");
				// 去空白
				s = s.Replace(" ", "");
				// PrintMatch 对易混字符与符号的归一
				s = s
					.Replace('l', 'I')
					.Replace('O', '0')
					.Replace('o', '0')
					.Replace('1', 'I')
					.Replace('S', '5')
					.Replace('Z', '2')
					.Replace('G', '6')
					.Replace('B', '8')
					.Replace("°C", "℃")
					.Replace("°", "")
					.Replace('。', '.')
					.Replace('！', '!')
					.Replace('，', ',')
					.Replace('(', '（')
					.Replace(')', '）')
					.Replace(':', '：')
					.Replace('[', '【')
					.Replace(']', '】')
					.Replace('“', '"')
					.Replace('”', '"')
					.Replace('\'', '"')
					.Replace("—", "-")
					.Replace("--", "-");
				return s.ToUpperInvariant();
			}

			private static double CalculatePositionError(SimpleOcrItem a, SimpleOcrItem b)
			{
				int ax = a.X + a.Width / 2;
				int ay = a.Y + a.Height / 2;
				int bx = b.X + b.Width / 2;
				int by = b.Y + b.Height / 2;
				int dx = ax - bx;
				int dy = ay - by;
				return Math.Sqrt(dx * dx + dy * dy);
			}

			private static double PositionErrorThreshold(double xTol, double yTol)
			{
				return Math.Sqrt(xTol * xTol + yTol * yTol);
			}

			private static bool HasOverlap(SimpleOcrItem t, SimpleOcrItem d)
			{
				int ax2 = t.X + t.Width, ay2 = t.Y + t.Height;
				int bx2 = d.X + d.Width, by2 = d.Y + d.Height;
				return !(ax2 < d.X || bx2 < t.X || ay2 < d.Y || by2 < t.Y);
			}

		private static JObject MatchAsPrintMatch(SimpleTemplate golden, List<SimpleOcrItem> det, double posTolXVal, double posTolYVal, double minConf, bool checkPosition)
		{
			if (golden == null) return new JObject();
			var templateItems = (golden.OCRResults ?? new List<SimpleOcrItem>()).Where(r => r != null && !string.IsNullOrWhiteSpace(r.Text)).ToList();
			var detectionItemsAll = (det ?? new List<SimpleOcrItem>()).Where(r => r != null && !string.IsNullOrWhiteSpace(r.Text)).ToList();
			// 置信度过滤（与 PrintMatch 一致）
			var validDetections = detectionItemsAll.Where(r => r.Confidence >= (float)minConf && r.Width > 0 && r.Height > 0).ToList();
			
			// 不检查位置时：只按文本内容和数量匹配
			if (!checkPosition)
			{
				return MatchByTextAndCountOnly(golden, validDetections, templateItems);
			}
				
				// 按规范化文本分组
				var allTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var tplGroups = new Dictionary<string, List<SimpleOcrItem>>(StringComparer.OrdinalIgnoreCase);
				var detGroups = new Dictionary<string, List<SimpleOcrItem>>(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < templateItems.Count; i++)
				{
					string key = NormalizeTextPM(templateItems[i].Text);
					allTexts.Add(key);
					if (!tplGroups.ContainsKey(key)) tplGroups[key] = new List<SimpleOcrItem>();
					tplGroups[key].Add(templateItems[i]);
				}
				for (int i = 0; i < validDetections.Count; i++)
				{
					string key = NormalizeTextPM(validDetections[i].Text);
					allTexts.Add(key);
					if (!detGroups.ContainsKey(key)) detGroups[key] = new List<SimpleOcrItem>();
					detGroups[key].Add(validDetections[i]);
				}
				
				var overDetectionsGlobal = new List<SimpleOcrItem>();
				var missedTemplatesGlobal = new List<SimpleOcrItem>();
				var deviationPairs = new List<Tuple<SimpleOcrItem, SimpleOcrItem>>(); // (tpl, det)
				var correctPairs = new List<Tuple<SimpleOcrItem, SimpleOcrItem>>();
				var misjudgmentPairs = new List<Tuple<SimpleOcrItem, SimpleOcrItem>>(); // (tpl, det)
				
				double errTh = PositionErrorThreshold(posTolXVal, posTolYVal);
				
				foreach (var text in allTexts)
				{
					var groupTpl = tplGroups.ContainsKey(text) ? new List<SimpleOcrItem>(tplGroups[text]) : new List<SimpleOcrItem>();
					var groupDet = detGroups.ContainsKey(text) ? new List<SimpleOcrItem>(detGroups[text]) : new List<SimpleOcrItem>();
					var usedDet = new HashSet<SimpleOcrItem>();
					var matchedTpl = new HashSet<SimpleOcrItem>();
					// 第一轮：精确位置（欧氏距离）
					for (int ti = 0; ti < groupTpl.Count; ti++)
					{
						var t = groupTpl[ti];
						for (int di = 0; di < groupDet.Count; di++)
						{
							var d = groupDet[di]; if (usedDet.Contains(d)) continue;
							double pe = CalculatePositionError(t, d);
							if (pe <= errTh)
							{
								correctPairs.Add(Tuple.Create(t, d));
								usedDet.Add(d);
								matchedTpl.Add(t);
								break;
							}
						}
						// 未匹配留给第二轮
					}
					// 第二轮：重叠偏差
					var tplRemain = groupTpl.Where(x => !matchedTpl.Contains(x)).ToList();
					var detRemain = groupDet.Where(x => !usedDet.Contains(x)).ToList();
					for (int ti = 0; ti < tplRemain.Count; ti++)
					{
						var t = tplRemain[ti];
						for (int di = 0; di < detRemain.Count; di++)
						{
							var d = detRemain[di]; if (usedDet.Contains(d)) continue;
							if (HasOverlap(t, d))
							{
								deviationPairs.Add(Tuple.Create(t, d));
								usedDet.Add(d);
								matchedTpl.Add(t);
								break;
							}
						}
					}
					// 本组剩余
					for (int di = 0; di < groupDet.Count; di++)
					{
						var d = groupDet[di]; if (!usedDet.Contains(d)) overDetectionsGlobal.Add(d);
					}
					for (int ti = 0; ti < groupTpl.Count; ti++)
					{
						var t = groupTpl[ti]; if (!matchedTpl.Contains(t)) missedTemplatesGlobal.Add(t);
					}
				}
				
				// 组间误判配对：过检与漏检重叠 → 误判
				var usedOver = new HashSet<SimpleOcrItem>();
				var usedMiss = new HashSet<SimpleOcrItem>();
				for (int oi = 0; oi < overDetectionsGlobal.Count; oi++)
				{
					var d = overDetectionsGlobal[oi]; if (usedOver.Contains(d)) continue;
					for (int mi = 0; mi < missedTemplatesGlobal.Count; mi++)
					{
						var t = missedTemplatesGlobal[mi]; if (usedMiss.Contains(t)) continue;
						if (HasOverlap(t, d))
						{
							misjudgmentPairs.Add(Tuple.Create(t, d));
							usedOver.Add(d);
							usedMiss.Add(t);
							break;
						}
					}
				}
				// 移除已并入误判的过检与漏检
				overDetectionsGlobal = overDetectionsGlobal.Where(d => !usedOver.Contains(d)).ToList();
				missedTemplatesGlobal = missedTemplatesGlobal.Where(t => !usedMiss.Contains(t)).ToList();
				
				// 统计
				int correctCount = correctPairs.Count;
				int deviationCount = deviationPairs.Count;
				int overCount = overDetectionsGlobal.Count;
				int missCount = missedTemplatesGlobal.Count;
				int misjudgeCount = misjudgmentPairs.Count;
				int totalTpl = Math.Max(0, templateItems.Count);
				double score = 0.0;
				if (totalTpl > 0)
				{
					var correctRatio = (double)correctCount / totalTpl;
					var deviationPenalty = Math.Min(deviationCount * 0.1, 0.3);
					var overPenalty = Math.Min(overCount * 0.15, 0.4);
					var missPenalty = Math.Min(missCount * 0.2, 0.5);
					var misPenalty = Math.Min(misjudgeCount * 0.5, 0.8);
					score = Math.Max(0.0, correctRatio - deviationPenalty - overPenalty - missPenalty - misPenalty);
				}
				bool isMatch = (missCount == 0 && overCount == 0 && misjudgeCount == 0);
				
				// 构造输出 JSON（与 PrintMatch 字段一致）
				var root = new JObject();
				// ocr_results
				var ocrArray = new JArray();
				for (int i = 0; i < validDetections.Count; i++)
				{
					var d = validDetections[i];
					string status = "OverDetection";
					if (correctPairs.Any(p => ReferenceEquals(p.Item2, d))) status = "Correct";
					else if (deviationPairs.Any(p => ReferenceEquals(p.Item2, d))) status = "PositionDeviation";
					else if (misjudgmentPairs.Any(p => ReferenceEquals(p.Item2, d))) status = "Misjudgment";
					var o = new JObject
					{
						["text"] = d.Text ?? string.Empty,
						["x"] = d.X,
						["y"] = d.Y,
						["width"] = d.Width,
						["height"] = d.Height,
						["confidence"] = (double)d.Confidence,
						["match_status"] = status
					};
					ocrArray.Add(o);
				}
				root["ocr_results"] = ocrArray;
				// missing_template_items
				var missingArr = new JArray();
				for (int i = 0; i < missedTemplatesGlobal.Count; i++)
				{
					var t = missedTemplatesGlobal[i];
					missingArr.Add(new JObject
					{
						["text"] = t.Text ?? string.Empty,
						["x"] = t.X,
						["y"] = t.Y,
						["width"] = t.Width,
						["height"] = t.Height,
						["status"] = "missing"
					});
				}
				root["missing_template_items"] = missingArr;
				// deviation_template_items（用于绘制模版虚线框）
				var devTplArr = new JArray();
				for (int i = 0; i < deviationPairs.Count; i++)
				{
					var t = deviationPairs[i].Item1;
					devTplArr.Add(new JObject
					{
						["text"] = t.Text ?? string.Empty,
						["x"] = t.X,
						["y"] = t.Y,
						["width"] = t.Width,
						["height"] = t.Height
					});
				}
				root["deviation_template_items"] = devTplArr;
				// misjudgment_pairs
				var misPairsArr = new JArray();
				for (int i = 0; i < misjudgmentPairs.Count; i++)
				{
					var pair = misjudgmentPairs[i];
					var t = pair.Item1; var d = pair.Item2;
					misPairsArr.Add(new JObject
					{
						["d_text"] = d.Text ?? string.Empty,
						["d_x"] = d.X,
						["d_y"] = d.Y,
						["d_w"] = d.Width,
						["d_h"] = d.Height,
						["t_text"] = t.Text ?? string.Empty,
						["t_x"] = t.X,
						["t_y"] = t.Y,
						["t_w"] = t.Width,
						["t_h"] = t.Height
					});
				}
				root["misjudgment_pairs"] = misPairsArr;
				// template_match_info
			root["template_match_info"] = new JObject
			{
				["template_name"] = golden.TemplateName ?? string.Empty,
				["product_name"] = golden.ProductName ?? string.Empty,
				["is_match"] = isMatch,
				["match_score"] = score,
				["perfect_matches"] = correctCount,
				["position_deviations"] = deviationCount,
				["over_detections"] = overCount,
				["missing_components"] = missCount,
				["misjudgments"] = misjudgeCount
			};
				return root;
			}

		private static List<SimpleOcrItem> ExtractOcrFromLocal(JArray results)
		{
			var list = new List<SimpleOcrItem>();
			foreach (var t in results)
			{
				var e = t as JObject; if (e == null) continue;
				if (!string.Equals(e["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase)) continue;
				var srs = e["sample_results"] as JArray; if (srs == null) continue;
				foreach (var s in srs)
				{
					var so = s as JObject; if (so == null) continue;
					var bbox = so["bbox"] as JArray; if (bbox == null || bbox.Count < 4) continue;
					bool withAngle = so["with_angle"]?.Value<bool>() ?? false;
					double angle = so["angle"]?.Value<double?>() ?? -100.0;
					int x, y, w, h;
					if (withAngle && angle != -100.0)
					{
						double cx = bbox[0].Value<double>();
						double cy = bbox[1].Value<double>();
						double ww = Math.Max(1.0, bbox[2].Value<double>());
						double hh = Math.Max(1.0, bbox[3].Value<double>());
						x = (int)Math.Round(cx - ww / 2.0);
						y = (int)Math.Round(cy - hh / 2.0);
						w = Math.Max(1, (int)Math.Round(ww));
						h = Math.Max(1, (int)Math.Round(hh));
					}
					else
					{
						x = ClampToInt(bbox[0].Value<double>());
						y = ClampToInt(bbox[1].Value<double>());
						w = Math.Max(1, ClampToInt(bbox[2].Value<double>()));
						h = Math.Max(1, ClampToInt(bbox[3].Value<double>()));
					}
					string text = so["category_name"]?.ToString() ?? string.Empty;
					float conf = SafeToFloat(so["score"]);
					list.Add(new SimpleOcrItem { Text = text, Confidence = conf, X = x, Y = y, Width = w, Height = h });
				}
			}
			return list;
		}

		private static SimpleTemplateMatchDetail MatchTemplate(SimpleTemplate golden, List<SimpleOcrItem> det, double posTolXVal, double posTolYVal)
		{
			var detail = new SimpleTemplateMatchDetail();
			var normalizedToTpl = new Dictionary<string, List<SimpleOcrItem>>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < golden.OCRResults.Count; i++)
			{
				var t = golden.OCRResults[i];
				string key = SimpleTemplateUtils.NormalizeText(t.Text);
				if (!normalizedToTpl.ContainsKey(key)) normalizedToTpl[key] = new List<SimpleOcrItem>();
				normalizedToTpl[key].Add(t);
			}

			var normalizedToDet = new Dictionary<string, List<SimpleOcrItem>>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < det.Count; i++)
			{
				string key = SimpleTemplateUtils.NormalizeText(det[i].Text);
				if (!normalizedToDet.ContainsKey(key)) normalizedToDet[key] = new List<SimpleOcrItem>();
				normalizedToDet[key].Add(det[i]);
			}

			int xTol = (int)Math.Round(posTolXVal);
			int yTol = (int)Math.Round(posTolYVal);

			int correct = 0, deviation = 0, over = 0, miss = 0, misjudge = 0;
			var usedDet = new HashSet<SimpleOcrItem>();
			var usedTpl = new HashSet<SimpleOcrItem>();

			var allTexts = new HashSet<string>(normalizedToTpl.Keys, StringComparer.OrdinalIgnoreCase);
			foreach (var k in normalizedToDet.Keys) allTexts.Add(k);

			foreach (var text in allTexts)
			{
				var tplList = normalizedToTpl.ContainsKey(text) ? normalizedToTpl[text] : new List<SimpleOcrItem>();
				var detList = normalizedToDet.ContainsKey(text) ? normalizedToDet[text] : new List<SimpleOcrItem>();

				// 第一轮：位置匹配
				foreach (var t in tplList)
				{
					bool matched = false;
					for (int i = 0; i < detList.Count; i++)
					{
						var d = detList[i]; if (usedDet.Contains(d)) continue;
						int tx = t.X + t.Width / 2, ty = t.Y + t.Height / 2;
						int dx = d.X + d.Width / 2, dy = d.Y + d.Height / 2;
						if (Math.Abs(tx - dx) <= xTol && Math.Abs(ty - dy) <= yTol)
						{
							correct += 1; usedDet.Add(d); usedTpl.Add(t);
							var jo = new JObject { ["t_text"] = t.Text, ["d_text"] = d.Text, ["status"] = "correct" };
							detail.CorrectItems.Add(jo);
							matched = true; break;
						}
					}
					if (!matched) { /* 留给第二轮 */ }
				}

				// 第二轮：重叠匹配（仅剩余项）
				var tplRemain = tplList.Where(x => !usedTpl.Contains(x)).ToList();
				var detRemain = detList.Where(x => !usedDet.Contains(x)).ToList();
				foreach (var t in tplRemain)
				{
					for (int i = 0; i < detRemain.Count; i++)
					{
						var d = detRemain[i]; if (usedDet.Contains(d)) continue;
						if (HasOverlap(t, d))
						{
							deviation += 1; usedDet.Add(d); usedTpl.Add(t);
							var jo = new JObject { ["t_text"] = t.Text, ["d_text"] = d.Text, ["status"] = "deviation" };
							detail.DeviationItems.Add(jo);
							break;
						}
					}
				}
			}

			// 误判配对
			var detUnused = det.Where(x => !usedDet.Contains(x)).ToList();
			var tplUnused = golden.OCRResults.Where(x => !usedTpl.Contains(x)).ToList();
			for (int i = 0; i < detUnused.Count; i++)
			{
				var d = detUnused[i];
				bool paired = false;
				for (int j = 0; j < tplUnused.Count; j++)
				{
					var t = tplUnused[j];
					if (HasOverlap(t, d))
					{
						misjudge += 1; paired = true;
						var jo = new JObject { ["t_text"] = t.Text, ["d_text"] = d.Text, ["status"] = "misjudgment" };
						detail.MisjudgmentPairs.Add(jo);
						usedTpl.Add(t);
						break;
					}
				}
				if (!paired) { over += 1; detail.OverDetectionItems.Add(new JObject { ["d_text"] = d.Text }); }
			}

			// 剩余未配对模版为漏检
			foreach (var t in golden.OCRResults)
			{
				if (!usedTpl.Contains(t)) { miss += 1; detail.MissedTemplateItems.Add(new JObject { ["t_text"] = t.Text }); }
			}

			// 评分与结论：无漏检/过检/误判为通过；分数粗略按正确率减罚分
			int total = Math.Max(1, golden.OCRResults.Count);
			detail.Score = Math.Max(0.0, (double)correct / total - Math.Min(over * 0.15, 0.4) - Math.Min(miss * 0.2, 0.5) - Math.Min(misjudge * 0.5, 0.8));
			detail.IsMatch = (miss == 0 && over == 0 && misjudge == 0);
			return detail;
		}

		private static int ClampToInt(double v)
		{
			if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
			if (v > int.MaxValue) return int.MaxValue;
			if (v < int.MinValue) return int.MinValue;
			return (int)Math.Round(v);
		}

		private static float SafeToFloat(JToken t)
		{
			try { return t != null ? Convert.ToSingle(((JValue)t).Value) : 0f; } catch { return 0f; }
		}

		private double ReadDoubleOr(string key, double dv)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				double x; if (double.TryParse(v.ToString(), out x)) return x;
			}
			return dv;
		}

		private bool ReadBoolOr(string key, bool dv)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				bool b; if (bool.TryParse(v.ToString(), out b)) return b;
			}
			return dv;
		}

		private static JObject MatchByTextAndCountOnly(SimpleTemplate golden, List<SimpleOcrItem> validDetections, List<SimpleOcrItem> templateItems)
		{
			var tplTextCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var detTextCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			
			foreach (var t in templateItems)
			{
				string key = NormalizeTextPM(t.Text);
				if (!tplTextCount.ContainsKey(key)) tplTextCount[key] = 0;
				tplTextCount[key]++;
			}
			
			foreach (var d in validDetections)
			{
				string key = NormalizeTextPM(d.Text);
				if (!detTextCount.ContainsKey(key)) detTextCount[key] = 0;
				detTextCount[key]++;
			}
			
			var allTexts = new HashSet<string>(tplTextCount.Keys, StringComparer.OrdinalIgnoreCase);
			foreach (var k in detTextCount.Keys) allTexts.Add(k);
			
			int totalMatch = 0;
			int overCount = 0;
			int missCount = 0;
			var correctList = new List<SimpleOcrItem>();
			var overList = new List<SimpleOcrItem>();
			var missList = new List<SimpleOcrItem>();
			
			foreach (var text in allTexts)
			{
				int tplCount = tplTextCount.ContainsKey(text) ? tplTextCount[text] : 0;
				int detCount = detTextCount.ContainsKey(text) ? detTextCount[text] : 0;
				int matched = Math.Min(tplCount, detCount);
				totalMatch += matched;
				
				if (detCount > tplCount)
				{
					overCount += (detCount - tplCount);
					var items = validDetections.Where(d => string.Equals(NormalizeTextPM(d.Text), text, StringComparison.OrdinalIgnoreCase)).ToList();
					for (int i = matched; i < items.Count; i++) overList.Add(items[i]);
				}
				else if (tplCount > detCount)
				{
					missCount += (tplCount - detCount);
					var items = templateItems.Where(t => string.Equals(NormalizeTextPM(t.Text), text, StringComparison.OrdinalIgnoreCase)).ToList();
					for (int i = matched; i < items.Count; i++) missList.Add(items[i]);
				}
				
				var matchedDet = validDetections.Where(d => string.Equals(NormalizeTextPM(d.Text), text, StringComparison.OrdinalIgnoreCase)).Take(matched).ToList();
				correctList.AddRange(matchedDet);
			}
			
			bool isMatch = (missCount == 0 && overCount == 0);
			int totalTpl = Math.Max(1, templateItems.Count);
			double score = (double)totalMatch / totalTpl;
			
			var root = new JObject();
			var ocrArray = new JArray();
			foreach (var d in validDetections)
			{
				string status = "OverDetection";
				if (correctList.Any(c => ReferenceEquals(c, d))) status = "Correct";
				var o = new JObject
				{
					["text"] = d.Text ?? string.Empty,
					["x"] = d.X,
					["y"] = d.Y,
					["width"] = d.Width,
					["height"] = d.Height,
					["confidence"] = (double)d.Confidence,
					["match_status"] = status
				};
				ocrArray.Add(o);
			}
			root["ocr_results"] = ocrArray;
			
			var missingArr = new JArray();
			foreach (var t in missList)
			{
				missingArr.Add(new JObject
				{
					["text"] = t.Text ?? string.Empty,
					["x"] = t.X,
					["y"] = t.Y,
					["width"] = t.Width,
					["height"] = t.Height,
					["status"] = "missing"
				});
			}
			root["missing_template_items"] = missingArr;
			root["deviation_template_items"] = new JArray();
			// 构造误判配对：按个数成对输出（用于“错：模型：x，模版：y”）
			var misPairsArr = new JArray();
			int pairCount = Math.Min(missList.Count, overList.Count);
			for (int i = 0; i < pairCount; i++)
			{
				var pair = new JObject
				{
					["t_text"] = missList[i].Text ?? string.Empty,
					["d_text"] = overList[i].Text ?? string.Empty
				};
				misPairsArr.Add(pair);
			}
			root["misjudgment_pairs"] = misPairsArr;
			
			root["template_match_info"] = new JObject
			{
				["template_name"] = golden.TemplateName ?? string.Empty,
				["product_name"] = golden.ProductName ?? string.Empty,
				["is_match"] = isMatch,
				["match_score"] = score,
				["perfect_matches"] = totalMatch,
				["position_deviations"] = 0,
				["over_detections"] = overCount,
				["missing_components"] = missCount,
				["misjudgments"] = 0
			};
			
			return root;
		}
	}
}


namespace DlcvModules
{
	/// <summary>
	/// 编排模块：印刷品模版匹配（复用现有模版模块，集中流程与条件）。
	/// type: features/printed_template_match
	/// properties:
	/// - product_type(string): 产品型号
	/// 可透传：position_tolerance_x(double), position_tolerance_y(double), min_confidence_threshold(double), check_position(bool)
	/// 输出：Scalar ok(bool), detail(string)；TemplateList 按条件返回。
	/// </summary>
	public class PrintedTemplateMatch : BaseModule
	{
		static PrintedTemplateMatch()
		{
			ModuleRegistry.Register("features/printed_template_match", typeof(PrintedTemplateMatch));
		}

		public PrintedTemplateMatch(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
			: base(nodeId, title, properties, context) { }

		public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
		{
			var images = imageList ?? new List<ModuleImage>();
			var results = resultList ?? new JArray();

			string saveDir = this.Context != null ? this.Context.Get<string>("templates_dir", null) : null;
			if (!string.IsNullOrWhiteSpace(saveDir)) { try { Directory.CreateDirectory(saveDir); } catch { } }
			string productType = ReadString("product_type", null);
			productType = productType ?? string.Empty;

			string face = this.Context != null ? this.Context.Get<string>("face", null) : null;
			string templateName = productType;
			if (!string.IsNullOrWhiteSpace(face))
			{
				templateName = productType + "_" + face;
			}

			// 1) 从 results 构建模版
			var builder = new TemplateFromResults(NodeId * 10 + 1, context: this.Context, properties: new Dictionary<string, object>
			{
				["product_name"] = productType,
				["product_id"] = productType,
				["template_name"] = templateName
			});
			var built = builder.Process(images, results);
			SimpleTemplate tpl = null;
			if (built != null && built.TemplateList != null && built.TemplateList.Count > 0)
			{
				tpl = built.TemplateList[0];
			}
			if (tpl == null)
			{
				try { this.ScalarOutputsByName["ok"] = false; this.ScalarOutputsByName["detail"] = string.Empty; } catch { }
				return new ModuleIO(images, results, new List<SimpleTemplate>());
			}

			// 条件分支：
			// 1) 有条码但无型号 -> 直接NG（error_reason: 条码NG）
			// 2) 无条码/条码为NG -> 无条码直接通过
			try
			{
				string barcode = this.Context != null ? this.Context.Get<string>("barcode_text", null) : null;
				bool isNoBarcode = string.IsNullOrWhiteSpace(barcode) || string.Equals(barcode, "NG", StringComparison.OrdinalIgnoreCase);
				bool isUnknownProduct = string.IsNullOrWhiteSpace(productType) || string.Equals(productType, "Unknown", StringComparison.OrdinalIgnoreCase);
				bool hasBarcode = !isNoBarcode;

				if (hasBarcode && isUnknownProduct)
				{
					var rootNg = new JObject();
					rootNg["ocr_results"] = new JArray();
					rootNg["missing_template_items"] = new JArray();
					rootNg["deviation_template_items"] = new JArray();
					rootNg["misjudgment_pairs"] = new JArray();
					rootNg["template_match_info"] = new JObject
					{
						["template_name"] = string.Empty,
						["product_name"] = string.Empty,
						["is_match"] = false,
						["match_score"] = 0.0,
						["perfect_matches"] = 0,
						["position_deviations"] = 0,
						["over_detections"] = 0,
						["missing_components"] = 0,
						["misjudgments"] = 0,
						["error_reason"] = "条码NG"
					};
					try { this.ScalarOutputsByName["ok"] = false; this.ScalarOutputsByName["detail"] = rootNg.ToString(Formatting.None); } catch { }
					return new ModuleIO(images, results, new List<SimpleTemplate>());
				}

				if (isNoBarcode)
				{
					var ocrArray = new JArray();
					int correctCount = 0;
					int overCount = 0;
					var list = tpl.OCRResults ?? new List<SimpleOcrItem>();
					for (int i = 0; i < list.Count; i++)
					{
						var it = list[i]; if (it == null) continue;
						string txt = it.Text ?? string.Empty;
						bool isNgText = txt.IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0;
						var o = new JObject
						{
							["text"] = txt,
							["x"] = it.X,
							["y"] = it.Y,
							["width"] = it.Width,
							["height"] = it.Height,
							["confidence"] = (double)it.Confidence,
							["match_status"] = isNgText ? "OverDetection" : "Correct"
						};
						if (isNgText) overCount++; else correctCount++;
						ocrArray.Add(o);
					}

					var root = new JObject();
					root["ocr_results"] = ocrArray;
					root["missing_template_items"] = new JArray();
					root["deviation_template_items"] = new JArray();
					root["misjudgment_pairs"] = new JArray();
					root["template_match_info"] = new JObject
					{
						["template_name"] = string.Empty,
						["product_name"] = string.Empty,
						["is_match"] = true,
						["match_score"] = 1.0,
						["perfect_matches"] = correctCount,
						["position_deviations"] = 0,
						["over_detections"] = overCount,
						["missing_components"] = 0,
						["misjudgments"] = 0
					};

					try { this.ScalarOutputsByName["ok"] = true; this.ScalarOutputsByName["detail"] = root.ToString(Formatting.None); } catch { }
					return new ModuleIO(images, results, new List<SimpleTemplate>());
				}
			}
			catch { }

			// 分支 A：无产品型号 -> 不保存，返回模版，ok=false
			if (string.IsNullOrWhiteSpace(productType))
			{
				try { this.ScalarOutputsByName["ok"] = false; this.ScalarOutputsByName["detail"] = string.Empty; } catch { }
				return new ModuleIO(images, results, new List<SimpleTemplate> { tpl });
			}

			// 计算模版文件路径（固定使用执行上下文中的 templates_dir）
			string fname = SimpleTemplateUtils.MakeSafeFileName(templateName);
			string jsonPath = string.IsNullOrWhiteSpace(saveDir) ? (fname + ".json") : Path.Combine(saveDir, fname + ".json");

			// 分支 B：无现存模版 -> 保存模版与 PNG（PNG 使用首图 OriginalImage），随后与自身匹配
			if (!File.Exists(jsonPath))
			{
				List<ModuleImage> imagesForSave = images;
				try
				{
					var img0 = images != null && images.Count > 0 ? images[0] : null;
					if (img0 != null && img0.OriginalImage != null && !img0.OriginalImage.Empty())
					{
						var ori = img0.OriginalImage;
						var ts = img0.TransformState ?? new TransformationState(ori.Width, ori.Height);
						imagesForSave = new List<ModuleImage> { new ModuleImage(ori, ori, ts, img0.OriginalIndex) };
					}
				}
				catch { imagesForSave = images; }

				var saver = new TemplateSave(NodeId * 10 + 2, context: this.Context, properties: new Dictionary<string, object>
				{
					["file_name"] = fname
				});
				saver.MainTemplateList = new List<SimpleTemplate> { tpl };
				try { saver.Process(imagesForSave, results); } catch { }

				// 自匹配：golden 使用磁盘上已保存（已过滤）的模版文件
				var goldenListSelf = new List<SimpleTemplate>();
				var loaderSelf = new TemplateLoad(NodeId * 10 + 3, context: this.Context, properties: new Dictionary<string, object>
				{
					["path"] = jsonPath
				});
				// 保存完成后校验文件存在
				if (!File.Exists(jsonPath))
				{
					throw new Exception("template save failed: file not found");
				}
				var loSelf = loaderSelf.Process(images, results);
				if (loSelf == null || loSelf.TemplateList == null || loSelf.TemplateList.Count == 0)
				{
					throw new Exception("template load failed");
				}
				goldenListSelf = loSelf.TemplateList;
				bool checkPosSelf = false; try { if (Properties != null && Properties.ContainsKey("check_position") && Properties["check_position"] != null) { bool.TryParse(Properties["check_position"].ToString(), out checkPosSelf); } } catch { }
				var matcherSelf = new TemplateMatch(NodeId * 10 + 5, context: this.Context, properties: new Dictionary<string, object>
				{
					["position_tolerance_x"] = ReadDoubleOr("position_tolerance_x", 20.0),
					["position_tolerance_y"] = ReadDoubleOr("position_tolerance_y", 20.0),
					["min_confidence_threshold"] = ReadDoubleOr("min_confidence_threshold", 0.5),
					["check_position"] = checkPosSelf
				});
				matcherSelf.MainTemplateList = new List<SimpleTemplate> { tpl };
				matcherSelf.ExtraInputsIn.Add(new ModuleChannel(new List<ModuleImage>(), new JArray(), goldenListSelf));
				try { matcherSelf.Process(images, results); } catch { }
				object okObjSelf = true; object detailObjSelf = string.Empty; // 自匹配必为 OK
				try { if (matcherSelf.ScalarOutputsByName != null && matcherSelf.ScalarOutputsByName.ContainsKey("ok")) okObjSelf = matcherSelf.ScalarOutputsByName["ok"]; } catch { }
				try { if (matcherSelf.ScalarOutputsByName != null && matcherSelf.ScalarOutputsByName.ContainsKey("detail")) detailObjSelf = matcherSelf.ScalarOutputsByName["detail"]; } catch { }
				try { this.ScalarOutputsByName["ok"] = okObjSelf; this.ScalarOutputsByName["detail"] = detailObjSelf; } catch { }
				return new ModuleIO(images, results, new List<SimpleTemplate>());
			}

			// 分支 C：存在模版文件 -> 加载并匹配；返回 ok/detail，模版为空
			var loader = new TemplateLoad(NodeId * 10 + 3, context: this.Context, properties: new Dictionary<string, object>
			{
				["path"] = jsonPath
			});
			var lo = loader.Process(images, results);
			if (lo == null || lo.TemplateList == null || lo.TemplateList.Count == 0)
			{
				throw new Exception("template load failed");
			}
			List<SimpleTemplate> goldenList = lo.TemplateList;

			bool checkPos = false; try { if (Properties != null && Properties.ContainsKey("check_position") && Properties["check_position"] != null) { bool.TryParse(Properties["check_position"].ToString(), out checkPos); } } catch { }
			var matcher = new TemplateMatch(NodeId * 10 + 5, context: this.Context, properties: new Dictionary<string, object>
			{
				["position_tolerance_x"] = ReadDoubleOr("position_tolerance_x", 20.0),
				["position_tolerance_y"] = ReadDoubleOr("position_tolerance_y", 20.0),
				["min_confidence_threshold"] = ReadDoubleOr("min_confidence_threshold", 0.5),
				["check_position"] = checkPos
			});
			matcher.MainTemplateList = new List<SimpleTemplate> { tpl };
			matcher.ExtraInputsIn.Add(new ModuleChannel(new List<ModuleImage>(), new JArray(), goldenList));
			try { matcher.Process(images, results); } catch { }

			object okObj = false; object detailObj = string.Empty;
			try { if (matcher.ScalarOutputsByName != null && matcher.ScalarOutputsByName.ContainsKey("ok")) okObj = matcher.ScalarOutputsByName["ok"]; } catch { }
			try { if (matcher.ScalarOutputsByName != null && matcher.ScalarOutputsByName.ContainsKey("detail")) detailObj = matcher.ScalarOutputsByName["detail"]; } catch { }
			try { this.ScalarOutputsByName["ok"] = okObj; this.ScalarOutputsByName["detail"] = detailObj; } catch { }
			return new ModuleIO(images, results, new List<SimpleTemplate>());
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

		private double ReadDoubleOr(string key, double dv)
		{
			if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
			{
				double x; if (double.TryParse(v.ToString(), out x)) return x;
			}
			return dv;
		}
	}
}

