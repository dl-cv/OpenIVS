using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dlcv_infer_csharp;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using CSharpObjectResult = dlcv_infer_csharp.Utils.CSharpObjectResult;
using CSharpResult = dlcv_infer_csharp.Utils.CSharpResult;
using CSharpSampleResult = dlcv_infer_csharp.Utils.CSharpSampleResult;

namespace DlcvDemo3
{
    /// <summary>
    /// 测试程序3 推理管线（WinForms 与命令行基准共用）。
    /// </summary>
    public static class Demo3Pipeline
    {
        public const int FixedCropWidth = 128;
        public const int FixedCropHeight = 192;

        public sealed class InferenceProgressInfo
        {
            public int Percent { get; set; }
            public string Stage { get; set; }
        }

        public sealed class PipelineRunResult
        {
            public CSharpResult DisplayResult { get; set; }
            public int Model1ObjectCount { get; set; }
            public int CropCount { get; set; }
            public int Model2BatchLimit { get; set; }
            public int Model2ThreadCount { get; set; }
            public int FinalResultCount { get; set; }
            public List<CSharpObjectResult> FinalObjects { get; } = new List<CSharpObjectResult>();
            public List<string> Logs { get; } = new List<string>();
        }

        public sealed class CenteredCropContext : IDisposable
        {
            public bool IsValid { get; set; }
            public string InvalidReason { get; set; }
            public Mat CropRgb { get; set; }
            public Rect RequestedRect { get; set; }
            public double[] CropToFullAffine { get; set; }

            public static CenteredCropContext Invalid(string reason)
            {
                return new CenteredCropContext
                {
                    IsValid = false,
                    InvalidReason = reason ?? "未知错误"
                };
            }

            public void Dispose()
            {
                if (CropRgb != null)
                {
                    CropRgb.Dispose();
                    CropRgb = null;
                }
            }
        }

        public static int NormalizeThreadCount(int requested)
        {
            if (requested < 1)
            {
                return 1;
            }
            if (requested > 32)
            {
                return 32;
            }
            return requested;
        }

        public static void ReportProgress(IProgress<InferenceProgressInfo> progress, int percent, string stage)
        {
            if (progress == null)
            {
                return;
            }

            progress.Report(new InferenceProgressInfo
            {
                Percent = ClampProgressPercent(percent),
                Stage = stage ?? string.Empty
            });
        }

        private static int ClampProgressPercent(int percent)
        {
            if (percent < 0)
            {
                return 0;
            }
            if (percent > 100)
            {
                return 100;
            }
            return percent;
        }

