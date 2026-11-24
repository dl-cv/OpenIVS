using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;
using ZXingCpp;

namespace DlcvModules
{
    internal static class DataMatrixRecovery
    {
        private static readonly int[] Sizes = { 10, 12, 14, 16, 18, 20, 22, 24, 26, 32, 36, 40, 44, 48, 52, 64, 72, 80, 88, 96, 104, 120, 132, 144 };
        private static readonly Point2f[] Target = { new Point2f(20, 20), new Point2f(200, 20), new Point2f(200, 200), new Point2f(20, 200) };
        private sealed class Geometry
        {
            public double Score = -1e9;
            public int Size, Regions, Rotation;
            public Point2f[] Corners;
        }
        private static Mat Blur(Mat a, double sigma)
        {
            var b = new Mat(); Cv2.GaussianBlur(a, b, new Size(), sigma); return b;
        }
        private static Mat Float(Mat a)
        {
            var b = new Mat(); a.ConvertTo(b, MatType.CV_32F); return b;
        }
        private static Mat Open(Mat a)
        {
            var b = new Mat();
            using (var kernel = new Mat(3, 3, MatType.CV_8U, Scalar.All(1))) Cv2.MorphologyEx(a, b, MorphTypes.Open, kernel);
            return b;
        }
        private static Mat Sharpen(Mat a, double sigma, double gain)
        {
            var b = new Mat();
            using (var sm = Blur(a, sigma)) Cv2.AddWeighted(a, 1 + gain, sm, -gain, 0, b);
            return b;
        }
        private static Point2f[] Order(Point2f[] p)
        {
            var center = new Point2f(p.Average(q => q.X), p.Average(q => q.Y));
            var sorted = p.OrderBy(q => Math.Atan2(q.Y - center.Y, q.X - center.X)).ToArray();
            int first = 0;
            for (int i = 1; i < 4; ++i) if (sorted[i].X + sorted[i].Y < sorted[first].X + sorted[first].Y) first = i;
            return Enumerable.Range(0, 4).Select(i => sorted[(first + i) % 4]).ToArray();
        }
        private static Mat Projection(Point2f[] p) => Cv2.GetPerspectiveTransform(Order(p), Target);
        private static Mat Rect(Mat a, Mat projection)
        {
            var b = new Mat(); Cv2.WarpPerspective(a, b, projection, new Size(220, 220), InterpolationFlags.Cubic); return b;
        }
        private static Point2f[] ImageQuad(int w, int h, int pad = 0) => new[] {
            new Point2f(pad, pad), new Point2f(pad+w-1, pad), new Point2f(pad+w-1, pad+h-1), new Point2f(pad, pad+h-1) };
        private static int RotIndex(int y, int x, int n, int r)
        {
            switch (r & 3) { case 1: return (n - 1 - x) * n + y; case 2: return (n - 1 - y) * n + n - 1 - x; case 3: return x * n + n - 1 - y; default: return y * n + x; }
        }
        private static Point2f[] RotateGrid(Point2f[] p, int n, int r)
        {
            var q = new Point2f[p.Length]; for (int i = 0; i < q.Length; ++i) q[i] = p[RotIndex(i / n, i % n, n, r)]; return q;
        }
        private static int[] Mask(int n, int k, int rot = 0)
        {
            var m = new int[n * n]; int s = n / k;
            for (int y = 0; y < n; y += s) for (int x = 0; x < n; x += s)
                {
                    for (int i = 0; i < s; ++i) m[RotIndex(y, x + i, n, rot)] = i % 2 == 0 ? -1 : 1;
                    for (int i = 0; i < s; ++i) m[RotIndex(y + i, x + s - 1, n, rot)] = i % 2 == 0 ? 1 : -1;
                    for (int i = 0; i < s; ++i) m[RotIndex(y + s - 1, x + i, n, rot)] = -1;
                    for (int i = 0; i < s; ++i) m[RotIndex(y + i, x, n, rot)] = -1;
                }
            return m;
        }
        private static Point2f Scale(Point2f p, float v) => new Point2f(p.X * v, p.Y * v);
        private static Point2f Cell(Point2f[] q, float x, float y) => Scale(Scale(q[0], 1 - x) + Scale(q[1], x), 1 - y) + Scale(Scale(q[3], 1 - x) + Scale(q[2], x), y);
        private static Point2f Weighted(Point2f[] q, float x, float y) => Scale(q[0], (1 - x) * (1 - y)) + Scale(q[1], x * (1 - y)) + Scale(q[2], x * y) + Scale(q[3], (1 - x) * y);
        private static Point2f[] Maps(int n, Point2f[] q)
        {
            var p = new Point2f[n * n]; for (int i = 0; i < p.Length; ++i) p[i] = Cell(q, (i % n + .5f) / n, (i / n + .5f) / n); return p;
        }
        private static T[] ArrayOf<T>(Mat a) where T : unmanaged
        {
            if (!a.GetArray(out T[] values)) throw new InvalidOperationException("Cannot read Data Matrix image samples");
            return values;
        }
        private static Mat FloatImage(float[] values, int n)
        {
            var m = new Mat(n, n, MatType.CV_32FC1); m.SetArray(values); return m;
        }
        // Match OpenCV remap's 1/32-pixel interpolation table without per-sample native calls.
        private sealed class Raster
        {
            private readonly float[] pixels;
            private readonly int cols, rows;
            public Raster(Mat a) { pixels = ArrayOf<float>(a); cols = a.Cols; rows = a.Rows; }
            private float At(int x, int y, bool replicate)
            {
                if (replicate) return pixels[Math.Max(0, Math.Min(rows - 1, y)) * cols + Math.Max(0, Math.Min(cols - 1, x))];
                return x >= 0 && x < cols && y >= 0 && y < rows ? pixels[y * cols + x] : 0;
            }
            public float Pixel(Point2f p, bool replicate = false)
            {
                int ix = (int)Math.Round(p.X * 32), iy = (int)Math.Round(p.Y * 32), x = ix >> 5, y = iy >> 5;
                float u = (ix & 31) * (1f / 32), v = (iy & 31) * (1f / 32), a = (1 - u) * (1 - v), b = u * (1 - v), c = (1 - u) * v, d = u * v;
                if (x >= 0 && x < cols - 1 && y >= 0 && y < rows - 1) { int i = y * cols + x; return pixels[i] * a + pixels[i + 1] * b + pixels[i + cols] * c + pixels[i + cols + 1] * d; }
                return At(x, y, replicate) * a + At(x + 1, y, replicate) * b + At(x, y + 1, replicate) * c + At(x + 1, y + 1, replicate) * d;
            }
            public float[] Sample(Point2f[] p)
            {
                var values = new float[p.Length]; for (int i = 0; i < p.Length; ++i) values[i] = Pixel(p[i]); return values;
            }
        }
        private static Geometry Score(Mat a, Point2f[] p, int known = 0)
        {
            var im = new Raster(a); var best = new Geometry();
            foreach (int n in Sizes)
            {
                if (known != 0 && n != known) continue;
                if (known == 0 && n > Math.Min(a.Cols, a.Rows) * .6) break;
                int k = n <= 26 ? 1 : n <= 52 ? 2 : n <= 104 ? 4 : 6, s = n / k, count = (s - 2) / 2;
                var grid = Maps(n, p); var v = new float[n * n];
                for (int dx = -2; dx <= 2; dx += 2) for (int dy = -2; dy <= 2; dy += 2)
                    {
                        var offset = new Point2f(dx, dy);
                        for (int i = 0; i < v.Length; ++i) v[i] = im.Pixel(grid[i] + offset, true);
                        for (int r = 0; r < 4; ++r)
                        {
                            double score = 0;
                            for (int y = 0; y < n; y += s) for (int x = 0; x < n; x += s) for (int edge = 0; edge < 2; ++edge)
                                    {
                                        double w = 0, b = 0, sq = 0;
                                        for (int i = 1; i < s - 1; i += 2)
                                        {
                                            float f = edge == 1 ? v[RotIndex(y + i + 1, x + s - 1, n, r)] : v[RotIndex(y, x + i, n, r)];
                                            float g = edge == 1 ? v[RotIndex(y + i, x + s - 1, n, r)] : v[RotIndex(y, x + i + 1, n, r)];
                                            w += f; b += g; sq += f * f + g * g;
                                        }
                                        double m = (w + b) / (2 * count); score += (w - b) / count / (Math.Sqrt(Math.Max(0, sq / (2 * count) - m * m)) + 5);
                                    }
                            score *= Math.Sqrt(n * k) / (2 * k * k);
                            if (score > best.Score) best = new Geometry { Score = score, Size = n, Regions = k, Rotation = r, Corners = p.Select(q => q + offset).ToArray() };
                        }
                    }
            }
            return best;
        }
        // Bounded Brent search along Powell directions.
        // 参考 SciPy 1.13.1，许可见 third_party/scipy/LICENSE.txt。
        private static double Line(Func<double[], double> f, double[] p, double[] d, double limit, double tol)
        {
            double a = -1e10, b = 1e10; bool moving = false;
            for (int i = 0; i < p.Length; ++i) if (d[i] != 0)
                {
                    moving = true; double l = (-limit - p[i]) / d[i], h = (limit - p[i]) / d[i]; a = Math.Max(a, Math.Min(l, h)); b = Math.Min(b, Math.Max(l, h));
                }
            if (!moving || a >= b) { Array.Clear(d, 0, d.Length); return f(p); }
            var trial = new double[p.Length];
            double At(double t) { for (int i = 0; i < p.Length; ++i) trial[i] = p[i] + t * d[i]; return f(trial); }
            const double golden = .3819660112501051;
            double x = a + golden * (b - a), w = x, v = x, fx = At(x), fw = fx, fv = fx, e = 0, step = 0;
            for (int i = 0; i < 80; ++i)
            {
                double m = (a + b) * .5, t1 = 1.4832396974191326e-8 * Math.Abs(x) + tol / 3, t2 = 2 * t1;
                if (Math.Abs(x - m) <= t2 - (b - a) * .5) break;
                bool parabola = false;
                if (Math.Abs(e) > t1)
                {
                    double r = (x - w) * (fx - fv), q = (x - v) * (fx - fw), u = (x - v) * q - (x - w) * r;
                    q = 2 * (q - r); if (q > 0) u = -u; q = Math.Abs(q); double old = e; e = step;
                    if (Math.Abs(u) < Math.Abs(.5 * q * old) && u > q * (a - x) && u < q * (b - x))
                    { step = u / q; parabola = true; if (x + step - a < t2 || b - x - step < t2) step = m >= x ? t1 : -t1; }
                }
                if (!parabola) { e = x >= m ? a - x : b - x; step = golden * e; }
                double z = x + (step >= 0 ? 1 : -1) * Math.Max(Math.Abs(step), t1), fz = At(z);
                if (fz <= fx) { if (z >= x) a = x; else b = x; v = w; fv = fw; w = x; fw = fx; x = z; fx = fz; }
                else { if (z < x) a = z; else b = z; if (fz <= fw || w == x) { v = w; fv = fw; w = z; fw = fz; } else if (fz <= fv || v == x || v == w) { v = z; fv = fz; } }
            }
            for (int i = 0; i < p.Length; ++i) { d[i] *= x; p[i] += d[i]; }
            return fx;
        }
        private static double[] Powell(Func<double[], double> f, double[] p, double limit, int iterations, double xtol, double ftol)
        {
            p = (double[])p.Clone(); var dirs = new double[8][]; for (int i = 0; i < 8; ++i) { dirs[i] = new double[8]; dirs[i][i] = 1; }
            var prev = (double[])p.Clone(); double value = f(p);
            for (int it = 0; it < iterations; ++it)
            {
                double start = value, delta = 0; int best = 0;
                for (int i = 0; i < 8; ++i) { var d = (double[])dirs[i].Clone(); double before = value; value = Line(f, p, d, limit, xtol); if (before - value > delta) { delta = before - value; best = i; } }
                if (2 * (start - value) <= ftol * (Math.Abs(start) + Math.Abs(value)) + 1e-20 || it + 1 == iterations) break;
                var dir = new double[8]; double max = 1;
                for (int i = 0; i < 8; ++i) { dir[i] = p[i] - prev[i]; if (dir[i] != 0) max = Math.Min(max, ((dir[i] > 0 ? limit : -limit) - p[i]) / dir[i]); prev[i] = p[i]; }
                var extrap = new double[8]; for (int i = 0; i < 8; ++i) extrap[i] = p[i] + max * dir[i]; double extra = f(extrap);
                if (start > extra) { double t = start - value - delta, u = start - extra; if (2 * (start + extra - 2 * value) * t * t - delta * u * u < 0) { value = Line(f, p, dir, limit, xtol); if (dir.Any(d => d != 0)) { dirs[best] = dirs[7]; dirs[7] = dir; } } }
            }
            return p;
        }
        private static Point2f[] Shift(double[] p, Point2f[] q = null)
        {
            var r = new Point2f[4]; for (int i = 0; i < 4; ++i) r[i] = new Point2f((float)((q?[i].X ?? 0) + p[2 * i]), (float)((q?[i].Y ?? 0) + p[2 * i + 1])); return r;
        }
        private static Point2f[] Optimize(Mat im, Geometry g)
        {
            using (var sm = Blur(im, 1.15)) using (var bg = Blur(sm, 4.5)) using (var difference = new Mat())
            {
                Cv2.Subtract(sm, bg, difference); var high = new Raster(difference); int n = g.Size; var mask = Mask(n, g.Regions, g.Rotation);
                int[] indices = Enumerable.Range(0, n * n).Where(i => mask[i] != 0).ToArray();
                double Loss(double[] p)
                {
                    var q = Shift(p, g.Corners); double a = 0, b = 0, sa = 0, sb = 0; int na = 0, nb = 0;
                    foreach (int i in indices) { float v = high.Pixel(Weighted(q, (i % n + .5f) / n, (i / n + .5f) / n)); if (mask[i] < 0) { a += v; sa += v * v; ++na; } else { b += v; sb += v * v; ++nb; } }
                    a /= na; b /= nb; return -(b - a) / Math.Sqrt(sa / na + sb / nb - a * a - b * b + 6) + .001 * p.Sum(v => v * v) / 8;
                }
                var corners = Shift(Powell(Loss, new double[8], 7, 12, .12, .001), g.Corners); var grid = new Point2f[n * n];
                for (int i = 0; i < grid.Length; ++i) grid[i] = Weighted(corners, (i % n + .5f) / n, (i / n + .5f) / n);
                return RotateGrid(grid, n, g.Rotation);
            }
        }
        private static void FitRegions(Mat im, Point2f[] p, int n, int k)
        {
            using (var mean = new Mat())
            {
                Cv2.Blur(im, mean, new Size(3, 3));
                using (var filtered = Sharpen(mean, 2, 2.8))
                {
                    var sharp = new Raster(filtered); int s = n / k;
                    for (int y = 0; y < n; y += s) for (int x = 0; x < n; x += s)
                        {
                            double Loss(double[] a)
                            {
                                var shift = Shift(a); double score = 0;
                                float Value(int j, int i) => sharp.Pixel(p[(y + j) * n + x + i] + Weighted(shift, (i + .5f) / s, (j + .5f) / s));
                                for (int i = 1; i < s - 1; i += 2) score += Math.Min(22f, Value(0, i) - Value(0, i + 1)) + Math.Min(22f, Value(i + 1, s - 1) - Value(i, s - 1));
                                return -score / ((s - 2) / 2) + .008 * a.Sum(v => v * v);
                            }
                            double[] best = null; double value = 1e10;
                            for (double dx = -2; dx <= 2; ++dx) for (double dy = -2; dy <= 2; ++dy) { var q = new[] { dx, dy, dx, dy, dx, dy, dx, dy }; double f = Loss(q); if (f < value) { value = f; best = q; } }
                            var fit = Powell(Loss, best, 4, 10, .13, .005); var corners = Shift(Loss(fit) < value ? fit : best);
                            for (int j = 0; j < s; ++j) for (int i = 0; i < s; ++i) p[(y + j) * n + x + i] += Weighted(corners, (i + .5f) / s, (j + .5f) / s);
                        }
                }
            }
        }
        private static Point2f[] Gradient(Point2f[] p, int n, bool horizontal)
        {
            var g = new Point2f[n * n];
            for (int i = 0; i < g.Length; ++i) { int j = horizontal ? i % n : i / n, d = horizontal ? 1 : n; g[i] = Scale(p[i + (j < n - 1 ? d : 0)] - p[i - (j > 0 ? d : 0)], j == 0 || j == n - 1 ? 1 : .5f); }
            return g;
        }
        private static Point2f[] Displace(Point2f[] p, Point2f[] dx, Point2f[] dy, float x, float y)
        {
            var q = new Point2f[p.Length]; for (int i = 0; i < p.Length; ++i) q[i] = p[i] + Scale(dx[i], x) + Scale(dy[i], y); return q;
        }
        private static double Length(Point2f p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);
        private static double Percentile(float[] sorted, double rank)
        {
            double p = (sorted.Length - 1) * rank; int i = (int)p; return sorted[i] + (p - i) * (sorted[Math.Min(i + 1, sorted.Length - 1)] - sorted[i]);
        }
        private static float[] GrayValues(Mat a)
        {
            int[] hist = new int[256]; foreach (byte b in ArrayOf<byte>(a)) ++hist[b];
            var v = new float[checked(a.Rows * a.Cols)]; for (int b = 0, i = 0; b < 256; ++b) for (int j = 0; j < hist[b]; ++j) v[i++] = b; return v;
        }
        private static double Std(float[] v)
        {
            double sum = 0, sq = 0; foreach (float x in v) { sum += x; sq += x * x; }
            return Math.Sqrt(Math.Max(0, sq / v.Length - Math.Pow(sum / v.Length, 2)));
        }
        private static float[] Blur(float[] a, int n, double sigma)
        {
            using (var m = FloatImage(a, n)) using (var b = Blur(m, sigma)) return ArrayOf<float>(b);
        }
        private static Point2f[] Refine(Mat im, Point2f[] p, int[] mask, int n)
        {
            var dx = Gradient(p, n, true); var dy = Gradient(p, n, false);
            var lens = dx.Select(u => (float)Length(u)).ToArray(); Array.Sort(lens);
            using (var sm = Blur(im, Percentile(lens, .5) * .22)) using (var sx = new Mat()) using (var sy = new Mat()) using (var gx = new Mat()) using (var gy = new Mat())
            {
                Cv2.Sobel(sm, sx, MatType.CV_32F, 1, 0); Cv2.Sobel(sm, sy, MatType.CV_32F, 0, 1);
                Point2f Axis(Point2f[] g) { var l = new Point2f(g.Average(u => u.X), g.Average(u => u.Y)); return Scale(l, (float)(1 / Length(l))); }
                var ax = Axis(dx); var ay = Axis(dy); Cv2.AddWeighted(sx, ax.X, sy, ax.Y, 0, gx); Cv2.AddWeighted(sx, ay.X, sy, ay.Y, 0, gy);
                var smooth = new Raster(sm); var rx = new Raster(gx); var ry = new Raster(gy); var all = new float[81][];
                var max = Enumerable.Repeat(-1e9f, n * n).ToArray();
                float Contrast(Raster g, Point2f pos, Point2f d) { float a = g.Pixel(pos + Scale(d, .5f)), b = g.Pixel(pos - Scale(d, .5f)), c = g.Pixel(pos); return a * a + b * b - 2 * c * c; }
                for (int ix = -4; ix <= 4; ++ix) for (int iy = -4; iy <= 4; ++iy)
                    {
                        float x = ix * .1f, y = iy * .1f; var q = Displace(p, dx, dy, x, y); var v = smooth.Sample(q); var vb = Blur(v, n, 2.5); var e = new float[n * n];
                        for (int i = 0; i < e.Length; ++i) { v[i] -= vb[i]; e[i] = Contrast(rx, q[i], dx[i]) + Contrast(ry, q[i], dy[i]); }
                        double es = 1 + Std(e), vs = 1 + Std(v);
                        for (int i = 0; i < e.Length; ++i) e[i] = (float)(e[i] / es + v[i] * mask[i] / vs);
                        e = Blur(e, n, 2); float penalty = .5f * (x * x + y * y);
                        for (int i = 0; i < e.Length; ++i) { e[i] -= penalty; max[i] = Math.Max(max[i], e[i]); }
                        all[(ix + 4) * 9 + iy + 4] = e;
                    }
                var uoff = new float[n * n]; var voff = new float[n * n];
                for (int i = 0; i < uoff.Length; ++i)
                {
                    double a = 0, b = 0, w = 0;
                    for (int j = 0; j < 81; ++j) { double v = Math.Exp((all[j][i] - max[i]) / .09f); a += v * (j / 9 - 4) * .1; b += v * (j % 9 - 4) * .1; w += v; }
                    uoff[i] = (float)(a / w); voff[i] = (float)(b / w);
                }
                uoff = Blur(uoff, n, 1.4); voff = Blur(voff, n, 1.4);
                var result = new Point2f[p.Length]; for (int i = 0; i < result.Length; ++i) result[i] = p[i] + Scale(dx[i], uoff[i]) + Scale(dy[i], voff[i]); return result;
            }
        }
        private static double[] Basis(int i, int n)
        {
            double x = 2d * (i % n) / (n - 1) - 1, y = 2d * (i / n) / (n - 1) - 1; return new[] { 1d, x, y, x * x, x * y, y * y };
        }
        private static double[] Plane(float[] z, int[] mask, int n, int sign)
        {
            int count = mask.Count(m => m == sign); var samples = new double[count]; var design = new double[count * 6];
            for (int i = 0, j = 0; i < z.Length; ++i) if (mask[i] == sign) { Array.Copy(Basis(i, n), 0, design, j * 6, 6); samples[j++] = z[i]; }
            using (var a = new Mat(count, 6, MatType.CV_64FC1)) using (var b = new Mat(count, 1, MatType.CV_64FC1)) using (var fit = new Mat())
            {
                double[] Solve() { a.SetArray(design); b.SetArray(samples); Cv2.Solve(a, b, fit, DecompTypes.SVD); return ArrayOf<double>(fit); }
                var c = Solve();
                for (int j = 0; j < count; ++j) { double r = -samples[j]; int o = j * 6; for (int k = 0; k < 6; ++k) r += design[o + k] * c[k]; r /= 8; double w = 1 / (1 + r * r); for (int k = 0; k < 6; ++k) design[o + k] *= w; samples[j] *= w; }
                c = Solve(); var outv = new double[z.Length];
                for (int i = 0; i < outv.Length; ++i) { var v = Basis(i, n); for (int j = 0; j < 6; ++j) outv[i] += v[j] * c[j]; }
                return outv;
            }
        }
        private static double[] SurfaceDecision(float[] z, int[] mask, int n, double fraction, bool normalized = false)
        {
            var lo = Plane(z, mask, n, -1); var hi = Plane(z, mask, n, 1);
            var w0 = new float[z.Length]; var w1 = new float[z.Length]; var s0 = new float[z.Length]; var s1 = new float[z.Length];
            for (int i = 0; i < z.Length; ++i)
            {
                double v = (z[i] - lo[i]) / Math.Max(1, hi[i] - lo[i]);
                w0[i] = mask[i] < 0 ? 2 : mask[i] == 0 && v < .28 ? 1 : 0; w1[i] = mask[i] > 0 ? 2 : mask[i] == 0 && v > .72 ? 1 : 0; s0[i] = z[i] * w0[i]; s1[i] = z[i] * w1[i];
            }
            w0 = Blur(w0, n, 3); w1 = Blur(w1, n, 3); s0 = Blur(s0, n, 3); s1 = Blur(s1, n, 3);
            for (int i = 0; i < z.Length; ++i)
            {
                double lower = (s0[i] + .12 * lo[i]) / (w0[i] + .12), upper = (s1[i] + .12 * hi[i]) / (w1[i] + .12);
                lo[i] = normalized ? (z[i] - lower) / Math.Max(1, upper - lower) - fraction : z[i] - (lower + (upper - lower) * fraction);
            }
            return lo;
        }
        private static int Reflect(int i, int n) => i < 0 ? -i - 1 : i >= n ? 2 * n - i - 1 : i;
        private static void Select(float[] a, int left, int right, int k)
        {
            while (left < right)
            {
                float pivot = a[(left + right) / 2]; int l = left, r = right;
                while (l <= r) { while (a[l] < pivot) ++l; while (a[r] > pivot) --r; if (l <= r) { float v = a[l]; a[l++] = a[r]; a[r--] = v; } }
                if (k <= r) right = r; else if (k >= l) left = l; else return;
            }
        }
        private static double[] QuantileDecision(float[] z, int n, float fraction = .47f, bool normalized = false)
        {
            var lo = new float[z.Length]; var hi = new float[z.Length]; var w = new float[49];
            for (int y = 0; y < n; ++y) for (int x = 0; x < n; ++x)
                {
                    int i = y * n + x; for (int j = -3, k = 0; j <= 3; ++j) for (int s = -3; s <= 3; ++s) w[k++] = z[Reflect(y + j, n) * n + Reflect(x + s, n)];
                    Select(w, 0, 48, 9); Select(w, 10, 48, 39); lo[i] = w[9]; hi[i] = w[39];
                }
            lo = Blur(lo, n, 2); hi = Blur(hi, n, 2); var d = new double[z.Length];
            for (int i = 0; i < d.Length; ++i)
            {
                float span = hi[i] - lo[i];
                d[i] = normalized ? (z[i] - lo[i]) / Math.Max(1, span) - fraction : z[i] - (lo[i] + fraction * span);
            }
            return d;
        }
        private static string CheckBits(double[] decision, int[] mask, int n, ZXingCpp.BarcodeReader pure, int flip = -1, int retry = 0)
        {
            int s = n + 6;
            var bitmap = Enumerable.Repeat((byte)255, s * s).ToArray();
            int Cell(int i) => (i / n + 3) * s + i % n + 3;
            for (int i = 0; i < mask.Length; ++i)
                if (mask[i] < 0 || (mask[i] == 0 && ((decision[i] < 0) ^ (i == flip)))) bitmap[Cell(i)] = 0;
            using (var image = new Mat(s, s, MatType.CV_8UC1))
            {
                string ReadBitmap()
                {
                    image.SetArray(bitmap);
                    var view = new ImageView(image.Data, s, s, ImageFormat.Lum, s);
                    var codes = pure.From(view) ?? Array.Empty<Barcode>();
                    try { return codes.FirstOrDefault(b => b.IsValid)?.Text; }
                    finally { foreach (var b in codes) b.Dispose(); GC.KeepAlive(image); }
                }
                var hit = ReadBitmap();
                if (hit != null || retry == 0) return hit;
                var cells = Enumerable.Range(0, mask.Length).Where(i => mask[i] == 0)
                    .OrderBy(i => Math.Abs(decision[i])).Take(retry).Select(Cell).ToArray();
                void Toggle(int i) => bitmap[cells[i]] ^= 255;
                for (int i = 0; i < cells.Length; ++i)
                {
                    Toggle(i);
                    hit = ReadBitmap();
                    Toggle(i);
                    if (hit != null) return hit;
                }
                for (int i = 0; i < cells.Length; ++i)
                {
                    Toggle(i);
                    for (int j = i + 1; j < cells.Length; ++j)
                    {
                        Toggle(j);
                        hit = ReadBitmap();
                        Toggle(j);
                        if (hit != null) return hit;
                    }
                    Toggle(i);
                }
                return null;
            }
        }
        private static string Probe(Mat im, Point2f[] p, int[] mask, int n, bool official, ZXingCpp.BarcodeReader pure)
        {
            using (var opened = Open(im)) using (var blurred = Blur(opened, 1.35)) using (var sharp = Sharpen(blurred, 2.1, 3))
            {
                var z = new Raster(sharp).Sample(p);
                return CheckBits(QuantileDecision(z, n), mask, n, pure) ?? (official ? CheckBits(SurfaceDecision(z, mask, n, .5), mask, n, pure) : null);
            }
        }
        private static string ExtraProbe(Mat im, Point2f[] p, int[] mask, int n, ZXingCpp.BarcodeReader pure)
        {
            using (var blurred = Blur(im, 1.15)) using (var sharp = Sharpen(blurred, 2.1, 3))
            {
                var d = SurfaceDecision(new Raster(sharp).Sample(p), mask, n, .47, true);
                var hit = CheckBits(d, mask, n, pure); if (hit != null) return hit;
                double closest = 1e20; int cell = -1;
                for (int i = 0; i < d.Length; ++i) if (mask[i] == 0 && Math.Abs(d[i]) < closest) { closest = Math.Abs(d[i]); cell = i; }
                hit = CheckBits(d, mask, n, pure, cell); if (hit != null) return hit;
            }
            using (var opened = Open(im)) using (var blurred = Blur(opened, 1.15)) using (var sharp = Sharpen(blurred, 2.1, 5))
            {
                var sm = new Raster(sharp); var dx = Gradient(p, n, true); var dy = Gradient(p, n, false);
                foreach (float y in new[] { -.1f, .1f }) foreach (float x in new[] { -.1f, .1f })
                    {
                        var hit = CheckBits(QuantileDecision(sm.Sample(Displace(p, dx, dy, x, y)), n), mask, n, pure); if (hit != null) return hit;
                    }
            }
            return null;
        }
        private static Point2f[] MapCorners(Point2f[] p, int n, Mat m)
        {
            var grid = p.Select((_, i) => new Point2d(i % n, i / n));
            using (var h = Cv2.FindHomography(grid, p.Select(q => new Point2d(q.X, q.Y)))) using (var inverse = new Mat()) using (var final = new Mat()) using (var empty = new Mat())
            {
                if (h.Empty()) return null;
                Cv2.Invert(m, inverse); Cv2.Gemm(inverse, h, 1, empty, 0, final);
                return Cv2.PerspectiveTransform(new[] { new Point2f(-.5f, -.5f), new Point2f(n - .5f, -.5f), new Point2f(n - .5f, n - .5f), new Point2f(-.5f, n - .5f) }, final);
            }
        }
        private static Point2f[] Seed(Mat a)
        {
            int w = a.Cols, h = a.Rows; var opts = new List<(double score, Point2f[] points)>();
            using (var sm = Blur(a, Math.Max(1.3, 3.25 * Math.Max(w, h) / 300))) using (var bw = new Mat())
            using (var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(7, 7)))
            {
                double t = Cv2.Threshold(sm, bw, 0, 255, ThresholdTypes.Otsu); var v = GrayValues(sm);
                foreach (double value in new[] { t, Percentile(v, .25), Percentile(v, .35), Percentile(v, .45), Percentile(v, .55) })
                {
                    Cv2.Threshold(sm, bw, Math.Ceiling(value) - 1, 255, ThresholdTypes.BinaryInv); Cv2.MorphologyEx(bw, bw, MorphTypes.Close, kernel);
                    Cv2.FindContours(bw, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                    foreach (var c in contours)
                    {
                        double area = Cv2.ContourArea(c); if (area < Math.Max(80, .035 * w * h)) continue;
                        var hull = Cv2.ConvexHull(c); double perimeter = Cv2.ArcLength(hull, true); OpenCvSharp.Point[] q = null;
                        foreach (double f in new[] { .015, .025, .04, .055, .075 }) { q = Cv2.ApproxPolyDP(hull, perimeter * f, true); if (q.Length == 4) break; }
                        if (q.Length != 4) continue;
                        var p = Order(q.Select(ps => new Point2f(ps.X, ps.Y)).ToArray()); var edges = p.Select((ps, i) => Length(ps - p[(i + 1) % 4])).ToArray();
                        double aspect = edges.Min() / Math.Max(1, edges.Max()), fill = Math.Min(1, area / Math.Max(1, Cv2.ContourArea(p)));
                        if (aspect > .53) opts.Add((Math.Pow(area / ((double)w * h), .25) * aspect * fill, p));
                    }
                }
            }
            if (opts.Count == 0) return Math.Min(w, h) > .6 * Math.Max(w, h) ? ImageQuad(w, h) : null;
            var top = opts.OrderByDescending(o => o.score).First().points;
            var near = opts.Where(o => Distance(o.points, top) < Math.Max(6, Math.Min(w, h) * .07)).Select(o => o.points).ToArray();
            return Enumerable.Range(0, 4).Select(i => new Point2f((float)Percentile(near.Select(p => p[i].X).OrderBy(v => v).ToArray(), .5), (float)Percentile(near.Select(p => p[i].Y).OrderBy(v => v).ToArray(), .5))).ToArray();
        }
        private static double Distance(Point2f[] a, Point2f[] b)
        {
            double m = 0; for (int i = 0; i < 4; ++i) m = Math.Max(m, Math.Max(Math.Abs(a[i].X - b[i].X), Math.Abs(a[i].Y - b[i].Y))); return m;
        }
        private static double Cross(Point2f a, Point2f b) => a.X * b.Y - a.Y * b.X;
        private static Point2f[] FitEdges(Mat im)
        {
            using (var a = Float(im)) using (var sm = Blur(a, 1.4))
            {
                var img = new Raster(sm); var starts = new Point2f[4]; var ends = new Point2f[4];
                for (int i = 0; i < 4; ++i)
                {
                    var p = Target[i]; var d = Target[(i + 1) % 4] - p; var normal = Scale(new Point2f(-d.Y, d.X), (float)(1 / Length(d)));
                    double best = -1e9; int offset = 0, delta = 0;
                    for (int o = -22; o <= 22; ++o) for (int z = -26; z <= 26; z += 2)
                        {
                            double score = 0; for (int j = 0; j < 56; ++j) { float f = (float)(.08 + .84 * j / 55); var r = p + Scale(d, f) + Scale(normal, o + z * (f - .5f) * 2); score += img.Pixel(r - Scale(normal, 3), true) - img.Pixel(r + Scale(normal, 3), true) + .4 * (img.Pixel(r - Scale(normal, 7), true) - img.Pixel(r + Scale(normal, 7), true)); }
                            if (score > best) { best = score; offset = o; delta = z; }
                        }
                    starts[i] = p + Scale(normal, offset - delta); ends[i] = p + d + Scale(normal, offset + delta);
                }
                var q = new Point2f[4];
                for (int i = 0; i < 4; ++i) { int prev = (i + 3) % 4; var d = ends[prev] - starts[prev]; var e = ends[i] - starts[i]; double det = Cross(d, e); if (Math.Abs(det) < 1e-6) return null; q[i] = starts[prev] + Scale(d, (float)(Cross(starts[i] - starts[prev], e) / det)); }
                return q;
            }
        }
        internal sealed class Result
        {
            public readonly string Text;
            public readonly Point2f[] Corners;
            public Result(string text, Point2f[] corners, int pad)
            {
                Text = text; Corners = corners.Select(p => new Point2f(p.X - pad, p.Y - pad)).ToArray();
            }
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int VerifyBitmap(IntPtr bitmap, IntPtr context);

