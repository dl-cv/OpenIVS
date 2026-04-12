#include "flow/BaseModule.h"
#include "flow/FlowPayloadTypes.h"
#include "flow/ModuleRegistry.h"
#include "flow/utils/MaskRleUtils.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <sstream>
#include <string>
#include <unordered_map>
#include <vector>

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>

#include "opencv2/imgcodecs.hpp"
#include "opencv2/imgproc.hpp"

namespace dlcv_infer {
namespace flow {

static std::string NowTimestamp() {
    using namespace std::chrono;
    const auto now = system_clock::now();
    const auto t = system_clock::to_time_t(now);
    std::tm tm{};
    localtime_s(&tm, &t);
    char buf[64] = {0};
    std::snprintf(buf, sizeof(buf), "%04d%02d%02d_%02d%02d%02d",
                  tm.tm_year + 1900, tm.tm_mon + 1, tm.tm_mday,
                  tm.tm_hour, tm.tm_min, tm.tm_sec);
    return std::string(buf);
}

static void EnsureDirExists(const std::string& dir) {
    if (dir.empty()) return;
    // 递归创建：支持 a\\b\\c
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

static std::string GetFileNameWithoutExt(const std::string& path) {
    size_t pos = path.find_last_of("\\/");
    std::string name = (pos == std::string::npos) ? path : path.substr(pos + 1);
    size_t dot = name.find_last_of('.');
    if (dot == std::string::npos) return name;
    return name.substr(0, dot);
}

static std::string SerializeTransformKeyFromAffine2x3(const std::vector<double>& a) {
    if (a.size() < 6) return std::string();
    char buf[256] = {0};
    std::snprintf(buf, sizeof(buf), "T:%.4f,%.4f,%.2f,%.4f,%.4f,%.2f", a[0], a[1], a[2], a[3], a[4], a[5]);
    return std::string(buf);
}

static std::string SerializeTransformKey(const TransformationState& st) {
    if (st.AffineMatrix2x3.size() < 6) return std::string();
    return SerializeTransformKeyFromAffine2x3(st.AffineMatrix2x3);
}

static std::vector<double> BuildTC2O(const TransformationState& st) {
    // T_c2o = Inverse(AffineMatrix2x3), where AffineMatrix2x3 is Original -> Current
    if (st.AffineMatrix2x3.size() != 6) return {1,0,0, 0,1,0};
    return TransformationState::Inverse2x3(st.AffineMatrix2x3);
}

static std::vector<cv::Point2f> TransformPoints2x3(const std::vector<double>& T, const std::vector<cv::Point2f>& pts) {
    std::vector<cv::Point2f> out;
    out.reserve(pts.size());
    const double a = (T.size() >= 6) ? T[0] : 1.0;
    const double b = (T.size() >= 6) ? T[1] : 0.0;
    const double tx = (T.size() >= 6) ? T[2] : 0.0;
    const double c = (T.size() >= 6) ? T[3] : 0.0;
    const double d = (T.size() >= 6) ? T[4] : 1.0;
    const double ty = (T.size() >= 6) ? T[5] : 0.0;
    for (const auto& p : pts) {
        const double x = p.x;
        const double y = p.y;
        const double nx = a * x + b * y + tx;
        const double ny = c * x + d * y + ty;
        out.emplace_back(static_cast<float>(nx), static_cast<float>(ny));
    }
    return out;
}

static Json AABBFromPoly(const std::vector<cv::Point2f>& poly) {
    if (poly.empty()) return Json::array({0,0,0,0});
    float minx = poly[0].x, miny = poly[0].y, maxx = poly[0].x, maxy = poly[0].y;
    for (const auto& p : poly) {
        minx = std::min(minx, p.x);
        miny = std::min(miny, p.y);
        maxx = std::max(maxx, p.x);
        maxy = std::max(maxy, p.y);
    }
    const int x1 = static_cast<int>(std::floor(minx));
    const int y1 = static_cast<int>(std::floor(miny));
    const int x2 = static_cast<int>(std::ceil(maxx));
    const int y2 = static_cast<int>(std::ceil(maxy));
    return Json::array({ x1, y1, x2, y2 });
}

static Json RBoxLocalToGlobal(const Json& rbox, const std::vector<double>& T) {
    // rbox: [cx, cy, w, h, angle(rad)]
    if (!rbox.is_array() || rbox.size() < 5) return Json();
    const double cx = rbox[0].get<double>();
    const double cy = rbox[1].get<double>();
    const double w = rbox[2].get<double>();
    const double h = rbox[3].get<double>();
    const double ang = rbox[4].get<double>();

    const double l00 = (T.size() >= 6) ? T[0] : 1.0;
    const double l01 = (T.size() >= 6) ? T[1] : 0.0;
    const double l10 = (T.size() >= 6) ? T[3] : 0.0;
    const double l11 = (T.size() >= 6) ? T[4] : 1.0;

    const double ncx = l00 * cx + l01 * cy + ((T.size() >= 6) ? T[2] : 0.0);
    const double ncy = l10 * cx + l11 * cy + ((T.size() >= 6) ? T[5] : 0.0);

    const double c = std::cos(ang);
    const double s = std::sin(ang);
    // unit vectors
    const double ux = c, uy = s;
    const double vx = -s, vy = c;
    // transformed axes
    const double tux_x = l00 * ux + l01 * uy;
    const double tux_y = l10 * ux + l11 * uy;
    const double tvx_x = l00 * vx + l01 * vy;
    const double tvx_y = l10 * vx + l11 * vy;
    const double scale_w = std::sqrt(tux_x * tux_x + tux_y * tux_y);
    const double scale_h = std::sqrt(tvx_x * tvx_x + tvx_y * tvx_y);
    const double nw = w * scale_w;
    const double nh = h * scale_h;
    const double nang = std::atan2(tux_y, tux_x);

    return Json::array({ ncx, ncy, nw, nh, nang });
}

/// output/save_image
class SaveImageModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        std::string saveDir = ReadString("save_path", std::string());
        std::string suffix = ReadString("suffix", std::string("_out"));
        std::string fmt = ReadString("format", std::string("png"));
        if (!saveDir.empty()) {
            try { EnsureDirExists(saveDir); } catch (...) {}
        }

        for (size_t i = 0; i < images.size(); i++) {
            const cv::Mat& mat = images[i].ImageObject;
            if (mat.empty()) continue;

            std::string baseName;
            try {
                if (i < results.size() && results.at(i).is_object()) {
                    const Json& r = results.at(i);
                    if (r.contains("filename") && r.at("filename").is_string()) {
                        baseName = GetFileNameWithoutExt(r.at("filename").get<std::string>());
                    }
                }
            } catch (...) {}
            if (baseName.empty()) baseName = NowTimestamp();

            const std::string fileName = baseName + suffix + "." + (fmt.empty() ? "png" : fmt);
            if (!saveDir.empty()) {
                const std::string full = JoinPath(saveDir, fileName);
                try {
                    const int ch = mat.channels();
                    if (ch == 4) {
                        cv::Mat bgr;
                        cv::cvtColor(mat, bgr, cv::COLOR_BGRA2BGR);
                        cv::imwrite(full, bgr);
                    } else if (ch == 1) {
                        cv::Mat bgr;
                        cv::cvtColor(mat, bgr, cv::COLOR_GRAY2BGR);
                        cv::imwrite(full, bgr);
                    } else {
                        cv::imwrite(full, mat);
                    }
                } catch (...) {
                }
            }
        }

        return ModuleIO(images, results, Json::array());
    }
};

/// output/preview
class PreviewModule final : public BaseModule {
public:
    using BaseModule::BaseModule;
    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        return ModuleIO(imageList, resultList.is_array() ? resultList : Json::array(), Json::array());
    }
};

