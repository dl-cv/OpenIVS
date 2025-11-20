using System;
using System.Collections.Generic;
using System.Text;
using OpenCvSharp;

namespace dlcv_infer_csharp
{
    public partial class Utils
    {
        /// <summary>
        /// 单个检测物体的结果
        /// </summary>
        public struct CSharpObjectResult
        {
            /// <summary>
            /// 类别ID
            /// </summary>
            public int CategoryId { get; set; }

            /// <summary>
            /// 类别名称
            /// </summary>
            public string CategoryName { get; set; }

            /// <summary>
            /// 检测置信度
            /// </summary>
            public float Score { get; set; }

            /// <summary>
            /// 检测框的面积
            /// </summary>
            public float Area { get; set; }

            // <summary>
            // 是否有检测框
            // </summary>
            public bool WithBbox { get; set; }

            /// <summary>
            /// 检测框坐标
            /// 对于普通目标检测/实例分割/语义分割：[x, y, w, h]，其中(x,y)为左上角坐标
            /// 对于旋转框检测：[cx, cy, w, h]，其中(cx,cy)为中心点坐标
            /// </summary>
            public List<double> Bbox { get; set; }

            /// <summary>
            /// 是否包含mask信息（仅实例分割和语义分割任务会有）
            /// </summary>
            public bool WithMask { get; set; }

            /// <summary>
            /// 实例分割或语义分割的mask
            /// 0表示非目标像素，255表示目标像素
            /// 尺寸与Bbox中的宽高一致
            /// </summary>
            public Mat Mask { get; set; }

            // <summary>
            // 是否有角度
            // </summary>
            public bool WithAngle { get; set; }

            /// <summary>
            /// 旋转框的角度（弧度制）
            /// 仅旋转框检测任务会有此值，其他任务为-100
            /// </summary>
            public float Angle { get; set; }

            public CSharpObjectResult(int categoryId, string categoryName, float score, float area,
                List<double> bbox, bool withMask, Mat mask,
                bool withBbox = false, bool withAngle = false, float angle = -100)
            {
                CategoryId = categoryId;
                CategoryName = categoryName;
                Score = score;
                Area = area;
                Bbox = bbox;
                WithMask = withMask;
                Mask = mask;
                Angle = angle;
                WithBbox = withBbox;
                WithAngle = withAngle;
            }

            public override String ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append($"{CategoryName}, ");
                sb.Append($"Score: {Score * 100:F1}, ");
                sb.Append($"Area: {Area:F1}, ");
                if (WithAngle)
                {
                    sb.Append($"Angle: {Angle * 180 / Math.PI:F1}, ");
                }
                if (WithBbox)
                {
                    sb.Append("Bbox: [");
                    foreach (var x in Bbox)
                    {
                        sb.Append($"{x:F1}, ");
                    }
                    sb.Append("], ");
                }
                if (WithMask)
                {
                    sb.Append($"Mask size: {Mask.Width}x{Mask.Height}, ");
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 单张图片的检测结果
        /// 包含该图片中所有检测到的物体信息
        /// </summary>
        public struct CSharpSampleResult
        {
            /// <summary>
            /// 该图片中所有检测到的物体列表
            /// </summary>
            public List<CSharpObjectResult> Results { get; set; }

            public CSharpSampleResult(List<CSharpObjectResult> results)
            {
                Results = results;
            }

            public override String ToString()
            {
                StringBuilder sb = new StringBuilder();
                foreach (var a in Results)
                {
                    sb.Append(a.ToString());
                    sb.AppendLine();
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 批量图片的检测结果
        /// 每个元素对应一张图片的检测结果
        /// </summary>
        public struct CSharpResult
        {
            /// <summary>
            /// 所有图片的检测结果列表
            /// </summary>
            public List<CSharpSampleResult> SampleResults { get; set; }

            public CSharpResult(List<CSharpSampleResult> sampleResults)
            {
                SampleResults = sampleResults;
            }
        }
    }
}

