using System;
using System.Windows;

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
            if (args != null && args.Length > 0)
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
