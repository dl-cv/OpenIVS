using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DlcvModuleApi.Pipeline;
using DlcvModuleApi.Utils;
using Newtonsoft.Json.Linq;

namespace DlcvModuleApi.Pipeline.Modules
{
    public class CombineResultsModule : ProcessModule
    {
        public CombineResultsModule(ModuleConfig cfg = null) : base(cfg) { }

        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            var rd = inputs.result_dict;
            string taskType = inputs.model_config?.task_type ?? "det";
            double thr = Config?.combine_ios_threshold ?? 0.2;

            if (!(taskType == "cls" || taskType == "分类" || taskType == "ocr" || taskType == "OCR" || taskType == "图像分类"))
            {
                if (taskType == "rotated_det" || taskType == "旋转框检测" || taskType == "rotated_detection_result")
                {
                    CombineRotated(rd, thr);
                }
                else
                {
                    CombineAxisAligned(rd, thr);
                }
            }
            return Task.FromResult(inputs);
        }

        private static double IOS(List<double> a, List<double> b)
        {
            double x1 = Math.Max(a[0], b[0]);
            double y1 = Math.Max(a[1], b[1]);
            double x2 = Math.Min(a[2], b[2]);
            double y2 = Math.Min(a[3], b[3]);
            double inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
            double area1 = Math.Max(0, a[2] - a[0]) * Math.Max(0, a[3] - a[1]);
            double area2 = Math.Max(0, b[2] - b[0]) * Math.Max(0, b[3] - b[1]);
            double small = Math.Min(area1, area2);
            if (small <= 0) return 0;
            return inter / small;
        }

        private static IEnumerable<Tuple<int, int>> GetNeighbors(Tuple<int, int> sliceIndex)
        {
            var right = new Tuple<int, int>(sliceIndex.Item1 + 1, sliceIndex.Item2);
            var down = new Tuple<int, int>(sliceIndex.Item1, sliceIndex.Item2 + 1);
            yield return right;
            yield return down;
        }

