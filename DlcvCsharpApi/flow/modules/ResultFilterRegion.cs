using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    /// <summary>
    /// 模块名称：结果过滤（区域）- 本地
    /// 按指定矩形区域将结果与图像拆分为两路输出（区域内 / 区域外）。
    ///
    /// 规则（对齐 Python: post_process/result_filter_region）：
    /// - “完全不在区域内”的结果将被分流到第二路（ExtraOutputs[0]）。
    /// - 若存在 mask_rle / mask_array：优先用 mask 与区域的像素级重叠判断（任意像素重叠即视为区域内）。
    /// - 若不存在 mask：使用 bbox 与区域的相交判断。
    /// - result_region 支持 any_bbox / top1_bbox 两种模式（由 result_region_mode 控制）。
    /// - 当 result_region 已连接但当前图像无有效 bbox 时，按“空区域”处理（全部判到区域外，不回退 x/y/w/h）。
    /// - 区域属性 x/y/w/h 默认认为处于“原图坐标”；若结果处于当前图坐标，会自动做一定的容错匹配。
    /// - 标量输出：has_positive（第一路是否存在任何结果）。
    /// </summary>
    public class ResultFilterRegion : BaseModule
    {
        static ResultFilterRegion()
        {
            ModuleRegistry.Register("post_process/result_filter_region", typeof(ResultFilterRegion));
            ModuleRegistry.Register("features/result_filter_region", typeof(ResultFilterRegion));
        }

        public ResultFilterRegion(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            return ProcessInternal(imageList, resultList, forceOriginalOverride: null, convertOutputToOriginalOverride: null);
        }

        /// <summary>
        /// 核心逻辑（可复用）：支持“强制按原图坐标判定”(forceOriginalOverride) 与 “是否把输出转换为原图坐标”(convertOutputToOriginalOverride)。
        /// - Local：override 均为 null -> 保持自动判定（与旧行为兼容）
        /// - Global：forceOriginalOverride=true 且 convertOutputToOriginalOverride=false -> 仅用原图坐标判定，但不改输出结果坐标系/transform
        /// </summary>
        protected ModuleIO ProcessInternal(
            List<ModuleImage> imageList,
            JArray resultList,
            bool? forceOriginalOverride,
            bool? convertOutputToOriginalOverride
        )
        {
            var images = imageList ?? new List<ModuleImage>();
            var results = resultList ?? new JArray();

            // 读取属性并规范化
            int x = ReadInt("x", 0);
            int y = ReadInt("y", 0);
            int w = Math.Max(1, ReadInt("w", 100));
            int h = Math.Max(1, ReadInt("h", 100));
            string resultRegionMode = ReadString("result_region_mode", "any_bbox");
            if (!string.Equals(resultRegionMode, "any_bbox", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(resultRegionMode, "top1_bbox", StringComparison.OrdinalIgnoreCase))
            {
                resultRegionMode = "any_bbox";
            }

            // ROI 规则（与 Python 最新一致）：
            // - ROI 的数值永远取输入的 x/y/w/h，不做 original<->current 的几何变换
            // - 仅根据本次选择的坐标系，对 ROI 做边界裁剪（clamp）
            // key: (wrapId, useOriginal) -> roi_xyxy
            var roiCache = new Dictionary<string, int[]>(StringComparer.Ordinal);
            int[] GetRoi(ModuleImage wrap, bool useOriginal, int Wc, int Hc, int W0, int H0)
            {
                int wrapId = wrap != null ? RuntimeHelpers.GetHashCode(wrap) : 0;
                string key = $"{wrapId}:{(useOriginal ? 1 : 0)}:{Wc}:{Hc}:{W0}:{H0}";
                if (roiCache.TryGetValue(key, out var cached)) return cached;
                int[] rr = useOriginal
                    ? ClampXYXY(x, y, x + w, y + h, W0, H0)
                    : ClampXYXY(x, y, x + w, y + h, Wc, Hc);
                roiCache[key] = rr;
                return rr;
            }

            // wrap 索引：transform 签名优先（对齐 Python：仅看 transform 本身，不依赖 origin/index），其次 index，再其次 origin_index
            var sigToWrap = new Dictionary<string, ModuleImage>(StringComparer.Ordinal);
            var originToWrap = new Dictionary<int, ModuleImage>();
            var indexToWrap = new Dictionary<int, ModuleImage>();
            var wrapIdToIndex = new Dictionary<int, int>();
            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null) continue;
                originToWrap[wrap.OriginalIndex] = wrap;
                indexToWrap[i] = wrap;
                wrapIdToIndex[RuntimeHelpers.GetHashCode(wrap)] = i;
                string sig = SerializeTransformSig(wrap.TransformState);
                if (!string.IsNullOrEmpty(sig))
                {
                    sigToWrap[sig] = wrap;
                }
            }

            bool maskMode = string.Equals(ReadString("filter_mode", "legacy"), "mask", StringComparison.OrdinalIgnoreCase);
            ModuleImage PickWrapForEntry(JObject entry)
            {
                if (entry == null) return null;
                int originIndex = entry["origin_index"]?.Value<int?>() ?? -1;
                int idx = entry["index"]?.Value<int?>() ?? -1;
                if (maskMode)
                {
                    // Mask 分支先限制原图，再按 transform/index 定位，避免分流重排或相同 transform 串图。
                    bool hasOrigin = entry["origin_index"] != null && entry["origin_index"].Type != JTokenType.Null;
                    var candidates = images.FindAll(wrap => wrap != null && (!hasOrigin || wrap.OriginalIndex == originIndex));
                    if (candidates.Count == 0) return null;
                    var indexed = indexToWrap.TryGetValue(idx, out var atIndex) ? atIndex : null;
                    string sig = SerializeTransformSig(entry["transform"] as JObject);
                    var matching = candidates.FindAll(wrap => sig != null && SerializeTransformSig(wrap.TransformState) == sig);
                    if (matching.Contains(indexed)) return indexed;
                    if (matching.Count == 1) return matching[0];
                    if (candidates.Contains(indexed)) return indexed;
                    if (hasOrigin) return candidates[0];
                    throw new ArgumentException("Mask mode requires origin_index or a valid image index");
                }

                try
                {
                    var stObj = entry["transform"] as JObject;
                    if (stObj != null)
                    {
                        string sig = SerializeTransformSig(stObj);
                        if (!string.IsNullOrEmpty(sig) && sigToWrap.TryGetValue(sig, out var w0)) return w0;
                    }
                }
                catch { }
                // Python 对齐：transform > index > origin_index
                if (idx >= 0 && indexToWrap.TryGetValue(idx, out var wIdx)) return wIdx;
                if (originIndex >= 0 && originToWrap.TryGetValue(originIndex, out var w1)) return w1;
                return null;
            }

            if (maskMode)
            {
                return ProcessMask(images, results, PickWrapForEntry);
            }

            // Python 对齐：不聚合为“每图一个 entry”，而是保留 entry 结构，仅拆分 sample_results。
            // 同时记录 entry 绑定的输入图像下标 oldIdx，便于分支输出时重排 index，避免二路错位。
            var insideEntries = new List<Tuple<JObject, int>>();
            var outsideEntries = new List<Tuple<JObject, int>>();
            var others = new List<JToken>(); // 非 local / 无法定位的条目：透传到两路，避免丢失
            // 记录每张图输入侧的 transform 形态（用于在不转换输出坐标系时，尽量保持 transform 与上游一致）
            // wrapId -> null(表示输入为原图坐标) / JObject(表示输入为局部坐标 transform)
            var inputTransformByWrapId = new Dictionary<int, JToken>();
            var inFlags = new bool[Math.Max(0, images.Count)];
            var outFlags = new bool[Math.Max(0, images.Count)];

            bool hasAnyInside = false;

            // -----------------------------
            // 坐标系处理策略（全局一次判定）：
            // 1) 若输入“待筛选结果”中存在原图坐标结果，则本次按原图坐标处理；
            //    注意：是否将输出也转换为原图坐标，由 convertOutputToOriginal 决定（默认与 forceOriginal 一致）。
            // 2) 若输入全部为局部坐标结果，则本次按局部/当前图坐标处理，不做转化
            //
            // 注意：C# 的 det["bbox"] 约定通常为 xywh（旋转框则 bbox=[cx,cy,w,h] + angle），
            // 本模块内部统一转为 AABB xyxy 做计算；若需要回写为原图坐标，输出仍按 xywh（AABB）回写。
            // -----------------------------
            bool forceOriginal = false;
            foreach (var token in results)
            {
                if (!(token is JObject entry) || !string.Equals(entry["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // entry 级别：transform==null 通常表示结果已处于原图坐标
                if (entry["transform"] == null || entry["transform"].Type == JTokenType.Null)
                {
                    forceOriginal = true;
                    break;
                }

                var dets = entry["sample_results"] as JArray;
                if (dets == null || dets.Count == 0) continue;

                var wrap = PickWrapForEntry(entry);
                if (wrap == null) continue;

                var baseImg = wrap.GetImage();
                int Wc = baseImg != null && !baseImg.Empty() ? baseImg.Width : 1;
                int Hc = baseImg != null && !baseImg.Empty() ? baseImg.Height : 1;
                int W0 = wrap.TransformState != null ? wrap.TransformState.OriginalWidth : Wc;
                int H0 = wrap.TransformState != null ? wrap.TransformState.OriginalHeight : Hc;
                if (W0 <= 0) W0 = Wc;
                if (H0 <= 0) H0 = Hc;

                foreach (var dTok in dets)
                {
                    if (!(dTok is JObject d)) continue;
                    if (!TryExtractBboxAabbCurrent(d, out double bx1, out double by1, out double bx2, out double by2)) continue;

                    var bboxCur = ClampXYXY(bx1, by1, bx2, by2, Wc, Hc);

                    // metadata.global_bbox 作为“原图坐标存在”的强证据（与 bboxCur 明显不同/或超出当前图范围）
                    int[] bboxMetaOri = null;
                    try
                    {
                        var meta = d["metadata"] as JObject;
                        var gb = meta != null ? (meta["global_bbox"] as JArray) : null;
                        if (gb != null && (gb.Count == 4 || gb.Count == 5))
                        {
                            bboxMetaOri = ParseGlobalBboxToAabb(gb);
                            if (bboxMetaOri != null)
                            {
                                bboxMetaOri = ClampXYXY(bboxMetaOri[0], bboxMetaOri[1], bboxMetaOri[2], bboxMetaOri[3], W0, H0);
                            }
                        }
                    }
                    catch { bboxMetaOri = null; }

                    if (bboxMetaOri != null)
                    {
                        try
                        {
                            // 1) bbox 明显不同
                            if (Math.Abs(bboxMetaOri[0] - bboxCur[0]) > 1 || Math.Abs(bboxMetaOri[1] - bboxCur[1]) > 1 ||
                                Math.Abs(bboxMetaOri[2] - bboxCur[2]) > 1 || Math.Abs(bboxMetaOri[3] - bboxCur[3]) > 1)
                            {
                                forceOriginal = true;
                                break;
                            }
                            // 2) meta bbox 明显超出当前图尺寸
                            if (bboxMetaOri[2] > Wc + 1 || bboxMetaOri[3] > Hc + 1)
                            {
                                forceOriginal = true;
                                break;
                            }
                        }
                        catch
                        {
                            forceOriginal = true;
                            break;
                        }
                    }

                    // bbox 数值范围超出当前图：也认为存在原图坐标结果
                    try
                    {
                        if (bx2 > Wc + 1 || by2 > Hc + 1)
                        {
                            forceOriginal = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (forceOriginal) break;
            }

            // override：与 Python 侧一致，只有在 override 显式给定时才覆盖自动判定
            if (forceOriginalOverride.HasValue)
            {
                forceOriginal = forceOriginalOverride.Value;
            }
            bool convertOutputToOriginal = convertOutputToOriginalOverride.HasValue
                ? convertOutputToOriginalOverride.Value
                : forceOriginal;

            // 读取第 3 输入口 result_region（C# 执行器按 pair 聚合：第 3 口对应 ExtraInputsIn[0]）
            var rrInput = ReadResultRegionInput();
            bool resultRegionConnected = rrInput.Item1;
            var resultRegionResults = rrInput.Item2 ?? new JArray();
            var regionBoxesByWrapIndex = new Dictionary<int, List<int[]>>();
            if (resultRegionConnected)
            {
                var regionCandidatesByWrapIndex = new Dictionary<int, List<Tuple<int[], double?>>>();
                foreach (var token in resultRegionResults)
                {
                    if (!(token is JObject regionEntry) ||
                        !string.Equals(regionEntry["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var regionDets = regionEntry["sample_results"] as JArray;
                    if (regionDets == null || regionDets.Count == 0) continue;

                    var regionWrap = PickWrapForEntry(regionEntry);
                    if (regionWrap == null) continue;

                    int regionWrapId = RuntimeHelpers.GetHashCode(regionWrap);
                    if (!wrapIdToIndex.TryGetValue(regionWrapId, out int regionWrapIdx) || regionWrapIdx < 0) continue;

                    var regionBaseImg = regionWrap.GetImage();
                    int regionWc = regionBaseImg != null && !regionBaseImg.Empty() ? regionBaseImg.Width : 1;
                    int regionHc = regionBaseImg != null && !regionBaseImg.Empty() ? regionBaseImg.Height : 1;
                    int regionW0 = regionWrap.TransformState != null ? regionWrap.TransformState.OriginalWidth : regionWc;
                    int regionH0 = regionWrap.TransformState != null ? regionWrap.TransformState.OriginalHeight : regionHc;
                    if (regionW0 <= 0) regionW0 = regionWc;
                    if (regionH0 <= 0) regionH0 = regionHc;

                    foreach (var dTok in regionDets)
                    {
                        if (!(dTok is JObject d)) continue;
                        if (!TryExtractBboxAabbCurrent(d, out double rbx1, out double rby1, out double rbx2, out double rby2)) continue;

                        var bboxCur = ClampXYXY(rbx1, rby1, rbx2, rby2, regionWc, regionHc);
                        int[] bboxUse = bboxCur;
                        if (forceOriginal)
                        {
                            int[] bboxMetaOri = null;
                            try
                            {
                                var meta = d["metadata"] as JObject;
                                var gb = meta != null ? (meta["global_bbox"] as JArray) : null;
                                if (gb != null && (gb.Count == 4 || gb.Count == 5))
                                {
                                    bboxMetaOri = ParseGlobalBboxToAabb(gb);
                                    if (bboxMetaOri != null)
                                    {
                                        bboxMetaOri = ClampXYXY(bboxMetaOri[0], bboxMetaOri[1], bboxMetaOri[2], bboxMetaOri[3], regionW0, regionH0);
                                    }
                                }
                            }
                            catch { bboxMetaOri = null; }

                            if (bboxMetaOri != null)
                            {
                                bboxUse = bboxMetaOri;
                            }
                            else
                            {
                                try
                                {
                                    var mapped = MapAabbToOriginalAndClamp(regionWrap.TransformState, bboxCur, regionW0, regionH0);
                                    if (mapped != null) bboxUse = mapped;
                                }
                                catch { }
                            }
                        }

                        if (!regionCandidatesByWrapIndex.TryGetValue(regionWrapIdx, out var cands))
                        {
                            cands = new List<Tuple<int[], double?>>();
                            regionCandidatesByWrapIndex[regionWrapIdx] = cands;
                        }
                        cands.Add(Tuple.Create(bboxUse, TryExtractScore(d)));
                    }
                }

                foreach (var kv in regionCandidatesByWrapIndex)
                {
                    var cands = kv.Value;
                    if (cands == null || cands.Count == 0) continue;

                    if (string.Equals(resultRegionMode, "top1_bbox", StringComparison.OrdinalIgnoreCase))
                    {
                        Tuple<int[], double?> best = null;
                        foreach (var cand in cands)
                        {
                            if (cand == null || cand.Item1 == null) continue;
                            if (!cand.Item2.HasValue) continue;
                            double s = cand.Item2.Value;
                            if (double.IsNaN(s) || double.IsInfinity(s)) continue;
                            if (best == null || s > best.Item2.GetValueOrDefault(double.MinValue))
                            {
                                best = cand;
                            }
                        }

                        if (best != null)
                        {
                            regionBoxesByWrapIndex[kv.Key] = new List<int[]> { best.Item1 };
                        }
                        else
                        {
                            foreach (var cand in cands)
                            {
                                if (cand != null && cand.Item1 != null)
                                {
                                    regionBoxesByWrapIndex[kv.Key] = new List<int[]> { cand.Item1 };
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        var boxes = new List<int[]>();
                        foreach (var cand in cands)
                        {
                            if (cand != null && cand.Item1 != null) boxes.Add(cand.Item1);
                        }
                        if (boxes.Count > 0)
                        {
                            regionBoxesByWrapIndex[kv.Key] = boxes;
                        }
                    }
                }
            }

            JObject ConvertDetToOriginal(JObject det, int[] bboxOriXYXY)
            {
                if (det == null || bboxOriXYXY == null || bboxOriXYXY.Length < 4) return det;
                var d2 = det.DeepClone() as JObject;
                if (d2 == null) return det;

                int ox1 = bboxOriXYXY[0];
                int oy1 = bboxOriXYXY[1];
                int ox2 = bboxOriXYXY[2];
                int oy2 = bboxOriXYXY[3];
                int ow = Math.Max(1, ox2 - ox1);
                int oh = Math.Max(1, oy2 - oy1);

                // C# bbox 约定：默认 xywh（轴对齐）；旋转框也允许但这里输出 AABB xywh，与 Python 一致（过滤模块允许损失旋转语义）
                d2["bbox"] = new JArray { ox1, oy1, ow, oh };

                // 同步 metadata.global_bbox（对齐 Python：优先写 xyxy；同时兼容 xywh 解析）
                try
                {
                    var meta = d2["metadata"] as JObject;
                    if (meta == null)
                    {
                        meta = new JObject();
                        d2["metadata"] = meta;
                    }
                    meta["global_bbox"] = new JArray { ox1, oy1, ox2, oy2 };
                }
                catch { }
                return d2;
            }

            foreach (var token in results)
            {
                var entry = token as JObject;
                if (entry == null)
                {
                    others.Add(token);
                    continue;
                }
                if (!string.Equals(entry["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase))
                {
                    others.Add(entry);
                    continue;
                }

                var dets = entry["sample_results"] as JArray;
                if (dets == null)
                {
                    others.Add(entry);
                    continue;
                }

                var wrap = PickWrapForEntry(entry);
                if (wrap == null)
                {
                    others.Add(entry);
                    continue;
                }

                int wrapId = RuntimeHelpers.GetHashCode(wrap);
                int oldIdx = -1;
                if (!wrapIdToIndex.TryGetValue(wrapId, out oldIdx)) oldIdx = -1;
                // 记录输入 transform（首次出现为准；若某 wrap 同时出现 null 与非 null，这本身就是数据不一致，优先保留 null）
                if (!inputTransformByWrapId.TryGetValue(wrapId, out var existingTransform))
                {
                    var tTok = entry["transform"];
                    inputTransformByWrapId[wrapId] = tTok != null ? tTok.DeepClone() : null;
                }
                else
                {
                    // 若已记录为 null，则不再覆盖；否则若当前 entry 为 null，则降级为 null（更保守，避免输出 transform 伪造）
                    var tTok = entry["transform"];
                    bool existingIsNull = existingTransform == null || existingTransform.Type == JTokenType.Null;
                    bool currentIsNull = (tTok == null) || (tTok.Type == JTokenType.Null);
                    if (!existingIsNull && currentIsNull)
                    {
                        inputTransformByWrapId[wrapId] = null;
                    }
                }

                var baseImg = wrap.GetImage();
                int Wc = baseImg != null && !baseImg.Empty() ? baseImg.Width : 1;
                int Hc = baseImg != null && !baseImg.Empty() ? baseImg.Height : 1;
                int W0 = wrap.TransformState != null ? wrap.TransformState.OriginalWidth : Wc;
                int H0 = wrap.TransformState != null ? wrap.TransformState.OriginalHeight : Hc;
                if (W0 <= 0) W0 = Wc;
                if (H0 <= 0) H0 = Hc;

                // useOriginal：仅用于“判定”使用的坐标系
                bool useOriginal = forceOriginal;
                var fallbackRoi = GetRoi(wrap, useOriginal, Wc, Hc, W0, H0);
                List<int[]> activeRois = null;
                bool forceOutsideByEmptyRegion = false;
                if (resultRegionConnected)
                {
                    if (oldIdx >= 0 &&
                        regionBoxesByWrapIndex.TryGetValue(oldIdx, out var regionRois) &&
                        regionRois != null &&
                        regionRois.Count > 0)
                    {
                        activeRois = regionRois;
                    }
                    else
                    {
                        // 已连接 result_region 且当前图无有效 bbox：按“空区域”语义处理
                        forceOutsideByEmptyRegion = true;
                    }
                }
                else
                {
                    activeRois = new List<int[]> { fallbackRoi };
                }
                int spaceW = useOriginal ? W0 : Wc;
                int spaceH = useOriginal ? H0 : Hc;

                var inArr = new JArray();
                var outArr = new JArray();
                foreach (var dTok in dets)
                {
                    if (!(dTok is JObject d))
                    {
                        outArr.Add(dTok);
                        continue;
                    }

                    // 提取 bbox（当前坐标系下的 AABB）
                    if (!TryExtractBboxAabbCurrent(d, out double bx1, out double by1, out double bx2, out double by2))
                    {
                        // 异常项：按区域外处理
                        outArr.Add(d);
                        continue;
                    }

                    var bboxCur = ClampXYXY(bx1, by1, bx2, by2, Wc, Hc);
                    int[] bboxUse = bboxCur;
                    int[] bboxOri = null;
                    if (useOriginal)
                    {
                        // 原图模式：优先使用 metadata.global_bbox（若存在），否则从 bboxCur 反算到原图坐标
                        int[] bboxMetaOri = null;
                        try
                        {
                            var meta = d["metadata"] as JObject;
                            var gb = meta != null ? (meta["global_bbox"] as JArray) : null;
                            if (gb != null && (gb.Count == 4 || gb.Count == 5))
                            {
                                bboxMetaOri = ParseGlobalBboxToAabb(gb);
                                if (bboxMetaOri != null) bboxMetaOri = ClampXYXY(bboxMetaOri[0], bboxMetaOri[1], bboxMetaOri[2], bboxMetaOri[3], W0, H0);
                            }
                        }
                        catch { bboxMetaOri = null; }

                        if (bboxMetaOri != null)
                        {
                            bboxOri = bboxMetaOri;
                        }
                        else
                        {
                            try { bboxOri = MapAabbToOriginalAndClamp(wrap.TransformState, bboxCur, W0, H0); } catch { bboxOri = null; }
                        }

                        bboxUse = bboxOri ?? bboxCur;
                    }

                    // 判定：mask 优先，其次 bbox
                    bool isIn = false;
                    bool decided = false;
                    if (forceOutsideByEmptyRegion)
                    {
                        // 空区域语义：不回退 fallback ROI，直接判外
                        decided = true;
                        isIn = false;
                    }

                    // mask_rle
                    var maskRle = d["mask_rle"];
                    if (!decided && maskRle != null)
                    {
                        try
                        {
                            using (var maskMat0 = MaskRleUtils.MaskInfoToMat(maskRle))
                            {
                                if (maskMat0 != null && !maskMat0.Empty())
                                {
                                    isIn = false;
                                    if (activeRois != null)
                                    {
                                        foreach (var roi in activeRois)
                                        {
                                            if (roi == null || roi.Length < 4) continue;
                                            if (CheckMaskOverlapWithRegion(maskMat0, bboxUse, roi, spaceW, spaceH))
                                            {
                                                isIn = true;
                                                break;
                                            }
                                        }
                                    }
                                    decided = true;
                                }
                            }
                        }
                        catch { decided = false; }
                    }

                    // mask_array（尽力支持；解析失败则忽略）
                    if (!decided && d["mask_array"] != null)
                    {
                        try
                        {
                            using (var maskMat = TryParseMaskArrayToMat(d["mask_array"]))
                            {
                                if (maskMat != null && !maskMat.Empty())
                                {
                                    isIn = false;
                                    if (activeRois != null)
                                    {
                                        foreach (var roi in activeRois)
                                        {
                                            if (roi == null || roi.Length < 4) continue;
                                            if (CheckMaskOverlapWithRegion(maskMat, bboxUse, roi, spaceW, spaceH))
                                            {
                                                isIn = true;
                                                break;
                                            }
                                        }
                                    }
                                    decided = true;
                                }
                            }
                        }
                        catch { decided = false; }
                    }

                    if (!decided)
                    {
                        // bbox 相交判定
                        isIn = false;
                        if (activeRois != null)
                        {
                            foreach (var roi in activeRois)
                            {
                                if (roi == null || roi.Length < 4) continue;
                                if (BboxIntersects(bboxUse, roi))
                                {
                                    isIn = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (isIn)
                    {
                        // 关键修复（对齐 Python 最新）：
                        // - 判定可以按原图坐标（useOriginal=true）
                        // - 但只有在 convertOutputToOriginal=true 时才允许改写输出 det/bbox/transform
                        inArr.Add((convertOutputToOriginal && useOriginal) ? ConvertDetToOriginal(d, bboxUse) : d);
                        hasAnyInside = true;
                    }
                    else
                    {
                        outArr.Add((convertOutputToOriginal && useOriginal) ? ConvertDetToOriginal(d, bboxUse) : d);
                    }
                }

                if (inArr.Count > 0)
                {
                    var e2 = entry.DeepClone() as JObject;
                    if (e2 != null)
                    {
                        e2["sample_results"] = inArr;
                        if (convertOutputToOriginal && useOriginal)
                        {
                            e2["transform"] = null;
                            // 与 Python 一致：输出为原图坐标时，把 origin_index 绑定到该图（更稳定）
                            e2["origin_index"] = wrap.OriginalIndex;
                        }
                        insideEntries.Add(Tuple.Create(e2, oldIdx));
                        if (oldIdx >= 0 && oldIdx < inFlags.Length) inFlags[oldIdx] = true;
                    }
                }
                if (outArr.Count > 0)
                {
                    var e3 = entry.DeepClone() as JObject;
                    if (e3 != null)
                    {
                        e3["sample_results"] = outArr;
                        if (convertOutputToOriginal && useOriginal)
                        {
                            e3["transform"] = null;
                            e3["origin_index"] = wrap.OriginalIndex;
                        }
                        outsideEntries.Add(Tuple.Create(e3, oldIdx));
                        if (oldIdx >= 0 && oldIdx < outFlags.Length) outFlags[oldIdx] = true;
                    }
                }
            }

            // 构建输出 image_list（子集）
            var outImagesIn = new List<ModuleImage>();
            var outImagesOut = new List<ModuleImage>();
            for (int i = 0; i < images.Count; i++)
            {
                var wrap = images[i];
                if (wrap == null) continue;
                if (i >= 0 && i < inFlags.Length && inFlags[i]) outImagesIn.Add(wrap);
                if (i >= 0 && i < outFlags.Length && outFlags[i]) outImagesOut.Add(wrap);
            }

            // 分支内重排 index（避免二路错位）
            var inReindex = new Dictionary<int, int>();
            var outReindex = new Dictionary<int, int>();
            int ptr = 0;
            for (int i = 0; i < inFlags.Length; i++) if (inFlags[i]) inReindex[i] = ptr++;
            ptr = 0;
            for (int i = 0; i < outFlags.Length; i++) if (outFlags[i]) outReindex[i] = ptr++;

            var outResultsIn = new JArray();
            foreach (var tup in insideEntries)
            {
                var e = tup.Item1;
                int oldIdx = tup.Item2;
                if (e == null) continue;
                if (oldIdx >= 0 && inReindex.TryGetValue(oldIdx, out int newIdx))
                {
                    e["index"] = newIdx;
                }
                else
                {
                    // 若无法定位 oldIdx，则尽量保持原 index（不强行改）
                }
                outResultsIn.Add(e);
            }

            var outResultsOut = new JArray();
            foreach (var tup in outsideEntries)
            {
                var e = tup.Item1;
                int oldIdx = tup.Item2;
                if (e == null) continue;
                if (oldIdx >= 0 && outReindex.TryGetValue(oldIdx, out int newIdx))
                {
                    e["index"] = newIdx;
                }
                outResultsOut.Add(e);
            }

            // 透传非 local 条目到两路（避免丢失）
            if (others.Count > 0)
            {
                foreach (var o in others)
                {
                    outResultsIn.Add(o);
                    outResultsOut.Add(o);
                }
            }

            // 第二路通过 extra output 暴露
            this.ExtraOutputs.Add(new ModuleChannel(outImagesOut, outResultsOut));

            // 标量输出
            try
            {
                this.ScalarOutputsByName["has_positive"] = hasAnyInside;
            }
            catch { }

            return new ModuleIO(outImagesIn, outResultsIn);
        }

        // Mask 模式单独分支，保留旧 ROI 行为；临时 mask 不写回输入结果。
        private sealed class RegionMask
        {
            public int X, Y, Width, Height, Area;
            public byte[] Pixels;
        }

        private ModuleIO ProcessMask(List<ModuleImage> images, JArray results, Func<JObject, ModuleImage> pickWrap)
        {
            var input = ReadResultRegionInput();
            if (!input.Item1) throw new ArgumentException("Mask mode requires result_region input");
            string metric = ReadString("metric", "ios").ToLowerInvariant();
            if (metric != "ios" && metric != "iou") throw new ArgumentException("Mask metric must be ios or iou");
            double threshold = Properties.ContainsKey("overlap_threshold")
                ? Convert.ToDouble(Properties["overlap_threshold"], System.Globalization.CultureInfo.InvariantCulture) : 0.5;
            if (!IsFinite(threshold) || threshold < 0 || threshold > 1)
                throw new ArgumentException("Mask overlap_threshold must be in [0, 1]");
            bool top1 = string.Equals(ReadString("result_region_mode", "any_bbox"), "top1_bbox", StringComparison.OrdinalIgnoreCase);
            var regions = new Dictionary<int, List<RegionMask>>();
            var scores = new Dictionary<int, double>();
            foreach (var token in input.Item2)
            {
                var entry = token as JObject;
                if (entry == null || !string.Equals(entry["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase)) continue;
                var wrap = pickWrap(entry);
                if (wrap == null) continue;
                foreach (var det in (entry["sample_results"] as JArray) ?? new JArray())
                {
                    if (!(det is JObject obj)) continue;
                    var mask = PrepareRegionMask(obj, entry, wrap);
                    if (mask == null) continue;
                    int origin = wrap.OriginalIndex;
                    double score = TryExtractScore(obj) ?? double.NegativeInfinity;
                    if (!regions.TryGetValue(origin, out var list))
                    {
                        list = new List<RegionMask>();
                        regions[origin] = list;
                        scores[origin] = double.NegativeInfinity;
                    }
                    if (top1 && list.Count > 0 && score <= scores[origin]) continue;
                    if (top1) list.Clear();
                    list.Add(mask);
                    scores[origin] = score;
                }
            }
            var branchEntries = new[] { new List<Tuple<JObject, int>>(), new List<Tuple<JObject, int>>() };
            var flags = new[] { new bool[images.Count], new bool[images.Count] };
            var others = new JArray();
            foreach (var token in results)
            {
                var entry = token as JObject;
                if (entry == null || !string.Equals(entry["type"]?.ToString(), "local", StringComparison.OrdinalIgnoreCase) || !(entry["sample_results"] is JArray dets))
                { others.Add(token.DeepClone()); continue; }
                var wrap = pickWrap(entry);
                if (wrap == null) throw new ArgumentException("Mask mode cannot locate target image");
                int index = images.IndexOf(wrap);
                var split = new[] { new JArray(), new JArray() };
                foreach (var tokenDet in dets)
                {
                    if (!(tokenDet is JObject det)) continue;
                    var mask = PrepareRegionMask(det, entry, wrap);
                    bool inside = mask != null && regions.TryGetValue(wrap.OriginalIndex, out var list)
                        && list.Exists(region => RegionMasksMatch(mask, region, metric, threshold));
                    split[inside ? 0 : 1].Add(det.DeepClone());
                }
                for (int branch = 0; branch < 2; branch++)
                {
                    if (split[branch].Count == 0) continue;
                    var copy = (JObject)entry.DeepClone();
                    copy["sample_results"] = split[branch];
                    branchEntries[branch].Add(Tuple.Create(copy, index));
                    flags[branch][index] = true;
                }
            }
            var outputs = new ModuleIO[2];
            for (int branch = 0; branch < 2; branch++)
            {
                var branchImages = new List<ModuleImage>();
                var indices = new Dictionary<int, int>();
                for (int i = 0; i < images.Count; i++)
                    if (flags[branch][i]) { indices[i] = branchImages.Count; branchImages.Add(images[i]); }
                var entries = new JArray();
                foreach (var item in branchEntries[branch])
                { item.Item1["index"] = indices[item.Item2]; entries.Add(item.Item1); }
                foreach (var other in others) entries.Add(other.DeepClone());
                outputs[branch] = new ModuleIO(branchImages, entries);
            }
            ExtraOutputs.Add(new ModuleChannel(outputs[1].ImageList, outputs[1].ResultList));
            ScalarOutputsByName["has_positive"] = branchEntries[0].Count > 0;
            return outputs[0];
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static RegionMask PrepareRegionMask(JObject det, JObject entry, ModuleImage wrap)
        {
            // SDK RLE 解码为 0/255；JSON mask_array 严格沿用 Python 的 >127 前景阈值。
            var raw = det["mask_array"];
            using (var mask = new Mat())
            {
                if (raw != null && raw.Type != JTokenType.Null)
                {
                    if (!(raw is JArray rows)) throw new ArgumentException("Mask array must be two-dimensional uint8");
                    if (rows.Count == 0) return null;
                    if (!(rows[0] is JArray first)) throw new ArgumentException("Mask array must be two-dimensional uint8");
                    if (first.Count == 0) return null;
                    mask.Create(rows.Count, first.Count, MatType.CV_8UC1);
                    for (int y = 0; y < rows.Count; y++)
                    {
                        if (!(rows[y] is JArray row) || row.Count != first.Count) throw new ArgumentException("Invalid mask row");
                        for (int x = 0; x < row.Count; x++)
                        {
                            double v = row[x].Value<double>();
                            if (!IsFinite(v) || v < 0 || v > 255 || v != Math.Floor(v)) throw new ArgumentException("Mask array must be uint8");
                            mask.Set(y, x, (byte)(v > 127 ? 255 : 0));
                        }
                    }
                }
                else if (det["mask_rle"] != null && det["mask_rle"].Type != JTokenType.Null)
                {
                    using (var decoded = MaskRleUtils.MaskInfoToMat(det["mask_rle"]))
                    {
                        if (decoded == null || decoded.Empty()) return null;
                        Cv2.Threshold(decoded, mask, 127, 255, ThresholdTypes.Binary);
                    }
                }
                else return null;
                if (Cv2.CountNonZero(mask) == 0) return null;
                int W = wrap.TransformState.OriginalWidth, H = wrap.TransformState.OriginalHeight;
                int sourceW = W, sourceH = H;
                double[] inv = { 1, 0, 0, 0, 1, 0 };
                var transform = entry["transform"];
                if (transform != null && transform.Type != JTokenType.Null)
                {
                    if (!(transform is JObject t)) throw new ArgumentException("Invalid mask transform");
                    // Native affine_2x3 已包含裁剪；Python affine_matrix 则需组合 crop_box。
                    bool native = t["original_width"] != null;
                    int tw = native ? t["original_width"].Value<int>() : t["original_size"][0].Value<int>();
                    int th = native ? t["original_height"].Value<int>() : t["original_size"][1].Value<int>();
                    if (tw != W || th != H) throw new ArgumentException("Mask original sizes differ");
                    var os = t["output_size"] as JArray;
                    sourceW = os == null && native ? W : os[0].Value<int>();
                    sourceH = os == null && native ? H : os[1].Value<int>();
                    double[] a = { 1, 0, 0, 0, 1, 0 };
                    if (native)
                    {
                        if (t["affine_2x3"] is JArray flat) a = flat.ToObject<double[]>();
                    }
                    else
                    {
                        var rows = (JArray)t["affine_matrix"];
                        a = new[] { rows[0][0].Value<double>(), rows[0][1].Value<double>(), rows[0][2].Value<double>(),
                            rows[1][0].Value<double>(), rows[1][1].Value<double>(), rows[1][2].Value<double>() };
                        double cx = t["crop_box"][0].Value<double>(), cy = t["crop_box"][1].Value<double>();
                        a[2] -= a[0] * cx + a[1] * cy; a[5] -= a[3] * cx + a[4] * cy;
                    }
                    if (a.Length != 6 || Array.Exists(a, v => !IsFinite(v))) throw new ArgumentException("Invalid mask affine");
                    double determinant = a[0] * a[4] - a[1] * a[3];
                    if (determinant == 0 || !IsFinite(determinant)) throw new ArgumentException("Singular mask transform");
                    inv = new[] { a[4] / determinant, -a[1] / determinant, (a[1] * a[5] - a[4] * a[2]) / determinant,
                        -a[3] / determinant, a[0] / determinant, (a[3] * a[2] - a[0] * a[5]) / determinant };
                    if (Array.Exists(inv, v => !IsFinite(v))) throw new ArgumentException("Invalid inverse mask transform");
                }
                if (Math.Min(Math.Min(W, H), Math.Min(sourceW, sourceH)) <= 0) throw new ArgumentException("Invalid mask image size");
                int x1 = 0, y1 = 0, x2 = sourceW, y2 = sourceH;
                if (mask.Cols != sourceW || mask.Rows != sourceH)
                {
                    if (!(det["bbox"] is JArray bbox) || bbox.Count < 4 || bbox[2].Value<double>() <= 0 || bbox[3].Value<double>() <= 0
                        || !TryExtractBboxAabbCurrent(det, out double bx1, out double by1, out double bx2, out double by2)
                        || !IsFinite(bx1) || !IsFinite(by1) || !IsFinite(bx2) || !IsFinite(by2))
                        throw new ArgumentException("Local mask requires a valid XYWH bbox");
                    x1 = (int)Math.Floor(bx1); y1 = (int)Math.Floor(by1);
                    x2 = (int)Math.Ceiling(bx2); y2 = (int)Math.Ceiling(by2);
                    if (mask.Cols != x2 - x1 || mask.Rows != y2 - y1)
                        Cv2.Resize(mask, mask, new Size(x2 - x1, y2 - y1), 0, 0, InterpolationFlags.Nearest);
                }
                int sx = Math.Max(0, x1), sy = Math.Max(0, y1);
                int ex = Math.Min(sourceW, x2), ey = Math.Min(sourceH, y2);
                if (ex <= sx || ey <= sy) return null;
                using (var clipped = new Mat(mask, new Rect(sx - x1, sy - y1, ex - sx, ey - sy)))
                using (var aligned = new Mat())
                using (var mapping = new Mat(2, 3, MatType.CV_64FC1))
                {
                    inv[2] += inv[0] * sx + inv[1] * sy; inv[5] += inv[3] * sx + inv[4] * sy;
                    double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
                    foreach (double px in new[] { -0.5, clipped.Cols - 0.5 })
                    foreach (double py in new[] { -0.5, clipped.Rows - 0.5 })
                    {
                        double ox = inv[0] * px + inv[1] * py + inv[2], oy = inv[3] * px + inv[4] * py + inv[5];
                        minX = Math.Min(minX, ox); maxX = Math.Max(maxX, ox); minY = Math.Min(minY, oy); maxY = Math.Max(maxY, oy);
                    }
                    int ox1 = Math.Max(0, (int)Math.Floor(minX + 0.5)), oy1 = Math.Max(0, (int)Math.Floor(minY + 0.5));
                    int ox2 = Math.Min(W, (int)Math.Ceiling(maxX + 0.5)), oy2 = Math.Min(H, (int)Math.Ceiling(maxY + 0.5));
                    if (ox2 <= ox1 || oy2 <= oy1) return null;
                    inv[2] -= ox1; inv[5] -= oy1;
                    for (int i = 0; i < 6; i++) mapping.Set(i / 3, i % 3, inv[i]);
                    Cv2.WarpAffine(clipped, aligned, mapping, new Size(ox2 - ox1, oy2 - oy1), InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.Black);
                    int area = Cv2.CountNonZero(aligned);
                    if (area == 0) return null;
                    var pixels = new byte[aligned.Rows * aligned.Cols];
                    System.Runtime.InteropServices.Marshal.Copy(aligned.Data, pixels, 0, pixels.Length);
                    return new RegionMask { X = ox1, Y = oy1, Width = aligned.Cols, Height = aligned.Rows, Area = area, Pixels = pixels };
                }
            }
        }

        private static bool RegionMasksMatch(RegionMask a, RegionMask b, string metric, double threshold)
        {
            int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
            int x2 = Math.Min(a.X + a.Width, b.X + b.Width), y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
            long intersection = 0;
            for (int y = y1; y < y2; y++)
                for (int x = x1; x < x2; x++)
                    if ((a.Pixels[(y - a.Y) * a.Width + x - a.X] & b.Pixels[(y - b.Y) * b.Width + x - b.X]) != 0) intersection++;
            double denominator = metric == "ios" ? Math.Min(a.Area, b.Area) : (double)a.Area + b.Area - intersection;
            return intersection > 0 && intersection / denominator >= threshold;
        }

        private int ReadInt(string key, int dv)
        {
            try
            {
                if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
                {
                    return Convert.ToInt32(Convert.ToDouble(v));
                }
            }
            catch { }
            return dv;
        }

        private string ReadString(string key, string dv)
        {
            try
            {
                if (Properties != null && Properties.TryGetValue(key, out object v) && v != null)
                {
                    var s = v.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
            }
            catch { }
            return dv;
        }

        private Tuple<bool, JArray> ReadResultRegionInput()
        {
            if (ExtraInputsIn == null || ExtraInputsIn.Count <= 0)
            {
                return Tuple.Create(false, new JArray());
            }
            try
            {
                var ch = ExtraInputsIn[0];
                return Tuple.Create(true, ch?.ResultList ?? new JArray());
            }
            catch
            {
                return Tuple.Create(true, new JArray());
            }
        }

        private static double? TryExtractScore(JObject det)
        {
            if (det == null) return null;
            string[] keys = new[] { "score", "conf", "confidence" };
            foreach (var key in keys)
            {
                try
                {
                    var tok = det[key];
                    if (tok == null || tok.Type == JTokenType.Null) continue;
                    double v = Convert.ToDouble((tok as JValue)?.Value ?? tok.ToString(), System.Globalization.CultureInfo.InvariantCulture);
                    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                    return v;
                }
                catch { }
            }
            return null;
        }

        // 对齐 Python: _serialize_transform(transform_dict) 的“稳定签名”
        // - 不依赖 index/origin_index，仅依赖 transform 本身
        // - 只取关键字段（crop_box / output_size / original_size / affine）并做浮点 round(6)
        private static string SerializeTransformSig(TransformationState st)
        {
            if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length < 6)
            {
                return null;
            }
            int cbx = 0, cby = 0, cbw = 0, cbh = 0;
            if (st.CropBox != null && st.CropBox.Length >= 4)
            {
                cbx = st.CropBox[0]; cby = st.CropBox[1]; cbw = st.CropBox[2]; cbh = st.CropBox[3];
            }
            int ow = st.OriginalWidth;
            int oh = st.OriginalHeight;
            int outW = 0, outH = 0;
            if (st.OutputSize != null && st.OutputSize.Length >= 2)
            {
                outW = st.OutputSize[0]; outH = st.OutputSize[1];
            }
            double[] a = st.AffineMatrix2x3;
            return $"cb:{cbx},{cby},{cbw},{cbh}|os:{outW},{outH}|ori:{ow},{oh}|A:{Round6(a[0])},{Round6(a[1])},{Round6(a[2])},{Round6(a[3])},{Round6(a[4])},{Round6(a[5])}";
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
                    cbx = cb[0].Value<int>(); cby = cb[1].Value<int>(); cbw = cb[2].Value<int>(); cbh = cb[3].Value<int>();
                }

                int outW = 0, outH = 0;
                var os = stObj["output_size"] as JArray;
                if (os != null && os.Count >= 2)
                {
                    outW = os[0].Value<int>(); outH = os[1].Value<int>();
                }

                int ow = 0, oh = 0;
                // 兼容 original_size: [W,H] 与 original_width/original_height
                var ori = stObj["original_size"] as JArray;
                if (ori != null && ori.Count >= 2)
                {
                    ow = ori[0].Value<int>(); oh = ori[1].Value<int>();
                }
                else
                {
                    ow = stObj["original_width"]?.Value<int?>() ?? 0;
                    oh = stObj["original_height"]?.Value<int?>() ?? 0;
                }

                // affine：兼容 affine_2x3(flat) 与 affine_matrix(2x3 rows)
                double[] a = null;
                var a23 = stObj["affine_2x3"] as JArray;
                if (a23 != null && a23.Count >= 6)
                {
                    a = new double[6];
                    for (int i = 0; i < 6; i++) a[i] = SafeToDouble(a23[i]);
                }
                else
                {
                    var amat = stObj["affine_matrix"] as JArray;
                    if (amat != null && amat.Count >= 2 && amat[0] is JArray r0 && amat[1] is JArray r1 && r0.Count >= 3 && r1.Count >= 3)
                    {
                        a = new double[6];
                        a[0] = SafeToDouble(r0[0]); a[1] = SafeToDouble(r0[1]); a[2] = SafeToDouble(r0[2]);
                        a[3] = SafeToDouble(r1[0]); a[4] = SafeToDouble(r1[1]); a[5] = SafeToDouble(r1[2]);
                    }
                }

                if (a == null || a.Length < 6) return null;
                return $"cb:{cbx},{cby},{cbw},{cbh}|os:{outW},{outH}|ori:{ow},{oh}|A:{Round6(a[0])},{Round6(a[1])},{Round6(a[2])},{Round6(a[3])},{Round6(a[4])},{Round6(a[5])}";
            }
            catch
            {
                return null;
            }
        }

        private static string Round6(double v)
        {
            try { return Math.Round(v, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture); }
            catch { return "0"; }
        }

        private static int[] ClampXYXY(double x1, double y1, double x2, double y2, int W, int H)
        {
            W = Math.Max(1, W);
            H = Math.Max(1, H);

            double a1 = Math.Min(x1, x2);
            double a2 = Math.Max(x1, x2);
            double b1 = Math.Min(y1, y2);
            double b2 = Math.Max(y1, y2);

            int ix1 = (int)Math.Max(0, Math.Min(W - 1, Math.Floor(a1)));
            int iy1 = (int)Math.Max(0, Math.Min(H - 1, Math.Floor(b1)));
            int ix2 = (int)Math.Max(ix1 + 1, Math.Min(W, Math.Ceiling(a2)));
            int iy2 = (int)Math.Max(iy1 + 1, Math.Min(H, Math.Ceiling(b2)));
            return new[] { ix1, iy1, ix2, iy2 };
        }

        private static bool BboxIntersects(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length < 4 || b.Length < 4) return false;
            int iw = Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]);
            int ih = Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]);
            return iw > 0 && ih > 0;
        }

        private static bool TryExtractBboxAabbCurrent(JObject det, out double x1, out double y1, out double x2, out double y2)
        {
            x1 = y1 = x2 = y2 = 0;
            if (det == null) return false;

            // bbox: 约定普通框为 xywh；旋转框为 [cx,cy,w,h] + angle(弧度)
            var bbox = det["bbox"] as JArray;
            if (bbox == null || bbox.Count < 4) return false;

            bool withAngle = det["with_angle"]?.Value<bool?>() ?? false;
            double angle = det["angle"]?.Value<double?>() ?? -100.0;

            if (withAngle && angle != -100.0)
            {
                double cx = SafeToDouble(bbox[0]);
                double cy = SafeToDouble(bbox[1]);
                double w = Math.Abs(SafeToDouble(bbox[2]));
                double h = Math.Abs(SafeToDouble(bbox[3]));
                if (w <= 0 || h <= 0) return false;

                // 角度容错：若像“度数”，则转换为弧度
                double angRad = Math.Abs(angle) > 3.2 ? (angle * Math.PI / 180.0) : angle;
                double c = Math.Cos(angRad);
                double s = Math.Sin(angRad);
                double hw = w / 2.0, hh = h / 2.0;
                double[,] offs = new double[,] { { -hw, -hh }, { hw, -hh }, { hw, hh }, { -hw, hh } };

                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    double dx = offs[k, 0];
                    double dy = offs[k, 1];
                    double px = cx + c * dx - s * dy;
                    double py = cy + s * dx + c * dy;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }
                x1 = minX; y1 = minY; x2 = maxX; y2 = maxY;
                return true;
            }
            else
            {
                // 视为 xywh
                double x = SafeToDouble(bbox[0]);
                double y = SafeToDouble(bbox[1]);
                double bw = Math.Abs(SafeToDouble(bbox[2]));
                double bh = Math.Abs(SafeToDouble(bbox[3]));
                x1 = x;
                y1 = y;
                x2 = x + bw;
                y2 = y + bh;
                return true;
            }
        }

        private static double SafeToDouble(JToken t)
        {
            try { return t != null ? Convert.ToDouble(((JValue)t).Value) : 0.0; } catch { return 0.0; }
        }

        private static int[] MapAabbToOriginalAndClamp(TransformationState st, int[] bboxCur, int W0, int H0)
        {
            if (bboxCur == null || bboxCur.Length < 4) return null;
            if (st == null || st.AffineMatrix2x3 == null || st.AffineMatrix2x3.Length != 6) return ClampXYXY(bboxCur[0], bboxCur[1], bboxCur[2], bboxCur[3], W0, H0);

            var inv = TransformationState.Inverse2x3(st.AffineMatrix2x3);

            // 对齐 Python: 若存在 crop_box，则 current -> ROI 后需要加回 (x0,y0) 映射到原图坐标
            int x0 = 0, y0 = 0;
            bool hasCropOffset = false;
            try
            {
                if (st.CropBox != null && st.CropBox.Length >= 4)
                {
                    x0 = st.CropBox[0];
                    y0 = st.CropBox[1];
                    // 仅当 crop_box 显著非零时启用（避免对“已融合 crop 的 affine”重复加偏移）
                    hasCropOffset = (x0 != 0 || y0 != 0);
                }
            }
            catch { hasCropOffset = false; x0 = 0; y0 = 0; }
            var pts = new[]
            {
                new Point2d(bboxCur[0], bboxCur[1]),
                new Point2d(bboxCur[2], bboxCur[1]),
                new Point2d(bboxCur[2], bboxCur[3]),
                new Point2d(bboxCur[0], bboxCur[3]),
            };

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < pts.Length; i++)
            {
                double x = pts[i].X, y = pts[i].Y;
                double ox = inv[0] * x + inv[1] * y + inv[2];
                double oy = inv[3] * x + inv[4] * y + inv[5];
                if (hasCropOffset)
                {
                    ox += x0;
                    oy += y0;
                }
                if (ox < minX) minX = ox;
                if (ox > maxX) maxX = ox;
                if (oy < minY) minY = oy;
                if (oy > maxY) maxY = oy;
            }
            return ClampXYXY(minX, minY, maxX, maxY, W0, H0);
        }

        private static int[] ParseGlobalBboxToAabb(JArray gb)
        {
            if (gb == null) return null;
            try
            {
                if (gb.Count == 4)
                {
                    // 双兼容：既支持 xyxy（Python 新版写法），也支持 xywh（C# 旧写法）
                    double a0 = SafeToDouble(gb[0]);
                    double a1 = SafeToDouble(gb[1]);
                    double a2 = SafeToDouble(gb[2]);
                    double a3 = SafeToDouble(gb[3]);

                    // 若第三/四个值更像右下角（大于左上角），优先按 xyxy 解读
                    if (a2 > a0 && a3 > a1)
                    {
                        return new[] { (int)Math.Floor(a0), (int)Math.Floor(a1), (int)Math.Ceiling(a2), (int)Math.Ceiling(a3) };
                    }
                    // 否则按 xywh 解读
                    double w = Math.Abs(a2);
                    double h = Math.Abs(a3);
                    return new[] { (int)Math.Floor(a0), (int)Math.Floor(a1), (int)Math.Ceiling(a0 + w), (int)Math.Ceiling(a1 + h) };
                }
                if (gb.Count == 5)
                {
                    // [cx,cy,w,h,angle]（angle 多为弧度）
                    double cx = SafeToDouble(gb[0]);
                    double cy = SafeToDouble(gb[1]);
                    double w = Math.Abs(SafeToDouble(gb[2]));
                    double h = Math.Abs(SafeToDouble(gb[3]));
                    double angle = SafeToDouble(gb[4]);
                    if (w <= 0 || h <= 0) return null;
                    double angRad = Math.Abs(angle) > 3.2 ? (angle * Math.PI / 180.0) : angle;
                    double c = Math.Cos(angRad);
                    double s = Math.Sin(angRad);
                    double hw = w / 2.0, hh = h / 2.0;
                    double[,] offs = new double[,] { { -hw, -hh }, { hw, -hh }, { hw, hh }, { -hw, hh } };
                    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                    for (int k = 0; k < 4; k++)
                    {
                        double dx = offs[k, 0];
                        double dy = offs[k, 1];
                        double px = cx + c * dx - s * dy;
                        double py = cy + s * dx + c * dy;
                        if (px < minX) minX = px;
                        if (px > maxX) maxX = px;
                        if (py < minY) minY = py;
                        if (py > maxY) maxY = py;
                    }
                    return new[] { (int)Math.Floor(minX), (int)Math.Floor(minY), (int)Math.Ceiling(maxX), (int)Math.Ceiling(maxY) };
                }
            }
            catch { }
            return null;
        }

        private static bool CheckMaskOverlapWithRegion(Mat maskMat0, int[] bboxXYXY, int[] roiXYXY, int W, int H)
        {
            if (maskMat0 == null || maskMat0.Empty()) return false;
            if (bboxXYXY == null || roiXYXY == null) return false;

            // bbox clamp + 同步裁剪 mask（若 bbox 越界）
            Mat maskMat = maskMat0;
            int[] bb = bboxXYXY;
            try
            {
                var tup = ClampBboxAndCropMask(bb, maskMat0, W, H);
                bb = tup.Item1;
                maskMat = tup.Item2 ?? maskMat0;
            }
            catch
            {
                bb = bboxXYXY;
                maskMat = maskMat0;
            }

            int bw = bb[2] - bb[0];
            int bh = bb[3] - bb[1];
            if (bw <= 0 || bh <= 0) return false;

            // mask resize 到 bbox 尺寸（实例分割常见：mask=32x32，bbox=512x512）
            Mat resized = null;
            Mat workMask = maskMat;
            try
            {
                if (workMask.Rows != bh || workMask.Cols != bw)
                {
                    resized = new Mat();
                    Cv2.Resize(workMask, resized, new Size(bw, bh), 0, 0, InterpolationFlags.Nearest);
                    workMask = resized;
                }
            }
            catch
            {
                if (resized != null) resized.Dispose();
                // resize 失败时降级为 bbox 相交判定（由外层决定）
                return false;
            }

            try
            {
                int ix1 = Math.Max(bb[0], roiXYXY[0]);
                int iy1 = Math.Max(bb[1], roiXYXY[1]);
                int ix2 = Math.Min(bb[2], roiXYXY[2]);
                int iy2 = Math.Min(bb[3], roiXYXY[3]);
                if (ix2 <= ix1 || iy2 <= iy1) return false;

                int lx1 = ix1 - bb[0];
                int ly1 = iy1 - bb[1];
                int lx2 = ix2 - bb[0];
                int ly2 = iy2 - bb[1];
                lx1 = Math.Max(0, Math.Min(bw, lx1));
                ly1 = Math.Max(0, Math.Min(bh, ly1));
                lx2 = Math.Max(lx1 + 1, Math.Min(bw, lx2));
                ly2 = Math.Max(ly1 + 1, Math.Min(bh, ly2));

                var rect = new Rect(lx1, ly1, lx2 - lx1, ly2 - ly1);
                using (var sub = new Mat(workMask, rect))
                {
                    // 任意像素非 0 即认为与 ROI 有重叠
                    return Cv2.CountNonZero(sub) > 0;
                }
            }
            finally
            {
                if (resized != null) resized.Dispose();
            }
        }

        private static Tuple<int[], Mat> ClampBboxAndCropMask(int[] bboxXYXY, Mat maskMat, int W, int H)
        {
            if (bboxXYXY == null || bboxXYXY.Length < 4) return Tuple.Create(bboxXYXY, (Mat)null);
            W = Math.Max(1, W);
            H = Math.Max(1, H);
            int x1 = bboxXYXY[0], y1 = bboxXYXY[1], x2 = bboxXYXY[2], y2 = bboxXYXY[3];
            int nx1 = Math.Max(0, Math.Min(W - 1, x1));
            int ny1 = Math.Max(0, Math.Min(H - 1, y1));
            int nx2 = Math.Max(nx1 + 1, Math.Min(W, x2));
            int ny2 = Math.Max(ny1 + 1, Math.Min(H, y2));

            if (maskMat == null || maskMat.Empty()) return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, (Mat)null);

            try
            {
                int dh0 = y2 - y1;
                int dw0 = x2 - x1;
                if (dh0 <= 0 || dw0 <= 0) return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, (Mat)null);
                if (maskMat.Rows != dh0 || maskMat.Cols != dw0) return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, (Mat)null);

                int cutL = Math.Max(0, nx1 - x1);
                int cutT = Math.Max(0, ny1 - y1);
                int cutR = Math.Max(0, x2 - nx2);
                int cutB = Math.Max(0, y2 - ny2);

                int rw = maskMat.Cols - cutL - cutR;
                int rh = maskMat.Rows - cutT - cutB;
                if (rw <= 0 || rh <= 0) return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, (Mat)null);
                var rect = new Rect(cutL, cutT, rw, rh);
                var cropped = new Mat(maskMat, rect).Clone();
                return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, cropped);
            }
            catch
            {
                return Tuple.Create(new[] { nx1, ny1, nx2, ny2 }, (Mat)null);
            }
        }

        private static Mat TryParseMaskArrayToMat(JToken tok)
        {
            // 期望形态：二维数组（行数组），元素可为 0/1/255
            var ja = tok as JArray;
            if (ja == null || ja.Count == 0) return null;
            if (!(ja[0] is JArray row0)) return null;
            int H = ja.Count;
            int W = row0.Count;
            if (H <= 0 || W <= 0) return null;

            var mat = new Mat(H, W, MatType.CV_8UC1);
            for (int y = 0; y < H; y++)
            {
                var row = ja[y] as JArray;
                if (row == null || row.Count != W) { mat.Dispose(); return null; }
                for (int x = 0; x < W; x++)
                {
                    byte v = 0;
                    try
                    {
                        double dv = Convert.ToDouble(((JValue)row[x]).Value);
                        v = (byte)(dv > 0 ? 255 : 0);
                    }
                    catch { v = 0; }
                    mat.Set(y, x, v);
                }
            }
            return mat;
        }

        // 说明：MapAabbToOriginalAndClamp 已对 crop_box 做了偏移补偿（对齐 Python 的 crop_box+affine 语义）。
    }

    /// <summary>
    /// 模块名称：结果过滤（区域-全局）
    /// 对齐 Python: post_process/result_filter_region_global / features/result_filter_region_global
    /// 语义：使用原图坐标做 ROI 相交判定，但不修改输出结果的坐标系/transform（避免图像与结果错配）。
    /// </summary>
    public class ResultFilterRegionGlobal : ResultFilterRegion
    {
        static ResultFilterRegionGlobal()
        {
            ModuleRegistry.Register("post_process/result_filter_region_global", typeof(ResultFilterRegionGlobal));
            ModuleRegistry.Register("features/result_filter_region_global", typeof(ResultFilterRegionGlobal));
        }

        public ResultFilterRegionGlobal(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            // 强制按原图坐标判定，但不把输出结果“原图化”
            return ProcessInternal(imageList, resultList, forceOriginalOverride: true, convertOutputToOriginalOverride: false);
        }
    }
}


