#pragma once

#include "flow/FlowTypes.h"
#include <cmath>
#include <stdexcept>

namespace dlcv_infer {
namespace flow {

inline bool HasResultPolygon(const Json& detection) {
    return detection.contains("polygon") && !detection.at("polygon").is_null();
}

inline bool HasResultMask(const Json& detection) {
    return (detection.contains("mask_array") && !detection.at("mask_array").is_null()) ||
           (detection.contains("mask_rle") && !detection.at("mask_rle").is_null());
}

inline std::vector<double> PolygonToOriginalTransform(const Json& transform) {
    std::vector<double> affine{1,0,0,0,1,0};
    if (transform.is_null()) return affine;
    if (!transform.is_object()) throw std::invalid_argument("无效的多边形坐标变换");
    if (transform.contains("affine_matrix") && !transform.at("affine_matrix").is_null()) {
        const auto& rows = transform.at("affine_matrix");
        for (int i = 0; i < 6; ++i) affine[i] = rows.at(i / 3).at(i % 3).get<double>();
        const auto& crop = transform.at("crop_box");
        const double x = crop.at(0).get<double>(), y = crop.at(1).get<double>();
        affine[2] -= affine[0] * x + affine[1] * y;
        affine[5] -= affine[3] * x + affine[4] * y;
    } else if (transform.contains("affine_2x3")) {
        affine = transform.at("affine_2x3").get<std::vector<double>>();
    } else return affine;
    if (affine.size() != 6 || std::any_of(affine.begin(), affine.end(), [](double value) { return !std::isfinite(value); }))
        throw std::invalid_argument("无效的多边形坐标变换");
    const double determinant = affine[0] * affine[4] - affine[1] * affine[3];
    if (!std::isfinite(determinant) || std::abs(determinant) < 1e-12)
        throw std::invalid_argument("无效的多边形坐标变换");
    return TransformationState::Inverse2x3(affine);
}

inline std::vector<cv::Point2d> ReadResultPolygon(const Json& detection) {
    if (!HasResultPolygon(detection)) return {};
    const auto& ring = detection.at("polygon");
    if (!ring.is_array() || ring.size() < 3) throw std::invalid_argument("polygon 至少需要三个点");
    std::vector<cv::Point2d> points;
    points.reserve(ring.size());
    for (const auto& point : ring) {
        if (!point.is_array() || point.size() != 2 || !point[0].is_number() || !point[1].is_number())
            throw std::invalid_argument("polygon 的每个点须包含两个坐标");
        const double x = point[0].get<double>(), y = point[1].get<double>();
        if (!std::isfinite(x) || !std::isfinite(y) || std::abs(x) > 1e9 || std::abs(y) > 1e9)
            throw std::invalid_argument("polygon 坐标须在 -1000000000 至 1000000000 之间");
        points.emplace_back(x, y);
    }
    return points;
}

inline Json MapResultPolygon(const std::vector<cv::Point2d>& points, const std::vector<double>& transform) {
    Json ring = Json::array();
    for (const auto& point : points) {
        ring.push_back({transform[0] * point.x + transform[1] * point.y + transform[2],
                        transform[3] * point.x + transform[4] * point.y + transform[5]});
    }
    return ring;
}

} // namespace flow
} // namespace dlcv_infer
