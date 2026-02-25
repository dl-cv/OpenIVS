#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include "flow/utils/MaskRleUtils.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <string>
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

static std::string SerializeTransform(const TransformationState* st, int index, int originIndex) {
    if (st == nullptr || st->AffineMatrix2x3.size() < 6) {
        return "idx:" + std::to_string(index) + "|org:" + std::to_string(originIndex) + "|T:null";
    }
    return "idx:" + std::to_string(index) + "|org:" + std::to_string(originIndex) + "|" + FormatAffineKey(st->AffineMatrix2x3);
}

static std::string SerializeTransformSigFromJson(const Json& tObj) {
    if (!tObj.is_object() || !tObj.contains("affine_2x3") || !tObj.at("affine_2x3").is_array()) return std::string();
    const Json& a = tObj.at("affine_2x3");
    if (a.size() < 6) return std::string();
    std::vector<double> v(6, 0.0);
    for (size_t i = 0; i < 6; i++) { try { v[i] = a.at(i).get<double>(); } catch (...) { v[i] = 0.0; } }
    return FormatAffineKey(v);
}

static std::vector<std::string> ReadStringList(const Json& props, const std::string& key) {
    std::vector<std::string> out;
    if (!props.is_object() || !props.contains(key)) return out;
    try {
        const Json& v = props.at(key);
        if (v.is_array()) {
            for (const auto& it : v) if (it.is_string()) out.push_back(it.get<std::string>());
        } else if (v.is_string()) {
            out.push_back(v.get<std::string>());
        }
    } catch (...) {}
    return out;
}

static std::string ReplaceAll(std::string s, const std::string& from, const std::string& to) {
    if (from.empty()) return s;
    size_t pos = 0;
    while ((pos = s.find(from, pos)) != std::string::npos) {
        s.replace(pos, from.size(), to);
        pos += to.size();
    }
    return s;
}

/// post_process/merge_results, features/merge_results
class MergeResultsModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        // 组装输入组：主输入在前，ExtraInputsIn 顺序在后
        std::vector<std::pair<std::vector<ModuleImage>, Json>> groups;
        groups.push_back({ imageList, resultList.is_array() ? resultList : Json::array() });
        for (const auto& ch : ExtraInputsIn) {
            groups.push_back({ ch.ImageList, ch.ResultList.is_array() ? ch.ResultList : Json::array() });
        }

        std::vector<ModuleImage> mergedImages;
        Json mergedResults = Json::array();

        for (const auto& g : groups) {
            const auto& imgs = g.first;
            const Json& res = g.second;

            const int baseIndex = static_cast<int>(mergedImages.size());
            std::unordered_map<int, int> localToGlobal;
            int added = 0;

            for (int i = 0; i < static_cast<int>(imgs.size()); i++) {
                const ModuleImage& im = imgs[static_cast<size_t>(i)];
                if (im.ImageObject.empty()) continue;
                const int globalIdx = baseIndex + added;
                localToGlobal[i] = globalIdx;
                added++;
                // 重包：确保 OriginalIndex 与全局顺序一致
                ModuleImage newWrap(im.ImageObject, im.OriginalImage, im.TransformState, globalIdx);
                mergedImages.push_back(newWrap);
            }

            for (const auto& t : res) {
                if (!t.is_object()) continue;
                const Json& r = t;
                if (r.value("type", "") != "local") {
                    mergedResults.push_back(r);
                    continue;
                }
                Json r2 = r;
                int idx = r2.contains("index") ? r2.at("index").get<int>() : -1;
                int oidx = r2.contains("origin_index") ? r2.at("origin_index").get<int>() : idx;

                if (added == 1) {
                    r2["index"] = baseIndex;
                    r2["origin_index"] = baseIndex;
                } else {
                    if (idx >= 0 && localToGlobal.count(idx)) r2["index"] = localToGlobal[idx];
                    if (oidx >= 0 && localToGlobal.count(oidx)) r2["origin_index"] = localToGlobal[oidx];
                    if (!r2.contains("origin_index") && r2.contains("index")) r2["origin_index"] = r2["index"];
                }
                mergedResults.push_back(r2);
            }
        }

        return ModuleIO(std::move(mergedImages), std::move(mergedResults), Json::array());
    }
};

