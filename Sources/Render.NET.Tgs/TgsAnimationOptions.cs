using System;

namespace Render.NET.Tgs
{
    /// <summary>Timing and output frame limits for sequential animation rendering.</summary>
    public record TgsAnimationOptions
    {
        /// <summary>Gets the start time relative to the composition's in-point.</summary>
        public TimeSpan StartTime { get; init; }
        /// <summary>Gets the duration to render; null uses the rest of the composition.</summary>
        public TimeSpan? Duration { get; init; }
        /// <summary>Gets the output frame rate; null uses the source frame rate.</summary>
        public double? FrameRate { get; init; }
        /// <summary>Gets the maximum output frame count.</summary>
        public int MaximumFrames { get; init; } = 10_000;
    }
}
