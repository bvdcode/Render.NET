using System;
using System.IO;

namespace Render.NET.Jbig2
{
    internal class Bitmap
    {
        internal Bitmap(int width, int height, DecodeBudget budget, int fill = 0)
        {
            if (width < 0 || height < 0 || (long)width * height > budget.Options.MaximumPixels)
            {
                throw new InvalidDataException("JBIG2 bitmap exceeds MaximumPixels.");
            }

            Width = width;
            Height = height;
            Stride = checked((width + 7) / 8);
            int size = checked(Stride * height);
            budget.Allocate(size);
            Data = new byte[size];
            if (fill != 0)
            {
                Array.Fill(Data, (byte)255);
                ClearPadding();
            }
        }

        internal int Width { get; }
        internal int Height { get; }
        internal int Stride { get; }
        internal byte[] Data { get; }

        internal int Get(int x, int y) => (uint)x < Width && (uint)y < Height ? (Data[(y * Stride) + (x >> 3)] >> (7 - (x & 7))) & 1 : 0;
        internal void Set(int x, int y, int value)
        {
            int index = (y * Stride) + (x >> 3), mask = 128 >> (x & 7);
            Data[index] = (byte)((Data[index] & ~mask) | (value == 0 ? 0 : mask));
        }

        internal void CopyRow(int y)
        {
            if (y > 0)
            {
                Data.AsSpan((y - 1) * Stride, Stride).CopyTo(Data.AsSpan(y * Stride, Stride));
            }
        }

        internal void ClearPadding()
        {
            if ((Width & 7) == 0)
            {
                return;
            }

            for (int y = 0; y < Height; y++)
            {
                Data[((y + 1) * Stride) - 1] &= (byte)(255 << (8 - (Width & 7)));
            }
        }

        internal void Compose(Bitmap source, long x, long y, int op, DecodeBudget budget)
        {
            if ((uint)op > 4)
            {
                throw new InvalidDataException("Invalid JBIG2 combination operator.");
            }

            if (x >= Width || y >= Height || x + source.Width <= 0 || y + source.Height <= 0)
            {
                return;
            }

            int left = (int)Math.Max(0, x), top = (int)Math.Max(0, y);
            long right = Math.Min(Width, x + source.Width), bottom = Math.Min(Height, y + source.Height);
            if (left >= right || top >= bottom)
            {
                return;
            }

            budget.Work((right - left) * (bottom - top));
            for (int row = top; row < bottom; row++)
            {
                budget.Cancellation.ThrowIfCancellationRequested();
                for (int col = left; col < right; col++)
                {
                    int a = Get(col, row), b = source.Get((int)(col - x), (int)(row - y));
                    int value = op switch
                    {
                        0 => a | b,
                        1 => a & b,
                        2 => a ^ b,
                        3 => 1 ^ a ^ b,
                        _ => b
                    };
                    Set(col, row, value);
                }
            }
        }

        internal Bitmap Slice(int x, int width, DecodeBudget budget)
        {
            Bitmap? result = new Bitmap(width, Height, budget);
            result.Compose(this, -x, 0, 4, budget);
            return result;
        }
    }
}
