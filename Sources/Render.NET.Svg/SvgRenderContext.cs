using System;
using System.Threading;

namespace Render.NET.Svg
{
    internal sealed class SvgRenderContext
    {
        private long _reserved;
        internal SvgRenderOptions Options { get; }
        internal CancellationToken Token { get; }

        internal SvgRenderContext(SvgRenderOptions options, CancellationToken token)
        {
            Options = options;
            Token = token;
        }

        internal IDisposable Reserve(int width, int height, int buffers)
        {
            long bytes = checked((long)width * height * 4 * buffers);
            if (bytes > Options.MaximumEffectBytes - _reserved)
            {
                throw new InvalidOperationException("SVG effects exceed MaximumEffectBytes.");
            }

            _reserved += bytes;
            return new Reservation(this, bytes);
        }

        private sealed class Reservation(SvgRenderContext owner, long bytes) : IDisposable
        {
            public void Dispose() => owner._reserved -= bytes;
        }
    }
}
