using System;
using System.IO;
using DlcvDemo;
using OpenCvSharp;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0) return CheckManifest(args[0], args.Length > 1 && args[1] == "--baseline");
        string directory = Path.Combine(Path.GetTempPath(), "demo-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckJpeg(directory, MatType.CV_8UC3);
            CheckJpeg(directory, MatType.CV_8UC1);
            CheckPng(directory, MatType.CV_8UC4, new Scalar(20, 40, 60, 80));
            CheckPng(directory, MatType.CV_16UC1, new Scalar(40000));
            CheckPng(directory, MatType.CV_16UC3, new Scalar(10000, 20000, 40000));
            Console.WriteLine("PASS: JPEG orientations 1-8, grayscale, renamed/copy/replaced files, PNG alpha and 16-bit pixels");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static int CheckManifest(string manifest, bool baseline)
    {
        int failures = 0;
        foreach (string line in File.ReadAllLines(manifest))
        {
            string[] fields = line.Split('\t');
            try
            {
                using (var actual = baseline ? Cv2.ImDecode(File.ReadAllBytes(fields[1]), ImreadModes.Unchanged)
                    : ImageFileReader.Read(fields[1]))
                using (var expected = Cv2.ImRead(fields[2], ImreadModes.Unchanged))
                {
                    if (actual.Empty() || actual.Type() != expected.Type() || actual.Size() != expected.Size()
                        || Cv2.Norm(actual, expected, NormTypes.INF) > int.Parse(fields[3]))
                        throw new InvalidOperationException("pixel/type/orientation mismatch: " + actual.Size() + " " + actual.Type());
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

    private static void CheckJpeg(string directory, MatType type)
    {
        using (var source = new Mat(24, 40, type))
        {
            for (int y = 0; y < source.Rows; y++)
                for (int x = 0; x < source.Cols; x++)
                    if (type == MatType.CV_8UC1)
                        source.Set(y, x, (byte)(x * 4 + y * 2));
                    else
                        source.Set(y, x, new Vec3b((byte)(x * 4), (byte)(y * 8), (byte)(x + y)));
            byte[] jpeg = source.ToBytes(".jpg");
            using (var raw = Cv2.ImDecode(jpeg, ImreadModes.Unchanged))
            {
                for (byte orientation = 1; orientation <= 8; orientation++)
                {
                    string path = Path.Combine(directory, "旋转图像.dat");
                    File.WriteAllBytes(path, AddOrientation(jpeg, orientation));
                    using (var actual = ImageFileReader.Read(path))
                    using (var expected = new Mat(orientation >= 5 ? raw.Cols : raw.Rows,
                        orientation >= 5 ? raw.Rows : raw.Cols, raw.Type()))
                    {
                        for (int y = 0; y < raw.Rows; y++)
                            for (int x = 0; x < raw.Cols; x++)
                            {
                                int dx = x, dy = y;
                                switch (orientation)
                                {
                                    case 2: dx = raw.Cols - 1 - x; break;
                                    case 3: dx = raw.Cols - 1 - x; dy = raw.Rows - 1 - y; break;
                                    case 4: dy = raw.Rows - 1 - y; break;
                                    case 5: dx = y; dy = x; break;
                                    case 6: dx = raw.Rows - 1 - y; dy = x; break;
                                    case 7: dx = raw.Rows - 1 - y; dy = raw.Cols - 1 - x; break;
                                    case 8: dx = y; dy = raw.Cols - 1 - x; break;
                                }
                                if (type == MatType.CV_8UC1)
                                    expected.Set(dy, dx, raw.At<byte>(y, x));
                                else
                                    expected.Set(dy, dx, raw.At<Vec3b>(y, x));
                            }
                        Equal(actual, expected, "EXIF " + orientation + " " + type);
                        string copy = Path.Combine(directory, "副本.jpg");
                        File.Copy(path, copy, true);
                        using (var copied = ImageFileReader.Read(copy))
                            Equal(copied, expected, "copy");
                    }
                }
                string plain = Path.Combine(directory, "无方向.jpg");
                File.WriteAllBytes(plain, jpeg);
                using (var decoded = ImageFileReader.Read(plain)) Equal(decoded, raw, "no EXIF");
            }
        }
    }

    private static byte[] AddOrientation(byte[] jpeg, byte orientation)
    {
        // JPEG APP1 contains a little-endian TIFF IFD with one SHORT Orientation tag.
        byte[] app1 = { 0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0,
            73, 73, 42, 0, 8, 0, 0, 0, 1, 0,
            0x12, 1, 3, 0, 1, 0, 0, 0, orientation, 0, 0, 0, 0, 0, 0, 0 };
        using (var stream = new MemoryStream())
        {
            stream.Write(jpeg, 0, 2);
            stream.Write(app1, 0, app1.Length);
            stream.Write(jpeg, 2, jpeg.Length - 2);
            return stream.ToArray();
        }
    }

    private static void CheckPng(string directory, MatType type, Scalar color)
    {
        string path = Path.Combine(directory, "原格式.png");
        using (var expected = new Mat(13, 21, type, color))
        {
            File.WriteAllBytes(path, expected.ToBytes(".png"));
            using (var actual = ImageFileReader.Read(path)) Equal(actual, expected, "PNG " + type);
        }
    }

    private static void Equal(Mat actual, Mat expected, string name)
    {
        if (actual.Type() != expected.Type() || actual.Size() != expected.Size()
            || Cv2.Norm(actual, expected, NormTypes.INF) != 0)
            throw new InvalidOperationException(name + ": decoded pixels do not match expected orientation/type");
    }
}
