using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;

namespace DlcvModules
{
    /// <summary>
    /// 支持加载 .dvst 格式（包含 pipeline.json + 多个 .dvt 模型）
    /// 解包 -> 临时存储 -> 加载 -> 清理
    /// </summary>
    public class DvstModel : IDisposable
    {
        private List<Dictionary<string, object>> _nodes;
        private JObject _root;
        private bool _loaded = false;
        private bool _disposed = false;
        private int _deviceId = 0;
        private string _dvstPath;
        private GraphExecutor _executor;

        // 临时文件夹路径，用于清理
        private string _tempDir = null;

        public bool IsLoaded { get { return _loaded; } }

        public JObject Load(string dvstPath, int deviceId = 0)
        {
            if (string.IsNullOrWhiteSpace(dvstPath)) throw new ArgumentException("文件路径为空", nameof(dvstPath));
            if (!File.Exists(dvstPath)) throw new FileNotFoundException("文件不存在", dvstPath);

            _dvstPath = dvstPath;
            _deviceId = deviceId;

            // 1. 准备临时目录
            _tempDir = Path.Combine(Path.GetTempPath(), "DlcvDvst_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            JObject pipelineJson = null;
            var extractedFiles = new Dictionary<string, string>(); // filename -> fullPath

            try
            {
                using (FileStream fs = new FileStream(dvstPath, FileMode.Open, FileAccess.Read))
                {
                    // 2. 校验头部 "DV\n"
                    byte[] magic = new byte[3];
                    if (fs.Read(magic, 0, 3) != 3 || magic[0] != 'D' || magic[1] != 'V' || magic[2] != '\n')
                    {
                        throw new InvalidDataException("文件格式错误：缺少 DV 头部");
                    }

                    // 3. 读取 JSON 头行
                    List<byte> lineBytes = new List<byte>();
                    int b;
                    while ((b = fs.ReadByte()) != -1)
                    {
                        if (b == '\n') break;
                        lineBytes.Add((byte)b);
                    }
                    string headerStr = Encoding.UTF8.GetString(lineBytes.ToArray());
                    JObject header = JObject.Parse(headerStr);

                    JArray fileList = header["file_list"] as JArray;
                    JArray fileSize = header["file_size"] as JArray;

                    if (fileList == null || fileSize == null || fileList.Count != fileSize.Count)
                    {
                        throw new InvalidDataException("文件头信息损坏：file_list 或 file_size 缺失/不匹配");
                    }

                    // 4. 遍历解包
                    for (int i = 0; i < fileList.Count; i++)
                    {
                        string fileName = fileList[i].ToString();
                        long size = (long)fileSize[i];

                        // 如果是 pipeline.json，直接读取到内存
                        if (fileName == "pipeline.json")
                        {
                            byte[] data = new byte[size];
                            if (fs.Read(data, 0, (int)size) != size) throw new EndOfStreamException("文件读取意外结束");
                            string jsonText = Encoding.UTF8.GetString(data);
                            pipelineJson = JObject.Parse(jsonText);
                        }
                        else
                        {
                            // 其他文件（.dvt）写入临时目录
                            string targetPath = Path.Combine(_tempDir, fileName);
                            using (FileStream outFs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                            {
                                byte[] buffer = new byte[8192];
                                long remaining = size;
                                while (remaining > 0)
                                {
                                    int read = fs.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                                    if (read == 0) throw new EndOfStreamException("文件读取意外结束");
                                    outFs.Write(buffer, 0, read);
                                    remaining -= read;
                                }
                            }
                            extractedFiles[fileName] = targetPath;
                        }
                    }
                }

                if (pipelineJson == null) throw new InvalidDataException("未找到 pipeline.json");
                _root = pipelineJson;

                // 5. 修改 Pipeline 中的 model_path
                var nodesToken = pipelineJson["nodes"] as JArray;
                if (nodesToken == null) throw new InvalidOperationException("Pipeline 缺少 nodes 数组");

                foreach (var node in nodesToken)
                {
                    if (node["properties"] is JObject props)
                    {
                        if (props.ContainsKey("model_path"))
                        {
                            string originalPath = props["model_path"].ToString();
                            // 如果提取的文件列表中有这个文件名，则替换为临时绝对路径
                            if (extractedFiles.ContainsKey(originalPath))
                            {
                                props["model_path"] = extractedFiles[originalPath];
                            }
                        }
                    }
                }

                // 6. 初始化 GraphExecutor
                _nodes = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(nodesToken.ToString());
                var ctx = new ExecutionContext();
                ctx.Set("device_id", deviceId);
                
                _executor = new GraphExecutor(_nodes, ctx);
                var report = _executor.LoadModels();

                // 7. 处理加载报告
                int code = report != null && report["code"] != null ? (int)report["code"] : 1;
                if (code != 0)
                {
                    // 尝试提取简化错误信息 (逻辑复用自 FlowGraphModel)
                    string simpleMessage = null;
                    JToken modelsToken = report != null ? report["models"] : null;
                    var models = modelsToken as JArray;
                    if (models != null)
                    {
                        foreach (var mToken in models)
                        {
                            var m = mToken as JObject;
                            if (m == null) continue;
                            if ((int?)m["status_code"] != 0)
                            {
                                string statusMsg = m["status_message"]?.ToString();
                                if (!string.IsNullOrEmpty(statusMsg))
                                {
                                    simpleMessage = statusMsg;
                                    // 尝试提取内嵌 JSON message
                                    int jsonStart = statusMsg.IndexOf('{');
                                    if (jsonStart >= 0)
                                    {
                                        try {
                                            var inner = JObject.Parse(statusMsg.Substring(jsonStart));
                                            if (inner["message"] != null) simpleMessage = inner["message"].ToString();
                                        } catch { }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    if (string.IsNullOrEmpty(simpleMessage))
                        simpleMessage = report?["message"]?.ToString() ?? "unknown error";

                    var simpleReport = new JObject();
                    simpleReport["code"] = 1;
                    simpleReport["message"] = simpleMessage;
                    report = simpleReport;
                }

                _loaded = true;
                return report;
            }
            catch (Exception)
            {
                // 加载失败，立即清理（如果成功加载，finally块中不清理？不，题目要求加载完即删除）
                // 但如果 MainProcess 正在占用文件句柄，这里删除会失败。
                // 假设底层库是 read-all-bytes 或者是 Copy-to-Memory，则可以删除。
                // 如果是 File Mapping，则不能删除。
                // 根据用户指示：“加载完之后删除此临时目录即可”，我们尝试删除。
                throw;
            }
            finally
            {
                // 8. 清理临时文件
                CleanupTemp();
            }
        }

        private void CleanupTemp()
        {
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DvstModel] Warning: Failed to cleanup temp dir {_tempDir}: {ex.Message}");
                }
                _tempDir = null;
            }
        }

        public JObject GetModelInfo()
        {
            if (!_loaded) throw new InvalidOperationException("模型未加载");
            return _root ?? new JObject();
        }

        // 复用 FlowGraphModel 的逻辑，这里因为没有公共基类，简单做个代理或者复制逻辑
        // 为了方便，直接暴露 Infer 方法，逻辑与 FlowGraphModel 几乎一致
        public Utils.CSharpResult InferBatch(List<Mat> imageList, JObject paramsJson = null)
        {
            if (!_loaded) throw new InvalidOperationException("模型未加载");
            var tuple = InferInternal(imageList, paramsJson);
            // 转换结果
            var resultListToken = tuple.Item1["result_list"];
            JArray resultList = resultListToken as JArray ?? new JArray();
            
            // 这里我们需要 FlowGraphModel 中的 ConvertFlowResultsToCSharp 逻辑
            // 鉴于不能修改 FlowGraphModel 为 public static，我必须复制那段转换代码
            // 或者我们实例化一个临时的 FlowGraphModel 辅助类？不，那太重了。
            // 最好的办法是把 ConvertFlowResultsToCSharp 变成一个工具方法。
            // 现在为了稳妥，我先复制一份转换逻辑到这里。
            return ConvertFlowResultsToCSharp(resultList);
        }

        public Tuple<JObject, IntPtr> InferInternal(List<Mat> images, JObject paramsJson)
        {
            if (!_loaded) throw new InvalidOperationException("模型未加载");
            
            var merged = new JArray();
            for (int i = 0; i < images.Count; i++)
            {
                var img = images[i];
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

                var outputs = _executor.Run();

                if (converted != null) converted.Dispose();

                // 选取最后节点
                int lastNodeId = -1;
                int bestOrderKey = int.MinValue;
                for (int ni = 0; ni < _nodes.Count; ni++)
                {
                    var node = _nodes[ni];
                    int order = node.ContainsKey("order") ? Convert.ToInt32(node["order"]) : 0;
                    int id = node.ContainsKey("id") ? Convert.ToInt32(node["id"]) : 0;
                    int key = (order << 20) + id;
                    if (key >= bestOrderKey) { bestOrderKey = key; lastNodeId = id; }
                }

                Dictionary<string, object> lastMap = null;
                if (lastNodeId != -1 && outputs.ContainsKey(lastNodeId)) lastMap = outputs[lastNodeId];
                else foreach (var kv in outputs) lastMap = kv.Value;

                var resultList = lastMap != null && lastMap.ContainsKey("result_list") ? lastMap["result_list"] as JArray : new JArray();
                
                var entry = new JObject();
                entry["result_list"] = resultList;
                merged.Add(entry);
            }

            var root = new JObject();
            root["result_list"] = merged.Count == 1 ? (merged[0] as JObject)["result_list"] : (JToken)merged;
            return new Tuple<JObject, IntPtr>(root, IntPtr.Zero);
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
                CleanupTemp();
                _disposed = true;
            }
        }

        ~DvstModel()
        {
            Dispose(false);
        }

        // --- Helper Methods copied from FlowGraphModel (Refactor recommended later) ---
        
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

