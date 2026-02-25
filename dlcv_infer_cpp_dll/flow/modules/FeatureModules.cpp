#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <limits>
#include <stdexcept>
#include <string>
#include <tuple>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include "opencv2/imgproc.hpp"

namespace dlcv_infer {
namespace flow {

static constexpr double kPi = 3.14159265358979323846;

static std::string FormatAffineKey(const std::vector<double>& a) {
    if (a.size() < 6) return std::string();
    char buf[256] = {0};
    std::snprintf(buf, sizeof(buf), "T:%.4f,%.4f,%.2f,%.4f,%.4f,%.2f", a[0], a[1], a[2], a[3], a[4], a[5]);
    return std::string(buf);
}

static std::string SerializeTransformSigFromJson(const Json& tObj) {
    if (!tObj.is_object() || !tObj.contains("affine_2x3") || !tObj.at("affine_2x3").is_array()) return std::string();
    const Json& a = tObj.at("affine_2x3");
    if (a.size() < 6) return std::string();
    std::vector<double> v(6, 0.0);
    for (size_t i = 0; i < 6; i++) {
        try { v[i] = a.at(i).get<double>(); } catch (...) { v[i] = 0.0; }
    }
    return FormatAffineKey(v);
}

static std::string SerializeTransform(const TransformationState* st, int index, int originIndex) {
    if (st == nullptr || st->AffineMatrix2x3.size() < 6) {
        return "idx:" + std::to_string(index) + "|org:" + std::to_string(originIndex) + "|T:null";
    }
    return "idx:" + std::to_string(index) + "|org:" + std::to_string(originIndex) + "|" + FormatAffineKey(st->AffineMatrix2x3);
}

static double GetDoubleProp(const Json& props, const std::string& key, double dv) {
    if (!props.is_object() || !props.contains(key)) return dv;
    try {
        const Json& v = props.at(key);
        if (v.is_number()) return v.get<double>();
        if (v.is_string()) return std::stod(v.get<std::string>());
    } catch (...) {}
    return dv;
}

static int GetIntProp(const Json& props, const std::string& key, int dv) {
    if (!props.is_object() || !props.contains(key)) return dv;
    try {
        const Json& v = props.at(key);
        if (v.is_number_integer()) return v.get<int>();
        if (v.is_number()) return static_cast<int>(std::llround(v.get<double>()));
        if (v.is_string()) return std::stoi(v.get<std::string>());
    } catch (...) {}
    return dv;
}

static std::unordered_set<std::string> ToLabelSet(const Json& v) {
    std::unordered_set<std::string> set;
    try {
        if (v.is_array()) {
            for (const auto& it : v) {
                if (it.is_string()) {
                    const std::string s = it.get<std::string>();
                    if (!s.empty()) set.insert(s);
                }
            }
        } else if (v.is_string()) {
            const std::string s = v.get<std::string>();
            if (!s.empty()) set.insert(s);
        }
    } catch (...) {}
    return set;
}

static void GetRotationAffineCcwDeg(int angleCcw, int w, int h, std::vector<double>& A, int& newW, int& newH) {
    const int a = ((angleCcw % 360) + 360) % 360;
    if (a == 90) {
        // (x,y)->(y, W-1-x)
        A = { 0, 1, 0, -1, 0, static_cast<double>(w - 1) };
        newW = h; newH = w;
    } else if (a == 180) {
        A = { -1, 0, static_cast<double>(w - 1), 0, -1, static_cast<double>(h - 1) };
        newW = w; newH = h;
    } else if (a == 270) {
        // (x,y)->(H-1-y, x)
        A = { 0, -1, static_cast<double>(h - 1), 1, 0, 0 };
        newW = h; newH = w;
    } else {
        A = { 1,0,0, 0,1,0 };
        newW = w; newH = h;
    }
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

static Json NormalizeAngleRad(Json angleVal) {
    double a = -100.0;
    try { if (angleVal.is_number()) a = angleVal.get<double>(); } catch (...) { a = -100.0; }
    if (a <= -99.0) return -100.0;
    // normalize to [-pi, pi)
    const double twoPi = 2.0 * kPi;
    a = std::fmod(a + kPi, twoPi);
    if (a < 0) a += twoPi;
    a -= kPi;
    return a;
}

static int SafeInt(const Json& v, int dv = -1) {
    try {
        if (v.is_number_integer()) return v.get<int>();
        if (v.is_number()) return static_cast<int>(std::llround(v.get<double>()));
        if (v.is_string()) return std::stoi(v.get<std::string>());
    } catch (...) {}
    return dv;
}

static double SafeScore(const Json& v) {
    try {
        if (v.is_number()) return v.get<double>();
        if (v.is_string()) return std::stod(v.get<std::string>());
    } catch (...) {}
    return 0.0;
}

static std::string BuildImageSignature(const ModuleImage& im) {
    std::string tSig;
    try {
        tSig = im.TransformState.ToJson().dump();
    } catch (...) {
        tSig.clear();
    }
    return "module|" + std::to_string(im.OriginalIndex) + "|" + tSig;
}

static std::string PickFirstLabel(const Json& sampleResults, bool useTop1) {
    if (!sampleResults.is_array() || sampleResults.empty()) return std::string();

    if (!useTop1) {
        for (const auto& d : sampleResults) {
            if (!d.is_object()) continue;
            if (d.contains("category_name") && d.at("category_name").is_string()) {
                return d.at("category_name").get<std::string>();
            }
        }
        return std::string();
    }

    bool hasTop = false;
    double topScore = -std::numeric_limits<double>::infinity();
    std::string topLabel;
    for (const auto& d : sampleResults) {
        if (!d.is_object()) continue;
        if (!d.contains("category_name") || !d.at("category_name").is_string()) continue;
        const std::string label = d.at("category_name").get<std::string>();
        if (label.empty()) continue;
        const double sc = d.contains("score") ? SafeScore(d.at("score")) : 0.0;
        if (!hasTop || sc > topScore) {
            hasTop = true;
            topScore = sc;
            topLabel = label;
        }
    }
    return hasTop ? topLabel : std::string();
}

/// features/image_generation
class ImageGenerationModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& imagesIn = imageList;
        const Json resultsIn = resultList.is_array() ? resultList : Json::array();

        const double cropExpand = GetDoubleProp(Properties, "crop_expand", 0.0);
        const int minSize = std::max(1, GetIntProp(Properties, "min_size", 1));

        int cropW = -1, cropH = -1;
        try {
            if (Properties.is_object() && Properties.contains("crop_shape") && Properties.at("crop_shape").is_array()) {
                const Json& cs = Properties.at("crop_shape");
                if (cs.size() >= 2) {
                    cropW = cs.at(0).get<int>();
                    cropH = cs.at(1).get<int>();
                }
            }
        } catch (...) { cropW = -1; cropH = -1; }

        // transform/index 映射
        std::unordered_map<std::string, std::tuple<ModuleImage, cv::Mat, int>> keyToImage;
        for (int i = 0; i < static_cast<int>(imagesIn.size()); i++) {
            const ModuleImage& wrap = imagesIn[static_cast<size_t>(i)];
            if (wrap.ImageObject.empty()) continue;
            const std::string key = SerializeTransform(&wrap.TransformState, i, wrap.OriginalIndex);
            keyToImage[key] = std::make_tuple(wrap, wrap.ImageObject, i);
        }

        std::vector<ModuleImage> imagesOut;
        Json resultsOut = Json::array();
        int outIndex = 0;

        for (const auto& entryToken : resultsIn) {
            if (!entryToken.is_object()) continue;
            const Json& entry = entryToken;
            int idx = entry.contains("index") ? entry.at("index").get<int>() : -1;
            int originIndex = entry.contains("origin_index") ? entry.at("origin_index").get<int>() : idx;
            TransformationState stEntry;
            try {
                if (entry.contains("transform") && entry.at("transform").is_object()) {
                    stEntry = TransformationState::FromJson(entry.at("transform"));
                }
            } catch (...) {}

            std::string key = SerializeTransform(&stEntry, idx, originIndex);
            auto it = keyToImage.find(key);
            if (it == keyToImage.end()) {
                key = SerializeTransform(nullptr, idx, originIndex);
                it = keyToImage.find(key);
            }
            if (it == keyToImage.end()) continue;

            const ModuleImage parentWrap = std::get<0>(it->second);
            const cv::Mat src = std::get<1>(it->second);
            if (src.empty()) continue;
            const int W = src.cols;
            const int H = src.rows;

            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) continue;
            const Json& sampleResults = entry.at("sample_results");
            if (sampleResults.empty()) continue;

            for (const auto& sr : sampleResults) {
                if (!sr.is_object()) continue;
                if (!sr.contains("bbox") || !sr.at("bbox").is_array() || sr.at("bbox").size() < 4) continue;

                const Json& bbox = sr.at("bbox");
                bool withAngle = false;
                double angle = -100.0;
                try { withAngle = sr.contains("with_angle") ? sr.at("with_angle").get<bool>() : false; } catch (...) { withAngle = false; }
                try { angle = sr.contains("angle") ? sr.at("angle").get<double>() : -100.0; } catch (...) { angle = -100.0; }
                if ((!withAngle || angle <= -99.0) && bbox.size() >= 5) {
                    try { angle = bbox.at(4).get<double>(); withAngle = true; } catch (...) {}
                }

                cv::Mat cropped;
                std::vector<double> childA2x3;
                int cw = 0, ch = 0;

                if (withAngle && angle > -99.0) {
                    // rotated crop: bbox=[cx,cy,w,h], angle(rad)
                    const double cx = bbox.at(0).get<double>();
                    const double cy = bbox.at(1).get<double>();
                    const double w = std::abs(bbox.at(2).get<double>());
                    const double h = std::abs(bbox.at(3).get<double>());

                    const double w2 = (cropW > 0 && cropH > 0) ? static_cast<double>(cropW) : std::max<double>(minSize, w + 2.0 * cropExpand);
                    const double h2 = (cropW > 0 && cropH > 0) ? static_cast<double>(cropH) : std::max<double>(minSize, h + 2.0 * cropExpand);
                    const int iw = std::max(minSize, static_cast<int>(w2));
                    const int ih = std::max(minSize, static_cast<int>(h2));

                    const double angDeg = angle * 180.0 / kPi;
                    cv::Mat rotMat = cv::getRotationMatrix2D(cv::Point2f(static_cast<float>(cx), static_cast<float>(cy)), angDeg, 1.0);
                    rotMat.at<double>(0, 2) += (w2 / 2.0) - cx;
                    rotMat.at<double>(1, 2) += (h2 / 2.0) - cy;

                    cv::warpAffine(src, cropped, rotMat, cv::Size(iw, ih));
                    cw = iw; ch = ih;
                    childA2x3 = {
                        rotMat.at<double>(0,0), rotMat.at<double>(0,1), rotMat.at<double>(0,2),
                        rotMat.at<double>(1,0), rotMat.at<double>(1,1), rotMat.at<double>(1,2)
                    };
                } else {
                    // axis-aligned crop: bbox=[x,y,w,h]
                    const double x = bbox.at(0).get<double>();
                    const double y = bbox.at(1).get<double>();
                    const double bw = bbox.at(2).get<double>();
                    const double bh = bbox.at(3).get<double>();
                    const double x1 = x;
                    const double y1 = y;
                    const double x2 = x + bw;
                    const double y2 = y + bh;

                    int nx1 = 0, ny1 = 0, nx2 = 0, ny2 = 0;
                    // 外扩：左上 floor，右下 round（对齐 C#）
                    const double tx1 = std::max(0.0, std::min(static_cast<double>(W), x1 - cropExpand));
                    const double ty1 = std::max(0.0, std::min(static_cast<double>(H), y1 - cropExpand));
                    nx1 = static_cast<int>(std::floor(tx1));
                    ny1 = static_cast<int>(std::floor(ty1));
                    const int rx2 = static_cast<int>(std::llround(x2 + cropExpand));
                    const int ry2 = static_cast<int>(std::llround(y2 + cropExpand));
                    nx2 = std::min(W, std::max(0, rx2));
                    ny2 = std::min(H, std::max(0, ry2));
                    nx2 = std::max(nx1 + minSize, nx2);
                    ny2 = std::max(ny1 + minSize, ny2);

                    nx1 = std::max(0, std::min(W, nx1));
                    ny1 = std::max(0, std::min(H, ny1));
                    nx2 = std::max(nx1 + 1, std::min(W, nx2));
                    ny2 = std::max(ny1 + 1, std::min(H, ny2));
                    cw = nx2 - nx1;
                    ch = ny2 - ny1;
                    if (cw <= 0 || ch <= 0) continue;
                    const cv::Rect rect(nx1, ny1, cw, ch);
                    cropped = src(rect).clone();
                    childA2x3 = { 1,0,-static_cast<double>(nx1), 0,1,-static_cast<double>(ny1) };
                }

                if (cropped.empty()) continue;

                const TransformationState parentState = (parentWrap.TransformState.OriginalWidth > 0 && parentWrap.TransformState.OriginalHeight > 0)
                    ? parentWrap.TransformState
                    : TransformationState(src.cols, src.rows);
                const TransformationState childState = parentState.DeriveChild(childA2x3, cw, ch);
                ModuleImage childWrap(cropped,
                                      parentWrap.OriginalImage.empty() ? src : parentWrap.OriginalImage,
                                      childState,
                                      parentWrap.OriginalIndex);
                imagesOut.push_back(childWrap);

                Json outEntry = Json::object();
                outEntry["type"] = "local";
                outEntry["index"] = outIndex;
                outEntry["origin_index"] = parentWrap.OriginalIndex;
                outEntry["transform"] = childState.ToJson();
                outEntry["sample_results"] = Json::array();
                resultsOut.push_back(outEntry);
                outIndex += 1;
            }
        }

