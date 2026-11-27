#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <vector>
#include <utility>

namespace dlcv_infer { namespace flow { namespace dpm { namespace vision {
struct Point { float X, Y; };
struct ComponentInfo {
    int Label, Area, MinX, MinY, MaxX, MaxY;
    std::vector<Point> BoundaryPoints;
    ComponentInfo(int label, int area, int minX, int minY, int maxX, int maxY, std::vector<Point> boundary)
        : Label(label), Area(area), MinX(minX), MinY(minY), MaxX(maxX), MaxY(maxY), BoundaryPoints(std::move(boundary)) {}
};
int ClampIdx(int idx, int len) { return idx < 0 ? 0 : (idx >= len ? len-1 : idx); }
int Reflect101(int idx, int len) {
    if (len <= 1) return 0;
    while (idx < 0 || idx >= len) { if (idx < 0) idx=-idx; if (idx>=len) idx=2*len-2-idx; }
    return idx;
}
    float ComputePercentileU8(const std::vector<uint8_t>& values, double q)
    {
        if (int(values.size()) == 0) return 0.f;
        std::vector<int> hist = std::vector<int>(256);
        for (int i = 0; i < int(values.size()); i++) hist[values[i]]++;
        double pos = q * (int(values.size()) - 1);
        int idxLo = (int)std::floor(pos);
        int idxHi = std::min(int(values.size()) - 1, idxLo + 1);
        double frac = pos - idxLo;
        int acc = 0, vLo = -1, vHi = -1;
        for (int v = 0; v < 256; v++)
        {
            acc += hist[v];
            if (vLo < 0 && acc > idxLo) vLo = v;
            if (vHi < 0 && acc > idxHi) { vHi = v; break; }
        }
        if (vLo < 0) vLo = 255;
        if (vHi < 0) vHi = vLo;
        return (float)(vLo + (vHi - vLo) * frac);
    }

    float InterpPercentile(const std::vector<float>& sorted, double q)
    {
        if (int(sorted.size()) == 1) return sorted[0];
        double pos = q * (int(sorted.size()) - 1);
        int lo = (int)std::floor(pos);
        int hi = std::min(int(sorted.size()) - 1, lo + 1);
        double frac = pos - lo;
        return (float)(sorted[lo] * (1.0 - frac) + sorted[hi] * frac);
    }

    std::pair<float, float> ComputePercentiles16And84(std::vector<float> vals)
    {
        if (int(vals.size()) == 0) return {0.f, 1.f};
        std::sort(vals.begin(), vals.end());
        return {InterpPercentile(vals, 0.16), InterpPercentile(vals, 0.84)};
    }

    std::vector<uint8_t> MedianBlurU8(const std::vector<uint8_t>& src, int w, int h, int ksize)
    {
        int rad = ksize / 2;
        std::vector<uint8_t> dst = std::vector<uint8_t>(w * h);
        int halfCount = (ksize * ksize) / 2;

        if (ksize == 3)
        {
            std::vector<uint8_t> win = std::vector<uint8_t>(9);
            for (int y = 0; y < h; y++)
            {
                int y0 = ClampIdx(y - 1, h) * w;
                int y1 = y * w;
                int y2 = ClampIdx(y + 1, h) * w;
                for (int x = 0; x < w; x++)
                {
                    int x0 = ClampIdx(x - 1, w), x1 = x, x2 = ClampIdx(x + 1, w);
                    win[0] = src[y0 + x0]; win[1] = src[y0 + x1]; win[2] = src[y0 + x2];
                    win[3] = src[y1 + x0]; win[4] = src[y1 + x1]; win[5] = src[y1 + x2];
                    win[6] = src[y2 + x0]; win[7] = src[y2 + x1]; win[8] = src[y2 + x2];
                    for (int i = 1; i < 9; i++)
                    {
                        uint8_t key = win[i];
                        int j = i - 1;
                        while (j >= 0 && win[j] > key) { win[j + 1] = win[j]; j--; }
                        win[j + 1] = key;
                    }
                    dst[y1 + x] = win[4];
                }
            }
            return dst;
        }

        std::vector<int> hist = std::vector<int>(256);
        for (int y = 0; y < h; y++)
        {
            std::fill(hist.begin(), hist.end(), 0);
            for (int dy = -rad; dy <= rad; dy++)
            {
                int ry = ClampIdx(y + dy, h) * w;
                for (int dx = -rad; dx <= rad; dx++)
                    hist[src[ry + ClampIdx(dx, w)]]++;
            }

            int med = 0, ltCount = 0;
            while (ltCount + hist[med] <= halfCount)
            {
                ltCount += hist[med];
                med++;
            }
            dst[y * w] = (uint8_t)med;

            for (int x = 1; x < w; x++)
            {
                int xRem = ClampIdx(x - 1 - rad, w);
                int xAdd = ClampIdx(x + rad, w);
                if (xRem != xAdd)
                {
                    for (int dy = -rad; dy <= rad; dy++)
                    {
                        int ry = ClampIdx(y + dy, h) * w;
                        uint8_t vRem = src[ry + xRem];
                        uint8_t vAdd = src[ry + xAdd];
                        hist[vRem]--;
                        if (vRem < med) ltCount--;
                        hist[vAdd]++;
                        if (vAdd < med) ltCount++;
                    }
                    while (ltCount > halfCount)
                    {
                        med--;
                        ltCount -= hist[med];
                    }
                    while (ltCount + hist[med] <= halfCount)
                    {
                        ltCount += hist[med];
                        med++;
                    }
                }
                dst[y * w + x] = (uint8_t)med;
            }
        }
        return dst;
    }

