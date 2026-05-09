#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include "flow/utils/FlowPlatformUtils.h"
#include "flow/utils/MaskRleUtils.h"

#include <algorithm>
#include <array>
#include <cctype>
#include <cmath>
#include <cstdio>
#include <fstream>
#include <iomanip>
#include <iterator>
#include <sstream>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#endif

#include "opencv2/imgcodecs.hpp"
#include "opencv2/imgproc.hpp"

namespace dlcv_infer {
namespace flow {

static constexpr double kPi = 3.14159265358979323846;

static std::string MakeSafeFileName(std::string name) {
    if (name.empty()) return "Unknown";
    const std::string bad = "<>:\"/\\|?*";
    for (char& c : name) {
        if (bad.find(c) != std::string::npos) c = '_';
        if (c == ' ') c = '_';
    }
    if (name.empty()) return "Unknown";
    return name;
}

static std::string NormalizeTextSimple(std::string s) {
    // 简化版归一：去空格 + 易混字符替换 + 大写
    std::string out;
    out.reserve(s.size());
    for (char c : s) {
        if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
        if (c == 'l') c = 'I';
        if (c == '1') c = 'I';
        if (c == 'o' || c == 'O') c = '0';
        out.push_back(static_cast<char>(std::toupper(static_cast<unsigned char>(c))));
    }
    return out;
}

static cv::Rect ClampRect(int x, int y, int w, int h, int W, int H) {
    if (W <= 0 || H <= 0) return cv::Rect();
    int x0 = std::max(0, std::min(W, x));
    int y0 = std::max(0, std::min(H, y));
    int x1 = std::max(x0, std::min(W, x + w));
    int y1 = std::max(y0, std::min(H, y + h));
    if (x1 <= x0 || y1 <= y0) return cv::Rect();
    return cv::Rect(x0, y0, x1 - x0, y1 - y0);
}

static bool RectIntersects(const cv::Rect& a, const cv::Rect& b) {
    return (a & b).area() > 0;
}

// -------------------- ResultFilterRegion --------------------
class ResultFilterRegionModule : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        return ProcessInternal(imageList, resultList,
                               /*hasForceOriginalOverride*/ false, /*forceOriginalOverride*/ false,
                               /*hasConvertOutputOverride*/ false, /*convertOutputOverride*/ false);
    }

protected:
    using Box = std::array<int, 4>; // [x1,y1,x2,y2]

    ModuleIO ProcessInternal(const std::vector<ModuleImage>& imageList,
                             const Json& resultList,
                             bool hasForceOriginalOverride,
                             bool forceOriginalOverride,
                             bool hasConvertOutputOverride,
                             bool convertOutputOverride) {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        const int x = ReadInt("x", 0);
        const int y = ReadInt("y", 0);
        const int w = std::max(1, ReadInt("w", 100));
        const int h = std::max(1, ReadInt("h", 100));

        std::string resultRegionMode = ToLowerCopy(ReadString("result_region_mode", "any_bbox"));
        if (resultRegionMode != "any_bbox" && resultRegionMode != "top1_bbox") {
            resultRegionMode = "any_bbox";
        }

        std::unordered_map<int, int> originToWrapIndex;
        std::unordered_map<std::string, int> sigToWrapIndex;
        for (int i = 0; i < static_cast<int>(images.size()); i++) {
            const ModuleImage& wrap = images[static_cast<size_t>(i)];
            originToWrapIndex[wrap.OriginalIndex] = i;
            const std::string sig = SerializeTransformSig(wrap.TransformState);
            if (!sig.empty()) sigToWrapIndex[sig] = i;
        }

        const auto pickWrapIndexForEntry = [&](const Json& entry) -> int {
            if (!entry.is_object()) return -1;
            try {
                if (entry.contains("transform") && entry.at("transform").is_object()) {
                    const std::string sig = SerializeTransformSig(entry.at("transform"));
                    auto itSig = sigToWrapIndex.find(sig);
                    if (!sig.empty() && itSig != sigToWrapIndex.end()) return itSig->second;
                }
            } catch (...) {}

            int idx = -1;
            try { idx = entry.value("index", -1); } catch (...) { idx = -1; }
            if (idx >= 0 && idx < static_cast<int>(images.size())) return idx;

            int originIdx = -1;
            try { originIdx = entry.value("origin_index", -1); } catch (...) { originIdx = -1; }
            auto itOrigin = originToWrapIndex.find(originIdx);
            if (originIdx >= 0 && itOrigin != originToWrapIndex.end()) return itOrigin->second;
            return -1;
        };

        bool forceOriginal = false;
        for (const auto& token : results) {
            if (!token.is_object()) continue;
            const Json& entry = token;
            if (!CaseEquals(entry.value("type", ""), "local")) continue;

            if (!entry.contains("transform") || entry.at("transform").is_null()) {
                forceOriginal = true;
                break;
            }
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) continue;

            const int wrapIndex = pickWrapIndexForEntry(entry);
            if (wrapIndex < 0 || wrapIndex >= static_cast<int>(images.size())) continue;
            const ModuleImage& wrap = images[static_cast<size_t>(wrapIndex)];
            const int Wc = std::max(1, wrap.ImageObject.empty() ? 1 : wrap.ImageObject.cols);
            const int Hc = std::max(1, wrap.ImageObject.empty() ? 1 : wrap.ImageObject.rows);
            int W0 = wrap.TransformState.OriginalWidth;
            int H0 = wrap.TransformState.OriginalHeight;
            if (W0 <= 0) W0 = Wc;
            if (H0 <= 0) H0 = Hc;

            for (const auto& detToken : entry.at("sample_results")) {
                if (!detToken.is_object()) continue;
                double bx1 = 0.0, by1 = 0.0, bx2 = 0.0, by2 = 0.0;
                if (!TryExtractBboxAabbCurrent(detToken, bx1, by1, bx2, by2)) continue;
                const Box bboxCur = ClampXYXY(bx1, by1, bx2, by2, Wc, Hc);
                (void)bboxCur;

                Box bboxMetaOri{};
                bool hasMetaOri = false;
                try {
                    if (detToken.contains("metadata") && detToken.at("metadata").is_object()) {
                        const Json& meta = detToken.at("metadata");
                        if (meta.contains("global_bbox") && meta.at("global_bbox").is_array()) {
                            if (TryParseGlobalBboxToAabb(meta.at("global_bbox"), bboxMetaOri)) {
                                bboxMetaOri = ClampXYXY(
                                    static_cast<double>(bboxMetaOri[0]), static_cast<double>(bboxMetaOri[1]),
                                    static_cast<double>(bboxMetaOri[2]), static_cast<double>(bboxMetaOri[3]), W0, H0);
                                hasMetaOri = true;
                            }
                        }
                    }
                } catch (...) { hasMetaOri = false; }

                if (hasMetaOri) {
                    if (std::abs(bboxMetaOri[0] - bboxCur[0]) > 1 ||
                        std::abs(bboxMetaOri[1] - bboxCur[1]) > 1 ||
                        std::abs(bboxMetaOri[2] - bboxCur[2]) > 1 ||
                        std::abs(bboxMetaOri[3] - bboxCur[3]) > 1 ||
                        bboxMetaOri[2] > Wc + 1 || bboxMetaOri[3] > Hc + 1) {
                        forceOriginal = true;
                        break;
                    }
                }

                if (bx2 > Wc + 1 || by2 > Hc + 1) {
                    forceOriginal = true;
                    break;
                }
            }
            if (forceOriginal) break;
        }

        if (hasForceOriginalOverride) {
            forceOriginal = forceOriginalOverride;
        }
        const bool convertOutputToOriginal = hasConvertOutputOverride ? convertOutputOverride : forceOriginal;

        const auto rrInput = ReadResultRegionInput();
        const bool resultRegionConnected = rrInput.first;
        const Json resultRegionResults = rrInput.second;