        return ModuleIO(std::move(imagesOut), std::move(resultsOut), Json::array());
    }
};

/// features/image_flip
class ImageFlipModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& /*resultList*/) override {
        const std::vector<ModuleImage>& images = imageList;
        if (images.empty()) return ModuleIO(std::vector<ModuleImage>(), Json::array(), Json::array());

        std::string dirStr = ReadString("direction", std::string("水平"));
        const bool vertical = (dirStr.find("竖直") != std::string::npos) || (dirStr.find("vertical") != std::string::npos);

        std::vector<ModuleImage> outImages;
        for (const auto& wrap : images) {
            if (wrap.ImageObject.empty()) continue;
            const cv::Mat baseImg = wrap.ImageObject;
            const int w = baseImg.cols;
            const int h = baseImg.rows;

            std::vector<double> A;
            if (vertical) {
                // y' = h-1 - y
                A = { 1,0,0, 0,-1, static_cast<double>(h - 1) };
            } else {
                // x' = w-1 - x
                A = { -1,0, static_cast<double>(w - 1), 0,1,0 };
            }

            cv::Mat flipped;
            try {
                cv::Mat matA(2, 3, CV_64FC1);
                matA.at<double>(0,0)=A[0]; matA.at<double>(0,1)=A[1]; matA.at<double>(0,2)=A[2];
                matA.at<double>(1,0)=A[3]; matA.at<double>(1,1)=A[4]; matA.at<double>(1,2)=A[5];
                cv::warpAffine(baseImg, flipped, matA, cv::Size(w, h));
            } catch (...) {
                const int mode = vertical ? 0 : 1; // 0: x-axis (vertical flip), 1: y-axis (horizontal flip)
                cv::flip(baseImg, flipped, mode);
            }

            const TransformationState parentState = (wrap.TransformState.OriginalWidth > 0 && wrap.TransformState.OriginalHeight > 0)
                ? wrap.TransformState
                : TransformationState(w, h);
            const TransformationState childState = parentState.DeriveChild(A, w, h);
            ModuleImage child(flipped,
                              wrap.OriginalImage.empty() ? baseImg : wrap.OriginalImage,
                              childState,
                              wrap.OriginalIndex);
            outImages.push_back(child);
        }

