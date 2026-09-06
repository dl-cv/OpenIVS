#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include "flow/utils/MaskRleUtils.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <queue>
#include <string>
#include <vector>

#include "opencv2/imgproc.hpp"

namespace dlcv_infer {
namespace flow {
namespace {

static double ReadNumber(const Json& value, double defaultValue = 0.0) {
    try {
        if (value.is_number()) return value.get<double>();
        if (value.is_string()) return std::stod(value.get<std::string>());
    } catch (...) {}
    return defaultValue;
}

static std::vector<cv::Point2f> ParsePolygon(const Json& token) {
    Json value = token;
    try {
        if (value.is_string()) value = Json::parse(value.get<std::string>());
    } catch (...) {
        return {};
    }
    if (!value.is_array()) return {};

    std::vector<cv::Point2f> points;
    if (!value.empty() && value.at(0).is_array()) {
        points.reserve(value.size());
        for (const auto& point : value) {
            if (!point.is_array() || point.size() < 2) continue;
            points.emplace_back(
                static_cast<float>(ReadNumber(point.at(0))),
                static_cast<float>(ReadNumber(point.at(1))));
        }
    } else {
        for (size_t i = 0; i + 1 < value.size(); i += 2) {
            points.emplace_back(
                static_cast<float>(ReadNumber(value.at(i))),
                static_cast<float>(ReadNumber(value.at(i + 1))));
        }
    }
    return points.size() >= 3 ? points : std::vector<cv::Point2f>();
}

static std::vector<cv::Point2f> PolygonFromMask(const Json& detection, int imageWidth, int imageHeight) {
    if (!detection.is_object() || !detection.contains("mask_rle")) return {};
    cv::Mat mask = MaskInfoToMat(detection.at("mask_rle"));
    if (mask.empty()) return {};

    std::vector<std::vector<cv::Point>> contours;
    cv::findContours(mask, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);
    if (contours.empty()) return {};
    const auto best = std::max_element(contours.begin(), contours.end(), [](const auto& a, const auto& b) {
        return std::abs(cv::contourArea(a)) < std::abs(cv::contourArea(b));
    });
    if (best == contours.end() || best->size() < 3) return {};

    double bx = 0.0;
    double by = 0.0;
    double bw = static_cast<double>(mask.cols);
    double bh = static_cast<double>(mask.rows);
    if (detection.contains("bbox") && detection.at("bbox").is_array() && detection.at("bbox").size() >= 4) {
        const Json& bbox = detection.at("bbox");
        bx = ReadNumber(bbox.at(0));
        by = ReadNumber(bbox.at(1));
        bw = std::max(1.0, ReadNumber(bbox.at(2), static_cast<double>(mask.cols)));
        bh = std::max(1.0, ReadNumber(bbox.at(3), static_cast<double>(mask.rows)));
    }

    const bool fullImageMask = mask.cols == imageWidth && mask.rows == imageHeight;
    const double sx = fullImageMask ? 1.0 : bw / std::max(1, mask.cols);
    const double sy = fullImageMask ? 1.0 : bh / std::max(1, mask.rows);
    const double ox = fullImageMask ? 0.0 : bx;
    const double oy = fullImageMask ? 0.0 : by;

    std::vector<cv::Point2f> points;
    points.reserve(best->size());
    for (const cv::Point& point : *best) {
        points.emplace_back(
            static_cast<float>(ox + point.x * sx),
            static_cast<float>(oy + point.y * sy));
    }
    return points;
}

static std::vector<cv::Point2f> ExtractPolygon(const Json& detection, int imageWidth, int imageHeight) {
    if (detection.is_object() && detection.contains("polygon")) {
        std::vector<cv::Point2f> polygon = ParsePolygon(detection.at("polygon"));
        if (polygon.size() >= 3) return polygon;
    }
    return PolygonFromMask(detection, imageWidth, imageHeight);
}

static void ZhangSuenThin(cv::Mat& image) {
    if (image.empty() || image.type() != CV_8UC1) return;
    cv::threshold(image, image, 0, 1, cv::THRESH_BINARY);
    cv::Mat marker = cv::Mat::zeros(image.size(), CV_8UC1);
    bool changed = true;
    while (changed) {
        changed = false;
        for (int iteration = 0; iteration < 2; iteration++) {
            marker.setTo(0);
            for (int y = 1; y < image.rows - 1; y++) {
                const uint8_t* prev = image.ptr<uint8_t>(y - 1);
                const uint8_t* row = image.ptr<uint8_t>(y);
                const uint8_t* next = image.ptr<uint8_t>(y + 1);
                uint8_t* mark = marker.ptr<uint8_t>(y);
                for (int x = 1; x < image.cols - 1; x++) {
                    if (row[x] == 0) continue;
                    const int p2 = prev[x];
                    const int p3 = prev[x + 1];
                    const int p4 = row[x + 1];
                    const int p5 = next[x + 1];
                    const int p6 = next[x];
                    const int p7 = next[x - 1];
                    const int p8 = row[x - 1];
                    const int p9 = prev[x - 1];
                    const int neighbours = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9;
                    if (neighbours < 2 || neighbours > 6) continue;
                    const int transitions =
                        (p2 == 0 && p3 == 1) + (p3 == 0 && p4 == 1)
                        + (p4 == 0 && p5 == 1) + (p5 == 0 && p6 == 1)
                        + (p6 == 0 && p7 == 1) + (p7 == 0 && p8 == 1)
                        + (p8 == 0 && p9 == 1) + (p9 == 0 && p2 == 1);
                    if (transitions != 1) continue;
                    if (iteration == 0) {
                        if (p2 * p4 * p6 != 0 || p4 * p6 * p8 != 0) continue;
                    } else {
                        if (p2 * p4 * p8 != 0 || p2 * p6 * p8 != 0) continue;
                    }
                    mark[x] = 1;
                }
            }
            if (cv::countNonZero(marker) > 0) {
                image.setTo(0, marker);
                changed = true;
            }
        }
    }
}

struct BfsResult final {
    int Farthest = -1;
    std::vector<int> Previous;
};

static BfsResult RunSkeletonBfs(const cv::Mat& skeleton, int start, bool keepPrevious) {
    const int width = skeleton.cols;
    const int total = skeleton.rows * width;
    std::vector<int> distance(static_cast<size_t>(total), -1);
    std::vector<int> previous(keepPrevious ? static_cast<size_t>(total) : 0, -1);
    std::queue<int> pending;
    distance[static_cast<size_t>(start)] = 0;
    pending.push(start);
    int farthest = start;
    static const int dx[8] = {-1, 0, 1, -1, 1, -1, 0, 1};
    static const int dy[8] = {-1, -1, -1, 0, 0, 1, 1, 1};

    while (!pending.empty()) {
        const int current = pending.front();
        pending.pop();
        if (distance[static_cast<size_t>(current)] > distance[static_cast<size_t>(farthest)]) farthest = current;
        const int x = current % width;
        const int y = current / width;
        for (int k = 0; k < 8; k++) {
            const int nx = x + dx[k];
            const int ny = y + dy[k];
            if (nx < 0 || ny < 0 || nx >= skeleton.cols || ny >= skeleton.rows) continue;
            if (skeleton.at<uint8_t>(ny, nx) == 0) continue;
            const int next = ny * width + nx;
            if (distance[static_cast<size_t>(next)] >= 0) continue;
            distance[static_cast<size_t>(next)] = distance[static_cast<size_t>(current)] + 1;
            if (keepPrevious) previous[static_cast<size_t>(next)] = current;
            pending.push(next);
        }
    }
    return BfsResult{farthest, std::move(previous)};
}

static std::vector<cv::Point2f> LongestSkeletonPath(const cv::Mat& skeleton) {
    int start = -1;
    for (int y = 0; y < skeleton.rows && start < 0; y++) {
        const uint8_t* row = skeleton.ptr<uint8_t>(y);
        for (int x = 0; x < skeleton.cols; x++) {
            if (row[x] != 0) {
                start = y * skeleton.cols + x;
                break;
            }
        }
    }
    if (start < 0) return {};
    const BfsResult first = RunSkeletonBfs(skeleton, start, false);
    const BfsResult second = RunSkeletonBfs(skeleton, first.Farthest, true);
    if (second.Farthest < 0) return {};

    std::vector<cv::Point2f> reversePath;
    int current = second.Farthest;
    while (current >= 0) {
        reversePath.emplace_back(
            static_cast<float>(current % skeleton.cols),
            static_cast<float>(current / skeleton.cols));
        if (current == first.Farthest) break;
        current = second.Previous[static_cast<size_t>(current)];
    }
    if (reversePath.size() < 2) return {};
    std::reverse(reversePath.begin(), reversePath.end());
    return reversePath;
}

static std::vector<cv::Point2f> SmoothPath(const std::vector<cv::Point2f>& input) {
    if (input.size() < 3) return input;
    std::vector<cv::Point2f> current = input;
    for (int pass = 0; pass < 2; pass++) {
        std::vector<cv::Point2f> smoothed(current.size());
        smoothed.front() = current.front();
        smoothed.back() = current.back();
        for (size_t i = 1; i + 1 < current.size(); i++) {
            const int begin = std::max<int>(0, static_cast<int>(i) - 4);
            const int end = std::min<int>(static_cast<int>(current.size()) - 1, static_cast<int>(i) + 4);
            cv::Point2f sum(0.0f, 0.0f);
            for (int j = begin; j <= end; j++) sum += current[static_cast<size_t>(j)];
            smoothed[i] = sum * (1.0f / static_cast<float>(end - begin + 1));
        }
        current.swap(smoothed);
    }
    return current;
}

static cv::Point2f ExtendToMask(const cv::Point2f& start, const cv::Point2f& direction, const cv::Mat& mask) {
    const float length = std::sqrt(direction.x * direction.x + direction.y * direction.y);
    if (length < 1e-6f) return start;
    const cv::Point2f unit = direction * (1.0f / length);
    cv::Point2f last = start;
    for (int step = 0; step <= 300; step++) {
        const cv::Point2f point = start + unit * static_cast<float>(step);
        const int x = cvRound(point.x);
        const int y = cvRound(point.y);
        if (x < 0 || y < 0 || x >= mask.cols || y >= mask.rows || mask.at<uint8_t>(y, x) == 0) break;
        last = point;
    }
    return last;
}

static std::vector<cv::Point2f> ComputeCenterCurve(
    const std::vector<cv::Point2f>& polygon,
    int imageWidth,
    int imageHeight) {
    std::vector<cv::Point> polygonInt;
    polygonInt.reserve(polygon.size());
    for (const cv::Point2f& point : polygon) {
        polygonInt.emplace_back(
            std::max(0, std::min(imageWidth - 1, cvRound(point.x))),
            std::max(0, std::min(imageHeight - 1, cvRound(point.y))));
    }
    cv::Rect bounds = cv::boundingRect(polygonInt);
    bounds.x = std::max(0, bounds.x - 2);
    bounds.y = std::max(0, bounds.y - 2);
    bounds.width = std::min(imageWidth - bounds.x, bounds.width + 4);
    bounds.height = std::min(imageHeight - bounds.y, bounds.height + 4);
    if (bounds.width < 3 || bounds.height < 3) return {};

    std::vector<cv::Point> localPolygon;
    localPolygon.reserve(polygonInt.size());
    for (const cv::Point& point : polygonInt) localPolygon.emplace_back(point.x - bounds.x, point.y - bounds.y);
    cv::Mat mask(bounds.height, bounds.width, CV_8UC1, cv::Scalar::all(0));
    cv::fillPoly(mask, std::vector<std::vector<cv::Point>>{localPolygon}, cv::Scalar::all(255));
    cv::Mat skeleton = mask.clone();
    ZhangSuenThin(skeleton);
    std::vector<cv::Point2f> curve = SmoothPath(LongestSkeletonPath(skeleton));
    if (curve.size() < 2) return {};

    const size_t directionIndex = std::min<size_t>(9, curve.size() - 1);
    const cv::Point2f headDirection = curve.front() - curve[directionIndex];
    const cv::Point2f tailDirection = curve.back() - curve[curve.size() - 1 - directionIndex];
    curve.front() = ExtendToMask(curve.front(), headDirection, mask);
    curve.back() = ExtendToMask(curve.back(), tailDirection, mask);
    for (cv::Point2f& point : curve) {
        point.x += static_cast<float>(bounds.x);
        point.y += static_cast<float>(bounds.y);
    }
    const float dx = curve.back().x - curve.front().x;
    const float dy = curve.back().y - curve.front().y;
    if (dx < 0.0f || (std::abs(dx) < 1e-6f && dy < 0.0f)) std::reverse(curve.begin(), curve.end());
    return curve;
}

static std::vector<cv::Point2f> ResamplePath(const std::vector<cv::Point2f>& points, double step) {
    if (points.size() < 2) return points;
    std::vector<double> cumulative(points.size(), 0.0);
    for (size_t i = 1; i < points.size(); i++) {
        cumulative[i] = cumulative[i - 1] + cv::norm(points[i] - points[i - 1]);
    }
    const double total = cumulative.back();
    if (total <= 1e-6) return {points.front()};
    const int count = std::max(2, static_cast<int>(std::floor(total / std::max(1e-6, step))) + 1);
    std::vector<cv::Point2f> output;
    output.reserve(static_cast<size_t>(count));
    size_t segment = 0;
    for (int i = 0; i < count; i++) {
        const double distance = total * static_cast<double>(i) / static_cast<double>(count - 1);
        while (segment + 1 < cumulative.size() && cumulative[segment + 1] < distance) segment++;
        if (segment + 1 >= points.size()) {
            output.push_back(points.back());
            continue;
        }
        const double length = cumulative[segment + 1] - cumulative[segment];
        const float alpha = length <= 1e-6 ? 0.0f : static_cast<float>((distance - cumulative[segment]) / length);
        output.push_back(points[segment] * (1.0f - alpha) + points[segment + 1] * alpha);
    }
    return output;
}

static bool SegmentIntersectionParameter(
    const cv::Point2f& point,
    const cv::Point2f& ray,
    const cv::Point2f& a,
    const cv::Point2f& b,
    double& t) {
    const cv::Point2d r(ray.x, ray.y);
    const cv::Point2d s(b.x - a.x, b.y - a.y);
    const double cross = r.x * s.y - r.y * s.x;
    if (std::abs(cross) < 1e-12) return false;
    const cv::Point2d qp(a.x - point.x, a.y - point.y);
    t = (qp.x * s.y - qp.y * s.x) / cross;
    const double u = (qp.x * r.y - qp.y * r.x) / cross;
    return u >= 0.0 && u <= 1.0;
}

static bool PointInside(const std::vector<cv::Point2f>& polygon, const cv::Point2f& point) {
    return cv::pointPolygonTest(polygon, point, false) >= 0.0;
}

static bool PullInside(
    const std::vector<cv::Point2f>& polygon,
    const cv::Point2f& center,
    cv::Point2f& point) {
    for (int i = 0; i < 4; i++) {
        if (PointInside(polygon, point)) return true;
        point = (point + center) * 0.5f;
    }
    return PointInside(polygon, point);
}

static bool BuildSides(
    const std::vector<cv::Point2f>& centerCurve,
    const std::vector<cv::Point2f>& polygon,
    double sampleStep,
    double shrinkInside,
    std::vector<cv::Point2f>& centers,
    std::vector<cv::Point2f>& lefts,
    std::vector<cv::Point2f>& rights) {
    const std::vector<cv::Point2f> sampled = ResamplePath(centerCurve, sampleStep);
    if (sampled.size() < 2) return false;
    for (size_t i = 0; i < sampled.size(); i++) {
        cv::Point2f tangent;
        if (i == 0) tangent = sampled[1] - sampled[0];
        else if (i + 1 == sampled.size()) tangent = sampled.back() - sampled[sampled.size() - 2];
        else tangent = sampled[i + 1] - sampled[i - 1];
        const float length = std::sqrt(tangent.x * tangent.x + tangent.y * tangent.y);
        if (length < 1e-6f) continue;
        tangent *= 1.0f / length;
        const cv::Point2f normal(-tangent.y, tangent.x);

        double positive = std::numeric_limits<double>::infinity();
        double negative = -std::numeric_limits<double>::infinity();
        for (size_t edge = 0; edge < polygon.size(); edge++) {
            double t = 0.0;
            if (!SegmentIntersectionParameter(sampled[i], normal, polygon[edge], polygon[(edge + 1) % polygon.size()], t)) continue;
            if (t > 1e-6 && t < positive) positive = t;
            else if (t < -1e-6 && t > negative) negative = t;
        }
        if (!std::isfinite(positive) || !std::isfinite(negative)) continue;
        cv::Point2f left = sampled[i] + normal * static_cast<float>(positive - shrinkInside);
        cv::Point2f right = sampled[i] + normal * static_cast<float>(negative + shrinkInside);
        if (!PullInside(polygon, sampled[i], left) || !PullInside(polygon, sampled[i], right)) continue;
        centers.push_back(sampled[i]);
        lefts.push_back(left);
        rights.push_back(right);
    }
    return centers.size() >= 2;
}

static cv::Mat UnwrapByRemap(
    const cv::Mat& image,
    const std::vector<cv::Point2f>& centers,
    const std::vector<cv::Point2f>& lefts,
    const std::vector<cv::Point2f>& rights,
    int outputHeight,
    int maxWidth,
    int borderMode) {
    if (centers.size() < 2 || centers.size() != lefts.size() || centers.size() != rights.size()) return cv::Mat();
    const int height = std::max(2, outputHeight);
    double meanLeftY = 0.0;
    double meanRightY = 0.0;
    for (size_t i = 0; i < lefts.size(); i++) {
        meanLeftY += lefts[i].y;
        meanRightY += rights[i].y;
    }
    const bool topIsLeft = meanLeftY <= meanRightY;
    const std::vector<cv::Point2f>& tops = topIsLeft ? lefts : rights;
    const std::vector<cv::Point2f>& bottoms = topIsLeft ? rights : lefts;

    std::vector<double> cumulative(centers.size(), 0.0);
    for (size_t i = 1; i < centers.size(); i++) cumulative[i] = cumulative[i - 1] + cv::norm(centers[i] - centers[i - 1]);
    const double total = cumulative.back();
    if (total <= 1e-6) return cv::Mat();
    int width = std::max(2, cvRound(total));
    if (maxWidth > 0) width = std::min(width, maxWidth);

    cv::Mat mapX(height, width, CV_32FC1);
    cv::Mat mapY(height, width, CV_32FC1);
    size_t segment = 0;
    for (int x = 0; x < width; x++) {
        const double distance = (static_cast<double>(x) + 0.5) * total / static_cast<double>(width);
        while (segment + 1 < cumulative.size() && cumulative[segment + 1] < distance) segment++;
        if (segment + 1 >= centers.size()) segment = centers.size() - 2;
        const double length = std::max(1e-6, cumulative[segment + 1] - cumulative[segment]);
        const float alpha = static_cast<float>((distance - cumulative[segment]) / length);
        const cv::Point2f top = tops[segment] * (1.0f - alpha) + tops[segment + 1] * alpha;
        const cv::Point2f bottom = bottoms[segment] * (1.0f - alpha) + bottoms[segment + 1] * alpha;
        for (int y = 0; y < height; y++) {
            const float vertical = height <= 1 ? 0.0f : static_cast<float>(y) / static_cast<float>(height - 1);
            const cv::Point2f source = top * (1.0f - vertical) + bottom * vertical;
            mapX.at<float>(y, x) = source.x;
            mapY.at<float>(y, x) = source.y;
        }
    }

    cv::Mat output;
    cv::remap(image, output, mapX, mapY, cv::INTER_CUBIC, borderMode);
    return output;
}

static Json PolygonToJson(const std::vector<cv::Point2f>& polygon) {
    Json result = Json::array();
    for (const cv::Point2f& point : polygon) result.push_back(Json::array({point.x, point.y}));
    return result;
}

static const Json* FindEntryForImage(const Json& results, size_t imageIndex, int originalIndex) {
    if (!results.is_array()) return nullptr;
    for (const auto& entry : results) {
        if (!entry.is_object() || entry.value("type", std::string()) != "local") continue;
        try {
            if (entry.contains("index") && entry.at("index").get<int>() == static_cast<int>(imageIndex)) return &entry;
        } catch (...) {}
    }
    if (imageIndex < results.size() && results.at(imageIndex).is_object()) return &results.at(imageIndex);
    for (const auto& entry : results) {
        if (!entry.is_object() || entry.value("type", std::string()) != "local") continue;
        try {
            if (entry.contains("origin_index") && entry.at("origin_index").get<int>() == originalIndex) return &entry;
        } catch (...) {}
    }
    return nullptr;
}

class CurveTextAffineModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        if (!ReadBool("enable", true)) return ModuleIO(imageList, resultList, Json::array());
        const int outputHeight = std::max(2, ReadInt("out_height", 80));
        const double sampleStep = std::max(1.0, ReadDouble("sample_step", 10.0));
        const double shrinkInside = std::max(0.0, ReadDouble("shrink_inside", 1.5));
        const int maxWidth = std::max(0, ReadInt("max_unwrap_width", 0));
        const std::string borderName = ReadString("border_mode", "reflect101");
        const int borderMode = (borderName == "constant" || borderName == "const") ? cv::BORDER_CONSTANT : cv::BORDER_REFLECT101;

