using System;
using System.IO;
using System.Text;

namespace SentinelManagerTest
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                if (args.Length == 0) return BackendTests.Run();
                if (args.Length == 3 && args[0] == "ui-test" && args[1] == "--output-dir")
                {
                    string output = Path.GetFullPath(args[2]);
                    string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!output.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || output.TrimEnd(Path.DirectorySeparatorChar) == temporary.TrimEnd(Path.DirectorySeparatorChar))
                        throw new ArgumentException("输出目录必须是系统临时目录内的专用子目录。");
                    for (var current = new DirectoryInfo(output); current != null; current = current.Parent)
                        if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                            throw new ArgumentException("输出目录不能经过目录链接。");
                    Directory.CreateDirectory(output);
                    return UiTests.Run(output);
                }
                Console.Error.WriteLine("参数：无参数执行后端测试；ui-test --output-dir <系统临时子目录> 执行界面测试。");
                return 2;
            }
            catch (Exception ex) { Console.Error.WriteLine("测试失败：" + ex); return 1; }
        }
    }
}