        return ModuleIO(std::move(outImages), Json::array(), Json::array());
    }
};

/// pre_process/coordinate_crop, features/coordinate_crop
class CoordinateCropModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        const int x = GetIntProp(Properties, "x", 0);
        const int y = GetIntProp(Properties, "y", 0);
        const int w0 = std::max(1, GetIntProp(Properties, "w", 100));
        const int h0 = std::max(1, GetIntProp(Properties, "h", 100));

        std::vector<ModuleImage> outImages;
        for (const auto& wrap : images) {
            if (wrap.ImageObject.empty()) continue;
            const cv::Mat baseMat = wrap.ImageObject;
            const int W = std::max(1, baseMat.cols);
            const int H = std::max(1, baseMat.rows);

            int x0 = std::max(0, std::min(W - 1, x));
            int y0 = std::max(0, std::min(H - 1, y));
            int ww = w0;
            int hh = h0;
            int x1 = std::min(W, x0 + ww);
            int y1 = std::min(H, y0 + hh);
            if (x1 <= x0) x1 = std::min(W, x0 + 1);
            if (y1 <= y0) y1 = std::min(H, y0 + 1);

            const int cw = std::max(1, x1 - x0);
            const int ch = std::max(1, y1 - y0);
            const cv::Rect rect(x0, y0, cw, ch);
            if (rect.width <= 0 || rect.height <= 0) continue;

            cv::Mat crop = baseMat(rect).clone();
            const std::vector<double> trans = { 1,0,-static_cast<double>(x0), 0,1,-static_cast<double>(y0) };
            const TransformationState parentState = (wrap.TransformState.OriginalWidth > 0 && wrap.TransformState.OriginalHeight > 0)
                ? wrap.TransformState
                : TransformationState(W, H);
            const TransformationState childState = parentState.DeriveChild(trans, cw, ch);

            ModuleImage child(crop,
                              wrap.OriginalImage.empty() ? baseMat : wrap.OriginalImage,
                              childState,
                              wrap.OriginalIndex);
            outImages.push_back(child);
        }

        return ModuleIO(std::move(outImages), results, Json::array());
    }
};