        std::unordered_map<int, std::vector<Box>> regionBoxesByWrapIndex;
        if (resultRegionConnected) {
            struct BoxCandidate {
                Box BBox{};
                bool HasScore = false;
                double Score = 0.0;
            };
            std::unordered_map<int, std::vector<BoxCandidate>> candidatesByWrapIndex;

            for (const auto& token : resultRegionResults) {
                if (!token.is_object()) continue;
                const Json& regionEntry = token;
                if (!CaseEquals(regionEntry.value("type", ""), "local")) continue;
                if (!regionEntry.contains("sample_results") || !regionEntry.at("sample_results").is_array()) continue;

                const int wrapIndex = pickWrapIndexForEntry(regionEntry);
                if (wrapIndex < 0 || wrapIndex >= static_cast<int>(images.size())) continue;
                const ModuleImage& wrap = images[static_cast<size_t>(wrapIndex)];
                const int Wc = std::max(1, wrap.ImageObject.empty() ? 1 : wrap.ImageObject.cols);
                const int Hc = std::max(1, wrap.ImageObject.empty() ? 1 : wrap.ImageObject.rows);
                int W0 = wrap.TransformState.OriginalWidth;
                int H0 = wrap.TransformState.OriginalHeight;
                if (W0 <= 0) W0 = Wc;
                if (H0 <= 0) H0 = Hc;

                for (const auto& detToken : regionEntry.at("sample_results")) {
                    if (!detToken.is_object()) continue;
                    double bx1 = 0.0, by1 = 0.0, bx2 = 0.0, by2 = 0.0;
                    if (!TryExtractBboxAabbCurrent(detToken, bx1, by1, bx2, by2)) continue;

                    const Box bboxCur = ClampXYXY(bx1, by1, bx2, by2, Wc, Hc);
                    Box bboxUse = bboxCur;
                    if (forceOriginal) {
                        Box bboxMetaOri{};
                        bool hasMetaOri = false;
                        try {
                            if (detToken.contains("metadata") && detToken.at("metadata").is_object()) {
                                const Json& meta = detToken.at("metadata");
                                if (meta.contains("global_bbox") && meta.at("global_bbox").is_array()) {
                                    if (TryParseGlobalBboxToAabb(meta.at("global_bbox"), bboxMetaOri)) {
                                        bboxMetaOri = ClampXYXY(
                                            static_cast<double>(bboxMetaOri[0]), static_cast<double>(bboxMetaOri[1]),
                                            static_cast<double>(bboxMetaOri[2]), static_cast<double>(bboxMetaOri[3]), W0, H0);
                                        hasMetaOri = true;
                                    }
                                }
                            }
                        } catch (...) { hasMetaOri = false; }

                        if (hasMetaOri) {
                            bboxUse = bboxMetaOri;
                        } else {
                            bboxUse = MapAabbToOriginalAndClamp(wrap.TransformState, bboxCur, W0, H0);
                        }
                    }

                    double score = 0.0;
                    const bool hasScore = TryExtractScore(detToken, score);
                    candidatesByWrapIndex[wrapIndex].push_back(BoxCandidate{ bboxUse, hasScore, score });
                }
            }

            for (const auto& kv : candidatesByWrapIndex) {
                const int wrapIndex = kv.first;
                const std::vector<BoxCandidate>& cands = kv.second;
                if (cands.empty()) continue;

                if (resultRegionMode == "top1_bbox") {
                    bool foundScored = false;
                    double bestScore = -1e100;
                    Box bestBox{};
                    for (const auto& cand : cands) {
                        if (!cand.HasScore || std::isnan(cand.Score) || std::isinf(cand.Score)) continue;
                        if (!foundScored || cand.Score > bestScore) {
                            foundScored = true;
                            bestScore = cand.Score;
                            bestBox = cand.BBox;
                        }
                    }
                    if (foundScored) {
                        regionBoxesByWrapIndex[wrapIndex] = { bestBox };
                    } else {
                        regionBoxesByWrapIndex[wrapIndex] = { cands.front().BBox };
                    }
                } else {
                    std::vector<Box> boxes;
                    boxes.reserve(cands.size());
                    for (const auto& cand : cands) boxes.push_back(cand.BBox);
                    if (!boxes.empty()) regionBoxesByWrapIndex[wrapIndex] = std::move(boxes);
                }
            }
        }

        struct EntryWithIndex {
            Json Entry;
            int OldIndex = -1;
        };

        std::vector<EntryWithIndex> insideEntries;
        std::vector<EntryWithIndex> outsideEntries;
        std::vector<Json> others;
        std::vector<unsigned char> inFlags(images.size(), 0);
        std::vector<unsigned char> outFlags(images.size(), 0);

        bool hasAnyInside = false;

        for (const auto& token : results) {
            if (!token.is_object()) {
                others.push_back(token);
                continue;
            }
            const Json& entry = token;
            if (!CaseEquals(entry.value("type", ""), "local")) {
                others.push_back(entry);
                continue;
            }
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) {
                others.push_back(entry);
                continue;
            }

            const int wrapIndex = pickWrapIndexForEntry(entry);
            if (wrapIndex < 0 || wrapIndex >= static_cast<int>(images.size())) {
                others.push_back(entry);
                continue;
            }

            const ModuleImage& wrap = images[static_cast<size_t>(wrapIndex)];
            const int Wc = std::max(1, wrap.ImageObject.empty() ? 1 : wrap.ImageObject.cols);
            const int Hc = std::max(1, wrap.ImageObject.empty() ? 1 : wrap.ImageObject.rows);
            int W0 = wrap.TransformState.OriginalWidth;
            int H0 = wrap.TransformState.OriginalHeight;
            if (W0 <= 0) W0 = Wc;
            if (H0 <= 0) H0 = Hc;

            const bool useOriginal = forceOriginal;
            const Box fallbackRoi = useOriginal
                ? ClampXYXY(static_cast<double>(x), static_cast<double>(y), static_cast<double>(x + w), static_cast<double>(y + h), W0, H0)
                : ClampXYXY(static_cast<double>(x), static_cast<double>(y), static_cast<double>(x + w), static_cast<double>(y + h), Wc, Hc);

            std::vector<Box> activeRois;
            bool forceOutsideByEmptyRegion = false;
            if (resultRegionConnected) {
                auto itRoi = regionBoxesByWrapIndex.find(wrapIndex);
                if (itRoi != regionBoxesByWrapIndex.end() && !itRoi->second.empty()) {
                    activeRois = itRoi->second;
                } else {
                    forceOutsideByEmptyRegion = true;
                }
            } else {
                activeRois.push_back(fallbackRoi);
            }

            const int spaceW = useOriginal ? W0 : Wc;
            const int spaceH = useOriginal ? H0 : Hc;

            Json inArr = Json::array();
            Json outArr = Json::array();
            for (const auto& detToken : entry.at("sample_results")) {
                if (!detToken.is_object()) {
                    outArr.push_back(detToken);
                    continue;
                }
                const Json& det = detToken;

                double bx1 = 0.0, by1 = 0.0, bx2 = 0.0, by2 = 0.0;
                if (!TryExtractBboxAabbCurrent(det, bx1, by1, bx2, by2)) {
                    outArr.push_back(det);
                    continue;
                }

                const Box bboxCur = ClampXYXY(bx1, by1, bx2, by2, Wc, Hc);
                Box bboxUse = bboxCur;
                if (useOriginal) {
                    Box bboxMetaOri{};
                    bool hasMetaOri = false;
                    try {
                        if (det.contains("metadata") && det.at("metadata").is_object()) {
                            const Json& meta = det.at("metadata");
                            if (meta.contains("global_bbox") && meta.at("global_bbox").is_array()) {
                                if (TryParseGlobalBboxToAabb(meta.at("global_bbox"), bboxMetaOri)) {
                                    bboxMetaOri = ClampXYXY(
                                        static_cast<double>(bboxMetaOri[0]), static_cast<double>(bboxMetaOri[1]),
                                        static_cast<double>(bboxMetaOri[2]), static_cast<double>(bboxMetaOri[3]), W0, H0);
                                    hasMetaOri = true;
                                }
                            }
                        }
                    } catch (...) { hasMetaOri = false; }

                    if (hasMetaOri) {
                        bboxUse = bboxMetaOri;
                    } else {
                        bboxUse = MapAabbToOriginalAndClamp(wrap.TransformState, bboxCur, W0, H0);
                    }
                }

                bool isIn = false;
                bool decided = false;
                if (forceOutsideByEmptyRegion) {
                    decided = true;
                    isIn = false;
                }

                if (!decided && det.contains("mask_rle") && det.at("mask_rle").is_object()) {
                    try {
                        cv::Mat maskMat = MaskInfoToMat(det.at("mask_rle"));
                        if (!maskMat.empty()) {
                            isIn = false;
                            for (const auto& roi : activeRois) {
                                if (CheckMaskOverlapWithRegion(maskMat, bboxUse, roi, spaceW, spaceH)) {
                                    isIn = true;
                                    break;
                                }
                            }
                            decided = true;
                        }
                    } catch (...) {
                        decided = false;
                    }
                }

                if (!decided && det.contains("mask_array") && det.at("mask_array").is_array()) {
                    try {
                        cv::Mat maskArray = TryParseMaskArrayToMat(det.at("mask_array"));
                        if (!maskArray.empty()) {
                            isIn = false;
                            for (const auto& roi : activeRois) {
                                if (CheckMaskOverlapWithRegion(maskArray, bboxUse, roi, spaceW, spaceH)) {
                                    isIn = true;
                                    break;
                                }
                            }
                            decided = true;
                        }
                    } catch (...) {
                        decided = false;
                    }
                }

                if (!decided) {
                    isIn = false;
                    for (const auto& roi : activeRois) {
                        if (BboxIntersects(bboxUse, roi)) {
                            isIn = true;
                            break;
                        }
                    }
                }

                Json detOut = det;
                if (convertOutputToOriginal && useOriginal) {
                    detOut = ConvertDetToOriginal(det, bboxUse);
                }

                if (isIn) {
                    inArr.push_back(detOut);
                    hasAnyInside = true;
                } else {
                    outArr.push_back(detOut);
                }
            }