        [DllImport("dlcv_datamatrix_runtime.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "dlcv_datamatrix_read_v1")]
        private static extern int ReadNative(IntPtr gray, int width, int height, int stride,
            int flags, [Out] float[] corners, VerifyBitmap verify, IntPtr context);

        internal static Result RecoverDeep(Mat gray, bool rotate, bool invert)
        {
            if (gray.Rows < 32 || gray.Cols < 32) return null;
            using (var pure = new ZXingCpp.BarcodeReader { Formats = BarcodeFormat.DataMatrix, TryDownscale = false,
                TryRotate = false, TryInvert = false, IsPure = true, TextMode = TextMode.HRI })
            {
                string text = null;
                Exception verifyError = null;
                VerifyBitmap verify = (bitmap, context) =>
                {
                    try
                    {
                        var view = new ImageView(bitmap, 38, 38, ImageFormat.Lum, 38);
                        var codes = pure.From(view) ?? Array.Empty<Barcode>();
                        try { text = codes.FirstOrDefault(b => b.IsValid && b.Format == BarcodeFormat.DataMatrix)?.Text; }
                        finally { foreach (var b in codes) b.Dispose(); }
                        return text == null ? 0 : 1;
                    }
                    catch (Exception error) { verifyError = error; return 0; }
                };
                var corners = new float[8];
                int found = ReadNative(gray.Data, gray.Cols, gray.Rows, checked((int)gray.Step()),
                    (rotate ? 1 : 0) | (invert ? 2 : 0), corners, verify, IntPtr.Zero);
                GC.KeepAlive(verify);
                GC.KeepAlive(gray);
                if (verifyError != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(verifyError).Throw();
                return found == 0 ? null : new Result(text, Enumerable.Range(0, 4)
                    .Select(i => new Point2f(corners[2 * i], corners[2 * i + 1])).ToArray(), 0);
            }
        }

        private sealed class Row : IDisposable
        {
            public Geometry Geometry;
            public Mat Image, Projection;
            public Point2f[] Outline;
            public void Dispose() { Image?.Dispose(); Projection.Dispose(); }
        }
        private static double[] GridDistance(Mat opened, Point2f[] grid, int[] mask, int n,
            double sigma, float gain, double fraction, bool rank = false, float x = 0, float y = 0)
        {
            var dx = Gradient(grid, n, true);
            double pitch = dx.Average(Length);
            using (var sm = Blur(opened, pitch * sigma))
            using (var bg = Blur(sm, pitch * (sigma == .22 ? .45 : .46)))
            {
                var shifted = x != 0 || y != 0 ? Displace(grid, dx, Gradient(grid, n, false), x, y) : grid;
                var z = new Raster(sm).Sample(shifted);
                var baseLevel = new Raster(bg).Sample(shifted);
                for (int i = 0; i < z.Length; ++i) z[i] += gain * (z[i] - baseLevel[i]);
                return rank ? QuantileDecision(z, n, (float)fraction, true) : SurfaceDecision(z, mask, n, fraction, true);
            }
        }
        private static Geometry LocateRoiGrid(Mat gray)
        {
            var seed = Seed(gray);
            if (seed == null) return null;
            using (var firstProjection = Projection(seed))
            using (var firstInverse = new Mat())
            using (var first = Rect(gray, firstProjection))
            {
                var edge = FitEdges(first);
                if (edge == null) return null;
                Cv2.Invert(firstProjection, firstInverse);
                var points = Cv2.PerspectiveTransform(edge, firstInverse);
                using (var projection = Projection(points))
                using (var inverse = new Mat())
                using (var rect = Rect(gray, projection))
                using (var im = Float(rect))
                using (var sm = Blur(im, 1.1))
                using (var bg = Blur(sm, 4))
                using (var high = new Mat())
                {
                    Cv2.Subtract(sm, bg, high);
                    var geometry = Score(high, Target);
                    if (geometry.Score < 3) return null;
                    Cv2.Invert(projection, inverse);
                    geometry.Corners = Cv2.PerspectiveTransform(geometry.Corners, inverse);
                    return geometry;
                }
            }
        }
        private static Result RecoverRoiGrid(Mat gray, ZXingCpp.BarcodeReader pure)
        {
            var geometry = LocateRoiGrid(gray);
            if (geometry == null) return null;
            int n = geometry.Size;
            var mask = Mask(n, geometry.Regions);
            using (var im = Float(gray))
            using (var opened8 = Open(gray))
            using (var opened = Float(opened8))
            using (var projection = new Mat(3, 3, MatType.CV_64FC1))
            {
                projection.SetArray(new[] { 1d, 0, 0, 0, 1, 0, 0, 0, 1 });
                Result Place(string text, Point2f[] grid)
                {
                    if (text == null) return null;
                    var corners = MapCorners(grid, n, projection);
                    return corners == null ? null : new Result(text, corners, 0);
                }
                var raw = Optimize(im, geometry);
                var rankDecision = GridDistance(opened, raw, mask, n, .22, 3, .47, rank: true);
                var hit = CheckBits(rankDecision, mask, n, pure);
                if (hit != null) return Place(hit, raw);
                // 浮点拟合的亚像素差异会改变接近阈值的格子，复用采样结果试一次较低阈值。
                for (int i = 0; i < rankDecision.Length; ++i) rankDecision[i] += .01;
                hit = CheckBits(rankDecision, mask, n, pure);
                if (hit != null) return Place(hit, raw);

                var refined = Refine(im, raw, mask, n);
                hit = CheckBits(GridDistance(opened, refined, mask, n, .22, 3, .47), mask, n, pure);
                if (hit != null) return Place(hit, refined);

                using (var median = new Mat())
                {
                    Cv2.MedianBlur(gray, median, 3);
                    using (var filtered = Float(median))
                    {
                        var refitted = Refine(im, Optimize(filtered, geometry), mask, n);
                        hit = CheckBits(GridDistance(opened, refitted, mask, n, .22, 3, .47, x: .1f, y: -.1f), mask, n, pure);
                        if (hit != null) return Place(hit, refitted);
                    }
                }
                // 定位和计时边保持不变，仅尝试判定最接近阈值的数据格。
                hit = CheckBits(GridDistance(opened, refined, mask, n, .16, 2, .49), mask, n, pure, retry: 12);
                return Place(hit, refined);
            }
        }
        internal static Result Recover(Mat gray, bool rotate, bool invert)
        {
            if (gray.Rows < 8 || gray.Cols < 8) return null;
            Cv2.MinMaxLoc(gray, out double min, out double max); if (min == max) return null;
            const int pad = 16;
            using (var a = new Mat()) using (var w = new WriterOptions { Scale = 1, AddQuietZones = false })
            using (var scan = new ZXingCpp.BarcodeReader { Formats = BarcodeFormat.DataMatrix, TryDownscale = false, TryRotate = rotate, TryInvert = invert, ReturnErrors = true, TextMode = TextMode.HRI })
            using (var pure = new ZXingCpp.BarcodeReader { Formats = BarcodeFormat.DataMatrix, TryDownscale = false, TryRotate = false, TryInvert = false, IsPure = true, TextMode = TextMode.HRI })
            {
                var roiHit = RecoverRoiGrid(gray, pure);
                if (roiHit != null) return roiHit;

                Cv2.CopyMakeBorder(gray, a, pad, pad, pad, pad, BorderTypes.Constant, new Scalar(Percentile(GrayValues(gray), .85)));
                var rows = new List<Row>(); int known = 0;
                try
                {
                    using (var blurred = Blur(a, 1)) using (var gauss = Sharpen(blurred, 6, 2)) using (var med = new Mat()) using (var mean = new Mat())
                    {
                        Cv2.MedianBlur(a, med, 5); Cv2.Blur(a, mean, new Size(3, 3));
                        using (var median = Sharpen(med, 1, 1)) using (var meanf = Float(mean)) using (var sharp = Sharpen(meanf, 2, 2.8)) using (var resized = new Mat())
                        {
                            foreach (double f in new[] { .75, 1.25, .80 })
                            {
                                Cv2.Resize(f > 1 ? median : gauss, resized, new Size(), f, f, f > 1 ? InterpolationFlags.Cubic : InterpolationFlags.Area);
                                scan.Binarizer = f > 1 ? Binarizer.GlobalHistogram : Binarizer.FixedThreshold;
                                var view = new ImageView(resized.Data, resized.Cols, resized.Rows, ImageFormat.Lum, checked((int)resized.Step()));
                                var codes = scan.From(view) ?? Array.Empty<Barcode>();
                                try
                                {
                                    foreach (var code in codes)
                                    {
                                        if (!code.IsValid && code.ErrorType != ErrorType.Checksum) continue;
                                        var s = code.Position;
                                        var p = new[] { s.TopLeft, s.TopRight, s.BottomRight, s.BottomLeft }.Select(pt => new Point2f((float)(pt.X / f), (float)(pt.Y / f))).ToArray();
                                        if (Cv2.ContourArea(p) * f * f < 30) continue;
                                        if (code.IsValid) return new Result(code.Text, p, pad);
                                        int n;
                                        try { using (var img = code.ToImage(w)) { if (img == null || img.Width != img.Height || !Sizes.Contains(img.Width)) continue; n = img.Width; } }
                                        catch (InvalidOperationException) { continue; }
                                        var ordered = Order(p); if (rows.Any(r => r.Geometry.Size == n && Distance(r.Outline, ordered) < 1.25)) continue;
                                        var m = Projection(ordered); var row = new Row { Projection = m, Outline = ordered }; rows.Add(row);
                                        using (var v = Rect(sharp, m)) row.Geometry = Score(v, Target, n);
                                        using (var v = Rect(a, m)) row.Image = Float(v);
                                    }
                                }
                                finally { foreach (var code in codes) code.Dispose(); GC.KeepAlive(resized); }
                            }
                        }
                    }
                    foreach (var row in rows.OrderByDescending(r => r.Geometry.Score).Take(2))
                    {
                        var g = row.Geometry; int n = g.Size; if (g.Score < 3) continue; if (known == 0 && g.Score >= 6) known = n;
                        var mask = Mask(n, g.Regions); var grid = RotateGrid(Maps(n, g.Corners), n, g.Rotation);
                        var text = Probe(row.Image, grid, mask, n, true, pure);
                        if (text == null) { FitRegions(row.Image, grid, n, g.Regions); grid = Refine(row.Image, grid, mask, n); text = Probe(row.Image, grid, mask, n, true, pure); }
                        if (text != null) { var corners = MapCorners(grid, n, row.Projection); if (corners != null) return new Result(text, corners, pad); }
                    }
                }
                finally { foreach (var row in rows) row.Dispose(); rows.Clear(); }
                try
                {
                    void Add(Mat image, Mat projection)
                    {
                        var v = Float(image); var row = new Row { Image = v, Projection = projection }; rows.Add(row);
                        using (var sm = Blur(v, 1.1)) using (var bg = Blur(sm, 4)) using (var diff = new Mat())
                        { Cv2.Subtract(sm, bg, diff); row.Geometry = Score(diff, Target, known); }
                    }
                    if (Math.Min(gray.Cols, gray.Rows) > .6 * Math.Max(gray.Cols, gray.Rows))
                    {
                        var m = Projection(ImageQuad(gray.Cols, gray.Rows, pad)); using (var v = Rect(a, m)) Add(v, m);
                    }
                    var seed = Seed(a);
                    if (seed != null)
                    {
                        using (var m = Projection(seed)) using (var v = Rect(a, m))
                        {
                            var q = FitEdges(v);
                            if (q != null) using (var p = Projection(q)) using (var rect = Rect(v, p)) using (var empty = new Mat())
                                { var combined = new Mat(); Cv2.Gemm(p, m, 1, empty, 0, combined); Add(rect, combined); }
                        }
                    }
                    foreach (var row in rows.OrderByDescending(r => r.Geometry.Score))
                    {
                        var g = row.Geometry; if (g.Score < 3) continue; int n = g.Size; var mask = Mask(n, g.Regions); var grid = Optimize(row.Image, g);
                        var text = Probe(row.Image, grid, mask, n, false, pure);
                        if (text == null) { grid = Refine(row.Image, grid, mask, n); text = Probe(row.Image, grid, mask, n, false, pure) ?? ExtraProbe(row.Image, grid, mask, n, pure); }
                        if (text != null) { var corners = MapCorners(grid, n, row.Projection); if (corners != null) return new Result(text, corners, pad); }
                    }
                }
                finally { foreach (var row in rows) row.Dispose(); }
                return null;
            }
        }
    }
}
