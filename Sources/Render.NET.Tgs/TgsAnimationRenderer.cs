using System;
using System.Collections.Generic;
using System.Threading;
using Render.NET.Svg;

namespace Render.NET.Tgs
{
    public static partial class TgsRenderer
    {
        /// <summary>Renders frames lazily in timestamp order, suitable for streaming into an encoder.</summary>
        public static IEnumerable<TgsFrame> RenderFrames(TgsDocument document, TgsAnimationOptions? animation = null, SvgRenderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            animation ??= new TgsAnimationOptions();
            double rate = animation.FrameRate ?? document.FrameRate;
            double start = animation.StartTime.TotalSeconds;
            double duration = animation.Duration?.TotalSeconds ?? document.Duration - start;
            double count = Math.Ceiling((duration * rate) - 1e-9);
            if (!double.IsFinite(rate) || rate <= 0 || start < 0 || start >= document.Duration || duration <= 0 || start + duration > document.Duration + 1e-9 || animation.MaximumFrames <= 0 || !double.IsFinite(count) || count > animation.MaximumFrames)
            {
                throw new ArgumentOutOfRangeException(nameof(animation));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Frames(document, options, start, duration, rate, (int)Math.Max(1, count), cancellationToken);
        }

        private static IEnumerable<TgsFrame> Frames(TgsDocument document, SvgRenderOptions? options, double start, double duration, double rate, int count, CancellationToken token)
        {
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                double elapsed = i / rate;
                double source = document.InPoint + ((start + elapsed) * document.FrameRate);
                TgsFrame frame = RenderFrameDetailed(document, source, options, token);
                yield return frame with { Duration = TimeSpan.FromTicks(checked((long)Math.Round(Math.Min(1 / rate, duration - elapsed) * TimeSpan.TicksPerSecond))) };
            }
        }
    }
}
