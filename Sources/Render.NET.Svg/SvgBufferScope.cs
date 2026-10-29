using System;
using System.Collections.Generic;
using System.Linq;

namespace Render.NET.Svg
{
    internal class SvgBufferScope(SvgRenderContext context, SvgBufferScope? parent) : IDisposable
    {
        private readonly HashSet<PixelBuffer> _owned = new();

        internal void Add(PixelBuffer image) => _owned.Add(image);

        internal void Adopt(PixelBuffer image)
        {
            if (parent is not null && parent._owned.Remove(image))
            {
                _owned.Add(image);
            }
        }

        internal bool Owns(PixelBuffer image) => _owned.Contains(image);

        internal void Release(PixelBuffer image)
        {
            if (_owned.Remove(image))
            {
                context.Buffers.Return(image);
            }
        }

        internal void Retain(HashSet<PixelBuffer> images)
        {
            foreach (PixelBuffer image in _owned.Where(image => !images.Contains(image)).ToArray())
            {
                Release(image);
            }
        }

        internal PixelBuffer Transfer(PixelBuffer image)
        {
            if (_owned.Remove(image))
            {
                parent!.Add(image);
            }

            return image;
        }

        public void Dispose()
        {
            foreach (PixelBuffer image in _owned)
            {
                context.Buffers.Return(image);
            }

            _owned.Clear();
            context.Scope = parent;
        }
    }
}