/// post_process/result_filter, features/result_filter
class ResultFilterModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& inImages = imageList;
        const Json inResults = resultList.is_array() ? resultList : Json::array();

        const auto cats = ReadStringList(Properties, "categories");
        std::unordered_set<std::string> keepSet(cats.begin(), cats.end());

        std::vector<ModuleImage> mainImages;
        Json mainResults = Json::array();
        std::vector<ModuleImage> altImages;
        Json altResults = Json::array();

        // image key map
        std::unordered_map<std::string, ModuleImage> keyToImage;
        for (int i = 0; i < static_cast<int>(inImages.size()); i++) {
            const ModuleImage& wrap = inImages[static_cast<size_t>(i)];
            if (wrap.ImageObject.empty()) continue;
            const std::string key = SerializeTransform(&wrap.TransformState, i, wrap.OriginalIndex);
            keyToImage[key] = wrap;
        }

        for (const auto& t : inResults) {
            if (!t.is_object()) continue;
            const Json& r = t;
            const int idx = r.contains("index") ? r.at("index").get<int>() : -1;
            const int originIndex = r.contains("origin_index") ? r.at("origin_index").get<int>() : idx;
            TransformationState st;
            try { if (r.contains("transform") && r.at("transform").is_object()) st = TransformationState::FromJson(r.at("transform")); } catch (...) {}
            const std::string key = SerializeTransform(&st, idx, originIndex);

            std::vector<Json> sKeep;
            std::vector<Json> sAlt;
            if (r.contains("sample_results") && r.at("sample_results").is_array()) {
                for (const auto& s : r.at("sample_results")) {
                    if (!s.is_object()) continue;
                    const std::string cat = s.value("category_name", "");
                    if (keepSet.empty() || keepSet.count(cat)) sKeep.push_back(s);
                    else sAlt.push_back(s);
                }
            }

            auto itImg = keyToImage.find(key);
            if (itImg == keyToImage.end()) continue;
            const ModuleImage imgObj = itImg->second;

            if (!sKeep.empty() || !r.contains("sample_results")) {
                mainImages.push_back(imgObj);
                Json e = Json::object();
                e["type"] = "local";
                e["index"] = static_cast<int>(mainResults.size());
                e["origin_index"] = originIndex;
                e["transform"] = st.ToJson();
                Json arr = Json::array();
                for (const auto& x : sKeep) arr.push_back(x);
                e["sample_results"] = arr;
                mainResults.push_back(e);
            }
            if (!sAlt.empty()) {
                altImages.push_back(imgObj);
                Json e2 = Json::object();
                e2["type"] = "local";
                e2["index"] = static_cast<int>(altResults.size());
                e2["origin_index"] = originIndex;
                e2["transform"] = st.ToJson();
                Json arr2 = Json::array();
                for (const auto& x : sAlt) arr2.push_back(x);
                e2["sample_results"] = arr2;
                altResults.push_back(e2);
            }
        }

        // extra output
        this->ExtraOutputs.push_back(ModuleChannel(altImages, altResults));

        bool hasPositive = false;
        for (const auto& t : mainResults) {
            if (t.is_object() && t.contains("sample_results") && t.at("sample_results").is_array() && !t.at("sample_results").empty()) {
                hasPositive = true;
                break;
            }
        }
        this->ScalarOutputsByName["has_positive"] = hasPositive;
        return ModuleIO(std::move(mainImages), std::move(mainResults), Json::array());
    }
};

