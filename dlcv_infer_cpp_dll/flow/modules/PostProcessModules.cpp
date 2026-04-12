#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include "flow/utils/MaskRleUtils.h"

#include <array>
#include <algorithm>
#include <cctype>
#include <cstdint>
#include <cmath>
#include <cstdio>
#include <limits>
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

static std::string SerializeTokenCompact(const Json& token) {
    try {
        if (token.is_null()) return "null";
        return token.dump();
    } catch (...) {
        return "null";
    }
}

static int SafeIntFromJson(const Json& token, int defaultValue) {
    try {
        if (token.is_number_integer()) return token.get<int>();
        if (token.is_number()) return static_cast<int>(std::llround(token.get<double>()));
        if (token.is_string()) return std::stoi(token.get<std::string>());
    } catch (...) {}
    return defaultValue;
}

static bool TryReadDouble(const Json& token, double& outVal) {
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

/// post_process/merge_results, features/merge_results
class MergeResultsModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const Json emptyResults = Json::array();
        const Json& inResults = resultList.is_array() ? resultList : emptyResults;
        return ProcessCore(imageList, inResults, nullptr);
    }

    ModuleIO ProcessOwned(const std::vector<ModuleImage>& imageList, Json&& resultList) override {
        const Json emptyResults = Json::array();
        const bool hasOwnedArray = resultList.is_array();
        const Json& inResults = hasOwnedArray ? resultList : emptyResults;
        return ProcessCore(imageList, inResults, hasOwnedArray ? &resultList : nullptr);
    }