    std::vector<float> ResizeAreaF32(const std::vector<float>& src, int srcW, int srcH, int dstW, int dstH)
    {
        std::vector<float> dst = std::vector<float>(dstW * dstH);
        double scaleX = (double)srcW / dstW;
        double scaleY = (double)srcH / dstH;
        double invArea = 1.0 / (scaleX * scaleY);

        for (int dy = 0; dy < dstH; dy++)
        {
            double sy0 = dy * scaleY, sy1 = (dy + 1) * scaleY;
            int iy0 = (int)std::floor(sy0), iy1 = std::min(srcH, (int)std::ceil(sy1));
            for (int dx = 0; dx < dstW; dx++)
            {
                double sx0 = dx * scaleX, sx1 = (dx + 1) * scaleX;
                int ix0 = (int)std::floor(sx0), ix1 = std::min(srcW, (int)std::ceil(sx1));
                double sum = 0.0;
                for (int sy = iy0; sy < iy1; sy++)
                {
                    double wy = std::min(sy + 1.0, sy1) - std::max((double)sy, sy0);
                    int rOff = sy * srcW;
                    double rowSum = 0.0;
                    for (int sx = ix0; sx < ix1; sx++)
                    {
                        double wx = std::min(sx + 1.0, sx1) - std::max((double)sx, sx0);
                        rowSum += src[rOff + sx] * wx;
                    }
                    sum += rowSum * wy;
                }
                dst[dy * dstW + dx] = (float)(sum * invArea);
            }
        }
        return dst;
    }

    std::vector<float> ResizeBilinearF32(const std::vector<float>& src, int srcW, int srcH, int dstW, int dstH)
    {
        std::vector<float> dst = std::vector<float>(dstW * dstH);
        float scaleX = (float)srcW / dstW;
        float scaleY = (float)srcH / dstH;

        for (int dy = 0; dy < dstH; dy++)
        {
            float sy = (dy + 0.5f) * scaleY - 0.5f;
            if (sy < 0.f) sy = 0.f;
            if (sy > srcH - 1) sy = srcH - 1;
            int iy0 = (int)std::floor(sy);
            int iy1 = std::min(srcH - 1, iy0 + 1);
            float fy = sy - iy0;

            int r0 = iy0 * srcW, r1 = iy1 * srcW, dOff = dy * dstW;
            for (int dx = 0; dx < dstW; dx++)
            {
                float sx = (dx + 0.5f) * scaleX - 0.5f;
                if (sx < 0.f) sx = 0.f;
                if (sx > srcW - 1) sx = srcW - 1;
                int ix0 = (int)std::floor(sx);
                int ix1 = std::min(srcW - 1, ix0 + 1);
                float fx = sx - ix0;

                float v0 = src[r0 + ix0] + fx * (src[r0 + ix1] - src[r0 + ix0]);
                float v1 = src[r1 + ix0] + fx * (src[r1 + ix1] - src[r1 + ix0]);
                dst[dOff + dx] = v0 + fy * (v1 - v0);
            }
        }
        return dst;
    }

