using System;
using System.Threading;

namespace Render.NET
{
    public partial class PixelBuffer
    {
        /// <summary>Downsamples by an integer factor using premultiplied-alpha averaging.</summary>
        public PixelBuffer Downsample(int factor, CancellationToken cancellationToken = default)
        {
            if (factor < 1 || factor > 16 || Width % factor != 0 || Height % factor != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(factor));
            }

            var output = new PixelBuffer(Width / factor, Height / factor);
            for (int y = 0; y < output.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < output.Width; x++)
                {
                    output[x, y] = Average(this, x * factor, y * factor, factor);
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
