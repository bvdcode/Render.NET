using System;
using System.Numerics;
using System.Threading;

namespace Render.NET.Pdf
{
    /// <summary>Rasterizes interpreted PDF pages on the CPU.</summary>
    public static class PdfRenderer
    {
        /// <summary>Renders the visible page, honoring its crop box and rotation.</summary>
        public static PixelBuffer Render(PdfPage page, PdfRenderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(page);
            options ??= new PdfRenderOptions();
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.AllowPartialRendering && page.UnsupportedFeatures.Count != 0)
            {
                throw new NotSupportedException("Page contains unsupported PDF features: " + string.Join("; ", page.UnsupportedFeatures));
            }

            (int width, int height) = Dimensions(page, options);
            int samples = options.Supersampling;
            var image = new PixelBuffer(width * samples, height * samples);
            image.Clear(options.Background);
            var canvas = new RasterCanvas(image);
            var transform = new Matrix3x2((float)(image.Width / page.Width), 0, 0, (float)(-image.Height / page.Height), 0, image.Height);
            foreach (IPdfDrawCommand command in page.Commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                command.Draw(canvas, transform, cancellationToken);
            }

            return samples == 1 ? image : Downsample(image, samples, cancellationToken);
        }

        private static (int Width, int Height) Dimensions(PdfPage page, PdfRenderOptions options)
        {
            if (!double.IsFinite(options.Dpi) || options.Dpi <= 0 || options.Dpi > 2400 || options.Supersampling < 1 || options.Supersampling > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            if (options.MaximumWidth <= 0 || options.MaximumHeight <= 0 || options.MaximumSamples <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            double scale = options.Dpi / 72;
            if (options.MaximumWidth is int maximumWidth)
            {
                scale = Math.Min(scale, maximumWidth / page.Width);
            }

            if (options.MaximumHeight is int maximumHeight)
            {
                scale = Math.Min(scale, maximumHeight / page.Height);
            }

            double width = Math.Max(1, Math.Ceiling(page.Width * scale));
            double height = Math.Max(1, Math.Ceiling(page.Height * scale));
            if (!double.IsFinite(width * height) || width * height * options.Supersampling * options.Supersampling > options.MaximumSamples)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "The rendered page exceeds MaximumSamples.");
            }

            return ((int)width, (int)height);
        }

        private static PixelBuffer Downsample(PixelBuffer input, int factor, CancellationToken cancellationToken)
        {
            var output = new PixelBuffer(input.Width / factor, input.Height / factor);
            for (int y = 0; y < output.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < output.Width; x++)
                {
                    output[x, y] = Average(input, x * factor, y * factor, factor);
                }
            }

            return output;
        }

        private static Rgba32 Average(PixelBuffer input, int x, int y, int factor)
        {
            int r = 0;
            int g = 0;
            int b = 0;
            int alpha = 0;
            for (int dy = 0; dy < factor; dy++)
            {
                for (int dx = 0; dx < factor; dx++)
                {
                    Rgba32 color = input[x + dx, y + dy];
                    alpha += color.A;
                    r += color.R * color.A;
                    g += color.G * color.A;
                    b += color.B * color.A;
                }
            }

            return alpha == 0 ? default : new Rgba32((byte)(r / alpha), (byte)(g / alpha), (byte)(b / alpha), (byte)(alpha / (factor * factor)));
        }
    }
}