private:
    ModuleIO ProcessCore(const std::vector<ModuleImage>& imageList, const Json& resultList, Json* movableMainResults) {
        struct LocalResultIndex final {
            bool IsLocal = false;
            int Index = -1;
            int OriginIndex = -1;
        };

        auto readLocalResultIndex = [](const Json& token) -> LocalResultIndex {
            LocalResultIndex out;
            if (!token.is_object()) return out;
            if (token.value("type", "") != "local") return out;
            out.IsLocal = true;
            out.Index = token.contains("index") ? SafeIntFromJson(token.at("index"), -1) : -1;
            out.OriginIndex = token.contains("origin_index")
                ? SafeIntFromJson(token.at("origin_index"), out.Index)
                : out.Index;
            return out;
        };

        struct GroupRef final {
            const std::vector<ModuleImage>* Images;
            const Json* Results;
            Json* MutableResults = nullptr;
        };

        std::vector<GroupRef> groups;
        groups.reserve(1 + ExtraInputsIn.size());
        groups.push_back(GroupRef{ &imageList, &resultList, movableMainResults });
        for (const auto& ch : ExtraInputsIn) {
            groups.push_back(GroupRef{ &ch.ImageList, &ch.ResultList, nullptr });
        }

        std::vector<ModuleImage> mergedImages;
        Json mergedResults = Json::array();
        auto& mergedResultsArr = mergedResults.get_ref<Json::array_t&>();

        size_t estimatedImageCount = 0;
        size_t estimatedResultCount = 0;
        for (const auto& g : groups) {
            if (g.Images != nullptr) estimatedImageCount += g.Images->size();
            if (g.Results != nullptr && g.Results->is_array()) estimatedResultCount += g.Results->size();
        }
        mergedImages.reserve(estimatedImageCount);
        mergedResultsArr.reserve(estimatedResultCount);

        for (const auto& g : groups) {
            if (g.Images == nullptr) continue;
            const auto& imgs = *(g.Images);
            const Json* res = (g.Results != nullptr && g.Results->is_array()) ? g.Results : nullptr;

            const int baseIndex = static_cast<int>(mergedImages.size());
            std::vector<int> localToGlobal(imgs.size(), -1);
            int added = 0;

            for (int i = 0; i < static_cast<int>(imgs.size()); i++) {
                const ModuleImage& im = imgs[static_cast<size_t>(i)];
                if (im.ImageObject.empty()) continue;
                const int globalIdx = baseIndex + added;
                localToGlobal[static_cast<size_t>(i)] = globalIdx;
                added++;
                // 重包：确保 OriginalIndex 与全局顺序一致
                ModuleImage newWrap(im.ImageObject, im.OriginalImage, im.TransformState, globalIdx);
                mergedImages.push_back(newWrap);
            }
            if (res == nullptr) continue;

            auto appendMappedToken = [&](Json token) {
                if (!token.is_object()) return;
                const LocalResultIndex localIndex = readLocalResultIndex(token);
                if (!localIndex.IsLocal) {
                    mergedResultsArr.push_back(std::move(token));
                    return;
                }
                int idx = localIndex.Index;
                int oidx = localIndex.OriginIndex;
                Json& r2 = token;

                if (added == 1) {
                    r2["index"] = baseIndex;
                    r2["origin_index"] = baseIndex;
                } else {
                    if (idx >= 0 && idx < static_cast<int>(localToGlobal.size()) && localToGlobal[static_cast<size_t>(idx)] >= 0) {
                        r2["index"] = localToGlobal[static_cast<size_t>(idx)];
                    }
                    if (oidx >= 0 && oidx < static_cast<int>(localToGlobal.size()) && localToGlobal[static_cast<size_t>(oidx)] >= 0) {
                        r2["origin_index"] = localToGlobal[static_cast<size_t>(oidx)];
                    }
                    if (!r2.contains("origin_index") && r2.contains("index")) r2["origin_index"] = r2["index"];
                }
                mergedResultsArr.push_back(std::move(token));
            };

            const bool canMoveTokens = (g.MutableResults != nullptr && g.MutableResults->is_array() && g.MutableResults == res);
            if (canMoveTokens) {
                auto& srcArr = g.MutableResults->get_ref<Json::array_t&>();
                for (auto& token : srcArr) {
                    appendMappedToken(std::move(token));
                }
            } else {
                for (const auto& token : *res) {
                    appendMappedToken(token);
                }
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
        const Json emptyResults = Json::array();
        const Json& inResults = resultList.is_array() ? resultList : emptyResults;

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
        const Json emptyResults = Json::array();
        const Json& inResults = resultList.is_array() ? resultList : emptyResults;
        return ProcessCore(imageList, inResults, nullptr);
    }

    ModuleIO ProcessOwned(const std::vector<ModuleImage>& imageList, Json&& resultList) override {
        const Json emptyResults = Json::array();
        const bool hasOwnedArray = resultList.is_array();
        const Json& inResults = hasOwnedArray ? resultList : emptyResults;
        return ProcessCore(imageList, inResults, hasOwnedArray ? &resultList : nullptr);
    }

private:
    ModuleIO ProcessCore(const std::vector<ModuleImage>& imageList, const Json& inResults, Json* ownedResults) {
        const std::vector<ModuleImage>& inImages = imageList;

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
        const bool noFilter = !enableBBoxWh && !enableRBoxWh && !enableBBoxArea && !enableMaskArea;
        const bool emitFailBranch = IsCurrentOutputConnected(Context, 2) || IsCurrentOutputConnected(Context, 3);

        if (noFilter && !emitFailBranch) {
            bool hasPositive = false;
            for (const auto& t : inResults) {
                if (!t.is_object()) continue;
                if (!t.contains("sample_results") || !t.at("sample_results").is_array()) continue;
                if (!t.at("sample_results").empty()) {
                    hasPositive = true;
                    break;
                }
            }
            this->ScalarOutputsByName["has_positive"] = hasPositive;
            if (ownedResults != nullptr) {
                return ModuleIO(inImages, std::move(*ownedResults), Json::array());
            }
            return ModuleIO(inImages, inResults, Json::array());
        }

        std::vector<ModuleImage> mainImages;
        Json mainResults = Json::array();
        std::vector<ModuleImage> altImages;
        Json altResults = Json::array();
        std::unordered_map<const Json*, double> maskAreaCache;

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
                    const Json& maskInfo = so.at("mask_rle");
                    const Json* maskKey = &maskInfo;
                    auto it = maskAreaCache.find(maskKey);
                    if (it != maskAreaCache.end()) {
                        marea = it->second;
                    } else {
                        marea = CalculateMaskArea(maskInfo);
                        maskAreaCache.emplace(maskKey, marea);
                    }
                }
                if (has_mask_area_min && marea < maskAreaMin) return false;
                if (has_mask_area_max && marea > maskAreaMax) return false;
            }
            return true;
        };

        // 常见 batch 场景下 image_list 与 result_list 一一对应，优先走顺序快路径。
        bool alignedLocalFastPath = false;
        if (inResults.is_array() && !inImages.empty() && inImages.size() == inResults.size()) {
            alignedLocalFastPath = true;
            for (size_t i = 0; i < inResults.size(); i++) {
                const Json& obj = inResults.at(i);
                if (!obj.is_object()) {
                    alignedLocalFastPath = false;
                    break;
                }
                if (obj.value("type", "") != "local") {
                    alignedLocalFastPath = false;
                    break;
                }
            }
        }

        if (alignedLocalFastPath) {
            for (size_t i = 0; i < inImages.size(); i++) {
                const ModuleImage& imgObj = inImages[i];
                if (imgObj.ImageObject.empty()) continue;

                const Json& entry = inResults.at(i);
                const bool hasSampleResults = entry.contains("sample_results") && entry.at("sample_results").is_array();
                Json passArr = Json::array();
                Json failArr = emitFailBranch ? Json::array() : Json();

                if (hasSampleResults) {
                    const Json& samples = entry.at("sample_results");
                    auto& passVec = passArr.get_ref<Json::array_t&>();
                    passVec.reserve(samples.size());
                    Json::array_t* failVec = nullptr;
                    if (emitFailBranch) {
                        failVec = &failArr.get_ref<Json::array_t&>();
                        failVec->reserve(samples.size());
                    }
                    for (const auto& s : samples) {
                        if (!s.is_object()) continue;
                        if (noFilter || passOne(s)) {
                            passVec.push_back(s);
                        } else if (failVec != nullptr) {
                            failVec->push_back(s);
                        }
                    }
                }

                int originIndex = imgObj.OriginalIndex;
                if (entry.contains("origin_index")) {
                    originIndex = SafeIntFromJson(entry.at("origin_index"), originIndex);
                }

                Json transformOut = Json();
                if (entry.contains("transform") && entry.at("transform").is_object()) {
                    transformOut = entry.at("transform");
                }

                if (!hasSampleResults || !passArr.empty()) {
                    mainImages.push_back(imgObj);
                    Json e = Json::object();
                    e["type"] = "local";
                    e["index"] = static_cast<int>(mainResults.size());
                    e["origin_index"] = originIndex;
                    e["transform"] = transformOut;
                    e["sample_results"] = hasSampleResults ? std::move(passArr) : Json::array();
                    mainResults.push_back(std::move(e));
                }

                if (emitFailBranch && hasSampleResults && !failArr.empty()) {
                    altImages.push_back(imgObj);
                    Json e2 = Json::object();
                    e2["type"] = "local";
                    e2["index"] = static_cast<int>(altResults.size());
                    e2["origin_index"] = originIndex;
                    e2["transform"] = transformOut;
                    e2["sample_results"] = std::move(failArr);
                    altResults.push_back(std::move(e2));
                }
            }

            if (emitFailBranch) {
                this->ExtraOutputs.push_back(ModuleChannel(std::move(altImages), std::move(altResults)));
            }
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

            Json passArr = Json::array();
            Json failArr = emitFailBranch ? Json::array() : Json();
            if (entry.contains("sample_results") && entry.at("sample_results").is_array()) {
                const Json& samples = entry.at("sample_results");
                auto& passVec = passArr.get_ref<Json::array_t&>();
                passVec.reserve(samples.size());
                Json::array_t* failVec = nullptr;
                if (emitFailBranch) {
                    failVec = &failArr.get_ref<Json::array_t&>();
                    failVec->reserve(samples.size());
                }
                for (const auto& s : entry.at("sample_results")) {
                    if (!s.is_object()) continue;
                    if (noFilter || passOne(s)) passVec.push_back(s);
                    else if (failVec != nullptr) failVec->push_back(s);
                }
            }

            Json transformOut = Json();
            if (entry.contains("transform") && entry.at("transform").is_object()) {
                transformOut = entry.at("transform");
            }

            if (!passArr.empty()) {
                mainImages.push_back(imgObj);
                Json e = Json::object();
                e["type"] = "local";
                e["index"] = static_cast<int>(mainResults.size());
                e["origin_index"] = originIndex;
                e["transform"] = transformOut;
                e["sample_results"] = std::move(passArr);
                mainResults.push_back(e);
            }
            if (emitFailBranch && !failArr.empty()) {
                altImages.push_back(imgObj);
                Json e2 = Json::object();
                e2["type"] = "local";
                e2["index"] = static_cast<int>(altResults.size());
                e2["origin_index"] = originIndex;
                e2["transform"] = transformOut;
                e2["sample_results"] = std::move(failArr);
                altResults.push_back(e2);
            }
        }

        if (emitFailBranch) {
            this->ExtraOutputs.push_back(ModuleChannel(std::move(altImages), std::move(altResults)));
        }
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
        const Json emptyResults = Json::array();
        const Json& results = resultList.is_array() ? resultList : emptyResults;

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

