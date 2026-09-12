#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <utility>
#include <vector>

#include "json/json.hpp"
#include "opencv2/imgproc.hpp"

namespace dlcv_infer {
namespace mask_utils {

// 保留普通模型的 bbox 取整规则；尺寸不变时共享只读数据。
inline cv::Mat ResizeMaskToBboxGrid(const cv::Mat& mask, const std::vector<double>& bbox) {
    if (mask.empty()) return cv::Mat();

    if (bbox.size() < 4) return mask;

    const int bboxWidth = std::max(0, static_cast<int>(std::llround(std::abs(bbox[2]))));
    const int bboxHeight = std::max(0, static_cast<int>(std::llround(std::abs(bbox[3]))));
    if (bboxWidth <= 0 || bboxHeight <= 0 ||
        (mask.cols == bboxWidth && mask.rows == bboxHeight)) {
        return mask;
    }

    cv::Mat output;
    cv::resize(mask, output, cv::Size(bboxWidth, bboxHeight), 0, 0, cv::INTER_NEAREST);
    return output;
}

// 在 SDK 结果释放前，将指针掩码转换为轮廓，并按同一栅格更新面积。
inline void ConvertPointerMaskToContour(nlohmann::json& result) {
    using json = nlohmann::json;
    if (!result["with_mask"].get<bool>()) return;

    const std::vector<double> bbox = result["bbox"].get<std::vector<double>>();
    const auto& mask = result["mask"];
    const int maskWidth = mask["width"].get<int>();
    const int maskHeight = mask["height"].get<int>();
    cv::Mat maskImage;
    if (maskWidth > 0 && maskHeight > 0 && mask["mask_ptr"].get<uint64_t>() != 0) {
        void* maskPtr = reinterpret_cast<void*>(static_cast<uintptr_t>(mask["mask_ptr"].get<uint64_t>()));
        maskImage = cv::Mat(maskHeight, maskWidth, CV_8UC1, maskPtr);
    }

    const cv::Mat effectiveMask = ResizeMaskToBboxGrid(maskImage, bbox);
    if (!effectiveMask.empty()) {
        result["area"] = cv::countNonZero(effectiveMask);
    }

    json pointsJson = json::array();
    if (!effectiveMask.empty() && bbox.size() >= 4) {
        std::vector<std::vector<cv::Point>> contours;
        cv::findContours(effectiveMask, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);
        if (!contours.empty()) {
            for (const auto& point : contours[0]) {
                pointsJson.push_back(json{
                    {"x", static_cast<int>(point.x + bbox[0])},
                    {"y", static_cast<int>(point.y + bbox[1])}
                });
            }
        }
    }
    result["mask"] = std::move(pointsJson);
}

} // namespace mask_utils
} // namespace dlcv_infer