            if (!inArr.empty()) {
                Json entryIn = entry;
                entryIn["sample_results"] = inArr;
                if (convertOutputToOriginal && useOriginal) {
                    entryIn["transform"] = nullptr;
                    entryIn["origin_index"] = wrap.OriginalIndex;
                }
                insideEntries.push_back(EntryWithIndex{ entryIn, wrapIndex });
                if (wrapIndex >= 0 && wrapIndex < static_cast<int>(inFlags.size())) inFlags[static_cast<size_t>(wrapIndex)] = 1;
            }
            if (!outArr.empty()) {
                Json entryOut = entry;
                entryOut["sample_results"] = outArr;
                if (convertOutputToOriginal && useOriginal) {
                    entryOut["transform"] = nullptr;
                    entryOut["origin_index"] = wrap.OriginalIndex;
                }
                outsideEntries.push_back(EntryWithIndex{ entryOut, wrapIndex });
                if (wrapIndex >= 0 && wrapIndex < static_cast<int>(outFlags.size())) outFlags[static_cast<size_t>(wrapIndex)] = 1;
            }
        }

        std::vector<ModuleImage> outImagesIn;
        std::vector<ModuleImage> outImagesOut;
        for (int i = 0; i < static_cast<int>(images.size()); i++) {
            if (i >= 0 && i < static_cast<int>(inFlags.size()) && inFlags[static_cast<size_t>(i)] != 0) {
                outImagesIn.push_back(images[static_cast<size_t>(i)]);
            }
            if (i >= 0 && i < static_cast<int>(outFlags.size()) && outFlags[static_cast<size_t>(i)] != 0) {
                outImagesOut.push_back(images[static_cast<size_t>(i)]);
            }
        }

        std::unordered_map<int, int> inReindex;
        std::unordered_map<int, int> outReindex;
        int ptr = 0;
        for (int i = 0; i < static_cast<int>(inFlags.size()); i++) {
            if (inFlags[static_cast<size_t>(i)] != 0) inReindex[i] = ptr++;
        }
        ptr = 0;
        for (int i = 0; i < static_cast<int>(outFlags.size()); i++) {
            if (outFlags[static_cast<size_t>(i)] != 0) outReindex[i] = ptr++;
        }

        Json outResultsIn = Json::array();
        for (auto& item : insideEntries) {
            Json entry = item.Entry;
            auto it = inReindex.find(item.OldIndex);
            if (it != inReindex.end()) entry["index"] = it->second;
            outResultsIn.push_back(entry);
        }

        Json outResultsOut = Json::array();
        for (auto& item : outsideEntries) {
            Json entry = item.Entry;
            auto it = outReindex.find(item.OldIndex);
            if (it != outReindex.end()) entry["index"] = it->second;
            outResultsOut.push_back(entry);
        }

        for (const auto& other : others) {
            outResultsIn.push_back(other);
            outResultsOut.push_back(other);
        }

        this->ExtraOutputs.push_back(ModuleChannel(outImagesOut, outResultsOut));
        this->ScalarOutputsByName["has_positive"] = hasAnyInside;
        return ModuleIO(std::move(outImagesIn), std::move(outResultsIn), Json::array());
    }

