using System;
using System.IO;
using DlcvDemo;
using OpenCvSharp;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: DemoImageLoadingTest.exe <cases.tsv> [--baseline]");
            return 2;
        }
        bool baseline = args.Length > 1 && args[1] == "--baseline";
        string copy = Path.GetTempFileName();
        int failures = 0;
        try
        {
            foreach (string line in File.ReadLines(args[0]))
            {
                string[] fields = line.Split('\t');
                try
                {
                    using (var expected = Cv2.ImRead(fields[2], ImreadModes.Unchanged))
                    {
                        CheckPixels(fields[1], expected, int.Parse(fields[3]), baseline);
                        // Reuse a renamed copy to check both file-name independence and same-path updates.
                        File.Copy(fields[1], copy, true);
                        CheckPixels(copy, expected, int.Parse(fields[3]), baseline);
                    }
                    Console.WriteLine("PASS\t" + fields[0]);
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine("FAIL\t" + fields[0] + "\t" + ex.Message.Replace('\n', ' '));
                }
            }
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            File.Delete(copy);
        }
    }

    private static void CheckPixels(string path, Mat expected, int tolerance, bool baseline)
    {
        using (var actual = baseline ? Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Unchanged)
            : ImageFileReader.Read(path))
            if (actual.Empty() || actual.Type() != expected.Type() || actual.Size() != expected.Size()
                || Cv2.Norm(actual, expected, NormTypes.INF) > tolerance)
                throw new InvalidOperationException("pixel/type/orientation mismatch: " + actual.Size() + " " + actual.Type());
    }
}
