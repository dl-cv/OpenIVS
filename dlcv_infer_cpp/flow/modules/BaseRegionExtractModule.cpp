#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include <opencv2/imgproc.hpp>
#include <array>
#include <limits>
#include <stdexcept>

namespace dlcv_infer {
namespace flow {
namespace {

int RoundEven(double value) {
    const double lower = std::floor(value);
    return static_cast<int>(lower + (value - lower > 0.5 || (value - lower == 0.5 && std::fmod(lower, 2) != 0) ? 1 : 0));
}

int ArgMax(const std::vector<float>& values, int begin, int end) {
    return static_cast<int>(std::max_element(values.begin() + begin, values.begin() + end) - values.begin());
}

double PercentileSorted(const std::vector<float>& values, double percentile) {
    const double index = (values.size() - 1) * percentile / 100;
    const size_t lower = static_cast<size_t>(index), upper = (std::min)(lower + 1, values.size() - 1);
    return values[lower] + (values[upper] - static_cast<double>(values[lower])) * (index - lower);
}

std::vector<float> Smooth(const std::vector<float>& values, int kernel) {
    cv::Mat input(1, static_cast<int>(values.size()), CV_32F, const_cast<float*>(values.data())), output;
    cv::GaussianBlur(input, output, cv::Size(kernel, 1), 0);
    return std::vector<float>(output.ptr<float>(), output.ptr<float>() + values.size());
}

// 对齐 NumPy float32 mean 的成对累加，避免凹槽峰值受求和精度影响。
float Sum(const std::vector<float>& values, int start, int count) {
    if (count < 8) { float sum = 0; for (int i = 0; i < count; ++i) sum += values[start + i]; return sum; }
    if (count > 128) { int half = count / 2; half -= half % 8; return Sum(values, start, half) + Sum(values, start + half, count - half); }
    std::array<float, 8> sums;
    std::copy_n(values.begin() + start, 8, sums.begin());
    int offset = 8;
    for (; offset < count - count % 8; offset += 8)
        for (int i = 0; i < 8; ++i) sums[i] += values[start + offset + i];
    float sum = ((sums[0] + sums[1]) + (sums[2] + sums[3])) + ((sums[4] + sums[5]) + (sums[6] + sums[7]));
    for (; offset < count; ++offset) sum += values[start + offset];
    return sum;
}

class BaseRegionExtractModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        ModuleIO output(imageList, resultList.is_array() ? resultList : Json::array());
        std::string category = ReadString("category_name", "底座");
        const auto begin = category.find_first_not_of(" \t\r\n"), end = category.find_last_not_of(" \t\r\n");
        category = begin == std::string::npos ? "底座" : category.substr(begin, end - begin + 1);
        for (size_t i = 0; i < imageList.size(); ++i) {
            const auto& image = imageList[i];
            if (image.ImageObject.empty()) continue;
            std::array<int, 4> box;
            try { box = ExtractBox(image.ImageObject); }
            catch (const std::invalid_argument&) { continue; }
            const auto state = image.TransformState.OriginalWidth > 0 ? image.TransformState : TransformationState(image.ImageObject.cols, image.ImageObject.rows);
            const Json detection{{"category_name", category}, {"category_id", 0}, {"score", 1.0},
                {"bbox", {box[0], box[1], box[2] - box[0], box[3] - box[1]}},
                {"width", box[2] - box[0]}, {"height", box[3] - box[1]}, {"with_angle", false}};
            output.ResultList.push_back({{"type", "local"}, {"originating_module", "post_process/base_region_extract"},
                {"index", i}, {"origin_index", image.OriginalIndex}, {"transform", state.ToJson()}, {"sample_results", Json::array({detection})}});
        }
        return output;
    }

private:
    double Number(const char* key, double fallback, double minimum, double maximum) const {
        const double value = ReadDouble(key, fallback);
        return (std::min)(maximum, (std::max)(minimum, std::isfinite(value) ? value : fallback));
    }

