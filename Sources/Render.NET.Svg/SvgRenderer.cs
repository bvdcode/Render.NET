using System;
using System.Numerics;
using System.Threading;

namespace Render.NET.Svg
{
    /// <summary>Renders an interpreted SVG viewport with the managed CPU raster canvas.</summary>
    public static class SvgRenderer
    {
        /// <summary>Renders an SVG to RGBA pixels. Unsupported features require explicit partial-render opt-in.</summary>
        public static PixelBuffer Render(SvgDocument document, SvgRenderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new SvgRenderOptions();
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.AllowPartialRendering && document.UnsupportedFeatures.Count != 0)
            {
                throw new NotSupportedException("Unsupported SVG features: " + string.Join("; ", document.UnsupportedFeatures));
            }

            var(width, height) = Dimensions(document, options);
            int factor = options.Supersampling;
            var image = new PixelBuffer(width * factor, height * factor);
            image.Clear(options.Background);
            Matrix3x2 matrix = Viewport(document.ViewBox, width, height, document.AspectRatio) * Matrix3x2.CreateScale(factor);
            document.Root.Draw(new RasterCanvas(image), matrix, new SvgRenderContext(options, cancellationToken));
            return factor == 1 ? image : image.Downsample(factor, cancellationToken);
        }

        private static (int Width, int Height) Dimensions(SvgDocument doc, SvgRenderOptions options)
        {
            if (options.Width <= 0 || options.Height <= 0 || options.Supersampling < 1 || options.Supersampling > 4 || options.MaximumSamples <= 0 || options.MaximumEffectBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            double width = options.Width ?? Math.Ceiling(options.Height.HasValue ? (options.Height.Value * (double)doc.Width) / doc.Height : doc.Width);
            double height = options.Height ?? Math.Ceiling(options.Width.HasValue ? (options.Width.Value * (double)doc.Height) / doc.Width : doc.Height);
            if (!double.IsFinite(width * height) || width * height * options.Supersampling * options.Supersampling > options.MaximumSamples)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "SVG output exceeds MaximumSamples.");
            }

            return ((int)Math.Max(1, width), (int)Math.Max(1, height));
        }

        internal static Matrix3x2 Viewport((float X, float Y, float Width, float Height) box, float width, float height, string aspect)
        {
            float sx = width / box.Width, sy = height / box.Height;
            string[] parts = aspect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string align = parts.Length == 0 ? "xMidYMid" : parts[0];
            if (align == "defer")
            {
                align = parts.Length > 1 ? parts[1] : "xMidYMid";
            }

            if (align == "none")
            {
                return Matrix3x2.CreateTranslation(-box.X, -box.Y) * Matrix3x2.CreateScale(sx, sy);
            }

            float scale = aspect.Contains("slice", StringComparison.Ordinal) ? Math.Max(sx, sy) : Math.Min(sx, sy);
            float x = align.Contains("xMax", StringComparison.Ordinal) ? 1 : align.Contains("xMin", StringComparison.Ordinal) ? 0 : 0.5f;
            float y = align.Contains("YMax", StringComparison.Ordinal) ? 1 : align.Contains("YMin", StringComparison.Ordinal) ? 0 : 0.5f;
            return Matrix3x2.CreateTranslation(-box.X, -box.Y) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation((width - (box.Width * scale)) * x, (height - (box.Height * scale)) * y);
        }
    }
}
