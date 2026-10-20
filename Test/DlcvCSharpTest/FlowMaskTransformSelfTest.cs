using System;
using System.Collections.Generic;
using System.Reflection;
using dlcv_infer_csharp;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class FlowMaskTransformSelfTest
    {
        public static int Run()
        {
            int failures = 0;
            Action<string, Action> check = (name, test) =>
            {
                try { test(); Console.WriteLine("通过 " + name); }
                catch (Exception ex) { failures++; Console.WriteLine("失败 " + name + ": " + ex.Message); }
            };
            using (var mask = new Mat(3, 5, MatType.CV_8UC1, Scalar.Black))
            {
                mask.Set(0, 0, (byte)255);
                mask.Set(0, 1, (byte)255);
                mask.Set(1, 0, (byte)255);
                mask.Set(2, 4, (byte)255);
                var rle = MaskRleUtils.MatToMaskInfo(mask);
                check("无变换及裁剪平移不解码重编码", () =>
                {
                    foreach (var t in new[] { new double[] { 1, 0, 0, 0, 1, 0 }, new double[] { 1, 0, -8, 0, 1, -12 }, new double[] { 2, 0, 7, 0, 3, 9 } })
                    {
                        var input = (JObject)rle.DeepClone();
                        var result = Output(input, t);
                        Require(ReferenceEquals(result["mask_rle"], input), "应保留原RLE引用");
                    }
                });
                check("顺时针矫正后mask逆旋转", () => Rotation(mask, rle, new double[] { 0, -1, 99, 1, 0, 0 }, RotateFlags.Rotate90Counterclockwise));
                check("逆时针矫正后mask逆旋转", () => Rotation(mask, rle, new double[] { 0, 1, 0, -1, 0, 99 }, RotateFlags.Rotate90Clockwise));
                check("180度", () => Rotation(mask, rle, new double[] { -1, 0, 99, 0, -1, 99 }, RotateFlags.Rotate180));
                check("水平翻转", () => Flip(mask, rle, new double[] { -1, 0, 99, 0, 1, 0 }, FlipMode.Y));
                check("竖直翻转", () => Flip(mask, rle, new double[] { 1, 0, 0, 0, -1, 99 }, FlipMode.X));
                check("转置及非等比缩放组合", () =>
                {
                    using (var expected = new Mat())
                    {
                        Cv2.Transpose(mask, expected);
                        Equal(expected, Output(rle, new double[] { 0, 2, 5, 3, 0, 9 }));
                    }
                });
                check("JSON poly路径的RLE与结构化输出一致", () =>
                {
                    using (var expected = new Mat())
                    {
                        Cv2.Rotate(mask, expected, RotateFlags.Rotate90Counterclockwise);
                        var result = Output(rle, new double[] { 0, -1, 99, 1, 0, 0 }, emitPoly: true);
                        Require(result.ContainsKey("poly"), "JSON应保留poly");
                        Equal(expected, result);
                    }
                });
                check("反转置", () =>
                {
                    using (var expected = new Mat())
                    {
                        Cv2.Transpose(mask, expected);
                        Cv2.Flip(expected, expected, FlipMode.XY);
                        Equal(expected, Output(rle, new double[] { 0, -1, 99, -1, 0, 99 }));
                    }
                });
                check("小数bbox旋转、映射慢路径、输入不变", () =>
                {
                    string before = rle.ToString();
                    using (var expected = new Mat())
                    {
                        Cv2.Rotate(mask, expected, RotateFlags.Rotate90Counterclockwise);
                        Equal(expected, Output(rle, new double[] { 0, -1, 99, 1, 0, 0 }, new JArray(10.2, 20.4, 4.8, 2.6), false, true));
                    }
                    Require(rle.ToString() == before, "输入RLE被修改");
                });
                check("with_mask缺省、null及无效值保持既有默认", () =>
                {
                    foreach (var parameters in new[] { new JObject(), new JObject { ["with_mask"] = JValue.CreateNull() }, new JObject { ["with_mask"] = "invalid" } })
                        Require(Output(rle, new double[] { 1, 0, 0, 0, 1, 0 }, inferParams: parameters).ContainsKey("mask_rle"), "默认mask被关闭");
                });
                check("with_mask=false不输出或转换mask", () =>
                    Require(!Output(rle, new double[] { 0, -1, 99, 1, 0, 0 }, withMask: false).ContainsKey("mask_rle"), "仍输出了mask"));
                check("一般仿射只输出bbox ROI，二值mask保持", () =>
                {
                    using (var full = new Mat(20, 30, MatType.CV_8UC1, Scalar.White))
                    {
                        var result = Output(MaskRleUtils.MatToMaskInfo(full), new double[] { 1, 0.5, 0, 0, 1, 0 }, new JArray(10, 20, 30, 20));
                        var box = (List<int>)result["bbox"];
                        using (var actual = MaskRleUtils.MaskInfoToMat((JToken)result["mask_rle"]))
                        {
                            Require(actual.Width == box[2] - box[0] && actual.Height == box[3] - box[1], "ROI尺寸不符");
                            Require(actual.Width == 40 && actual.Height == 20, "不应分配整图mask");
                            Require(actual.At<byte>(2, 2) == 0 && actual.At<byte>(2, 12) == 255 && actual.At<byte>(18, 35) == 0, "仿射方向错误");
                            Require(Cv2.CountNonZero(actual) == 600, "像素面积不符");
                        }
                    }
                });
                check("旋转框仍保留既有mask约定", () =>
                {
                    var input = (JObject)rle.DeepClone();
                    Require(ReferenceEquals(Output(input, new double[] { 0, -1, 99, 1, 0, 0 }, new JArray(10, 20, 5, 3, 30))["mask_rle"], input), "旋转框语义改变");
                });
            }
            Console.WriteLine("mask方向自测失败数: " + failures);
            return failures == 0 ? 0 : 1;
        }

        private static Dictionary<string, object> Output(JObject rle, double[] affine, JArray box = null, bool emitPoly = false, bool mapped = false, bool withMask = true, JObject inferParams = null)
        {
            using (var image = new Mat(100, 100, MatType.CV_8UC1, Scalar.Black))
            {
                var context = new DlcvModules.ExecutionContext();
                context.Set("infer_params", inferParams ?? new JObject { ["with_mask"] = withMask });
                context.Set("return_json_emit_poly", emitPoly);
                var wrap = new ModuleImage(null, image, new TransformationState(100, 100, affine2x3: affine), 0);
                var entry = new JObject { ["type"] = "local", ["index"] = mapped ? 7 : 0, ["origin_index"] = 0,
                    ["sample_results"] = new JArray(new JObject { ["bbox"] = box ?? new JArray(10, 20, 5, 3), ["category_name"] = "foreground", ["score"] = 1, ["mask_rle"] = rle }) };
                new ReturnJson(1, context: context).Process(new List<ModuleImage> { wrap }, new JArray(entry));
                var payload = (Dictionary<string, object>)context.Get<Dictionary<string, object>>("frontend_json")["last"];
                var images = (List<Dictionary<string, object>>)payload["by_image"];
                var result = ((List<Dictionary<string, object>>)images[0]["results"])[0];
                return result;
            }
        }
        private static void Rotation(Mat mask, JObject rle, double[] affine, RotateFlags flag)
        {
            using (var expected = new Mat()) { Cv2.Rotate(mask, expected, flag); Equal(expected, Output(rle, affine)); }
        }
        private static void Flip(Mat mask, JObject rle, double[] affine, FlipMode mode)
        {
            using (var expected = new Mat()) { Cv2.Flip(mask, expected, mode); Equal(expected, Output(rle, affine)); }
        }
        private static void Equal(Mat expected, Dictionary<string, object> result)
        {
            using (var actual = MaskRleUtils.MaskInfoToMat((JToken)result["mask_rle"]))
            {
                Require(expected.Size() == actual.Size(), "mask宽高方向错误");
                Require(Cv2.Norm(expected, actual, NormTypes.INF) == 0, "mask像素方向错误");
                using (var model = new FlowGraphModel())
                {
                    var objects = new List<Utils.CSharpObjectResult>();
                    typeof(FlowGraphModel).GetMethod("ParseNewFormatEntry", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(model, new object[] { JObject.FromObject(result), objects, true });
                    try
                    {
                        Require(objects.Count == 1 && objects[0].WithMask, "结构化输出遗漏mask");
                        Require(objects[0].Mask.Size() == expected.Size() && Cv2.Norm(expected, objects[0].Mask, NormTypes.INF) == 0, "结构化mask不同");
                    }
                    finally { foreach (var obj in objects) obj.Mask?.Dispose(); }
                }
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
