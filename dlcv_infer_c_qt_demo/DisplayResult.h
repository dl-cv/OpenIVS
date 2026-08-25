#pragma once

#include <string>
#include <vector>

#include <opencv2/core.hpp>

struct DisplayObjectResult {
    int categoryId = -1;
    std::string categoryName;
    float score = 0.0f;
    bool withBbox = false;
    std::vector<double> bbox;
    bool withMask = false;
    cv::Mat mask;
    bool withAngle = false;
    float angle = -100.0f;
    float area = 0.0f;
    bool withMean = false;
    double foregroundMean = 0.0;
    double backgroundMean = 0.0;
};

