using System;

namespace DlcvModules
{
    internal static class DpmMath
    {
        public static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);
        public static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
        public static double Clamp(double value, double min, double max) => value < min ? min : (value > max ? max : value);
    }

    internal static class DpmMathF
    {
        public static float Abs(float value) => Math.Abs(value);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Sqrt(float value) => (float)Math.Sqrt(value);
        public static float Round(float value) => (float)Math.Round(value);
        public static float Floor(float value) => (float)Math.Floor(value);
    }

    internal static class DpmBitOps
    {
        public static int PopCount(ulong value)
        {
            value -= (value >> 1) & 0x5555555555555555UL;
            value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
            value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((value * 0x0101010101010101UL) >> 56);
        }
    }
}