        std::vector<ModuleImage> outputImages;
        Json outputResults = Json::array();
        for (size_t imageIndex = 0; imageIndex < imageList.size(); imageIndex++) {
            const ModuleImage& base = imageList[imageIndex];
            if (base.ImageObject.empty()) continue;
            const Json* sourceEntry = FindEntryForImage(resultList, imageIndex, base.OriginalIndex);
            if (sourceEntry == nullptr || !sourceEntry->contains("sample_results") || !sourceEntry->at("sample_results").is_array()) continue;

            for (const auto& detection : sourceEntry->at("sample_results")) {
                if (!detection.is_object()) continue;
                std::vector<cv::Point2f> polygon = ExtractPolygon(detection, base.ImageObject.cols, base.ImageObject.rows);
                if (polygon.size() < 3 || std::abs(cv::contourArea(polygon)) <= 0.0) continue;
                std::vector<cv::Point2f> centerCurve = ComputeCenterCurve(polygon, base.ImageObject.cols, base.ImageObject.rows);
                if (centerCurve.size() < 2) continue;
                std::vector<cv::Point2f> centers;
                std::vector<cv::Point2f> lefts;
                std::vector<cv::Point2f> rights;
                if (!BuildSides(centerCurve, polygon, sampleStep, shrinkInside, centers, lefts, rights)) continue;
                cv::Mat affine = UnwrapByRemap(base.ImageObject, centers, lefts, rights, outputHeight, maxWidth, borderMode);
                if (affine.empty()) continue;

                ModuleImage output = base;
                output.AffineImage = affine;
                outputImages.push_back(std::move(output));

                Json detectionOut = detection;
                detectionOut["polygon"] = PolygonToJson(polygon);
                Json entryOut = *sourceEntry;
                entryOut["type"] = "local";
                entryOut["index"] = static_cast<int>(outputImages.size() - 1);
                entryOut["origin_index"] = base.OriginalIndex;
                entryOut["transform"] = base.TransformState.ToJson();
                entryOut["sample_results"] = Json::array({std::move(detectionOut)});
                entryOut["ok"] = true;
                entryOut["reason"] = nullptr;
                outputResults.push_back(std::move(entryOut));
            }
        }
        return ModuleIO(std::move(outputImages), std::move(outputResults), Json::array());
    }
};

DLCV_FLOW_REGISTER_MODULE("pre_process/curve_text_affine", CurveTextAffineModule)

} // namespace
} // namespace flow
} // namespace dlcv_infer
