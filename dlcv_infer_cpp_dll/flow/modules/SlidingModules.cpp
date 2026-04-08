#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <map>
#include <string>
#include <unordered_map>

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
            if (sscanf_s(s.c_str(), "%d%*[,; ]%d", &a, &b) == 2) {
                return { a, b };
            }
        }
    } catch (...) {}
    return { dv1, dv2 };
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

static bool TryMapDetToGlobal(const Json& det, const ModuleImage& wrap, SlidingMappedDet& mapped) {
    if (!det.is_object()) return false;
    if (!det.contains("bbox") || !det.at("bbox").is_array()) return false;
    const Json& bbox = det.at("bbox");
    if (bbox.size() < 4) return false;

    const std::vector<double> T_c2o = BuildTC2O(wrap.TransformState);
    Json detOut = det;
    detOut.erase("mask_rle");
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
        Json outResults = Json::array();
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

                    ModuleImage childWrap(cropped,
                                          wrap.OriginalImage.empty() ? mat : wrap.OriginalImage,
                                          childState,
                                          wrap.OriginalIndex);
                    outImages.push_back(childWrap);

                    Json entry = Json::object();
                    entry["type"] = "local";
                    entry["index"] = outIndex;
                    entry["origin_index"] = wrap.OriginalIndex;
                    entry["transform"] = childState.ToJson();
                    entry["sample_results"] = Json::array();
                    entry["sliding_meta"] = Json::object({
                        {"grid_x", c},
                        {"grid_y", r},
                        {"grid_size", Json::array({ colNum, rowNum })},
                        {"win_size", Json::array({ rect.width, rect.height })},
                        {"slice_index", Json::array({ r, c })},
                        {"x", startX},
                        {"y", startY},
                        {"w", rect.width},
                        {"h", rect.height}
                    });
                    outResults.push_back(entry);
                    outIndex += 1;
                }
            }
        }

        return ModuleIO(std::move(outImages), std::move(outResults), Json::array());
    }
};

/// pre_process/sliding_merge, features/sliding_merge
class SlidingMergeModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& inImages = imageList;
        const Json inResults = resultList.is_array() ? resultList : Json::array();
        const double iouThreshold = std::max(0.0, ReadDouble("iou_threshold", 0.2));
        const bool dedupResults = ReadBool("dedup_results", true);

        std::map<int, ModuleImage> originIndexToImage;
        for (const auto& wrap : inImages) {
            if (wrap.ImageObject.empty() && wrap.OriginalImage.empty()) continue;
            const cv::Mat originMat = wrap.OriginalImage.empty() ? wrap.ImageObject : wrap.OriginalImage;
            if (originMat.empty()) continue;

            const int originIndex = wrap.OriginalIndex;
            if (originIndexToImage.find(originIndex) == originIndexToImage.end()) {
                TransformationState st(originMat.cols, originMat.rows);
                originIndexToImage.emplace(originIndex, ModuleImage(originMat, originMat, st, originIndex));
            }
        }

        std::unordered_map<int, std::vector<SlidingMappedDet>> originIndexToMapped;
        int fallbackPos = 0;
        for (const auto& token : inResults) {
            if (!token.is_object()) {
                fallbackPos++;
                continue;
            }
            const Json& entry = token;
            if (entry.value("type", "") != "local") {
                fallbackPos++;
                continue;
            }

            int idx = fallbackPos;
            try {
                if (entry.contains("index")) idx = entry.at("index").get<int>();
            } catch (...) {}

            const ModuleImage* wrap = (idx >= 0 && idx < static_cast<int>(inImages.size()))
                ? &inImages[static_cast<size_t>(idx)]
                : nullptr;
            if (wrap == nullptr || (!wrap->ImageObject.data && !wrap->OriginalImage.data)) {
                fallbackPos++;
                continue;
            }

            const int originIndex = wrap->OriginalIndex;
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) {
                fallbackPos++;
                continue;
            }

            auto& mappedList = originIndexToMapped[originIndex];
            for (const auto& detToken : entry.at("sample_results")) {
                SlidingMappedDet mapped;
                if (TryMapDetToGlobal(detToken, *wrap, mapped)) {
                    mappedList.push_back(std::move(mapped));
                }
            }
            fallbackPos++;
        }

        std::vector<ModuleImage> outImages;
        Json outResults = Json::array();
        int outIdx = 0;
        for (auto& kv : originIndexToImage) {
            const int originIndex = kv.first;
            outImages.push_back(kv.second);

            auto itMapped = originIndexToMapped.find(originIndex);
            std::vector<SlidingMappedDet> mappedList = (itMapped != originIndexToMapped.end())
                ? itMapped->second
                : std::vector<SlidingMappedDet>();

            if (dedupResults && !mappedList.empty()) {
                std::stable_sort(mappedList.begin(), mappedList.end(), [](const SlidingMappedDet& a, const SlidingMappedDet& b) {
                    if (a.Score != b.Score) return a.Score > b.Score;
                    return a.CategoryKey < b.CategoryKey;
                });

                std::vector<SlidingMappedDet> kept;
                kept.reserve(mappedList.size());
                for (const auto& cand : mappedList) {
                    bool drop = false;
                    for (const auto& existing : kept) {
                        if (cand.CategoryKey != existing.CategoryKey) continue;
                        if (BoxIoU(cand.Aabb, existing.Aabb) > iouThreshold) {
                            drop = true;
                            break;
                        }
                    }
                    if (!drop) kept.push_back(cand);
                }
                mappedList = std::move(kept);
            }

            Json samples = Json::array();
            for (const auto& item : mappedList) {
                samples.push_back(item.Det);
            }

            Json mergedEntry = Json::object();
            mergedEntry["type"] = "local";
            mergedEntry["index"] = outIdx;
            mergedEntry["origin_index"] = originIndex;
            mergedEntry["transform"] = nullptr;
            mergedEntry["sample_results"] = std::move(samples);
            outResults.push_back(std::move(mergedEntry));
            outIdx += 1;
        }

        return ModuleIO(std::move(outImages), std::move(outResults), Json::array());
    }
};

// 注册
DLCV_FLOW_REGISTER_MODULE("pre_process/sliding_window", SlidingWindowModule)
DLCV_FLOW_REGISTER_MODULE("features/sliding_window", SlidingWindowModule)
DLCV_FLOW_REGISTER_MODULE("pre_process/sliding_merge", SlidingMergeModule)
DLCV_FLOW_REGISTER_MODULE("features/sliding_merge", SlidingMergeModule)

} // namespace flow
} // namespace dlcv_infer