/// post_process/result_category_override, features/result_category_override
class ResultCategoryOverrideModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        // Python: extra_inputs_in[1]["result_list"]；C++ 执行器聚合后对应 ExtraInputsIn[0].ResultList。
        Json replaceResults = Json::array();
        if (!ExtraInputsIn.empty() && ExtraInputsIn[0].ResultList.is_array()) {
            replaceResults = ExtraInputsIn[0].ResultList;
        }

        const std::string overrideName = ExtractFirstCategoryName(replaceResults);
        if (overrideName.empty()) {
            return ModuleIO(images, results, Json::array());
        }

        Json outResults = Json::array();
        for (const auto& token : results) {
            if (!token.is_object()) {
                outResults.push_back(token);
                continue;
            }

            const Json& entry = token;
            bool changed = false;
            Json entryCopy = entry;

            if (entry.contains("category_name") && entry.at("category_name").is_string()) {
                entryCopy["category_name"] = overrideName;
                changed = true;
            }

            if (entry.contains("sample_results") && entry.at("sample_results").is_array()) {
                bool sampleChanged = false;
                Json newSampleResults = OverrideSampleResults(entry.at("sample_results"), overrideName, sampleChanged);
                if (sampleChanged) {
                    entryCopy["sample_results"] = std::move(newSampleResults);
                    changed = true;
                }
            }

            outResults.push_back(changed ? entryCopy : entry);
        }

        return ModuleIO(images, outResults, Json::array());
    }

