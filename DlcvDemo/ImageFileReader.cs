using System.Drawing;
using System.IO;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace DlcvDemo
{
    internal static class ImageFileReader
    {
        internal static Mat Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            // OpenCV 4.10 has no GIF decoder; use the existing GDI+ dependency for its first frame.
            if (StartsWith(bytes, 0, "GIF87a") || StartsWith(bytes, 0, "GIF89a"))
            {
                using (var stream = new MemoryStream(bytes))
                using (var source = Image.FromStream(stream))
                using (var bitmap = new Bitmap(source))
                    return BitmapConverter.ToMat(bitmap);
            }

            // Unchanged ignores EXIF. JPEG has no alpha; other formats must retain all channels/depth.
            bool isJpeg = bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
            ImreadModes mode = isJpeg
                ? ImreadModes.AnyColor | ImreadModes.AnyDepth
                : ImreadModes.Unchanged;
            Mat image = Cv2.ImDecode(bytes, mode);
            if (image.Empty()) return image;
            try
            {
                // TIFF's decoder already applies orientation, including in Unchanged mode.
                int orientation = ReadChunkOrientation(bytes);
                switch (orientation)
                {
                    case 2: Cv2.Flip(image, image, FlipMode.Y); break;
                    case 3: Cv2.Flip(image, image, FlipMode.XY); break;
                    case 4: Cv2.Flip(image, image, FlipMode.X); break;
                    case 5: Cv2.Transpose(image, image); break;
                    case 6:
                        Cv2.Transpose(image, image);
                        Cv2.Flip(image, image, FlipMode.Y);
                        break;
                    case 7:
                        Cv2.Transpose(image, image);
                        Cv2.Flip(image, image, FlipMode.XY);
                        break;
                    case 8:
                        Cv2.Transpose(image, image);
                        Cv2.Flip(image, image, FlipMode.X);
                        break;
                }
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        private static int ReadChunkOrientation(byte[] bytes)
        {
            bool png = bytes.Length >= 8 && bytes[0] == 137 && StartsWith(bytes, 1, "PNG\r\n\x1a\n");
            bool webp = StartsWith(bytes, 0, "RIFF") && StartsWith(bytes, 8, "WEBP");
            if (!png && !webp) return 1;
            int end = bytes.Length;
            if (webp)
            {
                uint riffSize = ReadUInt32(bytes, 4, true);
                if (riffSize < 4 || riffSize > bytes.Length - 8) return 1;
                end = (int)riffSize + 8;
            }
            int offset = png ? 8 : 12;
            while (offset <= end - (png ? 12 : 8))
            {
                uint length = ReadUInt32(bytes, offset + (png ? 0 : 4), !png);
                int overhead = png ? 12 : 8;
                if (length > end - offset - overhead) return 1;
                int data = offset + 8;
                if (StartsWith(bytes, offset + (png ? 4 : 0), png ? "eXIf" : "EXIF"))
                {
                    int exifLength = (int)length;
                    if (exifLength >= 6 && StartsWith(bytes, data, "Exif\0\0"))
                    {
                        data += 6;
                        exifLength -= 6;
                    }
                    return ReadExifOrientation(bytes, data, exifLength);
                }
                long next = (long)offset + overhead + length + (png ? 0 : length % 2);
                if (next > end) return 1;
                offset = (int)next;
            }
            return 1;
        }

        private static int ReadExifOrientation(byte[] bytes, int start, int length)
        {
            if (length < 8) return 1;
            bool little = StartsWith(bytes, start, "II");
            if (!little && !StartsWith(bytes, start, "MM")) return 1;
            if (ReadUInt16(bytes, start + 2, little) != 42) return 1;
            uint ifd = ReadUInt32(bytes, start + 4, little);
            if (ifd < 8 || ifd > length - 2) return 1;
            int countAt = start + (int)ifd;
            int count = ReadUInt16(bytes, countAt, little);
            int entry = countAt + 2;
            int end = start + length;
            for (int i = 0; i < count && entry <= end - 12; i++, entry += 12)
            {
                if (ReadUInt16(bytes, entry, little) != 274) continue;
                if (ReadUInt16(bytes, entry + 2, little) != 3
                    || ReadUInt32(bytes, entry + 4, little) != 1) return 1;
                int orientation = ReadUInt16(bytes, entry + 8, little);
                return orientation >= 1 && orientation <= 8 ? orientation : 1;
            }
            return 1;
        }

        private static bool StartsWith(byte[] bytes, int offset, string value)
        {
            if (offset > bytes.Length - value.Length) return false;
            for (int i = 0; i < value.Length; i++)
                if (bytes[offset + i] != value[i]) return false;
            return true;
        }

        private static int ReadUInt16(byte[] bytes, int offset, bool little)
        {
            return little ? bytes[offset] | bytes[offset + 1] << 8
                : bytes[offset] << 8 | bytes[offset + 1];
        }

        private static uint ReadUInt32(byte[] bytes, int offset, bool little)
        {
            return little
                ? (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24)
                : (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
        }
    }
}
