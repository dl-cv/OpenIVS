#include "DataMatrixNative.h"
#include <opencv2/calib3d.hpp>
#include <opencv2/imgproc.hpp>
#include <algorithm>
#include <array>
#include <cmath>
#include <limits>
#include <map>
#include <numeric>
#include <set>
#include <vector>

namespace {
#include "DataMatrixGrid.inl"

constexpr int side = 32;
using Signal = std::array<cv::Mat, 8>;
using Word = std::array<unsigned char, 98>;
const auto finder = Mask(side, 2);

Quad RoughBox(const cv::Mat &image) {
    const float left = image.cols * .08f, top = image.rows * .08f;
    const float right = image.cols * .92f, bottom = image.rows * .92f;
    return {{{left, top}, {right, top}, {right, bottom}, {left, bottom}}};
}
Quad Transform(const Quad &q, const cv::Mat &matrix) {
    Grid out;
    cv::transform(Grid(q.begin(), q.end()), out, matrix);
    return {{out[0], out[1], out[2], out[3]}};
}

bool FindPolygon(const cv::Mat &image, Quad seed, Quad &corners, bool nearbyAngles = false) {
    if (!cv::isContourConvex(Grid(seed.begin(), seed.end()))) return false;
    if (cv::contourArea(Grid(seed.begin(), seed.end()), true) < 0) std::swap(seed[1], seed[3]);
    Point center{};
    std::array<double, 4> lengths;
    for (int i = 0; i < 4; ++i) {
        center += seed[i] * .25f;
        lengths[i] = cv::norm(seed[(i + 1) % 4] - seed[i]);
    }
    std::sort(lengths.begin(), lengths.end());
    const float size = static_cast<float>((lengths[1] + lengths[2]) * .5);
    if (size < 16 || center.x < 0 || center.x >= image.cols || center.y < 0 || center.y >= image.rows) return false;
    const cv::Point origin(std::max(0, static_cast<int>(center.x - size * 1.28f)),
                           std::max(0, static_cast<int>(center.y - size * 1.28f)));
    const cv::Point end(std::min(image.cols, static_cast<int>(center.x + size * 1.28f)),
                        std::min(image.rows, static_cast<int>(center.y + size * 1.28f)));
    if (end.x - origin.x < 16 || end.y - origin.y < 16) return false;
    cv::Mat clean, small8, rotated;
    cv::medianBlur(image(cv::Rect(origin, end)), clean, 5);
    cv::resize(clean, small8, {}, .5, .5, cv::INTER_AREA);
    const auto small = Float(small8);
    const Point local = (center - Point(origin)) * .5f;
    const cv::Point peak(cvRound(local.x), cvRound(local.y));
    const int radius = static_cast<int>(size * .34f);
    const auto region = cv::Rect(peak.x - radius, peak.y - radius, 2 * radius + 1, 2 * radius + 1)
                        & cv::Rect(0, 0, small.cols, small.rows);
    if (region.empty()) return false;
    std::vector<int> widths;
    int padding = 0;
    for (double width = size * .4; width < size * .61; width += 3) {
        const int n = cvRound(width), band = std::max(2, cvRound(n / 23.0));
        widths.push_back(n);
        padding = std::max(padding, (n + 2 * band) / 2);
    }
    const auto direction = seed[1] - seed[0];
    const int nearestAngle = (cvRound(std::atan2(direction.y, direction.x) * 180 / CV_PI / 3) % 30 + 30) % 30;
    double best = -std::numeric_limits<double>::infinity();
    for (int angle = 0; angle < 90; angle += 3) {
        const int distance = (angle / 3 - nearestAngle + 30) % 30;
        if (nearbyAngles && distance > 1 && distance < 29) continue;
        const auto rot = cv::getRotationMatrix2D(local, angle, 1);
        cv::warpAffine(small, rotated, rot, small.size(), cv::INTER_LINEAR, cv::BORDER_REPLICATE);
        cv::Mat padded, integral;
        cv::copyMakeBorder(rotated, padded, padding, padding, padding, padding, cv::BORDER_REFLECT_101);
        cv::integral(padded, integral, CV_64F);
        // 按行扫描候选中心，只保留当前最大值及其位置。
        for (int n : widths) {
            const int band = std::max(2, cvRound(n / 23.0));
            const int inner = n - 2 * band, outer = n + 2 * band;
            if (inner < 1) continue;
            const double outsideScale = 1.0 / (outer * outer - n * n);
            const double insideScale = 1.0 / (n * n - inner * inner);
            double value = -std::numeric_limits<double>::infinity();
            cv::Point position;
            for (int y = 0; y < region.height; ++y) {
                std::array<const double*, 3> top, bottom;
                const std::array<int, 3> lengths{inner, n, outer};
                for (int k = 0; k < 3; ++k) {
                    const int row = region.y + y + padding - lengths[k] / 2;
                    top[k] = integral.ptr<double>(row);
                    bottom[k] = integral.ptr<double>(row + lengths[k]);
                }
                for (int x = 0; x < region.width; ++x) {
                    std::array<float, 3> sums;
                    for (int k = 0; k < 3; ++k) {
                        const int left = region.x + x + padding - lengths[k] / 2;
                        sums[k] = static_cast<float>(bottom[k][left + lengths[k]] - top[k][left + lengths[k]] - bottom[k][left] + top[k][left]);
                    }
                    const float outside = static_cast<float>(sums[2] * outsideScale - sums[1] * outsideScale);
                    const float inside = static_cast<float>(sums[1] * insideScale - sums[0] * insideScale);
                    const float score = outside - inside;
                    if (score > value) { value = score; position = {x, y}; }
                }
            }
            if (value <= best) continue;
            best = value;
            position += region.tl();
            const Point p(position), d(n * .5f, n * .5f);
            cv::Mat inverse;
            cv::invertAffineTransform(rot, inverse);
            corners = Transform({{p - d, p + Point(d.x, -d.y), p + d, p + Point(-d.x, d.y)}}, inverse);
            for (auto &q : corners) q *= 2.f;
        }
    }
    if (best < 8) return false;
    const auto unordered = corners;
    best = std::numeric_limits<double>::infinity();
    for (int i = 0; i < 4; ++i) {
        double error = 0;
        for (int j = 0; j < 4; ++j) {
            const Point d = unordered[(i + j) % 4] + Point(origin) - seed[j];
            error += d.dot(d);
        }
        if (error < best) {
            best = error;
            for (int j = 0; j < 4; ++j) corners[j] = unordered[(i + j) % 4];
        }
    }
    const auto data = Float(Blur(clean, .9));
    std::array<Point, 4> normals;
    std::array<float, 4> offsets;
    for (int i = 0; i < 4; ++i) {
        const Point p = corners[i], q = corners[(i + 1) % 4], delta = q - p;
        const double length = cv::norm(delta);
        if (length < 1) return false;
        const Point normal = Point(-delta.y, delta.x) * static_cast<float>(1 / length);
        const Point mid = (p + q) * .5f;
        std::array<float, 96> times;
        std::array<Point, 96> base;
        for (int j = 0; j < 96; ++j) {
            times[j] = static_cast<float>(-.43 + .86 * j / 95);
            base[j] = mid + delta * times[j];
        }
        const auto choose = [&](double shift0, double shift1, double step, double slope0, double slope1,
                                double stepSlope, float near, bool coarse) {
            double highest = -std::numeric_limits<double>::infinity();
            Point selected{};
            const Point nearOffset = normal * near, farOffset = normal * 6.f, edgeOffset = normal * 12.f;
            for (double a = slope0; a < slope1; a += stepSlope) {
                std::array<double, 96> slopes;
                for (int j = 0; j < 96; ++j) slopes[j] = a * times[j];
                for (double s = shift0; s < shift1; s += step) {
                    double f = 0, far = 0, edge = 0;
                    for (int j = 0; j < 96; ++j) {
                        const auto at = base[j] + normal * static_cast<float>(s + slopes[j]);
                        const float outside = Pixel(data, at - nearOffset, true);
                        f += outside - Pixel(data, at + nearOffset, true);
                        if (coarse) {
                            far += Pixel(data, at - farOffset, true) - Pixel(data, at + farOffset, true);
                            edge += Pixel(data, at - edgeOffset, true) - outside;
                        }
                    }
                    const double score = f + .7 * far - .45 * std::max(0., edge);
                    if (score > highest) { highest = score; selected = Point(static_cast<float>(s), static_cast<float>(a)); }
                }
            }
            return selected;
        };
        const float range = size * .12f;
        auto fit = choose(-range, range, 1, -range, range, 2, 2.4f, true);
        fit = choose(fit.x - 2.25, fit.x + 2.26, .25, fit.y - 3, fit.y + 3.01, .5, 1.7f, false);
        const auto direction = delta + normal * fit.y;
        normals[i] = Point(direction.y, -direction.x) * static_cast<float>(1 / cv::norm(direction));
        offsets[i] = normals[i].dot(mid + normal * fit.x);
    }
    for (int i = 0; i < 4; ++i) {
        const int j = (i + 3) % 4;
        const auto normal = cv::Matx22f(normals[j].x, normals[j].y, normals[i].x, normals[i].y);
        cv::Vec2f out;
        if (!cv::solve(normal, cv::Vec2f(offsets[j], offsets[i]), out)) return false;
        corners[i] = Point(out) + Point(origin);
    }
    corners = Order(corners);
    return cv::isContourConvex(Grid(corners.begin(), corners.end()));
}

bool GetGrids(const cv::Mat &a, const Quad &quad, bool rotate, std::array<Grid, 4> &grids) {
    const auto im = Float(a), low = Blur(im, 1);
    const auto g = Score(low - Blur(low, 4), quad);
    if (g.n != side || g.regions != 2 || (!rotate && g.rot != 0)) return false;
    grids[0] = Optimize(im, g);
    grids[1] = Refine(im, grids[0], finder, side);
    grids[2] = grids[0];
    // 格线采样前先锐化，再分别拟合各区域。
    const auto sharp = Sharpen(low, 2, 2.8);
    FitRegionsFromSignal(sharp, grids[2], side, 2);
    grids[3] = Refine(im, grids[2], finder, side);
    return true;
}

cv::Mat Rectify(const cv::Mat &a, const Quad &q, int size, Quad &target, cv::Mat &transform) {
    constexpr float pad = 14;
    const float end = pad + static_cast<float>(size);
    target = {{{pad, pad}, {end, pad}, {end, end}, {pad, end}}};
    transform = cv::getPerspectiveTransform(q.data(), target.data());
    cv::Mat out;
    cv::warpPerspective(a, out, transform, {size + 28, size + 28}, cv::INTER_CUBIC);
    return out;
}

void ProjectGrid(Grid &g, const cv::Mat &h) {
    Grid replacement;
    cv::perspectiveTransform(g, replacement, h);
    g.swap(replacement);
}

cv::Mat Pool(const cv::Mat &image, bool center) {
    cv::Mat out(side, side, CV_32F);
    for (int y = 0; y < side; ++y)
        for (int x = 0; x < side; ++x) {
            float sum = 0;
            const int start = center ? 1 : 0, stop = center ? 3 : 4;
            for (int j = start; j < stop; ++j)
                for (int i = start; i < stop; ++i)
                    sum += image.at<float>(y * 4 + j, x * 4 + i);
            out.at<float>(y, x) = sum * (center ? .25f : .0625f);
        }
    return out;
}

cv::Mat Normalize(const cv::Mat &a, const Grid &grid) {
    cv::Mat padding(side + 2, side + 2, CV_32FC2);
    for (int y = 0; y < side; ++y) {
        auto *p = padding.ptr<Point>(y + 1);
        std::copy_n(grid.data() + y * side, side, p + 1);
        p[0] = 2.f * p[1] - p[2]; p[side + 1] = 2.f * p[side] - p[side - 1];
    }
    const cv::Mat head = 2 * padding.row(1) - padding.row(2), tail = 2 * padding.row(side) - padding.row(side - 1);
    head.copyTo(padding.row(0)); tail.copyTo(padding.row(side + 1));
    cv::Mat coordinates(side * 4, side * 4, CV_32FC2);
    for (int y = 0; y < coordinates.rows; ++y)
        for (int x = 0; x < coordinates.cols; ++x)
            coordinates.at<Point>(y, x) = Point((x + .5f) / 4 + .5f, (y + .5f) / 4 + .5f);
    cv::Mat remap, values;
    cv::remap(padding, remap, coordinates, {}, cv::INTER_LINEAR);
    cv::remap(a, values, remap, {}, cv::INTER_CUBIC);
    auto values32 = Float(values);
    const auto pooled = Pool(values32, false);
    std::vector<float> black, white;
    for (int i = 0; i < side * side; ++i) {
        if (finder[i] < 0) black.push_back(pooled.ptr<float>()[i]);
        if (finder[i] > 0) white.push_back(pooled.ptr<float>()[i]);
    }
    const auto lo = Percentile(std::move(black), .5), hi = Percentile(std::move(white), .5);
    return (values32 - lo) / std::max(15., hi - lo);
}

Signal MakeSignal(const cv::Mat &a, const Grid &g) {
    const auto raw = Normalize(a, g);
    std::array<cv::Mat, 4> data{raw};
    cv::medianBlur(raw, data[1], 3);
    cv::morphologyEx(raw, data[2], cv::MORPH_OPEN, cv::getStructuringElement(cv::MORPH_ELLIPSE, {3, 3}));
    cv::morphologyEx(raw, data[3], cv::MORPH_OPEN, cv::Mat::ones(3, 3, CV_8U));
    Signal out;
    const auto normalize = [](const cv::Mat &v, const std::pair<cv::Mat, cv::Mat> &range) {
        cv::Mat z, span, result;
        v.convertTo(z, range.first.type());
        span = range.second - range.first;
        cv::max(span, .05, span);
        cv::divide(z - range.first, span, result);
        result -= .5;
        return Float(result);
    };
    for (int i = 0; i < 4; ++i) {
        const auto pooled = Pool(Sharpen(Blur(data[i], .82), 1.8, 2.4), true);
        out[2 * i] = normalize(pooled, Surface(pooled, finder, side));
        out[2 * i + 1] = normalize(pooled, Quantiles(pooled, side));
    }
    return out;
}
using Signals = std::vector<Signal>;
using Fields = std::vector<cv::Mat>;

template <class Get> cv::Mat Median(int count, Get get) {
    cv::Mat out(side, side, CV_32F);
    std::vector<float> values(count);
    for (int i = 0; i < side * side; ++i) {
        for (int j = 0; j < count; ++j) values[j] = get(j, i);
        const auto it = values.begin() + count / 2;
        std::nth_element(values.begin(), it, values.end());
        out.ptr<float>()[i] = count % 2 ? *it : (*it + *std::max_element(values.begin(), it)) * .5f;
    }
    return out;
}

cv::Mat MedianSlice(const Signals &z, int f0, int f1, int fstep, int c0 = 0, int c1 = 8) {
    const int channels = c1 - c0;
    return Median((f1 - f0 + fstep - 1) / fstep * channels, [&](int j, int i) {
        return z[f0 + j / channels * fstep][c0 + j % channels].ptr<float>()[i];
    });
}
cv::Mat Uncertainty(const Signals &z, const cv::Mat &median, int scale) {
    const auto mad = Median(static_cast<int>(z.size() * 8), [&](int j, int i) {
        return std::abs(z[j / 8][j % 8].ptr<float>()[i] - median.ptr<float>()[i]);
    });
    return 1 + scale * (mad + Blur(mad, 1.4));
}

std::pair<Fields, cv::Mat> FirstSignals(const Signals &z) {
    const auto mid = MedianSlice(z, 0, 12, 1);
    Fields p(65);
    p[0] = mid;
    p[2] = MedianSlice(z, 0, 12, 1, 4);
    p[7] = MedianSlice(z, 0, 12, 1, 4, 5);
    p[9] = MedianSlice(z, 0, 12, 1, 6, 7);
    p[10] = MedianSlice(z, 0, 12, 1, 7, 8);
    p[14] = MedianSlice(z, 0, 4, 1, 4);
    p[25] = MedianSlice(z, 8, 9, 1);
    p[30] = MedianSlice(z, 1, 2, 1, 4);
    p[34] = MedianSlice(z, 5, 6, 1, 4);
    p[48] = MedianSlice(z, 0, 4, 1, 7, 8);
    p[64] = MedianSlice(z, 8, 12, 1, 7, 8);
    return {std::move(p), Uncertainty(z, mid, 3)};
}
std::pair<Fields, cv::Mat> OtherSignals(const Signals &z) {
    const auto mid = MedianSlice(z, 0, 56, 1);
    Fields p(14);
    p[0] = mid;
    p[6] = MedianSlice(z, 0, 56, 1, 5, 6);
    p[7] = MedianSlice(z, 0, 56, 1, 6, 7);
    p[8] = MedianSlice(z, 0, 56, 1, 7, 8);
    p[13] = MedianSlice(z, 32, 40, 1);
    return {std::move(p), Uncertainty(z, mid, 4)};
}
using Observations = std::map<Word, std::set<Word>>;

bool Confirm(const std::pair<Fields, cv::Mat> &signals, std::initializer_list<int> order,
             int iterations, int seed, Observations &seen, DlcvDataMatrixVerify verify, void *context) {
    std::set<Word> attempted;
    for (int index : order) {
        cv::Mat soft;
        cv::divide(signals.first[index], signals.second, soft);
        const auto *p = soft.ptr<float>();
        // 跳过固定格线，只按码字区域判断两次硬判是否相同。
        Word signs{};
        for (int i = 0, j = 0; i < side * side; ++i) if (!finder[i]) {
            signs[j / 8] |= static_cast<unsigned char>((p[i] < 0) << (7 - j % 8));
            ++j;
        }
        if (!attempted.insert(signs).second) continue;
        Word word{}, raw{};
        std::array<unsigned char, 38 * 38> bitmap{};
        if (!dlcv_datamatrix_recover_v1(p, iterations, seed + 13 * index, word.data(), raw.data(), bitmap.data())) continue;
        auto &matches = seen[word];
        matches.insert(raw);
        if (matches.size() >= 2 && verify(bitmap.data(), context)) return true;
    }
    return false;
}

bool FirstFrames(const cv::Mat &a, const cv::Mat &original, const cv::Mat &mapping, bool rotate,
                 Signals &z, Quad &output, DlcvDataMatrixVerify verify = nullptr, void* context = nullptr, bool* earlyFound = nullptr, bool nearbyAngles = false) {
    const auto rough = RoughBox(a);
    Quad tight{};
    if (!FindPolygon(a, rough, tight, nearbyAngles)) return false;
    std::array<Grid, 4> grids;
    if (!GetGrids(a, tight, rotate, grids)) return false;
    output = MapCorners(grids[0], side, cv::Mat::eye(3, 3, CV_64F));
    if (!cv::isContourConvex(Grid(output.begin(), output.end()))) return false;
    for (const auto &g : grids) z.push_back(MakeSignal(a, g));
    if (verify && earlyFound) {
        Fields fields(49);
        const auto mid = MedianSlice(z, 0, 4, 1);
        fields[14] = MedianSlice(z, 0, 4, 1, 4);
        fields[30] = MedianSlice(z, 1, 2, 1, 4);
        fields[48] = MedianSlice(z, 0, 4, 1, 7, 8);
        Observations seen;
        if (Confirm({std::move(fields), Uncertainty(z, mid, 3)}, {14,30,48}, 1, 7653, seen, verify, context)) {
            *earlyFound = true;
            return true;
        }
    }
    const bool same = a.data == original.data && cv::norm(mapping, cv::Mat::eye(2, 3, CV_32F)) == 0;
    std::vector<Quad> quads;
    if (!same) {
        Quad fitted{};
        if (!FindPolygon(original, Transform(rough, mapping), fitted, nearbyAngles)) return false;
        quads.push_back(Order(fitted));
    }
    quads.push_back(Order(Transform(tight, mapping)));
    for (const auto& q : quads) {
        Quad target{};
        cv::Mat transform;
        const auto canvas = Rectify(original, q, 176, target, transform);
        if (!GetGrids(canvas, target, rotate, grids)) return false;
        const cv::Mat inverse = transform.inv();
        std::array<Signal, 4> group;
        for (int i = 0; i < 4; ++i) {
            ProjectGrid(grids[i], inverse);
            group[i] = MakeSignal(original, grids[i]);
        }
        z.insert(z.end(), group.begin(), group.end());
        if (same) z.insert(z.end(), group.begin(), group.end());
    }
    return true;
}

struct RecoveryState {
    Quad corners{};
    Observations seen;
    bool ready = false;
};

bool TryLight(const cv::Mat &a, const cv::Mat &original, const cv::Mat &mapping, bool rotate,
              float *corners, DlcvDataMatrixVerify verify, void *context, bool nearbyAngles,
              int stage, RecoveryState& state) {
    Signals z;
    z.reserve(56);
    const auto found = [&] {
        for (int i = 0; i < 4; ++i) {
            corners[2 * i] = state.corners[i].x;
            corners[2 * i + 1] = state.corners[i].y;
        }
        return true;
    };
    auto& seen = state.seen;
    if (stage == 0) {
        bool earlyFound = false;
        state.ready = FirstFrames(a, original, mapping, rotate, z, state.corners, verify, context, &earlyFound, nearbyAngles);
        if (!state.ready) return false;
        if (earlyFound) return found();
        const auto fields = FirstSignals(z);
        return (Confirm(fields, {0,2,7,9,10,25,34,48,64,14}, 8, 3321, seen, verify, context) ||
            Confirm(fields, {30,34,64,2}, 12, 7653, seen, verify, context)) && found();
    }
    if (!state.ready) return false;
    const auto resample = [&](double scale, std::initializer_list<int> order, int iterations, int seed) {
        cv::Mat scaled;
        cv::resize(a, scaled, {}, scale, scale, cv::INTER_CUBIC);
        Signals augmented;
        Quad scaledCorners{};
        return FirstFrames(scaled, scaled, cv::Mat::eye(2, 3, CV_32F), rotate, augmented, scaledCorners, nullptr, nullptr, nullptr, nearbyAngles) &&
            Confirm(FirstSignals(augmented), order, iterations, seed, seen, verify, context);
    };
    // 改变采样相位，让靠近格线的采样点得到另一组置信度。
    if (stage == 1) return resample(1.1, {34,48,14}, 12, 7653) && found();

    const auto rough = RoughBox(a);
    std::array<Quad, 2> sourceQuads;
    std::array<Grid, 4> grids;
    Quad target{};
    cv::Mat transform;
    z.clear();
    if (!FindPolygon(original, Transform(rough, mapping), sourceQuads[0], nearbyAngles)) return false;
    Point center{};
    for (const auto &p : rough) center += p * .25f;
    auto shifted = rough;
    // 移动粗框后重新拟合，避免所有网格继承同一处边界偏差。
    for (auto &p : shifted) p = center + (p - center) * .9613578f + Point(-1.7452953f, -5.8736502f);
    if (!FindPolygon(original, Transform(shifted, mapping), sourceQuads[1], nearbyAngles)) return false;
    const std::array<int, 7> sizes{160,176,192,224,144,160,224};
    for (size_t i = 0; i < sizes.size(); ++i) {
        const auto canvas = Rectify(original, sourceQuads[i % 2], sizes[i], target, transform);
        if (!GetGrids(canvas, target, rotate, grids)) return false;
        const cv::Mat inverse = transform.inv();
        for (auto &g : grids) {
            z.push_back(MakeSignal(canvas, g));
            ProjectGrid(g, inverse);
            z.push_back(MakeSignal(original, g));
        }
    }
    return Confirm(OtherSignals(z), {0,7,6,8,13}, 16, 4421, seen, verify, context) && found();
}
} // namespace