/// output/return_json
static bool CanUseAlignedFastPath(const std::vector<ModuleImage>& images, const Json& results) {
    if (!results.is_array()) return false;
    if (images.empty() || images.size() != results.size()) return false;

    for (size_t i = 0; i < results.size(); i++) {
        const auto& entry = results.at(i);
        if (!entry.is_object()) return false;
        if (!entry.contains("type") || !entry.at("type").is_string()) return false;
        if (entry.at("type").get<std::string>() != "local") return false;
        int idx = static_cast<int>(i);
        try {
            if (entry.contains("index")) idx = entry.at("index").get<int>();
        } catch (...) {}
        if (idx != static_cast<int>(i)) return false;
    }
    return true;
}

static bool ReadBoolToken(const Json& token, bool defaultValue) {
    try {
        if (token.is_boolean()) return token.get<bool>();
        if (token.is_number_integer()) return token.get<int>() != 0;
        if (token.is_number()) return std::abs(token.get<double>()) > 0.0;
        if (token.is_string()) {
            const std::string s = token.get<std::string>();
            if (s == "1" || s == "true" || s == "TRUE" || s == "True") return true;
            if (s == "0" || s == "false" || s == "FALSE" || s == "False") return false;
        }
    } catch (...) {}
    return defaultValue;
}