/// post_process/result_filter_advanced, features/result_filter_advanced
class ResultFilterAdvancedModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& inImages = imageList;
        const Json inResults = resultList.is_array() ? resultList : Json::array();

        const bool enableBBoxWh = ReadBool("enable_bbox_wh", false);
        const bool enableRBoxWh = ReadBool("enable_rbox_wh", false);
        const bool enableBBoxArea = ReadBool("enable_bbox_area", false);
        const bool enableMaskArea = ReadBool("enable_mask_area", false);

        auto readOpt = [&](const std::string& key) -> Json {
            if (Properties.is_object() && Properties.contains(key) && !Properties.at(key).is_null()) return Properties.at(key);
            return Json();
        };
        auto optD = [&](const Json& v, bool& has) -> double {
            has = false;
            try {
                if (v.is_number()) { has = true; return v.get<double>(); }
                if (v.is_string()) { has = true; return std::stod(v.get<std::string>()); }
            } catch (...) {}
            return 0.0;
        };

        bool has_bbox_w_min=false, has_bbox_w_max=false, has_bbox_h_min=false, has_bbox_h_max=false;
        bool has_rbox_w_min=false, has_rbox_w_max=false, has_rbox_h_min=false, has_rbox_h_max=false;
        bool has_bbox_area_min=false, has_bbox_area_max=false, has_mask_area_min=false, has_mask_area_max=false;

        const double bboxWMin = optD(readOpt("bbox_w_min"), has_bbox_w_min);
        const double bboxWMax = optD(readOpt("bbox_w_max"), has_bbox_w_max);
        const double bboxHMin = optD(readOpt("bbox_h_min"), has_bbox_h_min);
        const double bboxHMax = optD(readOpt("bbox_h_max"), has_bbox_h_max);

        const double rboxWMin = optD(readOpt("rbox_w_min"), has_rbox_w_min);
        const double rboxWMax = optD(readOpt("rbox_w_max"), has_rbox_w_max);
        const double rboxHMin = optD(readOpt("rbox_h_min"), has_rbox_h_min);
        const double rboxHMax = optD(readOpt("rbox_h_max"), has_rbox_h_max);

        const double bboxAreaMin = optD(readOpt("bbox_area_min"), has_bbox_area_min);
        const double bboxAreaMax = optD(readOpt("bbox_area_max"), has_bbox_area_max);
        const double maskAreaMin = optD(readOpt("mask_area_min"), has_mask_area_min);
        const double maskAreaMax = optD(readOpt("mask_area_max"), has_mask_area_max);

        std::vector<ModuleImage> mainImages;
        Json mainResults = Json::array();
        std::vector<ModuleImage> altImages;
        Json altResults = Json::array();

        // map entry->image by (idx, origin, transform sig)
        std::unordered_map<int, int> originToIdx;
        std::unordered_map<std::string, int> sigToIdx;
        for (int i = 0; i < static_cast<int>(inImages.size()); i++) {
            originToIdx[inImages[static_cast<size_t>(i)].OriginalIndex] = i;
            if (inImages[static_cast<size_t>(i)].TransformState.AffineMatrix2x3.size() >= 6) {
                sigToIdx[FormatAffineKey(inImages[static_cast<size_t>(i)].TransformState.AffineMatrix2x3)] = i;
            }
        }

        auto passOne = [&](const Json& so) -> bool {
            if (!so.is_object()) return false;
            if (!so.contains("bbox") || !so.at("bbox").is_array() || so.at("bbox").size() < 4) return false;
            const Json& bb = so.at("bbox");
            const bool isRot = (bb.size() >= 5) || (so.value("with_angle", false) && (so.value("angle", -100.0) > -99.0));
            const double w = std::abs(bb.at(2).get<double>());
            const double h = std::abs(bb.at(3).get<double>());
            const double area = w * h;

            if (!isRot && enableBBoxWh) {
                if (has_bbox_w_min && w < bboxWMin) return false;
                if (has_bbox_w_max && w > bboxWMax) return false;
                if (has_bbox_h_min && h < bboxHMin) return false;
                if (has_bbox_h_max && h > bboxHMax) return false;
            }
            if (isRot && enableRBoxWh) {
                if (has_rbox_w_min && w < rboxWMin) return false;
                if (has_rbox_w_max && w > rboxWMax) return false;
                if (has_rbox_h_min && h < rboxHMin) return false;
                if (has_rbox_h_max && h > rboxHMax) return false;
            }
            if (enableBBoxArea) {
                if (has_bbox_area_min && area < bboxAreaMin) return false;
                if (has_bbox_area_max && area > bboxAreaMax) return false;
            }
            if (enableMaskArea) {
                double marea = 0.0;
                if (so.contains("mask_rle") && so.at("mask_rle").is_object()) {
                    marea = CalculateMaskArea(so.at("mask_rle"));
                }
                if (has_mask_area_min && marea < maskAreaMin) return false;
                if (has_mask_area_max && marea > maskAreaMax) return false;
            }
            return true;
        };

        for (const auto& token : inResults) {
            if (!token.is_object()) continue;
            const Json& entry = token;
            if (entry.value("type", "") != "local") continue;

            int idx = entry.contains("index") ? entry.at("index").get<int>() : -1;
            int originIndex = entry.contains("origin_index") ? entry.at("origin_index").get<int>() : idx;
            std::string sig;
            try { if (entry.contains("transform") && entry.at("transform").is_object()) sig = SerializeTransformSigFromJson(entry.at("transform")); } catch (...) {}
            int imgIdx = (idx >= 0 && idx < static_cast<int>(inImages.size())) ? idx : -1;
            if (imgIdx < 0 && originToIdx.count(originIndex)) imgIdx = originToIdx[originIndex];
            if (imgIdx < 0 && !sig.empty() && sigToIdx.count(sig)) imgIdx = sigToIdx[sig];
            if (imgIdx < 0 || imgIdx >= static_cast<int>(inImages.size())) continue;

            const ModuleImage imgObj = inImages[static_cast<size_t>(imgIdx)];

            std::vector<Json> passList;
            std::vector<Json> failList;
            if (entry.contains("sample_results") && entry.at("sample_results").is_array()) {
                for (const auto& s : entry.at("sample_results")) {
                    if (!s.is_object()) continue;
                    if (passOne(s)) passList.push_back(s);
                    else failList.push_back(s);
                }
            }

            TransformationState st;
            try { if (entry.contains("transform") && entry.at("transform").is_object()) st = TransformationState::FromJson(entry.at("transform")); } catch (...) {}

            if (!passList.empty()) {
                mainImages.push_back(imgObj);
                Json e = Json::object();
                e["type"] = "local";
                e["index"] = static_cast<int>(mainResults.size());
                e["origin_index"] = originIndex;
                e["transform"] = st.ToJson();
                Json arr = Json::array(); for (const auto& x : passList) arr.push_back(x);
                e["sample_results"] = arr;
                mainResults.push_back(e);
            }
            if (!failList.empty()) {
                altImages.push_back(imgObj);
                Json e2 = Json::object();
                e2["type"] = "local";
                e2["index"] = static_cast<int>(altResults.size());
                e2["origin_index"] = originIndex;
                e2["transform"] = st.ToJson();
                Json arr2 = Json::array(); for (const auto& x : failList) arr2.push_back(x);
                e2["sample_results"] = arr2;
                altResults.push_back(e2);
            }
        }

        this->ExtraOutputs.push_back(ModuleChannel(altImages, altResults));
        bool hasPositive = false;
        for (const auto& t : mainResults) {
            if (t.is_object() && t.contains("sample_results") && t.at("sample_results").is_array() && !t.at("sample_results").empty()) { hasPositive = true; break; }
        }
        this->ScalarOutputsByName["has_positive"] = hasPositive;
        return ModuleIO(std::move(mainImages), std::move(mainResults), Json::array());
    }
};

