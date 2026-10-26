using System;

namespace Render.NET
{
    /// <summary>A row-major RGBA8 pixel buffer using sRGB color and straight alpha.</summary>
    public class PixelBuffer
    {
        private readonly byte[] _pixels;

        /// <summary>Gets the image width in pixels.</summary>
        public int Width { get; }

        /// <summary>Gets the image height in pixels.</summary>
        public int Height { get; }

        /// <summary>Gets writable pixel memory in R, G, B, A channel order.</summary>
        public Memory<byte> Pixels => _pixels;

        /// <summary>Creates a transparent black image.</summary>
        public PixelBuffer(int width, int height)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            long pixelCount = (long)width * height;
            if (pixelCount > Array.MaxLength / 4)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The pixel buffer exceeds the supported array size.");
            }

            Width = width;
            Height = height;
            _pixels = new byte[(int)pixelCount * 4];
        }

        /// <summary>Gets or sets a pixel using zero-based coordinates.</summary>
        public Rgba32 this[int x, int y]
        {
            get
            {
                int offset = GetOffset(x, y);
                return new Rgba32(_pixels[offset], _pixels[offset + 1], _pixels[offset + 2], _pixels[offset + 3]);
            }
            set
            {
                int offset = GetOffset(x, y);
                _pixels[offset] = value.R;
                _pixels[offset + 1] = value.G;
                _pixels[offset + 2] = value.B;
                _pixels[offset + 3] = value.A;
            }
        }

        /// <summary>Fills the image with a color.</summary>
        public void Clear(Rgba32 color)
        {
            for (int offset = 0; offset < _pixels.Length; offset += 4)
            {
                _pixels[offset] = color.R;
                _pixels[offset + 1] = color.G;
                _pixels[offset + 2] = color.B;
                _pixels[offset + 3] = color.A;
            }
        }

        private int GetOffset(int x, int y)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(x);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
            ArgumentOutOfRangeException.ThrowIfNegative(y);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
            return ((y * Width) + x) * 4;
        }
    }
}
