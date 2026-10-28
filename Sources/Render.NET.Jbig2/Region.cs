using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Render.NET.Jbig2
{
    internal record Region(int Width, int Height, int X, int Y, int Op);
}