    std::vector<int> BuildEllipseRadii(int diameter)
    {
        int r = diameter / 2;
        std::vector<int> js = std::vector<int>(diameter);
        for (int dy = -r; dy <= r; dy++)
            js[dy + r] = (int)std::nearbyint(r * std::sqrt(std::max(0.0, 1.0 - (double)(dy * dy) / (r * r))));
        return js;
    }

    std::vector<float> MorphFilterEllipseF32(const std::vector<float>& src, int w, int h, int diameter, std::vector<int> radii, bool isDilate)
    {
        int r = diameter / 2;
        std::vector<std::vector<float>> hSpan = std::vector<std::vector<float>>(r + 1);
        hSpan[0] = src;

        // O(1) incremental recurrence across radii 1..r
        for (int rad = 1; rad <= r; rad++)
        {
            std::vector<float> prev = hSpan[rad - 1];
            std::vector<float> cur = std::vector<float>(w * h);
            for (int y = 0; y < h; y++)
            {
                int rOff = y * w;
                for (int x = 0; x < w; x++)
                {
                    float vPrev = prev[rOff + x];
                    float vL = src[rOff + std::max(0, x - rad)];
                    float vR = src[rOff + std::min(w - 1, x + rad)];
                    if (isDilate)
                    {
                        float m = vL > vR ? vL : vR;
                        cur[rOff + x] = vPrev > m ? vPrev : m;
                    }
                    else
                    {
                        float m = vL < vR ? vL : vR;
                        cur[rOff + x] = vPrev < m ? vPrev : m;
                    }
                }
            }
            hSpan[rad] = cur;
        }

        std::vector<float> dst = std::vector<float>(w * h);
        for (int y = 0; y < h; y++)
        {
            int dOff = y * w;
            for (int x = 0; x < w; x++)
            {
                float best = isDilate ? -INFINITY : INFINITY;
                for (int dy = -r; dy <= r; dy++)
                {
                    int yy = ClampIdx(y + dy, h);
                    float v = hSpan[radii[dy + r]][yy * w + x];
                    if (isDilate ? (v > best) : (v < best)) best = v;
                }
                dst[dOff + x] = best;
            }
        }
        return dst;
    }

    std::vector<float> MorphCloseEllipseF32(const std::vector<float>& src, int w, int h, int diameter)
    {
        std::vector<int> radii = BuildEllipseRadii(diameter);
        std::vector<float> dil = MorphFilterEllipseF32(src, w, h, diameter, radii, true);
        return MorphFilterEllipseF32(dil, w, h, diameter, radii, false);
    }

    std::vector<uint8_t> MorphRectU8(std::vector<uint8_t> srcBin, int w, int h, int kw, int kh, bool isDilate)
    {
        int rx = kw / 2, ry = kh / 2;
        std::vector<uint8_t> dst = std::vector<uint8_t>(w * h);
        for (int y = 0; y < h; y++)
        {
            int y0 = std::max(0, y - ry), y1 = std::min(h - 1, y + ry);
            for (int x = 0; x < w; x++)
            {
                int x0 = std::max(0, x - rx), x1 = std::min(w - 1, x + rx);
                uint8_t res = isDilate ? (uint8_t)0 : (uint8_t)255;
                bool done = false;
                for (int yy = y0; yy <= y1 && !done; yy++)
                {
                    int rOff = yy * w;
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        uint8_t v = srcBin[rOff + xx];
                        if (isDilate && v != 0) { res = 255; done = true; break; }
                        if (!isDilate && v == 0) { res = 0; done = true; break; }
                    }
                }
                dst[y * w + x] = res;
            }
        }
        return dst;
    }

    double ComputeOtsuThreshold(const std::vector<uint8_t>& src)
    {
        std::vector<int> hist = std::vector<int>(256);
        for (int i = 0; i < int(src.size()); i++) hist[src[i]]++;
        double invN = 1.0 / int(src.size());
        double muTotal = 0.0;
        for (int i = 0; i < 256; i++) muTotal += i * (hist[i] * invN);

        double q1 = 0.0, sum1 = 0.0, maxSigma = -1.0;
        int bestThresh = 0;
        for (int t = 0; t < 256; t++)
        {
            double p = hist[t] * invN;
            q1 += p;
            sum1 += t * p;
            double q2 = 1.0 - q1;
            if (q1 < 1e-9 || q2 < 1e-9) continue;
            double mu1 = sum1 / q1;
            double mu2 = (muTotal - sum1) / q2;
            double diff = mu1 - mu2;
            double sigma = q1 * q2 * diff * diff;
            if (sigma > maxSigma) { maxSigma = sigma; bestThresh = t; }
        }
        return bestThresh;
    }