        private static void CombineAxisAligned(Dictionary<Tuple<int, int>, ResultEntry> rd, double thr)
        {
            var sliceToKey = new Dictionary<string, Tuple<int, int>>();
            foreach (var kv in rd)
            {
                if (kv.Value.predictions.Count == 0) continue;
                var si = kv.Value.predictions.First().Value.metadata?["slice_index"] as JArray;
                if (si == null || si.Count == 0) continue;
                var idx = si;
                string key = $"{idx[0].Value<int>()},{idx[1].Value<int>()}";
                sliceToKey[key] = kv.Key;
            }

            foreach (var kv in rd.ToList())
            {
                var preds = kv.Value.predictions;
                foreach (var p in preds.ToList())
                {
                    var pred = p.Value;
                    if (pred.metadata?["combine_flag"]?.Value<bool>() == true) continue;
                    var si = pred.metadata?["slice_index"] as JArray;
                    if (si == null || si.Count == 0) continue;
                    var idx = si;
                    var baseKey = new Tuple<int, int>(idx[0].Value<int>(), idx[1].Value<int>());
                    var label = pred.category_id;
                    var gb = pred.metadata?["global_bbox"] as JArray;
                    var bbox = new List<double> { gb[0].Value<double>(), gb[1].Value<double>(), gb[2].Value<double>(), gb[3].Value<double>() };

                    foreach (var nb in GetNeighbors(baseKey))
                    {
                        string nk = $"{nb.Item1},{nb.Item2}";
                        if (!sliceToKey.ContainsKey(nk)) continue;
                        var nbKey = sliceToKey[nk];
                        var nbPreds = rd[nbKey].predictions;
                        foreach (var q in nbPreds.ToList())
                        {
                            var np = q.Value;
                            if (np.category_id != label) continue;
                            var ngb = np.metadata?["global_bbox"] as JArray;
                            var nbbox = new List<double> { ngb[0].Value<double>(), ngb[1].Value<double>(), ngb[2].Value<double>(), ngb[3].Value<double>() };
                            var ios = IOS(bbox, nbbox);
                            if (ios > thr)
                            {
                                if (np.score > pred.score)
                                {
                                    pred.metadata["combine_flag"] = true;
                                }
                                else
                                {
                                    np.metadata["combine_flag"] = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static double RotatedIoU(double cx1, double cy1, double w1, double h1, double a1, double cx2, double cy2, double w2, double h2, double a2)
        {
            // 简化：回退 AABB IoU 作为保底
            double x1min = cx1 - w1 / 2.0;
            double y1min = cy1 - h1 / 2.0;
            double x1max = cx1 + w1 / 2.0;
            double y1max = cy1 + h1 / 2.0;
            double x2min = cx2 - w2 / 2.0;
            double y2min = cy2 - h2 / 2.0;
            double x2max = cx2 + w2 / 2.0;
            double y2max = cy2 + h2 / 2.0;
            double interX1 = Math.Max(x1min, x2min);
            double interY1 = Math.Max(y1min, y2min);
            double interX2 = Math.Min(x1max, x2max);
            double interY2 = Math.Min(y1max, y2max);
            double inter = Math.Max(0, interX2 - interX1) * Math.Max(0, interY2 - interY1);
            double area1 = w1 * h1;
            double area2 = w2 * h2;
            double union = area1 + area2 - inter;
            if (union <= 0) return 0;
            return inter / union;
        }

        private static void CombineRotated(Dictionary<Tuple<int, int>, ResultEntry> rd, double thr)
        {
            var sliceToKey = new Dictionary<string, Tuple<int, int>>();
            foreach (var kv in rd)
            {
                if (kv.Value.predictions.Count == 0) continue;
                var si = kv.Value.predictions.First().Value.metadata?["slice_index"] as JArray;
                if (si == null || si.Count == 0) continue;
                var idx = si[0];
                string key = $"{idx[0].Value<int>()},{idx[1].Value<int>()}";
                sliceToKey[key] = kv.Key;
            }

            foreach (var kv in rd.ToList())
            {
                var preds = kv.Value.predictions;
                foreach (var p in preds.ToList())
                {
                    var pred = p.Value;
                    if (!(pred.metadata?["is_rotated"]?.Value<bool>() ?? false)) continue;
                    if (pred.metadata?["combine_flag"]?.Value<bool>() == true) continue;
                    var si = pred.metadata?["slice_index"] as JArray;
                    if (si == null || si.Count == 0) continue;
                    var idx = si[0];
                    var baseKey = new Tuple<int, int>(idx[0].Value<int>(), idx[1].Value<int>());
                    var label = pred.category_id;
                    var gb = pred.metadata?["global_bbox"] as JArray; // [cx,cy,w,h,angle]
                    double cx = gb[0].Value<double>();
                    double cy = gb[1].Value<double>();
                    double w = gb[2].Value<double>();
                    double h = gb[3].Value<double>();
                    double a = gb[4].Value<double>();

                    foreach (var nb in GetNeighbors(baseKey))
                    {
                        string nk = $"{nb.Item1},{nb.Item2}";
                        if (!sliceToKey.ContainsKey(nk)) continue;
                        var nbKey = sliceToKey[nk];
                        var nbPreds = rd[nbKey].predictions;
                        foreach (var q in nbPreds.ToList())
                        {
                            var np = q.Value;
                            if (!(np.metadata?["is_rotated"]?.Value<bool>() ?? false)) continue;
                            if (np.category_id != label) continue;
                            var ngb = np.metadata?["global_bbox"] as JArray;
                            double rcx = ngb[0].Value<double>();
                            double rcy = ngb[1].Value<double>();
                            double rw = ngb[2].Value<double>();
                            double rh = ngb[3].Value<double>();
                            double ra = ngb[4].Value<double>();
                            double iou = RotatedIoU(cx, cy, w, h, a, rcx, rcy, rw, rh, ra);
                            if (iou > thr)
                            {
                                if (np.score > pred.score)
                                {
                                    pred.metadata["combine_flag"] = true;
                                }
                                else
                                {
                                    np.metadata["combine_flag"] = true;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    public class VisualizationModule : ProcessModule
    {
        public VisualizationModule(ModuleConfig cfg = null) : base(cfg) { }
        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            return Task.FromResult(inputs);
        }
    }

    public class SaveImageModule : ProcessModule
    {
        public SaveImageModule(ModuleConfig cfg = null) : base(cfg) { }
        public override Task<PipelineIO> Process(PipelineIO inputs, Func<double, Task> update = null)
        {
            return Task.FromResult(inputs);
        }
    }
}


