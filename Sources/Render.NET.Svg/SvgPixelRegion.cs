using System;
using System.Numerics;
using System.Threading;

namespace Render.NET.Svg
{
    internal readonly record struct SvgPixelRegion(int Left, int Top, int Right, int Bottom)
    {
        internal int Width => Right - Left;
        internal int Height => Bottom - Top;
        internal bool Empty => Width == 0 || Height == 0;

        internal static SvgPixelRegion AlphaBounds(PixelBuffer image, CancellationToken token)
        {
            int left = image.Width, top = image.Height, right = 0, bottom = 0;
            ReadOnlySpan<byte> pixels = image.Pixels.Span;
            for (int y = 0; y < image.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                int row = (y * image.Width * 4) + 3;
                for (int x = 0; x < image.Width; x++)
                {
                    if (pixels[row + (x * 4)] != 0)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x + 1);
                        top = Math.Min(top, y);
                        bottom = y + 1;
                    }
                }
            }

            return right == 0 ? default : new SvgPixelRegion(left, top, right, bottom);
        }

        internal SvgPixelRegion Expand(Vector2 extent, int width, int height) => new(
            (int)Math.Clamp(MathF.Floor(Left - extent.X - 1), 0, width),
            (int)Math.Clamp(MathF.Floor(Top - extent.Y - 1), 0, height),
            (int)Math.Clamp(MathF.Ceiling(Right + extent.X + 1), 0, width),
            (int)Math.Clamp(MathF.Ceiling(Bottom + extent.Y + 1), 0, height));
    }
}
