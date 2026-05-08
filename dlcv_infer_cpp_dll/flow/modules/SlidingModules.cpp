#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include "flow/utils/MaskRleUtils.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cctype>
#include <cstdint>
#include <cstdio>
#include <iomanip>
#include <limits>
#include <map>
#include <set>
#include <sstream>
#include <string>
#include <unordered_map>
#include <unordered_set>

#include "opencv2/imgproc.hpp"

namespace dlcv_infer {
namespace flow {

static std::pair<int, int> ReadInt2(const Json& props, const std::string& key, int dv1, int dv2) {
    if (!props.is_object() || !props.contains(key)) return { dv1, dv2 };
    const Json& v = props.at(key);
    try {
        if (v.is_array() && v.size() >= 2) {
            return { v.at(0).get<int>(), v.at(1).get<int>() };
        }
        if (v.is_string()) {
            const std::string s = v.get<std::string>();
            int a = dv1, b = dv2;
            if (sscanf(s.c_str(), "%d%*[,; ]%d", &a, &b) == 2) {
                return { a, b };
            }
        }
    } catch (...) {}
    return { dv1, dv2 };
}

static std::uint64_t ReadCurrentOutputMask(const ExecutionContext* ctx) {
    if (ctx == nullptr) return std::numeric_limits<std::uint64_t>::max();
    try {
        return ctx->Get<std::uint64_t>(
            "__graph_current_output_mask",
            std::numeric_limits<std::uint64_t>::max());
    } catch (...) {
        return std::numeric_limits<std::uint64_t>::max();
    }
}

static bool IsCurrentOutputConnected(const ExecutionContext* ctx, int outputIndex) {
    if (outputIndex < 0 || outputIndex >= 64) return true;
    const std::uint64_t mask = ReadCurrentOutputMask(ctx);
    if (mask == std::numeric_limits<std::uint64_t>::max()) return true;
    return ((mask >> outputIndex) & static_cast<std::uint64_t>(1)) != 0;
}

static bool TryReadDoubleToken(const Json& token, double& outVal) {
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

static std::vector<double> BuildTC2O(const TransformationState& st) {
    if (st.AffineMatrix2x3.size() != 6) return { 1,0,0, 0,1,0 };
    return TransformationState::Inverse2x3(st.AffineMatrix2x3);
}

static cv::Point2f TransformPoint2x3(const std::vector<double>& T, const cv::Point2f& pt) {
    const double a = (T.size() >= 6) ? T[0] : 1.0;
    const double b = (T.size() >= 6) ? T[1] : 0.0;
    const double tx = (T.size() >= 6) ? T[2] : 0.0;
    const double c = (T.size() >= 6) ? T[3] : 0.0;
    const double d = (T.size() >= 6) ? T[4] : 1.0;
    const double ty = (T.size() >= 6) ? T[5] : 0.0;
    return cv::Point2f(
        static_cast<float>(a * pt.x + b * pt.y + tx),
        static_cast<float>(c * pt.x + d * pt.y + ty));
}

static double ReadAngleRad(const Json& det, const Json& bbox) {
    double angle = 0.0;
    if (bbox.is_array() && bbox.size() >= 5) {
        if (TryReadDoubleToken(bbox.at(4), angle)) {
            return std::abs(angle) > 3.2 ? angle * CV_PI / 180.0 : angle;
        }
    }
    try {
        if (det.is_object() && det.contains("angle")) {
            angle = det.at("angle").get<double>();
            return std::abs(angle) > 3.2 ? angle * CV_PI / 180.0 : angle;
        }
    } catch (...) {}
    return 0.0;
}

static std::array<double, 4> AabbFromPoints(const std::vector<cv::Point2f>& pts) {
    if (pts.empty()) return { 0.0, 0.0, 0.0, 0.0 };
    double minx = pts[0].x;
    double miny = pts[0].y;
    double maxx = pts[0].x;
    double maxy = pts[0].y;
    for (const auto& p : pts) {
        minx = std::min(minx, static_cast<double>(p.x));
        miny = std::min(miny, static_cast<double>(p.y));
        maxx = std::max(maxx, static_cast<double>(p.x));
        maxy = std::max(maxy, static_cast<double>(p.y));
    }
    return { minx, miny, maxx, maxy };
}

static std::array<double, 4> RBoxAabb(double cx, double cy, double w, double h, double angleRad) {
    const double hw = std::max(0.0, w) / 2.0;
    const double hh = std::max(0.0, h) / 2.0;
    const double c = std::cos(angleRad);
    const double s = std::sin(angleRad);
    std::vector<cv::Point2f> pts;
    pts.reserve(4);
    const std::array<std::array<double, 2>, 4> offs = {{
        {{ -hw, -hh }},
        {{ hw, -hh }},
        {{ hw, hh }},
        {{ -hw, hh }}
    }};
    for (const auto& off : offs) {
        const double x = cx + c * off[0] - s * off[1];
        const double y = cy + s * off[0] + c * off[1];
        pts.emplace_back(static_cast<float>(x), static_cast<float>(y));
    }
    return AabbFromPoints(pts);
}

static double BoxIoU(const std::array<double, 4>& a, const std::array<double, 4>& b) {
    const double ix1 = std::max(a[0], b[0]);
    const double iy1 = std::max(a[1], b[1]);
    const double ix2 = std::min(a[2], b[2]);
    const double iy2 = std::min(a[3], b[3]);
    const double iw = std::max(0.0, ix2 - ix1);
    const double ih = std::max(0.0, iy2 - iy1);
    const double inter = iw * ih;
    if (inter <= 0.0) return 0.0;
    const double areaA = std::max(0.0, a[2] - a[0]) * std::max(0.0, a[3] - a[1]);
    const double areaB = std::max(0.0, b[2] - b[0]) * std::max(0.0, b[3] - b[1]);
    const double uni = areaA + areaB - inter;
    return uni > 0.0 ? (inter / uni) : 0.0;
}

static std::string CategoryKeyOfDet(const Json& det) {
    std::string name;
    int categoryId = 0;
    try { name = det.value("category_name", std::string()); } catch (...) { name.clear(); }
    try { categoryId = det.value("category_id", 0); } catch (...) { categoryId = 0; }
    return std::to_string(categoryId) + "|" + name;
}

struct SlidingMappedDet {
    Json Det;
    std::array<double, 4> Aabb = { 0.0, 0.0, 0.0, 0.0 };
    std::string CategoryKey;
    double Score = 0.0;
};

static bool TryReadBboxXywh(const Json& det, double& x, double& y, double& w, double& h) {
    if (!det.is_object() || !det.contains("bbox") || !det.at("bbox").is_array()) return false;
    const Json& bbox = det.at("bbox");
    if (bbox.size() < 4) return false;
    if (!TryReadDoubleToken(bbox.at(0), x) ||
        !TryReadDoubleToken(bbox.at(1), y) ||
        !TryReadDoubleToken(bbox.at(2), w) ||
        !TryReadDoubleToken(bbox.at(3), h)) {
        return false;
    }
    return true;
}

// ---- sliding_merge helpers（对齐 DlcvCsharpApi/SlidingMerge.cs）----

static std::string Round6Sliding(double v) {
    if (std::isnan(v) || std::isinf(v)) return "0";
    std::ostringstream oss;
    oss << std::fixed << std::setprecision(6) << v;
    std::string s = oss.str();
    while (!s.empty() && s.back() == '0') s.pop_back();
    if (!s.empty() && s.back() == '.') s.pop_back();
    return s.empty() ? "0" : s;
}

static bool TryReadAffine2x3FromTransformJson(const Json& transformObj, std::array<double, 6>& outAffine) {
    try {
        if (transformObj.contains("affine_2x3") && transformObj.at("affine_2x3").is_array() &&
            transformObj.at("affine_2x3").size() >= 6) {
            const Json& a23 = transformObj.at("affine_2x3");
            for (int i = 0; i < 6; i++) {
                if (!TryReadDoubleToken(a23.at(static_cast<size_t>(i)), outAffine[static_cast<size_t>(i)])) return false;
            }
            return true;
        }
    } catch (...) {}
    return false;
}

static std::string SerializeTransformStateSig(const TransformationState& st) {
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
        << "|A:" << Round6Sliding(a[0]) << "," << Round6Sliding(a[1]) << "," << Round6Sliding(a[2]) << ","
        << Round6Sliding(a[3]) << "," << Round6Sliding(a[4]) << "," << Round6Sliding(a[5]);
    return oss.str();
}

static std::string SerializeTransformJsonSig(const Json& transformObj) {
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
        int ow = transformObj.value("original_width", 0);
        int oh = transformObj.value("original_height", 0);
        std::array<double, 6> affine{};
        if (!TryReadAffine2x3FromTransformJson(transformObj, affine)) return std::string();
        std::ostringstream oss;
        oss << "cb:" << cbx << "," << cby << "," << cbw << "," << cbh
            << "|os:" << outW << "," << outH
            << "|ori:" << ow << "," << oh
            << "|A:" << Round6Sliding(affine[0]) << "," << Round6Sliding(affine[1]) << "," << Round6Sliding(affine[2]) << ","
            << Round6Sliding(affine[3]) << "," << Round6Sliding(affine[4]) << "," << Round6Sliding(affine[5]);
        return oss.str();
    } catch (...) {
        return std::string();
    }
}

static int64_t PackWinDet(int winIdx, int detIdx) {
    return (static_cast<int64_t>(static_cast<uint32_t>(winIdx)) << 32) |
           static_cast<uint64_t>(static_cast<uint32_t>(detIdx));
}

struct UnionFindPairs final {
    std::unordered_map<int64_t, int64_t> parent;
    void Add(int64_t x) {
        if (parent.find(x) == parent.end()) parent[x] = x;
    }
    int64_t Find(int64_t x) {
        Add(x);
        int64_t& p = parent[x];
        if (p != x) p = Find(p);
        return p;
    }
    void Union(int64_t a, int64_t b) {
        a = Find(a);
        b = Find(b);
        if (a != b) parent[b] = a;
    }
};

static int SafeJsonInt(const Json& o, const char* key, int dv) {
    try {
        if (o.is_object() && o.contains(key) && !o.at(key).is_null()) return o.at(key).get<int>();
    } catch (...) {}
    return dv;
}

static std::string NormalizeTaskType(std::string s) {
    size_t start = 0;
    while (start < s.size() && std::isspace(static_cast<unsigned char>(s[start]))) start++;
    size_t end = s.size();
    while (end > start && std::isspace(static_cast<unsigned char>(s[end - 1]))) end--;
    s = s.substr(start, end - start);
    for (char& c : s) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return s;
}

static bool IsRotatedDetJson(const Json& det) {
    if (!det.is_object()) return false;
    bool withAngle = false;
    try { withAngle = det.value("with_angle", false); } catch (...) { withAngle = false; }
    double angle = -100.0;
    (void)TryReadDoubleToken(det.contains("angle") ? det.at("angle") : Json(), angle);
    if (withAngle && std::abs(angle - (-100.0)) > 1e-8) return true;
    if (det.contains("bbox") && det.at("bbox").is_array() && det.at("bbox").size() >= 5) return true;
    try {
        const Json& meta = det.value("metadata", Json::object());
        if (meta.is_object() && meta.value("is_rotated", false)) return true;
    } catch (...) {}
    return false;
}

static bool SameCategoryJson(const Json& a, const Json& b) {
    if (!a.is_object() || !b.is_object()) return false;
    if (a.contains("category_id") && b.contains("category_id") && !a.at("category_id").is_null() && !b.at("category_id").is_null()) {
        try {
            return a.at("category_id").get<int>() == b.at("category_id").get<int>();
        } catch (...) {}
    }
    std::string ca;
    std::string cb;
    try { ca = a.value("category_name", std::string()); } catch (...) { ca.clear(); }
    try { cb = b.value("category_name", std::string()); } catch (...) { cb.clear(); }
    for (char& c : ca) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    for (char& c : cb) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    if (!ca.empty() || !cb.empty()) return ca == cb;
    return true;
}

static double IntersectionAreaAabb(const std::array<double, 4>& a, const std::array<double, 4>& b) {
    const double x1 = std::max(a[0], b[0]);
    const double y1 = std::max(a[1], b[1]);
    const double x2 = std::min(a[2], b[2]);
    const double y2 = std::min(a[3], b[3]);
    return std::max(0.0, x2 - x1) * std::max(0.0, y2 - y1);
}

static double ComputeIoS(const std::array<double, 4>& a, const std::array<double, 4>& b) {
    const double inter = IntersectionAreaAabb(a, b);
    if (inter <= 0.0) return 0.0;
    const double areaA = std::max(0.0, a[2] - a[0]) * std::max(0.0, a[3] - a[1]);
    const double areaB = std::max(0.0, b[2] - b[0]) * std::max(0.0, b[3] - b[1]);
    const double smaller = std::min(areaA, areaB);
    return smaller > 0.0 ? (inter / smaller) : 0.0;
}

static bool HasMaskPayload(const Json& det) {
    if (!det.is_object()) return false;
    if (det.contains("mask_rle") && det.at("mask_rle").is_object()) return true;
    if (det.contains("mask_array") && !det.at("mask_array").is_null()) return true;
    return false;
}

static bool TryAabbToIntXYXY(const std::array<double, 4>& aabb, int& x1, int& y1, int& x2, int& y2) {
    const double ax1 = std::min(aabb[0], aabb[2]);
    const double ay1 = std::min(aabb[1], aabb[3]);
    const double ax2 = std::max(aabb[0], aabb[2]);
    const double ay2 = std::max(aabb[1], aabb[3]);
    x1 = static_cast<int>(std::floor(ax1));
    y1 = static_cast<int>(std::floor(ay1));
    x2 = static_cast<int>(std::ceil(ax2));
    y2 = static_cast<int>(std::ceil(ay2));
    if (x2 <= x1) x2 = x1 + 1;
    if (y2 <= y1) y2 = y1 + 1;
    return true;
}

static bool TryBuildAlignedMaskFromDet(const Json& det, const std::array<double, 4>& aabb, cv::Mat& alignedMask) {
    alignedMask = cv::Mat();
    int x1 = 0, y1 = 0, x2 = 0, y2 = 0;
    if (!det.is_object() || !TryAabbToIntXYXY(aabb, x1, y1, x2, y2)) return false;
    const int w = x2 - x1;
    const int h = y2 - y1;
    if (w <= 0 || h <= 0) return false;
    try {
        cv::Mat srcMask;
        if (det.contains("mask_rle") && det.at("mask_rle").is_object()) {
            srcMask = MaskInfoToMat(det.at("mask_rle"));
        }
        if (srcMask.empty()) return false;
        if (srcMask.rows != h || srcMask.cols != w) {
            cv::resize(srcMask, alignedMask, cv::Size(w, h), 0, 0, cv::INTER_NEAREST);
        } else {
            alignedMask = srcMask.clone();
        }
        return !alignedMask.empty();
    } catch (...) {
        alignedMask = cv::Mat();
        return false;
    }
}

static bool CheckMaskOverlapForDets(const Json& detA, const std::array<double, 4>& aabbA, const Json& detB,
                                    const std::array<double, 4>& aabbB) {
    int ax1 = 0, ay1 = 0, ax2 = 0, ay2 = 0;
    int bx1 = 0, by1 = 0, bx2 = 0, by2 = 0;
    if (!TryAabbToIntXYXY(aabbA, ax1, ay1, ax2, ay2)) return false;
    if (!TryAabbToIntXYXY(aabbB, bx1, by1, bx2, by2)) return false;
    cv::Mat maskA;
    if (!TryBuildAlignedMaskFromDet(detA, aabbA, maskA) || maskA.empty()) return false;
    cv::Mat maskB;
    if (!TryBuildAlignedMaskFromDet(detB, aabbB, maskB) || maskB.empty()) return false;

    const int ix1 = std::max(ax1, bx1);
    const int iy1 = std::max(ay1, by1);
    const int ix2 = std::min(ax2, bx2);
    const int iy2 = std::min(ay2, by2);
    if (ix2 <= ix1 || iy2 <= iy1) return false;
    const int aw = ix2 - ix1;
    const int ah = iy2 - iy1;
    const int aox = ix1 - ax1;
    const int aoy = iy1 - ay1;
    const int box = ix1 - bx1;
    const int boy = iy1 - by1;
    if (aox < 0 || aoy < 0 || box < 0 || boy < 0) return false;
    if (aox + aw > maskA.cols || aoy + ah > maskA.rows) return false;
    if (box + aw > maskB.cols || boy + ah > maskB.rows) return false;

    cv::Mat subA = maskA(cv::Rect(aox, aoy, aw, ah));
    cv::Mat subB = maskB(cv::Rect(box, boy, aw, ah));
    cv::Mat overlap;
    cv::bitwise_and(subA, subB, overlap);
    return cv::countNonZero(overlap) > 0;
}

struct MaskPlacementSliding final {
    Json Det;
    std::array<double, 4> Aabb = { 0.0, 0.0, 0.0, 0.0 };
};

static std::array<double, 4> CombineAabbPair(const std::array<double, 4>& a, const std::array<double, 4>& b) {
    return {
        std::min(a[0], b[0]),
        std::min(a[1], b[1]),
        std::max(a[2], b[2]),
        std::max(a[3], b[3])
    };
}

static Json BuildMergedMaskRlePlacements(const std::vector<MaskPlacementSliding>& placements,
                                         const std::array<double, 4>& unionAabb) {
    int ux1 = 0, uy1 = 0, ux2 = 0, uy2 = 0;
    if (!TryAabbToIntXYXY(unionAabb, ux1, uy1, ux2, uy2)) return Json();
    const int uw = ux2 - ux1;
    const int uh = uy2 - uy1;
    if (uw <= 0 || uh <= 0) return Json();

    cv::Mat mergedMask = cv::Mat::zeros(uh, uw, CV_8UC1);
    for (const auto& placement : placements) {
        if (!placement.Det.is_object()) continue;
        int sx1 = 0, sy1 = 0, sx2 = 0, sy2 = 0;
        if (!TryAabbToIntXYXY(placement.Aabb, sx1, sy1, sx2, sy2)) continue;
        const int sw = sx2 - sx1;
        const int sh = sy2 - sy1;
        if (sw <= 0 || sh <= 0) continue;

        cv::Mat src;
        if (!TryBuildAlignedMaskFromDet(placement.Det, placement.Aabb, src) || src.empty()) continue;

        int dstX = sx1 - ux1;
        int dstY = sy1 - uy1;
        int srcX = 0;
        int srcY = 0;
        int rw = src.cols;
        int rh = src.rows;
        if (dstX < 0) {
            srcX = -dstX;
            rw += dstX;
            dstX = 0;
        }
        if (dstY < 0) {
            srcY = -dstY;
            rh += dstY;
            dstY = 0;
        }
        rw = std::min(rw, uw - dstX);
        rh = std::min(rh, uh - dstY);
        if (rw <= 0 || rh <= 0) continue;
        if (srcX + rw > src.cols || srcY + rh > src.rows) continue;

        cv::Mat srcRoi = src(cv::Rect(srcX, srcY, rw, rh));
        cv::Mat dstRoi = mergedMask(cv::Rect(dstX, dstY, rw, rh));
        cv::max(dstRoi, srcRoi, dstRoi);
    }
    if (cv::countNonZero(mergedMask) <= 0) return Json();
    return MatToMaskInfo(mergedMask);
}

static bool TryGetGridFromSlidingMeta(const ModuleImage::SlidingMetaInfo& slidingMeta, int& gx, int& gy) {
    gx = -1;
    gy = -1;
    if (!slidingMeta.Valid) return false;
    gx = slidingMeta.GridX;
    gy = slidingMeta.GridY;
    return gx >= 0 && gy >= 0;
}

static bool TryGetGridFromSlidingMeta(const Json& slidingMeta, int& gx, int& gy) {
    gx = -1;
    gy = -1;
    if (!slidingMeta.is_object()) return false;
    try {
        if (slidingMeta.contains("slice_index") && slidingMeta.at("slice_index").is_array()) {
            const Json& sliceIndex = slidingMeta.at("slice_index");
            if (sliceIndex.size() >= 2 && !sliceIndex.at(0).is_array()) {
                try { gy = sliceIndex.at(0).get<int>(); } catch (...) { gy = -1; }
                try { gx = sliceIndex.at(1).get<int>(); } catch (...) { gx = -1; }
            } else if (sliceIndex.size() > 0 && sliceIndex.at(0).is_array()) {
                const Json& nested = sliceIndex.at(0);
                if (nested.size() >= 2) {
                    try { gy = nested.at(0).get<int>(); } catch (...) { gy = -1; }
                    try { gx = nested.at(1).get<int>(); } catch (...) { gx = -1; }
                }
            }
        }
        if (gx < 0 || gy < 0) {
            gx = SafeJsonInt(slidingMeta, "grid_x", -1);
            gy = SafeJsonInt(slidingMeta, "grid_y", -1);
        }
    } catch (...) {}
    return gx >= 0 && gy >= 0;
}

static Json CloneJsonTokenForOutput(const Json& token) {
    if (token.is_null()) return Json();
    if (token.is_boolean() || token.is_number() || token.is_string()) return token;
    return token;
}

static Json CloneMetadataForMergeOutput(const Json& metaIn) {
    Json result = Json::object();
    if (!metaIn.is_object()) return result;
    static const std::set<std::string> kSkip = { "global_bbox", "combine_flag", "slice_index", "is_rotated" };
    try {
        for (auto it = metaIn.begin(); it != metaIn.end(); ++it) {
            if (kSkip.count(it.key()) > 0) continue;
            result[it.key()] = CloneJsonTokenForOutput(it.value());
        }
    } catch (...) {}
    return result;
}

static Json CloneDetForMergeOutput(const Json& det) {
    Json result = Json::object();
    if (!det.is_object()) return result;
    static const std::set<std::string> kSkip = { "bbox", "with_bbox", "with_angle", "angle", "metadata" };
    try {
        for (auto it = det.begin(); it != det.end(); ++it) {
            if (kSkip.count(it.key()) > 0) continue;
            result[it.key()] = it.value();
        }
    } catch (...) {}
    return result;
}

static Json MappedAabbDetForMerge(const Json& seedDet, const std::array<double, 4>& unionAabb, bool setCombineFlag) {
    const int x1 = static_cast<int>(std::llround(unionAabb[0]));
    const int y1 = static_cast<int>(std::llround(unionAabb[1]));
    const int x2 = static_cast<int>(std::llround(unionAabb[2]));
    const int y2 = static_cast<int>(std::llround(unionAabb[3]));
    const int w = std::max(1, x2 - x1);
    const int h = std::max(1, y2 - y1);

    Json d2 = CloneDetForMergeOutput(seedDet);
    d2["bbox"] = Json::array({ x1, y1, w, h });
    d2["with_bbox"] = true;
    d2["with_angle"] = false;
    d2["angle"] = -100.0;
    Json meta = CloneMetadataForMergeOutput(seedDet.value("metadata", Json::object()));
    meta["global_bbox"] = Json::array({ x1, y1, x2, y2 });
    if (setCombineFlag) meta["combine_flag"] = false;
    d2["metadata"] = std::move(meta);
    return d2;
}

static bool TryMapDetToGlobal(const Json& det, const ModuleImage& wrap, SlidingMappedDet& mapped) {
    if (!det.is_object()) return false;
    if (!det.contains("bbox") || !det.at("bbox").is_array()) return false;
    const Json& bbox = det.at("bbox");
    if (bbox.size() < 4) return false;

    const std::vector<double> T_c2o = BuildTC2O(wrap.TransformState);
    Json detOut = det;
    // 保留 mask_rle（用于 UI 可视化）；但删除原始 mask 指针结构，避免悬空指针透传。
    detOut.erase("mask");

    double score = 0.0;
    (void)TryReadDoubleToken(det.contains("score") ? det.at("score") : Json(), score);

    if (bbox.size() >= 5) {
        double cx = 0.0, cy = 0.0, w = 0.0, h = 0.0;
        if (!TryReadDoubleToken(bbox.at(0), cx) || !TryReadDoubleToken(bbox.at(1), cy) ||
            !TryReadDoubleToken(bbox.at(2), w) || !TryReadDoubleToken(bbox.at(3), h)) {
            return false;
        }
        const double angleRad = ReadAngleRad(det, bbox);

        const double l00 = T_c2o[0], l01 = T_c2o[1];
        const double l10 = T_c2o[3], l11 = T_c2o[4];
        const cv::Point2f center = TransformPoint2x3(T_c2o, cv::Point2f(static_cast<float>(cx), static_cast<float>(cy)));

        const double ux = std::cos(angleRad);
        const double uy = std::sin(angleRad);
        const double vx = -std::sin(angleRad);
        const double vy = std::cos(angleRad);

        const double tuxX = l00 * ux + l01 * uy;
        const double tuxY = l10 * ux + l11 * uy;
        const double tvxX = l00 * vx + l01 * vy;
        const double tvxY = l10 * vx + l11 * vy;
        const double nw = std::abs(w) * std::sqrt(tuxX * tuxX + tuxY * tuxY);
        const double nh = std::abs(h) * std::sqrt(tvxX * tvxX + tvxY * tvxY);
        const double nang = std::atan2(tuxY, tuxX);

        detOut["bbox"] = Json::array({ center.x, center.y, nw, nh, nang });
        detOut["with_bbox"] = true;
        detOut["with_angle"] = true;
        detOut["angle"] = nang;

        mapped.Det = std::move(detOut);
        mapped.Aabb = RBoxAabb(center.x, center.y, nw, nh, nang);
        mapped.CategoryKey = CategoryKeyOfDet(det);
        mapped.Score = score;
        return true;
    }

    double x = 0.0, y = 0.0, w = 0.0, h = 0.0;
    if (!TryReadDoubleToken(bbox.at(0), x) || !TryReadDoubleToken(bbox.at(1), y) ||
        !TryReadDoubleToken(bbox.at(2), w) || !TryReadDoubleToken(bbox.at(3), h)) {
        return false;
    }
    const std::vector<cv::Point2f> pts = {
        TransformPoint2x3(T_c2o, cv::Point2f(static_cast<float>(x), static_cast<float>(y))),
        TransformPoint2x3(T_c2o, cv::Point2f(static_cast<float>(x + w), static_cast<float>(y))),
        TransformPoint2x3(T_c2o, cv::Point2f(static_cast<float>(x + w), static_cast<float>(y + h))),
        TransformPoint2x3(T_c2o, cv::Point2f(static_cast<float>(x), static_cast<float>(y + h)))
    };
    const auto aabb = AabbFromPoints(pts);
    detOut["bbox"] = Json::array({
        aabb[0],
        aabb[1],
        std::max(0.0, aabb[2] - aabb[0]),
        std::max(0.0, aabb[3] - aabb[1])
    });
    detOut["with_bbox"] = true;
    detOut["with_angle"] = false;
    detOut["angle"] = -100.0;

    mapped.Det = std::move(detOut);
    mapped.Aabb = aabb;
    mapped.CategoryKey = CategoryKeyOfDet(det);
    mapped.Score = score;
    return true;
}

static bool GetDetGlobalAabb(const Json& det, const ModuleImage& wrap, std::array<double, 4>& outAabb) {
    SlidingMappedDet mapped;
    if (!TryMapDetToGlobal(det, wrap, mapped)) return false;
    outAabb = mapped.Aabb;
    return true;
}

/// pre_process/sliding_window, features/sliding_window
class SlidingWindowModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        (void)resultList;
        const std::vector<ModuleImage>& images = imageList;

