using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jbig2
{
    internal record HuffmanRow(int Length, int Range, int Base, bool Lower = false);
}
