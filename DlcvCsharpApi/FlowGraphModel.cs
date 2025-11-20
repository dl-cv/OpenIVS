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

                // 选取最后节点（按 order/id）
                int lastNodeId = -1;
                int bestOrderKey = int.MinValue;
                for (int ni = 0; ni < _nodes.Count; ni++)
                {
                    var node = _nodes[ni];
                    if (node == null) continue;
                    int order = 0; int id = 0;
                    if (node.ContainsKey("order") && node["order"] != null) { try { order = Convert.ToInt32(node["order"]); } catch { order = 0; } }
                    if (node.ContainsKey("id") && node["id"] != null) { try { id = Convert.ToInt32(node["id"]); } catch { id = 0; } }
                    int key = (order << 20) + id;
                    if (key >= bestOrderKey) { bestOrderKey = key; lastNodeId = id; }
                }

                Dictionary<string, object> lastMap = null;
                if (lastNodeId != -1 && outputs.ContainsKey(lastNodeId))
                {
                    lastMap = outputs[lastNodeId];
                }
                else
                {
                    foreach (var kv in outputs) lastMap = kv.Value; // 取最后一个
                }

                JArray resultList = null;
                if (lastMap != null && lastMap.ContainsKey("result_list"))
                {
                    resultList = lastMap["result_list"] as JArray;
                }

                var entry = new JObject();
                entry["result_list"] = resultList ?? new JArray();
                merged.Add(entry);
            }

            var root = new JObject();
            root["result_list"] = merged.Count == 1 ? (merged[0] as JObject)["result_list"] : (JToken)merged;
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
            double ia = d * invDet;
            double ib = -b * invDet;
            double ic = -c * invDet;
            double id = a * invDet;
            double itx = -(ia * tx + ib * ty);
            double ity = -(ic * tx + id * ty);
            return new double[] { ia, ib, itx, ic, id, ity };
        }

        private Utils.CSharpResult ConvertFlowResultsToCSharp(JArray resultList)
        {
            var objects = new List<Utils.CSharpObjectResult>();
            if (resultList != null)
            {
                for (int ei = 0; ei < resultList.Count; ei++)
                {
                    var entry = resultList[ei] as JObject;
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

                    var samples = entry != null ? (entry["sample_results"] as JArray) : null;
                    if (samples == null) continue;
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
            }
            var sample = new Utils.CSharpSampleResult(objects);
            return new Utils.CSharpResult(new List<Utils.CSharpSampleResult> { sample });
        }
    }
}
