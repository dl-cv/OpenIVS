using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FourImageConcurrencyRegression
{
    internal sealed class Options
    {
        public string Model;
        public string Images;
        public string Output;
        public int Rounds = 3000;
        public int SerialRounds = 200;
        public int Device = 0;

        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var names = new HashSet<string> { "--model", "--images", "--output", "--rounds", "--serial-rounds", "--device" };
            for (int i = 0; i < args.Length; i += 2)
            {
                if (!names.Contains(args[i]) || i + 1 == args.Length || values.ContainsKey(args[i]))
                    throw new ArgumentException("参数无效或重复：" + args[i]);
                values.Add(args[i], args[i + 1]);
            }
            var options = new Options
            {
                Model = Absolute(values, "--model"),
                Images = Absolute(values, "--images"),
                Output = Absolute(values, "--output")
            };
            if (values.ContainsKey("--rounds")) options.Rounds = Positive(values["--rounds"]);
            if (values.ContainsKey("--serial-rounds")) options.SerialRounds = Positive(values["--serial-rounds"]);
            if (values.ContainsKey("--device") && (!int.TryParse(values["--device"], out options.Device) || options.Device < 0))
                throw new ArgumentException("--device 必须为非负整数");
            if (!File.Exists(options.Model)) throw new FileNotFoundException("模型不存在", options.Model);
            if (!Directory.Exists(options.Images)) throw new DirectoryNotFoundException(options.Images);
            if (!Directory.Exists(options.Output)) throw new DirectoryNotFoundException(options.Output);
            return options;
        }

        private static string Absolute(IDictionary<string, string> values, string key)
        {
            string value;
            if (!values.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value)
                || Path.GetPathRoot(value).Length < 3
                || value.IndexOf('%') >= 0 || value.IndexOf('$') >= 0)
                throw new ArgumentException(key + " 必须为已展开的绝对路径");
            return Path.GetFullPath(value);
        }

        private static int Positive(string value)
        {
            int number;
            if (!int.TryParse(value, out number) || number <= 0) throw new ArgumentException("轮数必须为正整数");
            return number;
        }
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Options options;
            try { options = Options.Parse(args); }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                Console.Error.WriteLine("FourImageConcurrencyRegression.exe --model <模型绝对路径> --images <A.bmp 至 D.bmp 目录绝对路径> --output <现有工作目录绝对路径> [--rounds 3000] [--serial-rounds 200] [--device 0]");
                return 2;
            }

            var summary = new JObject
            {
                ["started_utc"] = DateTime.UtcNow,
                ["options"] = JObject.FromObject(options),
                ["passed"] = false
            };
            int exitCode = 1;
            try
            {
                string runDirectory = Path.Combine(options.Output,
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Process.GetCurrentProcess().Id);
                if (Directory.Exists(runDirectory)) throw new IOException("结果目录已存在：" + runDirectory);
                Directory.CreateDirectory(runDirectory);
                summary["output_directory"] = runDirectory;
                Console.WriteLine("结果目录：" + runDirectory);
                string logPath = Path.Combine(runDirectory, "results.jsonl");
                string summaryPath = Path.Combine(runDirectory, "summary.json");
                using (var log = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)))
                {
                    log.AutoFlush = true;
                    try
                    {
                        exitCode = Regression.Run(options, log, summary);
                        summary["passed"] = exitCode == 0;
                    }
                    catch (Exception error)
                    {
                        summary["error"] = error.ToString();
                        log.WriteLine(new JObject { ["phase"] = "fatal", ["error"] = error.ToString() }.ToString(Formatting.None));
                        Console.Error.WriteLine(error);
                    }
                }
                summary["finished_utc"] = DateTime.UtcNow;
                summary["exit_code"] = exitCode;
                File.WriteAllText(summaryPath, summary.ToString(Formatting.Indented), new UTF8Encoding(false));
                Console.WriteLine(summary.ToString(Formatting.Indented));
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            return exitCode;
        }
    }
}