int __cdecl dlcv_datamatrix_read_v2(const unsigned char *gray, int width, int height, int stride,
    const unsigned char *source, int sourceWidth, int sourceHeight, int sourceStride, const float *roiToOriginal,
    int flags, float *corners, DlcvDataMatrixVerify verify, void *context) {
    if (!gray || !corners || !verify || width < side || height < side || stride < width || (flags & ~3)) return 0;
    try {
        const cv::Mat a(height, width, CV_8U, const_cast<unsigned char *>(gray), stride);
        cv::Mat original = a, mapping = cv::Mat::eye(2, 3, CV_32F);
        if (source) {
            if (!roiToOriginal || sourceWidth < side || sourceHeight < side || sourceStride < sourceWidth) return 0;
            for (int i = 0; i < 6; ++i) if (!std::isfinite(roiToOriginal[i])) return 0;
            original = cv::Mat(sourceHeight, sourceWidth, CV_8U, const_cast<unsigned char*>(source), sourceStride);
            mapping = cv::Mat(2, 3, CV_32F, const_cast<float*>(roiToOriginal));
        }
        double low = 0, high = 0;
        cv::minMaxLoc(a, &low, &high);
        if (high - low < 5) return 0;
        const bool rotate = (flags & 1) != 0;
        const auto read = [&](const cv::Mat &image, const cv::Mat &full, bool nearbyAngles = false) {
            std::array<RecoveryState, 2> states;
            cv::Mat reversed, reverseMap;
            if (rotate) {
                cv::rotate(image, reversed, cv::ROTATE_180);
                reverseMap = mapping.clone();
                for (int row = 0; row < 2; ++row) {
                    reverseMap.at<float>(row, 2) += mapping.at<float>(row, 0) * (width - 1) + mapping.at<float>(row, 1) * (height - 1);
                    reverseMap.at<float>(row, 0) *= -1; reverseMap.at<float>(row, 1) *= -1;
                }
            }
            // 两个方向各完成初始与重采样后，再进入较长的多组采样。
            const std::array<std::pair<int, int>, 6> stages{{{0,0}, {0,1}, {1,0}, {1,1}, {0,2}, {1,2}}};
            for (const auto& entry : stages) {
                const int direction = entry.first, stage = entry.second;
                if (direction == 0) {
                    if (TryLight(image, full, mapping, rotate, corners, verify, context, nearbyAngles, stage, states[0])) return true;
                } else if (rotate && TryLight(reversed, source ? full : reversed,
                    source ? reverseMap : cv::Mat::eye(2, 3, CV_32F), true, corners, verify, context, nearbyAngles, stage, states[1])) {
                    for (int i = 0; i < 8; i += 2) {
                        corners[i] = static_cast<float>(width - 1) - corners[i];
                        corners[i + 1] = static_cast<float>(height - 1) - corners[i + 1];
                    }
                    return true;
                }
            }
            return false;
        };
        // 优先使用输入四角的方向；双采样校验未通过时仍执行完整角度搜索。
        try {
            if (read(a, original, true)) return 1;
        } catch (const std::exception&) {
        }
        if (read(a, original)) return 1;
        if (flags & 2) {
            cv::Mat negative;
            cv::bitwise_not(a, negative);
            cv::Mat negativeOriginal;
            if (source) cv::bitwise_not(original, negativeOriginal);
            else negativeOriginal = negative;
            if (read(negative, negativeOriginal)) return 1;
        }
    } catch (const std::exception &) {
        return 0;
    }
    return 0;
}

int __cdecl dlcv_datamatrix_read_v1(const unsigned char *gray, int width, int height, int stride,
    int flags, float *corners, DlcvDataMatrixVerify verify, void *context) {
    return dlcv_datamatrix_read_v2(gray, width, height, stride, nullptr, 0, 0, 0, nullptr, flags, corners, verify, context);
}
