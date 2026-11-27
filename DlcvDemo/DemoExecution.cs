using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using dlcv_infer_csharp;
using DLCV;

namespace DlcvDemo
{
    public interface IDemoExecution
    {
        JObject GetGpuInfo();
        JObject GetDogInfo();
        string GetEnvironmentInfo();
        string DescribeCudaMemory(int deviceId);
        IDemoModel LoadModel(string path, int deviceId, bool rpcMode);
        void FreeAllModels();
    }

    public interface IDemoModel : IDisposable
    {
        JObject GetModelInfo();
        JObject GetCachedModelInfo();
        Utils.CSharpResult InferBatch(List<Mat> images, JObject parameters);
        JToken InferOneOutJson(Mat image, JObject parameters);
        IDemoPressureTest StartPressureTest(Mat image, int threadCount, int batchSize, bool checkConsistency);
    }

    public interface IDemoPressureTest : IDisposable
    {
        DemoPressureStatus GetStatus(string language);
    }

    public sealed class DemoPressureStatus
    {
        public string Statistics { get; set; }
        public JObject Baseline { get; set; }
        public JObject DifferentResult { get; set; }
        public string Error { get; set; }
    }

    internal sealed class LocalDemoExecution : IDemoExecution
    {
        public JObject GetGpuInfo() => Utils.GetGpuInfo();

        public JObject GetDogInfo()
        {
            try
            {
                return sntl_admin_csharp.DogUtils.GetAllDogInfo();
            }
            catch
            {
                return new JObject
                {
                    ["sentinel"] = new JObject { ["devices"] = new JArray(), ["features"] = new JArray() },
                    ["virbox"] = new JObject { ["devices"] = new JArray(), ["features"] = new JArray() }
                };
            }
        }

        public string GetEnvironmentInfo() => EnvironmentInfoCollector.Collect();
        public string DescribeCudaMemory(int deviceId) => CudaMemoryInfo.Describe((uint)Math.Max(0, deviceId));
        public IDemoModel LoadModel(string path, int deviceId, bool rpcMode) => new LocalDemoModel(path, deviceId, rpcMode);
        public void FreeAllModels() => Utils.FreeAllModels();
    }

    internal sealed class LocalDemoModel : IDemoModel
    {
        private readonly Model model;
        private readonly bool flowModel;

        internal LocalDemoModel(string path, int deviceId, bool rpcMode)
        {
            string extension = Path.GetExtension(path);
            flowModel = string.Equals(extension, ".dvst", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".dvso", StringComparison.OrdinalIgnoreCase);
            model = new Model(path, deviceId, rpcMode);
        }

        public JObject GetModelInfo() => model.GetModelInfo();
        public JObject GetCachedModelInfo() => model.GetCachedModelInfo();
        public Utils.CSharpResult InferBatch(List<Mat> images, JObject parameters) => model.InferBatch(images, parameters);
        public JToken InferOneOutJson(Mat image, JObject parameters) => (JToken)model.InferOneOutJson(image, parameters);
        public void Dispose() => model.Dispose();

        public IDemoPressureTest StartPressureTest(Mat image, int threadCount, int batchSize, bool checkConsistency)
            => new LocalPressureTest(model, image, threadCount, batchSize, flowModel, checkConsistency);
    }

    internal sealed class LocalPressureTest : IDemoPressureTest
    {
        private readonly PressureTestRunner runner;
        private readonly Model model;
        private readonly Mat image;
        private readonly bool checkConsistency;
        private readonly object resultLock = new object();
        private bool disposed;
        private JObject baseline;
        private JObject differentResult;
        private string error;

        internal LocalPressureTest(Model model, Mat image, int threads, int batch, bool flowModel, bool checkConsistency)
        {
            this.model = model;
            this.checkConsistency = checkConsistency;
            this.image = image.Clone();
            runner = new PressureTestRunner(threads, 1000000, batch);
            runner.SetFlowModelTiming(flowModel);
            var images = new List<Mat>(batch);
            for (int i = 0; i < batch; i++) images.Add(this.image);
            runner.SetTestAction(Infer, images);
            try
            {
                runner.Start();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Infer(object argument)
        {
            try
            {
                var parameters = new JObject { ["with_mask"] = false };
                var stopwatch = Stopwatch.StartNew();
                var result = model.InferInternal((List<Mat>)argument, parameters);
                stopwatch.Stop();
                try
                {
                    DlcvModules.InferTiming.GetLast(out double sdkMs, out double totalMs);
                    if (totalMs <= 0) totalMs = stopwatch.Elapsed.TotalMilliseconds;
                    if (sdkMs <= 0) sdkMs = totalMs;
                    var nodeTimings = new List<PressureNodeTiming>();
                    foreach (var node in DlcvModules.InferTiming.GetLastFlowNodeTimings())
                    {
                        if (node != null)
                            nodeTimings.Add(new PressureNodeTiming(node.NodeId, node.NodeType, node.NodeTitle, node.ElapsedMs));
                    }
                    runner.RecordLatencyBreakdown(sdkMs, totalMs, nodeTimings);
                    if (checkConsistency)
                    {
                        lock (resultLock)
                        {
                            if (baseline == null) baseline = result.Item1;
                            else if (differentResult == null && error == null && !string.Equals(
                                baseline.ToString(Formatting.None), result.Item1.ToString(Formatting.None), StringComparison.Ordinal))
                            {
                                differentResult = result.Item1;
                                runner.RequestStop();
                            }
                        }
                    }
                }
                finally
                {
                    if (result.Item2 != IntPtr.Zero)
                        DllLoader.Instance.dlcv_free_model_result(result.Item2);
                }
            }
            catch (Exception ex)
            {
                lock (resultLock)
                {
                    if (error == null && differentResult == null)
                    {
                        error = ex.Message;
                        runner.RequestStop();
                    }
                }
                throw;
            }
        }

        public DemoPressureStatus GetStatus(string language)
        {
            var status = new DemoPressureStatus();
            lock (resultLock)
            {
                status.Baseline = baseline;
                status.DifferentResult = differentResult;
                status.Error = error;
            }
            if (status.Error != null || status.DifferentResult != null)
                runner.Stop(waitForCompletion: true);
            status.Statistics = runner.GetStatistics(false, false, text => I18n.T(text, language));
            return status;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            runner.Stop(waitForCompletion: true);
            image.Dispose();
        }
    }
}