    std::array<int, 4> ExtractBox(const cv::Mat& image) const {
        const int h = image.rows, w = image.cols;
        if (h < 20 || w < 20) throw std::invalid_argument("图像尺寸过小");
        int kernel = static_cast<int>(Number("blur_kernel", 5, 1, (std::numeric_limits<int>::max)() - 1));
        if (kernel % 2 == 0) ++kernel;
        cv::Mat gray, smooth, gradient;
        // Flow 输入为 RGB，灰度结果与 Python 读盘的 BGR 图像保持一致。
        if (image.channels() == 1) gray = image;
        else cv::cvtColor(image, gray, image.channels() == 4 ? cv::COLOR_RGBA2GRAY : cv::COLOR_RGB2GRAY);
        cv::GaussianBlur(gray, smooth, cv::Size(kernel, kernel), 0);
        cv::Sobel(smooth, gradient, CV_32F, 1, 0, 3);
        std::vector<float> score(w), column(h);
        const double percentile = Number("edge_percentile", 90, 50, 99.9);
        for (int x = 0; x < w; ++x) {
            for (int y = 0; y < h; ++y) column[y] = std::abs(gradient.at<float>(y, x));
            std::sort(column.begin(), column.end());
            score[x] = static_cast<float>(PercentileSorted(column, percentile));
        }
        score = Smooth(score, 5);
        const int mid = w / 2, begin = (std::max)(1, RoundEven(w * 0.02));
        const int end = (std::max)(mid + 1, (std::min)(w - 1, RoundEven(w * 0.98)));
        const int left = ArgMax(score, begin, mid), outer = ArgMax(score, mid, end);
        int start = (std::max)(mid, outer - (std::max)(3, RoundEven(w * Number("right_pair_span_ratio", 0.05, 0.005, 0.2))));
        const double threshold = score[outer] * Number("right_edge_strength_ratio", 0.3, 0.01, 1);
        int right = outer;
        if (score[outer] > 1) {
            while (start < outer && score[start] < threshold) ++start;
            int stop = start + 1;
            while (stop <= outer && score[stop] >= threshold) ++stop;
            right = ArgMax(score, start, stop);
        }
        if (right - left < w * Number("min_width_ratio", 0.12, 0.01, 0.95) || (score[left] <= 1 && score[right] <= 1))
            throw std::invalid_argument("未找到可用的左右边界");
        std::vector<float> boundary(h, 0);
        for (int y = 0; y < h; ++y)
            for (int x = (std::max)(0, left - 2); x < (std::min)(w, left + 3); ++x)
                boundary[y] = (std::max)(boundary[y], std::abs(gradient.at<float>(y, x)));
        boundary = Smooth(boundary, 5);
        const float peak = boundary[ArgMax(boundary, 0, h)];
        if (peak <= 1) throw std::invalid_argument("左边界连续边线不足");
        cv::Mat rows(h, 1, CV_8U), closed;
        const double lineThreshold = peak * Number("line_strength_ratio", 0.25, 0.01, 1);
        for (int y = 0; y < h; ++y) rows.at<uchar>(y) = boundary[y] >= lineThreshold ? 1 : 0;
        const cv::Mat element = cv::Mat::ones((std::max)(1, RoundEven(h * 0.01)) * 2 + 1, 1, CV_8U);
        cv::morphologyEx(rows, closed, cv::MORPH_CLOSE, element);
        int top = -1, longest = 0, run = 0;
        for (int y = 0; y <= h; ++y) {
            if (y < h && closed.at<uchar>(y) != 0) ++run;
            else { if (run > longest) { longest = run; top = y - run; } run = 0; }
        }
        if (top < 0) throw std::invalid_argument("无法确定底座顶部范围");
        const int bottom = NotchTop(gray, left, right, top);
        if (bottom <= top) throw std::invalid_argument("无法确定底座凹槽上沿");
        return {left, top, right, bottom};
    }

    int NotchTop(const cv::Mat& gray, int left, int right, int top) const {
        const int h = gray.rows;
        cv::Mat reduced;
        cv::reduce(gray.colRange(left, right), reduced, 1, cv::REDUCE_AVG, CV_64F);
        std::vector<float> means(h);
        for (int y = 0; y < h; ++y) means[y] = static_cast<float>(reduced.at<double>(y));
        auto sorted = means; std::sort(sorted.begin(), sorted.end());
        const double threshold = (std::max)(10.0, (PercentileSorted(sorted, 90) - PercentileSorted(sorted, 10)) * Number("notch_drop_ratio", 0.15, 0.01, 1));
        const int band = (std::max)(2, RoundEven(h * Number("notch_band_ratio", 0.02, 0.005, 0.1)));
        std::vector<float> drop(h, 0);
        for (int y = band; y < h - band; ++y) drop[y] = Sum(means, y - band, band) / band - Sum(means, y, band) / band;
        drop = Smooth(drop, 15);
        int start = (std::max)(top + 1, RoundEven(h * Number("bottom_search_start_ratio", 0.55, 0, 0.95)));
        while (start < h && drop[start] <= threshold) ++start;
        if (start == h) throw std::invalid_argument("未找到底座下方凹槽上沿");
        int end = start + 1;
        while (end < h && drop[end] > threshold) ++end;
        const int peak = ArgMax(drop, start, end);
        return PercentileSorted(sorted, 50) >= Number("bright_product_threshold", 120, 0, 255)
            ? start + RoundEven((peak - start) * Number("bright_transition_position_ratio", 0.25, 0, 1)) : peak;
    }
};

} // namespace
DLCV_FLOW_REGISTER_MODULE("post_process/base_region_extract", BaseRegionExtractModule)
} // namespace flow
} // namespace dlcv_infer
