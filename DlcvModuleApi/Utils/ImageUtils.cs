using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvModuleApi.Utils
{
    public static class ImageUtils
    {
        public static Mat ImreadAny(string path)
        {
            // 支持中文路径
            var bytes = System.IO.File.ReadAllBytes(path);
            var mat = Mat.ImDecode(bytes, ImreadModes.AnyColor | ImreadModes.AnyDepth);
            if (mat.Type() == MatType.CV_16UC1)
            {
                var dst = new Mat();
                mat.ConvertTo(dst, MatType.CV_8UC1, 1.0 / 257.0);
                mat.Dispose();
                return dst;
            }
            return mat;
        }
    }
}


