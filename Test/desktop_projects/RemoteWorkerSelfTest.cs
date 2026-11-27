using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using static dlcv_infer_csharp.Utils;

namespace DlcvDemo
{
    internal static class RemoteWorkerSelfTest
    {
        internal static int Run(string[] args, TextWriter output)
        {
            if (args.Length != 3 || args[1] != "--root" || !Directory.Exists(args[2]))
                throw new ArgumentException("用法: remote-worker-selftest --root <现有测试目录>");
            string root = Path.GetFullPath(Path.Combine(args[2], "worker-test-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "测试.dvt"), "model");
                using (var image = new Mat(2, 2, MatType.CV_8UC3, new Scalar(7, 29, 211)))
                {
                    Require(Cv2.ImEncode(".png", image, out var png, (int[])null), "测试图片编码失败。");
                    File.WriteAllBytes(Path.Combine(root, "图片.png"), png);
                }
                var execution = new FakeExecution();
                var requests = new[]
                {
                    @"{""id"":1,""action"":""ping""}",
                    @"{""id"":2,""action"":""load_model"",""model"":""../测试.dvt"",""device_id"":0}",
                    @"{""id"":3,""action"":""load_model"",""model"":""测试.dvt"",""device_id"":0,""rpc_mode"":true}",
                    @"{""id"":4,""action"":""load_model"",""model"":""测试.dvt"",""device_id"":0}",
                    @"{""id"":5,""action"":""infer_batch"",""image"":""图片.png"",""batch_size"":2,""parameters"":{""threshold"":0.42,""with_mask"":true}}",
                    @"{""id"":6,""action"":""infer_json"",""image"":""图片.png"",""parameters"":{""arbitrary"":true}}",
                    @"{""id"":7,""action"":""pressure_start"",""image"":""图片.png"",""thread_count"":3,""batch_size"":2,""check_consistency"":true}",
                    @"{""id"":8,""action"":""pressure_status"",""language"":""en-US""}",
                    @"{""id"":9,""action"":""infer_json"",""image"":""图片.png""}"
                };
                var response = new StringWriter();
                using (var worker = new RemoteWorker(root, execution))
                    worker.Serve(new StringReader(string.Join("\n", requests)), response);
                var replies = new List<JObject>();
                using (var reader = new StringReader(response.ToString()))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) replies.Add(JObject.Parse(line));
                }
                Require(replies.Count == requests.Length, "响应数量与请求不一致。");
                for (int i = 0; i < replies.Count; i++)
                {
                    int id = i + 1;
                    Require(replies[i].Value<int>("id") == id, "响应 ID 错误。");
                    Require(replies[i].Value<bool>("ok") == (id != 2 && id != 3 && id != 6), "响应状态错误。");
                }
                Require(execution.LoadCount == 1 && execution.Model.Disposed && execution.Model.Pressure.Disposed,
                    "无效请求启动了模型，或 EOF 后资源未释放。");
                var result = JObject.Parse(ReadResult(root, replies[4]));
                var decoded = DemoRemoteResult.Decode(result);
                try
                {
                    var item = decoded.SampleResults[1].Results[0];
                    Require(item.Mask.At<byte>(0, 0) == 0 && item.Mask.At<byte>(0, 1) == 255 && item.CategoryName == "标记",
                        "遮罩像素或目标信息传输错误。");
                    Require(decoded.JsonText.Contains("width") && !decoded.JsonText.Contains("mask_ptr"), "推理 JSON 保留了本机地址或丢失字段。");
                }
                finally { DemoRemoteResult.DisposeMasks(decoded); }
                Require(replies[7]["result"].Value<string>("statistics") == "en-US" && JArray.Parse(ReadResult(root, replies[8])).Count == 1,
                    "统计语言或 JSON 推理结果错误。");
                output.WriteLine(new JObject { ["status"] = "passed" }.ToString(Formatting.None));
                return 0;
            }
            finally
            {
                foreach (var file in Directory.GetFiles(root)) File.Delete(file);
                Directory.Delete(root);
            }
        }

        private static string ReadResult(string root, JObject reply)
        {
            string file = reply.Value<string>("result_file");
            Require(file != null && file.StartsWith("result-", StringComparison.Ordinal) && Path.GetFileName(file) == file,
                "推理结果未保存到会话文件。");
            return File.ReadAllText(Path.Combine(root, file));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeExecution : IDemoExecution
        {
            internal int LoadCount;
            internal readonly FakeModel Model = new FakeModel();
            public JObject GetGpuInfo() => new JObject();
            public JObject GetDogInfo() => new JObject();
            public string GetEnvironmentInfo() => "test";
            public string DescribeCudaMemory(int deviceId) => "test";
            public void FreeAllModels() { }
            public IDemoModel LoadModel(string path, int deviceId, bool rpcMode)
            {
                Require(Path.GetFileName(path) == "测试.dvt" && deviceId == 0 && !rpcMode, "模型路径或设备错误。");
                LoadCount++;
                return Model;
            }
        }

        private sealed class FakeModel : IDemoModel
        {
            internal bool Disposed;
            internal readonly FakePressure Pressure = new FakePressure();
            public JObject GetModelInfo() => new JObject { ["task_type"] = "segmentation" };
            public JObject GetCachedModelInfo() => GetModelInfo();
            public void Dispose() => Disposed = true;
            public JToken InferOneOutJson(Mat image, JObject parameters) => new JArray(new JObject { ["category_name"] = "标记" });
            public IDemoPressureTest StartPressureTest(Mat image, int threadCount, int batchSize, bool checkConsistency)
            {
                Require(!image.Empty() && threadCount == 3 && batchSize == 2 && checkConsistency, "压力测试输入错误。");
                return Pressure;
            }
            public CSharpResult InferBatch(List<Mat> images, JObject parameters)
            {
                var pixel = images[0].At<Vec3b>(0, 0);
                Require(images.Count == 2 && pixel.Item0 == 7 && pixel.Item1 == 29 && pixel.Item2 == 211
                    && Math.Abs(parameters.Value<double>("threshold") - 0.42) < 0.00001 && parameters.Value<bool>("with_mask"),
                    "推理输入通道、批量或参数错误。");
                var samples = new List<CSharpSampleResult>();
                foreach (var image in images)
                {
                    var mask = new Mat(1, 2, MatType.CV_8UC1, Scalar.All(0));
                    mask.Set<byte>(0, 1, 255);
                    samples.Add(new CSharpSampleResult(new List<CSharpObjectResult>
                    {
                        new CSharpObjectResult(2, "标记", 0.92f, 1, new List<double> { 0, 0, 2, 1 }, true, mask, true)
                    }));
                }
                return new CSharpResult(samples, @"{""mask"":{""mask_ptr"":1234,""width"":2}}");
            }
        }

        private sealed class FakePressure : IDemoPressureTest
        {
            internal bool Disposed;
            public DemoPressureStatus GetStatus(string language) => new DemoPressureStatus { Statistics = language };
            public void Dispose() => Disposed = true;
        }
    }
}