private:
    static std::string ToLowerCopy(const std::string& s) {
        std::string out = s;
        std::transform(out.begin(), out.end(), out.begin(), [](unsigned char c) {
            return static_cast<char>(std::tolower(c));
        });
        return out;
    }

    static bool CaseEquals(const std::string& a, const std::string& b) {
        return ToLowerCopy(a) == ToLowerCopy(b);
    }

    static bool TryToDouble(const Json& token, double& outVal) {
        try {
            if (token.is_number()) {
                outVal = token.get<double>();
                return true;
            }
            if (token.is_string()) {
                outVal = std::stod(token.get<std::string>());
                return true;
            }
        } catch (...) {}
        return false;
    }

    static std::string Round6(double v) {
        if (std::isnan(v) || std::isinf(v)) return "0";
        std::ostringstream oss;
        oss << std::fixed << std::setprecision(6) << v;
        std::string s = oss.str();
        while (!s.empty() && s.back() == '0') s.pop_back();
        if (!s.empty() && s.back() == '.') s.pop_back();
        return s.empty() ? "0" : s;
    }

    static std::string SerializeTransformSig(const TransformationState& st) {
        if (st.AffineMatrix2x3.size() < 6) return std::string();
        int cbx = 0, cby = 0, cbw = 0, cbh = 0;
        if (st.CropBox.size() >= 4) {
            cbx = st.CropBox[0];
            cby = st.CropBox[1];
            cbw = st.CropBox[2];
            cbh = st.CropBox[3];
        }
        int outW = 0, outH = 0;
        if (st.OutputSize.size() >= 2) {
            outW = st.OutputSize[0];
            outH = st.OutputSize[1];
        }
        const std::vector<double>& a = st.AffineMatrix2x3;
        std::ostringstream oss;
        oss << "cb:" << cbx << "," << cby << "," << cbw << "," << cbh
            << "|os:" << outW << "," << outH
            << "|ori:" << st.OriginalWidth << "," << st.OriginalHeight
            << "|A:" << Round6(a[0]) << "," << Round6(a[1]) << "," << Round6(a[2]) << ","
            << Round6(a[3]) << "," << Round6(a[4]) << "," << Round6(a[5]);
        return oss.str();
    }

    static bool TryGetAffine2x3FromJson(const Json& transformObj, std::array<double, 6>& outAffine) {
        try {
            if (transformObj.contains("affine_2x3") && transformObj.at("affine_2x3").is_array()) {
                const Json& a23 = transformObj.at("affine_2x3");
                if (a23.size() >= 6) {
                    for (int i = 0; i < 6; i++) {
                        double v = 0.0;
                        if (!TryToDouble(a23.at(static_cast<size_t>(i)), v)) return false;
                        outAffine[static_cast<size_t>(i)] = v;
                    }
                    return true;
                }
            }
            if (transformObj.contains("affine_matrix") && transformObj.at("affine_matrix").is_array()) {
                const Json& amat = transformObj.at("affine_matrix");
                if (amat.size() >= 2 && amat.at(0).is_array() && amat.at(1).is_array() &&
                    amat.at(0).size() >= 3 && amat.at(1).size() >= 3) {
                    for (int r = 0; r < 2; r++) {
                        for (int c = 0; c < 3; c++) {
                            double v = 0.0;
                            if (!TryToDouble(amat.at(static_cast<size_t>(r)).at(static_cast<size_t>(c)), v)) return false;
                            outAffine[static_cast<size_t>(r * 3 + c)] = v;
                        }
                    }
                    return true;
                }
            }
        } catch (...) {}
        return false;
    }

    static std::string SerializeTransformSig(const Json& transformObj) {
        if (!transformObj.is_object()) return std::string();
        try {
            int cbx = 0, cby = 0, cbw = 0, cbh = 0;
            if (transformObj.contains("crop_box") && transformObj.at("crop_box").is_array() &&
                transformObj.at("crop_box").size() >= 4) {
                cbx = transformObj.at("crop_box").at(0).get<int>();
                cby = transformObj.at("crop_box").at(1).get<int>();
                cbw = transformObj.at("crop_box").at(2).get<int>();
                cbh = transformObj.at("crop_box").at(3).get<int>();
            }

            int outW = 0, outH = 0;
            if (transformObj.contains("output_size") && transformObj.at("output_size").is_array() &&
                transformObj.at("output_size").size() >= 2) {
                outW = transformObj.at("output_size").at(0).get<int>();
                outH = transformObj.at("output_size").at(1).get<int>();
            }

            int ow = 0, oh = 0;
            if (transformObj.contains("original_size") && transformObj.at("original_size").is_array() &&
                transformObj.at("original_size").size() >= 2) {
                ow = transformObj.at("original_size").at(0).get<int>();
                oh = transformObj.at("original_size").at(1).get<int>();
            } else {
                try { ow = transformObj.value("original_width", 0); } catch (...) { ow = 0; }
                try { oh = transformObj.value("original_height", 0); } catch (...) { oh = 0; }
            }

            std::array<double, 6> affine{};
            if (!TryGetAffine2x3FromJson(transformObj, affine)) return std::string();

            std::ostringstream oss;
            oss << "cb:" << cbx << "," << cby << "," << cbw << "," << cbh
                << "|os:" << outW << "," << outH
                << "|ori:" << ow << "," << oh
                << "|A:" << Round6(affine[0]) << "," << Round6(affine[1]) << "," << Round6(affine[2]) << ","
                << Round6(affine[3]) << "," << Round6(affine[4]) << "," << Round6(affine[5]);
            return oss.str();
        } catch (...) {
            return std::string();
        }
    }

    static Box ClampXYXY(double x1, double y1, double x2, double y2, int W, int H) {
        W = std::max(1, W);
        H = std::max(1, H);
        const double a1 = std::min(x1, x2);
        const double a2 = std::max(x1, x2);
        const double b1 = std::min(y1, y2);
        const double b2 = std::max(y1, y2);

        const int ix1 = static_cast<int>(std::max(0.0, std::min(static_cast<double>(W - 1), std::floor(a1))));
        const int iy1 = static_cast<int>(std::max(0.0, std::min(static_cast<double>(H - 1), std::floor(b1))));
        const int ix2 = static_cast<int>(std::max(static_cast<double>(ix1 + 1), std::min(static_cast<double>(W), std::ceil(a2))));
        const int iy2 = static_cast<int>(std::max(static_cast<double>(iy1 + 1), std::min(static_cast<double>(H), std::ceil(b2))));
        return Box{ ix1, iy1, ix2, iy2 };
    }

    static bool BboxIntersects(const Box& a, const Box& b) {
        const int iw = std::min(a[2], b[2]) - std::max(a[0], b[0]);
        const int ih = std::min(a[3], b[3]) - std::max(a[1], b[1]);
        return iw > 0 && ih > 0;
    }

    static bool TryExtractBboxAabbCurrent(const Json& det, double& x1, double& y1, double& x2, double& y2) {
        x1 = y1 = x2 = y2 = 0.0;
        if (!det.is_object()) return false;
        if (!det.contains("bbox") || !det.at("bbox").is_array() || det.at("bbox").size() < 4) return false;
        const Json& bbox = det.at("bbox");

        bool withAngle = false;
        double angle = -100.0;
        try { withAngle = det.value("with_angle", false); } catch (...) { withAngle = false; }
        try {
            if (det.contains("angle")) {
                double av = 0.0;
                if (TryToDouble(det.at("angle"), av)) angle = av;
            }
        } catch (...) { angle = -100.0; }

        if (withAngle && angle != -100.0) {
            double cx = 0.0, cy = 0.0, bw = 0.0, bh = 0.0;
            if (!TryToDouble(bbox.at(0), cx) || !TryToDouble(bbox.at(1), cy) ||
                !TryToDouble(bbox.at(2), bw) || !TryToDouble(bbox.at(3), bh)) {
                return false;
            }
            bw = std::abs(bw);
            bh = std::abs(bh);
            if (bw <= 0.0 || bh <= 0.0) return false;
            const double angRad = std::abs(angle) > 3.2 ? (angle * kPi / 180.0) : angle;
            const double c = std::cos(angRad);
            const double s = std::sin(angRad);
            const double hw = bw / 2.0;
            const double hh = bh / 2.0;
            const std::array<std::array<double, 2>, 4> offs{{
                { -hw, -hh }, { hw, -hh }, { hw, hh }, { -hw, hh }
            }};
            double minX = 1e100, minY = 1e100, maxX = -1e100, maxY = -1e100;
            for (const auto& off : offs) {
                const double px = cx + c * off[0] - s * off[1];
                const double py = cy + s * off[0] + c * off[1];
                minX = std::min(minX, px);
                minY = std::min(minY, py);
                maxX = std::max(maxX, px);
                maxY = std::max(maxY, py);
            }
            x1 = minX; y1 = minY; x2 = maxX; y2 = maxY;
            return true;
        }

        double bx = 0.0, by = 0.0, bw = 0.0, bh = 0.0;
        if (!TryToDouble(bbox.at(0), bx) || !TryToDouble(bbox.at(1), by) ||
            !TryToDouble(bbox.at(2), bw) || !TryToDouble(bbox.at(3), bh)) {
            return false;
        }
        bw = std::abs(bw);
        bh = std::abs(bh);
        x1 = bx;
        y1 = by;
        x2 = bx + bw;
        y2 = by + bh;
        return true;
    }

    static bool TryParseGlobalBboxToAabb(const Json& gb, Box& outBox) {
        if (!gb.is_array()) return false;
        try {
            if (gb.size() == 4) {
                double a0 = 0.0, a1 = 0.0, a2 = 0.0, a3 = 0.0;
                if (!TryToDouble(gb.at(0), a0) || !TryToDouble(gb.at(1), a1) ||
                    !TryToDouble(gb.at(2), a2) || !TryToDouble(gb.at(3), a3)) {
                    return false;
                }
                if (a2 > a0 && a3 > a1) {
                    outBox = Box{
                        static_cast<int>(std::floor(a0)),
                        static_cast<int>(std::floor(a1)),
                        static_cast<int>(std::ceil(a2)),
                        static_cast<int>(std::ceil(a3))
                    };
                } else {
                    const double w = std::abs(a2);
                    const double h = std::abs(a3);
                    outBox = Box{
                        static_cast<int>(std::floor(a0)),
                        static_cast<int>(std::floor(a1)),
                        static_cast<int>(std::ceil(a0 + w)),
                        static_cast<int>(std::ceil(a1 + h))
                    };
                }
                return true;
            }
            if (gb.size() == 5) {
                double cx = 0.0, cy = 0.0, w = 0.0, h = 0.0, angle = 0.0;
                if (!TryToDouble(gb.at(0), cx) || !TryToDouble(gb.at(1), cy) ||
                    !TryToDouble(gb.at(2), w) || !TryToDouble(gb.at(3), h) ||
                    !TryToDouble(gb.at(4), angle)) {
                    return false;
                }
                w = std::abs(w);
                h = std::abs(h);
                if (w <= 0.0 || h <= 0.0) return false;
                const double angRad = std::abs(angle) > 3.2 ? (angle * kPi / 180.0) : angle;
                const double c = std::cos(angRad);
                const double s = std::sin(angRad);
                const double hw = w / 2.0;
                const double hh = h / 2.0;
                const std::array<std::array<double, 2>, 4> offs{{
                    { -hw, -hh }, { hw, -hh }, { hw, hh }, { -hw, hh }
                }};
                double minX = 1e100, minY = 1e100, maxX = -1e100, maxY = -1e100;
                for (const auto& off : offs) {
                    const double px = cx + c * off[0] - s * off[1];
                    const double py = cy + s * off[0] + c * off[1];
                    minX = std::min(minX, px);
                    minY = std::min(minY, py);
                    maxX = std::max(maxX, px);
                    maxY = std::max(maxY, py);
                }
                outBox = Box{
                    static_cast<int>(std::floor(minX)),
                    static_cast<int>(std::floor(minY)),
                    static_cast<int>(std::ceil(maxX)),
                    static_cast<int>(std::ceil(maxY))
                };
                return true;
            }
        } catch (...) {}
        return false;
    }

    static Box MapAabbToOriginalAndClamp(const TransformationState& st, const Box& bboxCur, int W0, int H0) {
        if (st.AffineMatrix2x3.size() != 6) {
            return ClampXYXY(
                static_cast<double>(bboxCur[0]), static_cast<double>(bboxCur[1]),
                static_cast<double>(bboxCur[2]), static_cast<double>(bboxCur[3]), W0, H0);
        }

        const std::vector<double> inv = TransformationState::Inverse2x3(st.AffineMatrix2x3);
        int x0 = 0;
        int y0 = 0;
        bool hasCropOffset = false;
        if (st.CropBox.size() >= 4) {
            x0 = st.CropBox[0];
            y0 = st.CropBox[1];
            hasCropOffset = (x0 != 0 || y0 != 0);
        }

        const std::array<std::array<double, 2>, 4> pts{{
            { static_cast<double>(bboxCur[0]), static_cast<double>(bboxCur[1]) },
            { static_cast<double>(bboxCur[2]), static_cast<double>(bboxCur[1]) },
            { static_cast<double>(bboxCur[2]), static_cast<double>(bboxCur[3]) },
            { static_cast<double>(bboxCur[0]), static_cast<double>(bboxCur[3]) }
        }};

        double minX = 1e100, minY = 1e100, maxX = -1e100, maxY = -1e100;
        for (const auto& p : pts) {
            const double x = p[0];
            const double y = p[1];
            double ox = inv[0] * x + inv[1] * y + inv[2];
            double oy = inv[3] * x + inv[4] * y + inv[5];
            if (hasCropOffset) {
                ox += x0;
                oy += y0;
            }
            minX = std::min(minX, ox);
            minY = std::min(minY, oy);
            maxX = std::max(maxX, ox);
            maxY = std::max(maxY, oy);
        }
        return ClampXYXY(minX, minY, maxX, maxY, W0, H0);
    }

    static bool ClampBboxAndCropMask(const Box& bboxXYXY,
                                     const cv::Mat& maskMat,
                                     int W,
                                     int H,
                                     Box& clampedBox,
                                     cv::Mat& croppedMask) {
        W = std::max(1, W);
        H = std::max(1, H);
        const int x1 = bboxXYXY[0];
        const int y1 = bboxXYXY[1];
        const int x2 = bboxXYXY[2];
        const int y2 = bboxXYXY[3];

        const int nx1 = std::max(0, std::min(W - 1, x1));
        const int ny1 = std::max(0, std::min(H - 1, y1));
        const int nx2 = std::max(nx1 + 1, std::min(W, x2));
        const int ny2 = std::max(ny1 + 1, std::min(H, y2));
        clampedBox = Box{ nx1, ny1, nx2, ny2 };

        if (maskMat.empty()) {
            croppedMask.release();
            return true;
        }

        try {
            const int dh0 = y2 - y1;
            const int dw0 = x2 - x1;
            if (dh0 <= 0 || dw0 <= 0) {
                croppedMask.release();
                return true;
            }
            if (maskMat.rows != dh0 || maskMat.cols != dw0) {
                croppedMask.release();
                return true;
            }

            const int cutL = std::max(0, nx1 - x1);
            const int cutT = std::max(0, ny1 - y1);
            const int cutR = std::max(0, x2 - nx2);
            const int cutB = std::max(0, y2 - ny2);

            const int rw = maskMat.cols - cutL - cutR;
            const int rh = maskMat.rows - cutT - cutB;
            if (rw <= 0 || rh <= 0) {
                croppedMask.release();
                return true;
            }

            const cv::Rect cropRect(cutL, cutT, rw, rh);
            croppedMask = maskMat(cropRect).clone();
            return true;
        } catch (...) {
            croppedMask.release();
            return true;
        }
    }

    static bool CheckMaskOverlapWithRegion(const cv::Mat& maskMat0,
                                           const Box& bboxXYXY,
                                           const Box& roiXYXY,
                                           int W,
                                           int H) {
        if (maskMat0.empty()) return false;
        Box clampedBbox{};
        cv::Mat croppedMask;
        if (!ClampBboxAndCropMask(bboxXYXY, maskMat0, W, H, clampedBbox, croppedMask)) return false;

        cv::Mat workMask = croppedMask.empty() ? maskMat0 : croppedMask;
        const int bw = clampedBbox[2] - clampedBbox[0];
        const int bh = clampedBbox[3] - clampedBbox[1];
        if (bw <= 0 || bh <= 0) return false;

        cv::Mat resized;
        if (workMask.cols != bw || workMask.rows != bh) {
            cv::resize(workMask, resized, cv::Size(bw, bh), 0, 0, cv::INTER_NEAREST);
            workMask = resized;
        }

        const int ix1 = std::max(clampedBbox[0], roiXYXY[0]);
        const int iy1 = std::max(clampedBbox[1], roiXYXY[1]);
        const int ix2 = std::min(clampedBbox[2], roiXYXY[2]);
        const int iy2 = std::min(clampedBbox[3], roiXYXY[3]);
        if (ix2 <= ix1 || iy2 <= iy1) return false;

        int lx1 = ix1 - clampedBbox[0];
        int ly1 = iy1 - clampedBbox[1];
        int lx2 = ix2 - clampedBbox[0];
        int ly2 = iy2 - clampedBbox[1];
        lx1 = std::max(0, std::min(bw, lx1));
        ly1 = std::max(0, std::min(bh, ly1));
        lx2 = std::max(lx1 + 1, std::min(bw, lx2));
        ly2 = std::max(ly1 + 1, std::min(bh, ly2));
        if (lx2 <= lx1 || ly2 <= ly1) return false;

        const cv::Rect roiSrc(lx1, ly1, lx2 - lx1, ly2 - ly1);
        cv::Mat sub = workMask(roiSrc);
        return cv::countNonZero(sub) > 0;
    }

    static cv::Mat TryParseMaskArrayToMat(const Json& token) {
        if (!token.is_array() || token.empty()) return cv::Mat();
        if (!token.at(0).is_array()) return cv::Mat();
        const int H = static_cast<int>(token.size());
        const int W = static_cast<int>(token.at(0).size());
        if (H <= 0 || W <= 0) return cv::Mat();

        cv::Mat mask(H, W, CV_8UC1, cv::Scalar(0));
        for (int y = 0; y < H; y++) {
            if (!token.at(static_cast<size_t>(y)).is_array() ||
                static_cast<int>(token.at(static_cast<size_t>(y)).size()) != W) {
                return cv::Mat();
            }
            for (int x = 0; x < W; x++) {
                double dv = 0.0;
                if (TryToDouble(token.at(static_cast<size_t>(y)).at(static_cast<size_t>(x)), dv) && dv > 0.0) {
                    mask.at<unsigned char>(y, x) = 255;
                }
            }
        }
        return mask;
    }

    static bool TryExtractScore(const Json& det, double& scoreOut) {
        if (!det.is_object()) return false;
        static const std::array<const char*, 3> kScoreKeys = { "score", "conf", "confidence" };
        for (const char* key : kScoreKeys) {
            try {
                if (!det.contains(key) || det.at(key).is_null()) continue;
                double v = 0.0;
                if (!TryToDouble(det.at(key), v)) continue;
                if (std::isnan(v) || std::isinf(v)) continue;
                scoreOut = v;
                return true;
            } catch (...) {}
        }
        return false;
    }

    static Json ConvertDetToOriginal(const Json& det, const Box& bboxOriXYXY) {
        if (!det.is_object()) return det;
        Json d2 = det;
        const int ox1 = bboxOriXYXY[0];
        const int oy1 = bboxOriXYXY[1];
        const int ox2 = bboxOriXYXY[2];
        const int oy2 = bboxOriXYXY[3];
        const int ow = std::max(1, ox2 - ox1);
        const int oh = std::max(1, oy2 - oy1);
        d2["bbox"] = Json::array({ ox1, oy1, ow, oh });
        try {
            Json meta = Json::object();
            if (d2.contains("metadata") && d2.at("metadata").is_object()) {
                meta = d2.at("metadata");
            }
            meta["global_bbox"] = Json::array({ ox1, oy1, ox2, oy2 });
            d2["metadata"] = std::move(meta);
        } catch (...) {}
        return d2;
    }

    std::pair<bool, Json> ReadResultRegionInput() const {
        if (ExtraInputsIn.empty()) {
            return { false, Json::array() };
        }
        try {
            const ModuleChannel& ch = ExtraInputsIn[0];
            return { true, ch.ResultList.is_array() ? ch.ResultList : Json::array() };
        } catch (...) {
            return { true, Json::array() };
        }
    }
};

