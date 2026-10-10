#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include "flow/utils/MaskRleUtils.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <limits>
#include <string>
#include <utility>
#include <stdexcept>
#include <vector>

#include <opencv2/imgproc.hpp>

namespace dlcv_infer {
namespace flow {
namespace {

struct StatisticsTransform {
    int originalWidth = 0;
    int originalHeight = 0;
    int width = 0;
    int height = 0;
    cv::Matx33d matrix = cv::Matx33d::eye();
};

int StatisticsInteger(const Json& value, const char* name, bool positive) {
    if (!value.is_number_integer()) throw std::invalid_argument(std::string(name) + " 必须为整数");
    const double number = value.get<double>();
    if (number < (positive ? 1 : 0) || number > std::numeric_limits<int>::max()) {
        throw std::invalid_argument(std::string(name) + " 超出有效范围");
    }
    return static_cast<int>(number);
}

StatisticsTransform StatisticsState(const TransformationState& state) {
    StatisticsTransform out;
    out.originalWidth = state.OriginalWidth;
    out.originalHeight = state.OriginalHeight;
    out.width = out.originalWidth;
    out.height = out.originalHeight;
    if (!state.OutputSize.empty()) {
        if (state.OutputSize.size() != 2) throw std::invalid_argument("transform.output_size 必须包含宽高");
        out.width = state.OutputSize[0];
        out.height = state.OutputSize[1];
    }
    if (out.originalWidth <= 0 || out.originalHeight <= 0 || out.width <= 0 || out.height <= 0) {
        throw std::invalid_argument("transform 图像尺寸必须为正数");
    }
    if (!state.AffineMatrix2x3.empty()) {
        if (state.AffineMatrix2x3.size() != 6) throw std::invalid_argument("transform.affine_2x3 必须包含六个数值");
        for (int i = 0; i < 6; ++i) {
            const double value = state.AffineMatrix2x3[static_cast<size_t>(i)];
            if (!std::isfinite(value)) throw std::invalid_argument("transform.affine_2x3 包含非有限数值");
            out.matrix(i / 3, i % 3) = value;
        }
    }
    const double determinant = out.matrix(0, 0) * out.matrix(1, 1) - out.matrix(0, 1) * out.matrix(1, 0);
    if (!std::isfinite(determinant) || std::abs(determinant) < 1e-12) {
        throw std::invalid_argument("transform.affine_2x3 不可逆");
    }
    return out;
}

StatisticsTransform StatisticsSource(const Json& transform) {
    if (!transform.is_object() || !transform.contains("original_width") || !transform.contains("original_height")) {
        throw std::invalid_argument("结果 transform 缺少原图尺寸");
    }
    TransformationState state;
    state.OriginalWidth = StatisticsInteger(transform.at("original_width"), "transform.original_width", true);
    state.OriginalHeight = StatisticsInteger(transform.at("original_height"), "transform.original_height", true);
    if (transform.contains("output_size")) {
        const auto& size = transform.at("output_size");
        if (!size.is_array() || size.size() != 2) throw std::invalid_argument("transform.output_size 必须包含宽高");
        state.OutputSize = {StatisticsInteger(size[0], "transform.output_size", true),
            StatisticsInteger(size[1], "transform.output_size", true)};
    }
    if (transform.contains("affine_2x3")) {
        const auto& affine = transform.at("affine_2x3");
        if (!affine.is_array() || affine.size() != 6) throw std::invalid_argument("transform.affine_2x3 必须包含六个数值");
        for (const auto& value : affine) {
            if (!value.is_number()) throw std::invalid_argument("transform.affine_2x3 必须为数值数组");
            state.AffineMatrix2x3.push_back(value.get<double>());
        }
    }
    return StatisticsState(state);
}

const ModuleImage& StatisticsImage(const std::vector<ModuleImage>& images, const Json& entry) {
    const bool hasOrigin = entry.contains("origin_index") && !entry.at("origin_index").is_null();
    const bool hasIndex = entry.contains("index") && !entry.at("index").is_null();
    const int origin = hasOrigin ? StatisticsInteger(entry.at("origin_index"), "origin_index", false) : -1;
    const int index = hasIndex ? StatisticsInteger(entry.at("index"), "index", false) : -1;
    std::vector<size_t> candidates, matching;
    if (entry.contains("transform") && !entry.at("transform").is_null()) StatisticsSource(entry.at("transform"));
    for (size_t i = 0; i < images.size(); ++i) {
        if (hasOrigin && images[i].OriginalIndex != origin) continue;
        candidates.push_back(i);
        if (entry.contains("transform") && images[i].TransformState.ToJson() == entry.at("transform")) matching.push_back(i);
    }
    if (hasOrigin) {
        for (size_t i : matching) if (i == static_cast<size_t>(index)) return images[i];
        if (matching.size() == 1) return images[matching.front()];
    }
    for (size_t i : candidates) if (i == static_cast<size_t>(index)) return images[i];
    if (candidates.size() == 1 && (hasOrigin || !hasIndex)) return images[candidates.front()];
    throw std::invalid_argument("无法确定结果所属图像，请提供有效 index、origin_index 与 transform");
}

cv::Mat StatisticsMask(const Json& detection) {
    if (detection.contains("mask_array")) {
        const auto& array = detection.at("mask_array");
        if (!array.is_array() || array.empty() || array.size() > static_cast<size_t>(std::numeric_limits<int>::max()) ||
            !array[0].is_array() || array[0].empty() || array[0].size() > static_cast<size_t>(std::numeric_limits<int>::max())) return {};
        const size_t width = array[0].size();
        for (const auto& row : array) {
            if (!row.is_array() || row.size() != width) return {};
            for (const auto& value : row) {
                if (!value.is_number_integer()) return {};
                const double number = value.get<double>();
                if (number < 0 || number > 255) return {};
            }
        }
        cv::Mat mask(static_cast<int>(array.size()), static_cast<int>(width), CV_8UC1);
        for (int y = 0; y < mask.rows; ++y) {
            auto* row = mask.ptr<unsigned char>(y);
            for (int x = 0; x < mask.cols; ++x) row[x] = array[y][x].get<unsigned char>();
        }
        return mask;
    }
    if (!detection.contains("mask_rle")) return {};
    const auto& rle = detection.at("mask_rle");
    if (!rle.is_object() || !rle.contains("width") || !rle.contains("height") || !rle.contains("runs") ||
        !rle.at("width").is_number_integer() || !rle.at("height").is_number_integer() || !rle.at("runs").is_array()) return {};
    const double width = rle.at("width").get<double>();
    const double height = rle.at("height").get<double>();
    const double total = width * height;
    if (width <= 0 || height <= 0 || total > std::numeric_limits<int>::max()) return {};
    double count = 0;
    for (const auto& run : rle.at("runs")) {
        if (!run.is_number_integer()) return {};
        const double length = run.get<double>();
        if (length < 0 || length > total - count) return {};
        count += length;
    }
    return count == total ? MaskInfoToMat(rle) : cv::Mat();
}

double StatisticsTrig(double value) {
    if (std::abs(value) < 1e-12) return 0;
    if (std::abs(value - 1) < 1e-12) return 1;
    if (std::abs(value + 1) < 1e-12) return -1;
    return value;
}

// 旋转框可能包含上限坐标处的像素，采样范围需包含该坐标。
bool StatisticsBounds(const Json& detection, std::array<double, 4>& bounds, std::array<double, 4>& candidates,
    std::array<double, 5>& rotated, bool& hasRotation) {
    if (!detection.contains("bbox")) return false;
    const auto& bbox = detection.at("bbox");
    if (!bbox.is_array() || (bbox.size() != 4 && bbox.size() != 5)) return false;
    std::array<double, 5> values{};
    for (size_t i = 0; i < bbox.size(); ++i) {
        if (!bbox[i].is_number()) return false;
        values[i] = bbox[i].get<double>();
        if (!std::isfinite(values[i])) return false;
    }
    if (values[2] <= 0 || values[3] <= 0) return false;
    hasRotation = bbox.size() == 5;
    double left = values[0], top = values[1], right = left + values[2], bottom = top + values[3];
    if (hasRotation) {
        rotated = values;
        const double c = std::abs(StatisticsTrig(std::cos(values[4]))), s = std::abs(StatisticsTrig(std::sin(values[4])));
        const double rx = values[2] / 2 * c + values[3] / 2 * s;
        const double ry = values[2] / 2 * s + values[3] / 2 * c;
        left = values[0] - rx; right = values[0] + rx;
        top = values[1] - ry; bottom = values[1] + ry;
    }
    // 候选区保留可能属于旋转半开域的最大坐标，mask 缩放范围仍用原始 floor/ceil。
    candidates = {std::floor(left), std::floor(top), hasRotation ? std::floor(right) + 1 : std::ceil(right),
        hasRotation ? std::floor(bottom) + 1 : std::ceil(bottom)};
    left = std::floor(left); top = std::floor(top); right = std::ceil(right); bottom = std::ceil(bottom);
    if (!std::isfinite(left) || !std::isfinite(top) || !std::isfinite(right) || !std::isfinite(bottom) ||
        right <= left || bottom <= top || !std::isfinite(right - left) || !std::isfinite(bottom - top)) return false;
    bounds = {left, top, right, bottom};
    return true;
}

int StatisticsNearestIndex(double offset, double scale, int size) {
    return std::min(size - 1, static_cast<int>(std::floor(offset * scale)));
}

// 将结果选区映射回原图，避免缩放或旋转改变统计值。
std::array<std::vector<double>, 2> StatisticsPixels(const ModuleImage& wrap, const Json& entry,
    const Json& detection, const cv::Mat& mask) {
    const StatisticsTransform current = StatisticsState(wrap.TransformState);
    StatisticsTransform source;
    if (entry.contains("transform") && !entry.at("transform").is_null()) {
        source = StatisticsSource(entry.at("transform"));
    } else {
        source = StatisticsState(TransformationState(current.originalWidth, current.originalHeight));
    }
    if (source.originalWidth != current.originalWidth || source.originalHeight != current.originalHeight) {
        throw std::invalid_argument("结果和图像 transform 的原图尺寸不一致");
    }
    if (wrap.OriginalImage.empty() || wrap.OriginalImage.cols != current.originalWidth || wrap.OriginalImage.rows != current.originalHeight) {
        throw std::invalid_argument("原图尺寸与 transform 不一致");
    }
    const cv::Mat& image = wrap.OriginalImage;
    if (image.dims != 2) throw std::invalid_argument("统计需要二维原图");
    std::array<double, 4> bounds{}, candidates{};
    std::array<double, 5> rotated{};
    bool hasRotation = false;
    if (!StatisticsBounds(detection, bounds, candidates, rotated, hasRotation)) return {};
    const double left = std::max(0.0, candidates[0]), top = std::max(0.0, candidates[1]);
    const double right = std::min(static_cast<double>(source.width), candidates[2]);
    const double bottom = std::min(static_cast<double>(source.height), candidates[3]);
    if (right <= left || bottom <= top) return {};
    const cv::Rect clipped(static_cast<int>(left), static_cast<int>(top), static_cast<int>(right - left), static_cast<int>(bottom - top));
    cv::Mat localMask;
    if (mask.cols == source.width && mask.rows == source.height) {
        localMask = mask(clipped);
    } else {
        // 按完整 bbox 最近邻索引采样，只分配图内 ROI，图外前景不会挤入图内。
        localMask.create(clipped.size(), CV_8UC1);
        const double scaleX = 1.0 / ((bounds[2] - bounds[0]) / mask.cols);
        const double scaleY = 1.0 / ((bounds[3] - bounds[1]) / mask.rows);
        for (int y = 0; y < clipped.height; ++y) {
            const int sy = StatisticsNearestIndex(clipped.y + y - bounds[1], scaleY, mask.rows);
            const auto* sourceRow = mask.ptr<unsigned char>(sy);
            auto* row = localMask.ptr<unsigned char>(y);
            for (int x = 0; x < clipped.width; ++x) {
                const int sx = StatisticsNearestIndex(clipped.x + x - bounds[0], scaleX, mask.cols);
                row[x] = sourceRow[sx];
            }
        }
    }
    cv::Mat foreground;
    cv::compare(localMask, cv::Scalar(127), foreground, cv::CMP_GT);
    cv::Mat domain(clipped.size(), CV_8UC1, cv::Scalar(1));
    if (hasRotation) {
        const double c = StatisticsTrig(std::cos(rotated[4])), s = StatisticsTrig(std::sin(rotated[4]));
        for (int y = 0; y < domain.rows; ++y) {
            auto* row = domain.ptr<unsigned char>(y);
            for (int x = 0; x < domain.cols; ++x) {
                const double dx = clipped.x + x - rotated[0], dy = clipped.y + y - rotated[1];
                const double u = dx * c + dy * s, v = -dx * s + dy * c;
                row[x] = u >= -rotated[2] / 2 && u < rotated[2] / 2 && v >= -rotated[3] / 2 && v < rotated[3] / 2;
            }
        }
    }
    // affine_2x3 已包含裁剪平移，选区仅逆变换回原图，不采样处理后的图像。
    cv::Matx33d mapping = source.matrix.inv() * cv::Matx33d(1, 0, clipped.x, 0, 1, clipped.y, 0, 0, 1);
    double minX = std::numeric_limits<double>::infinity(), minY = minX, maxX = -minX, maxY = -minX;
    for (double y : {-0.5, domain.rows - 0.5}) {
        for (double x : {-0.5, domain.cols - 0.5}) {
            const cv::Vec3d point = mapping * cv::Vec3d(x, y, 1);
            if (!std::isfinite(point[0]) || !std::isfinite(point[1])) throw std::invalid_argument("统计映射包含非有限坐标");
            minX = std::min(minX, point[0]); maxX = std::max(maxX, point[0]);
            minY = std::min(minY, point[1]); maxY = std::max(maxY, point[1]);
        }
    }
    const double x1 = std::max(0.0, std::floor(minX + 0.5)), y1 = std::max(0.0, std::floor(minY + 0.5));
    const double x2 = std::min(static_cast<double>(image.cols), std::ceil(maxX + 0.5));
    const double y2 = std::min(static_cast<double>(image.rows), std::ceil(maxY + 0.5));
    if (x2 <= x1 || y2 <= y1) return {};
    const cv::Rect target(static_cast<int>(x1), static_cast<int>(y1), static_cast<int>(x2 - x1), static_cast<int>(y2 - y1));
    mapping(0, 2) -= target.x; mapping(1, 2) -= target.y;
    cv::Mat warpedForeground, warpedDomain;
    cv::warpAffine(foreground, warpedForeground, cv::Mat(mapping.get_minor<2, 3>(0, 0)), target.size(), cv::INTER_NEAREST);
    cv::warpAffine(domain, warpedDomain, cv::Mat(mapping.get_minor<2, 3>(0, 0)), target.size(), cv::INTER_NEAREST);
    cv::Mat values;
    image(target).convertTo(values, CV_64F);
    std::array<std::vector<double>, 2> pixels;
    for (int y = 0; y < target.height; ++y) {
        const auto* valid = warpedDomain.ptr<unsigned char>(y);
        const auto* selected = warpedForeground.ptr<unsigned char>(y);
        const auto* row = values.ptr<double>(y);
        for (int x = 0; x < target.width; ++x) {
            if (!valid[x]) continue;
            auto& region = pixels[selected[x] ? 0 : 1];
            for (int channel = 0; channel < values.channels(); ++channel) {
                const double value = row[x * values.channels() + channel];
                if (!std::isfinite(value)) throw std::invalid_argument("统计区域图像包含非有限数值");
                region.push_back(value);
            }
        }
    }
    return pixels;
}

class ForegroundBackgroundStatisticsModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        if (!Properties.is_object()) throw std::invalid_argument("统计 properties 必须为对象");
        bool mean = true, median = false;
        for (const char* name : {"mean", "median"}) {
            if (!Properties.contains(name)) continue;
            if (!Properties.at(name).is_boolean()) throw std::invalid_argument(std::string(name) + " 必须为 bool");
            (std::string(name) == "mean" ? mean : median) = Properties.at(name).get<bool>();
        }
        if (!resultList.is_null() && !resultList.is_array()) throw std::invalid_argument("统计 results 必须为数组");
        Json results = resultList.is_null() ? Json::array() : resultList;
        for (auto& entry : results) {
            if (!entry.is_object()) throw std::invalid_argument("结果 entry 必须为对象");
            if (entry.value("type", std::string()) != "local") continue;
            if (!entry.contains("sample_results")) entry["sample_results"] = Json::array();
            if (!entry.at("sample_results").is_array()) throw std::invalid_argument("local.sample_results 必须为数组");
            const ModuleImage* selected = nullptr;
            for (auto& detection : entry["sample_results"]) {
                if (!detection.is_object()) throw std::invalid_argument("sample_results 中的目标必须为对象");
                if (detection.contains("extra_info") && !detection.at("extra_info").is_null() &&
                    !detection.at("extra_info").is_object()) {
                    throw std::invalid_argument(std::string("extra_info 必须为对象或 null，实际类型为 ") +
                        detection.at("extra_info").type_name());
                }
                for (const char* name : {"with_mean", "foreground_mean", "background_mean", "with_median", "foreground_median", "background_median"}) {
                    detection.erase(name);
                    if (detection.contains("extra_info") && detection.at("extra_info").is_object()) {
                        detection["extra_info"].erase(name);
                    }
                }
                if (!mean && !median) {
                    if (detection.contains("extra_info") &&
                        (detection.at("extra_info").is_null() || detection.at("extra_info").empty())) {
                        detection.erase("extra_info");
                    }
                    continue;
                }
                if (!detection.contains("extra_info") || detection.at("extra_info").is_null()) {
                    detection["extra_info"] = Json::object();
                }
                Json& extraInfo = detection["extra_info"];
                std::array<std::vector<double>, 2> pixels;
                if (mean || median) {
                    const cv::Mat mask = StatisticsMask(detection);
                    if (!mask.empty()) {
                        if (!selected) selected = &StatisticsImage(imageList, entry);
                        pixels = StatisticsPixels(*selected, entry, detection, mask);
                    }
                }
                const bool sampled = !pixels[0].empty() || !pixels[1].empty();
                if (mean) extraInfo["with_mean"] = sampled;
                if (median) extraInfo["with_median"] = sampled;
                for (size_t i = 0; i < pixels.size(); ++i) {
                    auto& values = pixels[i];
                    const std::string prefix = i == 0 ? "foreground_" : "background_";
                    if (mean) {
                        if (values.empty()) extraInfo[prefix + "mean"] = nullptr;
                        else {
                            double scale = 0;
                            for (double value : values) scale = std::max(scale, std::abs(value));
                            double sum = 0;
                            if (scale > 0) for (double value : values) sum += value / scale;
                            extraInfo[prefix + "mean"] = std::max(-1.0, std::min(1.0, sum / values.size())) * scale;
                        }
                    }
                    if (median) {
                        if (values.empty()) extraInfo[prefix + "median"] = nullptr;
                        else {
                            std::sort(values.begin(), values.end());
                            const size_t mid = values.size() / 2;
                            double medianValue = values[mid];
                            if (values.size() % 2 == 0) {
                                const double scale = std::max(std::abs(values[mid - 1]), std::abs(values[mid]));
                                medianValue = scale == 0 ? 0 : (values[mid - 1] / scale + values[mid] / scale) / 2 * scale;
                            }
                            extraInfo[prefix + "median"] = medianValue;
                        }
                    }
                }
            }
        }
        return ModuleIO(imageList, std::move(results));
    }
};

DLCV_FLOW_REGISTER_MODULE("post_process/foreground_background_statistics", ForegroundBackgroundStatisticsModule)

} // namespace
} // namespace flow
} // namespace dlcv_infer
