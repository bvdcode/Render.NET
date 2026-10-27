using System;
using System.Numerics;
using System.Threading;

namespace Render.NET.Svg
{
    internal static class SvgPixels
    {
        internal static Vector4 Read(ReadOnlySpan<byte> data, int offset, bool linear = false, bool premultiplied = true)
        {
            float alpha = data[offset + 3] / 255f;
            var color = new Vector4(data[offset] / 255f, data[offset + 1] / 255f, data[offset + 2] / 255f, alpha);
            if (linear)
            {
                color.X = Linear(color.X);
                color.Y = Linear(color.Y);
                color.Z = Linear(color.Z);
            }

            if (premultiplied)
            {
                color.X *= alpha;
                color.Y *= alpha;
                color.Z *= alpha;
            }

            return color;
        }

        internal static void Write(Span<byte> data, int offset, Vector4 color, bool linear = false, bool premultiplied = true)
        {
            color.W = Math.Clamp(color.W, 0, 1);
            if (premultiplied)
            {
                color = color.W > 0 ? new Vector4(new Vector3(color.X, color.Y, color.Z) / color.W, color.W) : Vector4.Zero;
            }

            if (linear)
            {
                color.X = Srgb(color.X);
                color.Y = Srgb(color.Y);
                color.Z = Srgb(color.Z);
            }

            data[offset] = PixelCompositor.Byte(color.X);
            data[offset + 1] = PixelCompositor.Byte(color.Y);
            data[offset + 2] = PixelCompositor.Byte(color.Z);
            data[offset + 3] = PixelCompositor.Byte(color.W);
        }

        internal static float Linear(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        internal static float Srgb(float value) => value <= 0.0031308f ? 12.92f * value : (1.055f * MathF.Pow(value, 1 / 2.4f)) - 0.055f;
        internal static Vector4 Sample(PixelBuffer image, float x, float y, bool linear, string edge = "none")
        {
            int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
            float fx = x - ix, fy = y - iy;
            if (fx == 0 && fy == 0)
            {
                return At(image, ix, iy, linear, edge);
            }

            return Vector4.Lerp(Vector4.Lerp(At(image, ix, iy, linear, edge), At(image, ix + 1, iy, linear, edge), fx), Vector4.Lerp(At(image, ix, iy + 1, linear, edge), At(image, ix + 1, iy + 1, linear, edge), fx), fy);
        }

        internal static Vector4 At(PixelBuffer image, int x, int y, bool linear, string edge = "none", bool premultiplied = true)
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
            else if ((uint)x >= image.Width || (uint)y >= image.Height)
            {
                return Vector4.Zero;
            }

            return Read(image.Pixels.Span, ((y * image.Width) + x) * 4, linear, premultiplied);
        }

        internal static PixelBuffer Map(PixelBuffer size, Func<int, int, Vector4> sample, bool linear, CancellationToken token, bool premultiplied = true)
        {
            var output = new PixelBuffer(size.Width, size.Height);
            for (int y = 0; y < output.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < output.Width; x++)
                {
                    Write(output.Pixels.Span, ((y * output.Width) + x) * 4, sample(x, y), linear, premultiplied);
                }
            }

            return output;
        }

        internal static PixelBuffer Copy(PixelBuffer image)
        {
            var result = new PixelBuffer(image.Width, image.Height);
            image.Pixels.CopyTo(result.Pixels);
            return result;
        }
    }
}
