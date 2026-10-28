using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class Subband
    {
        internal int X0 { get; init; }
        internal int Y0 { get; init; }
        internal int Width { get; init; }
        internal int Height { get; init; }
        internal int Orientation { get; init; }
        internal int Planes { get; init; }
        internal double Step { get; init; }
        internal List<CodeBlock> Blocks { get; } = new();
    }
}
