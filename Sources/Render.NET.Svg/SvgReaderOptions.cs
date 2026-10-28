using System;
using System.Collections.Generic;

namespace Render.NET.Svg
{
    /// <summary>Bounds for XML input and interpreted geometry. External entities/resources are disabled.</summary>
    public record SvgReaderOptions
    {
        /// <summary>Gets the maximum UTF-8 input length; defaults to 16 MiB.</summary>
        public int MaximumInputBytes { get; init; } = 16 * 1024 * 1024;
        /// <summary>Gets the maximum XML element and expanded-reference count.</summary>
        public int MaximumElements { get; init; } = 100_000;
        /// <summary>Gets the maximum stored path commands across expanded nodes.</summary>
        public int MaximumPathCommands { get; init; } = 1_000_000;
        /// <summary>Gets the maximum XML/reference nesting depth, up to 64.</summary>
        public int MaximumDepth { get; init; } = 64;
        /// <summary>Gets the decoded pixel budget per embedded or resolved image.</summary>
        public int MaximumImagePixels { get; init; } = 16_777_216;
        /// <summary>Gets an optional caller-controlled font resolver. Return TrueType/OpenType glyf font bytes, or empty to use the bundled CC0 fonts.</summary>
        public Func<SvgFontRequest, ReadOnlyMemory<byte>>? FontResolver { get; init; }
        /// <summary>Gets an optional caller-controlled image resolver. No network or file access is performed implicitly.</summary>
        public Func<string, PixelBuffer?>? ImageResolver { get; init; }
    }
}
