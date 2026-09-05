#pragma once

#include "opencv2/core.hpp"
#include "opencv2/imgproc.hpp"

namespace dlcv_infer {
namespace image_input {

inline cv::Mat ConvertMatDepthTo8U(const cv::Mat& src) {
    if (src.empty()) {
        return {};
    }
    if (src.depth() == CV_8U) {
        return src;
    }

    cv::Mat dst;
    if (src.depth() == CV_16U) {
        src.convertTo(dst, CV_8U, 1.0 / 256.0);
        return dst;
    }
    if (src.depth() == CV_16S) {
        cv::normalize(src, dst, 0, 255, cv::NORM_MINMAX, CV_8U);
        return dst;
    }
    if (src.depth() == CV_32F || src.depth() == CV_64F) {
        double mn = 0.0;
        double mx = 0.0;
        cv::minMaxLoc(src, &mn, &mx);
        if (mx <= 1.0 + 1e-6 && mn >= -1e-6) {
            src.convertTo(dst, CV_8U, 255.0);
        } else {
            cv::normalize(src, dst, 0, 255, cv::NORM_MINMAX, CV_8U);
        }
        return dst;
    }

    src.convertTo(dst, CV_8U);
    return dst;
}

inline cv::Mat ConvertMatChannels(const cv::Mat& src, int expectedChannels) {
    if (src.empty()) {
        return {};
    }
    if (expectedChannels != 1 && expectedChannels != 3) {
        return src;
    }

    const int srcChannels = src.channels();
    if (srcChannels == expectedChannels) {
        return src;
    }

    cv::Mat converted;
    if (expectedChannels == 3) {
        if (srcChannels == 1) {
            cv::cvtColor(src, converted, cv::COLOR_GRAY2RGB);
        } else if (srcChannels == 4) {
            cv::cvtColor(src, converted, cv::COLOR_BGRA2RGB);
        }
    } else if (expectedChannels == 1) {
        if (srcChannels == 3) {
            cv::cvtColor(src, converted, cv::COLOR_RGB2GRAY);
        } else if (srcChannels == 4) {
            cv::cvtColor(src, converted, cv::COLOR_BGRA2GRAY);
        }
    }

    return converted.empty() ? src : converted;
}

inline cv::Mat NormalizeInferInputImage(const cv::Mat& src, int expectedChannels) {
    if (src.empty()) {
        return {};
    }

    cv::Mat normalizedDepth = ConvertMatDepthTo8U(src);
    return ConvertMatChannels(normalizedDepth, expectedChannels);
}

} // namespace image_input
} // namespace dlcv_infer
