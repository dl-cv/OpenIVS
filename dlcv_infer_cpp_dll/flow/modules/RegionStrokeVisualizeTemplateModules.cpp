#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include "flow/utils/MaskRleUtils.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdio>
#include <fstream>
#include <iterator>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>

#include "opencv2/imgcodecs.hpp"
#include "opencv2/imgproc.hpp"

namespace dlcv_infer {
namespace flow {

static constexpr double kPi = 3.14159265358979323846;

static void EnsureDirExists(const std::string& dir) {
    if (dir.empty()) return;
    std::string path;
    path.reserve(dir.size());
    for (size_t i = 0; i < dir.size(); i++) {
        const char c = dir[i];
        path.push_back(c);
        if (c == '\\' || c == '/') {
            CreateDirectoryA(path.c_str(), nullptr);
        }
    }
    CreateDirectoryA(dir.c_str(), nullptr);
}

static std::string JoinPath(const std::string& a, const std::string& b) {
    if (a.empty()) return b;
    if (b.empty()) return a;
    const char last = a.back();
    if (last == '\\' || last == '/') return a + b;
    return a + "\\" + b;
}

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

// -------------------- ResultFilterRegion (简化版) --------------------
class ResultFilterRegionModule : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        const int x = ReadInt("x", 0);
        const int y = ReadInt("y", 0);
        const int w = std::max(1, ReadInt("w", 100));
        const int h = std::max(1, ReadInt("h", 100));

        std::vector<ModuleImage> mainImages;
        Json mainResults = Json::array();
        std::vector<ModuleImage> altImages;
        Json altResults = Json::array();

        bool hasAnyInside = false;

        for (const auto& token : results) {
            if (!token.is_object()) continue;
            const Json& entry = token;
            if (entry.value("type", "") != "local") continue;
            const int idx = entry.value("index", -1);
            const int originIndex = entry.value("origin_index", idx);
            int imgIdx = (idx >= 0 && idx < static_cast<int>(images.size())) ? idx : -1;
            if (imgIdx < 0) {
                // fallback by origin_index
                for (int i = 0; i < static_cast<int>(images.size()); i++) {
                    if (images[static_cast<size_t>(i)].OriginalIndex == originIndex) { imgIdx = i; break; }
                }
            }
            if (imgIdx < 0 || imgIdx >= static_cast<int>(images.size())) continue;
            const ModuleImage imgObj = images[static_cast<size_t>(imgIdx)];
            const int W = imgObj.ImageObject.empty() ? 0 : imgObj.ImageObject.cols;
            const int H = imgObj.ImageObject.empty() ? 0 : imgObj.ImageObject.rows;
            const cv::Rect roi = ClampRect(x, y, w, h, W, H);
            if (roi.area() <= 0) continue;

            std::vector<Json> inside;
            std::vector<Json> outside;

            if (entry.contains("sample_results") && entry.at("sample_results").is_array()) {
                for (const auto& s : entry.at("sample_results")) {
                    if (!s.is_object()) continue;
                    const Json& so = s;
                    if (!so.contains("bbox") || !so.at("bbox").is_array() || so.at("bbox").size() < 4) { outside.push_back(so); continue; }
                    const Json& bb = so.at("bbox");
                    double bx = 0, by = 0, bw = 0, bh = 0;
                    try {
                        if (bb.size() >= 5) {
                            // rotated: [cx,cy,w,h,...] -> AABB
                            const double cx = bb.at(0).get<double>();
                            const double cy = bb.at(1).get<double>();
                            bw = std::abs(bb.at(2).get<double>());
                            bh = std::abs(bb.at(3).get<double>());
                            bx = cx - bw / 2.0;
                            by = cy - bh / 2.0;
                        } else {
                            bx = bb.at(0).get<double>();
                            by = bb.at(1).get<double>();
                            bw = bb.at(2).get<double>();
                            bh = bb.at(3).get<double>();
                        }
                    } catch (...) { outside.push_back(so); continue; }

                    const cv::Rect bboxRect = ClampRect(static_cast<int>(std::floor(bx)),
                                                        static_cast<int>(std::floor(by)),
                                                        static_cast<int>(std::ceil(bw)),
                                                        static_cast<int>(std::ceil(bh)),
                                                        W, H);
                    if (bboxRect.area() <= 0 || !RectIntersects(bboxRect, roi)) {
                        outside.push_back(so);
                        continue;
                    }

                    bool insideFlag = true;
                    if (so.contains("mask_rle") && so.at("mask_rle").is_object()) {
                        insideFlag = false;
                        try {
                            cv::Mat patch = MaskInfoToMat(so.at("mask_rle"));
                            if (!patch.empty()) {
                                // resize patch to bbox size if needed
                                const int roiW = std::max(1, bboxRect.width);
                                const int roiH = std::max(1, bboxRect.height);
                                if (patch.cols != roiW || patch.rows != roiH) {
                                    cv::Mat resized;
                                    cv::resize(patch, resized, cv::Size(roiW, roiH), 0, 0, cv::INTER_NEAREST);
                                    patch = resized;
                                }
                                const cv::Rect inter = bboxRect & roi;
                                const int sx0 = std::max(0, inter.x - bboxRect.x);
                                const int sy0 = std::max(0, inter.y - bboxRect.y);
                                const int sw = std::max(0, inter.width);
                                const int sh = std::max(0, inter.height);
                                if (sw > 0 && sh > 0) {
                                    cv::Rect roiSrc(sx0, sy0, std::min(sw, patch.cols - sx0), std::min(sh, patch.rows - sy0));
                                    if (roiSrc.width > 0 && roiSrc.height > 0) {
                                        cv::Mat sub = patch(roiSrc);
                                        if (cv::countNonZero(sub) > 0) insideFlag = true;
                                    }
                                }
                            }
                        } catch (...) {}
                    }

                    if (insideFlag) inside.push_back(so);
                    else outside.push_back(so);
                }
            }

            if (!inside.empty()) {
                hasAnyInside = true;
                mainImages.push_back(imgObj);
                Json e = Json::object();
                e["type"] = "local";
                e["index"] = static_cast<int>(mainResults.size());
                e["origin_index"] = originIndex;
                e["transform"] = entry.contains("transform") ? entry.at("transform") : Json();
                Json arr = Json::array(); for (const auto& x : inside) arr.push_back(x);
                e["sample_results"] = arr;
                mainResults.push_back(e);
            }
            if (!outside.empty()) {
                altImages.push_back(imgObj);
                Json e2 = Json::object();
                e2["type"] = "local";
                e2["index"] = static_cast<int>(altResults.size());
                e2["origin_index"] = originIndex;
                e2["transform"] = entry.contains("transform") ? entry.at("transform") : Json();
                Json arr2 = Json::array(); for (const auto& x : outside) arr2.push_back(x);
                e2["sample_results"] = arr2;
                altResults.push_back(e2);
            }
        }

        this->ExtraOutputs.push_back(ModuleChannel(altImages, altResults));
        this->ScalarOutputsByName["has_positive"] = hasAnyInside;
        return ModuleIO(std::move(mainImages), std::move(mainResults), Json::array());
    }
};

class ResultFilterRegionGlobalModule final : public ResultFilterRegionModule {
public:
    using ResultFilterRegionModule::ResultFilterRegionModule;
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