        /// <param name="model2">模型2（识别）。多线程时各任务共享此实例并发调用 <see cref="Model.InferBatch"/>，由底层 SDK 保证并行安全。</param>
        public static PipelineRunResult Run(
            Mat fullImageRgb,
            Model model1,
            Model model2,
            int requestedThreadCount,
            IProgress<InferenceProgressInfo> progress = null)
        {
            if (model1 == null)
            {
                throw new ArgumentNullException(nameof(model1));
            }
            if (model2 == null)
            {
                throw new ArgumentNullException(nameof(model2));
            }

            var runResult = new PipelineRunResult();
            var inferParams = new JObject
            {
                ["with_mask"] = false
            };

            var cropContexts = new List<CenteredCropContext>();
            try
            {
                ReportProgress(progress, 10, "模型1整图推理");
                CSharpResult model1Result = model1.Infer(fullImageRgb, inferParams);

                var model1Objects = new List<CSharpObjectResult>();
                foreach (var obj in ExtractObjects(model1Result))
                {
                    CSharpObjectResult clamped;
                    if (TryClampObjectToImage(obj, fullImageRgb.Width, fullImageRgb.Height, out clamped))
                    {
                        model1Objects.Add(clamped);
                    }
                }
                runResult.Model1ObjectCount = model1Objects.Count;

                ReportProgress(progress, 22, "按模型1结果在原图裁图");
                foreach (var obj in model1Objects)
                {
                    Point2d center = GetObjectCenter(obj);
                    CenteredCropContext context = BuildCenteredCropContext(
                        fullImageRgb,
                        center,
                        FixedCropWidth,
                        FixedCropHeight);

                    if (context.IsValid)
                    {
                        cropContexts.Add(context);
                    }
                    else
                    {
                        runResult.Logs.Add($"跳过目标[{obj.CategoryName}]：{context.InvalidReason}");
                        context.Dispose();
                    }
                }
                runResult.CropCount = cropContexts.Count;

                if (runResult.Model1ObjectCount == 0)
                {
                    runResult.Logs.Add("模型1未检测到目标。");
                }

                int batchLimit = Math.Max(1, model2.GetMaxBatchSize());
                runResult.Model2BatchLimit = batchLimit;
                List<List<CenteredCropContext>> chunks = SplitIntoChunks(cropContexts, batchLimit).ToList();
                int total = cropContexts.Count;

                int normalizedRequested = NormalizeThreadCount(requestedThreadCount);
                int threadCount = Math.Min(normalizedRequested, Math.Max(1, chunks.Count));
                if (normalizedRequested > threadCount && normalizedRequested > 1)
                {
                    runResult.Logs.Add($"模型2并发度已限制为 {threadCount}（batch 段数={chunks.Count}）。");
                }
                runResult.Model2ThreadCount = threadCount;
                ReportProgress(progress, 30, $"模型2线程池推理（线程={threadCount}）");

                if (chunks.Count == 0)
                {
                    ReportProgress(progress, 95, "整理显示结果");
                    runResult.DisplayResult = BuildDisplayResult(runResult.FinalObjects);
                    runResult.FinalResultCount = runResult.FinalObjects.Count;
                    ReportProgress(progress, 100, "推理完成");
                    return runResult;
                }

                var partials = new List<CSharpObjectResult>[chunks.Count];
                int processed = 0;
                object logLock = new object();

                if (threadCount <= 1)
                {
                    for (int c = 0; c < chunks.Count; c++)
                    {
                        try
                        {
                            partials[c] = ProcessModel2Chunk(fullImageRgb, chunks[c], model2, inferParams);
                        }
                        catch (Exception ex)
                        {
                            lock (logLock)
                            {
                                int begin = c * batchLimit + 1;
                                runResult.Logs.Add($"模型2 batch 推理失败(从第 {begin} 张开始，共 {chunks[c].Count} 张)：{ex.Message}");
                            }
                            partials[c] = new List<CSharpObjectResult>();
                        }
                        finally
                        {
                            int done = Interlocked.Add(ref processed, chunks[c].Count);
                            int percent = 30 + (int)Math.Round(55.0 * done / Math.Max(1, total));
                            ReportProgress(progress, percent, $"模型2 batch 推理 {done}/{total}");
                        }
                    }
                }
                else
                {
                    var tasks = new Task[threadCount];
                    for (int t = 0; t < threadCount; t++)
                    {
                        int tid = t;
                        tasks[tid] = Task.Run(() =>
                        {
                            for (int c = tid; c < chunks.Count; c += threadCount)
                            {
                                try
                                {
                                    partials[c] = ProcessModel2Chunk(fullImageRgb, chunks[c], model2, inferParams);
                                }
                                catch (Exception ex)
                                {
                                    lock (logLock)
                                    {
                                        int begin = c * batchLimit + 1;
                                        runResult.Logs.Add($"模型2 batch 推理失败(从第 {begin} 张开始，共 {chunks[c].Count} 张)：{ex.Message}");
                                    }
                                    partials[c] = new List<CSharpObjectResult>();
                                }
                                finally
                                {
                                    int done = Interlocked.Add(ref processed, chunks[c].Count);
                                    int percent = 30 + (int)Math.Round(55.0 * done / Math.Max(1, total));
                                    ReportProgress(progress, percent, $"模型2线程池推理 {done}/{total}");
                                }
                            }
                        });
                    }

                    Task.WaitAll(tasks);
                }

                foreach (var list in partials)
                {
                    if (list != null && list.Count > 0)
                    {
                        runResult.FinalObjects.AddRange(list);
                    }
                }

                ReportProgress(progress, 95, "整理显示结果");
                runResult.DisplayResult = BuildDisplayResult(runResult.FinalObjects);
                runResult.FinalResultCount = runResult.FinalObjects.Count;
                ReportProgress(progress, 100, "推理完成");
                return runResult;
            }
            finally
            {
                foreach (var context in cropContexts)
                {
                    context.Dispose();
                }
            }
        }

