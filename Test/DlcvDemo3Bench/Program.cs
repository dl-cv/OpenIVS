using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using dlcv_infer_csharp;
using DlcvDemo3;
using OpenCvSharp;

namespace DlcvDemo3Bench
{
    /// <summary>
    /// 用法:
    /// DlcvDemo3Bench.exe &lt;model1&gt; &lt;model2&gt; &lt;image&gt; [--threads N] [--runs N] [--target-ms 500] [--warmup] [--sweep]
    /// 位置参数: model1 model2 image
    /// --threads: 模型2并发线程数(默认 4)；多线程共享单个模型2实例，由 SDK 并行推理。
    /// --runs: 正式计时的重复次数(默认 3)，输出 min/max/avg。
    /// --target-ms: 与最短耗时比较，打印是否达标(默认 500)。
    /// --warmup: 在计时前先跑 1 次全链路(默认开启)。
    /// --sweep: 依次尝试线程数 1,2,4,8(以及不超过8的其它2的幂若需要可扩展)，每种跑 runs 次，找出最短 min。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("错误: " + ex.Message);
                Console.Error.WriteLine(ex);
                return 2;
            }
            finally
            {
                Utils.FreeAllModels();
            }
        }

        private static int Run(string[] args)
        {
            Model.EnableConsoleLog = false;

            if (args == null || args.Length < 3)
            {
                PrintUsage();
                return 1;
            }

            var parsed = ParseArgs(args);
            string model1Path = parsed.Model1;
            string model2Path = parsed.Model2;
            string imagePath = parsed.Image;

            if (!File.Exists(model1Path) || !File.Exists(model2Path) || !File.Exists(imagePath))
            {
                Console.Error.WriteLine("模型或图片路径不存在。");
                return 1;
            }

            Console.WriteLine("加载模型1: " + model1Path);
            Model model1 = new Model(model1Path, 0, false);
            Console.WriteLine("加载模型2: " + model2Path);
            Model model2 = new Model(model2Path, 0, false);

            Mat imageBgr = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (imageBgr == null || imageBgr.Empty())
            {
                Console.Error.WriteLine("图片解码失败。");
                return 1;
            }

            using (imageBgr)
            using (Mat imageRgb = new Mat())
            {
                Cv2.CvtColor(imageBgr, imageRgb, ColorConversionCodes.BGR2RGB);

                if (parsed.Sweep)
                {
                    RunSweep(model1, model2, imageRgb, parsed);
                }
                else
                {
                    RunSingleConfig(model1, model2, imageRgb, parsed.Threads, parsed.Runs, parsed.Warmup, parsed.TargetMs);
                }
            }

            model1.Dispose();
            model2.Dispose();
            return 0;
        }

        private static void RunSweep(Model model1, Model model2, Mat imageRgb, ParsedArgs parsed)
        {
            var candidates = new[] { 1, 2, 4, 8, 11, 16 };
            double bestMin = double.MaxValue;
            int bestThreads = 1;

            foreach (int t in candidates)
            {
                double minMs = RunSingleConfig(model1, model2, imageRgb, t, parsed.Runs, parsed.Warmup, parsed.TargetMs, quietHeader: true);
                Console.WriteLine($"[sweep] threads={t}  min={minMs:F2} ms (runs={parsed.Runs})");
                if (minMs < bestMin)
                {
                    bestMin = minMs;
                    bestThreads = t;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"扫线程完成: 最优 threads={bestThreads}, 最短耗时 min={bestMin:F2} ms");
            bool pass = bestMin <= parsed.TargetMs;
            Console.WriteLine($"目标 {parsed.TargetMs:F0} ms: " + (pass ? "达标" : "未达标(受 GPU/模型/输入规模限制，未必能通过软件侧优化达到)"));
        }

        /// <returns>该配置下 runs 次中的最短 ms</returns>
        private static double RunSingleConfig(
            Model model1,
            Model model2,
            Mat imageRgb,
            int threads,
            int runs,
            bool warmup,
            double targetMs,
            bool quietHeader = false)
        {
            if (!quietHeader)
            {
                Console.WriteLine($"配置: threads={threads}, runs={runs}, warmup={warmup}（单实例模型2 + 多线程 InferBatch）");
            }

            if (warmup)
            {
                Demo3Pipeline.Run(imageRgb, model1, model2, threads, progress: null);
            }

            var samples = new List<double>(runs);
            for (int i = 0; i < runs; i++)
            {
                var sw = Stopwatch.StartNew();
                Demo3Pipeline.PipelineRunResult r = Demo3Pipeline.Run(imageRgb, model1, model2, threads, progress: null);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                samples.Add(ms);
                if (!quietHeader)
                {
                    Console.WriteLine($"  第{i + 1}次: {ms:F2} ms  (最终结果数={r.FinalResultCount}, 模型2实际并发={r.Model2ThreadCount})");
                }
            }

            double min = samples.Min();
            double max = samples.Max();
            double avg = samples.Average();
            if (!quietHeader)
            {
                Console.WriteLine($"统计: min={min:F2} ms, max={max:F2} ms, avg={avg:F2} ms");
                bool pass = min <= targetMs;
                Console.WriteLine($"目标 {targetMs:F0} ms (按最短): " + (pass ? "达标" : "未达标"));
            }

            return min;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("DlcvDemo3Bench — C#测试程序3 命令行基准");
            Console.WriteLine("用法: DlcvDemo3Bench <model1> <model2> <image> [--threads N] [--runs N] [--target-ms 500] [--no-warmup] [--sweep]");
            Console.WriteLine("建议: Release | x64 编译后运行。对比 UI 计时请关闭其它占 GPU 进程。");
        }

        private sealed class ParsedArgs
        {
            public string Model1 { get; set; }
            public string Model2 { get; set; }
            public string Image { get; set; }
            public int Threads { get; set; } = 4;
            public int Runs { get; set; } = 3;
            public double TargetMs { get; set; } = 500;
            public bool Warmup { get; set; } = true;
            public bool Sweep { get; set; }
        }

        private static ParsedArgs ParseArgs(string[] args)
        {
            var p = new ParsedArgs();
            var pos = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--threads" && i + 1 < args.Length)
                {
                    p.Threads = int.Parse(args[++i]);
                }
                else if (a == "--runs" && i + 1 < args.Length)
                {
                    p.Runs = int.Parse(args[++i]);
                }
                else if (a == "--target-ms" && i + 1 < args.Length)
                {
                    p.TargetMs = double.Parse(args[++i]);
                }
                else if (a == "--no-warmup")
                {
                    p.Warmup = false;
                }
                else if (a == "--warmup")
                {
                    p.Warmup = true;
                }
                else if (a == "--sweep")
                {
                    p.Sweep = true;
                }
                else if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    pos.Add(a);
                }
            }

            if (pos.Count < 3)
            {
                throw new ArgumentException("至少需要 3 个位置参数: model1 model2 image");
            }

            p.Model1 = pos[0];
            p.Model2 = pos[1];
            p.Image = pos[2];
            if (p.Runs < 1)
            {
                p.Runs = 1;
            }

            return p;
        }
    }
}