/// post_process/text_replacement, features/text_replacement
class TextReplacementModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        std::unordered_map<std::string, std::string> mapping;
        try {
            if (Properties.is_object() && Properties.contains("mapping")) {
                const Json& m = Properties.at("mapping");
                if (m.is_object()) {
                    for (auto it = m.begin(); it != m.end(); ++it) {
                        mapping[it.key()] = it.value().is_string() ? it.value().get<std::string>() : it.value().dump();
                    }
                } else if (m.is_string()) {
                    const std::string s = m.get<std::string>();
                    try {
                        Json jo = Json::parse(s);
                        if (jo.is_object()) {
                            for (auto it = jo.begin(); it != jo.end(); ++it) {
                                mapping[it.key()] = it.value().is_string() ? it.value().get<std::string>() : it.value().dump();
                            }
                        }
                    } catch (...) {}
                }
            }
        } catch (...) {}

        if (mapping.empty()) {
            return ModuleIO(images, results, Json::array());
        }

        Json outResults = Json::array();
        for (const auto& token : results) {
            if (!token.is_object()) { outResults.push_back(token); continue; }
            Json entry = token;
            if (entry.value("type", "") != "local") { outResults.push_back(entry); continue; }
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) { outResults.push_back(entry); continue; }
            Json newDets = Json::array();
            for (const auto& dToken : entry.at("sample_results")) {
                if (!dToken.is_object()) { newDets.push_back(dToken); continue; }
                Json d = dToken;
                if (d.contains("category_name") && d.at("category_name").is_string()) {
                    std::string cat = d.at("category_name").get<std::string>();
                    std::string newCat = cat;
                    for (const auto& kv : mapping) {
                        newCat = ReplaceAll(newCat, kv.first, kv.second);
                    }
                    d["category_name"] = newCat;
                }
                newDets.push_back(d);
            }
            entry["sample_results"] = newDets;
            outResults.push_back(entry);
        }
        return ModuleIO(images, outResults, Json::array());
    }
};