        private static List<CSharpObjectResult> ProcessModel2Chunk(
            Mat fullImageRgb,
            List<CenteredCropContext> chunk,
            Model workerModel,
            JObject inferParams)
        {
            var mappedResults = new List<CSharpObjectResult>();
            List<Mat> mats = chunk.Select(x => x.CropRgb).ToList();
            CSharpResult batchResult = workerModel.InferBatch(mats, inferParams);
            for (int i = 0; i < chunk.Count; i++)
            {
                List<CSharpObjectResult> localObjects = GetSampleObjects(batchResult, i);
                foreach (var localObj in localObjects)
                {
                    CSharpObjectResult mapped;
                    if (!TryMapObjectWithAffine(localObj, chunk[i].CropToFullAffine, out mapped))
                    {
                        continue;
                    }

                    CSharpObjectResult clamped;
                    if (TryClampObjectToImage(mapped, fullImageRgb.Width, fullImageRgb.Height, out clamped))
                    {
                        mappedResults.Add(clamped);
                    }
                }
            }

            return mappedResults;
        }

        private static IEnumerable<CSharpObjectResult> ExtractObjects(CSharpResult result)
        {
            if (result.SampleResults == null || result.SampleResults.Count == 0)
            {
                return Enumerable.Empty<CSharpObjectResult>();
            }

            var sample = result.SampleResults[0];
            return sample.Results ?? new List<CSharpObjectResult>();
        }

        private static List<CSharpObjectResult> GetSampleObjects(CSharpResult batchResult, int sampleIndex)
        {
            if (batchResult.SampleResults == null)
            {
                return new List<CSharpObjectResult>();
            }
            if (sampleIndex < 0 || sampleIndex >= batchResult.SampleResults.Count)
            {
                return new List<CSharpObjectResult>();
            }

            return batchResult.SampleResults[sampleIndex].Results ?? new List<CSharpObjectResult>();
        }

        private static IEnumerable<List<CenteredCropContext>> SplitIntoChunks(List<CenteredCropContext> source, int chunkSize)
        {
            if (source == null || source.Count == 0)
            {
                yield break;
            }

            int realChunkSize = Math.Max(1, chunkSize);
            for (int i = 0; i < source.Count; i += realChunkSize)
            {
                int count = Math.Min(realChunkSize, source.Count - i);
                yield return source.GetRange(i, count);
            }
        }

        private static bool TryMapObjectWithAffine(CSharpObjectResult localObj, double[] affine2x3, out CSharpObjectResult mapped)
        {
            mapped = localObj;
            if (localObj.Bbox == null || localObj.Bbox.Count < 4 || affine2x3 == null || affine2x3.Length != 6)
            {
                return false;
            }

            var bbox = new List<double>(localObj.Bbox);
            double dx = affine2x3[2];
            double dy = affine2x3[5];
            bool isRotated = localObj.WithAngle || bbox.Count == 5;

            bbox[0] += dx;
            bbox[1] += dy;

            mapped.Bbox = bbox;
            mapped.WithBbox = true;
            mapped.WithAngle = isRotated || localObj.WithAngle;
            return true;
        }

