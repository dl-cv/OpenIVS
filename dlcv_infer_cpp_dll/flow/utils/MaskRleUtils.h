#pragma once

#include <algorithm>
#include <cstdlib>
#include <cstdint>
#include <cstring>
#include <limits>
#include <vector>

#include "flow/FlowTypes.h"
#include "opencv2/imgproc.hpp"

namespace dlcv_infer {
namespace flow {

/// <summary>
/// 将单通道掩膜 Mat 按行优先展开为数字 RLE：首段永远为 0，随后每段在 0/1 间切换。
/// 仅存储 width, height, runs（像素值非零视为 1）。对齐 OpenIVS/DlcvCsharpApi/MaskRleUtils.cs。
/// </summary>
inline Json MatToMaskInfo(const cv::Mat& mask) {
    int width = 0;
    int height = 0;
    std::vector<int> runs;

    if (!mask.empty()) {
        height = std::max(0, mask.rows);
        width = std::max(0, mask.cols);
        if (width > 0 && height > 0) {
            cv::Mat src = mask;
            if (!src.isContinuous()) src = src.clone();
            cv::Mat u8;
            if (src.type() != CV_8UC1) {
                if (src.channels() == 1) {
                    src.convertTo(u8, CV_8U);
                } else {
                    cv::cvtColor(src, u8, cv::COLOR_BGR2GRAY);
                }
            } else {
                u8 = src;
            }

            const int total = width * height;
            const uint8_t* buf = reinterpret_cast<const uint8_t*>(u8.data);

            int currentValue = 0; // 首段固定为 0
            int count = 0;
            for (int i = 0; i < total; i++) {
                const int bit = buf[i] != 0 ? 1 : 0;
                if (bit == currentValue) {
                    count += 1;
                } else {
                    runs.push_back(count);
                    currentValue = bit;
                    count = 1;
                }
            }
            runs.push_back(count);
        }
    }

    Json obj = Json::object();
    obj["width"] = width;
    obj["height"] = height;
    obj["runs"] = runs;
    return obj;
}

inline bool TryReadIntLike(const Json& token, int& outVal) {
    if (token.is_number_integer()) {
        outVal = token.get<int>();
        return true;
    }
    if (token.is_number_unsigned()) {
        const unsigned long long u = token.get<unsigned long long>();
        outVal = static_cast<int>(std::min<unsigned long long>(u, static_cast<unsigned long long>(std::numeric_limits<int>::max())));
        return true;
    }
    if (token.is_number_float()) {
        outVal = static_cast<int>(token.get<double>());
        return true;
    }
    if (token.is_string()) {
        const std::string s = token.get<std::string>();
        if (s.empty()) return false;
        char* endPtr = nullptr;
        const long long v = std::strtoll(s.c_str(), &endPtr, 10);
        if (endPtr == s.c_str()) return false;
        if (v > static_cast<long long>(std::numeric_limits<int>::max())) {
            outVal = std::numeric_limits<int>::max();
        } else if (v < static_cast<long long>(std::numeric_limits<int>::min())) {
            outVal = std::numeric_limits<int>::min();
        } else {
            outVal = static_cast<int>(v);
        }
        return true;
    }
    return false;
}

inline bool TryReadLongLike(const Json& token, long long& outVal) {
    if (token.is_number_integer() || token.is_number_unsigned()) {
        outVal = token.get<long long>();
        return true;
    }
    if (token.is_number_float()) {
        outVal = static_cast<long long>(token.get<double>());
        return true;
    }
    if (token.is_string()) {
        const std::string s = token.get<std::string>();
        if (s.empty()) return false;
        char* endPtr = nullptr;
        const long long v = std::strtoll(s.c_str(), &endPtr, 10);
        if (endPtr == s.c_str()) return false;
        outVal = v;
        return true;
    }
    return false;
}

inline bool TryReadMaskInfoHeader(const Json& maskInfo, int& width, int& height, const Json::array_t*& runsArr) {
    width = 0;
    height = 0;
    runsArr = nullptr;
    if (!maskInfo.is_object()) return false;

    const auto itW = maskInfo.find("width");
    const auto itH = maskInfo.find("height");
    const auto itRuns = maskInfo.find("runs");
    if (itW == maskInfo.end() || itH == maskInfo.end() || itRuns == maskInfo.end()) return false;
    if (!itRuns->is_array()) return false;
    if (!TryReadIntLike(*itW, width) || !TryReadIntLike(*itH, height)) return false;
    if (width <= 0 || height <= 0) return false;
    runsArr = &itRuns->get_ref<const Json::array_t&>();
    return true;
}

/// <summary>
/// 从数字 RLE 信息还原单通道掩膜（CV_8UC1），1 段写入 255，0 段写入 0。
/// 期望字段：width(int), height(int), runs(int[])；首段为 0。
/// </summary>
inline cv::Mat MaskInfoToMat(const Json& maskInfo) {
    int width = 0;
    int height = 0;
    const Json::array_t* runsArr = nullptr;
    if (!TryReadMaskInfoHeader(maskInfo, width, height, runsArr)) return cv::Mat();

    cv::Mat dst(height, width, CV_8UC1, cv::Scalar(0));
    if (!dst.isContinuous()) dst = dst.clone();
    const int total = width * height;
    uint8_t* basePtr = reinterpret_cast<uint8_t*>(dst.data);

    int idx = 0;
    int value = 0; // 首段为 0
    for (size_t i = 0; i < runsArr->size() && idx < total; i++) {
        int count = 0;
        if (!TryReadIntLike((*runsArr)[i], count)) count = 0;
        if (count <= 0) {
            value ^= 1;
            continue;
        }
        const int writeCount = std::min(count, total - idx);
        if (value == 1 && writeCount > 0) {
            std::memset(basePtr + idx, 0xFF, static_cast<size_t>(writeCount));
        }
        idx += writeCount;
        value ^= 1;
    }
    return dst;
}

/// <summary>
/// 从二值 mask 提取最小外接旋转框。
/// 使用外轮廓点代替全量前景点，降低 minAreaRect 输入规模。
/// </summary>
inline bool TryComputeMinAreaRectInplace(cv::Mat& binaryMask, cv::RotatedRect& rotatedRect) {
    rotatedRect = cv::RotatedRect();
    if (binaryMask.empty()) return false;

    std::vector<std::vector<cv::Point>> contours;
    cv::findContours(binaryMask, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);
    if (contours.empty()) return false;

    size_t totalPoints = 0;
    int firstNonEmptyIndex = -1;
    int nonEmptyContourCount = 0;
    for (size_t i = 0; i < contours.size(); i++) {
        const auto& contour = contours[i];
        if (contour.empty()) continue;
        if (firstNonEmptyIndex < 0) firstNonEmptyIndex = static_cast<int>(i);
        totalPoints += contour.size();
        nonEmptyContourCount += 1;
    }

    if (totalPoints == 0 || firstNonEmptyIndex < 0) return false;

    if (nonEmptyContourCount == 1) {
        rotatedRect = cv::minAreaRect(contours[static_cast<size_t>(firstNonEmptyIndex)]);
        return true;
    }

    std::vector<cv::Point> allPoints;
    allPoints.reserve(totalPoints);
    for (const auto& contour : contours) {
        if (contour.empty()) continue;
        allPoints.insert(allPoints.end(), contour.begin(), contour.end());
    }
    if (allPoints.empty()) return false;

    rotatedRect = cv::minAreaRect(allPoints);
    return true;
}

inline bool TryComputeMinAreaRect(const cv::Mat& maskMat, cv::RotatedRect& rotatedRect) {
    if (maskMat.empty()) {
        rotatedRect = cv::RotatedRect();
        return false;
    }

    cv::Mat binary;
    if (maskMat.type() == CV_8UC1) {
        binary = maskMat.clone();
    } else if (maskMat.channels() == 1) {
        maskMat.convertTo(binary, CV_8U);
    } else {
        cv::cvtColor(maskMat, binary, cv::COLOR_BGR2GRAY);
    }
    if (binary.empty()) {
        rotatedRect = cv::RotatedRect();
        return false;
    }

    return TryComputeMinAreaRectInplace(binary, rotatedRect);
}

/// <summary>
/// 从 RLE mask 直接提取最小外接旋转框。
/// </summary>
inline bool TryComputeMinAreaRectFromMaskInfo(const Json& maskInfo, cv::RotatedRect& rotatedRect) {
    int width = 0;
    int height = 0;
    const Json::array_t* runsArr = nullptr;
    if (!TryReadMaskInfoHeader(maskInfo, width, height, runsArr)) {
        rotatedRect = cv::RotatedRect();
        return false;
    }

    const int total = width * height;
    if (total <= 0) {
        rotatedRect = cv::RotatedRect();
        return false;
    }

    // 快速路径：直接基于 RLE 统计每行前景范围，避免完整解码与 findContours。
    std::vector<int> rowMin(static_cast<size_t>(height), std::numeric_limits<int>::max());
    std::vector<int> rowMax(static_cast<size_t>(height), -1);
    int idx = 0;
    int value = 0;

    for (size_t i = 0; i < runsArr->size() && idx < total; i++) {
        int count = 0;
        if (!TryReadIntLike((*runsArr)[i], count)) count = 0;
        if (count <= 0) {
            value ^= 1;
            continue;
        }

        const int writeCount = std::min(count, total - idx);
        if (value == 1 && writeCount > 0) {
            int remain = writeCount;
            int pos = idx;
            while (remain > 0) {
                const int y = pos / width;
                const int x = pos - y * width;
                const int seg = std::min(remain, width - x);
                const int x0 = x;
                const int x1 = x + seg - 1;
                if (y >= 0 && y < height && x1 >= x0) {
                    int& minRef = rowMin[static_cast<size_t>(y)];
                    int& maxRef = rowMax[static_cast<size_t>(y)];
                    if (x0 < minRef) minRef = x0;
                    if (x1 > maxRef) maxRef = x1;
                }
                pos += seg;
                remain -= seg;
            }
        }

        idx += writeCount;
        value ^= 1;
    }

    std::vector<cv::Point2f> points;
    points.reserve(static_cast<size_t>(height) * 4);
    for (int y = 0; y < height; y++) {
        const int xMin = rowMin[static_cast<size_t>(y)];
        const int xMax = rowMax[static_cast<size_t>(y)];
        if (xMax < xMin) continue;

        const bool prevSame = (y > 0 &&
            rowMin[static_cast<size_t>(y - 1)] == xMin &&
            rowMax[static_cast<size_t>(y - 1)] == xMax);
        const bool nextSame = (y + 1 < height &&
            rowMin[static_cast<size_t>(y + 1)] == xMin &&
            rowMax[static_cast<size_t>(y + 1)] == xMax);
        if (prevSame && nextSame) continue;

        const float fy = static_cast<float>(y);
        const float fy1 = static_cast<float>(y + 1);
        const float fx0 = static_cast<float>(xMin);
        const float fx1 = static_cast<float>(xMax + 1);
        points.emplace_back(fx0, fy);
        points.emplace_back(fx1, fy);
        points.emplace_back(fx0, fy1);
        points.emplace_back(fx1, fy1);
    }

    if (points.size() >= 3) {
        rotatedRect = cv::minAreaRect(points);
        return true;
    }

    // 兜底路径：与原实现保持一致。
    cv::Mat binaryMask = MaskInfoToMat(maskInfo);
    if (binaryMask.empty()) {
        rotatedRect = cv::RotatedRect();
        return false;
    }
    return TryComputeMinAreaRectInplace(binaryMask, rotatedRect);
}

/// <summary>
/// 计算 RLE Mask 的非零面积（累加 runs 中奇数索引的长度）
/// </summary>
inline double CalculateMaskArea(const Json& maskInfo) {
    int width = 0;
    int height = 0;
    const Json::array_t* runs = nullptr;
    if (!TryReadMaskInfoHeader(maskInfo, width, height, runs)) return 0.0;

    long long area = 0;
    for (size_t i = 1; i < runs->size(); i += 2) {
        long long one = 0;
        if (!TryReadLongLike((*runs)[i], one)) continue;
        if (one > 0) area += one;
    }
    return static_cast<double>(area);
}

} // namespace flow
} // namespace dlcv_infer