/// features/image_rotate_by_cls
class ImageRotateByClsModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json resultsDet = resultList.is_array() ? resultList : Json::array();

        if (images.empty()) {
            return ModuleIO(std::vector<ModuleImage>(), resultsDet, Json::array());
        }

        const auto set90 = ToLabelSet(Properties.value("rotate90_labels", Json::array()));
        const auto set180 = ToLabelSet(Properties.value("rotate180_labels", Json::array()));
        const auto set270 = ToLabelSet(Properties.value("rotate270_labels", Json::array()));

        Json clsResults = Json::array();
        if (!ExtraInputsIn.empty()) {
            clsResults = ExtraInputsIn[0].ResultList.is_array() ? ExtraInputsIn[0].ResultList : Json::array();
        }

        // Build label maps (transform/index/origin)
        std::unordered_map<std::string, std::string> tmap;
        std::unordered_map<int, std::string> imap;
        std::unordered_map<int, std::string> omap;

        auto ConsumeEntry = [&](const Json& entry) {
            if (!entry.is_object()) return;
            if (entry.value("type", "") != "local") return;
            std::string label;
            try {
                if (entry.contains("sample_results") && entry.at("sample_results").is_array() && !entry.at("sample_results").empty()) {
                    const Json& s0 = entry.at("sample_results").at(0);
                    if (s0.is_object() && s0.contains("category_name") && s0.at("category_name").is_string()) {
                        label = s0.at("category_name").get<std::string>();
                    }
                }
            } catch (...) {}
            if (label.empty()) return;

            try {
                if (entry.contains("transform") && entry.at("transform").is_object()) {
                    const std::string sig = SerializeTransformSigFromJson(entry.at("transform"));
                    if (!sig.empty()) tmap[sig] = label;
                }
            } catch (...) {}

            try { if (entry.contains("index")) imap[entry.at("index").get<int>()] = label; } catch (...) {}
            try { if (entry.contains("origin_index")) omap[entry.at("origin_index").get<int>()] = label; } catch (...) {}
        };

        for (const auto& e : clsResults) ConsumeEntry(e);
        for (const auto& e : resultsDet) ConsumeEntry(e);

        std::unordered_map<int, int> originIndexToImgIdx;
        std::unordered_map<std::string, int> sigToImgIdx;
        for (int i = 0; i < static_cast<int>(images.size()); i++) {
            originIndexToImgIdx[images[i].OriginalIndex] = i;
            if (images[i].TransformState.AffineMatrix2x3.size() >= 6) {
                sigToImgIdx[FormatAffineKey(images[i].TransformState.AffineMatrix2x3)] = i;
            }
        }

        std::vector<ModuleImage> outImages;
        outImages.reserve(images.size());
        std::unordered_map<int, TransformationState> imgNewStates;
        std::unordered_map<int, std::vector<double>> imgAffine; // current->new
        std::unordered_map<int, int> imgAngleDeg;

        for (int i = 0; i < static_cast<int>(images.size()); i++) {
            const ModuleImage& wrap = images[static_cast<size_t>(i)];
            if (wrap.ImageObject.empty()) continue;
            const cv::Mat baseImg = wrap.ImageObject;
            const int w = baseImg.cols;
            const int h = baseImg.rows;

            std::string sig = FormatAffineKey(wrap.TransformState.AffineMatrix2x3);
            std::string label;
            if (!sig.empty() && tmap.count(sig)) label = tmap[sig];
            else if (imap.count(i)) label = imap[i];
            else if (omap.count(wrap.OriginalIndex)) label = omap[wrap.OriginalIndex];

            int angleCcw = 0;
            if (!label.empty()) {
                const std::string key = label;
                if (set90.count(key)) angleCcw = 90;
                else if (set180.count(key)) angleCcw = 180;
                else if (set270.count(key)) angleCcw = 270;
            }

            std::vector<double> A;
            int newW = w, newH = h;
            GetRotationAffineCcwDeg(angleCcw, w, h, A, newW, newH);
            imgAffine[i] = A;
            imgAngleDeg[i] = angleCcw;

            cv::Mat rotated;
            if (angleCcw % 360 == 0) {
                rotated = baseImg;
            } else {
                cv::Mat matA(2, 3, CV_64FC1);
                matA.at<double>(0,0)=A[0]; matA.at<double>(0,1)=A[1]; matA.at<double>(0,2)=A[2];
                matA.at<double>(1,0)=A[3]; matA.at<double>(1,1)=A[4]; matA.at<double>(1,2)=A[5];
                cv::warpAffine(baseImg, rotated, matA, cv::Size(newW, newH));
            }

            const TransformationState parentState = (wrap.TransformState.OriginalWidth > 0 && wrap.TransformState.OriginalHeight > 0)
                ? wrap.TransformState
                : TransformationState(w, h);
            const TransformationState childState = parentState.DeriveChild(A, newW, newH);
            imgNewStates[i] = childState;

            ModuleImage child(rotated,
                              wrap.OriginalImage.empty() ? baseImg : wrap.OriginalImage,
                              childState,
                              wrap.OriginalIndex);
            outImages.push_back(child);
        }

        // 更新检测结果坐标（仅处理 resultsDet；分类结果不透传）
        Json outResults = Json::array();
        for (const auto& token : resultsDet) {
            if (!token.is_object()) { outResults.push_back(token); continue; }
            Json entry = token;
            if (entry.value("type", "") != "local") { outResults.push_back(entry); continue; }

            // 找到对应图像 idx
            int idx = -1;
            try { if (entry.contains("index")) idx = entry.at("index").get<int>(); } catch (...) { idx = -1; }
            if (idx < 0 || idx >= static_cast<int>(images.size())) {
                int oidx = -1;
                try { if (entry.contains("origin_index")) oidx = entry.at("origin_index").get<int>(); } catch (...) { oidx = -1; }
                if (oidx >= 0 && originIndexToImgIdx.count(oidx)) idx = originIndexToImgIdx[oidx];
                if (idx < 0 && entry.contains("transform") && entry.at("transform").is_object()) {
                    const std::string esig = SerializeTransformSigFromJson(entry.at("transform"));
                    if (!esig.empty() && sigToImgIdx.count(esig)) idx = sigToImgIdx[esig];
                }
            }

            if (idx < 0 || idx >= static_cast<int>(images.size()) || !imgAffine.count(idx) || !imgNewStates.count(idx)) {
                outResults.push_back(entry);
                continue;
            }

            const std::vector<double>& A = imgAffine[idx];
            entry["transform"] = imgNewStates[idx].ToJson();

            if (entry.contains("sample_results") && entry.at("sample_results").is_array()) {
                Json newSamples = Json::array();
                for (auto so : entry.at("sample_results")) {
                    if (!so.is_object()) { newSamples.push_back(so); continue; }
                    if (!so.contains("bbox") || !so.at("bbox").is_array() || so.at("bbox").size() < 4) {
                        newSamples.push_back(so); continue;
                    }
                    Json bbox = so.at("bbox");
                    bool withAngle = false;
                    double ang = -100.0;
                    try { withAngle = so.value("with_angle", false); } catch (...) { withAngle = false; }
                    try { ang = so.value("angle", -100.0); } catch (...) { ang = -100.0; }
                    if ((!withAngle || ang <= -99.0) && bbox.size() >= 5) { try { ang = bbox.at(4).get<double>(); withAngle = true; } catch (...) {} }

                    if (withAngle && ang > -99.0) {
                        // rotated bbox: transform center + angle
                        double cx = bbox.at(0).get<double>();
                        double cy = bbox.at(1).get<double>();
                        double ww = bbox.at(2).get<double>();
                        double hh = bbox.at(3).get<double>();
                        const cv::Point2f nc = Apply2x3(A, cv::Point2f(static_cast<float>(cx), static_cast<float>(cy)));
                        const double rotRad = (static_cast<double>(imgAngleDeg[idx]) * kPi / 180.0);
                        const double newAng = std::fmod(ang + rotRad + kPi, 2.0 * kPi) - kPi;
                        if (bbox.size() >= 5) {
                            so["bbox"] = Json::array({ nc.x, nc.y, ww, hh, newAng });
                        } else {
                            so["bbox"] = Json::array({ nc.x, nc.y, ww, hh });
                        }
                        so["with_angle"] = true;
                        so["angle"] = newAng;
                        newSamples.push_back(so);
                    } else {
                        // axis bbox: transform 4 corners then AABB
                        const double x = bbox.at(0).get<double>();
                        const double y = bbox.at(1).get<double>();
                        const double ww = bbox.at(2).get<double>();
                        const double hh = bbox.at(3).get<double>();
                        std::vector<cv::Point2f> pts = {
                            Apply2x3(A, cv::Point2f(static_cast<float>(x), static_cast<float>(y))),
                            Apply2x3(A, cv::Point2f(static_cast<float>(x+ww), static_cast<float>(y))),
                            Apply2x3(A, cv::Point2f(static_cast<float>(x+ww), static_cast<float>(y+hh))),
                            Apply2x3(A, cv::Point2f(static_cast<float>(x), static_cast<float>(y+hh))),
                        };
                        float minx = pts[0].x, miny = pts[0].y, maxx = pts[0].x, maxy = pts[0].y;
                        for (const auto& p : pts) {
                            minx = std::min(minx, p.x); miny = std::min(miny, p.y);
                            maxx = std::max(maxx, p.x); maxy = std::max(maxy, p.y);
                        }
                        so["bbox"] = Json::array({ minx, miny, std::max(1.0f, maxx - minx), std::max(1.0f, maxy - miny) });
                        so["with_bbox"] = true;
                        so["with_angle"] = false;
                        so["angle"] = -100.0;
                        newSamples.push_back(so);
                    }
                }
                entry["sample_results"] = newSamples;
            }

            outResults.push_back(entry);
        }

        return ModuleIO(std::move(outImages), std::move(outResults), Json::array());
    }
};

