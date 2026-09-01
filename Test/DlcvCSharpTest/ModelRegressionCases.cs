using System.Collections.Generic;

namespace DlcvCSharpTest
{
    internal sealed class ModelRegressionCase
    {
        public readonly string Name;
        public readonly string ModelFile;
        public readonly string ImageFile;
        public readonly List<ModelRegressionSample> Samples;

        public ModelRegressionCase(string name, string modelFile, string imageFile, params ModelRegressionSample[] samples)
        {
            Name = name;
            ModelFile = modelFile;
            ImageFile = imageFile;
            Samples = new List<ModelRegressionSample>(samples);
        }
    }

    internal sealed class ModelRegressionSample
    {
        public readonly List<ModelRegressionResult> Results;

        public ModelRegressionSample(params ModelRegressionResult[] results)
        {
            Results = new List<ModelRegressionResult>(results);
        }
    }

    internal sealed class ModelRegressionResult
    {
        public readonly int CategoryId;
        public readonly string CategoryName;
        public readonly double Score;
        public readonly double[] Bbox;
        public readonly double Area;
        public readonly double Angle;
        public readonly bool WithBbox;
        public readonly bool WithMask;
        public readonly bool WithAngle;
        public readonly ModelRegressionMask Mask;

        public ModelRegressionResult(int categoryId, string categoryName, double score, double[] bbox,
            double area, double angle, bool withBbox, bool withMask, bool withAngle, ModelRegressionMask mask)
        {
            CategoryId = categoryId;
            CategoryName = categoryName;
            Score = score;
            Bbox = bbox;
            Area = area;
            Angle = angle;
            WithBbox = withBbox;
            WithMask = withMask;
            WithAngle = withAngle;
            Mask = mask;
        }
    }

    internal sealed class ModelRegressionMask
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int NonZero;

