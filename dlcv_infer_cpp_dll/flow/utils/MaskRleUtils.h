#pragma once

#include <algorithm>
#include <cstdint>
#include <cstring>
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

/// <summary>
/// 从数字 RLE 信息还原单通道掩膜（CV_8UC1），1 段写入 255，0 段写入 0。
/// 期望字段：width(int), height(int), runs(int[])；首段为 0。
/// </summary>
inline cv::Mat MaskInfoToMat(const Json& maskInfo) {
    if (!maskInfo.is_object()) return cv::Mat();
    int width = 0;
    int height = 0;
    try { width = maskInfo.value("width", 0); } catch (...) { width = 0; }
    try { height = maskInfo.value("height", 0); } catch (...) { height = 0; }
    if (width <= 0 || height <= 0) return cv::Mat();
    if (!maskInfo.contains("runs") || !maskInfo.at("runs").is_array()) return cv::Mat();

    cv::Mat dst(height, width, CV_8UC1, cv::Scalar(0));
    if (!dst.isContinuous()) dst = dst.clone();
    const int total = width * height;
    uint8_t* basePtr = reinterpret_cast<uint8_t*>(dst.data);

    int idx = 0;
    int value = 0; // 首段为 0
    const Json& runsArr = maskInfo.at("runs");
    for (size_t i = 0; i < runsArr.size() && idx < total; i++) {
        int count = 0;
        try { count = runsArr.at(i).get<int>(); } catch (...) { count = 0; }
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
/// 计算 RLE Mask 的非零面积（累加 runs 中奇数索引的长度）
/// </summary>
inline double CalculateMaskArea(const Json& maskInfo) {
    if (!maskInfo.is_object()) return 0.0;
    if (!maskInfo.contains("runs") || !maskInfo.at("runs").is_array()) return 0.0;
    const Json& runs = maskInfo.at("runs");
    long long area = 0;
    for (size_t i = 1; i < runs.size(); i += 2) {
        try { area += runs.at(i).get<long long>(); } catch (...) {}
    }
    return static_cast<double>(area);
}

} // namespace flow
} // namespace dlcv_infer

