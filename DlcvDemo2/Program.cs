using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using dlcv_infer_csharp;

namespace DlcvDemo2
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                InitializeCommandLineOutput();
                return RunCommandLine(args);
            }

            FreeConsole();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
            return 0;
        }

        private static int RunCommandLine(string[] args)
        {
            if (args.Length == 1 && IsHelpArgument(args[0]))
            {
                PrintUsage();
                return 0;
            }

            if (args.Length != 4 || !string.Equals(args[0], "--load-three-models", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("命令行参数错误。");
                PrintUsage();
                return 1;
            }

            string extractModelPath = args[1];
            string componentModelPath = args[2];
            string icModelPath = args[3];

            if (!ValidateModelPath(extractModelPath, "元件提取模型")
                || !ValidateModelPath(componentModelPath, "元件检测模型")
                || !ValidateModelPath(icModelPath, "IC检测模型"))
            {
                return 1;
            }

            try
            {
                TimeSpan extractElapsed;
                TimeSpan componentElapsed;
                TimeSpan icElapsed;
                Model extractModel = ThreeModelLoadTimer.Load("元件提取模型", extractModelPath, Console.WriteLine, out extractElapsed);
                Model componentModel = ThreeModelLoadTimer.Load("元件检测模型", componentModelPath, Console.WriteLine, out componentElapsed);
                Model icModel = ThreeModelLoadTimer.Load("IC检测模型", icModelPath, Console.WriteLine, out icElapsed);

                TimeSpan totalElapsed = extractElapsed + componentElapsed + icElapsed;
                Console.WriteLine($"三个模型加载完成，总耗时 {totalElapsed.TotalSeconds:F2} 秒");
                GC.KeepAlive(extractModel);
                GC.KeepAlive(componentModel);
                GC.KeepAlive(icModel);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"加载失败: {ex.Message}");
                return 2;
            }
        }

        private static void InitializeCommandLineOutput()
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch
            {
            }
        }

        private static bool ValidateModelPath(string modelPath, string modelDisplayName)
        {
            if (File.Exists(modelPath))
            {
                return true;
            }

            Console.Error.WriteLine($"{modelDisplayName}文件不存在: {modelPath}");
            return false;
        }

        private static bool IsHelpArgument(string argument)
        {
            return string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "/?", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法:");
            Console.WriteLine("  C# 测试程序2.exe --load-three-models <元件提取模型> <元件检测模型> <IC检测模型>");
            Console.WriteLine();
            Console.WriteLine("说明:");
            Console.WriteLine("  三个模型按参数顺序串行加载，不加载预热模型，也不执行额外推理。");
            Console.WriteLine("  加载过程中实时输出每个模型的耗时，完成后输出总耗时。");
        }

    }
}