private:
    static std::string NormalizeCategoryName(const Json& token) {
        if (!token.is_string()) return std::string();
        std::string s = token.get<std::string>();
        bool hasNonSpace = false;
        for (char c : s) {
            if (!std::isspace(static_cast<unsigned char>(c))) {
                hasNonSpace = true;
                break;
            }
        }
        return hasNonSpace ? s : std::string();
    }

    static std::string ExtractFirstCategoryName(const Json& replaceResults) {
        if (!replaceResults.is_array()) return std::string();

        for (const auto& token : replaceResults) {
            if (!token.is_object()) continue;
            const Json& entry = token;

            if (entry.contains("category_name")) {
                const std::string entryName = NormalizeCategoryName(entry.at("category_name"));
                if (!entryName.empty()) return entryName;
            }

            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) continue;
            for (const auto& detToken : entry.at("sample_results")) {
                if (!detToken.is_object()) continue;
                const Json& det = detToken;
                if (!det.contains("category_name")) continue;
                const std::string detName = NormalizeCategoryName(det.at("category_name"));
                if (!detName.empty()) return detName;
            }
        }

        return std::string();
    }

    static Json OverrideSampleResults(const Json& sampleResults, const std::string& overrideName, bool& changed) {
        changed = false;
        Json out = Json::array();
        if (!sampleResults.is_array()) return out;

        for (const auto& detToken : sampleResults) {
            if (!detToken.is_object()) {
                out.push_back(detToken);
                continue;
            }
            const Json& det = detToken;
            if (det.contains("category_name") && det.at("category_name").is_string()) {
                Json detCopy = det;
                detCopy["category_name"] = overrideName;
                out.push_back(std::move(detCopy));
                changed = true;
            } else {
                out.push_back(det);
            }
        }
        return out;
    }
};

/// post_process/mask_to_rbox, features/mask_to_rbox
class MaskToRBoxModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        Json outResults = resultList.is_array() ? resultList : Json::array();
        return ProcessCore(imageList, std::move(outResults));
    }

    ModuleIO ProcessOwned(const std::vector<ModuleImage>& imageList, Json&& resultList) override {
        Json outResults = resultList.is_array() ? std::move(resultList) : Json::array();
        return ProcessCore(imageList, std::move(outResults));
    }

