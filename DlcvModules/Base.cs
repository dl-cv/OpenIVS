using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModules
{
    /// <summary>
    /// 轻量执行上下文：用于在执行图期间传递共享参数与回调
    /// </summary>
    public class ExecutionContext
    {
        private readonly Dictionary<string, object> _map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public ExecutionContext()
        {
        }

        public T Get<T>(string key, T defaultValue = default(T))
        {
            if (key == null) return defaultValue;
            if (_map.TryGetValue(key, out object value))
            {
                try
                {
                    if (value is T t) return t;
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        public void Set(string key, object value)
        {
            if (key == null) return;
            _map[key] = value;
        }
    }

    /// <summary>
    /// 模块注册表：按模块类型字符串获取对应的实现类型
    /// </summary>
    public static class ModuleRegistry
    {
        private static readonly Dictionary<string, Type> _registry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string moduleType, Type moduleClass)
        {
            if (string.IsNullOrWhiteSpace(moduleType)) throw new ArgumentException("moduleType is null or empty");
            if (moduleClass == null) throw new ArgumentNullException(nameof(moduleClass));
            _registry[moduleType] = moduleClass;
        }

        public static Type Get(string moduleType)
        {
            if (string.IsNullOrWhiteSpace(moduleType)) return null;
            _registry.TryGetValue(moduleType, out Type t);
            return t;
        }
    }

    /// <summary>
    /// 表示从原图到当前图的线性变换状态（仿射：2x3），以及尺寸信息
    /// 仅实现数学变换链路的维护，图像实际变换由具体功能模块完成
    /// </summary>
    public class TransformationState
    {
        public int OriginalWidth { get; private set; }
        public int OriginalHeight { get; private set; }
        public int[] CropBox { get; private set; } // [x, y, w, h] or null
        public double[] AffineMatrix2x3 { get; private set; } // 长度6，按行主序存储
        public int[] OutputSize { get; private set; } // [w, h] or null

        public TransformationState(int originalWidth, int originalHeight, int[] cropBox = null, double[] affine2x3 = null, int[] outputSize = null)
        {
            OriginalWidth = originalWidth;
            OriginalHeight = originalHeight;
            CropBox = cropBox;
            AffineMatrix2x3 = affine2x3;
            OutputSize = outputSize;
        }

        public TransformationState Clone()
        {
            return new TransformationState(
                OriginalWidth,
                OriginalHeight,
                CropBox != null ? (int[])CropBox.Clone() : null,
                AffineMatrix2x3 != null ? (double[])AffineMatrix2x3.Clone() : null,
                OutputSize != null ? (int[])OutputSize.Clone() : null
            );
        }

        public Dictionary<string, object> ToDict()
        {
            var dict = new Dictionary<string, object>();
            dict["original_width"] = OriginalWidth;
            dict["original_height"] = OriginalHeight;
            if (CropBox != null) dict["crop_box"] = new[] { CropBox[0], CropBox[1], CropBox[2], CropBox[3] };
            if (AffineMatrix2x3 != null) dict["affine_2x3"] = (double[])AffineMatrix2x3.Clone();
            if (OutputSize != null) dict["output_size"] = new[] { OutputSize[0], OutputSize[1] };
            return dict;
        }

        public static TransformationState FromDict(Dictionary<string, object> data)
        {
            if (data == null) return null;
            int ow = SafeGetInt(data, "original_width", 0);
            int oh = SafeGetInt(data, "original_height", 0);
            int[] crop = null;
            if (data.TryGetValue("crop_box", out object cropObj))
            {
                if (cropObj is Array cropArr && cropArr.Length >= 4)
                {
                    crop = new int[] { Convert.ToInt32(((Array)cropArr).GetValue(0)), Convert.ToInt32(((Array)cropArr).GetValue(1)), Convert.ToInt32(((Array)cropArr).GetValue(2)), Convert.ToInt32(((Array)cropArr).GetValue(3)) };
                }
                else if (cropObj is JArray jCrop && jCrop.Count >= 4)
                {
                    crop = new int[] { jCrop[0].Value<int>(), jCrop[1].Value<int>(), jCrop[2].Value<int>(), jCrop[3].Value<int>() };
                }
            }
            double[] a23 = null;
            if (data.TryGetValue("affine_2x3", out object a23Obj))
            {
                if (a23Obj is Array a23Arr && a23Arr.Length >= 6)
                {
                    a23 = new double[]
                    {
                        Convert.ToDouble(a23Arr.GetValue(0)), Convert.ToDouble(a23Arr.GetValue(1)), Convert.ToDouble(a23Arr.GetValue(2)),
                        Convert.ToDouble(a23Arr.GetValue(3)), Convert.ToDouble(a23Arr.GetValue(4)), Convert.ToDouble(a23Arr.GetValue(5))
                    };
                }
                else if (a23Obj is JArray jA23 && jA23.Count >= 6)
                {
                    a23 = new double[]
                    {
                        jA23[0].Value<double>(), jA23[1].Value<double>(), jA23[2].Value<double>(),
                        jA23[3].Value<double>(), jA23[4].Value<double>(), jA23[5].Value<double>()
                    };
                }
            }
            int[] osize = null;
            if (data.TryGetValue("output_size", out object osObj))
            {
                if (osObj is Array osArr && osArr.Length >= 2)
                {
                    osize = new int[] { Convert.ToInt32(osArr.GetValue(0)), Convert.ToInt32(osArr.GetValue(1)) };
                }
                else if (osObj is JArray jOs && jOs.Count >= 2)
                {
                    osize = new int[] { jOs[0].Value<int>(), jOs[1].Value<int>() };
                }
            }
            return new TransformationState(ow, oh, crop, a23, osize);
        }

        public TransformationState DeriveChild(double[] currentToNew2x3, int newWidth, int newHeight)
        {
            if (currentToNew2x3 == null || currentToNew2x3.Length != 6)
                throw new ArgumentException("currentToNew2x3 must be length-6 array");

            double[] parent2x3 = AffineMatrix2x3 ?? new double[] { 1, 0, 0, 0, 1, 0 };
            double[] parent3x3 = To3x3(parent2x3);
            double[] child3x3 = To3x3(currentToNew2x3);
            double[] composed3x3 = Mul3x3(child3x3, parent3x3); // original -> new
            double[] composed2x3 = To2x3(composed3x3);

            return new TransformationState(
                OriginalWidth,
                OriginalHeight,
                CropBox != null ? (int[])CropBox.Clone() : null,
                composed2x3,
                new[] { newWidth, newHeight }
            );
        }

        public static double[] To3x3(double[] a2x3)
        {
            if (a2x3 == null || a2x3.Length != 6) return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            return new double[]
            {
                a2x3[0], a2x3[1], a2x3[2],
                a2x3[3], a2x3[4], a2x3[5],
                0,       0,       1
            };
        }

        public static double[] To2x3(double[] a3x3)
        {
            if (a3x3 == null || a3x3.Length != 9) return new double[] { 1, 0, 0, 0, 1, 0 };
            return new double[] { a3x3[0], a3x3[1], a3x3[2], a3x3[3], a3x3[4], a3x3[5] };
        }

        public static double[] Mul3x3(double[] A, double[] B)
        {
            // C = A * B
            if (A == null || B == null || A.Length != 9 || B.Length != 9)
                throw new ArgumentException("Mul3x3 requires two 3x3 matrices");
            double[] C = new double[9];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    C[r * 3 + c] = A[r * 3 + 0] * B[0 * 3 + c] + A[r * 3 + 1] * B[1 * 3 + c] + A[r * 3 + 2] * B[2 * 3 + c];
                }
            }
            return C;
        }

        public static double[] Inverse2x3(double[] a2x3)
        {
            if (a2x3 == null || a2x3.Length != 6) throw new ArgumentException("Inverse2x3 requires 2x3 matrix");
            // A = [ a b tx ; c d ty ]
            double a = a2x3[0], b = a2x3[1], tx = a2x3[2];
            double c = a2x3[3], d = a2x3[4], ty = a2x3[5];
            double det = a * d - b * c;
            if (Math.Abs(det) < 1e-12) throw new InvalidOperationException("Matrix not invertible");
            double invDet = 1.0 / det;
            double ia = d * invDet;
            double ib = -b * invDet;
            double ic = -c * invDet;
            double id = a * invDet;
            double itx = -(ia * tx + ib * ty);
            double ity = -(ic * tx + id * ty);
            return new double[] { ia, ib, itx, ic, id, ity };
        }

        private static int SafeGetInt(Dictionary<string, object> dict, string key, int defaultValue)
        {
            if (dict == null || key == null) return defaultValue;
            if (!dict.TryGetValue(key, out object v) || v == null) return defaultValue;
            try { return Convert.ToInt32(v); } catch { return defaultValue; }
        }
    }

    /// <summary>
    /// 携带图像对象与变换状态的包装器
    /// imageObject 建议传入 OpenCvSharp.Mat 或 Bitmap；框架对类型不做强约束
    /// </summary>
    public class ModuleImage
    {
        public Mat ImageObject { get; private set; }
        public Mat OriginalImage { get; private set; }
        public TransformationState TransformState { get; private set; }
        public int OriginalIndex { get; private set; }

        public ModuleImage(Mat imageObject, Mat originalImage, TransformationState transformState, int originalIndex = 0)
        {
            ImageObject = imageObject;
            OriginalImage = originalImage ?? imageObject;
            TransformState = transformState;
            OriginalIndex = originalIndex;
        }

        public Mat GetImage()
        {
            return ImageObject;
        }

        public Dictionary<string, object> ToMeta()
        {
            var meta = new Dictionary<string, object>();
            meta["origin_index"] = OriginalIndex;
            meta["transform"] = TransformState != null ? (object)TransformState.ToDict() : null;
            return meta;
        }
    }

    /// <summary>
    /// 模块 I/O：统一强类型图像序列与 JArray 结果序列
    /// </summary>
    public class ModuleIO
    {
        public List<ModuleImage> ImageList { get; private set; }
        public JArray ResultList { get; private set; }
        public List<SimpleTemplate> TemplateList { get; private set; }

        public ModuleIO(List<ModuleImage> images = null, JArray results = null, List<SimpleTemplate> templates = null)
        {
            ImageList = images ?? new List<ModuleImage>();
            ResultList = results ?? new JArray();
            TemplateList = templates ?? new List<SimpleTemplate>();
        }
    }

    /// <summary>
    /// 模块输入/输出对（便于承载额外通道）
    /// </summary>
    public class ModuleChannel
    {
        public List<ModuleImage> ImageList { get; private set; }
        public JArray ResultList { get; private set; }
        public List<SimpleTemplate> TemplateList { get; private set; }

        public ModuleChannel(List<ModuleImage> images, JArray results, List<SimpleTemplate> templates = null)
        {
            ImageList = images ?? new List<ModuleImage>();
            ResultList = results ?? new JArray();
            TemplateList = templates ?? new List<SimpleTemplate>();
        }
    }

    /// <summary>
    /// 模块基类：统一签名 Process(image_list, result_list) -> (image_list, result_list)
    /// </summary>
    public class BaseModule
    {
        public int NodeId { get; private set; }
        public string Title { get; private set; }
        public Dictionary<string, object> Properties { get; private set; }
        public ExecutionContext Context { get; private set; }

        // 额外输入/输出对，索引与执行器的“扩展端口对”对应
        public List<ModuleChannel> ExtraInputsIn { get; private set; } = new List<ModuleChannel>();
        public List<ModuleChannel> ExtraOutputs { get; private set; } = new List<ModuleChannel>();

        // 主对模版输入
        public List<SimpleTemplate> MainTemplateList { get; set; } = new List<SimpleTemplate>();
        // 标量输入/输出（按索引与名称）
        public Dictionary<int, object> ScalarInputsByIndex { get; set; } = new Dictionary<int, object>();
        public Dictionary<string, object> ScalarInputsByName { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> ScalarOutputsByName { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public BaseModule(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
        {
            NodeId = nodeId;
            Title = title;
            Properties = properties ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Context = context ?? new ExecutionContext();
        }

        public virtual ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            // 缺省透传
            return new ModuleIO(imageList ?? new List<ModuleImage>(), resultList ?? new JArray(), new List<SimpleTemplate>());
        }
    }

    /// <summary>
    /// 输入模块基类：忽略输入，由 Generate 产生首对输出
    /// </summary>
    public class BaseInputModule : BaseModule
    {
        public BaseInputModule(int nodeId, string title = null, Dictionary<string, object> properties = null, ExecutionContext context = null)
            : base(nodeId, title, properties, context)
        {
        }

        public virtual ModuleIO Generate()
        {
            return new ModuleIO(new List<ModuleImage>(), new JArray());
        }

        public override ModuleIO Process(List<ModuleImage> imageList = null, JArray resultList = null)
        {
            return Generate();
        }
    }
}


