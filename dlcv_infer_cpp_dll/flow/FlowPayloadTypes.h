#pragma once

#include <climits>
#include <string>
#include <vector>

#include "flow/FlowTypes.h"

namespace dlcv_infer {
namespace flow {

struct FlowResultItem final {
    int CategoryId = 0;
    std::string CategoryName;
    double Score = 0.0;
    Json Bbox = Json::array();
    Json Metadata = Json::object();
    Json MaskRle = Json::object();
    Json Poly = Json::array();
    Json Extra = Json::object();

    Json ToJson() const {
        Json out = Json::object();
        out["category_id"] = CategoryId;
        out["category_name"] = CategoryName;
        out["score"] = Score;
        if (Bbox.is_array()) out["bbox"] = Bbox;
        if (Metadata.is_object() && !Metadata.empty()) out["metadata"] = Metadata;
        if (MaskRle.is_object() && !MaskRle.empty()) out["mask_rle"] = MaskRle;
        if (Poly.is_array() && !Poly.empty()) out["poly"] = Poly;
        if (Extra.is_object()) {
            for (auto it = Extra.begin(); it != Extra.end(); ++it) {
                if (out.contains(it.key())) continue;
                out[it.key()] = it.value();
            }
        }
        return out;
    }

    static FlowResultItem FromJson(const Json& src) {
        FlowResultItem out;
        if (!src.is_object()) return out;
        try { out.CategoryId = src.value("category_id", 0); } catch (...) { out.CategoryId = 0; }
        try { out.CategoryName = src.value("category_name", std::string()); } catch (...) { out.CategoryName.clear(); }
        try { out.Score = src.value("score", 0.0); } catch (...) { out.Score = 0.0; }
        try {
            if (src.contains("bbox") && src.at("bbox").is_array()) {
                out.Bbox = src.at("bbox");
            }
        } catch (...) {}
        try {
            if (src.contains("metadata") && src.at("metadata").is_object()) {
                out.Metadata = src.at("metadata");
            }
        } catch (...) {}
        try {
            if (src.contains("mask_rle") && src.at("mask_rle").is_object()) {
                out.MaskRle = src.at("mask_rle");
            }
        } catch (...) {}
        try {
            if (src.contains("poly") && src.at("poly").is_array()) {
                out.Poly = src.at("poly");
            }
        } catch (...) {}

        out.Extra = Json::object();
        for (auto it = src.begin(); it != src.end(); ++it) {
            const std::string& k = it.key();
            if (k == "category_id" || k == "category_name" || k == "score" ||
                k == "bbox" || k == "metadata" || k == "mask_rle" || k == "poly") {
                continue;
            }
            out.Extra[k] = it.value();
        }
        return out;
    }
};

inline Json FlowResultItemsToJsonArray(const std::vector<FlowResultItem>& items) {
    Json arr = Json::array();
    for (const auto& item : items) {
        arr.push_back(item.ToJson());
    }
    return arr;
}

struct FlowByImageEntry final {
    int OriginIndex = -1;
    int OriginalWidth = 0;
    int OriginalHeight = 0;
    std::vector<FlowResultItem> Results;

    Json ToJson() const {
        Json out = Json::object();
        out["origin_index"] = OriginIndex;
        out["original_size"] = Json::array({ OriginalWidth, OriginalHeight });
        out["results"] = FlowResultItemsToJsonArray(Results);
        return out;
    }

    static FlowByImageEntry FromJson(const Json& src) {
        FlowByImageEntry out;
        if (!src.is_object()) return out;
        try { out.OriginIndex = src.value("origin_index", -1); } catch (...) { out.OriginIndex = -1; }
        try {
            if (src.contains("original_size") && src.at("original_size").is_array() && src.at("original_size").size() >= 2) {
                out.OriginalWidth = src.at("original_size").at(0).get<int>();
                out.OriginalHeight = src.at("original_size").at(1).get<int>();
            }
        } catch (...) {
            out.OriginalWidth = 0;
            out.OriginalHeight = 0;
        }
        try {
            if (src.contains("results") && src.at("results").is_array()) {
                for (const auto& one : src.at("results")) {
                    out.Results.push_back(FlowResultItem::FromJson(one));
                }
            }
        } catch (...) {}
        return out;
    }
};

struct FlowFrontendPayload final {
    std::vector<FlowByImageEntry> ByImage;

    Json ToJson() const {
        Json byImage = Json::array();
        for (const auto& entry : ByImage) {
            byImage.push_back(entry.ToJson());
        }
        return Json::object({ {"by_image", byImage} });
    }

    static FlowFrontendPayload FromJson(const Json& src) {
        FlowFrontendPayload out;
        if (!src.is_object()) return out;
        try {
            if (src.contains("by_image") && src.at("by_image").is_array()) {
                for (const auto& item : src.at("by_image")) {
                    out.ByImage.push_back(FlowByImageEntry::FromJson(item));
                }
            }
        } catch (...) {}
        return out;
    }
};

struct FlowFrontendByNodePayload final {
    int NodeOrder = INT_MAX;
    int FallbackOrder = 0;
    FlowFrontendPayload Payload;
};

struct FlowBatchResult final {
    std::vector<std::vector<FlowResultItem>> PerImageResults;

    Json ToFlowRootJson() const {
        Json out = Json::object();
        if (PerImageResults.size() <= 1) {
            if (PerImageResults.empty()) {
                out["result_list"] = Json::array();
            } else {
                out["result_list"] = FlowResultItemsToJsonArray(PerImageResults[0]);
            }
            return out;
        }

        Json batch = Json::array();
        for (const auto& one : PerImageResults) {
            batch.push_back(Json::object({ {"result_list", FlowResultItemsToJsonArray(one)} }));
        }
        out["result_list"] = batch;
        return out;
    }
};

} // namespace flow
} // namespace dlcv_infer