private:
    ModuleIO ProcessCore(const std::vector<ModuleImage>& imageList, Json outResults) {
        struct MaskDetView final {
            const Json* Det = nullptr;
            const Json* MaskInfo = nullptr;
            float OffsetX = 0.0f;
            float OffsetY = 0.0f;
        };

        const std::vector<ModuleImage>& images = imageList;
        if (!outResults.is_array()) outResults = Json::array();
        auto& outEntries = outResults.get_ref<Json::array_t&>();
        std::unordered_map<const Json*, cv::RotatedRect> rectCache;

        auto NormalizeAngleLe90Rad = [](double aRad) -> double {
            double x = aRad;
            x = std::fmod(x + kPi / 2.0, kPi);
            if (x < 0) x += kPi;
            x -= kPi / 2.0;
            return x;
        };

        for (auto& entryToken : outEntries) {
            if (!entryToken.is_object() || entryToken.value("type", "") != "local") {
                continue;
            }
            Json& entry = entryToken;
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) {
                continue;
            }
            Json newDets = Json::array();
            auto& newDetArr = newDets.get_ref<Json::array_t&>();
            newDetArr.reserve(entry.at("sample_results").size());
            for (const auto& dToken : entry.at("sample_results")) {
                if (!dToken.is_object()) continue;

                MaskDetView detView;
                detView.Det = &dToken;
                const Json& d = *detView.Det;
                if (!d.contains("mask_rle") || !d.at("mask_rle").is_object()) {
                    continue; // 与 C# 对齐：无 mask_rle 直接跳过该条
                }
                if (!d.contains("bbox") || !d.at("bbox").is_array() || d.at("bbox").size() < 4) continue;
                const Json& bbox = d.at("bbox");
                detView.OffsetX = static_cast<float>(bbox.at(0).get<double>());
                detView.OffsetY = static_cast<float>(bbox.at(1).get<double>());
                detView.MaskInfo = &d.at("mask_rle");
                const Json* maskKey = detView.MaskInfo;

                cv::RotatedRect rr;
                auto it = rectCache.find(maskKey);
                if (it != rectCache.end()) {
                    rr = it->second;
                } else {
                    if (!TryComputeMinAreaRectFromMaskInfo(*detView.MaskInfo, rr)) continue;
                    rectCache.emplace(maskKey, rr);
                }
                rr.center += cv::Point2f(detView.OffsetX, detView.OffsetY);

                float rw = rr.size.width;
                float rh = rr.size.height;
                float angDeg = rr.angle;
                if (rw < rh) {
                    std::swap(rw, rh);
                    angDeg += 90.0f;
                }
                double angRad = NormalizeAngleLe90Rad(static_cast<double>(angDeg) * kPi / 180.0);

                Json d2 = Json::object();
                if (d.contains("category_id")) d2["category_id"] = d.at("category_id");
                if (d.contains("category_name")) d2["category_name"] = d.at("category_name");
                if (d.contains("score")) d2["score"] = d.at("score");
                if (d.contains("metadata") && d.at("metadata").is_object()) d2["metadata"] = d.at("metadata");
                d2["bbox"] = Json::array({ rr.center.x, rr.center.y, rw, rh, angRad });
                newDets.push_back(d2);
            }
            entry["sample_results"] = std::move(newDets);
        }

        return ModuleIO(images, std::move(outResults), Json::array());
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

/// post_process/bbox_iou_dedup, features/bbox_iou_dedup
class BBoxIoUDedupModule final : public BaseModule {
public:
    using BaseModule::BaseModule;
private:
    struct Candidate {
        int EntryIndex = -1;
        int DetIndex = -1;
        std::array<double, 4> BBox = {0.0, 0.0, 0.0, 0.0};
        double Area = 0.0;
    };

public:

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        std::string metric = NormalizeMetric(ReadString("metric", "iou"));
        const double threshold = Clamp01(ReadDouble("iou_threshold", 0.5));
        const bool perCategory = ReadBool("per_category", true);

        std::unordered_map<int, std::vector<bool>> keepFlags;
        std::unordered_map<std::string, std::vector<Candidate>> grouped;

        for (int entryIdx = 0; entryIdx < static_cast<int>(results.size()); entryIdx++) {
            const Json& entry = results.at(static_cast<size_t>(entryIdx));
            if (!entry.is_object()) continue;
            if (entry.value("type", "") != "local") continue;
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) continue;

            const Json& dets = entry.at("sample_results");
            keepFlags[entryIdx] = std::vector<bool>(dets.size(), true);

