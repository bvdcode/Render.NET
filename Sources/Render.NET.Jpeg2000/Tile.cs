using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class Tile
    {
        internal TileComponent[] Components { get; init; } = null!;
        internal HeaderSettings Settings { get; init; } = null!;
        internal Packet[] Packets { get; init; } = null!;
        internal int PacketIndex { get; set; }
        internal int NextPart { get; set; }
    }
}
