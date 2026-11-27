#pragma once
#include <functional>
#include <string>
#include <opencv2/core.hpp>

namespace dlcv_infer { namespace flow { namespace dpm {
struct EnhancementResult {
    cv::Mat image;
    std::string text, mode;
    float pixelsPerModule = 0;
    double s1Ms = 0, s2Ms = 0, s3Ms = 0, s4Ms = 0, enhanceMs = 0, decodeMs = 0, totalMs = 0;
};
EnhancementResult Enhance(const cv::Mat& gray,
    const std::function<std::string(const cv::Mat&)>& decode);
}}}
