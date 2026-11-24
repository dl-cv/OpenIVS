// Internal Data Matrix recovery. Include inside the barcode module's private namespace.
namespace datamatrix {
using Point = cv::Point2f;
using Quad = std::array<Point, 4>;
using Grid = std::vector<Point>;
using Params = std::array<double, 8>;
struct Hit {
    Json result;
    Quad corners;
    int size;
    int height;
};
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
// OpenCV bilinear interpolation rounds the fractional part to 1/32 of a pixel.
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

// Bounded Brent search along Powell directions, keeping variables inside their box.
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
Grid Optimize(const cv::Mat &im, const Geometry &g) {
    const auto base = Blur(im, 1.15);
    const cv::Mat high = base - Blur(base, 4.5);
    const auto m = Mask(g.n, g.regions, g.rot);
    const auto loss = [&](const Params &p) {
        const auto q = Shift(p, g.corners);
        double sum[2] = {}, sq[2] = {};
        int count[2] = {};
        for (int i = 0; i < g.n * g.n; ++i)
            if (m[i]) {
                const auto v = Pixel(high, Weighted(q, (i % g.n + .5f) / g.n, (i / g.n + .5f) / g.n));
                const int c = m[i] > 0;
                sum[c] += v;
                sq[c] += v * v;
                ++count[c];
            }
        double penalty = 0;
        for (auto d : p)
            penalty += d * d;
        const double a = sum[0] / count[0], b = sum[1] / count[1];
        return -(b - a) / std::sqrt(sq[0] / count[0] + sq[1] / count[1] - a * a - b * b + 6) + .001 * penalty / 8;
    };
    const auto q = Shift(Powell(loss, {}, 7, 12, .12, .001), g.corners);
    Grid out(g.n * g.n);
    for (int i = 0; i < g.n * g.n; ++i)
        out[i] = Weighted(q, (i % g.n + .5f) / g.n, (i / g.n + .5f) / g.n);
    return RotateGrid(out, g.n, g.rot);
}
void FitRegions(const cv::Mat &im, Grid &p, int n, int k) {
    const int s = n / k;
    cv::Mat mean;
    cv::blur(im, mean, {3, 3});
    const cv::Mat sharp = mean + 2.8 * (mean - Blur(mean, 2));
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
template <class Verify>
Json CheckBits(const cv::Mat &d, const std::vector<int> &mask, int n, Verify verify, int flip = -1,
               int retry = 0) {
    cv::Mat out(n + 6, n + 6, CV_8U, cv::Scalar(255));
    const auto value = [&](int i) -> double {
        return d.depth() == CV_32F ? d.ptr<float>()[i] : d.ptr<double>()[i];
    };
    const auto cell = [&](int i) -> uchar & { return out.at<uchar>(i / n + 3, i % n + 3); };
    for (int i = 0; i < n * n; ++i) {
        const bool dark = mask[i] ? mask[i] < 0 : (value(i) < 0) ^ (i == flip);
        cell(i) = dark ? 0 : 255;
    }
    auto hit = verify(out);
    if (!hit.is_null() || !retry)
        return hit;

    std::vector<int> candidates;
    for (int i = 0; i < n * n; ++i)
        if (!mask[i])
            candidates.push_back(i);
    const auto count = std::min(static_cast<size_t>(retry), candidates.size());
    std::partial_sort(candidates.begin(), candidates.begin() + count, candidates.end(), [&](int a, int b) {
        const double distanceA = std::abs(value(a)), distanceB = std::abs(value(b));
        return distanceA == distanceB ? a < b : distanceA < distanceB;
    });
    const auto toggle = [&](size_t i) { cell(candidates[i]) ^= 255; };
    for (size_t i = 0; i < count; ++i) {
        toggle(i);
        hit = verify(out);
        toggle(i);
        if (!hit.is_null())
            return hit;
    }
    for (size_t i = 0; i < count; ++i) {
        toggle(i);
        for (size_t j = i + 1; j < count; ++j) {
            toggle(j);
            hit = verify(out);
            toggle(j);
            if (!hit.is_null())
                return hit;
        }
        toggle(i);
    }
    return {};
}
template <class Verify>
Json Probe(const cv::Mat &im, const Grid &p, const std::vector<int> &mask, int n, bool official, Verify verify) {
    const auto z = Sample(Sharpen(Blur(Open(im), 1.35), 2.1, 3), p, n);
    auto result = CheckBits(Decision(z, Quantiles(z, n), .47), mask, n, verify);
    if (official && result.is_null())
        result = CheckBits(Decision(z, Surface(z, mask, n), .5), mask, n, verify);
    return result;
}
template <class Verify>
Json ExtraProbe(const cv::Mat &im, const Grid &p, const std::vector<int> &mask, int n, Verify verify) {
    const auto z = Sample(Sharpen(Blur(im, 1.15), 2.1, 3), p, n);
    const auto d = Decision(z, Surface(z, mask, n), .47, true);
    auto result = CheckBits(d, mask, n, verify);
    if (!result.is_null())
        return result;
    int cell = -1;
    double closest = 1e20;
    for (int i = 0; i < n * n; ++i) {
        const double v = std::abs(d.ptr<double>()[i]);
        if (!mask[i] && v < closest) {
            closest = v;
            cell = i;
        }
    }
    result = CheckBits(d, mask, n, verify, cell);
    if (!result.is_null())
        return result;
    const auto sm = Sharpen(Blur(Open(im), 1.15), 2.1, 5);
    const auto dx = Gradient(p, n, true), dy = Gradient(p, n, false);
    for (float y : {-.1f, .1f})
        for (float x : {-.1f, .1f}) {
            const auto v = Sample(sm, Displace(p, dx, dy, x, y), n);
            result = CheckBits(Decision(v, Quantiles(v, n), .47), mask, n, verify);
            if (!result.is_null())
                return result;
        }
    return {};
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
std::vector<Quad> Seeds(const cv::Mat &a) {
    const int w = a.cols, h = a.rows;
    const auto sm = Blur(a, std::max(1.3, 3.25 * std::max(w, h) / 300));
    cv::Mat temp;
    const auto t = cv::threshold(sm, temp, 0, 255, cv::THRESH_OTSU);
    const auto v = GrayValues(sm);
    std::vector<std::pair<double, Quad>> opts;
    const auto kernel = cv::getStructuringElement(cv::MORPH_ELLIPSE, {7, 7});
    for (auto value : {t, Percentile(v, .25), Percentile(v, .35), Percentile(v, .45), Percentile(v, .55)}) {
        cv::compare(sm, std::ceil(value), temp, cv::CMP_LT);
        cv::morphologyEx(temp, temp, cv::MORPH_CLOSE, kernel);
        std::vector<std::vector<cv::Point>> contours;
        cv::findContours(temp, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);
        for (const auto &c : contours) {
            const double area = cv::contourArea(c);
            if (area < std::max(80., .035 * w * h))
                continue;
            std::vector<cv::Point> hull, q;
            cv::convexHull(c, hull);
            const double perimeter = cv::arcLength(hull, true);
            for (auto f : {.015, .025, .04, .055, .075}) {
                cv::approxPolyDP(hull, q, perimeter * f, true);
                if (q.size() == 4)
                    break;
            }
            if (q.size() != 4)
                continue;
            const auto p = Order({{q[0], q[1], q[2], q[3]}});
            double shortest = 1e9, longest = 1;
            for (int i = 0; i < 4; ++i) {
                const double len = cv::norm(p[i] - p[(i + 1) % 4]);
                shortest = std::min(shortest, len);
                longest = std::max(longest, len);
            }
            const double aspect = shortest / longest;
            if (aspect > .53)
                opts.push_back({std::pow(area / (static_cast<double>(w) * h), .25) * aspect *
                                    std::min(1., area / std::max(1., cv::contourArea(Grid(p.begin(), p.end())))),
                                p});
        }
    }
    std::vector<Quad> result;
    if (!opts.empty()) {
        const auto top = std::max_element(opts.begin(), opts.end(), [](const auto &x, const auto &y) {
                             return x.first < y.first;
                         })->second;
        std::array<std::vector<float>, 8> values;
        for (const auto &o : opts) {
            double dist = 0;
            for (int i = 0; i < 4; ++i)
                dist = std::max({dist, std::abs(double(top[i].x - o.second[i].x)),
                                 std::abs(double(top[i].y - o.second[i].y))});
            if (dist < std::max(6., std::min(w, h) * .07))
                for (int i = 0; i < 4; ++i) {
                    values[2 * i].push_back(o.second[i].x);
                    values[2 * i + 1].push_back(o.second[i].y);
                }
        }
        Quad p{};
        for (int i = 0; i < 4; ++i)
            p[i] = Point(static_cast<float>(Percentile(values[2 * i], .5)),
                         static_cast<float>(Percentile(values[2 * i + 1], .5)));
        result.push_back(p);
    }
    if (std::min(w, h) > .6 * std::max(w, h))
        result.push_back(ImageQuad(w, h));
    return result;
}
bool FitEdges(const cv::Mat &im, Quad &q) {
    const auto a = Blur(Float(im), 1.4);
    std::array<Quad::value_type, 4> starts{}, ends{};
    for (int i = 0; i < 4; ++i) {
        const Point p = target[i], d = target[(i + 1) % 4] - p;
        const Point normal = Point(-d.y, d.x) * static_cast<float>(1 / cv::norm(d));
        double best = -1e9;
        int offset = 0, delta = 0;
        for (int o = -22; o <= 22; ++o)
            for (int z = -26; z <= 26; z += 2) {
                double s = 0;
                for (int j = 0; j < 56; ++j) {
                    const float f = static_cast<float>(.08 + .84 * j / 55);
                    const auto r = p + d * f + normal * (o + z * (f - .5f) * 2);
                    s += Pixel(a, r - normal * 3, true) - Pixel(a, r + normal * 3, true) +
                         .4 * (Pixel(a, r - normal * 7, true) - Pixel(a, r + normal * 7, true));
                }
                if (s > best) {
                    best = s;
                    offset = o;
                    delta = z;
                }
            }
        starts[i] = p + normal * static_cast<float>(offset - delta);
        ends[i] = p + d + normal * static_cast<float>(offset + delta);
    }
    for (int i = 0; i < 4; ++i) {
        const int prev = (i + 3) % 4;
        const auto p = starts[prev], d = ends[prev] - p, e = ends[i] - starts[i];
        const double det = d.cross(e);
        if (std::abs(det) < 1e-6)
            return false;
        q[i] = p + d * static_cast<float>((starts[i] - p).cross(e) / det);
    }
    return true;
}
void Place(Json &hit, const Quad &corners, int pad) {
    const float f = static_cast<float>(pad);
    Json line = Json::array();
    std::array<cv::Point, 4> p{};
    for (int i = 0; i < 4; ++i) {
        p[i] = {cvRound(corners[i].x - f), cvRound(corners[i].y - f)};
        line.push_back(Json::array({p[i].x, p[i].y}));
    }
    line.push_back(line[0]);
    const auto box = cv::boundingRect(std::vector<cv::Point>(p.begin(), p.end()));
    hit["bbox"] = Json::array({box.x, box.y, box.width - 1, box.height - 1});
    hit["area"] = std::abs(cv::contourArea(std::vector<cv::Point>(p.begin(), p.end())));
    hit["extra_info"]["polyline"] = std::move(line);
}
constexpr int checksumError = 2, globalHistogram = 1, fixedThreshold = 2;
std::vector<Hit> Read(BarcodeApi &api, const cv::Mat &im, void *options, void *writer, double scale) {
    BarcodeApi::Owned view(api.ImageView_new(im.data, im.cols, im.rows, 0x01000000, static_cast<int>(im.step), 0),
                           api.ImageView_delete);
    api.Check(view.get());
    BarcodeApi::Owned codes(api.ReadBarcodes(view.get(), options), api.Barcodes_delete);
    api.Check(codes.get());
    std::vector<Hit> out;
    for (int i = 0; i < api.Barcodes_size(codes.get()); ++i) {
        const auto *code = api.Barcodes_at(codes.get(), i);
        const bool valid = api.Barcode_isValid(code);
        const auto s = api.Barcode_position(code);
        const BarcodePoint v[] = {s.topLeft, s.topRight, s.bottomRight, s.bottomLeft};
        Quad p{};
        for (int j = 0; j < 4; ++j)
            p[j] = {static_cast<float>(v[j].x / scale), static_cast<float>(v[j].y / scale)};
        if (valid) {
            if (writer && cv::contourArea(Grid(p.begin(), p.end())) * scale * scale < 30)
                continue;
            Json result{{"category_id", 0},
                        {"category_name", api.Text(api.Barcode_text(code))},
                        {"score", 1.0},
                        {"with_bbox", true},
                        {"with_angle", false},
                        {"angle", -100},
                        {"with_mask", false},
                        {"extra_info",
                         {{"barcode_format", api.Text(api.BarcodeFormatToString(api.Barcode_format(code)))}}}};
            out.push_back({std::move(result), p, 0, 0});
            continue;
        }
        if (!writer || api.Barcode_errorType(code) != checksumError ||
            cv::contourArea(Grid(p.begin(), p.end())) * scale * scale < 30)
            continue;
        BarcodeApi::Owned raster(api.WriteBarcodeToImage(code, writer), api.Image_delete);
        if (raster)
            out.push_back({{}, p, api.Image_width(raster.get()), api.Image_height(raster.get())});
    }
    return out;
}
Quad TransformQuad(const Quad &q, const cv::Mat &m) {
    Grid transformed;
    cv::perspectiveTransform(Grid(q.begin(), q.end()), transformed, m);
    return {{transformed[0], transformed[1], transformed[2], transformed[3]}};
}
cv::Mat GridDistance(const cv::Mat &opened, const Grid &grid, const std::vector<int> &mask, int n, double sigma,
                     double gain, double fraction, bool rank = false, float x = 0, float y = 0) {
    const auto dx = Gradient(grid, n, true);
    double pitch = 0;
    for (auto delta : dx)
        pitch += cv::norm(delta);
    pitch /= dx.size();
    const auto smooth = Blur(opened, pitch * sigma);
    const auto background = Blur(smooth, pitch * (sigma == .22 ? .45 : .46));
    const auto shifted = x || y ? Displace(grid, dx, Gradient(grid, n, false), x, y) : grid;
    auto z = Sample(smooth, shifted, n);
    z += gain * (z - Sample(background, shifted, n));
    return Decision(z, rank ? Quantiles(z, n) : Surface(z, mask, n), fraction, true);
}
template <class Verify> Json RecoverRoiGrid(const cv::Mat &gray, Verify verify) {
    const auto seeds = Seeds(gray);
    if (seeds.empty())
        return {};
    const auto seedProjection = Projection(seeds[0]);
    Quad edge{};
    if (!FitEdges(Rect(gray, seedProjection), edge))
        return {};
    const auto projection = Projection(TransformQuad(edge, seedProjection.inv()));
    const auto smooth = Blur(Float(Rect(gray, projection)), 1.1);
    auto geometry = Score(smooth - Blur(smooth, 4), target);
    if (geometry.score < 3)
        return {};
    geometry.corners = TransformQuad(geometry.corners, projection.inv());

    const int n = geometry.n;
    const auto mask = Mask(n, geometry.regions);
    const auto opened = Float(Open(gray));
    const auto im = Float(gray);
    const auto place = [&](Json hit, const Grid &grid) {
        if (!hit.is_null())
            Place(hit, MapCorners(grid, n, cv::Mat::eye(3, 3, CV_64F)), 0);
        return hit;
    };
    const auto grid = Optimize(im, geometry);
    auto rankDecision = GridDistance(opened, grid, mask, n, .22, 3, .47, true);
    auto hit = CheckBits(rankDecision, mask, n, verify);
    if (!hit.is_null())
        return place(std::move(hit), grid);
    // 浮点拟合的亚像素差异会改变接近阈值的格子，复用采样结果试一次较低阈值。
    rankDecision += .01;
    hit = CheckBits(rankDecision, mask, n, verify);
    if (!hit.is_null())
        return place(std::move(hit), grid);
    const auto refined = Refine(im, grid, mask, n);
    hit = CheckBits(GridDistance(opened, refined, mask, n, .22, 3, .47), mask, n, verify);
    if (!hit.is_null())
        return place(std::move(hit), refined);

    cv::Mat median;
    cv::medianBlur(gray, median, 3);
    const auto refitted = Refine(im, Optimize(Float(median), geometry), mask, n);
    hit = CheckBits(GridDistance(opened, refitted, mask, n, .22, 3, .47, false, .1f, -.1f), mask, n, verify);
    if (!hit.is_null())
        return place(std::move(hit), refitted);

    // Keep finder and timing patterns fixed; try only the least confident data cells.
    hit = CheckBits(GridDistance(opened, refined, mask, n, .16, 2, .49), mask, n, verify, -1, 12);
    return place(std::move(hit), refined);
}

Json Recover(BarcodeApi &api, const cv::Mat &gray, int format, bool rotate, bool invert) {
    if (gray.rows < 8 || gray.cols < 8)
        return {};
    double min = 0, max = 0;
    cv::minMaxLoc(gray, &min, &max);
    if (min == max)
        return {};
    BarcodeApi::Owned scan(api.ReaderOptions_new(), api.ReaderOptions_delete),
        pure(api.ReaderOptions_new(), api.ReaderOptions_delete),
        writer(api.WriterOptions_new(), api.WriterOptions_delete);
    api.Check(scan.get());
    api.Check(pure.get());
    api.Check(writer.get());
    for (void *options : {scan.get(), pure.get()}) {
        api.ReaderOptions_setFormats(options, &format, 1);
        api.ReaderOptions_setTryDownscale(options, false);
        api.ReaderOptions_setTextMode(options, 2);
    }
    api.ReaderOptions_setTryRotate(scan.get(), rotate);
    api.ReaderOptions_setTryInvert(scan.get(), invert);
    api.ReaderOptions_setReturnErrors(scan.get(), true);
    api.ReaderOptions_setIsPure(pure.get(), true);
    api.ReaderOptions_setTryRotate(pure.get(), false);
    api.ReaderOptions_setTryInvert(pure.get(), false);
    api.WriterOptions_setScale(writer.get(), 1);
    api.WriterOptions_setAddQuietZones(writer.get(), false);
    const auto verify = [&](const cv::Mat &bitmap) -> Json {
        auto hits = Read(api, bitmap, pure.get(), nullptr, 1);
        return hits.empty() ? Json() : std::move(hits[0].result);
    };
    auto roiHit = RecoverRoiGrid(gray.isSubmatrix() ? gray.clone() : gray, verify);
    if (!roiHit.is_null())
        return roiHit;

    constexpr int pad = 16;
    cv::Mat a;
    cv::copyMakeBorder(gray, a, pad, pad, pad, pad, cv::BORDER_CONSTANT | cv::BORDER_ISOLATED,
                       cv::Scalar(Percentile(GrayValues(gray), .85)));
    const auto blurred = Blur(a, 1), gaussian = Sharpen(blurred, 6, 2);
    cv::Mat med;
    cv::medianBlur(a, med, 5);
    const auto median = Sharpen(med, 1, 1);
    cv::Mat mean;
    cv::blur(a, mean, {3, 3});
    const auto sharp = Sharpen(Float(mean), 2, 2.8);
    struct Row {
        Geometry g;
        cv::Mat image, projection;
        Quad original;
    };
    std::vector<Row> found;
    for (double scale : {.75, 1.25, .80}) {
        cv::Mat resized;
        cv::resize(scale > 1 ? median : gaussian, resized, {}, scale, scale,
                   scale > 1 ? cv::INTER_CUBIC : cv::INTER_AREA);
        api.ReaderOptions_setBinarizer(scan.get(), scale > 1 ? globalHistogram : fixedThreshold);
        for (auto hit : Read(api, resized, scan.get(), writer.get(), scale)) {
            if (!hit.result.is_null()) {
                Place(hit.result, hit.corners, pad);
                return hit.result;
            }
            if (hit.size != hit.height || std::none_of(std::begin(sizes), std::end(sizes),
                                                       [&](const auto &sz) { return sz.first == hit.size; }))
                continue;
            const auto p = Order(hit.corners);
            const bool seen = std::any_of(found.begin(), found.end(), [&](const Row &r) {
                if (r.g.n != hit.size)
                    return false;
                for (int i = 0; i < 4; ++i)
                    if (std::abs(r.original[i].x - p[i].x) >= 1.25 || std::abs(r.original[i].y - p[i].y) >= 1.25)
                        return false;
                return true;
            });
            if (seen)
                continue;
            const auto m = Projection(p);
            found.push_back({Score(Rect(sharp, m), target, hit.size), Float(Rect(a, m)), m, p});
        }
    }
    const auto byScore = [](const Row &a, const Row &b) { return a.g.score > b.g.score; };
    std::stable_sort(found.begin(), found.end(), byScore);
    int known = 0;
    for (size_t i = 0; i < std::min(size_t(2), found.size()); ++i) {
        const auto &row = found[i];
        const auto &g = row.g;
        if (g.score < 3)
            continue;
        if (!known && g.score >= 6)
            known = g.n;
        const auto mask = Mask(g.n, g.regions);
        auto p = RotateGrid(Maps(g.n, g.corners), g.n, g.rot);
        auto hit = Probe(row.image, p, mask, g.n, true, verify);
        if (hit.is_null()) {
            FitRegions(row.image, p, g.n, g.regions);
            p = Refine(row.image, p, mask, g.n);
            hit = Probe(row.image, p, mask, g.n, true, verify);
        }
        if (!hit.is_null()) {
            Place(hit, MapCorners(p, g.n, row.projection), pad);
            return hit;
        }
    }
    found.clear();
    const auto add = [&](const cv::Mat &im, const cv::Mat &m) {
        const auto v = Float(im), sm = Blur(v, 1.1);
        found.push_back({Score(sm - Blur(sm, 4), target, known), v, m, {}});
    };
    if (std::min(gray.cols, gray.rows) > .6 * std::max(gray.cols, gray.rows)) {
        const auto m = Projection(ImageQuad(gray.cols, gray.rows, pad));
        add(Rect(a, m), m);
    }
    const auto seeds = Seeds(a);
    if (!seeds.empty()) {
        const auto m = Projection(seeds[0]);
        const auto im = Rect(a, m);
        Quad q{};
        if (FitEdges(im, q)) {
            const auto p = Projection(q);
            add(Rect(im, p), p * m);
        }
    }
    std::stable_sort(found.begin(), found.end(), byScore);
    for (const auto &row : found) {
        const auto &g = row.g;
        if (g.score < 3)
            continue;
        const auto mask = Mask(g.n, g.regions);
        auto p = Optimize(row.image, g);
        auto hit = Probe(row.image, p, mask, g.n, false, verify);
        if (hit.is_null()) {
            p = Refine(row.image, p, mask, g.n);
            hit = Probe(row.image, p, mask, g.n, false, verify);
            if (hit.is_null())
                hit = ExtraProbe(row.image, p, mask, g.n, verify);
        }
        if (!hit.is_null()) {
            Place(hit, MapCorners(p, g.n, row.projection), pad);
            return hit;
        }
    }
    return {};
}
} // namespace datamatrix