    std::pair<std::vector<int>, std::vector<ComponentInfo>> ConnectedComponents8(const std::vector<uint8_t>& mask, int w, int h, bool collectBoundary)
    {
        std::vector<int> labels = std::vector<int>(w * h);
        std::vector<ComponentInfo> comps = std::vector<ComponentInfo>();
        std::vector<int> queue = std::vector<int>(w * h);
        int curLabel = 0;

        for (int i = 0; i < int(mask.size()); i++)
        {
            if (mask[i] == 0 || labels[i] != 0) continue;
            curLabel++;
            int head = 0, tail = 0;
            queue[tail++] = i;
            labels[i] = curLabel;

            int minX = i % w, maxX = minX, minY = i / w, maxY = minY;
            std::vector<Point> boundary = collectBoundary ? std::vector<Point>() : std::vector<Point>(0);

            while (head < tail)
            {
                int cur = queue[head++];
                int cx = cur % w, cy = cur / w;
                if (cx < minX) minX = cx;
                if (cx > maxX) maxX = cx;
                if (cy < minY) minY = cy;
                if (cy > maxY) maxY = cy;

                bool isBoundary = cx == 0 || cy == 0 || cx == w - 1 || cy == h - 1;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cy + dy;
                    if (ny < 0 || ny >= h) continue;
                    int rOff = ny * w;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx;
                        if (nx < 0 || nx >= w) continue;
                        int nIdx = rOff + nx;
                        if (mask[nIdx] == 0)
                        {
                            if (dx == 0 || dy == 0) isBoundary = true;
                        }
                        else if (labels[nIdx] == 0)
                        {
                            labels[nIdx] = curLabel;
                            queue[tail++] = nIdx;
                        }
                    }
                }
                if (collectBoundary && isBoundary)
                    boundary.push_back({float(cx), float(cy)});
            }
            comps.push_back(ComponentInfo(curLabel, tail, minX, minY, maxX, maxY, boundary));
        }
        return {labels, comps};
    }

    std::vector<Point> ConvexHull(const std::vector<Point>& pts)
    {
        if (int(pts.size()) <= 2) return std::vector<Point>(pts);
        auto sorted = std::vector<Point>(pts);
        std::sort(sorted.begin(), sorted.end(), [](Point a, Point b) { return a.X != b.X ? a.X < b.X : a.Y < b.Y; });

        auto Cross = [](Point o, Point a, Point b) { return ((double)a.X - o.X) * ((double)b.Y - o.Y) - ((double)a.Y - o.Y) * ((double)b.X - o.X); };

        auto lower = std::vector<Point>();
        for (const auto& p : sorted)
        {
            while (int(lower.size()) >= 2 && Cross(lower[int(lower.size()) - 2], lower[int(lower.size()) - 1], p) <= 0)
                lower.pop_back();
            lower.push_back(p);
        }

        auto upper = std::vector<Point>();
        for (int i = int(sorted.size()) - 1; i >= 0; i--)
        {
            auto p = sorted[i];
            while (int(upper.size()) >= 2 && Cross(upper[int(upper.size()) - 2], upper[int(upper.size()) - 1], p) <= 0)
                upper.pop_back();
            upper.push_back(p);
        }

        lower.pop_back();
        upper.pop_back();
        lower.insert(lower.end(), upper.begin(), upper.end());
        return lower;
    }

