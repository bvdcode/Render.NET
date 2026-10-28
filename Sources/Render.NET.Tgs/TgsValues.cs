using System;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace Render.NET.Tgs
{
    internal static class TgsValues
    {
        internal static TgsKeyPair Keys(JsonElement property, double frame)
        {
            JsonElement k = TgsJson.Get(property, "k");
            if (k.ValueKind == JsonValueKind.Undefined)
            {
                k = property;
            }

            if (k.ValueKind != JsonValueKind.Array || k.GetArrayLength() == 0 || TgsJson.Get(k[0], "t").ValueKind != JsonValueKind.Number)
            {
                return new TgsKeyPair(k, k, default, 0);
            }

            int i = 0;
            while (i + 1 < k.GetArrayLength() && TgsJson.Number(k[i + 1], "t") <= frame)
            {
                i++;
            }

            return KeyPair(k, i, frame);
        }

        private static TgsKeyPair KeyPair(JsonElement k, int i, double frame)
        {
            JsonElement current = k[i];
            JsonElement start = TgsJson.Get(current, "s");
            if (start.ValueKind == JsonValueKind.Undefined && i > 0)
            {
                start = TgsJson.Get(k[i - 1], "e");
            }

            JsonElement end = TgsJson.Get(current, "e");
            if (i + 1 < k.GetArrayLength() && end.ValueKind == JsonValueKind.Undefined)
            {
                end = TgsJson.Get(k[i + 1], "s");
            }

            double duration = i + 1 < k.GetArrayLength() ? TgsJson.Number(k[i + 1], "t") - TgsJson.Number(current, "t") : 0;
            double progress = duration <= 0 || TgsJson.Flag(current, "h") ? 0 : Math.Clamp((frame - TgsJson.Number(current, "t")) / duration, 0, 1);
            return new TgsKeyPair(start, end.ValueKind == JsonValueKind.Undefined ? start : end, current, progress);
        }

        internal static double[] Vector(JsonElement property, double frame, params double[] fallback)
        {
            if (property.ValueKind == JsonValueKind.Undefined)
            {
                return fallback;
            }

            if (TgsJson.Flag(property, "s"))
            {
                return new[]
                {
                    Scalar(TgsJson.Get(property, "x"), frame),
                    Scalar(TgsJson.Get(property, "y"), frame)
                };
            }

            TgsKeyPair pair = Keys(property, frame);
            double[] a = Numbers(pair.Start), b = Numbers(pair.End);
            if (a.Length == 0)
            {
                return fallback;
            }

            if (b.Length != a.Length)
            {
                throw new InvalidDataException("Mismatched Lottie keyframe dimensions.");
            }

            double[]? result = new double[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                result[i] = a[i] + ((b[i] - a[i]) * Ease(pair.Key, pair.Progress, i));
            }

            if (a.Length >= 2 && TgsJson.Get(pair.Key, "to").ValueKind == JsonValueKind.Array)
            {
                Spatial(pair, a, b, result);
            }

            return result;
        }

        internal static double Scalar(JsonElement property, double frame, double fallback = 0)
        {
            double[] v = Vector(property, frame, fallback);
            return v.Length == 0 ? fallback : v[0];
        }

        internal static Vector2 Point(JsonElement property, double frame, float x = 0, float y = 0)
        {
            double[] v = Vector(property, frame, x, y);
            if (v.Length < 2 || Math.Abs(v[0]) > float.MaxValue || Math.Abs(v[1]) > float.MaxValue)
            {
                throw new InvalidDataException("Invalid Lottie point.");
            }

            return new Vector2((float)v[0], (float)v[1]);
        }

        internal static double[] Numbers(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return new[]
                {
                    TgsJson.Number(value)
                };
            }

            JsonElement[] items = TgsJson.Array(value);
            double[]? result = new double[items.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = TgsJson.Number(items[i]);
            }

            return result;
        }

        internal static double Ease(JsonElement key, double progress, int dimension = 0)
        {
            JsonElement outgoing = TgsJson.Get(key, "o"), incoming = TgsJson.Get(key, "i");
            if (outgoing.ValueKind != JsonValueKind.Object || incoming.ValueKind != JsonValueKind.Object)
            {
                return progress;
            }

            double x1 = Math.Clamp(Component(TgsJson.Get(outgoing, "x"), dimension), 0, 1), y1 = Component(TgsJson.Get(outgoing, "y"), dimension);
            double x2 = Math.Clamp(Component(TgsJson.Get(incoming, "x"), dimension), 0, 1), y2 = Component(TgsJson.Get(incoming, "y"), dimension);
            double low = 0, high = 1;
            for (int j = 0; j < 28; j++)
            {
                double t = (low + high) / 2;
                if (Cubic(0, x1, x2, 1, t) < progress)
                {
                    low = t;
                }
                else
                {
                    high = t;
                }
            }

            return Cubic(0, y1, y2, 1, (low + high) / 2);
        }

        private static double Component(JsonElement e, int i) => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0 ? TgsJson.Number(e[Math.Min(i, e.GetArrayLength() - 1)]) : TgsJson.Number(e);
        private static void Spatial(TgsKeyPair pair, double[] a, double[] b, double[] result)
        {
            double[] outgoing = Numbers(TgsJson.Get(pair.Key, "to")), incoming = Numbers(TgsJson.Get(pair.Key, "ti"));
            if (outgoing.Length < 2 || incoming.Length < 2)
            {
                throw new InvalidDataException("Invalid Lottie spatial tangents.");
            }

            double[] lengths = new double[65];
            double px = a[0], py = a[1];
            for (int i = 1; i <= 64; i++)
            {
                double t = i / 64d;
                double x = Cubic(a[0], a[0] + outgoing[0], b[0] + incoming[0], b[0], t);
                double y = Cubic(a[1], a[1] + outgoing[1], b[1] + incoming[1], b[1], t);
                lengths[i] = lengths[i - 1] + Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py)));
                px = x;
                py = y;
            }

            double distance = Math.Clamp(Ease(pair.Key, pair.Progress), 0, 1) * lengths[64];
            int next = 1;
            while (next < 64 && lengths[next] < distance)
            {
                next++;
            }

            double span = lengths[next] - lengths[next - 1];
            double parameter = ((next - 1) + (span == 0 ? 0 : (distance - lengths[next - 1]) / span)) / 64;
            for (int i = 0; i < 2; i++)
            {
                result[i] = Cubic(a[i], a[i] + outgoing[i], b[i] + incoming[i], b[i], parameter);
            }
        }

        private static double Cubic(double a, double b, double c, double d, double t)
        {
            double u = 1 - t;
            return (u * u * u * a) + (3 * u * u * t * b) + (3 * u * t * t * c) + (t * t * t * d);
        }
    }
}
