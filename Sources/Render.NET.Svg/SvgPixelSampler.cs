using System;
using System.Numerics;

namespace Render.NET.Svg
{
    internal class SvgPixelSampler(PixelBuffer image, SvgPixelRegion region, bool linear, string edge)
    {
        internal Vector4 Sample(float x, float y)
        {
            int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
            float fx = x - ix, fy = y - iy;
            if (fx == 0 && fy == 0)
            {
                return Pixel(ix, iy);
            }

            return Vector4.Lerp(Vector4.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), fx), Vector4.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), fx), fy);
        }

        private Vector4 Pixel(int x, int y)
        {
            if (edge == "duplicate")
            {
                x = Math.Clamp(x, 0, image.Width - 1);
                y = Math.Clamp(y, 0, image.Height - 1);
            }
            else if (edge == "wrap")
            {
                x = ((x % image.Width) + image.Width) % image.Width;
                y = ((y % image.Height) + image.Height) % image.Height;
            }

            if (x < region.Left || x >= region.Right || y < region.Top || y >= region.Bottom)
            {
                return Vector4.Zero;
            }

            return SvgPixels.Read(image.Pixels.Span, ((y * image.Width) + x) * 4, linear);
        }
    }
}
