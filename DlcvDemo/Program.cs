using System;
using System.Windows.Forms;

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
            if (args != null && args.Length > 0
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

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var uiTestWindow = new MainWindow(options))
                {
                    Application.Run(uiTestWindow);
                    return uiTestWindow.UiTestExitCode;
                }
            }

            if (args != null && args.Length > 0)
            {
                CliRunner.InitializeConsole();
                return CliRunner.Run(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var window = new MainWindow())
            {
                Application.Run(window);
            }
            return 0;
        }
    }
}
