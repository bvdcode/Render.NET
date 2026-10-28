using System;
using System.IO;

namespace Render.NET.Jbig2
{
    internal static class BitmapDecoder
    {
        internal static (int X, int Y)[] Adaptive(BitReader input, int count)
        {
            (int X, int Y)[]? points = new (int X, int Y)[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = (input.SignedByte(), input.SignedByte());
            }

            return points;
        }

        internal static (int X, int Y)[] Template(int template, (int X, int Y)[] at)
        {
            foreach ((int X, int Y) point in at)
            {
                if (point.Y > 0 || (point.Y == 0 && point.X >= 0))
                {
                    throw new InvalidDataException("Non-causal JBIG2 adaptive pixel.");
                }
            }

            return template switch
            {
                0 => new[]
                {
                    at[3],
                    (-1, -2),
                    (0, -2),
                    (1, -2),
                    at[2],
                    at[1],
                    (-2, -1),
                    (-1, -1),
                    (0, -1),
                    (1, -1),
                    (2, -1),
                    at[0],
                    (-4, 0),
                    (-3, 0),
                    (-2, 0),
                    (-1, 0)
                },
                1 => new[]
                {
                    (-1, -2),
                    (0, -2),
                    (1, -2),
                    (2, -2),
                    (-2, -1),
                    (-1, -1),
                    (0, -1),
                    (1, -1),
                    (2, -1),
                    at[0],
                    (-3, 0),
                    (-2, 0),
                    (-1, 0)
                },
                2 => new[]
                {
                    (-1, -2),
                    (0, -2),
                    (1, -2),
                    (-2, -1),
                    (-1, -1),
                    (0, -1),
                    (1, -1),
                    at[0],
                    (-2, 0),
                    (-1, 0)
                },
                3 => new[]
                {
                    (-3, -1),
                    (-2, -1),
                    (-1, -1),
                    (0, -1),
                    (1, -1),
                    at[0],
                    (-4, 0),
                    (-3, 0),
                    (-2, 0),
                    (-1, 0)
                },
                _ => throw new InvalidDataException("Invalid JBIG2 template.")
            };
        }

        internal static Bitmap Generic(int width, int height, Arithmetic arithmetic, int template, (int X, int Y)[] at, bool typical, byte[] contexts, DecodeBudget budget, Bitmap? skip = null)
        {
            Bitmap? result = new Bitmap(width, height, budget);
            (int X, int Y)[]? points = Template(template, at);
            int reused = template switch
            {
                0 => 0x9b25,
                1 => 0x795,
                2 => 0xe5,
                _ => 0x195
            }, ltp = 0;
            budget.Work((long)width * height);
            for (int y = 0; y < height; y++)
            {
                budget.Cancellation.ThrowIfCancellationRequested();
                if (typical)
                {
                    ltp ^= arithmetic.Decoder.Read(contexts, reused);
                }

                if (ltp != 0)
                {
                    result.CopyRow(y);
                    continue;
                }

                for (int x = 0; x < width; x++)
                {
                    if (skip?.Get(x, y) == 1)
                    {
                        continue;
                    }

                    int context = Context(result, x, y, points);
                    result.Set(x, y, arithmetic.Decoder.Read(contexts, context));
                }
            }

            return result;
        }

        private static int Context(Bitmap bitmap, int x, int y, (int X, int Y)[] points)
        {
            int context = 0;
            foreach ((int X, int Y) p in points)
            {
                context = (context << 1) | bitmap.Get(x + p.X, y + p.Y);
            }

            return context;
        }

        internal static Bitmap Refine(int width, int height, Arithmetic arithmetic, int template, (int X, int Y)[] at, bool typical, byte[] contexts, Bitmap reference, int dx, int dy, DecodeBudget budget)
        {
            Bitmap? result = new Bitmap(width, height, budget);
            (int, int)[]? output = template == 0 ? new[]
            {
                at[0],
                (0, -1),
                (1, -1),
                (-1, 0)
            }

            : new[]
            {
                (-1, -1),
                (0, -1),
                (1, -1),
                (-1, 0)
            };
            if (output[0].Item2 > 0 || (output[0].Item2 == 0 && output[0].Item1 >= 0))
            {
                throw new InvalidDataException("Non-causal JBIG2 refinement pixel.");
            }

            (int, int)[]? source = template == 0 ? new[]
            {
                at[1],
                (0, -1),
                (1, -1),
                (-1, 0),
                (0, 0),
                (1, 0),
                (-1, 1),
                (0, 1),
                (1, 1)
            }

            : new[]
            {
                (0, -1),
                (-1, 0),
                (0, 0),
                (1, 0),
                (0, 1),
                (1, 1)
            };
            int ltp = 0;
            budget.Work((long)width * height);
            for (int y = 0; y < height; y++)
            {
                budget.Cancellation.ThrowIfCancellationRequested();
                if (typical)
                {
                    ltp ^= arithmetic.Decoder.Read(contexts, template == 0 ? 0x10 : 0x8);
                }

                for (int x = 0; x < width; x++)
                {
                    int pixel = RefinedPixel(arithmetic, contexts, result, reference, output, source, x, y, dx, dy, ltp);
                    result.Set(x, y, pixel);
                }
            }

            return result;
        }

        private static int RefinedPixel(Arithmetic arithmetic, byte[] contexts, Bitmap result, Bitmap reference, (int, int)[] output, (int, int)[] source, int x, int y, int dx, int dy, int ltp)
        {
            long rx = (long)x - dx, ry = (long)y - dy;
            int context = Context(result, x, y, output) << source.Length;
            if (rx >= int.MinValue + 128 && rx <= int.MaxValue - 128 && ry >= int.MinValue + 128 && ry <= int.MaxValue - 128)
            {
                int xx = (int)rx, yy = (int)ry;
                if (ltp != 0 && Uniform(reference, xx, yy))
                {
                    return reference.Get(xx, yy);
                }

                context |= Context(reference, xx, yy, source);
            }

            return arithmetic.Decoder.Read(contexts, context);
        }

        private static bool Uniform(Bitmap bitmap, int x, int y)
        {
            int pixel = bitmap.Get(x, y);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (bitmap.Get(x + dx, y + dy) != pixel)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
