using System;

namespace MCG_Doom.Compatibility
{
    internal static class ManagedDoomNet472Compat
    {
        public const float PI = (float)Math.PI;
        public const float E = (float)Math.E;

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

        public static float Abs(float value) => (float)Math.Abs(value);
        public static float Acos(float value) => (float)Math.Acos(value);
        public static float Asin(float value) => (float)Math.Asin(value);
        public static float Atan(float value) => (float)Math.Atan(value);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Ceiling(float value) => (float)Math.Ceiling(value);
        public static float Cos(float value) => (float)Math.Cos(value);
        public static float Cosh(float value) => (float)Math.Cosh(value);
        public static float Exp(float value) => (float)Math.Exp(value);
        public static float Floor(float value) => (float)Math.Floor(value);
        public static float IEEERemainder(float x, float y) => (float)Math.IEEERemainder(x, y);
        public static float Log(float value) => (float)Math.Log(value);
        public static float Log(float value, float newBase) => (float)Math.Log(value, newBase);
        public static float Log10(float value) => (float)Math.Log10(value);
        public static float Max(float left, float right) => Math.Max(left, right);
        public static float Min(float left, float right) => Math.Min(left, right);
        public static float Pow(float x, float y) => (float)Math.Pow(x, y);
        public static float Round(float value) => (float)Math.Round(value);
        public static float Round(float value, int digits) => (float)Math.Round(value, digits);
        public static float Round(float value, MidpointRounding mode) => (float)Math.Round(value, mode);
        public static float Round(float value, int digits, MidpointRounding mode) => (float)Math.Round(value, digits, mode);
        public static int Sign(float value) => Math.Sign(value);
        public static float Sin(float value) => (float)Math.Sin(value);
        public static float Sinh(float value) => (float)Math.Sinh(value);
        public static float Sqrt(float value) => (float)Math.Sqrt(value);
        public static float Tan(float value) => (float)Math.Tan(value);
        public static float Tanh(float value) => (float)Math.Tanh(value);
        public static float Truncate(float value) => (float)Math.Truncate(value);
    }
}

namespace System.Collections.Generic
{
    internal static class ManagedDoomDictionaryCompatExtensions
    {
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key))
            {
                return false;
            }

            dictionary.Add(key, value);
            return true;
        }
    }
}

namespace System.IO
{
    internal static class ManagedDoomStreamCompatExtensions
    {
        public static void ReadExactly(this Stream stream, byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }
        }
    }
}
