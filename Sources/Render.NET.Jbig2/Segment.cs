using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Render.NET.Jbig2
{
    internal record Segment(uint Number, int Type, uint Page, uint[] References, int Length)
    {
        internal ReadOnlyMemory<byte> Data { get; set; }
    }
}