        const int minSize = std::max(1, ReadInt("min_size", 1));
        const auto win = ReadInt2(Properties, "window_size", 640, 640);
        const auto ov = ReadInt2(Properties, "overlap", 0, 0);
        const int winW = std::max(minSize, win.first);
        const int winH = std::max(minSize, win.second);
        const int ovX = std::max(0, ov.first);
        const int ovY = std::max(0, ov.second);

        std::vector<ModuleImage> outImages;
        const bool emitResultEntries = IsCurrentOutputConnected(Context, 1);
        Json outResults = emitResultEntries ? Json::array() : Json();
        int outIndex = 0;

        for (size_t i = 0; i < images.size(); i++) {
            const ModuleImage& wrap = images[i];
            const cv::Mat& mat = wrap.ImageObject;
            if (mat.empty()) continue;

            const int H = mat.rows;
            const int W = mat.cols;
            const int smallW = std::min(winW, W);
            const int smallH = std::min(winH, H);

            int rowNum = 1;
            if (smallH < H) {
                const int effH = std::max(1, smallH - ovY);
                rowNum = H / effH;
                if (H % effH > 0) rowNum++;
            }
            int colNum = 1;
            if (smallW < W) {
                const int effW = std::max(1, smallW - ovX);
                colNum = W / effW;
                if (W % effW > 0) colNum++;
            }

            for (int r = 0; r < rowNum; r++) {
                for (int c = 0; c < colNum; c++) {
                    int startX = c * (smallW - ovX);
                    int startY = r * (smallH - ovY);
                    if (startX + smallW > W) startX = W - smallW;
                    if (startY + smallH > H) startY = H - smallH;
                    if (startX < 0) startX = 0;
                    if (startY < 0) startY = 0;

                    const int endX = startX + smallW;
                    const int endY = startY + smallH;
                    if ((endX - startX) < minSize || (endY - startY) < minSize) continue;

                    const cv::Rect rect(startX, startY, endX - startX, endY - startY);
                    // Use ROI view to avoid per-tile deep copy.
                    cv::Mat cropped = mat(rect);

                    const TransformationState parentState = (wrap.TransformState.OriginalWidth > 0 && wrap.TransformState.OriginalHeight > 0)
                        ? wrap.TransformState
                        : TransformationState(W, H);
                    const std::vector<double> childA2x3 = { 1,0,-static_cast<double>(startX), 0,1,-static_cast<double>(startY) };
                    const TransformationState childState = parentState.DeriveChild(childA2x3, rect.width, rect.height);

                    ModuleImage::SlidingMetaInfo slidingMeta;
                    slidingMeta.Valid = true;
                    slidingMeta.GridX = c;
                    slidingMeta.GridY = r;
                    slidingMeta.GridCols = colNum;
                    slidingMeta.GridRows = rowNum;
                    slidingMeta.X = startX;
                    slidingMeta.Y = startY;
                    slidingMeta.W = rect.width;
                    slidingMeta.H = rect.height;

                    ModuleImage childWrap(cropped,
                                          wrap.OriginalImage.empty() ? mat : wrap.OriginalImage,
                                          childState,
                                          wrap.OriginalIndex);
                    childWrap.SlidingMeta = slidingMeta;
                    outImages.push_back(childWrap);

                    if (emitResultEntries) {
                        Json entry = Json::object();
                        entry["type"] = "local";
                        entry["index"] = outIndex;
                        entry["origin_index"] = wrap.OriginalIndex;
                        entry["transform"] = childState.ToJson();
                        entry["sample_results"] = Json::array();
                        entry["sliding_meta"] = slidingMeta.ToJson();
                        outResults.push_back(std::move(entry));
                        outIndex += 1;
                    }
                }
            }
        }