            const int idx = entry.contains("index") ? SafeIntFromJson(entry.at("index"), -1) : -1;
            const int originIdx = entry.contains("origin_index") ? SafeIntFromJson(entry.at("origin_index"), idx) : idx;
            const std::string transformSig = entry.contains("transform") ? SerializeTokenCompact(entry.at("transform")) : "null";
            const std::string entryGroupKey = std::to_string(idx) + "|" + std::to_string(originIdx) + "|" + transformSig;

            for (int detIdx = 0; detIdx < static_cast<int>(dets.size()); detIdx++) {
                const Json& det = dets.at(static_cast<size_t>(detIdx));
                if (!det.is_object()) continue;

                std::array<double, 4> bbox = {0.0, 0.0, 0.0, 0.0};
                if (!TryExtractBboxXyxy(det, bbox)) continue;

                const double area = BBoxArea(bbox);
                if (area <= 0.0) continue;

                std::string groupKey;
                if (perCategory) {
                    const std::string catId = det.contains("category_id") ? SerializeTokenCompact(det.at("category_id")) : "null";
                    const std::string catName = det.contains("category_name") ? SerializeTokenCompact(det.at("category_name")) : "null";
                    groupKey = entryGroupKey + "|" + catId + "|" + catName;
                } else {
                    groupKey = entryGroupKey + "|__all__";
                }

                grouped[groupKey].push_back(Candidate{entryIdx, detIdx, bbox, area});
            }
        }

        int removedCount = 0;
        for (auto& kv : grouped) {
            std::vector<Candidate>& items = kv.second;
            std::sort(items.begin(), items.end(), [](const Candidate& a, const Candidate& b) {
                if (a.Area != b.Area) return a.Area > b.Area;
                if (a.EntryIndex != b.EntryIndex) return a.EntryIndex < b.EntryIndex;
                return a.DetIndex < b.DetIndex;
            });

            std::vector<std::array<double, 4>> keptBoxes;
            for (const auto& item : items) {
                bool shouldDrop = false;
                for (const auto& kept : keptBoxes) {
                    if (IsOverlapExceeded(item.BBox, kept, threshold, metric)) {
                        shouldDrop = true;
                        break;
                    }
                }

                if (shouldDrop) {
                    auto it = keepFlags.find(item.EntryIndex);
                    if (it != keepFlags.end()) {
                        std::vector<bool>& flags = it->second;
                        if (item.DetIndex >= 0 && item.DetIndex < static_cast<int>(flags.size())) {
                            flags[static_cast<size_t>(item.DetIndex)] = false;
                        }
                    }
                    removedCount++;
                    continue;
                }

                keptBoxes.push_back(item.BBox);
            }
        }

        int keptCount = 0;
        Json outResults = Json::array();
        for (int entryIdx = 0; entryIdx < static_cast<int>(results.size()); entryIdx++) {
            const Json& token = results.at(static_cast<size_t>(entryIdx));
            if (!token.is_object() || token.value("type", "") != "local") {
                outResults.push_back(token);
                continue;
            }

            if (!token.contains("sample_results") || !token.at("sample_results").is_array()) {
                outResults.push_back(token);
                continue;
            }

            const Json& dets = token.at("sample_results");
            auto it = keepFlags.find(entryIdx);
            if (it == keepFlags.end()) {
                keptCount += static_cast<int>(dets.size());
                outResults.push_back(token);
                continue;
            }

            const std::vector<bool>& flags = it->second;
            Json newDets = Json::array();
            for (int detIdx = 0; detIdx < static_cast<int>(dets.size()); detIdx++) {
                if (detIdx < static_cast<int>(flags.size()) && flags[static_cast<size_t>(detIdx)]) {
                    newDets.push_back(dets.at(static_cast<size_t>(detIdx)));
                }
            }
            keptCount += static_cast<int>(newDets.size());

            if (newDets.size() == dets.size()) {
                outResults.push_back(token);
                continue;
            }

            Json newEntry = token;
            newEntry["sample_results"] = newDets;
            outResults.push_back(newEntry);
        }

        this->ScalarOutputsByName["kept_count"] = keptCount;
        this->ScalarOutputsByName["removed_count"] = removedCount;
        return ModuleIO(images, outResults, Json::array());
    }