class ResultFilterRegionGlobalModule final : public ResultFilterRegionModule {
public:
    using ResultFilterRegionModule::ResultFilterRegionModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        // 对齐 C#：global 变体强制按原图坐标判定，但不改写输出坐标系。
        return ProcessInternal(imageList, resultList,
                               /*hasForceOriginalOverride*/ true, /*forceOriginalOverride*/ true,
                               /*hasConvertOutputOverride*/ true, /*convertOutputOverride*/ false);
    }
};

// -------------------- StrokeToPoints (简化版) --------------------
class StrokeToPointsModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        // counts_dict: { category_name: count }
        std::unordered_map<std::string, int> counts;
        try {
            if (Properties.is_object() && Properties.contains("counts_dict") && Properties.at("counts_dict").is_object()) {
                for (auto it = Properties.at("counts_dict").begin(); it != Properties.at("counts_dict").end(); ++it) {
                    try { counts[it.key()] = it.value().get<int>(); } catch (...) {}
                }
            }
        } catch (...) {}
        const int pointW = std::max(1, ReadInt("point_width", 10));
        const int pointH = std::max(1, ReadInt("point_height", 10));
        if (counts.empty()) return ModuleIO(images, Json::array(), Json::array());

        Json outResults = Json::array();
        for (const auto& token : results) {
            if (!token.is_object()) continue;
            Json entry = token;
            if (entry.value("type", "") != "local") continue;

            int W = 0, H = 0;
            try {
                if (entry.contains("transform") && entry.at("transform").is_object()) {
                    const Json& t = entry.at("transform");
                    if (t.contains("output_size") && t.at("output_size").is_array() && t.at("output_size").size() >= 2) {
                        W = t.at("output_size").at(0).get<int>();
                        H = t.at("output_size").at(1).get<int>();
                    }
                }
            } catch (...) {}
            if ((W <= 0 || H <= 0) && entry.contains("index")) {
                const int idx = entry.value("index", 0);
                if (idx >= 0 && idx < static_cast<int>(images.size()) && !images[static_cast<size_t>(idx)].ImageObject.empty()) {
                    W = images[static_cast<size_t>(idx)].ImageObject.cols;
                    H = images[static_cast<size_t>(idx)].ImageObject.rows;
                }
            }
            if (W <= 0 || H <= 0) {
                try {
                    if (entry.contains("transform") && entry.at("transform").is_object()) {
                        W = entry.at("transform").value("original_width", 0);
                        H = entry.at("transform").value("original_height", 0);
                    }
                } catch (...) {}
            }
            if (W <= 0 || H <= 0) continue;

            std::unordered_map<std::string, cv::Mat> maskByCat;
            if (entry.contains("sample_results") && entry.at("sample_results").is_array()) {
                for (const auto& s : entry.at("sample_results")) {
                    if (!s.is_object()) continue;
                    const std::string cat = s.value("category_name", "");
                    if (cat.empty() || !counts.count(cat)) continue;
                    if (!s.contains("mask_rle") || !s.at("mask_rle").is_object()) continue;

                    cv::Mat localMask = MaskInfoToMat(s.at("mask_rle"));
                    if (localMask.empty()) continue;

                    int x0 = 0, y0 = 0, roiW = localMask.cols, roiH = localMask.rows;
                    try {
                        if (s.contains("bbox") && s.at("bbox").is_array() && s.at("bbox").size() >= 4) {
                            const Json& bb = s.at("bbox");
                            if (bb.size() == 4) {
                                x0 = static_cast<int>(std::llround(bb.at(0).get<double>()));
                                y0 = static_cast<int>(std::llround(bb.at(1).get<double>()));
                                roiW = std::max(0, static_cast<int>(std::llround(std::abs(bb.at(2).get<double>()))));
                                roiH = std::max(0, static_cast<int>(std::llround(std::abs(bb.at(3).get<double>()))));
                            } else {
                                const double cx = bb.at(0).get<double>();
                                const double cy = bb.at(1).get<double>();
                                roiW = std::max(0, static_cast<int>(std::llround(std::abs(bb.at(2).get<double>()))));
                                roiH = std::max(0, static_cast<int>(std::llround(std::abs(bb.at(3).get<double>()))));
                                x0 = static_cast<int>(std::llround(cx - roiW / 2.0));
                                y0 = static_cast<int>(std::llround(cy - roiH / 2.0));
                            }
                        }
                    } catch (...) {}
                    if (roiW <= 0 || roiH <= 0) continue;

                    cv::Mat patch = localMask;
                    if (patch.cols != roiW || patch.rows != roiH) {
                        cv::Mat resized;
                        cv::resize(patch, resized, cv::Size(roiW, roiH), 0, 0, cv::INTER_NEAREST);
                        patch = resized;
                    }

                    int ix0 = std::max(0, x0);
                    int iy0 = std::max(0, y0);
                    int ix1 = std::min(W, x0 + patch.cols);
                    int iy1 = std::min(H, y0 + patch.rows);
                    const int rw = ix1 - ix0;
                    const int rh = iy1 - iy0;
                    if (rw <= 0 || rh <= 0) continue;
                    const int sx0 = ix0 - x0;
                    const int sy0 = iy0 - y0;

                    if (!maskByCat.count(cat)) {
                        maskByCat[cat] = cv::Mat(H, W, CV_8UC1, cv::Scalar(0));
                    }
                    cv::Mat& dst = maskByCat[cat];
                    cv::Mat roiDst = dst(cv::Rect(ix0, iy0, rw, rh));
                    cv::Mat roiSrc = patch(cv::Rect(sx0, sy0, rw, rh));
                    cv::bitwise_or(roiDst, roiSrc, roiDst);
                }
            }

            Json pointsItems = Json::array();
            for (const auto& kv : counts) {
                const std::string& cat = kv.first;
                const int cnt = std::max(0, kv.second);
                if (cnt <= 0) continue;
                if (!maskByCat.count(cat)) continue;
                cv::Mat& m = maskByCat[cat];
                std::vector<cv::Point> pts;
                cv::findNonZero(m, pts);
                if (pts.empty()) continue;
                const int step = std::max(1, static_cast<int>(pts.size() / static_cast<size_t>(cnt)));
                for (int i = 0; i < cnt; i++) {
                    const cv::Point p = pts[static_cast<size_t>((i * step) % static_cast<int>(pts.size()))];
                    int bx = p.x - pointW / 2;
                    int by = p.y - pointH / 2;
                    bx = std::max(0, std::min(W - 1, bx));
                    by = std::max(0, std::min(H - 1, by));
                    Json det = Json::object();
                    det["category_id"] = 0;
                    det["category_name"] = cat;
                    det["score"] = 1.0;
                    det["bbox"] = Json::array({ bx, by, pointW, pointH });
                    det["with_bbox"] = true;
                    det["with_mask"] = false;
                    det["with_angle"] = false;
                    det["angle"] = -100.0;
                    pointsItems.push_back(det);
                }
            }

            entry["sample_results"] = pointsItems;
            outResults.push_back(entry);
        }

        return ModuleIO(images, outResults, Json::array());
    }
};