    std::vector<Point> MinAreaRectBox(const std::vector<Point>& pts)
    {
        auto hull = ConvexHull(pts);
        int n = int(hull.size());
        if (n == 0) return std::vector<Point>(4);
        if (n == 1)
        {
            auto p = hull[0];
            return {p, p, p, p};
        }

        double bestArea = INFINITY;
        double bestUx = 1, bestUy = 0, bestMinU = 0, bestMaxU = 0, bestMinV = 0, bestMaxV = 0;

        for (int i = 0; i < n; i++)
        {
            auto p0 = hull[i];
            auto p1 = hull[(i + 1) % n];
            double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            double len = std::sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;
            double ux = dx / len, uy = dy / len;
            double vx = -uy, vy = ux;

            double minU = INFINITY, maxU = -INFINITY;
            double minV = INFINITY, maxV = -INFINITY;
            for (int j = 0; j < n; j++)
            {
                double u = hull[j].X * ux + hull[j].Y * uy;
                double v = hull[j].X * vx + hull[j].Y * vy;
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }
            double area = (maxU - minU) * (maxV - minV);
            if (area < bestArea)
            {
                bestArea = area;
                bestUx = ux; bestUy = uy;
                bestMinU = minU; bestMaxU = maxU;
                bestMinV = minV; bestMaxV = maxV;
            }
        }

        double bvx = -bestUy, bvy = bestUx;
        auto Pt = [&](double u, double v) { return Point{(float)(u * bestUx + v * bvx), (float)(u * bestUy + v * bvy)}; };

        return
        {
            Pt(bestMinU, bestMinV),
            Pt(bestMaxU, bestMinV),
            Pt(bestMaxU, bestMaxV),
            Pt(bestMinU, bestMaxV)
        };
    }

    bool SolveHomography4Pts(const std::vector<float>& src, const std::vector<float>& dst, std::vector<double>& H)
    {
        std::vector<double> A = std::vector<double>(72);
        for (int i = 0; i < 4; i++)
        {
            double sx = src[2 * i], sy = src[2 * i + 1];
            double dx = dst[2 * i], dy = dst[2 * i + 1];
            int r0 = (2 * i) * 9, r1 = (2 * i + 1) * 9;
            A[r0 + 0] = sx; A[r0 + 1] = sy; A[r0 + 2] = 1.0;
            A[r0 + 6] = -sx * dx; A[r0 + 7] = -sy * dx; A[r0 + 8] = dx;

            A[r1 + 3] = sx; A[r1 + 4] = sy; A[r1 + 5] = 1.0;
            A[r1 + 6] = -sx * dy; A[r1 + 7] = -sy * dy; A[r1 + 8] = dy;
        }

        for (int col = 0; col < 8; col++)
        {
            int piv = col;
            double maxV = std::abs(A[col * 9 + col]);
            for (int row = col + 1; row < 8; row++)
            {
                double v = std::abs(A[row * 9 + col]);
                if (v > maxV) { maxV = v; piv = row; }
            }
            if (maxV < 1e-12) return false;
            if (piv != col)
            {
                for (int k = col; k <= 8; k++)
                    std::swap(A[col * 9 + k], A[piv * 9 + k]);
            }
            double invP = 1.0 / A[col * 9 + col];
            for (int row = col + 1; row < 8; row++)
            {
                double f = A[row * 9 + col] * invP;
                for (int k = col + 1; k <= 8; k++)
                    A[row * 9 + k] -= f * A[col * 9 + k];
            }
        }

        for (int row = 7; row >= 0; row--)
        {
            double s = A[row * 9 + 8];
            for (int k = row + 1; k < 8; k++) s -= A[row * 9 + k] * H[k];
            H[row] = s / A[row * 9 + row];
        }
        H[8] = 1.0;
        return true;
    }

    bool Invert3x3(const std::vector<double>& m, std::vector<double>& inv)
    {
        double a = m[0], b = m[1], c = m[2];
        double d = m[3], e = m[4], f = m[5];
        double g = m[6], h = m[7], i = m[8];

        double A = e * i - f * h;
        double B = -(d * i - f * g);
        double C = d * h - e * g;
        double det = a * A + b * B + c * C;
        if (std::abs(det) < 1e-15) return false;
        double invDet = 1.0 / det;

        inv[0] = A * invDet;
        inv[1] = -(b * i - c * h) * invDet;
        inv[2] = (b * f - c * e) * invDet;
        inv[3] = B * invDet;
        inv[4] = (a * i - c * g) * invDet;
        inv[5] = -(a * f - c * d) * invDet;
        inv[6] = C * invDet;
        inv[7] = -(a * h - b * g) * invDet;
        inv[8] = (a * e - b * d) * invDet;
        return true;
    }

    void PerspectiveTransformPts(const std::vector<float>& pts, std::vector<double>& H, std::vector<float>& dstPts)
    {
        int count = int(pts.size()) / 2;
        for (int i = 0; i < count; i++)
        {
            double x = pts[2 * i], y = pts[2 * i + 1];
            double den = H[6] * x + H[7] * y + H[8];
            dstPts[2 * i] = (float)((H[0] * x + H[1] * y + H[2]) / den);
            dstPts[2 * i + 1] = (float)((H[3] * x + H[4] * y + H[5]) / den);
        }
    }

