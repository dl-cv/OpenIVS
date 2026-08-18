using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DlcvDemo
{
    internal sealed class UiTestOptions
    {
        internal string ModelPath { get; private set; }
        internal string ImagePath { get; private set; }
        internal string OutputPath { get; private set; }
        internal decimal Threshold { get; private set; } = 0.5m;
        internal int DeviceId { get; private set; } = 0;
        internal bool CalcMean { get; private set; }
        internal bool InteractiveDialogs { get; private set; }

        internal static bool TryParse(string[] args, out UiTestOptions options, out string error)
        {
            options = new UiTestOptions();
            error = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < args.Length; i += 2)
            {
                string key = args[i];
                if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("--", StringComparison.Ordinal))
                {
                    error = "无效参数名: " + (key ?? string.Empty);
                    return false;
                }
                if (i + 1 >= args.Length)
                {
                    error = "参数缺少值: " + key;
                    return false;
                }
                if (!seen.Add(key))
                {
                    error = "参数重复: " + key;
                    return false;
                }

                string value = args[i + 1];
                switch (key.ToLowerInvariant())
                {
                    case "--model":
                        options.ModelPath = value;
                        break;
                    case "--image":
                        options.ImagePath = value;
                        break;
                    case "--output":
                        options.OutputPath = value;
                        break;
                    case "--threshold":
                        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal threshold)
                            || threshold < 0m || threshold > 1m)
                        {
                            error = "--threshold 必须是 0 到 1 之间的数字。";
                            return false;
                        }
                        options.Threshold = threshold;
                        break;
                    case "--device":
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceId)
                            || deviceId < -2)
                        {
                            error = "--device 必须是 -2（OpenVINO）、-1（CPU）或非负整数（GPU 编号）。";
                            return false;
                        }
                        options.DeviceId = deviceId;
                        break;
                    case "--calc-mean":
                        if (!bool.TryParse(value, out bool calcMean))
                        {
                            error = "--calc-mean 必须是 true 或 false。";
                            return false;
                        }
                        options.CalcMean = calcMean;
                        break;
                    case "--interactive-dialogs":
                        if (!bool.TryParse(value, out bool interactiveDialogs))
                        {
                            error = "--interactive-dialogs 必须是 true 或 false。";
                            return false;
                        }
                        options.InteractiveDialogs = interactiveDialogs;
                        break;
                    default:
                        error = "未知参数: " + key;
                        return false;
                }
            }

            if (string.IsNullOrWhiteSpace(options.ModelPath))
            {
                error = "缺少必需参数: --model";
                return false;
            }
            if (string.IsNullOrWhiteSpace(options.ImagePath))
            {
                error = "缺少必需参数: --image";
                return false;
            }
            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                error = "缺少必需参数: --output";
                return false;
            }
            if (!File.Exists(options.ModelPath))
            {
                error = "模型文件不存在: " + options.ModelPath;
                return false;
            }
            if (!File.Exists(options.ImagePath))
            {
                error = "图片文件不存在: " + options.ImagePath;
                return false;
            }
            return true;
        }

        internal static void PrintHelp()
        {
            Console.Out.WriteLine("Usage:");
            Console.Out.WriteLine("  \"C# 测试程序.exe\" ui-test --model <path> --image <path> --output <jsonPath> [--threshold <0..1>] [--device <int>] [--calc-mean <true|false>] [--interactive-dialogs <true|false>]");
            Console.Out.WriteLine();
            Console.Out.WriteLine("ui-test starts the real WPF window and writes its progress/result to --output.");
            Console.Out.WriteLine("interactive-dialogs=false loads inputs without opening file dialogs and does not activate the window.");
        }
    }
}