/// post_process/result_label_merge, features/result_label_merge
class ResultLabelMergeModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& imagesA = imageList;
        const Json resultsA = resultList.is_array() ? resultList : Json::array();

        if (ExtraInputsIn.empty()) {
            throw std::runtime_error("结果标签合并需要第2路输入（image_2/results_2）");
        }

        const std::vector<ModuleImage>& imagesB = ExtraInputsIn[0].ImageList;
        const Json resultsB = ExtraInputsIn[0].ResultList.is_array() ? ExtraInputsIn[0].ResultList : Json::array();

        if (imagesA.size() != imagesB.size()) {
            throw std::runtime_error(
                "两路输入图像数量不一致: " + std::to_string(imagesA.size()) + " vs " + std::to_string(imagesB.size()));
        }

        for (size_t i = 0; i < imagesA.size(); i++) {
            if (BuildImageSignature(imagesA[i]) != BuildImageSignature(imagesB[i])) {
                throw std::runtime_error("两路输入不是同一组图像，index=" + std::to_string(i));
            }
        }

        const std::string fixedText = ReadString("fixed_text", std::string());
        const bool useTop1 = ReadBool("use_first_score_top1", true);

        // 第一路标签映射：entry.index -> top1 category_name
        std::unordered_map<int, std::string> labelMap;
        for (const auto& token : resultsA) {
            if (!token.is_object()) continue;
            if (token.value("type", "") != "local") continue;
            const int idx = token.contains("index") ? SafeInt(token.at("index"), -1) : -1;
            if (idx < 0) continue;
            const Json sampleResults = (token.contains("sample_results") && token.at("sample_results").is_array())
                ? token.at("sample_results")
                : Json::array();
            const std::string firstLabel = PickFirstLabel(sampleResults, useTop1);
            if (!firstLabel.empty()) labelMap[idx] = firstLabel;
        }

        Json outResults = Json::array();
        for (const auto& token : resultsB) {
            if (!token.is_object() || token.value("type", "") != "local") {
                outResults.push_back(token);
                continue;
            }

            Json entry = token;
            const int idx = entry.contains("index") ? SafeInt(entry.at("index"), -1) : -1;
            if (idx < 0 || !labelMap.count(idx)) {
                outResults.push_back(entry);
                continue;
            }

            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) {
                outResults.push_back(entry);
                continue;
            }

            const std::string& prefix = labelMap[idx];
            Json newDets = Json::array();
            for (const auto& dToken : entry.at("sample_results")) {
                if (!dToken.is_object()) {
                    newDets.push_back(dToken);
                    continue;
                }
                Json d = dToken;
                if (d.contains("category_name") && d.at("category_name").is_string()) {
                    const std::string cat = d.at("category_name").get<std::string>();
                    d["category_name"] = prefix + fixedText + cat;
                }
                newDets.push_back(d);
            }
            entry["sample_results"] = newDets;
            outResults.push_back(entry);
        }

        return ModuleIO(imagesB, outResults, Json::array());
    }
};

// 注册
DLCV_FLOW_REGISTER_MODULE("features/image_generation", ImageGenerationModule)
DLCV_FLOW_REGISTER_MODULE("features/image_flip", ImageFlipModule)
DLCV_FLOW_REGISTER_MODULE("pre_process/coordinate_crop", CoordinateCropModule)
DLCV_FLOW_REGISTER_MODULE("features/coordinate_crop", CoordinateCropModule)
DLCV_FLOW_REGISTER_MODULE("features/image_rotate_by_cls", ImageRotateByClsModule)
DLCV_FLOW_REGISTER_MODULE("post_process/result_label_merge", ResultLabelMergeModule)
DLCV_FLOW_REGISTER_MODULE("features/result_label_merge", ResultLabelMergeModule)

} // namespace flow
} // namespace dlcv_infer