        if (!emitResultEntries) outResults = Json::array();
        return ModuleIO(std::move(outImages), std::move(outResults), Json::array());
    }
};

/// pre_process/sliding_merge, features/sliding_merge（对齐 DlcvCsharpApi/SlidingMerge.cs）
class SlidingMergeModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& wrappers = imageList;
        const Json inResults = resultList.is_array() ? resultList : Json::array();
        if (wrappers.empty()) {
            return ModuleIO(std::vector<ModuleImage>(), inResults, Json::array());
        }

        const double iouTh = std::max(0.0, ReadDouble("iou_threshold", 0.2));
        const bool dedupResults = ReadBool("dedup_results", true);
        const std::string taskType = NormalizeTaskType(ReadString("task_type", "auto"));

        std::unordered_map<std::string, std::vector<Json>> transToSamples;
        std::unordered_map<int, std::vector<Json>> indexToSamples;
        std::unordered_map<int, std::vector<Json>> originToSamples;
        std::unordered_map<int, ModuleImage::SlidingMetaInfo> indexToSlidingMeta;
        Json otherResults = Json::array();

        for (const auto& token : inResults) {
            if (!token.is_object()) {
                otherResults.push_back(token);
                continue;
            }
            const Json& entry = token;
            if (entry.value("type", "") != "local") {
                otherResults.push_back(token);
                continue;
            }
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) continue;
            const Json& dets = entry.at("sample_results");

            const int entryIndex = SafeJsonInt(entry, "index", -1);

            std::string sig;
            try {
                if (entry.contains("transform") && entry.at("transform").is_object()) {
                    sig = SerializeTransformJsonSig(entry.at("transform"));
                }
            } catch (...) { sig.clear(); }

            if (!sig.empty()) {
                auto& list = transToSamples[sig];
                for (const auto& s : dets) {
                    if (s.is_object()) list.push_back(s);
                }
                continue;
            }
            if (entryIndex >= 0) {
                auto& list = indexToSamples[entryIndex];
                for (const auto& s : dets) {
                    if (s.is_object()) list.push_back(s);
                }
                continue;
            }
            const int originIdx = SafeJsonInt(entry, "origin_index", -1);
            if (originIdx >= 0) {
                auto& list = originToSamples[originIdx];
                for (const auto& s : dets) {
                    if (s.is_object()) list.push_back(s);
                }
                continue;
            }
            otherResults.push_back(token);
        }

        const int nWin = static_cast<int>(wrappers.size());
        std::vector<std::vector<Json>> windowDets(static_cast<size_t>(nWin));
        for (int i = 0; i < nWin; i++) {
            const ModuleImage& wrap = wrappers[static_cast<size_t>(i)];
            std::vector<Json> dets;
            const std::string ws = SerializeTransformStateSig(wrap.TransformState);
            if (!ws.empty()) {
                auto it = transToSamples.find(ws);
                if (it != transToSamples.end()) dets = it->second;
            } else {
                auto itIdx = indexToSamples.find(i);
                if (itIdx != indexToSamples.end()) dets = itIdx->second;
                else {
                    auto itO = originToSamples.find(wrap.OriginalIndex);
                    if (itO != originToSamples.end()) dets = itO->second;
                }
            }
            windowDets[static_cast<size_t>(i)] = std::move(dets);
        }

        bool hasSlidingMeta = false;
        for (int i = 0; i < nWin; i++) {
            const ModuleImage::SlidingMetaInfo& sm = wrappers[static_cast<size_t>(i)].SlidingMeta;
            int gx = 0, gy = 0;
            if (TryGetGridFromSlidingMeta(sm, gx, gy)) {
                hasSlidingMeta = true;
                indexToSlidingMeta[i] = sm;
            }
        }

        std::map<int, ModuleImage> originIdxToImgwrap;
        for (const auto& wrap : wrappers) {
            if (wrap.ImageObject.empty() && wrap.OriginalImage.empty()) continue;
            const cv::Mat originMat = wrap.OriginalImage.empty() ? wrap.ImageObject : wrap.OriginalImage;
            if (originMat.empty()) continue;
            const int oi = wrap.OriginalIndex;
            if (originIdxToImgwrap.find(oi) == originIdxToImgwrap.end()) {
                TransformationState st(originMat.cols, originMat.rows);
                originIdxToImgwrap.emplace(oi, ModuleImage(originMat, originMat, st, oi));
            }
        }

        auto appendGlobalForWindow = [&](int winIdx, std::vector<Json>& outItems) {
            const ModuleImage& wrap = wrappers[static_cast<size_t>(winIdx)];
            const auto& dets = windowDets[static_cast<size_t>(winIdx)];
            for (const auto& det : dets) {
                SlidingMappedDet mapped;
                if (!TryMapDetToGlobal(det, wrap, mapped)) continue;
                outItems.push_back(std::move(mapped.Det));
            }
        };

        auto buildOutput = [&](const std::unordered_map<int, std::vector<Json>>& originIdxToItems) {
            std::vector<ModuleImage> outImages;
            Json outRes = Json::array();
            int outIdx = 0;
            for (const auto& kv : originIdxToImgwrap) {
                outImages.push_back(kv.second);
                Json samples = Json::array();
                auto it = originIdxToItems.find(kv.first);
                if (it != originIdxToItems.end()) {
                    for (const auto& item : it->second) samples.push_back(item);
                }
                Json mergedEntry = Json::object();
                mergedEntry["type"] = "local";
                mergedEntry["index"] = outIdx;
                mergedEntry["origin_index"] = kv.first;
                mergedEntry["transform"] = nullptr;
                mergedEntry["sample_results"] = std::move(samples);
                outRes.push_back(std::move(mergedEntry));
                outIdx += 1;
            }
            for (const auto& t : otherResults) outRes.push_back(t);
            return ModuleIO(std::move(outImages), std::move(outRes), Json::array());
        };

        if (!dedupResults || !hasSlidingMeta) {
            std::unordered_map<int, std::vector<Json>> originIdxToItems;
            for (int i = 0; i < nWin; i++) {
                const int oi = wrappers[static_cast<size_t>(i)].OriginalIndex;
                appendGlobalForWindow(i, originIdxToItems[oi]);
            }
            return buildOutput(originIdxToItems);
        }

        std::unordered_map<int, std::vector<int>> groups;
        for (int i = 0; i < nWin; i++) {
            groups[wrappers[static_cast<size_t>(i)].OriginalIndex].push_back(i);
        }

        std::unordered_map<int, std::vector<Json>> originIdxToItems;

        for (const auto& g : groups) {
            const std::vector<int>& idxList = g.second;
            if (idxList.empty()) continue;
            if (idxList.size() == 1) {
                const int onlyIdx = idxList.front();
                appendGlobalForWindow(onlyIdx, originIdxToItems[g.first]);
                continue;
            }

            std::map<std::pair<int, int>, int> gridToIdx;
            for (int idx : idxList) {
                auto itMeta = indexToSlidingMeta.find(idx);
                if (itMeta == indexToSlidingMeta.end()) continue;
                int gx = 0, gy = 0;
                if (!TryGetGridFromSlidingMeta(itMeta->second, gx, gy)) continue;
                gridToIdx[{gx, gy}] = idx;
            }

            std::unordered_map<int, std::unordered_set<int>> removed;
            for (int idx : idxList) removed[idx] = std::unordered_set<int>();
            UnionFindPairs uf;

            for (const auto& kv : gridToIdx) {
                const int gx = kv.first.first;
                const int gy = kv.first.second;
                const int curIdx = kv.second;
                const std::array<std::pair<int, int>, 2> nbs = { std::make_pair(gx + 1, gy), std::make_pair(gx, gy + 1) };
                for (const auto& nbKey : nbs) {
                    auto itNb = gridToIdx.find(nbKey);
                    if (itNb == gridToIdx.end()) continue;
                    const int nbIdx = itNb->second;
                    const std::vector<Json>& detsA = windowDets[static_cast<size_t>(curIdx)];
                    const std::vector<Json>& detsB = windowDets[static_cast<size_t>(nbIdx)];
                    if (detsA.empty() || detsB.empty()) continue;

                    for (int ia = 0; ia < static_cast<int>(detsA.size()); ia++) {
                        const Json& da = detsA[static_cast<size_t>(ia)];
                        std::array<double, 4> aabbA{};
                        if (!GetDetGlobalAabb(da, wrappers[static_cast<size_t>(curIdx)], aabbA)) continue;
                        const bool aIsRot = IsRotatedDetJson(da);

                        for (int ib = 0; ib < static_cast<int>(detsB.size()); ib++) {
                            const Json& db = detsB[static_cast<size_t>(ib)];
                            if (!SameCategoryJson(da, db)) continue;
                            std::array<double, 4> aabbB{};
                            if (!GetDetGlobalAabb(db, wrappers[static_cast<size_t>(nbIdx)], aabbB)) continue;
                            const bool bIsRot = IsRotatedDetJson(db);
                            const bool modeRotate = (taskType == "rotate") || (taskType == "auto" && aIsRot && bIsRot);

                            if (modeRotate) {
                                const double riou = BoxIoU(aabbA, aabbB);
                                if (riou > iouTh) {
                                    double sa = 0.0, sb = 0.0;
                                    (void)TryReadDoubleToken(da.contains("score") ? da.at("score") : Json(), sa);
                                    (void)TryReadDoubleToken(db.contains("score") ? db.at("score") : Json(), sb);
                                    if (sa < sb) removed[curIdx].insert(ia);
                                    else removed[nbIdx].insert(ib);
                                }
                                continue;
                            }

                            bool shouldUnion = false;
                            const double ios = ComputeIoS(aabbA, aabbB);
                            if (ios > iouTh) {
                                const bool hasMaskA = HasMaskPayload(da);
                                const bool hasMaskB = HasMaskPayload(db);
                                if (hasMaskA && hasMaskB)
                                    shouldUnion = CheckMaskOverlapForDets(da, aabbA, db, aabbB);
                                else
                                    shouldUnion = true;
                            }
                            if (shouldUnion) {
                                uf.Union(PackWinDet(curIdx, ia), PackWinDet(nbIdx, ib));
                            }
                        }
                    }
                }
            }

            std::vector<Json>& outItems = originIdxToItems[g.first];

            std::vector<std::pair<int, int>> allUids;
            for (int idx : idxList) {
                const int nd = static_cast<int>(windowDets[static_cast<size_t>(idx)].size());
                for (int j = 0; j < nd; j++) allUids.push_back({ idx, j });
            }

            for (const auto& uid : allUids) {
                const Json& det = windowDets[static_cast<size_t>(uid.first)][static_cast<size_t>(uid.second)];
                if (!IsRotatedDetJson(det)) continue;
                auto itRm = removed.find(uid.first);
                if (itRm != removed.end() && itRm->second.count(uid.second) > 0) continue;
                SlidingMappedDet mapped;
                if (!TryMapDetToGlobal(det, wrappers[static_cast<size_t>(uid.first)], mapped)) continue;
                outItems.push_back(std::move(mapped.Det));
            }

            std::unordered_map<int64_t, std::vector<std::pair<int, int>>> rootToMembers;
            for (const auto& uid : allUids) {
                const Json& det = windowDets[static_cast<size_t>(uid.first)][static_cast<size_t>(uid.second)];
                if (IsRotatedDetJson(det)) continue;
                const int64_t key = PackWinDet(uid.first, uid.second);
                uf.Add(key);
                const int64_t root = uf.Find(key);
                rootToMembers[root].push_back(uid);
            }

            for (auto& rm : rootToMembers) {
                auto& members = rm.second;
                std::array<double, 4> unionAabb{ 0, 0, 0, 0 };
                bool hasUnion = false;
                double mergedScore = 0.0;
                Json seedDet;
                std::vector<MaskPlacementSliding> maskPlacements;

                for (const auto& uid : members) {
                    const Json& det = windowDets[static_cast<size_t>(uid.first)][static_cast<size_t>(uid.second)];
                    std::array<double, 4> aabb{};
                    if (!GetDetGlobalAabb(det, wrappers[static_cast<size_t>(uid.first)], aabb)) continue;
                    if (!seedDet.is_object()) seedDet = det;
                    if (!hasUnion) {
                        unionAabb = aabb;
                        hasUnion = true;
                    } else {
                        unionAabb = CombineAabbPair(unionAabb, aabb);
                    }
                    double sc = 0.0;
                    (void)TryReadDoubleToken(det.contains("score") ? det.at("score") : Json(), sc);
                    if (sc > mergedScore) mergedScore = sc;
                    if (HasMaskPayload(det)) maskPlacements.push_back(MaskPlacementSliding{ det, aabb });
                }

                if (!hasUnion || !seedDet.is_object()) continue;

                Json mergedMaskRle = BuildMergedMaskRlePlacements(maskPlacements, unionAabb);
                const bool hasMergedRle = mergedMaskRle.is_object();
                Json merged = MappedAabbDetForMerge(seedDet, unionAabb, true);
                merged["score"] = mergedScore;
                try {
                    if (seedDet.contains("category_id")) merged["category_id"] = seedDet.at("category_id");
                } catch (...) {}
                try {
                    merged["category_name"] = seedDet.value("category_name", std::string());
                } catch (...) {}

                if (hasMergedRle) {
                    merged["with_mask"] = true;
                    merged["mask_rle"] = std::move(mergedMaskRle);
                    merged.erase("mask_array");
                } else {
                    merged["with_mask"] = false;
                    merged.erase("mask_rle");
                    merged.erase("mask_array");
                }
                if (!merged.contains("metadata") || !merged["metadata"].is_object()) merged["metadata"] = Json::object();
                merged["metadata"]["merge_mode"] = hasMergedRle ? "mask_union" : "bbox_union";
                outItems.push_back(std::move(merged));
            }
        }

        return buildOutput(originIdxToItems);
    }
};

// 注册
DLCV_FLOW_REGISTER_MODULE("pre_process/sliding_window", SlidingWindowModule)
DLCV_FLOW_REGISTER_MODULE("features/sliding_window", SlidingWindowModule)
DLCV_FLOW_REGISTER_MODULE("pre_process/sliding_merge", SlidingMergeModule)
DLCV_FLOW_REGISTER_MODULE("features/sliding_merge", SlidingMergeModule)

} // namespace flow
} // namespace dlcv_infer

