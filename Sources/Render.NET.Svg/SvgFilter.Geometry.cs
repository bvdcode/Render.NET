using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;
using System.Threading.Tasks;

namespace Render.NET.Svg
{
    internal sealed partial record SvgFilter
    {
        private static PixelBuffer Gaussian(PixelBuffer input, Vector2 sigma, Matrix3x2 units, bool linear, string edge, SvgRenderContext context)
        {
            if (sigma.X < 0 || sigma.Y < 0)
            {
                throw new InvalidDataException("Negative SVG blur deviation.");
            }

            Vector2 x = Vector2.TransformNormal(new Vector2(sigma.X, 0), units), y = Vector2.TransformNormal(new Vector2(0, sigma.Y), units);
            PixelBuffer first = GaussianAxis(input, x, linear, edge, context);
            return GaussianAxis(first, y, linear, edge, context);
        }

        private static PixelBuffer GaussianAxis(PixelBuffer input, Vector2 axis, bool linear, string edge, SvgRenderContext context)
        {
            float sigma = axis.Length();
            if (sigma < .01f)
            {
                return SvgPixels.Copy(input);
            }

            if (sigma > 256)
            {
                throw new InvalidDataException("SVG blur deviation exceeds 256 device samples.");
            }

            int radius = (int)MathF.Ceiling(sigma * 3);
            var kernel = new float[(radius * 2) + 1];
            float total = 0;
            for (int i = -radius; i <= radius; i++)
            {
                float weight = MathF.Exp(-(i * i) / (2 * sigma * sigma));
                kernel[i + radius] = weight;
                total += weight;
            }

            for (int i = 0; i < kernel.Length; i++)
            {
                kernel[i] /= total;
            }

            Vector2 direction = axis / sigma;
            if (axis.X == 0 || axis.Y == 0)
            {
                return GaussianAligned(input, kernel, axis.Y == 0, axis.X + axis.Y < 0, linear, edge, context);
            }

            using IDisposable cacheBudget = context.Reserve(input.Width, input.Height, 4);
            var samples = new Vector4[checked(input.Width * input.Height)];
            for (int y = 0; y < input.Height; y++)
            {
                context.Token.ThrowIfCancellationRequested();
                for (int x = 0; x < input.Width; x++)
                {
                    samples[(y * input.Width) + x] = SvgPixels.Read(input.Pixels.Span, ((y * input.Width) + x) * 4, linear, true);
                }
            }

            var result = new PixelBuffer(input.Width, input.Height);
            Parallel.For(0, input.Height, new ParallelOptions { CancellationToken = context.Token }, y =>
            {
                for (int x = 0; x < input.Width; x++)
                {
                    Vector4 value = Vector4.Zero;
                    for (int i = -radius; i <= radius; i++)
                    {
                        value += CachedSample(samples, input.Width, input.Height, x + (direction.X * i), y + (direction.Y * i), edge) * kernel[i + radius];
                    }

                    SvgPixels.Write(result.Pixels.Span, ((y * input.Width) + x) * 4, value, linear, true);
                }
            });
            return result;
        }

