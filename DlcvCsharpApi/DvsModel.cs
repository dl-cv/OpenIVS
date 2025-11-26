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
    /// 支持加载 .dvst/dvso/dvsp 格式（包含 pipeline.json + 多个 .dvt/.dvs/.dvp 模型）
    /// 解包 -> 临时存储 -> 加载 -> 清理
    /// </summary>
    public class DvsModel : IDisposable
    {
        private List<Dictionary<string, object>> _nodes;
        private JObject _root;
        private bool _loaded = false;
        private bool _disposed = false;
        private int _deviceId = 0;
        private string _dvsPath;
        private GraphExecutor _executor;

        // 临时文件夹路径，用于清理
        private string _tempDir = null;

        public bool IsLoaded { get { return _loaded; } }

        public JObject Load(string dvsPath, int deviceId = 0)
        {
            if (string.IsNullOrWhiteSpace(dvsPath)) throw new ArgumentException("文件路径为空", nameof(dvsPath));
            if (!File.Exists(dvsPath)) throw new FileNotFoundException("文件不存在", dvsPath);

            _dvsPath = dvsPath;
            _deviceId = deviceId;

                // 1. 准备临时目录
            _tempDir = Path.Combine(Path.GetTempPath(), "DlcvDvs_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            JObject pipelineJson = null;
            var extractedFiles = new Dictionary<string, string>(); // filename -> fullPath
            // 映射：原始文件名 -> 临时文件路径（Guid命名）
            var fileNameToTempPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (FileStream fs = new FileStream(dvsPath, FileMode.Open, FileAccess.Read))
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
                            // 关键修改：为了避免中文文件名导致的底层库打开失败，我们将临时文件重命名为纯英文（Guid）
                            // 保留原始扩展名以便识别
                            string ext = Path.GetExtension(fileName);
                            if (string.IsNullOrEmpty(ext)) ext = ".tmp";
                            string safeName = Guid.NewGuid().ToString("N") + ext;
                            string targetPath = Path.Combine(_tempDir, safeName);
                            
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
                            fileNameToTempPath[fileName] = targetPath;
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
                            
                            // 尝试匹配策略：
                            // 1. 原始路径是否就是文件名且在映射中？
                            if (fileNameToTempPath.ContainsKey(originalPath))
                            {
                                props["model_path"] = fileNameToTempPath[originalPath];
                            }
                            else
                            {
                                // 2. 尝试提取原始路径的文件名部分进行匹配
                                try 
                                {
                                    string justName = Path.GetFileName(originalPath);
                                    if (!string.IsNullOrEmpty(justName) && fileNameToTempPath.ContainsKey(justName))
                                    {
                                        props["model_path"] = fileNameToTempPath[justName];
                                    }
                                }
                                catch {}
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
                    Console.WriteLine($"[DvsModel] Warning: Failed to cleanup temp dir {_tempDir}: {ex.Message}");
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

                // 必须使用包含当前图像数据的 ctx 创建新的 GraphExecutor
                var exec = new GraphExecutor(_nodes, ctx);
                var outputs = exec.Run();

                if (converted != null) converted.Dispose();

                // 获取 output/return_json 的结果
                JArray resultList = new JArray();
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
                                    foreach (var r in resultsArr) resultList.Add(r);
                                }
                            }
                        }
                    }
                    merged.Add(resultList);
                }
            }

            var root = new JObject();
            root["result_list"] = images.Count == 1 ? merged[0] : merged;
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

        ~DvsModel()
        {
            Dispose(false);
        }

        // --- Helper Methods copied from FlowGraphModel (Refactor recommended later) ---
        
        private Utils.CSharpResult ConvertFlowResultsToCSharp(JArray resultList)
        {
            var samples = new List<Utils.CSharpSampleResult>();
            if (resultList == null || resultList.Count == 0)
            {
                var sample_result = new Utils.CSharpSampleResult();
                sample_result.Results = new List<Utils.CSharpObjectResult>();
                samples.Add(sample_result);
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

