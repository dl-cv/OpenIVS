using System;
using System.Windows;
using dlcv_infer_csharp;

namespace DlcvDemo
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            bool hasArguments = args != null && args.Length > 0;
            DllLoader.ShowMissingDllDialog = !hasArguments;

            if (hasArguments
                && string.Equals(args[0], "ui-test", StringComparison.OrdinalIgnoreCase))
            {
                CliRunner.InitializeConsole();
                if (args.Length == 2 && (args[1] == "--help" || args[1] == "-h" || args[1] == "/?"))
                {
                    UiTestOptions.PrintHelp();
                    return 0;
                }
                if (!UiTestOptions.TryParse(args, out UiTestOptions options, out string error))
                {
                    Console.Error.WriteLine("参数错误: " + error);
                    UiTestOptions.PrintHelp();
                    return 2;
                }

                var uiTestApplication = new System.Windows.Application();
                var uiTestWindow = new MainWindow(options);
                uiTestApplication.Run(uiTestWindow);
                return uiTestWindow.UiTestExitCode;
            }

            if (hasArguments)
            {
                CliRunner.InitializeConsole();
                return CliRunner.Run(args);
            }

            var application = new System.Windows.Application();
            var window = new MainWindow();
            application.Run(window);
            return 0;
        }
    }
}