/// post_process/mask_to_rbox, features/mask_to_rbox
class MaskToRBoxModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();
        Json outResults = Json::array();

        auto NormalizeAngleLe90Rad = [](double aRad) -> double {
            double x = aRad;
            x = std::fmod(x + kPi / 2.0, kPi);
            if (x < 0) x += kPi;
            x -= kPi / 2.0;
            return x;
        };

        for (const auto& entryToken : results) {
            if (!entryToken.is_object() || entryToken.value("type", "") != "local") {
                outResults.push_back(entryToken);
                continue;
            }
            const Json& entry = entryToken;
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) {
                outResults.push_back(entryToken);
                continue;
            }
            Json newDets = Json::array();
            for (const auto& dToken : entry.at("sample_results")) {
                if (!dToken.is_object()) continue;
                const Json& d = dToken;
                if (!d.contains("mask_rle") || !d.at("mask_rle").is_object()) {
                    continue; // 与 C# 对齐：无 mask_rle 直接跳过该条
                }
                if (!d.contains("bbox") || !d.at("bbox").is_array() || d.at("bbox").size() < 4) continue;
                const Json& bbox = d.at("bbox");
                const float bx = static_cast<float>(bbox.at(0).get<double>());
                const float by = static_cast<float>(bbox.at(1).get<double>());

                cv::Mat maskMat = MaskInfoToMat(d.at("mask_rle"));
                if (maskMat.empty()) continue;
                std::vector<cv::Point> pts;
                cv::findNonZero(maskMat, pts);
                if (pts.empty()) continue;

                cv::RotatedRect rr = cv::minAreaRect(pts);
                rr.center += cv::Point2f(bx, by);

                float rw = rr.size.width;
                float rh = rr.size.height;
                float angDeg = rr.angle;
                if (rw < rh) {
                    std::swap(rw, rh);
                    angDeg += 90.0f;
                }
                double angRad = NormalizeAngleLe90Rad(static_cast<double>(angDeg) * kPi / 180.0);

                Json d2 = d;
                d2["bbox"] = Json::array({ rr.center.x, rr.center.y, rw, rh, angRad });
                d2["with_angle"] = true;
                d2["angle"] = angRad;
                d2.erase("mask_rle");
                d2.erase("mask");
                newDets.push_back(d2);
            }
            Json entry2 = entry;
            entry2["sample_results"] = newDets;
            outResults.push_back(entry2);
        }

        return ModuleIO(images, outResults, Json::array());
    }
};

