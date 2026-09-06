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
        internal string ScreenshotPath { get; private set; }
        internal decimal Threshold { get; private set; } = 0.5m;
        internal int DeviceId { get; private set; } = 0;
        internal bool? CalcMean { get; private set; }
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
                    case "--screenshot":
                        options.ScreenshotPath = value;
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
            try
            {
                string model = Path.GetFullPath(options.ModelPath);
                string image = Path.GetFullPath(options.ImagePath);
                string output = Path.GetFullPath(options.OutputPath);
                string screenshot = options.ScreenshotPath == null ? null : Path.GetFullPath(options.ScreenshotPath);
                if (string.Equals(output, model, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(output, image, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(output + ".tmp", model, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(output + ".tmp", image, StringComparison.OrdinalIgnoreCase)
                    || (screenshot != null && (string.Equals(screenshot, model, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(screenshot, image, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(screenshot, output, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(screenshot, output + ".tmp", StringComparison.OrdinalIgnoreCase))))
                {
                    error = "测试输出路径不能与输入文件或其他输出文件相同。";
                    return false;
                }
                if (screenshot != null && !string.Equals(Path.GetExtension(screenshot), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    error = "--screenshot 必须指定 PNG 文件路径。";
                    return false;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                error = "测试路径无效: " + ex.Message;
                return false;
            }
            return true;
        }

        internal static void PrintHelp()
        {
            Console.Out.WriteLine("Usage:");
            Console.Out.WriteLine("  \"C# 测试程序.exe\" ui-test --model <path> --image <path> --output <jsonPath> [--threshold <0..1>] [--device <int>] [--calc-mean <true|false>] [--interactive-dialogs <true|false>] [--screenshot <pngPath>]");
            Console.Out.WriteLine();
            Console.Out.WriteLine("ui-test 启动正式程序使用的 WinForms 窗口，将进度和结果写入 --output。");
            Console.Out.WriteLine("interactive-dialogs=false 不弹出文件对话框且不激活窗口；--screenshot 通过窗口绘制代码保存截图。");
        }
    }
}
