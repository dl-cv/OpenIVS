// Internal Data Matrix recovery. Include inside the barcode module's private namespace.
namespace datamatrix {
#include "../../native/datamatrix/DataMatrixGrid.inl"

struct Hit {
    Json result;
    Quad corners;
    int size;
    int height;
};
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
#ifdef _WIN64
Json RecoverDeep(BarcodeApi &api, const cv::Mat &gray, int format, bool rotate, bool invert) {
    if (gray.rows < 32 || gray.cols < 32) return {};
    BarcodeApi::Owned pure(api.ReaderOptions_new(), api.ReaderOptions_delete);
    api.Check(pure.get());
    api.ReaderOptions_setFormats(pure.get(), &format, 1);
    api.ReaderOptions_setIsPure(pure.get(), true);
    api.ReaderOptions_setTryRotate(pure.get(), false);
    api.ReaderOptions_setTryInvert(pure.get(), false);
    api.ReaderOptions_setTryDownscale(pure.get(), false);
    api.ReaderOptions_setTextMode(pure.get(), 2);
    struct Context {
        BarcodeApi &api;
        void *options;
        Json hit;
        std::exception_ptr failure;
    } context{api, pure.get(), {}, {}};
    const auto verify = [](const unsigned char *bitmap, void *user) -> int {
        auto &ctx = *static_cast<Context *>(user);
        try {
            const cv::Mat image(38, 38, CV_8U, const_cast<unsigned char *>(bitmap));
            auto hits = Read(ctx.api, image, ctx.options, nullptr, 1);
            if (hits.empty()) return 0;
            ctx.hit = std::move(hits.front().result);
            return 1;
        } catch (...) {
            ctx.failure = std::current_exception();
            return 0;
        }
    };
    float corners[8]{};
    const int found = dlcv_datamatrix_read_v1(gray.data, gray.cols, gray.rows, static_cast<int>(gray.step),
        (rotate ? 1 : 0) | (invert ? 2 : 0), corners, verify, &context);
    if (context.failure) std::rethrow_exception(context.failure);
    if (!found) return {};
    Quad output;
    for (int i = 0; i < 4; ++i) output[i] = {corners[2 * i], corners[2 * i + 1]};
    Place(context.hit, output, 0);
    return std::move(context.hit);
}
#endif
} // namespace datamatrix