/// post_process/rbox_correction, features/rbox_correction
class RBoxCorrectionModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        const int fillVal = ReadInt("fill_value", 0);

        // 先按 index 建立 entry 列表
        std::unordered_map<int, std::vector<Json>> imgIdxToEntries;
        for (const auto& token : results) {
            if (!token.is_object()) continue;
            const Json& e = token;
            if (e.value("type", "") != "local") continue;
            const int idx = e.value("index", -1);
            if (idx >= 0) imgIdxToEntries[idx].push_back(e);
        }

        std::vector<ModuleImage> outImages;
        outImages.reserve(images.size());

        std::unordered_map<int, std::vector<double>> imgA;
        std::unordered_map<int, TransformationState> imgState;

        for (int i = 0; i < static_cast<int>(images.size()); i++) {
            const ModuleImage& wrap = images[static_cast<size_t>(i)];
            if (wrap.ImageObject.empty()) { continue; }

            double refAngleRad = 0.0;
            bool found = false;
            auto it = imgIdxToEntries.find(i);
            if (it != imgIdxToEntries.end()) {
                for (const auto& entry : it->second) {
                    if (!entry.contains("transform") || !entry.at("transform").is_object()) continue;
                    try {
                        const Json& a23 = entry.at("transform").at("affine_2x3");
                        if (a23.is_array() && a23.size() >= 6) {
                            const double a = a23.at(0).get<double>();
                            const double c = a23.at(3).get<double>();
                            refAngleRad = std::atan2(c, a);
                            found = true;
                            break;
                        }
                    } catch (...) {}
                }
            }
            if (!found) {
                outImages.push_back(wrap);
                continue;
            }

            const double rotDeg = -refAngleRad * 180.0 / kPi;
            const cv::Mat baseImg = wrap.ImageObject;
            const int w = baseImg.cols;
            const int h = baseImg.rows;
            const cv::Point2f center(static_cast<float>(w / 2.0), static_cast<float>(h / 2.0));
            cv::Mat rotMat = cv::getRotationMatrix2D(center, rotDeg, 1.0);

            cv::Mat rotated;
            cv::warpAffine(baseImg, rotated, rotMat, cv::Size(w, h), cv::INTER_LINEAR, cv::BORDER_CONSTANT, cv::Scalar(fillVal, fillVal, fillVal));

            std::vector<double> A = {
                rotMat.at<double>(0,0), rotMat.at<double>(0,1), rotMat.at<double>(0,2),
                rotMat.at<double>(1,0), rotMat.at<double>(1,1), rotMat.at<double>(1,2)
            };

            const TransformationState parentState = (wrap.TransformState.OriginalWidth > 0 && wrap.TransformState.OriginalHeight > 0)
                ? wrap.TransformState
                : TransformationState(w, h);
            const TransformationState childState = parentState.DeriveChild(A, w, h);
            imgA[i] = A;
            imgState[i] = childState;

            ModuleImage child(rotated,
                              wrap.OriginalImage.empty() ? baseImg : wrap.OriginalImage,
                              childState,
                              wrap.OriginalIndex);
            outImages.push_back(child);
        }

        // 更新结果坐标（只做 bbox 坐标变换；mask 不再保证一致，移除）
        Json outResults = Json::array();
        for (const auto& token : results) {
            if (!token.is_object()) { outResults.push_back(token); continue; }
            Json entry = token;
            if (entry.value("type", "") != "local") { outResults.push_back(entry); continue; }
            const int idx = entry.value("index", -1);
            if (idx < 0 || !imgA.count(idx) || !imgState.count(idx)) { outResults.push_back(entry); continue; }
            const std::vector<double>& A = imgA[idx];
            entry["transform"] = imgState[idx].ToJson();
            if (entry.contains("sample_results") && entry.at("sample_results").is_array()) {
                Json newSrs = Json::array();
                for (auto so : entry.at("sample_results")) {
                    if (!so.is_object()) { newSrs.push_back(so); continue; }
                    if (so.contains("bbox") && so.at("bbox").is_array() && so.at("bbox").size() >= 4) {
                        Json bb = so.at("bbox");
                        const bool isRot = (bb.size() >= 5) || (so.value("with_angle", false) && so.value("angle", -100.0) > -99.0);
                        if (isRot) {
                            const double cx = bb.at(0).get<double>();
                            const double cy = bb.at(1).get<double>();
                            const double w = bb.at(2).get<double>();
                            const double h = bb.at(3).get<double>();
                            double ang = so.value("angle", -100.0);
                            if (bb.size() >= 5) ang = bb.at(4).get<double>();
                            const cv::Point2f nc(
                                static_cast<float>(A[0] * cx + A[1] * cy + A[2]),
                                static_cast<float>(A[3] * cx + A[4] * cy + A[5]));
                            so["bbox"] = Json::array({ nc.x, nc.y, w, h, ang });
                            so["with_angle"] = true;
                            so["angle"] = ang;
                        } else {
                            const double x = bb.at(0).get<double>();
                            const double y = bb.at(1).get<double>();
                            const double w = bb.at(2).get<double>();
                            const double h = bb.at(3).get<double>();
                            auto P = [&](double px, double py) -> cv::Point2f {
                                return cv::Point2f(static_cast<float>(A[0] * px + A[1] * py + A[2]),
                                                   static_cast<float>(A[3] * px + A[4] * py + A[5]));
                            };
                            std::vector<cv::Point2f> pts = { P(x,y), P(x+w,y), P(x+w,y+h), P(x,y+h) };
                            float minx=pts[0].x,miny=pts[0].y,maxx=pts[0].x,maxy=pts[0].y;
                            for (const auto& p : pts) { minx=std::min(minx,p.x); miny=std::min(miny,p.y); maxx=std::max(maxx,p.x); maxy=std::max(maxy,p.y); }
                            so["bbox"] = Json::array({ minx, miny, std::max(1.0f, maxx-minx), std::max(1.0f, maxy-miny) });
                            so["with_angle"] = false;
                            so["angle"] = -100.0;
                        }
                    }
                    so.erase("mask_rle");
                    so.erase("mask");
                    newSrs.push_back(so);
                }
                entry["sample_results"] = newSrs;
            }
            outResults.push_back(entry);
        }

        return ModuleIO(std::move(outImages), std::move(outResults), Json::array());
    }
};