        private static Vector4 CachedSample(Vector4[] data, int width, int height, float x, float y, string edge)
        {
            Vector4 Pixel(int px, int py)
            {
                if (edge == "duplicate")
                {
                    px = Math.Clamp(px, 0, width - 1);
                    py = Math.Clamp(py, 0, height - 1);
                }
                else if (edge == "wrap")
                {
                    px = ((px % width) + width) % width;
                    py = ((py % height) + height) % height;
                }

                return (uint)px < width && (uint)py < height ? data[(py * width) + px] : Vector4.Zero;
            }

            int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
            float fx = x - ix, fy = y - iy;
            if (fx == 0 && fy == 0)
            {
                return Pixel(ix, iy);
            }

            return Vector4.Lerp(Vector4.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), fx), Vector4.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), fx), fy);
        }

        private static PixelBuffer Offset(PixelBuffer input, Vector2 delta, bool linear, SvgRenderContext context) => SvgPixels.Map(input, (x, y) => SvgPixels.Sample(input, x - delta.X, y - delta.Y, linear), linear, context.Token);
        private static PixelBuffer Morphology(XElement p, PixelBuffer input, Matrix3x2 units, bool linear, SvgRenderContext context)
        {
            Vector2 radius = Pair(p, "radius");
            if (radius.X < 0 || radius.Y < 0)
            {
                throw new InvalidDataException("Negative SVG morphology radius.");
            }

            bool dilate = Text(p, "operator", "erode") == "dilate";
            Vector2 xAxis = Vector2.TransformNormal(new Vector2(radius.X, 0), units), yAxis = Vector2.TransformNormal(new Vector2(0, radius.Y), units);
            PixelBuffer first = MorphAxis(input, xAxis, dilate, linear, context);
            return MorphAxis(first, yAxis, dilate, linear, context);
        }

        private static PixelBuffer MorphAxis(PixelBuffer input, Vector2 axis, bool dilate, bool linear, SvgRenderContext context)
        {
            float length = axis.Length();
            if (length > 256)
            {
                throw new InvalidDataException("SVG morphology radius exceeds 256 device samples.");
            }

            int radius = (int)MathF.Floor(length);
            if (axis.X == 0 || axis.Y == 0)
            {
                return MorphAligned(input, radius, axis.Y == 0, axis.X + axis.Y < 0, dilate, linear, context);
            }

            Vector2 direction = length == 0 ? Vector2.Zero : axis / length;
            return SvgPixels.Map(input, (x, y) =>
            {
                Vector4 result = dilate ? Vector4.Zero : Vector4.One;
                for (int i = -radius; i <= radius; i++)
                {
                    Vector4 sample = SvgPixels.Sample(input, x + (direction.X * i), y + (direction.Y * i), linear);
                    result = dilate ? Vector4.Max(result, sample) : Vector4.Min(result, sample);
                }

                return result;
            }, linear, context.Token);
        }

        private static PixelBuffer Convolve(XElement p, PixelBuffer input, Matrix3x2 units, bool linear, SvgRenderContext context)
        {
            var(width, height, kernel) = ConvolutionKernel(p);
            float divisor = Number(p, "divisor", kernel.Sum());
            divisor = divisor == 0 ? 1 : divisor;
            float bias = Number(p, "bias");
            int tx = (int)Number(p, "targetX", width / 2), ty = (int)Number(p, "targetY", height / 2);
            if (tx < 0 || tx >= width || ty < 0 || ty >= height)
            {
                throw new InvalidDataException("Invalid SVG convolution target.");
            }

            bool preserve = Text(p, "preserveAlpha") == "true";
            Vector2 step = Pair(p, "kernelUnitLength", 1);
            Vector2 dx = p.Attribute("kernelUnitLength") == null ? Vector2.UnitX : Vector2.TransformNormal(new Vector2(step.X, 0), units);
            Vector2 dy = p.Attribute("kernelUnitLength") == null ? Vector2.UnitY : Vector2.TransformNormal(new Vector2(0, step.Y), units);
            string edge = Text(p, "edgeMode", "duplicate");
            return SvgPixels.Map(input, (x, y) =>
            {
                Vector4 sum = Vector4.Zero;
                for (int row = 0; row < height; row++)
                {
                    for (int col = 0; col < width; col++)
                    {
                        Vector2 d = (dx * (col - tx)) + (dy * (row - ty));
                        Vector4 sample = SvgPixels.Sample(input, x + d.X, y + d.Y, linear, edge);
                        if (preserve && sample.W > 0)
                        {
                            sample = new Vector4(new Vector3(sample.X, sample.Y, sample.Z) / sample.W, sample.W);
                        }

                        sum += sample * kernel[(((height - 1 - row) * width) + width) - 1 - col];
                    }
                }

                sum = (sum / divisor) + new Vector4(bias);
                if (preserve)
                {
                    sum.W = input[x, y].A / 255f;
                }

                return sum;
            }, linear, context.Token, !preserve);
        }

        private static (int Width, int Height, float[] Kernel) ConvolutionKernel(XElement p)
        {
            Vector2 order = Pair(p, "order", 3);
            int width = (int)order.X, height = (int)order.Y;
            float[] kernel = SvgNumbers.List(Text(p, "kernelMatrix"));
            if (width <= 0 || height <= 0 || width > 31 || height > 31 || width != order.X || height != order.Y || kernel.Length != width * height)
            {
                throw new InvalidDataException("Invalid SVG convolution kernel (maximum 31 x 31).");
            }

            return (width, height, kernel);
        }

        private static PixelBuffer Displace(XElement p, PixelBuffer input, PixelBuffer second, Matrix3x2 units, bool linear, SvgRenderContext context)
        {
            float scale = Number(p, "scale");
            int xc = Channel(Text(p, "xChannelSelector", "A")), yc = Channel(Text(p, "yChannelSelector", "A"));
            return SvgPixels.Map(input, (x, y) =>
            {
                Vector4 control = SvgPixels.At(second, x, y, linear, premultiplied: false);
                Vector2 delta = Vector2.TransformNormal(new Vector2((control[xc] - .5f) * scale, (control[yc] - .5f) * scale), units);
                return SvgPixels.Sample(input, x + delta.X, y + delta.Y, linear);
            }, linear, context.Token);
        }

        private static int Channel(string name) => name switch
        {
            "R" => 0,
            "G" => 1,
            "B" => 2,
            _ => 3
        };
        private static PixelBuffer Tile(PixelBuffer input, (float X, float Y, float Width, float Height) region, Matrix3x2 matrix, bool linear, SvgRenderContext context)
        {
            if (region.Width <= 0 || region.Height <= 0 || !Matrix3x2.Invert(matrix, out Matrix3x2 inverse))
            {
                return new PixelBuffer(input.Width, input.Height);
            }

            return SvgPixels.Map(input, (x, y) =>
            {
                Vector2 local = Vector2.Transform(new Vector2(x + .5f, y + .5f), inverse);
                float Wrap(float v, float origin, float size) => origin + ((((v - origin) % size) + size) % size);
                local = new Vector2(Wrap(local.X, region.X, region.Width), Wrap(local.Y, region.Y, region.Height));
                Vector2 point = Vector2.Transform(local, matrix) - new Vector2(.5f);
                return SvgPixels.Sample(input, point.X, point.Y, linear);
            }, linear, context.Token);
        }
    }
}
