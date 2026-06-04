using System;
using System.IO;
using System.Windows.Forms;

namespace DlcvDemo2
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 4)
            {
                // 命令行模式
                RunCommandLine(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        private static void RunCommandLine(string[] args)
        {
            string extractModelPath = args[0];
            string componentModelPath = args[1];
            string icModelPath = args[2];
            string imagePath = args[3];
            float threshold = 0.3f;
            if (args.Length >= 5 && !float.TryParse(args[4], out threshold))
            {
                threshold = 0.3f;
            }

            foreach (var path in new[] { extractModelPath, componentModelPath, icModelPath, imagePath })
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine($"文件不存在: {path}");
                    Environment.Exit(1);
                    return;
                }
            }

            try
            {
                using (var form = new Form1())
                {
                    string result = form.RunHeadless(extractModelPath, componentModelPath, icModelPath, imagePath, threshold);
                    Console.WriteLine(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"推理失败: {ex}");
                Environment.Exit(1);
            }
        }
    }
}
