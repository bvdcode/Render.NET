using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class CodeBlock
    {
        internal int X0 { get; init; }
        internal int Y0 { get; init; }
        internal int Width { get; init; }
        internal int Height { get; init; }
        internal int ZeroPlanes { get; set; }
        internal int Passes { get; set; }
        internal int LengthBits { get; set; } = 3;
        internal List<CodewordSegment> Segments { get; } = new();
    }
}
