using System;
using System.Numerics;
using System.Threading.Tasks;

namespace Render.NET.Svg
{
    internal partial record SvgFilter
    {
        private static PixelBuffer GaussianRotated(PixelBuffer input, Vector2 direction, float[] kernel, bool linear, string edge, SvgRenderContext context)
        {
            SvgPixelRegion source = SvgPixelRegion.AlphaBounds(input, context.Token);
            PixelBuffer result = context.CreateBuffer(input.Width, input.Height);
            if (source.Empty)
            {
                return result;
            }

            int radius = kernel.Length / 2;
            SvgPixelRegion target = edge == "none"
                ? source.Expand(Vector2.Abs(direction) * radius, input.Width, input.Height)
                : new SvgPixelRegion(0, 0, input.Width, input.Height);
            SvgPixelSampler samples = new(input, source, linear, edge);
            Parallel.For(target.Top, target.Bottom, new ParallelOptions { CancellationToken = context.Token }, y =>
            {
                context.Token.ThrowIfCancellationRequested();
                for (int x = target.Left; x < target.Right; x++)
                {
                    if ((x & 63) == 0)
                    {
                        context.Token.ThrowIfCancellationRequested();
                    }

                    int first = -radius, last = radius;
                    if (edge == "none")
                    {
                        IntersectKernel(x, direction.X, source.Left, source.Right, ref first, ref last);
                        IntersectKernel(y, direction.Y, source.Top, source.Bottom, ref first, ref last);
                    }

                    Vector4 value = Vector4.Zero;
                    for (int i = first; i <= last; i++)
                    {
                        value += samples.Sample(x + (direction.X * i), y + (direction.Y * i)) * kernel[i + radius];
                    }

                    SvgPixels.Write(result.Pixels.Span, ((y * input.Width) + x) * 4, value, linear);
                }
            });
            return result;
        }

        private static void IntersectKernel(int pixel, float direction, int minimum, int maximum, ref int first, ref int last)
        {
            float a = (minimum - 1f - pixel) / direction, b = (maximum - pixel) / direction;
            first = (int)Math.Clamp(MathF.Floor(Math.Min(a, b)) - 1, first, last + 1);
            last = (int)Math.Clamp(MathF.Ceiling(Math.Max(a, b)) + 1, first - 1, last);
        }
    }
}
