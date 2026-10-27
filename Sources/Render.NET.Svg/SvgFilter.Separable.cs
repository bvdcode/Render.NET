using System;
using System.Numerics;
using System.Threading.Tasks;

namespace Render.NET.Svg
{
    internal sealed partial record SvgFilter
    {
        private static PixelBuffer GaussianAligned(PixelBuffer input, float[] kernel, bool horizontal, bool reverse, bool linear, string edge, SvgRenderContext context)
        {
            int length = horizontal ? input.Width : input.Height;
            int lines = horizontal ? input.Height : input.Width;
            int radius = kernel.Length / 2;
            int workers = Math.Min(Environment.ProcessorCount, lines);
            int padded = checked(length + (2 * radius));
            using IDisposable reservation = context.Reserve(padded, workers, 4);
            var result = new PixelBuffer(input.Width, input.Height);
            Parallel.For(0, lines, new ParallelOptions { CancellationToken = context.Token, MaxDegreeOfParallelism = workers },
                () => new Vector4[padded],
                (line, _, samples) =>
                {
                    var (first, last) = ReadAxisLine(input, samples, line, radius, horizontal, reverse, linear, edge, context);
                    int start = Math.Max(0, first - (2 * radius)), end = Math.Min(length - 1, last);
                    for (int x = start; x <= end; x++)
                    {
                        Vector4 sum = Vector4.Zero;
                        for (int i = 0; i < kernel.Length; i++)
                        {
                            sum += samples[x + i] * kernel[i];
                        }

                        WriteAxisPixel(result, x, line, horizontal, reverse, sum, linear);
                    }

                    return samples;
                }, _ => { });
            return result;
        }

        private static PixelBuffer MorphAligned(PixelBuffer input, int radius, bool horizontal, bool reverse, bool dilate, bool linear, SvgRenderContext context)
        {
            int length = horizontal ? input.Width : input.Height;
            int lines = horizontal ? input.Height : input.Width;
            int window = (radius * 2) + 1;
            int padded = checked(length + (2 * radius));
            int workers = Math.Min(Environment.ProcessorCount, lines);
            using IDisposable reservation = context.Reserve(padded, workers, 12);
            var result = new PixelBuffer(input.Width, input.Height);
            Parallel.For(0, lines, new ParallelOptions { CancellationToken = context.Token, MaxDegreeOfParallelism = workers },
                () => (Samples: new Vector4[padded], Prefix: new Vector4[padded], Suffix: new Vector4[padded]),
                (line, _, scratch) =>
                {
                    ReadAxisLine(input, scratch.Samples, line, radius, horizontal, reverse, linear, "none", context);
                    for (int i = 0; i < padded; i++)
                    {
                        scratch.Prefix[i] = i % window == 0 ? scratch.Samples[i] : Extremum(scratch.Prefix[i - 1], scratch.Samples[i], dilate);
                        int j = padded - 1 - i;
                        scratch.Suffix[j] = j == padded - 1 || (j + 1) % window == 0 ? scratch.Samples[j] : Extremum(scratch.Suffix[j + 1], scratch.Samples[j], dilate);
                    }

                    for (int x = 0; x < length; x++)
                    {
                        Vector4 value = Extremum(scratch.Suffix[x], scratch.Prefix[x + window - 1], dilate);
                        WriteAxisPixel(result, x, line, horizontal, reverse, value, linear);
                    }

                    return scratch;
                }, _ => { });
            return result;
        }

        private static Vector4 Extremum(Vector4 a, Vector4 b, bool dilate) => dilate ? Vector4.Max(a, b) : Vector4.Min(a, b);

        private static (int First, int Last) ReadAxisLine(PixelBuffer input, Vector4[] samples, int line, int radius, bool horizontal, bool reverse, bool linear, string edge, SvgRenderContext context)
        {
            context.Token.ThrowIfCancellationRequested();
            int length = horizontal ? input.Width : input.Height;
            int first = samples.Length, last = -1;
            for (int i = 0; i < samples.Length; i++)
            {
                int coordinate = reverse ? length - 1 - (i - radius) : i - radius;
                Vector4 value = horizontal ? SvgPixels.At(input, coordinate, line, linear, edge) : SvgPixels.At(input, line, coordinate, linear, edge);
                samples[i] = value;
                if (value != Vector4.Zero)
                {
                    first = Math.Min(first, i);
                    last = i;
                }
            }

            return (first, last);
        }

        private static void WriteAxisPixel(PixelBuffer output, int coordinate, int line, bool horizontal, bool reverse, Vector4 value, bool linear)
        {
            int length = horizontal ? output.Width : output.Height;
            coordinate = reverse ? length - 1 - coordinate : coordinate;
            int x = horizontal ? coordinate : line, y = horizontal ? line : coordinate;
            SvgPixels.Write(output.Pixels.Span, ((y * output.Width) + x) * 4, value, linear);
        }
    }
}
