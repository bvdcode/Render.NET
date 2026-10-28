using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Render.NET.Jbig2
{
    internal record SymbolDictionary(Bitmap[] Symbols, byte[] Generic, byte[] Refinement, int Flags, (int, int)[] At, (int, int)[] RefAt);
}