static Json AABBFromLocalBboxFast(const Json& bboxLocal, const std::vector<double>& T_c2o) {
    if (!bboxLocal.is_array() || bboxLocal.size() < 4 || T_c2o.size() < 6) return Json();
    try {
        const double bx = bboxLocal[0].get<double>();
        const double by = bboxLocal[1].get<double>();
        const double bw = bboxLocal[2].get<double>();
        const double bh = bboxLocal[3].get<double>();

        const double x1 = bx;
        const double y1 = by;
        const double x2 = bx + bw;
        const double y2 = by + bh;

        const auto tx = [&T_c2o](double x, double y) {
            const double gx = T_c2o[0] * x + T_c2o[1] * y + T_c2o[2];
            const double gy = T_c2o[3] * x + T_c2o[4] * y + T_c2o[5];
            return std::pair<double, double>(gx, gy);
        };

        auto p0 = tx(x1, y1);
        auto p1 = tx(x2, y1);
        auto p2 = tx(x2, y2);
        auto p3 = tx(x1, y2);

        const double minX = std::min(std::min(p0.first, p1.first), std::min(p2.first, p3.first));
        const double minY = std::min(std::min(p0.second, p1.second), std::min(p2.second, p3.second));
        const double maxX = std::max(std::max(p0.first, p1.first), std::max(p2.first, p3.first));
        const double maxY = std::max(std::max(p0.second, p1.second), std::max(p2.second, p3.second));
        return Json::array({ minX, minY, maxX, maxY });
    } catch (...) {
        return Json();
    }
}

static void AppendOutResultItem(const Json& d, const std::vector<double>& T_c2o, std::vector<FlowResultItem>& outResults) {
    if (!d.is_object()) return;

    FlowResultItem item;
    item.CategoryId = d.value("category_id", 0);
    item.CategoryName = d.value("category_name", "");
    item.Score = d.value("score", 0.0);

    // bbox
    if (d.contains("bbox") && d.at("bbox").is_array()) {
        const Json& bboxLocal = d.at("bbox");
        const bool isRot = (bboxLocal.size() == 5);
        if (isRot) {
            Json rboxG = RBoxLocalToGlobal(bboxLocal, T_c2o);
            if (!rboxG.is_null()) {
                item.Bbox = std::move(rboxG);
                item.Metadata = Json::object({ {"is_rotated", true} });
            }
            } else if (bboxLocal.size() >= 4) {
                Json bboxGlobal = AABBFromLocalBboxFast(bboxLocal, T_c2o);
                if (!bboxGlobal.is_null()) {
                    item.Bbox = std::move(bboxGlobal);
                    item.Metadata = Json::object({ {"is_rotated", false} });
                }
        }
    }

    // mask_rle 透传即可。下游 ConvertFlowResultListToObjects / BuildMaskFromFlowEntry 优先使用 mask_rle。
    // 注意：不要再用 findNonZero 把每个前景像素写成 poly——大图语义分割会产生千万级点，
    // JSON 序列化与内存会拖垮 return_json（用户可见 4s+ 仅耗在本节点）。
    if (d.contains("mask_rle") && d.at("mask_rle").is_object()) {
        item.MaskRle = d.at("mask_rle");
    }

    outResults.push_back(std::move(item));
}