private:
    static bool TryExtractBboxXyxy(const Json& det, std::array<double, 4>& bbox) {
        if (!det.is_object() || !det.contains("bbox") || !det.at("bbox").is_array()) return false;
        const Json& arr = det.at("bbox");
        if (arr.size() != 4) return false;

        double x = 0.0, y = 0.0, w = 0.0, h = 0.0;
        if (!TryReadDouble(arr.at(0), x) || !TryReadDouble(arr.at(1), y) ||
            !TryReadDouble(arr.at(2), w) || !TryReadDouble(arr.at(3), h)) {
            return false;
        }

        double x1 = x;
        double y1 = y;
        double x2 = x + w;
        double y2 = y + h;
        if (x2 < x1) std::swap(x1, x2);
        if (y2 < y1) std::swap(y1, y2);
        if (x2 <= x1 || y2 <= y1) return false;

        bbox = {x1, y1, x2, y2};
        return true;
    }

    static double BBoxArea(const std::array<double, 4>& bbox) {
        return std::max(0.0, bbox[2] - bbox[0]) * std::max(0.0, bbox[3] - bbox[1]);
    }

    static std::string NormalizeMetric(std::string metricRaw) {
        auto trim = [](std::string& s) {
            while (!s.empty() && std::isspace(static_cast<unsigned char>(s.front()))) s.erase(s.begin());
            while (!s.empty() && std::isspace(static_cast<unsigned char>(s.back()))) s.pop_back();
        };
        trim(metricRaw);
        std::transform(metricRaw.begin(), metricRaw.end(), metricRaw.begin(), [](unsigned char c){ return static_cast<char>(std::tolower(c)); });
        if (metricRaw == "ios") return "ios";
        return "iou";
    }

    static double Clamp01(double v) {
        if (std::isnan(v) || std::isinf(v)) return 0.0;
        if (v < 0.0) return 0.0;
        if (v > 1.0) return 1.0;
        return v;
    }

    static bool IsOverlapExceeded(const std::array<double, 4>& a,
                                  const std::array<double, 4>& b,
                                  double threshold,
                                  const std::string& metric) {
        if (metric == "ios") return ComputeIoS(a, b) > threshold;
        return ComputeIoU(a, b) > threshold;
    }

    static double ComputeIoU(const std::array<double, 4>& a, const std::array<double, 4>& b) {
        const double inter = IntersectionArea(a, b);
        if (inter <= 0.0) return 0.0;
        const double areaA = BBoxArea(a);
        const double areaB = BBoxArea(b);
        const double uni = areaA + areaB - inter;
        if (uni <= 0.0) return 0.0;
        return inter / uni;
    }

    static double ComputeIoS(const std::array<double, 4>& a, const std::array<double, 4>& b) {
        const double inter = IntersectionArea(a, b);
        if (inter <= 0.0) return 0.0;
        const double areaA = BBoxArea(a);
        const double areaB = BBoxArea(b);
        const double smaller = std::min(areaA, areaB);
        if (smaller <= 0.0) return 0.0;
        return inter / smaller;
    }

    static double IntersectionArea(const std::array<double, 4>& a, const std::array<double, 4>& b) {
        const double x1 = std::max(a[0], b[0]);
        const double y1 = std::max(a[1], b[1]);
        const double x2 = std::min(a[2], b[2]);
        const double y2 = std::min(a[3], b[3]);
        const double w = std::max(0.0, x2 - x1);
        const double h = std::max(0.0, y2 - y1);
        return w * h;
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
DLCV_FLOW_REGISTER_MODULE("post_process/result_category_override", ResultCategoryOverrideModule)
DLCV_FLOW_REGISTER_MODULE("features/result_category_override", ResultCategoryOverrideModule)
DLCV_FLOW_REGISTER_MODULE("post_process/mask_to_rbox", MaskToRBoxModule)
DLCV_FLOW_REGISTER_MODULE("features/mask_to_rbox", MaskToRBoxModule)
DLCV_FLOW_REGISTER_MODULE("post_process/rbox_correction", RBoxCorrectionModule)
DLCV_FLOW_REGISTER_MODULE("features/rbox_correction", RBoxCorrectionModule)
DLCV_FLOW_REGISTER_MODULE("post_process/bbox_iou_dedup", BBoxIoUDedupModule)
DLCV_FLOW_REGISTER_MODULE("features/bbox_iou_dedup", BBoxIoUDedupModule)

} // namespace flow
} // namespace dlcv_infer

