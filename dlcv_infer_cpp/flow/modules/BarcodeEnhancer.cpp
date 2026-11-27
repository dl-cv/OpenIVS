#include "BarcodeEnhancer.h"
#include <opencv2/imgproc.hpp>
#include <algorithm>
#include <cmath>
#include <vector>

namespace dlcv_infer { namespace flow { namespace barcode {
namespace {
// Math.Round / np.rint 的偶数舍入，不使用 std::round 的远离零舍入。
int RoundEven(double value) {
    double low = std::floor(value), fraction = value - low;
    return static_cast<int>(fraction < .5 ? low : fraction > .5 ? low + 1 :
        (static_cast<int>(low) % 2 == 0 ? low : low + 1));
}
double Energy(const cv::Mat& clipped, double shear, std::vector<float>& acc) {
    std::fill(acc.begin(), acc.end(), 0.f);
    for (int y = 0; y < clipped.rows; ++y) {
        int shift = RoundEven((y - (clipped.rows - 1)*.5) * (shear / clipped.rows));
        const auto* row = clipped.ptr<uint8_t>(y);
        for (int x = 0; x < clipped.cols; ++x)
            acc[x] += row[std::max(0, std::min(clipped.cols - 1, x + shift))];
    }
    double energy = 0;
    for (size_t x = 1; x < acc.size(); ++x) { double d = acc[x] - acc[x-1]; energy += d*d; }
    return energy;
}
}

cv::Mat Enhance(const cv::Mat& gray, int attempt) {
    int w = gray.cols, h = gray.rows;
    bool upper = attempt == 2 || attempt == 4, softer = attempt == 3;
    int y0 = static_cast<int>(h * (upper ? .05 : softer ? .18 : .22));
    int y1 = std::min(h, std::max(y0 + 1, static_cast<int>(h * (upper ? .55 : softer ? .80 : .78))));
    int bh = y1 - y0, uw = w * 2;
    cv::Mat band = gray(cv::Rect(0, y0, w, bh)), clipped;
    cv::min(band, 200, clipped);
    std::vector<float> acc(w);
    double best = 0, score = -1;
    for (int i = 0; i < 9; ++i) {
        double shear = -6 + i * 1.5, energy = Energy(clipped, shear, acc);
        if (energy > score) { score = energy; best = shear; }
    }
    double center = best;
    for (double delta : {-.75, -.375, .375, .75}) {
        double shear = center + delta, energy = Energy(clipped, shear, acc);
        if (energy > score) { score = energy; best = shear; }
    }
    cv::Mat up;
    cv::resize(band, up, cv::Size(uw, bh), 0, 0, cv::INTER_CUBIC);
    std::vector<int> shifts(bh);
    for (int y = 0; y < bh; ++y) shifts[y] = RoundEven((y - (bh - 1)*.5) * (best * 2 / bh));
    int k = RoundEven((attempt == 1 ? .50 : softer ? .45 : .35) * (bh - 1));
    std::vector<uint8_t> column(bh);
    cv::Mat profile(1, uw, CV_32FC1), smooth, blur;
    for (int x = 0; x < uw; ++x) {
        for (int y = 0; y < bh; ++y) column[y] = up.ptr<uint8_t>(y)[std::max(0, std::min(uw - 1, x + shifts[y]))];
        std::nth_element(column.begin(), column.begin() + k, column.end());
        profile.at<float>(0, x) = column[k];
    }
    cv::GaussianBlur(profile, smooth, cv::Size(), softer ? 2.8 : 1.6, 0);
    cv::GaussianBlur(smooth, blur, cv::Size(), softer ? 9.0 : 7.0, 0);
    std::vector<float> sharp(uw);
    for (int x = 0; x < uw; ++x) sharp[x] = smooth.at<float>(0, x) + (softer ? 1.4f : 1.8f) * (smooth.at<float>(0, x) - blur.at<float>(0, x));
    auto sorted = sharp;
    std::sort(sorted.begin(), sorted.end());
    float low = sorted[static_cast<int>(.10 * (uw - 1))];
    float span = std::max(sorted[static_cast<int>(.90 * (uw - 1))] - low, 1.f);
    cv::Mat output(268, uw + 160, CV_8UC1, cv::Scalar(255));
    const float thresholds[] = {.18f, .22f, .26f, .30f, .34f};
    const float softThresholds[] = {.22f, .28f, .34f, .40f, .46f};
    for (int index = 0; index < 5; ++index) {
        float threshold = low + (softer ? softThresholds[index] : thresholds[index]) * span;
        for (int y = 24 + index*44; y < 68 + index*44; ++y) {
            auto* row = output.ptr<uint8_t>(y) + 80;
            for (int x = 0; x < uw; ++x) row[x] = sharp[x] >= threshold ? 255 : 0;
        }
    }
    if (attempt >= 3)
        cv::dilate(output, output, cv::getStructuringElement(cv::MORPH_RECT, cv::Size(attempt == 3 ? 2 : 3, 1)));
    return output;
}
} } }
