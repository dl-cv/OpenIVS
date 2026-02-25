#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
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
                    cv::Mat cropped = mat(rect).clone();

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

        // 将输入中 affine_2x3 为空的视为“原图”，建立 origin_index->image 映射
        std::unordered_map<int, ModuleImage> originIndexToImage;
        for (size_t i = 0; i < inImages.size(); i++) {
            const ModuleImage& wrap = inImages[i];
            if (wrap.ImageObject.empty()) continue;
            const int originIndex = wrap.OriginalIndex;
            if (wrap.TransformState.AffineMatrix2x3.empty()) {
                originIndexToImage[originIndex] = wrap;
            }
        }

        // 收集每个 origin_index 的所有局部结果（简单拼接）
        std::unordered_map<int, std::vector<Json>> originIndexToSamples;
        for (const auto& token : inResults) {
            if (!token.is_object()) continue;
            const Json& entry = token;
            int originIndex = 0;
            try {
                if (entry.contains("origin_index")) originIndex = entry.at("origin_index").get<int>();
                else if (entry.contains("index")) originIndex = entry.at("index").get<int>();
            } catch (...) { originIndex = 0; }
            if (!entry.contains("sample_results") || !entry.at("sample_results").is_array()) continue;
            auto& vec = originIndexToSamples[originIndex];
            for (const auto& o : entry.at("sample_results")) {
                if (o.is_object()) vec.push_back(o);
            }
        }

        std::vector<ModuleImage> outImages;
        Json outResults = Json::array();
        int outIdx = 0;
        for (const auto& kv : originIndexToImage) {
            const int originIndex = kv.first;
            outImages.push_back(kv.second);

            Json samples = Json::array();
            auto it = originIndexToSamples.find(originIndex);
            if (it != originIndexToSamples.end()) {
                for (const auto& s : it->second) samples.push_back(s);
            }

            Json mergedEntry = Json::object();
            mergedEntry["type"] = "local";
            mergedEntry["index"] = outIdx;
            mergedEntry["origin_index"] = originIndex;
            mergedEntry["transform"] = nullptr;
            mergedEntry["sample_results"] = samples;
            outResults.push_back(mergedEntry);
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

