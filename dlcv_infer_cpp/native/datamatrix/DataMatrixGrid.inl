using Point = cv::Point2f;
using Quad = std::array<Point, 4>;
using Grid = std::vector<Point>;
using Params = std::array<double, 8>;
struct Geometry {
    double score = -1e9;
    int n = 0, regions = 0, rot = 0;
    Quad corners{};
};
const Quad target{{{20, 20}, {200, 20}, {200, 200}, {20, 200}}};
const std::pair<int, int> sizes[] = {{10, 1}, {12, 1}, {14, 1}, {16, 1}, {18, 1},  {20, 1},  {22, 1},  {24, 1},
                                     {26, 1}, {32, 2}, {36, 2}, {40, 2}, {44, 2},  {48, 2},  {52, 2},  {64, 4},
                                     {72, 4}, {80, 4}, {88, 4}, {96, 4}, {104, 4}, {120, 6}, {132, 6}, {144, 6}};

cv::Mat Blur(const cv::Mat &a, double sigma) {
    cv::Mat b;
    cv::GaussianBlur(a, b, {}, sigma);
    return b;
}
cv::Mat Float(const cv::Mat &a) {
    cv::Mat b;
    a.convertTo(b, CV_32F);
    return b;
}
cv::Mat Open(const cv::Mat &a) {
    cv::Mat b;
    cv::morphologyEx(a, b, cv::MORPH_OPEN, cv::Mat::ones(3, 3, CV_8U));
    return b;
}
Quad Order(Quad p) {
    Point center{};
    for (auto q : p)
        center += q * .25f;
    std::sort(p.begin(), p.end(), [&](Point a, Point b) {
        return std::atan2(a.y - center.y, a.x - center.x) < std::atan2(b.y - center.y, b.x - center.x);
    });
    std::rotate(p.begin(),
                std::min_element(p.begin(), p.end(), [](Point a, Point b) { return a.x + a.y < b.x + b.y; }),
                p.end());
    return p;
}
cv::Mat Projection(const Quad &q) {
    const auto p = Order(q);
    return cv::getPerspectiveTransform(p.data(), target.data());
}
cv::Mat Rect(const cv::Mat &a, const cv::Mat &projection) {
    cv::Mat b;
    cv::warpPerspective(a, b, projection, {220, 220}, cv::INTER_CUBIC);
    return b;
}
Quad ImageQuad(int w, int h, int pad = 0) {
    const float x = static_cast<float>(pad), r = x + w - 1, b = x + h - 1;
    return {{{x, x}, {r, x}, {r, b}, {x, b}}};
}
int RotIndex(int y, int x, int n, int rot) {
    switch (rot & 3) {
    case 1:
        return (n - 1 - x) * n + y;
    case 2:
        return (n - 1 - y) * n + n - 1 - x;
    case 3:
        return x * n + n - 1 - y;
    default:
        return y * n + x;
    }
}
Grid RotateGrid(const Grid &p, int n, int rot) {
    Grid q(p.size());
    for (int y = 0; y < n; ++y)
        for (int x = 0; x < n; ++x)
            q[y * n + x] = p[RotIndex(y, x, n, rot)];
    return q;
}
std::vector<int> Mask(int n, int k, int rot = 0) {
    std::vector<int> m(n * n);
    const int s = n / k;
    for (int y = 0; y < n; y += s)
        for (int x = 0; x < n; x += s) {
            for (int i = 0; i < s; ++i)
                m[RotIndex(y, x + i, n, rot)] = i % 2 ? 1 : -1;
            for (int i = 0; i < s; ++i)
                m[RotIndex(y + i, x + s - 1, n, rot)] = i % 2 ? -1 : 1;
            for (int i = 0; i < s; ++i)
                m[RotIndex(y + s - 1, x + i, n, rot)] = -1;
            for (int i = 0; i < s; ++i)
                m[RotIndex(y + i, x, n, rot)] = -1;
        }
    return m;
}
Point Cell(const Quad &q, float x, float y) {
    return (q[0] * (1 - x) + q[1] * x) * (1 - y) + (q[3] * (1 - x) + q[2] * x) * y;
}
Grid Maps(int n, const Quad &q) {
    Grid result(n * n);
    for (int y = 0; y < n; ++y)
        for (int x = 0; x < n; ++x)
            result[y * n + x] = Cell(q, (x + .5f) / n, (y + .5f) / n);
    return result;
}
// OpenCV 双线性插值将小数坐标舍入到 1/32 像素。
float Pixel(const cv::Mat &a, Point p, bool replicate = false) {
    const int ix = cvRound(p.x * 32), iy = cvRound(p.y * 32), x = ix >> 5, y = iy >> 5;
    const float u = (ix & 31) * (1.f / 32), v = (iy & 31) * (1.f / 32);
    if (x >= 0 && x < a.cols - 1 && y >= 0 && y < a.rows - 1) {
        const float *r = a.ptr<float>(y), *s = a.ptr<float>(y + 1);
        return r[x] * ((1 - u) * (1 - v)) + r[x + 1] * (u * (1 - v)) + s[x] * ((1 - u) * v) + s[x + 1] * (u * v);
    }
    const auto at = [&](int xx, int yy) {
        if (replicate)
            return a.at<float>(std::max(0, std::min(a.rows - 1, yy)), std::max(0, std::min(a.cols - 1, xx)));
        return xx >= 0 && xx < a.cols && yy >= 0 && yy < a.rows ? a.at<float>(yy, xx) : 0.f;
    };
    return at(x, y) * ((1 - u) * (1 - v)) + at(x + 1, y) * (u * (1 - v)) + at(x, y + 1) * ((1 - u) * v) +
           at(x + 1, y + 1) * (u * v);
}
cv::Mat Sample(const cv::Mat &a, const Grid &p, int n) {
    cv::Mat out(n, n, CV_32F);
    for (int y = 0; y < n; ++y)
        for (int x = 0; x < n; ++x)
            out.at<float>(y, x) = Pixel(a, p[y * n + x]);
    return out;
}
Geometry Score(const cv::Mat &im, const Quad &p, int known = 0) {
    Geometry best;
    for (auto size : sizes) {
        const int n = size.first, k = size.second, s = n / k, count = (s - 2) / 2;
        if (known && n != known)
            continue;
        if (!known && n > std::min(im.rows, im.cols) * .6)
            break;
        const auto base = Maps(n, p);
        std::vector<float> v(n * n);
        for (float dx : {-2.f, 0.f, 2.f})
            for (float dy : {-2.f, 0.f, 2.f}) {
                const Point offset(dx, dy);
                for (int i = 0; i < n * n; ++i)
                    v[i] = Pixel(im, base[i] + offset, true);
                for (int r = 0; r < 4; ++r) {
                    double score = 0;
                    for (int y = 0; y < n; y += s)
                        for (int x = 0; x < n; x += s)
                            for (int edge = 0; edge < 2; ++edge) {
                                double w = 0, b = 0, sq = 0;
                                for (int i = 1; i < s - 1; i += 2) {
                                    const float f = edge ? v[RotIndex(y + i + 1, x + s - 1, n, r)]
                                                         : v[RotIndex(y, x + i, n, r)];
                                    const float g = edge ? v[RotIndex(y + i, x + s - 1, n, r)]
                                                         : v[RotIndex(y, x + i + 1, n, r)];
                                    w += f;
                                    b += g;
                                    sq += f * f + g * g;
                                }
                                const double m = (w + b) / (2 * count);
                                score += (w - b) / count / (std::sqrt(std::max(0., sq / (2 * count) - m * m)) + 5);
                            }
                    score *= std::sqrt(n * k) / (2 * k * k);
                    if (score > best.score) {
                        best = {score, n, k, r, p};
                        for (auto &q : best.corners)
                            q += offset;
                    }
                }
            }
    }
    return best;
}

