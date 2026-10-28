using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Render.NET.Jbig2
{
    internal record SymbolSettings(int Flags, (int, int)[] At, (int, int)[] RefAt, int Exported, int Added, byte[] Generic, byte[] Refinement)
    {
        internal int Template => (Flags >> 10) & 3;
        internal int RefTemplate => (Flags >> 12) & 1;
        internal bool Huffman => (Flags & 1) != 0;
        internal bool Aggregate => (Flags & 2) != 0;
        internal int IdBits { get; set; }
        internal HuffmanTable[] Tables { get; set; } = Array.Empty<HuffmanTable>();
    }
}
