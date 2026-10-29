using System;
using System.Collections.Generic;

namespace Render.NET.Tgs
{
    /// <summary>A rendered animation frame with composition timing and feature diagnostics.</summary>
    public record TgsFrame
    {
        /// <summary>Gets the absolute frame number in the source composition.</summary>
        public required double SourceFrame { get; init; }
        /// <summary>Gets the elapsed time from the source composition's in-point.</summary>
        public required TimeSpan Timestamp { get; init; }
        /// <summary>Gets the display duration of this output frame.</summary>
        public required TimeSpan Duration { get; init; }
        /// <summary>Gets the rendered RGBA image.</summary>
        public required PixelBuffer Image { get; init; }
        /// <summary>Gets detected unsupported features when partial rendering is enabled.</summary>
        public required IReadOnlyList<string> Warnings { get; init; }
    }
}
