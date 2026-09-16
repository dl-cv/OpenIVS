using System;
using System.Windows.Forms;

namespace DlcvCSharpCppTest
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "model-test") return CommandLineTest.Run(args);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "designer-test") return SelfTest.RunDesigner(args);
            if (args.Length > 0) return SelfTest.Run(args);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