// 注册
DLCV_FLOW_REGISTER_MODULE("post_process/merge_results", MergeResultsModule)
DLCV_FLOW_REGISTER_MODULE("features/merge_results", MergeResultsModule)
DLCV_FLOW_REGISTER_MODULE("post_process/result_filter", ResultFilterModule)
DLCV_FLOW_REGISTER_MODULE("features/result_filter", ResultFilterModule)
DLCV_FLOW_REGISTER_MODULE("post_process/result_filter_advanced", ResultFilterAdvancedModule)
DLCV_FLOW_REGISTER_MODULE("features/result_filter_advanced", ResultFilterAdvancedModule)
DLCV_FLOW_REGISTER_MODULE("post_process/text_replacement", TextReplacementModule)
DLCV_FLOW_REGISTER_MODULE("features/text_replacement", TextReplacementModule)
DLCV_FLOW_REGISTER_MODULE("post_process/mask_to_rbox", MaskToRBoxModule)
DLCV_FLOW_REGISTER_MODULE("features/mask_to_rbox", MaskToRBoxModule)
DLCV_FLOW_REGISTER_MODULE("post_process/rbox_correction", RBoxCorrectionModule)
DLCV_FLOW_REGISTER_MODULE("features/rbox_correction", RBoxCorrectionModule)

} // namespace flow
} // namespace dlcv_infer