static std::vector<FlowResultItem> BuildOutResultItems(const ModuleImage& wrap, const Json& dets) {
    std::vector<FlowResultItem> outResults;
    if (!dets.is_array() || dets.empty()) return outResults;

    const std::vector<double> T_c2o = BuildTC2O(wrap.TransformState);
    outResults.reserve(dets.size());
    for (const auto& d : dets) {
        AppendOutResultItem(d, T_c2o, outResults);
    }
    return outResults;
}

static std::vector<FlowResultItem> BuildOutResultItems(const ModuleImage& wrap, const std::vector<const Json*>& dets) {
    std::vector<FlowResultItem> outResults;
    if (dets.empty()) return outResults;

    const std::vector<double> T_c2o = BuildTC2O(wrap.TransformState);
    outResults.reserve(dets.size());
    for (const Json* d : dets) {
        if (d == nullptr) continue;
        AppendOutResultItem(*d, T_c2o, outResults);
    }
    return outResults;
}

static FlowByImageEntry BuildImagePayloadEntry(const ModuleImage& wrap, std::vector<FlowResultItem> outResults) {
    const cv::Mat& ori = wrap.OriginalImage.empty() ? wrap.ImageObject : wrap.OriginalImage;
    FlowByImageEntry entry;
    entry.OriginIndex = wrap.OriginalIndex;
    entry.OriginalWidth = ori.empty() ? 0 : ori.cols;
    entry.OriginalHeight = ori.empty() ? 0 : ori.rows;
    entry.Results = std::move(outResults);
    return entry;
}

class ReturnJsonModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const std::vector<ModuleImage>& images = imageList;
        const Json results = resultList.is_array() ? resultList : Json::array();

        std::vector<FlowByImageEntry> byImageEntries;
        byImageEntries.reserve(images.size());

        if (CanUseAlignedFastPath(images, results)) {
            for (size_t i = 0; i < images.size(); i++) {
                const ModuleImage& wrap = images[i];
                try {
                    const auto& entry = results.at(i);
                    if (entry.is_object() && entry.contains("sample_results") && entry.at("sample_results").is_array()) {
                        byImageEntries.push_back(
                            BuildImagePayloadEntry(wrap, BuildOutResultItems(wrap, entry.at("sample_results"))));
                        continue;
                    }
                } catch (...) {}
                byImageEntries.push_back(BuildImagePayloadEntry(wrap, std::vector<FlowResultItem>()));
            }
        } else {
            // 建立 index/origin_index/transform -> dets 映射
            std::unordered_map<std::string, std::vector<const Json*>> transToDets;
            std::unordered_map<int, std::vector<const Json*>> indexToDets;
            std::unordered_map<int, std::vector<const Json*>> originToDets;

            for (const auto& entryToken : results) {
                if (!entryToken.is_object()) continue;
                const Json& entry = entryToken;
                if (!entry.contains("type") || !entry.at("type").is_string()) continue;
                if (entry.at("type").get<std::string>() != "local") continue;
                if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) continue;

                std::vector<const Json*> detList;
                detList.reserve(entry.at("sample_results").size());
                for (const auto& d : entry.at("sample_results")) {
                    if (d.is_object()) detList.push_back(&d);
                }
                if (detList.empty()) continue;

                const int idx = entry.contains("index") ? entry.at("index").get<int>() : -1;
                if (idx >= 0) {
                    auto& v = indexToDets[idx];
                    v.insert(v.end(), detList.begin(), detList.end());
                }
                const int oidx = entry.contains("origin_index") ? entry.at("origin_index").get<int>() : -1;
                if (oidx >= 0) {
                    auto& v = originToDets[oidx];
                    v.insert(v.end(), detList.begin(), detList.end());
                }

                // transform 兜底兼容
                try {
                    if (entry.contains("transform") && entry.at("transform").is_object()) {
                        TransformationState st = TransformationState::FromJson(entry.at("transform"));
                        const std::string sig = SerializeTransformKey(st);
                        if (!sig.empty()) {
                            auto& v = transToDets[sig];
                            v.insert(v.end(), detList.begin(), detList.end());
                        }
                    }
                } catch (...) {}
            }

            // 遍历图像，还原坐标
            for (size_t i = 0; i < images.size(); i++) {
                const ModuleImage& wrap = images[i];
                const std::string sig = SerializeTransformKey(wrap.TransformState);

                const std::vector<const Json*>* dets = nullptr;
                auto itIdx = indexToDets.find(static_cast<int>(i));
                if (itIdx != indexToDets.end()) dets = &itIdx->second;
                if (dets == nullptr) {
                    auto itOrg = originToDets.find(wrap.OriginalIndex);
                    if (itOrg != originToDets.end()) dets = &itOrg->second;
                }
                if (dets == nullptr && !sig.empty()) {
                    auto itT = transToDets.find(sig);
                    if (itT != transToDets.end()) dets = &itT->second;
                }

                std::vector<FlowResultItem> outItems;
                if (dets != nullptr) outItems = BuildOutResultItems(wrap, *dets);
                byImageEntries.push_back(BuildImagePayloadEntry(wrap, std::move(outItems)));
            }
        }

        FlowFrontendPayload payloadTyped;
        payloadTyped.ByImage = std::move(byImageEntries);

        // 写入 Context
        if (Context != nullptr) {
            try {
                std::vector<FlowFrontendByNodePayload> typedByNode = Context->Get<std::vector<FlowFrontendByNodePayload>>(
                    "frontend_payloads_by_node", std::vector<FlowFrontendByNodePayload>());
                typedByNode.erase(
                    std::remove_if(
                        typedByNode.begin(),
                        typedByNode.end(),
                        [this](const FlowFrontendByNodePayload& one) { return one.NodeOrder == this->NodeId; }),
                    typedByNode.end());

                FlowFrontendByNodePayload typedNodePayload;
                typedNodePayload.NodeOrder = NodeId;
                typedNodePayload.FallbackOrder = static_cast<int>(typedByNode.size());
                typedNodePayload.Payload = payloadTyped;
                typedByNode.push_back(std::move(typedNodePayload));

                Context->Set<std::vector<FlowFrontendByNodePayload>>("frontend_payloads_by_node", typedByNode);
                Context->Set<FlowFrontendPayload>("frontend_payload_last", payloadTyped);
            } catch (...) {}

            bool writeLegacyJson = false;
            try {
                writeLegacyJson = Context->Get<bool>("return_json_write_legacy_json", false);
            } catch (...) {}
            if (Properties.is_object() && Properties.contains("write_legacy_json")) {
                writeLegacyJson = ReadBoolToken(Properties.at("write_legacy_json"), writeLegacyJson);
            }

            if (writeLegacyJson) {
                Json payload = payloadTyped.ToJson();
                Json byNode = Context->Get<Json>("frontend_json_by_node", Json::object());
                if (!byNode.is_object()) byNode = Json::object();
                byNode[std::to_string(NodeId)] = payload;

                Json existing = Json::object();
                existing["last"] = payload;
                existing["by_node"] = byNode;
                Context->Set<Json>("frontend_json_by_node", byNode);
                Context->Set<Json>("frontend_json", existing);
            }
        }

        return ModuleIO(images, results, Json::array());
    }
};

// 注册
DLCV_FLOW_REGISTER_MODULE("output/save_image", SaveImageModule)
DLCV_FLOW_REGISTER_MODULE("output/preview", PreviewModule)
DLCV_FLOW_REGISTER_MODULE("output/return_json", ReturnJsonModule)

} // namespace flow
} // namespace dlcv_infer