// 沿 Powell 搜索方向应用 Brent 法，并保持各参数在范围内。
// 参考 SciPy 1.13.1，许可见 third_party/scipy/LICENSE.txt。
template <class F> double Line(F f, Params &p, Params &d, double limit, double tol) {
    double a = -1e10, b = 1e10;
    bool moving = false;
    for (size_t i = 0; i < p.size(); ++i)
        if (d[i] != 0) {
            moving = true;
            const double l = (-limit - p[i]) / d[i], h = (limit - p[i]) / d[i];
            a = std::max(a, std::min(l, h));
            b = std::min(b, std::max(l, h));
        }
    if (!moving || a >= b) {
        d.fill(0);
        return f(p);
    }
    const auto at = [&](double t) {
        auto q = p;
        for (size_t i = 0; i < q.size(); ++i)
            q[i] += t * d[i];
        return f(q);
    };
    constexpr double golden = .3819660112501051;
    double x = a + golden * (b - a), w = x, v = x, fx = at(x), fw = fx, fv = fx, e = 0, step = 0;
    for (int i = 0; i < 80; ++i) {
        const double m = (a + b) * .5, t1 = 1.4832396974191326e-8 * std::abs(x) + tol / 3, t2 = 2 * t1;
        if (std::abs(x - m) <= t2 - (b - a) * .5)
            break;
        bool parabola = false;
        if (std::abs(e) > t1) {
            const double r = (x - w) * (fx - fv);
            double q = (x - v) * (fx - fw), u = (x - v) * q - (x - w) * r;
            q = 2 * (q - r);
            if (q > 0)
                u = -u;
            q = std::abs(q);
            const double old = e;
            e = step;
            if (std::abs(u) < std::abs(.5 * q * old) && u > q * (a - x) && u < q * (b - x)) {
                step = u / q;
                parabola = true;
                if (x + step - a < t2 || b - x - step < t2)
                    step = m >= x ? t1 : -t1;
            }
        }
        if (!parabola) {
            e = x >= m ? a - x : b - x;
            step = golden * e;
        }
        const double u = x + (step >= 0 ? 1 : -1) * std::max(std::abs(step), t1), fu = at(u);
        if (fu <= fx) {
            if (u >= x)
                a = x;
            else
                b = x;
            v = w;
            fv = fw;
            w = x;
            fw = fx;
            x = u;
            fx = fu;
        } else {
            if (u < x)
                a = u;
            else
                b = u;
            if (fu <= fw || w == x) {
                v = w;
                fv = fw;
                w = u;
                fw = fu;
            } else if (fu <= fv || v == x || v == w) {
                v = u;
                fv = fu;
            }
        }
    }
    for (size_t i = 0; i < p.size(); ++i) {
        d[i] *= x;
        p[i] += d[i];
    }
    return fx;
}
template <class F> Params Powell(F f, Params p, double limit, int iterations, double xtol, double ftol) {
    std::array<Params, 8> dirs{};
    for (size_t i = 0; i < dirs.size(); ++i)
        dirs[i][i] = 1;
    auto prev = p;
    double value = f(p);
    for (int it = 0; it < iterations; ++it) {
        const double start = value;
        double delta = 0;
        size_t best = 0;
        for (size_t i = 0; i < dirs.size(); ++i) {
            auto d = dirs[i];
            const double before = value;
            value = Line(f, p, d, limit, xtol);
            if (before - value > delta) {
                delta = before - value;
                best = i;
            }
        }
        if (2 * (start - value) <= ftol * (std::abs(start) + std::abs(value)) + 1e-20 || it + 1 == iterations)
            break;
        Params d{};
        double maxStep = 1;
        for (size_t i = 0; i < d.size(); ++i) {
            d[i] = p[i] - prev[i];
            if (d[i])
                maxStep = std::min(maxStep, ((d[i] > 0 ? limit : -limit) - p[i]) / d[i]);
        }
        prev = p;
        auto extrap = p;
        for (size_t i = 0; i < extrap.size(); ++i)
            extrap[i] += maxStep * d[i];
        const double extra = f(extrap);
        if (start > extra) {
            const double t = start - value - delta, u = start - extra;
            if (2 * (start + extra - 2 * value) * t * t - delta * u * u < 0) {
                value = Line(f, p, d, limit, xtol);
                if (std::any_of(d.begin(), d.end(), [](double a) { return a != 0; })) {
                    dirs[best] = dirs.back();
                    dirs.back() = d;
                }
            }
        }
    }
    return p;
}
Quad Shift(const Params &p, const Quad &q = {}) {
    auto r = q;
    for (int i = 0; i < 4; ++i)
        r[i] = Point(static_cast<float>(r[i].x + p[2 * i]), static_cast<float>(r[i].y + p[2 * i + 1]));
    return r;
}
Point Weighted(const Quad &q, float x, float y) {
    return q[0] * ((1 - x) * (1 - y)) + q[1] * (x * (1 - y)) + q[2] * (x * y) + q[3] * ((1 - x) * y);
}
// 与参考实现使用相同的浮点累加顺序，避免改变贴近格线时的拟合结果。
float FinderSum(const float *v, int n) {
    if (n > 128) {
        const int mid = (n / 2) & ~7;
        return FinderSum(v, mid) + FinderSum(v + mid, n - mid);
    }
    if (n < 8) return std::accumulate(v, v + n, 0.f);
    std::array<float, 8> lanes;
    std::copy_n(v, 8, lanes.begin());
    int i = 8;
    for (; i + 7 < n; i += 8)
        for (int j = 0; j < 8; ++j) lanes[j] += v[i + j];
    float sum = ((lanes[0] + lanes[1]) + (lanes[2] + lanes[3])) +
                ((lanes[4] + lanes[5]) + (lanes[6] + lanes[7]));
    for (; i < n; ++i) sum += v[i];
    return sum;
}
Grid Optimize(const cv::Mat &im, const Geometry &g) {
    const auto base = Blur(im, 1.15);
    const cv::Mat high = base - Blur(base, 4.5);
    const auto m = Mask(g.n, g.regions, g.rot);
    std::array<std::vector<float>, 2> samples;
    for (int k = 0; k < 2; ++k)
        samples[k].resize(std::count(m.begin(), m.end(), k ? 1 : -1));
    const auto loss = [&](const Params &p) {
        const auto q = Shift(p, g.corners);
        int count[2] = {};
        for (int i = 0; i < g.n * g.n; ++i)
            if (m[i]) {
                const auto v = Pixel(high, Weighted(q, (i % g.n + .5f) / g.n, (i / g.n + .5f) / g.n));
                const int c = m[i] > 0;
                samples[c][count[c]++] = v;
            }
        double penalty = 0;
        for (auto d : p)
            penalty += d * d;
        float mean[2], variance[2];
        for (int k = 0; k < 2; ++k) {
            auto &v = samples[k];
            mean[k] = FinderSum(v.data(), count[k]) / count[k];
            for (auto &x : v) { const float d = x - mean[k]; x = d * d; }
            variance[k] = FinderSum(v.data(), count[k]) / count[k];
        }
        return -(mean[1] - mean[0]) / std::sqrt((variance[0] + variance[1]) + 6.) + .001 * penalty / 8;
    };
    const auto q = Shift(Powell(loss, {}, 7, 12, .12, .001), g.corners);
    Grid out(g.n * g.n);
    for (int i = 0; i < g.n * g.n; ++i)
        out[i] = Weighted(q, (i % g.n + .5f) / g.n, (i / g.n + .5f) / g.n);
    return RotateGrid(out, g.n, g.rot);
}
void FitRegionsFromSignal(const cv::Mat &sharp, Grid &p, int n, int k) {
    const int s = n / k;
    for (int y = 0; y < n; y += s)
        for (int x = 0; x < n; x += s) {
            const auto loss = [&](const Params &a) {
                const auto q = Shift(a);
                double score = 0, penalty = 0;
                const auto val = [&](int j, int i) {
                    return Pixel(sharp, p[(y + j) * n + x + i] + Weighted(q, (i + .5f) / s, (j + .5f) / s));
                };
                for (int i = 1; i < s - 1; i += 2)
                    score += std::min(22.f, val(0, i) - val(0, i + 1)) +
                             std::min(22.f, val(i + 1, s - 1) - val(i, s - 1));
                for (auto z : a)
                    penalty += z * z;
                return -score / ((s - 2) / 2) + .008 * penalty;
            };
            Params best{};
            double value = 1e10;
            for (double dx : {-2., -1., 0., 1., 2.})
                for (double dy : {-2., -1., 0., 1., 2.}) {
                    const Params q{dx, dy, dx, dy, dx, dy, dx, dy};
                    const auto f = loss(q);
                    if (f < value) {
                        value = f;
                        best = q;
                    }
                }
            const auto fit = Powell(loss, best, 4, 10, .13, .005);
            const auto q = Shift(loss(fit) < value ? fit : best);
            for (int j = 0; j < s; ++j)
                for (int i = 0; i < s; ++i)
                    p[(y + j) * n + x + i] += Weighted(q, (i + .5f) / s, (j + .5f) / s);
        }
}
void FitRegions(const cv::Mat &im, Grid &p, int n, int k) {
    cv::Mat mean;
    cv::blur(im, mean, {3, 3});
    FitRegionsFromSignal(mean + 2.8 * (mean - Blur(mean, 2)), p, n, k);
}
Grid Gradient(const Grid &p, int n, bool horizontal) {
    Grid g(n * n);
    for (int y = 0; y < n; ++y)
        for (int x = 0; x < n; ++x) {
            const int i = y * n + x, j = horizontal ? x : y, d = horizontal ? 1 : n;
            g[i] = (p[i + (j < n - 1 ? d : 0)] - p[i - (j > 0 ? d : 0)]) * (j == 0 || j == n - 1 ? 1.f : .5f);
        }
    return g;
}
Grid Displace(const Grid &p, const Grid &dx, const Grid &dy, float u, float v) {
    Grid q(p.size());
    for (size_t i = 0; i < p.size(); ++i)
        q[i] = p[i] + dx[i] * u + dy[i] * v;
    return q;
}
double Percentile(std::vector<float> v, double rank) {
    const double t = (v.size() - 1) * rank;
    const auto i = static_cast<size_t>(t);
    std::nth_element(v.begin(), v.begin() + i, v.end());
    const float x = v[i];
    if (t == i)
        return x;
    return x + (t - i) * (*std::min_element(v.begin() + i + 1, v.end()) - x);
}
std::vector<float> GrayValues(const cv::Mat &a) {
    std::vector<float> v;
    v.reserve(a.total());
    for (int y = 0; y < a.rows; ++y) {
        const auto p = a.ptr<uchar>(y);
        v.insert(v.end(), p, p + a.cols);
    }
    return v;
}
Grid Refine(const cv::Mat &im, const Grid &p, const std::vector<int> &mask, int n) {
    const auto dx = Gradient(p, n, true), dy = Gradient(p, n, false);
    std::vector<float> lens;
    for (auto u : dx)
        lens.push_back(static_cast<float>(cv::norm(u)));
    const auto sm = Blur(im, Percentile(lens, .5) * .22);
    cv::Mat sx, sy;
    cv::Sobel(sm, sx, -1, 1, 0);
    cv::Sobel(sm, sy, -1, 0, 1);
    const auto axis = [&](const Grid &g) {
        Point l{};
        for (auto u : g)
            l += u;
        return l * static_cast<float>(1 / cv::norm(l));
    };
    const auto ax = axis(dx), ay = axis(dy);
    const cv::Mat gx = ax.x * sx + ax.y * sy, gy = ay.x * sx + ay.y * sy;
    std::vector<cv::Mat> all;
    all.reserve(81);
    cv::Mat maximum(n, n, CV_32F, cv::Scalar(-1e9));
    const auto contrast = [](const cv::Mat &g, Point pt, Point d) {
        const float a = Pixel(g, pt + d * .5f), b = Pixel(g, pt - d * .5f), c = Pixel(g, pt);
        return a * a + b * b - 2 * c * c;
    };
    for (int ix = -4; ix <= 4; ++ix)
        for (int iy = -4; iy <= 4; ++iy) {
            const float x = ix * .1f, y = iy * .1f;
            const auto q = Displace(p, dx, dy, x, y);
            auto v = Sample(sm, q, n);
            v -= Blur(v, 2.5);
            cv::Mat e(n, n, CV_32F);
            for (int i = 0; i < n * n; ++i)
                e.ptr<float>()[i] = contrast(gx, q[i], dx[i]) + contrast(gy, q[i], dy[i]);
            cv::Scalar mean, es, vs;
            cv::meanStdDev(e, mean, es);
            cv::meanStdDev(v, mean, vs);
            for (int i = 0; i < n * n; ++i)
                e.ptr<float>()[i] = static_cast<float>(e.ptr<float>()[i] / (es[0] + 1) +
                                                       v.ptr<float>()[i] * mask[i] / (vs[0] + 1));
            e = Blur(e, 2);
            e -= .5 * (x * x + y * y);
            cv::max(maximum, e, maximum);
            all.push_back(e);
        }
    cv::Mat sxg = cv::Mat::zeros(n, n, CV_32F), syg = sxg.clone();
    for (int i = 0; i < n * n; ++i) {
        double a = 0, b = 0, w = 0;
        for (int j = 0; j < 81; ++j) {
            const double z = std::exp((all[j].ptr<float>()[i] - maximum.ptr<float>()[i]) / .09f);
            a += z * (j / 9 - 4) * .1;
            b += z * (j % 9 - 4) * .1;
            w += z;
        }
        sxg.ptr<float>()[i] = static_cast<float>(a / w);
        syg.ptr<float>()[i] = static_cast<float>(b / w);
    }
    sxg = Blur(sxg, 1.4);
    syg = Blur(syg, 1.4);
    Grid q(p.size());
    for (int i = 0; i < n * n; ++i)
        q[i] = p[i] + dx[i] * sxg.ptr<float>()[i] + dy[i] * syg.ptr<float>()[i];
    return q;
}
std::array<double, 6> Basis(int i, int n) {
    const double x = 2. * (i % n) / (n - 1) - 1, y = 2. * (i / n) / (n - 1) - 1;
    return {1, x, y, x * x, x * y, y * y};
}
cv::Mat Plane(const cv::Mat &z, const std::vector<int> &mask, int n, int sign) {
    const int count = static_cast<int>(std::count(mask.begin(), mask.end(), sign));
    cv::Mat b(count, 6, CV_64F), s(count, 1, CV_64F), coef;
    for (int i = 0, j = 0; i < n * n; ++i)
        if (mask[i] == sign) {
            const auto x = Basis(i, n);
            std::copy(x.begin(), x.end(), b.ptr<double>(j));
            s.at<double>(j++) = z.ptr<float>()[i];
        }
    cv::solve(b, s, coef, cv::DECOMP_SVD);
    const cv::Mat residual = b * coef - s;
    for (int i = 0; i < count; ++i) {
        const double d = residual.at<double>(i) / 8, w = 1 / (1 + d * d);
        for (int j = 0; j < 6; ++j)
            b.at<double>(i, j) *= w;
        s.at<double>(i) *= w;
    }
    cv::solve(b, s, coef, cv::DECOMP_SVD);
    cv::Mat out(n, n, CV_64F);
    for (int i = 0; i < n * n; ++i) {
        const auto x = Basis(i, n);
        out.ptr<double>()[i] = std::inner_product(x.begin(), x.end(), coef.ptr<double>(), 0.);
    }
    return out;
}
std::pair<cv::Mat, cv::Mat> Surface(const cv::Mat &z, const std::vector<int> &mask, int n) {
    const auto lo = Plane(z, mask, n, -1), hi = Plane(z, mask, n, 1);
    cv::Mat w0(n, n, CV_32F), w1(n, n, CV_32F);
    for (int i = 0; i < n * n; ++i) {
        const double v =
            (z.ptr<float>()[i] - lo.ptr<double>()[i]) / std::max(1., hi.ptr<double>()[i] - lo.ptr<double>()[i]);
        w0.ptr<float>()[i] = mask[i] < 0 ? 2.f : (mask[i] == 0 && v < .28 ? 1.f : 0.f);
        w1.ptr<float>()[i] = mask[i] > 0 ? 2.f : (mask[i] == 0 && v > .72 ? 1.f : 0.f);
    }
    cv::Mat s0, s1, d0, d1;
    Blur(z.mul(w0), 3).convertTo(s0, CV_64F);
    Blur(z.mul(w1), 3).convertTo(s1, CV_64F);
    d0 = Blur(w0, 3) + .12;
    d1 = Blur(w1, 3) + .12;
    d0.convertTo(d0, CV_64F);
    d1.convertTo(d1, CV_64F);
    cv::Mat q0, q1;
    cv::divide(s0 + .12 * lo, d0, q0);
    cv::divide(s1 + .12 * hi, d1, q1);
    return {q0, q1};
}
std::pair<cv::Mat, cv::Mat> Quantiles(const cv::Mat &z, int n) {
    cv::Mat lo(n, n, CV_32F), hi(n, n, CV_32F), p;
    cv::copyMakeBorder(z, p, 3, 3, 3, 3, cv::BORDER_REFLECT);
    for (int y = 0; y < n; ++y)
        for (int x = 0; x < n; ++x) {
            std::array<float, 49> w{};
            for (int j = 0; j < 7; ++j)
                std::copy_n(p.ptr<float>(y + j) + x, 7, w.begin() + j * 7);
            std::nth_element(w.begin(), w.begin() + 9, w.end());
            lo.at<float>(y, x) = w[9];
            std::nth_element(w.begin() + 10, w.begin() + 39, w.end());
            hi.at<float>(y, x) = w[39];
        }
    return {Blur(lo, 2), Blur(hi, 2)};
}
cv::Mat Sharpen(const cv::Mat &a, double sigma, double gain) {
    cv::Mat b;
    cv::addWeighted(a, 1 + gain, Blur(a, sigma), -gain, 0, b);
    return b;
}
cv::Mat Decision(const cv::Mat &z, const std::pair<cv::Mat, cv::Mat> &range, double f, bool normalized = false) {
    const auto &lo = range.first;
    const auto &hi = range.second;
    cv::Mat v, d;
    z.convertTo(v, lo.type());
    if (normalized) {
        cv::Mat span = hi - lo;
        cv::max(span, 1, span);
        cv::divide(v - lo, span, d);
        d -= f;
    } else {
        d = v - (lo + (hi - lo) * f);
    }
    return d;
}
Quad MapCorners(const Grid &p, int n, const cv::Mat &m) {
    Grid lattice(p.size()), mapped;
    for (int i = 0; i < n * n; ++i)
        lattice[i] = Point(static_cast<float>(i % n), static_cast<float>(i / n));
    const cv::Mat h = cv::findHomography(lattice, p, 0);
    if (h.empty())
        return {};
    Grid square{{-.5f, -.5f}, {n - .5f, -.5f}, {n - .5f, n - .5f}, {-.5f, n - .5f}};
    const cv::Mat transform = m.inv() * h;
    cv::perspectiveTransform(square, mapped, transform);
    return {{mapped[0], mapped[1], mapped[2], mapped[3]}};
}
