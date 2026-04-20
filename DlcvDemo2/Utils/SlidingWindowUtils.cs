using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace DlcvDemo2
{
    internal static class SlidingWindowUtils
    {
        public static List<Rect> BuildSlidingWindows(
            int imageWidth,
            int imageHeight,
            int configuredWindowWidth,
            int configuredWindowHeight,
            int overlapX,
            int overlapY)
        {
            var windows = new List<Rect>();
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                return windows;
            }

            int windowW = Math.Min(Math.Max(1, configuredWindowWidth), Math.Max(1, imageWidth));
            int windowH = Math.Min(Math.Max(1, configuredWindowHeight), Math.Max(1, imageHeight));

            List<int> xs = BuildStartPositions(imageWidth, windowW, overlapX);
            List<int> ys = BuildStartPositions(imageHeight, windowH, overlapY);

            windows = new List<Rect>(xs.Count * ys.Count);
            foreach (int y in ys)
            {
                foreach (int x in xs)
                {
                    windows.Add(new Rect(x, y, windowW, windowH));
                }
            }

            return windows;
        }

        private static List<int> BuildStartPositions(int totalSize, int windowSize, int overlap)
        {
            var positions = new List<int>();
            if (totalSize <= 0 || windowSize <= 0)
            {
                return positions;
            }

            if (windowSize >= totalSize)
            {
                positions.Add(0);
                return positions;
            }

            int step = Math.Max(1, windowSize - Math.Max(0, overlap));
            int current = 0;
            while (true)
            {
                if (current + windowSize >= totalSize)
                {
                    int tail = totalSize - windowSize;
                    if (positions.Count == 0 || positions[positions.Count - 1] != tail)
                    {
                        positions.Add(tail);
                    }
                    break;
                }

                positions.Add(current);
                current += step;
            }

            return positions;
        }
    }
}