// -------------------- Visualize (简化版) --------------------
static cv::Scalar ReadColorBgr(const Json& props, const std::string& key, cv::Scalar dv) {
    if (!props.is_object() || !props.contains(key)) return dv;
    try {
        const Json& v = props.at(key);
        if (v.is_array() && v.size() >= 3) {
            const int b = v.at(0).get<int>();
            const int g = v.at(1).get<int>();
            const int r = v.at(2).get<int>();
            return cv::Scalar(b, g, r);
        }
    } catch (...) {}
    return dv;
}

static std::vector<double> Inverse2x3FromTransform(const Json& tObj) {
    if (!tObj.is_object() || !tObj.contains("affine_2x3") || !tObj.at("affine_2x3").is_array()) return {1,0,0, 0,1,0};
    std::vector<double> a(6, 0.0);
    try {
        for (size_t i = 0; i < 6; i++) a[i] = tObj.at("affine_2x3").at(i).get<double>();
    } catch (...) { return {1,0,0, 0,1,0}; }
    return TransformationState::Inverse2x3(a);
}

static cv::Point2f Apply2x3(const std::vector<double>& A, const cv::Point2f& p) {
    const double a = A.size() >= 6 ? A[0] : 1.0;
    const double b = A.size() >= 6 ? A[1] : 0.0;
    const double tx = A.size() >= 6 ? A[2] : 0.0;
    const double c = A.size() >= 6 ? A[3] : 0.0;
    const double d = A.size() >= 6 ? A[4] : 1.0;
    const double ty = A.size() >= 6 ? A[5] : 0.0;
    return cv::Point2f(static_cast<float>(a * p.x + b * p.y + tx),
                       static_cast<float>(c * p.x + d * p.y + ty));
}

class VisualizeOnOriginalModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        const bool blackBg = ReadBool("black_background", false);
        const bool displayBbox = ReadBool("display_bbox", true);
        const bool displayText = ReadBool("display_text", true);
        const bool displayScore = ReadBool("display_score", true);
        const double fontScale = ReadDouble("font_scale", 0.5);
        const int fontThickness = std::max(1, ReadInt("font_thickness", 1));
        const cv::Scalar bboxColor = ReadColorBgr(Properties, "bbox_color", cv::Scalar(0, 255, 0));
        const cv::Scalar bboxColorRot = ReadColorBgr(Properties, "bbox_color_rot", cv::Scalar(0, 128, 255));

        // origin_index -> canvas
        std::unordered_map<int, cv::Mat> canvasMap;
        std::unordered_map<int, int> areaMap;
        for (const auto& wrap : images) {
            if (wrap.ImageObject.empty() && wrap.OriginalImage.empty()) continue;
            const int originIndex = wrap.OriginalIndex;
            const cv::Mat originMat = wrap.OriginalImage.empty() ? wrap.ImageObject : wrap.OriginalImage;
            if (originMat.empty()) continue;
            const int area = originMat.cols * originMat.rows;
            if (!areaMap.count(originIndex) || area > areaMap[originIndex]) {
                areaMap[originIndex] = area;
                if (blackBg) canvasMap[originIndex] = cv::Mat(originMat.rows, originMat.cols, originMat.type(), cv::Scalar(0,0,0));
                else canvasMap[originIndex] = originMat.clone();
            }
        }

        for (const auto& token : results) {
            if (!token.is_object()) continue;
            const Json& entry = token;
            const int originIndex = entry.value("origin_index", entry.value("index", 0));
            if (!canvasMap.count(originIndex)) continue;
            cv::Mat& target = canvasMap[originIndex];
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) continue;

            const std::vector<double> inv2x3 = (entry.contains("transform") && entry.at("transform").is_object())
                ? Inverse2x3FromTransform(entry.at("transform"))
                : std::vector<double>{1,0,0,0,1,0};

            for (const auto& s : entry.at("sample_results")) {
                if (!s.is_object()) continue;
                if (!displayBbox) continue;
                if (!s.contains("bbox") || !s.at("bbox").is_array() || s.at("bbox").size() < 4) continue;
                const Json& bb = s.at("bbox");
                bool withAngle = s.value("with_angle", false);
                double ang = s.value("angle", -100.0);
                if ((!withAngle || ang <= -99.0) && bb.size() >= 5) { try { ang = bb.at(4).get<double>(); withAngle = true; } catch (...) {} }
                std::vector<cv::Point> pts;
                if (withAngle && ang > -99.0) {
                    const double cx = bb.at(0).get<double>();
                    const double cy = bb.at(1).get<double>();
                    const double w = std::abs(bb.at(2).get<double>());
                    const double h = std::abs(bb.at(3).get<double>());
                    const double hw = w / 2.0, hh = h / 2.0;
                    const double c = std::cos(ang), sn = std::sin(ang);
                    const double dx[4] = { -hw, hw, hw, -hw };
                    const double dy[4] = { -hh, -hh, hh, hh };
                    for (int i = 0; i < 4; i++) {
                        const double x = cx + c * dx[i] - sn * dy[i];
                        const double y = cy + sn * dx[i] + c * dy[i];
                        const cv::Point2f g = Apply2x3(inv2x3, cv::Point2f(static_cast<float>(x), static_cast<float>(y)));
                        pts.push_back(cv::Point(static_cast<int>(std::llround(g.x)), static_cast<int>(std::llround(g.y))));
                    }
                    cv::polylines(target, pts, true, bboxColorRot, 2, cv::LINE_AA);
                } else {
                    const double x = bb.at(0).get<double>();
                    const double y = bb.at(1).get<double>();
                    const double w = bb.at(2).get<double>();
                    const double h = bb.at(3).get<double>();
                    const cv::Point2f p1 = Apply2x3(inv2x3, cv::Point2f(static_cast<float>(x), static_cast<float>(y)));
                    const cv::Point2f p2 = Apply2x3(inv2x3, cv::Point2f(static_cast<float>(x+w), static_cast<float>(y)));
                    const cv::Point2f p3 = Apply2x3(inv2x3, cv::Point2f(static_cast<float>(x+w), static_cast<float>(y+h)));
                    const cv::Point2f p4 = Apply2x3(inv2x3, cv::Point2f(static_cast<float>(x), static_cast<float>(y+h)));
                    pts = { cv::Point((int)std::llround(p1.x),(int)std::llround(p1.y)),
                            cv::Point((int)std::llround(p2.x),(int)std::llround(p2.y)),
                            cv::Point((int)std::llround(p3.x),(int)std::llround(p3.y)),
                            cv::Point((int)std::llround(p4.x),(int)std::llround(p4.y)) };
                    cv::polylines(target, pts, true, bboxColor, 2, cv::LINE_AA);
                }

                if (displayText) {
                    std::string label = s.value("category_name", "");
                    if (displayScore) {
                        try {
                            const double sc = s.value("score", 0.0);
                            char buf[64]; std::snprintf(buf, sizeof(buf), " %.2f", sc);
                            label += std::string(buf);
                        } catch (...) {}
                    }
                    if (!label.empty() && !pts.empty()) {
                        int minx = pts[0].x, miny = pts[0].y;
                        for (const auto& p : pts) { minx = std::min(minx, p.x); miny = std::min(miny, p.y); }
                        cv::putText(target, label, cv::Point(minx, std::max(0, miny - 4)),
                                    cv::FONT_HERSHEY_SIMPLEX, fontScale, cv::Scalar(0,0,0), fontThickness + 1, cv::LINE_AA);
                        cv::putText(target, label, cv::Point(minx, std::max(0, miny - 4)),
                                    cv::FONT_HERSHEY_SIMPLEX, fontScale, cv::Scalar(255,255,255), fontThickness, cv::LINE_AA);
                    }
                }
            }
        }

        std::vector<ModuleImage> outImages;
        for (const auto& kv : canvasMap) {
            const int originIndex = kv.first;
            const cv::Mat& mat = kv.second;
            if (mat.empty()) continue;
            TransformationState st(mat.cols, mat.rows);
            ModuleImage wrap(mat, mat, st, originIndex);
            outImages.push_back(wrap);
        }

        return ModuleIO(std::move(outImages), results, Json::array());
    }
};

