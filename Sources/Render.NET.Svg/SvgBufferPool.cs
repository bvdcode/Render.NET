using System;
using System.Collections.Generic;
using System.Linq;

namespace Render.NET.Svg
{
    internal class SvgBufferPool(SvgRenderContext context) : IDisposable
    {
        private readonly Dictionary<PixelBuffer, IDisposable> _buffers = new();
        private readonly HashSet<PixelBuffer> _available = new();

        internal PixelBuffer Rent(int width, int height)
        {
            PixelBuffer? image = _available.FirstOrDefault(buffer => buffer.Width == width && buffer.Height == height);
            if (image is not null)
            {
                _available.Remove(image);
                image.Pixels.Span.Clear();
                return image;
            }

            IDisposable reservation = context.Reserve(width, height, 1);
            image = new PixelBuffer(width, height);
            _buffers.Add(image, reservation);
            return image;
        }

        internal void Return(PixelBuffer image) => _available.Add(image);

        internal void Trim()
        {
            foreach (PixelBuffer image in _available)
            {
                _buffers[image].Dispose();
                _buffers.Remove(image);
            }

            _available.Clear();
        }

        public void Dispose()
        {
            foreach (IDisposable reservation in _buffers.Values)
            {
                reservation.Dispose();
            }

            _buffers.Clear();
            _available.Clear();
        }
    }
}