        public ModelRegressionMask(int width, int height, int nonZero)
        {
            Width = width;
            Height = height;
            NonZero = nonZero;
        }
    }

    internal static class ModelRegressionCases
    {
        public const double ScoreTolerance = 0.002;
        public const double BboxTolerance = 1.0;
        public const double AreaTolerance = 128.0;
        public const double AngleTolerance = 0.05;
        public const int MaskNonZeroTolerance = 128;

        // 结果字段基准来自 dlcv_infer 的 shared_model_regression_cases.json；mask 统计按 C# API 缩放后的局部 Mat 记录。
        public static readonly List<ModelRegressionCase> Cases = new List<ModelRegressionCase>
        {
            Case("测试无监督 DVT", "测试无监督-v5_120_50_s.dvt", "1786969663716.jpg",
                Sample(Result(0, "NG", 1.0, B(0.0, 13.0, 438.0, 611.0), 248859.5, -100.0, true, true, false, M(438, 611, 249853)))),
            Case("猫狗分类 Sentinel DVT", "猫狗-分类_120_50_s.dvt", "猫狗-狗.jpg",
                Sample(Result(0, "狗", 0.9951171875, B(-1.0, -1.0, -1.0, -1.0), -1.0, -100.0, false, false, false, null))),
            Case("猫狗分类 Virbox DVT", "猫狗-分类_120_50_v.dvt", "猫狗-狗.jpg",
                Sample(Result(0, "狗", 0.9951171875, B(-1.0, -1.0, -1.0, -1.0), -1.0, -100.0, false, false, false, null))),
            Case("气球大模型 DVT", "气球-大模型_20260830_010011_120_50_s.dvt", "气球.jpg",
                Sample(Result(0, "气球", 0.9591543078422546, B(77.875, 59.8125, 486.25, 569.625), 220828.0, -100.0, true, true, false, M(486, 569, 219656)))),
            Case("气球实例分割 Sentinel DVT", "气球-实例分割_120_50_s.dvt", "气球.jpg",
                Sample(Result(0, "气球", 0.9892578125, B(41.79999923706055, 29.100000381469727, 517.4000244140625, 622.5000610351562), 228600.0, -100.0, true, true, false, M(517, 622, 227891)))),
            Case("气球实例分割 Virbox DVT", "气球-实例分割_120_50_v.dvt", "气球.jpg",
                Sample(Result(0, "气球", 0.9990234375, B(56.20000076293945, 46.0, 505.4000244140625, 588.7999877929688), 227980.0, -100.0, true, true, false, M(505, 588, 227775)))),
            Case("气球语义分割 DVT", "气球-语义分割_120_50_s.dvt", "气球.jpg",
                Sample(Result(0, "气球", 1.0, B(68.5714340209961, 54.85714340209961, 498.2857360839844, 579.4285888671875), 219930.140625, -100.0, true, true, false, M(498, 579, 222774)))),
            Case("手机屏幕实例分割 DVT", "手机屏幕-实例分割_120_50_s.dvt", "手机屏幕.jpg",
                Sample(Result(0, "划痕", 0.9970703125, B(390.0, 131.28750610351562, 177.857177734375, 39.48750305175781), 1510.0, -100.0, true, true, false, M(177, 39, 1510)))),
            Case("引脚定位目标检测 DVT", "引脚定位-目标检测_120_50_s.dvt", "引脚定位-目标检测.jpg",
                Sample(
                    Result(0, "引脚", 1.0, B(108.65478515625, 73.37890625, 25.25830078125, 33.5849609375), 848.0, -100.0, true, false, false, null),
                    Result(0, "引脚", 1.0, B(44.564208984375, 187.3984375, 26.297607421875, 46.28515625), 1217.0, -100.0, true, false, false, null),
                    Result(0, "引脚", 1.0, B(169.3125, 188.52734375, 28.72265625, 45.4384765625), 1305.0, -100.0, true, false, false, null))),
            Case("AOI 旋转框检测 DVT", "AOI-旋转框检测_120_50_s.dvt", "AOI-测试.jpg",
                Sample(
                    Result(5, "电阻", 1.0, B(627.8125, 1191.875, 220.15625, 107.109375), 23580.0, -0.775390625, true, false, true, null),
                    Result(5, "电阻", 1.0, B(479.0625, 951.25, 170.3125, 84.6875), 14423.0, 0.82421875, true, false, true, null),
                    Result(5, "电阻", 1.0, B(806.875, 821.25, 176.5625, 81.015625), 14304.0, -0.78466796875, true, false, true, null),
                    Result(2, "三极管", 0.95947265625, B(523.75, 556.875, 550.3125, 507.8125), 279455.0, 0.802734375, true, false, true, null),
                    Result(2, "三极管", 0.95166015625, B(220.9375, 1198.75, 629.375, 508.75), 320194.0, -0.798828125, true, false, true, null))),
            Case("OCR DVT", "OCR_120_50_s.dvt", "OCR-472.jpg",
                Sample(Result(0, "472", 0.9928385615348816, B(-1.0, -1.0, -1.0, -1.0), -1.0, -100.0, false, false, false, null)))
        };

        private static ModelRegressionCase Case(string name, string model, string image, params ModelRegressionSample[] samples)
        {
            return new ModelRegressionCase(name, model, image, samples);
        }

        private static ModelRegressionSample Sample(params ModelRegressionResult[] results)
        {
            return new ModelRegressionSample(results);
        }

        private static ModelRegressionResult Result(int categoryId, string categoryName, double score, double[] bbox,
            double area, double angle, bool withBbox, bool withMask, bool withAngle, ModelRegressionMask mask)
        {
            return new ModelRegressionResult(categoryId, categoryName, score, bbox, area, angle, withBbox, withMask, withAngle, mask);
        }

        private static double[] B(params double[] values) { return values; }
        private static ModelRegressionMask M(int width, int height, int nonZero) { return new ModelRegressionMask(width, height, nonZero); }
    }
}
