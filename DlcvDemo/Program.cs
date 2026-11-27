using System;
using System.Windows.Forms;

namespace DlcvDemo
{
    public static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static int Main(string[] args) => Run(args, null);

        public static string CurrentLanguage => I18n.CurrentLanguage;

        [STAThread]
        public static int Run(string[] args, IDemoExecution execution)
        {
            if (execution == null && args != null && args.Length > 0 && string.Equals(args[0], "remote-worker", StringComparison.OrdinalIgnoreCase))
                return RemoteWorker.Run(args);
            if (execution == null && args != null && args.Length > 0 && string.Equals(args[0], "remote-worker-selftest", StringComparison.OrdinalIgnoreCase))
                return RemoteWorker.RunWithStreams((input, output) => RemoteWorkerSelfTest.Run(args, output));

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
                using (var uiTestWindow = new MainWindow(options, execution))
                {
                    Application.Run(uiTestWindow);
                    return uiTestWindow.UiTestExitCode;
                }
            }

            if (args != null && args.Length > 0)
            {
                CliRunner.InitializeConsole();
                if (execution == null) return CliRunner.Run(args);
                Console.Error.WriteLine("云端测试程序的命令行支持 ui-test。");
                return 2;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var window = new MainWindow(execution))
            {
                Application.Run(window);
            }
            return 0;
        }
    }
}