class VisualizeOnLocalModule final : public BaseModule {
public:
    using BaseModule::BaseModule;
    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();
        const cv::Scalar bboxColor = ReadColorBgr(Properties, "bbox_color", cv::Scalar(0, 255, 0));
        const double fontScale = ReadDouble("font_scale", 0.5);
        const int fontThickness = std::max(1, ReadInt("font_thickness", 1));

        std::vector<ModuleImage> outImages;
        for (int i = 0; i < static_cast<int>(images.size()); i++) {
            const ModuleImage& wrap = images[static_cast<size_t>(i)];
            if (wrap.ImageObject.empty()) continue;
            cv::Mat canvas = wrap.ImageObject.clone();
            for (const auto& token : results) {
                if (!token.is_object()) continue;
                const Json& entry = token;
                if (entry.value("type", "") != "local") continue;
                if (entry.value("index", -1) != i) continue;
                if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) continue;
                for (const auto& s : entry.at("sample_results")) {
                    if (!s.is_object()) continue;
                    if (!s.contains("bbox") || !s.at("bbox").is_array() || s.at("bbox").size() < 4) continue;
                    const Json& bb = s.at("bbox");
                    const int x = (int)std::llround(bb.at(0).get<double>());
                    const int y = (int)std::llround(bb.at(1).get<double>());
                    const int w = (int)std::llround(bb.at(2).get<double>());
                    const int h = (int)std::llround(bb.at(3).get<double>());
                    cv::rectangle(canvas, cv::Rect(x, y, w, h), bboxColor, 2);
                    const std::string label = s.value("category_name", "");
                    if (!label.empty()) {
                        cv::putText(canvas, label, cv::Point(x, std::max(0, y - 4)),
                                    cv::FONT_HERSHEY_SIMPLEX, fontScale, cv::Scalar(0,0,0), fontThickness + 1, cv::LINE_AA);
                        cv::putText(canvas, label, cv::Point(x, std::max(0, y - 4)),
                                    cv::FONT_HERSHEY_SIMPLEX, fontScale, cv::Scalar(255,255,255), fontThickness, cv::LINE_AA);
                    }
                }
            }
            ModuleImage out(canvas, wrap.OriginalImage.empty() ? wrap.ImageObject : wrap.OriginalImage, wrap.TransformState, wrap.OriginalIndex);
            outImages.push_back(out);
        }
        return ModuleIO(std::move(outImages), results, Json::array());
    }
};

// -------------------- Templates (简化版) --------------------
class TemplateFromResultsModule final : public BaseModule {
public:
    using BaseModule::BaseModule;
    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        std::string productName = ReadString("product_name", "");
        std::string productId = ReadString("product_id", "");
        std::string templateName = ReadString("template_name", productName);

        // context barcode_text 覆盖 product_id
        try {
            if (Context) {
                const std::string barcode = Context->Get<std::string>("barcode_text", "");
                if (!barcode.empty()) productId = barcode;
            }
        } catch (...) {}

        int cameraPos = 0;
        try {
            if (Context) {
                const std::string face = Context->Get<std::string>("face", "");
                if (!face.empty()) {
                    const char c = static_cast<char>(std::toupper(static_cast<unsigned char>(face[0])));
                    if (c == 'A') cameraPos = 0;
                    else if (c == 'B') cameraPos = 1;
                    else if (c == 'C') cameraPos = 2;
                    else if (c == 'D') cameraPos = 3;
                }
            }
        } catch (...) {}

        Json tpl = Json::object();
        tpl["template_name"] = templateName;
        tpl["product_name"] = productName;
        tpl["product_id"] = productId;
        tpl["camera_position"] = cameraPos;

        // OCRResults from local entries
        Json ocrArr = Json::array();
        std::unordered_set<std::string> seen;
        for (const auto& token : results) {
            if (!token.is_object()) continue;
            const Json& entry = token;
            if (entry.value("type", "") != "local") continue;
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) continue;
            std::vector<double> inv = {1,0,0, 0,1,0};
            try {
                if (entry.contains("transform") && entry.at("transform").is_object()) {
                    inv = Inverse2x3FromTransform(entry.at("transform"));
                }
            } catch (...) {}

            for (const auto& s : entry.at("sample_results")) {
                if (!s.is_object()) continue;
                const Json& so = s;
                std::string text = so.value("category_name", "");
                if (text.empty()) text = so.value("text", "");
                if (text.empty()) continue;
                const double conf = so.value("score", 0.0);
                if (!so.contains("bbox") || !so.at("bbox").is_array() || so.at("bbox").size() < 4) continue;
                const Json& bb = so.at("bbox");
                bool withAngle = so.value("with_angle", false);
                double ang = so.value("angle", -100.0);
                if ((!withAngle || ang <= -99.0) && bb.size() >= 5) { try { ang = bb.at(4).get<double>(); withAngle = true; } catch (...) {} }

                std::vector<cv::Point2f> corners;
                if (withAngle && ang > -99.0) {
                    const double cx = bb.at(0).get<double>();
                    const double cy = bb.at(1).get<double>();
                    const double w = std::abs(bb.at(2).get<double>());
                    const double h = std::abs(bb.at(3).get<double>());
                    const double hw = w / 2.0, hh = h / 2.0;
                    const double c = std::cos(ang), sn = std::sin(ang);
                    const double dx[4] = { -hw, hw, hw, -hw };
                    const double dy[4] = { -hh, -hh, hh, hh };
                    for (int i = 0; i < 4; i++) {
                        const double x = cx + c * dx[i] - sn * dy[i];
                        const double y = cy + sn * dx[i] + c * dy[i];
                        corners.push_back(Apply2x3(inv, cv::Point2f((float)x,(float)y)));
                    }
                } else {
                    const double x = bb.at(0).get<double>();
                    const double y = bb.at(1).get<double>();
                    const double w = bb.at(2).get<double>();
                    const double h = bb.at(3).get<double>();
                    corners = { Apply2x3(inv, cv::Point2f((float)x,(float)y)),
                                Apply2x3(inv, cv::Point2f((float)(x+w),(float)y)),
                                Apply2x3(inv, cv::Point2f((float)(x+w),(float)(y+h))),
                                Apply2x3(inv, cv::Point2f((float)x,(float)(y+h))) };
                }
                float minx=corners[0].x,miny=corners[0].y,maxx=corners[0].x,maxy=corners[0].y;
                for (const auto& p : corners) { minx=std::min(minx,p.x); miny=std::min(miny,p.y); maxx=std::max(maxx,p.x); maxy=std::max(maxy,p.y); }
                const int ix = (int)std::floor(minx);
                const int iy = (int)std::floor(miny);
                const int iw = std::max(1, (int)std::ceil(maxx - minx));
                const int ih = std::max(1, (int)std::ceil(maxy - miny));
                const std::string key = NormalizeTextSimple(text) + "|" + std::to_string(ix) + "," + std::to_string(iy) + "," + std::to_string(iw) + "," + std::to_string(ih);
                if (!seen.insert(key).second) continue;
                Json item = Json::object();
                item["text"] = text;
                item["confidence"] = conf;
                item["x"] = ix;
                item["y"] = iy;
                item["width"] = iw;
                item["height"] = ih;
                ocrArr.push_back(item);
            }
        }
        tpl["OCRResults"] = ocrArr;

        if (!tpl.contains("template_id") || tpl.at("template_id").is_null()) {
            std::string baseName = templateName.empty() ? (productName.empty() ? "Template" : productName) : templateName;
            tpl["template_id"] = MakeSafeFileName(baseName);
        }

        Json templates = Json::array({ tpl });
        return ModuleIO(images, results, templates);
    }
};

