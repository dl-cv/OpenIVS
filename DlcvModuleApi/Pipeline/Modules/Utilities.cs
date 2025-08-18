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
                    CombineAxisAligned(rd, thr, inputs.current_round);
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

        private static List<double> GetXyxyFromMeta(JObject meta)
        {
            if (meta == null) return null;
            var xyxyTok = meta["global_bbox_xyxy"] as JArray;
            var tok = (JArray)(xyxyTok ?? meta["global_bbox"] as JArray);
            if (tok == null || tok.Count < 4) return null;
            var mode = meta["bbox_mode"]?.Value<string>() ?? "xyxy";
            if (xyxyTok != null || mode == "xyxy")
            {
                double ax1 = tok[0].Value<double>(), ay1 = tok[1].Value<double>(), ax2 = tok[2].Value<double>(), ay2 = tok[3].Value<double>();
                double nx1 = Math.Min(ax1, ax2);
                double ny1 = Math.Min(ay1, ay2);
                double nx2 = Math.Max(ax1, ax2);
                double ny2 = Math.Max(ay1, ay2);
                return new List<double> { nx1, ny1, nx2, ny2 };
            }
            else
            {
                // xywh -> xyxy
                double x = tok[0].Value<double>();
                double y = tok[1].Value<double>();
                double w = tok[2].Value<double>();
                double h = tok[3].Value<double>();
                double x1 = x;
                double y1 = y;
                double x2 = x + Math.Max(1.0, w);
                double y2 = y + Math.Max(1.0, h);
                return new List<double> { x1, y1, x2, y2 };
            }
        }

        private static void CombineAxisAligned(Dictionary<Tuple<int, int>, ResultEntry> rd, double thr, int targetRound)
        {
            // 1) 建立切片索引到结果键的映射（支持 [i,j] 和 [[i,j]] 两种格式），仅处理当前轮次
            var sliceToKey = new Dictionary<string, Tuple<int, int>>();
            foreach (var kv in rd)
            {
                if (kv.Value.current_round != targetRound) continue;
                if (kv.Value.predictions.Count == 0) continue;
                var firstPred = kv.Value.predictions.First().Value;
                var si = firstPred.metadata?["slice_index"] as JArray;
                if (si == null || si.Count == 0) continue;
                var idx = si; // 按用户要求保留该写法
                // 支持两种格式：[i,j] 或 [[i,j]]
                int sliceI, sliceJ;
                if (idx.Count >= 2 && idx[0].Type != JTokenType.Array)
                {
                    // 格式：[i,j]
                    sliceI = idx[0].Value<int>();
                    sliceJ = idx[1].Value<int>();
                }
                else if (idx.Count >= 1 && idx[0] is JArray idxPair && idxPair.Count >= 2)
                {
                    // 格式：[[i,j]]
                    sliceI = idxPair[0].Value<int>();
                    sliceJ = idxPair[1].Value<int>();
                }
                else continue;
                string key = $"{sliceI},{sliceJ}";
                sliceToKey[key] = kv.Key;
            }

            // 2) 收集所有预测节点，准备做连通分量合并（两两比较、同类且 IOS>thr 即连边），仅当前轮次
            var nodeResultKeys = new List<Tuple<int, int>>();
            var nodePredKeys = new List<int>();
            var nodeSliceKeys = new List<Tuple<int, int>>();
            var nodeLabels = new List<int>();
            var nodeScores = new List<float>();
            var nodeBboxes = new List<List<double>>();
            var nodeIndexMap = new Dictionary<string, int>(); // (rdKey.Item1,rdKey.Item2,predKey) -> idx

            foreach (var kv in rd)
            {
                if (kv.Value.current_round != targetRound) continue;
                var preds = kv.Value.predictions;
                foreach (var p in preds)
                {
                    var pred = p.Value;
                    var si = pred.metadata?["slice_index"] as JArray;
                    if (si == null || si.Count == 0) continue;
                    var idx = si; // 保留写法
                    // 支持两种格式：[i,j] 或 [[i,j]]
                    int sliceI, sliceJ;
                    if (idx.Count >= 2 && idx[0].Type != JTokenType.Array)
                    {
                        // 格式：[i,j]
                        sliceI = idx[0].Value<int>();
                        sliceJ = idx[1].Value<int>();
                    }
                    else if (idx.Count >= 1 && idx[0] is JArray idxPair && idxPair.Count >= 2)
                    {
                        // 格式：[[i,j]]
                        sliceI = idxPair[0].Value<int>();
                        sliceJ = idxPair[1].Value<int>();
                    }
                    else continue;
                    var baseKey = new Tuple<int, int>(sliceI, sliceJ);
                    var bbox = GetXyxyFromMeta(pred.metadata);
                    if (bbox == null) continue;
                    
                    int ni = nodeResultKeys.Count;
                    nodeResultKeys.Add(kv.Key);
                    nodePredKeys.Add(p.Key);
                    nodeSliceKeys.Add(baseKey);
                    nodeLabels.Add(pred.category_id);
                    nodeScores.Add(pred.score);
                    nodeBboxes.Add(bbox);
                    nodeIndexMap[$"{kv.Key.Item1},{kv.Key.Item2}:{p.Key}"] = ni;
                }
            }

            int n = nodeResultKeys.Count;
            if (n == 0) return;

            // 3) 并查集
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            Func<int, int> find = null;
            find = i => parent[i] == i ? i : (parent[i] = find(parent[i]));
            Action<int, int> unite = (a, b) =>
            {
                int ra = find(a), rb = find(b);
                if (ra != rb) parent[rb] = ra;
            };

            // 4) 对所有同类框进行两两比较（跨切片），构造连通关系
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (nodeLabels[i] != nodeLabels[j]) continue;
                    var ios = IOS(nodeBboxes[i], nodeBboxes[j]);
                    if (ios > thr)
                    {
                        unite(i, j);
                    }
                }
            }

            // 5) 每个连通分量仅保留最高分，其余标记 combine_flag=true（确保全局只输出一个）
            var bestInGroup = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                int r = find(i);
                if (!bestInGroup.ContainsKey(r) || nodeScores[i] > nodeScores[bestInGroup[r]])
                {
                    bestInGroup[r] = i;
                }
            }

            for (int i = 0; i < n; i++)
            {
                int r = find(i);
                bool keep = bestInGroup[r] == i;
                var rk = nodeResultKeys[i];
                var pk = nodePredKeys[i];
                var pred = rd[rk].predictions[pk];
                if (!keep)
                {
                    if (pred.metadata == null) pred.metadata = new JObject();
                    pred.metadata["combine_flag"] = true;
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