        private static bool TryClampObjectToImage(CSharpObjectResult obj, int imageWidth, int imageHeight, out CSharpObjectResult clamped)
        {
            clamped = obj;
            if (obj.Bbox == null || obj.Bbox.Count < 4 || imageWidth <= 0 || imageHeight <= 0)
            {
                return false;
            }

            var bbox = new List<double>(obj.Bbox);
            bool isRotated = obj.WithAngle || obj.Bbox.Count == 5;
            if (isRotated)
            {
                double cx = bbox[0];
                double cy = bbox[1];
                double w = bbox[2];
                double h = bbox[3];
                if (w <= 0 || h <= 0)
                {
                    return false;
                }

                double left = cx - w / 2.0;
                double right = cx + w / 2.0;
                double top = cy - h / 2.0;
                double bottom = cy + h / 2.0;

                double clampedLeft = Math.Max(0.0, left);
                double clampedTop = Math.Max(0.0, top);
                double clampedRight = Math.Min(imageWidth, right);
                double clampedBottom = Math.Min(imageHeight, bottom);
                if (clampedRight <= clampedLeft || clampedBottom <= clampedTop)
                {
                    return false;
                }

                bbox[0] = (clampedLeft + clampedRight) / 2.0;
                bbox[1] = (clampedTop + clampedBottom) / 2.0;
                bbox[2] = clampedRight - clampedLeft;
                bbox[3] = clampedBottom - clampedTop;
            }
            else
            {
                double x = bbox[0];
                double y = bbox[1];
                double w = bbox[2];
                double h = bbox[3];
                if (w <= 0 || h <= 0)
                {
                    return false;
                }

                double left = Math.Max(0.0, x);
                double top = Math.Max(0.0, y);
                double right = Math.Min(imageWidth, x + w);
                double bottom = Math.Min(imageHeight, y + h);
                if (right <= left || bottom <= top)
                {
                    return false;
                }

                bbox[0] = left;
                bbox[1] = top;
                bbox[2] = right - left;
                bbox[3] = bottom - top;
            }

            clamped.Bbox = bbox;
            clamped.WithBbox = true;
            return true;
        }

        private static Point2d GetObjectCenter(CSharpObjectResult obj)
        {
            if (obj.Bbox == null || obj.Bbox.Count < 4)
            {
                return new Point2d(0, 0);
            }

            bool isRotated = obj.WithAngle || obj.Bbox.Count == 5;
            if (isRotated)
            {
                return new Point2d(obj.Bbox[0], obj.Bbox[1]);
            }

            return new Point2d(obj.Bbox[0] + obj.Bbox[2] / 2.0, obj.Bbox[1] + obj.Bbox[3] / 2.0);
        }

        private static CenteredCropContext BuildCenteredCropContext(Mat fullImageRgb, Point2d center, int cropW, int cropH)
        {
            int requestLeft = (int)Math.Round(center.X - cropW / 2.0);
            int requestTop = (int)Math.Round(center.Y - cropH / 2.0);
            Rect requestedRect = new Rect(requestLeft, requestTop, cropW, cropH);
            Rect imageRect = new Rect(0, 0, fullImageRgb.Width, fullImageRgb.Height);
            Rect srcRect = IntersectRect(requestedRect, imageRect);

            if (srcRect.Width <= 0 || srcRect.Height <= 0)
            {
                return CenteredCropContext.Invalid("裁图完全落在图像外");
            }

            Mat crop = null;
            try
            {
                crop = new Mat(new Size(cropW, cropH), fullImageRgb.Type(), Scalar.Black);
                Rect dstRect = new Rect(srcRect.X - requestLeft, srcRect.Y - requestTop, srcRect.Width, srcRect.Height);

                using (var srcView = new Mat(fullImageRgb, srcRect))
                using (var dstView = new Mat(crop, dstRect))
                {
                    srcView.CopyTo(dstView);
                }

                return new CenteredCropContext
                {
                    IsValid = true,
                    InvalidReason = string.Empty,
                    CropRgb = crop,
                    RequestedRect = requestedRect,
                    CropToFullAffine = new[] { 1.0, 0.0, (double)requestLeft, 0.0, 1.0, (double)requestTop }
                };
            }
            catch (Exception ex)
            {
                if (crop != null)
                {
                    crop.Dispose();
                }
                return CenteredCropContext.Invalid(ex.Message);
            }
        }

        private static Rect IntersectRect(Rect a, Rect b)
        {
            int x1 = Math.Max(a.X, b.X);
            int y1 = Math.Max(a.Y, b.Y);
            int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
            if (x2 <= x1 || y2 <= y1)
            {
                return new Rect(0, 0, 0, 0);
            }
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }

        private static CSharpResult BuildDisplayResult(List<CSharpObjectResult> finalObjects)
        {
            var sampleResults = new List<CSharpSampleResult>
            {
                new CSharpSampleResult(finalObjects ?? new List<CSharpObjectResult>())
            };
            return new CSharpResult(sampleResults);
        }
    }
}