    float SampleBilinearZero(const std::vector<float>& img, int w, int h, float x, float y)
    {
        if (x < 0.f || y < 0.f || x >= w - 1 || y >= h - 1) return 0.f;
        int ix = (int)x, iy = (int)y;
        float fx = x - ix, fy = y - iy;
        int r0 = iy * w + ix, r1 = r0 + w;
        float v0 = img[r0] + fx * (img[r0 + 1] - img[r0]);
        float v1 = img[r1] + fx * (img[r1 + 1] - img[r1]);
        return v0 + fy * (v1 - v0);
    }

    std::vector<float> WarpPerspectiveF32(const std::vector<float>& src, int srcW, int srcH, std::vector<double>& H, int dstW, int dstH)
    {
        std::vector<float> dst = std::vector<float>(dstW * dstH);
        std::vector<double> invH = std::vector<double>(9);
        if (!Invert3x3(H, invH)) return dst;

        for (int dy = 0; dy < dstH; dy++)
        {
            int dOff = dy * dstW;
            for (int dx = 0; dx < dstW; dx++)
            {
                double den = invH[6] * dx + invH[7] * dy + invH[8];
                float sx = (float)((invH[0] * dx + invH[1] * dy + invH[2]) / den);
                float sy = (float)((invH[3] * dx + invH[4] * dy + invH[5]) / den);
                dst[dOff + dx] = SampleBilinearZero(src, srcW, srcH, sx, sy);
            }
        }
        return dst;
    }

    std::vector<float> RemapGrid32(const std::vector<float>& src, int w, int h, const std::vector<float>& gridPtsXY)
    {
        std::vector<float> dst = std::vector<float>(1024);
        for (int i = 0; i < 1024; i++)
            dst[i] = SampleBilinearZero(src, w, h, gridPtsXY[2 * i], gridPtsXY[2 * i + 1]);
        return dst;
    }

    std::vector<float> Rot90Ccw(const std::vector<float>& m, int w, int k)
    {
        k = ((k % 4) + 4) % 4;
        if (k == 0) return m;
        std::vector<float> cur = m;
        for (int step = 0; step < k; step++)
        {
            std::vector<float> next = std::vector<float>(w * w);
            for (int r = 0; r < w; r++)
                for (int c = 0; c < w; c++)
                    next[(w - 1 - c) * w + r] = cur[r * w + c];
            cur = next;
        }
        return cur;
    }
// 保留 C# 的核半径、逐项 float32 累加及 Reflect101 边界，避免几何搜索漂移。
std::vector<float> GaussianBlurF32(const std::vector<float>& src, int w, int h, double sigma) {
    if (sigma <= 1e-5) return src;
    int rad=std::max(1, int(std::nearbyint(sigma*4.0))), count=2*rad+1;
    std::vector<float> kernel(count), tmp(w*h), dst(w*h);
    double sum=0, inverse=1.0/(2.0*sigma*sigma);
    for(int i=-rad;i<=rad;++i) { double v=std::exp(-(i*i)*inverse); kernel[i+rad]=float(v); sum+=v; }
    float invSum=float(1.0/sum);
    for(auto& v:kernel) v*=invSum;
    std::vector<int> mapX(w+2*rad), mapY(h+2*rad);
    for(int i=0;i<int(mapX.size());++i) mapX[i]=Reflect101(i-rad,w);
    for(int i=0;i<int(mapY.size());++i) mapY[i]=Reflect101(i-rad,h)*w;
    for(int y=0;y<h;++y) for(int k=0;k<count;++k) for(int x=0;x<w;++x)
        tmp[y*w+x]+=src[y*w+mapX[x+k]]*kernel[k];
    for(int y=0;y<h;++y) for(int k=0;k<count;++k) for(int x=0;x<w;++x)
        dst[y*w+x]+=tmp[mapY[y+k]+x]*kernel[k];
    return dst;
}
std::vector<uint8_t> DilateRect3x3(const std::vector<uint8_t>& src, int w, int h) {
    return MorphRectU8(src,w,h,3,3,true);
}
std::vector<float> OptimizeClock8Dof(const std::vector<float>& image, int h, int w, int n, const std::vector<float>& corners, float pitch) {
    std::vector<float> output(8);
    optimize_clock_8dof_c(image.data(),h,w,n,corners.data(),pitch,output.data());
    return output;
}

}}}}
