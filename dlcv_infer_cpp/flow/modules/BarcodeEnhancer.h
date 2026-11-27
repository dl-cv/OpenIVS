#pragma once
#include <opencv2/core.hpp>

namespace dlcv_infer { namespace flow { namespace barcode {
cv::Mat Enhance(const cv::Mat& gray, int attempt);
} } }
