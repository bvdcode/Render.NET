using System;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class ContainerInfo
    {
        internal Jpeg2000ColorSpace ColorSpace { get; set; }
        internal int Alpha { get; set; } = -1;
        internal bool Premultiplied { get; set; }
        internal int Width { get; set; }
        internal int Height { get; set; }
        internal int Components { get; set; }
        internal bool Profile { get; set; }
    }
}
