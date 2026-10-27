using System;
using System.Collections.Generic;

namespace Render.NET.Svg
{
    /// <summary>An interpreted SVG scene independent of its input stream. Safe for concurrent rendering.</summary>
    public sealed class SvgDocument
    {
        internal SvgNode Root { get; }
        internal string AspectRatio { get; }
        internal (float X, float Y, float Width, float Height) ViewBox { get; }
        /// <summary>Gets the intrinsic viewport width in CSS pixels (96 DPI).</summary>
        public float Width { get; }
        /// <summary>Gets the intrinsic viewport height in CSS pixels (96 DPI).</summary>
        public float Height { get; }
        /// <summary>Gets explicitly detected features that this renderer cannot paint.</summary>
        public IReadOnlyList<string> UnsupportedFeatures { get; }

        internal SvgDocument(SvgNode root, float width, float height, (float, float, float, float) box, string aspect, string[] warnings)
        {
            Root = root;
            Width = width;
            Height = height;
            ViewBox = box;
            AspectRatio = aspect;
            UnsupportedFeatures = System.Array.AsReadOnly(warnings);
        }
    }

    /// <summary>Bounds for XML input and interpreted geometry. External entities/resources are disabled.</summary>
    public sealed record SvgReaderOptions
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

    /// <summary>Raster output settings for an SVG viewport.</summary>
    public sealed record SvgRenderOptions
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

    /// <summary>A requested CSS font face.</summary>
    public sealed record SvgFontRequest(string Family, int Weight = 400, bool Italic = false);
}
