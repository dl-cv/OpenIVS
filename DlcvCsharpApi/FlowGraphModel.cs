using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;

namespace DlcvModules
{
    /// <summary>
    /// 流程图推理模型封装：与普通模型一致的调用方式（先加载，再推理/测速）。
    /// 强类型直连 ExecutionContext/GraphExecutor，便于调试与维护。
    /// </summary>
    public class FlowGraphModel : IDisposable
    {
        private List<Dictionary<string, object>> _nodes;
        private JObject _root;
        private bool _loaded = false;
        private bool _disposed = false;
        private int _deviceId = 0;
        private string _flowJsonPath;

        public bool IsLoaded { get { return _loaded; } }

        public JObject Load(string flowJsonPath, int deviceId = 0)
        {
            if (string.IsNullOrWhiteSpace(flowJsonPath)) throw new ArgumentException("流程 JSON 路径为空", nameof(flowJsonPath));
            if (!File.Exists(flowJsonPath)) throw new FileNotFoundException("流程 JSON 不存在", flowJsonPath);

            string text = File.ReadAllText(flowJsonPath);
            var root = JObject.Parse(text);
            var nodesToken = root["nodes"] as JArray;
            if (nodesToken == null) throw new InvalidOperationException("流程 JSON 缺少 nodes 数组");

            _nodes = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(nodesToken.ToString());
            _root = root;
            _deviceId = deviceId;
            _flowJsonPath = flowJsonPath;

            var ctx = new ExecutionContext();
            ctx.Set("device_id", deviceId);
            var exec = new GraphExecutor(_nodes, ctx);
            var report = exec.LoadModels();
            int code = report != null && report["code"] != null ? (int)report["code"] : 1;
            if (code != 0)
            {
                string simpleMessage = null;
                JToken modelsToken = report != null ? report["models"] : null;
                var models = modelsToken as JArray;
                if (models != null)
                {
                    for (int mi = 0; mi < models.Count; mi++)
                    {
                        var m = models[mi] as JObject;
                        if (m == null) continue;
                        int sc = m["status_code"] != null ? (int)m["status_code"] : 0;
                        if (sc != 0)
                        {
                            string statusMsg = m["status_message"] != null ? m["status_message"].ToString() : null;
                            if (!string.IsNullOrEmpty(statusMsg))
                            {
                                int jsonStart = statusMsg.IndexOf('{');
                                if (jsonStart >= 0)
                                {
                                    string innerJson = statusMsg.Substring(jsonStart);
                                    try
                                    {
                                        var innerObj = JObject.Parse(innerJson);
                                        string innerMsg = innerObj["message"] != null ? innerObj["message"].ToString() : null;
                                        if (!string.IsNullOrEmpty(innerMsg))
                                        {
                                            simpleMessage = innerMsg;
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                                else
                                {
                                    simpleMessage = statusMsg;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(simpleMessage))
                {
                    simpleMessage = report != null && report["message"] != null ? report["message"].ToString() : "unknown error";
                }

                var simpleObj = new JObject();
                simpleObj["code"] = 1;
                simpleObj["message"] = simpleMessage;
                report = simpleObj;
            }
            _loaded = true;
            return report;
        }

        public JObject GetModelInfo()
        {
            if (!_loaded) throw new InvalidOperationException("模型未加载");
            return _root ?? new JObject();
        }

        public Tuple<JObject, IntPtr> InferInternal(List<Mat> images, JObject paramsJson)
        {
            if (!_loaded) throw new InvalidOperationException("模型未加载");
            if (images == null || images.Count == 0) throw new ArgumentException("输入图像列表为空", nameof(images));

            var merged = new JArray();
            for (int i = 0; i < images.Count; i++)
            {
                var img = images[i];
                if (img == null || img.Empty())
                {
                    merged.Add(new JObject());
                    continue;
                }

                // UI 统一传入 RGB，这里转换为 BGR 进入流程（流程内约定 BGR）
                Mat bgrMat = img;
                Mat converted = null;
                try
                {
                    if (img != null && img.Channels() == 3)
                    {
                        converted = new Mat();
                        Cv2.CvtColor(img, converted, ColorConversionCodes.RGB2BGR);
                        bgrMat = converted;
                    }
                }
                catch { bgrMat = img; }

                var ctx = new ExecutionContext();
                ctx.Set("frontend_image_mat", bgrMat);
                ctx.Set("frontend_image_path", "");
                ctx.Set("device_id", _deviceId);

                var exec = new GraphExecutor(_nodes, ctx);
                var outputs = exec.Run();

                try { if (converted != null) { converted.Dispose(); } } catch { }

                // 获取 output/return_json 的结果
                JArray resultList = null;
                var feJson = ctx.Get<Dictionary<string, object>>("frontend_json");
                if (feJson != null && feJson.ContainsKey("last"))
                {
                    var lastPayload = feJson["last"] as Dictionary<string, object>;
                    if (lastPayload != null && lastPayload.ContainsKey("by_image"))
                    {
                        var byImg = lastPayload["by_image"] as List<Dictionary<string, object>>;
                        if (byImg != null && byImg.Count > 0)
                        {
                            foreach (var item in byImg)
                            {
                                int idx = Convert.ToInt32(item["origin_index"]);
                                if (idx != i) continue;
                                if (item.ContainsKey("results"))
                                {
                                    var resultsObj = item["results"];
                                    JArray resultsArr = null;
                                    if (resultsObj is JArray ja) resultsArr = ja;
                                    else if (resultsObj is List<Dictionary<string, object>> ldo) resultsArr = JArray.FromObject(ldo);
                                    else if (resultsObj is List<object> lo) resultsArr = JArray.FromObject(lo);
                                    else resultsArr = new JArray();
                                    // 合并
                                    if (resultList is JArray exist && exist.Count > 0)
                                    {
                                        foreach (var r in resultsArr) exist.Add(r);
                                    }
                                    else
                                    {
                                        resultList = resultsArr;
                                    }
                                }
                            }
                            merged.Add(resultList);
                        }
                    }
                }
            }

            var root = new JObject();
            root["result_list"] = images.Count == 1 ? merged[0] : merged;
            return new Tuple<JObject, IntPtr>(root, IntPtr.Zero);
        }

        public Utils.CSharpResult Infer(Mat image, JObject paramsJson = null)
        {
            var t = InferInternal(new List<Mat> { image }, paramsJson);
            var resultListToken = t.Item1["result_list"];
            JArray resultList;
            if (resultListToken is JArray ja)
            {
                resultList = ja;
            }
            else if (resultListToken is JObject jo)
            {
                resultList = jo["result_list"] as JArray ?? new JArray();
            }
            else
            {
                resultList = new JArray();
            }
            return ConvertFlowResultsToCSharp(resultList);
        }

        public Utils.CSharpResult InferBatch(List<Mat> imageList, JObject paramsJson = null)
        {
            if (imageList == null || imageList.Count == 0) throw new ArgumentException("输入图像列表为空", nameof(imageList));
            var allObjects = new List<Utils.CSharpObjectResult>();
            for (int i = 0; i < imageList.Count; i++)
            {
                var r = Infer(imageList[i], paramsJson);
                if (r.SampleResults != null && r.SampleResults.Count > 0)
                {
                    var objs = r.SampleResults[0].Results ?? new List<Utils.CSharpObjectResult>();
                    for (int k = 0; k < objs.Count; k++) allObjects.Add(objs[k]);
                }
            }
            var sample = new Utils.CSharpSampleResult(allObjects);
            return new Utils.CSharpResult(new List<Utils.CSharpSampleResult> { sample });
        }

        public double Benchmark(Mat image, int warmup = 1, int runs = 10)
        {
            if (image == null || image.Empty()) throw new ArgumentException("输入图像为空", nameof(image));
            for (int i = 0; i < warmup; i++) { Infer(image, null); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < runs; i++) { Infer(image, null); }
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / Math.Max(1, runs);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        ~FlowGraphModel()
        {
            Dispose(false);
        }

        private Utils.CSharpResult ConvertFlowResultsToCSharp(JArray resultList)
        {
            var samples = new List<Utils.CSharpSampleResult>();
            if (resultList == null || resultList.Count == 0)
            {
                return new Utils.CSharpResult(samples);
            }

            // 判断是否是 Batch 容器格式
            bool isBatchContainer = false;
            var first = resultList[0] as JObject;
            if (first != null && first.ContainsKey("result_list") && first["result_list"] is JArray)
            {
                isBatchContainer = true;
            }

            if (isBatchContainer)
            {
                foreach (var token in resultList)
                {
                    var container = token as JObject;
                    var list = container != null ? (container["result_list"] as JArray) : null;
                    samples.Add(ParseSingleImageResults(list));
                }
            }
            else
            {
                // Batch=1，resultList 本身就是结果列表
                samples.Add(ParseSingleImageResults(resultList));
            }

            return new Utils.CSharpResult(samples);
        }

        private Utils.CSharpSampleResult ParseSingleImageResults(JArray list)
        {
            var objects = new List<Utils.CSharpObjectResult>();
            if (list == null) return new Utils.CSharpSampleResult(objects);

            foreach (var token in list)
            {
                var entry = token as JObject;
                if (entry == null) continue;

                // 分支 1: 旧格式 (含 sample_results)
                if (entry.ContainsKey("sample_results"))
                {
                    ParseOldFormatEntry(entry, objects);
                }
                // 分支 2: 新格式 (直接含 bbox/category_id)
                else if (entry.ContainsKey("bbox") || entry.ContainsKey("category_id"))
                {
                    ParseNewFormatEntry(entry, objects);
                }
            }
            return new Utils.CSharpSampleResult(objects);
        }
        private void ParseNewFormatEntry(JObject entry, List<Utils.CSharpObjectResult> objects)
        {
            int categoryId = entry.Value<int?>("category_id") ?? 0;
            string categoryName = entry.Value<string>("category_name") ?? string.Empty;
            float score = entry.Value<float?>("score") ?? 0f;
            float area = entry.Value<float?>("area") ?? 0f;

            var bboxArr = entry["bbox"] as JArray;
            var bboxRaw = bboxArr != null ? bboxArr.ToObject<List<double>>() : new List<double>();

            // CSharpObjectResult 期望 XYWH (水平) 或 CXCYWH (旋转)
            // output/return_json 输出的是 XYXY (水平) 或 CXCYWHA (旋转)

            var bbox = new List<double>();
            bool withAngle = false;
            float angle = -100f;
            var meta = entry["metadata"] as JObject;
            bool isRotated = meta != null && meta.Value<bool?>("is_rotated") == true;

            if (bboxRaw.Count >= 4)
            {
                if (isRotated && bboxRaw.Count >= 5)
                {
                    // [cx, cy, w, h, angle] -> 取前4个
                    bbox.Add(bboxRaw[0]);
                    bbox.Add(bboxRaw[1]);
                    bbox.Add(bboxRaw[2]);
                    bbox.Add(bboxRaw[3]);
                    angle = (float)bboxRaw[4];
                    withAngle = true;
                }
                else if (bboxRaw.Count == 4)
                {
                    // [x1, y1, x2, y2] -> [x, y, w, h]
                    double x1 = bboxRaw[0];
                    double y1 = bboxRaw[1];
                    double x2 = bboxRaw[2];
                    double y2 = bboxRaw[3];
                    bbox.Add(x1);
                    bbox.Add(y1);
                    bbox.Add(Math.Max(0, x2 - x1));
                    bbox.Add(Math.Max(0, y2 - y1));
                }
                else
                {
                    // 回退
                    bbox.AddRange(bboxRaw);
                }
            }

            bool withBbox = (bbox.Count >= 4);

            Mat mask = new Mat();
            bool withMask = false;
            var polyToken = entry["poly"];

            if (polyToken is JArray polyOuter && polyOuter.Count > 0 && withBbox)
            {
                // 对于 mask 绘制，需要相对于 bbox 的左上角
                // 如果是旋转框，通常 mask 是在旋转矩形内或者全局 mask
                // 根据 output/return_json 逻辑，poly 是全局多边形
                // 而 CSharpObjectResult 的 mask 是局部 mask (从 bbox 裁剪出来的)
                // 所以我们需要计算 bbox 的 AABB，计算偏移，并绘制

                // 简单起见，我们这里计算 mask 的尺寸为 bbox 的 w,h (如果是旋转框，取 w,h)
                // 并根据 bbox 的中心点/左上角进行平移

                // 重新计算用于 mask 的左上角和尺寸
                double x0, y0;
                int w, h;

                if (withAngle)
                {
                    // 旋转框：bbox=[cx,cy,w,h]
                    // 无法简单绘制局部 mask，除非旋转 poly
                    // 暂不支持旋转框的 mask 还原到局部 mask (OpenCV 需要旋转)
                    // 或者我们假设 mask 画在 bounding rect 上
                    w = (int)Math.Max(1, bbox[2]);
                    h = (int)Math.Max(1, bbox[3]);
                    x0 = bbox[0] - w / 2.0;
                    y0 = bbox[1] - h / 2.0;
                }
                else
                {
                    // 水平框：bbox=[x,y,w,h]
                    x0 = bbox[0];
                    y0 = bbox[1];
                    w = (int)Math.Max(1, bbox[2]);
                    h = (int)Math.Max(1, bbox[3]);
                }

                try
                {
                    mask = Mat.Zeros(h, w, MatType.CV_8UC1);
                    bool anyPolyDrawn = false;

                    foreach (var contourToken in polyOuter)
                    {
                        var ptsArr = contourToken as JArray;
                        if (ptsArr == null || ptsArr.Count < 3) continue;

                        var points = new List<Point>();
                        foreach (var pToken in ptsArr)
                        {
                            var pa = pToken as JArray;
                            if (pa != null && pa.Count >= 2)
                            {
                                double px = pa[0].Value<double>();
                                double py = pa[1].Value<double>();
                                int rx = (int)Math.Round(px - x0);
                                int ry = (int)Math.Round(py - y0);
                                // 暂时不限制，让OpenCV处理
                                points.Add(new Point(rx, ry));
                            }
                        }
                        if (points.Count > 2)
                        {
                            var pts = new Point[][] { points.ToArray() };
                            Cv2.FillPoly(mask, pts, Scalar.White);
                            anyPolyDrawn = true;
                        }
                    }

                    if (anyPolyDrawn) withMask = true;
                }
                catch { }
            }

            var obj = new Utils.CSharpObjectResult(
                categoryId, categoryName, score, area, bbox,
                withMask, mask, withBbox, withAngle, angle);
            objects.Add(obj);
        }

        private void ParseOldFormatEntry(JObject entry, List<Utils.CSharpObjectResult> objects)
        {
            double[] invA23 = null;
            try
            {
                var tdict = entry != null ? (entry["transform"] as JObject) : null;
                var a23 = tdict != null ? (tdict["affine_2x3"] as JArray) : null;
                if (a23 != null && a23.Count >= 6)
                {
                    invA23 = Inverse2x3(new double[] {
                        a23[0].Value<double>(), a23[1].Value<double>(), a23[2].Value<double>(),
                        a23[3].Value<double>(), a23[4].Value<double>(), a23[5].Value<double>()
                    });
                }
            }
            catch { invA23 = null; }

            var samples = entry["sample_results"] as JArray;
            if (samples == null) return;

            for (int si = 0; si < samples.Count; si++)
            {
                var so = samples[si] as JObject;
                if (so == null) continue;

                int categoryId = so.Value<int?>("category_id") ?? 0;
                string categoryName = so.Value<string>("category_name") ?? string.Empty;
                float score = so.Value<float?>("score") ?? 0f;
                float area = so.Value<float?>("area") ?? 0f;
                var bboxArr = so["bbox"] as JArray;
                var bbox = bboxArr != null ? bboxArr.ToObject<List<double>>() : new List<double>();
                bool withBbox = so.Value<bool?>("with_bbox") ?? (bbox != null && bbox.Count > 0);
                bool withMask = so.Value<bool?>("with_mask") ?? false;
                bool withAngle = so.Value<bool?>("with_angle") ?? false;
                float angle = so.Value<float?>("angle") ?? -100f;

                if (invA23 != null && bbox != null && bbox.Count >= 4)
                {
                    if (withAngle && angle != -100f)
                    {
                        double cx = bbox[0], cy = bbox[1];
                        double w = Math.Abs(bbox[2]), h = Math.Abs(bbox[3]);
                        var cpt = ApplyAffine(invA23, cx, cy);
                        double ia = invA23[0], ib = invA23[1], ic = invA23[3], id = invA23[4];
                        double exx = Math.Cos(angle), exy = Math.Sin(angle);
                        double eyx = -Math.Sin(angle), eyy = Math.Cos(angle);
                        double gx_ex = ia * exx + ib * exy;
                        double gy_ex = ic * exx + id * exy;
                        double gx_ey = ia * eyx + ib * eyy;
                        double gy_ey = ic * eyx + id * eyy;
                        double sx = Math.Sqrt(gx_ex * gx_ex + gy_ex * gy_ex);
                        double sy = Math.Sqrt(gx_ey * gx_ey + gy_ey * gy_ey);
                        double newAngle = Math.Atan2(gy_ex, gx_ex);
                        bbox = new List<double> { cpt.X, cpt.Y, w * sx, h * sy };
                        withAngle = true; angle = (float)newAngle; withBbox = true;
                    }
                    else
                    {
                        double x = bbox[0], y = bbox[1], w = bbox[2], h = bbox[3];
                        var p1 = ApplyAffine(invA23, x, y);
                        var p2 = ApplyAffine(invA23, x + w, y);
                        var p3 = ApplyAffine(invA23, x + w, y + h);
                        var p4 = ApplyAffine(invA23, x, y + h);
                        double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
                        double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
                        double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
                        double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
                        bbox = new List<double> { minX, minY, Math.Max(1.0, maxX - minX), Math.Max(1.0, maxY - minY) };
                        withAngle = false; angle = -100f; withBbox = true;
                    }
                }

                Mat mask = new Mat();
                if (withMask && bbox != null && bbox.Count >= 4)
                {
                    var maskToken = so["mask"] ?? so["polygon"];
                    var pointsArray = maskToken as JArray;
                    int w = (int)(bbox.Count > 2 ? bbox[2] : 0);
                    int h = (int)(bbox.Count > 3 ? bbox[3] : 0);
                    if (pointsArray != null && w > 0 && h > 0)
                    {
                        try
                        {
                            mask = Mat.Zeros(h, w, MatType.CV_8UC1);
                            var points = new List<Point>();
                            double x0 = bbox[0];
                            double y0 = bbox[1];
                            for (int pi = 0; pi < pointsArray.Count; pi++)
                            {
                                var pToken = pointsArray[pi];
                                int px; int py;
                                var pj = pToken as JObject;
                                if (pj != null)
                                {
                                    px = pj.Value<int>("x");
                                    py = pj.Value<int>("y");
                                }
                                else
                                {
                                    var pa = pToken as JArray;
                                    if (pa == null || pa.Count < 2) continue;
                                    px = pa[0].Value<int>();
                                    py = pa[1].Value<int>();
                                }
                                int rx = (int)Math.Round(px - x0);
                                int ry = (int)Math.Round(py - y0);
                                rx = Math.Max(0, Math.Min(w - 1, rx));
                                ry = Math.Max(0, Math.Min(h - 1, ry));
                                points.Add(new Point(rx, ry));
                            }
                            if (points.Count > 2)
                            {
                                var pts = new Point[][] { points.ToArray() };
                                Cv2.FillPoly(mask, pts, Scalar.White);
                            }
                        }
                        catch { }
                    }
                }

                var obj = new Utils.CSharpObjectResult(
                    categoryId, categoryName, score, area, bbox,
                    withMask, mask, withBbox, withAngle, angle);
                objects.Add(obj);
            }
        }

        private static Point2d ApplyAffine(double[] a2x3, double x, double y)
        {
            if (a2x3 == null || a2x3.Length != 6) return new Point2d(x, y);
            double nx = a2x3[0] * x + a2x3[1] * y + a2x3[2];
            double ny = a2x3[3] * x + a2x3[4] * y + a2x3[5];
            return new Point2d(nx, ny);
        }

        private static double[] Inverse2x3(double[] a2x3)
        {
            if (a2x3 == null || a2x3.Length != 6) return null;
            double a = a2x3[0], b = a2x3[1], tx = a2x3[2];
            double c = a2x3[3], d = a2x3[4], ty = a2x3[5];
            double det = a * d - b * c;
            if (Math.Abs(det) < 1e-12) return null;
            double invDet = 1.0 / det;
            return new double[] { d * invDet, -b * invDet, -(d * invDet * tx + -b * invDet * ty),
                                  -c * invDet, a * invDet, -(-c * invDet * tx + a * invDet * ty) };
        }

    }
}
