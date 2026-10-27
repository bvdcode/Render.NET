using System;
using System.Numerics;

namespace Render.NET.Svg
{
    internal sealed record SvgRegion(string X, string Y, string Width, string Height, bool ObjectBox, float ViewWidth, float ViewHeight)
    {
        internal VectorPath Path((float X, float Y, float Width, float Height) box)
        {
            float x = SvgLength.Parse(X, ObjectBox ? 1 : ViewWidth), y = SvgLength.Parse(Y, ObjectBox ? 1 : ViewHeight);
            float width = SvgLength.Parse(Width, ObjectBox ? 1 : ViewWidth), height = SvgLength.Parse(Height, ObjectBox ? 1 : ViewHeight);
            if (ObjectBox)
            {
                x = box.X + (x * box.Width);
                y = box.Y + (y * box.Height);
                width *= box.Width;
                height *= box.Height;
            }

            return width <= 0 || height <= 0 ? new VectorPath() : new VectorPath().Rectangle(x, y, width, height);
        }
    }

    internal sealed record SvgMask(SvgNode Content, SvgRegion Region, bool ContentBox, bool Luminance, bool Linear)
    {
        internal void Apply(PixelBuffer source, (float X, float Y, float Width, float Height) box, Matrix3x2 matrix, SvgRenderContext context)
        {
            using IDisposable reservation = context.Reserve(source.Width, source.Height, 1);
            var mask = new PixelBuffer(source.Width, source.Height);
            var canvas = new RasterCanvas(mask);
            canvas.ClipPath(Region.Path(box), matrix, cancellationToken: context.Token);
            Matrix3x2 content = ContentBox ? Matrix3x2.CreateScale(box.Width, box.Height) * Matrix3x2.CreateTranslation(box.X, box.Y) * matrix : matrix;
            Content.Draw(canvas, content, context);
            Span<byte> pixels = source.Pixels.Span;
            ReadOnlySpan<byte> values = mask.Pixels.Span;
            for (int y = 0; y < source.Height; y++)
            {
                context.Token.ThrowIfCancellationRequested();
                for (int x = 0; x < source.Width; x++)
                {
                    int i = ((y * source.Width) + x) * 4;
                    float alpha = values[i + 3] / 255f;
                    if (Luminance)
                    {
                        Vector4 c = SvgPixels.Read(values, i, Linear, false);
                        alpha *= (0.2126f * c.X) + (0.7152f * c.Y) + (0.0722f * c.Z);
                    }

                    pixels[i + 3] = (byte)MathF.Round(pixels[i + 3] * alpha);
                }
            }
        }
    }
}
