using System;
using System.Collections.Generic;

namespace Render.NET.Svg
{
    /// <summary>An interpreted SVG scene independent of its input stream. Safe for concurrent rendering.</summary>
    public class SvgDocument
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
}
