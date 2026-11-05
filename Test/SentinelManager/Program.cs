using System;
using System.Windows.Forms;

namespace SentinelManager
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 0) return 2;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SentinelManagerForm(new SentinelClient()));
            return 0;
        }
    }
}