class TemplateSaveModule final : public BaseModule {
public:
    using BaseModule::BaseModule;
    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        (void)resultList;
        if (!MainTemplateList.is_array() || MainTemplateList.empty() || !MainTemplateList.at(0).is_object()) {
            return ModuleIO(imageList, Json::array(), Json::array());
        }
        Json tpl = MainTemplateList.at(0);

        std::string dir;
        try { if (Context) dir = Context->Get<std::string>("templates_dir", ""); } catch (...) {}
        if (dir.empty()) dir = "模版";
        EnsureDirExists(dir);

        std::string fileName = ReadString("file_name", "");
        if (fileName.empty()) {
            try { fileName = tpl.value("template_id", "Template"); } catch (...) { fileName = "Template"; }
        }
        fileName = MakeSafeFileName(fileName);

        const std::string jsonPath = JoinPath(dir, fileName + ".json");
        const std::string pngPath = JoinPath(dir, fileName + ".png");

        // save image
        if (!imageList.empty() && !imageList[0].ImageObject.empty()) {
            try {
                cv::imwrite(pngPath, imageList[0].ImageObject);
                tpl["image_path"] = fileName + ".png";
            } catch (...) {}
        }

        try {
            std::ofstream ofs(jsonPath, std::ios::binary);
            ofs << tpl.dump(2);
        } catch (...) {}

        return ModuleIO(std::vector<ModuleImage>(), Json::array(), Json::array());
    }
};

class TemplateLoadModule final : public BaseModule {
public:
    using BaseModule::BaseModule;
    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();
        const std::string path = ReadString("path", "");
        if (path.empty()) return ModuleIO(images, results, Json::array());
        try {
            std::ifstream ifs(path, std::ios::binary);
            if (!ifs) return ModuleIO(images, results, Json::array());
            std::string s((std::istreambuf_iterator<char>(ifs)), std::istreambuf_iterator<char>());
            Json tpl = Json::parse(s);
            Json templates = Json::array({ tpl });
            return ModuleIO(images, results, templates);
        } catch (...) {}
        return ModuleIO(images, results, Json::array());
    }
};

class TemplateMatchModule final : public BaseModule {
public:
    using BaseModule::BaseModule;
    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        (void)imageList; (void)resultList;

        if (!MainTemplateList.is_array() || MainTemplateList.empty() || !MainTemplateList.at(0).is_object()) {
            return ModuleIO(std::vector<ModuleImage>(), Json::array(), Json::array());
        }
        Json toCheck = MainTemplateList.at(0);
        Json golden;
        if (!ExtraInputsIn.empty() && ExtraInputsIn[0].TemplateList.is_array() && !ExtraInputsIn[0].TemplateList.empty()) {
            golden = ExtraInputsIn[0].TemplateList.at(0);
        }
        if (!golden.is_object()) {
            return ModuleIO(std::vector<ModuleImage>(), Json::array(), Json::array());
        }

        const double posTolX = ReadDouble("position_tolerance_x", 20.0);
        const double posTolY = ReadDouble("position_tolerance_y", 20.0);
        const double minConf = ReadDouble("min_confidence_threshold", 0.5);
        const bool checkPos = ReadBool("check_position", true);
        const double errTh = std::sqrt(posTolX * posTolX + posTolY * posTolY);

        auto getItems = [&](const Json& tpl) -> std::vector<Json> {
            std::vector<Json> v;
            try {
                if (tpl.contains("OCRResults") && tpl.at("OCRResults").is_array()) {
                    for (const auto& it : tpl.at("OCRResults")) if (it.is_object()) v.push_back(it);
                }
            } catch (...) {}
            return v;
        };

        auto tplItems = getItems(golden);
        auto detItemsAll = getItems(toCheck);
        std::vector<Json> detItems;
        for (const auto& it : detItemsAll) {
            const double conf = it.value("confidence", 0.0);
            const int w = it.value("width", 0);
            const int h = it.value("height", 0);
            if (conf >= minConf && w > 0 && h > 0) detItems.push_back(it);
        }

        int matched = 0;
        std::vector<bool> used(detItems.size(), false);
        for (const auto& t : tplItems) {
            const std::string tText = NormalizeTextSimple(t.value("text", ""));
            const double tcx = t.value("x", 0) + t.value("width", 0) / 2.0;
            const double tcy = t.value("y", 0) + t.value("height", 0) / 2.0;
            bool okOne = false;
            for (size_t di = 0; di < detItems.size(); di++) {
                if (used[di]) continue;
                const Json& d = detItems[di];
                const std::string dText = NormalizeTextSimple(d.value("text", ""));
                if (tText != dText) continue;
                if (checkPos) {
                    const double dcx = d.value("x", 0) + d.value("width", 0) / 2.0;
                    const double dcy = d.value("y", 0) + d.value("height", 0) / 2.0;
                    const double dx = tcx - dcx;
                    const double dy = tcy - dcy;
                    const double dist = std::sqrt(dx * dx + dy * dy);
                    if (dist > errTh) continue;
                }
                used[di] = true;
                okOne = true;
                matched++;
                break;
            }
            if (!okOne) {
                // continue, count missed later
            }
        }

        const int totalTpl = static_cast<int>(tplItems.size());
        int usedCount = 0;
        for (bool u : used) if (u) usedCount++;
        const int missed = totalTpl - matched;
        const int over = static_cast<int>(detItems.size()) - usedCount;
        const bool ok = (missed == 0 && over == 0);
        const double score = (totalTpl > 0) ? (static_cast<double>(matched) / static_cast<double>(totalTpl)) : 1.0;

        Json detail = Json::object({
            {"is_match", ok},
            {"score", score},
            {"matched", matched},
            {"missed", missed},
            {"over", over},
            {"total_template", totalTpl},
            {"total_detection", static_cast<int>(detItems.size())}
        });

        this->ScalarOutputsByName["ok"] = ok;
        this->ScalarOutputsByName["detail"] = detail.dump();
        return ModuleIO(std::vector<ModuleImage>(), Json::array(), Json::array());
    }
};

// 注册
DLCV_FLOW_REGISTER_MODULE("post_process/result_filter_region", ResultFilterRegionModule)
DLCV_FLOW_REGISTER_MODULE("features/result_filter_region", ResultFilterRegionModule)
DLCV_FLOW_REGISTER_MODULE("post_process/result_filter_region_global", ResultFilterRegionGlobalModule)
DLCV_FLOW_REGISTER_MODULE("features/result_filter_region_global", ResultFilterRegionGlobalModule)

DLCV_FLOW_REGISTER_MODULE("features/stroke_to_points", StrokeToPointsModule)

DLCV_FLOW_REGISTER_MODULE("output/visualize", VisualizeOnOriginalModule)
DLCV_FLOW_REGISTER_MODULE("output/visualize_local", VisualizeOnLocalModule)

DLCV_FLOW_REGISTER_MODULE("features/template_from_results", TemplateFromResultsModule)
DLCV_FLOW_REGISTER_MODULE("features/template_save", TemplateSaveModule)
DLCV_FLOW_REGISTER_MODULE("features/template_load", TemplateLoadModule)
DLCV_FLOW_REGISTER_MODULE("features/template_match", TemplateMatchModule)
DLCV_FLOW_REGISTER_MODULE("features/printed_template_match", TemplateMatchModule)

} // namespace flow
} // namespace dlcv_infer

