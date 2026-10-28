using System;
using System.Collections.Generic;

namespace Render.NET.Svg
{
    /// <summary>Raster output settings for an SVG viewport.</summary>
    public record SvgRenderOptions
    {
        /// <summary>Gets output width; defaults to the intrinsic SVG width.</summary>
        public int? Width { get; init; }
        /// <summary>Gets output height; defaults to the intrinsic SVG height. One supplied dimension preserves intrinsic proportions.</summary>
        public int? Height { get; init; }
        /// <summary>Gets a supersampling factor from 1 to 4; defaults to 2.</summary>
        public int Supersampling { get; init; } = 2;
        /// <summary>Gets the output background; defaults to transparent.</summary>
        public Rgba32 Background { get; init; }
        /// <summary>Gets whether to permit rendering with known unsupported features.</summary>
        public bool AllowPartialRendering { get; init; }
        /// <summary>Gets the output sample budget, including supersampling.</summary>
        public int MaximumSamples { get; init; } = 16_777_216;
        /// <summary>Gets the live temporary buffer budget for nested masks and filters, default 256 MiB.</summary>
        public long MaximumEffectBytes { get; init; } = 256L * 1024 * 1024;
    }
}
