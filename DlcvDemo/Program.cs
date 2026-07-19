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
            if (args != null && args.Length > 0)
            {
                CliRunner.InitializeConsole();
                return CliRunner.Run(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
            return 0;
        }
    }
}
